"""Long-range trijet (two wing engines and an S-duct engine in the fin root, 250 seats), built procedurally.

Stations `s` are metres aft of the nose tip. Blender Y = s - S_REF, so the model origin sits on the ground
under the main-gear bogies. Nose points -Y, port wing is +X, Z up.

The veteran of the fleet: a twin-aisle tube of modest width, a lightly swept wing with no winglets, long slim
low-bypass nacelles, and the third engine fed by an intake above the rear fuselage with its exhaust at the tail
cone. Nothing about it is a copy of a real type.
"""
import math

import numpy as np
from mathutils import Vector

from fuselage import Fuselage as BaseFuselage, Loft, profile
from geom import MeshBuilder
from shapes import _fd_normal, drape_pane, lifting_surface, revolve, rounded_outline, tube, wheel

NOSE_S = 5.8            # nose gear axle station
S_REF = NOSE_S + 22.0   # main bogie centre -> model origin (wheelbase 22.0 m)


def Y(s):
    return s - S_REF


def P(x, s, z):
    return (x, Y(s), z)


# ============================================================== fuselage

KEEL = 2.10             # fuselage bottom line (constant section)
CROWN = 7.20            # 5.1 m tall, 5.1 m wide: seven abreast, twin aisle
HALF_WIDTH = 2.55
TIP_H = 1.75            # radome tip height above the keel

NOSE_UP = (11.0, 0.95, 3.2)     # (length, a, b): side view, tip -> crown
NOSE_LO = (8.6, 0.70, 3.3)      # side view, tip -> keel
NOSE_W = (9.2, 0.78, 3.0)       # plan view, centre line -> full width


def _rise(s, shape):
    length, a, b = shape
    t = min(max(s / length, 0.0), 1.0)
    return 1.0 - (1.0 - t ** a) ** b


def _nose_samples(length):
    """Stations for sampling a nose profile, dense at the tip where it curves fastest."""
    return [0.008] + [length * (1.0 - math.cos(a)) for a in np.linspace(0.06, math.pi / 2, 28)]


class Fuselage(BaseFuselage):
    S0 = 0.008
    S1 = 57.80
    ML = 16
    MU = 16

    # constant section to s 40, a wing-to-body fairing s 19-38, then the tail upsweep to the centre engine's
    # exhaust, which keeps the tail cone fat right to the end
    zt = profile([(s, KEEL + TIP_H + (CROWN - KEEL - TIP_H) * _rise(s, NOSE_UP)) for s in _nose_samples(NOSE_UP[0])] +
                 [(40.0, CROWN), (44.0, 7.16), (48.0, 7.02), (51.0, 6.84), (54.0, 6.62), (56.5, 6.46), (57.8, 6.40)])
    zb = profile([(s, KEEL + TIP_H * (1.0 - _rise(s, NOSE_LO))) for s in _nose_samples(NOSE_LO[0])] +
                 [(10.0, KEEL), (18.0, KEEL), (20.5, 1.98), (24.0, 1.86), (29.0, 1.82), (33.0, 1.86), (36.0, 1.98),
                  (38.5, 2.10), (41.0, 2.22), (44.0, 2.55), (47.0, 3.10), (50.0, 3.75), (53.0, 4.40), (56.0, 4.95),
                  (57.8, 5.20)])
    hw = profile([(s, HALF_WIDTH * _rise(s, NOSE_W)) for s in _nose_samples(NOSE_W[0])] +
                 [(11.0, HALF_WIDTH), (19.0, HALF_WIDTH), (22.0, 2.60), (26.0, 2.65), (32.0, 2.65), (36.0, 2.60),
                  (39.0, HALF_WIDTH), (43.0, 2.50), (47.0, 2.32), (50.0, 2.05), (53.0, 1.65), (55.5, 1.22),
                  (57.8, 0.78)])
    fm = profile([(0.008, 0.5), (19.0, 0.5), (22.0, 0.46), (35.0, 0.46), (39.0, 0.5), (57.8, 0.5)])
    nt = profile([(0.008, 2.0), (57.8, 2.0)])
    nb = profile([(0.008, 2.0), (19.0, 2.0), (22.0, 2.7), (35.0, 2.7), (39.0, 2.0), (57.8, 2.0)])


