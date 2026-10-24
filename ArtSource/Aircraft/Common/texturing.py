"""UV packing and Cycles bakes for the livery atlas.

Everything painted (material "Body") shares one 0..1 UV atlas. We bake world
position / normal / part-id maps into that atlas so livery masks can be written
as plain numpy functions of 3D position (see paint.py), plus an AO map.
"""
import bmesh
import bpy
import numpy as np
from mathutils import Vector

TEX = 2048


# ------------------------------------------------------------------ tags

def assign_global_tags(objs):
    tags = sorted({t for ob in objs for t in ob.data["part_tags"]})
    for ob in objs:
        me = ob.data
        local = list(me["part_tags"])
        src = me.attributes["part_tag"].data
        vals = [tags.index(local[d.value]) for d in src]
        if "tag_gid" in me.attributes:
            me.attributes.remove(me.attributes["tag_gid"])
        a = me.attributes.new("tag_gid", "INT", "FACE")
        a.data.foreach_set("value", vals)
    return tags


# ------------------------------------------------------------------ UV packing

def _select_only(objs):
    for ob in bpy.context.view_layer.objects:
        ob.select_set(False)
    for ob in objs:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def pack_body_uvs(objs, margin=0.0035):
    """Pack every Body-material face of every object into one 0..1 atlas.
    The unpacked metric UVs are kept in a "UVraw" layer (used to place the registration)."""
    for ob in objs:
        if "UVraw" not in ob.data.uv_layers:
            ob.data.uv_layers.new(name="UVraw", do_init=True)
        ob.data.uv_layers.active = ob.data.uv_layers["UVMap"]
    _select_only(objs)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.context.scene.tool_settings.use_uv_select_sync = True
    for ob in objs:
        bm = bmesh.from_edit_mesh(ob.data)
        body = {i for i, m in enumerate(ob.data.materials) if m and m.name == "Body"}
        for v in bm.verts:
            v.select_set(False)
        for f in bm.faces:
            f.select_set(f.material_index in body)
        bm.select_flush_mode()
        bmesh.update_edit_mesh(ob.data)
    bpy.ops.uv.pack_islands(udim_source="CLOSEST_UDIM", rotate=False, scale=True, merge_overlap=False,
                            margin_method="FRACTION", margin=margin, shape_method="CONCAVE")
    bpy.ops.object.mode_set(mode="OBJECT")
    # The packer picks a UDIM tile and islands can poke slightly past it: fit the packed
    # bounding box back into 0..1 (uniform scale, so texel density stays even).
    lo = np.array([np.inf, np.inf])
    hi = -lo
    for ob in objs:
        for u in body_uvs(ob):
            lo = np.minimum(lo, u.min(axis=0))
            hi = np.maximum(hi, u.max(axis=0))
    pad = 2.0 / TEX
    scale = (1.0 - 2 * pad) / max(hi - lo)
    print("uv pack bbox", lo.round(4), hi.round(4), "-> scale %.4f" % scale)
    for ob in objs:
        me = ob.data
        uvl = me.uv_layers.active.data
        body = {i for i, m in enumerate(me.materials) if m and m.name == "Body"}
        for p in me.polygons:
            if p.material_index in body:
                for li in p.loop_indices:
                    uvl[li].uv = (pad + (uvl[li].uv[0] - lo[0]) * scale, pad + (uvl[li].uv[1] - lo[1]) * scale)


def body_uvs(ob):
    me = ob.data
    uvl = me.uv_layers.active.data
    body = {i for i, m in enumerate(me.materials) if m and m.name == "Body"}
    out = [np.array([uvl[li].uv[:] for li in p.loop_indices]) for p in me.polygons if p.material_index in body]
    return out


# ------------------------------------------------------------------ bakes

def _new_image(name, float_buffer=True, color=(0, 0, 0, 0)):
    if name in bpy.data.images:
        bpy.data.images.remove(bpy.data.images[name])
    img = bpy.data.images.new(name, TEX, TEX, alpha=True, float_buffer=float_buffer)
    img.colorspace_settings.name = "Non-Color"
    img.generated_color = color
    return img


def _bake_material(kind, img, pos_lo=(0, 0, 0), pos_span=1.0):
    m = bpy.data.materials.new("BAKE_" + kind)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(em.outputs[0], out.inputs["Surface"])
    if kind in ("pos", "nrm"):
        g = nt.nodes.new("ShaderNodeNewGeometry")
        add = nt.nodes.new("ShaderNodeVectorMath")
        add.operation = "ADD"
        mul = nt.nodes.new("ShaderNodeVectorMath")
        mul.operation = "SCALE"
        if kind == "pos":   # encode world position into 0..1: (p - lo) / span
            nt.links.new(g.outputs["Position"], add.inputs[0])
            add.inputs[1].default_value = tuple(-c for c in pos_lo)
            mul.inputs["Scale"].default_value = 1.0 / pos_span
        else:               # normal -> (n + 1) / 2
            nt.links.new(g.outputs["Normal"], add.inputs[0])
            add.inputs[1].default_value = (1.0, 1.0, 1.0)
            mul.inputs["Scale"].default_value = 0.5
        nt.links.new(add.outputs[0], mul.inputs[0])
        nt.links.new(mul.outputs[0], em.inputs["Color"])
    elif kind == "tag":
        a = nt.nodes.new("ShaderNodeAttribute")
        a.attribute_type = "GEOMETRY"
        a.attribute_name = "tag_gid"
        mth = nt.nodes.new("ShaderNodeMath")
        mth.operation = "MULTIPLY_ADD"
        mth.inputs[1].default_value = 1.0 / 64.0
        mth.inputs[2].default_value = 0.5 / 64.0
        nt.links.new(a.outputs["Fac"], mth.inputs[0])
        nt.links.new(mth.outputs[0], em.inputs["Color"])
    else:
        em.inputs["Color"].default_value = (1, 1, 1, 1)
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.nodes.active = tex
    tex.select = True
    return m


