"""Small control tower for a grass airfield: a two-storey rendered flight office / clubhouse with a glazed cab on
its flat roof, as on small European airfields (Freiburg EDTF's cab, with its red-and-white roof edge). A roof
terrace with railings round the cab, an external steel stair up the left side (a landing at the first floor door,
then on to the roof), a radio mast with an obstruction light, whip antennas and a wind mast on the cab roof.

Space: (right, forward, up) metres, origin on the ground at the middle of the building's front; the building lies
behind it and the front (forward) faces the airfield.
"""
import tower
from kit import Part
from tower import anemometer, antenna, belt, cab, chamfer_rect, flight, railing, window

NAME = "TowerSmall"
DISPLAY_NAME = "Control tower (small)"
KIND = "Tower"
AO_DISTANCE = 1.2
AO_STRENGTH = 0.55
BAKE_SIZE = 4096
LOD_RATIOS = (0.35, 0.1)
PREVIEW_GROUND = (0.3, 0.3, 0.31)

MATS = dict(tower.MATS, **{
    "Render": ("#E7E4DC", 0.0, 0.85, False),
    "Plinth": ("#5B5E61", 0.0, 0.85, False),
    "Band": ("#C9C6BE", 0.0, 0.8, False),
    "SignalRed": ("#C8231C", 0.0, 0.45, False),
})
GRIME = tower.GRIME
grime = tower.grime

WIDTH, DEPTH = 10.0, 7.6
PLINTH, FIRST, ROOF, PARAPET = 0.45, 3.2, 6.35, 6.75
CAB = chamfer_rect(4.6, 4.6, 0.9, 1.5, -3.5)
CAB_GLASS, CAB_TOP = 7.25, 9.65
CAB_ROOF = CAB_TOP + 0.6 - 0.08     # the deck inside the chequered upstand
GAP = (-1.15, -0.2)                 # the opening in the left parapet where the stair arrives
INNER, OUTER = (-6.2, -5.12), (-7.3, -6.25)     # the stair's two lanes, outside the left wall
LANDING = (-6.45, -5.0)             # first floor landing (forward range)
MAST = (-0.1, -5.3, 3.2)            # radio mast on the cab roof: right, forward, height

WHEELS = []
MOVERS = []
SPINNERS = []
WORK = {}

LIGHTS = [
    ("Flood_Front", "Flood", (0.0, 0.45, 5.95), (0, 0.6, -1), "#FFF1D8", 1.0, 0.0),
    ("Obstruction", "Marker", (MAST[0], MAST[1], CAB_ROOF + MAST[2] + 0.12), (0, 0, 1), "#FF2A1A", 0.9, 0.0),
]

VIEWS = {
    "front34": ((-20.0, 20.0, 11.0), (0.5, -3.5, 5.0), 35),
    "rear34": ((22.0, -30.0, 13.0), (0.0, -3.8, 5.0), 35),
    "stair": ((-17.0, 5.0, 4.5), (-5.5, -3.2, 3.6), 35),
    "cab": ((-5.0, 7.0, 12.0), (1.9, -3.5, 8.6), 35),
    "overview": ((-38.0, 40.0, 42.0), (0.0, -3.8, 3.0), 35),
}


