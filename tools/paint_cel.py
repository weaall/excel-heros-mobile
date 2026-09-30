# -*- coding: utf-8 -*-
"""
The painted outfit sheets (Resources/Art/SDBase/painted/<id>.png, Editor/SampleRepaint) cleaned into a
cel finish: the repaint bakes a painting through a projection, which leaves soft smears and speckle
("muddy, pixelated" in the Blue Archive cross-check). Two passes of an edge-preserving filter, then
the colours quantised to a small palette in Lab (flat fills, like a hand-painted anime sheet), then
the speckle removed. Transparent texels untouched. The original is kept in tools/out/painted_orig/.

    python tools/paint_cel.py [--k 28] [ids...]      (all sheets when no id)
SECURITY: none (local files only). The sheets are sample-derived: git-ignored, mirrored to Drive only.
"""
import os, shutil, sys
import cv2, numpy as np

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "SDBase", "painted")
ORIG = os.path.join(ROOT, "tools", "out", "painted_orig")


def cel(path, k):
    im = cv2.imread(path, cv2.IMREAD_UNCHANGED)
    bgr, a = (im[..., :3], im[..., 3]) if im.shape[2] == 4 else (im, None)
    sm = cv2.bilateralFilter(bgr, 9, 38, 7)
    sm = cv2.bilateralFilter(sm, 9, 30, 7)
    lab = cv2.cvtColor(sm, cv2.COLOR_BGR2LAB).reshape(-1, 3).astype(np.float32)
    mask = (a.reshape(-1) > 8) if a is not None else np.ones(len(lab), bool)
    pts = lab[mask]
    crit = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 30, 0.5)
    sample = pts[np.random.default_rng(0).choice(len(pts), min(len(pts), 60000), replace=False)]
    _, _, cent = cv2.kmeans(sample, k, None, crit, 3, cv2.KMEANS_PP_CENTERS)
    d = ((pts[:, None, :] - cent[None, :, :]) ** 2).sum(-1)
    q = lab.copy(); q[mask] = cent[d.argmin(1)]
    out = cv2.cvtColor(q.reshape(sm.shape).astype(np.uint8), cv2.COLOR_LAB2BGR)
    out = cv2.medianBlur(out, 3)
    if a is not None: out = np.dstack([out, a])
    cv2.imwrite(path, out)


if __name__ == "__main__":
    args = sys.argv[1:]; k = 28
    if "--k" in args: k = int(args[args.index("--k") + 1]); args = [x for x in args if x not in ("--k", str(k))]
    os.makedirs(ORIG, exist_ok=True)
    ids = args or sorted(f[:-4] for f in os.listdir(SRC) if f.endswith(".png"))
    for i in ids:
        p = os.path.join(SRC, i + ".png")
        if not os.path.exists(p): continue
        o = os.path.join(ORIG, i + ".png")
        if not os.path.exists(o): shutil.copy(p, o)
        shutil.copy(o, p)          # always from the original, so a rerun does not quantise twice
        cel(p, k); print("  cel", i)
