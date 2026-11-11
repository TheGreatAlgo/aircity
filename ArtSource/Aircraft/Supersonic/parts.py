"""Supersonic airliner (100 seats, Mach 2 class), built procedurally. An original design, not a copy of any real
type: a slender, slightly waisted (area-ruled) fuselage with a long needle nose, a pitot boom and a fixed flight
deck (no drooping nose); a thin cranked-arrow wing (a 77 deg strake into a 64.5 deg outer panel, a straight
trailing edge and a little anhedral outboard); four afterburning engines in separate nacelles with raked
rectangular intakes under the wing; elevons instead of flaps; a tall swept fin and no tailplane.

Stations `s` are metres aft of the nose tip. Blender Y = s - S_REF, so the model origin sits on the ground under
the main bogies. Nose points -Y, port wing is +X, Z up.
"""
import math

import numpy as np
from mathutils import Vector

from fuselage import Fuselage as BaseFuselage, Loft, profile
from geom import MeshBuilder, airfoil_eval
from shapes import _fd_normal, drape_pane, lifting_surface, revolve, rounded_outline, tube, wheel

NOSE_S = 12.0           # nose gear axle station
S_REF = 43.0            # main bogie centre -> model origin (wheelbase 31 m): far aft, as on any delta


def Y(s):
    return s - S_REF


def P(x, s, z):
    return (x, Y(s), z)


# ============================================================== fuselage

KEEL = 2.95             # fuselage bottom line: the wing is low and its lower skin runs flush with the belly
CROWN = 6.30            # 3.35 m tall, 2.9 m wide: four abreast, one aisle
HALF_WIDTH = 1.45
TIP_H = 1.20            # nose tip height above the keel (z 4.15)
NOSE_W = (14.0, 0.85, 2.5)      # (length, a, b): plan view, centre line -> full width
NOSE_LO = (12.0, 0.80, 2.6)     # side view, tip -> keel


def _rise(s, shape):
    length, a, b = shape
    t = min(max(s / length, 0.0), 1.0)
    return 1.0 - (1.0 - t ** a) ** b


def _nose_samples(length):
    return [0.008] + [length * (1.0 - math.cos(a)) for a in np.linspace(0.06, math.pi / 2, 26)]


# Top line: a long needle that rises slowly, a short steeper windshield step (s 6.0-7.6), then the crown.
NOSE_TOP = [(0.008, 4.152), (0.5, 4.30), (1.0, 4.43), (2.0, 4.66), (3.0, 4.85), (4.5, 5.07), (6.0, 5.27), (6.6, 5.44),
            (7.2, 5.68), (7.8, 5.90), (8.6, 6.08), (9.8, 6.21), (11.5, 6.28), (13.5, CROWN)]


class Fuselage(BaseFuselage):
    S0 = 0.008
    S1 = 61.0
    ML = 14
    MU = 16

    # constant section to s 50, waisted a little across the wing (area rule), then a long upswept tail cone
    zt = profile(NOSE_TOP + [(49.0, CROWN), (52.0, 6.24), (55.0, 6.08), (58.0, 5.86), (60.0, 5.66), (61.0, 5.55)])
    zb = profile([(s, KEEL + TIP_H * (1.0 - _rise(s, NOSE_LO))) for s in _nose_samples(NOSE_LO[0])] +
                 [(50.0, KEEL), (52.0, 3.04), (54.0, 3.32), (56.0, 3.76), (58.0, 4.30), (60.0, 4.82), (61.0, 5.06)])
    hw = profile([(s, HALF_WIDTH * _rise(s, NOSE_W)) for s in _nose_samples(NOSE_W[0])] +
                 [(24.0, HALF_WIDTH), (30.0, 1.41), (36.0, 1.385), (42.0, 1.41), (48.0, HALF_WIDTH), (51.0, 1.43),
                  (54.0, 1.27), (57.0, 0.98), (59.5, 0.62), (61.0, 0.36)])
    fm = profile([(0.008, 0.5), (61.0, 0.5)])
    nt = profile([(0.008, 2.0), (61.0, 2.0)])
    nb = profile([(0.008, 2.0), (16.0, 2.0), (22.0, 2.35), (48.0, 2.35), (54.0, 2.0), (61.0, 2.0)])


