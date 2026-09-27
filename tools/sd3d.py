# -*- coding: utf-8 -*-
"""
3D SD from 2D SD: each hero's cut-out chibi (Resources/Art/SD/<id>.png) reconstructed into a
vertex-coloured mesh with TripoSR (MIT licence, runs on the local GPU), normalised (feet at y=0,
1.2 m tall, facing +Z, centred) and written as Resources/Art/SD3D/<id>.bytes for World/SdModel.

    python tools/sd3d.py intern cfo            # some
    python tools/sd3d.py                       # every hero with 2D SD art

The 3D figure is made FROM the 2D one, so the face, hair, outfit and head size are the same.
Previews (front / three-quarter / side, painted from the vertex colours) go to tools/out/sd3d/.

Needs: C:\\Users\\user\\TripoSR (git clone, isosurface patched to skimage), torch+CUDA,
omegaconf einops transformers trimesh scikit-image fast-simplification.
"""
import glob, os, struct, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SD = os.path.join(HERE, "..", "Assets", "ExcelHeroes", "ArtSource", "SD")   # clean (no sheet): the reconstruction input
OUT = os.path.join(HERE, "..", "Assets", "ExcelHeroes", "Resources", "Art", "SD3D")
PREVIEW = os.path.join(HERE, "out", "sd3d")
TRIPOSR = os.environ.get("TRIPOSR", r"C:\Users\user\TripoSR")
HEIGHT = 1.2
FACES = int(os.environ.get("SD3D_FACES", "36000"))
RES = int(os.environ.get("SD3D_RES", "256"))

sys.path.insert(0, TRIPOSR)


