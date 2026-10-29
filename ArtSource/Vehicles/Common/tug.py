"""Shared parts for the towbarless aircraft tugs (TugSmall, TugLarge).

A towbarless tug is a low, wide, flat tractor shaped like a U from above: the two legs of the U at the back carry
the rear wheels, and the aircraft's nose wheel goes in between them. The tug backs up to the nose wheel, a cradle
(the platform, side clamps, a gate that closes behind the wheel and a pusher in front of it) grips it and lifts it
a little off the ground, and from then on tug and aircraft move as one: the tug steers the aircraft by turning its
nose wheel, pushing it back off its stand or towing it. The cab sits at a front corner, low enough to pass under the
aircraft's nose; the engine is under a low hood beside it.

Both sizes share this layout and differ in their numbers (Spec). The nose wheel sits in the cradle exactly between
the rear wheels, on the vehicle origin: gameplay puts the tug's rear axle on the aircraft's nose-gear point.

Space: (right, forward, up) metres, origin on the ground under the rear axle (the middle of the cradle).
"""
import math

from kit import M, Part, arc

# handler blue with a white cab roof and yellow-and-black hazard stripes on the ends of the legs and the bumper
MATS = {
    "Paint": ("#1F4E8E", 0.0, 0.4, False),
    "PaintDark": ("#183A66", 0.0, 0.45, False),
    "HazardYellow": ("#F0B70D", 0.0, 0.45, False),
    "HazardBlack": ("#151515", 0.0, 0.6, False),
    "Deck": ("#6A6D72", 0.7, 0.55, False),
    "Rubber": ("#1B1B1C", 0.0, 0.9, False),
}
GRIME = "#57534C"          # tyre dust and hydraulic oil


class Spec:
    """One tug's dimensions (metres):
      rear, front     the ends of the legs and the front bumper, along forward
      hw              half the width
      wb, track       wheelbase (front axle at forward = wb, rear axle at 0) and track
      R, W, rim       tyre radius and width, rim radius
      deck, clear     top of the deck plate, underside of the body
      open, uend      half the width of the opening between the legs, and where it ends (the U's closed end)
      rw              the largest nose wheel radius the cradle takes (where its gate and pusher sit)
      cab, hood       (x0, x1, f0, f1, top) boxes
    """

    def __init__(self, **kw):
        self.__dict__.update(kw)

    @property
    def wheels(self):
        t = self.track / 2
        return [
            ("Wheel_FL", (-t, self.wb, self.R), self.R, True),
            ("Wheel_FR", (t, self.wb, self.R), self.R, True),
            ("Wheel_RL", (-t, 0.0, self.R), self.R, False),
            ("Wheel_RR", (t, 0.0, self.R), self.R, False),
        ]

    @property
    def inner(self):
        """x of the wheels' inner faces (less a gap): the chassis and the legs' inner walls end here."""
        return self.track / 2 - self.W / 2 - 0.06


def build(S, lights, extra=None):
    body = Part("Body")
    sides(body, S)
    chassis(body, S)
    legs(body, S)
    front(body, S)
    cab(body, S)
    hood(body, S)
    if extra:
        extra(body)
    lamps(body, lights)
    parts = [body, cradle(S)]
    for name, c, r, _ in S.wheels:
        w = Part(name, pivot=c)
        w.wheel(c, r, S.W, S.rim, side=1 if c[0] > 0 else -1, rim="Rim", lugs=10)
        parts.append(w)
    return parts


# ============================================================== stripes

def _clip(poly, u0, u1, v0, v1):
    """Clip a convex 2D polygon to the rectangle u0..u1 x v0..v1 (Sutherland-Hodgman)."""
    def cut(pts, inside, at):
        out = []
        for i, p in enumerate(pts):
            q = pts[i - 1]
            if inside(p):
                if not inside(q):
                    out.append(at(q, p))
                out.append(p)
            elif inside(q):
                out.append(at(q, p))
        return out

    def lerp_u(u):
        return lambda a, b: (u, a[1] + (b[1] - a[1]) * (u - a[0]) / (b[0] - a[0]))

    def lerp_v(v):
        return lambda a, b: (a[0] + (b[0] - a[0]) * (v - a[1]) / (b[1] - a[1]), v)

    pts = poly
    for inside, at in ((lambda p: p[0] >= u0, lerp_u(u0)), (lambda p: p[0] <= u1, lerp_u(u1)),
                       (lambda p: p[1] >= v0, lerp_v(v0)), (lambda p: p[1] <= v1, lerp_v(v1))):
        pts = cut(pts, inside, at)
        if not pts:
            return []
    return pts


