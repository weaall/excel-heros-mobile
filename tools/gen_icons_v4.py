# -*- coding: utf-8 -*-
"""
Icons v5 (ICON_V=5) — one shared palette; v4 was full colour (the user, 2026-09-30: "the icons are black-and-white, that's ugly"). v2 was flat
navy, v3 pastel sky-blue only; with the nav's navy tint on top the bar read as black silhouettes. Now each
menu icon is a small glossy illustration with its OWN main colour, so the lobby's menus tell apart at a
glance, in the bright anime-gacha UI finish: a clean dark outline, two-tone cel shading, a white gloss.
Sheet 0 only (the lobby / nav icons); sliced by gen_icons_v2 onto Resources/Art/Icons/<name>.png.
The previous set is kept in tools/out/icons_v3_backup/.

    python tools/gen_icons_v4.py            # generate + slice
    python tools/gen_icons_v4.py --slice
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, json, os, shutil, sys, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import gen_icons_v2 as v2

ITEMS = [("lobby", "a small house, sky blue walls and a red roof"), ("roster", "an employee ID card on a lanyard, blue card, orange strap, tiny portrait"),
         ("party", "three cheerful people busts in teal, orange and pink"), ("tasks", "a yellow clipboard with a white checklist and green check marks"),
         ("review", "a purple magnifying glass over a white paper with a gold star"), ("messenger", "a pink smartphone with a white chat bubble with three dots"),
         ("album", "an orange photo album with a polaroid of a green mountain and blue sky"), ("codex", "a thick red book with a gold bookmark ribbon"),
         ("chart", "rising bars in green, blue and orange with a red arrow"), ("recruit", "a white envelope with a glowing gold star seal and sparkles"),
         ("notice", "an orange megaphone with a white bell end"), ("shop", "a mint green shopping bag with a pink ribbon"),
         ("mail", "a light blue envelope with a red heart seal"), ("settings", "a silver gear with a blue centre"),
         ("back", "a white left arrow in a blue circle"), ("search", "a blue magnifying glass")]
STYLE = ("Draw a set of mobile game menu icons in the polished style of a bright Japanese anime gacha game UI. Each icon is a small "
         "cute glossy illustration in FULL COLOUR — each one with its own vivid main colour as described, never monochrome, never all blue — "
         "a clean dark navy outline of even weight, two-tone cel shading, a white gloss highlight on the upper left, simple readable shapes "
         "that stay clear at 64 px, slight 3/4 view, consistent size. Exactly 16 separate icons in a 4 by 4 grid, evenly spaced, each centred "
         "in its own square cell with a wide empty margin, on a flat pure white background (#FFFFFF). No text, no letters, no labels, no grid "
         "lines, no frames, no tiles behind the icons. Left to right, top to bottom: ")
v2.sheet_path = lambda k: os.path.join(g.ROOT, "tools", "out", f"icon_v4_sheet{k}.png")


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
    bak = os.path.join(g.ROOT, "tools", "out", "icons_v3_backup"); os.makedirs(bak, exist_ok=True)
    for name, _ in ITEMS:
        src = os.path.join(v2.OUT, name + ".png")
        if os.path.exists(src) and not os.path.exists(os.path.join(bak, name + ".png")): shutil.copy(src, bak)
    if "--slice" not in sys.argv and not generate(g.read_key()): sys.exit("FAILED")
    v2.slice_sheet(0, ITEMS)
