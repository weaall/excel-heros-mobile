# -*- coding: utf-8 -*-
"""
Each hero's own outfit on the sample body they wear, painted from their illustration:

    1. Unity: SampleRepaint.Views   → tools/out/samplepaint/views/<id>_<front|back|left|right>.png
    2. this script: Gemini repaints every view as the hero's illustrated outfit (same silhouette,
       the sample's folds and shading kept) → tools/out/samplepaint/painted/<id>_<view>.png
    3. Unity: SampleRepaint.Bake    → Resources/Art/SDBase/painted/<id>.png (SdSample wears it)

    python tools/gen_samplepaint_gemini.py acct_lead cfo      # these
    python tools/gen_samplepaint_gemini.py                     # every hero with views
The front goes first; the other three get the painted front as well, so the four agree.
SECURITY: the key is read from the env file by NAME and sent in a header; never printed or written.
"""
import glob, os, sys, time
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import gen_sdtex_gemini as sd

sd.MODELS = ["gemini-3-pro-image-preview", "gemini-2.5-flash-image"]
ROOT = os.path.join(g.ROOT, "tools", "out", "samplepaint")
VIEWS, PAINTED = os.path.join(ROOT, "views"), os.path.join(ROOT, "painted")
STAND = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Standing")
SIDE = {"front": "front", "back": "back", "left": "left side", "right": "right side"}

PROMPT = (
    "Image 1 is a flat 3D render of a chibi character seen from the {view}. "
    "Image 2 is the full illustration of the character who will wear it. {ref}"
    "Repaint image 1 so its CLOTHES are exactly the outfit of image 2 (leave the head, face and hair as they are): its garments, colours, trims, buttons, ties, ribbons, "
    "patterns, logos and accessories on the torso, arms and legs — mapped onto the shapes that are there. "
    "Anything on image 1 that image 2 does not wear — cross straps, harness belts, pouches, holsters, mechanical parts, "
    "big logos — is painted over in the colour of the garment under it, so it disappears. "
    "Keep image 1's silhouette, folds, pleats and shading exactly: every edge stays where it is; only the colours and "
    "surface details change. Blue Archive SD texture style: flat cel colours, soft two-tone shading, clean thin dark lines on seams. "
    "Plain pure white background; paint nothing outside the figure; same size and framing as image 1."
)
REF_BACK = "Image 3 is the same clothes already repainted from the front: match it exactly. "


def paint(key, hid, view, front=None):
    src = os.path.join(VIEWS, f"{hid}_{view}.png")
    art = os.path.join(STAND, hid + ".png")
    parts = [{"inlineData": {"mimeType": "image/png", "data": sd.b64(src)}},
             {"inlineData": {"mimeType": "image/png", "data": sd.b64(art)}}]
    if front is not None: parts.append({"inlineData": {"mimeType": "image/png", "data": sd.b64(front)}})
    parts.append({"text": PROMPT.format(view=SIDE[view], ref=REF_BACK if front else "")})
    img = sd.call(key, parts)
    out = sd.align(img, src)
    if out is None: raise RuntimeError("align failed")
    dst = os.path.join(PAINTED, f"{hid}_{view}.png"); out.save(dst)
    return dst


if __name__ == "__main__":
    os.makedirs(PAINTED, exist_ok=True)
    ids = [a for a in sys.argv[1:] if not a.startswith("--")] or sorted({os.path.basename(f).rsplit("_", 1)[0] for f in glob.glob(os.path.join(VIEWS, "*_front.png"))})
    key = g.read_key()
    for hid in ids:
        if not os.path.exists(os.path.join(STAND, hid + ".png")): print(f"  no illustration {hid}"); continue
        if all(os.path.exists(os.path.join(PAINTED, f"{hid}_{v}.png")) for v in SIDE) and "--force" not in sys.argv: print(f"  skip {hid}"); continue
        try:
            front = paint(key, hid, "front")
            for v in ("back", "left", "right"): paint(key, hid, v, front)
            print(f"  ok   {hid}")
        except Exception as e:
            print(f"  FAIL {hid}: {str(e)[:160]}")
        time.sleep(1)
