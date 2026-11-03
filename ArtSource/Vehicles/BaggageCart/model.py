"""Covered, 3.05 x 1.65 m loose-baggage trailer, with fifth-wheel steering and rolling side curtains."""
import math
import bpy
from mathutils import Vector
import baggage as g
from kit import Part

NAME, DISPLAY_NAME, KIND = "BaggageCart", "Covered baggage cart", "BaggageCart"
MATS, GRIME, grime = g.MATS, g.GRIME, g.grime
AO_DISTANCE, AO_STRENGTH = 0.35, 0.45
LOD_RATIOS = (0.3, 0.09)
SEPARATE_MOVERS = True
MOVING_LODS = True
WHEELBASE = 2.16
WHEELS = [(f"Wheel_{end}{side}", (sign * 0.67, f, 0.203), 0.203, False)
          for end, f in (("F", WHEELBASE), ("R", 0)) for side, sign in (("L", -1), ("R", 1))]
MOVERS = [w[0] for w in WHEELS] + ["FrontAxle", "Drawbar", "Curtain_L", "Curtain_R",
                                    "CurtainRoll_L", "CurtainRoll_R", "Load"]
LIGHTS, SPINNERS = [], []
WORK = {"laneWidth": 1.65, "workSpeed": 1.4, "transitSpeed": 5.0, "turnRadius": 3.0}
BAGGAGE = {"frontHitch": (0, 3.72, 0.38), "rearHitch": (0, -0.64, 0.38),
           "loadMin": (-0.72, -0.31, 0.61), "loadMax": (0.72, 2.48, 1.83),
           "payload": 1800.0, "steeringPivot": (0, WHEELBASE, 0.33)}


