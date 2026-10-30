"""Widebody twinjet (A350-900-class proportions), built procedurally.

Stations `s` are metres aft of the nose tip. Blender Y = s - S_REF, so the model origin sits on the ground
under the middle of the main-gear bogies. Nose points -Y, port wing is +X, Z up.
"""
import math

import numpy as np
from mathutils import Vector

from fuselage import Fuselage as BaseFuselage, Loft, profile
from geom import MeshBuilder
from shapes import _fd_normal, drape_pane, lifting_surface, revolve, rounded_outline, tube, wheel

NOSE_S = 6.1            # nose gear axle station
S_REF = NOSE_S + 28.7   # main bogie centre -> model origin (A350-900 wheelbase 28.7 m)


def Y(s):
    return s - S_REF


def P(x, s, z):
    return (x, Y(s), z)


# ============================================================== fuselage

KEEL = 2.40             # fuselage bottom line (constant section)
CROWN = 8.49            # 6.09 m tall, 5.96 m wide
HALF_WIDTH = 2.98
TIP_H = 2.05            # radome tip height above the keel: a third of the fuselage height

# Nose profiles, as on the Narrowbody: each rises from the tip (0) to the constant section (1) as
# 1 - (1 - t^a)^b, t = s / length. Long and full like an A350's: the top line climbs steadily to the windshield
# and levels off ~2 fuselage heights back; the chin rounds off quickly under the radome.
NOSE_UP = (13.0, 0.95, 3.15)    # (length, a, b): side view, tip -> crown
NOSE_LO = (10.0, 0.70, 3.30)    # side view, tip -> keel
NOSE_W = (10.5, 0.78, 3.00)     # plan view, centre line -> full width


def _rise(s, shape):
    length, a, b = shape
    t = min(max(s / length, 0.0), 1.0)
    return 1.0 - (1.0 - t ** a) ** b


def _nose_samples(length):
    """Stations for sampling a nose profile, dense at the tip where it curves fastest."""
    return [0.008] + [length * (1.0 - math.cos(a)) for a in np.linspace(0.06, math.pi / 2, 28)]


class Fuselage(BaseFuselage):
    S0 = 0.008
    S1 = 66.70
    ML = 16
    MU = 16

    # constant section from the nose to s 44, a wing-to-body fairing s 22-44 (lower, wider, boxier belly), then
    # the tail upsweep (tail-strike angle ~10.5 deg about the main gear) to the APU exhaust
    zt = profile([(s, KEEL + TIP_H + (CROWN - KEEL - TIP_H) * _rise(s, NOSE_UP)) for s in _nose_samples(NOSE_UP[0])] +
                 [(44.0, CROWN), (47.0, 8.46), (50.0, 8.36), (53.0, 8.18), (56.0, 7.94), (59.0, 7.66), (62.0, 7.38),
                  (64.5, 7.18), (66.7, 7.02)])
    zb = profile([(s, KEEL + TIP_H * (1.0 - _rise(s, NOSE_LO))) for s in _nose_samples(NOSE_LO[0])] +
                 [(12.0, KEEL), (21.0, KEEL), (23.5, 2.27), (26.5, 2.12), (30.0, 2.05), (35.0, 2.06), (39.0, 2.14),
                  (42.0, 2.30), (44.5, 2.42), (47.5, 2.60), (51.0, 3.00), (54.0, 3.55), (57.0, 4.25), (60.0, 5.00),
                  (63.0, 5.75), (65.5, 6.30), (66.7, 6.55)])
    hw = profile([(s, HALF_WIDTH * _rise(s, NOSE_W)) for s in _nose_samples(NOSE_W[0])] +
                 [(13.0, HALF_WIDTH), (22.0, HALF_WIDTH), (25.0, 3.03), (29.0, 3.08), (37.0, 3.08), (41.0, 3.03),
                  (44.0, HALF_WIDTH), (47.0, 2.95), (50.0, 2.84), (53.0, 2.64), (56.0, 2.34), (59.0, 1.96),
                  (62.0, 1.50), (64.5, 1.02), (66.7, 0.42)])
    fm = profile([(0.008, 0.5), (22.0, 0.5), (25.5, 0.46), (40.0, 0.46), (44.0, 0.5), (66.7, 0.52)])
    nt = profile([(0.008, 2.0), (66.7, 2.0)])
    nb = profile([(0.008, 2.0), (22.0, 2.0), (25.5, 2.8), (40.0, 2.8), (44.0, 2.0), (66.7, 2.0)])


