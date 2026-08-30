#!/usr/bin/env bash
# Repeatedly run the visual REINFORCE trainer against finite training episodes.
set -euo pipefail

if [ "$#" -lt 2 ]; then
  echo "usage: $0 OUTPUT_DIRECTORY ROUNDS [MAX_EPISODE_SECONDS]" >&2
  exit 2
fi

output_directory=$1
rounds=$2
maximum_seconds=${3:-120}
player=${PHYSICAL_AI_PLAYER:-Build/AWSIM.x86_64}
python=${PHYSICAL_AI_PYTHON:-python3}
port=${PHYSICAL_AI_PORT:-5015}
device=${PHYSICAL_AI_TRAINER_DEVICE:-cpu}

case "$rounds" in ''|*[!0-9]*) echo "ROUNDS must be a positive integer" >&2; exit 2;; esac
test "$rounds" -gt 0
test -x "$player"
mkdir -p "$output_directory"

"$python" scripts/train_vision_rl.py --port "$port" \
  --device "$device" \
  --output "$output_directory/physical-ai-vision-reinforce.pt" \
  --metrics "$output_directory/physical-ai-vision-reinforce-metrics.jsonl" \
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
  echo "training round $round/$rounds"
  "$player" -batchmode -physicalAiRlPort "$port" -physicalAiEvaluation rollout \
    -physicalAiTrainingRound "$round" \
    -physicalAiMaximumEpisodeSeconds "$maximum_seconds" \
    -physicalAiQuitAfterEvaluation \
    -logFile "$output_directory/training-$round.log"
done

test -s "$output_directory/physical-ai-vision-reinforce.pt"
test -s "$output_directory/physical-ai-vision-reinforce-metrics.jsonl"
echo "checkpoint and learning curve written to $output_directory"
