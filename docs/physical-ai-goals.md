# Goals

This is the authoritative statement of what the Physical AI simulator is for. Where any other
document in this directory disagrees with it, this one wins.

## Active scope revision (2026-09-21)

The current deliverable is the [operator collection and RL workflow](physical-ai-operator-workflows.md),
including shared launch options, fixed/endless maps, RGB/metric depth/BEV, automatic and manual
annotations, data review and RL start/stop/resume. Detailed tyre development is deferred. Stable
assisted and dynamic models may both produce data when their settings and identity are recorded;
assisted data is not evidence of physical-model or real-car fidelity. This revision supersedes the
older blanket recording prohibition and tyre-calibration-first sequencing below. The original
long-term planning/transfer aims remain, not prerequisites for this operator-environment delivery.

## Vision

Build the training platform that lets a camera-based mobile robot, in a racing-style
high-speed scenario, drive an **unseen, differently-shaped course zero-shot** — safely and
quickly. Learning effort is concentrated on the **generalisation of the planning layer**, not
on control: low-level actuation is delegated to classical algorithms.

## Task

| | |
| --- | --- |
| Target | Mobile robot / racing scenario, referencing A2RL, IAC and F1TENTH |
| Sensing | Camera-primary, with a swappable sensor configuration (2D LiDAR retained as an option, solid-state LiDAR anticipated) |
| Environment | Course shape differs every run; specialising to a fixed course is explicitly not the goal |
| Control | Hierarchical — classical low-level (MPC, Pure Pursuit), learned planning |
| Base | Unity plus ROS2ForUnity, inheriting the AWSIM configuration |

## What the simulator must achieve

**As a training platform.** It carries the entire sim-RL phase of a pipeline that is offline BC
from in-sim human teleoperation, then sim RL for 90% of the learning volume, then 10% real-vehicle
fine-tuning. Large-scale real-vehicle data collection is ruled out: course setup cost and data
efficiency mean it does not scale. GPU-scale parallelism is **not** a goal — throughput comes from
scenario density and driving time within a single environment, not from environment count.

**Generalisation.** Policies must run zero-shot on procedurally generated courses never seen in
training. The observation is therefore local and relative — next-corner curvature, relative
waypoints — never absolute course coordinates. A fixed holdout set, separate from the training
distribution, must make zero-shot performance measurable.

**Physical consistency — the highest-priority foundation.** Vehicle dynamics include the friction
circle via a combined-slip Pacejka model, so the policy cannot learn a path that is geometrically
plausible but physically undrivable. The course generation pipeline itself checks whether the
curvature it produces is traversable by the vehicle model at the intended speed, and either does
not generate the impossible combination or surfaces it as an RL penalty. Friction is a domain
randomisation target, not a constant.

**Partial observability and blind corners.** Camera field of view and occlusion — blind corners
and self-occlusion by the body — must render accurately, so that the information loss at corners
sharper than 90° is genuinely experienced in simulation. Hazards appear probabilistically beyond
blind corners so the policy learns that a blind spot always carries risk. The intended outcome is
that conservative, risk-aware behaviour — slowing down, taking a sightline-preserving racing line —
emerges from learning rather than from explicit rules.

**Head-to-head.** Beyond solo lap time, multiple vehicles interact in one simulation: overtaking
and blocking, with opponent intent prediction and strategy available for training and evaluation.

**Transfer to the real vehicle.** The ROS2 low-level control node is the same in simulation and on
the real car — this is why the AWSIM/Autoware-compatible configuration was chosen. Real driving
telemetry calibrates the vehicle physics model by system identification, so the sim-to-real gap
can be closed continuously.

## Out of scope

- GPU-scale parallelism (hundreds to thousands of simultaneous environments, Isaac-style).
- Reducing perception to a single planar sensor. Since the camera is primary, a design where
  perception is nothing but 2D ray casting is not adopted; 2D LiDAR stays as an auxiliary and
  comparison sensor.
- Learning low-level control. Steering, throttle and brake conversion stay classical.
- Large-scale real-vehicle data collection. The real car is for final calibration and edge cases.

