# Physical AI data collection

This page defines the two external datasets needed to complete the Physical AI
goal. Synthetic `reference-*` recordings are useful only to smoke-test the
pipeline; they must not be mixed into human demonstrations or used as sim-to-real
evidence.

## Human teleoperation demonstrations

Run the interactive player. Manual driving is enabled by default: `W`/up
accelerates, `S`/down brakes, and `A`/`D` (or left/right) steers directly. The
controls are stateless: releasing a steering key centers it, and releasing W
coasts. Press `F8` to start or stop recording; it never disables steering.

Collect separate episodes for nominal laps, braking before blind corners,
hazard avoidance, recovery after a poor line, overtaking, and defensive lines.
Do not use the fixed holdout seeds for training. Keep the generated `teleop-*`
JSONL files together; the recorder stores camera JPEGs and observations in every
frame that has an image.

For the nominal subset, force hazards off and write directly to a labelled directory:

```bash
scripts/run_human_nominal_capture.sh Build/teleop/human-nominal-001
scripts/prepare_human_nominal_dataset.sh Build/teleop/human-nominal-001 \
  Build/camera-demos/human-nominal-001
```

```bash
python3 scripts/export_camera_demonstrations.py teleop-*.jsonl --output Build/camera-demos/human-001
../.physical-ai-venv/bin/python scripts/train_visual_bc.py \
  Build/camera-demos/human-001/manifest.jsonl \
  --output Build/training/human-bc-001/physical-ai-visual-bc.pt --device cuda
```

Evaluate the resulting checkpoint on the fixed suites before RL fine tuning.
The final report must preserve the training and holdout JSON files rather than
using the training recording as evidence of generalisation.

### Head-to-head demonstrations

Use the dedicated launcher for overtaking and defensive examples. It writes only the human
`teleop-*` files to the chosen directory and tags every frame with
`human-head-to-head-overtake`; this preserves the distinction from solo drives and synthetic
reference rollouts.

```bash
scripts/run_human_head_to_head_capture.sh Build/teleop/human-head-to-head-001
scripts/prepare_human_head_to_head_dataset.sh Build/teleop/human-head-to-head-001 \
  Build/camera-demos/human-head-to-head-001
```

Drive several collision-free passes, safe braking approaches, recoveries and defensive lines.
Use only training-distribution seeds for these demonstrations; retain separate holdout suites for
evaluation. The launcher starts a 1.25 m/s opponent 1.5 m ahead and 0.28 m to the side. A
deterministic ±0.08 m, 0.5 Hz lateral motion supplies approach/blocking examples while remaining
inside the corridor; each reset begins at the same phase for reproducibility.
Ordinary manual and blind-corner sessions keep the opponent disabled; only
`-physicalAiHeadToHead` activates and configures it before the first course reset.

### Blind-corner safety demonstrations

The safety launcher forces hazard probability to one and labels every recorded frame separately
from nominal and race data. Record early braking before visibility is lost, sightline-preserving
approaches, avoidance and recovery; do not keep only successful examples.

```bash
scripts/run_human_blind_corner_capture.sh Build/teleop/human-blind-corner-001
scripts/prepare_human_blind_corner_dataset.sh Build/teleop/human-blind-corner-001 \
  Build/camera-demos/human-blind-corner-001
```

Both specialized dataset commands reject any frame whose scenario label does not match the
requested capture. Combine their validated manifests with nominal human demonstrations only in a
new, provenance-preserving training run; never relabel or mix synthetic `reference-*` samples.

```bash
python3 scripts/compose_human_visual_dataset.py \
  --nominal Build/camera-demos/human-nominal-001/manifest.jsonl \
  --blind-corner Build/camera-demos/human-blind-corner-001/manifest.jsonl \
  --head-to-head Build/camera-demos/human-head-to-head-001/manifest.jsonl \
  --output Build/camera-demos/human-combined-001
```

The composer requires all three human-only labels, verifies one observation width and existing
images, copies the images into a self-contained dataset, and writes `dataset-summary.json` with
per-scenario counts. Use its combined manifest for BC; keep the three source datasets unchanged.

## Real-vehicle cornering telemetry

Use steady-state cornering segments only: constant steering and speed, with no
launch, braking, collision, wheel lift, or transient steering input. The CSV
must have one row per synchronized sample and these SI-unit columns:

| Column | Unit | Meaning |
| --- | --- | --- |
| `speed_mps` | m/s | vehicle longitudinal speed magnitude |
| `yaw_rate_radps` | rad/s | body yaw rate, signed or unsigned |
| `bank_degrees` | degrees | local road bank; use `0` on a flat course |

Collect multiple speeds and both turn directions on the surface used for racing.
The converter rejects non-finite/non-positive values and banks outside ±30°, removes samples
below 0.5 m/s², and requires at least 30 remaining steady-state samples by default. Preserve the
raw CSV beside the converted asset and record vehicle, tires, battery state, surface, weather and
a stable run identifier in the run notes. The generated JSON records that run identifier, raw
sample count and accepted samples; do not edit it manually.

```bash
python3 scripts/convert_telemetry_csv.py real-cornering.csv \
  --run-id tt02-asphalt-dry-001 \
  --output Assets/PhysicalAi/Calibration/real-cornering.json
```

Assign that JSON as the `TelemetryCalibrationApplier` asset, then capture the
generated calibration report. Its `sampleCount` and `lateralAccelerationRmse` refer only to the
accepted steady-state samples, while its `runId`, `rawSampleCount` and
`rejectedLowAccelerationSampleCount` make the source and filtering auditable.
Acceptance requires a documented RMSE threshold and a before/after comparison against the same
real run; friction estimation alone does not establish full suspension or drivetrain fidelity.
