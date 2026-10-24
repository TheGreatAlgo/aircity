"""Runway snow plough: a cab-forward 4x4 airport plough-sweeper, after the multi-tasking runway units airports
run in echelon (Oshkosh HT / M-B MTE style): a 6.1 m one-way front blade angled 30 degrees (it casts snow to the
left), a 6.6 m rotary broom slung between the axles at the same angle, and a high-velocity air-blast nozzle
behind it. Fleet yellow with a white cab roof, amber beacons, high plough lights (the blade hides the bumper
headlights), work lights on the blade ends and the broom, and blade-end marker rods.

Vehicle space: (right, forward, up) metres, origin on the ground under the rear axle.
"""
import math

from kit import M, Part, arc, rounded_rect

NAME = "SnowPlough"
DISPLAY_NAME = "Runway snow plough"
KIND = "SnowPlough"
AO_DISTANCE = 0.8
LOD_RATIOS = (0.3, 0.08)
PREVIEW_GROUND = (0.55, 0.56, 0.58)

MATS = {
    # sRGB, metallic, roughness, lamp
    "Paint": ("#F2A30F", 0.0, 0.38, False),
    "Bristle": ("#23262B", 0.0, 0.95, False),
    "Marker": ("#FF5A0A", 0.0, 0.4, False),
}
GRIME = "#6A6763"          # road salt and slush spray

WHEELBASE = 6.2
TRACK = 2.08
WHEEL_R, WHEEL_W, RIM_R = 0.56, 0.42, 0.29
BLADE_ANGLE = 30.0         # degrees; the right end leads, snow leaves the left end
BLADE_W, BLADE_H = 6.1, 1.3
BLADE_F = 10.05            # blade centre (forward of the rear axle)
BROOM_F, BROOM_R, BROOM_L = 3.15, 0.45, 6.6

WHEELS = [
    ("Wheel_FL", (-TRACK / 2, WHEELBASE, WHEEL_R), WHEEL_R, True),
    ("Wheel_FR", (TRACK / 2, WHEELBASE, WHEEL_R), WHEEL_R, True),
    ("Wheel_RL", (-TRACK / 2, 0.0, WHEEL_R), WHEEL_R, False),
    ("Wheel_RR", (TRACK / 2, 0.0, WHEEL_R), WHEEL_R, False),
]
MOVERS = [w[0] for w in WHEELS] + ["Broom"]


def _broom_axis():
    a = math.radians(BLADE_ANGLE)
    return (math.cos(a), math.sin(a), 0.0)       # right end forward


SPINNERS = [("Broom", _broom_axis(), 380.0)]

# lights: name, type, position, direction, sRGB colour, glow size, phase
_A = math.radians(BLADE_ANGLE)


def _on_blade(x, y):
    """A point in the blade's frame (along the blade, forward of its centre) in vehicle (right, forward)."""
    return (x * math.cos(_A) - y * math.sin(_A), BLADE_F + x * math.sin(_A) + y * math.cos(_A))


