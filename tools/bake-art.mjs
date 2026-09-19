// Bakes the web build's art into the Unity project.
//
// The web game is the source of truth for how this game LOOKS as well as how it plays: 55 hand-made
// 832x1216 card illustrations with three outfits each, and pixel battle sprites built procedurally
// from the 0x72 tileset. Generating replacements produced worse art and broke the visual identity —
// same character, different face — so nothing here generates anything. It copies and it bakes.
//
//   node tools/bake-art.mjs cards      832x1216 illustrations -> 512x748 (both /4, so DXT applies)
//   node tools/bake-art.mjs sprites    9-frame 144x28 pixel strips, one per hero
//   node tools/bake-art.mjs sheets     the raw tilesets + the rect tables that index them
//   node tools/bake-art.mjs story      episode backdrops
//   node tools/bake-art.mjs all

import fs from 'node:fs';
import path from 'node:path';
import { install, loadImage, savePNG } from './canvas-shim.mjs';

const WEB = 'C:/Users/user/excel-heros';
const ART = 'Assets/ExcelHeroes/Resources/Art';
const DATA = 'Assets/ExcelHeroes/Resources/Data';

const mkdir = (d) => fs.mkdirSync(d, { recursive: true });
const webPath = (...p) => path.join(WEB, ...p);

install();

// ---------------------------------------------------------------- sprites --
async function bakeSprites() {
  const { HEROES } = await import(`file://${webPath('src/data/heroes.js')}`);
  const { HERO_MAP } = await import(`file://${webPath('src/data/packSprites.js')}`);
  const { buildHeroStrip } = await import(`file://${webPath('src/data/heroSkins.js')}`);
  const { buildDollStrip, hasDoll } = await import(`file://${webPath('src/data/dollSprites.js')}`);

  const sheet = loadImage(webPath('assets/sprites/0x72/sheet.png'));
  const out = path.join(ART, 'Sprites');
  fs.rmSync(out, { recursive: true, force: true });
  mkdir(out);

  let dolls = 0, recoloured = 0, skipped = 0;
  for (const def of HEROES) {
    const m = HERO_MAP[def.id];
    if (!m) { skipped++; console.warn(`  no sprite mapping: ${def.id}`); continue; }
    // Hand-drawn dolls win over the recoloured base — same precedence as heroStrip() in the web.
    const doll = hasDoll(def.id) ? buildDollStrip(def.id, null, def.grade) : null;
    const strip = doll ?? buildHeroStrip(sheet, def, m);
    doll ? dolls++ : recoloured++;
    savePNG(path.join(out, `${def.id}.png`), strip);
  }
  console.log(`sprites: ${dolls} paper dolls + ${recoloured} recoloured bases${skipped ? `, ${skipped} unmapped` : ''} -> ${out}`);
}

// ------------------------------------------------------------------ sheets --
/** The tilesets go in whole and get sliced at runtime — 512x512 costs less than the sprites cut out of it. */
function bakeSheets() {
  const out = path.join(ART, 'Sheets');
  mkdir(out);
  fs.copyFileSync(webPath('assets/sprites/0x72/sheet.png'), path.join(out, 'dungeon.png'));
  fs.copyFileSync(webPath('assets/sprites/tiny-creatures/tilemap_packed.png'), path.join(out, 'tiny.png'));
  for (const [src, dst] of [['assets/sprites/0x72/LICENSE.txt', 'dungeon.LICENSE.txt'],
                            ['assets/sprites/tiny-creatures/LICENSE.txt', 'tiny.LICENSE.txt']])
    fs.copyFileSync(webPath(src), path.join(out, dst));
  console.log(`sheets: dungeon.png + tiny.png (+ licences) -> ${out}`);
}

// ------------------------------------------------------------------- cards --
/**
 * 832x1216 is more than a phone needs and, at 165 files, more than a phone has. Half-size keeps a
 * card crisp when it fills the screen and cuts the set to a quarter of the memory.
 *
 * The exact numbers matter: DXT compresses in 4x4 blocks, so a dimension that is not a multiple of
 * 4 silently falls back to uncompressed RGBA32. 512x748 preserves the aspect ratio to within 0.05%
 * and both sides divide by 4.
 */
function bakeCards() {
  const [DW, DH] = [512, 748];
  const src = webPath('assets/cards');
  const out = path.join(ART, 'Cards');
  fs.rmSync(out, { recursive: true, force: true });
  mkdir(out);

  const files = fs.readdirSync(src).filter((f) => f.endsWith('.png'));
  let n = 0, bytes = 0;
  for (const f of files) {
    const img = loadImage(path.join(src, f));
    const dst = new Uint8ClampedArray(DW * DH * 4);
    for (let y = 0; y < DH; y++) {
      const sy = Math.min(img.height - 1, Math.floor((y * img.height) / DH));
      for (let x = 0; x < DW; x++) {
        const sx = Math.min(img.width - 1, Math.floor((x * img.width) / DW));
        const s = (sy * img.width + sx) * 4, d = (y * DW + x) * 4;
        dst[d] = img.data[s]; dst[d + 1] = img.data[s + 1];
        dst[d + 2] = img.data[s + 2]; dst[d + 3] = img.data[s + 3];
      }
    }
    savePNG(path.join(out, f), { width: DW, height: DH, data: dst });
    bytes += fs.statSync(path.join(out, f)).size;
    n++;
  }
  console.log(`cards: ${n} illustrations at ${DW}x${DH}, ${(bytes / 1048576).toFixed(0)} MB on disk -> ${out}`);
}

// ------------------------------------------------------------------- story --
function bakeStory() {
  const src = webPath('assets/story');
  const out = path.join(ART, 'Story');
  mkdir(out);
  let n = 0;
  for (const f of fs.readdirSync(src)) { fs.copyFileSync(path.join(src, f), path.join(out, f)); n++; }
  console.log(`story: ${n} backdrops -> ${out}`);
}

const cmd = process.argv[2] ?? 'all';
if (cmd === 'sprites' || cmd === 'all') await bakeSprites();
if (cmd === 'sheets' || cmd === 'all') bakeSheets();
if (cmd === 'cards' || cmd === 'all') bakeCards();
if (cmd === 'story' || cmd === 'all') bakeStory();
