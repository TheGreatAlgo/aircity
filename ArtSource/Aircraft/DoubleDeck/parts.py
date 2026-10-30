"""Double-deck quadjet, built procedurally. Proportions and figures follow the Airbus A380-800 "Aircraft
Characteristics - Airport and Maintenance Planning" document (rev. Dec 2025): the side, plan and front views
of its general-dimensions sheet were traced into the profiles below (heights above the ground at maximum ramp
weight, forward CG).

Stations `s` are metres aft of the nose tip. Blender Y = s - S_REF, so the model origin sits on the ground
under the effective main-gear centre (the turn centre of the A380's turning-radii table). Nose points -Y,
port wing is +X, Z up.
"""
import math

import numpy as np
from mathutils import Vector

from fuselage import Fuselage as BaseFuselage, Loft, profile
from geom import MeshBuilder, resample_polyline, smoothstep
from shapes import _fd_normal, drape_pane, lifting_surface, revolve, rounded_outline, tube, wheel

NOSE_S = 4.97           # nose gear axle station (A380: 4.97 m)
S_REF = 35.55           # turn centre: NLG radius 32.66 m at 69.5 deg effective steering -> wheelbase 30.58 m


def Y(s):
    return s - S_REF


def P(x, s, z):
    return (x, Y(s), z)


# ============================================================== fuselage

KEEL = 2.38             # keel of the constant section (7.14 m wide x 8.41 m tall)
CROWN = 10.79
HALF_WIDTH = 3.57
TIP_Z = 4.78            # radome tip, just under the main-deck floor
ZM = 6.33               # widest line of the constant section (main-deck window belt)

# Traced from the side / plan views (A380 general dimensions sheet), heights above the ground. The top line was
# smoothed through the windshield frame, the keel scaled to the 8.41 m section height.
TOP = [(0.008, 4.90), (0.03, 5.01), (0.08, 5.15), (0.2, 5.34), (0.3, 5.45), (0.5, 5.75), (0.75, 6.06), (1.0, 6.33),
       (1.5, 6.79), (2.0, 7.21), (2.5, 7.60), (3.0, 7.97), (3.5, 8.33), (4.0, 8.66), (4.5, 8.96), (5.0, 9.22),
       (6.0, 9.65), (7.0, 9.96), (8.0, 10.19), (9.0, 10.37), (10.0, 10.51), (11.0, 10.62), (12.0, 10.70),
       (13.0, 10.755), (14.5, CROWN), (52.0, CROWN), (54.0, 10.72), (56.0, 10.56), (58.0, 10.35), (60.0, 10.13),
       (62.0, 9.90), (64.0, 9.72), (66.0, 9.55), (68.0, 9.30), (69.0, 9.17), (70.0, 9.03), (70.3, 8.98)]
BOTTOM = [(0.008, 4.67), (0.03, 4.57), (0.08, 4.45), (0.2, 4.28), (0.3, 4.18), (0.5, 3.98), (0.75, 3.80),
          (1.0, 3.64), (1.5, 3.44), (2.0, 3.26), (2.5, 3.13), (3.0, 3.03), (3.5, 2.95), (4.0, 2.88), (4.5, 2.81),
          (5.0, 2.76), (6.0, 2.67), (7.0, 2.59), (8.0, 2.53), (9.0, 2.48), (10.0, 2.44), (11.0, 2.41), (12.5, 2.39),
          (14.0, KEEL), (17.0, KEEL),
          # wing-to-body fairing: 1.85 m at its lowest (A380 belly fairing BF1 1.82 m)
          (20.0, 2.30), (23.0, 2.10), (26.0, 1.93), (29.0, 1.86), (33.0, 1.85), (37.0, 1.88), (40.0, 1.98),
          (43.0, 2.15), (46.0, 2.40), (48.0, 2.58),
          # tail upsweep to the APU exhaust
          (50.0, 2.83), (52.0, 3.18), (54.0, 3.57), (56.0, 3.93), (58.0, 4.39), (60.0, 4.81), (62.0, 5.24),
          (64.0, 5.73), (66.0, 6.30), (68.0, 6.86), (69.0, 7.18), (70.0, 7.46), (70.3, 7.55)]
HALF = [(0.008, 0.15), (0.03, 0.29), (0.08, 0.47), (0.2, 0.73), (0.3, 0.81), (0.4, 0.94), (0.5, 1.04), (0.65, 1.16),
        (0.8, 1.30), (1.0, 1.40), (1.25, 1.54), (1.5, 1.68), (1.75, 1.79), (2.0, 1.89), (2.5, 2.10), (3.0, 2.25),
        (3.5, 2.39), (4.0, 2.51), (4.5, 2.63), (5.0, 2.74), (5.5, 2.84), (6.0, 2.93), (7.0, 3.10), (8.0, 3.22),
        (9.0, 3.33), (10.0, 3.41), (11.0, 3.48), (12.0, 3.535), (13.0, 3.565), (14.0, HALF_WIDTH),
        (50.0, HALF_WIDTH), (52.0, 3.48), (54.0, 3.28), (56.0, 3.03), (58.0, 2.58), (60.0, 2.24), (62.0, 1.90),
        (64.0, 1.57), (66.0, 1.25), (68.0, 0.95), (69.0, 0.68), (70.0, 0.36), (70.3, 0.30)]
