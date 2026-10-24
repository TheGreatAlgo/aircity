"""Cessna 172-style aircraft parts, built procedurally.

Stations `s` are metres aft of the spinner tip. Blender Y = s - S_REF, so the
model origin sits on the ground under the main-gear axle (a natural taxi pivot).
Nose points -Y, port wing is +X, Z up.
"""
import math

import numpy as np
from mathutils import Vector

from fuselage import Fuselage as BaseFuselage, Loft, profile
from geom import MeshBuilder, airfoil_eval
from shapes import _fd_normal, drape_pane, lifting_surface, revolve, rounded_outline, tube, wheel

S_REF = 2.72          # main gear axle station -> model origin
THRUST_Z = 1.22       # propeller axis height


def Y(s):
    return s - S_REF


def P(x, s, z):
    return (x, Y(s), z)


# ============================================================== fuselage

class Fuselage(BaseFuselage):
    S0 = 0.40     # cowling front
    S1 = 7.92     # tail end

    zt = profile([(0.40, 1.485), (0.55, 1.53), (0.80, 1.575), (1.10, 1.608), (1.42, 1.632), (1.55, 1.69),
                  (1.75, 1.80), (1.95, 1.905), (2.12, 1.985), (2.30, 2.02), (3.70, 2.02), (3.85, 1.995),
                  (4.20, 1.875), (4.60, 1.77), (5.20, 1.675), (6.00, 1.605), (7.00, 1.55), (7.92, 1.50)])
    zb = profile([(0.40, 0.90), (0.55, 0.84), (0.80, 0.80), (1.10, 0.775), (1.40, 0.755), (2.00, 0.725),
                  (3.20, 0.725), (3.60, 0.75), (4.20, 0.865), (5.00, 1.02), (6.00, 1.16), (7.00, 1.28), (7.92, 1.37)])
    hw = profile([(0.40, 0.36), (0.55, 0.43), (0.80, 0.48), (1.10, 0.51), (1.40, 0.53), (1.80, 0.55),
                  (2.60, 0.555), (3.30, 0.55), (3.80, 0.52), (4.40, 0.44), (5.00, 0.355), (6.00, 0.235),
                  (7.00, 0.13), (7.92, 0.045)])
    fm = profile([(0.40, 0.50), (1.40, 0.42), (2.40, 0.40), (3.60, 0.42), (5.00, 0.50), (7.92, 0.55)])
    nt = profile([(0.40, 2.4), (1.40, 2.6), (2.00, 3.4), (2.30, 5.0), (3.60, 5.0), (4.20, 3.0), (5.50, 2.4), (7.92, 2.2)])
    nb = profile([(0.40, 2.6), (1.40, 3.0), (2.00, 3.4), (3.60, 3.4), (5.00, 2.6), (7.92, 2.3)])


def fuselage_stations():
    s = [0.40, 0.405, 0.415, 0.43, 0.45, 0.48, 0.52, 0.57, 0.63, 0.70, 0.78, 0.86]
    while s[-1] < 2.30:
        s.append(round(s[-1] + 0.08, 4))
    while s[-1] < 7.40:
        s.append(round(s[-1] + 0.10, 4))
    while s[-1] < 7.85:
        s.append(round(s[-1] + 0.07, 4))
    s.append(Fuselage.S1)
    return s


