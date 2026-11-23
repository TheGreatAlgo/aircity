"""The Starling B12 flagship business jet, its livery and gameplay data."""

import math

import numpy as np

import parts
from airlines import LIVERIES as SHARED_LIVERIES
from geom import airfoil_eval
from paint import curve_dist, round_rect_sdf
from parts import Fuselage, P, S_REF
from pipeline import Mover


NAME = "FlagshipJet"
DISPLAY_NAME = "Flagship private jet"
SEATS = 12
CATEGORY = "BusinessJet"
AO_DISTANCE = 2.1
CARGO_HINGES = {"Baggage": ("Forward", 105.0, 2.5)}
AIRSTAIR = ("Door_L1", 132.0, 3.6)
LIVERIES = {
    "GraphiteSilver": ("Classic", "#CED5DB", "#303842", "#E4EAF0", "#485666", "#27313A", "C-FSTL", None),
    **SHARED_LIVERIES,
}


def build():
    body = parts.build_fuselage()
    parts.build_windows(body)
    movers = parts.build_surfaces(body)
    for sign, side in ((1, "L"), (-1, "R")):
        fan, hub = parts.build_engine(body, sign)
        movers.append(Mover(f"Fan_{side}", fan, hub))
    nose, wheels = parts.build_gear(body)
    movers.append(Mover("NoseGear", nose, P(0, parts.NOSE_S, parts.NOSE_R)))
    movers.extend(Mover(name, mesh, axle, parent="NoseGear" if steerable else None)
                  for name, mesh, axle, steerable in wheels)
    return body, movers


LOD_RATIOS = {1: .24, 2: .075}
LOD1_KEEP = ["Airstair_Door_L1"]
COLLIDER_RATIO = .35
UV_TOP_BOTTOM = {"wing", "wing_flap", "wing_aileron", "wing_filler", "htail", "elevator", "pylon"}
UV_SIDE = {"vtail", "rudder"}
DENSITY = {"wing": .71, "wing_flap": .71, "wing_aileron": .71, "wing_filler": .55, "htail": .76,
           "elevator": .76, "pylon": .60, "nacelle": .98, "wing_cap": .20, "wing_flap_cap": .18,
           "wing_aileron_cap": .18, "wing_filler_cap": .18, "htail_cap": .18, "elevator_cap": .18,
           "vtail_cap": .20, "rudder_cap": .20, "pylon_cap": .20}
BOTTOM_DENSITY = .62


def _tips(p):
    return np.maximum(p.inside(10.72 - np.abs(p.x)) * p.wing,
                      p.inside(6.48 - p.z) * p.vt)


def scheme_classic(p):
    belly = curve_dist(p.s, p.z, [(0, 1.96), (1.6, 2.02), (4.4, 2.10), (12.2, 2.12), (15.1, 2.46),
                                  (18.1, 3.12), (20.4, 3.90), (23.7, 5.72), (25.7, 6.92)])
    body = p.fus | p.vt
    accent = p.band(belly, -.015, .077) * body
    engine_belly = p.inside(p.z - (parts.ENGINE_Z - .26)) * p.group("nacelle")
    trim = np.maximum(_tips(p), engine_belly)
    return p.inside(belly) * body, accent, trim


def scheme_swoosh(p):
    low = curve_dist(p.s, p.z, [(0, 1.75), (8, 1.78), (13, 1.85), (19, 2.38), (26, 3.24)])
    high = curve_dist(p.s, p.z, [(0, 1.91), (8, 1.94), (13, 2.18), (18, 2.72), (21.5, 4.05), (26, 7.0)])
    body = p.fus | p.vt
    return (np.minimum(p.inside(-low), p.inside(high)) * body, p.band(high, .028, .088) * body,
            np.maximum(_tips(p), p.f(p.group("nacelle"))))


def scheme_two_tone(p):
    cheat = curve_dist(p.s, p.z, [(0, 1.97), (6.2, 2.08), (13.0, 2.13), (19.5, 2.39), (26, 2.63)])
    return p.inside(cheat + .05) * p.fus, p.band(cheat, -.05, .05) * p.fus, np.maximum(_tips(p), p.f(p.vt))


def scheme_trainer(p):
    nose = p.inside(p.s - 1.53 - .30 * (p.z - 2.40)) * p.fus
    stripe = p.band(p.z, 2.25, 2.34) * p.fus * p.inside(2.08 - p.s) * p.inside(p.s - 16.60)
    return nose, _tips(p), stripe


SCHEMES = {"Classic": scheme_classic, "Swoosh": scheme_swoosh, "TwoTone": scheme_two_tone, "Trainer": scheme_trainer}
REG_BOX = dict.fromkeys(SCHEMES, (14.30, 15.16, .085, .265))
REG_FACES_U, REG_FACES_V = parts.REG_S, (-.15, .43)
REG_TEX_W = 896  # A one-metre patch stays sharp within the fleet's texture budget.
LOGO_BOX = (21.77, 23.91, 5.03, 6.38)