def fuselage_stations():
    s = [0.008, 0.02, 0.04, 0.07, 0.11, 0.16, 0.22, 0.30, 0.40, 0.52, 0.66, 0.82, 1.0]
    for stop, step in ((8.0, 0.25), (14.0, 0.6), (21.0, 1.0), (45.0, 0.6), (66.4, 0.4)):
        while s[-1] < stop - 1e-6:
            s.append(round(s[-1] + step, 4))
    s.append(Fuselage.S1)
    return s


def build_fuselage():
    F = Fuselage
    b = MeshBuilder()
    loft = Loft(F, b, fuselage_stations(), P)
    last = len(loft.st) - 1
    # rounded nose tip and a short tail cone end
    loft.fan_cap(0, P(0, 0.0, KEEL + TIP_H), s_tip=0.0)
    loft.fan_cap(last, P(0, F.S1 + 0.10, float(F.zm(F.S1))), s_tip=F.S1 + 0.1)
    # APU exhaust
    revolve(b, [(-0.06, 0.26), (0.16, 0.26), (0.16, 0.20), (-0.02, 0.20)], P(0, F.S1 + 0.02, float(F.zm(F.S1)) + 0.02),
            (0, 1, 0), "DarkMetal", "apu", segs=20)
    return b


# ============================================================== cockpit windows

def _out_dir(p):
    c = Fuselage.sec(p.y + S_REF)
    return Vector((p.x, 0.0, p.z - c["zm"]))


R_ARC = 2.9     # window outlines are laid out in (station, phi * R_ARC): roughly metres along the skin


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


# Six-pane flight deck (port side; mirrored to starboard), after the A350: a big windshield pane either side of a
# thin centre post, a raked post, a near-rectangular sliding side window, and a smaller rear side window whose top
# edge drops aft. The sill is level, so the windshield's lower edge follows the nose round from the centre line
# (where the top line passes sill height) to the post. Posts are ~0.12 m wide.
SILL = 3.50     # band bottom, metres above the keel
HEAD = 4.52     # band top
WS_FRONT = _top_reaches(SILL)       # windshield bottom on the centre line
WS_TOP = _top_reaches(HEAD)         # windshield top on the centre line
POST = WS_TOP + 0.55                # raked windshield post, at the sill
SIDE_1 = POST + 0.12                # sliding window
SIDE_2 = SIDE_1 + 1.10 + 0.14       # rear side window
COCKPIT_WINDOWS = [
    ("windshield", [_at(WS_FRONT, deg=1.6), _at(WS_FRONT + 0.35, SILL), _at(POST - 0.45, SILL), _at(POST, SILL),
                    _at(POST + 0.30, HEAD), _at(WS_TOP + 0.18, HEAD), _at(WS_TOP, deg=1.6)],
     [0.04, 0.10, 0.40, 0.07, 0.06, 0.40, 0.04]),
    ("sidewindow", [_at(SIDE_1, SILL), _at(SIDE_1 + 1.10, SILL), _at(SIDE_1 + 1.10, HEAD - 0.06), _at(SIDE_1 + 0.42, HEAD)],
     [0.07, 0.16, 0.18, 0.07]),
    ("sidewindow", [_at(SIDE_2, SILL), _at(SIDE_2 + 0.78, SILL + 0.06), _at(SIDE_2 + 0.78, HEAD - 0.40),
                    _at(SIDE_2, HEAD - 0.08)],
     [0.07, 0.14, 0.14, 0.10]),
]


def build_windows():
    b = MeshBuilder()
    for sign in (1, -1):
        surf = skin_surface(sign)
        for tag, corners, radii in COCKPIT_WINDOWS:
            drape_pane(b, _outline(corners, radii), surf, tag=tag, lift=0.01, sink=0.025)
    return b


# ============================================================== wing

