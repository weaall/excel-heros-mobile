# -*- coding: utf-8 -*-
"""
The 3D hair colour read off the character's 2D SD (flat cel colours, so the hair is one or two clean
tones): the pixels of the figure's top band, less skin, outline and highlight white, clustered; the
largest cluster is the hair's base tone. The spec's hair (sdspec "hair") had drifted dark and grey
(the Gemini identity read: cso lavender stored as #a5888c, pivot's black as brown, barista's orange
as red — the consistency audit's first complaint).

    python tools/hair_from_sd.py [--sd DIR] [--apply] [ids...]   (prints spec → read, ΔE)
SECURITY: none (local files only).
"""
import colorsys, json, os, subprocess, sys
import numpy as np
from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
DATA = os.path.join(ROOT, "Assets", "ExcelHeroes", "Resources", "Data")


def hexc(c): return "#%02x%02x%02x" % tuple(int(round(v)) for v in c)


def read(path):
    im = np.asarray(Image.open(path).convert("RGBA")).astype(np.float32)
    a = im[..., 3] > 200
    ys, xs = np.nonzero(a)
    if len(ys) == 0: return None
    top, bot = ys.min(), ys.max()
    band = a.copy(); band[int(top + (bot - top) * 0.36):, :] = False        # the head: the top ~36% of a 2.4-head figure
    px = im[band][:, :3]
    hsv = np.array([colorsys.rgb_to_hsv(*(p / 255.0)) for p in px[:: max(1, len(px) // 6000)]])
    sub = px[:: max(1, len(px) // 6000)]
    h, s, v = hsv[:, 0], hsv[:, 1], hsv[:, 2]
    skin = ((h < 0.11) | (h > 0.95)) & (s > 0.08) & (s < 0.42) & (v > 0.78)
    white = (s < 0.08) & (v > 0.9)
    outline = v < 0.1
    keep = ~(skin | white | outline)
    pts = sub[keep]
    if len(pts) < 50: return None
    # k-means, k=4, a few rounds
    rng = np.random.default_rng(0); cent = pts[rng.choice(len(pts), 4, replace=False)]
    for _ in range(12):
        d = ((pts[:, None, :] - cent[None, :, :]) ** 2).sum(-1); lab = d.argmin(1)
        cent = np.array([pts[lab == k].mean(0) if (lab == k).any() else cent[k] for k in range(4)])
    counts = np.bincount(lab, minlength=4)
    return cent[counts.argmax()]


def lab_dist(a, b):
    a = np.array(a) / 255.0; b = np.array(b) / 255.0
    return float(np.sqrt(((a - b) ** 2).sum()) * 100)


if __name__ == "__main__":
    args = sys.argv[1:]; sd = os.path.join(ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "SD"); apply = "--apply" in args
    if "--sd" in args: sd = args[args.index("--sd") + 1]; args = [a for a in args if a not in ("--sd", sd)]
    ids = [a for a in args if not a.startswith("--")] or [h["id"] for h in json.load(open(os.path.join(DATA, "heroes.json"), encoding="utf-8"))["items"]]
    spec = {r["id"]: r for r in json.load(open(os.path.join(DATA, "sdspec.json"), encoding="utf-8"))["items"]}
    for hid in ids:
        p = os.path.join(sd, hid + ".png")
        if not os.path.exists(p) or hid not in spec: continue
        c = read(p)
        if c is None: continue
        old = spec[hid].get("hair") or "#000000"
        oc = [int(old[i:i + 2], 16) for i in (1, 3, 5)]
        d = lab_dist(oc, c)
        print(f"{hid:14s} {old} -> {hexc(c)}  d={d:5.1f}")
        if apply and d > 12 and hid != "intern":
            subprocess.run([sys.executable, os.path.join(ROOT, "tools", "sd_spec.py"), "--set", hid, "hair=" + hexc(c)], check=False, capture_output=True)
