"""A small modelling kit for vehicles and buildings: bevelled boxes, cylinders, extruded profiles and lofts,
built into one bmesh per part with a material per face.

Coordinates are given in vehicle terms, V(right, forward, up) in metres, origin on the ground under the rear
axle (buildings: on the ground at the front centre). That is Blender space as is (+X right, +Y forward, +Z up):
the FBX export maps Blender (x, y, z) to Unity (x, z, y), so the vehicle faces Unity +Z with +X to its right,
with no turn-around (unlike the aircraft pipeline).
"""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector


def V(r, f, z):
    """Vehicle (right, forward, up) -> Blender (x, y, z)."""
    return Vector((r, f, z))


def M(r=0.0, f=0.0, z=0.0, yaw=0.0, pitch=0.0, roll=0.0):
    """Placement in vehicle terms: translate to (r, f, z), then yaw (degrees, positive = nose turns right),
    pitch (positive = nose up) and roll (positive = right side down), applied roll -> pitch -> yaw."""
    T = Matrix.Translation(V(r, f, z))
    Ry = Matrix.Rotation(math.radians(-yaw), 4, "Z")     # yaw right: forward (+Y) turns to right (+X)
    Rp = Matrix.Rotation(math.radians(pitch), 4, "X")    # pitch up: forward (+Y) turns to +Z
    Rr = Matrix.Rotation(math.radians(roll), 4, "Y")     # roll right: right (+X) turns to -Z
    return T @ Ry @ Rp @ Rr


