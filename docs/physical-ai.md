# Physical AI

初めて利用する方は [日本語の利用マニュアル](physical-ai-user-guide.md) を参照してください。

Interactive manual driving currently defaults to a temporary **no-slip handling aid**.
Use `-physicalAiAllowSlip` to restore tire dynamics. Assisted drives cannot certify physical
handling, but may record explicitly labelled simulator data. The active goal is
[operator collection and RL workflows](physical-ai-operator-workflows.md).

A scale autonomous-racing stack layered on AWSIM, targeting the Tamiya class of
**MAGP (Minicar Autonomous Grand-Prix)**, the Japanese localisation of RoboRacer / F1TENTH.
It adds procedurally generated, physically feasible courses, a combined-slip vehicle model,
a framework-neutral planning interface, behaviour cloning, head-to-head racing, blind-corner
safety, and a fixed holdout evaluation suite.

Upstream AWSIM code under `Assets/Awsim/`, `Assets/Ros2ForUnity/` and `Assets/RGLUnityPlugin/`
is untouched; everything described here lives under `Assets/PhysicalAi/`.

## Layout

Each `Assets/PhysicalAi/<Area>/` folder is its own assembly with a matching root namespace,
and the references form a DAG:

```
VehicleDynamics ──┬── Randomization ── Calibration ──┐
Course ──┬─────────┴── Racing ─┐                     │
         ├── Safety ───────────┴─────────────────► Planning ◄──┘
         └── Evaluation (← Course, Planning)
Sensors  (standalone)
```

`Assets/PhysicalAi/Ros2/` deliberately has **no** assembly definition. Upstream AWSIM has no
asmdefs, so `Awsim.Common` and `ROS2` compile into `Assembly-CSharp`, which an assembly cannot
reference. That folder is therefore the single seam where the physics and planning layer meets
Autoware messages, and tests reach its components by string type name.

## Episode loop

`PhysicalAiEpisodeOrchestrator` is the runtime boundary, driven from `FixedUpdate`:

1. `ResetEpisode(seed)` generates the centreline, applies domain randomisation and telemetry
   calibration, builds collider-backed road segments, spawns hazards, and opens a recording.
2. Each step encodes an observation: ego-local waypoints, camera-limited safe speed, and
   opponent kinematics.
3. A command is chosen by the first available of `CommandProvider` (an external ML, Python or
   ROS2 bridge), `BehaviorCloningPolicy`, then `ReferencePlanningPolicy`.
4. `PlanningLowLevelController` converts the command to throttle, brake and steering, and the
   recorder logs `(observation, command, reward)`.
5. The episode ends on completion, course departure, or collision with a hazard or opponent.

The step reward combines progress, collision/departure and unsafe-speed penalties with one-shot
arc-length pass events: a verified ego overtake adds 5, while being passed subtracts 3. Collision
still ends the episode with the larger safety penalty, so contact cannot be traded for a pass.

The planning and actuation split is load-bearing: a learned policy only ever emits
`PlanningCommand { DesiredSpeed, DesiredLateralOffsetNormalized }`, and a classical controller
owns actuation. `PlanningObservation` is a flat `float[]` —
`[speed, cameraSafeSpeed, opponentLongitudinal, opponentLateral]` followed by five floats per
waypoint: `[localX, localZ, curvature, maximumSafeSpeed, auxiliary]`. The first auxiliary value
is opponent-relative longitudinal speed, the second is opponent-relative lateral speed, and the
remaining values are zero. This fills formerly unused slots without changing the vector width.
Changing that layout width invalidates every recorded dataset and trained model, so
`BehaviorCloningPolicy` falls back to the reference policy on a feature-count mismatch rather
than throwing.

Course generation, domain randomisation and hazard placement are all seeded from the single
episode seed, so an episode is reproducible from it alone. `HoldoutCourseSuite.Seeds` is a
fixed evaluation set that must stay disjoint from training seeds.

## Commands

The Unity Editor must be closed for any batch invocation.