def fuselage_stations():
    s = [0.008, 0.02, 0.04, 0.07, 0.11, 0.16, 0.22, 0.30, 0.40, 0.52, 0.66, 0.82, 1.0]
    for stop, step in ((9.0, 0.25), (16.0, 0.5), (50.0, 0.8), (60.8, 0.4)):
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
    loft.fan_cap(last, P(0, F.S1 + 0.12, float(F.zm(F.S1))), s_tip=F.S1 + 0.12)
    # pitot boom out of the nose tip
    o = P(0, 0.0, KEEL + TIP_H)
    revolve(b, [(-0.62, 0.001), (-0.56, 0.02), (-0.46, 0.03), (-0.10, 0.055), (0.30, 0.075)], o, (0, 1, 0), "Metal",
            "detail", segs=10)
    return b


# ============================================================== cockpit windows

def _out_dir(p):
    c = Fuselage.sec(p.y + S_REF)
    return Vector((p.x, 0.0, p.z - c["zm"]))


R_ARC = 1.5


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
    lo, hi = 0.01, 13.0
    for _ in range(50):
        mid = 0.5 * (lo + hi)
        lo, hi = (mid, hi) if float(Fuselage.zt(mid)) < KEEL + h else (lo, mid)
    return 0.5 * (lo + hi)


# Two raked windshield panes either side of a centre post on the windshield step, then two small side windows.
SILL = 2.36     # band bottom above the keel (z 5.31)
HEAD = 2.92     # band top (z 5.87)
WS_FRONT = _top_reaches(SILL)
WS_TOP = _top_reaches(HEAD)
POST = WS_TOP + 0.55
SIDE_1 = POST + 0.12
SIDE_2 = SIDE_1 + 0.78 + 0.12
COCKPIT_WINDOWS = [
    ("windshield", [_at(WS_FRONT, deg=2.2), _at(WS_FRONT + 0.30, SILL), _at(POST - 0.35, SILL), _at(POST, SILL),
                    _at(POST + 0.18, HEAD), _at(WS_TOP + 0.12, HEAD), _at(WS_TOP, deg=2.2)],
     [0.03, 0.08, 0.25, 0.05, 0.05, 0.25, 0.03]),
    ("sidewindow", [_at(SIDE_1, SILL), _at(SIDE_1 + 0.78, SILL), _at(SIDE_1 + 0.78, HEAD - 0.06),
                    _at(SIDE_1 + 0.22, HEAD)], [0.05, 0.12, 0.12, 0.05]),
    ("sidewindow", [_at(SIDE_2, SILL + 0.02), _at(SIDE_2 + 0.50, SILL + 0.06), _at(SIDE_2 + 0.50, HEAD - 0.20),
                    _at(SIDE_2, HEAD - 0.08)], [0.05, 0.10, 0.10, 0.07]),
]


def build_windows():
    b = MeshBuilder()
    for sign in (1, -1):
        surf = skin_surface(sign)
        for tag, corners, radii in COCKPIT_WINDOWS:
            drape_pane(b, _outline(corners, radii), surf, tag=tag, lift=0.008, sink=0.02)
    return b


# ============================================================== wing (cranked arrow)

WING_SIDE = 1.20        # where the chord line meets the fuselage side
CRANK = 3.30            # strake -> outer panel
WING_TIP = 13.0         # span 26.0 m: ICAO code C (24-36 m); the 61.8 m length makes it code D (AircraftType.SizeCode)
LE_S0 = 19.5            # strake apex at the fuselage side
STRAKE_TAN = math.tan(math.radians(77.0))
OUTER_TAN = math.tan(math.radians(64.5))
TE_S = 51.2             # straight trailing edge
WING_Z0 = 3.60          # chord-line height of the leading edge
ANHEDRAL_Y = 7.6        # flat inboard of here (the nacelles), drooping outboard
ANHEDRAL = math.radians(3.5)
WING_AF = "0004"
ELEVON_ROOT, ELEVON_TIP = 2.4, 1.5      # elevon chord: a straight hinge line just ahead of the trailing edge


