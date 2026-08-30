#!/usr/bin/env bash
# Run one visual checkpoint on the training and unseen-course suites.
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

"$python" scripts/serve_vision_policy.py "$checkpoint" --port "$port" \
  >"$output_directory/policy-server.log" 2>&1 &
server_pid=$!
trap 'kill "$server_pid" 2>/dev/null || true; wait "$server_pid" 2>/dev/null || true' EXIT

sleep 1
"$player" -batchmode -physicalAiRlPort "$port" -physicalAiEvaluation training \
  -physicalAiMaximumEpisodeSeconds "$maximum_seconds" \
  -physicalAiReportDirectory "$output_directory" \
  -physicalAiQuitAfterEvaluation \
  -logFile "$output_directory/training-player.log"
"$player" -batchmode -physicalAiRlPort "$port" -physicalAiEvaluation holdout \
  -physicalAiMaximumEpisodeSeconds "$maximum_seconds" \
  -physicalAiReportDirectory "$output_directory" \
  -physicalAiQuitAfterEvaluation \
  -logFile "$output_directory/holdout-player.log"

test -s "$output_directory/physical-ai-training-report.json"
test -s "$output_directory/physical-ai-holdout-report.json"
echo "reports written to $output_directory"
