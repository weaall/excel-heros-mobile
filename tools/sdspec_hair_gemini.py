# -*- coding: utf-8 -*-
"""
Each hero's 3D hair as a recipe of sample hair parts (sdspec "hairParts", World/SdSample.Recipe):
Gemini's vision model gets the hero's illustration and the labelled reference of the eight
sample hairs (tools/out/hair_reference.png: front row, back row) and picks, by SHAPE only:

    front  — the fringe / bangs most like the illustration's
    side   — the side locks framing the face
    back   — the back hair (its fall, twin tails, a side ponytail, low tails…)
    extra  — "mika" (a small side bun), "haruka" (a big back bow) or "none"
    len    — short (jaw) | bob (below the chin) | shoulder | long (as the sample)

    python tools/sdspec_hair_gemini.py            # every hero with an illustration and a sample body
    python tools/sdspec_hair_gemini.py cfo ceo
A row with "hairParts" in "manual" is left alone. Colour is ignored: the hair is recoloured anyway.
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, json, os, sys, time, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

MODEL = os.environ.get("QA_MODEL", "gemini-3.8-flash")
SPEC = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Data", "sdspec.json")
STAND = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Standing")
REF = os.path.join(g.ROOT, "tools", "out", "hair_reference.png")
LIBS = ["haruka", "miku", "yuuka", "hikari", "mika", "reisa", "natsu"]   # not kayoko: her black / white split survives any recolour
ASK = ("Image 1 is a character illustration. Image 2 shows eight 3D hairstyles we can build from, labelled in red "
       "(top row from the front, bottom row from the back). Ignore colours, hats, horns and bodies; compare hair SHAPE only. "
       "Choose parts to rebuild image 1's hairstyle: 'front' = whose fringe/bangs is closest; 'side' = whose side locks by the face; "
       "'back' = whose back hair is closest (long straight fall, twin tails, low twin tails, side ponytail, short with bow…); "
       "'extra' = 'mika' if the character has a bun, 'haruka' if a big bow at the back, else 'none'; "
       "'len' = the back hair's length in image 1: 'short' (to the jaw), 'bob' (just below the chin), 'shoulder', or 'long'. "
       "For short or bob hair choose a 'back' with a plain fall (hikari, mika or haruka), never tails. "
       "Also: 'badge' = true if the character wears an ID badge / lanyard; 'bow' = true if a hair bow or ribbon in the hair. "
       f"Names: {', '.join(LIBS)} (kayoko is not available). Answer JSON only: " + '{"front":"","side":"","back":"","extra":"","len":"","badge":false,"bow":false,"why":""}')


def b64(path, side=1024):
    from PIL import Image
    import io
    im = Image.open(path).convert("RGBA"); im.thumbnail((side, side))
    bg = Image.new("RGB", im.size, (255, 255, 255)); bg.paste(im, (0, 0), im)
    buf = io.BytesIO(); bg.save(buf, "PNG"); return base64.b64encode(buf.getvalue()).decode("ascii")


def ask(key, png):
    parts = [{"inlineData": {"mimeType": "image/png", "data": b64(png)}}, {"inlineData": {"mimeType": "image/png", "data": b64(REF, 1600)}}, {"text": ASK}]
    body = json.dumps({"contents": [{"parts": parts}], "generationConfig": {"responseMimeType": "application/json", "temperature": 0}}).encode("utf-8")
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent",
                                 data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
    for _ in range(3):
        try:
            with urllib.request.urlopen(req, timeout=120) as r: data = json.loads(r.read().decode("utf-8"))
            return json.loads(data["candidates"][0]["content"]["parts"][0]["text"])
        except (urllib.error.HTTPError, urllib.error.URLError, KeyError, json.JSONDecodeError) as e:
            print(f"    retry ({type(e).__name__})"); time.sleep(3)
    return None


if __name__ == "__main__":
    only = [a for a in sys.argv[1:] if not a.startswith("--")]
    spec = json.load(open(SPEC, encoding="utf-8")); key = g.read_key()
    for row in spec["items"]:
        hid = row["id"]
        if only and hid not in only: continue
        if "hairParts" in (row.get("manual") or []) or not row.get("body"): continue
        png = os.path.join(STAND, hid + ".png")
        if not os.path.exists(png): continue
        a = ask(key, png)
        if not a: print(f"  ?  {hid}"); continue
        def ok(v, allowed): return v if v in allowed else ""
        front = ok(a.get("front"), LIBS) or "haruka"
        recipe = f"front={front};side={ok(a.get('side'), LIBS) or front};back={ok(a.get('back'), LIBS) or front};" \
                 f"extra={ok(a.get('extra'), ['mika', 'haruka']) or 'none'};len={ok(a.get('len'), ['short', 'bob', 'shoulder', 'long']) or 'long'}"
        row["hairParts"] = recipe
        acc = (["nameplate"] if a.get("badge") else []) + (["ribbon"] if a.get("bow") and "haruka" not in recipe.split("extra=")[1][:6] else [])
        row["acc"] = ",".join(acc)
        print(f"  {hid:16s} {recipe:60s} {a.get('why', '')[:60]}")
    with open(SPEC, "w", encoding="utf-8", newline="\n") as f: json.dump(spec, f, ensure_ascii=False, indent=1)
