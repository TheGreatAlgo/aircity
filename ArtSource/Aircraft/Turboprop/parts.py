"""Regional twin turboprop (high wing, T-tail, 50 seats), built procedurally.

Stations `s` are metres aft of the nose tip. Blender Y = s - S_REF, so the model origin sits on the ground
under the main-gear axles. Nose points -Y, port wing is +X, Z up.

The layout is the classic regional turboprop: a slim pressurised tube slung low under a straight high wing, two
big six-blade propellers on nacelles that also carry the main gear, and a T-tail. Nothing about it is a copy of a
real type.
"""
import math

import numpy as np
from mathutils import Vector

from fuselage import Fuselage as BaseFuselage, Loft, profile
from geom import MeshBuilder, airfoil_eval
from shapes import _fd_normal, drape_pane, lifting_surface, revolve, rounded_outline, tube, wheel

NOSE_S = 2.7            # nose gear axle station
S_REF = NOSE_S + 9.2    # main axles -> model origin (wheelbase 9.2 m)


def Y(s):
    return s - S_REF


def P(x, s, z):
    return (x, Y(s), z)


# ============================================================== fuselage

KEEL = 1.05             # fuselage bottom line (constant section): low, so passengers board by airstair
CROWN = 3.95            # 2.9 m tall, 2.8 m wide: four abreast
HALF_WIDTH = 1.40
TIP_H = 1.15            # radome tip height above the keel: a blunt nose

# Nose profiles, as on the jets: each rises from the tip (0) to the constant section (1) as 1 - (1 - t^a)^b,
# t = s / length. Short and round: the top line reaches the crown within two fuselage heights.
NOSE_UP = (5.2, 0.92, 3.0)      # (length, a, b): side view, tip -> crown
NOSE_LO = (3.6, 0.72, 3.2)      # side view, tip -> keel
NOSE_W = (4.6, 0.80, 3.0)       # plan view, centre line -> full width


def _rise(s, shape):
    length, a, b = shape
    t = min(max(s / length, 0.0), 1.0)
    return 1.0 - (1.0 - t ** a) ** b


def _nose_samples(length):
    """Stations for sampling a nose profile, dense at the tip where it curves fastest."""
    return [0.008] + [length * (1.0 - math.cos(a)) for a in np.linspace(0.06, math.pi / 2, 24)]


class Fuselage(BaseFuselage):
    S0 = 0.008
    S1 = 25.40
    ML = 14
    MU = 14

    # constant section s 5-16.5, then the tail upsweep (about 14 deg about the main gear) to a slim tail cone
    zt = profile([(s, KEEL + TIP_H + (CROWN - KEEL - TIP_H) * _rise(s, NOSE_UP)) for s in _nose_samples(NOSE_UP[0])] +
                 [(16.5, CROWN), (18.5, 3.93), (20.5, 3.86), (22.5, 3.74), (24.0, 3.60), (25.4, 3.45)])
    zb = profile([(s, KEEL + TIP_H * (1.0 - _rise(s, NOSE_LO))) for s in _nose_samples(NOSE_LO[0])] +
                 [(15.5, KEEL), (17.0, 1.10), (18.5, 1.30), (20.0, 1.65), (21.5, 2.10), (23.0, 2.60), (24.5, 3.05),
                  (25.4, 3.25)])
    hw = profile([(s, HALF_WIDTH * _rise(s, NOSE_W)) for s in _nose_samples(NOSE_W[0])] +
                 [(15.5, HALF_WIDTH), (17.5, 1.38), (19.5, 1.28), (21.5, 1.08), (23.0, 0.82), (24.5, 0.50),
                  (25.4, 0.22)])
    fm = profile([(0.008, 0.5), (16.0, 0.5), (25.4, 0.56)])
    nt = profile([(0.008, 2.0), (25.4, 2.0)])
    nb = profile([(0.008, 2.0), (4.0, 2.0), (6.0, 2.4), (16.0, 2.4), (18.0, 2.0), (25.4, 2.0)])     # flattish belly


