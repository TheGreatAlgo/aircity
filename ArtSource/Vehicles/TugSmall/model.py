"""Compact towbarless tug for regional and narrow-body aircraft (10 to 100 t: turboprops, regional jets, A320 /
737 families), as a smaller airport has, after the Kalmar TBL-50/100 and Goldhofer AST-2 class: 6 m long, 2.9 m
wide, the cab roof 1.74 m up so it fits under an A320-size nose with the nose wheel in its cradle. Handler blue with
a white cab roof, hazard stripes on the bumper and the ends of the legs, an amber beacon on the cab, work lights
into the cradle.

Shared parts in Common/tug.py. Space: (right, forward, up) metres, origin on the ground under the rear axle, which
is where the aircraft's nose wheel sits.
"""
import tug

NAME = "TugSmall"
DISPLAY_NAME = "Towbarless tug (narrow-body)"
KIND = "Tug"
AO_DISTANCE = 0.6
LOD_RATIOS = (0.3, 0.08)
PREVIEW_GROUND = (0.5, 0.5, 0.5)

MATS = tug.MATS
GRIME = tug.GRIME
grime = tug.grime

S = tug.Spec(rear=-1.45, front=4.55, hw=1.45, wb=3.0, track=2.32, R=0.40, W=0.34, rim=0.24,
             deck=1.02, clear=0.22, open=0.62, uend=0.78, rw=0.45,
             cab=(-1.39, -0.24, 2.5, 4.3, 1.74), hood=(-0.04, 1.38, 1.2, 4.28, 1.3))
WHEELBASE = S.wb
WHEELS = S.wheels
MOVERS = [w[0] for w in WHEELS] + ["Cradle"]
SPINNERS = []

_x0, _x1, _f0, _f1, _top = S.cab
LIGHTS = [
    ("Beacon", "Beacon", (_x1 - 0.2, _f1 - 0.22, _top + 0.15), (0, 0, 1), "#FF9A10", 0.95, 0.0),
    ("Headlight_L", "Head", (-S.hw + 0.32, S.front - 0.095, S.deck - 0.2), (0, 1, -0.05), "#FFF4E0", 0.75, 0.0),
    ("Headlight_R", "Head", (S.hw - 0.32, S.front - 0.095, S.deck - 0.2), (0, 1, -0.05), "#FFF4E0", 0.75, 0.0),
    ("Tail_L", "Tail", (-S.hw + 0.2, S.rear - 0.002, S.deck - 0.17), (0, -1, 0), "#FF1A12", 0.55, 0.0),
    ("Tail_R", "Tail", (S.hw - 0.2, S.rear - 0.002, S.deck - 0.17), (0, -1, 0), "#FF1A12", 0.55, 0.0),
    ("Work_L", "Work", (-S.open - 0.22, S.uend + 0.2, S.deck + 0.17), (0.15, -0.75, -0.65), "#FFFFFF", 0.7, 0.0),
    ("Work_R", "Work", (S.open + 0.22, S.uend + 0.2, S.deck + 0.17), (-0.15, -0.75, -0.65), "#FFFFFF", 0.7, 0.0),
    ("Marker_L", "Marker", (-S.hw - 0.01, S.rear + 0.45, S.deck - 0.32), (-1, 0, 0), "#FF8A10", 0.45, 0.0),
    ("Marker_R", "Marker", (S.hw + 0.01, S.rear + 0.45, S.deck - 0.32), (1, 0, 0), "#FF8A10", 0.45, 0.0),
]

WORK = {
    "laneWidth": 2 * S.hw,
    "workSpeed": 1.3,            # m/s (5 km/h) pushing an aircraft back
    "transitSpeed": 8.3,         # m/s (30 km/h) driving solo
    "turnRadius": 5.0,
    "sprayPoint": (0.0, 0.0, 0.0),
    "sprayDirection": (0.0, -1.0, 0.0),
    "broomPoint": (0.0, 0.0, 0.0),
}
TOW = {
    "minMass": 10000.0,          # kg: below that the nose wheel is too small (light aircraft are pushed by hand)
    "maxMass": 100000.0,
    "cradle": (0.0, 0.0, 0.0),   # where the nose wheel stands: on the rear axle line
    "lift": 0.22,
    "opening": 2 * S.open,
    "part": "Cradle",
}


def extra(b):
    # battery / hydraulic box on the deck behind the cab
    b.box((-1.3, 1.05, S.deck - 0.02), (-0.3, 2.3, S.deck + 0.22), "PaintDark", bevel=0.04)
    for k in range(3):
        f = 1.25 + k * 0.38
        b.box((-1.2, f, S.deck + 0.215), (-0.4, f + 0.2, S.deck + 0.228), "Dark")
    # grab rail along the hood's inner edge
    b.cyl((-0.02, 1.4, S.hood[4] + 0.12), (-0.02, 3.9, S.hood[4] + 0.12), 0.022, "HazardYellow", segs=8)
    for f in (1.45, 3.85):
        b.cyl((-0.02, f, S.hood[4] - 0.02), (-0.02, f, S.hood[4] + 0.12), 0.022, "HazardYellow", segs=8)


def build():
    return tug.build(S, LIGHTS, extra)


VIEWS = {
    "front34": ((-7.0, 9.5, 3.6), (0.0, 1.5, 0.7), 40),
    "rear34": ((5.5, -7.5, 4.2), (0.0, 0.3, 0.4), 40),
    "side": ((-15.0, 1.55, 1.3), (0.0, 1.55, 0.8), 50),
    "top": ((0.0, 1.55, 17.0), (0.0, 1.55, 0.0), 40),
}