def build_fuselage():
    F = Fuselage
    b = MeshBuilder()
    loft = Loft(F, b, fuselage_stations(), P)
    rings, N, H = loft.rings, loft.N, loft.H
    vid, a_of = loft.vid, loft.a

    # --- cowling front: roll the lip inward to the spinner opening.
    c0 = F.sec(F.S0)
    zc = THRUST_Z
    outer = rings[0]
    cap_rings = []
    cap_specs = [(0.396, ("scale", 0.94)), (0.398, ("scale", 0.86)), (0.404, ("circle", 0.240)), (0.43, ("circle", 0.198))]
    for s, (kind, k) in cap_specs:
        ring = []
        for (x, y, z) in outer:
            dx, dz = x, z - zc
            if kind == "scale":
                ring.append(P(dx * k, s, zc + dz * k))
            else:
                ang = math.atan2(dz, dx)
                ring.append(P(k * math.cos(ang), s, zc + k * math.sin(ang)))
        cap_rings.append(ring)
    prev = [vid(0, j) for j in range(N)]
    dist = np.zeros(N)
    prev_pts = [Vector(p) for p in outer]
    for k, ring in enumerate(cap_rings):
        start = b.add_verts(ring)
        cur = [start + j for j in range(N)]
        cur_pts = [Vector(p) for p in ring]
        new_dist = dist + np.array([(cp - pp).length for cp, pp in zip(cur_pts, prev_pts)])
        for j in range(N):
            j2 = (j + 1) % N
            port = j < H
            sgn = 1.0 if port else -1.0
            u0, u1 = F.S0 - dist[j], F.S0 - dist[j2]
            w0, w1 = F.S0 - new_dist[j], F.S0 - new_dist[j2]
            quad = [prev[j], prev[j2], cur[j2], cur[j]]
            uvs = [(sgn * u0, a_of(0, j)), (sgn * u1, a_of(0, j2)), (sgn * w1, a_of(0, j2)), (sgn * w0, a_of(0, j))]
            tag = "fus_port" if port else "fus_stbd"
            if k == len(cap_rings) - 1:
                tag = "hidden"
            b.add_face(quad, "Body", uvs, tag)
        prev, prev_pts, dist = cur, cur_pts, new_dist
    # dark bulkhead behind the spinner so nothing can be seen through into the open fuselage
    b.add_face(prev, "Dark", None, "bulkhead")

    # --- tail end cap (fan)
    last = len(loft.st) - 1
    loft.fan_cap(last, np.array(rings[last]).mean(axis=0), s_tip=F.S1 + 0.03)
    return b


# ============================================================== windows

def _out_dir(p):
    s = p.y + S_REF
    c = Fuselage.sec(s)
    return Vector((p.x, 0.0, p.z - c["zm"]))


def side_surface(sign):
    """(s, z) -> point on the port (+1) / starboard (-1) side skin."""
    def fn(s, z):
        return P(sign * Fuselage.side_x(s, z), s, z)
    return lambda s, z: _fd_normal(fn, s, z, _out_dir)


def top_surface():
    """(s, x) -> point on the upper skin (used for windshield / rear window)."""
    def fn(s, x):
        return P(x, s, Fuselage.top_z(s, x))
    return lambda s, x: _fd_normal(fn, s, x, _out_dir)


def build_windows():
    b = MeshBuilder()
    # windshield: (s, x) outline; corners spread back at the A-pillars
    ws = rounded_outline([(1.515, -0.30), (1.60, -0.505), (2.095, -0.515), (2.095, 0.515), (1.60, 0.505), (1.515, 0.30)],
                         [0.10, 0.06, 0.05, 0.05, 0.06, 0.10])
    drape_pane(b, ws, top_surface(), tag="windshield")
    # Omni-vision rear window
    rw = rounded_outline([(3.80, -0.47), (3.80, 0.47), (4.18, 0.40), (4.40, 0.0), (4.18, -0.40)], [0.05, 0.05, 0.12, 0.20, 0.12])
    drape_pane(b, rw, top_surface(), tag="rearwindow")
    for sign in (1, -1):
        door = rounded_outline([(2.02, 1.40), (2.80, 1.40), (2.80, 1.915), (2.20, 1.915)], [0.06, 0.05, 0.05, 0.05])
        drape_pane(b, door, side_surface(sign), tag="sidewindow")
        rear = rounded_outline([(2.90, 1.42), (3.52, 1.465), (3.66, 1.88), (2.90, 1.905)], [0.05, 0.06, 0.05, 0.05])
        drape_pane(b, rear, side_surface(sign), tag="sidewindow")
    return b


# ---------------------------------------------------------------- wing

WING_LE_S = 2.13
WING_ROOT_Z = 2.06
WING_ROOT_Y = 0.56     # fuselage side
WING_BREAK = 2.90      # start of the trailing-edge taper
WING_TIP = 5.50
WING_HINGE = 0.735
DIHEDRAL = math.radians(1.73)


