"""The Starling A2 two-seat trainer, its livery and gameplay data."""

import numpy as np

import parts
from airlines import LIVERIES as SHARED_LIVERIES
from paint import curve_dist, round_rect_sdf
from parts import Fuselage, P, S_REF
from pipeline import Mover

NAME = "LowWingPiston"
DISPLAY_NAME = "Low-wing trainer"
SEATS = 2
CATEGORY = "GeneralAviation"
AO_DISTANCE = 0.85
CARGO_HINGES = {"Baggage": ("Forward", 104.0, 2.2)}
LIVERIES = {
    "SpruceGold": ("Classic", "#F5F2E8", "#174A40", "#BF9954", "#174A40", "#174A40", "C-FSLA", None),
    **SHARED_LIVERIES,
}


def build():
    body = parts.build_fuselage()
    parts.build_windows(body)
    movers = parts.build_surfaces(body)
    propeller, hub = parts.build_propeller()
    movers.append(Mover("Propeller", propeller, hub))
    movers.extend(parts.build_gear(body))
    parts.build_details(body)
    return body, movers


LOD_RATIOS = {1: .26, 2: .08}
LOD1_KEEP = ["Propeller"]
COLLIDER_RATIO = .32
UV_TOP_BOTTOM = {"wing", "wing_flap", "wing_aileron", "wing_filler", "htail", "elevator"}
UV_SIDE = {"vtail", "rudder"}
DENSITY = {"wing": .79, "wing_flap": .79, "wing_aileron": .79, "wing_filler": .60,
           "wing_cap": .27, "wing_flap_cap": .22, "wing_aileron_cap": .22, "wing_filler_cap": .22,
           "htail_cap": .26, "elevator_cap": .26, "vtail_cap": .25, "rudder_cap": .25}
BOTTOM_DENSITY = .62


def _tips(p):
    return np.maximum.reduce([p.inside(4.87 - np.abs(p.x)) * p.wing,
                              p.inside(2.38 - p.z) * p.vt, p.f(p.group("spinner")), p.f(p.group("pant"))])


def scheme_classic(p):
    belt = curve_dist(p.s, p.z, [(0, 1.08), (1.12, 1.155), (2.6, 1.142), (4.72, 1.125),
                                 (6.20, 1.184), (7.02, 1.442), (7.90, 2.02)])
    area = p.fus | p.vt
    trim = np.maximum(_tips(p), p.inside(6.68 - p.s) * p.vt)
    return p.band(belt, -.09, .065) * area, p.band(belt, .08, .11) * area, trim


def scheme_swoosh(p):
    low = curve_dist(p.s, p.z, [(0, 1.025), (2.46, 1.03), (4.40, 1.07), (5.92, 1.11),
                                (7.20, 1.37), (8.00, 1.63)])
    high = curve_dist(p.s, p.z, [(0, 1.047), (2.46, 1.08), (4.40, 1.15), (5.92, 1.197),
                                 (7.20, 1.73), (8.00, 2.65)])
    area = p.fus | p.vt
    return (np.minimum(p.inside(-low), p.inside(high)) * area, p.band(high, .018, .044) * area, _tips(p))


def scheme_two_tone(p):
    line = curve_dist(p.s, p.z, [(0, 1.07), (2.44, 1.06), (4.81, 1.091), (6.36, 1.15), (8.00, 1.245)])
    return p.inside(line) * p.fus, p.band(line, -.023, .028) * p.fus, np.maximum(_tips(p), p.f(p.vt))


def scheme_trainer(p):
    cowl = p.fus * p.inside(p.s - (1.22 + .18 * (p.z - 1.16)))
    cheat = p.band(p.z, 1.28, 1.32) * p.fus * p.inside(1.49 - p.s) * p.inside(p.s - 4.54)
    return cowl, _tips(p), cheat


SCHEMES = {"Classic": scheme_classic, "Swoosh": scheme_swoosh, "TwoTone": scheme_two_tone, "Trainer": scheme_trainer}
REG_BOX = dict.fromkeys(SCHEMES, (4.94, 6.13, .078, .220))
REG_FACES_U, REG_FACES_V = parts.REG_S, (.006, .295)
REG_TEX_W = 896
LOGO_BOX = (7.05, 7.78, 1.74, 2.24)


def panel_lines(p):
    starboard = p.fus & (p.nx < -.40)
    door = round_rect_sdf(p.s, p.z, parts.DOOR_S, parts.DOOR_SILL + parts.DOOR_H / 2,
                          parts.DOOR_W / 2, parts.DOOR_H / 2, .079)
    hatch = round_rect_sdf(p.s, p.z, parts.BAG_S, parts.BAG_SILL + parts.BAG_H / 2,
                           parts.BAG_W / 2, parts.BAG_H / 2, .039)
    handle = round_rect_sdf(p.s, p.z, 2.75, 1.316, .050, .013, .010)
    cowl = p.s - (1.25 + .17 * (p.z - 1.15))
    return np.maximum.reduce([.58 * p.line(door, .006) * starboard,
                              .60 * p.line(hatch, .006) * starboard,
                              .45 * p.inside(handle) * starboard,
                              .43 * p.line(cowl, .006) * p.fus])


