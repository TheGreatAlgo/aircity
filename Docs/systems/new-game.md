# New Game: build a valley airfield

Open `Assets/AirCity/Scenes/NewGame.unity` and press Play. The career launches before the independent
Sandbox development scene. **AirCity > Game > Update New Game World Assets** updates references and installs
missing player tools while preserving the scene layout. **Create New Game Scene** recreates the scene.

## Player experience

Survey one of three seeded valley sites and buy its land. After purchase the screen shows one short current
task and a bottom toolbar containing only earned tools. There is no construction checklist, one-click airport
build, automatic land-clearing purchase, or prescribed runway outline. The boundary shows owned land.

Career and Sandbox share `DevGui` controls, colours, type sizes and screen scaling. The bottom bar uses the
same 48-unit square icons, dark panel, group dividers, amber selection and hover tips; clicking the active
tool returns to Select. Tools still unlock through play. There is no full-width brand/header or centred
message banner. The objective is a compact corner panel with its explanation on hover. Funds/Save stay in
the opposite corner; **Game** opens focus, pause/speed and New Game controls. Active tool settings sit just
above the toolbar. **H** hides the HUD. Feedback and unlock messages appear briefly beside the objective.

Tools unlock through actual work, and remain available afterward:

| Action | Tool unlocked |
|---|---|
| Buy land | Select and Clear trees |
| Remove a selected tree or brush patch | Level ground |
| Level a cleared patch | Runway |
| Place a valid runway | Taxiway |
| Draw a connected taxiway | Parking |
| Place aircraft parking | Public road |
| Draw the public road connection | Reception |

The task card helps with the next goal. It does not require clearing or grading the entire parcel.
Players choose the runway position/direction, taxiway paths, parking side, airport road entrance, road route
and reception position. Construction refuses a wooded or uneven footprint and leaves funds/world unchanged.
Earlier tools remain usable for preparing additional land and extending the runway.

## Controls

- **Clear trees:** click/drag a circular brush, or switch to **Single tree** and click a tree. The preview shows
  the actual count and $24 per tree. Only chosen trees are removed. Brush size is adjustable.
- **Level ground:** drag the brush over cleared land. Set a target height in 0.5 m steps and adjust brush size.
  Trees must be removed first. The quote comes from the actual earth volume; grading never removes trees.
  Brushes that could disturb existing runway/taxiway pavement, parking pads, reception, public roads or bridge
  supports are refused, including at the airport elevation. Keep the brush clear or demolish the affected
  structure first. The protected margin includes one terrain sample beyond the brush for interpolation.
- **Runway:** drag between thresholds for a grass strip, 700–1,400 m long. The bearing snaps to 5 degrees
  and length to 10 m; hold **Shift** for a free bearing. Its footprint must be clear and level, with room
  for shoulders. To extend it later, clear/level new land and drag an existing threshold outward.
- **Taxiway:** click points, starting or ending on the runway or an existing taxiway. **Enter** or
  **Finish taxiway** builds the draft. Right-click removes a corner; right-drag still orbits.
  Aircraft need an exit far enough along the runway to slow down, so connections near its ends are useful.
- **Parking:** click beside a taxiway. The clicked side sets the stand direction; its pad previews before
  purchase. The starter is a grass code A taxi-out stand.
- **Public road:** click the airport end, add corners, then click the valley road to snap and purchase.
  Clear its trees first, including the road corridor outside the airport parcel. Public roads must stay
  clear of aircraft pavement. There is no automatic tree clearance or suggested-connection build button.
- **Reception:** place the building beside the airport end of the public road; **R** rotates it.
- **Esc** cancels the active tool/draft. **WASD**, middle drag, right-drag and scroll control the camera.
  Buttons and trays consume pointer input. Save remains directly available; focus, pause and 1×/5×/20× are
  in the compact **Game** menu.

The first task sequence introduces these tools. Once the airport is open, schedule a local charter.
The existing aircraft simulation performs arrival, taxi, boarding and departure, with a $250 operating cost
and $1,800 service fee paid only after completion. The initial balance is $125,000. The next goal encourages
a 1,000 m runway, and more charters remain available. Construction is disabled during an active flight.

## Saved layout and world

