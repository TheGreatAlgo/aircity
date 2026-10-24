"""Render a registration-letter atlas (white glyphs, alpha = coverage) with Blender's
bundled Inter font (SIL OFL), and measure per-glyph ink extents."""
import os

import bpy
import numpy as np

CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-"
CELL = 128          # px, square cells
COLS = 8
SIZE = 1024         # atlas is SIZE x SIZE
BASELINE = 26       # px above the cell bottom
PX = 0.01           # metres per pixel in the render scene


def render_atlas(path, bold_px=3.0):
    prev = bpy.context.window.scene if bpy.context.window else bpy.context.scene
    sc = bpy.data.scenes.new("FontAtlas")
    font_path = os.path.join(bpy.utils.system_resource("DATAFILES", path="fonts"), "Inter.woff2")
    vfont = bpy.data.fonts.load(font_path, check_existing=True)
    mat = bpy.data.materials.new("FontWhite")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = (1, 1, 1, 1)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(em.outputs[0], out.inputs["Surface"])
    cap_h_target = 0.86 * (CELL - BASELINE - 10)
    cells = {}
    for i, ch in enumerate(CHARS):
        col, row = i % COLS, i // COLS
        cx, cy = col * CELL, SIZE - (row + 1) * CELL      # bottom-left of the cell, y up
        cu = bpy.data.curves.new("glyph_" + ch, "FONT")
        cu.body = ch
        cu.font = vfont
        cu.size = cap_h_target * PX / 0.727 * 1.28          # measured: Blender's size isn't quite 1 em
        cu.align_x = "LEFT"
        cu.materials.append(mat)
        ob = bpy.data.objects.new("glyph_" + ch, cu)
        ob.location = ((cx + 8) * PX, (cy + BASELINE) * PX, 0)
        sc.collection.objects.link(ob)
        cells[ch] = (cx, cy)
    cam = bpy.data.cameras.new("FontCam")
    cam.type = "ORTHO"
    cam.ortho_scale = SIZE * PX
    co = bpy.data.objects.new("FontCam", cam)
    co.location = (SIZE * PX / 2, SIZE * PX / 2, 5)
    sc.collection.objects.link(co)
    sc.camera = co
    w = bpy.data.worlds.new("FontWorld")
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Color"].default_value = (0, 0, 0, 1)
    sc.world = w
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x = SIZE
    sc.render.resolution_y = SIZE
    sc.render.resolution_percentage = 100
    sc.view_settings.view_transform = "Standard"
    sc.eevee.taa_render_samples = 16
    tmp = path + ".render.png"
    sc.render.filepath = tmp
    bpy.ops.render.render(write_still=True, scene=sc.name)
    img = bpy.data.images.load(tmp)
    px = np.empty(SIZE * SIZE * 4, np.float32)
    img.pixels.foreach_get(px)
    alpha = px.reshape(SIZE, SIZE, 4)[..., 0].copy()
    # embolden in image space (Blender's curve offset corrupts glyphs with counters, e.g. A, G, Q)
    r = int(np.ceil(bold_px))
    bold = alpha.copy()
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            d = np.hypot(dx, dy)
            if d > bold_px + 0.5:
                continue
            w = np.clip(bold_px + 0.5 - d, 0, 1)
            bold = np.maximum(bold, np.roll(np.roll(alpha, dy, 0), dx, 1) * w)
    alpha = bold
    bpy.data.images.remove(img)
    os.remove(tmp)

    font = {"cell_w": CELL, "cell_h": CELL, "baseline": BASELINE, "size": SIZE, "glyphs": {}}
    tops = []
    for ch, (cx, cy) in cells.items():
        a = alpha[cy:cy + CELL, cx:cx + CELL]
        cols = np.where(a.max(axis=0) > 0.08)[0]
        rows = np.where(a.max(axis=1) > 0.08)[0]
        font["glyphs"][ch] = {"cell_x": cx, "cell_y": cy, "ink_left": int(cols.min()), "ink_right": int(cols.max()) + 1}
        if ch in "HEFTIL":
            tops.append(rows.max() + 1)
    font["cap_top"] = float(np.median(tops))
    rgba = np.ones((SIZE, SIZE, 4), np.float32)
    rgba[..., 3] = alpha
    out = bpy.data.images.new("RegistrationFont", SIZE, SIZE, alpha=True)
    out.pixels.foreach_set(rgba.ravel())
    out.filepath_raw = path
    out.file_format = "PNG"
    out.save()
    if bpy.context.window:
        bpy.context.window.scene = prev
    bpy.data.scenes.remove(sc)
    return font, alpha
