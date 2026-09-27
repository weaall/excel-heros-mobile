# -*- coding: utf-8 -*-
"""
One command from a character's illustration colours to a reviewable model sheet:

    python tools/sd_review.py                # every character in the spec
    python tools/sd_review.py intern cfo     # some

Steps: sample_looks.py (colours from ArtSource/SD, if the cut-out exists) → sd_spec.py (the spec
row, hand edits kept) → Unity SdBasePreview.Sheet (one PNG per character: 4 views · 4 expressions
· idle / victory / attack / walk) → tools/out/sd3d/review.png (a contact sheet of them all).
Look at review.png; fix a row with `python tools/sd_spec.py --set <id> fringe=2 idle=3 ...`; re-run.
"""
import glob, json, os, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
UNITY = os.environ.get("UNITY", r"C:\Program Files\Unity\Hub\Editor\6000.0.82f1\Editor\Unity.exe")
OUT = os.path.join(HERE, "out", "sd3d")


def main(ids):
    subprocess.run([sys.executable, os.path.join(HERE, "sample_looks.py")], check=False)
    subprocess.run([sys.executable, os.path.join(HERE, "sd_spec.py")] + ids, check=True)
    if not ids:
        spec = json.load(open(os.path.join(ROOT, "Assets", "ExcelHeroes", "Resources", "Data", "sdspec.json"), encoding="utf-8"))
        ids = [r["id"] for r in spec["items"]]
    env = dict(os.environ, SD_IDS=",".join(ids), SD_PREVIEW_OUT=OUT)
    log = os.path.join(OUT, "sheet.log")
    os.makedirs(OUT, exist_ok=True)
    subprocess.run([UNITY, "-batchmode", "-quit", "-projectPath", ROOT, "-executeMethod", "ExcelHeroes.EditorTools.SdBasePreview.Sheet", "-logFile", log], check=False, env=env)
    from PIL import Image
    sheets = sorted(glob.glob(os.path.join(OUT, "sheets", "*.png")))
    cols = 4; tw, th = 440, 450
    contact = Image.new("RGB", (tw * cols, th * ((len(sheets) + cols - 1) // cols)), (40, 40, 40))
    for i, p in enumerate(sheets):
        contact.paste(Image.open(p).resize((tw, th), Image.LANCZOS), ((i % cols) * tw, (i // cols) * th))
    contact.save(os.path.join(OUT, "review.png"))
    print(f"{len(sheets)} sheets → {os.path.join(OUT, 'review.png')}")


if __name__ == "__main__":
    main(sys.argv[1:])
