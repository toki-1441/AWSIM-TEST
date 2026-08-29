#!/usr/bin/env bash
# Build this Unity project as a Linux 64-bit player.
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: scripts/build_linux.sh [--clean] [--output <path>]

Builds a Linux 64-bit Unity player for this project.

Options:
  --clean          Remove the output directory before building.
  --output <path>  Player executable path, relative to the project root unless absolute.
  -h, --help       Show this help text.

Environment overrides:
  UNITY_BIN         Full path to the Unity executable.
  UNITY_VERSION     Unity editor version (default: read from ProjectVersion.txt).
EOF
}

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unity_version="${UNITY_VERSION:-$(sed -n 's/^m_EditorVersion: //p' "${project_root}/ProjectSettings/ProjectVersion.txt" | head -n 1)}"
unity_bin="${UNITY_BIN:-${HOME}/Unity/Hub/Editor/${unity_version}/Editor/Unity}"
output_path="Build/AWSIM.x86_64"
clean_output=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --clean) clean_output=true ;;
        --output)
            [[ $# -ge 2 ]] || { echo "ERROR: --output requires a path." >&2; exit 2; }
            output_path="$2"
            shift
            ;;
        -h|--help) usage; exit 0 ;;
        *) echo "ERROR: unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
    shift
done

[[ -n "$unity_version" ]] || { echo "ERROR: Unity version is not configured." >&2; exit 1; }
[[ -x "$unity_bin" ]] || { echo "ERROR: Unity executable not found: $unity_bin" >&2; exit 1; }

if [[ "$output_path" != /* ]]; then
    output_path="${project_root}/${output_path}"
fi
output_dir="$(dirname "$output_path")"

if [[ "$clean_output" == true && -d "$output_dir" ]]; then
    case "$output_dir" in
        "${project_root}"/*) rm -rf "$output_dir" ;;
        *) echo "ERROR: refusing to remove output directory outside the project: $output_dir" >&2; exit 1 ;;
    esac
fi
mkdir -p "$output_dir"

# A running editor holds the project lock and can make a batch build fail or
# produce inconsistent artifacts. Unity writes this file while the editor is open.
instance_json="${project_root}/Library/EditorInstance.json"
if [[ -f "$instance_json" ]]; then
    editor_pid="$(sed -n 's/.*"process_id"[[:space:]]*:[[:space:]]*\([0-9][0-9]*\).*/\1/p' "$instance_json" | head -n 1)"
    if [[ -n "$editor_pid" ]] && kill -0 "$editor_pid" 2>/dev/null; then
        echo "ERROR: Unity Editor is running for this project (PID ${editor_pid}). Close it before building." >&2
        exit 1
    fi
fi

license_client="$(dirname "$unity_bin")/Data/Resources/Licensing/Client/Unity.Licensing.Client"
license_pipe="LicenseClient-$(id -un)"
license_socket="/tmp/${license_pipe}.sock"
license_pid=""

cleanup() {
    if [[ -n "$license_pid" ]] && kill -0 "$license_pid" 2>/dev/null; then
        kill "$license_pid" 2>/dev/null || true
    fi
}
trap cleanup EXIT INT TERM

if [[ -x "$license_client" && ! -S "$license_socket" ]]; then
    echo "Starting Unity Licensing Client..."
    "$license_client" --namedPipe "$license_pipe" &
    license_pid=$!
    for _ in $(seq 1 10); do
        [[ -S "$license_socket" ]] && break
        sleep 1
    done
    [[ -S "$license_socket" ]] || { echo "ERROR: Unity Licensing Client did not become ready." >&2; exit 1; }
fi

echo "Building Linux player with Unity ${unity_version}: ${output_path}"
"$unity_bin" \
    -batchmode -nographics -quit \
    -projectPath "$project_root" \
    -buildLinux64Player "$output_path" \
    -logFile /dev/stdout

[[ -f "$output_path" ]] || { echo "ERROR: Unity exited successfully but produced no player: $output_path" >&2; exit 1; }
echo "Build succeeded: $output_path"
