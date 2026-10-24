# SnowPlough: runway snow plough

A cab-forward 4×4 airport plough-sweeper, modelled after the multi-tasking runway units airports run in echelon
(Oshkosh HT / M-B MTE style). The user asked for "3-4 snowplows lined up at an angle". The team size is variable;
see [maintenance.md](../systems/maintenance.md).

## Key numbers

| | |
|---|---|
| Size | 6.3 m wide (the blade), 13.6 m long, 4.05 m high |
| Origin | under the rear axle |
| Wheels | wheelbase 6.2 m, track 2.08 m, wheel radius 0.56 m; front wheels steer |
| Blade | 6.1 × 1.3 m, angled 30°: the right end leads, so snow leaves the left end; centred 10.05 m ahead of the rear axle |
| Broom | 6.6 m long, 0.45 m radius, at the same 30°, between the axles; mover `Broom`, 380 rpm while working |
| Work figures | lane 5 m, work 11 m/s (40 km/h), transit 13.9 m/s, turn radius 12 m, echelon gap 16 m (3.2 lanes) |
| Triangles | about 21.6k at LOD0 (body 3.6k faces + 4 wheels + broom); LOD1 ratio 0.3, LOD2 0.08 |
| Textures | `SnowPlough_Albedo` (colour × AO × salt grime) and `SnowPlough_MetalGloss`: 1024², baked at 2048 |
| Materials | one per renderer |

## Look

- Fleet yellow `#F2A30F` with a white cab roof and a black chassis.
- **Body:** louvred engine / blower module, ladder at the rear, red / white chevrons on the rear bumper and the top of
  the rear face.
- **Plough:** curved moldboard with ribs, a steel cutting edge and a rubber deflector; marker rods with orange tips
  at both blade ends.
- **Broom and blower:** a hood over the broom; an air-blast nozzle behind the broom, with a duct from the module.
- **Grime:** salt and slush, strongest low down and in crevices (`grime()` in `model.py`).

## Lights (LIGHTS in model.py → VehicleType.lights)

| Light | Type | Where |
|---|---|---|
| PloughLight L/R | Head | above the roof front, 3.78 m (the blade hides the bumper headlights) |
| Headlight L/R | Head | in the bumper |
| Tail L/R, TailHigh L/R | Tail | rear bumper, and high on the rear face |
| Beacon L/R | Beacon | rear corners of the roof, phases 0 and 0.37 |
| Work_BladeL/R, Work_BroomL/R | Work | roof edges, aimed at the blade ends and at the broom |
| Marker_BladeL/R | Marker | amber, on the tips of the blade-end rods |

## Source and outputs

- **Source:** `ArtSource/Vehicles/SnowPlough/model.py`, which contains everything: geometry functions `chassis`,
  `cab`, `module`, `rear`, `plough`, `broom`, `air_blast`, `lamps`. Helpers are in `Common/kit.py`; the pipeline is
  `Common/vpipe.py`.
- **Outputs** in `Assets/AirCity/Vehicles/SnowPlough/`:
  - `Models/SnowPlough.fbx`: `Body_LOD0..2`, `Wheel_FL/FR/RL/RR`, `Broom`
  - `Textures/`
  - `SnowPlough.vehicle`
  - `SnowPlough.prefab` (created once by `VehicleSetup`, then hand-editable)

## Regenerate

```sh
cd ArtSource/Vehicles
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build.py -- --model SnowPlough --previews
# quick geometry check: --geometry --check --views front34,side,top     (previews in SnowPlough/Build/previews/)
```

Then in Unity, from the repo root (prefabs already exist and are kept; delete the prefab to recreate it):

```sh
U=/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity
$U -batchmode -quit -projectPath . -executeMethod AirCity.EditorTools.Airfield.AirfieldSetup.Bootstrap -logFile Logs/airfield.log
```

## Notes

- `--check` reports 2 Body faces whose winding disagrees with bmesh's outward guess. These are single-sided quads,
  such as glass panes and chevrons, which bmesh can't orient. They face the right way in renders; recheck if you
  change those parts.
- Possible later additions: a rotary blower team member (not wanted for now), the snow windrow along the
  cleared edge, and blade angling (both sides).