def wing_chord(y):
    y = abs(y)
    if y <= WING_BREAK:
        return 1.63
    return 1.63 + (1.13 - 1.63) * (y - WING_BREAK) / (WING_TIP - WING_BREAK)


def wing_station(y, h=WING_HINGE, scale_t=1.0, c_override=None, le_shift=0.0):
    ay = abs(y)
    sgn = 1.0 if y >= 0 else -1.0
    c = c_override if c_override else wing_chord(ay)
    zr = WING_ROOT_Z + max(ay - WING_ROOT_Y, 0.0) * math.tan(DIHEDRAL)
    inc = math.radians(1.5 - 3.0 * min(ay / WING_TIP, 1.0))   # 1.5 deg root -> -1.5 deg tip washout
    chord_dir = Vector((0.0, math.cos(inc), -math.sin(inc)))
    up = Vector((0.0, math.sin(inc), math.cos(inc)))
    le = Vector((sgn * ay, Y(WING_LE_S + le_shift), zr))
    hh = None if h is None else (WING_LE_S + wing_chord(ay) * h - (WING_LE_S + le_shift)) / c
    return dict(le=le, chord_dir=chord_dir, up=up, c=c, h=hh, scale_t=scale_t)


def wing_lower_z(y, s):
    """Approximate z of the wing's lower skin at span y, station s (strut attach)."""
    st = wing_station(y, h=None)
    af = airfoil_eval("2412")
    xf = (s - WING_LE_S) / st["c"]
    _, zl, _ = af(np.array([xf]))
    return float(st["le"].z + zl[0] * st["c"])


def build_wing():
    """Returns dict of MeshBuilders: Wing (fixed), Flap_L/R, Aileron_L/R."""
    tip_start = 5.16
    ys = list(np.linspace(-tip_start, -WING_ROOT_Y, 12)) + [0.0] + list(np.linspace(WING_ROOT_Y, tip_start, 12))
    ys = sorted(set([round(v, 4) for v in ys] + [-WING_BREAK, WING_BREAK]))
    wing = MeshBuilder()
    lifting_surface(wing, [wing_station(y) for y in ys], "2412", "Body", "wing", flip=True)

    out = {"Wing": wing}
    # rounded tips: full chord, thinning + plan-form rounding
    for sgn, side in ((1, "L"), (-1, "R")):
        tip_ys = [tip_start - 0.03, 5.25, 5.34, 5.41, 5.46, 5.49]
        t_scale = [1.0, 0.98, 0.9, 0.75, 0.5, 0.25]
        c_frac = [1.0, 0.99, 0.95, 0.86, 0.72, 0.52]
        sts = []
        for y, ts, cf in zip(tip_ys, t_scale, c_frac):
            c = wing_chord(y) * cf
            st = wing_station(sgn * y, h=None, scale_t=ts, c_override=c, le_shift=wing_chord(y) * (1 - cf) * 0.35)
            # Cessna conical-camber tips droop slightly
            st["le"] = st["le"] + Vector((0, 0, -0.06 * ((y - tip_start) / (WING_TIP - tip_start)) ** 2))
            sts.append(st)
        if sgn < 0:
            sts = sts  # same order; winding fixed by flip below
        lifting_surface(wing, sts, "2412", "Body", "wing", part="main", flip=(sgn > 0))
        # trailing-edge pieces behind the hinge
        segments = [("Flap", 0.60, 2.92), ("filler", 2.935, 3.05), ("Aileron", 3.065, tip_start - 0.015)]
        for name, y0, y1 in segments:
            yy = np.linspace(y0, y1, max(int((y1 - y0) / 0.35) + 2, 2))
            sts = [wing_station(sgn * y) for y in yy]
            target = wing if name == "filler" else out.setdefault(f"{name}_{side}", MeshBuilder())
            lifting_surface(target, sts, "2412", "Body", "wing_" + name.lower(), part="ctrl", flip=(sgn > 0))
    # centre filler behind the hinge over the cabin roof
    yy = np.linspace(-0.585, 0.585, 5)
    lifting_surface(wing, [wing_station(y) for y in yy], "2412", "Body", "wing_filler", part="ctrl", flip=True)
    return out