def hazard(b, origin, u, v, width, height, pitch=0.16, lean=1.0):
    """Yellow-and-black 45 degree stripes on a flat rectangle: origin + u * (0..width) + v * (0..height), drawn
    3 mm off the face (u x v should point out of it). lean flips the slant."""
    ox, oy, oz = origin
    n = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
    ln = math.sqrt(sum(c * c for c in n)) or 1.0
    off = [c / ln * 0.003 for c in n]

    def P(a, c):
        return (ox + u[0] * a + v[0] * c + off[0], oy + u[1] * a + v[1] * c + off[1], oz + u[2] * a + v[2] * c + off[2])

    b.quad([P(0, 0), P(width, 0), P(width, height), P(0, height)], "HazardYellow")
    k = -int(height / pitch) - 2
    while k * pitch < width + height:
        a0 = k * pitch
        band = [(a0, 0.0), (a0 + pitch * 0.5, 0.0), (a0 + pitch * 0.5 + height, height), (a0 + height, height)]
        if lean < 0:
            band = [(width - x, y) for x, y in band][::-1]
        pts = _clip(band, 0.0, width, 0.0, height)
        if len(pts) >= 3:
            b.quad([(lambda p: (p[0] + off[0], p[1] + off[1], p[2] + off[2]))(P(x, y)) for x, y in pts], "HazardBlack")
        k += 1


# ============================================================== body

def sides(b, S):
    """Side skins the full length, with an arch over each wheel and a dark liner inside it."""
    f0, f1 = S.rear + 0.22, S.front - 0.24
    z0, z1 = S.clear, S.deck - 0.1
    ar = S.R + 0.07
    prof = [(f0, z0)]
    for fc in (0.0, S.wb):
        prof += [(fc - ar, z0), (fc - ar, S.R)]
        prof += [(fc + ar * math.cos(math.radians(a)), S.R + ar * math.sin(math.radians(a))) for a in range(170, 9, -20)]
        prof += [(fc + ar, S.R), (fc + ar, z0)]
    prof += [(f1, z0), (f1, z1), (f0, z1)]
    for s in (-1, 1):
        b.prism(prof, "r", S.hw - 0.08 if s > 0 else -S.hw, 0.08, "Paint", bevel=0.012)
        # rubbing strip along the top of the side, an amber reflector between the arches
        lo, hi = sorted((s * S.hw, s * (S.hw + 0.03)))
        b.box((lo, f0 + 0.1, z1 - 0.2), (hi, f1 - 0.1, z1 - 0.12), "Rubber")
        x = s * (S.hw + 0.004)
        b.box((x - 0.006, S.wb * 0.5 - 0.06, z1 - 0.34), (x + 0.006, S.wb * 0.5 + 0.06, z1 - 0.28), "LensAmber")
        lo, hi = sorted((s * (S.hw - 0.01), s * (S.hw + 0.006)))
        for fa, fb in ((f0 + 0.05, -ar - 0.04), (ar + 0.04, S.wb - ar - 0.04), (S.wb + ar + 0.04, f1 - 0.05)):
            if fb - fa > 0.1:
                b.box((lo, fa, z0), (hi, fb, z0 + 0.14), "PaintDark")
        # dark liners inside the wheel arches
        for fc in (0.0, S.wb):
            inner = s * S.inner
            outer = s * (S.hw - 0.08)
            band = [(math.cos(math.radians(a)) * ar + fc, S.R + math.sin(math.radians(a)) * ar) for a in range(5, 176, 10)]
            band += [(math.cos(math.radians(a)) * (ar + 0.05) + fc, S.R + math.sin(math.radians(a)) * (ar + 0.05))
                     for a in range(175, 4, -10)]
            b.prism(band, "r", min(inner, outer), abs(outer - inner), "Chassis")


def chassis(b, S):
    # frame between the wheels and under the deck, the deck plate, the U's closed end
    b.box((-S.inner, S.uend, S.clear + 0.02), (S.inner, S.front - 0.3, S.deck - 0.1), "Chassis", bevel=0.03)
    b.box((-S.hw, S.uend - 0.02, S.deck - 0.1), (S.hw, S.front - 0.24, S.deck), "Paint", bevel=0.025)
    b.box((-S.open, S.uend - 0.06, S.clear), (S.open, S.uend + 0.3, S.deck - 0.1), "PaintDark", bevel=0.02)
    # axle housings
    for f in (0.0, S.wb):
        b.cyl((-S.inner, f, S.R), (S.inner, f, S.R), S.R * 0.32, "Chassis", segs=14)
    # chequer plate walkway along the front
    b.box((-S.hw + 0.12, S.uend + 0.1, S.deck), (S.hw - 0.12, S.uend + 0.42, S.deck + 0.012), "Deck")


