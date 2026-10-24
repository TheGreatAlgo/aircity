"""Grass mower: a cab tractor in the airport's fleet yellow towing an orange batwing rotary mower (a centre deck and
two wings, about 4.6 m of cut), as airports use to keep grass runways and runway strips short. Amber beacon on the
cab roof, work lights front and back, road lights, tail lights repeated on the mower, amber markers on the wing
tips and a slow-moving-vehicle triangle.

Vehicle space: (right, forward, up) metres, origin on the ground under the tractor's rear axle.
"""
import math

from kit import M, Part, arc

NAME = "Mower"
DISPLAY_NAME = "Grass mower"
KIND = "Mower"
AO_DISTANCE = 0.6
LOD_RATIOS = (0.3, 0.08)
PREVIEW_GROUND = (0.16, 0.24, 0.1)

MATS = {
    "Paint": ("#F2A30F", 0.0, 0.38, False),
    "Implement": ("#D4501C", 0.0, 0.45, False),
    "Seat": ("#2A2C30", 0.0, 0.7, False),
}
GRIME = "#4E5638"          # grass clippings and mud

WHEELBASE = 2.6
REAR_R, REAR_W, REAR_RIM = 0.84, 0.5, 0.48
FRONT_R, FRONT_W, FRONT_RIM = 0.58, 0.38, 0.32
REAR_TRACK, FRONT_TRACK = 1.96, 1.84

WHEELS = [
    ("Wheel_FL", (-FRONT_TRACK / 2, WHEELBASE, FRONT_R), FRONT_R, True),
    ("Wheel_FR", (FRONT_TRACK / 2, WHEELBASE, FRONT_R), FRONT_R, True),
    ("Wheel_RL", (-REAR_TRACK / 2, 0.0, REAR_R), REAR_R, False),
    ("Wheel_RR", (REAR_TRACK / 2, 0.0, REAR_R), REAR_R, False),
]
MOVERS = [w[0] for w in WHEELS]
SPINNERS = []

DECK_F0, DECK_F1 = -2.6, -4.9          # centre deck front / back
WING_IN, WING_OUT = 1.1, 2.55

LIGHTS = [
    ("Beacon", "Beacon", (0.0, -0.42, 3.27), (0, 0, 1), "#FF9A10", 1.1, 0.0),
    ("Road_L", "Head", (-0.62, 1.66, 2.94), (0, 1, -0.05), "#FFF4E0", 0.8, 0.0),
    ("Road_R", "Head", (0.62, 1.66, 2.94), (0, 1, -0.05), "#FFF4E0", 0.8, 0.0),
    ("Headlight_L", "Head", (-0.27, 3.99, 1.38), (0, 1, 0), "#FFF4E0", 0.7, 0.0),
    ("Headlight_R", "Head", (0.27, 3.99, 1.38), (0, 1, 0), "#FFF4E0", 0.7, 0.0),
    ("Work_FL", "Work", (-0.78, 1.64, 3.03), (-0.2, 0.8, -0.55), "#FFFFFF", 0.8, 0.0),
    ("Work_FR", "Work", (0.78, 1.64, 3.03), (0.2, 0.8, -0.55), "#FFFFFF", 0.8, 0.0),
    ("Work_RL", "Work", (-0.78, -0.66, 3.03), (-0.35, -0.75, -0.55), "#FFFFFF", 0.8, 0.0),
    ("Work_RR", "Work", (0.78, -0.66, 3.03), (0.35, -0.75, -0.55), "#FFFFFF", 0.8, 0.0),
    ("Tail_L", "Tail", (-1.0, -0.62, 1.62), (0, -1, 0), "#FF1A12", 0.6, 0.0),
    ("Tail_R", "Tail", (1.0, -0.62, 1.62), (0, -1, 0), "#FF1A12", 0.6, 0.0),
    ("MowerTail_L", "Tail", (-0.95, DECK_F1 - 0.06, 0.78), (0, -1, 0), "#FF1A12", 0.6, 0.0),
    ("MowerTail_R", "Tail", (0.95, DECK_F1 - 0.06, 0.78), (0, -1, 0), "#FF1A12", 0.6, 0.0),
    ("Marker_L", "Marker", (-WING_OUT - 0.04, -2.45, 0.86), (-1, 0, 0), "#FF8A10", 0.5, 0.0),
    ("Marker_R", "Marker", (WING_OUT + 0.04, -2.45, 0.86), (1, 0, 0), "#FF8A10", 0.5, 0.0),
]