# ---------------------------------------------------------------- tail

HT_Z = 1.44
HT_HINGE_S = 7.53


def ht_station(x, h=True, scale_t=1.0, cf=1.0):
    ax = abs(x)
    le_s = 6.74 + ax * math.tan(math.radians(9.0))
    te_s = 8.20 - ax * 0.06
    c = (te_s - le_s) * cf
    le_s = le_s + (te_s - le_s) * (1 - cf) * 0.4
    hh = (HT_HINGE_S - le_s) / c if h else None
    return dict(le=Vector((x, Y(le_s), HT_Z)), chord_dir=Vector((0, 1, 0)), up=Vector((0, 0, 1)),
                c=c, h=hh, scale_t=scale_t)


def build_htail():
    out = {}
    stab = MeshBuilder()
    span = 1.56
    xs = sorted(set([round(v, 4) for v in np.linspace(-span, span, 13)]))
    lifting_surface(stab, [ht_station(x) for x in xs], "0010", "Body", "htail", flip=True)
    for sgn, side in ((1, "L"), (-1, "R")):
        tip = [span - 0.02, 1.62, 1.67, 1.70, 1.72]
        ts = [1.0, 0.92, 0.75, 0.5, 0.25]
        cf = [1.0, 0.96, 0.86, 0.70, 0.48]
        lifting_surface(stab, [ht_station(sgn * x, h=False, scale_t=t, cf=c) for x, t, c in zip(tip, ts, cf)],
                        "0010", "Body", "htail", flip=(sgn > 0))
    out["HStab"] = stab
    elev = MeshBuilder()
    for sgn in (1, -1):
        xs = np.linspace(0.11, span - 0.015, 6)
        lifting_surface(elev, [ht_station(sgn * x) for x in xs], "0010", "Body", "elevator", part="ctrl", flip=(sgn > 0))
    out["Elevator"] = elev
    return out


VT_HINGE_S = 7.80


def vt_le_s(z):
    return 6.12 + (z - 1.55) * (7.28 - 6.12) / (2.60 - 1.55)


def vt_station(z, part, scale_t=1.0, cf=1.0, le_s=None, te_s=None):
    """Vertical tail section at height z (chord runs along +Y, thickness along X)."""
    if le_s is None:
        le_s = vt_le_s(z)
    if te_s is None:
        te_s = float(np.interp(z, [1.26, 1.50, 1.80, 2.40, 2.62], [8.13, 8.23, 8.27, 8.20, 8.12]))
    c = (te_s - le_s) * cf
    h = (VT_HINGE_S - le_s) / c
    # "up" is the airfoil thickness axis -> +X (port); chord along +Y
    return dict(le=Vector((0.0, Y(le_s), z)), chord_dir=Vector((0, 1, 0)), up=Vector((1, 0, 0)),
                c=c, h=h, scale_t=scale_t)