WING_SIDE = 2.95        # fuselage side (span station of the wing root)
WING_KINK = 10.4        # trailing-edge kink: lightly swept inboard, swept outboard
WING_TIP = 30.0         # span with the curved winglets: 64.8 m (ICAO code E)
LE_S0 = 25.4            # leading edge station at the fuselage side
LE_TAN = math.tan(math.radians(34.0))
TE_S_IN = 38.8          # trailing edge at the fuselage side
TE_S_KINK = 39.5
TE_S_TIP = 46.55
WING_Z0 = 3.30          # chord-line height of the leading edge at the fuselage side
DIHEDRAL = math.radians(6.0)
WING_HINGE = 0.76       # rear-spar (flap / aileron hinge) as a fraction of the local chord
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
    return float(np.interp(abs(y), [0, WING_SIDE, WING_KINK, WING_TIP], [1.0, 1.0, 0.74, 0.62]))


def wing_station(y, h=WING_HINGE, scale_t=None, c_override=None):
    ay = abs(y)
    sgn = 1.0 if y >= 0 else -1.0
    c = c_override or wing_chord(ay)
    inc = math.radians(2.5 - 3.5 * min(ay / WING_TIP, 1.0))      # 2.5 deg root -> -1 deg tip washout
    chord_dir = Vector((0.0, math.cos(inc), -math.sin(inc)))
    up = Vector((0.0, math.sin(inc), math.cos(inc)))
    le = Vector((sgn * ay, Y(le_s(ay)), wing_z(ay)))
    return dict(le=le, chord_dir=chord_dir, up=up, c=c, h=h, scale_t=wing_thickness(ay) if scale_t is None else scale_t)


def wing_surface_z(y, s, upper=False):
    """z of the wing's lower (or upper) skin at span y, station s."""
    from geom import airfoil_eval
    st = wing_station(y, h=None)
    zu, zl, _ = airfoil_eval(WING_AF)(np.array([(s - le_s(y)) / st["c"]]))
    return float(st["le"].z + (zu[0] if upper else zl[0]) * st["scale_t"] * st["c"])


def hinge_point(y):
    st = wing_station(y)
    return st["le"] + st["chord_dir"] * (st["c"] * st["h"])


# trailing-edge pieces per side: (name, from, to); "filler" pieces are fixed. The inboard flap ends at the kink so
# its hinge stays straight.
TE_PIECES = [("Flap_{}_In", 3.05, 10.30), ("filler", 10.30, 10.50), ("Flap_{}_Out", 10.50, 21.40),
             ("filler", 21.40, 21.60), ("Aileron_{}", 21.60, 28.60)]
TIP_START = 28.65


def build_wing():
    """Returns {"Wing": fixed MeshBuilder, "Flap_L_In": ..., "Aileron_R": ...}."""
    ys = sorted(set([round(v, 4) for v in
                     list(np.linspace(-TIP_START, -WING_KINK, 12)) + list(np.linspace(-WING_KINK, WING_KINK, 11)) +
                     list(np.linspace(WING_KINK, TIP_START, 12)) + [-WING_SIDE, WING_SIDE]]))
    wing = MeshBuilder()
    lifting_surface(wing, [wing_station(y) for y in ys], WING_AF, "Body", "wing", flip=True)
    out = {"Wing": wing}
    for sgn, side in ((1, "L"), (-1, "R")):
        tip = [wing_station(sgn * y, h=None) for y in np.linspace(TIP_START - 0.03, WING_TIP, 5)]
        lifting_surface(wing, tip, WING_AF, "Body", "wing", flip=(sgn > 0))
        for name, y0, y1 in TE_PIECES:
            yy = np.linspace(y0, y1, max(int((y1 - y0) / 1.4) + 2, 2))
            target = wing if name == "filler" else out.setdefault(name.format(side), MeshBuilder())
            tag = "wing_filler" if name == "filler" else "wing_" + name.split("_")[0].lower()
            lifting_surface(target, [wing_station(sgn * y) for y in yy], WING_AF, "Body", tag, part="ctrl",
                            flip=(sgn > 0))
    return out


