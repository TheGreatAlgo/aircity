"""Example airlines. Liveries are model-independent: every aircraft type paints every style, so these
show up on all of them (and become AircraftLiveryAssets in Assets/AirCity/Liveries)."""
import numpy as np

# name: style, base, color1, color2, color3, registration colour, default registration, tail logo (or None)
LIVERIES = {
    "ClassicBlue":  ("Classic", "#F4F4F1", "#1B2A5C", "#C9A13A", "#1B2A5C", "#1B2A5C", "N172CB", None),
    "SunsetRed":    ("Classic", "#F5F3EE", "#A3141B", "#3A3A3E", "#A3141B", "#2B2B2E", "N4721R", "SunsetRed"),
    "SkyhawkSP":    ("Swoosh",  "#F6F6F4", "#1D3E72", "#8E98A6", "#1D3E72", "#1D3E72", "N652SP", None),
    "FlightSchool": ("Trainer", "#F7F7F5", "#C62026", "#C62026", "#1F2F57", "#1F2F57", "N35FS", None),
    "RegionalAir":  ("TwoTone", "#F7F7F4", "#B8BEC6", "#0F6B4F", "#0F6B4F", "#0F6B4F", "N208RA", "RegionalAir"),
    "BushYellow":   ("TwoTone", "#F1B91C", "#1E1E1E", "#F7F7F4", "#1E1E1E", "#1E1E1E", "C-FBSH", None),
}

LOGO_SIZE = 256


def _grid(n):
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32) + 0.5
    return xx / n * 2 - 1, yy / n * 2 - 1        # -1..1, y up (row 0 = bottom)


def _cov(d, n):
    """Signed distance (in -1..1 units, negative inside) -> antialiased coverage."""
    return np.clip(0.5 - d * n / 2, 0, 1)


def logo_sunset(n=LOGO_SIZE):
    """A setting sun over three horizon bars."""
    x, y = _grid(n)
    rgba = np.zeros((n, n, 4), np.float32)
    sun = _cov(np.hypot(x, y - 0.12) - 0.62, n) * _cov(-(y + 0.02), n)
    bars = np.zeros_like(x)
    for k, (yc, half) in enumerate(((-0.18, 0.06), (-0.40, 0.055), (-0.60, 0.05))):
        w = 0.80 - 0.14 * k
        bars = np.maximum(bars, _cov(np.maximum(np.abs(y - yc) - half, np.abs(x) - w), n))
    orange = np.array([0.98, 0.62, 0.15], np.float32)
    red = np.array([0.64, 0.08, 0.10], np.float32)
    rgba[..., :3] = orange * sun[..., None] + red * (1 - sun[..., None])
    rgba[..., 3] = np.maximum(sun, bars)
    return rgba


def logo_regional(n=LOGO_SIZE):
    """A white disc with a green leaf-wing."""
    x, y = _grid(n)
    rgba = np.zeros((n, n, 4), np.float32)
    disc = _cov(np.hypot(x, y) - 0.92, n)
    # leaf: intersection of two offset circles, tilted
    c, s = np.cos(0.6), np.sin(0.6)
    u, v = c * x + s * y, -s * x + c * y
    leaf = _cov(np.maximum(np.hypot(u, v - 0.55) - 0.85, np.hypot(u, v + 0.55) - 0.85), n)
    vein = _cov(np.maximum(np.abs(v) - 0.025, np.abs(u) - 0.55), n)
    white = np.array([0.97, 0.97, 0.95], np.float32)
    green = np.array([0.06, 0.42, 0.31], np.float32)
    col = white * (1 - leaf[..., None]) + green * leaf[..., None]
    col = col * (1 - vein[..., None]) + white * vein[..., None]
    rgba[..., :3] = col
    rgba[..., 3] = disc
    return rgba


LOGOS = {"SunsetRed": logo_sunset, "RegionalAir": logo_regional}
