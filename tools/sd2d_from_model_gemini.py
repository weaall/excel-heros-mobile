# -*- coding: utf-8 -*-
"""
2D SD, fourth generation — drawn FROM THE 3D MODEL (the user's round of 2026-09-30: "check the SD
against the illustration and against the model — the SD design should be the model's"). The v3 SDs
were drawn from the illustration alone, so the 2D and the 3D were two designs of one character (the
consistency audit: 2D↔3D 4.2/10). Here two images go in: the character's standing illustration (the
source of identity: face, exact hair / eye / outfit colours, glasses, accessories) and a front render
of our 3D SD model (the source of the SD DESIGN: head size and proportions, hair shape and volume,
the outfit's pieces, lengths and layering, legwear, shoes). No other game's art goes in; the style
is described in words. Output → ArtSource/SD_v4/raw/<id>.png, then cutout (SD=1) and uniform.py sd.

    python tools/sd2d_from_model_gemini.py cro cfo      (renders: tools/out/sd3d_front/strip_<id>.png)
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, io, json, os, sys, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
from PIL import Image

STAND = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Standing")
RENDERS = os.environ.get("SD_RENDERS") or os.path.join(g.ROOT, "tools", "out", "sd3d_front")
OUT = os.environ.get("SD_OUT") or os.path.join(g.ROOT, "ArtSource", "SD_v4", "raw")
SD3 = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "SD")
# STYLE_REF=1: our current 2D SD of the character goes in as a third image — the drawing style and finish to keep
STYLE_REF = os.environ.get("STYLE_REF") == "1"
ASK_STYLE = (" Image 3 is our current 2D SD drawing of this character: match ITS drawing style, line quality, face, eye drawing, "
             "shading and finish exactly (it is the quality bar) — only the design details change, to the 3D model's.")
ASK = ("Image 1 is the official illustration of a character from our game. Image 2 is our 3D super-deformed (chibi) model of the "
       "SAME character. Draw this character as a 2D SD chibi illustration that is clearly the SAME DESIGN as the 3D model in image 2: "
       "follow image 2 for the proportions (the big head, the small body — about 2.4 heads tall), the hair's shape, length, volume "
       "and parting, the outfit's pieces, their lengths and layering (jacket, shirt, skirt or trousers), the legwear and the shoes. "
       "Follow image 1 for the identity and every colour: the face and eye colour, the exact hair colour, the outfit colours and trims, "
       "glasses, badges, lanyards and hair accessories — include those even if the model lacks them. HAIR: keep every bun, ponytail, twin tail, braid and side lock that BOTH images show, in the same place (a bun on the side stays on that side). Do NOT copy the 3D model's "
       "rendering flaws (blurry textures, jagged hair, stretched patterns): redraw it cleanly. STYLE: the polished official SD art of a "
       "top-tier Japanese mobile gacha game — clean thin dark line art, flat two-tone cel shading, one crisp highlight band on the hair, "
       "large glossy eyes with a bright highlight, small nose dot, small gentle smile, light blush, soft pastel colours, no texture noise. "
       "POSE: full body from the top of the hair to the shoes, a relaxed three-quarter standing pose facing the viewer like the model's, "
       "both feet on the ground, hands empty and relaxed. Plain flat pure white background, no shadow, no text, one character only.")


def part(path, crop_alpha=False):
    im = Image.open(path).convert("RGBA")
    if crop_alpha:
        # the render's flat blue backdrop → white, cropped to the figure
        px = im.load(); bg = px[2, 2]
        for y in range(im.height):
            for x in range(im.width):
                p = px[x, y]
                if abs(p[0] - bg[0]) + abs(p[1] - bg[1]) + abs(p[2] - bg[2]) < 40: px[x, y] = (255, 255, 255, 255)
        bb = Image.eval(im.convert("L"), lambda v: 255 if v < 250 else 0).getbbox()
        if bb: im = im.crop((max(0, bb[0] - 20), max(0, bb[1] - 20), min(im.width, bb[2] + 20), min(im.height, bb[3] + 20)))
    bg = Image.new("RGBA", im.size, (255, 255, 255, 255)); bg.alpha_composite(im)
    buf = io.BytesIO(); bg.convert("RGB").save(buf, "PNG")
    return {"inlineData": {"mimeType": "image/png", "data": base64.b64encode(buf.getvalue()).decode("ascii")}}


def make(key, hid):
    src = os.path.join(STAND, hid + ".png"); ren = os.path.join(RENDERS, "strip_" + hid + ".png")
    if not os.path.exists(src) or not os.path.exists(ren): print("  skip", hid); return False
    parts = [part(src), part(ren, True)]
    if STYLE_REF and os.path.exists(os.path.join(SD3, hid + ".png")): parts.append(part(os.path.join(SD3, hid + ".png")))
    body = json.dumps({"contents": [{"parts": parts + [{"text": ASK + (ASK_STYLE if len(parts) == 3 else "")}]}],
                       "generationConfig": {"responseModalities": ["IMAGE"], "temperature": 0.4, "imageConfig": {"aspectRatio": "4:5"}}}).encode("utf-8")
    for model in g.MODELS:
        req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent", data=body, method="POST",
                                     headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=300) as r: d = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e: print(f"  {hid} {model}: HTTP {e.code}"); continue
        for c in d.get("candidates", []):
            for p in c.get("content", {}).get("parts", []):
                inline = p.get("inlineData") or p.get("inline_data")
                if inline and inline.get("data"):
                    open(os.path.join(OUT, hid + ".png"), "wb").write(base64.b64decode(inline["data"])); print(f"  ok {hid} ({model})"); return True
    print("  FAILED", hid); return False


if __name__ == "__main__":
    key = g.read_key(); os.makedirs(OUT, exist_ok=True)
    ids = [a for a in sys.argv[1:] if not a.startswith("--")]
    if "--missing" in sys.argv: ids = [i for i in ids if not os.path.exists(os.path.join(OUT, i + ".png"))]
    import concurrent.futures as cf
    with cf.ThreadPoolExecutor(4) as ex: list(ex.map(lambda h: make(key, h), ids))
