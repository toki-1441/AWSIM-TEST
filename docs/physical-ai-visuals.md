# Course and vehicle visuals

`PhysicalAiTrainingScene` is generated from
`PhysicalAiTrainingSceneCreator.CreateOrReplace`.  Visual assets are therefore
created and assigned by `PhysicalAiVisualAssets`, rather than relying on manual
Unity editor state.

## Perception contract

The scene uses HDRP and contains a directional key light plus a global volume.
Exposure is fixed at 9 EV. Automatic exposure is deliberately excluded: its
adaptation would make an unchanged scene encode as different camera observations
on successive policy steps.

The ego camera has a 0.02 m near clipping plane and a 120 m far clipping plane.
The former matters at 1/10 scale: Unity's default 0.3 m plane is nearly the
length of the 0.385 m TT-02 and hides obstacles inside its stopping distance.

## Assets and geometry

The generator creates HDRP/Lit assets for carpet, road, both duct-wall colours,
ego and opponent bodies, glass, and tires. The yellow regulation centre line
uses HDRP/Unlit, avoiding the magenta fallback produced by `Sprites/Default`
under HDRP. Course boundary walls alternate white and red every three generated
segments.

The vehicle visual is the repository's existing Hatchback prefab, scaled from
its wheelbase to the TT-02 specification. Its four visual wheels are moved below
the physical wheel mounts; colliders from the donor prefab are removed, so they
cannot affect the ray-cast vehicle model. If the donor asset is unavailable,
the generator emits a material-assigned primitive as an explicit fallback.

## Verification

Regenerating the scene compiles cleanly and the EditMode suite passes 51 cases.
The camera image must be inspected from a graphics-enabled player because the
headless graphics device intentionally cannot produce a rendered frame. The two
graphics-enabled PlayMode contracts pass in an isolated Unity test scene; the
normal player bootstrap recognises `-runTests` and does not start AWSIM's
Autoware demo during the test run.
