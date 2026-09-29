# -*- coding: utf-8 -*-
"""
The hair-variety round (tools/hair_diversity_gemini.py → tools/out/hair_plan.json): each planned
character's card redrawn with ONLY the hairstyle and hair colour changed — face, eyes, outfit, pose,
framing and background kept (Gemini image edit, one image in).

    python tools/cards_hair.py                 # every id in the plan
    python tools/cards_hair.py guard cfo       # some
      → ArtSource/Cards_v2/<id>.png (the previous one kept as ArtSource/Cards_v2/_prev_hair/<id>.png)
Then: cards_v2_import.py <ids> · gen_standing_gemini.py <ids> · standing_v2.sh · SD 2D · sdspec style/hair · 3D.
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, json, os, shutil, sys, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

CARDS = os.path.join(g.ROOT, "ArtSource", "Cards_v2")
PLAN = os.path.join(g.ROOT, "tools", "out", "hair_plan.json")
ASK = ("This is a character card from our game (Blue Archive official-illustration finish). Redraw the SAME card changing ONLY the "
       "hair: the new hairstyle is {desc} The hair colour becomes {hair} ({style} style). Everything else stays exactly as it is — "
       "the same face and expression, eye colour, glasses and accessories, the same outfit and its colours, the same pose, hands, "
       "props, framing, camera distance and background. The whole head and the new hair fully in frame. No text, no frame, no watermark.")


def generate(key, r):
    hid = r["id"]; src = os.path.join(CARDS, hid + ".png")
    if not os.path.exists(src): print("  missing", hid); return False
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": base64.b64encode(open(src, "rb").read()).decode("ascii")}},
                                               {"text": ASK.format(desc=r["desc"], hair=r["hair"], style=r["style"])}]}],
                       "generationConfig": {"responseModalities": ["IMAGE"], "temperature": 0.5, "imageConfig": {"aspectRatio": "2:3"}}}).encode("utf-8")
    for model in g.MODELS:
        req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent", data=body, method="POST",
                                     headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=300) as resp: d = json.loads(resp.read().decode("utf-8"))
        except urllib.error.HTTPError as e: print(f"  {hid} {model}: HTTP {e.code}"); continue
        for c in d.get("candidates", []):
            for p in c.get("content", {}).get("parts", []):
                inline = p.get("inlineData") or p.get("inline_data")
                if inline and inline.get("data"):
                    prev = os.path.join(CARDS, "_prev_hair"); os.makedirs(prev, exist_ok=True)
                    if not os.path.exists(os.path.join(prev, hid + ".png")): shutil.copy(src, os.path.join(prev, hid + ".png"))
                    open(src, "wb").write(base64.b64decode(inline["data"])); print(f"  ok {hid} ({model})"); return True
    return False


if __name__ == "__main__":
    key = g.read_key()
    plan = {r["id"]: r for r in json.load(open(PLAN, encoding="utf-8"))}
    ids = sys.argv[1:] or list(plan)
    import concurrent.futures as cf
    with cf.ThreadPoolExecutor(4) as ex:
        list(ex.map(lambda h: generate(key, plan[h]) or print("  FAILED", h), ids))