_BLADE_LEFT = _on_blade(-BLADE_W / 2, 0.0)
_BLADE_RIGHT = _on_blade(BLADE_W / 2, 0.0)
_ROD_L = _on_blade(-(BLADE_W / 2 - 0.05), -0.32)
_ROD_R = _on_blade(BLADE_W / 2 - 0.05, -0.32)
LIGHTS = [
    ("PloughLight_L", "Head", (-0.98, 8.38, 3.78), (0, 1, -0.06), "#FFF4E0", 1.0, 0.0),
    ("PloughLight_R", "Head", (0.98, 8.38, 3.78), (0, 1, -0.06), "#FFF4E0", 1.0, 0.0),
    ("Headlight_L", "Head", (-0.92, 8.8, 1.12), (0, 1, 0), "#FFF4E0", 0.8, 0.0),
    ("Headlight_R", "Head", (0.92, 8.8, 1.12), (0, 1, 0), "#FFF4E0", 0.8, 0.0),
    ("Tail_L", "Tail", (-1.08, -1.98, 0.95), (0, -1, 0), "#FF1A12", 0.7, 0.0),
    ("Tail_R", "Tail", (1.08, -1.98, 0.95), (0, -1, 0), "#FF1A12", 0.7, 0.0),
    ("TailHigh_L", "Tail", (-0.98, -1.83, 2.42), (0, -1, 0), "#FF1A12", 0.7, 0.0),
    ("TailHigh_R", "Tail", (0.98, -1.83, 2.42), (0, -1, 0), "#FF1A12", 0.7, 0.0),
    ("Beacon_L", "Beacon", (-0.92, 6.32, 3.86), (0, 0, 1), "#FF9A10", 1.15, 0.0),
    ("Beacon_R", "Beacon", (0.92, 6.32, 3.86), (0, 0, 1), "#FF9A10", 1.15, 0.37),
    ("Work_BladeL", "Work", (-1.2, 8.2, 3.7), (-0.55, 0.75, -0.35), "#FFFFFF", 0.85, 0.0),
    ("Work_BladeR", "Work", (1.2, 8.2, 3.7), (0.45, 0.85, -0.3), "#FFFFFF", 0.85, 0.0),
    ("Work_BroomL", "Work", (-1.22, 6.1, 3.55), (-0.5, -0.6, -0.6), "#FFFFFF", 0.85, 0.0),
    ("Work_BroomR", "Work", (1.22, 6.1, 3.55), (0.5, -0.6, -0.6), "#FFFFFF", 0.85, 0.0),
    ("Marker_BladeL", "Marker", (_ROD_L[0], _ROD_L[1], BLADE_H + 1.06), (0, 0, 1), "#FF8A10", 0.55, 0.0),
    ("Marker_BladeR", "Marker", (_ROD_R[0], _ROD_R[1], BLADE_H + 1.06), (0, 0, 1), "#FF8A10", 0.55, 0.0),
]

WORK = {
    "laneWidth": 5.0,            # cleared width per truck: the 6.1 m blade at 30 degrees, with overlap
    "workSpeed": 11.0,           # m/s (40 km/h) ploughing
    "transitSpeed": 13.9,        # m/s (50 km/h) on taxiways
    "turnRadius": 12.0,
    "sprayPoint": (_BLADE_LEFT[0] - 0.3, _BLADE_LEFT[1], 0.5),     # snow leaves the blade's left end
    "sprayDirection": (-0.85, 0.35, 0.4),
    "broomPoint": (-math.cos(_A) * BROOM_L / 2 - 0.4, BROOM_F - math.sin(_A) * BROOM_L / 2, 0.3),
}

VIEWS = {
    "front34": ((-14.0, 24.0, 6.5), (0.0, 5.0, 1.6), 40),
    "front34r": ((15.0, 22.0, 5.5), (0.0, 5.0, 1.6), 40),
    "rear34": ((13.0, -14.0, 6.0), (0.0, 3.5, 1.6), 40),
    "side": ((-34.0, 4.5, 2.2), (0.0, 4.5, 1.8), 50),
    "top": ((0.0, 4.5, 40.0), (0.0, 4.6, 0.0), 40),
    "cab": ((-6.0, 14.0, 4.0), (0.0, 7.5, 2.4), 40),
}


# ------------------------------------------------------------------ geometry

def build():
    parts = []
    body = Part("Body")
    chassis(body)
    cab(body)
    module(body)
    rear(body)
    plough(body)
    air_blast(body)
    lamps(body)
    parts.append(body)

    for name, c, r, steer in WHEELS:
        w = Part(name, pivot=c)
        w.wheel(c, WHEEL_R, WHEEL_W, RIM_R, side=1 if c[0] > 0 else -1)
        parts.append(w)

    parts.append(broom())
    return parts


