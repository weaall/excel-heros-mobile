// Put every screen in front of Gemini at once and ask for a design system, not a list of opinions.
//
//   node tools/design-audit.mjs <strip.png>
//
// Writes docs/design/system.md. The key stays in tools/.env.local.
import { genText, refFromFile } from './gemini.mjs';
import { mkdirSync, writeFileSync, readFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const shot = process.argv[2];
if (!shot) { console.error('usage: node tools/design-audit.mjs <strip.png>'); process.exit(1); }

const OUT = resolve(HERE, '..', 'docs', 'design');
mkdirSync(OUT, { recursive: true });

const uss = readFileSync(resolve(HERE, '..', 'Assets/ExcelHeroes/UI/App.uss'), 'utf8');

const prompt = `Attached is a strip of five screens from the same Korean mobile game, side by side, in
order: the battle home, the roster, the summon banner, the daily tasks, and a character detail modal.
Portrait phone, 1080x2400, shown here at 486 wide each.

The game is disguised as Microsoft Excel for Android: the green app bar, the formula bar, the column
letters, the row gutter and the sheet tabs are deliberate and must stay. Everything inside the sheet
is the game and is fair game to redesign.

The problem is not any single screen. It is that each screen was styled separately, so there are
five different button treatments, four different card treatments and no shared spacing or type
scale. Here is the stylesheet, which shows that directly:

--- App.uss (${uss.length} chars, truncated) ---
${uss.slice(0, 22000)}
--- end ---

I do not want a list of opinions. I want a DESIGN SYSTEM I can implement as USS, in this order:

1. TOKENS. Exact values. Colour roles (surface, surface-raised, ink, ink-muted, line, accent,
   accent-ink, danger, and the Excel chrome greens), a spacing scale, a radius scale, and a type
   scale with the px size and weight for each role at 1080x2400. Name them like --ex-space-3.

2. BUTTONS. One system. Give me the variants that this game actually needs and, for each: height,
   horizontal padding, radius, font size and weight, fill, border, the disabled treatment, and the
   pressed treatment. Say explicitly which of the existing classes each variant replaces.

3. SURFACES. One card/panel treatment and one section-header treatment, with the exact values, and
   which existing classes they replace.

4. LAYOUT RHYTHM. Page padding, the gap between sections, the gap inside a card, and how a screen
   header is built. One rule each.

5. THE TEN WORST SPECIFIC THINGS in the attached screens, each with the fix in one sentence. Be
   blunt and name the element.

Constraints: Unity UI Toolkit USS, which is a CSS subset. No gradients, no box-shadow, no z-index,
no :nth-child, no attribute selectors, no \`border\` shorthand, no \`gap\`. Flexbox only. USS has no
custom properties in the CSS sense, but it does support \`--name: value\` variables on a selector and
\`var(--name)\` — assume those work. Elevation must be done with borders or a darker bottom border.

Be concrete enough that I can write the stylesheet from this without another decision.`;

const spec = await genText(prompt, {
  system: 'You are a senior mobile game UI designer who ships design systems. You are specific, you use exact numbers, and you never pad.',
  temperature: 0.7,
});

writeFileSync(resolve(OUT, 'system.md'), `# Excel Heroes — UI design system\n\n${spec}\n`, 'utf8');
console.log(`docs/design/system.md  (${spec.length} chars)`);