WORK = {
    "laneWidth": 4.4,            # cut width per mower, with overlap
    "workSpeed": 4.2,            # m/s (15 km/h) mowing
    "transitSpeed": 8.0,         # m/s (29 km/h) towing the mower
    "turnRadius": 7.0,
    "sprayPoint": (0.0, DECK_F1 - 0.1, 0.25),        # clippings leave the back of the decks
    "sprayDirection": (0.0, -1.0, 0.3),
    "broomPoint": (0.0, 0.0, 0.0),
}

VIEWS = {
    "front34": ((-9.0, 9.5, 4.2), (0.0, -0.8, 1.3), 40),
    "rear34": ((8.5, -11.0, 4.6), (0.0, -1.8, 1.0), 40),
    "side": ((-22.0, -0.9, 1.6), (0.0, -0.9, 1.4), 50),
    "top": ((0.0, -0.9, 26.0), (0.0, -0.9, 0.0), 40),
}


def build():
    parts = []
    body = Part("Body")
    tractor(body)
    cab(body)
    hitch(body)
    mower(body)
    lamps(body)
    parts.append(body)
    for name, c, r, steer in WHEELS:
        w = Part(name, pivot=c)
        front = c[1] > 1.0
        w.wheel(c, r, FRONT_W if front else REAR_W, FRONT_RIM if front else REAR_RIM, side=1 if c[0] > 0 else -1,
                rim="Paint", lugs=8)
        parts.append(w)
    return parts


def tractor(b):
    # frame / engine block and front axle
    b.box((-0.36, 0.6, 0.55), (0.36, 3.85, 1.02), "Chassis", bevel=0.04)
    b.box((-0.3, 3.6, 0.5), (0.3, 4.05, 0.95), "Chassis", bevel=0.05)    # front weight block
    for k in range(5):
        b.box((-0.34, 3.98 + k * 0.0, 0.52 + k * 0.08), (0.34, 4.12, 0.57 + k * 0.08), "Dark")
    b.cyl((-FRONT_TRACK / 2 + 0.12, WHEELBASE, FRONT_R), (FRONT_TRACK / 2 - 0.12, WHEELBASE, FRONT_R), 0.1, "Chassis", segs=12)
    b.box((-0.4, WHEELBASE - 0.2, FRONT_R - 0.12), (0.4, WHEELBASE + 0.2, FRONT_R + 0.14), "Chassis", bevel=0.03)
    # rear axle housing and transmission
    b.cyl((-REAR_TRACK / 2 + 0.15, 0.0, REAR_R), (REAR_TRACK / 2 - 0.15, 0.0, REAR_R), 0.13, "Chassis", segs=14)
    b.box((-0.45, -0.45, 0.6), (0.45, 0.7, 1.5), "Chassis", bevel=0.06)
    # bonnet: side profile (forward, up), sloping to a rounded nose
    prof = [(1.5, 1.0), (3.98, 1.0), (4.02, 1.25)] + [(p[0], p[1]) for p in arc(3.72, 1.48, 0.3, 0, 90, 5)] + \
        [(2.2, 1.86), (1.5, 1.9)]
    b.prism(prof, "r", -0.47, 0.94, "Paint", bevel=0.08)
    # grille and bonnet vents
    b.box((-0.36, 3.985, 1.05), (0.36, 4.03, 1.62), "Dark", bevel=0.02)
    for k in range(6):
        b.box((-0.33, 4.02, 1.08 + k * 0.09), (0.33, 4.04, 1.11 + k * 0.09), "Chassis")
    for s in (-1, 1):
        x = s * 0.475
        for k in range(5):
            f = 2.7 + k * 0.17
            b.box((x - s * 0.005, f, 1.45), (x + s * 0.012, f + 0.08, 1.7), "Dark")
    # exhaust stack at the cab's right front pillar
    b.cyl((0.52, 1.65, 1.7), (0.52, 1.65, 3.35), 0.055, "Chrome", segs=12)
    b.cyl((0.52, 1.65, 2.2), (0.52, 1.65, 2.8), 0.08, "Dark", segs=12)
    # rear fenders (wide arcs over the rear wheels), joined to the cab
    for s in (-1, 1):
        x0 = s * (REAR_TRACK / 2 - REAR_W / 2 - 0.08)
        width = REAR_W + 0.2
        outer, inner = REAR_R + 0.17, REAR_R + 0.11
        prof = [(math.cos(math.radians(a)) * outer, REAR_R + math.sin(math.radians(a)) * outer) for a in range(10, 171, 10)]
        prof += [(math.cos(math.radians(a)) * inner, REAR_R + math.sin(math.radians(a)) * inner) for a in range(170, 9, -10)]
        b.prism(prof, "r", x0 if s > 0 else x0 - width, width, "Paint", bevel=0.01)
    # fuel tank and steps (left)
    b.box((-0.8, 0.95, 0.75), (-0.42, 1.55, 1.35), "Chassis", bevel=0.06)
    for z in (0.62, 1.02):
        b.box((-0.95, 1.0, z - 0.035), (-0.72, 1.5, z), "Steel")


