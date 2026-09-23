# Offline behavior cloning

Operator-workflow recordings now use `teleop-<time>-<id>/frames.jsonl`, with a manifest and
event/map log beside it. Keep the whole directory. The exporter discovers both this format
and legacy flat recordings, and carries vehicle/action-space metadata. See
[operator workflows](physical-ai-operator-workflows.md) for inspection and the important
distinction between raw manual actuation and valid planning targets; exporting the legacy
manual proxy alone is not proof of a correct planning-learning dataset.

`PlanningDemonstrationRecorder` writes one JSON object per line. Train a linear
planning model without additional Python packages:

```bash
python3 scripts/train_bc.py path/to/teleop-episode-*.jsonl --output Assets/PhysicalAi/Models/bc-policy.json
```

Assign the generated JSON file as a `TextAsset` to `BehaviorCloningPolicy` on
the training vehicle. It predicts desired speed and normalized lateral offset;
the existing classical low-level controller still owns throttle, brake, and
steering. If the model's feature count does not match the active observation
layout, the orchestrator safely uses its reference fallback policy instead.

For a reproducible bootstrap before human data is available, a player rollout
may record the deterministic reference planner with
`-physicalAiRecordReferenceDemonstrations`. These files are named `reference-*`
and must remain distinguished from human teleoperation data when datasets are
curated. The same camera-frame export and visual BC trainer accept either source.

## Preparing an auditable human visual dataset

Run the player normally (not with `-physicalAiReferenceDriver`), press F8 before
driving and again at the end of each demonstration. Copy only the resulting
`teleop-*.jsonl` files from the player's `PhysicalAiDemonstrations` persistent-data
directory into one collection directory. Then export and validate them:

```bash
PHYSICAL_AI_PYTHON=../.physical-ai-venv/bin/python \
  scripts/prepare_human_visual_bc_dataset.sh path/to/human-recordings Build/camera-demos/human-001
```

The command rejects an empty collection, unsynchronised camera/command frames,
missing JPEGs, inconsistent observation layouts, and `reference-*` inputs. Retain
the source JSONL files and the resulting manifest alongside the run video and
streaming-validation log; they are the provenance for a human-BC claim.

## Camera-priority visual BC

The planning observation contains relative generated-course waypoints, which are
useful for a non-visual baseline but are privileged geometry rather than camera
evidence. To test a camera-priority policy without changing the runtime
observation contract, train with waypoint-feature dropout. The first four
vehicle/safety values remain; the per-waypoint values are masked on the selected
fraction of training samples.

```bash
../.physical-ai-venv/bin/python scripts/train_visual_bc.py \
  Build/camera-demos/human-001/manifest.jsonl \
  --output Build/training/human-camera-priority.pt \
  --architecture spatial-v2 --waypoint-dropout 0.75 --seed 1 --device cuda
```

Report both normal evaluation and an image-blackout ablation before claiming the
policy is camera-based. Synthetic `reference-*` data can validate this mechanism
only; it cannot replace that human-data evidence.

The final `run_goal_gate.sh` workflow generates this ablation from the identical checkpoint,
records its SHA-256 in every policy label, and requires the same ordered holdout seeds. A report
from the reference planner or another checkpoint is rejected rather than treated as the
no-camera baseline.

`spatial-v2` retains a compact 3×5 feature map rather than globally averaging the
image to one value per channel, so it can represent whether the road turns left
or right. Existing checkpoints without an architecture label continue to load as
the legacy global-pooling model.