def build_winglets():
    """Curved blended winglets, A350-style: the tip section bends up through a long, wide arc, then carries on as
    a short, highly swept blade leaning 18 deg outboard, tapering to a narrow tip."""
    b = MeshBuilder()
    tip = wing_station(WING_TIP, h=None)
    z0 = tip["le"].z
    R, lean = 1.65, math.radians(72)
    path = []
    for phi in np.linspace(0, lean, 10):
        path.append((R * math.sin(phi), R * (1 - math.cos(phi)), phi))
    x1, z1 = path[-1][0], path[-1][1]
    for d in (0.5, 1.0, 1.5, 2.0, 2.45):
        path.append((x1 + d * math.cos(lean), z1 + d * math.sin(lean), lean))
    seg = [0.0] + [math.hypot(path[k][0] - path[k - 1][0], path[k][1] - path[k - 1][1]) for k in range(1, len(path))]
    u = np.cumsum(seg) / sum(seg)
    for sgn in (1, -1):
        sts = []
        for (dx, dz, phi), t in zip(path, u):
            c = tip["c"] - (tip["c"] - 0.62) * t**0.75
            le_shift = 2.55 * t**1.25
            up = Vector((-sgn * math.sin(phi), 0.0, math.cos(phi)))
            sts.append(dict(le=Vector((sgn * (WING_TIP + dx), Y(le_s(WING_TIP) + le_shift), z0 + dz)),
                            chord_dir=Vector((0, 1, 0)), up=up, c=c, h=None, scale_t=tip["scale_t"] * (1 - 0.35 * t)))
        lifting_surface(b, sts, WING_AF, "Body", "winglet", flip=(sgn > 0))
    return b


# ============================================================== engines

K = 1.62                # the Narrowbody's nacelle scaled up: fan ~2.9 m, nacelle 3.4 m across, 6.1 m long
ENGINE_X = 9.70
ENGINE_Z = 2.65
INLET_S = 23.6
NACELLE = [(0.0, 0.90), (0.03, 0.955), (0.10, 0.99), (0.25, 1.02), (0.6, 1.045), (1.3, 1.055), (2.0, 1.04),
           (2.7, 0.99), (3.3, 0.90), (3.75, 0.80)]


def _k(pts):
    return [(K * a, K * r) for a, r in pts]


def build_engine(sign):
    """Returns (static nacelle + pylon, spinning fan, fan hub)."""
    b = MeshBuilder()
    x = sign * ENGINE_X
    o = P(x, INLET_S, ENGINE_Z)
    revolve(b, _k(NACELLE), o, (0, 1, 0), "Body", "nacelle", segs=48)
    # intake lining down to the fan face, and the fan-duct exit behind the cowl
    revolve(b, _k([(0.0, 0.90), (0.05, 0.865), (0.25, 0.845), (0.85, 0.835)]), o, (0, 1, 0), "Metal", "intake",
            segs=48, flip=True)
    revolve(b, _k([(0.85, 0.835), (0.86, 0.24)]), o, (0, 1, 0), "Dark", "fanface", segs=48, flip=True)
    revolve(b, _k([(3.75, 0.80), (3.70, 0.74), (3.45, 0.70)]), o, (0, 1, 0), "DarkMetal", "fanduct", segs=48, flip=True)
    # core cowl, core nozzle and plug
    revolve(b, _k([(3.45, 0.70), (3.8, 0.66), (4.3, 0.58), (4.65, 0.50)]), o, (0, 1, 0), "Metal", "core", segs=36)
    revolve(b, _k([(4.65, 0.50), (4.62, 0.44), (4.40, 0.42)]), o, (0, 1, 0), "DarkMetal", "core", segs=36, flip=True)
    revolve(b, _k([(4.40, 0.40), (4.75, 0.33), (5.05, 0.18), (5.20, 0.02)]), o, (0, 1, 0), "DarkMetal", "plug", segs=28)
    # pylon: a thin blade from inside the nacelle top, along the core, up into the wing
    sts = []
    le_w = le_s(ENGINE_X)
    for z, le, te in ((ENGINE_Z + 0.9, INLET_S + 2.4, INLET_S + 8.9), (ENGINE_Z + 1.55, INLET_S + 1.95, INLET_S + 9.5),
                      (wing_surface_z(ENGINE_X, le_w + 2.5) + 0.35, le_w + 0.35, le_w + 5.6)):
        sts.append(dict(le=Vector((x, Y(le), z)), chord_dir=Vector((0, 1, 0)), up=Vector((1, 0, 0)), c=te - le, h=None,
                        scale_t=0.50 / ((te - le) * 0.10)))
    lifting_surface(b, sts, "0010", "Body", "pylon")

    fan = MeshBuilder()
    hub = Vector(P(x, INLET_S + K * 0.8, ENGINE_Z))
    prof = [(0.38 + 0.42 * (k / 10), 0.25 * (1 - (1 - k / 10) ** 2.2) ** 0.5 + 0.001) for k in range(11)]
    revolve(fan, [(K * (t - 0.8), K * r) for t, r in prof], hub, (0, 1, 0), "Metal", "spinner", segs=28)
    n_blades = 22
    for k in range(n_blades):
        a0 = 2 * math.pi * k / n_blades
        radial = Vector((math.cos(a0), 0, math.sin(a0)))
        tangential = Vector((-math.sin(a0), 0, math.cos(a0)))
        axial = Vector((0, 1, 0))
        rings = []
        for r in np.linspace(0.24, 0.83, 6):
            twist = math.radians(55 - 30 * (r - 0.24) / 0.59)
            half = K * (0.065 + 0.03 * (r - 0.24) / 0.59)
            chord = tangential * math.cos(twist) + axial * math.sin(twist)
            thick = chord.cross(radial).normalized() * 0.011
            c = hub + radial * (K * r)
            rings.append([c - chord * half - thick, c + chord * half - thick, c + chord * half + thick, c - chord * half + thick])
        base = fan.add_verts([p for ring in rings for p in ring])
        for i in range(len(rings) - 1):
            for j in range(4):
                j2 = (j + 1) % 4
                fan.add_face([base + 4 * i + j, base + 4 * (i + 1) + j, base + 4 * (i + 1) + j2, base + 4 * i + j2],
                             "DarkMetal", None, "blade")
        fan.add_face([base + 4 * (len(rings) - 1) + j for j in range(4)][::-1], "DarkMetal", None, "blade")
    return b, fan, tuple(hub)


