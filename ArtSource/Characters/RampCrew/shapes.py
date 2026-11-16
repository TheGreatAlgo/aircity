"""The ramp worker as signed distance fields: anatomy, clothing, PPE, paint, relief and skin weights.

Metres, Blender +X = character's right, +Y forward, +Z up; the rest pose is a T-pose. Proportions follow
adult male anthropometry (ANSUR II means, scaled to 1.78 m stature) rather than eyeballed shapes:
  vertex 1.78, eye 1.656, subnasale 1.610, stomion 1.587, menton 1.543 (head height 0.237, face 0.12),
  head breadth 0.152, head length 0.196, bizygomatic 0.136, bigonial 0.112, interpupillary 0.064,
  nose breadth 0.035, mouth width 0.050, neck circumference 0.38, biacromial 0.41, bideltoid 0.50,
  chest/waist/hip breadth 0.33/0.31/0.36, upper arm 0.33 circumference, wrist 0.18, thigh 0.60, calf 0.39,
  hand length 0.19 and breadth 0.088, foot 0.27 (boot 0.31).
Each part is meshed separately; garments of one part share a single closed surface so they cannot
separate when skinned. Paint is evaluated per texel on the true surface when baking the atlas.
"""
import math

import numpy as np

from sdf import (blend, capsule, ellipsoid, gradient, intersect, loft, loft2, norm, offset, round_box, round_cone,
                 smax, smin, sphere, subtract)

# ------------------------------------------------------------------ helpers

def ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def mirrored(f):
    """Evaluate a right-side definition for both sides (the character is symmetric)."""
    def g(p):
        q = p.copy(); q[:, 0] = np.abs(q[:, 0])
        return f(q)
    return g


def rot_x(deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def rot_y(deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def rot_z(deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def _hash(ix, iy, iz, seed):
    h = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791) ^ (seed * 2654435761)
    h = h & 0xffffffff
    h = ((h ^ (h >> 13)) * 1274126177) & 0xffffffff
    h = h ^ (h >> 16)
    return (h & 0xffff) / 65535.0


def noise(p, scale, seed=1):
    """Smooth value noise in [-1, 1]; deterministic, no external state."""
    q = p * scale
    i = np.floor(q).astype(np.int64); f = q - i
    f = f * f * (3 - 2 * f)
    out = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                out = out + w * _hash(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz, seed)
    return out * 2 - 1


def fbm(p, scale, octaves=3, seed=1):
    total, amp, norm_ = 0, 1, 0
    for o in range(octaves):
        total = total + amp * noise(p, scale * 2 ** o, seed + o)
        norm_ += amp; amp *= .5
    return total / norm_


def hexrgb(h):
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], float) / 255


def mix(a, b, t):
    t = np.asarray(t)[..., None] if np.ndim(t) else t
    return a * (1 - t) + b * t


# Skin tones: base, lips, hair, iris. Same uniform for every tone.
TONES = [
    dict(skin="b98a6c", lips="9c5f55", hair="2b211c", iris="4a3222"),
    dict(skin="7c5541", lips="64392f", hair="17130f", iris="2e1f16"),
    dict(skin="e3b9a0", lips="c07f74", hair="5a4030", iris="5d6a6e"),
    dict(skin="a37653", lips="85503f", hair="231b16", iris="3f2c1d"),
]
COL = dict(shirt="2b3747", trousers="252f3c", patch="1f2832", vest="c9d63a", binding="aab52c",
           tape="b7bcb8", zip="2c2f31", belt="2a2b2b", metal="8c908d", glove="9aa09c", coating="3a3f40",
           cuff="2f3438", boot="34302c", toecap="2b2826", sole="1c1c1c", lace="1f1f20", cap="27313f",
           cup="32373d", cushion="1b1d20", band="2a2d31", sclera="e2ddd3", pupil="0b0b0b", seam="1c232d")
CLASSES = ["shirt", "trousers", "vest", "tape", "belt", "skin", "lips", "hair", "glove", "coating",
           "boot", "sole", "cap", "cup", "sclera", "iris", "zip", "patch", "metal", "cushion"]
CLS = {name: i for i, name in enumerate(CLASSES)}
# Smoothness (URP, multiplied into the MetalGloss alpha) and metallic per class.
GLOSS = dict(shirt=.20, trousers=.18, patch=.16, vest=.30, tape=.55, belt=.40, skin=.38, lips=.50, hair=.18,
             glove=.14, coating=.42, boot=.40, sole=.12, cap=.18, cup=.55, sclera=.85, iris=.85, zip=.45,
             metal=.60, cushion=.25)
METAL = dict(tape=.12, metal=.85, zip=.2)


class Paint:
    """Accumulates per-point colour, class, smoothness and metallic."""
    def __init__(self, n):
        self.rgb = np.zeros((n, 3)); self.cls = np.zeros(n, int)
        self.gloss = np.zeros(n); self.metal = np.zeros(n)

    def set(self, mask, name, rgb=None, gloss=None):
        mask = np.asarray(mask, bool)
        if not mask.any():
            return
        self.cls[mask] = CLS[name] if name in CLS else self.cls[mask]
        self.rgb[mask] = hexrgb(COL[name]) if rgb is None else (rgb[mask] if np.ndim(rgb) == 2 else rgb)
        self.gloss[mask] = GLOSS.get(name, .25) if gloss is None else gloss
        self.metal[mask] = METAL.get(name, 0)

    def shade(self, factor, mask=None):
        f = np.asarray(factor, float)
        if f.ndim == 0:
            f = np.full(len(self.rgb), float(f))
        if mask is not None:
            f = np.where(mask, f, 1)
        self.rgb *= f[:, None]


# ------------------------------------------------------------------ joints (fixed contract + additions)
JOINTS = {"Hips": (0, 0, .94), "Spine": (0, 0, 1.10), "Chest": (0, 0, 1.32),
          "Neck": (0, 0, 1.49), "Head": (0, 0, 1.53)}
PARENTS = {"Hips": None, "Spine": "Hips", "Chest": "Spine", "Neck": "Chest", "Head": "Neck"}
TAILS = {}
for side, sign in (("L", -1), ("R", 1)):
    for name, position, parent in (
            ("Clavicle", (sign * .028, .045, 1.445), "Chest"),
            ("UpperArm", (sign * .205, 0, 1.40), "Clavicle_" + side),
            ("Forearm", (sign * .505, 0, 1.40), "UpperArm_" + side),
            ("Hand", (sign * .775, 0, 1.40), "Forearm_" + side),
            ("Fingers", (sign * .872, 0, 1.401), "Hand_" + side),
            ("Thumb", (sign * .792, .026, 1.390), "Hand_" + side),
            ("Thigh", (sign * .105, 0, .94), "Hips"),
            ("Shin", (sign * .105, .015, .52), "Thigh_" + side),
            ("Foot", (sign * .105, 0, .09), "Shin_" + side),
            ("Toe", (sign * .105, .18, .055), "Foot_" + side)):
        JOINTS[name + "_" + side] = position
        PARENTS[name + "_" + side] = parent
    TAILS["Fingers_" + side] = (sign * .950, 0, 1.385)
    TAILS["Thumb_" + side] = (sign * .842, .066, 1.372)
    TAILS["Toe_" + side] = (sign * .105, .25, .04)
    TAILS["Hand_" + side] = (sign * .872, 0, 1.401)
