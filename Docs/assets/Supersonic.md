# Supersonic — Starling D100 (Supersonic airliner)

A 100-seat Mach 2 class airliner, four abreast in a slender fuselage. An original design, not a copy of any real
type: a slightly waisted (area-ruled) fuselage with a long needle nose, a pitot boom and a **fixed** flight deck (no
drooping nose); a thin cranked-arrow wing (a 77° strake into a 64.5° outer panel, one straight trailing edge, a
little anhedral outboard of the nacelles); **four separate nacelles** under the wing, each with a raked rectangular
ramp intake and a round variable afterburner nozzle at the trailing edge; elevons instead of flaps; a tall swept fin
and **no tailplane**. Departures light the afterburners for the takeoff (`AircraftAfterburner`, see
`Docs/systems/aircraft.md` "Afterburners").

Size code **D**: its span (26.2 m) is ICAO code C (`WingspanCode`), but at 61.8 m it is longer than a code C stand's
45 m, so `AircraftType.SizeCode` raises it to D. It needs code D stands (or larger), code D taxiways (18 m) and a
45 m wide runway, and the player sees it as the D100 (`RunwayRules.CheckStand` on a code C stand → `TooLong`). Paved runways only, and the longest runway of
the fleet. Small bulk holds only (loose bags, no containers). `CATEGORY = "Airliner"`, `SEATS = 100`,
`DISPLAY_NAME = "Supersonic airliner"`. Built by the shared pipeline; see `Docs/systems/aircraft.md`.

## Key numbers

All numbers come from `Assets/AirCity/Aircraft/Supersonic/Supersonic.aircraft`, `ArtSource/Aircraft/Supersonic/*.py`
and the stage 2 log.

| Item | Value | Source / note |
|---|---|---|
| Wingspan × length × height | 26.15 × 61.76 × 12.63 m | `bounds` (tests: 25–27, 60.5–62, 12.2–13) |
| Bounds (Unity) | min (−13.075, −0.03, −18.14), max (13.075, 12.6, 43.62) | origin under the main bogies, `S_REF` = 43.0 |
| Fuselage | 2.9 m wide (`HALF_WIDTH` 1.45, waisted to 1.385 at s 36), keel z 2.95 → crown 6.30; nose tip z 4.15 with a 0.62 m pitot boom; tail cone sweeps up to s 61 | `parts.py` |
| Wheelbase | 31.0 m | `steeringPivot.z`, `NOSE_S` 12.0 |
| Main track / wheels | legs at x ±2.75 (track 5.5 m, inboard of the nacelles), four-wheel bogies: axles ±0.82 m, wheels ±0.42, r 0.60 m; nose twin ±0.34, r 0.42 m | `parts.py` |
| Max steering | 60° | `GEAR` |
| MTOW | 170,000 kg | `PERFORMANCE` |
| Takeoff / landing distance | 3,300 m / 2,300 m | `RunwayLength` 3,300 m: fits the sandbox's 3,600 m runway dry |
| Speeds | VR 102 m/s, approach 82, climb 15 m/s at 125 m/s | `PERFORMANCE` |
| Wing | NACA 0004 scaled to 3% at the root, 4% at the tip; apex s 19.5 at y 1.2; strake 77° to y 3.3, outer panel 64.5° to the tip at y 13.0 (chord 2.3 m); straight TE at s 51.2; flat to y 7.6, then 3.5° anhedral; washout 0.6° → −1° | `parts.py` |
| Elevons | chord 2.4 m (root) → 1.5 m (tip), hinge line just ahead of the TE; fixed TE over the nozzles (y 3.75–7.6) | `TE_PIECES` |
| Nacelles | x ±4.62 (lip s 38.6, exit s 52.7) and ±6.72 (lip s 39.6, exit s 53.1); box 1.48 m wide, bottom z 1.62, top buried 0.16 m in the wing; intake top lip raked 0.65 m forward; turns round into a nozzle r 0.66 → exit r 0.60, centre z 2.90 | `NACELLES`, `build_engine` |
| Fin | root (z 6.0) LE s 46.0, chord 13.6 → 3.0 at z 12.6, 58° sweep; rudder z 6.55–12.25, hinge 0.74 chord | `parts.py` |
| Rotation clearance | at the game's 8.5° rotation the nozzles' bottoms (z 2.30, 9.7 m behind the axles) clear by ~0.85 m | `S_REF` placed for this |
| Triangles | LOD0 body 45,601 · LOD1 11,499 · LOD2 2,889 | stage 2 log |
| LOD settings | `LOD_RATIOS {1: 0.2, 2: 0.05}`; `LOD1_KEEP []`; `COLLIDER_RATIO 0.3` | `model.py` |
| Texel density | 33 px/m (atlas); `REG_TEX_W` 768 | `texelsPerMetre` |

