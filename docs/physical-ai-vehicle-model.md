# Vehicle model

## Temporary no-slip manual mode (2026-09-06)

At the operator's request, interactive keyboard driving now defaults to `planar-no-slip-assist`.
Launch `Build/AWSIM.x86_64` and select manual driving as usual. WASD/arrow controls are unchanged.
To restore the physical tire model, launch `Build/AWSIM.x86_64 -physicalAiAllowSlip`.
Automated reference, ROS2/RL and evaluation runs retain the physical model; the aid is selected
by the active keyboard driver, not by changing the vehicle specification or scene physics.

The aid removes accumulated sideways velocity and yaw momentum each physics step. Speed uses
the current body's forward velocity and the existing straight-line acceleration/braking envelope;
yaw is prescribed from speed, wheelbase and rate-limited steering. Velocity follows the car's
mid-step heading. This is an arcade handling constraint at the CG, not a calibrated no-slip
two-axle model or a friction-circle solution. Small timestep/contact residuals can remain, but
there is no free-running drift. Lateral acceleration is intentionally not limited by tire grip.
Speed is not cached: a wall stop or episode reset cannot restore a stale pre-impact speed.
The body stays dynamic with continuous collision detection; PhysX integrates its position and
resolves collisions after the velocity constraint. No pose teleporting is used for driving.

Assisted telemetry identifies its model and velocity-constraint sampling phase, with an empty
axle array (no fabricated tire forces). Streaming logs also identify the assisted model.
As of the 2026-09-21 operator-workflow revision, demonstration recording is permitted with
explicit model/settings metadata. The physical-model acceptance validator still rejects assisted
runs as fidelity evidence. This temporary convenience does **not** complete
the physical handling/calibration goal. The contracts below describe the retained physical mode.

Verification: the full EditMode suite passed 128/128 and graphics-enabled PlayMode passed
22/22, including assisted forward/reverse turning, steering-release yaw cancellation, braking,
side-impulse removal, actual wall stop/back-away and floating-origin invariance. Eleven Python
validator tests pass, including rejection (but correct transition counting) of assisted evidence.
These are automated checks, not an operator judgement of the new handling.

## Steering travel and cornering grip (2026-09-22)

The operator reported that steering was abnormal and that cornering slip at speed was
severe. Three independent causes were found; all three were invisible to the 130 passing
tests, because nothing asserted tire curve shape, handling balance, or how much friction
the longitudinal solve was allowed to consume.

**Coasting consumed the whole friction budget.** The coast-down term added on 2026-09-06
was purely speed-proportional at 2.4 per second, so it demanded `2.4 * v` m/s^2 — passing
the 8.83 m/s^2 the tires can produce at 3.68 m/s. Because `Step` applies the longitudinal
impulse first and gives the lateral solve only `sqrt(budget^2 - longitudinal^2)`, releasing
the throttle above that speed left *zero* cornering grip. At 4 m/s the same steering input
produced 0.19 rad/s of yaw coasting against 1.98 rad/s with light throttle. Coasting was
also decelerating harder than full braking, which is not what a released throttle does.
`MaximumCoastDeceleration` now caps it at 2.0 m/s^2; coasting spends under a third of the
rear budget at every speed, and 4 m/s rolls 4.15 m against 0.93 m on the brake.

**The lateral tire curve peaked at 18.1 degrees of slip.** `B` multiplies a slip angle in
radians, so it sets where the force peaks; at `B = 5.2` the tire only delivered the `mu*Fz`
the friction circle already assumed after the axle had crabbed more than twice as far as
any real tire. Rubber peaks between roughly 4 and 12 degrees at any scale. The curves are
now split per axle and re-fitted: front peaks at 7.9 degrees, rear at 6.8.

**The vehicle was exactly neutral-steer at every speed.** A single shared `TireB` served
both axles, and `D` is a fraction of `mu*Fz`, so each axle's cornering stiffness came out
exactly proportional to its own load and the understeer gradient `Wf/Cf - Wr/Cr` cancelled
to zero identically — for any mass distribution, any load split, any friction. A neutral
vehicle saturates both axles at the same instant, so it snaps into a spin instead of
pushing wide, and its yaw gain never rolls off with speed. `TireBFront` below `TireBRear`
restores a positive margin.