# height of the widest line: the radome tip, climbing to the main-deck window belt; the tail cone's middle
WIDEST = [(0.008, TIP_Z), (1.0, 4.90), (2.0, 5.12), (4.0, 5.60), (6.0, 5.92), (8.0, 6.13), (11.0, 6.27), (14.0, ZM),
          (47.0, ZM), (52.0, 6.55), (56.0, 7.05), (60.0, 7.45), (64.0, 7.75), (68.0, 8.05), (70.3, 8.27)]

# The belly fairing is far wider than the fuselage's lower lobe (4.35 m half width over a flat bottom, A380
# front view), so it is a box-like superellipse merged into the lower lobe with a smooth max.
FAIRING = (14.5, 21.0, 41.0, 47.0)      # ramps in from s 14.5 to 21, out from 41 to 47
FAIRING_HW = 4.35
FAIRING_TOP = 5.2
FAIRING_N = 4.0


def _fairing_weight(s):
    a, b, c, d = FAIRING
    return float(smoothstep(a, b, s) * (1.0 - smoothstep(c, d, s)))


def _smax(a, b, k=0.30):
    return 0.5 * (a + b + np.sqrt((a - b) ** 2 + k * k))


def _with_fairing(s, x, z, zb):
    """Half width at height z once the fairing is merged in (x = the plain fuselage's half width)."""
    w = _fairing_weight(s)
    if w <= 0.0:
        return x
    zc, h = 0.5 * (zb + FAIRING_TOP), 0.5 * (FAIRING_TOP - zb)
    q = np.clip(np.abs(z - zc) / h, 0.0, 1.0)
    xf = FAIRING_HW * np.maximum(1.0 - q ** FAIRING_N, 0.0) ** (1.0 / FAIRING_N)
    xf = np.where(z < FAIRING_TOP, xf, 0.0)
    return x + w * (np.maximum(_smax(x, xf), x) - x)


def _fm_profile():
    """fm (widest line as a fraction of the height) from the WIDEST heights."""
    zt, zb, zm = profile(TOP), profile(BOTTOM), profile(WIDEST)
    grid = sorted(set(np.round(np.concatenate([np.linspace(0.008, 1.0, 12), np.linspace(1.0, 70.3, 140)]), 4)))
    return profile([(s, (float(zm(s)) - float(zb(s))) / (float(zt(s)) - float(zb(s)))) for s in grid])


class Fuselage(BaseFuselage):
    S0 = 0.008
    S1 = 70.3
    ML = 20
    MU = 18
    zt = profile(TOP)
    zb = profile(BOTTOM)
    hw = profile(HALF)
    fm = _fm_profile()
    nt = profile([(0.008, 2.0), (6.0, 2.1), (14.0, 2.2), (50.0, 2.2), (60.0, 2.1), (70.3, 2.0)])
    nb = profile([(0.008, 2.0), (14.0, 2.1), (19.0, 2.1), (25.0, 2.5), (40.0, 2.5), (47.0, 2.1), (56.0, 2.0),
                  (70.3, 2.0)])

    @classmethod
    def lower_quarter(cls, s, count):
        """Port lower quarter (widest line -> bottom centre) with the belly fairing merged in."""
        c = cls.sec(s)
        n = c["nb"]
        t = np.linspace(0.0, math.pi / 2, 1200)
        x = c["hw"] * np.cos(t) ** (2.0 / n)
        z = c["zm"] - (c["zm"] - c["zb"]) * np.sin(t) ** (2.0 / n)
        x = _with_fairing(s, x, z, c["zb"])
        x[-1] = 0.0
        return resample_polyline(np.stack([x, z], 1), count)

    @classmethod
    def port_profile(cls, s):
        lo, alo = cls.lower_quarter(s, cls.ML + 1)
        up, aup = cls.quarter(s, True, cls.MU + 1)
        pts = np.concatenate([lo[::-1], up[1:]])
        a = np.concatenate([-alo[::-1], aup[1:]])
        return pts, a

    @classmethod
    def side_x(cls, s, z):
        x = BaseFuselage.side_x.__func__(cls, s, z)
        c = cls.sec(s)
        if z >= c["zm"]:
            return x
        return float(_with_fairing(s, np.array(x), np.array(z), c["zb"]))


def fuselage_stations():
    s = [0.008, 0.02, 0.04, 0.07, 0.11, 0.16, 0.22, 0.30, 0.40, 0.52, 0.66, 0.82, 1.0]
    for stop, step in ((8.0, 0.25), (14.0, 0.5), (49.0, 0.6), (70.1, 0.4)):
        while s[-1] < stop - 1e-6:
            s.append(round(s[-1] + step, 4))
    s.append(Fuselage.S1)
    return s


def build_fuselage():
    F = Fuselage
    b = MeshBuilder()
    loft = Loft(F, b, fuselage_stations(), P)
    last = len(loft.st) - 1
    loft.fan_cap(0, P(0, 0.0, TIP_Z), s_tip=0.0)
    loft.fan_cap(last, P(0, F.S1 + 0.08, float(F.zm(F.S1))), s_tip=F.S1 + 0.08)
    # APU exhaust
    revolve(b, [(-0.06, 0.24), (0.14, 0.24), (0.14, 0.18), (-0.02, 0.18)], P(0, F.S1 + 0.02, float(F.zm(F.S1))),
            (0, 1, 0), "DarkMetal", "apu", segs=20)
    return b


