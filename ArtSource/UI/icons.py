"""Tool icons for the bottom toolbar: flat white pictograms on a clear background, tinted by the HUD when drawn
(DevGui.IconButton). Plain Python 3 (Pillow), no Blender. See ArtSource/README.md.

    python3 ArtSource/UI/icons.py

Each icon is drawn on a 128-unit grid at 4x and scaled down, so edges come out smooth. Shapes are drawn into an
alpha mask: fill() adds, cut() takes away (white lines inside a shape, such as a runway's centreline, are cuts).
Writes Assets/AirCity/UI/Resources/AirCityIcons/<name>.png (Resources.Load("AirCityIcons/<name>")).
"""
import math
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "AirCity", "UI", "Resources", "AirCityIcons")
SIZE = 128
SS = 4   # supersampling


class Icon:
    """An alpha mask drawn in 0..128 units."""

    def __init__(self):
        self.mask = Image.new("L", (SIZE * SS, SIZE * SS), 0)
        self.draw = ImageDraw.Draw(self.mask)

    @staticmethod
    def _p(points):
        return [(x * SS, y * SS) for x, y in points]

    def polygon(self, points, on=True):
        self.draw.polygon(self._p(points), fill=255 if on else 0)

    def rect(self, x0, y0, x1, y1, radius=0, on=True):
        self.draw.rounded_rectangle([x0 * SS, y0 * SS, x1 * SS, y1 * SS], radius=radius * SS, fill=255 if on else 0)

    def circle(self, x, y, r, on=True):
        self.draw.ellipse([(x - r) * SS, (y - r) * SS, (x + r) * SS, (y + r) * SS], fill=255 if on else 0)

    def ring(self, x, y, r, width, on=True):
        self.draw.ellipse([(x - r) * SS, (y - r) * SS, (x + r) * SS, (y + r) * SS], outline=255 if on else 0,
                          width=int(width * SS))

    def line(self, points, width, on=True, round_caps=True):
        """A stroke along points, with round joins (and caps)."""
        pts = self._p(points)
        self.draw.line(pts, fill=255 if on else 0, width=int(width * SS), joint="curve")
        if round_caps:
            for x, y in points:
                self.circle(x, y, width / 2, on)

    def path(self, points, width, on=True):
        """A smooth band along a densely sampled path (circles stamped along it)."""
        for (x0, y0), (x1, y1) in zip(points, points[1:]):
            n = max(1, int(math.hypot(x1 - x0, y1 - y0) * 2))
            for i in range(n + 1):
                t = i / n
                self.circle(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, width / 2, on)

    def dashes(self, points, width, dash, gap, on=False):
        """Dashes along a sampled path: `dash` long, `gap` apart."""
        run, drawing, seg = 0.0, True, [points[0]]
        for (x0, y0), (x1, y1) in zip(points, points[1:]):
            step = math.hypot(x1 - x0, y1 - y0)
            run += step
            seg.append((x1, y1))
            if run >= (dash if drawing else gap):
                if drawing:
                    self.path(seg, width, on)
                drawing, run, seg = not drawing, 0.0, [(x1, y1)]
        if drawing and len(seg) > 1:
            self.path(seg, width, on)

    def save(self, name):
        alpha = self.mask.resize((SIZE, SIZE), Image.LANCZOS)
        img = Image.new("RGBA", (SIZE, SIZE), (255, 255, 255, 0))
        img.putalpha(alpha)
        os.makedirs(OUT, exist_ok=True)
        img.save(os.path.join(OUT, name + ".png"))


def bezier(p0, p1, p2, p3, n=64):
    out = []
    for i in range(n + 1):
        t = i / n
        u = 1 - t
        out.append((u ** 3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t ** 3 * p3[0],
                    u ** 3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t ** 3 * p3[1]))
    return out


def lerp(a, b, t):
    return a + (b - a) * t


# ============================================================== the icons

def select():
    i = Icon()
    arrow = [(34, 14), (34, 98), (54, 80), (68, 112), (84, 105), (70, 74), (96, 74)]
    i.polygon(arrow)
    return i


