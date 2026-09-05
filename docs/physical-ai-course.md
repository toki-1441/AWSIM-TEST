# Map and course

The [goals document](physical-ai-goals.md) fixes a **hybrid map specification**: an endless
streaming road for training, and closed-loop finite courses for holdout evaluation. This page
records both, plus the real-world course the holdout set is modelled on.

## Training map: endless streaming

A single never-ending road, generated in chunks ahead of the vehicle, with passed chunks
discarded behind it. The reasons are throughput and realism: no episode-reset overhead in a
deliberately non-parallel environment, and it matches the deployment case of one attempt on a
road never seen before.

This is now implemented in part; the remaining constraints are explicit:

- **Floating origin is active for streamed training.** At a configurable planar-distance
  threshold, the vehicle is returned near the origin and the centreline, retained road and wall
  geometry, opponents, and active hazards shift by the same offset. Ego-relative observations
  are therefore unchanged.
- **Chunk lifecycle is incremental.** `StreamingCourseState` deterministically continues chunks
  from its authoritative prior end pose and heading; floating-origin translation moves the
  generator frontier as well as the retained points. `ProceduralCourseTrackBuilder.Append` adds only the new
  road and wall colliders, then discards a retained-window-external rear section. Once the
  configurable logical-history cap is reached, the same retained waypoint window is rebased;
  progress and physical-segment indices remain continuous.
- **Intersection admission uses the XZ course plane.** True non-adjacent segment crossings are
  rejected even inside the locally connected clearance exemption. Parallel-segment distance is
  calculated entirely from projected XZ points; Unity's implicit `Vector3`→`Vector2` conversion
  is not used. Every admitted chunk still contains one parameterised left or right corner.
- **The initial cache is bounded.** Three chunks provide at least 13.5 m from the configured
  minimum primitive lengths, beyond the 6 m rendered sensor horizon. Runtime extension starts
  while approximately 12 m remains, rather than prebuilding twelve chunks and stalling startup.
- **"Course completed" is not an outcome in training.** The orchestrator extends before the
  streamed tail is reached. Evaluation explicitly selects the separate finite holdout loop.
- **Every world-management event is instrumented.** The transition record identifies the
  command source and compares pose, rotation, linear/angular velocity, throttle/brake/steering,
  the per-wheel grounded mask, suspension compression and suspension velocity immediately before
  and after append/retirement/rebase work. This distinguishes an actual contact or command
  discontinuity from a purely visual defect and prevents a reference-driver run from being
  presented as manual evidence.

## Evaluation map: closed-loop holdout

Fixed, separately generated closed circuits, scored by lap time and the metrics in the goals
document. These are deliberately outside the training distribution — downscaled real courses are
the intended source — which is what makes the zero-shot claim measurable.

`HoldoutCourseSuite` pins eight seeds and `ClosedHoldoutCourseGenerator` maps them to
deterministic rounded-rectangle circuits within the MAGP envelope. This generator is separate
from the open-chain training generator, and `HoldoutEvaluationRunner` explicitly selects it via
`ResetHoldoutEpisode`. Each result is therefore a one-lap closed-circuit measurement rather than
a training-map completion.

The current variants are synthetic MAGP-envelope circuits, not yet downscaled surveyed real
tracks. Replacing their fixed parameter table with surveyed geometry is a data-import task that
does not change the evaluation contract.

## Real-world reference: the MAGP course

MAGP publishes a course specification, and it is the authority for what a real event looks like.
It is the archetype the holdout courses should imitate.

- Built on flat ground of roughly **20–30 m × 10 m**.
- Walls are made from **duct** (MonotaRO item 00944665). **The joints leave gaps that LiDAR light
  passes through** — the walls are not a clean, continuous reflector.
- **Minimum course width is about three car bodies.**
- **A single-colour line is drawn on the course**, its colour depending on the floor colour, and
  it is explicitly **not** a centreline.