# ============================================================== cockpit windows

def _out_dir(p):
    c = Fuselage.sec(p.y + S_REF)
    return Vector((p.x, 0.0, p.z - c["zm"]))


R_ARC = 3.6     # window outlines are laid out in (station, phi * R_ARC): roughly metres along the skin


def skin_surface(sign):
    def fn(s, v):
        x, z = Fuselage.ring_point(s, v / R_ARC)
        return P(sign * x, s, z)
    return lambda s, v: _fd_normal(fn, s, v, _out_dir)


def _outline(corners, radii):
    return rounded_outline([(s, math.radians(d) * R_ARC) for s, d in corners], radii)


def _phi(s, h):
    """Degrees from the top centre to the port skin at station s, height h above the keel."""
    c = Fuselage.sec(s)
    z = KEEL + h
    return math.degrees(math.atan2(BaseFuselage.side_x.__func__(Fuselage, s, z), z - c["zm"]))


def _at(s, h=None, deg=None):
    return (s, deg if deg is not None else _phi(s, h))


def _top_reaches(h):
    """Station where the nose top line climbs through height h above the keel."""
    lo, hi = 0.01, 14.0
    for _ in range(50):
        mid = 0.5 * (lo + hi)
        lo, hi = (mid, hi) if float(Fuselage.zt(mid)) < KEEL + h else (lo, mid)
    return 0.5 * (lo + hi)


# Six-pane flight deck between the decks (pilot eye 7.17 m above the ground, A380 visibility sheet): a wide
# windshield pane either side of a thin centre post, a raked post, a sliding side window and a smaller rear
# window whose top edge drops aft. The band runs from SILL 6.75 m to HEAD 7.48 m above the ground.
SILL = 6.75 - KEEL
HEAD = 7.48 - KEEL
WS_FRONT = _top_reaches(SILL)
WS_TOP = _top_reaches(HEAD)
POST = WS_TOP + 0.45
SIDE_1 = POST + 0.12
SIDE_1_L = 0.78
SIDE_2 = SIDE_1 + SIDE_1_L + 0.13
SIDE_2_L = 0.62
COCKPIT_WINDOWS = [
    ("windshield", [_at(WS_FRONT, deg=1.4), _at(WS_FRONT + 0.30, SILL), _at(POST - 0.40, SILL), _at(POST, SILL),
                    _at(POST + 0.26, HEAD), _at(WS_TOP + 0.16, HEAD), _at(WS_TOP, deg=1.4)],
     [0.04, 0.10, 0.40, 0.07, 0.06, 0.40, 0.04]),
    ("sidewindow", [_at(SIDE_1, SILL), _at(SIDE_1 + SIDE_1_L, SILL), _at(SIDE_1 + SIDE_1_L, HEAD - 0.05),
                    _at(SIDE_1 + 0.30, HEAD)],
     [0.07, 0.15, 0.17, 0.07]),
    ("sidewindow", [_at(SIDE_2, SILL), _at(SIDE_2 + SIDE_2_L, SILL + 0.05), _at(SIDE_2 + SIDE_2_L, HEAD - 0.32),
                    _at(SIDE_2, HEAD - 0.07)],
     [0.07, 0.13, 0.13, 0.09]),
]


def build_windows():
    b = MeshBuilder()
    for sign in (1, -1):
        surf = skin_surface(sign)
        for tag, corners, radii in COCKPIT_WINDOWS:
            drape_pane(b, _outline(corners, radii), surf, tag=tag, lift=0.01, sink=0.025)
    return b


# ============================================================== wing

# Planform traced from the plan view: leading edge 19.4 m aft of the nose at the fuselage side, ~37 deg swept;
# trailing edge kink at 11.5 m span; tip chord 3.8 m at 39.6 m (79.75 m over the wingtip fences).
WING_SIDE = HALF_WIDTH
WING_KINK = 11.5
WING_TIP = 39.70           # fence outer face at 39.87 m: 79.75 m span
LE_PTS = [(0.0, 16.55), (WING_SIDE, 19.40), (8.0, 22.99), (12.0, 26.32), (16.0, 29.47), (20.0, 32.44), (26.0, 36.87),
          (30.0, 39.81), (34.0, 42.85), (38.0, 45.80), (WING_TIP, 46.97)]
TE_IN = 36.70           # trailing edge at the fuselage side
TE_KINK = 38.95
TE_TIP = 50.80
WING_Z0 = 4.20          # chord-line height of the leading edge at the fuselage side
DIHEDRAL = math.radians(5.6)
WING_HINGE = 0.74       # rear spar (flap / aileron hinge) as a fraction of the local chord
WING_AF = "2415"
_le_tab = np.array(LE_PTS)


def le_s(y):
    return float(np.interp(abs(y), _le_tab[:, 0], _le_tab[:, 1]))


def te_s(y):
    ay = max(abs(y), WING_SIDE)
    if ay <= WING_KINK:
        return TE_IN + (ay - WING_SIDE) * (TE_KINK - TE_IN) / (WING_KINK - WING_SIDE)
    return TE_KINK + (ay - WING_KINK) * (TE_TIP - TE_KINK) / (WING_TIP - WING_KINK)


