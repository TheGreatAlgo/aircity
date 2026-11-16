"""Generate the shared ramp worker: skinned FBX with three LODs, four skin-tone atlases, crew data and wand.

    blender -b --factory-startup --python ArtSource/Characters/RampCrew/build.py

Metres, Blender +Y forward, +Z up. Keep joints aligned with RampCrewRig.cs (see shapes.JOINTS).
The body, head, gloves, boots, cap and ear defenders are signed distance fields (shapes.py) meshed by
surface nets (sdf.py), decimated per part and skinned from smooth anatomical weight functions. Colour,
smoothness and a tangent-space normal map are baked per texel from the same fields, so the face, tape,
seams and fine relief survive decimation. One material per LOD; no downloaded assets; fixed seeds.
"""
import json
import math
import os
import struct
import sys
import time
import zlib

import bpy
import bmesh
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import sdf  # noqa: E402
import shapes  # noqa: E402
from shapes import BONES, JOINTS, PARENTS, TAILS  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "../../.."))
OUT = os.path.join(ROOT, "Assets/AirCity/Characters/RampCrew")
os.makedirs(os.path.join(HERE, "Build"), exist_ok=True)
for folder in ("Models", "Textures"):
    os.makedirs(os.path.join(OUT, folder), exist_ok=True)

ATLAS = 1024
SWATCH_V = .972                     # top strip of the atlas holds flat class swatches for the far LOD
# Triangle budgets per part (LOD0, LOD1, LOD2); gloves and boots are per side.
BUDGET = {"Body": (11300, 2500, 540), "Vest": (4400, 600, 0), "Head": (7400, 900, 140), "Glove": (1400, 170, 36),
          "Boot": (1050, 150, 40), "Cap": (1250, 200, 52), "Muffs": (900, 140, 56), "Eye": (220, 24, 0)}
FAR_H = {"Body": .012, "Head": .008, "Glove": .006, "Boot": .008, "Cap": .008, "Muffs": .008}
# Texel density relative to the clothing; the face is what players zoom in on.
DENSITY = {"Body": 1.0, "Vest": 2.0, "Head": 2.3, "Glove": 1.5, "Boot": 1.0, "Cap": 1.1, "Muffs": .9, "Eye": 2.6}
PART_IDS = {name: i for i, name in enumerate(["Body", "Head", "Glove", "Boot", "Cap", "Muffs", "Eye", "Vest"])}
OLD_JOINTS = {"Hips", "Spine", "Chest", "Neck", "Head"} | {f"{n}_{s}" for s in "LR" for n in
                                                          ("UpperArm", "Forearm", "Hand", "Thigh", "Shin", "Foot", "Toe")}
OLD_PARENTS = dict(PARENTS)
for s in "LR":
    OLD_PARENTS["UpperArm_" + s] = "Chest"


def log(*args):
    print("[RampCrew]", *args, flush=True)


def png(path, pixels):
    height, width = pixels.shape[:2]
    raw = b"".join(b"\0" + row.tobytes() for row in pixels.astype(np.uint8))
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xffffffff)
    with open(path, "wb") as stream:
        stream.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)) +
                     chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


# ------------------------------------------------------------------ meshing
def make_object(name, verts, faces, part):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], [tuple(int(i) for i in f) for f in faces])
    mesh.update()
    ob = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(ob)
    attr = mesh.attributes.new("part", "INT", "POINT")
    attr.data.foreach_set("value", np.full(len(verts), PART_IDS[part], np.int32))
    return ob


def activate(ob):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True); bpy.context.view_layer.objects.active = ob


