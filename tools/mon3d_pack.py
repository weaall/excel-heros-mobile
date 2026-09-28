# -*- coding: utf-8 -*-
"""
TRELLIS GLB (tools/mon3d_trellis.py) → the game's textured monster mesh.

TRELLIS gets the shape right but blurs the face out of its texture (eyes and mouth are gone). The
mascot drawing IS the face, so it is projected back onto the mesh from the view TRELLIS
reconstructed it from: every texel of a new 1024 texture is placed on the surface (rasterised in
UV space), and where that point faces the input camera and is not hidden, it takes the drawing's
pixel; elsewhere (sides, back) it keeps TRELLIS's own colour. The input view is found by matching
the mesh's silhouette from four yaws (and mirrored) against the drawing's alpha.

    python tools/mon3d_pack.py [ids]      → Resources/Art/SD3DM/<id>.bytes (SDM2) + <id>_tex.png
                                            tools/out/mon3d_pack/<id>.png (four-view preview)
SDM2: "SDM2", nv, nt, nv × (pos xyz, normal xyz, uv) float32, nt × 3 int32. Height 1, feet y = 0,
facing +Z (the drawing's view), like the SDM1 meshes it replaces (World/SdModel.BuildMonster).
"""
import os, struct, sys
import numpy as np, trimesh
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
GLB = os.path.join(HERE, "out", "mon3d_glb")
SRC = os.path.join(HERE, "..", "Assets", "ExcelHeroes", "Resources", "Art", "SDMonsters")
OUT = os.path.join(HERE, "..", "Assets", "ExcelHeroes", "Resources", "Art", "SD3DM")
PREV = os.path.join(HERE, "out", "mon3d_pack")
TEX = 1024
G = 384          # silhouette / depth grid


def load(path):
    s = trimesh.load(path)
    m = s.to_geometry() if hasattr(s, "to_geometry") else s
    mat = m.visual.material
    img = getattr(mat, "baseColorTexture", None) or getattr(mat, "image", None)
    return (np.asarray(m.vertices, np.float64), np.asarray(m.faces, np.int64),
            np.asarray(m.visual.uv, np.float64), img.convert("RGB"))


def rot(yaw, mirror):
    r = np.radians(yaw)
    R = np.array([[np.cos(r), 0, np.sin(r)], [0, 1, 0], [-np.sin(r), 0, np.cos(r)]])
    if mirror: R = np.diag([-1, 1, 1]) @ R
    return R


def fit(v):
    """Feet on y = 0, height 1, centred in x and z."""
    v = v - [0, v[:, 1].min(), 0]
    v = v / max(1e-6, v[:, 1].max())
    v[:, 0] -= (v[:, 0].max() + v[:, 0].min()) * 0.5
    v[:, 2] -= (v[:, 2].max() + v[:, 2].min()) * 0.5
    return v


