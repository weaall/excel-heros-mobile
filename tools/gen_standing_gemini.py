# -*- coding: utf-8 -*-
"""
Standing art v2 through Gemini (Nano Banana), FOR THE UNITY BUILD ONLY — the full-body figure
the lobby, the detail page, the summon stage and every portrait crop use. The character's v2
card (ArtSource/Cards_v2, else the current card) goes in as the identity reference; a full-body
figure on flat white comes back, drawn to Blue Archive's standing-art body (about six and a half
heads, long legs). Then the web repo's own checks finish it, run on OUR folders:

    python tools/gen_standing_gemini.py ceo cfo              → ArtSource/Standing_v2/raw/<id>.png
    python ../excel-heros/tools/cutout_ai.py ArtSource/Standing_v2/raw/*.png --out ArtSource/Standing_v2/alpha
    python ../excel-heros/tools/uniform.py std ArtSource/Standing_v2/alpha ArtSource/Standing_v2/uniform
    (bash tools/standing_v2.sh does all three and copies the result into Resources/Art/Standing)

uniform.py gives the whole cast ONE body — the same head size and height, feet on one line —
which is what "Blue Archive proportions" means across a roster, and lists anything it would have
to distort too far for regeneration instead.

SECURITY: the key is read from the env file by NAME and sent in a header; never printed or written.
"""
import os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

OUT = os.path.join(g.ROOT, "ArtSource", "Standing_v2", "raw")
V2 = os.path.join(g.ROOT, "ArtSource", "Cards_v2")

LEGS = {
    "D": "a knee-length skirt or neat trousers, simple shoes",
    "C": "a fitted skirt with black pantyhose, neat shoes",
    "B": "a fitted pencil skirt with black pantyhose or thighhighs, low heels",
    "A": "a stylish fitted skirt with black thighhighs, heels",
    "S": "an elegant fitted skirt or dress with black pantyhose, high heels",
}
PROMPT_F = (
    "Draw THIS character as a Blue Archive official full-body standing illustration. Keep her identity exactly: the same face, "
    "hairstyle and hair colour, eye colour, glasses or accessories, the same outfit design and colours, the same job props.\n"
    "- Full body, standing, from the top of the hair to the soles of the shoes, feet visible, a thin strip of empty space above the "
    "head and below the feet; the figure fills the height of the image.\n"
    "- Blue Archive standing-art body: a beautiful young adult woman in her twenties (clearly an adult), about six and a half heads "
    "tall, small face, slim figure, narrow waist, long slender legs.\n"
    "- Styling for her rank ({grade}): {glamour}; below the waist {legs}. Tasteful: no nudity, no underwear, nothing see-through. Adult office wear, NOT a school uniform.\n"
    "- A relaxed natural standing pose, body at a slight three-quarter angle, face towards the viewer, both hands visible.\n"
    "- Background: plain flat pure white, nothing else — no floor, no shadow under the feet, no scenery, no gradient.\n"
    "- Blue Archive finish: clean thin lineart, soft two-tone cel shading, bright clear colours, large sparkling eyes. "
    "NO halo, no text, no frame."
)
PROMPT_M = (
    "Draw THIS character as a Blue Archive official full-body standing illustration. Keep his identity exactly: the same face, "
    "hairstyle and hair colour, eye colour, the same outfit design and colours, lanyard and props. Full body, standing, from the top "
    "of the hair to the soles of the shoes, feet visible, a thin strip of empty space above the head and below the feet, the figure "
    "filling the height of the image. A handsome young man in his twenties, about six and a half heads tall, slim. A relaxed natural "
    "standing pose, slight three-quarter angle, face towards the viewer, both hands visible. Background: plain flat pure white, no "
    "floor, no shadow, no scenery. Blue Archive finish (clean thin lineart, soft two-tone cel shading, bright clear colours). NO halo, "
    "no text, no frame."
)

if __name__ == "__main__":
    args = sys.argv[1:]
    force = "--force" in args
    cast = g.cast()
    # the protagonist has one standing figure for every job: the intern's
    standing_ids = [i for i, (_, sex) in cast.items() if sex == "F"] + ["intern"]
    ids = [a for a in args if not a.startswith("--")] or (standing_ids if "--all" in args else [])
    os.makedirs(OUT, exist_ok=True)
    key = g.read_key(); ok = 0
    for i in ids:
        out = os.path.join(OUT, i + ".png")
        if os.path.exists(out) and not force: print(f"  skip {i}"); ok += 1; continue
        ref = os.path.join(V2, i + ".png")
        if not os.path.exists(ref): ref = os.path.join(g.CARDS, i + ".png")
        grade, sex = cast.get(i, ("D", "F"))
        text = (PROMPT_F.format(grade=grade, glamour=g.GLAMOUR.get(grade, g.GLAMOUR["D"]), legs=LEGS.get(grade, LEGS["D"])) if sex == "F" else PROMPT_M)
        # 9:16 — the standing canvas is 768x1344 (4:7); uniform.py rescales and places the figure
        m = None
        body = None
        import base64, json, urllib.request, urllib.error
        with open(ref, "rb") as f: img = base64.b64encode(f.read()).decode("ascii")
        body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": img}}, {"text": text}]}],
                           "generationConfig": {"responseModalities": ["IMAGE"], "temperature": 0.6, "imageConfig": {"aspectRatio": "9:16"}}}).encode("utf-8")
        for model in g.MODELS:
            req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent",
                                         data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
            try:
                with urllib.request.urlopen(req, timeout=300) as r: data = json.loads(r.read().decode("utf-8"))
            except urllib.error.HTTPError as e:
                print(f"    {model}: HTTP {e.code} {e.read().decode('utf-8', 'replace')[:200]}"); continue
            parts = [p for c in data.get("candidates", []) for p in c.get("content", {}).get("parts", [])]
            inline = next((p.get("inlineData") or p.get("inline_data") for p in parts if (p.get("inlineData") or p.get("inline_data"))), None)
            if inline and inline.get("data"):
                with open(out, "wb") as f: f.write(base64.b64decode(inline["data"]))
                m = model; break
            print(f"    {model}: no image ({json.dumps(data)[:200]})")
        print(f"  {'ok  ' if m else 'FAIL'} {i} ({grade}{sex}){' ' + m if m else ''}"); ok += bool(m)
        time.sleep(1)
    print(f"done {ok}/{len(ids)}")
