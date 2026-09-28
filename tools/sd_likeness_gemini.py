# -*- coding: utf-8 -*-
"""
How well each 3D SD matches its illustration, by Gemini's vision model: the front view and the
face close-up from a SdBasePreview lineup (one column per hero) next to the standing
illustration → a score and what is most off, so the next round fixes the worst first.

    python tools/sd_likeness_gemini.py tools/out/lineup/a  cfo,ceo,...   # the lineup dir and its ids, in order
Writes tools/out/likeness.json (merged by id) and prints the ranking.
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, io, json, os, sys, time, urllib.request, urllib.error
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

MODEL = os.environ.get("QA_MODEL", "gemini-3.8-flash")
STAND = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Standing")
OUT = os.path.join(g.ROOT, "tools", "out", "likeness.json")
ASK = ("Image 1 is a character illustration. Image 2 is our 3D chibi model of her (full body front view, then a face close-up). "
       "Colours matter less than SHAPE: judge whether the 3D chibi reads as the same character — hairstyle (length, bangs, tails, buns), "
       "outfit silhouette (jacket, skirt or trousers, legwear), accessories. Answer JSON only: "
       '{"score": 1-10, "hair": 1-10, "outfit": 1-10, "worst": "the single most wrong thing, a few words", '
       '"fix": one of "hair_len_longer","hair_len_shorter","add_tails","remove_tails","add_bun","bangs","outfit_top","outfit_skirt_len","legwear","accessory","colours","ok"}')


def png64(im, side=768):
    im = im.convert("RGB"); im.thumbnail((side, side))
    buf = io.BytesIO(); im.save(buf, "PNG"); return base64.b64encode(buf.getvalue()).decode("ascii")


def ask(key, ill, model):
    parts = [{"inlineData": {"mimeType": "image/png", "data": png64(ill)}}, {"inlineData": {"mimeType": "image/png", "data": png64(model)}}, {"text": ASK}]
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
    folder, ids = sys.argv[1], sys.argv[2].split(",")
    sheet = Image.open(os.path.join(folder, "sdbase_lineup.png"))
    W, H = sheet.size; n = len(ids); cw = W / n
    # rows: front, three-quarter, side, back (each 0.2 of the height), then the face close-ups
    key = g.read_key()
    res = json.load(open(OUT, encoding="utf-8")) if os.path.exists(OUT) else {}
    for i, hid in enumerate(ids):
        png = os.path.join(STAND, hid + ".png")
        if not os.path.exists(png): continue
        front = sheet.crop((int(i * cw), 0, int((i + 1) * cw), int(H * 0.2)))
        face = sheet.crop((int(i * cw), int(H * 0.8), int((i + 1) * cw), H))
        both = Image.new("RGB", (front.width, front.height + face.height), (255, 255, 255))
        both.paste(front, (0, 0)); both.paste(face, (0, front.height))
        a = ask(key, Image.open(png), both)
        if not a: print("  ?", hid); continue
        res[hid] = a
        print(f"  {hid:16s} {a.get('score')} hair {a.get('hair')} outfit {a.get('outfit')}  {a.get('fix', ''):18s} {a.get('worst', '')[:60]}")
    json.dump(res, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    rank = sorted(res.items(), key=lambda kv: kv[1].get("score", 0))
    print("worst:", ", ".join(f"{k}({v.get('score')})" for k, v in rank[:12]))
