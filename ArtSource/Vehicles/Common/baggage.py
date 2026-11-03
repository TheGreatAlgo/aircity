"""Shared construction and finishes for the first baggage equipment family.

Metres, (right, forward, up). Equipment origins are on the ground below the rear axle.
This is mesh geometry, including the reusable bags; no raster concept art is involved.
"""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

from kit import Part, M

MATS = {
    "Paint": ("#235185", 0.0, 0.38, False),
    "PaintDark": ("#18364F", 0.0, 0.44, False),
    "Enamel": ("#DDE2E1", 0.0, 0.35, False),
    "Zinc": ("#A6ADB1", 0.78, 0.48, False),
    "Yellow": ("#EAB526", 0.0, 0.4, False),
    "Curtain": ("#284B69", 0.0, 0.78, False),
    "Webbing": ("#223748", 0.0, 0.9, False),
    "Belt": ("#22282B", 0.0, 0.91, False),
    "BagBlue": ("#244B62", 0.0, 0.42, False),
    "BagRed": ("#8C3B38", 0.0, 0.55, False),
    "BagTan": ("#9D8060", 0.0, 0.67, False),
    "BagFabric": ("#42494D", 0.0, 0.92, False),
    "Tag": ("#E8E4D8", 0.0, 0.87, False),
}
GRIME = "#645F53"


def grime(pos, ao):
    low = np.exp(-np.maximum(pos[..., 2], 0) / 0.55)
    noise = 0.5 + 0.5 * np.sin(pos[..., 0] * 31 + np.sin(pos[..., 1] * 13))
    return np.clip(0.045 * low + 0.055 * (1 - ao) + 0.018 * low * noise, 0, 0.18)


def wheel(name, centre, radius, width, steerable=False):
    p = Part(name, centre)
    p.wheel(centre, radius, width, radius * 0.56, tread=False, rim="Zinc", lugs=5,
            side=1 if centre[0] > 0 else -1)
    return p


def hitch(p, forward, height=0.38, front=False):
    """Pin-and-clevis coupling, with its mating point at (0, forward, height)."""
    sign = 1 if front else -1
    p.box((-0.14, forward - sign * 0.15, height - 0.10),
          (0.14, forward + sign * 0.03, height + 0.10), "Chassis", bevel=0.015)
    for z in (height - 0.055, height + 0.055):
        p.box((-0.11, forward - 0.07, z - 0.018), (0.11, forward + 0.09, z + 0.018),
              "Zinc", bevel=0.012)
    p.cyl((0, forward, height - 0.10), (0, forward, height + 0.14), 0.022, "Steel", segs=12)
    p.cyl((0, forward, height + 0.13), (0.085, forward, height + 0.13), 0.014, "Yellow", segs=8)


def beacon(p, centre):
    x, f, z = centre
    p.cyl((x, f, z - 0.07), (x, f, z - 0.035), 0.072, "Dark", segs=20)
    p.cyl((x, f, z - 0.035), (x, f, z + 0.075), 0.059, "LensAmber", segs=20,
          bevel=0.015)


def lamp(p, x, f, z, front=True):
    d = 1 if front else -1
    p.box((x - 0.095, f - 0.022, z - 0.065), (x + 0.095, f + 0.022, z + 0.065),
          "Dark", bevel=0.022)
    p.box((x - 0.075, f + d * 0.024 - 0.005, z - 0.045),
          (x + 0.075, f + d * 0.024 + 0.005, z + 0.045),
          "LensWhite" if front else "LensRed", bevel=0.012)


