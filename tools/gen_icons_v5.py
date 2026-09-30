# -*- coding: utf-8 -*-
"""
Icons v5 — one family (the user, 2026-10-01: "too many colours, artificial — simpler, the icons should share
similar colours"). v4 gave every icon its own loud colour; v3 was sky blue only and read monochrome under the
nav's tint. Now ONE shared palette for all 16: sky blue (#5BB8F2 .. #2F8FE0) and white bodies, one even dark
navy line (#1F2D4D), a soft top-to-bottom gradient and a small white gloss, and at most ONE small accent per
icon from the SAME two: warm yellow (#FFC845) or soft coral pink (#FF7E9B). Simple rounded shapes, few parts.
Sliced by gen_icons_v2 onto Resources/Art/Icons/<name>.png (v4 kept in tools/out/icons_v4_backup/).

    python tools/gen_icons_v5.py [--slice]
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, json, os, shutil, sys, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import gen_icons_v2 as v2

ITEMS = [("lobby", "a small house, yellow door"), ("roster", "an ID card on a lanyard"), ("party", "three people busts, the middle one in front"),
         ("tasks", "a clipboard with two check marks, a yellow clip"), ("review", "a magnifying glass with a small yellow star"),
         ("messenger", "a chat bubble with three dots, a small pink heart"), ("album", "a photo frame with a mountain and a yellow sun"),
         ("codex", "a closed book with a pink bookmark"), ("chart", "three rising bars with a yellow arrow"),
         ("recruit", "an envelope with a yellow star seal"), ("notice", "a megaphone"), ("shop", "a shopping bag with a pink ribbon"),
         ("mail", "a closed envelope with a pink heart seal"), ("settings", "a gear"), ("back", "a left arrow"), ("search", "a magnifying glass")]
STYLE = ("Draw a set of 16 mobile game menu icons as ONE consistent family, in a clean polished Japanese gacha game UI style. STRICT "
         "PALETTE shared by every icon: bodies in sky blue (#5BB8F2 to #2F8FE0) and white only, with a soft top-to-bottom gradient and "
         "one small white gloss; one even dark navy (#1F2D4D) outline of the same weight on all; at most ONE small accent per icon, "
         "and only warm yellow (#FFC845) or soft coral pink (#FF7E9B). No other colours at all. Simple rounded chunky shapes with few "
         "parts, readable at 48 px, front or slight 3/4 view, all the same size. Exactly 16 icons in a 4 by 4 grid, evenly spaced, each "
         "centred in its own cell with a wide margin, on a flat pure white background (#FFFFFF). No text, no letters, no grid lines, no "
         "frames, no tiles behind the icons. Left to right, top to bottom: ")
v2.sheet_path = lambda k: os.path.join(g.ROOT, "tools", "out", f"icon_v5_sheet{k}.png")


def generate(key):
    text = STYLE + "; ".join(f"{i + 1}. {d}" for i, (_, d) in enumerate(ITEMS)) + "."
    body = json.dumps({"contents": [{"parts": [{"text": text}]}],
                       "generationConfig": {"responseModalities": ["IMAGE"], "imageConfig": {"aspectRatio": "1:1", "imageSize": "2K"}}}).encode("utf-8")
    for model in g.MODELS:
        req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent", data=body, method="POST",
                                     headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=400) as r: data = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e: print(f"  {model}: HTTP {e.code}"); continue
        for c in data.get("candidates", []):
            for p in c.get("content", {}).get("parts", []):
                inline = p.get("inlineData") or p.get("inline_data")
                if inline and inline.get("data"):
                    open(v2.sheet_path(0), "wb").write(base64.b64decode(inline["data"])); print(f"  sheet: {model}"); return True
    return False


if __name__ == "__main__":
    bak = os.path.join(g.ROOT, "tools", "out", "icons_v4_backup"); os.makedirs(bak, exist_ok=True)
    for name, _ in ITEMS:
        src = os.path.join(v2.OUT, name + ".png")
        if os.path.exists(src) and not os.path.exists(os.path.join(bak, name + ".png")): shutil.copy(src, bak)
    if "--slice" not in sys.argv and not generate(g.read_key()): sys.exit("FAILED")
    v2.slice_sheet(0, ITEMS)