```bash
UNITY=~/Unity/Hub/Editor/6000.0.61f1/Editor/Unity

# --clean removes only AWSIM.x86_64 and AWSIM_Data; training/evaluation artifacts
# below Build/ are preserved.
scripts/build_linux.sh --clean                       # Linux player -> Build/AWSIM.x86_64

$UNITY -batchmode -projectPath . -runTests -testPlatform EditMode \
  -testResults Logs/editmode.xml -logFile /dev/stdout        # add -testFilter for one class

$UNITY -batchmode -nographics -quit -projectPath . \
  -executeMethod PhysicalAi.Editor.PhysicalAiTrainingSceneCreator.CreateOrReplace \
  -logFile /dev/stdout                               # rebuild the training scene

$UNITY -batchmode -nographics -quit -projectPath . \
  -executeMethod PhysicalAi.Editor.PhysicalAiRuntimeSmokeTest.Run -logFile /dev/stdout

python3 scripts/train_bc.py <demos>/*.jsonl --output Assets/PhysicalAi/Models/bc-policy.json

python3 scripts/export_camera_demonstrations.py <demos>/*.jsonl --output Build/camera-demos
# Export only human F8 recordings and reject missing camera-command pairs.
PHYSICAL_AI_PYTHON=../.physical-ai-venv/bin/python \
  scripts/prepare_human_visual_bc_dataset.sh path/to/human-recordings Build/camera-demos/human-001
# Labelled nominal subset (hazards and opponents disabled).
scripts/run_human_nominal_capture.sh Build/teleop/human-nominal-001
scripts/prepare_human_nominal_dataset.sh Build/teleop/human-nominal-001 \
  Build/camera-demos/human-nominal-001
# Human-operated overtake/defence examples, saved directly to the supplied
# directory with a scenario label (F8 starts/stops each recording).
scripts/run_human_head_to_head_capture.sh Build/teleop/human-head-to-head-001
scripts/prepare_human_head_to_head_dataset.sh Build/teleop/human-head-to-head-001 \
  Build/camera-demos/human-head-to-head-001
scripts/run_human_blind_corner_capture.sh Build/teleop/human-blind-corner-001
scripts/prepare_human_blind_corner_dataset.sh Build/teleop/human-blind-corner-001 \
  Build/camera-demos/human-blind-corner-001

python3 scripts/rl_policy_server.py --port 5015     # single-environment REINFORCE trainer

# One-time setup for the camera-capable REINFORCE baseline (outside the repository):
python3 -m venv ../.physical-ai-venv
../.physical-ai-venv/bin/pip install -r scripts/requirements-vision-rl.txt
../.physical-ai-venv/bin/python scripts/train_visual_bc.py Build/camera-demos/manifest.jsonl --output Build/physical-ai-visual-bc.pt
../.physical-ai-venv/bin/python scripts/train_vision_rl.py --port 5015
# Evaluate a saved visual checkpoint through the same Unity bridge:
../.physical-ai-venv/bin/python scripts/serve_vision_policy.py Build/physical-ai-vision-reinforce.pt --port 5015

# In another terminal, run reproducible suite reports against that server. The
# evaluation flag loads the Physical AI scene even though the ordinary player
# continues to start at the AWSIM Autoware demo.
# Reports are written to the player persistent-data PhysicalAi/ directory.
Build/AWSIM.x86_64 -physicalAiRlPort 5015 -physicalAiEvaluation training
Build/AWSIM.x86_64 -physicalAiRlPort 5015 -physicalAiEvaluation holdout
# Optional for smoke tests only; normal reports retain the scene's 120 s limit.
Build/AWSIM.x86_64 -physicalAiRlPort 5015 -physicalAiEvaluation training -physicalAiMaximumEpisodeSeconds 2
# Record opt-in synthetic reference demonstrations (not labelled as human teleop).
Build/AWSIM.x86_64 -physicalAiEvaluation rollout -physicalAiEpisodeCount 8 -physicalAiMaximumEpisodeSeconds 30 -physicalAiRecordReferenceDemonstrations
# Inspect the primary endless-streaming route with the deterministic reference
# planner. This disables keyboard input; it is a geometry/physics observation
# aid, not learned-policy evidence. Streaming transition continuity is written
# to Player.log.
Build/AWSIM.x86_64 -physicalAiReferenceDriver
# Operator-driven streaming evidence. Drive with WASD/arrows until several chunks
# append and a floating-origin rebase occurs, then close the player. The script
# preserves Player.log and emits an instrumentation report. It accepts only
# manual-teleoperation transitions and checks pose/velocity, control input,
# actual steering, axle forces, model blend state, horizon, lifecycle and curve variety.
scripts/run_manual_streaming_validation.sh Build/validation/manual-streaming-001
# Record at least 30 seconds of the same run. Review it, fill the generated
# operator-assessment.json, and run the finalization command written to
# streaming-continuity-summary.txt. Only that paired report may pass the full
# manual gate; an automated reference-driver log is rejected.
# Save comparable run artifacts outside Unity's persistent-data default.
Build/AWSIM.x86_64 -physicalAiRlPort 5015 -physicalAiEvaluation holdout -physicalAiReportDirectory Build/evaluations/run-001
# Record body and two-axle diagnostics (schema 2) without changing BC/RL observations.
# HDRP player evaluation requires an available graphics display.
Build/AWSIM.x86_64 -physicalAiEvaluation holdout -physicalAiRecordDynamicsTelemetry \
  -physicalAiReportDirectory Build/evaluations/dynamics-001 -physicalAiQuitAfterEvaluation
# Heuristic reference-planner diagnostic only (planning ignores camera occlusion).
# This is not the learned-policy blind-corner goal baseline; the goal gate uses
# the same checkpoint with rendered frames withheld.
Build/AWSIM.x86_64 -physicalAiEvaluation holdout -physicalAiDisableVisibilitySafety -physicalAiReportDirectory Build/evaluations/blind-baseline

# Reproducibly evaluate one saved visual checkpoint on both distributions. This
# requires a graphical display: the script intentionally does not use batchmode,
# because a Null graphics device cannot produce camera JPEG observations. It
# automatically disables keyboard teleoperation, so the external policy — not
# zero manual input — owns low-level actuation.
# fails unless both reports record nonzero `cameraFramesCaptured` (or nonzero
# `cameraFramesWithheld` for the blackout ablation).
PHYSICAL_AI_PYTHON=../.physical-ai-venv/bin/python \
  scripts/run_visual_evaluation.sh Build/physical-ai-vision-reinforce.pt Build/evaluations/run-001

# Paired camera ablation: run the same checkpoint and evaluation seeds while
# withholding only camera JPEGs at the Unity-to-Python bridge. The generated
# reports are labelled `+image-blackout`; compare them with the normal run.
PHYSICAL_AI_PYTHON=../.physical-ai-venv/bin/python \
  PHYSICAL_AI_IMAGE_BLACKOUT=1 \
  scripts/run_visual_evaluation.sh Build/physical-ai-vision-reinforce.pt Build/evaluations/run-001-blackout

# Fine-tune a visual-BC checkpoint with visual REINFORCE, then evaluate the saved
# checkpoint.  Initialising from BC is the normal path: random-policy REINFORCE
# is only a bridge smoke test, not a meaningful driving baseline.
# This also requires a graphical display: the rollout script deliberately avoids
# batchmode so the trainer receives rendered camera JPEGs.
# The trainer keeps a cross-episode return baseline: it deliberately does not
# normalise each rollout independently, so timeout and collision penalties retain
# their learning signal. Each round uses a separate deterministic training-only
# seed batch; fixed training and holdout seeds remain evaluation-only.
# The default balanced curriculum cycles deterministic nominal, forced
# blind-corner-hazard and moving-opponent head-to-head rounds. Set
# PHYSICAL_AI_RL_CURRICULUM=nominal only for an explicitly labelled ablation.
# All runner output directories are canonicalized before they are passed to the
# player; Unity otherwise resolves relative -logFile paths beside the executable.
PHYSICAL_AI_PYTHON=../.physical-ai-venv/bin/python \
  PHYSICAL_AI_INITIAL_CHECKPOINT=Build/physical-ai-visual-bc.pt \
  PHYSICAL_AI_VISION_ARCHITECTURE=spatial-v2 \
  scripts/run_vision_reinforce_training.sh Build/training/run-001 100

# Produce normal, same-checkpoint image-blackout, head-to-head and reference-race
# reports plus the final gate result. Each report label includes the checkpoint's
# SHA-256. The gate rejects a different/unhashed policy, missing rendered/withheld
# frames, changed seed order, overlapping training/holdout seeds, or a non-solo
# generalisation report. Blind-corner safety compares only seed-matched episodes
# in which a hazard spawned and requires an exact paired significance test; a
# zero rate without hazard exposure is intentionally inconclusive. Hazard
# probability is 1 by default for this evaluation (override with
# PHYSICAL_AI_HAZARD_PROBABILITY); normal training remains probabilistic.
PHYSICAL_AI_PYTHON=../.physical-ai-venv/bin/python \
  scripts/run_goal_gate.sh Build/training/run-001/physical-ai-vision-reinforce.pt Build/evaluations/gate-001

# The gate's head-to-head probe starts a deliberately slower opponent 1.5 m
# ahead and 0.28 m to the side, with deterministic ±0.08 m lateral motion at
# 0.5 Hz. The suite can therefore measure a pass against a moving/blocking
# opponent without placing two collision boxes at the same point. Override only to evaluate a
# documented alternate scenario; solo reports are rejected as race evidence.
# PHYSICAL_AI_OVERTAKE_OPPONENT_SPEED=1.25
# PHYSICAL_AI_OVERTAKE_OPPONENT_INITIAL_PROGRESS_METERS=1.5
# PHYSICAL_AI_OVERTAKE_OPPONENT_LATERAL_OFFSET=-0.28
# PHYSICAL_AI_OVERTAKE_OPPONENT_LATERAL_AMPLITUDE=0.08
# PHYSICAL_AI_OVERTAKE_OPPONENT_LATERAL_FREQUENCY_HZ=0.5

# Each report records these resolved command-line settings in
# `evaluationScenario`; a solo or unlabelled report is rejected as race evidence.

python3 scripts/convert_telemetry_csv.py run.csv --output Assets/PhysicalAi/Calibration/real-run.json

python3 scripts/evaluate_goal_metrics.py --training train.json --holdout holdout.json \
  --head-to-head head-to-head.json --blind-baseline image-blackout.json \
  --race-baseline reference-race.json
```

