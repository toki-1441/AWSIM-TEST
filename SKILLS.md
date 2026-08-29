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

## Confidential references

Reference repositories are implementation aids, not project inputs. Recreate only the needed general behavior, never copy non-public source or configuration, and remove the local reference checkout once the implementation is in place.