def wing_chord(y):
    return te_s(y) - le_s(y)


def wing_z(y):
    return WING_Z0 + max(abs(y) - WING_SIDE, 0.0) * math.tan(DIHEDRAL)


def wing_thickness(y):
    return float(np.interp(abs(y), [0, WING_SIDE, WING_KINK, WING_TIP], [0.85, 0.85, 0.70, 0.60]))


def wing_station(y, h=WING_HINGE, scale_t=None, c_override=None):
    ay = abs(y)
    sgn = 1.0 if y >= 0 else -1.0
    c = c_override or wing_chord(ay)
    inc = math.radians(2.2 - 3.4 * min(ay / WING_TIP, 1.0))      # 2.2 deg root -> -1.2 deg tip washout
    chord_dir = Vector((0.0, math.cos(inc), -math.sin(inc)))
    up = Vector((0.0, math.sin(inc), math.cos(inc)))
    le = Vector((sgn * ay, Y(le_s(ay)), wing_z(ay)))
    return dict(le=le, chord_dir=chord_dir, up=up, c=c, h=h, scale_t=wing_thickness(ay) if scale_t is None else scale_t)


def wing_surface_z(y, s, upper=False):
    """z of the wing's lower (or upper) skin at span y, station s."""
    from geom import airfoil_eval
    st = wing_station(y, h=None)
    xf = (s - le_s(y)) / st["c"]
    zu, zl, _ = airfoil_eval(WING_AF)(np.array([xf]))
    inc_drop = st["c"] * xf * st["chord_dir"].z
    return float(st["le"].z + inc_drop + (zu[0] if upper else zl[0]) * st["scale_t"] * st["c"])


def hinge_point(y):
    st = wing_station(y)
    return st["le"] + st["chord_dir"] * (st["c"] * st["h"])


# trailing-edge pieces per side: (name, from, to); "filler" pieces are fixed. Two flaps (the inboard one ends at
# the kink so its hinge stays straight) and one aileron per side keep LOD0 within its draw-call budget.
TE_PIECES = [("Flap_{}_In", WING_SIDE + 0.10, 11.30), ("filler", 11.30, 11.70),
             ("Flap_{}_Out", 11.70, 27.40), ("filler", 27.40, 27.60), ("Aileron_{}", 27.60, 38.20)]
TIP_START = 38.25


def build_wing():
    """Returns {"Wing": fixed MeshBuilder, "Flap_L_In": ..., "Aileron_R": ...}."""
    ys = sorted(set([round(v, 4) for v in
                     list(np.linspace(-TIP_START, -WING_KINK, 16)) + list(np.linspace(-WING_KINK, WING_KINK, 13)) +
                     list(np.linspace(WING_KINK, TIP_START, 16)) + [-WING_SIDE, WING_SIDE] +
                     [sg * v for sg in (1, -1) for v in (8.0, 12.0, 16.0, 20.0, 26.0, 30.0, 34.0, 38.0)]]))
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


# Flap-track fairings ("canoes") under the wing, at the spans the plan view dimensions (9.36 ... 25.97 m)
FLAP_TRACKS = (9.36, 13.49, 17.65, 21.87, 25.97)


def build_flap_tracks():
    b = MeshBuilder()
    for sgn in (1, -1):
        for y in FLAP_TRACKS:
            c = wing_chord(y)
            s0 = le_s(y) + 0.42 * c
            length = 0.58 * c + 1.9
            r_max = 0.20 + 0.012 * c
            prof = [(length * t, r_max * (math.sin(math.pi * min(t / 0.62, 1.0) * 0.5) ** 0.8) *
                     (1.0 if t < 0.62 else (1.0 - (t - 0.62) / 0.38) ** 0.7) + 0.002)
                    for t in np.linspace(0.0, 1.0, 14)]
            z = wing_surface_z(y, s0 + 0.55 * length) - 0.55 * r_max
            revolve(b, prof, P(sgn * y, s0, z), (0, 1, 0), "Body", "wing_track", segs=14)
    return b


# Wingtip fences: a thin arrow-head plate standing above and below the tip (2.4 m tall in the front view),
# projecting 1.1 m behind the trailing edge (52.07 m aft of the nose in the plan view).
FENCE_UP, FENCE_DN = 1.25, 1.15
FENCE_AFT = 52.07


def _fence_station(dz):
    t = abs(dz) / (FENCE_UP if dz >= 0 else FENCE_DN)
    le = le_s(WING_TIP) + 3.25 * t ** 0.9
    te = FENCE_AFT - (1.05 if dz >= 0 else 1.25) * t ** 1.3
    return le, te


def build_fences():
    b = MeshBuilder()
    tip = wing_station(WING_TIP, h=None)
    z0 = tip["le"].z - 0.02
    for sgn in (1, -1):
        sts = []
        for dz in (-FENCE_DN, -0.95, -0.7, -0.4, -0.15, 0.0, 0.15, 0.4, 0.7, 1.0, FENCE_UP):
            le, te = _fence_station(dz)
            sts.append(dict(le=Vector((sgn * (WING_TIP + 0.02), Y(le), z0 + dz)), chord_dir=Vector((0, 1, 0)),
                            up=Vector((sgn, 0, 0)), c=te - le, h=None, scale_t=0.55))
        lifting_surface(b, sts, "0010", "Body", "fence", flip=(sgn < 0))
    return b