The training scene is generated rather than hand-authored, so a component that the scene must
carry has to be wired in `PhysicalAiTrainingSceneCreator`; `PhysicalAiTrainingSceneTests`
asserts the wiring.

## Status

The 2026-09-06 integration run passed all 128 EditMode cases in 20 test files,
including 29 single-track calculation cases, and 17 graphics-enabled PlayMode cases.
The single-track runtime adapter and production streaming transitions are covered; human
manual-drive acceptance of the replacement remains pending.
The Linux player has been rebuilt and exercised for a 12-second reference-driver smoke run
(24.28 m progress, no collision/departure, ending at the configured timeout).
The PlayMode runner keeps its generated empty bootstrap scene: the ordinary
player bootstrap recognises `-runTests` and does not load AWSIM's Autoware demo.

| Subsystem | Implemented | Unit-tested | Active in the scene |
| --- | --- | --- | --- |
| Combined-slip tire model | yes | force and friction-circle regression | lateral coefficients feed the single-track calculation |
| Planar single-track replacement | calculation + PhysX adapter | 29 calculation cases, adapter and streaming PlayMode tests | yes, behind `CombinedSlipVehicle`; opt-in schema-2 two-axle JSONL |
| Vehicle specification (MAGP class TT-02) | yes | yes | yes |
| Physics-constrained course generation | yes | yes | yes |
| Procedural track, progress and departure | yes | yes; open/closed route-distance and finish-line regressions | yes |
| Domain randomisation | yes | yes | yes |
| Blind-corner safe speed | yes | yes | yes |
| Hazard spawning | yes | — | yes |
| Head-to-head opponent and tactics | yes | tactics; closed-loop opponent circulation | yes |
| Planning contract, reference policy, reward | yes | yes; timeout has an explicit terminal penalty and classical 0.4 m look-ahead | yes |
| ROS2 control input | yes | mapping | starts an idempotent AWSIM ROS2 node and subscribes; it applies only after a command arrives |
| Behaviour cloning policy and trainer | yes | yes | **no model asset assigned** |
| Demonstration recorder | yes | yes | scene-wired; F8 records human teleoperation, while an explicit player flag records separately prefixed reference-policy demonstrations |
| Telemetry friction calibration | yes | yes | CSV conversion rejects malformed/non-steady-state data, records run provenance, and the scene persists accepted-sample RMSE; awaiting a real-run asset |
| Holdout evaluation | yes | evaluator | scene-wired JSON report sink records aggregate metrics and seed-level episode outcomes |
| Sensor rig, planar LiDAR, TF | yes | yes | mounted, not read by any policy |
| Rendered camera observation | yes | EditMode + graphics-enabled PlayMode | mounted; emitted to external bridges without changing vector-policy compatibility |

