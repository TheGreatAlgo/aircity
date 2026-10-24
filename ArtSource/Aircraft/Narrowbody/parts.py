"""Narrowbody twinjet (A320-class proportions), built procedurally.

Stations `s` are metres aft of the nose tip. Blender Y = s - S_REF, so the model origin sits on the ground
under the main-gear axles. Nose points -Y, port wing is +X, Z up.
"""
import math

import numpy as np
from mathutils import Vector

from fuselage import Fuselage as BaseFuselage, Loft, profile
from geom import MeshBuilder
from shapes import _fd_normal, drape_pane, lifting_surface, revolve, rounded_outline, tube, wheel

S_REF = 17.74         # main gear axle station -> model origin


def Y(s):
    return s - S_REF


def P(x, s, z):
    return (x, Y(s), z)


# ============================================================== fuselage

KEEL = 1.91             # fuselage bottom line (constant section)
CROWN = 6.09
HALF_WIDTH = 1.975
TIP_H = 1.17            # radome tip height above the keel: 28% of the fuselage height, as on the A220

# A220 nose, fitted to side-on photos. Each profile rises from the tip (0) to the constant section (1) as
# 1 - (1 - t^a)^b, t = s / length: a < 1 gives the pointed tip, b sets how soon the line levels off. The top line
# climbs almost straight from the tip to the windshield and reaches the crown ~2 fuselage heights back.
NOSE_UP = (9.4, 0.97, 3.45)     # (length, a, b): side view, tip -> crown
NOSE_LO = (8.5, 0.69, 3.44)     # side view, tip -> keel
NOSE_W = (8.0, 0.80, 3.00)      # plan view, centre line -> full width


def _rise(s, shape):
    length, a, b = shape
    t = min(max(s / length, 0.0), 1.0)
    return 1.0 - (1.0 - t ** a) ** b


def _nose_samples(length):
    """Stations for sampling a nose profile, dense at the tip where it curves fastest."""
    return [0.008] + [length * (1.0 - math.cos(a)) for a in np.linspace(0.06, math.pi / 2, 26)]


class Fuselage(BaseFuselage):
    S0 = 0.008
    S1 = 37.50
    ML = 16
    MU = 16

    zt = profile([(s, KEEL + TIP_H + (CROWN - KEEL - TIP_H) * _rise(s, NOSE_UP)) for s in _nose_samples(NOSE_UP[0])] +
                 [(27.0, 6.09), (29.0, 6.06), (31.0, 5.98), (33.0, 5.82), (35.0, 5.58), (36.5, 5.32), (37.5, 5.08)])
    zb = profile([(s, KEEL + TIP_H * (1.0 - _rise(s, NOSE_LO))) for s in _nose_samples(NOSE_LO[0])] +
                 [(11.5, 1.90), (13.0, 1.82), (14.5, 1.72), (16.5, 1.70), (19.5, 1.74), (21.0, 1.84), (22.5, 1.90),
                  (26.0, 1.91), (27.5, 2.00), (29.0, 2.22), (31.0, 2.63), (33.0, 3.12), (35.0, 3.72), (36.5, 4.25),
                  (37.5, 4.62)])
    hw = profile([(s, HALF_WIDTH * _rise(s, NOSE_W)) for s in _nose_samples(NOSE_W[0])] +
                 [(12.5, 1.975), (14.5, 2.03), (17.0, 2.05), (19.5, 2.03), (21.5, 1.975), (26.5, 1.975), (28.5, 1.93),
                  (30.5, 1.78), (32.5, 1.53), (34.5, 1.18), (36.0, 0.82), (37.5, 0.38)])
    fm = profile([(0.008, 0.5), (12.5, 0.5), (14.5, 0.45), (19.5, 0.45), (21.5, 0.5), (37.5, 0.52)])
    nt = profile([(0.008, 2.0), (37.5, 2.0)])
    nb = profile([(0.008, 2.0), (12.5, 2.0), (14.5, 2.8), (19.5, 2.8), (21.5, 2.0), (37.5, 2.0)])


def fuselage_stations():
    s = [0.008, 0.02, 0.04, 0.07, 0.11, 0.16, 0.22, 0.30, 0.40, 0.52, 0.66, 0.82, 1.0]
    for stop, step in ((6.0, 0.2), (12.0, 0.6), (22.0, 0.5), (26.5, 1.0), (37.2, 0.3)):
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
    revolve(b, [(-0.05, 0.17), (0.12, 0.17), (0.12, 0.13), (-0.02, 0.13)], P(0, F.S1 + 0.02, float(F.zm(F.S1)) + 0.02),
            (0, 1, 0), "DarkMetal", "apu", segs=16)
    return b


