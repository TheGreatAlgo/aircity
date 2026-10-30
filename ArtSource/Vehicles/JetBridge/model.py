"""Passenger boarding bridge ("jet bridge"), apron-drive type with three telescoping tunnels, after the
ThyssenKrupp / JBT / ADELTE bridges at Schiphol, Frankfurt and Changi: a round rotunda on one steel column, three
rectangular steel tunnels that slide inside each other (A in B in C), a wheel bogie on two lifting legs under the
outer tunnel, and the operator cab with its black canopy at the aircraft end. A fixed link corridor on columns
leads from the terminal to the rotunda. A gate with two or three bridges has a head instead: one main link (the
Link, widened) from the terminal to a fixed building on columns (Head, one storey; HeadUpper, a second storey
for an upper-deck bridge), and each bridge's rotunda stands against it on a short stub link. An upper-deck
bridge's rotunda (RotundaHigh) is level with the head's upper floor, so every link is level.

The game moves every part itself each frame (the bridge swings round the rotunda, telescopes and pitches, the cab
turns to the door, the legs stretch down to the bogie, the link and the head stretch to their lengths, the
columns' shafts stretch up to them). So every part is its
own object, a direct child of the root, with its origin on the pivot the game places it by, built facing forward
with no rotation. Their rest positions don't matter: for the AO bake they are laid out apart along +right (LAYOUT)
so none shadows another, and preview_pose() assembles them, docked, for the previews only. There are no LODs.

The numbers the game's rules share are in BRIDGE (exported into the .vehicle as "bridge").

Space: (right, forward, up) metres.
"""
import math

import bmesh
from mathutils import Matrix

from kit import M, Part, V
from tug import hazard

NAME = "JetBridge"
DISPLAY_NAME = "Jet bridge"
KIND = "JetBridge"
AO_DISTANCE = 0.8
AO_STRENGTH = 0.6
BAKE_SIZE = 4096           # saved at 2048: three 13 m tunnels are a lot of surface
LOD_RATIOS = None          # no LODs: the game assembles the bridge part by part
PREVIEW_GROUND = (0.42, 0.42, 0.43)

MATS = {
    "Cladding": ("#C8CCD0", 0.25, 0.45, False),
    "Roof": ("#DADDE0", 0.15, 0.55, False),
    "Trim": ("#7E858C", 0.35, 0.45, False),
    "CabWhite": ("#E6E8E8", 0.0, 0.4, False),
    "BridgeGlass": ("#27313B", 0.0, 0.12, False),
    "Bellows": ("#151617", 0.0, 0.85, False),
    "Rubber": ("#1B1B1C", 0.0, 0.9, False),
    "HazardYellow": ("#F2C200", 0.0, 0.45, False),
    "HazardBlack": ("#141414", 0.0, 0.6, False),
    "SteelDark": ("#4E545A", 0.6, 0.5, False),
    "Legs": ("#80868C", 0.5, 0.5, False),
    "Under": ("#8D9297", 0.3, 0.6, False),
    "Interior": ("#2A2C2F", 0.0, 0.8, False),
    "Concrete": ("#9C9A94", 0.0, 0.85, False),
    "CabFloor": ("#3A3C3E", 0.0, 0.8, False),
}
GRIME = "#5A5650"

# ------------------------------------------------------------------ the contract (the game uses the same numbers)
ROTUNDA_FLOOR = 4.0        # rotunda floor above the ground
ROTUNDA_R = 2.3            # drum's outer radius
TUNNEL_START = 2.3         # Tunnel_A's pivot from the rotunda centre
SECTION_L = 13.0
# (name, exterior width, exterior bottom, exterior top), relative to the section's floor line
SECTIONS = [("Tunnel_A", 2.50, -0.25, 2.55), ("Tunnel_B", 2.80, -0.40, 2.70), ("Tunnel_C", 3.10, -0.55, 2.85)]
CAB_REACH, CAB_W = 2.6, 3.4
CAB_BOT, CAB_TOP = -0.75, 3.18  # the cab's underside and roof rim: below and above Tunnel_C pitched 1 in 10
DRIVE_FROM_END = 4.0       # the drive yoke under Tunnel_C, this far before its outer end
LINK_W, LINK_BOT, LINK_TOP = 2.6, -0.35, 2.75
LEG_BASE = 1.0             # top of the Drive frame, where DriveLegs start
YOKE_BOTTOM = -0.95        # underside of the yoke's crossbeam (Tunnel_C floor line = 0), where DriveLegs end
UPPER_FLOOR = 7.8          # an upper-deck bridge's rotunda (RotundaHigh) and a head's upper floor, for double-deck
                           # aircraft: its tunnel clears a main-deck link by 0.5 m
COLUMN = 0.24              # half a link column's square shaft
COLUMN_BASE = 0.15         # the top of a column's foot, where its shaft starts
COLUMN_SHAFT_TOP = 0.45    # a column's shaft ends this far under the link's underside, inside the head's collar
# the head: HEAD_W wide, from its underside (HEAD_BOT) to its parapet (HEAD_TOP) round a flat roof (HEAD_ROOF), all
# from a storey's floor; the upper storey (floor UPPER_FLOOR) sits on the lower one's roof, HEAD_UPPER_BOT under its
# floor and a little wider, so the lower parapet is inside it
HEAD_W, HEAD_BOT, HEAD_TOP, HEAD_ROOF = 7.0, -0.6, 3.35, 3.1
HEAD_UPPER_BOT = HEAD_ROOF + ROTUNDA_FLOOR - UPPER_FLOOR - 0.05
HEAD_UPPER_WIDER = 0.06
HEAD_WIN = (0.5, 2.55)     # the head's glass band above its floor
MAIN_LINK_W = 3.6          # a head's main link: the Link widened (the game scales it across)
HEAD_STUB = 1.2            # a head's bridges: the gap from its wall to the rotunda's drum, a short stub link
HEAD_COLUMN_X = 2.2        # the head stands on pairs of link columns this far either side of its centreline

