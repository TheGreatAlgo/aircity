"""Regional twin turboprop (high wing, T-tail, 50 seats): the model definition the shared pipeline
(../Common/pipeline.py) builds. The player sees it as the Starling B50.

Stations `s` are metres aft of the nose; Blender Y = s - S_REF. Passenger windows and doors are painted into
the shading map rather than modelled, as on the jets.
"""
import numpy as np

import parts
from geom import MeshBuilder
from paint import curve_dist, round_rect_sdf
from parts import P, Y, Fuselage, S_REF  # noqa: F401  (Fuselage / S_REF are read by the pipeline)
from pipeline import Mover

NAME = "Turboprop"
# Baggage goes in by hand through two starboard bulk doors (the apron side, like the jets): forward (between the
# flight deck and the cabin) and aft.
CARGO_SILL_FWD = 1.42
CARGO_SILL_AFT = 2.00
# The forward door sits right behind the flight deck, well ahead of the propeller arcs (s 5.4-9.3), so the belt
# loader never works under a blade.
CARGO_STATIONS = (("Cargo_Fwd", 4.6), ("Cargo_Aft", 20.0))
CARGO_HOLDS = {"Cargo_Fwd": (3.9, 5.3), "Cargo_Aft": (18.9, 21.4)}
CARGO_HINGES = {"Cargo_Fwd": ("Top", 145.0, 4.0), "Cargo_Aft": ("Top", 145.0, 4.0)}
DISPLAY_NAME = "Regional turboprop"
SEATS = 50  # typical layout; the player sees "Starling {code}50"
CATEGORY = "Airliner"
AO_DISTANCE = 1.8

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
        nacelle, prop, hub = parts.build_engine(sign)
        body.merge(nacelle)
        movers.append(Mover("Propeller_" + side, prop, hub))
    r0, r1 = parts.RUDDER
    movers.append(Mover("Rudder", vt["Rudder"], parts.vt_hinge((r0 + r1) / 2), hinge=parts.vt_hinge(r1) - parts.vt_hinge(r0),
                        axis="z"))
    for side, sgn in (("L", 1), ("R", -1)):
        a, b = parts.ht_hinge(sgn * parts.EL_ROOT), parts.ht_hinge(sgn * (parts.HT_SPAN - 0.25))
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
LOD1_KEEP = ["Propeller_L", "Propeller_R"]      # the 3.9 m discs read from far off
COLLIDER_RATIO = 0.3

# ============================================================== UV atlas

UV_TOP_BOTTOM = {"wing", "wing_flap", "wing_aileron", "wing_filler", "htail", "elevator"}
UV_SIDE = {"vtail", "rudder", "dorsal", "bullet"}
DENSITY = {"wing": 0.5, "wing_flap": 0.5, "wing_aileron": 0.5, "wing_filler": 0.4, "htail": 0.6, "elevator": 0.6,
           "wing_cap": 0.1, "htail_cap": 0.2, "vtail_cap": 0.2, "elevator_cap": 0.2, "wing_flap_cap": 0.15,
           "wing_aileron_cap": 0.15, "wing_filler_cap": 0.15, "rudder_cap": 0.2, "bullet": 0.5,
           "nacelle": 0.8, "gearleg": 0.3, "spinner": 0.6}
BOTTOM_DENSITY = 0.5

# ============================================================== livery schemes

CHEAT_Z = parts.KEEL + 1.85       # passenger-window line
RADOME_S = 0.95                   # radome joint at the tip's height; it leans forwards at the top
DOOR_1_S = 4.4                    # forward airstair door (L1)
DOOR_2_S = 19.0                   # rear service door (R2)


def _radome(p):
    """Signed distance to the radome joint, negative on the radome."""
    return p.s - (RADOME_S - 0.15 * (p.z - parts.KEEL - parts.TIP_H))


def _tail(p):
    return p.f(p.vt)


def _engines(p):
    return np.maximum(p.f(p.group("nacelle")), p.f(p.group("spinner")))


def scheme_classic(p):
    """Cheatline through the windows sweeping up into the fin, a pinstripe below, painted nacelles."""
    knots = [(0.3, 2.2), (2.5, 2.45), (4.5, CHEAT_Z - 0.28), (16.5, CHEAT_Z - 0.28), (18.5, 3.0), (20.0, 3.75),
             (21.6, 5.2), (23.2, 7.3)]
    d = curve_dist(p.s, p.z, knots)
    zone = (p.fus | p.vt) * p.inside(-_radome(p))
    r = p.band(d, -0.22, 0.09) * zone
    g = p.band(d, -0.36, -0.29) * zone
    return r, g, _engines(p)