def le_s(y):
    ay = max(abs(y), WING_SIDE)
    if ay <= CRANK:
        return LE_S0 + (ay - WING_SIDE) * STRAKE_TAN
    return LE_S0 + (CRANK - WING_SIDE) * STRAKE_TAN + (ay - CRANK) * OUTER_TAN


def te_s(y):
    return TE_S


def wing_chord(y):
    return te_s(y) - le_s(y)


def wing_z(y):
    return WING_Z0 - max(abs(y) - ANHEDRAL_Y, 0.0) * math.tan(ANHEDRAL)


def wing_thickness(y):
    """Scale on the 4% section: 3% at the root, 4% at the tip."""
    return float(np.interp(abs(y), [0, WING_SIDE, CRANK, WING_TIP], [0.75, 0.75, 0.8, 1.0]))


def elevon_chord(y):
    t = min(max((abs(y) - WING_SIDE) / (WING_TIP - WING_SIDE), 0.0), 1.0)
    return ELEVON_ROOT + (ELEVON_TIP - ELEVON_ROOT) * t


def wing_hinge(y):
    return 1.0 - elevon_chord(y) / wing_chord(y)


def wing_station(y, h="hinge", scale_t=None):
    ay = abs(y)
    sgn = 1.0 if y >= 0 else -1.0
    c = wing_chord(ay)
    inc = math.radians(0.6 - 1.6 * min(ay / WING_TIP, 1.0))      # washout: 0.6 deg root, -1 deg tip
    chord_dir = Vector((0.0, math.cos(inc), -math.sin(inc)))
    up = Vector((0.0, math.sin(inc), math.cos(inc)))
    le = Vector((sgn * ay, Y(le_s(ay)), wing_z(ay)))
    return dict(le=le, chord_dir=chord_dir, up=up, c=c, h=wing_hinge(ay) if h == "hinge" else h,
                scale_t=wing_thickness(ay) if scale_t is None else scale_t)


def wing_surface_z(y, s, upper=False):
    """z of the wing's lower (or upper) skin at span y, station s."""
    st = wing_station(y, h=None)
    f = min(max((s - le_s(y)) / st["c"], 0.0), 1.0)
    zu, zl, _ = airfoil_eval(WING_AF)(np.array([f]))
    z = (zu[0] if upper else zl[0]) * st["scale_t"] * st["c"]
    return float(st["le"].z + st["chord_dir"].z * f * st["c"] + z * st["up"].z)


def hinge_point(y):
    st = wing_station(y)
    return st["le"] + st["chord_dir"] * (st["c"] * st["h"])


# trailing-edge pieces per side: (name, from, to); "filler" pieces are fixed (above the nacelles' nozzles)
TE_PIECES = [("filler", 0.60, 1.50), ("Elevator_{}", 1.50, 3.75), ("filler", 3.75, 7.60), ("Aileron_{}_In", 7.60, 10.05),
             ("filler", 10.05, 10.15), ("Aileron_{}_Out", 10.15, 12.35)]
TIP_START = 12.38


def build_wing():
    """Returns {"Wing": fixed MeshBuilder, "Elevator_L": ..., "Aileron_R_Out": ...}."""
    ys = [0.0, WING_SIDE, CRANK, ANHEDRAL_Y, TIP_START] + [v for _, a, b in TE_PIECES for v in (a, b)]
    ys += list(np.linspace(WING_SIDE, CRANK, 4)) + list(np.linspace(CRANK, ANHEDRAL_Y, 6)) + \
        list(np.linspace(ANHEDRAL_Y, TIP_START, 6))
    ys = sorted(set(round(sg * v, 4) for v in ys for sg in (1, -1) if v <= TIP_START))
    wing = MeshBuilder()
    lifting_surface(wing, [wing_station(y) for y in ys], WING_AF, "Body", "wing", flip=True, n_af=26)
    out = {"Wing": wing}
    for sgn, side in ((1, "L"), (-1, "R")):
        tip = [wing_station(sgn * y, h=None, scale_t=t) for y, t in
               ((TIP_START - 0.03, 1.0), (WING_TIP - 0.3, 1.0), (WING_TIP - 0.08, 0.8), (WING_TIP, 0.45))]
        lifting_surface(wing, tip, WING_AF, "Body", "wing", flip=(sgn > 0), n_af=26)
        for name, y0, y1 in TE_PIECES:
            yy = np.linspace(y0, y1, max(int((y1 - y0) / 0.8) + 2, 2))
            target = wing if name == "filler" else out.setdefault(name.format(side), MeshBuilder())
            tag = "wing_filler" if name == "filler" else "wing_elevon"
            lifting_surface(target, [wing_station(sgn * y) for y in yy], WING_AF, "Body", tag, part="ctrl",
                            flip=(sgn > 0))
    return out