def build():
    b = Part("Body")
    hw = WIDTH / 2
    # walls on a dark plinth, a band at the first floor
    b.box((-hw - 0.05, -DEPTH - 0.05, 0.0), (hw + 0.05, 0.05, PLINTH), "Plinth")
    b.box((-hw, -DEPTH, PLINTH), (hw, 0.0, ROOF), "Render")
    belt(b, -hw, -DEPTH, hw, 0.0, FIRST - 0.08, FIRST + 0.08, "Band")
    # parapet with a metal coping (open where the stair arrives), the roof deck inside it
    t = 0.2
    walls = [((-hw, -t), (hw, 0.0)), ((-hw, -DEPTH), (hw, -DEPTH + t)), ((hw - t, -DEPTH + t), (hw, -t)),
             ((-hw, -DEPTH + t), (-hw + t, GAP[0])), ((-hw, GAP[1]), (-hw + t, -t))]
    for (x0, y0), (x1, y1) in walls:
        b.box((x0, y0, ROOF), (x1, y1, PARAPET), "Render")
        b.box((x0 - 0.03, y0 - 0.03, PARAPET), (x1 + 0.03, y1 + 0.03, PARAPET + 0.06), "Rail")
    b.box((-hw + t, -DEPTH + t, ROOF), (hw - t, -t, ROOF + 0.03), "RoofDeck")
    railing(b, [(-hw + 0.1, GAP[0] - 0.05), (-hw + 0.1, -DEPTH + 0.1), (hw - 0.1, -DEPTH + 0.1), (hw - 0.1, -0.1),
                (-hw + 0.1, -0.1), (-hw + 0.1, GAP[1] + 0.05)], PARAPET + 0.06, h=0.62, closed=False)
    # the cab on the roof, its roof edge chequered red and white
    cab(b, CAB, ROOF + 0.03, CAB_GLASS, CAB_TOP, sill="Render", over=0.35, roof_h=0.6, chequer=("SignalRed", "White"))
    antenna(b, MAST[0], MAST[1], CAB_ROOF, MAST[2], r=0.045)
    antenna(b, 3.3, -5.3, CAB_ROOF, 2.0)
    antenna(b, 3.4, -1.8, CAB_ROOF, 1.5)
    anemometer(b, 0.0, -1.8, CAB_ROOF, 1.9)
    windows(b)
    stair(b)
    # entrance canopy, a condenser on the roof terrace, downpipes at the back corners
    b.box((-4.6, 0.0, 2.95), (-2.8, 1.25, 3.08), "Frame")
    b.box((-3.9, -6.9, ROOF + 0.03), (-2.6, -6.1, ROOF + 0.85), "Plant", bevel=0.03)
    b.cyl((-3.25, -6.5, ROOF + 0.85), (-3.25, -6.5, ROOF + 0.88), 0.32, "Dark", segs=16)
    for x in (-hw + 0.25, hw - 0.25):
        b.cyl((x, -DEPTH - 0.08, 0.1), (x, -DEPTH - 0.08, PARAPET), 0.05, "Rail", segs=8)
    tower.lamps(b, LIGHTS)
    return [b]


def windows(b):
    hw = WIDTH / 2
    front, back = ("y", 0.0, 1), ("y", -DEPTH, -1)
    right, left = ("x", hw, 1), ("x", -hw, -1)
    window(b, front, -3.7, PLINTH, 1.2, 2.3)                    # glazed entrance door
    for x in (-1.4, 1.0, 3.4):
        window(b, front, x, 0.95, 1.9, 1.55, bars=1)
    for x in (-2.4, 2.4):
        window(b, front, x, 4.1, 3.6, 1.5, bars=2)               # briefing room
    for y in (-2.0, -5.4):
        window(b, right, y, 1.0, 1.4, 1.4)
        window(b, right, y, 4.1, 1.4, 1.4)
    window(b, back, 2.8, PLINTH, 1.0, 2.2)
    window(b, back, -1.5, 1.4, 1.2, 1.0)
    for x in (-2.5, 1.5):
        window(b, back, x, 4.15, 1.4, 1.3)
    window(b, left, -3.0, 1.0, 1.4, 1.4)
    window(b, left, -5.72, FIRST, 1.0, 2.2)                      # first floor door onto the stair landing


def stair(b):
    """Two flights outside the left wall: up the outer lane to the first floor landing at the back, then up the
    inner lane to a landing at the parapet opening."""
    flight(b, OUTER[0], OUTER[1], -1.0, 0.0, 4.0, FIRST, back=True)
    flight(b, INNER[0], INNER[1], LANDING[1], FIRST, GAP[0] - LANDING[1], ROOF - FIRST)
    x0, x1 = OUTER[0], -WIDTH / 2
    b.box((x0, LANDING[0], FIRST - 0.08), (x1, LANDING[1], FIRST), "Galv")
    b.box((INNER[0], GAP[0], ROOF - 0.08), (-WIDTH / 2, GAP[1] + 0.1, ROOF), "Galv")
    for x, y, top in ((x0 + 0.05, LANDING[0] + 0.05, FIRST), (x0 + 0.05, LANDING[1] - 0.05, FIRST),
                      (INNER[0] + 0.05, GAP[0] + 0.05, ROOF), (INNER[0] + 0.05, GAP[1] + 0.05, ROOF)):
        b.box((x - 0.05, y - 0.05, 0.0), (x + 0.05, y + 0.05, top - 0.08), "Galv")
    railing(b, [(x0 + 0.03, LANDING[1]), (x0 + 0.03, LANDING[0] + 0.03), (x1, LANDING[0] + 0.03)], FIRST, h=1.0,
            closed=False, mat="Galv")
    railing(b, [(INNER[0] + 0.03, GAP[0]), (INNER[0] + 0.03, GAP[1] + 0.07), (-WIDTH / 2, GAP[1] + 0.07)], ROOF,
            h=1.0, closed=False, mat="Galv")
