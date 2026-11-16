# Trijet — Starling D250 (Long-range trijet)

A 250-seat long-range trijet, the veteran of the fleet: a twin-aisle tube of modest width, a lightly swept wing
with plain squared tips and no winglets, two long slim low-bypass nacelles under the wing, and a third engine fed
by an intake above the rear fuselage at the fin root (an S-duct), exhausting through the end of the tail cone.
ICAO aerodrome code **D** (span < 52 m): it fills the gap between the C180 and the E350 and is the only type that
needs a code D stand. Paved runways only; it needs more runway than the twins. Widebody holds: containers through
`Cargo_Fwd` / `Cargo_Aft`, loose bags through `Cargo_Bulk`. `CATEGORY = "Airliner"`, `SEATS = 250`,
`DISPLAY_NAME = "Long-range trijet"`. Built by the shared pipeline; see `Docs/systems/aircraft.md`. Not a copy of
any real type.

## Key numbers

All numbers come from `Assets/AirCity/Aircraft/Trijet/Trijet.aircraft`, `ArtSource/Aircraft/Trijet/*.py` and the
EditMode test log.

| Item | Value | Source / note |
|---|---|---|
| Wingspan × length × height | 50.09 × 58.50 × 16.00 m | `bounds` (test ranges 49.5–51.9, 57–59, 15.5–16.5) |
| Bounds (Unity) | min (−25.045, −0.03, −30.7), max (25.045, 16.0, 27.8) | origin under the main bogies, `S_REF` = 27.8 |
| Fuselage | 5.1 m wide (`HALF_WIDTH` 2.55, 2.65 at the wing fairing), keel z 2.10 → crown 7.20, tail end S1 57.8; the tail cone stays fat to the exhaust nozzle (r 0.62) | `parts.py` |
| Wheelbase | 22.0 m | `steeringPivot.z`, `NOSE_S` 5.8 |
| Main track / wheels | legs at x ±4.7 (track 9.4 m), four-wheel bogies: axles ±0.85 m about the leg, wheels at ±0.62 from it, r 0.58 m; nose twin at ±0.36, r 0.48 m | `parts.py` |
| Max steering | 68° | `GEAR` |
| MTOW | 210,000 kg | `PERFORMANCE` |
| Takeoff / landing distance | 3,000 m / 1,850 m | `RunwayLength` = 3,000 m; fits the sandbox's 3,600 m runway dry |
| Unpaved runways | no | `PERFORMANCE` |
| Wing | NACA 2415; LE sweep 31° from s 21.6 at the side (y 2.55); TE kink at y 9.2; tip y 24.95, squared with a small fairing, no winglet; dihedral 5.5°; washout 2.5° → −1°; hinge 0.75 chord | `parts.py` |
| Wing engines | x ±8.1, z 2.15, inlet s 21.3; the Narrowbody nacelle profile stretched 1.25× across and 1.94× along (`K`, `L`): fan 2.1 m, cowl 7.3 m long; 20-blade fans; long core and plug; NACA 0010 pylon | `build_engine`, `_engine_body` |
| Centre engine | intake ring at s 42.0, centre z 7.83, same 2.1 m fan (`Fan_C`); one loft carries the lip and barrel back as a fairing that shrinks and sinks into the tail cone under the fin (s 44–55.5); exhaust nozzle in the tail cone end | `build_tail_engine`, `build_fuselage` |
| Tail | Low stabiliser z 6.25, root LE s 49.6, 33° sweep, half-span 8.4, dihedral 4°. Fin z 8.2 → 16.0 rising out of the fairing (surface from z 6.3), 41° sweep, chord 9.2 → 3.3; rudder z 8.9–15.6. Hinges at 0.70 chord | `parts.py` |
| Triangles / draws | LOD0 43,026 · LOD1 12,358 · LOD2 3,122; each fan 1,360 | stage 2 log, 2026-11-06 |
| LOD settings | `LOD_RATIOS {1: 0.2, 2: 0.05}`; `LOD1_KEEP []`; `COLLIDER_RATIO 0.3` | `model.py` |
| Texel density | 35 px/m (atlas); `REG_TEX_W` 1024 on the registration patch | `texelsPerMetre` |
| AO distance | 3.5 m | `AO_DISTANCE` |

## Source and outputs

| File | Contents |
|---|---|
| `ArtSource/Aircraft/Trijet/parts.py` | Constants `S_REF`, `KEEL`, `CROWN`, `HALF_WIDTH`, `TIP_H`; nose profiles; `Fuselage`; `build_fuselage` (+ exhaust nozzle), `build_windows` (`COCKPIT_WINDOWS`), `build_wing` + `TE_PIECES`, `build_engine` / `_engine_body` / `_fan`, `build_tail_engine`, `build_htail`, `build_vtail`, `build_gear`, `build_details` |
| `ArtSource/Aircraft/Trijet/model.py` | `build()` and the movers, LOD / UV settings, four `SCHEMES` (the tail fairing is painted with the fin), `FLOOR_Z`, `CHEAT_Z`, `DOORS`, painted `WINDOWS` / `_doors`, `REG_*`, `LOGO_BOX`, `GEAR`, `PERFORMANCE`, `POINTS`, `LIGHTS`, `VIEWS` |
| `Assets/AirCity/Aircraft/Trijet/Models/Trijet.fbx`, `Textures/`, `Trijet.aircraft`, `Trijet.prefab` | Generated model, maps, type data; the prefab is created once by `AircraftSetup` |

## Parts and movers

| Mover (Unity name) | Driven by | Notes |
|---|---|---|
| `Fan_L`, `Fan_R`, `Fan_C` | `PropellerSpinner` | Spinner + 20 blades each; `Fan_C` sits in the centre intake. Not kept at LOD1 |
| `Rudder` | `AircraftControlSurfaces.rudder` | `axis="z"` |
| `Elevator_L`, `Elevator_R` | `.elevator` | x 2.0–8.12 |
| `Flap_L_In`, `Flap_L_Out`, `Flap_R_In`, `Flap_R_Out` | `.flaps` | Span 2.65–9.1 and 9.3–18.2 |
| `Aileron_L`, `Aileron_R` | `.aileron` | Span 18.4–24.2 |
| `NoseGear`, `NoseGear/Wheel_Nose_*` | steering, wheel spin | |
| `Wheel_L1`, `Wheel_L2`, `Wheel_R1`, `Wheel_R2` | wheel spin | A "wheel" is a bogie axle: its two wheels roll together, as on the Widebody |
| `CargoDoor_Cargo_Fwd`, `CargoDoor_Cargo_Aft`, `CargoDoor_Cargo_Bulk` | cargo door hinge | Starboard; the two container doors swing up 145°, the bulk hatch is a plug |

**Service points** (19): doors L1–L4 / R1–R4 at the cabin floor (sill 4.15 m, jet-bridge height), three starboard
cargo doors, wing fuel, GPU, water, lavatory, tow bar, chocks.
**Lights** (13): wingtip navigation + strobes, fin-top tail light + strobe, crown + belly beacons, landing lights
under the wing roots, nose-gear taxi light, two stabiliser logo lights.

## Pipeline notes

- The centre intake's outer skin and its fairing are one loft, so there is no shading seam between them; rings
  that would dip under the crown follow the skin instead. Its tags are `tailnacelle` / `tailintake` /
  `tailfanface`, which `service_footprints` counts with the tail, not the wing engines.
- Clipping the cargo-door openings out of this fuselage produced sliver polygons whose corners rounded onto each
  other; `cargo_doors._write` now drops any face with fewer than three distinct corners (Blender's custom-normals
  call crashed on them).
