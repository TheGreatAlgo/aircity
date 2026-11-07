"""Open driving-station, 3 m baggage tractor; generic JST / TY-25 class, not an aircraft tug."""
import baggage as g
from kit import Part

NAME = "BaggageTractor"
DISPLAY_NAME = "Baggage tractor"
KIND = "BaggageTractor"
MATS, GRIME, grime = g.MATS, g.GRIME, g.grime
AO_DISTANCE, AO_STRENGTH = 0.35, 0.5
LOD_RATIOS = (0.28, 0.08)
WHEELBASE = 1.60
WHEELS = [(f"Wheel_{end}{side}", (sign * track, f, 0.30), 0.30, end == "F")
          for end, f, track in (("F", WHEELBASE, 0.57), ("R", 0, 0.55))
          for side, sign in (("L", -1), ("R", 1))]
MOVERS = [w[0] for w in WHEELS]
SPINNERS = []
WORK = {"laneWidth": 1.47, "workSpeed": 1.4, "transitSpeed": 6.0, "turnRadius": 2.9}
CREW = dict(seated=True, position=(0,.24,1.10), leftHand=(-.11,.73,1.28), rightHand=(.11,.73,1.28),
            leftFoot=(-.11,.74,.45), rightFoot=(.11,.74,.45), height=1.96)
BAGGAGE = {"rearHitch": (0, -0.68, 0.38), "frontHitch": (0, 2.34, 0.38),
           "operator": (0, 0.27, 0.88)}
LIGHTS = [("Beacon", "Beacon", (-0.49, -0.18, 1.78), (0, 0, 1), "#FF9A1A", 0.55, 0),
          *[(f"Head_{s}", "Head", (x, 2.25, 0.72), (0, 1, -0.1), "#FFF4E0", 0.45, 0)
            for s, x in (("L", -0.51), ("R", 0.51))],
          *[(f"Tail_{s}", "Tail", (x, -0.61, 0.70), (0, -1, 0), "#E0181C", 0.35, 0)
            for s, x in (("L", -0.54), ("R", 0.54))]]


def build():
    b = Part("Body")
    b.box((-0.44, -0.57, 0.18), (0.44, 2.25, 0.43), "Chassis", bevel=0.035)
    b.box((-0.40, 0.99, 0.40), (0.40, 2.13, 0.66), "Chassis", bevel=0.025)
    for f in (0, WHEELBASE):
        b.cyl((-0.56, f, 0.30), (0.56, f, 0.30), 0.052, "Steel", segs=12)
        b.box((-0.16, f - 0.13, 0.22), (0.16, f + 0.13, 0.39), "Chassis", bevel=0.04)
    # Front bonnet and rear wheel guards are separate solid panels with actual wheel clearance.
    b.box((-0.68, 0.96, 0.64), (0.68, 2.19, 1.08), "Paint", bevel=0.045, segments=3)
    b.box((-0.67, 1.04, 1.07), (0.67, 2.17, 1.105), "Enamel", bevel=0.035)
    b.box((-0.68, -0.54, 0.59), (0.68, -0.18, 0.87), "Paint", bevel=0.04)
    for side in (-1, 1):
        x = side * 0.64
        b.box((x - 0.09, -0.40, 0.62), (x + 0.09, 0.49, 0.68), "Paint", bevel=0.018)
        b.box((x - 0.08, 0.36, 0.39), (x + 0.08, 0.99, 0.44), "Zinc", bevel=0.018)
        for k in range(5):
            f = 0.43 + k * 0.09
            b.box((x - 0.06, f, 0.438), (x + 0.06, f + 0.015, 0.447), "Dark")
        b.cyl((x, -0.20, 0.73), (x, -0.20, 1.16), 0.022, "Yellow", segs=10)
        b.cyl((x, -0.20, 1.16), (x, 0.17, 1.16), 0.022, "Yellow", segs=10)
        b.cyl((x, 0.17, 1.16), (x, 0.17, 0.70), 0.022, "Yellow", segs=10)
        for k in range(4):
            b.box((side * 0.686 - 0.004, 1.14 + k * 0.12, 0.83),
                  (side * 0.686 + 0.004, 1.21 + k * 0.12, 0.855), "Dark", bevel=0.004)
        g.label(b, "BT 018", (side * 0.686, -0.36, 0.76), 0.073, side)
    # Open, human-sized station: seat, dashboard, pedals and hand controls.
    b.box((-0.44, -0.08, 0.43), (0.44, 0.79, 0.51), "PaintDark", bevel=0.015)
    b.box((-0.18, 0.06, 0.50), (0.18, 0.39, 0.79), "Chassis", bevel=0.025)
    g.seat(b, 0, 0.23, 0.79)
    b.box((-0.43, 0.78, 0.55), (0.43, 1.01, 1.15), "Enamel", bevel=0.04)
    b.box((-0.38, 0.765, 1.035), (0.38, 0.79, 1.15), "Plastic", bevel=0.014)
    g.steering_wheel(b, (0, 0.73, 1.28))
    for x in (-0.13, 0.11):
        b.box((x - 0.045, 0.58, 0.51), (x + 0.045, 0.71, 0.54), "Plastic", bevel=0.012)
    b.cyl((0.30, 0.36, 0.58), (0.30, 0.41, 0.92), 0.012, "Steel", segs=8)
    b.sphere((0.30, 0.41, 0.93), 0.033, "Plastic", segs=12, rings=6)
    # Front grille, lamps and replaceable bumper strip.
    b.box((-0.45, 2.195, 0.62), (0.45, 2.215, 0.99), "Dark", bevel=0.015)
    for k in range(6):
        z = 0.65 + k * 0.058
        b.box((-0.43, 2.215, z), (0.43, 2.23, z + 0.013), "Zinc", bevel=0.004)
    for front, f in ((True, 2.25), (False, -0.61)):
        b.box((-0.73, f - 0.045, 0.29), (0.73, f + 0.045, 0.48), "Dark", bevel=0.02)
        for x in (-0.54, 0.54):
            g.lamp(b, x, f, 0.73 if front else 0.70, front)
        for x in (-0.55, 0.55):
            b.box((x - 0.11, f - 0.049, 0.32), (x + 0.11, f + 0.049, 0.37), "Yellow")
    g.hitch(b, -0.68)
    g.hitch(b, 2.34, front=True)
    b.cyl((-0.49, -0.18, 0.83), (-0.49, -0.18, 1.72), 0.022, "Yellow", segs=10)
    g.beacon(b, (-0.49, -0.18, 1.78))
    return [b] + [g.wheel(n, c, r, 0.20) for n, c, r, _ in WHEELS]


VIEWS = {"front34": ((-4.8, 6.3, 3.1), (0, 0.75, 0.70), 45),
         "rear34": ((4.5, -4.2, 2.8), (0, 0.62, 0.72), 45),
         "side": ((-7, 0.8, 2.1), (0, 0.8, 0.7), 48)}