BRIDGE = {
    "rotundaFloor": ROTUNDA_FLOOR,
    "rotundaRadius": ROTUNDA_R,
    "tunnelStart": TUNNEL_START,
    "sectionLength": SECTION_L,
    "sections": [[w, b, t] for _, w, b, t in SECTIONS],
    "cabReach": CAB_REACH,
    "cabWidth": CAB_W,
    "cabBottom": CAB_BOT,
    "cabTop": CAB_TOP,
    "driveFromEnd": DRIVE_FROM_END,
    "linkWidth": LINK_W,
    "linkBottom": LINK_BOT,
    "linkTop": LINK_TOP,
    "legBase": LEG_BASE,
    "yokeBottom": YOKE_BOTTOM,
    "upperFloor": UPPER_FLOOR,
    "headWidth": HEAD_W,
    "headBottom": HEAD_BOT,
    "headTop": HEAD_TOP,
    "headUpperBottom": HEAD_UPPER_BOT,
    "headUpperWider": HEAD_UPPER_WIDER,
}

WALL = 0.12                # tunnel wall thickness (B's inside holds A, C's holds B, 3 cm clear all round)
RECESS = 0.02              # glass set back from the wall face
WIN = (0.98, 1.92)         # window band above the floor line (tunnels, link and rotunda line up)

MOVERS = ["Tunnel_A", "Tunnel_B", "Tunnel_C", "DriveLegs", "Drive", "Cab", "Link", "LinkColumnFoot", "LinkColumn",
          "LinkColumnHead", "RotundaHigh", "Head", "HeadUpper", "HeadEnd", "HeadUpperEnd"]
WHEELS = []
SPINNERS = []
LIGHTS = []
WORK = {}
# parts the game stretches: their UVs collapse along that axis after the bake (vpipe.collapse_uvs)
STRETCH = {"Link": "f", "DriveLegs": "z", "LinkColumn": "z", "Head": "f", "HeadUpper": "f"}
# where each part sits for the bake (right offset), apart so no part shades another
LAYOUT = {"Rotunda": 0.0, "Tunnel_A": 8.0, "Tunnel_B": 13.0, "Tunnel_C": 19.0, "Cab": 26.0, "Drive": 33.0,
          "DriveLegs": 38.0, "Link": 42.0, "LinkColumnFoot": 46.0, "LinkColumn": 49.0, "LinkColumnHead": 52.0,
          "RotundaHigh": 58.0, "Head": 66.0, "HeadUpper": 76.0, "HeadEnd": 86.0, "HeadUpperEnd": 96.0}
# the parts that stand on the apron (road dust low down) and their half-width round their LAYOUT x
GROUNDED = {"Rotunda": 2.5, "RotundaHigh": 2.5, "Drive": 1.6, "LinkColumnFoot": 0.8}
# the profile's bounds: the (low) rotunda, the one part that stays put
BOUNDS = ((-ROTUNDA_R - 0.15, -ROTUNDA_R - 0.15, 0.0), (ROTUNDA_R + 0.15, ROTUNDA_R + 0.15, 7.26))


# ============================================================== helpers

def face(b, pts, normal, mat):
    """A flat polygon facing `normal`."""
    n = [0.0, 0.0, 0.0]
    for i in range(len(pts)):
        p, q = pts[i], pts[(i + 1) % len(pts)]
        n[0] += (p[1] - q[1]) * (p[2] + q[2])
        n[1] += (p[2] - q[2]) * (p[0] + q[0])
        n[2] += (p[0] - q[0]) * (p[1] + q[1])
    if sum(a * c for a, c in zip(n, normal)) < 0:
        pts = pts[::-1]
    return b.quad(pts, mat)


def lathe(b, prof, segs=32, bottom="Under"):
    """A closed solid of revolution about the up axis: prof = [(radius, up, material of the band from here to the
    next point)], bottom to top; capped below (`bottom`) and on top (the last point's material)."""
    bm = b.bm
    angs = [2 * math.pi * k / segs for k in range(segs)]
    rings = [[bm.verts.new(V(r * math.cos(a), r * math.sin(a), z)) for a in angs] for r, z, _ in prof]
    faces = []
    for i in range(len(rings) - 1):
        mi = b.mat(prof[i][2])
        for k in range(segs):
            k2 = (k + 1) % segs
            f = bm.faces.new((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]))
            f.material_index = mi
            faces.append(f)
    for ring, mat in ((rings[0][::-1], bottom), (rings[-1], prof[-1][2])):
        f = bm.faces.new(ring)
        f.material_index = b.mat(mat)
        faces.append(f)
    bmesh.ops.recalc_face_normals(bm, faces=faces)


def groove(x, z, mat="Cladding", depth=0.012, h=0.02):
    """A shallow horizontal groove at height z in a wall face at x (right side, top to bottom)."""
    return [((x, z + h), mat), ((x - depth, z + h), mat), ((x - depth, z - h), mat), ((x, z - h), mat)]


def section_loop(side, bottom="Under", roof="Roof"):
    """A closed cross-section (right, up) from its right half: side = [((x, z), material of the segment to the next
    point)] from the roof edge down to the bottom corner (x >= 0; the last point's material is unused). Returns the
    loop (right side down, bottom, left side up, roof: extruded forward, its faces point out) and a material per
    segment."""
    pts = [p for p, _ in side]
    mats = [m for _, m in side[:-1]]
    loop = pts + [(-x, z) for x, z in reversed(pts)]
    return loop, mats + [bottom] + mats[::-1] + [roof]


def extrude(b, loop, mats, y0, y1, inward=False):
    """The side faces of a cross-section loop (from section_loop) swept forward from y0 to y1, no ends."""
    bm = b.bm
    a = [bm.verts.new(V(x, y0, z)) for x, z in loop]
    c = [bm.verts.new(V(x, y1, z)) for x, z in loop]
    faces = []
    for k in range(len(loop)):
        k2 = (k + 1) % len(loop)
        q = (a[k], a[k2], c[k2], c[k])
        f = bm.faces.new(q[::-1] if inward else q)
        f.material_index = b.mat(mats[k])
        faces.append(f)
    return a, c, faces


def rect(h, bot, top):
    return [(h, top), (h, bot), (-h, bot), (-h, top)]


