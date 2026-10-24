# Narrowbody — Narrowbody twinjet

A short/medium-haul twinjet with Airbus A320-family proportions and A320-200 airport-planning performance. The
nose and flight-deck glazing are styled after the Airbus A220. `CATEGORY = "Airliner"`, `DISPLAY_NAME = "Narrowbody
twinjet"`. ICAO aerodrome code **C** (span < 36 m). Paved runways only. Built by the shared pipeline; see
`Docs/systems/aircraft.md`.

## Key numbers

All numbers come from `Assets/AirCity/Aircraft/Narrowbody/Narrowbody.aircraft`,
`ArtSource/Aircraft/Narrowbody/*.py` and the EditMode test log.

| Item | Value | Source / note |
|---|---|---|
| Wingspan × length × height | 35.83 × 37.64 × 11.58 m | `bounds` (A320 with sharklets 35.8 × 37.57 × 11.76; test ranges 34–36, 36.5–38.5, 11–12.5) |
| Bounds (Unity) | min (−17.913, −0.03, −19.9), max (17.913, 11.55, 17.74) | origin under the main axles, `S_REF` = 17.74 |
| Fuselage | 3.95 m wide (`HALF_WIDTH` 1.975, 2.05 at the wing fairing), keel z 1.91 → crown 6.09, tail end S1 37.5 | `parts.py` |
| Wheelbase | 12.64 m (A320: 12.64) | `steeringPivot.z`, `NOSE_S` 5.1 |
| Main track / wheels | legs at x ±3.8 (track 7.6 m), twin wheels at ±3.35 / ±4.25, r 0.585 m; nose twin at ±0.27, r 0.38 m | `parts.py` |
| Max steering / min turn radius | 75° / 3.4 m (to the main-gear midpoint) | `GEAR`, `MinTurnRadius` |
| MTOW | 78,000 kg | `PERFORMANCE` |
| Takeoff / landing distance | 2,100 m / 1,500 m (A320-200 field lengths, max weight, sea level, ISA) | `RunwayLength` = 2,100 m |
| Unpaved runways | no ("Jets need a paved runway") | `PERFORMANCE` |
| Wing | NACA 2415; LE sweep 27.5° from s 13.2 at the side (y 2.0); unswept TE inboard of the kink at y 6.4 (s 20.6 → 22.6 at the tip); tip y 16.85 + sharklets; dihedral 5.1°; washout 2.5° → −0.5°; hinge 0.76 chord | `parts.py` |
| Engines | at x ±5.75, z 1.62, inlet s 10.9; nacelle max radius 1.055 m; 18-blade fan; core cowl, nozzle, plug; NACA 0010 pylon | `build_engine` |
| Tail | Stabiliser z 4.72, root LE s 31.6, 32° sweep, half-span 6.2, dihedral 6°. Fin z 5.9 → 11.55, 37° sweep, chord 5.6 → 1.95. Hinges at 0.70 chord. Dorsal fillet s 27.6–30.6 | `parts.py` |
| Triangles / draws | LOD0 46,027 / 21 · LOD1 10,399 / 2 · LOD2 2,836 / 2 | `Lods_CutTrianglesAndDrawCalls` log, 2026-10-23 |
| LOD settings | `LOD_RATIOS {1: 0.22, 2: 0.06}`; `LOD1_KEEP []` ("fans are invisible behind the intake lip at LOD1 sizes"); `COLLIDER_RATIO 0.3` | `model.py` |
| Texel density | 53.4 px/m (atlas); 85 px/m on the registration patch | `texelsPerMetre`, `REG_TEX_W` comment |
| Textures | atlas maps 2048² (Body_Default, Detail, Mask ×4, UV_Template); Dirt 1024²; LogoUV 512²; GroundShadow 256²; Registration_Default 768×536 (~0.28 MB per aircraft as BC1); RegistrationDirt 384×268 | files in `Textures/` |
| Materials per renderer | `Body_LOD0`: Body + Registration + Trim; `Body_LOD1/2`: Body + Trim; each mover 1 | test log |
| AO distance | 2.5 m | `AO_DISTANCE` |

## Source and outputs

