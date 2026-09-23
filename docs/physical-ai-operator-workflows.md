# Operator workflows — implementation and acceptance evidence

Agreed 2026-09-21. This supersedes requirements that made detailed tyre-model acceptance a
prerequisite for all simulator data collection. The long-term goal remains camera-primary
planning on unseen courses; the deliverable here is a usable collection/training environment.

## Required end state

| Requirement | Acceptance evidence | Current state |
| --- | --- | --- |
| Unified launch | GUI, saved configuration and CLI resolve to the same actual mode, scenario, map, vehicle and sensor settings; invalid settings surface useful errors | verified: config round-trip/override tests, GUI save/load/run, built-player invalid-value and RL-to-manual checks |
| Fixed maps | Save complete course geometry and reload it unchanged, with identity/hash; usable for manual collection and repeatable evaluation | verified: offset-map load/save/reset, unchanged samples/hash and deterministic fixed-map episode tests |
| Endless maps | Curves/straights continue to append and retire with rebasing during a sustained drive | verified by 92-second reference-policy multimodal run: 15 appends, 13 trims, 3 rebases; not human-drive evidence |
| Manual collection | Start/stop/restart in the built player; show destination, counts and errors; assisted and dynamic modes identifiable, no overwrite of existing runs | verified by synthetic UI input in both models, separate recording segments, visible counts/paths and output-error check |
| RGB + metric depth | Preview and synchronized recording with frame IDs, simulation timestamps, camera calibration, depth units/convention and invalid-pixel mask | verified: calibrated GPU plane/sky tests, inspected previews and validated moving recordings |
| BEV + automatic annotations | Ego-centric metric BEV; road/boundary/obstacle/other-vehicle labels, documented classes and transforms; keep ground truth separate from policy inputs | verified: geometry/class/rebase tests, inspected previews, synchronized binary/JSON recordings and distinct privileged-source metadata |
| Operator annotation | Record manual event tags linked to episode, frame/time and pose | verified: UI tag capture without unintended driving/reset; validator rejects corrupted event linkage |
| Data review | Replay images, annotations and actions; validate missing frames, alignment, metadata and resumable session boundaries | verified: browser tag navigation/playback, multimodal reviews and corruption tests; whole-segment memory limitation documented |
| RL workflow | Start, stop and resume with the same configuration/observation schema; verify action/reward/reset/termination, checkpoint and metrics; no silent reference-policy fallback | verified: managed UI lifecycle, optimizer/RNG tests, current-build resume, changed-mount rejection, absent-server and disconnect failures |
| End-to-end build | Actually operate the rebuilt player through collection/review and a bounded RL run, preserving outputs and evidence | verified: Linux build plus manual-input collection/review and latest-build RL episodes 5/6; evidence below |

All rows are required; a UI alone, reference rollout, compilation, or isolated test is not completion.
GPU-scale parallelism, high-fidelity tyre development and proving a high-performing learned policy
are not requirements for this goal. Stable dynamics and collision, reproducibility and honest
model/sensor metadata are requirements.

## Sequence

1. Shared settings, fixed/endless map selection and reliable manual recording.
2. RGB/depth/BEV/annotations captured in one synchronized data contract and replay/validation.
3. RL process lifecycle and save/resume using that same environment contract.
4. Rebuild and exercise the entire operator workflow; update this matrix with evidence.

The goal branch starts from `dev`, retaining the existing vehicle/coast-down commits as
dependencies without declaring those older manual-acceptance goals complete. No push or dev
integration occurs until the requested goal is verified.

## Current commands and data contract

Bare launch opens the operator settings screen. Select manual driving, map mode, seed,
assisted/dynamic vehicle, RGB/depth/BEV, image dimensions/rate and output directory. Save a configuration for:

```bash
Build/AWSIM.x86_64 -physicalAiConfig Build/config/operator.json
# Explicit CLI values override values supplied by the configuration:
Build/AWSIM.x86_64 -physicalAiConfig Build/config/operator.json -physicalAiSeed 42
```