def fuselage_stations():
    s = [0.008, 0.02, 0.04, 0.07, 0.11, 0.16, 0.22, 0.30, 0.40, 0.52, 0.66, 0.82, 1.0]
    for stop, step in ((4.0, 0.2), (7.0, 0.5), (16.0, 0.6), (25.2, 0.3)):
        while s[-1] < stop - 1e-6:
            s.append(round(s[-1] + step, 4))
    s.append(Fuselage.S1)
    return s


def build_fuselage():
    F = Fuselage
    b = MeshBuilder()
    loft = Loft(F, b, fuselage_stations(), P)
    last = len(loft.st) - 1
    loft.fan_cap(0, P(0, 0.0, KEEL + TIP_H), s_tip=0.0)
    loft.fan_cap(last, P(0, F.S1 + 0.08, float(F.zm(F.S1))), s_tip=F.S1 + 0.08)
    return b


# ============================================================== cockpit windows

def _out_dir(p):
    c = Fuselage.sec(p.y + S_REF)
    return Vector((p.x, 0.0, p.z - c["zm"]))


R_ARC = 1.3     # window outlines are laid out in (station, phi * R_ARC): roughly metres along the skin


def skin_surface(sign):
    """surf(s, v) for drape_pane: the skin at station s, angle v / R_ARC from the top centre (sign -1 = starboard)."""
    def fn(s, v):
        x, z = Fuselage.ring_point(s, v / R_ARC)
        return P(sign * x, s, z)
    return lambda s, v: _fd_normal(fn, s, v, _out_dir)


def _outline(corners, radii):
    """Corners as (station, degrees from the top centre)."""
    return rounded_outline([(s, math.radians(d) * R_ARC) for s, d in corners], radii)


def _phi(s, h):
    """Degrees from the top centre to the port skin at station s, height h above the keel."""
    c = Fuselage.sec(s)
    z = KEEL + h
    return math.degrees(math.atan2(Fuselage.side_x(s, z), z - c["zm"]))


def _at(s, h=None, deg=None):
    return (s, deg if deg is not None else _phi(s, h))


def _top_reaches(h):
    """Station where the nose top line climbs through height h above the keel."""
    lo, hi = 0.01, NOSE_UP[0]
    for _ in range(50):
        mid = 0.5 * (lo + hi)
        lo, hi = (mid, hi) if float(Fuselage.zt(mid)) < KEEL + h else (lo, mid)
    return 0.5 * (lo + hi)


# Four-pane flight deck (port side; mirrored to starboard): a windshield pane either side of a thin centre post,
# an upright post, then a deep side window. The sill is level, so the windshield's lower edge follows the nose
# round from the centre line (where the top line passes sill height) to the post.
SILL = 1.95     # band bottom, metres above the keel
HEAD = 2.62     # band top
WS_FRONT = _top_reaches(SILL)
WS_TOP = _top_reaches(HEAD)
POST = WS_TOP + 0.35
SIDE_1 = POST + 0.10
COCKPIT_WINDOWS = [
    ("windshield", [_at(WS_FRONT, deg=2.0), _at(WS_FRONT + 0.25, SILL), _at(POST - 0.30, SILL), _at(POST, SILL),
                    _at(POST + 0.12, HEAD), _at(WS_TOP + 0.12, HEAD), _at(WS_TOP, deg=2.0)],
     [0.03, 0.08, 0.25, 0.05, 0.05, 0.25, 0.03]),
    ("sidewindow", [_at(SIDE_1, SILL), _at(SIDE_1 + 0.95, SILL), _at(SIDE_1 + 0.95, HEAD), _at(SIDE_1 + 0.25, HEAD)],
     [0.05, 0.14, 0.14, 0.05]),
]


def build_windows():
    b = MeshBuilder()
    for sign in (1, -1):
        surf = skin_surface(sign)
        for tag, corners, radii in COCKPIT_WINDOWS:
            drape_pane(b, _outline(corners, radii), surf, tag=tag, lift=0.008, sink=0.02)
    return b


# ============================================================== wing (high, straight, tapered outboard)

