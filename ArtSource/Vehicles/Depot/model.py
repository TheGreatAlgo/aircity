"""Airside maintenance depot: a steel-clad portal-frame garage for the snow ploughs and mowers. Four tall roller
doors with yellow guard posts, an office end with windows and a personnel door, standing-seam cladding on a
concrete plinth, a low-pitch roof with gutters and downpipes, and flood lights over the doors.

The vehicles park inside: behind the doors is a hollow hall (concrete floor with yellow bay lines, lined walls with
a dark dado, a dark ceiling, steel columns and rafters between the drive lanes). The door curtains are separate
parts, Door_0..3, pivoted at the top of their openings: Unity rolls them up by scaling their height. They stay
separate in the far LODs too (SEPARATE_MOVERS), and their openings and the hall's free space are exported for the
game (DOOR_PARTS, INTERIOR).

Space: (right, forward, up) metres, origin on the ground at the middle of the door front; the building lies
behind it (forward = out of the doors).
"""
import math

from kit import Part

NAME = "Depot"
DISPLAY_NAME = "Maintenance depot"
KIND = "Depot"
AO_DISTANCE = 1.2
AO_STRENGTH = 0.5
BAKE_SIZE = 4096           # saved at 2048: a building has far more surface than a vehicle
LOD_RATIOS = (0.35, 0.1)
PREVIEW_GROUND = (0.3, 0.3, 0.31)

MATS = {
    "Cladding": ("#AEB4B8", 0.3, 0.45, False),
    "Trim": ("#3F4A57", 0.3, 0.45, False),
    "Roof": ("#4B5157", 0.4, 0.5, False),
    "Concrete": ("#9C9A94", 0.0, 0.85, False),
    "Door": ("#C9CDD0", 0.4, 0.4, False),
    "Guard": ("#F2B40F", 0.0, 0.4, False),
    "Floor": ("#8A8883", 0.0, 0.8, False),
    "Dado": ("#4A5058", 0.2, 0.5, False),
    "Ceiling": ("#2E3135", 0.3, 0.6, False),
}
GRIME = "#57524A"

WIDTH, DEPTH = 46.0, 24.0
EAVES, RIDGE = 8.6, 10.2
DOOR_W, DOOR_H = 6.5, 6.4
DOORS = (-18.0, -9.5, -1.0, 7.5)           # door centres (right); the office end is right of them
OFFICE_X0 = 12.0
WALL = 0.25                                # wall thickness
PARTITION = 11.5                           # the hall runs from the left wall to here; the office beyond
CEILING = 8.3

WHEELS = []
MOVERS = [f"Door_{i}" for i in range(len(DOORS))]
SEPARATE_MOVERS = True                     # doors stay separate at every LOD (they roll up)
SPINNERS = []
# the doors' openings: name, bottom centre on the outer wall (right, forward, up), width, height
DOOR_PARTS = [(f"Door_{i}", (d, 0.0, 0.0), DOOR_W, DOOR_H) for i, d in enumerate(DOORS)]
# the hall's free floor space (right, forward, up): inside the walls, floor to ceiling
INTERIOR = ((-WIDTH / 2 + WALL, -DEPTH + WALL, 0.0), (PARTITION, -WALL, CEILING))

LIGHTS = [(f"Flood_{i}", "Flood", (x, 0.55, DOOR_H + 1.15), (0, 0.55, -1), "#FFF1D8", 1.1, 0.0) for i, x in enumerate(DOORS)] + [
    ("Flood_Office", "Flood", (17.0, 0.55, 4.2), (0, 0.55, -1), "#FFF1D8", 1.0, 0.0),
]

WORK = {}

VIEWS = {
    "front34": ((-38.0, 42.0, 18.0), (0.0, -10.0, 3.5), 35),
    "front": ((0.0, 60.0, 6.0), (0.0, 0.0, 5.0), 35),
    "rear34": ((40.0, -60.0, 20.0), (0.0, -12.0, 4.0), 35),
    "doors": ((-14.0, 16.0, 4.0), (-6.0, 0.0, 4.0), 35),
    "open": ((-4.0, 15.0, 3.2), (-7.0, -12.0, 2.5), 28),
    "inside": ((9.0, -21.0, 5.5), (-12.0, -3.0, 1.5), 22),
}
# views rendered with the doors rolled up
PREVIEW_OPEN = ("open", "inside")


