"""One LD3 roller-bed dolly, 508 mm transfer height, 1.6 t payload, fifth-wheel steering.
Generic construction based on Orientitan's HD-P011603 size class, sharing the fleet's 0.38 m hitch.
"""
import math
import bpy
from mathutils import Vector, Matrix
import baggage as g
import container_gse as c
from kit import Part

NAME, DISPLAY_NAME, KIND = "ContainerDolly", "LD3 container dolly", "ContainerDolly"
MATS, GRIME, grime = g.MATS, g.GRIME, g.grime
AO_DISTANCE, AO_STRENGTH = .25, .4
LOD_RATIOS = (.28, .08)
SEPARATE_MOVERS, MOVING_LODS = True, True
WHEELBASE = 1.80
WHEELS = [(f"Wheel_{end}{side}", (sign * .72, f, .20), .20, False)
          for end, f in (("F", WHEELBASE), ("R", 0)) for side, sign in (("L", -1), ("R", 1))]
MOVERS = [w[0] for w in WHEELS] + ["FrontAxle", "Drawbar", "Deck", "Stop_L", "Stop_R"]
MOVERS += ["EndStop_F", "EndStop_R"]
WORK = {"laneWidth": 1.77, "workSpeed": .8, "transitSpeed": 4.5, "turnRadius": 3.0}
BAGGAGE = {"frontHitch": (0, 2.95, .38), "rearHitch": (0, -.55, .38),
           "steeringPivot": (0, WHEELBASE, .30), "payload": 1600.,
           "loadMin": (-.781, .13, .508), "loadMax": (.781, 1.664, .508)}
CONTAINER = {"code": "AKE", "transfer": (0, .897, .508), "maxGross": 1600., "deckWidth": 1.77, "deckLength": 1.915}
# Conservative transport bounds include the detachable LD3's outboard overhang and height.
BOUNDS = ((-.90, -.55, 0), (1.25, 2.95, 2.134))


def build():
    p = Part("Body")
    for x in (-.60, .60):
        p.box((x - .055, -.38, .23), (x + .055, 2.17, .36), "Paint", bevel=.012)
    for f in (-.36, .90, 2.16):
        p.box((-.86, f - .045, .25), (.86, f + .045, .37), "Zinc", bevel=.012)
    p.cyl((-.72, 0, .20), (.72, 0, .20), .042, "Steel", segs=12)
    p.cyl((0, .897, .35), (0, .897, .41), .39, "Steel", segs=32)
    g.hitch(p, -.55)
    g.label(p, "CD 031 / 1600 KG", (-.88, .9, .32), .07, -1)
    axle = Part("FrontAxle", (0, WHEELBASE, .30))
    axle.cyl((-.72, WHEELBASE, .20), (.72, WHEELBASE, .20), .042, "Steel", segs=12)
    axle.cyl((0, WHEELBASE, .27), (0, WHEELBASE, .34), .22, "Zinc", segs=24)
    hinge = 2.18
    for x in (-.25, .25): axle.cyl((x, WHEELBASE, .30), (x, hinge, .38), .027, "Zinc", segs=10)
    draw = Part("Drawbar", (0, hinge, .38))
    for x in (-.25, .25): draw.cyl((x, hinge, .38), (0, 2.81, .38), .027, "Zinc", segs=10)
    draw.cyl((0, 2.80, .38), (0, 2.92, .38), .026, "Zinc", segs=10)
    for k in range(20):
        a, b = k * math.tau / 20, (k + 1) * math.tau / 20
        draw.cyl((.06 * math.cos(a), 2.95 + .06 * math.sin(a), .38),
                 (.06 * math.cos(b), 2.95 + .06 * math.sin(b), .38), .016, "Zinc", segs=8)
    deck = Part("Deck", (0, .897, .40))
    c.roller_bed(deck, (0, .897), 1.77, 1.915, .508, False)
    parts = [p, axle, draw, deck] + [g.wheel(n, pos, r, .12) for n, pos, r, _ in WHEELS]
    for side, suffix in ((-1, "L"), (1, "R")):
        stop = Part("Stop_" + suffix, (side * .82, .897, .508))
        for f in (.28, 1.5):
            stop.box((side * .82 - .035, f - .09, .46), (side * .82 + .035, f + .09, .545), "Yellow", bevel=.008)
        parts.append(stop)
    for suffix, f in (("F", 1.73), ("R", .065)):
        stop = Part("EndStop_" + suffix, (0, f, .508))
        for x in (-.55, .55): stop.box((x - .09, f - .025, .49), (x + .09, f + .025, .64), "Yellow", bevel=.008)
        parts.append(stop)
    return parts


def preview_pose(view):
    yaw = math.radians(-30 if view == "turn" else 0)
    pivot = Vector((0, WHEELBASE, .30)); rot = Matrix.Rotation(yaw, 4, "Z")
    for name in ("FrontAxle", "Drawbar", "Wheel_FL", "Wheel_FR"):
        ob = bpy.data.objects[name]
        if "rest_location" not in ob: ob["rest_location"] = list(ob.location)
        ob.location = pivot + rot.to_3x3() @ (Vector(ob["rest_location"]) - pivot); ob.rotation_euler.z = yaw
    bpy.data.objects["Deck"].rotation_euler.z = math.radians(90 if view == "transfer" else 0)


VIEWS = {"front34": ((-4.3, 5.2, 2.7), (0, 1, .35), 46), "turn": ((-4.5, 5.2, 3), (0, 1, .35), 46),
         "transfer": ((4.3, 4.6, 3), (0, 1, .4), 47)}
