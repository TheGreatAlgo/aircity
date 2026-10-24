# Cessna172 — Light single

A Cessna 172 Skyhawk-style four-seat, high-wing, fixed-gear single-engine prop. The performance numbers are C172S
POH figures. `CATEGORY = "GeneralAviation"`, `DISPLAY_NAME = "Light single"`. ICAO aerodrome code **A** (span
< 15 m). It is the only type with `unpavedRunways = true`, so it can use dirt and grass strips. Built by the shared
pipeline; see `Docs/systems/aircraft.md`.

## Key numbers

All numbers come from `Assets/AirCity/Aircraft/Cessna172/Cessna172.aircraft`, `ArtSource/Aircraft/Cessna172/*.py`
and the EditMode test log.

| Item | Value | Source / note |
|---|---|---|
| Wingspan × length × height | 11.06 × 8.27 × 2.73 m | `bounds` (real C172: 11.0 × 8.28 m; test ranges 10.5–11.5, 7.9–8.6) |
| Bounds (Unity) | min (−5.531, −0.01, −5.546), max (5.531, 2.72, 2.72) | origin on the ground under the main axle |
| Wheelbase | 1.64 m | `steeringPivot.z` (`NOSE_PIVOT` s = 1.08, `S_REF` = 2.72) |
| Main track / wheel radii | 2.54 m (`MAIN_X` ±1.27) / main 0.222 m, nose 0.19 m | `parts.py` |
| Max steering / min turn radius | 30° ("rudder-pedal steering plus differential braking") / 2.8 m | `GEAR`, `MinTurnRadius` |
| MTOW | 1,157 kg | `PERFORMANCE` |
| Takeoff / landing distance | 497 m / 407 m (over 50 ft, max weight, sea level, 15 °C) | `PERFORMANCE`; `RunwayLength` = 497 m |
| Unpaved runways | yes | `PERFORMANCE["unpaved"]` |
| Wing | NACA 2412, LE station 2.13, root z 2.06, tip y 5.50, chord 1.63 → 1.13 m outboard of y 2.90, dihedral 1.73°, washout +1.5° → −1.5°, hinge at 0.735 chord | `parts.py` |
| Propeller | 2 blades, NACA 4408, radius 0.95 m, axis z 1.22, pitch 1.37 m; 0.40 m ogive spinner | `build_propeller` |
| Triangles / draws | LOD0 37,283 / 17 · LOD1 9,729 / 4 · LOD2 2,676 / 2 | `Lods_CutTrianglesAndDrawCalls` log, 2026-10-23 |
| LOD settings | `LOD_RATIOS {1: 0.25, 2: 0.07}`, `LOD1_KEEP ["Propeller"]` (low-poly prop at 0.3), `COLLIDER_RATIO 0.3` | `model.py` |
| Texel density | 205.7 px/m on the fuselage | `texelsPerMetre` |
| Textures | atlas maps 2048² (Body_Default, Detail, Mask ×4, UV_Template); Dirt 1024²; LogoUV 512²; GroundShadow 256²; Registration_Default 1024×432; RegistrationDirt 512×216 | files in `Textures/` |
| Materials per renderer | `Body_LOD0`: Body + Registration + Trim (3 submeshes); `Body_LOD1/2`: Body + Trim; movers 1–2 | test log |
| AO distance | 0.9 m | `AO_DISTANCE` |

## Source and outputs

| File | Contents |
|---|---|
| `ArtSource/Aircraft/Cessna172/parts.py` | `S_REF = 2.72` and `THRUST_Z = 1.22`; the `Fuselage` profiles (S0 0.40 cowl front → S1 7.92 tail); and the builders: `build_fuselage` (cowl lip rolled into the spinner opening, dark bulkhead, tail fan cap), `build_windows` (windshield, Omni-vision rear window, two side windows per side), `build_wing` (rounded drooping tips; Flap / filler / Aileron pieces), `build_htail`, `build_vtail` (fin cap, dorsal fillet, rudder with horn balance and the white tail light), `build_propeller`, `build_gear` (spring-steel legs, wheel pants), `build_struts`, `build_details` |
| `ArtSource/Aircraft/Cessna172/model.py` | `build()` and the movers, LOD / UV settings, four `SCHEMES`, `REG_*`, `LOGO_BOX`, `panel_lines`, `dirt_extra`, `GEAR`, `PERFORMANCE`, `POINTS`, `LIGHTS`, `VIEWS` |
| `Assets/AirCity/Aircraft/Cessna172/Models/Cessna172.fbx` | Generated model |
| `…/Textures/Cessna172_*.png` | `Body_Default`, `Registration_Default`, `Detail`, `Dirt`, `RegistrationDirt`, `LogoUV`, `GroundShadow`, `Mask_{Classic,Swoosh,TwoTone,Trainer}`, `UV_Template` |
| `…/Cessna172.aircraft` | Type data (imported as `AircraftType`) |
| `…/Cessna172.prefab` | Created once by `AircraftSetup`, hand-editable |
| `…/Thumbnails/Cessna172_<Livery>.png` | Menu thumbnails (6 liveries) |

## Parts and movers

