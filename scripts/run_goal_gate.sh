#!/usr/bin/env bash
# Evaluate one visual checkpoint and create all inputs to evaluate_goal_metrics.py.
set -euo pipefail

if [ "$#" -lt 2 ]; then
  echo "usage: $0 CHECKPOINT OUTPUT_DIRECTORY [MAX_EPISODE_SECONDS]" >&2
  exit 2
fi

checkpoint=$1
output_directory=$2
maximum_seconds=${3:-120}
player=${PHYSICAL_AI_PLAYER:-Build/AWSIM.x86_64}
python=${PHYSICAL_AI_PYTHON:-python3}
port=${PHYSICAL_AI_PORT:-5015}

test -f "$checkpoint"
test -x "$player"
mkdir -p "$output_directory"

PHYSICAL_AI_PLAYER="$player" PHYSICAL_AI_PYTHON="$python" PHYSICAL_AI_PORT="$port" \
  scripts/run_visual_evaluation.sh "$checkpoint" "$output_directory/model" "$maximum_seconds"

for baseline in blind-baseline race-baseline; do
  arguments=(-batchmode -physicalAiEvaluation holdout -physicalAiMaximumEpisodeSeconds "$maximum_seconds"
    -physicalAiReportDirectory "$output_directory/$baseline" -physicalAiQuitAfterEvaluation)
  if [ "$baseline" = blind-baseline ]; then
    arguments+=(-physicalAiDisableVisibilitySafety)
  fi
  "$player" "${arguments[@]}" -logFile "$output_directory/$baseline/player.log"
done

"$python" scripts/evaluate_goal_metrics.py \
  --training "$output_directory/model/physical-ai-training-report.json" \
  --holdout "$output_directory/model/physical-ai-holdout-report.json" \
  --blind-baseline "$output_directory/blind-baseline/physical-ai-holdout-report.json" \
  --race-baseline "$output_directory/race-baseline/physical-ai-holdout-report.json" \
  --output "$output_directory/goal-gate.json"
