# -*- coding: utf-8 -*-
"""
The long-haired heroes' back hair, spread so they do not all wear one silhouette: Gemini's vision
model scores the five sample backs (tools/out/back_reference.png: haruka, hikari, mika, yuuka,
kayoko — front and back views on one head) 0-10 against each illustration, then an assignment
with a cap per back (about a fifth of them each, +1) keeps the best matches. Only heroes whose
recipe has len=long or len=shoulder; "hairParts" in "manual" keeps its back.

    python tools/sdspec_longhair_gemini.py [--rescore]
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import json, math, os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import sd_likeness_gemini as lk
import sdspec_body_gemini as sb
import sdspec_diverse_gemini as dv
import sdspec_face_gemini as fc
from PIL import Image

KEYS = ["haruka", "hikari", "mika", "yuuka", "kayoko"]
fc.REF = os.path.join(g.ROOT, "tools", "out", "back_reference.png")
fc.ASK = ("Image 1 is a character illustration. Image 2 shows one 3D chibi head wearing five different LONG back hairs (labelled in red; "
          "top row front view, bottom row back view): haruka = a short layered back with long thin side locks, hikari = long straight "
          "smooth fall, mika = long wavy fall with big separate locks, yuuka = long straight fall with blunt ends, kayoko = a heavy straight curtain. "
          "Ignore colours and the face. Score how well each matches the illustration's hair silhouette from behind and its length, 0-10. "
          'Answer JSON only: {"scores": {label: number, ...}}')
CACHE = os.path.join(g.ROOT, "tools", "out", "backscores.json")

if __name__ == "__main__":
    spec = json.load(open(sb.SPEC, encoding="utf-8")); key = g.read_key()
    cache = json.load(open(CACHE, encoding="utf-8")) if os.path.exists(CACHE) else {}
    rows = [r for r in spec["items"] if re.search(r"len=(long|shoulder)", r.get("hairParts") or "") and os.path.exists(os.path.join(sb.STAND, r["id"] + ".png"))]
    for r in rows:
        if r["id"] in cache and "--rescore" not in sys.argv: continue
        a = fc.ask(key, os.path.join(sb.STAND, r["id"] + ".png"))
        if not a: print("  ?", r["id"]); continue
        cache[r["id"]] = {k: float(v) for k, v in (a.get("scores") or {}).items() if k in KEYS}
        json.dump(cache, open(CACHE, "w", encoding="utf-8"), indent=1)
    heroes = [r["id"] for r in rows if r["id"] in cache]
    byid = {r["id"]: r for r in rows}
    fixed = {h: re.search(r"back=([a-z]+)", byid[h]["hairParts"]).group(1) for h in heroes if "hairParts" in (byid[h].get("manual") or [])}
    pick = dv.assign(heroes, cache, KEYS, math.ceil(len(heroes) / len(KEYS)) + 1, fixed, step=0.5)
    from collections import Counter
    for h in heroes:
        rec = byid[h]["hairParts"]
        rec = re.sub(r"back=[a-z]+", "back=" + pick[h], rec)
        # a straight back with straight sides: kayoko's and hikari's hang with their own locks
        if pick[h] in ("kayoko", "hikari") and re.search(r"side=mika", rec): rec = rec.replace("side=mika", "side=" + pick[h])
        byid[h]["hairParts"] = rec
        print(f"  {h:16s} {pick[h]:8s} ({cache[h].get(pick[h])}, best {max(cache[h], key=cache[h].get)} {max(cache[h].values())})  {rec}")
    print(Counter(pick.values()))
    with open(sb.SPEC, "w", encoding="utf-8", newline="\n") as f: json.dump(spec, f, ensure_ascii=False, indent=1)
