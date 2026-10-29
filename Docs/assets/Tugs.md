# Tugs: TugSmall, TugLarge

Two towbarless aircraft tugs, one vehicle that both pushes back and tows, in two sizes. The user asked for a
small one for small airports and a full-size one for the big aircraft (2026-10-26), and chose towbarless over a
towbar tractor. How they push aircraft back is in [maintenance.md](../systems/maintenance.md#tugs-and-push-back).

A towbarless tug is a low, wide, flat tractor shaped like a U from above. The legs of the U at the back carry the
rear wheels, and the aircraft's nose wheel goes between them onto a **cradle**. The cradle is a platform with side
clamps, a pusher in front and a gate behind, on lift arms from the closed end of the U. It grips the wheel and
lifts it, and from then on tug and aircraft move as one: the tug steers the aircraft by turning its nose wheel.
The cab sits at a front corner, low enough to pass under a narrow-body's nose. The engine is under a low hood
beside it.

| Model | After | Takes | Size (w × l × h) |
|---|---|---|---|
| `TugSmall` | Kalmar TBL-50/100, Goldhofer AST-2 class | 10–100 t: turboprops, regional jets, A320 / 737 | 3.1 × 6.2 × 2.0 m (body 2.9 m wide, cab roof 1.74 m) |
| `TugLarge` | Goldhofer AST-1X, Kalmar TBL-600 class | 40–600 t: up to an A380 | 4.5 × 10.1 × 2.5 m (body 4.3 m wide, cab roof 2.28 m) |

The Cessna (1.1 t) is below both: light aircraft are pushed by hand.

## Key numbers

| | TugSmall | TugLarge |
|---|---|---|
| Origin | under the rear axle, which is where the nose wheel stands in the cradle | same |
| Wheels | wheelbase 3.0 m, track 2.32 m, radius 0.40 m | wheelbase 4.6 m, track 3.42 m, radius 0.60 m |
| Steering | front wheels | front wheels |
| Opening between the legs | 1.24 m | 1.84 m (twin nose wheels up to 1.36 m) |
| Cradle lift (`liftHeight`) | 0.22 m | 0.30 m |
| Work figures | push-back 1.3 m/s, transit 8.3 m/s, turn radius 5 m | push-back 1.1 m/s, transit 7 m/s, turn radius 7.5 m |
| Triangles (LOD0) | 18.1k (body 2.8k faces, cradle 390, 4 wheels) | 18.8k (body 3.0k) |
| LODs | ratios 0.3 / 0.08 | same |
| Textures | `_Albedo` and `_MetalGloss` at 1024², baked at 2048 | same |
| Materials | one per renderer | same |

The towing data is `TOW` in `model.py` → `VehicleType.minTowMass`, `maxTowMass`, `cradle`, `liftHeight`,
`cradleOpening` and `liftPart` (`"Cradle"`). `VehicleType.CanTow(aircraftType)` compares the aircraft's maximum
takeoff mass.

## Look

- Handler blue `#1F4E8E` with a white cab roof.
- Darker blue `#183A66` inside the U, round the arches' kick band and on the deck boxes.
- Yellow-and-black hazard stripes on the bumper, the ends of the legs and the mouth of the U.
- Chequer-plate deck on the legs and across the front of the U.
- Rubber guide rollers at the mouth, a rubbing strip along each side, and amber side reflectors.
- **Cab:** glazed on all sides with dark posts, a door, a step ahead of the front wheel, mirrors on arms and a
  wiper.
- **Hood:** louvred top and side, a sloping front, a filler cap and lifting eyes.
- **Small tug:** a battery / hydraulic box behind the cab and a yellow grab rail.
- **Large tug:**
  - ballast blocks and a hydraulic tank behind the cab;
  - the exhaust out of the hood's side;
  - a longer grab rail;
  - a second beacon on a post on the right leg.
- **Cradle (mover `Cradle`):**
  - a steel platform with rubber grip strips, and clamp pads on chrome rams;
  - a yellow pusher pad in front of the wheel and a yellow gate behind it;
  - lift arms up into the U's closed end.
- **Grime:** tyre dust low down and in crevices, and oil in the cradle pit (`tug.grime`).

## Lights (LIGHTS in model.py)

| Light | Type | Where |
|---|---|---|
| Beacon | Beacon | cab roof (large: also `Beacon_Rear` on the right leg, phase 0.41) |
| Headlight L/R | Head | front panel, either side |
| Tail L/R | Tail | the ends of the legs |
| Work L/R | Work | either side of the U's closed end, aimed back down into the cradle |
| Marker L/R | Marker | amber, on the sides near the back |

## Source and outputs

- **Source:**
  - `ArtSource/Vehicles/Common/tug.py` holds every shared part: `Spec` (the numbers), `sides`, `chassis`, `legs`,
    `front`, `cab`, `hood`, `cradle`, `hazard` (clipped diagonal stripes), `lamps` and `grime`.
  - `ArtSource/Vehicles/TugSmall/model.py` and `TugLarge/model.py` hold each tug's `Spec`, lights, work and tow
    figures, and an `extra()` for its deck details.
- **Outputs** in `Assets/AirCity/Vehicles/<Model>/`:
  - `Models/<Model>.fbx`: `Body_LOD0..2`, `Wheel_FL/FR/RL/RR`, `Cradle`
  - `Textures/`
  - `<Model>.vehicle`
  - `<Model>.prefab` (created once by `VehicleSetup`, then hand-editable)

## Regenerate

```sh
cd ArtSource/Vehicles
B=/Applications/Blender.app/Contents/MacOS/Blender
$B -b --factory-startup --python build.py -- --model TugSmall --previews    # ~1.5 min (TugLarge ~3.5 min)
$B -b --factory-startup --python build.py -- --model TugLarge --geometry --check --views front34,rear34   # quick look
```

Views: `front34`, `rear34`, `side`, `top`.

## Notes

- **The cradle must stay on the rear axle line.** Gameplay puts the tug's origin on the aircraft's nose-gear
  point, and the test `Tugs_TwoSizesWithACradleOnTheRearAxle` guards it.
- **Height:** the small tug's cab roof (1.74 m, 2.0 m to the beacon top) passes under an A320-class nose: the belly
  is 1.93 m up at the nose wheel and 2.4 m a metre from the tip. While it holds the nose wheel, its cab is ahead of
  the nose.
- **Normals check:** `--check` flags 8 (small) / 13 (large) faces. These are false positives where separate solids
  touch (the hazard stripes on their panels, the deck plates); the stored normals point the right way.
- **Not modelled:** a driver, the hydraulic hoses, a towbar (towbarless only).
