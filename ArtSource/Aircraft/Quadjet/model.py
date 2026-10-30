"""Long-haul quadjet with a partial upper deck (proportions and figures of the 747-8 Intercontinental): the model
definition the shared pipeline (../Common/pipeline.py) builds.

Stations `s` are metres aft of the nose; Blender Y = s - S_REF. Passenger windows (main and upper deck) and
doors are painted into the shading map rather than modelled, as on the other airliners.
"""
import numpy as np

import parts
from geom import MeshBuilder
from paint import curve_dist, round_rect_sdf
from parts import P, Y, Fuselage, S_REF  # noqa: F401  (Fuselage / S_REF are read by the pipeline)
from pipeline import Mover

NAME = "Quadjet"
DISPLAY_NAME = "Long-haul quadjet"
CATEGORY = "Airliner"
AO_DISTANCE = 4.2

# ============================================================== geometry


def build():
    body = MeshBuilder()
    body.merge(parts.build_fuselage())
    body.merge(parts.build_windows())
    wing = parts.build_wing()
    body.merge(wing.pop("Wing"))
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
    return ([(f"Wheel_W{k}", parts.WLG_S + ds) for k, ds in parts.AXLES] +
            [(f"Wheel_B{k}", parts.BLG_S + ds) for k, ds in parts.AXLES])


LOD_RATIOS = {1: 0.18, 2: 0.045}
LOD1_KEEP = []          # fans are invisible behind the intake lip at LOD1 sizes
COLLIDER_RATIO = 0.3

# ============================================================== UV atlas

UV_TOP_BOTTOM = {"wing", "wing_flap", "wing_aileron", "wing_filler", "htail", "elevator"}
UV_SIDE = {"vtail", "rudder", "dorsal", "pylon"}
DENSITY = {"wing": 0.42, "wing_flap": 0.42, "wing_aileron": 0.42, "wing_filler": 0.32, "htail": 0.5, "elevator": 0.5,
           "wing_cap": 0.1, "htail_cap": 0.2, "vtail_cap": 0.2, "elevator_cap": 0.2, "wing_flap_cap": 0.15,
           "wing_aileron_cap": 0.15, "wing_filler_cap": 0.15, "rudder_cap": 0.2,
           "nacelle": 0.75, "pylon": 0.5, "wing_track": 0.3, "pylon_cap": 0.2, "gearleg": 0.25, "spinner": 0.5, "satcom": 0.4}
BOTTOM_DENSITY = 0.5

# ============================================================== livery schemes

MAIN_WIN_Z = 6.00               # main-deck window centres (side view)
UPPER_WIN_Z = 8.65              # upper-deck window centres (floor 7.73 m: ground clearance B 7.56-7.90 m)
# main-deck passenger doors (station, sill height above the ground): 747-8 door stations (door-location sheet
# 2.7.1) and the middle of the ground-clearance ranges C / S / V / T / J (2.3.2)
MAIN_DOORS = [(9.5, 4.97), (22.9, 5.005), (34.7, 5.03), (46.3, 5.075), (60.8, 5.135)]
MAIN_DOOR = (1.07, 1.93)
# upper-deck exit, painted only (not a boarding door): station and size from the side views
UPPER_EXIT = (15.25, 7.85)
UPPER_EXIT_SIZE = (0.84, 1.52)
CARGO = [("Cargo_Fwd", 13.3, 2.92, 2.64, 1.68), ("Cargo_Aft", 50.67, 3.035, 2.64, 1.68),
         ("Cargo_Bulk", 54.28, 3.225, 1.12, 1.24)]
# the radome joint passes just ahead of the first main-deck window, leaning forwards at the top
RADOME_S = 1.75
RADOME_LEAN = 0.25


def _radome(p):
    """Signed distance to the radome joint, negative on the radome."""
    return p.s - (RADOME_S - RADOME_LEAN * (p.z - parts.TIP_Z))


def _tail(p):
    return p.f(p.vt)


def _nacelles(p):
    return p.f(p.group("nacelle"))