# ============================================================== engines

# Trent 900-sized nacelles (A380 nacelle sheet: 3.88 m across, 6.2 m from the intake lip to the end of the
# thrust reverser, 8.4 m to the plug tip): the Narrowbody's nacelle profile scaled KL along, KR across.
KL = 1.65
KR = 1.835
# (engine number, span, intake-lip station, centre height): 1 = left outboard ... 4 = right outboard.
# Spans 29.6 / 51.4 m between the engines, intakes 22.23 / 29.94 m aft of the nose, nacelle bottoms 1.05 / 1.90 m.
ENGINES = [(1, 25.70, 29.94, 3.85), (2, 14.80, 22.23, 3.00), (3, -14.80, 22.23, 3.00), (4, -25.70, 29.94, 3.85)]
NACELLE = [(0.0, 0.90), (0.03, 0.955), (0.10, 0.99), (0.25, 1.02), (0.6, 1.045), (1.3, 1.055), (2.0, 1.04),
           (2.7, 0.99), (3.3, 0.90), (3.75, 0.80)]


def _k(pts):
    return [(KL * a, KR * r) for a, r in pts]


def build_engine(x, inlet_s, z):
    """Returns (static nacelle + pylon, spinning fan, fan hub)."""
    b = MeshBuilder()
    o = P(x, inlet_s, z)
    revolve(b, _k(NACELLE), o, (0, 1, 0), "Body", "nacelle", segs=48)
    revolve(b, _k([(0.0, 0.90), (0.05, 0.865), (0.25, 0.845), (0.85, 0.835)]), o, (0, 1, 0), "Metal", "intake",
            segs=48, flip=True)
    revolve(b, _k([(0.85, 0.835), (0.86, 0.24)]), o, (0, 1, 0), "Dark", "fanface", segs=48, flip=True)
    revolve(b, _k([(3.75, 0.80), (3.70, 0.74), (3.45, 0.70)]), o, (0, 1, 0), "DarkMetal", "fanduct", segs=48, flip=True)
    revolve(b, _k([(3.45, 0.70), (3.8, 0.66), (4.3, 0.58), (4.65, 0.50)]), o, (0, 1, 0), "Metal", "core", segs=36)
    revolve(b, _k([(4.65, 0.50), (4.62, 0.44), (4.40, 0.42)]), o, (0, 1, 0), "DarkMetal", "core", segs=36, flip=True)
    revolve(b, _k([(4.40, 0.40), (4.75, 0.33), (5.05, 0.18), (5.20, 0.02)]), o, (0, 1, 0), "DarkMetal", "plug", segs=28)
    # pylon: a thin blade from inside the nacelle top, along the core, up and back into the wing
    le_w = le_s(x)
    top = wing_surface_z(x, le_w + 2.8) + 0.30
    sts = []
    for zz, le, te in ((z + 0.55 * KR, inlet_s + 1.5 * KL, inlet_s + 5.4 * KL),
                       (0.5 * (z + 1.0 * KR + top), inlet_s + 1.15 * KL, le_w + 4.2),
                       (top, le_w + 0.30, le_w + 6.2)):
        sts.append(dict(le=Vector((x, Y(le), zz)), chord_dir=Vector((0, 1, 0)), up=Vector((1, 0, 0)), c=te - le,
                        h=None, scale_t=0.62 / ((te - le) * 0.10)))
    lifting_surface(b, sts, "0010", "Body", "pylon")

    fan = MeshBuilder()
    hub = Vector(P(x, inlet_s + KL * 0.8, z))
    prof = [(0.38 + 0.42 * (k / 10), 0.25 * (1 - (1 - k / 10) ** 2.2) ** 0.5 + 0.001) for k in range(11)]
    revolve(fan, [(KL * (t - 0.8), KR * r) for t, r in prof], hub, (0, 1, 0), "Metal", "spinner", segs=28)
    n_blades = 24
    for k in range(n_blades):
        a0 = 2 * math.pi * k / n_blades
        radial = Vector((math.cos(a0), 0, math.sin(a0)))
        tangential = Vector((-math.sin(a0), 0, math.cos(a0)))
        axial = Vector((0, 1, 0))
        rings = []
        for r in np.linspace(0.24, 0.83, 6):
            twist = math.radians(55 - 30 * (r - 0.24) / 0.59)
            half = KR * (0.06 + 0.03 * (r - 0.24) / 0.59)
            chord = tangential * math.cos(twist) + axial * math.sin(twist)
            thick = chord.cross(radial).normalized() * 0.012
            c = hub + radial * (KR * r)
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

# Tailplane traced from the plan view: 30.37 m span, root LE 57.35 m (on the centre line), 0.765 m of sweep per
# metre; TE 67.05 + 0.36 per metre; elevator hinge at 0.70 chord. Tip 9.2 m above the ground (HT, MRW).
HT_Z = 7.60
HT_ROOT_LE = 57.35
HT_SPAN = 15.18
HT_LE_TAN = 0.765
HT_TE0, HT_TE_TAN = 67.05, 0.36
HT_HINGE = 0.70
HT_DIHEDRAL = math.radians(6.0)
EL_ROOT = 2.30          # elevator inboard end, clear of the tail cone
EL_TIP = 14.75


