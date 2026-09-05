# Project Workflows

## Branch workflow

`main` is the stable base and `dev` is the integration branch. Develop each independently verifiable goal in a branch created from `dev`:

```bash
git switch dev
git switch -c goal/<short-goal>
```

Commit the goal branch as work is completed. After verification, integrate a single squashed commit into `dev`, then confirm that the branch content is identical before deleting the local goal branch:

```bash
git switch dev
git merge --squash goal/<short-goal>
git commit -m "<goal summary>"
git diff --exit-code dev goal/<short-goal>
git branch -D goal/<short-goal>
```

Do not commit to forks or reference repositories. Do not push any branch unless explicitly requested by the user.

## Linux Unity build

Use the local build helper from the repository root:

```bash
scripts/build_linux.sh --clean
```

It reads the required editor version from `ProjectSettings/ProjectVersion.txt` and writes the player to `Build/AWSIM.x86_64`. Set `UNITY_BIN` only when Unity is installed outside the standard Unity Hub location. Use `--output <path>` to select another player executable path.

The helper is intentionally local-only: it does not deploy or publish artifacts.

## Manual streaming acceptance

Use the rebuilt Linux player and the evidence launcher from the repository root:

```bash
scripts/run_manual_streaming_validation.sh Build/validation/manual-streaming-001
```

Drive with WASD or the arrow keys through at least three append operations, a history trim and a
floating-origin rebase. Record at least 30 seconds of the same run. The launcher preserves
`Player.log`, creates `instrumented-streaming-report.json`, and writes an operator-assessment
template. After reviewing the video, complete that template and run the finalization command in
`streaming-continuity-summary.txt`.

Only a report whose transitions identify `manual-teleoperation` may satisfy this gate. Automated
reference-driver output is useful regression evidence but must not be presented as a human drive.
The final report also requires unchanged pose/velocity, control input, grounded-wheel mask and
suspension state at every world-management event, both turn directions, a nonempty forward horizon,
the video, and all visual/handling checks.

## Confidential references

Reference repositories are implementation aids, not project inputs. Recreate only the needed general behavior, never copy non-public source or configuration, and remove the local reference checkout once the implementation is in place.
