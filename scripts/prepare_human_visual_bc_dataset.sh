#!/usr/bin/env bash
# Export and validate camera-labelled human teleoperation recordings only.
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "usage: $0 TELEOP_RECORDING_DIRECTORY OUTPUT_DIRECTORY" >&2
  exit 2
fi

recording_directory=$1
output_directory=$2
python=${PHYSICAL_AI_PYTHON:-python3}

test -d "$recording_directory"
inputs=()
while IFS= read -r -d '' recording; do
  inputs+=("$recording")
done < <(find "$recording_directory" -maxdepth 1 -type f -name 'teleop-*.jsonl' -print0 | sort -z)

if [ "${#inputs[@]}" -eq 0 ]; then
  echo "No human teleoperation files matching teleop-*.jsonl in $recording_directory." >&2
  echo "Reference-* recordings are intentionally excluded from the human BC dataset." >&2
  exit 1
fi

"$python" scripts/export_camera_demonstrations.py "${inputs[@]}" --output "$output_directory"

"$python" - "$output_directory/manifest.jsonl" <<'PY'
import json
import sys
from pathlib import Path

manifest = Path(sys.argv[1])
rows = [json.loads(line) for line in manifest.read_text(encoding="utf-8").splitlines()]
if not rows:
    raise SystemExit("No non-terminal human command frames were exported.")
missing = [index + 1 for index, row in enumerate(rows) if not row.get("image")]
if missing:
    raise SystemExit(f"{len(missing)} exported command frames have no synchronized camera image (first row {missing[0]}).")
missing_files = [row["image"] for row in rows if not (manifest.parent / row["image"]).is_file()]
if missing_files:
    raise SystemExit(f"Exported camera image is missing: {missing_files[0]}")
width = len(rows[0]["observation"])
if width == 0 or any(len(row["observation"]) != width for row in rows):
    raise SystemExit("Human recordings do not share one non-empty observation layout.")
print(f"validated {len(rows)} human camera-command pairs, observation width {width} -> {manifest}")
PY
