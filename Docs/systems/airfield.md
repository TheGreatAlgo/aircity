# Airfield: runways, taxiways, aprons, weather

An `Airport` builds its runways, taxiways, service roads, aprons, verges and ground at runtime from a small saved
layout. Only the layout data is serialized; every mesh, paint mark and light is regenerated when something is enabled
or changed. Things that change all the time (wear, snow, grass length, ploughs or mowers at work) are pushed into
vertex channels, never rebuilt. Gameplay rules (lengths, widths, closures, wear, verge widths) live in one place,
`RunwayRules`, and are tested.

Design choices the user made (2026-10-23):

| Topic | Choice |
|---|---|
| Placement | any angle, with a 10° snap |
| Taxiways | straight segments with automatic curves and fillets |
| Lengths | real-world distances |
| Surfaces | dirt, grass, asphalt, concrete; they differ by wear, weather and upkeep, with no weight limits; jets need pavement |
| Lighting | a per-runway upgrade; unlit runways close at night |
| Snow | builds up, must be ploughed |
| Grass | grows, must be mowed; so do the verges (the graded grass of the runway and taxiway strips) |
| Service roads | vehicle-only, white markings, give way at taxiways; aircraft never use them |
| Terrain | flattening under runways is deferred (the ground is flat) |

## Where it lives

| Path | Role |
|---|---|
| `Scripts/Runtime/Airfield/Airport.cs` | Root: materials, aprons, ground, weather (`Condition`, `groundSnow`), `timeScale`, `Simulate`, runway and depot add / remove, `Depots`, `Maintenance`, `Pavement` |
| `Runway.cs` | One runway: thresholds, surface, width, lighting, wear, snow, grass, verge grass, letter; `Check(type)`; rebuild |
| `TaxiwayNetwork.cs` | Taxiway and service-road graph (`TaxiwayNode`, `TaxiwayEdge`), editing (`Connect` splits crossings), routing (aircraft; vehicles with roads preferred and aprons crossable), `Run` (chains of sections for the depots' teams) |
| `Pavement.cs` | Where there's pavement to drive on, from the surface meshes, in a 16 m grid (the ploughs' turns) |
| `RunwayRules.cs` | All gameplay constants and rules: widths, required length, `Check`, snow, grass, wear, designators |
| `StripWork.cs` | `SurfaceValues` (per-owner shader state) and `StripWork` (the ploughing / mowing pass pattern) |
| `AirfieldTypes.cs` | Enums: `PavementSurface` (+ `Surfaces.IsPaved`), `WorkKind`, `RunwayLighting`, `SurfaceCondition`, `AirfieldLightType`, `AirfieldKind`; `AirfieldHeights` |
| `AirfieldPart.cs` | A generated child MeshRenderer (`HideFlags.DontSave`), so meshes are never saved into scenes |
| `Building/MeshData.cs` | Vertex lists (float4 UVs, per-vertex owner); `Geo` 2D helpers (x, z ground plane) |
| `Building/SurfaceBuilder.cs` | Pavement meshes (`Rect`, `Strip`, `Fan`, fringes), `Verges`; `SurfaceState` (rewrites state channels) |
| `Building/PaintBuilder.cs` | Paint marks: boxes, strips, dashes, SDF glyphs; `RunwayFrame` |
| `Building/LightBuilder.cs` | Light glows plus fixture hardware (elevated / inset lights, PAPI units, barrettes, cones) |
| `Building/RunwayBuilder.cs` | ICAO Annex 14 runway layout: surface, shoulders, blast pads, markings, lights |
| `Building/TaxiwayBuilder.cs` | Graph to geometry: curves, junction fillets, centrelines, holding positions, edge lights, stop bars; road markings and give-way lines; taxiway verges |
| `Shaders/AirfieldCommon.hlsl` | Shared lighting wrapper, weather globals, `TouchdownRubber`, `SnowCoverage`, `StripWorked` |
| `Shaders/AirfieldSurface.shader` | Pavement, dirt, grass: world-space photo textures, joints, rubber, wear, wet, snow, grass length / stripes |
| `Shaders/AirfieldPaint.shader` | Paint: SDF coverage, rubber, wear, hidden under snow except where ploughed |
| `Shaders/AirfieldLightGlow.shader` | Camera-facing glows with a minimum pixel size; two-colour by direction, PAPI, flashers |
| `Shaders/AirfieldFixture.shader` | Light hardware; shrinks away beyond its fade distance |
| `Scripts/Editor/Airfield/AirfieldSetup.cs` | Creates the materials (missing ones only) and builds the sandbox airport (`Bootstrap`) |
| `Scripts/Editor/Airfield/AirfieldTexturePostprocessor.cs` | Import settings for `Airfield/Textures` |
| `ArtSource/Airfield/textures.py` | Generates ground textures and the glyph atlas (see [assets/airfield-textures.md](../assets/airfield-textures.md)) |
| `Tests/EditMode/AirfieldTests.cs`, `MaintenanceTests.cs` | Rules, geometry, routing, snow / ploughing, grass / mowing, vehicles; runs, depots, avoidance, verges, roads |