## Source and outputs

| File | Contents |
|---|---|
| `ArtSource/Aircraft/Supersonic/parts.py` | Constants `S_REF`, `KEEL`, `CROWN`, `HALF_WIDTH`, `NOSE_TOP`; `Fuselage`; `build_fuselage` (+ pitot boom), `build_windows` (`COCKPIT_WINDOWS`), cranked-arrow `build_wing` + `TE_PIECES`, `build_engine` (superellipse nacelle loft, `_nacelle_section`, `_ring`), `build_vtail`, `build_gear`, `build_details` |
| `ArtSource/Aircraft/Supersonic/model.py` | `build()` and the movers, LOD / UV settings, four `SCHEMES`, `FLOOR_Z`, `CHEAT_Z`, `DOORS`, painted `WINDOWS` / `_doors`, `REG_*`, `LOGO_BOX`, `GEAR`, `PERFORMANCE`, `POINTS`, `LIGHTS`, **`EXHAUSTS`**, `VIEWS` |
| `Assets/AirCity/Aircraft/Supersonic/Models/Supersonic.fbx`, `Textures/`, `Supersonic.aircraft`, `Supersonic.prefab` | Generated model, maps, type data; the prefab is created once by `AircraftSetup` and carries `AircraftAfterburner` |

## Parts and movers

| Mover (Unity name) | Driven by | Notes |
|---|---|---|
| `Elevator_L`, `Elevator_R` | `AircraftControlSurfaces.elevator` | Inboard elevons, y 1.5–3.75: the pitch surfaces |
| `Aileron_L_In`, `Aileron_L_Out`, `Aileron_R_In`, `Aileron_R_Out` | `.aileron` | Outboard elevons, y 7.6–10.05 and 10.15–12.35 |
| `Rudder` | `.rudder` | `axis="z"` |
| `NoseGear`, `NoseGear/Wheel_Nose_*` | steering, wheel spin | Long strut: the nose stands high |
| `Wheel_L1`, `Wheel_L2`, `Wheel_R1`, `Wheel_R2` | wheel spin | A "wheel" is a bogie axle (two wheels) |
| `CargoDoor_Cargo_Fwd`, `CargoDoor_Cargo_Aft` | cargo door hinge | Starboard plug hatches (−100°) |

No `Flap_*` (a delta has none; `flaps` does nothing) and no `Fan_*` (the compressor faces sit deep in the ducts).

**Service points** (17): doors L1–L3 / R1–R3 at s 11.0, 17.6, 52.4 (sill 4.0 m), two starboard bulk holds (forward
s 15.6, sill 3.25 m; aft s 55.0 in the tail cone, sill 4.0 m), wing fuel, GPU, water, lavatory, tow bar, chocks.
**Lights** (13): wingtip navigation + strobes, tail-cone light + strobe, crown + belly beacons, landing lights under the
strakes, nose-gear taxi light, two logo lights on the wing roots aimed at the fin.
**Exhausts** (4): `Nozzle_L1/L2/R1/R2` at the nozzle exits, pointing aft, radius 0.56, all afterburning.

## Liveries

- Classic: a cheatline through the windows that sweeps up across the fin; pinstripe below; nacelles in colour 3.
- Swoosh: rises from the wing root to the fin; fin and nacelles in colour 3.
- TwoTone: coloured belly under a low cheatline; fin and nacelles in colour 3.
- Trainer: coloured needle nose and nacelles, lower fin, a stripe above the windows.

Registration on the fuselage over the wing root (s 42–46.5, below the windows; Swoosh above them). Logo box on the
fin (s 53.4–58.4, z 7.0–10.6).

## Review captures

`AircraftAfterburner` flames lit (and half lit), day and night, from behind, the side and the game camera:

```sh
$U -batchmode -projectPath . -executeMethod AirCity.EditorTools.Aircraft.AfterburnerReview.FromCommandLine -logFile Logs/afterburner.log
```

Output: `Captures/Afterburner/Supersonic-{rear34,side,close,close-half,game}-{day,night}.png`.
