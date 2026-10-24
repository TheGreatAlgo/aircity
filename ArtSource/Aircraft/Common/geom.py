"""Generic geometry helpers: interpolation, airfoils, lofting and mesh creation.

Conventions (Blender space): nose points -Y, Z up, port (left) wing is +X,
starboard (right) wing is -X. Units are metres. Ground is z = 0.
"""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector


# ---------------------------------------------------------------- interpolation

def pchip(xs, ys):
    """Monotone cubic (PCHIP) interpolator; clamps outside the knot range."""
    xs = np.asarray(xs, float)
    ys = np.asarray(ys, float)
    n = len(xs)
    h = np.diff(xs)
    d = np.diff(ys) / h
    m = np.zeros(n)
    for k in range(1, n - 1):
        if d[k - 1] * d[k] <= 0:
            m[k] = 0.0
        else:
            w1 = 2 * h[k] + h[k - 1]
            w2 = h[k] + 2 * h[k - 1]
            m[k] = (w1 + w2) / (w1 / d[k - 1] + w2 / d[k])
    m[0] = d[0]
    m[-1] = d[-1]

    def f(x):
        x = np.clip(np.asarray(x, float), xs[0], xs[-1])
        k = np.clip(np.searchsorted(xs, x) - 1, 0, n - 2)
        t = (x - xs[k]) / h[k]
        h00 = 2 * t**3 - 3 * t**2 + 1
        h10 = t**3 - 2 * t**2 + t
        h01 = -2 * t**3 + 3 * t**2
        h11 = t**3 - t**2
        return h00 * ys[k] + h10 * h[k] * m[k] + h01 * ys[k + 1] + h11 * h[k] * m[k + 1]

    return f


def table(rows):
    """{name: pchip} from [(x, a, b, ...)] rows with a header row of names."""
    header, *data = rows
    arr = np.array(data, float)
    return {name: pchip(arr[:, 0], arr[:, i]) for i, name in enumerate(header) if i > 0}


def resample_polyline(pts, count):
    """Resample an (N,2|3) polyline to `count` points evenly spaced by arc length.
    Returns (points, cumulative_arc_of_each_point)."""
    pts = np.asarray(pts, float)
    seg = np.linalg.norm(np.diff(pts, axis=0), axis=1)
    cum = np.concatenate([[0.0], np.cumsum(seg)])
    target = np.linspace(0.0, cum[-1], count)
    out = np.stack([np.interp(target, cum, pts[:, i]) for i in range(pts.shape[1])], axis=1)
    return out, target