# ============================================================== engines

# (span, intake lip station, nozzle exit station): four separate nacelles, the outer pair a metre further aft
NACELLES = [(4.62, 38.6, 52.7), (6.72, 39.6, 53.1)]
NAC_HW = 0.74           # nacelle half width
NAC_BOTTOM = 1.62       # nacelle bottom line ahead of the boat-tail
NOZZLE_Z = 2.90         # nozzle centre: its top meets the wing's trailing edge
NOZZLE_R = 0.66         # where the nacelle turns round (the joint)
EXIT_R = 0.60           # afterburner nozzle exit
NOZZLE_LEN = 1.35       # joint -> exit
INTAKE_RAKE = 0.65      # the top lip (under the wing) stands this far ahead of the bottom lip
LIP = 0.06              # intake lip thickness
SEGS = 40


def _nacelle_section(x, s, lip_s, joint_s):
    """(centre z, half height up, half height down, half width, superellipse exponent) at station s."""
    top_box = wing_surface_z(x, min(s, TE_S - 0.4)) + 0.16
    box = (0.5 * (top_box + NAC_BOTTOM), 0.5 * (top_box - NAC_BOTTOM), NAC_HW, 3.6)
    t = float(np.clip((s - (joint_s - 4.2)) / 4.2, 0.0, 1.0))
    t = t * t * (3 - 2 * t)
    zc = box[0] + (NOZZLE_Z - box[0]) * t
    top = box[0] + box[1] + (NOZZLE_Z + NOZZLE_R - box[0] - box[1]) * t
    if s < TE_S:
        top = max(top, wing_surface_z(x, s) + 0.06)      # stays buried in the wing up to the trailing edge
    bottom = NAC_BOTTOM + (NOZZLE_Z - NOZZLE_R - NAC_BOTTOM) * t
    zc = 0.5 * (top + bottom) if t < 1.0 else NOZZLE_Z
    hw = NAC_HW + (NOZZLE_R - NAC_HW) * t
    n = 3.6 + (2.0 - 3.6) * t
    return zc, top - zc, zc - bottom, hw, n


def _ring(x, s, sec, inset=0.0, rake_s=None):
    zc, hu, hd, hw, n = sec
    hu, hd, hw = hu - inset, hd - inset, hw - inset
    pts = []
    for k in range(SEGS):
        a = 2 * math.pi * k / SEGS
        c, sn = math.cos(a), math.sin(a)
        dx = hw * math.copysign(abs(c) ** (2.0 / n), c)
        dz = (hu if sn >= 0 else hd) * math.copysign(abs(sn) ** (2.0 / n), sn)
        z = zc + dz
        ss = s
        if rake_s is not None:
            # rake: the intake's top lip leans forward (a ramp intake under the wing)
            up = (z - (zc - hd)) / max(hu + hd, 1e-6)
            ss = s - INTAKE_RAKE * up * max(0.0, 1.0 - (s - rake_s) / 2.0)
        pts.append(P(x + dx, ss, z))
    return pts


def _loft(b, rings, mat, tag, flip=False):
    n = len(rings[0])
    base = b.add_verts([p for r in rings for p in r])
    for i in range(len(rings) - 1):
        for k in range(n):
            k2 = (k + 1) % n
            q = [base + i * n + k, base + i * n + k2, base + (i + 1) * n + k2, base + (i + 1) * n + k]
            b.add_face(q[::-1] if flip else q, mat, None, tag)
    return base


