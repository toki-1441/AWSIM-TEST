# Map and course

The [goals document](physical-ai-goals.md) fixes a **hybrid map specification**: an endless
streaming road for training, and closed-loop finite courses for holdout evaluation. This page
records both, plus the real-world course the holdout set is modelled on.

## Training map: endless streaming

A single never-ending road, generated in chunks ahead of the vehicle, with passed chunks
discarded behind it. The reasons are throughput and realism: no episode-reset overhead in a
deliberately non-parallel environment, and it matches the deployment case of one attempt on a
road never seen before.

This has consequences that the current implementation does not yet meet:

- **Floating origin is a prerequisite.** Coordinates grow without bound, so single-precision
  degradation is inevitable unless the vehicle is kept near the origin and the world is shifted
  instead. Everything holding world positions has to shift together: the centreline, built road
  and wall geometry, opponents and hazards. Ego-relative observations are unaffected, which is
  one more reason the relative encoding is the right contract.
- **Chunk lifecycle, not rebuild.** `ProceduralCourseTrackBuilder` currently clears and rebuilds
  the whole track. Streaming needs incremental creation ahead and destruction behind.
- **Generation must be resumable.** `PhysicsConstrainedCourseGenerator` produces a finite chain
  from a seed in one call. Streaming needs to continue from the previous chunk's end pose and
  heading.
- **"Course completed" stops being an episode outcome.** There is no end of road, so an episode
  ends on collision, departure, or a time limit — not on reaching the last waypoint.

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
| Nominal course width | 1.0 m | chosen within the regulation |
| Wall height | 0.06–0.10 m — **to be confirmed** | estimated from the duct and the images |
| Surface friction | 0.9 carpet, lower for the vinyl variant | see the vehicle model |
| Corner design speed | 3.5 m/s | below the 4–5 m/s top speed |
| Minimum corner radius | 1.4 m | derived: `v² / (μg) = 3.5² / (0.9 × 9.81)` |
| Maximum corner radius | 4.0 m | fits the envelope |
| Straight length | 2–8 m | fits the envelope |
| Centreline sample spacing | 0.1 m | 1 m cannot resolve a 1.4 m radius |
| Streaming distance ahead / behind | to be decided | must exceed camera and sensor range |

A 1.4 m radius hairpin needs `2R + width = 3.8 m` across, fitting the 10 m dimension. Cornering at
3.5 m/s on that radius draws `v²/R = 8.75 m/s²`, essentially the friction limit, so the two
numbers are consistent by construction.

## Gaps against the current implementation

Beyond the streaming requirements above:

1. **Straight-segment side walls are generated.** `ProceduralCourseTrackBuilder` now builds
   collider-backed 0.08 m side walls for every road segment, which are visible to the auxiliary
   LiDAR and terminate an episode on impact. Corner joins remain deliberately simple; a
   continuous duct-wall mesh and its regulation-style gaps are still future fidelity work.
2. **The regulation's colour line is not modelled.** It is the one visual cue the rules
   guarantee, and the camera-primary policy is exactly what would use it.
3. **No checkpoint or lap counter**, so the metric the competition scores cannot be measured on
   the holdout courses.
4. **The 2D LiDAR sits above a duct wall.** The current mount is about 0.196 m above the floor
   against a wall roughly 0.06–0.10 m tall, so it would scan straight over the course. This
   matters less than it first appeared — the camera is the primary sensor and the LiDAR is
   auxiliary — but the mount height should still be corrected when walls are built.

## Sources

- MAGP regulations: <https://minicar-autogp.org/race/regulation/>
- MAGP 2024 regulations: <https://f1tenth-japan.org/2024/regulation/>
- Third event entry page (venue): <https://minicar-autogp.org/race/entry/03rd/>
