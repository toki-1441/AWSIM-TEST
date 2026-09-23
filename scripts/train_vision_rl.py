#!/usr/bin/env python3
"""Single-environment visual REINFORCE trainer for ``PythonRlBridge``.

The Unity bridge sends newline-delimited JSON containing its relative observation
and, when a rendered frame is available, a base64 JPEG.  This deliberately small
trainer is a runnable visual baseline: Unity remains responsible for low-level
actuation while the policy chooses desired speed and lateral offset.
"""
import argparse
import base64
from io import BytesIO
import json
import math
import os
from pathlib import Path
import socketserver
import tempfile

import numpy as np
from PIL import Image
import torch
from torch import nn
from torch.distributions import Normal


IMAGE_WIDTH, IMAGE_HEIGHT = 160, 90


def image_tensor(encoded, device):
    """Decode a bridge JPEG, or supply a zero frame before the first render."""
    if not encoded:
        return torch.zeros((1, 3, IMAGE_HEIGHT, IMAGE_WIDTH), device=device)
    image = Image.open(BytesIO(base64.b64decode(encoded))).convert("RGB")
    image = image.resize((IMAGE_WIDTH, IMAGE_HEIGHT))
    pixels = np.array(image, dtype=np.float32, copy=True) / 255.0
    return torch.from_numpy(pixels).permute(2, 0, 1).unsqueeze(0).to(device)


class VisionPolicy(nn.Module):
    def __init__(self, vector_width):
        super().__init__()
        self.encoder = nn.Sequential(
            nn.Conv2d(3, 8, kernel_size=5, stride=2), nn.ReLU(),
            nn.Conv2d(8, 16, kernel_size=3, stride=2), nn.ReLU(),
            nn.AdaptiveAvgPool2d((1, 1)), nn.Flatten(),
        )
        self.head = nn.Sequential(
            nn.Linear(16 + vector_width, 64), nn.ReLU(), nn.Linear(64, 2),
        )
        self.log_std = nn.Parameter(torch.full((2,), -1.2))

    def forward(self, image, vector):
        mean = torch.tanh(self.head(torch.cat((self.encoder(image), vector), dim=1)))
        return mean, self.log_std.exp().expand_as(mean)


