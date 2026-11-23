# New Game: build a regional airport

Open `Assets/AirCity/Scenes/NewGame.unity` and press Play. Career and Sandbox use this same scene, airport and tools. Choose the mode in **Options > Switch to Sandbox / Career**, including during region selection. The old `Sandbox.unity` is a populated development fixture. **AirCity > Game > Update New Game World Assets** updates references and installs
missing player tools while preserving the scene layout. **Create New Game Scene** recreates the scene.

## Shared game architecture

`CareerGame` is the shared session host (the serialized class name is retained). `GameMode` changes funds,
progression and guidance. Switching mode keeps the airport, public-road graph and camera, and does not grant
career unlocks. Both modes use `DevAirfieldTool` / `DevToolbar` for construction and the same terrain/reception
operations. A new feature belongs in these shared systems, never in a second mode-specific implementation.

`AirportSaveData` saves the entire authored airport: multiple runways, the taxiway/service-road graph, stands,
aprons, terminals and bridges, maintenance depots/fleets, baggage halls/teams, container stations and deicing
facilities/equipment. Object references are remapped to local component identities or stable GUID/local-file
asset keys from `GameAssetCatalog`. Restore creates all components inactive, reconnects references, then enables
geometry and simulation. `SharedGameAssets` prepares the catalog and common equipment defaults before Play or
a player build; **AirCity > Game > Update Shared Game Assets** also refreshes it explicitly.

Save version 3 adds the game mode and full airport data. Versions 1 and 2 migrate through their existing starter
layout; after a save, that starter data is only the tutorial's projection. The existing atomic save/backup and
background snapshot queue serve both modes. The historical on-disk `Career/career.json` path is retained so
existing saves remain discoverable. Switching modes updates this same save.

Roads use one `PublicRoadConstruction` operation: validate a detached graph, check funds, and only commit a
successful connection. It charges road-type prices in Career and skips payment in Sandbox. `PublicRoadsView`
can delegate drawing to `ValleyView`, so the shared tool edits the valley graph directly. The first connection
from an airport road node to a region boundary road node satisfies the access objective.

## Player experience

Choose **Snoqualmie Valley**, **Yosemite Valley**, **Bora Bora** or **Moab** for an offline region.
**Choose on globe** opens a rotatable, zoomable globe with these four marked locations. Click the globe
or type decimal latitude/longitude to select a different area, then **Download & preview**. The globe
uses NASA Blue Marble imagery and the selected region uses real elevation and WorldCover classifications.
The first download requires a connection; a verified local cache serves subsequent previews offline.
Download progress and cancellation are shown, and a failed/cancelled request retains the current preview.
Current common provider coverage is 60°S–83°N; unsupported selections are explained before download.
Location replaces the player-facing seed: geographic terrain stays north up. Pan, orbit and zoom to explore
the generated region before starting.
**Start building** opens the entire 6.4 × 6.4 km region without a land purchase. There are no purchasable parcels
or suggested airport boundaries in new games. More distant airports need longer paid road connections, and
rougher ground requires more paid earthwork.

The compact 40-unit top bar shows time and date together, temperature and season together, funds,
pause/resume and 1×/5×/20× speeds. The extra authority/funds captions are removed; text uses smaller
13–14 unit sizes. **Options** opens a dropdown with Save, Open saved airport, focus, new game and mode
switching. Open uses the existing latest-airport save slot; there is no second save system. Speed buttons resume the simulation. The calendar advances one displayed
minute per simulation second and is derived from saved `operatingSeconds`; pause freezes it along with
flights, vehicles and road signals. Temperature follows the selected biome, season, daily cycle and seeded
weather. The top bar reserves flexible space for future statistics and wraps on narrow screens.
The dropdown consumes pointer input and closes on an outside click or Escape. Below 680 units wide, controls
wrap into a 72-unit bar while each time/date and temperature/season group stays on one line. **H** hides the HUD.