BONES = list(JOINTS)


# ================================================================== BODY (shirt, vest, belt, trousers, arms)
TORSO = [  # z, half depth (y), half breadth (x), y offset, superellipse exponent
    (0.80, .098, .148, -.014, 2.2), (0.86, .121, .175, -.017, 2.4), (0.92, .125, .180, -.012, 2.5),
    (0.98, .120, .170, -.004, 2.6), (1.05, .116, .158, .000, 2.6), (1.12, .119, .159, .004, 2.5),
    (1.20, .124, .164, .006, 2.5), (1.28, .126, .168, .006, 2.6), (1.35, .118, .166, .000, 2.6),
    (1.41, .102, .146, -.008, 2.4), (1.46, .078, .098, -.012, 2.2), (1.497, .066, .068, -.012, 2.0)]


def _torso_core():
    z0, z1 = TORSO[0][0], TORSO[-1][0]
    st = [((z - z0) / (z1 - z0), d, b, oy, 0, n) for z, d, b, oy, n in TORSO]
    return loft((0, 0, z0), (0, 0, z1), (0, 1, 0), st)


def _arm_loft(stations, x0, x1, caps=(True, True)):
    """Stations (x, half height z, half depth y, z offset, y offset, n) along +X at shoulder height."""
    st = [((x - x0) / (x1 - x0), rz, ry, oz, -oy, n) for x, rz, ry, oz, oy, n in stations]
    return loft((x0, 0, 1.40), (x1, 0, 1.40), (0, 0, 1), st, caps)


ARM = [  # exposed arm below the sleeve: x, half height, half depth, z offset, y offset, n
    (.25, .049, .051, .000, .000, 2), (.31, .047, .050, .001, .003, 2), (.38, .044, .047, .002, .004, 2),
    (.45, .040, .043, .001, .002, 2), (.505, .037, .040, .000, -.002, 2), (.55, .039, .045, .003, .002, 2),
    (.62, .033, .040, .002, .002, 2), (.70, .025, .033, .000, .001, 2.1), (.755, .020, .029, .000, .000, 2.2),
    (.772, .019, .028, .000, .000, 2.2)]
SLEEVE = [(.16, .057, .059, .000, .000, 2), (.25, .057, .059, .000, .001, 2), (.32, .055, .057, .000, .002, 2),
          (.362, .055, .058, -.001, .002, 2)]
LEG = [  # z, half depth (y), half breadth (x), y offset, x offset (outwards), n
    (.97, .108, .098, -.010, -.004, 2.2), (.86, .104, .099, -.004, .004, 2.2), (.76, .094, .089, .003, .003, 2.2),
    (.64, .082, .078, .008, .000, 2.1), (.54, .071, .068, .013, -.002, 2.1), (.48, .068, .065, .010, -.002, 2.1),
    (.41, .070, .065, -.004, .000, 2.1), (.33, .068, .062, -.006, .000, 2.0), (.25, .063, .058, -.003, .000, 2.0),
    (.18, .064, .060, .004, .000, 2.0), (.145, .066, .062, .006, .000, 2.0)]