def chassis(b):
    for s in (-1, 1):
        b.box((s * 0.38, -1.75, 0.95), (s * 0.54, 8.6, 1.22), "Chassis", bevel=0.02)
    # cross members
    for f in (-1.5, 0.9, 5.4, 8.3):
        b.box((-0.54, f - 0.08, 1.0), (0.54, f + 0.08, 1.18), "Chassis")
    # axles and suspension
    for f in (0.0, WHEELBASE):
        b.cyl((-TRACK / 2 + 0.1, f, WHEEL_R), (TRACK / 2 - 0.1, f, WHEEL_R), 0.11, "Chassis", segs=12)
        b.box((-0.25, f - 0.28, WHEEL_R - 0.18), (0.25, f + 0.28, WHEEL_R + 0.18), "Chassis", bevel=0.04)
        for s in (-1, 1):
            # leaf spring packs
            b.box((s * 0.36, f - 0.75, WHEEL_R + 0.12), (s * 0.56, f + 0.75, WHEEL_R + 0.3), "Steel", bevel=0.03)
    # fuel tanks under the cab side, right; battery / air tanks left
    b.cyl((0.6, 4.9, 1.02), (1.22, 4.9, 1.02), 0.32, "Metal", segs=20, bevel=0.03)
    b.box((-1.2, 4.6, 0.72), (-0.6, 5.5, 1.25), "Chassis", bevel=0.04)
    # fenders over all wheels
    for f in (0.0, WHEELBASE):
        for s in (-1, 1):
            fender(b, s * TRACK / 2, f, WHEEL_R, WHEEL_W + 0.12)


def fender(b, r, f, radius, width):
    outer, inner = radius + 0.16, radius + 0.1
    a0, a1 = 20, 160
    prof = [(f + math.cos(math.radians(a)) * outer, WHEEL_R + math.sin(math.radians(a)) * outer) for a in range(a0, a1 + 1, 10)]
    prof += [(f + math.cos(math.radians(a)) * inner, WHEEL_R + math.sin(math.radians(a)) * inner) for a in range(a1, a0 - 1, -10)]
    b.prism(prof, "r", r - width / 2, width, "Plastic")


def cab(b):
    hw = 1.26
    f0, f1 = 6.05, 8.45
    z0, z1 = 1.32, 3.58
    # side profile (forward, up), raked windscreen, rounded roof front
    prof = [(f0, z0), (f1, z0), (f1, 2.32), (f1 - 0.05, 2.45)] + \
        [(p[0], p[1]) for p in arc(f1 - 0.42, z1 - 0.32, 0.32, 10, 90, 5)] + [(f0 + 0.18, z1), (f0, z1 - 0.15)]
    # make the windscreen rake: move the top-front points back
    prof = [(f, z) if z < 2.45 else (f - (z - 2.45) * 0.17, z) for f, z in prof]
    b.prism(prof, "r", -hw, 2 * hw, "Paint", bevel=0.06, segments=2)
    # white roof cap
    b.box((-hw + 0.1, f0 + 0.15, z1 - 0.02), (hw - 0.1, f1 - 0.65, z1 + 0.07), "White", bevel=0.03)
    # windscreen (two panes), side and rear windows
    for s in (-1, 1):
        x0, x1 = (0.04, 1.14) if s > 0 else (-1.14, -0.04)
        b.quad([(x0, f1 - 0.07 + 0.012, 2.52), (x1, f1 - 0.07 + 0.012, 2.52),
                (x1, f1 - 0.25 + 0.012, 3.38), (x0, f1 - 0.25 + 0.012, 3.38)], "Glass")
    # pillar between the panes
    b.box((-0.04, f1 - 0.28, 2.48), (0.04, f1 - 0.02, 3.4), "Dark")
    for s in (-1, 1):
        x = s * (hw + 0.012)
        win = [(x, 6.85, 2.42), (x, 8.18, 2.42), (x, 8.03, 3.32), (x, 6.85, 3.32)]
        b.quad(win if s > 0 else win[::-1], "Glass")
        # door seam, handle, steps
        for (fa, fb, za, zb) in ((6.72, 6.75, 1.42, 3.36), (8.22, 8.25, 1.42, 2.38)):
            b.box((x - s * 0.01, fa, za), (x + s * 0.004, fb, zb), "Dark")
        b.box((x - s * 0.01, 6.72, 1.42), (x + s * 0.004, 8.25, 1.45), "Dark")
        b.box((x, 6.9, 2.18), (x + s * 0.03, 7.15, 2.23), "Chrome")
        for z in (0.62, 1.02):
            b.box((s * 1.0, 6.9, z - 0.04), (s * 1.24, 7.6, z), "Steel")
        b.box((s * 1.18, 6.88, 0.55), (s * 1.24, 6.92, 1.32), "Steel")
        b.box((s * 1.18, 7.58, 0.55), (s * 1.24, 7.62, 1.32), "Steel")
        # mirror arms and mirrors
        b.cyl((s * 1.2, 8.15, 2.55), (s * 1.62, 8.4, 2.6), 0.025, "Dark", segs=8)
        b.cyl((s * 1.2, 8.15, 3.1), (s * 1.62, 8.4, 3.05), 0.025, "Dark", segs=8)
        b.box((s * 1.58, 8.34, 2.35), (s * 1.66 if s > 0 else s * 1.66, 8.5, 3.15), "Plastic", bevel=0.02)
    rear_win = [(-0.9, f0 - 0.012, 2.6), (0.9, f0 - 0.012, 2.6), (0.9, f0 - 0.012, 3.25), (-0.9, f0 - 0.012, 3.25)]
    b.quad(rear_win[::-1], "Glass")
    # grille and bumper
    b.box((-0.85, f1 - 0.02, 1.5), (0.85, f1 + 0.02, 2.2), "Dark", bevel=0.01)
    for k in range(6):
        z = 1.56 + k * 0.11
        b.box((-0.82, f1 + 0.015, z), (0.82, f1 + 0.04, z + 0.035), "Chassis")
    b.box((-1.27, f1, 0.95), (1.27, f1 + 0.35, 1.36), "Chassis", bevel=0.03)
    # windscreen wipers
    for x in (-0.6, 0.55):
        b.cyl((x, f1 - 0.05, 2.56), (x + 0.45, f1 - 0.1, 2.9), 0.012, "Dark", segs=6)