# ============================================================== tail

HT_Z = 6.35
HT_ROOT_LE = 55.4
HT_SPAN = 9.6           # 19.2 m across
HT_SWEEP = math.tan(math.radians(34))
HT_HINGE = 0.70
HT_DIHEDRAL = math.radians(5.0)
EL_ROOT = 1.95          # elevator inboard end, clear of the tail cone


def ht_station(x, h=HT_HINGE, scale_t=1.0):
    ax = abs(x)
    le = HT_ROOT_LE + ax * HT_SWEEP
    c = 7.2 - (ax / HT_SPAN) * 4.9
    return dict(le=Vector((x, Y(le), HT_Z + ax * math.tan(HT_DIHEDRAL))), chord_dir=Vector((0, 1, 0)),
                up=Vector((0, 0, 1)), c=c, h=h, scale_t=scale_t)


def ht_hinge(x):
    st = ht_station(x)
    return st["le"] + st["chord_dir"] * (st["c"] * st["h"])


def build_htail():
    out = {}
    stab = MeshBuilder()
    xs = sorted(set([round(v, 4) for v in np.linspace(-9.35, 9.35, 15)]))
    lifting_surface(stab, [ht_station(x) for x in xs], "0010", "Body", "htail", flip=True)
    for sgn, side in ((1, "L"), (-1, "R")):
        tip = [ht_station(sgn * x, h=None, scale_t=t) for x, t in ((9.32, 1.0), (9.48, 0.85), (9.6, 0.55))]
        lifting_surface(stab, tip, "0010", "Body", "htail", flip=(sgn > 0))
        el = MeshBuilder()
        lifting_surface(el, [ht_station(sgn * x) for x in np.linspace(EL_ROOT, 9.32, 7)], "0010", "Body", "elevator",
                        part="ctrl", flip=(sgn > 0))
        out["Elevator_" + side] = el
    out["HStab"] = stab
    return out


VT_ROOT_Z = 8.0
VT_TOP_Z = 17.0
VT_HINGE = 0.70
VT_BOTTOM = 7.35        # the fin surface starts inside the tail cone