class Body:
    name = "Body"
    lo, hi, h = (-.84, -.17, .13), (.84, .17, 1.56), .0035

    def __init__(self, far=False):
        """far=True includes the vest in the surface (far LOD); LOD0 meshes the vest as its own shell."""
        self.far = far
        core = _torso_core()
        R = lambda f: mirrored(f)
        self.pec = R(ellipsoid((.078, .070, 1.300), (.074, .045, .060)))
        self.scap = R(ellipsoid((.085, -.078, 1.330), (.074, .042, .085)))
        self.trap = R(round_cone((.035, -.030, 1.495), (.182, -.008, 1.438), .034, .030))
        self.glute = R(ellipsoid((.072, -.074, .885), (.080, .064, .085)))
        belly = ellipsoid((0, .052, 1.060), (.120, .070, .100))
        self.upper = blend(core, (self.pec, .03), (self.scap, .03), (self.trap, .028), (belly, .04))
        self.delt = R(ellipsoid((.200, .000, 1.405), (.057, .056, .055)))
        self.sleeve = R(_arm_loft(SLEEVE, .16, .362))
        shirt = blend(self.upper, (self.delt, .022), (self.sleeve, .02))
        # Skin of the arm (short sleeves): olecranon and wrist bones give the forearm its silhouette.
        arm = _arm_loft(ARM, .25, .772)
        olecranon = ellipsoid((.500, -.034, 1.400), (.016, .012, .016))
        ulna = ellipsoid((.770, -.020, 1.403), (.010, .008, .008))
        self.arm = R(blend(arm, (olecranon, .012), (ulna, .006)))
        # Trousers: legs blend into the seat; reinforced knee panels and cargo pockets are raised patches.
        leg_st = [((.97 - z) / (.97 - .145), d, b, oy, ox, n) for z, d, b, oy, ox, n in LEG]
        leg = loft((.105, 0, .97), (.105, 0, .145), (0, 1, 0), leg_st)
        knee_region = ellipsoid((.105, .075, .515), (.070, .060, .080))
        pocket_region = round_box((.205, .000, .705), (.030, .062, .072), .012)
        self.leg = R(leg)
        self.knee = R(lambda p: smax(offset(leg, .0026)(p), knee_region(p), .004))
        self.pocket = R(lambda p: smax(offset(leg, .0045)(p), pocket_region(p), .003))
        self.seat = blend(core, (self.glute, .03))
        trousers = blend(self.seat, (self.leg, .035))
        # Hi-vis vest: offset of the upper torso, cut by hem, armholes, neck and V front.
        armhole = R(ellipsoid((.215, -.006, 1.385), (.085, .105, .128)))
        def region(p):
            x, y, z = np.abs(p[:, 0]), p[:, 1], p[:, 2]
            hem = VEST_HEM - z
            hole = -armhole(p)
            neck = .088 - np.sqrt(x * x + ((y + .012) * 1.1) ** 2)
            vee = np.minimum((z - 1.25) * .5 - x, y - .02)
            return np.maximum.reduce([hem, hole, neck, vee])
        self.vest_region = region
        self.vest = intersect(offset(self.upper, .0068), region, .003)
        self.shirt = shirt
        self.trousers = trousers
        self.core = core

    def components(self, p):
        c = dict(shirt=self.shirt(p), arm=self.arm(p), trousers=self.trousers(p), knee=self.knee(p),
                 pocket=self.pocket(p))
        if self.far:
            c["vest"] = self.vest(p)
        return c

    def sdf(self, p):
        # Hard unions keep a readable step at sleeve hems, knee panels and pockets.
        return np.minimum.reduce(list(self.components(p).values()))

    def paint(self, p, n, tone):
        c = self.components(p)
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        ax = np.abs(x)
        out = Paint(len(p))
        names = list(c)
        stack = np.stack([c[k] for k in names], 1)
        # Core torso below the belt is trouser cloth; above it is the shirt.
        win = np.argmin(stack, 1)
        which = np.array(names)[win]
        torso_low = (which == "shirt") & (z < 1.0)
        which[torso_low] = "trousers"
        trouser_top = (which == "trousers") & (z > 1.0)
        which[trouser_top] = "shirt"
        cloth = fbm(p, 60, 2, 7)
        out.set(which == "shirt", "shirt"); out.set(which == "trousers", "trousers")
        out.set(which == "knee", "patch"); out.set(which == "pocket", "trousers")
        skin = hexrgb(TONES[tone]["skin"])
        tint = 1 + .035 * fbm(p, 40, 3, 11)
        out.set(which == "arm", "skin", np.clip(skin[None, :] * tint[:, None], 0, 1))
        # Far LOD only: the vest is part of this surface and is classified per face.
        vest = which == "vest"
        out.set(vest, "vest")
        out.set(vest & (vest_tape(p) > .5), "tape")
        # Stitch lines: sleeve hems, shoulder seams, side seams, trouser out-seams, pockets and knee panels.
        seam = np.zeros(len(p), bool)
        seam |= (which == "shirt") & (np.abs(ax - .352) < .0016)
        lx = ax - .105
        outer = np.arctan2(y, lx)
        seam |= (which == "trousers") & (z < .95) & (np.abs(outer) < .025)
        seam |= (which == "trousers") & (np.abs(z - .985) < .0016)
        seam |= (which == "pocket") & (np.abs(z - .745) < .0018)
        seam |= (which == "knee") & (np.abs(c["knee"] - c["trousers"]) < .0015)
        seam |= (which == "shirt") & (np.abs(z - 1.49) < .0018)
        out.shade(.72, seam)
        # Hi-vis trouser bands below the knee.
        bands = (which == "trousers") & ((np.abs(z - .235) < .0125) | (np.abs(z - .292) < .0125))
        out.set(bands, "tape")
        # Weave and gentle fold shading so flat cloth is not uniform.
        fabric = np.isin(out.cls, [CLS["shirt"], CLS["trousers"], CLS["patch"], CLS["vest"]])
        out.shade(1 + .05 * cloth, fabric)
        # Ambient occlusion from the field itself: creases and armpits read darker.
        out.shade(occlusion(self.sdf, p, n))
        return out

    def relief(self, p):
        """Small heights (m) for the normal map: cloth grain, folds at elbows/waist/knees and seams."""
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        ax = np.abs(x)
        h = .00012 * fbm(p, 380, 2, 21)
        folds = .0009 * np.sin(z * 260 + 3 * fbm(p, 9, 2, 5)) * np.exp(-((z - .52) / .05) ** 2) * (ax < .25)
        folds += .0007 * np.sin(z * 210 + 2 * fbm(p, 8, 2, 6)) * np.exp(-((z - 1.0) / .05) ** 2)
        folds += .0007 * np.sin(ax * 240 + 2 * fbm(p, 8, 2, 8)) * np.exp(-((ax - .30) / .05) ** 2) * (z > 1.3)
        folds += .0006 * np.sin(z * 120 + 4 * fbm(p, 6, 2, 9)) * ((z > .2) & (z < .9) & (ax < .25))
        return h + folds


# ================================================================== VEST (its own thin shell, so its outline is geometry)
VEST_HEM = .975                 # level hem, front and back
VEST_MID, VEST_HALF = .0062, .0022   # shell centre offset over the shirt and half thickness
HOOPS = (1.040, 1.140)          # reflective hoop centres (z); tape is 48 mm wide
TAPE_HALF = .024
BRACE_X = .118


def _aa(d, width=.0012):
    """Antialiased coverage of d <= 0 over about one texel (d in metres)."""
    return np.clip(.5 - d / width, 0, 1)


def vest_tape(p, aa=.0012):
    """Coverage of the retro-reflective tape: two level hoops and two braces over the shoulders."""
    ax, z = np.abs(p[:, 0]), p[:, 2]
    hoop = np.maximum(_aa(np.abs(z - HOOPS[0]) - TAPE_HALF, aa), _aa(np.abs(z - HOOPS[1]) - TAPE_HALF, aa))
    brace = _aa(np.abs(ax - BRACE_X) - TAPE_HALF, aa) * _aa(HOOPS[1] - z, aa)
    return np.maximum(hoop, brace)


class Vest:
    name = "Vest"
    lo, hi, h = (-.25, -.17, .94), (.25, .17, 1.53), .0012
    far_lod = False             # the far LOD folds the vest into the body surface

    def __init__(self):
        body = Body()
        self.upper = body.upper
        self.region = body.vest_region

    def edge(self, p):
        """Cut-edge distance measured on the body surface below p, so every edge is cut square to the cloth."""
        d = self.upper(p)
        g = gradient(self.upper, p, 1e-3)
        g /= np.maximum(norm(g), 1e-9)[:, None]
        return self.region(p - g * d[:, None]), d

    def shell(self, p):
        edge, d = self.edge(p)
        # The bound edge rolls slightly thicker, like a binding tape folded over the cut.
        half = VEST_HALF + .0010 * np.clip(1 + edge / .007, 0, 1)
        return smax(np.abs(d - VEST_MID) - half, edge, .0012)

    def sdf(self, p):
        return self.shell(p)

    def paint(self, p, n, tone):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        ax = np.abs(x)
        edge, _ = self.edge(p)
        out = Paint(len(p))
        rgb = np.tile(hexrgb(COL["vest"]), (len(p), 1)) * (1 + .03 * fbm(p, 60, 2, 7))[:, None]
        binding = _aa(-.0075 - edge, .0012)                     # 1 within 7.5 mm of the cut edge
        rgb = mix(rgb, hexrgb(COL["binding"])[None, :], binding)
        tape = vest_tape(p) * (1 - binding)
        rgb = mix(rgb, hexrgb(COL["tape"])[None, :], tape)
        zip_ = _aa(ax - .0032, .0008) * (y > 0) * _aa(z - 1.255, .001)
        rgb = mix(rgb, hexrgb(COL["zip"])[None, :], zip_)
        out.set(np.ones(len(p), bool), "vest", np.clip(rgb, 0, 1))
        out.cls[tape > .5] = CLS["tape"]
        out.gloss = GLOSS["vest"] + (GLOSS["tape"] - GLOSS["vest"]) * tape
        out.metal = METAL["tape"] * tape
        # Gentle occlusion against the body (armholes, under the collar), uniform across tape.
        body = lambda q: np.minimum(self.shell(q), self.upper(q))
        out.shade(occlusion(body, p, n) * .4 + .6)
        return out

    def relief(self, p):
        weave = .00006 * fbm(p, 700, 1, 23)
        return weave * (1 - vest_tape(p))


