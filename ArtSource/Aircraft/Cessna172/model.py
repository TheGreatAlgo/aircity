"""Cessna 172-style light single: the model definition the shared pipeline (../Common/pipeline.py) builds.

Stations `s` are metres aft of the spinner tip; Blender Y = s - S_REF. See ArtSource/README.md for what a
model definition provides.
"""
import math

import numpy as np
from mathutils import Vector

import parts
from geom import MeshBuilder
from paint import curve_dist, round_rect_sdf
from parts import P, Y, Fuselage, S_REF  # noqa: F401  (Fuselage / S_REF are read by the pipeline)
from pipeline import Mover

NAME = "Cessna172"
DISPLAY_NAME = "Light single"
CATEGORY = "GeneralAviation"
AO_DISTANCE = 0.9

# ============================================================== geometry


def build():
    body = MeshBuilder()
    body.merge(parts.build_fuselage())
    body.merge(parts.build_windows())
    wing = parts.build_wing()
    body.merge(wing.pop("Wing"))
    ht = parts.build_htail()
    body.merge(ht.pop("HStab"))
    vt = parts.build_vtail()
    body.merge(vt.pop("VStab"))
    gear = parts.build_gear()
    body.merge(gear.pop("Gear"))
    body.merge(parts.build_struts())
    body.merge(parts.build_details())

    prop, hub = parts.build_propeller()
    movers = [
        Mover("Propeller", prop, hub),
        Mover("Rudder", vt["Rudder"], (0, Y(parts.VT_HINGE_S), 2.0)),
        Mover("Elevator", ht["Elevator"], (0, Y(parts.HT_HINGE_S), parts.HT_Z)),
    ]
    for name, b in wing.items():
        pts = np.array(b.verts)
        side = 1 if name.endswith("_L") else -1
        y_mid = abs(pts[:, 0]).mean()
        st = parts.wing_station(side * y_mid)
        hinge = st["le"] + st["chord_dir"] * (st["c"] * st["h"])
        movers.append(Mover(name, b, (side * y_mid, hinge.y, hinge.z)))
    nx, ns = parts.NOSE_PIVOT
    movers += [
        Mover("NoseGear", gear["NoseGear"], P(nx, ns, parts.NOSE_AXLE_Z)),
        Mover("Wheel_Nose", gear["Wheel_Nose"], P(0, parts.NOSE_S, parts.NOSE_AXLE_Z), parent="NoseGear"),
        Mover("Wheel_L", gear["Wheel_L"], P(parts.MAIN_X, parts.MAIN_S, parts.MAIN_AXLE_Z)),
        Mover("Wheel_R", gear["Wheel_R"], P(-parts.MAIN_X, parts.MAIN_S, parts.MAIN_AXLE_Z)),
    ]
    return body, movers


# LODs: triangle ratios of the far LODs, parts kept (low-poly) in LOD1, collider ratio relative to LOD2
LOD_RATIOS = {1: 0.25, 2: 0.07}
LOD1_KEEP = ["Propeller"]
COLLIDER_RATIO = 0.3

# ============================================================== UV atlas

UV_TOP_BOTTOM = {"wing", "wing_flap", "wing_aileron", "wing_filler", "htail", "elevator"}
UV_SIDE = {"vtail", "rudder", "dorsal"}
# texel-density multipliers (relative to the fuselage) for less visible surfaces
DENSITY = {"hidden": 0.15, "wing_cap": 0.15, "htail_cap": 0.3, "vtail_cap": 0.3, "elevator_cap": 0.3,
           "wing_flap_cap": 0.3, "wing_aileron_cap": 0.3, "wing_filler_cap": 0.3, "rudder_cap": 0.3,
           "gearleg": 0.7, "strut": 0.6, "pant": 0.9, "spinner": 0.9}
BOTTOM_DENSITY = 0.55

# ============================================================== livery schemes


def _accents(p):
    """Spinner, wheel pants, wingtips, stab tips, fin cap: the usual 'trim' set."""
    return np.maximum.reduce([p.f(p.group("spinner")), p.f(p.group("pant")),
                              p.inside(5.0 - np.abs(p.x)) * p.wing, p.inside(1.55 - np.abs(p.x)) * p.ht,
                              p.inside(2.47 - p.z) * p.vt])