def legs(b, S):
    for s in (-1, 1):
        lo, hi = sorted((s * S.open, s * S.hw))
        wall = sorted((s * S.open, s * S.inner))
        # inner wall facing the opening, the deck over the leg, the end block
        b.box((wall[0], S.rear + 0.2, S.clear - 0.04), (wall[1], S.uend, S.deck - 0.1), "PaintDark", bevel=0.025)
        b.box((lo, S.rear + 0.2, S.deck - 0.1), (hi, S.uend + 0.02, S.deck), "Paint", bevel=0.025)
        b.box((lo, S.rear, S.clear - 0.06), (hi, S.rear + 0.24, S.deck), "Paint", bevel=0.04)
        b.box((lo + 0.1, S.rear + 0.32, S.deck), (hi - 0.1, S.uend - 0.12, S.deck + 0.012), "Deck")
        # stripes on the end and on the mouth's inner face
        x0 = lo + 0.04
        hazard(b, (x0, S.rear - 0.001, S.clear), (1, 0, 0), (0, 0, 1), (hi - lo) - 0.08, S.deck - S.clear - 0.06,
               pitch=0.14 + 0.04 * S.hw, lean=s)
        xi = s * S.open
        L = min(1.1, S.uend - S.rear - 0.5)
        if s > 0:
            hazard(b, (xi - 0.001, S.rear + 0.25 + L, S.clear + 0.05), (0, -1, 0), (0, 0, 1), L, S.deck - S.clear - 0.3, 0.14)
        else:
            hazard(b, (xi + 0.001, S.rear + 0.25, S.clear + 0.05), (0, 1, 0), (0, 0, 1), L, S.deck - S.clear - 0.3, 0.14)
        # rubber guide rollers at the mouth
        b.cyl((xi - s * 0.07, S.rear + 0.18, S.clear + 0.02), (xi - s * 0.07, S.rear + 0.18, S.deck - 0.18), 0.07, "Rubber",
              segs=12)
        # tie-down eye on the end
        b.cyl((s * (S.hw - 0.25), S.rear - 0.04, S.deck - 0.45), (s * (S.hw - 0.25), S.rear - 0.04, S.deck - 0.3), 0.04,
              "Steel", segs=8)


def front(b, S):
    hw = S.hw
    # front panel (slightly raked) and the bumper below it with stripes and a tow coupling
    prof = [(S.front - 0.24, S.clear), (S.front - 0.1, S.clear), (S.front - 0.1, S.deck - 0.3), (S.front - 0.18, S.deck),
            (S.front - 0.24, S.deck)]
    b.prism(prof, "r", -hw, 2 * hw, "Paint", bevel=0.02)
    bz0, bz1 = S.clear + 0.06, S.clear + 0.06 + 0.26 + 0.06 * hw
    b.box((-hw + 0.04, S.front - 0.12, bz0), (hw - 0.04, S.front, bz1), "Rubber", bevel=0.02)
    hazard(b, (hw - 0.1, S.front + 0.001, bz0 + 0.03), (-1, 0, 0), (0, 0, 1), 2 * hw - 0.2, bz1 - bz0 - 0.06,
           pitch=0.16 + 0.04 * hw)
    b.box((-0.11, S.front - 0.05, bz0 + 0.04), (0.11, S.front + 0.08, bz1 - 0.04), "Steel", bevel=0.015)
    b.cyl((0.0, S.front + 0.03, bz0 + 0.02), (0.0, S.front + 0.03, bz1 + 0.03), 0.03, "Chrome", segs=10)
    # number plate
    b.box((0.25 * hw, S.front - 0.1, S.deck - 0.27), (0.25 * hw + 0.52, S.front - 0.085, S.deck - 0.15), "White")


