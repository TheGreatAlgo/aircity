# JetBridge: passenger boarding bridge

An apron-drive boarding bridge with three telescoping tunnels, after the ThyssenKrupp / JBT / ADELTE bridges at
Schiphol, Frankfurt and Changi. A round **rotunda** stands on one steel column. Three rectangular steel
**tunnels** slide inside each other (A inside B inside C). A wheel **bogie** on two lifting **legs** carries the
outer tunnel. The operator **cab** with its black accordion canopy docks at the aircraft door. A fixed **link**
corridor on **columns** leads from the terminal to the rotunda, leaving the terminal at the main floor (4.0 m). For the
upper deck of a double-deck (A380-class) airliner, whose door sills are about 7.9 m up, the same tunnels, cab and drive
stand on a **high rotunda** (`RotundaHigh`, floor 7.8 m). A gate with two or three bridges has a **head**: a fixed
building on link columns beside the aircraft (`Head`, with a second storey `HeadUpper` at 7.8 m for an upper-deck
bridge), reached from the terminal by one **main link** (the `Link`, widened), its bridges' rotundas standing along
its side on short level stub links. It is built by the vehicle pipeline (`KIND = "JetBridge"`).

Unlike the other vehicles, the game assembles the bridge itself each frame: it swings round the rotunda,
telescopes, pitches, turns the cab to the door, stretches the legs down to the bogie, stretches (and pitches) the
link to the terminal, stretches a head to its length and each column's shaft up to its link or head. So every part is a separate object, a direct child of the root. Each part's origin is the pivot the game
places it by, and its geometry faces forward with no rotation. Where the parts rest in the FBX doesn't matter: they
are laid out apart along +X (the bake layout). There are no LODs.

## Key numbers

The game's rules use the same numbers, read from the `"bridge"` object in `JetBridge.vehicle` (`BRIDGE` in
`model.py`). Heights are relative to each part's floor line.

| | |
|---|---|
| Rotunda | floor 4.0 m (`rotundaFloor`), drum radius 2.3 m (`rotundaRadius`), drum 3.6–6.9 m, roof with parapet to 7.15 m (vent 7.26 m) |
| High rotunda | floor 7.8 m (`upperFloor`); the same drum 3.8 m higher (7.4–10.7 m, parapet 10.95 m, vent 11.06 m); column radius 0.52 m (low: 0.45) with a base sleeve to 1.42 m and two splice flanges. At 7.8 m its tunnel clears a main-deck link (roof 6.75 m) by 0.5 m |
| Tunnels | 13.0 m each (`sectionLength`); Tunnel_A's pivot 2.3 m from the rotunda centre (`tunnelStart`) |
| Short tunnels (2026-10-31) | `ShortTunnel_A/B/C`: the same sections 8.5 m long (`shortSectionLength`), 9.5–22.5 m together, for a bridge close to the aircraft (on a head) that retracts out of its way. A bridge shows one set and hides the other (`JetBridge.tunnel`) |
| Sections (`sections`: width, bottom, top) | A 2.50 / −0.25 / 2.55, B 2.80 / −0.40 / 2.70, C 3.10 / −0.55 / 2.85; walls 0.12 m, so each inside clears the next smaller outside by 3 cm all round |
| Drive yoke | under Tunnel_C, 4.0 m before its outer end (`driveFromEnd`), underside at −0.95 (`yokeBottom`) |
| Drive | frame 2.9 m wide, top at 1.0 m (`legBase`); dual solid tyres, radius 0.43 m |
| Legs | two 0.32 m square columns, 2.4 m apart (centres x ±1.2), modelled 1.0 m tall |
| Cab | 3.4 m wide (`cabWidth`); bumper face 2.6 m ahead of the pivot (`cabReach`); its rear a half circle of radius 1.7 round the pivot; bottom −0.75 (`cabBottom`), roof 3.05, rim 3.18 (`cabTop`): proud of Tunnel_C pitched 1 in 10 all round |
| Link | width 2.6 (`linkWidth`), bottom −0.35 (`linkBottom`), top 2.75 (`linkTop`), modelled 1.0 m long |
| Link column | three parts, so it stands under a link at any height: a foot (footing 1.5 m square, base plate, sleeve to 0.9 m), a 0.48 m square shaft from 0.15 m (`COLUMN_BASE`) to 0.45 m under the link (`COLUMN_SHAFT_TOP`), and a head (2.6 m cross-head, collar, braces) under the link |
| Head | 7.0 m wide (`headWidth`), floor at 4.0 m, underside −0.6 (`headBottom`), flat roof 3.1 m with a parapet to 3.35 (`headTop`); a glass band 0.5–2.55 m. The upper storey (floor 7.8 m) is 6 cm wider (`headUpperWider`), its underside −0.75 (`headUpperBottom`), so it sits on the lower one's roof and hides its parapet. End walls (`HeadEnd`, `HeadUpperEnd`) wrap the storeys' plain ends, the upper one thick enough to cover the lower one's parapet |
| Main link | the `Link` scaled 3.6 / 2.6 across (`JetBridgeRules.MainLinkWidth`), its columns' cross-heads with it |
| Triangles | 9.1k in the file: Rotunda 2.1k, RotundaHigh 2.4k, tunnels 0.5–0.6k each, Drive 1.1k, Cab 0.9k, Link 180, a link column 128 (foot 68, shaft 12, head 48), DriveLegs 24, Head and HeadUpper 140 each, HeadEnd and HeadUpperEnd 144 each. A bridge draws one rotunda: 6.0k (main deck) or 6.3k (upper deck), plus 128 per link column. A head draws 0.6k (two storeys: 1.0k), plus its main link and 128 per column |
| Textures | baked at 4096, saved at 2048 (`BAKE_SIZE`), AO strength 0.6 |
| Colours | cladding `#C8CCD0`, roof `#DADDE0`, trim `#7E858C`, cab `#E6E8E8`, glass `#27313B`, canopy `#151617`, hazard `#F2C200` / `#141414`, steel `#4E545A` |

