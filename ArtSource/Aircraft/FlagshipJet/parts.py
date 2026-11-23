"""Starling B12 geometry. Stations are metres aft of the nose."""

import math

import numpy as np
from mathutils import Matrix, Vector

from fuselage import Fuselage as BaseFuselage, Loft
from geom import MeshBuilder, pchip
from pipeline import Mover
from shapes import _fd_normal, drape_pane, lifting_surface, revolve, rounded_outline, tube, wheel


LENGTH = 26.0
S_REF = 14.62
DOOR_S, DOOR_SILL, DOOR_W, DOOR_H = 4.88, 1.64, .98, 1.94
BAG_S, BAG_SILL, BAG_W, BAG_H = 15.82, 1.84, 1.04, .86
REG_S = (14.23, 15.23)


def P(x, s, z):
    return Vector((x, s - S_REF, z))


# The radome tapers down toward the tip and blends into the raked windshield.
_SECTIONS = np.array([
    (.028, .056, 2.15, 2.25), (.12, .154, 2.08, 2.35), (.30, .276, 1.98, 2.53),
    (.63, .428, 1.88, 2.75), (1.06, .593, 1.78, 3.00), (1.54, .746, 1.68, 3.27),
    (2.00, .885, 1.62, 3.57), (2.42, .999, 1.58, 3.88), (2.82, 1.088, 1.54, 4.09),
    (3.27, 1.166, 1.49, 4.24), (3.90, 1.245, 1.43, 4.33),
    (5.30, 1.316, 1.37, 4.37), (7.30, 1.340, 1.36, 4.39), (12.95, 1.340, 1.36, 4.39),
    (15.30, 1.323, 1.38, 4.37), (16.88, 1.253, 1.44, 4.25), (18.35, 1.078, 1.58, 4.00),
    (20.40, .806, 1.81, 3.64), (22.35, .526, 2.09, 3.23), (24.00, .298, 2.33, 2.94),
    (25.28, .151, 2.48, 2.77), (25.95, .035, 2.56, 2.64),
])


class Fuselage(BaseFuselage):
    S0, S1 = _SECTIONS[0, 0], _SECTIONS[-1, 0]
    hw, zb, zt = (staticmethod(pchip(_SECTIONS[:, 0], _SECTIONS[:, i])) for i in (1, 2, 3))
    fm = staticmethod(lambda s: .49 + .04 * np.clip((s - 1.0) / 3.0, 0, 1))
    nt = staticmethod(lambda s: 2.0 + .18 * np.clip((s - 1.0) / 2.8, 0, 1))
    nb = staticmethod(lambda s: 2.06)


def build_fuselage():
    body = MeshBuilder()
    stations = np.unique(np.concatenate((_SECTIONS[:, 0], np.linspace(Fuselage.S0, Fuselage.S1, 197),
                                         REG_S, [DOOR_S - DOOR_W / 2, DOOR_S + DOOR_W / 2,
                                                 BAG_S - BAG_W / 2, BAG_S + BAG_W / 2])))
    shell = Loft(Fuselage, body, stations, P)
    shell.fan_cap(0, P(0, 0, 2.20), s_tip=0)
    shell.fan_cap(len(stations) - 1, P(0, LENGTH, 2.60), s_tip=LENGTH)
    return body


def build_windows(body):
    angle = np.linspace(0, 2 * math.pi, 40, endpoint=False)
    arc_scale = 1.1

    def at_height(station, height):
        phi = math.atan2(Fuselage.side_x(station, height), height - Fuselage.zm(station))
        return station, arc_scale * phi

    sill = 3.49
    panes = (
        ([(1.83, .028), at_height(2.18, sill), at_height(2.90, sill - .06), at_height(3.10, 4.08), (2.85, .028)],
         [.025, .080, .034, .031, .027]),
        ([at_height(3.00, sill - .06), at_height(3.69, sill - .06), at_height(3.70, 4.00), at_height(3.20, 4.07)],
         [.035, .063, .063, .035]),
        ([at_height(3.78, sill - .06), at_height(4.26, sill + .035), at_height(4.07, 3.95), at_height(3.79, 4.00)],
         [.045, .075, .068, .038]),
    )
    for side in (1, -1):
        def screen(s, arc):
            x, z = Fuselage.ring_point(s, side * arc / arc_scale)
            return P(x, s, z)

        def screen_surface(s, arc):
            return _fd_normal(screen, s, arc, lambda p: Vector((p.x, -.2, p.z - Fuselage.zm(s))))

        # Six panes share a beltline. A narrow centre post and swept corner posts frame the windshields.
        for outline, radii in panes:
            drape_pane(body, rounded_outline(outline, radii), screen_surface, lift=.008)

        def cabin(s, z):
            return P(side * Fuselage.side_x(s, z), s, z)

        def cabin_surface(s, z):
            return _fd_normal(cabin, s, z, lambda p: Vector((side, 0, 0)))

        for station in (6.10, 7.30, 8.50, 9.80, 11.10, 12.30, 13.50):
            # A lightly squared ellipse keeps the glazing broad and low along the cabin.
            u, v = np.cos(angle), np.sin(angle)
            pane = np.stack((station + .351 * np.sign(u) * np.abs(u)**.86,
                             3.23 + .237 * np.sign(v) * np.abs(v)**.86), axis=1)
            drape_pane(body, pane, cabin_surface, lift=.011, rings=4)


