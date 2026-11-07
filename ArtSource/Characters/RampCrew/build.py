"""Generate the shared ramp worker, skinned LODs and four fabric/skin atlases.

    blender -b --factory-startup --python ArtSource/Characters/RampCrew/build.py

Metres, Blender +Y forward, +Z up. Keep joints aligned with RampCrewRig.cs.
Clothing is fused before skinning to avoid disconnected shoulders, hips and knees.
All detail uses one material per LOD; there are no downloaded character assets.
"""
import json
import math
import os
import struct
import zlib

import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../../.."))
OUT = os.path.join(ROOT, "Assets/AirCity/Characters/RampCrew")
HERE = os.path.dirname(os.path.abspath(__file__))
os.makedirs(os.path.join(HERE, "Build"), exist_ok=True)
for folder in ("Models", "Textures"):
    os.makedirs(os.path.join(OUT, folder), exist_ok=True)

COLORS = ["344452", "293745", "c1cf42", "b6bcb5", "32383b", "292c2b", "b58567", "885c46",
          "c0967a", "293e4b", "8d9693", "464e4c", "d9d9c9", "475663", "403c33", "292c29"]
TONES = [("b58567", "885c46", "c0967a"), ("76513d", "513728", "936c50"),
         ("ceaa90", "a77c63", "dfbda5"), ("9b7151", "6e4b34", "b38965")]
M = dict(shirt=0, trousers=1, vest=2, silver=3, belt=4, boot=5, skin=6, shade=7, light=8,
         headset=9, glove=10, glove_pad=11, badge=12, seam=13, hair=14, eye=15)
TILE = 128


def png(path, pixels):
    height, width = pixels.shape[:2]
    raw = b"".join(b"\0" + row.tobytes() for row in pixels.astype(np.uint8))
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xffffffff)
    with open(path, "wb") as stream:
        stream.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)) +
                     chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def textures():
    """Padded 4x4 atlas: gentle weave, broad cloth creases and material-specific roughness.

    Sample inside each tile's gutter so filtering and mipmaps cannot expose adjacent colours.
    A fixed seed makes art regeneration reproducible. Normal/roughness are shared by all tones.
    """
    yy, xx = np.mgrid[0:TILE, 0:TILE] / TILE
    rng = np.random.default_rng(3407)
    noise = rng.normal(0, 1, (TILE, TILE))
    weave = np.sin(xx * math.tau * 48) * np.sin(yy * math.tau * 48)
    creases = np.sin(xx * math.tau * 3 + .8 * np.sin(yy * math.tau))
    creases += .35 * np.sin(yy * math.tau * 5 + xx * 9)
    normal = np.full((TILE * 4, TILE * 4, 4), 255, dtype=np.uint8)
    metal = normal.copy()
    for variation, tone in enumerate(TONES):
        colors = COLORS.copy(); colors[6:9] = tone
        atlas = normal.copy()
        for index, color in enumerate(colors):
            cloth = index in (0, 1, 2, 10, 13)
            relief = (.003 * creases + .00035 * weave) if cloth else .00008 * noise
            dy, dx = np.gradient(relief, 1 / TILE)
            n = np.dstack((-dx, dy, np.ones_like(dx)))
            n /= np.linalg.norm(n, axis=2, keepdims=True)
            rgb = np.array(list(bytes.fromhex(color)), dtype=float)
            modulation = 1 + (.026 * creases + .012 * weave + .006 * noise if cloth else .005 * noise)
            if index in (6, 7, 8):
                modulation += .022 * np.sin(yy * math.pi)
            tile = np.dstack((np.clip(rgb[None, None, :] * modulation[:, :, None], 0, 255), np.full_like(xx, 255)))
            row, col = 3 - index // 4, index % 4
            area = (slice(row * TILE, (row + 1) * TILE), slice(col * TILE, (col + 1) * TILE))
            atlas[area] = tile.astype(np.uint8)
            normal[area] = np.dstack(((n * .5 + .5) * 255, np.full_like(xx, 255))).astype(np.uint8)
            gloss = {3: 115, 4: 58, 5: 43, 6: 65, 7: 65, 8: 65, 9: 48, 11: 40, 15: 100}.get(index, 28)
            metal[area] = (8 if index == 3 else 0, 0, 0, gloss)
        png(os.path.join(OUT, "Textures", f"RampCrew_{variation}.png"), atlas)
    png(os.path.join(OUT, "Textures", "RampCrew_Normal.png"), normal)
    png(os.path.join(OUT, "Textures", "RampCrew_MetalGloss.png"), metal)


