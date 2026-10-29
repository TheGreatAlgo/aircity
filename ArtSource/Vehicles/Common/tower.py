"""Shared parts of the control towers (TowerSmall, TowerMedium, TowerLarge): polygon outlines and their offsets,
slabs and bands between them, the glazed cab, windows, railings, stairs, antennas, lamps and grime.

Same space as kit: (right, forward, up) metres. Outlines are convex, counter-clockwise lists of (right, forward).
"""
import math

import numpy as np

from kit import M

LEAN = math.radians(15.0)   # cab glass leans out at the top, as on real towers (no reflections of the consoles)

# Colours every tower uses; a model adds its own walls and roofs in MATS.
MATS = {
    "CabGlass": ("#2B4651", 0.0, 0.06, False),
    "Frame": ("#30353A", 0.5, 0.45, False),
    "Fascia": ("#2C3136", 0.4, 0.45, False),
    "RoofDeck": ("#55585A", 0.0, 0.9, False),
    "Rail": ("#A3A8AC", 0.7, 0.45, False),
    "Galv": ("#8E9396", 0.7, 0.55, False),
    "Plant": ("#B4B7B8", 0.3, 0.5, False),
    "LensGreen": ("#22E060", 0.0, 0.1, True),
}
GRIME = "#57524A"


# ============================================================== outlines

def ngon(n, r, cx=0.0, cy=0.0):
    """Regular n-gon, circumradius r. For n a multiple of 4 its flat sides face the axes (one faces forward)."""
    return [(cx + r * math.cos(math.pi / n + 2 * math.pi * k / n), cy + r * math.sin(math.pi / n + 2 * math.pi * k / n))
            for k in range(n)]


def chamfer_rect(w, d, c, cx=0.0, cy=0.0):
    """Rectangle w (right) x d (forward) with its corners cut back by c: 8 points, lined up with ngon(8)."""
    hw, hd = w / 2, d / 2
    pts = [(hw, hd - c), (hw - c, hd), (-hw + c, hd), (-hw, hd - c), (-hw, -hd + c), (-hw + c, -hd), (hw - c, -hd),
           (hw, -hd + c)]
    return [(cx + x, cy + y) for x, y in pts]


def offset(outline, d):
    """The outline moved outward by d (inward if negative), every side parallel to the original (mitred)."""
    p = np.array(outline, float)
    out = []
    for i in range(len(p)):
        n1, n2 = _normal(p[i - 1], p[i]), _normal(p[i], p[(i + 1) % len(p)])
        out.append(tuple(p[i] + d * (n1 + n2) / (1.0 + float(n1 @ n2))))
    return out


def _normal(a, b):
    e = (b - a) / np.linalg.norm(b - a)
    return np.array([e[1], -e[0]])


def ring(outline, z):
    return [(x, y, z) for x, y in outline]


def stations(lo, hi, step):
    """Matching points along two outlines of the same shape (a cab's sill and glass head): every corner, plus
    evenly spaced points on sides longer than step. Yields (point on lo, point on hi, is corner)."""
    n = len(lo)
    for i in range(n):
        a0, a1 = np.array(lo[i]), np.array(lo[(i + 1) % n])
        b0, b1 = np.array(hi[i]), np.array(hi[(i + 1) % n])
        k = max(1, math.ceil(float(np.linalg.norm(a1 - a0)) / step - 1e-6))
        for j in range(k):
            t = j / k
            yield tuple(a0 + (a1 - a0) * t), tuple(b0 + (b1 - b0) * t), j == 0


# ============================================================== solids

def slab(b, outline, z0, z1, mat):
    """A vertical prism of the outline from z0 to z1."""
    return b.prism(list(outline), "z", z0, z1 - z0, mat)


def band(b, lo, z0, hi, z1, mat):
    """A closed solid from outline lo at z0 to outline hi at z1 (same point count): tapers, flares, leaning glass."""
    return b.loft([ring(lo, z0), ring(hi, z1)], mat)