def vt_le_s(z):
    return 50.6 + (z - VT_ROOT_Z) * math.tan(math.radians(40))


def vt_chord(z):
    return 10.0 - (z - VT_ROOT_Z) / (VT_TOP_Z - VT_ROOT_Z) * (10.0 - 3.4)


def vt_station(z, h=VT_HINGE, scale_t=1.0):
    c = vt_chord(z)
    return dict(le=Vector((0.0, Y(vt_le_s(z)), z)), chord_dir=Vector((0, 1, 0)), up=Vector((1, 0, 0)), c=c, h=h,
                scale_t=scale_t)


def vt_hinge(z):
    st = vt_station(z)
    return st["le"] + st["chord_dir"] * (st["c"] * st["h"])


RUDDER = (8.35, 16.6)


def build_vtail():
    out = {}
    fin = MeshBuilder()
    lifting_surface(fin, [vt_station(z) for z in np.linspace(VT_BOTTOM, 16.62, 11)], "0010", "Body", "vtail")
    caps = [vt_station(z, h=None, scale_t=t) for z, t in ((16.6, 1.0), (16.85, 0.85), (17.0, 0.5))]
    lifting_surface(fin, caps, "0010", "Body", "vtail")
    # dorsal fillet blending the fin root into the fuselage crown
    s0, s1 = 45.5, 51.75     # ends just inside the fin's leading edge at its top (z 9.2, s 51.6)
    rings = []
    for s in np.linspace(s0, s1, 12):
        t = (s - s0) / (s1 - s0)
        zt = float(Fuselage.zt(s))
        ztop = zt + 0.01 + (VT_ROOT_Z + 1.2 - zt) * t**2.2
        wb = 0.16 + 0.34 * math.sin(math.pi * t) ** 0.6
        wt = 0.03 + 0.16 * t
        port = []
        for h in (0.0, 0.25, 0.55, 0.8, 1.0):
            px = wb * (1 - h) ** 2.4 + wt * h * (1 - h * 0.35)
            port.append((px, Fuselage.top_z(s, wb) - 0.04 + (ztop - Fuselage.top_z(s, wb) + 0.04) * h))
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
    lifting_surface(rud, [vt_station(z) for z in np.linspace(RUDDER[0], RUDDER[1], 8)], "0010", "Body", "rudder",
                    part="ctrl")
    out["Rudder"] = rud
    return out


# ============================================================== landing gear

NOSE_R = 0.53
NOSE_AXLE_Z = NOSE_R - 0.02
NOSE_WHEEL_X = 0.40
MAIN_X = 5.3            # leg (main-gear track 10.6 m)
MAIN_WHEEL_DX = 0.72    # wheel offset from the leg
BOGIE_DS = 1.0          # axle offset fore and aft of the bogie pivot
MAIN_R = 0.64
MAIN_AXLE_Z = MAIN_R - 0.03
LEG_S = S_REF - 0.15    # the shock strut meets the bogie just ahead of its middle

# four-wheel bogies: each axle (k = 1 front, 2 rear) carries two wheels that roll together, so it is one part
MAIN_AXLES = [(1, -BOGIE_DS), (2, BOGIE_DS)]


