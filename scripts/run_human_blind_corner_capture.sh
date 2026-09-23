#!/usr/bin/env bash
# Launch a human demonstration session with forced blind-corner hazard exposure.
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
Blind-corner human demonstration capture
========================================
Controls: W/S/A/D or arrow keys. Press F8 to start and stop each recording.
Hazards are forced on. Record early braking, sightline-preserving approaches,
safe avoidance and recovery; close the player when finished.

Recordings are written directly under:
  $output_directory
EOF

"$player" \
  -physicalAiHazardProbability 1 \
  -physicalAiDemonstrationDirectory "$output_directory" \
  -physicalAiDemonstrationScenario human-blind-corner-hazard \
  -logFile "$output_directory/player.log"

recording_count=$(find "$output_directory" -maxdepth 2 -type f \( -name 'teleop-*.jsonl' -o -path '*/teleop-*/frames.jsonl' \) | wc -l)
if [ "$recording_count" -eq 0 ]; then
  echo "No human recordings were captured. Rerun and use F8 while driving." >&2
  exit 1
fi
echo "Captured $recording_count human blind-corner recording(s)."
echo "Next: scripts/prepare_human_blind_corner_dataset.sh $output_directory Build/camera-demos/human-blind-corner-001"