def build():
    b = Part("Body")
    hw = WIDTH / 2
    shell(b)
    hall(b)
    for f0 in (0.0, -DEPTH):
        prof = [(-hw, EAVES), (hw, EAVES), (0.0, RIDGE)]
        b.prism([(x, z) for x, z in prof], "f", f0 - 0.0 if f0 < 0 else f0 - 0.2, 0.2, "Cladding")
    # standing seams on every wall
    for x in [-hw + 0.3 + k * 0.6 for k in range(int(WIDTH / 0.6))]:
        in_door = any(abs(x - d) < DOOR_W / 2 + 0.3 for d in DOORS)
        in_window = x > OFFICE_X0 - 0.2 and any(abs(x - w) < 1.1 for w in (OFFICE_X0 + 1.0, OFFICE_X0 + 3.6, OFFICE_X0 + 8.0,
                                                                            OFFICE_X0 + 10.6))
        z0 = DOOR_H + 0.65 if in_door else 6.2 if in_window else 0.6
        b.box((x - 0.04, 0.0, z0), (x + 0.04, 0.03, gable(x)), "Cladding", bevel=0.01)
        b.box((x - 0.04, -DEPTH - 0.03, 0.6), (x + 0.04, -DEPTH, gable(x)), "Cladding", bevel=0.01)
    for f in [-0.3 - k * 0.6 for k in range(int(DEPTH / 0.6))]:
        for s in (-1, 1):
            b.box((s * hw - 0.015 + s * 0.015, f - 0.04, 0.6), (s * hw + 0.015 + s * 0.015, f + 0.04, EAVES), "Cladding",
                  bevel=0.01)
    # corner and eaves flashing
    for s in (-1, 1):
        for f in (0.0, -DEPTH):
            b.box((s * hw - 0.12, f - 0.12, 0.6), (s * hw + 0.12, f + 0.12, EAVES + 0.05), "Trim")
    # roof: two slopes with overhang, ridge cap, gutters and downpipes
    over = 0.45
    for s in (-1, 1):
        x0, x1 = 0.0, s * (hw + over)
        z0, z1 = RIDGE + 0.05, EAVES - (RIDGE - EAVES) * over / hw + 0.05
        prof = [(x0, z0), (x1, z1), (x1, z1 + 0.18), (x0, z0 + 0.18)]
        b.prism(prof if s > 0 else prof[::-1], "f", -DEPTH - over, DEPTH + 2 * over, "Roof")
        b.box((s * (hw + over) - 0.12, -DEPTH - over, z1 - 0.18), (s * (hw + over) + 0.12, over, z1 + 0.02), "Trim")
        for f in (-0.6, -DEPTH + 0.6, -DEPTH / 2):
            b.cyl((s * (hw + 0.25), f, 0.6), (s * (hw + 0.25), f, z1 - 0.1), 0.07, "Trim", segs=10)
    b.box((-0.3, -DEPTH - over, RIDGE + 0.15), (0.3, over, RIDGE + 0.3), "Trim")
    # roof ribs (standing seams)
    for f in [-DEPTH - over + 0.4 + k * 0.8 for k in range(int((DEPTH + 2 * over) / 0.8))]:
        for s in (-1, 1):
            x1 = s * (hw + over)
            z1 = EAVES - (RIDGE - EAVES) * over / hw + 0.05
            b.cyl((0.0, f, RIDGE + 0.24), (x1, f, z1 + 0.19), 0.03, "Roof", segs=4)
    # roller doors: a frame and hood box round each opening, yellow guard posts in front; the curtains are parts
    doors = []
    for i, d in enumerate(DOORS):
        x0, x1 = d - DOOR_W / 2, d + DOOR_W / 2
        b.box((x0 - 0.22, 0.0, 0.0), (x0, 0.18, DOOR_H + 0.35), "Trim")
        b.box((x1, 0.0, 0.0), (x1 + 0.22, 0.18, DOOR_H + 0.35), "Trim")
        b.box((x0 - 0.22, 0.0, DOOR_H), (x1 + 0.22, 0.45, DOOR_H + 0.6), "Trim", bevel=0.05)
        doors.append(curtain(i, d))
        for x in (x0 - 0.45, x1 + 0.45):
            b.cyl((x, 0.65, 0.0), (x, 0.65, 1.25), 0.11, "Guard", segs=14)
            b.cyl((x, 0.65, 1.25), (x, 0.65, 1.3), 0.1, "Dark", segs=14)
    # office end: windows in a row, a personnel door, a canopy
    for x in (OFFICE_X0 + 1.0, OFFICE_X0 + 3.6, OFFICE_X0 + 8.0, OFFICE_X0 + 10.6):
        for z0 in (1.4, 4.6):
            b.box((x - 1.0, -0.05, z0), (x + 1.0, 0.08, z0 + 1.5), "Trim")
            b.box((x - 0.92, 0.075, z0 + 0.08), (x + 0.92, 0.09, z0 + 1.42), "Glass")
            b.box((x - 0.03, 0.08, z0 + 0.08), (x + 0.03, 0.1, z0 + 1.42), "Trim")
    b.box((OFFICE_X0 + 5.3, -0.05, 0.6), (OFFICE_X0 + 6.5, 0.1, 2.9), "Trim")
    b.box((OFFICE_X0 + 5.4, 0.1, 0.62), (OFFICE_X0 + 6.4, 0.12, 2.8), "Door")
    b.box((OFFICE_X0 + 4.6, -0.1, 3.0), (OFFICE_X0 + 7.2, 1.4, 3.15), "Trim")
    b.box((OFFICE_X0 + 4.9, 0.0, 0.0), (OFFICE_X0 + 7.0, 1.6, 0.6), "Concrete")     # step
    # side personnel door and a louvred vent on the left gable wall
    b.box((-WIDTH / 2 - 0.1, -6.0, 0.6), (-WIDTH / 2 + 0.02, -4.8, 2.9), "Trim")
    b.box((-WIDTH / 2 - 0.12, -5.9, 0.62), (-WIDTH / 2 - 0.1, -4.9, 2.8), "Door")
    for k in range(8):
        b.box((-WIDTH / 2 - 0.15, -14.0, 5.0 + k * 0.18), (-WIDTH / 2, -12.0, 5.07 + k * 0.18), "Trim")
    lamps(b)
    return [b] + doors