Numeric fields preserve unfinished editing text such as `0.` or `-` rather than replacing it
on each redraw. Save/start rejects empty, malformed, or non-finite visible numeric fields;
it never silently submits the previous value. Loading a configuration replaces the editing text.
Episode duration accepts fractional seconds.

The shared argument reader uses the final occurrence of an option. An explicit
`-physicalAiRecordOnStart false` disables a prior configuration switch; a later bare switch
enables it again. Numeric getters only use defaults when the option is absent, never when an
explicit value is malformed, non-finite or missing. The player bootstrap validates resolved
operator options against the configuration's range/combination rules before loading the scene,
including strict lowercase `true`/`false` values and existence/format of a supplied map.
High-level mode/scenario overrides are resolved before deriving RL/evaluation/hazard flags;
switching a configured fixed map to endless also removes the inherited map path. An explicitly
supplied path with endless mode remains invalid. A standalone `-physicalAiMode` uses GUI defaults;
legacy scripts without that option/config retain their existing preset behavior.
Built-player evidence for invalid-value rejection and RL-to-manual conversion appears below;
the complete launch acceptance matrix still requires an audit across supported combinations.

The driving HUD and F8 start/stop recording immediately. R restarts; pause/resume does not
advance simulation time. Text-field focus prevents WASD from steering while typing a tag/path.
Save a map to a NEW path from the HUD, then select fixed mode and that file on the next launch.
Blank fixed-map path generates a repeatable closed course. Endless snapshots reload as finite
roads: they are not a saved generator continuation. Existing map files are never overwritten.
Loaded snapshots retain their stored coordinates unchanged. The vehicle starts at the first
retained centerline sample, facing the first nonzero segment, at its configured chassis height.
This also applies after a reset: an offset snapshot captured after history trimming/rebasing
must not spawn the car at the unrelated scene origin. Loading is a fresh episode, not restoration
of the vehicle's capture-time speed, pose or streaming-generator state.

Each recording segment has a unique `teleop-<time>-<id>/` directory containing:

- `manifest.json`: vehicle model/specification, map identity, seed, resolved arguments,
  camera mount/FOV/resolution/clip planes and coordinate/control conventions.
- `frames.jsonl`: commands, actual throttle/brake/steer, body pose/velocity, reward, terminal
  and segment-end markers, sparse JPEG payloads with sensor frame IDs, simulation timestamps,
  camera world transforms at capture and relative paths to depth/BEV payloads.
- `events.jsonl`: operator tags and full map snapshots at recording start and world-management
  operations, including the accumulated floating-origin offset.
- `sensors/` (when selected): metric depth, validity masks, semantic BEV labels, PNG previews
  and JSON object annotations sharing the camera frame ID and simulation timestamp.

### Multimodal sensor contract (recording schema 3)

Camera mounting position and orientation are **fixed**, never randomized by episode or seed.
Configure them in the launch screen or configuration file (defaults below):

```json
{
  "cameraHeightMeters": 0.15,
  "cameraForwardMeters": 0.077,
  "cameraLateralMeters": 0.0,
  "cameraPitchDegrees": 22.0,
  "cameraYawDegrees": 0.0,
  "cameraRollDegrees": 0.0
}
```

Height is above the planar course, forward/right offsets are relative to the vehicle origin,
and positive pitch looks down. Corresponding CLI options use `-physicalAiCameraHeightMeters`,
`-physicalAiCameraForwardMeters`, `-physicalAiCameraLateralMeters`, `-physicalAiCameraPitchDegrees`,
`-physicalAiCameraYawDegrees`, and `-physicalAiCameraRollDegrees`.
Resolved local extrinsics are saved in recording manifests and checked for RL resume compatibility.
Legacy serialized mount-jitter settings are ignored; friction randomization is separate and unchanged.

Enable `depth` and/or `bev` in the configuration or use `-physicalAiDepth true -physicalAiBev true`.
These sensors can be recorded without saving RGB (`-physicalAiRecordRgb false`); they still
use the sensor-camera capture cadence. Real rendering requires a graphics device.

