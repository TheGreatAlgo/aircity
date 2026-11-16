# Public roads

Public roads have their own serializable, metre-scale 3D graph in `Runtime/World/PublicRoadNetwork.cs`.
Airport service roads remain in the existing taxiway graph. Public streets, highways and terminal approaches
need independent elevations, lanes, controls and destinations; aircraft routing never sees these segments.

## Shared construction and game modes

The Street tool is shared by Career and Sandbox in `NewGame.unity`. `RoadConstructionPlan` clones the
input graph, resolves a snapped end, validates the connection and checks the quote before committing. The
legacy `PublicRoadConstruction.Build` entry point delegates to this plan. A preview
or refused purchase leaves nodes, split segments and funds unchanged. Career pays `RoadType.pricePerMetre`;
Sandbox uses the same geometry and clearance rules with free construction. The tool's `PublicRoadsView` delegates
to `ValleyView.RefreshRoads` in the main game, rather than creating a separate airport-local road network.
Both modes save `CareerState.publicRoads` and use the same first outside-connection objective check.

Street previews retain the real start/end heights and show the complete proposed bend with its actual
boundary. The compact map label gives the total construction price; an overlay keeps buried portions
readable before grading. Validation permits vegetation. An accepted road clears its exact ground geometry, including shoulders
and junctions, through the shared [built-surface map](built-surfaces.md).
Unchanged endpoint/style/network/funds/layout inputs reuse the quote. Free endpoints snap to a one-metre
cursor grid, avoiding geometry rebuilds from sub-metre ray noise; existing road snaps keep full precision.

`CareerRoadTool` is retained for original reviews/compatibility, not as a second feature implementation. Build
curves, elevations, bridges, tunnels and future street editing on the shared tool and transaction.

## Existing roads in new regions

Generator 4 plans the region's country road after sampling its real elevation and land/water mask. The
`RegionRoadPlanner` searches dry terrain and strongly penalizes steep, high routes, then rounds its alignment
without crossing water. It saves the resulting `world.publicRoad` path and aligned `world.roadHeights` profile;
its smoothed node grades are limited to 7.5%. Loading retains this engineered alignment and elevation rather
than draping a new road over later player earthworks. Earlier worlds retain their saved route and profile.

`PublicRoadNode.outsideConnection` identifies regional traffic entrances. Mainland entrances usually lie at
the map boundary; coastal island entrances represent ferry/harbour access. They remain part of the road graph
and survive clone/snapshot/save. The older boundary-position convention remains a load fallback.

Ground-road earthworks support the pavement with soil and blend into the terrain through cuts and 1:2 filled
embankments, with up to 192 m of influence. Supporting heightmap limits follow the pavement's longitudinal
grade and a level soil formation across its crown; projecting the crown's steep crossfall onto coarse terrain
corners would excavate a trench under a sloping road. Elevated road
decks do not contribute to this roadbed. The derived earthworks preserve the stored natural elevation data.

Player-authored ground streets use a shared maximum 12% grade and refuse routes through ocean or river water.
Refusal occurs before graph splitting or payment in both modes. Preview and purchase use the same checks,
including the road's shoulders at shorelines. Snapping onto an existing road resolves its actual interpolated
profile before preview, grade checks, quotes and splitting, so a new street cannot move the old road onto
the original ungraded terrain. Players can follow a contour, prepare the
land, or use an appropriate bridge connection instead of creating a near-vertical ground ramp.

## Network and connections

`PublicRoadNode` stores a stable ID, explicit `Vector3` position, junction control (and whether it was chosen by hand,
`controlChosen`) and optional `terminalEntrance` identifier. `PublicRoadSegment` stores stable ID, endpoints, style
and Ground/Elevated structure.

### Road types

`RoadTypes` (`Runtime/World/RoadTypes.cs`) holds one `RoadType` per `PublicRoadStyle`: lanes each way, lane width,
median (none, raised kerbed island, concrete barrier), paved shoulders, edge (gravel shoulder or kerb and sidewalk),
verge or sidewalk width, centre line, speed, curve radius, junction kerb radius, price per metre and whether it may
cross other roads at grade. Saves store only the style number, so **append** styles, never reorder them.

