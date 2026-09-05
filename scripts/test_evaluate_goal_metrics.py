#!/usr/bin/env python3
"""Unit checks for evidence-aware Physical AI completion metrics."""
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from evaluate_goal_metrics import (
    blind_ablation_provenance,
    generalization_provenance,
    head_to_head_provenance,
    paired_hazard_safety,
)


class PairedHazardSafetyTests(unittest.TestCase):
    def report(self, collisions):
        return {"episodes": [
            {"courseSeed": seed, "hazardWasSpawned": True,
             "blindCornerHazardCollisionCount": collision}
            for seed, collision in enumerate(collisions)
        ]}

    def test_passes_when_six_paired_hazards_all_improve(self):
        passed, evidence = paired_hazard_safety(self.report([0] * 6), self.report([1] * 6), 0.05, 0.05)

        self.assertTrue(passed)
        self.assertEqual(evidence["pairedHazardEpisodes"], 6)
        self.assertLessEqual(evidence["pValue"], 0.05)

    def test_rejects_reports_without_paired_hazard_exposure(self):
        model = {"episodes": [{"courseSeed": 1, "hazardWasSpawned": True}]}
        baseline = {"episodes": [{"courseSeed": 1, "hazardWasSpawned": False}]}

        passed, evidence = paired_hazard_safety(model, baseline, 0.05, 0.05)

        self.assertFalse(passed)
        self.assertEqual(evidence["pairedHazardEpisodes"], 0)


class BlindAblationProvenanceTests(unittest.TestCase):
    def report(self, label, captured=0, withheld=0, seeds=(1009, 2017)):
        return {
            "policyLabel": label,
            "evaluationScenario": "solo",
            "observationContract": "relative-course-waypoints+speed+visibility",
            "cameraFramesCaptured": captured,
            "cameraFramesWithheld": withheld,
            "episodes": [{"courseSeed": seed} for seed in seeds],
        }

    def test_accepts_same_checkpoint_camera_blackout_pair(self):
        model = self.report("visual-checkpoint:model.pt", captured=120)
        blackout = self.report("visual-checkpoint:model.pt+image-blackout", withheld=118)

        passed, evidence = blind_ablation_provenance(model, blackout)

        self.assertTrue(passed)
        self.assertTrue(all(evidence["checks"].values()))

    def test_rejects_different_policy_as_blind_baseline(self):
        model = self.report("visual-checkpoint:model.pt", captured=120)
        reference = self.report("reference-planner", withheld=118)

        passed, evidence = blind_ablation_provenance(model, reference)

        self.assertFalse(passed)
        self.assertFalse(evidence["checks"]["sameCheckpointLabel"])

    def test_rejects_seed_or_camera_evidence_mismatch(self):
        model = self.report("visual-checkpoint:model.pt", captured=120)
        blackout = self.report(
            "visual-checkpoint:model.pt+image-blackout", withheld=0, seeds=(1009, 9999)
        )

        passed, evidence = blind_ablation_provenance(model, blackout)

        self.assertFalse(passed)
        self.assertFalse(evidence["checks"]["sameOrderedSeeds"])
        self.assertFalse(evidence["checks"]["blackoutFramesWithheld"])


class SuiteProvenanceTests(unittest.TestCase):
    sha_label = "visual-checkpoint:model.pt:sha256=" + "a" * 64

    def report(self, label, seeds, scenario="solo", frames=100):
        return {
            "policyLabel": label,
            "evaluationScenario": scenario,
            "observationContract": "relative-course-waypoints+speed+visibility",
            "cameraFramesCaptured": frames,
            "episodeCount": len(seeds),
            "episodes": [{"courseSeed": seed} for seed in seeds],
        }

    def test_accepts_disjoint_training_and_holdout_for_same_hashed_policy(self):
        training = self.report(self.sha_label, (1, 2))
        holdout = self.report(self.sha_label, (1009, 2017))

        passed, evidence = generalization_provenance(training, holdout)

        self.assertTrue(passed)
        self.assertTrue(all(evidence["checks"].values()))

    def test_rejects_unhashed_or_overlapping_generalization_reports(self):
        training = self.report("visual-checkpoint:model.pt", (1, 1009))
        holdout = self.report("visual-checkpoint:model.pt", (1009, 2017))

        passed, evidence = generalization_provenance(training, holdout)

        self.assertFalse(passed)
        self.assertFalse(evidence["checks"]["sameContentAddressedPolicy"])
        self.assertFalse(evidence["checks"]["disjointEvaluationSeeds"])

    def test_accepts_same_holdout_suite_with_head_to_head_suffix(self):
        holdout = self.report(self.sha_label, (1009, 2017))
        race = self.report(
            self.sha_label + "+head-to-head",
            (1009, 2017),
            scenario="head-to-head:targetSpeed=1.25",
        )

        passed, evidence = head_to_head_provenance(holdout, race)

        self.assertTrue(passed)
        self.assertTrue(all(evidence["checks"].values()))

    def test_rejects_different_checkpoint_in_head_to_head_report(self):
        holdout = self.report(self.sha_label, (1009, 2017))
        race = self.report(
            "visual-checkpoint:other.pt:sha256=" + "b" * 64 + "+head-to-head",
            (1009, 2017),
            scenario="head-to-head:targetSpeed=1.25",
        )

        passed, evidence = head_to_head_provenance(holdout, race)

        self.assertFalse(passed)
        self.assertFalse(evidence["checks"]["sameCheckpointLabel"])