- Depth comes from the same HDRP camera's GPU depth buffer, not collision raycasts. Files are
  little-endian float32 optical-axis **z in metres**, top-left row-major. They are not Euclidean
  ray distance and are not normalized preview brightness. Sky, far clip and values beyond
  `depthMaxMeters` (default 30) are zero, with an accompanying uint8 validity mask (0/1).
  Transparent surfaces that do not write depth are not represented as opaque measurements.
  The grayscale PNG is only a logarithmic-distance preview (near bright, far dark); purple
  means invalid. Camera calibration is in the
  manifest, and each sampled frame stores the actual rendered camera transform.
- BEV is simulator ground truth, **not camera-visible segmentation**. It rasterizes the actual
  rendered road triangles and collision-box footprints into an ego-centred grid. Classes in
  overwrite priority are 0 background, 1 road, 2 boundary, 3 obstacle, 4 other vehicle, 5 ego.
  Default `bevSize=128`, `bevMetersPerPixel=0.1` covers 12.8 m square. Labels are uint8,
  top-left row-major; forward is image up, right is image right. Cell-centre transforms and
  class names are included in the manifest. Box annotations carry ego-frame pose/size and
  session-local object IDs; they do not claim camera occlusion or dataset-global identity.
  The footprint model assumes the current planar course/vehicle setup.
- BEV and annotations are saved as separate privileged targets, not silently appended to
  the existing planning-policy observation. Floating-origin rebasing is represented explicitly;
  global camera/body position is local-world position plus the recorded origin offset.

Manual `Command` is a legacy speed/lateral proxy, not an independently measured planning
target. Actual controls are in `DirectActuation`; downstream planning-label derivation is still
required before claiming this dataset trains the intended planning layer correctly. Metadata
must survive export; never silently mix assisted/dynamic or direct/planning action spaces.

```bash
python3 scripts/review_recording.py Build/datasets/teleop-<time>-<id> --output Build/reviews/run-001
```

Open `review.html` locally for synchronized RGB/depth/BEV, action and tag inspection, with
simulation-time playback and a depth-pixel metre readout. The operator-tag dropdown jumps to
the nearest displayed frame and shows its time difference from the original tag. Tags retain
their own exact time/pose; a navigation link does not claim image-time synchronization.
`report.json` checks timestamp/index,
model, required payloads, depth dimensions/range/masks, BEV dimensions/classes, annotation
alignment and PNG integrity. It rejects sensor paths outside the recording directory.
The reviewer does not establish physical fidelity or planning-target quality. Requested
sensors without actual captured payloads fail validation. Review currently loads a whole segment
into memory; use short recording segments for large images/long runs.

The RL UI now manages a Python trainer with start, stop/save and resume controls. Select a Python
environment containing the trainer dependencies and a trainer script; each start/resume creates a
new output run containing `trainer.log`, `launch.json`, `environment.json`, `checkpoint.pt` and
`metrics.jsonl`. Resume restores optimizer state, statistics and RNG, and rejects a different
environment contract (including camera mounting). Existing checkpoints from before contract
changes are not interchangeable. Connection/trainer errors pause training rather than invoking
a reference driver. Current-contract resume, changed-mount rejection and absent-server failure
have built-player evidence below; remaining lifecycle checks must still pass before full acceptance.

## Evidence — launch/recording foundation

2026-09-21/22: EditMode 139/139 (`/tmp/operator-editmode-001.xml`), graphics-enabled PlayMode
22/22 (`/tmp/operator-playmode-001.xml`), Python review/streaming-validator tests 16/16.
Linux rebuild passed (`/tmp/operator-linux-build-001.log`). A bounded graphical player launch
from `Build/validation/operator-workflows-001/config.json` loaded fixed seed 7 in assisted
manual mode and wrote 1,200 records including 120 rendered RGB frames. The standalone reviewer
accepted the recording; report/HTML are under `Build/validation/operator-workflows-001/review/`.
This was a no-input startup/recording check, not a human drive or an end-to-end goal pass.