| File | Contents |
|---|---|
| `ArtSource/Aircraft/Narrowbody/parts.py` | Constants `S_REF`, `KEEL`, `CROWN`, `HALF_WIDTH`, `TIP_H`; the A220 nose profiles (`NOSE_UP`, `NOSE_LO`, `NOSE_W`, `_rise`); `Fuselage`; and the builders: `build_fuselage` (rounded nose cap, tail cone, APU exhaust), `build_windows` (`COCKPIT_WINDOWS`), `build_wing` + `TE_PIECES`, `build_sharklets`, `build_engine`, `build_htail`, `build_vtail` (+ dorsal fillet), `build_gear`, `build_details` (lights, antennas, pitot probes) |
| `ArtSource/Aircraft/Narrowbody/model.py` | `build()` and the movers, LOD / UV settings, four `SCHEMES`, the radome joint (`RADOME_S`, `_radome`), `CHEAT_Z`, `DOOR_1_S`, painted `WINDOWS` / `_doors`, `REG_*`, `LOGO_BOX`, `GEAR`, `PERFORMANCE`, `POINTS`, `LIGHTS`, `VIEWS` |
| `Assets/AirCity/Aircraft/Narrowbody/Models/Narrowbody.fbx` | Generated model |
| `…/Textures/Narrowbody_*.png` | `Body_Default`, `Registration_Default`, `Detail`, `Dirt`, `RegistrationDirt`, `LogoUV`, `GroundShadow`, `Mask_{Classic,Swoosh,TwoTone,Trainer}`, `UV_Template` |
| `…/Narrowbody.aircraft` | Type data (imported as `AircraftType`) |
| `…/Narrowbody.prefab` | Created once by `AircraftSetup`, hand-editable |
| `…/Thumbnails/Narrowbody_<Livery>.png` | Menu thumbnails (6 liveries) |

## Parts and movers

| Mover (Unity name) | Driven by | Notes |
|---|---|---|
| `Fan_L`, `Fan_R` | `PropellerSpinner` | Spinner cone + 18 blades; pivot at inlet + 0.8 m. Excluded from the outline. Not kept at LOD1 |
| `Rudder` | `AircraftControlSurfaces.rudder` | `hinge` along the fin hinge line, `axis="z"` (Unity local Y) |
| `Elevator_L`, `Elevator_R` | `.elevator` | `hinge` along the swept stabiliser hinge, y 1.0–5.97 |
| `Flap_L_In`, `Flap_L_Out`, `Flap_R_In`, `Flap_R_Out` | `.flaps` | Span 2.06–6.34 and 6.46–12.54, swept hinges; fixed fillers between pieces |
| `Aileron_L`, `Aileron_R` | `.aileron` | Span 12.66–16.21 |
| `NoseGear` | `AircraftGroundMotion` steering | Lower strut, axle, torque link and the taxi-light housing (so it steers) |
| `NoseGear/Wheel_Nose_L`, `Wheel_Nose_R` | wheel spin | Exported as `NoseGear__Wheel_Nose_*` |
| `Wheel_L1`, `Wheel_L2`, `Wheel_R1`, `Wheel_R2` | wheel spin | 1 = inboard, 2 = outboard; the legs and braces are fixed (`Gear`) |

**Lights** (13):

| Name | Type | Where | Spread |
|---|---|---|---|
| `Nav_L` red / `Nav_R` green | Navigation | Wingtip leading edges (`NAV_S`) | 110° |
| `Nav_Tail` white | Navigation | Tail cone, z 4.50 | 140° |
| `Strobe_L` / `Strobe_R` | Strobe | Wingtips, 0.32 m behind the nav lights | 360° |
| `Strobe_Tail` | Strobe | Tail cone, z 4.62, aimed aft | 360° |
| `Beacon_Top` / `Beacon_Bottom` red | Beacon | Crown at s 17.0 / belly at s 19.5 | 360° |
| `Landing_L` / `Landing_R` | Landing | Under the wing roots, y ±3.0, s 14.4, aimed forward and slightly down | 25° |
| `Taxi` | Taxi | Nose-gear strut, z 1.15 | 50° |
| `Logo_L` / `Logo_R` | Logo | On the stabiliser at y ±2.2, s 33.6, aimed at the fin | 35° |

**Service points** (14; Unity side in brackets):

| Points | Notes |
|---|---|
| `Door_L1`, `Door_R1` | s 5.6 (`DOOR_1_S`), sill z 2.62, 0.81 × 1.85 m |
| `Door_L2`, `Door_R2` | s 30.95, sill z 2.72 |
| `Cargo_Fwd`, `Cargo_Aft` | Starboard (+X), s 11.3 / 23.2, 1.8 × 1.24 m |
| `Fuel` | Starboard wing underside at 8 m span |
| `GPU` (GroundPower) | Starboard under the nose, s 3.9 |
| `Water` | Port (−X), aft belly, s 26.5 |
| `Lav` (Lavatory) | Starboard, aft belly, s 29.0 |
| `TowBar`, `Chock_Nose`, `Chock_L`, `Chock_R` | Nose axle and the gear ground points |

Passenger boarding is on the left and cargo on the right (`ServicePoints_AreOnTheCorrectSide`).

## Liveries

The type implements all four styles. The stripes stop at the radome joint (`_radome`), which leans forward at the
top so it passes just ahead of the windshield. How liveries work in general: `Docs/systems/liveries.md`.

