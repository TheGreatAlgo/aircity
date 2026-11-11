"""Supersonic airliner (100 seats, four afterburning engines): the model definition the shared pipeline
(../Common/pipeline.py) builds. The player sees it as the Starling D100. An original design, not a copy of any
real type (see parts.py for the layout).

Stations `s` are metres aft of the nose; Blender Y = s - S_REF. Passenger windows and doors are painted into
the shading map rather than modelled, as on the other jets. `EXHAUSTS` places the afterburner flames the game
lights for the takeoff (AircraftAfterburner).
"""
import numpy as np

import parts
from geom import MeshBuilder
from paint import curve_dist, round_rect_sdf
from parts import P, Y, Fuselage, S_REF  # noqa: F401  (Fuselage / S_REF are read by the pipeline)
from pipeline import Mover

NAME = "Supersonic"
DISPLAY_NAME = "Supersonic airliner"
SEATS = 100  # four abreast; the player sees "Starling D100" (code C span, but its length needs code D stands)
CATEGORY = "Airliner"
AO_DISTANCE = 2.5

# Small bulk holds (no containers): one under the forward cabin, one in the tail cone behind the cabin
CARGO_FWD_S, CARGO_FWD_SILL = 15.6, 3.25
CARGO_AFT_S, CARGO_AFT_SILL = 55.0, 4.00
CARGO_HOLDS = {"Cargo_Fwd": (13.6, 18.6), "Cargo_Aft": (53.8, 56.4)}
CARGO_HINGES = {"Cargo_Fwd": ("Top", -100.0, 3.5), "Cargo_Aft": ("Top", -100.0, 3.5)}

# ============================================================== geometry


def build():
    body = MeshBuilder()
    body.merge(parts.build_fuselage())
    body.merge(parts.build_windows())
    wing = parts.build_wing()
    body.merge(wing.pop("Wing"))
    vt = parts.build_vtail()
    body.merge(vt.pop("VStab"))
    gear = parts.build_gear()
    body.merge(gear.pop("Gear"))
    body.merge(parts.build_details())
    for sx in (1, -1):
        for y, lip, exit_s in parts.NACELLES:
            body.merge(parts.build_engine(sx * y, lip, exit_s))
    movers = []
    r0, r1 = parts.RUDDER
    movers.append(Mover("Rudder", vt["Rudder"], parts.vt_hinge((r0 + r1) / 2), hinge=parts.vt_hinge(r1) - parts.vt_hinge(r0),
                        axis="z"))
    # elevons: the inboard pair pitch (the game's elevator), the outboard pairs roll (its ailerons)
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
LOD1_KEEP = []
COLLIDER_RATIO = 0.3

# ============================================================== UV atlas

UV_TOP_BOTTOM = {"wing", "wing_elevon", "wing_filler"}
UV_SIDE = {"vtail", "rudder", "dorsal"}
DENSITY = {"wing": 0.4, "wing_elevon": 0.4, "wing_filler": 0.35, "wing_cap": 0.1, "wing_elevon_cap": 0.15,
           "wing_filler_cap": 0.15, "vtail_cap": 0.2, "rudder_cap": 0.2,   # fin at full density: it carries the logo
           "nacelle": 0.6, "intake": 0.3, "core": 0.4, "gearleg": 0.3}
BOTTOM_DENSITY = 0.45

# ============================================================== livery schemes

FLOOR_Z = parts.KEEL + 1.05     # cabin floor = passenger-door sills (z 4.0)
CHEAT_Z = 4.92                  # passenger-window line
DOORS = (11.0, 17.6, 52.4)      # passenger doors 1-3 (both sides)
DOOR_HW, DOOR_HH = 0.40, 0.86
RADOME_S = 2.4                  # the needle nose cone's joint
FIN_SWEEP = parts.VT_TAN


def _radome(p):
    """Signed distance to the nose cone's joint, negative on the cone."""
    return p.s - RADOME_S