def module(b):
    """Engine and blower housing behind the cab, with louvres, an intake and the exhaust stack."""
    hw = 1.2
    b.box((-hw, -1.8, 1.18), (hw, 5.95, 3.05), "Paint", bevel=0.07, segments=2)
    for s in (-1, 1):
        x = s * (hw + 0.005)
        # louvre panels
        for (fa, fb) in ((-1.3, 0.2), (1.0, 2.6), (3.3, 4.9)):
            b.box((x - s * 0.01, fa, 1.7), (x + s * 0.012, fb, 2.75), "Dark")
            for k in range(8):
                z = 1.76 + k * 0.125
                b.box((x, fa + 0.05, z), (x + s * 0.04, fb - 0.05, z + 0.05), "Paint")
        # access door seams
        for fd in (0.55, 2.85):
            b.box((x - s * 0.005, fd, 1.3), (x + s * 0.01, fd + 0.03, 2.95), "Dark")
        # handrail
        b.cyl((x + s * 0.05, -1.4, 2.9), (x + s * 0.05, 5.6, 2.9), 0.02, "Steel", segs=8)
    # roof intake and exhaust
    b.box((-0.7, 1.4, 3.05), (0.7, 3.0, 3.35), "Dark", bevel=0.04)
    for k in range(7):
        f = 1.5 + k * 0.21
        b.box((-0.66, f, 3.33), (0.66, f + 0.08, 3.37), "Chassis")
    b.cyl((0.85, 5.55, 3.0), (0.85, 5.55, 4.05), 0.075, "Chrome", segs=14)
    b.cyl((0.85, 5.55, 3.25), (0.85, 5.55, 3.7), 0.11, "Steel", segs=14)
    # ladder up the rear face, left
    f = -1.84
    for x in (-1.0, -0.62):
        b.box((x, f - 0.04, 0.55), (x + 0.04, f, 3.0), "Steel")
    for z in (0.7, 1.1, 1.5, 1.9, 2.3, 2.7):
        b.box((-1.0, f - 0.05, z), (-0.58, f - 0.01, z + 0.03), "Steel")