def occlusion(sdf_fn, p, n, steps=(.006, .016, .032)):
    """Cheap SDF ambient occlusion: how quickly free space opens along the normal."""
    total = 0
    for i, d in enumerate(steps):
        sample = sdf_fn(p + n * d)
        total += (d - np.clip(sample, -d, d)) / d * .5 ** i
    return np.clip(1 - .28 * total, .62, 1)


# ================================================================== HEAD (face, neck, hair)
# Facial landmarks (m): profile y and height z on the midline, half widths x. Adult male means.
#   vertex z 1.780; glabella (y .090, z 1.680); nasion (.083, 1.667); pupils (x .032, z 1.656);
#   pronasale (.110, 1.621); subnasale (.090, 1.610); labrale superius (.092, 1.597); stomion (.088, 1.588);
#   labrale inferius (.090, 1.579); sublabiale (.081, 1.566); pogonion (.086, 1.554); menton (.070, 1.543).
#   Half widths: euryon .076, bizygomatic .068, bigonial .056, exocanthion .046, endocanthion .016,
#   alar .0175, cheilion .025, chin .021. Eyeball radius 12 mm, palpebral fissure 30 x 10 mm.
EYE = np.array([.0320, .0628, 1.6560])
EYE_R = .0120
SKULL = ((0, -.012, 1.684), (.0760, .0980, .0960))
FACE = [  # z, front depth, back depth, half width, section centre y, superellipse n
    (1.5440, .0110, .0090, .0170, .0650, 2.2), (1.5520, .0330, .0280, .0335, .0500, 2.3),
    (1.5620, .0660, .0400, .0465, .0200, 2.4), (1.5750, .0880, .0400, .0540, .0000, 2.4),
    (1.5900, .0930, .0450, .0555, -.0050, 2.3), (1.6050, .0960, .0500, .0600, -.0080, 2.35),
    (1.6200, .0950, .0550, .0640, -.0100, 2.4), (1.6400, .0930, .0600, .0680, -.0100, 2.4),
    (1.6600, .0920, .0600, .0690, -.0100, 2.4), (1.6800, .0950, .0600, .0680, -.0100, 2.3),
    (1.7000, .0880, .0600, .0640, -.0100, 2.2)]


