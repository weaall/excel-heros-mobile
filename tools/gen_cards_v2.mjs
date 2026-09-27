// Card illustrations v2 — FOR THE UNITY BUILD ONLY. The web repo (../excel-heros, the live game)
// is read, never written: its prompt builder (scripts/genCardsHF.mjs → prompt / negFor) supplies
// each character's description, and this script adds the v2 direction on top, calls the same
// Animagine XL 4.0 Hugging Face Space, and writes into THIS repo:
//   ArtSource/Cards_v2/<id>.png   the 832×1216 original
// (tools/cards_v2_import.py then puts the 512×748 copy into Resources/Art/Cards/<id>.png).
//
// The v2 direction (user, 2026-09-27): Blue Archive body proportions and prettier women across
// the all-female cast; at higher grades a little more glamour — tailored, form-fitting, alluring —
// never explicit, and always clearly ADULT women (the negatives keep child bodies and nsfw out).
// The protagonist (the main jobs) is redrawn too: a handsome young man.
//
//   node tools/gen_cards_v2.mjs ceo intern          # these ids
//   node tools/gen_cards_v2.mjs --all               # every hero and main job without a v2 png yet
//   node tools/gen_cards_v2.mjs --force ceo         # redo
//   SEED=7 node tools/gen_cards_v2.mjs ceo
//
// HF tokens are read BY NAME from the env file (HUGGING_FACE_API_KEY_*) into memory and rotated
// on quota errors; they are never printed or written anywhere.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const WEB = process.env.WEB_REPO ?? path.resolve(HERE, '..', '..', 'excel-heros');
const OUT = path.resolve(HERE, '..', 'ArtSource', 'Cards_v2');
const ENV_FILES = [process.env.ENV_FILE, 'C:\\Users\\minds\\Desktop\\mindsai_weaall.env', 'C:\\Users\\user\\Desktop\\mindsai_weaall.env'].filter(Boolean);

const web = (rel) => pathToFileURL(path.join(WEB, rel)).href;
const { HEROES, MAIN_JOBS } = await import(web('src/data/heroes.js'));
const { PROFILES } = await import(web('src/data/profiles.js'));
const { prompt, negFor } = await import(web('scripts/genCardsHF.mjs'));

// ---------------------------------------------------------------- the v2 direction

/** Every woman: an adult drawn to Blue Archive's body — slim, long-legged, small face, big eyes. */
// The cards are waist-up (the base negatives ban legs and feet), so the glamour is carried by the
// upper body: the fit, the collar, the face — leg tags would only fight the framing.
const F_BODY = 'beautiful adult woman, mature female, office lady, blue archive character design, slender figure, narrow waist, small face, large sparkling eyes, long eyelashes, glossy lips, pretty, cute, fashionable';
/** Glamour climbs with the grade — through the clothes and the attitude, never through exposure. */
const F_GRADE = {
  D: 'neat blouse, friendly smile',
  C: 'fitted blouse, cheerful cute smile',
  B: 'fitted tailored outfit, slightly open collar, collarbone, confident smile',
  A: 'stylish form-fitting outfit, open blazer, collarbone, medium breasts, alluring smile, elegant pose',
  S: 'glamorous elegant form-fitting dress suit, collarbone, medium breasts, confident alluring smile, graceful pose, radiant',
};
const M_BODY = 'handsome young man, bishounen, adult, slim fit, sharp jawline, bright eyes, blue archive character design, cool and kind expression';
/** Kept out of every card: child bodies, anything explicit. */
const V2_NEG = 'child, loli, shota, petite, flat chest, toddler, aged down, chibi, big head, short legs, nsfw, nude, nipples, underwear, panties, bra, see-through, cleavage cutout, huge breasts, gigantic breasts, lewd, suggestive pose, spread legs, upskirt, bad proportions, fat, muscular female';

function v2Prompt(def, pid) {
  const base = prompt(def, pid);
  const female = (PROFILES[pid] ?? {}).gender === 'F';
  const extra = female ? `${F_BODY}, ${F_GRADE[def.grade] ?? F_GRADE.D}` : M_BODY;
  // after the count tags (1girl, solo / 1boy, solo, male focus), before the rest
  const m = base.match(/^(1girl, solo|1boy, solo, male focus)(, )?/);
  return m ? `${m[1]}, ${extra}, ${base.slice(m[0].length)}` : `${extra}, ${base}`;
}
const v2Neg = (def) => `${negFor(def?.grade)}, ${V2_NEG}`;

// ---------------------------------------------------------------- Hugging Face (Gradio queue)

