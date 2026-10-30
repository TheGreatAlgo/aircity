"""Widebody twinjet (A350-900-class): the model definition the shared pipeline (../Common/pipeline.py) builds.

Stations `s` are metres aft of the nose; Blender Y = s - S_REF. Passenger windows and doors are painted into
the shading map rather than modelled, as on the Narrowbody.
"""
import numpy as np

import parts
from geom import MeshBuilder
from paint import curve_dist, round_rect_sdf
from parts import P, Y, Fuselage, S_REF  # noqa: F401  (Fuselage / S_REF are read by the pipeline)
from pipeline import Mover

NAME = "Widebody"
DISPLAY_NAME = "Widebody twinjet"
CATEGORY = "Airliner"
AO_DISTANCE = 4.0

# ============================================================== geometry


def build():
    body = MeshBuilder()
    body.merge(parts.build_fuselage())
    body.merge(parts.build_windows())
    wing = parts.build_wing()
    body.merge(wing.pop("Wing"))
    body.merge(parts.build_winglets())
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
    r0, r1 = parts.RUDDER
    movers.append(Mover("Rudder", vt["Rudder"], parts.vt_hinge((r0 + r1) / 2), hinge=parts.vt_hinge(r1) - parts.vt_hinge(r0),
                        axis="z"))
    for side, sgn in (("L", 1), ("R", -1)):
        a, b = parts.ht_hinge(sgn * parts.EL_ROOT), parts.ht_hinge(sgn * 9.32)
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
        for k, ds in parts.MAIN_AXLES:
            movers.append(Mover(f"Wheel_{side}{k}", gear[f"Wheel_{side}{k}"], P(sx * parts.MAIN_X, S_REF + ds, parts.MAIN_AXLE_Z)))
    return body, movers


LOD_RATIOS = {1: 0.2, 2: 0.05}
LOD1_KEEP = []          # fans are invisible behind the intake lip at LOD1 sizes
COLLIDER_RATIO = 0.3

# ============================================================== UV atlas

UV_TOP_BOTTOM = {"wing", "wing_flap", "wing_aileron", "wing_filler", "htail", "elevator"}
UV_SIDE = {"vtail", "rudder", "dorsal", "pylon", "winglet"}
DENSITY = {"wing": 0.45, "wing_flap": 0.45, "wing_aileron": 0.45, "wing_filler": 0.35, "htail": 0.55, "elevator": 0.55,
           "wing_cap": 0.1, "htail_cap": 0.2, "vtail_cap": 0.2, "elevator_cap": 0.2, "wing_flap_cap": 0.15,
           "wing_aileron_cap": 0.15, "wing_filler_cap": 0.15, "rudder_cap": 0.2, "winglet_cap": 0.2,
           "nacelle": 0.8, "pylon": 0.5, "pylon_cap": 0.2, "gearleg": 0.25, "spinner": 0.5, "satcom": 0.4}
BOTTOM_DENSITY = 0.5

# ============================================================== livery schemes

FLOOR_Z = parts.KEEL + 2.30     # cabin floor = passenger-door sills
CHEAT_Z = 5.85                  # passenger-window line
DOORS = (8.4, 19.2, 43.4, 55.0)  # passenger doors 1-4 (both sides)
DOOR_HW, DOOR_HH = 0.535, 0.965  # 1.07 x 1.93 m type A doors
# the radome joint passes ~0.25 m ahead of the windshield's lowest point, leaning forwards at the top
RADOME_S = parts.WS_FRONT - 0.25 + 0.17 * (parts.SILL - parts.TIP_H)


def _radome(p):
    """Signed distance to the radome joint, negative on the radome."""
    return p.s - (RADOME_S - 0.17 * (p.z - parts.KEEL - parts.TIP_H))


def _tail(p):
    return p.f(p.vt)


