# Investigation: vehicle oscillation

## Point in time

Everything below was measured against this exact state. Re-measure before trusting it.

| | |
| --- | --- |
| Investigated | 2026-08-30, 22:38 JST |
| `dev` HEAD | `7d5ff551` *Record opt-in reference policy demonstrations*, 2026-08-30 21:02 JST |
| Working branch | `goal/display-camera-and-default-scene`, based on `7d5ff551` |
| Player build | `Build/AWSIM.x86_64`, 2026-08-30 22:17 JST |
| Fixed timestep | 1411199 / 141120000 s = **0.01 s exactly** (100 Hz) |
| Data source | `-physicalAiEvaluation holdout -physicalAiRecordReferenceDemonstrations`, reference policy, holdout seed 2027 |

The working branch differs from `dev` only in the display camera and the default
start scene. Neither touches physics, so these measurements describe `7d5ff551`.

## Reported symptom

The vehicle visibly shakes and its motion looks unlike a real car.

## Confirmed: the oscillation is real and non-physical

From 994 recorded steps (9.9 s) of one holdout episode, using the observation's
speed channel:

| Measurement | Value |
| --- | --- |
| Speed range | 0.00 – 6.12 m/s (mean 4.74) |
| Step-to-step speed change, mean | 0.047 m/s per 0.01 s = **4.7 m/s²** |
| Step-to-step speed change, max | 0.596 m/s per 0.01 s = **59.6 m/s²** |
| Sign reversals of acceleration | 466 in 994 steps |
| Dominant half-period (steady state) | **1 step** |

Two figures settle it.

**59.6 m/s² is 6.7× the nominal traction limit.** With μ = 0.9 the tires can
deliver at most `μg = 8.83 m/s²` at static load. The body is being accelerated far
beyond that.

**The dominant oscillation period is exactly 2 steps = 50.0 Hz**, which is precisely
the Nyquist frequency of the 100 Hz timestep. A physical resonance lands on a
physical frequency; an oscillation pinned to Nyquist is the signature of an
unstable explicit integrator. The half-period histogram is dominated by 1-step runs
(194 occurrences) followed by 2-step (145), i.e. the state alternates almost every
step.

## Root cause: the wheel spin integration is unstable at this timestep

`CombinedSlipVehicle.Wheel.IntegrateWheel` advances wheel angular velocity with
explicit Euler:

```
netTorque = driveTorque - longitudinalForce * radius - brakeDirection * brakeTorque
_angularVelocity += netTorque / _rotationalInertia * deltaTime
```

Longitudinal force feeds back into that torque through slip, and slip depends on
angular velocity, so this is a stiff feedback loop. Linearising it:

- Slip stiffness `dF/ds = B·C·D·μ·Fz = 10 × 1.9 × 1 × 0.9 × 5.15 N = 88.1 N` per unit slip
  (static wheel load `Fz = mg/4 = 5.15 N`)
- `s = (ω·r − v) / max(1, |v|)`, so `∂s/∂ω = r / max(1, |v|)`
- Torque feedback `dτ/dω = −(dF/ds)(∂s/∂ω)·r`
- Time constant `τ = I / |dτ/dω|` with `I = 6×10⁻⁵ kg·m²`

| Condition | τ | Explicit-Euler stability limit `dt < 2τ` | Actual `dt` |
| --- | --- | --- | --- |
| Cruise, v = 4.7 m/s | 3.13 ms | 6.25 ms | 10 ms — **unstable** |
| Low speed, v ≤ 1 m/s | 0.67 ms | 1.33 ms | 10 ms — **7.5× over** |

The step is above the stability limit in both regimes, so the wheel state
alternates and grows until the tire's nonlinearity bounds it. That produces a
sustained oscillation at the Nyquist frequency, which is what the data shows.

The `max(1, |v|)` clamp in the slip denominator makes this worse below 1 m/s: it
inflates `∂s/∂ω` by up to 4.7× relative to cruise, which is why the stability
margin collapses at the start of every episode.

## Why the acceleration exceeds the traction limit

The friction circle is enforced per instant against the *current* normal load, and
that load is itself oscillating. Suspension travel is 0.02 m at 2000 N/m, so a
fully compressed corner produces `2000 × 0.02 = 40 N`, **7.8× the 5.15 N static
load**. At that load the friction circle permits `μ · 40 N · 4 / 2.1 kg = 68.6 m/s²`.
The measured 59.6 m/s² sits just under that ceiling.

So the friction circle is not being violated. The normal force it is evaluated
against is unphysically spiky, which lets the tires briefly generate forces a real
1/10 car never could.

Suspension itself is not the primary instability — natural frequency 9.82 Hz,
damping ratio 0.39 — but at `dt = 0.01 s` it is sampled only ~10 times per cycle,
which is marginal, and it is being excited by the wheel-spin oscillation.

## Limits of this evidence

- The speed channel is `Rigidbody.linearVelocity.magnitude`, which **includes the
  vertical component**. Part of the measured variation may be vertical hop rather
  than longitudinal surging. Separating them needs per-axis logging, which the
  observation does not currently carry.
- Wheel angular velocity, per-wheel normal force and `PeakTireUtilization` are not
  recorded, so the causal chain above is inferred from the timestep signature and
  the linearised model rather than observed directly.
- One episode, one seed. The 50 Hz signature was checked only on holdout seed 2027.

## Remediation and follow-up measurement

1. **Wheel spin is now integrated with an implicit contact reaction.**
   `CombinedSlipVehicle.Wheel.IntegrateWheel` uses the linearised contact damping
   in its effective inertia. The PlayMode regression checks that full-throttle
   horizontal step acceleration remains below three times static traction; the
   pre-fix implementation reached about 60 m/s².
