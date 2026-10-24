"""Generic shape builders shared by all aircraft: draped panes, lifting surfaces, surfaces of
revolution, tubes and wheels. All write into a geom.MeshBuilder.

Conventions (Blender space): nose points -Y, Z up, port (left) wing is +X. Units are metres.
"""
import math

import numpy as np
from mathutils import Vector

from geom import airfoil_eval


def rounded_outline(corners, radius, seg_len=0.04, corner_steps=7):
    """Closed outline through `corners` (2D, CCW or CW) with Bezier-rounded corners.
    radius may be a scalar or per-corner list."""
    pts = [np.array(c, float) for c in corners]
    n = len(pts)
    rad = radius if isinstance(radius, (list, tuple)) else [radius] * n
    out = []
    for i in range(n):
        prv, cur, nxt = pts[i - 1], pts[i], pts[(i + 1) % n]
        d0 = prv - cur
        d1 = nxt - cur
        r = min(rad[i], 0.45 * np.linalg.norm(d0), 0.45 * np.linalg.norm(d1))
        a = cur + d0 / np.linalg.norm(d0) * r
        c = cur + d1 / np.linalg.norm(d1) * r
        for k in range(corner_steps):
            t = k / (corner_steps - 1)
            out.append((1 - t) ** 2 * a + 2 * (1 - t) * t * cur + t**2 * c)
        # straight run to the next corner, subdivided so it hugs the curved skin
        nxt_prev = cur
        nxt_cur = nxt
        nxt_next = pts[(i + 2) % n]
        rn = min(rad[(i + 1) % n], 0.45 * np.linalg.norm(nxt_prev - nxt_cur), 0.45 * np.linalg.norm(nxt_next - nxt_cur))
        a2 = nxt_cur + (nxt_prev - nxt_cur) / np.linalg.norm(nxt_prev - nxt_cur) * rn
        steps = max(int(np.linalg.norm(a2 - c) / seg_len), 1)
        for k in range(1, steps):
            out.append(c + (a2 - c) * k / steps)
    return np.array(out)


def drape_pane(b, outline, surf, mat="Glass", lift=0.005, sink=0.014, rings=6, tag="glass"):
    """Build a window pane draped on a surface. surf(u, v) -> (point Vector, outward normal).
    Pane sits `lift` above the skin; a rim drops `sink` below it so no gap shows."""
    centre = outline.mean(axis=0)
    ring_pts = []
    for k in range(rings):
        f = 1.0 - k / rings
        ring_pts.append([centre + (p - centre) * f for p in outline])

    def on(u, v, off):
        p, n = surf(u, v)
        return p + n * off, n

    M = len(outline)
    vrows = []
    for rp in ring_pts:
        vrows.append([b.add_vert(on(u, v, lift)[0]) for u, v in rp])
    cvert = b.add_vert(on(centre[0], centre[1], lift)[0])
    rim = [b.add_vert(on(u, v, -sink)[0]) for u, v in outline]

    def oriented(idx, ref_uv):
        pts = [Vector(b.verts[i]) for i in idx]
        nrm = (pts[1] - pts[0]).cross(pts[2] - pts[0])
        if len(pts) == 4:
            nrm += (pts[2] - pts[0]).cross(pts[3] - pts[0])
        _, sn = surf(*ref_uv)
        return idx if nrm.dot(sn) >= 0 else idx[::-1]

    for k in range(rings - 1):
        for j in range(M):
            j2 = (j + 1) % M
            q = [vrows[k][j], vrows[k][j2], vrows[k + 1][j2], vrows[k + 1][j]]
            b.add_face(oriented(q, ring_pts[k][j]), mat, None, tag)
    for j in range(M):
        j2 = (j + 1) % M
        b.add_face(oriented([vrows[-1][j], vrows[-1][j2], cvert], ring_pts[-1][j]), mat, None, tag)
    # rim: faces whose normal points outward from the pane centre (along the skin)
    for j in range(M):
        j2 = (j + 1) % M
        q = [rim[j], rim[j2], vrows[0][j2], vrows[0][j]]
        pa, pb = Vector(b.verts[rim[j]]), Vector(b.verts[rim[j2]])
        pc = Vector(b.verts[vrows[0][j]])
        nrm = (pb - pa).cross(pc - pa)
        c3, _ = surf(*centre)
        if nrm.dot(pa - c3) < 0:
            q = q[::-1]
        b.add_face(q, mat, None, tag + "_rim")


