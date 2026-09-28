# -*- coding: utf-8 -*-
"""
The Excel-themed attack sprites (World/BattleWorld fire profiles): the squad attacks with the
spreadsheet itself, not with guns.

    python tools/gen_fx_tex.py      → Assets/ExcelHeroes/Resources/Art/Fx/*.png

  cell_0..2   a sheet cell with a value in it          (셀 입력 · 자동 채우기 · 범위 붙여넣기)
  formula     =SUM( in a formula-bar box, green edge   (수식 계산 — the charged shot)
  result      the green-bordered active cell with the total
  trace_dot / trace_head / solid   Excel's blue trace-precedents arrow (참조 추적)
  ants        the marching-ants dash of a cut selection, tiles along x (잘라내기 Ctrl+X)
Drawn at 2x and downsampled; transparent outside the shapes.
"""
import os
from PIL import Image, ImageDraw, ImageFont

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "ExcelHeroes", "Resources", "Art", "Fx")
FONTS = [r"C:\Windows\Fonts\consolab.ttf", r"C:\Windows\Fonts\arialbd.ttf", r"C:\Windows\Fonts\malgunbd.ttf"]
GREEN = (33, 115, 70, 255)        # Excel's own green
BLUE = (46, 117, 214, 255)        # trace-arrow blue
GRID = (190, 196, 204, 255)


def font(px):
    for f in FONTS:
        if os.path.exists(f): return ImageFont.truetype(f, px)
    return ImageFont.load_default()


def save(im, name, size):
    os.makedirs(OUT, exist_ok=True)
    im.resize(size, Image.LANCZOS).save(os.path.join(OUT, name + ".png"))


def cell(text, name, edge=GRID, width=3):
    W, H = 256, 128
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rounded_rectangle((4, 4, W - 5, H - 5), 10, fill=(255, 255, 255, 255), outline=edge, width=width * 2)
    px = 64
    while True:                                   # long text (#DIV/0!) shrinks to fit the cell
        f = font(px); tw = d.textlength(text, font=f)
        if tw <= W - 44 or px <= 30: break
        px -= 4
    d.text((W - 24 - tw, H / 2), text, font=f, fill=(30, 36, 48, 255), anchor="lm")   # numbers sit right, as in a sheet
    save(im, name, (128, 64))


if __name__ == "__main__":
    cell("42", "cell_0"); cell("1,024", "cell_1"); cell("3.14", "cell_2")
    cell("99,999", "result", edge=GREEN, width=5)

    W, H = 384, 128
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rounded_rectangle((4, 4, W - 5, H - 5), 16, fill=(255, 255, 255, 250), outline=GREEN, width=8)
    d.text((28, H / 2), "=SUM(", font=font(76), fill=GREEN, anchor="lm")
    save(im, "formula", (192, 64))

    im = Image.new("RGBA", (128, 128), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.ellipse((24, 24, 104, 104), fill=BLUE); save(im, "trace_dot", (64, 64))
    im = Image.new("RGBA", (128, 128), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.polygon([(8, 16), (120, 64), (8, 112)], fill=BLUE); save(im, "trace_head", (64, 64))
    im = Image.new("RGBA", (16, 16), BLUE); save(im, "solid", (8, 8))

    im = Image.new("RGBA", (128, 16), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rectangle((0, 0, 63, 15), fill=(20, 24, 32, 255)); d.rectangle((64, 0, 127, 15), fill=(255, 255, 255, 255))
    save(im, "ants", (64, 8))

    # skills: a white fill to tint, Excel's own error, a + cell, an up arrow, a warning bang
    save(Image.new("RGBA", (16, 16), (255, 255, 255, 255)), "white", (8, 8))
    cell("#DIV/0!", "err_div0", edge=(214, 48, 48, 255), width=5)
    W, H = 160, 128
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rounded_rectangle((4, 4, W - 5, H - 5), 12, fill=(232, 250, 238, 255), outline=GREEN, width=8)
    d.text((W / 2, H / 2 + 2), "+", font=font(110), fill=GREEN, anchor="mm"); save(im, "plus_cell", (80, 64))
    im = Image.new("RGBA", (128, 128), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.polygon([(64, 8), (120, 72), (84, 72), (84, 120), (44, 120), (44, 72), (8, 72)], fill=(255, 255, 255, 255)); save(im, "arrow_up", (64, 64))
    im = Image.new("RGBA", (128, 128), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.ellipse((6, 6, 122, 122), fill=(232, 52, 52, 255), outline=(255, 255, 255, 255), width=8)
    d.text((64, 66), "!", font=font(96), fill=(255, 255, 255, 255), anchor="mm"); save(im, "bang", (64, 64))
    print("fx textures ->", os.path.normpath(OUT))
