#!/usr/bin/env python3
"""Convert steady-state real-vehicle cornering CSV samples to Unity calibration JSON."""
import argparse
import csv
import json
import math
from pathlib import Path


def finite_float(row, column, line):
    try:
        value = float(row[column])
    except (KeyError, ValueError) as error:
        raise ValueError(f"Invalid telemetry row {line}, column {column!r}: {error}") from error
    if not math.isfinite(value):
        raise ValueError(f"Invalid telemetry row {line}, column {column!r}: value must be finite")
    return value


def convert(rows, speed_column, yaw_rate_column, bank_column, minimum_lateral_acceleration, run_id):
    samples = []
    raw_sample_count = 0
    for line, row in enumerate(rows, start=2):
        raw_sample_count += 1
        speed = finite_float(row, speed_column, line)
        yaw_rate = finite_float(row, yaw_rate_column, line)
        bank = finite_float(row, bank_column, line) if bank_column else 0.0
        if speed <= 0.0:
            raise ValueError(f"Invalid telemetry row {line}: speed must be positive")
        if abs(bank) > 30.0:
            raise ValueError(f"Invalid telemetry row {line}: bank exceeds the supported ±30 degree range")
        if abs(speed * yaw_rate) < minimum_lateral_acceleration:
            continue
        samples.append({
            "Speed": speed,
            "YawRateRadiansPerSecond": yaw_rate,
            "BankDegrees": bank,
        })
    if not samples:
        raise ValueError("CSV contained no usable steady-state cornering samples")
    return {"RunId": run_id, "RawSampleCount": raw_sample_count, "Samples": samples}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--speed-column", default="speed_mps")
    parser.add_argument("--yaw-rate-column", default="yaw_rate_radps")
    parser.add_argument("--bank-column", default="bank_degrees", help="pass an empty string for a flat course")
    parser.add_argument("--run-id", help="stable identifier written into the calibration asset (defaults to input stem)")
    parser.add_argument("--minimum-lateral-acceleration", type=float, default=0.5)
    parser.add_argument("--minimum-samples", type=int, default=30)
    args = parser.parse_args()
    if args.minimum_lateral_acceleration <= 0.0:
        parser.error("--minimum-lateral-acceleration must be positive")
    if args.minimum_samples < 1:
        parser.error("--minimum-samples must be at least one")
    with args.input.open(newline="", encoding="utf-8") as handle:
        data = convert(
            csv.DictReader(handle),
            args.speed_column,
            args.yaw_rate_column,
            args.bank_column,
            args.minimum_lateral_acceleration,
            args.run_id or args.input.stem,
        )
    if len(data["Samples"]) < args.minimum_samples:
        raise ValueError(
            f"Only {len(data['Samples'])} usable steady-state samples; require at least {args.minimum_samples}. "
            "Collect additional constant-speed, constant-steering cornering data."
        )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(data, indent=2), encoding="utf-8")
    print(f"converted {len(data['Samples'])}/{data['RawSampleCount']} usable samples ({data['RunId']}) -> {args.output}")


if __name__ == "__main__":
    main()