def cab(b, S):
    x0, x1, f0, f1, top = S.cab
    z0 = S.deck
    band = z0 + 0.16
    roof = top - 0.07
    b.box((x0, f0, z0 - 0.02), (x1, f1, band), "Paint", bevel=0.03)
    b.box((x0 + 0.03, f0 + 0.03, band), (x1 - 0.03, f1 - 0.03, roof), "Glass")
    post = 0.06
    for x in (x0, x1 - post):
        for f in (f0, f1 - post):
            b.box((x, f, band), (x + post, f + post, roof), "Dark")
        mid = (f0 + f1) * 0.5
        b.box((x, mid - 0.03, band), (x + post, mid + 0.03, roof), "Dark")
    b.box((x0 - 0.05, f0 - 0.05, roof), (x1 + 0.05, f1 + 0.05, top), "White", bevel=0.03)
    # door frame on the outer side, a step and a grab handle below it
    xo = x0 if x0 < 0 else x1
    so = -1 if x0 < 0 else 1
    d0 = max(f0 + 0.25, S.wb + S.R + 0.12)
    d1 = min(d0 + 0.8, f1 - 0.1)
    for f in (d0, d1):
        b.box((xo + so * 0.002 - 0.012, f - 0.012, band), (xo + so * 0.002 + 0.012, f + 0.012, roof), "Dark")
    b.box((xo - 0.014, d0 + 0.1, band + 0.22), (xo + 0.014, d0 + 0.24, band + 0.26), "Chrome")
    b.box((xo + so * 0.02 - 0.1, d0, S.clear + 0.05), (xo + so * 0.02 + 0.1, d1, S.clear + 0.09), "Steel")
    # mirrors on arms at the front corners, a wiper on the front glass
    for x, sx in ((x0, -1), (x1, 1)):
        arm_end = (x + sx * 0.2, f1 - 0.1, roof - 0.1)
        b.cyl((x + sx * 0.02, f1 - 0.1, roof - 0.12), arm_end, 0.015, "Dark", segs=6)
        b.box((arm_end[0] - 0.05, arm_end[1] - 0.03, arm_end[2] - 0.2), (arm_end[0] + 0.05, arm_end[1] + 0.03, arm_end[2] + 0.02),
              "Dark", bevel=0.01)
    b.cyl(((x0 + x1) * 0.5 - 0.2, f1 - 0.025, band + 0.05), ((x0 + x1) * 0.5 + 0.15, f1 - 0.025, roof - 0.12), 0.01,
          "Dark", segs=6)


def hood(b, S):
    x0, x1, f0, f1, top = S.hood
    z0 = S.deck - 0.02
    slope = 0.5
    prof = [(f0, z0), (f1, z0), (f1, z0 + 0.35 * (top - z0)), (f1 - slope, top), (f0, top)]
    b.prism(prof, "r", x0, x1 - x0, "Paint", bevel=0.05, segments=3)
    # vents on top and on the outer side
    n = max(4, int((f1 - f0 - slope) / 0.5))
    for k in range(n):
        f = f0 + 0.35 + k * (f1 - f0 - slope - 0.6) / max(n - 1, 1)
        b.box((x0 + 0.18, f - 0.05, top - 0.004), (x1 - 0.18, f + 0.05, top + 0.006), "Dark")
    xo = x1 if abs(x1) > abs(x0) else x0
    so = 1 if xo > 0 else -1
    for k in range(6):
        f = f0 + 0.4 + k * 0.14
        b.box((xo + so * 0.002 - 0.006, f, S.deck + 0.06), (xo + so * 0.002 + 0.006, f + 0.07, top - 0.08), "Dark")
    # filler cap, lifting eyes
    b.cyl((x1 - 0.3, f0 + 0.3, top - 0.01), (x1 - 0.3, f0 + 0.3, top + 0.04), 0.07, "Dark", segs=12)
    for f in (f0 + 0.12, f1 - slope - 0.1):
        b.cyl(((x0 + x1) * 0.5 - 0.04, f, top - 0.01), ((x0 + x1) * 0.5 + 0.04, f, top + 0.06), 0.03, "Steel", segs=8)


