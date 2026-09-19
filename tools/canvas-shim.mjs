// A canvas just big enough to run the web game's sprite builders under Node.
//
// The web builds every hero sprite procedurally — the 0x72 base recoloured to the hero's palette,
// or a hand-written paper doll — and it does that against a 2D canvas. Reimplementing those
// builders in C# would be several hundred lines of pixel work whose only job is to agree with the
// originals, so instead the originals run here, unmodified, and their output is baked to PNG.
//
// That means this shim only has to cover what they actually call: an RGBA buffer, a
// nearest-neighbour drawImage, and get/put/createImageData. Smoothing is ignored because the game
// disables it everywhere — these are pixel sprites, and every scale factor in use is an integer.

import fs from 'node:fs';
import path from 'node:path';
import { decodePNG, encodePNG } from './png.mjs';

/** Accepts the colour spellings the game actually uses: #rgb, #rrggbb, rgb(), rgba(). */
function parseColor(v) {
  if (typeof v !== 'string') return [0, 0, 0, 255];
  const m = v.match(/rgba?\(([^)]+)\)/);
  if (m) {
    const p = m[1].split(',').map((x) => parseFloat(x));
    return [p[0] | 0, p[1] | 0, p[2] | 0, Math.round((p[3] ?? 1) * 255)];
  }
  let h = v.replace('#', '');
  if (h.length === 3) h = h.split('').map((c) => c + c).join('');
  if (h.length !== 6) return [0, 0, 0, 255];
  return [parseInt(h.slice(0, 2), 16), parseInt(h.slice(2, 4), 16), parseInt(h.slice(4, 6), 16), 255];
}

export class ImageData {
  constructor(a, b, c) {
    if (a instanceof Uint8ClampedArray) { this.data = a; this.width = b; this.height = c; }
    else { this.width = a; this.height = b; this.data = new Uint8ClampedArray(a * b * 4); }
  }
}

class Ctx {
  constructor(canvas) {
    this.canvas = canvas;
    this.imageSmoothingEnabled = false;
    this._stack = [];
    this._sx = 1; this._sy = 1; this._tx = 0; this._ty = 0;
  }

  // Ignored: the sprite code only uses it for hue variants, which are not baked.
  set filter(_) {}
  get filter() { return 'none'; }

  // Bosses get office props painted over the dungeon creature — "사무실 소품이 없으면 던전
  // 몬스터일 뿐이다" — and those are plain filled rectangles and paths.
  set fillStyle(v) { this._fill = parseColor(v); }
  get fillStyle() { return this._fill; }
  set strokeStyle(v) { this._stroke = parseColor(v); }
  get strokeStyle() { return this._stroke; }

  fillRect(x, y, w, h) { this._rect(x, y, w, h, this._fill ?? [0, 0, 0, 255]); }
  clearRect(x, y, w, h) { this._rect(x, y, w, h, [0, 0, 0, 0], true); }

  _rect(x, y, w, h, rgba, clear = false) {
    x = Math.round(x * this._sx + this._tx); y = Math.round(y * this._sy + this._ty);
    w = Math.round(w * this._sx); h = Math.round(h * this._sy);
    if (w < 0) { x += w; w = -w; }
    if (h < 0) { y += h; h = -h; }
    const { data, width: cw, height: ch } = this.canvas;
    const a = (rgba[3] / 255) * (this.globalAlpha ?? 1);
    for (let j = 0; j < h; j++) {
      const ty = y + j;
      if (ty < 0 || ty >= ch) continue;
      for (let i = 0; i < w; i++) {
        const tx = x + i;
        if (tx < 0 || tx >= cw) continue;
        const d = (ty * cw + tx) * 4;
        if (clear) { data[d + 3] = 0; continue; }
        for (let k = 0; k < 3; k++) data[d + k] = data[d + k] + (rgba[k] - data[d + k]) * a;
        data[d + 3] = Math.max(data[d + 3], rgba[3] * a);
      }
    }
  }

  // Paths, kept to what the prop drawings use: straight edges filled as a polygon.
  beginPath() { this._path = []; }
  moveTo(x, y) { (this._path ??= []).push([x, y]); }
  lineTo(x, y) { (this._path ??= []).push([x, y]); }
  closePath() {}
  stroke() {}
  fill() {
    const p = this._path;
    if (!p || p.length < 3) return;
    const ys = p.map((q) => q[1]), y0 = Math.floor(Math.min(...ys)), y1 = Math.ceil(Math.max(...ys));
    for (let y = y0; y <= y1; y++) {
      const xs = [];
      for (let i = 0, n = p.length; i < n; i++) {
        const [ax, ay] = p[i], [bx, by] = p[(i + 1) % n];
        if (ay === by || y < Math.min(ay, by) || y >= Math.max(ay, by)) continue;
        xs.push(ax + ((y - ay) / (by - ay)) * (bx - ax));
      }
      xs.sort((a, b) => a - b);
      for (let i = 0; i + 1 < xs.length; i += 2)
        this._rect(xs[i], y, xs[i + 1] - xs[i], 1, this._fill ?? [0, 0, 0, 255]);
    }
  }

  save() { this._stack.push([this._sx, this._sy, this._tx, this._ty]); }
  restore() { const s = this._stack.pop(); if (s) [this._sx, this._sy, this._tx, this._ty] = s; }
  scale(x, y) { this._sx *= x; this._sy *= y; }
  translate(x, y) { this._tx += x * this._sx; this._ty += y * this._sy; }