WING_SIDE = HALF_WIDTH  # fuselage side
WING_BREAK = 4.6        # taper starts outboard of the nacelles
WING_TIP = 11.65        # span 23.3 m plus the tip lights: ICAO code B
LE_S0 = 9.6             # leading edge station over the cabin
TE_S0 = 12.3            # root chord 2.7 m
WING_Z0 = CROWN + 0.08  # chord line sits on the crown; the lower skin is buried in the fuselage top
DIHEDRAL = math.radians(2.5)
WING_HINGE = 0.75       # rear-spar (flap / aileron hinge) as a fraction of the local chord
WING_AF = "2415"


def le_s(y):
    return LE_S0 + max(abs(y) - WING_BREAK, 0.0) * 0.06


def te_s(y):
    return TE_S0 - max(abs(y) - WING_BREAK, 0.0) * 0.14


def wing_chord(y):
    return te_s(y) - le_s(y)


def wing_z(y):
    return WING_Z0 + abs(y) * math.tan(DIHEDRAL)


def wing_thickness(y):
    return float(np.interp(abs(y), [0, WING_BREAK, WING_TIP], [1.0, 1.0, 0.8]))


def wing_station(y, h=WING_HINGE, scale_t=None, c_override=None):
    ay = abs(y)
    sgn = 1.0 if y >= 0 else -1.0
    c = c_override or wing_chord(ay)
    inc = math.radians(2.0 - 2.0 * min(ay / WING_TIP, 1.0))      # 2 deg root -> 0 at the tip
    chord_dir = Vector((0.0, math.cos(inc), -math.sin(inc)))
    up = Vector((0.0, math.sin(inc), math.cos(inc)))
    le = Vector((sgn * ay, Y(le_s(ay)), wing_z(ay)))
    return dict(le=le, chord_dir=chord_dir, up=up, c=c, h=h, scale_t=wing_thickness(ay) if scale_t is None else scale_t)


def wing_surface_z(y, s, upper=False):
    """z of the wing's lower (or upper) skin at span y, station s."""
    st = wing_station(y, h=None)
    zu, zl, _ = airfoil_eval(WING_AF)(np.array([(s - le_s(y)) / st["c"]]))
    return float(st["le"].z + (zu[0] if upper else zl[0]) * st["scale_t"] * st["c"])


def hinge_point(y):
    st = wing_station(y)
    return st["le"] + st["chord_dir"] * (st["c"] * st["h"])


# trailing-edge pieces per side: (name, from, to); "filler" pieces are fixed (the middle one sits behind the nacelle)
TE_PIECES = [("Flap_{}_In", 1.50, 3.45), ("filler", 3.45, 4.75), ("Flap_{}_Out", 4.75, 8.20), ("filler", 8.20, 8.35),
             ("Aileron_{}", 8.35, 11.30)]
TIP_START = 11.35


def build_wing():
    """Returns {"Wing": fixed MeshBuilder, "Flap_L_In": ..., "Aileron_R": ...}."""
    ys = sorted(set([round(v, 4) for v in
                     list(np.linspace(-TIP_START, -WING_BREAK, 8)) + list(np.linspace(-WING_BREAK, WING_BREAK, 9)) +
                     list(np.linspace(WING_BREAK, TIP_START, 8)) + [-WING_SIDE, WING_SIDE]]))
    wing = MeshBuilder()
    lifting_surface(wing, [wing_station(y) for y in ys], WING_AF, "Body", "wing", flip=True)
    out = {"Wing": wing}
    for sgn, side in ((1, "L"), (-1, "R")):
        tip = [wing_station(sgn * y, h=None, scale_t=t) for y, t in ((TIP_START - 0.03, 0.8), (11.5, 0.7), (WING_TIP, 0.45))]
        lifting_surface(wing, tip, WING_AF, "Body", "wing", flip=(sgn > 0))
        for name, y0, y1 in TE_PIECES:
            yy = np.linspace(y0, y1, max(int((y1 - y0) / 0.9) + 2, 2))
            target = wing if name == "filler" else out.setdefault(name.format(side), MeshBuilder())
            tag = "wing_filler" if name == "filler" else "wing_" + name.split("_")[0].lower()
            lifting_surface(target, [wing_station(sgn * y) for y in yy], WING_AF, "Body", tag, part="ctrl",
                            flip=(sgn > 0))
    return out


# ============================================================== engines: nacelles and propellers