def cradle(S):
    """The pick-up: a low platform the nose wheel rolls onto, clamps either side, a pusher in front and a gate behind,
    on lift arms from the U's closed end. A mover: Unity raises it by the type's lift while it holds a wheel."""
    c = Part("Cradle", pivot=(0.0, 0.0, 0.0))
    w = S.open - 0.05
    r = S.rw
    c.box((-w, -(r + 0.38), 0.02), (w, S.uend - 0.1, 0.09), "Steel", bevel=0.01)
    c.prism([(-(r + 0.62), 0.0), (-(r + 0.38), 0.0), (-(r + 0.38), 0.09), (-(r + 0.44), 0.09)], "r", -w, 2 * w, "Steel")
    # grip strips across the platform
    for k in range(5):
        f = -r * 0.8 + k * r * 0.4
        c.box((-w + 0.08, f - 0.02, 0.09), (w - 0.08, f + 0.02, 0.105), "Rubber")
    for s in (-1, 1):
        x = s * (S.open - 0.12)
        c.box((x - 0.07, -r * 0.55, 0.09), (x + 0.07, r * 0.55, 0.09 + r * 1.15), "PaintDark", bevel=0.02)
        c.box((x - s * 0.075 - 0.012, -r * 0.5, 0.14), (x - s * 0.075 + 0.012, r * 0.5, 0.06 + r * 1.15), "Rubber")
        # clamp cylinders back into the walls
        c.cyl((x, 0.0, 0.09 + r * 0.6), (s * (S.open + 0.05), 0.0, 0.09 + r * 0.6), 0.05, "Chrome", segs=10)
        # lift arms up into the closed end
        c.cyl((s * (w - 0.2), r + 0.3, 0.12), (s * (w - 0.2), S.uend + 0.05, S.deck - 0.25), 0.06, "Steel", segs=10)
    # pusher pad in front of the wheel, gate behind it
    c.box((-w + 0.12, r + 0.12, 0.09), (w - 0.12, r + 0.32, 0.09 + r * 0.9), "HazardYellow", bevel=0.02)
    c.box((-w + 0.16, r + 0.1, 0.12), (w - 0.16, r + 0.125, 0.06 + r * 0.85), "Rubber")
    c.box((-w + 0.04, -(r + 0.34), 0.16), (w - 0.04, -(r + 0.16), 0.16 + r * 0.55), "HazardYellow", bevel=0.02)
    c.box((-w + 0.08, -(r + 0.165), 0.18), (w - 0.08, -(r + 0.145), 0.14 + r * 0.5), "Rubber")
    c.cyl((-w + 0.05, -(r + 0.25), 0.16 + r * 0.55), (-w + 0.05, -(r + 0.25), 0.05), 0.045, "Steel", segs=10)
    return c


# ============================================================== lamps and grime

def lamps(b, lights):
    for name, kind, p, d, *_ in lights:
        r, f, z = p
        if kind == "Beacon":
            b.cyl((r, f, z - 0.15), (r, f, z - 0.07), 0.11, "Dark", segs=16)
            b.cyl((r, f, z - 0.07), (r, f, z + 0.09), 0.085, "LensAmber", segs=16, r2=0.072)
        elif kind == "Head":
            b.box((r - 0.13, f - 0.06, z - 0.07), (r + 0.13, f + 0.005, z + 0.07), "Dark", bevel=0.015)
            b.box((r - 0.1, f + 0.002, z - 0.05), (r + 0.1, f + 0.012, z + 0.05), "LensWhite")
        elif kind == "Tail":
            b.box((r - 0.09, f - 0.012, z - 0.05), (r + 0.09, f + 0.004, z + 0.05), "LensRed")
        elif kind == "Work":
            yaw = math.degrees(math.atan2(d[0], d[1]))
            pitch = math.degrees(math.atan2(d[2], math.hypot(d[0], d[1])))
            mtx = M(r, f, z, yaw=yaw, pitch=pitch)
            b.box((-0.07, -0.08, -0.06), (0.07, 0.02, 0.06), "Dark", bevel=0.012, matrix=mtx)
            b.box((-0.055, 0.02, -0.045), (0.055, 0.032, 0.045), "LensWhite", matrix=mtx)
            b.cyl((r, f, z - 0.06), (r, f, z - 0.16), 0.02, "Dark", segs=6)
        elif kind == "Marker":
            b.box((r - 0.006, f - 0.05, z - 0.03), (r + 0.006, f + 0.05, z + 0.03), "LensAmber")


def grime(pos, ao):
    """Dust thrown up by the tyres, darkest low down and in the cradle, oil round the pick-up."""
    import numpy as np
    z = pos[..., 2]
    low = np.clip(1.0 - (z - 0.1) / 0.8, 0, 1) ** 1.6
    crev = np.clip(1.0 - ao, 0, 1) ** 1.5
    pit = ((np.abs(pos[..., 0]) < 1.0) & (pos[..., 1] < 1.2) & (z < 0.5)).astype(np.float32) * 0.2
    return np.clip(0.45 * low + 0.25 * crev + pit, 0, 0.7).astype(np.float32)
