#!/usr/bin/env python3
"""Gate Physical AI definition-of-done comparisons from persisted evaluation reports."""
import argparse
import json
import math
import re
from pathlib import Path


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def finite(value):
    return value is not None and value >= 0 and math.isfinite(value)


def paired_hazard_safety(model_report, baseline_report, minimum_reduction, significance):
    """Compare identical seeded hazards with an exact paired sign test.

    A raw collision-rate difference is not safety evidence when no hazard was
    actually spawned, or when the outcome came from different courses.  Only
    seeds with a hazard in both reports are compared.  The two-sided binomial
    test on discordant pairs is the exact McNemar test for this binary outcome.
    """
    model = {episode.get("courseSeed"): episode for episode in model_report.get("episodes", [])}
    baseline = {episode.get("courseSeed"): episode for episode in baseline_report.get("episodes", [])}
    pairs = [(model[seed], baseline[seed]) for seed in model.keys() & baseline.keys()
             if model[seed].get("hazardWasSpawned") and baseline[seed].get("hazardWasSpawned")]
    if not pairs:
        return False, {"pairedHazardEpisodes": 0, "pValue": 1.0}
    model_collisions = sum(bool(model_episode.get("blindCornerHazardCollisionCount", 0)) for model_episode, _ in pairs)
    baseline_collisions = sum(bool(baseline_episode.get("blindCornerHazardCollisionCount", 0)) for _, baseline_episode in pairs)
    baseline_only = sum(not bool(model_episode.get("blindCornerHazardCollisionCount", 0)) and bool(baseline_episode.get("blindCornerHazardCollisionCount", 0)) for model_episode, baseline_episode in pairs)
    model_only = sum(bool(model_episode.get("blindCornerHazardCollisionCount", 0)) and not bool(baseline_episode.get("blindCornerHazardCollisionCount", 0)) for model_episode, baseline_episode in pairs)
    discordant = baseline_only + model_only
    tail = sum(math.comb(discordant, count) for count in range(max(baseline_only, model_only), discordant + 1)) / (2 ** discordant) if discordant else 1.0
    p_value = min(1.0, 2.0 * tail)
    reduction = (baseline_collisions - model_collisions) / len(pairs)
    passed = reduction >= minimum_reduction and baseline_only > model_only and p_value <= significance
    return passed, {"pairedHazardEpisodes": len(pairs), "modelHazardCollisions": model_collisions,
                    "baselineHazardCollisions": baseline_collisions, "reduction": reduction, "pValue": p_value}


def blind_ablation_provenance(model_report, blackout_report):
    """Require a same-policy, same-suite rendered-camera blackout pair."""
    model_label = str(model_report.get("policyLabel", ""))
    blackout_label = str(blackout_report.get("policyLabel", ""))
    model_scenario = str(model_report.get("evaluationScenario", ""))
    blackout_scenario = str(blackout_report.get("evaluationScenario", ""))
    model_seeds = [episode.get("courseSeed") for episode in model_report.get("episodes", [])]
    blackout_seeds = [episode.get("courseSeed") for episode in blackout_report.get("episodes", [])]
    checks = {
        "sameCheckpointLabel": bool(model_label) and blackout_label == model_label + "+image-blackout",
        "sameSoloScenario": model_scenario == "solo" and blackout_scenario == model_scenario,
        "sameObservationContract": bool(model_report.get("observationContract")) and
                                   blackout_report.get("observationContract") == model_report.get("observationContract"),
        "sameOrderedSeeds": bool(model_seeds) and blackout_seeds == model_seeds and len(set(model_seeds)) == len(model_seeds),
        "renderedFramesPresent": model_report.get("cameraFramesCaptured", 0) > 0,
        "blackoutFramesWithheld": blackout_report.get("cameraFramesWithheld", 0) > 0,
    }
    return all(checks.values()), {
        "checks": checks,
        "modelPolicyLabel": model_label,
        "blackoutPolicyLabel": blackout_label,
        "modelScenario": model_scenario,
        "blackoutScenario": blackout_scenario,
        "modelCameraFramesCaptured": model_report.get("cameraFramesCaptured", 0),
        "blackoutCameraFramesWithheld": blackout_report.get("cameraFramesWithheld", 0),
        "pairedSeeds": model_seeds if checks["sameOrderedSeeds"] else [],
    }