def scheme_classic(p):
    """Cheatline through the windows sweeping up into the fin, a pinstripe below, painted engines."""
    knots = [(0.7, 4.55), (4.5, 4.95), (9.5, CHEAT_Z - 0.50), (46.0, CHEAT_Z - 0.50), (50.3, 6.6), (52.9, 8.4),
             (55.6, 12.4), (58.8, 16.6), (60.6, 19.0)]       # up the fin ~1.2 m behind its leading edge, out past the tip
    d = curve_dist(p.s, p.z, knots)
    zone = (p.fus | p.vt) * p.inside(-_radome(p))           # stripes stop at the radome joint, not at the tip
    r = p.band(d, -0.48, 0.17) * zone
    g = p.band(d, -0.78, -0.63) * zone
    b = p.f(p.group("nacelle"))
    return r, g, b


def scheme_swoosh(p):
    """A belly-to-tail swoosh that grows from the wing root, the whole fin in colour 3."""
    lo = [(25.0, 3.3), (35.0, 2.8), (49.0, 3.4), (58.0, 5.6), (66.9, 7.4)]
    hi = [(25.0, 3.3), (35.0, 4.5), (46.0, 6.1), (53.0, 8.1), (58.0, 9.9)]
    zone = p.fus
    start = p.inside(25.0 - p.s)
    d_lo, d_hi = curve_dist(p.s, p.z, lo), curve_dist(p.s, p.z, hi)
    r = np.minimum(p.inside(-d_lo), p.inside(d_hi)) * start * zone
    g = p.band(d_hi, 0.15, 0.32) * start * zone
    b = np.maximum(_tail(p), p.f(p.group("nacelle")))
    return r, g, b


def scheme_two_tone(p):
    """Airline standard: grey belly under a cheatline, painted tail and engines."""
    cheat = [(-1, 3.25), (7.0, 3.62), (20.0, 3.68), (47.0, 3.68), (55.0, 4.6), (68.0, 6.6)]
    d = curve_dist(p.s, p.z, cheat)
    r = p.inside(d + 0.15) * p.fus
    g = p.band(d, -0.15, 0.15) * p.fus
    b = np.maximum(_tail(p), p.f(p.group("nacelle")))
    return r, g, b


def scheme_trainer(p):
    """Charter / 'eurowhite': coloured nose cone, engine cowls, winglets and a window-line stripe."""
    r = np.maximum.reduce([p.inside(_radome(p)) * p.fus, p.f(p.group("nacelle"))])
    g = np.maximum(p.f(p.group("winglet")), p.inside(15.2 - p.z) * p.vt)
    d = p.z - (CHEAT_Z - 0.75)
    b = p.band(d, -0.09, 0.09) * p.fus * p.inside(4.5 - p.s) * p.inside(p.s - 60.0) * (np.abs(p.nx) > 0.35)
    return r, g, b


SCHEMES = {"Classic": scheme_classic, "Swoosh": scheme_swoosh, "TwoTone": scheme_two_tone, "Trainer": scheme_trainer}

# Registration on the rear fuselage (arc metres from the widest line, up positive)
REG_BOX = {"Classic": (46.5, 51.5, -1.75, -1.20), "Swoosh": (40.5, 45.5, 1.55, 2.10),
           "TwoTone": (47.0, 52.0, 0.75, 1.30), "Trainer": (47.0, 52.0, 0.75, 1.30)}
REG_FACES_U = (40.0, 52.5)
REG_FACES_V = (-1.95, 2.3)
REG_TEX_W = 1024          # ~82 px/m on the patch (the atlas is 30): sharp 0.55 m letters, 0.52 MB per aircraft

LOGO_BOX = (56.4, 61.2, 9.8, 15.2)

# ============================================================== shading, dirt

WINDOW_PITCH = 0.635
WINDOWS = [s for s in np.arange(9.55, 54.4, WINDOW_PITCH)
           if all(abs(s - d) > 0.75 for d in DOORS)]


def _windows(p):
    side = (np.abs(p.nx) > 0.5) & p.fus
    w = np.zeros_like(p.x)
    for s in WINDOWS:
        w = np.maximum(w, p.inside(round_rect_sdf(p.s, p.z, s, CHEAT_Z, 0.16, 0.23, 0.12)))
    return w * side


