# Quadjet — Long-haul quadjet

A generic long-haul four-engine airliner with a partial upper deck: a single main deck under a stretched upper-deck
"hump" that carries the flight deck at its front and blends into the crown behind it, a long, slightly drooped nose
under the hump, a big swept wing with raked wingtips (no winglets), four turbofans on pylons, a tall fin, a two-wheel
nose gear and four four-wheel main bogies (two wing gears, two body gears). It carries no real airline or
manufacturer branding. `CATEGORY = "Airliner"`, `DISPLAY_NAME = "Long-haul quadjet"`. ICAO aerodrome code **F**,
paved runways only. The pipeline is the shared one in `Docs/systems/aircraft.md`; this page covers only what is
specific to this type.

## Key numbers

From `Assets/AirCity/Aircraft/Quadjet/Quadjet.aircraft` and `ArtSource/Aircraft/Quadjet/*.py`. The profiles were
traced from the general-dimensions side, plan and front views of the reference document and checked by overlaying
it on orthographic renders. The reference document is named in the first row's Source column; section numbers
below are its own. Heights use the middle of each minimum / maximum range of its ground-clearance table (2.3.2).

| What | Value | Source |
|---|---|---|
| Wingspan × length × height | 68.40 × 76.25 × 19.41 m | `Dimensions_MatchTheRealAircraftClass` log; Boeing "747-8 Airplane Characteristics for Airport Planning" (D6-58326-3, rev. E, Jan. 2026): 747-8 68.40 × 76.25 m (2.2.2), fin tip 18.97–19.51 m (2.3.2 K) |
| Origin | ground under the turn centre of the main gears; `S_REF` = 36.68 (stations `s` are metres aft of the nose tip) | `parts.py`; 747-8 turning radii with body-gear steering (4.2.1): nose gear radius 30.8 m at 70° → 28.94 m effective wheelbase |
| Fuselage | 6.48 m wide; keel 2.24 m above the ground; main-deck crown 9.02 m, upper-deck hump 10.04 m; hump constant from s 9 to 24.5, faired down to the main crown by s 35.5; constant section s 15–56; tail cone to the APU at s 76.05; a slight wing-to-body fairing s 19.5–50.5 (3.52 m half width) | `parts.py`; 747-8: 6.50 m cabin width (2.2.2, 2.5) |
| Nose | traced top, keel and plan-view curves (`TOP`, `BOTTOM`, `HALF`); radome tip 5.70 m above the ground, level with the main-deck windows; the keel droops from the tip to the constant section by s 14 | `parts.py`; 747-8 side / plan views (2.2.2) |
| Flight deck | six panes at the front of the hump, `SILL` 8.48 → `HEAD` 9.12 m above the ground, s 4.2–6.7 | `COCKPIT_WINDOWS`; side view (2.2.2) |
| Wheelbase / min turn | 28.94 m (nose gear to the turn centre; 29.66 m to the midpoint of the wing and body gears); 70° steering → 10.5 m to the turn centre | `GEAR`, test log; 747-8 nose gear 7.74 m aft of the nose, wheelbase 29.66 m (2.2.2), 70° max steering (4.2.1) |
| Wing gears | 35.87 m aft of the nose, legs ±5.50 m (track 10.99 m), four-wheel bogies, axles ±0.735 m, tyres ±0.56 m from the leg, 1.32 m tyres | `WLG_*`, `BOGIE_DS`; 747-8 wing-gear track 10.99 m, body gears 3.07 m aft (2.2.2), 52 in tyres, 58 in axle pitch (7.2) |
| Body gears | 38.94 m aft of the nose, legs ±1.92 m (track 3.83 m), four-wheel bogies like the wing gears | `BLG_*`; 747-8 body-gear track 3.83 m (2.2.2) |
| Wing | LE s 24.45 at the fuselage side, traced sweep (~42° inboard, ~40° outboard); TE kink at y 10.3; raked tip from y 31.0 to 34.2 (LE 49.2 → 52.6, TE 53.05 → 55.25); dihedral 6.1° (tip 6.65 m above the ground); washout 2° → −1.5°; hinge 0.74 chord; NACA 2415 at 0.90 → 0.50 thickness | `parts.py`; 747-8 span 68.40 m, tip 51.94 / 55.34 m aft of the nose (2.2.2), wingtip height 6.51–6.80 m (2.3.2 M) |
| Engines | four, inboard x ±11.93 (inlet s 27.25, centre z 2.45), outboard x ±21.18 (inlet s 35.86, centre z 3.58); the Narrowbody's nacelle scaled 1.36 along / 1.52 across (3.2 m across, 5.1 m to the reverser, 7.1 m to the plug); 18-blade fans; NACA 0010 pylons | `ENGINES`, `build_engine`; 747-8 engine spans 11.93 / 21.18 m, intakes 27.25 / 35.86 m aft of the nose (2.2.2), nacelle bottoms 0.73–0.96 / 1.90–2.07 m (2.3.2 P / N) |
| Tail | Stabiliser z 7.30, root LE s 62.36, half-span 11.0 m (21.99 m), 7° dihedral (tip 8.65 m above the ground), elevator hinge 0.70. Fin root (z 9.0) LE s 60.30 chord 11.90, tip (z 19.38) LE s 72.45 chord 3.80, rudder hinge 0.68. Dorsal fillet s 56.4–61.6 | `parts.py`; 747-8 tailplane span 21.99 m (2.2.2), stabiliser 8.58–9.12 m (2.3.2 L) |
| Triangles / draws | LOD0 86,277 / 22 · LOD1 15,754 / 2 · LOD2 3,938 / 2 | `Lods_CutTrianglesAndDrawCalls` log, 2026-10-29 |
| LOD settings | `LOD_RATIOS {1: 0.18, 2: 0.045}`; `LOD1_KEEP []`; `COLLIDER_RATIO 0.3` | `model.py` |
| Texel density | ~26 px/m (the shared 2048² atlas); ~150 px/m on the registration patch | `texelsPerMetre`, `REG_TEX_W` |
| Textures | atlas maps 2048²; Dirt 1024²; LogoUV 512²; GroundShadow 256²; Registration_Default 1024×760 (~0.4 MB per aircraft as BC1); RegistrationDirt 512×380 | files in `Textures/` |
| AO distance | 4.2 m | `AO_DISTANCE` |
| Performance | takeoff 3,100 m, landing 2,200 m, MTOW 447.7 t; taxi 10, VR 80, VREF 75 m/s; climb 10 m/s at 85 m/s. Needs 3,100 m dry (fits the sandbox's 3,400 × 60 m main runway) | `PERFORMANCE`; 747-8 MTOW 447,695 kg (2.1.2), take-off and landing runway charts (3.3, 3.4) |

A fleet of 100 of them in 10 liveries uses ~76 MB of GPU texture memory (bodies 26.7 MB, registrations 49.5 MB),
inside the 80 MB budget in `Fleet_100Aircraft_10Liveries_StaysWithinBudget`.

## Outputs

| File | What |
|---|---|
| `ArtSource/Aircraft/Quadjet/parts.py` | Traced profiles and the builders: `Fuselage` (hump, wing-to-body fairing), `build_fuselage`, `build_windows` (`COCKPIT_WINDOWS`), `build_wing` + `TE_PIECES` (raked tips), `build_flap_tracks`, `build_engine`, `build_htail`, `build_vtail` (+ dorsal fillet), `build_gear`, `build_details` (lights, antennas, satcom fairing, pitot probes) |
| `ArtSource/Aircraft/Quadjet/model.py` | `build()`, UV rules, `SCHEMES`, `REG_BOX`, `LOGO_BOX`, painted windows (main and upper deck) / doors (incl. the upper-deck exit), grime, `GEAR`, `POINTS`, `LIGHTS`, `PERFORMANCE`, `VIEWS` |
| `Assets/AirCity/Aircraft/Quadjet/Models/Quadjet.fbx` | LOD0–2, movers, collider, ground shadow |
| `…/Quadjet.aircraft` | Type data (imported as `AircraftType`) |
| `…/Quadjet.prefab` | `AircraftSetup`, hand-editable |
| `…/Thumbnails/Quadjet_<Livery>.png` | Menu thumbnails (6 liveries) |

## Parts

| Mover | Driven by | Notes |
|---|---|---|
| `Fan_1` … `Fan_4` | `PropellerSpinner` | Spinner + 18 blades; 1 = left outboard, 2 = left inboard, 3 = right inboard, 4 = right outboard. Not kept at LOD1 |
| `Rudder` | `AircraftControlSurfaces.rudder` | `hinge` along the fin hinge line, z 9.35–18.85, `axis="z"` |
| `Elevator_L`, `Elevator_R` | `.elevator` | Swept hinge, y 1.85–10.55 |
| `Flap_L_In`, `Flap_L_Out`, `Flap_R_In`, `Flap_R_Out` | `.flaps` | Span 3.34–10.30 (inboard flap ends at the kink) and 12.60–24.90; fixed pieces between (behind the inboard engine) |
| `Aileron_L`, `Aileron_R` | `.aileron` | Span 25.10–30.95, up to the raked tip |
| `NoseGear` | `AircraftGroundMotion` steering | |
| `NoseGear/Wheel_Nose` | wheel spin | Both nose wheels as one part, exported as `NoseGear__Wheel_Nose` |
| `Wheel_W1`, `Wheel_W2` | wheel spin | **One per axle line across the aircraft**: the front (1) or rear (2) axle of both wing-gear bogies, four wheels rolling together. Pivot on the centre line |
| `Wheel_B1`, `Wheel_B2` | wheel spin | The same for the body gears' two axles |

Bogie beams, legs and braces are fixed (`Gear`). Main-gear wheels are merged per axle line, so all 16 main wheels
cost four draws and LOD0 stays under its 24-draw budget with four fans.

**Lights** (13), as on the other airliners: red / green nav lights at the raked tips' leading edge (`NAV_S`) with
strobes 0.6 m behind, white tail nav and strobe on the tail cone, red beacons on the crown (s 37) and belly (s 44),
landing lights under the wing roots (y 5.0, s 26.9), a taxi light on the nose-gear strut, and logo lights on the
stabiliser (y 3.2, s 67.6) aimed at the fin.

**Service points** (heights above the ground; Unity x = −port / +starboard):

| Point | Where |
|---|---|
| `Door_L1`–`Door_L5`, `Door_R1`–`Door_R5` | Main deck, s 9.50 / 22.90 / 34.70 / 46.30 / 60.80, sills 4.97 / 5.005 / 5.03 / 5.075 / 5.135 m, 1.07 × 1.93 m |
| upper-deck exit (painted only) | s 15.25, sill 7.85 m, 0.84 × 1.52 m outline on both sides. Not a `PassengerDoor`: the game treats a passenger door more than 2 m above the lowest one as an upper-deck boarding door (`JetBridge.TryDoor`) |
| `Cargo_Fwd`, `Cargo_Aft` | Starboard (+X), s 13.30 / 50.67, sills 2.92 / 3.04 m, 2.64 × 1.68 m |
| `Cargo_Bulk` | Starboard, s 54.28, sill 3.23 m, 1.12 × 1.24 m |
| `Fuel` | Starboard wing underside at 14.5 m span |
| `GPU` | under the nose, s 5.6 |
| `Water` / `Lav` | aft belly, s 53.0 port / s 57.0 starboard |
| `TowBar`, `Chock_Nose`, `Chock_L`, `Chock_R`, `Chock_BL`, `Chock_BR` | nose axle; the wing gears' and body gears' front axles, on the ground |

Door stations come from the document's door-location sheet (2.7.1, p. 2-14: 9.5 / 22.9 / 34.7 / 46.3 / 60.8 m;
cargo 13.3 / 50.67 / 54.28 m, 2.7.4–2.7.6) and the sills from its ground-clearance table (2.3.2, p. 2-7: C 4.78–5.16,
S 4.87–5.14, V 4.94–5.12, T 4.95–5.20, J 4.95–5.32, D 2.75–3.09, F 2.89–3.18, G 3.07–3.38 m, the middle of each
range). Opening sizes: 42 × 76 in (2.7.1), cargo 104 × 66 in (2.7.4), bulk 44 × 49 in (2.7.6). The game counts doors
from the front on each side, so a code F stand's bridges dock at L1 and L2.

