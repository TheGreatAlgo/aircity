"""Starling A2 geometry. Stations are metres aft of the spinner tip."""

import math

import numpy as np
from mathutils import Vector

from fuselage import Fuselage as BaseFuselage, Loft
from geom import MeshBuilder, airfoil_eval, pchip
from pipeline import Mover
from shapes import _fd_normal, drape_pane, lifting_surface, revolve, rounded_outline, tube, wheel


LENGTH = 8.00
S_REF = 3.12
THRUST_Z = 1.15
DOOR_S, DOOR_SILL, DOOR_W, DOOR_H = 2.38, .91, 1.08, .97
BAG_S, BAG_SILL, BAG_W, BAG_H = 4.42, .94, .44, .36
REG_S = (4.80, 6.54)


def P(x, s, z):
    return Vector((x, s - S_REF, z))


# Low rounded cowl, raked canopy and narrow tailcone.
_SECTIONS = np.array([
    (.32, .337, .868, 1.445), (.40, .383, .829, 1.483), (.57, .421, .797, 1.515),
    (.92, .451, .759, 1.543), (1.20, .477, .738, 1.568), (1.40, .492, .731, 1.633),
    (1.62, .520, .727, 1.782), (1.84, .541, .725, 1.916), (2.12, .555, .722, 1.987),
    (2.54, .563, .724, 1.998), (2.88, .558, .732, 1.984), (3.31, .555, .739, 1.965),
    (3.60, .550, .745, 1.942), (4.00, .515, .767, 1.836), (4.40, .461, .817, 1.666),
    (4.82, .388, .881, 1.520), (5.32, .312, .945, 1.433), (5.95, .225, 1.008, 1.366),
    (6.64, .155, 1.059, 1.314), (7.22, .100, 1.096, 1.274), (7.80, .040, 1.144, 1.225),
    (7.97, .012, 1.170, 1.201),
])


class Fuselage(BaseFuselage):
    S0, S1 = _SECTIONS[0, 0], _SECTIONS[-1, 0]
    hw, zb, zt = (staticmethod(pchip(_SECTIONS[:, 0], _SECTIONS[:, i])) for i in (1, 2, 3))
    fm = staticmethod(lambda s: .48 - .10 * np.clip((s - .7) / 1.1, 0, 1)
                      + .14 * np.clip((s - 3.72) / 2.3, 0, 1))
    nt = staticmethod(lambda s: 2.15 + .80 * np.clip((s - 1.2) / .9, 0, 1)
                      - .65 * np.clip((s - 3.52) / 1.6, 0, 1))
    nb = staticmethod(lambda s: 2.38)


def build_fuselage():
    mesh = MeshBuilder()
    stations = np.unique(np.concatenate((_SECTIONS[:, 0], np.linspace(Fuselage.S0, Fuselage.S1, 119),
                                         REG_S, [BAG_S - BAG_W / 2, BAG_S + BAG_W / 2])))
    loft = Loft(Fuselage, mesh, stations, P)
    loft.fan_cap(0, P(0, .268, THRUST_Z), s_tip=.268)
    loft.fan_cap(len(stations) - 1, P(0, LENGTH, 1.186), s_tip=LENGTH)
    return mesh


def build_windows(mesh):
    def out(p):
        return Vector((p.x, -.12, p.z - Fuselage.zm(p.y + S_REF)))

    def canopy(s, x):
        return P(x, s, Fuselage.top_z(s, x))

    windshield = [(1.34, .012), (1.43, .29), (1.60, .457), (1.94, .456), (2.01, .012)]
    for sign in (1, -1):
        screen = [(s, sign * x) for s, x in windshield]
        drape_pane(mesh, rounded_outline(screen, [.038, .065, .047, .052, .040]),
                   lambda s, x: _fd_normal(canopy, s, x, out), lift=.009, rings=5)

        def side(s, z):
            return P(sign * Fuselage.side_x(s, z), s, z)

        def skin(s, z):
            return _fd_normal(side, s, z, out)

        door_glass = [(1.975, 1.388), (2.77, 1.406), (2.76, 1.875), (2.13, 1.897), (2.018, 1.786)]
        cabin_glass = [(2.87, 1.417), (3.49, 1.436), (3.45, 1.849), (2.87, 1.873)]
        quarter_glass = [(3.58, 1.446), (4.18, 1.499), (3.89, 1.754), (3.55, 1.835)]
        drape_pane(mesh, rounded_outline(door_glass, [.073, .061, .062, .054, .058]), skin, lift=.009)
        drape_pane(mesh, rounded_outline(cabin_glass, [.050, .052, .053, .048]), skin, lift=.009, rings=5)
        drape_pane(mesh, rounded_outline(quarter_glass, [.049, .066, .055, .058]), skin, lift=.009, rings=5)