def build_gear():
    """Returns {"Gear": fixed legs + bogies, "NoseGear": steering part, "Wheel_*": spinning wheels (main gear: one
    per axle)}."""
    b, nose, out = MeshBuilder(), MeshBuilder(), {}
    for sx, side in ((1, "L"), (-1, "R")):
        x = sx * MAIN_X
        top = wing_surface_z(MAIN_X, LEG_S - 0.1) + 0.2
        pivot_z = MAIN_AXLE_Z + 0.12
        tube(b, [P(x, LEG_S - 0.1, top), P(x, LEG_S, 1.75)], lambda t: 0.25, "Metal", "gearleg", segs=16)
        tube(b, [P(x, LEG_S, 1.82), P(x, LEG_S, pivot_z + 0.1)], lambda t: 0.16, "Chrome", "gearleg", segs=16)
        # bogie beam, the two axles, a pitch damper from the leg to the front of the beam
        tube(b, [P(x, S_REF - BOGIE_DS - 0.3, MAIN_AXLE_Z + 0.04), P(x, S_REF + BOGIE_DS + 0.3, MAIN_AXLE_Z + 0.04)],
             lambda t: 0.13, "Metal", "gearleg", segs=12)
        tube(b, [P(x, LEG_S - 0.2, pivot_z + 0.12), P(x, LEG_S + 0.2, pivot_z + 0.12)], lambda t: 0.19, "DarkMetal",
             "gearleg", segs=12)
        for ds in (-BOGIE_DS, BOGIE_DS):
            tube(b, [P(x - sx * (MAIN_WHEEL_DX + 0.05), S_REF + ds, MAIN_AXLE_Z),
                     P(x + sx * (MAIN_WHEEL_DX + 0.05), S_REF + ds, MAIN_AXLE_Z)], lambda t: 0.10, "DarkMetal", "gearleg",
                 segs=10)
        tube(b, [P(x + sx * 0.2, LEG_S - 0.05, 1.45), P(x + sx * 0.2, S_REF - BOGIE_DS + 0.25, MAIN_AXLE_Z + 0.15)],
             lambda t: 0.06, "Metal", "gearleg", segs=8)
        # side brace up to the fuselage and the torque link
        tube(b, [P(x, LEG_S + 0.05, 2.55), P(sx * 2.55, LEG_S + 0.4, 3.05)], lambda t: 0.10, "Metal", "gearleg", segs=10)
        tube(b, [P(x, LEG_S, 1.65), P(x, LEG_S - 0.3, 1.35), P(x, LEG_S - 0.02, 1.08)], lambda t: 0.05, "Metal",
             "gearleg", segs=8)
        for k, ds in MAIN_AXLES:
            w = MeshBuilder()
            for dx in (-MAIN_WHEEL_DX, MAIN_WHEEL_DX):
                wheel(w, P(x + sx * dx, S_REF + ds, MAIN_AXLE_Z), MAIN_R, 0.50, "wheel")
            out[f"Wheel_{side}{k}"] = w
    # nose gear: the upper strut is fixed, everything below the steering collar turns
    tube(b, [P(0, NOSE_S - 0.08, float(Fuselage.zb(NOSE_S)) + 0.4), P(0, NOSE_S, 1.62)], lambda t: 0.14, "Metal",
         "nosegear", segs=14)
    tube(nose, [P(0, NOSE_S, 1.68), P(0, NOSE_S, NOSE_AXLE_Z + 0.12)], lambda t: 0.10, "Chrome", "nosegear", segs=14)
    tube(nose, [P(-NOSE_WHEEL_X - 0.05, NOSE_S, NOSE_AXLE_Z), P(NOSE_WHEEL_X + 0.05, NOSE_S, NOSE_AXLE_Z)],
         lambda t: 0.08, "DarkMetal", "nosegear", segs=10)
    tube(nose, [P(0, NOSE_S + 0.03, 1.45), P(0, NOSE_S + 0.28, 1.20), P(0, NOSE_S + 0.04, 0.98)], lambda t: 0.04,
         "Metal", "nosegear", segs=8)
    # drag brace forwards into the gear bay
    tube(b, [P(0, NOSE_S - 0.02, 1.95), P(0, NOSE_S - 1.3, float(Fuselage.zb(NOSE_S - 1.3)) + 0.25)], lambda t: 0.07,
         "Metal", "nosegear", segs=10)
    # taxi / turn-off light housing on the strut
    revolve(nose, [(-0.08, 0.001), (-0.06, 0.09), (0.05, 0.095), (0.06, 0.001)], P(0, NOSE_S - 0.16, 1.52), (0, 1, 0),
            "LightLens", "light", segs=12)
    for side, sx in (("L", 1), ("R", -1)):
        w = MeshBuilder()
        wheel(w, P(sx * NOSE_WHEEL_X, NOSE_S, NOSE_AXLE_Z), NOSE_R, 0.36, "wheel")
        out["Wheel_Nose_" + side] = w
    out["Gear"] = b
    out["NoseGear"] = nose
    return out


# ============================================================== lights, antennas, probes

NAV_S = le_s(WING_TIP) + 0.2
BEACON_TOP_S = 31.0
BEACON_BOTTOM_S = 38.0
LANDING = (4.6, 28.6)           # (span, station) under the wing root
LOGO_LIGHT = (3.4, 59.4)        # (span, station) on the stabiliser


