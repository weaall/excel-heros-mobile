# -*- coding: utf-8 -*-
"""
More UI icons in the SAME hand as the existing set: four of the current icons (Resources/Art/
Icons) go in as the style reference, and a 2x2 sheet of new ones comes back, sliced and cut out
with gen_icons_gemini.cut_white. Adding icons one batch at a time this way keeps the outline
weight and palette of the first sixteen.

    python tools/gen_more_icons.py            # the batch below → Resources/Art/Icons/<name>.png
SECURITY: the key is read from the env file by NAME and sent in a header; never printed or written.
"""
import base64, io, json, os, sys, urllib.request, urllib.error
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import gen_icons_gemini as ic

BATCH = [
    ("eq_keyboard", "a white computer keyboard with light blue keys"),
    ("eq_chair",    "a navy office chair on wheels"),
    ("eq_monitor",  "a computer monitor with a glowing light blue screen"),
    ("eq_badge",    "an employee ID badge on a light blue lanyard"),
]
REF = ["lobby", "tasks", "gem", "messenger"]
SHEET = os.path.join(g.ROOT, "tools", "out", "icon_sheet_more.png")


def reference():
    cells = [Image.open(os.path.join(ic.OUT_DIR, n + ".png")).convert("RGBA").resize((256, 256)) for n in REF]
    ref = Image.new("RGB", (512, 512), "white")
    for i, c in enumerate(cells): ref.paste(c, ((i % 2) * 256, (i // 2) * 256), c)
    buf = io.BytesIO(); ref.save(buf, "PNG"); return base64.b64encode(buf.getvalue()).decode("ascii")


def generate():
    items = "; ".join(f"{i + 1}. {d}" for i, (_, d) in enumerate(BATCH))
    text = ("The attached image shows four icons from our game's UI icon set. Draw FOUR NEW icons in exactly the same style "
            "(same thick dark navy outline weight, same two-tone cel shading, same white highlight on the upper left, same pastel "
            "palette, same 3/4 view and scale). " + ic.STYLE +
            "Arrange exactly 4 separate icons in a 2 by 2 grid, each centred in its own square cell with generous empty white "
            "margin so no two icons touch. Left to right, top to bottom: " + items + ".")
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": reference()}}, {"text": text}]}],
                       "generationConfig": {"responseModalities": ["IMAGE"], "imageConfig": {"aspectRatio": "1:1"}}}).encode("utf-8")
    key = g.read_key()
    for model in g.MODELS:
        req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent",
                                     data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=240) as r: data = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            print(f"  {model}: HTTP {e.code} {e.read().decode('utf-8', 'replace')[:200]}"); continue
        for c in data.get("candidates", []):
            for p in c.get("content", {}).get("parts", []):
                inline = p.get("inlineData") or p.get("inline_data")
                if inline and inline.get("data"):
                    with open(SHEET, "wb") as f: f.write(base64.b64decode(inline["data"]))
                    print(f"  sheet with {model}"); return
    sys.exit("no image")


def slice_sheet():
    sheet = Image.open(SHEET).convert("RGB"); W, H = sheet.size
    for i, (name, _) in enumerate(BATCH):
        c, r = i % 2, i // 2
        cut = ic.cut_white(sheet.crop((int(c * W / 2), int(r * H / 2), int((c + 1) * W / 2), int((r + 1) * H / 2))))
        bbox = cut.getbbox()
        if not bbox: print(f"  {name}: EMPTY"); continue
        icon = cut.crop(bbox); side = int(max(icon.size) * 1.08)
        canvas = Image.new("RGBA", (side, side), (255, 255, 255, 0))
        canvas.paste(icon, ((side - icon.width) // 2, (side - icon.height) // 2), icon)
        canvas.resize((256, 256), Image.LANCZOS).save(os.path.join(ic.OUT_DIR, f"{name}.png"))
        print(f"  {name}")


if __name__ == "__main__":
    if "--slice" not in sys.argv: generate()
    slice_sheet()
