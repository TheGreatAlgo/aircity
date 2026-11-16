"""Signed distance primitives, smooth blends and a narrow-band surface-nets mesher (numpy only).

Every function takes points as an (N, 3) float array and returns (N,) distances, negative inside.
Shapes are approximate distance bounds; they are meshed, projected back onto the zero set and then
decimated by Blender, so only the zero set and a sane gradient matter.
"""
import math

import numpy as np


def _v(a):
    return np.asarray(a, dtype=np.float64)


def norm(v):
    return np.sqrt(v[..., 0] * v[..., 0] + v[..., 1] * v[..., 1] + v[..., 2] * v[..., 2])


def dot3(q, v):
    """Row-wise dot with a constant 3-vector, without BLAS (Blender's numpy matmul is slow for N x 3)."""
    return q[:, 0] * v[0] + q[:, 1] * v[1] + q[:, 2] * v[2]


def rotate(q, m):
    m = np.asarray(m)
    return np.stack([dot3(q, m[0]), dot3(q, m[1]), dot3(q, m[2])], 1)


# ---------------------------------------------------------------- combinators
def smin(a, b, k):
    if callable(a):
        return lambda p: smin(a(p), b(p), k)
    if k <= 0:
        return np.minimum(a, b)
    h = np.clip(.5 + .5 * (b - a) / k, 0, 1)
    return b * (1 - h) + a * h - k * h * (1 - h)


def smax(a, b, k):
    if callable(a):
        return lambda p: smax(a(p), b(p), k)
    return -smin(-a, -b, k)


def union(*parts, k=0.0):
    def f(p):
        d = parts[0](p)
        for part in parts[1:]:
            d = smin(d, part(p), k)
        return d
    return f


def blend(base, *parts):
    """Smooth union where every part carries its own blend radius: blend(base, (shape, k), ...)."""
    def f(p):
        d = base(p)
        for part, k in parts:
            d = smin(d, part(p), k)
        return d
    return f


def subtract(base, cut, k=0.0):
    return lambda p: smax(base(p), -cut(p), k)


def intersect(a, b, k=0.0):
    return lambda p: smax(a(p), b(p), k)


def offset(shape, amount):
    return lambda p: shape(p) - amount


# ---------------------------------------------------------------- primitives
def sphere(c, r):
    c = _v(c)
    return lambda p: norm(p - c) - r


def ellipsoid(c, r, rotation=None):
    """Bound by Inigo Quilez; rotation is a 3x3 matrix taking world to local axes."""
    c, r = _v(c), _v(r)
    def f(p):
        q = p - c
        if rotation is not None:
            q = rotate(q, rotation)
        k0 = norm(q / r)
        k1 = norm(q / (r * r))
        return k0 * (k0 - 1) / np.maximum(k1, 1e-9)
    return f


def round_cone(a, b, r1, r2):
    """Exact distance to a cone capped with spheres of radius r1 at a and r2 at b."""
    a, b = _v(a), _v(b)
    ba = b - a
    l2 = float(ba @ ba)
    rr = r1 - r2
    a2 = l2 - rr * rr
    il2 = 1.0 / l2
    def f(p):
        pa = p - a
        y = dot3(pa, ba)
        z = y - l2
        x = pa * l2 - y[:, None] * ba
        x2 = np.sum(x * x, axis=1)
        y2 = y * y * l2
        z2 = z * z * l2
        k = math.copysign(1, rr) * rr * rr * x2
        d_end = np.sqrt(x2 + z2) * il2 - r2
        d_start = np.sqrt(x2 + y2) * il2 - r1
        d_side = (np.sqrt(x2 * a2 * il2) + y * rr) * il2 - r1
        return np.where(np.sign(z) * a2 * z2 > k, d_end, np.where(np.sign(y) * a2 * y2 < k, d_start, d_side))
    return f


def capsule(a, b, r):
    return round_cone(a, b, r, r)