def build():
    b = Part("Body")
    b.box((-0.79, -0.43, 0.43), (0.79, 2.62, 0.57), "Zinc", bevel=0.018)
    b.box((-0.75, -0.38, 0.57), (0.75, 2.57, 0.61), "Steel", bevel=0.012)
    for x in (-0.55, 0.55):
        b.box((x - 0.04, -0.40, 0.30), (x + 0.04, 2.58, 0.43), "Chassis", bevel=0.01)
    for f in (-0.39, 2.58):
        b.box((-0.79, f - 0.025, 0.60), (0.79, f + 0.025, 1.96), "Enamel", bevel=0.025)
        for x in (-0.77, 0.77):
            b.box((x - 0.028, f - 0.045, 0.58), (x + 0.028, f + 0.045, 1.98), "Zinc", bevel=0.012)
            b.box((x - 0.045, f - 0.06, 0.74), (x + 0.045, f + 0.06, 1.46), "Plastic", bevel=0.025)
    # Shallow arched sheet roof; closed underside and thin, rolled edges.
    roof = [(-0.825, 1.94), (-0.80, 2.005), (0, 2.045), (0.80, 2.005), (0.825, 1.94),
            (0.79, 1.94), (0, 1.992), (-0.79, 1.94)]
    b.prism(roof, "f", -0.46, 3.11, "Enamel")
    for side in (-1, 1):
        x = side * 0.796
        b.box((x - 0.022, -0.36, 1.89), (x + 0.022, 2.54, 1.94), "Zinc", bevel=0.008)
        b.box((x - 0.023, -0.36, 0.61), (x + 0.023, 2.54, 0.68), "Paint", bevel=0.008)
        g.label(b, "BC 024", (side * 0.817, 0.20, 0.65), 0.055, side)
        for f in (-0.30, 2.45):
            b.box((x - 0.029, f, 0.72), (x + 0.029, f + 0.07, 0.78), "LensAmber", bevel=0.008)
    g.hitch(b, -0.64)
    # Brake linkage and rear axle.
    b.cyl((-0.64, 0, 0.23), (0.64, 0, 0.23), 0.045, "Chassis", segs=12)
    axle = Part("FrontAxle", (0, WHEELBASE, 0.33))
    axle.cyl((-0.64, WHEELBASE, 0.23), (0.64, WHEELBASE, 0.23), 0.045, "Chassis", segs=12)
    axle.cyl((0, WHEELBASE, 0.30), (0, WHEELBASE, 0.39), 0.23, "Steel", segs=32)
    # The horizontal steering bracket reaches beyond the body; the parking hinge then clears its front wall.
    hinge = 2.73
    for x in (-0.25, 0.25):
        axle.cyl((x, WHEELBASE, 0.33), (x, hinge, 0.38), 0.028, "Zinc", segs=10)
    axle.cyl((-0.30, hinge, 0.38), (0.30, hinge, 0.38), 0.035, "Steel", segs=12)
    draw = Part("Drawbar", (0, hinge, 0.38))
    for x in (-0.25, 0.25):
        draw.cyl((x, hinge, 0.38), (0, 3.57, 0.38), 0.028, "Zinc", segs=10)
    # Eye ring around the common 0.38 m coupling height.
    for k in range(20):
        a, c = k * math.tau / 20, (k + 1) * math.tau / 20
        draw.cyl((0.065 * math.cos(a), 3.72 + 0.065 * math.sin(a), 0.38),
                 (0.065 * math.cos(c), 3.72 + 0.065 * math.sin(c), 0.38), 0.018, "Zinc", segs=8)
    draw.cyl((0, 3.49, 0.38), (0, 3.66, 0.38), 0.032, "Zinc", segs=12)
    parts = [b, axle, draw] + [g.wheel(n, c, r, 0.105) for n, c, r, _ in WHEELS]
    for suffix, side in (("L", -1), ("R", 1)):
        x = side * 0.786
        curtain = Part("Curtain_" + suffix, (x, 1.09, 1.89))
        curtain.box((x - 0.009, -0.34, 0.68), (x + 0.009, 2.52, 1.89), "Curtain", bevel=0.007)
        for k in range(10):
            f = -0.25 + k * 0.29
            curtain.box((x - 0.012, f, 0.69), (x + 0.012, f + 0.023, 1.88), "Webbing")
            curtain.box((x - 0.017, f - 0.004, 0.73), (x + 0.017, f + 0.027, 0.78), "Zinc", bevel=0.003)
        roll = Part("CurtainRoll_" + suffix, (x, 1.09, 1.86))
        roll.cyl((x, -0.34, 1.86), (x, 2.52, 1.86), 0.047, "Curtain", segs=20)
        parts += [curtain, roll]
    load = Part("Load")
    for row in range(3):
        for col in range(3):
            g.suitcase(load, ((col - 1) * 0.44, 0.12 + row * 0.78, 0.615),
                       (0.38, 0.27, 0.57 + 0.08 * ((col + row) % 2)),
                       ("BagRed", "BagBlue", "BagTan")[(row + col) % 3], yaw=4 * (col - 1))
    g.duffel(load, (0.1, 2.20, 0.615), yaw=90)
    parts.append(load)
    return parts


def preview_pose(view):
    opened = view in ("open", "turn", "parked")
    for suffix in ("L", "R"):
        ob = bpy.data.objects.get("Curtain_" + suffix)
        ob.scale.z = 0.035 if opened else 1
        bpy.data.objects["CurtainRoll_" + suffix].scale.x = 1.7 if opened else 1
        bpy.data.objects["CurtainRoll_" + suffix].scale.z = 1.7 if opened else 1
    yaw = math.radians(-30 if view == "turn" else 0)
    pivot = Vector((0, WHEELBASE, 0.33))
    for name in ("FrontAxle", "Drawbar", "Wheel_FL", "Wheel_FR"):
        ob = bpy.data.objects.get(name)
        if "rest_location" not in ob:
            ob["rest_location"] = list(ob.location)
        from mathutils import Matrix
        rot = Matrix.Rotation(yaw, 4, "Z")
        ob.location = pivot + rot.to_3x3() @ (Vector(ob["rest_location"]) - pivot)
        ob.rotation_euler.z = yaw
        if name == "Drawbar":
            ob.rotation_euler.x = math.radians(75 if view == "parked" else 0)


VIEWS = {"front34": ((-5.5, 7.2, 3.6), (0, 1.3, 0.95), 46),
         "rear34": ((5.0, -5.3, 3.4), (0, 1.0, 0.9), 45),
         "open": ((-5.8, 4.8, 3.0), (0, 1.1, 1.0), 44),
         "turn": ((-5.5, 6.5, 4.2), (0, 1.4, 0.9), 43)}
VIEWS["parked"] = ((-5.2, 7.0, 3.2), (0, 1.3, 1.0), 45)