def smoothstep(e0, e1, x):
    t = np.clip((np.asarray(x, float) - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


# ---------------------------------------------------------------- airfoils

def naca4(code, n=28, cosine=True):
    """NACA 4-digit airfoil. Returns (upper, lower) arrays of (x, z), x 0..1,
    both ordered LE -> TE. Closed (zero-thickness) trailing edge."""
    m = int(code[0]) / 100.0
    p = int(code[1]) / 10.0
    t = int(code[2:]) / 100.0
    beta = np.linspace(0, math.pi, n)
    x = (1 - np.cos(beta)) / 2 if cosine else np.linspace(0, 1, n)
    yt = 5 * t * (0.2969 * np.sqrt(x) - 0.1260 * x - 0.3516 * x**2 + 0.2843 * x**3 - 0.1036 * x**4)
    if m == 0:
        yc = np.zeros_like(x)
        dyc = np.zeros_like(x)
    else:
        yc = np.where(x < p, m / p**2 * (2 * p * x - x**2), m / (1 - p) ** 2 * ((1 - 2 * p) + 2 * p * x - x**2))
        dyc = np.where(x < p, 2 * m / p**2 * (p - x), 2 * m / (1 - p) ** 2 * (p - x))
    th = np.arctan(dyc)
    xu, zu = x - yt * np.sin(th), yc + yt * np.cos(th)
    xl, zl = x + yt * np.sin(th), yc - yt * np.cos(th)
    return np.stack([xu, zu], 1), np.stack([xl, zl], 1)


def airfoil_eval(code):
    """Callable (x_frac array) -> (z_upper, z_lower, z_camber) for a NACA 4 section."""
    up, lo = naca4(code, n=400)

    def ev(xf):
        xf = np.asarray(xf, float)
        zu = np.interp(xf, up[:, 0], up[:, 1])
        zl = np.interp(xf, lo[:, 0], lo[:, 1])
        return zu, zl, 0.5 * (zu + zl)

    return ev


# ---------------------------------------------------------------- mesh building

class MeshBuilder:
    """Accumulates verts/faces/uvs/material slots, then emits one Blender object."""

    def __init__(self):
        self.verts = []
        self.faces = []  # list of vertex index tuples
        self.face_uvs = []  # per face: list of (u, v) or None
        self.face_mat = []  # per face: material name
        self.face_tag = []  # per face: free-form tag (for later UV grouping / selection)

    def add_vert(self, p):
        self.verts.append(tuple(float(c) for c in p))
        return len(self.verts) - 1

    def add_verts(self, pts):
        start = len(self.verts)
        for p in pts:
            self.verts.append(tuple(float(c) for c in p))
        return start

    def add_face(self, idx, mat, uvs=None, tag=""):
        self.faces.append(tuple(idx))
        self.face_uvs.append(uvs)
        self.face_mat.append(mat)
        self.face_tag.append(tag)

    def grid(self, base, rows, cols, mat, uv_fn=None, tag="", wrap=False, flip=False):
        """Quads over a rows x cols vertex grid starting at vertex `base`.
        uv_fn(r, c) -> (u, v) for vertex (r, c). wrap closes the columns."""
        ccount = cols if wrap else cols - 1
        for r in range(rows - 1):
            for c in range(ccount):
                c2 = (c + 1) % cols
                a = base + r * cols + c
                b = base + r * cols + c2
                d = base + (r + 1) * cols + c
                e = base + (r + 1) * cols + c2
                quad = [a, b, e, d]
                uvq = None
                if uv_fn is not None:
                    uvq = [uv_fn(r, c), uv_fn(r, c2 if c2 else cols), uv_fn(r + 1, c2 if c2 else cols), uv_fn(r + 1, c)]
                if flip:
                    quad = quad[::-1]
                    uvq = uvq[::-1] if uvq else None
                self.add_face(quad, mat, uvq, tag)

    def merge(self, other):
        off = len(self.verts)
        self.verts.extend(other.verts)
        for f, uv, m, t in zip(other.faces, other.face_uvs, other.face_mat, other.face_tag):
            self.faces.append(tuple(i + off for i in f))
            self.face_uvs.append(uv)
            self.face_mat.append(m)
            self.face_tag.append(t)

    def build(self, name, materials, collection=None, sharp_angle=35.0, origin=None):
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.verts, [], self.faces)
        me.validate(clean_customdata=False)
        slot_names = []
        for mname in self.face_mat:
            if mname not in slot_names:
                slot_names.append(mname)
        for mname in slot_names:
            me.materials.append(materials[mname])
        idx = {n: i for i, n in enumerate(slot_names)}
        me.polygons.foreach_set("material_index", [idx[m] for m in self.face_mat])
        uvl = me.uv_layers.new(name="UVMap")
        for poly, uvs in zip(me.polygons, self.face_uvs):
            if uvs is None:
                continue
            for li, uv in zip(poly.loop_indices, uvs):
                uvl.data[li].uv = uv
        # face tags survive as an int attribute so later stages can select by tag
        tags = sorted(set(self.face_tag))
        attr = me.attributes.new("part_tag", "INT", "FACE")
        attr.data.foreach_set("value", [tags.index(t) for t in self.face_tag])
        me["part_tags"] = tags
        me.shade_smooth()
        if sharp_angle is not None:
            me.set_sharp_from_angle(angle=math.radians(sharp_angle))
        me.update()
        ob = bpy.data.objects.new(name, me)
        (collection or bpy.context.scene.collection).objects.link(ob)
        if origin is not None:
            set_origin(ob, origin)
        return ob


def set_origin(ob, origin):
    origin = Vector(origin)
    ob.data.transform(__import__("mathutils").Matrix.Translation(-origin))
    ob.location = ob.location + origin


def loft(builder, sections, mat, closed=True, uv_fn=None, tag="", cap_start=False, cap_end=False, flip=False):
    """Loft a list of equal-length point rings (each (N,3)). Returns base index."""
    rows = len(sections)
    cols = len(sections[0])
    base = builder.add_verts([p for sec in sections for p in sec])
    builder.grid(base, rows, cols, mat, uv_fn=uv_fn, tag=tag, wrap=closed, flip=flip)
    if cap_start:
        builder.add_face([base + c for c in range(cols)][:: (1 if flip else -1)], mat, tag=tag + "_cap")
    if cap_end:
        last = base + (rows - 1) * cols
        builder.add_face([last + c for c in range(cols)][:: (-1 if flip else 1)], mat, tag=tag + "_cap")
    return base


def sweep_profile(path_pts, profile_fn, up=(0, 0, 1)):
    """Sweep a 2D profile along a 3D polyline. profile_fn(i, t) -> (M,2) local
    (side, up) coords for path point i (t = i/(n-1)). Returns list of rings."""
    path = [Vector(p) for p in path_pts]
    n = len(path)
    rings = []
    upv = Vector(up)
    for i, p in enumerate(path):
        if i == 0:
            tan = path[1] - path[0]
        elif i == n - 1:
            tan = path[-1] - path[-2]
        else:
            tan = path[i + 1] - path[i - 1]
        tan.normalize()
        side = tan.cross(upv)
        if side.length < 1e-6:
            side = tan.cross(Vector((0, 1, 0)))
        side.normalize()
        nup = side.cross(tan).normalized()
        prof = profile_fn(i, i / (n - 1) if n > 1 else 0.0)
        rings.append([tuple(p + side * a + nup * b) for a, b in prof])
    return rings


def bm_from_object(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    return bm