## Liveries

| Style | Quadjet: R / G / B |
|---|---|
| Classic | Cheatline under the main-deck windows (R) sweeping up the fin ~1.2 m behind its leading edge and out past the tip, pinstripe below (G), painted engines (B) |
| Swoosh | Belly-to-tail swoosh from the wing root at s 30 (R + edge G), passing under the registration; whole fin and nacelles in colour 3 |
| TwoTone | Grey belly under a cheatline at the main-deck floor, painted tail and engines |
| Trainer | Charter / "eurowhite": nose cone + cowls (R), raked wingtips + fin tip (G), main-deck window-line stripe (B) |

Both window rows (main deck centred 6.00 m above the ground, upper deck 8.65 m, 0.508 m pitch, gapped at the doors),
the door outlines and the upper-deck exits are painted into the detail map, as on the other airliners.

`REG_BOX` is the same for every style: (62.4, 67.2, −0.95, −0.35) on the tail cone under the stabiliser, clear of
every stripe. `REG_FACES_U/V` = (61.6, 68.2) × (−1.6, 0.5). `LOGO_BOX` = (68.0, 73.4, 12.2, 16.8) on the fin.

## Regenerate

```sh
cd ArtSource/Aircraft
B=/Applications/Blender.app/Contents/MacOS/Blender
$B -b --factory-startup --python build.py -- --model Quadjet --preview --cull --views side_ortho,front34,nose_34   # geometry
$B -b --factory-startup --python build.py -- --model Quadjet --texture --check      # after geometry / UV / mover changes (~2 min)
$B -b --factory-startup --python finish.py -- --model Quadjet --previews --liveries ClassicBlue,SunsetRed --views front34,rear34,photo
```

