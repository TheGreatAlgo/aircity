"""Four independent, life-size reusable baggage meshes, with their origins at ground contact."""
import baggage as g
from kit import Part

NAME, DISPLAY_NAME, KIND = "BaggageProps", "Baggage prop set", "BaggageProp"
MATS = g.MATS
AO_DISTANCE, AO_STRENGTH = 0.15, 0.4
LOD_RATIOS = None
WHEELS, SPINNERS, LIGHTS = [], [], []
MOVERS = ["Bag_HardSmall", "Bag_Soft", "Bag_Duffel"]


def build():
    specs = [("Bag_HardLarge", -0.85, (0.48, 0.29, 0.74), "BagBlue", False),
             ("Bag_HardSmall", -0.22, (0.36, 0.24, 0.54), "BagRed", False),
             ("Bag_Soft", 0.34, (0.44, 0.29, 0.67), "BagTan", True)]
    out = []
    for name, x, size, mat, soft in specs:
        p = Part(name, (x, 0, 0))
        g.suitcase(p, (x, 0, 0), size, mat, soft=soft)
        out.append(p)
    p = Part("Bag_Duffel", (1.02, 0, 0))
    g.duffel(p, (1.02, 0, 0))
    out.append(p)
    return out


VIEWS = {"front34": ((-2.5, 3.6, 2.0), (0.1, 0, 0.34), 48)}
