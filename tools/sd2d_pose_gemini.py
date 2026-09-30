# -*- coding: utf-8 -*-
"""
Expressive SD poses (the user's round of 2026-09-30: "SD poses, face size, quality, unity — at Blue
Archive's SD level"; the cross-check: "a simple line-up, no varied poses, expressions or props"). The
neutral SDs (Resources/Art/SD, drawn from the 3D model — tools/sd2d_from_model_gemini.py) stay the
source for the EX cards and the 3D pipeline; this draws each member ONCE MORE, from that very SD (the
same design, head size and drawing), in a pose and face that fit their persona (sdspec "persona") —
for the recruit line-up, the pull reveal and the notice board. Only our own art goes in.

    python tools/sd2d_pose_gemini.py [ids...] [--missing]    → ArtSource/SD_pose/raw/<id>.png
    then: cutout → uniform.py sd → Resources/Art/SDPose/<id>.png
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, io, json, os, sys, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
from PIL import Image

SD = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "SD")
SPEC = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Data", "sdspec.json")
OUT = os.path.join(g.ROOT, "ArtSource", "SD_pose", "raw")
POSES = {
    "confident": "one hand on the hip, the other giving a thumbs-up, a bold grin, chest out",
    "elegant":   "a graceful half turn, one hand lightly raised near the chin, a calm closed-mouth smile, one foot crossed in front",
    "shy":       "both hands held together in front of the chest, shoulders raised, a small embarrassed smile with a big blush, looking slightly aside",
    "energetic": "jumping up with one fist raised high, mouth open in a cheer, one knee bent up, hair flying",
    "lazy":      "a big yawn with one hand over the mouth and the other scratching the head, half-closed eyes, slouching",
    "stern":     "arms firmly crossed, a serious look with a slight frown, standing square",
    "nerdy":     "pushing up glasses or pointing one finger up as if explaining, a pleased knowing smile",
    "cool":      "hands in the pockets or one hand raised in a lazy wave, a cool half smile, weight on one leg",
    "cheerful":  "waving happily with one hand high, the other on the cheek, a wide open-mouth smile, eyes curved in joy",
    "caring":    "both hands cupped in front as if offering a warm cup of coffee, a gentle soft smile, head tilted",
    "executive": "one hand raised in a composed 'leave it to me' gesture, the other on the hip, a confident small smile",
    "playful":   "a wink with a peace sign next to the eye, tongue slightly out, leaning to one side",
    "":          "a cheerful wave with one hand, a friendly open smile, a slight lean",
}
ASK = ("This is one of our game's 2D SD (chibi) characters. Redraw THE SAME CHARACTER in the same drawing style, line quality, colours "
       "and head size (keep exactly the hairstyle, face, eye colour, glasses if any, outfit, legwear and shoes — nothing added or removed), "
       "but in this lively pose and expression: {pose}. Full body from the top of the hair to the shoes, both feet (or the landing foot) "
       "inside the image, the pose readable as a clear silhouette. Plain flat pure white background, no shadow, no text, no effects, one character only.")


def part(path):
    im = Image.open(path).convert("RGBA"); bg = Image.new("RGBA", im.size, (255, 255, 255, 255)); bg.alpha_composite(im)
    buf = io.BytesIO(); bg.convert("RGB").save(buf, "PNG")
    return {"inlineData": {"mimeType": "image/png", "data": base64.b64encode(buf.getvalue()).decode("ascii")}}


def make(key, hid, persona):
    src = os.path.join(SD, hid + ".png")
    if not os.path.exists(src): print("  skip", hid); return False
    body = json.dumps({"contents": [{"parts": [part(src), {"text": ASK.format(pose=POSES.get(persona, POSES[""]))}]}],
                       "generationConfig": {"responseModalities": ["IMAGE"], "temperature": 0.5, "imageConfig": {"aspectRatio": "4:5"}}}).encode("utf-8")
    for model in g.MODELS:
        req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent", data=body, method="POST",
                                     headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=300) as r: d = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e: print(f"  {hid} {model}: HTTP {e.code}"); continue
        for c in d.get("candidates", []):
            for p in c.get("content", {}).get("parts", []):
                inline = p.get("inlineData") or p.get("inline_data")
                if inline and inline.get("data"):
                    open(os.path.join(OUT, hid + ".png"), "wb").write(base64.b64decode(inline["data"])); print(f"  ok {hid} ({persona or '-'})"); return True
    print("  FAILED", hid); return False


if __name__ == "__main__":
    key = g.read_key(); os.makedirs(OUT, exist_ok=True)
    spec = {r["id"]: r.get("persona", "") for r in json.load(open(SPEC, encoding="utf-8"))["items"]}
    ids = [a for a in sys.argv[1:] if not a.startswith("--")] or sorted(f[:-4] for f in os.listdir(SD) if f.endswith(".png"))
    if "--missing" in sys.argv: ids = [i for i in ids if not os.path.exists(os.path.join(OUT, i + ".png"))]
    import concurrent.futures as cf
    with cf.ThreadPoolExecutor(4) as ex: list(ex.map(lambda h: make(key, h, spec.get(h, "")), ids))