| Style | Name | Lanes | Cross-section |
|---|---|---|---|
| Rural | Country road | 1 + 1 | gravel shoulders, dashed centre line (profile `RuralRoad` sets its width) |
| AirportAccess | Airport access road | 1 + 1 | as country road (profile `AirportAccessRoad`) |
| Urban | Two-lane street | 1 + 1 | kerbs and 2 m sidewalks, double solid centre line |
| Highway | Highway | 2 + 2 | barrier median, paved shoulders; only bridges and ramps meet it |
| Ramp | Ramp | 1 one way | paved shoulders |
| Avenue | Four-lane avenue | 2 + 2 | 2 m raised median, kerbs, sidewalks |
| Boulevard | Six-lane boulevard | 3 + 3 | 4 m raised median, kerbs, sidewalks |
| OneWay1/2/3 | One-way street | 1 / 2 / 3 one way | kerbs, sidewalks (terminal loops and kerbside frontage) |

`RoadType.LaneCentres` gives each lane's offset and direction; `Carriageway(forwards)` the edges of one direction's
lanes. Traffic keeps right; looking from node a to b, forward lanes are on the right.

### Junction control and lanes

`Connect` gives every junction it makes or changes its default control unless `controlChosen`
(`DefaultControl`): **traffic lights** where a town road (any type with kerbs) meets others and traffic arrives from
two or more roads; a **priority** junction (the road straight through keeps going, the others give way) where only
country roads meet or one-way roads merge or part. `SetControl` chooses one by hand and it then stays.
`RemoveSegment` drops a section and empty nodes and re-applies defaults. A road ending on another road's section
(within 10 cm, same height) joins it there (a T junction).

`LaneConnections` gives which lane may go where, as on a real road. Lanes are numbered from the kerb (0) inwards. The
kerb lane turns right, the centre lane turns left, and every lane may go straight. With no road straight on, the kerb
half turns right and the centre half left; a single lane makes every move. Moves land kerb to kerb, centre to centre,
and straight lane to lane. Left turns yield to opposing traffic. A one-way road leaving a junction takes no traffic
into it. U-turns are left out.

`Connect` preflights all crossings before changing segments. Elevations within 0.35 m form one junction;
different levels remain disconnected with at least 6.5 m centreline separation, allowing for the bridge deck.
An automatic street crossing cannot create an at-grade highway junction: merge/ramp nodes must be authored
explicitly. `HasRoute` respects one-way ramps. Save validation rejects missing IDs, invalid coordinates,
unsplit at-grade crossings and elevated crossings without clearance. A topological route alone does not yet
prove vehicle swept-path clearance or a safe merge.

## Meshes and signals

`PublicRoadGeometry` draws every road from its type:
- **Strips:** crowned pavement, with gravel shoulders (falling away) or 15 cm kerbs with sidewalks behind them.
- **Markings:** edge lines on shouldered roads; a dashed or double solid centre line on undivided two-way roads;
  dashed lines between lanes running the same way, solid for the last 25 m before a junction; yellow lines along a
  median.
- **Medians:** raised medians are kerbed grass islands and highway medians are concrete barriers. Both stop behind
  the stop lines.
- **Approaches:** each road arriving at a junction gets a stop line (give-way dashes where it gives way), a zebra
  crossing on roads with sidewalks, and a filled arrow per approach lane showing that lane's `LaneConnections` moves.
  Lane lines stop at the stop line. `StopLine(node, edge)` says where it is.
- **Meshes:** `kerbMesh` (kerbs and barriers, two-sided) and `medianGrass` join pavement, shoulders, markings and
  structures. `ValleyView`, the road kit and `PublicRoadsView` each render all six.