def cab(b):
    f0, f1 = -0.55, 1.5
    hw = 0.82
    z0, z1 = 1.55, 2.95
    # lower cab shell and floor
    b.box((-hw, f0, 1.45), (hw, f1, z0 + 0.05), "Paint", bevel=0.03)
    # glass box with corner posts
    b.box((-hw + 0.02, f0 + 0.02, z0), (hw - 0.02, f1 - 0.02, z1), "Glass")
    for x in (-hw, hw - 0.07):
        for f in (f0, f1 - 0.07):
            b.box((x, f, z0), (x + 0.07, f + 0.07, z1), "Dark")
        b.box((x, 0.55, z0), (x + 0.07, 0.6, z1), "Dark")        # door post
    # roof with overhang, white top
    b.box((-hw - 0.08, f0 - 0.15, z1), (hw + 0.08, f1 + 0.15, z1 + 0.12), "Paint", bevel=0.04)
    b.box((-hw + 0.02, f0 - 0.05, z1 + 0.12), (hw - 0.02, f1 + 0.05, z1 + 0.17), "White", bevel=0.03)
    # seat and steering wheel seen through the glass
    b.box((-0.3, -0.25, 1.62), (0.3, 0.25, 1.9), "Seat", bevel=0.05)
    b.box((-0.3, -0.33, 1.9), (0.3, -0.18, 2.5), "Seat", bevel=0.05)
    b.cyl((0.0, 0.6, 1.62), (0.0, 0.85, 2.15), 0.04, "Dark", segs=8)
    b.cyl((0.0, 0.83, 2.12), (0.0, 0.87, 2.2), 0.2, "Dark", segs=16)
    # mirrors
    for s in (-1, 1):
        b.cyl((s * 0.84, 1.42, 2.55), (s * 1.2, 1.55, 2.6), 0.018, "Dark", segs=6)
        b.box((s * 1.16, 1.5, 2.35), (s * 1.24 if s > 0 else s * 1.24, 1.6, 2.75), "Plastic", bevel=0.015)


def hitch(b):
    # three-point linkage (lower links, top link) and drawbar
    for s in (-1, 1):
        b.cyl((s * 0.38, -0.45, 0.62), (s * 0.45, -1.15, 0.5), 0.035, "Chassis", segs=8)
        b.cyl((s * 0.4, -0.3, 1.25), (s * 0.43, -0.85, 0.6), 0.025, "Chassis", segs=8)
    b.cyl((0.0, -0.45, 1.35), (0.0, -1.05, 1.1), 0.04, "Chassis", segs=8)
    b.box((-0.08, -1.35, 0.4), (0.08, -0.4, 0.5), "Chassis")
    # mower tongue: an A-frame from the drawbar to the deck front, PTO shaft with its shield
    for s in (-1, 1):
        b.cyl((0.0, -1.3, 0.48), (s * 0.85, DECK_F0 - 0.05, 0.62), 0.05, "Implement", segs=10)
    b.cyl((0.0, -1.3, 0.48), (0.0, DECK_F0, 0.66), 0.06, "Implement", segs=10)
    b.cyl((0.0, -0.5, 0.7), (0.0, -3.4, 0.85), 0.035, "Steel", segs=10)
    b.cyl((0.0, -1.0, 0.72), (0.0, -2.9, 0.83), 0.065, "Dark", segs=12)
    # parking jack
    b.cyl((0.18, -1.7, 0.2), (0.18, -1.7, 0.95), 0.04, "Steel", segs=8)