def label(p, text, centre, size=0.10, side=1, mat="Enamel"):
    """Bake real small lettering into the vehicle atlas from simple font geometry."""
    curve = bpy.data.curves.new("Label", "FONT")
    curve.body, curve.size = text, size
    curve.align_x, curve.align_y = "CENTER", "CENTER"
    curve.resolution_u = 1
    ob = bpy.data.objects.new("Label", curve)
    bpy.context.scene.collection.objects.link(ob)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(ob.evaluated_get(depsgraph))
    # Text +X follows forward on the right side, aft on the left; +Y goes up.
    rot = Matrix(((0, 0, side, 0), (side, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
    mesh.transform(Matrix.Translation(Vector(centre)) @ rot)
    before = set(p.bm.faces)
    p.bm.from_mesh(mesh)
    mi = p.mat(mat)
    for face in set(p.bm.faces) - before:
        face.material_index = mi
    bpy.data.meshes.remove(mesh)
    bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.curves.remove(curve)


def seat(p, x, f, z):
    p.box((x - 0.21, f - 0.23, z), (x + 0.21, f + 0.23, z + 0.12), "Plastic", bevel=0.06)
    p.box((x - 0.23, f - 0.27, z + 0.07), (x + 0.23, f - 0.15, z + 0.60), "Plastic", bevel=0.065)
    for dx in (-0.14, 0.14):
        p.box((x + dx - 0.014, f - 0.285, z + 0.18),
              (x + dx + 0.014, f - 0.277, z + 0.47), "Dark", bevel=0.006)


def steering_wheel(p, centre, radius=0.15):
    x, f, z = centre
    # Wheel lies in a tilted plane; the rim is a closed tube rather than a solid disc.
    points = [(x + radius * math.cos(k * math.tau / 24),
               f + radius * math.sin(k * math.tau / 24) * 0.65,
               z + radius * math.sin(k * math.tau / 24) * 0.76) for k in range(25)]
    for a, b in zip(points, points[1:]):
        p.cyl(a, b, 0.013, "Plastic", segs=8)
    for k in range(3):
        p.cyl(centre, points[k * 8], 0.013, "Plastic", segs=8)
    p.cyl((x, f - 0.15, z - 0.30), centre, 0.022, "Chassis", segs=10)


def suitcase(p, centre=(0, 0, 0), size=(0.48, 0.29, 0.72), material="BagBlue", yaw=0, soft=False):
    """Upright suitcase; centre is the ground contact, not the middle of the shell."""
    w, d, h = size
    pose = M(*centre, yaw=yaw)
    bevel = min(w, d) * (0.20 if soft else 0.17)
    p.box((-w / 2, -d / 2, 0.055), (w / 2, d / 2, h - 0.055), material,
          bevel=bevel, segments=3, matrix=pose)
    # Zipper band and face ribs sit on the shell rather than oversized toy ridges.
    p.box((-w / 2 - 0.002, -0.013, 0.10), (w / 2 + 0.002, 0.013, h - 0.10),
          "Dark", bevel=0.012, matrix=pose)
    for side in (-1, 1):
        if not soft:
            for k in range(5):
                x = (k - 2) * w / 7
                p.box((x - 0.009, side * d / 2 - 0.006, 0.14),
                      (x + 0.009, side * d / 2 + 0.006, h - 0.14), material,
                      bevel=0.008, matrix=pose)
        for x in (-w * 0.34, w * 0.34):
            p.cyl((x - 0.025, side * d * 0.31, 0.032),
                  (x + 0.025, side * d * 0.31, 0.032), 0.031, "Plastic", segs=10, matrix=pose)
    p.box((-0.09, -0.025, h - 0.06), (0.09, 0.025, h - 0.025), "Plastic", bevel=0.012, matrix=pose)
    for x in (-0.072, 0.072):
        p.cyl((x, 0, h - 0.07), (x, 0, h - 0.005), 0.009, "Metal", segs=8, matrix=pose)
    p.box((-0.09, -0.02, h - 0.018), (0.09, 0.02, h + 0.005), "Plastic", bevel=0.01, matrix=pose)
    p.box((0.025, -0.034, h - 0.12), (0.073, -0.027, h - 0.025), "Tag", matrix=pose)


def duffel(p, centre=(0, 0, 0), yaw=0):
    pose = M(*centre, yaw=yaw)
    p.box((-0.29, -0.155, 0.015), (0.29, 0.155, 0.285), "BagFabric", bevel=0.105, segments=4, matrix=pose)
    for x in (-0.16, 0.16):
        for f in (-0.14, 0.14):
            p.box((x - 0.017, f - 0.012, 0.06), (x + 0.017, f + 0.012, 0.26), "Webbing", matrix=pose)
        p.cyl((x, -0.13, 0.245), (x, -0.06, 0.36), 0.013, "Webbing", segs=8, matrix=pose)
        p.cyl((x, -0.06, 0.36), (x, 0.06, 0.36), 0.013, "Webbing", segs=8, matrix=pose)
        p.cyl((x, 0.06, 0.36), (x, 0.13, 0.245), 0.013, "Webbing", segs=8, matrix=pose)


def scissor_points(centre, base, top, length, stages):
    """Fixed-length crossed arms, stacked vertically with sliding end bearings."""
    step = (top - base) / stages
    if step <= 0 or step >= length:
        raise ValueError(f"scissor outside its mechanical range: {step} / {length}")
    half = math.sqrt(length * length - step * step) / 2
    for stage in range(stages):
        z = base + step * stage
        yield ((centre - half, z), (centre + half, z + step))
        yield ((centre + half, z), (centre - half, z + step))


def beam(name, a, b, x=0.30, width=0.065):
    """Pair of fixed-length rectangular arms, initial mesh points along +forward."""
    f0, z0 = a
    f1, z1 = b
    length = math.hypot(f1 - f0, z1 - z0)
    p = Part(name, (x, f0, z0))
    pose = M(x, f0, z0, pitch=math.degrees(math.atan2(z1 - z0, f1 - f0)))
    # Crossing members occupy adjacent planes, rather than intersecting as solid steel at the pin.
    track = 0.21 if int(name.rsplit("_", 1)[-1]) % 2 else 0.29
    for dx in (-track, track):
        p.box((dx - width / 2, 0, -width / 2), (dx + width / 2, length, width / 2),
              "PaintDark", bevel=0.009, matrix=pose)
        for f in (0, length):
            p.cyl((dx - 0.052, f, 0), (dx + 0.052, f, 0), 0.045, "Zinc", segs=12, matrix=pose)
    return p


def pose_scissors(prefix, centre, base, top, length, stages, x=0.30):
    for i, (a, b) in enumerate(scissor_points(centre, base, top, length, stages)):
        ob = bpy.data.objects.get(f"{prefix}_{i}")
        if ob is None:
            continue
        # Meshes were built in their reference pose. Transform the complete arm rigidly.
        if "rest_matrix" not in ob:
            ob["rest_matrix"] = list(sum((list(row) for row in ob.matrix_world), []))
            ob["rest_angle"] = math.atan2(b[1] - a[1], b[0] - a[0])
        rest = Matrix([ob["rest_matrix"][k:k + 4] for k in range(0, 16, 4)])
        angle = math.atan2(b[1] - a[1], b[0] - a[0])
        ob.matrix_world = (Matrix.Translation(Vector((x, a[0], a[1]))) @
                           Matrix.Rotation(angle - ob["rest_angle"], 4, "X") @
                           Matrix.Translation(-rest.translation) @ rest)
