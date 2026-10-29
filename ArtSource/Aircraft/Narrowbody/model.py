"""Narrowbody twinjet (A320-class): the model definition the shared pipeline (../Common/pipeline.py) builds.

Stations `s` are metres aft of the nose; Blender Y = s - S_REF. Passenger windows and doors are painted into
the shading map rather than modelled: at airport-camera distances that reads the same and costs nothing.
"""
import numpy as np

import parts
from geom import MeshBuilder
from paint import curve_dist, round_rect_sdf
from parts import P, Y, Fuselage, S_REF  # noqa: F401  (Fuselage / S_REF are read by the pipeline)
from pipeline import Mover

NAME = "Narrowbody"
DISPLAY_NAME = "Narrowbody twinjet"
CATEGORY = "Airliner"
AO_DISTANCE = 2.5

# ============================================================== geometry


def build():
    body = MeshBuilder()
    body.merge(parts.build_fuselage())
    body.merge(parts.build_windows())
    wing = parts.build_wing()
    body.merge(wing.pop("Wing"))
    body.merge(parts.build_sharklets())
    ht = parts.build_htail()
    body.merge(ht.pop("HStab"))
    vt = parts.build_vtail()
    body.merge(vt.pop("VStab"))
    gear = parts.build_gear()
    body.merge(gear.pop("Gear"))
    body.merge(parts.build_details())
    movers = []
    for sign, side in ((1, "L"), (-1, "R")):
        nacelle, fan, hub = parts.build_engine(sign)
        body.merge(nacelle)
        movers.append(Mover("Fan_" + side, fan, hub))
    movers.append(Mover("Rudder", vt["Rudder"], parts.vt_hinge(8.6), hinge=parts.vt_hinge(11.2) - parts.vt_hinge(6.1),
                        axis="z"))
    for side, sgn in (("L", 1), ("R", -1)):
        a, b = parts.ht_hinge(sgn * 1.0), parts.ht_hinge(sgn * 5.97)
        movers.append(Mover("Elevator_" + side, ht["Elevator_" + side], (a + b) / 2, hinge=b - a))
    for side, sgn in (("L", 1), ("R", -1)):
        for pattern, y0, y1 in parts.TE_PIECES:
            if pattern == "filler":
                continue
            h0, h1 = parts.hinge_point(sgn * y0), parts.hinge_point(sgn * y1)
            name = pattern.format(side)
            movers.append(Mover(name, wing[name], (h0 + h1) / 2, hinge=h1 - h0))
    movers.append(Mover("NoseGear", gear["NoseGear"], P(0, parts.NOSE_S, parts.NOSE_AXLE_Z)))
    for side, sx in (("L", 1), ("R", -1)):
        movers.append(Mover("Wheel_Nose_" + side, gear["Wheel_Nose_" + side],
                            P(sx * parts.NOSE_WHEEL_X, parts.NOSE_S, parts.NOSE_AXLE_Z), parent="NoseGear"))
        for k, dx in ((1, -parts.MAIN_WHEEL_DX), (2, parts.MAIN_WHEEL_DX)):
            movers.append(Mover(f"Wheel_{side}{k}", gear[f"Wheel_{side}{k}"],
                                P(sx * (parts.MAIN_X + dx), parts.S_REF, parts.MAIN_AXLE_Z)))
    return body, movers


LOD_RATIOS = {1: 0.22, 2: 0.06}
LOD1_KEEP = []          # fans are invisible behind the intake lip at LOD1 sizes
COLLIDER_RATIO = 0.3

# ============================================================== UV atlas

UV_TOP_BOTTOM = {"wing", "wing_flap", "wing_aileron", "wing_filler", "htail", "elevator"}
UV_SIDE = {"vtail", "rudder", "dorsal", "pylon", "sharklet"}
DENSITY = {"wing": 0.5, "wing_flap": 0.5, "wing_aileron": 0.5, "wing_filler": 0.4, "htail": 0.6, "elevator": 0.6,
           "wing_cap": 0.1, "htail_cap": 0.2, "vtail_cap": 0.2, "elevator_cap": 0.2, "wing_flap_cap": 0.15,
           "wing_aileron_cap": 0.15, "wing_filler_cap": 0.15, "rudder_cap": 0.2, "sharklet_cap": 0.2,
           "nacelle": 0.8, "pylon": 0.5, "pylon_cap": 0.2, "gearleg": 0.3, "spinner": 0.5}