class GoalGateCliTests(unittest.TestCase):
    def test_launcher_passes_signed_opponent_offset_to_both_race_runs(self):
        launcher = Path(__file__).with_name("run_goal_gate.sh").resolve()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "scripts").mkdir()
            # Capture the actual child-process environment and player argv.
            # No Unity process or checkpoint inference is needed for this wiring test.
            visual = root / "scripts/run_visual_evaluation.sh"
            visual.write_text(
                "#!/usr/bin/env python3\n"
                "import json, os\n"
                "from pathlib import Path\n"
                "if os.environ.get('PHYSICAL_AI_HEAD_TO_HEAD') == '1':\n"
                "    Path('visual-offset.json').write_text(json.dumps(os.environ['PHYSICAL_AI_OPPONENT_LATERAL_OFFSET']))\n",
                encoding="utf-8",
            )
            visual.chmod(0o755)
            player = root / "player"
            player.write_text(
                "#!/usr/bin/env python3\n"
                "import json, sys\n"
                "from pathlib import Path\n"
                "Path('player-args.json').write_text(json.dumps(sys.argv[1:]))\n",
                encoding="utf-8",
            )
            player.chmod(0o755)
            (root / "scripts/evaluate_goal_metrics.py").write_text("", encoding="utf-8")
            checkpoint = root / "fixture.pt"
            checkpoint.touch()
            environment = {key: value for key, value in os.environ.items()
                           if not key.startswith("PHYSICAL_AI_")}
            environment.update(PHYSICAL_AI_PLAYER=str(player), PHYSICAL_AI_PYTHON=sys.executable)
            for override, expected in ((None, "-0.28"), ("0.15", "0.15")):
                with self.subTest(override=override):
                    if override is not None:
                        environment["PHYSICAL_AI_OVERTAKE_OPPONENT_LATERAL_OFFSET"] = override
                    result = subprocess.run(
                        ["bash", str(launcher), str(checkpoint), str(root / "output"), "2"],
                        cwd=root, env=environment, capture_output=True, text=True, check=False,
                    )
                    self.assertEqual(0, result.returncode, result.stderr)
                    self.assertEqual(expected, json.loads((root / "visual-offset.json").read_text()))
                    arguments = json.loads((root / "player-args.json").read_text())
                    self.assertEqual(expected, arguments[arguments.index("-physicalAiOpponentLateralOffset") + 1])

    def test_complete_content_addressed_report_set_passes(self):
        label = "visual-checkpoint:model.pt:sha256=" + "c" * 64
        training_seeds = list(range(1, 9))
        holdout_seeds = list(range(1009, 1017))

        def report(policy_label, seeds, scenario="solo", captured=100, withheld=0):
            return {
                "policyLabel": policy_label,
                "evaluationScenario": scenario,
                "observationContract": "relative-course-waypoints+speed+visibility",
                "cameraFramesCaptured": captured,
                "cameraFramesWithheld": withheld,
                "episodeCount": len(seeds),
                "completionRate": 0.9,
                "meanCompletedLapTimeSeconds": 10.0,
                "opponentCollisionRate": 0.0,
                "egoOvertakeRate": 0.5,
                "opponentOvertakeRate": 0.0,
                "episodes": [
                    {
                        "courseSeed": seed,
                        "hazardWasSpawned": True,
                        "blindCornerHazardCollisionCount": 0,
                    }
                    for seed in seeds
                ],
            }

        training = report(label, training_seeds)
        holdout = report(label, holdout_seeds)
        blackout = report(label + "+image-blackout", holdout_seeds, captured=0, withheld=100)
        for episode in blackout["episodes"]:
            episode["blindCornerHazardCollisionCount"] = 1
        head_to_head = report(
            label + "+head-to-head",
            holdout_seeds,
            scenario="head-to-head:targetSpeed=1.25",
        )
        race = {"opponentCollisionRate": 0.25}

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            paths = {}
            for name, value in {
                "training": training,
                "holdout": holdout,
                "head-to-head": head_to_head,
                "blind": blackout,
                "race": race,
            }.items():
                paths[name] = root / f"{name}.json"
                paths[name].write_text(json.dumps(value), encoding="utf-8")
            output = root / "goal-gate.json"
            result = subprocess.run(
                [
                    sys.executable,
                    str(Path(__file__).with_name("evaluate_goal_metrics.py")),
                    "--training", str(paths["training"]),
                    "--holdout", str(paths["holdout"]),
                    "--head-to-head", str(paths["head-to-head"]),
                    "--blind-baseline", str(paths["blind"]),
                    "--race-baseline", str(paths["race"]),
                    "--output", str(output),
                ],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(0, result.returncode, result.stderr + result.stdout)
            gate = json.loads(output.read_text(encoding="utf-8"))
            self.assertTrue(gate["passed"])
            self.assertTrue(all(gate["checks"].values()))


if __name__ == "__main__":
    unittest.main()