def apply(ob, kind, **settings):
    activate(ob)
    modifier = ob.modifiers.new(kind, kind)
    for key, value in settings.items():
        setattr(modifier, key, value)
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def triangles(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def decimate(ob, budget):
    for _ in range(6):        # collapse can stop short (open borders); repeat until under budget
        count = triangles(ob)
        if count <= budget:
            break
        apply(ob, "DECIMATE", ratio=budget / count * .995, use_collapse_triangulate=True)
    apply(ob, "TRIANGULATE")


def mirror_copy(ob, name):
    copy = ob.copy(); copy.data = ob.data.copy(); copy.name = name
    bpy.context.scene.collection.objects.link(copy)
    bm = bmesh.new(); bm.from_mesh(copy.data)
    for v in bm.verts:
        v.co.x = -v.co.x
    bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    bm.to_mesh(copy.data); bm.free()
    return copy


def eye_object(name, centre):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=shapes.EYE_R)
    for v in bm.verts:
        v.co += Vector(centre)
    mesh = bpy.data.meshes.new(name); bm.to_mesh(mesh); bm.free()
    ob = bpy.data.objects.new(name, mesh); bpy.context.scene.collection.objects.link(ob)
    attr = mesh.attributes.new("part", "INT", "POINT")
    attr.data.foreach_set("value", np.full(len(mesh.vertices), PART_IDS["Eye"], np.int32))
    return ob


def surface(Part):
    """Mesh one part, reusing Build/cache when neither its definition nor the mesher changed."""
    import hashlib
    import inspect
    key = inspect.getsource(Part) + inspect.getsource(sdf)
    if Part is shapes.Vest:     # the vest is an offset of the body's upper torso
        key += inspect.getsource(shapes.Body) + repr((shapes.VEST_HEM, shapes.VEST_MID, shapes.VEST_HALF))
    for name in ("TORSO", "ARM", "SLEEVE", "LEG", "EYE", "EYE_R", "FINGERS", "CURL", "THUMB", "KNUCKLE_X"):
        key += repr(getattr(shapes, name))
    digest = hashlib.sha1(key.encode()).hexdigest()[:16]
    path = os.path.join(HERE, "Build", "cache", f"{Part.name}-{digest}.npz")
    if os.path.exists(path):
        data = np.load(path)
        return data["v"], data["q"]
    part = Part()
    verts, quads = sdf.mesh(part.sdf, part.lo, part.hi, part.h)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    np.savez(path, v=verts, q=quads)
    return verts, quads


def build_parts():
    """Per-part LOD0 meshes, plus far-LOD meshes collapsed separately from the full surface (each part
    gets its own share of the 900-triangle budget; eyes are left out of the far LOD)."""
    objects, far = [], []
    for Part in shapes.PARTS:
        part = Part(); start = time.time()
        verts, quads = surface(Part)
        ob = make_object(part.name, verts, quads, part.name)
        log(part.name, "surface", len(verts), "vertices", round(time.time() - start, 1), "s")
        # The far LOD is meshed coarsely from a slightly inflated field: thin parts (bill, fingers, hair)
        # merge into robust shells instead of pinning the collapse with many tiny islands.
        decimate(ob, BUDGET[part.name][0])
        pairs = [(ob, objects)]
        if getattr(part, "far_lod", True):
            # The body's far mesh includes the vest (Body(far=True)); the vest has no far mesh of its own.
            field = shapes.Body(far=True).sdf if part.name == "Body" else part.sdf
            fat = lambda p, f=field: f(p) - .0035
            lv, lq = sdf.mesh(fat, np.array(part.lo) - .01, np.array(part.hi) + .01, FAR_H[part.name], block=4, steps=1)
            low = make_object(part.name + "_far", lv, lq, part.name)
            decimate(low, BUDGET[part.name][2])
            pairs.append((low, far))
        log(part.name, "LOD0", triangles(ob), "far", triangles(pairs[-1][0]) if len(pairs) > 1 else 0)
        for o, out in pairs:
            o["part"] = part.name
            if part.name in ("Glove", "Boot"):
                left = mirror_copy(o, o.name + "_L"); left["part"] = part.name
                out += [o, left]
            else:
                out.append(o)
    for side, sign in (("L", -1), ("R", 1)):
        eye = eye_object("Eye_" + side, shapes.EYE * np.array([sign, 1, 1]))
        eye["part"] = "Eye"; objects.append(eye)
    return objects, far