class Part:
    """One mesh object being built. Materials are named; faces remember theirs."""

    def __init__(self, name, pivot=(0.0, 0.0, 0.0)):
        self.name = name
        self.pivot = V(*pivot)
        self.bm = bmesh.new()
        self.mats = []

    def mat(self, name):
        if name not in self.mats:
            self.mats.append(name)
        return self.mats.index(name)

    def _finish(self, geom_faces, mat, bevel, segments, matrix, sharp=True):
        bm = self.bm
        faces = [f for f in geom_faces if isinstance(f, bmesh.types.BMFace)]
        verts = list({v for f in faces for v in f.verts})
        if matrix is not None:
            bmesh.ops.transform(bm, matrix=matrix, verts=verts)
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        mi = self.mat(mat)
        for f in faces:
            f.material_index = mi
        if bevel > 0:
            edges = list({e for f in faces for e in f.edges})
            res = bmesh.ops.bevel(bm, geom=edges, offset=bevel, offset_type="OFFSET", segments=segments,
                                  profile=0.5, affect="EDGES", clamp_overlap=True)
            for f in res["faces"]:
                f.material_index = mi
            faces = faces + list(res["faces"])
        return faces

    # ------------------------------------------------------------------ primitives

    def box(self, lo, hi, mat, bevel=0.0, segments=2, matrix=None):
        """Axis-aligned box between corners lo / hi given as (right, forward, up), optionally bevelled, then
        placed by matrix (applied about the vehicle origin)."""
        lo, hi = np.array(lo, float), np.array(hi, float)
        size = np.abs(hi - lo)
        c = (lo + hi) / 2
        res = bmesh.ops.create_cube(self.bm, size=1.0)
        verts = res["verts"]
        S = Matrix.Diagonal((size[0], size[1], size[2], 1.0))
        bmesh.ops.transform(self.bm, matrix=Matrix.Translation(V(*c)) @ S, verts=verts)
        faces = list({f for v in verts for f in v.link_faces})
        return self._finish(faces, mat, bevel, segments, matrix)

    def cyl(self, a, b, r, mat, segs=20, bevel=0.0, segments=2, r2=None, matrix=None):
        """Cylinder (or cone with r2) from point a to point b, both (right, forward, up)."""
        A, B = V(*a), V(*b)
        d = B - A
        L = d.length
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r,
                                    radius2=r if r2 is None else r2, depth=L)
        rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
        bmesh.ops.transform(self.bm, matrix=Matrix.Translation((A + B) / 2) @ rot, verts=res["verts"])
        faces = list({f for v in res["verts"] for f in v.link_faces})
        return self._finish(faces, mat, bevel, segments, matrix)

    def prism(self, profile, axis, start, length, mat, bevel=0.0, segments=2, matrix=None):
        """Extrude a closed 2D profile. axis "r": profile is (forward, up) points, extruded along right from
        `start` (right) by `length`; axis "f": (right, up) along forward; axis "z": (right, forward) along up."""
        bm = self.bm
        pts = []
        for p in profile:
            if axis == "r":
                pts.append(V(start, p[0], p[1]))
            elif axis == "f":
                pts.append(V(p[0], start, p[1]))
            else:
                pts.append(V(p[0], p[1], start))
        verts = [bm.verts.new(p) for p in pts]
        face = bm.faces.new(verts)
        step = {"r": V(length, 0, 0), "f": V(0, length, 0), "z": V(0, 0, length)}[axis]
        ext = bmesh.ops.extrude_face_region(bm, geom=[face])
        new_verts = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMVert)]
        bmesh.ops.translate(bm, vec=step, verts=new_verts)
        faces = list({f for v in verts + new_verts for f in v.link_faces})
        return self._finish(faces, mat, bevel, segments, matrix)

    def loft(self, rings, mat, cap=True, matrix=None, bevel=0.0):
        """Loft through closed rings of equal length, each a list of (right, forward, up)."""
        bm = self.bm
        vr = [[bm.verts.new(V(*p)) for p in ring] for ring in rings]
        n = len(rings[0])
        faces = []
        for i in range(len(vr) - 1):
            for k in range(n):
                k2 = (k + 1) % n
                faces.append(bm.faces.new([vr[i][k], vr[i][k2], vr[i + 1][k2], vr[i + 1][k]]))
        if cap:
            faces.append(bm.faces.new(vr[0]))
            faces.append(bm.faces.new(vr[-1]))
        return self._finish(faces, mat, bevel, 2, matrix)

    def quad(self, pts, mat, two_sided=False, matrix=None):
        """A flat polygon (flag, sign); two_sided adds a back face a hair behind."""
        bm = self.bm
        vs = [bm.verts.new(V(*p)) for p in pts]
        faces = [bm.faces.new(vs)]
        if two_sided:
            n = faces[0].normal.copy()
            vs2 = [bm.verts.new(v.co - n * 0.004) for v in vs]
            faces.append(bm.faces.new(vs2[::-1]))
        mi = self.mat(mat)
        for f in faces:
            f.material_index = mi
        if matrix is not None:
            bmesh.ops.transform(bm, matrix=matrix, verts=list({v for f in faces for v in f.verts}))
        return faces

    # ------------------------------------------------------------------ compound parts

    def wheel(self, centre, radius, width, rim_radius, tread=True, rim="Rim", lugs=10, side=1):
        """A truck / tractor wheel with its axle along right: tyre with tread blocks, dished rim, hub and wheel
        nuts. side = +1 for a wheel on the right (rim face outward = +right), -1 on the left."""
        cr, cf, cz = centre
        hw = width / 2
        segs = 36
        # tyre: revolve a closed section (bead, bulging sidewall, rounded shoulder, flat tread)
        inner = rim_radius * 1.0
        wall = radius - inner
        section = [(hw * 0.86, inner), (hw, inner + wall * 0.25), (hw * 1.02, inner + wall * 0.6), (hw, radius - 0.07),
                   (hw * 0.9, radius - 0.02), (hw * 0.6, radius), (-hw * 0.6, radius), (-hw * 0.9, radius - 0.02),
                   (-hw, radius - 0.07), (-hw * 1.02, inner + wall * 0.6), (-hw, inner + wall * 0.25), (-hw * 0.86, inner)]
        self._revolve(centre, section, "Tyre", segs)
        if tread:
            n = 30
            for k in range(n):
                a = 2 * math.pi * (k + 0.5 * (k % 2)) / n
                for s in (-1, 1):
                    x0 = s * hw * 0.08
                    x1 = s * hw * 0.88
                    off = 0.055 * s
                    pts = []
                    for (x, da) in ((x0, 0.0), (x1, off), (x1, off + 0.06), (x0, 0.06)):
                        pts.append((x, a + da))
                    ring_lo = [(cr + x, cf + math.sin(aa) * (radius - 0.025), cz + math.cos(aa) * (radius - 0.025)) for x, aa in pts]
                    ring_hi = [(cr + x, cf + math.sin(aa) * (radius + 0.022), cz + math.cos(aa) * (radius + 0.022)) for x, aa in pts]
                    self.loft([ring_lo, ring_hi], "Tyre")
        # rim (a closed solid): flange at the tyre bead, dished disc, hub; then hub cap and wheel nuts
        s = side
        rim_prof = [(-s * hw * 0.8, rim_radius), (s * hw * 0.8, rim_radius), (s * hw * 0.8, rim_radius * 0.9),
                    (s * hw * 0.3, rim_radius * 0.84), (s * hw * 0.42, rim_radius * 0.42), (s * hw * 0.6, rim_radius * 0.37),
                    (s * hw * 0.6, 0.01), (s * hw * 0.45, 0.01), (s * hw * 0.2, rim_radius * 0.5), (-s * hw * 0.8, rim_radius * 0.9)]
        self._revolve(centre, rim_prof, rim, 28)
        hub_x = side * hw * 0.6
        self.cyl((cr + hub_x, cf, cz), (cr + hub_x + side * 0.09, cf, cz), rim_radius * 0.28, "Chassis", segs=16,
                 bevel=0.01)
        for k in range(lugs):
            a = 2 * math.pi * k / lugs
            pr = rim_radius * 0.36
            p = (cr + hub_x, cf + math.sin(a) * pr, cz + math.cos(a) * pr)
            self.cyl(p, (p[0] + side * 0.045, p[1], p[2]), 0.022, "Metal", segs=6)

    def revolve(self, centre, section, mat, segs=24, axis="r"):
        """Revolve a closed section of (axial offset, radius) points about an axis through centre (axis "r":
        right, "f": forward, "z": up) into a closed solid."""
        cr, cf, cz = centre
        bm = self.bm
        rings = []
        for k in range(segs):
            a = 2 * math.pi * k / segs
            ca, sa = math.cos(a), math.sin(a)
            ring = []
            for x, r in section:
                if axis == "r":
                    p = (cr + x, cf + sa * r, cz + ca * r)
                elif axis == "f":
                    p = (cr + sa * r, cf + x, cz + ca * r)
                else:
                    p = (cr + ca * r, cf + sa * r, cz + x)
                ring.append(bm.verts.new(V(*p)))
            rings.append(ring)
        faces = []
        m = len(section)
        for k in range(segs):
            k2 = (k + 1) % segs
            for i in range(m):
                i2 = (i + 1) % m
                faces.append(bm.faces.new([rings[k][i], rings[k2][i], rings[k2][i2], rings[k][i2]]))
        mi = self.mat(mat)
        for f in faces:
            f.material_index = mi
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        return faces

    def _revolve(self, centre, section, mat, segs):
        return self.revolve(centre, section, mat, segs, "r")

    def build(self, materials, collection):
        me = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(me)
        self.bm.free()
        for n in self.mats:
            me.materials.append(materials[n])
        ob = bpy.data.objects.new(self.name, me)
        collection.objects.link(ob)
        # origin at the pivot
        me.transform(Matrix.Translation(-self.pivot))
        ob.location = self.pivot
        for p in me.polygons:
            p.use_smooth = True
        me.set_sharp_from_angle(angle=math.radians(38))
        me.update()
        return ob


def arc(cx, cy, r, a0, a1, n):
    """Points on a circle in 2D from angle a0 to a1 (degrees), inclusive."""
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * k / n)), cy + r * math.sin(math.radians(a0 + (a1 - a0) * k / n)))
            for k in range(n + 1)]


def rounded_rect(w, h, r, n=4, cx=0.0, cy=0.0):
    """Closed 2D outline of a rounded rectangle (counter-clockwise), centred on (cx, cy)."""
    pts = []
    for (sx, sy, a0) in ((1, 1, 0), (-1, 1, 90), (-1, -1, 180), (1, -1, 270)):
        ox, oy = cx + sx * (w / 2 - r), cy + sy * (h / 2 - r)
        pts += arc(ox, oy, r, a0, a0 + 90, n)
    return pts