def scheme_swoosh(p):
    """A belly-to-tail swoosh that grows from under the wing, the whole fin in colour 3."""
    lo = [(9.0, 1.6), (13.0, 1.3), (17.0, 1.7), (20.0, 2.6), (23.5, 3.5)]
    hi = [(9.0, 1.6), (13.0, 2.1), (16.5, 2.9), (19.0, 3.6), (21.5, 4.4)]
    zone = p.fus
    start = p.inside(9.0 - p.s)
    d_lo, d_hi = curve_dist(p.s, p.z, lo), curve_dist(p.s, p.z, hi)
    r = np.minimum(p.inside(-d_lo), p.inside(d_hi)) * start * zone
    g = p.band(d_hi, 0.07, 0.15) * start * zone
    return r, g, np.maximum(_tail(p), _engines(p))


def scheme_two_tone(p):
    """Airline standard: grey belly under a cheatline, painted tail and nacelles."""
    cheat = [(-1, 1.7), (3.0, 1.95), (8.0, 2.0), (17.0, 2.0), (20.0, 2.5), (25.5, 3.3)]
    d = curve_dist(p.s, p.z, cheat)
    r = p.inside(d + 0.07) * p.fus
    g = p.band(d, -0.07, 0.07) * p.fus
    return r, g, np.maximum(_tail(p), _engines(p))


def scheme_trainer(p):
    """Charter / 'eurowhite': coloured nose cone and nacelles, painted spinners and lower fin, a window-line stripe."""
    r = np.maximum(p.inside(_radome(p)) * p.fus, p.f(p.group("nacelle")))
    g = np.maximum(p.f(p.group("spinner")), p.inside(5.6 - p.z) * p.vt)
    d = p.z - (CHEAT_Z - 0.40)
    b = p.band(d, -0.045, 0.045) * p.fus * p.inside(2.6 - p.s) * p.inside(p.s - 22.0) * (np.abs(p.nx) > 0.35)
    return r, g, b


SCHEMES = {"Classic": scheme_classic, "Swoosh": scheme_swoosh, "TwoTone": scheme_two_tone, "Trainer": scheme_trainer}

# Registration on the rear fuselage (arc metres from the widest line, up positive): below the cheatline on
# Classic, above the windows otherwise
REG_BOX = {"Classic": (17.2, 19.9, -0.78, -0.43), "Swoosh": (16.4, 19.1, 0.78, 1.13),
           "TwoTone": (17.2, 19.9, 0.78, 1.13), "Trainer": (17.2, 19.9, 0.78, 1.13)}
REG_FACES_U = (15.8, 21.5)
REG_FACES_V = (-1.0, 1.4)
REG_TEX_W = 640

LOGO_BOX = (22.3, 24.2, 4.6, 6.6)

# ============================================================== shading, dirt

WINDOWS = [6.0 + 0.72 * k for k in range(15)]     # 15 per side, behind the forward hold


def _windows(p):
    side = (np.abs(p.nx) > 0.5) & p.fus
    w = np.zeros_like(p.x)
    for s in WINDOWS:
        w = np.maximum(w, p.inside(round_rect_sdf(p.s, p.z, s, CHEAT_Z, 0.13, 0.17, 0.10)))
    return w * side


def _doors(p):
    """Door outlines: the port airstair (L1), the starboard rear service door (R2), the two starboard cargo doors."""
    side = (np.abs(p.nx) > 0.35) & p.fus
    lines = [p.line(round_rect_sdf(p.s, p.z, DOOR_1_S, parts.KEEL + 0.12 + 0.85, 0.38, 0.85, 0.12), 0.012) * side * (p.x > 0),
             p.line(round_rect_sdf(p.s, p.z, DOOR_2_S, 1.55 + 0.78, 0.35, 0.78, 0.12), 0.012) * side * (p.x < 0)]
    for (_, station), sill, hw, hh in zip(CARGO_STATIONS, (CARGO_SILL_FWD, CARGO_SILL_AFT), (0.60, 0.60), (0.60, 0.55)):
        lines.append(p.line(round_rect_sdf(p.s, p.z, station, sill + hh, hw, hh, 0.08), 0.012) * side * (p.x < 0))
    return np.maximum.reduce(lines)


def panel_lines(p):
    w = 0.012
    lines = [_doors(p)]
    lines.append(p.line(_radome(p), w * 0.8) * p.fus * 0.4)
    for s in (4.9, 9.0, 13.5, 17.6):
        lines.append(p.line(p.s - s, w * 0.7) * p.fus * 0.5)
    # nacelle cowl split lines and the wing-root fairing line along the crown
    nac = p.group("nacelle")
    lines.append(p.line(p.s - (parts.NACELLE_S0 + 0.5), w) * nac)
    lines.append(p.line(p.s - (parts.NACELLE_S0 + 2.2), w) * nac)
    lines.append(p.line(p.s - parts.S_REF + 0.9, w * 0.8) * nac * 0.6)
    return np.clip(np.maximum.reduce(lines), 0, 1)