# ============================================================== cockpit windows

def _out_dir(p):
    c = Fuselage.sec(p.y + S_REF)
    return Vector((p.x, 0.0, p.z - c["zm"]))


R_ARC = 1.8     # window outlines are laid out in (station, phi * R_ARC): roughly metres along the skin


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


# A220 flight deck glazing (port side; mirrored to starboard): four windows in a level band, set back on the pointed
# nose. A wrap-around windshield pane either side of a thin centre post, a raked post, then one big side window with
# well-rounded rear corners. The sill is level, so the windshield's lower edge follows the nose round from the centre
# line (where the top line passes sill height) to the post. Posts are ~0.1 m wide.
SILL = 2.40     # band bottom, metres above the keel
HEAD = 3.22     # band top
COCKPIT_WINDOWS = [
    ("windshield", [_at(1.30, deg=2.0), _at(1.55, SILL), _at(2.05, SILL), _at(2.62, SILL), _at(2.92, HEAD),
                    _at(2.70, HEAD), _at(2.56, deg=2.0)],
     [0.03, 0.08, 0.30, 0.06, 0.05, 0.30, 0.03]),
    ("sidewindow", [_at(2.72, SILL), _at(3.85, SILL), _at(3.85, HEAD), _at(3.02, HEAD)], [0.06, 0.20, 0.20, 0.06]),
]


def build_windows():
    b = MeshBuilder()
    for sign in (1, -1):
        surf = skin_surface(sign)
        for tag, corners, radii in COCKPIT_WINDOWS:
            drape_pane(b, _outline(corners, radii), surf, tag=tag, lift=0.008, sink=0.02)
    return b


# ============================================================== wing

WING_SIDE = 2.0         # fuselage side (span station of the wing root)
WING_KINK = 6.4         # trailing-edge kink: unswept inboard, swept outboard
WING_TIP = 16.85        # span with sharklets: 35.8 m (ICAO code C)
LE_S0 = 13.2            # leading edge station at the fuselage side
LE_TAN = math.tan(math.radians(27.5))
TE_S_IN = 20.6
TE_S_TIP = 22.6
WING_Z0 = 2.45          # chord-line height of the leading edge at the fuselage side
DIHEDRAL = math.radians(5.1)
WING_HINGE = 0.76       # rear-spar (flap / aileron hinge) as a fraction of the local chord
WING_AF = "2415"


def le_s(y):
    return LE_S0 + (abs(y) - WING_SIDE) * LE_TAN


def te_s(y):
    ay = abs(y)
    if ay <= WING_KINK:
        return TE_S_IN
    return TE_S_IN + (ay - WING_KINK) * (TE_S_TIP - TE_S_IN) / (WING_TIP - WING_KINK)


def wing_chord(y):
    return te_s(y) - le_s(y)


def wing_z(y):
    return WING_Z0 + (abs(y) - WING_SIDE) * math.tan(DIHEDRAL)


def wing_thickness(y):
    return float(np.interp(abs(y), [0, WING_SIDE, WING_KINK, WING_TIP], [1.0, 1.0, 0.76, 0.66]))


def wing_station(y, h=WING_HINGE, scale_t=None, c_override=None):
    ay = abs(y)
    sgn = 1.0 if y >= 0 else -1.0
    c = c_override or wing_chord(ay)
    inc = math.radians(2.5 - 3.0 * min(ay / WING_TIP, 1.0))      # 2.5 deg root -> -0.5 deg tip washout
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


# trailing-edge pieces per side: (name, from, to); "filler" pieces are fixed
TE_PIECES = [("Flap_{}_In", 2.06, 6.34), ("filler", 6.34, 6.46), ("Flap_{}_Out", 6.46, 12.54), ("filler", 12.54, 12.66),
             ("Aileron_{}", 12.66, 16.21)]
TIP_START = 16.25


