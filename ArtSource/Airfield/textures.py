"""Ground textures for runways, taxiways and aprons, the macro-variation noise and the runway-designation glyph
atlas. Plain Python 3 (numpy, Pillow, scipy), no Blender. See ArtSource/README.md.

    python3 ArtSource/Airfield/textures.py

The photo textures are CC0 (Poly Haven, ambientCG; see SOURCES). They are downloaded into Airfield/Build/ on the
first run, then flattened (large blotches removed so they tile over kilometres), colour-matched and packed into
Assets/AirCity/Airfield/Textures:

    <Set>_Albedo.png   sRGB colour
    <Set>_Normal.png   tangent-space normal (OpenGL / Unity convention)
    <Set>_Mask.png     R = roughness, G = ambient occlusion, B = height (linear)
    Macro.png          tileable noise (linear): R large blobs, G medium, B fine, A grain
    RunwayGlyphs.png   signed distance field of the runway designation characters (linear, one channel)
"""
import io
import json
import os
import sys
import urllib.request
import zipfile

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "AirCity", "Airfield", "Textures")
CACHE = os.path.join(HERE, "Build", "downloads")
SIZE = 1024
AGENT = {"User-Agent": "AirCity-ArtSource/1.0"}

# set: (provider, asset id, mean albedo (sRGB) to match, share of the source's colour to keep)
SOURCES = {
    "Asphalt": ("polyhaven", "clean_asphalt", (0.25, 0.25, 0.26), 0.3),
    "AsphaltWorn": ("polyhaven", "asphalt_02", (0.36, 0.36, 0.36), 0.3),
    "Concrete": ("polyhaven", "concrete_floor_03", (0.64, 0.63, 0.60), 0.25),
    "ConcreteWorn": ("polyhaven", "cracked_concrete_02", (0.52, 0.51, 0.48), 0.35),
    "Dirt": ("polyhaven", "dirt", (0.47, 0.40, 0.31), 0.85),
    "DirtWorn": ("polyhaven", "brown_mud_02", (0.38, 0.31, 0.23), 1.0),
    "Grass": ("ambientcg", "Grass004", (0.30, 0.38, 0.15), 1.0),
}


# ============================================================== downloads

def fetch(url):
    req = urllib.request.Request(url, headers=AGENT)
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read()


def cached(name, url):
    path = os.path.join(CACHE, name)
    if not os.path.exists(path):
        os.makedirs(CACHE, exist_ok=True)
        print("  download", url)
        data = fetch(url)
        with open(path, "wb") as fh:
            fh.write(data)
    with open(path, "rb") as fh:
        return fh.read()


def source_maps(provider, asset):
    """{'albedo', 'normal', 'rough', 'ao', 'height'} -> PIL images (ao may be missing)."""
    if provider == "polyhaven":
        files = json.loads(cached(f"{asset}.json", f"https://api.polyhaven.com/files/{asset}"))
        keys = {"albedo": "Diffuse", "normal": "nor_gl", "rough": "Rough", "ao": "AO", "height": "Displacement"}
        out = {}
        for k, key in keys.items():
            if key not in files:
                continue
            entry = files[key]["1k"]
            fmt = "jpg" if "jpg" in entry else "png"
            out[k] = Image.open(io.BytesIO(cached(f"{asset}_{key}.{fmt}", entry[fmt]["url"])))
        return out
    data = cached(f"{asset}_1K-JPG.zip", f"https://ambientcg.com/get?file={asset}_1K-JPG.zip")
    z = zipfile.ZipFile(io.BytesIO(data))
    suffix = {"albedo": "_Color.jpg", "normal": "_NormalGL.jpg", "rough": "_Roughness.jpg",
              "ao": "_AmbientOcclusion.jpg", "height": "_Displacement.jpg"}
    out = {}
    for k, s in suffix.items():
        names = [n for n in z.namelist() if n.endswith(s)]
        if names:
            out[k] = Image.open(io.BytesIO(z.read(names[0])))
    return out


# ============================================================== colour

def to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def array(img, mode):
    img = img.convert(mode)
    if img.size != (SIZE, SIZE):
        img = img.resize((SIZE, SIZE), Image.LANCZOS)
    return np.asarray(img, dtype=np.float64) / 255.0


def save(arr, path, mode):
    arr = np.clip(np.round(arr * 255.0), 0, 255).astype(np.uint8)
    Image.fromarray(arr, mode).save(path, optimize=True)


