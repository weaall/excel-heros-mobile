# -*- coding: utf-8 -*-
"""
The user's bar (2026-09-30): "is this really Blue Archive grade — graphics, motion, layout, UI —
cross-checked by Gemini, 9 / 10 or better". Each of our screens is paired with the Blue Archive
screenshot of the same kind (tools/out/ba_ref/refNN.png, the user's own collection; only ever SHOWN
to the judge, never fed to a generator) and scored two ways:

  anchored — image 1 is Blue Archive and defines 10; image 2 (ours) is rated on that scale for each
             axis: graphics (art, 3D, rendering, effects), layout, ui (components, typography,
             polish), motion-readability (what the frame says about animation: poses, impact, FX),
             plus the three biggest gaps with fixes;
  blind    — both unlabelled, in both orders, each scored 1-10 (catches a judge that is kind to "ours").

    python tools/ba_crosscheck.py [pairs...]      (pair names from PAIRS; default all)
      SHOTS=<dir> for our captures (default tools/out/shots2) → tools/out/ba_crosscheck.json
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, json, os, sys, urllib.request, concurrent.futures as cf
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

OUT = os.path.join(g.ROOT, "tools", "out")
SHOTS = os.environ.get("SHOTS") or os.path.join(OUT, "shots2")
REF = os.path.join(OUT, "ba_ref")
MODEL = os.environ.get("CROSS_MODEL", "gemini-3.1-pro-preview")
# name: (our image, relative to SHOTS unless absolute-ish with a folder; BA refs; what the screen is)
PAIRS = {
    "lobby":     ("05-Home.png", ["ref01.png", "ref03.png", "ref13.png"], "the main lobby"),
    "battle":    ("07-Fight2.png", ["ref02.png", "ref14.png"], "a battle in progress"),
    "result":    ("07-Win.png", ["ref04.png"], "the battle result"),
    "formation": ("08-Party.png", ["ref06.png"], "the squad formation screen"),
    "profile":   ("17-Detail.png", ["ref10.png"], "a character profile page"),
    "roster":    ("07-Roster.png", ["ref12.png"], "the character roster"),
    "notice":    ("24-Notice.png", ["ref00.png"], "the notice / event board"),
    "sd2d":      ("@sd_lineup.png", ["ref07.png", "ref09.png"], "2D SD (chibi) character illustrations"),
    "model3d":   ("@model_lineup.png", ["ref11.png", "ref06.png"], "3D SD character models"),
}
AXES = ["graphics", "layout", "ui", "motion"]
ANCHOR = ("Image(s) 1..{n} are from Blue Archive — the benchmark; they define 10/10. The LAST image is from our game, the same kind of "
          "screen: {what}. You are a strict senior art director / UI lead from a top gacha studio. Rate OUR image on the Blue "
          "Archive scale (10 = equal to Blue Archive, 9 = nearly indistinguishable in quality, 7 = clearly a tier below, 5 = "
          "indie, 3 = prototype) for each axis: graphics (art, 3D models, rendering, effects), layout (composition, spacing, "
          "hierarchy), ui (components, typography, polish, consistency), motion (what the frame shows of animation quality: poses, "
          "impact, effects, liveliness; for a static screen, how alive and animated it looks). Then the three biggest gaps with a "
          "concrete fix each. Do not ignore flaws. JSON only: {{\"graphics\": n, \"layout\": n, \"ui\": n, \"motion\": n, "
          "\"gaps\": [\"...\", \"...\", \"...\"]}}")
BLIND = ("Two mobile game screenshots of the same kind of screen ({what}), A (image 1) and B (image 2). As a strict senior art "
         "director, score each 1-10 for overall visual quality (art, rendering, layout, UI polish). JSON only: {{\"a\": n, \"b\": n}}")


def img(path):
    return {"inlineData": {"mimeType": "image/png", "data": base64.b64encode(open(path, "rb").read()).decode("ascii")}}


def call(key, parts):
    body = json.dumps({"contents": [{"parts": parts}], "generationConfig": {"temperature": 0.1, "responseMimeType": "application/json"}}).encode("utf-8")
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent", data=body, method="POST",
                                 headers={"Content-Type": "application/json", "x-goog-api-key": key})
    for _ in range(3):
        try:
            with urllib.request.urlopen(req, timeout=400) as r: d = json.loads(r.read().decode("utf-8"))
            t = "".join(p.get("text", "") for c in d.get("candidates", []) for p in c.get("content", {}).get("parts", []) if not p.get("thought"))
            v = json.loads(t)
            return v[0] if isinstance(v, list) else v
        except Exception as e: err = e
    raise RuntimeError(err)


def ours(name):
    # CALIBRATE=<ref image> puts a Blue Archive screenshot in our slot: what the judge gives BA itself
    if os.environ.get("CALIBRATE"): return os.path.join(REF, os.environ["CALIBRATE"])
    if os.environ.get("MOCK"): return os.environ["MOCK"]   # a target mock (tools/target_mock_gemini.py) in our slot
    f = PAIRS[name][0]
    return os.path.join(OUT, f[1:]) if f.startswith("@") else os.path.join(SHOTS, f)


def judge(key, name):
    mine, refs, what = ours(name), [os.path.join(REF, r) for r in PAIRS[name][1]], PAIRS[name][2]
    # REPEAT=n: the anchored score averaged over n asks (one ask moves by ±1)
    runs = [call(key, [img(r) for r in refs] + [img(mine), {"text": ANCHOR.format(n=len(refs), what=what)}]) for _ in range(int(os.environ.get("REPEAT", "1")))]
    a = dict(runs[0])
    for k in AXES: a[k] = round(sum(float(x.get(k) or 0) for x in runs) / len(runs), 2)
    o, b = [], []
    for r in refs[:1]:
        x = call(key, [img(mine), img(r), {"text": BLIND.format(what=what)}]); o.append(float(x["a"])); b.append(float(x["b"]))
        y = call(key, [img(r), img(mine), {"text": BLIND.format(what=what)}]); o.append(float(y["b"])); b.append(float(y["a"]))
    a["blind_ours"] = sum(o) / len(o); a["blind_ba"] = sum(b) / len(b)
    return name, a


if __name__ == "__main__":
    key = g.read_key()
    names = sys.argv[1:] or list(PAIRS)
    names = [n for n in names if os.path.exists(ours(n))]
    with cf.ThreadPoolExecutor(5) as ex: res = dict(ex.map(lambda n: judge(key, n), names))
    prev = json.load(open(os.path.join(OUT, "ba_crosscheck.json"), encoding="utf-8")) if os.path.exists(os.path.join(OUT, "ba_crosscheck.json")) else {}
    if not os.environ.get("CALIBRATE") and not os.environ.get("MOCK"): prev.update(res)
    json.dump(prev, open(os.path.join(OUT, "ba_crosscheck.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    for n, r in res.items():
        axes = " ".join(f"{k}={r.get(k)}" for k in AXES)
        mean = sum(float(r.get(k) or 0) for k in AXES) / len(AXES)
        print(f"{n:10s} {axes}  mean={mean:.2f}  blind ours {r['blind_ours']:.1f} vs BA {r['blind_ba']:.1f}")
        for gp in r.get("gaps", []): print("     -", gp)
