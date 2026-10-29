# ArtSource

Scripts that generate the game's art. The outputs go straight into `Assets/AirCity/`. Regenerate rather than hand-edit.

## Aircraft

Every aircraft type is built procedurally in Blender (5.2+) by one shared pipeline (`Aircraft/Common`) from a
small model definition (`Aircraft/<Model>/model.py` + `parts.py`). There are two stages:

| Stage | Script | Time | Does |
|---|---|---|---|
| 1 | `build.py --texture` | 2–4 min | geometry, UV atlas packing, Cycles bakes (position / normal / part / AO) → `<Model>/Build/work/` |
| 2 | `finish.py` | ~20 s | livery masks, shading / dirt / logo maps, registration patch + font, LODs, collider, ground shadow, FBX, `<Model>.aircraft` |

```sh
cd ArtSource/Aircraft
B=/Applications/Blender.app/Contents/MacOS/Blender
$B -b --factory-startup --python build.py -- --model Narrowbody --texture --check     # only after geometry/UV changes
$B -b --factory-startup --python finish.py -- --model Narrowbody --previews --liveries ClassicBlue,SunsetRed --views front34,rear34
```

- `--check` flags parts whose face winding is inverted. Unity culls back faces, so inverted parts disappear.
  Inner surfaces such as a jet's fan duct are reported too; that's expected.
- `--previews` renders Eevee previews into `<Model>/Build/previews/`. Views are listed in the model's `VIEWS`.
- Geometry-only look: `$B -b --factory-startup --python build.py -- --model Narrowbody --preview --cull --views side_ortho,front34`
- `Build/` is scratch output and is gitignored.

After regenerating, focus Unity (or run the bootstrap command in the root README). The importers pick up the new
FBX, textures and type data automatically.

### Layout

