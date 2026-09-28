# -*- coding: utf-8 -*-
"""
Long hair, told apart: Gemini's vision model looks at the long-haired heroes side by side (a
SdBasePreview lineup of them, front row and back row, numbered) and names the pairs whose hair reads
the same; for each it says which one to change and how, within our knobs, looking at that hero's
own illustration so the change moves TOWARD it. The change is written as "hairPrev" + new
"hairParts" for tools/sd_likeness_gemini.py to accept (hair score not worse) or undo.

    python tools/sd_hairdistinct_gemini.py tools/out/longhair tools/out/longhair_ids.txt
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, io, json, os, re, sys, time, urllib.request
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import sd_likeness_gemini as lk
import sdspec_body_gemini as sb

KNOBS = ("front (haruka|hikari|mika|yuuka|natsu|reisa|miku), side (the same names or kayoko), back (haruka|hikari|mika|yuuka|kayoko), "
         "len (short|bob|shoulder|long), extra (mika a bun|haruka a bow|none), tails (reisa twin|miku long twin|natsu side ponytail|none), "
         "vol 0.9-1.18, fall 0.8-1.3, wave 0-1.2, spread -1..1, gather 0-1 (low ponytail), curl -1..1 (+ ends in, - flick out), "
         "slant -1..1 (a slanted cut: + longer in front, - longer behind)")


def call(key, parts):
    body = json.dumps({"contents": [{"parts": parts}], "generationConfig": {"responseMimeType": "application/json", "temperature": 0}}).encode("utf-8")
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{lk.MODEL}:generateContent",
                                 data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
    for _ in range(3):
        try:
            with urllib.request.urlopen(req, timeout=180) as r: return json.loads(json.loads(r.read().decode("utf-8"))["candidates"][0]["content"]["parts"][0]["text"])
        except (OSError, KeyError, json.JSONDecodeError): time.sleep(3)
    return None


if __name__ == "__main__":
    folder, idfile = sys.argv[1], sys.argv[2]
    ids = open(idfile, encoding="utf-8").read().strip().split(",")
    sheet = Image.open(os.path.join(folder, "sdbase_lineup.png")); W, H = sheet.size; cw = W // len(ids)
    lab = Image.new("RGB", (W, int(H * 0.4) + 34), "white"); d = ImageDraw.Draw(lab)
    try: font = ImageFont.truetype("arial.ttf", 26)
    except OSError: font = None
    lab.paste(sheet.crop((0, 0, W, int(H * 0.2))), (0, 34)); lab.paste(sheet.crop((0, int(H * 0.6), W, int(H * 0.8))), (0, 34 + int(H * 0.2)))
    for i in range(len(ids)): d.text((i * cw + 8, 4), str(i + 1), fill=(220, 0, 0), font=font)
    key = g.read_key()
    ask = (f"These are {len(ids)} 3D chibi characters (numbered in red), front row and back row. List the PAIRS whose HAIR "
           "looks nearly the same (silhouette, length, fringe, waves, how the ends fall) — ignore colour. Only real near-duplicates. "
           'Answer JSON only: {"pairs": [[a, b], ...]}')
    a = call(key, [{"inlineData": {"mimeType": "image/png", "data": lk.png64(lab, 2400)}}, {"text": ask}])
    pairs = [(int(p[0]), int(p[1])) for p in (a or {}).get("pairs", []) if len(p) == 2 and 1 <= int(p[0]) <= len(ids) and 1 <= int(p[1]) <= len(ids)]
    print("near-duplicates:", [(ids[x - 1], ids[y - 1]) for x, y in pairs])
    spec = json.load(open(sb.SPEC, encoding="utf-8")); rows = {r["id"]: r for r in spec["items"]}
    changed = set()
    for x, y in pairs:
        h1, h2 = ids[x - 1], ids[y - 1]
        if h1 in changed or h2 in changed: continue
        def crop(i):
            fr = sheet.crop((i * cw, 0, (i + 1) * cw, int(H * 0.2))); bk = sheet.crop((i * cw, int(H * 0.6), (i + 1) * cw, int(H * 0.8)))
            o = Image.new("RGB", (cw * 2, fr.height), "white"); o.paste(fr, (0, 0)); o.paste(bk, (cw, 0)); return o
        parts = []
        for h, i in ((h1, x - 1), (h2, y - 1)):
            parts += [{"inlineData": {"mimeType": "image/png", "data": lk.png64(Image.open(os.path.join(lk.STAND, h + ".png")))}},
                      {"inlineData": {"mimeType": "image/png", "data": lk.png64(crop(i))}}]
        q = (f"Images 1-2: character A's illustration and our 3D chibi of her (front, back). Images 3-4: the same for character B. "
             f"Their 3D hairs look too alike. A's recipe: {rows[h1]['hairParts']}. B's recipe: {rows[h2]['hairParts']}. "
             f"Change ONE of them so they differ clearly, moving that one closer to HER OWN illustration. Knobs: {KNOBS}. "
             'Answer JSON only: {"who": "A" | "B", "set": {knob: value, ...}, "why": ""}')
        r = call(key, parts + [{"text": q}])
        if not r or r.get("who") not in ("A", "B") or not isinstance(r.get("set"), dict): continue
        h = h1 if r["who"] == "A" else h2
        rec = rows[h]["hairParts"]; new = rec
        for k, v in r["set"].items():
            if k not in ("front", "side", "back", "len", "extra", "tails", "vol", "fall", "wave", "spread", "gather", "curl", "slant"): continue
            v = str(v)
            if k == "front" and v == "kayoko": continue
            if k == "back" and v not in ("haruka", "hikari", "mika", "yuuka", "kayoko"): continue
            if k == "len" and v not in ("short", "bob", "shoulder", "long"): continue
            if k == "extra" and v not in ("mika", "haruka", "none"): continue
            if k == "tails" and v not in ("reisa", "miku", "natsu", "none"): continue
            new = re.sub(rf"(^|;){k}=[^;]*", "", new)
            try: v = f"{float(v):.2f}"
            except ValueError: pass
            new = (new + f";{k}={v}").strip(";")
        if new != rec:
            rows[h]["hairPrev"] = rec; rows[h]["hairParts"] = new; changed.add(h)
            print(f"  {h:16s} {rec}\n  {'':16s} -> {new}   ({r.get('why', '')[:70]})")
    with open(sb.SPEC, "w", encoding="utf-8", newline="\n") as f: json.dump(spec, f, ensure_ascii=False, indent=1)
    print("changed:", ",".join(sorted(changed)))