def scheme_classic(p):
    """Cheatline under the main-deck windows sweeping up into the fin, a pinstripe below, painted engines."""
    knots = [(0.5, 5.15), (3.0, 5.33), (8.0, MAIN_WIN_Z - 0.55), (55.0, MAIN_WIN_Z - 0.55), (58.0, 6.3), (60.5, 8.0),
             (62.0, 9.6), (63.8, 11.0), (67.3, 14.0), (70.8, 17.0), (74.3, 20.0)]   # up the fin ~1.2 m behind its LE
    d = curve_dist(p.s, p.z, knots)
    zone = (p.fus | p.vt) * p.inside(-_radome(p))
    r = p.band(d, -0.52, 0.18) * zone
    g = p.band(d, -0.84, -0.68) * zone
    return r, g, _nacelles(p)


def scheme_swoosh(p):
    """A belly-to-tail swoosh that grows from the wing root, the whole fin in colour 3."""
    lo = [(30.0, 4.0), (40.0, 3.2), (50.0, 3.6), (57.0, 5.4), (62.0, 7.15), (68.0, 7.9), (76.5, 8.8)]
    hi = [(30.0, 4.0), (40.0, 5.3), (48.0, 5.75), (53.0, 6.6), (58.0, 9.0), (62.0, 11.5)]
    zone = p.fus
    start = p.inside(30.0 - p.s)
    d_lo, d_hi = curve_dist(p.s, p.z, lo), curve_dist(p.s, p.z, hi)
    r = np.minimum(p.inside(-d_lo), p.inside(d_hi)) * start * zone
    g = p.band(d_hi, 0.15, 0.34) * start * zone
    b = np.maximum(_tail(p), _nacelles(p))
    return r, g, b


def scheme_two_tone(p):
    """Airline standard: grey belly under a cheatline, painted tail and engines."""
    cheat = [(-1, 5.0), (3.0, 4.55), (8.0, 4.45), (52.0, 4.45), (60.0, 4.75), (66.0, 5.3), (72.0, 6.6), (77.0, 8.3)]
    d = curve_dist(p.s, p.z, cheat)
    r = p.inside(d + 0.16) * p.fus
    g = p.band(d, -0.16, 0.16) * p.fus
    b = np.maximum(_tail(p), _nacelles(p))
    return r, g, b


def scheme_trainer(p):
    """Charter / 'eurowhite': coloured nose cone, engine cowls, raked wingtips and fin tip, window-line stripe."""
    r = np.maximum(p.inside(_radome(p)) * p.fus, _nacelles(p))
    g = np.maximum(p.f(p.wing) * p.inside(parts.RAKE - np.abs(p.x)), p.inside(17.8 - p.z) * p.vt)
    d = p.z - (MAIN_WIN_Z - 0.62)
    b = p.band(d, -0.10, 0.10) * p.fus * p.inside(3.5 - p.s) * p.inside(p.s - 60.0) * (np.abs(p.nx) > 0.35)
    return r, g, b


SCHEMES = {"Classic": scheme_classic, "Swoosh": scheme_swoosh, "TwoTone": scheme_two_tone, "Trainer": scheme_trainer}

# Registration on the tail cone under the tailplane (arc metres from the widest line, up positive), the same
# box for every style: the stripes are kept clear of it
REG_BOX = {"Classic": (62.4, 67.2, -0.95, -0.35), "Swoosh": (62.4, 67.2, -0.95, -0.35),
           "TwoTone": (62.4, 67.2, -0.95, -0.35), "Trainer": (62.4, 67.2, -0.95, -0.35)}
REG_FACES_U = (61.6, 68.2)
REG_FACES_V = (-1.6, 0.5)
REG_TEX_W = 1024          # ~150 px/m on the patch (the atlas is ~28): sharp 0.6 m letters

LOGO_BOX = (68.0, 73.4, 12.2, 16.8)

# ============================================================== shading, dirt

WINDOW_PITCH = 0.508