A node joining two
sections of the same style and width becomes a curve (`CurveRadius`: highway 150 m, rural 60 m, ramp 45 m, others
25 m, shortened to fit each section's share, see airfield.md) carrying the strip, shoulders and markings round; hairpins (arms under 25°
apart), bends too tight for the width and mismatched sections are junctions. Junctions use the shared `JunctionShape` solver (see airfield.md,
"Junctions at any angle"): sections are cut back clear of each other at any angle, kerbs get 8 m fillets (12 m with a
highway) and shoulders follow the kerbs. `MisfitJunctions` lists junctions whose sections are too short for them. Curves add their arc to `links`, so roadbed
grading, ground texture and ground cover follow the curve. `PublicRoadLayout.ShapeProblem` (used by
`CareerConstruction.Road` and `PublicRoadLayout.Validate`) refuses a route that meets the valley road under 15° or
doubles back sharper than that.
Roads flatten toward a junction's saved elevation. Only Ground links contribute
to derived roadbed terrain heights. Decks, parapets and piers belong to Elevated links; piers skip lower roads.
The builder shares low-level `MeshData`/`PaintBuilder` utilities and surface shaders with the airfield, but has
its own topology and junction construction. Generated meshes are rebuilt from the saved graph.

`PublicRoadSignalsView.Build(graph, prefab, stopLine)` puts signals at every signalled junction as on a real road.
Each road arriving gets a pole at its kerb just past the stop line, facing its traffic. Roads with two or more
approach lanes also get a mast arm (6.6 m) with a head over the lanes, and divided roads a head on the median's nose.
One-way roads leaving the junction get none. `HeadCount` is for tests.

`PublicRoadSignals` groups the most nearly opposing approaches into a through-road phase. Its deterministic
44-second cycle gives that phase 24 seconds green, 3 amber and 1 all-red, followed by 12 seconds green,
3 amber and 1 all-red for the other approaches. Left turns yield to opposing traffic; protected turn arrows
and vehicle-actuated phases are future work. `PublicRoadSignalsView` controls the reusable `TrafficSignal`
prefab through property blocks. Career signals use the saved game clock and pause/speed controls.

## Building them

The shared Street tool (`DevStreetTool.cs`, after Road in the toolbar) serves both modes in `NewGame.unity`.
- Click points; right-click or Esc ends.
- Ends snap to road nodes or onto a section, joining it there.
- Shift gives 10° / 5 m steps.
- Preview geometry keeps real elevations; the compact map label shows the total cost, including earthwork.
- Construction clears vegetation automatically; immediate and queued rendering use the same built footprint.
- `PublicRoadNetwork.JoinProblem` refuses shallow joins, sections too short for their junctions and highways
  crossing at grade. Terrain/clearance/funds checks appear before purchase.
- The tray provides each type's cross-section. Both modes save the complete public graph.

The old `Sandbox.unity` development scene can also draw airport-local public roads without a valley.
`CareerRoadTool` below remains an original-format review/migration path.

## Player flow and reusable assets

The first career connection can be drawn from a player-chosen airport entrance to the region road with the
shared Street tool. The original `CareerRoadTool` review path offers clicked
corners, end snapping, terrain-following preview, length-based pricing, undo and cancel. `CareerState.Build`
validates and connects a cloned graph before committing funds. New games require manual tree removal along
the route; building a road never clears its woodland. The entrance uses public node ID `airport-reception`;
future terminals can provide separate arrivals/departures entrance identifiers. The graph is saved with the
career; original implicit-road saves migrate from their saved valley.

Assets live under `Assets/AirCity/World/Roads`. `RuralRoad` and `AirportAccessRoad` profiles hold materials,
widths, shoulder widths and crown. Materials are independent copies of the existing airfield materials and
remain hand-tunable. **AirCity > World > Create Public Road Model Kit** creates missing prefabs for
`RuralStraight`, `UrbanCorner`, `SignalTjunction`, `SignalCrossroads` and `HighwayOverpass`. The latter includes
an 8 m high highway, an unrelated road underneath, and an ascending one-way ramp to a named upper entrance.
`PublicRoadModel.network` keeps the source layout with each prefab. `RoadJunctionReview.FromCommandLine` also captures
each road type's junctions (`type-*`, from above and at an angle). `RoadTypeTests` checks the types, signal defaults,
lane rules, the join guard and every type crossing a street or one-way street (no misfit junctions, no down-facing
pavement).

