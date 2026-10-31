# Airfield: runways, taxiways, stands, aprons, weather

An `Airport` builds its runways, taxiways, service roads, stands and de-icing bays, aprons, verges and ground at
runtime from a small saved layout. Only the layout data is serialized; every mesh, paint mark and light is regenerated when something is enabled
or changed. Things that change all the time (wear, snow, grass length, ploughs or mowers at work) are pushed into
vertex channels, never rebuilt. Gameplay rules (lengths, widths, closures, wear, verge widths) live in one place,
`RunwayRules`, and are tested.

Design choices the user made (2026-10-23):

| Topic | Choice |
|---|---|
| Placement | any angle, free by default; Shift gives 10° / 5 m steps (2026-10-27: the user found always-on snapping too much) |
| Taxiways | straight segments with automatic curves and fillets |
| Lengths | real-world distances |
| Surfaces | dirt, grass, asphalt, concrete; they differ by wear, weather and upkeep, with no weight limits; jets need pavement |
| Lighting | a per-runway upgrade; unlit runways close at night |
| Snow | builds up, must be ploughed |
| Grass | grows, must be mowed; so do the verges (the graded grass of the runway and taxiway strips) |
| Service roads | vehicle-only, white markings, give way at taxiways; aircraft never use them |
| Terrain | flattening under runways is deferred (the ground is flat) |

Stands, 2026-10-24:

| Topic | Choice |
|---|---|
| Attaching | stands open onto either side of any taxiway; there is no separate apron-lane object (a taxiway lined with stands *is* the apron taxilane). Since 2026-10-27 stands can be built first, anywhere, and connected afterwards by a taxiway laid along their front line ("most people place the stands first") |
| Angles | any angle from 25° to 155° to the front line (90° square on, past it leaning the other way); 90°, 60°, 45° and 30° are one click, keeping the lean; Shift+scroll turns it (2026-10-27) |
| Sizes | ICAO codes A–F by wingspan; each stand shows its code (painted letter), the largest aircraft it takes |
| Leaving | per stand: push-back (nose in) or taxi-out (nose out, needs about twice the room) |
| Grass | a surface choice, for codes A and B only |
| De-icing | drive-through bays on a taxiway, with lanes for the trucks either side |
| Old apron rectangles | kept as open tarmac (`ApronArea`); since 2026-10-26 aprons are free-placed polygons |
| Later | hangars and jet bridges attach to stands; aircraft parking and taxiing to them |

## Where it lives