def _window_rows():
    doors_main = [s for s, _ in MAIN_DOORS]
    main = [s for s in np.arange(2.45, 59.9, WINDOW_PITCH) if all(abs(s - d) > 0.85 for d in doors_main)]
    upper = [s for s in np.arange(9.6, 26.4, WINDOW_PITCH) if abs(s - UPPER_EXIT[0]) > 0.8]
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
    """Main-deck doors and the upper-deck exits on both sides; cargo and bulk doors on the starboard side."""
    both = (np.abs(p.nx) > 0.30) & p.fus
    stbd = (p.nx < -0.30) & p.fus
    lines = []
    w, h = MAIN_DOOR
    for s, sill in MAIN_DOORS:
        lines.append(p.line(round_rect_sdf(p.s, p.z, s, sill + h / 2, w / 2, h / 2, 0.22), 0.018) * both)
    (s, sill), (w, h) = UPPER_EXIT, UPPER_EXIT_SIZE
    lines.append(p.line(round_rect_sdf(p.s, p.z, s, sill + h / 2, w / 2, h / 2, 0.18), 0.018) * both)
    for _, s, sill, w, h in CARGO:
        lines.append(p.line(round_rect_sdf(p.s, p.z, s, sill + h / 2, w / 2, h / 2, 0.10), 0.018) * stbd)
    return np.maximum.reduce(lines)


def panel_lines(p):
    w = 0.018
    lines = [_doors(p)]
    lines.append(p.line(_radome(p), w * 0.8) * p.fus * 0.4)
    for s in (7.2, 12.0, 26.6, 41.0, 56.0, 61.6):
        lines.append(p.line(p.s - s, w * 0.7) * p.fus * 0.5)
    # wing-to-body fairing outline
    fair = (p.s > 21.0) & (p.s < 48.5) & (p.z < 4.8) & (p.z > 3.8) & p.fus
    lines.append(p.line(p.z - 4.28, w) * fair * 0.7)
    nac = p.group("nacelle")
    for _, x, inlet, _z in parts.ENGINES:
        mine = nac & (np.abs(p.x - x) < 2.2)
        lines.append(p.line(p.s - (inlet + parts.KL * 0.6), w) * mine)
        lines.append(p.line(p.s - (inlet + parts.KL * 2.7), w) * mine)
    return np.clip(np.maximum.reduce(lines), 0, 1)


def detail_extra(p):
    """Passenger windows on both decks, painted as dark glass. Cockpit windows are real geometry."""
    return 1.0 - 0.9 * _windows(p)


def dirt_extra(p):
    """Exhaust staining behind the engines on the wing undersides, grimy belly around the wing root."""
    stain = np.zeros_like(p.x)
    for _, x, inlet, _z in parts.ENGINES:
        stain = np.maximum(stain, np.exp(-((p.x - x) / 2.0) ** 2) * (p.s > inlet + 6.5) * (p.nz < 0))
    belly = p.fus * (p.nz < -0.6) * p.inside(p.s - 50.0) * p.inside(20.0 - p.s)
    return -0.10 * stain * p.wing - 0.08 * belly


# ============================================================== gameplay data (Blender space; the pipeline converts)

def _wheels():
    out = [("Wheel_Nose", P(0, parts.NOSE_S, parts.NOSE_AXLE_Z), parts.NOSE_R, True)]
    for name, s in _main_axles():       # a main "wheel" is an axle line: its wheels (both sides) roll together
        out.append((name, P(0, s, parts.MAIN_AXLE_Z), parts.MAIN_R, False))
    return out


GEAR = {"wheels": _wheels(), "steering_pivot": P(0, parts.NOSE_S, 0.0), "max_steering": 70.0}

# runway needs: 747-8 airport-planning figures at maximum weight, sea level (MTOW 447,695 kg, 2.1.2; take-off
# and landing runway-length charts, 3.3 / 3.4). Jets need a paved runway.
PERFORMANCE = {"takeoff_distance": 3100.0, "landing_distance": 2200.0, "max_takeoff_mass": 447700.0, "unpaved": False,
               # m/s: taxi 20 kn on straights, rotate (VR) ~155 kn, approach (VREF) ~146 kn, initial climb
               # ~2,000 ft/min at 165 kn
               "taxi_speed": 10.0, "rotate_speed": 80.0, "approach_speed": 75.0, "climb_rate": 10.0, "climb_speed": 85.0}


