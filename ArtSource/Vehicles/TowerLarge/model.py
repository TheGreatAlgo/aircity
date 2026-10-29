"""Large control tower for a hub airport, after Schiphol's (101 m): a round concrete shaft tapering out of a
three-storey ATC centre, a wide two-tier head (a flare out to the equipment floors with a ribbon of glass, a terrace
round a dark neck), a twelve-sided cab with glass leaning out, and a radar dome on the cab roof. A glazed lift slot
runs up the shaft's front and a service riser up its back. Red obstruction lights at the top, round the head and
half way up the shaft. Cab floor at 86 m, about 99 m to the top light.

Space: (right, forward, up) metres, origin on the ground at the middle of the ATC centre's front; everything lies
behind it and the front (forward) faces the airfield.
"""
import math

import tower
from kit import Part, rounded_rect
from tower import antenna, band, cab, glazed_band, ngon, offset, railing, slab

NAME = "TowerLarge"
DISPLAY_NAME = "Control tower (large)"
KIND = "Tower"
AO_DISTANCE = 2.5
AO_STRENGTH = 0.55
BAKE_SIZE = 4096
LOD_RATIOS = (0.35, 0.1)
PREVIEW_GROUND = (0.3, 0.3, 0.31)

MATS = dict(tower.MATS, **{
    "Concrete": ("#D2D1CC", 0.0, 0.85, False),
    "Plinth": ("#5B5E61", 0.0, 0.85, False),
    "Panel": ("#D9DCDD", 0.4, 0.45, False),
    "Cladding": ("#3A4046", 0.4, 0.45, False),
    "Radome": ("#EEEEEA", 0.0, 0.6, False),
})
GRIME = tower.GRIME
grime = tower.grime

# ATC centre
WIDTH, DEPTH, CORNER = 46.0, 26.0, 3.0
PLINTH, STOREY, FLOORS = 0.5, 4.2, 3
ROOF = PLINTH + STOREY * FLOORS
PARAPET = ROOF + 0.6
# shaft and head, round, centred on (SX, SY)
SX, SY = 11.0, -13.0
N = 24
R_FOOT, R_NECK = 5.4, 4.4                      # shaft radius at the podium roof / under the head
Z_SHAFT = 70.0
R_HEAD, R_TERRACE, R_NECK_RING, R_CAB = 10.5, 9.4, 6.4, 7.4
Z_HEAD, Z_TERRACE = 77.0, 84.3
Z_FLOOR, Z_GLASS, Z_TOP = 86.0, 87.0, 90.8
Z_CAP = Z_TOP + 0.9
DOME_R, DOME_Z = 2.5, Z_CAP + 1.4 + 2.5 * 0.55   # the dome's centre (it sits on a drum, a bit over half showing)
TOP = DOME_Z + DOME_R + 2.2


def r_shaft(z):
    return R_FOOT + (R_NECK - R_FOOT) * (z - ROOF) / (Z_SHAFT - ROOF)


WHEELS = []
MOVERS = []
SPINNERS = []
WORK = {}

Z_MID = 44.0
LIGHTS = [("Obstruction_Top", "Marker", (SX, SY, TOP + 0.12), (0, 0, 1), "#FF2A1A", 1.3, 0.0)] + [
    (f"Obstruction_Head_{k}", "Marker", (SX + math.cos(a) * (R_HEAD + 0.4), SY + math.sin(a) * (R_HEAD + 0.4),
                                         Z_HEAD + 0.9), (math.cos(a), math.sin(a), 0), "#FF2A1A", 1.1, 0.0)
    for k, a in enumerate(math.radians(45 + 90 * i) for i in range(4))] + [
    (f"Obstruction_Mid_{k}", "Marker", (SX + math.cos(a) * (r_shaft(Z_MID) + 0.4), SY + math.sin(a) * (r_shaft(Z_MID) + 0.4),
                                        Z_MID), (math.cos(a), math.sin(a), 0), "#FF2A1A", 1.0, 0.0)
    for k, a in enumerate(math.radians(45 + 90 * i) for i in range(4))] + [
    ("Flood_Entrance_L", "Flood", (-4.0, 3.6, PLINTH + 7.85), (0, 0.6, -1), "#FFF1D8", 1.0, 0.0),
    ("Flood_Entrance_R", "Flood", (4.0, 3.6, PLINTH + 7.85), (0, 0.6, -1), "#FFF1D8", 1.0, 0.0),
]

