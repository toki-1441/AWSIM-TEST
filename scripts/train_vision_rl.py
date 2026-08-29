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
from pathlib import Path
import socketserver

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
    def __init__(self, learning_rate, discount, device):
        self.learning_rate, self.discount, self.device = learning_rate, discount, device
        self.policy = self.optimizer = None
        self.rollout, self.episode = [], 0

    def _ensure_policy(self, observation):
        if self.policy is None:
            self.policy = VisionPolicy(len(observation)).to(self.device)
            self.optimizer = torch.optim.Adam(self.policy.parameters(), lr=self.learning_rate)
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
        self.rollout.append({"log_probability": distribution.log_prob(raw_action).sum(), "reward": 0.0})
        safe_speed = observation[1] if len(observation) > 1 else 3.0
        safe_speed = safe_speed if np.isfinite(safe_speed) else 3.0
        return {
            "desiredSpeed": max(0.0, float(safe_speed) * (float(action[0, 0]) + 1.0) * 0.5),
            "desiredLateralOffsetNormalized": float(action[0, 1]),
        }

    def reward_previous(self, reward):
        if self.rollout:
            self.rollout[-1]["reward"] += float(reward)

    def finish_episode(self, terminal_reward):
        self.reward_previous(terminal_reward)
        rewards = [entry["reward"] for entry in self.rollout]
        returns, running = [], 0.0
        for reward in reversed(rewards):
            running = reward + self.discount * running
            returns.append(running)
        returns.reverse()
        total_reward = sum(rewards)
        if returns:
            returns = torch.tensor(returns, dtype=torch.float32, device=self.device)
            returns = (returns - returns.mean()) / (returns.std(unbiased=False) + 1e-6)
            loss = torch.stack([-entry["log_probability"] * advantage for entry, advantage in zip(self.rollout, returns)]).sum()
            self.optimizer.zero_grad()
            loss.backward()
            torch.nn.utils.clip_grad_norm_(self.policy.parameters(), 1.0)
            self.optimizer.step()
        self.rollout.clear()
        self.episode += 1
        return {"episode": self.episode, "return": total_reward, "steps": len(rewards)}

    def save(self, path):
        if self.policy is None:
            return
        path.parent.mkdir(parents=True, exist_ok=True)
        torch.save({"vector_width": self.policy.head[0].in_features - 16, "model_state": self.policy.state_dict()}, path)


class TrainerServer(socketserver.TCPServer):
    allow_reuse_address = True

    def __init__(self, address, handler, trainer, checkpoint, metrics):
        super().__init__(address, handler)
        self.trainer, self.checkpoint, self.metrics = trainer, checkpoint, metrics

    def record(self, metric):
        self.metrics.parent.mkdir(parents=True, exist_ok=True)
        with self.metrics.open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(metric) + "\n")


class Handler(socketserver.StreamRequestHandler):
    def handle(self):
        for raw in self.rfile:
            request = json.loads(raw)
            if request.get("type") == "step":
                self.server.trainer.reward_previous(request.get("reward", 0.0))
                action = self.server.trainer.act(request.get("observation", []), request.get("cameraJpegBase64"))
                self.wfile.write((json.dumps(action) + "\n").encode("utf-8"))
            elif request.get("type") == "terminal":
                metric = self.server.trainer.finish_episode(request.get("reward", 0.0))
                self.server.trainer.save(self.server.checkpoint)
                self.server.record(metric)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=5015)
    parser.add_argument("--output", type=Path, default=Path("Build/physical-ai-vision-reinforce.pt"))
    parser.add_argument("--metrics", type=Path, default=Path("Build/physical-ai-vision-reinforce-metrics.jsonl"))
    parser.add_argument("--learning-rate", type=float, default=0.0003)
    parser.add_argument("--discount", type=float, default=0.99)
    parser.add_argument("--device", default="auto", choices=("auto", "cpu", "cuda"))
    args = parser.parse_args()
    if args.device == "cuda" and not torch.cuda.is_available():
        raise SystemExit("--device cuda was requested but CUDA is unavailable")
    device = torch.device("cuda" if args.device == "auto" and torch.cuda.is_available() else args.device if args.device != "auto" else "cpu")
    trainer = VisionReinforceTrainer(args.learning_rate, args.discount, device)
    with TrainerServer(("127.0.0.1", args.port), Handler, trainer, args.output, args.metrics) as server:
        print(f"Visual REINFORCE trainer on 127.0.0.1:{args.port} ({device}); checkpoints -> {args.output}")
        server.serve_forever()


if __name__ == "__main__":
    main()