def build_wing():
    """Returns {"Wing": fixed MeshBuilder, "Flap_L_In": ..., "Aileron_R": ...}."""
    ys = sorted(set([round(v, 4) for v in
                     list(np.linspace(-TIP_START, -WING_KINK, 9)) + list(np.linspace(-WING_KINK, WING_KINK, 9)) +
                     list(np.linspace(WING_KINK, TIP_START, 9)) + [-WING_SIDE, WING_SIDE]]))
    wing = MeshBuilder()
    lifting_surface(wing, [wing_station(y) for y in ys], WING_AF, "Body", "wing", flip=True)
    out = {"Wing": wing}
    for sgn, side in ((1, "L"), (-1, "R")):
        tip = [wing_station(sgn * y, h=None) for y in np.linspace(TIP_START - 0.03, WING_TIP, 4)]
        lifting_surface(wing, tip, WING_AF, "Body", "wing", flip=(sgn > 0))
        for name, y0, y1 in TE_PIECES:
            yy = np.linspace(y0, y1, max(int((y1 - y0) / 0.9) + 2, 2))
            target = wing if name == "filler" else out.setdefault(name.format(side), MeshBuilder())
            tag = "wing_filler" if name == "filler" else "wing_" + name.split("_")[0].lower()
            lifting_surface(target, [wing_station(sgn * y) for y in yy], WING_AF, "Body", tag, part="ctrl",
                            flip=(sgn > 0))
    return out


def build_sharklets():
    """Blended winglets: the tip section curls up through a quarter bend, then leans 15 deg outboard."""
    b = MeshBuilder()
    tip = wing_station(WING_TIP, h=None)
    z0 = tip["le"].z
    R, lean = 0.55, math.radians(75)
    path = []
    for phi in np.linspace(0, lean, 6):
        path.append((R * math.sin(phi), R * (1 - math.cos(phi)), phi))
    x1, z1 = path[-1][0], path[-1][1]
    for d in (0.6, 1.1, 1.6, 2.0):
        path.append((x1 + d * math.cos(lean), z1 + d * math.sin(lean), lean))
    seg = [0.0] + [math.hypot(path[k][0] - path[k - 1][0], path[k][1] - path[k - 1][1]) for k in range(1, len(path))]
    u = np.cumsum(seg) / sum(seg)
    for sgn in (1, -1):
        sts = []
        for (dx, dz, phi), t in zip(path, u):
            c = tip["c"] - (tip["c"] - 0.55) * t**0.8
            le_shift = 1.55 * t**1.2
            up = Vector((-sgn * math.sin(phi), 0.0, math.cos(phi)))
            sts.append(dict(le=Vector((sgn * (WING_TIP + dx), Y(le_s(WING_TIP) + le_shift), z0 + dz)),
                            chord_dir=Vector((0, 1, 0)), up=up, c=c, h=None, scale_t=tip["scale_t"] * (1 - 0.3 * t)))
        lifting_surface(b, sts, WING_AF, "Body", "sharklet", flip=(sgn > 0))
    return b


# ============================================================== engines

ENGINE_X = 5.75
ENGINE_Z = 1.62
INLET_S = 10.9
NACELLE = [(0.0, 0.90), (0.03, 0.955), (0.10, 0.99), (0.25, 1.02), (0.6, 1.045), (1.3, 1.055), (2.0, 1.04),
           (2.7, 0.99), (3.3, 0.90), (3.75, 0.80)]


