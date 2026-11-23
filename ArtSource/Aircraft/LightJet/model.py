"""The Starling A6 compact business twinjet and its AirCity gameplay data."""

import numpy as np

import parts
from geom import airfoil_eval
from paint import curve_dist, round_rect_sdf
from parts import Fuselage, P, S_REF
from pipeline import Mover


NAME = "LightJet"
DISPLAY_NAME = "Compact private jet"
SEATS = 6
CATEGORY = "BusinessJet"
AO_DISTANCE = 1.35
CARGO_HINGES = {"Baggage": ("Forward", 105.0, 2.5)}


def build():
    body = parts.build_fuselage()
    body.merge(parts.build_windows())
    movers = [Mover(name, mesh, pivot, hinge=hinge, axis=axis)
              for name, mesh, pivot, hinge, axis in parts.build_surfaces(body)]
    for sign, side in ((1, "L"), (-1, "R")):
        fan, hub = parts.build_engine(body, sign)
        movers.append(Mover(f"Fan_{side}", fan, hub))
    nose, wheels = parts.build_gear(body)
    movers.append(Mover("NoseGear", nose, P(0, parts.NOSE_S, parts.NOSE_R)))
    movers.extend(Mover(name, mesh, axle, parent="NoseGear" if steerable else None)
                  for name, mesh, axle, steerable in wheels)
    return body, movers


LOD_RATIOS = {1: 0.23, 2: 0.075}
LOD1_KEEP = []
COLLIDER_RATIO = 0.35
UV_TOP_BOTTOM = {"wing", "wing_flap", "wing_aileron", "wing_filler", "htail", "elevator", "pylon"}
UV_SIDE = {"vtail", "rudder"}
DENSITY = {"wing": .68, "wing_flap": .68, "wing_aileron": .68, "wing_filler": .53, "htail": .75,
           "elevator": .75, "pylon": .60, "nacelle": 1.02, "wing_cap": .20, "wing_flap_cap": .18,
           "wing_aileron_cap": .18, "wing_filler_cap": .18, "htail_cap": .18, "elevator_cap": .18,
           "vtail_cap": .20, "rudder_cap": .20, "pylon_cap": .20}
BOTTOM_DENSITY = .60


def _tips(p):
    return np.maximum(p.inside(6.18 - np.abs(p.x)) * p.wing, p.inside(3.77 - p.z) * p.vt)


def scheme_classic(p):
    """A low cheatline that splits into a thin gold pinstripe and a rising tail sweep."""
    d = curve_dist(p.s, p.z, [(0, 1.48), (1.25, 1.62), (2.8, 1.68), (7.8, 1.72), (9.7, 1.89),
                              (10.8, 2.24), (11.76, 2.77), (13.5, 3.89), (14.0, 4.3)])
    zone = p.fus | p.vt
    main = p.band(d, -.18, .07) * zone
    accent = p.band(d, .104, .145) * zone
    trim = np.maximum(_tips(p), p.group("nacelle") * p.band(p.z, 1.87, 2.18))
    return main, accent, trim


def scheme_swoosh(p):
    low = curve_dist(p.s, p.z, [(0, 1.15), (3, 1.14), (8.5, 1.20), (10.5, 1.38), (14, 1.77)])
    high = curve_dist(p.s, p.z, [(0, 1.20), (3, 1.25), (7.5, 1.55), (9.4, 1.80), (11.7, 2.72), (14, 4.4)])
    zone = p.fus | p.vt
    return (np.minimum(p.inside(-low), p.inside(high)) * zone, p.band(high, .026, .068) * zone,
            np.maximum(_tips(p), p.f(p.group("nacelle"))))


def scheme_two_tone(p):
    cheat = curve_dist(p.s, p.z, [(0, 1.52), (5.7, 1.61), (9, 1.68), (14, 1.96)])
    return p.inside(cheat + .04) * p.fus, p.band(cheat, -.04, .04) * p.fus, np.maximum(_tips(p), p.f(p.vt))


def scheme_trainer(p):
    nose = p.inside(p.s - .90 - .32 * (p.z - 1.7)) * p.fus
    stripe = p.band(p.z, 1.71, 1.755) * p.fus * p.inside(1.28 - p.s) * p.inside(p.s - 9.8)
    return nose, _tips(p), stripe


SCHEMES = {"Classic": scheme_classic, "Swoosh": scheme_swoosh, "TwoTone": scheme_two_tone, "Trainer": scheme_trainer}
REG_BOX = dict.fromkeys(SCHEMES, (7.08, 8.11, .065, .254))
REG_FACES_U, REG_FACES_V = (7.01, 8.16), (-.035, .33)
REG_TEX_W = 1024
LOGO_BOX = (11.91, 12.88, 2.90, 3.63)