## Parts and pivots

Object names in `Models/JetBridge.fbx` (all direct children of the root `JetBridge`). Coordinates are Unity local
space (+X right, +Y up, +Z forward).

| Object | Pivot | Built | Game |
|---|---|---|---|
| `Rotunda` | column centre on the ground | drum (round, no openings, so any yaw looks the same) on a round column with base plate | static part; may be yawed freely |
| `RotundaHigh` | column centre on the ground | the same drum with its floor at 7.8 m, on a stouter column | an upper-deck bridge's rotunda instead of `Rotunda` |
| `Tunnel_A` | inner end, centre of the floor line | +Z 0..13; the skin also runs 1.0 m behind the pivot (−Z) into the drum | at `tunnelStart` from the rotunda centre, floor at `rotundaFloor`; pitches about its pivot |
| `Tunnel_B`, `Tunnel_C` | inner end, centre of the floor line | +Z 0..13, open ends with a rubber-lipped frame at the inner end | slide along A's axis |
| `ShortTunnel_A/B/C` | as `Tunnel_A/B/C` | +Z 0..8.5 | as the long ones, on a short bridge |
| `DriveLegs` | bottom centre | Y 0..1 | at the drive's top (y 1.0), scaled in local Y up to the yoke |
| `Drive` | ground centre | frame top at y 1.0 | under the yoke; keep its yaw equal to the legs' (the legs stand on the frame at x ±1.2) |
| `Cab` | rear centre, floor level | +Z −1.7..2.6 (bumper face at 2.6), the rear a half circle round the pivot | at Tunnel_C's outer end; may yaw to face the door |
| `Link` | start, centre of the floor line | +Z 0..1 | at the terminal wall, floor at `rotundaFloor`; scaled in local Z to its length and pitched up to an upper-deck bridge's rotunda |
| `LinkColumnFoot` | ground centre | Y 0..0.9 | under the link, as often as wanted (instantiate copies) |
| `LinkColumn` | bottom centre | Y 0..1, the shaft | on its foot (y 0.15), scaled in local Y up to 0.45 m under the link |
| `LinkColumnHead` | top centre (the link's underside) | Y −1.15..0 | level, under the link (or the head: pairs 2.2 m either side of its centreline); scaled across under a main link |
| `Head` | near end, centre of the floor line | +Z 0..1, the lower storey | at the head's near end, floor 4.0 m, scaled in local Z to its length |
| `HeadUpper` | near end, centre of the floor line | +Z 0..1, the upper storey | the same, floor 7.8 m, on a head with an upper-deck bridge |
| `HeadEnd`, `HeadUpperEnd` | centre of the floor line | Z 0..0.05 (0.08), facing +Z | at the head's far end facing out, and a copy turned round at its near end |

## Use in game

- **Static part:** the static part merges into `Rotunda` (no `_LODn` suffix). With one level, `BuildLodGroup` makes
  no LODGroup.
- **Upper-deck bridge:**
  - Use `RotundaHigh` instead of `Rotunda` (hide the other), and raise Tunnel_A's floor to `upperFloor` (7.8 m).
  - On a head, its stub link is level at 7.8 m, from the upper storey's wall.
  - On its own, the link starts at the terminal at `rotundaFloor` and pitches up to 7.8 m at the drum: 1 in 10 at
    most, so it is at least 38 m long. At 1 in 10 its floor drops 0.13 m across the drum's 1.3 m half-width, inside
    the drum's floor band.
  - The link's columns stretch their shafts to the link's height where they stand; their heads stay level (at 1 in
    10 the link's underside drops 2.4 cm across a cross-head).
  - Everything else is unchanged; the legs simply stretch further: about 5.8 m to a 7.9 m sill, against 2–3 m on a
    main-deck bridge.
- **Head:** one copy of the model per head, showing only `Head` (`HeadUpper`), the two end walls, the `Link` (the
  main link, scaled 3.6 / 2.6 across, ending 0.5 m inside the head's end) and the columns. A bridge's copy hides the
  head parts. A stub link starts 0.3 m inside the head's wall.
- **Link length:** end the link inside the drum. Its corners (x ±1.3) meet the drum surface 0.40 m behind the
  drum's front point, so stop it at least 1.9 m from the rotunda centre (1.6 m is safe). Everything inside 2.3 m is
  hidden in the drum.
- **Tunnel A:** its extra 1 m behind the pivot fills the gap the drum's curve leaves at its corners (0.37 m). It also
  covers the gap at the top of the end face when the tunnel pitches, up to about ±10°.
- **Legs:** stretch them from y 1.0 on the drive up to the yoke underside (`yokeBottom`, −0.95 below Tunnel_C's floor
  line). The yoke's sleeves reach down to −1.07 and up to the tunnel floor (−0.55), so a leg top anywhere in that
  range looks attached.
- **Cab:** its rear is a half circle round the pivot, so however far it turns (up to 95°) Tunnel_C's end corners
  (±1.55) stay inside its round wall, as Tunnel_A's do in the rotunda's drum. It is taller than Tunnel_C at both
  ends (−0.75 / 3.05 against −0.55 / 2.85), so the tunnel's end stays inside it when the tunnel pitches 1 in 10 and
  the cab stays level.
- **Stretched parts (`STRETCH`):** the UVs of `Link`, `Head` and `HeadUpper` (along Z), `DriveLegs` and `LinkColumn` (along Y) collapse
  after the bake onto the middle of each face, so their texture and AO don't vary along the stretch.
- **`bounds` in the profile:** the low rotunda's (`BOUNDS`). The rest of the bridge moves. `RotundaHigh` has the
  same footprint and reaches 11.06 m.

## Regenerate

```sh
cd ArtSource/Vehicles
B=/Applications/Blender.app/Contents/MacOS/Blender
$B -b --factory-startup --python build.py -- --model JetBridge --previews          # ~6.5 min (4K bake)
$B -b --factory-startup --python build.py -- --model JetBridge --geometry --check  # quick untextured look, ~15 s
```

The previews show the bridge assembled by `preview_pose()`, in a docked-looking pose: tunnel extended to 25 m and
pitched 2° down, cab turned square to an imaginary fuselage, link 10.4 m. Views:

- `aerial34`, `side`, `cab`, `top`
- a double-deck gate's head (`HEAD_VIEWS`: `head34`, `headside`, `headend`, `headunder`): a 28 m two-storey head on
  three column pairs, its 20 m main link from the terminal face on one column, a main-deck bridge to a 5.2 m sill and
  an upper-deck bridge on `RotundaHigh` to a 7.9 m sill 12.5 m further aft, both on the short sections, off the
  head's side on 1.2 m stubs.
  `preview_pose` copies the parts it needs twice after the export (preview only).
- `turned`, `turnedin`: the main bridge with its cab turned 85° from the tunnel, from outside and inside the turn
- close-ups for checking joints: `rotunda`, `drive`, `joint` (cab / Tunnel_C), `lip` (Tunnel_B's inner end round
  Tunnel_A)

## Notes

- **Normals check:** `--check` flags A 4, B 8, C 8, Drive 11, Cab 1, RotundaHigh 1. These are false positives:
  - the dark inner skins where they meet the end frames;
  - the one-sided hazard, doorway and floor quads.
- **Detail is geometry and flat colours** in the one baked atlas:
  - grooves along the cladding, the recessed glass bands with posts, roof seams, end frames;
  - the canopy's folds, the hazard stripes.
- **Not modelled:**
  - service stairs and the pre-conditioned-air and ground-power units under the tunnel;
  - a levelling wheel under the cab;
  - lights. The profile's `lights` is empty.
