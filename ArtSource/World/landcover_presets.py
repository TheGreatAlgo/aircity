"""Bake compact real land/water classifications aligned to the region elevation presets.

python3 ArtSource/World/landcover_presets.py [Woodland Mountains Tropical Desert]
Reads clipped windows from ESA WorldCover 2021 v200 public cloud-optimized GeoTIFFs.
The original giant tiles are never downloaded or redistributed. Output contains the original
class codes, one byte per height sample, south row first / east across, with no image header.
ESA WorldCover 2021 is CC-BY-4.0; see the bundled landcover-sources.json acknowledgement.
"""
from pathlib import Path
import hashlib
import json
import math
import sys

import numpy as np
import rasterio
from rasterio.windows import Window

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/AirCity/World/Resources/AirCityRegions"
CACHE = ROOT / "ArtSource/World/Build/LandCoverWindows"
BASE = "https://esa-worldcover.s3.eu-central-1.amazonaws.com/v200/2021/map/"
CLASSES = {0: "No data", 10: "Tree cover", 20: "Shrubland", 30: "Grassland", 40: "Cropland",
           50: "Built-up", 60: "Bare / sparse vegetation", 70: "Snow and ice", 80: "Permanent water",
           90: "Herbaceous wetland", 95: "Mangroves", 100: "Moss and lichen"}


def tile_name(west, south):
    return f"{'N' if south >= 0 else 'S'}{abs(south):02d}{'E' if west >= 0 else 'W'}{abs(west):03d}"


def bake(name):
    metadata = json.loads((OUT / f"{name}Source.json").read_text())
    lat, lon, size, resolution = (metadata[key] for key in
                                 ("latitude", "longitude", "sourceExtentMetres", "resolution"))
    east, north = np.meshgrid(np.linspace(-size / 2, size / 2, resolution),
                              np.linspace(-size / 2, size / 2, resolution))
    longitude = lon + east / (111320 * math.cos(math.radians(lat)))
    latitude = lat + north / 111320
    tile_west = np.floor(longitude / 3).astype(int) * 3
    tile_south = np.floor(latitude / 3).astype(int) * 3
    cover = np.zeros((resolution, resolution), dtype=np.uint8)
    urls = []
    for west in np.unique(tile_west):
        for south in np.unique(tile_south):
            selected = (tile_west == west) & (tile_south == south)
            if not selected.any():
                continue
            tile = tile_name(west, south)
            url = BASE + f"ESA_WorldCover_10m_2021_v200_{tile}_Map.tif"
            urls.append(url)
            cache = CACHE / f"{name}-{tile}.npz"
            if cache.exists():
                stored = np.load(cache)
                if np.array_equal(stored["longitude"], longitude[selected]) and np.array_equal(stored["latitude"], latitude[selected]):
                    cover[selected] = stored["values"]
                    continue
            # This GDAL setting only prevents remote directory listing; COG reads remain byte-range requests.
            with rasterio.Env(GDAL_DISABLE_READDIR_ON_OPEN="EMPTY_DIR"):
                with rasterio.open(url) as source:
                    rows, cols = rasterio.transform.rowcol(source.transform, longitude[selected], latitude[selected], op=np.floor)
                    rows, cols = np.asarray(rows, dtype=int), np.asarray(cols, dtype=int)
                    row0, col0 = rows.min(), cols.min()
                    window = Window(int(col0), int(row0), int(cols.max() - col0 + 1), int(rows.max() - row0 + 1))
                    values = source.read(1, window=window)[rows - row0, cols - col0]
            cover[selected] = values
            np.savez_compressed(cache, longitude=longitude[selected], latitude=latitude[selected], values=values)
    unexpected = set(np.unique(cover)) - set(CLASSES)
    if unexpected:
        raise ValueError(f"Unknown WorldCover classes: {unexpected}")
    payload = cover.tobytes(order="C")
    (OUT / f"{name}Cover.bytes").write_bytes(payload)
    values, counts = np.unique(cover, return_counts=True)
    summary = dict(name=name, latitude=lat, longitude=lon, sourceExtentMetres=size, resolution=resolution,
                   format="raw uint8 class code, south row first; no header", sha256=hashlib.sha256(payload).hexdigest(),
                   waterFraction=float(np.mean(cover == 80)),
                   classes={str(value): dict(name=CLASSES[value], fraction=float(count / cover.size)) for value, count in zip(values, counts)},
                   tiles=urls)
    (OUT / f"{name}CoverSource.json").write_text(json.dumps(summary, indent=2) + "\n")
    print(name, "water", round(summary["waterFraction"] * 100, 1), "%", "classes", list(summary["classes"]), flush=True)
    return summary


def main():
    CACHE.mkdir(parents=True, exist_ok=True)
    selected = sys.argv[1:] or ["Woodland", "Mountains", "Tropical", "Desert"]
    for name in selected:
        bake(name)
    maps = [json.loads(path.read_text()) for path in sorted(OUT.glob("*CoverSource.json"))]
    (OUT / "landcover-sources.json").write_text(json.dumps(dict(
        source="ESA WorldCover 10 m 2021 v200, accessed 2026-11-14",
        source_url="https://esa-worldcover.org/en/data-access",
        doi="https://doi.org/10.5281/zenodo.7254221", license="CC-BY-4.0",
        license_url="https://creativecommons.org/licenses/by/4.0/",
        attribution="© ESA WorldCover project 2021 / Contains modified Copernicus Sentinel data (2021) processed by ESA WorldCover consortium",
        modification="Nearest-neighbour samples extracted from geographic source windows to AirCity's 513 × 513 local east/north grids; source class values retained.",
        citation="Zanaga, D., et al. (2022). ESA WorldCover 10 m 2021 v200.",
        generator="ArtSource/World/landcover_presets.py", classes=CLASSES, maps=maps), indent=2) + "\n")


if __name__ == "__main__":
    main()