class VisionReinforceTrainer:
    def __init__(self, learning_rate, discount, device, entropy_coefficient=0.001, baseline_decay=0.95,
                 initialize_from=None, resume=None):
        if initialize_from and resume:
            raise ValueError("Choose weight initialization OR full training resume, not both")
        self.learning_rate, self.discount, self.device = learning_rate, discount, device
        self.entropy_coefficient = entropy_coefficient
        self.baseline_decay = baseline_decay
        self.policy = self.optimizer = None
        self.rollout, self.episode = [], 0
        # Keep statistics across episodes.  Normalising returns *within* one
        # episode makes a terminal timeout penalty largely disappear because it
        # is shared by every action in that episode.
        self.return_baseline = 0.0
        self.return_scale = 1.0
        self.initialize_from = Path(initialize_from) if initialize_from else None
        self.environment_contract = None
        self._resume = None
        if resume:
            self._resume = torch.load(resume, map_location=device, weights_only=True)
            if self._resume.get("training_schema") != 2 or not self._resume.get("environment_contract"):
                raise ValueError("Resume requires an operator checkpoint with optimizer and environment state; use --initialize-from for weights only")
            self.environment_contract = self._resume["environment_contract"]
            self.episode = self._resume["completed_episodes"]
            self.return_baseline, self.return_scale = self._resume["return_baseline"], self._resume["return_scale"]
            settings = self._resume["training_settings"]
            self.learning_rate, self.discount = settings["learning_rate"], settings["discount"]
            self.entropy_coefficient, self.baseline_decay = settings["entropy_coefficient"], settings["baseline_decay"]

    def bind_environment(self, contract):
        if not isinstance(contract, dict) or contract.get("schemaVersion") != 1:
            raise ValueError("Unsupported operator environment contract")
        if self.environment_contract is not None and self.environment_contract != contract:
            raise ValueError("Environment/settings differ from the checkpoint; resume rejected")
        self.environment_contract = contract

    def _ensure_policy(self, observation):
        if self.policy is None:
            self.policy = VisionPolicy(len(observation)).to(self.device)
            if self.initialize_from:
                checkpoint = torch.load(self.initialize_from, map_location=self.device, weights_only=True)
                if checkpoint.get("vector_width") != len(observation):
                    raise ValueError(
                        f"Initial checkpoint expects {checkpoint.get('vector_width')} observation values, "
                        f"but Unity supplied {len(observation)}")
                self.policy.load_state_dict(checkpoint["model_state"])
                print(f"initialised policy from {self.initialize_from}", flush=True)
            self.optimizer = torch.optim.Adam(self.policy.parameters(), lr=self.learning_rate)
            if self._resume:
                if self._resume["vector_width"] != len(observation):
                    raise ValueError("Resume observation layout does not match the environment")
                self.policy.load_state_dict(self._resume["model_state"])
                self.optimizer.load_state_dict(self._resume["optimizer_state"])
                # Network construction consumes random values; restore sampling state AFTER construction.
                torch.set_rng_state(self._resume["torch_rng_state"].cpu())
                if self.device.type == "cuda" and self._resume.get("cuda_rng_states"):
                    torch.cuda.set_rng_state_all([state.cpu() for state in self._resume["cuda_rng_states"]])
                self._resume = None
        if self.policy.head[0].in_features != 16 + len(observation):
            raise ValueError("Observation layout changed during training")

    def act(self, observation, encoded_camera):
        self._ensure_policy(observation)
        vector = torch.tensor(observation, dtype=torch.float32, device=self.device).clamp(-10, 10).div(10).unsqueeze(0)
        image = image_tensor(encoded_camera, self.device)
        mean, stddev = self.policy(image, vector)
        distribution = Normal(mean, stddev)
        # REINFORCE needs a score-function sample.  ``rsample`` would let the
        # sample path cancel the mean gradient in ``log_prob``.
        raw_action = distribution.sample()
        action = raw_action.clamp(-1, 1)
        self.rollout.append({
            "log_probability": distribution.log_prob(raw_action).sum(),
            "entropy": distribution.entropy().sum(),
            "reward": 0.0,
        })
        safe_speed = observation[1] if len(observation) > 1 else 3.0
        safe_speed = safe_speed if np.isfinite(safe_speed) else 3.0
        return {
            "desiredSpeed": max(0.0, float(safe_speed) * (float(action[0, 0]) + 1.0) * 0.5),
            "desiredLateralOffsetNormalized": float(action[0, 1]),
        }

    def reward_previous(self, reward):
        if self.rollout:
            self.rollout[-1]["reward"] += float(reward)

    def discounted_returns(self, rewards):
        returns, running = [], 0.0
        for reward in reversed(rewards):
            running = reward + self.discount * running
            returns.append(running)
        returns.reverse()
        return returns

    def finish_episode(self, terminal_reward):
        self.reward_previous(terminal_reward)
        rewards = [entry["reward"] for entry in self.rollout]
        returns = self.discounted_returns(rewards)
        total_reward = sum(rewards)
        if returns:
            returns = torch.tensor(returns, dtype=torch.float32, device=self.device)
            advantages = (returns - self.return_baseline) / max(0.1, self.return_scale)
            policy_loss = torch.stack([-entry["log_probability"] * advantage for entry, advantage in zip(self.rollout, advantages)]).mean()
            entropy_bonus = torch.stack([entry["entropy"] for entry in self.rollout]).mean()
            loss = policy_loss - self.entropy_coefficient * entropy_bonus
            self.optimizer.zero_grad()
            loss.backward()
            torch.nn.utils.clip_grad_norm_(self.policy.parameters(), 1.0)
            self.optimizer.step()
            return_mean = float(returns.mean().item())
            return_deviation = float(returns.std(unbiased=False).item())
            self.return_baseline = self.baseline_decay * self.return_baseline + (1.0 - self.baseline_decay) * return_mean
            self.return_scale = self.baseline_decay * self.return_scale + (1.0 - self.baseline_decay) * max(0.1, return_deviation)
        self.rollout.clear()
        self.episode += 1
        return {"episode": self.episode, "return": total_reward, "steps": len(rewards),
                "returnBaseline": self.return_baseline, "returnScale": self.return_scale}

    def save(self, path):
        if self.policy is None:
            return
        path.parent.mkdir(parents=True, exist_ok=True)
        checkpoint = {
            "training_schema": 2, "architecture": "legacy", "vector_width": self.policy.head[0].in_features - 16,
            "model_state": self.policy.state_dict(), "optimizer_state": self.optimizer.state_dict(),
            "completed_episodes": self.episode, "return_baseline": self.return_baseline, "return_scale": self.return_scale,
            "environment_contract": self.environment_contract,
            "training_settings": {"learning_rate": self.learning_rate, "discount": self.discount,
                                  "entropy_coefficient": self.entropy_coefficient, "baseline_decay": self.baseline_decay},
            "torch_rng_state": torch.get_rng_state(),
            "cuda_rng_states": torch.cuda.get_rng_state_all() if self.device.type == "cuda" else [],
            "resume_boundary": "between episodes; operator-stop truncates and updates the current rollout",
        }
        # Keep the preceding complete checkpoint recoverable if writing is interrupted.
        descriptor, temporary = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp", dir=path.parent)
        os.close(descriptor)
        try:
            torch.save(checkpoint, temporary)
            os.replace(temporary, path)
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)


