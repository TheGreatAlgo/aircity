"""Generic 7-tonne, 9.2 x 3.6 m lower-deck loader in the TREPEL CHAMP 70S size class.
Independent level main/front platforms, fixed-length scissor arms, transfer rollers, end stops and six stabilisers.
"""
import bpy
import baggage as g
import container_gse as c
from kit import Part

NAME, DISPLAY_NAME, KIND = "ContainerLoader", "Lower-deck container loader", "ContainerLoader"
MATS, GRIME, grime = g.MATS, g.GRIME, g.grime
AO_DISTANCE, AO_STRENGTH = .5, .4
LOD_RATIOS = (.26, .07)
SEPARATE_MOVERS, MOVING_LODS = True, True
WHEELBASE = 6.2
WHEELS = [(f"Wheel_{end}{side}", (sign * 1.50, f, .38), .38, end == "F")
          for end, f in (("F", WHEELBASE), ("R", 0)) for side, sign in (("L", -1), ("R", 1))]
LIFTS = [("MainPlatform", 1., .508, 4.8, 4.9, 1, .15), ("FrontPlatform", 5.65, 1.8, 4.3, 2.2, 2, .20)]
MOVERS = [w[0] for w in WHEELS]
for prefix, _, _, _, _, stages, _ in LIFTS:
    MOVERS += [prefix, prefix + "StopRear", prefix + "StopFront"] + [prefix + "Lift_" + str(k) for k in range(stages * 2)]
MOVERS += [f"Stabiliser_{k}" for k in range(6)]
MOVERS += ["MainPlatformGate"]
MOVERS += ["SideBridge"]
WORK = {"laneWidth": 3.6, "workSpeed": .5, "transitSpeed": 4., "turnRadius": 8.7}
CREW = dict(seated=False, position=(-1.54,4.92,1.43), leftHand=(-1.65,5.31,2.30), rightHand=(-1.44,5.31,2.30),
            leftFoot=(-1.65,4.92,1.43), rightFoot=(-1.44,4.92,1.43), height=3.21)
CONTAINER = {"code": "AKE", "maxGross": 7000., "deckWidth": 2.52, "deckLength": 4.8,
             "sideBridgeReach": 2.5,
             "mainTransfer": (0, 1., .508), "frontTransfer": (0, 5.65, 1.8),
             "mainMinHeight": .485, "mainMaxHeight": 3.7, "frontMinHeight": 1.8, "frontMaxHeight": 3.7,
             "mainLift": {"centre": 1., "base": .15, "length": 4.9, "stages": 1},
             "frontLift": {"centre": 5.65, "base": .20, "length": 2.2, "stages": 2}}


def build():
    b = Part("Body")
    # Open lift wells; side sills and crossmembers carry the level platforms and operator station.
    for x in (-1.40, 1.40):
        b.box((x - .10, -1.4, .20), (x + .10, 7.8, .39), "Paint", bevel=.02)
    for f in (-1.28, -.48, 3.44, 6.95, 7.68):
        b.box((-1.36, f - .075, .20), (1.36, f + .075, .36), "Chassis", bevel=.012)
    for side in (-1, 1):
        x = side * 1.61
        b.box((x - .17, -.8, .50), (x + .17, 2.8, 1.10), "Enamel", bevel=.035)
        for k in range(12):
            f = -.55 + k * .22
            b.box((side * 1.787 - .006, f, .65), (side * 1.787 + .006, f + .08, .94), "Dark", bevel=.006)
        g.label(b, "CL 007", (side * 1.795, 1., 1.03), .11, side, "PaintDark")
        for f in (0, WHEELBASE):
            b.box((x - .18, f - .55, .74), (x + .18, f + .55, .81), "Paint", bevel=.04)
    # Elevated operator station sits beside the front platform, with a real access stair.
    b.box((-1.78, 3.6, 1.35), (-1.30, 5.6, 1.43), "Zinc", bevel=.012)
    for k in range(6):
        b.box((-1.78, 2.5 + k * .18, .43 + k * .15), (-1.30, 2.68 + k * .18, .49 + k * .15), "Zinc", bevel=.008)
    for f in (3.7, 5.4):
        b.cyl((-1.78, f, 1.4), (-1.78, f, 2.45), .025, "Yellow", segs=12)
    b.cyl((-1.78, 3.7, 2.45), (-1.78, 5.4, 2.45), .025, "Yellow", segs=12)
    b.box((-1.77, 5.20, 1.45), (-1.30, 5.58, 2.20), "Paint", bevel=.025)
    for x in (-1.65, -1.44): b.cyl((x, 5.31, 2.16), (x, 5.31, 2.32), .018, "Plastic", segs=10)
    g.beacon(b, (-1.6, 5.5, 2.45))
    for x in (-1.50, 1.50):
        g.lamp(b, x, 7.80, .62); g.lamp(b, x, -1.40, .62, False)
    parts = [b] + [g.wheel(n, pos, r, .25) for n, pos, r, _ in WHEELS]
    for prefix, centre, height, length, arm, stages, base in LIFTS:
        parts += c.platform(prefix, centre, height, 2.52, length, arm, stages, base)
    # A sliding, transverse roller bridge spans the clearance between the dolly and main platform.
    bridge = Part("SideBridge", (-1.26, 1., .508))
    for f in (.16, 1.84):
        bridge.box((-1.26, f - .035, .37), (1.24, f + .035, .46), "PaintDark", bevel=.008)
    for k in range(15):
        x = -1.20 + k * .17
        bridge.cyl((x, .20, .463), (x, 1.80, .463), .045, "Zinc", segs=12)
    parts.append(bridge)
    for k, (x, f) in enumerate(( (x, f) for f in (-.90, 3.1, 7.3) for x in (-1.55, 1.55) )):
        b.cyl((x, f, .25), (x, f, .70), .07, "PaintDark", segs=16)
        leg = Part(f"Stabiliser_{k}", (x, f, .23))
        leg.cyl((x, f, .23), (x, f, .66), .045, "Chrome", segs=12)
        leg.box((x - .16, f - .16, .20), (x + .16, f + .16, .24), "Zinc", bevel=.015)
        parts.append(leg)
    return parts


def preview_pose(view):
    for prefix, centre, rest, _, arm, stages, base in LIFTS:
        height = 2.95 if view == "working" else 3.7 if view == "high" else rest
        c.pose_platform(prefix, centre, height, rest, arm, stages, base)
    for k in range(6):
        ob = bpy.data.objects[f"Stabiliser_{k}"]
        if "rest_location" not in ob: ob["rest_location"] = list(ob.location)
        ob.location.z = ob["rest_location"][2] - (.20 if view in ("working", "high") else 0)


VIEWS = {"front34": ((-12., 13., 7.), (0, 3., 1.1), 46),
         "working": ((-12., 12., 7.), (0, 3., 1.8), 46), "high": ((12., -10., 8.), (0, 3., 2.), 45),
         "side": ((-17., 3., 6.), (0, 3., 1.6), 47)}