class Head:
    name = "Head"
    lo, hi, h = (-.105, -.135, 1.39), (.105, .14, 1.795), .0011

    def __init__(self):
        R = mirrored
        self.skull = ellipsoid(*SKULL)
        z0, z1 = FACE[0][0], FACE[-1][0]
        face = loft2((0, 0, z0), (0, 0, z1), (0, 1, 0),
                     [((z - z0) / (z1 - z0), f, b, w, c, n) for z, f, b, w, c, n in FACE])
        head = smin(self.skull, face, .022)
        # Bony landmarks: brow ridge, cheekbones and the mental protuberance, all restrained.
        brow = R(round_cone((.010, .081, 1.6775), (.046, .068, 1.6745), .0085, .0060))
        cheekbone = R(ellipsoid((.050, .048, 1.6390), (.014, .018, .010)))
        chin = ellipsoid((0, .0775, 1.5555), (.0215, .0115, .0120))
        buccal = R(ellipsoid((.040, .036, 1.600), (.018, .026, .026)))
        head = blend(head, (brow, .010), (cheekbone, .014), (chin, .010), (buccal, .02))
        # Eyes: a shallow socket for the eyeball; the lids are a shell around it opened by an almond cut.
        c = EYE
        head = subtract(head, R(sphere(c + np.array([0, .002, 0]), .0150)), .006)
        lids = R(lambda p: smax(sphere(c, .0146)(p), -sphere(c, .0123)(p), .0005))
        head = smin(head, lids, .0035)
        upper_fold = R(ellipsoid(c + np.array([.001, .0070, .0085]), (.0150, .0060, .0030), rot_y(-6)))
        head = smin(head, upper_fold, .003)
        self.opening = R(ellipsoid(c + np.array([.0008, .0128, -.0004]), (.0154, .0130, .0049), rot_y(-6)))
        head = subtract(head, self.opening, .0010)
        # Nose: bridge, body, rounded tip and alae; nostrils are shallow carves underneath.
        bridge = round_cone((0, .0835, 1.6650), (0, .0995, 1.6280), .0058, .0072)
        body = ellipsoid((0, .0925, 1.6290), (.0105, .0110, .0200))
        tip = sphere((0, .0985, 1.6225), .0080)
        alae = R(ellipsoid((.0108, .0890, 1.6130), (.0056, .0080, .0056)))
        nose = blend(bridge, (body, .006), (tip, .005), (alae, .0075))
        head = smin(head, nose, .005)
        head = subtract(head, R(ellipsoid((.0068, .0975, 1.6082), (.0032, .0042, .0018))), .0012)
        # Lips: modest vermilion volumes, a fine mouth line, philtrum and labiomental groove.
        self.upper_lip = ellipsoid((0, .0808, 1.5925), (.0235, .0098, .0055))
        self.lower_lip = ellipsoid((0, .0795, 1.5815), (.0205, .0093, .0062))
        head = smin(head, smin(self.upper_lip, self.lower_lip, .002), .005)
        head = subtract(head, ellipsoid((0, .0915, 1.5880), (.0245, .0120, .00065)), .0010)
        head = subtract(head, ellipsoid((0, .0935, 1.6030), (.0030, .0028, .0055)), .0020)
        head = subtract(head, ellipsoid((0, .0880, 1.5680), (.0150, .0040, .0032)), .0035)
        # Neck with sternocleidomastoids and a slight larynx; the trapezius blend lives in the body.
        neck_st = [((z - 1.40) / .23, d, b, oy, 0, 2) for z, d, b, oy in
                   ((1.40, .062, .062, -.014), (1.47, .058, .058, -.016), (1.53, .055, .055, -.022),
                    (1.58, .054, .054, -.024), (1.63, .052, .053, -.022))]
        neck = loft((0, 0, 1.40), (0, 0, 1.63), (0, 1, 0), neck_st)
        scm = R(round_cone((.047, -.022, 1.608), (.016, .036, 1.466), .0100, .0078))
        larynx = ellipsoid((0, .030, 1.522), (.0085, .0080, .012))
        self.neck = blend(neck, (scm, .012), (larynx, .008))
        ear = R(ellipsoid((.075, -.012, 1.642), (.011, .020, .028)))
        self.face = smin(head, ear, .006)
        # Short hair below the cap: a thin layer over the back and sides of the skull.
        def hair_region(p):
            y, z = p[:, 1], p[:, 2]
            line = 1.603 + .9 * np.maximum(0, y + .015) + .25 * np.maximum(0, y - .02)
            return np.maximum(line - z, y - .07)
        self.hair_region = hair_region
        self.hair = intersect(offset(self.skull, .0030), hair_region, .0015)

    def sdf(self, p):
        head = smin(self.face(p), self.neck(p), .009)
        return np.minimum(head, self.hair(p))

    def paint(self, p, n, tone):
        T = TONES[tone]
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        ax = np.abs(x)
        front = ss(.03, .06, y)
        out = Paint(len(p))
        skin = hexrgb(T["skin"])
        var = 1 + .025 * fbm(p, 55, 3, 3)
        rgb = skin[None, :] * var[:, None]
        # Warmer cheeks, nose tip and ears; cooler, slightly darker beard area and under-eye.
        red = np.array([1.07, .95, .93])
        warm = .7 * np.exp(-((ax - .046) / .016) ** 2 - ((z - 1.620) / .016) ** 2) * front
        warm += .6 * np.exp(-(x / .012) ** 2 - ((z - 1.619) / .010) ** 2) * ss(.09, .1, y)
        rgb = rgb * mix(np.ones(3), red, np.clip(warm, 0, 1) * .6)
        under = np.exp(-((ax - .036) / .011) ** 2 - ((z - 1.6435) / .0045) ** 2) * front
        rgb *= (1 - .03 * under)[:, None]
        beard = ss(1.600, 1.588, z) * ss(-.03, .0, y)
        beard = np.maximum(beard, ss(1.612, 1.600, z) * (ax > .02) * ss(.02, .05, y) * .7)
        beard *= 1 - np.exp(-(x / .022) ** 2 - ((z - 1.585) / .009) ** 2)    # not on the lips
        shade = (.07 if tone != 2 else .05) * beard * (.75 + .25 * fbm(p, 600, 1, 4))
        rgb = mix(rgb, hexrgb(T["hair"])[None, :], shade)
        # Nasolabial shading: a soft line from the alae to beside the mouth corners.
        t = np.clip((1.611 - z) / .026, 0, 1)
        fold_x = .021 + .009 * t
        fold = np.exp(-((ax - fold_x) / .0025) ** 2) * (z < 1.612) * (z > 1.582) * front
        rgb *= (1 - .06 * fold)[:, None]
        out.set(np.ones(len(p), bool), "skin", np.clip(rgb, 0, 1))
        # Lip vermilion: cupid's bow on the upper lip, fuller lower lip, borders softened over 0.5 mm.
        u = np.clip(ax / .0245, 0, 1)
        top = 1.5600 + 0 * u
        top = 1.5885 + .0085 * (1 - u ** 2.2) - .0012 * np.exp(-(x / .0030) ** 2) + .0006 * np.exp(-((ax - .006) / .003) ** 2)
        bottom = 1.5880 - .0098 * (1 - u ** 2.0)
        lipmask = ss(.0006, -.0002, z - top) * ss(-.0006, .0002, z - bottom) * ss(.0255, .0225, ax) * ss(.07, .08, y)
        lip_rgb = hexrgb(T["lips"])[None, :] * var[:, None]
        out.rgb = mix(out.rgb, lip_rgb, lipmask * .92)
        line = np.exp(-((z - 1.5880) / .0007) ** 2) * ss(.0255, .020, ax) * ss(.07, .08, y)
        out.rgb *= (1 - .45 * line)[:, None]
        out.cls[lipmask > .5] = CLS["lips"]; out.gloss[lipmask > .5] = GLOSS["lips"]
        # Nostril shadows.
        nostril = np.exp(-((ax - .0068) / .0028) ** 2 - ((z - 1.6085) / .0018) ** 2) * ss(.09, .095, y) * (n[:, 2] < -.2)
        out.rgb *= (1 - .55 * nostril)[:, None]
        # Eyebrows: tapered arcs, dense at the head, fine at the tail; softly antialiased.
        bx = np.clip((ax - .012) / .038, 0, 1)
        arc = 1.6740 + .0060 * np.sin(bx * math.pi * .8) - .0025 * bx
        half = .0036 * (1 - .6 * bx) + .0007
        density = ss(half, half * .55, np.abs(z - arc)) * ss(.010, .014, ax) * ss(.052, .046, ax) * front
        hairs = .78 + .22 * np.sin(ax * 900 + (z - arc) * 2500)
        out.rgb = mix(out.rgb, hexrgb(T["hair"])[None, :], np.clip(density * hairs, 0, .9))
        # Lash line and the upper lid crease.
        rim = self.opening(p)
        lash = ss(.0009, .0001, rim) * front * ss(EYE[2] - .002, EYE[2] + .002, z)   # upper lid only
        out.rgb = mix(out.rgb, hexrgb(T["hair"])[None, :], .45 * lash)
        crease = np.exp(-((z - (EYE[2] + .0098 - .02 * (ax - EYE[0]) ** 2 * 0)) / .0016) ** 2) * \
            np.exp(-((ax - EYE[0]) / .013) ** 2) * front
        out.rgb *= (1 - .06 * crease)[:, None]
        # Hair, with a soft hairline.
        hair = self.hair(p) <= np.minimum(self.face(p), self.neck(p)) + 1e-4
        hair_rgb = hexrgb(T["hair"])[None, :] * (1 + .22 * fbm(p, 700, 2, 13))[:, None]
        edge = ss(-.004, .002, self.hair_region(p))
        out.rgb = np.where(hair[:, None], mix(hair_rgb, out.rgb, edge * .6), out.rgb)
        out.cls[hair] = CLS["hair"]; out.gloss[hair] = GLOSS["hair"]
        # Socket occlusion is kept faint around the eyes so they do not read as made up.
        socket = np.exp(-((ax - EYE[0]) / .018) ** 2 - ((z - EYE[2]) / .012) ** 2) * front
        occ = occlusion(self.sdf, p, n, (.0015, .004, .008))
        out.shade(1 - (1 - occ) * (.5 - .35 * socket))
        return out

    def relief(self, p):
        z = p[:, 2]
        pores = .00003 * fbm(p, 2400, 1, 17)
        hair = self.hair(p) <= np.minimum(self.face(p), self.neck(p)) + 1e-4
        strands = .00010 * np.sin(z * 2600 + 8 * fbm(p, 400, 1, 3)) * hair
        return pores + strands



