# -*- coding: utf-8 -*-
"""
Triptych sheets for tools/sd_consistency_gemini.py: per character a column of the illustration, the
2D SD and the 3D render (tools/out/all3d/strip_<id>.png), eleven columns a sheet.

    python tools/trip_sheets.py [--sd DIR] [--out DIR] [ids...]
      --sd   the 2D SD folder (default Resources/Art/SD; ArtSource/SD_v3/uniform for a candidate set)
SECURITY: none (local files only).
"""
import json, os, sys
from PIL import Image, ImageDraw

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
ART = os.path.join(ROOT, "Assets", "ExcelHeroes", "Resources", "Art")


def fit(im, w, h, bg=(236, 242, 250)):
    im = im.convert("RGBA"); bb = im.getchannel("A").getbbox(); im = im.crop(bb) if bb else im
    im.thumbnail((w, h)); c = Image.new("RGBA", (w, h), bg + (255,)); c.alpha_composite(im, ((w - im.width) // 2, h - im.height)); return c.convert("RGB")


def main(argv):
    sd = os.path.join(ART, "SD"); out = os.path.join(ROOT, "tools", "out", "trip")
    ids = []
    it = iter(argv)
    for a in it:
        if a == "--sd": sd = next(it)
        elif a == "--out": out = next(it)
        else: ids.append(a)
    ids = ids or [h["id"] for h in json.load(open(os.path.join(ROOT, "Assets", "ExcelHeroes", "Resources", "Data", "heroes.json"), encoding="utf-8"))["items"]]
    os.makedirs(out, exist_ok=True)
    for f in os.listdir(out):
        if f.startswith("trip_"): os.remove(os.path.join(out, f))
    W, H = 200, 300
    for k in range(0, len(ids), 11):
        grp = ids[k:k + 11]
        sheet = Image.new("RGB", (len(grp) * W, 3 * H + 30), "white"); d = ImageDraw.Draw(sheet)
        for i, hid in enumerate(grp):
            st = os.path.join(ART, "Standing", hid + ".png")
            a = fit(Image.open(st if os.path.exists(st) else os.path.join(ART, "Cards", hid + ".png")), W, H)
            b = fit(Image.open(os.path.join(sd, hid + ".png")), W, H)
            r3 = os.path.join(os.environ.get("ALL3D") or os.path.join(ROOT, "tools", "out", "all3d"), "strip_" + hid + ".png")
            c = Image.open(r3).convert("RGB").resize((W, H)) if os.path.exists(r3) else Image.new("RGB", (W, H), (106, 156, 214))
            sheet.paste(a, (i * W, 0)); sheet.paste(b, (i * W, H)); sheet.paste(c, (i * W, 2 * H)); d.text((i * W + 4, 3 * H + 6), hid, fill="black")
        sheet.save(os.path.join(out, f"trip_{k // 11}.png"))
    print("sheets", (len(ids) + 10) // 11, "->", out)


if __name__ == "__main__":
    main(sys.argv[1:])
