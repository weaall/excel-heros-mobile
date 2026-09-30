# -*- coding: utf-8 -*-
"""
The prologue's seven scenes, drawn again at the screen's own shape (the user, 2026-09-30: "regenerate, re-lay
out, no broken resolution — illustrations sized to fit the screen"). The old set was 912x624 (3:2) stretched
over a 2400x1080 (20:9) screen: upscaled ~2.6x, then cropped top and bottom, and the character in scene one
was not our intern. Now each scene is generated wide (21:9) at the image model's large size, with the
intern's own standing illustration (and a few of the cast) as the character reference, composed for the
page: the subject in the upper two thirds, the lower-left quarter quiet for the dialogue box, the top
corners quiet for the chapter label and the AUTO / skip pills.

    python tools/prologue_gemini.py [ids...]      → Assets/ExcelHeroes/Resources/Art/Story/<id>.png (1080 tall)
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, io, json, os, sys, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
from PIL import Image

ART = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art")
OUT = os.path.join(ART, "Story")
KEEP = os.path.join(g.ROOT, "ArtSource", "Story_v1")   # the old set, kept
CAST = ["cfo", "ceo", "vlookup", "cco"]
STYLE = ("Polished Japanese mobile gacha game key visual / story CG: clean line art, soft cel shading with gentle gradients, "
         "bright luminous colours, cinematic lighting, detailed but clean background, anime style matching the reference "
         "illustrations exactly. Wide cinematic landscape frame. COMPOSITION FOR THE GAME'S STORY PAGE: keep the main subject "
         "in the upper two thirds and the centre-right; keep the bottom-left quarter calm and uncluttered (a dialogue box is "
         "laid over it) and the two top corners free of important detail. No text, no letters, no UI, no speech bubbles, no "
         "watermark, no border.")
SCENES = {
    "deadline": ("intern", "Late night before the quarterly deadline in a bright 3rd-floor open-plan office: the young intern from the "
                 "reference (same face, hair and outfit, office lanyard) sits at a desk between tall stacks of paper, tired but "
                 "focused, the glow of a spreadsheet on the monitor lighting the face; coworkers' desks around with warm lamps; a "
                 "huge window behind shows the city skyline at night."),
    "meteor": ("intern", "The same office at 11:47 pm seen from inside: through the tall window a brilliant meteor streaks across the "
               "night sky over the city, lighting the room blue-white; the intern from the reference turns from the desk toward the "
               "window, eyes wide; other workers still hunched over keyboards, not looking."),
    "impact": ("", "A wide aerial night view of a modern city centre at the moment of impact: a meteor has struck downtown, a huge ring "
               "shock wave of light spreading between the skyscrapers; along its path traffic lights, billboards and building "
               "windows glitch into red error blocks and broken grid cells, like a corrupted spreadsheet over the city."),
    "errors": ("", "A city street at night after the impact: cute but menacing error creatures crawl out of cracks of red glitching "
               "spreadsheet cells in the asphalt — living '#REF!' blobs, tangled circular-arrow monsters, blank torn sheets with "
               "eyes; a traffic light and a bank sign turn red and corrupted as they touch them; people fleeing in the distance. Absolutely no dialogue box, no subtitles, no captions, no words except the '#REF!' on the creatures."),
    "sheet": ("intern", "Dawn after the chaos, dust settling on a quiet street: the intern from the reference stands, seen from a "
              "three-quarter back view looking over the shoulder, and a large translucent glowing spreadsheet (a grid of softly lit "
              "cells) floats behind the upper back, leaning out past one shoulder like a halo; other survivors in the background "
              "also have faint sheets behind them; warm sunrise light."),
    "awaken": ("intern", "A heroic awakening moment: the intern from the reference in a dynamic confident pose, the glowing spreadsheet "
               "behind the shoulder blazing with filled cells; around them office skills turn into weapons of light — keyboard "
               "shortcut symbols forming a sword, a report turning into a shield, an approval stamp glowing like a seal; blue and "
               "gold light, energy particles, office rooftop at dawn."),
    "roster": ("intern+cast", "The company lobby in the morning: the intern from image 1 holds a single sheet of paper (a staff roster) "
               "with a surprised, slightly overwhelmed face; behind them, a lineup of the other employees from the other reference "
               "images (keep each one's face, hair and outfit) stand confidently, ready to fight; bright hopeful morning light "
               "through glass walls. The INTERN (the young man in the light mint jacket from image 1) is in the FRONT and CENTRE holding the "
               "paper; the others stand behind him, smaller."),
}


def part(path):
    im = Image.open(path).convert("RGBA"); bg = Image.new("RGBA", im.size, (255, 255, 255, 255)); bg.alpha_composite(im)
    buf = io.BytesIO(); bg.convert("RGB").save(buf, "PNG")
    return {"inlineData": {"mimeType": "image/png", "data": base64.b64encode(buf.getvalue()).decode("ascii")}}


def make(key, sid):
    who, scene = SCENES[sid]
    parts = []
    if who.startswith("intern"): parts.append(part(os.path.join(ART, "Standing", "intern.png")))
    if who.endswith("cast"): parts += [part(os.path.join(ART, "Standing", c + ".png")) for c in CAST]
    parts.append({"text": ("The reference image(s) show characters from our game; draw them exactly as they are. " if parts else "") + scene + " " + STYLE})
    for size in ("2K", None):
        cfg = {"responseModalities": ["IMAGE"], "temperature": 0.6, "imageConfig": {"aspectRatio": "21:9"}}
        if size: cfg["imageConfig"]["imageSize"] = size
        body = json.dumps({"contents": [{"parts": parts}], "generationConfig": cfg}).encode("utf-8")
        for model in g.MODELS:
            req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent", data=body, method="POST",
                                         headers={"Content-Type": "application/json", "x-goog-api-key": key})
            try:
                with urllib.request.urlopen(req, timeout=400) as r: d = json.loads(r.read().decode("utf-8"))
            except urllib.error.HTTPError as e: print(f"  {sid} {model} {size}: HTTP {e.code}"); continue
            for c in d.get("candidates", []):
                for p in c.get("content", {}).get("parts", []):
                    inline = p.get("inlineData") or p.get("inline_data")
                    if inline and inline.get("data"):
                        im = Image.open(io.BytesIO(base64.b64decode(inline["data"]))).convert("RGB")
                        src = im.size
                        # to cover the 2400x1080 screen: 1080 tall (a 21:9 frame is then ~2546 wide — the page crops a sliver
                        # off the sides and never upscales)
                        im = im.resize((round(1080 * im.width / im.height), 1080), Image.LANCZOS)
                        im.save(os.path.join(OUT, sid + ".png")); print(f"  ok {sid} {src} -> {im.size} ({model} {size})"); return True
    print("  FAILED", sid); return False


if __name__ == "__main__":
    key = g.read_key(); os.makedirs(KEEP, exist_ok=True)
    ids = [a for a in sys.argv[1:]] or list(SCENES)
    for i in ids:
        old = os.path.join(OUT, i + ".png")
        if os.path.exists(old) and not os.path.exists(os.path.join(KEEP, i + ".png")):
            import shutil; shutil.copy(old, os.path.join(KEEP, i + ".png"))
    import concurrent.futures as cf
    with cf.ThreadPoolExecutor(4) as ex: list(ex.map(lambda s: make(key, s), ids))
