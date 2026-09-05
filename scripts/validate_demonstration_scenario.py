#!/usr/bin/env python3
"""Require every exported demonstration row to carry one expected scenario label."""
import argparse
import json
from pathlib import Path


def validate(manifest, expected):
    rows = [json.loads(line) for line in manifest.read_text(encoding="utf-8").splitlines()]
    if not rows:
        raise ValueError("Demonstration manifest is empty")
    wrong = [index + 1 for index, row in enumerate(rows) if row.get("scenarioLabel") != expected]
    if wrong:
        raise ValueError(
            f"{len(wrong)} samples are not labelled {expected!r} (first row {wrong[0]}). "
            "Use a dedicated capture directory."
        )
    return len(rows)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("manifest", type=Path)
    parser.add_argument("expected_label")
    args = parser.parse_args()
    count = validate(args.manifest, args.expected_label)
    print(f"validated {count} samples labelled {args.expected_label!r} -> {args.manifest}")


if __name__ == "__main__":
    main()