def panel_lines(p):
    port, starboard = p.fus & (p.nx > .45), p.fus & (p.nx < -.45)
    door = round_rect_sdf(p.s, p.z, parts.DOOR_S, parts.DOOR_SILL + parts.DOOR_H / 2,
                          parts.DOOR_W / 2, parts.DOOR_H / 2, .12)
    baggage = round_rect_sdf(p.s, p.z, parts.BAG_S, parts.BAG_SILL + parts.BAG_H / 2,
                             parts.BAG_W / 2, parts.BAG_H / 2, .035)
    handle = round_rect_sdf(p.s, p.z, parts.DOOR_S + .21, 1.87, .046, .018, .010)
    return np.maximum.reduce([
        .72 * p.line(door, .007) * port, .72 * p.line(baggage, .006) * starboard,
        p.inside(handle) * port, .44 * p.line(p.s - (.79 + .27 * (p.z - 1.7)), .006) * p.fus,
        .37 * p.line(p.s - 9.56, .007) * p.group("nacelle"),
        .40 * p.line(p.s - 1.95 * (p.z - 1.80) - 9.73, .008) * p.vt,
    ])


def detail_extra(p):
    return None


def dirt_extra(p):
    return -.085 * p.group("nacelle") * p.inside(10.25 - p.s) * np.clip((2.50 - p.z) / .38, 0, 1)


FWD, AFT, UP, DOWN = (0, -1, 0), (0, 1, 0), (0, 0, 1), (0, 0, -1)
NOSE_AXLE = P(0, parts.NOSE_S, parts.NOSE_R)
GEAR = {
    "wheels": [("Wheel_Nose", NOSE_AXLE, parts.NOSE_R, True),
               ("Wheel_L", P(parts.MAIN_X, S_REF, parts.MAIN_R), parts.MAIN_R, False),
               ("Wheel_R", P(-parts.MAIN_X, S_REF, parts.MAIN_R), parts.MAIN_R, False)],
    "steering_pivot": P(0, parts.NOSE_S, 0),
    "max_steering": 53.0,
}

# Fictional airport-planning figures, in metres, kilograms and metres per second.
PERFORMANCE = {"takeoff_distance": 970.0, "landing_distance": 815.0, "max_takeoff_mass": 4800.0,
               "unpaved": False, "taxi_speed": 7.5, "rotate_speed": 50.0, "approach_speed": 55.0,
               "climb_rate": 9.5, "climb_speed": 68.0}


def _side(sign, s, z):
    return P(sign * Fuselage.side_x(s, z), s, z)


def _fuel(sign):
    station = parts.wing_station(sign * 1.75, h=None)
    upper, _, _ = airfoil_eval(parts.WING_AF)(np.array([.40]))
    return station["le"] + station["chord_dir"] * (station["c"] * .40) + station["up"] * (station["c"] * upper[0])


POINTS = [
    ("PassengerDoor", "Door_L1", _side(1, parts.DOOR_S, parts.DOOR_SILL), (1, 0, 0), (parts.DOOR_W, parts.DOOR_H)),
    ("CargoDoor", "Baggage", _side(-1, parts.BAG_S, parts.BAG_SILL), (-1, 0, 0), (parts.BAG_W, parts.BAG_H)),
    ("Fuel", "Fuel_L", _fuel(1), UP, (.10, .10)),
    ("Fuel", "Fuel_R", _fuel(-1), UP, (.10, .10)),
    ("TowAttach", "TowBar", P(0, parts.NOSE_S - .11, parts.NOSE_R), FWD, (0, 0)),
    ("WheelChock", "Chock_Nose", P(0, parts.NOSE_S, 0), FWD, (.12, 0)),
    ("WheelChock", "Chock_L", P(parts.MAIN_X, S_REF, 0), FWD, (.18, 0)),
    ("WheelChock", "Chock_R", P(-parts.MAIN_X, S_REF, 0), FWD, (.18, 0)),
]

RED, GREEN, WHITE = (1, .07, .04), (.12, 1, .31), (1, .97, .91)
LIGHTS = [
    ("Navigation", "Nav_L", P(6.48, 7.56, 1.59), (.82, -.57, 0), 110, RED),
    ("Navigation", "Nav_R", P(-6.48, 7.56, 1.59), (-.82, -.57, 0), 110, GREEN),
    ("Navigation", "Nav_Tail", P(0, 14.01, 1.88), AFT, 140, WHITE),
    ("Strobe", "Strobe_L", P(6.43, 7.78, 1.63), UP, 360, WHITE),
    ("Strobe", "Strobe_R", P(-6.43, 7.78, 1.63), UP, 360, WHITE),
    ("Beacon", "Beacon_Top", P(0, 6.97, 3.03), UP, 360, RED),
    ("Beacon", "Beacon_Bottom", P(0, 4.02, .965), DOWN, 360, RED),
    ("Landing", "Landing_L", P(1.16, 5.06, 1.12), FWD, 35, WHITE),
    ("Landing", "Landing_R", P(-1.16, 5.06, 1.12), FWD, 35, WHITE),
    ("Taxi", "Taxi", P(0, parts.NOSE_S - .064, .51), FWD, 60, WHITE),
]


VIEWS = {
    "front34": ((13, -16, 7.5), (0, -.25, 1.5), 54),
    "rear34": ((-13, 16, 6.4), (0, .15, 1.55), 54),
    "side": ((24, 0, 4.2), (0, 0, 1.9), 52),
    "game": ((-20, -17, 23), (0, 0, .9), 67),
    "top": ((0, -.3, 27), (0, -.3, 0), 45),
}
