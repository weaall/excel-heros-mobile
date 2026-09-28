# -*- coding: utf-8 -*-
"""
A mascot drawing inflated into a plush toy, for the monsters TRELLIS could not do (its ZeroGPU quota
runs dry after ~23 generations): no GPU, no model, only the drawing.

    python tools/mon3d_inflate.py            # every mascot in Resources/Art/SDMonsters without a SD3DM mesh
    python tools/mon3d_inflate.py cloud boss # these (replacing what is there)

The silhouette (the sprite's alpha) is the outline seen from the front; the depth at each point is
the square root of its distance from the edge — a stuffed-cushion profile, round at the rim and
fullest in the middle — with a floor so thin limbs are still limbs. Front and back are two height
fields that meet at the rim. The front wears the drawing itself (so the face is the face); the back
wears the drawing mirrored and blurred to its colours, as a toy's back is plain.
Writes the same SDM2 mesh + texture as tools/mon3d_pack.py (World/SdModel.LoadTextured) and a
four-view preview in tools/out/mon3d_pack/<id>_inflate.png.
"""
import os, sys
import numpy as np, trimesh
from PIL import Image, ImageFilter
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import mon3d_pack as mp
import glb_preview

N = 96             # grid cells across the longer side of the drawing (~10k vertices: several on screen at once on a phone)
DEPTH = 0.36       # the fullest depth (each side) against the width
FLOOR = 0.1        # the thinnest a part may be, against the fullest