| Style | Look (from the docstrings) |
|---|---|
| Classic | Cheatline through the windows (R) sweeping up into the fin, pinstripe below (G), painted engines (B) |
| Swoosh | Belly-to-tail swoosh growing from the wing root at s 13 (R + G edge); whole fin and nacelles in colour 3 |
| TwoTone | "Airline standard": belly colour under a cheatline, painted tail and engines |
| Trainer | Charter / "eurowhite": coloured nose cone and engine cowls (R), sharklets and fin top (G), window-line stripe (B) |

- `REG_BOX` sits on the rear fuselage below the fin: Classic (26.6, 29.5, −1.02, −0.67), Swoosh (24.5, 27.4, 1.15,
  1.50), and TwoTone / Trainer (26.6, 29.5, 0.30, 0.65). Patch window: `REG_FACES_U (23.9, 32.9)`,
  `REG_FACES_V (−1.2, 1.7)`. `REG_TEX_W 768` gives sharp letters at 0.28 MB per aircraft.
- `LOGO_BOX (31.4, 35.2, 6.6, 10.3)` puts the logo on the fin. In `Logo_IsPaintedOnTheFin` it covers 0.67% of the
  atlas.

## Regenerate

```sh
cd ArtSource/Aircraft
B=/Applications/Blender.app/Contents/MacOS/Blender
$B -b --factory-startup --python build.py -- --model Narrowbody --preview --cull --views side_ortho,nose_side,nose_34   # geometry only
$B -b --factory-startup --python build.py -- --model Narrowbody --texture --check     # after geometry / UV / mover changes
$B -b --factory-startup --python finish.py -- --model Narrowbody --previews --liveries ClassicBlue,SunsetRed --views front34,rear34,nose_34,photo
```

`--check` reports the inner nacelle surfaces (intake, fan face, fan duct); that is expected. Views (`VIEWS`):
`front34`, `rear34`, `side`, `top`, `front`, `nose_close`, `nose_side` (ortho, for comparing with side-on photos),
`nose_front`, `nose_34`, `photo` ("low, front-left, like a spotter's photo"), `tail_close`, `engine_close`,
`engine_rear`, `dorsal_close`, `game`, and `side_ortho` / `top_ortho` / `front_ortho`.

Then, from the repo root, with the editor closed:

```sh
U=/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity
$U -batchmode -quit -projectPath . -executeMethod AirCity.EditorTools.Aircraft.AircraftSetup.Bootstrap -logFile Logs/bootstrap.log
$U -batchmode -projectPath . -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode.xml" -logFile Logs/tests.log
```

If mover names, pivots or hinges changed, the existing prefab's cached parts are stale; see Invariants in
`Docs/systems/aircraft.md`.

## Look notes and history

- **The nose took several rounds.** The user found the first jet nose "awful". After three rounds, the front was
  made to look like an Airbus A220: a pointed nose and four cockpit windows. The nose profile was fitted to side-on
  A220 photos from Wikimedia Commons, and fitting to real photos worked better than guessing curves. The user judges
  looks critically: check close-up renders (`nose_side`, `nose_34`, `photo`) against real reference photos before
  showing art.
- How the fit is encoded:
  - Each nose profile rises from the tip as `1 − (1 − t^a)^b`, `t = s / length`. `a < 1` gives the pointed tip and
    `b` sets how soon the line levels off.
  - `NOSE_UP (9.4, 0.97, 3.45)`: the top line climbs almost straight to the windshield and reaches the crown about two
    fuselage heights back. `NOSE_LO (8.5, 0.69, 3.44)` is the lower line and `NOSE_W (8.0, 0.80, 3.00)` the plan view.
  - The tip height `TIP_H = 1.17` is 28% of the fuselage height, as on the A220.
- Flight deck glazing (`COCKPIT_WINDOWS`, port side, mirrored to starboard): four windows in a level band from
  `SILL` 2.40 to `HEAD` 3.22 m above the keel. Each side has a wrap-around windshield pane beside a thin centre post,
  a raked post, then one big side window with well-rounded rear corners. The posts are about 0.1 m wide.
- L1 / R1 doors sit at s 5.6, "well behind the flight deck as on the A220".
- Passenger windows (26 per side at 0.762 m pitch, with a gap at the overwing exits) and the door outlines are
  painted into the detail map, not modelled: "at airport-camera distances that reads the same and costs nothing". The
  cockpit windows are real geometry.
- Grime (`dirt_extra`): exhaust staining on the wing undersides behind the engines, and a grimy belly between s 12
  and 22.
- `Build/previews/` (gitignored) still holds the iteration renders (`pointy_*`, `a220_*`, `nose_montage.png`,
  `a220_photos.png`), which are useful as a before/after reference.

## Open issues / ideas

- The L1 sill is at 2.62 m. `ServicePoints_AreOnTheCorrectSide` accepts 2.5–3.6 m, but its comment says jet bridges
  need ~3.4 m. Check this before building jet bridges against it.