def fuselage_stations():
    s = [0.008, 0.02, 0.04, 0.07, 0.11, 0.16, 0.22, 0.30, 0.40, 0.52, 0.66, 0.82, 1.0]
    for stop, step in ((7.0, 0.25), (12.0, 0.6), (18.0, 1.0), (40.0, 0.6), (57.5, 0.4)):
        while s[-1] < stop - 1e-6:
            s.append(round(s[-1] + step, 4))
    s.append(Fuselage.S1)
    return s


EXHAUST_R = 0.62        # the centre engine's nozzle, in the end of the tail cone


def build_fuselage():
    F = Fuselage
    b = MeshBuilder()
    loft = Loft(F, b, fuselage_stations(), P)
    last = len(loft.st) - 1
    loft.fan_cap(0, P(0, 0.0, KEEL + TIP_H), s_tip=0.0)
    zc = float(F.zm(F.S1))
    loft.fan_cap(last, P(0, F.S1 + 0.05, zc), s_tip=F.S1 + 0.05)
    # the centre engine's exhaust: a nozzle ring out of the tail cone end, dark inside
    o = P(0, F.S1, zc)
    revolve(b, [(-0.10, EXHAUST_R + 0.06), (0.55, EXHAUST_R + 0.02), (0.70, EXHAUST_R - 0.04)], o, (0, 1, 0), "DarkMetal",
            "tailexhaust", segs=28)
    revolve(b, [(0.70, EXHAUST_R - 0.04), (0.65, EXHAUST_R - 0.10), (-0.05, EXHAUST_R - 0.14)], o, (0, 1, 0), "Dark",
            "tailexhaust", segs=28, flip=True)
    revolve(b, [(-0.05, EXHAUST_R - 0.14), (0.25, 0.22), (0.55, 0.02)], o, (0, 1, 0), "DarkMetal", "tailexhaust", segs=20)
    return b


# ============================================================== cockpit windows

def _out_dir(p):
    c = Fuselage.sec(p.y + S_REF)
    return Vector((p.x, 0.0, p.z - c["zm"]))


R_ARC = 2.5


def skin_surface(sign):
    def fn(s, v):
        x, z = Fuselage.ring_point(s, v / R_ARC)
        return P(sign * x, s, z)
    return lambda s, v: _fd_normal(fn, s, v, _out_dir)


def _outline(corners, radii):
    return rounded_outline([(s, math.radians(d) * R_ARC) for s, d in corners], radii)


def _phi(s, h):
    c = Fuselage.sec(s)
    z = KEEL + h
    return math.degrees(math.atan2(Fuselage.side_x(s, z), z - c["zm"]))


def _at(s, h=None, deg=None):
    return (s, deg if deg is not None else _phi(s, h))


def _top_reaches(h):
    lo, hi = 0.01, NOSE_UP[0]
    for _ in range(50):
        mid = 0.5 * (lo + hi)
        lo, hi = (mid, hi) if float(Fuselage.zt(mid)) < KEEL + h else (lo, mid)
    return 0.5 * (lo + hi)


# Six-pane flight deck of the older style: flat-ish windshield panes either side of a centre post, a raked post,
# a sliding side window and a small rear "eyebrow-less" side pane.
SILL = 3.05
HEAD = 3.95
WS_FRONT = _top_reaches(SILL)
WS_TOP = _top_reaches(HEAD)
POST = WS_TOP + 0.50
SIDE_1 = POST + 0.12
SIDE_2 = SIDE_1 + 1.00 + 0.14
COCKPIT_WINDOWS = [
    ("windshield", [_at(WS_FRONT, deg=1.6), _at(WS_FRONT + 0.32, SILL), _at(POST - 0.40, SILL), _at(POST, SILL),
                    _at(POST + 0.26, HEAD), _at(WS_TOP + 0.16, HEAD), _at(WS_TOP, deg=1.6)],
     [0.04, 0.09, 0.30, 0.06, 0.06, 0.30, 0.04]),
    ("sidewindow", [_at(SIDE_1, SILL), _at(SIDE_1 + 1.00, SILL), _at(SIDE_1 + 1.00, HEAD - 0.05), _at(SIDE_1 + 0.38, HEAD)],
     [0.06, 0.15, 0.16, 0.06]),
    ("sidewindow", [_at(SIDE_2, SILL), _at(SIDE_2 + 0.70, SILL + 0.05), _at(SIDE_2 + 0.70, HEAD - 0.35),
                    _at(SIDE_2, HEAD - 0.07)],
     [0.06, 0.12, 0.12, 0.09]),
]