def _cycles(samples):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    prefs = bpy.context.preferences.addons["cycles"].preferences
    prefs.compute_device_type = "METAL"
    prefs.get_devices()
    for d in prefs.devices:
        d.use = True
    sc.cycles.device = "GPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = False


def _swap_materials(objs, body_mat, dummy_mat):
    saved = []
    for ob in objs:
        for i, slot in enumerate(ob.material_slots):
            saved.append((ob, i, slot.material))
            ob.material_slots[i].material = body_mat if slot.material and slot.material.name == "Body" else dummy_mat
    return saved


def _restore(saved):
    for ob, i, m in saved:
        ob.material_slots[i].material = m


def _read(img):
    a = np.empty(TEX * TEX * 4, np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(TEX, TEX, 4)


def bake_maps(objs, ao_samples=192, ao_distance=0.9):
    """Returns dict of numpy maps (H, W, ...) in the Body atlas."""
    corners = np.array([ob.matrix_world @ Vector(c) for ob in objs for c in ob.bound_box])
    pos_lo = corners.min(axis=0) - 1.0
    pos_span = float((corners.max(axis=0) - corners.min(axis=0)).max() + 2.0)
    _select_only(objs)
    dummy_img = _new_image("BAKE_dummy", float_buffer=False)
    dummy = _bake_material("dummy", dummy_img)
    sc = bpy.context.scene
    sc.render.bake.margin = 10
    sc.render.bake.margin_type = "ADJACENT_FACES"
    maps = {}
    for kind, samples, margin in (("pos", 4, 10), ("nrm", 4, 10), ("tag", 1, 10), ("white", 1, 0)):
        img = _new_image("BAKE_" + kind)
        mat = _bake_material(kind, img, pos_lo, pos_span)
        saved = _swap_materials(objs, mat, dummy)
        _cycles(samples)
        sc.render.bake.margin = margin
        bpy.ops.object.bake(type="EMIT", margin=margin, margin_type="ADJACENT_FACES", use_clear=True)
        _restore(saved)
        maps[kind] = _read(img)
        print("baked", kind)

    # ambient occlusion
    if sc.world is None:
        sc.world = bpy.data.worlds.new("BakeWorld")
    sc.world.light_settings.distance = ao_distance
    img = _new_image("BAKE_ao")
    mat = _bake_material("white", img)
    saved = _swap_materials(objs, mat, dummy)
    _cycles(ao_samples)
    bpy.ops.object.bake(type="AO", margin=10, margin_type="ADJACENT_FACES", use_clear=True)
    _restore(saved)
    maps["ao"] = _read(img)
    print("baked ao")

    out = {
        "pos": maps["pos"][..., :3] * pos_span + pos_lo.astype(np.float32),
        "nrm": maps["nrm"][..., :3] * 2.0 - 1.0,
        "tag": np.floor(maps["tag"][..., 0] * 64.0).astype(np.int32),
        "cov": maps["white"][..., 0] > 0.5,
        "valid": maps["pos"][..., 3] > 0.5,   # covered incl. bake margin
        "ao": maps["ao"][..., 0],
    }
    return out


# ------------------------------------------------------------------ image io

def downsample(arr, factor=2):
    """Box-filter (H, W[, C]) by an integer factor."""
    h, w = arr.shape[:2]
    a = arr[:h - h % factor, :w - w % factor]
    return a.reshape(h // factor, factor, w // factor, factor, *arr.shape[2:]).mean(axis=(1, 3))


def save_png(arr, path, name=None):
    """arr: (H, W, 3|4) or (H, W) floats in 0..1 (stored as-is, i.e. already sRGB-encoded)."""
    arr = np.asarray(arr, np.float32)
    if arr.ndim == 2:
        arr = np.repeat(arr[..., None], 3, axis=2)
    h, w = arr.shape[:2]
    if arr.shape[2] == 3:
        arr = np.concatenate([arr, np.ones((h, w, 1), np.float32)], axis=2)
    name = name or path.split("/")[-1]
    if name in bpy.data.images:
        bpy.data.images.remove(bpy.data.images[name])
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.colorspace_settings.name = "sRGB"
    img.pixels.foreach_set(np.clip(arr, 0, 1).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def load_image(path, colorspace="sRGB"):
    img = bpy.data.images.load(path, check_existing=True)
    img.reload()
    img.colorspace_settings.name = colorspace
    return img
