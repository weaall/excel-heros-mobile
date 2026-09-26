# -*- coding: utf-8 -*-
"""
The spreadsheet errors as SD monster sprites, with Gemini (Nano Banana): 16 to a sheet in a 4x4
grid on white, sliced into one PNG per monster (tools/out/monsters/<id>.png). The cut-out and
normalisation is the web repo's tools/cutout_ai.py with MONSTER=1.

    python tools/gen_monsters_gemini.py

Monsters are not the cast: the cast's illustrations come from Hugging Face; these come from
Gemini with the UI assets and backdrops. SECURITY: the key is read from the env file by NAME,
sent in a header, never printed or written anywhere.
"""
import base64, json, os, sys, urllib.request, urllib.error
from PIL import Image

ENV_FILE = os.environ.get("ENV_FILE", r"C:\Users\user\Desktop\mindsai_weaall.env")
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out", "monsters")
MODELS = ["gemini-2.5-flash-image", "gemini-2.5-flash-image-preview"]

MONSTERS = [
    ("circ", "a round blue slime made of looping circular arrows"),
    ("merged", "a chunky block made of merged spreadsheet cells, grumpy"),
    ("ref", "a floating purple diamond crystal with a broken chain link"),
    ("virus", "a spiky green virus ball"),
    ("copier", "an angry little photocopier spitting out paper"),
    ("shredder", "a paper shredder with jagged teeth"),
    ("ceo_chair", "an evil black office swivel chair creature"),
    ("slide", "a living presentation slide with a pie chart on it"),
    ("stapler", "a red stapler shaped like a little crocodile"),
    ("ghost", "a little ghost made of a sheet of grid paper"),
    ("sheet", "a living spreadsheet page with an angry face"),
    ("chart", "a living bar chart with bars for arms"),
    ("hourglass", "a nervous hourglass creature, a deadline"),
    ("lock", "a golden padlock creature"),
    ("bug", "a software bug beetle, green shell"),
    ("cloud", "a small grumpy grey cloud with a lightning bolt"),
    ("cursor", "a white mouse pointer arrow creature"),
    ("monkey", "a mischievous little monkey wearing a tiny necktie"),
    ("bull", "a small angry red bull"),
    ("mushroom", "a sleepy mushroom creature"),
    ("eyeball", "a floating eyeball with little bat wings"),
    ("hand", "a floating white glove hand"),
    ("golem", "a golem made of stacked cardboard file boxes"),
    ("flame", "a small orange fire spirit"),
    ("orb", "a glowing cyan orb spirit"),
    ("rabbit", "a fluffy white rabbit creature with a stopwatch"),
    ("chicken", "a plump chicken creature"),
    ("cat", "a sly black cat creature"),
    ("rat", "a grey office rat creature holding a cable"),
    ("snake", "a green snake creature coiled up"),
    ("robot", "a small boxy robot with a monitor for a face"),
    ("boss", "a big boss monster: a giant burning URGENT paper ticket with angry eyes, flames around it"),
    ("boss_zombie", "a big boss monster: a zombie office manager in a green suit, groggy, holding a coffee mug"),
    ("boss_ogre", "a big boss monster: a hulking blue ogre in a tight shirt and necktie"),
    ("boss_audit", "a big boss monster: a giant magnifying glass creature with a stern eye"),
    ("boss_target", "a big boss monster: a giant sales target dartboard creature"),
    ("boss_approval", "a big boss monster: a giant red rubber approval stamp creature"),
    ("boss_copier", "a big boss monster: a huge industrial copier machine with glowing eyes"),
    ("boss_cabinet", "a big boss monster: a towering filing cabinet with drawers for mouths"),
    ("boss_elevator", "a big boss monster: an elevator whose doors are a toothy mouth"),
    ("boss_hr", "a big boss monster: a giant HR clipboard creature with a red pen"),
    ("boss_ledger", "a big boss monster: a huge leather ledger book with a golden lock"),
    ("boss_legal", "a big boss monster: a giant judge's gavel creature with scales"),
]