def rear(b):
    # rear bumper with red / white chevrons (as on airside vehicles)
    f = -1.95
    b.box((-1.27, f, 0.72), (1.27, f + 0.3, 1.16), "Chassis", bevel=0.02)
    n = 12
    for k in range(n):
        x0 = -1.2 + k * 2.4 / n
        x1 = x0 + 2.4 / n
        mat = "StripeRed" if k % 2 == 0 else "StripeWhite"
        b.quad([(x0, f - 0.006, 0.77), (x0 + 0.1, f - 0.006, 1.11), (x1 + 0.1, f - 0.006, 1.11), (x1, f - 0.006, 0.77)][::-1],
               mat)
    # chevron band across the top of the rear face too
    for k in range(n):
        x0 = -1.12 + k * 2.24 / n
        x1 = x0 + 2.24 / n
        mat = "StripeRed" if k % 2 == 0 else "StripeWhite"
        b.quad([(x0, -1.806, 2.62), (x0 + 0.1, -1.806, 2.92), (x1 + 0.1, -1.806, 2.92), (x1, -1.806, 2.62)][::-1], mat)
    # mud flaps
    for s in (-1, 1):
        b.box((s * TRACK / 2 - 0.25, -0.75, 0.18), (s * TRACK / 2 + 0.25, -0.72, 0.95), "Dark")
    # rear frame of the module, with the tail lamps' housings
    for s in (-1, 1):
        b.box((s * 0.85, f - 0.02, 0.86), (s * 1.25, f + 0.02, 1.05), "Dark")


def plough(b):
    """Moldboard (curved, concave forward) at BLADE_ANGLE, its push frame, ribs, cutting edge and marker rods."""
    yaw = -BLADE_ANGLE                 # turns the blade's right end forward
    place = M(0.0, BLADE_F, 0.0, yaw=yaw)
    hw = BLADE_W / 2
    # front surface profile (forward, up), local to the blade: cutting edge forward, curling over at the top
    front = [(0.0, 0.06), (-0.1, 0.3), (-0.17, 0.55), (-0.2, 0.8), (-0.17, 1.02), (-0.08, 1.2), (0.06, BLADE_H)]
    back = [(f - 0.07, z) for f, z in front[::-1]]
    b.prism(front + back, "r", -hw, BLADE_W, "Paint", matrix=place)
    # worn steel face strip near the cutting edge, and the cutting edge itself
    b.prism([(0.012, 0.06), (-0.088, 0.3), (-0.1, 0.3), (0.0, 0.06)], "r", -hw + 0.02, BLADE_W - 0.04, "Steel", matrix=place)
    b.box((-hw, -0.09, 0.0), (hw, 0.02, 0.08), "Steel", matrix=place)
    # end plates (shoes)
    for s in (-1, 1):
        b.box((s * hw - 0.03, -0.45, 0.0), (s * hw + 0.03, 0.08, BLADE_H - 0.1), "Paint", bevel=0.01, matrix=place)
        b.box((s * hw - 0.08, -0.5, 0.0), (s * hw + 0.08, -0.15, 0.08), "Steel", matrix=place)
    # vertical ribs and a horizontal beam on the back
    for k in range(9):
        x = -hw + 0.3 + k * (BLADE_W - 0.6) / 8
        b.prism([(-0.27, 0.1), (-0.38, 0.4), (-0.4, 0.85), (-0.3, 1.18), (-0.2, 1.18), (-0.25, 0.8), (-0.24, 0.4), (-0.12, 0.1)],
                "r", x - 0.025, 0.05, "Paint", matrix=place)
    b.box((-hw + 0.2, -0.46, 0.55), (hw - 0.2, -0.28, 0.75), "Paint", bevel=0.02, matrix=place)
    # rubber snow deflector along the top
    b.box((-hw, -0.02, BLADE_H - 0.02), (hw, 0.06, BLADE_H + 0.12), "Dark", matrix=place)
    # marker rods with orange tips at both ends
    for s in (-1, 1):
        rod = M(0, BLADE_F, 0, yaw=yaw)
        b.cyl((s * (hw - 0.05), -0.32, BLADE_H - 0.1), (s * (hw - 0.05), -0.32, BLADE_H + 1.0), 0.016, "Dark", segs=6, matrix=rod)
        b.cyl((s * (hw - 0.05), -0.32, BLADE_H + 0.75), (s * (hw - 0.05), -0.32, BLADE_H + 1.0), 0.022, "Marker", segs=8,
              matrix=rod)
    # push frame: turntable and arms from the truck's front plate to the blade beam
    b.box((-0.75, 8.78, 0.45), (0.75, 8.9, 1.25), "Chassis", bevel=0.02)
    for s in (-1, 1):
        b.cyl((s * 0.55, 8.85, 0.6), (s * 0.35, 9.55, 0.62), 0.07, "Chassis", segs=10)
    b.cyl((0.0, 9.45, 0.55), (0.0, 9.45, 0.82), 0.32, "Chassis", segs=20)
    # angling and lift rams
    for s in (-1, 1):
        b.cyl((s * 0.5, 9.0, 0.95), (s * 1.2, 9.55 + s * 0.35, 0.68), 0.055, "Chassis", segs=10)
        b.cyl((s * 0.95, 9.38 + s * 0.2, 0.75), (s * 1.2, 9.55 + s * 0.35, 0.68), 0.03, "Chrome", segs=8)
    b.cyl((0.0, 8.95, 1.3), (0.0, 9.5, 0.95), 0.06, "Chassis", segs=10)