# ================================================================== GLOVES (right hand, mirrored for left)
KNUCKLE_X = .872
FINGERS = [  # y at the knuckle, length, radius, splay (deg)
    (.0305, .074, .0098, -4), (.0105, .081, .0102, -1), (-.0095, .077, .0098, 2), (-.0285, .061, .0088, 6)]
CURL = (12, 18, 14)
THUMB = [(.792, .026, 1.390), (.818, .052, 1.380), (.840, .066, 1.373), (.862, .075, 1.366)]


def finger_points(index):
    y0, length, radius, splay = FINGERS[index]
    points = [np.array([KNUCKLE_X, y0, 1.4005])]
    direction = np.array([math.cos(math.radians(splay)), math.sin(math.radians(splay)) * -1, 0.0])
    pitch = 4
    for segment, fraction in enumerate((.45, .30, .25)):
        pitch += CURL[segment]
        a = math.radians(pitch)
        d = np.array([direction[0] * math.cos(a), direction[1] * math.cos(a), -math.sin(a)])
        points.append(points[-1] + d * length * fraction)
    return points, radius


class Glove:
    name = "Glove"
    lo, hi, h = (.725, -.06, 1.33), (.975, .10, 1.44), .0010

    def __init__(self):
        palm_st = [((x - .772) / (.885 - .772), rz, ry, oz, -oy, n) for x, rz, ry, oz, oy, n in
                   ((.772, .0215, .0315, .000, .000, 2.2), (.80, .0185, .0400, -.001, .001, 2.6),
                    (.84, .0160, .0445, -.0010, .0005, 2.8), (.866, .0135, .0440, -.0010, .0005, 2.8),
                    (.880, .0112, .0400, -.0010, .000, 2.5))]
        palm = loft((.772, 0, 1.40), (.885, 0, 1.40), (0, 0, 1), palm_st)
        thenar = ellipsoid((.812, .026, 1.386), (.028, .017, .012))
        hypothenar = ellipsoid((.825, -.030, 1.388), (.034, .012, .010))
        hand = blend(palm, (thenar, .012), (hypothenar, .010))
        knuckles = [sphere((KNUCKLE_X - .003, f[0], 1.4035), f[2] * 1.12) for f in FINGERS]
        for k in knuckles:
            hand = smin(hand, k, .007)
        digits = []
        for i in range(4):
            pts, r = finger_points(i)
            seg = None
            for k in range(3):
                piece = round_cone(pts[k], pts[k + 1], r * (1 - .06 * k), r * (1 - .06 * (k + 1)))
                seg = piece if seg is None else smin(seg, piece, .003)
            digits.append(seg)
        self.digits = digits
        fingers = lambda p: np.minimum.reduce([d(p) for d in digits])
        t = [np.array(v) for v in THUMB]
        thumb = smin(smin(round_cone(t[0], t[1], .0135, .0115), round_cone(t[1], t[2], .0115, .0108), .003),
                     round_cone(t[2], t[3], .0108, .0098), .003)
        self.thumb = thumb
        self.hand = smin(hand, fingers, .006)
        self.hand = smin(self.hand, thumb, .009)
        cuff_st = [((x - .735) / (.786 - .735), rz, ry, 0, 0, 2.2) for x, rz, ry in
                   ((.735, .0242, .0322), (.745, .0236, .0316), (.770, .0228, .0306), (.786, .0224, .0300))]
        cuff = loft((.735, 0, 1.40), (.786, 0, 1.40), (0, 0, 1), cuff_st)
        # A rolled rib at the opening gives the cuff a clean, readable edge.
        rib = lambda p: np.sqrt((p[:, 0] - .7372) ** 2 + np.maximum(
            np.sqrt(((p[:, 2] - 1.40) / .0242) ** 2 + (p[:, 1] / .0322) ** 2) - 1, 0) ** 2 * .027 ** 2) - .0024
        self.cuff = smin(cuff, rib, .0015)

    def sdf(self, p):
        return smin(self.hand(p), self.cuff(p), .004)

    def paint(self, p, n, tone):
        out = Paint(len(p))
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        # The knit cuff ends on a fixed line around the wrist, antialiased over a texel.
        cuff_cover = np.clip(.5 - (x - .7835) / .0012, 0, 1)
        cuff = cuff_cover > .5
        knit = 1 + .06 * np.sin(x * 2200) * np.sin(y * 900 + z * 900)
        out.set(np.ones(len(p), bool), "glove")
        out.shade(knit)
        # Nitrile coating on the palm, the palm side of the digits and over the fingertips.
        palm_side = n[:, 2] < -.15
        tips = np.zeros(len(p), bool)
        for i in range(4):
            pts, r = finger_points(i)
            tips |= norm(p - pts[3]) < .018
        tips |= norm(p - np.array(THUMB[3])) < .016
        coat = (palm_side | tips) & ~cuff
        out.set(coat, "coating")
        cuff_rgb = np.tile(hexrgb(COL["cuff"]), (len(p), 1)) * (1 + .06 * np.sin(np.arctan2(y, z - 1.40) * 60))[:, None]
        out.rgb = mix(out.rgb, cuff_rgb, cuff_cover)
        out.cls[cuff] = CLS["glove"]; out.gloss[cuff] = GLOSS["glove"]
        out.shade(occlusion(self.sdf, p, n, (.0015, .004, .008)))
        return out

    def relief(self, p):
        x = p[:, 0]
        return .00010 * np.sin(x * 2200) * np.sin(p[:, 1] * 900 + p[:, 2] * 900) + .00004 * fbm(p, 1500, 1, 2)