def round_box(c, half, r, rotation=None):
    c, half = _v(c), _v(half)
    def f(p):
        q = p - c
        if rotation is not None:
            q = rotate(q, rotation)
        q = np.abs(q) - half + r
        return norm(np.maximum(q, 0)) + np.minimum(np.max(q, axis=1), 0) - r
    return f


def plane(point, normal):
    """Half space; negative on the side opposite the normal."""
    point, normal = _v(point), _v(normal)
    normal = normal / np.linalg.norm(normal)
    return lambda p: dot3(p - point, normal)


# ---------------------------------------------------------------- lofts
def _pchip(x, xs, ys):
    """Monotone cubic (Fritsch-Carlson) interpolation of columns ys[:, j] at x; clamps outside."""
    xs = np.asarray(xs, float); ys = np.asarray(ys, float)
    if ys.ndim == 1:
        ys = ys[:, None]
    n = len(xs)
    h = np.diff(xs)
    delta = np.diff(ys, axis=0) / h[:, None]
    m = np.zeros_like(ys)
    if n > 2:
        w1 = 2 * h[1:] + h[:-1]; w2 = h[1:] + 2 * h[:-1]
        d0, d1 = delta[:-1], delta[1:]
        same = (d0 * d1) > 0
        with np.errstate(divide="ignore", invalid="ignore"):
            harmonic = (w1 + w2)[:, None] / (w1[:, None] / d0 + w2[:, None] / d1)
        m[1:-1] = np.where(same, harmonic, 0)
    m[0] = delta[0]; m[-1] = delta[-1]
    x = np.clip(x, xs[0], xs[-1])
    i = np.clip(np.searchsorted(xs, x) - 1, 0, n - 2)
    t = ((x - xs[i]) / h[i])[:, None]
    hi = h[i][:, None]
    t2, t3 = t * t, t * t * t
    return ((2 * t3 - 3 * t2 + 1) * ys[i] + (t3 - 2 * t2 + t) * hi * m[i] +
            (-2 * t3 + 3 * t2) * ys[i + 1] + (t3 - t2) * hi * m[i + 1])


def frame(a, b, up):
    a, b, up = _v(a), _v(b), _v(up)
    axis = b - a
    length = np.linalg.norm(axis)
    u = axis / length
    v = up - u * (up @ u)
    v /= np.linalg.norm(v)
    w = np.cross(u, v)
    return a, u, v, w, length


def loft(a, b, up, stations, caps=(True, True)):
    """Superelliptic sections along a->b.

    stations: rows (t, rv, rw, ov, ow, n): t in [0, 1] along the segment, half widths along v (=up
    projected) and w (= u x v), the section centre offset, and the superellipse exponent (2 = ellipse).
    Ends are flat at the first and last station unless capped by other shapes.
    """
    a, u, v, w, length = frame(a, b, up)
    stations = np.asarray(stations, float)
    ts = stations[:, 0]
    def f(p):
        q = p - a
        s = dot3(q, u)
        t = s / length
        params = _pchip(t, ts, stations[:, 1:])
        rv, rw, ov, ow, n = (params[:, k] for k in range(5))
        x = dot3(q, v) - ov
        y = dot3(q, w) - ow
        ax = np.abs(x) / rv + 1e-12
        ay = np.abs(y) / rw + 1e-12
        g = (ax ** n + ay ** n) ** (1 / n)
        gx = g ** (1 - n) * ax ** (n - 1) / rv
        gy = g ** (1 - n) * ay ** (n - 1) / rw
        d = (g - 1) / np.maximum(np.sqrt(gx * gx + gy * gy), 1e-9)
        if caps[0]:
            d = np.maximum(d, (ts[0] - t) * length)
        if caps[1]:
            d = np.maximum(d, (t - ts[-1]) * length)
        return d
    return f