2. **Static ride height now uses the weight-derived spring sag.** The prior scene
   compressed each 2000 N/m spring by 10 mm (20 N per wheel) before the car moved.
   The TT-02's static sag is `mg/(4k) = 2.58 mm`. The regenerated scene reduced
   maximum logged normal load from 65.2 N to 48.8 N and maximum vertical step
   acceleration from 45.8 to 31.7 m/s² in the same holdout suite. Remaining load
   spikes still require calibration.
3. **Per-wheel diagnostics are now available.** Pass
   `-physicalAiRecordDynamicsTelemetry` with `-physicalAiReportDirectory <path>`
   to write JSONL by seed. Each step carries body velocity, normalized actuator
   commands, wheel speed, normal load, spring compression, damper velocity, slip,
   tire forces and friction-circle utilisation without changing BC observations.
4. **Drive torque now follows a motor speed envelope.** The former constant-torque
   source accelerated airborne wheels without bound. `VehicleSpecification` limits
   the T17.5-equivalent wheel to 200 rad/s, where drive torque falls to zero. A
   fixed holdout suite measured 189.6–193.8 rad/s maxima, reduced completion-run
   peak acceleration from about 70 to 52 m/s², and improved reference completion
   from 25% to 37.5%. The remaining departures are control/ride-dynamics work,
   rather than motor energy injected while airborne.
5. **Reduce the fixed timestep** to ≈2 ms remains an escalation option, but
   physics cost roughly fivefold, which matters for a project whose throughput
   comes from driving time in a single environment.
6. **Replace the `max(1, |v|)` slip clamp** with a relaxation-length formulation, so
   low-speed slip stops being artificially stiff.

7. **Keep episode reset and contact sampling in the same physics state.** A later
   diagnostic replay found that `ResetVehicleState` could be called from an
   `EpisodeFinished` callback during `FixedUpdate`. It reset the Rigidbody but put
   it to sleep before the child wheel mounts had been synchronised. After an
   off-course departure, the following holdout seed could therefore retain the
   previous pose for its wheel raycasts, observe zero grounded wheels, and time out
   without moving. The reset now calls `Physics.SyncTransforms()` and `WakeUp()`.
   This is an evaluation-validity correction, not a suspension tuning change.

The dynamics JSONL also records normalized throttle, brake, and steering from the
low-level controller. This permits the next calibration pass to distinguish an
actuator/controller excursion from a contact-load excursion without changing the
policy observation contract.

8. **Only drivable surfaces can support a wheel.** `CombinedSlipVehicle` formerly
   ray-cast against every collider.  That let a wheel which had left the course
   mistake a duct-wall top, an opponent, or a hazard for road and receive a
   suspension impulse.  The generated floor and road segments now use the
   `PhysicalAiGround` layer and wheel raycasts are restricted to that layer.
   Rendered road pieces have no collider, so the continuous venue floor is the
   only tire-contact surface; boundaries remain collidable to sensors but cannot
   become tire contact.

## Current fixed-suite baseline

After the reset-contact correction, `Build/evaluations/post-reset-dynamics-001`
ran the fixed eight holdout seeds without an artificial airborne timeout:

| Metric | Result |
| --- | --- |
| Reference-policy completion | 3 / 8 (37.5%) |
| Mean completed lap time | 10.43 s |
| Collision / timeout | 0 / 8, 0 / 8 |
| Off-course | 5 / 8 |
| Per-wheel mean normal load | 5.2 N (matches the 5.15 N static target) |
| 95th-percentile / maximum normal load | 13.5–16.8 N / 34.7–60.4 N by seed |

The diagnostic extension separates the peak into spring compression and damper
velocity. At several peaks the 20 mm suspension travel is fully consumed, with
roughly 40 N spring force plus 13–19 N compression damping. This is a real model
calibration issue, not a friction-circle violation or the former wheel-spin
instability. Do not tune a learned planner against these excursions: first compare
the logged compression/velocity traces to real TT-02 suspension telemetry, then
select a relaxation-length or suspension parameter change and rerun this exact
suite.

Raising `WheelRotationalInertia` would also stabilise the loop, but `6×10⁻⁵ kg·m²`
was derived in [the vehicle model](physical-ai-vehicle-model.md) from wheel geometry
plus reflected rotor inertia. Inflating it to buy stability would trade a numerical
problem for a physical error.

## Reproducing this

```bash
cd /home/ares-22/tamiya/AWSIM-TEST
./Build/AWSIM.x86_64 -batchmode -nographics \
  -physicalAiEvaluation holdout -physicalAiMaximumEpisodeSeconds 30 \
  -physicalAiRecordReferenceDemonstrations \
  -physicalAiReportDirectory /tmp/dynamics -physicalAiQuitAfterEvaluation \
  -logFile /tmp/dynamics/run.log
# Recordings land in ~/.config/unity3d/TIERIV/AWSIM/PhysicalAiDemonstrations/
# Observation[0] is the speed channel used above.
```

For the current diagnostic path, use an available graphics display: HDRP does not
support `-nographics` in the player.

```bash
Build/AWSIM.x86_64 -batchmode \
  -physicalAiEvaluation holdout -physicalAiMaximumEpisodeSeconds 30 \
  -physicalAiRecordDynamicsTelemetry \
  -physicalAiReportDirectory Build/evaluations/dynamics-diagnosis \
  -physicalAiQuitAfterEvaluation
```