# ================================================================== BOOTS (right, mirrored)
class Boot:
    name = "Boot"
    lo, hi, h = (.03, -.10, -.005), (.18, .27, .25), .0016

    def __init__(self):
        cx = .105
        st = [((y + .070) / (.232 + .070), hz, hx, cz, 0, n) for y, hx, hz, cz, n in
              ((-.070, .033, .042, .062, 2.4), (-.058, .042, .060, .078, 2.6), (-.028, .045, .078, .094, 2.8),
               (.020, .046, .074, .090, 2.8), (.070, .049, .054, .072, 2.8), (.125, .052, .037, .054, 2.8),
               (.170, .052, .031, .047, 2.8), (.205, .046, .027, .043, 2.6), (.232, .029, .020, .041, 2.3))]
        upper = loft((cx, -.070, 0), (cx, .232, 0), (0, 0, 1), st)
        shaft = capsule((cx, -.006, .09), (cx, -.004, .17), .050)
        collar = capsule((cx, -.006, .165), (cx, -.004, .172), .054)
        self.upper = blend(upper, (shaft, .03), (collar, .01))
        sole_st = [((y + .076) / (.240 + .076), .015, hx, .015, 0, 4) for y, hx in
                   ((-.076, .034), (-.062, .044), (-.03, .047), (.06, .049), (.13, .055), (.18, .054), (.222, .044), (.240, .028))]
        sole = loft((cx, -.076, 0), (cx, .240, 0), (0, 0, 1), sole_st)
        # Raise the toe spring slightly: the sole bottom stays flat under heel and ball.
        self.sole = lambda p: np.maximum(sole(p), -p[:, 2])
        self.cx = cx

    def sdf(self, p):
        return smin(self.upper(p), self.sole(p), .003)

    def paint(self, p, n, tone):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        lx = x - self.cx
        out = Paint(len(p))
        out.set(np.ones(len(p), bool), "boot", hexrgb(COL["boot"]) * (1 + .05 * fbm(p, 120, 2, 3))[:, None])
        sole = (self.sole(p) < self.upper(p)) | (z < .022)
        out.set(sole, "sole")
        toe = (y > .165) & ~sole
        out.set(toe, "boot", hexrgb(COL["toecap"]))
        # Laces across the instep and the welt stitch.
        lace_zone = (np.abs(lx) < .021) & (y > -.005) & (y < .125) & (z > .06) & (n[:, 2] > .2)
        rungs = np.abs(((y + .005) / .018) % 1 - .5) < .17
        out.set(lace_zone & rungs, "boot", hexrgb(COL["lace"]))
        out.shade(.8, lace_zone & ~rungs)
        welt = (np.abs(z - .027) < .0018) & ~sole
        out.shade(.7, welt)
        out.shade(occlusion(self.sdf, p, n, (.003, .008, .016)))
        return out

    def relief(self, p):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        lugs = .0008 * (np.sin(y * 260) > .3) * (z < .02)
        return .00008 * fbm(p, 600, 2, 4) + lugs


# ================================================================== CAP AND EAR DEFENDERS
class Cap:
    name = "Cap"
    lo, hi, h = (-.10, -.13, 1.64), (.10, .17, 1.81), .0012

    def __init__(self):
        crown = offset(ellipsoid(*SKULL), .0060)
        front = ellipsoid((0, .026, 1.744), (.069, .062, .042))
        crown = smin(crown, front, .02)
        band = lambda p: (1.6925 + .21 * p[:, 1]) - p[:, 2]
        self.crown = intersect(crown, band, .002)
        def bill(p):
            x, y, z = p[:, 0], p[:, 1], p[:, 2]
            surface = 1.7110 - .16 * (y - .07) - 2.0 * x * x
            slab = np.abs(z - surface) * .97 - .0024
            outline = (np.sqrt((x / .089) ** 2 + ((y - .062) / .088) ** 2) - 1) * .085
            return np.maximum(np.maximum(slab, outline), .066 - y)
        self.bill = bill
        self.button = sphere((0, -.010, 1.7855), .0068)

    def sdf(self, p):
        return smin(self.crown(p), self.bill(p), .004)

    def paint(self, p, n, tone):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        out = Paint(len(p))
        out.set(np.ones(len(p), bool), "cap")
        # Panel seams radiating from the button, stitch rows along the bill and a darker underside.
        angle = np.arctan2(x, y + .01)
        panels = np.abs(((angle / (math.pi / 3)) + .5) % 1 - .5) < .012
        out.shade(.75, panels & (self.crown(p) < self.bill(p)) & (z > 1.71))
        billside = self.bill(p) < self.crown(p)
        rows = np.zeros(len(p), bool)
        for k in range(1, 5):
            edge = (np.sqrt((x / .089) ** 2 + ((y - .062) / .088) ** 2))
            rows |= np.abs(edge - (1 - .045 * k)) < .006
        out.shade(.78, billside & rows & (n[:, 2] > 0))
        out.shade(.82, billside & (n[:, 2] < 0))
        out.shade(occlusion(self.sdf, p, n, (.002, .005, .010)))
        return out

    def relief(self, p):
        return .0001 * fbm(p, 900, 2, 5)


class Muffs:
    name = "Muffs"
    lo, hi, h = (-.15, -.05, 1.58), (.15, .04, 1.82), .0013

    def __init__(self):
        cup = smax(smax(ellipsoid((.100, -.008, 1.641), (.026, .037, .048)), lambda p: .089 - p[:, 0], .003),
                   lambda p: p[:, 0] - .118, .006)
        cushion = smax(ellipsoid((.087, -.008, 1.641), (.012, .035, .045)), lambda p: p[:, 0] - .0915, .003)
        self.cup = mirrored(cup)
        self.cushion = mirrored(cushion)
        def band(p):
            x, y, z = p[:, 0], p[:, 1], p[:, 2]
            r = (np.sqrt((x / .1045) ** 2 + ((z - 1.645) / .1450) ** 2) - 1) * .1045
            section = np.sqrt(np.maximum(np.abs(r) - .0025, 0) ** 2 + np.maximum(np.abs(y + .008) - .0105, 0) ** 2) - .0025
            return np.maximum(section, 1.676 - z)
        self.band = band
        yoke = round_box((.1035, -.008, 1.6765), (.0040, .0085, .0110), .0035)
        self.yoke = mirrored(yoke)

    def sdf(self, p):
        return np.minimum.reduce([smin(self.cup(p), self.yoke(p), .004), self.cushion(p), smin(self.band(p), self.yoke(p), .004)])

    def paint(self, p, n, tone):
        out = Paint(len(p))
        out.set(np.ones(len(p), bool), "cup")
        cushion = self.cushion(p) < np.minimum(self.cup(p), self.band(p)) + 1e-4
        out.set(cushion, "cushion")
        band = self.band(p) < self.cup(p)
        out.set(band & ~cushion, "cup", hexrgb(COL["band"]))
        out.shade(occlusion(self.sdf, p, n, (.002, .005, .010)))
        return out

    def relief(self, p):
        return .00005 * fbm(p, 700, 1, 6)


