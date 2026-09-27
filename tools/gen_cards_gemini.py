# -*- coding: utf-8 -*-
"""
Card illustrations v2 through Gemini (Nano Banana), FOR THE UNITY BUILD ONLY: each character's
CURRENT card goes in as the reference, and the same character comes back redrawn — same hair,
eyes, glasses, outfit design and colours, so the cast keeps its identity (the thing every earlier
regeneration lost) — but to the v2 direction: Blue Archive official-art finish, Blue Archive body
proportions, a beautiful young-adult woman (the protagonist: a handsome young man), glamour
rising with the grade through tailoring and attitude, never explicit; no halo (the game draws the
back sheet itself).

    python tools/gen_cards_gemini.py ceo cfo intern       # these
    python tools/gen_cards_gemini.py --all                # every hero + main job without an output yet
    python tools/gen_cards_gemini.py --force ceo
      → ArtSource/Cards_v2/<id>.png  (git-ignored; tools/cards_v2_import.py puts 512x748 in the game)

Reads grade / gender / name from the game's own data (Resources/Data/heroes.json, mainJobs.json).
SECURITY: the key is read from the env file by NAME and sent in a header; never printed or written.
"""
import base64, json, os, sys, time, urllib.request, urllib.error

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..")
DATA = os.path.join(ROOT, "Assets", "ExcelHeroes", "Resources", "Data")
CARDS = os.path.join(ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Cards")
OUT = os.path.join(ROOT, "ArtSource", "Cards_v2")
ENV_FILES = [os.environ.get("ENV_FILE", ""), r"C:\Users\minds\Desktop\mindsai_weaall.env", r"C:\Users\user\Desktop\mindsai_weaall.env"]
MODELS = ["gemini-3-pro-image-preview", "gemini-2.5-flash-image"]

GLAMOUR = {
    "D": "a neat, cute everyday office look; friendly smile",
    "C": "a fitted, cute office look; cheerful smile",
    "B": "a tailored, fitted outfit with the collar slightly open; confident smile",
    "A": "a stylish form-fitting outfit, an open blazer, collarbone showing; an alluring, elegant smile",
    "S": "a glamorous, elegant form-fitting outfit with luxurious details; a confident, alluring smile and a graceful pose; radiant",
}

PROMPT_F = (
    "Redraw THIS character as a polished Blue Archive official character illustration. Keep her identity exactly: the same "
    "hairstyle and hair colour, the same eye colour, the same glasses or accessories if she has them, the same outfit design and "
    "colour scheme, the same job props, the same general pose. Change the finish and the figure:\n"
    "- Blue Archive art style: clean thin lineart, soft two-tone cel shading, bright clear pastel palette, large sparkling "
    "detailed eyes, glossy hair highlights, a soft blurred background of her workplace that is bright and colourful, not washed out or hazy.\n"
    "- She is a beautiful young adult woman in her twenties (clearly an adult, not a child), with Blue Archive proportions: "
    "small face, slim figure, narrow waist; pretty and appealing, with a clearly feminine face and figure even if her hair is short.\n"
    "- Styling for her rank ({grade}): {glamour}. Tasteful: no nudity, no underwear, nothing see-through. Adult office wear, "
    "NOT a school uniform: no sailor collar, no pleated schoolgirl skirt, no school blazer-and-scarf look.\n"
    "- Waist-up (cowboy shot) portrait, the whole head in frame with space above the hair, face fully visible and brightly lit, "
    "looking at the viewer, centred, 2:3 portrait framing.\n"
    "- NO halo, no ring above the head, no text, no frame, no watermark."
)
PROMPT_M = (
    "Redraw THIS character as a polished Blue Archive official character illustration. Keep his identity exactly: the same "
    "hairstyle and hair colour, the same eye colour, the same outfit design and colours, lanyard and props, the same general pose. "
    "Change the finish: Blue Archive art style (clean thin lineart, soft two-tone cel shading, bright clear pastel palette, large "
    "bright detailed eyes, glossy hair highlights, a soft blurred office background that is bright and colourful, not hazy). He is a handsome young man in his "
    "twenties, slim, kind and cool, the likeable protagonist. Rank ({grade}): {glamour}. Waist-up portrait, whole head in frame, "
    "face fully visible and brightly lit, looking at the viewer, centred, 2:3 portrait framing. NO halo, no text, no frame."
)
GLAMOUR_M = {"D": "a neat intern's shirt and lanyard", "C": "a smart shirt and tie", "B": "a sharp fitted suit", "A": "a refined tailored suit with fine details", "S": "an impeccable luxury suit, radiant confidence"}


def read_key():
    for path in ENV_FILES:
        if path and os.path.exists(path):
            with open(path, encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if line.startswith("GEMINI_API_KEY="):
                        return line.split("=", 1)[1].strip().strip('"').strip("'")
    sys.exit("GEMINI_API_KEY not found (set ENV_FILE)")


def cast():
    def load(name):
        with open(os.path.join(DATA, name), encoding="utf-8") as f:
            d = json.load(f)
        return d if isinstance(d, list) else next(v for v in d.values() if isinstance(v, list))
    heroes = {h["id"]: (h.get("grade", "D"), "F") for h in load("heroes.json")}
    jobs = {j["id"]: (j.get("grade", "D"), "M") for j in load("mainJobs.json")}
    return {**heroes, **jobs}


def generate(key, ref, text, out):
    with open(ref, "rb") as f:
        img = base64.b64encode(f.read()).decode("ascii")
    body = json.dumps({
        "contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": img}}, {"text": text}]}],
        "generationConfig": {"responseModalities": ["IMAGE"], "temperature": 0.7, "imageConfig": {"aspectRatio": "2:3"}},
    }).encode("utf-8")
    for model in MODELS:
        req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent",
                                     data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=300) as r:
                data = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            print(f"    {model}: HTTP {e.code} {e.read().decode('utf-8', 'replace')[:200]}"); continue
        for cand in data.get("candidates", []):
            for part in cand.get("content", {}).get("parts", []):
                inline = part.get("inlineData") or part.get("inline_data")
                if inline and inline.get("data"):
                    with open(out, "wb") as f: f.write(base64.b64decode(inline["data"]))
                    return model
        print(f"    {model}: no image ({json.dumps(data)[:200]})")
    return None


