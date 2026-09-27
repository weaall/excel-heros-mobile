# -*- coding: utf-8 -*-
"""
Puts the v2 card illustrations (tools/gen_cards_v2.mjs → ArtSource/Cards_v2/<id>.png, 832×1216,
git-ignored, mirrored to Drive generated/cards_v2) into the game: Resources/Art/Cards/<id>.png at
512×748 (both ÷1.625, same aspect, so DXT's 4×4 blocks apply). The existing .meta is kept, so
the import settings and GUID stay. Skins (<id>__casual / __formal) are not touched.

    python tools/cards_v2_import.py            # every v2 original
    python tools/cards_v2_import.py ceo cfo    # just these
"""
import os, sys
from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "ArtSource", "Cards_v2")
DST = os.path.join(ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Cards")

ids = sys.argv[1:] or sorted(f[:-4] for f in os.listdir(SRC) if f.endswith(".png"))
for i in ids:
    src = os.path.join(SRC, i + ".png")
    if not os.path.exists(src): print(f"  missing {i}"); continue
    im = Image.open(src).convert("RGB")
    # to the card's aspect by cropping, never stretching (Gemini answers 2:3, the card is 512:748);
    # the spare height comes off the bottom — the head is at the top and must keep its margin
    w, h = im.size; ta = 512 / 748
    if w / h > ta: nw = round(h * ta); im = im.crop(((w - nw) // 2, 0, (w - nw) // 2 + nw, h))
    else: nh = round(w / ta); im = im.crop((0, 0, w, nh))
    im = im.resize((512, 748), Image.LANCZOS)
    im.save(os.path.join(DST, i + ".png"), optimize=True)
    print(f"  {i} -> Cards/{i}.png")
