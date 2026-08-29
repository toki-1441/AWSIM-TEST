# Project Workflows

## Linux Unity build

Use the local build helper from the repository root:

```bash
scripts/build_linux.sh --clean
```

It reads the required editor version from `ProjectSettings/ProjectVersion.txt` and writes the player to `Build/AWSIM.x86_64`. Set `UNITY_BIN` only when Unity is installed outside the standard Unity Hub location. Use `--output <path>` to select another player executable path.

The helper is intentionally local-only: it does not deploy or publish artifacts.

## Confidential references

Reference repositories are implementation aids, not project inputs. Recreate only the needed general behavior, never copy non-public source or configuration, and remove the local reference checkout once the implementation is in place.
