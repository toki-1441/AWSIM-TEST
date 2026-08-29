#!/usr/bin/env python3
"""Train a dependency-free linear planning BC model from Physical AI JSONL demos.

The output is directly consumable by the BehaviorCloningPolicy Unity component.
"""
import argparse
import json
import math
from pathlib import Path


def load_samples(paths):
    samples = []
    for path in paths:
        with path.open(encoding="utf-8") as handle:
            for line in handle:
                frame = json.loads(line)
                observation = frame.get("Observation")
                command = frame.get("Command")
                if frame.get("Terminal") or not observation or not command:
                    continue
                samples.append((observation, command["DesiredSpeed"], command["DesiredLateralOffsetNormalized"]))
    if not samples:
        raise ValueError("No non-terminal demonstration frames were found.")
    width = len(samples[0][0])
    if any(len(features) != width for features, _, _ in samples):
        raise ValueError("All demonstration observations must use one feature layout.")
    return samples, width


def fit(features, targets, iterations, learning_rate, l2):
    count, width = len(features), len(features[0])
    means = [sum(row[index] for row in features) / count for index in range(width)]
    scales = [math.sqrt(sum((row[index] - means[index]) ** 2 for row in features) / count) or 1.0 for index in range(width)]
    normalized = [[(value - means[index]) / scales[index] for index, value in enumerate(row)] for row in features]
    weights = [0.0] * width
    bias = sum(targets) / count
    for _ in range(iterations):
        gradient = [0.0] * width
        bias_gradient = 0.0
        for row, target in zip(normalized, targets):
            error = bias + sum(weight * value for weight, value in zip(weights, row)) - target
            bias_gradient += error
            for index, value in enumerate(row):
                gradient[index] += error * value
        for index in range(width):
            weights[index] -= learning_rate * (gradient[index] / count + l2 * weights[index])
        bias -= learning_rate * bias_gradient / count
    raw_weights = [weight / scale for weight, scale in zip(weights, scales)]
    raw_bias = bias - sum(weight * mean for weight, mean in zip(raw_weights, means))
    return raw_weights, raw_bias


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("input", nargs="+", type=Path, help="JSONL files written by PlanningDemonstrationRecorder")
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--iterations", type=int, default=2000)
    parser.add_argument("--learning-rate", type=float, default=0.03)
    parser.add_argument("--l2", type=float, default=0.0001)
    args = parser.parse_args()
    samples, _ = load_samples(args.input)
    features = [sample[0] for sample in samples]
    speed_weights, speed_bias = fit(features, [sample[1] for sample in samples], args.iterations, args.learning_rate, args.l2)
    lateral_weights, lateral_bias = fit(features, [sample[2] for sample in samples], args.iterations, args.learning_rate, args.l2)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({
        "SpeedWeights": speed_weights,
        "SpeedBias": speed_bias,
        "LateralWeights": lateral_weights,
        "LateralBias": lateral_bias,
    }, indent=2), encoding="utf-8")
    print(f"trained {len(samples)} frames -> {args.output}")


if __name__ == "__main__":
    main()
