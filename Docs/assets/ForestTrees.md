# Forest trees

The base library in `Assets/AirCity/World/Vegetation` contains three spruce and three pine variants, with
tapered trunks, branch whorls and textured needle cards. `RegionTreePalette` combines those with three
broadleaf forms in Woodland: rounded alder, slender birch and a spreading woodland crown. Tropical uses
seven forms: tall and leaning coconut palms, a short fan palm, an umbrella crown, a tall rainforest crown,
a low coastal broadleaf and a low pandanus crown. Coasts favour palms, while inland tropical stands favour
broad canopy trees; woodland patches vary the broadleaf/conifer mix. These names describe art families,
not surveyed species distributions. The saved tree height, deterministic variant,
width and yaw create individual variation. Clearing consumes the same random stream as surviving trees,
so removing one patch never changes the surrounding forest.

`ForestPopulation` fills ESA WorldCover tree/mangrove habitat with irregular 8 m candidates in temperate
regions and 6 m candidates in tropical regions.
Broad stand variation, smaller gaps and a minority of younger trees keep the canopy uneven; mapped fields,
towns and water stay open. Steep slopes thin out, and roads and settlement gardens reserve crown clearance.
The scatter runs in Burst. The shared saved-tree safety limit is 800,000 rather than the old 120,000 cap.
Desert scrub remains sparse and uses two palm forms. All biome palettes use six-triangle distance trees.

Saved `forestVersion = 2` records the density pass separately from terrain generation. Opening an older
mapped region appends trees once using its saved habitat/heightfield. Original indices, positions, heights,
removal flags and terrain remain intact. Removed roots reserve 32 m, disturbance reserves two source cells,
and the game protects airport bounds and public-road corridors. Procedural generator 1–3 saves retain their
original population. The larger snapshots use the existing save format with a bounded 128 MiB read limit.
Version 2 adds denser tropical stands on forested volcanic slopes and low reef islands; a version 1
temperate forest only updates its version marker and keeps its population. Tropical slope thinning accounts
for the source crop's horizontal/vertical adaptation, with a separate hard cliff cutoff. Roots remain above
the wash zone and mapped water stays empty. Fully forested tropical downloads that exceed the tree budget
sample candidates uniformly across the entire region rather than leaving the last rows empty.
`ForestQuery` keeps a weak, disposable 100 m spatial index for construction footprints, brush previews and
ray picking; removals are read live and appended/replaced lists invalidate the index.

`TreeMeshGenerator` builds the high and medium detail geometry; `TreeImpostorBaker` renders those models
into a transparent front/top atlas for the six-triangle distance representation. The bake uses an isolated
preview scene, unlit intrinsic colours and no wind. Change the generators rather than the mesh subassets
in `ForestTrees.asset`. The standalone model prefabs use Unity LODGroups. Materials remain hand-tunable.
At runtime the conifers use their former medium geometry even at close range. New branched crowns use
overlapping foliage-cluster cards with perforated leaflet masks, costing about 350 triangles close and
210 at medium distance; palms use 116/80 and pandanus 256/176. Analytic front/top
silhouettes are baked once per palette by a Burst job and compressed by `Bc1TextureBuilder`, without a
camera, per-tree textures or downloaded art. Woodland adds one 256 × 512 BC1 atlas; Tropical uses 256 × 1024.
Each variant can override its shared bark/foliage/distant materials so conifers and broadleaf trees batch
together without changing or disposing the authored source assets. `TreeCanopy.shader` supplies leaf-scale
colour/normal variation in stable local coordinates, subtle instance tint, wind and shared sunlight/shadows.

`ForestRenderer` uses a two-level spatial hierarchy: 200 m cells near the camera and densely packed 800 m
cells for distant trees, grouped by variant with up to 500 nonuniform transforms per submission. Full geometry
ends at 90 m, simplified branches at 360 m, then crossed front cards plus a top card cost six triangles per
tree. A nearby tree no longer keeps an entire 800 m cell in detailed geometry. Bounds contain the transformed
mesh at every level plus wind padding; shadows stop at 180 m. Wind, alpha-tested needle holes,
main-light shadowing, ambient light, mild transmission and per-tree tint live in `Vegetation.shader`.
There are no individual tree GameObjects or copied tree meshes in the valley. The distant transform/index
buffers cost an extra 68 bytes per surviving tree (34 MB for 500,000 trees); they keep overview submission
counts small while fine cells cull expensive branches close to the camera. Clearing compacts only the affected
fine buffers and their parent buffers, preserving all other arrays and surviving transforms. Local grading
reseats both detail levels using displayed terrain heights. A changed world or appended population rebuilds
the hierarchy. `LastSubmittedTrees`, the three detail counts, `LastSubmittedTriangles`, `LastDrawCalls` and
`LastRemovalCheckedTrees` expose camera and clearing work for the visual/performance review tools.

