"""The aircraft pipeline shared by every model. ArtSource/Aircraft/build.py and finish.py run its two stages
on a model definition (<Model>/model.py, see ArtSource/README.md for what it provides).

Stage 1 (stage1): geometry, UV atlas packing, Cycles bakes (position / normal / part / AO).
Stage 2 (stage2): livery masks, shading + dirt + logo maps, registration patch, LODs, collider, ground
shadow, FBX, the <Model>.aircraft profile (painting + gameplay data) and previews.

Blender space: nose points -Y, Z up, port wing +X, metres, origin on the ground under the main gear.
Unity space (FBX import with bakeAxisConversion): nose +Z, Y up, starboard +X, so (x, y, z) -> (-x, z, -y).
"""
import importlib
import json
import math
import os
import re
import shutil
import sys
import time

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

import airlines
import font as fontlib
import paint
import texturing
from geom import MeshBuilder

COMMON = os.path.dirname(os.path.abspath(__file__))
AIRCRAFT = os.path.dirname(COMMON)
REPO = os.path.abspath(os.path.join(AIRCRAFT, "..", ".."))
ASSETS = os.path.join(REPO, "Assets", "AirCity")
SHARED = os.path.join(ASSETS, "Aircraft", "Shared")
LOGOS = os.path.join(ASSETS, "Liveries", "Logos")

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def arg(name, default=None):
    if name in ARGS:
        i = ARGS.index(name)
        return ARGS[i + 1] if i + 1 < len(ARGS) and not ARGS[i + 1].startswith("--") else True
    return default


def load_model():
    name = arg("--model")
    if not name:
        raise SystemExit("pass --model <Folder>, e.g. -- --model Cessna172")
    sys.path.insert(0, os.path.join(AIRCRAFT, name))
    model = importlib.import_module("model")
    assert model.NAME == name, (model.NAME, name)
    return model


class Paths:
    def __init__(self, model):
        self.out = os.path.join(AIRCRAFT, model.NAME, "Build")        # generator scratch (gitignored)
        self.work = os.path.join(self.out, "work")
        self.unity = os.path.join(ASSETS, "Aircraft", model.NAME)
        self.tex = os.path.join(self.unity, "Textures")
        self.models = os.path.join(self.unity, "Models")
        self.previews = os.path.join(self.out, "previews")
        self.liveries = os.path.join(self.out, "liveries")


def to_unity(v):
    return [round(-float(v[0]), 4), round(float(v[2]), 4), round(-float(v[1]), 4)]


# ============================================================== materials

MATS = {
    # name: (base rgb, metallic, roughness, emission strength). Everything but Body / Registration /
    # GroundShadow is a flat colour that Unity's importer merges into the Trim palette.
    "Body": ((0.92, 0.92, 0.92), 0.0, 0.32, 0.0),
    "Registration": ((0.92, 0.92, 0.92), 0.0, 0.32, 0.0),
    "Glass": ((0.015, 0.022, 0.03), 0.0, 0.04, 0.0),
    "Metal": ((0.62, 0.62, 0.64), 1.0, 0.32, 0.0),
    "Chrome": ((0.92, 0.92, 0.94), 1.0, 0.08, 0.0),
    "DarkMetal": ((0.16, 0.14, 0.12), 0.8, 0.5, 0.0),
    "Rubber": ((0.025, 0.025, 0.025), 0.0, 0.85, 0.0),
    "Dark": ((0.02, 0.02, 0.022), 0.0, 0.6, 0.0),
    "PropBlade": ((0.03, 0.03, 0.03), 0.3, 0.45, 0.0),
    "PropTip": ((0.95, 0.95, 0.95), 0.0, 0.4, 0.0),
    "LightRed": ((1.0, 0.04, 0.02), 0.0, 0.2, 4.0),
    "LightGreen": ((0.05, 1.0, 0.2), 0.0, 0.2, 4.0),
    "LightWhite": ((1.0, 1.0, 1.0), 0.0, 0.2, 4.0),
    "LightLens": ((0.85, 0.88, 0.95), 0.0, 0.05, 0.6),
    "GroundShadow": ((0.0, 0.0, 0.0), 0.0, 1.0, 0.0),
}


def make_materials():
    out = {}
    for name, (rgb, metal, rough, emit) in MATS.items():
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
        bsdf.inputs["Metallic"].default_value = metal
        bsdf.inputs["Roughness"].default_value = rough
        if emit > 0:
            bsdf.inputs["Emission Color"].default_value = (*rgb, 1.0)
            bsdf.inputs["Emission Strength"].default_value = emit
        m.diffuse_color = (*rgb, 1.0)
        out[name] = m
    return out


# ============================================================== movers (separately animated parts)

class Mover:
    """A part Unity animates. `pivot` is its origin. With a `hinge` direction the object is rotated so
    the hinge becomes its local X axis (axis="x": flaps, ailerons, elevators), pointing to starboard like an
    unrotated part's, or its local Z axis (axis="z": rudders; Unity's Y). Unity then animates
    rest rotation * angle about that axis, with the same sign convention on every aircraft."""

    def __init__(self, name, builder, pivot, hinge=None, axis="x", parent=None):
        self.name, self.builder, self.pivot = name, builder, Vector(pivot)
        self.hinge, self.axis, self.parent = hinge, axis, parent


def hinge_basis(hinge, axis):
    h = Vector(hinge).normalized()
    if axis == "x":
        if h.x < 0:          # Blender +X; the FBX conversion makes it Unity local +X (starboard), as for unrotated parts
            h = -h
        z = Vector((0, 0, 1))
        z = (z - h * z.dot(h)).normalized()
        y = z.cross(h)
        return Matrix((h, y, z)).transposed()
    if h.z < 0:
        h = -h
    x = Vector((1, 0, 0))
    x = (x - h * x.dot(h)).normalized()
    y = h.cross(x)
    return Matrix((x, y, h)).transposed()


# ============================================================== UV projection rules

