# -*- coding: utf-8 -*-
"""
The battle street, painted (the Blue Archive cross-check, 2026-09-30: "the 3D environment is extremely
flat — no textures, no shadows, no detail"). The plate camera's render of the empty low-poly set
(StreetPlate.Render → tools/out/plates/street_<mood>.png) goes to Gemini to be repainted in place as a
detailed, high-quality anime mobile-game environment — the SAME geometry, perspective and layout, every
edge where it was, so the painting can be projected straight back onto the set (Plate.shader).

    python tools/street_plate_gemini.py [moods...]   → Assets/ExcelHeroes/Resources/Art/Battle/plate_<mood>.png
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, json, os, sys, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
from PIL import Image

IN = os.path.join(g.ROOT, "tools", "out", "plates")
OUT = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Battle")
LIGHT = {0: "bright clear late-morning daylight, soft warm sun from the upper left, crisp soft shadows",
         1: "warm golden evening light, long soft shadows, a peach sky glow on the facades, a few windows lit",
         2: "night: cool blue ambient light, warm glowing shop windows and street lamps, lit signs, gentle reflections on the asphalt"}
ASK = ("This is a flat untextured 3D render of a city street set, seen from a high three-quarter game camera. Repaint it IN PLACE as a "
       "finished background for a top-tier anime mobile game's battle scene (clean, bright, highly detailed, cel-shaded anime "
       "illustration style, like a polished Japanese gacha game's 3D city). KEEP EVERYTHING EXACTLY WHERE IT IS: the same camera, "
       "perspective and horizon, every building edge, window, awning, the curb line, the sidewalk, the crosswalk stripes, the lane "
       "markings, the manhole, the lamp posts, the trees and pots, the bus stop — same positions, sizes and silhouettes, nothing "
       "moved, added in the road or removed. ADD DETAIL AND MATERIAL: fine asphalt texture with subtle wear and patches, crisp "
       "painted lane lines, tiled sidewalk with joints, curb stones, detailed shop fronts (signs with simple generic shapes and no "
       "readable text or logos, display windows with reflections, window frames, sills, balconies, air conditioners), leafy tree "
       "canopies, planters with flowers, soft ambient occlusion in corners, contact shadows under objects. Make it VIVID and DENSE like a "
       "lively Tokyo shopping street in a hit anime game: saturated clean colours (not pastel-washed), colourful shop signs and banners "
       "(abstract shapes, no readable letters), striped awnings, neon accents, posters in windows, vending machines, bicycles parked on "
       "the sidewalk, flower boxes, road signs, painted yellow curb markings and bright white crossings. Lighting: {light}. "
       "No people, no vehicles, no characters, no text, no watermark. Output the same framing, 16:9.")


def run(key, mood):
    src = os.path.join(IN, f"street_{mood}.png")
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": base64.b64encode(open(src, "rb").read()).decode("ascii")}},
                                               {"text": ASK.format(light=LIGHT[mood])}]}],
                       "generationConfig": {"responseModalities": ["IMAGE"], "temperature": 0.4, "imageConfig": {"aspectRatio": "16:9"}}}).encode("utf-8")
    for model in g.MODELS:
        req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent", data=body, method="POST",
                                     headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=300) as r: d = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e: print(f"  mood {mood} {model}: HTTP {e.code}"); continue
        for c in d.get("candidates", []):
            for p in c.get("content", {}).get("parts", []):
                inline = p.get("inlineData") or p.get("inline_data")
                if inline and inline.get("data"):
                    raw = os.path.join(IN, f"painted_{mood}.png"); open(raw, "wb").write(base64.b64decode(inline["data"]))
                    im = Image.open(raw).convert("RGB").resize((2048, 1152), Image.LANCZOS)
                    im.save(os.path.join(OUT, f"plate_{mood}.png")); print(f"  ok mood {mood} ({model}) {Image.open(raw).size}"); return True
    return False


if __name__ == "__main__":
    key = g.read_key()
    for m in [int(a) for a in sys.argv[1:]] or [0, 1, 2]: run(key, m)