BOTTOM_DENSITY = 0.5

# ============================================================== livery schemes

CHEAT_Z = 3.92            # passenger-window line
RADOME_S = 1.35           # radome joint at the tip's height; it slants forwards to pass just ahead of the windshield
DOOR_1_S = 5.6            # forward doors (L1 / R1), well behind the flight deck as on the A220


def _radome(p):
    """Signed distance to the radome joint, negative on the radome. The joint leans forwards at the top, so it
    passes just ahead of the windshield and well behind the tip lower down."""
    return p.s - (RADOME_S - 0.17 * (p.z - parts.KEEL - parts.TIP_H))


def _tail(p):
    return p.f(p.vt)


def scheme_classic(p):
    """Cheatline through the windows sweeping up into the fin, a pinstripe below, painted engines."""
    knots = [(0.5, 3.15), (3.0, 3.45), (6.0, CHEAT_Z - 0.38), (25.0, CHEAT_Z - 0.38), (28.5, 4.25), (30.6, 5.5),
             (32.6, 8.3), (34.4, 11.4)]
    d = curve_dist(p.s, p.z, knots)
    zone = (p.fus | p.vt) * p.inside(-_radome(p))           # stripes stop at the radome joint, not at the tip
    r = p.band(d, -0.32, 0.12) * zone
    g = p.band(d, -0.52, -0.42) * zone
    b = p.f(p.group("nacelle"))
    return r, g, b


def scheme_swoosh(p):
    """A belly-to-tail swoosh that grows from the wing root, the whole fin in colour 3."""
    lo = [(13.0, 2.4), (20.0, 2.0), (28.0, 2.4), (33.0, 3.8), (37.6, 5.6)]
    hi = [(13.0, 2.4), (20.0, 3.0), (27.0, 4.1), (31.0, 5.4), (34.0, 6.6)]
    zone = p.fus
    start = p.inside(13.0 - p.s)
    d_lo, d_hi = curve_dist(p.s, p.z, lo), curve_dist(p.s, p.z, hi)
    r = np.minimum(p.inside(-d_lo), p.inside(d_hi)) * start * zone
    g = p.band(d_hi, 0.10, 0.22) * start * zone
    b = np.maximum(_tail(p), p.f(p.group("nacelle")))
    return r, g, b


def scheme_two_tone(p):
    """Airline standard: grey belly under a cheatline, painted tail and engines."""
    cheat = [(-1, 2.45), (4.0, 2.75), (12.0, 2.78), (28.0, 2.78), (33.0, 3.4), (38.0, 4.4)]
    d = curve_dist(p.s, p.z, cheat)
    r = p.inside(d + 0.10) * p.fus
    g = p.band(d, -0.10, 0.10) * p.fus
    b = np.maximum(_tail(p), p.f(p.group("nacelle")))
    return r, g, b


def scheme_trainer(p):
    """Charter / 'eurowhite': coloured nose cone, engine cowls, sharklets and a window-line stripe."""
    r = np.maximum.reduce([p.inside(_radome(p)) * p.fus, p.f(p.group("nacelle"))])
    g = np.maximum(p.f(p.group("sharklet")), p.inside(10.0 - p.z) * p.vt)
    d = p.z - (CHEAT_Z - 0.55)
    b = p.band(d, -0.06, 0.06) * p.fus * p.inside(2.8 - p.s) * p.inside(p.s - 34.0) * (np.abs(p.nx) > 0.35)
    return r, g, b


SCHEMES = {"Classic": scheme_classic, "Swoosh": scheme_swoosh, "TwoTone": scheme_two_tone, "Trainer": scheme_trainer}

