"""Medium control tower for a regional airport: a two-storey ATC office block with a square concrete shaft rising
out of its right end (a glazed stair slot up its front), a wider equipment floor with a window ribbon at the top,
a catwalk, and an octagonal cab with glass leaning out. The roof carries the aerodrome beacon (white / green),
antennas, a wind mast and an obstruction light on a mast. Cab floor at 29.3 m, about 37 m to the mast top.

Space: (right, forward, up) metres, origin on the ground at the middle of the office block's front; everything lies
behind it and the front (forward) faces the airfield.
"""
import tower
from kit import Part
from tower import anemometer, antenna, band, belt, cab, chamfer_rect, glazed_band, offset, railing, slab, window

NAME = "TowerMedium"
DISPLAY_NAME = "Control tower (medium)"
KIND = "Tower"
AO_DISTANCE = 1.6
AO_STRENGTH = 0.55
BAKE_SIZE = 4096
LOD_RATIOS = (0.35, 0.1)
PREVIEW_GROUND = (0.3, 0.3, 0.31)

MATS = dict(tower.MATS, **{
    "Concrete": ("#CDCBC4", 0.0, 0.85, False),
    "Plinth": ("#5B5E61", 0.0, 0.85, False),
    "Panel": ("#E2E4E3", 0.3, 0.5, False),
    "Cladding": ("#6F767C", 0.3, 0.5, False),
    "Accent": ("#2F5E8C", 0.2, 0.45, False),
})
GRIME = tower.GRIME
grime = tower.grime

# office block
WIDTH, DEPTH = 17.0, 11.0
PLINTH, FIRST, ROOF, PARAPET = 0.45, 3.6, 7.2, 7.75
# shaft and head, centred over (SX, SY)
SX, SY = 4.5, -5.5
SHAFT = chamfer_rect(6.0, 6.0, 1.2, SX, SY)
HEAD = chamfer_rect(8.6, 8.6, 2.2, SX, SY)
CAB = chamfer_rect(7.6, 7.6, 2.0, SX, SY)
Z_FLARE, Z_HEAD, Z_DECK = 24.0, 25.6, 29.0
Z_FLOOR, Z_GLASS, Z_TOP = 29.3, 30.4, 33.4
Z_ROOF = Z_TOP + 0.75 + 0.04     # top of the cab roof deck (cab() roof_h 0.75, deck 4 cm)
BEACON = (SX - 2.0, SY + 1.2, Z_ROOF + 1.15)
MAST = (SX + 1.6, SY - 1.5, 3.6)

WHEELS = []
MOVERS = []
SPINNERS = []
WORK = {}

LIGHTS = [
    ("Beacon_White", "Beacon", BEACON, (0, 0, 1), "#FFFFFF", 1.4, 0.0),
    ("Beacon_Green", "Beacon", BEACON, (0, 0, 1), "#30FF70", 1.4, 0.5),
    ("Obstruction", "Marker", (MAST[0], MAST[1], Z_ROOF + MAST[2] + 0.12), (0, 0, 1), "#FF2A1A", 1.0, 0.0),
    ("Obstruction_Head_L", "Marker", (SX - 4.3 - 0.4, SY, Z_HEAD + 0.45), (-1, 0, 0), "#FF2A1A", 0.9, 0.0),
    ("Obstruction_Head_R", "Marker", (SX + 4.3 + 0.4, SY, Z_HEAD + 0.45), (1, 0, 0), "#FF2A1A", 0.9, 0.0),
    ("Flood_Entrance", "Flood", (-5.0, 0.9, 2.85), (0, 0.6, -1), "#FFF1D8", 1.0, 0.0),
    ("Flood_Front", "Flood", (-2.0, 0.45, 6.9), (0, 0.6, -1), "#FFF1D8", 1.0, 0.0),
]

VIEWS = {
    "front34": ((-42.0, 46.0, 26.0), (2.0, -5.5, 15.0), 35),
    "rear34": ((46.0, -60.0, 30.0), (2.0, -5.5, 15.0), 35),
    "head": ((-12.0, 14.0, 34.0), (SX, SY, 30.0), 35),
    "base": ((-20.0, 22.0, 6.0), (0.0, -5.0, 4.5), 35),
    "overview": ((-90.0, 100.0, 75.0), (0.0, -5.5, 12.0), 35),
}


