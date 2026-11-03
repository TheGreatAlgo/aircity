"""Reusable roller beds and level scissor platforms for the container equipment family."""
import math
import bpy
from mathutils import Vector, Matrix
from kit import Part, M
import baggage as g


def roller_bed(p, centre, width, length, height, rails=True):
    x, f = centre
    for side in (-1, 1):
        p.box((x + side * (width / 2 - .04) - .035, f - length / 2, height - .16),
              (x + side * (width / 2 - .04) + .035, f + length / 2, height - .025), "Paint", bevel=.012)
    for k in range(int(length / .18)):
        y = f - length / 2 + .10 + k * .18
        # Split rollers leave a service spine and reveal the supporting steel underneath.
        for side in (-1, 1):
            p.cyl((x + side * .06, y, height - .045), (x + side * (width / 2 - .08), y, height - .045),
                  .045, "Zinc", segs=12)
        p.box((x - .055, y - .05, height - .13), (x + .055, y + .05, height - .06), "Chassis", bevel=.008)
    for y in (f - length / 2, f + length / 2):
        p.box((x - width / 2, y - .035, height - .15), (x + width / 2, y + .035, height - .065), "Zinc", bevel=.01)
    if rails:
        for side in (-1, 1):
            p.box((x + side * width / 2 - .025, f - length / 2, height),
                  (x + side * width / 2 + .025, f + length / 2, height + .12), "Yellow", bevel=.01)


def lift_arm(name, a, b, track=.88):
    f0, z0 = a; f1, z1 = b
    p = Part(name, (0, f0, z0))
    pose = M(0, f0, z0, pitch=math.degrees(math.atan2(z1 - z0, f1 - f0)))
    length = math.hypot(f1 - f0, z1 - z0)
    offset = .04 if int(name.rsplit("_", 1)[-1]) % 2 else -.04
    for side in (-1, 1):
        x = side * (track + offset)
        p.box((x - .045, 0, -.055), (x + .045, length, .055), "PaintDark", bevel=.008, matrix=pose)
        for y in (0, length / 2, length):
            p.cyl((x - .065, y, 0), (x + .065, y, 0), .06, "Zinc", segs=12, matrix=pose)
    return p


def platform(prefix, centre, height, width, length, arm_length, stages, base):
    p = Part(prefix, (0, centre, height))
    roller_bed(p, (0, centre), width, length, height, prefix != "MainPlatform")
    # The side walkways carry rails; transfer ends remain open and have retractable stops.
    gate = Part(prefix + "Gate", (0, centre, height)) if prefix == "MainPlatform" else None
    if gate:
        p.box((width / 2 - .025, centre - length / 2, height), (width / 2 + .025, centre + length / 2, height + .12), "Yellow", bevel=.008)
        gate.box((-width / 2 - .025, centre - length / 2, height), (-width / 2 + .025, centre + length / 2, height + .12), "Yellow", bevel=.008)
    for side in (-1, 1):
        x = side * (width / 2 + .19)
        p.box((x - .14, centre - length / 2, height - .08), (x + .14, centre + length / 2, height - .025), "Zinc", bevel=.008)
        rail = gate if side == -1 and gate else p
        for y in (centre - length / 2 + .12, centre, centre + length / 2 - .12):
            rail.cyl((x, y, height - .04), (x, y, height + 1.0), .022, "Yellow", segs=10)
        for z in (.47, 1.):
            rail.cyl((x, centre - length / 2 + .12, height + z), (x, centre + length / 2 - .12, height + z), .022, "Yellow", segs=10)
    result = [p] + ([gate] if gate else [])
    for k, (a, b) in enumerate(g.scissor_points(centre, base, height - .17, arm_length, stages)):
        result.append(lift_arm(prefix + "Lift_" + str(k), a, b))
    for end, y in (("Rear", centre - length / 2 + .09), ("Front", centre + length / 2 - .09)):
        stop = Part(prefix + "Stop" + end, (0, y, height))
        for x in (-.65, .65):
            stop.box((x - .11, y - .025, height - .025), (x + .11, y + .025, height + .13), "Yellow", bevel=.008)
        result.append(stop)
    return result


def pose_platform(prefix, centre, height, reference, length, stages, base):
    delta = height - reference
    for name in (prefix, prefix + "StopRear", prefix + "StopFront", prefix + "Gate"):
        ob = bpy.data.objects.get(name)
        if ob:
            if "rest_location" not in ob: ob["rest_location"] = list(ob.location)
            ob.location = Vector(ob["rest_location"]) + Vector((0, 0, delta))
    if prefix == "MainPlatform":
        bridge = bpy.data.objects.get("SideBridge")
        if bridge:
            if "rest_location" not in bridge: bridge["rest_location"] = list(bridge.location)
            bridge.location = Vector(bridge["rest_location"]) + Vector((0, 0, delta - .14))
    reference_points = list(g.scissor_points(centre, base, reference - .17, length, stages))
    for i, (a, b) in enumerate(g.scissor_points(centre, base, height - .17, length, stages)):
        ob = bpy.data.objects[prefix + "Lift_" + str(i)]
        if "rest_matrix" not in ob:
            ob["rest_matrix"] = list(sum((list(row) for row in ob.matrix_world), []))
            ra, rb = reference_points[i]
            ob["rest_angle"] = math.atan2(rb[1] - ra[1], rb[0] - ra[0])
        rest = Matrix([ob["rest_matrix"][k:k + 4] for k in range(0, 16, 4)])
        ob.matrix_world = (Matrix.Translation(Vector((0, a[0], a[1]))) @
                           Matrix.Rotation(math.atan2(b[1] - a[1], b[0] - a[0]) - ob["rest_angle"], 4, "X") @
                           Matrix.Translation(-rest.translation) @ rest)
