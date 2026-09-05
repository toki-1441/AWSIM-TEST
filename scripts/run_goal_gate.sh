#!/usr/bin/env bash
# Evaluate one visual checkpoint and create all inputs to evaluate_goal_metrics.py.
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
hazard_probability=${PHYSICAL_AI_HAZARD_PROBABILITY:-1}
overtake_opponent_speed=${PHYSICAL_AI_OVERTAKE_OPPONENT_SPEED:-1.25}
overtake_opponent_initial_progress=${PHYSICAL_AI_OVERTAKE_OPPONENT_INITIAL_PROGRESS_METERS:-1.5}
# The default word itself is negative, so Bash needs the parameter-default
# operator (`:-`) followed by a second minus sign (`-0.28`).
overtake_opponent_lateral_offset=${PHYSICAL_AI_OVERTAKE_OPPONENT_LATERAL_OFFSET:--0.28}
overtake_opponent_lateral_amplitude=${PHYSICAL_AI_OVERTAKE_OPPONENT_LATERAL_AMPLITUDE:-0.08}
overtake_opponent_lateral_frequency=${PHYSICAL_AI_OVERTAKE_OPPONENT_LATERAL_FREQUENCY_HZ:-0.5}

test -f "$checkpoint"
test -x "$player"
mkdir -p "$output_directory"

PHYSICAL_AI_PLAYER="$player" PHYSICAL_AI_PYTHON="$python" PHYSICAL_AI_PORT="$port" PHYSICAL_AI_HAZARD_PROBABILITY="$hazard_probability" \
  scripts/run_visual_evaluation.sh "$checkpoint" "$output_directory/model" "$maximum_seconds"

# Blind-corner safety is a paired ablation, not a comparison against a different
# reference policy. Run the identical checkpoint and suites again while only the
# rendered camera payload is withheld at the Unity/Python boundary.
PHYSICAL_AI_PLAYER="$player" PHYSICAL_AI_PYTHON="$python" PHYSICAL_AI_PORT="$port" PHYSICAL_AI_HAZARD_PROBABILITY="$hazard_probability" \
  PHYSICAL_AI_IMAGE_BLACKOUT=1 \
  scripts/run_visual_evaluation.sh "$checkpoint" "$output_directory/model-image-blackout" "$maximum_seconds"

# Generalisation and head-to-head are distinct evaluations.  The latter must
# execute the supplied visual checkpoint with an opponent present; a solo
# report has no evidence for collision-free overtaking or defence.
PHYSICAL_AI_PLAYER="$player" PHYSICAL_AI_PYTHON="$python" PHYSICAL_AI_PORT="$port" PHYSICAL_AI_HAZARD_PROBABILITY="$hazard_probability" \
  PHYSICAL_AI_HEAD_TO_HEAD=1 \
  PHYSICAL_AI_OPPONENT_SPEED="$overtake_opponent_speed" \
  PHYSICAL_AI_OPPONENT_INITIAL_PROGRESS_METERS="$overtake_opponent_initial_progress" \
  PHYSICAL_AI_OPPONENT_LATERAL_OFFSET="$overtake_opponent_lateral_offset" \
  PHYSICAL_AI_OPPONENT_LATERAL_AMPLITUDE="$overtake_opponent_lateral_amplitude" \
  PHYSICAL_AI_OPPONENT_LATERAL_FREQUENCY_HZ="$overtake_opponent_lateral_frequency" \
  scripts/run_visual_evaluation.sh "$checkpoint" "$output_directory/model-head-to-head" "$maximum_seconds"

for baseline in race-baseline; do
  arguments=(-batchmode -physicalAiEvaluation holdout -physicalAiMaximumEpisodeSeconds "$maximum_seconds"
    -physicalAiReportDirectory "$output_directory/$baseline" -physicalAiQuitAfterEvaluation)
  if [ "$baseline" = race-baseline ]; then
    # Race metrics are meaningful only when the independently-scored
    # kinematic opponent is explicitly enabled. Solo evaluation deliberately
    # disables it to keep generalisation results uncontaminated.
    arguments+=(-physicalAiHeadToHead -physicalAiOpponentTargetSpeed "$overtake_opponent_speed" -physicalAiOpponentInitialProgressMeters "$overtake_opponent_initial_progress" -physicalAiOpponentLateralOffset "$overtake_opponent_lateral_offset" -physicalAiOpponentLateralOscillationAmplitude "$overtake_opponent_lateral_amplitude" -physicalAiOpponentLateralOscillationFrequencyHz "$overtake_opponent_lateral_frequency")
  fi
  arguments+=(-physicalAiHazardProbability "$hazard_probability")
  "$player" "${arguments[@]}" -logFile "$output_directory/$baseline/player.log"
done

"$python" scripts/evaluate_goal_metrics.py \
  --training "$output_directory/model/physical-ai-training-report.json" \
  --holdout "$output_directory/model/physical-ai-holdout-report.json" \
  --head-to-head "$output_directory/model-head-to-head/physical-ai-holdout-report.json" \
  --blind-baseline "$output_directory/model-image-blackout/physical-ai-holdout-report.json" \
  --race-baseline "$output_directory/race-baseline/physical-ai-holdout-report.json" \
  --output "$output_directory/goal-gate.json"