def join(objects, name):
    bpy.ops.object.select_all(action="DESELECT")
    for ob in objects:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    ob = bpy.context.object; ob.name = name
    apply(ob, "TRIANGULATE")
    for polygon in ob.data.polygons:
        polygon.use_smooth = True
    return ob


# ------------------------------------------------------------------ hidden surface removal
def cull_hidden(ob, hide_tests):
    """Delete faces buried inside other parts (neck inside the collar, boot shafts inside trousers)."""
    bm = bmesh.new(); bm.from_mesh(ob.data)
    centres = np.array([f.calc_center_median() for f in bm.faces])
    buried = np.zeros(len(centres), bool)
    for test in hide_tests:
        buried |= test(centres)
    if buried.any():
        doomed = [f for f, b in zip(bm.faces, buried) if b]
        bmesh.ops.delete(bm, geom=doomed, context="FACES")
        loose = [v for v in bm.verts if not v.link_faces]
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(ob.data); bm.free()
    return int(buried.sum())


# ------------------------------------------------------------------ UVs
def unwrap(ob):
    """Smart-project islands per part, scale them by the part's texel density and pack below the swatches."""
    activate(ob)
    if not ob.data.uv_layers:
        ob.data.uv_layers.new(name="UVMap")
    part_of = np.empty(len(ob.data.vertices), np.int32)
    ob.data.attributes["part"].data.foreach_get("value", part_of)
    names = {v: k for k, v in PART_IDS.items()}
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(62), island_margin=.004, area_weight=0,
                             correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.object.mode_set(mode="OBJECT")
    uv = ob.data.uv_layers.active.data
    coords = np.empty(len(uv) * 2); uv.foreach_get("uv", coords); coords = coords.reshape(-1, 2)
    # Scale every face's UVs about its island-agnostic centroid by the part density (islands never span parts).
    loops_vertex = np.empty(len(uv), np.int32); ob.data.loops.foreach_get("vertex_index", loops_vertex)
    scale = np.array([DENSITY[names[part_of[v]]] for v in loops_vertex])
    coords *= scale[:, None]
    uv.foreach_set("uv", coords.ravel())
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT"); bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.pack_islands(rotate=True, margin=.0035, shape_method="CONCAVE", scale=True)
    bpy.ops.object.mode_set(mode="OBJECT")
    uv = ob.data.uv_layers.active.data
    coords = np.empty(len(uv) * 2); uv.foreach_get("uv", coords)
    coords = coords.reshape(-1, 2) * SWATCH_V * .995
    uv.foreach_set("uv", coords.ravel())


# ------------------------------------------------------------------ baking
def mesh_arrays(ob):
    me = ob.data
    me.calc_loop_triangles()
    tris = np.empty(len(me.loop_triangles) * 3, np.int32); me.loop_triangles.foreach_get("loops", tris)
    tris = tris.reshape(-1, 3)
    loop_vert = np.empty(len(me.loops), np.int32); me.loops.foreach_get("vertex_index", loop_vert)
    co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    normals = np.empty(len(me.loops) * 3); me.corner_normals.foreach_get("vector", normals)
    normals = normals.reshape(-1, 3)
    uv = np.empty(len(me.loops) * 2); me.uv_layers.active.data.foreach_get("uv", uv); uv = uv.reshape(-1, 2)
    part = np.empty(len(me.vertices), np.int32); me.attributes["part"].data.foreach_get("value", part)
    return tris, loop_vert, co, normals, uv, part


