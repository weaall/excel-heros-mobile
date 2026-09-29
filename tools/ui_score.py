# -*- coding: utf-8 -*-
"""
The publish-readiness gate: every capture goes to Gemini's vision model as a senior UI director who
ships Blue Archive / GODDESS OF VICTORY: NIKKE, and comes back as JSON — a score out of 10 for
"would this screen pass a publisher's UI review for a major gacha title", whether it passes (>= 8),
and the concrete blockers. Scores are collected in tools/out/ui_score.json and a table is printed,
so each round of fixes can be measured against the last.

    python tools/ui_score.py                 # every screen in tools/out/shots worth scoring
    python tools/ui_score.py 05-Home 08-Party

SECURITY: the key is read from the env file by NAME and sent in a header; never printed or written.
"""
import base64, json, os, sys, urllib.request, concurrent.futures as cf
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

MODEL = os.environ.get("CRITIQUE_MODEL", "gemini-3.8-flash")
SHOTS = os.path.join(g.ROOT, "tools", "out", "shots")
OUT = os.path.join(g.ROOT, "tools", "out", "ui_score.json")
SCREENS = ["05-Home", "07-Roster", "17-Detail", "18-Enhance", "20-Skins", "08-Party", "09-Gacha", "22-Pull10",
           "10-Quests", "11-Progress", "12-Story", "13-Album", "14-Codex", "15-Chart", "16-Shop", "23-Mail",
           "24-Notice", "07-BattleHud", "07-Fight1", "19-Scout", "21-Promotion"]
ASK = ("This is one screen ({name}) of our landscape mobile gacha game (2400x1080 capture). You are the UI director who "
       "shipped Blue Archive and GODDESS OF VICTORY: NIKKE, doing the publisher's final UI review. Judge ONLY the UI "
       "(layout, hierarchy, spacing, typography, components, consistency, functional clarity, polish) — not the character "
       "illustrations or 3D models. Intentional motifs, NOT defects: the small glowing 1x4 spreadsheet 'sheet halo' behind or "
       "under each character (our version of BA halos); the lobby's single long slanted glass top bar (taken from BA's own "
       "lobby); the office/Excel theme. Calibrate the scale: 3 = prototype / programmer art; 5 = polished indie; 6 = mid-tier "
       "commercial gacha; 7 = strong commercial release; 8 = ships as-is beside Blue Archive / NIKKE; 9-10 = their best "
       "screens. Score each category 1-10, then an overall score. Return JSON only: {{\"categories\": {{\"layout\": n, "
       "\"hierarchy\": n, \"typography\": n, \"components\": n, \"consistency\": n, \"clarity\": n, \"polish\": n}}, "
       "\"score\": number, \"pass\": boolean (score>=8), \"blockers\": [up to 3 short Korean strings, most important first, "
       "each a concrete fix naming the element and the size/colour/placement], \"strengths\": [1-2 short Korean strings]}}")


def ask(key, path, name):
    with open(path, "rb") as f: img = base64.b64encode(f.read()).decode("ascii")
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": img}}, {"text": ASK.format(name=name)}]}],
                       "generationConfig": {"temperature": 0.1, "responseMimeType": "application/json"}}).encode("utf-8")
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent",
                                 data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
    with urllib.request.urlopen(req, timeout=180) as r: d = json.loads(r.read().decode("utf-8"))
    text = "".join(p.get("text", "") for c in d.get("candidates", []) for p in c.get("content", {}).get("parts", []))
    return json.loads(text)


if __name__ == "__main__":
    names = sys.argv[1:] or [n for n in SCREENS if os.path.exists(os.path.join(SHOTS, n + ".png"))]
    key = g.read_key()
    prev = json.load(open(OUT, encoding="utf-8")) if os.path.exists(OUT) else {}
    res = dict(prev)

    def one(n):
        for _ in range(2):
            try: return n, ask(key, os.path.join(SHOTS, n + ".png"), n)
            except Exception as e: err = str(e)[:80]
        return n, {"score": 0, "pass": False, "blockers": [f"(error {err})"], "strengths": []}

    with cf.ThreadPoolExecutor(6) as ex:
        for n, r in ex.map(one, names): res[n] = r
    json.dump(res, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    sys.stdout.reconfigure(encoding="utf-8")
    for n in names:
        r = res[n]; was = prev.get(n, {}).get("score")
        print(f"{n:14s} {r.get('score'):>4}  {'PASS' if r.get('score', 0) >= 8 else '    '}  {'(was ' + str(was) + ')' if was is not None else ''}")
        for b in r.get("blockers", [])[:3]: print(f"      - {b}")
    sc = [res[n].get("score", 0) for n in names]
    print(f"mean {sum(sc) / max(1, len(sc)):.2f}   pass {sum(1 for s in sc if s >= 8)}/{len(sc)}")