def collar(b, h, bot, top, ya, yb, band, lip, lip_at):
    """A flush frame round a tunnel end from ya to yb: outer skin `band`, a dark inner skin WALL in, the open end
    face (at lip_at) in `lip`, the other end face in `band`."""
    bm = b.bm
    oa, ob, fo = extrude(b, rect(h, bot, top), [band] * 4, ya, yb)
    ia, ib, fi = extrude(b, rect(h - WALL, bot + WALL, top - WALL), ["Interior"] * 4, ya, yb, inward=True)
    faces = fo + fi
    for ro, ri, y in ((oa, ia, ya), (ob, ib, yb)):
        mi = b.mat(lip if abs(y - lip_at) < 1e-6 else band)
        for k in range(4):
            k2 = (k + 1) % 4
            f = bm.faces.new((ro[k], ro[k2], ri[k2], ri[k]))
            f.material_index = mi
            faces.append(f)
    bmesh.ops.recalc_face_normals(bm, faces=faces)


def frange(a, b, step):
    out = []
    while a <= b + 1e-9:
        out.append(a)
        a += step
    return out


# ============================================================== parts

def rotunda(name="Rotunda", F=ROTUNDA_FLOOR):
    """A rotunda: a round drum on one steel column, floor at F, a ribbon window, a flat roof with a slight overhang
    and a parapet. Rotationally symmetric: the tunnel and the link butt into it at any angle. The drum is the same
    at any floor height (everything above the column is relative to F); a high rotunda (RotundaHigh, upper-deck
    bridges) gets a thicker column with a base sleeve and splice flanges so it doesn't look spindly."""
    b = Part(name)
    R = ROTUNDA_R

    def grv(z):
        return [(R, z + 0.02, "Cladding"), (R - 0.012, z + 0.02, "Cladding"), (R - 0.012, z - 0.02, "Cladding"),
                (R, z - 0.02, "Cladding")]

    prof = [(2.05, F - 0.40, "Under"), (R, F - 0.22, "Trim"), (R, F + 0.12, "Cladding")]
    prof += grv(F + 0.48)
    prof += [(R, F + 0.86, "Trim"), (R + 0.03, F + 0.86, "Trim"), (R + 0.03, F + WIN[0] - 0.03, "Trim"),
             (R - RECESS, F + WIN[0] - 0.03, "BridgeGlass"), (R - RECESS, F + WIN[1], "Cladding"),
             (R, F + WIN[1], "Cladding")]
    prof += grv(F + 2.40)
    prof += [(R, F + 2.90, "Trim"), (R + 0.15, F + 2.90, "Trim"), (R + 0.15, F + 3.15, "Trim"),
             (R + 0.07, F + 3.15, "Roof"), (R + 0.07, F + 3.03, "Roof")]
    lathe(b, prof, 32)
    # mullions in the window band
    for k in range(16):
        b.box((-0.035, R - 0.035, F + WIN[0] - 0.028), (0.035, R - 0.004, F + WIN[1] - 0.002), "Trim",
              matrix=M(yaw=k * 22.5 + 11.25))
    # roof vent
    b.cyl((0, 0, F + 3.02), (0, 0, F + 3.2), 0.32, "Under", segs=16)
    b.cyl((0, 0, F + 3.2), (0, 0, F + 3.26), 0.4, "Trim", segs=16)
    # column: footing, base plate, round column, flared head, slewing ring under the drum
    high = F > ROTUNDA_FLOOR
    cr = 0.52 if high else 0.45
    foot, plate = (1.1, 0.85) if high else (0.95, 0.72)
    b.box((-foot, -foot, 0.0), (foot, foot, 0.10), "Concrete")
    b.box((-plate, -plate, 0.10), (plate, plate, 0.16), "SteelDark")
    for sx in (-1, 1):
        for sy in (-1, 1):
            q = plate - 0.16
            b.cyl((sx * q, sy * q, 0.16), (sx * q, sy * q, 0.22), 0.04, "Steel", segs=6)
    head = F - 0.95
    b.cyl((0, 0, 0.16), (0, 0, head), cr, "SteelDark", segs=20)
    if high:
        b.cyl((0, 0, 0.16), (0, 0, 1.3), cr + 0.08, "SteelDark", segs=20)          # base sleeve
        b.cyl((0, 0, 1.3), (0, 0, 1.42), cr + 0.08, "SteelDark", segs=20, r2=cr)
        for z in (2.9, 4.6):                                                        # splice flanges
            b.cyl((0, 0, z), (0, 0, z + 0.07), cr + 0.05, "SteelDark", segs=20)
    b.cyl((0, 0, head), (0, 0, F - 0.58), cr, "SteelDark", segs=20, r2=0.85)
    b.cyl((0, 0, F - 0.58), (0, 0, F - 0.40), 1.05, "SteelDark", segs=24)
    return b