## Performance

Placing a road has to stay within a frame or two. `RoadPlacementTiming.FromCommandLine` (Editor/Airfield) times every
step and writes `Logs/road-placement-timing.txt`. Figures from 2026-11-14:
- Sandbox airside road click: 300 ms before, 45 ms after. A public street click: 13–21 ms.
- Career access road: 1250 ms before, 78 ms after; a later road change about 95 ms.

The 2026-11-14 terrain-aware preview review added 220,000 distant trees and measured a warm moving
Street-tool mean of 5.9 ms, with unchanged-cursor validation at 0.004 ms. The previous per-footprint-point
whole-tree scan is replaced by a single local spatial query and an input-keyed quote cache. Actual mouse
placement and portable saves passed; nine Game-view captures are in `Captures/NewGame/AirsideBuild` and
operation timings in `Logs/airside-build-review.json`. These timings exclude the rest of the frame.

How:
- `ValleyView.RefreshRoads` diffs the network against the one last drawn. It regrades the terrain, repaints the
  ground and queues ground cover only within `RoadInfluence` (earthwork reach plus 32 m) of sections that changed (and those meeting them).
- Ground cover and ground paint drain in `LateUpdate` within a 3 ms budget.
- `SetGroundCoverExclusions` refreshes only the tiles an exclusion that came or went touches.
- Placing things in the career saves in the background (`QueueAutosave`).
- Airside, `AirportBridges` lays the bridges out again only when their inputs change: aircraft types, terminals,
  stands, and pavement within 60 m of the gates (`Signature`, quantised to the millimetre).
- `StandEquipmentLayout` caches each stand's staging by a hash of what reaches it.
- `JunctionShape.SolveCached` remembers solved junctions.
- `TaxiwayNetwork.Rebuild` no longer re-projects runway nodes that are already on the centreline. That re-projection
  moved them by a float ulp on every rebuild and made every rebuild look like a layout change. Forced kit regeneration is available
through `PublicRoadKitSetup.Create(true)` and updates mesh subassets in place.

Open `Scenes/WorldAssets.unity`, or create it with **AirCity > World > Create Tree and Road Review Scene**.
`WorldAssetReview.FromCommandLine` renders the kit, the actual airport junction and forest, then completes
a real charter. `CareerRoadPlayReview.Run` captures the actual Game view and drives the drawing tool with
queued mouse clicks, then checks save/load. `PublicRoadTests` covers topology, clearance, one-way ramps,
signal interlocks, custom purchase/save and migration; career tests cover the existing flight loop.

## Next work

This is the graph, asset and first construction foundation. Civilian vehicles, queues, intersection reservations,
highway merge behaviour, passenger transfers, sidewalks/crosswalks, swept turn paths, road maintenance and
road upgrades and undo/redo still need implementation. The shared Street tool now builds ground roads,
bridges and tunnels with real profiles in both game modes.

## Persistent pavement ownership (2026-11-15)

`PublicRoadGeometry.groundTriangles` records ground carriageways, bends, junctions and shoulders/sidewalks.
The valley combines these with committed airport surfaces for terrain clipping and precise nature exclusion.
Elevated decks keep their underlying terrain and vegetation. Trees, rocks and tufts are removed from a new
road after acceptance; the Street preview no longer refuses them. Existing crown-aware public-road formation
remains authoritative during every local/full heightmap rebuild. See [built surfaces](built-surfaces.md).


### Ungraded Street previews (2026-11-15)

The Street ghost's `PublicRoadsView.constructionPreview` uses the shared temporary `ConstructionMaterials`
for its asphalt, concrete and paint. Their forward passes stay visible through hills before the earthwork
is accepted, with depth writes and depth/normal passes disabled. Committed public roads retain their ordinary
materials and terrain ownership. Preview material copies are released with rebuilt/cancelled geometry.
The native placement follow-up verifies all eleven sampled Street-preview locations across an 8 m hill,
then commits the real section, checks the price and saves/reloads the region.


