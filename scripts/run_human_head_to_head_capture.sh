#!/usr/bin/env bash
# Launch an operator-driven head-to-head recording session with an auditable output location.
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 OUTPUT_DIRECTORY" >&2
  exit 2
fi

output_directory=$(realpath -m "$1")
player=${PHYSICAL_AI_PLAYER:-Build/AWSIM.x86_64}
opponent_speed=${PHYSICAL_AI_OPPONENT_SPEED:-1.25}
opponent_initial_progress=${PHYSICAL_AI_OPPONENT_INITIAL_PROGRESS_METERS:-1.5}
opponent_lateral_offset=${PHYSICAL_AI_OPPONENT_LATERAL_OFFSET:--0.28}
opponent_lateral_amplitude=${PHYSICAL_AI_OPPONENT_LATERAL_AMPLITUDE:-0.08}
opponent_lateral_frequency=${PHYSICAL_AI_OPPONENT_LATERAL_FREQUENCY_HZ:-0.5}
mkdir -p "$output_directory"
test -x "$player"

cat <<EOF
Head-to-head human demonstration capture
========================================
Controls: W/S/A/D or arrow keys. Press F8 to start and stop each recording.
Drive past the slower, laterally-offset opponent without contact; include safe
braking, recovery and defensive examples. Close the player when finished.

Recordings are written directly under:
  $output_directory

Only human F8 recordings use the teleop-* prefix. Do not mix reference-policy
files or holdout evaluation seeds into this directory.
EOF

"$player" \
  -physicalAiHeadToHead \
  -physicalAiOpponentTargetSpeed "$opponent_speed" \
  -physicalAiOpponentInitialProgressMeters "$opponent_initial_progress" \
  -physicalAiOpponentLateralOffset "$opponent_lateral_offset" \
  -physicalAiOpponentLateralOscillationAmplitude "$opponent_lateral_amplitude" \
  -physicalAiOpponentLateralOscillationFrequencyHz "$opponent_lateral_frequency" \
  -physicalAiDemonstrationDirectory "$output_directory" \
  -physicalAiDemonstrationScenario human-head-to-head-overtake \
  -logFile "$output_directory/player.log"

recording_count=$(find "$output_directory" -maxdepth 2 -type f \( -name 'teleop-*.jsonl' -o -path '*/teleop-*/frames.jsonl' \) | wc -l)
if [ "$recording_count" -eq 0 ]; then
  echo "No human recordings were captured. Rerun and use F8 while driving." >&2
  exit 1
fi
echo "Captured $recording_count human head-to-head recording(s)."
echo "Next: scripts/prepare_human_head_to_head_dataset.sh $output_directory Build/camera-demos/human-head-to-head-001"