def tunnel(name, W, bot, top):
    """One telescoping section, SECTION_L long from its pivot (the inner end's floor line) forward. Exterior W x
    bot..top, walls WALL thick near the open ends (the next smaller section slides inside), grey cladding with
    horizontal grooves, a glass band with posts, a lighter roof with seams, darker frames at the ends. Tunnel_A's
    skin runs on 1 m behind its pivot into the rotunda, so the drum's curve and the tunnel's pitch leave no gap.
    Tunnel_C carries the drive yoke."""
    b = Part(name)
    h, L = W / 2, SECTION_L
    first = name == "Tunnel_A"
    lip_in = 0.0 if first else 0.16
    lip_out = 0.12
    y0, y1 = (-1.0 if first else lip_in), L - lip_out
    side = [((h - 0.07, top - 0.02), "Cladding"), ((h, top - 0.09), "Cladding")]
    for z in (2.45, 2.2):
        if z + 0.06 < top - 0.09:
            side += groove(h, z)
    side += [((h, WIN[1]), "Cladding"), ((h - RECESS, WIN[1]), "BridgeGlass"), ((h - RECESS, WIN[0]), "Cladding"),
             ((h, WIN[0]), "Cladding")]
    for z in (0.7, 0.4, 0.1, -0.2):
        if z - 0.06 > bot + 0.14:
            side += groove(h, z)
    side += [((h, bot + 0.14), "Trim"), ((h, bot), "Under")]
    loop, mats = section_loop(side)
    extrude(b, loop, mats, y0, y1)
    # end frames (B and C wrap the smaller section at their inner end: a rubber seal on the lip) and dark inner
    # skins a metre into each open end
    inner = rect(h - WALL, bot + WALL, top - WALL)
    if not first:
        collar(b, h, bot, top, 0.0, lip_in, "Trim", "Rubber", 0.0)
        extrude(b, inner, ["Interior"] * 4, lip_in, 1.0, inward=True)
    collar(b, h, bot, top, L - lip_out, L, "Trim", "Trim", L)
    extrude(b, inner, ["Interior"] * 4, L - 1.0, L - lip_out, inward=True)
    # window posts, flush with the wall
    start = 0.6 if first else lip_in + 0.55
    for y in frange(start, y1 - 0.4, 1.3):
        for s in (-1, 1):
            x0, x1 = sorted((s * (h - RECESS - 0.005), s * h))
            b.box((x0, y - 0.04, WIN[0] + 0.002), (x1, y + 0.04, WIN[1] - 0.002), "Cladding")
    # roof seams
    for y in frange(start + 0.65, y1 - 0.3, 1.3):
        b.box((-h + 0.09, y - 0.025, top - 0.021), (h - 0.09, y + 0.025, top), "Cladding")
    if name == "Tunnel_C":
        # drive yoke: a crossbeam under the floor, sleeves at the ends that the legs end in
        yc = L - DRIVE_FROM_END
        b.box((-1.3, yc - 0.25, YOKE_BOTTOM), (1.3, yc + 0.25, bot), "SteelDark")
        for s in (-1, 1):
            x0, x1 = sorted((s * 0.99, s * 1.41))
            b.box((x0, yc - 0.21, YOKE_BOTTOM - 0.12), (x1, yc + 0.21, bot), "SteelDark")
        for s in (-1, 1):
            b.box((-1.0, yc + s * 0.25 - 0.03, bot - 0.2), (1.0, yc + s * 0.25 + 0.03, bot), "SteelDark")
    return b


def legs():
    """Two square lifting columns, 1 m tall: the game stretches them (local up) from the Drive to the yoke."""
    b = Part("DriveLegs")
    for s in (-1, 1):
        x0, x1 = sorted((s * 1.04, s * 1.36))
        b.box((x0, -0.16, 0.0), (x1, 0.16, 1.0), "Legs")
    return b


def tyre(b, centre, r, w, bore=0.24, segs=16):
    hw = w / 2
    b.revolve(centre, [(-hw, bore), (-hw, r - 0.05), (-hw + 0.04, r), (hw - 0.04, r), (hw, r - 0.05), (hw, bore)],
              "Tyre", segs, "r")


def drive():
    """The wheel bogie: a frame 2.9 m wide whose top (where the legs stand) is at LEG_BASE, with a pair of solid
    dual tyres under each leg and hazard stripes."""
    b = Part("Drive")
    R, top = 0.43, LEG_BASE
    b.box((-1.45, -0.42, top - 0.12), (1.45, 0.42, top), "SteelDark", bevel=0.015, segments=1)
    b.box((-0.6, -0.36, 0.30), (0.6, 0.36, top - 0.12), "SteelDark")
    for s in (-1, 1):
        # stripes on the plate's edges and the middle gearbox, front and back
        hazard(b, (s * 1.42, s * 0.42, top - 0.11), (-s, 0, 0), (0, 0, 1), 2.84, 0.10, pitch=0.2)
        hazard(b, (s * 0.58, s * 0.36, 0.33), (-s, 0, 0), (0, 0, 1), 1.16, 0.50, pitch=0.2)
        x0, x1 = sorted((s * 1.36, s * 1.45))
        b.box((x0, -0.30, 0.20), (x1, 0.30, top - 0.12), "SteelDark")
        b.cyl((s * 1.45, 0, R), (s * 1.52, 0, R), 0.13, "Steel", segs=12)
        b.cyl((s * 0.62, 0, R), (s * 1.36, 0, R), 0.25, "Rim", segs=14)
        for xc in (0.84, 1.17):
            tyre(b, (s * xc, 0.0, R), R, 0.28)
    return b


def u_outline(hw, top, rad, n=4):
    """The canopy's arch from the right foot (on the floor) over to the left foot: (right, up) points."""
    pts = [(hw, 0.0)]
    for k in range(n + 1):
        a = math.pi / 2 * k / n
        pts.append((hw - rad + rad * math.cos(a), top - rad + rad * math.sin(a)))
    for k in range(n + 1):
        a = math.pi / 2 + math.pi / 2 * k / n
        pts.append((-hw + rad + rad * math.cos(a), top - rad + rad * math.sin(a)))
    pts.append((-hw, 0.0))
    return pts


def d_outline(h, yf, segs=12):
    """The cab's plan, (right, forward): a half circle of radius h round the pivot behind it, straight sides out to
    yf in front."""
    pts = [(h * math.cos(-math.pi * k / segs), h * math.sin(-math.pi * k / segs)) for k in range(segs + 1)]
    return pts + [(-h, yf), (h, yf)]


def arc_rim(b, r, z0, z1, mat, t=0.04, segs=12):
    """A thin upstand round the cab's rounded rear (radius r, behind the pivot), from z0 to z1."""
    for k in range(segs):
        a0, a1 = -math.pi * k / segs, -math.pi * (k + 1) / segs
        mid = (a0 + a1) / 2
        chord = 2 * (r + t) * math.sin(math.pi / segs / 2) + 0.01
        cx, cy = (r + t / 2) * math.cos(mid), (r + t / 2) * math.sin(mid)
        b.box((-chord / 2, -t / 2, z0), (chord / 2, t / 2, z1), mat,
              matrix=Matrix.Translation(V(cx, cy, 0.0)) @ Matrix.Rotation(mid + math.pi / 2, 4, "Z"))