# Registration on the rear fuselage below the fin (arc metres from the widest line, up positive)
REG_BOX = {"Classic": (26.6, 29.5, -1.02, -0.67), "Swoosh": (24.5, 27.4, 1.15, 1.50),
           "TwoTone": (26.6, 29.5, 0.30, 0.65), "Trainer": (26.6, 29.5, 0.30, 0.65)}
REG_FACES_U = (23.9, 32.9)
REG_FACES_V = (-1.2, 1.7)
REG_TEX_W = 768           # 85 px/m on the patch (the atlas is 53): sharp letters, 0.28 MB per aircraft

LOGO_BOX = (31.4, 35.2, 6.6, 10.3)

# ============================================================== shading, dirt

WINDOWS = [6.6 + 0.762 * k for k in range(28) if k not in (9, 10)]     # 26 per side, gap at the overwing exits


def _windows(p):
    side = (np.abs(p.nx) > 0.5) & p.fus
    w = np.zeros_like(p.x)
    for s in WINDOWS:
        w = np.maximum(w, p.inside(round_rect_sdf(p.s, p.z, s, CHEAT_Z, 0.12, 0.17, 0.10)))
    return w * side


def _doors(p):
    """Door outlines on both sides: L1/R1, overwing exits, L2/R2, cargo doors."""
    side = (np.abs(p.nx) > 0.35) & p.fus
    lines = []
    for s, z, hw, hh, r in ((DOOR_1_S, 3.55, 0.42, 0.95, 0.15), (13.95, 3.75, 0.25, 0.5, 0.10), (14.72, 3.75, 0.25, 0.5, 0.10),
                            (30.95, 3.62, 0.40, 0.92, 0.15), (11.3, 2.55, 0.9, 0.62, 0.08), (23.2, 2.55, 0.9, 0.62, 0.08)):
        lines.append(p.line(round_rect_sdf(p.s, p.z, s, z, hw, hh, r), 0.012) * side)
    return np.maximum.reduce(lines)


def panel_lines(p):
    w = 0.012
    lines = [_doors(p)]
    # radome joint (subtle) and a few fuselage barrel joints
    lines.append(p.line(_radome(p), w * 0.8) * p.fus * 0.4)
    for s in (6.3, 12.4, 20.8, 27.4, 33.5):
        lines.append(p.line(p.s - s, w * 0.7) * p.fus * 0.5)
    # wing-to-body fairing outline
    fair = (p.s > 12.4) & (p.s < 21.8) & (p.z < 2.6) & p.fus
    lines.append(p.line(p.z - 2.55, w) * fair)
    # engine cowl split lines
    nac = p.group("nacelle")
    lines.append(p.line(p.s - (parts.INLET_S + 0.6), w) * nac)
    lines.append(p.line(p.s - (parts.INLET_S + 2.7), w) * nac)
    return np.clip(np.maximum.reduce(lines), 0, 1)


def detail_extra(p):
    """Passenger windows, painted as dark glass with a light frame. Cockpit windows are real geometry."""
    win = _windows(p)
    return 1.0 - 0.9 * win


def dirt_extra(p):
    """Exhaust staining behind the engines' core nozzles on the wing undersides, grimy belly and gear bays."""
    stain = np.zeros_like(p.x)
    for sx in (1, -1):
        stain = np.maximum(stain, np.exp(-((p.x - sx * parts.ENGINE_X) / 1.4) ** 2) * (p.s > 15.0) * (p.nz < 0))
    belly = p.fus * (p.nz < -0.6) * p.inside(p.s - 22.0) * p.inside(12.0 - p.s)
    return -0.10 * stain * p.wing - 0.08 * belly


# ============================================================== gameplay data (Blender space; the pipeline converts)

