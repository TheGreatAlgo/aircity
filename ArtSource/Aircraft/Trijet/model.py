"""Long-range trijet (250 seats, two wing engines and one in the fin root): the model definition the shared
pipeline (../Common/pipeline.py) builds. The player sees it as the Starling D250.

Stations `s` are metres aft of the nose; Blender Y = s - S_REF. Passenger windows and doors are painted into
the shading map rather than modelled, as on the other jets.
"""
import numpy as np

import parts
from geom import MeshBuilder
from paint import curve_dist, round_rect_sdf
from parts import P, Y, Fuselage, S_REF  # noqa: F401  (Fuselage / S_REF are read by the pipeline)
from pipeline import Mover

NAME = "Trijet"
CARGO_FORWARD_STATION = 12.5   # ahead of the engines, clear for the container loader and its dolly train
CARGO_AFT_STATION = 40.5
CARGO_BULK_STATION = 45.4
CARGO_HOLDS = {"Cargo_Bulk": (44.0, 47.2)}
CARGO_HINGES = {"Cargo_Fwd": ("Top", 145.0, 9.0), "Cargo_Aft": ("Top", 145.0, 9.0), "Cargo_Bulk": ("Top", -100.0, 3.5)}
DISPLAY_NAME = "Long-range trijet"
SEATS = 250  # typical layout; the player sees "Starling {code}250"
CATEGORY = "Airliner"
AO_DISTANCE = 3.5

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
    body.merge(parts.build_details())
    movers = []
    for sign, side in ((1, "L"), (-1, "R")):
        nacelle, fan, hub = parts.build_engine(sign)
        body.merge(nacelle)
        movers.append(Mover("Fan_" + side, fan, hub))
    tail, fan, hub = parts.build_tail_engine()
    body.merge(tail)
    movers.append(Mover("Fan_C", fan, hub))
    r0, r1 = parts.RUDDER
    movers.append(Mover("Rudder", vt["Rudder"], parts.vt_hinge((r0 + r1) / 2), hinge=parts.vt_hinge(r1) - parts.vt_hinge(r0),
                        axis="z"))
    for side, sgn in (("L", 1), ("R", -1)):
        a, b = parts.ht_hinge(sgn * parts.EL_ROOT), parts.ht_hinge(sgn * 8.12)
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
LOD1_KEEP = []
COLLIDER_RATIO = 0.3

# ============================================================== UV atlas

UV_TOP_BOTTOM = {"wing", "wing_flap", "wing_aileron", "wing_filler", "htail", "elevator"}
UV_SIDE = {"vtail", "rudder", "dorsal", "pylon", "tailnacelle"}
DENSITY = {"wing": 0.45, "wing_flap": 0.45, "wing_aileron": 0.45, "wing_filler": 0.35, "htail": 0.55, "elevator": 0.55,
           "wing_cap": 0.1, "htail_cap": 0.2, "vtail_cap": 0.2, "elevator_cap": 0.2, "wing_flap_cap": 0.15,
           "wing_aileron_cap": 0.15, "wing_filler_cap": 0.15, "rudder_cap": 0.2,
           "nacelle": 0.8, "tailnacelle": 0.7, "pylon": 0.5, "pylon_cap": 0.2, "gearleg": 0.25, "spinner": 0.5}
BOTTOM_DENSITY = 0.5

# ============================================================== livery schemes

FLOOR_Z = parts.KEEL + 2.05     # cabin floor = passenger-door sills
CHEAT_Z = 5.15                  # passenger-window line
DOORS = (7.6, 17.4, 36.6, 46.4)  # passenger doors 1-4 (both sides)
DOOR_HW, DOOR_HH = 0.535, 0.965
RADOME_S = parts.WS_FRONT - 0.25 + 0.17 * (parts.SILL - parts.TIP_H)


def _radome(p):
    """Signed distance to the radome joint, negative on the radome."""
    return p.s - (RADOME_S - 0.17 * (p.z - parts.KEEL - parts.TIP_H))


def _tail(p):
    """The fin and the centre engine's fairing, painted together."""
    return np.maximum(p.f(p.vt), p.f(p.group("tailnacelle")))


def _engines(p):
    return p.f(p.group("nacelle"))


def scheme_classic(p):
    """Cheatline through the windows sweeping up over the centre intake into the fin, a pinstripe below."""
    knots = [(0.6, 4.0), (4.0, 4.4), (8.5, CHEAT_Z - 0.45), (38.0, CHEAT_Z - 0.45), (42.5, 5.9), (46.5, 8.0),
             (49.5, 10.8), (52.5, 14.0), (54.5, 17.0)]
    d = curve_dist(p.s, p.z, knots)
    zone = (p.fus | p.vt | p.group("tailnacelle")) * p.inside(-_radome(p))
    r = p.band(d, -0.42, 0.15) * zone
    g = p.band(d, -0.68, -0.55) * zone
    return r, g, _engines(p)