After starting, the compact task card sits below the top bar. Select, Clear trees, Level ground and Runway
are available immediately, so open desert terrain does not require clearing a tree to progress. Build a
runway to earn Taxiway, connect a taxiway to earn Parking, place parking to earn Public road, and connect
an airport road to earn Reception. Earlier tools remain available. Sandbox unlocks all tools and builds free.

Players choose the runway position/direction, taxiway paths, parking side, road entrance, route and reception
position anywhere within the region. Runways keep a straight natural incline between their thresholds;
their quote includes the cut/fill needed to remove bumps and crossfall. Clearance, protected construction,
water and affordability are checked before any terrain or funds change. The first runway in an otherwise
empty airport anchors the airport datum at its higher threshold, preserving the world profile and allowing
flat facilities on the upper plateau. Taxiways and service roads can continue along terrain grades with
automatic earthwork. Stands choose their own saved elevation and automatically prepare their pad;
terminals and aprons still use prepared ground. Existing
saves retain their earlier owned parcel and progression.

## Controls

- **Clear land:** click/drag a circular brush to remove trees, rocks, fallen timber and shrubs, or switch to
  **Tree** to remove one selected tree. The actual price follows the brush on the map. The compact tray
  contains only the mode switch and a radius rail with its current value.
  Brush clearance prices trees at $24 each and natural obstacles by kind and size; grass and ferns remain.
  Removed rocks, stones, logs and shrubs are saved by deterministic prop ID. Clearing a boulder does not
  remove neighbouring cluster stones outside the brush or change terrain heights. Only affected instance
  tiles refresh; later loading and full/local scatter honour the same saved removal IDs.
- **Level ground:** click or drag the brush over cleared land. Type a target height directly, use precise
  half-metre buttons, or hold a button to accelerate; target heights stay within 10–639 m. Adjust the radius
  with the same rail. Price and any refusal reason appear beside the map brush, rather than in the toolbar.
  Trees must be removed first. The quote comes from the actual earth volume; grading never removes trees.
  One stamp completes its flat core and clamps its feather to an absolute shoulder envelope. Repeating
  the same point, radius and height changes no more terrain and costs nothing; choosing another height
  or area is new work. Preview and commit measure the exact same changed volume.
  The completed spot stays anchored under a stationary pointer when the camera follows raised ground
  vertically. Moving the pointer, panning/orbiting/zooming or changing height/radius releases that anchor.
  Brushes that could disturb existing runway/taxiway pavement, parking pads, reception, public roads or bridge
  supports are refused, including at the airport elevation. Keep the brush clear or demolish the affected
  structure first. The protected margin includes one terrain sample beyond the brush for interpolation.
- **Runway:** click or drag between thresholds. A new small airfield defaults to an 18 m grass strip; the
  shared catalog also offers the other surfaces and widths. Build at least 700 m for the first local charter.
  Start at an existing threshold to extend the same runway. Its taxiway links and surface settings are retained;
  Career pays for the added length and its actual earthwork. Clear trees, rocks and logs from the footprint
  first. A smooth hill remains inclined, while a crooked or cross-sloping surface is automatically graded
  with a feathered terrain edge. The world quote shows the total cost, including grading; invalid or unaffordable
  placements do not change the world. New Game uses natural terrain beside the runway instead of generated
  grass side strips. The saved grade also moves paint, lights, taxiway joins, aircraft and maintenance vehicles.
- **Taxiway:** click points to build connected sections; right-click ends the chain. The starting choice is
  7.5 m grass, and the shared catalog offers larger paved taxiways as the airport grows. Taxiways and service
  roads sample the actual terrain at free endpoints and retain the engineered height of snapped pavement.
  They automatically grade bumps/crossfall and support the complete pavement, including bends and junctions.
  The map quote shows the total cost. The preview includes the actual bend and re-trimmed adjoining
  approach, with an outline following the pavement boundary. Placement uses that same quote, so an unprepared slope no longer looks
  valid and then silently refuses the click. Taxiways allow up to 15% grade, service roads 20%; excessively
  steep bends, water and earthwork conflicts show their reason before purchase. Accepted taxiway/service-road
  construction clears vegetation automatically, included in its road price; previews and rejected clicks
  remove nothing.
  New Game omits taxiway grass side bands, including when restoring existing layouts.