def build_windows():
    b = MeshBuilder()
    for sign in (1, -1):
        surf = skin_surface(sign)
        for tag, corners, radii in COCKPIT_WINDOWS:
            drape_pane(b, _outline(corners, radii), surf, tag=tag, lift=0.01, sink=0.025)
    return b


# ============================================================== wing

WING_SIDE = 2.55        # fuselage side
WING_KINK = 9.2         # trailing-edge kink
WING_TIP = 24.95        # span 49.9 m plus the tip lights: ICAO code D (< 52 m)
LE_S0 = 21.6            # leading edge station at the fuselage side
LE_TAN = math.tan(math.radians(31.0))
TE_S_IN = 33.2
TE_S_KINK = 33.8
TE_S_TIP = 38.6
WING_Z0 = 2.95
DIHEDRAL = math.radians(5.5)
WING_HINGE = 0.75
WING_AF = "2415"


def le_s(y):
    return LE_S0 + (abs(y) - WING_SIDE) * LE_TAN


def te_s(y):
    ay = max(abs(y), WING_SIDE)
    if ay <= WING_KINK:
        return TE_S_IN + (ay - WING_SIDE) * (TE_S_KINK - TE_S_IN) / (WING_KINK - WING_SIDE)
    return TE_S_KINK + (ay - WING_KINK) * (TE_S_TIP - TE_S_KINK) / (WING_TIP - WING_KINK)


def wing_chord(y):
    return te_s(y) - le_s(y)


def wing_z(y):
    return WING_Z0 + max(abs(y) - WING_SIDE, 0.0) * math.tan(DIHEDRAL)


def wing_thickness(y):
    return float(np.interp(abs(y), [0, WING_SIDE, WING_KINK, WING_TIP], [1.0, 1.0, 0.76, 0.64]))


def wing_station(y, h=WING_HINGE, scale_t=None, c_override=None):
    ay = abs(y)
    sgn = 1.0 if y >= 0 else -1.0
    c = c_override or wing_chord(ay)
    inc = math.radians(2.5 - 3.5 * min(ay / WING_TIP, 1.0))
    chord_dir = Vector((0.0, math.cos(inc), -math.sin(inc)))
    up = Vector((0.0, math.sin(inc), math.cos(inc)))
    le = Vector((sgn * ay, Y(le_s(ay)), wing_z(ay)))
    return dict(le=le, chord_dir=chord_dir, up=up, c=c, h=h, scale_t=wing_thickness(ay) if scale_t is None else scale_t)


def wing_surface_z(y, s, upper=False):
    from geom import airfoil_eval
    st = wing_station(y, h=None)
    zu, zl, _ = airfoil_eval(WING_AF)(np.array([(s - le_s(y)) / st["c"]]))
    return float(st["le"].z + (zu[0] if upper else zl[0]) * st["scale_t"] * st["c"])


def hinge_point(y):
    st = wing_station(y)
    return st["le"] + st["chord_dir"] * (st["c"] * st["h"])


TE_PIECES = [("Flap_{}_In", 2.65, 9.10), ("filler", 9.10, 9.30), ("Flap_{}_Out", 9.30, 18.20),
             ("filler", 18.20, 18.40), ("Aileron_{}", 18.40, 24.20)]
TIP_START = 24.25


def build_wing():
    ys = sorted(set([round(v, 4) for v in
                     list(np.linspace(-TIP_START, -WING_KINK, 11)) + list(np.linspace(-WING_KINK, WING_KINK, 11)) +
                     list(np.linspace(WING_KINK, TIP_START, 11)) + [-WING_SIDE, WING_SIDE]]))
    wing = MeshBuilder()
    lifting_surface(wing, [wing_station(y) for y in ys], WING_AF, "Body", "wing", flip=True)
    out = {"Wing": wing}
    for sgn, side in ((1, "L"), (-1, "R")):
        # a plain squared-off tip with a small fairing: no winglet, as on the older types
        tip = [wing_station(sgn * y, h=None, scale_t=t) for y, t in ((TIP_START - 0.03, 0.64), (24.6, 0.58), (WING_TIP, 0.3))]
        lifting_surface(wing, tip, WING_AF, "Body", "wing", flip=(sgn > 0))
        for name, y0, y1 in TE_PIECES:
            yy = np.linspace(y0, y1, max(int((y1 - y0) / 1.3) + 2, 2))
            target = wing if name == "filler" else out.setdefault(name.format(side), MeshBuilder())
            tag = "wing_filler" if name == "filler" else "wing_" + name.split("_")[0].lower()
            lifting_surface(target, [wing_station(sgn * y) for y in yy], WING_AF, "Body", tag, part="ctrl",
                            flip=(sgn > 0))
    return out