def rasterize(tris, uv, size):
    """Texel -> (triangle, barycentrics), sampling texel centres; triangles own texels they cover."""
    owner = np.full((size, size), -1, np.int32)
    bary = np.zeros((size, size, 3), np.float32)
    pix = uv[tris] * size - .5                         # (T, 3, 2) in texel-centre coordinates
    lo = np.floor(pix.min(1)).astype(int); hi = np.ceil(pix.max(1)).astype(int)
    lo = np.clip(lo, 0, size - 1); hi = np.clip(hi, 0, size - 1)
    for t in range(len(tris)):
        (x0, y0), (x1, y1) = lo[t], hi[t]
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        a, b, c = pix[t]
        v0, v1 = b - a, c - a
        den = v0[0] * v1[1] - v1[0] * v0[1]
        if abs(den) < 1e-12:
            continue
        px, py = xs - a[0], ys - a[1]
        l1 = (px * v1[1] - v1[0] * py) / den
        l2 = (v0[0] * py - px * v0[1]) / den
        l0 = 1 - l1 - l2
        inside = (l0 >= -1e-4) & (l1 >= -1e-4) & (l2 >= -1e-4)
        owner[ys[inside], xs[inside]] = t
        bary[ys[inside], xs[inside]] = np.stack([l0[inside], l1[inside], l2[inside]], -1)
    return owner, bary


def dilate(image, filled, passes):
    image = image.copy(); filled = filled.copy()
    for _ in range(passes):
        acc = np.zeros_like(image); count = np.zeros(filled.shape)
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, 1), (-1, 1), (1, -1)):
            shifted = np.roll(np.roll(image, dy, 0), dx, 1)
            mask = np.roll(np.roll(filled, dy, 0), dx, 1)
            acc += shifted * mask[..., None]; count += mask
        grow = (~filled) & (count > 0)
        image[grow] = acc[grow] / count[grow][:, None]
        filled |= grow
    return image, filled