def belt(b, x0, y0, x1, y1, z0, z1, mat, d=0.02):
    """A band d proud of the walls of the rectangular block (x0, y0)..(x1, y1), as four thin strips (a solid
    box would show its top through the roof)."""
    b.box((x0 - d, y1 - 0.05, z0), (x1 + d, y1 + d, z1), mat)
    b.box((x0 - d, y0 - d, z0), (x1 + d, y0 + 0.05, z1), mat)
    b.box((x0 - d, y0, z0), (x0 + 0.05, y1, z1), mat)
    b.box((x1 - 0.05, y0, z0), (x1 + d, y1, z1), mat)


def glazed_band(b, outline, z0, z1, step, glass="CabGlass", frame="Frame", inset=0.12, r=0.05):
    """A ribbon of glass set back `inset` from the outline, with mullions every `step` (and at every corner)."""
    inner = offset(outline, -inset)
    slab(b, inner, z0, z1, glass)
    face = offset(outline, -inset + r)
    for p, _, _ in stations(face, face, step):
        b.cyl((p[0], p[1], z0), (p[0], p[1], z1), r, frame, segs=4)


def cab(b, base, z_floor, z_glass, z_top, *, sill, over=0.4, roof_h=0.55, step=2.4, mullion=0.06, chequer=None):
    """The visual control room on a floor at z_floor: a solid sill wall up to z_glass, glass leaning out 15° up to
    z_top with mullions at the corners (and across panes wider than step), frame rails, and a roof that flares out
    `over` past the glass head. chequer = two materials alternating round the roof edge (small airfields paint it
    red and white). Returns the roof's outline and its top height."""
    lean = (z_top - z_glass) * math.tan(LEAN)
    top = offset(base, lean)
    slab(b, base, z_floor, z_glass, sill)
    band(b, offset(base, -0.04), z_glass, offset(top, -0.04), z_top, "CabGlass")
    slab(b, offset(base, 0.03), z_glass - 0.06, z_glass + 0.1, "Frame")
    slab(b, offset(top, 0.03), z_top - 0.12, z_top + 0.02, "Frame")
    for lo, hi, corner in stations(base, top, step):
        b.cyl((lo[0], lo[1], z_glass), (hi[0], hi[1], z_top), mullion * (1.4 if corner else 1.0), "Frame", segs=6)
    roof = offset(top, over)
    band(b, top, z_top, roof, z_top + 0.16, "Fascia")
    z_cap = z_top + roof_h
    if chequer:
        inner = offset(roof, -0.12)
        k = 0
        for (o0, i0), (o1, i1) in _pairs(list(stations(roof, inner, 0.85))):
            pts = [o0, o1, i1, i0]
            b.loft([ring(pts, z_top + 0.16), ring(pts, z_cap)], chequer[k % 2])
            k += 1
        slab(b, inner, z_top + 0.16, z_cap - 0.08, "RoofDeck")
    else:
        slab(b, roof, z_top + 0.16, z_cap, "Fascia")
        slab(b, offset(roof, -0.18), z_cap, z_cap + 0.04, "RoofDeck")
    return roof, z_cap


def _pairs(st):
    """Consecutive (outer, inner) station pairs round a closed outline."""
    pts = [(o, i) for o, i, _ in st]
    return [(pts[k], pts[(k + 1) % len(pts)]) for k in range(len(pts))]