Separately, a keyboard key is either off or hard over, but full lock is a 0.55 m radius
that needs all of `mu*g` above 2.21 m/s. At 4 m/s a held key demanded 3.3 times the
available friction and at top speed 8.4 times. `GripLimitedSteeringAngle` scales the
operator's steering travel to what the surface can hold — what an RC driver does with the
transmitter's dual-rate dial, which a keyboard has no way to express. Demand on a held key
is now bounded at 1.0 times the friction limit at every speed instead of 8.4, and peak
body slip over a second of held steering falls from 29-35 degrees to 5-21.

Still an estimate: the lateral coefficients are fitted to a plausible peak slip angle, not
identified from the vehicle. A steady-state skidpad sweep observes both `alpha_peak` and
`C_alpha` directly and is the measurement that would replace them. `MaximumSteeringRate`
(3.2 rad/s) also remains a provisional actuator assumption; a real TT-02 servo is faster,
so it is conservative rather than optimistic.

## Coast-down and respawn (2026-09-06)

Releasing the throttle now decelerates the vehicle. The operator asked for a higher
deceleration rate; measurement showed the brake was already at the friction limit
(18.50 N, 8.81 m/s^2 against a limit of 8.83 m/s^2, stopping from 4 m/s in 0.91 m), so
raising the brake parameter could not change anything. The gap was that *coasting* was
unresisted: the keyboard maps a released key to neither throttle nor brake, and rolling
plus aerodynamic drag alone would have carried a 1/10 car 34 m from 4 m/s.

`CoastDecelerationRate` (2.4 per second, speed-proportional) stands for drivetrain drag
and motor back-EMF, which dominate a geared RC car and are both roughly proportional to
speed. From 4 m/s the speed now halves every 0.29 s and the vehicle rolls 1.6 m. The
resistance is drawn from the same per-axle friction budget as drive and brake, so it
cannot exceed available grip, and it is clamped so it can never reverse the vehicle.
Reverse is unaffected: the throttle is signed, and only an exactly released throttle
coasts.

`R` restarts the current episode from its start pose, keeping the episode seed so the
course is unchanged. Without it an operator who left the corridor had to quit and
relaunch, discarding the streaming history a validation run accumulates.

## Planar single-track calculation (2026-09-06)

`SingleTrackVehicleModel` is the pure calculation implementation. The generated scene now runs
it through `CombinedSlipVehicle`, whose component name and script GUID are retained for existing
keyboard, ROS2, planner and calibration references. The four-wheel integrators have been removed.
Passing calculation and integration tests does not close the manual driving/streaming gate.

