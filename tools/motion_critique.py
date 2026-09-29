# -*- coding: utf-8 -*-
"""
Motion research: Gemini as an animation director who knows Blue Archive's SD combat animation
critiques our pose-library strips (SdBasePreview.Strip) row by row and names concrete changes —
joint angles, timing, spacing, secondary motion — to reach that feel. Reference is knowledge of
the style only; nothing of NEXON's is copied.

    python tools/motion_critique.py tools/out/crit/strip_cso.png "idle,ready,attack,hit,run,skill,victory"
      → tools/out/motion_critique.json
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, json, os, sys, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

ASK = ("You are a senior game animator who knows Blue Archive's 3D SD (chibi, ~2.4 heads) battle animation very well. This "
       "filmstrip shows OUR chibi's motions, one row per motion ({rows}), frames left to right evenly in time. Compare each row "
       "with how a Blue Archive SD would perform it: pose clarity/silhouette, anticipation, snap, follow-through and settle, "
       "weight, spacing, exaggeration, secondary motion of hair/skirt, and how appealing and readable it is at small size. "
       "Score each row 1-10 for 'Blue Archive feel'. Then give CONCRETE fixes in terms a programmer can apply to joint "
       "angles and timing (e.g. 'lean the trunk 18° into the run', 'hold the contact pose 3 frames', 'elbows at 90°'). "
       "Answer JSON only: [{{\"row\": \"name\", \"score\": n, \"problems\": [..], \"fixes\": [..]}}]")

if __name__ == "__main__":
    key = g.read_key(); path, rows = sys.argv[1], sys.argv[2]
    data = base64.b64encode(open(path, "rb").read()).decode("ascii")
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": data}}, {"text": ASK.format(rows=rows)}]}],
                       "generationConfig": {"temperature": 0.3, "responseMimeType": "application/json"}}).encode("utf-8")
    req = urllib.request.Request("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-pro-preview:generateContent", data=body, method="POST",
                                 headers={"Content-Type": "application/json", "x-goog-api-key": key})
    with urllib.request.urlopen(req, timeout=300) as r: d = json.loads(r.read().decode("utf-8"))
    t = "".join(p.get("text", "") for c in d.get("candidates", []) for p in c.get("content", {}).get("parts", []) if not p.get("thought"))
    res = json.loads(t)
    json.dump(res, open(os.path.join(g.ROOT, "tools", "out", "motion_critique.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    for r in res: print(r["row"], r["score"], "|", " / ".join(r["fixes"])[:600])
