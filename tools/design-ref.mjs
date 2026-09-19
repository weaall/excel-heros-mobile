// Ask Gemini for a design reference for one screen, then save both the written spec and a mockup.
//
// Used when a screen is visibly bad and the fix is a layout decision rather than a bug: the
// current screenshot goes in as a reference image, so the critique is of what is actually on
// screen rather than of a description of it.
//
//   node tools/design-ref.mjs <slug> <screenshot.png> "<what the screen is for>"
//
// Writes docs/design/<slug>.md and docs/design/<slug>-mock.png. The key stays in tools/.env.local.
import { genText, genImage, refFromFile, writeOut } from './gemini.mjs';
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const [slug, shot, brief] = process.argv.slice(2);
if (!slug || !shot) {
  console.error('usage: node tools/design-ref.mjs <slug> <screenshot.png> "<brief>"');
  process.exit(1);
}

const OUT = resolve(HERE, '..', 'docs', 'design');
mkdirSync(OUT, { recursive: true });

const CONTEXT = `
This is a Korean mobile gacha RPG (portrait phone, 1080x2400) disguised as Microsoft Excel for
Android — the joke is that you can play it at your desk at work. So the chrome is real Excel:
a green app bar, a formula bar, column letters, a row-number gutter, sheet tabs along the bottom.
The content inside the sheet is the game.

The visual target for the GAME content is Blue Archive: large character illustrations, confident
type hierarchy, coloured rarity accents, generous spacing, everything anchored rather than floating.
The Excel chrome stays plain and grey-green; the game content is allowed to be rich. The tension
between the two is the point, but right now the game content is simply small and cramped.

Constraints: Unity UI Toolkit (USS), which is a CSS subset — NO gradients, no box-shadow, no
z-index, no :nth-child. Flexbox only. Colour, borders, border-radius, background images, opacity
and transforms are available. So the design has to work with flat fills, borders and layered
elements.
`;

const spec = await genText(`${CONTEXT}

The screen attached is: ${brief ?? slug}.

Critique it honestly and specifically, then give me a concrete replacement layout. I want:

1. What is wrong, in order of how much it hurts. Be blunt and name the actual elements.
2. A replacement layout, as a nesting diagram with concrete pixel values at 1080x2400
   (heights, paddings, font sizes, border widths). Assume the sheet content area is about
   1010px wide and 1500px tall.
3. The type scale and the colour roles, as a short table.
4. Three specific things that would make it read as Blue Archive quality rather than as a
   placeholder.

Be concrete enough that someone can write the USS directly from it. No preamble.`,
  { system: 'You are a senior mobile game UI designer. You are specific, you use numbers, and you never pad.' });

writeFileSync(resolve(OUT, `${slug}.md`), `# ${slug} — design reference\n\n${spec}\n`, 'utf8');
console.log(`docs/design/${slug}.md  (${spec.length} chars)`);

// The mockup. Asked for as a flat UI render rather than a "poster", because what is needed is
// something to measure against, not something to admire.
const images = await genImage(`${CONTEXT}

Render a clean, flat UI mockup of the redesigned screen described below, as it would look on a
1080x2400 phone. Show the Excel chrome (green app bar, formula bar, column letters, row-number
gutter, four sheet tabs at the bottom) exactly as in the attached screenshot, and redesign only
the content between them. Korean labels. No annotations, no callouts, no device frame — just the
screen as the app would draw it.

${spec.slice(0, 4000)}`, { refs: [refFromFile(shot)], aspectRatio: '9:16' });

writeOut(resolve(OUT, `${slug}-mock.png`), images[0].buffer);
console.log(`docs/design/${slug}-mock.png  (${(images[0].buffer.length / 1024).toFixed(0)} KB)`);
