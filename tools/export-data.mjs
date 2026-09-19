// Pull the canonical roster / balance / story out of the web build (vanilla ES modules, so we can
// just import them) and write flat JSON into Assets/ExcelHeroes/Resources/Data for the Unity client.
// The web repo stays the single source of truth for balance; re-run this after changing it there.
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const WEB = process.env.EXCEL_HEROES_WEB ?? resolve(HERE, '..', '..', 'excel-heros');
const OUT = resolve(HERE, '..', 'Assets', 'ExcelHeroes', 'Resources', 'Data');

const load = (rel) => import(pathToFileURL(join(WEB, rel)).href);

const heroes    = await load('src/data/heroes.js');
const pickup    = await load('src/data/pickup.js');
const quests    = await load('src/data/quests.js');
const extra     = await load('src/data/profilesExtra.js');
const profiles  = await load('src/data/profiles.js');
const divisions = await load('src/data/divisions.js');
const story     = await load('src/data/story.js');
const balance   = await load('src/config/balance.js');
const monsters  = await load('src/data/monsters.js').catch(() => ({}));

const { GRADES, ROLES, SKILLS, TRAITS, HEROES, MAIN_JOBS, MAIN_ID } = heroes;
const { PROFILES } = profiles;
const { DIVISIONS, SYNERGY, PERKS, divisionOf } = divisions;
const { BALANCE } = balance;

/** Flatten a keyed record into an array, folding the key in as `id`. */
const rows = (obj) => Object.entries(obj).map(([id, v]) => ({ id, ...v }));

const roster = HEROES.map((h) => {
  const p = PROFILES[h.id] ?? {};
  return {
    id: h.id,
    name: h.name,
    grade: h.grade,
    role: h.role,
    trait: h.trait,
    skillType: h.skill.type,
    skillPower: h.skill.power,
    skillName: h.skill.name ?? SKILLS[h.skill.type]?.name ?? h.skill.type,
    division: divisionOf(h.id),
    nick: p.nick ?? '',
    dept: p.dept ?? '',
    gender: p.gender ?? '',
    bio: p.bio ?? '',
    line: p.line ?? '',
    ult: p.ult ?? '',
    palette: h.palette ?? {},
    look: h.look ?? {},
  };
});

const mainJobs = rows(MAIN_JOBS).map((j) => ({
  id: j.id, tier: j.tier, grade: j.grade, name: j.name, title: j.title, track: j.track ?? '',
  role: j.role, trait: j.trait, skillType: j.skill.type, skillPower: j.skill.power,
  skillName: j.skill.name ?? SKILLS[j.skill.type]?.name ?? j.skill.type, next: j.next ?? [],
}));

// JsonUtility (Unity, no extra packages) cannot deserialise a top-level JSON array, so every
// list is wrapped as { items: [...] }. Keep this in sync with GameData.cs.
const wrap = (items) => ({ items });