# ============================================================== engines: two under the wing, one in the tail

K = 1.25                # fan ~2.1 m: a low-bypass engine, so the nacelle is slim for its length
L = 1.55                # ... and long: the cowl runs well past the wing's leading edge
ENGINE_X = 8.1
ENGINE_Z = 2.15
INLET_S = 21.3
# (axial, radius) of the Narrowbody's nacelle; stretched L along the axis, K across
NACELLE = [(0.0, 0.90), (0.03, 0.955), (0.10, 0.99), (0.25, 1.02), (0.6, 1.045), (1.3, 1.055), (2.0, 1.04),
           (2.7, 0.99), (3.3, 0.90), (3.75, 0.80)]
FAN_DEPTH = 0.8


def _k(pts):
    return [(K * L * a, K * r) for a, r in pts]


def _engine_body(b, o, tag="nacelle"):
    """Nacelle, intake, fan face, duct, core and plug about origin o (axis +Y). Returns the fan hub."""
    inner = tag if tag != "nacelle" else ""
    revolve(b, _k(NACELLE), o, (0, 1, 0), "Body", tag, segs=44)
    revolve(b, _k([(0.0, 0.90), (0.05, 0.865), (0.25, 0.845), (FAN_DEPTH + 0.05, 0.835)]), o, (0, 1, 0), "Metal",
            inner + "intake", segs=44, flip=True)
    revolve(b, _k([(FAN_DEPTH + 0.05, 0.835), (FAN_DEPTH + 0.06, 0.24)]), o, (0, 1, 0), "Dark", inner + "fanface", segs=44,
            flip=True)
    revolve(b, _k([(3.75, 0.80), (3.70, 0.74), (3.45, 0.70)]), o, (0, 1, 0), "DarkMetal", inner + "fanduct", segs=44,
            flip=True)
    revolve(b, _k([(3.45, 0.70), (3.9, 0.66), (4.6, 0.56), (5.1, 0.46)]), o, (0, 1, 0), "Metal", inner + "core", segs=32)
    revolve(b, _k([(5.1, 0.46), (5.07, 0.40), (4.8, 0.38)]), o, (0, 1, 0), "DarkMetal", inner + "core", segs=32, flip=True)
    revolve(b, _k([(4.8, 0.36), (5.2, 0.30), (5.55, 0.16), (5.7, 0.02)]), o, (0, 1, 0), "DarkMetal", inner + "plug", segs=24)
    return Vector(o) + Vector((0, K * L * FAN_DEPTH, 0))


def _fan(hub, n_blades=20):
    fan = MeshBuilder()
    prof = [(0.38 + 0.42 * (k / 10), 0.25 * (1 - (1 - k / 10) ** 2.2) ** 0.5 + 0.001) for k in range(11)]
    revolve(fan, [(K * (t - 0.8), K * r) for t, r in prof], hub, (0, 1, 0), "Metal", "spinner", segs=26)
    for k in range(n_blades):
        a0 = 2 * math.pi * k / n_blades
        radial = Vector((math.cos(a0), 0, math.sin(a0)))
        tangential = Vector((-math.sin(a0), 0, math.cos(a0)))
        axial = Vector((0, 1, 0))
        rings = []
        for r in np.linspace(0.24, 0.83, 6):
            twist = math.radians(55 - 30 * (r - 0.24) / 0.59)
            half = K * (0.07 + 0.03 * (r - 0.24) / 0.59)
            chord = tangential * math.cos(twist) + axial * math.sin(twist)
            thick = chord.cross(radial).normalized() * 0.01
            c = hub + radial * (K * r)
            rings.append([c - chord * half - thick, c + chord * half - thick, c + chord * half + thick, c - chord * half + thick])
        base = fan.add_verts([p for ring in rings for p in ring])
        for i in range(len(rings) - 1):
            for j in range(4):
                j2 = (j + 1) % 4
                fan.add_face([base + 4 * i + j, base + 4 * (i + 1) + j, base + 4 * (i + 1) + j2, base + 4 * i + j2],
                             "DarkMetal", None, "blade")
        fan.add_face([base + 4 * (len(rings) - 1) + j for j in range(4)][::-1], "DarkMetal", None, "blade")
    return fan


