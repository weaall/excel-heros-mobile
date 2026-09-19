// Minimal zero-dependency PNG decoder (8-bit RGBA/RGB/palette, non-interlaced) + bounding-box finder.
// Usage: node scripts/png.mjs boxes <file.png> [minAlpha]   → prints sprite bounding boxes (connected components)
import fs from 'node:fs';
import zlib from 'node:zlib';

export function decodePNG(buf) {
  if (buf.readUInt32BE(0) !== 0x89504e47) throw new Error('not a PNG');
  let pos = 8; let ihdr = null; const idat = []; let plte = null, trns = null;
  while (pos < buf.length) {
    const len = buf.readUInt32BE(pos); const type = buf.toString('ascii', pos + 4, pos + 8); const data = buf.subarray(pos + 8, pos + 8 + len);
    if (type === 'IHDR') ihdr = { w: data.readUInt32BE(0), h: data.readUInt32BE(4), depth: data[8], color: data[9], interlace: data[12] };
    else if (type === 'IDAT') idat.push(data);
    else if (type === 'PLTE') plte = data;
    else if (type === 'tRNS') trns = data;
    else if (type === 'IEND') break;
    pos += 12 + len;
  }
  if (!ihdr || ihdr.depth !== 8 || ihdr.interlace) throw new Error('unsupported PNG (need 8-bit non-interlaced)');
  const channels = { 0: 1, 2: 3, 3: 1, 4: 2, 6: 4 }[ihdr.color];
  const raw = zlib.inflateSync(Buffer.concat(idat));
  const { w, h } = ihdr; const stride = w * channels;
  const out = Buffer.alloc(w * h * 4); const prev = Buffer.alloc(stride); const cur = Buffer.alloc(stride);
  let p = 0;
  for (let y = 0; y < h; y++) {
    const filter = raw[p++]; raw.copy(cur, 0, p, p + stride); p += stride;
    for (let i = 0; i < stride; i++) {
      const a = i >= channels ? cur[i - channels] : 0, b = prev[i], c = i >= channels ? prev[i - channels] : 0;
      let v = cur[i];
      if (filter === 1) v += a; else if (filter === 2) v += b; else if (filter === 3) v += (a + b) >> 1;
      else if (filter === 4) { const pa = Math.abs(b - c), pb = Math.abs(a - c), pc = Math.abs(a + b - 2 * c); v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c; }
      cur[i] = v & 255;
    }
    for (let x = 0; x < w; x++) {
      const o = (y * w + x) * 4;
      if (ihdr.color === 6) { out[o] = cur[x * 4]; out[o + 1] = cur[x * 4 + 1]; out[o + 2] = cur[x * 4 + 2]; out[o + 3] = cur[x * 4 + 3]; }
      else if (ihdr.color === 2) { out[o] = cur[x * 3]; out[o + 1] = cur[x * 3 + 1]; out[o + 2] = cur[x * 3 + 2]; out[o + 3] = 255; }
      else if (ihdr.color === 3) { const idx = cur[x]; out[o] = plte[idx * 3]; out[o + 1] = plte[idx * 3 + 1]; out[o + 2] = plte[idx * 3 + 2]; out[o + 3] = trns && idx < trns.length ? trns[idx] : 255; }
      else if (ihdr.color === 0) { out[o] = out[o + 1] = out[o + 2] = cur[x]; out[o + 3] = 255; }
      else if (ihdr.color === 4) { out[o] = out[o + 1] = out[o + 2] = cur[x * 2]; out[o + 3] = cur[x * 2 + 1]; }
    }
    cur.copy(prev);
  }
  return { width: w, height: h, data: out };
}

/** Connected components of opaque pixels (8-neighbour, with `gap` tolerance) → bounding boxes. */
export function boxes(img, minAlpha = 1, gap = 1) {
  const { width: w, height: h, data } = img;
  const seen = new Uint8Array(w * h); const result = [];
  const solid = (x, y) => x >= 0 && y >= 0 && x < w && y < h && data[(y * w + x) * 4 + 3] >= minAlpha;
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    if (seen[y * w + x] || !solid(x, y)) continue;
    let minX = x, maxX = x, minY = y, maxY = y; const stack = [[x, y]]; seen[y * w + x] = 1; let count = 0;
    while (stack.length) {
      const [cx, cy] = stack.pop(); count++;
      minX = Math.min(minX, cx); maxX = Math.max(maxX, cx); minY = Math.min(minY, cy); maxY = Math.max(maxY, cy);
      for (let dy = -gap; dy <= gap; dy++) for (let dx = -gap; dx <= gap; dx++) {
        const nx = cx + dx, ny = cy + dy;
        if (solid(nx, ny) && !seen[ny * w + nx]) { seen[ny * w + nx] = 1; stack.push([nx, ny]); }
      }
    }
    result.push({ x: minX, y: minY, w: maxX - minX + 1, h: maxY - minY + 1, px: count });
  }
  return result.sort((a, b) => a.y - b.y || a.x - b.x);
}

if (process.argv[2] === 'boxes') {
  const img = decodePNG(fs.readFileSync(process.argv[3]));
  const list = boxes(img, Number(process.argv[4] ?? 1), Number(process.argv[5] ?? 1));
  console.log(`${img.width}x${img.height}, ${list.length} components`);
  for (const b of list) console.log(`${b.x},${b.y} ${b.w}x${b.h} (${b.px}px)`);
}

/** Minimal PNG encoder (8-bit RGBA, no filtering). `data` is a Uint8ClampedArray/Buffer of width*height*4. */
export function encodePNG(width, height, data) {
  const crcTable = new Int32Array(256);
  for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; crcTable[n] = c; }
  const crc = (buf) => { let c = -1; for (const b of buf) c = crcTable[(c ^ b) & 0xff] ^ (c >>> 8); return (c ^ -1) >>> 0; };
  const chunk = (type, body) => { const len = Buffer.alloc(4); len.writeUInt32BE(body.length); const tb = Buffer.concat([Buffer.from(type, 'ascii'), body]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(tb)); return Buffer.concat([len, tb, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(width, 0); ihdr.writeUInt32BE(height, 4); ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  const raw = Buffer.alloc((width * 4 + 1) * height);
  for (let y = 0; y < height; y++) { raw[y * (width * 4 + 1)] = 0; Buffer.from(data.buffer, data.byteOffset + y * width * 4, width * 4).copy(raw, y * (width * 4 + 1) + 1); }
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
}