def _doors(p):
    """Passenger doors on both sides; cargo and bulk doors on the starboard side."""
    both = (np.abs(p.nx) > 0.35) & p.fus
    stbd = (p.nx < -0.35) & p.fus
    lines = []
    for s in DOORS:
        lines.append(p.line(round_rect_sdf(p.s, p.z, s, FLOOR_Z + DOOR_HH, DOOR_HW, DOOR_HH, 0.22), 0.016) * both)
    for s, z, hw, hh, r in ((14.6, 3.60, 1.35, 0.85, 0.10), (46.0, 3.80, 1.35, 0.85, 0.10), (50.2, 4.02, 0.48, 0.48, 0.08)):
        lines.append(p.line(round_rect_sdf(p.s, p.z, s, z, hw, hh, r), 0.016) * stbd)
    return np.maximum.reduce(lines)


def panel_lines(p):
    w = 0.016
    lines = [_doors(p)]
    # radome joint (subtle) and a few fuselage barrel joints
    lines.append(p.line(_radome(p), w * 0.8) * p.fus * 0.4)
    for s in (6.6, 14.0, 22.6, 44.6, 53.0, 60.5):
        lines.append(p.line(p.s - s, w * 0.7) * p.fus * 0.5)
    # wing-to-body fairing outline
    fair = (p.s > 22.5) & (p.s < 43.5) & (p.z < 4.0) & p.fus
    lines.append(p.line(p.z - 3.92, w) * fair)
    # engine cowl split lines
    nac = p.group("nacelle")
    lines.append(p.line(p.s - (parts.INLET_S + parts.K * 0.6), w) * nac)
    lines.append(p.line(p.s - (parts.INLET_S + parts.K * 2.7), w) * nac)
    return np.clip(np.maximum.reduce(lines), 0, 1)


def detail_extra(p):
    """Passenger windows, painted as dark glass. Cockpit windows are real geometry."""
    win = _windows(p)
    return 1.0 - 0.9 * win


def dirt_extra(p):
    """Exhaust staining behind the engines' core nozzles on the wing undersides, grimy belly and gear bays."""
    stain = np.zeros_like(p.x)
    for sx in (1, -1):
        stain = np.maximum(stain, np.exp(-((p.x - sx * parts.ENGINE_X) / 2.2) ** 2) *
                           (p.s > parts.INLET_S + 7.0) * (p.nz < 0))
    belly = p.fus * (p.nz < -0.6) * p.inside(p.s - 42.0) * p.inside(22.0 - p.s)
    return -0.10 * stain * p.wing - 0.08 * belly


# ============================================================== gameplay data (Blender space; the pipeline converts)

def _wheels():
    out = [("Wheel_Nose_L", P(parts.NOSE_WHEEL_X, parts.NOSE_S, parts.NOSE_AXLE_Z), parts.NOSE_R, True),
           ("Wheel_Nose_R", P(-parts.NOSE_WHEEL_X, parts.NOSE_S, parts.NOSE_AXLE_Z), parts.NOSE_R, True)]
    for side, sx in (("L", 1), ("R", -1)):
        for k, ds in parts.MAIN_AXLES:       # a main "wheel" is an axle: its two wheels roll together
            out.append((f"Wheel_{side}{k}", P(sx * parts.MAIN_X, S_REF + ds, parts.MAIN_AXLE_Z), parts.MAIN_R, False))
    return out


GEAR = {"wheels": _wheels(), "steering_pivot": P(0, parts.NOSE_S, 0.0), "max_steering": 70.0}

# runway needs: A350-900 airport-planning figures at maximum weight, sea level, ISA (field lengths). Jets
# need a paved runway.
PERFORMANCE = {"takeoff_distance": 2700.0, "landing_distance": 2000.0, "max_takeoff_mass": 283000.0, "unpaved": False,
               # m/s: taxi 20 kn on straights, rotate (VR) 152 kn, approach (VREF) 140 kn, initial climb
               # 2,200 ft/min at 170 kn
               "taxi_speed": 10.0, "rotate_speed": 78.0, "approach_speed": 72.0, "climb_rate": 11.2, "climb_speed": 87.5}