def cab():
    """The operator cab, pivoted at its rear centre on the floor (where Tunnel_C ends). White, CAB_W wide, floor at 0,
    its bumper at CAB_REACH; its rear is a half circle round the pivot, so as the cab turns its round wall always
    meets the tunnel the same way (Tunnel_C runs into it, like Tunnel_A into the rotunda's drum). It stands proud of
    every tunnel section, CAB_BOT to CAB_TOP. The door opening is in a black accordion canopy, a rubber bumper with
    hazard stripes under it, the console window on the left, a roof rim."""
    b = Part("Cab")
    h, yf, roof = CAB_W / 2, 2.2, 3.05
    b.prism(d_outline(h, yf), "z", CAB_BOT, roof - CAB_BOT, "CabWhite")
    # grey skirt round the bottom
    b.prism(d_outline(h + 0.012, yf + 0.012), "z", CAB_BOT + 0.03, 0.24, "Trim")
    # roof with a rim
    rz = (roof + 0.08, CAB_TOP)
    b.prism(d_outline(h + 0.04, yf + 0.14), "z", roof, 0.08, "Roof")
    b.box((-h - 0.04, 0.0, rz[0]), (-h + 0.0, yf + 0.14, rz[1]), "Trim")
    b.box((h - 0.0, 0.0, rz[0]), (h + 0.04, yf + 0.14, rz[1]), "Trim")
    b.box((-h, yf + 0.10, rz[0]), (h, yf + 0.14, rz[1]), "Trim")
    arc_rim(b, h, rz[0], rz[1], "Trim")
    # a seam band round the body at the tunnels' roof line
    b.prism(d_outline(h + 0.006, yf + 0.006), "z", 2.62, 0.05, "Trim")
    # floor nose under the canopy: rubber bumper at floor level (its face at CAB_REACH), hazard stripes below it
    nose = CAB_REACH - 0.1
    b.box((-1.55, yf - 0.03, -0.45), (1.55, nose, 0.0), "Under")
    b.box((-1.5, nose, -0.15), (1.5, CAB_REACH, 0.0), "Rubber", bevel=0.02, segments=1)
    hazard(b, (1.5, nose, -0.44), (-1, 0, 0), (0, 0, 1), 3.0, 0.27, pitch=0.22)
    # the canopy (closure): a black accordion round the door opening
    rings = []
    folds = [(yf - 0.02, 0), (2.26, 1), (2.32, -1), (2.38, 1), (2.44, -1), (2.50, 1), (nose + 0.05, 0)]
    for y, k in folds:
        outer = u_outline(1.33 + 0.05 * k, 2.55 + 0.05 * k, 0.42 + 0.05 * k)
        inner = u_outline(1.08 - 0.04 * k, 2.30 - 0.04 * k, 0.25 - 0.04 * k)
        rings.append([(x, y, z) for x, z in outer + inner[::-1]])
    faces = b.loft(rings, "Bellows", cap=True)
    bmesh.ops.triangulate(b.bm, faces=faces[-2:], ngon_method="EAR_CLIP")
    # the dark doorway, its floor and a yellow edge line
    door = u_outline(1.06, 2.28, 0.23)
    face(b, [(x, yf + 0.01, z) for x, z in door], (0, 1, 0), "Dark")
    face(b, [(-1.06, yf, 0.004), (1.06, yf, 0.004), (1.06, nose, 0.004), (-1.06, nose, 0.004)], (0, 0, 1), "CabFloor")
    face(b, [(-1.06, nose - 0.08, 0.007), (1.06, nose - 0.08, 0.007), (1.06, nose, 0.007), (-1.06, nose, 0.007)],
         (0, 0, 1), "HazardYellow")
    # windows: the operator's console at the front left (side, low floor-view pane, front corner), one on the right
    for s, y0, y1, z0, z1 in ((-1, 1.15, 2.05, 0.85, 2.35), (-1, 1.45, 2.05, 0.08, 0.62), (1, 0.85, 1.85, 1.05, 2.35)):
        x = s * h
        x0, x1 = sorted((x - s * 0.01, x + s * 0.02))
        b.box((x0, y0, z0), (x1, y1, z1), "Trim")
        x0, x1 = sorted((x, x + s * 0.035))
        b.box((x0, y0 + 0.05, z0 + 0.05), (x1, y1 - 0.05, z1 - 0.05), "BridgeGlass")
    b.box((-1.63, yf - 0.01, 0.85), (-1.42, yf + 0.02, 2.35), "Trim")
    b.box((-1.60, yf, 0.9), (-1.45, yf + 0.035, 2.3), "BridgeGlass")
    return b


def link():
    """The fixed corridor, 1 m long (the game stretches it forward): only bands that run its whole length."""
    b = Part("Link")
    h = LINK_W / 2
    w = h - 0.02
    side = [((h - 0.08, LINK_TOP - 0.05), "Trim"), ((h - 0.08, LINK_TOP), "Trim"), ((h, LINK_TOP), "Trim"),
            ((h, 2.45), "Trim"), ((w, 2.45), "Cladding"), ((w, WIN[1]), "Cladding"),
            ((w - RECESS, WIN[1]), "BridgeGlass"), ((w - RECESS, WIN[0]), "Cladding"), ((w, WIN[0]), "Cladding")]
    for z in (0.7, 0.4, 0.1):
        side += groove(w, z)
    side += [((w, LINK_BOT + 0.14), "Trim"), ((w, LINK_BOT), "Under")]
    loop, mats = section_loop(side)
    a, c, faces = extrude(b, loop, mats, 0.0, 1.0)
    bm = b.bm
    caps = [bm.faces.new(a[::-1]), bm.faces.new(c)]
    for f in caps:
        f.material_index = b.mat("Interior")
    bmesh.ops.recalc_face_normals(bm, faces=faces + caps)
    bmesh.ops.triangulate(bm, faces=caps, ngon_method="EAR_CLIP")
    return b