PROJ = {  # dominant axis -> (u, v) as seen from outside, never mirrored
    "+X": lambda p: (p.y, p.z), "-X": lambda p: (-p.y, p.z),
    "+Z": lambda p: (-p.x, -p.y), "-Z": lambda p: (p.x, -p.y),
    "-Y": lambda p: (p.x, p.z), "+Y": lambda p: (-p.x, p.z),
}


def face_normal(pts):
    n = Vector()
    for i in range(len(pts)):
        a, c = pts[i], pts[(i + 1) % len(pts)]
        n.x += (a.y - c.y) * (a.z + c.z)
        n.y += (a.z - c.z) * (a.x + c.x)
        n.z += (a.x - c.x) * (a.y + c.y)
    return n.normalized() if n.length > 1e-12 else Vector((0, 0, 1))


def assign_uvs(b: MeshBuilder, model):
    """Fill in UVs for faces that have none. Tags in model.UV_TOP_BOTTOM are projected from above/below
    (left and right halves apart), tags in model.UV_SIDE from the sides, everything else along its
    dominant axis. model.DENSITY scales texel density per tag (relative to the fuselage)."""
    island_offsets = {}
    for fi, (face, tag) in enumerate(zip(b.faces, b.face_tag)):
        pts = [Vector(b.verts[i]) for i in face]
        n = face_normal(pts)
        dens = model.DENSITY.get(tag, 1.0)
        if b.face_uvs[fi] is not None:   # analytic fuselage UVs
            b.face_uvs[fi] = [(u * dens, v * dens) for u, v in b.face_uvs[fi]]
            continue
        base = tag.replace("_cap", "")
        if base in model.UV_TOP_BOTTOM:
            axis = "+Z" if n.z >= 0 else "-Z"
            cx = sum(p.x for p in pts) / len(pts)
            key = (base, axis, cx >= 0)                     # split left / right halves
            if axis == "-Z":
                dens *= model.BOTTOM_DENSITY
        elif base in model.UV_SIDE:
            axis = "+X" if n.x >= 0 else "-X"
            key = (base, axis)
        else:
            comps = {"+X": n.x, "-X": -n.x, "+Y": n.y, "-Y": -n.y, "+Z": n.z, "-Z": -n.z}
            axis = max(comps, key=comps.get)
            cx = sum(p.x for p in pts) / len(pts)
            key = (base, axis, cx >= 0)
            if axis == "-Z":
                dens *= 0.7
        if tag.endswith("_cap"):
            key = key + ("cap",)
        off = island_offsets.setdefault(key, len(island_offsets) * 100.0)
        b.face_uvs[fi] = [(PROJ[axis](p)[0] * dens + off, PROJ[axis](p)[1] * dens) for p in pts]


# ============================================================== stage 1

def build_objects(model):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mats = make_materials()
    coll = bpy.data.collections.new(model.NAME)
    bpy.context.scene.collection.children.link(coll)
    body, movers = model.build()
    assign_uvs(body, model)
    objs = {"Body": body.build("Body", mats, coll)}
    for m in movers:
        assign_uvs(m.builder, model)
        ob = m.builder.build(m.name, mats, coll, origin=m.pivot)
        if m.hinge is not None:
            basis = hinge_basis(m.hinge, m.axis)
            ob.data.transform(basis.inverted().to_4x4())
            ob.matrix_basis = Matrix.Translation(m.pivot) @ basis.to_4x4()
        objs[m.name] = ob
    root = bpy.data.objects.new(model.NAME, None)
    coll.objects.link(root)
    bpy.context.view_layer.update()
    for m in [None] + movers:
        ob = objs["Body"] if m is None else objs[m.name]
        parent = root if m is None or m.parent is None else objs[m.parent]
        world = ob.matrix_world.copy()
        ob.parent = parent
        ob.matrix_world = world
    return root, objs, mats


def check_normals(objs):
    """Per tag: how many faces disagree with bmesh's outward recalculation.
    Unity culls back faces, so inverted parts would simply vanish in game."""
    bad = {}
    for name, ob in objs.items():
        me = ob.data
        tags = me["part_tags"]
        attr = me.attributes["part_tag"].data
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)   # weld seams so shells are closed
        before = {f.index: f.normal.copy() for f in bm.faces}
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        for f in bm.faces:
            if f.normal.dot(before[f.index]) < 0:
                key = f"{name}:{tags[attr[f.index].value]}"
                bad[key] = bad.get(key, 0) + 1
        bm.free()
    print("NORMALS (flipped faces per object:tag):", bad or "all consistent")
    return bad


def stage1(model):
    paths = Paths(model)
    root, objs, mats = build_objects(model)
    tris = sum(sum(len(p.vertices) - 2 for p in ob.data.polygons) for ob in objs.values())
    print("TRIANGLES", tris, {k: len(o.data.polygons) for k, o in objs.items()})
    if arg("--check"):
        check_normals(objs)
    if arg("--texture"):
        t0 = time.time()
        mesh_objs = list(objs.values())
        tags = texturing.assign_global_tags(mesh_objs)
        texturing.pack_body_uvs(mesh_objs)
        print("packed in %.1fs" % (time.time() - t0))
        maps = texturing.bake_maps(mesh_objs, ao_distance=model.AO_DISTANCE)
        print("baked in %.1fs" % (time.time() - t0))
        os.makedirs(paths.work, exist_ok=True)
        np.savez_compressed(os.path.join(paths.work, "bake_maps.npz"), tags=np.array(tags), **maps)
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(paths.work, "stage1.blend"))
        cov = maps["cov"]
        print("coverage %.1f%%" % (100 * cov.mean()), "fuselage px/m %.0f" % fuselage_density(maps, tags))
        texturing.save_png(maps["ao"], os.path.join(paths.out, "debug_ao.png"))
        rng = np.random.default_rng(3)
        lut = rng.random((64, 3)) * 0.8 + 0.2
        texturing.save_png(np.where(cov[..., None], lut[maps["tag"] % 64], 0), os.path.join(paths.out, "debug_tags.png"))
    if arg("--cull"):
        for m in mats.values():
            m.use_backface_culling = True
    if arg("--preview"):
        setup_preview_scene(model, arg("--engine", "BLENDER_EEVEE"))
        views = (arg("--views") or "front34,side,rear34,top").split(",")
        render_previews(model, paths, views, arg("--prefix", "geo"))


