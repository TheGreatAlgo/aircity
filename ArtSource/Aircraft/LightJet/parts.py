"""Original compact twinjet geometry. Stations are metres aft of the nose."""

import math

import numpy as np
from mathutils import Vector

from fuselage import Fuselage as BaseFuselage, Loft
from geom import MeshBuilder, pchip
from shapes import _fd_normal, drape_pane, lifting_surface, revolve, rounded_outline, tube, wheel


LENGTH = 14.0
S_REF = 7.45
DOOR_S, DOOR_SILL, DOOR_W, DOOR_H = 3.18, 1.23, 0.70, 1.25
BAG_S, BAG_SILL, BAG_W, BAG_H = 8.46, 1.34, 0.66, 0.62


def Y(s):
    return s - S_REF


def P(x, s, z):
    return Vector((x, Y(s), z))


# A pointed radome, a near-constant cabin, then a raised tailcone below the low stabilizer.
_SECTIONS = np.array([
    (0.04, 0.035, 1.515, 1.592), (0.23, 0.155, 1.45, 1.70), (0.55, 0.34, 1.35, 1.92),
    (1.05, 0.56, 1.22, 2.24), (1.70, 0.77, 1.10, 2.65), (2.38, 0.93, 1.02, 2.96),
    (3.05, 0.98, 0.98, 3.02), (5.60, 0.98, 0.97, 3.02), (7.55, 0.96, 0.99, 3.00),
    (8.70, 0.89, 1.04, 2.91), (9.65, 0.76, 1.14, 2.72), (10.82, 0.58, 1.30, 2.48),
    (12.08, 0.38, 1.53, 2.22), (13.35, 0.17, 1.74, 2.02), (13.95, 0.035, 1.84, 1.91),
])


class Fuselage(BaseFuselage):
    S0, S1 = _SECTIONS[0, 0], _SECTIONS[-1, 0]
    hw, zb, zt = (staticmethod(pchip(_SECTIONS[:, 0], _SECTIONS[:, i])) for i in (1, 2, 3))
    fm = staticmethod(lambda s: 0.48 + 0.04 * np.clip((s - 1.0) / 2.0, 0, 1))
    nt = staticmethod(lambda s: 2.0 + 0.22 * np.clip((s - 1.2) / 1.6, 0, 1))
    nb = staticmethod(lambda s: 2.08)


def build_fuselage():
    body = MeshBuilder()
    stations = np.unique(np.concatenate((_SECTIONS[:, 0], np.linspace(Fuselage.S0, Fuselage.S1, 142),
                                         [7.01, 8.16, BAG_S - BAG_W / 2, BAG_S + BAG_W / 2])))
    shell = Loft(Fuselage, body, stations, P)
    shell.fan_cap(0, P(0, 0, 1.55), s_tip=0)
    shell.fan_cap(len(stations) - 1, P(0, LENGTH, 1.875), s_tip=LENGTH)
    return body


def build_windows():
    windows = MeshBuilder()
    angle = np.linspace(0, 2 * math.pi, 40, endpoint=False)
    for side in (1, -1):
        def screen(s, azimuth):
            x, z = Fuselage.ring_point(s, side * azimuth)
            return P(x, s, z)

        def screen_surface(s, azimuth):
            return _fd_normal(screen, s, azimuth, lambda p: Vector((p.x, -0.10, p.z - 1.82)))

        # Two faceted panes per pilot, with a centre post and a thin swept corner post.
        for corners in (((1.15, 0.055), (1.31, 0.87), (1.97, 0.76), (2.04, 0.055)),
                        ((1.38, 0.92), (1.66, 1.18), (2.71, 1.15), (2.52, 0.77), (2.04, 0.79))):
            drape_pane(windows, rounded_outline(corners, 0.09), screen_surface, lift=0.010)

        def cabin(s, z):
            return P(side * Fuselage.side_x(s, z), s, z)

        def cabin_surface(s, z):
            return _fd_normal(cabin, s, z, lambda p: Vector((side, 0, 0)))

        for station in (4.05, 4.90, 5.75, 6.60):
            oval = np.stack((station + 0.225 * np.cos(angle), 2.27 + 0.252 * np.sin(angle)), axis=1)
            drape_pane(windows, oval, cabin_surface, lift=0.010, rings=4)
    return windows