def link_column_foot():
    """A link column's foot (pivot on the ground): concrete footing, base plate, a sleeve round the shaft's foot."""
    b = Part("LinkColumnFoot")
    c = COLUMN
    b.box((-0.75, -0.75, 0.0), (0.75, 0.75, 0.10), "Concrete")
    b.box((-0.42, -0.42, 0.10), (0.42, 0.42, COLUMN_BASE), "SteelDark")
    b.box((-c - 0.04, -c - 0.04, COLUMN_BASE), (c + 0.04, c + 0.04, 0.9), "SteelDark", bevel=0.02, segments=1)
    return b


def link_column():
    """A link column's square shaft, 1 m tall: the game stretches it (local up) from the foot (COLUMN_BASE) to just
    under the link (COLUMN_SHAFT_TOP below its underside, inside the head's collar), however high the link is."""
    b = Part("LinkColumn")
    b.box((-COLUMN, -COLUMN, 0.0), (COLUMN, COLUMN, 1.0), "SteelDark")
    return b


def link_column_head():
    """A link column's head, pivoted at its top centre (the link's underside): the cross-head the link sits on, a
    collar the shaft's top runs into, and two braces down to the shaft. It stays level under a ramped link (at 1 in
    10 the link's underside drops 2.4 cm across it)."""
    b = Part("LinkColumnHead")
    c = COLUMN
    b.box((-LINK_W / 2, -0.24, -0.34), (LINK_W / 2, 0.24, 0.0), "SteelDark")
    b.box((-c - 0.05, -c - 0.05, -0.75), (c + 0.05, c + 0.05, -0.34), "SteelDark")
    for s in (-1, 1):
        b.cyl((s * (c - 0.02), 0.0, -1.15), (s * 1.0, 0.0, -0.36), 0.06, "SteelDark", segs=4)
    return b


def head_storey(name, h, bot):
    """One storey of the head, 1 m long: the game stretches it forward to the head's length, so it has only bands
    along its length: a wide glass band, grooves, a trim fascia and a parapet round the flat roof. Floor line at 0,
    h half its width, its underside at `bot`; its plain ends are covered by HeadEnd / HeadUpperEnd."""
    b = Part(name)
    side = [((h - 0.25, HEAD_ROOF), "Trim"), ((h - 0.25, HEAD_TOP), "Trim"), ((h, HEAD_TOP), "Trim"),
            ((h, HEAD_ROOF - 0.05), "Cladding")]
    side += groove(h, 2.82)
    side += [((h, HEAD_WIN[1]), "Cladding"), ((h - RECESS, HEAD_WIN[1]), "BridgeGlass"),
             ((h - RECESS, HEAD_WIN[0]), "Cladding"), ((h, HEAD_WIN[0]), "Cladding")]
    side += groove(h, 0.2)
    side += [((h, bot + 0.15), "Trim"), ((h, bot), "Under")]
    loop, mats = section_loop(side)
    a, c, faces = extrude(b, loop, mats, 0.0, 1.0)
    bm = b.bm
    caps = [bm.faces.new(a[::-1]), bm.faces.new(c)]
    for f in caps:
        f.material_index = b.mat("Cladding")
    bmesh.ops.recalc_face_normals(bm, faces=faces + caps)
    bmesh.ops.triangulate(bm, faces=caps, ngon_method="EAR_CLIP")
    return b


def head_end(name, h, bot, t):
    """A head storey's end wall, pivoted at the centre of its floor line, facing forward (the game turns it to face
    out of either end): a panel t thick over the storey's plain end, wrapping its corners, with a mullioned glass
    band and the trim fascia along the top. The upper storey's is thicker, so it covers the top of the lower one's."""
    b = Part(name)
    b.box((-h - t, 0.0, bot), (h + t, t, HEAD_TOP), "Cladding")
    b.box((-h - t - 0.01, 0.0, HEAD_ROOF - 0.05), (h + t + 0.01, t + 0.01, HEAD_TOP), "Trim")
    b.box((-h - t - 0.01, 0.0, bot), (h + t + 0.01, t + 0.01, bot + 0.15), "Trim")
    g = h - 0.35
    b.box((-g, t, HEAD_WIN[0]), (g, t + 0.012, HEAD_WIN[1]), "BridgeGlass")
    n = 5
    for k in range(n + 1):
        x = -g + 2 * g * k / n
        b.box((x - 0.04, t, HEAD_WIN[0] - 0.04), (x + 0.04, t + 0.03, HEAD_WIN[1] + 0.04), "Trim")
    for z in (HEAD_WIN[0] - 0.04, HEAD_WIN[1] + 0.04):
        b.box((-g - 0.04, t, z - 0.04), (g + 0.04, t + 0.03, z), "Trim")
    return b


def build():
    parts = [rotunda()] + [tunnel(n, w, bo, t) for n, w, bo, t in SECTIONS] + [legs(), drive(), cab(), link()]
    parts += [link_column_foot(), link_column(), link_column_head(), rotunda("RotundaHigh", UPPER_FLOOR)]
    hu = (HEAD_W + HEAD_UPPER_WIDER) / 2
    parts += [head_storey("Head", HEAD_W / 2, HEAD_BOT), head_storey("HeadUpper", hu, HEAD_UPPER_BOT),
              head_end("HeadEnd", HEAD_W / 2, HEAD_BOT, 0.05), head_end("HeadUpperEnd", hu, HEAD_UPPER_BOT, 0.08)]
    for p in parts:
        dx = LAYOUT[p.name]
        bmesh.ops.translate(p.bm, vec=V(dx, 0.0, 0.0), verts=p.bm.verts[:])
        p.pivot = V(dx, 0.0, 0.0)
    return parts


# ============================================================== previews: the bridge assembled, docked

TERMINAL_Y = -12.0         # the terminal's face (forward), where the links start
LINK_END = 1.6             # a link stops this far short of its rotunda's centre (its corners inside the drum)
INTO_WALL = 0.3            # a stub link starts this far inside the head's wall
CAB_HEADING = -90.0        # the cabs face the fuselage (left), square to it


def _yaw(dx, dy):
    return math.degrees(math.atan2(dx, dy))