VIEWS = {
    "front34": ((-125.0, 145.0, 75.0), (6.0, -13.0, 48.0), 35),
    "rear34": ((140.0, -170.0, 80.0), (6.0, -13.0, 48.0), 35),
    "head": ((-18.0, 22.0, 96.0), (SX, SY, 86.0), 35),
    "base": ((-48.0, 52.0, 14.0), (0.0, -13.0, 8.0), 35),
    "overview": ((-200.0, 230.0, 150.0), (6.0, -13.0, 30.0), 35),
}


def build():
    b = Part("Body")
    podium(b)
    shaft(b)
    head(b)
    tower.lamps(b, LIGHTS)
    return [b]


def podium(b):
    """The ATC centre: a rounded block on a dark plinth, a spandrel and a ribbon of glass per storey, a parapet,
    a glazed double-height entrance under a canopy, plant on the roof."""
    out = rounded_rect(WIDTH, DEPTH, CORNER, n=4, cy=-DEPTH / 2)
    slab(b, offset(out, 0.06), 0.0, PLINTH, "Plinth")
    z = PLINTH
    for k in range(FLOORS):
        slab(b, out, z, z + 1.1, "Panel")
        glazed_band(b, out, z + 1.1, z + STOREY, 1.8)
        z += STOREY
    slab(b, out, ROOF, PARAPET, "Panel")
    slab(b, offset(out, 0.05), PARAPET, PARAPET + 0.08, "Rail")
    slab(b, offset(out, -0.3), PARAPET - 0.5, PARAPET + 0.01, "RoofDeck")
    # entrance: a glazed box out in front, two storeys high, under a deep canopy on columns
    b.box((-7.0, -0.2, PLINTH), (7.0, 2.4, PLINTH + 8.0), "CabGlass")
    for x in [-7.0 + 1.75 * i for i in range(9)]:
        b.box((x - 0.06, 2.35, PLINTH), (x + 0.06, 2.5, PLINTH + 8.0), "Frame")
    for z in (PLINTH + 2.8, PLINTH + 4.2 + 1.1, PLINTH + 8.0):
        b.box((-7.05, 2.35, z - 0.08), (7.05, 2.5, z + 0.08), "Frame")
    b.box((-9.0, -0.5, PLINTH + 8.0), (9.0, 5.2, PLINTH + 8.6), "Panel")
    b.box((-9.02, 5.0, PLINTH + 8.0), (9.02, 5.22, PLINTH + 8.6), "Cladding")
    for x in (-8.4, 8.4):
        b.cyl((x, 4.6, PLINTH), (x, 4.6, PLINTH + 8.0), 0.22, "Panel", segs=16)
    b.box((-9.0, 2.4, 0.0), (9.0, 5.6, PLINTH), "Plinth")
    # roof plant: a row of condensers, two louvred plant rooms, a lift overrun
    for x in (-17.0, -14.5, -12.0, -9.5):
        b.box((x - 1.0, -22.5, PARAPET - 0.5), (x + 1.0, -21.1, PARAPET + 0.7), "Plant", bevel=0.03)
        b.cyl((x, -21.8, PARAPET + 0.7), (x, -21.8, PARAPET + 0.73), 0.5, "Dark", segs=16)
    for x0, x1, y0, y1 in ((-19.0, -12.0, -17.5, -12.5), (-4.0, 2.0, -21.0, -16.0)):
        b.box((x0, y0, PARAPET - 0.5), (x1, y1, PARAPET + 2.6), "Cladding", bevel=0.04)
        for k in range(7):
            b.box((x0 + 0.6, y1 - 0.02, PARAPET + 0.4 + k * 0.28), (x1 - 0.6, y1 + 0.04, PARAPET + 0.52 + k * 0.28), "Dark")
    railing(b, offset(rounded_rect(WIDTH, DEPTH, CORNER, n=4, cy=-DEPTH / 2), -0.3), PARAPET + 0.08, h=0.6, step=3.0)


def strip(b, angle, z0, z1, w, proud, mat, inset=0.25):
    """A flat strip up the tapering shaft, centred on bearing `angle` (degrees, 90 = forward), w wide, from inset
    into the shaft to `proud` out of it, following the taper."""
    a = math.radians(angle)
    u, n = (-math.sin(a), math.cos(a)), (math.cos(a), math.sin(a))

    def quad(z):
        r = r_shaft(z)
        pts = []
        for s, d in ((-1, r - inset), (1, r - inset), (1, r + proud), (-1, r + proud)):
            pts.append((SX + n[0] * d + u[0] * s * w / 2, SY + n[1] * d + u[1] * s * w / 2, z))
        return pts

    b.loft([quad(z0), quad(z1)], mat)


