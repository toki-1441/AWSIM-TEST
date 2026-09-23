#!/usr/bin/env python3

import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from scripts import validate_manual_streaming


SCRIPT = Path(__file__).with_name("validate_manual_streaming.py")


def transition(
    kind: str,
    corner: str = "none",
    forward: int = 140,
    source: str = "manual-teleoperation",
) -> str:
    return (
        f"Physical AI streaming transition ({kind}) is continuous: "
        "position error 0.000000 m, rotation 0.0000°, velocity 0.000000 m/s, "
        f"angular velocity 0.000000 rad/s; source {source}, "
        "control input (0.750000,0.000000,0.250000)->(0.750000,0.000000,0.250000), "
        "control delta 0.000000, "
        "model planar-single-track, actual steering 0.100000->0.100000 rad, "
        "axle force delta 0.000000 N, dynamic weight delta 0.000000; "
        f"retained 364, forward {forward}, chunks 10, appended corner {corner}."
    )


class ValidateManualStreamingTests(unittest.TestCase):
    def run_validator(self, lines: list[str]) -> tuple[subprocess.CompletedProcess[str], dict]:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            log = root / "player.log"
            report = root / "report.json"
            log.write_text("\n".join(lines) + "\n", encoding="utf-8")
            result = subprocess.run(
                ["python3", str(SCRIPT), str(log), "--output", str(report)],
                check=False,
                capture_output=True,
                text=True,
            )
            return result, json.loads(report.read_text(encoding="utf-8"))

    def test_accepts_complete_instrumented_run(self) -> None:
        result, report = self.run_validator(
            [
                transition("AppendAndRetire", "left"),
                transition("AppendAndRetire", "right"),
                transition("AppendAndRetire", "left"),
                transition("HistoryTrim"),
                transition("FloatingOriginRebase"),
            ]
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(report["instrumentedStreamingGatePassed"])
        self.assertEqual("pending", report["manualVisualAssessment"])
        self.assertEqual(3, report["counts"]["appendAndRetire"])

    def test_rejects_assisted_run_as_physical_model_evidence(self) -> None:
        lines = [transition("AppendAndRetire", "left"), transition("AppendAndRetire", "right"),
                 transition("AppendAndRetire", "left"), transition("HistoryTrim"),
                 transition("FloatingOriginRebase"), "Physical AI manual vehicle model: planar-no-slip-assist."]
        result, report = self.run_validator(lines)
        self.assertEqual(1, result.returncode)
        self.assertIn("no-slip assisted driving", " ".join(report["failures"]))
        result, report = self.run_validator([line.replace("planar-single-track", "planar-no-slip-assist") for line in lines])
        self.assertEqual(1, result.returncode)
        self.assertEqual("planar-no-slip-assist", report["vehicleModel"])
        self.assertEqual(3, report["counts"]["appendAndRetire"])

    def test_rejects_missing_lifecycle_and_variety(self) -> None:
        result, report = self.run_validator([transition("AppendAndRetire", "left")])
        self.assertEqual(1, result.returncode)
        self.assertFalse(report["instrumentedStreamingGatePassed"])
        self.assertGreaterEqual(len(report["failures"]), 3)

    def test_rejects_old_or_incomplete_transition_schema(self) -> None:
        result, report = self.run_validator(
            [
                "Physical AI streaming transition (AppendAndRetire) is continuous: "
                "position error 0.000000 m, rotation 0.0000°, velocity 0.000000 m/s, "
                "angular velocity 0.000000 rad/s; retained 364, forward 140, chunks 10, "
                "appended corner left."
            ]
        )
        self.assertEqual(1, result.returncode)
        self.assertIn("full control/planar-model telemetry", " ".join(report["failures"]))

    def test_rejects_steering_axle_and_blend_changes(self) -> None:
        for before, after in (
            ("0.100000->0.100000 rad", "0.100000->0.200000 rad"),
            ("axle force delta 0.000000 N", "axle force delta 0.010000 N"),
            ("dynamic weight delta 0.000000", "dynamic weight delta 0.100000"),
            ("position error 0.000000 m", "position error 0.100000 m"),
            ("rotation 0.0000°", "rotation 1.0000°"),
            ("velocity 0.000000 m/s", "velocity 1.000000 m/s"),
            ("->(0.750000,0.000000,0.250000)", "->(0.750000,0.000000,0.750000)"),
        ):
            with self.subTest(field=before):
                lines = [transition("AppendAndRetire", "left"), transition("AppendAndRetire", "right"),
                         transition("AppendAndRetire", "left"), transition("HistoryTrim"),
                         transition("FloatingOriginRebase")]
                lines[0] = lines[0].replace(before, after)
                result, report = self.run_validator(lines)
                self.assertEqual(1, result.returncode)
                self.assertFalse(report["instrumentedStreamingGatePassed"])

    def test_rejects_legacy_wheel_telemetry_as_planar_evidence(self) -> None:
        line = transition("AppendAndRetire", "left").replace(
            "model planar-single-track, actual steering 0.100000->0.100000 rad, "
            "axle force delta 0.000000 N, dynamic weight delta 0.000000; ",
            "ground mask F->F, suspension compression 0.000000 m, suspension velocity 0.000000 m/s; ")
        result, report = self.run_validator([line])
        self.assertEqual(1, result.returncode)
        self.assertIn("full control/planar-model telemetry", " ".join(report["failures"]))

    def test_rejects_discontinuity_even_with_complete_transitions(self) -> None:
        lines = [
            transition("AppendAndRetire", "left"),
            transition("AppendAndRetire", "right"),
            transition("AppendAndRetire", "left"),
            transition("HistoryTrim"),
            transition("FloatingOriginRebase"),
            "Physical AI streaming discontinuity (AppendAndRetire): position error 0.1 m",
        ]
        result, report = self.run_validator(lines)
        self.assertEqual(1, result.returncode)
        self.assertIn("streaming discontinuity", " ".join(report["failures"]))

    def test_rejects_exhausted_horizon(self) -> None:
        result, report = self.run_validator(
            [
                transition("AppendAndRetire", "left", 0),
                transition("AppendAndRetire", "right"),
                transition("AppendAndRetire", "left"),
                transition("HistoryTrim"),
                transition("FloatingOriginRebase"),
            ]
        )
        self.assertEqual(1, result.returncode)
        self.assertIn("forward waypoint horizon", " ".join(report["failures"]))

    def test_rejects_reference_driver_as_manual_evidence(self) -> None:
        result, report = self.run_validator(
            [
                transition("AppendAndRetire", "left", source="reference-planner"),
                transition("AppendAndRetire", "right", source="reference-planner"),
                transition("AppendAndRetire", "left", source="reference-planner"),
                transition("HistoryTrim", source="reference-planner"),
                transition("FloatingOriginRebase", source="reference-planner"),
            ]
        )
        self.assertEqual(1, result.returncode)
        self.assertIn("manual-teleoperation", " ".join(report["failures"]))

    def test_requires_assessment_and_video_together(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            log = root / "player.log"
            report = root / "report.json"
            assessment = root / "assessment.json"
            log.write_text(transition("AppendAndRetire", "left"), encoding="utf-8")
            assessment.write_text("{}", encoding="utf-8")
            result = subprocess.run(
                [
                    "python3",
                    str(SCRIPT),
                    str(log),
                    "--output",
                    str(report),
                    "--operator-assessment",
                    str(assessment),
                ],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(1, result.returncode)
            self.assertIn("must be supplied together", result.stderr)

    def test_completed_assessment_and_video_can_close_manual_gate(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            assessment = root / "assessment.json"
            video = root / "manual-drive.mp4"
            assessment.write_text(
                json.dumps(
                    {
                        "operator": "test-operator",
                        "completedUtc": "2026-09-05T09:30:00Z",
                        "notes": "reviewed fixture",
                        "checks": {
                            name: True
                            for name in validate_manual_streaming.REQUIRED_OPERATOR_CHECKS
                        },
                    }
                ),
                encoding="utf-8",
            )
            video.write_bytes(b"test-video-fixture")
            report = {
                "instrumentedStreamingGatePassed": True,
                "manualVisualAssessment": "pending",
                "failures": [],
            }
            with mock.patch.object(
                validate_manual_streaming, "probe_video", return_value=(31.0, None)
            ):
                validate_manual_streaming.add_manual_assessment(
                    report, assessment, video, 30.0
                )
            self.assertEqual("passed", report["manualVisualAssessment"])
            self.assertTrue(report["overallManualStreamingGatePassed"])
            self.assertEqual(31.0, report["manualDriveVideo"]["durationSeconds"])


if __name__ == "__main__":
    unittest.main()
