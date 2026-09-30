# -*- coding: utf-8 -*-
"""
The painted outfit sheets given back the line work the repaint lost (the Blue Archive cross-check:
"muddy 3D textures"). Editor/SampleRepaint bakes a painting through a projection, so the repaint has
the right COLOURS but soft edges; the sample body's own sheet (Resources/Art/SDBase/bodies/<key>) has
crisp fold lines, seams and edges on the same UV layout. Each painted sheet keeps its colour (Lab a/b
and its broad lightness, smoothed edge-aware) and takes the sample sheet's high-frequency lightness —
its detail above a blur — so the lines are sharp again without the sample's colours or broad prints.
The originals stay in tools/out/painted_orig/.

    python tools/paint_detail.py [--amt 1.0] [ids...]
SECURITY: none (local files only). Sample-derived sheets: git-ignored, Drive only.
"""
import glob, json, os, shutil, sys
import cv2, numpy as np

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
ART = os.path.join(ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "SDBase")
PAINT, BODIES = os.path.join(ART, "painted"), os.path.join(ART, "bodies")
ORIG = os.path.join(ROOT, "tools", "out", "painted_orig")
SPEC = os.path.join(ROOT, "Assets", "ExcelHeroes", "Resources", "Data", "sdspec.json")


def body_sheet(key):
    fs = [f for f in glob.glob(os.path.join(BODIES, key, "*_Body*.png"))]
    return sorted(fs, key=len)[0] if fs else None


def detail(painted, raw, amt):
    p = cv2.imread(painted, cv2.IMREAD_UNCHANGED); r = cv2.imread(raw, cv2.IMREAD_UNCHANGED)
    a = p[..., 3] if p.shape[2] == 4 else None
    pb = p[..., :3]; rb = cv2.resize(r[..., :3], (pb.shape[1], pb.shape[0]), interpolation=cv2.INTER_AREA)
    base = cv2.bilateralFilter(pb, 9, 40, 7)
    lab = cv2.cvtColor(base, cv2.COLOR_BGR2LAB).astype(np.float32)
    rl = cv2.cvtColor(rb, cv2.COLOR_BGR2LAB)[..., 0].astype(np.float32) + 8.0
    if os.environ.get("DETAIL_MODE") == "mul":
        # the sample's shading structure (folds, cel shadow shapes) as a RATIO over its own broad tone, laid
        # onto the painted tone: out = painted × (raw / blur(raw)) at a mid scale
        ratio = rl / np.maximum(cv2.GaussianBlur(rl, (0, 0), float(os.environ.get("DETAIL_SIGMA", "10"))), 1.0)
        ratio = np.clip(1.0 + (ratio - 1.0) * amt, 0.45, 1.3)
        lab[..., 0] = np.clip(lab[..., 0] * ratio, 0, 255)
    else:
        hf = rl - cv2.GaussianBlur(rl, (0, 0), 3.0)          # the sample's lines and folds, not its broad tones
        hf = np.clip(hf, -60, 30)                             # dark lines strong, highlights gentle
        lab[..., 0] = np.clip(lab[..., 0] + hf * amt, 0, 255)
    out = cv2.cvtColor(lab.astype(np.uint8), cv2.COLOR_LAB2BGR)
    if a is not None: out = np.dstack([out, a])
    cv2.imwrite(painted, out)


if __name__ == "__main__":
    args = sys.argv[1:]; amt = 1.0
    if "--amt" in args: amt = float(args[args.index("--amt") + 1]); args = [x for x in args if x not in ("--amt", args[args.index("--amt") + 1])]
    spec = {r["id"]: r for r in json.load(open(SPEC, encoding="utf-8"))["items"]}
    os.makedirs(ORIG, exist_ok=True)
    ids = args or sorted(f[:-4] for f in os.listdir(PAINT) if f.endswith(".png"))
    for i in ids:
        p = os.path.join(PAINT, i + ".png"); key = (spec.get(i) or {}).get("body") or ""
        raw = body_sheet(key) if key else None
        if not os.path.exists(p) or not raw: print("  skip", i, key); continue
        o = os.path.join(ORIG, i + ".png")
        if not os.path.exists(o): shutil.copy(p, o)
        shutil.copy(o, p)
        detail(p, raw, amt); print("  detail", i, "<-", os.path.basename(raw))
