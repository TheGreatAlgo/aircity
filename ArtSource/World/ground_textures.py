"""Preserve photo-scanned ground detail for the valley (CC0, Poly Haven / ambientCG).

python3 ArtSource/World/ground_textures.py
Sources are cached by the shared airfield downloader, never committed in Build/.
Unlike airfield pavement, natural ground keeps its scanned colour and local contrast.
"""
from pathlib import Path
import importlib.util
import json
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("airfield_textures", ROOT / "ArtSource/Airfield/textures.py")
shared = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shared)
OUT = ROOT / "Assets/AirCity/World/Textures"


def save(name, maps, tint=(1, 1, 1)):
    colour = shared.array(maps["albedo"], "RGB")
    # Keep measured fine colour; restrained exposure only, without flattening the photograph.
    colour = shared.to_srgb(shared.to_linear(colour) * np.array(tint))
    shared.save(colour, str(OUT / f"{name}_Albedo.png"), "RGB")
    shared.save(shared.array(maps["normal"], "RGB"), str(OUT / f"{name}_Normal.png"), "RGB")
    ao = shared.array(maps["ao"], "L") if "ao" in maps else np.ones(colour.shape[:2])
    rough = shared.array(maps["rough"], "L") if "rough" in maps else np.full(colour.shape[:2], .9)
    height = shared.array(maps["height"], "L") if "height" in maps else np.full(colour.shape[:2], .5)
    # Unity Terrain/Lit mask channels: metallic, ambient occlusion, height, smoothness.
    mask = np.stack((np.zeros_like(ao), ao, height, np.clip(1 - rough, 0, .18)), -1)
    shared.save(mask, str(OUT / f"{name}_Mask.png"), "RGBA")
    print(name)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    save("ForestFloor", shared.source_maps("polyhaven", "forest_leaves_02"), (.8, .8, .8))
    save("RockSurface", shared.source_maps("polyhaven", "rock_face_03"), (.84, .88, .94))
    grass = shared.source_maps("ambientcg", "Grass004")
    save("Meadow", grass, (.78, .82, .8))
    save("DryMeadow", grass, (.95, .84, .66))
    (OUT / "sources.json").write_text(json.dumps({
        "license": "CC0-1.0", "license_url": "https://polyhaven.com/license",
        "ForestFloor": "https://polyhaven.com/a/forest_leaves_02",
        "RockSurface": "https://polyhaven.com/a/rock_face_03",
        "RockSurface_authors": "Dario Barresi (photography), Rico Cilliers (processing)",
        "Meadow": "https://ambientcg.com/view?id=Grass004",
        "DryMeadow": "Grass004 with restrained dry-grass exposure tint",
        "generator": "ArtSource/World/ground_textures.py",
    }, indent=2) + "\n")


if __name__ == "__main__":
    main()
