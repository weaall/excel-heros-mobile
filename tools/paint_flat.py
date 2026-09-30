# -*- coding: utf-8 -*-
"""
Flat cel finish for the painted outfit sheets (the BA cross-check on the 3D models, every round: "muddy
textures, lacks the cel-shaded polish"). The repaint (Editor/SampleRepaint) bakes a painting through a
projection, so its colours carry brush noise and blotches that read as dirt on a chibi. Here each sheet
is smoothed edge-preservingly (mean shift: the seams and trims stay) and quantised to a small palette
(k-means over the opaque texels), so every garment is one or two flat tones like the reference's.
The source is tools/out/painted_backup_0930 (the bakes as they were); alpha is kept.

    python tools/paint_flat.py [--k 18] [--sp 9] [--sr 26] ids...   → Resources/Art/SDBase/painted/<id>.png
SECURITY: none (local files only). Sample-derived sheets: git-ignored, Drive only.
"""
import os, sys
import cv2, numpy as np

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "tools", "out", "painted_backup_0930")
DST = os.path.join(ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "SDBase", "painted")


def flat(src, dst, k, sp, sr):
    im = cv2.imread(src, cv2.IMREAD_UNCHANGED)
    a = im[..., 3] if im.shape[2] == 4 else None
    bgr = np.ascontiguousarray(im[..., :3])
    sm = cv2.pyrMeanShiftFiltering(bgr, sp, sr)
    mask = (a > 16) if a is not None else np.ones(bgr.shape[:2], bool)
    px = sm[mask].reshape(-1, 3).astype(np.float32)
    if len(px) > k * 10:
        crit = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 30, 0.5)
        sample = px[np.random.default_rng(7).choice(len(px), min(len(px), 60000), replace=False)]
        _, _, centres = cv2.kmeans(sample, k, None, crit, 3, cv2.KMEANS_PP_CENTERS)
        d = ((px[:, None, :] - centres[None, :, :]) ** 2).sum(-1)
        q = centres[d.argmin(1)].astype(np.uint8)
        out = sm.copy(); out[mask] = q
    else:
        out = sm
    if a is not None: out = np.dstack([out, a])
    cv2.imwrite(dst, out)


if __name__ == "__main__":
    args = sys.argv[1:]
    def opt(name, d):
        if name in args: i = args.index(name); v = args[i + 1]; del args[i:i + 2]; return type(d)(v)
        return d
    k, sp, sr = opt("--k", 18), opt("--sp", 9), opt("--sr", 26)
    for hid in args:
        s = os.path.join(SRC, hid + ".png")
        if not os.path.exists(s): print("  skip", hid); continue
        flat(s, os.path.join(DST, hid + ".png"), k, sp, sr); print("  flat", hid)