def airstair_faces(pivot, angle):
    """Model level treads in the deployed pose and fold them back into the closed door."""
    pivot = Vector(pivot)
    deploy = Matrix.Rotation(math.radians(angle), 3, "Y")
    stow = deploy.transposed()

    def box(centre, size):
        x, y, z = centre
        dx, dy, dz = (a / 2 for a in size)
        return [pivot + stow @ (Vector((x + ix * dx, y + iy * dy, z + iz * dz)) - pivot)
                for iz in (-1, 1) for iy in (-1, 1) for ix in (-1, 1)]

    for height in np.linspace(.12, DOOR_H - .11, 8):
        z = DOOR_SILL + height
        tread = P(Fuselage.side_x(DOOR_S, z) - .158, DOOR_S, z)
        tread = pivot + deploy @ (tread - pivot)
        boxes = (
            (box(tread + Vector((-.066, 0, -.043)), (.13, DOOR_W - .23, .068)), "DarkMetal"),
            (box(tread, (.222, DOOR_W - .14, .044)), "DarkMetal"),
            (box(tread + Vector((.095, 0, .017)), (.032, DOOR_W - .14, .016)), "Metal"),
        )
        # The lower fuselage curls inward. Check the corners across the full width so each
        # folded tread and its mount stay behind the curved door skin.
        clearance = min(Fuselage.side_x(p.y + S_REF, p.z) - p.x for corners, _ in boxes for p in corners)
        inset = Vector((max(0.0, .03 - clearance), 0, 0))
        for corners, material in boxes:
            for face in ((0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4),
                         (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)):
                yield [corners[i] - inset for i in face], material


WING_AF = "2412"
WING_TIP = 11.50
_WING_PLAN = np.array([
    (0.00, 7.30, 6.74, 1.60, 1.03, 0), (1.34, 7.84, 6.17, 1.63, 1.00, 0),
    (2.73, 8.67, 5.42, 1.71, .97, 0), (5.18, 10.20, 4.09, 1.85, .90, 0),
    (8.56, 12.30, 2.36, 2.05, .76, 0), (9.48, 12.86, 1.90, 2.11, .68, 5),
    (10.25, 13.33, 1.53, 2.24, .60, 17), (10.85, 13.87, 1.09, 2.48, .51, 30),
    (11.26, 14.46, .65, 2.79, .42, 42), (11.50, 15.03, .22, 3.06, .33, 49),
])
_wing_le, _wing_chord, _wing_z, _wing_thickness, _wing_cant = [
    pchip(_WING_PLAN[:, 0], column) for column in _WING_PLAN.T[1:]
]


def _section(x, s, z, chord, h, thickness=1, up=(0, 0, 1)):
    return dict(le=P(x, s, z), chord_dir=Vector((0, 1, 0)), up=Vector(up),
                c=float(chord), h=h, scale_t=float(thickness))


def wing_station(x, h=.72):
    span = abs(x)
    cant = math.radians(_wing_cant(span))
    up = (-math.copysign(math.sin(cant), x), 0, math.cos(cant))
    return _section(x, _wing_le(span), _wing_z(span), _wing_chord(span), h, _wing_thickness(span), up)


TAIL_Z = 6.73
TAIL_TIP = 4.28


def horizontal_station(x, h=.68):
    span = abs(x)
    chord = 3.22 - .58 * span - .095 * max(span - 3.1, 0)**2
    return _section(x, 21.72 + .54 * span, TAIL_Z + .032 * span, chord, h, .90)