def scheme_classic(p):
    """1970s Skyhawk: a cheat stripe from the nose that sweeps up the tail and fin,
    a pinstripe above it, and trim on the spinner / pants / tips."""
    knots = [(-0.2, 1.10), (2.0, 1.10), (3.7, 1.12), (4.7, 1.15), (5.6, 1.20), (6.3, 1.30), (6.9, 1.55),
             (7.4, 1.90), (7.9, 2.27), (8.6, 2.75)]
    d = curve_dist(p.s, p.z, knots)
    zone = p.fus | p.vt
    r = p.band(d, -0.075, 0.075) * zone
    g = p.band(d, 0.105, 0.14) * zone
    return r, g, _accents(p)


def scheme_swoosh(p):
    """2000s Skyhawk SP: a swoosh that grows out of the cabin and sweeps up the fin."""
    lo = [(2.95, 1.07), (4.0, 1.03), (5.5, 1.11), (7.0, 1.29), (8.6, 1.42)]
    hi = [(2.95, 1.07), (4.0, 1.13), (5.4, 1.33), (6.5, 1.62), (7.2, 2.02), (8.0, 2.62)]
    zone = p.fus | p.vt
    start = p.inside(2.95 - p.s)
    d_lo = curve_dist(p.s, p.z, lo)
    d_hi = curve_dist(p.s, p.z, hi)
    r = np.minimum(p.inside(-d_lo), p.inside(d_hi)) * start * zone
    g = p.band(d_hi, 0.035, 0.07) * p.inside(3.35 - p.s) * zone
    g = np.maximum(g, p.band(d_lo, -0.085, -0.055) * p.inside(3.6 - p.s) * zone)
    return r, g, _accents(p)


def scheme_two_tone(p):
    """Two-tone: belly colour under a cheatline, painted tail."""
    cheat = [(-0.2, 1.10), (2.0, 1.10), (4.0, 1.14), (6.0, 1.28), (8.6, 1.40)]
    d = curve_dist(p.s, p.z, cheat)
    pant, leg = p.group("pant"), p.group("gearleg")
    r = p.inside(d + 0.055) * (p.fus | pant | leg)
    r = np.where(pant | leg, 1.0, r)
    g = p.band(d, -0.045, 0.045) * p.fus
    b = np.maximum.reduce([p.f(p.vt), p.f(p.group("spinner")), p.inside(5.0 - np.abs(p.x)) * p.wing])
    return r, g, b


def scheme_trainer(p):
    """Flight-school: painted cowl and spinner, coloured tips, thin window-line stripe."""
    spinner, pant = p.group("spinner"), p.group("pant")
    cowl = p.fus & (p.s < 1.6)
    r = np.maximum.reduce([p.inside(p.s - 1.42) * p.fus, p.f(spinner), p.f(pant)]) * (cowl | spinner | pant)
    g = np.maximum.reduce([p.inside(4.6 - np.abs(p.x)) * p.wing, p.inside(1.35 - np.abs(p.x)) * p.ht,
                           p.inside(2.30 - p.z) * p.vt])
    d = p.z - 1.315
    b = p.band(d, -0.02, 0.02) * p.fus * p.inside(1.42 - p.s) * p.inside(p.s - 4.4) * (np.abs(p.nx) > 0.35)
    return r, g, b


SCHEMES = {"Classic": scheme_classic, "Swoosh": scheme_swoosh, "TwoTone": scheme_two_tone, "Trainer": scheme_trainer}

# Registration box per scheme, in fuselage metres: (station from, station to, arc-from-the-widest-line
# from, to). Letters are as tall as the box. Kept clear of the stripes.
REG_BOX = {"Classic": (4.50, 5.65, 0.02, 0.18), "Swoosh": (4.30, 5.15, 0.05, 0.22),
           "TwoTone": (4.75, 6.15, -0.04, 0.15), "Trainer": (4.75, 6.15, -0.06, 0.13)}
# fuselage faces that get the per-aircraft registration texture (metric UV units: |station|, arc)
REG_FACES_U = (4.18, 6.40)
REG_FACES_V = (-0.13, 0.27)
REG_TEX_W = 1024

# tail logo area on both sides of the fin: station from / to, height from / to
LOGO_BOX = (7.08, 7.98, 1.68, 2.42)

# ============================================================== shading, dirt