## Technical constraints

Temporary operator-requested exception (2026-09-06): manual driving defaults to a no-slip
handling aid while road/control usability is checked. Retain wall collision and streamed-world
continuity; keep the physical model available with `-physicalAiAllowSlip`. Assisted runs must
be labelled separately, must not generate physical-model training demonstrations, and do not
satisfy the tire-dynamics acceptance below. See [temporary mode](physical-ai-vehicle-model.md#temporary-no-slip-manual-mode-2026-09-06).

- **Engine.** Unity with ROS2ForUnity and Autoware-compatible vehicle and sensor interfaces.
- **Vehicle physics.** No `WheelCollider`. A planar single-track model in the style of
  `f1tenth_gym`, which switches to a kinematic formulation below 0.5 m/s because the
  dynamic tire equations are numerically unstable there.

  This replaces the four-wheel ray-cast suspension with per-wheel Pacejka tires that the
  project carried until 2026-09-06. That model repeatedly produced non-physical motion
  that an operator felt while driving: a 50 Hz oscillation at the timestep's Nyquist
  frequency in longitudinal slip, suspension corners alternating between zero and their
  force ceiling, a stationary vehicle winding itself to 5.6 rad/s, and front and rear
  lateral forces forming a pure yaw couple at zero speed with the tires saturated. Each
  was fixed in isolation and the next appeared elsewhere, because the common cause is
  integrating stiff per-wheel dynamics explicitly at 100 Hz. A 400 Hz step would cost
  four times the physics budget this project spends on driving time instead.

  What the goals actually require of the physics — that a policy cannot learn a path the
  vehicle could not drive — is carried by the friction circle, finite acceleration and
  finite steering, all of which the single-track model keeps analytically. Attitude and
  per-wheel load are not read by any planning observation. Vertical dynamics are dropped
  deliberately: at 1/10 scale on a flat indoor floor with a 0.054 m centre of gravity,
  they contribute little to the behaviour being learned and much to the instability.
- **Map — hybrid.** Spline-based procedural generation from parameterised primitives (straight,
  corner radius, bank angle), in two forms:
  - **Training (primary): endless streaming.** A single never-ending road generated in chunks
    ahead of the vehicle, with passed chunks discarded behind it. This avoids episode-reset
    overhead and secures throughput in a non-parallel environment, and it resembles the real
    deployment case — one attempt, on a road never seen before.
  - **Evaluation (holdout): closed-loop finite courses**, generated separately and fixed, scored
    by lap time and the other metrics below.
  - **Requirement:** because the endless form lets coordinates grow without bound, a floating
    origin — keeping the vehicle near the origin and shifting the world instead — is a
    prerequisite for avoiding single-precision degradation.
- **Streaming spatial integrity — prerequisite for training.** The endless course is a single
  authoritative geometric representation, not independent visual, collision and centre-line
  approximations. Every retained chunk must derive its road surface, lane markings, walls and
  colliders from that representation in the same coordinate frame. Appending, discarding and
  floating-origin rebasing must preserve their relative positions exactly. A candidate chunk is
  accepted only when its swept road-and-wall envelope clears every non-adjacent retained section
  and its own non-adjacent sections by the configured vehicle and safety margin. Generation may
  retry deterministically, but it must not fall back to a permanently straight or fixed course:
  the training distribution must retain parameterised corners as well as straights.
- **Streaming horizon.** Before the vehicle can reach the end of retained geometry, sufficient
  forward road must already be generated. Passed road is discarded only after it is safely behind
  the vehicle. Creating or removing a chunk must not introduce a gap, overlap, visible flicker,
  or contact impulse into the vehicle.
- **Streaming progression and variety — non-negotiable acceptance rule.** Generation is driven by
  the vehicle's forward progress, not merely by a fixed initial cache.  At every reachable point
  it must retain a configured look-ahead horizon and append the next valid chunk before that
  horizon is exhausted.  Collision rejection is not permission to stop extending the road or to
  degenerate into a straight-only continuation: deterministic retries must select another valid
  curved or straight primitive while preserving the configured distribution.  A candidate is
  invalid if either its swept road/wall envelope or its rendered geometry intersects, touches, or
  visually crosses any non-adjacent retained chunk.  The same rule applies after trimming and
  after a floating-origin rebase.
- **Generation-boundary dynamics.** A chunk append, removal or rebase is a world-management
  operation only. It must not alter the vehicle's pose, velocity, angular velocity, steering
  actuator state, or controller command except for normal wall contact that already existed.
  This explicitly rules out the unexplained inertial kick reported near newly created road.
- **Driving and visual baseline — prerequisite for learning.** The built player must be manually
  drivable with stable, predictable vehicle motion.  The road must be continuously rendered well
  beyond the stopping/sight distance in front of the car; it must not appear as darkness, a
  short isolated patch, seams, z-fighting or flickering edges.  The car must remain supported by
  one continuous physical road surface, and walls/boundary colliders must keep it in the intended
  corridor without pumping, bouncing, snagging or a spurious inertia impulse when a chunk is
  appended or discarded.  A vehicle-mounted camera view, approximately 0.15 m above the vehicle
  and looking down the driving direction, must be available alongside the operator view so that
  the primary observation can be inspected in the running build.
- **Learning interface.** Python integration via ML-Agents or similar; large-scale parallelism is
  not assumed.
- **Data ratio.** 90% simulation, 10% real vehicle.
- **Sensors.** A camera-primary perception pipeline with a swappable sensor abstraction. 2D LiDAR
  remains as an alternative and auxiliary sensor, using the automotive-grade sensor framework
  AWSIM already supports (LiDAR, camera, IMU, GNSS). A TF sensor — odometry and pose estimation,
  connected to the ROS2 TF tree — is a **mandatory** part of the configuration, so that
  Autoware-compatible coordinate management holds.
- **Sensor mounts.** Camera, 2D LiDAR, solid-state LiDAR and TF sensor all take mounting position
  and orientation as parameters, so the difference in information and blind spots between
  viewpoints can be experimented with cheaply.
- **Future.** Solid-state LiDAR is anticipated; the sensor abstraction must accept it as an added
  implementation. It is not in scope now.

## Definition of done

| Metric | Criterion |
| --- | --- |
| Physical consistency | The frequency of demanding curvature the vehicle model cannot traverse is near zero |
| Generalisation | Zero-shot completion rate and lap time on unseen holdout courses do not diverge much from in-distribution courses |
| Blind-corner safety | Collision and departure rates at blind corners are significantly lower than without sightline consideration |
| Head-to-head | Overtaking and defending happen without collisions |
| Sim-to-real consistency | After calibrating the model with real telemetry, the change in simulated behaviour stays within tolerance |
| Streaming-course integrity | Across initial generation, repeated append/discard cycles and floating-origin rebases, varied curved and straight chunks remain non-intersecting, gap-free and mutually aligned (surface, markings, walls and colliders); no generation event causes an unintended vehicle impulse |
| Usable running build | A human can drive the built player through the streamed corridor without persistent bouncing, floor/edge flicker, or unexplained force near generation boundaries; boundary collision prevents unintended departure |
| Forward road and camera observability | The build always renders a continuous, illuminated forward corridor beyond the usable sight/stopping distance, continues generating before it is reached, and exposes an inspectable vehicle-mounted camera view |
| Course variety | Streaming generation retains the configured mix of valid corners and straights; an intersection-rejection fallback must retry or use another valid curved primitive, never silently reduce the run to straight-only geometry |
| Streaming progression | During a sustained manual drive, the vehicle triggers repeated forward chunk appends and passed-chunk retirement; the retained horizon never runs out and the generated path demonstrably contains both curves and straights |
| Intersection exclusion | No initial, appended, retained, trimmed or rebased non-adjacent road/wall pair has physical-envelope overlap, touching, or a visible crossing; this must be checked over multiple seeds and the actual running build |
| Generation-boundary stability | Instrumented append, discard and rebase events leave the vehicle state continuous and do not cause a perceptible kick, bounce, snag or new contact; this is validated while manually driving the built player, not by static geometry alone |

## Pre-learning running-build gate

The following four properties are a single prerequisite for collecting demonstrations or judging a
policy.  They are deliberately **not tradeable**: passing one does not compensate for a failure in
another.  In particular, an implementation must not avoid crossings by switching to a fixed
initial cache, stopping generation, or silently producing only straight road.

| Gate | Required behaviour | Evidence required |
| --- | --- | --- |
| Geometric continuity | One authoritative streamed corridor produces matching road, marking, wall and collision geometry.  No non-adjacent retained geometry may overlap, touch or visibly cross, including after append, retirement and rebase. | Multi-seed automated envelope checks **and** a captured manual built-player drive through the same transitions. |
| Forward progression and variety | Driving forward repeatedly appends valid chunks before the horizon is reached and retires passed chunks.  The forward visible road remains a mixed sequence of parameterised curves and straights; it is not merely the initial course cache. | Instrumented append/retirement/rebase record during a sustained manual drive, with visible forward horizon. |
| Driveable physical corridor | The manually controlled vehicle is supported by continuous ground and is retained by boundary collision.  It has no persistent pumping, bouncing, snag, floor/edge flicker, seam, or force/velocity change caused by generation work. | Built-player manual-drive recording across several generation boundaries; vehicle-state instrumentation at each event. |
| Camera-primary observability | The built player exposes the vehicle-mounted camera and renders an illuminated, continuous forward sight/stopping corridor.  A visual-policy evaluation must prove that it received rendered camera frames; batch/headless output with a Null graphics device is not camera evidence. | Captured operator view plus reports with nonzero rendered-camera frame counts, and a paired camera-blackout comparison. |

## Immediate corrective milestone: driveable varied streaming

The current course work is complete only when one built-player run satisfies all of the following
conditions together.  Fixing intersection rejection by removing corners, exhausting the generated
horizon, or retaining only the initial cache is a failure, not a partial pass.

1. The retained road contains both straights and non-trivial left and right curves, while every
   non-adjacent swept road/wall envelope remains separated by the configured clearance.
2. Forward motion repeatedly triggers generation before the remaining drivable horizon falls below
   the configured sight/stopping requirement; passed chunks are retired only after the vehicle and
   its cameras can no longer depend on them.
3. The road surface, markings, walls and colliders share the same sampled boundaries at every seam.
   There are no gaps, overlaps, z-fighting edges or wall discontinuities.
4. Append, retirement and floating-origin events preserve rigidbody pose, linear/angular velocity,
   actual steering actuator state and current control input, apart from ordinary pre-existing
   collision forces. For the planar model, diagnostic continuity covers front/rear axle forces
   and dynamic blend state; wheel-contact and suspension states no longer exist.
5. A human can drive through several append/retirement cycles without persistent pumping, bouncing,
   snagging, unexplained inertia, or unintended departure through the boundary collision.
6. The operator view and the approximately 0.15 m-high vehicle camera both show an illuminated,
   continuous forward road far enough to choose an action before reaching generated geometry.

Acceptance evidence must include a multi-seed geometry test, an event log listing retained horizon,
primitive type, append/retirement/rebase events and vehicle-state deltas, plus a captured sustained
manual run of the built player.  Compile success, EditMode tests, or a static screenshot alone do not
close this milestone.

### Regression acceptance: varied road that keeps extending

The operator-reported regressions — a straight-only course and no next-chunk generation —
must be checked together with intersection exclusion, not treated as acceptable ways to achieve
it. The target is a **manually driveable, varied, continuously extending corridor**.

- In the same built-player run, demonstrate at least **three forward append operations, one
  passed-chunk retirement and one floating-origin rebase**. Record the seed and generation
  settings so that the run can be reproduced.
- Show non-trivial left and right curves as well as straights in the **newly appended road**,
  not only in the initial cache. Log primitive types and remaining forward distance; verify
  that the configured forward sight/stopping horizon is maintained throughout the drive.
- Validate the full road-and-wall envelopes after each transition. Centre-line separation
  alone is insufficient; rejection of an intersecting candidate must not silently result in
  straight-only continuation or stalled generation. Record rejected candidates and any inability
  to replenish the horizon as a failure, not as successful intersection prevention.
- Check vehicle behaviour both around each generation event **and while approaching and driving
  through the newly generated seams and curves**. An unchanged state at the event instant does
  not rule out later collision snags, unexplained inertia, pumping or bouncing. Correlate the
  drive recording with vehicle-state and contact/collision evidence along those sections.

All conditions above are required together. Until the rebuilt player passes this manual run,
report the milestone as unverified or failed; a successful build or automated test alone must
not be described as fixing the operator-reported regressions.

## Current scope: a trustworthy data-collection and RL environment

Agreed 2026-09-06. This narrows what the project is trying to close **next**; it does not
change the vision above.

**In scope.** Reaching the point where an operator can drive the built player manually and
produce demonstration data that is worth training on, and where the RL loop is wired end to
end. The deliverable is the *environment*, evidenced.

**Explicitly out of scope for now.** Demonstrating that a policy actually learns well.
Learning outcomes depend on data volume, architecture and hyperparameters, so pursuing them
before the environment is trustworthy makes an environment defect and a training failure
indistinguishable. The RL loop only has to be shown to carry observations, rewards, episode
termination and checkpoints correctly — not to produce a good policy.

**Why "trustworthy" and not merely "possible".** Data recorded from an incorrect vehicle is
poisoned data. Every hour of manual driving collected over a physics defect is invalidated the
moment the defect is fixed. The known defects that block collection are therefore in scope:
the wheel that loses contact across a streaming chunk transition, and the unresolved
suspension load spikes described in
[the oscillation investigation](physical-ai-vehicle-oscillation.md).

The acceptance criteria for this scope already exist above: the four **pre-learning running
build gates** and the **driveable varied streaming** milestone. This section does not add new
criteria, it states which ones are the current target.

## Mode selection

A build must be usable by an operator who is not its author. Today the player is driven by 23
command-line flags covering scenario, opponent speed and lateral motion, hazard probability,
recording and report paths. That is an implementer-only interface, and it conflicts directly
with the acceptance criteria above, which require an operator to drive the build and affirm a
checklist.

**Goal.** Launching with arguments selects a mode directly, exactly as it does now. Launching
**without** arguments presents an on-screen mode selection the operator can proceed from,
rather than silently defaulting into one scene.

Design constraints, chosen for implementability and extensibility:

- **The mode set is data, not wiring.** Modes live in one declarative table; adding a mode is
  adding an entry, not authoring a prefab or a scene. This is what keeps the screen from
  decaying as scenarios multiply.
- **Immediate-mode UI.** The screen is drawn from code, with no prefab, scene asset or UI
  framework dependency, matching the existing convention that
  `PhysicalAiTrainingSceneCreator` generates the scene from code. `PhysicalAiPlayerBootstrap`
  already owns an otherwise empty scene at build index 0, which is where it belongs.
- **Presets, not a parameter editor.** The screen offers a small set of useful combinations.
  Exhaustively exposing every flag would create a second interface to maintain alongside the
  command line, and the two would drift.
- **Two levels.** Mode, then scenario or suite where one applies — manual driving splits into
  nominal, blind-corner and head-to-head; evaluation splits into training and holdout.
- **The screen reports its own command line.** A selection displays the equivalent arguments,
  so a configuration explored interactively can be reproduced by an automated run. This keeps
  operator-driven evidence reproducible rather than one-off.

The command line remains authoritative: when arguments select a mode, the screen is skipped.

## Implementation order

The vehicle physics layer is the precondition for every other goal. Proceeding to course
generation and learning while it is unverified risks accumulating learning signal that is
physically inconsistent throughout. **The tire physics model is therefore what must be built and
verified first.**

For the primary endless-training form, a second non-negotiable foundation is streaming-course
integrity. It is completed before visual tuning or policy training: first establish one coordinate
authority and a continuous, non-intersecting road/collision corridor through append, discard and
floating-origin transitions; then verify, in the actual built player, that a manually driven
vehicle remains stable while the forward curved-and-straight horizon is extended and old chunks
are retired; then validate the camera observation; and only then collect demonstrations or train
policies.
Straight-only continuation, a fixed substitute course, compile-only checks, and EditMode-only
tests are not acceptable evidence for this milestone.  In particular, a change that prevents
intersections by freezing generation, starving the forward horizon, or removing curved primitives
does not satisfy the goal; it is a regression until the full streaming acceptance criteria above
are met.

### Sequence for the current scope

Agreed 2026-09-06, refining the order above for the
[data-collection and RL environment scope](#current-scope-a-trustworthy-data-collection-and-rl-environment).
Revised the same day after operator driving exposed defects that automated evidence had
reported as clean.

**Done.**

1. ~~Fix the contact loss across streaming transitions.~~ The ray reached only to the
   spring's rest length, which reduces lift tolerance algebraically to the static sag —
   2.58 mm here, against the 4.78 mm a few degrees of roll produce. The suspension gained
   the rebound travel a real arm has.
2. ~~Add mode selection~~, so the remaining steps need an operator rather than 23 flags.
3. ~~Verify manual teleoperation end to end.~~ Direct input reaches the actuators and moves
   the vehicle; the built player was driven.

**In progress.**

4. **Replace the vehicle model with the planar single-track formulation.** Operator driving
   found four defects that every automated check had passed: a 31.7 mm step where wall
   segments met, a venue floor left behind by a floating-origin rebase, a stationary vehicle
   winding itself to 5.6 rad/s, and a saturated yaw couple at zero speed that turned the car
   through half a revolution. The first two are fixed. The last two are the same class of
   failure the earlier oscillation investigation found, in new places, and six attempted
   fixes to the four-wheel model resolved two of them. The cause is common to all: stiff
   per-wheel dynamics integrated explicitly at 100 Hz.

   Implement the model as a pure calculation first and pin its behaviour before anything is
   swapped: no yaw from rest with no input, cornering that matches `v^2/R <= mu*g`, and a
   continuous transition across the 0.5 m/s kinematic switch. Keep `Throttle`, `Brake`,
   `Steering` and `SurfaceFriction`, which is all that most of the thirteen dependent files
   use. Per-wheel telemetry has to be reinterpreted for two axles rather than four corners.

   **Integration progress (2026-09-06):** `SingleTrackVehicleModel` and its 29 dedicated
   tests are implemented; all 128 EditMode and 17 graphics-enabled PlayMode cases pass. See the
   [model contract and numerical method](physical-ai-vehicle-model.md#planar-single-track-calculation-2026-09-06).
   `CombinedSlipVehicle` now wraps this model, keeping its control interface. The regenerated
   scene uses the new adapter; two-axle telemetry and streaming validation use schema 2.
   Wall collision, external state changes and subsequent physics steps after production streaming
   transitions are covered automatically. The scene smoke test and Linux rebuild passed; a
   12-second built-player reference run advanced 24.28 m with no collision/departure before its
   configured timeout. Manual acceptance remains outstanding; successful automated checks alone
   do not close the environment scope.

**Then.**

5. **Pass the manual streaming validation** with its video and affirmed operator checklist.
   `scripts/run_manual_streaming_validation.sh` refuses evidence produced by the reference
   planner, so this must be a genuine human drive.
6. **Collect one human demonstration session** and confirm the dataset validation scripts
   accept it.

Learning performance is not measured in this sequence.

**What this revision was bought with.** Every one of those four defects was reported by
someone driving, while the streaming instrumentation recorded a position error of 0.000000 m
and no discontinuity throughout. That instrumentation is not broken: it samples the vehicle at
the instant of a transition, and none of these defects act then. Geometry can bend without the
car touching it, and a model can be unstable while no transition is in progress. Automated
evidence bounds what it measures, not what an operator experiences, and this scope is closed by
a human drive for that reason.