def build():
    b = Part("Body")
    office(b)
    shaft(b)
    head(b)
    tower.lamps(b, LIGHTS)
    return [b]


def office(b):
    hw = WIDTH / 2
    b.box((-hw - 0.05, -DEPTH - 0.05, 0.0), (hw + 0.05, 0.05, PLINTH), "Plinth")
    b.box((-hw, -DEPTH, PLINTH), (hw, 0.0, ROOF), "Cladding")
    # parapet with coping, the roof inside it
    t = 0.22
    for (x0, y0), (x1, y1) in (((-hw, -t), (hw, 0.0)), ((-hw, -DEPTH), (hw, -DEPTH + t)),
                               ((-hw, -DEPTH + t), (-hw + t, -t)), ((hw - t, -DEPTH + t), (hw, -t))):
        b.box((x0, y0, ROOF), (x1, y1, PARAPET), "Cladding")
        b.box((x0 - 0.04, y0 - 0.04, PARAPET), (x1 + 0.04, y1 + 0.04, PARAPET + 0.07), "Rail")
    b.box((-hw + t, -DEPTH + t, ROOF), (hw - t, -t, ROOF + 0.03), "RoofDeck")
    # a blue band under the coping, a pale band at the first floor
    belt(b, -hw, -DEPTH, hw, 0.0, PARAPET - 0.55, PARAPET - 0.1, "Accent")
    belt(b, -hw, -DEPTH, hw, 0.0, FIRST - 0.1, FIRST + 0.1, "Panel")
    front, back = ("y", 0.0, 1), ("y", -DEPTH, -1)
    left, right = ("x", -hw, -1), ("x", hw, 1)
    # entrance: a glazed door pair under a canopy, ribbons of windows on both floors
    window(b, front, -5.0, PLINTH, 2.2, 2.45, bars=1)
    b.box((-6.6, 0.0, 3.0), (-3.4, 1.6, 3.15), "Frame")
    for x in (-7.3, -1.9, 0.4, 2.7, 5.0, 7.3):
        if x > -2.5:
            window(b, front, x, 1.0, 1.9, 1.7, bars=1)
    for x in (-6.75, -3.65, -0.55, 2.55, 5.65):
        window(b, front, x, 4.4, 2.9, 1.8, bars=2)
    for x in (-6.75, -3.65, -0.55, 6.5):
        window(b, back, x, 1.0, 2.2, 1.6, bars=1)
        window(b, back, x, 4.4, 2.2, 1.8, bars=1)
    window(b, back, 3.0, PLINTH, 1.1, 2.3)
    for y in (-2.7, -8.3):
        for z in (1.0, 4.4):
            window(b, left, y, z, 2.4, 1.7, bars=1)
            window(b, right, y, z, 2.4, 1.7, bars=1)
    # roof plant: two condensers and a louvred plant box
    for x in (-6.5, -4.3):
        b.box((x - 0.8, -9.6, ROOF + 0.03), (x + 0.8, -8.6, ROOF + 1.0), "Plant", bevel=0.03)
        b.cyl((x, -9.1, ROOF + 1.0), (x, -9.1, ROOF + 1.03), 0.38, "Dark", segs=16)
    b.box((-6.8, -6.0, ROOF + 0.03), (-3.6, -3.8, ROOF + 1.6), "Plant", bevel=0.03)
    for k in range(5):
        b.box((-6.82, -5.8, ROOF + 0.3 + k * 0.22), (-6.79, -4.0, ROOF + 0.4 + k * 0.22), "Dark")
    for x in (-hw + 0.3, hw - 0.3):
        b.cyl((x, -DEPTH - 0.09, 0.1), (x, -DEPTH - 0.09, PARAPET), 0.06, "Rail", segs=8)


