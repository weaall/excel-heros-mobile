// Minimal Gemini REST client for the Excel Heroes art/design pipeline.
// The key is read from tools/.env.local (gitignored) or the GEMINI_API_KEY env var —
// it is never written into generated assets or committed.
import { readFileSync, existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const BASE = 'https://generativelanguage.googleapis.com/v1beta';

export function apiKey() {
  if (process.env.GEMINI_API_KEY) return process.env.GEMINI_API_KEY;
  const envFile = join(HERE, '.env.local');
  if (existsSync(envFile)) {
    const m = readFileSync(envFile, 'utf8').match(/^GEMINI_API_KEY=(.+)$/m);
    if (m) return m[1].trim();
  }
  throw new Error('GEMINI_API_KEY missing — put it in tools/.env.local');
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** POST :generateContent with retry on 429/5xx. The key rides in a header, never the URL. */
async function call(model, body, { tries = 5 } = {}) {
  let lastErr;
  for (let attempt = 0; attempt < tries; attempt++) {
    if (attempt) await sleep(Math.min(30000, 1500 * 2 ** attempt) + Math.random() * 500);
    let res;
    try {
      res = await fetch(`${BASE}/models/${model}:generateContent`, {
        method: 'POST',
        headers: { 'content-type': 'application/json', 'x-goog-api-key': apiKey() },
        body: JSON.stringify(body),
      });
    } catch (e) { lastErr = e; continue; }
    if (res.ok) return res.json();
    const text = await res.text().catch(() => '');
    lastErr = new Error(`${model} ${res.status}: ${text.slice(0, 400)}`);
    if (res.status !== 429 && res.status < 500) throw lastErr; // 4xx other than rate limit is fatal
  }
  throw lastErr;
}

/** Plain text generation. Returns the concatenated text parts. */
export async function genText(prompt, { model = 'gemini-3.1-pro-preview', system, temperature = 0.9, json = false } = {}) {
  const body = {
    contents: [{ role: 'user', parts: [{ text: prompt }] }],
    generationConfig: { temperature, ...(json ? { responseMimeType: 'application/json' } : {}) },
  };
  if (system) body.systemInstruction = { parts: [{ text: system }] };
  const data = await call(model, body);
  return (data.candidates?.[0]?.content?.parts ?? []).map((p) => p.text ?? '').join('').trim();
}

/**
 * Image generation / editing. `refs` are { data: base64, mimeType } reference images —
 * passing the existing card art keeps a new pose or outfit recognisably the same character.
 * Returns an array of { buffer, mimeType }.
 */
export async function genImage(prompt, { model = 'gemini-3-pro-image', refs = [], aspectRatio = '3:4', temperature = 1.0 } = {}) {
  const parts = [...refs.map((r) => ({ inlineData: { mimeType: r.mimeType ?? 'image/png', data: r.data } })), { text: prompt }];
  const body = {
    contents: [{ role: 'user', parts }],
    generationConfig: { temperature, responseModalities: ['TEXT', 'IMAGE'], imageConfig: { aspectRatio } },
  };
  let data;
  try {
    data = await call(model, body);
  } catch (e) {
    // Older image models reject imageConfig — retry without it rather than losing the job.
    if (!/imageConfig|aspectRatio|INVALID_ARGUMENT/i.test(String(e.message))) throw e;
    delete body.generationConfig.imageConfig;
    data = await call(model, body);
  }
  const out = [];
  for (const p of data.candidates?.[0]?.content?.parts ?? []) {
    if (p.inlineData?.data) out.push({ buffer: Buffer.from(p.inlineData.data, 'base64'), mimeType: p.inlineData.mimeType ?? 'image/png' });
  }
  if (!out.length) throw new Error(`no image returned (finish=${data.candidates?.[0]?.finishReason ?? '?'})`);
  return out;
}

export function refFromFile(path) {
  const ext = path.toLowerCase().split('.').pop();
  const mimeType = ext === 'jpg' || ext === 'jpeg' ? 'image/jpeg' : ext === 'webp' ? 'image/webp' : 'image/png';
  return { data: readFileSync(path).toString('base64'), mimeType };
}

export function writeOut(path, buffer) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, buffer);
  return path;
}

/** Run `jobs` (array of async thunks) with a concurrency cap; never rejects — returns per-job results. */
export async function pool(jobs, limit = 4, onDone = () => {}) {
  const results = new Array(jobs.length);
  let next = 0, done = 0;
  await Promise.all(Array.from({ length: Math.min(limit, jobs.length) }, async () => {
    while (next < jobs.length) {
      const i = next++;
      try { results[i] = { ok: true, value: await jobs[i]() }; }
      catch (e) { results[i] = { ok: false, error: e }; }
      onDone(++done, jobs.length, results[i]);
    }
  }));
  return results;
}