def build_engine(sign):
    """Returns (static nacelle + pylon, spinning fan, fan hub) for a wing engine."""
    b = MeshBuilder()
    x = sign * ENGINE_X
    hub = _engine_body(b, P(x, INLET_S, ENGINE_Z))
    # pylon: a thin blade from inside the nacelle top, along the core, up into the wing
    le_w = le_s(ENGINE_X)
    sts = []
    for z, le, te in ((ENGINE_Z + 0.8, INLET_S + 2.6, INLET_S + 8.6), (ENGINE_Z + 1.3, INLET_S + 2.2, INLET_S + 9.0),
                      (wing_surface_z(ENGINE_X, le_w + 2.2) + 0.3, le_w + 0.3, le_w + 5.2)):
        sts.append(dict(le=Vector((x, Y(le), z)), chord_dir=Vector((0, 1, 0)), up=Vector((1, 0, 0)), c=te - le, h=None,
                        scale_t=0.42 / ((te - le) * 0.10)))
    lifting_surface(b, sts, "0010", "Body", "pylon")
    return b, _fan(hub), tuple(hub)


# the centre engine: its intake sits above the rear fuselage at the fin root, the S-duct runs down through the
# tail cone to the exhaust. The visible parts are the intake ring with its fan, and a fairing that runs the
# nacelle's top line back into the fin's leading edge and its sides down into the fuselage.
TAIL_INLET_S = 42.0
TAIL_INLET_R = K * 0.90
TAIL_INLET_Z = 7.05 + TAIL_INLET_R - 0.35      # the ring's bottom is buried 0.35 m in the crown fairing


def build_tail_engine():
    """Returns (static intake + fairing, spinning fan, fan hub)."""
    b = MeshBuilder()
    o = P(0, TAIL_INLET_S, TAIL_INLET_Z)
    # intake lining and fan face; the outer skin is one loft below, from the lip back to the tail, so there is
    # no shading seam between the intake and its fairing
    revolve(b, _k([(0.0, 0.90), (0.05, 0.865), (0.25, 0.845), (FAN_DEPTH + 0.05, 0.835)]), o, (0, 1, 0), "Metal",
            "tailintake", segs=44, flip=True)
    revolve(b, _k([(FAN_DEPTH + 0.05, 0.835), (FAN_DEPTH + 0.06, 0.24)]), o, (0, 1, 0), "Dark", "tailfanface", segs=44,
            flip=True)
    hub = Vector(o) + Vector((0, K * L * FAN_DEPTH, 0))
    # outer skin: the nacelle's lip and barrel as full circles, then rings that shrink and sink into the tail
    # cone, so the top line runs back under the fin and the flanks wrap down onto the fuselage shoulders. Where a
    # circle would dip under the skin the ring follows the skin instead.
    n = 44
    sections = [(TAIL_INLET_S + a, r, TAIL_INLET_Z) for a, r in _k([(0.0, 0.90), (0.03, 0.955), (0.10, 0.99), (0.25, 1.02),
                                                                   (0.6, 1.045), (1.3, 1.055)])]
    s0 = sections[-1][0]
    s1 = 55.5
    r0 = K * 1.055
    for s in np.linspace(s0, s1, 18)[1:]:
        t = (s - s0) / (s1 - s0)
        sections.append((s, r0 * (1 - 0.72 * t**1.6) + 0.02,
                         TAIL_INLET_Z + (float(Fuselage.zt(s)) + 0.22 - TAIL_INLET_Z) * t**1.3))
    rings = []
    for s, r, zc in sections:
        ring = []
        for k in range(n):
            a = 2 * math.pi * k / n
            x, z = r * math.cos(a), zc + r * math.sin(a)
            xs = min(abs(x), float(Fuselage.hw(s)) - 0.01)
            skin = float(Fuselage.top_z(s, xs))
            if z < skin + 0.02:
                z = skin - 0.05
            ring.append(P(x, s, z))
        rings.append(ring)
    base = b.add_verts([p for rr in rings for p in rr])
    for i in range(len(rings) - 1):
        for k in range(n):
            k2 = (k + 1) % n
            b.add_face([base + (i + 1) * n + k, base + (i + 1) * n + k2, base + i * n + k2, base + i * n + k], "Body",
                       None, "tailnacelle")
    b.add_face([base + (len(rings) - 1) * n + k for k in range(n)][::-1], "Body", None, "tailnacelle")
    return b, _fan(hub), tuple(hub)