def _side(s, z, sign):
    return P(sign * Fuselage.side_x(s, z), s, z)


FWD, AFT, UP, DOWN = (0, -1, 0), (0, 1, 0), (0, 0, 1), (0, 0, -1)
_fuel_y = 14.5
_fuel_s = parts.le_s(_fuel_y) + 2.0

POINTS = [
    # main-deck doors: position = sill centre on the skin. L1..L5 / R1..R5 (the upper-deck exit is painted only)
    *[("PassengerDoor", f"Door_{side}{k + 1}", _side(s, sill, sign), (sign, 0, 0), MAIN_DOOR)
      for k, (s, sill) in enumerate(MAIN_DOORS) for side, sign in (("L", 1), ("R", -1))],
    *[("CargoDoor", name, _side(s, sill, -1), (-1, 0, 0), (w, h)) for name, s, sill, w, h in CARGO],
    ("Fuel", "Fuel", (-_fuel_y, Y(_fuel_s), parts.wing_surface_z(_fuel_y, _fuel_s)), DOWN, (0.4, 0.4)),
    ("GroundPower", "GPU", P(-0.8, 5.6, float(Fuselage.zb(5.6)) + 0.10), (-0.4, 0, -0.9), (0.25, 0.25)),
    ("Water", "Water", P(0.9, 53.0, float(Fuselage.zb(53.0)) + 0.08), (0.4, 0, -0.9), (0.18, 0.18)),
    ("Lavatory", "Lav", P(-0.9, 57.0, float(Fuselage.zb(57.0)) + 0.08), (-0.4, 0, -0.9), (0.18, 0.18)),
    ("TowAttach", "TowBar", P(0, parts.NOSE_S - 0.3, parts.NOSE_AXLE_Z), FWD, (0, 0)),
    ("WheelChock", "Chock_Nose", P(0, parts.NOSE_S, 0), FWD, (1.5, 0)),
    ("WheelChock", "Chock_L", P(parts.WLG_X, parts.WLG_S + parts.AXLES[0][1], 0), FWD, (2.0, 0)),
    ("WheelChock", "Chock_R", P(-parts.WLG_X, parts.WLG_S + parts.AXLES[0][1], 0), FWD, (2.0, 0)),
    ("WheelChock", "Chock_BL", P(parts.BLG_X, parts.BLG_S + parts.AXLES[0][1], 0), FWD, (2.0, 0)),
    ("WheelChock", "Chock_BR", P(-parts.BLG_X, parts.BLG_S + parts.AXLES[0][1], 0), FWD, (2.0, 0)),
]

_nav_y = parts.WING_TIP - 0.25
_tip_z = parts.wing_station(_nav_y, h=None)["le"].z
_logo_z = parts.ht_station(parts.LOGO_LIGHT[0], h=None)["le"].z + 0.5
RED, GREEN, WHITE = (1.0, 0.08, 0.05), (0.15, 1.0, 0.35), (1.0, 0.97, 0.9)
_tail_z = float(Fuselage.zm(Fuselage.S1))