def fuselage_density(maps, tags):
    """Texels per metre along the fuselage (the reference texel density)."""
    p = maps["pos"]
    dx = np.linalg.norm(p[:, 1:] - p[:, :-1], axis=2)
    fus_ids = [tags.index("fus_port"), tags.index("fus_stbd")]
    ok = maps["cov"][:, 1:] & maps["cov"][:, :-1] & np.isin(maps["tag"][:, 1:], fus_ids) & (
        maps["tag"][:, 1:] == maps["tag"][:, :-1])
    return float(1.0 / np.median(dx[ok]))


# ============================================================== previews

def setup_preview_scene(model, engine="BLENDER_EEVEE"):
    sc = bpy.context.scene
    sc.render.engine = engine
    sc.render.resolution_x = int(arg("--w", 1280))
    sc.render.resolution_y = int(arg("--h", 760))
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    nt = world.node_tree
    bg = nt.nodes["Background"]
    sky = nt.nodes.new("ShaderNodeTexSky")
    try:
        sky.sky_type = "NISHITA"
    except Exception:
        pass
    nt.links.new(sky.outputs["Color"], bg.inputs["Color"])
    bg.inputs["Strength"].default_value = 0.35
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 4.0
    sun.angle = math.radians(2.5)
    so = bpy.data.objects.new("Sun", sun)
    so.rotation_euler = (math.radians(50), math.radians(10), math.radians(150))
    sc.collection.objects.link(so)
    g = 8 * max(abs(c) for v in model.VIEWS.values() for c in v[0])
    me = bpy.data.meshes.new("Ground")
    me.from_pydata([(-g, -g, 0), (g, -g, 0), (g, g, 0), (-g, g, 0)], [], [(0, 1, 2, 3)])
    ground = bpy.data.objects.new("Ground", me)
    gm = bpy.data.materials.new("GroundMat")
    gm.use_nodes = True
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.32, 0.33, 0.34, 1)
    gm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.9
    me.materials.append(gm)
    sc.collection.objects.link(ground)


def look_at(cam_ob, target):
    d = Vector(target) - cam_ob.location
    cam_ob.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


def render_previews(model, paths, views, prefix):
    sc = bpy.context.scene
    cam = bpy.data.cameras.get("Cam") or bpy.data.cameras.new("Cam")
    cam_ob = bpy.data.objects.get("Cam") or bpy.data.objects.new("Cam", cam)
    if cam_ob.name not in sc.collection.objects:
        sc.collection.objects.link(cam_ob)
    sc.camera = cam_ob
    cam.clip_end = 1000
    os.makedirs(paths.previews, exist_ok=True)
    for n in views:
        loc, tgt, lens = model.VIEWS[n]
        cam_ob.location = loc
        if lens < 0:
            cam.type = "ORTHO"
            cam.ortho_scale = -lens
        else:
            cam.type = "PERSP"
            cam.lens = lens
        look_at(cam_ob, tgt)
        sc.render.filepath = os.path.join(paths.previews, f"{prefix}_{n}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", sc.render.filepath)


# ============================================================== registration patch

def make_registration_patch(model, body, reg_mat):
    """Move the fuselage faces under the registration onto their own material with a local 0..1 UV
    (port strip on the top half, starboard strip on the bottom half). Returns the rects (in main-atlas
    UV) the runtime needs to rebuild that texture for any livery."""
    me = body.data
    uv = me.uv_layers["UVMap"].data
    raw = me.uv_layers["UVraw"].data
    tags = list(me["part_tags"])
    tag_attr = me.attributes["part_tag"].data
    if "Registration" not in [m.name for m in me.materials]:
        me.materials.append(reg_mat)
    reg_index = [m.name for m in me.materials].index("Registration")
    # keep the atlas UVs: the far LODs put these faces back onto the Body material
    atlas = me.uv_layers.get("UVatlas") or me.uv_layers.new(name="UVatlas", do_init=False)
    buf = np.zeros(len(me.loops) * 2, np.float32)
    me.uv_layers["UVMap"].data.foreach_get("uv", buf)
    atlas.data.foreach_set("uv", buf)
    me.uv_layers.active = me.uv_layers["UVMap"]
    fu, fv = model.REG_FACES_U, model.REG_FACES_V
    sides = {}
    for p in me.polygons:
        tag = tags[tag_attr[p.index].value]
        if tag not in ("fus_port", "fus_stbd"):
            continue
        r = np.array([raw[li].uv[:] for li in p.loop_indices])
        if np.all((np.abs(r[:, 0]) >= fu[0]) & (np.abs(r[:, 0]) <= fu[1]) & (r[:, 1] >= fv[0]) & (r[:, 1] <= fv[1])):
            sides.setdefault("port" if tag == "fus_port" else "stbd", []).append(p.index)
    out = {}
    for side, faces in sides.items():
        loops = [li for fi in faces for li in me.polygons[fi].loop_indices]
        r = np.array([raw[li].uv[:] for li in loops])
        q = np.array([uv[li].uv[:] for li in loops])
        # packing is a uniform scale + translation per island: fit it
        A = np.stack([r[:, 0], np.ones(len(r))], 1)
        ku, tu = np.linalg.lstsq(A, q[:, 0], rcond=None)[0]
        A = np.stack([r[:, 1], np.ones(len(r))], 1)
        kv, tv = np.linalg.lstsq(A, q[:, 1], rcond=None)[0]
        err = np.abs(np.stack([ku * r[:, 0] + tu, kv * r[:, 1] + tv], 1) - q).max()
        assert err < 1e-4 and abs(ku - kv) < 1e-4, (side, err, ku, kv)
        pad = 6.0 / texturing.TEX
        crop = [q[:, 0].min() - pad, q[:, 1].min() - pad, q[:, 0].max() + pad, q[:, 1].max() + pad]
        # metric (station, arc) -> atlas UV:  u = k * sign * station + tu,  v = k * arc + tv
        out[side] = {"crop": crop, "k": float(ku), "tu": float(tu), "tv": float(tv), "faces": faces}
    w = model.REG_TEX_W
    half_h = int(round(w * max((o["crop"][3] - o["crop"][1]) / (o["crop"][2] - o["crop"][0]) for o in out.values())))
    half_h += half_h % 4   # BC1 blocks: the texture height (2 * half_h) must be a multiple of 4
    for side, o in out.items():
        cu0, cv0, cu1, cv1 = o["crop"]
        for fi in o.pop("faces"):
            p = me.polygons[fi]
            p.material_index = reg_index
            for li in p.loop_indices:
                lu = (uv[li].uv[0] - cu0) / (cu1 - cu0)
                lv = (uv[li].uv[1] - cv0) / (cv1 - cv0)
                uv[li].uv = (lu, 0.5 + 0.5 * lv) if side == "port" else (lu, 0.5 * lv)
    out["tex_w"] = w
    out["half_h"] = half_h
    return out