def build_engine(sign):
    """Returns (static nacelle + pylon, spinning fan)."""
    b = MeshBuilder()
    x = sign * ENGINE_X
    o = P(x, INLET_S, ENGINE_Z)
    revolve(b, NACELLE, o, (0, 1, 0), "Body", "nacelle", segs=40)
    # intake lining down to the fan face, and the fan-duct exit behind the cowl
    revolve(b, [(0.0, 0.90), (0.05, 0.865), (0.25, 0.845), (0.85, 0.835)], o, (0, 1, 0), "Metal", "intake", segs=40, flip=True)
    revolve(b, [(0.85, 0.835), (0.86, 0.24)], o, (0, 1, 0), "Dark", "fanface", segs=40, flip=True)
    revolve(b, [(3.75, 0.80), (3.70, 0.74), (3.45, 0.70)], o, (0, 1, 0), "DarkMetal", "fanduct", segs=40, flip=True)
    # core cowl, core nozzle and plug
    revolve(b, [(3.45, 0.70), (3.8, 0.66), (4.3, 0.58), (4.65, 0.50)], o, (0, 1, 0), "Metal", "core", segs=32)
    revolve(b, [(4.65, 0.50), (4.62, 0.44), (4.40, 0.42)], o, (0, 1, 0), "DarkMetal", "core", segs=32, flip=True)
    revolve(b, [(4.40, 0.40), (4.75, 0.33), (5.05, 0.18), (5.20, 0.02)], o, (0, 1, 0), "DarkMetal", "plug", segs=24)
    # pylon: a thin blade from inside the nacelle top up into the wing
    sts = []
    for z, le, te in ((ENGINE_Z + 0.55, INLET_S + 1.5, 16.4), (ENGINE_Z + 1.0, INLET_S + 1.2, 16.9),
                      (wing_surface_z(ENGINE_X, 17.0) + 0.25, le_s(ENGINE_X) + 0.25, 19.0)):
        sts.append(dict(le=Vector((x, Y(le), z)), chord_dir=Vector((0, 1, 0)), up=Vector((1, 0, 0)), c=te - le, h=None,
                        scale_t=0.32 / ((te - le) * 0.10)))
    lifting_surface(b, sts, "0010", "Body", "pylon")

    fan = MeshBuilder()
    hub = Vector(P(x, INLET_S + 0.8, ENGINE_Z))
    prof = [(0.38 + 0.42 * (k / 10), 0.25 * (1 - (1 - k / 10) ** 2.2) ** 0.5 + 0.001) for k in range(11)]
    revolve(fan, [(t - 0.8, r) for t, r in prof], hub, (0, 1, 0), "Metal", "spinner", segs=24)
    n_blades = 18
    for k in range(n_blades):
        a0 = 2 * math.pi * k / n_blades
        radial = Vector((math.cos(a0), 0, math.sin(a0)))
        tangential = Vector((-math.sin(a0), 0, math.cos(a0)))
        axial = Vector((0, 1, 0))
        rings = []
        for r in np.linspace(0.24, 0.83, 6):
            twist = math.radians(55 - 30 * (r - 0.24) / 0.59)
            half = 0.07 + 0.03 * (r - 0.24) / 0.59
            chord = tangential * math.cos(twist) + axial * math.sin(twist)
            thick = chord.cross(radial).normalized() * 0.008
            c = hub + radial * r
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

HT_Z = 4.72
HT_ROOT_LE = 31.6
HT_SPAN = 6.2
HT_HINGE = 0.70
HT_DIHEDRAL = math.radians(6.0)


def ht_station(x, h=HT_HINGE, scale_t=1.0):
    ax = abs(x)
    le = HT_ROOT_LE + ax * math.tan(math.radians(32))
    c = 4.0 - (ax / HT_SPAN) * 2.6
    return dict(le=Vector((x, Y(le), HT_Z + ax * math.tan(HT_DIHEDRAL))), chord_dir=Vector((0, 1, 0)),
                up=Vector((0, 0, 1)), c=c, h=h, scale_t=scale_t)


def ht_hinge(x):
    st = ht_station(x)
    return st["le"] + st["chord_dir"] * (st["c"] * st["h"])


def build_htail():
    out = {}
    stab = MeshBuilder()
    xs = sorted(set([round(v, 4) for v in np.linspace(-6.0, 6.0, 13)]))
    lifting_surface(stab, [ht_station(x) for x in xs], "0010", "Body", "htail", flip=True)
    for sgn, side in ((1, "L"), (-1, "R")):
        tip = [ht_station(sgn * x, h=None, scale_t=t) for x, t in ((5.97, 1.0), (6.1, 0.85), (6.2, 0.55))]
        lifting_surface(stab, tip, "0010", "Body", "htail", flip=(sgn > 0))
        el = MeshBuilder()
        lifting_surface(el, [ht_station(sgn * x) for x in np.linspace(1.0, 5.97, 6)], "0010", "Body", "elevator",
                        part="ctrl", flip=(sgn > 0))
        out["Elevator_" + side] = el
    out["HStab"] = stab
    return out


VT_ROOT_Z = 5.9
VT_TOP_Z = 11.55
VT_HINGE = 0.70


def vt_le_s(z):
    return 29.8 + (z - VT_ROOT_Z) * math.tan(math.radians(37))


def vt_chord(z):
    return 5.6 - (z - VT_ROOT_Z) / (VT_TOP_Z - VT_ROOT_Z) * (5.6 - 1.95)


def vt_station(z, h=VT_HINGE, scale_t=1.0):
    c = vt_chord(z)
    return dict(le=Vector((0.0, Y(vt_le_s(z)), z)), chord_dir=Vector((0, 1, 0)), up=Vector((1, 0, 0)), c=c, h=h,
                scale_t=scale_t)


