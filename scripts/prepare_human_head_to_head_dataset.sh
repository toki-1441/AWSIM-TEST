#!/usr/bin/env bash
# Export only labelled human overtake/defence recordings for a separately auditable BC dataset.
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "usage: $0 TELEOP_RECORDING_DIRECTORY OUTPUT_DIRECTORY" >&2
  exit 2
fi

recording_directory=$1
output_directory=$2
python=${PHYSICAL_AI_PYTHON:-python3}

scripts/prepare_human_visual_bc_dataset.sh "$recording_directory" "$output_directory"
"$python" scripts/validate_demonstration_scenario.py \
  "$output_directory/manifest.jsonl" human-head-to-head-overtake
