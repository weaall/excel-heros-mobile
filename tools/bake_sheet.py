# -*- coding: utf-8 -*-
"""
The Excel sheet is part of the character's art. Each character's own sheet — the four-cell strip
of BackSheet.cs: accent colour walked cell by cell, the id's solid pattern, the grade frame — is
painted BEHIND the figure (the body covers part of it), at the upper back, leaning out past one
shoulder, with its glow and a little light spilling onto the figure's edge. Drawn at the
signature state (all four cells lit), the way the reference's halo is always the same.

    python tools/bake_sheet.py std      # ArtSource/Standing -> Resources/Art/Standing
    python tools/bake_sheet.py sd       # ArtSource/SD       -> Resources/Art/SD
    python tools/bake_sheet.py all

The clean originals in ArtSource stay the input for the 3D SD (tools/sd3d.py) and the face boxes.
"""
import colorsys, glob, json, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "Assets", "ExcelHeroes")
DATA = os.path.join(ROOT, "Resources", "Data")
sys.path.insert(0, r"C:\Users\user\excel-heros\tools")

FRAME = {"S": (255, 200, 50), "A": (176, 110, 236), "B": (70, 150, 240), "C": (60, 190, 120), "D": (150, 160, 176)}
MAIN_ACCENT = "#2ec4b6"


def hsum(s):
    return sum(ord(c) for c in s or "")


def heroes():
    h = {x["id"]: x for x in json.load(open(os.path.join(DATA, "heroes.json"), encoding="utf-8"))["items"]}
    h["intern"] = {"id": "main", "grade": "D", "colorAccent": MAIN_ACCENT}
    return h


def hexrgb(s, fb=(80, 210, 255)):
    try:
        s = s.lstrip("#"); return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))
    except Exception:
        return fb