### Complete bend previews and road finish (2026-11-15)

The optional preview callback on `PublicRoadConstruction.Build` exposes its detached validated proposal;
preview calls still return the original graph and change no funds or layout. The Street tool draws that full
proposal, so the preceding approach, curve, kerbs and markings match the accepted road. It temporarily
replaces committed road visuals and GPU terrain ownership, then restores them on cancel/accept. CPU terrain,
colliders and permanent ownership retain the committed state throughout preview. Boundary outlines follow
the actual surface rather than a straight rectangular guide. Valid placement labels show the total cost only.

Ground dead ends have rounded corners, returning sidewalks/shoulders and a set-back median. The finish stays
within the original pavement length and width; outside-connection and elevated endpoints keep square ends.
Asphalt carries `AirfieldKind.Road` and uses deterministic broad discoloration plus fine tonal variation.

Lane arrows use continuous signed-distance triangular heads and curved turn stems contained within their
lane. Short approaches omit arrows that cannot fit. Public-road paint is finally projected onto actual
pavement triangles, choosing the closest deck to its original elevation at stacked crossings. The same
triangle gradient drives the paint shader's pixel expansion, preventing grown corners sinking into slopes.
Long edge/stop lines have intermediate samples so they follow graded approaches. This projection uses the
existing spatial index rather than scanning every road triangle for every paint vertex.

`RoadFinishTests` covers rounded/cross-border ends and lane-contained paint on graded Urban/Avenue junctions;
`AirsideTerrainTests` compares the preview mesh with committed geometry and verifies cancel restores the
renderer and terrain mask without editing funds, graph or heights. The extended native review captures both
curve previews, cancellation, finished ends, asphalt and close junction arrows via actual mouse clicks.

Validation: `Logs/road-shape-final.xml` passed all 164 selected game, airfield, maintenance, public-road,
road-finish and ownership cases. The final isolated Game-view review passed actual bend placement for both
road systems, non-mutating cancellation, rounded finishes, exact total prices and a graded T-junction, then
saved/reloaded the result. Its 23 captures are under `Captures/NewGame/RoadFinish`; the report is
`Logs/road-shape-review.json`. The fixture charged $51,482, cleared 28 roots and 207 props and retained
12,274 owned triangles. These controlled checks also include repeated grading and forced-terrain clipping.


### No terrain-asphalt halo (2026-11-15)

`ValleySurface` no longer paints an approximate asphalt strip around public-road links. Roads, curves,
ends and shoulders use only their actual meshes; surrounding land retains its natural biome layers.
`RefreshRoads` updates engineering heights and nature without repainting an enlarged road corridor.
Paint projection and boundary queries now use the sparse CPU `Pavement` index. The reusable terrain-preview
index retains its native/GPU capacity between moving endpoints and restores committed bindings on cancel.

