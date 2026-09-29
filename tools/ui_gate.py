# -*- coding: utf-8 -*-
"""
The launch gate, the way a publisher's UI review actually reads: not a score but a defect list,
each issue graded BLOCKER (must fix before launch — broken, unreadable, clipped, misleading,
prototype-looking), MAJOR (clearly below Blue Archive / NIKKE and noticed by players) or MINOR
(polish). A screen passes at zero blockers and at most one major. tools/ui_score.py's 1-10 scale
compressed everything into 5-6 (Gemini's own "shipped" mock-ups scored the same), so it could not
tell a finished screen from an unfinished one; a defect list can.

    python tools/ui_gate.py                 # every screen
    python tools/ui_gate.py 05-Home 08-Party
    GATE_MODEL=gemini-3.1-pro-preview python tools/ui_gate.py

Results in tools/out/ui_gate.json. SECURITY: the key is read from the env file by NAME only.
"""
import base64, json, os, sys, urllib.request, concurrent.futures as cf
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
from ui_score import SCREENS, SHOTS

MODEL = os.environ.get("GATE_MODEL", "gemini-3.1-pro-preview")
OUT = os.path.join(g.ROOT, "tools", "out", "ui_gate.json")
ASK = ("This is one screen ({name}) of our landscape mobile gacha game, captured at 2400x1080. You are the publisher's UI "
       "lead doing the final pre-launch review, with Blue Archive and GODDESS OF VICTORY: NIKKE as the bar. Review ONLY the UI "
       "(layout, spacing, alignment, hierarchy, typography, components, states, consistency, clarity) — not the character "
       "illustrations or 3D models. Intentional motifs, not defects: the small glowing 1x4 spreadsheet 'sheet halo' behind or "
       "under characters (our BA-halo equivalent), the lobby's single long slanted top bar (from BA's own lobby), the "
       "office/Excel theme. List every issue you would file, each graded: BLOCKER = must fix before launch (broken, clipped "
       "or overlapping text, unreadable, misleading state, obviously prototype/placeholder look); MAJOR = clearly below the "
       "BA/NIKKE bar and players would notice; MINOR = polish. Do not invent issues to fill the list; an excellent screen may "
       "have none. Return JSON only: {{\"issues\": [{{\"severity\": \"BLOCKER|MAJOR|MINOR\", \"element\": \"...\", "
       "\"problem\": \"short Korean\", \"fix\": \"short Korean, concrete size/colour/placement\"}}], \"verdict\": \"LAUNCH|NOT YET\"}}")


def ask(key, path, name):
    with open(path, "rb") as f: img = base64.b64encode(f.read()).decode("ascii")
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": img}}, {"text": ASK.format(name=name)}]}],
                       "generationConfig": {"temperature": 0.1, "responseMimeType": "application/json"}}).encode("utf-8")
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent",
                                 data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
    with urllib.request.urlopen(req, timeout=300) as r: d = json.loads(r.read().decode("utf-8"))
    text = "".join(p.get("text", "") for c in d.get("candidates", []) for p in c.get("content", {}).get("parts", []) if not p.get("thought"))
    return json.loads(text)


def passes(r):
    sev = [i.get("severity", "").upper() for i in r.get("issues", [])]
    return sev.count("BLOCKER") == 0 and sev.count("MAJOR") <= 1


if __name__ == "__main__":
    names = sys.argv[1:] or [n for n in SCREENS if os.path.exists(os.path.join(SHOTS, n + ".png"))]
    key = g.read_key()
    prev = json.load(open(OUT, encoding="utf-8")) if os.path.exists(OUT) else {}
    res = dict(prev)

    def one(n):
        err = ""
        for _ in range(2):
            try: return n, ask(key, os.path.join(SHOTS, n + ".png"), n)
            except Exception as e: err = str(e)[:100]
        return n, {"issues": [{"severity": "BLOCKER", "element": "-", "problem": f"(error {err})", "fix": ""}], "verdict": "NOT YET"}

    with cf.ThreadPoolExecutor(5) as ex:
        for n, r in ex.map(one, names): res[n] = r
    json.dump(res, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    sys.stdout.reconfigure(encoding="utf-8")
    ok = 0
    for n in names:
        r = res[n]; sev = [i.get("severity", "").upper() for i in r.get("issues", [])]
        p = passes(r); ok += p
        print(f"{n:14s} {'PASS' if p else 'FAIL'}  B{sev.count('BLOCKER')} M{sev.count('MAJOR')} m{sev.count('MINOR')}  {r.get('verdict')}")
        for i in r.get("issues", []):
            if i.get("severity", "").upper() in ("BLOCKER", "MAJOR"):
                print(f"      [{i.get('severity')}] {i.get('element')}: {i.get('problem')} → {i.get('fix')}")
    print(f"pass {ok}/{len(names)}")
