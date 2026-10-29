"""Full-size towbarless tug for everything up to an A380 (40 to 600 t), as a hub has, after the Goldhofer AST-1X
and Kalmar TBL-600 class: 10 m long, 4.3 m wide, a wide U for twin nose wheels of up to 1.3 m, big tyres, the low
cab on the front left corner (ahead of a narrow-body's nose when it holds one) and a long engine hood on the right.
Same handler blue and stripes as the small tug, with a second beacon on the right leg.

Shared parts in Common/tug.py. Space: (right, forward, up) metres, origin on the ground under the rear axle, which
is where the aircraft's nose wheel sits.
"""
import tug

NAME = "TugLarge"
DISPLAY_NAME = "Towbarless tug (wide-body)"
KIND = "Tug"
AO_DISTANCE = 0.9
LOD_RATIOS = (0.3, 0.08)
PREVIEW_GROUND = (0.5, 0.5, 0.5)

MATS = tug.MATS
GRIME = tug.GRIME
grime = tug.grime

S = tug.Spec(rear=-2.3, front=7.65, hw=2.15, wb=4.6, track=3.42, R=0.60, W=0.52, rim=0.36,
             deck=1.42, clear=0.3, open=0.92, uend=1.25, rw=0.68,
             cab=(-2.09, -0.74, 5.45, 7.4, 2.28), hood=(-0.5, 2.05, 1.75, 7.3, 1.85))
WHEELBASE = S.wb
WHEELS = S.wheels
MOVERS = [w[0] for w in WHEELS] + ["Cradle"]
SPINNERS = []

_x0, _x1, _f0, _f1, _top = S.cab
LIGHTS = [
    ("Beacon", "Beacon", (_x0 + 0.25, _f1 - 0.25, _top + 0.15), (0, 0, 1), "#FF9A10", 1.1, 0.0),
    ("Beacon_Rear", "Beacon", (S.hw - 0.45, S.rear + 0.7, S.deck + 0.28), (0, 0, 1), "#FF9A10", 1.1, 0.41),
    ("Headlight_L", "Head", (-S.hw + 0.4, S.front - 0.095, S.deck - 0.24), (0, 1, -0.05), "#FFF4E0", 0.85, 0.0),
    ("Headlight_R", "Head", (S.hw - 0.4, S.front - 0.095, S.deck - 0.24), (0, 1, -0.05), "#FFF4E0", 0.85, 0.0),
    ("Tail_L", "Tail", (-S.hw + 0.28, S.rear - 0.002, S.deck - 0.2), (0, -1, 0), "#FF1A12", 0.65, 0.0),
    ("Tail_R", "Tail", (S.hw - 0.28, S.rear - 0.002, S.deck - 0.2), (0, -1, 0), "#FF1A12", 0.65, 0.0),
    ("Work_L", "Work", (-S.open - 0.3, S.uend + 0.25, S.deck + 0.2), (0.15, -0.75, -0.65), "#FFFFFF", 0.8, 0.0),
    ("Work_R", "Work", (S.open + 0.3, S.uend + 0.25, S.deck + 0.2), (-0.15, -0.75, -0.65), "#FFFFFF", 0.8, 0.0),
    ("Marker_L", "Marker", (-S.hw - 0.01, S.rear + 0.6, S.deck - 0.4), (-1, 0, 0), "#FF8A10", 0.5, 0.0),
    ("Marker_R", "Marker", (S.hw + 0.01, S.rear + 0.6, S.deck - 0.4), (1, 0, 0), "#FF8A10", 0.5, 0.0),
]

WORK = {
    "laneWidth": 2 * S.hw,
    "workSpeed": 1.1,            # m/s (4 km/h) pushing a wide-body back
    "transitSpeed": 7.0,         # m/s (25 km/h) driving solo
    "turnRadius": 7.5,
    "sprayPoint": (0.0, 0.0, 0.0),
    "sprayDirection": (0.0, -1.0, 0.0),
    "broomPoint": (0.0, 0.0, 0.0),
}
TOW = {
    "minMass": 40000.0,
    "maxMass": 600000.0,
    "cradle": (0.0, 0.0, 0.0),
    "lift": 0.3,
    "opening": 2 * S.open,
    "part": "Cradle",
}


def extra(b):
    # ballast blocks on the deck behind the cab, a hydraulic tank, the exhaust out of the hood's side
    for k in range(3):
        f = 2.0 + k * 1.05
        b.box((-2.0, f, S.deck - 0.02), (-0.75, f + 0.95, S.deck + 0.3), "PaintDark", bevel=0.05)
        b.cyl((-1.37, f + 0.47, S.deck + 0.29), (-1.37, f + 0.47, S.deck + 0.36), 0.08, "Steel", segs=10)
    b.cyl((-1.9, 5.05, S.deck + 0.28), (-0.85, 5.05, S.deck + 0.28), 0.26, "Steel", segs=18)
    b.cyl((S.hood[1] + 0.02, 3.2, S.deck + 0.2), (S.hood[1] + 0.12, 3.2, S.deck + 0.2), 0.07, "Chrome", segs=12)
    # grab rails along the hood and up to the cab
    b.cyl((-0.56, 2.0, S.hood[4] + 0.14), (-0.56, 5.2, S.hood[4] + 0.14), 0.026, "HazardYellow", segs=8)
    for f in (2.05, 5.15):
        b.cyl((-0.56, f, S.hood[4] - 0.02), (-0.56, f, S.hood[4] + 0.14), 0.026, "HazardYellow", segs=8)
    # post for the rear beacon
    b.cyl((S.hw - 0.45, S.rear + 0.7, S.deck), (S.hw - 0.45, S.rear + 0.7, S.deck + 0.14), 0.05, "Dark", segs=10)


def build():
    return tug.build(S, LIGHTS, extra)


VIEWS = {
    "front34": ((-11.0, 15.0, 5.6), (0.0, 2.4, 1.0), 40),
    "rear34": ((8.5, -11.5, 6.5), (0.0, 0.6, 0.6), 40),
    "side": ((-24.0, 2.7, 2.0), (0.0, 2.7, 1.2), 50),
    "top": ((0.0, 2.7, 27.0), (0.0, 2.7, 0.0), 40),
}
