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
```

The training scene is generated rather than hand-authored, so a component that the scene must
carry has to be wired in `PhysicalAiTrainingSceneCreator`; `PhysicalAiTrainingSceneTests`
asserts the wiring.

## Status

16 EditMode test files, 35 test cases, all passing.

| Subsystem | Implemented | Unit-tested | Active in the scene |
| --- | --- | --- | --- |
| Combined-slip vehicle and tire model | yes | tire model | yes |
| Vehicle specification (MAGP class TT-02) | yes | yes | yes |
| Physics-constrained course generation | yes | yes | yes |
| Procedural track, progress and departure | yes | yes | yes |
| Domain randomisation | yes | yes | yes |
| Blind-corner safe speed | yes | yes | yes |
| Hazard spawning | yes | — | yes |
| Head-to-head opponent and tactics | yes | tactics | yes |
| Planning contract, reference policy, reward | yes | yes | yes |
| ROS2 control input | yes | mapping | **component present, never initialised** |
| Behaviour cloning policy and trainer | yes | yes | **no model asset assigned** |
| Demonstration recorder | yes | yes | **not in the scene** |
| Telemetry friction calibration | yes | yes | **no telemetry asset assigned** |
| Holdout evaluation | yes | evaluator | **report has no subscriber** |
| Sensor rig, planar LiDAR, TF | yes | yes | mounted, not read by any policy |

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
| Shared ROS2 low-level node | partly: the component exists but is never initialised |
| Offline BC from in-sim teleoperation | partly: recorder, trainer and policy exist, but the recorder is not in the scene and there is no teleoperation input |
| Zero-shot holdout evaluation | partly: fixed seeds exist, but they generate the training archetype and the report has no subscriber |
| **Camera field of view and occlusion** | **not met**: visible distance is a single forward raycast; no rendered camera enters the observation |
| **Endless streaming map with floating origin** | **not implemented** |
| Sim RL via a Python interface | **not started**: no ML-Agents package in the manifest |
| Definition-of-done metrics | **not measurable**: no report sink, and no metric for blind-corner safety or head-to-head outcomes |

The two structural absences are the camera perception path and the streaming map. Everything
listed as "partly" is wiring rather than design.

## Implementation order

The goals document is explicit that the vehicle physics layer precedes everything, because
learning signal accumulated over inconsistent physics is silently wrong.

1. **Verify the vehicle physics.** The specification and tire model are in place and unit-tested
   at the force level. What is missing is a dynamics-level check: that the assembled vehicle
   reaches — and does not exceed — the analytic friction-circle limit in steady-state cornering
   and in braking.
2. **Endless streaming map and floating origin.** Resumable chunk generation, incremental build
   and discard, and the origin shift. This changes what an episode is, so it should land before
   any data is recorded.
3. **Camera-based observation.** The perception goal the project is named for, and the
   precondition for blind-corner behaviour to be learned rather than hand-coded.
4. **Teleoperation input and recorder wiring**, which together give the BC pipeline a data source.
5. **Python learning interface** for the sim-RL phase.
6. **Closed-loop holdout courses, checkpoints and a report sink**, which is what makes the
   definition-of-done metrics measurable.

## Documents

- [Goals](physical-ai-goals.md) — what the simulator is for, and the authority over the rest.
- [Map and course](physical-ai-course.md) — the hybrid map specification and the MAGP course.
- [Vehicle model](physical-ai-vehicle-model.md) — MAGP regulations, the adopted TT-02
  parameter set, and the provenance of every value.
- [Offline behaviour cloning](physical-ai-bc.md) — recording format and the training script.
