# -*- coding: utf-8 -*-
"""
Icons v3 — the user's Blue Archive reference (tools/out/ba_icon_ref.png: MISSION / RECRUIT / STUDENT /
CAFE / CLUB tiles): soft pastel icon illustrations — mostly light sky blue and white, a thin dark
navy line, a gentle top-to-bottom gradient and a white gloss, now and then ONE warm accent (pink,
yellow) — calm and readable, never many colours. The reference row goes in as the style image; 16
per 4x4 sheet, sliced by tools/gen_icons_v2 onto Resources/Art/Icons/<name>.png (same names).

    python tools/gen_icons_v3.py            # both sheets
    python tools/gen_icons_v3.py --slice
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import gen_icons_v2 as v2

v2.REF = os.path.join(g.ROOT, "tools", "out", "ba_icon_ref.png")
v2.STYLE = ("Image 1 shows five game menu icons (a clipboard, a winged shield with a star, a book with a person, a coffee cup, "
            "a pink star badge). Draw NEW icons in EXACTLY that style: soft pastel illustrations, mostly light sky blue (#8FD3F4 .. #3FA9E8) "
            "and white, a thin clean dark navy (#23324F) outline, a gentle vertical gradient, a small white gloss highlight, "
            "at most ONE small warm accent colour (soft pink or soft yellow) and only where it helps; simple readable shapes, "
            "front or slight 3/4 view, no text. Draw ONLY the icons, NOT the glass tiles behind them. "
            "Exactly 16 separate icons in a 4 by 4 grid, evenly spaced, each centred in its own square cell with a wide empty "
            "margin, on a flat pure white background (#FFFFFF). No labels, no grid lines, no frames. Left to right, top to bottom: ")
v2.sheet_path = lambda k: os.path.join(g.ROOT, "tools", "out", f"icon_v3_sheet{k}.png")

_orig = v2.generate
def generate(key, k, items):
    # the reference row is short and wide: pass it whole (v2 crops a third off a kit sheet)
    import base64, io, json, urllib.request, urllib.error
    from PIL import Image
    ref = Image.open(v2.REF).convert("RGB"); buf = io.BytesIO(); ref.save(buf, "PNG")
    text = v2.STYLE + "; ".join(f"{i + 1}. {d}" for i, (_, d) in enumerate(items)) + "."
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": base64.b64encode(buf.getvalue()).decode("ascii")}}, {"text": text}]}],
                       "generationConfig": {"responseModalities": ["IMAGE"], "imageConfig": {"aspectRatio": "1:1"}}}).encode("utf-8")
    for model in g.MODELS:
        req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent",
                                     data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=300) as r: data = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            print(f"  {model}: HTTP {e.code}"); continue
        for c in data.get("candidates", []):
            for p in c.get("content", {}).get("parts", []):
                inline = p.get("inlineData") or p.get("inline_data")
                if inline and inline.get("data"):
                    open(v2.sheet_path(k), "wb").write(base64.b64decode(inline["data"])); print(f"  sheet {k}: {model}"); return True
    return False

if __name__ == "__main__":
    key = None if "--slice" in sys.argv else g.read_key()
    for k, items in enumerate(v2.SHEETS):
        if key and not generate(key, k, items): print("  FAILED sheet", k); continue
        v2.slice_sheet(k, items)