def vt_hinge(z):
    st = vt_station(z)
    return st["le"] + st["chord_dir"] * (st["c"] * st["h"])


def build_vtail():
    out = {}
    fin = MeshBuilder()
    lifting_surface(fin, [vt_station(z) for z in np.linspace(5.45, 11.25, 9)], "0010", "Body", "vtail")
    caps = [vt_station(z, h=None, scale_t=t) for z, t in ((11.22, 1.0), (11.42, 0.85), (11.55, 0.5))]
    lifting_surface(fin, caps, "0010", "Body", "vtail")
    # dorsal fillet blending the fin root into the fuselage crown
    s0, s1 = 27.6, 30.6
    rings = []
    for s in np.linspace(s0, s1, 10):
        t = (s - s0) / (s1 - s0)
        zt = float(Fuselage.zt(s))
        ztop = zt + 0.01 + (VT_ROOT_Z + 0.9 - zt) * t**2.2
        wb = 0.10 + 0.22 * math.sin(math.pi * t) ** 0.6
        wt = 0.02 + 0.10 * t
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
    lifting_surface(rud, [vt_station(z) for z in np.linspace(6.1, 11.2, 7)], "0010", "Body", "rudder", part="ctrl")
    out["Rudder"] = rud
    return out


# ============================================================== landing gear

NOSE_S = 5.1
NOSE_R = 0.38
NOSE_AXLE_Z = NOSE_R - 0.02
NOSE_WHEEL_X = 0.27
MAIN_X = 3.8           # leg
MAIN_WHEEL_DX = 0.45   # wheel offset from the leg
MAIN_R = 0.585
MAIN_AXLE_Z = MAIN_R - 0.03


def build_gear():
    """Returns {"Gear": fixed legs, "NoseGear": steering part, "Wheel_*": spinning wheels}."""
    b, nose, out = MeshBuilder(), MeshBuilder(), {}
    for sx, side in ((1, "L"), (-1, "R")):
        x = sx * MAIN_X
        top = wing_surface_z(MAIN_X, 17.6) + 0.15
        tube(b, [P(x, 17.6, top), P(x, 17.68, 1.2)], lambda t: 0.16, "Metal", "gearleg", segs=14)
        tube(b, [P(x, 17.68, 1.25), P(x, S_REF, MAIN_AXLE_Z + 0.2)], lambda t: 0.11, "Chrome", "gearleg", segs=14)
        tube(b, [P(x - sx * (MAIN_WHEEL_DX + 0.05), S_REF, MAIN_AXLE_Z), P(x + sx * (MAIN_WHEEL_DX + 0.05), S_REF, MAIN_AXLE_Z)],
             lambda t: 0.09, "DarkMetal", "gearleg", segs=10)
        # side brace to the fuselage and torque link
        tube(b, [P(x, 17.66, 1.75), P(sx * 1.6, 17.9, 2.05)], lambda t: 0.07, "Metal", "gearleg", segs=10)
        tube(b, [P(x, 17.60, 1.15), P(x, 17.42, 0.98), P(x, 17.62, 0.82)], lambda t: 0.035, "Metal", "gearleg", segs=8)
        for k, dx in ((1, -MAIN_WHEEL_DX), (2, MAIN_WHEEL_DX)):
            w = MeshBuilder()
            wheel(w, P(x + sx * dx, S_REF, MAIN_AXLE_Z), MAIN_R, 0.42, "wheel")
            out[f"Wheel_{side}{k}"] = w
    # nose gear: the upper strut is fixed, everything below the steering collar turns
    tube(b, [P(0, NOSE_S - 0.05, float(Fuselage.zb(NOSE_S)) + 0.3), P(0, NOSE_S, 1.25)], lambda t: 0.10, "Metal",
         "nosegear", segs=14)
    tube(nose, [P(0, NOSE_S, 1.30), P(0, NOSE_S, NOSE_AXLE_Z + 0.1)], lambda t: 0.07, "Chrome", "nosegear", segs=14)
    tube(nose, [P(-NOSE_WHEEL_X - 0.05, NOSE_S, NOSE_AXLE_Z), P(NOSE_WHEEL_X + 0.05, NOSE_S, NOSE_AXLE_Z)],
         lambda t: 0.06, "DarkMetal", "nosegear", segs=10)
    tube(nose, [P(0, NOSE_S + 0.02, 1.10), P(0, NOSE_S + 0.20, 0.92), P(0, NOSE_S + 0.03, 0.75)], lambda t: 0.03,
         "Metal", "nosegear", segs=8)
    # taxi / turn-off light housing on the strut
    revolve(nose, [(-0.06, 0.001), (-0.05, 0.07), (0.04, 0.075), (0.05, 0.001)], P(0, NOSE_S - 0.12, 1.15), (0, 1, 0),
            "LightLens", "light", segs=12)
    for side, sx in (("L", 1), ("R", -1)):
        w = MeshBuilder()
        wheel(w, P(sx * NOSE_WHEEL_X, NOSE_S, NOSE_AXLE_Z), NOSE_R, 0.22, "wheel")
        out["Wheel_Nose_" + side] = w
    out["Gear"] = b
    out["NoseGear"] = nose
    return out


