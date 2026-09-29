# -*- coding: utf-8 -*-
"""
2D SD (chibi) art FROM EACH CHARACTER'S OWN ILLUSTRATION, on Hugging Face image-edit Spaces:
the standing illustration goes in, the same character comes back as a Blue Archive-style SD
chibi (about 2.4 heads, full body, front three-quarter, flat white background) — same hair,
eyes, outfit and colours, because it is an edit of the picture rather than a text prompt.

    python tools/sd2d_hf.py cfo ceo            → ArtSource/SD_v2/raw/<id>.png
    python tools/sd2d_hf.py --all
Then the web repo's cutout + uniform (SD head band) finish it:
    python ../excel-heros/tools/cutout_ai.py ArtSource/SD_v2/raw/*.png --out ArtSource/SD_v2/alpha  (SD=1)

Spaces tried in order: multimodalart/Qwen-Image-Edit-Fast, Qwen/Qwen-Image-Edit,
black-forest-labs/FLUX.1-Kontext-Dev. ZeroGPU quota: the six tokens rotate, then anonymous.
SECURITY: the tokens are read from the env file BY NAME (HUGGING_FACE_API_KEY*) into memory only.
"""
import os, shutil, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mon3d_trellis import tokens

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Standing")
OUT = os.path.join(ROOT, "ArtSource", "SD_v2", "raw")
PROMPT = ("Redraw this exact character as a cute Blue Archive style SD chibi figure: about 2.4 heads tall with a big head and "
          "a small body, full body from head to shoes, standing in a relaxed three-quarter pose facing the viewer, same face, "
          "same hairstyle and hair colour, same eye colour, same outfit design and colours, same accessories. Clean anime cel "
          "shading, crisp line art, plain flat pure white background, no shadow, no text, single character.")
SPACES = [("Qwen/Qwen-Image-Edit", "qwen", 20), ("black-forest-labs/FLUX.1-Kontext-Dev", "kontext", 24), ("multimodalart/Qwen-Image-Edit-Fast", "qwen", 8)]


def hints(hid):
    """What the edit models tend to drop: the glasses and the eye colour, stated per character."""
    import json, colorsys
    try:
        d = json.load(open(os.path.join(ROOT, "Assets", "ExcelHeroes", "Resources", "Data", "sdspec.json"), encoding="utf-8"))
        it = d.get("items", d); row = next((x for x in (it if isinstance(it, list) else [dict(v, id=k) for k, v in it.items()]) if x.get("id") == hid), {})
    except Exception: row = {}
    out = []
    if row.get("sunglasses"): out.append("She wears sunglasses, keep them.")
    elif row.get("glasses"): out.append(f"She wears {row.get('glassesStyle') or ''} glasses, KEEP THE GLASSES.")
    else: out.append("No glasses.")
    e = (row.get("eye") or "").lstrip("#")
    if len(e) == 6:
        r, g, b = (int(e[i:i + 2], 16) / 255 for i in (0, 2, 4)); h, l, s = colorsys.rgb_to_hls(r, g, b)
        name = "grey" if s < 0.18 else ["red", "golden amber", "golden", "yellow", "green", "teal", "cyan", "blue", "blue", "violet", "purple", "pink"][int(h * 12) % 12]
        out.append(f"Keep her {name} eyes.")
    return " ".join(out)


def flat(src):
    from PIL import Image
    im = Image.open(src).convert("RGBA")
    bg = Image.new("RGBA", im.size, (255, 255, 255, 255)); bg.alpha_composite(im)
    p = os.path.join(OUT, "_in.png"); bg.convert("RGB").save(p); return p


def run(ids, seed=7):
    from gradio_client import Client, handle_file
    os.makedirs(OUT, exist_ok=True)
    pools = tokens() + [None]
    dead = set()
    for hid in ids:
        dst = os.path.join(OUT, hid + ".png")
        if os.path.exists(dst) and "--force" not in sys.argv: print("  skip", hid); continue
        inp = flat(os.path.join(SRC, hid + ".png"))
        done = False
        for sp, kind, steps in SPACES:
            for pi, tok in enumerate(pools):
                if (sp, pi) in dead: continue
                try:
                    c = Client(sp, token=tok, verbose=False) if tok else Client(sp, verbose=False)
                    if kind == "qwen":
                        r = c.predict(image=handle_file(inp), prompt=PROMPT + ' ' + hints(hid), seed=seed, randomize_seed=False, true_guidance_scale=1.0 if "Fast" in sp else 4.0,
                                      num_inference_steps=steps, rewrite_prompt=False, api_name="/infer")
                    else:
                        r = c.predict(input_image=handle_file(inp), prompt=PROMPT + ' ' + hints(hid), seed=seed, randomize_seed=False, guidance_scale=2.5, steps=steps, api_name="/infer")
                    path = r[0] if isinstance(r, (list, tuple)) else r
                    if isinstance(path, dict): path = path.get("path") or path.get("url")
                    if isinstance(path, (list, tuple)): path = path[0]
                    if isinstance(path, dict): path = path.get("image", path).get("path") if isinstance(path.get("image", path), dict) else path.get("path")
                    shutil.copy(path, dst); print(f"  ok {hid} ({sp.split('/')[-1]}, pool {pi})"); done = True; break
                except Exception as e:
                    msg = str(e)
                    if "quota" in msg.lower() or "exceeded" in msg.lower(): dead.add((sp, pi)); continue
                    print(f"  {hid} {sp.split('/')[-1]} pool {pi}: {msg[:90]}"); dead.add((sp, pi))
            if done: break
        if not done: print("  FAILED", hid)


if __name__ == "__main__":
    import json
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if "--all" in sys.argv:
        args = [h["id"] for h in json.load(open(os.path.join(ROOT, "Assets", "ExcelHeroes", "Resources", "Data", "heroes.json"), encoding="utf-8"))["items"]]
    run(args)