textures()
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)

JOINTS = {"Hips": (0, 0, .94), "Spine": (0, 0, 1.10), "Chest": (0, 0, 1.32),
          "Neck": (0, 0, 1.49), "Head": (0, 0, 1.53)}
PARENTS = {"Hips": None, "Spine": "Hips", "Chest": "Spine", "Neck": "Chest", "Head": "Neck"}
for side, sign in (("L", -1), ("R", 1)):
    for name, position, parent in (
            ("UpperArm", (sign*.205, 0, 1.40), "Chest"),
            ("Forearm", (sign*.505, 0, 1.40), "UpperArm_"+side),
            ("Hand", (sign*.775, 0, 1.40), "Forearm_"+side),
            ("Thigh", (sign*.105, 0, .94), "Hips"),
            ("Shin", (sign*.105, .015, .52), "Thigh_"+side),
            ("Foot", (sign*.105, 0, .09), "Shin_"+side),
            ("Toe", (sign*.105, .18, .055), "Foot_"+side)):
        JOINTS[name+"_"+side] = position
        PARENTS[name+"_"+side] = parent


class Mesh:
    def __init__(self):
        self.vertices, self.faces, self.colors, self.weights, self.uvs = [], [], [], [], []

    def vertex(self, p, weights, uv=(.5, .5)):
        self.vertices.append(tuple(p)); self.weights.append(weights); self.uvs.append(uv)
        return len(self.vertices)-1

    def face(self, indices, material):
        self.faces.append(indices); self.colors.append(M[material])

    def loft(self, rings, material, weights, cap=True, wrap=True):
        indices = [[self.vertex(p, weights[k] if isinstance(weights, list) else weights,
                                (j / (len(ring)-1), k / max(1, len(rings)-1)))
                    for j, p in enumerate(ring)] for k, ring in enumerate(rings)]
        for a, b in zip(indices, indices[1:]):
            for k in range(len(a) if wrap else len(a)-1):
                self.face((a[k], a[(k+1) % len(a)], b[(k+1) % len(a)], b[k]), material)
        if cap:
            self.face(tuple(indices[0][::-1]), material); self.face(tuple(indices[-1]), material)

    def ellipse(self, centre, radii, material, bone, segments=16, rings=8):
        points = []
        for j in range(rings+1):
            latitude = -.5*math.pi + math.pi*(j+.015)/(rings+.03)
            points.append([(centre[0]+radii[0]*math.cos(latitude)*math.cos(i*math.tau/segments),
                            centre[1]+radii[1]*math.cos(latitude)*math.sin(i*math.tau/segments),
                            centre[2]+radii[2]*math.sin(latitude)) for i in range(segments)])
        self.loft(points, material, {bone: 1})

    def tube(self, a, b, radii, material, weights, segments=16):
        a, b = Vector(a), Vector(b)
        axis = (b-a).normalized()
        u = axis.cross(Vector((0, 1, 0))).normalized()
        if u.length < .01: u = axis.cross(Vector((1, 0, 0))).normalized()
        v = axis.cross(u).normalized()
        self.loft([[a.lerp(b, t) + u*(ru*math.cos(i*math.tau/segments)) + v*(rv*math.sin(i*math.tau/segments))
                    for i in range(segments)] for t, ru, rv in radii], material, weights)

    def ribbon(self, points, width, material, bone):
        """A thin, curved seam/tape that sits on the underlying garment."""
        self.loft([[(x-width/2, y, z), (x+width/2, y, z)] for x, y, z in points],
                  material, {bone: 1}, cap=False, wrap=False)

    def object(self, name):
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(self.vertices, [], self.faces); mesh.update()
        uv = mesh.uv_layers.new(name="UVMap")
        for polygon, color in zip(mesh.polygons, self.colors):
            polygon.use_smooth = True
            for li in polygon.loop_indices:
                u, v = self.uvs[mesh.loops[li].vertex_index]
                uv.data[li].uv = ((color % 4 + .08 + u*.84)/4, (color // 4 + .08 + v*.84)/4)
        ob = bpy.data.objects.new(name, mesh); bpy.context.scene.collection.objects.link(ob)
        for name in JOINTS: ob.vertex_groups.new(name=name)
        for i, weights in enumerate(self.weights):
            total = sum(weights.values())
            for name, value in weights.items(): ob.vertex_groups[name].add([i], value/total, "REPLACE")
        # Tube winding depends on the authored chain direction; recalculate every closed island.
        bpy.context.view_layer.objects.active = ob; ob.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.normals_make_consistent(inside=False); bpy.ops.object.mode_set(mode="OBJECT")
        ob.select_set(False)
        return ob


def fuse(source, name, material, voxel=.009):
    """Join intersecting cloth volumes, relax their silhouette and transfer smooth bone weights.

    Only the base garments are remeshed. Tape, facial features and PPE keep their authored shapes.
    """
    ob = source.object(name)
    tree = KDTree(len(source.vertices))
    for i, p in enumerate(source.vertices): tree.insert(p, i)
    tree.balance()
    bpy.context.view_layer.objects.active = ob
    remesh = ob.modifiers.new("Continuous clothing", "REMESH")
    remesh.mode = "VOXEL"; remesh.voxel_size = voxel; remesh.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier=remesh.name)
    smooth = ob.modifiers.new("Relax seams", "SMOOTH"); smooth.factor = 1.1; smooth.iterations = 4
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    reduce = ob.modifiers.new("Cloth topology", "DECIMATE"); reduce.ratio = .32
    bpy.ops.object.modifier_apply(modifier=reduce.name)
    # Transfer from the nearest four source samples rather than rigidly assigning each new island.
    for group in ob.vertex_groups: group.remove(range(len(ob.data.vertices)))
    for vertex in ob.data.vertices:
        weights = {}
        for _, index, distance in tree.find_n(vertex.co, 4):
            influence = 1 / max(.002, distance)**2
            for bone, weight in source.weights[index].items():
                weights[bone] = weights.get(bone, 0) + weight*influence
        weights = dict(sorted(weights.items(), key=lambda p: p[1], reverse=True)[:4])
        total = sum(weights.values())
        for bone, weight in weights.items(): ob.vertex_groups[bone].add([vertex.index], weight/total, "REPLACE")
    # Cylindrical fabric UVs; limbs have their own longitudinal mapping to avoid stretched weave.
    uv = ob.data.uv_layers.active or ob.data.uv_layers.new(name="UVMap")
    color = M[material]
    for polygon in ob.data.polygons:
        polygon.use_smooth = True
        for li in polygon.loop_indices:
            p = ob.data.vertices[ob.data.loops[li].vertex_index].co
            if material == "shirt" and abs(p.x) > .23:
                u = math.atan2(p.y, p.z-1.4)/math.tau + .5; v = (abs(p.x)-.2)/.23
            else:
                centre = math.copysign(.105, p.x) if material == "trousers" and p.z < .88 else 0
                u = math.atan2(p.y, p.x-centre)/math.tau + .5
                v = (p.z-.1)/.98 if material == "trousers" else (p.z-1.02)/.44
            uv.data[li].uv = ((color % 4 + .08 + .84*u)/4, (color // 4 + .08 + .84*min(1,max(0,v)))/4)
    return ob


def torso_weights(z):
    if z <= 1.08: return {"Hips": .35, "Spine": .65}
    chest = min(1, max(0, (z-1.10)/.18))
    return {"Spine": 1-chest, "Chest": chest}


def ring(z, rx, ry, count=32, cy=0, wrinkle=0):
    return [(rx*math.cos(t)*(1+wrinkle*math.sin(7*t+z*30)),
             cy+ry*math.sin(t)*(1+wrinkle*math.sin(5*t-z*20)), z)
            for t in (i*math.tau/count for i in range(count))]


shirt, pants, detail = Mesh(), Mesh(), Mesh()
body = [(1.025,.158,.106),(1.07,.157,.111),(1.16,.171,.121),(1.29,.190,.116),
        (1.365,.207,.102),(1.407,.190,.084),(1.447,.076,.057)]
shirt.loft([ring(*r, wrinkle=.018) for r in body], "shirt", [torso_weights(r[0]) for r in body])
pants.ellipse((0,-.006,.978),(.167,.116,.140),"trousers","Hips",24,14)
# A soft collar rises over the base of the neck instead of exposing a cylindrical stalk.
detail.loft([ring(1.431,.077,.062),ring(1.469,.056,.053)],"shirt",{"Chest":.75,"Neck":.25})
detail.tube((0,0,1.448),(0,.003,1.57),[(0,.050,.047),(.55,.047,.045),(1,.049,.048)],"skin",{"Neck":1},24)

for side, sign in (("L",-1),("R",1)):
    upper, fore, hand = (name+"_"+side for name in ("UpperArm","Forearm","Hand"))
    a, b, c = (Vector(JOINTS[name]) for name in (upper,fore,hand))
    sleeve = a.lerp(b,.59)
    shirt.tube(a-Vector((sign*.065,0,0)),sleeve,
               [(0,.046,.068),(.24,.061,.076),(.56,.063,.070),(1,.057,.061)],"shirt",
               [{"Chest":.7,upper:.3},{"Chest":.3,upper:.7},{upper:1},{upper:1}],24)
    # One continuous exposed arm across the elbow, with an anatomical forearm taper.
    detail.loft([[a.lerp(b,t)+Vector((0,ry*math.sin(i*math.tau/20),rz*math.cos(i*math.tau/20)))
                  for i in range(20)] for t,ry,rz in ((.55,.055,.052),(.78,.048,.049),(1,.043,.045))] +
                [[b.lerp(c,t)+Vector((0,ry*math.sin(i*math.tau/20),rz*math.cos(i*math.tau/20)))
                  for i in range(20)] for t,ry,rz in ((.18,.049,.046),(.48,.043,.039),(.8,.033,.031),(1,.028,.027))],
                "skin", [{upper:1},{upper:.9,fore:.1},{upper:.5,fore:.5},{fore:1},{fore:1},{fore:1},{fore:.9,hand:.1}])
    detail.tube(c,c+Vector((sign*.103,0,0)),[(0,.028,.032),(.25,.024,.039),(.75,.023,.040),(1,.020,.035)],"glove",{hand:1},20)
    for finger, length in enumerate((.052,.066,.061,.046)):
        start = c+Vector((sign*.094,-.029+finger*.019,0))
        detail.tube(start,start+Vector((sign*length,0,-.010)),
                    [(0,.009,.010),(.5,.010,.010),(1,.007,.008)],"glove_pad",{hand:1},8)
    thumb = c+Vector((sign*.035,.035,-.005))
    detail.tube(thumb,thumb+Vector((sign*.060,.020,-.015)),[(0,.016,.015),(.5,.014,.013),(1,.010,.010)],"glove_pad",{hand:1},10)
    thigh, shin, foot = (name+"_"+side for name in ("Thigh","Shin","Foot"))
    a,b,c = (Vector(JOINTS[name]) for name in (thigh,shin,foot))
    pants.loft([[a.lerp(b,t)+Vector((rx*math.cos(i*math.tau/24),ry*math.sin(i*math.tau/24),0))
                 for i in range(24)] for t,rx,ry in ((-.12,.089,.100),(.12,.096,.106),(.4,.088,.097),(.75,.075,.081),(1,.066,.072))] +
               [[b.lerp(c,t)+Vector((rx*math.cos(i*math.tau/24),ry*math.sin(i*math.tau/24),0))
                 for i in range(24)] for t,rx,ry in ((.16,.071,.076),(.35,.072,.072),(.65,.063,.065),(.86,.057,.057),(1,.055,.053))],
               "trousers",[{"Hips":.7,thigh:.3},{"Hips":.2,thigh:.8},{thigh:1},{thigh:.9,shin:.1},{thigh:.5,shin:.5},
                            {shin:1},{shin:1},{shin:1},{shin:1},{shin:.9,foot:.1}])
    # Soft cargo pocket on the outer thigh, matching the cloth rather than floating rigid boxes.
    pocket = []
    for z in (.72,.76,.82,.86):
        pocket.append([(sign*(.194+.008*math.sin(j*math.pi/6)), -.047+j*.016, z) for j in range(7)])
    detail.loft(pocket,"shirt",{thigh:1},False,False)
    # Reinforced knee fabric follows the curved front, with no external plastic kneecap.
    detail.loft([[(sign*.105+.060*math.sin(t),.015+.074*math.cos(t),z)
                  for t in (-.85,-.55,-.25,0,.25,.55,.85)] for z in (.47,.50,.54,.57)],
                "shirt",[{thigh:.25,shin:.75},{thigh:.4,shin:.6},{thigh:.6,shin:.4},{thigh:.8,shin:.2}],False,False)
    for z in (.225,.273):
        detail.tube((sign*.105,.008,z-.009),(sign*.105,.008,z+.009),[(0,.068,.071),(1,.068,.071)],"silver",{shin:1},24)
    # Work boot: rounded rectangular sole, toe box, sloping vamp, padded ankle and tongue.
    def boot_ring(z, rx, ry, cy):
        points = []
        for j in range(24):
            t = j*math.tau/24
            x = math.copysign(abs(math.cos(t))**.65,math.cos(t))*rx
            y = math.copysign(abs(math.sin(t))**.70,math.sin(t))*ry
            points.append((sign*.105+x,cy+y,z))
        return points
    detail.loft([boot_ring(z,.055,.141,.078) for z in (.012,.024,.039)],"glove_pad",{foot:1})
    detail.loft([boot_ring(z,rx,ry,cy) for z,rx,ry,cy in
                 ((.038,.054,.137,.078),(.065,.053,.132,.078),(.096,.050,.106,.054),
                  (.133,.047,.073,.018),(.178,.048,.053,-.002))],"boot",{foot:1})
    for y,z in ((.025,.154),(.048,.140),(.071,.125),(.094,.112)):
        detail.tube((sign*.105-.029,y,z),(sign*.105+.029,y+.011,z-.003),[(0,.0018,.0018),(1,.0018,.0018)],"seam",{foot:1},6)

base = [fuse(shirt,"Shirt","shirt"),fuse(pants,"Trousers","trousers")]

# A sleeveless vest with real shoulder panels, armholes, V neck and a curved, lightly folded hem.
def vest_top(t):
    angle = t % math.tau
    if angle <= math.pi:
        return float(np.interp(angle,[0,.42,.78,1.02,math.pi/2,math.pi-1.02,math.pi-.78,math.pi-.42,math.pi],
                               [1.285,1.36,1.467,1.455,1.287,1.455,1.467,1.36,1.285]))
    return float(np.interp(angle,[math.pi,math.pi+.42,math.pi+.85,math.pi*1.5,math.tau-.85,math.tau-.42,math.tau],
                           [1.285,1.375,1.468,1.427,1.468,1.375,1.285]))


def vest_point(t,z,extra=0):
    rx = float(np.interp(z,[1.04,1.10,1.20,1.29,1.37,1.45],[.170,.176,.190,.201,.216,.182]))
    ry = float(np.interp(z,[1.04,1.10,1.20,1.29,1.37,1.45],[.125,.130,.137,.129,.115,.080]))
    fold = .002*math.sin(t*12+z*18) + .0015*math.sin(t*19-z*30)
    armhole = .08*math.exp(-((z-1.405)/.075)**2)*abs(math.cos(t))**3
    return ((rx+fold+extra)*math.cos(t),
            (ry+fold+extra)*math.sin(t)+armhole*max(-1,min(1,math.sin(t)*5)), z)


angles = [math.pi/2+.025+j*(math.tau-.05)/64 for j in range(65)]
levels = (0,.08,.18,.32,.48,.64,.80,.90,1)
vest = Mesh()
vest.loft([[vest_point(t,1.046+(vest_top(t)-1.046)*v) for t in angles] for v in levels],
          "vest",{"Chest":1},False,False)
# Use the same height weights for cloth and tape; it remains attached when bending to lift luggage.
for i,p in enumerate(vest.vertices): vest.weights[i] = torso_weights(p[2])
for z in (1.105,1.205):
    start = len(vest.vertices)
    vest.loft([[vest_point(t,height,.002) for t in angles] for height in (z-.022,z+.022)],"silver",{"Spine":1},False,False)
    for i in range(start,len(vest.vertices)): vest.weights[i] = torso_weights(vest.vertices[i][2])
# Shoulder tape follows each front panel over the top and down the back.
for sign in (-1,1):
    for front in (True,False):
        theta = math.acos(sign*.62)
        if not front: theta = -theta
        columns = (theta-.105,theta,theta+.105)
        tape = [[vest_point(t,z,.003) for t in columns] for z in (1.245,1.29,1.34,1.39,1.435)]
        tape.append([vest_point(t,vest_top(t),.003) for t in columns])
        vest.loft(tape,"silver",{"Chest":1},False,False)
    # Join front and back above the shirt, completing each shoulder instead of leaving two tabs.
    for material, lo, hi, offset in (("vest",.70,1.10,0),("silver",.80,1.00,.002)):
        bridge = []
        for v in np.linspace(0,1,9):
            row = []
            for theta in np.linspace(hi,lo,7):
                back = Vector(vest_point(-theta,vest_top(-theta)))
                front = Vector(vest_point(theta,vest_top(theta)))
                point = back.lerp(front,float(v))
                point.x *= sign
                point.z += .012*math.sin(float(v)*math.pi)+offset
                row.append(point)
            bridge.append(row if sign > 0 else row[::-1])
        vest.loft(bridge,material,{"Chest":1},False,False)
    # Flush lower patch pockets with a small top hem.
    theta = math.acos(sign*.58)
    vest.loft([[vest_point(t,z,.0035) for t in (theta-.18,theta,theta+.18)]
               for z in (1.062,1.08,1.13,1.15)],"vest",{"Spine":1},False,False)
    vest.loft([[vest_point(t,z,.0045) for t in (theta-.18,theta,theta+.18)]
               for z in (1.147,1.151)],"seam",{"Spine":1},False,False)
# Narrow zip, piping at the neckline, radio clip and unobtrusive ID badge.
for z0,z1 in ((1.048,1.12),(1.12,1.20),(1.20,1.285)):
    detail.ribbon([(0,vest_point(math.pi/2,z,.004)[1],z) for z in (z0,z1)],.006,"belt","Spine" if z1<1.23 else "Chest")
for direction in (-1,1):
    path = [vest_point(math.pi/2+direction*j*.075,vest_top(math.pi/2+direction*j*.075),.001) for j in range(10)]
    detail.ribbon(path,.004,"seam","Chest")
detail.ribbon([(.083,.128,z) for z in (1.29,1.337)],.030,"badge","Chest")
detail.ribbon([(.083,.130,z) for z in (1.312,1.333)],.024,"headset","Chest")

# Sculpted continuous head: jaw, cheeks, brow and nose are part of the same surface.
head = []
profile = [(1.536,.036,.035,.021),(1.55,.054,.050,.014),(1.575,.071,.061,.006),
           (1.61,.081,.075,-.002),(1.645,.085,.080,-.006),(1.68,.083,.080,-.008),
           (1.715,.078,.073,-.013),(1.741,.058,.052,-.015),(1.753,.008,.008,-.015)]
for z in np.linspace(1.536,1.753,30):
    rx,ry,cy = (float(np.interp(z,[p[0] for p in profile],[p[k] for p in profile])) for k in (1,2,3))
    points = []
    for i in range(48):
        t = i*math.tau/48; x = rx*math.cos(t); y = cy+ry*math.sin(t)
        front = max(0,math.sin(t))**6
        nose = .036*math.exp(-((x/.018)**2)-((z-1.623)/.028)**2)
        brow = .0035*math.exp(-((z-1.666)/.010)**2)
        socket = .003*math.exp(-((abs(x)-.032)/.018)**2-((z-1.65)/.010)**2)
        cheeks = .005*math.exp(-((abs(x)-.045)/.022)**2-((z-1.614)/.022)**2)
        muzzle = .006*math.exp(-(x/.030)**2-((z-1.585)/.017)**2)
        y += front*(nose+brow-socket+cheeks+muzzle)
        points.append((x,y,z))
    head.append(points)
detail.loft(head,"skin",{"Head":1})
for sign in (-1,1):
    detail.ellipse((sign*.086,-.003,1.622),(.012,.020,.028),"skin","Head",12,8)
    # Small inset eyes and eyelids; avoid spheres projecting out of the face.
    detail.ellipse((sign*.031,.069,1.650),(.014,.003,.0045),"shade","Head",12,6)
    detail.ellipse((sign*.031,.072,1.650),(.009,.0015,.0025),"light","Head",12,4)
    detail.ellipse((sign*.031,.074,1.650),(.003,.001,.0028),"eye","Head",10,4)
    detail.tube((sign*.016,.070,1.667),(sign*.048,.062,1.666),[(0,.0015,.002),(1,.001,.0015)],"hair",{"Head":1},6)
    detail.ellipse((sign*.011,.092,1.605),(.004,.0015,.0017),"shade","Head",8,4)
detail.tube((-.020,.068,1.578),(.020,.068,1.578),[(0,.0008,.001),(.5,.0015,.0015),(1,.0008,.001)],"shade",{"Head":1},8)

# Fabric cap, a curved bill, compact padded ear defenders, and a thin microphone boom.
detail.ellipse((0,-.013,1.723),(.091,.086,.053),"headset","Head",24,10)
bill = []
for radius in (0,.3,.7,1):
    bill.append([(x,.038+radius*(.100*math.sqrt(max(0,1-(x/.091)**2))),1.724-.014*(x/.091)**2-.006*radius)
                 for x in np.linspace(-.090,.090,19)])
detail.loft(bill,"headset",{"Head":1},False,False)
detail.loft([[(x,y,z-.003) for x,y,z in r] for r in reversed(bill)],"headset",{"Head":1},False,False)
for sign in (-1,1):
    detail.ellipse((sign*.100,-.012,1.627),(.016,.030,.043),"belt","Head",16,10)
    detail.ellipse((sign*.113,-.012,1.627),(.010,.027,.037),"headset","Head",16,10)
for k in range(18):
    t0,t1 = k*math.pi/18,(k+1)*math.pi/18
    a = (-.112*math.cos(t0),-.016,1.630+.146*math.sin(t0))
    b = (-.112*math.cos(t1),-.016,1.630+.146*math.sin(t1))
    detail.tube(a,b,[(0,.004,.005),(1,.004,.005)],"belt",{"Head":1},6)
detail.tube((-.112,.01,1.607),(-.050,.098,1.582),[(0,.0025,.0025),(1,.0025,.0025)],"belt",{"Head":1},8)
detail.ellipse((-.048,.099,1.582),(.009,.006,.005),"belt","Head",10,6)

base += [vest.object("Vest"),detail.object("Details")]
bpy.ops.object.select_all(action="DESELECT")
for ob in base: ob.select_set(True)
bpy.context.view_layer.objects.active = base[0]
bpy.ops.object.join(); body = bpy.context.object; body.name = "Body_LOD0"
# Triangulate once before reducing so every LOD and export uses the same surface.
tri = body.modifiers.new("Triangles","TRIANGULATE"); bpy.ops.object.modifier_apply(modifier=tri.name)
mat = bpy.data.materials.new("RampCrew"); mat.diffuse_color = (.45,.5,.2,1)
body.data.materials.clear(); body.data.materials.append(mat)

armature = bpy.data.armatures.new("RampCrewRig")
rig = bpy.data.objects.new("Skeleton",armature); bpy.context.scene.collection.objects.link(rig)
bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = rig; rig.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
for name, position in JOINTS.items():
    bone = armature.edit_bones.new(name); bone.head = Vector(position)
    children = [n for n,p in PARENTS.items() if p == name]
    bone.tail = Vector(JOINTS[children[0]]) if children else bone.head+Vector((0,0,.13))
    if (bone.tail-bone.head).length < .01: bone.tail.z += .10
    if PARENTS[name]: bone.parent = armature.edit_bones[PARENTS[name]]
bpy.ops.object.mode_set(mode="OBJECT")
objects = [rig]
triangles = sum(len(p.vertices)-2 for p in body.data.polygons)
for level, budget in enumerate((triangles,5000,900)):
    ob = body if level == 0 else body.copy()
    if level:
        ob.data = body.data.copy(); bpy.context.scene.collection.objects.link(ob)
        ob.modifiers.clear(); ob.name = f"Body_LOD{level}"; bpy.context.view_layer.objects.active = ob
        if level == 2:
            # At airport zoom, individual fingers, eye inserts, stitches and headset struts are subpixel.
            # Remove those islands before reduction; preserve the garments, head and PPE silhouette.
            bm = bmesh.new(); bm.from_mesh(ob.data)
            remaining = set(bm.verts); discard = []
            while remaining:
                first = remaining.pop(); island = [first]; stack = [first]
                while stack:
                    vertex = stack.pop()
                    for edge in vertex.link_edges:
                        neighbour = edge.other_vert(vertex)
                        if neighbour in remaining:
                            remaining.remove(neighbour); island.append(neighbour); stack.append(neighbour)
                size = max(max(v.co[axis] for v in island)-min(v.co[axis] for v in island) for axis in range(3))
                if size < .075: discard.extend(island)
            bmesh.ops.delete(bm,geom=discard,context="VERTS"); bm.to_mesh(ob.data); bm.free()
        count = sum(len(p.vertices)-2 for p in ob.data.polygons)
        decimate = ob.modifiers.new("LOD", "DECIMATE"); decimate.ratio = budget/count; decimate.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=decimate.name)
    skin = ob.modifiers.new("Skin","ARMATURE"); skin.object = rig
    ob.parent = rig; objects.append(ob)
    print(ob.name,"vertices",len(ob.data.vertices),"triangles",sum(len(p.vertices)-2 for p in ob.data.polygons))

bpy.ops.object.select_all(action="DESELECT")
for ob in objects: ob.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,"Models","RampCrew.fbx"),use_selection=True,
    object_types={"ARMATURE","MESH"},apply_unit_scale=True,apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",axis_up="Y",bake_space_transform=False,mesh_smooth_type="OFF",
    use_mesh_modifiers=False,add_leaf_bones=False,bake_anim=False,path_mode="STRIP")
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE,"Build","RampCrew.blend"))

