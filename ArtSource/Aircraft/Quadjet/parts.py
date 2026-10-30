"""Long-haul quadjet with a partial upper deck, built procedurally. Proportions and figures follow the Boeing
747-8 "Airplane Characteristics for Airport Planning" document (D6-58326-3, rev. E, January 2026): the side,
plan and front views of its general-dimensions sheet (2.2.2) were traced into the profiles below, and heights
were set from its ground-clearance table (2.3.2, the middle of each minimum / maximum range).

Stations `s` are metres aft of the nose tip. Blender Y = s - S_REF, so the model origin sits on the ground
under the turn centre of the main gears (the effective main-gear centre of the 747-8 turning-radii table).
Nose points -Y, port wing is +X, Z up.
"""
import math

import numpy as np
from mathutils import Vector

from fuselage import Fuselage as BaseFuselage, Loft, profile
from geom import MeshBuilder, resample_polyline, smoothstep
from shapes import _fd_normal, drape_pane, lifting_surface, revolve, rounded_outline, tube, wheel

NOSE_S = 7.74           # nose gear axle station (747-8: 7.74 m)
# turn centre: nose-gear radius 30.8 m at 70 deg steering with body-gear steering (747-8 table 4.2.1)
# -> 28.94 m effective wheelbase (29.66 m to the midpoint of the wing and body gears)
S_REF = NOSE_S + 28.94


def Y(s):
    return s - S_REF


def P(x, s, z):
    return (x, Y(s), z)


# ============================================================== fuselage

KEEL = 2.24             # keel of the constant section (6.50 m wide; 747-8 general dimensions)
HUMP = 10.04            # upper-deck crown
CROWN = 9.02            # main-deck crown aft of the hump
HALF_WIDTH = 3.24
TIP_Z = 5.70            # radome tip, level with the main-deck window belt
ZM = 5.50               # widest line of the constant section

# Traced from the side / plan views of the general-dimensions sheet (line centres), heights above the ground.
# The top line climbs from the radome over the flight-deck "brow" (steeper at the windshield) onto the
# upper-deck hump, which ends in a long fairing down to the main-deck crown at s ~35.
TOP = [(0.008, 5.83), (0.05, 5.98), (0.12, 6.10), (0.28, 6.27), (0.53, 6.48), (0.8, 6.66), (1.05, 6.81),
       (1.5, 7.08), (2.0, 7.37), (2.5, 7.63), (3.0, 7.90), (3.5, 8.14), (4.0, 8.38), (4.3, 8.56), (4.6, 8.80),
       (4.9, 9.05), (5.2, 9.27), (5.6, 9.50), (6.0, 9.67), (6.5, 9.82), (7.0, 9.93), (7.5, 9.99), (8.3, 10.03),
       (9.0, HUMP), (24.5, HUMP), (25.5, 10.00), (26.5, 9.94), (27.5, 9.86), (28.5, 9.75), (29.5, 9.62),
       (30.5, 9.49), (31.5, 9.36), (32.5, 9.23), (33.5, 9.12), (34.5, 9.05), (35.5, CROWN), (60.0, CROWN),
       (70.0, 9.00), (73.6, 8.95), (75.0, 8.80), (76.05, 8.62)]
BOTTOM = [(0.008, 5.57), (0.05, 5.42), (0.12, 5.28), (0.28, 5.10), (0.53, 4.90), (0.8, 4.72), (1.05, 4.57),
          (1.6, 4.29), (2.15, 4.03), (2.7, 3.81), (3.2, 3.62), (3.7, 3.46), (4.2, 3.31), (5.2, 3.10), (6.2, 2.90),
          (7.2, 2.74), (8.3, 2.57), (9.3, 2.46), (10.3, 2.38), (11.4, 2.31), (12.5, 2.26), (14.0, KEEL),
          (50.0, KEEL),
          # tail upsweep to the APU exhaust
          (50.8, 2.30), (52.0, 2.42), (53.5, 2.62), (55.0, 2.86), (56.5, 3.15), (58.0, 3.50), (59.5, 3.73),
          (61.0, 4.04), (62.5, 4.39), (64.0, 4.67), (65.5, 5.00), (67.0, 5.31), (68.5, 5.63), (70.0, 5.95),
          (71.5, 6.27), (73.0, 6.63), (74.2, 7.00), (75.2, 7.62), (76.05, 8.10)]