def _wheels():
    out = [("Wheel_Nose_L", P(parts.NOSE_WHEEL_X, parts.NOSE_S, parts.NOSE_AXLE_Z), parts.NOSE_R, True),
           ("Wheel_Nose_R", P(-parts.NOSE_WHEEL_X, parts.NOSE_S, parts.NOSE_AXLE_Z), parts.NOSE_R, True)]
    for side, sx in (("L", 1), ("R", -1)):
        for k, dx in ((1, -parts.MAIN_WHEEL_DX), (2, parts.MAIN_WHEEL_DX)):
            out.append((f"Wheel_{side}{k}", P(sx * (parts.MAIN_X + dx), parts.S_REF, parts.MAIN_AXLE_Z), parts.MAIN_R,
                        False))
    return out


GEAR = {"wheels": _wheels(), "steering_pivot": P(0, parts.NOSE_S, 0.0), "max_steering": 75.0}

# runway needs: A320-200 airport-planning figures at maximum weight, sea level, ISA (field lengths). Jets
# need a paved runway.
PERFORMANCE = {"takeoff_distance": 2100.0, "landing_distance": 1500.0, "max_takeoff_mass": 78000.0, "unpaved": False,
               # m/s: taxi 20 kn on straights, rotate (VR) 145 kn, approach (VREF) 137 kn, initial climb
               # 2,500 ft/min at 165 kn
               "taxi_speed": 10.0, "rotate_speed": 74.0, "approach_speed": 70.5, "climb_rate": 12.7, "climb_speed": 85.0}


def _side(s, z, sign):
    return P(sign * Fuselage.side_x(s, z), s, z)


FWD, AFT, UP, DOWN = (0, -1, 0), (0, 1, 0), (0, 0, 1), (0, 0, -1)

POINTS = [
    # doors: position = sill centre
    ("PassengerDoor", "Door_L1", _side(DOOR_1_S, 2.62, 1), (1, 0, 0), (0.81, 1.85)),
    ("PassengerDoor", "Door_R1", _side(DOOR_1_S, 2.62, -1), (-1, 0, 0), (0.81, 1.85)),
    ("PassengerDoor", "Door_L2", _side(30.95, 2.72, 1), (1, 0, 0), (0.81, 1.85)),
    ("PassengerDoor", "Door_R2", _side(30.95, 2.72, -1), (-1, 0, 0), (0.81, 1.85)),
    ("CargoDoor", "Cargo_Fwd", _side(11.3, 1.95, -1), (-1, 0, 0), (1.8, 1.24)),
    ("CargoDoor", "Cargo_Aft", _side(23.2, 1.95, -1), (-1, 0, 0), (1.8, 1.24)),
    ("Fuel", "Fuel", (-8.0, Y(parts.le_s(8.0) + 1.2), parts.wing_surface_z(8.0, parts.le_s(8.0) + 1.2)), DOWN, (0.3, 0.3)),
    ("GroundPower", "GPU", P(-0.55, 3.9, float(Fuselage.zb(3.9)) + 0.05), (-0.4, 0, -0.9), (0.2, 0.2)),
    ("Water", "Water", P(0.6, 26.5, float(Fuselage.zb(26.5)) + 0.05), (0.4, 0, -0.9), (0.15, 0.15)),
    ("Lavatory", "Lav", P(-0.6, 29.0, float(Fuselage.zb(29.0)) + 0.05), (-0.4, 0, -0.9), (0.15, 0.15)),
    ("TowAttach", "TowBar", P(0, parts.NOSE_S - 0.2, parts.NOSE_AXLE_Z), FWD, (0, 0)),
    ("WheelChock", "Chock_Nose", P(0, parts.NOSE_S, 0), FWD, (0.6, 0)),
    ("WheelChock", "Chock_L", P(parts.MAIN_X, parts.S_REF, 0), FWD, (1.3, 0)),
    ("WheelChock", "Chock_R", P(-parts.MAIN_X, parts.S_REF, 0), FWD, (1.3, 0)),
]

_tip_z = parts.wing_station(parts.WING_TIP, h=None)["le"].z
RED, GREEN, WHITE = (1.0, 0.08, 0.05), (0.15, 1.0, 0.35), (1.0, 0.97, 0.9)