def ht_station(x, h=HT_HINGE, scale_t=1.0):
    ax = abs(x)
    le = HT_ROOT_LE + ax * HT_LE_TAN
    c = (HT_TE0 + ax * HT_TE_TAN) - le
    return dict(le=Vector((x, Y(le), HT_Z + ax * math.tan(HT_DIHEDRAL))), chord_dir=Vector((0, 1, 0)),
                up=Vector((0, 0, 1)), c=c, h=h, scale_t=scale_t)


def ht_hinge(x):
    st = ht_station(x)
    return st["le"] + st["chord_dir"] * (st["c"] * st["h"])


def build_htail():
    out = {}
    stab = MeshBuilder()
    xs = sorted(set([round(v, 4) for v in np.linspace(-EL_TIP, EL_TIP, 17)]))
    lifting_surface(stab, [ht_station(x) for x in xs], "0011", "Body", "htail", flip=True)
    for sgn, side in ((1, "L"), (-1, "R")):
        tip = [ht_station(sgn * x, h=None, scale_t=t) for x, t in ((EL_TIP, 1.0), (14.98, 0.85), (HT_SPAN, 0.55))]
        lifting_surface(stab, tip, "0011", "Body", "htail", flip=(sgn > 0))
        el = MeshBuilder()
        lifting_surface(el, [ht_station(sgn * x) for x in np.linspace(EL_ROOT, EL_TIP, 8)], "0011", "Body", "elevator",
                        part="ctrl", flip=(sgn > 0))
        out["Elevator_" + side] = el
    out["HStab"] = stab
    return out


# Fin traced from the side view: root (on the 9.58 m fuselage reference line) LE 53.94 m, chord 12.06 m; tip at
# 24.17 m above the ground (VT, MRW), LE 68.02 m, chord 4.70 m. Rudder hinge at 0.68 chord.
VT_ROOT_Z = 9.58
VT_TOP_Z = 24.17
VT_ROOT_LE, VT_TIP_LE = 53.94, 68.02
VT_ROOT_C, VT_TIP_C = 12.06, 4.70
VT_HINGE = 0.68
VT_BOTTOM = 8.90        # the fin surface starts inside the tail cone


def vt_le_s(z):
    return VT_ROOT_LE + (z - VT_ROOT_Z) * (VT_TIP_LE - VT_ROOT_LE) / (VT_TOP_Z - VT_ROOT_Z)


def vt_chord(z):
    return VT_ROOT_C - (z - VT_ROOT_Z) / (VT_TOP_Z - VT_ROOT_Z) * (VT_ROOT_C - VT_TIP_C)


def vt_station(z, h=VT_HINGE, scale_t=1.0):
    c = vt_chord(z)
    return dict(le=Vector((0.0, Y(vt_le_s(z)), z)), chord_dir=Vector((0, 1, 0)), up=Vector((1, 0, 0)), c=c, h=h,
                scale_t=scale_t)


def vt_hinge(z):
    st = vt_station(z)
    return st["le"] + st["chord_dir"] * (st["c"] * st["h"])


RUDDER = (9.95, 23.60)


def build_vtail():
    out = {}
    fin = MeshBuilder()
    lifting_surface(fin, [vt_station(z) for z in np.linspace(VT_BOTTOM, 23.62, 14)], "0010", "Body", "vtail")
    caps = [vt_station(z, h=None, scale_t=t) for z, t in ((23.60, 1.0), (23.95, 0.85), (VT_TOP_Z, 0.5))]
    lifting_surface(fin, caps, "0010", "Body", "vtail")
    # dorsal fillet blending the fin root into the crown
    s0, s1 = 50.6, 55.9
    rings = []
    for s in np.linspace(s0, s1, 12):
        t = (s - s0) / (s1 - s0)
        zt = float(Fuselage.zt(s))
        ztop = zt + 0.01 + (11.3 - zt) * t ** 2.2
        wb = 0.18 + 0.40 * math.sin(math.pi * t) ** 0.6
        wt = 0.03 + 0.20 * t
        port = []
        for h in (0.0, 0.25, 0.55, 0.8, 1.0):
            px = wb * (1 - h) ** 2.4 + wt * h * (1 - h * 0.35)
            port.append((px, Fuselage.top_z(s, wb) - 0.05 + (ztop - Fuselage.top_z(s, wb) + 0.05) * h))
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
    lifting_surface(rud, [vt_station(z) for z in np.linspace(RUDDER[0], RUDDER[1], 10)], "0010", "Body", "rudder",
                    part="ctrl")
    out["Rudder"] = rud
    return out


# ============================================================== landing gear

# Nose gear 4.97 m aft of the nose, twin 1270 mm tyres. Wing gears (4-wheel bogies) 33.58 m aft of the nose on
# a 12.46 m track (14.34 m over the outer tyres); body gears (6-wheel bogies) 36.85 m aft of the nose on a
# 5.26 m track. Axle stations and tyre centres traced from the side and front views. 1400 mm main tyres.
NOSE_R = 0.62
NOSE_AXLE_Z = NOSE_R - 0.02
NOSE_WHEEL_X = 0.57
MAIN_R = 0.67
MAIN_W = 0.53
MAIN_AXLE_Z = MAIN_R - 0.03
WLG_S, WLG_X, WLG_DX = 33.58, 6.23, 0.675
WLG_AXLES = [(1, -0.81), (2, 0.81)]
BLG_S, BLG_X, BLG_DX = 36.85, 2.63, 0.785
BLG_AXLES = [(1, -1.70), (2, 0.0), (3, 1.70)]