def build_vtail():
    out = {}
    fin = MeshBuilder()
    zs = list(np.linspace(1.50, 2.52, 9))
    lifting_surface(fin, [vt_station(z, "main") for z in zs], "0010", "Body", "vtail")
    # rounded fin cap (fixed part, ahead of the rudder horn)
    caps = []
    for z, ts, shrink in [(2.50, 1.0, 0.0), (2.57, 0.9, 0.015), (2.625, 0.65, 0.04), (2.65, 0.3, 0.08)]:
        le = vt_le_s(z) + shrink
        te = 7.50 - shrink * 0.5
        caps.append(dict(le=Vector((0, Y(le), z)), chord_dir=Vector((0, 1, 0)), up=Vector((1, 0, 0)),
                         c=te - le, h=None, scale_t=ts))
    lifting_surface(fin, caps, "0010", "Body", "vtail")
    # dorsal fin fillet: a concave-sided blade that rises from the tailcone into the fin leading edge
    dorsal = MeshBuilder()
    s0, s1 = 4.90, 6.62
    ss = np.linspace(s0, s1, 16)
    hs = [0.0, 0.12, 0.3, 0.52, 0.74, 0.9, 1.0]
    rings = []
    for s in ss:
        t = (s - s0) / (s1 - s0)
        zt = float(Fuselage.zt(s))
        ztop = zt + 0.004 + (vt_le_s_inv(s1) - zt) * t**1.9
        wb = 0.05 + 0.07 * math.sin(math.pi * t) ** 0.6       # fillet half-width at the base
        wt = 0.006 + 0.018 * t                                 # half-thickness near the top edge
        zbase = Fuselage.top_z(s, wb) - 0.015
        port = []
        for h in hs:
            x = wb * (1 - h) ** 2.4 + wt * h * (1 - h * 0.35)
            z = zbase + (ztop - zbase) * h
            port.append((x, z))
        ring = [P(x, s, z) for x, z in port] + [P(0.0, s, ztop + wt * 0.6)] + [P(-x, s, z) for x, z in port[::-1]]
        rings.append(ring)
    n = len(rings[0])
    base = dorsal.add_verts([p for r in rings for p in r])
    for i in range(len(rings) - 1):
        for j in range(n - 1):
            q = [base + i * n + j, base + (i + 1) * n + j, base + (i + 1) * n + j + 1, base + i * n + j + 1]
            dorsal.add_face(q, "Body", None, "dorsal")
    dorsal.add_face([base + j for j in range(n)], "Body", None, "dorsal")
    fin.merge(dorsal)
    out["VStab"] = fin

    rud = MeshBuilder()
    zs = list(np.linspace(1.27, 2.60, 9))
    lifting_surface(rud, [vt_station(z, "ctrl") for z in zs], "0010", "Body", "rudder", part="ctrl")
    # horn balance on top of the rudder (full chord, overhangs the fin cap)
    horn = []
    for z, ts, cf in [(2.585, 1.0, 1.0), (2.635, 0.95, 0.97), (2.68, 0.8, 0.9), (2.705, 0.55, 0.78), (2.72, 0.3, 0.6)]:
        le = 7.53 + (1 - cf) * 0.20
        te = 8.10 - (1 - cf) * 0.20
        horn.append(dict(le=Vector((0, Y(le), z)), chord_dir=Vector((0, 1, 0)), up=Vector((1, 0, 0)),
                         c=te - le, h=None, scale_t=ts * 1.15))
    lifting_surface(rud, horn, "0010", "Body", "rudder")
    # white tail light at the rudder's lower trailing edge
    revolve(rud, [(-0.025, 0.001), (-0.01, 0.017), (0.02, 0.012), (0.03, 0.001)], P(0, 8.165, 1.33), (0, 1, 0),
            "LightWhite", "light", segs=10)
    out["Rudder"] = rud
    return out


def vt_le_s_inv(s):
    """Height of the fin leading edge at station s."""
    return 1.55 + (s - 6.12) * (2.60 - 1.55) / (7.28 - 6.12)


# ============================================================== propeller + spinner

PROP_S = 0.24
PROP_R = 0.95


