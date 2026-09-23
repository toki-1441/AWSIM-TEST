# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository scope

The git repository root is `AWSIM-TEST/`; the VS Code workspace may be its parent (`tamiya/`). `../ref/` is a non-public reference checkout — read it only to learn an implementation approach, never copy its source, paths, or configuration into this repository, and never commit it.

This is a fork of [AWSIM](https://github.com/autowarefoundation/AWSIM) (Unity 6000.0.61f1) with a project-specific `Assets/PhysicalAi/` layer added on top. Upstream AWSIM code under `Assets/Awsim/`, `Assets/Ros2ForUnity/`, and `Assets/RGLUnityPlugin/` is untouched by this work.

`AGENT.md` and `SKILLS.md` hold the working agreement (branch policy, reference-material policy) and are authoritative where they overlap with this file.

The active goal is `docs/physical-ai-operator-workflows.md`: operator launch, collection,
multimodal annotations/review and RL lifecycle. Physics is a baseline, not the current research task.

## Commands

The `*.csproj`/`*.sln` files in the root are Unity-generated and gitignored; do not edit them. Everything below assumes the Unity Editor is **closed** — a running editor holds the project lock and breaks batch invocations.

```bash
# Linux player build (resolves the editor from ProjectSettings/ProjectVersion.txt, writes Build/AWSIM.x86_64)
scripts/build_linux.sh --clean
scripts/build_linux.sh --output Build/custom.x86_64      # UNITY_BIN=... if Unity is outside ~/Unity/Hub/Editor

# EditMode tests (no wrapper script exists; use the editor directly — do not add -quit with -runTests)
UNITY=~/Unity/Hub/Editor/6000.0.61f1/Editor/Unity
$UNITY -batchmode -projectPath . -runTests -testPlatform EditMode \
  -testResults Logs/editmode.xml -logFile /dev/stdout

# A single test class or method
$UNITY -batchmode -projectPath . -runTests -testPlatform EditMode \
  -testFilter PhysicalAi.Tests.EditMode.RaceTacticsTests \
  -testResults Logs/editmode.xml -logFile /dev/stdout

# Scene-level smoke test (opens PhysicalAiTraining.unity, resets one episode, asserts the course built)
$UNITY -batchmode -nographics -quit -projectPath . \
  -executeMethod PhysicalAi.Editor.PhysicalAiRuntimeSmokeTest.Run -logFile /dev/stdout

# Offline BC training (stdlib only, no pip dependencies)
python3 scripts/train_bc.py <demos>/*.jsonl --output Assets/PhysicalAi/Models/bc-policy.json
```

The training scene itself is generated, not hand-authored: `Physical AI/Create Training Scene` in the editor menu (`PhysicalAiTrainingSceneCreator`) rebuilds `Assets/PhysicalAi/Scenes/PhysicalAiTraining.unity` from code. When you add a component the scene must carry, wire it there — `PhysicalAiTrainingSceneTests` asserts the wiring.

## Architecture

### Assembly graph

Each `Assets/PhysicalAi/<Area>/` folder is its own assembly with a matching root namespace. References form a DAG:

```
VehicleDynamics ──┬── Randomization ── Calibration ──┐
Course ──┬─────────┴── Racing ─┐                     │
         ├── Safety ───────────┴─────────────────► Planning ◄──┘
         └── Evaluation (← Course, Planning)
Configuration ──► Sensors, Safety, Planning
```

Two consequences worth knowing before adding code:

- **Anything that touches AWSIM or ROS2 types cannot live in a `PhysicalAi.*` assembly.** Upstream AWSIM has no asmdefs, so `Awsim.Common` and `ROS2` compile into `Assembly-CSharp`, which assemblies cannot reference. `Assets/PhysicalAi/Ros2/` therefore deliberately has *no* asmdef — it is the single seam where the physics/planning layer meets Autoware messages (`CombinedSlipVehicleRos2Input` subscribes to `autoware_control_msgs/Control`; `Ros2ControlMapping` converts acceleration + steer angle to normalized actuation). Tests reach that component by string type name because they cannot reference it either.
- New assemblies must be added to `Assets/Tests/EditMode/PhysicalAi.VehicleDynamics.Tests.asmdef` (one Editor-only test assembly covers all of PhysicalAi).

### Episode loop

`PhysicalAiEpisodeOrchestrator` (Planning) is the runtime boundary that ties everything together, driven from `FixedUpdate`:

1. `ResetEpisode(seed)` — `PhysicsConstrainedCourseGenerator` produces a centerline, `DomainRandomizer` applies the seed, `TelemetryCalibrationApplier` re-centers friction, `ProceduralCourseTrackBuilder` instantiates collider road segments, hazards spawn, the recorder opens a file.
2. Each physics step encodes an observation (`RelativeCourseObservation` → ego-local waypoints, plus camera-limited safe speed and opponent kinematics) via `PlanningObservationEncoder`.
3. A command is chosen by the first available of: `CommandProvider` (external ML/Python/ROS2 bridge) → `BehaviorCloningPolicy` → `ReferencePlanningPolicy`.
4. `PlanningLowLevelController` converts the command to throttle/brake/steer; the recorder logs `(observation, command, reward)`.
5. Termination on completion, off-course departure (`CourseProgressMonitor`), or collision with an `EpisodeHazard`/`OpponentCourseFollower`.

The planning/actuation split is deliberate and load-bearing: a learned policy only ever emits `PlanningCommand { DesiredSpeed, DesiredLateralOffsetNormalized }`, and a classical controller owns actuation. Keep new policies on that contract.

`PlanningObservation` is a flat `float[]`: `[speed, cameraSafeSpeed, opponentLongitudinal, opponentLateral]` followed by 5 floats per waypoint. Changing this layout invalidates every recorded dataset and every trained BC model — `BehaviorCloningPolicy` deliberately falls back to the reference policy on a feature-count mismatch rather than throwing.

### Determinism and seeds

Course generation, domain randomization, and hazard placement are all `System.Random(seed)` driven from the single episode seed, so an episode is fully reproducible from it. `HoldoutCourseSuite.Seeds` is a fixed evaluation set — keep it disjoint from training seeds, and use `HoldoutCourseEvaluator.IsHoldoutSeed` rather than duplicating the list.

### Physics and sim2real chain

`CombinedSlipVehicle` now wraps the planar single-track model, with an optional assisted
no-slip mode; it has no wheel raycasts/suspension. Both modes can record labelled simulator data.
The calibration chain remains available for the dynamic mode, not a prerequisite for operator data capture.

The same physics constrains course generation: `CoursePhysicsConstraints` bounds corner radius by design speed, so infeasible curvature never enters the training distribution, and the per-waypoint `MaximumSafeSpeed` it computes is reused as an observation feature and a reward term (`PlanningReward`). `BlindCornerSafety` applies the analogous bound from camera visibility.

### Conventions in PhysicalAi code

- Pure logic lives in `static` classes / `readonly struct`s and is unit-tested; `MonoBehaviour`s are thin wiring layers holding `[SerializeField]` references. Follow this split — it is why nearly all of this layer is testable in EditMode.
- Optional collaborators are null-tolerant (`_hazardSpawner?.…`), so a partially wired scene still runs.
- Serialized fields are private with a leading underscore; every file carries the `// Copyright 2026` + `SPDX-License-Identifier: Apache-2.0` header.

## Workflow policy

From `AGENT.md`/`SKILLS.md`: `main` is stable, `dev` is the integration branch. Branch each goal off `dev` as `goal/<short-goal>`, commit there, then squash-merge into `dev`, verify with `git diff --exit-code dev goal/<short-goal>`, and delete the local branch. Never commit to a fork or reference repo, and never push unless the user explicitly asks (`origin` must be `toki-1441/AWSIM-TEST`).

Licensing differs by directory: code Apache 2.0, assets CC BY-NC, and `docs/` may not be publicly hosted.