def broom():
    """The rotary broom: bristle drum on a steel core with end hubs (a mover that spins about its axis), at
    the blade's angle between the axles. Its hood is part of the body."""
    a = math.radians(BLADE_ANGLE)
    c = (0.0, BROOM_F, BROOM_R + 0.01)
    p = Part("Broom", pivot=c)
    ax = (math.cos(a), math.sin(a), 0.0)
    half = BROOM_L / 2
    e0 = (c[0] - ax[0] * half, c[1] - ax[1] * half, c[2])
    e1 = (c[0] + ax[0] * half, c[1] + ax[1] * half, c[2])
    p.cyl(e0, e1, BROOM_R, "Bristle", segs=24)
    # dark wafer bands (so the spin reads up close)
    n = 22
    for k in range(n):
        t = (k + 0.5) / n
        q0 = tuple(e0[i] + (e1[i] - e0[i]) * (t - 0.008) for i in range(3))
        q1 = tuple(e0[i] + (e1[i] - e0[i]) * (t + 0.008) for i in range(3))
        p.cyl(q0, q1, BROOM_R * 0.97 + 0.012, "Dark", segs=24)
    # bristle tufts as raised ridges, a few along the drum
    for k in range(8):
        ang = 2 * math.pi * k / 8
        nrm = (-math.sin(a) * math.cos(ang), math.cos(a) * math.cos(ang), math.sin(ang))
        q0 = tuple(e0[i] + nrm[i] * BROOM_R for i in range(3))
        q1 = tuple(e1[i] + nrm[i] * BROOM_R for i in range(3))
        p.cyl(q0, q1, 0.035, "Bristle", segs=6)
    for e in (e0, e1):
        d = (ax[0] * 0.06, ax[1] * 0.06, 0)
        p.cyl((e[0] - d[0], e[1] - d[1], e[2]), (e[0] + d[0], e[1] + d[1], e[2]), 0.22, "Steel", segs=16)
    return p


