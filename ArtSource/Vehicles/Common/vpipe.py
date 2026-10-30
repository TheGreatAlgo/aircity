"""The vehicle (and small building) pipeline. ArtSource/Vehicles/build.py runs it on a model definition
(<Model>/model.py, see ArtSource/README.md):

  geometry (kit.Part per object) -> one UV atlas over every part -> Cycles bakes (material colour, metal /
  gloss, ambient occlusion) -> albedo with grime and AO, metal-gloss map -> LODs -> FBX + <Model>.vehicle

Unlike aircraft there's no livery: every material is a flat colour from MATS, baked into one texture with the
AO and grime, so a vehicle draws with one material. Blender space: +X right, +Y forward, +Z up, metres, origin on
the ground under the rear axle; the FBX export maps that to Unity +X right, +Y up, +Z forward.
"""
import importlib
import json
import math
import os
import sys
import time

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

COMMON = os.path.dirname(os.path.abspath(__file__))
VEHICLES = os.path.dirname(COMMON)
REPO = os.path.abspath(os.path.join(VEHICLES, "..", ".."))
ASSETS = os.path.join(REPO, "Assets", "AirCity", "Vehicles")
sys.path.insert(0, os.path.join(REPO, "ArtSource", "Aircraft", "Common"))

import texturing  # noqa: E402  (shared bake helpers: images, cycles setup, png io)

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
TEX = 2048          # bake size; saved at half (supersampled AO)


def arg(name, default=None):
    if name in ARGS:
        i = ARGS.index(name)
        return ARGS[i + 1] if i + 1 < len(ARGS) and not ARGS[i + 1].startswith("--") else True
    return default


def load_model():
    name = arg("--model")
    if not name:
        raise SystemExit("pass --model <Folder>, e.g. -- --model SnowPlough")
    sys.path.insert(0, os.path.join(VEHICLES, name))
    model = importlib.import_module("model")
    assert model.NAME == name, (model.NAME, name)
    return model