## Evidence — multimodal capture foundation

2026-09-22: EditMode 141/141 (`/tmp/operator-multimodal-editmode-003.xml`), after fixing the
pre-Awake editor camera-target initialization path; graphics-enabled PlayMode 23/23
(`/tmp/operator-multimodal-playmode-001.xml`).
The GPU depth test checks a plane at 2 m at the image centre and corner (optical-axis depth,
not ray range), a high wall only in the top image rows, explicit invalid sky, and target-camera
calibration before the first capture. BEV tests cover actual road/boundary geometry, unchanged
ego labels after a shared world rebase, and separate obstacle/opponent classes with ego-frame poses.
Python review/streaming-validator tests: 19/19, including corrupted preview rejection.

Linux rebuilds succeeded (`/tmp/operator-multimodal-build-001.log`,
`/tmp/operator-multimodal-build-002.log`). Bounded graphical player checks in assisted manual,
fixed seed 7 recorded RGB/depth/BEV simultaneously:

- `Build/validation/operator-multimodal-001/`: 3,243 records and 325 samples of each modality.
- `Build/validation/operator-multimodal-002/`: 1,731 records and 173 samples of each modality,
  after improving the log-depth preview contrast. The launch used the first run's configuration
  with `-physicalAiDemonstrationDirectory Build/validation/operator-multimodal-002/datasets`.

Both standalone validation reports passed with no failures. Each directory contains `review/`
and an inspected `player-preview.png`; the metric/semantic PNGs were also visually inspected.
These were no-input startup checks, **not human driving, moving-sensor accuracy or the full
operator/RL acceptance**. Managed RL lifecycle and moving collection remain required.

## Evidence — fixed camera mounting

2026-09-22: focused configuration/randomization EditMode tests 16/16
(`/tmp/operator-fixed-camera-001.xml`), Linux rebuild success
(`/tmp/operator-fixed-camera-build-001.log`). Two bounded RL player runs, each with seeds 7 and 8,
verified identical local mounting transforms across episodes. The default run measured camera
world height 0.15 m; the override run used height 0.20 m and pitch 18 degrees and verified both
in recorded metadata (height remained 0.20 m throughout its 405 records).
Override evidence: `Build/validation/operator-fixed-camera-002/datasets/`, with player log
`/tmp/operator-fixed-camera-player-002.log`. These checks establish fixed/configurable mounting,
not complete operator-workflow acceptance.

## Evidence — configuration input regression

2026-09-22: complete EditMode suite 154/154 (`/tmp/operator-workflows-regression-001.xml`),
including numeric editing, configuration round trips, legacy mount-noise suppression and fixed
camera pose across seeds. All Python test modules passed 37/37 via
`PYTHONDONTWRITEBYTECODE=1 ../.physical-ai-venv/bin/python -m unittest discover -s scripts -p 'test_*.py'`.
These include checkpoint optimizer/RNG restoration, required visual input/handshake, and
checkpoint/metrics persistence before stop acknowledgement. Built-player operator acceptance
is tracked separately; these tests alone do not complete it.

Rebuild `/tmp/operator-numeric-input-build-001.log` succeeded. Synthetic input confined to the
validation player's X11 window proved `0.` survives redraw, blank height rejects save, and
typing `0.25` saves/reloads `cameraHeightMeters: 0.25` in `Build/config/operator.json` (created
new for this test). Inspected screenshots: `Build/validation/operator-numeric-ui-001/`.
After launching from that UI, F8/W did not produce a recording or movement. The broad
`GUIUtility.keyboardControl != 0` guard was replaced with named HUD text-field focus;
this follow-up still needs rebuilt-player verification. Do not count the failed key attempt as
manual-recording acceptance or the settings UI test as proof of runtime camera height.