def window(b, wall, u, z0, w, h, bars=0, frame="Frame", glass="CabGlass", sill="Rail"):
    """A window on an axis-aligned outer wall: a frame proud of the wall, the glass set back inside it, a sill.
    wall = (axis, c, side): axis "y" is a wall at y = c facing forward (side +1) or back (-1), and u is the
    window's centre x; axis "x" is a wall at x = c facing right (+1) or left (-1), and u is its centre y.
    bars = vertical glazing bars across it."""
    axis, c, s = wall

    def box(u0, u1, za, zb, d0, d1, mat):        # d: distance out from the wall face
        if axis == "y":
            b.box((u0, c + s * d0, za), (u1, c + s * d1, zb), mat)
        else:
            b.box((c + s * d0, u0, za), (c + s * d1, u1, zb), mat)

    f = 0.07
    u0, u1, z1 = u - w / 2, u + w / 2, z0 + h
    box(u0 + f, u1 - f, z0 + f, z1 - f, -0.02, 0.03, glass)
    box(u0, u1, z1 - f, z1, -0.02, 0.09, frame)
    box(u0, u1, z0, z0 + f, -0.02, 0.09, frame)
    box(u0, u0 + f, z0, z1, -0.02, 0.09, frame)
    box(u1 - f, u1, z0, z1, -0.02, 0.09, frame)
    for k in range(bars):
        x = u0 + w * (k + 1) / (bars + 1)
        box(x - 0.03, x + 0.03, z0, z1, -0.02, 0.07, frame)
    box(u0 - 0.05, u1 + 0.05, z0 - 0.05, z0, -0.02, 0.14, sill)


def railing(b, pts, z, h=1.1, step=1.6, closed=True, mat="Rail", r=0.025):
    """Posts and a top and mid rail along a polyline (closed: round an outline) at deck height z."""
    n = len(pts)
    sides = n if closed else n - 1
    for i in range(sides):
        a, c = np.array(pts[i], float), np.array(pts[(i + 1) % n], float)
        k = max(1, math.ceil(float(np.linalg.norm(c - a)) / step - 1e-6))
        for j in range(k + (1 if not closed and i == sides - 1 else 0)):
            p = a + (c - a) * j / k
            b.cyl((p[0], p[1], z), (p[0], p[1], z + h), r, mat, segs=6)
        b.cyl((a[0], a[1], z + h), (c[0], c[1], z + h), r * 1.3, mat, segs=6)
        b.cyl((a[0], a[1], z + h * 0.5), (c[0], c[1], z + h * 0.5), r * 0.8, mat, segs=6)


def flight(b, x0, x1, y, z, run, rise, back=False, mat="Galv"):
    """A straight steel stair between x0 and x1 (right), starting at (y, z) and climbing `rise` over `run` towards
    forward (or back): two stringers, open grating treads and a handrail on each side."""
    n = max(2, round(rise / 0.185))
    step_r, step_h = run / n, rise / n
    yaw = 180.0 if back else 0.0
    theta = math.atan2(rise, run)
    length = math.hypot(run, rise)
    w = x1 - x0
    xc = (x0 + x1) / 2
    along = M(xc, y, z, yaw=yaw, pitch=math.degrees(theta))      # frame along the slope (the nosing line)
    flat = M(xc, y, z, yaw=yaw)
    rail = 0.95                                                     # handrail height over the nosings
    # stringer: 0.04 over and 0.24 under the nosing line (measured square to it), cut flat at the floor
    t, a, c = math.tan(theta), 0.04 / math.cos(theta), 0.24 / math.cos(theta)
    end = run + 0.05
    stringer = [(-a / t, 0.0), (c / t, 0.0), (end, end * t - c), (end, end * t + a)]
    for s in (-1, 1):
        x = s * (w / 2 - 0.02)
        b.prism(stringer, "r", x - 0.02, 0.04, mat, matrix=flat)
        b.box((x - 0.025, 0.0, rail * math.cos(theta) - 0.05), (x + 0.025, length, rail * math.cos(theta)), mat,
              matrix=along)
        for t in (0.04, 0.5, 0.96):
            f, h = run * t, rise * t
            b.box((x - 0.02, f - 0.02, h - 0.1), (x + 0.02, f + 0.02, h + rail - 0.03), mat, matrix=flat)
    for k in range(1, n):            # treads centred on the pitch line; the top step is the landing
        f, h = k * step_r, k * step_h
        b.box((-w / 2 + 0.04, f - step_r / 2 - 0.02, h - 0.04), (w / 2 - 0.04, f + step_r / 2, h), mat, matrix=flat)


def antenna(b, x, y, z, h, r=0.03, mat="Rail"):
    """A whip or mast antenna on a short base."""
    b.cyl((x, y, z), (x, y, z + 0.22), r * 3.0, "Dark", segs=8)
    b.cyl((x, y, z + 0.22), (x, y, z + h), r, mat, segs=6, r2=r * 0.45)


