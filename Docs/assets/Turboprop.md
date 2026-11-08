# Turboprop — Starling B50 (Regional turboprop)

A 50-seat regional twin turboprop: a slim low-slung fuselage under a straight high wing, two six-blade scimitar
propellers on deep nacelles that also carry the main gear, and a T-tail. It is the fleet's only propeller
airliner and its only type between the light single and the jets: ICAO aerodrome code **B** (span < 24 m), and
it takes grass and dirt strips, so it is the first airliner a grass-field airport can serve. Boards by its own
airstair (no jet bridge). `CATEGORY = "Airliner"`, `SEATS = 50`, `DISPLAY_NAME = "Regional turboprop"`. Built by
the shared pipeline; see `Docs/systems/aircraft.md`. Not a copy of any real type.

## Key numbers

All numbers come from `Assets/AirCity/Aircraft/Turboprop/Turboprop.aircraft`,
`ArtSource/Aircraft/Turboprop/*.py` and the EditMode test log.

| Item | Value | Source / note |
|---|---|---|
| Wingspan × length × height | 23.43 × 25.59 × 7.70 m | `bounds` (test ranges 23–24, 25–26.5, 7.2–8) |
| Bounds (Unity) | min (−11.715, −0.02, −13.685), max (11.715, 7.699, 11.9) | origin under the main axles, `S_REF` = 11.9 |
| Fuselage | 2.8 m wide (`HALF_WIDTH` 1.40), keel z 1.05 → crown 3.95, tail end S1 25.4; flattish belly (`nb` 2.4) | `parts.py` |
| Wheelbase | 9.2 m | `steeringPivot.z`, `NOSE_S` 2.7 |
| Main track / wheels | legs at x ±4.1 under the nacelles (track 8.2 m), twin wheels at ±3.8 / ±4.4, r 0.45 m; nose twin at ±0.20, r 0.27 m | `parts.py` |
| Max steering | 60° | `GEAR` |
| MTOW | 23,000 kg | `PERFORMANCE` |
| Takeoff / landing distance | 1,350 m / 1,050 m | `RunwayLength` = 1,350 m |
| Unpaved runways | **yes** | `PERFORMANCE["unpaved"]` |
| Wing | NACA 2415, high-mounted on the crown (`WING_Z0` 4.03); constant 2.7 m chord to y 4.6 then tapered; tip y 11.65; dihedral 2.5°; washout 2° → 0°; hinge 0.75 chord | `parts.py` |
| Engines | nacelle centre lines x ±4.1, thrust line z 3.45; superellipse sections deepening to 1.05 m below the thrust line behind the wing (gear bay); 6-blade propellers r 1.95 m at s 7.35, scimitar tips | `build_engine`, `_blades` |
| Tail | T-tail: fin z 3.7 → 7.5, 38° sweep, chord 4.2 → 2.3, rudder z 4.05–7.35; stabiliser on the fin top (z 7.38), half-span 4.25, 12° sweep, bullet fairing at the junction; dorsal fillet s 17.9–20.5 | `parts.py` |
| Triangles / draws | LOD0 24,737 · LOD1 7,564 (+ two 1,948-triangle propellers) · LOD2 2,874 | stage 2 log, 2026-11-06 |
| LOD settings | `LOD_RATIOS {1: 0.22, 2: 0.06}`; `LOD1_KEEP ["Propeller_L", "Propeller_R"]` (the 3.9 m discs read from far off); `COLLIDER_RATIO 0.3` | `model.py` |
| Texel density | 79 px/m (atlas) | `texelsPerMetre` |
| AO distance | 1.8 m | `AO_DISTANCE` |

## Source and outputs

| File | Contents |
|---|---|
| `ArtSource/Aircraft/Turboprop/parts.py` | Constants `S_REF`, `KEEL`, `CROWN`, `HALF_WIDTH`, `TIP_H`; nose profiles; `Fuselage`; `build_fuselage`, `build_windows` (`COCKPIT_WINDOWS`), `build_wing` + `TE_PIECES`, `build_engine` (nacelle loft `NACELLE`, `_blades`), `build_htail` (T-tail + bullet), `build_vtail` (+ dorsal), `build_gear`, `build_details` |
| `ArtSource/Aircraft/Turboprop/model.py` | `build()` and the movers, LOD / UV settings, four `SCHEMES`, `CHEAT_Z`, `DOOR_1_S` / `DOOR_2_S`, painted `WINDOWS` / `_doors`, `REG_*`, `LOGO_BOX`, `GEAR`, `PERFORMANCE`, `POINTS`, `LIGHTS`, `VIEWS` |
| `Assets/AirCity/Aircraft/Turboprop/Models/Turboprop.fbx`, `Textures/`, `Turboprop.aircraft`, `Turboprop.prefab` | Generated model, maps, type data; the prefab is created once by `AircraftSetup` |

## Parts and movers

| Mover (Unity name) | Driven by | Notes |
|---|---|---|
| `Propeller_L`, `Propeller_R` | `PropellerSpinner` (900 rpm, like the Cessna's) | Spinner + six blades; pivot on the thrust line at the propeller plane. Kept as low-poly copies at LOD1 |
| `Rudder` | `AircraftControlSurfaces.rudder` | `axis="z"` |
| `Elevator_L`, `Elevator_R` | `.elevator` | On the T-tail, x 0.45–4.0 |
| `Flap_L_In`, `Flap_L_Out`, `Flap_R_In`, `Flap_R_Out` | `.flaps` | Span 1.5–3.45 and 4.75–8.2, a fixed filler behind each nacelle |
| `Aileron_L`, `Aileron_R` | `.aileron` | Span 8.35–11.3 |
| `NoseGear`, `NoseGear/Wheel_Nose_*` | steering, wheel spin | |
| `Wheel_L1`, `Wheel_L2`, `Wheel_R1`, `Wheel_R2` | wheel spin | 1 = inboard, 2 = outboard |
| `CargoDoor_Cargo_Fwd`, `CargoDoor_Cargo_Aft` | cargo door hinge | Top-hinged bulk doors on the starboard side, like every type's (baggage by hand: belt loader and carts) |

**Service points** (13): airstair door `Door_L1` (sill 1.17 m: no jet bridge), rear service door `Door_R2`, starboard
cargo doors `Cargo_Fwd` / `Cargo_Aft`, wing-top fuel caps, GPU, water, lavatory, tow bar, chocks.
**Lights** (13): wingtip navigation + strobes, tail navigation + strobe, crown + belly beacons, leading-edge
landing lights inboard of the nacelles, nose-gear taxi light, two crown logo lights aimed up the fin.