def _column(out, at, yaw, under, tag="", widen=1.0):
    """A link column (foot, shaft, head) at ground point `at` under a link (or the head) whose underside is `under`;
    `tag` names copies of the parts, `widen` the head's cross-head across (under a main link)."""
    out["LinkColumnFoot" + tag] = M(at[0], at[1], 0.0, yaw=yaw)
    out["LinkColumn" + tag] = M(at[0], at[1], COLUMN_BASE, yaw=yaw) @ Matrix.Diagonal(
        (1.0, 1.0, under - COLUMN_SHAFT_TOP - COLUMN_BASE, 1.0))
    out["LinkColumnHead" + tag] = M(at[0], at[1], under, yaw=yaw) @ Matrix.Diagonal((widen, 1.0, 1.0, 1.0))


def _bridge(at, floor, heading, pitch, run, column=None, cab_turn=None, link_from=None, tag=""):
    """A docked bridge (preview only): rotunda centre `at` (right, forward), floor height, heading and pitch of the
    tunnel, its run (Tunnel_A's pivot to the cab's pivot), the cab's turn from the tunnel (else square to the
    fuselage), and its link, level at its floor, from `link_from` (default: straight out of the terminal's face)
    with a column `column` metres along it (None: none, a stub). Returns part name (+ `tag` for copies) -> world
    matrix; "Rotunda" stands for the high one on an upper-deck bridge."""
    yaw = math.radians(heading)
    d = (math.sin(yaw), math.cos(yaw))
    p = math.radians(pitch)
    ax = (d[0] * math.cos(p), d[1] * math.cos(p), math.sin(p))
    a = (at[0] + TUNNEL_START * d[0], at[1] + TUNNEL_START * d[1], floor)

    def along(s):
        return (a[0] + s * ax[0], a[1] + s * ax[1], a[2] + s * ax[2])

    c0 = run - SECTION_L                                   # Tunnel_C's pivot; B halfway, A at the start
    out = {"Rotunda": M(at[0], at[1], 0.0, yaw=heading)}
    for n, s in (("Tunnel_A", 0.0), ("Tunnel_B", c0 / 2), ("Tunnel_C", c0)):
        out[n + tag] = M(*along(s), yaw=heading, pitch=pitch)
    out["Cab" + tag] = M(*along(run), yaw=CAB_HEADING if cab_turn is None else heading + cab_turn)
    yoke = out["Tunnel_C" + tag] @ V(0.0, SECTION_L - DRIVE_FROM_END, YOKE_BOTTOM)
    out["Drive" + tag] = M(yoke.x, yoke.y, 0.0, yaw=heading)
    out["DriveLegs" + tag] = M(yoke.x, yoke.y, LEG_BASE, yaw=heading) @ Matrix.Diagonal(
        (1.0, 1.0, yoke.z - LEG_BASE, 1.0))
    lf = link_from or (at[0], TERMINAL_Y)
    dx, dy = at[0] - lf[0], at[1] - lf[1]
    dist = math.hypot(dx, dy)
    ly = _yaw(dx, dy)
    out["Link" + tag] = M(lf[0], lf[1], floor, yaw=ly) @ Matrix.Diagonal((1.0, dist - LINK_END, 1.0, 1.0))
    if column is not None:
        _column(out, (lf[0] + dx / dist * column, lf[1] + dy / dist * column), ly, floor + LINK_BOT, tag)
    return out


def _aim(at, floor, door, sill):
    """heading, pitch, run for a bridge from rotunda `at` (floor) to put its cab's pivot on `door` at `sill`."""
    dx, dy = door[0] - at[0], door[1] - at[1]
    horiz = math.hypot(dx, dy) - TUNNEL_START
    return math.degrees(math.atan2(dx, dy)), math.degrees(math.atan2(sill - floor, horiz)), math.hypot(horiz, sill - floor)


# the main previews: one bridge, tunnel out to 25 m, sloping 2° down to the door, its link 10.4 m out of the terminal
LOWER = dict(at=(0.0, 0.0), floor=ROTUNDA_FLOOR, heading=-60.0, pitch=-2.0, run=25.0, column=5.0)
# the main bridge with its cab turned 85 degrees from the tunnel (near its limit), from either side
TURNED_VIEWS = ("turned", "turnedin")
# the head views: a double-deck gate. The head's centreline runs out from y = 8 to y = 36 (x = 0), its main link
# straight out of the terminal's face; off its left side a main-deck bridge (L) and an upper-deck one (UL, on
# RotundaHigh, level with the head's upper floor) dock at doors on a fuselage whose side is 32 m to the left: L 5.2 m
# up at y = 26, UL 7.9 m up 12.5 m further aft.
HEAD_A, HEAD_B = 8.0, 36.0
HEAD_STATIONS = (9.2, 22.0, 34.8)          # its column pairs (forward)
_wall = -HEAD_W / 2
_rx = _wall - HEAD_STUB - ROTUNDA_R
_pivot = -32.0 + CAB_REACH + 0.1
_h, _p, _r = _aim((_rx, 30.0), ROTUNDA_FLOOR, (_pivot, 26.0), 5.2)
HEAD_MAIN = dict(at=(_rx, 30.0), floor=ROTUNDA_FLOOR, heading=_h, pitch=_p, run=_r, link_from=(_wall + INTO_WALL, 30.0))
_h, _p, _r = _aim((_rx, 16.0), UPPER_FLOOR, (_pivot, 13.5), 7.9)
HEAD_UPPER = dict(at=(_rx, 16.0), floor=UPPER_FLOOR, heading=_h, pitch=_p, run=_r, link_from=(_wall + INTO_WALL, 16.0),
                  tag=":u")
HEAD_VIEWS = ("head34", "headside", "headend", "headunder")
_copies = {}               # preview-only copies of parts (name ":" tag), sharing their meshes (made after export)


