# Ramp crew

One shared generated worker, 1.78 m tall, in navy work clothing, a fluorescent vest with reflective bands,
coated work gloves, reinforced trouser knees, boots, cap and ear defenders. Four atlases vary the skin tone
while keeping the same unbranded uniform. The [IATA ground-handling photographs](https://www.iata.org/en/youandiata/ground-handlers/)
provide a reference for clothing and equipment scale. Baggage stations, container stations and the depot
use it as drivers, loader operators and handlers; gate arrivals use it as a marshaller (below).

## Model

`ArtSource/Characters/RampCrew/` generates everything; nothing is hand-edited and nothing is downloaded.

- `build.py` drives Blender: meshes each part, skins it, bakes the atlases, builds the three LODs and writes
  `RampCrew.fbx`, `RampCrew.crew` (body dimensions and handling timings) and the marshalling wand.
- `shapes.py` holds the shapes as signed distance fields, the paint classes, the skin-weight functions and
  `JOINTS` (the skeleton). Proportions follow adult male anthropometry (ANSUR II means) scaled to 1.78 m.
- `sdf.py` is the numpy-only signed-distance toolkit and surface-nets mesher.
- `review.py` renders the built worker in Blender without Unity (see Review).

Body, head, gloves, boots, cap and ear defenders are distance fields meshed per part and decimated. The vest
is its own thin shell over the torso, so its outline is real geometry, with analytic reflective bands; the belt
sits under it. The far LOD includes the vest in the body surface. There is no radio clip, ID badge or mic boom.

Atlases are 1024 px and baked per texel from the same fields (colour, smoothness, a tangent-space normal map),
so face, tape and seams survive decimation: one albedo per skin tone plus a shared `RampCrew_Normal` and
`RampCrew_MetalGloss`. `RampCrewImporter` imports the worker atlases with high-quality block compression (BC7
on desktop, about 8 MB for all six); the small wand palettes stay uncompressed. The far LOD has no unwrapped
texture detail: its faces sample flat class colours from swatches in the atlas's top strip. One material and
draw per LOD; the model worker's build reports about 27.6k / 5.0k / 890 triangles, the far LOD importing at
about 580 vertices (under the 1,000-vertex budget in `RampCrewTests`).

The skeleton (rest T-pose, model space +X right, +Y up, +Z forward) is `Hips, Spine, Chest, Neck, Head` and per
side `Clavicle_, UpperArm_, Forearm_, Hand_, Fingers_, Thumb_, Thigh_, Shin_, Foot_, Toe_` (`L`/`R`): 25 bones,
all weighted in every LOD. `Clavicle` (parent `Chest`) carries the upper arm; `Fingers` curls the four fingers
together and `Thumb` flexes the thumb, both about local X (negative toward the palm; about 80° and 35° make a
fist); `Toe` hinges at the ball of the foot about local X (negative bends the toes up).

```sh
rtk proxy /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python ArtSource/Characters/RampCrew/build.py
rtk proxy python3 ArtSource/Vehicles/crew_profiles.py
rtk proxy timeout 300 /Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath . -executeMethod AirCity.EditorTools.Characters.RampCrewSetup.Install -logFile Logs/ramp-crew-import.log
```

`RampCrewSetup.CreateAssets` creates the editable prefab once and afterwards only refreshes generated material
textures, normal maps and smoothness. When the generated skeleton changes, it replaces just the prefab's
`Model` child; hand edits on the root (`RampCrewRig`, the LOD group, materials, LOD thresholds) stay. The
installer assigns crew to existing stations in Sandbox, BaggageTurnaround and ContainerTurnaround; scene
generators and stations added from the sandbox copy the same references. Vehicle `CREW` declarations author
seat/platform, hand and foot positions in Blender metres; the vehicle pipeline exports them and
`crew_profiles.py` refreshes just those fields when vehicle geometry has not changed. The occupied height
counts in aircraft clearance. The belt loader also exports its access treads and walkway offset, so the
boarding path follows its stairs and catwalk as the conveyor rises.

## Rig

`RampCrewRig` (`Scripts/Runtime/Characters`) turns a `RampCrewPose` into bone transforms every frame. It is
presentation only: the simulation stays headless and deterministic, and nothing the rig does feeds back.
Segment lengths, shoulder and hip offsets come from the skeleton's rest pose at `Initialize`; `Clavicle`,
`Fingers`, `Thumb` and `Toe` are driven when present. Palms land exactly (under 2 mm) on hand targets in reach,
and `LeftPalm`, `RightPalm`, `CarrySocket` and `Grip` keep their meaning for bags, wands and controls.

Call `rig.Apply(pose, gameDt, airport.timeScale)`. The rig advances its own state by screen time
(`gameDt / timeScale`, at most 0.1 s per call), so walking and easing look the same at every clock speed; the old
`Apply(pose)` assumes one frame of real time.

What the rig does by itself, so producers should not fake it:

- **Feet.** A stateful stepper plants each foot in world space and steps from the root's real motion: walking
  forwards, backwards and sideways, turning on the spot (pivoting on the ball of the foot), small adjustment
  steps, heel strike and toe-off. Stride and cadence follow speed. Beyond about 3 steps per second (time-
  compressed work, fast-forward) the excess slides with the body and strides shorten, so legs never strobe; a
  foot is never left more than a stride from under its hip. Foot targets (`leftFoot`/`rightFoot`, seats,
  pedals, platforms) pin the feet instead; out of reach, the foot lifts along the shin rather than detaching.
- **Knees and pelvis height.** An upright `hipHeight` (about .94) is the legs' to settle: the pelvis sits exactly
  as high as the weight-bearing legs allow at a per-leg knee cap (interior thigh-shin angle, 180 = straight), so
  standing legs are nearly straight (about 173°, the rest skeleton's own knee is 176°) rather than crouched. Through a
  stride the cap follows human gait: 175.5° at heel strike, a give to about 163° as the leg takes weight, straight
  again from mid-stance to heel rise, then released so the trailing knee folds into the swing (about 58° of flexion).
  A swinging leg lets the pelvis rise above its reach of the ground only by less than the foot is lifted, and over
  the second half of the swing brings the pelvis down toward the height at which it will land straight, so the pelvis
  is highest over the stance leg and about 4.5 cm lower in double support. The cap is solved exactly (no wide smooth
  minimum), so the binding knee never reaches the two-bone solver's locked limit, where a straight knee would swing
  wildly for millimetres of foot motion; the legs' bound on the pelvis may fall at once but rises at most 0.45 m per
  screen second, so a lifting or relaxing leg never makes the pelvis jump. Lower hip heights (picks, placing, kneeling,
  sitting) are as asked, as before.
- **Trunk.** Lean is shared across pelvis, spine and chest; the pelvis sways and turns with the stride and the
  chest counter-rotates. When a hand target is beyond a comfortable reach the trunk leans, twists, side-bends
  and the hips shift; the clavicles protract and shrug. On the move the trunk folds much less (it reaches with
  the arms), so a walking worker never dives at a target. A root that turns instantly (a 180° route reversal)
  is followed by the body at a human rate, faster while the hands hold something.
- **Arms and hands.** Free arms hang and swing opposite the legs with a soft elbow; seated and kneeling free
  hands rest on the thighs. Attached hands follow their target from the first frame: the palm keeps the offset
  it had from the target when the target appeared and closes it in a short eased move (about 3.5 m/s), so a
  target that grows out of the hanging hand is tracked exactly. Wrist, elbow pole, forearm roll and finger curl
  ease in over about 0.2 s and come from the grip kind (`Bag`, `Handle`, `Wheel`, `Control`, `Flat`, `Wand`, or
  `Auto` from context). A `Wheel` grip (and a seated `Auto` hand) wraps the rim: `Fingers_*` curl to -75° about
  local X (a fist is about -80°) and `Thumb_*` closes 35° over it, eased in with the reach. Out of reach, the hand
  stops at full stretch instead of tearing off the forearm.
- **Head and life.** The head looks at the work in the hands, ahead while walking, out of the vehicle for
  drivers and operators, and glances around when idle, within neck limits. Idle workers breathe, shift weight
  and occasionally re-set a foot.
- **Postures.** `kneel` and `sit` (or the `kneeling`/`seated` switches, eased) are continuous: standing to
  kneeling and standing to sitting blend through real intermediate poses, with the knees placed on the floor
  and the toes tucked when kneeling.

State snaps to the pose (no motion) on the first `Apply`, on `OnEnable`, after the far-camera skip (workers
more than 250 m from `Camera.main` in Play mode keep their last pose), on `Snap()`, on a jump of more than
3 m plus 40 game m/s, and whenever no time passes and the pose changed (edit mode, tests). With no time passing
and an unchanged pose (airport paused) the worker is frozen. `Apply` allocates nothing; it costs roughly
30 to 90 µs per worker in the editor (Mono), measured by `RampCrewMotionTests`.

### Pose fields

All points are in the actor's local frame unless noted; booleans keep working as weight 0 or 1.

| Field | Default | Meaning |
|---|---|---|
| `position`, `rotation` | | Ground point and facing in the parent (airport) frame. Walking and turning are derived from their change. |
| `hipHeight`, `lean` | .94, 0 | Hip joint height (about .65 for a low pick, .38 kneeling) and forward trunk bend in degrees. |
| `hands`, `leftReach`, `rightReach` | false, 0 | Attach both / each palm to its target (weight 0..1). |
| `leftHand`, `rightHand` | | Palm centres. Kept while a reach weight eases out, so producers may zero them. |
| `seated`, `sit` / `kneeling`, `kneel` | false, 0 | Sitting / kneeling weights. Kneeling puts the knees down only below a hip height of about .75. |
| `leftFoot`, `rightFoot`, `feet` | zero, 0 | Floor points under the ankles; non-zero (or seated, or `feet` > 0) pins the feet there. |
| `leftGrip`, `rightGrip` | `Auto` | Grip kind: wrist, elbow and finger pose, never the palm position. |
| `leftPalmNormal`, `rightPalmNormal`, `leftFingers`, `rightFingers` | zero | Optional explicit hand orientation. |
| `lookAt` | zero | Optional point to look at. |
| `load` | 0 (auto ~.6 for a two-handed bag) | How heavy the held object is: elbows tuck, the body leans back. |
| `velocity` | zero | Optional root velocity, game m/s; otherwise measured from motion. |
| `sideBend`, `twist` | 0 | Extra trunk side bend and twist, degrees. |
| `stride`, `phase` | | Ignored (legacy); stepping comes from the root's motion. |

`FootContact(side, out heel, out ball)` and `FootSwinging(side)` report the feet after an `Apply` (footstep
sounds, decals).

## Marshaller

Gate arrivals also use this worker as a marshaller. `AirportMarshalling` owns its game-time walk and signals;
`MarshallerPresentation` instantiates the worker with two shared orange illuminated wands (wand grips, eyes on
the cockpit while signalling) and poses it each `LateUpdate` with the airport's game time; review tools call
`Present(dt)` themselves. The marshaller rigs are `DontSave` children of the airport. The character generator
also writes `MarshallingWand.fbx` and its albedo/emission maps; `RampCrewSetup` creates the editable wand
prefab and material once. See [traffic](../systems/traffic.md) for arrival integration.

## Review

Do not start batch Unity while the project is open in the editor.

Stills of the real turnarounds under `Captures/RampCrew/`:

```sh
rtk proxy timeout 600 /Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath . -executeMethod AirCity.EditorTools.Characters.RampCrewReview.FromCommandLine -logFile Logs/ramp-crew-review.log
```

A close shot of the seated tractor driver's hands on the wheel while driving in:
`BaggagePerformanceReview.FromCommandLine -performanceOnly wheel` writes
`Captures/RampCrewNew/performance/after/tractor-wheel-grip.mp4` and logs the finger and thumb angles.

Motion clips (MP4 plus a contact sheet of 24 evenly spaced frames, side and front views; needs
`/opt/homebrew/bin/ffmpeg`). `-motionSet human` plays the scripted human-pace motions in
`RampCrewMotionScripts` (walk and turn, side-step and backwards, pick/carry/place, kneel/shuffle/reach,
sit/wheel/stand, idle, an 18× dash); `game` records the narrowbody turnaround at `-motionTimeScale`;
`stations` follows the marshaller (walk-in, signals, walk-out), the seated tractor driver and the container
loader operator at ×1. `-motionOnly` filters by clip name. Human and station clips also log their knee angles
(`Motion review knees <clip>: both feet down ... mean, ... least (at t); stepping ... least`), so posture can be
judged from the log before opening a sheet. Posture before/after clips are under
`Captures/RampCrewNew/motion/posture/`, each sheet with a small `<clip>-small.jpg` copy and a `walk-strip.jpg`.

```sh
rtk proxy timeout 1800 /Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath . -executeMethod AirCity.EditorTools.Characters.RampCrewMotionReview.FromCommandLine -motionOut Captures/RampCrewNew/motion/after -motionSet human -logFile Logs/ramp-crew-motion.log
```

The model alone, in Blender (shots include `vest-front`, `vest-back`, `vest-34`, `vest-armhole`,
`vest-hem-back` and `cuff`):

```sh
blender -b --factory-startup --python ArtSource/Characters/RampCrew/review.py -- OUTDIR [tone] [lod] [shot,...]
```

## Tests

- `RampCrewTests`: skinned LODs, weights, floor clearance, palm targets, boarding/vehicle interlocks, one held
  bag per worker, blocked access, cancellation and retry.
- `RampCrewMotionTests`: a pop detector on every scripted motion (the largest per-sample change of every bone
  must shrink with a four times finer step), planted feet while walking and side-stepping, turning on the spot,
  first-Apply and teleport placement, pause freeze, readable legs at 18×, exact palms in random postures, the
  seated workstation, and zero allocations per `Apply`. Posture: standing, a signalling marshaller and the container
  loader operator keep knees at or above 170° (operator 165°), ankles 0.18-0.26 m apart, toes out at most 10°;
  walking and carrying keep a human knee envelope (at least 170° at heel strike, a loading give no lower than
  158°/155°, back to 170° in mid-stance, 50-75° of swing flexion) and 2.5-6 cm of pelvis rise and fall.
- `RampCrewPlayModeTests`: in Play mode, BaggageTurnaround (a marshalled arrival, dismount, unloading,
  loading, boarding and departure while the airport clock runs 0, 1, 12, 30, back to 1 mid-loading, and 0, with
  workers leaving and re-entering the far-camera skip), ContainerTurnaround (1, 30, 0) and Sandbox (1, 4, 30, 0):
  no error logs, every bone finite and attached to its parent.

Existing turnaround tests cover conservation, screening, arrivals capacity and combined container/bulk
departure readiness. See [the baggage system](../systems/baggage.md) for crew staffing and handling.
