# -*- coding: utf-8 -*-
"""
UI icons, generated with Gemini's image model ("Nano Banana") and cut out for Unity.

The reference game's icons are small illustrations — a megaphone, a pink phone, a clipboard, a
shopping bag — with a dark outline, two-step cel shading and a white highlight. Line icons from an
icon font are the furthest thing from that, and they were what the user singled out.

All sixteen are asked for in ONE image so they share one hand: separately generated icons drift
in outline weight, palette and angle, and a bar of mismatched icons reads worse than a bar of
plain ones.

    python tools/gen_icons_gemini.py            # generate a sheet, then slice it
    python tools/gen_icons_gemini.py --slice    # re-slice the last sheet only

SECURITY: the key is read from the env file by NAME and sent in a header. It is never printed,
never written anywhere, never put in a URL. Set ENV_FILE to point somewhere else.
"""
import base64, io, json, os, sys, urllib.request, urllib.error
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ENV_FILE = os.environ.get("ENV_FILE", r"C:\Users\user\Desktop\mindsai_weaall.env")
OUT_DIR = os.path.join(ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Icons")
SHEET = os.path.join(ROOT, "tools", "out", "icon_sheet.png")

# Row-major, 4 x 4. The file name is what the game loads.
ICONS = [
    ("lobby",    "a small modern office building with blue glass windows"),
    ("roster",   "an employee ID card on a lanyard with a tiny portrait"),
    ("party",    "a clipboard showing a small organisation chart"),
    ("tasks",    "a clipboard with a checklist and a red check mark"),
    ("review",   "a magnifying glass over a document with a gold star"),
    ("messenger","a pink smartphone with a white chat bubble on screen"),
    ("album",    "a photo album with a polaroid photo sticking out"),
    ("codex",    "a thick navy ring binder with coloured index tabs"),
    ("chart",    "a small monitor showing a rising bar chart"),
    ("recruit",  "a white envelope with a glowing cyan star seal, sparkles"),
    ("notice",   "a blue and white megaphone"),
    ("shop",     "a light blue shopping bag with a white ribbon"),
    ("gold",     "a shiny gold coin with a small won sign"),
    ("gem",      "a faceted light blue crystal gem"),
    ("mail",     "a navy envelope with a small red dot"),
    ("settings", "a navy gear"),
]

STYLE = (
    "Mobile game UI icon set in the style of a bright Japanese anime gacha game interface. "
    "Each icon is a small glossy illustration: thick dark navy outline, simple two-tone cel shading, "
    "a clean white highlight on the upper left, pastel colours, 3/4 view, cute and readable at small size. "
    "Flat pure white background (#FFFFFF). No text, no letters, no numbers, no labels, no grid lines, "
    "no frames, no drop shadows on the background. "
)

def prompt():
    items = "; ".join(f"{i + 1}. {d}" for i, (_, d) in enumerate(ICONS))
    return (STYLE +
            "Arrange exactly 16 separate icons in a 4 by 4 grid, evenly spaced, each centred in its own "
            "square cell with generous empty white margin around it so no two icons touch. "
            "Left to right, top to bottom: " + items + ".")

def read_key():
    with open(ENV_FILE, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line.startswith("GEMINI_API_KEY="):
                return line.split("=", 1)[1].strip().strip('"').strip("'")
    sys.exit("GEMINI_API_KEY not found in the env file")

MODELS = ["gemini-2.5-flash-image", "gemini-2.5-flash-image-preview", "gemini-3-pro-image-preview"]

def generate():
    key = read_key()
    body = json.dumps({
        "contents": [{"parts": [{"text": prompt()}]}],
        "generationConfig": {"responseModalities": ["IMAGE"], "imageConfig": {"aspectRatio": "1:1"}},
    }).encode("utf-8")
    last = None
    for model in MODELS:
        req = urllib.request.Request(
            f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent",
            data=body, method="POST",
            headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=180) as r:
                data = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            # The error body is Google's message, not the key — safe to show, and the only way
            # to tell "model not found" from "quota" from "bad request".
            msg = e.read().decode("utf-8", "replace")[:400]
            print(f"  {model}: HTTP {e.code} {msg}")
            last = e
            continue
        for cand in data.get("candidates", []):
            for part in cand.get("content", {}).get("parts", []):
                inline = part.get("inlineData") or part.get("inline_data")
                if inline and inline.get("data"):
                    os.makedirs(os.path.dirname(SHEET), exist_ok=True)
                    with open(SHEET, "wb") as f:
                        f.write(base64.b64decode(inline["data"]))
                    usage = data.get("usageMetadata", {})
                    print(f"  {model}: sheet saved ({usage.get('totalTokenCount', '?')} tokens)")
                    return
        print(f"  {model}: no image in the response ({json.dumps(data)[:300]})")
    sys.exit(f"no model produced an image ({last})")

def cut_white(im, tol=26):
    """Flood the near-white background inward from the border and make it transparent.
    A flood, not a colour key: white highlights INSIDE an icon are enclosed by its outline and
    must survive."""
    im = im.convert("RGBA")
    w, h = im.size
    px = im.load()
    seen = bytearray(w * h)
    stack = [(x, 0) for x in range(w)] + [(x, h - 1) for x in range(w)] + \
            [(0, y) for y in range(h)] + [(w - 1, y) for y in range(h)]
    def bg(p):
        r, g, b, a = p
        return r > 255 - tol and g > 255 - tol and b > 255 - tol
    while stack:
        x, y = stack.pop()
        i = y * w + x
        if seen[i]: continue
        seen[i] = 1
        if not bg(px[x, y]): continue
        px[x, y] = (255, 255, 255, 0)
        if x > 0: stack.append((x - 1, y))
        if x < w - 1: stack.append((x + 1, y))
        if y > 0: stack.append((x, y - 1))
        if y < h - 1: stack.append((x, y + 1))
    # Soften the one-pixel rim the flood leaves: partly-white edge pixels get partial alpha.
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a and min(r, g, b) > 200:
                n = sum(1 for dx, dy in ((1,0),(-1,0),(0,1),(0,-1))
                        if 0 <= x+dx < w and 0 <= y+dy < h and px[x+dx, y+dy][3] == 0)
                if n: px[x, y] = (r, g, b, int(255 * (255 - min(r, g, b)) / 55))
    return im

def slice_sheet():
    sheet = Image.open(SHEET).convert("RGB")
    W, H = sheet.size
    cw, ch = W / 4, H / 4
    os.makedirs(OUT_DIR, exist_ok=True)
    for i, (name, _) in enumerate(ICONS):
        c, r = i % 4, i // 4
        cell = sheet.crop((int(c * cw), int(r * ch), int((c + 1) * cw), int((r + 1) * ch)))
        cut = cut_white(cell)
        bbox = cut.getbbox()
        if not bbox:
            print(f"  {name}: EMPTY cell"); continue
        icon = cut.crop(bbox)
        # Square it, with a little air, and settle on 256px: large enough for a 96px slot at 2x.
        side = int(max(icon.size) * 1.08)
        canvas = Image.new("RGBA", (side, side), (255, 255, 255, 0))
        canvas.paste(icon, ((side - icon.width) // 2, (side - icon.height) // 2), icon)
        canvas.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT_DIR, f"{name}.png"))
        edge = bbox[0] < 2 or bbox[1] < 2 or bbox[2] > cell.width - 2 or bbox[3] > cell.height - 2
        print(f"  {name}: {icon.size[0]}x{icon.size[1]}{'  (touches the cell edge — check it)' if edge else ''}")

if __name__ == "__main__":
    if "--slice" not in sys.argv:
        generate()
    slice_sheet()