def build_engine(x, lip_s, exit_s):
    """One nacelle: raked rectangular intake, a box under the wing that turns round into a variable nozzle."""
    b = MeshBuilder()
    joint_s = exit_s - NOZZLE_LEN
    stations = [lip_s, lip_s + 0.12, lip_s + 0.35, lip_s + 0.8, lip_s + 1.5, lip_s + 2.4]
    s = stations[-1]
    while s < joint_s - 4.6:
        s += 1.2
        stations.append(s)
    stations += list(np.linspace(joint_s - 4.2, joint_s, 9))
    stations = sorted(set(round(v, 4) for v in stations if v <= joint_s))
    outer = [_ring(x, st, _nacelle_section(x, st, lip_s, joint_s), rake_s=lip_s) for st in stations]
    # the cowl's first ring sits on the lip; a short sharp lip turns into the duct
    _loft(b, outer, "Body", "nacelle", flip=True)
    duct_st = [lip_s, lip_s + 0.12, lip_s + 0.35, lip_s + 0.8, lip_s + 1.5, lip_s + 2.4]
    inner = [_ring(x, st, _nacelle_section(x, st, lip_s, joint_s), inset=LIP + 0.02 * min(k, 3), rake_s=lip_s)
             for k, st in enumerate(duct_st)]
    _loft(b, [outer[0], inner[0]], "Metal", "intake")
    _loft(b, inner, "Dark", "intake")
    # compressor face, deep in the duct
    face_c = Vector(P(x, duct_st[-1], _nacelle_section(x, duct_st[-1], lip_s, joint_s)[0]))
    base = b.add_vert(face_c)
    last = len(inner) - 1
    n = SEGS
    first = b.add_verts(inner[last])
    for k in range(n):
        b.add_face([base, first + k, first + (k + 1) % n], "Dark", None, "fanface")
    # variable nozzle: a slightly converging dark-metal shroud with a ring of flaps' edges, dark inside, and the
    # afterburner's flame holder rings deep inside
    o = P(x, joint_s, NOZZLE_Z)
    L = NOZZLE_LEN
    revolve(b, [(-0.02, NOZZLE_R), (0.25, NOZZLE_R - 0.005), (0.55, NOZZLE_R - 0.02), (L - 0.12, EXIT_R + 0.01),
                (L, EXIT_R)], o, (0, 1, 0), "DarkMetal", "core", segs=SEGS)
    revolve(b, [(L, EXIT_R), (L - 0.03, EXIT_R - 0.05), (0.2, EXIT_R - 0.07)], o, (0, 1, 0), "DarkMetal", "core",
            segs=SEGS)
    revolve(b, [(0.2, EXIT_R - 0.07), (0.15, 0.38), (0.12, 0.30), (0.10, 0.12), (0.08, 0.001)], o, (0, 1, 0), "Dark",
            "core", segs=24)
    for r0 in (0.42, 0.24):
        revolve(b, [(0.28, r0 - 0.03), (0.30, r0), (0.28, r0 + 0.03)], o, (0, 1, 0), "Metal", "core", segs=24,
                flip=True)
    # flap seams on the shroud: thin ridges every 30 degrees
    for k in range(12):
        a = 2 * math.pi * (k + 0.5) / 12
        d = Vector((math.cos(a), 0.0, math.sin(a)))
        p0 = Vector(o) + Vector((0, 0.55, 0)) + d * (NOZZLE_R - 0.01)
        p1 = Vector(o) + Vector((0, L - 0.05, 0)) + d * (EXIT_R + 0.004)
        tube(b, [p0, p1], lambda t: 0.012, "Metal", "core", segs=4, cap=False)
    return b


# ============================================================== fin (no tailplane)

VT_ROOT_Z = 6.0         # full root chord here, at the crown
VT_BOTTOM = 5.40        # lofted from inside the tail cone
VT_TOP_Z = 12.6
VT_ROOT_LE = 46.0
VT_TAN = math.tan(math.radians(58.0))
VT_ROOT_C = 13.6
VT_TIP_C = 3.0
VT_HINGE = 0.74
RUDDER = (6.55, 12.25)


def vt_le_s(z):
    return VT_ROOT_LE + (z - VT_ROOT_Z) * VT_TAN