def runway():
    """A runway seen down its length: piano keys at the threshold, centreline dashes."""
    i = Icon()
    near, far, y0, y1 = (20, 108), (46, 82), 116, 12
    i.polygon([(near[0], y0), (near[1], y0), (far[1], y1), (far[0], y1)])

    def across(t, u):   # t: 0 near .. 1 far; u: 0 left .. 1 right
        return lerp(lerp(near[0], far[0], t), lerp(near[1], far[1], t), u), lerp(y0, y1, t)

    # threshold piano keys
    for k in range(6):
        u0, u1 = 0.08 + k * 0.145, 0.08 + k * 0.145 + 0.08
        if 0.42 < u0 < 0.58 or 0.42 < u1 < 0.58:
            continue
        i.polygon([across(0.05, u0), across(0.05, u1), across(0.2, u1), across(0.2, u0)], on=False)
    # centreline
    for t0, t1 in ((0.28, 0.42), (0.5, 0.62), (0.7, 0.8), (0.86, 0.93)):
        w = 0.035
        i.polygon([across(t0, 0.5 - w), across(t0, 0.5 + w), across(t1, 0.5 + w), across(t1, 0.5 - w)], on=False)
    return i


def taxiway():
    """A taxiway turning off: a band with its centreline cut."""
    i = Icon()
    pts = [(40, 124), (40, 86)] + bezier((40, 86), (40, 44), (60, 30), (124, 30))[1:]
    i.path(pts, 34)
    i.path(pts, 4.5, on=False)
    return i


def road():
    """A road winding away, its lanes dashed."""
    i = Icon()
    pts = bezier((24, 126), (24, 72), (104, 76), (100, 6))
    i.path(pts, 36)
    i.dashes(pts, 5, 11, 9)
    return i


def apron():
    """An apron with three parking lead-in lines and their stop bars."""
    i = Icon()
    i.rect(8, 22, 120, 106, radius=10)
    for x in (34, 64, 94):
        i.line([(x, 96), (x, 48)], 5, on=False)
        i.line([(x - 10, 40), (x + 10, 40)], 5, on=False)
    return i


def terminal():
    """A terminal building: a wave roof over glass."""
    i = Icon()
    roof = [(6, 50)] + bezier((6, 50), (40, 18), (88, 18), (122, 50))[1:] + [(122, 58), (6, 58)]
    i.polygon(roof)
    i.rect(14, 62, 114, 112, radius=3)
    for row in (0, 1):
        for col in range(5):
            x = 22 + col * 18
            y = 70 + row * 18
            if row == 1 and col == 2:
                continue
            i.rect(x, y, x + 12, y + 12, radius=1.5, on=False)
    i.rect(56, 90, 72, 112, on=False)   # doors
    i.rect(2, 112, 126, 118, radius=2)  # ground
    return i


def depot():
    """A vehicle shed with a roller door."""
    i = Icon()
    i.polygon([(10, 52), (64, 16), (118, 52), (118, 114), (10, 114)])
    i.rect(30, 62, 98, 114, on=False)
    for y in (70, 82, 94, 106):
        i.rect(34, y, 94, y + 5)
    return i


def plane(i, cx, top, s):
    """A generic twin-jet from above, nose up, `s` = its length in units."""
    def P(x, y):
        return cx + x * s, top + y * s

    fus_w = 0.055
    i.rect(*P(-fus_w, 0.04), *P(fus_w, 0.97), radius=fus_w * s)
    i.circle(*P(0, 0.06), fus_w * s)
    i.polygon([P(-fus_w, 0.36), P(-0.5, 0.6), P(-0.5, 0.66), P(-fus_w, 0.55),
               P(fus_w, 0.55), P(0.5, 0.66), P(0.5, 0.6), P(fus_w, 0.36)])
    i.polygon([P(-fus_w, 0.82), P(-0.2, 0.95), P(-0.2, 0.99), P(0, 0.95),
               P(0.2, 0.99), P(0.2, 0.95), P(fus_w, 0.82)])
    for x in (-0.25, 0.25):   # engines under the wings
        i.rect(*P(x - 0.035, 0.44), *P(x + 0.035, 0.56), radius=0.03 * s)


def stand():
    """An aircraft on its stand: lead-in line and stop bar."""
    i = Icon()
    plane(i, 64, 14, 96)
    i.rect(30, 6, 98, 10, radius=2)        # stop bar ahead of the nose
    i.rect(62, 112, 66, 126)                # lead-in line behind
    return i


def deice():
    """A snowflake with a spray drop."""
    i = Icon()
    cx, cy, r = 56, 58, 46
    for k in range(3):
        a = math.radians(90 + k * 60)
        dx, dy = math.cos(a) * r, math.sin(a) * r
        i.line([(cx - dx, cy - dy), (cx + dx, cy + dy)], 8)
        for sign in (-1, 1):
            for along in (0.62,):
                bx, by = cx + dx * along * sign, cy + dy * along * sign
                for turn in (-1, 1):
                    b = a + (math.pi if sign < 0 else 0) + turn * math.radians(45)
                    i.line([(bx, by), (bx + math.cos(b) * 16, by + math.sin(b) * 16)], 7)
    i.circle(cx, cy, 10, on=False)
    i.ring(cx, cy, 10, 7)
    # drop
    i.circle(102, 104, 16, on=False)
    i.polygon([(102, 74), (114, 98), (90, 98)])
    i.circle(102, 102, 12.5)
    return i