Follow-up 2026-09-22: focused EditMode 21/21 (`/tmp/operator-text-focus-001.xml`) and rebuild
`/tmp/operator-text-focus-build-001.log` passed. A bare player launch loaded the GUI-saved
configuration and started the scene. Synthetic F8/W input now opened a recording and moved the
vehicle 2.48 m. `Build/datasets/teleop-20260922T115514269-f1799692/` contains 2,250 records,
322 RGB frames, constant camera world height 0.25 m, and operator tag `wr`. Throttle was active
only at steps 38–77; typing `wr` near step 2,233 neither accelerated nor reset the episode,
and F8 while typing did not toggle recording. Adding the tag released text focus; F8 then
stopped recording. F8 restart/stop created a separate 73-record segment
`teleop-20260922T115601880-50a50422` without changing the first. Both offline review reports
are valid, under `Build/validation/operator-text-focus-001/`, alongside inspected UI captures.
The HUD saved a new map `Build/maps/course-20260922T115508.json`; R then reset the episode.
This proves a bounded RGB recording/tag/restart workflow, not a human drive, sustained streaming,
multimodal manual lifecycle, or saved-map reload acceptance. The older numeric-UI failed-input
attempt above is retained as the regression history, not counted as a successful run.

## Evidence — current-contract RL resume and failures

Using player build `/tmp/operator-text-focus-build-001.log`, resumed
`Build/validation/operator-fixed-camera-002/training/rl-20260922T114437584-fec3d865/checkpoint.pt`
with the same camera height 0.20 m and pitch 18 degrees. The run exited 0, resumed at episodes
3/4 (seeds 9/10), and saved `completed_episodes: 4` plus optimizer/RNG state in a new checkpoint
under `Build/validation/operator-rl-current-resume-001/training/`.
The source checkpoint SHA-256 remained
`4988acf6e939f1a2075d362d548dd19a70cc10d5163054d0c8f56f2c5a6ec2db`.
Its two recordings contain 202/201 records and 21/20 samples of each RGB/depth/BEV modality;
both reviews passed (`review-seed9/`, `review-seed10/`). Player log:
`/tmp/operator-rl-current-resume-001.log`. This is a bounded learning/lifecycle check,
not a claim of learned-policy performance.

Changing only camera height to 0.21 m rejected resume with
`Environment/settings differ from the checkpoint; resume rejected`, exit code 2, no dataset,
no metrics and no new checkpoint. Evidence: `Build/validation/operator-rl-mismatch-001/`
and `/tmp/operator-rl-mismatch-001.log`. External-server mode on an unused port likewise
exited 2 with `RL stopped: Connection refused`, without creating a dataset
(`/tmp/operator-rl-no-server-001.log`). Both runs left no player/trainer process alive.

2026-09-23 UTC: simulated a mid-run server failure by sending SIGTERM only to the verified
Python child of this validation player. The player exited 2 on `Connection reset by peer`,
without a reference-policy continuation. Evidence: `/tmp/operator-rl-disconnect-001.log`
and `Build/validation/operator-rl-disconnect-001/`. The already completed seed-7 episode
has one metrics entry (148 policy steps) and a checkpoint with `completed_episodes: 1`.
The interrupted seed-8 rollout did not add a completed checkpoint/metrics entry; its partial
recording closes with `Terminal: false`, `EndReason: trainer-stop`. Both complete/partial
recording reviews pass structural validation, which does not make the partial rollout a
completed learning episode. The validation player and its trainer both terminated.

## Evidence — offset fixed-map loading and reset

2026-09-23: fixed the loaded-map spawn contract: keep snapshot coordinates unchanged and
place the vehicle at its first retained road sample, facing along the road. Focused EditMode
22/22 (`/tmp/operator-map-spawn-001.xml`) covers an offset map, a nonzero authored scene pose,
repeated resets and unchanged geometry/hash. Rebuild succeeded
(`/tmp/operator-map-spawn-build-001.log`).