def mower(b):
    imp = "Implement"
    # centre deck: a domed housing with skirts
    b.box((-WING_IN - 0.05, DECK_F1, 0.2), (WING_IN + 0.05, DECK_F0, 0.66), imp, bevel=0.12, segments=3)
    b.box((-WING_IN - 0.08, DECK_F1 + 0.02, 0.1), (WING_IN + 0.08, DECK_F0 - 0.02, 0.24), "Dark")
    # wings, hinged at the centre deck's sides (working position: down)
    for s in (-1, 1):
        lo, hi = sorted((s * (WING_IN + 0.03), s * WING_OUT))
        b.box((lo, -4.3, 0.2), (hi, -2.15, 0.6), imp, bevel=0.1, segments=3)
        b.box((lo + (0.03 if s < 0 else 0.0), -4.28, 0.1), (hi - (0.03 if s > 0 else 0.0), -2.17, 0.22), "Dark")
        # hinge knuckles and fold ram
        for f in (-2.4, -4.05):
            b.cyl((s * (WING_IN - 0.05), f - 0.1, 0.68), (s * (WING_IN - 0.05), f + 0.1, 0.68), 0.07, "Chassis", segs=10)
        b.cyl((s * 0.5, -3.0, 0.95), (s * (WING_IN + 0.55), -3.0, 0.72), 0.05, "Chassis", segs=10)
        b.cyl((s * 0.9, -3.0, 0.85), (s * (WING_IN + 0.55), -3.0, 0.72), 0.025, "Chrome", segs=8)
        # wing gearbox and its driveline from the centre
        gx = s * 1.85
        b.box((gx - 0.18, -3.38, 0.6), (gx + 0.18, -3.02, 0.92), "Dark", bevel=0.04)
        b.cyl((gx, -3.2, 0.92), (gx, -3.2, 1.0), 0.12, "Dark", segs=12)
        b.cyl((s * 0.2, -3.45, 0.86), (gx - s * 0.18, -3.2, 0.8), 0.035, "Steel", segs=8)
        b.cyl((s * 0.35, -3.43, 0.86), (gx - s * 0.3, -3.22, 0.81), 0.06, "Dark", segs=10)
        # wing wheel on a trailing arm
        wc = (s * 2.2, -4.62, 0.28)
        b.cyl((wc[0], -4.25, 0.55), (wc[0], wc[1], wc[2] + 0.05), 0.04, "Chassis", segs=8)
        b.cyl((wc[0] - 0.09, wc[1], wc[2]), (wc[0] + 0.09, wc[1], wc[2]), 0.28, "Tyre", segs=18)
        b.cyl((wc[0] - 0.1, wc[1], wc[2]), (wc[0] + 0.1, wc[1], wc[2]), 0.13, "Steel", segs=12)
        # safety chains along the front edge
        lo, hi = sorted((s * (WING_IN + 0.1), s * (WING_OUT - 0.05)))
        b.box((lo, -2.17, 0.06), (hi, -2.13, 0.26), "Dark")
        # wing-tip marker plate
        b.box((s * WING_OUT - 0.02, -2.6, 0.55), (s * WING_OUT + 0.02, -2.3, 0.9), "Dark")
    # centre gearbox
    b.box((-0.25, -3.62, 0.62), (0.25, -3.28, 0.98), "Dark", bevel=0.05)
    b.cyl((0.0, -3.45, 0.98), (0.0, -3.45, 1.08), 0.14, "Dark", segs=14)
    b.box((-WING_IN, DECK_F0 - 0.02, 0.06), (WING_IN, DECK_F0 + 0.02, 0.26), "Dark")     # front chains
    # centre deck rear wheels on a walking axle
    b.cyl((-0.75, DECK_F1 + 0.3, 0.6), (0.75, DECK_F1 + 0.3, 0.6), 0.05, "Chassis", segs=10)
    for s in (-1, 1):
        wc = (s * 0.75, DECK_F1 - 0.22, 0.3)
        b.cyl((wc[0], DECK_F1 + 0.3, 0.6), (wc[0], wc[1], wc[2]), 0.045, "Chassis", segs=8)
        b.cyl((wc[0] - 0.1, wc[1], wc[2]), (wc[0] + 0.1, wc[1], wc[2]), 0.3, "Tyre", segs=18)
        b.cyl((wc[0] - 0.11, wc[1], wc[2]), (wc[0] + 0.11, wc[1], wc[2]), 0.14, "Steel", segs=12)
    # rear lamp bar and slow-moving-vehicle triangle
    b.box((-1.1, DECK_F1 - 0.05, 0.7), (1.1, DECK_F1 + 0.02, 0.86), "Dark")
    tri_h = 0.36
    zc = 1.05
    pts = [(-0.21, DECK_F1 - 0.07, zc - tri_h / 2), (0.21, DECK_F1 - 0.07, zc - tri_h / 2), (0.0, DECK_F1 - 0.07, zc + tri_h / 2)]
    b.quad(pts, "LensOrange")
    inner = [(-0.13, DECK_F1 - 0.075, zc - tri_h / 2 + 0.06), (0.13, DECK_F1 - 0.075, zc - tri_h / 2 + 0.06),
             (0.0, DECK_F1 - 0.075, zc + tri_h / 2 - 0.1)]
    b.quad(inner, "StripeRed")
    b.box((-0.02, DECK_F1 - 0.06, 0.82), (0.02, DECK_F1 - 0.02, zc - tri_h / 2), "Dark")


