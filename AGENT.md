# Agent Working Agreement

## Git and remote policy

- Do not commit to or push to a fork or any reference repository.
- Use `dev` as the integration branch. Start every goal branch from `dev`; do not make feature work directly on `main` or `dev`.
- Commit completed logical changes in the goal branch. Once the goal is verified, squash merge it into `dev`, create the resulting integration commit, and remove the local goal branch after confirming its content matches `dev`.
- Do not push unless the user explicitly requests it. Confirm `origin` targets `toki-1441/AWSIM-TEST` before any future push.

## Reference-material policy

- Material under `../ref/` is non-public reference information. Use it only to learn an implementation approach; do not copy its proprietary content, paths, configuration, or credentials into this repository.
- After extracting the permitted implementation approach, remove the local `../ref/AWSIM-Unity2022LTS` checkout. Do not commit reference material.
- Document only the independently implemented behavior and public operational instructions here.

## Current implementation priority

The active goal is operator usability: unified launch configuration, fixed/endless maps, manual
multimodal data and annotation recording/review, and RL start/stop/resume using the same settings.
See `docs/physical-ai-operator-workflows.md` for the full acceptance matrix and evidence.
Vehicle physics is a stable baseline, not a research workstream. Fix failures that prevent use;
do not make fine tyre calibration a prerequisite for collecting explicitly labelled simulator data.
Assisted and dynamic vehicle runs may both be recorded; retain their model/settings in metadata
and never present assisted data as physical-model or real-car validation.
Keep GUI, configuration-file and CLI behavior consistent and reject unsupported selections visibly.
Sensor mounting position and rotation are fixed configuration, not domain randomization.
Do not vary the camera extrinsics by episode seed; preserve them in recordings and RL resume contracts.
The Linux build helper remains local-only, uses the declared Unity version and preserves datasets.
