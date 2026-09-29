# -*- coding: utf-8 -*-
"""
A target to build toward: our screen redrawn by Gemini at Blue Archive's finish — the SAME layout,
elements, characters, text and positions, only the rendering, materials, UI finish and effects raised
— so the gap between what we have and what would pass the cross-check (tools/ba_crosscheck.py) is a
picture, not a list. The mock is then scored by the same judge (MOCK=<file> ba_crosscheck.py).

    python tools/target_mock_gemini.py 07-Fight2 [05-Home ...]   → tools/out/mock/<shot>.png
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, json, os, sys, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
from PIL import Image

SHOTS = os.environ.get("SHOTS") or os.path.join(g.ROOT, "tools", "out", "shots2")
OUT = os.path.join(g.ROOT, "tools", "out", "mock")
ASK = ("This is a screenshot of our landscape mobile gacha game. Redraw it as the SAME screen at the polish level of Blue Archive: "
       "keep the exact layout, every UI element in its place with the same text and numbers, the same characters in the same "
       "places and poses, the same street. Raise only the finish: crisp high-resolution cel-shaded 3D characters with clean "
       "outlines and bright faces, a detailed lit environment, a refined UI (layered panels, subtle gradients, drop shadows, sharp "
       "icons, polished typography), and lively effects. 16:9, no watermark.")


def run(key, shot):
    src = os.path.join(SHOTS, shot + ".png")
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": base64.b64encode(open(src, "rb").read()).decode("ascii")}}, {"text": ASK}]}],
                       "generationConfig": {"responseModalities": ["IMAGE"], "temperature": 0.4, "imageConfig": {"aspectRatio": "16:9"}}}).encode("utf-8")
    for model in g.MODELS:
        req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent", data=body, method="POST",
                                     headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=300) as r: d = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e: print(f"  {shot} {model}: HTTP {e.code}"); continue
        for c in d.get("candidates", []):
            for p in c.get("content", {}).get("parts", []):
                inline = p.get("inlineData") or p.get("inline_data")
                if inline and inline.get("data"):
                    open(os.path.join(OUT, shot + ".png"), "wb").write(base64.b64decode(inline["data"])); print("  ok", shot, model); return
    print("  FAILED", shot)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True); key = g.read_key()
    for s in sys.argv[1:]: run(key, s)