- **Parking:** use Stand and click beside a taxiway. The shared gate workspace provides size, surface, orientation,
  exit and equipment settings. A new small airfield defaults to code A taxi-out parking, so the charter does not
  require a push-back tug. Bigger airports use these same tools. A free stand sits at the local terrain height;
  an attached stand meets its taxilane with a short graded approach. The map shows the total price before clicking. Accepted placement clears vegetation and grades the pad; a red preview explains refusals.
  Grass/dirt pads use this same transaction. Stand meshes rebuild before terrain ownership and saving refresh.
- **Street:** choose the public road type and click successive points. Ends snap to existing road nodes or sections;
  crossings make junctions. Right-click ends the chain. Each accepted section is a transaction (paid in Career,
  free in Sandbox); a cancelled first point does not leave a split road behind. Vegetation in an accepted
  road footprint is cleared automatically. Connect
  a new airport-end road node near parking to a region boundary road to satisfy the career access objective. Public roads
  must stay clear of aircraft pavement. The preview shows the actual proposed bend and elevation, with a compact total-price label. Cursor validation queries
  clearance and terrain inputs; it reuses an unchanged quote. Accepted geometry supplies the exact
  vegetation footprint instead of checking the whole forest while moving the cursor. Free endpoints use one-metre cursor steps; snapped engineered profiles retain full precision.
- **Visitor centre:** place the pavilion and its 18-bay parking lot beside a connected public road. Prepare
  the complete outlined site at the airport height. The driveway leaves the lot's right side and connects to
  the public-road graph; the quote includes that connection. Building, parking and driveway follow the cursor
  before purchase. See [visitor centre](visitor-centre.md) for site dimensions, saved layouts and road access.
  The tool shares the HUD's **Turn − / angle / +** controls and **Snap 15° / Free angle** toggle.
  **Z / Shift+Z** turns ±45° (**R** remains an alias); **Shift+wheel** turns in 15° snapped or 5° free
  increments, **Alt+Shift+wheel** in 1° increments. Rotation consumes the wheel so the camera stays still.
- **Esc** cancels the active tool/draft. **WASD**, middle drag, right-drag and scroll control the camera.
  Buttons and trays consume pointer input. Pause and 1×/5×/20× stay in the top bar; Save and Open are under Options.

The first task sequence introduces these tools. Once the airport is open, schedule a local charter.
The existing aircraft simulation performs arrival, taxi, boarding and departure, with a $250 operating cost
and $1,800 service fee paid only after completion. The initial balance is $10,000,000. The next goal encourages
a 1,000 m runway, and more charters remain available. Construction is disabled during an active flight.

## Saved layout and world

`AirportSaveData` saves the full placed airport in both modes. `CareerLayout` keeps tutorial unlocks, clearing/earthwork history, reception and a projection of the first runway/stand for the existing objectives. `CareerConstruction` supplies terrain/reception operations and legacy migration fixtures. `DevAirfieldTool` handles shared airport construction, and `PublicRoadConstruction` owns street transactions. `CareerHud` adds the permanent top bar, region chooser and career objectives to the shared workspace. `CareerGame` restores the real airport from its full saved data.