def lamps(b):
    for name, kind, p, d, *_ in LIGHTS:
        r, f, z = p
        if kind == "Beacon":
            b.cyl((r, f, z - 0.16), (r, f, z - 0.08), 0.12, "Dark", segs=16)
            b.cyl((r, f, z - 0.08), (r, f, z + 0.1), 0.095, "LensAmber", segs=16, r2=0.08)
        elif kind == "Head" and z > 2.5:
            b.box((r - 0.13, f - 0.12, z - 0.07), (r + 0.13, f, z + 0.07), "Dark", bevel=0.02)
            b.box((r - 0.1, f - 0.01, z - 0.05), (r + 0.1, f + 0.005, z + 0.05), "LensWhite")
        elif kind == "Head":
            b.box((r - 0.08, f - 0.02, z - 0.05), (r + 0.08, f + 0.005, z + 0.05), "LensWhite")
        elif kind == "Tail":
            b.box((r - 0.09, f - 0.004, z - 0.05), (r + 0.09, f + 0.03, z + 0.05), "LensRed")
        elif kind == "Work":
            yaw = math.degrees(math.atan2(d[0], d[1]))
            pitch = math.degrees(math.atan2(d[2], math.hypot(d[0], d[1])))
            mtx = M(r, f, z, yaw=yaw, pitch=pitch)
            b.box((-0.08, -0.08, -0.07), (0.08, 0.03, 0.07), "Dark", bevel=0.015, matrix=mtx)
            b.box((-0.065, 0.03, -0.055), (0.065, 0.042, 0.055), "LensWhite", matrix=mtx)
        elif kind == "Marker":
            b.box((r - 0.03, f - 0.06, z - 0.05), (r + 0.03, f + 0.06, z + 0.05), "LensAmber")


def grime(pos, ao):
    import numpy as np
    z = pos[..., 2]
    low = np.clip(1.0 - (z - 0.1) / 0.9, 0, 1) ** 1.5
    crev = np.clip(1.0 - ao, 0, 1) ** 1.5
    deck = ((pos[..., 1] < -2.0) & (z < 0.75)).astype(np.float32) * 0.25
    return np.clip(0.5 * low + 0.25 * crev + deck, 0, 0.7).astype(np.float32)