profile = {"height":1.78,"hips":.94,"minCrouch":.78,"walkSpeed":1.2,"returnSpeed":1.4,
           "pickSeconds":.65,"liftSeconds":.45,"turnSeconds":.70,"placeSeconds":.65,
           "turnBackSeconds":.5,"settleSeconds":.3,"holdPickSeconds":.55,"holdLiftSeconds":.4,
           "holdStowSeconds":.75,"holdResetSeconds":.55,"boardingSpeed":.9}
with open(os.path.join(OUT,"RampCrew.crew"),"w") as stream: json.dump(profile,stream,indent=2)

# A separate shared wand is attached only to marshallers. Palette UVs keep the grip dark and the tube luminous.
wm = Mesh()
wm.tube((0,0,0),(0,0,.12),[(0,.018,.018),(1,.018,.018)],"belt",{"Hand_L":1},12)
wm.tube((0,0,.12),(0,0,.42),[(0,.022,.022),(.94,.022,.022),(1,.012,.012)],"vest",{"Hand_L":1},12)
mesh = bpy.data.meshes.new("MarshallingWand"); mesh.from_pydata(wm.vertices,[],wm.faces); mesh.update()
uv = mesh.uv_layers.new(name="UVMap")
for polygon, color in zip(mesh.polygons,wm.colors):
    polygon.use_smooth = True
    for li in polygon.loop_indices: uv.data[li].uv = ((color+.5)/16,.5)
wand = bpy.data.objects.new("MarshallingWand",mesh); bpy.context.scene.collection.objects.link(wand)
bpy.ops.object.select_all(action="DESELECT"); wand.select_set(True); bpy.context.view_layer.objects.active = wand
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,"Models","MarshallingWand.fbx"),use_selection=True,
    object_types={"MESH"},apply_scale_options="FBX_SCALE_ALL",axis_forward="-Z",axis_up="Y",
    bake_space_transform=True,use_mesh_modifiers=False,bake_anim=False)
palette = COLORS.copy(); palette[2] = "ff621c"
def wand_palette(colors):
    pixels = np.empty((16,256,4),dtype=np.uint8)
    for i, color in enumerate(colors): pixels[:,i*16:(i+1)*16] = (*bytes.fromhex(color),255)
    return pixels
png(os.path.join(OUT,"Textures","Wand_Albedo.png"),wand_palette(palette))
emission = ["000000"]*16; emission[2] = "ff621c"
png(os.path.join(OUT,"Textures","Wand_Emission.png"),wand_palette(emission))