`--check` reports the usual false positives: the inner nacelle surfaces (fan duct, core) and the wheels. Views
(`VIEWS`): `front34`, `rear34`, `side`, `top`, `front`, `nose_close`, `nose_side`, `nose_front`, `nose_34`, `hump`,
`photo`, `tail_close`, `engine_close`, `engine_rear`, `gear_close`, `wingtip`, `wingtip_top`, `dorsal_close`,
`game`, and `side_ortho` / `top_ortho` / `front_ortho` (82 m wide, centred on s 38.1 and 9.5 m up, so a render can
be overlaid on the traced drawings at their 23.46 px/m).

## Design notes

- **Profiles are traced, not shaped.** `TOP`, `BOTTOM`, `HALF` and the wing / tail planforms are point lists read
  off the general-dimensions drawings at 400 dpi (line centres). The keel was set 2.24 m above the ground so the
  door sills of the ground-clearance table land on the skin at the painted door bottoms.
- **The hump.** The upper deck is part of the one fuselage loft: the top line rises over the flight-deck brow to
  10.04 m, runs level to s 24.5 and fairs down to the 9.02 m main crown by s 35.5, while the half width stays at the
  main deck's 3.24 m. With `nt` a little above 2 the section over the upper deck is the tall oval of the cross-section
  sheet (2.5.2), not a separate bubble, so the hump blends into the sides with no seam.
- **Nose.** The radome tip sits level with the main-deck window line (5.7 m) and the keel droops away from it, so the
  nose reads long and slightly drooped under the hump; the radome joint is painted just ahead of the first windows.
- **Raked tips.** The last 3.2 m of span sweep both edges further back (the plan view's tip points 52.6 / 55.25 m
  aft of the nose) on thinner sections; the nav and strobe lights sit in the tip's leading edge.
- **Wheel parts per axle line** (see Parts): gameplay reads wheels by name from `type.wheels`, so the names
  (`Wheel_Nose`, `Wheel_W1/2`, `Wheel_B1/2`) are free; the chocks name the gears instead.
- **Grime** (`dirt_extra`): exhaust staining on the wing undersides behind all four engines, a grimy belly between
  s 20 and 50.