WING_AF = "2412"
WING_SIDE, WING_TIP = 0.90, 6.50
_WING_PLAN = np.array([
    (0.00, 4.73, 3.37, 1.10, 1.00), (0.95, 4.98, 3.14, 1.10, 1.00),
    (2.15, 5.55, 2.70, 1.16, 0.95), (4.55, 6.52, 1.83, 1.32, 0.84),
    (6.19, 7.24, 1.07, 1.49, 0.72), (6.40, 7.37, 0.82, 1.54, 0.59),
    (6.50, 7.54, 0.51, 1.59, 0.33),
])
_wing_le, _wing_chord, _wing_z, _wing_thickness = [pchip(_WING_PLAN[:, 0], column) for column in _WING_PLAN.T[1:]]


def _section(x, s, z, chord, h, thickness=1.0, vertical=False):
    return dict(le=P(x, s, z), chord_dir=Vector((0, 1, 0)), up=Vector((1, 0, 0) if vertical else (0, 0, 1)),
                c=float(chord), h=h, scale_t=float(thickness))


def wing_station(x, h=0.71):
    span = abs(x)
    return _section(x, _wing_le(span), _wing_z(span), _wing_chord(span), h, _wing_thickness(span))


def horizontal_station(x, h=0.70):
    span = abs(x)
    return _section(x, 11.16 + 0.49 * span, 2.13 + 0.047 * span, 2.33 - 0.69 * span, h)


def fin_station(z, h=0.69):
    t = (z - 1.91) / (3.92 - 1.91)
    return _section(0, 10.47 + 1.78 * t, z, 3.10 - 2.02 * t, h, 1.0 - 0.38 * t, vertical=True)


def _hinge(station):
    return station["le"] + station["chord_dir"] * station["c"] * station["h"]


def _control(body, station, code, tag, bounds, flip):
    start, end = bounds
    mesh = MeshBuilder()
    lifting_surface(mesh, [station(x) for x in np.linspace(start, end, 9)], code, "Body", tag,
                    part="ctrl", flip=flip, gap=0.013)
    a, b = _hinge(station(start)), _hinge(station(end))
    if body is not None:
        body.merge(mesh)
    return mesh, (a + b) / 2, b - a


def build_surfaces(body):
    """Fixed panels and the separately hinged controls, from the same airfoil sections."""
    controls = []
    lifting_surface(body, [wing_station(x) for x in np.linspace(-6.19, 6.19, 45)],
                    WING_AF, "Body", "wing", flip=True, gap=0.013)
    lifting_surface(body, [horizontal_station(x) for x in np.linspace(-2.31, 2.31, 17)],
                    "0011", "Body", "htail", flip=True, gap=0.013)
    for sign, side in ((1, "L"), (-1, "R")):
        flip = sign > 0
        lifting_surface(body, [wing_station(sign * x, h=None) for x in (6.17, 6.25, 6.40, WING_TIP)],
                        WING_AF, "Body", "wing", flip=flip)
        lifting_surface(body, [horizontal_station(sign * x, h=None) for x in (2.29, 2.43, 2.52)],
                        "0011", "Body", "htail", flip=flip)
        for tag, bounds, stationary in (("wing_filler", (0, 1.02), True), ("wing_flap", (1.03, 3.99), False),
                                         ("wing_filler", (4.00, 4.10), True), ("wing_aileron", (4.11, 6.18), False)):
            name = "Flap" if tag == "wing_flap" else "Aileron"
            control = _control(body if stationary else None, wing_station, WING_AF, tag,
                               tuple(sign * x for x in bounds), flip)
            if not stationary:
                controls.append((f"{name}_{side}", *control, "x"))
        _control(body, horizontal_station, "0011", "htail", (0, sign * 0.53), flip)
        controls.append((f"Elevator_{side}", *_control(None, horizontal_station, "0011", "elevator",
                                                        (sign * 0.54, sign * 2.30), flip), "x"))

    lifting_surface(body, [fin_station(z) for z in np.linspace(1.91, 3.79, 15)], "0012", "Body", "vtail", gap=0.013)
    lifting_surface(body, [fin_station(z, h=None) for z in (3.78, 3.88, 3.92)], "0012", "Body", "vtail")
    _control(body, fin_station, "0012", "vtail", (1.91, 2.22), False)
    controls.append(("Rudder", *_control(None, fin_station, "0012", "rudder", (2.23, 3.78), False), "z"))
    return controls


