"""Double-deck quadjet (proportions and figures of the A380-800): the model definition the shared pipeline
(../Common/pipeline.py) builds.

Stations `s` are metres aft of the nose; Blender Y = s - S_REF. Passenger windows (two decks) and doors are
painted into the shading map rather than modelled, as on the other airliners.
"""
import numpy as np

import parts
from geom import MeshBuilder
from paint import curve_dist, round_rect_sdf
from parts import P, Y, Fuselage, S_REF  # noqa: F401  (Fuselage / S_REF are read by the pipeline)
from pipeline import Mover

NAME = "DoubleDeck"
DISPLAY_NAME = "Double-deck quadjet"
CATEGORY = "Airliner"
AO_DISTANCE = 4.5

# ============================================================== geometry


def build():
    body = MeshBuilder()
    body.merge(parts.build_fuselage())
    body.merge(parts.build_windows())
    wing = parts.build_wing()
    body.merge(wing.pop("Wing"))
    body.merge(parts.build_fences())
    body.merge(parts.build_flap_tracks())
    ht = parts.build_htail()
    body.merge(ht.pop("HStab"))
    vt = parts.build_vtail()
    body.merge(vt.pop("VStab"))
    gear = parts.build_gear()
    body.merge(gear.pop("Gear"))
    body.merge(parts.build_details())
    movers = []
    for n, x, inlet, z in parts.ENGINES:
        nacelle, fan, hub = parts.build_engine(x, inlet, z)
        body.merge(nacelle)
        movers.append(Mover(f"Fan_{n}", fan, hub))
    r0, r1 = parts.RUDDER
    movers.append(Mover("Rudder", vt["Rudder"], parts.vt_hinge((r0 + r1) / 2), hinge=parts.vt_hinge(r1) - parts.vt_hinge(r0),
                        axis="z"))
    for side, sgn in (("L", 1), ("R", -1)):
        a, b = parts.ht_hinge(sgn * parts.EL_ROOT), parts.ht_hinge(sgn * parts.EL_TIP)
        movers.append(Mover("Elevator_" + side, ht["Elevator_" + side], (a + b) / 2, hinge=b - a))
    for side, sgn in (("L", 1), ("R", -1)):
        for pattern, y0, y1 in parts.TE_PIECES:
            if pattern == "filler":
                continue
            h0, h1 = parts.hinge_point(sgn * y0), parts.hinge_point(sgn * y1)
            name = pattern.format(side)
            movers.append(Mover(name, wing[name], (h0 + h1) / 2, hinge=h1 - h0))
    movers.append(Mover("NoseGear", gear["NoseGear"], P(0, parts.NOSE_S, parts.NOSE_AXLE_Z)))
    movers.append(Mover("Wheel_Nose", gear["Wheel_Nose"], P(0, parts.NOSE_S, parts.NOSE_AXLE_Z), parent="NoseGear"))
    for name, s in _main_axles():
        movers.append(Mover(name, gear[name], P(0, s, parts.MAIN_AXLE_Z)))
    return body, movers


def _main_axles():
    """(part name, station): one part per axle line across the aircraft (both bogies' wheels at that station)."""
    return ([(f"Wheel_W{k}", parts.WLG_S + ds) for k, ds in parts.WLG_AXLES] +
            [(f"Wheel_B{k}", parts.BLG_S + ds) for k, ds in parts.BLG_AXLES])


LOD_RATIOS = {1: 0.18, 2: 0.045}
LOD1_KEEP = []          # fans are invisible behind the intake lip at LOD1 sizes
COLLIDER_RATIO = 0.3

# ============================================================== UV atlas

UV_TOP_BOTTOM = {"wing", "wing_flap", "wing_aileron", "wing_filler", "htail", "elevator"}
UV_SIDE = {"vtail", "rudder", "dorsal", "pylon", "fence"}
DENSITY = {"wing": 0.42, "wing_flap": 0.42, "wing_aileron": 0.42, "wing_filler": 0.32, "htail": 0.5, "elevator": 0.5,
           "wing_cap": 0.1, "htail_cap": 0.2, "vtail_cap": 0.2, "elevator_cap": 0.2, "wing_flap_cap": 0.15,
           "wing_aileron_cap": 0.15, "wing_filler_cap": 0.15, "rudder_cap": 0.2, "fence_cap": 0.2,
           "nacelle": 0.75, "pylon": 0.5, "wing_track": 0.3, "pylon_cap": 0.2, "gearleg": 0.25, "spinner": 0.5, "satcom": 0.4}
BOTTOM_DENSITY = 0.5

