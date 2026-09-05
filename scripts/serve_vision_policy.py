#!/usr/bin/env python3
"""Serve a saved visual BC or REINFORCE checkpoint to ``PythonRlBridge``."""
import argparse
import json
from pathlib import Path
import socketserver

import numpy as np
import torch

from train_vision_rl import VisionPolicy, image_tensor


class PolicyServer(socketserver.TCPServer):
    allow_reuse_address = True

    def __init__(self, address, handler, policy, vector_width, device):
        super().__init__(address, handler)
        self.policy, self.vector_width, self.device = policy, vector_width, device

    def command(self, observation, camera):
        if len(observation) != self.vector_width:
            raise ValueError(f"Expected {self.vector_width} observation values, got {len(observation)}")
        vector = torch.tensor(observation, dtype=torch.float32, device=self.device).clamp(-10, 10).div(10).unsqueeze(0)
        with torch.inference_mode():
            action, _ = self.policy(image_tensor(camera, self.device), vector)
        safe_speed = observation[1] if len(observation) > 1 and np.isfinite(observation[1]) else 3.0
        return {"desiredSpeed": max(0.0, float(safe_speed) * (float(action[0, 0]) + 1.0) * 0.5),
                "desiredLateralOffsetNormalized": float(action[0, 1])}


class Handler(socketserver.StreamRequestHandler):
    def handle(self):
        for raw in self.rfile:
            request = json.loads(raw)
            if request.get("type") == "step":
                self.wfile.write((json.dumps(self.server.command(request.get("observation", []), request.get("cameraJpegBase64"))) + "\n").encode("utf-8"))
                self.wfile.flush()
            elif request.get("type") == "terminal":
                # PythonRlBridge waits for this acknowledgement before a player
                # evaluation exits, ensuring the final message cannot leave a
                # synchronous stream half-open.
                self.wfile.write(b'{"ack":true}\n')
                self.wfile.flush()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("checkpoint", type=Path)
    parser.add_argument("--port", type=int, default=5015)
    parser.add_argument("--device", default="cpu", choices=("cpu", "cuda"))
    args = parser.parse_args()
    if args.device == "cuda" and not torch.cuda.is_available():
        raise SystemExit("--device cuda was requested but CUDA is unavailable")
    data = torch.load(args.checkpoint, map_location=args.device, weights_only=True)
    policy = VisionPolicy(data["vector_width"]).to(args.device)
    policy.load_state_dict(data["model_state"])
    policy.eval()
    with PolicyServer(("127.0.0.1", args.port), Handler, policy, data["vector_width"], torch.device(args.device)) as server:
        print(f"Visual policy serving {args.checkpoint} on 127.0.0.1:{args.port} ({args.device})")
        server.serve_forever()


if __name__ == "__main__":
    main()
