# -*- coding: utf-8 -*-
"""
Each hero's face (sdspec "face", World/SdSample.SwapFace): Gemini's vision model scores the eight
sample faces (tools/out/face_reference.png — one hero wearing each, labelled) 0-10 against the
illustration's eyes and brows, then an assignment with a cap per face keeps the cast from sharing
two or three — so heroes on the same body still look at you differently.

    python tools/sdspec_face_gemini.py            # score what is missing (tools/out/facescores.json), assign, write
    python tools/sdspec_face_gemini.py --rescore
A row with "face" in "manual" keeps it.
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import json, math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import sd_likeness_gemini as lk
import sdspec_body_gemini as sb
import sdspec_diverse_gemini as dv
from PIL import Image

KEYS = ["hikari", "hayase_yuuka", "haruka", "kayoko_dress_ver_", "mika", "yutori_natsu", "reisa", "hatsune_miku"]
REF = os.path.join(g.ROOT, "tools", "out", "face_reference.png")
CACHE = os.path.join(g.ROOT, "tools", "out", "facescores.json")
ASK = ("Image 1 is a character illustration. Image 2 shows the same 3D chibi head wearing eight different FACES (labelled in red). "
       "Ignore hair and colours; compare the EYES (shape, size, how round / sharp / droopy / sleepy, the lashes) and the brows' mood. "
       "Score how well each face matches the illustration's face, 0-10. Answer JSON only: {\"scores\": {label: number, ...}}")


def ask(key, png):
    import urllib.request, urllib.error, time
    parts = [{"inlineData": {"mimeType": "image/png", "data": lk.png64(Image.open(png))}},
             {"inlineData": {"mimeType": "image/png", "data": lk.png64(Image.open(REF), 1200)}}, {"text": ASK}]
    body = json.dumps({"contents": [{"parts": parts}], "generationConfig": {"responseMimeType": "application/json", "temperature": 0}}).encode("utf-8")
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{lk.MODEL}:generateContent",
                                 data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
    for _ in range(3):
        try:
            with urllib.request.urlopen(req, timeout=120) as r: return json.loads(json.loads(r.read().decode("utf-8"))["candidates"][0]["content"]["parts"][0]["text"])
        except (OSError, KeyError, json.JSONDecodeError): time.sleep(3)
    return None


if __name__ == "__main__":
    spec = json.load(open(sb.SPEC, encoding="utf-8")); key = g.read_key()
    cache = json.load(open(CACHE, encoding="utf-8")) if os.path.exists(CACHE) else {}
    rows = [r for r in spec["items"] if r.get("body") and os.path.exists(os.path.join(sb.STAND, r["id"] + ".png"))]
    for r in rows:
        if r["id"] in cache and "--rescore" not in sys.argv: continue
        a = ask(key, os.path.join(sb.STAND, r["id"] + ".png"))
        if not a: print("  ?", r["id"]); continue
        cache[r["id"]] = {k: float(v) for k, v in (a.get("scores") or {}).items() if k in KEYS}
        json.dump(cache, open(CACHE, "w", encoding="utf-8"), indent=1)
    heroes = [r["id"] for r in rows if r["id"] in cache]
    byid = {r["id"]: r for r in rows}
    fixed = {h: byid[h]["face"] for h in heroes if "face" in (byid[h].get("manual") or [])}
    pick = dv.assign(heroes, cache, KEYS, math.ceil(len(heroes) / len(KEYS)) + 2, fixed, step=0.6)
    from collections import Counter
    for h in heroes:
        # the body's own face needs no transplant
        byid[h]["face"] = "" if pick[h] == byid[h]["body"] or (pick[h] == "hayase_yuuka" and byid[h]["body"] == "hayase_yuuka_gym_ver_") else pick[h]
        print(f"  {h:16s} {pick[h]:20s} ({cache[h].get(pick[h])}, best {max(cache[h], key=cache[h].get)} {max(cache[h].values())})")
    print(Counter(pick.values()))
    with open(sb.SPEC, "w", encoding="utf-8", newline="\n") as f: json.dump(spec, f, ensure_ascii=False, indent=1)
