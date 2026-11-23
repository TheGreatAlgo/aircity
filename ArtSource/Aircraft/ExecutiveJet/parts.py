"""Starling B8 geometry. Stations are metres aft of the nose."""

import math

import numpy as np
from mathutils import Matrix, Vector

from fuselage import Fuselage as BaseFuselage, Loft
from geom import MeshBuilder, pchip
from pipeline import Mover
from shapes import _fd_normal, drape_pane, lifting_surface, revolve, rounded_outline, tube, wheel


LENGTH = 20.0
S_REF = 11.42
DOOR_S, DOOR_SILL, DOOR_W, DOOR_H = 4.39, 1.52, .86, 1.62
BAG_S, BAG_SILL, BAG_W, BAG_H = 11.90, 1.79, .85, .73
REG_S = (10.92, 12.57)


def P(x, s, z):
    return Vector((x, s - S_REF, z))


# The rounded radome runs into a distinct windshield rake and a broad, shallow cabin crown.
_SECTIONS = np.array([
    (.035, .126, 2.13, 2.40), (.11, .228, 2.00, 2.52), (.29, .374, 1.82, 2.66),
    (.62, .527, 1.65, 2.80), (1.04, .672, 1.52, 2.91), (1.45, .785, 1.42, 3.04),
    (1.88, .895, 1.34, 3.24), (2.29, .980, 1.29, 3.48), (2.67, 1.042, 1.26, 3.64),
    (3.15, 1.101, 1.23, 3.72),
    (3.85, 1.155, 1.19, 3.76), (6.20, 1.185, 1.16, 3.79), (9.30, 1.185, 1.17, 3.79),
    (11.80, 1.160, 1.19, 3.76), (13.45, 1.085, 1.27, 3.62), (14.85, .890, 1.43, 3.36),
    (16.30, .660, 1.63, 3.04), (17.80, .415, 1.89, 2.74), (19.20, .202, 2.09, 2.49),
    (19.94, .040, 2.245, 2.326),
])


class Fuselage(BaseFuselage):
    S0, S1 = _SECTIONS[0, 0], _SECTIONS[-1, 0]
    hw, zb, zt = (staticmethod(pchip(_SECTIONS[:, 0], _SECTIONS[:, i])) for i in (1, 2, 3))
    fm = staticmethod(lambda s: .49 + .04 * np.clip((s - 1.0) / 3.0, 0, 1))
    nt = staticmethod(lambda s: 2.0 + .18 * np.clip((s - 1.0) / 2.8, 0, 1))
    nb = staticmethod(lambda s: 2.06)


def build_fuselage():
    body = MeshBuilder()
    stations = np.unique(np.concatenate((_SECTIONS[:, 0], np.linspace(Fuselage.S0, Fuselage.S1, 165),
                                         REG_S, [DOOR_S - DOOR_W / 2, DOOR_S + DOOR_W / 2,
                                                 BAG_S - BAG_W / 2, BAG_S + BAG_W / 2])))
    shell = Loft(Fuselage, body, stations, P)
    shell.fan_cap(0, P(0, 0, 2.266), s_tip=0)
    shell.fan_cap(len(stations) - 1, P(0, LENGTH, 2.29), s_tip=LENGTH)
    return body


