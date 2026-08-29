# Goals

This is the authoritative statement of what the Physical AI simulator is for. Where any other
document in this directory disagrees with it, this one wins.

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

- **Engine.** Unity with ROS2ForUnity and Autoware-compatible vehicle and sensor interfaces.
- **Vehicle physics.** No `WheelCollider`. A custom Pacejka combined-slip tire model.
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

## Implementation order

The vehicle physics layer is the precondition for every other goal. Proceeding to course
generation and learning while it is unverified risks accumulating learning signal that is
physically inconsistent throughout. **The tire physics model is therefore what must be built and
verified first.**
