# DoubleDeck — Double-deck quadjet

A generic long-haul four-engine airliner with a full-length double-deck fuselage: a tall oval section, a blunt
rounded nose with the flight deck between the two decks, a large swept wing with small wingtip fences, four big
turbofans, a huge fin, a nose gear, two four-wheel wing gears and two six-wheel body gears. It carries no real
airline or manufacturer branding. `CATEGORY = "Airliner"`, `DISPLAY_NAME = "Double-deck quadjet"`. ICAO aerodrome
code **F**, paved runways only. The pipeline is the shared one in `Docs/systems/aircraft.md`; this page covers only
what is specific to this type.

## Key numbers

From `Assets/AirCity/Aircraft/DoubleDeck/DoubleDeck.aircraft` and `ArtSource/Aircraft/DoubleDeck/*.py`. The
profiles were traced from the general-dimensions side, plan and front views of the reference document and checked
by overlaying it on orthographic renders.

| What | Value | Source |
|---|---|---|
| Wingspan × length × height | 79.72 × 72.72 × 24.20 m | `Dimensions_MatchTheRealAircraftClass` log; A380-800: 79.75 × 72.73 × 24.17 m (Airbus "A380 Aircraft Characteristics – Airport and Maintenance Planning", rev. Dec 2025, 2-2-0 and 2-3-0) |
| Origin | ground under the turn centre of the main gears; `S_REF` = 35.55 (stations `s` are metres aft of the nose tip) | `parts.py`; A380-800 turning-radii table (4-2-0): nose gear radius 32.66 m at 69.5° |
| Fuselage | 7.14 m wide × 8.41 m tall, keel 2.38 m above the ground; constant section s 14.5–50; belly fairing s 14.5–47 (4.35 m half width, bottom 1.85 m above the ground) merged into the lower lobe; tail cone to the APU at s 70.3 | `parts.py`; A380-800: 7.14 × 8.41 m (2-2-0), BF1 1.82 m (2-3-0) |
| Nose | traced top, keel and plan-view curves (`TOP`, `BOTTOM`, `HALF`), radome tip 4.78 m above the ground, widest line rising from the tip to the main-deck window belt (6.33 m) | `parts.py`; A380-800 side / plan views (2-2-0) |
| Wheelbase / min turn | 30.58 m (nose gear to the turn centre); 70° steering → 11.1 m to the turn centre | `GEAR`, test log; A380-800 nose gear 4.97 m aft of the nose (2-2-0), effective steering 69.5° (4-2-0) |
| Wing gears | 33.58 m aft of the nose, legs ±6.23 m (track 12.46 m), four-wheel bogies, axles ±0.81 m, tyres ±0.675 m from the leg, 1.34 m tyres | `WLG_*`; A380-800 WLG 33.58 m, 12.46 m / 14.34 m over the tyres (2-2-0) |
| Body gears | 36.85 m aft of the nose, legs ±2.63 m (track 5.26 m), six-wheel bogies, axles at −1.70 / 0 / +1.70 m | `BLG_*`; A380-800 BLG 36.85 m, 5.26 m (2-2-0) |
| Wing | LE s 19.4 at the fuselage side, traced sweep (~37°); TE kink at y 11.5; tip y 39.7 + fences; dihedral 5.6°; washout 2.2° → −1.2°; hinge 0.74 chord; NACA 2415 at 0.85 → 0.60 thickness | `parts.py`; A380-800 plan view, span 79.75 m, tip 46.97 m aft of the nose (2-2-0) |
| Wingtip fences | thin plates 1.25 m above and 1.15 m below the tip, leading edges raked back, ending 52.07 m aft of the nose | `build_fences`; A380-800 plan / front views (2-2-0) |
| Engines | four, inboard x ±14.8 (inlet s 22.23, centre z 3.00), outboard x ±25.7 (inlet s 29.94, centre z 3.85); the Narrowbody's nacelle scaled 1.65 along / 1.835 across (3.9 m across, 6.2 m to the reverser, 8.6 m to the plug); 24-blade fans; NACA 0010 pylons | `ENGINES`, `build_engine`; A380-800 engine spans 29.6 / 51.4 m, intakes 22.23 / 29.94 m (2-2-0), nacelle bottoms N1 1.05 / N2 1.90 m (2-3-0), Trent 900 nacelle 3.88 × 3.9 m (2-12-0) |
| Tail | Stabiliser z 7.60, root LE s 57.35, span 30.37 m, dihedral 6°, elevator hinge 0.70. Fin root (z 9.58) LE s 53.94 chord 12.06, tip (z 24.17) LE s 68.02 chord 4.70, rudder hinge 0.68. Dorsal fillet s 50.6–55.9 | `parts.py`; A380-800 tailplane span 30.37 m, fin 14.59 m above the reference line, 12.06 / 4.70 m chords (2-2-0) |
| Triangles / draws | LOD0 94,059 / 23 · LOD1 17,154 / 2 · LOD2 4,288 / 2 | `Lods_CutTrianglesAndDrawCalls` log, 2026-10-29 |
| LOD settings | `LOD_RATIOS {1: 0.18, 2: 0.045}`; `LOD1_KEEP []`; `COLLIDER_RATIO 0.3` | `model.py` |
| Texel density | 28.5 px/m (the same 2048² atlas on the largest surface of any type); ~142 px/m on the registration patch | `texelsPerMetre`, `REG_TEX_W` |
| Textures | atlas maps 2048²; Dirt 1024²; LogoUV 512²; GroundShadow 256²; Registration_Default 1024×712 (~0.46 MB per aircraft as BC1); RegistrationDirt 512×356 | files in `Textures/` |
| AO distance | 4.5 m | `AO_DISTANCE` |
| Performance | takeoff 2,900 m, landing 2,100 m, MTOW 575 t; taxi 10, VR 80, VREF 71 m/s; climb 10 m/s at 85 m/s. Needs 2,900 m dry (fits the sandbox's 3,400 m main runway) | `PERFORMANCE`; A380-800 MTOW 575 t (2-1-1), take-off and landing field lengths (3-3-1, 3-4-1) |

A fleet of 100 of them in 10 liveries uses ~73 MB of GPU texture memory (bodies 26.7 MB, registrations 46.4 MB),
inside the 80 MB budget in `Fleet_100Aircraft_10Liveries_StaysWithinBudget`.

## Outputs

| File | What |
|---|---|
| `ArtSource/Aircraft/DoubleDeck/parts.py` | Traced profiles and the builders: `Fuselage` (with the merged belly fairing), `build_fuselage`, `build_windows` (`COCKPIT_WINDOWS`), `build_wing` + `TE_PIECES`, `build_flap_tracks`, `build_fences`, `build_engine`, `build_htail`, `build_vtail` (+ dorsal fillet), `build_gear`, `build_details` (lights, antennas, satcom fairing, pitot probes) |
| `ArtSource/Aircraft/DoubleDeck/model.py` | `build()`, UV rules, `SCHEMES`, `REG_BOX`, `LOGO_BOX`, painted windows (two decks) / doors, grime, `GEAR`, `POINTS`, `LIGHTS`, `PERFORMANCE`, `VIEWS` |
| `Assets/AirCity/Aircraft/DoubleDeck/Models/DoubleDeck.fbx` | LOD0–2, movers, collider, ground shadow |
| `…/DoubleDeck.aircraft` | Type data (imported as `AircraftType`) |
| `…/DoubleDeck.prefab` | `AircraftSetup`, hand-editable |
| `…/Thumbnails/DoubleDeck_<Livery>.png` | Menu thumbnails (6 liveries) |

## Parts

| Mover | Driven by | Notes |
|---|---|---|
| `Fan_1` … `Fan_4` | `PropellerSpinner` | Spinner + 24 blades; 1 = left outboard, 2 = left inboard, 3 = right inboard, 4 = right outboard. Not kept at LOD1 |
| `Rudder` | `AircraftControlSurfaces.rudder` | `hinge` along the fin hinge line, z 9.95–23.6, `axis="z"` |
| `Elevator_L`, `Elevator_R` | `.elevator` | Swept hinge, y 2.30–14.75 |
| `Flap_L_In`, `Flap_L_Out`, `Flap_R_In`, `Flap_R_Out` | `.flaps` | Span 3.67–11.30 (inboard flap ends at the kink) and 11.70–27.40; fixed fillers between |
| `Aileron_L`, `Aileron_R` | `.aileron` | Span 27.60–38.20 |
| `NoseGear` | `AircraftGroundMotion` steering | |
| `NoseGear/Wheel_Nose` | wheel spin | Both nose wheels as one part, exported as `NoseGear__Wheel_Nose` |
| `Wheel_W1`, `Wheel_W2` | wheel spin | **One per axle line across the aircraft**: the front (1) or rear (2) axle of both wing-gear bogies, four wheels rolling together. Pivot on the centre line |
| `Wheel_B1`, `Wheel_B2`, `Wheel_B3` | wheel spin | The same for the body gears' three axles (front to rear), four wheels each |

Bogie beams, legs and braces are fixed (`Gear`). Main-gear wheels are merged per axle line, so all 20 main wheels
cost five draws and LOD0 stays under its 24-draw budget with four fans.

**Lights** (13), as on the other airliners: red / green nav lights in the wingtip fences' leading edges (`NAV_S`)
with strobes 0.5 m behind, white tail nav and strobe on the tail cone, red beacons on the crown (s 30) and belly
(s 44.5), landing lights under the wing roots (y 5.2, s 24.4), a taxi light on the nose-gear strut, and logo
lights on the stabiliser (y 3.6, s 63.4) aimed at the fin.

**Service points** (heights above the ground; Unity x = −port / +starboard):

| Point | Where |
|---|---|
| `Door_L1`–`Door_L5`, `Door_R1`–`Door_R5` | Main deck, s 6.32 / 16.50 / 32.68 / 40.30 / 53.63, sills 5.10 / 5.12 / 5.15 / 5.18 / 5.20 m, 1.07 × 1.93 m |
| `Door_UL1`–`Door_UL3`, `Door_UR1`–`Door_UR3` | Upper deck, s 20.94 / 44.74 / 49.19, sills 7.87 / 7.91 / 7.94 m, 1.07 × 1.92 m. More than 2 m above the lowest door, so `JetBridge.TryDoor` treats them as upper-deck doors |
| `Cargo_Fwd`, `Cargo_Aft` | Starboard (+X), s 11.73 / 48.07, sills 3.05 / 3.11 m, 2.79 × 1.75 m |
| `Cargo_Bulk` | Starboard, s 51.09, sill 3.41 m, 1.13 × 1.13 m |
| `Fuel` | Starboard wing underside at 17 m span |
| `GPU` | under the nose, s 3.6 |
| `Water` / `Lav` | aft belly, s 50.5 port / s 54.5 starboard |
| `TowBar`, `Chock_Nose`, `Chock_L`, `Chock_R`, `Chock_BL`, `Chock_BR` | nose axle; the wing gears' and body gears' front axles, on the ground |

Door stations and sills come from the reference document's door-location sheet (2-7-0) and its ground-clearance
table at maximum ramp weight, forward CG (2-3-0).

## Liveries

| Style | DoubleDeck: R / G / B |
|---|---|
| Classic | Cheatline under the main-deck windows (R) sweeping up the fin ~1.2 m behind its leading edge and out past the tip, pinstripe below (G), painted engines (B) |
| Swoosh | Belly-to-tail swoosh from the wing root at s 28 (R + edge G), passing over the registration; whole fin and nacelles in colour 3 |
| TwoTone | Grey belly under a cheatline at the main-deck floor, painted tail and engines |
| Trainer | Charter / "eurowhite": nose cone + cowls (R), wingtip fences + fin tip (G), main-deck window-line stripe (B) |

`REG_BOX` is the same for every style: (55.6, 61.2, −0.95, −0.35) on the tail cone under the stabiliser, clear of
every stripe. `REG_FACES_U/V` = (54.6, 62.2) × (−1.6, 0.5). `LOGO_BOX` = (62.0, 68.0, 13.0, 19.0) on the fin.

## Regenerate

```sh
cd ArtSource/Aircraft
B=/Applications/Blender.app/Contents/MacOS/Blender
$B -b --factory-startup --python build.py -- --model DoubleDeck --preview --cull --views side_ortho,front34,nose_34   # geometry
$B -b --factory-startup --python build.py -- --model DoubleDeck --texture --check      # after geometry / UV / mover changes (~2.5 min)
$B -b --factory-startup --python finish.py -- --model DoubleDeck --previews --liveries ClassicBlue,SunsetRed --views front34,rear34,photo
```

`--check` reports the same false positives as the Widebody: the inner nacelle surfaces (fan duct, core), the
dorsal fillet, and the wheels. Views (`VIEWS`): `front34`, `rear34`, `side`, `top`, `front`, `nose_close`,
`nose_side`, `nose_front`, `nose_34`, `photo`, `tail_close`, `engine_close`, `engine_rear`, `gear_close`,
`wingtip`, `dorsal_close`, `game`, and `side_ortho` / `top_ortho` / `front_ortho` (the ortho views use the same
frames as the traced drawings, so a render can be overlaid on them).

## Design notes

- **Profiles are traced, not shaped.** The nose and tail (`TOP`, `BOTTOM`, `HALF`) are point lists read off the
  reference drawings at 400 dpi rather than `_rise` curves; the top line is smoothed through the windshield frame.
  The flight deck sits between the decks, so the radome tip is just under the main-deck floor and the glazing
  band (`SILL` 6.75 → `HEAD` 7.48 m above the ground) is level with the pilots' eyes (7.17 m).
- **Belly fairing.** The fairing is wider than the fuselage's lower lobe, so `Fuselage.lower_quarter` merges a
  boxy superellipse into it with a smooth max; `side_x` does the same, so doors and windows drape correctly.
- **Two decks of windows.** Main deck centred 6.22 m above the ground, upper deck 9.02 m, 0.533 m pitch, gapped
  at the doors; painted into the detail map with the door outlines, as on the other airliners.
- **Wheel parts per axle line** (see Parts): gameplay reads wheels by name from `type.wheels`, so the names
  (`Wheel_Nose`, `Wheel_W1/2`, `Wheel_B1/2/3`) are free; the chocks name the gears instead.
- **Grime** (`dirt_extra`): exhaust staining on the wing undersides behind all four engines, grimy belly fairing.