def shell(b):
    """Plinth and walls round the hall (with the door openings down to the ground) and the solid office end."""
    hw = WIDTH / 2
    edges = [-hw]
    for d in DOORS:
        edges += [d - DOOR_W / 2, d + DOOR_W / 2]
    edges.append(PARTITION)
    # front: piers between the openings (on the plinth), the wall over them
    for k in range(0, len(edges), 2):
        xa, xb = edges[k], edges[k + 1]
        b.box((xa - (0.05 if k == 0 else 0.0), -WALL, 0.0), (xb, 0.05, 0.6), "Concrete")
        b.box((xa, -WALL, 0.6), (xb, 0.0, DOOR_H), "Cladding")
    b.box((-hw, -WALL, DOOR_H), (PARTITION, 0.0, EAVES), "Cladding")
    # back and left walls of the hall
    b.box((-hw - 0.05, -DEPTH - 0.05, 0.0), (PARTITION, -DEPTH + WALL, 0.6), "Concrete")
    b.box((-hw, -DEPTH, 0.6), (PARTITION, -DEPTH + WALL, EAVES), "Cladding")
    b.box((-hw - 0.05, -DEPTH + WALL, 0.0), (-hw + WALL, -WALL, 0.6), "Concrete")
    b.box((-hw, -DEPTH + WALL, 0.6), (-hw + WALL, -WALL, EAVES), "Cladding")
    # the office end, solid
    b.box((PARTITION, -DEPTH - 0.05, 0.0), (hw + 0.05, 0.05, 0.6), "Concrete")
    b.box((PARTITION, -DEPTH, 0.6), (hw, 0.0, EAVES), "Cladding")


def face(b, pts, normal, mat):
    """A one-sided quad facing `normal` (the hall's lining faces inwards)."""
    import numpy as np
    p = [np.array(q, float) for q in pts]
    if np.dot(np.cross(p[1] - p[0], p[2] - p[0]), np.array(normal, float)) < 0:
        pts = pts[::-1]
    b.quad(pts, mat)