class Spec:
    """The same numbers as BackSheet.For (C#), for the art."""
    def __init__(self, hid, d):
        self.id = d.get("id", hid)
        acc = np.array(hexrgb(d.get("colorAccent", "")), np.float32) / 255
        lum = 0.299 * acc[0] + 0.587 * acc[1] + 0.114 * acc[2]
        if lum > 0.72:
            t = (lum - 0.72) / 0.28 * 0.55 + 0.2
            acc = acc + (np.array([40, 60, 110]) / 255 - acc) * t
        self.accent = acc
        self.frame = FRAME.get(d.get("grade", "D"), FRAME["D"])
        self.pattern = (hsum(self.id) // 2) % 6
        self.left = hsum(self.id) % 2 == 0

    def cell(self, i):
        h, s, v = colorsys.rgb_to_hsv(*self.accent)
        h = (h + (i - 1.5) * 0.045) % 1.0
        r, g, b = colorsys.hsv_to_rgb(h, min(1, s * 0.95 + 0.05), min(1, v * (1.02 - i * 0.04)))
        return np.array([r, g, b]) * 255


def ink(p, u, v):
    k = 5.0
    if p == 0: return (v * k) % 1 < 0.38
    if p == 1: return ((u * k) % 1 - 0.5) ** 2 + ((v * k) % 1 - 0.5) ** 2 < 0.07
    if p == 2: return (int(u * 4) + int(v * 4)) % 2 == 0
    if p == 3: return ((u + v) * k * 0.8) % 1 < 0.35
    if p == 4: return (u * k) % 1 < 0.16 or (v * k) % 1 < 0.16
    return (v * k + np.sin(u * np.pi * 4) * 0.25) % 1 < 0.36


def strip(spec, w):
    """The sheet itself, w wide (aspect 4.5 : 1), RGBA, with its outer glow."""
    h = int(w / 4.5)
    g = int(h * 0.45)                       # glow margin
    im = Image.new("RGBA", (w + 2 * g, h + 2 * g), (0, 0, 0, 0))
    a = np.zeros((im.height, im.width, 4), np.float32)
    pad, gap = h * 0.1, h * 0.07
    cw = (w - pad * 2 - gap * 3) / 4
    ch = h - pad * 2
    # body: translucent white glass
    a[g:g + h, g:g + w] = [235, 248, 255, 95]
    for i in range(4):
        x0 = g + pad + i * (cw + gap); y0 = g + pad
        col = spec.cell(i); lite = col + (255 - col) * 0.38
        xs = np.arange(int(cw)); ys = np.arange(int(ch))
        uu, vv = np.meshgrid(xs / cw, ys / ch)
        mask = np.vectorize(lambda u, v: ink(spec.pattern, u, v))(uu, vv)
        block = np.where(mask[..., None], lite, col)
        a[int(y0):int(y0) + len(ys), int(x0):int(x0) + len(xs), :3] = block
        a[int(y0):int(y0) + len(ys), int(x0):int(x0) + len(xs), 3] = 225
    im = Image.fromarray(a.clip(0, 255).astype(np.uint8), "RGBA")
    d = ImageDraw.Draw(im)
    fw = max(2, h // 22)
    d.rounded_rectangle([g, g, g + w, g + h], radius=max(3, h // 10), outline=spec.frame + (255,), width=fw)
    # glow in the frame colour, behind
    glow = Image.new("RGBA", im.size, (0, 0, 0, 0))
    ImageDraw.Draw(glow).rounded_rectangle([g, g, g + w, g + h], radius=h // 8, fill=spec.frame + (150,))
    glow = glow.filter(ImageFilter.GaussianBlur(g * 0.45))
    return Image.alpha_composite(glow, im)


def perspective(im, side):
    """Turn the strip a little into depth: the end behind the body is shorter than the end
    that leans out, so it reads as a plane in space rather than a label."""
    w, h = im.size
    near, far = 1.0, 0.72                  # height of the outer end vs the inner end
    if side > 0: coeffs_l, coeffs_r = far, near
    else: coeffs_l, coeffs_r = near, far
    # quad corners in the source (UL, LL, LR, UR) for PIL's QUAD transform
    ml = h * (1 - coeffs_l) / 2; mr = h * (1 - coeffs_r) / 2
    return im.transform((w, h), Image.QUAD, (0, -ml / coeffs_l * 0, 0, h, w, h, w, 0), Image.BICUBIC) if False else         _quad(im, ml, mr)


def _quad(im, ml, mr):
    w, h = im.size
    src = np.asarray(im).astype(np.float32)
    out = np.zeros_like(src)
    for x in range(w):
        t = x / max(1, w - 1)
        m = ml + (mr - ml) * t
        col = src[:, x]
        span = h - 2 * m
        if span < 2: continue
        ys = np.linspace(0, h - 1, int(span))
        idx = ys.astype(int)
        out[int(m):int(m) + len(idx), x] = col[idx]
    return Image.fromarray(out.clip(0, 255).astype(np.uint8), "RGBA")


def place(fig, spec, head, kind):
    """Sheet behind `fig` (RGBA, canvas-sized), from the head box (x0, y0, x1, y1 in px)."""
    W, H = fig.size
    hx0, hy0, hx1, hy1 = head
    hw, hh = hx1 - hx0, hy1 - hy0
    side = -1 if spec.left else 1
    # Behind the upper back: most of the strip is hidden by the head and shoulders, one end
    # leans out past the shoulder on the sheet's side, rising towards it (a halo is worn, not
    # held up next to the face).
    if kind == "sd":
        w = int(hw * 1.7); cx = (hx0 + hx1) / 2 + side * hw * 0.38; cy = hy1 + hh * 0.06; tilt = 16
    else:
        w = int(hw * 2.6); cx = (hx0 + hx1) / 2 + side * hw * 0.55; cy = hy1 + hh * 0.55; tilt = 14
    s = perspective(strip(spec, w), side).rotate(side * tilt, resample=Image.BICUBIC, expand=True)
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    x = int(cx - s.width / 2); y = int(cy - s.height / 2)
    x = max(-s.width // 4, min(W - s.width * 3 // 4, x))
    layer.alpha_composite(s, (max(0, x), max(0, y)), (max(0, -x), max(0, -y)))
    out = Image.alpha_composite(layer, fig)
    # light spill: a soft rim of the frame colour where the figure's edge meets the sheet
    fa = np.asarray(fig.split()[3]).astype(np.float32) / 255
    la = np.asarray(layer.filter(ImageFilter.GaussianBlur(10)).split()[3]).astype(np.float32) / 255
    edge = np.asarray(fig.split()[3].filter(ImageFilter.FIND_EDGES).filter(ImageFilter.GaussianBlur(3))).astype(np.float32) / 255
    k = np.clip(edge * la * fa * 0.55, 0, 1)[..., None]
    o = np.asarray(out).astype(np.float32)
    o[..., :3] = o[..., :3] * (1 - k) + np.array(spec.frame) * k
    return Image.fromarray(o.clip(0, 255).astype(np.uint8), "RGBA")


def head_box(im, hid, kind):
    if kind == "std":
        faces = {f["id"]: f for f in json.load(open(os.path.join(DATA, "faces.json"), encoding="utf-8"))["items"]}
        f = faces.get(hid)
        if f:
            W, H = im.size
            return (f["x0"] * W, f["y0"] * H, f["x1"] * W, f["y1"] * H)
    import proportions
    _, _, box = proportions.measure(im)
    return box


def run(kind):
    src = os.path.join(ROOT, "ArtSource", "Standing" if kind == "std" else "SD")
    dst = os.path.join(ROOT, "Resources", "Art", "Standing" if kind == "std" else "SD")
    hs = heroes()
    n = 0
    for f in sorted(glob.glob(os.path.join(src, "*.png"))):
        hid = os.path.splitext(os.path.basename(f))[0]
        d = hs.get(hid)
        if d is None: print(f"  {hid}: no hero data"); continue
        im = Image.open(f).convert("RGBA")
        box = head_box(im, hid, kind)
        if box is None: print(f"  {hid}: no head"); continue
        place(im, Spec(hid, d), box, kind).save(os.path.join(dst, hid + ".png"))
        n += 1
    print(f"{kind}: {n} baked")


if __name__ == "__main__":
    which = sys.argv[1] if len(sys.argv) > 1 else "all"
    for k in (["std", "sd"] if which == "all" else [which]):
        run(k)
