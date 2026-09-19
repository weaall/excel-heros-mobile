// Prompt construction for the Gemini art pipeline.
//
// Art direction (locked):
//   1. The CHARACTER carries the card. Backgrounds stay flat and quiet so the silhouette reads
//      instantly at thumbnail size in a 10-pull grid.
//   2. Every hero wears a HALO - a floating ring above the head, themed to their division. It is
//      the signature read of the game and the main rarity tell.
//   3. One design per hero. Motion comes from same-pose variants (see posePrompt), never from a
//      redesign, so the detail screen can cross-fade between frames.

export const STYLE = [
  'Style: premium Korean mobile-gacha character art (Blue Archive tier),',
  'clean confident lineart, crisp cel shading with two shadow steps, soft rim light,',
  'saturated character colours against a quiet background, high finish, no sketchiness.',
].join(' ');

export const NEGATIVE = [
  'Absolutely NO text, NO letters, NO numbers, NO logos, NO watermark, NO signature, NO UI frame,',
  'NO border, NO card frame, NO speech bubbles, NO captions anywhere in the image.',
  'No busy background, no detailed scenery, no crowds, no furniture clutter, no cityscape.',
  'No extra limbs, no duplicated faces, no cropped head, no cropped halo.',
].join(' ');

/** The signature read. One per division so a player can tell a tech card from a finance card at a glance. */
const HALO = {
  admin:   'a slim brushed-steel ring, slate blue, with tiny paper-clip and document shapes orbiting it slowly',
  ops:     'a sturdy woven green ring like a safety cord, with small shield and leaf shapes set along it',
  people:  'a warm orange ring with small heart and approval-stamp glyphs set into it like gemstones',
  finance: 'a heavy polished gold ring with coin edges and fine tick marks like a ruler around its rim',
  tech:    'a glowing cyan ring made of circuit traces and bracket shapes, with a faint scanline sweeping around it',
  market:  'a vivid red ring with a rising arrow chasing around it and small graph spikes along the outside',
  exec:    'a double ring, an inner gold band and an outer violet band, with a crown silhouette rising from the top',
};

/** Rarity is told through the halo and the light on the character, not through background detail. */
const GRADE_HALO_POWER = {
  D: 'the halo is thin, matte and barely glowing',
  C: 'the halo glows gently and a few motes drift off it',
  B: 'the halo glows clearly, slowly rotating, shedding small light fragments',
  A: 'the halo is thick and bright, double-layered, casting visible light down onto the hair and shoulders',
  S: 'the halo is enormous and radiant, multi-layered with a second counter-rotating ring, pouring volumetric light down over the whole figure',
};

/** Deliberately plain. Flat colour plus a hint of grid, nothing to compete with the character. */
const GRADE_BG = {
  D: 'flat desaturated slate-grey backdrop, a very faint darker spreadsheet grid, soft vignette',
  C: 'flat muted sage-green backdrop, a very faint darker spreadsheet grid, soft vignette',
  B: 'flat dusty blue backdrop, a very faint darker spreadsheet grid, soft vignette',
  A: 'flat deep violet backdrop, a faint luminous spreadsheet grid, soft radial glow behind the head',
  S: 'flat deep navy backdrop, a faint golden spreadsheet grid, strong radial golden glow behind the figure',
};

const ROLE_POSE = {
  tank:   'planted wide stance, one arm forward as if bracing a door shut, steady unmoving expression',
  melee:  'forward-leaning ready stance, weight on the front foot, caught mid-step',
  ranged: 'poised upright, one arm extended forward at shoulder height as if presenting a figure',
  healer: 'open welcoming posture, one hand raised palm-up with a soft glow above it, calm expression',
};

const GENDER = { M: 'a Korean man', F: 'a Korean woman' };