| File | Purpose |
|---|---|
| `build.py` / `finish.py` | the two stages, for any `--model` |
| `Common/pipeline.py` | both stages: UV rules, bakes, registration patch, LODs, collider, ground shadow, FBX, type data |
| `Common/geom.py`, `shapes.py`, `fuselage.py` | mesh builder, NACA airfoils, lifting surfaces, revolve / tube / wheel, superellipse fuselage loft |
| `Common/paint.py` | livery composition, detail / dirt / logo maps, registration text (mirrored by the C# compositor) |
| `Common/airlines.py` | example airlines and their procedural tail logos (written to `Assets/AirCity/Liveries/Logos`) |
| `Common/font.py` | registration glyph atlas (Blender's bundled Inter, SIL OFL) |
| `<Model>/parts.py` | that aircraft's geometry |
| `<Model>/model.py` | the model definition (below) |

### What a model definition provides

`model.py` is a plain module. See `Cessna172/model.py` (light single) and `Narrowbody/model.py` (twinjet).

- `NAME`, `DISPLAY_NAME`, `CATEGORY` (`AirCity.Aircraft.AircraftCategory`), `S_REF`, `Fuselage`, `AO_DISTANCE`.
- `build()` → `(body MeshBuilder, [Mover, ...])`. Movers are separately animated parts; the runtime finds them by
  name: `Propeller` / `Fan_*` spin; `Rudder*`, `Elevator*`, `Aileron_L*` / `Aileron_R*`, `Flap_L*` / `Flap_R*`
  deflect; `NoseGear` steers; the wheels named in `GEAR` roll. Give a mover a `hinge` direction when its hinge
  isn't along an axis.
- `LOD_RATIOS`, `LOD1_KEEP`, `COLLIDER_RATIO`; UV rules `UV_TOP_BOTTOM`, `UV_SIDE`, `DENSITY`, `BOTTOM_DENSITY`.
- `SCHEMES`: a mask function for **every** `AirCity.Aircraft.LiveryStyle`. R, G and B mark zones 1, 2 and 3 over
  the base colour, with the same meaning on every type (zone 1 = main stripe / swoosh / belly / nose, zone 2 =
  accent / cheatline / tips, zone 3 = trim / tail / engines). Then one airline livery fits its whole fleet.
- `REG_BOX` per scheme (clear of its stripes), `REG_FACES_U/V`, `REG_TEX_W`: the registration patch.
- `LOGO_BOX`: the tail-logo area on the fin (station from / to, height from / to).
- `panel_lines(p)`, `detail_extra(p)`, `dirt_extra(p)`: shading and grime specific to the type.
- Gameplay data in Blender space (the pipeline converts it to Unity space):
  - `GEAR`: wheels (name, axle, radius, steerable), steering pivot, max steering angle;
  - `POINTS`: doors, cargo doors, fuel, ground power, water, lavatory, tow bar, chocks, tie-downs;
  - `LIGHTS`: navigation, strobe, beacon, landing, taxi and logo lights;
  - `PERFORMANCE`: takeoff and landing distance (metres, at maximum weight, sea level), maximum takeoff mass,
    and whether it can use unpaved (dirt) runways. Runways qualify aircraft from these.
- `VIEWS`: preview cameras.

The painted materials must be `Body` and `Registration`. Every other material must be a flat colour from
`pipeline.MATS`; Unity's importer merges those into one `Trim` palette material, so a renderer draws at most three.

Unity imports this FBX setup as Blender (x, y, z) → Unity (x, z, y), so `pipeline.turn_around` rotates each
aircraft 180° for export. The nose then faces Unity +Z and starboard is +X, the frame all `.aircraft` positions
use. `AircraftTypeTests.Model_MatchesTypeData` fails if the mesh and the data disagree.

The C# compositor reproduces `paint.compose` exactly; `AircraftLiveryTests.BodyTexture_MatchesGeneratorReference`
checks the two against each other for every type.

## Airfield

`Airfield/textures.py` (plain Python 3: numpy, Pillow, scipy) makes the ground textures in
`Assets/AirCity/Airfield/Textures`: albedo / normal / mask sets for asphalt, concrete, dirt and grass (each with a
worn variant), the macro-variation noise, and the runway-designation distance-field glyphs.

```sh
python3 ArtSource/Airfield/textures.py            # everything (downloads sources into Airfield/Build/ once)
python3 ArtSource/Airfield/textures.py Glyphs     # just one set: Asphalt, Dirt, ..., Macro, Glyphs
```

The photo sources are CC0 (no attribution required): Poly Haven `clean_asphalt`, `asphalt_02`, `concrete_floor_03`,
`cracked_concrete_02`, `dirt`, `brown_mud_02`; ambientCG `Grass004`. The script flattens their large-scale
blotches (so they tile over kilometres) and matches each set's mean colour. Glyph shapes are drawn as strokes in
`glyph_strokes()`; keep `CHARS` / cell sizes in sync with `PaintBuilder`.

## Vehicles

`Vehicles/` builds the airport's maintenance vehicles and tugs, their depot and the control towers in Blender, lighter than the aircraft pipeline:
no liveries, one stage. Every material is a flat colour; the pipeline unwraps all the parts into one atlas, bakes
the colours, metallic / smoothness and ambient occlusion with Cycles, and adds grime (road salt on the plough, grass
on the mower). So a vehicle draws with one material.

```sh
cd ArtSource/Vehicles
B=/Applications/Blender.app/Contents/MacOS/Blender
$B -b --factory-startup --python build.py -- --model SnowPlough --previews            # ~1 min (Depot ~2 min)
$B -b --factory-startup --python build.py -- --model Mower --geometry --views front34  # quick untextured look
```

| Model | What |
|---|---|
| `SnowPlough` | cab-forward 4x4 runway plough-sweeper: 6.1 m blade at 30°, angled mid-mount broom, air-blast nozzle |
| `Mower` | cab tractor with a 4.6 m batwing rotary mower |
| `TugSmall` / `TugLarge` | towbarless aircraft tugs (U-shaped, nose-wheel cradle between the rear legs) for narrow-bodies (6 m) and up to an A380 (10 m); shared parts in `Common/tug.py` |
| `Depot` | steel-clad maintenance garage: four roller doors, office end, flood lights |
| `TowerSmall` / `TowerMedium` / `TowerLarge` | control towers for a grass airfield (cab on a two-storey office, 13.6 m), a regional airport (square shaft, 38 m) and a hub (round Schiphol-like shaft, radome, 99 m); shared parts in `Common/tower.py` |

Output: `Assets/AirCity/Vehicles/<Model>/Models/<Model>.fbx` (LOD0 = body + wheels / broom as separate movers,
LOD1 / LOD2 merged and decimated), `Textures/<Model>_Albedo.png` and `_MetalGloss.png`, and `<Model>.vehicle`
(VehicleType: bounds, wheels, spinners, lights, work figures).

- Modelling helpers are in `Common/kit.py` (bevelled boxes, cylinders, spheres, prisms, lofts, truck wheels). Vehicle space
  is Blender space as is: +X right, +Y forward, +Z up, origin on the ground under the rear axle. The FBX maps it to
  Unity +X right, +Y up, +Z forward, so there's no turn-around step (unlike aircraft).
- A `model.py` provides `NAME`, `DISPLAY_NAME`, `KIND` (`AirCity.Vehicles.VehicleKind`), `MATS` (extra flat colours),
  `build()` → `[kit.Part]` (the body and each mover), `MOVERS`, `WHEELS`, `SPINNERS`, `LIGHTS` (type `Head`, `Tail`,
  `Beacon`, `Work`, `Marker` or `Flood`), `WORK` (lane width, speeds, turn radius, spray points), `LOD_RATIOS`,
  `AO_DISTANCE`, optional `grime(pos, ao)`, optional `TOW` (tugs: tow masses, cradle point, lift, opening, lift part), and `VIEWS`.