def _side(s, z, sign):
    return P(sign * Fuselage.side_x(s, z), s, z)


FWD, AFT, UP, DOWN = (0, -1, 0), (0, 1, 0), (0, 0, 1), (0, 0, -1)
_fuel_y = 13.5
_fuel_s = parts.le_s(_fuel_y) + 1.6

POINTS = [
    # doors: position = sill centre
    *[("PassengerDoor", f"Door_{side}{k + 1}", _side(s, FLOOR_Z, sign), (sign, 0, 0), (1.07, 1.93))
      for k, s in enumerate(DOORS) for side, sign in (("L", 1), ("R", -1))],
    ("CargoDoor", "Cargo_Fwd", _side(14.6, 2.75, -1), (-1, 0, 0), (2.70, 1.70)),
    ("CargoDoor", "Cargo_Aft", _side(46.0, 2.95, -1), (-1, 0, 0), (2.70, 1.70)),
    ("CargoDoor", "Cargo_Bulk", _side(50.2, 3.54, -1), (-1, 0, 0), (0.95, 0.95)),
    ("Fuel", "Fuel", (-_fuel_y, Y(_fuel_s), parts.wing_surface_z(_fuel_y, _fuel_s)), DOWN, (0.4, 0.4)),
    ("GroundPower", "GPU", P(-0.75, 4.6, float(Fuselage.zb(4.6)) + 0.08), (-0.4, 0, -0.9), (0.25, 0.25)),
    ("Water", "Water", P(0.9, 49.5, float(Fuselage.zb(49.5)) + 0.08), (0.4, 0, -0.9), (0.18, 0.18)),
    ("Lavatory", "Lav", P(-0.9, 52.5, float(Fuselage.zb(52.5)) + 0.08), (-0.4, 0, -0.9), (0.18, 0.18)),
    ("TowAttach", "TowBar", P(0, parts.NOSE_S - 0.3, parts.NOSE_AXLE_Z), FWD, (0, 0)),
    ("WheelChock", "Chock_Nose", P(0, parts.NOSE_S, 0), FWD, (1.2, 0)),
    ("WheelChock", "Chock_L", P(parts.MAIN_X, S_REF - parts.BOGIE_DS, 0), FWD, (1.95, 0)),
    ("WheelChock", "Chock_R", P(-parts.MAIN_X, S_REF - parts.BOGIE_DS, 0), FWD, (1.95, 0)),
]

_tip_z = parts.wing_station(parts.WING_TIP, h=None)["le"].z
_logo_z = parts.ht_station(parts.LOGO_LIGHT[0], h=None)["le"].z + 0.5
RED, GREEN, WHITE = (1.0, 0.08, 0.05), (0.15, 1.0, 0.35), (1.0, 0.97, 0.9)
_tail_z = float(Fuselage.zm(Fuselage.S1))