def albedo(img, target_srgb, keep_chroma):
    """Flatten the big light / colour blotches (they repeat visibly over a runway), keep the grain, then match
    the set's mean colour."""
    lin = to_linear(array(img, "RGB"))
    low = np.stack([ndimage.gaussian_filter(lin[..., c], SIZE / 12, mode="wrap") for c in range(3)], -1)
    lin = lin / np.maximum(low, 1e-4) * low.mean(axis=(0, 1))
    lum = lin @ np.array([0.2126, 0.7152, 0.0722])
    lin = lum[..., None] + (lin - lum[..., None]) * keep_chroma
    target = to_linear(np.array(target_srgb))
    lin *= target / np.maximum(lin.mean(axis=(0, 1)), 1e-4)
    return to_srgb(lin)


def flatten_gray(arr, strength=1.0):
    low = ndimage.gaussian_filter(arr, SIZE / 12, mode="wrap")
    return arr - (low - low.mean()) * strength


def build_set(name, provider, asset, target, keep):
    maps = source_maps(provider, asset)
    save(albedo(maps["albedo"], target, keep), os.path.join(OUT, f"{name}_Albedo.png"), "RGB")
    save(array(maps["normal"], "RGB"), os.path.join(OUT, f"{name}_Normal.png"), "RGB")
    rough = flatten_gray(array(maps["rough"], "L")) if "rough" in maps else np.full((SIZE, SIZE), 0.8)
    ao = array(maps["ao"], "L") if "ao" in maps else np.ones((SIZE, SIZE))
    height = array(maps["height"], "L") if "height" in maps else np.full((SIZE, SIZE), 0.5)
    height = flatten_gray(height)
    height = (height - height.min()) / max(height.max() - height.min(), 1e-6)
    mask = np.stack([rough, ao, height, np.ones_like(rough)], -1)
    save(mask, os.path.join(OUT, f"{name}_Mask.png"), "RGBA")


# ============================================================== macro noise

def tileable_noise(n, beta, seed, low_cut=1.0):
    """Gaussian noise with a 1/f^beta spectrum, tileable (built in frequency space), equalised to 0..1."""
    rng = np.random.default_rng(seed)
    spectrum = np.fft.fft2(rng.standard_normal((n, n)))
    fx = np.fft.fftfreq(n)[:, None] * n
    fy = np.fft.fftfreq(n)[None, :] * n
    f = np.hypot(fx, fy)
    amp = np.where(f >= low_cut, np.maximum(f, 1e-6) ** (-beta / 2), 0.0)
    field = np.real(np.fft.ifft2(spectrum * amp))
    ranks = field.ravel().argsort().argsort().astype(np.float64) / (n * n - 1)
    return ranks.reshape(n, n)


def build_macro():
    n = 512
    r = tileable_noise(n, 3.2, 11, low_cut=2)    # large blobs: tint, wear patches
    g = tileable_noise(n, 2.4, 12, low_cut=3)    # medium: second sample blend, puddles
    b = tileable_noise(n, 1.6, 13, low_cut=4)    # fine: streaks (sampled stretched), snow edges
    a = tileable_noise(n, 0.6, 14, low_cut=8)    # grain: ragged dirt edges, paint wear
    save(np.stack([r, g, b, a], -1), os.path.join(OUT, "Macro.png"), "RGBA")


# ============================================================== runway glyphs

# Runway designation characters, drawn as strokes on a 3 x 9 m box (x right, y up = away from the threshold).
# Keep CHARS, CELL, PX_PER_M and the atlas size in sync with PaintBuilder.cs (GlyphChars, CellW / CellH, GlyphPxPerM).
CHARS = "0123456789LCR"
CELL = (128, 256)            # pixels per cell; 8 x 2 cells in a 1024 x 512 atlas
PX_PER_M = 24.0
BOX = (3.0, 9.0)
STROKE = 0.8                 # metres (ICAO: about a quarter of the 3 m digit width)
SPREAD = 0.66                # metres of distance encoded each side of the edge (0.5 = edge)
SS = 4                       # supersampling for the distance transform


def _arc(cx, cy, r, a0, a1, ry=None, n=24):
    ry = r if ry is None else ry
    t = np.radians(np.linspace(a0, a1, n))
    return [(cx + r * np.cos(a), cy + ry * np.sin(a)) for a in t]


