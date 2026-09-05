#!/usr/bin/env bash
# Launch an operator-driven endless-streaming validation and preserve its log.
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 OUTPUT_DIRECTORY" >&2
  exit 2
fi

output_directory=$(realpath -m "$1")
player=${PHYSICAL_AI_PLAYER:-Build/AWSIM.x86_64}
mkdir -p "$output_directory"
test -x "$player"

log_path="$output_directory/player.log"
summary_path="$output_directory/streaming-continuity-summary.txt"
report_path="$output_directory/instrumented-streaming-report.json"
assessment_path="$output_directory/operator-assessment.json"

for evidence_path in "$log_path" "$report_path" "$assessment_path" "$output_directory/manual-drive.mp4"; do
  if [ -e "$evidence_path" ]; then
    echo "Refusing to overwrite existing validation evidence: $evidence_path" >&2
    echo "Use a new output directory for each manual run." >&2
    exit 1
  fi
done
cp scripts/manual-streaming-operator-assessment.template.json "$assessment_path"

cat >"$summary_path" <<'EOF'
Manual streaming validation
===========================
Drive with W/S/A/D or arrow keys. F8 starts/stops a human demonstration recording.
The run has blind-corner hazards disabled so this validation isolates streamed-road
geometry, rendering, boundary collision, and generation-boundary dynamics.

After the player closes, inspect player.log:
  - each "Physical AI streaming transition" must say "is continuous";
  - there must be no "Physical AI streaming discontinuity" line;
  - at least three append events, a history trim, and a floating-origin rebase
    must have occurred;
  - appended-corner evidence must contain both "left" and "right";
  - each line records retained/forward waypoint counts and total chunk count;
  - each line also records unchanged control input, grounded-wheel mask,
    suspension compression, and suspension velocity;
  - capture a video while passing multiple generated chunks and a rebase;
  - after reviewing it, complete operator-assessment.json and finalize with:
      python3 scripts/validate_manual_streaming.py OUTPUT/player.log \
        --operator-assessment OUTPUT/operator-assessment.json \
        --video OUTPUT/manual-drive.mp4 \
        --output OUTPUT/manual-streaming-report.json

This script does not claim that a reference or learned policy is safe. It records
only the operator-driven streaming integrity evidence required before data collection.
EOF

echo "Manual validation starts now. Drive far enough to trigger several generation events, then close the player."
"$player" -physicalAiHazardProbability 0 -logFile "$log_path"

if [ ! -s "$log_path" ]; then
  echo "No player log was written to $log_path; Unity's platform log location must be copied into this directory." >&2
  exit 1
fi
python3 scripts/validate_manual_streaming.py "$log_path" --output "$report_path"
echo "Continuous instrumented transitions recorded in $log_path"
echo "Complete $assessment_path after reviewing a >=30 s manual-drive video, then run the finalization command in $summary_path."