## Data model

- **Space.** Everything is in the Airport's local space, on y = 0. `Geo.XZ` / `Geo.X0Z` convert between `Vector3`
  and the 2D ground plane (`Vector2`(x, z)). Bearings are clockwise from north = +Z.
- **`Runway`** (child GameObject of the Airport):
  - `start` / `end` thresholds. The start designator comes from the heading start → end; north is 36, never 0.
  - L / C / R letters for parallel runways, assigned by `Airport.AssignLetters`.
  - `surface`, `width` (presets in `RunwayRules.RunwayWidths`), `lighting` (unpaved runways are forced to `None`).
  - `wear` 0..1, `snow` cm, `grass` cm (grass strips only), `vergeGrass` cm (its verges).
  - Transient `Work` and `VergeWork` (`StripWork`s) while a depot team is assigned.
- **`TaxiwayNetwork`** (a child, auto-created by `Airport.Taxiways`):
  - **Nodes:** `id`, `position`, and an optional `runway` (the node sits on that runway's centreline).
  - **Edges:** `a`, `b`, `width`, `surface`, `lit`, `wear`, `snow`, `grass`, `road`, `vergeGrass`, plus transient
    `work`, `stripes`, `vergeWork` and `vergeStripes`. A `road` edge is a service road: 7 m (`RunwayRules.RoadWidth`),
    never lit, no verges, and aircraft never route on it.
  - **Verges:** paved taxiways (`HasVerges`) have mown grass out to `RunwayRules.TaxiwayVerge(width)`.
  - **Splitting:** `Connect(a, b, ...)` splits existing edges where they cross and adds runway nodes where the new
    edge crosses a runway, so the graph always matches the pavement.
- **`ApronArea`** structs on the Airport: plain rectangles with no stands yet.
- **Surface submeshes are indexed by `PavementSurface`** (Dirt 0, Asphalt 1, Concrete 2, Grass 3) for taxiways and
  aprons. Runways use submesh 0 for their own surface and submesh 1 for shoulders and blast pads (asphalt).
- **Verges** are separate meshes (a "Verges" part on each runway and on the taxiway network), drawn with the
  GrassStrip material, one owner per runway / edge, with their own `SurfaceState`.

## Verges

The graded grass beside runways and paved taxiways (the strip, in ICAO terms) is mown, so it shows lighter cut grass
and mowing stripes like a grass strip.

| | Half width from the centreline | Beyond the ends |
|---|---|---|
| Paved runway ≥ 29 m wide (code 3 / 4) | 75 m | 60 m |
| Paved runway ≥ 22 m (code 2) | 40 m | 60 m |
| Paved runway, narrower (code 1) | 30 m | 60 m |
| Grass / dirt runway | max(30 m, half width + 12 m) | 30 m |
| Paved taxiway | ICAO graded strip: 10.25 (A), 11 (B), 12.5 (C), 19 (D), 22 m (E); at least 4 m of grass | stops short of junctions |

- **Mesh:** `SurfaceBuilder.Verges` lays two bands, from the pavement edge (runway incl. shoulders) out, with a ragged
  fringe on the outside. uv0.x is measured from the pavement edge (as if the pavement weren't there), so
  `StripWorked` draws the mowers' pattern unchanged. Kind `AirfieldKind.Verge` (6).
- **Heights:** runway verges at 4 mm, taxiway verges at 6 mm (they win where they overlap), both under everything paved.
- **Taxiway verges** stop where they'd run into another arm's verge or a runway's (`TaxiwayBuilder.VergeTrim`), so
  bands never overlap; bends and junctions keep small unmown corners.
- **Growth:** 1 cm a game day while the ground is clear of snow (`groundSnow` < 0.05 cm and not snowing). The depots
  mow them at 15 cm. Verges never close anything.

## Service roads

Edges with `road = true` in the taxiway network:

- **Routing:** aircraft never use them (`FindRoute` leaves them out). Vehicles prefer them: in `VehicleRoute` a road
  metre costs 1, a taxiway 1.25, an apron crossing 1.4 and a runway 1.6.
- **Geometry:** the same strips, curves and junctions as taxiways. Corners where a road meets anything use an 8 m
  radius (`RoadCornerRadius`), not the aircraft-sized taxiway fillets. Curves between two road arms use 2.5 × width.
- **Paint** (paved roads): white edge lines 0.3 m in, white centre dashes (3 m dash, 6 m gap). Where a road meets
  a taxiway (`GiveWayAt`), the approach lane gets a double dashed give-way line and a give-way triangle pointing at
  the driver. Before a runway, a solid stop line at the holding distance. Taxiway centrelines never connect across
  a road arm.
- **Never lit:** `SetLit` and `Connect` keep roads unlit.

## Gameplay rules (RunwayRules)

- **Required length:** `max(takeoff × fx, landing × fy)` from `AircraftType` performance. Factors (fx, fy) by surface:

  | Surface | Factors |
  |---|---|
  | Paved, dry | (1, 1) |
  | Paved, wet | (1, 1.15) |
  | Paved, snow over 0.3 cm (contaminated) | (1.15, 1.4) |
  | Dirt | 1.15 dry, 1.3 slippery |
  | Grass, dry | (1.2, 1.15) |
  | Grass, slippery | (1.3, 1.35) |

  On grass longer than 15 cm, fx is a further × 1.15.
- **`Check` order:** pavement → width (ICAO by size code) → ploughing → mowing → snow-closed → grass-closed →
  length → wear ≥ 1 → unlit at night. It returns the first `RunwayProblem` found. `RunwayCheck.ToString()` gives the
  UI text ("needs 2,100 m").
- **Snow:**
  - Falls at 3 cm/h. It melts at 0.5 cm/h, or 2 cm/h in rain.
  - Closes a runway at 3 cm paved, 1 cm unpaved.
  - The depot auto-ploughs at 1.5 cm paved, 0.6 cm unpaved.
- **Grass:**
  - Cut to 5 cm.
  - Grows 1 cm per game day, only while no snow is lying and it isn't snowing.
  - Long at 15 cm, which is also when auto-mowing starts. Closes at 25 cm.
- **Wear per movement:** `rate × ((MTOW / 50 t)^1.5 + 0.02)`.

  | Surface | Rate | About this many movements to wear out |
  |---|---|---|
  | Dirt | 0.071 | 600 light aircraft |
  | Grass | 0.05 | 850 light aircraft |
  | Asphalt | 1.27e-4 | 4,000 narrowbody |
  | Concrete | 0.42e-4 | 12,000 narrowbody |

  Wet unpaved surfaces wear at × 2. Taxiing wears taxiways at half the runway rate.
- **Taxi routing:** `FindRoute(from, to, AircraftType)` uses taxiways (never roads) the type fits that aren't
  closed. Runway segments between runway nodes cost × 1.5, so back-taxiing is the last resort.
- **Vehicle routing:** `VehicleRoute(starts, targets, blocked)` and `FindVehicleRoute` ignore all closures and are
  used by the depots' vehicles. They take several start nodes, each with a cost already spent, and the nearest of
  several targets. Links blocked by a parked aircraft are left out.
- **Night:** `Airport.IsNight` is `AircraftLights.Glow > 0.5`. That global is set by the dev "N" key and
  `SceneCapture -captureNight`.

## How it builds

1. `Runway.Rebuild()` / `TaxiwayNetwork.Rebuild()` / `Airport.BuildGround()` create a `SurfaceBuilder`,
   `PaintBuilder` and `LightBuilder` and run the matching `*Builder`.
2. Each `AirfieldPart` receives its mesh and materials:

   | Part | Material |
   |---|---|
   | Surface | per surface |
   | Paint | `Paint` |
   | LightGlows | `LightGlow` |
   | Fixtures | `Fixture` |
3. `SurfaceState.Capture(...)` remembers each vertex's owner, then `RefreshState()` writes the current values.
4. `Airport.RunwaysChanged()` reassigns letters and rebuilds every runway and the taxiways (taxiway joints depend on
   runway positions). `OnValidate` marks things dirty, and `Update` rebuilds them in edit mode too
   (`[ExecuteAlways]`).

**TaxiwayBuilder** lays out each node:

- **Curve:** a node with exactly two edges of the same width and surface becomes a curve of radius
  `2.5 × width` (shortened on short edges). The arc is sampled every 2 m, and its end edges are laid square to the
  straights (along the arms, not the end chords), so the two meet without a wedge-shaped gap.
- **Junction:** any other node becomes a junction polygon with fillets between neighbouring arms, fan-triangulated.
  Triangles are flipped where the outline doubles back.
- **Runway node:** the runway counts as two wide arms, so taxiways flare into it. The flare lies under the runway,
  which is stacked higher.
- **Junction surface:** the majority surface of its edges; ties go to the better surface.
- **Holding positions:** at 30 / 60 / 75 m by runway width, with red stop bars on precision-lit runways.

**RunwayBuilder** (ICAO Annex 14):

- **Paint:**
  - Threshold stripes, counted by width.
  - Designation glyphs, with letters.
  - Centreline: 30 m dashes with 20 m gaps.
  - Side stripes, aiming point and touchdown zone marks.
  - Blast pads (60 m) with chevrons on runways 30 m and wider.
- **Basic lighting:**
  - Edge lights every 60 m or less. The last 600 m are yellow on precision runways.
  - Green / red threshold and end lights.
  - PAPI.
- **Precision lighting** adds:
  - Centreline lights (red / white near the end).
  - Touchdown-zone lights.
  - Approach lights out to 720 m with sequenced flashers.
- **Unpaved runways:** a fringed surface with orange / white edge cones and no paint.

**Layer stacking.** Overlapping pavements aren't cut against each other. They are stacked a few mm apart, all
below the 3 cm aircraft contact shadow:

| Layer | Height |
|---|---|
| Ground | 0 |
| Runway verges / taxiway verges | 0.004 / 0.006 |
| Shoulder | 0.010 |
| Apron | 0.015 |
| Taxiway | 0.020 |
| Runway | 0.024, + 0.002 × `Runway.Layer` for crossing runways |
| Paint | 0.031 |
| Vehicle light pools | 0.045 |

## Vertex layouts (keep in sync with the shaders)

**Surface (AirfieldSurface)**

| Channel | Contents |
|---|---|
| uv0 | (x across, s along). Junction, apron and ground use world xz. Verges: x from the pavement edge. |
| uv1 | (`AirfieldKind`, runway length). A length of 0 means no tyre rubber. |
| color | r wear, g snow cm, b grass cm, a fringe coverage |
| uv4 | work: see `StripWorked` |

**Paint (AirfieldPaint)**

| Channel | Contents |
|---|---|
| uv0 | (across, along from the mark's centre, snow cm, unused) |
| uv1 | box (half width, half length, along-centre, kind 0 box / 1 glyph) |
| uv2 | glyph atlas rect |
| uv3 | (strip along, strip across, runway length, wear) |
| uv4 | work |
| normal / tangent | which way each corner grows by a pixel (anti-aliasing) |

**Glows (AirfieldLightGlow)**

| Channel | Contents |
|---|---|
| color | colour seen from behind |
| uv1 | colour seen from ahead |
| uv2 | (size, type 0 steady / 1 PAPI / 2 flasher, param) |

Brightness follows the global `_AirCityLightGlow`.

**Owners.** `SurfaceState.Apply(owner → SurfaceValues)` rewrites only the state channels. It's cheap enough to run
every frame. For taxiways, an owner is either one edge's straight or a joint (curve or junction) covering several
edges. A joint shows max wear, min snow and min grass across its edges, and never shows a work front.

## Shader features

**Surface**

- **Textures:**
  - World-space CC0 photo textures, sampled twice with a rotated second sample to hide the repeat.
  - A macro noise texture for large-scale variation.
  - Worn textures blended by `wear` with noise.
- **Pavement detail:** slab joints taken from uv0 (concrete 7.5 × 7.5 m, asphalt lanes 5 m) and tyre rubber in the
  touchdown zones.
- **Wetness:** `_AirfieldWetness` darkens porous surfaces and adds puddles.
- **Snow:** `SnowCoverage(noise, depth)` with depth taken from:

  | Kind | Snow depth |
  |---|---|
  | Ground, verge | `_AirfieldSnow` (ground snow) |
  | Apron | 15% of `_AirfieldSnow` |
  | Runway, taxiway | the owner's `snow`, or 0 where `StripWorked` says the ploughs have been |
- **`_FRINGE`:** ragged alpha-clipped edges on dirt and grass strips.
- **`_GRASS_LENGTH`** (used by the GrassStrip and Grass materials):
  - Long grass is darker, with seed heads.
  - Cut grass is lighter.
  - Mowing stripes turn with the view direction, computed from screen derivatives of the strip's along axis, and
    fade as the grass regrows.
  - The ground is treated as 15 cm grass.

**Paint**

- Box and glyph coverage is analytic / SDF, and quads grow by one pixel.
- Alpha blended, `Offset -1,-1`, queue Transparent-150.
- Hidden under snow using the same noise as the surface, so marks vanish exactly where the snow lies.

## Common tasks

**Add a surface type**

1. Add the enum value (append it; values are serialized as ints).
2. Bump `Surfaces.Count`.
3. Update `IsPaved`, `Airport.SurfaceMaterial` and its material field.
4. Add an entry to `AirfieldSetup.Surfaces` and `AssignMaterials`.
5. Pass the extra material in `TaxiwayNetwork.Rebuild` and `Airport.BuildGround`.
6. Update the junction-majority order in `TaxiwayBuilder.BuildJunction`.
7. Add the rules in `RunwayRules` (`LengthFactors`, `WearPerMovement`, snow closing) and update the tests.

**Change a rule constant.** Edit `RunwayRules` only, then rerun the tests. The docstrings there give the
real-world source.

**Add a runway marking or light.** Edit `RunwayBuilder.Paint` / `Lights`. Set `paint.frame` so rubber and plough
clearing line up. Keep marks inside the runway's owner (owner 0).

**Change shader state.** Edit `SurfaceValues`, `SurfaceState.Apply`, both shaders' attributes, and this doc's
vertex layout section.

**Add a material property.** Materials are created once by `AirfieldSetup.CreateMaterials` (missing ones only, so
hand-tuning survives). To pick up new defaults, delete the `.mat` (keep the `.meta`) and rerun the bootstrap.

**Add a service road.** `net.Connect(a, b, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, road: true)`,
then `net.Rebuild()`; in the sandbox, build mode **Road**.

**Rebuild the sandbox airport.** Run **AirCity ▸ Airfield ▸ Add Sandbox Airport**, or the batch command in
[dev-tools.md](dev-tools.md). It contains:

- 09/27: 2,800 m, concrete, precision lighting, with a parallel taxiway, a high-speed exit and apron links.
- 13/31: 1,600 m, asphalt, basic lighting, crossed by a taxiway.
- A 600 m dirt strip.
- A 700 m grass strip 06/24, grown to 19 cm, with a grass taxiway.
- Verges at various lengths (the main runway's at 14 cm, nearly due).
- The depot west of the apron (3 ploughs, 1 mower inside) on a small pad, with service roads east to the apron and
  north across the dirt taxiway to the parallel taxiway.

## Invariants and gotchas

- **Never save generated meshes.** `AirfieldPart` uses DontSave. Scenes store only the layout.
- **HLSL reserved word:** `pass` is reserved. A variable named `pass` broke every airfield shader on Metal, and
  everything rendered magenta.
- **Metal:** `UNITY_MATRIX_P[1][1]` is negative when rendering to a texture on Metal. Pixel-sized quads (paint
  growth, glows) need `abs()`.
- **Pass-pattern twins:** `StripWork.Worked` (C#) and `StripWorked` (HLSL) must agree, and so must
  `ECHELON_GAP_PER_LANE` / `EchelonGapPerLane`, `GRASS_CUT_CM` / `CutGrass` and the centred flag (lane width
  + 100, `StripWork.CentredFlag`).
- **Pavement map:** `Airport.Pavement` is rebuilt lazily after `LayoutChanged()`, which `Runway.Rebuild`,
  `TaxiwayNetwork.Rebuild` and `BuildGround` call. Generated meshes are assumed to sit in Airport space (runway and
  taxiway objects at identity under the Airport).
- **Taxiway coordinates:** an edge's strip frame starts at node a (uv0.y = s from a). The edge's work state and
  paint `frame` rely on this.
- **Runway nodes:** a taxiway node on a runway is re-snapped to the centreline on every taxiway rebuild.
  `ForgetRunway` turns its nodes into ordinary junctions when the runway is removed.
- **Rebuild order:** after `Capture`, call `RefreshState()`. Otherwise uv4 (work) stays empty until the next state
  change.
- **Time:** `Airport.timeScale` is game seconds per real second (default 1). Weather, grass and vehicles all use
  it, so a higher value fast-forwards everything together. There is no game clock yet.

## Tests and verification

`AirfieldTests`:

| Test | What it checks |
|---|---|
| `Performance_*` | aircraft performance data feeding the rules |
| `Designators_*` | runway numbers from the heading |
| `ParallelRunways_*` | L / R letters |
| `Qualification_*` | length, surface, width, weather, night, wear, snow, grass |
| `Wear_*` | design lives |
| `Snow_*` | build-up and closure; a real depot team ploughing the runway, then the taxiways, then going home; melting |
| `Ploughing_OnePloughTakesLongerThanAnEchelon` | about 55 min vs 21 min |
| `StripWork_*` | the pass pattern |
| `GrassStrip_*` | growth, closure and mowing |
| `Runway_BuildsSurfacePaintAndLights` | generated runway geometry |
| `Taxiways_*` | split crossings |
| `TaxiRouting_*` | routes respect width and surface |
| `SandboxAirport_*` | no down-facing or NaN triangles (surfaces, paint, verges), depot and road present |
| `Vehicles_*` | vehicle import |

Commands are in [dev-tools.md](dev-tools.md). Look at results with SceneCapture or a temporary editor script that
calls `SceneCapture.Capture(cam, path, w, h)`; delete the script afterwards.
