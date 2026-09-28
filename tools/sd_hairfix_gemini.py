# -*- coding: utf-8 -*-
"""
The hair recipe corrected with the render in view: for each hero the likeness check
(tools/sd_likeness_gemini.py → tools/out/likeness.json) flagged on the hair, Gemini's vision model
gets the illustration, our current 3D render (front + face, cut from a SdBasePreview lineup) and the
labelled sheet of the sample hairs (tools/out/hair_reference.png), and returns a corrected recipe.
The old one is kept in "hairPrev" so tools/sd_likeness_gemini.py can decide which stays.

    python tools/sd_hairfix_gemini.py tools/out/lineup_ids.txt tools/out/lineup
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import json, os, re, sys
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import sd_likeness_gemini as lk
import sdspec_hair_gemini as hg

# the samples whose hair has a back fall once the tails are their own region (SdRefHairLib.TailTags)
BACKS = ["haruka", "hikari", "mika", "yuuka", "kayoko"]
FIXES = {"bangs", "hair_len_shorter", "hair_len_longer", "add_bun", "remove_tails", "add_tails"}
ASK = ("Image 1: a character illustration. Image 2: our 3D chibi of her (full body front, then the face). "
       "Image 3: the 3D hair parts we build from (red labels; top row front views, bottom row back views). "
       "Our current recipe is: {recipe}. A reviewer says the hair's worst problem is: '{worst}'. "
       "Give a corrected recipe that fixes it, changing as little as needed. Parts: front (fringe), side (locks by the face), "
       "back (the fall: only haruka, hikari, mika, yuuka or kayoko — a straight heavy curtain — have one; kayoko never as front), extra ('mika' a small bun, 'haruka' a big bow, 'none'), tails ('reisa' twin tails, 'miku' very long twin tails, "
       "'natsu' one side ponytail, 'none'), len ('short' jaw, 'bob' below chin, 'shoulder', 'long'); and the fringe's cut: "
       "bang 0.7-1.3 (its length: short above the brows .. long into the eyes), sweep -1..1 (swept to her right .. her left), split 0-1 (parted at the middle). "
       f"Names: {', '.join(hg.LIBS)}. Answer JSON only: " + '{"front":"","side":"","back":"","extra":"","tails":"","len":"","bang":1,"sweep":0,"split":0,"why":""}')


def crops(folder, ids, hid):
    sheet = Image.open(os.path.join(folder, "sdbase_lineup.png")); W, H = sheet.size; cw = W / len(ids); i = ids.index(hid)
    front = sheet.crop((int(i * cw), 0, int((i + 1) * cw), int(H * 0.2))); face = sheet.crop((int(i * cw), int(H * 0.8), int((i + 1) * cw), H))
    both = Image.new("RGB", (front.width, front.height + face.height), (255, 255, 255)); both.paste(front, (0, 0)); both.paste(face, (0, front.height))
    return both


if __name__ == "__main__":
    batches = [l.strip().split(",") for l in open(sys.argv[1], encoding="utf-8") if l.strip()]
    root = sys.argv[2]
    like = json.load(open(lk.OUT, encoding="utf-8"))
    spec = json.load(open(hg.SPEC, encoding="utf-8")); key = g.read_key()
    rows = {r["id"]: r for r in spec["items"]}
    import base64, io, time, urllib.request, urllib.error
    for n, ids in enumerate(batches, 1):
        for hid in ids:
            v = like.get(hid)
            if not v or v.get("fix") not in FIXES or hid not in rows: continue
            row = rows[hid]; rec = row.get("hairParts") or ""
            prompt = ASK.replace("{recipe}", rec).replace("{worst}", v.get("worst", ""))
            parts = [{"inlineData": {"mimeType": "image/png", "data": lk.png64(Image.open(os.path.join(lk.STAND, hid + ".png")))}},
                     {"inlineData": {"mimeType": "image/png", "data": lk.png64(crops(os.path.join(root, str(n)), ids, hid))}},
                     {"inlineData": {"mimeType": "image/png", "data": lk.png64(Image.open(hg.REF), 1600)}}, {"text": prompt}]
            body = json.dumps({"contents": [{"parts": parts}], "generationConfig": {"responseMimeType": "application/json", "temperature": 0}}).encode("utf-8")
            req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{lk.MODEL}:generateContent",
                                         data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
            a = None
            for _ in range(3):
                try:
                    with urllib.request.urlopen(req, timeout=120) as r: a = json.loads(json.loads(r.read().decode("utf-8"))["candidates"][0]["content"]["parts"][0]["text"]); break
                except (OSError, KeyError, json.JSONDecodeError): time.sleep(3)
            if not a: print("  ?", hid); continue
            def ok(x, allowed, fb): return x if x in allowed else fb
            keep = dict(p.split("=") for p in rec.split(";") if "=" in p)
            new = {"front": ok(a.get("front"), hg.LIBS, keep.get("front", "haruka")), "side": ok(a.get("side"), hg.LIBS + ["kayoko"], keep.get("side", "haruka")),
                   "back": ok(a.get("back"), BACKS, keep.get("back", "haruka")), "extra": ok(a.get("extra"), ["mika", "haruka", "none"], "none"),
                   "len": ok(a.get("len"), ["short", "bob", "shoulder", "long"], keep.get("len", "long"))}
            tails = ok(a.get("tails"), ["reisa", "miku", "natsu", "none"], "none")
            out = ";".join(f"{k}={v2}" for k, v2 in new.items())
            if tails != "none": out += f";tails={tails}"
            for k2, lo, hi, dv in (("bang", 0.7, 1.3, 1.0), ("sweep", -1, 1, 0.0), ("split", 0, 1, 0.0)):
                try: x = max(lo, min(hi, float(a.get(k2, dv))))
                except (TypeError, ValueError): continue
                if abs(x - dv) >= 0.08: out += f";{k2}={x:.2f}"
            for k in ("vol", "fall", "wave", "spread", "gather", "curl", "slant"):
                if k in keep and (k != "fall" or new["len"] == "long"): out += f";{k}={keep[k]}"
            if out != rec:
                row["hairPrev"] = rec; row["hairParts"] = out
            print(f"  {hid:16s} {rec}\n  {'':16s} -> {out}   ({a.get('why', '')[:60]})")
    with open(hg.SPEC, "w", encoding="utf-8", newline="\n") as f: json.dump(spec, f, ensure_ascii=False, indent=1)
