# Control towers: TowerSmall, TowerMedium, TowerLarge

Three control towers in increasing size, for a grass airfield, a regional airport and a hub. They are buildings,
built by the vehicle pipeline like the depot (`KIND = "Tower"`, `VehicleKind.Tower`), and share their parts through
`ArtSource/Vehicles/Common/tower.py`:

- polygon outlines and offsets;
- the glazed cab, with glass leaning out 15° at the top as real cabs do;
- window ribbons, windows, railings, steel stairs, antennas, a wind mast, lamp housings and grime.

| Model | Looks like | Height | Footprint |
|---|---|---|---|
| `TowerSmall` | A two-storey rendered flight office with a glazed cab on the roof and a red-and-white chequered cab roof edge (after Freiburg EDTF). It has a roof terrace with railings, an external steel stair up the left side, a radio mast and a wind mast. | cab floor 6.4 m, 13.6 m to the light | 12.4 × 8.9 m |
| `TowerMedium` | A two-storey ATC office block with a square chamfered concrete shaft rising out of its right end, with a glazed stair slot up the front. On top: a flared equipment floor with a window ribbon, a catwalk, and an octagonal cab. The roof carries the aerodrome beacon and antennas. | cab floor 29.3 m, 38 m to the light | 18.2 × 12.8 m |
| `TowerLarge` | A three-storey rounded ATC centre with a glazed entrance, and a round shaft tapering from 10.8 to 8.8 m wide (after Schiphol). The two-tier head has equipment floors with a window ribbon, a terrace round a dark neck, a 12-sided cab, and a radar dome on the cab roof. The shaft has a glazed lift slot up the front and a service riser up the back. | cab floor 86 m, 99.4 m to the light | 46.1 × 31.7 m |

## Key numbers

| | |
|---|---|
| Origin | On the ground at the middle of the building's front. The building lies behind it (−Z), and +Z faces the airfield. |
| Triangles (LOD0) | Small 4.8k, medium 8.6k, large 16k. LOD ratios 0.35 / 0.1, one material each. |
| Textures | Baked at 4096 and saved at 2048 (`BAKE_SIZE`), AO strength 0.55. |
| Lights | Red obstruction lights (`Marker`): one on top of each tower, four round the medium tower's head, and on the large tower four round its head and four half way up its shaft. The medium tower also has the aerodrome beacon: `Beacon_White` and `Beacon_Green` at one point, half a turn apart, so the flashes alternate white and green. Entrance / front flood lights with ground pools. |
| Colours | Cab glass `#2B4651`, frames `#30353A`, the cab fascia `#2C3136`. Small: render `#E7E4DC`, chequer `#C8231C` / white. Medium: concrete `#CDCBC4`, cladding `#6F767C`, blue band `#2F5E8C`. Large: concrete `#D2D1CC`, panels `#D9DCDD`, radome `#EEEEEA`. |

## Use in game

- Prefabs: `Assets/AirCity/Vehicles/Tower*/Tower*.prefab`, created by **AirCity ▸ Vehicles ▸ Create Missing
  Prefabs** (`VehicleSetup.Towers` lists them, smallest first). Each prefab has `VehicleLights` only, with no
  `Vehicle`, since `VehicleType.IsBuilding` is true.
- Lights: `beacon` and `beaconOnlyAtNight` are on. The beacon turns from dusk to dawn (`AircraftLights.Glow` >
  0.3), and the obstruction lights come on with it. Flood lights follow the night as on the depot. Headlights and
  work lights are off.
- Nothing places the towers yet: there's no build tool and no ATC. Drop a prefab into a scene to use one.
  `TowerTests` checks the type data (three sizes standing on the ground, a red obstruction light on top, the
  beacon pair) and that the prefabs light up only at night.

## Regenerate

```sh
cd ArtSource/Vehicles
B=/Applications/Blender.app/Contents/MacOS/Blender
$B -b --factory-startup --python build.py -- --model TowerSmall --previews                      # about 1 min each
$B -b --factory-startup --python build.py -- --model TowerLarge --geometry --views head,base     # quick untextured look
```

Views: `front34`, `rear34`, `overview` on all three. The small tower also has `stair` and `cab`; the medium and
large towers have `head` and `base`.

## Notes

- **References:** the hub tower's proportions follow photos of Schiphol's tower: a slender plain shaft, a wide
  lower head with one glass band, the cab above it, and a radome on top. The small tower's cab and chequered edge
  follow Freiburg EDTF. The medium tower is a generic modern regional tower.
- **Glass is opaque:** dark, glossy and reflective, the same as the depot's windows. There's no interior.
- **Normals check:** `--check` flags 12 / 17 / 39 faces. These are false positives: internal faces where separate
  solids touch (chequer blocks, stacked shaft sections), and the undersides of the head flare and cab fascia. Their
  stored normals correctly point down and out; the check merges touching solids before recomputing them.
- **Possible later additions:** lit cab and office windows at night (needs an emission map in the vehicle
  pipeline), signage, a beacon on the small and large towers if wanted.