def build_propeller():
    b = MeshBuilder()
    hub = Vector(P(0, PROP_S, THRUST_Z))
    # spinner: ogive from tip (s=0) to backplate (s=0.40)
    prof = []
    for k in range(15):
        t = k / 14
        s = 0.40 * t
        r = 0.205 * (1 - (1 - t) ** 2.5) ** 0.5
        prof.append((s, max(r, 0.0015)))
    prof.append((0.405, 0.205))
    prof.append((0.405, 0.15))
    revolve(b, prof, P(0, 0, THRUST_Z), (0, 1, 0), "Body", "spinner", segs=32, cap_start=False, cap_end=True,
            ref=(0, 0, 1))

    af = airfoil_eval("4408")
    radii = list(np.linspace(0.13, 0.86, 14)) + [0.89, 0.915, 0.93, 0.942, 0.95]

    def chord(r):
        x = r / PROP_R
        c = 0.048 + 0.088 * math.sin(math.pi * min((x - 0.12) / 0.75, 1.0) * 0.62) - 0.02 * max(x - 0.75, 0) / 0.25
        if r > 0.89:
            c *= math.sqrt(max(1 - ((r - 0.89) / 0.0615) ** 2, 0.04))
        return c

    def beta(r):
        return math.atan(1.37 / (2 * math.pi * r))

    xs = np.concatenate([[0.0], (1 - np.cos(np.linspace(0.05, math.pi / 2, 10))) * 0.5, np.linspace(0.55, 1.0, 6)])
    xs = np.unique(xs)
    for blade_sign in (1, -1):
        rings = []
        for r in radii:
            c = chord(r)
            bt = beta(r)
            thick = 1.0 if r > 0.25 else 1.0 + 2.2 * (0.25 - r) / 0.12   # thicker shank
            zu, zl, _ = af(xs)
            pts = []
            seq = list(zip(xs, zu * thick)) + list(zip(xs[::-1][1:-1], (zl * thick)[::-1][1:-1]))
            for xf, zf in seq:
                tang = (0.32 - xf) * c          # + toward the direction of motion
                ax = zf * c
                # chord line tilted by beta: motion axis = +Z (blade at +X), forward = -Y
                tz = tang * math.cos(bt) - ax * math.sin(bt)
                ty = -(tang * math.sin(bt) + ax * math.cos(bt))
                local = Vector((r, ty, tz))
                if blade_sign < 0:
                    local = Vector((-local.x, local.y, -local.z))
                pts.append(hub + local)
            rings.append(pts)
        n = len(rings[0])
        base = b.add_verts([p for rr in rings for p in rr])
        for i in range(len(rings) - 1):
            mat = "PropTip" if radii[i] >= 0.86 else "PropBlade"
            for j in range(n):
                j2 = (j + 1) % n
                q = [base + i * n + j, base + (i + 1) * n + j, base + (i + 1) * n + j2, base + i * n + j2]
                b.add_face(q[::-1], mat, None, "blade")
        tipf = [base + (len(rings) - 1) * n + j for j in range(n)]
        b.add_face(tipf, "PropTip", None, "blade")
    return b, tuple(hub)


# ============================================================== landing gear

MAIN_X = 1.27
MAIN_S = S_REF
MAIN_R = 0.222
NOSE_S = 1.05
NOSE_R = 0.19


def pant(b, centre, length, half_w, top, bottom, nose_frac=0.36, tag="pant"):
    """Teardrop wheel fairing centred on a wheel; open-bottom look via a flat underside."""
    c = Vector(centre)
    s_front = c.y - length * nose_frac
    s_back = c.y + length * (1 - nose_frac)
    rings = []
    K = 16
    ys = list(np.linspace(0, 1, K))
    for t in ys:
        yy = s_front + (s_back - s_front) * t
        if t <= nose_frac:
            f = math.sqrt(max(1 - ((nose_frac - t) / nose_frac) ** 2, 0.0))
        else:
            f = (1 - ((t - nose_frac) / (1 - nose_frac)) ** 1.6) ** 0.9
        f = max(f, 0.03)
        w = half_w * f
        zc = bottom + (top - bottom) * (0.5 + 0.18 * (t - nose_frac) * (t > nose_frac))
        ht = (top - bottom) / 2 * f
        zmid = zc
        ring = []
        for k in range(20):
            a = 2 * math.pi * k / 20
            x = w * math.cos(a)
            z = zmid + ht * math.sin(a) * (1.0 if math.sin(a) > 0 else 0.9)
            z = max(z, bottom)
            ring.append(Vector((c.x + x, yy, z)))
        rings.append(ring)
    base = b.add_verts([p for r in rings for p in r])
    n = 20
    for i in range(K - 1):
        for k in range(n):
            k2 = (k + 1) % n
            q = [base + i * n + k, base + i * n + k2, base + (i + 1) * n + k2, base + (i + 1) * n + k]
            b.add_face(q[::-1], "Body", None, tag)
    b.add_face([base + k for k in range(n)], "Body", None, tag)
    b.add_face([base + (K - 1) * n + k for k in range(n)][::-1], "Body", None, tag)


MAIN_AXLE_Z = MAIN_R - 0.01
NOSE_AXLE_Z = NOSE_R - 0.01
NOSE_PIVOT = (0.0, NOSE_S + 0.03)      # (x, s) of the steering axis


