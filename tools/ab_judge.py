# -*- coding: utf-8 -*-
"""
A/B: is a change a step toward Blue Archive? The absolute cross-check (ba_crosscheck / ba_parts) swings
±1 run to run, so a single change can't be read off it. Here the judge sees the Blue Archive screen and
our screen before (A) and after (B) — unlabelled, both orders, several times — and says which of the two
is closer to Blue Archive's quality. B's win rate > 0.6 keeps a change, < 0.4 reverts it.

    python tools/ab_judge.py <before.png> <after.png> <ba_ref.png> ["what the screen is"] [--n 3] [--crop x0,y0,x1,y1]
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, io, json, os, sys, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
from PIL import Image

MODEL = os.environ.get("CROSS_MODEL", "gemini-3.1-pro-preview")
ASK = ("Image 1 is from Blue Archive (the quality bar). Images 2 and 3 are two versions of the same screen of our game ({what}). "
       "Which of images 2 and 3 is closer to Blue Archive's overall quality (3D models, shading, poses, environment, layout, UI)? "
       "Be decisive. JSON only: {{\"better\": 2 or 3, \"why\": \"short\"}}")


def part(path, crop=None, w=1100):
    im = Image.open(path).convert("RGB")
    if crop:
        W, H = im.size; im = im.crop((int(crop[0] * W), int(crop[1] * H), int(crop[2] * W), int(crop[3] * H)))
    im = im.resize((w, round(w * im.height / im.width)), Image.LANCZOS)
    buf = io.BytesIO(); im.save(buf, "PNG")
    return {"inlineData": {"mimeType": "image/png", "data": base64.b64encode(buf.getvalue()).decode("ascii")}}


def ask(key, parts):
    body = json.dumps({"contents": [{"parts": parts}], "generationConfig": {"temperature": 0.4, "responseMimeType": "application/json"}}).encode("utf-8")
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent", data=body, method="POST",
                                 headers={"Content-Type": "application/json", "x-goog-api-key": key})
    for _ in range(3):
        try:
            with urllib.request.urlopen(req, timeout=300) as r: d = json.loads(r.read().decode("utf-8"))
            t = "".join(p.get("text", "") for c in d.get("candidates", []) for p in c.get("content", {}).get("parts", []) if not p.get("thought"))
            v = json.loads(t); v = v[0] if isinstance(v, list) else v
            return int(v["better"]), v.get("why", "")
        except Exception: pass
    return 0, "error"


if __name__ == "__main__":
    args = sys.argv[1:]; n = 3; crop = None
    if "--n" in args: i = args.index("--n"); n = int(args[i + 1]); del args[i:i + 2]
    if "--crop" in args: i = args.index("--crop"); crop = [float(x) for x in args[i + 1].split(",")]; del args[i:i + 2]
    a, b, ref = args[:3]; what = args[3] if len(args) > 3 else "a game screen"
    key = g.read_key(); pa, pb, pr = part(a, crop), part(b, crop), part(ref)
    wins, reasons = 0, []
    import concurrent.futures as cf
    def one(k):
        if k % 2 == 0: v, why = ask(key, [pr, pa, pb, {"text": ASK.format(what=what)}]); return (v == 3), why
        v, why = ask(key, [pr, pb, pa, {"text": ASK.format(what=what)}]); return (v == 2), why
    with cf.ThreadPoolExecutor(6) as ex: res = list(ex.map(one, range(n * 2)))
    wins = sum(1 for w, _ in res if w)
    print(f"B wins {wins}/{len(res)} = {wins / len(res):.2f}")
    for w, why in res: print("  ", "B" if w else "A", "|", why[:160])