def vt_chord(z):
    return VT_ROOT_C - (z - VT_ROOT_Z) / (VT_TOP_Z - VT_ROOT_Z) * (VT_ROOT_C - VT_TIP_C)


def vt_station(z, h=VT_HINGE, scale_t=1.0):
    return dict(le=Vector((0.0, Y(vt_le_s(z)), z)), chord_dir=Vector((0, 1, 0)), up=Vector((1, 0, 0)), c=vt_chord(z),
                h=h, scale_t=scale_t)


def vt_hinge(z):
    st = vt_station(z)
    return st["le"] + st["chord_dir"] * (st["c"] * st["h"])


def build_vtail():
    out = {}
    fin = MeshBuilder()
    zs = sorted(set([VT_BOTTOM, VT_ROOT_Z, *RUDDER, *np.linspace(VT_ROOT_Z, VT_TOP_Z - 0.35, 9)]))
    lifting_surface(fin, [vt_station(z) for z in zs], "0004", "Body", "vtail")
    caps = [vt_station(z, h=None, scale_t=t) for z, t in ((VT_TOP_Z - 0.38, 1.0), (VT_TOP_Z - 0.12, 0.8),
                                                         (VT_TOP_Z, 0.4))]
    lifting_surface(fin, caps, "0004", "Body", "vtail")
    # dorsal fillet: the crown rises into the fin's leading edge
    s0, s1 = 40.5, vt_le_s(VT_ROOT_Z + 0.75) + 0.2
    rings = []
    for s in np.linspace(s0, s1, 12):
        t = (s - s0) / (s1 - s0)
        zt = float(Fuselage.zt(s))
        ztop = zt + 0.005 + (VT_ROOT_Z + 0.75 - zt) * t ** 2.4
        wb = 0.08 + 0.22 * math.sin(math.pi * min(t * 1.2, 1.0)) ** 0.6
        wt = 0.015 + 0.08 * t
        port = []
        for hh in (0.0, 0.25, 0.55, 0.8, 1.0):
            px = wb * (1 - hh) ** 2.4 + wt * hh * (1 - hh * 0.35)
            port.append((px, Fuselage.top_z(s, wb) - 0.03 + (ztop - Fuselage.top_z(s, wb) + 0.03) * hh))
        rings.append([P(px, s, pz) for px, pz in port] + [P(0.0, s, ztop + wt * 0.6)] +
                     [P(-px, s, pz) for px, pz in port[::-1]])
    n = len(rings[0])
    base = fin.add_verts([p for r in rings for p in r])
    for i in range(len(rings) - 1):
        for j in range(n - 1):
            fin.add_face([base + (i + 1) * n + j, base + (i + 1) * n + j + 1, base + i * n + j + 1, base + i * n + j],
                         "Body", None, "dorsal")
    fin.add_face([base + j for j in range(n)], "Body", None, "dorsal")
    out["VStab"] = fin
    rud = MeshBuilder()
    lifting_surface(rud, [vt_station(z) for z in np.linspace(*RUDDER, 8)], "0004", "Body", "rudder", part="ctrl")
    out["Rudder"] = rud
    return out


# ============================================================== landing gear (tall: the wing sits high off the ground)

NOSE_R = 0.42
NOSE_AXLE_Z = NOSE_R - 0.02
NOSE_WHEEL_X = 0.34
MAIN_X = 2.75           # leg (main-gear track 5.5 m), inboard of the nacelles
MAIN_WHEEL_DX = 0.42
BOGIE_DS = 0.82
MAIN_R = 0.60
MAIN_AXLE_Z = MAIN_R - 0.03
LEG_S = S_REF - 0.10
MAIN_AXLES = [(1, -BOGIE_DS), (2, BOGIE_DS)]


