# Built surfaces and terrain

Built pavement owns its footprint. Terrain provides the supporting land, while the runway, taxiway,
service-road, public-road and apron builders retain their own materials, markings and maintenance state.
`World/BuiltSurfaceMap` is the common source for ground support, terrain rendering and nature exclusion.
It replaces the old tutorial's approximate runway/taxiway strips as the main game's pavement exclusion.

## Geometry and ownership

`ValleyBuiltSurfaces` reads committed airport meshes after they have rebuilt, transformed into world space.
It includes runway cores, taxiway/service-road surfaces and junctions, free/connected stand pads and aprons.
Vertices with transparent fringe coverage are excluded; natural terrain remains under those ragged edges.
A runway ghost never owns terrain. `PublicRoadGeometry.groundTriangles` records the actual ground carriageway
and shoulder/sidewalk triangles while building straights, curves and intersections. Elevated decks are omitted,
so terrain and nature remain below bridges.

The immutable map buckets triangles into 32 m cells across the region. CPU and Burst queries use exact
triangle tests, including a model's full footprint radius. Terrain shader passes use the same indexed
triangles through structured buffers. This avoids a low-resolution mask's stair steps or expanding a road
into a wide rectangular exclusion. Runtime buffers and foundation limits are rebuilt when the airport's
layout or public-road geometry changes, then disposed with the old map/world. Brush edits reuse the map.

## Persistent foundations

Airport pavement triangles also define limits at every saved heightfield corner whose cells they touch.
A Burst job derives those limits from the actual planes. `TerrainHeightmap.Resample` constrains supporting
source samples to a thin formation below the pavement and protects the wet bed and dry bank at waterlines
before refining the native heightmap. Both local
updates and complete rebuilds use this path, including their terrain collider.

Subsequent earthworks cannot raise soil through an existing surface or drop away its support. Saved source
heights remain the editable earthwork data; the airport's saved layout rebuilds its formation on load.
Public streets retain their own crown-aware `PublicRoadGeometry` formation, which already accounts for
junctions, shoulders and embankments. New Game runway commits rebuild their mesh/root profile before the
valley reads that geometry.

## Rendering

`ValleyBuiltSurfaces` binds the index with `Terrain.SetSplatMaterialPropertyBlock`. `ValleyBuiltSurfaces.hlsl`
clips soil in terrain forward/GBuffer, add, distant base, depth, normals and shadow passes. Each footprint
carries a lower clipping depth. Road edges and slabs supply their physical thickness, and covered bores
also supply a roof height. Soil below the lower limit still renders. Public road kerbs own the strip
between the asphalt and the shoulder so the terrain cut covers the full road width.
Open tunnel approaches retain a full cut between their retaining walls. Construction drafts have separate
forward-only overlay materials, so ungraded hills cannot hide
their proposed surfaces. Those temporary copies never change committed pavement or own terrain. The GPU query checks only the current spatial cell, rejects degenerate triangles and discards once
after the ownership test. Unbuilt land and transparent surface fringes retain ordinary terrain rendering.
The lit and normals passes discard at the end, after their layer and cliff lookups. Those lookups skip unseen
layers inside dynamic branches, and on Metal (Apple GPUs) a discard before such a branch left blocks of
garbage pixels along every road edge (2026-11-21). Keep the discard after any branch that samples a texture.

`ValleyTerrainShaderSetup` reproduces every extension from the installed URP source, including the custom
base pass. Terrain shader targets are 4.5 for the desktop structured-buffer path. Runway construction rings
and outlines use the existing construction overlay, so their visibility does not depend on terrain depth.

## Vegetation and transactions

Taxiway/service-road and stand/de-icing-pad quotes allow trees and ground props. An accepted `AirsideTerrainPlan` removes its
quoted roots and obstacle IDs; clearance is included in the construction price. Insufficient funds, changed
ground/layout/forest or another refusal clears nothing. The Street tool likewise permits vegetation. Stand pad quotes use the same formation transaction, preserving existing pavement support while levelling the new pad. Their meshes rebuild before ownership is refreshed, including immediately after the first placement.

After committed geometry changes, the shared game clears roots and prop footprints under the precise built
map, updates saved removal flags/IDs and refreshes the visible forest batches. This also repairs legacy
pavement on loading. The renderer immediately removes every overlapping ground model, including grass and
ferns. Deferred tile updates, full scatter and reload all filter through the same map, so plants cannot grow
back through the road. Pending prop candidates are retained before immediate filtering so their cleared IDs
can be saved. The separate tutorial reception still has its own building exclusion.

The Level and Clear land brushes retain their existing transactions. Runway placement still requires its
larger natural-obstacle footprint to be cleared first.

## Verification