HALF = [(0.008, 0.16), (0.1, 0.42), (0.25, 0.63), (0.5, 0.88), (0.8, 1.07), (1.2, 1.25), (1.6, 1.37),
        (2.0, 1.49), (2.5, 1.64), (3.0, 1.79), (3.5, 1.93), (4.0, 2.07), (4.5, 2.19), (5.0, 2.29), (5.5, 2.38),
        (6.0, 2.47), (6.5, 2.55), (7.0, 2.63), (7.5, 2.71), (8.0, 2.78), (9.0, 2.87), (10.0, 2.95), (11.0, 3.06),
        (12.0, 3.14), (13.0, 3.20), (14.0, 3.23), (15.0, HALF_WIDTH), (56.0, HALF_WIDTH), (57.0, 3.13),
        (58.0, 3.04), (59.0, 2.95), (60.0, 2.82), (61.0, 2.65), (62.0, 2.53), (63.0, 2.36), (64.0, 2.18),
        (65.0, 2.00), (66.0, 1.85), (67.0, 1.66), (68.0, 1.50), (69.0, 1.33), (70.0, 1.12), (71.0, 0.92),
        (72.0, 0.73), (73.0, 0.56), (74.0, 0.42), (75.0, 0.32), (76.05, 0.26)]
# height of the widest line: the radome tip, settling to the main deck; the tail cone's middle
WIDEST = [(0.008, TIP_Z), (1.0, 5.69), (3.0, 5.70), (5.0, 5.68), (8.0, 5.62), (12.0, 5.55), (15.0, ZM),
          (55.0, ZM), (56.0, 5.55), (58.0, 5.95), (60.0, 6.25), (62.0, 6.55), (64.0, 6.82), (66.0, 7.05),
          (68.0, 7.30), (70.0, 7.55), (72.0, 7.78), (74.0, 8.00), (76.05, 8.36)]

# The wing-to-body fairing is a little wider than the lower lobe around the wing root: a box-like
# superellipse merged into it with a smooth max (as on the DoubleDeck, much smaller).
FAIRING = (19.5, 25.5, 43.0, 50.5)      # ramps in from s 19.5 to 25.5, out from 43 to 50.5
FAIRING_HW = 3.52
FAIRING_TOP = 4.55
FAIRING_N = 3.2

S_END = 76.05           # end of the loft (APU exhaust ring)


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
    grid = sorted(set(np.round(np.concatenate([np.linspace(0.008, 1.0, 12), np.linspace(1.0, S_END, 150)]), 4)))
    return profile([(s, (float(zm(s)) - float(zb(s))) / (float(zt(s)) - float(zb(s)))) for s in grid])


