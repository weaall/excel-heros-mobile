# -*- coding: utf-8 -*-
"""
Enemies v3 — office objects on the heroes' chibi skeleton (World/SdEnemy): each enemy's HEAD is the
object itself with its face, and its torso and limbs are built from the object's own materials. The
design came from Gemini concepts (tools/out/design/concept_*: a copier-headed robot with cable arms,
a snake-headed one with paper-stack limbs and stapler knees).

For every mascot (Resources/Art/SDMonsters/<id>.png):
  1. Gemini (image) draws ONLY the head, straight from the front, same style
       → ArtSource/EnemyHeads/<id>.png (raw) and Resources/Art/SDEnemyHeads/<id>.png (cut out)
     tools/mon3d_inflate.py then makes the plush head mesh from it (SDEnemyHeads → SD3DM/head_<id>)
  2. Gemini (vision) reads the body recipe: material (metal | paper | soft | energy | wood), four
     colours and a signature part → Resources/Data/enemybody.json

    python tools/gen_enemyhead_gemini.py            # every mascot missing one
    python tools/gen_enemyhead_gemini.py copier --force
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, io, json, os, re, sys, time, urllib.request
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import gen_icons_gemini as ic
import gen_monsters_v2 as mv

SRC = mv.SRC
RAW = os.path.join(g.ROOT, "ArtSource", "EnemyHeads")
OUT = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "SDEnemyHeads")
BODY = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Data", "enemybody.json")
VISION = os.environ.get("QA_MODEL", "gemini-3.8-flash")

HEAD = ("The attached image is one enemy mascot of our Blue Archive-style game (office errors and office objects come to life). "
        "It is going to be the HEAD of a chibi humanoid monster whose body we build separately. Draw ONLY that head: the object "
        "itself with its face (the same object, colours, eyes and mouth as the attached mascot), seen STRAIGHT FROM THE FRONT, "
        "symmetric, facing the viewer; no arms, no hands, no legs, no feet, no body, no tail, no stand. Rounded chunky toy-like "
        "shape, thick clean dark navy outline, soft two-tone cel shading, white highlight at the upper left. {boss}"
        "Centred, filling about 85% of the frame. Plain flat pure white background (#FFFFFF), nothing else: no shadow, no text.")
BOSS = "It is a BOSS: keep its crown or warning ornament on top. "
ASK = ("This is an enemy mascot of our game. We build its body as a small humanoid robot/toy made of the object's own materials. "
       "Answer JSON only: {\"material\": \"metal\" | \"paper\" | \"soft\" | \"energy\" | \"wood\", "
       "\"main\": \"#rrggbb\", \"second\": \"#rrggbb\", \"dark\": \"#rrggbb\", \"accent\": \"#rrggbb\", "
       "\"part\": \"cable\" | \"spring\" | \"paper\" | \"tape\" | \"panel\" | \"fur\" | \"glow\" | \"bolt\"}. "
       "material: what the limbs should be made of (a machine = metal, a document / sheet / chart = paper, an animal or plant = soft, "
       "a flame / orb / ghost / virus / cursor = energy, furniture = wood). main = the object's main colour, second = its second, "
       "dark = its darkest (joints, soles), accent = a small highlight colour. part = the detail that wraps the limbs.")


def head(key, i):
    raw = os.path.join(RAW, i + ".png"); dst = os.path.join(OUT, i + ".png")
    text = HEAD.replace("{boss}", BOSS if i.startswith("boss") else "")
    m = mv.generate(key, os.path.join(SRC, i + ".png"), text, raw)
    if not m: return None
    im = ic.cut_white(Image.open(raw).convert("RGB"))
    im = im.crop(im.getbbox()); im.thumbnail((512, 512))
    sq = Image.new("RGBA", (max(im.size) + 16,) * 2, (0, 0, 0, 0)); sq.paste(im, ((sq.width - im.width) // 2, (sq.height - im.height) // 2), im)
    sq.save(dst)
    return m


def body(key, i):
    im = Image.open(os.path.join(SRC, i + ".png")).convert("RGBA"); bg = Image.new("RGB", im.size, (255, 255, 255)); bg.paste(im, (0, 0), im)
    buf = io.BytesIO(); bg.save(buf, "PNG")
    parts = [{"inlineData": {"mimeType": "image/png", "data": base64.b64encode(buf.getvalue()).decode("ascii")}}, {"text": ASK}]
    data = json.dumps({"contents": [{"parts": parts}], "generationConfig": {"responseMimeType": "application/json", "temperature": 0}}).encode("utf-8")
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{VISION}:generateContent",
                                 data=data, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
    for _ in range(3):
        try:
            with urllib.request.urlopen(req, timeout=120) as r: a = json.loads(json.loads(r.read().decode("utf-8"))["candidates"][0]["content"]["parts"][0]["text"])
            ok = lambda c: isinstance(c, str) and re.fullmatch(r"#[0-9a-fA-F]{6}", c)
            if all(ok(a.get(k)) for k in ("main", "second", "dark", "accent")): return a
        except (OSError, KeyError, json.JSONDecodeError): time.sleep(3)
    return None


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]; force = "--force" in sys.argv
    os.makedirs(RAW, exist_ok=True); os.makedirs(OUT, exist_ok=True)
    ids = args or sorted(p[:-4] for p in os.listdir(SRC) if p.endswith(".png"))
    key = g.read_key()
    table = json.load(open(BODY, encoding="utf-8")) if os.path.exists(BODY) else {"items": []}
    rows = {r["id"]: r for r in table["items"]}
    for i in ids:
        if force or not os.path.exists(os.path.join(OUT, i + ".png")):
            print(f"  head {i}: {head(key, i) or 'FAILED'}")
        if force or i not in rows:
            b = body(key, i)
            if b:
                rows[i] = {"id": i, "material": b.get("material", "metal"), "main": b["main"], "second": b["second"],
                           "dark": b["dark"], "accent": b["accent"], "part": b.get("part", "panel")}
                print(f"  body {i}: {rows[i]['material']} {rows[i]['part']}")
        table["items"] = sorted(rows.values(), key=lambda r: r["id"])
        json.dump(table, open(BODY, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