`BuiltSurfaceMapTests` covers exact spatial boundaries, transparent grass fringes, public-road curves and
sidewalks, vegetation below elevated roads, whole-model exclusion across immediate/queued/full scatter,
saved nature removals and repeated raising/lowering of supporting ground. It compares local and full
height/collider updates and checks every generated terrain shader pass. `AirsideTerrainTests` also verifies
non-mutating previews, paid construction through vegetation and stale-forest atomicity.

`PavementOwnershipReview.RunFromCommandLine` runs in an isolated graphics editor with a synthetic hill and
bounded dense vegetation corridors. It uses actual mouse placement, its own save slot, a 2049 native terrain
and real Game-view captures. It checks runway boundary pixels at far zoom, unbuilt terrain, immediate tree
and prop disappearance, twenty repeated terrain updates, a deliberate three-metre native-terrain rise,
asphalt pixels before/after Save and Continue, and grass-runway pixels over dirt. Shader compilation is
synchronous for the review, with the editor setting restored afterward, so captures cannot accept shader
compilation placeholders. Reports are `Logs/pavement-ownership-review.{json,txt}` and screenshots are under
`Captures/NewGame/PavementOwnership`.

The 2026-11-15 validation selected 278 broad game/world/airfield/routing/maintenance cases: 277 passed and
one explicit external terrain-download case was skipped. The final focused game/terrain/vegetation run
passed all 120 cases. Across both reports: **278 unique passing cases, one skipped, no failures**.
Reports: `Logs/surface-ownership-{regression,final}.xml`.

The native review passed at 1440 × 900 and 680 × 650. It checked all 76 sampled far-zoom boundary points,
preserved unbuilt terrain, cleared 26 tree roots and 177 prop slots through accepted road construction,
and charged the displayed total of $33,670. All 21 sampled asphalt locations survived the deliberate
three-metre terrain rise and actual Save/Continue; 25 grass-runway samples remained green over dirt.
The fixture produced 11,244 owned triangles and ten Game-view captures. These are controlled synthetic
geometry/rendering checks, independent of a particular downloaded region.


## Placement and turf follow-up (2026-11-15)

Road/taxiway, public-Street and runway ghosts use private construction surface/paint materials with an always-visible
forward pass and no depth writes. A proposed road can therefore be reviewed through the hill that its
accepted earthwork will remove; committed materials retain ordinary depth testing. Preview copies are
released on commit/cancel and never change shared assets.

Stand/de-icing ghosts now run `AirsideTerrainPlan.TryCreateStand`, sharing the same grading, clearing,
affordability and stale-source checks as the accepted click. There is no later prepared-ground check that
can silently veto a green ghost. Stand pads save their local Y level and retain it when added/copied/turned;
free pads use local ground, attached pads blend onto their taxilane over 12 m. Earthwork preserves existing
pavement foundations. The network rebuild precedes the map/vegetation refresh and autosave.

Grass-strip shading adds metre-scale damp/dry clumps, compacted wheel paths and filtered mower bands using
the existing material textures. This adds variation beyond the close photo texture's distant mip.

`AirsideTerrainTests` exercises pure/paid grass, asphalt and de-icing pad placement through vegetation,
height retention across a portable save, attached graded taxilanes, refused prices/regions and stale soil.
The broad follow-up regression passed all 158 selected game, airfield, maintenance, stand and ownership
cases (`Logs/placement-followup-regression.xml`).

The extended native review also passed actual grass and paved stand clicks, a red unaffordable-stand ghost
before clicking, exact stand quotes and saved pad heights. It sampled the proposed road through an 8 m hill
at 11 locations for each road type, then retained the existing repeated-grading and raised-terrain rendering stress. The complete
fixture cleared 28 tree roots and 177 props, charged $40,527 and rebuilt 11,252 owned triangles. Its sixteen
captures are in `Captures/NewGame/PlacementFollowup`; reports are `Logs/placement-followup-review.{json,txt}`.

The traffic/bridge follow-up passed 130 selected cases; a final placement/ownership compile check passed
27. Across the three reports there are **251 unique passing cases and no failures**
(`Logs/placement-followup-validation-summary.json`). The final native captures use the preserved flat-airport
fast path and the shared service-road/public-Street construction-material implementation.


## Whole-network construction previews (2026-11-15)

`PreviewBuiltSurfaces` builds a separate render-only map from the candidate taxiway network or public-road
geometry plus the remaining committed airport surfaces. All preview triangles have foundations disabled.
The permanent `BuiltSurfaces` index, terrain samples/collider, vegetation removals and saved world remain
unchanged. The matching committed network renderers are temporarily hidden to avoid drawing their old
square endpoint through the future bend. `EndBuiltSurfacePreview` restores the permanent buffers and
renderers on cancellation, tool change and accepted construction, before committed geometry rebuilds.

`BuiltSurfaceMap.Project` returns the nearest actual triangle height and gradient at a point, with an
approximate elevation hint to distinguish stacked decks. Sparse CPU `Pavement.Project` supplies this query for public-road markings after building the
complete pavement so paint and its antialias expansion follow the rendered triangles, including graded
junction approaches. It creates no GPU buffers and no terrain formation data for this build-time query.