# ============================================================== livery schemes

MAIN_WIN_Z = 6.22               # main-deck window centres (5.10 m floor)
UPPER_WIN_Z = 9.02              # upper-deck window centres (7.87 m floor)
# passenger doors (station, sill height above the ground): A380 door stations and MRW / forward-CG sills
MAIN_DOORS = [(6.32, 5.10), (16.50, 5.12), (32.68, 5.15), (40.30, 5.18), (53.63, 5.20)]
UPPER_DOORS = [(20.94, 7.87), (44.74, 7.91), (49.19, 7.94)]
MAIN_DOOR, UPPER_DOOR = (1.07, 1.93), (1.07, 1.92)
CARGO = [("Cargo_Fwd", 11.73, 3.05, 2.79, 1.75), ("Cargo_Aft", 48.07, 3.11, 2.79, 1.75),
         ("Cargo_Bulk", 51.09, 3.41, 1.13, 1.13)]
# the radome joint passes ~0.2 m ahead of the windshield's lowest point, leaning forwards at the top
RADOME_S = 1.80
RADOME_LEAN = 0.30


def _radome(p):
    """Signed distance to the radome joint, negative on the radome."""
    return p.s - (RADOME_S - RADOME_LEAN * (p.z - parts.TIP_Z))


def _tail(p):
    return p.f(p.vt)


def _nacelles(p):
    return p.f(p.group("nacelle"))


def scheme_classic(p):
    """Cheatline under the main-deck windows sweeping up into the fin, a pinstripe below, painted engines."""
    knots = [(0.6, 5.05), (4.5, 5.45), (9.5, MAIN_WIN_Z - 0.50), (49.0, MAIN_WIN_Z - 0.50), (53.5, 7.0),
             (56.5, 9.6), (59.5, 13.2), (63.5, 18.4), (68.5, 24.9)]       # up the fin ~1.2 m behind its LE
    d = curve_dist(p.s, p.z, knots)
    zone = (p.fus | p.vt) * p.inside(-_radome(p))
    r = p.band(d, -0.52, 0.18) * zone
    g = p.band(d, -0.84, -0.68) * zone
    return r, g, _nacelles(p)


def scheme_swoosh(p):
    """A belly-to-tail swoosh that grows from the wing root, the whole fin in colour 3."""
    lo = [(28.0, 4.2), (38.0, 3.4), (48.0, 4.5), (56.0, 7.15), (61.0, 7.75), (70.6, 9.0)]
    hi = [(28.0, 4.2), (38.0, 5.5), (46.0, 6.0), (51.0, 6.9), (56.0, 9.4), (61.0, 12.2)]
    zone = p.fus
    start = p.inside(28.0 - p.s)
    d_lo, d_hi = curve_dist(p.s, p.z, lo), curve_dist(p.s, p.z, hi)
    r = np.minimum(p.inside(-d_lo), p.inside(d_hi)) * start * zone
    g = p.band(d_hi, 0.15, 0.34) * start * zone
    b = np.maximum(_tail(p), _nacelles(p))
    return r, g, b


def scheme_two_tone(p):
    """Airline standard: grey belly under a cheatline, painted tail and engines."""
    cheat = [(-1, 4.25), (7.0, 4.62), (20.0, 4.70), (49.0, 4.70), (60.0, 5.9), (72.0, 8.4)]
    d = curve_dist(p.s, p.z, cheat)
    r = p.inside(d + 0.16) * p.fus
    g = p.band(d, -0.16, 0.16) * p.fus
    b = np.maximum(_tail(p), _nacelles(p))
    return r, g, b


def scheme_trainer(p):
    """Charter / 'eurowhite': coloured nose cone, engine cowls, wingtip fences and fin tip, window-line stripe."""
    r = np.maximum(p.inside(_radome(p)) * p.fus, _nacelles(p))
    g = np.maximum(p.f(p.group("fence")), p.inside(21.6 - p.z) * p.vt)
    d = p.z - (MAIN_WIN_Z - 0.62)
    b = p.band(d, -0.10, 0.10) * p.fus * p.inside(4.0 - p.s) * p.inside(p.s - 62.0) * (np.abs(p.nx) > 0.35)
    return r, g, b


SCHEMES = {"Classic": scheme_classic, "Swoosh": scheme_swoosh, "TwoTone": scheme_two_tone, "Trainer": scheme_trainer}

# Registration on the rear fuselage under the tailplane (arc metres from the widest line, up positive), the same
# box for every style: the stripes are kept clear of it
REG_BOX = {"Classic": (55.6, 61.2, -0.95, -0.35), "Swoosh": (55.6, 61.2, -0.95, -0.35),
           "TwoTone": (55.6, 61.2, -0.95, -0.35), "Trainer": (55.6, 61.2, -0.95, -0.35)}