## Against the goals

Measured against the [goals document](physical-ai-goals.md):

| Goal | State |
| --- | --- |
| Physical consistency — friction circle, feasibility-checked generation, randomised μ | **met**: combined-slip Pacejka, `CoursePhysicsConstraints` gates generation, `DomainRandomizer` varies friction |
| Relative, non-absolute observation | **met**: `RelativeCourseObservation` encodes ego-local waypoints and curvature over a 2.5 m planning horizon |
| Head-to-head interaction | met structurally: opponent, relative kinematics, tactics |
| Hazards beyond blind corners | met: probabilistic, camera-visibility-driven spawning |
| Swappable sensor mounts, mandatory TF sensor | met: `SensorMount`, `SensorRig`, `TransformFrameSensor` |
| System identification from real telemetry | partly: friction only |
| Shared ROS2 low-level node | met structurally: the AWSIM ROS2 node is initialised idempotently and the custom vehicle subscribes to Autoware control |
| Offline BC from in-sim teleoperation | partly: keyboard high-level teleoperation (F8, WASD/arrows), recorder, camera export and visual BC trainer are wired; recorded JSONL carries sparse JPEG camera frames alongside relative observations |
| Zero-shot holdout evaluation | partly: fixed holdout circuits and finite training-distribution evaluation write separate JSON reports; surveyed geometry and trained-policy results remain |
| **Camera field of view and occlusion** | partly: rendered JPEG camera frames feed a runnable visual REINFORCE baseline; the built-in reference/BC vector policies still use the conservative visibility bound rather than image inference |
| **Endless streaming map with floating origin** | met structurally: deterministic chunks append ahead, rear state and logical history are bounded, and world state is shifted |
| Sim RL via a Python interface | partly: `PythonRlBridge` supports both the dependency-free vector baseline and a camera-capable PyTorch REINFORCE trainer. Planning decisions run at 10 Hz and are held by the 100 Hz classical low-level loop; a repeatable trained-policy report remains required |
| Definition-of-done metrics | partly: completion, lap-time, collision, departure, timeout, blind-corner-hazard collision, opponent-collision, ego-overtake and opponent-overtake rates are persisted after a holdout suite; comparative target thresholds remain to be established |