def _bogie(b, x, sx, s_c, axles, dx, top, leg_s):
    """Leg from `top` to the bogie beam, beam, axles, a pitch damper and side brace."""
    pivot_z = MAIN_AXLE_Z + 0.14
    tube(b, [P(x, leg_s - 0.1, top), P(x, leg_s, max(top - 0.9, 1.95))], lambda t: 0.27, "Metal", "gearleg", segs=16)
    tube(b, [P(x, leg_s, max(top - 0.8, 2.0)), P(x, leg_s, pivot_z + 0.1)], lambda t: 0.17, "Chrome", "gearleg",
         segs=16)
    lo, hi = axles[0][1], axles[-1][1]
    tube(b, [P(x, s_c + lo - 0.32, MAIN_AXLE_Z + 0.05), P(x, s_c + hi + 0.32, MAIN_AXLE_Z + 0.05)], lambda t: 0.14,
         "Metal", "gearleg", segs=12)
    tube(b, [P(x, leg_s - 0.22, pivot_z + 0.12), P(x, leg_s + 0.22, pivot_z + 0.12)], lambda t: 0.20, "DarkMetal",
         "gearleg", segs=12)
    for _, ds in axles:
        tube(b, [P(x - sx * (dx + 0.05), s_c + ds, MAIN_AXLE_Z), P(x + sx * (dx + 0.05), s_c + ds, MAIN_AXLE_Z)],
             lambda t: 0.10, "DarkMetal", "gearleg", segs=10)
    tube(b, [P(x + sx * 0.22, leg_s - 0.05, min(top - 0.6, 1.6)), P(x + sx * 0.22, s_c + lo + 0.25, MAIN_AXLE_Z + 0.16)],
         lambda t: 0.06, "Metal", "gearleg", segs=8)
    tube(b, [P(x, leg_s, 1.75), P(x, leg_s - 0.32, 1.42), P(x, leg_s - 0.02, 1.12)], lambda t: 0.05, "Metal",
         "gearleg", segs=8)


def build_gear():
    """Returns {"Gear": fixed legs + bogies, "NoseGear": steering part, "Wheel_*": spinning wheels}. A main-gear
    wheel part is one axle line across the aircraft: the left and right bogies' axles at the same station share
    a spin axis, so each pair rolls as one part (fewer draw calls)."""
    b, nose, out = MeshBuilder(), MeshBuilder(), {}
    for sx in (1, -1):
        # wing gear: hangs from the wing's rear spar
        x = sx * WLG_X
        top = wing_surface_z(WLG_X, WLG_S - 0.2) + 0.25
        _bogie(b, x, sx, WLG_S, WLG_AXLES, WLG_DX, top, WLG_S - 0.15)
        tube(b, [P(x, WLG_S - 0.1, top - 0.7), P(sx * (WLG_X - 1.9), WLG_S + 0.3, top - 0.25)], lambda t: 0.11, "Metal",
             "gearleg", segs=10)
        # body gear: from the belly fairing
        x = sx * BLG_X
        top = Fuselage.bottom_z(BLG_S, BLG_X) + 0.35
        _bogie(b, x, sx, BLG_S, BLG_AXLES, BLG_DX, top, BLG_S - 0.3)
    for k, ds in WLG_AXLES:
        w = MeshBuilder()
        for sx in (1, -1):
            for dx in (-WLG_DX, WLG_DX):
                wheel(w, P(sx * WLG_X + dx, WLG_S + ds, MAIN_AXLE_Z), MAIN_R, MAIN_W, "wheel")
        out[f"Wheel_W{k}"] = w
    for k, ds in BLG_AXLES:
        w = MeshBuilder()
        for sx in (1, -1):
            for dx in (-BLG_DX, BLG_DX):
                wheel(w, P(sx * BLG_X + dx, BLG_S + ds, MAIN_AXLE_Z), MAIN_R, MAIN_W, "wheel")
        out[f"Wheel_B{k}"] = w
    # nose gear: the upper strut is fixed, everything below the steering collar turns
    zb = float(Fuselage.zb(NOSE_S))
    tube(b, [P(0, NOSE_S - 0.08, zb + 0.4), P(0, NOSE_S, 1.85)], lambda t: 0.16, "Metal", "nosegear", segs=14)
    tube(nose, [P(0, NOSE_S, 1.92), P(0, NOSE_S, NOSE_AXLE_Z + 0.12)], lambda t: 0.115, "Chrome", "nosegear", segs=14)
    tube(nose, [P(-NOSE_WHEEL_X - 0.05, NOSE_S, NOSE_AXLE_Z), P(NOSE_WHEEL_X + 0.05, NOSE_S, NOSE_AXLE_Z)],
         lambda t: 0.08, "DarkMetal", "nosegear", segs=10)
    tube(nose, [P(0, NOSE_S + 0.03, 1.55), P(0, NOSE_S + 0.30, 1.28), P(0, NOSE_S + 0.04, 1.02)], lambda t: 0.045,
         "Metal", "nosegear", segs=8)
    tube(b, [P(0, NOSE_S - 0.02, 2.2), P(0, NOSE_S - 1.5, float(Fuselage.zb(NOSE_S - 1.5)) + 0.25)], lambda t: 0.08,
         "Metal", "nosegear", segs=10)
    revolve(nose, [(-0.08, 0.001), (-0.06, 0.10), (0.05, 0.105), (0.06, 0.001)], P(0, NOSE_S - 0.17, 1.70), (0, 1, 0),
            "LightLens", "light", segs=12)
    w = MeshBuilder()
    for sx in (1, -1):
        wheel(w, P(sx * NOSE_WHEEL_X, NOSE_S, NOSE_AXLE_Z), NOSE_R, 0.45, "wheel")
    out["Wheel_Nose"] = w
    out["Gear"] = b
    out["NoseGear"] = nose
    return out


