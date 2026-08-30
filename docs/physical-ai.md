# Physical AI

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

The planning and actuation split is load-bearing: a learned policy only ever emits
`PlanningCommand { DesiredSpeed, DesiredLateralOffsetNormalized }`, and a classical controller
owns actuation. `PlanningObservation` is a flat `float[]` —
`[speed, cameraSafeSpeed, opponentLongitudinal, opponentLateral]` followed by five floats per
waypoint. Changing that layout invalidates every recorded dataset and trained model, so
`BehaviorCloningPolicy` falls back to the reference policy on a feature-count mismatch rather
than throwing.

Course generation, domain randomisation and hazard placement are all seeded from the single
episode seed, so an episode is reproducible from it alone. `HoldoutCourseSuite.Seeds` is a
fixed evaluation set that must stay disjoint from training seeds.

## Commands

The Unity Editor must be closed for any batch invocation.

```bash
UNITY=~/Unity/Hub/Editor/6000.0.61f1/Editor/Unity

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
# Save comparable run artifacts outside Unity's persistent-data default.
Build/AWSIM.x86_64 -physicalAiRlPort 5015 -physicalAiEvaluation holdout -physicalAiReportDirectory Build/evaluations/run-001
# Baseline for the blind-corner safety comparison (planning ignores camera occlusion).
Build/AWSIM.x86_64 -physicalAiEvaluation holdout -physicalAiDisableVisibilitySafety -physicalAiReportDirectory Build/evaluations/blind-baseline

# Reproducibly evaluate one saved visual checkpoint on both distributions.
PHYSICAL_AI_PYTHON=../.physical-ai-venv/bin/python \
  scripts/run_visual_evaluation.sh Build/physical-ai-vision-reinforce.pt Build/evaluations/run-001

# Train visual REINFORCE from rendered frames, then evaluate the saved checkpoint.
PHYSICAL_AI_PYTHON=../.physical-ai-venv/bin/python \
  scripts/run_vision_reinforce_training.sh Build/training/run-001 100

# Produce model, blind-corner, head-to-head baseline reports and the final gate result.
PHYSICAL_AI_PYTHON=../.physical-ai-venv/bin/python \
  scripts/run_goal_gate.sh Build/training/run-001/physical-ai-vision-reinforce.pt Build/evaluations/gate-001

python3 scripts/convert_telemetry_csv.py run.csv --output Assets/PhysicalAi/Calibration/real-run.json

python3 scripts/evaluate_goal_metrics.py --training train.json --holdout holdout.json --blind-baseline blind.json --race-baseline race.json
```

The training scene is generated rather than hand-authored, so a component that the scene must
carry has to be wired in `PhysicalAiTrainingSceneCreator`; `PhysicalAiTrainingSceneTests`
asserts the wiring.

## Status

17 EditMode test files (51 cases) and two graphics-enabled PlayMode cases pass.
The PlayMode runner keeps its generated empty bootstrap scene: the ordinary
player bootstrap recognises `-runTests` and does not load AWSIM's Autoware demo.

| Subsystem | Implemented | Unit-tested | Active in the scene |
| --- | --- | --- | --- |
| Combined-slip vehicle and tire model | yes | tire model | yes |
| Vehicle specification (MAGP class TT-02) | yes | yes | yes |
| Physics-constrained course generation | yes | yes | yes |
| Procedural track, progress and departure | yes | yes; closed-loop lap-distance and finish-line regressions | yes |
| Domain randomisation | yes | yes | yes |
| Blind-corner safe speed | yes | yes | yes |
| Hazard spawning | yes | — | yes |
| Head-to-head opponent and tactics | yes | tactics; closed-loop opponent circulation | yes |
| Planning contract, reference policy, reward | yes | yes | yes |
| ROS2 control input | yes | mapping | starts an idempotent AWSIM ROS2 node and subscribes; it applies only after a command arrives |
| Behaviour cloning policy and trainer | yes | yes | **no model asset assigned** |
| Demonstration recorder | yes | yes | scene-wired; recording begins only when F8 enables keyboard teleoperation |
| Telemetry friction calibration | yes | yes | CSV conversion, scene applier and persistent fit-quality report are ready; awaiting a real-run asset |
| Holdout evaluation | yes | evaluator | scene-wired JSON report sink records completion, lap-time and safety metrics |
| Sensor rig, planar LiDAR, TF | yes | yes | mounted, not read by any policy |
| Rendered camera observation | yes | EditMode + graphics-enabled PlayMode | mounted; emitted to external bridges without changing vector-policy compatibility |

## Against the goals

Measured against the [goals document](physical-ai-goals.md):

| Goal | State |
| --- | --- |
| Physical consistency — friction circle, feasibility-checked generation, randomised μ | **met**: combined-slip Pacejka, `CoursePhysicsConstraints` gates generation, `DomainRandomizer` varies friction |
| Relative, non-absolute observation | **met**: `RelativeCourseObservation` encodes ego-local waypoints and curvature |
| Head-to-head interaction | met structurally: opponent, relative kinematics, tactics |
| Hazards beyond blind corners | met: probabilistic, camera-visibility-driven spawning |
| Swappable sensor mounts, mandatory TF sensor | met: `SensorMount`, `SensorRig`, `TransformFrameSensor` |
| System identification from real telemetry | partly: friction only |
| Shared ROS2 low-level node | met structurally: the AWSIM ROS2 node is initialised idempotently and the custom vehicle subscribes to Autoware control |
| Offline BC from in-sim teleoperation | partly: keyboard high-level teleoperation (F8, WASD/arrows), recorder, camera export and visual BC trainer are wired; recorded JSONL carries sparse JPEG camera frames alongside relative observations |
| Zero-shot holdout evaluation | partly: fixed holdout circuits and finite training-distribution evaluation write separate JSON reports; surveyed geometry and trained-policy results remain |
| **Camera field of view and occlusion** | partly: rendered JPEG camera frames feed a runnable visual REINFORCE baseline; the built-in reference/BC vector policies still use the conservative visibility bound rather than image inference |
| **Endless streaming map with floating origin** | met structurally: deterministic chunks append ahead, rear state and logical history are bounded, and world state is shifted |
| Sim RL via a Python interface | partly: `PythonRlBridge` supports both the dependency-free vector baseline and a camera-capable PyTorch REINFORCE trainer; a repeatable trained-policy report remains required |
| Definition-of-done metrics | partly: completion, lap-time, collision, departure, timeout, blind-corner-hazard collision and opponent-collision rates are persisted after a holdout suite; comparative target thresholds remain to be established |

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