| Mover (Unity name) | Driven by | Notes |
|---|---|---|
| `Propeller` (+ child `Propeller_LOD1`) | `PropellerSpinner` | Pivot at the hub; the LOD1 copy is kept so the prop still spins at LOD1 |
| `Rudder` | `AircraftControlSurfaces.rudder` | Pivot (0, Y(7.80), 2.0); axis-aligned, no `hinge` vector |
| `Elevator` | `.elevator` | One mover for both sides; pivot at `HT_HINGE_S` 7.53, z 1.44 |
| `Flap_L`, `Flap_R` | `.flaps` | Span 0.60–2.92 m |
| `Aileron_L`, `Aileron_R` | `.aileron` | Span 3.065–5.145 m; a fixed filler sits between flap and aileron |
| `NoseGear` | `AircraftGroundMotion` steering | Lower strut, torque link, nose pant; pivot at `NOSE_PIVOT` |
| `NoseGear/Wheel_Nose` | wheel spin | Exported as `NoseGear__Wheel_Nose` and re-nested on import |
| `Wheel_L`, `Wheel_R` | wheel spin | Main wheels; the pants are fixed (`Gear` in the body) |

**Lights** (8; no logo light):

| Name | Type | Where | Spread |
|---|---|---|---|
| `Nav_L` red / `Nav_R` green | Navigation | Wingtips, aimed 55° off the nose | 110° |
| `Nav_Tail` white | Navigation | Rudder trailing edge (s 8.20, z 1.33) | 140° |
| `Strobe_L` / `Strobe_R` | Strobe | Wingtips, just behind the nav lights | 360° |
| `Beacon` red | Beacon | Fin cap (s 7.40, z 2.69) | 360° |
| `Landing` | Landing | Port wing leading edge, y 1.55 | 30° |
| `Taxi` | Taxi | Port wing leading edge, y 1.72 | 60° |

**Service points** (12):

| Points | Notes |
|---|---|
| `Door_L`, `Door_R` | Cabin doors, sill at z 0.83, 0.94 × 1.24 m |
| `Baggage` (CargoDoor) | Port side, 0.38 × 0.36 m |
| `Fuel_L`, `Fuel_R` | Wing-top caps at y ±1.55 (test: y > 1.9 m) |
| `TowBar` | Nose axle |
| `Chock_Nose`, `Chock_L`, `Chock_R` | Ground points under the axles |
| `TieDown_L`, `TieDown_R` | Under the wing at ±3.6 m |
| `TieDown_Tail` | Under the tail, s 7.55 |

## Liveries

The type implements all four styles. Each scheme returns R / G / B zone masks; `_accents` is the shared trim set:
spinner, pants, wingtips, stab tips, fin cap. How liveries work in general: `Docs/systems/liveries.md`.

| Style | Look (from the docstrings) |
|---|---|
| Classic | 1970s Skyhawk cheat stripe from the nose sweeping up the tail and fin, a pinstripe above, trim on the accents |
| Swoosh | 2000s Skyhawk SP swoosh growing out of the cabin and up the fin |
| TwoTone | Belly colour under a cheatline (pants and legs included), painted tail, spinner, wingtips |
| Trainer | Flight-school look: painted cowl and spinner, coloured tips, thin window-line stripe |

- `REG_BOX` (station from/to, arc from/to, in metres) sits on the tailcone between stations 4.30 and 6.15 and is kept
  clear of each scheme's stripes. Patch window: `REG_FACES_U (4.18, 6.40)`, `REG_FACES_V (−0.13, 0.27)`,
  `REG_TEX_W 1024`.
- `LOGO_BOX (7.08, 7.98, 1.68, 2.42)` puts the logo on both sides of the fin. In `Logo_IsPaintedOnTheFin` it covers
  0.33% of the atlas.

## Regenerate

```sh
cd ArtSource/Aircraft
B=/Applications/Blender.app/Contents/MacOS/Blender
$B -b --factory-startup --python build.py -- --model Cessna172 --texture --check      # only after geometry / UV / mover changes
$B -b --factory-startup --python finish.py -- --model Cessna172 --previews --liveries ClassicBlue,SunsetRed --views front34,rear34,tail_close
```

Useful views (`VIEWS`): `front34`, `side`, `rear34`, `top`, `front`, `nose_close`, `cabin_close`, `tail_close`,
`game` (airport-camera angle), and `side_ortho` / `top_ortho` / `front_ortho`.

Then, from the repo root, with the editor closed:

```sh
U=/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity
$U -batchmode -quit -projectPath . -executeMethod AirCity.EditorTools.Aircraft.AircraftSetup.Bootstrap -logFile Logs/bootstrap.log
$U -batchmode -projectPath . -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode.xml" -logFile Logs/tests.log
```

If mover names or pivots changed, the existing prefab's cached parts are stale; see Invariants in
`Docs/systems/aircraft.md`.

## Look notes and history

- The cowl-nose landing light (older 172 style) was skipped: the landing and taxi lights are in the port wing
  leading edge (`build_details` comment).
- A dark bulkhead sits behind the spinner "so nothing can be seen through into the open fuselage". The cowling air
  inlets are dark shells either side of the spinner.
- The wingtips have conical-camber droop and plan-form rounding, and the wing has 3° of washout.
- All windows are real geometry (`drape_pane` on the skin): the windshield with its corners spread back at the
  A-pillars, the Omni-vision rear window, and the door and rear side windows. The Narrowbody instead paints its cabin
  windows.
- Panel lines include the firewall / cowl joint, the upper / lower cowl split, the oil door, cabin doors with
  handles, the port baggage door, tailcone skin joints, wing skin joints and the fin root fairing.
- Grime (`dirt_extra`): exhaust soot along the starboard belly behind the exhaust stack, an oil streak under the
  cowl, and grimy wheel pants.
- `Build/previews/` (gitignored) holds earlier `lod1_*` / `lod2_*` and livery renders used to review the far LODs.