def scheme_swoosh(p):
    """A belly-to-tail swoosh that grows from the wing root, the whole tail in colour 3."""
    lo = [(21.0, 2.9), (30.0, 2.5), (42.0, 3.4), (50.0, 5.0), (58.0, 6.3)]
    hi = [(21.0, 2.9), (30.0, 4.0), (40.0, 5.4), (46.0, 6.9), (50.0, 8.0)]
    zone = p.fus
    start = p.inside(21.0 - p.s)
    d_lo, d_hi = curve_dist(p.s, p.z, lo), curve_dist(p.s, p.z, hi)
    r = np.minimum(p.inside(-d_lo), p.inside(d_hi)) * start * zone
    g = p.band(d_hi, 0.13, 0.28) * start * zone
    return r, g, np.maximum(_tail(p), _engines(p))


def scheme_two_tone(p):
    """Airline standard: grey belly under a cheatline, painted tail and engines."""
    cheat = [(-1, 2.9), (6.0, 3.25), (18.0, 3.3), (40.0, 3.3), (47.0, 4.1), (58.5, 5.7)]
    d = curve_dist(p.s, p.z, cheat)
    r = p.inside(d + 0.13) * p.fus
    g = p.band(d, -0.13, 0.13) * p.fus
    return r, g, np.maximum(_tail(p), _engines(p))


def scheme_trainer(p):
    """Charter / 'eurowhite': coloured nose cone and engine cowls, the lower fin, a window-line stripe."""
    r = np.maximum(p.inside(_radome(p)) * p.fus, _engines(p))
    g = np.maximum(p.f(p.group("tailnacelle")), p.inside(13.5 - p.z) * p.vt)
    d = p.z - (CHEAT_Z - 0.65)
    b = p.band(d, -0.08, 0.08) * p.fus * p.inside(4.0 - p.s) * p.inside(p.s - 50.0) * (np.abs(p.nx) > 0.35)
    return r, g, b


SCHEMES = {"Classic": scheme_classic, "Swoosh": scheme_swoosh, "TwoTone": scheme_two_tone, "Trainer": scheme_trainer}

# Registration on the rear fuselage ahead of the stabiliser (arc metres from the widest line, up positive)
REG_BOX = {"Classic": (38.5, 43.0, -1.55, -1.05), "Swoosh": (33.5, 38.0, 1.35, 1.85),
           "TwoTone": (38.5, 43.0, 0.70, 1.20), "Trainer": (38.5, 43.0, 0.70, 1.20)}
REG_FACES_U = (33.0, 44.0)
REG_FACES_V = (-1.75, 2.1)
REG_TEX_W = 1024

LOGO_BOX = (51.8, 55.8, 10.2, 14.4)

# ============================================================== shading, dirt

WINDOW_PITCH = 0.635
WINDOWS = [s for s in np.arange(8.8, 47.6, WINDOW_PITCH) if all(abs(s - d) > 0.75 for d in DOORS)]


def _windows(p):
    side = (np.abs(p.nx) > 0.5) & p.fus
    w = np.zeros_like(p.x)
    for s in WINDOWS:
        w = np.maximum(w, p.inside(round_rect_sdf(p.s, p.z, s, CHEAT_Z, 0.15, 0.22, 0.12)))
    return w * side


def _doors(p):
    both = (np.abs(p.nx) > 0.35) & p.fus
    stbd = (p.nx < -0.35) & p.fus
    lines = []
    for s in DOORS:
        lines.append(p.line(round_rect_sdf(p.s, p.z, s, FLOOR_Z + DOOR_HH, DOOR_HW, DOOR_HH, 0.22), 0.016) * both)
    for s, z, hw, hh, r in ((CARGO_FORWARD_STATION, 3.35, 1.30, 0.80, 0.10), (CARGO_AFT_STATION, 3.55, 1.30, 0.80, 0.10),
                            (CARGO_BULK_STATION, 3.90, 0.45, 0.45, 0.08)):
        lines.append(p.line(round_rect_sdf(p.s, p.z, s, z, hw, hh, r), 0.016) * stbd)
    return np.maximum.reduce(lines)


def panel_lines(p):
    w = 0.016
    lines = [_doors(p)]
    lines.append(p.line(_radome(p), w * 0.8) * p.fus * 0.4)
    for s in (6.4, 13.5, 19.5, 39.5, 46.0, 52.5):
        lines.append(p.line(p.s - s, w * 0.7) * p.fus * 0.5)
    fair = (p.s > 19.5) & (p.s < 38.5) & (p.z < 3.5) & p.fus
    lines.append(p.line(p.z - 3.45, w) * fair)
    nac = p.group("nacelle")
    lines.append(p.line(p.s - (parts.INLET_S + parts.K * parts.L * 0.6), w) * nac)
    lines.append(p.line(p.s - (parts.INLET_S + parts.K * parts.L * 2.7), w) * nac)
    return np.clip(np.maximum.reduce(lines), 0, 1)