# ============================================================== lights, antennas, probes

NAV_S = le_s(WING_TIP) + 0.35
BEACON_TOP_S = 30.0
BEACON_BOTTOM_S = 44.5
LANDING = (5.2, 24.4)           # (span, station) under the wing root
LOGO_LIGHT = (3.6, 63.4)        # (span, station) on the stabiliser


def build_details():
    b = MeshBuilder()
    tip_z = wing_station(WING_TIP, h=None)["le"].z
    for sx, mat in ((1, "LightRed"), (-1, "LightGreen")):
        # nav light in the fence's leading edge, strobe behind it
        revolve(b, [(-0.13, 0.001), (-0.08, 0.065), (0.06, 0.07), (0.13, 0.001)],
                (sx * (WING_TIP + 0.05), Y(NAV_S), tip_z + 0.02), (0, 1, 0), mat, "light", segs=12)
        revolve(b, [(-0.08, 0.001), (-0.04, 0.05), (0.05, 0.05), (0.08, 0.001)],
                (sx * (WING_TIP + 0.05), Y(NAV_S + 0.5), tip_z + 0.03), (0, 1, 0), "LightWhite", "light", segs=10)
        y, s = LANDING
        revolve(b, [(-0.01, 0.001), (-0.005, 0.17), (0.05, 0.18)], (sx * y, Y(s), wing_surface_z(y, s) - 0.01),
                (0, 0, -1), "LightLens", "light", segs=14, ref=(1, 0, 0))
        x, s = sx * LOGO_LIGHT[0], LOGO_LIGHT[1]
        z = ht_station(x, h=None)["le"].z + 0.14
        revolve(b, [(-0.01, 0.08), (0.04, 0.07), (0.07, 0.001)], P(x, s, z), (0, 0, 1), "LightWhite", "light",
                segs=10, ref=(1, 0, 0))
    s = BEACON_TOP_S
    revolve(b, [(-0.01, 0.13), (0.06, 0.12), (0.13, 0.07), (0.16, 0.001)], P(0, s, float(Fuselage.zt(s)) - 0.02),
            (0, 0, 1), "LightRed", "light", segs=14, ref=(1, 0, 0))
    s = BEACON_BOTTOM_S
    revolve(b, [(-0.01, 0.13), (0.06, 0.12), (0.13, 0.07), (0.16, 0.001)], P(0, s, Fuselage.bottom_z(s, 0) + 0.02),
            (0, 0, -1), "LightRed", "light", segs=14, ref=(1, 0, 0))
    zt = float(Fuselage.zm(Fuselage.S1))
    revolve(b, [(-0.05, 0.001), (-0.03, 0.08), (0.05, 0.08), (0.08, 0.001)], P(0, Fuselage.S1 + 0.02, zt + 0.40),
            (0, 1, 0), "LightWhite", "light", segs=10)
    for s, up, h in ((12.5, 1, 0.50), (22.0, 1, 0.42), (50.5, -1, 0.34), (10.0, -1, 0.32)):
        z = float(Fuselage.zt(s)) if up > 0 else Fuselage.bottom_z(s, 0)
        tube(b, [P(0, s, z - up * 0.02), P(0, s + 0.34, z + up * h)], lambda t: 0.065 * (1 - 0.6 * t), "Dark", "detail",
             segs=6, squash=0.3)
    s0, s1 = 25.5, 29.3
    sat = []
    for s in np.linspace(s0, s1, 9):
        t = (s - s0) / (s1 - s0)
        sat.append((s, 0.36 * math.sin(math.pi * t) ** 0.5 + 0.001))
    zc = float(Fuselage.zt(27.4)) - 0.19
    revolve(b, [(s - s0, r) for s, r in sat], P(0, s0, zc), (0, 1, 0), "Body", "satcom", segs=20)
    # pitot probes under the windshield
    for sx, z in ((1, 6.15), (-1, 6.15), (1, 5.70)):
        s = WS_FRONT + 1.0
        x = sx * Fuselage.side_x(s, z)
        tube(b, [P(x, s, z), P(x + sx * 0.16, s - 0.03, z), P(x + sx * 0.17, s - 0.28, z)], lambda t: 0.017, "Metal",
             "detail", segs=6)
    return b