# ============================================================== lights, antennas, probes

NAV_S = le_s(WING_TIP) + 0.15


def build_details():
    b = MeshBuilder()
    tip_z = wing_station(WING_TIP, h=None)["le"].z
    for sx, mat in ((1, "LightRed"), (-1, "LightGreen")):
        # nav light in the leading edge of the wingtip, strobe behind it
        revolve(b, [(-0.10, 0.001), (-0.06, 0.05), (0.05, 0.055), (0.10, 0.001)],
                (sx * (WING_TIP + 0.02), Y(NAV_S), tip_z + 0.02), (0, 1, 0), mat, "light", segs=12)
        revolve(b, [(-0.06, 0.001), (-0.03, 0.04), (0.04, 0.04), (0.06, 0.001)],
                (sx * (WING_TIP + 0.02), Y(NAV_S + 0.32), tip_z + 0.03), (0, 1, 0), "LightWhite", "light", segs=10)
        # landing light under the wing root
        s = 14.4
        revolve(b, [(-0.01, 0.001), (-0.005, 0.11), (0.04, 0.12)], (sx * 3.0, Y(s), wing_surface_z(3.0, s) - 0.01),
                (0, 0, -1), "LightLens", "light", segs=14, ref=(1, 0, 0))
        # logo light on the stabiliser, aimed at the fin
        x = sx * 2.2
        z = ht_station(x, h=None)["le"].z + 0.07
        revolve(b, [(-0.01, 0.06), (0.03, 0.05), (0.05, 0.001)], P(x, 33.6, z), (0, 0, 1), "LightWhite", "light",
                segs=10, ref=(1, 0, 0))
    # beacons on the crown and belly, white tail light
    s = 17.0
    revolve(b, [(-0.01, 0.10), (0.05, 0.09), (0.10, 0.05), (0.12, 0.001)], P(0, s, float(Fuselage.zt(s)) - 0.02),
            (0, 0, 1), "LightRed", "light", segs=14, ref=(1, 0, 0))
    s = 19.5
    revolve(b, [(-0.01, 0.10), (0.05, 0.09), (0.10, 0.05), (0.12, 0.001)], P(0, s, Fuselage.bottom_z(s, 0) + 0.02),
            (0, 0, -1), "LightRed", "light", segs=14, ref=(1, 0, 0))
    revolve(b, [(-0.04, 0.001), (-0.02, 0.06), (0.04, 0.06), (0.06, 0.001)], P(0, Fuselage.S1 + 0.02, 4.50), (0, 1, 0),
            "LightWhite", "light", segs=10)
    # antennas on the crown and belly
    for s, up, h in ((10.5, 1, 0.38), (21.0, 1, 0.30), (25.0, -1, 0.25)):
        z = float(Fuselage.zt(s)) if up > 0 else Fuselage.bottom_z(s, 0)
        tube(b, [P(0, s, z - up * 0.02), P(0, s + 0.25, z + up * h)], lambda t: 0.045 * (1 - 0.6 * t), "Dark", "detail",
             segs=6, squash=0.3)
    # pitot probes on the nose
    for sx, z in ((1, 3.95), (-1, 3.95), (1, 3.55)):
        s = 2.1
        x = sx * Fuselage.side_x(s, z)
        tube(b, [P(x, s, z), P(x + sx * 0.12, s - 0.02, z), P(x + sx * 0.13, s - 0.20, z)], lambda t: 0.012, "Metal",
             "detail", segs=6)
    return b
