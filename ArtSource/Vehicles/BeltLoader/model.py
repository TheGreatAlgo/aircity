"""Full-size, 7.8 m conveyor on a low self-propelled chassis, with independent scissor lifts.

Generic equipment in the NBL size class. The fixed-length, sliding-end lift linkage is our own family,
not an exact reproduction of a manufacturer's hydraulic geometry. Aircraft work is deliberately separate.
"""
import math
import bpy
from mathutils import Vector, Matrix
import baggage as g
from kit import Part

NAME, DISPLAY_NAME, KIND = "BeltLoader", "Baggage belt loader", "BeltLoader"
MATS, GRIME, grime = g.MATS, g.GRIME, g.grime
AO_DISTANCE, AO_STRENGTH = 0.4, 0.45
LOD_RATIOS = (0.26, 0.07)
SEPARATE_MOVERS = True
MOVING_LODS = True
WHEELBASE = 3.6
WHEELS = [(f"Wheel_{end}{side}", (sign * 0.83, f, 0.33), 0.33, end == "F")
          for end, f in (("F", WHEELBASE), ("R", 0)) for side, sign in (("L", -1), ("R", 1))]
LENGTH, REAR, FRONT_HEIGHT = 7.8, (0.30, -1.95, 0.82), 1.45
ANGLE = math.asin((FRONT_HEIGHT - REAR[2]) / LENGTH)
SUPPORT_OFFSET = 0.68
LIFTS = [("LiftRear", 0.2, 0.10, 1.6, 1), ("LiftFront", 3.3, 0.10, 1.75, 2)]
MOVERS = [w[0] for w in WHEELS] + ["Conveyor", "BeltSurface", "RearRoller", "FrontRoller"]
MOVERS += [f"{prefix}_{i}" for prefix, _, _, _, stages in LIFTS for i in range(stages * 2)]
MOVERS += ["LiftRear_Head", "LiftFront_Head"]
LIGHTS = [("Beacon", "Beacon", (-0.75, 1.05, 1.71), (0, 0, 1), "#FF9A1A", 0.55, 0),
          *[(f"Head_{s}", "Head", (x, 4.39, 0.64), (0, 1, -0.1), "#FFF4E0", 0.45, 0)
            for s, x in (("L", -0.78), ("R", 0.78))]]
SPINNERS = []
WORK = {"laneWidth": 2.02, "workSpeed": 0.6, "transitSpeed": 4.0, "turnRadius": 5.0}
BAGGAGE = {"rearHitch": (0, -0.87, 0.38), "operator": (-0.65, 1.88, 0.89),
           "beltRear": REAR, "beltLength": LENGTH, "beltFrontHeight": FRONT_HEIGHT, "beltSupportOffset": SUPPORT_OFFSET,
           "frontMinHeight": 1.2, "frontMaxHeight": 4.3, "rearMinHeight": 0.7, "rearMaxHeight": 1.1,
           "rearLift": {"centre": 0.2, "base": 0.10, "length": 1.6, "stages": 1},
           "frontLift": {"centre": 3.3, "base": 0.10, "length": 1.75, "stages": 2}}