def build_gear():
    """Returns {name: MeshBuilder}: "Gear" (fixed: main legs + pants, upper nose strut), "NoseGear" (steers:
    lower strut, torque link, nose pant), "Wheel_Nose", "Wheel_L", "Wheel_R" (spin about their axles)."""
    b = MeshBuilder()
    nose, out = MeshBuilder(), {}
    axle_z = MAIN_AXLE_Z
    for sx, side in ((1, "L"), (-1, "R")):
        # tubular spring-steel leg: belly attach -> inboard side of the wheel
        path = []
        for k in range(10):
            t = k / 9
            x = 0.40 + (MAIN_X - 0.13 - 0.40) * t
            z = 0.79 - (0.79 - axle_z) * (t**0.85)
            s = MAIN_S - 0.10 + 0.10 * t
            path.append(P(sx * x, s, z))
        tube(b, path, lambda t: 0.036 - 0.012 * t, "Body", "gearleg", segs=12, ref=(0, 1, 0), squash=1.0)
        w = MeshBuilder()
        wheel(w, P(sx * MAIN_X, MAIN_S, axle_z), MAIN_R, 0.15, "wheel")
        out["Wheel_" + side] = w
        pant(b, P(sx * MAIN_X, MAIN_S, axle_z), 0.86, 0.135, axle_z + 0.27, 0.085, tag="pant")
    # nose gear: the upper strut is fixed; everything below the steering collar turns
    nz = NOSE_AXLE_Z
    tube(b, [P(0, NOSE_S + 0.06, 0.80), P(0, NOSE_S + 0.04, 0.58)], lambda t: 0.045, "Metal", "nosegear", segs=14)
    tube(nose, [P(0, NOSE_S + 0.04, 0.60), P(0, NOSE_S + 0.015, 0.40)], lambda t: 0.032, "Chrome", "nosegear", segs=14)
    # torque link
    tube(nose, [P(0, NOSE_S + 0.06, 0.62), P(0, NOSE_S + 0.11, 0.53), P(0, NOSE_S + 0.05, 0.43)], lambda t: 0.012,
         "Metal", "nosegear", segs=8)
    w = MeshBuilder()
    wheel(w, P(0, NOSE_S, nz), NOSE_R, 0.13, "wheel")
    out["Wheel_Nose"] = w
    pant(nose, P(0, NOSE_S, nz), 0.66, 0.11, nz + 0.23, 0.075, nose_frac=0.40, tag="pant")
    out["Gear"] = b
    out["NoseGear"] = nose
    return out


def build_struts():
    b = MeshBuilder()
    af = airfoil_eval("0030")
    for sx in (1, -1):
        root = Vector(P(sx * 0.535, 2.66, 0.90))
        ys = 2.48
        tip = Vector(P(sx * ys, 2.40, wing_lower_z(ys, 2.40) + 0.01))
        axis = (tip - root).normalized()
        fwd = Vector((0, -1, 0))
        fwd = (fwd - axis * fwd.dot(axis)).normalized()
        side = axis.cross(fwd).normalized()
        xs = np.linspace(0, 1, 12)
        zu, zl, _ = af(xs)
        chord = 0.105
        seq = list(zip(xs, zu)) + list(zip(xs[::-1][1:-1], zl[::-1][1:-1]))
        rings = []
        for k in range(8):
            t = k / 7
            c = root + (tip - root) * t
            sc = 1.0 + 0.5 * (math.exp(-t * 30) + math.exp(-(1 - t) * 30))   # small root / tip cuffs
            rings.append([c + fwd * ((0.35 - x) * chord * sc) + side * (z * chord * sc) for x, z in seq])
        n = len(rings[0])
        base = b.add_verts([p for r in rings for p in r])
        for i in range(len(rings) - 1):
            for j in range(n):
                j2 = (j + 1) % n
                q = [base + i * n + j, base + i * n + j2, base + (i + 1) * n + j2, base + (i + 1) * n + j]
                b.add_face(q, "Body", None, "strut")
        b.add_face([base + j for j in range(n)][::-1], "Body", None, "strut")
        b.add_face([base + (len(rings) - 1) * n + j for j in range(n)], "Body", None, "strut")
        # step on the lower strut
        st = root + (tip - root) * 0.12
        tube(b, [st, st + Vector((0, 0.10, -0.02))], lambda t: 0.012, "Metal", "detail", segs=8)
    return b