WING_AF = "2413"
WING_TIP = 5.10
_WING_PLAN = np.array([
    (0, 2.07, 1.98, .799, 1.20), (.57, 2.12, 1.91, .799, 1.16),
    (.92, 2.19, 1.76, .827, 1.04), (2.75, 2.36, 1.42, .993, .93),
    (4.70, 2.65, .98, 1.166, .85), (4.96, 2.75, .87, 1.183, .79),
    (5.10, 2.89, .57, 1.187, .57),
])
_wing_le, _wing_c, _wing_z, _wing_t = (pchip(_WING_PLAN[:, 0], c) for c in _WING_PLAN.T[1:])
_wing_airfoil = airfoil_eval(WING_AF)


def section(x, s, z, chord, h, thickness=1, up=(0, 0, 1)):
    return dict(le=P(x, s, z), chord_dir=Vector((0, 1, 0)), up=Vector(up),
                c=float(chord), h=h, scale_t=float(thickness))


def wing_station(x, h=.735):
    span = abs(x)
    return section(x, _wing_le(span), _wing_z(span), _wing_c(span), h, _wing_t(span))


def wing_skin(x, fraction, top=True):
    row = wing_station(x, h=None)
    upper, lower, _ = _wing_airfoil(np.array([fraction]))
    height = upper[0] if top else lower[0]
    return row["le"] + row["chord_dir"] * (row["c"] * fraction) + row["up"] * (row["scale_t"] * row["c"] * height)


FIN_TOP = 2.51
BEACON_S = 7.51


def fin_station(z, h=.70):
    t = (z - 1.17) / (FIN_TOP - 1.17)
    lead = 5.94 + 1.26 * t + .68 * t * (1 - t)
    chord = 7.96 - .13 * t - lead
    return section(0, lead, z, chord, h, .97 - .28 * t, (1, 0, 0))


def tail_station(x, h=.66):
    span = abs(x)
    return section(x, 6.76 + .41 * span, 1.26 + .039 * span, 1.21 - .39 * span, h, .92)


def control_panel(station, limits, airfoil, tag, flip=False):
    mesh = MeshBuilder()
    rows = [station(x) for x in np.linspace(*limits, 9)]
    lifting_surface(mesh, rows, airfoil, "Body", tag, part="ctrl", flip=flip, gap=.009)
    hinges = [row["le"] + row["chord_dir"] * row["c"] * row["h"] for row in (rows[0], rows[-1])]
    return mesh, (hinges[0] + hinges[1]) / 2, hinges[1] - hinges[0]


def build_surfaces(body):
    movers = []
    lifting_surface(body, [wing_station(x) for x in np.linspace(-4.86, 4.86, 47)],
                    WING_AF, "Body", "wing", flip=True, gap=.009)
    lifting_surface(body, [tail_station(x) for x in np.linspace(-1.51, 1.51, 21)],
                    "0010", "Body", "htail", flip=True, gap=.009)
    lifting_surface(body, [fin_station(z) for z in np.linspace(1.17, 2.43, 19)],
                    "0012", "Body", "vtail", gap=.009)
    lifting_surface(body, [fin_station(z, h=None) for z in (2.42, 2.48, FIN_TOP)],
                    "0012", "Body", "vtail")

    body.merge(control_panel(wing_station, (-.618, .618), WING_AF, "wing_filler", True)[0])
    body.merge(control_panel(tail_station, (-.15, .15), "0010", "htail", True)[0])
    body.merge(control_panel(fin_station, (1.17, 1.26), "0012", "vtail")[0])
    rudder, pivot, hinge = control_panel(fin_station, (1.27, 2.415), "0012", "rudder")
    movers.append(Mover("Rudder", rudder, pivot, hinge=hinge, axis="z"))

    for sign, side in ((1, "L"), (-1, "R")):
        flip = sign > 0
        lifting_surface(body, [wing_station(sign * x, h=None) for x in (4.85, 4.94, 5.03, WING_TIP)],
                        WING_AF, "Body", "wing", flip=flip)
        lifting_surface(body, [tail_station(sign * x, h=None) for x in (1.50, 1.60, 1.69)],
                        "0010", "Body", "htail", flip=flip)
        for tag, name, limits in (
            ("wing_flap", f"Flap_{side}", (.626, 2.74)),
            ("wing_filler", None, (2.75, 2.82)),
            ("wing_aileron", f"Aileron_{side}", (2.83, 4.845)),
        ):
            mesh, pivot, hinge = control_panel(wing_station, tuple(sign * x for x in limits), WING_AF, tag, flip)
            if name is None:
                body.merge(mesh)
            else:
                movers.append(Mover(name, mesh, pivot, hinge=hinge, axis="x"))
        mesh, pivot, hinge = control_panel(tail_station, (sign * .16, sign * 1.496), "0010", "elevator", flip)
        movers.append(Mover(f"Elevator_{side}", mesh, pivot, hinge=hinge, axis="x"))
    return movers


