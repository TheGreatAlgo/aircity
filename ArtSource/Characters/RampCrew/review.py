"""Render the built worker in Blender for quick review (no Unity needed).

    blender -b --factory-startup --python ArtSource/Characters/RampCrew/review.py -- OUTDIR [tone] [lod] [shots]

Opens Build/RampCrew.blend, textures LOD0 with the generated atlases and poses the skeleton exactly as
RampCrewRig.Apply does (Unity +Z forward maps to Blender +Y). Undriven bones (clavicles, fingers, thumbs,
toes) stay at rest relative to their parents, as they do with the current rig.
"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "../../.."))
TEX = os.path.join(ROOT, "Assets/AirCity/Characters/RampCrew/Textures")
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUTDIR = argv[0] if argv else os.path.join(HERE, "Build", "review")
TONE = int(argv[1]) if len(argv) > 1 else 0
LOD = int(argv[2]) if len(argv) > 2 else 0
ONLY = argv[3].split(",") if len(argv) > 3 else None
os.makedirs(OUTDIR, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "Build", "RampCrew.blend"))
scene = bpy.context.scene
rig = bpy.data.objects["Skeleton"]
for ob in bpy.data.objects:
    if ob.type == "MESH" and ob.name.startswith("Body_LOD"):
        ob.hide_render = ob.name != f"Body_LOD{LOD}"


def material():
    m = bpy.data.materials.new("Review"); m.use_nodes = True
    nt = m.node_tree; nodes = nt.nodes; links = nt.links
    bsdf = nodes["Principled BSDF"]
    base = nodes.new("ShaderNodeTexImage"); base.image = bpy.data.images.load(os.path.join(TEX, f"RampCrew_{TONE}.png"))
    gloss = nodes.new("ShaderNodeTexImage"); gloss.image = bpy.data.images.load(os.path.join(TEX, "RampCrew_MetalGloss.png"))
    gloss.image.colorspace_settings.name = "Non-Color"; gloss.image.alpha_mode = "CHANNEL_PACKED"
    normal = nodes.new("ShaderNodeTexImage"); normal.image = bpy.data.images.load(os.path.join(TEX, "RampCrew_Normal.png"))
    normal.image.colorspace_settings.name = "Non-Color"
    for node in (base, gloss, normal):
        node.interpolation = "Cubic" if False else "Linear"
    invert = nodes.new("ShaderNodeMath"); invert.operation = "SUBTRACT"; invert.inputs[0].default_value = 1
    links.new(gloss.outputs["Alpha"], invert.inputs[1]); links.new(invert.outputs[0], bsdf.inputs["Roughness"])
    sep = nodes.new("ShaderNodeSeparateColor"); links.new(gloss.outputs["Color"], sep.inputs[0])
    links.new(sep.outputs[0], bsdf.inputs["Metallic"])
    links.new(base.outputs["Color"], bsdf.inputs["Base Color"])
    nmap = nodes.new("ShaderNodeNormalMap"); nmap.inputs["Strength"].default_value = .65
    links.new(normal.outputs["Color"], nmap.inputs["Color"]); links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    return m


mat = material()
for ob in bpy.data.objects:
    if ob.type == "MESH" and ob.name.startswith("Body_LOD"):
        ob.data.materials.clear(); ob.data.materials.append(mat)

for engine in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
    try:
        scene.render.engine = engine; break
    except TypeError:
        continue
scene.view_settings.view_transform = "Standard"
world = bpy.data.worlds.new("World"); scene.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (.40, .44, .50, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = .9
sun_data = bpy.data.lights.new("Sun", "SUN"); sun_data.energy = 3.2; sun_data.angle = math.radians(3)
sun = bpy.data.objects.new("Sun", sun_data); scene.collection.objects.link(sun)
sun.rotation_euler = (math.radians(45), 0, math.radians(-155))
floor_mesh = bpy.data.meshes.new("Floor")
floor_mesh.from_pydata([(-3, -3, 0), (3, -3, 0), (3, 3, 0), (-3, 3, 0)], [], [(0, 1, 2, 3)])
floor = bpy.data.objects.new("Floor", floor_mesh); scene.collection.objects.link(floor)
fm = bpy.data.materials.new("FloorMat"); fm.use_nodes = True
fm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (.22, .24, .27, 1)
floor_mesh.materials.append(fm)
cam_data = bpy.data.cameras.new("Cam"); cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam); scene.camera = cam


# ---------------------------------------------------------------- RampCrewRig port (Unity space -> Blender)
def B(v):
    return Vector((v[0], v[2], v[1]))


rest = {}
for bone in rig.data.bones:
    rest[bone.name] = bone.matrix_local.copy()
CHILD = {"Hips": "Spine", "Spine": "Chest", "Chest": "Neck", "Neck": "Head"}
for s in "LR":
    CHILD.update({"UpperArm_" + s: "Forearm_" + s, "Forearm_" + s: "Hand_" + s, "Thigh_" + s: "Shin_" + s, "Shin_" + s: "Foot_" + s})
axis = {}
for name in rest:
    if name in CHILD:
        axis[name] = (rest[CHILD[name]].translation - rest[name].translation).normalized()
    elif name.startswith("Hand_"):
        axis[name] = (rest[name].translation - rest[name.replace("Hand_", "Forearm_")].translation).normalized()
    elif name.startswith("Foot_"):
        axis[name] = Vector((0, 1, 0))
    else:
        axis[name] = Vector((0, 0, 1))
DRIVEN = ["Hips", "Spine", "Chest", "Neck", "Head"] + [f"{b}_{s}" for s in "LR" for b in ("UpperArm", "Forearm", "Hand", "Thigh", "Shin", "Foot")]


def joint(a, b, upper, lower, pole):
    d = b - a; length = min(max(d.length, .015), upper + lower - .001)
    ax = d.normalized() if d.length_squared > 1e-5 else Vector((0, -1, 0))
    across = (pole - ax * pole.dot(ax)).normalized()
    if across.length_squared < .1:
        f = Vector((0, 0, 1)); across = (f - ax * f.dot(ax)).normalized()
    along = (upper * upper - lower * lower + length * length) / (2 * length)
    return a + ax * along + across * math.sqrt(max(0, upper * upper - along * along))


def apply_pose(hip_height=.94, lean=0., stride=0., phase=0., hands=False, seated=False, kneeling=False,
               left_hand=(0, 0, 0), right_hand=(0, 0, 0), left_foot=(0, 0, 0), right_foot=(0, 0, 0)):
    """Arguments in Unity space (x right, y up, z forward), like RampCrewPose."""
    targets = {}
    def bone_at(name, point_u, dir_u):
        targets[name] = (B(point_u), B(dir_u).normalized())
    V = Vector
    gait = phase * math.tau
    hips = V((0, hip_height + abs(math.sin(gait)) * .018 * stride, 0))
    up = V((0, math.cos(math.radians(lean)), math.sin(math.radians(lean))))
    spine = hips + up * .16; chest = spine + up * .22; neck = chest + up * .17; head = neck + V((0, 1, 0)) * .04
    bone_at("Hips", hips, up); bone_at("Spine", spine, up); bone_at("Chest", chest, up)
    bone_at("Neck", neck, V((0, 1, 0))); bone_at("Head", head, V((0, 1, 0)))
    for i in range(2):
        side = "L" if i == 0 else "R"; sign = -1 if i == 0 else 1
        swing = math.sin(gait + i * math.pi) * stride
        if seated:
            ankle = V(left_foot if i == 0 else right_foot) + V((0, .09, 0))
        elif kneeling:
            ankle = V((sign * .14, .10, -.28))
        else:
            ankle = V((sign * .105, .09 + max(0, math.cos(gait + i * math.pi)) * .10 * stride, swing * .29))
        hip = hips + V((sign * .105, 0, 0))
        if kneeling:
            knee = V((sign * .14, .085, math.sqrt(max(.01, .421 ** 2 - (hips.y - .085) ** 2))))
        else:
            knee = joint(hip, ankle, .421, .430, V((0, 0, 1)))
        if kneeling:
            ankle = knee + V((0, .005, -.430))
        bone_at("Thigh_" + side, hip, knee - hip); bone_at("Shin_" + side, knee, ankle - knee)
        bone_at("Foot_" + side, ankle, V((0, 0, -1)) if kneeling else V((0, 0, 1)))
        shoulder = chest + up * .08 + V((sign * .205, 0, 0))
        palm = V(left_hand if i == 0 else right_hand) if hands else \
            hips + V((sign * .245, -.17 + abs(swing) * .045, .025 - swing * .20))
        hand_dir = V((0, -.65, .76)).normalized() if hands else V((0, -1, 0))
        wrist = palm - hand_dir * .075
        elbow = joint(shoulder, wrist, .30, .27, V((sign * .8, -.5, -.45)))
        bone_at("UpperArm_" + side, shoulder, elbow - shoulder); bone_at("Forearm_" + side, elbow, wrist - elbow)
        bone_at("Hand_" + side, wrist, hand_dir)
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    order = [b.name for b in rig.data.bones]  # parents precede children
    for name in order:
        if name not in targets:
            continue
        point, direction = targets[name]
        q = axis[name].rotation_difference(direction)
        m = (q.to_matrix() @ rest[name].to_3x3()).to_4x4(); m.translation = point
        rig.pose.bones[name].matrix = m
        bpy.context.view_layer.update()


def shoot(name, target, offset, lens=50, size=(900, 1100)):
    if ONLY and name not in ONLY:
        return
    scene.render.resolution_x, scene.render.resolution_y = size
    cam.data.lens = lens
    cam.location = Vector(target) + Vector(offset)
    direction = Vector(target) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(OUTDIR, name + ".png")
    bpy.ops.render.render(write_still=True)


T = (0, 0, .92)
apply_pose()
shoot("front", T, (0, 4.2, .25), 50)
shoot("side", T, (4.2, 0, .25), 50)
shoot("back", T, (0, -4.2, .25), 50)
shoot("three-quarter", T, (2.4, 3.3, .45), 50)
shoot("face", (0, 0, 1.64), (.20, .62, .03), 85, (900, 900))
shoot("face-front", (0, 0, 1.64), (0, .66, .02), 85, (900, 900))
shoot("face-side", (0, 0, 1.64), (.66, .04, .02), 85, (900, 900))
shoot("hand", (.27, .03, .66), (.30, .45, .06), 85, (900, 900))
shoot("vest-front", (0, 0, 1.22), (0, 1.25, .08), 50, (900, 900))
shoot("vest-back", (0, 0, 1.22), (0, -1.25, .08), 50, (900, 900))
shoot("vest-34", (0, 0, 1.25), (.8, .95, .15), 50, (900, 900))
shoot("vest-armhole", (.17, 0, 1.33), (.55, .35, .12), 50, (900, 900))
shoot("cuff", (.27, .02, .76), (.30, .38, .10), 85, (900, 900))
shoot("vest-hem-back", (0, -.1, 1.0), (.25, -.6, -.05), 50, (900, 900))
shoot("boots", (0, .08, .08), (.6, .8, .3), 50, (900, 900))
apply_pose(stride=.8, phase=.18)
shoot("walk", T, (2.4, 3.3, .45), 50)
apply_pose(hip_height=.69, lean=28, hands=True, left_hand=(-.19, .62, .52), right_hand=(.19, .62, .52))
shoot("squat", (0, 0, .70), (2.6, 2.6, .5), 50)
shoot("squat-side", (0, 0, .70), (3.6, 0, .4), 50)
shoot("squat-hips", (0, -.1, .62), (1.0, -1.0, .2), 50, (900, 900))
apply_pose(hip_height=.35, lean=60, kneeling=True, hands=True, left_hand=(-.2, .30, .55), right_hand=(.2, .30, .55))
shoot("kneel", (0, 0, .50), (2.6, 2.6, .6), 50)
shoot("kneel-side", (0, 0, .50), (3.4, 0, .4), 50)
shoot("kneel-hips", (0, -.15, .40), (1.1, -.6, .25), 50, (900, 900))
apply_pose(hip_height=.94, lean=8, hands=True, left_hand=(-.17, 1.22, .40), right_hand=(.17, 1.22, .40))
shoot("carry", T, (2.4, 3.3, .45), 50)
shoot("carry-shoulder", (0, 0, 1.35), (.9, .9, .2), 50, (900, 900))
apply_pose(hip_height=.94, hands=True, left_hand=(-.28, 2.02, .12), right_hand=(.28, 2.02, .12))
shoot("overhead", (0, 0, 1.1), (2.4, 3.3, .45), 50)
shoot("overhead-front", (0, 0, 1.1), (0, 4.2, .3), 50)
apply_pose(hip_height=.62, seated=True, hands=True, left_hand=(-.20, .98, .46), right_hand=(.20, .98, .46),
           left_foot=(-.16, .02, .50), right_foot=(.16, .02, .50))
shoot("seated", (0, 0, .75), (2.6, 2.6, .5), 50)


def bend(name, degrees, axis="X"):
    pb = rig.pose.bones[name]
    pb.rotation_mode = "XYZ"
    setattr(pb.rotation_euler, "xyz"["XYZ".index(axis)], math.radians(degrees))
    bpy.context.view_layer.update()


# Added bones driven (Blender bone X; Unity's local X has the opposite sign): fist, open hand, toes, shrug.
apply_pose(hip_height=.94, lean=8, hands=True, left_hand=(-.17, 1.22, .40), right_hand=(.17, 1.22, .40))
for s in "LR":
    bend("Fingers_" + s, -80); bend("Thumb_" + s, -35)
shoot("fist", (.17, .40, 1.20), (.25, .35, .15), 85, (900, 900))
shoot("fist-side", (.17, .40, 1.20), (.45, 0, .02), 85, (900, 900))
for s in "LR":
    bend("Fingers_" + s, 15); bend("Thumb_" + s, 15)
shoot("open-hand", (.17, .40, 1.20), (.25, .35, .15), 85, (900, 900))
shoot("open-hand-side", (.17, .40, 1.20), (.45, 0, .02), 85, (900, 900))
apply_pose(hip_height=.35, lean=60, kneeling=True, hands=True, left_hand=(-.2, .30, .55), right_hand=(.2, .30, .55))
for s in "LR":
    bend("Toe_" + s, 40)
shoot("kneel-toes", (0, -.25, .15), (.9, -.7, .25), 50, (900, 900))
apply_pose()
for s in "LR":
    bend("Clavicle_" + s, 12)
shoot("shrug", (0, 0, 1.45), (0, 1.4, .1), 50, (900, 900))
