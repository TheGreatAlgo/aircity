"""Main-wing service skin, sampled from the airfoil definitions used to generate the model.

The right wing is stored in Unity metres (+X right, +Y up, +Z forward). Mirroring X supplies the left wing.
This avoids runtime mesh sampling and includes sweep, dihedral, incidence and airfoil camber.
"""
import numpy as np
from geom import airfoil_eval


def sections(model):
    parts = model.parts
    root = getattr(parts, "WING_SIDE", getattr(parts, "WING_ROOT_Y", None))
    if root is None or not hasattr(parts, "wing_station"):
        return []
    airfoil = airfoil_eval(parts.WING_AF)
    result = []
    for span in np.linspace(root, parts.WING_TIP, 17):
        station = parts.wing_station(-float(span), h=None)

        def point(fraction):
            upper, _, _ = airfoil(np.array([fraction]))
            p = (station["le"] + station["chord_dir"] * (station["c"] * fraction) +
                 station["up"] * (float(upper[0]) * station["c"] * station.get("scale_t", 1.0)))
            return [round(float(-p.x), 4), round(float(p.z), 4), round(float(-p.y), 4)]

        result.append(dict(span=round(float(span), 4), leading=point(.015), middle=point(.5), trailing=point(.985)))
    return result
