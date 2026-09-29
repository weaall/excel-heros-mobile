# -*- coding: utf-8 -*-
"""
One round of "compare with Blue Archive": each capture goes to Gemini's vision model with the
same question — as a Blue Archive UI designer, what are the three biggest gaps on THIS screen and
exactly how to fix each (sizes, colours, placement) — and the answers are collected into
tools/out/ui_critique.md, one section per screen. Text only; nothing is generated.

    python tools/ui_critique.py                         # the main screens of tools/out/shots
    python tools/ui_critique.py 05-Home 16-Detail       # these

The loop: buildshots → ui_critique → fix the top items → buildshots again.
SECURITY: the key is read from the env file by NAME and sent in a header; never printed or written.
"""
import base64, json, os, sys, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

MODEL = os.environ.get("CRITIQUE_MODEL", "gemini-3.8-flash")
SHOTS = os.environ.get("SHOTS") or os.path.join(g.ROOT, "tools", "out", "shots")
DEFAULT = ["05-Home", "07-Roster", "16-Detail", "08-Party", "09-Gacha", "10-Quests", "11-Progress", "12-Story", "13-Album", "07-BattleHud", "17-Enhance", "21-Pull10"]
ASK = ("This is one screen ({name}) of our landscape mobile gacha game, which is meant to reach Blue Archive's UI quality. "
       "As a senior UI designer who knows Blue Archive's screens in detail, list the THREE biggest remaining gaps on this "
       "screen versus Blue Archive, most important first. For each: what is wrong, and a concrete fix a developer can apply "
       "(element, size in px at a 2400x1080 canvas, colour, placement, font weight). Ignore the character art itself. "
       "Answer in Korean, compact: '1. 문제 → 수정'.")


def ask(key, path, name):
    with open(path, "rb") as f: img = base64.b64encode(f.read()).decode("ascii")
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": img}}, {"text": ASK.format(name=name)}]}],
                       "generationConfig": {"temperature": 0.2}}).encode("utf-8")
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent",
                                 data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
    with urllib.request.urlopen(req, timeout=180) as r: d = json.loads(r.read().decode("utf-8"))
    return "".join(p.get("text", "") for c in d.get("candidates", []) for p in c.get("content", {}).get("parts", []))


if __name__ == "__main__":
    names = sys.argv[1:] or DEFAULT
    key = g.read_key(); out = []
    for n in names:
        p = os.path.join(SHOTS, n + ".png")
        if not os.path.exists(p): continue
        try: out.append(f"## {n}\n{ask(key, p, n).strip()}\n")
        except Exception as e: out.append(f"## {n}\n(error {str(e)[:80]})\n")
        print(f"  {n}")
    path = os.path.join(g.ROOT, "tools", "out", "ui_critique.md")
    with open(path, "w", encoding="utf-8") as f: f.write("\n".join(out))
    print(path)