def build_details():
    b = MeshBuilder()
    tip_z = wing_station(WING_TIP, h=None)["le"].z
    for sx, mat in ((1, "LightRed"), (-1, "LightGreen")):
        # nav light in the leading edge of the wingtip, strobe behind it
        revolve(b, [(-0.13, 0.001), (-0.08, 0.065), (0.06, 0.07), (0.13, 0.001)],
                (sx * (WING_TIP + 0.03), Y(NAV_S), tip_z + 0.03), (0, 1, 0), mat, "light", segs=12)
        revolve(b, [(-0.08, 0.001), (-0.04, 0.05), (0.05, 0.05), (0.08, 0.001)],
                (sx * (WING_TIP + 0.03), Y(NAV_S + 0.42), tip_z + 0.04), (0, 1, 0), "LightWhite", "light", segs=10)
        # landing light under the wing root
        y, s = LANDING
        revolve(b, [(-0.01, 0.001), (-0.005, 0.15), (0.05, 0.16)], (sx * y, Y(s), wing_surface_z(y, s) - 0.01),
                (0, 0, -1), "LightLens", "light", segs=14, ref=(1, 0, 0))
        # logo light on the stabiliser, aimed at the fin
        x, s = sx * LOGO_LIGHT[0], LOGO_LIGHT[1]
        z = ht_station(x, h=None)["le"].z + 0.12
        revolve(b, [(-0.01, 0.08), (0.04, 0.07), (0.07, 0.001)], P(x, s, z), (0, 0, 1), "LightWhite", "light",
                segs=10, ref=(1, 0, 0))
    # beacons on the crown and belly, white tail light
    s = BEACON_TOP_S
    revolve(b, [(-0.01, 0.13), (0.06, 0.12), (0.13, 0.07), (0.16, 0.001)], P(0, s, float(Fuselage.zt(s)) - 0.02),
            (0, 0, 1), "LightRed", "light", segs=14, ref=(1, 0, 0))
    s = BEACON_BOTTOM_S
    revolve(b, [(-0.01, 0.13), (0.06, 0.12), (0.13, 0.07), (0.16, 0.001)], P(0, s, Fuselage.bottom_z(s, 0) + 0.02),
            (0, 0, -1), "LightRed", "light", segs=14, ref=(1, 0, 0))
    zt = float(Fuselage.zm(Fuselage.S1))
    revolve(b, [(-0.05, 0.001), (-0.03, 0.08), (0.05, 0.08), (0.08, 0.001)], P(0, Fuselage.S1 + 0.02, zt + 0.32),
            (0, 1, 0), "LightWhite", "light", segs=10)
    # antennas on the crown and belly (satcom fairing on top)
    for s, up, h in ((14.5, 1, 0.48), (24.0, 1, 0.40), (46.5, -1, 0.34), (11.0, -1, 0.30)):
        z = float(Fuselage.zt(s)) if up > 0 else Fuselage.bottom_z(s, 0)
        tube(b, [P(0, s, z - up * 0.02), P(0, s + 0.32, z + up * h)], lambda t: 0.06 * (1 - 0.6 * t), "Dark", "detail",
             segs=6, squash=0.3)
    s0, s1 = 27.0, 30.4
    sat = []
    for s in np.linspace(s0, s1, 9):
        t = (s - s0) / (s1 - s0)
        sat.append((s, 0.34 * math.sin(math.pi * t) ** 0.5 + 0.001))
    zc = float(Fuselage.zt(28.7)) - 0.18
    revolve(b, [(s - s0, r) for s, r in sat], P(0, s0, zc), (0, 1, 0), "Body", "satcom", segs=20)
    # pitot probes on the nose
    for sx, h in ((1, 2.95), (-1, 2.95), (1, 2.45)):
        s = WS_FRONT + 0.4
        z = KEEL + h
        x = sx * Fuselage.side_x(s, z)
        tube(b, [P(x, s, z), P(x + sx * 0.15, s - 0.03, z), P(x + sx * 0.16, s - 0.26, z)], lambda t: 0.016, "Metal",
             "detail", segs=6)
    return b
