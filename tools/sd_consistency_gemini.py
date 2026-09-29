# -*- coding: utf-8 -*-
"""
SD consistency audit (the user's round of 2026-09-30): for every character, the illustration, the
2D SD and the 3D SD side by side (tools/out/trip/trip_<n>.png: row 1 illustration, row 2 2D SD,
row 3 3D render, ids under the columns), with Blue Archive's own SD art as the bar (tools/out/ba_ref:
ref07 the GS25 collab chibis, ref11 an SD model front / side / back). Gemini scores each character
0-10 on: 2D SD vs illustration, 3D vs illustration, 2D SD vs 3D, 2D SD quality vs BA, 3D quality vs
BA, and lists the concrete mismatches (hair colour / style / length, eyes, glasses, outfit pieces,
colours, props) — the fix list.

    python tools/sd_consistency_gemini.py            → tools/out/sd_consistency.json
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, glob, json, os, sys, urllib.request, concurrent.futures as cf
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

OUT = os.path.join(g.ROOT, "tools", "out")
MODEL = os.environ.get("QA_MODEL", "gemini-3.1-pro-preview")
ASK = ("Image 1 is a sheet of characters from our game: each COLUMN is one character (id printed at the bottom); row 1 = the "
       "official illustration (the source of truth for design), row 2 = our 2D SD (chibi) illustration, row 3 = our 3D SD model "
       "render. Image 2 and image 3 are Blue Archive's own SD art (2D chibi illustrations and a 3D SD model's front/side/back) — "
       "the quality bar. For EVERY column give scores 0-10: sd_illust (2D SD matches the illustration's design), model_illust (3D "
       "matches the illustration), sd_model (2D SD and 3D look like the same character design), sd_ba (2D SD drawing quality vs "
       "Blue Archive's 2D chibis), model_ba (3D quality vs Blue Archive's 3D SD), and list concrete mismatches as short strings, "
       "each prefixed with where it is wrong: 'SD:' or '3D:' (e.g. '3D: hair should be black, is brown', '3D: missing bun', "
       "'SD: glasses not in illustration'). Be strict and specific. Answer JSON only: "
       '[{"id": "...", "sd_illust": n, "model_illust": n, "sd_model": n, "sd_ba": n, "model_ba": n, "issues": ["..."]}]')


def part(path):
    return {"inlineData": {"mimeType": "image/png", "data": base64.b64encode(open(path, "rb").read()).decode("ascii")}}


def ask(key, sheet):
    parts = [part(sheet), part(os.path.join(OUT, "ba_ref", "ref07.png")), part(os.path.join(OUT, "ba_ref", "ref11.png")), {"text": ASK}]
    body = json.dumps({"contents": [{"parts": parts}], "generationConfig": {"temperature": 0.2, "responseMimeType": "application/json"}}).encode("utf-8")
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent", data=body, method="POST",
                                 headers={"Content-Type": "application/json", "x-goog-api-key": key})
    for _ in range(3):
        try:
            with urllib.request.urlopen(req, timeout=500) as r: d = json.loads(r.read().decode("utf-8"))
            t = "".join(p.get("text", "") for c in d.get("candidates", []) for p in c.get("content", {}).get("parts", []) if not p.get("thought"))
            return json.loads(t)
        except Exception as e: print("  retry", sheet, e)
    return []


if __name__ == "__main__":
    key = g.read_key()
    sheets = sorted(glob.glob(os.path.join(OUT, os.environ.get("TRIP_DIR", "trip"), "trip_*.png")))
    with cf.ThreadPoolExecutor(5) as ex: rows = [r for rs in ex.map(lambda s: ask(key, s), sheets) for r in rs]
    json.dump(rows, open(os.path.join(OUT, "sd_consistency.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    keys = ("sd_illust", "model_illust", "sd_model", "sd_ba", "model_ba")
    for r in rows: print(f'{r["id"]:14s} ' + " ".join(f'{k}={r.get(k)}' for k in keys) + " | " + "; ".join(r.get("issues", []))[:300])
    if rows:
        print("MEAN " + " ".join(f'{k}={sum(float(r.get(k) or 0) for r in rows) / len(rows):.2f}' for k in keys))