def detail_extra(p):
    return 1.0 - 0.9 * _windows(p)


def dirt_extra(p):
    """Sooty low-bypass exhaust behind all three engines, grimy belly and gear bays."""
    stain = np.zeros_like(p.x)
    for sx in (1, -1):
        stain = np.maximum(stain, np.exp(-((p.x - sx * parts.ENGINE_X) / 1.8) ** 2) *
                           (p.s > parts.INLET_S + 7.0) * (p.nz < 0))
    tail = p.fus * np.exp(-(p.x / 1.2) ** 2) * (p.s > 54.0)
    belly = p.fus * (p.nz < -0.6) * p.inside(p.s - 37.0) * p.inside(19.0 - p.s)
    return -0.14 * stain * p.wing - 0.10 * tail - 0.08 * belly


# ============================================================== gameplay data (Blender space; the pipeline converts)

def _wheels():
    out = [("Wheel_Nose_L", P(parts.NOSE_WHEEL_X, parts.NOSE_S, parts.NOSE_AXLE_Z), parts.NOSE_R, True),
           ("Wheel_Nose_R", P(-parts.NOSE_WHEEL_X, parts.NOSE_S, parts.NOSE_AXLE_Z), parts.NOSE_R, True)]
    for side, sx in (("L", 1), ("R", -1)):
        for k, ds in parts.MAIN_AXLES:
            out.append((f"Wheel_{side}{k}", P(sx * parts.MAIN_X, S_REF + ds, parts.MAIN_AXLE_Z), parts.MAIN_R, False))
    return out


GEAR = {"wheels": _wheels(), "steering_pivot": P(0, parts.NOSE_S, 0.0), "max_steering": 68.0}

# runway needs: an older long-range trijet at maximum weight, sea level, ISA. Thirsty engines and a heavy
# airframe for its seats, so it needs more runway than the twins. Jets need a paved runway.
PERFORMANCE = {"takeoff_distance": 3000.0, "landing_distance": 1850.0, "max_takeoff_mass": 210000.0, "unpaved": False,
               "taxi_speed": 10.0, "rotate_speed": 80.0, "approach_speed": 73.0, "climb_rate": 10.5, "climb_speed": 88.0}


def _side(s, z, sign):
    return P(sign * Fuselage.side_x(s, z), s, z)


FWD, AFT, UP, DOWN = (0, -1, 0), (0, 1, 0), (0, 0, 1), (0, 0, -1)
_fuel_y = 11.5
_fuel_s = parts.le_s(_fuel_y) + 1.4

POINTS = [
    *[("PassengerDoor", f"Door_{side}{k + 1}", _side(s, FLOOR_Z, sign), (sign, 0, 0), (1.07, 1.93))
      for k, s in enumerate(DOORS) for side, sign in (("L", 1), ("R", -1))],
    ("CargoDoor", "Cargo_Fwd", _side(CARGO_FORWARD_STATION, 2.55, -1), (-1, 0, 0), (2.60, 1.60)),
    ("CargoDoor", "Cargo_Aft", _side(CARGO_AFT_STATION, 2.75, -1), (-1, 0, 0), (2.60, 1.60)),
    ("CargoDoor", "Cargo_Bulk", _side(CARGO_BULK_STATION, 3.45, -1), (-1, 0, 0), (0.90, 0.90)),
    ("Fuel", "Fuel", (-_fuel_y, Y(_fuel_s), parts.wing_surface_z(_fuel_y, _fuel_s)), DOWN, (0.4, 0.4)),
    ("GroundPower", "GPU", P(-0.7, 4.4, float(Fuselage.zb(4.4)) + 0.08), (-0.4, 0, -0.9), (0.25, 0.25)),
    ("Water", "Water", P(0.8, 43.0, float(Fuselage.zb(43.0)) + 0.08), (0.4, 0, -0.9), (0.18, 0.18)),
    ("Lavatory", "Lav", P(-0.8, 48.0, float(Fuselage.zb(48.0)) + 0.08), (-0.4, 0, -0.9), (0.18, 0.18)),
    ("TowAttach", "TowBar", P(0, parts.NOSE_S - 0.3, parts.NOSE_AXLE_Z), FWD, (0, 0)),
    ("WheelChock", "Chock_Nose", P(0, parts.NOSE_S, 0), FWD, (1.1, 0)),
    ("WheelChock", "Chock_L", P(parts.MAIN_X, S_REF - parts.BOGIE_DS, 0), FWD, (1.75, 0)),
    ("WheelChock", "Chock_R", P(-parts.MAIN_X, S_REF - parts.BOGIE_DS, 0), FWD, (1.75, 0)),
]