def panel_lines(p):
    w = 0.0042
    lines = []
    side = np.abs(p.nx) > 0.35
    fus = p.fus
    # firewall / cowling joint and upper/lower cowl split
    lines.append(p.line(p.s - 1.42, w) * fus)
    lines.append(p.line(p.z - p.zm, w) * fus * side * p.inside(p.s - 1.42) * p.inside(0.47 - p.s))
    # oil door on the cowl top
    sdf = round_rect_sdf(p.s, p.x, 1.10, 0.0, 0.15, 0.13, 0.03)
    lines.append(p.line(sdf, w) * fus * (p.nz > 0.55))
    # cabin doors (both sides) and the port baggage door
    sdf = round_rect_sdf(p.s, p.z, 2.395, 1.42, 0.47, 0.62, 0.06)
    lines.append(p.line(sdf, w) * fus * side)
    sdf = round_rect_sdf(p.s, p.z, 3.24, 1.11, 0.19, 0.18, 0.03)
    lines.append(p.line(sdf, w) * fus * side * (p.x > 0))
    # door handles
    lines.append(p.inside(round_rect_sdf(p.s, p.z, 2.76, 1.32, 0.035, 0.012, 0.008)) * fus * side * 1.6)
    # tailcone skin joints
    for s in (4.30, 5.40, 6.45):
        lines.append(p.line(p.s - s, w * 0.8) * fus * 0.55)
    # wing skin: leading-edge skin joint (top) and two rib-line joints
    top = p.nz > 0
    lines.append(p.line(p.s - (parts.WING_LE_S + 0.34), w) * p.wing * top * p.inside(0.6 - np.abs(p.x)))
    for y in (2.9, 4.1):
        lines.append(p.line(np.abs(p.x) - y, w * 0.8) * p.wing * 0.6)
    # stabiliser / fin root fairing line
    lines.append(p.line(p.s - 7.92, w) * fus * 0.5)
    return np.clip(np.maximum.reduce(lines), 0, 1)


def detail_extra(p):
    return None


def dirt_extra(p):
    """Exhaust soot along the starboard belly, an oil streak under the cowl, grimy wheel pants."""
    soot = p.fus * (p.x < 0.05) * (p.nz < 0.3) * p.inside(p.s - 3.6) * p.inside(1.25 - p.s) * np.exp(
        -((p.z - 0.80) / 0.16) ** 2)
    oil = p.fus * (p.nz < -0.4) * p.inside(p.s - 4.2) * p.inside(1.4 - p.s) * np.exp(-(p.x / 0.18) ** 2)
    pants = p.group("pant") * (p.nz < 0.3)
    return -0.16 * soot - 0.08 * oil - 0.06 * pants


# ============================================================== gameplay data (Blender space; the pipeline converts)

GEAR = {
    "wheels": [("Wheel_Nose", P(0, parts.NOSE_S, parts.NOSE_AXLE_Z), parts.NOSE_R, True),
               ("Wheel_L", P(parts.MAIN_X, parts.MAIN_S, parts.MAIN_AXLE_Z), parts.MAIN_R, False),
               ("Wheel_R", P(-parts.MAIN_X, parts.MAIN_S, parts.MAIN_AXLE_Z), parts.MAIN_R, False)],
    "steering_pivot": P(parts.NOSE_PIVOT[0], parts.NOSE_PIVOT[1], 0.0),
    "max_steering": 30.0,     # rudder-pedal steering plus differential braking
}

# runway needs: C172S POH at maximum weight, sea level, 15 C, distances over a 50 ft obstacle
PERFORMANCE = {"takeoff_distance": 497.0, "landing_distance": 407.0, "max_takeoff_mass": 1157.0, "unpaved": True}


def _side(s, z, sign):
    return P(sign * Fuselage.side_x(s, z), s, z)


def _wing_top(x, s):
    st = parts.wing_station(x, h=None)
    from geom import airfoil_eval
    zu, _, _ = airfoil_eval("2412")(np.array([(s - parts.WING_LE_S) / st["c"]]))
    return (x, Y(s), st["le"].z + zu[0] * st["c"])


FWD, AFT, UP, DOWN = (0, -1, 0), (0, 1, 0), (0, 0, 1), (0, 0, -1)

