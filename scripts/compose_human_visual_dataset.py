#!/usr/bin/env python3
"""Compose nominal, blind-corner and head-to-head human manifests with provenance."""
import argparse
import json
import shutil
from pathlib import Path


SCENARIOS = (
    ("human-nominal", "nominal"),
    ("human-blind-corner-hazard", "blind_corner"),
    ("human-head-to-head-overtake", "head_to_head"),
)


def load_rows(manifest, expected_label):
    rows = [json.loads(line) for line in manifest.read_text(encoding="utf-8").splitlines()]
    if not rows:
        raise ValueError(f"Manifest is empty: {manifest}")
    for index, row in enumerate(rows, start=1):
        if row.get("scenarioLabel") != expected_label:
            raise ValueError(f"{manifest}:{index} is not labelled {expected_label!r}")
        image = manifest.parent / row.get("image", "")
        if not image.is_file():
            raise ValueError(f"{manifest}:{index} references missing image {image}")
        if not row.get("observation"):
            raise ValueError(f"{manifest}:{index} has an empty observation")
    return rows


def compose(manifests, output):
    loaded = []
    widths = set()
    for expected_label, short_name in SCENARIOS:
        manifest = manifests[expected_label]
        rows = load_rows(manifest, expected_label)
        loaded.append((expected_label, short_name, manifest, rows))
        widths.update(len(row["observation"]) for row in rows)
    if len(widths) != 1:
        raise ValueError(f"Human datasets do not share one observation width: {sorted(widths)}")
    if output.exists() and any(output.iterdir()):
        raise ValueError(f"Output directory is not empty: {output}")

    output.mkdir(parents=True, exist_ok=True)
    combined = []
    counts = {}
    observation_width = widths.pop()
    for expected_label, short_name, manifest, rows in loaded:
        counts[expected_label] = len(rows)
        image_directory = output / "images" / short_name
        image_directory.mkdir(parents=True, exist_ok=True)
        for index, row in enumerate(rows):
            source_image = manifest.parent / row["image"]
            suffix = source_image.suffix.lower() or ".jpg"
            destination = image_directory / f"{index:08d}{suffix}"
            shutil.copy2(source_image, destination)
            exported = dict(row)
            exported["image"] = destination.relative_to(output).as_posix()
            exported["sourceScenario"] = expected_label
            combined.append(exported)

    manifest_path = output / "manifest.jsonl"
    manifest_path.write_text("".join(json.dumps(row) + "\n" for row in combined), encoding="utf-8")
    summary = {
        "humanOnly": True,
        "observationWidth": observation_width,
        "sampleCount": len(combined),
        "scenarioCounts": counts,
    }
    (output / "dataset-summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    return summary


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--nominal", required=True, type=Path)
    parser.add_argument("--blind-corner", required=True, type=Path)
    parser.add_argument("--head-to-head", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    manifests = {
        "human-nominal": args.nominal,
        "human-blind-corner-hazard": args.blind_corner,
        "human-head-to-head-overtake": args.head_to_head,
    }
    summary = compose(manifests, args.output)
    print(f"composed {summary['sampleCount']} human samples, observation width {summary['observationWidth']} -> {args.output}")


if __name__ == "__main__":
    main()