class Fuselage(BaseFuselage):
    S0 = 0.008
    S1 = S_END
    ML = 18
    MU = 20
    zt = profile(TOP)
    zb = profile(BOTTOM)
    hw = profile(HALF)
    fm = _fm_profile()
    # the upper-deck bubble keeps fuller sides than the main-deck crown
    nt = profile([(0.008, 2.0), (6.0, 2.05), (10.0, 2.15), (24.0, 2.15), (33.0, 2.0), (S_END, 2.0)])
    nb = profile([(0.008, 2.0), (14.0, 2.15), (20.0, 2.2), (26.0, 2.45), (42.0, 2.45), (50.0, 2.2), (60.0, 2.0),
                  (S_END, 2.0)])

    @classmethod
    def lower_quarter(cls, s, count):
        """Port lower quarter (widest line -> bottom centre) with the wing-to-body fairing merged in."""
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
    for stop, step in ((9.0, 0.25), (14.0, 0.5), (24.0, 0.65), (36.0, 0.5), (54.0, 0.65), (75.85, 0.4)):
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
    revolve(b, [(-0.06, 0.22), (0.12, 0.22), (0.12, 0.16), (-0.02, 0.16)], P(0, F.S1 + 0.02, float(F.zm(F.S1))),
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
    lo, hi = 0.01, 9.0
    for _ in range(50):
        mid = 0.5 * (lo + hi)
        lo, hi = (mid, hi) if float(Fuselage.zt(mid)) < KEEL + h else (lo, mid)
    return 0.5 * (lo + hi)


# Six-pane flight deck at the front of the upper deck (the window box of the side view: 8.5 to 9.1 m above
# the ground, s 4.2 to 6.7): a windshield pane either side of a thin centre post on the steep "brow", then a
# sliding side window and a smaller rear window whose top edge drops aft.
SILL = 8.48 - KEEL
HEAD = 9.12 - KEEL
WS_FRONT = _top_reaches(SILL)
WS_TOP = _top_reaches(HEAD)
POST = WS_TOP + 0.36
SIDE_1 = POST + 0.12
SIDE_1_L = 0.64
SIDE_2 = SIDE_1 + SIDE_1_L + 0.13
SIDE_2_L = 0.50
COCKPIT_WINDOWS = [
    ("windshield", [_at(WS_FRONT, deg=1.6), _at(WS_FRONT + 0.22, SILL), _at(POST - 0.34, SILL), _at(POST, SILL),
                    _at(POST + 0.20, HEAD), _at(WS_TOP + 0.14, HEAD), _at(WS_TOP, deg=1.6)],
     [0.04, 0.09, 0.32, 0.06, 0.05, 0.30, 0.04]),
    ("sidewindow", [_at(SIDE_1, SILL), _at(SIDE_1 + SIDE_1_L, SILL), _at(SIDE_1 + SIDE_1_L, HEAD - 0.04),
                    _at(SIDE_1 + 0.24, HEAD)],
     [0.06, 0.12, 0.13, 0.06]),
    ("sidewindow", [_at(SIDE_2, SILL), _at(SIDE_2 + SIDE_2_L, SILL + 0.04), _at(SIDE_2 + SIDE_2_L, HEAD - 0.26),
                    _at(SIDE_2, HEAD - 0.06)],
     [0.06, 0.10, 0.10, 0.07]),
]


def build_windows():
    b = MeshBuilder()
    for sign in (1, -1):
        surf = skin_surface(sign)
        for tag, corners, radii in COCKPIT_WINDOWS:
            drape_pane(b, _outline(corners, radii), surf, tag=tag, lift=0.01, sink=0.025)
    return b


# ============================================================== wing

# Planform traced from the plan view (line centres): leading edge 24.45 m aft of the nose at the fuselage
# side, ~42 deg inboard and ~40 deg outboard; trailing edge kinked at 10.3 m span. The raked tip sweeps both
# edges further back over the last 3.2 m (tip 52.6 / 55.25 m aft of the nose at 34.2 m: 68.4 m span).
WING_SIDE = HALF_WIDTH
WING_TIP = 34.20
RAKE = 31.0             # the raked tip starts here
LE_PTS = [(0.0, 21.45), (WING_SIDE, 24.45), (4.0, 25.17), (6.0, 26.96), (9.0, 29.80), (12.0, 32.54),
          (14.0, 34.38), (17.0, 37.13), (21.0, 40.84), (24.0, 43.33), (26.0, 45.01), (28.0, 46.68), (30.0, 48.36),
          (RAKE, 49.22), (32.6, 50.66), (33.6, 51.80), (WING_TIP, 52.60)]
TE_PTS = [(0.0, 39.0), (WING_SIDE, 39.25), (10.3, 41.50), (RAKE, 53.05), (32.7, 54.20), (WING_TIP, 55.25)]
WING_KINK = 10.3
WING_Z0 = 3.35          # chord-line height of the leading edge at the fuselage side
DIHEDRAL = math.radians(6.1)        # tip ~6.65 m above the ground (747-8 M: 6.51-6.80 m)
WING_HINGE = 0.74       # rear spar (flap / aileron hinge) as a fraction of the local chord
WING_AF = "2415"
_le_tab = np.array(LE_PTS)
_te_tab = np.array(TE_PTS)


def le_s(y):
    return float(np.interp(abs(y), _le_tab[:, 0], _le_tab[:, 1]))


def te_s(y):
    return float(np.interp(max(abs(y), WING_SIDE), _te_tab[:, 0], _te_tab[:, 1]))


def wing_chord(y):
    return te_s(y) - le_s(y)


def wing_z(y):
    return WING_Z0 + max(abs(y) - WING_SIDE, 0.0) * math.tan(DIHEDRAL)


def wing_thickness(y):
    return float(np.interp(abs(y), [0, WING_SIDE, WING_KINK, RAKE, WING_TIP], [0.90, 0.90, 0.72, 0.56, 0.50]))


def wing_station(y, h=WING_HINGE, scale_t=None, c_override=None):
    ay = abs(y)
    sgn = 1.0 if y >= 0 else -1.0
    c = c_override or wing_chord(ay)
    inc = math.radians(2.0 - 3.5 * min(ay / WING_TIP, 1.0))      # 2 deg root -> -1.5 deg tip washout
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
# the kink so its hinge stays straight; the fixed piece behind the inboard engine stands for the high-speed
# aileron) and one outboard aileron per side keep LOD0 within its draw-call budget.
TE_PIECES = [("Flap_{}_In", WING_SIDE + 0.10, WING_KINK), ("filler", WING_KINK, 12.6),
             ("Flap_{}_Out", 12.6, 24.9), ("filler", 24.9, 25.1), ("Aileron_{}", 25.1, 30.95)]
TIP_START = 31.0


def build_wing():
    """Returns {"Wing": fixed MeshBuilder, "Flap_L_In": ..., "Aileron_R": ...}."""
    ys = sorted(set([round(v, 4) for v in
                     list(np.linspace(-TIP_START, -WING_KINK, 16)) + list(np.linspace(-WING_KINK, WING_KINK, 13)) +
                     list(np.linspace(WING_KINK, TIP_START, 16)) + [-WING_SIDE, WING_SIDE] +
                     [sg * v for sg in (1, -1) for v in (4.0, 6.0, 9.0, 12.0, 14.0, 17.0, 21.0, 24.0, 26.0, 28.0,
                                                          30.0)]]))
    wing = MeshBuilder()
    lifting_surface(wing, [wing_station(y) for y in ys], WING_AF, "Body", "wing", flip=True)
    out = {"Wing": wing}
    for sgn, side in ((1, "L"), (-1, "R")):
        tip = [wing_station(sgn * y, h=None) for y in (TIP_START - 0.03, 31.6, 32.2, 32.7, 33.2, 33.6, 33.95, WING_TIP)]
        lifting_surface(wing, tip, WING_AF, "Body", "wing", flip=(sgn > 0))
        for name, y0, y1 in TE_PIECES:
            yy = np.linspace(y0, y1, max(int((y1 - y0) / 1.4) + 2, 2))
            target = wing if name == "filler" else out.setdefault(name.format(side), MeshBuilder())
            tag = "wing_filler" if name == "filler" else "wing_" + name.split("_")[0].lower()
            lifting_surface(target, [wing_station(sgn * y) for y in yy], WING_AF, "Body", tag, part="ctrl",
                            flip=(sgn > 0))
    return out


# Flap-track fairings ("canoes") under the wing, at the spans the plan view shows them
FLAP_TRACKS = (6.0, 9.6, 14.9, 18.7, 22.6, 26.4)


def build_flap_tracks():
    b = MeshBuilder()
    for sgn in (1, -1):
        for y in FLAP_TRACKS:
            c = wing_chord(y)
            s0 = le_s(y) + 0.42 * c
            length = 0.58 * c + 1.7
            r_max = 0.17 + 0.012 * c
            prof = [(length * t, r_max * (math.sin(math.pi * min(t / 0.62, 1.0) * 0.5) ** 0.8) *
                     (1.0 if t < 0.62 else (1.0 - (t - 0.62) / 0.38) ** 0.7) + 0.002)
                    for t in np.linspace(0.0, 1.0, 14)]
            z = wing_surface_z(y, s0 + 0.55 * length) - 0.55 * r_max
            revolve(b, prof, P(sgn * y, s0, z), (0, 1, 0), "Body", "wing_track", segs=14)
    return b


# ============================================================== engines

# GEnx-2B-sized nacelles (~3.2 m across, ~5.1 m fan cowl, ~7.1 m to the plug tip in the side view): the
# Narrowbody's nacelle profile scaled KL along, KR across.
KL = 1.36
KR = 1.52
# (engine number, span, intake-lip station, centre height): 1 = left outboard ... 4 = right outboard.
# Spans 11.93 / 21.18 m from the centre line, intakes 27.25 / 35.86 m aft of the nose (general dimensions);
# nacelle bottoms 0.85 / 1.98 m (ground clearances P / N).
NACELLE_R = KR * 1.055
ENGINES = [(1, 21.18, 35.86, 1.98 + NACELLE_R), (2, 11.93, 27.25, 0.85 + NACELLE_R),
           (3, -11.93, 27.25, 0.85 + NACELLE_R), (4, -21.18, 35.86, 1.98 + NACELLE_R)]
NACELLE = [(0.0, 0.90), (0.03, 0.955), (0.10, 0.99), (0.25, 1.02), (0.6, 1.045), (1.3, 1.055), (2.0, 1.04),
           (2.7, 0.99), (3.3, 0.90), (3.75, 0.80)]


def _k(pts):
    return [(KL * a, KR * r) for a, r in pts]


def build_engine(x, inlet_s, z):
    """Returns (static nacelle + pylon, spinning fan, fan hub)."""
    b = MeshBuilder()
    o = P(x, inlet_s, z)
    revolve(b, _k(NACELLE), o, (0, 1, 0), "Body", "nacelle", segs=44)
    revolve(b, _k([(0.0, 0.90), (0.05, 0.865), (0.25, 0.845), (0.85, 0.835)]), o, (0, 1, 0), "Metal", "intake",
            segs=44, flip=True)
    revolve(b, _k([(0.85, 0.835), (0.86, 0.24)]), o, (0, 1, 0), "Dark", "fanface", segs=44, flip=True)
    revolve(b, _k([(3.75, 0.80), (3.70, 0.74), (3.45, 0.70)]), o, (0, 1, 0), "DarkMetal", "fanduct", segs=44, flip=True)
    revolve(b, _k([(3.45, 0.70), (3.8, 0.66), (4.3, 0.58), (4.65, 0.50)]), o, (0, 1, 0), "Metal", "core", segs=32)
    revolve(b, _k([(4.65, 0.50), (4.62, 0.44), (4.40, 0.42)]), o, (0, 1, 0), "DarkMetal", "core", segs=32, flip=True)
    revolve(b, _k([(4.40, 0.40), (4.75, 0.33), (5.05, 0.18), (5.20, 0.02)]), o, (0, 1, 0), "DarkMetal", "plug", segs=24)
    # pylon: a thin blade from inside the nacelle top, along the core, up and back into the wing
    le_w = le_s(x)
    top = wing_surface_z(x, le_w + 2.4) + 0.30
    sts = []
    for zz, le, te in ((z + 0.55 * KR, inlet_s + 1.2 * KL, inlet_s + 5.3 * KL),
                       (0.5 * (z + 1.0 * KR + top), inlet_s + 1.0 * KL, le_w + 3.6),
                       (top, le_w + 0.25, le_w + 5.4)):
        sts.append(dict(le=Vector((x, Y(le), zz)), chord_dir=Vector((0, 1, 0)), up=Vector((1, 0, 0)), c=te - le,
                        h=None, scale_t=0.56 / ((te - le) * 0.10)))
    lifting_surface(b, sts, "0010", "Body", "pylon")

    fan = MeshBuilder()
    hub = Vector(P(x, inlet_s + KL * 0.8, z))
    prof = [(0.38 + 0.42 * (k / 10), 0.25 * (1 - (1 - k / 10) ** 2.2) ** 0.5 + 0.001) for k in range(11)]
    revolve(fan, [(KL * (t - 0.8), KR * r) for t, r in prof], hub, (0, 1, 0), "Metal", "spinner", segs=24)
    n_blades = 18
    for k in range(n_blades):
        a0 = 2 * math.pi * k / n_blades
        radial = Vector((math.cos(a0), 0, math.sin(a0)))
        tangential = Vector((-math.sin(a0), 0, math.cos(a0)))
        axial = Vector((0, 1, 0))
        rings = []
        for r in np.linspace(0.24, 0.83, 6):
            twist = math.radians(55 - 30 * (r - 0.24) / 0.59)
            half = KR * (0.075 + 0.04 * (r - 0.24) / 0.59)
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

# Tailplane traced from the plan view: 21.99 m span, LE 62.36 m on the centre line with 0.932 m of sweep per
# metre (43 deg); TE 72.36 + 0.277 per metre; elevator hinge at 0.70 chord. 7 deg dihedral puts the tip 8.65 m
# above the ground (747-8 L: 8.58-9.12 m).
HT_Z = 7.30
HT_ROOT_LE = 62.36
HT_SPAN = 11.0
HT_LE_TAN = 0.932
HT_TE0, HT_TE_TAN = 72.36, 0.277
HT_HINGE = 0.70
HT_DIHEDRAL = math.radians(7.0)
EL_ROOT = 1.85          # elevator inboard end, clear of the tail cone
EL_TIP = 10.55


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
    xs = sorted(set([round(v, 4) for v in np.linspace(-EL_TIP, EL_TIP, 15)]))
    lifting_surface(stab, [ht_station(x) for x in xs], "0011", "Body", "htail", flip=True)
    for sgn, side in ((1, "L"), (-1, "R")):
        tip = [ht_station(sgn * x, h=None, scale_t=t) for x, t in ((EL_TIP, 1.0), (10.8, 0.85), (HT_SPAN, 0.55))]
        lifting_surface(stab, tip, "0011", "Body", "htail", flip=(sgn > 0))
        el = MeshBuilder()
        lifting_surface(el, [ht_station(sgn * x) for x in np.linspace(EL_ROOT, EL_TIP, 8)], "0011", "Body", "elevator",
                        part="ctrl", flip=(sgn > 0))
        out["Elevator_" + side] = el
    out["HStab"] = stab
    return out


# Fin traced from the side view: root (on the 9.0 m crown line) LE 60.3 m, chord 11.9 m, LE sweep 1.17 m
# per metre of height; tip 19.38 m above the ground (747-8: 19.4 m; ground clearance K 18.97-19.51 m),
# chord 3.8 m, its trailing edge at 76.25 m (the overall length). Rudder hinge at 0.68 chord.
VT_ROOT_Z = 9.0
VT_TOP_Z = 19.38
VT_ROOT_LE, VT_TIP_LE = 60.30, 72.45
VT_ROOT_C, VT_TIP_C = 11.90, 3.80
VT_HINGE = 0.68
VT_BOTTOM = 8.40        # the fin surface starts inside the tail cone


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


RUDDER = (9.35, 18.85)


def build_vtail():
    out = {}
    fin = MeshBuilder()
    lifting_surface(fin, [vt_station(z) for z in np.linspace(VT_BOTTOM, 18.87, 13)], "0010", "Body", "vtail")
    caps = [vt_station(z, h=None, scale_t=t) for z, t in ((18.85, 1.0), (19.15, 0.85), (VT_TOP_Z, 0.5))]
    lifting_surface(fin, caps, "0010", "Body", "vtail")
    # dorsal fillet blending the fin root into the crown
    s0, s1 = 56.4, 61.6
    rings = []
    for s in np.linspace(s0, s1, 12):
        t = (s - s0) / (s1 - s0)
        zt = float(Fuselage.zt(s))
        ztop = zt + 0.01 + (10.45 - zt) * t ** 2.2
        wb = 0.16 + 0.34 * math.sin(math.pi * t) ** 0.6
        wt = 0.03 + 0.18 * t
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
    lifting_surface(rud, [vt_station(z) for z in np.linspace(RUDDER[0], RUDDER[1], 9)], "0010", "Body", "rudder",
                    part="ctrl")
    out["Rudder"] = rud
    return out


# ============================================================== landing gear

# Nose gear 7.74 m aft of the nose, twin 50 in (1.27 m) tyres. Wing gears 35.87 m aft of the nose on a 10.99 m
# track, body gears 3.07 m further aft (38.94 m) on a 3.83 m track; all four are four-wheel bogies with 58 in
# (1.47 m) between the axles and 52 in (1.32 m) tyres (general dimensions, gear footprint).
NOSE_R = 0.635
NOSE_AXLE_Z = NOSE_R - 0.02
NOSE_WHEEL_X = 0.46
MAIN_R = 0.66
MAIN_W = 0.53
MAIN_AXLE_Z = MAIN_R - 0.03
BOGIE_DS = 0.735
WLG_S, WLG_X, WLG_DX = 35.87, 5.495, 0.56
BLG_S, BLG_X, BLG_DX = 38.94, 1.915, 0.56
AXLES = [(1, -BOGIE_DS), (2, BOGIE_DS)]


def _bogie(b, x, sx, s_c, axles, dx, top, leg_s):
    """Leg from `top` to the bogie beam, beam, axles, a pitch damper and side brace."""
    pivot_z = MAIN_AXLE_Z + 0.14
    tube(b, [P(x, leg_s - 0.1, top), P(x, leg_s, max(top - 0.9, 1.85))], lambda t: 0.25, "Metal", "gearleg", segs=14)
    tube(b, [P(x, leg_s, max(top - 0.8, 1.9)), P(x, leg_s, pivot_z + 0.1)], lambda t: 0.16, "Chrome", "gearleg",
         segs=14)
    lo, hi = axles[0][1], axles[-1][1]
    tube(b, [P(x, s_c + lo - 0.30, MAIN_AXLE_Z + 0.05), P(x, s_c + hi + 0.30, MAIN_AXLE_Z + 0.05)], lambda t: 0.13,
         "Metal", "gearleg", segs=12)
    tube(b, [P(x, leg_s - 0.2, pivot_z + 0.12), P(x, leg_s + 0.2, pivot_z + 0.12)], lambda t: 0.19, "DarkMetal",
         "gearleg", segs=12)
    for _, ds in axles:
        tube(b, [P(x - sx * (dx + 0.05), s_c + ds, MAIN_AXLE_Z), P(x + sx * (dx + 0.05), s_c + ds, MAIN_AXLE_Z)],
             lambda t: 0.09, "DarkMetal", "gearleg", segs=10)
    tube(b, [P(x + sx * 0.2, leg_s - 0.05, min(top - 0.6, 1.55)), P(x + sx * 0.2, s_c + lo + 0.25, MAIN_AXLE_Z + 0.16)],
         lambda t: 0.055, "Metal", "gearleg", segs=8)
    tube(b, [P(x, leg_s, 1.70), P(x, leg_s - 0.30, 1.38), P(x, leg_s - 0.02, 1.10)], lambda t: 0.05, "Metal",
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
        _bogie(b, x, sx, WLG_S, AXLES, WLG_DX, top, WLG_S - 0.1)
        tube(b, [P(x, WLG_S - 0.1, top - 0.7), P(sx * (WLG_X - 1.7), WLG_S + 0.3, top - 0.25)], lambda t: 0.10, "Metal",
             "gearleg", segs=10)
        # body gear: from the belly
        x = sx * BLG_X
        top = Fuselage.bottom_z(BLG_S, BLG_X) + 0.35
        _bogie(b, x, sx, BLG_S, AXLES, BLG_DX, top, BLG_S - 0.2)
    for k, ds in AXLES:
        for prefix, s_c, gx, gdx in (("W", WLG_S, WLG_X, WLG_DX), ("B", BLG_S, BLG_X, BLG_DX)):
            w = MeshBuilder()
            for sx in (1, -1):
                for dx in (-gdx, gdx):
                    wheel(w, P(sx * gx + dx, s_c + ds, MAIN_AXLE_Z), MAIN_R, MAIN_W, "wheel")
            out[f"Wheel_{prefix}{k}"] = w
    # nose gear: the upper strut is fixed, everything below the steering collar turns
    zb = float(Fuselage.zb(NOSE_S))
    tube(b, [P(0, NOSE_S - 0.08, zb + 0.4), P(0, NOSE_S, 1.80)], lambda t: 0.15, "Metal", "nosegear", segs=14)
    tube(nose, [P(0, NOSE_S, 1.86), P(0, NOSE_S, NOSE_AXLE_Z + 0.12)], lambda t: 0.11, "Chrome", "nosegear", segs=14)
    tube(nose, [P(-NOSE_WHEEL_X - 0.05, NOSE_S, NOSE_AXLE_Z), P(NOSE_WHEEL_X + 0.05, NOSE_S, NOSE_AXLE_Z)],
         lambda t: 0.075, "DarkMetal", "nosegear", segs=10)
    tube(nose, [P(0, NOSE_S + 0.03, 1.50), P(0, NOSE_S + 0.28, 1.24), P(0, NOSE_S + 0.04, 1.00)], lambda t: 0.045,
         "Metal", "nosegear", segs=8)
    tube(b, [P(0, NOSE_S - 0.02, 2.15), P(0, NOSE_S - 1.5, float(Fuselage.zb(NOSE_S - 1.5)) + 0.25)], lambda t: 0.08,
         "Metal", "nosegear", segs=10)
    revolve(nose, [(-0.08, 0.001), (-0.06, 0.10), (0.05, 0.105), (0.06, 0.001)], P(0, NOSE_S - 0.17, 1.66), (0, 1, 0),
            "LightLens", "light", segs=12)
    w = MeshBuilder()
    for sx in (1, -1):
        wheel(w, P(sx * NOSE_WHEEL_X, NOSE_S, NOSE_AXLE_Z), NOSE_R, 0.47, "wheel")
    out["Wheel_Nose"] = w
    out["Gear"] = b
    out["NoseGear"] = nose
    return out


# ============================================================== lights, antennas, probes

NAV_S = le_s(WING_TIP - 0.25) + 0.30
BEACON_TOP_S = 37.0
BEACON_BOTTOM_S = 44.0
LANDING = (5.0, 26.9)           # (span, station) under the wing root
LOGO_LIGHT = (3.2, 67.6)        # (span, station) on the stabiliser


def build_details():
    b = MeshBuilder()
    for sx, mat in ((1, "LightRed"), (-1, "LightGreen")):
        # nav light at the raked tip's leading edge, strobe behind it
        y = WING_TIP - 0.25
        tip_z = wing_station(y, h=None)["le"].z
        revolve(b, [(-0.12, 0.001), (-0.07, 0.06), (0.05, 0.065), (0.12, 0.001)],
                (sx * y, Y(NAV_S), tip_z + 0.02), (0, 1, 0), mat, "light", segs=12)
        revolve(b, [(-0.08, 0.001), (-0.04, 0.045), (0.05, 0.045), (0.08, 0.001)],
                (sx * (y - 0.05), Y(NAV_S + 0.6), tip_z + 0.0), (0, 1, 0), "LightWhite", "light", segs=10)
        y, s = LANDING
        revolve(b, [(-0.01, 0.001), (-0.005, 0.16), (0.05, 0.17)], (sx * y, Y(s), wing_surface_z(y, s) - 0.01),
                (0, 0, -1), "LightLens", "light", segs=14, ref=(1, 0, 0))
        x, s = sx * LOGO_LIGHT[0], LOGO_LIGHT[1]
        z = ht_station(x, h=None)["le"].z + 0.12
        revolve(b, [(-0.01, 0.08), (0.04, 0.07), (0.07, 0.001)], P(x, s, z), (0, 0, 1), "LightWhite", "light",
                segs=10, ref=(1, 0, 0))
    s = BEACON_TOP_S
    revolve(b, [(-0.01, 0.13), (0.06, 0.12), (0.13, 0.07), (0.16, 0.001)], P(0, s, float(Fuselage.zt(s)) - 0.02),
            (0, 0, 1), "LightRed", "light", segs=14, ref=(1, 0, 0))
    s = BEACON_BOTTOM_S
    revolve(b, [(-0.01, 0.13), (0.06, 0.12), (0.13, 0.07), (0.16, 0.001)], P(0, s, Fuselage.bottom_z(s, 0) + 0.02),
            (0, 0, -1), "LightRed", "light", segs=14, ref=(1, 0, 0))
    zt = float(Fuselage.zm(Fuselage.S1))
    revolve(b, [(-0.05, 0.001), (-0.03, 0.08), (0.05, 0.08), (0.08, 0.001)], P(0, Fuselage.S1 + 0.02, zt + 0.32),
            (0, 1, 0), "LightWhite", "light", segs=10)
    for s, up, h in ((15.0, 1, 0.48), (41.0, 1, 0.40), (52.0, -1, 0.34), (12.0, -1, 0.30)):
        z = float(Fuselage.zt(s)) if up > 0 else Fuselage.bottom_z(s, 0)
        tube(b, [P(0, s, z - up * 0.02), P(0, s + 0.34, z + up * h)], lambda t: 0.065 * (1 - 0.6 * t), "Dark", "detail",
             segs=6, squash=0.3)
    # satcom fairing on the crown behind the hump
    s0, s1 = 44.0, 47.4
    sat = []
    for s in np.linspace(s0, s1, 9):
        t = (s - s0) / (s1 - s0)
        sat.append((s, 0.33 * math.sin(math.pi * t) ** 0.5 + 0.001))
    zc = float(Fuselage.zt(45.7)) - 0.18
    revolve(b, [(s - s0, r) for s, r in sat], P(0, s0, zc), (0, 1, 0), "Body", "satcom", segs=20)
    # pitot probes on the nose below the flight deck
    for sx, z in ((1, 6.55), (-1, 6.55), (1, 6.15)):
        s = 2.6
        x = sx * Fuselage.side_x(s, z)
        tube(b, [P(x, s, z), P(x + sx * 0.15, s - 0.03, z), P(x + sx * 0.16, s - 0.26, z)], lambda t: 0.016, "Metal",
             "detail", segs=6)
    return b