def build_propeller():
    propeller = MeshBuilder()
    hub = P(0, .20, THRUST_Z)
    revolve(propeller, [(0, .001), (.033, .067), (.091, .113), (.161, .142), (.239, .162), (.282, .163)],
            P(0, 0, THRUST_Z), (0, 1, 0), "Body", "spinner", segs=48, cap_start=True, cap_end=True)
    for sign in (-1, 1):
        forward = Vector((0, -1, 0))
        tangential = Vector((0, 0, sign))
        rows = []
        for radius, chord, pitch, sweep in ((.13, .080, 38, 0), (.22, .119, 33, 0), (.37, .147, 25, .006),
                                             (.55, .138, 19, .015), (.70, .120, 15, .033),
                                             (.823, .094, 12, .046), (.894, .070, 10, .068), (.918, .024, 9, .085)):
            beta = math.radians(pitch)
            chord_dir = -tangential * math.cos(beta) - forward * math.sin(beta)
            up = forward * math.cos(beta) - tangential * math.sin(beta)
            le = hub + Vector((sign * radius, 0, 0)) - tangential * sweep - chord_dir * (.32 * chord)
            rows.append(dict(le=le, chord_dir=chord_dir, up=up, c=chord, h=None,
                             scale_t=1.55 if radius <= .22 else 1))
        lifting_surface(propeller, rows[:-2], "3409", "PropBlade", "blade", closed_ends=False, te_thick=.0015)
        lifting_surface(propeller, rows[-3:], "3409", "PropTip", "blade_tip", te_thick=.0015)
    return propeller, hub


NOSE_S, NOSE_R, NOSE_WIDTH = .92, .182, .124
MAIN_X, MAIN_R, MAIN_WIDTH = 1.49, .237, .154


def build_gear(body):
    nose = MeshBuilder()
    tube(nose, [P(0, NOSE_S + .055, .79), P(0, NOSE_S + .018, .46), P(0, NOSE_S, NOSE_R)],
         lambda t: .040 - .009 * t, "Metal", "strut", ref=(1, 0, 0))
    tube(nose, [P(0, NOSE_S + .07, .55), P(0, NOSE_S + .128, .43), P(0, NOSE_S + .06, .31)],
         lambda t: .014, "DarkMetal", "torque_link", segs=10)
    specs = [("Wheel_Nose", P(0, NOSE_S, NOSE_R), NOSE_R, NOSE_WIDTH, nose, True)]
    for sign, side in ((1, "L"), (-1, "R")):
        x = sign * MAIN_X
        tyre = P(x, S_REF, MAIN_R)
        tube(body, [P(x - sign * .12, S_REF - .12, .857), P(x - sign * .068, S_REF - .038, .42), tyre],
             lambda t: .060 - .021 * t, "Metal", "strut", ref=(1, 0, 0))
        specs.append((f"Wheel_{side}", tyre, MAIN_R, MAIN_WIDTH, body, False))

    movers = [Mover("NoseGear", nose, P(0, NOSE_S, NOSE_R))]
    for name, centre, radius, width, gear_part, steers in specs:
        tire = MeshBuilder()
        wheel(tire, centre, radius, width, name)
        # Short upper fairings leave the lower tyres exposed for grass strips.
        cover = centre + Vector((0, 0, radius * .56))
        outline = [(-1.76, .035), (-1.40, .48), (-.93, .68), (-.30, .74), (.47, .71),
                   (1.12, .54), (1.67, .28), (2.10, .034)]
        revolve(gear_part, [(radius * s, radius * r) for s, r in outline], cover, (0, 1, 0),
                "Body", "pant", segs=28, cap_start=True, cap_end=True)
        movers.append(Mover(name, tire, centre, parent="NoseGear" if steers else None))
    return movers


def build_details(body):
    for sign, lens in ((1, "LightRed"), (-1, "LightGreen")):
        # Dark inlets sit on the rounded face ahead of the engine cowl.
        inlet = [(0, .004), (.005, .031), (.012, .047), (.024, .050)]
        revolve(body, inlet, P(sign * .238, .281, 1.194), (0, 1, 0),
                "Dark", "inlet", segs=24, cap_start=True)
        cap = wing_skin(sign * 1.32, .31)
        revolve(body, [(-.008, .032), (.005, .031), (.011, .025), (.014, .003)], cap, (0, 0, 1),
                "Metal", "fuel_cap", segs=16, cap_end=True)
        tip = wing_skin(sign * 5.085, .27)
        revolve(body, [(-.040, .003), (-.024, .024), (.030, .024), (.047, .002)], tip, (0, 1, 0),
                lens, "light", segs=16, cap_start=True, cap_end=True)

    for x in (1.06, -1.06):
        row = wing_station(x, h=None)
        leading = row["le"] + Vector((0, .004, .027))
        revolve(body, [(-.003, .003), (.002, .029), (.034, .031)], leading, (0, 1, 0),
                "LightLens", "light", segs=16, cap_start=True)
    tube(body, [P(-.25, 1.12, .805), P(-.27, 1.22, .733), P(-.28, 1.37, .685)],
         lambda t: .024, "DarkMetal", "exhaust", segs=12)
    tube(body, [P(0, 3.77, 1.89), P(0, 3.93, 2.15), P(0, 4.01, 2.20)],
         lambda t: .009 - .005 * t, "Dark", "antenna", segs=8)
    revolve(body, [(-.016, .025), (.016, .023), (.035, .012), (.039, .003)],
            P(0, BEACON_S, FIN_TOP), (0, 0, 1), "LightRed", "light", segs=16, cap_end=True)