def bake(ob, tones=4):
    tris, loop_vert, co, normals, uv, part = mesh_arrays(ob)
    owner, bary = rasterize(tris, uv, ATLAS)
    texels = np.argwhere(owner >= 0)
    t = owner[texels[:, 0], texels[:, 1]]; w = bary[texels[:, 0], texels[:, 1]].astype(float)
    corners = tris[t]                                      # loop indices
    P = np.einsum("nk,nkd->nd", w, co[loop_vert[corners]])
    N = np.einsum("nk,nkd->nd", w, normals[corners]); N /= np.linalg.norm(N, axis=1, keepdims=True)
    # Per-triangle tangent frame from UV derivatives (intrinsic, so it survives the FBX axis swap).
    p0, p1, p2 = (co[loop_vert[tris[:, k]]] for k in range(3))
    u0, u1, u2 = (uv[tris[:, k]] for k in range(3))
    e1, e2 = p1 - p0, p2 - p0; d1, d2 = u1 - u0, u2 - u0
    r = d1[:, 0] * d2[:, 1] - d2[:, 0] * d1[:, 1]
    r = np.where(np.abs(r) < 1e-12, 1e-12, r)
    T = (e1 * d2[:, 1:2] - e2 * d1[:, 1:2]) / r[:, None]
    B = (e2 * d1[:, 0:1] - e1 * d2[:, 0:1]) / r[:, None]
    T, B = T[t], B[t]
    T -= N * np.sum(T * N, 1, keepdims=True); T /= np.maximum(np.linalg.norm(T, axis=1, keepdims=True), 1e-12)
    B -= N * np.sum(B * N, 1, keepdims=True) + T * np.sum(B * T, 1, keepdims=True)
    B /= np.maximum(np.linalg.norm(B, axis=1, keepdims=True), 1e-12)
    pid = part[loop_vert[corners[:, 0]]]
    names = {v: k for k, v in PART_IDS.items()}
    albedo = np.zeros((tones, ATLAS, ATLAS, 3)); normal_map = np.zeros((ATLAS, ATLAS, 3)); normal_map[..., 2] = 1
    gloss = np.zeros((ATLAS, ATLAS)); metal = np.zeros((ATLAS, ATLAS))
    classes = np.full((ATLAS, ATLAS), -1, np.int32)
    instances = {Part.name: Part() for Part in shapes.PARTS}
    for pid_value in np.unique(pid):
        name = names[int(pid_value)]
        sel = np.nonzero(pid == pid_value)[0]
        p, n_low = P[sel], N[sel]
        rows, cols = texels[sel, 0], texels[sel, 1]
        start = time.time()
        if name == "Eye":
            centre = np.where(p[:, :1] < 0, shapes.EYE * np.array([-1, 1, 1]), shapes.EYE)
            d = p - centre; n_hi = d / np.linalg.norm(d, axis=1, keepdims=True)
            p_true = centre + n_hi * shapes.EYE_R
            for tone in range(tones):
                paint = shapes.eye_paint(p_true, tone, centre)
                albedo[tone, rows, cols] = paint.rgb
            relief = None
        else:
            inst = instances[name]
            side = np.where(p[:, 0] < 0, -1.0, 1.0) if name in ("Glove", "Boot") else np.ones(len(p))
            q = p * np.stack([side, np.ones(len(p)), np.ones(len(p))], 1)   # mirrored parts paint as right
            q = sdf.project(inst.sdf, q, steps=3)
            g = sdf.gradient(inst.sdf, q, 1e-4); n_hi = g / np.maximum(np.linalg.norm(g, axis=1, keepdims=True), 1e-9)
            # Relief: perturb the true normal by the tangential gradient of a small height field.
            eps = 2.5e-4
            hg = np.stack([(inst.relief(q + e) - inst.relief(q - e)) / (2 * eps)
                           for e in np.eye(3) * eps], 1)
            hg -= n_hi * np.sum(hg * n_hi, 1, keepdims=True)
            n_hi = n_hi - hg; n_hi /= np.linalg.norm(n_hi, axis=1, keepdims=True)
            n_true = sdf_normal(inst, q)
            for tone in range(tones):
                paint = inst.paint(q, n_true, tone)
                albedo[tone, rows, cols] = paint.rgb
            n_hi[:, 0] *= side                                               # back to the mirrored side
        gloss[rows, cols] = paint.gloss; metal[rows, cols] = paint.metal; classes[rows, cols] = paint.cls
        tb = np.stack([np.sum(n_hi * T[sel], 1), np.sum(n_hi * B[sel], 1), np.sum(n_hi * n_low, 1)], 1)
        tb[:, 2] = np.maximum(tb[:, 2], .05); tb /= np.linalg.norm(tb, axis=1, keepdims=True)
        normal_map[rows, cols] = tb
        log("baked", name, len(sel), "texels", round(time.time() - start, 1), "s")
    filled = owner >= 0
    log("atlas coverage", round(filled.mean() * 100, 1), "%")
    # Class swatches for the far LOD: mean colour of each class, painted into the reserved top strip.
    swatch_rows = slice(int(ATLAS * SWATCH_V) + 3, ATLAS)
    width = ATLAS // len(shapes.CLASSES)
    swatches = {}
    for k, name in enumerate(shapes.CLASSES):
        mask = classes == k
        cols = slice(k * width + 2, (k + 1) * width - 2)
        swatches[name] = ((k + .5) * width / ATLAS, (int(ATLAS * SWATCH_V) + 3 + ATLAS) / 2 / ATLAS)
        for tone in range(tones):
            colour = albedo[tone][mask].mean(0) if mask.any() else np.array([.5, .5, .5])
            albedo[tone, swatch_rows, cols] = colour
        gloss[swatch_rows, cols] = gloss[mask].mean() if mask.any() else .3
        metal[swatch_rows, cols] = metal[mask].mean() if mask.any() else 0
        filled[swatch_rows, cols] = True
    out = []
    for tone in range(tones):
        image, _ = dilate(albedo[tone], filled, 12)
        out.append(image)
    nm, _ = dilate(normal_map, filled, 12)
    nm[~filled & (np.linalg.norm(nm, axis=2) < .5)] = (0, 0, 1)
    gm, _ = dilate(np.stack([gloss, metal, np.zeros_like(gloss)], -1), filled, 12)
    for tone, image in enumerate(out):
        rgba = np.dstack([np.clip(image, 0, 1) * 255, np.full((ATLAS, ATLAS), 255)])
        png(os.path.join(OUT, "Textures", f"RampCrew_{tone}.png"), rgba[::-1])
    nm /= np.maximum(np.linalg.norm(nm, axis=2, keepdims=True), 1e-9)
    png(os.path.join(OUT, "Textures", "RampCrew_Normal.png"),
        np.dstack([(nm * .5 + .5) * 255, np.full((ATLAS, ATLAS), 255)])[::-1])
    png(os.path.join(OUT, "Textures", "RampCrew_MetalGloss.png"),
        np.dstack([gm[..., 1] * 255, np.zeros((ATLAS, ATLAS)), np.zeros((ATLAS, ATLAS)), gm[..., 0] * 255])[::-1])
    return swatches