The built player loaded `Build/validation/operator-map-spawn-001/offset-map.json`, whose first
point is (120, 0, -35), rather than spawning at world zero. Synthetic W input moved the car
to x=120.4644; R reset it to x=120, z=-35 with the original chassis height. The HUD-saved
`Build/maps/course-20260922T231107.json` and both recording-start snapshots have exactly the
same sample positions/curvatures/speeds as the input. Two segments contain 591/1,511 records
and 85/216 samples of each RGB/depth/BEV modality; both standalone reviews pass. Evidence and
an inspected camera/depth/BEV screenshot are under `Build/validation/operator-map-spawn-001/`;
player log is `/tmp/operator-map-spawn-player-001.log`.
This is a short synthetic-input fixed-map check, not sustained endless streaming or human driving.

## Evidence — CLI validation and mode overrides

2026-09-23: shared-reader regression EditMode 162/162
(`/tmp/operator-cli-regression-001.xml`); after adding prelaunch checks and derived-mode
normalization, focused operator tests 34/34 (`/tmp/operator-mode-overrides-001.xml`).
Rebuilt player: `/tmp/operator-launch-validation-build-001.log`.
Three actual headless launches rejected width 15, depth `yes`, and a missing camera-Hz value
with exit code 2 before any episode began. Logs:
`/tmp/operator-launch-invalid-{width,boolean,missing}-001.log`.

A graphical launch loaded `Build/validation/operator-rl-001/config.json` with CLI overrides
`-physicalAiMode manual -physicalAiRecordOnStart false`. It started a manual fixed seed-7
session, launched no trainer, and created no dataset until synthetic F8 input. W then produced
actual throttle, and F8 stopped the segment. Its manifest contains no inherited RL port,
managed-trainer or evaluation flags. The 1,324-record recording includes 132 samples each of
RGB/depth/BEV and passes offline review. Evidence: `Build/validation/operator-launch-manual-001/`
and `/tmp/operator-launch-manual-001.log`. This is synthetic UI validation, not human driving.

## Evidence — sustained endless multimodal regression

2026-09-23: a 100-second wall-clock bounded run produced 92.4676 simulation seconds,
9,248 records and 463 samples of each RGB/depth/BEV modality. It used assisted dynamics and
the **reference planner**, not human control or a learned policy. Both left and right appended
corners occurred: 15 append/retire events, 13 history trims and 3 floating-origin rebases.
Every instrumented transition reports zero pose/control/velocity/model-state discontinuity;
no collision/off-course termination was logged. The external duration limit ended the run
(exit 124); the recording closes with `Terminal: false`, `EndReason: disabled`, not success.

Evidence: `Build/validation/operator-endless-reference-001/`, player log
`/tmp/operator-endless-reference-001.log`, inspected `streaming.png`, and valid offline
`review/report.json`. Reconstructing positions as local position plus recorded floating-origin
offset yielded maximum inter-sample speed 2.737 m/s for the body and 2.101 m/s for captured
camera positions; no rebase-sized jump appears in either series. These are diagnostic maxima,
not calibrated speed estimates (the final body record has a shortened time interval).
The recording preserves complete map snapshots at each world-management event.
This run provides automated streaming/data regression evidence, not the separate human-drive
physical-model acceptance in `SKILLS.md` and not completion of the whole operator goal.

## Evidence — annotation linkage validation

2026-09-23: the offline reviewer now rejects events with missing/nonfinite time, position or
floating-origin offset, invalid/out-of-segment step indices, mismatched episode IDs, blank tag
labels, or tag timestamps outside the recording segment. Python suite: 38/38 passed, including
deliberately corrupted annotation fields. The existing `wr` tag recording
`Build/datasets/teleop-20260922T115514269-f1799692/` and the 9,248-record endless regression
both pass the stricter validator. This validates event linkage, not the semantic correctness
of an operator's label.

