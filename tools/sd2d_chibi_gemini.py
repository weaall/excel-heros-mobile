# -*- coding: utf-8 -*-
"""
The 2D SD, second step: Hugging Face (tools/sd2d_hf.py, Qwen-Image-Edit) redraws each CURRENT
illustration as a clean full-body figure in her exact outfit — faithful, but about 4.5 heads tall,
not an SD. Gemini then takes that figure (who she is, what she wears) and an SD of the same set
(her previous 2D SD, or cso's for the ten who never had one) for the proportions and finish, and
draws her at 2.4 heads.

    python tools/sd2d_chibi_gemini.py              # every hero
    python tools/sd2d_chibi_gemini.py cso cfo      # some
  in:  ArtSource/SD_v2/raw_qwen1/<id>.png (else the standing illustration)
  out: ArtSource/SD_v2/raw/<id>.png — then cutout (SD=1) and uniform.py sd as before.
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, io, json, os, sys, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
from PIL import Image

SV2 = os.path.join(g.ROOT, "ArtSource", "SD_v2")
QWEN, OUT = os.path.join(SV2, "raw_qwen1"), os.path.join(SV2, "raw")
STAND = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Standing")
OLD = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "ArtSource", "SD")
ASK = ("Image 1 shows a character (her face, hair, eyes, glasses, outfit and colours). Image 2 is a super-deformed chibi from the same "
       "game, shown ONLY for its proportions and drawing style. Draw the character of IMAGE 1 as a chibi figure with EXACTLY image 2's "
       "proportions and finish: about 2.4 heads tall, the head as big as the whole rest of the body, short limbs, small hands and feet, "
       "clean anime cel shading and crisp line art, full body from the top of the hair to the shoes, a relaxed three-quarter standing "
       "pose facing the viewer. Keep from image 1: the face, hairstyle, hair colour, eye colour, glasses or none, the complete outfit "
       "design and colours (the same garments, lengths, tights, shoes), accessories and the item she holds. Take NOTHING of image 2's "
       "face, hair, clothes or colours. Plain flat pure white background, no shadow, no text, one character.")


def part(path):
    im = Image.open(path).convert("RGBA"); bg = Image.new("RGBA", im.size, (255, 255, 255, 255)); bg.alpha_composite(im)
    buf = io.BytesIO(); bg.convert("RGB").save(buf, "PNG")
    return {"inlineData": {"mimeType": "image/png", "data": base64.b64encode(buf.getvalue()).decode("ascii")}}


def make(key, hid):
    src = os.path.join(QWEN, hid + ".png")
    if not os.path.exists(src): src = os.path.join(STAND, hid + ".png")
    ref = os.path.join(OLD, hid + ".png")
    if not os.path.exists(ref): ref = os.path.join(OLD, "cso.png")
    body = json.dumps({"contents": [{"parts": [part(src), part(ref), {"text": ASK}]}],
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
                    open(os.path.join(OUT, hid + ".png"), "wb").write(base64.b64decode(inline["data"])); print(f"  ok {hid} ({model})"); return True
    print("  FAILED", hid); return False


if __name__ == "__main__":
    key = g.read_key(); os.makedirs(OUT, exist_ok=True)
    ids = [a for a in sys.argv[1:] if not a.startswith("--")] or [h["id"] for h in json.load(open(os.path.join(g.DATA, "heroes.json"), encoding="utf-8"))["items"]]
    if "--missing" in sys.argv: ids = [i for i in ids if not os.path.exists(os.path.join(OUT, i + ".png"))]
    import concurrent.futures as cf
    with cf.ThreadPoolExecutor(4) as ex: list(ex.map(lambda h: make(key, h), ids))