def sdf_normal(inst, q):
    g = sdf.gradient(inst.sdf, q, 1e-4)
    return g / np.maximum(np.linalg.norm(g, axis=1, keepdims=True), 1e-9)


def classify_faces(ob):
    """Dominant paint class per face, for the far LOD's flat swatches."""
    me = ob.data
    part = np.empty(len(me.vertices), np.int32); me.attributes["part"].data.foreach_get("value", part)
    names = {v: k for k, v in PART_IDS.items()}
    centres = np.array([p.center for p in me.polygons]); pids = np.array([part[p.vertices[0]] for p in me.polygons])
    result = np.zeros(len(centres), np.int32)
    instances = {Part.name: Part() for Part in shapes.PARTS}
    instances["Body"] = shapes.Body(far=True)
    for pid_value in np.unique(pids):
        name = names[int(pid_value)]; sel = np.nonzero(pids == pid_value)[0]
        q = centres[sel].copy()
        if name == "Eye":
            result[sel] = shapes.CLS["sclera"]; continue
        q[:, 0] = np.abs(q[:, 0]) if name in ("Glove", "Boot") else q[:, 0]
        inst = instances[name]
        q = sdf.project(inst.sdf, q, steps=3)
        result[sel] = inst.paint(q, sdf_normal(inst, q), 0).cls
    # A few pixels tall, stripes and small features turn into noise: keep the garment colour instead.
    C = shapes.CLS
    z = centres[:, 2]
    result[(result == C["tape"]) & (z > .95)] = C["vest"]
    result[(result == C["tape"]) & (z <= .95)] = C["trousers"]
    for small, into in (("lips", "skin"), ("zip", "vest"), ("sclera", "skin"), ("iris", "skin"), ("patch", "trousers"),
                        ("metal", "belt"), ("coating", "glove")):
        result[result == C[small]] = C[into]
    return result


# ------------------------------------------------------------------ skin weights
def skin(ob):
    me = ob.data
    co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    part = np.empty(len(me.vertices), np.int32); me.attributes["part"].data.foreach_get("value", part)
    names = {v: k for k, v in PART_IDS.items()}
    for bone in BONES:
        if bone not in ob.vertex_groups:
            ob.vertex_groups.new(name=bone)
    for pid_value in np.unique(part):
        sel = np.nonzero(part == pid_value)[0]
        W = shapes.weights(co[sel], names[int(pid_value)])
        matrix = np.stack([W[b] for b in BONES], 1)
        matrix = np.clip(matrix, 0, None)
        # Four strongest influences, normalised; tiny weights are dropped as Unity would.
        order = np.argsort(-matrix, 1)[:, :4]
        top = np.take_along_axis(matrix, order, 1)
        top[top < .01] = 0
        top /= np.maximum(top.sum(1, keepdims=True), 1e-9)
        for column in range(4):
            for b_index in np.unique(order[:, column]):
                rows = np.nonzero((order[:, column] == b_index) & (top[:, column] > 0))[0]
                group = ob.vertex_groups[BONES[b_index]]
                for value in np.unique(np.round(top[rows, column], 4)):
                    members = sel[rows[np.round(top[rows, column], 4) == value]]
                    group.add(members.tolist(), float(value), "REPLACE")