def inflate(src):
    im = Image.open(src).convert("RGBA")
    a = np.asarray(im)[:, :, 3]
    ys, xs = np.nonzero(a > 40)
    x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    pad = 4
    im = im.crop((x0 - pad, y0 - pad, x1 + pad, y1 + pad))
    W, H = im.size
    s = N / max(W, H)
    gw, gh = max(8, int(W * s)), max(8, int(H * s))
    mask = np.asarray(im.resize((gw, gh), Image.LANCZOS))[:, :, 3] > 110
    mask = ndimage.binary_opening(mask, iterations=1)
    lab, n = ndimage.label(mask)
    if n > 1:   # keep the biggest piece and anything touching it
        sizes = ndimage.sum(mask, lab, range(1, n + 1)); mask = lab == (1 + int(np.argmax(sizes)))
    mask = ndimage.binary_fill_holes(mask)
    d = ndimage.distance_transform_edt(np.pad(mask, 1))[1:-1, 1:-1]
    dmax = max(1.0, d.max())
    # a rounder rim than sqrt: a quarter circle (a stuffed toy's seam), eased to the middle
    t = np.clip(d / (dmax * 0.55), 0, 1)
    prof = np.sqrt(1 - (1 - t) ** 2) * (0.75 + 0.25 * np.sqrt(np.clip(d / dmax, 0, 1)))
    prof = np.where(mask, np.maximum(prof, FLOOR), 0.0)
    depth = prof * DEPTH * gw

    # vertices at the grid corners: a corner's depth is the mean of the cells round it
    cd = np.zeros((gh + 1, gw + 1)); cm = np.zeros((gh + 1, gw + 1), bool)
    for dy in (0, 1):
        for dx in (0, 1):
            cd[dy:dy + gh, dx:dx + gw] += depth / 4.0
            cm[dy:dy + gh, dx:dx + gw] |= mask
    idx_f = -np.ones((gh + 1, gw + 1), int); idx_b = -np.ones((gh + 1, gw + 1), int)
    verts, uvs = [], []
    rim = cm & (cd < 1e-6 + FLOOR * DEPTH * gw * 0.26)
    for j in range(gh + 1):
        for i in range(gw + 1):
            if not cm[j, i]: continue
            x, y = i - gw / 2.0, gh - j
            u, v = i / gw, 1 - j / gh
            idx_f[j, i] = len(verts); verts.append((x, y, cd[j, i])); uvs.append((u * 0.5, v))
            # always a back vertex of its own (a shared rim vertex made the back faces sample the front half)
            idx_b[j, i] = len(verts); verts.append((x, y, -cd[j, i] * 0.85)); uvs.append((0.5 + (1 - u) * 0.5, v))
    faces = []
    for j in range(gh):
        for i in range(gw):
            if not mask[j, i]: continue
            a, b, c, e = (j, i), (j, i + 1), (j + 1, i + 1), (j + 1, i)
            fa, fb, fc, fe = idx_f[a], idx_f[b], idx_f[c], idx_f[e]
            faces += [(fa, fe, fc), (fa, fc, fb)]          # facing +z (y is up, rows run down)
            ba, bb, bc, be = idx_b[a], idx_b[b], idx_b[c], idx_b[e]
            faces += [(ba, bc, be), (ba, bb, bc)]
    # stitch the rim: along every mask edge the front and back must join
    for j in range(gh):
        for i in range(gw):
            if not mask[j, i]: continue
            for (dj, di), (p, q) in (((-1, 0), ((j, i), (j, i + 1))), ((1, 0), ((j + 1, i + 1), (j + 1, i))),
                                     ((0, -1), ((j + 1, i), (j, i))), ((0, 1), ((j, i + 1), (j + 1, i + 1)))):
                jj, ii = j + dj, i + di
                if 0 <= jj < gh and 0 <= ii < gw and mask[jj, ii]: continue
                # the side band gets vertices of its own, all on the plain back colours: a quad whose
                # corners sat on the front and the back halves of the texture swept across all of it
                f0, f1, b0, b1 = idx_f[p], idx_f[q], idx_b[p], idx_b[q]
                s0 = len(verts); verts.append(verts[f0]); uvs.append(uvs[b0])
                s1 = len(verts); verts.append(verts[f1]); uvs.append(uvs[b1])
                faces += [(s0, b1, b0), (s0, s1, b1)]   # wound outward (inward, the outline hull showed through as a black band)
    v = np.asarray(verts, float); f = np.asarray(faces, int)
    f = f[(f[:, 0] != f[:, 1]) & (f[:, 1] != f[:, 2]) & (f[:, 0] != f[:, 2])]
    # the grid's stair-stepped outline smoothed in x / y (Laplacian, a few passes; depth kept),
    # heaviest at the rim where the steps show from the side
    # smoothing works on the grid corners (front and back and seam copies share a corner's x, y)
    key = {}
    for j in range(gh + 1):
        for i in range(gw + 1):
            if idx_f[j, i] >= 0: key[(j, i)] = len(key)
    corner = np.zeros(len(v), int) - 1
    pos = {}
    for (j, i), k2 in key.items(): pos[(float(i - gw / 2.0), float(gh - j))] = k2
    for n2 in range(len(v)): corner[n2] = pos.get((float(v[n2, 0]), float(v[n2, 1])), -1)
    cxy = np.zeros((len(key), 2)); crim = np.zeros(len(key), bool)
    for (j, i), k2 in key.items(): cxy[k2] = (i - gw / 2.0, gh - j); crim[k2] = rim[j, i]
    cnb = [set() for _ in range(len(key))]
    for (j, i), k2 in key.items():
        for dj, di in ((0, 1), (1, 0), (0, -1), (-1, 0)):
            o = key.get((j + dj, i + di))
            if o is not None: cnb[k2].add(o)
    cnb = [np.array(sorted(x)) for x in cnb]
    for _ in range(6):
        avg = np.array([cxy[x].mean(0) if len(x) else cxy[k2] for k2, x in enumerate(cnb)])
        w = np.where(crim, 0.6, 0.2)[:, None]
        cxy = cxy * (1 - w) + avg * w
    ok = corner >= 0
    v[ok, :2] = cxy[corner[ok]]
    # the texture: the drawing | its mirrored, blurred colours
    T = mp.TEX
    front = Image.new("RGB", (W, H), (240, 240, 240)); front.paste(im, (0, 0), im)
    # the drawing's outer ink ring filled with the colours just inside it: seen edge-on at the rim it
    # read as a black band from the side, and the toon outline draws the edge anyway
    fa = np.asarray(front, float); al = np.asarray(im)[:, :, 3] > 110
    din = ndimage.distance_transform_edt(al)
    band = al & (din < max(W, H) * 0.035) & (fa.mean(axis=2) < 110)
    keep = (al & ~band).astype(float)
    num = np.stack([ndimage.gaussian_filter(fa[:, :, c] * keep, 6) for c in range(3)], 2)
    den = ndimage.gaussian_filter(keep, 6)[:, :, None] + 1e-6
    fa[band] = (num / den)[band]
    # and outside the silhouette the nearest inside colour, so a texel sampled just past the edge is not grey
    idx = ndimage.distance_transform_edt(~al, return_distances=False, return_indices=True)
    fa[~al] = fa[idx[0][~al], idx[1][~al]]
    front = Image.fromarray(np.clip(fa, 0, 255).astype(np.uint8)).resize((T // 2, T))
    # the back: the drawing's colours with its dark lines and features (eyes, mouth, outline) filled
    # from round about, then blurred — a plain toy back, not a smudged face
    fa = np.asarray(front.transpose(Image.FLIP_LEFT_RIGHT), float)
    dark = fa.mean(axis=2) < 95
    keep = (~dark).astype(float)
    num = np.stack([ndimage.gaussian_filter(fa[:, :, c] * keep, 14) for c in range(3)], 2)
    den = ndimage.gaussian_filter(keep, 14)[:, :, None] + 1e-6
    fill = num / den
    fa[dark] = fill[dark]
    back = Image.fromarray(np.clip(fa, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(10))
    back = Image.eval(back, lambda c: int(c * 0.93))
    tex = Image.new("RGB", (T, T)); tex.paste(front, (0, 0)); tex.paste(back, (T // 2, 0))
    return v, f, np.asarray(uvs, float), tex


def main(ids):
    os.makedirs(mp.OUT, exist_ok=True); os.makedirs(mp.PREV, exist_ok=True)
    for i in ids:
        src = os.path.join(mp.SRC, i + ".png")
        if not os.path.exists(src): print("  no sprite", i); continue
        v, f, uv, tex = inflate(src)
        v = mp.fit(v)
        m = trimesh.Trimesh(v, f, process=False)
        n = np.asarray(m.vertex_normals, np.float64)
        tex.save(os.path.join(mp.OUT, i + "_tex.png"))
        vu = v * [-1, 1, 1]; nu = n * [-1, 1, 1]
        mp.write(os.path.join(mp.OUT, i + ".bytes"), vu.astype(np.float32), nu.astype(np.float32), uv.astype(np.float32), f[:, ::-1].copy())
        pm = trimesh.Trimesh(v, f, process=False,
                             visual=trimesh.visual.TextureVisuals(uv=uv, material=trimesh.visual.material.PBRMaterial(baseColorTexture=tex)))
        pts, nor, col = glb_preview.samples(pm)
        views = [glb_preview.view(pts, nor, col, y) for y in (0, -35, 90, 180)]
        S = views[0].size[0]; sheet = Image.new("RGB", (S * 4, S))
        for k, imv in enumerate(views): sheet.paste(imv, (k * S, 0))
        sheet.save(os.path.join(mp.PREV, i + "_inflate.png"))
        print(f"  {i}: {len(v)} v, {len(f)} t")


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        have = {p[:-8] for p in os.listdir(mp.OUT) if p.endswith("_tex.png")}   # textured (SDM2) ones; the old SDM1 meshes are not worn
        args = sorted(p[:-4] for p in os.listdir(mp.SRC) if p.endswith(".png") and p[:-4] not in have)
    main(args)
