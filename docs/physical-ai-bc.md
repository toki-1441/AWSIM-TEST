# Offline behavior cloning

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
