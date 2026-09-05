#!/usr/bin/env bash
# Run one visual checkpoint on the training and unseen-course suites.
set -euo pipefail

if [ "$#" -lt 2 ]; then
  echo "usage: $0 CHECKPOINT OUTPUT_DIRECTORY [MAX_EPISODE_SECONDS]" >&2
  exit 2
fi

checkpoint=$1
output_directory=$(realpath -m "$2")
maximum_seconds=${3:-120}
player=${PHYSICAL_AI_PLAYER:-Build/AWSIM.x86_64}
python=${PHYSICAL_AI_PYTHON:-python3}
port=${PHYSICAL_AI_PORT:-5015}
device=${PHYSICAL_AI_POLICY_DEVICE:-cpu}
hazard_probability=${PHYSICAL_AI_HAZARD_PROBABILITY:-}
image_blackout=${PHYSICAL_AI_IMAGE_BLACKOUT:-}
head_to_head=${PHYSICAL_AI_HEAD_TO_HEAD:-}
opponent_speed=${PHYSICAL_AI_OPPONENT_SPEED:-}
opponent_initial_progress=${PHYSICAL_AI_OPPONENT_INITIAL_PROGRESS_METERS:-}
opponent_lateral_offset=${PHYSICAL_AI_OPPONENT_LATERAL_OFFSET:-}
opponent_lateral_amplitude=${PHYSICAL_AI_OPPONENT_LATERAL_AMPLITUDE:-}
opponent_lateral_frequency=${PHYSICAL_AI_OPPONENT_LATERAL_FREQUENCY_HZ:-}

test -f "$checkpoint"
test -x "$player"
mkdir -p "$output_directory"
checkpoint_sha256=$(sha256sum "$checkpoint" | awk '{print $1}')
policy_label="${PHYSICAL_AI_POLICY_LABEL:-visual-checkpoint:$(basename "$checkpoint")}:sha256=${checkpoint_sha256}"

player_args=()
if [ -n "$hazard_probability" ]; then
  player_args+=(-physicalAiHazardProbability "$hazard_probability")
fi
case "$image_blackout" in
  "") ;;
  1|true|TRUE|yes|YES)
    player_args+=(-physicalAiImageBlackout)
    policy_label="${policy_label}+image-blackout"
    ;;
  *)
    echo "PHYSICAL_AI_IMAGE_BLACKOUT must be 1, true, or yes when set" >&2
    exit 2
    ;;
esac

case "$head_to_head" in
  "") ;;
  1|true|TRUE|yes|YES)
    player_args+=(-physicalAiHeadToHead)
    policy_label="${policy_label}+head-to-head"
    ;;
  *)
    echo "PHYSICAL_AI_HEAD_TO_HEAD must be 1, true, or yes when set" >&2
    exit 2
    ;;
esac

if [ -n "$opponent_speed" ] || [ -n "$opponent_initial_progress" ] || [ -n "$opponent_lateral_offset" ] ||
   [ -n "$opponent_lateral_amplitude" ] || [ -n "$opponent_lateral_frequency" ]; then
  case "$head_to_head" in
    1|true|TRUE|yes|YES) ;;
    *) echo "Opponent probe settings require PHYSICAL_AI_HEAD_TO_HEAD=1" >&2; exit 2 ;;
  esac
  if [ -n "$opponent_speed" ]; then player_args+=(-physicalAiOpponentTargetSpeed "$opponent_speed"); fi
  if [ -n "$opponent_initial_progress" ]; then player_args+=(-physicalAiOpponentInitialProgressMeters "$opponent_initial_progress"); fi
  if [ -n "$opponent_lateral_offset" ]; then player_args+=(-physicalAiOpponentLateralOffset "$opponent_lateral_offset"); fi
  if [ -n "$opponent_lateral_amplitude" ]; then player_args+=(-physicalAiOpponentLateralOscillationAmplitude "$opponent_lateral_amplitude"); fi
  if [ -n "$opponent_lateral_frequency" ]; then player_args+=(-physicalAiOpponentLateralOscillationFrequencyHz "$opponent_lateral_frequency"); fi
fi

"$python" scripts/serve_vision_policy.py "$checkpoint" --port "$port" --device "$device" \
  >"$output_directory/policy-server.log" 2>&1 &
server_pid=$!
trap 'kill "$server_pid" 2>/dev/null || true; wait "$server_pid" 2>/dev/null || true' EXIT

sleep 1
# Do not use -batchmode here: HDRP exposes a Null graphics device in that mode
# and the Unity bridge correctly withholds unavailable camera readbacks. Visual
# evaluation requires a working display so that the checkpoint receives JPEGs.
"$player" -physicalAiRlPort "$port" -physicalAiEvaluation training \
  -physicalAiMaximumEpisodeSeconds "$maximum_seconds" \
  -physicalAiReportDirectory "$output_directory" \
  -physicalAiPolicyLabel "$policy_label" \
  "${player_args[@]}" \
  -physicalAiQuitAfterEvaluation \
  -logFile "$output_directory/training-player.log"
"$player" -physicalAiRlPort "$port" -physicalAiEvaluation holdout \
  -physicalAiMaximumEpisodeSeconds "$maximum_seconds" \
  -physicalAiReportDirectory "$output_directory" \
  -physicalAiPolicyLabel "$policy_label" \
  "${player_args[@]}" \
  -physicalAiQuitAfterEvaluation \
  -logFile "$output_directory/holdout-player.log"

test -s "$output_directory/physical-ai-training-report.json"
test -s "$output_directory/physical-ai-holdout-report.json"
if [ -n "$image_blackout" ]; then
  grep -Eq '"cameraFramesWithheld": [1-9][0-9]*' "$output_directory/physical-ai-training-report.json"
  grep -Eq '"cameraFramesWithheld": [1-9][0-9]*' "$output_directory/physical-ai-holdout-report.json"
else
  grep -Eq '"cameraFramesCaptured": [1-9][0-9]*' "$output_directory/physical-ai-training-report.json"
  grep -Eq '"cameraFramesCaptured": [1-9][0-9]*' "$output_directory/physical-ai-holdout-report.json"
fi
echo "reports written to $output_directory"
