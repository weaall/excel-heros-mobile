# -*- coding: utf-8 -*-
"""
Enemies v2 — one Blue Archive-style mascot per monster, through Gemini, with the CURRENT sprite
(Resources/Art/SDMonsters/<id>.png) as the identity reference: the same object, the same colours,
redrawn in ONE hand. The first set mixed cute mascots with faceless realistic office furniture;
the battle target mock (tools/out/design/target_3) has every enemy as a round, cute, readable
mascot with a face, and that is the rule here.

    python tools/gen_monsters_v2.py                 # every monster without a v2 image yet
    python tools/gen_monsters_v2.py copier boss_hr  # these
    python tools/gen_monsters_v2.py --force copier
      → ArtSource/Monsters_v2/<id>.png (raw, white) and Resources/Art/SDMonsters/<id>.png (cut out)

The cut-out is the icon tool's border flood (gen_icons_gemini.cut_white): the mascots are drawn on
flat white with a dark outline, so a flood from the border stops at the outline and keeps every
white highlight inside.
SECURITY: the key is read from the env file by NAME and sent in a header; never printed or written.
"""
import base64, io, json, os, sys, time, urllib.request, urllib.error
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import gen_icons_gemini as ic

SRC = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "SDMonsters")
RAW = os.path.join(g.ROOT, "ArtSource", "Monsters_v2")

PROMPT = (
    "The attached image is one enemy of our Blue Archive-style mobile game, where the enemies are spreadsheet and office "
    "errors come to life. Redraw THIS SAME enemy (same object, same main colours, same idea) as a Blue Archive-quality "
    "cute chibi mascot monster: a round, chunky, simple silhouette; a clear cute face with big expressive eyes and a "
    "mischievous or grumpy mouth; tiny stubby arms and feet if it has any; thick clean dark navy outline; soft two-tone cel "
    "shading with a white highlight on the upper left; bright pastel colours; 3/4 view facing left; full body, centred, "
    "filling about 80% of the frame. {boss}"
    "Plain flat pure white background (#FFFFFF), nothing else: no floor, no shadow, no text, no letters, no frame."
)
BOSS = "It is a BOSS: bigger and more imposing, a small crown or red warning-sign ornament, a fierce but still cute face. "


def generate(key, ref_path, text, out):
    im = Image.open(ref_path).convert("RGBA"); bg = Image.new("RGBA", im.size, (255, 255, 255, 255)); bg.alpha_composite(im)
    buf = io.BytesIO(); bg.convert("RGB").save(buf, "PNG")
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": base64.b64encode(buf.getvalue()).decode("ascii")}}, {"text": text}]}],
                       "generationConfig": {"responseModalities": ["IMAGE"], "temperature": 0.7, "imageConfig": {"aspectRatio": "1:1"}}}).encode("utf-8")
    for model in g.MODELS:
        req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent",
                                     data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=300) as r: data = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            print(f"    {model}: HTTP {e.code} {e.read().decode('utf-8', 'replace')[:160]}"); continue
        for c in data.get("candidates", []):
            for p in c.get("content", {}).get("parts", []):
                inline = p.get("inlineData") or p.get("inline_data")
                if inline and inline.get("data"):
                    with open(out, "wb") as f: f.write(base64.b64decode(inline["data"]))
                    return model
    return None


def cut(raw, dst):
    im = ic.cut_white(Image.open(raw).convert("RGB"))
    bbox = im.getbbox()
    if not bbox: return False
    icon = im.crop(bbox); side = int(max(icon.size) * 1.06)
    canvas = Image.new("RGBA", (side, side), (255, 255, 255, 0))
    # feet on the floor: bottom-aligned, horizontally centred
    canvas.paste(icon, ((side - icon.width) // 2, side - icon.height - int(side * 0.02)), icon)
    canvas.resize((512, 512), Image.LANCZOS).save(dst)
    return True


if __name__ == "__main__":
    args = sys.argv[1:]; force = "--force" in args
    ids = [a for a in args if not a.startswith("--")] or sorted(f[:-4] for f in os.listdir(SRC) if f.endswith(".png"))
    os.makedirs(RAW, exist_ok=True)
    key = g.read_key(); ok = 0
    for i in ids:
        ref = os.path.join(SRC, i + ".png"); raw = os.path.join(RAW, i + ".png")
        if not os.path.exists(ref): print(f"  no sprite {i}"); continue
        if os.path.exists(raw) and not force: print(f"  skip {i}"); ok += 1; continue
        # keep the original once, so a redo still has the first identity to work from
        orig = os.path.join(RAW, "_orig_" + i + ".png")
        if not os.path.exists(orig): Image.open(ref).save(orig)
        m = generate(key, orig, PROMPT.format(boss=BOSS if i.startswith("boss") else ""), raw)
        if m and cut(raw, ref): print(f"  ok   {i} ({m})"); ok += 1
        else: print(f"  FAIL {i}")
        time.sleep(1)
    print(f"done {ok}/{len(ids)}")