def build_gear():
    """Returns {"Gear": fixed legs, "NoseGear": steering part, "Wheel_*": spinning bogie axles}."""
    b, nose, out = MeshBuilder(), MeshBuilder(), {}
    for sx, side in ((1, "L"), (-1, "R")):
        x = sx * MAIN_X
        top = wing_surface_z(MAIN_X, LEG_S) + 0.25
        pivot_z = MAIN_AXLE_Z + 0.12
        tube(b, [P(x, LEG_S - 0.15, top), P(x, LEG_S, 1.55)], lambda t: 0.21, "Metal", "gearleg", segs=16)
        tube(b, [P(x, LEG_S, 1.62), P(x, LEG_S, pivot_z + 0.1)], lambda t: 0.13, "Chrome", "gearleg", segs=16)
        tube(b, [P(x, S_REF - BOGIE_DS - 0.28, MAIN_AXLE_Z + 0.04), P(x, S_REF + BOGIE_DS + 0.28, MAIN_AXLE_Z + 0.04)],
             lambda t: 0.11, "Metal", "gearleg", segs=12)
        tube(b, [P(x, LEG_S - 0.2, pivot_z + 0.12), P(x, LEG_S + 0.2, pivot_z + 0.12)], lambda t: 0.16, "DarkMetal",
             "gearleg", segs=12)
        for ds in (-BOGIE_DS, BOGIE_DS):
            tube(b, [P(x - sx * (MAIN_WHEEL_DX + 0.05), S_REF + ds, MAIN_AXLE_Z),
                     P(x + sx * (MAIN_WHEEL_DX + 0.05), S_REF + ds, MAIN_AXLE_Z)], lambda t: 0.085, "DarkMetal",
                 "gearleg", segs=10)
        # side brace up to the wing root, drag brace forward, torque link
        tube(b, [P(x, LEG_S + 0.05, 2.55), P(sx * 1.35, LEG_S + 0.3, float(Fuselage.zb(LEG_S)) + 0.35)],
             lambda t: 0.08, "Metal", "gearleg", segs=10)
        tube(b, [P(x, LEG_S, 2.2), P(x, LEG_S - 1.6, top - 0.05)], lambda t: 0.075, "Metal", "gearleg", segs=10)
        tube(b, [P(x, LEG_S, 1.45), P(x, LEG_S - 0.28, 1.18), P(x, LEG_S - 0.02, 0.95)], lambda t: 0.045, "Metal",
             "gearleg", segs=8)
        for k, ds in MAIN_AXLES:
            w = MeshBuilder()
            for dx in (-MAIN_WHEEL_DX, MAIN_WHEEL_DX):
                wheel(w, P(x + sx * dx, S_REF + ds, MAIN_AXLE_Z), MAIN_R, 0.40, "wheel")
            out[f"Wheel_{side}{k}"] = w
    # nose gear: a long strut (the nose stands high); everything below the steering collar turns
    zb = float(Fuselage.zb(NOSE_S))
    tube(b, [P(0, NOSE_S - 0.08, zb + 0.4), P(0, NOSE_S, 1.75)], lambda t: 0.12, "Metal", "nosegear", segs=14)
    tube(nose, [P(0, NOSE_S, 1.82), P(0, NOSE_S, NOSE_AXLE_Z + 0.12)], lambda t: 0.085, "Chrome", "nosegear", segs=14)
    tube(nose, [P(-NOSE_WHEEL_X - 0.05, NOSE_S, NOSE_AXLE_Z), P(NOSE_WHEEL_X + 0.05, NOSE_S, NOSE_AXLE_Z)],
         lambda t: 0.07, "DarkMetal", "nosegear", segs=10)
    tube(nose, [P(0, NOSE_S + 0.03, 1.35), P(0, NOSE_S + 0.26, 1.12), P(0, NOSE_S + 0.04, 0.90)], lambda t: 0.04,
         "Metal", "nosegear", segs=8)
    tube(b, [P(0, NOSE_S + 0.02, 2.15), P(0, NOSE_S + 1.4, float(Fuselage.zb(NOSE_S + 1.4)) + 0.25)], lambda t: 0.06,
         "Metal", "nosegear", segs=10)
    revolve(nose, [(-0.07, 0.001), (-0.05, 0.08), (0.05, 0.085), (0.06, 0.001)], P(0, NOSE_S - 0.14, 1.45), (0, 1, 0),
            "LightLens", "light", segs=12)
    for side, sx in (("L", 1), ("R", -1)):
        w = MeshBuilder()
        wheel(w, P(sx * NOSE_WHEEL_X, NOSE_S, NOSE_AXLE_Z), NOSE_R, 0.26, "wheel")
        out["Wheel_Nose_" + side] = w
    out["Gear"] = b
    out["NoseGear"] = nose
    return out


