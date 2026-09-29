# -*- coding: utf-8 -*-
"""
Illustrated banners for the notice board (the Blue Archive cross-check, 2026-09-30: "a generic list
and detail view — the reference's notices are illustrated banners with characters"). One wide
illustration per notice (Resources/Data/notices.json id), drawn from our own cast's cards so the
characters are ours, with no text in it (the title is laid over by the UI).

    python tools/notice_banners_gemini.py [ids...]   → Assets/ExcelHeroes/Resources/Art/Notice/<id>.png
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, io, json, os, sys, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
from PIL import Image

CARDS = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Cards")
OUT = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Notice")
# id: (the characters, by card; the scene)
SCENES = {
    "n_affinity": (["vlookup", "macro"], "two office heroines at a glowing holographic spreadsheet, colour-coded red / orange / blue type icons floating around them, one pointing confidently, the other typing"),
    "n_enemy3d": (["cso", "courier"], "two office heroines bracing playfully against cute chibi office-object monsters (a grumpy photocopier, a stapler with teeth, a floating virus blob) bursting out of a monitor, spreadsheet cells scattering"),
    "n_shop": (["barista", "welfare"], "two office heroines behind a bright office shop counter full of supply boxes, coffee cups and snacks, one presenting a gift box with a wink"),
    "n_action": (["pr_yoo", "guard"], "two office heroines in a dynamic action pose on a city street, one lunging forward with a tablet, the other ready, speed lines and bright impact bursts"),
    "n_event": (["hr_jung", "intern_min"], "two office heroines working late in a cosy night office with city lights through the window, a clock at midnight, one cheering with a coffee, a double-pay sparkle mood"),
    "n_rule": (["security_yang", "legal_yoon"], "two office heroines at a sleek office security gate, one holding up an ID badge, the other a clipboard with a checklist, a friendly but firm mood"),
}
ASK = ("Draw a wide promotional event banner illustration for a Japanese mobile gacha game, in a polished official anime illustration "
       "style with bright soft lighting and crisp line art. The characters are the ones in the reference images — keep their faces, "
       "hair, eye colours, glasses and outfits exactly. Scene: {scene}. Compose it as a banner: the characters large on the right two "
       "thirds, the left third lighter and simpler (room for a title laid over later), a soft bokeh office background with light "
       "rays. NO text, no letters, no logos, no watermark anywhere.")


def part(path):
    im = Image.open(path).convert("RGB"); im.thumbnail((768, 768)); buf = io.BytesIO(); im.save(buf, "PNG")
    return {"inlineData": {"mimeType": "image/png", "data": base64.b64encode(buf.getvalue()).decode("ascii")}}


def run(key, nid):
    chars, scene = SCENES[nid]
    parts = [part(os.path.join(CARDS, c + ".png")) for c in chars] + [{"text": ASK.format(scene=scene)}]
    body = json.dumps({"contents": [{"parts": parts}], "generationConfig": {"responseModalities": ["IMAGE"], "temperature": 0.6, "imageConfig": {"aspectRatio": "21:9"}}}).encode("utf-8")
    for model in g.MODELS:
        req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent", data=body, method="POST",
                                     headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=300) as r: d = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e: print(f"  {nid} {model}: HTTP {e.code}"); continue
        for c in d.get("candidates", []):
            for p in c.get("content", {}).get("parts", []):
                inline = p.get("inlineData") or p.get("inline_data")
                if inline and inline.get("data"):
                    im = Image.open(io.BytesIO(base64.b64decode(inline["data"]))).convert("RGB")
                    im.thumbnail((1344, 1344)); im.save(os.path.join(OUT, nid + ".png")); print("  ok", nid, im.size); return
    print("  FAILED", nid)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True); key = g.read_key()
    import concurrent.futures as cf
    with cf.ThreadPoolExecutor(3) as ex: list(ex.map(lambda n: run(key, n), sys.argv[1:] or list(SCENES)))