def _engines(p):
    return p.f(p.group("nacelle"))


def scheme_classic(p):
    """Cheatline through the windows, sweeping up the fin parallel to its leading edge; a pinstripe below."""
    knots = [(1.5, 4.32), (5.0, 4.62), (10.0, CHEAT_Z - 0.06), (46.0, CHEAT_Z - 0.06), (49.0, 5.25), (51.5, 6.5),
             (53.5, 8.0), (55.5, 9.6), (57.5, 11.2), (59.0, 12.6)]
    d = curve_dist(p.s, p.z, knots)
    zone = (p.fus | p.vt) * p.inside(-_radome(p))
    r = p.band(d, -0.30, 0.16) * zone
    g = p.band(d, -0.48, -0.40) * zone
    return r, g, _engines(p)


def scheme_swoosh(p):
    """A swoosh rising from the wing root to the fin, the whole fin in colour 3."""
    lo = [(30.0, 3.90), (40.0, 3.95), (48.0, 4.15), (54.0, 4.9), (61.5, 6.0)]
    hi = [(30.0, 3.90), (38.0, 4.35), (46.0, 5.05), (52.0, 5.8), (58.0, 6.8)]
    start = p.inside(30.0 - p.s)
    d_lo, d_hi = curve_dist(p.s, p.z, lo), curve_dist(p.s, p.z, hi)
    r = np.minimum(p.inside(-d_lo), p.inside(d_hi)) * start * p.fus
    g = p.band(d_hi, 0.10, 0.22) * start * p.fus
    return r, g, np.maximum(p.f(p.vt), _engines(p))


def scheme_two_tone(p):
    """Coloured belly under a cheatline low on the fuselage, painted fin and nacelles."""
    cheat = [(-1.5, 4.10), (6.0, 4.02), (14.0, 4.0), (48.0, 4.0), (54.0, 4.45), (61.5, 5.4)]
    d = curve_dist(p.s, p.z, cheat)
    r = p.inside(d + 0.10) * p.fus
    g = p.band(d, -0.10, 0.10) * p.fus
    return r, g, np.maximum(p.f(p.vt), _engines(p))


def scheme_trainer(p):
    """Charter style: coloured needle nose and nacelles, the fin's lower half, a stripe above the windows."""
    r = np.maximum(p.inside(_radome(p)) * p.fus, _engines(p))
    g = p.inside(9.0 - p.z) * p.vt
    d = p.z - (CHEAT_Z + 0.32)
    b = p.band(d, -0.07, 0.07) * p.fus * p.inside(6.5 - p.s) * p.inside(p.s - 54.0) * (np.abs(p.nx) > 0.35)
    return r, g, b


SCHEMES = {"Classic": scheme_classic, "Swoosh": scheme_swoosh, "TwoTone": scheme_two_tone, "Trainer": scheme_trainer}

# Registration on the rear fuselage over the wing root (arc metres from the widest line, up positive)
REG_BOX = {"Classic": (42.0, 46.5, -0.44, -0.10), "Swoosh": (40.0, 44.5, 0.64, 0.98),
           "TwoTone": (42.0, 46.5, -0.44, -0.10), "Trainer": (42.0, 46.5, -0.44, -0.10)}
REG_FACES_U = (39.0, 48.0)
REG_FACES_V = (-0.9, 1.3)
REG_TEX_W = 768

LOGO_BOX = (53.4, 58.4, 7.0, 10.6)

# ============================================================== shading, dirt

WINDOW_PITCH = 0.52
WINDOWS = [s for s in np.arange(12.2, 51.6, WINDOW_PITCH) if all(abs(s - d) > 0.62 for d in DOORS)]


def _windows(p):
    side = (np.abs(p.nx) > 0.5) & p.fus
    w = np.zeros_like(p.x)
    for s in WINDOWS:
        w = np.maximum(w, p.inside(round_rect_sdf(p.s, p.z, s, CHEAT_Z, 0.11, 0.15, 0.09)))
    return w * side