def glyph_strokes(ch):
    """Stroke centrelines (lists of points, metres) of a character."""
    xl, xr, xc, yb, yt = 0.31, 2.69, 1.5, 0.31, 8.69
    r = xc - xl
    if ch == "0":
        return [_arc(xc, yt - r, r, 0, 180) + _arc(xc, yb + r, r, 180, 360) + [(xr, yt - r)]]
    if ch == "1":
        return [[(1.7, yb), (1.7, yt), (0.55, yt - 1.25)]]
    if ch == "2":
        return [_arc(xc, yt - r, r, 165, -10) + [(xr - 0.15, 5.9), (xl, 1.2), (xl, yb), (xr, yb)]]
    if ch == "3":
        return [[(xl, yt), (xr, yt), (1.05, 5.25), (xc, 5.25)] + _arc(xc, 5.25 - r, r, 90, 0)
                + [(xr, yb + r)] + _arc(xc, yb + r, r, 0, -180)[1:] + [(xl, yb + r + 0.35)]]
    if ch == "4":
        return [[(2.05, yb), (2.05, yt), (xl, 3.05), (xr, 3.05)]]
    if ch == "5":
        return [[(xr, yt), (xl, yt), (xl, 5.25), (xc, 5.25)] + _arc(xc, 5.25 - r, r, 90, 0)
                + [(xr, yb + r)] + _arc(xc, yb + r, r, 0, -180)[1:] + [(xl, yb + r + 0.35)]]
    if ch == "6":
        return [_arc(xc, yt - r, r, 25, 180) + [(xl, yb + r)] + _arc(xc, yb + r, r, 180, 360)
                + [(xr, 4.0 - r)] + _arc(xc, 4.0 - r, r, 0, 180)]
    if ch == "7":
        return [[(xl, yt), (xr, yt), (0.95, yb)]]
    if ch == "8":
        rb, rt = r, r * 0.9
        bottom = _arc(xc, 4.6 - rb, rb, 0, 180) + [(xl, yb + rb)] + _arc(xc, yb + rb, rb, 180, 360) + [(xr, 4.6 - rb)]
        top = _arc(xc, yt - rt, rt, 0, 180) + [(xc - rt, 4.6 + rt)] + _arc(xc, 4.6 + rt, rt, 180, 360) + [(xc + rt, yt - rt)]
        return [bottom, top]
    if ch == "9":
        return [_arc(xc, yb + r, r, 205, 360) + [(xr, yt - r)] + _arc(xc, yt - r, r, 0, 180)
                + [(xl, 5.0 + r)] + _arc(xc, 5.0 + r, r, 180, 360)]
    if ch == "L":
        return [[(xl, yt), (xl, yb), (xr, yb)]]
    if ch == "C":
        return [_arc(xc, yt - r, r, 20, 180) + [(xl, yb + r)] + _arc(xc, yb + r, r, 180, 340)]
    if ch == "R":
        bowl = [(xl, yt), (1.05, yt)] + _arc(1.05, 6.95, xr - 1.05, 90, -90, ry=yt - 6.95) + [(xl, 5.2)]
        return [[(xl, yb), (xl, yt)], bowl, [(1.25, 5.2), (xr, yb)]]
    raise ValueError(ch)


def glyph_sdf(ch):
    cw, chh = CELL
    W, H = cw * SS, chh * SS
    k = PX_PER_M * SS
    ox = (W - BOX[0] * k) / 2
    oy = (H - BOX[1] * k) / 2
    img = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(img)
    width = int(round(STROKE * k))
    for stroke in glyph_strokes(ch):
        pts = [(ox + x * k, H - (oy + y * k)) for x, y in stroke]
        d.line(pts, fill=255, width=width, joint="curve")
    inside = np.asarray(img) > 127
    dist = ndimage.distance_transform_edt(inside) - ndimage.distance_transform_edt(~inside)
    sdf = 0.5 + dist / (SPREAD * k) * 0.5
    sdf = sdf.reshape(chh, SS, cw, SS).mean(axis=(1, 3))     # back to cell resolution
    return np.clip(sdf, 0, 1)


def build_glyphs():
    cw, chh = CELL
    atlas = np.zeros((chh * 2, cw * 8))
    for i, ch in enumerate(CHARS):
        col, row = i % 8, i // 8
        # row 0 = the atlas's top (v near 1 in Unity)
        atlas[row * chh:(row + 1) * chh, col * cw:(col + 1) * cw] = glyph_sdf(ch)
    save(atlas, os.path.join(OUT, "RunwayGlyphs.png"), "L")


# ============================================================== main

def main():
    os.makedirs(OUT, exist_ok=True)
    only = sys.argv[1:]
    for name, (provider, asset, target, keep) in SOURCES.items():
        if only and name not in only:
            continue
        print(name, "<-", provider, asset)
        build_set(name, provider, asset, target, keep)
    if not only or "Macro" in only:
        build_macro()
    if not only or "Glyphs" in only:
        build_glyphs()
    print("wrote", OUT)


if __name__ == "__main__":
    main()