LIGHTS = [
    ("Navigation", "Nav_L", (parts.WING_TIP + 0.15, Y(parts.NAV_S), _tip_z + 0.03), (0.82, -0.57, 0), 110, RED),
    ("Navigation", "Nav_R", (-parts.WING_TIP - 0.15, Y(parts.NAV_S), _tip_z + 0.03), (-0.82, -0.57, 0), 110, GREEN),
    ("Navigation", "Nav_Tail", P(0, Fuselage.S1 + 0.1, _tail_z + 0.32), AFT, 140, WHITE),
    ("Strobe", "Strobe_L", (parts.WING_TIP + 0.12, Y(parts.NAV_S + 0.42), _tip_z + 0.04), UP, 360, WHITE),
    ("Strobe", "Strobe_R", (-parts.WING_TIP - 0.12, Y(parts.NAV_S + 0.42), _tip_z + 0.04), UP, 360, WHITE),
    ("Strobe", "Strobe_Tail", P(0, Fuselage.S1 + 0.12, _tail_z + 0.46), AFT, 360, WHITE),
    ("Beacon", "Beacon_Top", P(0, parts.BEACON_TOP_S, float(Fuselage.zt(parts.BEACON_TOP_S)) + 0.14), UP, 360, RED),
    ("Beacon", "Beacon_Bottom", P(0, parts.BEACON_BOTTOM_S, Fuselage.bottom_z(parts.BEACON_BOTTOM_S, 0) - 0.14), DOWN, 360,
     RED),
    ("Landing", "Landing_L", (parts.LANDING[0], Y(parts.LANDING[1]), parts.wing_surface_z(*parts.LANDING) - 0.12),
     (0, -0.98, -0.18), 25, WHITE),
    ("Landing", "Landing_R", (-parts.LANDING[0], Y(parts.LANDING[1]), parts.wing_surface_z(*parts.LANDING) - 0.12),
     (0, -0.98, -0.18), 25, WHITE),
    ("Taxi", "Taxi", P(0, parts.NOSE_S - 0.26, 1.52), (0, -0.97, -0.24), 50, WHITE),
    ("Logo", "Logo_L", P(parts.LOGO_LIGHT[0], parts.LOGO_LIGHT[1], _logo_z), (-0.5, -0.25, 0.83), 35, WHITE),
    ("Logo", "Logo_R", P(-parts.LOGO_LIGHT[0], parts.LOGO_LIGHT[1], _logo_z), (0.5, -0.25, 0.83), 35, WHITE),
]

# ============================================================== previews

_nose = Y(5.0)
VIEWS = {
    "front34": ((-54.0, -68.0, 21.0), (0.0, -4.0, 6.5), 40),
    "rear34": ((54.0, 65.0, 25.0), (0.0, 7.0, 7.0), 40),
    "side": ((140.0, 0.0, 8.0), (0.0, 0.0, 8.0), 50),
    "top": ((0.0, 0.0, 125.0), (0.0, 0.0, 0.0), 35),
    "front": ((0.0, -100.0, 7.0), (0.0, 0.0, 7.0), 45),
    "nose_close": ((-14.0, _nose - 18.0, 10.0), (0.0, _nose + 1.0, 6.0), 40),
    "nose_side": ((45.0, _nose + 1.5, 6.2), (0.0, _nose + 1.5, 6.2), -17.0),
    "nose_front": ((-5.0, _nose - 40.0, 7.6), (0.0, _nose, 6.8), 60),
    "nose_34": ((-15.0, _nose - 18.0, 9.0), (0.0, _nose + 0.5, 6.6), 45),
    "photo": ((26.0, _nose - 20.0, 3.4), (0.0, _nose + 1.0, 6.2), 50),     # low, front-left, like a spotter's photo
    "tail_close": ((-32.0, 52.0, 19.0), (0.0, 23.0, 10.5), 40),
    "engine_close": ((-20.0, Y(10.0), 4.5), (-parts.ENGINE_X, Y(25.5), 2.9), 40),
    "engine_rear": ((-17.0, Y(41.0), 4.2), (-parts.ENGINE_X, Y(29.0), 2.9), 40),
    "gear_close": ((-13.0, 11.0, 2.2), (-parts.MAIN_X, 0.0, 1.2), 40),
    "wingtip": ((-46.0, -6.0, 12.0), (-31.0, Y(44.5), 7.6), 50),
    "dorsal_close": ((-19.0, 1.0, 14.0), (0.0, Y(49.5), 9.3), 40),
    "game": ((-100.0, -72.0, 90.0), (0.0, 0.0, 3.0), 32),
    "side_ortho": ((140.0, 0.0, 8.5), (0.0, -1.5, 8.5), -72.0),
    "top_ortho": ((0.0, 0.0, 140.0), (0.0, -1.5, 0.0), -72.0),
    "front_ortho": ((0.0, -140.0, 8.5), (0.0, 0.0, 8.5), -68.0),
}