def shaft(b):
    """The square shaft from the ground (through the office block) to the flare under the head, with a glazed stair
    slot up its front, small windows on the sides and a service door at the back."""
    slab(b, SHAFT, 0.0, Z_FLARE, "Concrete")
    slab(b, offset(SHAFT, 0.06), 0.0, PLINTH, "Plinth")
    # the stair slot: framed glass in panels between floors, from the office roof to the head
    x0, x1, y = SX - 1.0, SX + 1.0, SY + 3.0
    b.box((x0 - 0.08, y - 0.05, PARAPET + 0.3), (x1 + 0.08, y + 0.1, Z_FLARE - 0.6), "Frame")
    b.box((x0, y + 0.05, PARAPET + 0.38), (x1, y + 0.12, Z_FLARE - 0.68), "CabGlass")
    z = PARAPET + 0.38
    while z < Z_FLARE - 0.7:
        b.box((x0, y + 0.08, z - 0.04), (x1, y + 0.16, z + 0.04), "Frame")
        z += 1.6
    b.box((SX - 0.03, y + 0.08, PARAPET + 0.38), (SX + 0.03, y + 0.16, Z_FLARE - 0.68), "Frame")
    # side windows (one per floor, staggered), a reveal line every 4 floors
    for k, z in enumerate((10.0, 13.4, 16.8, 20.2)):
        window(b, ("x", SX - 3.0, -1), SY + (0.8 if k % 2 else -0.8), z, 0.9, 1.3)
        window(b, ("x", SX + 3.0, 1), SY + (-0.8 if k % 2 else 0.8), z, 0.9, 1.3)
    for z in (12.0, 18.6):
        slab(b, offset(SHAFT, 0.03), z, z + 0.08, "Panel")
    # cable tray up the back
    b.box((SX - 0.35, SY - 3.0 - 0.3, PARAPET), (SX + 0.35, SY - 3.0, Z_FLARE), "Galv")


def head(b):
    """Flare from the shaft out to the equipment floor, its window ribbon, the catwalk and the cab."""
    band(b, SHAFT, Z_FLARE, HEAD, Z_HEAD, "Panel")
    slab(b, HEAD, Z_HEAD, Z_HEAD + 0.9, "Panel")
    glazed_band(b, HEAD, Z_HEAD + 0.9, Z_HEAD + 2.3, 1.5)
    slab(b, HEAD, Z_HEAD + 2.3, Z_DECK, "Panel")
    slab(b, offset(HEAD, 0.02), Z_HEAD + 2.45, Z_HEAD + 2.6, "Accent")       # blue line
    # catwalk: a deck out past the head, railing round it
    deck = offset(HEAD, 0.35)
    slab(b, deck, Z_DECK, Z_FLOOR, "Galv")
    railing(b, offset(deck, -0.08), Z_FLOOR, h=1.1, step=1.4)
    roof, z_cap = cab(b, CAB, Z_FLOOR, Z_GLASS, Z_TOP, sill="Panel", over=0.5, roof_h=0.75, step=2.2)
    railing(b, offset(roof, -0.15), z_cap - 0.04, h=1.0, step=1.8)
    # roof: the beacon on a stand, antennas, wind mast, obstruction light mast, a hatch
    bx, by, bz = BEACON
    b.box((bx - 0.5, by - 0.5, Z_ROOF), (bx + 0.5, by + 0.5, Z_ROOF + 0.3), "Galv")
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.cyl((bx + sx * 0.4, by + sy * 0.4, Z_ROOF + 0.3), (bx + sx * 0.15, by + sy * 0.15, bz - 0.7), 0.04,
                  "Galv", segs=6)
    antenna(b, MAST[0], MAST[1], Z_ROOF, MAST[2], r=0.05)
    for x, y, h in ((SX + 2.6, SY + 2.0, 2.4), (SX + 2.9, SY + 0.4, 1.8), (SX - 2.7, SY - 2.0, 2.6),
                    (SX - 0.4, SY - 2.8, 1.6)):
        antenna(b, x, y, Z_ROOF, h)
    anemometer(b, SX + 0.2, SY + 2.6, Z_ROOF, 2.4)
    b.box((SX - 1.4, SY - 0.6, Z_ROOF), (SX - 0.4, SY + 0.4, Z_ROOF + 0.35), "Plant", bevel=0.03)