def build():
    b = Part("Body")
    # Open centre wells let the folded linkage sit below the chassis deck without intersecting it.
    for x in (-0.64, 0.68):
        b.box((x - 0.04, -0.78, 0.23), (x + 0.04, 4.38, 0.54), "Paint", bevel=0.014)
    for start, end in ((-0.78, -0.38), (0.42, 3.18), (4.02, 4.38)):
        for left, right in ((-0.95, -0.70), (0.74, 0.95)):
            b.box((left, start, 0.26), (right, end, 0.48), "Paint", bevel=0.025)
    for f in (-0.73, 1.30, 4.30):
        b.box((-0.65, f - 0.05, 0.25), (0.68, f + 0.05, 0.48), "Chassis", bevel=0.015)
    for f in (0, WHEELBASE):
        for side in (-1, 1):
            b.cyl((side * 0.65, f, 0.33), (side * 0.83, f, 0.33), 0.052, "Steel", segs=12)
    # Rear power unit, driver station to the left of the conveyor, and walk-on front bumper.
    b.box((-0.93, -0.61, 0.71), (-0.19, 1.02, 1.01), "Enamel", bevel=0.025)
    b.box((0.72, -0.61, 0.71), (0.95, 0.98, 0.87), "Enamel", bevel=0.018)
    for side in (-1, 1):
        for k in range(7):
            f = -0.36 + k * 0.15
            b.box((side * 0.954 - 0.005, f, 0.76), (side * 0.954 + 0.005, f + 0.055, 0.82), "Dark", bevel=0.005)
        g.label(b, "BL 012", (side * 0.957, 3.20, 0.38), 0.07, side, "Enamel")
    b.box((-0.96, 1.13, 0.47), (-0.19, 2.56, 0.55), "Zinc", bevel=0.018)
    b.box((-0.80, 1.53, 0.55), (-0.44, 1.96, 0.81), "Chassis", bevel=0.02)
    g.seat(b, -0.64, 1.82, 0.81)
    b.box((-0.96, 2.34, 0.55), (-0.25, 2.58, 1.18), "Enamel", bevel=0.035)
    b.box((-0.92, 2.32, 1.06), (-0.29, 2.34, 1.18), "Plastic", bevel=0.012)
    g.steering_wheel(b, (-0.64, 2.23, 1.31), 0.145)
    for f in (1.23, 2.46):
        b.cyl((-0.98, f, 0.54), (-0.98, f, 1.12), 0.022, "Yellow", segs=10)
    b.cyl((-0.98, 1.23, 1.12), (-0.98, 1.66, 1.12), 0.022, "Yellow", segs=10)
    b.box((-1.01, 1.26, 0.30), (-0.84, 1.72, 0.38), "Zinc", bevel=0.01)
    b.cyl((-0.75, 1.05, 0.88), (-0.75, 1.05, 1.65), 0.02, "Yellow", segs=10)
    g.beacon(b, (-0.75, 1.05, 1.71))
    for x in (-0.78, 0.78):
        g.lamp(b, x, 4.39, 0.64)
    g.hitch(b, -0.87)
    parts = [b] + [g.wheel(n, c, r, 0.19) for n, c, r, _ in WHEELS]
    # Everything on the boom is generated horizontal about its rear end, then posed as one rigid assembly.
    pivot = Vector(REAR)
    boom = Part("Conveyor", REAR)
    belt = Part("BeltSurface", REAR)
    for x in (-0.385, 0.385):
        boom.box((x + REAR[0] - 0.035, REAR[1], REAR[2] - 0.17),
                 (x + REAR[0] + 0.035, REAR[1] + LENGTH, REAR[2] - 0.015), "Enamel", bevel=0.014)
    for k in range(14):
        f = REAR[1] + 0.20 + k * (LENGTH - 0.40) / 13
        boom.cyl((REAR[0] - 0.33, f, REAR[2] - 0.10),
                 (REAR[0] + 0.33, f, REAR[2] - 0.10), 0.029, "Zinc", segs=10)
    belt.box((REAR[0] - 0.30, REAR[1], REAR[2] - 0.032),
             (REAR[0] + 0.30, REAR[1] + LENGTH, REAR[2]), "Belt", bevel=0.012)
    # Low-profile cleats, joining seams, end bumpers and a narrow serrated access strip.
    for k in range(31):
        f = REAR[1] + 0.11 + k * 0.25
        belt.box((REAR[0] - 0.295, f, REAR[2]), (REAR[0] + 0.295, f + 0.008, REAR[2] + 0.002), "Plastic")
    boom.box((REAR[0] + 0.43, REAR[1] + 0.6, REAR[2] - 0.08),
             (REAR[0] + 0.65, REAR[1] + LENGTH - 0.55, REAR[2] - 0.035), "Zinc", bevel=0.01)
    for end in (0, LENGTH):
        boom.cyl((REAR[0] - 0.42, REAR[1] + end, REAR[2] - 0.04),
                 (REAR[0] + 0.42, REAR[1] + end, REAR[2] - 0.04), 0.064, "Plastic", segs=20)
    for side in (-1, 1):
        x = REAR[0] + side * 0.45
        for f in (0.70, 2.15, 3.60, 5.05, 6.50):
            boom.cyl((x, REAR[1] + f, REAR[2] - 0.05), (x, REAR[1] + f, REAR[2] + 0.64),
                     0.021, "Yellow", segs=10)
        boom.cyl((x, REAR[1] + 0.70, REAR[2] + 0.64),
                 (x, REAR[1] + 6.50, REAR[2] + 0.64), 0.022, "Yellow", segs=10)
        boom.cyl((x, REAR[1] + 0.70, REAR[2] + 0.30),
                 (x, REAR[1] + 6.50, REAR[2] + 0.30), 0.016, "Yellow", segs=8)
    rotation = Matrix.Rotation(ANGLE, 4, "X")
    for p in (boom, belt):
        bmesh_transform = Matrix.Translation(pivot) @ rotation @ Matrix.Translation(-pivot)
        import bmesh
        bmesh.ops.transform(p.bm, matrix=bmesh_transform, verts=p.bm.verts[:])
    parts += [boom, belt]
    for name, distance in (("RearRoller", 0), ("FrontRoller", LENGTH)):
        centre = pivot + rotation @ Vector((0, distance, -0.06))
        roll = Part(name, centre)
        roll.cyl(centre + Vector((-0.30, 0, 0)), centre + Vector((0.30, 0, 0)), 0.052, "Belt", segs=20)
        parts.append(roll)
    for prefix, centre, base, length, stages in LIFTS:
        top = REAR[2] + (centre - REAR[1]) * math.tan(ANGLE) - SUPPORT_OFFSET
        for i, (a, c) in enumerate(g.scissor_points(centre, base, top, length, stages)):
            parts.append(g.beam(f"{prefix}_{i}", a, c))
        head = Part(prefix + "_Head", (REAR[0], centre, top))
        # Level bearing rails receive both sliding ends. A raised central fork provides pitch clearance.
        for dx in (-0.29, 0.29):
            head.box((REAR[0] + dx - 0.035, centre - length / 2 - 0.025, top - 0.025),
                     (REAR[0] + dx + 0.035, centre + length / 2 + 0.025, top + 0.025), "Zinc", bevel=0.008)
            head.box((REAR[0] + dx - 0.035, centre - 0.055, top),
                     (REAR[0] + dx + 0.035, centre + 0.055, top + SUPPORT_OFFSET - 0.14), "PaintDark", bevel=0.01)
        head.cyl((REAR[0] - 0.43, centre, top + SUPPORT_OFFSET - 0.14),
                 (REAR[0] + 0.43, centre, top + SUPPORT_OFFSET - 0.14), 0.048, "Steel", segs=16)
        parts.append(head)
        # Bearing rails allow fixed-length arms to slide as they open.
        for x in (0.01, 0.59):
            b.box((x - 0.035, centre - length / 2 - 0.04, base - 0.03),
                  (x + 0.035, centre + length / 2 + 0.04, base + 0.025), "Zinc", bevel=0.008)
    return parts