_tip_z = parts.wing_station(parts.WING_TIP, h=None)["le"].z
_logo_z = parts.ht_station(parts.LOGO_LIGHT[0], h=None)["le"].z + 0.5
RED, GREEN, WHITE = (1.0, 0.08, 0.05), (0.15, 1.0, 0.35), (1.0, 0.97, 0.9)
_tail_light_s = parts.vt_le_s(15.75) + parts.vt_chord(15.75)

LIGHTS = [
    ("Navigation", "Nav_L", (parts.WING_TIP + 0.15, Y(parts.NAV_S), _tip_z + 0.03), (0.82, -0.57, 0), 110, RED),
    ("Navigation", "Nav_R", (-parts.WING_TIP - 0.15, Y(parts.NAV_S), _tip_z + 0.03), (-0.82, -0.57, 0), 110, GREEN),
    ("Navigation", "Nav_Tail", P(0, _tail_light_s + 0.02, 15.75), AFT, 140, WHITE),
    ("Strobe", "Strobe_L", (parts.WING_TIP + 0.12, Y(parts.NAV_S + 0.4), _tip_z + 0.04), UP, 360, WHITE),
    ("Strobe", "Strobe_R", (-parts.WING_TIP - 0.12, Y(parts.NAV_S + 0.4), _tip_z + 0.04), UP, 360, WHITE),
    ("Strobe", "Strobe_Tail", P(0, _tail_light_s + 0.04, 15.9), AFT, 360, WHITE),
    ("Beacon", "Beacon_Top", P(0, parts.BEACON_TOP_S, float(Fuselage.zt(parts.BEACON_TOP_S)) + 0.13), UP, 360, RED),
    ("Beacon", "Beacon_Bottom", P(0, parts.BEACON_BOTTOM_S, Fuselage.bottom_z(parts.BEACON_BOTTOM_S, 0) - 0.13), DOWN, 360, RED),
    ("Landing", "Landing_L", (parts.LANDING[0], Y(parts.LANDING[1]), parts.wing_surface_z(*parts.LANDING) - 0.12),
     (0, -0.98, -0.18), 25, WHITE),
    ("Landing", "Landing_R", (-parts.LANDING[0], Y(parts.LANDING[1]), parts.wing_surface_z(*parts.LANDING) - 0.12),
     (0, -0.98, -0.18), 25, WHITE),
    ("Taxi", "Taxi", P(0, parts.NOSE_S - 0.25, 1.42), (0, -0.97, -0.24), 50, WHITE),
    ("Logo", "Logo_L", P(parts.LOGO_LIGHT[0], parts.LOGO_LIGHT[1], _logo_z), (-0.5, -0.25, 0.83), 35, WHITE),
    ("Logo", "Logo_R", P(-parts.LOGO_LIGHT[0], parts.LOGO_LIGHT[1], _logo_z), (0.5, -0.25, 0.83), 35, WHITE),
]

# ============================================================== previews

_nose = Y(4.5)
VIEWS = {
    "front34": ((-46.0, -58.0, 18.0), (0.0, -3.0, 5.5), 40),
    "rear34": ((46.0, 56.0, 21.0), (0.0, 6.0, 6.0), 40),
    "side": ((120.0, 0.0, 7.0), (0.0, 0.0, 7.0), 50),
    "top": ((0.0, 0.0, 105.0), (0.0, 0.0, 0.0), 35),
    "front": ((0.0, -85.0, 6.0), (0.0, 0.0, 6.0), 45),
    "nose_close": ((-12.0, _nose - 16.0, 9.0), (0.0, _nose + 1.0, 5.2), 40),
    "tail_close": ((-28.0, 44.0, 17.0), (0.0, 20.0, 9.5), 40),
    "tail_engine": ((-14.0, Y(36.0), 12.0), (0.0, Y(45.0), 8.5), 40),
    "engine_close": ((-17.0, Y(9.0), 4.0), (-parts.ENGINE_X, Y(23.0), 2.4), 40),
    "gear_close": ((-12.0, 10.0, 2.0), (-parts.MAIN_X, 0.0, 1.1), 40),
    "game": ((-85.0, -62.0, 78.0), (0.0, 0.0, 3.0), 32),
    "side_ortho": ((120.0, 0.0, 7.5), (0.0, -1.0, 7.5), -62.0),
    "top_ortho": ((0.0, 0.0, 120.0), (0.0, -1.0, 0.0), -62.0),
    "front_ortho": ((0.0, -120.0, 7.5), (0.0, 0.0, 7.5), -58.0),
}