def set_image(mat, img, socket="Base Color"):
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tex = nt.nodes.get("LiveryTex") or nt.nodes.new("ShaderNodeTexImage")
    tex.name = "LiveryTex"
    tex.image = img
    tex.interpolation = "Linear"
    tex.location = (-400, 200)
    nt.links.new(tex.outputs["Color"], bsdf.inputs[socket])
    bsdf.inputs["Base Color"].default_value = (1, 1, 1, 1)


def uv_wire(objs, size):
    """Rasterise the Body faces' UV edges (alpha mask). Island borders are drawn bolder."""
    seg = []
    for ob in objs:
        me = ob.data
        uvl = me.uv_layers["UVMap"].data
        body = {i for i, m in enumerate(me.materials) if m and m.name == "Body"}
        for poly in me.polygons:
            if poly.material_index not in body:
                continue
            li = list(poly.loop_indices)
            for a, b in zip(li, li[1:] + li[:1]):
                seg.append((*uvl[a].uv, *uvl[b].uv))
    seg = np.array(seg, np.float32) * size
    # an edge shared by two faces appears twice with identical endpoints; borders appear once
    key = np.round(np.sort(seg.reshape(-1, 2, 2).view(np.complex64).reshape(-1, 2), axis=1).view(np.float32), 1)
    _, inv, counts = np.unique(key.reshape(len(seg), -1), axis=0, return_inverse=True, return_counts=True)
    border = counts[inv.ravel()] == 1
    alpha = np.zeros((size, size), np.float32)
    for sel, val in ((~border, 0.45), (border, 1.0)):
        sg = seg[sel]
        n = np.maximum(np.ceil(np.hypot(sg[:, 2] - sg[:, 0], sg[:, 3] - sg[:, 1]) * 2).astype(int), 1)
        t = np.concatenate([np.linspace(0, 1, k + 1) for k in n])
        idx = np.repeat(np.arange(len(sg)), n + 1)
        x = sg[idx, 0] + (sg[idx, 2] - sg[idx, 0]) * t
        y = sg[idx, 1] + (sg[idx, 3] - sg[idx, 1]) * t
        xi = np.clip(x.astype(int), 0, size - 1)
        yi = np.clip(y.astype(int), 0, size - 1)
        alpha[yi, xi] = np.maximum(alpha[yi, xi], val)
    return alpha


# ============================================================== LODs, collider, ground shadow

def lod_level(name):
    m = re.search(r"_LOD(\d+)$", name)
    return int(m.group(1)) if m else 0


def copy_object(ob, name):
    c = ob.copy()
    c.data = ob.data.copy()
    c.name = c.data.name = name
    for col in ob.users_collection:
        col.objects.link(c)
    return c


def unpatch_registration(ob):
    """Registration faces back onto the Body material and atlas UVs: the letters are unreadable at LOD1/2 sizes."""
    me = ob.data
    names = [m.name if m else "" for m in me.materials]
    if "Registration" not in names:
        return
    reg, body = names.index("Registration"), names.index("Body")
    uv, atlas = me.uv_layers["UVMap"].data, me.uv_layers["UVatlas"].data
    for p in me.polygons:
        if p.material_index == reg:
            p.material_index = body
            for li in p.loop_indices:
                uv[li].uv = atlas[li].uv


def select_only(ob, others=()):
    for o in bpy.context.view_layer.objects:
        o.select_set(o == ob or o in others)
    bpy.context.view_layer.objects.active = ob


