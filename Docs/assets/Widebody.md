# Widebody — Widebody twinjet

A long-haul twinjet with Airbus A350-900 proportions and A350-900 airport-planning performance. It has a long, full
nose with a six-pane flight deck, curved blended winglets, Trent XWB-sized engines and four-wheel main bogies.
`CATEGORY = "Airliner"`, `DISPLAY_NAME = "Widebody twinjet"`. ICAO aerodrome code **E**, paved runways only. The
pipeline is the shared one in `Docs/systems/aircraft.md`; this page covers only what is specific to this type.

## Key numbers

From `Assets/AirCity/Aircraft/Widebody/Widebody.aircraft` and `ArtSource/Aircraft/Widebody/*.py`.

| What | Value | Source |
|---|---|---|
| Wingspan × length × height | 64.68 × 66.88 × 17.03 m (A350-900: 64.75 × 66.8 × 17.05) | `Dimensions_MatchTheRealAircraftClass` log |
| Origin | ground under the middle of the main bogies; `S_REF` = 34.8 (stations `s` are metres aft of the nose tip) | `parts.py` |
| Fuselage | 5.96 m wide × 6.09 m tall, keel 2.40 m above the ground; constant section to s 44; wing-to-body fairing s 22–44 (lower, wider, boxier belly) | `parts.py` |
| Wheelbase / min turn | 28.70 m (A350-900: 28.7); 70° nose-wheel steering → 10.4 m to the main-gear midpoint | `GEAR`, test log |
| Main gear | track 10.6 m; four-wheel bogies, axles ±1.0 m, wheels ±0.72 m from the leg, tyres 1.28 m | `MAIN_*`, `BOGIE_DS` |
| Wing | LE s 25.4 at the fuselage side, 34° sweep; TE kink at y 10.4; tip y 30.0 + winglets; dihedral 6°; washout 2.5° → −1°; hinge 0.76 chord | `parts.py` |
| Winglets | the tip section bends up through a 1.65 m-radius arc to 72°, then a 2.45 m blade, swept back 2.55 m, tapering to 0.62 m chord | `build_winglets` |
| Engines | at x ±9.70, z 2.65, inlet s 23.6; the Narrowbody's nacelle scaled by `K = 1.62` (3.4 m across, 6.1 m long); 22-blade fan; NACA 0010 pylon | `build_engine` |
| Tail | Stabiliser z 6.35, root LE s 55.4, 34° sweep, half-span 9.6, dihedral 5°. Fin z 8.0 → 17.0, 40° sweep, chord 10.0 → 3.4. Hinges at 0.70 chord. Dorsal fillet s 45.5–51.75 | `parts.py` |
| Triangles / draws | LOD0 64,735 / 21 · LOD1 13,196 / 2 · LOD2 3,298 / 2 | `Lods_CutTrianglesAndDrawCalls` log, 2026-10-29 |
| LOD settings | `LOD_RATIOS {1: 0.2, 2: 0.05}`; `LOD1_KEEP []`; `COLLIDER_RATIO 0.3` | `model.py` |
| Texel density | 30.1 px/m (atlas: the same 2048² as the Narrowbody's on ~3× the surface); ~82 px/m on the registration patch | `texelsPerMetre`, `REG_TEX_W` |
| Textures | atlas maps 2048²; Dirt 1024²; LogoUV 512²; GroundShadow 256²; Registration_Default 1024×772 (~0.5 MB per aircraft as BC1); RegistrationDirt 512×386 | files in `Textures/` |
| AO distance | 4.0 m | `AO_DISTANCE` |
| Performance | takeoff 2,700 m, landing 2,000 m, MTOW 283 t; taxi 10, VR 78, VREF 72 m/s; climb 11.2 m/s at 87.5 m/s | `PERFORMANCE` |

A fleet of 100 widebodies in 10 liveries uses ~77 MB of GPU texture memory (bodies 26.7 MB, registrations 50.3 MB),
inside the 80 MB budget in `Fleet_100Aircraft_10Liveries_StaysWithinBudget`.

## Outputs

| File | What |
|---|---|
| `ArtSource/Aircraft/Widebody/parts.py` | Constants and the builders: `build_fuselage`, `build_windows` (`COCKPIT_WINDOWS`), `build_wing` + `TE_PIECES`, `build_winglets`, `build_engine`, `build_htail`, `build_vtail` (+ dorsal fillet), `build_gear`, `build_details` (lights, antennas, satcom fairing, pitot probes) |
| `ArtSource/Aircraft/Widebody/model.py` | `build()`, UV rules, `SCHEMES`, `REG_BOX`, `LOGO_BOX`, painted windows / doors, grime, `GEAR`, `POINTS`, `LIGHTS`, `PERFORMANCE`, `VIEWS` |
| `Assets/AirCity/Aircraft/Widebody/Models/Widebody.fbx` | LOD0–2, movers, collider, ground shadow |
| `…/Widebody.aircraft` | Type data (imported as `AircraftType`) |
| `…/Widebody.prefab` | `AircraftSetup`, hand-editable |
| `…/Thumbnails/Widebody_<Livery>.png` | Menu thumbnails (6 liveries) |

## Parts

| Mover | Driven by | Notes |
|---|---|---|
| `Fan_L`, `Fan_R` | `PropellerSpinner` | Spinner + 22 blades; pivot at inlet + 1.3 m. Not kept at LOD1 |
| `Rudder` | `AircraftControlSurfaces.rudder` | `hinge` along the fin hinge line, z 8.35–16.6, `axis="z"` |
| `Elevator_L`, `Elevator_R` | `.elevator` | Swept hinge, y 1.95–9.32 |
| `Flap_L_In`, `Flap_L_Out`, `Flap_R_In`, `Flap_R_Out` | `.flaps` | Span 3.05–10.30 (inboard flap ends at the kink) and 10.50–21.40; fixed fillers between |
| `Aileron_L`, `Aileron_R` | `.aileron` | Span 21.60–28.60 |
| `NoseGear` | `AircraftGroundMotion` steering | |
| `NoseGear/Wheel_Nose_L`, `Wheel_Nose_R` | wheel spin | Exported as `NoseGear__Wheel_Nose_*` |
| `Wheel_L1`, `Wheel_L2`, `Wheel_R1`, `Wheel_R2` | wheel spin | **One per bogie axle** (1 = front, 2 = rear): both wheels on an axle roll together, so they are one part. The bogie beam, axles and pitch damper are fixed (`Gear`) |

**Lights** (13), as on the Narrowbody: red / green nav lights in the winglet roots' leading edge (`NAV_S`) with
strobes 0.42 m behind, white tail nav and strobe on the tail cone, red beacons on the crown (s 31) and belly (s 38),
landing lights under the wing roots (y 4.6, s 28.6), a taxi light on the nose-gear strut, and logo lights on the
stabiliser (y 3.4, s 59.4) aimed at the fin.

**Service points:**

| Point | Where |
|---|---|
| `Door_L1`–`Door_L4`, `Door_R1`–`Door_R4` | s 8.4 / 19.2 / 43.4 / 55.0, sill z 4.70 (cabin floor; jet bridges reach ~4.7 m for an A350), 1.07 × 1.93 m |
| `Cargo_Fwd`, `Cargo_Aft` | Starboard (+X), s 14.6 / 46.0, 2.70 × 1.70 m |
| `Cargo_Bulk` | Starboard, s 50.2, 0.95 × 0.95 m |
| `Fuel` | Starboard wing underside at 13.5 m span |
| `GPU` | under the nose, s 4.6 |
| `Water` / `Lav` | aft belly, s 49.5 port / s 52.5 starboard |
| `TowBar`, `Chock_Nose`, `Chock_L`, `Chock_R` | nose axle and the front bogie axles, on the ground |

## Liveries

| Style | Widebody: R / G / B |
|---|---|
| Classic | Cheatline through the windows (R) sweeping up the fin ~1.2 m behind its leading edge and out past the tip, pinstripe below (G), painted engines (B) |
| Swoosh | Belly-to-tail swoosh from the wing root at s 25 (R + edge G); whole fin and nacelles in colour 3 |
| TwoTone | Grey belly under a cheatline, painted tail and engines |
| Trainer | Charter / "eurowhite": nose cone + cowls (R), winglets + fin tip (G), window-line stripe (B) |

`REG_BOX` on the rear fuselage: Classic (46.5, 51.5, −1.75, −1.20), Swoosh (40.5, 45.5, 1.55, 2.10), TwoTone and
Trainer (47.0, 52.0, 0.75, 1.30). `LOGO_BOX` = (56.4, 61.2, 9.8, 15.2) on the fin.

## Regenerate

```sh
cd ArtSource/Aircraft
B=/Applications/Blender.app/Contents/MacOS/Blender
$B -b --factory-startup --python build.py -- --model Widebody --preview --cull --views side_ortho,front34,nose_34   # geometry
$B -b --factory-startup --python build.py -- --model Widebody --texture --check      # after geometry / UV / mover changes (~3 min)
$B -b --factory-startup --python finish.py -- --model Widebody --previews --liveries ClassicBlue,SunsetRed --views front34,rear34,photo
```

`--check` reports the inner nacelle surfaces (fan duct, core) as on the Narrowbody, and also the dorsal fillet: a
false positive. The fillet is an open shell whose winding matches the Narrowbody's, and it renders with back faces
culled. Views (`VIEWS`): `front34`, `rear34`, `side`, `top`, `front`, `nose_close`, `nose_side`, `nose_front`,
`nose_34`, `photo`, `tail_close`, `engine_close`, `engine_rear`, `gear_close`, `wingtip`, `dorsal_close`, `game`, and
`side_ortho` / `top_ortho` / `front_ortho`.

## Design notes

- **Nose.** Same `_rise` profile family as the Narrowbody, but longer and fuller (`NOSE_UP` 13 m, `NOSE_LO` 10 m,
  `NOSE_W` 10.5 m) with the radome tip at a third of the fuselage height.
- **Flight deck.** Six panes, three per side: a big windshield pane beside a thin centre post, a raked post, a near-
  rectangular sliding window, and a smaller rear side window whose top edge drops aft. The band runs from `SILL` 3.50
  to `HEAD` 4.52 m above the keel; the windshield's stations come from where the nose top line reaches those heights
  (`_top_reaches`), so the glazing follows the nose if its profile changes.
- **Windows and doors.** Passenger windows (0.635 m pitch, gapped at the doors) and door outlines are painted into
  the detail map, as on the Narrowbody.
- **Grime** (`dirt_extra`): exhaust staining on the wing undersides behind the engines, grimy belly between s 22 and
  42.
- **Sandbox.** `CreateSandboxScene` parks one row of three widebodies (every other livery) so they fit on the apron.
  The sandbox airport's one code E stand (x 343) can take a widebody.