ENGINE_X = 4.10         # nacelle centre line (main-gear track 8.2 m)
ENGINE_Z = 3.45         # thrust line; the nacelle top is buried in the wing underside
NACELLE_S0 = 7.50       # nacelle front (spinner backplate)
NACELLE_S1 = 14.10      # tail of the nacelle, well behind the wing trailing edge
# (station, half width, height above the thrust line, depth below it): round at the spinner, deepening into an
# oval tub that bulges down behind the wing to house the main gear, then tapering out
NACELLE = [(7.50, 0.40, 0.40, 0.40), (7.95, 0.50, 0.55, 0.55), (8.60, 0.56, 0.68, 0.72), (9.60, 0.58, 0.75, 0.90),
           (10.8, 0.58, 0.78, 1.05), (12.0, 0.56, 0.72, 1.10), (12.8, 0.50, 0.55, 0.95), (13.6, 0.36, 0.30, 0.60),
           (14.1, 0.18, 0.12, 0.25)]
PROP_S = 7.35           # propeller plane
PROP_R = 1.95           # six blades, 3.9 m disc
SPINNER_TIP_S = 6.95
NACELLE_N = 2.3         # superellipse exponent of the sections: a little boxier than an ellipse


def nacelle_bottom_z(s):
    return ENGINE_Z - float(np.interp(s, [p[0] for p in NACELLE], [p[3] for p in NACELLE]))


def _nacelle(b, x):
    """Loft the nacelle sections at x. Rings run from +X round through -Z, as `tube` does, so the winding is outward."""
    segs = 28
    rings = []
    for s, hw, ht, hb in NACELLE:
        ring = []
        for k in range(segs):
            a = 2 * math.pi * k / segs
            ca, sa = math.cos(a), math.sin(a)
            hh = hb if sa > 0 else ht
            r = (abs(ca) ** NACELLE_N + abs(sa) ** NACELLE_N) ** (-1.0 / NACELLE_N)
            ring.append(P(x + hw * r * ca, s, ENGINE_Z - hh * r * sa))
        rings.append(ring)
    base = b.add_verts([q for r in rings for q in r])
    for i in range(len(rings) - 1):
        for k in range(segs):
            k2 = (k + 1) % segs
            b.add_face([base + i * segs + k, base + i * segs + k2, base + (i + 1) * segs + k2, base + (i + 1) * segs + k],
                       "Body", None, "nacelle")
    b.add_face([base + k for k in range(segs)][::-1], "Body", None, "nacelle")
    b.add_face([base + (len(rings) - 1) * segs + k for k in range(segs)], "Body", None, "nacelle")


def build_engine(sign):
    """Returns (static nacelle, spinning propeller, hub) for one side."""
    b = MeshBuilder()
    x = sign * ENGINE_X
    _nacelle(b, x)
    # exhaust stub along the outboard upper flank, and the oil-cooler intake under the spinner
    ex = Vector(P(x + sign * 0.50, 8.9, ENGINE_Z + 0.38))
    tube(b, [ex, ex + Vector((sign * 0.10, 0.55, 0.05))], lambda t: 0.09, "DarkMetal", "nacelle_detail", segs=12)
    revolve(b, [(-0.02, 0.001), (0.0, 0.15), (0.45, 0.13)], P(x, NACELLE_S0 + 0.30, ENGINE_Z - 0.42), (0, 1, 0), "Dark",
            "inlet", segs=14)

    prop = MeshBuilder()
    hub = Vector(P(x, PROP_S, ENGINE_Z))
    # spinner: ogive from the tip to the backplate, ending just inside the nacelle front
    spin_len = NACELLE_S0 - SPINNER_TIP_S
    prof = []
    for k in range(13):
        t = k / 12
        prof.append((spin_len * t, max(0.39 * (1 - (1 - t) ** 2.4) ** 0.5, 0.002)))
    prof.append((spin_len + 0.03, 0.39))
    prof.append((spin_len + 0.03, 0.30))
    revolve(prop, prof, P(x, SPINNER_TIP_S, ENGINE_Z), (0, 1, 0), "Body", "spinner", segs=28, cap_end=True, ref=(0, 0, 1))
    _blades(prop, hub, PROP_R, 6, sign)
    return b, prop, tuple(hub)


