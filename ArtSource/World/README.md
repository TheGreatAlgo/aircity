# World asset sources

`Source/ConiferBranches.png` is the original transparent foliage atlas generated with the built-in ImageGen
tool on 2026-11-08, mode `photorealistic-natural`. It is 1254 × 1254 RGBA. The exact prompt is saved in
[`foliage-prompt.txt`](foliage-prompt.txt). The request was a new image: four separate spruce/pine branchlet
cutouts, neutral diffuse light, fine needle transparency and no background. The prompt requested 2048 square;
the tool returned the preserved dimensions above. No reference images were supplied. The production copy is
`Assets/AirCity/World/Vegetation/Textures/ConiferBranches.png`.

Native Unity mesh generators live in `Assets/AirCity/Scripts/Editor/World`:

| Source | Output |
|---|---|
| `TreeMeshGenerator.cs` | spruce/pine trunks, branches and foliage cards, six variants with distance levels |
| `TreeImpostorBaker.cs` | transparent front/top views of the generated models, used by the distant tree meshes |
| `WorldAssetSetup.cs` | tree library, model prefabs, materials, road profiles and traffic signal prefab |
| `PublicRoadKitSetup.cs` | separate street/junction/highway/ramp mesh kit and source graph per prefab |
| `GroundCoverSetup.cs` | twelve boulder/stone/grass/sedge/fern/shrub models with near/far geometry in `GroundCover.asset` |

Runtime `PublicRoadGeometry` generates career road meshes from the saved 3D public graph. These assets share
the airfield's low-level mesh/paint utilities and texture sources; they use their own profiles and materials.

`ground_textures.py` preserves natural scanned colour/detail for the landscape. Run
`python3 ArtSource/World/ground_textures.py` to regenerate `Assets/AirCity/World/Textures`.
It uses the shared airfield downloader/cache, keeps the original fine colour, and emits albedo/OpenGL-normal
plus a Unity Terrain mask (R metallic=0, G occlusion, B height, A matte smoothness capped at .18).
Sources: Poly Haven [Forest Leaves 02](https://polyhaven.com/a/forest_leaves_02) and
[Rock 01](https://polyhaven.com/a/rock_01), and ambientCG
[Grass004](https://ambientcg.com/view?id=Grass004), all CC0. Provider source/license links are also saved in
`Textures/sources.json`. Dirt and road underlay keep the existing airfield source textures.

`ValleySurfaceSetup` assigns the scanned meadow/dry/forest/rock layers. `WorldTexturePostprocessor` imports
them with proper colour space, normal maps, mipmaps and high anisotropy. Runtime `ValleySurface` blends them
using Burst jobs, small warped patches, canopy, slope/height, river banks and saved manual earthworks.
`ArtSource/UI/icons.py` generates the Clear trees and Level ground toolbar icons.

Use **AirCity > World > Regenerate Rocks and Understorey** to rebuild native meshes in place.
`GroundCover.shader` provides scanned triplanar rock detail, vertex occlusion, matte lighting, plant wind,
shadow casting/receiving and complementary distance-level dithering. Models are instanced by
`GroundCoverRenderer`, with habitat/clearance generated in Burst by `GroundCoverScatter`.

`ValleyTerrainSampling.hlsl` breaks repeating ground tiles with three smoothly blended random offsets and
explicit sampling gradients. `ValleyTerrainShaderSetup.Ensure()` generates the terrain shaders/includes from
the installed URP source, changing only layer sampling and include paths. **Regenerate Terrain Shader**
rebases after a URP update; generated source retains the adjacent Unity Companion License.

Use **AirCity > World > Regenerate Trees and Public Road Assets**, then **Create Public Road Model Kit**
for missing kit prefabs. The regeneration command updates existing tree mesh subassets in place and rebakes
`ForestImpostors.png`; it preserves material tuning. Forced kit rebuilds use `PublicRoadKitSetup.Create(true)`.
Never edit generated mesh buffers or the baked impostor atlas by hand. For a new foliage direction, regenerate
the source atlas, preserve its prompt, then copy it to the production texture before rebuilding the library.

`WorldAssetReview.FromCommandLine` creates a separate review scene, renders close-up trees, signal junctions,
an elevated crossing and the actual career airport, and completes a charter. Captures live in ignored
`Captures/WorldAssets`. See [forest trees](../../Docs/assets/ForestTrees.md) and
[public roads](../../Docs/systems/public-roads.md) for gameplay contracts and current limits.
