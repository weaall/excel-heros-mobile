# -*- coding: utf-8 -*-
"""
A LAYOUT reference for the boss-fight HUD, from Gemini: our own battle capture goes in, and the
same screen comes back with a Blue Archive style boss HP bar, hit rings and a kill counter drawn
on it — everything else left where it is. The output is a picture to build the HUD against in
UI Toolkit, never an asset the game ships (the "do not generate art" rule is about the character
illustrations; this is a mock-up, like the reference screenshots in temp_images).

    python tools/gen_hudref_gemini.py [capture.png] [variants]
      capture   default tools/out/shots/07-BattleHud.png
      variants  default 3  → tools/out/hudref/hudref_<n>.png

SECURITY: the key is read from the env file by NAME and sent in a header. It is never printed,
logged or written anywhere else.
"""
import base64, json, os, sys, urllib.request, urllib.error

HERE = os.path.dirname(os.path.abspath(__file__))
ENV_FILES = [os.environ.get("ENV_FILE", ""), r"C:\Users\minds\Desktop\mindsai_weaall.env", r"C:\Users\user\Desktop\mindsai_weaall.env"]
OUT = os.path.join(HERE, "out", "hudref")
MODELS = ["gemini-3-pro-image-preview", "gemini-2.5-flash-image"]

PROMPT = (
    "This is a screenshot of our landscape mobile gacha game's battle screen (1200x540). Redraw THIS SAME SCREEN as a UI "
    "layout mock-up of a BOSS fight, in the HUD style of Blue Archive's boss battles. Keep the camera, the street, the "
    "characters, the monsters, the top bar, the top-right pill and buttons, the skill cards bottom-right and the cost gauge "
    "exactly where they are. ADD only these three things: "
    "(1) at the top centre, under the top bar, a large boss HP bar in Blue Archive style: a slanted dark navy name plate on "
    "the left with a level number and the boss name, a long thick slanted HP bar with a bright red fill over a darker layer "
    "showing the next bar, thin white rim, and at its right end a bar-count badge like 'x12'; a small row of square "
    "buff/debuff icons under the bar's left end. "
    "(2) on the monster being hit, Blue Archive style hit effects: thin white-and-cyan expanding impact rings with small "
    "sparks, and damage numbers in bold italic white with a dark outline. "
    "(3) a small kill counter near the top-left under the top bar: a skull-free, office-themed icon with a number. "
    "Clean flat vector UI, crisp edges, the same colour language as the existing HUD (navy, cyan, white, a little gold). "
    "Readable English placeholder text is fine; no Japanese. Do not change the art style of the characters.")


def read_key():
    for path in ENV_FILES:
        if path and os.path.exists(path):
            with open(path, encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if line.startswith("GEMINI_API_KEY="):
                        return line.split("=", 1)[1].strip().strip('"').strip("'")
    sys.exit("GEMINI_API_KEY not found (set ENV_FILE)")


def generate(key, capture, n):
    with open(capture, "rb") as f:
        ref = base64.b64encode(f.read()).decode("ascii")
    body = json.dumps({
        "contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": ref}}, {"text": PROMPT}]}],
        "generationConfig": {"responseModalities": ["IMAGE"], "temperature": 0.8, "imageConfig": {"aspectRatio": "21:9"}},
    }).encode("utf-8")
    for model in MODELS:
        req = urllib.request.Request(
            f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent",
            data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=300) as r:
                data = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            print(f"  #{n} {model}: HTTP {e.code} {e.read().decode('utf-8', 'replace')[:300]}")
            continue
        for cand in data.get("candidates", []):
            for part in cand.get("content", {}).get("parts", []):
                inline = part.get("inlineData") or part.get("inline_data")
                if inline and inline.get("data"):
                    os.makedirs(OUT, exist_ok=True)
                    path = os.path.join(OUT, f"hudref_{n}.png")
                    with open(path, "wb") as f:
                        f.write(base64.b64decode(inline["data"]))
                    print(f"  #{n}: saved with {model} -> {path}")
                    return True
        print(f"  #{n} {model}: no image ({json.dumps(data)[:200]})")
    return False


if __name__ == "__main__":
    key = read_key()
    args = [a for a in sys.argv[1:]]
    capture = args[0] if args else os.path.join(HERE, "out", "shots", "07-BattleHud.png")
    count = int(args[1]) if len(args) > 1 else 3
    ok = sum(generate(key, capture, i) for i in range(count))
    print(f"{ok}/{count} layout references")