def srgb_to_linear(c):
    c = np.asarray(c, float)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(c):
    c = np.clip(np.asarray(c, np.float32), 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


# ============================================================== materials

# Shared look of materials every model may use: (sRGB colour, metallic, roughness, lamp?). A model adds its own
# (paint colours) in model.MATS. Lamp lenses glow in previews only; in game the glow quads light them.
BASE_MATS = {
    "Chassis": ("#1A1B1D", 0.3, 0.6, False),
    "Dark": ("#0E0F10", 0.0, 0.55, False),
    "Plastic": ("#16171A", 0.0, 0.45, False),
    "Glass": ("#0B1015", 0.0, 0.05, False),
    "Tyre": ("#141414", 0.0, 0.88, False),
    "Rim": ("#B9BCC0", 0.8, 0.35, False),
    "Metal": ("#9A9DA2", 1.0, 0.35, False),
    "Steel": ("#6E7074", 1.0, 0.5, False),
    "Chrome": ("#E6E8EB", 1.0, 0.1, False),
    "White": ("#E9EAE8", 0.0, 0.4, False),
    "StripeRed": ("#C3161C", 0.0, 0.35, False),
    "StripeWhite": ("#EDEDEA", 0.0, 0.35, False),
    "LensAmber": ("#FF9A1A", 0.0, 0.1, True),
    "LensWhite": ("#F4F6FA", 0.0, 0.05, True),
    "LensRed": ("#E0181C", 0.0, 0.1, True),
    "LensOrange": ("#FF6A10", 0.0, 0.2, True),
}


def make_materials(model):
    defs = dict(BASE_MATS)
    defs.update(getattr(model, "MATS", {}))
    out = {}
    for name, (hexc, metal, rough, lamp) in defs.items():
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        lin = tuple(srgb_to_linear(hex_rgb(hexc)))
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = (*lin, 1.0)
        bsdf.inputs["Metallic"].default_value = metal
        bsdf.inputs["Roughness"].default_value = rough
        if lamp:
            bsdf.inputs["Emission Color"].default_value = (*lin, 1.0)
            bsdf.inputs["Emission Strength"].default_value = 2.0
        m["look"] = (*lin, metal, rough)
        out[name] = m
    return out


# ============================================================== build

def build_objects(model):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mats = make_materials(model)
    coll = bpy.data.collections.new(model.NAME)
    bpy.context.scene.collection.children.link(coll)
    parts = model.build()
    root = bpy.data.objects.new(model.NAME, None)
    coll.objects.link(root)
    objs = {}
    for p in parts:
        ob = p.build(mats, coll)
        ob.parent = root
        objs[p.name] = ob
    bpy.context.view_layer.update()
    return root, objs, mats


def check_normals(objs):
    """Faces whose winding disagrees with bmesh's outward recalculation (Unity culls back faces)."""
    bad = {}
    for name, ob in objs.items():
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
        before = {f.index: f.normal.copy() for f in bm.faces}
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        n = sum(1 for f in bm.faces if f.normal.dot(before[f.index]) < 0)
        if n:
            bad[name] = n
        bm.free()
    print("NORMALS (flipped faces per object):", bad or "all consistent")


# ============================================================== uv atlas + bakes

def select_only(objs, active=None):
    for o in bpy.context.view_layer.objects:
        o.select_set(o in objs)
    bpy.context.view_layer.objects.active = active or objs[0]


def unwrap(objs):
    """One atlas over every part: smart projection per object, then all islands packed together, sized by
    their area so texel density is even."""
    select_only(objs)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.0, area_weight=0.0,
                             correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(udim_source="CLOSEST_UDIM", rotate=True, scale=True, margin_method="FRACTION",
                            margin=0.004, shape_method="CONCAVE")
    bpy.ops.object.mode_set(mode="OBJECT")


def _emit_material(name, color_fn, img):
    """A bake material emitting a per-material constant (color_fn(look) -> rgb)."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = (*color_fn, 1.0)
    nt.links.new(em.outputs[0], out.inputs["Surface"])
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.nodes.active = tex
    return m


def _image(name):
    if name in bpy.data.images:
        bpy.data.images.remove(bpy.data.images[name])
    img = bpy.data.images.new(name, TEX, TEX, alpha=True, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    img.generated_color = (0, 0, 0, 0)     # alpha 0 = no face here (the bake writes 1)
    return img


def _read(img):
    a = np.empty(TEX * TEX * 4, np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(TEX, TEX, 4)


def bake(objs, ao_distance):
    """Bake into the atlas: (colour rgb, metallic, smoothness, coverage) per texel, world position, AO."""
    select_only(objs)
    sc = bpy.context.scene
    texturing._cycles(4)
    results = {}
    # one emit bake per channel set; every material slot is temporarily swapped for a constant emitter
    for kind in ("color", "metal", "pos", "ao"):
        img = _image("BAKE_" + kind)
        saved = []
        temp = {}
        for ob in objs:
            for i, slot in enumerate(ob.material_slots):
                saved.append((ob, i, slot.material))
                look = slot.material["look"]
                if kind == "color":
                    key = (look[0], look[1], look[2])
                elif kind == "metal":
                    key = (look[3], 1.0 - look[4], 1.0)
                else:
                    key = (1.0, 1.0, 1.0)
                if key not in temp:
                    temp[key] = _emit_material(f"BAKE_{kind}_{len(temp)}", key, img)
                ob.material_slots[i].material = temp[key]
        if kind == "pos":
            # world position as emission (for grime that depends on height)
            for m in temp.values():
                nt = m.node_tree
                g = nt.nodes.new("ShaderNodeNewGeometry")
                nt.links.new(g.outputs["Position"], nt.nodes["Emission"].inputs["Color"])
        if kind == "ao":
            sc.world = sc.world or bpy.data.worlds.new("BakeWorld")
            sc.world.light_settings.distance = ao_distance
            texturing._cycles(256)
            bpy.ops.object.bake(type="AO", margin=12, margin_type="EXTEND", use_clear=True)
        else:
            texturing._cycles(1 if kind != "pos" else 2)
            bpy.ops.object.bake(type="EMIT", margin=12, margin_type="EXTEND", use_clear=True)
        for ob, i, m in saved:
            ob.material_slots[i].material = m
        results[kind] = _read(img)
        print("baked", kind)
    return results


def collapse_uvs(ob, axis):
    """For a part the game stretches along one local axis ("r", "f" or "z"; the jet bridge's link and legs):
    each face's UVs collapse onto the line through the middle of its island along that axis, so the texture (AO
    included) doesn't vary along the stretch, however far it goes."""
    k = "rfz".index(axis)
    me = ob.data
    uv = me.uv_layers.active.data
    for poly in me.polygons:
        groups = {}
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            groups.setdefault(tuple(round(co[i], 4) for i in range(3) if i != k), []).append(li)
        for loops in groups.values():
            if len(loops) > 1:
                mid = sum((uv[li].uv for li in loops), Vector((0.0, 0.0))) / len(loops)
                for li in loops:
                    uv[li].uv = mid


def compose(model, maps):
    """Albedo = material colour x AO x grime (sRGB), metal-gloss = (metallic, 0, 0, smoothness)."""
    color = maps["color"][..., :3]
    metal = maps["metal"][..., 0]
    gloss = maps["metal"][..., 1]
    pos = maps["pos"][..., :3]
    ao = np.clip(maps["ao"][..., 0], 0, 1)
    covered = maps["color"][..., 3] > 0.5

    strength = getattr(model, "AO_STRENGTH", 0.75)
    ao_soft = 1.0 - strength + strength * ao ** 1.3
    grime = model.grime(pos, ao) if hasattr(model, "grime") else np.zeros(ao.shape, np.float32)
    grime_rgb = np.array(srgb_to_linear(hex_rgb(getattr(model, "GRIME", "#5A5248"))), np.float32)
    albedo = color * ao_soft[..., None]
    albedo = albedo * (1 - grime[..., None]) + grime_rgb * ao_soft[..., None] * grime[..., None]
    gloss = gloss * (1 - 0.6 * grime) * (0.6 + 0.4 * ao)
    metal = metal * (1 - 0.7 * grime)
    albedo = np.where(covered[..., None], albedo, 0.5)
    alb = linear_to_srgb(albedo)
    mg = np.stack([metal, np.zeros_like(metal), np.zeros_like(metal), gloss], -1)
    return texturing.downsample(alb, 2), texturing.downsample(mg, 2)


# ============================================================== LODs + export

def copy_object(ob, name):
    c = ob.copy()
    c.data = ob.data.copy()
    c.name = c.data.name = name
    for col in ob.users_collection:
        col.objects.link(c)
    return c


def decimate(ob, ratio):
    mod = ob.modifiers.new("Decimate", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    select_only([ob])
    bpy.ops.object.modifier_apply(modifier=mod.name)
    ob.data.set_sharp_from_angle(angle=math.radians(40))


def make_lods(model, root, objs):
    """Body (static parts merged) + movers at LOD0; LOD1 / LOD2 = everything merged and decimated.
    Unity's importer builds the LODGroup from the _LODn suffixes (as for aircraft).

    LOD_RATIOS = None: no LODs (the jet bridge, which the game assembles part by part). The static parts merge
    into the first one, which keeps its part name (no _LODn suffix, so Unity builds no LODGroup)."""
    movers = set(model.MOVERS)
    static = [ob for n, ob in objs.items() if n not in movers]
    select_only(static, static[0])
    bpy.ops.object.join()
    body = static[0]
    if model.LOD_RATIOS is None:
        return [body] + [objs[n] for n in model.MOVERS]
    body.name = body.data.name = "Body_LOD0"
    out = [body]
    for level, ratio in ((1, model.LOD_RATIOS[0]), (2, model.LOD_RATIOS[1])):
        ob = copy_object(body, f"Body_LOD{level}")
        extras = []
        for n in ([] if getattr(model, "SEPARATE_MOVERS", False) else model.MOVERS):
            c = copy_object(objs[n], f"{n}_merge{level}")
            c.parent = None
            c.matrix_world = objs[n].matrix_world.copy()
            extras.append(c)
        select_only([ob] + extras, ob)
        bpy.ops.object.join()
        ob.parent = root
        ob.matrix_parent_inverse = root.matrix_world.inverted()
        decimate(ob, ratio)
        out.append(ob)
    return out + [objs[n] for n in model.MOVERS]


def export_fbx(model, root, objs, path):
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for ob in objs:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"EMPTY", "MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="OFF", path_mode="STRIP", embed_textures=False, bake_anim=False)


def to_unity(v):
    """Blender (right, forward, up) -> Unity (x right, y up, z forward)."""
    return [round(float(v[0]), 4), round(float(v[2]), 4), round(float(v[1]), 4)]


def write_profile(model, objs, path):
    if hasattr(model, "BOUNDS"):
        # a model laid out apart for the bake (the jet bridge) gives its bounds: (right, forward, up) lo / hi
        lo, hi = (np.array(v, float) for v in model.BOUNDS)
    else:
        corners = np.array([ob.matrix_world @ Vector(c) for ob in objs for c in ob.bound_box])
        lo, hi = corners.min(axis=0), corners.max(axis=0)
    data = {
        "name": model.NAME,
        "displayName": model.DISPLAY_NAME,
        "kind": model.KIND,
        "bounds": {"min": to_unity(lo), "max": to_unity(hi)},
        "wheelbase": getattr(model, "WHEELBASE", 0.0),
        "wheels": [{"name": n, "centre": to_unity(c), "radius": r, "steerable": s} for n, c, r, s in getattr(model, "WHEELS", [])],
        "spinners": [{"name": n, "axis": to_unity(a), "rpm": rpm} for n, a, rpm in getattr(model, "SPINNERS", [])],
        "lights": [{"name": n, "type": t, "position": to_unity(p), "direction": to_unity(d), "color": c, "size": s,
                    "phase": ph} for n, t, p, d, c, s, ph in getattr(model, "LIGHTS", [])],
        "work": {k: (to_unity(v) if isinstance(v, (tuple, list)) else v) for k, v in getattr(model, "WORK", {}).items()},
        "parking": to_unity(getattr(model, "PARKING", (0, 0, 0))),
    }
    if hasattr(model, "TOW"):
        data["tow"] = {k: (to_unity(v) if isinstance(v, (tuple, list)) else v) for k, v in model.TOW.items()}
    if hasattr(model, "DOOR_PARTS"):
        data["doors"] = [{"name": n, "position": to_unity(p), "width": w, "height": h} for n, p, w, h in model.DOOR_PARTS]
    if hasattr(model, "INTERIOR"):
        lo, hi = model.INTERIOR
        data["interior"] = {"min": to_unity(lo), "max": to_unity(hi)}
    if hasattr(model, "BRIDGE"):
        data["bridge"] = model.BRIDGE          # plain numbers the game's jet-bridge rig shares, as given
    with open(path, "w") as fh:
        json.dump(data, fh, indent=2)
    print("wrote", path)


# ============================================================== previews

def setup_preview(model, textured, albedo_path=None, mg_path=None):
    sc = bpy.context.scene
    sc.render.engine = arg("--engine", "BLENDER_EEVEE")
    sc.render.resolution_x = int(arg("--w", 1280))
    sc.render.resolution_y = int(arg("--h", 800))
    sc.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    nt = world.node_tree
    sky = nt.nodes.new("ShaderNodeTexSky")
    nt.links.new(sky.outputs["Color"], nt.nodes["Background"].inputs["Color"])
    nt.nodes["Background"].inputs["Strength"].default_value = 0.35
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 4.0
    sun.angle = math.radians(2.5)
    so = bpy.data.objects.new("Sun", sun)
    so.rotation_euler = (math.radians(48), math.radians(8), math.radians(140))
    sc.collection.objects.link(so)
    me = bpy.data.meshes.new("Ground")
    g = 400
    me.from_pydata([(-g, -g, 0), (g, -g, 0), (g, g, 0), (-g, g, 0)], [], [(0, 1, 2, 3)])
    ground = bpy.data.objects.new("Ground", me)
    gm = bpy.data.materials.new("GroundMat")
    gm.use_nodes = True
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*getattr(model, "PREVIEW_GROUND", (0.3, 0.3, 0.31)), 1)
    gm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.9
    me.materials.append(gm)
    sc.collection.objects.link(ground)
    if textured:
        # one textured material like Unity's (albedo with AO + grime, metal / gloss)
        m = bpy.data.materials.new("Baked")
        m.use_nodes = True
        nt = m.node_tree
        bsdf = nt.nodes["Principled BSDF"]
        ti = nt.nodes.new("ShaderNodeTexImage")
        ti.image = texturing.load_image(albedo_path, "sRGB")
        nt.links.new(ti.outputs["Color"], bsdf.inputs["Base Color"])
        tm = nt.nodes.new("ShaderNodeTexImage")
        tm.image = texturing.load_image(mg_path, "Non-Color")
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(tm.outputs["Color"], sep.inputs["Color"])
        nt.links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])
        inv = nt.nodes.new("ShaderNodeMath")
        inv.operation = "SUBTRACT"
        inv.inputs[0].default_value = 1.0
        nt.links.new(tm.outputs["Alpha"], inv.inputs[1])
        nt.links.new(inv.outputs[0], bsdf.inputs["Roughness"])
        for ob in bpy.data.objects:
            if ob.type == "MESH" and ob.name != "Ground" and not ob.name.endswith(("_LOD1", "_LOD2")):
                lamp = [i for i, s in enumerate(ob.material_slots) if s.material and s.material.name.startswith("Lens")]
                for i, s in enumerate(ob.material_slots):
                    if i not in lamp:
                        s.material = m