def _fd_normal(fn, u, v, ref_out, eps=1e-3):
    p = Vector(fn(u, v))
    du = Vector(fn(u + eps, v)) - Vector(fn(u - eps, v))
    dv = Vector(fn(u, v + eps)) - Vector(fn(u, v - eps))
    n = du.cross(dv).normalized()
    if n.dot(ref_out(p)) < 0:
        n = -n
    return p, n


def lifting_surface(b, stations, code, mat, tag, hinge=None, part="main", gap=0.012, te_thick=0.004,
                    n_af=22, closed_ends=True, flip=False):
    """Loft an airfoil along `stations`.

    Each station: dict(le=Vector, chord_dir=Vector (LE->TE unit), up=Vector (unit),
    c=float, h=hinge fraction or None, scale_t=thickness scale (default 1)).
    part="main": LE -> hinge (blunt spar face at the hinge) or LE -> TE if h is None.
    part="ctrl": hinge -> TE with a rounded nose.
    """
    af = airfoil_eval(code)
    rings = []
    for st in stations:
        c = st["c"]
        h = st.get("h")
        ts = st.get("scale_t", 1.0)
        le, cd, up = Vector(st["le"]), Vector(st["chord_dir"]), Vector(st["up"])
        if part == "main":
            xe = 1.0 if h is None else h - gap / (2 * c)
            beta = np.linspace(0, math.pi / 2, n_af)
            xs = xe * (1 - np.cos(beta))           # dense at the LE
            zu, zl, _ = af(xs)
            zu = zu * ts
            zl = zl * ts
            if h is None:                          # finite trailing edge thickness
                zu[-1] = max(zu[-1], te_thick / (2 * c))
                zl[-1] = min(zl[-1], -te_thick / (2 * c))
            ring = [le + cd * (x * c) + up * (z * c) for x, z in zip(xs, zu)]
            ring += [le + cd * (x * c) + up * (z * c) for x, z in zip(xs[::-1][:-1], zl[::-1][:-1])]
        else:
            x0 = h + gap / (2 * c)
            xs = np.linspace(x0, 1.0, n_af)
            zu, zl, zc = af(xs)
            zu = zu * ts
            zl = zl * ts
            zu[-1] = max(zu[-1], te_thick / (2 * c))
            zl[-1] = min(zl[-1], -te_thick / (2 * c))
            r = 0.5 * (zu[0] - zl[0])
            nose = []
            for k in range(1, 8):                  # semicircular nose, upper -> lower
                ang = math.pi / 2 + math.pi * k / 8
                nose.append((x0 + r * math.cos(ang) * 0.9, zc[0] + r * math.sin(ang)))
            upper = list(zip(xs, zu))
            lower = list(zip(xs[::-1], zl[::-1]))
            seq = upper + lower[1:-1] + [(xs[0], zl[0])] + nose
            # seq goes LE-top -> TE -> bottom -> back to nose; rotate so it starts at TE (seam on the TE)
            ring = [le + cd * (x * c) + up * (z * c) for x, z in seq]
        rings.append(ring)
    n = len(rings[0])
    base = b.add_verts([p for r in rings for p in r])
    for i in range(len(rings) - 1):
        for j in range(n):
            j2 = (j + 1) % n
            q = [base + i * n + j, base + i * n + j2, base + (i + 1) * n + j2, base + (i + 1) * n + j]
            b.add_face(q[::-1] if flip else q, mat, None, tag)
    if closed_ends:
        first = [base + j for j in range(n)]
        last = [base + (len(rings) - 1) * n + j for j in range(n)]
        b.add_face(first if flip else first[::-1], mat, None, tag + "_cap")
        b.add_face(last[::-1] if flip else last, mat, None, tag + "_cap")
    return rings