def _head():
    """The head, its main link and their columns (preview only): part name (":tag" for copies) -> world matrix."""
    length = HEAD_B - HEAD_A
    out = {
        "Head": M(0.0, HEAD_A, ROTUNDA_FLOOR) @ Matrix.Diagonal((1.0, length, 1.0, 1.0)),
        "HeadUpper": M(0.0, HEAD_A, UPPER_FLOOR) @ Matrix.Diagonal((1.0, length, 1.0, 1.0)),
        "HeadEnd": M(0.0, HEAD_B, ROTUNDA_FLOOR),
        "HeadUpperEnd": M(0.0, HEAD_B, UPPER_FLOOR),
        "HeadEnd:a": M(0.0, HEAD_A, ROTUNDA_FLOOR, yaw=180.0),
        "HeadUpperEnd:a": M(0.0, HEAD_A, UPPER_FLOOR, yaw=180.0),
        # the main link from the terminal's face into the head's end, widened, on one column halfway
        "Link:main": M(0.0, TERMINAL_Y, ROTUNDA_FLOOR) @ Matrix.Diagonal(
            (MAIN_LINK_W / LINK_W, HEAD_A + 0.5 - TERMINAL_Y, 1.0, 1.0)),
    }
    _column(out, (0.0, (TERMINAL_Y + HEAD_A) / 2), 0.0, ROTUNDA_FLOOR + LINK_BOT, ":main", MAIN_LINK_W / LINK_W)
    for i, y in enumerate(HEAD_STATIONS):
        for k, x in enumerate((-HEAD_COLUMN_X, HEAD_COLUMN_X)):
            _column(out, (x, y), 0.0, ROTUNDA_FLOOR + HEAD_BOT, ":h%d%d" % (i, k))
    return out


def preview_pose(view):
    import bpy
    obj = bpy.data.objects

    def part(n):
        if ":" not in n:
            return obj[n]
        if n not in _copies:
            src = obj[n.split(":")[0]]
            c = src.copy()                                 # shares the mesh (and its baked material)
            c.name = n.replace(":", "_Preview_")
            for col in src.users_collection:
                col.objects.link(c)
            _copies[n] = c
        return _copies[n]

    if view in HEAD_VIEWS:
        pose = _head()
        pose.update(_bridge(**HEAD_MAIN))
        for n, m in _bridge(**HEAD_UPPER).items():
            pose["RotundaHigh" if n == "Rotunda" else n] = m
    else:
        pose = _bridge(**(dict(LOWER, cab_turn=-85.0) if view in TURNED_VIEWS else LOWER))
    shown = set()
    for n, m in pose.items():
        ob = part(n)
        ob.matrix_world = m
        shown.add(ob.name)
    for ob in [obj[n] for n in ["Rotunda"] + MOVERS] + list(_copies.values()):
        ob.hide_render = ob.name not in shown


def _views():
    P = _bridge(**LOWER)
    mid = P["Tunnel_B"] @ V(0.0, 6.5, 1.0)
    front = P["Cab"] @ V(0.0, CAB_REACH, 1.1)
    yaw = math.radians(LOWER["heading"])
    left = (-math.cos(yaw), math.sin(yaw))            # the bridge's left side
    yoke = P["Tunnel_C"] @ V(0.0, SECTION_L - DRIVE_FROM_END, 0.0)
    joint = P["Cab"] @ V(0.0, 0.0, 0.0)
    lip = P["Tunnel_B"] @ V(0.0, 0.0, 0.0)
    c = (mid.x, mid.y, 2.5)
    T = _bridge(**dict(LOWER, cab_turn=-85.0))
    tj = T["Cab"] @ V(0.0, 0.0, 1.2)
    back = (-math.sin(yaw), -math.cos(yaw))
    hc = (-12.0, 22.0, 6.0)                           # between the head and the cabs
    return {
        "aerial34": ((c[0] - 30.0, c[1] - 26.0, 24.0), c, 35),
        "side": ((mid.x + 44.0 * left[0], mid.y + 44.0 * left[1], 4.0), (mid.x, mid.y, 3.2), 35),
        "cab": ((front.x - 6.5, front.y - 3.2, front.z + 0.9), (front.x, front.y, front.z - 0.3), 35),
        "top": ((-11.0, 1.0, 62.0), (-11.0, 1.0, 0.0), 35),
        "rotunda": ((9.0, -9.5, 7.5), (0.0, -1.0, 4.6), 35),
        "drive": ((yoke.x - 7.0, yoke.y - 5.5, 2.2), (yoke.x, yoke.y, 1.6), 35),
        "joint": ((joint.x + 5.0 * left[0] - 2.0, joint.y + 5.0 * left[1] - 1.0, joint.z + 2.2),
                  (joint.x, joint.y, joint.z + 1.0), 35),
        "lip": ((lip.x + 6.0 * left[0], lip.y + 6.0 * left[1], lip.z + 4.0), (lip.x, lip.y, lip.z + 1.2), 35),
        "turned": ((tj.x + 6.5 * left[0] + 3.0 * back[0], tj.y + 6.5 * left[1] + 3.0 * back[1], tj.z + 3.5),
                   (tj.x, tj.y, tj.z), 35),
        "turnedin": ((tj.x - 6.5 * left[0] + 3.0 * back[0], tj.y - 6.5 * left[1] + 3.0 * back[1], tj.z + 2.0),
                     (tj.x, tj.y, tj.z), 35),
        "head34": ((hc[0] - 40.0, hc[1] + 46.0, 34.0), hc, 35),
        "headside": ((hc[0] - 34.0, hc[1] - 2.0, 7.0), (hc[0] + 6.0, hc[1], 6.5), 35),
        "headend": ((16.0, HEAD_B + 30.0, 14.0), (0.0, (HEAD_A + HEAD_B) / 2, 6.5), 35),
        "headunder": ((14.0, TERMINAL_Y + 4.0, 2.5), (0.0, HEAD_A + 4.0, 4.5), 35),
    }


VIEWS = _views()


def grime(pos, ao):
    """Crevice dirt everywhere, road dust low on the parts that stand on the apron (laid out at LAYOUT x)."""
    import numpy as np
    x, z = pos[..., 0], pos[..., 2]
    ground = np.zeros_like(x)
    for n, hw in GROUNDED.items():
        ground = np.maximum(ground, (np.abs(x - LAYOUT[n]) < hw).astype(np.float32))
    low = np.clip(1.0 - z / 0.9, 0, 1) ** 2 * ground
    crev = np.clip(1.0 - ao, 0, 1) ** 1.6
    return np.clip(0.3 * low + 0.15 * crev, 0, 0.45).astype(np.float32)
