# Geographic terrain downloads

The four curated landscapes are bundled and work offline. For an arbitrary globe location,
`LocationTerrainLoader.LoadAsync(latitude, longitude, extent, cancellationToken, progress)` downloads an
8 km crop by default. It returns detached `RegionTerrainData`; the caller keeps the current preview until
the complete download and new-world generation succeed. Cancellation and provider errors leave that world
unchanged. Regions already saved retain their heightfield and classification and never need the provider.

The returned 513 × 513 grid has south in its first row and east across each row. Latitude/longitude refer to
its centre; `extent` is the real source footprint in metres. Elevations are metres above sea level, without
seeded rotation, reflection or generated elevation variation. World generation can then apply its documented
height-range adaptation and shoreline/road conditioning. Longitude wraps across the dateline. The crop must
fit between 60°S and 83°N; unsupported polar and unavailable source areas return a clear coverage error.

Elevation comes from the public [Mapzen/Tilezen Terrain Tiles](https://registry.opendata.aws/terrain-tiles/)
Terrarium PNGs. RGB values decode as `R × 256 + G + B / 256 − 32768`, then bilinear sampling aligns the crop.
Zoom adapts to latitude/output spacing, capped at 13. Unity's image loader uses a linear texture and samples
the original colour bytes, preserving elevation values. Missing/non-finite heights are rejected.

Land cover comes from [ESA WorldCover 10 m 2021 v200](https://esa-worldcover.org/en/data-access), using small
HTTP byte ranges from its public cloud-optimized GeoTIFFs. `WorldCoverTiff` reads the original single uint8
classification band, its georeference and tile directory. It decodes the source's lossless DEFLATE blocks,
checks their Adler checksum, and retains nearest-neighbour class codes. It does not use a rendered RGB map
or a colour-derived water mask. Only the blocks intersecting the crop are downloaded. Class 80 is permanent
water. Marine generation starts from boundary-connected low water and broad water interiors, then
extends through a bounded coastal/mouth band. This corrects known elevated DEM artefacts over lagoons
without flattening every upstream river. Tiny low coastal remnants receive a limited cleanup; elevated
disconnected lakes and higher narrow river reaches remain freshwater. This is adapted game hydrology:
a broad uphill waterbody connected to the sea, or a tiny low lake close to shore, can be classified as
marine. An authoritative global ocean/inland mask would remove that remaining ambiguity.

Up to four small requests run concurrently. Requests have a timeout and one retry for connection/server
failures. `RegionTerrainProgress` exposes `fraction` and `message`; cancellation aborts pending requests.
No credentials or API key are required by these public sources.

Completed crops are cached under `Application.persistentDataPath/TerrainCache/v1`. Keys include the provider
version, resolution, centre coordinates (rounded to 0.0000001°) and extent, using invariant formatting.
Cache records store the exact grids, coordinates, source version and SHA-256 of both sample arrays. A cache
load checks all metadata and hashes; damaged/obsolete records are downloaded again. Temporary files do not
become cache entries. `HasCached` is a fast existence hint; `LoadAsync` performs full validation. Downloaded
source crops are cached separately from airport saves, so editing an airport cannot overwrite the source.

Elevation credits: Mapzen/Tilezen Terrain Tiles, accessed 2026-11-14. 3DEP, GMTED2010 and SRTM terrain data
courtesy of the U.S. Geological Survey; global ETOPO1 terrain data, U.S. National Oceanic and Atmospheric
Administration. [Provider attribution and terms](https://github.com/tilezen/joerd/blob/master/docs/attribution.md).

Land-cover credits: © ESA WorldCover project 2021 / Contains modified Copernicus Sentinel data (2021)
processed by ESA WorldCover consortium. Zanaga, D., et al. (2022),
[ESA WorldCover 10 m 2021 v200](https://doi.org/10.5281/zenodo.7254221),
[CC-BY-4.0](https://creativecommons.org/licenses/by/4.0/). Crops are resampled adaptations. Bundled preset
source metadata retains source URLs and checksums. The small original TIFF header/block test fixtures have
their own `WorldCoverSources.json` attribution and checksums.

`LocationTerrainLoaderTests` verifies a real source TIFF block losslessly, corruption rejection, RGB height
precision/orientation, geographic/dateline sampling, coverage errors, marine/inland distinction and verified
offline cache roundtrips. An explicit Unity integration test downloads Snoqualmie Valley and then loads the
same arrays from cache; normal EditMode runs stay offline.