# ------------------------------------------------------------------ main
def main():
    start = time.time()
    bpy.ops.object.select_all(action="SELECT"); bpy.ops.object.delete(use_global=False)
    parts, far = build_parts()
    # Remove faces that are always covered: the neck inside the collar and the boot shafts inside trousers.
    body_inst = shapes.Body()
    for ob in parts + far:
        if ob["part"] == "Head":
            n = cull_hidden(ob, [lambda c: (c[:, 2] < 1.465) & (body_inst.sdf(c) < -.002)])
            log("culled", n, "neck faces")
        if ob["part"] == "Boot":
            n = cull_hidden(ob, [lambda c: (c[:, 2] > .12) & (body_inst.sdf(c) < -.002)])
            log("culled", n, ob.name, "faces")
        if ob.name == "Body":
            # Shirt fully under the vest (more than 12 mm in from its cut edges) can never be seen.
            n = cull_hidden(ob, [lambda c: (body_inst.vest_region(c) < -.012) & (np.abs(body_inst.upper(c)) < .004)])
            log("culled", n, "shirt faces under the vest")
    lod0 = join(parts, "Body_LOD0")
    unwrap(lod0)
    swatches = bake(lod0)
    log("textures", round(time.time() - start, 1), "s")
    material = bpy.data.materials.new("RampCrew")
    lod0.data.materials.clear(); lod0.data.materials.append(material)

    # LOD1: collapse the textured LOD0 (UVs survive), eyes reduced to a few triangles.
    lod1 = lod0.copy(); lod1.data = lod0.data.copy(); lod1.name = "Body_LOD1"
    bpy.context.scene.collection.objects.link(lod1)
    decimate(lod1, sum(b[1] for b in BUDGET.values()) + BUDGET["Glove"][1] + BUDGET["Boot"][1] + BUDGET["Eye"][1])
    # LOD2: per-part collapses joined, then faces mapped to flat class swatches.
    lod2 = join(far, "Body_LOD2")
    lod2.data.materials.clear(); lod2.data.materials.append(material)
    lod2.data.uv_layers.new(name="UVMap")
    classes = classify_faces(lod2)
    uv = lod2.data.uv_layers.active.data
    for polygon, cls in zip(lod2.data.polygons, classes):
        target = swatches[shapes.CLASSES[cls]]
        for li in polygon.loop_indices:
            uv[li].uv = target
    lods = [lod0, lod1, lod2]
    for ob in lods:
        skin(ob)
        log(ob.name, "vertices", len(ob.data.vertices), "triangles", triangles(ob))

    armature = bpy.data.armatures.new("RampCrewRig")
    rig = bpy.data.objects.new("Skeleton", armature); bpy.context.scene.collection.objects.link(rig)
    activate(rig)
    bpy.ops.object.mode_set(mode="EDIT")
    for name, position in JOINTS.items():
        bone = armature.edit_bones.new(name); bone.head = Vector(position)
        if name in OLD_JOINTS:
            # Existing bones keep their historical orientation (first child of the original hierarchy).
            children = [n for n, p in OLD_PARENTS.items() if p == name and n in OLD_JOINTS]
            bone.tail = Vector(JOINTS[children[0]]) if children else bone.head + Vector((0, 0, .13))
        elif name in TAILS:
            bone.tail = Vector(TAILS[name])
        else:
            children = [n for n, p in PARENTS.items() if p == name]
            bone.tail = Vector(JOINTS[children[0]])
        if (bone.tail - bone.head).length < .01:
            bone.tail.z += .10
    for name, parent in PARENTS.items():
        if parent:
            armature.edit_bones[name].parent = armature.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    for ob in lods:
        modifier = ob.modifiers.new("Skin", "ARMATURE"); modifier.object = rig
        ob.parent = rig
    bpy.ops.object.select_all(action="DESELECT")
    for ob in [rig] + lods:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "Models", "RampCrew.fbx"), use_selection=True,
                             object_types={"ARMATURE", "MESH"}, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False, mesh_smooth_type="OFF",
                             use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False, path_mode="STRIP")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "Build", "RampCrew.blend"))

    profile = {"height": 1.78, "hips": .94, "minCrouch": .78, "walkSpeed": 1.2, "returnSpeed": 1.4,
               "pickSeconds": .65, "liftSeconds": .45, "turnSeconds": .70, "placeSeconds": .65,
               "turnBackSeconds": .5, "settleSeconds": .3, "holdPickSeconds": .55, "holdLiftSeconds": .4,
               "holdStowSeconds": .75, "holdResetSeconds": .55, "boardingSpeed": .9}
    with open(os.path.join(OUT, "RampCrew.crew"), "w") as stream:
        json.dump(profile, stream, indent=2)
    wand()
    log("done in", round(time.time() - start, 1), "s")