def silhouette(v, f, S=G):
    """Front orthographic mask (looking down -z) and depth, fitted to the mesh's own x/y box."""
    x0, x1 = v[:, 0].min(), v[:, 0].max(); y0, y1 = v[:, 1].min(), v[:, 1].max()
    span = max(x1 - x0, y1 - y0)
    px = (v[:, 0] - x0) / span * (S - 1); py = (y1 - v[:, 1]) / span * (S - 1)
    depth = np.full((S, S), -1e9)
    tri = f
    for t in tri:
        xs, ys, zs = px[t], py[t], v[t, 2]
        a, b = int(max(0, np.floor(xs.min()))), int(min(S - 1, np.ceil(xs.max())))
        c, d = int(max(0, np.floor(ys.min()))), int(min(S - 1, np.ceil(ys.max())))
        if b < a or d < c: continue
        gx, gy = np.meshgrid(np.arange(a, b + 1), np.arange(c, d + 1))
        den = (ys[1] - ys[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (ys[0] - ys[2])
        if abs(den) < 1e-9: continue
        l0 = ((ys[1] - ys[2]) * (gx - xs[2]) + (xs[2] - xs[1]) * (gy - ys[2])) / den
        l1 = ((ys[2] - ys[0]) * (gx - xs[2]) + (xs[0] - xs[2]) * (gy - ys[2])) / den
        l2 = 1 - l0 - l1
        inside = (l0 >= -0.01) & (l1 >= -0.01) & (l2 >= -0.01)
        z = l0 * zs[0] + l1 * zs[1] + l2 * zs[2]
        sub = depth[c:d + 1, a:b + 1]
        np.maximum(sub, np.where(inside, z, -1e9), out=sub)
    return depth, (x0, y1, span)


def sprite_mask(src, S=G):
    im = Image.open(src).convert("RGBA")
    bb = im.split()[3].point(lambda t: 255 if t > 40 else 0).getbbox()
    crop = im.crop(bb); w, h = crop.size; side = max(w, h)
    sq = Image.new("RGBA", (side, side), (0, 0, 0, 0)); sq.paste(crop, (0, 0))   # top-left aligned, like silhouette()
    return np.asarray(sq.resize((S, S), Image.LANCZOS)), crop


def best_view(v, f, src):
    target = sprite_mask(src)[0][..., 3] > 100
    best = None
    for mirror in (False, True):
        for yaw in range(0, 360, 15):
            vv = fit(v @ rot(yaw, mirror).T)
            d, _ = silhouette(vv, f, S=128)   # coarse; every face (a subset leaves holes)
            m = np.asarray(Image.fromarray((d > -1e8).astype(np.uint8) * 255).resize((G, G))) > 127
            iou = (m & target).sum() / max(1, (m | target).sum())
            if best is None or iou > best[0]: best = (iou, yaw, mirror)
    return best


def bake(v, n, f, uv, tex, src):
    """New texture: the drawing where the surface faces the input camera, TRELLIS's colour elsewhere."""
    depth, (x0, y1, span) = silhouette(v, f)
    rgba, crop = sprite_mask(src)
    draw = np.asarray(crop.convert("RGBA")).astype(np.float32)
    dh, dw = draw.shape[:2]; side = max(dw, dh)
    old = np.asarray(tex.resize((TEX, TEX), Image.LANCZOS)).astype(np.float32)
    out = old.copy()
    zspan = v[:, 2].max() - v[:, 2].min()
    tu = uv[:, 0] * (TEX - 1); tv = (1 - uv[:, 1]) * (TEX - 1)
    for t in f:
        xs, ys = tu[t], tv[t]
        a, b = int(max(0, np.floor(xs.min()))), int(min(TEX - 1, np.ceil(xs.max())))
        c, d = int(max(0, np.floor(ys.min()))), int(min(TEX - 1, np.ceil(ys.max())))
        if b < a or d < c: continue
        gx, gy = np.meshgrid(np.arange(a, b + 1), np.arange(c, d + 1))
        den = (ys[1] - ys[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (ys[0] - ys[2])
        if abs(den) < 1e-12: continue
        l0 = ((ys[1] - ys[2]) * (gx - xs[2]) + (xs[2] - xs[1]) * (gy - ys[2])) / den
        l1 = ((ys[2] - ys[0]) * (gx - xs[2]) + (xs[0] - xs[2]) * (gy - ys[2])) / den
        l2 = 1 - l0 - l1
        inside = (l0 >= -0.02) & (l1 >= -0.02) & (l2 >= -0.02)
        if not inside.any(): continue
        L = np.stack([l0[inside], l1[inside], l2[inside]], 1)
        P = L @ v[t]; N = L @ n[t]
        nz = N[:, 2] / np.maximum(1e-6, np.linalg.norm(N, axis=1))
        facing = np.clip((nz - 0.3) * 2.5, 0, 1)   # not at grazing angles: the drawing's outline smeared round the sides
        gxp = np.clip(((P[:, 0] - x0) / span * (G - 1)).round().astype(int), 0, G - 1)
        gyp = np.clip(((y1 - P[:, 1]) / span * (G - 1)).round().astype(int), 0, G - 1)
        vis = P[:, 2] >= depth[gyp, gxp] - zspan * 0.04
        sx = np.clip(((P[:, 0] - x0) / span * side).astype(int), 0, dw - 1)
        sy = np.clip(((y1 - P[:, 1]) / span * side).astype(int), 0, dh - 1)
        px = draw[sy, sx]
        k = (facing * vis * (px[:, 3] / 255.0))[:, None]
        yy, xx = gy[inside], gx[inside]
        out[yy, xx] = px[:, :3] * k + out[yy, xx] * (1 - k)
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8))


def write(path, v, n, uv, f):
    with open(path, "wb") as fh:
        fh.write(b"SDM2"); fh.write(struct.pack("<ii", len(v), len(f)))
        block = np.zeros(len(v), dtype=[("p", "<f4", 3), ("n", "<f4", 3), ("t", "<f4", 2)])
        block["p"] = v; block["n"] = n; block["t"] = uv
        fh.write(block.tobytes()); fh.write(f.astype("<i4").tobytes())


def main(ids):
    os.makedirs(OUT, exist_ok=True); os.makedirs(PREV, exist_ok=True)
    scores = {}
    sys.path.insert(0, HERE)
    import glb_preview
    for i in ids:
        glb = os.path.join(GLB, i + ".glb"); src = os.path.join(SRC, i + ".png")
        if not os.path.exists(glb): print(f"  no glb {i}"); continue
        v, f, uv, tex = load(glb)
        iou, yaw, mirror = best_view(v, f, src)
        v = fit(v @ rot(yaw, mirror).T)
        if mirror: f = f[:, ::-1].copy()      # a mirror turns the winding inside out
        # a mascot is about as deep as it is wide; TRELLIS sometimes returns a relief (the cloud, the
        # cat came back paper-thin from the side)
        width = v[:, 0].max() - v[:, 0].min(); depth = v[:, 2].max() - v[:, 2].min()
        if depth < width * 0.3:
            # a card, not a figure (the cloud, the flame-framed boss): inflating it only makes a
            # black-sided slab. Left for mon3d_fix.py (toy render); the game keeps the 2D mascot.
            print(f"  {i}: FLAT (depth {depth / width:.2f} of width) -- skipped, retry with mon3d_fix.py")
            scores[i] = 0.5
            for ext in (".bytes", "_tex.png"):
                q = os.path.join(OUT, i + ext)
                if ext == "_tex.png" and os.path.exists(q): os.remove(q)
            continue
        if depth < width * 0.62:
            k = min(3.0, width * 0.62 / max(1e-6, depth)); v[:, 2] *= k
            print(f"  {i}: depth x{k:.2f}")
        m = trimesh.Trimesh(v, f, process=False)
        n = np.asarray(m.vertex_normals, np.float64)
        img = bake(v, n, f, uv, tex, src)
        img.save(os.path.join(OUT, i + "_tex.png"))
        # glTF/trimesh is right-handed, Unity left-handed: mirror x (and the winding) or the drawing
        # reads backwards in game (HR boss's TERMINATION came out mirrored)
        vu = v * [-1, 1, 1]; nu = n * [-1, 1, 1]
        write(os.path.join(OUT, i + ".bytes"), vu.astype(np.float32), nu.astype(np.float32), uv.astype(np.float32), f[:, ::-1].copy())
        # preview from the packed result
        pm = trimesh.Trimesh(v, f, process=False,
                             visual=trimesh.visual.TextureVisuals(uv=uv, material=trimesh.visual.material.PBRMaterial(baseColorTexture=img)))
        pts, nor, col = glb_preview.samples(pm)
        views = [glb_preview.view(pts, nor, col, y) for y in (0, -35, 90, 180)]
        S = views[0].size[0]; sheet = Image.new("RGB", (S * 4, S))
        for k, im in enumerate(views): sheet.paste(im, (k * S, 0))
        sheet.save(os.path.join(PREV, i + ".png"))
        print(f"  {i}: {len(v)} v, {len(f)} t, view yaw {yaw}{' mirrored' if mirror else ''} (IoU {iou:.2f})")
        scores[i] = round(float(iou), 3)
    import json
    path = os.path.join(PREV, "iou.json")
    old = json.load(open(path)) if os.path.exists(path) else {}
    old.update(scores); json.dump(old, open(path, "w"), indent=1)


if __name__ == "__main__":
    ids = sys.argv[1:] or sorted(p[:-4] for p in os.listdir(GLB) if p.endswith(".glb"))
    main(ids)
