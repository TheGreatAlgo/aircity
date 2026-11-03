"""Generic aluminium AKE/LD3. Contour traced from Granger's 61.5/79/64 inch dimension drawing.
Base: 1.562 x 1.534 m. The sloping outer panel overhangs the base; this is not a rectangular box.
"""
import bpy
import baggage as g
from kit import Part

NAME, DISPLAY_NAME, KIND = "ContainerLD3", "AKE / LD3 container", "ContainerULD"
MATS = {**g.MATS, "Aluminium": ("#C4C8C8", .8, .47, False)}
GRIME, grime = g.GRIME, g.grime
AO_DISTANCE, AO_STRENGTH = .18, .35
LOD_RATIOS = (.32, .09)
SEPARATE_MOVERS, MOVING_LODS = True, True
MOVERS = ["Curtain", "CurtainRoll", "BagLoad"]
CONTAINER = {"code": "AKE", "baseWidth": 1.562, "baseLength": 1.534, "height": 1.626,
             "tare": 95., "maxGross": 1588., "transfer": (0, 0, 0)}


def build():
    p = Part("Shell")
    p.box((-.781, -.767, 0), (.781, .767, .038), "Zinc", bevel=.008)
    # Thin closed shell panels, with an open inboard loading face at x=-0.781.
    contour = [(-.775, .04), (.775, .04), (1.2256, .56), (1.2256, 1.626), (-.775, 1.626)]
    for f in (-.755, .755):
        p.prism(contour, "f", f - .008, .016, "Aluminium")
    p.box((-.775, -.755, 1.602), (1.2256, .755, 1.626), "Aluminium", bevel=.009)
    p.box((1.21, -.755, .56), (1.2256, .755, 1.61), "Aluminium", bevel=.008)
    # Sloping outboard panel, extruded across the depth rather than a solid wedge.
    p.prism([(.775, .038), (.793, .038), (1.2256, .56), (1.209, .574)], "f", -.755, 1.51, "Aluminium")
    for f in (-.763, .763):
        for a, b in zip(contour, contour[1:] + contour[:1]):
            p.cyl((a[0], f, a[1]), (b[0], f, b[1]), .015, "Zinc", segs=8)
        for x in (-.72, -.1, .70):
            p.cyl((x, f, .08), (x, f, 1.59), .011, "Zinc", segs=8)
        for z in (.55, 1.10):
            p.cyl((-.74, f, z), (1.20 if z > .55 else .78, f, z), .012, "Zinc", segs=8)
    for f in (-.758, .758):
        p.box((-.795, f - .02, .03), (-.765, f + .02, 1.626), "Zinc", bevel=.006)
    g.label(p, "AKE 001", (1.232, 0, 1.20), .115, 1, "PaintDark")
    g.label(p, "MAX GROSS 1588 KG", (1.233, 0, .86), .055, 1, "PaintDark")
    curtain = Part("Curtain", (-.796, 0, 1.59))
    curtain.box((-.80, -.715, .065), (-.783, .715, 1.59), "Curtain", bevel=.008)
    for f in (-.52, 0, .52):
        curtain.box((-.808, f - .015, .07), (-.80, f + .015, 1.58), "Webbing")
        for z in (.24, .80, 1.32):
            curtain.box((-.82, f - .027, z), (-.807, f + .027, z + .06), "Zinc", bevel=.004)
    roll = Part("CurtainRoll", (-.79, 0, 1.59))
    roll.cyl((-.79, -.73, 1.59), (-.79, .73, 1.59), .035, "Curtain", segs=16)
    bags = Part("BagLoad")
    for x in (-.49, -.04, .40):
        for f in (-.48, 0, .48):
            g.suitcase(bags, (x, f, .045), (.34, .26, .65), "BagBlue" if f else "BagTan")
    return [p, curtain, roll, bags]


def preview_pose(view):
    bpy.data.objects["Curtain"].scale.z = .035 if view == "open" else 1
    bpy.data.objects["BagLoad"].hide_render = view != "open"


VIEWS = {"front34": ((-3.6, 3.2, 2.5), (.12, 0, .80), 48),
         "contour": ((3.8, -3.4, 2.7), (.2, 0, .8), 48),
         "open": ((-3.6, 2.8, 2.4), (0, 0, .8), 48)}
