# Agent Working Agreement

## Git and remote policy

- Do not commit to or push to a fork or any reference repository.
- Make work in a dedicated branch of this `toki-1441/AWSIM-TEST` repository and record completed logical changes with commits.
- Do not push unless the user explicitly requests it. Confirm `origin` targets `toki-1441/AWSIM-TEST` before any future push.

## Reference-material policy

- Material under `../ref/` is non-public reference information. Use it only to learn an implementation approach; do not copy its proprietary content, paths, configuration, or credentials into this repository.
- After extracting the permitted implementation approach, remove the local `../ref/AWSIM-Unity2022LTS` checkout. Do not commit reference material.
- Document only the independently implemented behavior and public operational instructions here.

## Current implementation priority

The first technical milestone is a Linux batch-build workflow. It must use the Unity version declared in `ProjectSettings/ProjectVersion.txt`, detect a concurrently running editor, and produce an ignored local build artifact without embedding machine-specific paths.