def build_windows(body):
    angle = np.linspace(0, 2 * math.pi, 40, endpoint=False)
    arc_scale = 1.1

    def at_height(station, height):
        phi = math.atan2(Fuselage.side_x(station, height), height - Fuselage.zm(station))
        return station, arc_scale * phi

    sill = 3.00
    panes = (
        ([(1.41, .035), at_height(1.72, sill), at_height(2.33, sill), at_height(2.65, 3.52), (2.55, .035)],
         [.028, .095, .035, .034, .028]),
        ([at_height(2.42, sill), at_height(3.16, sill), at_height(3.22, 3.49), at_height(2.73, 3.52)],
         [.035, .064, .065, .033]),
        ([at_height(3.25, sill), at_height(3.82, sill + .08), at_height(3.67, 3.45), at_height(3.31, 3.49)],
         [.045, .096, .093, .038]),
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

        for station in (5.58, 6.78, 7.98, 9.18, 10.38):
            oval = np.stack((station + .289 * np.cos(angle), 2.83 + .322 * np.sin(angle)), axis=1)
            drape_pane(body, oval, cabin_surface, lift=.011, rings=4)


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

    for height in np.linspace(.12, DOOR_H - .09, 7):
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
WING_SIDE, WING_TIP = 1.10, 9.00
_WING_PLAN = np.array([
    (0.00, 5.91, 5.47, 1.35, 1.01, 0), (1.12, 6.30, 5.10, 1.35, 1.00, 0),
    (2.25, 7.04, 4.42, 1.43, .96, 0), (4.85, 8.76, 3.07, 1.61, .85, 0),
    (7.52, 10.48, 1.69, 1.81, .72, 0), (8.20, 10.92, 1.30, 1.90, .66, 8),
    (8.56, 11.20, 1.05, 2.04, .58, 37), (8.83, 11.62, .74, 2.34, .51, 65),
    (8.96, 12.15, .48, 2.71, .40, 78), (9.00, 12.40, .26, 2.88, .26, 82),
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


TAIL_Z = 5.94
TAIL_TIP = 3.35


def horizontal_station(x, h=.68):
    span = abs(x)
    return _section(x, 17.34 + .58 * span, TAIL_Z + .028 * span, 2.40 - .52 * span, h, .94)


def fin_station(z, h=.70):
    t = (z - 2.54) / (TAIL_Z - 2.54)
    return _section(0, 13.91 + 3.42 * t, z, 5.18 - 2.78 * t, h, .96 - .28 * t, (1, 0, 0))


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
    lifting_surface(body, [wing_station(x) for x in np.linspace(-8.21, 8.21, 57)],
                    WING_AF, "Body", "wing", flip=True, gap=.017)
    lifting_surface(body, [horizontal_station(x) for x in np.linspace(-3.12, 3.12, 25)],
                    "0011", "Body", "htail", flip=True, gap=.017)
    body.merge(_trailing_panel(wing_station, (-1.27, 1.27), WING_AF, "wing_filler", True)[0])
    body.merge(_trailing_panel(horizontal_station, (-.42, .42), "0011", "htail", True)[0])
    for sign, side in ((1, "L"), (-1, "R")):
        flip = sign > 0
        tip_stations = (8.19, 8.27, 8.43, 8.56, 8.69, 8.83, 8.91, 8.96, WING_TIP)
        lifting_surface(body, [wing_station(sign * x, h=None) for x in tip_stations],
                        WING_AF, "Body", "wing", flip=flip)
        lifting_surface(body, [horizontal_station(sign * x, h=None) for x in (3.10, 3.23, TAIL_TIP)],
                        "0011", "Body", "htail", flip=flip)
        for tag, bounds, name in (("wing_flap", (1.28, 5.22), "Flap"), ("wing_filler", (5.23, 5.39), None),
                                  ("wing_aileron", (5.40, 8.20), "Aileron")):
            panel, pivot, hinge = _trailing_panel(wing_station, tuple(sign * x for x in bounds), WING_AF, tag, flip)
            if name:
                movers.append(Mover(f"{name}_{side}", panel, pivot, hinge=hinge, axis="x"))
            else:
                body.merge(panel)
        panel, pivot, hinge = _trailing_panel(horizontal_station, (sign * .43, sign * 3.11), "0011", "elevator", flip)
        movers.append(Mover(f"Elevator_{side}", panel, pivot, hinge=hinge, axis="x"))

    lifting_surface(body, [fin_station(z) for z in np.linspace(2.54, 5.80, 24)], "0012", "Body", "vtail", gap=.017)
    lifting_surface(body, [fin_station(z, h=None) for z in (5.78, 5.87, TAIL_Z)], "0012", "Body", "vtail")
    body.merge(_trailing_panel(fin_station, (2.54, 3.05), "0012", "vtail")[0])
    panel, pivot, hinge = _trailing_panel(fin_station, (3.06, 5.78), "0012", "rudder")
    movers.append(Mover("Rudder", panel, pivot, hinge=hinge, axis="z"))
    return movers


ENGINE_X, ENGINE_S, ENGINE_Z = 1.96, 13.03, 3.30


def build_engine(body, sign):
    origin = P(sign * ENGINE_X, ENGINE_S, ENGINE_Z)
    axis = (0, 1, 0)
    revolve(body, [(.10, .578), (.28, .626), (.64, .662), (1.12, .658), (1.74, .589),
                   (2.26, .484), (2.71, .364)], origin, axis, "Body", "nacelle", segs=48)
    revolve(body, [(.10, .578), (.027, .568), (0, .538), (.026, .505), (.10, .485)],
            origin, axis, "Chrome", "intake", segs=48, flip=True)
    revolve(body, [(.10, .485), (.24, .471), (.52, .467)],
            origin, axis, "DarkMetal", "intake", segs=48, flip=True)
    revolve(body, [(.52, .467), (.56, .024)], origin, axis, "Dark", "intake",
            segs=48, flip=True, cap_end=True)
    revolve(body, [(2.71, .364), (2.84, .349), (2.87, .332), (2.56, .302), (2.49, .291)],
            origin, axis, "DarkMetal", "exhaust", segs=48)
    revolve(body, [(2.49, .291), (2.49, .12), (2.84, .092), (2.97, .013)],
            origin, axis, "Metal", "exhaust", segs=40, cap_end=True)
    pylon = [_section(sign * x, s, z, c, None, thickness) for x, s, z, c, thickness in (
        (.76, 13.88, 2.99, 2.54, 1.48), (1.19, 13.96, 3.06, 2.31, 1.24), (1.75, 14.12, 3.21, 1.88, .94))]
    lifting_surface(body, pylon, "0012", "Body", "pylon", flip=sign > 0)

    hub = origin + Vector((0, .44, 0))
    fan = MeshBuilder()
    revolve(fan, [(-.22, .012), (-.18, .055), (-.084, .103), (.024, .116)], hub, axis,
            "Metal", "spinner", segs=28, cap_start=True, cap_end=True)
    blade_count = 22
    for i in range(blade_count):
        angle = 2 * math.pi * i / blade_count
        radial = Vector((math.cos(angle), 0, math.sin(angle)))
        tangent = Vector((-radial.z, 0, radial.x))
        rows = []
        for radius, sweep, depth, width in ((.102, 0, -.020, .018), (.275, .015, 0, .040),
                                             (.459, .080, .051, .042)):
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


NOSE_S, NOSE_R, NOSE_WIDTH = 3.15, .238, .15
MAIN_X, MAIN_R, MAIN_WIDTH = 2.05, .355, .22


def build_gear(body):
    nose = MeshBuilder()
    tube(nose, [P(0, NOSE_S + .12, 1.29), P(0, NOSE_S + .04, .63), P(0, NOSE_S, NOSE_R)],
         lambda t: .085 - .028 * t, "Metal", "strut")
    specs = [("Wheel_Nose", P(0, NOSE_S, NOSE_R), NOSE_R, NOSE_WIDTH, True)]
    for sign, side in ((1, "L"), (-1, "R")):
        axle = P(sign * MAIN_X, S_REF, MAIN_R)
        tube(body, [P(sign * 1.60, S_REF - .54, 1.40), P(sign * 1.97, S_REF - .02, .71), axle],
             lambda t: .119 - .035 * t, "Metal", "strut")
        tube(body, [P(sign * 1.66, S_REF - .87, 1.43), P(sign * 1.91, S_REF - .06, .69)],
             lambda t: .05, "DarkMetal", "brace", segs=10)
        specs.append((f"Wheel_{side}", axle, MAIN_R, MAIN_WIDTH, False))
    wheels = []
    for name, axle, radius, width, steerable in specs:
        mesh = MeshBuilder()
        wheel(mesh, axle, radius, width, name)
        wheels.append((name, mesh, axle, steerable))
    return nose, wheels
