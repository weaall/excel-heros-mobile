# -*- coding: utf-8 -*-
"""Gemini (vision) on a filmstrip of our fight (tools/out/action_strip.png): what the action lacks
against Blue Archive's battles — attacks, impacts, hit reactions, deaths — and concrete fixes.
    python tools/action_critique.py [strip.png] -> tools/out/action_critique.md
SECURITY: the key is read from the env file by NAME; never printed or written."""
import base64, json, os, sys, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
MODEL = os.environ.get("CRITIQUE_MODEL", "gemini-3.8-flash")
ASK = ("This is a filmstrip (12 consecutive frames, left to right, top to bottom) of our landscape mobile gacha battle: "
       "chibi office workers (quarter view) fight office-object monsters with Excel-themed attacks. As a senior action / VFX "
       "designer who knows Blue Archive's battles, list the FIVE biggest gaps in the ACTION FEEL versus Blue Archive — attack "
       "motion, anticipation, impact frames, hit reactions, knockback, deaths, camera, effects, readability — most important "
       "first, each with a concrete fix a Unity developer can implement (timings in seconds, sizes relative to the character, "
       "colours). Answer in Korean, compact: '1. 문제 → 수정'.")
if __name__ == "__main__":
    path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(g.ROOT, "tools", "out", "action_strip.png")
    img = base64.b64encode(open(path, "rb").read()).decode("ascii")
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": img}}, {"text": ASK}]}]}).encode("utf-8")
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent", data=body, method="POST",
                                 headers={"Content-Type": "application/json", "x-goog-api-key": g.read_key()})
    with urllib.request.urlopen(req, timeout=300) as r: data = json.loads(r.read().decode("utf-8"))
    text = data["candidates"][0]["content"]["parts"][0]["text"]
    out = os.path.join(g.ROOT, "tools", "out", "action_critique.md")
    open(out, "w", encoding="utf-8").write(text); print(out)
