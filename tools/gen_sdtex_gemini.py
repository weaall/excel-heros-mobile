# -*- coding: utf-8 -*-
"""
Hand-painted textures for the common SD base, by Gemini (Nano Banana) image editing:

    1. SdTexBake.Views renders each character's flat base from four sides → tools/out/sdtex/views
    2. this script sends every view, with the character's own SD illustration as the design
       reference, and asks for it repainted: cloth details, hair strands, the face — silhouette,
       pose and camera unchanged → tools/out/sdtex/painted (aligned to the render, with its alpha)
    3. SdTexBake.Bake projects the painted views back onto the mesh

    python tools/gen_sdtex_gemini.py            # every character with views
    python tools/gen_sdtex_gemini.py intern cfo
    python tools/gen_sdtex_gemini.py --sheet    # review montage only

SECURITY: the key is read from the env file by NAME and sent in a header, never printed or
written anywhere.
"""
import base64, glob, io, json, os, sys, time, urllib.request, urllib.error
import numpy as np
from PIL import Image

ENV_FILE = os.environ.get("ENV_FILE", r"C:\Users\user\Desktop\mindsai_weaall.env")
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "out", "sdtex")
VIEWS = os.path.join(ROOT, "views")
PAINTED = os.path.join(ROOT, "painted")
SDART = os.path.join(HERE, "..", "Assets", "ExcelHeroes", "ArtSource", "SD")
MODELS = ["gemini-2.5-flash-image", "gemini-2.5-flash-image-preview"]
VIEW_NAMES = ["front", "back", "left", "right"]

PROMPT = (
    "Image 1 is a plain, untextured 3D render of a chibi (super-deformed) character from the {view}. "
    "Image 2 is the finished 2D illustration of the SAME character: copy its face, hairstyle, hair colour, outfit design and colours exactly. "
    "Repaint image 1 as a finished game texture in the Blue Archive SD style: hand-painted cloth with collar, buttons, seams, pockets, pleats and soft fabric folds; "
    "hair drawn as sculpted clumps with sharp strand tips and a glossy highlight band; {face}"
    "flat cel colours with light soft shading, thin clean dark outlines on the details. "
    "KEEP the exact silhouette, proportions, pose, size, position and camera of image 1 — paint only inside the figure; do not add, move, crop or remove anything. "
    "Plain pure white background, nothing else in the picture. Output at the same size as image 1."
)
FACE = {
    "front": "big detailed anime eyes with bright irises and highlights, thin eyebrows, a small mouth, a light blush; ",
    "back": "this is the BACK view: no face; paint the back of the hair and the back of the outfit; ",
    "left": "this is the LEFT side view: paint the side of the face (one eye, in profile), the side of the hair and outfit; ",
    "right": "this is the RIGHT side view: paint the side of the face (one eye, in profile), the side of the hair and outfit; ",
}