  createImageData(w, h) { return new ImageData(w, h); }

  getImageData(x, y, w, h) {
    const out = new ImageData(w, h);
    const { data: src, width: cw, height: ch } = this.canvas;
    for (let j = 0; j < h; j++) {
      for (let i = 0; i < w; i++) {
        const sx = x + i, sy = y + j;
        if (sx < 0 || sy < 0 || sx >= cw || sy >= ch) continue;
        const s = (sy * cw + sx) * 4, d = (j * w + i) * 4;
        out.data[d] = src[s]; out.data[d + 1] = src[s + 1];
        out.data[d + 2] = src[s + 2]; out.data[d + 3] = src[s + 3];
      }
    }
    return out;
  }

  putImageData(img, x, y) {
    const { data: dst, width: cw, height: ch } = this.canvas;
    for (let j = 0; j < img.height; j++) {
      for (let i = 0; i < img.width; i++) {
        const dx = x + i, dy = y + j;
        if (dx < 0 || dy < 0 || dx >= cw || dy >= ch) continue;
        const s = (j * img.width + i) * 4, d = (dy * cw + dx) * 4;
        dst[d] = img.data[s]; dst[d + 1] = img.data[s + 1];
        dst[d + 2] = img.data[s + 2]; dst[d + 3] = img.data[s + 3];
      }
    }
  }

  /**
   * drawImage in its 3-, 5- and 9-argument forms, nearest-neighbour and source-over.
   *
   * Source-over rather than a straight copy matters: heroSkins stamps a head and accessories over
   * an already-drawn body, and a copy would punch transparent holes through it.
   */
  drawImage(img, ...a) {
    let sx = 0, sy = 0, sw = img.width, sh = img.height, dx, dy, dw, dh;
    if (a.length === 2) { [dx, dy] = a; dw = sw; dh = sh; }
    else if (a.length === 4) { [dx, dy, dw, dh] = a; }
    else { [sx, sy, sw, sh, dx, dy, dw, dh] = a; }

    dx = dx * this._sx + this._tx; dy = dy * this._sy + this._ty;
    dw *= this._sx; dh *= this._sy;

    // A negative scale is a mirror: normalise it to a positive box and flip the source lookup.
    const flipX = dw < 0, flipY = dh < 0;
    if (flipX) { dx += dw; dw = -dw; }
    if (flipY) { dy += dh; dh = -dh; }

    const { data: dst, width: cw, height: ch } = this.canvas;
    const src = img.data, iw = img.width;
    for (let j = 0; j < dh; j++) {
      const ty = Math.round(dy) + j;
      if (ty < 0 || ty >= ch) continue;
      const py = sy + Math.floor(((flipY ? dh - 1 - j : j) * sh) / dh);
      for (let i = 0; i < dw; i++) {
        const tx = Math.round(dx) + i;
        if (tx < 0 || tx >= cw) continue;
        const px = sx + Math.floor(((flipX ? dw - 1 - i : i) * sw) / dw);
        if (px < 0 || py < 0 || px >= iw || py >= img.height) continue;
        const s = (py * iw + px) * 4;
        if (src[s + 3] === 0) continue;                   // source-over: leave what is underneath
        const d = (ty * cw + tx) * 4;
        dst[d] = src[s]; dst[d + 1] = src[s + 1];
        dst[d + 2] = src[s + 2]; dst[d + 3] = src[s + 3];
      }
    }
  }
}

class Canvas {
  constructor() { this._w = 0; this._h = 0; this.data = new Uint8ClampedArray(0); }
  get width() { return this._w; }
  set width(v) { this._w = v | 0; this._alloc(); }
  get height() { return this._h; }
  set height(v) { this._h = v | 0; this._alloc(); }
  _alloc() { this.data = new Uint8ClampedArray(Math.max(0, this._w * this._h * 4)); this._ctx = null; }
  getContext() { return (this._ctx ??= new Ctx(this)); }
  toDataURL() { return 'data:image/png;base64,' + Buffer.from(encodePNG(this._w, this._h, this.data)).toString('base64'); }
}

/** Installs the globals the web modules expect. Call once before importing them. */
export function install(assetRoot = null) {
  globalThis.ImageData = ImageData;
  globalThis.document = { createElement: (tag) => { if (tag !== 'canvas') throw new Error(`no shim for <${tag}>`); return new Canvas(); } };
  globalThis.window = globalThis;

  // packSprites loads its tilesets through fetch + createImageBitmap. Pointing those at the web
  // build's asset folder lets its own frame-cutting code run here unchanged, which matters more
  // than it sounds: the sheet coordinates are per-creature rows and easy to transcribe wrong.
  if (assetRoot) {
    globalThis.fetch = async (url) => ({ ok: true, status: 200, blob: async () => path.join(assetRoot, url) });
    globalThis.createImageBitmap = async (file) => loadImage(file);
  }
}

/** Loads a PNG into something drawImage accepts (the shim's stand-in for ImageBitmap). */
export function loadImage(file) {
  const img = decodePNG(fs.readFileSync(file));
  return { width: img.width, height: img.height, data: img.data };
}

export function savePNG(file, canvasOrImage) {
  const w = canvasOrImage.width, h = canvasOrImage.height;
  fs.writeFileSync(file, encodePNG(w, h, canvasOrImage.data));
}
