# Goal readiness audit

This is a completion audit against [the goals document](physical-ai-goals.md). A component or a
unit test is not evidence that a target behaviour has been achieved; the evidence column names
the artifact that would be needed before claiming completion.

| Goal | Current state | Evidence still required |
| --- | --- | --- |
| Physics feasibility | implemented and dynamics-tested | real steady-state braking/cornering telemetry and calibrated replay comparison |
| Unknown-course generalisation | generated training roads and fixed holdout suite exist | a trained policy's training-vs-holdout completion/lap-time report |
| Camera blind-corner safety | rendered camera, occluding hazards and camera export exist | camera-policy versus no-visibility baseline comparison with confidence bounds |
| Head-to-head strategy | kinematic opponent, tactics and collision metric exist | measured overtakes, blocks and collision-free interaction episodes |
| Sim RL phase | one-environment vector REINFORCE transport/trainer exists | repeatable trained checkpoint plus learning curve; a functioning camera-capable trainer remains needed |
| Offline visual BC | teleoperation recorder exports sparse camera JPEG frames | a collected dataset and validation of a trained visual model |
| Shared ROS2 control | custom vehicle subscribes after idempotent node initialisation | live ROS2 publish/subscribe integration test against an external node |
| Sim-to-real calibration | CSV importer, friction estimator and report sink exist | real vehicle run, accepted RMSE threshold and before/after simulation comparison |

## Completion gate

Do not mark the simulator goal complete until every evidence item above exists and the resulting
reports satisfy the thresholds agreed for the project. In particular, synthetic tests do not
substitute for real telemetry, trained-policy evaluation, or comparative safety results.