def _doors(p):
    both = (np.abs(p.nx) > 0.35) & p.fus
    stbd = (p.nx < -0.2) & p.fus
    lines = []
    for s in DOORS:
        lines.append(p.line(round_rect_sdf(p.s, p.z, s, FLOOR_Z + DOOR_HH, DOOR_HW, DOOR_HH, 0.18), 0.014) * both)
    for name, sill in (("Cargo_Fwd", CARGO_FWD_SILL), ("Cargo_Aft", CARGO_AFT_SILL)):
        point = next(pt for pt in POINTS if pt[1] == name)
        w, h = point[4]
        s = point[2][1] + S_REF
        lines.append(p.line(round_rect_sdf(p.s, p.z, s, sill + h / 2, w / 2, h / 2, 0.08), 0.014) * stbd)
    return np.maximum.reduce(lines)


def panel_lines(p):
    w = 0.014
    lines = [_doors(p)]
    lines.append(p.line(_radome(p), w * 0.8) * p.fus * 0.5)
    for s in (8.0, 14.0, 20.5, 27.0, 33.5, 40.0, 46.5, 51.6, 56.0):
        lines.append(p.line(p.s - s, w * 0.7) * p.fus * 0.45)
    nac = p.group("nacelle")
    for y, lip, exit_s in parts.NACELLES:
        for s in (lip + 1.2, lip + 5.5, exit_s - parts.NOZZLE_LEN - 2.6):
            lines.append(p.line(p.s - s, w) * nac * (np.abs(np.abs(p.x) - y) < 1.0))
    return np.clip(np.maximum.reduce(lines), 0, 1)


def detail_extra(p):
    return 1.0 - 0.9 * _windows(p)


def dirt_extra(p):
    """Soot on the nacelles' boat-tails and the elevons above the nozzles, a grimy belly between the gear legs."""
    stain = np.zeros_like(p.x)
    for y, lip, exit_s in parts.NACELLES:
        stain = np.maximum(stain, np.exp(-((np.abs(p.x) - y) / 0.9) ** 2) * p.inside(exit_s - parts.NOZZLE_LEN - 3.0 - p.s))
    belly = p.fus * (p.nz < -0.6) * p.inside(p.s - 46.0) * p.inside(10.0 - p.s)
    return -0.14 * stain * np.maximum(p.f(p.group("nacelle")), p.wing * (p.nz < 0)) - 0.07 * belly


# ============================================================== gameplay data (Blender space; the pipeline converts)

def _wheels():
    out = [("Wheel_Nose_L", P(parts.NOSE_WHEEL_X, parts.NOSE_S, parts.NOSE_AXLE_Z), parts.NOSE_R, True),
           ("Wheel_Nose_R", P(-parts.NOSE_WHEEL_X, parts.NOSE_S, parts.NOSE_AXLE_Z), parts.NOSE_R, True)]
    for side, sx in (("L", 1), ("R", -1)):
        for k, ds in parts.MAIN_AXLES:
            out.append((f"Wheel_{side}{k}", P(sx * parts.MAIN_X, S_REF + ds, parts.MAIN_AXLE_Z), parts.MAIN_R, False))
    return out


GEAR = {"wheels": _wheels(), "steering_pivot": P(0, parts.NOSE_S, 0.0), "max_steering": 60.0}

# runway needs at maximum weight, sea level, ISA: a slender delta needs a fast rotation and a long roll even with
# reheat, and lands fast. Fits the sandbox's 3,400 m runway when dry. Jets need a paved runway.
PERFORMANCE = {"takeoff_distance": 3300.0, "landing_distance": 2300.0, "max_takeoff_mass": 170000.0, "unpaved": False,
               # m/s: rotate (VR) ~198 kn, approach ~160 kn, a steep fast initial climb
               "taxi_speed": 10.0, "rotate_speed": 102.0, "approach_speed": 82.0, "climb_rate": 15.0, "climb_speed": 125.0}