The reviewer no longer drops tags outside a fixed 0.1-second display window: all valid tags
are indexed to their nearest displayed sample, with earlier-frame tie-breaking and explicit
time offset. A unit case covers sparse one-second samples, midpoint tags and segment edges.
Python suite: 39/39 passed. In an isolated headless Chrome profile, actual tag selection on
`Build/validation/operator-tag-navigation-001/review/review.html` selected sample 319 for `wr`
and displayed a +0.016-second offset; play/pause advanced six samples in 450 ms. The browser
was terminated after verification. The first browser probe failed while its page execution
context was being replaced during loading; waiting for document completion resolved the probe,
without changing the reviewer to bypass validation.

## Evidence — dynamic-mode manual-input collection

2026-09-23: selected `-physicalAiMode manual -physicalAiVehicleModel dynamic`, fixed seed 7,
RGB/depth/BEV at 160x90 and 5 Hz. Synthetic F8/W input started a recording and moved the car
1.67 m. The 1,755-record segment has 87 samples of every modality; a second F8-started segment
has 78 records and four samples of every modality. Both manifests and all frames identify
`planar-single-track`, not the assisted model, and both reviews pass the current event/payload
validator. Evidence: `Build/validation/operator-manual-dynamic-001/`, with player log
`/tmp/operator-manual-dynamic-001.log`.

Pause/resume was checked using the visible `Resume` button and an unchanged recording size
during a one-second pause, then further records after resume. An initial zero-duration injected
mouse click was missed; the validation helper now holds mouse presses for its bounded `--hold`
duration before releasing, just as for keys. The successful retry and failed attempt are not
conflated. Screenshots `current.png` and `paused.png` were inspected. This is a short synthetic
operator-workflow check, not calibration or a claim of real-car physical fidelity.

## Latest regression and error-path audit

2026-09-23: current C# tree passed EditMode 172/172
(`/tmp/operator-final-editmode-001.xml`) and graphics-enabled PlayMode 23/23
(`/tmp/operator-final-playmode-001.xml`). Python suite most recently passed 39/39, including
annotation validation and sparse-tag navigation. Built-player output-directory failure was
tested by specifying the existing validation configuration file as a directory. Recording
stopped with zero records and a visible HUD error rather than overwriting the file; the
configuration remained readable. Evidence: `/tmp/operator-record-error-001.log` and inspected
`Build/validation/operator-record-error-001/error.png`.

Before integration, remaining audit work is to exercise the managed RL path once more through
the latest launch-normalization build, reconcile every row above with its concrete evidence,
and verify the goal-branch/dev integration diff. No human-drive physical-calibration claim or
learned-policy performance claim is part of that acceptance. The accumulated evidence includes
synthetic UI operation and automated reference rollouts, which must stay labelled accordingly.

## Acceptance audit — 2026-09-23

The preceding pre-integration checklist is now satisfied for runtime functionality. The latest
launch-validation player resumed the four-episode checkpoint and completed episodes 5/6,
seeds 11/12, exiting 0 with `completed_episodes: 6`. Evidence:
`Build/validation/operator-rl-final-001/training/`, `/tmp/operator-rl-final-001.log`.
Its two segments contain 201/202 records and 20/21 samples of each RGB/depth/BEV modality;
both pass the current reviewer. No player or owned trainer remains running.

Audit checked the actual 172 EditMode and 23 PlayMode results, including camera geometry/depth,
BEV classes/transforms, fixed-map and camera determinism, recording lifecycle and launch checks;
the 39 Python cases; actual manifests, event streams, metrics and checkpoints; inspected UI
captures and browser interaction; and recording-error/fail-closed logs. The table above records
the scope of each result rather than claiming exhaustive input combinations or policy quality.

This deliverable is usable operator collection/review and a bounded learning lifecycle. It is
not a human road-test sign-off, real-car calibration, a successful learned racing policy, or
completion of the separate long-term unseen-course planning objective. Manual-input command
proxies are explicitly labelled and must not be silently reused as valid planning BC targets.
Local evidence under `Build/validation/` and logs under `/tmp/` are intentionally not versioned;
preserve them separately if moving the workspace. No remote publication is authorized.