class TrainerServer(socketserver.TCPServer):
    allow_reuse_address = True

    def __init__(self, address, handler, trainer, checkpoint, metrics, require_contract=False):
        super().__init__(address, handler)
        self.trainer, self.checkpoint, self.metrics = trainer, checkpoint, metrics
        self.require_contract = require_contract

    def record(self, metric):
        self.metrics.parent.mkdir(parents=True, exist_ok=True)
        with self.metrics.open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(metric) + "\n")


class Handler(socketserver.StreamRequestHandler):
    def handle(self):
        print(f"client connected: {self.client_address[0]}:{self.client_address[1]}", flush=True)
        bound = False
        episode_context = None
        try:
            for raw in self.rfile:
                request = json.loads(raw)
                kind = request.get("type")
                if kind == "hello":
                    self.server.trainer.bind_environment(request.get("environment"))
                    bound = True
                    self.respond({"ack": True, "completedEpisodes": self.server.trainer.episode})
                    continue
                if self.server.require_contract and not bound:
                    raise ValueError("Environment handshake required before training")
                if kind == "step":
                    if self.server.require_contract and not request.get("cameraJpegBase64"):
                        raise ValueError("Visual training requires an actual camera frame")
                    if self.server.require_contract:
                        episode_id, frame_id = request.get("episodeId"), request.get("cameraFrameIndex")
                        timestamp, camera_time = request.get("simulationTime"), request.get("cameraSimulationTime")
                        if not isinstance(episode_id, int) or episode_id < 0 or not isinstance(frame_id, int) or frame_id < 1:
                            raise ValueError("Missing episode/camera frame identity")
                        if not all(isinstance(value, (int, float)) and math.isfinite(value) for value in (timestamp, camera_time)) or not 0 <= timestamp - camera_time <= 1 / self.server.trainer.environment_contract["cameraHz"] + 0.02:
                            raise ValueError("Camera observation is not synchronized with the RL step")
                        if episode_context is None:
                            episode_context = {"episodeId": episode_id, "firstCameraFrame": frame_id, "firstSimulationTime": timestamp}
                        elif episode_id != episode_context["episodeId"]:
                            raise ValueError("Episode changed without a terminal transition")
                        episode_context.update(lastCameraFrame=frame_id, lastSimulationTime=timestamp)
                    self.server.trainer.reward_previous(request.get("reward", 0.0))
                    action = self.server.trainer.act(request.get("observation", []), request.get("cameraJpegBase64"))
                    self.respond(action)
                elif kind in ("terminal", "stop"):
                    if self.server.trainer.rollout:
                        metric = self.server.trainer.finish_episode(request.get("reward", 0.0))
                        metric.update(episode_context or {})
                        episode_context = None
                        metric["reason"] = request.get("reason") or ("operator-stop" if kind == "stop" else "terminal")
                        self.server.trainer.save(self.server.checkpoint)
                        self.server.record(metric)
                        print(f"episode {metric['episode']}: return={metric['return']:.3f}, steps={metric['steps']}", flush=True)
                    self.respond({"ack": True, "completedEpisodes": self.server.trainer.episode})
                    if kind == "stop":
                        return
                else:
                    raise ValueError("Unsupported RL request type")
        except (ValueError, KeyError, RuntimeError) as error:
            self.respond({"error": str(error)})
            print(f"RL session rejected: {error}", flush=True)
        except (ConnectionError, OSError) as error:
            print(f"RL connection lost: {error}", flush=True)
        finally:
            if self.server.trainer.rollout:
                self.server.record({"event": "connection-lost", "discardedSteps": len(self.server.trainer.rollout),
                                    "completedEpisodes": self.server.trainer.episode})
                self.server.trainer.rollout.clear()

    def respond(self, value):
        self.wfile.write((json.dumps(value, allow_nan=False) + "\n").encode("utf-8"))
        self.wfile.flush()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=5015)
    parser.add_argument("--output", type=Path, default=Path("Build/physical-ai-vision-reinforce.pt"))
    parser.add_argument("--metrics", type=Path, default=Path("Build/physical-ai-vision-reinforce-metrics.jsonl"))
    parser.add_argument("--learning-rate", type=float, default=0.0003)
    parser.add_argument("--discount", type=float, default=0.99)
    parser.add_argument("--entropy-coefficient", type=float, default=0.001)
    parser.add_argument("--initialize-from", type=Path,
                        help="compatible visual-BC or REINFORCE checkpoint used as the initial policy")
    parser.add_argument("--resume", type=Path, help="resume optimizer, statistics and RNG at an episode boundary")
    parser.add_argument("--require-contract", action="store_true", help="require operator handshake and actual RGB frames")
    parser.add_argument("--architecture", choices=("legacy",), default="legacy")
    parser.add_argument("--threads", type=int, default=1, help="CPU threads for this single-environment baseline")
    parser.add_argument("--seed", type=int, default=1)
    parser.add_argument("--device", default="auto", choices=("auto", "cpu", "cuda"))
    args = parser.parse_args()
    if args.output.exists() or args.metrics.exists():
        parser.error("Choose new checkpoint/metrics output paths; existing runs are never overwritten at startup")
    if args.resume and args.initialize_from:
        parser.error("--resume and --initialize-from are mutually exclusive")
    if args.threads < 1:
        parser.error("--threads must be positive")
    torch.set_num_threads(args.threads)
    torch.manual_seed(args.seed)
    if args.device == "cuda" and not torch.cuda.is_available():
        raise SystemExit("--device cuda was requested but CUDA is unavailable")
    device = torch.device("cuda" if args.device == "auto" and torch.cuda.is_available() else args.device if args.device != "auto" else "cpu")
    trainer = VisionReinforceTrainer(args.learning_rate, args.discount, device, args.entropy_coefficient,
                                     initialize_from=args.initialize_from, resume=args.resume)
    with TrainerServer(("127.0.0.1", args.port), Handler, trainer, args.output, args.metrics, args.require_contract) as server:
        print(json.dumps({"type": "ready", "port": args.port, "completedEpisodes": trainer.episode}), flush=True)
        print(f"Visual REINFORCE trainer on 127.0.0.1:{args.port} ({device}); checkpoints -> {args.output}", flush=True)
        server.serve_forever()


if __name__ == "__main__":
    main()
