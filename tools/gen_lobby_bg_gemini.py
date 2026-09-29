# -*- coding: utf-8 -*-
"""
The lobby room, redrawn (the Blue Archive cross-check, 2026-09-30: "the background lacks depth and
lighting; the benchmark's lobby rooms are dense, crisp and lit"): the current room
(Resources/Art/Backdrop/lobby.png — a sofa, a window, a whiteboard, a shelf, and a large blank wall)
goes in as the layout, and comes back as a finished, detailed, crisp office lounge — the window on
the left, the shelf on the right, the middle kept clearer for the character who stands in it — with
real depth: a foreground table edge, the room receding, sunlight across the floor.

    python tools/gen_lobby_bg_gemini.py      → Resources/Art/Backdrop/lobby.png (the old one kept as tools/out/lobby_prev.png)
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, io, json, os, shutil, sys, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
from PIL import Image

DST = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Backdrop", "lobby.png")
ASK = ("This is the background of our mobile game's main lobby: an office lounge where the player's character stands in the middle. "
       "Redraw it as a finished, premium background in the style of a top Japanese gacha game's lobby (a bright, detailed, crisp "
       "anime background painting, clean lines, rich but soft colours). Keep the same room layout and camera: the big window with the "
       "city on the left, the sofa and low table on the left, the whiteboard and the tall shelf on the right. Make it DENSE and "
       "lived-in and give it DEPTH: the room recedes to a back wall with a door and a glass partition to an open-plan office with "
       "desks and monitors, a hanging pendant lamp, a potted plant corner, framed charts and a wall clock, a coffee station, papers "
       "and a laptop on the table, a rug, a foreground plant cut by the frame's lower corner. Warm late-morning sunlight streams "
       "through the window in soft beams across the floor and wall, gentle bloom, crisp shadows. The MIDDLE of the picture stays "
       "calmer (floor and far wall) where the character stands. Sharp focus everywhere, no blur. No people, no characters, no text, "
       "no logos, no watermark. Same ultra-wide framing.")


def main():
    key = g.read_key()
    im = Image.open(DST).convert("RGB"); buf = io.BytesIO(); im.save(buf, "PNG")
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": base64.b64encode(buf.getvalue()).decode("ascii")}}, {"text": ASK}]}],
                       "generationConfig": {"responseModalities": ["IMAGE"], "temperature": 0.5, "imageConfig": {"aspectRatio": "21:9"}}}).encode("utf-8")
    for model in g.MODELS:
        req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent", data=body, method="POST",
                                     headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=300) as r: d = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e: print(f"  {model}: HTTP {e.code}"); continue
        for c in d.get("candidates", []):
            for p in c.get("content", {}).get("parts", []):
                inline = p.get("inlineData") or p.get("inline_data")
                if inline and inline.get("data"):
                    prev = os.path.join(g.ROOT, "tools", "out", "lobby_prev.png")
                    if not os.path.exists(prev): shutil.copy(DST, prev)
                    out = Image.open(io.BytesIO(base64.b64decode(inline["data"]))).convert("RGB")
                    out = out.resize((2048, round(2048 * out.height / out.width)), Image.LANCZOS)
                    out.save(DST); print("  ok", model, out.size); return
    print("  FAILED")


if __name__ == "__main__":
    main()