const files = {
  'grades.json':    wrap(rows(GRADES).map((g) => ({ ...g, baseAtk: g.base.atk, baseHp: g.base.hp, base: undefined }))),
  'roles.json':     wrap(rows(ROLES).map((r) => ({ ...r, slot: undefined }))),
  'skills.json':    wrap(rows(SKILLS)),
  'traits.json':    wrap(rows(TRAITS)),
  'heroes.json':    wrap(roster.map((h) => ({
                      ...h,
                      palette: undefined, look: undefined,
                      colorHair: h.palette.H ?? '', colorBody: h.palette.B ?? '',
                      colorPants: h.palette.P ?? '', colorAccent: h.palette.W ?? '',
                    }))),
  'mainJobs.json':  { mainId: MAIN_ID, items: mainJobs },
  'divisions.json': { items: rows(DIVISIONS), perks: rows(PERKS),
                      pairAtk: SYNERGY.pair.atk, pairHp: SYNERGY.pair.hp,
                      trioAtk: SYNERGY.trio.atk, trioHp: SYNERGY.trio.hp,
                      balancedHp: SYNERGY.balanced.hp },
  // 일일 업무. Six of nine quests run each day, chosen by a date seed so everyone shares a list.
  // This is the whole gem economy: a battle win pays 5, and a ten-pull costs 900.
  'quests.json': {
    perDay: quests.DAILY_COUNT,
    loginGems: quests.LOGIN_BONUS?.gems ?? 100,
    loginGoldKills: quests.LOGIN_BONUS?.goldKills ?? 100,
    streakGemsPerDay: quests.STREAK?.gemsPerDay ?? 20,
    streakMaxDays: quests.STREAK?.maxDays ?? 7,
    allClearGems: quests.ALL_CLEAR_BONUS?.gems ?? 100,
    items: quests.DAILY_QUESTS.map((q) => ({
      id: q.id, name: q.name, desc: q.desc, target: q.target,
      gems: q.reward?.gems ?? 0, goldKills: q.reward?.goldKills ?? 0,
    })),
  },
  // 오늘의 픽업. The web build rotates a featured S and A through the whole roster so wanting a
  // particular card becomes "come back on their day" rather than praying. We export the fixed
  // per-grade order it walks, so the Unity client lands on the same hero on the same date.
  'pickup.json': {
    rate: pickup.PICKUP_RATE, days: pickup.PICKUP_DAYS,
    sparkS: pickup.SPARK_COST?.S ?? 150, sparkA: pickup.SPARK_COST?.A ?? 60,
    orders: pickup.PICKUP_GRADES.map((g) => ({
      grade: g,
      // pickupFor() walks this order one step per banner; reproducing the order is enough.
      ids: (() => {
        const ids = [];
        for (let i = 0; i < 400; i++) {
          const key = new Date((i * pickup.PICKUP_DAYS) * 86400000).toISOString().slice(0, 10);
          const id = pickup.pickupFor(key)[g];
          if (id && !ids.includes(id)) ids.push(id);
        }
        return ids;
      })(),
    })),
  },
  // 호감도 해금 텍스트: a second bio line at Lv3 and a private message at Lv5. This is the payoff
  // that makes levelling a bond worth doing, so it ships with the roster rather than as an extra.
  'affection.json': wrap(Object.entries(extra.EXTRA ?? {}).map(([id, e]) => ({
    id, secret: e.secret ?? '', line2: e.line2 ?? '',
  }))),
  // 몬스터는 스프레드시트 오류다. 페이즈마다 다른 3종을 뽑아 쓰고, 10스테이지마다 보스가 돌아온다.
  'monsters.json': {
    items: (monsters.MONSTER_TYPES ?? []).map((m) => ({ id: m.id, name: m.name, shape: m.shape })),
    bosses: (monsters.BOSSES ?? []).map((b) => ({
      id: b.id, name: b.name, desc: b.desc ?? '',
      hp: b.hp ?? 1, atk: b.atk ?? 1, interval: b.interval ?? 2,
      specials: (b.specials ?? []).map((s) => ({ every: s.every, kind: s.kind, name: s.name, desc: s.desc ?? '' })),
    })),
  },
  'story.json':     wrap((story.EPISODES ?? []).map((e) => ({
                      id: e.id, phase: e.phase, title: e.title, room: e.room,
                      lines: e.lines.map(([who, text]) => ({ who, text })),
                    }))),
  'balance.json': {
    upgradeCostBase: BALANCE.UPGRADE_COST_BASE, upgradeCostGrowth: BALANCE.UPGRADE_COST_GROWTH,
    monsterHpBase: BALANCE.MONSTER_HP_BASE,     monsterHpGrowth: BALANCE.MONSTER_HP_GROWTH,
    goldBase: BALANCE.GOLD_BASE,                goldGrowth: BALANCE.GOLD_GROWTH,
    gachaSingleCost: BALANCE.GACHA_SINGLE_COST, gachaTenCost: BALANCE.GACHA_TEN_COST,
    pityA: BALANCE.PITY_A, pityS: BALANCE.PITY_S,
    duplicateShardsMin: BALANCE.DUPLICATE_SHARDS_MIN, duplicateShardsMax: BALANCE.DUPLICATE_SHARDS_MAX,
    unlockShards: BALANCE.UNLOCK_SHARDS,
    startingGold: BALANCE.STARTING_GOLD, startingGems: BALANCE.STARTING_GEMS,
    partySize: BALANCE.PARTY_SIZE, maxStar: BALANCE.MAX_STAR, skillUnlockStar: BALANCE.SKILL_UNLOCK_STAR,
    skillBoostStar: BALANCE.SKILL_BOOST_STAR ?? 4,
    storyGems: BALANCE.STORY?.gems ?? 30,
    heroAtkGrowth: BALANCE.HERO_ATK_GROWTH, heroHpGrowth: BALANCE.HERO_HP_GROWTH,
    starMult: BALANCE.STAR_MULT, levelCapByStar: BALANCE.LEVEL_CAP_BY_STAR,
    enhancePerLevel: BALANCE.ENHANCE_PER_LEVEL,
    traitPerStar: BALANCE.TRAIT_STAR?.perStar ?? 0.12,
    monsterAtkRampFull: BALANCE.MONSTER_ATK_RAMP?.full ?? 5,
    monsterAtkRampByStage: BALANCE.MONSTER_ATK_RAMP?.byStage ?? 25,
    affectionMax: BALANCE.AFFECTION?.maxLevel ?? 10,
    affectionXpBase: BALANCE.AFFECTION?.xpBase ?? 60,
    affectionXpGrowth: BALANCE.AFFECTION?.xpGrowth ?? 1.45,
    affectionXpPerKill: BALANCE.AFFECTION?.xpPerKill ?? 1,
    affectionXpPerBoss: BALANCE.AFFECTION?.xpPerBoss ?? 15,
    affectionGiftXp: BALANCE.AFFECTION?.giftXp ?? 45,
    affectionGiftGoldKills: BALANCE.AFFECTION?.giftGoldKills ?? 40,
    affectionBonusPerLevel: BALANCE.AFFECTION?.bonusPerLevel ?? 0.01,
    affectionUnlockSecret: BALANCE.AFFECTION?.unlockSecret ?? 3,
    affectionUnlockLine: BALANCE.AFFECTION?.unlockLine ?? 5,
  },
};

mkdirSync(OUT, { recursive: true });
let total = 0;
for (const [name, data] of Object.entries(files)) {
  const json = JSON.stringify(data, null, 1);
  writeFileSync(join(OUT, name), json, 'utf8');
  const n = Array.isArray(data?.items) ? data.items.length : Object.keys(data).length;
  total += json.length;
  console.log(`  ${name.padEnd(16)} ${String(n).padStart(4)} entries  ${(json.length / 1024).toFixed(1)} KB`);
}
console.log(`\n${Object.keys(files).length} files, ${(total / 1024).toFixed(1)} KB total -> ${OUT}`);

// A quick sanity read-out so a broken import is obvious immediately.
const byGrade = roster.reduce((a, h) => ((a[h.grade] = (a[h.grade] ?? 0) + 1), a), {});
console.log(`roster: ${roster.length} heroes  ${JSON.stringify(byGrade)}`);
console.log(`gacha rates: ${rows(GRADES).map((g) => `${g.id}=${(g.rate * 100).toFixed(1)}%`).join(' ')}`);