# ================================================================== EYES (separate spheres, painted iris)
def eye_paint(p, tone, centre):
    """Straight-ahead gaze: the iris is centred on the lid opening (measured 0.6 mm temporal, 0.4 mm low)."""
    centre = np.broadcast_to(np.asarray(centre, float), p.shape)       # one centre, or one per point
    side = np.where(centre[:, 0] >= 0, 1.0, -1.0)
    az, el = math.radians(2.9), math.radians(-2.0)
    d = p - centre
    d /= np.maximum(norm(d), 1e-9)[:, None]
    cos = d[:, 0] * side * math.sin(az) * math.cos(el) + d[:, 1] * math.cos(az) * math.cos(el) + d[:, 2] * math.sin(el)
    angle = np.degrees(np.arccos(np.clip(cos, -1, 1)))
    out = Paint(len(p))
    sclera = hexrgb(COL["sclera"])[None, :] * (1 - .12 * ss(35, 85, angle))[:, None]
    out.set(np.ones(len(p), bool), "sclera", sclera)
    # 11.7 mm iris on a 24 mm eyeball; antialiased limbus with a soft, narrow dark ring.
    radial = 1 + .14 * np.sin(np.arctan2(d[:, 0], d[:, 2]) * 23) * ss(9, 20, angle)
    iris_rgb = hexrgb(TONES[tone]["iris"])[None, :] * radial[:, None] * (1 - .25 * ss(24, 28.5, angle))[:, None]
    out.rgb = mix(out.rgb, iris_rgb, np.clip((28.5 - angle) / 1.5 + .5, 0, 1))
    out.rgb = mix(out.rgb, np.tile(hexrgb(COL["pupil"]), (len(p), 1)), np.clip((9 - angle) / 1.2 + .5, 0, 1))
    out.cls[angle < 28.5] = CLS["iris"]; out.gloss[angle < 28.5] = GLOSS["iris"]
    return out


PARTS = [Body, Vest, Head, Glove, Boot, Cap, Muffs]


# ================================================================== SKIN WEIGHTS
def weights(p, part):
    """Bone weights for points in the rest pose; returns {bone: array}. Smooth, symmetric and limb-aware."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    ax = np.abs(x)
    n = len(p)
    W = {b: np.zeros(n) for b in BONES}
    left = x < 0
    def add(bone, w):
        if bone.endswith("_"):
            W[bone + "L"] += np.where(left, w, 0); W[bone + "R"] += np.where(left, 0, w)
        else:
            W[bone] += w
    if part in ("Cap", "Muffs", "Eye"):
        W["Head"][:] = 1
        return W
    # ---- arms: boundary depends on the side of the shoulder (broad over the deltoid, tight in the armpit).
    s = np.clip((z - 1.40) / np.maximum(np.hypot(z - 1.40, y), 1e-6), -1, 1)
    top, bottom = np.clip(s, 0, 1), np.clip(-s, 0, 1)
    lo = .172 - .030 * top + .022 * bottom
    hi = .240 + .035 * top - .010 * bottom
    arm = ss(lo, hi, ax) * ss(1.18, 1.26, z)
    if part == "Glove":
        arm = np.ones(n)
    # ---- legs: groin crease in front, broad blend over the buttock behind.
    front = ss(-.04, .05, y)
    crease = .825 + .55 * np.clip(ax - .02, 0, .2)
    b = crease * front + .905 * (1 - front)
    w = .025 * front + .075 * (1 - front)
    # At the crotch half the weight stays on the pelvis and the rest is shared by both thighs.
    leg = (1 - ss(b - w, b + w, z)) * (.45 + .55 * ss(.0, .045, ax))
    if part == "Boot":
        leg = np.ones(n)
    leg *= (1 - arm)
    torso = np.clip(1 - arm - leg, 0, 1)
    # ---- torso chain, neck and head.
    hips = 1 - ss(.98, 1.12, z)
    chest = ss(1.15, 1.31, z)
    spine = np.clip(1 - hips - chest, 0, 1)
    if part == "Head":
        head_d = HEAD_ONLY(p)
        head = 1 - ss(.0, .022, head_d)
        head = np.maximum(head, ss(1.60, 1.63, z))
    else:
        head = np.zeros(n)
    neck = ss(-.018, .018, z - (1.478 - .25 * y)) * (1 - ss(.070, .105, ax))
    clav = ss(.06, .13, ax) * ss(1.36, 1.44, z) * (1 - neck)
    head_part = neck * head
    neck_part = neck * (1 - head)
    rest = 1 - neck
    add("Hips", torso * rest * hips)
    add("Spine", torso * rest * spine)
    add("Chest", torso * rest * chest * (1 - clav))
    add("Clavicle_", torso * rest * chest * clav)
    add("Neck", torso * neck_part)
    add("Head", torso * head_part)
    # ---- arm chain.
    crease_front = ss(-.01, .03, y)
    we = .036 - .010 * crease_front
    elbow = ss(.505 - we, .505 + we, ax)
    wrist = ss(.776, .802, ax)
    if part == "Glove":
        fing, thumb = glove_masks(p)
    else:
        fing, thumb = np.zeros(n), np.zeros(n)
    add("Clavicle_", arm * (1 - elbow) * (1 - ss(.17, .23, ax)) * .35 * top)
    add("UpperArm_", arm * (1 - elbow) * (1 - (1 - ss(.17, .23, ax)) * .35 * top))
    add("Forearm_", arm * elbow * (1 - wrist))
    add("Hand_", arm * wrist * (1 - fing - thumb))
    add("Fingers_", arm * wrist * fing)
    add("Thumb_", arm * wrist * thumb)
    # ---- leg chain.
    kfront = ss(-.01, .04, y - .015)
    wk = .055 - .030 * kfront
    knee = 1 - ss(.515 - wk, .515 + wk, z)
    ankle = 1 - ss(.065, .125, z)
    toe = ss(.150, .200, y) * (1 - ss(.08, .11, z))
    thigh = leg * (1 - knee)
    to_left = ss(.025, -.025, x)
    W["Thigh_L"] += thigh * to_left; W["Thigh_R"] += thigh * (1 - to_left)
    add("Shin_", leg * knee * (1 - ankle))
    add("Foot_", leg * knee * ankle * (1 - toe))
    add("Toe_", leg * knee * ankle * toe)
    return W


def glove_masks(p):
    """Fingers bend at the knuckle line (one bone for the four digits); the thumb has its own bone."""
    q = p.copy(); q[:, 0] = np.abs(q[:, 0])
    x, y, z = q[:, 0], q[:, 1], q[:, 2]
    t = [np.array(v) for v in THUMB]
    seg = t[1] - t[0]
    along = np.clip(((q - t[0]) @ seg) / (seg @ seg), -1, 2)
    radial = norm(q - (t[0] + along[:, None] * seg))
    thumb = ss(.08, .45, along) * (1 - ss(.016, .024, radial)) * ss(.010, .022, y)
    thumb = np.maximum(thumb, (1 - ss(.012, .020, norm(q - t[2]))) * 1.0)
    thumb = np.maximum(thumb, (1 - ss(.012, .020, norm(q - t[3]))))
    thumb = np.clip(thumb, 0, 1)
    fing = ss(KNUCKLE_X - .014, KNUCKLE_X + .010, x) * (1 - thumb)
    return fing, thumb


_head = None


def HEAD_ONLY(p):
    global _head
    if _head is None:
        _head = Head()
    return _head.face(p)