def generalization_provenance(training_report, holdout_report):
    """Require two rendered-camera suites from one content-addressed policy."""
    training_label = str(training_report.get("policyLabel", ""))
    holdout_label = str(holdout_report.get("policyLabel", ""))
    training_seeds = [episode.get("courseSeed") for episode in training_report.get("episodes", [])]
    holdout_seeds = [episode.get("courseSeed") for episode in holdout_report.get("episodes", [])]
    sha_pattern = re.compile(r":sha256=[0-9a-f]{64}$")
    checks = {
        "sameContentAddressedPolicy": training_label == holdout_label and bool(sha_pattern.search(training_label)),
        "bothSolo": training_report.get("evaluationScenario") == "solo" and
                    holdout_report.get("evaluationScenario") == "solo",
        "sameObservationContract": bool(training_report.get("observationContract")) and
                                   training_report.get("observationContract") == holdout_report.get("observationContract"),
        "renderedFramesPresent": training_report.get("cameraFramesCaptured", 0) > 0 and
                                 holdout_report.get("cameraFramesCaptured", 0) > 0,
        "completeUniqueEpisodeLists": bool(training_seeds) and bool(holdout_seeds) and
                                      len(set(training_seeds)) == len(training_seeds) and
                                      len(set(holdout_seeds)) == len(holdout_seeds) and
                                      training_report.get("episodeCount") == len(training_seeds) and
                                      holdout_report.get("episodeCount") == len(holdout_seeds),
        "disjointEvaluationSeeds": not (set(training_seeds) & set(holdout_seeds)),
    }
    return all(checks.values()), {"checks": checks, "trainingPolicyLabel": training_label,
                                  "holdoutPolicyLabel": holdout_label,
                                  "trainingSeeds": training_seeds, "holdoutSeeds": holdout_seeds}