## Placement performance and unchanged surrounding land (2026-11-15)

Moving previews keep their native arrays, occupied spatial buckets, GPU buffers and material property block.
Only previously occupied cells are cleared; buffers grow by capacity and update in place. Cancel deactivates
the preview and restores committed bindings while retaining reusable storage until the valley is disposed.
The permanent ownership map remains immutable. Sparse CPU-only `Pavement` queries handle boundary and paint
projection without allocating a full region index or GPU resources.

`TaxiwayElevation` indexes links and junction polygons in 32 m cells, preserving insertion priority and exact
profiles. A query no longer scans distant junction outlines for every mesh vertex. Draft generation snapshots
the runway list once; empty stand lists do not repeat the profile query. Runway terrain checks likewise cache
nodes, spans and terminal outlines once per quote. Ghost runway mesh rebuilds leave `Airport.LayoutVersion`
unchanged; committed rebuilds still invalidate routing/support caches. Nearest-triangle grading samples run
as a Burst parallel job. Trees/props query a sparse footprint index rather than every pavement triangle.

Local construction supplies its known changed rectangle to ownership refresh, keeping nature/terrain work
near the affected approach instead of spanning every existing airport road. Automatic grading queues height,
water and nature updates without repainting surrounding land. It no longer writes exposed-soil disturbance.

`ValleySurface` ignores saved construction disturbance and assigns zero terrain-asphalt weight. Exact road
meshes are the only asphalt; no coarse blurred road layer extends beyond a curve/end. Existing saves lose
those artificial dirt/asphalt halos when their terrain layers rebuild. The natural biome/canopy mix remains,
including its normal shoreline and cliff materials. Manual brush disturbance remains available for nature
suppression, but does not stain the terrain palette.

Public Street previews partition the detached candidate graph into changed approaches/junctions and reusable
roads. Two graph-neighbour rings cover junction-reach dependencies. Both partitions use full topology to
compute the same cuts, curves, ends and signal layout; only the changing partition rebuilds on cursor moves.
The static part is invalidated when its source graph or membership changes, and cancel clears both views.
Their exact triangle footprints are combined for terrain clipping. Sparse projection context keeps markings
on the correct grade where a junction and its approach belong to different partitions. Cached edge height
profiles avoid repeated graph/node lookups, and final paint projection supplies gradients once.

The repeatable isolated-editor benchmark is
`PavementOwnershipReview.RunPerformanceFromCommandLine`; it uses a controlled hill with 17 service roads,
24 moving updates after warm-up and 100 stationary repeats. Reports are
`Logs/placement-performance-{before,after}.json` and `Logs/placement-performance-street-{before,after}.json`.
In this fixture, moving service-road CPU work fell from 144.34 ms to 16.15 ms, runway work from 16.33 ms
to 2.40 ms, and public Street work from its measured 128.97 ms baseline to 9.58 ms. These are construction-method CPU timings, not frame-rate
or GPU-rendering claims; allocation accounting is unavailable in this Unity runtime and is not reported.

Validation: the isolated native Play review (`Logs/placement-speed-review.json` and
`Captures/NewGame/PlacementSpeed/`) checks actual mouse placement, cancellation, quotes, vegetation clearance,
20 terrain changes, a forced +3 m terrain test and save/reload. Road ends/junctions retain natural surrounding
land instead of blurred asphalt or grading stains. `RoadFinishTests` also compares both preview partitions
against a complete rebuild, including graded junction markings and a split into an existing road, then moves
the draft endpoint and checks that the cached part stays unchanged.

Final regression run: `Logs/placement-speed-complete.xml` — 167 passed, 0 failed. The batch editor still
reports persistent-allocation shutdown warnings also seen in the earlier terrain test runs; the separate native
Play placement/save/reload review and final benchmark exit without that warning or runtime exceptions.


## Bridge and tunnel ownership (2026-11-15)

Airport ownership now comes from `TaxiwayNetwork.GroundOwnership`, separately from its multi-level render
mesh. `AirfieldRoadGeometry` excludes structured deck/bore owners, then adds the shared structure builder's
footings, abutments and open approaches. Covered tunnel roofs and high bridge decks never acquire a soil
formation. Preview uses its detached ownership list too. Stand grading protects ground foundations rather
than treating every elevated carriageway as land. The full airport's restored terrain provider is bound before
regenerating structures, so portal cover and support heights survive loading.

`ValleyView.RefreshLand(Rect)` refreshes only overlapping public structure chunks and changed airside structure
terrain signatures. Fingerprints avoid rebuild loops when an ownership refresh has not changed the ground.
The terrain palette still remains natural around roads; this adds no dirt/asphalt halo. See
[3D road construction](public-roads.md#shared-3d-construction-2026-11-15).