if __name__ == "__main__":
    args = sys.argv[1:]
    force = "--force" in args
    c = cast()
    note = args[args.index("--note") + 1] if "--note" in args else None
    ids = [a for a in args if not a.startswith("--") and a != note] or (list(c) if "--all" in args else [])
    os.makedirs(OUT, exist_ok=True)
    key = read_key(); ok = 0
    for i in ids:
        if i not in c: print(f"  unknown {i}"); continue
        out = os.path.join(OUT, i + ".png")
        if os.path.exists(out) and not force: print(f"  skip {i}"); ok += 1; continue
        ref = os.path.join(CARDS, i + ".png")
        if not os.path.exists(ref): print(f"  no current card for {i}"); continue
        grade, sex = c[i]
        text = (PROMPT_F.format(grade=grade, glamour=GLAMOUR.get(grade, GLAMOUR["D"])) if sex == "F"
                else PROMPT_M.format(grade=grade, glamour=GLAMOUR_M.get(grade, GLAMOUR_M["D"])))
        # --feminine: for the ones the review (qa_cards_gemini.py) read as a man — the old card drew
        # her as a boy and "keep the identity" carried it over; the gender outranks the reference
        # --note "<text>": one run's extra direction for a character the review keeps flagging
        if "--note" in args: text += "\n" + args[args.index("--note") + 1]
        if "--feminine" in args and sex == "M":
            text += "\nIMPORTANT: he read as a boy. He is a grown man in his mid-to-late twenties: an adult face and build, a professional."
        if "--feminine" in args and sex == "F":
            text += ("\nIMPORTANT: the reference drew her too boyish. She is a WOMAN: give her an unmistakably feminine, pretty face "
                     "(softer jawline, feminine eyes with long lashes, soft lips), a feminine figure and posture. Keep her hairstyle "
                     "length, hair colour, outfit colours and props, but make the outfit's cut a women's cut. She is a grown woman "
                     "in her mid-twenties — an office professional, not a schoolgirl.")
        m = generate(key, ref, text, out)
        print(f"  {'ok  ' if m else 'FAIL'} {i} ({grade}{sex}){' ' + m if m else ''}"); ok += bool(m)
        time.sleep(1)
    print(f"done {ok}/{len(ids)}")