`CareerLayout` saves placed runway thresholds/elevation, clicked taxiway paths, the parking frame and reception
pose, plus earned unlocks and clearing/earthwork history. `CareerConstruction` validates previews and commits
paid transactions. `CareerBuildTool` handles player mouse input; `CareerHud` presents tasks and tool controls.
`CareerGame` rebuilds the real airport from the saved placement data rather than a fixed template.

`ValleyWorld` saves the full 6.4 km heightfield, trees/removal flags, river/road/sites and generator version.
Generator 2 adds rolling valley floors, drainage swales, low hummocks and irregular valley shoulders.
Starter approaches remain bounded; only the player's grading produces a level construction pad.
Generator 1 snapshots keep their stored heights unchanged when loaded.
Brush earthworks modify only selected height samples within the parcel and preserve blended edges.
A saved disturbance field records clearing/soil exposure. See [public roads](public-roads.md) for their
independent 3D graph, saved entrance node, route and signal clock.

`ValleyView` uses Unity Terrain. Six texture layers provide meadow, dry grass, exposed soil, rocky ground,
forest floor and road underlay. The meadow, litter and rock use photo-scanned CC0 surfaces with normal and
occlusion maps, rather than mud reused as rock. Source/processing details are in
[`ArtSource/World`](../../ArtSource/World/README.md). `ValleySurface` uses Burst jobs to combine warped small
patches, actual canopy coverage, height/slope, river banks and saved earthworks.
Terrain layers use constant matte smoothness; opaque albedo alpha must not become a glossy ground surface.
`ValleyTerrainSampling.hlsl` blends three stable random texture offsets, with matched albedo/normal/mask
lookups and explicit mip gradients. This breaks repeating scan stripes. `ValleyTerrainShaderSetup` rebases
the small sampling extension onto installed URP terrain passes, including the extra-layer and basemap passes;
generated copies retain Unity's license notice. Daylight uses neutral sunlight and lower ambient fill so
forms and forest shadows remain visible.
Clearing/levelling updates geometry while dragging. Each levelling stamp uploads only its heightmap rectangle,
using the existing public-road grading; road meshes and forest batches are retained. Surviving trees beside
the brush are re-seated to account for height interpolation. The terrain collider updates with the stamp.
On brush release, only affected surface tiles are queued: 32 × 32 control pixels, one per frame, with a 50 m
halo covering canopy/soil interpolation. Levelling also queues affected 256 m groundcover tiles, one per frame,
including the interpolation/slope halo. Props outside those tiles keep their existing instance batches.
Overlapping strokes coalesce. `ValleySurface.PaintRegion` and local groundcover generation sample the same
global fields as full rebuilds; tree clearing never rebuilds terrain heights, roads or groundcover. The
[instanced forest](../assets/ForestTrees.md) uses shared spruce/pine models rather than individual tree objects.

`GroundCoverSetup` generates twelve shared near/far models: weathered boulders and stones, curved grass/sedge
tufts, compound fern fronds and low woody shrubs. `GroundCoverScatter` uses stable seed/cell candidates and
the original woodland habitat. Tree clearing leaves the understory in place; grading removes detail from
worked soil. Ground detail is decorative in this pass, with no separate boulder excavation price or tool.
Roads, aircraft clearances, actual stand pads, reception, settlements and water exclude these props.
`GroundCoverRenderer` draws spatial GPU instance batches with frustum/distance culling, near/far dithering,
rock normal maps, leaf wind and shadows. Tree-only strokes retain these instance batches. Groundcover
suppression ignores tree-clearing disturbance samples before interpolation, so clearing beside an old grading
edge cannot remove plants when the save is later loaded.

The world still has three broad starter valleys, one operating airport elevation and a finite owned parcel.
There is one career runway, parking stand, reception and public-road connection; additional taxiway paths
and runway extension are supported. Multiple runways/terminals, general deletion/moving, airport-wide service
systems, general land purchasing, large highway construction and international progression remain future work.

## Saving and compatibility

`CareerSaveStore` writes `Application.persistentDataPath/Career/career.json` with flushed temporary data and
atomic replacement. The previous good save is retained as a backup. Invalid saves cannot replace it.
Loading uses the saved heightfield rather than generating new terrain from the seed.

Clearing/levelling queues an autosave at stroke end; periodic autosaves use the same queue. `CareerSnapshot`
copies mutable world/layout/road data on the game thread. A single worker validates, serializes and durably
writes the detached snapshot; only the latest waiting snapshot is retained. The worker never reads live
game data. Save errors return to the game thread. Explicit Save, Continue, application pause and quit wait
for pending writes; explicit saves and placed constructions retain synchronous completion. Active charters reconstruct/replay at fixed
0.25-second boundaries without charging twice. Pause and simulation speed also control public signals.

