// Prompt construction for the Gemini art pipeline.
//
// Art direction (locked):
//   1. The CHARACTER carries the card. Backgrounds stay flat and quiet so the silhouette reads
//      instantly at thumbnail size in a 10-pull grid.
//   2. Every hero wears a HALO - a floating ring above the head, themed to their division. It is
//      the signature read of the game and the main rarity tell.
//   3. One design per hero. Motion comes from same-pose variants (see posePrompt), never from a
//      redesign, so the detail screen can cross-fade between frames.

/**
 * The look, described rather than referenced.
 *
 * The ask was "make it taste like Blue Archive". I am not feeding the model Nexon's own character
 * art to copy from — that is someone else's copyrighted work, and a game that might ship should not
 * be built on derivatives of it. What actually moves these models is a specific written description
 * anyway, so this is that description: the rendering decisions that produce the look, named one by
 * one, instead of a picture to imitate.
 */
export const STYLE = [
  'Style: premium Korean mobile-gacha character art, the Blue Archive school of anime illustration.',
  'Crisp uniform-weight lineart that thins at the ends of a stroke. Cel shading in exactly two steps',
  'with hard-edged shadow shapes, never a soft airbrushed gradient on skin or cloth.',
  'A bright key light from the front and slightly above, a coloured rim light along the far shoulder',
  'and jaw picking the figure off the background, and a warm bounce under the chin.',
  'Large clean eyes with a bright highlight, a small secondary highlight, and a soft colour gradient',
  'in the iris; a light blush across the nose. Hair drawn in a few big confident clumps with a glossy',
  'band across the top, not in thousands of strands.',
  'Clothing folds simplified to a handful of decisive creases that follow the pose.',
  'Saturated character colours against a quiet flat background. High finish, no sketchiness,',
  'no visible brush texture, no painterly rendering, no photorealism, no 3D render look.',
].join(' ');

export const NEGATIVE = [
  'Absolutely NO text, NO letters, NO numbers, NO logos, NO watermark, NO signature, NO UI frame,',
  'NO border, NO card frame, NO speech bubbles, NO captions anywhere in the image.',
  'No busy background, no detailed scenery, no crowds, no furniture clutter, no cityscape.',
  'No extra limbs, no duplicated faces, no cropped head, no cropped halo.',
].join(' ');

/**
 * The halo is LIGHT, not hardware.
 *
 * The first pass built it out of objects — brushed steel, coin edges, orbiting paper clips — and
 * every card came back wearing a prop. What the halo is meant to say is that this person has stayed
 * late fixing someone else's spreadsheet, and a machined ring says the opposite. So: a ring of soft
 * light, its colour set by the division, thin enough that it never competes with the face.
 */
const HALO = {
  admin:   'cool slate-blue light',
  ops:     'soft green light',
  people:  'warm amber light',
  finance: 'pale gold light',
  tech:    'clear cyan light',
  market:  'warm red-orange light',
  exec:    'white-gold light with a faint violet inner edge',
};

/**
 * Rarity climbs through the halo, but quietly. Even at S it is a ring of light over someone's head,
 * not a set piece — the instruction that keeps mattering is that it must not dominate the face.
 */
const GRADE_HALO_POWER = {
  D: 'a thin, faint ring of light, barely brighter than the air around it',
  C: 'a soft clean ring of light with a gentle bloom',
  B: 'a clear ring of light, slightly brighter at its edges, a few small motes drifting from it',
  A: 'a luminous ring of light with a faint second ring just inside it, casting a soft glow on the hair',
  S: 'a radiant ring of light with a slow inner and outer ring, shedding fine light particles and laying a warm glow across the hair and shoulders',
};

/** Deliberately plain. Flat colour plus a hint of grid, nothing to compete with the character. */
const GRADE_BG = {
  D: 'flat desaturated slate-grey backdrop, a very faint darker spreadsheet grid, soft vignette',
  C: 'flat muted sage-green backdrop, a very faint darker spreadsheet grid, soft vignette',
  B: 'flat dusty blue backdrop, a very faint darker spreadsheet grid, soft vignette',
  A: 'flat deep violet backdrop, a faint luminous spreadsheet grid, soft radial glow behind the head',
  S: 'flat deep navy backdrop, a faint golden spreadsheet grid, strong radial golden glow behind the figure',
};

/**
 * A pose is a moment out of a movement, not a stance held for a photograph.
 *
 * The first pass keyed the pose off the role alone, which produced ten cards doing the same thing:
 * torso twisted, one arm flung out to the right. A role is four buckets across fifty-five people,
 * so it can only ever give four poses. The bank below is picked per character, with the role
 * deciding which half of the bank it draws from, so two tanks brace differently.
 */
