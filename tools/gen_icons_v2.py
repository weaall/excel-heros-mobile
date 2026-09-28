# -*- coding: utf-8 -*-
"""
Icons v2 — one simple system for every menu / system icon (the user: the illustrated icons were too
busy and too many colours to tell apart). The Gemini UI kit (tools/out/design/kit_0.png) set it:
flat TWO-TONE only, dark navy #1E2B45 shapes with a single cyan #2AB8F0 accent, thick rounded
forms, no gradients, no small detail, readable at 48 px. The kit's icon block goes in as the style
reference; 16 icons per 4x4 sheet, sliced and cut out onto Resources/Art/Icons/<name>.png.
Item icons (gold, gem, the eq_* equipment) stay illustrated: they are things you get, not buttons.

    python tools/gen_icons_v2.py            # both sheets
    python tools/gen_icons_v2.py --slice    # re-slice the saved sheets
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, io, json, os, sys, urllib.request, urllib.error
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import gen_icons_gemini as ic

OUT = ic.OUT_DIR
REF = os.path.join(g.ROOT, "tools", "out", "design", "kit_0.png")
SHEETS = [
    [("lobby", "a house"), ("roster", "a person bust wearing a suit and tie"), ("party", "three people, the middle one in front"),
     ("tasks", "a clipboard with two check marks"), ("review", "a magnifying glass with a small star inside"), ("messenger", "a chat bubble with three dots"),
     ("album", "two stacked photo frames with a mountain picture"), ("codex", "a closed book with a bookmark"), ("chart", "three rising bars with a small arrow"),
     ("recruit", "an envelope with a star seal"), ("notice", "a megaphone"), ("shop", "a shopping bag"),
     ("mail", "a closed envelope"), ("settings", "a gear"), ("back", "a left arrow"), ("search", "a magnifying glass")],
    [("add", "a plus sign in a rounded square"), ("undo", "a curved arrow turning back"), ("overflow", "three dots in a row"),
     ("sheets", "a spreadsheet grid, 3 by 3 cells, the top row cyan"), ("card", "a portrait card with a star"), ("levelup", "an upward double chevron"),
     ("awaken", "a star with rays"), ("convert", "two arrows forming a circle"), ("bosskey", "a key"), ("bosskey_on", "a key with a cyan glow ring"),
     ("battle", "two crossed swords"), ("close", "an X in a circle"), ("home", "a house with a door"), ("filter", "a funnel"),
     ("sort", "up and down arrows"), ("lock", "a padlock")],
]
STYLE = ("Image 1 is our UI kit; copy EXACTLY the style of its ICONS section: flat two-tone icons, dark navy #1E2B45 solid shapes "
         "with ONE cyan #2AB8F0 accent part each, thick rounded forms, no gradients, no outlines of another colour, no small details, "
         "no shadows. Draw exactly 16 separate icons in a 4 by 4 grid, evenly spaced, each centred in its own square cell with a wide "
         "empty margin, on a flat pure white background (#FFFFFF). No text, no labels, no grid lines, no frames. "
         "Left to right, top to bottom: ")


def sheet_path(k): return os.path.join(g.ROOT, "tools", "out", f"icon_v2_sheet{k}.png")


def generate(key, k, items):
    ref = Image.open(REF).convert("RGB"); ref = ref.crop((0, 0, int(ref.width * 0.33), ref.height))
    buf = io.BytesIO(); ref.save(buf, "PNG")
    text = STYLE + "; ".join(f"{i + 1}. {d}" for i, (_, d) in enumerate(items)) + "."
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
                    open(sheet_path(k), "wb").write(base64.b64decode(inline["data"])); print(f"  sheet {k}: {model}"); return True
    return False


def slice_sheet(k, items):
    im = Image.open(sheet_path(k)).convert("RGB"); W, H = im.size
    cw, ch = W / 4, H / 4
    for i, (name, _) in enumerate(items):
        cx, cy = i % 4, i // 4
        cell = im.crop((int(cx * cw), int(cy * ch), int((cx + 1) * cw), int((cy + 1) * ch)))
        cut = ic.cut_white(cell)
        bb = cut.getbbox()
        if not bb: print("  empty", name); continue
        cut = cut.crop(bb)
        side = int(max(cut.size) * 1.12)
        sq = Image.new("RGBA", (side, side), (0, 0, 0, 0)); sq.paste(cut, ((side - cut.width) // 2, (side - cut.height) // 2), cut)
        sq = sq.resize((256, 256), Image.LANCZOS)
        sq.save(os.path.join(OUT, name + ".png"))
    print(f"  sliced {len(items)} from sheet {k}")


if __name__ == "__main__":
    key = None if "--slice" in sys.argv else g.read_key()
    for k, items in enumerate(SHEETS):
        if key and not generate(key, k, items): print("  FAILED sheet", k); continue
        slice_sheet(k, items)
