# -*- coding: utf-8 -*-
"""Quick look at a textured GLB without a renderer: dense surface samples, texture colours, simple
Lambert light, z-buffered splats from four yaws.  python tools/glb_preview.py a.glb [b.glb ...] out.png"""
import sys
import numpy as np, trimesh
from PIL import Image


def load(path):
    s = trimesh.load(path)
    return s.to_geometry() if hasattr(s, "to_geometry") else s


def samples(m, n=260000):
    pts, fi = trimesh.sample.sample_surface(m, n)
    tri = m.triangles[fi]
    bary = trimesh.triangles.points_to_barycentric(tri, pts)
    uv = (m.visual.uv[m.faces[fi]] * bary[:, :, None]).sum(1)
    img = m.visual.material.baseColorTexture or m.visual.material.image
    a = np.asarray(img.convert("RGB")).astype(np.float32)
    h, w = a.shape[:2]
    x = np.clip((uv[:, 0] % 1) * (w - 1), 0, w - 1).astype(int)
    y = np.clip((1 - uv[:, 1] % 1) * (h - 1), 0, h - 1).astype(int)
    return pts, m.face_normals[fi], a[y, x]


def view(pts, nor, col, yaw, S=300):
    r = np.radians(yaw)
    R = np.array([[np.cos(r), 0, np.sin(r)], [0, 1, 0], [-np.sin(r), 0, np.cos(r)]])
    c = (pts.max(0) + pts.min(0)) * 0.5
    p = (pts - c) @ R.T; n = nor @ R.T
    lo, hi = pts.min(0), pts.max(0); span = (hi - lo).max() * 1.1
    u = ((p[:, 0] / span + 0.5) * S).astype(int); v = ((0.5 - p[:, 1] / span) * S).astype(int)
    z = p[:, 2]
    img = np.full((S, S, 3), 235, np.float32); zb = np.full((S, S), -1e9)
    light = np.clip(0.55 + 0.45 * (n @ np.array([0.3, 0.5, 0.8])), 0.3, 1.0)[:, None]
    order = np.argsort(z)
    ok = (u >= 0) & (u < S) & (v >= 0) & (v < S)
    for i in order[ok[order]]:
        img[v[i], u[i]] = col[i] * light[i]
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))


if __name__ == "__main__":
    ins, out = sys.argv[1:-1], sys.argv[-1]
    rows = []
    for path in ins:
        pts, nor, col = samples(load(path))
        rows.append([view(pts, nor, col, y) for y in (0, 35, 90, 180)])
    S = rows[0][0].size[0]
    sheet = Image.new("RGB", (S * 4, S * len(rows)), (255, 255, 255))
    for j, row in enumerate(rows):
        for i, im in enumerate(row): sheet.paste(im, (i * S, j * S))
    sheet.save(out)
