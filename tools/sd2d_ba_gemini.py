# -*- coding: utf-8 -*-
"""
2D SD, third generation (the user's round of 2026-09-30: "Blue Archive SD level, faithful to the
illustration and to the model"). One image in — the character's CURRENT standing illustration — and
the SD style spelled out in words (no other game's art goes in): 2.4 heads (the game's SD format), a big round head
with large glossy eyes, small soft body, clean thin dark line art, flat two-tone cel shading with a
crisp hair highlight band, full body in a neutral three-quarter standing pose, both feet on the
ground, hands relaxed. Strict fidelity: only what the illustration shows — no added glasses,
headphones, tablets, cups, or weapons. The result then goes through cutout (SD=1) and uniform.py sd.

    python tools/sd2d_ba_gemini.py cso pivot        → ArtSource/SD_v3/raw/<id>.png
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, io, json, os, sys, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
from PIL import Image

STAND = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Standing")
CARDS = os.path.join(g.ROOT, "ArtSource", "Cards_v2")
OUT = os.path.join(g.ROOT, "ArtSource", "SD_v3", "raw")
ASK = ("Draw the character in the image as a super-deformed chibi figure in the polished style of a top-tier Japanese mobile "
       "gacha game's official SD art. PROPORTIONS: exactly 2.4 heads tall — the head (hair included) is about 40% of the "
       "figure's total height, big and round; a small soft torso, short arms with small hands, short legs, small feet. FACE: "
       "large glossy eyes with a bright highlight and a soft gradient iris in the character's exact eye colour, small nose dot, "
       "small mouth with a gentle smile, light blush. RENDERING: clean thin dark-brown line art, flat cel shading in two tones, "
       "one crisp highlight band on the hair, soft pastel colours, no texture noise, no painterly strokes. POSE: full body from "
       "the top of the hair to the shoes, a relaxed three-quarter standing pose facing the viewer, both feet on the ground, "
       "arms relaxed at the sides or holding ONLY the item the character holds in the image. FIDELITY (most important): keep "
       "exactly the character's hairstyle, hair length, parting and hair accessories, hair colour, eye colour, glasses ONLY if "
       "the image has them, every garment with its colours and lengths (jacket, shirt, tie, skirt or trousers, tights or bare "
       "legs, shoes), badges and lanyards. Do NOT add anything that is not in the image — no extra glasses, headphones, "
       "tablets, cups, phones or weapons. Plain flat pure white background, no shadow, no text, one character only.")


def part(path):
    im = Image.open(path).convert("RGBA"); bg = Image.new("RGBA", im.size, (255, 255, 255, 255)); bg.alpha_composite(im)
    buf = io.BytesIO(); bg.convert("RGB").save(buf, "PNG")
    return {"inlineData": {"mimeType": "image/png", "data": base64.b64encode(buf.getvalue()).decode("ascii")}}


def make(key, hid, note=""):
    src = os.path.join(STAND, hid + ".png")
    if not os.path.exists(src): src = os.path.join(CARDS, hid + ".png")
    body = json.dumps({"contents": [{"parts": [part(src), {"text": ASK + (" " + note if note else "")}]}],
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
    ids = [a for a in sys.argv[1:] if not a.startswith("--")] or [h["id"] for h in json.load(open(os.path.join(g.DATA, "heroes.json"), encoding="utf-8"))["items"]]
    if "--missing" in sys.argv: ids = [i for i in ids if not os.path.exists(os.path.join(OUT, i + ".png"))]
    notes = json.load(open(os.path.join(g.ROOT, "tools", "out", "sd3_notes.json"), encoding="utf-8")) if os.path.exists(os.path.join(g.ROOT, "tools", "out", "sd3_notes.json")) else {}
    import concurrent.futures as cf
    with cf.ThreadPoolExecutor(4) as ex: list(ex.map(lambda h: make(key, h, notes.get(h, "")), ids))