def preview_pose(view):
    front = {"raised": 3.05, "high": 4.3}.get(view, FRONT_HEIGHT)
    angle = math.asin((front - REAR[2]) / LENGTH)
    pivot = Vector(REAR)
    delta = Matrix.Rotation(angle - ANGLE, 4, "X")
    for name in ("Conveyor", "BeltSurface", "RearRoller", "FrontRoller"):
        ob = bpy.data.objects[name]
        if "rest_location" not in ob:
            ob["rest_location"] = list(ob.location)
        ob.location = pivot + delta @ (Vector(ob["rest_location"]) - pivot)
        ob.rotation_euler.x = angle - ANGLE
    for prefix, centre, base, length, stages in LIFTS:
        top = REAR[2] + (centre - REAR[1]) * math.tan(angle) - SUPPORT_OFFSET
        # Record the actual build-pose arm angles, independent of the first requested view.
        reference = list(g.scissor_points(centre, base, REAR[2] + (centre - REAR[1]) * math.tan(ANGLE) - SUPPORT_OFFSET,
                                          length, stages))
        for i, (a, c) in enumerate(reference):
            ob = bpy.data.objects[f"{prefix}_{i}"]
            if "rest_angle" not in ob:
                ob["rest_angle"] = math.atan2(c[1] - a[1], c[0] - a[0])
                ob["rest_matrix"] = list(sum((list(row) for row in ob.matrix_world), []))
        g.pose_scissors(prefix, centre, base, top, length, stages)
        bpy.data.objects[prefix + "_Head"].location.z = top


VIEWS = {"front34": ((-8.8, 11.0, 4.0), (0, 1.7, 1.0), 47),
         "rear34": ((7.8, -8.5, 3.9), (0, 1.6, 1.0), 45),
         "raised": ((-9.4, 9.5, 4.8), (0, 1.7, 1.35), 46),
         "high": ((-10.0, 9.4, 5.2), (0, 1.6, 1.8), 44),
         "side": ((-13.5, 1.8, 3.5), (0, 1.8, 1.0), 47)}