# ============================================================== tail

HT_Z = 6.25             # low-set stabiliser on the rear fuselage
HT_ROOT_LE = 49.6
HT_SPAN = 8.4           # 16.8 m across
HT_SWEEP = math.tan(math.radians(33))
HT_HINGE = 0.70
HT_DIHEDRAL = math.radians(4.0)
EL_ROOT = 2.0


def ht_station(x, h=HT_HINGE, scale_t=1.0):
    ax = abs(x)
    le = HT_ROOT_LE + ax * HT_SWEEP
    c = 6.0 - (ax / HT_SPAN) * 3.9
    return dict(le=Vector((x, Y(le), HT_Z + ax * math.tan(HT_DIHEDRAL))), chord_dir=Vector((0, 1, 0)),
                up=Vector((0, 0, 1)), c=c, h=h, scale_t=scale_t)


def ht_hinge(x):
    st = ht_station(x)
    return st["le"] + st["chord_dir"] * (st["c"] * st["h"])


def build_htail():
    out = {}
    stab = MeshBuilder()
    xs = sorted(set([round(v, 4) for v in np.linspace(-8.15, 8.15, 13)]))
    lifting_surface(stab, [ht_station(x) for x in xs], "0010", "Body", "htail", flip=True)
    for sgn, side in ((1, "L"), (-1, "R")):
        tip = [ht_station(sgn * x, h=None, scale_t=t) for x, t in ((8.12, 1.0), (8.28, 0.85), (HT_SPAN, 0.55))]
        lifting_surface(stab, tip, "0010", "Body", "htail", flip=(sgn > 0))
        el = MeshBuilder()
        lifting_surface(el, [ht_station(sgn * x) for x in np.linspace(EL_ROOT, 8.12, 7)], "0010", "Body", "elevator",
                        part="ctrl", flip=(sgn > 0))
        out["Elevator_" + side] = el
    out["HStab"] = stab
    return out


VT_ROOT_Z = 8.2         # the fin rises out of the centre engine's fairing
VT_TOP_Z = 16.0
VT_HINGE = 0.70
VT_BOTTOM = 6.3         # the fin surface starts inside the tail cone, under the fairing


def vt_le_s(z):
    return 46.4 + (z - VT_ROOT_Z) * math.tan(math.radians(41))


def vt_chord(z):
    return 9.2 - (z - VT_ROOT_Z) / (VT_TOP_Z - VT_ROOT_Z) * (9.2 - 3.3)


def vt_station(z, h=VT_HINGE, scale_t=1.0):
    return dict(le=Vector((0.0, Y(vt_le_s(z)), z)), chord_dir=Vector((0, 1, 0)), up=Vector((1, 0, 0)), c=vt_chord(z), h=h,
                scale_t=scale_t)


def vt_hinge(z):
    st = vt_station(z)
    return st["le"] + st["chord_dir"] * (st["c"] * st["h"])


RUDDER = (8.9, 15.6)


def build_vtail():
    out = {}
    fin = MeshBuilder()
    lifting_surface(fin, [vt_station(z) for z in np.linspace(VT_BOTTOM, 15.62, 11)], "0010", "Body", "vtail")
    caps = [vt_station(z, h=None, scale_t=t) for z, t in ((15.6, 1.0), (15.85, 0.85), (VT_TOP_Z, 0.5))]
    lifting_surface(fin, caps, "0010", "Body", "vtail")
    out["VStab"] = fin
    rud = MeshBuilder()
    lifting_surface(rud, [vt_station(z) for z in np.linspace(RUDDER[0], RUDDER[1], 8)], "0010", "Body", "rudder",
                    part="ctrl")
    out["Rudder"] = rud
    return out


