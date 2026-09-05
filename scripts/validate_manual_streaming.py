#!/usr/bin/env python3
"""Validate the instrumented half of a manual streamed-course drive.

This deliberately does not infer human handling or visual quality from a log.
It produces a durable report for the state-continuity requirements and leaves
the operator/visual assessment explicitly pending.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from collections import Counter
from datetime import datetime, timedelta, timezone
from pathlib import Path


TRANSITION_PATTERN = re.compile(
    r"Physical AI streaming transition \((?P<kind>AppendAndRetire|HistoryTrim|FloatingOriginRebase)\) "
    r"is continuous: position error (?P<position>[0-9.eE+-]+) m, "
    r"rotation (?P<rotation>[0-9.eE+-]+)°, velocity (?P<velocity>[0-9.eE+-]+) m/s, "
    r"angular velocity (?P<angular>[0-9.eE+-]+) rad/s; "
    r"source (?P<source>[A-Za-z0-9+_.-]+), "
    r"control input \((?P<throttle_before>[0-9.eE+-]+),(?P<brake_before>[0-9.eE+-]+),(?P<steering_before>[0-9.eE+-]+)\)"
    r"->\((?P<throttle_after>[0-9.eE+-]+),(?P<brake_after>[0-9.eE+-]+),(?P<steering_after>[0-9.eE+-]+)\), "
    r"control delta (?P<control_delta>[0-9.eE+-]+), "
    r"ground mask (?P<ground_before>[0-9A-Fa-f]+)->(?P<ground_after>[0-9A-Fa-f]+), "
    r"suspension compression (?P<compression>[0-9.eE+-]+) m, "
    r"suspension velocity (?P<suspension_velocity>[0-9.eE+-]+) m/s; "
    r"retained (?P<retained>\d+), forward (?P<forward>\d+), chunks (?P<chunks>\d+), "
    r"appended corner (?P<corner>left|right|none)\."
)

REQUIRED_OPERATOR_CHECKS = (
    "manualControlResponsive",
    "continuousGroundSupport",
    "boundaryCollisionRetainedVehicle",
    "noPersistentPumpingBouncingOrSnagging",
    "noGenerationBoundaryKick",
    "noVisibleGapOverlapCrossingOrFlicker",
    "operatorCameraVisible",
    "sensorCameraVisible",
    "continuousIlluminatedForwardRoad",
    "bothCurvesAndStraightsObserved",
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Validate streaming transition telemetry from a manual built-player run."
    )
    parser.add_argument("log", type=Path, help="Unity Player.log captured by the manual launcher")
    parser.add_argument("--output", type=Path, required=True, help="JSON report path")
    parser.add_argument("--minimum-appends", type=int, default=3)
    parser.add_argument(
        "--operator-assessment",
        type=Path,
        help="completed operator-assessment.json; requires --video",
    )
    parser.add_argument("--video", type=Path, help="captured manual-drive video; requires --operator-assessment")
    parser.add_argument("--minimum-video-seconds", type=float, default=30.0)
    return parser.parse_args()


def validate(log_path: Path, minimum_appends: int) -> dict:
    raw = log_path.read_bytes()
    text = raw.decode("utf-8", errors="replace")
    matches = list(TRANSITION_PATTERN.finditer(text))
    all_transition_lines = [
        line for line in text.splitlines() if "Physical AI streaming transition (" in line
    ]
    kinds = Counter(match.group("kind") for match in matches)
    corners = Counter(
        match.group("corner")
        for match in matches
        if match.group("kind") == "AppendAndRetire"
    )
    failures: list[str] = []

    if "Physical AI streaming discontinuity" in text:
        failures.append("the player reported a streaming discontinuity")
    if len(matches) != len(all_transition_lines):
        failures.append(
            f"{len(all_transition_lines) - len(matches)} transition line(s) lack the full control/contact telemetry contract"
        )
    if kinds["AppendAndRetire"] < minimum_appends:
        failures.append(
            f"only {kinds['AppendAndRetire']} append transition(s) were recorded; {minimum_appends} required"
        )
    if kinds["HistoryTrim"] < 1:
        failures.append("no history retirement/trim transition was recorded")
    if kinds["FloatingOriginRebase"] < 1:
        failures.append("no floating-origin rebase transition was recorded")
    if corners["left"] < 1 or corners["right"] < 1:
        failures.append("the append transitions did not include both left and right curves")
    if matches and min(int(match.group("forward")) for match in matches) < 1:
        failures.append("a transition exhausted the forward waypoint horizon")
    if "Could not generate a non-intersecting curved streaming chunk" in text:
        failures.append("streaming generation exhausted its valid-candidate retry budget")
    command_sources = Counter(match.group("source") for match in matches)
    if matches and set(command_sources) != {"manual-teleoperation"}:
        failures.append(
            "all transitions must identify source manual-teleoperation; observed "
            + ", ".join(sorted(command_sources))
        )

    for index, match in enumerate(matches, start=1):
        control_delta = float(match.group("control_delta"))
        if control_delta > 0.000001:
            failures.append(f"transition {index} changed a control input")
        if match.group("ground_before").lower() != match.group("ground_after").lower():
            failures.append(f"transition {index} changed the grounded-wheel mask")
        if float(match.group("compression")) > 0.000001:
            failures.append(f"transition {index} changed suspension compression")
        if float(match.group("suspension_velocity")) > 0.000001:
            failures.append(f"transition {index} changed suspension velocity")

    maxima = {
        "positionErrorMeters": max((float(match.group("position")) for match in matches), default=None),
        "rotationErrorDegrees": max((float(match.group("rotation")) for match in matches), default=None),
        "linearVelocityDeltaMetersPerSecond": max((float(match.group("velocity")) for match in matches), default=None),
        "angularVelocityDeltaRadiansPerSecond": max((float(match.group("angular")) for match in matches), default=None),
        "controlInputDelta": max(
            (
                max(
                    abs(float(match.group("throttle_after")) - float(match.group("throttle_before"))),
                    abs(float(match.group("brake_after")) - float(match.group("brake_before"))),
                    abs(float(match.group("steering_after")) - float(match.group("steering_before"))),
                    float(match.group("control_delta")),
                )
                for match in matches
            ),
            default=None,
        ),
        "suspensionCompressionDeltaMeters": max(
            (float(match.group("compression")) for match in matches), default=None
        ),
        "suspensionVelocityDeltaMetersPerSecond": max(
            (float(match.group("suspension_velocity")) for match in matches), default=None
        ),
    }
    return {
        "schemaVersion": 1,
        "createdUtc": datetime.now(timezone.utc).isoformat(),
        "sourceLog": str(log_path.resolve()),
        "sourceLogSha256": hashlib.sha256(raw).hexdigest(),
        "instrumentedStreamingGatePassed": not failures,
        "manualVisualAssessment": "pending",
        "note": (
            "A passing instrumentation report does not prove human driveability, boundary retention, "
            "or absence of visible gaps, crossings, and flicker; review captured media and complete the operator checklist."
        ),
        "counts": {
            "transitions": len(matches),
            "appendAndRetire": kinds["AppendAndRetire"],
            "historyTrim": kinds["HistoryTrim"],
            "floatingOriginRebase": kinds["FloatingOriginRebase"],
            "appendedLeft": corners["left"],
            "appendedRight": corners["right"],
            "appendedStraight": corners["none"],
            "commandSources": dict(sorted(command_sources.items())),
        },
        "minimumForwardWaypoints": min(
            (int(match.group("forward")) for match in matches), default=None
        ),
        "maximumStateDeltas": maxima,
        "failures": failures,
    }


def probe_video(video_path: Path) -> tuple[float | None, str | None]:
    if not video_path.is_file() or video_path.stat().st_size == 0:
        return None, f"manual-drive video does not exist or is empty: {video_path}"
    try:
        result = subprocess.run(
            [
                "ffprobe",
                "-v",
                "error",
                "-select_streams",
                "v:0",
                "-show_entries",
                "format=duration",
                "-of",
                "default=noprint_wrappers=1:nokey=1",
                str(video_path),
            ],
            check=False,
            capture_output=True,
            text=True,
        )
    except FileNotFoundError:
        return None, "ffprobe is required to verify the manual-drive video"
    if result.returncode != 0:
        return None, f"ffprobe could not read the manual-drive video: {result.stderr.strip()}"
    try:
        duration = float(result.stdout.strip())
    except ValueError:
        return None, "ffprobe did not report a valid manual-drive video duration"
    return duration, None


def add_manual_assessment(
    report: dict,
    assessment_path: Path,
    video_path: Path,
    minimum_video_seconds: float,
) -> None:
    assessment_failures: list[str] = []
    try:
        assessment = json.loads(assessment_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        assessment = {}
        assessment_failures.append(f"operator assessment could not be read: {error}")

    if not str(assessment.get("operator", "")).strip():
        assessment_failures.append("operator assessment has no operator name or identifier")
    completed_utc = str(assessment.get("completedUtc", "")).strip()
    if not completed_utc:
        assessment_failures.append("operator assessment has no completion timestamp")
    else:
        try:
            completed_time = datetime.fromisoformat(completed_utc.replace("Z", "+00:00"))
            if completed_time.tzinfo is None or completed_time.utcoffset() != timedelta(0):
                raise ValueError("timestamp is not UTC")
        except ValueError as error:
            assessment_failures.append(f"operator completion timestamp is not ISO-8601 with an offset: {error}")
    checks = assessment.get("checks", {})
    for name in REQUIRED_OPERATOR_CHECKS:
        if checks.get(name) is not True:
            assessment_failures.append(f"operator check is not affirmed: {name}")

    duration, video_error = probe_video(video_path)
    if video_error:
        assessment_failures.append(video_error)
    elif duration is not None and duration < minimum_video_seconds:
        assessment_failures.append(
            f"manual-drive video is {duration:.3f} s; at least {minimum_video_seconds:.3f} s is required"
        )

    video_bytes = video_path.read_bytes() if video_error is None else b""
    report["manualVisualAssessment"] = "passed" if not assessment_failures else "failed"
    report["operatorAssessment"] = {
        "source": str(assessment_path.resolve()),
        "sha256": hashlib.sha256(assessment_path.read_bytes()).hexdigest()
        if assessment_path.is_file()
        else None,
        "operator": assessment.get("operator"),
        "completedUtc": assessment.get("completedUtc"),
        "notes": assessment.get("notes", ""),
        "checks": checks,
    }
    report["manualDriveVideo"] = {
        "source": str(video_path.resolve()),
        "sha256": hashlib.sha256(video_bytes).hexdigest() if video_bytes else None,
        "durationSeconds": duration,
        "minimumDurationSeconds": minimum_video_seconds,
    }
    report["failures"].extend(assessment_failures)
    report["overallManualStreamingGatePassed"] = (
        report["instrumentedStreamingGatePassed"] and not assessment_failures
    )


def main() -> int:
    args = parse_args()
    if args.minimum_appends < 1:
        raise SystemExit("--minimum-appends must be at least 1")
    if args.minimum_video_seconds <= 0:
        raise SystemExit("--minimum-video-seconds must be positive")
    if (args.operator_assessment is None) != (args.video is None):
        raise SystemExit("--operator-assessment and --video must be supplied together")
    if not args.log.is_file():
        raise SystemExit(f"log does not exist: {args.log}")
    report = validate(args.log, args.minimum_appends)
    report["overallManualStreamingGatePassed"] = False
    if args.operator_assessment is not None:
        add_manual_assessment(
            report,
            args.operator_assessment,
            args.video,
            args.minimum_video_seconds,
        )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    if not report["instrumentedStreamingGatePassed"] or report["manualVisualAssessment"] == "failed":
        for failure in report["failures"]:
            print(f"FAIL: {failure}", file=sys.stderr)
        print(f"Instrumentation report written to {args.output}", file=sys.stderr)
        return 1
    print(f"Instrumented streaming gate passed; report written to {args.output}")
    if report["manualVisualAssessment"] == "pending":
        print("Manual visual/handling assessment remains pending.")
    else:
        print("Manual visual/handling assessment and video are attached; overall gate passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