def air_blast(b):
    """Broom hood and arms, and the blower duct to a long nozzle behind the broom."""
    a = math.radians(BLADE_ANGLE)
    yaw = -BLADE_ANGLE
    place = M(0.0, BROOM_F, BROOM_R + 0.01, yaw=yaw)
    half = BROOM_L / 2
    rr = BROOM_R + 0.09
    shell = [(math.cos(math.radians(t)) * rr, math.sin(math.radians(t)) * rr) for t in range(40, 201, 16)]
    shell += [(math.cos(math.radians(t)) * (rr + 0.04), math.sin(math.radians(t)) * (rr + 0.04)) for t in range(200, 39, -16)]
    # hood: a half shell over the drum (profile in (forward, up) about the drum axis), extruded along the drum
    b.prism(shell, "r", -half - 0.05, BROOM_L + 0.1, "Paint", matrix=place)
    for s in (-1, 1):
        b.prism([(0.0, 0.0)] + [(math.cos(math.radians(t)) * (rr + 0.04), math.sin(math.radians(t)) * (rr + 0.04))
                                for t in range(40, 201, 16)],
                "r", s * (half + 0.05) - (0.02 if s > 0 else 0.0), 0.02, "Paint", matrix=place)
    # carrier arms to the frame
    for s in (-1, 1):
        x = s * 1.6
        p0 = (x * math.cos(a), BROOM_F + x * math.sin(a), BROOM_R + rr + 0.03)
        b.cyl(p0, (s * 0.46, BROOM_F + s * 0.5, 1.0), 0.06, "Chassis", segs=10)
    b.box((-0.5, BROOM_F - 0.4, 0.95), (0.5, BROOM_F + 0.4, 1.12), "Chassis", bevel=0.02)
    # (the hood is a quarter shell over the top and back of the drum; the front stays open)
    # air-blast nozzle behind the drum, and the duct from the blower housing down to it
    noz = M(0.0, BROOM_F - 1.0, 0.0, yaw=yaw)
    b.box((-half + 0.4, -0.12, 0.12), (half - 0.4, 0.12, 0.45), "Chassis", bevel=0.04, matrix=noz)
    b.box((-half + 0.42, -0.1, 0.08), (half - 0.42, 0.1, 0.13), "Dark", matrix=noz)
    b.cyl((-0.6, 1.0, 1.2), (-0.9, BROOM_F - 1.0 - 0.9 * math.sin(a), 0.42), 0.26, "Paint", segs=18)
    b.cyl((-0.6, 1.0, 1.2), (-0.6, 1.0, 1.35), 0.3, "Chassis", segs=18)


def lamps(b):
    """Lamp housings and lenses at the light positions (the glows come from LIGHTS)."""
    for name, kind, p, d, *_ in LIGHTS:
        r, f, z = p
        if kind == "Beacon":
            b.cyl((r, f, z - 0.24), (r, f, z - 0.13), 0.13, "Dark", segs=16)
            b.cyl((r, f, z - 0.13), (r, f, z + 0.08), 0.1, "LensAmber", segs=16, r2=0.085)
        elif kind == "Head" and z > 3:
            # plough lights on a bracket above the roof front
            b.box((r - 0.04, f - 0.3, 3.58), (r + 0.04, f - 0.2, z - 0.05), "Dark")
            b.box((r - 0.17, f - 0.3, z - 0.11), (r + 0.17, f - 0.02, z + 0.11), "Dark", bevel=0.03)
            b.box((r - 0.14, f - 0.02, z - 0.08), (r + 0.14, f + 0.004, z + 0.08), "LensWhite")
        elif kind == "Head":
            b.box((r - 0.17, f - 0.08, z - 0.09), (r + 0.17, f, z + 0.09), "Dark", bevel=0.02)
            b.box((r - 0.14, f - 0.01, z - 0.06), (r + 0.14, f + 0.006, z + 0.06), "LensWhite")
        elif kind == "Tail":
            b.box((r - 0.12, f - 0.004, z - 0.07), (r + 0.12, f + 0.02, z + 0.07), "LensRed")
        elif kind == "Work":
            # square work lamps on the roof edge, aimed along d
            yaw = math.degrees(math.atan2(d[0], d[1]))
            pitch = math.degrees(math.atan2(d[2], math.hypot(d[0], d[1])))
            mtx = M(r, f, z, yaw=yaw, pitch=pitch)
            b.box((-0.1, -0.1, -0.09), (0.1, 0.04, 0.09), "Dark", bevel=0.02, matrix=mtx)
            b.box((-0.08, 0.04, -0.07), (0.08, 0.055, 0.07), "LensWhite", matrix=mtx)
            b.box((-0.03, -0.1, -0.2), (0.03, -0.04, -0.09), "Dark", matrix=mtx)
        elif kind == "Marker":
            b.cyl((r, f, z - 0.06), (r, f, z + 0.05), 0.045, "LensAmber", segs=10)


def grime(pos, ao):
    """Salt and slush spray: strongest low down and behind the wheels, collecting in crevices."""
    import numpy as np
    z = pos[..., 2]
    low = np.clip(1.0 - (z - 0.2) / 1.1, 0, 1) ** 1.6
    crev = np.clip(1.0 - ao, 0, 1) ** 1.5
    return np.clip(0.55 * low + 0.25 * crev * np.clip(1.4 - z / 2.5, 0, 1), 0, 0.75).astype(np.float32)