LIGHTS = [
    ("Navigation", "Nav_L", (parts.WING_TIP + 0.1, Y(parts.NAV_S), _tip_z + 0.02), (0.82, -0.57, 0), 110, RED),
    ("Navigation", "Nav_R", (-parts.WING_TIP - 0.1, Y(parts.NAV_S), _tip_z + 0.02), (-0.82, -0.57, 0), 110, GREEN),
    ("Navigation", "Nav_Tail", P(0, Fuselage.S1 + 0.08, 4.50), AFT, 140, WHITE),
    ("Strobe", "Strobe_L", (parts.WING_TIP + 0.08, Y(parts.NAV_S + 0.32), _tip_z + 0.03), UP, 360, WHITE),
    ("Strobe", "Strobe_R", (-parts.WING_TIP - 0.08, Y(parts.NAV_S + 0.32), _tip_z + 0.03), UP, 360, WHITE),
    ("Strobe", "Strobe_Tail", P(0, Fuselage.S1 + 0.1, 4.62), AFT, 360, WHITE),
    ("Beacon", "Beacon_Top", P(0, 17.0, float(Fuselage.zt(17.0)) + 0.1), UP, 360, RED),
    ("Beacon", "Beacon_Bottom", P(0, 19.5, Fuselage.bottom_z(19.5, 0) - 0.1), DOWN, 360, RED),
    ("Landing", "Landing_L", (3.0, Y(14.4), parts.wing_surface_z(3.0, 14.4) - 0.1), (0, -0.98, -0.18), 25, WHITE),
    ("Landing", "Landing_R", (-3.0, Y(14.4), parts.wing_surface_z(3.0, 14.4) - 0.1), (0, -0.98, -0.18), 25, WHITE),
    ("Taxi", "Taxi", P(0, parts.NOSE_S - 0.2, 1.15), (0, -0.97, -0.24), 50, WHITE),
    ("Logo", "Logo_L", P(2.2, 33.6, parts.HT_Z + 0.4), (-0.45, 0.25, 0.86), 35, WHITE),
    ("Logo", "Logo_R", P(-2.2, 33.6, parts.HT_Z + 0.4), (0.45, 0.25, 0.86), 35, WHITE),
]

# ============================================================== previews

VIEWS = {
    "front34": ((-30.0, -38.0, 12.0), (0.0, -2.0, 3.5), 40),
    "rear34": ((30.0, 36.0, 14.0), (0.0, 4.0, 4.0), 40),
    "side": ((80.0, 0.0, 4.5), (0.0, 0.0, 4.5), 50),
    "top": ((0.0, 0.0, 70.0), (0.0, 0.0, 0.0), 35),
    "front": ((0.0, -55.0, 4.0), (0.0, 0.0, 4.0), 45),
    "nose_close": ((-8.0, -24.0, 6.0), (0.0, -14.0, 4.0), 40),
    "nose_side": ((30.0, -13.5, 4.2), (0.0, -13.5, 4.2), -9.0),
    "nose_front": ((-3.0, -40.0, 4.6), (0.0, -15.0, 4.4), 60),
    "nose_34": ((-9.0, -26.0, 5.5), (0.0, -14.5, 4.6), 45),
    "photo": ((15.0, -27.0, 2.6), (0.0, -14.8, 4.3), 50),       # low, front-left, like a spotter's photo
    "tail_close": ((-14.0, 26.0, 9.0), (0.0, 15.0, 7.0), 40),
    "engine_close": ((-11.0, -16.0, 3.0), (-5.75, -6.0, 1.8), 40),
    "engine_rear": ((-9.5, 5.0, 2.6), (-5.75, -1.5, 1.7), 40),
    "dorsal_close": ((-7.0, 6.0, 9.0), (0.0, 11.5, 6.4), 40),
    "game": ((-55.0, -40.0, 50.0), (0.0, 0.0, 2.0), 32),
    "side_ortho": ((80.0, 0.0, 5.0), (0.0, 0.0, 5.0), -40.0),
    "top_ortho": ((0.0, 0.0, 80.0), (0.0, 0.0, 0.0), -40.0),
    "front_ortho": ((0.0, -80.0, 5.0), (0.0, 0.0, 5.0), -38.0),
}