def fin_station(z, h=.70):
    t = (z - 3.05) / (TAIL_Z - 3.05)
    # A low dorsal fillet blends the swept fin into the tapered tailcone.
    lead = 17.15 + 4.57 * t + 3.05 * t * (1.0 - t)
    return _section(0, lead, z, 7.22 + .57 * t - (lead - 17.15), h,
                    .78 - .16 * t, (1, 0, 0))


def _hinge(station):
    return station["le"] + station["chord_dir"] * station["c"] * station["h"]


def _trailing_panel(station, bounds, airfoil, tag, flip=False):
    panel = MeshBuilder()
    lifting_surface(panel, [station(x) for x in np.linspace(*bounds, 10)], airfoil, "Body", tag,
                    part="ctrl", flip=flip, gap=.017)
    start, end = (_hinge(station(x)) for x in bounds)
    return panel, (start + end) / 2, end - start


def build_surfaces(body):
    movers = []
    lifting_surface(body, [wing_station(x) for x in np.linspace(-9.48, 9.48, 63)],
                    WING_AF, "Body", "wing", flip=True, gap=.017)
    lifting_surface(body, [horizontal_station(x) for x in np.linspace(-3.96, 3.96, 27)],
                    "0011", "Body", "htail", flip=True, gap=.017)
    body.merge(_trailing_panel(wing_station, (-1.48, 1.48), WING_AF, "wing_filler", True)[0])
    body.merge(_trailing_panel(horizontal_station, (-.50, .50), "0011", "htail", True)[0])
    for sign, side in ((1, "L"), (-1, "R")):
        flip = sign > 0
        tip_stations = (9.45, 9.66, 9.87, 10.25, 10.55, 10.85, 11.08, 11.26, 11.40, WING_TIP)
        lifting_surface(body, [wing_station(sign * x, h=None) for x in tip_stations],
                        WING_AF, "Body", "wing", flip=flip)
        lifting_surface(body, [horizontal_station(sign * x, h=None) for x in (3.94, 4.10, TAIL_TIP)],
                        "0011", "Body", "htail", flip=flip)
        for tag, bounds, name in (("wing_flap", (1.49, 6.53), "Flap"), ("wing_filler", (6.54, 6.74), None),
                                  ("wing_aileron", (6.75, 9.47), "Aileron")):
            panel, pivot, hinge = _trailing_panel(wing_station, tuple(sign * x for x in bounds), WING_AF, tag, flip)
            if name:
                movers.append(Mover(f"{name}_{side}", panel, pivot, hinge=hinge, axis="x"))
            else:
                body.merge(panel)
        panel, pivot, hinge = _trailing_panel(horizontal_station, (sign * .51, sign * 3.95), "0011", "elevator", flip)
        movers.append(Mover(f"Elevator_{side}", panel, pivot, hinge=hinge, axis="x"))

    lifting_surface(body, [fin_station(z) for z in np.linspace(3.05, 6.56, 28)], "0012", "Body", "vtail", gap=.017)
    lifting_surface(body, [fin_station(z, h=None) for z in (6.53, 6.63, TAIL_Z)], "0012", "Body", "vtail")
    body.merge(_trailing_panel(fin_station, (3.05, 3.78), "0012", "vtail")[0])
    panel, pivot, hinge = _trailing_panel(fin_station, (3.79, 6.53), "0012", "rudder")
    movers.append(Mover("Rudder", panel, pivot, hinge=hinge, axis="z"))
    return movers


ENGINE_X, ENGINE_S, ENGINE_Z = 2.37, 17.40, 3.77