`ValleyWorld` saves the full 6.4 km heightfield, trees/removal flags, river/road/sites and generator version.
Forest density has its own saved `forestVersion`: mapped woodland uses irregular 8 m candidates and
tropical forest uses 6 m candidates with
overlapping mature crowns, smaller young trees and local gaps. Fields, towns, water, roads and steep cliffs
remain clear or thinly vegetated. Older mapped saves receive an additive density pass once, preserving
all saved roots and removal flags, disturbance with a two-cell halo, airport bounds and road corridors.
Version 2 adds density to previous tropical saves, including low reef woodland and forested volcanic slopes;
version 1 temperate populations stay intact. Legacy generator 1–3 populations stay intact.
The shared tree limit is 800,000 and save reads allow up to
128 MiB so dense forests can be opened again. Construction uses local spatial queries and clearing only
compacts affected instance buffers; see [forest trees](../assets/ForestTrees.md).
Bundled real elevation foundations come from Mapzen Terrain Tiles: Snoqualmie Valley (Woodland),
Yosemite Valley (Mountains), Bora Bora (Tropical) and Moab (Desert). New Woodland regions use a wooded
inland valley with a narrow mapped river and small lakes, rather than an ocean inlet; earlier Hood Canal
snapshots retain their saved terrain. Source crops span 8 km (12.8 km for
Bora Bora), resampled into the 6.4 km game region with recorded vertical scaling/offset so relief fits the
terrain's height range. These are adapted landscapes, rather than geographically exact airport replicas.
ESA WorldCover 2021 supplies aligned water/landcover masks. This keeps lagoons open where the coarse DEM
contains false positive coastal heights, preserves reef islands missed by elevation samples, and avoids
forest placement in built-up areas, crops, bare ground and water. The four curated source samples are baked offline; starting
a curated region or loading a saved airport never requires an elevation service. All source URLs, transforms, checksums and license
credits ship under `World/Resources/AirCityRegions`; see `ArtSource/World/README.md` for regeneration.
Geographic generation preserves north, east, elevation and drainage. Internal seeds affect roads/vegetation;
they do not rotate the selected location or invent map-wide cliffs and rivers. Existing generator 1–3 snapshots retain their exact terrain/hydrology.
`GenerateLegacy` remains only for original construction/regression fixtures.
Generator 5 rounds the mapped ocean coast using a smoothed signed-distance contour and a continuous
beach/seabed slope. This changes saved terrain geometry instead of masking square edges with foam.
The narrow coastal band retains mapped reef islands and land bridges; inland rivers and deep ocean
remain unchanged. Loading a generator 4 ocean world upgrades its natural coast once, preserving all
saved disturbance plus its interpolation halo, airport bounds and road corridors. It works from the
current saved heightfield and never regenerates the region. Later player earthwork stays untouched.
Ocean and inland-water regions display the saved 513×513 heightfield on a 2049×2049 terrain/collider using bilinear
subdivision, matching the world height sampler without overshoot. Local brush updates use the same
source-to-display mapping; roadbed caps remain in force. Ocean depth/shore wash now uses the full
513×513 terrain source detail so foam follows the coast.
Generator 6 extends the same treatment to inland banks, with local varying water levels instead of a
single sea plane. A derived median/local filter removes DEM spikes without flattening a whole river
or changing the original saved water-level samples. The water sheet extends under the bank and uses
fine shoreline triangles, so the terrain intersection defines the visible edge. Ocean and elevated
freshwater can coexist. Generator 4/5 inland regions upgrade their natural banks once while preserving
player disturbance, its interpolation halo, airport bounds and road corridors. Earlier Woodland saves
keep their stored Hood Canal terrain; only new Woodland maps use Snoqualmie Valley.

`LocationTerrainLoader` requests Mapzen Terrarium PNG tiles and byte ranges from ESA WorldCover 2021
cloud-optimized GeoTIFFs. It decodes original uint8 classifications rather than interpreting a rendered
colour map. `WorldCoverTiff` handles the provider's tiled lossless TIFF blocks. Complete crops are cached
under `Application.persistentDataPath/TerrainCache`, keyed by location, extent, resolution and source
version, with SHA-256 checksums. See [the download pipeline](location-terrain.md) for provider coverage,
cache details and the coastal classification heuristic. Downloaded locations save coordinates, source extent and all generated
height/landcover/water data in the airport snapshot; loading a save does not redownload or regenerate it.
Brush earthworks modify only selected height samples within the buildable region and preserve blended edges.
Ground roadbed grading also caps every contributing terrain sample beneath pavement and shoulder vertices,
keeping irregular junctions and cliff cuts clear. Legacy 2D access routes join the actual interpolated ground-road
profile rather than the ungraded terrain height. Terrain uses the detailed surface shader throughout overview
distances to avoid discontinuities at the former basemap handoff. Steep surfaces use a fractured CC0 rock-face
scan projected in world space from three axes, with matched normals and broad weathering variation. This
avoids stretching horizontal ground textures up cliffs; the shader generator reproduces all URP passes.
A saved disturbance field records clearing/soil exposure. See [public roads](public-roads.md) for their
independent 3D graph, saved entrance node, route and signal clock.