def _blades(b, hub, R, count, sign):
    """`count` propeller blades about the hub (axis +Y). Each blade is a twisted NACA 4408 loft, scimitar-tipped."""
    af = airfoil_eval("4408")
    radii = list(np.linspace(0.30, 0.88 * R, 12)) + [0.92 * R, 0.95 * R, 0.975 * R, 0.99 * R, R]
    xs = np.unique(np.concatenate([[0.0], (1 - np.cos(np.linspace(0.05, math.pi / 2, 9))) * 0.5, np.linspace(0.55, 1.0, 5)]))

    def chord(r):
        x = r / R
        c = 0.18 + 0.20 * math.sin(math.pi * min((x - 0.12) / 0.80, 1.0) * 0.65)
        if x > 0.88:
            c *= math.sqrt(max(1 - ((x - 0.88) / 0.125) ** 2, 0.05))
        return c

    def beta(r):
        return math.atan(2.6 / (2 * math.pi * r))

    for k in range(count):
        a0 = 2 * math.pi * k / count
        ca, sa = math.cos(a0), math.sin(a0)
        rings = []
        for r in radii:
            c, bt = chord(r), beta(r)
            thick = 1.0 if r > 0.55 else 1.0 + 1.8 * (0.55 - r) / 0.25
            zu, zl, _ = af(xs)
            sweep = 0.12 * max(r / R - 0.6, 0.0) ** 2 * R     # scimitar: the tip leans back against rotation
            seq = list(zip(xs, zu * thick)) + list(zip(xs[::-1][1:-1], (zl * thick)[::-1][1:-1]))
            pts = []
            for xf, zf in seq:
                tang = (0.35 - xf) * c - sweep    # + toward the direction of motion
                ax = zf * c
                tz = tang * math.cos(bt) - ax * math.sin(bt)
                ty = -(tang * math.sin(bt) + ax * math.cos(bt))
                # blade at angle a0 about +Y: radial (r) and tangential (tz) in the X-Z plane
                local = Vector((r * ca - tz * sa, ty, r * sa + tz * ca))
                if sign < 0:
                    local = Vector((-local.x, local.y, local.z))
                pts.append(hub + local)
            rings.append(pts)
        n = len(rings[0])
        base = b.add_verts([p for rr in rings for p in rr])
        for i in range(len(rings) - 1):
            mat = "PropTip" if radii[i] >= 0.88 * R else "PropBlade"
            for j in range(n):
                j2 = (j + 1) % n
                q = [base + i * n + j, base + (i + 1) * n + j, base + (i + 1) * n + j2, base + i * n + j2]
                b.add_face(q[::-1] if sign > 0 else q, mat, None, "blade")
        tipf = [base + (len(rings) - 1) * n + j for j in range(n)]
        b.add_face(tipf if sign > 0 else tipf[::-1], "PropTip", None, "blade")
        rootf = [base + j for j in range(n)]
        b.add_face(rootf[::-1] if sign > 0 else rootf, "PropBlade", None, "blade")


# ============================================================== T-tail

VT_ROOT_Z = 3.70        # the fin surface starts inside the tail cone
VT_TOP_Z = 7.50
VT_HINGE = 0.68
VT_LE_ROOT = 20.0
VT_SWEEP = math.tan(math.radians(38))
RUDDER = (4.05, 7.35)


def vt_le_s(z):
    return VT_LE_ROOT + (z - VT_ROOT_Z) * VT_SWEEP


def vt_chord(z):
    return 4.2 - (z - VT_ROOT_Z) / (VT_TOP_Z - VT_ROOT_Z) * (4.2 - 2.3)


def vt_station(z, h=VT_HINGE, scale_t=1.0):
    return dict(le=Vector((0.0, Y(vt_le_s(z)), z)), chord_dir=Vector((0, 1, 0)), up=Vector((1, 0, 0)), c=vt_chord(z), h=h,
                scale_t=scale_t)


def vt_hinge(z):
    st = vt_station(z)
    return st["le"] + st["chord_dir"] * (st["c"] * st["h"])


