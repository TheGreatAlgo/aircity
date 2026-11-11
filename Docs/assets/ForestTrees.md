# Forest trees

`Assets/AirCity/World/Vegetation` contains three spruce and three pine variants. Each has a tapered irregular
24 m trunk, branch whorls and layered textured needle cards. The saved tree height, deterministic variant,
width and yaw create individual variation. Clearing consumes the same random stream as surviving trees,
so removing one patch never changes the surrounding forest.

`TreeMeshGenerator` builds the high and medium detail geometry; `TreeImpostorBaker` renders those models
into a transparent front/top atlas for the six-triangle distance representation. The bake uses an isolated
preview scene, unlit intrinsic colours and no wind. Change the generators rather than the mesh subassets
in `ForestTrees.asset`. The standalone model prefabs use Unity LODGroups. Materials remain hand-tunable.

`ForestRenderer` spatially groups instances into 800 m cells by variant and batches up to 500 nonuniform
transforms. It culls cell bounds per camera and uses full geometry below 250 m, simplified branches below
850 m, then crossed front cards plus a top card. Those thresholds are measured to batch bounds, so a whole
cell can retain more detail near the camera. Distant shadows are disabled. Wind, alpha-tested needle holes,
main-light shadowing, ambient light, mild transmission and per-tree tint live in `Vegetation.shader`.
There are no individual tree GameObjects or copied tree meshes in the valley. Construction rebuilds the
instance lists; clearing and road grading use saved tree positions and the displayed terrain heights.

The foliage source is an AI-generated transparent branchlet atlas, not a botanical scan. The original
1254 × 1254 RGBA PNG and exact generation prompt are preserved in [ArtSource/World](../../ArtSource/World/README.md).
Spruce/Pine are art categories here, rather than a claim of accurate species simulation.

Regenerate with **AirCity > World > Regenerate Trees and Public Road Assets**. This updates existing mesh
subassets without changing their IDs and rebakes distance foliage. `TreeLibrary.generatorVersion` is checked
by setup (bump the comparison/assigned version in setup when changing generation).
**AirCity > World > Create Tree and Road Review Scene** creates a separate scene containing the two species
and public-road kit. Batch entry `WorldAssetReview.FromCommandLine` writes close-up and airport forest captures
under ignored `Captures/WorldAssets`. `PublicRoadTests` checks the shared model/LOD/material contract.

Remaining visual work includes branch-normal/roughness textures, closer bark detail, wind-driven branch
motion, species/age distributions, slope/root treatment and smoother LOD transitions. The current assets
establish the semi-realistic direction and are intended to be iterated from close and airport-scale views.

Reference: [Unity RenderMeshInstanced API](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/graphics/rendermeshinstanced)
for the shared-instance rendering approach; [RHS Norway spruce](https://www.rhs.org.uk/plants/12899/picea-abies/details)
for the broad conifer silhouette direction.