`ValleyView` uses Unity Terrain. Six texture layers provide meadow, dry grass, exposed soil, rocky ground,
forest floor and road underlay. The meadow, litter and rock use photo-scanned CC0 surfaces with normal and
occlusion maps, rather than mud reused as rock. Source/processing details are in
[`ArtSource/World`](../../ArtSource/World/README.md). `ValleySurface` uses Burst jobs to combine warped small
patches, actual canopy coverage, height/slope, river banks and saved earthworks.
Terrain layers use constant matte smoothness; opaque albedo alpha must not become a glossy ground surface.
`ValleyTerrainSampling.hlsl` blends three stable random texture offsets, with matched albedo/normal/mask
lookups and explicit mip gradients. This breaks repeating scan stripes. Those lookups are most of the terrain's
cost, so `ValleySampleWeighted` skips a layer the pixel does not show (its splat weight) and any lattice sample
under 1%, and `ValleyCliffs.hlsl` likewise skips projections and patches under 1%; on 2026-11-19 this cut the
terrain's frame time by 25–35% in Woodland and about half in Tropical, with no visible change. `ValleyTerrainShaderSetup` rebases
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
global fields as full rebuilds. Clearing never rebuilds terrain heights or roads; it filters paid removal IDs
from affected groundcover tiles while preserving other tiles and neighbouring instances. Single-tree removal
leaves the groundcover collection unchanged. The [instanced forest](../assets/ForestTrees.md) uses shared
biome palettes rather than individual tree objects.

`GroundCoverSetup` version 3 generates twenty shared near/far models: weathered boulders and ledges, broken
stones, fallen trunks, curved grass/sedge tufts, compound ferns and several low/wide woody shrubs.
`GroundCoverScatter` uses stable seed/cell candidates and the original woodland habitat. Rock outcrops follow
a separate 52 m patch field, with more exposed stone on rocky slopes/bare ground and small stone aprons
around larger boulders. Some larger outcrops remain visible from the normal builder camera; forest shrubs
and ferns vary in size, and fallen wood breaks up the ground silhouette. Mapped crop/urban/water pixels
stay open. Grass and fern meshes use fewer tiny blades/leaflets so detail goes into larger visible props.
Clear land removes and prices rocks, fallen timber and woody shrubs as well as trees; grass and ferns
remain after clearing. Grading suppresses ground detail on worked soil. Stable prop IDs persist individual
removals across full/local scattering and saves.
Actual built pavement excludes full prop footprints through the shared [built-surface map](built-surfaces.md).
Reception, settlements and water retain their separate exclusions.
`GroundCoverRenderer` draws spatial GPU instance batches with frustum/distance culling, near/far dithering,
rock normal maps, leaf wind and shadows. Actual transformed model bounds contain large outcrops and
fallen logs, with cheap distant meshes and shorter shadow ranges. Per-camera submitted triangle, visible
rock and debris counters are available for the world review. Tree-only strokes retain these instance batches. Groundcover
suppression ignores tree-clearing disturbance samples before interpolation, so clearing beside an old grading
edge cannot remove plants when the save is later loaded.

