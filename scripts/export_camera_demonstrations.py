#!/usr/bin/env python3
"""Extract sparse JPEG frames and synchronized commands from teleoperation JSONL files."""
import argparse
import base64
import json
from pathlib import Path


def export(inputs, output):
    image_dir = output / "images"
    image_dir.mkdir(parents=True, exist_ok=True)
    manifest = []
    last_image = None
    for source in inputs:
        for line_number, line in enumerate(source.read_text(encoding="utf-8").splitlines(), start=1):
            frame = json.loads(line)
            encoded = frame.get("CameraJpegBase64")
            if encoded:
                name = f"{source.stem}-{frame['EpisodeId']:06d}-{frame['Step']:06d}.jpg"
                (image_dir / name).write_bytes(base64.b64decode(encoded))
                last_image = f"images/{name}"
            if frame.get("Terminal") or not frame.get("Observation") or not frame.get("Command"):
                continue
            manifest.append({
                "image": last_image,
                "observation": frame["Observation"],
                "desiredSpeed": frame["Command"]["DesiredSpeed"],
                "desiredLateralOffsetNormalized": frame["Command"]["DesiredLateralOffsetNormalized"],
                "reward": frame.get("Reward", 0.0),
            })
    (output / "manifest.jsonl").write_text("".join(json.dumps(row) + "\n" for row in manifest), encoding="utf-8")
    return len(manifest)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("input", nargs="+", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    print(f"exported {export(args.input, args.output)} synchronized samples -> {args.output}")


if __name__ == "__main__":
    main()