/** Card illustration - the hero shot the gacha reveal lands on. */
export function cardPrompt(h, { hasRef }) {
  const who = GENDER[h.gender] ?? 'a Korean office worker';
  const identity = hasRef
    ? 'Redraw the character from the reference image. Keep the SAME identity: same face, hairstyle, hair colour, skin tone, outfit design, outfit colours and accessories. Only the rendering quality, the pose, the halo and the background change.'
    : 'Design a character matching the description below.';
  const accents = [h.colorHair, h.colorBody, h.colorPants, h.colorAccent].filter(Boolean).join(', ')
    || 'corporate navy and warm grey';
  return [
    identity,
    '',
    'SUBJECT: ' + who + ', ' + h.nick + ' - ' + h.bio + ' Department: ' + h.dept + '.',
    'Wearing realistic modern Korean office attire suited to that job, not fantasy armour, not a costume.',
    'POSE: knee-up three-quarter view facing the camera, ' + (ROLE_POSE[h.role] ?? ROLE_POSE.melee) + '.',
    '',
    'HALO (most important element): floating horizontally above the head, clearly separated from the hair,',
    (HALO[h.division] ?? HALO.admin) + '. ' + GRADE_HALO_POWER[h.grade] + '.',
    'The halo must be fully inside the frame with clear space above it.',
    '',
    'BACKGROUND (keep it quiet): ' + (GRADE_BG[h.grade] ?? GRADE_BG.D) + '. Nothing else in the background at all.',
    'Character colour accents: ' + accents + '.',
    '',
    STYLE,
    'Vertical portrait composition, character centred, figure from the knees up, generous headroom for the halo.',
    NEGATIVE,
  ].join('\n');
}

/**
 * Same design, small movement - the frames the detail screen cross-fades between so the card feels
 * alive without a rigged Live2D model. Everything except the named change must match frame for frame.
 */
export const POSES = {
  blink:   'both eyes gently closed as in a natural blink',
  talk:    'mouth open mid-word in a friendly expression, eyes unchanged',
  breathe: 'chest and shoulders raised very slightly as in an inhale, hair and halo drifted up a hair',
  smile:   'a warmer brighter smile and slightly narrowed happy eyes',
};

export function posePrompt(h, poseKey) {
  return [
    'Produce a near-identical variant of the character in the reference image.',
    'CRITICAL: identity, hairstyle, outfit, colours, halo design, lighting, background, camera framing,',
    'body pose and scale must match the reference EXACTLY. The two images will be cross-faded, so any',
    'drift in position or colour will read as a glitch.',
    'Change ONLY this: ' + POSES[poseKey] + '.',
    STYLE,
    NEGATIVE,
  ].join('\n');
}

/** Rarity card frame - flat black interior so it can be cut to alpha at import time. */
export function framePrompt(grade, colour) {
  return [
    'A single vertical trading-card FRAME only: an empty decorative border with a completely flat pure black interior.',
    'Theme: a spreadsheet cell border that became a treasure frame. Corner ornaments look like cell drag-handles and formula brackets.',
    'Rarity tier ' + grade + '. ' + GRADE_HALO_POWER[grade] + ' - match that level of ornament. Frame metal colour: ' + colour + '.',
    'The frame occupies only the outer 10 percent of the image on all sides. Everything inside is pure flat black, empty.',
    'Outside the frame is also pure flat black. Clean vector-like game UI art, crisp edges, symmetrical left to right.',
    NEGATIVE,
  ].join('\n');
}

export const UI_PIECES = {
  gacha_bg: [
    'A vertical mobile game gacha summon screen background, deliberately simple.',
    'A deep navy void with a faint golden spreadsheet grid receding into the distance, a soft radial glow in the upper middle,',
    'a few slow motes of light. Nothing else. The centre must stay open and low contrast for the summon animation and UI.',
    'Painterly game-UI background art, calm, uncluttered.',
    NEGATIVE,
  ].join('\n'),
  home_bg: [
    'A vertical mobile game home screen background, deliberately simple.',
    'A warm cream and amber gradient with a faint translucent spreadsheet grid drifting across it and soft light bloom at the top.',
    'No furniture, no people, no room. The lower two thirds stay clean and low contrast so UI panels sit on top.',
    'Painterly game-UI background art, calm, inviting.',
    NEGATIVE,
  ].join('\n'),
  battle_bg: [
    'A vertical mobile game battle stage background, deliberately simple, side-on flat perspective.',
    'A muted teal office floor plane across the lower half with a clear empty band for characters to stand on,',
    'a dim upper half suggesting a collapsed ceiling, and cracks of glowing spreadsheet cells in the dark.',
    'Very low contrast, almost graphic. No furniture clutter, no debris detail, nothing busy.',
    'Painterly game-UI background art.',
    NEGATIVE,
  ].join('\n'),
  logo: [
    'A mobile game logo mark, icon only, no words: a glowing spreadsheet cell shaped like a shield,',
    'with a halo ring floating above it, gold on deep navy, bold and readable at small size,',
    'clean vector game-logo style, centred on a pure flat black background.',
    NEGATIVE,
  ].join('\n'),
};