STYLE = ("A sprite sheet of cute chibi MONSTER enemies for an anime mobile gacha game in the style of Blue Archive: "
         "clean cel shading, a thin dark outline, bright saturated colours, big expressive eyes, mischievous and a little angry. "
         "Every monster faces LEFT, is shown whole (full body), and stands alone. "
         "Plain pure white background everywhere, with NO cell borders, NO boxes, NO frames, NO grid lines. "
         "No people, no text, no labels, no numbers. ")

def read_key():
    with open(ENV_FILE, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line.startswith("GEMINI_API_KEY="):
                return line.split("=", 1)[1].strip().strip('"').strip("'")
    sys.exit("GEMINI_API_KEY not found in the env file")

def sheet(batch, key, path):
    rows = (len(batch) + 3) // 4
    layout = (f"Arrange exactly {len(batch)} monsters in an invisible grid of 4 columns and {rows} rows, each centred in its own "
              "area with generous empty white margin so no two touch and none is cut off by the edge. ")
    items = layout + "; ".join(f"{i + 1}. {d}" for i, (_, d) in enumerate(batch))
    if len(batch) % 4:
        items += "; leave the remaining spots empty white"
    body = json.dumps({
        "contents": [{"parts": [{"text": STYLE + "Left to right, top to bottom: " + items + "."}]}],
        "generationConfig": {"responseModalities": ["IMAGE"], "imageConfig": {"aspectRatio": "1:1"}},
    }).encode("utf-8")
    for model in MODELS:
        req = urllib.request.Request(
            f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent",
            data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=240) as r:
                data = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            print(f"  {model}: HTTP {e.code} {e.read().decode('utf-8', 'replace')[:300]}")
            continue
        for cand in data.get("candidates", []):
            for part in cand.get("content", {}).get("parts", []):
                inline = part.get("inlineData") or part.get("inline_data")
                if inline and inline.get("data"):
                    with open(path, "wb") as f:
                        f.write(base64.b64decode(inline["data"]))
                    return True
        print(f"  {model}: no image")
    return False

def slice_sheet(path, batch):
    im = Image.open(path).convert("RGB")
    w, h = im.size
    # a short batch comes back laid out in as few rows as it needs (11 → 4 x 3)
    rows = (len(batch) + 3) // 4
    cw, ch = w / 4, h / rows
    for i, (mid, _) in enumerate(batch):
        x, y = i % 4, i // 4
        # inset: some sheets come back with a thin frame round every cell
        ix, iy = cw * 0.07, ch * 0.07
        cell = im.crop((int(x * cw + ix), int(y * ch + iy), int((x + 1) * cw - ix), int((y + 1) * ch - iy)))
        # pad onto a white square so the cut-out models see margin on every side
        side = max(cell.size) + 60
        pad = Image.new("RGB", (side, side), (255, 255, 255))
        pad.paste(cell, ((side - cell.width) // 2, (side - cell.height) // 2))
        pad.resize((768, 768), Image.LANCZOS).save(os.path.join(OUT, f"{mid}.png"))

if __name__ == "__main__":
    if "--slice" in sys.argv:   # re-slice the sheets already on disk
        os.makedirs(OUT, exist_ok=True)
        for b in range(0, len(MONSTERS), 16):
            path = os.path.join(OUT, f"_sheet{b // 16}.png")
            if os.path.exists(path): slice_sheet(path, MONSTERS[b:b + 16])
        print("resliced"); sys.exit(0)
    key = read_key()
    os.makedirs(OUT, exist_ok=True)
    only = set(a for a in sys.argv[1:] if not a.startswith("--"))
    if "--bosses" in sys.argv: only = {m[0] for m in MONSTERS if m[0].startswith("boss")}
    todo = [m for m in MONSTERS if not only or m[0] in only]
    for b in range(0, len(todo), 16):
        batch = todo[b:b + 16]
        # a partial run gets its own sheet file, so --slice never re-cuts it from the wrong layout
        tag = f"{b // 16}" if not only else "_" + batch[0][0]
        path = os.path.join(OUT, f"_sheet{tag}.png")
        if sheet(batch, key, path):
            slice_sheet(path, batch)
            print(f"  sheet {b // 16}: {len(batch)} monsters")
    print("done")