def anemometer(b, x, y, z, h=2.2):
    """A wind mast: a pole, a cross arm, a cup anemometer at one end and a vane at the other."""
    b.cyl((x, y, z), (x, y, z + h), 0.04, "Rail", segs=8)
    b.cyl((x - 0.45, y, z + h), (x + 0.45, y, z + h), 0.025, "Rail", segs=6)
    b.cyl((x - 0.45, y, z + h), (x - 0.45, y, z + h + 0.18), 0.015, "Rail", segs=6)
    for k in range(3):
        a = 2 * math.pi * k / 3
        cx, cy = x - 0.45 + 0.13 * math.cos(a), y + 0.13 * math.sin(a)
        b.cyl((x - 0.45, y, z + h + 0.18), (cx, cy, z + h + 0.18), 0.008, "Rail", segs=4)
        b.sphere((cx, cy, z + h + 0.18), 0.04, "White", segs=8, rings=4)
    b.cyl((x + 0.45, y, z + h), (x + 0.45, y, z + h + 0.15), 0.015, "Rail", segs=6)
    b.box((x + 0.3, y - 0.01, z + h + 0.12), (x + 0.75, y + 0.01, z + h + 0.3), "Rail")


def lamp(b, kind, p, d):
    """The housing at a light point (the glow itself comes from LIGHTS). Obstruction lights (Marker) stand on a
    stub, or sit on a short arm out of a wall when their direction is horizontal; the aerodrome beacon is a drum
    with a white and a green lens on opposite sides; flood lights hang off an arm out of the wall."""
    x, y, z = p
    dx, dy, dz = d
    if kind == "Marker":
        if abs(dz) < 0.5:
            b.cyl((x - dx * 0.35, y - dy * 0.35, z - 0.12), (x, y, z - 0.12), 0.035, "Dark", segs=6)
        else:
            b.cyl((x, y, z - 0.32), (x, y, z - 0.1), 0.04, "Dark", segs=8)
        b.cyl((x, y, z - 0.14), (x, y, z - 0.08), 0.1, "Dark", segs=12)
        b.cyl((x, y, z - 0.08), (x, y, z + 0.1), 0.09, "LensRed", segs=12, r2=0.055)
    elif kind == "Beacon":
        b.cyl((x, y, z - 0.75), (x, y, z - 0.25), 0.12, "Dark", segs=10)
        b.cyl((x, y, z - 0.25), (x, y, z + 0.25), 0.32, "Dark", segs=16, bevel=0.03)
        b.cyl((x + 0.3, y, z), (x + 0.36, y, z), 0.2, "LensWhite", segs=16)
        b.cyl((x - 0.3, y, z), (x - 0.36, y, z), 0.2, "LensGreen", segs=16)
    elif kind == "Flood":
        b.box((x - 0.04, y - 0.5, z + 0.05), (x + 0.04, y, z + 0.12), "Dark")       # arm back to a wall at y - 0.45
        b.box((x - 0.3, y - 0.2, z - 0.03), (x + 0.3, y + 0.2, z + 0.1), "Dark", bevel=0.02)
        b.box((x - 0.26, y - 0.17, z - 0.045), (x + 0.26, y + 0.17, z - 0.03), "LensWhite")


def lamps(b, lights):
    seen = set()
    for name, kind, p, d, *_ in lights:
        key = (kind, tuple(round(v, 2) for v in p))
        if key in seen:
            continue                 # the beacon's white and green flashes share one drum
        seen.add(key)
        lamp(b, kind, p, d)


def grime(pos, ao):
    """Darker towards the ground and in crevices (sills, under ledges)."""
    z = pos[..., 2]
    low = np.clip(1.0 - z / 1.4, 0, 1) ** 2.0
    crev = np.clip(1.0 - ao, 0, 1) ** 1.6
    return np.clip(0.3 * low + 0.25 * crev, 0, 0.45).astype(np.float32)