def _side(s, z, sign):
    return P(sign * Fuselage.side_x(s, z), s, z)


FWD, AFT, UP, DOWN = (0, -1, 0), (0, 1, 0), (0, 0, 1), (0, 0, -1)
_fuel_y = 9.0
_fuel_s = parts.le_s(_fuel_y) + 1.2

POINTS = [
    *[("PassengerDoor", f"Door_{side}{k + 1}", _side(s, FLOOR_Z, sign), (sign, 0, 0), (0.80, 1.72))
      for k, s in enumerate(DOORS) for side, sign in (("L", 1), ("R", -1))],
    ("CargoDoor", "Cargo_Fwd", _side(CARGO_FWD_S, CARGO_FWD_SILL, -1), (-1, 0, 0), (0.95, 0.70)),
    ("CargoDoor", "Cargo_Aft", _side(CARGO_AFT_S, CARGO_AFT_SILL, -1), (-1, 0, 0), (0.90, 0.72)),
    ("Fuel", "Fuel", (-_fuel_y, Y(_fuel_s), parts.wing_surface_z(_fuel_y, _fuel_s)), DOWN, (0.3, 0.3)),
    ("GroundPower", "GPU", P(-0.5, 9.0, float(Fuselage.zb(9.0)) + 0.05), (-0.4, 0, -0.9), (0.2, 0.2)),
    ("Water", "Water", P(0.5, 53.0, float(Fuselage.zb(53.0)) + 0.05), (0.4, 0, -0.9), (0.15, 0.15)),
    ("Lavatory", "Lav", P(-0.5, 53.6, float(Fuselage.zb(53.6)) + 0.05), (-0.4, 0, -0.9), (0.15, 0.15)),
    ("TowAttach", "TowBar", P(0, parts.NOSE_S - 0.25, parts.NOSE_AXLE_Z), FWD, (0, 0)),
    ("WheelChock", "Chock_Nose", P(0, parts.NOSE_S, 0), FWD, (0.9, 0)),
    ("WheelChock", "Chock_L", P(parts.MAIN_X, S_REF - parts.BOGIE_DS, 0), FWD, (1.4, 0)),
    ("WheelChock", "Chock_R", P(-parts.MAIN_X, S_REF - parts.BOGIE_DS, 0), FWD, (1.4, 0)),
]

_tip_z = parts.wing_station(parts.WING_TIP, h=None)["le"].z
RED, GREEN, WHITE = (1.0, 0.08, 0.05), (0.15, 1.0, 0.35), (1.0, 0.97, 0.9)
_tail_s = Fuselage.S1 + 0.14
_tail_z = float(Fuselage.zm(Fuselage.S1))
_logo_z = parts.wing_surface_z(parts.LOGO_LIGHT[0], parts.LOGO_LIGHT[1], True) + 0.05


def _toward_fin(sx):
    d = np.array([-sx * parts.LOGO_LIGHT[0], 55.5 - parts.LOGO_LIGHT[1], 9.0 - _logo_z])
    return tuple(float(v) for v in d / np.linalg.norm(d))