const SPACE = process.env.SPACE ?? 'asahina2k-animagine-xl-4-0';
const BASE = `https://${SPACE}.hf.space`;
function readTokens() {
  for (const f of ENV_FILES) {
    if (!fs.existsSync(f)) continue;
    const toks = fs.readFileSync(f, 'utf8').split(/\r?\n/).map((l) => l.trim()).filter((l) => /^HUGGING_FACE_API_KEY_\d+=/.test(l)).map((l) => l.split('=')[1].trim().replace(/^["']|["']$/g, '')).filter(Boolean);
    if (toks.length) return toks;
  }
  return [];
}
const POOL = [...new Set(readTokens()), '']; // '' = anonymous, last
let pool = 0;
const auth = () => (POOL[pool] ? { authorization: `Bearer ${POOL[pool]}` } : {});
const poolName = () => (POOL[pool] ? `token #${pool + 1}` : 'anonymous');
let fnIndex = null;

async function generate(text, neg, seed, width = 832, height = 1216) {
  const data = [text, neg, seed, width, height, 5, 28, 'Euler a', `${width} x ${height}`, 'Anim4gine', false, 0.55, 1.5, true];
  fnIndex ??= (await (await fetch(`${BASE}/config`, { headers: auth() })).json()).dependencies.findIndex((d) => d.api_name === 'generate');
  const session_hash = Math.random().toString(36).slice(2);
  const r = await fetch(`${BASE}/queue/join`, { method: 'POST', headers: { 'content-type': 'application/json', ...auth() }, body: JSON.stringify({ data, fn_index: fnIndex, session_hash, trigger_id: null }) });
  if (!r.ok) throw new Error(`join ${r.status}: ${(await r.text()).slice(0, 200)}`);
  const ev = await fetch(`${BASE}/queue/data?session_hash=${session_hash}`, { headers: auth(), signal: AbortSignal.timeout(300000) });
  const reader = ev.body.getReader(); const dec = new TextDecoder(); let buf = '', out = null;
  outer: while (true) {
    const { value, done } = await reader.read(); if (done) break;
    buf += dec.decode(value, { stream: true }); const lines = buf.split('\n'); buf = lines.pop();
    for (const line of lines) {
      if (!line.startsWith('data:')) continue;
      const m = JSON.parse(line.slice(5));
      if (m.msg === 'process_completed') { if (m.output?.error !== undefined || !m.success) throw new Error(`space: ${String(m.output?.error ?? 'failed').slice(0, 300)}`); out = m.output.data; break outer; }
      if (m.msg === 'close_stream') break outer;
    }
  }
  if (!out) throw new Error('stream closed without a result');
  const img = out[0]?.[0]?.image ?? out[0]?.[0];
  const urls = [img?.url, img?.path && `${BASE}/gradio_api/file=${img.path}`, img?.path && `${BASE}/file=${img.path}`].filter(Boolean);
  for (const u of urls) { const ir = await fetch(u); if (ir.ok) return Buffer.from(await ir.arrayBuffer()); }
  throw new Error('no image fetched');
}

// ---------------------------------------------------------------- batch

const args = process.argv.slice(2);
const force = args.includes('--force'); const all = args.includes('--all');
const ids = args.filter((a) => !a.startsWith('--'));
const defs = [...HEROES.map((h) => [h.id, h, h.id]), ...Object.values(MAIN_JOBS).map((j) => [j.id, j, 'main'])]
  .filter(([id]) => all || ids.includes(id));
const seed = Number(process.env.SEED ?? 11);
fs.mkdirSync(OUT, { recursive: true });
if (args.includes('--print')) { for (const [id, def, pid] of defs) console.log(`${id}\n  + ${v2Prompt(def, pid)}\n  - ${v2Neg(def)}\n`); process.exit(0); }
console.log(`${defs.length} cards, ${POOL.length} quota pools`);
let ok = 0;
for (const [id, def, pid] of defs) {
  const target = path.join(OUT, `${id}.png`);
  if (!force && fs.existsSync(target)) { console.log(`skip ${id}`); ok++; continue; }
  let soonest = Infinity;
  for (let attempt = 1; attempt <= 40; attempt++) {
    try {
      const buf = await generate(v2Prompt(def, pid), v2Neg(def), seed + id.length);
      fs.writeFileSync(target, buf);
      console.log(`ok   ${id} ${(buf.length / 1024).toFixed(0)} KB ${new Date().toLocaleTimeString()} (${poolName()})`); ok++; break;
    } catch (e) {
      const quota = /quota|event error/i.test(e.message), busy = /No GPU was available/i.test(e.message);
      const m = e.message.match(/Try again in (\d+):(\d\d):(\d\d)/);
      if (m) soonest = Math.min(soonest, ((+m[1]) * 3600 + (+m[2]) * 60 + (+m[3]) + 30) * 1000);
      let wait = 15000;
      if (quota || busy) {
        if (pool + 1 < POOL.length) { pool++; wait = 2000; }
        else { pool = 0; wait = quota ? (Number.isFinite(soonest) ? soonest : 240000) : 15000; soonest = Infinity; }
      }
      console.log(`retry ${id} (${attempt}): ${e.message.slice(0, 160)} — next ${poolName()} in ${(wait / 1000).toFixed(0)} s`);
      await new Promise((r) => setTimeout(r, wait));
    }
  }
  await new Promise((r) => setTimeout(r, 3000));
}
console.log(`done: ${ok}/${defs.length}`);