The tutorial still tracks one runway, parking stand, reception and public-road connection at one airport
elevation. The shared airport model and tools support additional runways, terminals and service systems;
the complete authored airport is saved independently of this tutorial projection. Three hidden legacy site
anchors remain in the world format for existing saves and fixtures; they do not restrict new-region play.

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
its automated construction actions. Current saves use format 3; formats 1 and 2 remain readable and are
upgraded on Continue. New saves retain `wholeRegion`, biome and hydrology in detached snapshots; older
saves default to their original parcel rules.

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

## Shared-session verification

The final focused suite passes 74 checks (`Logs/unified-focused-final.xml`).
`SharedGamePlayReview.Run` verifies actual mouse street placement and runway extension, mode switching,
full airport save/reload and both HUD sizes under `Captures/SharedGame`. The broad suite has one known native
baggage scene-opening render-texture warning, reproduced against the pre-integration source; see the detailed
verification notes in [the road handoff](../roads-todo.md#shared-game-integration-verification-2026-11-14).

## Region and HUD verification

The 2026-11-14 terrain/construction pass ran 355 relevant Game, World, airfield, traffic, maintenance and
vehicle checks: 354 passed, no failures; the explicit external terrain-download check was skipped. Long
traffic soak scenarios were excluded (`Logs/terrain-construction-tests.xml`). The run includes saved prop
removal, idempotent brush cut/fill, grading quotes, preserved legacy verge maintenance, all sloped runway
mesh/aircraft/vehicle support contracts, upper-plateau connections, preview support and portable saves.

`BrushControlsReview.RunFromCommandLine` verified actual 1440 × 900 and 680 × 650 Game views, the radius
rail, typed height and accelerating held controls. A real map click cleared 186 trees and 58 props for its
exact $6,536 quote. Levelling reached the requested target for $1,116; clicking again at the same screen
position changed neither terrain nor funds. Clearing and terrain survived an isolated save/load.
See `Captures/NewGame/BrushControls` and `Logs/brush-controls-review.json`.

`RunwayBuildReview.RunFromCommandLine` uses a controlled 12% hill with bumps/crossfall, rather than a claimed
real-world airport. Actual map clicks placed a roughly 700 m asphalt runway, from 100 m to 184 m, for its
exact $29,212 quote ($15,212 grading). Preview did not alter terrain/funds, all 63 pavement support samples
matched the graded terrain, no grass-strip mesh was present, and portable save/restore retained the profile.
Desktop quote and committed desktop/narrow/low-angle captures live in `Captures/NewGame/RunwayBuild`;
see `Logs/runway-build-review.json`.

**AirCity > Game > Run Region Tests** runs the affected Game/World EditMode suites in an already open editor,
writing `Logs/region-tests.xml`. These cover free full-region starts, remote clearing/grading/runway placement,
legacy parcels, seed reproducibility, biome differences, cliff slopes, coherent river banks and the saved clock.
**AirCity > Game > Review Regions and HUD** captures four biome previews, a close cliff view, another seed,
a random region, and the started HUD at wide/narrow sizes under `Captures/NewGame/Regions`. It uses an isolated
save slot, restores the original scene setup and leaves the editor running. The batch entry point is
`AirCity.EditorTools.Game.RegionPlayReview.RunFromCommandLine`.

The 2026-11-14 final region run passed **107/107** Game/World tests in Unity 6000.6.7f1
(`Logs/region-tests.xml`). Live review passed ten captures, including palms at close range, cliff faces,
seed 42/43 layout differences, and the HUD at 1440 px and 680 px widths
(`Logs/region-play-review.json`). The review uses an isolated save directory and restores the scene setup.

## Inhabited regions and coastal access

New region roads are planned over dry lowlands, with a saved vertical profile limited to 7.5% grade. Graded
cut slopes and embankments join the road to the surrounding terrain; pavement support follows actual mesh
planes, rather than lowering an entire terrain sample to the lowest nearby vertex. New manual ground roads
refuse grades above 12% and water crossings before charging. See [public roads](public-roads.md).

Island road endpoints carry an explicit `outsideConnection` flag for ferry/harbour access; progression does
not require a road to reach an ocean-covered map boundary. Older boundary-node connections keep working.
Road routes, profiles, outside flags, landcover, inland water levels and DEM source information survive detached snapshots/load.

`RegionWaterView` renders an ocean beyond the terrain boundary, with depth-coloured lagoons, animated ripples
and shore wash. Mapped inland water also retains local water-surface elevations for rivers/lakes in the
mountain and desert regions, rather than treating every water body as an ocean at sea level. `RegionSceneryView` replaces identical settlement grids with small road-aligned villages,
varied gabled houses, windows/verandas, gardens and grounded driveways. Suitable coastal road endpoints gain
a fishing/ferry jetty with moored boats. Scenery avoids water, trees, steep land and airport footprints.
`RegionTrafficView` draws twenty regional cars in GPU instance batches, following the saved road profile.
Cars, ocean animation and moored boat motion share the road simulation clock; pause freezes them. Region
previews use an ambient clock before the player begins building. These are regional scenery, not a new
passenger/cargo economy or a shipping simulation.

`AirCity.EditorTools.Game.RealLandscapeReview.RunFromCommandLine` writes synchronous world-only renders
under `Captures/NewGame/RealLandscapes` and a scenery/road diagnostic report. It is intended for an isolated
validation project and does not depend on an interactive Game view or alter the user's active Play session.

The real-landscape validation run on 2026-11-14 passed **122/122** affected Game/World tests
(`Logs/real-region-tests.xml`), including saved local river elevations, dry/wet ground, street joins onto
engineered profiles, road support and old-save compatibility. Fourteen world-only renders were inspected
under `Captures/NewGame/RealLandscapes`, including the volcanic lagoon/harbour, fractured cliff surface,
all four road-support views, inland rivers and the small settlements. Metal shader compilation was checked
with the final ocean fog correction. Validation used an isolated project; the active Play session was left open.

**AirCity > Game > Review Compact HUD** captures the actual Game view and IMGUI at 1910, 1440 and
680 px widths, including the Options dropdown. It checks outside-click dismissal, Open and Save against
an isolated checkpoint. Run `AirCity.EditorTools.Game.CompactHudReview.RunFromCommandLine` in a graphics-enabled,
non-batch validation editor; outputs are `Captures/NewGame/CompactHud` and `Logs/compact-hud-review.json`.

The compact HUD/coast validation on 2026-11-14 passed **130/130** affected Game/World tests
(`Logs/compact-coast-tests.xml`), including refined terrain/collider brush updates, local seams, roadbed
limits and the protected generator 4 coast upgrade. The actual HUD review checked Save, Open and outside
dismissal at 1910, 1440 and 680 px. Coastal close-ups include both the main island and narrow motu.

The inland-water/globe validation on 2026-11-14 passed **161/161** affected Game/World tests
(`Logs/inland-globe-tests.xml`), including old thin-river rendering, protected bank migration, north-up
geographic generation, mixed ocean/elevated freshwater, source metadata and exact saved terrain.
`AirCity.EditorTools.Game.GlobePickerReview.RunFromCommandLine` runs in an isolated graphics-enabled
editor. Its actual 1440/680 px Game view review passed curated previews, globe rotation/zoom/picking, a
cold download at 47.30°N/0.73°E, exact cache reuse, custom previews and saved-world roundtrip
(`Logs/globe-picker-review.json`, eleven screenshots under `Captures/NewGame/GlobePicker`).

The final river render pass inspected all four locations and close inland banks. The water mesh boundary
diagnostic found no exposed sheet edges (all tested inland edge midpoints were at least 0.244 m below
the terrain). Inland water uses depth colour without ocean surf rings; coastal shore wash is unchanged.
Saved grids and source masks still limit very close shoreline detail, even with the finer display mesh.

## Taxiway and road terrain verification

The 2026-11-14 pavement pass selected 477 broad Game, World, Airfield and Vehicles cases: **476 passed,
one explicit live terrain-download case skipped**, no failures (`Logs/airside-regression.xml`). The final
focused run passed **58** road/shared-save/runway/airside checks, including fourteen airside terrain cases
(`Logs/airside-final-targeted.xml`). Across both runs: **480 unique passing cases, one skipped**. Long traffic
soaks were excluded. The final cases cover actual sloped-runway departures onto taxiways, graded T-junctions,
trimmed-approach splits, compatible nearby public roadbeds, atomic stale-graph rejection and natural soil
beside lightly adjusted pavement.

`AirsideBuildReview.RunFromCommandLine` verified actual mouse construction of a sloped taxiway, its bend,
a service road and a public street in an isolated graphics editor. Previewing changed no ground or funds;
accepted clicks charged the displayed total of $30,830 and local rendered terrain supported the pavement.
Portable save/load retained all node elevations, grading and natural edges. Nine real Game-view images cover
1440 × 900, 680 × 650 and close/low-angle views under `Captures/NewGame/AirsideBuild`.
`Logs/airside-build-review.json` records the result and warm Street-preview operation means with 220,000
additional distant trees: 5.9 ms moving, 0.004 ms stationary. These measure the tool operation in the isolated
editor, rather than complete frame time.

## Pavement ownership follow-up (2026-11-15)

Terrain support, terrain shader coverage and nature exclusion now share the committed pavement geometry;
see [built surfaces](built-surfaces.md). This prevents grass strips/asphalt being buried by terrain LOD or
later earthworks. All terrain render passes share exact triangle clipping, while source-height resampling
retains the existing pavement formation. Runway construction boundaries use the overlay at any zoom.
Road placement clears vegetation automatically and refreshes visible tree/prop batches immediately.
The old tutorial runway/taxiway/stand approximations no longer determine ground-cover exclusion.

Verification: 277 broad regression cases passed with one external-download skip; the final focused run
passed 120 cases (278 unique passing cases across both runs). Actual mouse construction cleared 26 trees
and 177 props, retained the quoted $33,670 total and survived twenty terrain updates and a forced three-metre
rendering stress. Save/Continue and the grass/asphalt pixel checks passed. Ten captures and reports are in
`Captures/NewGame/PavementOwnership` and `Logs/pavement-ownership-review.{json,txt}`.


### Placement responsiveness and ground appearance (2026-11-15)

Road/runway ghosts retain their exact curves and validation while reusing preview buffers and spatially
indexing elevation/vegetation queries. Terrain grading samples use Burst. Moving a runway ghost no longer
invalidates the committed airport's routing/elevation caches. Local commits refresh their actual affected
area; automatic grading does not queue terrain repaint or mark exposed soil. The terrain painter no longer
adds a blurred asphalt layer or uses old disturbance as a dirt tint, so existing saved construction halos
also disappear when loading. Physical road shoulders/sidewalks and the built runway's own material remain.
See [built surfaces](built-surfaces.md#placement-performance-and-unchanged-surrounding-land-2026-11-15) for
benchmark and verification details.

The public Street tool also caches unchanged regional roads while the cursor moves; its detached candidate
still resolves all curves, junctions, arrows and terrain clipping exactly as placement does. The warm isolated
fixture measured about 9.58 ms per moving Street preview, 16.15 ms for service roads and 2.40 ms for runways.
These measure construction CPU work, not complete frame time. Actual placement/cancel/save/load checks are
archived in `Logs/placement-speed-review.json` and `Captures/NewGame/PlacementSpeed/`.


## 3D road integration (2026-11-15)

Career and Sandbox share the same 3D road tools: curves, vertical profiles, automatic structure spans,
bridges, tunnels and airside underpasses. Detached construction plans supply the preview and accepted graph;
no second placement calculation runs on click. Existing straight saves load with default alignment data.
The full airport binds its terrain-height provider before regenerating restored bridge supports and tunnel
portals. Controls and terrain/vehicle contracts are in [public roads](public-roads.md#shared-3d-construction-2026-11-15).
