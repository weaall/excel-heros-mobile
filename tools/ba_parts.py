# -*- coding: utf-8 -*-
"""
The Blue Archive cross-check by PART (tools/ba_crosscheck.py scores a whole screen and every round
came back "models, UI and poses are all weaker" — no handle). The same element is cut from our
capture and from a Blue Archive screenshot of the same kind, and the judge scores only that element
(BA's = 10) with the concrete differences. Crops are fractions of the image (x0, y0, x1, y1).

    python tools/ba_parts.py [parts...]    (SHOTS=<dir>, default tools/out/shots2) → tools/out/ba_parts.json
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, io, json, os, sys, urllib.request, concurrent.futures as cf
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
from PIL import Image

OUT = os.path.join(g.ROOT, "tools", "out")
SHOTS = os.environ.get("SHOTS") or os.path.join(OUT, "shots2")
REF = os.path.join(OUT, "ba_ref")
MODEL = os.environ.get("CROSS_MODEL", "gemini-3.1-pro-preview")
# part: (our shot, our crop, BA ref, BA crop, what)
PARTS = {
    "battle_chars": ("07-Fight2.png", (0.06, 0.25, 0.48, 0.78), "ref02.png", (0.14, 0.37, 0.62, 0.96), "the SD characters fighting on the street (3D models, poses, shading)"),
    "battle_cards": ("07-Fight2.png", (0.6, 0.68, 0.99, 0.99), "ref02.png", (0.67, 0.75, 1.0, 0.98), "the skill cards and cost gauge at the bottom right (UI)"),
    "battle_env":   ("07-Fight2.png", (0.0, 0.0, 0.6, 0.45), "ref02.png", (0.0, 0.0, 0.62, 0.5), "the city street environment (buildings, props, road)"),
    "battle_hud":   ("07-Fight2.png", (0.62, 0.0, 1.0, 0.12), "ref02.png", (0.78, 0.0, 1.0, 0.13), "the top HUD bar (timer, counters, buttons)"),
    "result_chars": ("07-Win.png", (0.05, 0.12, 0.95, 0.72), "ref04.png", (0.15, 0.2, 0.9, 0.85), "the squad posing after winning (3D models, victory poses)"),
    "result_title": ("07-Win.png", (0.3, 0.0, 0.7, 0.2), "ref04.png", (0.3, 0.05, 0.7, 0.2), "the 'Battle Complete' title"),
    "formation_chars": ("08-Party.png", (0.1, 0.12, 0.8, 0.62), "ref06.png", (0.0, 0.1, 0.9, 0.62), "the squad standing in the formation slots (3D models, idle poses)"),
    "lobby_char":   ("05-Home.png", (0.25, 0.1, 0.6, 1.0), "ref13.png", (0.3, 0.1, 0.72, 1.0), "the character standing in the lobby (illustration)"),
    "lobby_menu":   ("05-Home.png", (0.0, 0.84, 1.0, 1.0), "ref13.png", (0.0, 0.84, 1.0, 1.0), "the bottom menu bar"),
}
ASK = ("Image 1 is from Blue Archive and defines 10/10. Image 2 is the same kind of element from our game: {what}. As a strict "
       "senior art director, score image 2 on the Blue Archive scale (10 = equal, 7 = a tier below, 5 = indie, 3 = prototype) and "
       "list the three most important concrete differences, each with a fix a developer can apply (sizes, colours, shapes, "
       "angles, timing). JSON only: {{\"score\": n, \"diffs\": [\"...\"]}}")


def crop(path, box, w=900):
    im = Image.open(path).convert("RGB"); W, H = im.size
    im = im.crop((int(box[0] * W), int(box[1] * H), int(box[2] * W), int(box[3] * H)))
    im = im.resize((w, max(1, round(w * im.height / im.width))), Image.LANCZOS)
    buf = io.BytesIO(); im.save(buf, "PNG")
    return {"inlineData": {"mimeType": "image/png", "data": base64.b64encode(buf.getvalue()).decode("ascii")}}


def call(key, parts):
    body = json.dumps({"contents": [{"parts": parts}], "generationConfig": {"temperature": 0.1, "responseMimeType": "application/json"}}).encode("utf-8")
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent", data=body, method="POST",
                                 headers={"Content-Type": "application/json", "x-goog-api-key": key})
    err = None
    for _ in range(3):
        try:
            with urllib.request.urlopen(req, timeout=400) as r: d = json.loads(r.read().decode("utf-8"))
            t = "".join(p.get("text", "") for c in d.get("candidates", []) for p in c.get("content", {}).get("parts", []) if not p.get("thought"))
            v = json.loads(t); return v[0] if isinstance(v, list) else v
        except Exception as e: err = e
    raise RuntimeError(err)


def judge(key, name, n):
    ours, obox, ref, rbox, what = PARTS[name]
    parts = [crop(os.path.join(REF, ref), rbox), crop(os.path.join(SHOTS, ours), obox), {"text": ASK.format(what=what)}]
    runs = [call(key, parts) for _ in range(n)]
    return name, {"score": sum(float(r["score"]) for r in runs) / n, "diffs": runs[0].get("diffs", [])}


if __name__ == "__main__":
    key = g.read_key(); names = sys.argv[1:] or list(PARTS); n = int(os.environ.get("REPEAT", "2"))
    with cf.ThreadPoolExecutor(5) as ex: res = dict(ex.map(lambda x: judge(key, x, n), names))
    path = os.path.join(OUT, "ba_parts.json")
    prev = json.load(open(path, encoding="utf-8")) if os.path.exists(path) else {}
    prev.update(res); json.dump(prev, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    for k, r in sorted(res.items(), key=lambda kv: kv[1]["score"]):
        print(f"{k:16s} {r['score']:.1f}")
        for d in r["diffs"]: print("     -", d)