# ============================================================== lights, antennas

NAV_S = le_s(WING_TIP) + 0.35
BEACON_TOP_S = 30.0
BEACON_BOTTOM_S = 35.0
LANDING = (2.15, 0.6)           # (span, metres behind the strake's leading edge) under the strake
LOGO_LIGHT = (2.6, 46.0)        # (span, station) on the wing's upper skin, aimed at the fin


def landing_point(sx):
    y, back = LANDING
    s = le_s(y) + back
    return (sx * y, Y(s), wing_surface_z(y, s) - 0.01), s


def build_details():
    b = MeshBuilder()
    tip_z = wing_station(WING_TIP, h=None)["le"].z
    for sx, mat in ((1, "LightRed"), (-1, "LightGreen")):
        # nav light at the wingtip's leading edge, strobe behind it
        revolve(b, [(-0.10, 0.001), (-0.06, 0.05), (0.05, 0.055), (0.10, 0.001)],
                (sx * (WING_TIP + 0.02), Y(NAV_S), tip_z + 0.0), (0, 1, 0), mat, "light", segs=12)
        revolve(b, [(-0.06, 0.001), (-0.03, 0.04), (0.04, 0.04), (0.06, 0.001)],
                (sx * (WING_TIP + 0.02), Y(NAV_S + 0.45), tip_z - 0.01), (0, 1, 0), "LightWhite", "light", segs=10)
        # landing light under the strake
        pos, s = landing_point(sx)
        revolve(b, [(-0.01, 0.001), (-0.005, 0.11), (0.04, 0.12)], pos, (0, 0, -1), "LightLens", "light", segs=14,
                ref=(1, 0, 0))
        # logo light on the wing, aimed at the fin
        x, s = sx * LOGO_LIGHT[0], LOGO_LIGHT[1]
        revolve(b, [(-0.01, 0.06), (0.03, 0.05), (0.05, 0.001)], P(x, s, wing_surface_z(LOGO_LIGHT[0], s, True)),
                (0, 0, 1), "LightWhite", "light", segs=10, ref=(1, 0, 0))
    # beacons on the crown and belly, white light in the tail cone's tip
    s = BEACON_TOP_S
    revolve(b, [(-0.01, 0.10), (0.05, 0.09), (0.10, 0.05), (0.12, 0.001)], P(0, s, float(Fuselage.zt(s)) - 0.02),
            (0, 0, 1), "LightRed", "light", segs=14, ref=(1, 0, 0))
    s = BEACON_BOTTOM_S
    revolve(b, [(-0.01, 0.10), (0.05, 0.09), (0.10, 0.05), (0.12, 0.001)], P(0, s, Fuselage.bottom_z(s, 0) + 0.02),
            (0, 0, -1), "LightRed", "light", segs=14, ref=(1, 0, 0))
    revolve(b, [(-0.04, 0.001), (-0.02, 0.06), (0.04, 0.06), (0.06, 0.001)],
            P(0, Fuselage.S1 + 0.08, float(Fuselage.zm(Fuselage.S1))), (0, 1, 0), "LightWhite", "light", segs=10)
    # antennas on the crown and belly
    for s, up, h in ((16.0, 1, 0.30), (27.0, 1, 0.26), (22.0, -1, 0.22)):
        z = float(Fuselage.zt(s)) if up > 0 else Fuselage.bottom_z(s, 0)
        tube(b, [P(0, s, z - up * 0.02), P(0, s + 0.22, z + up * h)], lambda t: 0.04 * (1 - 0.6 * t), "Dark", "detail",
             segs=6, squash=0.3)
    # angle-of-attack vanes either side of the nose
    for sx in (1, -1):
        s, z = 3.2, KEEL + TIP_H + 0.05
        x = sx * Fuselage.side_x(s, z)
        tube(b, [P(x, s, z), P(x + sx * 0.10, s - 0.04, z), P(x + sx * 0.11, s - 0.18, z + 0.02)], lambda t: 0.012,
             "Metal", "detail", segs=6)
    return b