def revolve(b, profile, axis_origin, axis_dir, mat, tag, segs=24, cap_start=False, cap_end=False, flip=False, ref=None):
    """Surface of revolution. profile: list of (t along axis, radius)."""
    o = Vector(axis_origin)
    d = Vector(axis_dir).normalized()
    ref = Vector(ref or ((0, 0, 1) if abs(d.z) < 0.9 else (1, 0, 0)))
    e1 = d.cross(ref).normalized()
    e2 = d.cross(e1).normalized()
    rings = []
    for t, r in profile:
        ring = []
        for k in range(segs):
            a = 2 * math.pi * k / segs
            ring.append(o + d * t + (e1 * math.cos(a) + e2 * math.sin(a)) * r)
        rings.append(ring)
    base = b.add_verts([p for r in rings for p in r])
    for i in range(len(rings) - 1):
        for k in range(segs):
            k2 = (k + 1) % segs
            q = [base + i * segs + k, base + i * segs + k2, base + (i + 1) * segs + k2, base + (i + 1) * segs + k]
            b.add_face(q[::-1] if flip else q, mat, None, tag)
    if cap_start:
        f = [base + k for k in range(segs)]
        b.add_face(f if flip else f[::-1], mat, None, tag)
    if cap_end:
        f = [base + (len(rings) - 1) * segs + k for k in range(segs)]
        b.add_face(f[::-1] if flip else f, mat, None, tag)
    return rings


def tube(b, path, radius_fn, mat, tag, segs=12, cap=True, ref=(0, 0, 1), squash=1.0):
    """Tube along a polyline; radius_fn(t) with t 0..1. squash scales one axis."""
    path = [Vector(p) for p in path]
    n = len(path)
    refv = Vector(ref)
    rings = []
    for i, p in enumerate(path):
        tan = (path[min(i + 1, n - 1)] - path[max(i - 1, 0)]).normalized()
        e1 = tan.cross(refv)
        if e1.length < 1e-5:
            e1 = tan.cross(Vector((1, 0, 0)))
        e1.normalize()
        e2 = tan.cross(e1).normalized()
        r = radius_fn(i / (n - 1))
        rings.append([p + (e1 * math.cos(2 * math.pi * k / segs) * squash + e2 * math.sin(2 * math.pi * k / segs)) * r
                      for k in range(segs)])
    base = b.add_verts([q for r in rings for q in r])
    for i in range(n - 1):
        for k in range(segs):
            k2 = (k + 1) % segs
            q = [base + i * segs + k, base + i * segs + k2, base + (i + 1) * segs + k2, base + (i + 1) * segs + k]
            b.add_face(q, mat, None, tag)
    if cap:
        b.add_face([base + k for k in range(segs)][::-1], mat, None, tag)
        b.add_face([base + (n - 1) * segs + k for k in range(segs)], mat, None, tag)


def wheel(b, centre, r_out, width, tag):
    """Tyre (torus-ish) + hub, axle along X."""
    c = Vector(centre)
    tube_r = width / 2
    R0 = r_out - tube_r
    segs, tsegs = 28, 12
    rings = []
    for k in range(segs):
        a = 2 * math.pi * k / segs
        ring = []
        for m in range(tsegs):
            t = 2 * math.pi * m / tsegs
            rr = R0 + tube_r * math.cos(t) * 1.0
            ring.append(c + Vector((tube_r * 0.95 * math.sin(t), rr * math.cos(a), rr * math.sin(a))))
        rings.append(ring)
    base = b.add_verts([p for r in rings for p in r])
    for k in range(segs):
        k2 = (k + 1) % segs
        for m in range(tsegs):
            m2 = (m + 1) % tsegs
            q = [base + k * tsegs + m, base + k2 * tsegs + m, base + k2 * tsegs + m2, base + k * tsegs + m2]
            b.add_face(q, "Rubber", None, tag)
    for sx in (1, -1):
        revolve(b, [(sx * (tube_r * 0.8), R0 * 0.98), (sx * tube_r * 0.95, R0 * 0.55), (sx * tube_r * 0.98, 0.0005)],
                c, (1, 0, 0), "Metal", tag, segs=20, flip=(sx < 0), ref=(0, 0, 1))