LIGHTS = [
    ("Navigation", "Nav_L", (parts.WING_TIP + 0.12, Y(parts.NAV_S), _tip_z), (0.82, -0.57, 0), 110, RED),
    ("Navigation", "Nav_R", (-parts.WING_TIP - 0.12, Y(parts.NAV_S), _tip_z), (-0.82, -0.57, 0), 110, GREEN),
    ("Navigation", "Nav_Tail", P(0, _tail_s, _tail_z), AFT, 140, WHITE),
    ("Strobe", "Strobe_L", (parts.WING_TIP + 0.1, Y(parts.NAV_S + 0.45), _tip_z), UP, 360, WHITE),
    ("Strobe", "Strobe_R", (-parts.WING_TIP - 0.1, Y(parts.NAV_S + 0.45), _tip_z), UP, 360, WHITE),
    ("Strobe", "Strobe_Tail", P(0, _tail_s + 0.02, _tail_z + 0.1), AFT, 360, WHITE),
    ("Beacon", "Beacon_Top", P(0, parts.BEACON_TOP_S, float(Fuselage.zt(parts.BEACON_TOP_S)) + 0.1), UP, 360, RED),
    ("Beacon", "Beacon_Bottom", P(0, parts.BEACON_BOTTOM_S, Fuselage.bottom_z(parts.BEACON_BOTTOM_S, 0) - 0.1), DOWN, 360, RED),
    ("Landing", "Landing_L", (parts.landing_point(1)[0][0], parts.landing_point(1)[0][1], parts.landing_point(1)[0][2] - 0.1),
     (0, -0.98, -0.18), 25, WHITE),
    ("Landing", "Landing_R", (parts.landing_point(-1)[0][0], parts.landing_point(-1)[0][1], parts.landing_point(-1)[0][2] - 0.1),
     (0, -0.98, -0.18), 25, WHITE),
    ("Taxi", "Taxi", P(0, parts.NOSE_S - 0.22, 1.45), (0, -0.97, -0.24), 50, WHITE),
    ("Logo", "Logo_L", P(parts.LOGO_LIGHT[0], parts.LOGO_LIGHT[1], _logo_z), _toward_fin(1), 35, WHITE),
    ("Logo", "Logo_R", P(-parts.LOGO_LIGHT[0], parts.LOGO_LIGHT[1], _logo_z), _toward_fin(-1), 35, WHITE),
]

# Afterburner flames: (name, nozzle exit centre, direction of the jet, exit radius, afterburner). The game draws
# the flame (AircraftAfterburner) out of each nozzle while a departure rolls and climbs away.
EXHAUSTS = [(f"Nozzle_{side}{k + 1}", P(sx * y, exit_s, parts.NOZZLE_Z), AFT, parts.EXIT_R - 0.04, True)
            for side, sx in (("L", 1), ("R", -1)) for k, (y, lip, exit_s) in enumerate(parts.NACELLES)]

# ============================================================== previews

_nose = Y(5.0)
VIEWS = {
    "front34": ((-34.0, -48.0, 14.0), (0.0, -4.0, 4.0), 40),
    "rear34": ((32.0, 44.0, 15.0), (0.0, 4.0, 4.0), 40),
    "side": ((105.0, 0.0, 5.5), (0.0, -6.0, 5.5), 50),
    "top": ((0.0, -6.0, 120.0), (0.0, -6.0, 0.0), 40),
    "front": ((0.0, -90.0, 5.0), (0.0, 0.0, 5.0), 30),
    "nose_close": ((-7.0, _nose - 10.0, 7.5), (0.0, _nose + 1.5, 5.2), 40),
    "nose_side": ((16.0, _nose + 1.0, 5.2), (0.0, _nose + 1.0, 5.2), -10.0),
    "tail_close": ((-20.0, 34.0, 13.0), (0.0, 13.0, 8.0), 40),
    "engine_close": ((-10.0, Y(31.0), 1.6), (-5.6, Y(40.0), 2.6), 45),
    "engine_rear": ((-12.0, Y(64.0), 3.2), (-5.6, Y(52.0), 2.9), 45),
    "gear_close": ((-10.0, 9.0, 1.8), (-parts.MAIN_X, 0.0, 1.4), 40),
    "game": ((-60.0, -45.0, 55.0), (0.0, -4.0, 2.0), 32),
    "side_ortho": ((110.0, -5.0, 6.0), (0.0, -5.0, 6.0), -66.0),
    "top_ortho": ((0.0, -5.0, 110.0), (0.0, -5.0, 0.0), -108.0),
    "front_ortho": ((0.0, -120.0, 6.0), (0.0, 0.0, 6.0), -30.0),
}