def render_previews(model, folder, views, prefix):
    sc = bpy.context.scene
    cam = bpy.data.cameras.new("Cam")
    cam_ob = bpy.data.objects.new("Cam", cam)
    sc.collection.objects.link(cam_ob)
    sc.camera = cam_ob
    cam.clip_end = 2000
    os.makedirs(folder, exist_ok=True)
    open_views = getattr(model, "PREVIEW_OPEN", ())
    pose = getattr(model, "preview_pose", None)
    for n in views:
        # movers rolled up (the depot's doors) for these views
        if open_views:
            for m in getattr(model, "MOVERS", []):
                ob = bpy.data.objects.get(m)
                if ob is not None:
                    ob.scale = (1.0, 1.0, 0.05 if n in open_views else 1.0)
        if pose:
            pose(n)            # the model places its parts for this view (the jet bridge assembles itself, docked)
        loc, tgt, lens = model.VIEWS[n]
        cam_ob.location = loc
        cam.type = "PERSP"
        cam.lens = lens
        d = Vector(tgt) - cam_ob.location
        cam_ob.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = os.path.join(folder, f"{prefix}_{n}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", sc.render.filepath)


# ============================================================== main

def run():
    global TEX
    model = load_model()
    TEX = getattr(model, "BAKE_SIZE", TEX)
    out = os.path.join(VEHICLES, model.NAME, "Build")
    unity = os.path.join(ASSETS, model.NAME)
    root, objs, mats = build_objects(model)
    tris = sum(sum(len(p.vertices) - 2 for p in ob.data.polygons) for ob in objs.values())
    print("TRIANGLES", tris, {k: len(o.data.polygons) for k, o in objs.items()})
    if arg("--check"):
        check_normals(objs)
    if arg("--geometry"):
        # quick look at the untextured geometry
        setup_preview(model, False)
        render_previews(model, os.path.join(out, "previews"), (arg("--views") or ",".join(model.VIEWS)).split(","), "geo")
        return
    t0 = time.time()
    mesh_objs = list(objs.values())
    unwrap(mesh_objs)
    maps = bake(mesh_objs, model.AO_DISTANCE)
    print("baked in %.1fs" % (time.time() - t0))
    for n, axis in getattr(model, "STRETCH", {}).items():
        collapse_uvs(objs[n], axis)
    albedo, mg = compose(model, maps)
    os.makedirs(os.path.join(unity, "Textures"), exist_ok=True)
    os.makedirs(os.path.join(unity, "Models"), exist_ok=True)
    albedo_path = os.path.join(unity, "Textures", f"{model.NAME}_Albedo.png")
    mg_path = os.path.join(unity, "Textures", f"{model.NAME}_MetalGloss.png")
    texturing.save_png(albedo, albedo_path)
    texturing.save_png(mg, mg_path)            # alpha = smoothness
    write_profile(model, mesh_objs, os.path.join(unity, f"{model.NAME}.vehicle"))
    export = make_lods(model, root, objs)
    export_fbx(model, root, export, os.path.join(unity, "Models", f"{model.NAME}.fbx"))
    if arg("--previews"):
        setup_preview(model, True, albedo_path, mg_path)
        for ob in export:
            if ob.name.endswith(("_LOD1", "_LOD2")):
                ob.hide_render = True
        render_previews(model, os.path.join(out, "previews"), (arg("--views") or ",".join(model.VIEWS)).split(","), "tex")
    print("done in %.1fs" % (time.time() - t0))
