# -*- coding: utf-8 -*-
"""
Battle stage backdrops with Gemini (Nano Banana): an open-plan office seen from the reference's
high three-quarter battle camera, the middle of the floor left empty for the fight. One per mood
(day, evening, night — BattleWorld switches at Phase 20 and 40).

    python tools/gen_bg_gemini.py            # all three
    python tools/gen_bg_gemini.py night      # just one

SECURITY: the key is read from the env file by NAME and sent in a header. It is never printed,
logged or written anywhere else.
"""
import base64, json, os, sys, urllib.request, urllib.error

ENV_FILE = os.environ.get("ENV_FILE", r"C:\Users\user\Desktop\mindsai_weaall.env")
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "ExcelHeroes", "Resources", "Art", "Battle")
MODELS = ["gemini-2.5-flash-image", "gemini-2.5-flash-image-preview", "gemini-3-pro-image-preview"]

BASE = ("Background art for the battle stage of an anime mobile gacha game, in the visual style of Blue Archive's "
        "battle maps: clean stylised 3D render with soft cel shading, bright pastel palette, crisp simple shapes. "
        "A modern open-plan office floor seen from a high three-quarter camera looking down at about 30 degrees, "
        "wide landscape framing. The floor is light grey carpet tiles in a faint spreadsheet grid. "
        "The whole middle band of the floor, from the left edge to the right edge, is completely EMPTY and clear, "
        "a wide open lane for characters to stand and fight. Along the back: a row of white desks with monitors, "
        "office chairs, glass partitions, potted plants, filing cabinets, and a wall of large windows. "
        "Along the very bottom edge in the foreground: a few low cabinets and a printer, cut off by the frame. "
        "No people, no characters, no animals, no text, no letters, no logos, no user interface. ")
MOODS = {
    "day": "Bright morning daylight through the windows, blue sky and a white city skyline outside, soft shadows.",
    "evening": "Warm golden evening light through the windows, orange and pink sunset sky over the city, long soft shadows.",
    "night": "Late night overtime: dark navy city with lit windows outside, the office lit by ceiling lights and glowing cyan monitors.",
}

def read_key():
    with open(ENV_FILE, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line.startswith("GEMINI_API_KEY="):
                return line.split("=", 1)[1].strip().strip('"').strip("'")
    sys.exit("GEMINI_API_KEY not found in the env file")

def generate(name, key):
    body = json.dumps({
        "contents": [{"parts": [{"text": BASE + MOODS[name]}]}],
        "generationConfig": {"responseModalities": ["IMAGE"], "imageConfig": {"aspectRatio": "16:9"}},
    }).encode("utf-8")
    for model in MODELS:
        req = urllib.request.Request(
            f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent",
            data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=240) as r:
                data = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            print(f"  {name} {model}: HTTP {e.code} {e.read().decode('utf-8', 'replace')[:300]}")
            continue
        for cand in data.get("candidates", []):
            for part in cand.get("content", {}).get("parts", []):
                inline = part.get("inlineData") or part.get("inline_data")
                if inline and inline.get("data"):
                    os.makedirs(OUT, exist_ok=True)
                    path = os.path.join(OUT, f"office_{name}.png")
                    with open(path, "wb") as f:
                        f.write(base64.b64decode(inline["data"]))
                    print(f"  {name}: saved with {model}")
                    return True
        print(f"  {name} {model}: no image ({json.dumps(data)[:200]})")
    return False

if __name__ == "__main__":
    key = read_key()
    names = sys.argv[1:] or list(MOODS)
    ok = sum(generate(n, key) for n in names)
    print(f"{ok}/{len(names)} backdrops")