ENGINE_X, ENGINE_S, ENGINE_Z = 1.43, 9.02, 2.40


def build_engine(body, sign):
    origin = P(sign * ENGINE_X, ENGINE_S, ENGINE_Z)
    axis = (0, 1, 0)
    revolve(body, [(0.06, .401), (.16, .435), (.44, .451), (.90, .436), (1.38, .390),
                   (1.75, .327), (1.96, .270)], origin, axis, "Body", "nacelle", segs=40)
    revolve(body, [(0.06, .401), (.019, .394), (0, .376), (.018, .352), (.079, .339)],
            origin, axis, "Chrome", "intake", segs=40, flip=True)
    revolve(body, [(.079, .339), (.18, .334), (.37, .330)], origin, axis, "DarkMetal", "intake", segs=40, flip=True)
    revolve(body, [(.37, .330), (.38, .025)], origin, axis, "Dark", "intake", segs=40, flip=True, cap_end=True)
    revolve(body, [(1.96, .270), (2.04, .255), (2.06, .238), (1.92, .222), (1.85, .215)],
            origin, axis, "DarkMetal", "exhaust", segs=40)
    revolve(body, [(1.85, .215), (1.85, .08), (2.09, .065), (2.17, .012)],
            origin, axis, "DarkMetal", "exhaust", segs=40, cap_end=True)
    pylon = [_section(sign * x, s, z, c, None, thick) for x, s, z, c, thick in (
        (.64, 9.66, 2.23, 1.72, 1.55), (.94, 9.60, 2.26, 1.49, 1.30), (1.32, 9.57, 2.32, 1.24, 0.94))]
    lifting_surface(body, pylon, "0012", "Body", "pylon", flip=(sign > 0))

    hub = origin + Vector((0, .32, 0))
    fan = MeshBuilder()
    revolve(fan, [(-.16, .012), (-.13, .038), (-.07, .068), (.015, .078)], hub, axis,
            "Metal", "spinner", segs=24, cap_start=True, cap_end=True)
    for i in range(18):
        angle = 2 * math.pi * i / 18
        radial = Vector((math.cos(angle), 0, math.sin(angle)))
        tangent = Vector((-radial.z, 0, radial.x))
        rows = []
        for radius, sweep, depth, width in ((.071, 0, -.013, .013), (.19, .006, 0, .026), (.325, .040, .028, .031)):
            centre = hub + radial * radius + tangent * sweep + Vector((0, depth, 0))
            chord = tangent * width + Vector((0, -width * .55, 0))
            thickness = Vector((0, .004, 0))
            rows.append(fan.add_verts([centre - chord - thickness, centre + chord - thickness,
                                       centre + chord + thickness, centre - chord + thickness]))
        for a, b in zip(rows, rows[1:]):
            for j in range(4):
                k = (j + 1) % 4
                fan.add_face([a + j, b + j, b + k, a + k], "DarkMetal", None, "blade")
        fan.add_face([rows[-1] + j for j in range(3, -1, -1)], "DarkMetal", None, "blade")
    return fan, hub


NOSE_S, NOSE_R = 2.33, 0.185
MAIN_X, MAIN_R = 1.53, 0.265


def build_gear(body):
    nose = MeshBuilder()
    tube(nose, [P(0, NOSE_S + .09, 1.12), P(0, NOSE_S + .03, .48), P(0, NOSE_S, NOSE_R)],
         lambda t: .066 - .019 * t, "Metal", "strut")
    specs = [("Wheel_Nose", P(0, NOSE_S, NOSE_R), NOSE_R, .12, True)]
    for sign, side in ((1, "L"), (-1, "R")):
        axle = P(sign * MAIN_X, S_REF, MAIN_R)
        tube(body, [P(sign * 1.09, S_REF - .18, 1.16), P(sign * 1.46, S_REF - .03, .52), axle],
             lambda t: .092 - .026 * t, "Metal", "strut")
        tube(body, [P(sign * 1.28, S_REF - .62, 1.10), P(sign * 1.43, S_REF - .04, .58)],
             lambda t: .039, "DarkMetal", "brace", segs=8)
        specs.append((f"Wheel_{side}", axle, MAIN_R, .18, False))
    wheels = []
    for name, axle, radius, width, steerable in specs:
        mesh = MeshBuilder()
        wheel(mesh, axle, radius, width, name)
        wheels.append((name, mesh, axle, steerable))
    return nose, wheels