LIGHTS = [
    ("Navigation", "Nav_L", (_nav_y + 0.08, Y(parts.NAV_S - 0.1), _tip_z + 0.02), (0.82, -0.57, 0), 110, RED),
    ("Navigation", "Nav_R", (-_nav_y - 0.08, Y(parts.NAV_S - 0.1), _tip_z + 0.02), (-0.82, -0.57, 0), 110, GREEN),
    ("Navigation", "Nav_Tail", P(0, Fuselage.S1 + 0.1, _tail_z + 0.32), AFT, 140, WHITE),
    ("Strobe", "Strobe_L", (_nav_y + 0.02, Y(parts.NAV_S + 0.6), _tip_z + 0.05), UP, 360, WHITE),
    ("Strobe", "Strobe_R", (-_nav_y - 0.02, Y(parts.NAV_S + 0.6), _tip_z + 0.05), UP, 360, WHITE),
    ("Strobe", "Strobe_Tail", P(0, Fuselage.S1 + 0.12, _tail_z + 0.46), AFT, 360, WHITE),
    ("Beacon", "Beacon_Top", P(0, parts.BEACON_TOP_S, float(Fuselage.zt(parts.BEACON_TOP_S)) + 0.14), UP, 360, RED),
    ("Beacon", "Beacon_Bottom", P(0, parts.BEACON_BOTTOM_S, Fuselage.bottom_z(parts.BEACON_BOTTOM_S, 0) - 0.14), DOWN,
     360, RED),
    ("Landing", "Landing_L", (parts.LANDING[0], Y(parts.LANDING[1]), parts.wing_surface_z(*parts.LANDING) - 0.12),
     (0, -0.98, -0.18), 25, WHITE),
    ("Landing", "Landing_R", (-parts.LANDING[0], Y(parts.LANDING[1]), parts.wing_surface_z(*parts.LANDING) - 0.12),
     (0, -0.98, -0.18), 25, WHITE),
    ("Taxi", "Taxi", P(0, parts.NOSE_S - 0.27, 1.66), (0, -0.97, -0.24), 50, WHITE),
    ("Logo", "Logo_L", P(parts.LOGO_LIGHT[0], parts.LOGO_LIGHT[1], _logo_z), (-0.45, -0.2, 0.87), 35, WHITE),
    ("Logo", "Logo_R", P(-parts.LOGO_LIGHT[0], parts.LOGO_LIGHT[1], _logo_z), (0.45, -0.2, 0.87), 35, WHITE),
]

# ============================================================== previews

_nose = Y(6.0)
_mid = Y(38.1)
VIEWS = {
    "front34": ((-60.0, -76.0, 24.0), (0.0, -6.0, 7.0), 40),
    "rear34": ((60.0, 74.0, 28.0), (0.0, 8.0, 8.0), 40),
    "side": ((160.0, _mid, 10.0), (0.0, _mid, 9.0), 50),
    "top": ((0.0, _mid, 150.0), (0.0, _mid, 0.0), 35),
    "front": ((0.0, -115.0, 7.0), (0.0, 0.0, 7.0), 45),
    "nose_close": ((-15.0, _nose - 19.0, 11.0), (0.0, _nose + 1.5, 7.5), 40),
    "nose_side": ((55.0, _nose + 3.0, 7.0), (0.0, _nose + 3.0, 6.5), -20.0),
    "nose_front": ((-5.0, _nose - 45.0, 8.0), (0.0, _nose, 7.0), 60),
    "nose_34": ((-16.0, _nose - 19.0, 9.0), (0.0, _nose + 1.5, 7.0), 45),
    "hump": ((-30.0, Y(4.0), 16.0), (0.0, Y(20.0), 8.0), 40),
    "photo": ((26.0, _nose - 21.0, 3.4), (0.0, _nose + 1.5, 6.8), 50),
    "tail_close": ((-36.0, Y(84.0), 22.0), (0.0, Y(68.0), 12.5), 40),
    "engine_close": ((-22.0, Y(13.0), 4.5), (-11.9, Y(29.0), 2.8), 40),
    "engine_rear": ((-18.0, Y(52.0), 4.2), (-11.9, Y(33.0), 2.8), 40),
    "gear_close": ((-14.0, Y(50.0), 2.4), (-3.5, Y(37.4), 1.2), 40),
    "wingtip": ((-48.0, Y(38.0), 13.0), (-33.0, Y(52.5), 6.6), 50),
    "wingtip_top": ((-31.0, Y(53.0), 40.0), (-31.0, Y(53.0), 6.5), -14.0),
    "dorsal_close": ((-20.0, Y(52.0), 15.0), (0.0, Y(60.0), 10.0), 40),
    "game": ((-120.0, -86.0, 108.0), (0.0, 0.0, 4.0), 32),
    # orthographic frames for overlaying the traced drawings: centre (Y(38.1), 9.5), width 82 m
    "side_ortho": ((160.0, _mid, 9.5), (0.0, _mid, 9.5), -82.0),
    "top_ortho": ((0.0, _mid, 160.0), (0.0, _mid, 0.0), -82.0),
    "front_ortho": ((0.0, -160.0, 9.5), (0.0, 0.0, 9.5), -82.0),
}