def shaft(b):
    band(b, ngon(N, R_FOOT * 1.04, SX, SY), 0.0, ngon(N, R_FOOT * 1.04, SX, SY), PLINTH, "Plinth")
    band(b, ngon(N, R_FOOT, SX, SY), PLINTH, ngon(N, R_FOOT, SX, SY), ROOF, "Concrete")
    band(b, ngon(N, R_FOOT, SX, SY), ROOF, ngon(N, R_NECK, SX, SY), Z_SHAFT, "Concrete")
    # glazed lift slot up the front (framed, a transom every storey), service riser up the back
    z0, z1 = PARAPET + 0.5, Z_SHAFT - 1.0
    strip(b, 90, z0 - 0.15, z1 + 0.15, 2.3, 0.12, "Frame")
    strip(b, 90, z0, z1, 2.0, 0.16, "CabGlass")
    z = z0 + 3.6
    while z < z1 - 1.0:
        strip(b, 90, z - 0.06, z + 0.06, 2.1, 0.2, "Frame")
        z += 3.6
    strip(b, 270, ROOF, Z_HEAD + 0.5, 0.8, 0.45, "Galv")
    # thin reveal lines round the shaft every 12 m, as formwork joints
    for z in (24.0, 36.0, 48.0, 60.0):
        band(b, ngon(N, r_shaft(z) + 0.03, SX, SY), z, ngon(N, r_shaft(z + 0.12) + 0.03, SX, SY), z + 0.12, "Panel")


def head(b):
    """Flare out to the equipment floors, their glass ribbon, the terrace and neck, the cab, the radar dome."""
    flare = ngon(N, R_NECK, SX, SY)
    head = ngon(N, R_HEAD, SX, SY)
    band(b, flare, Z_SHAFT, head, Z_HEAD, "Panel")
    slab(b, head, Z_HEAD, Z_HEAD + 1.6, "Panel")
    glazed_band(b, head, Z_HEAD + 1.6, Z_HEAD + 4.4, 1.6)
    slab(b, head, Z_HEAD + 4.4, Z_HEAD + 5.9, "Panel")
    slab(b, offset(head, 0.03), Z_HEAD + 5.1, Z_HEAD + 5.25, "Cladding")                # dark line
    terrace = ngon(N, R_TERRACE, SX, SY)
    band(b, head, Z_HEAD + 5.9, terrace, Z_TERRACE, "Panel")
    slab(b, offset(terrace, -0.05), Z_TERRACE, Z_TERRACE + 0.08, "RoofDeck")
    railing(b, offset(terrace, -0.25), Z_TERRACE + 0.08, h=1.1, step=1.6)
    slab(b, ngon(N, R_NECK_RING, SX, SY), Z_TERRACE, Z_FLOOR, "Cladding")
    cab_base = ngon(12, R_CAB, SX, SY)
    roof, z_cap = cab(b, cab_base, Z_FLOOR, Z_GLASS, Z_TOP, sill="Panel", over=0.7, roof_h=0.9, step=2.2,
                      mullion=0.07)
    # radar dome on a drum, a lightning mast on top, antennas round the roof edge
    b.cyl((SX, SY, z_cap), (SX, SY, z_cap + 1.4), 1.9, "Panel", segs=24)
    b.cyl((SX, SY, z_cap + 1.4), (SX, SY, z_cap + 1.55), 2.1, "Frame", segs=24)
    b.sphere((SX, SY, DOME_Z), DOME_R, "Radome", segs=32, rings=16)
    b.cyl((SX, SY, DOME_Z + DOME_R - 0.05), (SX, SY, TOP - 0.3), 0.05, "Rail", segs=8, r2=0.03)
    for k in range(6):
        a = math.radians(15 + 60 * k)
        x, y = SX + math.cos(a) * (R_CAB + 0.2), SY + math.sin(a) * (R_CAB + 0.2)
        antenna(b, x, y, z_cap + 0.04, 1.6 + 0.6 * (k % 3))
    railing(b, offset(roof, -0.2), z_cap + 0.04, h=1.0, step=2.0)
