#!/usr/bin/env python3
"""Convert steady-state real-vehicle cornering CSV samples to Unity calibration JSON."""
import argparse
import csv
import json
from pathlib import Path


def convert(rows, speed_column, yaw_rate_column, bank_column):
    samples = []
    for line, row in enumerate(rows, start=2):
        try:
            samples.append({
                "Speed": float(row[speed_column]),
                "YawRateRadiansPerSecond": float(row[yaw_rate_column]),
                "BankDegrees": float(row[bank_column]) if bank_column else 0.0,
            })
        except (KeyError, ValueError) as error:
            raise ValueError(f"Invalid telemetry row {line}: {error}") from error
    if not samples:
        raise ValueError("CSV contained no telemetry samples")
    return {"Samples": samples}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--speed-column", default="speed_mps")
    parser.add_argument("--yaw-rate-column", default="yaw_rate_radps")
    parser.add_argument("--bank-column", default="bank_degrees", help="pass an empty string for a flat course")
    args = parser.parse_args()
    with args.input.open(newline="", encoding="utf-8") as handle:
        data = convert(csv.DictReader(handle), args.speed_column, args.yaw_rate_column, args.bank_column)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(data, indent=2), encoding="utf-8")
    print(f"converted {len(data['Samples'])} samples -> {args.output}")


if __name__ == "__main__":
    main()