const POSE_BANK = {
  tank: [
    'shoulder-charging something off-frame, front arm braced across the chest, head down and eyes up',
    'planted low with both arms spread wide to block, knees bent, looking off to one side',
    'turning to shield someone behind them, one arm thrown back protectively, chin over the shoulder',
    'rising out of a crouch with a fist clenched at the hip, coat still settling from the movement',
  ],
  melee: [
    'mid-lunge with the body coiled low, one arm cocked back to strike, hair thrown forward',
    'landing from a jump, one knee up, one hand touching down, looking up past the camera',
    'spinning through a turn, jacket flaring, arms crossed tight against the body',
    'caught at the top of a downward swing, arms overhead, weight on the back foot',
  ],
  ranged: [
    'leaning back and away while aiming something forward and down, head tilted, one eye narrowed',
    'twisting at the waist to look behind, one arm still pointing the way they came from',
    'crouched low with the forearms up, glancing sideways at the camera',
    'stepping back with the hips angled away and the shoulders turned in, head lowered, eyes up',
  ],
  healer: [
    'kneeling on one knee with both hands cupped low, light pooling in them, eyes closed',
    'reaching up and out with one hand as if catching something falling, body arched back',
    'half-turned away with a hand pressed to the chest, looking back over the shoulder',
    'seated on an unseen edge, legs crossed, leaning forward on one arm, chin tucked',
  ],
};

/**
 * On top of the pose, a per-character attitude so a row of ten pulls is not ten variations of one
 * mood. Both are chosen by hashing the id, so a hero's pose and mood never change between runs.
 */
const ATTITUDE = [
  'confident and fired up, grinning through the effort',
  'coolly composed, eyes half-lowered, faintly amused',
  'sharp and challenging, chin raised, meeting the viewer sideways',
  'poised and elegant, a slight sensual arch through the back and hip',
  'fierce and shouting, full of fight',
  'quietly focused, lips pressed, entirely absorbed in the task',
  'playful and teasing, head tilted, glancing back over the shoulder',
  'weary but unbroken, a tired half-smile',
];

const hash = (s) => { let h = 0; for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0; return h; };
const poseFor = (id, role) => { const bank = POSE_BANK[role] ?? POSE_BANK.melee; return bank[hash(id) % bank.length]; };
const attitudeFor = (id) => ATTITUDE[hash(id + '!') % ATTITUDE.length];

const GENDER = { M: 'a Korean man', F: 'a Korean woman' };

