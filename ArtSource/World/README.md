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
| `RegionTreePalette.cs` | runtime valley trees: firs, spruces, pines, broadleaf crowns and palms as opaque crown volumes with bough/leaf-spray cards nearby, four levels; nine woodland, six mountain and seven tropical forms |
| `WorldAssetSetup.cs` | tree library, model prefabs, materials, road profiles and traffic signal prefab |
| `PublicRoadKitSetup.cs` | separate street/junction/highway/ramp mesh kit and source graph per prefab |
| `GroundCoverSetup.cs` | twenty boulder/stone/grass/sedge/fern/shrub/fallen-timber models with near/far geometry in `GroundCover.asset` |

Runtime `PublicRoadGeometry` generates career road meshes from the saved 3D public graph. These assets share
the airfield's low-level mesh/paint utilities and texture sources; they use their own profiles and materials.

`ground_textures.py` preserves natural scanned colour/detail for the landscape. Run
`python3 ArtSource/World/ground_textures.py` to regenerate `Assets/AirCity/World/Textures`.
It uses the shared airfield downloader/cache, keeps the original fine colour, and emits albedo/OpenGL-normal
plus a Unity Terrain mask (R metallic=0, G occlusion, B height, A matte smoothness capped at .18).
Sources: Poly Haven [Forest Leaves 02](https://polyhaven.com/a/forest_leaves_02) and
[Rock Face 03](https://polyhaven.com/a/rock_face_03) (Dario Barresi / Rico Cilliers), and ambientCG
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
explicit sampling gradients. `ValleyCliffs.hlsl` projects the fractured Rock Face 03 scan onto steep terrain
in world space across three axes, keeping fissures and normal detail unstretched on cliff walls. The normal
perturbation is projected back onto the geometric terrain tangent plane. Broad mineral/weather patches
break up the material at airport scale; smoothness remains matte and the same detail reaches depth normals.
`ValleyTerrainShaderSetup.Ensure()` generates the terrain shaders/includes from the installed URP source,
injecting the layer sampling and cliff extension into both forward/deferred lighting and depth-normal passes.
**Regenerate Terrain Shader** rebases after a URP update; generated source retains the adjacent Unity
Companion License. Runtime material clones bind the saved region's rock layer and biome mineral tint; source
materials and terrain-layer assets are preserved.

Use **AirCity > World > Regenerate Trees and Public Road Assets**, then **Create Public Road Model Kit**
for missing kit prefabs. The regeneration command updates existing tree mesh subassets in place and rebakes
`ForestImpostors.png`; it preserves material tuning. Forced kit rebuilds use `PublicRoadKitSetup.Create(true)`.
Never edit generated mesh buffers or the baked impostor atlas by hand. For a new foliage direction, regenerate
the source atlas, preserve its prompt, then copy it to the production texture before rebuilding the library.

`WorldAssetReview.FromCommandLine` creates a separate review scene, renders close-up trees, signal junctions,
an elevated crossing and the actual career airport, and completes a charter. Captures live in ignored
`Captures/WorldAssets`. See [forest trees](../../Docs/assets/ForestTrees.md) and
[public roads](../../Docs/systems/public-roads.md) for gameplay contracts and current limits.

`landcover_presets.py` extracts real 10 m land/water classifications from
[ESA WorldCover 2021 v200](https://esa-worldcover.org/en/data-access), using HTTP range reads of small
windows within the public cloud-optimized GeoTIFF tiles. The giant original tiles are never bundled.
Run it after `elevation_presets.py`; it reads the matching preset centre, source extent and resolution from
each generated elevation JSON. `Resources/AirCityRegions/*Cover.bytes` stores 513 × 513 original byte class
codes with south at the first row and east across each row, matching the elevation grids. Class 80 identifies
permanent water independently of elevation, preserving Bora Bora's lagoon and barrier islands and the
Snoqualmie Valley's narrow river channels and small inland lakes. The other cover classes can also guide forest, bare ground, grass, wetland and built-up areas.

ESA WorldCover data is **CC-BY-4.0**. `Resources/AirCityRegions/landcover-sources.json` retains dataset citation,
license, original tile URLs, transformations, checksums and this acknowledgement:
© ESA WorldCover project 2021 / Contains modified Copernicus Sentinel data (2021) processed by ESA WorldCover consortium.
The source citation is [Zanaga et al. (2022), ESA WorldCover 10 m 2021 v200](https://doi.org/10.5281/zenodo.7254221).

## Real landscape foundations

Run `python3 ArtSource/World/elevation_presets.py`, then `python3 ArtSource/World/landcover_presets.py` to
rebuild the offline 513 × 513 foundation assets under `Assets/AirCity/World/Resources/AirCityRegions`.
The first script caches public Terrarium PNG tiles in `Build/ElevationTiles`, decodes quarter-metre original
heights, and records every source tile/checksum. The second reads only small windows of public ESA WorldCover
2021 cloud-optimized GeoTIFFs, using the same geographic grid, and saves the original landcover classes.
Unity resource basenames are unique: elevation `.bytes`, `Cover.bytes`, and `Source.json` metadata.

The game adapts source extent and relief to its 6.4 km region; its saved source/transform fields document that
change. WorldCover class 80 defines permanent water; its masks correct DEM coastline artefacts and preserve
reef islands. The four bundled locations work offline. Globe selections can download these same data products at runtime,
then cache the complete crop for later offline previews. Geographic generation stays north up; internal seeds
only vary scenery and road planning.

Elevation: Mapzen/Tilezen Terrain Tiles, https://registry.opendata.aws/terrain-tiles/, accessed 2026-11-14.
3DEP, GMTED2010 and SRTM terrain data courtesy of the U.S. Geological Survey. Global ETOPO1 terrain data:
U.S. National Oceanic and Atmospheric Administration. Provider terms/attribution:
https://github.com/tilezen/joerd/blob/master/docs/attribution.md.

Landcover: © ESA WorldCover project 2021 / Contains modified Copernicus Sentinel data (2021) processed by
ESA WorldCover consortium. ESA WorldCover 10 m 2021 v200, Zanaga et al. (2022),
https://doi.org/10.5281/zenodo.7254221, CC-BY-4.0, https://esa-worldcover.org/en/data-access.
The generated masks are resampled/cropped adaptations. Each foundation ships exact coordinates, source
extent, download URLs and checksums in its metadata; `sources.json` and `landcover-sources.json` retain credits.

The Woodland foundation is an inland **Snoqualmie Valley, Washington** crop centred on 47.665°N,
121.928°W, spanning 8 km before the game's horizontal adaptation. It replaces the previous Hood Canal
foundation for newly generated regions. The ESA classification is 73.4% tree cover, 22.6% grass/cropland
and 2.3% permanent water; source relief is 9–195 m, with broad low-gradient land beside the real meandering
river. It has no ocean. Existing saved heightfields and recorded Hood Canal source names are retained.
Rebuild only this foundation with `python3 ArtSource/World/elevation_presets.py Woodland` followed by
`python3 ArtSource/World/landcover_presets.py Woodland`; other bundled foundation bytes stay unchanged.

## Globe picker imagery

`python3 ArtSource/World/globe_texture.py` bakes `Earth.jpg` from NASA Blue Marble: Next Generation,
July 2004. NASA Earth Observatory / Reto Stöckli; [source and downloads](https://science.nasa.gov/earth/earth-observatory/blue-marble-next-generation/base-map/).
The original is cached under `Build/Globe`; the shipped image is resized to 2048 × 1024. Exact source
URL, credit and output checksum are in `EarthSource.json`. `RegionGlobe` projects this texture on the GPU;
it is geographic source imagery, not a generated illustration.
