# -*- coding: utf-8 -*-
"""
Where each standing illustration's head is, so portraits crop onto the face instead of onto a
fixed percentage of the canvas (which cut crowns off and missed the face on wide poses).

    python tools/face_boxes.py

Reads Assets/ExcelHeroes/Resources/Art/Standing/*.png, runs the anime head detector
(deepghs imgutils, local), writes Resources/Data/faces.json:
    { "items": [ { "id": "cfo", "x0": .., "y0": .., "x1": .., "y1": .. }, ... ] }
with the box as fractions of the canvas, y measured from the TOP.
"""
import glob, json, os
from PIL import Image
from imgutils.detect import detect_heads

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.join(HERE, '..', 'Assets', 'ExcelHeroes', 'Resources', 'Art', 'Standing')   # the art the game shows (ArtSource went stale when illustrations were replaced)
OUT = os.path.join(HERE, '..', 'Assets', 'ExcelHeroes', 'Resources', 'Data', 'faces.json')

items = []
for f in sorted(glob.glob(os.path.join(ART, '*.png'))):
    im = Image.open(f).convert('RGBA')
    flat = Image.new('RGB', im.size, (255, 255, 255))
    flat.paste(im, (0, 0), im)
    w, h = im.size
    found = sorted([d for d in detect_heads(flat) if d[2] > 0.35], key=lambda d: -d[2])
    top = [d for d in found if d[0][1] < h * 0.5] or found
    if not top:
        print(f'  {os.path.basename(f)}: no head found')
        continue
    (x0, y0, x1, y1), _, score = top[0]
    items.append({'id': os.path.splitext(os.path.basename(f))[0],
                  'x0': round(x0 / w, 4), 'y0': round(y0 / h, 4), 'x1': round(x1 / w, 4), 'y1': round(y1 / h, 4)})
    print(f'  {items[-1]["id"]:16s} head {x0},{y0} - {x1},{y1}  ({score:.2f})')

with open(OUT, 'w', encoding='utf-8') as fh:
    json.dump({'items': items}, fh, indent=1)
print(f'{len(items)} faces -> {OUT}')
