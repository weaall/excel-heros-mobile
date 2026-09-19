// Gemini art pipeline for Excel Heroes (Unity).
//   node tools/gen-art.mjs cards  [--only a,b] [--limit N] [--force] [--concurrency 4]
//   node tools/gen-art.mjs poses  [--only a,b] [--force]
//   node tools/gen-art.mjs frames [--force]
//   node tools/gen-art.mjs ui     [--force]
// Output goes straight into Assets/ExcelHeroes/Art/**. Existing files are skipped unless --force,
// so a failed run can just be re-run.
import { readFileSync, existsSync, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { genImage, refFromFile, writeOut, pool } from './gemini.mjs';
import { cardPrompt, posePrompt, spritePrompt, framePrompt, POSES, UI_PIECES } from './art-prompts.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, '..');
const WEB  = process.env.EXCEL_HEROES_WEB ?? resolve(ROOT, '..', 'excel-heros');
const ART  = join(ROOT, 'Assets', 'ExcelHeroes', 'Resources', 'Art');
const DATA = join(ROOT, 'Assets', 'ExcelHeroes', 'Resources', 'Data');

const argv = process.argv.slice(2);
const cmd  = argv[0] ?? 'help';
const flag = (name, def = null) => { const i = argv.indexOf(`--${name}`); return i === -1 ? def : argv[i + 1]; };
const has  = (name) => argv.includes(`--${name}`);
const FORCE = has('force');
const CONC  = Number(flag('concurrency', 3));
const ONLY  = flag('only') ? new Set(flag('only').split(',').map((s) => s.trim())) : null;
const LIMIT = flag('limit') ? Number(flag('limit')) : Infinity;

const heroes = JSON.parse(readFileSync(join(DATA, 'heroes.json'), 'utf8')).items;
const grades = JSON.parse(readFileSync(join(DATA, 'grades.json'), 'utf8')).items;
const gradeColour = Object.fromEntries(grades.map((g) => [g.id, g.color]));

/** The web build's AI card art, used as an identity reference so the Unity art is the same cast. */
function webRef(id) {
  for (const ext of ['png', 'jpg']) {
    const p = join(WEB, 'assets', 'cards', `${id}.${ext}`);
    if (existsSync(p)) return refFromFile(p);
  }
  return null;
}

const report = (label) => (done, total, r) =>
  console.log(`[${String(done).padStart(3)}/${total}] ${label} ${r.ok ? 'ok  ' + r.value : 'FAIL ' + r.error.message.slice(0, 140)}`);

async function cards() {
  let list = heroes.filter((h) => !ONLY || ONLY.has(h.id));
  list = list.filter((h) => FORCE || !existsSync(join(ART, 'Cards', `${h.id}.png`)));
  list = list.slice(0, LIMIT);
  console.log(`cards: ${list.length} to generate (concurrency ${CONC})`);
  const jobs = list.map((h) => async () => {
    const ref = webRef(h.id);
    const [img] = await genImage(cardPrompt(h, { hasRef: !!ref }), {
      model: 'gemini-3-pro-image', refs: ref ? [ref] : [], aspectRatio: '3:4',
    });
    writeOut(join(ART, 'Cards', `${h.id}.png`), img.buffer);
    return `${h.id} (${h.grade}) ${(img.buffer.length / 1024).toFixed(0)}KB${ref ? ' [ref]' : ''}`;
  });
  const res = await pool(jobs, CONC, report('card'));
  summarise(res);
}

/** Extra frames of the same pose so the detail screen can breathe/blink/talk. */
async function poses() {
  const list = heroes.filter((h) => (!ONLY || ONLY.has(h.id)) && existsSync(join(ART, 'Cards', `${h.id}.png`))).slice(0, LIMIT);
  const keys = Object.keys(POSES).filter((k) => k !== 'idle');
  console.log(`poses: ${list.length} heroes x ${keys.length} variants`);
  const jobs = [];
  for (const h of list) for (const k of keys) {
    const out = join(ART, 'Cards', `${h.id}__${k}.png`);
    if (!FORCE && existsSync(out)) continue;
    jobs.push(async () => {
      const [img] = await genImage(posePrompt(h, k), {
        model: 'gemini-3-pro-image', refs: [refFromFile(join(ART, 'Cards', `${h.id}.png`))], aspectRatio: '3:4', temperature: 0.4,
      });
      writeOut(out, img.buffer);
      return `${h.id}__${k}`;
    });
  }
  summarise(await pool(jobs, CONC, report('pose')));
}

/** Chibi battle sprites on a flat magenta key, cut to alpha by the Unity importer. */
async function sprites() {
  let list = heroes.filter((h) => !ONLY || ONLY.has(h.id));
  list = list.filter((h) => FORCE || !existsSync(join(ART, 'Sprites', `${h.id}.png`)));
  list = list.slice(0, LIMIT);
  console.log(`sprites: ${list.length} to generate (concurrency ${CONC})`);
  const jobs = list.map((h) => async () => {
    const ref = webRef(h.id) ?? (existsSync(join(ART, 'Cards', `${h.id}.png`))
      ? refFromFile(join(ART, 'Cards', `${h.id}.png`)) : null);
    const [img] = await genImage(spritePrompt(h, { hasRef: !!ref }), {
      model: 'gemini-3-pro-image', refs: ref ? [ref] : [], aspectRatio: '1:1', temperature: 0.8,
    });
    writeOut(join(ART, 'Sprites', `${h.id}.png`), img.buffer);
    return `${h.id} (${h.grade}) ${(img.buffer.length / 1024).toFixed(0)}KB`;
  });
  summarise(await pool(jobs, CONC, report('sprite')));
}

async function frames() {
  const jobs = grades.filter((g) => FORCE || !existsSync(join(ART, 'UI', `frame_${g.id}.png`))).map((g) => async () => {
    const [img] = await genImage(framePrompt(g.id, g.color), { model: 'gemini-3-pro-image', aspectRatio: '3:4' });
    writeOut(join(ART, 'UI', `frame_${g.id}.png`), img.buffer);
    return `frame_${g.id}`;
  });
  summarise(await pool(jobs, CONC, report('frame')));
}

async function ui() {
  const jobs = Object.entries(UI_PIECES)
    .filter(([name]) => FORCE || !existsSync(join(ART, 'UI', `${name}.png`)))
    .map(([name, prompt]) => async () => {
      const ratio = name === 'logo' ? '1:1' : '9:16';
      const [img] = await genImage(prompt, { model: 'gemini-3-pro-image', aspectRatio: ratio });
      writeOut(join(ART, 'UI', `${name}.png`), img.buffer);
      return name;
    });
  summarise(await pool(jobs, CONC, report('ui')));
}

function summarise(res) {
  const ok = res.filter((r) => r?.ok).length;
  console.log(`\ndone: ${ok}/${res.length} ok`);
  for (const r of res) if (r && !r.ok) console.log(`  FAIL ${r.error.message.slice(0, 200)}`);
  if (ok < res.length) process.exitCode = 1;
}

mkdirSync(join(ART, 'Cards'), { recursive: true });
mkdirSync(join(ART, 'Sprites'), { recursive: true });
mkdirSync(join(ART, 'UI'), { recursive: true });
const table = { cards, poses, sprites, frames, ui };
if (!table[cmd]) { console.log('usage: node tools/gen-art.mjs <cards|poses|frames|ui> [--only ids] [--limit N] [--force]'); process.exit(1); }
await table[cmd]();
