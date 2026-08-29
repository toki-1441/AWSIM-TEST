#!/usr/bin/env python3
"""Gate Physical AI definition-of-done comparisons from persisted holdout reports."""
import argparse
import json
import math
from pathlib import Path


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def finite(value):
    return value is not None and value >= 0 and math.isfinite(value)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--training", required=True, type=Path, help="report on training-distribution courses")
    parser.add_argument("--holdout", required=True, type=Path, help="report on fixed unseen courses")
    parser.add_argument("--blind-baseline", required=True, type=Path, help="same holdout suite without visibility-aware policy")
    parser.add_argument("--race-baseline", required=True, type=Path, help="same head-to-head suite baseline")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--max-completion-gap", type=float, default=0.10)
    parser.add_argument("--max-lap-time-ratio", type=float, default=1.20)
    parser.add_argument("--minimum-blind-collision-reduction", type=float, default=0.05)
    args = parser.parse_args()
    training, holdout, blind, race = map(load, (args.training, args.holdout, args.blind_baseline, args.race_baseline))
    train_lap, holdout_lap = training.get("meanCompletedLapTimeSeconds", -1), holdout.get("meanCompletedLapTimeSeconds", -1)
    checks = {
        "generalizationCompletion": holdout["completionRate"] >= training["completionRate"] - args.max_completion_gap,
        "generalizationLapTime": finite(train_lap) and finite(holdout_lap) and holdout_lap <= train_lap * args.max_lap_time_ratio,
        "blindCornerSafety": holdout["blindCornerHazardCollisionRate"] <= blind["blindCornerHazardCollisionRate"] - args.minimum_blind_collision_reduction,
        "headToHeadSafety": holdout["opponentCollisionRate"] <= race["opponentCollisionRate"],
    }
    result = {"passed": all(checks.values()), "checks": checks, "thresholds": vars(args)}
    encoded = json.dumps(result, indent=2, default=str)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True); args.output.write_text(encoded, encoding="utf-8")
    print(encoded)
    if not result["passed"]: raise SystemExit(1)


if __name__ == "__main__": main()