def detail_extra(p):
    """Passenger windows, painted as dark glass with a light frame. Cockpit windows are real geometry."""
    return 1.0 - 0.9 * _windows(p)


def dirt_extra(p):
    """Exhaust soot streaking back along the nacelle tops, a grimy belly behind the nose gear."""
    stain = np.zeros_like(p.x)
    for sx in (1, -1):
        stain = np.maximum(stain, np.exp(-((p.x - sx * (parts.ENGINE_X + 0.45)) / 0.35) ** 2) * (p.s > 9.3) * (p.nz > 0.1))
    belly = p.fus * (p.nz < -0.6) * p.inside(p.s - 3.5) * p.inside(13.0 - p.s)
    return -0.14 * stain * p.group("nacelle") - 0.07 * belly


# ============================================================== gameplay data (Blender space; the pipeline converts)

def _wheels():
    out = [("Wheel_Nose_L", P(parts.NOSE_WHEEL_X, parts.NOSE_S, parts.NOSE_AXLE_Z), parts.NOSE_R, True),
           ("Wheel_Nose_R", P(-parts.NOSE_WHEEL_X, parts.NOSE_S, parts.NOSE_AXLE_Z), parts.NOSE_R, True)]
    for side, sx in (("L", 1), ("R", -1)):
        for k, dx in ((1, -parts.MAIN_WHEEL_DX), (2, parts.MAIN_WHEEL_DX)):
            out.append((f"Wheel_{side}{k}", P(sx * (parts.MAIN_X + dx), parts.S_REF, parts.MAIN_AXLE_Z), parts.MAIN_R,
                        False))
    return out


GEAR = {"wheels": _wheels(), "steering_pivot": P(0, parts.NOSE_S, 0.0), "max_steering": 60.0}

# runway needs: a 50-seat regional turboprop at maximum weight, sea level, ISA. Turboprops take gravel and
# grass strips.
PERFORMANCE = {"takeoff_distance": 1350.0, "landing_distance": 1050.0, "max_takeoff_mass": 23000.0, "unpaved": True,
               # m/s: taxi 18 kn, rotate 110 kn, approach 108 kn, climb 1,500 ft/min at 150 kn
               "taxi_speed": 9.0, "rotate_speed": 57.0, "approach_speed": 55.5, "climb_rate": 7.6, "climb_speed": 77.0}


def _side(s, z, sign):
    return P(sign * Fuselage.side_x(s, z), s, z)


FWD, AFT, UP, DOWN = (0, -1, 0), (0, 1, 0), (0, 0, 1), (0, 0, -1)

POINTS = [
    # doors: position = sill centre. The airstair sill is 1.2 m up: no jet bridge, passengers walk across the apron.
    ("PassengerDoor", "Door_L1", _side(DOOR_1_S, parts.KEEL + 0.12, 1), (1, 0, 0), (0.76, 1.70)),
    ("PassengerDoor", "Door_R2", _side(DOOR_2_S, 1.55, -1), (-1, 0, 0), (0.70, 1.55)),
    ("CargoDoor", "Cargo_Fwd", _side(4.6, CARGO_SILL_FWD, -1), (-1, 0, 0), (1.20, 1.20)),
    ("CargoDoor", "Cargo_Aft", _side(20.0, CARGO_SILL_AFT, -1), (-1, 0, 0), (1.20, 1.10)),
    ("Fuel", "Fuel_L", (5.5, Y(parts.le_s(5.5) + 0.8), parts.wing_surface_z(5.5, parts.le_s(5.5) + 0.8, upper=True)), UP, (0.25, 0.25)),
    ("Fuel", "Fuel_R", (-5.5, Y(parts.le_s(5.5) + 0.8), parts.wing_surface_z(5.5, parts.le_s(5.5) + 0.8, upper=True)), UP, (0.25, 0.25)),
    ("GroundPower", "GPU", P(-0.45, 4.0, float(Fuselage.zb(4.0)) + 0.05), (-0.4, 0, -0.9), (0.2, 0.2)),
    ("Water", "Water", P(0.5, 17.5, float(Fuselage.zb(17.5)) + 0.05), (0.4, 0, -0.9), (0.15, 0.15)),
    ("Lavatory", "Lav", P(-0.5, 18.5, float(Fuselage.zb(18.5)) + 0.05), (-0.4, 0, -0.9), (0.15, 0.15)),
    ("TowAttach", "TowBar", P(0, parts.NOSE_S - 0.15, parts.NOSE_AXLE_Z), FWD, (0, 0)),
    ("WheelChock", "Chock_Nose", P(0, parts.NOSE_S, 0), FWD, (0.5, 0)),
    ("WheelChock", "Chock_L", P(parts.MAIN_X, parts.S_REF, 0), FWD, (0.9, 0)),
    ("WheelChock", "Chock_R", P(-parts.MAIN_X, parts.S_REF, 0), FWD, (0.9, 0)),
]