# ============================================================== landing gear

NOSE_R = 0.48
NOSE_AXLE_Z = NOSE_R - 0.02
NOSE_WHEEL_X = 0.36
MAIN_X = 4.7            # leg (main-gear track 9.4 m)
MAIN_WHEEL_DX = 0.62
BOGIE_DS = 0.85
MAIN_R = 0.58
MAIN_AXLE_Z = MAIN_R - 0.03
LEG_S = S_REF - 0.12
MAIN_AXLES = [(1, -BOGIE_DS), (2, BOGIE_DS)]


def build_gear():
    b, nose, out = MeshBuilder(), MeshBuilder(), {}
    for sx, side in ((1, "L"), (-1, "R")):
        x = sx * MAIN_X
        top = wing_surface_z(MAIN_X, LEG_S - 0.1) + 0.2
        pivot_z = MAIN_AXLE_Z + 0.12
        tube(b, [P(x, LEG_S - 0.1, top), P(x, LEG_S, 1.6)], lambda t: 0.22, "Metal", "gearleg", segs=16)
        tube(b, [P(x, LEG_S, 1.66), P(x, LEG_S, pivot_z + 0.1)], lambda t: 0.14, "Chrome", "gearleg", segs=16)
        tube(b, [P(x, S_REF - BOGIE_DS - 0.28, MAIN_AXLE_Z + 0.04), P(x, S_REF + BOGIE_DS + 0.28, MAIN_AXLE_Z + 0.04)],
             lambda t: 0.12, "Metal", "gearleg", segs=12)
        tube(b, [P(x, LEG_S - 0.2, pivot_z + 0.12), P(x, LEG_S + 0.2, pivot_z + 0.12)], lambda t: 0.17, "DarkMetal",
             "gearleg", segs=12)
        for ds in (-BOGIE_DS, BOGIE_DS):
            tube(b, [P(x - sx * (MAIN_WHEEL_DX + 0.05), S_REF + ds, MAIN_AXLE_Z),
                     P(x + sx * (MAIN_WHEEL_DX + 0.05), S_REF + ds, MAIN_AXLE_Z)], lambda t: 0.09, "DarkMetal", "gearleg",
                 segs=10)
        tube(b, [P(x, LEG_S + 0.05, 2.35), P(sx * 2.2, LEG_S + 0.4, 2.75)], lambda t: 0.09, "Metal", "gearleg", segs=10)
        tube(b, [P(x, LEG_S, 1.5), P(x, LEG_S - 0.28, 1.22), P(x, LEG_S - 0.02, 0.98)], lambda t: 0.045, "Metal",
             "gearleg", segs=8)
        for k, ds in MAIN_AXLES:
            w = MeshBuilder()
            for dx in (-MAIN_WHEEL_DX, MAIN_WHEEL_DX):
                wheel(w, P(x + sx * dx, S_REF + ds, MAIN_AXLE_Z), MAIN_R, 0.46, "wheel")
            out[f"Wheel_{side}{k}"] = w
    tube(b, [P(0, NOSE_S - 0.08, float(Fuselage.zb(NOSE_S)) + 0.4), P(0, NOSE_S, 1.5)], lambda t: 0.13, "Metal",
         "nosegear", segs=14)
    tube(nose, [P(0, NOSE_S, 1.56), P(0, NOSE_S, NOSE_AXLE_Z + 0.12)], lambda t: 0.09, "Chrome", "nosegear", segs=14)
    tube(nose, [P(-NOSE_WHEEL_X - 0.05, NOSE_S, NOSE_AXLE_Z), P(NOSE_WHEEL_X + 0.05, NOSE_S, NOSE_AXLE_Z)],
         lambda t: 0.075, "DarkMetal", "nosegear", segs=10)
    tube(nose, [P(0, NOSE_S + 0.03, 1.35), P(0, NOSE_S + 0.26, 1.12), P(0, NOSE_S + 0.04, 0.92)], lambda t: 0.04,
         "Metal", "nosegear", segs=8)
    tube(b, [P(0, NOSE_S - 0.02, 1.85), P(0, NOSE_S - 1.2, float(Fuselage.zb(NOSE_S - 1.2)) + 0.25)], lambda t: 0.065,
         "Metal", "nosegear", segs=10)
    revolve(nose, [(-0.08, 0.001), (-0.06, 0.085), (0.05, 0.09), (0.06, 0.001)], P(0, NOSE_S - 0.15, 1.42), (0, 1, 0),
            "LightLens", "light", segs=12)
    for side, sx in (("L", 1), ("R", -1)):
        w = MeshBuilder()
        wheel(w, P(sx * NOSE_WHEEL_X, NOSE_S, NOSE_AXLE_Z), NOSE_R, 0.33, "wheel")
        out["Wheel_Nose_" + side] = w
    out["Gear"] = b
    out["NoseGear"] = nose
    return out


