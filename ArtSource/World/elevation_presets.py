"""Bake four real landscape foundations from the public Mapzen Terrain Tiles archive.

python3 ArtSource/World/elevation_presets.py [Woodland Mountains Tropical Desert]
Downloads are cached under Build/; player builds use the bundled height samples offline.
Heights are original metres above EGM96. Runtime records and applies game-scale transforms.
"""
from concurrent.futures import ThreadPoolExecutor
from io import BytesIO
from pathlib import Path
import hashlib
import json
import math
import struct
import sys
import urllib.request

import numpy as np
from PIL import Image
from scipy.ndimage import map_coordinates

ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / "ArtSource/World/Build/ElevationTiles"
OUT = ROOT / "Assets/AirCity/World/Resources/AirCityRegions"
ZOOM, RESOLUTION = 13, 513
PRESETS = [
    ("Woodland", "Snoqualmie Valley, Washington", 47.665, -121.928, 8000, False),
    ("Mountains", "Yosemite Valley, California", 37.735, -119.597, 8000, False),
    ("Tropical", "Bora Bora volcanic lagoon, French Polynesia", -16.503, -151.744, 12800, True),
    ("Desert", "Moab canyon country, Utah", 38.57, -109.55, 8000, False),
]


def pixel(lon, lat):
    scale = 256 * 2**ZOOM
    latitude = np.radians(lat)
    return (lon + 180) / 360 * scale, (1 - np.arcsinh(np.tan(latitude)) / math.pi) * .5 * scale


def tile(key):
    x, y = key
    path = CACHE / f"{ZOOM}-{x}-{y}.png"
    url = f"https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{ZOOM}/{x}/{y}.png"
    if not path.exists():
        with urllib.request.urlopen(url, timeout=40) as response:
            data = response.read()
        image = Image.open(BytesIO(data)).convert("RGB")
        if image.size != (256, 256):
            raise ValueError(f"Unexpected terrain tile dimensions: {url}")
        path.write_bytes(data)
    rgb = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32)
    return key, rgb[..., 0] * 256 + rgb[..., 1] + rgb[..., 2] / 256 - 32768, url


def bake(preset):
    name, location, lat, lon, size, ocean = preset
    # Local east/north grid, with north at the final row to match Unity's heightmap.
    east, north = np.meshgrid(np.linspace(-size / 2, size / 2, RESOLUTION),
                              np.linspace(-size / 2, size / 2, RESOLUTION))
    longitude = lon + east / (111320 * math.cos(math.radians(lat)))
    latitude = lat + north / 111320
    px, py = pixel(longitude, latitude)
    x0, x1 = math.floor(px.min() / 256), math.floor(px.max() / 256)
    y0, y1 = math.floor(py.min() / 256), math.floor(py.max() / 256)
    mosaic = np.empty(((y1 - y0 + 1) * 256, (x1 - x0 + 1) * 256), dtype=np.float32)
    keys = [(x, y) for x in range(x0, x1 + 1) for y in range(y0, y1 + 1)]
    urls = []
    with ThreadPoolExecutor(max_workers=6) as pool:
        for (x, y), heights, url in pool.map(tile, keys):
            mosaic[(y-y0)*256:(y-y0+1)*256, (x-x0)*256:(x-x0+1)*256] = heights
            urls.append(url)
    samples = map_coordinates(mosaic, [py - y0 * 256 - .5, px - x0 * 256 - .5], order=1, mode="nearest")
    if not np.isfinite(samples).all() or samples.min() < -12000 or samples.max() > 9000:
        raise ValueError(f"Invalid elevation data for {name}")
    # Quarter-metre signed samples are precise enough for DEM source detail and only 514 KiB/map.
    quantized = np.rint(np.clip(samples, -8192, 8191.75) * 4).astype("<i2")
    payload = struct.pack("<4sI", b"ACDE", RESOLUTION) + quantized.tobytes(order="C")
    (OUT / f"{name}.bytes").write_bytes(payload)
    metadata = dict(name=name, location=location, latitude=lat, longitude=lon, sourceExtentMetres=size,
                    resolution=RESOLUTION, hasOcean=ocean, minElevation=float(samples.min()),
                    maxElevation=float(samples.max()), seaFraction=float(np.mean(samples <= .5)),
                    sha256=hashlib.sha256(payload).hexdigest(), tiles=sorted(urls))
    (OUT / f"{name}Source.json").write_text(json.dumps(metadata, indent=2) + "\n")
    print(name, location, "height", round(samples.min()), "to", round(samples.max()),
          "sea", round(metadata["seaFraction"] * 100, 1), "%", flush=True)
    return metadata


def main():
    CACHE.mkdir(parents=True, exist_ok=True)
    OUT.mkdir(parents=True, exist_ok=True)
    selected = set(sys.argv[1:]) or {preset[0] for preset in PRESETS}
    unknown = selected - {preset[0] for preset in PRESETS}
    if unknown:
        raise ValueError(f"Unknown region presets: {sorted(unknown)}")
    for preset in PRESETS:
        if preset[0] in selected:
            bake(preset)
    # Subset rebuilds preserve the other already baked foundations and their exact source metadata.
    maps = [json.loads((OUT / f"{preset[0]}Source.json").read_text()) for preset in PRESETS
            if (OUT / f"{preset[0]}Source.json").exists()]
    (OUT / "sources.json").write_text(json.dumps(dict(
        source="Mapzen Terrain Tiles, accessed 2026-11-14", source_url="https://registry.opendata.aws/terrain-tiles/",
        attribution_url="https://github.com/tilezen/joerd/blob/master/docs/attribution.md",
        format_url="https://github.com/tilezen/joerd/blob/master/docs/formats.md",
        attribution=["3DEP, GMTED2010 and SRTM terrain data courtesy of the U.S. Geological Survey.",
                     "Global ETOPO1 terrain data: U.S. National Oceanic and Atmospheric Administration.", "Mapzen / Tilezen"],
        generator="ArtSource/World/elevation_presets.py", maps=maps), indent=2) + "\n")


if __name__ == "__main__":
    main()