def detail_extra(p):
    return None


def dirt_extra(p):
    exhaust = p.inside(1.25 - p.s) * np.exp(-((p.x + .29) / .17)**2 - ((p.z - .75) / .10)**2)
    return -.13 * p.f(p.fus) * exhaust * np.exp(-np.maximum(p.s - 1.25, 0) / .83)


GEAR = {
    "wheels": [("Wheel_Nose", P(0, parts.NOSE_S, parts.NOSE_R), parts.NOSE_R, True),
               ("Wheel_L", P(parts.MAIN_X, S_REF, parts.MAIN_R), parts.MAIN_R, False),
               ("Wheel_R", P(-parts.MAIN_X, S_REF, parts.MAIN_R), parts.MAIN_R, False)],
    "steering_pivot": P(0, parts.NOSE_S, 0),
    "max_steering": 38.0,
}

# Fictional airport-planning figures in metres, kilograms and metres per second.
PERFORMANCE = {"takeoff_distance": 445.0, "landing_distance": 385.0, "max_takeoff_mass": 935.0,
               "unpaved": True, "taxi_speed": 6.5, "rotate_speed": 25.0, "approach_speed": 30.0,
               "climb_rate": 3.8, "climb_speed": 35.5}

FWD, AFT, UP, DOWN = (0, -1, 0), (0, 1, 0), (0, 0, 1), (0, 0, -1)
STARBOARD = (-1, 0, 0)
RED, GREEN, WHITE = (1, .07, .04), (.12, 1, .31), (1, .97, .91)


def right_skin(s, z):
    return P(-Fuselage.side_x(s, z), s, z)


POINTS = [
    ("PassengerDoor", "Door_R", right_skin(parts.DOOR_S, parts.DOOR_SILL), STARBOARD, (parts.DOOR_W, parts.DOOR_H)),
    ("CargoDoor", "Baggage", right_skin(parts.BAG_S, parts.BAG_SILL), STARBOARD, (parts.BAG_W, parts.BAG_H)),
    ("Fuel", "Fuel_L", parts.wing_skin(1.32, .31), UP, (.064, .064)),
    ("Fuel", "Fuel_R", parts.wing_skin(-1.32, .31), UP, (.064, .064)),
    ("TowAttach", "TowBar", P(0, parts.NOSE_S - .12, parts.NOSE_R), FWD, (0, 0)),
    ("WheelChock", "Chock_Nose", P(0, parts.NOSE_S, 0), FWD, (parts.NOSE_WIDTH, 0)),
    ("WheelChock", "Chock_L", P(parts.MAIN_X, S_REF, 0), FWD, (parts.MAIN_WIDTH, 0)),
    ("WheelChock", "Chock_R", P(-parts.MAIN_X, S_REF, 0), FWD, (parts.MAIN_WIDTH, 0)),
    ("TieDown", "TieDown_L", parts.wing_skin(3.37, .46, top=False), DOWN, (0, 0)),
    ("TieDown", "TieDown_R", parts.wing_skin(-3.37, .46, top=False), DOWN, (0, 0)),
    ("TieDown", "TieDown_Tail", P(0, 7.25, Fuselage.zb(7.25)), DOWN, (0, 0)),
]

tip = parts.wing_skin(5.085, .27)
landing = parts.wing_station(1.06)["le"]
LIGHTS = [
    ("Navigation", "Nav_L", P(5.115, tip.y + S_REF, tip.z), (.82, -.57, 0), 110, RED),
    ("Navigation", "Nav_R", P(-5.115, tip.y + S_REF, tip.z), (-.82, -.57, 0), 110, GREEN),
    ("Navigation", "Nav_Tail", P(0, parts.LENGTH + .01, 1.186), AFT, 140, WHITE),
    ("Strobe", "Strobe_L", P(5.093, tip.y + S_REF + .035, tip.z + .025), UP, 360, WHITE),
    ("Strobe", "Strobe_R", P(-5.093, tip.y + S_REF + .035, tip.z + .025), UP, 360, WHITE),
    ("Beacon", "Beacon", P(0, parts.BEACON_S, parts.FIN_TOP + .032), UP, 360, RED),
    ("Landing", "Landing", P(1.06, landing.y + S_REF, landing.z + .027), FWD, 35, WHITE),
    ("Taxi", "Taxi", P(-1.06, landing.y + S_REF, landing.z + .027), FWD, 60, WHITE),
]

VIEWS = {
    "front34": ((-8.6, -9.4, 4.05), (0, .47, 1.05), 39),
    "rear34": ((8.9, 9.8, 3.95), (0, 1.00, 1.12), 39),
    "side": ((-21, .88, 1.78), (0, .88, 1.20), 70),
    "top": ((0, .88, 18.4), (0, .88, 0), 42),
    "game": ((-9.8, -10, 12.5), (0, .80, .8), 46),
}


def preview_pose(view):
    import bpy

    propeller = bpy.data.objects.get("Propeller")
    if propeller is not None:
        propeller.rotation_euler.y = -.41 if view != "top" else 0.0
