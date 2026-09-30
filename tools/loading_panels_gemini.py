# -*- coding: utf-8 -*-
"""
The loading screen's SD comic panels (the BA cross-check on the SD art: "a plain line-up, no scene, no
interaction, no presentation" — the reference shows its SDs in little office-life gag panels while it
loads). Each panel is one small chibi scene of two or three of OUR members, drawn from their own SD
pose art (Resources/Art/SDPose — only our art goes in), in the same SD style. The title is drawn by
the game (LoadingScreen), not in the picture.

    python tools/loading_panels_gemini.py [n ...]   → Assets/ExcelHeroes/Resources/Art/Loading/panel_<n>.png
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, io, json, os, sys, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
from PIL import Image

SD = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "SDPose")
OUT = os.environ.get("PANEL_OUT") or os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Loading")
# v2 (the BA cross-check on v1: "backgrounds too detailed, clash with the chibis; proportions not exaggerated enough"):
MINI = os.environ.get("MINI") == "1"
ASK_MINI = (" Draw them as MINI chibis — even rounder and more squashed than the images, about 2 heads tall, stubby limbs, big "
            "expressive faces, thick clean dark outlines — and keep the background almost empty: a flat pale tint with only the one or "
            "two props the gag needs (no walls, windows or furniture detail).")
PANELS = [
    (["intern", "vlookup"], "an office desk at night: the two panic in front of a frozen monitor showing a spinning wheel over a spreadsheet — "
     "the first clutches their head with big teary eyes, the second pushes up their glasses and types furiously; sweat drops, a tiny '#REF!' note stuck on the screen"),
    (["cfo", "secretary_yun"], "the first searches the office with a magnifying glass looking puzzled, while the second hides behind a tall wobbling stack of files, "
     "peeking out with a guilty smile; question marks and a small dust cloud"),
    (["barista", "dev_lead"], "the break room: the second lies face-down on a table, completely drained with a ghostly sigh; the first holds out a steaming cup of "
     "coffee with a sparkling smile and a tiny heart; a coffee machine behind"),
    (["ceo", "cco"], "a meeting room: the first presents proudly at a whiteboard covered in rising charts, pointer raised; the second dozes in a chair with a "
     "snot bubble and a floating 'zzz'; a projector glow"),
    (["acct_lead", "audit_han"], "month-end closing at night: both are buried up to their shoulders in paperwork and receipts, one frantically hitting a big "
     "calculator, the other holding up a single receipt with a dramatic gasp; the window shows a dark city"),
]
ASK = ("The images are two of our game's SD (chibi) characters. Draw ONE small comic panel in exactly their SD style (same faces, hair, "
       "outfits, colours, head size — each character recognisably the same): {scene}. Cute, lively and funny, like the office-life gag panels "
       "on a mobile gacha game's loading screen: full bodies, clear expressions, simple soft background of the place with a few props, clean "
       "thin line art, flat pastel cel shading, a little motion and emotion marks. Landscape 4:3, the scene filling the frame, no speech "
       "bubbles, no text, no border, no watermark.")


def part(path):
    im = Image.open(path).convert("RGBA"); bg = Image.new("RGBA", im.size, (255, 255, 255, 255)); bg.alpha_composite(im)
    buf = io.BytesIO(); bg.convert("RGB").save(buf, "PNG")
    return {"inlineData": {"mimeType": "image/png", "data": base64.b64encode(buf.getvalue()).decode("ascii")}}


def make(key, n):
    ids, scene = PANELS[n]
    parts = [part(os.path.join(SD, i + ".png")) for i in ids if os.path.exists(os.path.join(SD, i + ".png"))]
    body = json.dumps({"contents": [{"parts": parts + [{"text": ASK.format(scene=scene) + (ASK_MINI if MINI else "")}]}],
                       "generationConfig": {"responseModalities": ["IMAGE"], "temperature": 0.6, "imageConfig": {"aspectRatio": "4:3"}}}).encode("utf-8")
    for model in g.MODELS:
        req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent", data=body, method="POST",
                                     headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=300) as r: d = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e: print(f"  {n} {model}: HTTP {e.code}"); continue
        for c in d.get("candidates", []):
            for p in c.get("content", {}).get("parts", []):
                inline = p.get("inlineData") or p.get("inline_data")
                if inline and inline.get("data"):
                    im = Image.open(io.BytesIO(base64.b64decode(inline["data"]))).convert("RGB"); im.thumbnail((1024, 1024))
                    im.save(os.path.join(OUT, f"panel_{n}.png")); print(f"  ok panel_{n} ({model})"); return True
    print("  FAILED", n); return False


if __name__ == "__main__":
    key = g.read_key(); os.makedirs(OUT, exist_ok=True)
    ns = [int(a) for a in sys.argv[1:]] or list(range(len(PANELS)))
    import concurrent.futures as cf
    with cf.ThreadPoolExecutor(5) as ex: list(ex.map(lambda n: make(key, n), ns))
