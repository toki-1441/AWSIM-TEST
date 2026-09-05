# Course and vehicle visuals

`PhysicalAiTrainingScene` is generated from
`PhysicalAiTrainingSceneCreator.CreateOrReplace`.  Visual assets are therefore
created and assigned by `PhysicalAiVisualAssets`, rather than relying on manual
Unity editor state.

## Perception contract

The scene uses HDRP and contains a directional key light plus a global volume.
Both the ego and chase cameras clear to the same muted venue colour rather than
depending on HDRP's sky initialisation; this prevents a black background during
player startup. Exposure is fixed at 9 EV. Automatic exposure is deliberately
excluded: its adaptation would make an unchanged scene encode as different camera
observations on successive policy steps.

The ego camera is mounted about 0.15 m above the floor and pitches down 22°. Its
0.02 m near clipping plane and 120 m far clipping plane keep both the compact car
and the upcoming corridor visible; Unity's default 0.3 m near plane is nearly the
length of the 0.385 m TT-02 and hides obstacles inside its stopping distance.

Streaming episodes preload three deterministic course chunks before the first
physics step, so the camera can render the upcoming road and turns rather than
only the initial short chunk. Subsequent chunks retain the same append/discard
streaming behaviour, extending again while about 12 m of generated road remains.

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

The rendered road cubes are deliberately not colliders. They sit over one large,
continuous `PhysicalAiGround` floor, the only layer considered by wheel
raycasts. This avoids wheel contact switching between overlapping or adjacent
road pieces at segment joins.

The asphalt is lifted 2 mm above that floor and both road and duct sections have
non-overlapping ends, avoiding depth-buffer flicker. The vehicle body has a
physical box collider, while every duct boundary keeps an enabled box collider;
the walls therefore stop the car without ever being a tire-contact surface.

The upper-right 384×216 inset displays the same render texture captured by the
ego perception camera. It is a display-only `RawImage`: it does not add a second
sensor or change the observation contract.

## Verification

Regenerating the scene compiles cleanly and the EditMode suite passes 67 cases.
The camera image must be inspected from a graphics-enabled player because the
headless graphics device intentionally cannot produce a rendered frame. The two
graphics-enabled PlayMode contracts pass in an isolated Unity test scene; the
normal player bootstrap recognises `-runTests` and does not start AWSIM's
Autoware demo during the test run.