The conifer foliage source is an AI-generated transparent branchlet atlas, not a botanical scan. The original
1254 × 1254 RGBA PNG and exact generation prompt are preserved in [ArtSource/World](../../ArtSource/World/README.md).
Spruce/Pine are art categories here, rather than a claim of accurate species simulation.
Broadleaf cluster masks, palm fronds and mixed-tree distance atlases are generated by the runtime palette;
they do not add another downloaded or AI-generated texture.

Regenerate with **AirCity > World > Regenerate Trees and Public Road Assets**. This updates existing mesh
subassets without changing their IDs and rebakes distance foliage. `TreeLibrary.generatorVersion` is checked
by setup (bump the comparison/assigned version in setup when changing generation).
**AirCity > World > Create Tree and Road Review Scene** creates a separate scene containing the two species
and public-road kit. Batch entry `WorldAssetReview.FromCommandLine` writes close-up and airport forest captures
under ignored `Captures/WorldAssets`. `PublicRoadTests` checks the shared model/LOD/material contract.

`ForestDensityReview.RunFromCommandLine` captures each curated biome at fixed cameras and measures submitted
geometry and real brush work. The 2026-11-14 isolated Metal review matched all 13 camera pairs: Woodland
120,000 → 322,785 trees, Mountains 37,925 → 129,835, Tropical 18,561 → 32,922. Woodland's submitted triangles
fell 55% at the medium camera and 42% close up despite the denser forest. A 60 m clearing brush removed
102 trees in 1.09 ms plus 5.11 ms for the local visual refresh (17.78 ms initial index creation measured
separately). These are editor CPU/submission measurements, not frame-rate promises. The initial full
forest buffer build cost 469 ms and distant buffers consume additional memory; clearing uses the local path.
Matched previews and measurements live under ignored `Captures/NewGame/ForestDensity/{before,after}` and
`Logs/forest-density-{before,after}.json`. `ForestPopulationTests`, `ForestQueryTests`, `ForestRendererTests`
and the dense save regression protect placement, saved clearing, selection and both instance levels.
The combined isolated Game/World EditMode run passed 176/176 tests (104.83 s, no failures or skips;
`Logs/forest-tests.xml`).

The subsequent biome/depth review increased Bora Bora from 32,922 to 104,725 trees, including a grove on
the small 33-source-cell reef island that previously held only two trees. Woodland retains its 322,785
saved roots while gaining the mixed palette. Shared ground scenery now includes clustered outcrops,
stone aprons, shrubs, larger ferns and fallen timber; the common near tree meshes and distance thresholds
are deliberately cheaper to leave room for those landscape layers. The isolated Game/World regression
suite passed 187/187 tests in 103.17 s with no failures or skips (`Logs/biome-tests.xml`), including tropical
version 1 migration, dry low-island roots, a uniform bounded forest budget, all palette geometry/material
contracts, full prop clearances, local clearing/terrain updates and save/load. Matched review images live
under `Captures/NewGame/ForestDensity/biome-{before,after}`.
The final Metal review passed 19 matched camera pairs plus two ground-feature showcases. At the normal
Woodland camera, trees plus visible props submitted 452,358 triangles, compared with 4,171,126 triangles
for trees alone before this pass; Bora submitted 309,960 combined versus 424,964 for the previous trees
alone. At the close Woodland camera the combined total was 5,595,514 versus 11,743,992 previous tree-only
triangles. Earlier ground geometry was not instrumented, so those baselines are conservative lower bounds,
not exact combined comparisons or frame-rate measurements. See `Logs/biome-visual-review.txt`.

Remaining visual work includes branch-normal/roughness textures, closer bark detail, wind-driven branch
motion, species/age distributions, slope/root treatment and smoother LOD transitions. The current assets
establish the semi-realistic direction and are intended to be iterated from close and airport-scale views.

Reference: [Unity RenderMeshInstanced API](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/graphics/rendermeshinstanced)
for the shared-instance rendering approach; [RHS Norway spruce](https://www.rhs.org.uk/plants/12899/picea-abies/details)
for the broad conifer silhouette direction.