/** Card illustration - the hero shot the gacha reveal lands on. */
export function cardPrompt(h, { hasRef }) {
  const who = GENDER[h.gender] ?? 'a Korean office worker';
  const identity = hasRef
    ? 'Redraw the character from the reference image. Keep the SAME identity: same face, hairstyle, hair colour, skin tone, outfit design, outfit colours and accessories. Only the rendering quality, the pose, the framing, the halo and the background change — and those change completely.'
    : 'Design a character matching the description below.';
  const accents = [h.colorHair, h.colorBody, h.colorPants, h.colorAccent].filter(Boolean).join(', ')
    || 'corporate navy and warm grey';
  return [
    // The crop leads, and is repeated at the end. Stated once in the middle of the prompt it was
    // simply ignored: the model kept pulling back to a full body with the feet in, which is the
    // default for a character illustration and the reason every face came out small.
    'FRAMING (decide this first): a COWBOY SHOT — the bottom edge of the image cuts straight across',
    'the THIGHS. Head to mid-thigh only. No feet, no shoes, no floor, no full body. The head is large,',
    'roughly one fifth of the image height, and the figure fills the frame from edge to edge.',
    '',
    identity,
    '',
    'SUBJECT: ' + who + ', ' + h.nick + ' - ' + h.bio + ' Department: ' + h.dept + '.',
    'Wearing realistic modern Korean office attire suited to that job, not fantasy armour, not a costume.',
    'POSE (within that crop): ' + poseFor(h.id, h.role) + '.',
    'ATTITUDE: ' + attitudeFor(h.id) + '.',
    'The character must NOT stare straight down the lens — the head is turned, tilted or looking',
    'past the camera. A dead-on symmetrical pose is wrong for every card.',
    'Render it as a single frame paused out of that movement: the limbs at an angle to the camera,',
    'strong foreshortening, hair and clothing still carrying the motion. 2D illustration throughout —',
    'cel-shaded anime art, not a 3D render — but with the depth and dynamism of a 3D action shot.',
    '',
    'HALO: a ring of pure light floating above the head, clearly separated from the hair, tilted with',
    'the head rather than pinned flat to the camera. Colour: ' + (HALO[h.division] ?? HALO.admin) + '.',
    GRADE_HALO_POWER[h.grade] + '.',
    'It is made of light only — never metal, never a solid object, nothing orbiting it, no attached',
    'props or symbols. It is a quiet accent, not the subject: it must never outshine the face.',
    'The halo must be fully inside the frame with clear space above it.',
    '',
    'BACKGROUND (keep it quiet): ' + (GRADE_BG[h.grade] ?? GRADE_BG.D) + '. Nothing else in the background at all.',
    'Character colour accents: ' + accents + '.',
    '',
    STYLE,
    'Vertical composition. Check the crop once more: cowboy shot, cut at the thighs, no feet in frame,',
    'head large and near the top with just enough room for the halo above it.',
    'The face is LARGE in frame, clearly lit and unobstructed — front-lit, never lost in shadow.',
    'Avoid the stock pose of one straight arm thrust out to the side; every card must read differently.',
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

/**
 * Battle sprite. The lane battle currently draws cropped card portraits in circles, which reads as
 * placeholder art the moment anything moves. A chibi standing on a flat key colour can be cut to
 * alpha at import and put on the lane properly.
 *
 * The key colour has to be one that never appears in the character: pure magenta is the standard
 * choice because no skin, hair or office wear lands near it.
 */
export function spritePrompt(h, { hasRef }) {
  const who = GENDER[h.gender] ?? 'a Korean office worker';
  const identity = hasRef
    ? 'Redraw the character from the reference image as a chibi. Keep the identity readable: same hair colour and shape, same outfit colours, same accessories and the same halo.'
    : 'Design a chibi character matching the description below.';
  return [
    identity,
    '',
    'CHIBI GAME SPRITE. Two-heads-tall proportions, big head, small body, simple readable shapes.',
    'Full body including both feet. Standing battle-ready pose, three-quarter view facing RIGHT.',
    `SUBJECT: ${who}, ${h.nick}. Department: ${h.dept}. Modern Korean office attire.`,
    `Keep the floating halo above the head, small and simple.`,
    '',
    'BACKGROUND: the entire background is one FLAT PURE MAGENTA (#FF00FF) with absolutely no',
    'gradient, texture, shadow, glow or vignette. The character must not use magenta anywhere.',
    'No ground shadow, no platform, no base — the character floats on flat magenta.',
    '',
    'Clean thick outlines, flat cel shading, bright saturated colours, readable at 120 pixels tall.',
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

/**
 * 스킨 — the same person in different clothes.
 *
 * NOT RUN by default any more: the cast is one illustration per character, with the motion coming
 * from ArtMotion rather than from a second drawing. Kept because the command still works and the
 * web build does ship 캐주얼 / 정장 outfits, so this is what to run if they come back.
 */
const SKIN_OUTFIT = {
  casual: [
    'Weekend clothes: a relaxed modern Korean casual outfit suited to their personality —',
    'oversized knit, hoodie, denim, a light jacket, whatever fits who they are. Comfortable, not scruffy.',
    'Keep any accessory that identifies them (glasses, earrings, a hair clip, the lanyard if it is theirs).',
  ].join(' '),
  formal: [
    'Formal wear: a sharply tailored suit, dress or gown in their own colours — the outfit for a',
    'board presentation or a company dinner. Immaculate, structured, a little more dramatic than daywear.',
  ].join(' '),
};

export function skinPrompt(h, skin) {
  return [
    'FRAMING (decide this first): a COWBOY SHOT — the bottom edge of the image cuts straight across',
    'the THIGHS. Head to mid-thigh only. No feet, no shoes, no floor, no full body. The head is large,',
    'roughly one fifth of the image height, and the figure fills the frame from edge to edge.',
    '',
    'Redraw the character in the reference image wearing different clothes. Keep the SAME identity:',
    'same face, same hairstyle and hair colour, same skin tone, same eye colour, same build.',
    'The reference is the definitive look of this person — a stranger must not appear.',
    '',
    (SKIN_OUTFIT[skin] ?? SKIN_OUTFIT.casual),
    '',
    'POSE: a different moment from the reference, and a relaxed one — this is them off duty rather',
    'than mid-fight. ' + attitudeFor(h.id + skin) + '. The head is turned or tilted, never staring',
    'straight down the lens.',
    '',
    'HALO: keep the ring of light above the head, same colour and strength as the reference.',
    'Light only — never metal, never a solid object, and never brighter than the face.',
    '',
    'BACKGROUND (keep it quiet): ' + (GRADE_BG[h.grade] ?? GRADE_BG.D) + '. Nothing else at all.',
    '',
    STYLE,
    'The face is LARGE in frame, clearly lit and unobstructed — front-lit, never lost in shadow.',
    NEGATIVE,
  ].join('\n');
}