def hall(b):
    """The inside: floor with bay lines, lined walls with a dado, ceiling, columns and rafters between the lanes."""
    (x0, y0, _), (x1, y1, _) = INTERIOR
    floor = 0.015
    face(b, [(x0, y0, floor), (x1, y0, floor), (x1, 0.0, floor), (x0, 0.0, floor)], (0, 0, 1), "Floor")
    # the ceiling, a thin slab (closed above too, for the gap under the eaves)
    b.box((x0, y0, CEILING), (x1, y1, CEILING + 0.06), "Ceiling")
    # dado along the walls, a hair inside them
    e = 0.01
    dado = 1.2
    face(b, [(x0, y0 + e, 0.0), (x1, y0 + e, 0.0), (x1, y0 + e, dado), (x0, y0 + e, dado)], (0, 1, 0), "Dado")
    face(b, [(x0 + e, y0, 0.0), (x0 + e, y1, 0.0), (x0 + e, y1, dado), (x0 + e, y0, dado)], (1, 0, 0), "Dado")
    face(b, [(x1 - e, y0, 0.0), (x1 - e, y1, 0.0), (x1 - e, y1, dado), (x1 - e, y0, dado)], (-1, 0, 0), "Dado")
    piers = [x0]
    for d in DOORS:
        piers += [d - DOOR_W / 2, d + DOOR_W / 2]
    piers.append(x1)
    for k in range(0, len(piers), 2):
        xa, xb = piers[k], piers[k + 1]
        face(b, [(xa, y1 - e, 0.0), (xb, y1 - e, 0.0), (xb, y1 - e, dado), (xa, y1 - e, dado)], (0, -1, 0), "Dado")
    # bay lines along the drive lanes, and a stop line at the back of each
    for d in DOORS:
        for x in (d - DOOR_W / 2 - 0.06, d + DOOR_W / 2 + 0.06):
            face(b, [(x - 0.06, y0 + 0.6, 0.02), (x + 0.06, y0 + 0.6, 0.02), (x + 0.06, -0.8, 0.02), (x - 0.06, -0.8, 0.02)],
                 (0, 0, 1), "Guard")
        face(b, [(d - 2.5, y0 + 0.5, 0.02), (d + 2.5, y0 + 0.5, 0.02), (d + 2.5, y0 + 0.75, 0.02), (d - 2.5, y0 + 0.75, 0.02)],
             (0, 0, 1), "Guard")
    # steel portal frames between the lanes: columns at the front, middle and back, rafters under the ceiling
    for k in range(len(DOORS) - 1):
        x = (DOORS[k] + DOORS[k + 1]) / 2
        for y in (y1 - 0.35, (y0 + y1) / 2, y0 + 0.35):
            b.box((x - 0.17, y - 0.17, 0.0), (x + 0.17, y + 0.17, CEILING - 0.6), "Steel")
        b.box((x - 0.15, y0, CEILING - 0.6), (x + 0.15, y1, CEILING), "Steel")
    for y in ((y0 + y1) / 2,):
        b.box((x0, y - 0.12, CEILING - 0.45), (x1, y + 0.12, CEILING), "Steel")


def curtain(i, d):
    """Door i's roller curtain (a part pivoted at the top of the opening, on the door's plane), ribbed, with a
    vision panel strip."""
    x0, x1 = d - DOOR_W / 2, d + DOOR_W / 2
    p = Part(f"Door_{i}", pivot=(d, -0.075, DOOR_H))
    p.box((x0, -0.15, 0.0), (x1, 0.0, DOOR_H), "Door")
    for k in range(int(DOOR_H / 0.3)):
        z = 0.1 + k * 0.3
        if z + 0.1 > DOOR_H:
            break
        p.box((x0 + 0.05, -0.02, z), (x1 - 0.05, 0.025, z + 0.1), "Door", bevel=0.01)
    p.box((x0 + 0.4, 0.035, 3.4), (x1 - 0.4, 0.05, 3.7), "Glass")
    return p


def gable(x):
    """Wall top at x on the front / back walls (under the gable)."""
    return EAVES + (RIDGE - EAVES) * (1 - abs(x) / (WIDTH / 2))


def lamps(b):
    for name, kind, p, d, *_ in LIGHTS:
        r, f, z = p
        b.box((r - 0.04, -0.02, z + 0.05), (r + 0.04, f - 0.15, z + 0.12), "Dark")
        b.box((r - 0.3, f - 0.25, z - 0.03), (r + 0.3, f + 0.15, z + 0.1), "Dark", bevel=0.02)
        b.box((r - 0.26, f - 0.22, z - 0.045), (r + 0.26, f + 0.12, z - 0.03), "LensWhite")


def grime(pos, ao):
    import numpy as np
    z = pos[..., 2]
    low = np.clip(1.0 - z / 1.6, 0, 1) ** 2.0
    crev = np.clip(1.0 - ao, 0, 1) ** 1.6
    streak = np.clip((np.sin(pos[..., 0] * 3.1) * 0.5 + 0.5) * np.clip((z - 6.0) / 3.0, 0, 1), 0, 1) * 0.12
    return np.clip(0.3 * low + 0.25 * crev + streak, 0, 0.5).astype(np.float32)