# ============================================================== lights, antennas, probes

NAV_S = le_s(WING_TIP) + 0.2
BEACON_TOP_S = 27.0
BEACON_BOTTOM_S = 32.0
LANDING = (4.2, 24.4)           # (span, station) under the wing root
LOGO_LIGHT = (3.0, 53.6)        # (span, station) on the stabiliser


def build_details():
    b = MeshBuilder()
    tip_z = wing_station(WING_TIP, h=None)["le"].z
    for sx, mat in ((1, "LightRed"), (-1, "LightGreen")):
        revolve(b, [(-0.12, 0.001), (-0.07, 0.06), (0.06, 0.065), (0.12, 0.001)],
                (sx * (WING_TIP + 0.03), Y(NAV_S), tip_z + 0.03), (0, 1, 0), mat, "light", segs=12)
        revolve(b, [(-0.07, 0.001), (-0.04, 0.045), (0.05, 0.045), (0.07, 0.001)],
                (sx * (WING_TIP + 0.03), Y(NAV_S + 0.4), tip_z + 0.04), (0, 1, 0), "LightWhite", "light", segs=10)
        y, s = LANDING
        revolve(b, [(-0.01, 0.001), (-0.005, 0.14), (0.05, 0.15)], (sx * y, Y(s), wing_surface_z(y, s) - 0.01),
                (0, 0, -1), "LightLens", "light", segs=14, ref=(1, 0, 0))
        x, s = sx * LOGO_LIGHT[0], LOGO_LIGHT[1]
        z = ht_station(x, h=None)["le"].z + 0.11
        revolve(b, [(-0.01, 0.075), (0.04, 0.065), (0.07, 0.001)], P(x, s, z), (0, 0, 1), "LightWhite", "light",
                segs=10, ref=(1, 0, 0))
    s = BEACON_TOP_S
    revolve(b, [(-0.01, 0.12), (0.06, 0.11), (0.12, 0.065), (0.15, 0.001)], P(0, s, float(Fuselage.zt(s)) - 0.02),
            (0, 0, 1), "LightRed", "light", segs=14, ref=(1, 0, 0))
    s = BEACON_BOTTOM_S
    revolve(b, [(-0.01, 0.12), (0.06, 0.11), (0.12, 0.065), (0.15, 0.001)], P(0, s, Fuselage.bottom_z(s, 0) + 0.02),
            (0, 0, -1), "LightRed", "light", segs=14, ref=(1, 0, 0))
    # white tail light on the fin's trailing edge above the rudder
    revolve(b, [(-0.05, 0.001), (-0.03, 0.07), (0.05, 0.07), (0.07, 0.001)], P(0, vt_le_s(15.75) + vt_chord(15.75) - 0.05, 15.75),
            (0, 1, 0), "LightWhite", "light", segs=10)
    for s, up, h in ((13.0, 1, 0.45), (20.0, 1, 0.38), (40.5, -1, 0.32), (10.0, -1, 0.28)):
        z = float(Fuselage.zt(s)) if up > 0 else Fuselage.bottom_z(s, 0)
        tube(b, [P(0, s, z - up * 0.02), P(0, s + 0.3, z + up * h)], lambda t: 0.055 * (1 - 0.6 * t), "Dark", "detail",
             segs=6, squash=0.3)
    for sx, h in ((1, 2.55), (-1, 2.55), (1, 2.1)):
        s = WS_FRONT + 0.4
        z = KEEL + h
        x = sx * Fuselage.side_x(s, z)
        tube(b, [P(x, s, z), P(x + sx * 0.14, s - 0.03, z), P(x + sx * 0.15, s - 0.24, z)], lambda t: 0.015, "Metal",
             "detail", segs=6)
    return b
