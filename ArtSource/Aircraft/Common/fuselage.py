"""Superellipse fuselages lofted from PCHIP profiles, with analytic metric UVs.

A model subclasses Fuselage and sets the profile curves (functions of station s, metres aft of the nose):
zt/zb = top/bottom height, hw = half width, fm = height of the widest line as a fraction of zt - zb,
nt/nb = superellipse exponents of the upper/lower halves (2 = ellipse, larger = boxier).
"""
import math

import numpy as np

from geom import pchip, resample_polyline


def profile(pts):
    a = np.array(pts, float)
    return pchip(a[:, 0], a[:, 1])


class Fuselage:
    S0 = 0.0
    S1 = 1.0
    ML = 11       # verts per lower quarter (widest line -> bottom)
    MU = 14       # verts per upper quarter (widest line -> top)
    zt = zb = hw = fm = nt = nb = None

    @classmethod
    def sec(cls, s):
        zt, zb = float(cls.zt(s)), float(cls.zb(s))
        return dict(zt=zt, zb=zb, hw=float(cls.hw(s)), zm=zb + float(cls.fm(s)) * (zt - zb),
                    nt=float(cls.nt(s)), nb=float(cls.nb(s)))

    @classmethod
    def zm(cls, s):
        return cls.zb(s) + cls.fm(s) * (cls.zt(s) - cls.zb(s))

    @classmethod
    def quarter(cls, s, upper, count):
        """Port quarter from the widest point to the top/bottom centre, resampled to `count`
        points by arc length. Returns (pts(x,z), arc)."""
        c = cls.sec(s)
        n = c["nt"] if upper else c["nb"]
        t = np.linspace(0.0, math.pi / 2, 900)
        x = c["hw"] * np.cos(t) ** (2.0 / n)
        dz = (c["zt"] - c["zm"]) if upper else (c["zm"] - c["zb"])
        z = c["zm"] + (1 if upper else -1) * dz * np.sin(t) ** (2.0 / n)
        x[-1] = 0.0
        return resample_polyline(np.stack([x, z], 1), count)

    @classmethod
    def port_profile(cls, s):
        """Port half, bottom centre -> top centre. Returns (pts(x,z), a) where a is the signed
        arc length from the widest line (up positive)."""
        lo, alo = cls.quarter(s, False, cls.ML + 1)
        up, aup = cls.quarter(s, True, cls.MU + 1)
        pts = np.concatenate([lo[::-1], up[1:]])
        a = np.concatenate([-alo[::-1], aup[1:]])
        return pts, a

    # --- analytic surface lookups used to drape windows / details on the skin
    @classmethod
    def side_x(cls, s, z):
        c = cls.sec(s)
        if z >= c["zm"]:
            r = min(abs(z - c["zm"]) / (c["zt"] - c["zm"]), 1.0)
            n = c["nt"]
        else:
            r = min(abs(z - c["zm"]) / (c["zm"] - c["zb"]), 1.0)
            n = c["nb"]
        return c["hw"] * max(1.0 - r**n, 0.0) ** (1.0 / n)

    @classmethod
    def top_z(cls, s, x):
        c = cls.sec(s)
        q = min(abs(x) / c["hw"], 1.0)
        return c["zm"] + (c["zt"] - c["zm"]) * max(1.0 - q ** c["nt"], 0.0) ** (1.0 / c["nt"])

    @classmethod
    def ring_point(cls, s, phi):
        """(x, z) of the skin at station s, angle phi (radians) from the top centre towards port (+x).
        Windows that wrap around a corner of the nose are easiest to lay out in (s, phi)."""
        c = cls.sec(s)
        sp, cp = math.sin(phi), math.cos(phi)
        n = c["nt"] if cp >= 0 else c["nb"]
        a = (c["zt"] - c["zm"]) if cp >= 0 else (c["zm"] - c["zb"])
        r = ((abs(sp) / c["hw"]) ** n + (abs(cp) / a) ** n) ** (-1.0 / n)
        return r * sp, c["zm"] + r * cp

    @classmethod
    def bottom_z(cls, s, x):
        c = cls.sec(s)
        q = min(abs(x) / c["hw"], 1.0)
        return c["zm"] - (c["zm"] - c["zb"]) * max(1.0 - q ** c["nb"], 0.0) ** (1.0 / c["nb"])


class Loft:
    """The fuselage skin as two UV halves (tags fus_port / fus_stbd). UVs are metric: u = +-station,
    v = arc length from the widest line, which the registration patch relies on."""

    def __init__(self, F, b, stations, P):
        self.F, self.b, self.st, self.P = F, b, list(stations), P
        self.H = F.ML + F.MU            # port vertex span (bottom..top) = H+1 verts
        self.N = 2 * self.H             # verts per ring
        self.rings, self.arcs = [], []
        for s in self.st:
            pts, a = F.port_profile(s)
            ring = [P(x, s, z) for x, z in pts]                          # j = 0..H (port)
            ring += [P(-x, s, z) for x, z in pts[1:-1][::-1]]            # j = H+1..N-1 (starboard, top->bottom)
            self.rings.append(ring)
            self.arcs.append(a)
        self.base = b.add_verts([p for r in self.rings for p in r])
        for i in range(len(self.st) - 1):
            for j in range(self.N):
                sgn = 1.0 if self.port(j) else -1.0
                quad = [self.vid(i, j), self.vid(i + 1, j), self.vid(i + 1, j + 1), self.vid(i, j + 1)]
                uvs = [(sgn * self.st[i], self.a(i, j)), (sgn * self.st[i + 1], self.a(i + 1, j)),
                       (sgn * self.st[i + 1], self.a(i + 1, j + 1)), (sgn * self.st[i], self.a(i, j + 1))]
                b.add_face(quad, "Body", uvs, self.tag(j))

    def port(self, j):
        return (j % self.N) < self.H

    def tag(self, j):
        return "fus_port" if self.port(j) else "fus_stbd"

    def vid(self, i, j):
        return self.base + i * self.N + (j % self.N)

    def a(self, i, j):
        j %= self.N
        return self.arcs[i][j] if j <= self.H else self.arcs[i][self.N - j]

    def fan_cap(self, i, tip, mat="Body", tag=None, s_tip=None):
        """Close ring i with a fan to the point `tip`. UVs continue the halves' metric mapping."""
        b = self.b
        c = b.add_vert(tip)
        s = self.st[i]
        s_tip = s + (0.03 if i else -0.03) if s_tip is None else s_tip
        for j in range(self.N):
            sgn = 1.0 if self.port(j) else -1.0
            uvs = [(sgn * s_tip, 0.0), (sgn * s, self.a(i, j + 1)), (sgn * s, self.a(i, j))]
            face = [c, self.vid(i, j + 1), self.vid(i, j)]
            if i == 0:
                face, uvs = face[::-1], uvs[::-1]
            b.add_face(face, mat, uvs if mat == "Body" else None, tag or self.tag(j))
