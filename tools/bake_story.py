# -*- coding: utf-8 -*-
"""
The prologue scenes with the intern's own sheet painted INTO them (not floated over them by the
game): cut the character out of the scene (rembg isnet-anime), put the sheet between the
background and the character — behind the upper back, leaning out past a shoulder — glowing,
and let its light spill onto the figure and the air around it.

    python tools/bake_story.py        # ArtSource/Story -> Resources/Art/Story (+ web assets/story/*.webp)
"""
import os, sys
import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bake_sheet as B

ROOT = os.path.join(HERE, "..", "Assets", "ExcelHeroes")
SRC = os.path.join(ROOT, "ArtSource", "Story")
DST = os.path.join(ROOT, "Resources", "Art", "Story")
WEB = r"C:\Users\user\excel-heros\assets\story"
SCENES = {"sheet": 1.9, "awaken": 1.6, "roster": 1.6}     # sheet width in head widths


def main():
    from rembg import new_session, remove
    from imgutils.detect import detect_heads
    session = new_session("isnet-anime")
    spec = B.Spec("intern", B.heroes()["intern"])
    for sid, scale in SCENES.items():
        path = os.path.join(SRC, sid + ".png")
        if not os.path.exists(path): print(f"  {sid}: missing"); continue
        scene = Image.open(path).convert("RGBA")
        cut = remove(scene, session=session)
        heads = sorted([d for d in detect_heads(scene.convert("RGB")) if d[2] > 0.3], key=lambda d: -d[2])
        if not heads: print(f"  {sid}: no head"); continue
        (x0, y0, x1, y1), _, _ = heads[0]
        hw, hh = x1 - x0, y1 - y0
        # the side with more room gets the sheet
        side = 1 if (x0 + x1) / 2 < scene.width / 2 else -1
        spec.left = side < 0
        w = int(hw * scale)
        strip = B.perspective(B.strip(spec, w), side).rotate(side * 16, resample=Image.BICUBIC, expand=True)
        # brighter glow for a scene: the sheet is the light source of the moment
        glow = strip.split()[3].filter(ImageFilter.GaussianBlur(w * 0.06))
        halo = Image.new("RGBA", strip.size, spec.frame + (0,)); halo.putalpha(glow.point(lambda a: int(a * 0.7)))
        layer = Image.new("RGBA", scene.size, (0, 0, 0, 0))
        cx = (x0 + x1) / 2 + side * hw * 0.62; cy = y1 - hh * 0.05
        pos = (int(cx - strip.width / 2), int(cy - strip.height / 2))
        layer.alpha_composite(halo, (max(0, pos[0]), max(0, pos[1])), (max(0, -pos[0]), max(0, -pos[1])))
        layer.alpha_composite(strip, (max(0, pos[0]), max(0, pos[1])), (max(0, -pos[0]), max(0, -pos[1])))
        out = Image.alpha_composite(scene, layer)          # sheet over the background ...
        out = Image.alpha_composite(out, cut)              # ... and the character back on top
        # light spill onto the figure's edge nearest the sheet, and a faint cyan bloom in the air
        la = np.asarray(layer.split()[3].filter(ImageFilter.GaussianBlur(w * 0.12))).astype(np.float32) / 255
        ca = np.asarray(cut.split()[3]).astype(np.float32) / 255
        o = np.asarray(out).astype(np.float32)
        k = (la * (0.35 * ca + 0.12))[..., None]
        o[..., :3] = o[..., :3] * (1 - k) + np.array(spec.frame, np.float32) * k
        res = Image.fromarray(o.clip(0, 255).astype(np.uint8), "RGBA").convert("RGB")
        res.save(os.path.join(DST, sid + ".png"))
        if os.path.isdir(WEB):
            res.resize((912, 624), Image.LANCZOS).save(os.path.join(WEB, sid + ".webp"), quality=85)
        print(f"  {sid}: head {x0},{y0} side {side}")


if __name__ == "__main__":
    main()