- Laps are counted as one circuit **from a checkpoint**; after a restart, lap 1 counts on the
  second checkpoint pass.
- **Collision with the wall is an expected event**, with defined restart procedure in both
  qualifying and the final.
- Format: two qualifying time attacks, then a knock-out final — head-to-head running is part of
  the competition.
- The third event ran in a conference room, so the floor is **conference-room carpet**.

### Reference images

**Competition-style course.** Grey carpet; white-and-red duct walls forming a closed polygonal
boundary with bracketed joints; coloured lines on the carpet; small traffic cones; a `P` parking
area with white hatching; a gate; a tunnel. This matches the regulation.

**Commercial RC circuit.** Smooth black vinyl tiles; painted racing lines and direction arrows;
artificial-turf run-off; red-and-white kerbs; guardrails. Not a MAGP-regulation course — no duct
walls, and a lower-grip surface. It is useful as a **second surface and a flowing circuit
geometry for domain randomisation and holdout variety**, which is exactly the kind of variation
that must not live in the training distribution alone.

## Adopted parameters

Derived from the vehicle specification and the MAGP envelope. They apply to both map forms; only
the topology differs.

| Quantity | Value | Provenance |
| --- | --- | --- |
| Course envelope (holdout only) | 30 × 10 m | MAGP regulation |
| Minimum course width | 0.57 m (3 × 0.19 m body width) | regulation, interpreted |
| Nominal course width | 1.2 m | selected after swept-body validation at the 1.4 m minimum radius |
| Wall height | 0.06–0.10 m — **to be confirmed** | estimated from the duct and the images |
| Surface friction | 0.9 carpet, lower for the vinyl variant | see the vehicle model |
| Corner design speed | 3.5 m/s | below the 4–5 m/s top speed |
| Minimum corner radius | 1.4 m | derived: `v² / (μg) = 3.5² / (0.9 × 9.81)` |
| Maximum corner radius | 4.0 m | fits the envelope |
| Straight length | 2–8 m | fits the envelope |
| Centreline sample spacing | 0.1 m | 1 m cannot resolve a 1.4 m radius |
| Streaming distance ahead / behind | extend at 12 m / retain 8 m (120 / 80 samples) | exceeds the 6 m camera horizon; scene configuration |
| Initial streaming cache | 3 chunks, at least 13.5 m | bounded startup while exceeding the camera and stopping horizon |
| Logical waypoint history | at most 400 samples | bounds collision admission and floating-origin bookkeeping |

A 1.4 m radius hairpin needs `2R + width = 3.8 m` across, fitting the 10 m dimension. Cornering at
3.5 m/s on that radius draws `v²/R = 8.75 m/s²`, essentially the friction limit, so the two
numbers are consistent by construction.

## Gaps against the current implementation

Beyond the streaming requirements above:

1. **Straight-segment side walls are generated.** `ProceduralCourseTrackBuilder` now builds
   collider-backed 0.08 m side walls for every road segment, which are visible to the auxiliary
   LiDAR and terminate an episode on impact. Corner joins remain deliberately simple; a
   continuous duct-wall mesh and its regulation-style gaps are still future fidelity work.
2. **A yellow rendered centreline is generated** just above the road slab. It is a visual
   training cue for the camera pipeline only; the physical course still uses the road and walls,
   and the regulation's exact floor-dependent line colour remains a domain-randomisation input.
3. **No checkpoint or lap counter**, so the metric the competition scores cannot be measured on
   the holdout courses.
4. **The 2D LiDAR mount is 0.06 m above the road**, below the default 0.08 m wall top, so the
   auxiliary scan can observe the boundary while the camera remains the primary sensor.

## Sources

- MAGP regulations: <https://minicar-autogp.org/race/regulation/>
- MAGP 2024 regulations: <https://f1tenth-japan.org/2024/regulation/>
- Third event entry page (venue): <https://minicar-autogp.org/race/entry/03rd/>
