"""The Starling B8 executive twinjet, its livery and gameplay data."""

import math

import numpy as np

import parts
from airlines import LIVERIES as SHARED_LIVERIES
from geom import airfoil_eval
from paint import curve_dist, round_rect_sdf
from parts import Fuselage, P, S_REF
from pipeline import Mover


NAME = "ExecutiveJet"
DISPLAY_NAME = "Executive twinjet"
SEATS = 8
CATEGORY = "BusinessJet"
AO_DISTANCE = 1.65
CARGO_HINGES = {"Baggage": ("Forward", 105.0, 2.5)}
AIRSTAIR = ("Door_L1", 148.0, 3.4)
LIVERIES = {
    "MidnightChampagne": ("Classic", "#F7EEDA", "#152441", "#CEB785", "#152441", "#152441", "C-FSBX", None),
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
    return np.maximum(p.inside(8.43 - np.abs(p.x)) * p.wing,
                      p.inside(5.76 - p.z) * p.vt)


def scheme_classic(p):
    belly = curve_dist(p.s, p.z, [(0, 1.72), (4.4, 1.88), (10.9, 1.90), (13.1, 2.02),
                                  (14.9, 2.62), (16.35, 3.57), (18.22, 5.39), (19.1, 6.12)])
    body = p.fus | p.vt
    accent = p.band(belly, -.005, .058) * body
    engine_belly = p.inside(p.z - (parts.ENGINE_Z - .25)) * p.group("nacelle")
    trim = np.maximum(_tips(p), engine_belly)
    return p.inside(belly) * body, accent, trim


def scheme_swoosh(p):
    low = curve_dist(p.s, p.z, [(0, 1.44), (7, 1.49), (12, 1.64), (16, 2.04), (20, 2.64)])
    high = curve_dist(p.s, p.z, [(0, 1.51), (7, 1.64), (11, 1.96), (14.6, 2.64), (17.2, 4.0), (20, 6.6)])
    body = p.fus | p.vt
    return (np.minimum(p.inside(-low), p.inside(high)) * body, p.band(high, .034, .085) * body,
            np.maximum(_tips(p), p.f(p.group("nacelle"))))


def scheme_two_tone(p):
    cheat = curve_dist(p.s, p.z, [(0, 1.71), (5.7, 1.87), (12, 1.92), (16, 2.13), (20, 2.36)])
    return p.inside(cheat + .05) * p.fus, p.band(cheat, -.05, .05) * p.fus, np.maximum(_tips(p), p.f(p.vt))


def scheme_trainer(p):
    nose = p.inside(p.s - 1.20 - .32 * (p.z - 2.0)) * p.fus
    stripe = p.band(p.z, 2.00, 2.06) * p.fus * p.inside(1.65 - p.s) * p.inside(p.s - 12.12)
    return nose, _tips(p), stripe


SCHEMES = {"Classic": scheme_classic, "Swoosh": scheme_swoosh, "TwoTone": scheme_two_tone, "Trainer": scheme_trainer}
REG_BOX = dict.fromkeys(SCHEMES, (11.03, 12.46, .074, .275))
REG_FACES_U, REG_FACES_V = parts.REG_S, (-.15, .43)
REG_TEX_W = 1024
LOGO_BOX = (17.28, 18.76, 4.52, 5.63)


def panel_lines(p):
    port, starboard = p.fus & (p.nx > .45), p.fus & (p.nx < -.45)
    door = round_rect_sdf(p.s, p.z, parts.DOOR_S, parts.DOOR_SILL + parts.DOOR_H / 2,
                          parts.DOOR_W / 2, parts.DOOR_H / 2, .13)
    baggage = round_rect_sdf(p.s, p.z, parts.BAG_S, parts.BAG_SILL + parts.BAG_H / 2,
                             parts.BAG_W / 2, parts.BAG_H / 2, .045)
    handle = round_rect_sdf(p.s, p.z, parts.DOOR_S + .24, 2.15, .064, .020, .012)
    return np.maximum.reduce([
        .68 * p.line(door, .009) * port, .68 * p.line(baggage, .007) * starboard, p.inside(handle) * port,
        .37 * p.line(p.s - (1.12 + .18 * (p.z - 2.30)), .008) * p.fus,
        .37 * p.line(p.s - (parts.ENGINE_S + .74), .008) * p.group("nacelle"),
        .33 * p.line(p.s - (parts.ENGINE_S + 2.18), .008) * p.group("nacelle"),
    ])


def detail_extra(p):
    return None


def dirt_extra(p):
    return -.075 * p.group("nacelle") * p.inside(parts.ENGINE_S + 1.82 - p.s) * np.clip((3.32 - p.z) / .51, 0, 1)


FWD, AFT, UP, DOWN = (0, -1, 0), (0, 1, 0), (0, 0, 1), (0, 0, -1)
NOSE_AXLE = P(0, parts.NOSE_S, parts.NOSE_R)
GEAR = {
    "wheels": [("Wheel_Nose", NOSE_AXLE, parts.NOSE_R, True),
               ("Wheel_L", P(parts.MAIN_X, S_REF, parts.MAIN_R), parts.MAIN_R, False),
               ("Wheel_R", P(-parts.MAIN_X, S_REF, parts.MAIN_R), parts.MAIN_R, False)],
    "steering_pivot": P(0, parts.NOSE_S, 0),
    "max_steering": 59.0,
}

# Fictional airport-planning figures, in metres, kilograms and metres per second.
PERFORMANCE = {"takeoff_distance": 1320.0, "landing_distance": 1040.0, "max_takeoff_mass": 10600.0,
               "unpaved": False, "taxi_speed": 8.0, "rotate_speed": 55.0, "approach_speed": 61.0,
               "climb_rate": 10.5, "climb_speed": 78.0}


def _side(sign, s, z):
    return P(sign * Fuselage.side_x(s, z), s, z)


def _fuel(sign):
    station = parts.wing_station(sign * 2.50, h=None)
    upper, _, _ = airfoil_eval(parts.WING_AF)(np.array([.40]))
    return station["le"] + station["chord_dir"] * (station["c"] * .40) + station["up"] * (station["c"] * upper[0])


POINTS = [
    ("PassengerDoor", "Door_L1", _side(1, parts.DOOR_S, parts.DOOR_SILL), (1, 0, 0), (parts.DOOR_W, parts.DOOR_H)),
    ("CargoDoor", "Baggage", _side(-1, parts.BAG_S, parts.BAG_SILL), (-1, 0, 0), (parts.BAG_W, parts.BAG_H)),
    ("Fuel", "Fuel_L", _fuel(1), UP, (.12, .12)),
    ("Fuel", "Fuel_R", _fuel(-1), UP, (.12, .12)),
    ("TowAttach", "TowBar", P(0, parts.NOSE_S - .15, parts.NOSE_R), FWD, (0, 0)),
    ("WheelChock", "Chock_Nose", P(0, parts.NOSE_S, 0), FWD, (parts.NOSE_WIDTH, 0)),
    ("WheelChock", "Chock_L", P(parts.MAIN_X, S_REF, 0), FWD, (parts.MAIN_WIDTH, 0)),
    ("WheelChock", "Chock_R", P(-parts.MAIN_X, S_REF, 0), FWD, (parts.MAIN_WIDTH, 0)),
]

RED, GREEN, WHITE = (1, .07, .04), (.12, 1, .31), (1, .97, .91)
LIGHTS = [
    ("Navigation", "Nav_L", P(9.00, 12.43, 2.89), (.82, -.57, 0), 110, RED),
    ("Navigation", "Nav_R", P(-9.00, 12.43, 2.89), (-.82, -.57, 0), 110, GREEN),
    ("Navigation", "Nav_Tail", P(0, 20.01, 2.29), AFT, 140, WHITE),
    ("Strobe", "Strobe_L", P(9.00, 12.60, 2.89), UP, 360, WHITE),
    ("Strobe", "Strobe_R", P(-9.00, 12.60, 2.89), UP, 360, WHITE),
    ("Beacon", "Beacon_Top", P(0, 10.89, 3.79), UP, 360, RED),
    ("Beacon", "Beacon_Bottom", P(0, 5.21, 1.16), DOWN, 360, RED),
    ("Landing", "Landing_L", P(1.46, 6.51, 1.37), FWD, 35, WHITE),
    ("Landing", "Landing_R", P(-1.46, 6.51, 1.37), FWD, 35, WHITE),
    ("Taxi", "Taxi", P(0, parts.NOSE_S - .077, .65), FWD, 60, WHITE),
]

VIEWS = {
    "front34": ((18, -25, 11), (0, -1.1, 2.2), 53),
    "rear34": ((-19, 22, 9), (0, -.9, 2.25), 46),
    "side": ((37, .0, 4.3), (0, -1.4, 3.0), 54),
    "game": ((-26, -24, 33), (0, -1.0, 1.8), 59),
    "top": ((0, -1.4, 38), (0, -1.4, 0), 45),
    "cockpit": ((5.6, -21.0, 4.9), (0, -9.42, 2.53), 65),
    "airstair": ((8.9, -10.4, 4.6), (.65, -7.3, 1.71), 43),
}


def preview_pose(view):
    import bpy

    stair = bpy.data.objects.get("Airstair_" + AIRSTAIR[0])
    if stair is not None:
        stair.rotation_euler.y = math.radians(AIRSTAIR[1]) if view == "airstair" else 0.0