REG_FACES_U = (54.6, 62.2)
REG_FACES_V = (-1.6, 0.5)
REG_TEX_W = 1024          # ~135 px/m on the patch (the atlas is 28): sharp 0.6 m letters, ~0.36 MB per aircraft

LOGO_BOX = (62.0, 68.0, 13.0, 19.0)

# ============================================================== shading, dirt

WINDOW_PITCH = 0.533


def _window_rows():
    doors_main = [s for s, _ in MAIN_DOORS]
    doors_up = [s for s, _ in UPPER_DOORS]
    main = [s for s in np.arange(8.1, 52.5, WINDOW_PITCH) if all(abs(s - d) > 0.78 for d in doors_main)]
    upper = [s for s in np.arange(12.6, 53.6, WINDOW_PITCH) if all(abs(s - d) > 0.78 for d in doors_up)]
    return main, upper


MAIN_WINDOWS, UPPER_WINDOWS = _window_rows()


def _windows(p):
    side = (np.abs(p.nx) > 0.45) & p.fus
    w = np.zeros_like(p.x)
    for z, row in ((MAIN_WIN_Z, MAIN_WINDOWS), (UPPER_WIN_Z, UPPER_WINDOWS)):
        band = np.abs(p.z - z) < 0.4
        if not band.any():
            continue
        for s in row:
            near = band & (np.abs(p.s - s) < 0.3)
            if near.any():
                w[near] = np.maximum(w[near], p.inside(round_rect_sdf(p.s[near], p.z[near], s, z, 0.13, 0.19, 0.11)))
    return w * side


def _doors(p):
    """Passenger doors on both decks and both sides; cargo and bulk doors on the starboard side."""
    both = (np.abs(p.nx) > 0.30) & p.fus
    stbd = (p.nx < -0.30) & p.fus
    lines = []
    for doors, (w, h) in ((MAIN_DOORS, MAIN_DOOR), (UPPER_DOORS, UPPER_DOOR)):
        for s, sill in doors:
            lines.append(p.line(round_rect_sdf(p.s, p.z, s, sill + h / 2, w / 2, h / 2, 0.22), 0.018) * both)
    for _, s, sill, w, h in CARGO:
        lines.append(p.line(round_rect_sdf(p.s, p.z, s, sill + h / 2, w / 2, h / 2, 0.10), 0.018) * stbd)
    return np.maximum.reduce(lines)


def panel_lines(p):
    w = 0.018
    lines = [_doors(p)]
    lines.append(p.line(_radome(p), w * 0.8) * p.fus * 0.4)
    for s in (5.4, 12.6, 25.0, 46.6, 55.6, 62.0):
        lines.append(p.line(p.s - s, w * 0.7) * p.fus * 0.5)
    # belly-fairing outline: where it leaves the fuselage's lower lobe
    fair = (p.s > 15.5) & (p.s < 46.0) & (p.z < 5.0) & (p.z > 4.0) & p.fus
    lines.append(p.line(p.z - 4.62, w) * fair * 0.7)
    nac = p.group("nacelle")
    for _, x, inlet, _z in parts.ENGINES:
        mine = nac & (np.abs(p.x - x) < 2.5)
        lines.append(p.line(p.s - (inlet + parts.KL * 0.6), w) * mine)
        lines.append(p.line(p.s - (inlet + parts.KL * 2.7), w) * mine)
    return np.clip(np.maximum.reduce(lines), 0, 1)


def detail_extra(p):
    """Passenger windows on both decks, painted as dark glass. Cockpit windows are real geometry."""
    return 1.0 - 0.9 * _windows(p)


def dirt_extra(p):
    """Exhaust staining behind the engines on the wing undersides, grimy belly fairing."""
    stain = np.zeros_like(p.x)
    for _, x, inlet, _z in parts.ENGINES:
        stain = np.maximum(stain, np.exp(-((p.x - x) / 2.4) ** 2) * (p.s > inlet + 8.0) * (p.nz < 0))
    belly = p.fus * (p.nz < -0.6) * p.inside(p.s - 46.0) * p.inside(16.0 - p.s)
    return -0.10 * stain * p.wing - 0.08 * belly


# ============================================================== gameplay data (Blender space; the pipeline converts)