# ------------------------------------------------------------------ marshalling wand (unchanged design)
WAND_COLORS = ["344452", "293745", "c1cf42", "b6bcb5", "32383b", "292c2b", "b58567", "885c46",
               "c0967a", "293e4b", "8d9693", "464e4c", "d9d9c9", "475663", "403c33", "292c29"]


def wand():
    """A separate shared wand attached only to marshallers. Palette UVs keep the grip dark and the tube luminous."""
    def tube(verts, faces, colors, a, b, radii, color, segments=12):
        a, b = Vector(a), Vector(b)
        axis = (b - a).normalized(); u = axis.cross(Vector((0, 1, 0))).normalized()
        if u.length < .01:
            u = axis.cross(Vector((1, 0, 0))).normalized()
        v = axis.cross(u).normalized()
        rings = []
        for t, ru, rv in radii:
            rings.append([len(verts) + i for i in range(segments)])
            for i in range(segments):
                angle = i * math.tau / segments
                verts.append(tuple(a.lerp(b, t) + u * (ru * math.cos(angle)) + v * (rv * math.sin(angle))))
        for r0, r1 in zip(rings, rings[1:]):
            for k in range(segments):
                faces.append((r0[k], r0[(k + 1) % segments], r1[(k + 1) % segments], r1[k])); colors.append(color)
        faces.append(tuple(rings[0][::-1])); colors.append(color)
        faces.append(tuple(rings[-1])); colors.append(color)
    verts, faces, colors = [], [], []
    tube(verts, faces, colors, (0, 0, 0), (0, 0, .12), [(0, .018, .018), (1, .018, .018)], 4)
    tube(verts, faces, colors, (0, 0, .12), (0, 0, .42), [(0, .022, .022), (.94, .022, .022), (1, .012, .012)], 2)
    mesh = bpy.data.meshes.new("MarshallingWand"); mesh.from_pydata(verts, [], faces); mesh.update()
    uv = mesh.uv_layers.new(name="UVMap")
    for polygon, color in zip(mesh.polygons, colors):
        polygon.use_smooth = True
        for li in polygon.loop_indices:
            uv.data[li].uv = ((color + .5) / 16, .5)
    ob = bpy.data.objects.new("MarshallingWand", mesh); bpy.context.scene.collection.objects.link(ob)
    activate(ob)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "Models", "MarshallingWand.fbx"), use_selection=True,
                             object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, use_mesh_modifiers=False, bake_anim=False)
    palette = WAND_COLORS.copy(); palette[2] = "ff621c"
    def wand_palette(colors):
        pixels = np.empty((16, 256, 4), dtype=np.uint8)
        for i, color in enumerate(colors):
            pixels[:, i * 16:(i + 1) * 16] = (*bytes.fromhex(color), 255)
        return pixels
    png(os.path.join(OUT, "Textures", "Wand_Albedo.png"), wand_palette(palette))
    emission = ["000000"] * 16; emission[2] = "ff621c"
    png(os.path.join(OUT, "Textures", "Wand_Emission.png"), wand_palette(emission))


main()