HT_Z = VT_TOP_Z - 0.12  # the stabiliser sits on the fin top
HT_ROOT_LE = 22.55
HT_SPAN = 4.25          # 8.5 m across
HT_SWEEP = math.tan(math.radians(12))
HT_HINGE = 0.68
EL_ROOT = 0.45


def ht_station(x, h=HT_HINGE, scale_t=1.0):
    ax = abs(x)
    le = HT_ROOT_LE + ax * HT_SWEEP
    c = 2.6 - (ax / HT_SPAN) * 1.25
    return dict(le=Vector((x, Y(le), HT_Z)), chord_dir=Vector((0, 1, 0)), up=Vector((0, 0, 1)), c=c, h=h, scale_t=scale_t)


def ht_hinge(x):
    st = ht_station(x)
    return st["le"] + st["chord_dir"] * (st["c"] * st["h"])


def build_htail():
    out = {}
    stab = MeshBuilder()
    xs = sorted(set([round(v, 4) for v in np.linspace(-HT_SPAN + 0.2, HT_SPAN - 0.2, 11)]))
    lifting_surface(stab, [ht_station(x) for x in xs], "0010", "Body", "htail", flip=True)
    for sgn, side in ((1, "L"), (-1, "R")):
        tip = [ht_station(sgn * x, h=None, scale_t=t) for x, t in ((HT_SPAN - 0.22, 1.0), (HT_SPAN - 0.1, 0.8), (HT_SPAN, 0.45))]
        lifting_surface(stab, tip, "0010", "Body", "htail", flip=(sgn > 0))
        el = MeshBuilder()
        lifting_surface(el, [ht_station(sgn * x) for x in np.linspace(EL_ROOT, HT_SPAN - 0.25, 5)], "0010", "Body",
                        "elevator", part="ctrl", flip=(sgn > 0))
        out["Elevator_" + side] = el
    # bullet fairing along the fin / stabiliser junction
    s0 = vt_le_s(HT_Z) - 0.45
    s1 = vt_le_s(HT_Z) + vt_chord(HT_Z) + 0.35
    prof = [(0.0, 0.002)] + [((s1 - s0) * t, 0.30 * (1 - (2 * t - 1) ** 2) ** 0.55) for t in np.linspace(0.04, 0.96, 12)] + \
           [(s1 - s0, 0.002)]
    revolve(stab, prof, P(0, s0, HT_Z + 0.02), (0, 1, 0), "Body", "bullet", segs=20, ref=(0, 0, 1))
    out["HStab"] = stab
    return out


def build_vtail():
    out = {}
    fin = MeshBuilder()
    lifting_surface(fin, [vt_station(z) for z in np.linspace(VT_ROOT_Z - 0.3, HT_Z - 0.02, 9)], "0010", "Body", "vtail")
    lifting_surface(fin, [vt_station(z, h=None) for z in (HT_Z - 0.03, HT_Z + 0.08, VT_TOP_Z)], "0010", "Body", "vtail")
    # dorsal fillet blending the fin root into the fuselage crown
    s0, s1 = 17.9, VT_LE_ROOT + 0.5
    rings = []
    for s in np.linspace(s0, s1, 10):
        t = (s - s0) / (s1 - s0)
        zt = float(Fuselage.zt(s))
        ztop = zt + 0.01 + (VT_ROOT_Z + 0.75 - zt) * t**2.2
        wb = 0.08 + 0.16 * math.sin(math.pi * t) ** 0.6
        wt = 0.02 + 0.08 * t
        port = []
        for h in (0.0, 0.25, 0.55, 0.8, 1.0):
            px = wb * (1 - h) ** 2.4 + wt * h * (1 - h * 0.35)
            port.append((px, Fuselage.top_z(s, wb) - 0.03 + (ztop - Fuselage.top_z(s, wb) + 0.03) * h))
        rings.append([P(px, s, pz) for px, pz in port] + [P(0.0, s, ztop + wt * 0.6)] +
                     [P(-px, s, pz) for px, pz in port[::-1]])
    n = len(rings[0])
    base = fin.add_verts([p for r in rings for p in r])
    for i in range(len(rings) - 1):
        for j in range(n - 1):
            fin.add_face([base + i * n + j, base + i * n + j + 1, base + (i + 1) * n + j + 1, base + (i + 1) * n + j],
                         "Body", None, "dorsal")
    fin.add_face([base + j for j in range(n)][::-1], "Body", None, "dorsal")
    out["VStab"] = fin
    rud = MeshBuilder()
    lifting_surface(rud, [vt_station(z) for z in np.linspace(RUDDER[0], RUDDER[1], 7)], "0010", "Body", "rudder", part="ctrl")
    out["Rudder"] = rud
    return out