def head_to_head_provenance(holdout_report, head_to_head_report):
    """Require the holdout policy and seeds with only head-to-head enabled."""
    holdout_label = str(holdout_report.get("policyLabel", ""))
    race_label = str(head_to_head_report.get("policyLabel", ""))
    holdout_seeds = [episode.get("courseSeed") for episode in holdout_report.get("episodes", [])]
    race_seeds = [episode.get("courseSeed") for episode in head_to_head_report.get("episodes", [])]
    checks = {
        "sameCheckpointLabel": bool(holdout_label) and race_label == holdout_label + "+head-to-head",
        "headToHeadScenario": str(head_to_head_report.get("evaluationScenario", "")).startswith("head-to-head:"),
        "sameObservationContract": bool(holdout_report.get("observationContract")) and
                                   head_to_head_report.get("observationContract") == holdout_report.get("observationContract"),
        "sameOrderedSeeds": bool(holdout_seeds) and race_seeds == holdout_seeds,
        "renderedFramesPresent": head_to_head_report.get("cameraFramesCaptured", 0) > 0,
    }
    return all(checks.values()), {"checks": checks, "holdoutPolicyLabel": holdout_label,
                                  "headToHeadPolicyLabel": race_label, "pairedSeeds": race_seeds}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--training", required=True, type=Path, help="report on training-distribution courses")
    parser.add_argument("--holdout", required=True, type=Path, help="report on fixed unseen courses")
    parser.add_argument("--head-to-head", required=True, type=Path,
                        help="same visual policy evaluated with the scored opponent enabled")
    parser.add_argument("--blind-baseline", required=True, type=Path,
                        help="same checkpoint and holdout suite with rendered camera frames withheld")
    parser.add_argument("--race-baseline", required=True, type=Path, help="same head-to-head suite baseline")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--max-completion-gap", type=float, default=0.10)
    parser.add_argument("--max-lap-time-ratio", type=float, default=1.20)
    parser.add_argument("--minimum-blind-collision-reduction", type=float, default=0.05)
    parser.add_argument("--blind-significance", type=float, default=0.05)
    parser.add_argument("--minimum-ego-overtake-rate", type=float, default=0.05,
                        help="require a measurable collision-free overtake rate in the race suite")
    parser.add_argument("--max-head-to-head-opponent-collision-rate", type=float, default=0.0,
                        help="require a collision-free learned-policy head-to-head suite by default")
    args = parser.parse_args()
    training, holdout, head_to_head, blind, race = map(load, (args.training, args.holdout, args.head_to_head, args.blind_baseline, args.race_baseline))
    train_lap, holdout_lap = training.get("meanCompletedLapTimeSeconds", -1), holdout.get("meanCompletedLapTimeSeconds", -1)
    blind_safe, blind_evidence = paired_hazard_safety(holdout, blind, args.minimum_blind_collision_reduction, args.blind_significance)
    blind_provenance_valid, blind_provenance = blind_ablation_provenance(holdout, blind)
    generalization_provenance_valid, generalization_evidence = generalization_provenance(training, holdout)
    head_to_head_provenance_valid, head_to_head_provenance_evidence = head_to_head_provenance(holdout, head_to_head)
    head_to_head_label = str(head_to_head.get("policyLabel", ""))
    head_to_head_scenario = str(head_to_head.get("evaluationScenario", ""))
    head_to_head_was_enabled = "+head-to-head" in head_to_head_label and head_to_head_scenario.startswith("head-to-head:")
    checks = {
        "generalizationProvenance": generalization_provenance_valid,
        "generalizationCompletion": holdout["completionRate"] >= training["completionRate"] - args.max_completion_gap,
        "generalizationLapTime": finite(train_lap) and finite(holdout_lap) and holdout_lap <= train_lap * args.max_lap_time_ratio,
        "blindCornerAblationProvenance": blind_provenance_valid,
        "blindCornerSafety": blind_provenance_valid and blind_safe,
        "headToHeadProvenance": head_to_head_provenance_valid,
        "headToHeadSafety": head_to_head_provenance_valid and finite(head_to_head.get("opponentCollisionRate")) and
                            head_to_head["opponentCollisionRate"] <= args.max_head_to_head_opponent_collision_rate,
        "headToHeadOvertake": head_to_head_provenance_valid and head_to_head.get("egoOvertakeRate", 0.0) >= args.minimum_ego_overtake_rate,
    }
    result = {"passed": all(checks.values()), "checks": checks,
              "generalizationEvidence": generalization_evidence,
              "blindCornerEvidence": {**blind_evidence, "ablationProvenance": blind_provenance},
              "headToHeadEvidence": {
                  "episodeCount": head_to_head.get("episodeCount", 0),
                  "policyLabel": head_to_head_label,
                  "evaluationScenario": head_to_head_scenario,
                  "headToHeadFlagRecorded": head_to_head_was_enabled,
                  "provenance": head_to_head_provenance_evidence,
                  "opponentCollisionRate": head_to_head.get("opponentCollisionRate"),
                  "egoOvertakeRate": head_to_head.get("egoOvertakeRate"),
                  "opponentOvertakeRate": head_to_head.get("opponentOvertakeRate"),
                  "referenceOpponentCollisionRate": race.get("opponentCollisionRate"),
              }, "thresholds": vars(args)}
    encoded = json.dumps(result, indent=2, default=str)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True); args.output.write_text(encoded, encoding="utf-8")
    print(encoded)
    if not result["passed"]: raise SystemExit(1)


if __name__ == "__main__": main()
