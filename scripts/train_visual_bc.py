#!/usr/bin/env python3
"""Train a visual behaviour-cloning policy from exported camera demonstrations."""
import argparse
import base64
import json
from pathlib import Path

import torch
from torch import nn

from train_vision_rl import VisionPolicy, image_tensor


def load_samples(manifest):
    samples = []
    for line in manifest.read_text(encoding="utf-8").splitlines():
        row = json.loads(line)
        image = row.get("image")
        if image and (manifest.parent / image).is_file():
            samples.append(row)
    if not samples:
        raise ValueError("No synchronized camera samples found; export a recording with rendered frames.")
    width = len(samples[0]["observation"])
    if any(len(sample["observation"]) != width for sample in samples):
        raise ValueError("All demonstrations must use one observation layout.")
    return samples, width


def target(sample):
    safe_speed = max(float(sample["observation"][1]), 0.1) if len(sample["observation"]) > 1 else 3.0
    speed = max(-1.0, min(1.0, 2.0 * float(sample["desiredSpeed"]) / safe_speed - 1.0))
    lateral = max(-1.0, min(1.0, float(sample["desiredLateralOffsetNormalized"])))
    return [speed, lateral]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("manifest", type=Path, help="manifest.jsonl from export_camera_demonstrations.py")
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--metrics", type=Path, default=Path("Build/physical-ai-visual-bc-metrics.json"))
    parser.add_argument("--epochs", type=int, default=20)
    parser.add_argument("--learning-rate", type=float, default=0.0003)
    parser.add_argument("--device", default="cpu", choices=("cpu", "cuda"))
    args = parser.parse_args()
    if args.device == "cuda" and not torch.cuda.is_available():
        raise SystemExit("--device cuda was requested but CUDA is unavailable")
    device = torch.device(args.device)
    samples, width = load_samples(args.manifest)
    policy = VisionPolicy(width).to(device)
    optimizer = torch.optim.Adam(policy.parameters(), lr=args.learning_rate)
    loss_function = nn.MSELoss()
    losses = []
    for epoch in range(args.epochs):
        total = 0.0
        for sample in samples:
            encoded = base64.b64encode((args.manifest.parent / sample["image"]).read_bytes()).decode("ascii")
            vector = torch.tensor(sample["observation"], dtype=torch.float32, device=device).clamp(-10, 10).div(10).unsqueeze(0)
            prediction, _ = policy(image_tensor(encoded, device), vector)
            loss = loss_function(prediction, torch.tensor(target(sample), dtype=torch.float32, device=device).unsqueeze(0))
            optimizer.zero_grad()
            loss.backward()
            optimizer.step()
            total += float(loss.detach())
        losses.append(total / len(samples))
        print(f"epoch {epoch + 1}/{args.epochs}: mse={losses[-1]:.6f}")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    torch.save({"vector_width": width, "model_state": policy.state_dict()}, args.output)
    args.metrics.parent.mkdir(parents=True, exist_ok=True)
    args.metrics.write_text(json.dumps({"samples": len(samples), "epochs": args.epochs, "mse": losses}, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