# ============================================================== landing gear

NOSE_R = 0.27
NOSE_AXLE_Z = NOSE_R - 0.02
NOSE_WHEEL_X = 0.20
MAIN_X = ENGINE_X       # the main legs hang from the nacelles
MAIN_WHEEL_DX = 0.30    # wheel offset from the leg
MAIN_R = 0.45
MAIN_AXLE_Z = MAIN_R - 0.02


def build_gear():
    """Returns {"Gear": fixed legs, "NoseGear": steering part, "Wheel_*": spinning wheels}."""
    b, nose, out = MeshBuilder(), MeshBuilder(), {}
    for sx, side in ((1, "L"), (-1, "R")):
        x = sx * MAIN_X
        top = nacelle_bottom_z(S_REF) + 0.35
        tube(b, [P(x, S_REF, top), P(x, S_REF, 1.35)], lambda t: 0.09, "Metal", "gearleg", segs=12)
        tube(b, [P(x, S_REF, 1.40), P(x, S_REF, MAIN_AXLE_Z + 0.12)], lambda t: 0.065, "Chrome", "gearleg", segs=12)
        tube(b, [P(x - sx * (MAIN_WHEEL_DX + 0.04), S_REF, MAIN_AXLE_Z), P(x + sx * (MAIN_WHEEL_DX + 0.04), S_REF, MAIN_AXLE_Z)],
             lambda t: 0.06, "DarkMetal", "gearleg", segs=10)
        # drag brace forward into the nacelle and the torque link
        tube(b, [P(x, S_REF, 1.70), P(x, S_REF - 0.95, nacelle_bottom_z(S_REF - 0.95) + 0.3)], lambda t: 0.045, "Metal", "gearleg", segs=8)
        tube(b, [P(x, S_REF - 0.02, 1.30), P(x, S_REF - 0.16, 1.05), P(x, S_REF - 0.02, 0.82)], lambda t: 0.025, "Metal",
             "gearleg", segs=8)
        for k, dx in ((1, -MAIN_WHEEL_DX), (2, MAIN_WHEEL_DX)):
            w = MeshBuilder()
            wheel(w, P(x + sx * dx, S_REF, MAIN_AXLE_Z), MAIN_R, 0.30, "wheel")
            out[f"Wheel_{side}{k}"] = w
    # nose gear: the upper strut is fixed, everything below the steering collar turns
    tube(b, [P(0, NOSE_S - 0.04, float(Fuselage.zb(NOSE_S)) + 0.25), P(0, NOSE_S, 0.85)], lambda t: 0.07, "Metal",
         "nosegear", segs=12)
    tube(nose, [P(0, NOSE_S, 0.90), P(0, NOSE_S, NOSE_AXLE_Z + 0.08)], lambda t: 0.05, "Chrome", "nosegear", segs=12)
    tube(nose, [P(-NOSE_WHEEL_X - 0.04, NOSE_S, NOSE_AXLE_Z), P(NOSE_WHEEL_X + 0.04, NOSE_S, NOSE_AXLE_Z)],
         lambda t: 0.045, "DarkMetal", "nosegear", segs=10)
    tube(nose, [P(0, NOSE_S + 0.02, 0.78), P(0, NOSE_S + 0.14, 0.64), P(0, NOSE_S + 0.03, 0.50)], lambda t: 0.02,
         "Metal", "nosegear", segs=8)
    # taxi light housing on the strut
    revolve(nose, [(-0.05, 0.001), (-0.04, 0.055), (0.03, 0.06), (0.04, 0.001)], P(0, NOSE_S - 0.09, 0.80), (0, 1, 0),
            "LightLens", "light", segs=12)
    for side, sx in (("L", 1), ("R", -1)):
        w = MeshBuilder()
        wheel(w, P(sx * NOSE_WHEEL_X, NOSE_S, NOSE_AXLE_Z), NOSE_R, 0.17, "wheel")
        out["Wheel_Nose_" + side] = w
    out["Gear"] = b
    out["NoseGear"] = nose
    return out