The visual learning path is now executable. Everything listed as "partly" still needs measured
training and evaluation evidence rather than more structural wiring.

## Implementation order

The goals document is explicit that the vehicle physics layer precedes everything, because
learning signal accumulated over inconsistent physics is silently wrong.

1. **Vehicle physics verification.** The assembled TT-02 now has a PlayMode combined-slip
   friction-circle bound test. Steady-state cornering and braking calibration against telemetry
   remain to be added once those samples are available.
2. **Camera-based policy training and evaluation.** Run the visual trainer, preserve its
   checkpoint and learning curve, then compare blind-corner safety against the no-visibility
   baseline.
3. **Teleoperation input and recorder wiring**, which together give the BC pipeline a data source.
4. **Python learning interface** for the sim-RL phase.
5. **Closed-loop holdout courses, checkpoints and a report sink**, which is what makes the
   definition-of-done metrics measurable.

## Documents

- [Goals](physical-ai-goals.md) — what the simulator is for, and the authority over the rest.
- [Map and course](physical-ai-course.md) — the hybrid map specification and the MAGP course.
- [Vehicle model](physical-ai-vehicle-model.md) — MAGP regulations, the adopted TT-02
  parameter set, and the provenance of every value.
- [Offline behaviour cloning](physical-ai-bc.md) — recording format and the training script.
- [Course and vehicle visuals](physical-ai-visuals.md) — reproducible HDRP lighting, materials and scale vehicle rendering.
- [Readiness audit](physical-ai-readiness.md) — required evidence before the simulator goal can be claimed complete.