def _wheels():
    out = [("Wheel_Nose", P(0, parts.NOSE_S, parts.NOSE_AXLE_Z), parts.NOSE_R, True)]
    for name, s in _main_axles():       # a main "wheel" is an axle line: its wheels (both sides) roll together
        out.append((name, P(0, s, parts.MAIN_AXLE_Z), parts.MAIN_R, False))
    return out


GEAR = {"wheels": _wheels(), "steering_pivot": P(0, parts.NOSE_S, 0.0), "max_steering": 70.0}

# runway needs: A380-800 airport-planning figures at maximum weight, sea level, ISA. Jets need a paved runway.
PERFORMANCE = {"takeoff_distance": 2900.0, "landing_distance": 2100.0, "max_takeoff_mass": 575000.0, "unpaved": False,
               # m/s: taxi 20 kn on straights, rotate (VR) ~155 kn, approach (VREF) ~138 kn, initial climb
               # ~2,000 ft/min at 165 kn
               "taxi_speed": 10.0, "rotate_speed": 80.0, "approach_speed": 71.0, "climb_rate": 10.0, "climb_speed": 85.0}


def _side(s, z, sign):
    return P(sign * Fuselage.side_x(s, z), s, z)


FWD, AFT, UP, DOWN = (0, -1, 0), (0, 1, 0), (0, 0, 1), (0, 0, -1)
_fuel_y = 17.0
_fuel_s = parts.le_s(_fuel_y) + 2.0

POINTS = [
    # doors: position = sill centre on the skin. Main deck L1..L5 / R1..R5, upper deck UL1..UL3 / UR1..UR3
    *[("PassengerDoor", f"Door_{side}{k + 1}", _side(s, sill, sign), (sign, 0, 0), MAIN_DOOR)
      for k, (s, sill) in enumerate(MAIN_DOORS) for side, sign in (("L", 1), ("R", -1))],
    *[("PassengerDoor", f"Door_U{side}{k + 1}", _side(s, sill, sign), (sign, 0, 0), UPPER_DOOR)
      for k, (s, sill) in enumerate(UPPER_DOORS) for side, sign in (("L", 1), ("R", -1))],
    *[("CargoDoor", name, _side(s, sill, -1), (-1, 0, 0), (w, h)) for name, s, sill, w, h in CARGO],
    ("Fuel", "Fuel", (-_fuel_y, Y(_fuel_s), parts.wing_surface_z(_fuel_y, _fuel_s)), DOWN, (0.4, 0.4)),
    ("GroundPower", "GPU", P(-0.8, 3.6, float(Fuselage.zb(3.6)) + 0.10), (-0.4, 0, -0.9), (0.25, 0.25)),
    ("Water", "Water", P(0.9, 50.5, float(Fuselage.zb(50.5)) + 0.08), (0.4, 0, -0.9), (0.18, 0.18)),
    ("Lavatory", "Lav", P(-0.9, 54.5, float(Fuselage.zb(54.5)) + 0.08), (-0.4, 0, -0.9), (0.18, 0.18)),
    ("TowAttach", "TowBar", P(0, parts.NOSE_S - 0.3, parts.NOSE_AXLE_Z), FWD, (0, 0)),
    ("WheelChock", "Chock_Nose", P(0, parts.NOSE_S, 0), FWD, (1.6, 0)),
    ("WheelChock", "Chock_L", P(parts.WLG_X, parts.WLG_S + parts.WLG_AXLES[0][1], 0), FWD, (1.9, 0)),
    ("WheelChock", "Chock_R", P(-parts.WLG_X, parts.WLG_S + parts.WLG_AXLES[0][1], 0), FWD, (1.9, 0)),
    ("WheelChock", "Chock_BL", P(parts.BLG_X, parts.BLG_S + parts.BLG_AXLES[0][1], 0), FWD, (2.1, 0)),
    ("WheelChock", "Chock_BR", P(-parts.BLG_X, parts.BLG_S + parts.BLG_AXLES[0][1], 0), FWD, (2.1, 0)),
]

_tip_z = parts.wing_station(parts.WING_TIP, h=None)["le"].z
_logo_z = parts.ht_station(parts.LOGO_LIGHT[0], h=None)["le"].z + 0.55
RED, GREEN, WHITE = (1.0, 0.08, 0.05), (0.15, 1.0, 0.35), (1.0, 0.97, 0.9)
_tail_z = float(Fuselage.zm(Fuselage.S1))