def build_engine(body, sign):
    origin = P(sign * ENGINE_X, ENGINE_S, ENGINE_Z)
    axis = (0, 1, 0)
    revolve(body, [(.116, .719), (.31, .776), (.78, .809), (1.42, .799),
                   (2.12, .718), (2.78, .575), (3.30, .432)],
            origin, axis, "Body", "nacelle", segs=56)
    revolve(body, [(.116, .719), (.036, .702), (0, .668), (.034, .628), (.13, .605)],
            origin, axis, "Chrome", "intake", segs=56, flip=True)
    revolve(body, [(.13, .605), (.32, .587), (.64, .582)],
            origin, axis, "DarkMetal", "intake", segs=56, flip=True)
    revolve(body, [(.64, .582), (.69, .026)], origin, axis, "Dark", "intake",
            segs=56, flip=True, cap_end=True)
    revolve(body, [(3.30, .432), (3.45, .411), (3.49, .390), (3.21, .355), (3.12, .339)],
            origin, axis, "DarkMetal", "exhaust", segs=56)
    revolve(body, [(3.12, .339), (3.12, .153), (3.42, .119), (3.62, .012)],
            origin, axis, "Metal", "exhaust", segs=40, cap_end=True)
    pylon = [_section(sign * x, s, z, c, None, thickness) for x, s, z, c, thickness in (
        (.83, 18.56, 3.38, 3.12, 1.30), (1.30, 18.65, 3.42, 2.83, 1.18), (2.06, 18.98, 3.69, 1.92, .91))]
    lifting_surface(body, pylon, "0012", "Body", "pylon", flip=sign > 0)

    hub = origin + Vector((0, .53, 0))
    fan = MeshBuilder()
    revolve(fan, [(-.26, .014), (-.21, .066), (-.102, .124), (.028, .139)], hub, axis,
            "Metal", "spinner", segs=28, cap_start=True, cap_end=True)
    blade_count = 24
    for i in range(blade_count):
        angle = 2 * math.pi * i / blade_count
        radial = Vector((math.cos(angle), 0, math.sin(angle)))
        tangent = Vector((-radial.z, 0, radial.x))
        rows = []
        for radius, sweep, depth, width in ((.123, 0, -.022, .021), (.330, .026, 0, .045),
                                             (.568, .096, .058, .050)):
            centre = hub + radial * radius + tangent * sweep + Vector((0, depth, 0))
            chord = tangent * width + Vector((0, -width * .52, 0))
            thickness = Vector((0, .006, 0))
            rows.append(fan.add_verts([centre - chord - thickness, centre + chord - thickness,
                                       centre + chord + thickness, centre - chord + thickness]))
        for a, b in zip(rows, rows[1:]):
            for j in range(4):
                k = (j + 1) % 4
                fan.add_face([a + j, b + j, b + k, a + k], "DarkMetal", None, "blade")
        fan.add_face([rows[-1] + j for j in range(3, -1, -1)], "DarkMetal", None, "blade")
    return fan, hub


NOSE_S, NOSE_R = 3.62, .275
MAIN_X, MAIN_R = 2.68, .416
NOSE_TRACK, NOSE_TIRE = .186, .115
MAIN_TRACK, MAIN_TIRE = .296, .222
NOSE_WIDTH = NOSE_TRACK + NOSE_TIRE
MAIN_WIDTH = MAIN_TRACK + MAIN_TIRE


def build_gear(body):
    nose = MeshBuilder()
    tube(nose, [P(0, NOSE_S + .12, 1.55), P(0, NOSE_S + .05, .74), P(0, NOSE_S, NOSE_R)],
         lambda t: .095 - .035 * t, "Metal", "strut", ref=(1, 0, 0))
    tube(nose, [P(0, NOSE_S + .10, 1.31), P(0, NOSE_S + .06, .80)],
         lambda t: .108, "DarkMetal", "oleo", ref=(1, 0, 0))
    tube(nose, [P(0, NOSE_S + .14, .76), P(0, NOSE_S + .24, .60), P(0, NOSE_S + .06, .46)],
         lambda t: .034, "Metal", "torque_link", segs=10)
    specs = [("Wheel_Nose", P(0, NOSE_S, NOSE_R), NOSE_R, NOSE_TRACK, NOSE_TIRE, True)]
    for sign, side in ((1, "L"), (-1, "R")):
        axle = P(sign * MAIN_X, S_REF, MAIN_R)
        tube(body, [P(sign * 2.06, S_REF - 1.38, 1.65), P(sign * 2.59, S_REF - .25, .83), axle],
             lambda t: .132 - .037 * t, "Metal", "strut")
        tube(body, [P(sign * 2.16, S_REF - 1.80, 1.62), P(sign * 2.51, S_REF - .29, .80)],
             lambda t: .05, "DarkMetal", "brace", segs=10)
        specs.append((f"Wheel_{side}", axle, MAIN_R, MAIN_TRACK, MAIN_TIRE, False))
    wheels = []
    for name, axle, radius, track, tire, steerable in specs:
        mesh = MeshBuilder()
        for side in (-1, 1):
            wheel(mesh, axle + Vector((side * track / 2, 0, 0)), radius, tire, name)
        tube(mesh, [axle - Vector((track / 2, 0, 0)), axle + Vector((track / 2, 0, 0))],
             lambda t: radius * .23, "Metal", "axle", segs=12)
        wheels.append((name, mesh, axle, steerable))
    return nose, wheels
