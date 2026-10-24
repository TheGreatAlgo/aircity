"""Texture maps over the Body atlas, computed in numpy from the baked position / normal / part maps.

A *scheme* is an RGB mask: R/G/B mark three paint zones that sit on top of a base colour. A *livery* is
a scheme + four colours (+ registration colour). Composition (mirrored exactly by AircraftLiveryCompositor.cs):

    col = base
    col = lerp(col, color1, mask.r)
    col = lerp(col, color2, mask.g)
    col = lerp(col, color3, mask.b)
    col = lerp(col, logo.rgb, logo.a)  # tail logo, where the LogoUV map covers the fin
    col *= detail                      # baked AO + panel lines

All colour maths happens on sRGB-encoded values, like the C# side.
"""
import numpy as np

from geom import pchip


class Px:
    """Per-texel geometry sampled from the bakes. s = station (metres aft of the nose)."""

    def __init__(self, maps, tags, px_per_m, model):
        pos = maps["pos"]
        self.x, self.y, self.z = pos[..., 0], pos[..., 1], pos[..., 2]
        self.s = self.y + model.S_REF
        n = maps["nrm"]
        self.nx, self.ny, self.nz = n[..., 0], n[..., 1], n[..., 2]
        self.valid = maps["valid"]
        self.tag = maps["tag"]
        self.names = [str(t) for t in tags]
        self.fus = self.group("fus", "hidden", "fairing")
        self.wing = self.group("wing")
        self.ht = self.group("htail", "elevator")
        self.vt = self.group("vtail", "rudder", "dorsal")
        self.aa = 0.75 / px_per_m          # half-width of the antialiasing ramp, metres
        self.px_per_m = px_per_m
        F = model.Fuselage
        sc = np.clip(self.s, F.S0, F.S1)
        self.zm = F.zm(sc)

    def group(self, *want):
        """Texels whose part tag is one of `want` or starts with "<want>_"."""
        ids = [i for i, t in enumerate(self.names) if any(t == w or t.startswith(w + "_") for w in want)]
        return np.isin(self.tag, ids) & self.valid

    def f(self, mask):
        return mask.astype(np.float32)

    # --- antialiased primitives (inputs are signed distances in metres)
    def inside(self, d):
        """1 where d < 0, smooth over one texel."""
        return np.clip(0.5 - d / (2 * self.aa), 0.0, 1.0)

    def band(self, d, lo, hi):
        return np.minimum(self.inside(lo - d), self.inside(d - hi))

    def line(self, d, half_width):
        return self.inside(np.abs(d) - half_width)


def curve_dist(s, z, knots):
    """Signed (perpendicular-ish) distance of (s, z) above a curve z = f(s)."""
    k = np.array(knots, float)
    f = pchip(k[:, 0], k[:, 1])
    eps = 1e-3
    slope = (f(s + eps) - f(s - eps)) / (2 * eps)
    return (z - f(s)) / np.sqrt(1 + slope**2)


def round_rect_sdf(px, py, cx, cy, hx, hy, r):
    qx = np.abs(px - cx) - (hx - r)
    qy = np.abs(py - cy) - (hy - r)
    outside = np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qy, 0) ** 2)
    return outside + np.minimum(np.maximum(qx, qy), 0) - r


def blur(a, radius_px):
    """Separable box blur, three passes (~Gaussian)."""
    a = np.asarray(a, np.float32)
    r = max(int(radius_px), 1)
    for _ in range(3):
        for axis in (0, 1):
            c = np.cumsum(np.pad(a, [(r + 1, r) if k == axis else (0, 0) for k in range(a.ndim)], mode="edge"),
                          axis=axis, dtype=np.float64)
            hi = np.take(c, np.arange(2 * r + 1, c.shape[axis]), axis=axis)
            lo = np.take(c, np.arange(0, c.shape[axis] - 2 * r - 1), axis=axis)
            a = ((hi - lo) / (2 * r + 1)).astype(np.float32)
    return a


def value_noise(x, y, z, scale, seed=0):
    """Smooth 3D value noise in 0..1 at world positions (continuous across UV seams)."""
    rng = np.random.default_rng(seed)
    lut = rng.random(4096).astype(np.float32)
    p = np.stack([x, y, z], -1) / scale
    i = np.floor(p).astype(np.int64)
    f = p - i
    f = f * f * (3 - 2 * f)

    def h(dx, dy, dz):
        k = (i[..., 0] + dx) * 73856093 ^ (i[..., 1] + dy) * 19349663 ^ (i[..., 2] + dz) * 83492791
        return lut[k & 4095]

    out = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[..., 0] if dx else 1 - f[..., 0]) * (f[..., 1] if dy else 1 - f[..., 1]) * (
                    f[..., 2] if dz else 1 - f[..., 2])
                out = out + w * h(dx, dy, dz)
    return out


# ======================================================================= detail (AO + panel lines)

