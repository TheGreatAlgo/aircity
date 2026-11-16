"""Bake the globe picker texture from NASA Blue Marble July 2004 (public NASA imagery)."""
from pathlib import Path
import hashlib
import json
import urllib.request
from PIL import Image
ROOT=Path(__file__).resolve().parents[2]
URL="https://assets.science.nasa.gov/content/dam/science/esd/eo/images/bmng/bmng-base/july/world.200407.3x5400x2700.jpg"
CACHE=ROOT/"ArtSource/World/Build/Globe"
OUT=ROOT/"Assets/AirCity/World/Resources/AirCityRegions"
def main():
    CACHE.mkdir(parents=True,exist_ok=True); OUT.mkdir(parents=True,exist_ok=True)
    original=CACHE/"world.200407.jpg"
    if not original.exists():
        with urllib.request.urlopen(URL,timeout=60) as response: original.write_bytes(response.read())
    Image.open(original).convert("RGB").resize((2048,1024),Image.Resampling.LANCZOS).save(OUT/"Earth.jpg",quality=92,optimize=True)
    (OUT/"EarthSource.json").write_text(json.dumps(dict(source="NASA Blue Marble: Next Generation, July 2004",url=URL,information="https://science.nasa.gov/earth/earth-observatory/blue-marble-next-generation/base-map/",credit="NASA Earth Observatory / Reto Stöckli",modification="Downsampled to 2048 × 1024 for the globe picker",sha256=hashlib.sha256((OUT/"Earth.jpg").read_bytes()).hexdigest()),indent=2)+"\n")
if __name__=="__main__": main()