The public [F1TENTH dynamic-model implementation](https://github.com/f1tenth/f1tenth_gym/blob/main/gym/f110_gym/envs/dynamic_models.py)
uses a single-track dynamic model with a kinematic branch below 0.5 m/s. We retain that
low-speed design principle, not its truck parameters or its exact differential equations.
This independently implemented solver uses body velocities, two axle impulses and bounded
semi-implicit tire forces, so the low-speed branch cannot create unlimited grip or reset yaw.

### Contract and assumptions

- `SingleTrackParameters.FromSpecification` takes mass, axle distances, yaw inertia, steering
  limit and drivetrain force/speed limits from the TT-02 specification. Gravity is explicitly
  9.81 m/s²; the calculation does not read scene gravity or suspension state.
- State is CG position `(X, Y)`, heading, body longitudinal/lateral velocity, yaw rate and actual
  steering angle, in SI units and radians. X is forward at zero heading, Y is left, and positive
  heading/steering turns left. Heading is wrapped to `[-pi, pi]`; comparisons must account for
  that wrap. Unity's right/forward axes and steering sign require an explicit adapter.
- Inputs retain the names `Throttle`, `Brake`, `Steering`, `SurfaceFriction`. Throttle and
  steering are clamped to `[-1, 1]`, brake to `[0, 1]`; non-finite input and negative friction
  are rejected. Zero friction is valid and must produce no tire force.
- Steering is rate-limited. The provisional 3.2 rad/s limit is an actuator assumption, **not
  a measured TT-02 servo specification**. It remains configurable.
- Drive force follows the existing linear no-load-speed envelope, with equal per-wheel AWD
  torque. Brake overrides throttle and opposes rolling motion without reversing it. There is
  no wheel-spin integrator, suspension, vertical motion, load transfer or rolling resistance.
  Neutral coasting therefore need not come to a stop; braking must stop it.
- `Step(state, input, dt)` returns a new state, front/rear average forces and the dynamic blend
  weight. It does not mutate the supplied values or access a Rigidbody, Transform, clock or
  random generator. It accepts `0 < dt <= 0.02 s`; the intended runtime step is 0.01 s.
  There are no internal substeps or allocations on the per-step path.

### Force calculation and low-speed transition

Static axle loads are `Fzf = m*g*lr/(lf+lr)` and `Fzr = m*g*lf/(lf+lr)`. Each axle has a
friction impulse budget `mu*Fz*dt`. Longitudinal impulse consumes part of that budget; the
remaining lateral limit is `sqrt((mu*Fz*dt)^2 - Jx^2)`. Thus both axles satisfy the friction
circle and their total world acceleration cannot exceed `mu*g`. An impossible corner must
produce slip, not an artificially imposed turning radius.

Below 0.5 m/s, solve the front and rear no-lateral-slip constraints together, subject to those
friction bounds. Above 1.0 m/s, use the existing Pacejka lateral coefficients to calculate
secant compliance (slip velocity divided by tire force). Between the thresholds, a smoothstep
blends compliance from zero to its dynamic value. This is a **semi-implicit linearisation**,
not an exact nonlinear Pacejka ODE solution: the previous-state secant is held for one step.
The coupled 2-by-2 effective-mass system is solved with both lateral impulse bounds, including
when either or both axles saturate. No iterative convergence or 400 Hz wheel solve is needed.

State and momentum are not snapped to a kinematic target at either threshold. Tire impulses
change world momentum; integrating heading only changes the body-frame representation of that
momentum. Unpowered tire constraints dissipate energy rather than supplying it. With adequate
grip the low-speed steady radius tends to
`R = (lf+lr)/(cos(beta)*tan(delta))`, where `beta = atan(lr*tan(delta)/(lf+lr))`.

### Runtime ownership and telemetry

The adapter reads **Rigidbody pose and velocity afresh on every FixedUpdate**, computes the two
axle forces, and submits their resultant force and yaw torque to PhysX. It never assigns the
calculation's predicted pose or velocity back to the body. Thus wall impulses, episode teleports
and floating-origin translations cannot be overwritten by stale model state. Only actual steering
actuator angle persists internally. The adapter runs after ordinary controllers (execution order
100), before the diagnostic recorder (1000).

The [Unity force API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody.AddForce.html)
queues forces for the following physics simulation. PhysX owns pose integration and collision;
the calculation's trapezoidal pose integration is used only by standalone calculation callers.
The runtime uses the model's forces, not a second pose integration. Mass/yaw inertia come from
the same specification, gravity and damping are disabled, vertical/pitch/roll motion is constrained,
and continuous collision detection is enabled for the body's wall-collision envelope.

Coordinate mapping is explicit: model world `(X,Y)` is Unity `(z,-x)`, body velocity `(u,v)` is
Unity local `(z,-x)`, and model yaw/steering signs are opposite Unity's. The **public component**
keeps positive steering = right, so D/right-arrow and the existing ROS2 mapping remain unchanged.
`CaptureState()` reads the body, including a rebase already performed in the current frame.
`ResetState()` clears commands, actual steering and diagnostics; the episode owner resets the body.

Dynamics JSONL now uses **schema 2**, `vehicleModel: planar-single-track`, with `axles` ordered
front/rear. Normal loads are analytical flat-ground support, not ray-cast measurements. Axle
forces use forward/left signs; body velocity retains Unity local axes. There are no invented
wheel angular speeds, grounded masks or suspension traces. Sampling is explicitly labelled
`pre-physics-after-tire-force-queue`: logged forces are queued tire forces, not wall reaction forces.
Actual body responses appear in subsequent samples.

Streaming logs/validator now check actual steering, axle-force and dynamic-weight continuity in
addition to pose, velocities and inputs. Old wheel-schema logs cannot validate this replacement.
These immediate checks still cannot replace observing the vehicle as it crosses new geometry.

### Verification and remaining acceptance

`SingleTrackVehicleModelTests` checks stationary steering, finite actuation, forward/reverse
drive and braking, axle friction circles and world acceleration, continuity at both blend
boundaries in both directions, steady-turn radius, over-limit slip, mirror symmetry, energy
dissipation, the reported 5.6 rad/s spin perturbation, position-rebase invariance and timestep
convergence. These are synthetic model checks, not real-car calibration or wall-contact tests.
The 2026-09-06 integration run passed **29/29** dedicated calculation cases, **128/128** EditMode
cases overall and **17/17** graphics-enabled PlayMode cases with Unity 6000.0.61f1. The latter
include eight isolated PhysX-adapter cases (real wall stop/back-away, collision-state feedback,
rebase, reset, steering signs and force integration), plus production streaming transitions and
subsequent physics-step comparison against a control vehicle. Ten Python streaming-validator
tests pass. These are automated checks, **not a completed human manual-drive assessment**.

The scene smoke test and `scripts/build_linux.sh` also passed. The rebuilt Linux player ran a
single 12-second reference-planner episode (seed 10000): 24.28 m progress, no collision or departure,
then the configured timeout (not a completed lap). All 1,200 dynamics frames use schema 2/two axles;
peak speed was 2.10 m/s, peak step acceleration 4.62 m/s² and vertical velocity stayed zero.
Local evidence is in `Build/validation/single-track-runtime-001/`. This reference run reports
zero camera-observation frames and is **not** camera-policy or manual-streaming evidence.

Run them using the project's Unity editor:

```bash
"$UNITY_BIN" -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode -testFilter SingleTrackVehicleModelTests \
  -testResults /tmp/single-track-tests.xml -logFile /tmp/single-track-tests.log
```

After rebuilding, repeat the captured manual streaming drive and operator checklist, then collect
and validate a human demonstration. Neither a successful build nor these automated tests certifies
the handling, visual quality or real-car calibration.

## Competition context

The target event is **MAGP (Minicar Autonomous Grand-Prix)**, the Japanese localisation of
RoboRacer (formerly F1TENTH). It runs 1/10 scale RC vehicles and defines three classes:

| | RoboRacer class | **MAGP class** | Open class |
| --- | --- | --- | --- |
| Chassis | Traxxas Slash 4x4 Premium (6804R) | TT-02 series (TYPE-S(RX), R(R)) or M series (M-05, M-07, M-08) | approx. 1/10 scale |
| Motor | Velineon 3500 kV, single DC motor only | Tamiya 540-N brushed, or brushless T21.5 / T17.5 (T15.5 and T10.5 prohibited) | per RoboRacer rules |
| Speed controller | — | TBLE-04S or TBLE-04SR (Tamiya official only) | — |
| Battery | single 3-cell LiPo | Tamiya 7.2 V Ni-MH, 6.6 V LiFe, or 2-cell equivalent | — |
| Tires | — | Tamiya official only; adhesive compounds prohibited | — |
| Size | — | 300–400 mm length, wheelbase 200 mm or more | — |

Common rules: sensors that directly localise globally (indoor GPS and similar) are prohibited,
and all computation must run onboard.

The MAGP class is the Tamiya route and is assumed to be this project's target. **Confirm the
chassis before the parameters below are treated as final.**

## Target vehicle: Tamiya TT-02

Published geometry, from the manufacturer:

| Quantity | Value |
| --- | --- |
| Wheelbase | 257 mm (short 251 mm, buggy 266 mm) |
| Chassis length × width | 385 × 187 mm (TYPE-S) |
| Tire width / diameter | 24 / 64 mm, front and rear (radius 0.032 m) |
| Gear ratio | 8.27:1 (on-road standard) |
| Drivetrain | longitudinal motor, shaft-drive 4WD |

The MAGP class also fixes the drivetrain. This project assumes a Tamiya brushless 02 sensored
motor and the 8.27:1 on-road gear ratio:

| Motor | Item | KV | Voltage | Mass |
| --- | --- | --- | --- | --- |
| T17.5 | OP.1894 (TBLM-02S) | 2200 | 6.6–7.2 V | 185 g |
| T21.5 | OP.1895 (TBLM-02S) | 1800 | 6.6–7.2 V | 185 g |

Everything else — mass, centre of gravity, inertia, suspension rates, tire force
characteristics — is **not published by Tamiya**. No physical vehicle is available yet, so the
values below are derived or estimated from the published data. Each carries its provenance;
the telemetry calibration chain exists precisely to correct them once a real car is measured.

### Adopted parameter set

| Quantity | Value | Provenance |
| --- | --- | --- |
| Wheelbase | 0.257 m | published |
| Tire radius | 0.032 m | published (64 mm diameter) |
| Body length | 0.385 m | published (chassis length) |
| Track width | 0.166 m | derived: 190 mm class width limit − 24 mm tire width |
| Body width / height | 0.19 / 0.11 m | estimated |
| Mass | 2.1 kg | derived, build-up below |
| CG to front / rear axle | 0.134 / 0.123 m | estimated 48/52 front/rear distribution |
| CG height | 0.054 m | derived, build-up below |
| Yaw inertia | 0.016 kg·m² | derived by scaling the reference platform |
| Suspension rest length | 0.02 m | estimated (touring car travel is short) |
| Spring rate | 2000 N/m | derived from 2.5 mm static sag |
| Damper rate | 35 N·s/m | operationally tuned, ~0.54 of critical damping |
| Maximum wheel normal force | 13 N | operational safety cap, 2.5× static corner load |
| Wheel rotational inertia | 6×10⁻⁵ kg·m² | derived, wheel plus reflected rotor |
| Drive / brake torque per wheel | 0.148 N·m | derived, traction limit |
| No-load wheel speed | 200 rad/s | derived from T17.5 / 8.27:1 drivetrain |
| Max steering angle | 25° | estimated |
| Surface friction | 0.9 | estimated (adhesive tires are prohibited) |

These live in `VehicleSpecification.Default`, which the scene builder reads. `VehicleSpecification`
is a settings struct with a `Default` property, matching `CourseGenerationSettings` and
`DomainRandomizationSettings` rather than introducing a new asset type.

### Derivations

**Mass.** A lightened TT-02 TYPE-S racing build is reported at 1358 g. Adding a 2D LiDAR
(~170 g), a single-board computer with case and cooling (~250 g), mounting plates (~100 g) and
a power bank (~200 g) gives ≈ 2.08 kg, rounded to **2.1 kg**.

**CG height.** Chassis, drivetrain and drive battery — about 1.36 kg — sit low in the tub at
roughly 0.035 m. The autonomy payload, about 0.72 kg, rides on the top plate at roughly
0.09 m. The mass-weighted result is **0.054 m**.

**Yaw inertia.** The reference platform gives a dimensionless
`I_z / (m · wheelbase²) = 0.04712 / (3.74 × 0.3302²) = 0.1156`. Applying that ratio to this
vehicle: `0.1156 × 2.1 × 0.257² =` **0.016 kg·m²**.

**Drive torque.** With four driven wheels the tires saturate before the drivetrain does, so the
useful bound is the traction limit `μ · m · g · r / 4 = 0.9 × 2.1 × 9.81 × 0.032 / 4 =`
**0.148 N·m** per wheel. `VehicleSpecification.TractionLimitedWheelTorque` computes this and a
test pins the default to it.

Motor cross-check: the total 0.593 N·m at the contact patches needs
`0.593 / (8.27 × 0.85) = 0.084 N·m` at the motor. With `Kt = 9.5493 / KV`, that is 15.9 A for
the T21.5 and 19.4 A for the T17.5 — comfortably inside the regulation drivetrain. **Traction,
not the motor, is the binding constraint.**

**Top speed.** At 7.2 V the no-load figures are 12 960 rpm (T21.5) and 15 840 rpm (T17.5).
Through 8.27:1 onto a 0.201 m tire circumference that is 5.3 and 6.4 m/s unloaded, so roughly
**4–5 m/s in practice**.

The vehicle model uses **200 rad/s** as its common maximum wheel angular velocity
(6.4 m/s / 0.032 m, rounded down). Drive torque follows a first-order DC
torque–speed envelope and becomes zero there. This prevents an airborne wheel
from accumulating energy that the regulated drivetrain cannot supply.

**Historical four-wheel parameters.** Suspension and wheel-inertia values below no longer
participate in the dynamics solve. Static ride-height geometry still uses the old sag estimate.

**Suspension.** Static load per wheel is `2.1 × 9.81 / 4 = 5.15 N`. Assuming 2.5 mm of static
sag gives `k ≈ 2060 N/m`, rounded to **2000 N/m**. Critical damping per corner is
`2√(2000 × 0.525) = 64.8 N·s/m`. The initial 25 N·s/m estimate visibly
under-damped the generated car, so the current operational value is **35 N·s/m**
(~0.54 critical) pending real-vehicle damper telemetry. A fully compressed
uncalibrated ray-cast spring can otherwise turn a vertical hop into excessive tire
force, so contact is capped at **13 N** per wheel (2.5× the 5.15 N static load)
until real bump-stop and damper traces are available.

The ray-cast model has no unsprung wheel mass. On rebound its contact force cannot
be tensile, so it creates a false contact/liftoff cycle when the rigid body is free
to heave. MAGP's generated venue is flat, so the operational scene freezes vertical
translation plus pitch and roll; forward, lateral, and yaw dynamics remain active.
Do not treat this planar stabilisation as suitable for banking, jumps, or uneven
roads: those require a full unsprung wheel/suspension model.

**Wheel rotational inertia.** A 45 g wheel and tire at 0.032 m contributes `½mr² = 2.3×10⁻⁵`.
A ~30 g rotor of ~6.25 mm radius contributes `5.9×10⁻⁷`, reflected through the 8.27:1 gearing
as `× 8.27² = 4.0×10⁻⁵`. The total is **≈ 6×10⁻⁵ kg·m²**, and the reflected rotor dominates.

### What changes when a real vehicle exists

Mass, CG and yaw inertia are the values worth measuring first — a scale, split axle weights and
a tilt test cover the first three in well under an hour. Surface friction should not be measured
directly; it is what the telemetry calibration chain identifies from steady-state cornering.

## Reference platform (not the target)

The F1TENTH parameter set is recorded here because it is the only **identified** 1/10 scale
data available, and it is useful for sanity-checking measurements and for the derivations
below. It describes the Traxxas Slash 4x4, i.e. the RoboRacer class — **not** the TT-02.

| Quantity | Value |
| --- | --- |
| Mass | 3.74 kg |
| CG to front / rear axle | 0.15875 / 0.17145 m (wheelbase 0.3302 m) |
| CG height | 0.074 m |
| Yaw inertia | 0.04712 kg·m² |
| Track width | 0.296 m |
| Tire diameter | 0.1095 m |
| Body length × width | 0.58 × 0.31 m |
| Max steering angle | 0.4189 rad = 24.0° |
| Max steering rate | 3.2 rad/s |
| Max acceleration | 9.51 m/s² |
| Friction coefficient | 1.0489 (identified on concrete floor) |
| Cornering stiffness front / rear | 4.718 / 5.4562 [1/rad] |

These values were identified on a concrete floor with the default F1TENTH configuration, and
its geometry cross-checks against the Traxxas specification (324 mm wheelbase, 296 mm track).
A second, minimum-lap-time parameter set also circulates (`I_z` 0.128 kg·m², CG height
0.045 m, track 0.161 m); its track width contradicts both the manufacturer figure and the same
project's body width, so it is not used here.

**Do not transplant these numbers into the TT-02 model.** The wheelbase differs by 1.3×, the
tire radius by 1.7×, and the two vehicles are different categories: the Slash is a
short-course truck with long, soft suspension travel and a high CG, while the TT-02 is an
on-road touring car. Only the derivation methods below transfer.

## Derivation methods

**Drive torque from peak acceleration.** `F = m · a_max`, and the total torque at the contact
patches is `F · r`. `_maximumDriveTorque` is applied **per driven wheel**, so divide by the
number of driven wheels. For the reference platform: `3.74 × 9.51 = 35.6 N`, `× 0.05475 =
1.95 N·m`, `/ 4 = 0.49 N·m` per wheel.

**Pacejka `B` from cornering stiffness.** For small slip our magic formula linearises to
`dFy/dα ≈ B · C · D · μ · Fz`. Matching that against a load-normalised cornering stiffness
`C_S` [1/rad] gives `B = C_S / (C · D)`. For the reference platform with `D = 1` and `C = 1.3`
this yields `B ≈ 3.63` (front) and `B ≈ 4.20` (rear). **Verify where μ sits in the source
model's convention before adopting these numbers.**

## Gap against the current placeholder

The scene values predate this work and are placeholders, not a different design:

| Parameter | Placeholder | TT-02 | Ratio |
| --- | --- | --- | --- |
| Wheelbase | 1.3 m | 0.257 m | 5.1× |
| Track | 0.96 m | ≤ 0.187 m (chassis width) | ≥ 5.1× |
| Tire radius | 0.15 m | 0.032 m | 4.7× |
| Mass | 30 kg | to be measured (order 1–2 kg) | ~20× |
| CG | (0, 0, 0) | to be measured | — |
| Inertia tensor | (1, 1, 1) | to be measured | — |
| Drive torque (per wheel) | 65 N·m | to be derived from motor and 8.27:1 | — |

## Course-scale alignment

The training distribution is now rescaled before data collection: course samples are 0.1 m
apart, straights are 2–8 m, corner radii are 1.4–4 m, and design speed is 3.5 m/s on a 0.9-
friction surface. The runtime road is 1.0 m wide with 0.08 m boundary walls; off-course is
0.45 m from the centreline, planning offsets use a 0.5 m half-width, and the opponent targets
3 m/s. Opponent ride height and generated hazard height derive from `VehicleSpecification`
rather than a placeholder constant.

This deliberately invalidates pre-scale demonstrations; collect or train only after this change
so that the BC and RL data distribution matches the TT-02 model.

The scene itself is generated, so after changing `VehicleSpecification` it must be rebuilt via
`Physical AI/Create Training Scene`, or in batch mode with
`-executeMethod PhysicalAi.Editor.PhysicalAiTrainingSceneCreator.CreateOrReplace`.

## Implementation notes and limitations

1. **Slip convention.** `CombinedSlipTireModel` takes the slip angle in radians and a
   normalised slip ratio clamped to ±3, not the classical degrees/percent convention.
   A `B` taken from literature must match the convention it was fitted in.
2. **`D` is normalised.** It expresses the peak as a fraction of `μ · Fz`, so `D = 1` means
   "use all available friction". Absolute peak forces do not belong in this field.
3. **No front/rear tire split.** `CombinedSlipVehicle` holds one lateral coefficient set for
   all four wheels, so a front/rear cornering-stiffness difference cannot be represented and
   the resulting understeer bias is lost.
4. **Steering rate is instantaneous.** A servo rate limit is not modelled; `_steering` is
   applied directly each `FixedUpdate`.
5. **Friction is surface-specific.** Both the domain randomisation range and the telemetry
   calibration should be centred on the surface actually raced on.

## Dynamics-level friction-circle verification

`CombinedSlipVehiclePlayModeTests` instantiates the TT-02 specification with four ray-cast
suspension contacts on a Unity rigidbody, commands simultaneous full drive and steering for
120 physics steps, and records the peak tire-force utilisation from the running vehicle. The
test requires sustained contact, nonzero utilisation, finite rigidbody velocity, and a peak no
greater than `1.0001`; it therefore verifies the friction-circle clamp at the vehicle dynamics
level rather than only through the pure tire-model unit test.

```bash
UNITY=~/Unity/Hub/Editor/6000.0.61f1/Editor/Unity
$UNITY -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode \
  -testResults Logs/playmode.xml -logFile /dev/stdout
```

## Sources

- MAGP regulations: <https://minicar-autogp.org/race/regulation/>
- MAGP overview: <https://minicar-autogp.org/race/>
- TT-02 chassis specification (Tamiya):
  <https://www.tamiya.com/japan/products/product_info_ex.html?genre_item=0111>
- F1TENTH build documentation (Traxxas Slash 4x4 Premium chassis):
  <https://f1tenth.readthedocs.io/en/stable/getting_started/build_car/bom.html>
- `f1tenth_gym` default vehicle parameters:
  <https://f1tenth-gym.readthedocs.io/en/v1.0.0/api/env.html>
- Traxxas Slash 4x4 manufacturer specification: <https://traxxas.com/slash-4x4-vxl-68386-4>