Public Street preview performance now uses `PublicRoadPreviewPartition`: keep the candidate's full topology,
but cache unchanged roads and rebuild changed sections plus their dependent approaches/junctions. The two
partitions share terrain ownership and paint projection context; cancellation restores the committed roads.
Cached edge elevation profiles remove repeated node lookups and duplicate marking-gradient calculations.
The isolated warm cursor benchmark fell from 128.97 ms to 9.58 ms; service-road/runway timings and native
validation are recorded in [built surfaces](built-surfaces.md#placement-performance-and-unchanged-surrounding-land-2026-11-15).


## Shared 3D construction (2026-11-15)

`World/Alignment3D` is the common path contract for public streets and airside service roads. Saved
`AlignmentShape` adds a quadratic horizontal bend and Hermite vertical profile. Its default preserves old
straight, constant-grade saves. Geometry, lane marks, snapping, quotes, splitting, ray picking and service
vehicle contact read the same path. Splits preserve the original curve and grade. Graphs remain independent:
a height-separated crossing never becomes a junction or an aircraft route.

The shared Road/Street inspector provides Auto, Ground, Bridge and Tunnel, typed elevation, held +/- controls,
Page Up/Down and a bend slider. Height is relative to the current chain's starting datum; bridges and tunnels
hold that construction level across valleys, water and hills. Auto divides the proposal at actual terrain
transitions. Ground returns the next point to the local terrain. Prices include structure multipliers and
earthwork and appear as one total. Ground roads refuse water, bridges require water clearance, tunnel mouths
require dry land, and covered water crossings require rock cover. Terminal/depot foundations remain protected.
Select/Delete resolve the visible deck; public-road demolition uses the public graph.

`RoadConstructionPlan` retains its detached result and source/terrain signatures. Preview and purchase consume
that same result once. Changed terrain/layout/funds refuse before mutation or charge. Airside roads use the
same path/settings and retain an exact `TaxiwayNetwork.GraphSnapshot` in `AirsideTerrainPlan`. These are the
command foundation; saved undo history and road-style upgrades are not implemented yet.

`RoadStructureBuilder` supplies both systems' closed bridge slabs, parapets, piers, footings, tunnel lining,
retaining walls and portal frames. Concrete has a dedicated rough URP material. Piers avoid lower roads and
aircraft pavement. Only ground pavement, bridge abutment/footing openings and open tunnel approaches enter
terrain ownership. High decks preserve their ground; covered bores preserve their roof. Low trees remain under
bridges and only canopies entering the deck are removed. Terrain edits refresh nearby structural supports.

`PublicRoadChunks` retains separate section/junction renderers and meshes by graph ID. Changes rebuild their
dependent neighbours; distant identities survive. A metadata composition supplies full grading and ownership
without combining render meshes on every edit. Section terrain fingerprints refresh affected structures after
earthworks. Signals are cached at rebuild time. This keeps local street commits local as the graph grows.

Underground view is temporary rendering/picking state: a height cut across terrain, pavement, concrete and
buildings, hidden surface nature/traffic, and a subdued 25 m construction grid. Surface view and scene unload
restore renderer/camera state. It never excavates terrain or changes saves. Construction ghosts remain overlays. Independent equipment-paint refreshes use the same owner-specific
road profiles as a full rebuild, so lower-deck markings cannot jump up to the surface after loading or
changing a parking layout.

Repeatable native verification: `PavementOwnershipReview.RunStructuresFromCommandLine`. It drives actual mouse
placement for an automatic bridge, ground exit, tunnel ramp and underpass below a taxiway, verifies exact
charges, retained terrain, tree clearance, a real baggage tractor on its lower deck, public-road picking/deletion,
and full save/reload. Captures: `Captures/NewGame/RoadStructures`; report: `Logs/road-structures-play-review.json`.


### Foundation verification

The final affected-system suite passed 284 cases, and the traffic/runway-clearance follow-up passed 30.
The final paint/parking follow-up passed 47 cases. Across the three runs, 309 unique cases passed. Reports are
`Logs/road-foundation-{final,clearance,paint-final}.xml`. The earlier attempt to run every airfield/world
fixture exceeded its eight-minute limit and is not a complete-suite pass. The native Play review uses a
private save slot and validates visible geometry, real input, exact charges and reload independently of
headless tests. It also catches independent equipment-paint rebuilds lifting buried markings, now covered
by both the mesh regression and the native reload assertion.

The controlled warm 17-road editor fixture measured moving service-road preview work at 24.12 ms, public
Street work at 9.91 ms and runway work at 2.50 ms. Unchanged endpoints cost 0.035, 0.004 and 0.002 ms
respectively. The extra service-road path sampling supports real grades; public Street cursor work remains
close to the previous 9.58 ms result. These CPU measurements exclude the rest of the frame. Report:
`Logs/road-foundation-performance.json`. Existing region painting, terrain and runways remain shared with
the previous no-halo placement implementation.