def prepare(path, ratio=0.85):
    """The cut-out on mid grey, the figure filling `ratio` of a square — TripoSR's own input."""
    im = Image.open(path).convert("RGBA")
    bbox = im.split()[3].point(lambda v: 255 if v > 20 else 0).getbbox()
    im = im.crop(bbox)
    side = int(max(im.size) / ratio)
    sq = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    sq.paste(im, ((side - im.width) // 2, (side - im.height) // 2), im)
    a = np.asarray(sq).astype(np.float32) / 255.0
    rgb = a[..., :3] * a[..., 3:4] + (1 - a[..., 3:4]) * 0.5
    return Image.fromarray((rgb * 255).astype(np.uint8)).resize((512, 512), Image.LANCZOS)


def orient(v):
    """TripoSR's frame → ours: the tallest axis is up; the figure faces the input camera."""
    # TripoSR: z is up, the input view looks along -x (the figure faces +x). Ours: y up, facing +z.
    x, y, z = v[:, 0], v[:, 1], v[:, 2]
    out = np.stack([-y, z, x], axis=1)
    out[:, 1] -= out[:, 1].min()
    s = HEIGHT / max(1e-6, out[:, 1].max())
    out *= s
    out[:, 0] -= (out[:, 0].max() + out[:, 0].min()) * 0.5
    out[:, 2] -= (out[:, 2].max() + out[:, 2].min()) * 0.5
    return out


def preview(v, c, path):
    """Three orthographic splat views from the vertex colours, painter's order."""
    views = []
    for yaw in (0, 40, 90):
        a = np.radians(yaw)
        rx = v[:, 0] * np.cos(a) + v[:, 2] * np.sin(a)
        rz = -v[:, 0] * np.sin(a) + v[:, 2] * np.cos(a)
        W, H = 300, 400
        img = np.full((H, W, 3), (104, 156, 214), np.uint8)
        px = ((rx / HEIGHT) * 330 + W / 2).astype(int)
        py = (H - 20 - (v[:, 1] / HEIGHT) * 360).astype(int)
        order = np.argsort(rz)            # far first
        for i in order:
            x0, y0 = px[i], py[i]
            if 1 <= x0 < W - 1 and 1 <= y0 < H - 1:
                img[y0 - 1:y0 + 2, x0 - 1:x0 + 2] = c[i, :3]
        views.append(Image.fromarray(img))
    sheet = Image.new("RGB", (900, 400))
    for i, im in enumerate(views): sheet.paste(im, (i * 300, 0))
    sheet.save(path)


def reproject(v, n, c, src):
    """
    Paint the 2D SD itself onto the surfaces that face the camera. TripoSR's own colours are a
    blur of it; the drawing is sharp, and it is the design. The figure's front silhouette spans
    the cut-out's opaque box, so a vertex's (x, y) maps straight onto it; a coarse z-buffer
    keeps the back of an arm from taking the colour of the chest in front of it.
    """
    im = Image.open(src).convert("RGBA")
    a = np.asarray(im)
    bbox = im.split()[3].point(lambda t: 255 if t > 20 else 0).getbbox()
    x0, y0, x1, y1 = bbox
    xmin, xmax = v[:, 0].min(), v[:, 0].max()
    H = v[:, 1].max()
    u = x0 + (v[:, 0] - xmin) / max(1e-6, xmax - xmin) * (x1 - x0 - 1)
    w = y0 + (1 - v[:, 1] / H) * (y1 - y0 - 1)
    # visibility from the front (+z is toward the viewer)
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
    # The back is not in the drawing. TripoSR's guess is a dark smear, so the back is painted
    # from the drawing too: the back of the head in the hair colour (the crown's median), the
    # back of the body in a blurred copy of the front (a shirt is the same shirt behind), both
    # shaded by TripoSR's own light/dark so folds and the nape survive.
    from PIL import ImageFilter
    alpha = a[..., 3]
    crown = a[y0:y0 + max(2, int((y1 - y0) * 0.1)), x0:x1]
    hair = np.median(crown[crown[..., 3] > 200][:, :3], axis=0) if (crown[..., 3] > 200).any() else np.array([60, 50, 50])
    blur = np.asarray(im.filter(ImageFilter.MedianFilter(9)).filter(ImageFilter.GaussianBlur(6))).astype(np.float32)
    bp = blur[wi, ui, :3]
    lum = (c[:, :3].astype(np.float32) @ np.array([0.3, 0.59, 0.11])) / 255.0
    shade = np.clip(0.75 + (lum - lum.mean()) * 0.8, 0.6, 1.05)[:, None]
    head = (v[:, 1] / H) > 0.585
    back_col = np.where(head[:, None], hair[None, :], bp) * shade * 0.92
    # where the blurred sample is transparent (outside the silhouette), fall back to hair/TripoSR
    miss = blur[wi, ui, 3] < 128
    back_col[miss & ~head] = c[miss & ~head, :3]
    out = c.astype(np.float32).copy()
    out[:, :3] = back_col * (1 - k) + px[:, :3] * k
    out[:, 3] = 255
    return np.clip(out, 0, 255).astype(np.uint8)


def write(path, v, n, c, f):
    with open(path, "wb") as fh:
        fh.write(b"SDM1")
        fh.write(struct.pack("<ii", len(v), len(f)))
        block = np.zeros(len(v), dtype=[("p", "<f4", 3), ("n", "<f4", 3), ("c", "u1", 4)])
        block["p"] = v; block["n"] = n; block["c"] = c
        fh.write(block.tobytes())
        fh.write(f.astype("<i4").tobytes())


def main(ids):
    import torch, trimesh
    from tsr.system import TSR
    dev = "cuda" if torch.cuda.is_available() else "cpu"
    model = TSR.from_pretrained("stabilityai/TripoSR", config_name="config.yaml", weight_name="model.ckpt")
    model.renderer.set_chunk_size(8192)
    model.to(dev)
    os.makedirs(OUT, exist_ok=True); os.makedirs(PREVIEW, exist_ok=True)
    for hid in ids:
        src = os.path.join(SD, hid + ".png")
        if not os.path.exists(src):
            print(f"  {hid}: no 2D SD"); continue
        img = prepare(src)
        img.save(os.path.join(PREVIEW, hid + "_input.png"))
        with torch.no_grad():
            codes = model([img], device=dev)
        mesh = model.extract_mesh(codes, True, resolution=RES)[0]
        # smooth the marching-cubes terraces before anything else
        trimesh.smoothing.filter_taubin(mesh, lamb=0.5, nu=-0.53, iterations=12)
        # keep the largest piece (floaters from the grey backdrop), then decimate
        parts = mesh.split(only_watertight=False)
        if len(parts) > 1: mesh = max(parts, key=lambda m: len(m.faces))
        if len(mesh.faces) > FACES:
            try:
                col = mesh.visual.vertex_colors.copy()
                simp = mesh.simplify_quadric_decimation(face_count=FACES)
                # carry colours over by nearest original vertex
                from scipy.spatial import cKDTree
                idx = cKDTree(mesh.vertices).query(simp.vertices)[1]
                simp.visual.vertex_colors = col[idx]
                mesh = simp
            except Exception as e:
                print(f"  {hid}: no decimation ({str(e)[:60]})")
        v = orient(np.asarray(mesh.vertices, dtype=np.float32))
        # orient() is a reflection (det -1), which turns every triangle inside out: flip them back
        faces = np.asarray(mesh.faces)[:, ::-1].copy()
        m2 = trimesh.Trimesh(vertices=v, faces=faces, process=False)
        n = np.asarray(m2.vertex_normals, dtype=np.float32)
        c = np.asarray(mesh.visual.vertex_colors, dtype=np.uint8)[:, :4]
        c = reproject(v, n, c, src)
        write(os.path.join(OUT, hid + ".bytes"), v, n, c, faces)
        preview(v, c, os.path.join(PREVIEW, hid + ".png"))
        print(f"  {hid}: {len(v)} verts {len(mesh.faces)} tris")


if __name__ == "__main__":
    ids = sys.argv[1:] or sorted(os.path.splitext(os.path.basename(p))[0] for p in glob.glob(os.path.join(SD, "*.png")))
    main(ids)