def decimate(ob, ratio):
    mod = ob.modifiers.new("Decimate", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    select_only(ob)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    ob.data.set_sharp_from_angle(angle=math.radians(40))
    bpy.ops.object.material_slot_remove_unused()


def make_lods(model, body, movers):
    """Body -> Body_LOD0 plus far LODs that each render as one static mesh.

    LOD0 = Body_LOD0 + the separately animated parts (movers).
    LOD1 = Body_LOD1 (movers merged in at rest) + a low-poly copy of each part in model.LOD1_KEEP
           (spinning propellers), parented to the full part so the same component turns it.
    LOD2 = Body_LOD2 (everything merged).
    Unity's importer (AircraftAssetPostprocessor) builds the LODGroup from the _LODn suffixes; unsuffixed = LOD0.
    """
    keep = [m for m in movers if m.name in model.LOD1_KEEP]
    out = []
    for level, parts in ((1, [m for m in movers if m not in keep]), (2, movers)):
        ob = copy_object(body, f"Body_LOD{level}")
        unpatch_registration(ob)
        extras = []
        for p in parts:
            c = copy_object(p, f"{p.name}_merge{level}")
            c.parent = None
            c.matrix_world = p.matrix_world.copy()
            extras.append(c)
        select_only(ob, extras)
        bpy.ops.object.join()
        decimate(ob, model.LOD_RATIOS[level])
        out.append(ob)
    for p in keep:
        pl = copy_object(p, f"{p.name}_LOD1")
        pl.parent = p
        pl.matrix_parent_inverse = Matrix.Identity(4)
        pl.matrix_basis = Matrix.Identity(4)
        decimate(pl, 0.3)
        out.append(pl)
    body.name = body.data.name = "Body_LOD0"
    return [body] + out


def make_collider(lod2, ratio):
    """A coarse copy of the far LOD for mouse picking (Unity turns it into a MeshCollider)."""
    c = copy_object(lod2, "Collider")
    decimate(c, ratio)
    c.data.materials.clear()
    return c


def ground_shadow(objs, model, paths, size=256):
    """Soft contact shadow under the aircraft: render the height of the lowest surface from below, then
    blur each height band by its distance to the ground. Returns the quad object and the texture path."""
    corners = np.array([ob.matrix_world @ Vector(c) for ob in objs for c in ob.bound_box])
    lo, hi = corners.min(axis=0), corners.max(axis=0)
    height = float(hi[2])
    margin = 0.5 + 0.12 * float(max(hi[0] - lo[0], hi[1] - lo[1]))
    cx, cy = float(lo[0] + hi[0]) / 2, float(lo[1] + hi[1]) / 2
    side = float(max(hi[0] - lo[0], hi[1] - lo[1])) + 2 * margin

    sc = bpy.data.scenes.new("ShadowBake")
    linked = list(objs) + [bpy.data.objects[model.NAME]]     # parents too, so world transforms evaluate
    for ob in linked:
        sc.collection.objects.link(ob)
    mat = bpy.data.materials.new("HeightBake")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    em = nt.nodes.new("ShaderNodeEmission")
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(geo.outputs["Position"], sep.inputs[0])
    nt.links.new(sep.outputs["Z"], em.inputs["Strength"])
    nt.links.new(em.outputs[0], out.inputs["Surface"])
    sc.view_layers[0].material_override = mat
    cam = bpy.data.cameras.new("ShadowCam")
    cam.type = "ORTHO"
    cam.ortho_scale = side
    cam.clip_end = 200
    co = bpy.data.objects.new("ShadowCam", cam)
    co.location = (cx, cy, -50)
    co.rotation_euler = (math.pi, 0, 0)        # looking up: image right = +X, image up = -Y
    sc.collection.objects.link(co)
    sc.camera = co
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x = sc.render.resolution_y = size
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = True
    sc.view_settings.view_transform = "Standard"
    sc.render.image_settings.file_format = "OPEN_EXR"
    sc.render.image_settings.color_depth = "32"
    tmp = os.path.join(paths.work, "shadow_height.exr")
    sc.render.filepath = tmp
    bpy.ops.render.render(write_still=True, scene=sc.name)
    img = bpy.data.images.load(tmp)
    px = np.empty(size * size * 4, np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(size, size, 4)
    bpy.data.images.remove(img)
    for ob in linked:
        sc.collection.objects.unlink(ob)
    bpy.data.scenes.remove(sc)

    cov = np.clip(px[..., 3], 0, 1)
    z = np.where(cov > 0.01, px[..., 0] / np.maximum(cov, 1e-3), height)
    px_per_m = size / side
    occ = np.zeros_like(cov)
    edges = np.array([0.0, 0.12, 0.3, 0.55, 1.0]) * height
    for z0, z1 in zip(edges[:-1], edges[1:]):
        band = cov * ((z >= z0) & (z < z1 + 1e-3))
        zc = 0.5 * (z0 + z1)
        spread = paint.blur(band, (0.15 + 0.45 * zc) * px_per_m)
        strength = 0.85 * math.exp(-zc / (0.75 * height))
        occ = 1 - (1 - occ) * (1 - np.clip(spread * strength * 1.6, 0, strength))
    edge = np.minimum(np.arange(size), np.arange(size)[::-1]).astype(np.float32)
    occ *= np.clip(np.minimum(edge[None, :], edge[:, None]) / 6.0, 0, 1)      # fade out at the quad's border
    tex_path = os.path.join(paths.tex, f"{model.NAME}_GroundShadow.png")
    texturing.save_png(occ, tex_path)

    me = bpy.data.meshes.new("GroundShadow")
    h = side / 2
    verts = [(cx - h, cy + h, 0.03), (cx - h, cy - h, 0.03), (cx + h, cy - h, 0.03), (cx + h, cy + h, 0.03)]
    me.from_pydata(verts, [], [(0, 1, 2, 3)])                  # counter-clockwise from above: faces up
    uvl = me.uv_layers.new(name="UVMap")
    for li, loop in enumerate(me.loops):                       # v grows towards -Y, like the render
        x, y, _ = verts[loop.vertex_index]
        uvl.data[li].uv = ((x - (cx - h)) / side, ((cy + h) - y) / side)
    mat = bpy.data.materials.get("GroundShadow") or bpy.data.materials.new("GroundShadow")
    me.materials.append(mat)
    ob = bpy.data.objects.new("GroundShadow", me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


# ============================================================== unity glue

def write_profile(model, paths, px_per_m, reg, font, bounds, wing):
    """Write what Unity needs: <Model>.aircraft (painting + gameplay data, imported by AircraftTypeImporter)
    and the shared RegistrationFont.regfont. Positions are in Unity space, relative to the aircraft root."""
    def side(d, sign):
        return {"crop": [float(v) for v in d["crop"]], "k": d["k"], "tu": d["tu"], "tv": d["tv"], "sign": float(sign)}

    lo, hi = bounds
    gear = model.GEAR
    profile = {
        "model": model.NAME,
        "displayName": model.DISPLAY_NAME,
        "category": model.CATEGORY,
        "bounds": {"min": to_unity((hi[0], hi[1], lo[2])), "max": to_unity((lo[0], lo[1], hi[2]))},
        "wingOutline": wing,
        "atlasSize": texturing.TEX,
        "texelsPerMetre": px_per_m,
        "detail": f"Textures/{model.NAME}_Detail.png",
        "dirt": f"Textures/{model.NAME}_Dirt.png",
        "registrationDirt": f"Textures/{model.NAME}_RegistrationDirt.png",
        "logoUV": f"Textures/{model.NAME}_LogoUV.png",
        "logoBox": list(model.LOGO_BOX),
        "groundShadow": f"Textures/{model.NAME}_GroundShadow.png",
        "font": "../Shared/RegistrationFont.regfont",
        "defaultBody": f"Textures/{model.NAME}_Body_Default.png",
        "defaultRegistration": f"Textures/{model.NAME}_Registration_Default.png",
        "uvTemplate": f"Textures/{model.NAME}_UV_Template.png",
        "schemes": [{"style": n, "mask": f"Textures/{model.NAME}_Mask_{n}.png", "registrationBox": list(model.REG_BOX[n])}
                    for n in model.SCHEMES],
        "registration": {"textureWidth": reg["tex_w"], "halfHeight": reg["half_h"],
                         "port": side(reg["port"], 1), "starboard": side(reg["stbd"], -1)},
        "examples": [{"name": n, "style": v[0], "baseColor": v[1], "color1": v[2], "color2": v[3], "color3": v[4],
                      "registrationColor": v[5], "registration": v[6],
                      "logo": f"../../Liveries/Logos/{v[7]}.png" if v[7] else ""}
                     for n, v in airlines.LIVERIES.items()],
        "gear": {
            "wheels": [{"name": n, "position": to_unity(pos), "radius": r, "steerable": st}
                       for n, pos, r, st in gear["wheels"]],
            "steeringPivot": to_unity(gear["steering_pivot"]),
            "maxSteeringAngle": gear["max_steering"],
        },
        "performance": {"takeoffDistance": model.PERFORMANCE["takeoff_distance"],
                        "landingDistance": model.PERFORMANCE["landing_distance"],
                        "maxTakeoffMass": model.PERFORMANCE["max_takeoff_mass"],
                        "unpavedRunways": model.PERFORMANCE["unpaved"],
                        "taxiSpeed": model.PERFORMANCE["taxi_speed"],
                        "rotateSpeed": model.PERFORMANCE["rotate_speed"],
                        "approachSpeed": model.PERFORMANCE["approach_speed"],
                        "climbRate": model.PERFORMANCE["climb_rate"],
                        "climbSpeed": model.PERFORMANCE["climb_speed"]},
        "points": [{"name": n, "type": t, "position": to_unity(pos), "normal": to_unity(nrm), "size": list(size)}
                   for t, n, pos, nrm, size in model.POINTS],
        "lights": [{"name": n, "type": t, "position": to_unity(pos), "direction": to_unity(d), "spread": spread,
                    "color": list(col)} for t, n, pos, d, spread, col in model.LIGHTS],
    }
    with open(os.path.join(paths.unity, f"{model.NAME}.aircraft"), "w") as fh:
        json.dump(profile, fh, indent=2, default=float)
    chars = "".join(font["glyphs"].keys())
    regfont = {
        "atlas": "RegistrationFont.png", "atlasSize": font["size"], "cell": font["cell_w"],
        "baseline": font["baseline"], "capTop": font["cap_top"], "chars": chars,
        **{key: [font["glyphs"][c][src] for c in chars]
           for key, src in (("cellX", "cell_x"), ("cellY", "cell_y"), ("inkLeft", "ink_left"), ("inkRight", "ink_right"))},
    }
    with open(os.path.join(SHARED, "RegistrationFont.regfont"), "w") as fh:
        json.dump(regfont, fh, indent=2, default=float)


def turn_around(objs):
    """Rotate the aircraft 180 degrees about Z (calling it twice restores it).

    Unity imports this FBX setup as Blender (x, y, z) -> Unity (x, z, y), so a nose at Blender -Y would face
    Unity -Z. Exporting turned around gives nose = +Z (transform.forward), starboard = +X, which is what
    to_unity() and all gameplay data assume. Each part is conjugated (mesh R v, matrix R M R^-1), so pivots
    move with the aircraft and every part's local axes keep their meaning (hinges along local X, starboard).
    AircraftTypeTests.Model_MatchesTypeData checks the result.
    """
    R = Matrix.Rotation(math.pi, 4, "Z")
    Ri = R.inverted()
    worlds = {ob: ob.matrix_world.copy() for ob in objs}

    def depth(ob):
        d = 0
        while ob.parent is not None:
            ob, d = ob.parent, d + 1
        return d

    for ob in objs:
        if ob.type == "MESH":
            ob.data.transform(R)
    for ob in sorted(objs, key=depth):          # parents first: a child's world matrix is set relative to them
        ob.matrix_world = R @ worlds[ob] @ Ri
        bpy.context.view_layer.update()


def flatten(root, objs):
    """Blender's FBX exporter bakes the axis conversion correctly only into the root's direct children:
    deeper nodes come out with a stray 270 degree rotation. So nested parts are exported as children of the
    root named "<Parent>__<Name>", and Unity's importer (AircraftAssetPostprocessor) puts them back."""
    nested = []
    for ob in objs:
        if ob.parent is not None and ob.parent != root:
            world = ob.matrix_world.copy()
            nested.append((ob, ob.parent, ob.name))
            ob.parent = root
            ob.matrix_world = world
            ob.name = f"{nested[-1][1].name}__{nested[-1][2]}"
    bpy.context.view_layer.update()
    return nested


def unflatten(nested):
    for ob, parent, name in nested:
        world = ob.matrix_world.copy()
        ob.parent = parent
        ob.matrix_world = world
        ob.name = name
    bpy.context.view_layer.update()


def write_logos():
    os.makedirs(LOGOS, exist_ok=True)
    out = {}
    for name, fn in airlines.LOGOS.items():
        rgba = fn()
        texturing.save_png(rgba, os.path.join(LOGOS, f"{name}.png"))
        out[name] = rgba
    return out


# ============================================================== stage 2

def world_bounds(objs):
    """(min, max) of every vertex in world space. Not the objects' bound_box corners: a rotated mover (a swept
    rudder hinge) would carry its local box's empty corners into the aircraft's dimensions."""
    pts = []
    for ob in objs:
        co = np.empty(len(ob.data.vertices) * 3, np.float64)
        ob.data.vertices.foreach_get("co", co)
        m = np.array(ob.matrix_world)
        pts.append(co.reshape(-1, 3) @ m[:3, :3].T + m[:3, 3])
    pts = np.concatenate(pts)
    return pts.min(axis=0), pts.max(axis=0)


# the parts of a wing and its engines, by tag prefix (their _cap faces too)
WING_TAGS = ("wing", "sharklet", "winglet", "fence", "nacelle", "pylon", "intake", "fanduct", "fanface", "plug", "core")


def convex_hull(points):
    """Counter-clockwise convex hull of 2D points (Andrew's monotone chain), without collinear corners."""
    pts = sorted(set(points))
    if len(pts) < 3:
        return pts

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    lower, upper = [], []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]


def _tagged_edges(ob, prefixes):
    """The edges (Unity (x, z) end points, two (n, 2) arrays) of an object's faces whose tag starts with `prefixes`."""
    me = ob.data
    tags = list(me.get("part_tags", []))
    keep = [i for i, t in enumerate(tags) if t.startswith(prefixes)]
    if not keep or "part_tag" not in me.attributes:
        return None
    n = len(me.polygons)
    tag, start, total = (np.empty(n, np.int32) for _ in range(3))
    me.attributes["part_tag"].data.foreach_get("value", tag)
    me.polygons.foreach_get("loop_start", start)
    me.polygons.foreach_get("loop_total", total)
    loops = np.empty(len(me.loops), np.int32)
    me.loops.foreach_get("vertex_index", loops)
    a, b = [], []
    for s, k in zip(start[np.isin(tag, keep)], total[np.isin(tag, keep)]):
        ring = loops[s:s + k]
        a.append(ring)
        b.append(np.roll(ring, -1))
    if not a:
        return None
    co = np.empty(len(me.vertices) * 3, np.float64)
    me.vertices.foreach_get("co", co)
    m = np.array(ob.matrix_world)
    world = co.reshape(-1, 3) @ m[:3, :3].T + m[:3, 3]
    uxz = np.stack([-world[:, 0], -world[:, 1]], 1)                    # Unity x, z (to_unity)
    return uxz[np.concatenate(a)], uxz[np.concatenate(b)]


def wing_outline(objs, tolerance=0.02):
    """The left wing and its engines from above, outboard of the fuselage's side: a convex outline in Unity (x, z),
    counter-clockwise, flattened to [x0, z0, x1, z1, ...] (the right wing mirrors it). Jet bridges keep their tunnels
    off it: a swept wing leaves room ahead of it (an L2 door just ahead of the wing root) that a box as wide as the
    span wouldn't. The wing box inside the fuselage is left out (it would pull the root's leading edge forward over
    the doors). Corners that stand less than `tolerance` off the line through their neighbours are dropped."""
    side = 0.0
    for ob in objs:
        fus = _tagged_edges(ob, ("fus_port", "fus_stbd"))
        if fus is not None:
            side = max(side, float(np.abs(fus[0][:, 0]).max()))
    pts = []
    for ob in objs:
        edges = _tagged_edges(ob, WING_TAGS)
        if edges is None:
            continue
        a, b = edges
        cut = -side
        pts.append(a[a[:, 0] <= cut])
        # where an edge crosses the fuselage's side
        cross = (a[:, 0] - cut) * (b[:, 0] - cut) < 0
        t = (cut - a[cross, 0]) / (b[cross, 0] - a[cross, 0])
        pts.append(a[cross] + (b[cross] - a[cross]) * t[:, None])
    pts = np.round(np.concatenate(pts), 3)
    hull = convex_hull([tuple(p) for p in pts.tolist()])
    while len(hull) > 3:
        for i in range(len(hull)):
            a, b, c = np.array(hull[i - 1]), np.array(hull[i]), np.array(hull[(i + 1) % len(hull)])
            d = c - a
            if abs(d[0] * (b[1] - a[1]) - d[1] * (b[0] - a[0])) < tolerance * np.linalg.norm(d):
                del hull[i]
                break
        else:
            break
    return [round(float(v), 3) for p in hull for v in p]


def stage2(model):
    paths = Paths(model)
    bpy.ops.wm.open_mainfile(filepath=os.path.join(paths.work, "stage1.blend"))
    for d in (paths.tex, paths.models, SHARED, paths.previews, paths.liveries):
        os.makedirs(d, exist_ok=True)
    data = np.load(os.path.join(paths.work, "bake_maps.npz"))
    tags = [str(t) for t in data["tags"]]
    maps = {k: data[k] for k in ("pos", "nrm", "tag", "cov", "valid", "ao")}
    px_per_m = fuselage_density(maps, tags)
    p = paint.Px(maps, tags, px_per_m, model)
    print("px/m", px_per_m)
    name = model.NAME
    tex = lambda suffix: os.path.join(paths.tex, f"{name}_{suffix}.png")   # noqa: E731

    # --- shading, dirt, logo placement and scheme masks
    lines = model.panel_lines(p)
    detail = paint.detail_map(maps, p, lines, model.detail_extra(p))
    texturing.save_png(detail, tex("Detail"))
    dirt = paint.dirt_map(maps, p, lines, model.dirt_extra(p))
    texturing.save_png(texturing.downsample(dirt, 2), tex("Dirt"))
    logo_uv = paint.logo_uv_map(p, model.LOGO_BOX)
    # stored premultiplied by coverage (u * cov, v * cov, cov) so bilinear sampling stays exact at the edges
    premul = np.concatenate([logo_uv[..., :2] * logo_uv[..., 2:3], logo_uv[..., 2:3]], -1)
    texturing.save_png(texturing.downsample(premul, 4), tex("LogoUV"))
    masks = {}
    for scheme, fn in model.SCHEMES.items():
        r, g, b = fn(p)
        m = np.stack([r, g, b], -1).astype(np.float32) * maps["valid"][..., None]
        masks[scheme] = m
        texturing.save_png(m, tex(f"Mask_{scheme}"))
    print("masks written")

    # --- uv template for painters (before the registration faces get their own UVs)
    objs = [bpy.data.objects["Body"]] + [ob for ob in bpy.data.objects if ob.type == "MESH" and ob.name != "Body"
                                         and ob.parent is not None]
    wa = uv_wire(objs, texturing.TEX)
    templ = np.where(maps["valid"][..., None], 0.82 * detail[..., None], 0.25) * np.ones(3, np.float32)
    templ = templ * (1 - wa[..., None] * 0.85) + np.array([0.1, 0.35, 0.9], np.float32) * wa[..., None] * 0.85
    texturing.save_png(templ, tex("UV_Template"))

    # --- registration font + patch
    font, atlas = fontlib.render_atlas(os.path.join(SHARED, "RegistrationFont.png"))
    print("font atlas rendered; cap", font["cap_top"] - font["baseline"])
    body_mat = bpy.data.materials["Body"]
    reg_mat = bpy.data.materials.get("Registration")
    if reg_mat is None:   # unused materials are not saved with stage1.blend
        reg_mat = body_mat.copy()
        reg_mat.name = "Registration"
    reg = make_registration_patch(model, bpy.data.objects["Body"], reg_mat)
    print("registration", json.dumps(reg, default=float))
    # the dirt map in the registration patch's layout (its material samples it with the patch UVs)
    reg_dirt = paint.registration_texture(np.repeat(dirt[..., None], 3, -1), np.ones_like(dirt), reg, "", (0, 0, 0),
                                          font, atlas, (0, 0, 0, 0))
    texturing.save_png(texturing.downsample(reg_dirt, 2), tex("RegistrationDirt"))

    # --- example liveries (previews only: the game composes liveries at runtime)
    logos = write_logos()
    box_aspect = (model.LOGO_BOX[1] - model.LOGO_BOX[0]) / (model.LOGO_BOX[3] - model.LOGO_BOX[2])
    ones = np.ones_like(detail)
    produced = {}
    for livery, (scheme, base, c1, c2, c3, regcol, regtext, logo) in airlines.LIVERIES.items():
        colors = [paint.hexrgb(c) for c in (base, c1, c2, c3)]
        m = masks[scheme]
        unshaded = paint.compose((m[..., 0], m[..., 1], m[..., 2]), colors, ones)
        if logo:
            unshaded = paint.apply_logo(unshaded, logo_uv, logos[logo], box_aspect)
        albedo = unshaded * detail[..., None]
        a_path = os.path.join(paths.liveries, f"{name}_Livery_{livery}.png")
        texturing.save_png(albedo, a_path)
        regtex = paint.registration_texture(unshaded, detail, reg, regtext, paint.hexrgb(regcol), font, atlas,
                                            model.REG_BOX[scheme])
        r_path = os.path.join(paths.liveries, f"{name}_Registration_{livery}.png")
        texturing.save_png(regtex, r_path)
        produced[livery] = (a_path, r_path)
        print("livery", livery)

    # the default livery ships with the model so it looks right before any script runs
    default = next(iter(airlines.LIVERIES))
    shutil.copyfile(produced[default][0], tex("Body_Default"))
    shutil.copyfile(produced[default][1], tex("Registration_Default"))
    set_image(body_mat, texturing.load_image(tex("Body_Default")))
    set_image(reg_mat, texturing.load_image(tex("Registration_Default")))
    for m in list(bpy.data.materials):
        if m.name.startswith("BAKE_"):
            bpy.data.materials.remove(m)

    # --- export
    wing = wing_outline(objs)
    for ob in objs:
        if "UVraw" in ob.data.uv_layers:
            ob.data.uv_layers.remove(ob.data.uv_layers["UVraw"])
        for a in ("part_tag", "tag_gid"):
            if a in ob.data.attributes:
                ob.data.attributes.remove(ob.data.attributes[a])
    movers = [ob for ob in objs if ob.name != "Body"]
    bounds = world_bounds(objs)
    write_profile(model, paths, px_per_m, reg, font, bounds, wing)
    shadow = ground_shadow(objs, model, paths)
    lods = make_lods(model, bpy.data.objects["Body"], movers)
    collider = make_collider(bpy.data.objects["Body_LOD2"], model.COLLIDER_RATIO)
    export = lods + movers + [collider, shadow]
    for ob in export:
        if "UVatlas" in ob.data.uv_layers:
            ob.data.uv_layers.remove(ob.data.uv_layers["UVatlas"])
        print("tris", ob.name, sum(len(p.vertices) - 2 for p in ob.data.polygons),
              "materials", [m.name for m in ob.data.materials])
    root = bpy.data.objects[name]
    for ob in (collider, shadow):
        ob.parent = root
    for ob in bpy.context.view_layer.objects:
        ob.select_set(False)
    root.select_set(True)
    for ob in export:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = root
    turn_around([root] + export)
    nested = flatten(root, export)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(paths.models, f"{name}.fbx"), use_selection=True, object_types={"EMPTY", "MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        bake_space_transform=True, mesh_smooth_type="OFF", use_mesh_modifiers=True, use_tspace=False,
        add_leaf_bones=False, path_mode="STRIP", embed_textures=False, bake_anim=False)
    unflatten(nested)
    turn_around([root] + export)          # back, for the previews below
    show = int(arg("--lod", 0))
    for ob in lods + movers:
        ob.hide_render = lod_level(ob.name) != show
        ob.hide_set(ob.hide_render)
    for ob in (collider, shadow):
        ob.hide_render = True
        ob.hide_set(True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(paths.out, f"{name}.blend"))
    print("exported")

    if arg("--previews"):
        setup_preview_scene(model, "BLENDER_EEVEE")
        names = (arg("--liveries") or default).split(",")
        views = (arg("--views") or "front34,rear34").split(",")
        for livery in names:
            body_mat.node_tree.nodes["LiveryTex"].image = texturing.load_image(produced[livery][0])
            reg_mat.node_tree.nodes["LiveryTex"].image = texturing.load_image(produced[livery][1])
            render_previews(model, paths, views, "livery_" + livery)