def detail_map(maps, p: Px, lines, extra=None):
    ao = np.clip(maps["ao"], 0, 1)
    shade = 0.42 + 0.58 * ao**0.75
    shade *= 1.0 - 0.55 * lines
    if extra is not None:
        shade *= extra
    return np.where(p.valid, shade, 1.0).astype(np.float32)


def dirt_map(maps, p: Px, lines, extra=None):
    """Grime for URP Lit's detail albedo (_DETAIL_SCALED): 0.5 = clean, below darkens, above lightens.
    The shader applies albedo *= 1 + dirt * (2 * d - 1), dirt = the aircraft's 0..1 dirt amount."""
    d = np.full(p.x.shape, 0.5, np.float32)
    d -= 0.10 * np.clip(-p.nz - 0.2, 0, 1)                      # belly and lower surfaces collect grime
    d += 0.035 * np.clip(p.nz - 0.4, 0, 1)                      # sun-faded tops
    d -= 0.10 * np.clip(blur(lines, 0.04 * p.px_per_m) * 3.0, 0, 1)   # dirt settles in panel lines
    d -= 0.07 * np.clip((-p.ny - 0.75) * 4, 0, 1) * (p.wing | p.ht | p.vt)   # bugs on the leading edges
    blotch = value_noise(p.x, p.y, p.z, 0.9, seed=1)
    d -= 0.07 * np.clip(blotch - 0.45, 0, 1)
    streak = value_noise(p.x * 0.25, p.y * 6.0, p.z * 0.25, 0.4, seed=2)   # rain streaks run down the sides
    d -= 0.05 * np.clip(streak - 0.5, 0, 1) * (np.abs(p.nz) < 0.5)
    if extra is not None:
        d += extra
    return np.where(p.valid, np.clip(d, 0.15, 0.7), 0.5).astype(np.float32)


# ======================================================================= tail logo

def logo_uv_map(p: Px, box):
    """Where the tail logo goes: R, G = logo u, v (0..1); B = coverage. Both sides read left-to-right."""
    s0, s1, z0, z1 = box
    u = np.where(p.nx >= 0, (p.s - s0) / (s1 - s0), (s1 - p.s) / (s1 - s0))
    v = (p.z - z0) / (z1 - z0)
    cover = p.band(p.s, s0, s1) * p.band(p.z, z0, z1) * p.vt * (np.abs(p.nx) > 0.5)
    out = np.stack([np.clip(u, 0, 1), np.clip(v, 0, 1), cover], -1).astype(np.float32)
    return np.where(cover[..., None] > 0, out, 0.0)


# ======================================================================= composition

def hexrgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def compose(masks, colors, detail):
    """masks: (r, g, b) arrays; colors: (base, c1, c2, c3) sRGB tuples."""
    base, c1, c2, c3 = [np.array(c, np.float32) for c in colors]
    h, w = detail.shape
    col = np.empty((h, w, 3), np.float32)
    col[:] = base
    for m, c in zip(masks, (c1, c2, c3)):
        m = np.clip(m, 0, 1)[..., None].astype(np.float32)
        col = col * (1 - m) + c * m
    return col * detail[..., None]


def apply_logo(col, logo_uv, logo, box_aspect):
    """Blend an RGBA logo (H, W, 4, row 0 = bottom) over `col` where logo_uv covers, fitted (contain) into the box."""
    lh, lw = logo.shape[:2]
    la = lw / lh
    sx, sy = (1.0, box_aspect / la) if la >= box_aspect else (la / box_aspect, 1.0)
    u = (logo_uv[..., 0] - 0.5) / sx + 0.5
    v = (logo_uv[..., 1] - 0.5) / sy + 0.5
    inside = (u >= 0) & (u <= 1) & (v >= 0) & (v <= 1) & (logo_uv[..., 2] > 0)
    s = bilinear(logo, np.clip(u, 0, 1), np.clip(v, 0, 1))
    a = s[..., 3:4] * logo_uv[..., 2:3] * inside[..., None]
    return col * (1 - a) + s[..., :3] * a


# ======================================================================= registration text

def bilinear(img, u, v):
    """Sample img (H, W, C) at normalised (u, v) arrays; v=0 is the bottom row (Blender/Unity UV)."""
    h, w = img.shape[:2]
    x = np.clip(u * w - 0.5, 0, w - 1)
    y = np.clip(v * h - 0.5, 0, h - 1)
    x0 = np.floor(x).astype(int)
    y0 = np.floor(y).astype(int)
    x1 = np.minimum(x0 + 1, w - 1)
    y1 = np.minimum(y0 + 1, h - 1)
    fx = (x - x0)[..., None]
    fy = (y - y0)[..., None]
    a = img[y0, x0] * (1 - fx) + img[y0, x1] * fx
    b = img[y1, x0] * (1 - fx) + img[y1, x1] * fx
    return a * (1 - fy) + b * fy


