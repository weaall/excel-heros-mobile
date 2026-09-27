# -*- coding: utf-8 -*-
"""
Colours for the 3D SD, taken from each character's own clean SD illustration (ArtSource/SD) so
the model wears what the drawing wears: hair (crown), skin (cheeks), top (chest), bottom (hips),
legs (shins), shoes (feet). Regions come from the detected head box and the common body
proportions (every SD is normalised to 2.4 heads, feet at 944). → Resources/Data/looks.json

    python tools/sample_looks.py
"""
import glob, json, os, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "..", "Assets", "ExcelHeroes", "ArtSource", "SD")
OUT = os.path.join(HERE, "..", "Assets", "ExcelHeroes", "Resources", "Data", "looks.json")
sys.path.insert(0, r"C:\Users\user\excel-heros\tools")
import proportions


SKINS = np.array([[247, 220, 203], [251, 227, 212], [226, 184, 148], [240, 200, 180]], np.float32)


def median(a, box, skin_ok=False):
    """The region's dominant colour: outlines (very dark) and, unless asked, skin tones dropped;
    then the most common colour after quantising to 12 levels a channel."""
    x0, y0, x1, y1 = [int(v) for v in box]
    reg = a[max(0, y0):y1, max(0, x0):x1].reshape(-1, 4)
    reg = reg[reg[:, 3] > 200][:, :3]
    lum = reg @ np.array([0.3, 0.59, 0.11])
    reg = reg[lum > 28]
    if not skin_ok and len(reg):
        d = np.min(np.linalg.norm(reg[:, None, :] - SKINS[None], axis=2), axis=1)
        reg = reg[d > 38]
    if len(reg) < 30: return None
    q = (reg // 21).astype(int)
    keys, counts = np.unique(q, axis=0, return_counts=True)
    top = keys[np.argmax(counts)]
    members = reg[np.all(q == top, axis=1)]
    c = np.median(members, axis=0)
    return "#%02x%02x%02x" % tuple(int(v) for v in c)


def main():
    rows = []
    for f in sorted(glob.glob(os.path.join(SRC, "*.png"))):
        hid = os.path.splitext(os.path.basename(f))[0]
        im = Image.open(f).convert("RGBA")
        a = np.asarray(im).astype(np.float32)
        _, _, box = proportions.measure(im)
        if box is None: print(f"  {hid}: no head"); continue
        hx0, hy0, hx1, hy1 = box
        hw, hh = hx1 - hx0, hy1 - hy0
        cx = (hx0 + hx1) / 2
        feet = 944.0
        body = feet - hy1
        r = {
            "id": hid,
            "hair": median(a, (cx - hw * 0.35, hy0 + hh * 0.02, cx + hw * 0.35, hy0 + hh * 0.22)),
            "top": median(a, (cx - hw * 0.28, hy1 + body * 0.12, cx + hw * 0.28, hy1 + body * 0.3)),
            "bottom": median(a, (cx - hw * 0.3, hy1 + body * 0.46, cx + hw * 0.3, hy1 + body * 0.6)),
            "legs": median(a, (cx - hw * 0.4, hy1 + body * 0.74, cx + hw * 0.4, hy1 + body * 0.86)),
            "shoes": median(a, (cx - hw * 0.45, feet - body * 0.06, cx + hw * 0.45, feet - body * 0.01)),
        }
        rows.append({k: v for k, v in r.items() if v})
        print(f"  {hid:16s} hair {r['hair']} top {r['top']} bottom {r['bottom']} legs {r['legs']}")
    with open(OUT, "w", encoding="utf-8") as fh:
        json.dump({"items": rows}, fh, indent=1)
    print(f"{len(rows)} looks -> {OUT}")


if __name__ == "__main__":
    main()