# ============================================================== lights, antennas, probes

NAV_S = le_s(WING_TIP) + 0.12
BEACON_TOP_S = 18.0
BEACON_BOTTOM_S = 14.0
LANDING = (2.45, le_s(2.45))     # (span, station) in the wing leading edge, inboard of the nacelles
LOGO_LIGHT = (0.55, 19.6)        # (x, station) on the rear fuselage crown, aimed up at the fin


def build_details():
    b = MeshBuilder()
    tip_z = wing_station(WING_TIP, h=None)["le"].z
    for sx, mat in ((1, "LightRed"), (-1, "LightGreen")):
        revolve(b, [(-0.08, 0.001), (-0.05, 0.04), (0.04, 0.045), (0.08, 0.001)],
                (sx * (WING_TIP + 0.02), Y(NAV_S), tip_z + 0.01), (0, 1, 0), mat, "light", segs=12)
        revolve(b, [(-0.05, 0.001), (-0.025, 0.032), (0.03, 0.032), (0.05, 0.001)],
                (sx * (WING_TIP + 0.02), Y(NAV_S + 0.26), tip_z + 0.02), (0, 1, 0), "LightWhite", "light", segs=10)
        # landing light in the wing leading edge
        yl, sl = LANDING
        st = wing_station(sx * yl, h=None)
        revolve(b, [(-0.004, 0.001), (-0.002, 0.09), (0.05, 0.10)], (sx * yl, st["le"].y + 0.006, st["le"].z + 0.01),
                (0, 1, 0), "LightLens", "light", segs=14)
        # logo light on the rear crown
        xl, sl = LOGO_LIGHT
        revolve(b, [(-0.01, 0.05), (0.03, 0.04), (0.045, 0.001)], P(sx * xl, sl, float(Fuselage.top_z(sl, xl)) - 0.01),
                (0, 0, 1), "LightWhite", "light", segs=10, ref=(1, 0, 0))
    # beacons on the crown and belly, white tail light at the tail cone
    revolve(b, [(-0.01, 0.08), (0.04, 0.07), (0.08, 0.04), (0.10, 0.001)], P(0, BEACON_TOP_S, float(Fuselage.zt(BEACON_TOP_S)) - 0.02),
            (0, 0, 1), "LightRed", "light", segs=14, ref=(1, 0, 0))
    revolve(b, [(-0.01, 0.08), (0.04, 0.07), (0.08, 0.04), (0.10, 0.001)], P(0, BEACON_BOTTOM_S, Fuselage.bottom_z(BEACON_BOTTOM_S, 0) + 0.02),
            (0, 0, -1), "LightRed", "light", segs=14, ref=(1, 0, 0))
    revolve(b, [(-0.03, 0.001), (-0.015, 0.045), (0.03, 0.045), (0.05, 0.001)], P(0, Fuselage.S1 + 0.02, float(Fuselage.zm(Fuselage.S1))),
            (0, 1, 0), "LightWhite", "light", segs=10)
    # antennas on the crown (ahead of and behind the wing) and belly
    for s, up, h in ((6.5, 1, 0.30), (15.0, 1, 0.26), (16.0, -1, 0.22)):
        z = float(Fuselage.zt(s)) if up > 0 else Fuselage.bottom_z(s, 0)
        tube(b, [P(0, s, z - up * 0.02), P(0, s + 0.2, z + up * h)], lambda t: 0.035 * (1 - 0.6 * t), "Dark", "detail",
             segs=6, squash=0.3)
    # pitot probes on the nose
    for sx, z in ((1, KEEL + 1.55), (-1, KEEL + 1.55)):
        s = 1.5
        x = sx * Fuselage.side_x(s, z)
        tube(b, [P(x, s, z), P(x + sx * 0.08, s - 0.02, z), P(x + sx * 0.09, s - 0.16, z)], lambda t: 0.01, "Metal",
             "detail", segs=6)
    return b
