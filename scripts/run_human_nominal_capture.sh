#!/usr/bin/env bash
# Launch a clean human course-following demonstration session.
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 OUTPUT_DIRECTORY" >&2
  exit 2
fi

output_directory=$(realpath -m "$1")
player=${PHYSICAL_AI_PLAYER:-Build/AWSIM.x86_64}
mkdir -p "$output_directory"
test -x "$player"

cat <<EOF
Nominal human demonstration capture
===================================
Controls: W/S/A/D or arrow keys. Press F8 to start and stop each recording.
Hazards and opponents are disabled. Record clean course following at varied
speeds, deliberate braking and recovery from imperfect lines.

Recordings are written directly under:
  $output_directory
EOF

"$player" \
  -physicalAiHazardProbability 0 \
  -physicalAiDemonstrationDirectory "$output_directory" \
  -physicalAiDemonstrationScenario human-nominal \
  -logFile "$output_directory/player.log"

recording_count=$(find "$output_directory" -maxdepth 2 -type f \( -name 'teleop-*.jsonl' -o -path '*/teleop-*/frames.jsonl' \) | wc -l)
if [ "$recording_count" -eq 0 ]; then
  echo "No human recordings were captured. Rerun and use F8 while driving." >&2
  exit 1
fi
echo "Captured $recording_count nominal human recording(s)."
echo "Next: scripts/prepare_human_nominal_dataset.sh $output_directory Build/camera-demos/human-nominal-001"
