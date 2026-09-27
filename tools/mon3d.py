# -*- coding: utf-8 -*-
"""
3D monsters from the 2D monster mascots: each Gemini SD sprite (Resources/Art/SDMonsters/<id>.png)
reconstructed with TripoSR (MIT, local GPU — see sd3d.py), the drawing painted back onto the
front, the back a blurred copy of the front, and written as Resources/Art/SD3DM/<id>.bytes for
World/SdModel.BuildMonster. Mascots are compact shapes (a copier, a blob, a stapler), which is
what TripoSR does well; the faces stay the drawing's own.

    python tools/mon3d.py copier circ          # some
    python tools/mon3d.py                      # every monster sprite

Height is normalised to 1.0 (BattleWorld scales elites and bosses), facing +Z, feet at y = 0.
Previews go to tools/out/mon3d/.
"""
import glob, importlib.util, os, sys
import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("sd3d", os.path.join(HERE, "sd3d.py"))
sd3d = importlib.util.module_from_spec(spec); spec.loader.exec_module(sd3d)

SRC = os.path.join(HERE, "..", "Assets", "ExcelHeroes", "Resources", "Art", "SDMonsters")
OUT = os.path.join(HERE, "..", "Assets", "ExcelHeroes", "Resources", "Art", "SD3DM")
PREVIEW = os.path.join(HERE, "out", "mon3d")
HEIGHT = 1.0
FACES = int(os.environ.get("MON3D_FACES", "24000"))
RES = int(os.environ.get("MON3D_RES", "256"))


def orient(v):
    out = sd3d.orient(v)                    # 1.2 tall from sd3d; rescale to ours
    out *= HEIGHT / sd3d.HEIGHT
    # TripoSR sometimes returns a relief (a sphere as a thick coin). A mascot is about as deep
    # as it is wide: inflate the depth toward that, since the squad sees it turned 35 degrees.
    width = out[:, 0].max() - out[:, 0].min()
    depth = out[:, 2].max() - out[:, 2].min()
    want = width * 0.75
    if depth < want:
        f = min(3.5, want / max(1e-6, depth))
        zc = (out[:, 2].max() + out[:, 2].min()) * 0.5
        out[:, 2] = (out[:, 2] - zc) * f
        print(f"  (inflated depth x{f:.2f})")
    return out


def paint(v, n, c, src):
    """The drawing on the front; the back a blurred, slightly darker copy of the front."""
    im = Image.open(src).convert("RGBA")
    a = np.asarray(im)
    bbox = im.split()[3].point(lambda t: 255 if t > 20 else 0).getbbox()
    x0, y0, x1, y1 = bbox
    xmin, xmax = v[:, 0].min(), v[:, 0].max()
    H = v[:, 1].max()
    u = x0 + (v[:, 0] - xmin) / max(1e-6, xmax - xmin) * (x1 - x0 - 1)
    w = y0 + (1 - v[:, 1] / H) * (y1 - y0 - 1)
    G = 192
    gi = np.clip((u - x0) / (x1 - x0) * (G - 1), 0, G - 1).astype(int)
    gj = np.clip((w - y0) / (y1 - y0) * (G - 1), 0, G - 1).astype(int)
    zbuf = np.full((G, G), -1e9, np.float32)
    np.maximum.at(zbuf, (gj, gi), v[:, 2])
    near = zbuf[gj, gi]
    depth = max(1e-6, v[:, 2].max() - v[:, 2].min())
    visible = v[:, 2] >= near - depth * 0.05
    facing = np.clip((n[:, 2] + 0.15) * 1.5, 0, 1)
    ui = np.clip(u.round().astype(int), 0, a.shape[1] - 1)
    wi = np.clip(w.round().astype(int), 0, a.shape[0] - 1)
    px = a[wi, ui].astype(np.float32)
    k = (facing * visible * (px[:, 3] / 255.0))[:, None]
    blur = np.asarray(im.filter(ImageFilter.MedianFilter(9)).filter(ImageFilter.GaussianBlur(8))).astype(np.float32)
    bp = blur[wi, ui, :3]
    lum = (c[:, :3].astype(np.float32) @ np.array([0.3, 0.59, 0.11])) / 255.0
    shade = np.clip(0.75 + (lum - lum.mean()) * 0.8, 0.6, 1.05)[:, None]
    back = bp * shade * 0.9
    miss = blur[wi, ui, 3] < 128
    back[miss] = c[miss, :3]
    out = c.astype(np.float32).copy()
    out[:, :3] = back * (1 - k) + px[:, :3] * k
    out[:, 3] = 255
    return np.clip(out, 0, 255).astype(np.uint8)


def main(ids):
    import torch, trimesh
    sys.path.insert(0, sd3d.TRIPOSR)
    from tsr.system import TSR
    dev = "cuda" if torch.cuda.is_available() else "cpu"
    model = TSR.from_pretrained("stabilityai/TripoSR", config_name="config.yaml", weight_name="model.ckpt")
    model.renderer.set_chunk_size(8192)
    model.to(dev)
    os.makedirs(OUT, exist_ok=True); os.makedirs(PREVIEW, exist_ok=True)
    for mid in ids:
        src = os.path.join(SRC, mid + ".png")
        if not os.path.exists(src):
            print(f"  {mid}: no sprite"); continue
        img = sd3d.prepare(src, ratio=0.8)
        with torch.no_grad():
            codes = model([img], device=dev)
        mesh = model.extract_mesh(codes, True, resolution=RES)[0]
        trimesh.smoothing.filter_taubin(mesh, lamb=0.5, nu=-0.53, iterations=10)
        parts = mesh.split(only_watertight=False)
        if len(parts) > 1: mesh = max(parts, key=lambda m: len(m.faces))
        if len(mesh.faces) > FACES:
            try:
                col = mesh.visual.vertex_colors.copy()
                simp = mesh.simplify_quadric_decimation(face_count=FACES)
                from scipy.spatial import cKDTree
                idx = cKDTree(mesh.vertices).query(simp.vertices)[1]
                simp.visual.vertex_colors = col[idx]
                mesh = simp
            except Exception as e:
                print(f"  {mid}: no decimation ({str(e)[:60]})")
        v = orient(np.asarray(mesh.vertices, dtype=np.float32))
        faces = np.asarray(mesh.faces)[:, ::-1].copy()
        m2 = trimesh.Trimesh(vertices=v, faces=faces, process=False)
        n = np.asarray(m2.vertex_normals, dtype=np.float32)
        c = np.asarray(mesh.visual.vertex_colors, dtype=np.uint8)[:, :4]
        c = paint(v, n, c, src)
        sd3d.write(os.path.join(OUT, mid + ".bytes"), v, n, c, faces)
        sd3d.preview(v * (sd3d.HEIGHT / HEIGHT), c, os.path.join(PREVIEW, mid + ".png"))
        print(f"  {mid}: {len(v)} verts, {len(faces)} tris")


if __name__ == "__main__":
    ids = sys.argv[1:] or sorted(os.path.splitext(os.path.basename(p))[0] for p in glob.glob(os.path.join(SRC, "*.png")))
    main(ids)