Original checklist-era saves retain their former layout, finances and terrain. Continue upgrades their
implicit runway/taxiway/stand/reception into explicit player layout and grants the earned tools. The old
sequential `Build` implementation remains for format compatibility/regression fixtures; new games refuse
its automated construction actions. Player layouts use save format 2; original format 1 remains readable
and is upgraded on Continue.

## Verification

`CareerTests` / `CareerSceneTests` retain original-save, finance, flight/resume and compatibility coverage.
`PlayerConstructionTests` covers manual/atomic tree removal, grading without clearance, tool progression,
placed thresholds, road refusal through woodland, serialization and original-save conversion. Grading
protection checks runway/taxiway margins, full parking/reception footprints and ground/elevated public roads;
previews and commits refuse the work without changing funds, heights or unlocks until the obstruction is removed.
`PlayerAirportSceneTests` checks real arrival/departure and identical-pose resume from a manually placed airport.
`ValleySurfaceTests` checks normalized, varied terrain weights and visible earthwork changes.
`TerrainReliefTests` checks natural relief, clear approaches, local grading and original height snapshots.
`GroundCoverTests` checks stable habitats, tree-clearing/earthwork behavior, full-footprint construction
exclusions, area consistency and water clearance.
`CareerSaveTests` covers detached snapshots, queued ordering, explicit-save barriers, backup preservation
and background failures. Surface regressions compare dirty-region painting against a full map and verify
tree clearing preserves groundcover instances, road meshes and terrain heights.
`TerrainBrushViewTests` checks immediate local heights and collider picking against a full road-graded
heightmap, including map edges; surviving trees keep their instance arrays and follow the edited terrain.
It also compares bounded surface/groundcover updates with full rebuilds, including prop transforms, tile
boundaries, overlapping strokes and unchanged distant batches.

`CareerClearingPerformanceReview.Run` measures the original full-map release work against actual clearing,
queued saving and bounded terrain tile updates in the same seeded world. It writes warm timings and saved-state
checks to `Logs/career-clearing-performance.json`; all review saves use an isolated `Logs` directory.
The 2026-11-10 isolated editor run measured 59,462 trees / 409,280 groundcover instances and an 11.9 MB save:
original release median 671 ms, new release median 7.9 ms, bounded paint tile median 5.1 ms. These are warm
CPU timings for the measured operations, not whole-frame or minimum-hardware guarantees. All 58 focused
career/road/terrain/save tests passed.

`CareerLevelingPerformanceReview.Run` compares the former full-world stamp/release operations with actual
paid levelling stamps, bounded release queues and combined deferred tile updates in the same seeded world.
It writes warm CPU timings to `Logs/career-leveling-performance.json`, verifies road mesh identity and durable
saved heights/disturbance/funds, and uses an isolated save directory under `Logs`.
The 2026-11-10 isolated editor run measured a 131.5 ms full-world stamp versus 4.4 ms for a local stamp,
and 617.5 ms full-world release versus 11.2 ms for queued release. Combined deferred surface/prop work had
a 13.8 ms median and 15.6 ms maximum per measured update. These are warm operation timings, not whole-frame
guarantees. All 66 focused career/road/terrain/save tests passed, including nearby-structure protection.

`NaturalLandscapeReview.FromCommandLine` captures the actual seeded site, naturally placed boulders and
forest floor under `Captures/NewGame/NaturalLandscape`, then prepares an airport with paid manual operations
and runs a real charter. It uses the isolated validation project while the main Unity editor is open.

`PlayerConstructionPlayReview.Run` drives the actual mouse-controlled clearing/levelling tools and runway
drag in Play mode, captures all task/tool stages under `Captures/NewGame/PlayerConstruction`, then verifies a
placed airport, save/load and charter. Its save slot is isolated under `Logs/PlayerConstructionPlayReview`.
`CareerPlayReview.Run` remains an alias for this review. `CareerRoadPlayReview` retains the original-road
compatibility fixture.
`CareerHudPlayReview.Run` captures the live site-selection screen, two/three/eight earned tool states,
active clearing/levelling controls and a narrow Game view under `Captures/NewGame/CareerHud`.