def loft2(a, b, up, stations, caps=(True, True)):
    """Like loft, but the half width along +v and -v may differ (egg-shaped sections).

    stations: rows (t, rv_front, rv_back, rw, ov, n); ov is where the two halves meet along v.
    """
    a, u, v, w, length = frame(a, b, up)
    stations = np.asarray(stations, float)
    ts = stations[:, 0]
    def f(p):
        q = p - a
        t = dot3(q, u) / length
        params = _pchip(t, ts, stations[:, 1:])
        rf, rb, rw, ov, n = (params[:, k] for k in range(5))
        x = dot3(q, v) - ov
        y = dot3(q, w)
        rv = np.where(x > 0, rf, rb)
        ax = np.abs(x) / rv + 1e-12
        ay = np.abs(y) / rw + 1e-12
        g = (ax ** n + ay ** n) ** (1 / n)
        gx = g ** (1 - n) * ax ** (n - 1) / rv
        gy = g ** (1 - n) * ay ** (n - 1) / rw
        d = (g - 1) / np.maximum(np.sqrt(gx * gx + gy * gy), 1e-9)
        if caps[0]:
            d = np.maximum(d, (ts[0] - t) * length)
        if caps[1]:
            d = np.maximum(d, (t - ts[-1]) * length)
        return d
    return f


# ---------------------------------------------------------------- sampling and meshing
def gradient(shape, p, eps=2e-4):
    g = np.empty_like(p)
    for axis in range(3):
        e = np.zeros(3); e[axis] = eps
        g[:, axis] = (shape(p + e) - shape(p - e)) / (2 * eps)
    return g


def evaluate(shape, p, chunk=400000):
    out = np.empty(len(p))
    for start in range(0, len(p), chunk):
        out[start:start + chunk] = shape(p[start:start + chunk])
    return out


