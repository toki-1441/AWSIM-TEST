# Vehicle model

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
| Damper rate | 25 N·s/m | derived, ~0.4 of critical damping |
| Wheel rotational inertia | 6×10⁻⁵ kg·m² | derived, wheel plus reflected rotor |
| Drive / brake torque per wheel | 0.148 N·m | derived, traction limit |
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

**Suspension.** Static load per wheel is `2.1 × 9.81 / 4 = 5.15 N`. Assuming 2.5 mm of static
sag gives `k ≈ 2060 N/m`, rounded to **2000 N/m**. Critical damping per corner is
`2√(2000 × 0.525) = 64.8 N·s/m`; RC dampers typically run at 0.3–0.4 of critical, hence
**25 N·s/m**.

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

## Still sized for the placeholder

The vehicle now follows the specification, but the scene and course constants around it were
chosen for the 2 m, 30 kg placeholder and have not been rescaled. Until they are, the episode
is a 0.385 m car driving on a four-metre-wide road:

| Constant | Current | Problem |
| --- | --- | --- |
| `CourseGenerationSettings.DesignSpeed` | 12 m/s | the vehicle tops out near 4–5 m/s |
| `CourseGenerationSettings` corner radii | 5–30 m | huge relative to a 0.257 m wheelbase |
| `ProceduralCourseTrackBuilder._roadWidth` | 4 m | 21× the body width |
| `CourseProgressMonitor._maximumDistanceFromCenterline` | 2 m | off-course is never detected in practice |
| `PhysicalAiEpisodeOrchestrator._trackHalfWidth` | 2 m | lateral offsets are scaled against the wrong width |
| `PhysicalAiEpisodeOrchestrator._maximumVisibleDistance` | 40 m | far beyond a 2D LiDAR or this course scale |
| `OpponentCourseFollower._targetSpeed` | 7 m/s | above the vehicle's top speed |
| `OpponentCourseFollower` ride height | hardcoded `0.35 m` | not serialised, and sized for the placeholder |
| `BlindCornerHazardSpawner` hazard pose and scale | `0.35 m`, `(0.6, 0.7, 0.8)` | a hazard larger than the vehicle |

Rescaling these is a separate goal: it changes the course distribution and therefore invalidates
recorded demonstrations, so it should land before any data collection starts.

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