def bridge():
    """A jet bridge from the side: terminal wall, sloping tunnel, its wheeled leg and the cab."""
    i = Icon()
    i.rect(4, 14, 30, 122, radius=3)                      # terminal wall
    i.polygon([(30, 34), (94, 46), (94, 72), (30, 60)])   # tunnel
    i.rect(90, 38, 124, 80, radius=5)                     # cab
    i.rect(98, 46, 116, 58, radius=2, on=False)           # its window
    i.rect(72, 66, 82, 104)                               # leg
    i.rect(60, 102, 94, 110, radius=3)                    # axle
    i.circle(64, 112, 9)
    i.circle(90, 112, 9)
    return i


def gates():
    """Gate design: a set square and a pencil."""
    i = Icon()
    i.polygon([(10, 118), (10, 22), (106, 118)])
    i.polygon([(26, 102), (26, 62), (66, 102)], on=False)
    # pencil, laid diagonally across
    a = math.radians(-45)
    ux, uy = math.cos(a), math.sin(a)          # along, towards the top right
    vx, vy = -uy, ux                           # across
    x0, y0, length, half = 58, 98, 66, 9

    def P(t, s):
        return x0 + ux * t + vx * s, y0 + uy * t + vy * s

    i.polygon([P(-2, -half - 5), P(length + 2, -half - 5), P(length + 2, half + 5), P(-2, half + 5)], on=False)
    i.polygon([P(14, -half), P(length, -half), P(length, half), P(14, half)])
    i.polygon([P(0, 0), P(14, -half), P(14, half)])
    i.line([P(length - 12, -half), P(length - 12, half)], 3.5, on=False, round_caps=False)
    return i


def lights():
    """An airfield edge light: a domed lens on a short frangible post, shining."""
    i = Icon()
    cx = 64
    i.polygon([(cx - 26, 70)] + bezier((cx - 26, 70), (cx - 26, 36), (cx + 26, 36), (cx + 26, 70))[1:])   # lens dome
    i.rect(cx - 30, 70, cx + 30, 80, radius=3)   # its rim
    i.rect(cx - 7, 80, cx + 7, 108)              # post
    i.rect(cx - 24, 106, cx + 24, 116, radius=3)   # base
    for deg in (-150, -120, -90, -60, -30):      # rays
        a = math.radians(deg)
        r0, r1 = 40, 58
        i.line([(cx + math.cos(a) * r0, 62 + math.sin(a) * r0), (cx + math.cos(a) * r1, 62 + math.sin(a) * r1)], 7)
    return i


def delete():
    """A bin."""
    i = Icon()
    i.rect(22, 24, 106, 34, radius=3)
    i.rect(50, 12, 78, 30, radius=4)
    i.rect(57, 18, 71, 24, on=False)
    i.polygon([(30, 40), (98, 40), (90, 118), (38, 118)])
    for x in (48, 64, 80):
        i.rect(x - 3, 52, x + 3, 106, radius=3, on=False)
    return i


ICONS = {
    "select": select, "runway": runway, "taxiway": taxiway, "road": road, "apron": apron, "terminal": terminal,
    "depot": depot, "lights": lights, "stand": stand, "deice": deice, "bridge": bridge, "gates": gates, "delete": delete,
}


def main():
    for name, make in ICONS.items():
        make().save(name)
        print("  " + name)
    # a contact sheet to look at (gitignored Build/)
    sheet_dir = os.path.join(HERE, "Build")
    os.makedirs(sheet_dir, exist_ok=True)
    names = list(ICONS)
    sheet = Image.new("RGBA", (len(names) * (SIZE + 16) + 16, SIZE * 2 + 48), (30, 34, 42, 255))
    for k, name in enumerate(names):
        icon = Image.open(os.path.join(OUT, name + ".png"))
        x = 16 + k * (SIZE + 16)
        sheet.alpha_composite(icon, (x, 16))
        small = icon.resize((32, 32), Image.LANCZOS)
        sheet.alpha_composite(small, (x + SIZE // 2 - 16, SIZE + 32))
    sheet.save(os.path.join(sheet_dir, "icons_sheet.png"))
    print("wrote", OUT)


if __name__ == "__main__":
    main()