def panel_lines(p):
    port, starboard = p.fus & (p.nx > .45), p.fus & (p.nx < -.45)
    door = round_rect_sdf(p.s, p.z, parts.DOOR_S, parts.DOOR_SILL + parts.DOOR_H / 2,
                          parts.DOOR_W / 2, parts.DOOR_H / 2, .13)
    baggage = round_rect_sdf(p.s, p.z, parts.BAG_S, parts.BAG_SILL + parts.BAG_H / 2,
                             parts.BAG_W / 2, parts.BAG_H / 2, .045)
    handle = round_rect_sdf(p.s, p.z, parts.DOOR_S + .304, 2.42, .074, .021, .013)
    return np.maximum.reduce([
        .68 * p.line(door, .009) * port, .68 * p.line(baggage, .007) * starboard, p.inside(handle) * port,
        .37 * p.line(p.s - (1.55 + .22 * (p.z - 2.76)), .008) * p.fus,
        .37 * p.line(p.s - (parts.ENGINE_S + .87), .008) * p.group("nacelle"),
        .33 * p.line(p.s - (parts.ENGINE_S + 2.64), .008) * p.group("nacelle"),
    ])


def detail_extra(p):
    return None


def dirt_extra(p):
    return -.075 * p.group("nacelle") * p.inside(parts.ENGINE_S + 2.45 - p.s) * np.clip((3.82 - p.z) / .64, 0, 1)


FWD, AFT, UP, DOWN = (0, -1, 0), (0, 1, 0), (0, 0, 1), (0, 0, -1)
NOSE_AXLE = P(0, parts.NOSE_S, parts.NOSE_R)
GEAR = {
    "wheels": [("Wheel_Nose", NOSE_AXLE, parts.NOSE_R, True),
               ("Wheel_L", P(parts.MAIN_X, S_REF, parts.MAIN_R), parts.MAIN_R, False),
               ("Wheel_R", P(-parts.MAIN_X, S_REF, parts.MAIN_R), parts.MAIN_R, False)],
    "steering_pivot": P(0, parts.NOSE_S, 0),
    "max_steering": 62.0,
}

# Fictional airport-planning figures, in metres, kilograms and metres per second.
PERFORMANCE = {"takeoff_distance": 1590.0, "landing_distance": 1230.0, "max_takeoff_mass": 22100.0,
               "unpaved": False, "taxi_speed": 8.0, "rotate_speed": 64.0, "approach_speed": 66.0,
               "climb_rate": 11.2, "climb_speed": 85.0}


def _side(sign, s, z):
    return P(sign * Fuselage.side_x(s, z), s, z)


def _fuel(sign):
    station = parts.wing_station(sign * 3.12, h=None)
    upper, _, _ = airfoil_eval(parts.WING_AF)(np.array([.40]))
    return station["le"] + station["chord_dir"] * (station["c"] * .40) + station["up"] * (station["c"] * upper[0])


POINTS = [
    ("PassengerDoor", "Door_L1", _side(1, parts.DOOR_S, parts.DOOR_SILL), (1, 0, 0), (parts.DOOR_W, parts.DOOR_H)),
    ("CargoDoor", "Baggage", _side(-1, parts.BAG_S, parts.BAG_SILL), (-1, 0, 0), (parts.BAG_W, parts.BAG_H)),
    ("Fuel", "Fuel_L", _fuel(1), UP, (.12, .12)),
    ("Fuel", "Fuel_R", _fuel(-1), UP, (.12, .12)),
    ("TowAttach", "TowBar", P(0, parts.NOSE_S - .21, parts.NOSE_R), FWD, (0, 0)),
    ("WheelChock", "Chock_Nose", P(0, parts.NOSE_S, 0), FWD, (parts.NOSE_WIDTH, 0)),
    ("WheelChock", "Chock_L", P(parts.MAIN_X, S_REF, 0), FWD, (parts.MAIN_WIDTH, 0)),
    ("WheelChock", "Chock_R", P(-parts.MAIN_X, S_REF, 0), FWD, (parts.MAIN_WIDTH, 0)),
]

RED, GREEN, WHITE = (1, .07, .04), (.12, 1, .31), (1, .97, .91)
LIGHTS = [
    ("Navigation", "Nav_L", P(11.47, 15.02, 3.05), (.82, -.57, 0), 110, RED),
    ("Navigation", "Nav_R", P(-11.47, 15.02, 3.05), (-.82, -.57, 0), 110, GREEN),
    ("Navigation", "Nav_Tail", P(0, 26.02, 2.60), AFT, 140, WHITE),
    ("Strobe", "Strobe_L", P(11.43, 15.16, 3.05), UP, 360, WHITE),
    ("Strobe", "Strobe_R", P(-11.43, 15.16, 3.05), UP, 360, WHITE),
    ("Beacon", "Beacon_Top", P(0, 13.15, 4.40), UP, 360, RED),
    ("Beacon", "Beacon_Bottom", P(0, 6.25, 1.36), DOWN, 360, RED),
    ("Landing", "Landing_L", P(1.57, 7.96, 1.64), FWD, 35, WHITE),
    ("Landing", "Landing_R", P(-1.57, 7.96, 1.64), FWD, 35, WHITE),
    ("Taxi", "Taxi", P(0, parts.NOSE_S - .082, .74), FWD, 60, WHITE),
]

VIEWS = {
    "front34": ((24, -30, 13), (0, -1.35, 2.8), 50),
    "rear34": ((-26, 28, 10.5), (0, -1.4, 2.7), 48),
    "side": ((45, -1.55, 4.75), (0, -1.62, 3.15), 52),
    "game": ((-31, -32, 41), (0, -1.4, 2.5), 56),
    "top": ((0, -1.62, 54), (0, -1.62, 0), 37),
    "airstair": ((9.1, -15.2, 4.8), (.72, -9.65, 1.93), 45),
}


def preview_pose(view):
    import bpy

    stair = bpy.data.objects.get("Airstair_" + AIRSTAIR[0])
    if stair is not None:
        stair.rotation_euler.y = math.radians(AIRSTAIR[1]) if view == "airstair" else 0.0