def read_key():
    with open(ENV_FILE, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line.startswith("GEMINI_API_KEY="):
                return line.split("=", 1)[1].strip().strip('"').strip("'")
    sys.exit("GEMINI_API_KEY not found in the env file")


def b64(path, max_side=1024):
    im = Image.open(path).convert("RGBA")
    if max(im.size) > max_side:
        im.thumbnail((max_side, max_side), Image.LANCZOS)
    # the renders are transparent; Gemini wants an opaque picture — white behind
    bg = Image.new("RGB", im.size, (255, 255, 255)); bg.paste(im, (0, 0), im)
    buf = io.BytesIO(); bg.save(buf, "PNG")
    return base64.b64encode(buf.getvalue()).decode("ascii")


def call(key, parts):
    body = json.dumps({"contents": [{"parts": parts}],
                       "generationConfig": {"responseModalities": ["IMAGE"], "imageConfig": {"aspectRatio": "1:1"}}}).encode("utf-8")
    last = None
    for model in MODELS:
        for attempt in range(3):
            req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent",
                                         data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
            try:
                with urllib.request.urlopen(req, timeout=240) as r:
                    data = json.loads(r.read().decode("utf-8"))
            except urllib.error.HTTPError as e:
                msg = e.read().decode("utf-8", "replace")[:200]
                last = f"{model} HTTP {e.code} {msg}"
                if e.code in (429, 500, 503): time.sleep(8 * (attempt + 1)); continue
                break
            for cand in data.get("candidates", []):
                for part in cand.get("content", {}).get("parts", []):
                    inline = part.get("inlineData") or part.get("inline_data")
                    if inline and inline.get("data"):
                        return Image.open(io.BytesIO(base64.b64decode(inline["data"]))).convert("RGB")
            last = f"{model}: no image ({json.dumps(data)[:160]})"
            break
    raise RuntimeError(last)


def align(painted, render_path):
    """Fit the painted figure onto the render's silhouette (scale + shift by bounding boxes) and
    give it the render's alpha, so the bake samples exactly where the model is."""
    ren = Image.open(render_path).convert("RGBA")
    ra = np.asarray(ren)[..., 3]
    rys, rxs = np.nonzero(ra > 40)
    if len(rxs) == 0: return None
    p = painted.resize(ren.size, Image.LANCZOS)
    pa = np.asarray(p.convert("RGB")).astype(np.int32)
    mask = (255 - pa.min(axis=2)) > 24                       # not white
    pys, pxs = np.nonzero(mask)
    if len(pxs) < 100: return None
    sx = (rxs.max() - rxs.min() + 1) / max(1, pxs.max() - pxs.min() + 1)
    sy = (rys.max() - rys.min() + 1) / max(1, pys.max() - pys.min() + 1)
    s = (sx + sy) / 2
    if abs(sx - sy) > 0.08 or abs(s - 1) > 0.25:
        print(f"    warning: painted figure off by scale {sx:.2f}/{sy:.2f}; fitting anyway")
    new_w, new_h = int(round(p.width * s)), int(round(p.height * s))
    ps = p.resize((new_w, new_h), Image.LANCZOS)
    # align bounding-box centres
    pcx = (pxs.min() + pxs.max()) / 2 * s; pcy = (pys.min() + pys.max()) / 2 * s
    rcx = (rxs.min() + rxs.max()) / 2; rcy = (rys.min() + rys.max()) / 2
    out = Image.new("RGB", ren.size, (255, 255, 255))
    out.paste(ps, (int(round(rcx - pcx)), int(round(rcy - pcy))))
    out = out.convert("RGBA")
    out.putalpha(Image.fromarray(ra))
    return out


def sheet():
    ids = sorted({os.path.basename(f).rsplit("_", 1)[0] for f in glob.glob(os.path.join(PAINTED, "*_front.png"))})
    if not ids: return
    W, H = 200, 200
    s = Image.new("RGB", (len(ids) * W, 9 * H), (104, 156, 214))
    for i, hid in enumerate(ids):
        for j, v in enumerate(VIEW_NAMES):
            for k, d in enumerate((VIEWS, PAINTED)):
                f = os.path.join(d, f"{hid}_{v}.png")
                if os.path.exists(f):
                    im = Image.open(f).convert("RGBA").resize((W, H)); s.paste(im, (i * W, (j * 2 + k) * H), im)
        art = os.path.join(SDART, hid + ".png")
        if os.path.exists(art):
            im = Image.open(art).convert("RGBA"); im.thumbnail((W, H)); s.paste(im, (i * W, 8 * H), im)
    s.save(os.path.join(ROOT, "_review.png"))
    print(f"review -> {os.path.join(ROOT, '_review.png')}")


def main(argv):
    if "--sheet" in argv:
        sheet(); return
    key = read_key()
    os.makedirs(PAINTED, exist_ok=True)
    ids = [a for a in argv if not a.startswith("--")] or sorted({os.path.basename(f).rsplit("_", 1)[0] for f in glob.glob(os.path.join(VIEWS, "*_front.png"))})
    force = "--force" in argv
    for hid in ids:
        art = os.path.join(SDART, hid + ".png")
        for view in VIEW_NAMES:
            src = os.path.join(VIEWS, f"{hid}_{view}.png")
            dst = os.path.join(PAINTED, f"{hid}_{view}.png")
            if not os.path.exists(src): continue
            if os.path.exists(dst) and not force: continue
            parts = [{"inlineData": {"mimeType": "image/png", "data": b64(src)}}]
            if os.path.exists(art):
                parts.append({"inlineData": {"mimeType": "image/png", "data": b64(art, 768)}})
            parts.append({"text": PROMPT.format(view=view + " view", face=FACE[view])})
            try:
                img = call(key, parts)
            except Exception as e:
                print(f"  {hid} {view}: FAILED {str(e)[:120]}"); continue
            out = align(img, src)
            if out is None:
                print(f"  {hid} {view}: could not align"); continue
            out.save(dst)
            print(f"  {hid} {view}: ok")
            time.sleep(1.5)
    sheet()


if __name__ == "__main__":
    main(sys.argv[1:])