def sample(shape, lo, hi, h, block=8):
    """Dense grid with exact values only in a narrow band; elsewhere a trilinear coarse estimate."""
    lo = _v(lo); hi = _v(hi)
    counts = np.ceil((hi - lo) / h).astype(int) + 1
    counts = ((counts - 1 + block - 1) // block) * block + 1
    coarse_counts = (counts - 1) // block + 1
    H = h * block
    axes = [lo[i] + H * np.arange(coarse_counts[i]) for i in range(3)]
    grid = np.stack(np.meshgrid(*axes, indexing="ij"), -1).reshape(-1, 3)
    coarse = evaluate(shape, grid).reshape(coarse_counts)
    # A coarse cell may contain surface if any corner is within reach of the far corner (1.8 slack for bounds).
    reach = H * math.sqrt(3) * 1.8
    near = np.abs(coarse) < reach
    cells = (near[:-1, :-1, :-1] | near[1:, :-1, :-1] | near[:-1, 1:, :-1] | near[:-1, :-1, 1:] |
             near[1:, 1:, :-1] | near[1:, :-1, 1:] | near[:-1, 1:, 1:] | near[1:, 1:, 1:])
    # Trilinear upsample of the coarse field fills the far region with correctly signed values.
    F = coarse
    for axis in range(3):
        n = F.shape[axis]
        x = np.arange((n - 1) * block + 1) / block
        i0 = np.minimum(np.floor(x).astype(int), n - 2)
        t = x - i0
        shape_t = [1, 1, 1]; shape_t[axis] = -1
        t = t.reshape(shape_t)
        F = np.take(F, i0, axis=axis) * (1 - t) + np.take(F, i0 + 1, axis=axis) * t
    F = F.astype(np.float32)
    local = np.stack(np.meshgrid(*[np.arange(block + 1)] * 3, indexing="ij"), -1).reshape(-1, 3)
    chosen = np.argwhere(cells)
    batch = max(1, 300000 // len(local))
    for start in range(0, len(chosen), batch):
        part = chosen[start:start + batch]
        idx = (part[:, None, :] * block + local[None, :, :]).reshape(-1, 3)
        idx = np.unique(idx, axis=0)
        values = evaluate(shape, lo + idx * h)
        F[idx[:, 0], idx[:, 1], idx[:, 2]] = values
    return F, lo, h


def surface_nets(F, lo, h):
    """Quad mesh of the zero set of a sampled field (naive surface nets). Faces wind outwards."""
    inside = F < 0
    nx, ny, nz = F.shape
    corners = [(i, j, k) for i in (0, 1) for j in (0, 1) for k in (0, 1)]
    count = np.zeros((nx - 1, ny - 1, nz - 1), np.uint8)
    for i, j, k in corners:
        count += inside[i:nx - 1 + i, j:ny - 1 + j, k:nz - 1 + k]
    mixed = (count > 0) & (count < 8)
    cells = np.argwhere(mixed)
    vid = np.full(mixed.shape, -1, np.int64)
    vid[cells[:, 0], cells[:, 1], cells[:, 2]] = np.arange(len(cells))
    # Average of the 12 edge crossings in each mixed cell.
    acc = np.zeros((len(cells), 3)); hits = np.zeros(len(cells))
    ci, cj, ck = cells[:, 0], cells[:, 1], cells[:, 2]
    for c0, c1 in ((a, b) for a in corners for b in corners if sum(abs(x - y) for x, y in zip(a, b)) == 1 and a < b):
        f0 = F[ci + c0[0], cj + c0[1], ck + c0[2]]
        f1 = F[ci + c1[0], cj + c1[1], ck + c1[2]]
        crossing = (f0 < 0) != (f1 < 0)
        t = np.where(crossing, f0 / np.where(f0 - f1 == 0, 1, f0 - f1), 0)
        point = np.array(c0, float)[None, :] + t[:, None] * (np.array(c1, float) - np.array(c0, float))[None, :]
        acc += point * crossing[:, None]; hits += crossing
    verts = lo + (cells + acc / np.maximum(hits, 1)[:, None]) * h
    quads = []
    for axis in range(3):
        o1, o2 = [a for a in range(3) if a != axis]
        sl0 = [slice(None)] * 3; sl1 = [slice(None)] * 3
        sl0[axis] = slice(0, -1); sl1[axis] = slice(1, None)
        a_in = inside[tuple(sl0)]; b_in = inside[tuple(sl1)]
        change = a_in != b_in
        # Edges on the boundary of the grid have fewer than four cells.
        lim = [slice(None)] * 3
        lim[o1] = slice(1, None); lim[o2] = slice(1, None)
        mask = np.zeros_like(change); mask[tuple(lim)] = True
        edges = np.argwhere(change & mask)
        flip = a_in[edges[:, 0], edges[:, 1], edges[:, 2]]
        def cell(d1, d2):
            c = edges.copy(); c[:, o1] -= d1; c[:, o2] -= d2
            c = np.minimum(c, np.array(mixed.shape) - 1)
            return vid[c[:, 0], c[:, 1], c[:, 2]]
        q = np.stack([cell(1, 1), cell(0, 1), cell(0, 0), cell(1, 0)], 1)
        if axis == 1:
            flip = ~flip
        q[~flip] = q[~flip][:, ::-1]
        quads.append(q)
    quads = np.concatenate(quads)
    quads = quads[(quads >= 0).all(1)]
    return verts, quads


def project(shape, verts, steps=2, eps=2e-4):
    """Newton steps onto the zero set for crisp features after surface nets' cell averaging."""
    v = verts.copy()
    for _ in range(steps):
        d = evaluate(shape, v)
        g = gradient(shape, v, eps)
        gg = np.maximum(np.sum(g * g, 1), 1e-6)
        step = (d / gg)[:, None] * g
        # Never move further than a cell: bounds far from the surface are unreliable.
        lengths = norm(step)
        step *= np.minimum(1, 0.004 / np.maximum(lengths, 1e-12))[:, None]
        v -= step
    return v


def mesh(shape, lo, hi, h, block=8, steps=2):
    F, lo, h = sample(shape, lo, hi, h, block)
    verts, quads = surface_nets(F, lo, h)
    verts = project(shape, verts, steps)
    return verts, quads
