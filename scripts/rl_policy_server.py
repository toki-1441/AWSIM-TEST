#!/usr/bin/env python3
"""Dependency-free single-environment REINFORCE trainer for PythonRlBridge.

It deliberately trains only the planning action (desired speed and lateral offset); Unity owns
throttle, brake and steering actuation. Camera JPEG bytes are transported by the protocol for
vision trainers, while this small baseline learns from the stable relative-vector contract.
"""
import argparse
import json
import math
from pathlib import Path
from random import gauss
import socketserver


class ReinforcePolicy:
    def __init__(self, learning_rate, discount, exploration):
        self.learning_rate, self.discount, self.exploration = learning_rate, discount, exploration
        self.weights, self.bias, self.rollout = None, [0.0, 0.0], []

    def act(self, observation):
        if self.weights is None:
            self.weights = [[0.0] * len(observation) for _ in range(2)]
        if len(observation) != len(self.weights[0]):
            raise ValueError("Observation layout changed during training")
        features = [max(-10.0, min(10.0, value)) / 10.0 for value in observation]
        means = [math.tanh(self.bias[row] + sum(w * x for w, x in zip(self.weights[row], features))) for row in range(2)]
        sampled = [max(-1.0, min(1.0, mean + gauss(0.0, self.exploration))) for mean in means]
        self.rollout.append([features, means, sampled, 0.0])
        safe_speed = observation[1] if len(observation) > 1 and math.isfinite(observation[1]) else 3.0
        return {"desiredSpeed": max(0.0, safe_speed * (sampled[0] + 1.0) * 0.5), "desiredLateralOffsetNormalized": sampled[1]}

    def reward_previous(self, reward):
        if self.rollout:
            self.rollout[-1][3] += reward

    def finish_episode(self, terminal_reward):
        self.reward_previous(terminal_reward)
        returns, running = [], 0.0
        for _, _, _, reward in reversed(self.rollout):
            running = reward + self.discount * running
            returns.append(running)
        returns.reverse()
        if returns:
            baseline = sum(returns) / len(returns)
            variance = sum((value - baseline) ** 2 for value in returns) / len(returns)
            scale = math.sqrt(variance) or 1.0
            for (features, means, sampled, _), value in zip(self.rollout, returns):
                advantage = (value - baseline) / scale
                for row in range(2):
                    gradient = advantage * (sampled[row] - means[row]) / (self.exploration ** 2)
                    self.bias[row] += self.learning_rate * gradient
                    for index, feature in enumerate(features):
                        self.weights[row][index] += self.learning_rate * gradient * feature
        self.rollout.clear()

    def save(self, path):
        if self.weights is None:
            return
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps({"weights": self.weights, "bias": self.bias}, indent=2), encoding="utf-8")


class TrainerServer(socketserver.TCPServer):
    allow_reuse_address = True
    def __init__(self, address, handler, policy, checkpoint):
        super().__init__(address, handler)
        self.policy, self.checkpoint = policy, checkpoint


class Handler(socketserver.StreamRequestHandler):
    def handle(self):
        for raw in self.rfile:
            request = json.loads(raw)
            if request.get("type") == "step":
                self.server.policy.reward_previous(request.get("reward", 0.0))
                action = self.server.policy.act(request.get("observation", []))
                self.wfile.write((json.dumps(action) + "\n").encode())
            elif request.get("type") == "terminal":
                self.server.policy.finish_episode(request.get("reward", 0.0))
                self.server.policy.save(self.server.checkpoint)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=5015)
    parser.add_argument("--output", type=Path, default=Path("Build/physical-ai-reinforce-policy.json"))
    parser.add_argument("--learning-rate", type=float, default=0.002)
    parser.add_argument("--discount", type=float, default=0.99)
    parser.add_argument("--exploration", type=float, default=0.2)
    args = parser.parse_args()
    policy = ReinforcePolicy(args.learning_rate, args.discount, args.exploration)
    with TrainerServer(("127.0.0.1", args.port), Handler, policy, args.output) as server:
        print(f"REINFORCE trainer listening on 127.0.0.1:{args.port}; checkpoints -> {args.output}")
        server.serve_forever()


if __name__ == "__main__":
    main()
