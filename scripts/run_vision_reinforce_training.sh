#!/usr/bin/env bash
# Repeatedly run the visual REINFORCE trainer against finite training episodes.
set -euo pipefail

if [ "$#" -lt 2 ]; then
  echo "usage: $0 OUTPUT_DIRECTORY ROUNDS [MAX_EPISODE_SECONDS]" >&2
  exit 2
fi

output_directory=$(realpath -m "$1")
rounds=$2
maximum_seconds=${3:-120}
player=${PHYSICAL_AI_PLAYER:-Build/AWSIM.x86_64}
python=${PHYSICAL_AI_PYTHON:-python3}
port=${PHYSICAL_AI_PORT:-5015}
device=${PHYSICAL_AI_TRAINER_DEVICE:-cpu}
episode_count=${PHYSICAL_AI_EPISODE_COUNT:-8}
initial_checkpoint=${PHYSICAL_AI_INITIAL_CHECKPOINT:-}
architecture=${PHYSICAL_AI_VISION_ARCHITECTURE:-legacy}
curriculum=${PHYSICAL_AI_RL_CURRICULUM:-balanced}
opponent_speed=${PHYSICAL_AI_OPPONENT_SPEED:-1.25}
opponent_initial_progress=${PHYSICAL_AI_OPPONENT_INITIAL_PROGRESS_METERS:-1.5}
opponent_lateral_offset=${PHYSICAL_AI_OPPONENT_LATERAL_OFFSET:--0.28}
opponent_lateral_amplitude=${PHYSICAL_AI_OPPONENT_LATERAL_AMPLITUDE:-0.08}
opponent_lateral_frequency=${PHYSICAL_AI_OPPONENT_LATERAL_FREQUENCY_HZ:-0.5}

case "$rounds" in ''|*[!0-9]*) echo "ROUNDS must be a positive integer" >&2; exit 2;; esac
case "$episode_count" in ''|*[!0-9]*) echo "PHYSICAL_AI_EPISODE_COUNT must be a positive integer" >&2; exit 2;; esac
case "$architecture" in legacy|spatial-v2) ;; *) echo "PHYSICAL_AI_VISION_ARCHITECTURE must be legacy or spatial-v2" >&2; exit 2;; esac
case "$curriculum" in balanced|nominal) ;; *) echo "PHYSICAL_AI_RL_CURRICULUM must be balanced or nominal" >&2; exit 2;; esac
test "$rounds" -gt 0
test "$episode_count" -gt 0
test -x "$player"
mkdir -p "$output_directory"
if [ -e "$output_directory/physical-ai-vision-reinforce.pt" ] || [ -e "$output_directory/physical-ai-vision-reinforce-metrics.jsonl" ]; then
  echo "Output directory already contains a visual RL checkpoint or metrics: $output_directory" >&2
  exit 1
fi

trainer_args=()
if [ -n "$initial_checkpoint" ]; then
  test -s "$initial_checkpoint"
  trainer_args=(--initialize-from "$initial_checkpoint")
fi

"$python" scripts/train_vision_rl.py --port "$port" \
  --device "$device" \
  --architecture "$architecture" \
  --output "$output_directory/physical-ai-vision-reinforce.pt" \
  --metrics "$output_directory/physical-ai-vision-reinforce-metrics.jsonl" \
  "${trainer_args[@]}" \
  >"$output_directory/trainer.log" 2>&1 &
trainer_pid=$!
cleanup() {
  status=$?
  kill "$trainer_pid" 2>/dev/null || true
  wait "$trainer_pid" 2>/dev/null || true
  exit "$status"
}
trap cleanup EXIT

sleep 1
for round in $(seq 1 "$rounds"); do
  scenario=nominal
  scenario_args=(-physicalAiHazardProbability 0)
  if [ "$curriculum" = balanced ]; then
    case $(((round - 1) % 3)) in
      0)
        scenario=nominal
        ;;
      1)
        scenario=blind-corner-hazard
        scenario_args=(-physicalAiHazardProbability 1)
        ;;
      2)
        scenario=head-to-head-moving-opponent
        scenario_args=(
          -physicalAiHazardProbability 0
          -physicalAiHeadToHead
          -physicalAiOpponentTargetSpeed "$opponent_speed"
          -physicalAiOpponentInitialProgressMeters "$opponent_initial_progress"
          -physicalAiOpponentLateralOffset "$opponent_lateral_offset"
          -physicalAiOpponentLateralOscillationAmplitude "$opponent_lateral_amplitude"
          -physicalAiOpponentLateralOscillationFrequencyHz "$opponent_lateral_frequency"
        )
        ;;
    esac
  fi
  echo "training round $round/$rounds: $scenario"
  # Visual REINFORCE must run with a graphics device. In batchmode HDRP exposes
  # a Null device and PythonRlBridge correctly cannot turn the sensor render
  # texture into a JPEG, silently reducing this to vector-only training.
  "$player" -physicalAiRlPort "$port" -physicalAiEvaluation rollout \
    -physicalAiTrainingRound "$round" \
    -physicalAiEpisodeCount "$episode_count" \
    -physicalAiMaximumEpisodeSeconds "$maximum_seconds" \
    "${scenario_args[@]}" \
    -physicalAiQuitAfterEvaluation \
    -logFile "$output_directory/training-$round-$scenario.log"
done

test -s "$output_directory/physical-ai-vision-reinforce.pt"
test -s "$output_directory/physical-ai-vision-reinforce-metrics.jsonl"
echo "checkpoint and learning curve written to $output_directory"