LIGHTS = [
    ("Navigation", "Nav_L", (parts.WING_TIP + 0.17, Y(parts.NAV_S), _tip_z + 0.02), (0.82, -0.57, 0), 110, RED),
    ("Navigation", "Nav_R", (-parts.WING_TIP - 0.17, Y(parts.NAV_S), _tip_z + 0.02), (-0.82, -0.57, 0), 110, GREEN),
    ("Navigation", "Nav_Tail", P(0, Fuselage.S1 + 0.1, _tail_z + 0.40), AFT, 140, WHITE),
    ("Strobe", "Strobe_L", (parts.WING_TIP + 0.14, Y(parts.NAV_S + 0.5), _tip_z + 0.03), UP, 360, WHITE),
    ("Strobe", "Strobe_R", (-parts.WING_TIP - 0.14, Y(parts.NAV_S + 0.5), _tip_z + 0.03), UP, 360, WHITE),
    ("Strobe", "Strobe_Tail", P(0, Fuselage.S1 + 0.12, _tail_z + 0.55), AFT, 360, WHITE),
    ("Beacon", "Beacon_Top", P(0, parts.BEACON_TOP_S, float(Fuselage.zt(parts.BEACON_TOP_S)) + 0.14), UP, 360, RED),
    ("Beacon", "Beacon_Bottom", P(0, parts.BEACON_BOTTOM_S, Fuselage.bottom_z(parts.BEACON_BOTTOM_S, 0) - 0.14), DOWN,
     360, RED),
    ("Landing", "Landing_L", (parts.LANDING[0], Y(parts.LANDING[1]), parts.wing_surface_z(*parts.LANDING) - 0.12),
     (0, -0.98, -0.18), 25, WHITE),
    ("Landing", "Landing_R", (-parts.LANDING[0], Y(parts.LANDING[1]), parts.wing_surface_z(*parts.LANDING) - 0.12),
     (0, -0.98, -0.18), 25, WHITE),
    ("Taxi", "Taxi", P(0, parts.NOSE_S - 0.27, 1.70), (0, -0.97, -0.24), 50, WHITE),
    ("Logo", "Logo_L", P(parts.LOGO_LIGHT[0], parts.LOGO_LIGHT[1], _logo_z), (-0.45, -0.2, 0.87), 35, WHITE),
    ("Logo", "Logo_R", P(-parts.LOGO_LIGHT[0], parts.LOGO_LIGHT[1], _logo_z), (0.45, -0.2, 0.87), 35, WHITE),
]

# ============================================================== previews

_nose = Y(5.0)
VIEWS = {
    "front34": ((-62.0, -80.0, 25.0), (0.0, -5.0, 8.0), 40),
    "rear34": ((62.0, 76.0, 30.0), (0.0, 8.0, 9.0), 40),
    "side": ((160.0, 0.0, 10.0), (0.0, -1.0, 10.0), 50),
    "top": ((0.0, 0.0, 150.0), (0.0, 0.0, 0.0), 35),
    "front": ((0.0, -115.0, 8.0), (0.0, 0.0, 8.0), 45),
    "nose_close": ((-16.0, _nose - 20.0, 11.0), (0.0, _nose + 1.5, 7.0), 40),
    "nose_side": ((55.0, _nose + 2.5, 7.0), (0.0, _nose + 2.5, 7.0), -20.0),
    "nose_front": ((-5.0, _nose - 45.0, 8.5), (0.0, _nose, 7.6), 60),
    "nose_34": ((-17.0, _nose - 20.0, 9.5), (0.0, _nose + 1.0, 7.2), 45),
    "photo": ((28.0, _nose - 22.0, 3.6), (0.0, _nose + 1.5, 7.0), 50),
    "tail_close": ((-38.0, 62.0, 24.0), (0.0, Y(63.0), 14.0), 40),
    "engine_close": ((-24.0, Y(8.0), 5.0), (-14.8, Y(25.0), 3.6), 40),
    "engine_rear": ((-20.0, Y(48.0), 4.6), (-14.8, Y(30.0), 3.4), 40),
    "gear_close": ((-15.0, 14.0, 2.4), (-4.0, 0.0, 1.3), 40),
    "wingtip": ((-56.0, -2.0, 14.0), (-40.0, Y(49.0), 8.0), 50),
    "dorsal_close": ((-22.0, 4.0, 17.0), (0.0, Y(54.0), 11.0), 40),
    "game": ((-120.0, -86.0, 108.0), (0.0, 0.0, 4.0), 32),
    "side_ortho": ((160.0, 0.0, 12.0), (0.0, -0.9, 12.0), -78.0),
    "top_ortho": ((0.0, 0.0, 160.0), (0.0, -0.9, 0.0), -84.0),
    "front_ortho": ((0.0, -160.0, 11.0), (0.0, 0.0, 11.0), -84.0),
}
