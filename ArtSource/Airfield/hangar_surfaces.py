"""Generate one tileable, linear RGBA mask for the executive and airline hangars.

R is profiled metal siding, G is standing-seam roofing, B is poured concrete,
and A is the sectional lift door. Run with Python, NumPy, SciPy and Pillow.
"""
from pathlib import Path

import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter


SIZE = 512
OUT = Path(__file__).resolve().parents[2] / "Assets/AirCity/Airfield/Resources/HangarSurface.png"


def generate():
    rng = np.random.default_rng(12029)
    x, y = np.meshgrid((np.arange(SIZE) + 0.5) / SIZE, (np.arange(SIZE) + 0.5) / SIZE)
    source = rng.standard_normal((SIZE, SIZE))

    def noise(sy, sx):
        field = gaussian_filter(source, (sy, sx), mode="wrap")
        return np.clip(field / (3 * field.std()), -1, 1)

    def phase(at, period, offset=0):
        return (at * period + offset + 0.5) % 1 - 0.5

    def ridge(at, width):
        return np.exp(-(at / width) ** 2)

    grain = np.clip(source / 3, -1, 1)
    cloudy = noise(20, 18)
    broken = noise(2, 2)
    streak = noise(36, 1.1)

    # Broad profiled pans with distinct lit shoulders, gaskets and fastener heads.
    rib = phase(x, 24)
    side = 0.53 + 0.08 * cloudy + 0.12 * streak + 0.035 * grain
    side += 0.18 * ridge(rib + 0.16, 0.10) - 0.25 * ridge(rib - 0.14, 0.11)
    panel = phase(x, 6)
    side -= 0.14 * ridge(panel, 0.018)
    screw = ridge(phase(y, 6, 0.19), 0.014)
    side += screw * (0.24 * ridge(panel + 0.057, 0.009) - 0.22 * ridge(panel + 0.038, 0.016))
    side -= 0.12 * np.maximum(streak, 0) * ridge(panel + 0.046, 0.043)

    seam = phase(x, 14)
    lap = phase(y, 2)
    strip_tone = np.asarray([0.03, -0.05, 0.04, 0, -0.03, 0.05, 0.02, -0.07,
                             0.03, -0.02, 0.01, -0.04, 0.05, -0.01])
    roof = 0.56 + strip_tone[np.floor(x * 14).astype(int)] + 0.09 * cloudy + 0.09 * streak
    roof += 0.29 * ridge(seam + 0.033, 0.024) - 0.39 * ridge(seam - 0.027, 0.034)
    roof -= 0.10 * ridge(lap, 0.011) + 0.045 * ridge(lap - 0.024, 0.03)
    roof += 0.035 * grain

    # Four adjoining pours, hairline joints and exposed aggregate revealed by wear.
    concrete = 0.55 + 0.095 * cloudy + 0.085 * noise(5.5, 4) + 0.066 * broken + 0.055 * grain
    slabs = np.asarray([[0.025, -0.025], [-0.047, 0.028]])
    concrete += slabs[np.floor(y * 2).astype(int), np.floor(x * 2).astype(int)]
    for direction in (x, y):
        joint = phase(direction, 2)
        concrete -= (0.29 + 0.07 * broken) * ridge(joint, 0.004) + 0.058 * ridge(joint, 0.015)
        concrete += 0.10 * ridge(joint - 0.006, 0.003)
    concrete -= 0.11 * np.maximum(noise(17, 4), 0) ** 2

    fold = phase(y, 3)
    door = 0.52 + 0.10 * cloudy + 0.090 * streak + 0.03 * grain
    door += 0.24 * ridge(fold + 0.035, 0.031) - 0.26 * ridge(fold - 0.038, 0.045)
    door -= 0.16 * ridge(phase(x, 4, 0.11), 0.01)
    door -= 0.10 * np.maximum(streak, 0) * ridge(fold + 0.16, 0.15)
    return np.round(np.clip(np.stack((side, roof, concrete, door), axis=-1), 0, 1) * 255).astype(np.uint8)


if __name__ == "__main__":
    OUT.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(generate()).save(OUT, optimize=True)