| Path | Role |
|---|---|
| `Scripts/Runtime/Airfield/Airport.cs` | Root: materials, aprons, ground, weather (`Condition`, `groundSnow`), `timeScale`, `Simulate`, runway and depot add / remove, `Depots`, `Maintenance`, `Pavement` |
| `Runway.cs` | One runway: thresholds, surface, width, lighting, wear, snow, grass, verge grass, letter; `Check(type)`; rebuild |
| `TaxiwayNetwork.cs` | Taxiway and service-road graph (`TaxiwayNode`, `TaxiwayEdge`), editing (`Connect` splits crossings), routing (aircraft; vehicles with roads preferred and aprons crossable), `Run` (chains of sections for the depots' teams); stands (`AddStand`, `Layout`, `Fits`, `CheckStand`, `StandsFor`) |
| `Stand.cs` | `Stand` (saved: kind, number, code, surface, exit, position, heading, `bridges`, `head`) and `StandLayout` (pad, clearances, stop bar, taxi-out hairpin, frontage, `ParkPosition`) |
| `Terminal.cs` | `Terminal` (saved: corners, a bulge per edge, floors): outline with arcs, `Contains`, `ClosestOnWall` |
| `JetBridge.cs` | `JetBridge` (saved per stand: door, rotunda, root), `BridgeHead` (saved per stand), `JetBridgeRules`, `BridgePose`, `BridgeLayout` (`Dock`, `Parked`, `ParkAll`, `Columns`, `Shape`, `Placement`, `Check`, `Suggest`), `BridgeSite`, `BridgeShape` / `Solid` (clearance between bridges), `BridgeProblem` |
| `HeadLayout.cs` | A stand's bridge head laid out: walls, main link, columns, `Shape`, `Placement`; `Suggest` (a head and its bridges), `SuggestBridge` |
| `AirportBridges.cs` | `Airport.Bridges`: bridges docking and pulling back in game seconds, `Clear(aircraft)`, `SetBridges(stand, n)`, `Check` / `FirstProblem`, `HeadOf(stand)` |
| `BridgeRig.cs` | On the Airport: the JetBridge model per bridge, its parts placed from the pose each frame; a copy per head showing its head parts |
| `Building/TerminalBuilder.cs` | Terminal walls (plinth, glass by floor, spandrels, mullions), parapet, roof, plant rooms |
| `Shaders/Building.shader` | Terminals: flat colours, glass tint with a lit hall at night, casts shadows |
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
| `Building/TaxiwayBuilder.cs` | Graph to geometry: curves, junction fillets, centrelines, holding positions, edge lights, stop bars; road markings and give-way lines; taxiway verges (left out where stands open onto a side) |
| `Building/StandBuilder.cs` | Stand and de-icing bay pads and paint: lead-in / lead-out lines, stop bars, red boundaries, numbers and code letters |
| `Shaders/AirfieldCommon.hlsl` | Shared lighting wrapper, weather globals, `TouchdownRubber`, `SnowCoverage`, `StripWorked` |
| `Shaders/AirfieldSurface.shader` | Pavement, dirt, grass: world-space photo textures, joints, rubber, wear, wet, snow, grass length / stripes |
| `Shaders/AirfieldPaint.shader` | Paint: SDF coverage, rubber, wear, hidden under snow except where ploughed |
| `Shaders/AirfieldLightGlow.shader` | Camera-facing glows with a minimum pixel size; two-colour by direction, PAPI, flashers |
| `Shaders/AirfieldFixture.shader` | Light hardware; shrinks away beyond its fade distance |
| `Scripts/Editor/Airfield/AirfieldSetup.cs` | Creates the materials (missing ones only) and builds the sandbox airport (`Bootstrap`) |
| `Scripts/Editor/Airfield/AirfieldTexturePostprocessor.cs` | Import settings for `Airfield/Textures` |
| `ArtSource/Airfield/textures.py` | Generates ground textures and the glyph atlas (see [assets/airfield-textures.md](../assets/airfield-textures.md)) |
| `Tests/EditMode/AirfieldTests.cs`, `StandTests.cs`, `MaintenanceTests.cs` | Rules, geometry, routing, snow / ploughing, grass / mowing, vehicles; runs, depots, avoidance, verges, roads |
| `Tests/EditMode/TerminalBridgeTests.cs` | Terminal arcs and what they may stand on; the bridge model against the rules; placing, docking, pulling back before push-back |

Roads and aprons, 2026-10-26:

| Topic | Choice |
|---|---|
| Road classes | `RoadClass` Service / Street / Highway, told apart in the data now (`RunwayRules.Road`); only service roads can be built until there is road traffic |
| Aprons | free-placed polygons of any surface: click corners, or drag a rectangle |
| Roads on aprons | one Road tool, no modes: across a paved apron a road is only its paint, elsewhere it lays its own pavement (which a road will need in tunnels and on bridges) |
| Taxiways on aprons | unchanged: they still lay their own strip (paint-only taxilanes may follow) |
| Where roads live | service roads stay edges of the taxiway network: they cross taxiways at grade and the depots' vehicles drive both. Where streets and highways live is decided when road traffic comes |
| Tunnels and bridges | later: nodes get a height, and only edges at one level split where they cross |

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
    `work`, `stripes`, `vergeWork` and `vergeStripes`. `road` is a `RoadClass`: `None` is a taxiway, anything else
    a road (`IsRoad`), never lit, with no verges, and aircraft never route on it. (It was a bool; saved `1`s load
    as `Service`.)
  - **Verges:** paved taxiways (`HasVerges`) have mown grass out to `RunwayRules.TaxiwayVerge(width)`.
  - **Splitting:** `Connect(a, b, ...)` splits existing edges where they cross and adds runway nodes where the new
    edge crosses a runway, so the graph always matches the pavement.
  - **Stands:** `stands` (see [Stands and de-icing bays](#stands-and-de-icing-bays)).
- **`ApronArea`** on the Airport (`aprons`): an `outline` (a simple polygon, corners counter-clockwise, Airport
  space) and a `surface`. Stands draw over them; roads run under paved ones. Built with `Airport.AddApron(corners,
  surface)` (null if `ApronProblem` objects: under three corners, crossing edges, under 100 m², or overlapping an
  apron it mustn't). Aprons of one surface may overlap and merge (world-mapped texture, identical pixels); asphalt
  and concrete may not overlap each other, nor dirt and grass; paved ones cover unpaved ones. Scenes saved with the
  old rectangle (`centre`, `size`, `rotation`) get an outline in `BuildGround` (`Migrate`). The mesh is ear-clipped
  (`Geo.Triangulate`, `SurfaceBuilder.Polygon`).
- **Surface submeshes are indexed by `PavementSurface`** (Dirt 0, Asphalt 1, Concrete 2, Grass 3) for taxiways,
  stands and aprons. Runways use submesh 0 for their own surface and submesh 1 for shoulders and blast pads (asphalt).
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

Edges with `road = RoadClass.Service` in the taxiway network (streets and highways would draw the same way for
now; their lanes and widths are in `RunwayRules.Road`: street 2 × 3.25 m, highway 4 × 3.75 m plus 3 m shoulders):

- **Routing:** aircraft never use them (`FindRoute` leaves them out). Vehicles prefer them: in `VehicleRoute` a road
  metre costs 1, a taxiway 1.25, an apron crossing 1.4 and a runway 1.6.
- **Geometry:** the same strips, curves and junctions as taxiways. Corners where a road meets anything use an 8 m
  radius (`RoadCornerRadius`), not the aircraft-sized taxiway fillets. Curves between two road arms use 2.5 × width.
- **Paint** (paved roads): white edge lines 0.3 m in, white centre dashes (3 m dash, 6 m gap). Where a road meets
  a taxiway (`GiveWayAt`), the approach lane gets a double dashed give-way line and a give-way triangle pointing at
  the driver. Before a runway, a solid stop line at the holding distance. Taxiway centrelines never connect across
  a road arm.
- **Never lit:** `SetLit` and `Connect` keep roads unlit.
- **On aprons:** road straights, road-only bends and road-only junctions sit at `AirfieldHeights.Road`, under paved
  aprons and over unpaved ones. So across a concrete apron only the white paint shows, and across a grass one the
  road's own asphalt does. Nothing is cut: an apron added, moved or removed needs no road rebuild. A junction with a
  taxiway stays at taxiway height. The road's paint still takes its section's snow: until a plough has been along
  it, the markings on a snowy apron are hidden even though the apron itself shows only light snow.
- **Junctions of roads only** (`RoadJunctionPaint`): edge lines follow every kerb fillet round, carrying straight
  on across the far side of a T. If exactly one road runs straight through (two arms 150° or more apart:
  `MainRoad`), it has priority: its centre dashes cross the junction and only the other roads give way, the
  double dashed line at the main road's edge plus 0.5 m. At a crossroads of two straight roads, or a junction
  where none runs through, every arm gives way (the user's choice) and no dashes cross the middle.
- **Bends:** two road sections meeting at an angle become a curve (radius 2.5 × width) with edge lines and centre
  dashes along the arc (`PaintBuilder.Dashes` over a polyline).
- **Stands** (`RoadAccess`): a road whose pavement comes within 1 m of a stand pad's edge, along 3 m or more, is a
  way in for vehicles: at the head of a parking stand (side 0, where the nose points) or either side (-1 / 1),
  never the taxiway edge; de-icing bays only at their sides. Roads may touch pads but never run across them
  (`StandAcross`, half a metre's slack), and stands can't be built over roads (`Fits`). The tug uses a head road
  (maintenance.md). Inside the pad vehicles plan their own way; stands are never a shortcut. The stand panel says
  which road serves it.
- **Vehicles across aprons:** straight lines between nodes on one apron are kept only where the line stays on it
  (`ApronArea.Crossable`), so routes go round the inside corner of an L-shaped apron.

## Stands and de-icing bays

A `Stand` (in `TaxiwayNetwork.stands`) is an aircraft parking stand (`StandKind.Parking`) or a drive-through de-icing
bay (`StandKind.DeIcing`). It stands on its own, built in the taxiway network's rebuild into its surface and paint
meshes, and is **connected** when a taxiway runs along its front line.

**Saved data.** `kind`, `number` (painted: stands 1, 2, ..., bays D1, D2, ...; the lowest free number for its kind),
`code` (`SizeCode` A–F), `surface`, `exit` (`PushBack` / `TaxiOut`), `noSmallAircraft` (it turns light aircraft,
under 5.7 t, away: `StandProblem.NoSmallAircraft`; so a Cessna doesn't take a jet stand), and its front line: `position` (where its
centreline crosses the front line, the taxilane centreline; a bay's middle), `front` (the front line's bearing),
`lane` (the taxilane width it's laid out for; 0 = its code's, `RunwayRules.TaxiwayWidthFor`), plus `heading` (its
centreline's bearing away from the front line; a bay's direction of travel). It stores no edge id.
`TaxiwayNetwork.Connection` finds a taxiway section running along the front line (within 0.02 in sine and 0.75 m)
past `position`. `Layout` always lays the stand out on its own front line, for its own taxilane (`Stand.Lane`), and
only links it to that section (`edge`; null with none), its pad running in under the section's pavement. So
connecting, splitting, rejoining or removing a taxiway never moves a stand (the user saw stands jump outwards when
connected, 2026-10-27). A stand laid out for a narrower taxilane than the taxiway it's connected to keeps aircraft
only that taxilane's clearance away, so the section takes aircraft up to that code only (`LaneLimits`, used by taxi
routing and `CheckStand`; the panels say so). Unconnected stands are refused by
`CheckStand` (`StandProblem.NotConnected`), so nothing parks on them, and push-back says why.
Building: `AddStand(kind, onCentreline, bearing, ...)` puts one against the taxiway at a point; `Add(stand)` adds one
framed with `Stand.Frame(front, lane)`, connected or not. Stands saved before front lines existed (no `framed` flag)
take theirs from the taxiway they sit on when first laid out (else square on).
A taxiway may run along a stand's front line but not across its pad (`Fits`, `StandAcross(a, b, width, road: false)`).

**Layout (`StandLayout.Of`).** It's computed from the code and the taxiway, never stored:

- **Frame:** s along the stand's axis from `entry`, x to its right. The distance from the taxiway centreline is
  `s * sin + x * k`.
- **Pad:** x0..x1 across, from under the taxiway (half width − 1 m, hidden) out to `far`. Its sides stay parallel to
  the axis, so stands of one angle side by side tile exactly and their slab joints (uv0 in the stand frame) line up.
- **Width:** wingspan + stand clearance (`StandClearance`: 3 / 4.5 / 7.5 m).
- **Depth:** the design aircraft (`StandLength`, the code's longest) parks with its nearest corner exactly
  `TaxilaneClearance` from the taxiway centreline (by the *taxiway's* code). So angled stands sit further out:
  `(clearance + span/2 · |cos|) / sin`.
- **Push-back:** nose away from the taxiway. The stop bar is where the nose gear stops (`NoseGearSetback`, 12% of
  the length behind the nose).
- **Taxi-out:** nose towards the taxiway. Aircraft arrive along the taxiway on the stand's acute side and turn onto
  a track two turn radii (`StandTurnRadius`) to that side. They run out along it, U-turn onto the stand centreline
  beyond the stop bar, and come down it. They leave straight on, turning the short way onto the taxiway (back the
  way they came). The pad widens to hold the whole hairpin: the gear track, and the wingtips and tailplane plus
  half the clearance wherever they're beyond the taxilane clearance. That's about twice a push-back stand's width.
- **De-icing bay:** a rectangle centred on the taxiway, wingspan + 2 × clearance + 2 × `DeIcingVehicleLane` (8 m)
  wide, the aircraft's length plus room fore and aft long. The aircraft is centred with its nose the way it's
  heading. Always paved.
- **Angles:** parking stands need 25° or more to the taxiway (`MinSin`). Bays must run along it (within 3°).
- **Fits:** a pad mustn't overlap another stand's (sharing an edge is fine, separating-axis test on the quads),
  a runway with its shoulders, or a road (touching one is fine). The tool and `EditStand` enforce it. `AddStand` doesn't check, so code that places
  stands calls `Fits`.

**Gameplay.**

- `RunwayRules.CheckStand(type, code, surface, taxiwayWidth, taxiwaySurface)` checks, in order: wingspan code ≤ the
  stand's, length ≤ `StandLength`, pavement unless the type takes unpaved runways, and the taxiway it opens onto
  (`CanTaxi`).
- `net.CheckStand(stand, type)` and `net.StandsFor(type, kind)` wrap it.
- `layout.ParkPosition(type)` gives where an aircraft's origin (main gear) stands with its nose gear on the stop
  bar, with `layout.nose` the way it faces. That's for parking aircraft, and later for jet bridges (on the nose's
  left) and hangars (beyond `far`).

**Paint** (paved stands only, `StandBuilder`):

- **Lead-in lines** (yellow): push-back stands get a curve of `StandTurnRadius` off the taxiway centreline, from both
  directions when within 15° of square. Taxi-out stands get the hairpin, the stop bar, then on as the lead-out turn.
- **Stop bar** for the nose gear.
- **Red boundary** round the parked design aircraft (half span + clearance/4), so neighbours' boxes don't touch.
- **Labels:** at the entrance the number (yellow, on the reader's left) and the code letter (white, right), 2.4 m
  (A) to 7 m (F) tall.
- **De-icing bays:** red lines either side at half span + clearance (the trucks' limit while an aircraft moves),
  the stop bar, and "D1" and the code beside the taxiway at the entrance.
- **Unpaved taxiways:** stand lines stop at their edge.

**Taxiway changes along a stand.** `Frontage` gives the stretch of each side a stand covers. On it, the taxiway
leaves out its verges (split round the stand) and edge lights.

**Not done yet:**

- Mowers still mow the full verge passes, driving over the pad.
- Stands have no wear or snow state of their own; like aprons they show light snow only.
- Aircraft don't park or taxi by themselves yet. A tug can push one back off a push-back stand onto the taxiway
  (`Maintenance.PushBack`, see [maintenance.md](maintenance.md#tugs-and-push-back)).
- Stands don't move when their taxiway's nodes are moved (nothing moves nodes yet), and a taxiway moved off a
  stand's front line leaves it unconnected.

## Terminals and jet bridges

Added 2026-10-29. The user's choices:

| Topic | Choice |
|---|---|
| Terminal shape | a polygon whose edges can bow out (or in) into arcs, up to a half circle: curved ends, satellites, bowed frontages. Floors 1–4. No interiors yet; deeper customisation comes later |
| Bridge ownership | a bridge belongs to one stand (`Stand.bridges`, 0–3) and never serves another. It may stand on either side of the stand's centreline |
| Doors | each bridge serves one left-hand passenger door, L1, L2 … counted from the front (`AircraftType` `PassengerDoor` points with `normal.x < 0`) |
| Long bridges | the fixed link from the terminal to the rotunda runs 0–60 m, for a curved terminal end whose stands fan out |
| Bridge heads (2026-10-30) | two or three bridges on one side of a stand share a head, as at most real gates: one main link from the terminal to a building on columns beside the aircraft, the bridges off its side. A double-deck gate's head has a second storey, so the upper-deck bridge's link is level |
| Short tunnels, staged moves (2026-10-31) | the user saw bridges swing round with their tunnels out and hit the aircraft. A bridge on a head stands close to the aircraft and needn't reach far, so it gets a short tunnel that retracts out of the way; every bridge now swings round only retracted, then telescopes out. The head can be dragged to place it where the player wants |

**Loose coupling.** Terminals know nothing of stands. A bridge is saved in its stand's frame (x across, s along, as
`StandLayout`), so it moves with the stand. Its link *root* is connected when it lies on or within
`JetBridgeRules.ConnectReach` of a terminal wall (`BridgeLayout.Connected`). Reshaping or deleting a terminal never
moves a bridge; it only disconnects it, and an unconnected bridge never docks.

**Geometry (`JetBridgeRules`, built into the JetBridge model to the same numbers, checked by a test).** Rotunda floor
4 m (an upper-deck bridge's raised rotunda: 7.8 m), radius 2.3; three 13 m tunnel sections telescoping from 14 to
36 m, or on a short bridge (`JetBridge.tunnel` = `TunnelSize.Short`, the model's `ShortTunnel_A/B/C`) three 8.5 m
sections from 9.5 to 22.5 m; a 2.6 m cab, its rear a half circle round its pivot, that turns up to 95°; slope at most
1:10; the drive 4 m back from the tunnel's end. Every link that leaves a terminal leaves it at the 4 m floor.

**Doors (2026-10-29: right-hand and upper-deck doors).** A bridge serves one door: `side` (Left / Right), `upper`
(upper deck) and `door` (counted from the front on that side and deck): L1, R2, UL1 ... (`JetBridge.DoorName`). A
passenger door more than 2 m above the type's lowest one is on its upper deck (`JetBridgeRules.UpperDeckAbove`,
`BridgeLayout.TryDoor`, `HasUpperDeck`). An upper-deck bridge stands on the raised rotunda (`JetBridge.Floor`, the
model's `RotundaHigh`). On a head (below) its stub link is level with the head's upper storey. On its own, its link
leaves the terminal at the 4 m floor like the others and ramps up to it at 1:10 at most, so it is at least 38 m long
(`JetBridgeRules.MinLink`, `BridgeProblem.LinkTooSteep`). Its tunnel may run over the wing root (parked, too), and
over a main-deck bridge where it clears it (below). `AirportBridges.PlanDoors(stand, n)` picks the doors for the bridge
count buttons: L1, L2, L3, or on a stand taking a double-deck type L1, then L2 + UL1, then L1 + L2 + UL1.
`ChangeDoor` points one bridge at another door and places it again (last in the stand's list: see parking below).

**Clearance between bridges (2026-10-30).** The user's ask: no bridge clipping another, clearance widths between
them, all three of an A380 gate starting at one level, and no link columns on roads. A bridge's parts in a pose are
boxes over the ground (`BridgeLayout.Shape`, `Solid`): rotunda, link (sloping where it ramps) and its columns,
tunnel (sloping), drive with its legs, cab. Two bridges clash (`Solid.Clash`, `BridgeLayout.Crosses`) where boxes
come within `JetBridgeRules.Clearance` (0.75 m) on the ground, unless one passes over the other with
`JetBridgeRules.Headroom` (0.3 m) between them: the parts are straight, so it's enough to check the corners of their
overlap. Rotundas, columns and the drive stand on the ground, so nothing passes under them. Cabs docked at the
double-deck's L2 and UL1 (4.4 m apart) stand 0.96 m apart, which sets how large the clearance can be. At 7.8 m an
upper-deck tunnel clears a main-deck link by 0.5 m; it can't pass over a main-deck tunnel or cab near the aircraft
(they rise to 8 m), so it runs beside them.

**Bridge heads (2026-10-30, `BridgeHead`, `HeadLayout`).** The user's ask: real gates don't run each bridge's link
the full way from the terminal; one main link reaches a fixed head, and the bridges split off from it, and a
double-deck gate's head has an upper floor so the upper-deck bridge needn't ramp. A stand has at most one head
(`Stand.head`, saved in the stand's frame: its centreline `from` (near end) to `to`, and the main link's `root` on the
terminal; `from == to`: none). It is 7 m wide (`JetBridgeRules.HeadWidth`), floor 4 m, on pairs of link columns 2.2 m
either side of its centreline at most 18 m apart, its ends overhanging the end pairs by at most 5 m, kept off roads
and taxiways like link columns (`HeadLayout.Columns`). Its main link is a link 3.6 m wide on its own columns, straight
from the terminal to the head's near end, or none if the head stands against the terminal. A head has a second storey
(floor 7.8 m, `HeadLayout.upper`) when an upper-deck bridge stands on it. A bridge stands on the head when its link
root touches the head's wall (`HeadLayout.Touches`, `BridgeLayout.head`): its rotunda 1.2 m off the wall on a level
stub link (`HeadStub`, `HeadLayout.Spot`), connected if the head's main link is. The head is a fixed shape every
bridge on the stand keeps clear of, parked, docked and on the way (`BridgeSite.head`, `HeadLayout.Shape`), and
neighbouring stands' bridges keep clear of it too; its own bridges' stub links and its columns share its `Solid.joint`
so they don't count as clashes. `HeadLayout.Placement`: the head and main link 1.5 m off every type the stand takes
(`HeadInTheWay`, `LinkInTheWay`), columns off the pavement, the head off taxiways and off the other stands' pads,
clear of the neighbours' heads and bridges. A bridge on a head reports its head's problems first.

**Placing a head (`HeadLayout.Suggest`, used by `AirportBridges.SetBridges`).** For two or more doors on one side
of the aircraft (L1 + R1 can't share one). The head runs parallel to the stand's axis beside the aircraft, its
centreline 12–34 m (plus the 7 m from rotunda to centreline) out from the largest type's doors, nearest a 16 m tunnel
first, within the stand's sides; its near end from a little ahead of the foremost door to the terminal wall straight
ahead (the main link covering the rest, at most 60 m). The bridges are placed on it from the aftmost door forward
(`SuggestBridge`: the side facing the aircraft, every metre along it, a short tunnel before a long one, preferring
the tunnel square to the fuselage and near 14 m, long: 20 m), and the first one also sets where the head ends, just
aft of it. That first bridge must be able to park swung back along the head and swing round from there to its door
(`BridgeLayout.CanParkToward`): the ones ahead dock across where it would otherwise park. A bridge is only accepted
where the bridges placed before it still dock everything they did (`BridgeLayout.Rate`). The first head whose bridges
dock every type with their doors wins, else the one that takes the most and docks the most types. `SetBridges` keeps the head unless placing the bridges each with its own
link takes more of them. `ChangeDoor` places a bridge on the stand's head first (a short tunnel first there). A bridge keeps its tunnel size
when moved; the inspector's Long / Short buttons (Bridges pop-out) change it (`JetBridge.tunnel`).

**Placing by hand (dev tool, 2026-10-31).** The user wants to choose where bridges and their links leave the
terminal. `AirportBridges` has the pieces: `AddBridge` (a rotunda and a link root; it serves the free door nearest it along
the aircraft that it docks at least one type at, short tunnel first: placed by hand it serves the door it was put
beside), `PlaceHead` / `HeadProblem` (a head from a main link root, near end and far end; a head may stand with no
bridges on it), `RemoveHead` (with the bridges on it), `MoveBridge` (rotunda; the link keeps its terminal end while that works),
`MoveRoot` (where a bridge's or the head's main link leaves the terminal, along its walls), `RemoveBridge` (its head
stays), `WhereProblem` (a quick check for a rotunda before a door is picked). A link may not run
through a terminal (`BridgeProblem.LinkThroughTerminal`) nor be longer than 60 m (`LinkTooLong`), a head's main link
included, and a head may not stand in a terminal (`HeadInTerminal`). Hand placements are checked by the same rules as placed ones; the inspector's Auto-place buttons
(`SetBridges`) place all of a stand's bridges afresh, throwing hand placements away.

**Dragging a head (dev tool, 2026-10-31).** The user's ask: say where the head starts, before the bridges branch off.
On a selected stand the head's two ends show rings: drag one to move that end (the head turns and stretches about
the other, 10–60 m long), or the head itself to move it whole. Its bridges go with it, each on its side of it at its
distance along it from the end that stays put, and its main link follows to the nearest terminal wall. A place where
the head, its main link or a bridge's rotunda would stand where the aircraft parks, on pavement, on the next stand or
in a terminal is refused (it stays where it last stood right); other problems show in the inspector.

**Docking (`BridgeLayout.Dock(type)`).** The cab's bumper stops 0.1 m off the door's sill, square on to the
fuselage; the tunnel runs straight from the rotunda to the cab. It fails (`BridgeProblem`) if it's too close or too far,
too steep, the cab can't turn that far, or the tunnel comes within 0.3 m of a wing or an engine
(`AircraftType.wingOutline` and its mirror, `BridgeLayout.Wings`), the tailplane or the fuselage
(`AirportMaintenance.Outline` boxes). The wings are their swept outline, not a box as wide as the span: on a
four-engined type L2 is only a few metres ahead of the wing root's leading edge, and a span-wide box over the root
would stop an L2 bridge placed for one type from reaching another's. A bridge needn't serve every type: it stays parked for a type whose door it
can't reach (an L2 bridge on a wide-body stand when a narrow-body parks).

**Placement (`BridgeLayout.Placement`, `Check`).** Not on taxi-out stands. The rotunda and link stay 1.5 m off where
any type the stand takes parks: its fuselage and tail boxes and its swept wings with their engines (the outline, as
for docking; an aircraft only moves forward to its spot, so the ground ahead of its wings' leading edges is never
under them). The rotunda column and every link column stay off roads and taxiways; links and tunnels pass over
roads. A link's columns (one per 18 m) are evenly spaced, or, where one would stand on pavement, moved along the
link off it with every span still within 18 m (`BridgeLayout.Columns`, `ColumnOnPavement` if no spacing works).
Parked (retracted, level, pointing at its door on the largest type with it, swung away as far as needed), it clears
every type by 1.5 m, stays off the taxiways, keeps its wheels off roads and stays off the other stands' pads. An
upper-deck bridge parks with its cab raised 1.39 m (`JetBridgeRules.ParkRise`: 1:10 on a long tunnel, steeper on a
short one, as it carries no passengers parked) with its tunnel and cab over the wings (its drive stays off them),
passing over the main-deck bridges docking under it. A stand's bridges park in its list's order, each swung clear of its head
and of the ones before it as well (`BridgeLayout.ParkAll`, used by `Check` and at work), so two never swing into
each other; if it can, each also parks out of where the others dock the types it can't (`BridgeLayout.Clear`: an L2
bridge left parked for a narrow-body mustn't block L1's way to it, nor its way out there), swinging round further if
it must. Rotundas stand at least 7.6 m apart. A stand's bridges keep clear of each other (see above) parked, docked to
each type and on the way (the stand's bridges set off together; checked at eight steps along the staged move below),
and of the neighbouring stands' bridges parked or docked (`BridgeSite.neighbours`).

**Moving (`BridgePose.Move`, 2026-10-31).** A bridge moves as an operator drives one: going out it first swings round,
as retracted as it is, to its docking heading, then telescopes out, tilts and turns its cab; coming back in it does
those first and swings last. Each stage takes its share of the 50 s by how far it goes (the swing measured at the cab).
So it never sweeps across the aircraft with its tunnel out. Swinging round, retracted, it must keep 0.5 m off the
aircraft (`BridgeLayout.SwingClear`, `BridgeProblem.SwingHitsAircraft`; an upper-deck bridge's tunnel passes over the
wings, its drive doesn't). The types a stand takes
are `Airport.aircraftTypes` filtered by code and length (`AirportBridges.TypesFor`); if that list is empty, the types
in the scene.

**Suggesting (`BridgeLayout.Suggest`, `AirportBridges.SetBridges(stand, n)`).** Two or more doors on one side go on
a head if one fits (above). Otherwise each bridge gets its own link: a grid search round the stand, on its own ground
(beside the aircraft or beyond its head). The link goes straight to the nearest terminal wall within 60 m (an
upper-deck bridge's at least 38 m long).
The place must dock the stand's largest type with that door. It prefers places that dock the other types too, a tunnel
about 22 m out, a short link, near the stand's head. `SetBridges` places doors L1..Ln in turn, each against the ones
before; it returns how many found a place.

**At work (`Airport.Bridges`, stepped by `Airport.Simulate`).** An aircraft that has stood still on its stand's
parking spot for 5 s gets its connected bridges, which dock in 50 s. When its departure flight starts they pull back,
and `Flight` waits ("waiting for the jet bridges to pull back") before calling the tug. A scene opened with aircraft
already parked shows their bridges docked. `BridgeRig` places the model's parts from each pose: the link pitched up
to a raised rotunda (or a stub from a little inside the head's wall), and at each column a foot, a shaft stretched
to the link and a head. Each bridge head is another copy of the model showing only `Head` (and `HeadUpper`)
stretched to its length, an end wall at each end, the `Link` widened as its main link, and the columns. Outside Play
mode it also places them before each render, so the editor and captures show them.

**Not done yet:** interiors; terminal styles beyond the one curtain wall; a bridge shared by two stands (by choice,
not planned); a head shared by two stands (bridges off both its sides, as at many piers); placing a head by hand
from nothing (drag one placed by the bridge count buttons); pre-conditioned air and ground power on the bridge. On the double-deck
stand a narrow-body gets no bridge: L2, parked, stands in L1's way to its door.

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
| Unpaved aprons (dirt, grass) | 0.011 |
| Roads (straights, road-only bends and junctions) | 0.013 |
| Paved aprons | 0.015 |
| Stand pads and de-icing bays | 0.0175 |
| Taxiway | 0.020 |
| Runway | 0.024, + 0.002 × `Runway.Layer` for crossing runways |
| Paint | 0.031 |
| Vehicle light pools | 0.045 |

## Vertex layouts (keep in sync with the shaders)

**Surface (AirfieldSurface)**

| Channel | Contents |
|---|---|
| uv0 | (x across, s along). Junction, apron and ground use world xz. Verges: x from the pavement edge. Stands: their own frame. |
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

**Add a service road.** `net.Connect(a, b, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service)`,
then `net.Rebuild()`; in the sandbox, build mode **Road**.

**Add an apron.** `airport.AddApron(corners, PavementSurface.Concrete)` (rebuilds the ground; null if
`airport.ApronProblem(corners, surface)` says why not), or `ApronArea.Rect(centre, size, rotation, surface)` into
`airport.aprons` plus `BuildGround()`. In the sandbox, the **Apron** tool.

**Add a stand or de-icing bay.**

1. `net.AddStand(StandKind.Parking, pointOnCentreline, bearingAwayFromTaxiway, SizeCode.C, PavementSurface.Concrete,
   StandExit.PushBack)`, or `StandKind.DeIcing` with the bearing along the taxiway.
2. Check `net.Fits(net.Layout(stand))`.
3. `net.Rebuild()`.

In the sandbox, use the **Stand** / **De-ice** tools. Neighbours at one angle sit `(x1 − x0) / sin` apart along the
taxiway.

**Add a stand marking.** Edit `StandBuilder` (stand frame: `l.At(x, s)`). New characters need the glyph atlas (see
[airfield-textures.md](../assets/airfield-textures.md)).

**Rebuild the sandbox airport.** Run **AirCity ▸ Airfield ▸ Add Sandbox Airport**, or the batch command in
[dev-tools.md](dev-tools.md). It contains:

- 09/27: 3,400 m x 60 m, concrete, precision lighting: every type up to code F. Taxiway A parallel to it, 25 m
  (code F) like every taxiway jets use, with entrances at both thresholds, three straight exits and a rapid exit
  each way.
- 13/31: 1,600 m, asphalt, basic lighting, crossed by a taxiway.
- A 600 m dirt strip, and a 700 m grass strip 06/24, grown to 19 cm, with a grass taxiway to a GA ramp (three code A
  grass stands, two code A asphalt taxi-out stands). The GA apron joins A by two 15 m links.
- Verges at various lengths (the main runway's at 14 cm, nearly due).
- The terminal apron: taxilane T1, 25 m, 100 m south of A and joined to it at both ends (no dead end). South of it,
  contact stands pulled against each other: four code C, one code E, two code F. From T1's east end a 15 m lane bends
  round the terminal's half-circle east end with three code C stands facing into it, and leaves north-east to A.
- The terminal: two floors, a half-circle at each end, its landside wall bowed. Jet bridges: L1 on every stand (R1
  as well on the first code C stand), L1 + L2 on a head on the code E stand and the second code F stand, L1 + L2 +
  UL1 on a two-storey head on the first code F stand (UL1 only once a double-deck type exists), and one long bridge
  per fan stand.
- Parked, ready to leave: narrow-bodies on two contact stands and a fan stand, a wide-body on the code E stand, and
  the double-deck and long-haul quadjet types on the code F stands once their models exist.
- Two code E de-icing bays (D1, D2) on a bypass beside A at the 09 end, for westbound departures.
- The depot west of the GA apron (3 ploughs, 1 mower, 2 small and 2 large tugs) on a small pad, with service roads
  east onto the GA apron, north across the dirt taxiway to A, and round the GA apron to the heads of the code C
  contact stands (their tugs come that way).
- A display ramp south-east, off every taxiway, where Add Sandbox Airport moves the scene's showcase rows of every
  type and livery.

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
- **Stands find their taxiway on every rebuild** from their position. Anything that edits the graph and can
  remove the last section under a stand must go through `RemoveOrphans` (`RemoveEdge(s)`, `RemoveNode` do).
- **Glyph atlas twins:** `PaintBuilder.GlyphChars` / atlas size and `textures.py` `CHARS` / `ROWS` must agree.
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

`StandTests`:

| Test | What it checks |
|---|---|
| `Rules_*` | codes, surfaces, the taxiway check, `StandsFor`, numbering, stands kept off runways |
| `PushBackStands_*` | at 90 / 60 / 45 / 30° both sides: the parked aircraft's nearest corner on the clearance line, neighbours tile, overlaps refused, shallow angles and off-taxiway points refused, meshes face up |
| `TaxiOutStands_*` | nose out, wider than push-back, the hairpin from the taxiway centreline to the stop bar is continuous and on the pad, wingtips on the pad beyond the taxilane clearance |
| `DeIcingBays_*` | along the taxiway only, always paved, size, stop bar, no edge lights in it, no overlaps |
| `Stands_StayOnTheirTaxiway_AndStayPutWhenItGoes` | verge split round a stand; survives a crossing splitting its section and the rejoin; stays put, unconnected, when its taxiway goes; reconnects; picking |
| `Stands_BuiltFirstThenConnectedByATaxiwayAlongTheirFront` | a free stand builds, is refused by aircraft, can't be crossed; a taxiway along its front connects it at its width; an angled one doesn't |
| `Stands_SavedBeforeFrontLines_TakeTheirTaxiways` | old saves frame from their taxiway |
| `Stands_TakeAnyAngleEitherWay` | 25°..155° to the taxiway, too shallow refused |
| `SandboxStands_*` | every sandbox stand attached and fitting; which take the jet and the Cessna |

Commands are in [dev-tools.md](dev-tools.md). Look at results with SceneCapture or a temporary editor script that
calls `SceneCapture.Capture(cam, path, w, h)`; delete the script afterwards.