# (type, name, position = door sill / hatch / attach point, outward normal, size (w, h) in metres)
POINTS = [
    ("PassengerDoor", "Door_L", _side(2.395, 0.83, 1), (1, 0, 0), (0.94, 1.24)),
    ("PassengerDoor", "Door_R", _side(2.395, 0.83, -1), (-1, 0, 0), (0.94, 1.24)),
    ("CargoDoor", "Baggage", _side(3.24, 0.95, 1), (1, 0, 0), (0.38, 0.36)),
    ("Fuel", "Fuel_L", _wing_top(1.55, parts.WING_LE_S + 0.42), UP, (0.07, 0.07)),
    ("Fuel", "Fuel_R", _wing_top(-1.55, parts.WING_LE_S + 0.42), UP, (0.07, 0.07)),
    ("TowAttach", "TowBar", P(0, parts.NOSE_S - 0.05, parts.NOSE_AXLE_Z), FWD, (0, 0)),
    ("WheelChock", "Chock_Nose", P(0, parts.NOSE_S, 0), FWD, (0.13, 0)),
    ("WheelChock", "Chock_L", P(parts.MAIN_X, parts.MAIN_S, 0), FWD, (0.15, 0)),
    ("WheelChock", "Chock_R", P(-parts.MAIN_X, parts.MAIN_S, 0), FWD, (0.15, 0)),
    ("TieDown", "TieDown_L", (3.6, Y(2.75), parts.wing_lower_z(3.6, 2.75)), DOWN, (0, 0)),
    ("TieDown", "TieDown_R", (-3.6, Y(2.75), parts.wing_lower_z(3.6, 2.75)), DOWN, (0, 0)),
    ("TieDown", "TieDown_Tail", P(0, 7.55, float(Fuselage.zb(7.55))), DOWN, (0, 0)),
]

_nav_s = parts.WING_LE_S + 0.25
_nav_z = parts.wing_station(5.47, h=None)["le"].z - 0.055
_c55 = (math.sin(math.radians(55)), -math.cos(math.radians(55)), 0)
RED, GREEN, WHITE = (1.0, 0.08, 0.05), (0.15, 1.0, 0.35), (1.0, 0.97, 0.9)

# (type, name, position, direction, spread in degrees (360 = all round), colour)
LIGHTS = [
    ("Navigation", "Nav_L", (parts.WING_TIP + 0.03, Y(_nav_s), _nav_z), _c55, 110, RED),
    ("Navigation", "Nav_R", (-parts.WING_TIP - 0.03, Y(_nav_s), _nav_z), (-_c55[0], _c55[1], 0), 110, GREEN),
    ("Navigation", "Nav_Tail", P(0, 8.20, 1.33), AFT, 140, WHITE),
    ("Strobe", "Strobe_L", (parts.WING_TIP + 0.03, Y(_nav_s + 0.06), _nav_z), UP, 360, WHITE),
    ("Strobe", "Strobe_R", (-parts.WING_TIP - 0.03, Y(_nav_s + 0.06), _nav_z), UP, 360, WHITE),
    ("Beacon", "Beacon", P(0, 7.40, 2.69), UP, 360, RED),
    ("Landing", "Landing", (1.55, Y(parts.WING_LE_S - 0.01), parts.WING_ROOT_Z + 0.05), FWD, 30, WHITE),
    ("Taxi", "Taxi", (1.72, Y(parts.WING_LE_S - 0.01), parts.WING_ROOT_Z + 0.06), FWD, 60, WHITE),
]

# ============================================================== previews: (camera position, target, lens; negative = ortho scale)

VIEWS = {
    "front34": ((-7.5, -9.0, 3.6), (0.0, -0.4, 1.3), 40),
    "side": ((24.0, 1.3, 1.5), (0.0, 1.3, 1.4), 70),
    "rear34": ((8.0, 9.0, 4.2), (0.0, 0.8, 1.4), 40),
    "top": ((0.0, 0.6, 16.0), (0.0, 0.6, 0.0), 50),
    "front": ((0.0, -12.5, 1.5), (0.0, 0.0, 1.4), 45),
    "nose_close": ((-2.6, -5.2, 2.1), (0.0, -2.2, 1.3), 40),
    "cabin_close": ((4.5, -2.4, 2.6), (0.0, 0.2, 1.6), 40),
    "tail_close": ((-3.8, 7.6, 2.6), (0.0, 4.8, 1.8), 40),
    "game": ((-14.0, -10.0, 13.0), (0.0, 0.4, 0.8), 32),
    "side_ortho": ((30.0, 1.3, 1.4), (0.0, 1.3, 1.4), -9.5),
    "top_ortho": ((0.0, 1.3, 30.0), (0.0, 1.3, 0.0), -12.0),
    "front_ortho": ((0.0, -30.0, 1.4), (0.0, 0.0, 1.4), -12.0),
}