def layout_text(text, font, box_w, box_h):
    """Returns ([(char, x, w)], scale) in 'box units' where letter cap height == box_h
    unless the string is too wide, in which case everything shrinks to fit."""
    cap = font["cap_top"] - font["baseline"]
    track = 0.16 * cap
    space = 0.42 * cap
    items = []
    pen = 0.0
    for ch in text.upper():
        g = font["glyphs"].get(ch)
        if g is None:
            pen += space
            continue
        ink = g["ink_right"] - g["ink_left"]
        items.append((ch, pen, ink))
        pen += ink + track
    width = max(pen - track, 1e-6)
    scale = min(box_h / cap, box_w / width)
    scale = np.floor(scale * 50.0) / 50.0     # same quantisation as the C# compositor
    return items, scale, width * scale


def draw_text(canvas, rect_px, text, color, font, atlas):
    """Draw `text` into canvas (H, W, 3) inside rect_px = (x0, y0, x1, y1) (pixel coords,
    y up), letters as tall as the rect, centred. atlas: (H, W) alpha, row 0 = bottom."""
    x0, y0, x1, y1 = rect_px
    items, scale, total_w = layout_text(text, font, x1 - x0, y1 - y0)
    cap = font["cap_top"] - font["baseline"]
    start = x0 + ((x1 - x0) - total_w) / 2
    base_y = y0 + ((y1 - y0) - cap * scale) / 2
    col = np.array(color, np.float32)
    ah, aw = atlas.shape
    cw, chh = font["cell_w"], font["cell_h"]
    for ch, pen, ink in items:
        g = font["glyphs"][ch]
        gx0 = start + pen * scale
        gx1 = gx0 + ink * scale
        gy0 = base_y + (0 - font["baseline"]) * scale
        gy1 = base_y + (chh - font["baseline"]) * scale
        ix0, ix1 = int(np.floor(gx0)) - 1, int(np.ceil(gx1)) + 1
        iy0, iy1 = int(np.floor(gy0)) - 1, int(np.ceil(gy1)) + 1
        ix0, iy0 = max(ix0, 0), max(iy0, 0)
        ix1, iy1 = min(ix1, canvas.shape[1]), min(iy1, canvas.shape[0])
        if ix1 <= ix0 or iy1 <= iy0:
            continue
        yy, xx = np.mgrid[iy0:iy1, ix0:ix1].astype(np.float32) + 0.5
        # supersample 3x3 for clean edges
        acc = np.zeros_like(xx)
        for oy in (-1 / 3, 0, 1 / 3):
            for ox in (-1 / 3, 0, 1 / 3):
                ax = g["cell_x"] + g["ink_left"] + (xx + ox - gx0) / scale
                ay = g["cell_y"] + (yy + oy - base_y) / scale + font["baseline"]
                inside = (ax >= g["cell_x"]) & (ax < g["cell_x"] + cw) & (ay >= g["cell_y"]) & (ay < g["cell_y"] + chh)
                a = bilinear(atlas[..., None], ax / aw, ay / ah)[..., 0]
                acc += np.where(inside, a, 0)
        acc /= 9.0
        region = canvas[iy0:iy1, ix0:ix1]
        canvas[iy0:iy1, ix0:ix1] = region * (1 - acc[..., None]) + col * acc[..., None]
    return canvas


def reg_text_rect(reg_side, sign, box):
    """Metric registration box -> main-atlas UV rect for one side (sign +1 port, -1 starboard)."""
    k, tu, tv = reg_side["k"], reg_side["tu"], reg_side["tv"]
    us = sorted([k * sign * box[0] + tu, k * sign * box[1] + tu])
    return us[0], k * box[2] + tv, us[1], k * box[3] + tv


def registration_texture(albedo_unshaded, detail, reg, text, color, font, atlas, box):
    """Build the per-aircraft registration texture: two stacked strips (port on top,
    starboard below), each a crop of the livery with the letters painted in, then shaded."""
    W = reg["tex_w"]
    hh = reg["half_h"]
    out = np.zeros((2 * hh, W, 3), np.float32)
    for side, row0, sign in (("port", hh, 1), ("stbd", 0, -1)):
        cu0, cv0, cu1, cv1 = reg[side]["crop"]
        yy, xx = np.mgrid[0:hh, 0:W].astype(np.float32) + 0.5
        u = cu0 + (cu1 - cu0) * xx / W
        v = cv0 + (cv1 - cv0) * yy / hh
        strip = bilinear(albedo_unshaded, u, v)
        tu0, tv0, tu1, tv1 = reg_text_rect(reg[side], sign, box)
        rect = ((tu0 - cu0) / (cu1 - cu0) * W, (tv0 - cv0) / (cv1 - cv0) * hh,
                (tu1 - cu0) / (cu1 - cu0) * W, (tv1 - cv0) / (cv1 - cv0) * hh)
        draw_text(strip, rect, text, color, font, atlas)
        shade = bilinear(detail[..., None], u, v)
        out[row0:row0 + hh] = strip * shade
    return out