_tip_z = parts.wing_station(parts.WING_TIP, h=None)["le"].z
RED, GREEN, WHITE = (1.0, 0.08, 0.05), (0.15, 1.0, 0.35), (1.0, 0.97, 0.9)
_land_y, _land_s = parts.LANDING
_logo_x, _logo_s = parts.LOGO_LIGHT

LIGHTS = [
    ("Navigation", "Nav_L", (parts.WING_TIP + 0.1, Y(parts.NAV_S), _tip_z + 0.01), (0.82, -0.57, 0), 110, RED),
    ("Navigation", "Nav_R", (-parts.WING_TIP - 0.1, Y(parts.NAV_S), _tip_z + 0.01), (-0.82, -0.57, 0), 110, GREEN),
    ("Navigation", "Nav_Tail", P(0, Fuselage.S1 + 0.07, float(Fuselage.zm(Fuselage.S1))), AFT, 140, WHITE),
    ("Strobe", "Strobe_L", (parts.WING_TIP + 0.08, Y(parts.NAV_S + 0.26), _tip_z + 0.02), UP, 360, WHITE),
    ("Strobe", "Strobe_R", (-parts.WING_TIP - 0.08, Y(parts.NAV_S + 0.26), _tip_z + 0.02), UP, 360, WHITE),
    ("Strobe", "Strobe_Tail", P(0, Fuselage.S1 + 0.09, float(Fuselage.zm(Fuselage.S1)) + 0.1), AFT, 360, WHITE),
    ("Beacon", "Beacon_Top", P(0, parts.BEACON_TOP_S, float(Fuselage.zt(parts.BEACON_TOP_S)) + 0.08), UP, 360, RED),
    ("Beacon", "Beacon_Bottom", P(0, parts.BEACON_BOTTOM_S, Fuselage.bottom_z(parts.BEACON_BOTTOM_S, 0) - 0.08), DOWN, 360, RED),
    ("Landing", "Landing_L", (_land_y, Y(_land_s) - 0.02, parts.wing_station(_land_y, h=None)["le"].z + 0.01), (0, -0.98, -0.18), 25, WHITE),
    ("Landing", "Landing_R", (-_land_y, Y(_land_s) - 0.02, parts.wing_station(_land_y, h=None)["le"].z + 0.01), (0, -0.98, -0.18), 25, WHITE),
    ("Taxi", "Taxi", P(0, parts.NOSE_S - 0.15, 0.80), (0, -0.97, -0.24), 50, WHITE),
    ("Logo", "Logo_L", P(_logo_x, _logo_s, float(Fuselage.top_z(_logo_s, _logo_x)) + 0.05), (-0.25, 0.45, 0.86), 35, WHITE),
    ("Logo", "Logo_R", P(-_logo_x, _logo_s, float(Fuselage.top_z(_logo_s, _logo_x)) + 0.05), (0.25, 0.45, 0.86), 35, WHITE),
]

# ============================================================== previews

VIEWS = {
    "front34": ((-20.0, -26.0, 8.0), (0.0, -1.5, 2.8), 40),
    "rear34": ((20.0, 24.0, 9.0), (0.0, 3.0, 3.0), 40),
    "side": ((55.0, 0.0, 3.5), (0.0, 0.0, 3.5), 50),
    "top": ((0.0, 0.0, 48.0), (0.0, 0.0, 0.0), 35),
    "front": ((0.0, -38.0, 3.0), (0.0, 0.0, 3.0), 45),
    "nose_close": ((-6.0, -17.0, 4.5), (0.0, -9.5, 2.8), 40),
    "tail_close": ((-10.0, 18.0, 7.0), (0.0, 10.0, 5.0), 40),
    "engine_close": ((-8.0, -12.0, 3.0), (-4.1, -4.5, 3.4), 40),
    "gear_close": ((-9.0, 7.0, 1.6), (-4.1, 0.0, 1.2), 40),
    "game": ((-38.0, -28.0, 34.0), (0.0, 0.0, 1.5), 32),
    "side_ortho": ((55.0, 0.0, 3.8), (0.0, 0.0, 3.8), -28.0),
    "top_ortho": ((0.0, 0.0, 55.0), (0.0, 0.0, 0.0), -28.0),
    "front_ortho": ((0.0, -55.0, 3.8), (0.0, 0.0, 3.8), -26.0),
}