# ============================================================== details

def build_details():
    b = MeshBuilder()
    # nav lights (port red, starboard green) at the wingtips; white tail light
    for sx, mat in ((1, "LightRed"), (-1, "LightGreen")):
        y = 5.47
        st = wing_station(sx * y, h=None)
        c = Vector((sx * (WING_TIP + 0.005), Y(WING_LE_S + 0.25), st["le"].z - 0.055))
        revolve(b, [(-0.05, 0.001), (-0.03, 0.022), (0.02, 0.026), (0.05, 0.001)], c, (0, 1, 0), mat, "light", segs=12)
    # anti-collision beacon on the fin cap
    revolve(b, [(-0.01, 0.030), (0.03, 0.028), (0.055, 0.016), (0.065, 0.001)], P(0, 7.40, 2.635), (0, 0, 1), "LightRed",
            "light", segs=14, ref=(1, 0, 0))
    # landing / taxi lights in the port wing leading edge
    for k, dx in enumerate((1.55, 1.72)):
        st = wing_station(dx, h=None)
        c = Vector((dx, st["le"].y + 0.006, st["le"].z + 0.012))
        revolve(b, [(-0.004, 0.001), (-0.002, 0.026), (0.03, 0.03)], c, (0, 1, 0), "LightLens", "light", segs=14)
    # pitot tube under the port wing
    pz = wing_lower_z(2.15, 2.30)
    tube(b, [P(2.15, 2.30, pz + 0.01), P(2.15, 2.28, pz - 0.10), P(2.15, 2.05, pz - 0.10)],
         lambda t: 0.009, "Metal", "detail", segs=8)
    # fuel caps on the wing tops
    for sx in (1, -1):
        y = 1.55
        st = wing_station(sx * y, h=None)
        s = WING_LE_S + 0.42
        af = airfoil_eval("2412")
        zu, _, _ = af(np.array([(s - WING_LE_S) / st["c"]]))
        z = st["le"].z + zu[0] * st["c"]
        revolve(b, [(-0.01, 0.035), (0.006, 0.035), (0.010, 0.02), (0.011, 0.001)], P(sx * y, s, z), (0, 0, 1),
                "Metal", "detail", segs=14, ref=(1, 0, 0))
    # comm antennas on the cabin top behind the rear window
    for dx, s in ((0.05, 4.55), (-0.05, 4.85)):
        z = float(Fuselage.zt(s))
        tube(b, [P(dx, s, z - 0.02), P(dx, s + 0.18, z + 0.46)], lambda t: 0.006 * (1 - 0.5 * t), "Dark", "detail", segs=6)
        revolve(b, [(0, 0.022), (0.02, 0.012), (0.022, 0.001)], P(dx, s, z - 0.005), (0, 0, 1), "Dark", "detail",
                segs=10, ref=(1, 0, 0))
    # exhaust stack (starboard, under the cowl)
    tube(b, [P(-0.14, 1.20, 0.86), P(-0.16, 1.24, 0.75), P(-0.17, 1.36, 0.70)], lambda t: 0.024, "DarkMetal",
         "detail", segs=10)
    # cowling air inlets either side of the spinner (dark shells)
    for sx in (1, -1):
        cx = 0.266
        drape = []
        for k in range(16):
            a = 2 * math.pi * k / 16
            drape.append((cx * sx + 0.052 * math.cos(a), THRUST_Z + 0.02 + 0.07 * math.sin(a)))
        ring = [b.add_vert(P(x, 0.391, z)) for x, z in drape]
        cen = b.add_vert(P(cx * sx, 0.400, THRUST_Z + 0.02))
        for k in range(16):
            k2 = (k + 1) % 16
            tri = [ring[k], ring[k2], cen]
            b.add_face(tri, "Dark", None, "inlet")
    # cowl nose landing-light lens below the spinner (older 172 style) – skipped: wing lights used instead
    return b
