using System;
using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;
using Random = UnityEngine.Random;

namespace ExcelHeroes.Core
{
    public enum Side { Hero, Monster }

    /// <summary>One fighter on the line. Heroes keep formation; monsters walk in until they can reach.</summary>
    public class Combatant
    {
        public Side side;
        public string heroId;          // null for monsters
        public string name;
        public string role;
        public int maxHp, hp;
        public int atk;
        public float interval;         // seconds between basic attacks
        public float range;            // in lane cells
        public float x;                // lane position, 0 (hero backline) .. 12 (monster spawn)
        public float homeX;            // formation slot a hero drifts back to between waves
        public float attackTimer;
        public float skillTimer;       // counts down to ready
        public float skillCooldown;    // 0 = this fighter has no skill
        public bool Alive => hp > 0;

        // hero loadout
        public string traitId;          // 특성, resolved once at setup; value already scaled by ★
        public float traitValue;
        public float skillPower;        // ★-boosted, from StatMath.SkillPower
        public bool elite;              // crowned wave enemy: tougher, pays better

        // boss script state
        public BossDef boss;            // null for everything that is not a boss
        public int attackCount;

        // transient combat state
        public float burnLeft, burnPerSec;
        public float shield;
        public float hasteLeft, hasteAmount;
        public float slowLeft, slowAmount;
        public float tauntLeft;
        public float guardLeft;         // timed damage reduction (보스 방어막, 총대 메기)
        public float dmgReduction;      // the timed part, cleared when those timers run out
        public float baseReduction;     // standing part (철벽 멘탈); never cleared

        /// <summary>The two reductions stack multiplicatively, so neither can reach immunity alone.</summary>
        public float Mitigation => 1f - (1f - Math.Clamp(baseReduction, 0f, 0.9f)) * (1f - Math.Clamp(dmgReduction, 0f, 0.9f));

        public bool SkillReady => skillCooldown > 0f && skillTimer <= 0f && Alive;
        public float SkillCharge => skillCooldown <= 0f ? 0f : 1f - Math.Clamp(skillTimer / skillCooldown, 0f, 1f);
    }

    public enum EventKind { Damage, Heal, Death, Skill, WaveClear, Victory, Defeat, Spawn }

    public struct BattleEvent
    {
        public EventKind kind;
        public Combatant actor;
        public Combatant target;
        public int amount;
        public string text;
    }

    /// <summary>
    /// The line auto-battle: the party holds a single rank and waves of spreadsheet errors walk into
    /// them. Basic attacks are automatic; EX skills charge on a timer and, unless auto is on, wait for
    /// the player to fire them — that tap is the only moment-to-moment input the game asks for.
    ///
    /// The sim is UI-free and stepped with a fixed dt so it behaves the same however the frame rate
    /// moves. BattleScreen renders whatever is in Heroes/Monsters and drains Events each frame.
    /// </summary>
    public class BattleSim
    {
        public const float LaneCells = 13f;
        const float HeroFrontX = 4.5f;

        // --- pacing (measured with Excel Heroes ▸ Run Balance Bench) -----------------------------
        // Monsters used to amble in at 1.6 cells/s, which spent ~16s of a 40s fight on nobody
        // attacking anybody. Faster approach, fewer bodies per wave, and a boss that is a wall rather
        // than a sponge — same total health, far less dead air.
        const float MonsterSpeed = 2.8f;
        const float HeroSpeed = 3.2f;         // melee closes a little faster than the wave advances
        const float BossHpMultiplier = 4f;

        /// <summary>
        /// Hard stop. Without it a party whose regen out-paces the wave's damage simply never
        /// resolves — the bench found three such stalemates sitting at the 180s sampling limit, which
        /// in the real game is a battle screen that never ends. Running out of time is a loss.
        /// </summary>
        public const float TimeLimit = 90f;
        public float Elapsed { get; private set; }
        public bool TimedOut { get; private set; }

        /// <summary>
        /// 야근 — past this point the errors start hitting harder every second. A fight the party
        /// cannot finish should end in a body, not a clock: the bench kept producing rows where a
        /// party neither killed the wave nor died to it, which reads to the player as a frozen game
        /// rather than as "you are too weak". With this, TimeLimit is a backstop that should never fire.
        /// </summary>
        const float EnrageFrom = 42f;
        const float EnrageDoubleEvery = 14f;

        public float EnrageMultiplier =>
            Elapsed <= EnrageFrom ? 1f : 1f + (Elapsed - EnrageFrom) / EnrageDoubleEvery;

        /// <summary>True once the overtime pressure has started, so the HUD can say so.</summary>
        public bool Enraged => Elapsed > EnrageFrom;

        public readonly List<Combatant> Heroes = new();
        public readonly List<Combatant> Monsters = new();
        public readonly Queue<BattleEvent> Events = new();

        public int Stage { get; private set; }
        public int Wave { get; private set; }
        public int WaveCount { get; }
        public bool AutoSkill { get; set; }
        public bool Finished { get; private set; }
        public bool Won { get; private set; }
        public int GoldEarned { get; private set; }

        /// <summary>Monsters put down this run — what 호감도 is paid on.</summary>
        public int Kills { get; private set; }

        /// <summary>Broken out for the daily tasks that ask for elites or chests specifically.</summary>
        public int EliteKills { get; private set; }
        public int ChestsOpened { get; private set; }

        readonly SynergyResult _synergy;
        readonly int _perWave;

        public BattleSim(PlayerState player, int stage, int waves = 3, int monstersPerWave = 3)
        {
            Stage = stage;
            WaveCount = waves;
            _perWave = monstersPerWave;
            _synergy = StatMath.Synergy(player);

            var slot = 0;
            foreach (var owned in player.PartyMembers())
            {
                var def = GameData.Hero(owned.id);
                var role = GameData.Role(def?.role);
                if (def == null || role == null) continue;

                var skill = GameData.Skill(def.skillType);
                var hasSkill = StatMath.SkillUnlocked(owned) && skill != null;
                var cooldown = hasSkill ? skill.cooldown * (1f - Perk("cooldown")) : 0f;
                var traitValue = StatMath.TraitValue(owned);   // already scaled by ★

                var c = new Combatant
                {
                    side = Side.Hero,
                    heroId = owned.id,
                    name = def.name,
                    role = def.role,
                    maxHp = (int)(StatMath.Hp(owned) * (1f + _synergy.hpBonus)),
                    atk = (int)(StatMath.Atk(owned) * (1f + _synergy.atkBonus)),
                    interval = role.interval,
                    range = role.range,
                    x = HeroFrontX - slot * 0.75f,
                    homeX = HeroFrontX - slot * 0.75f,
                    skillCooldown = cooldown,
                    skillTimer = cooldown * 0.35f,   // a little charge at the bell so wave 1 has a beat
                    traitId = def.trait,
                    traitValue = traitValue,
                    skillPower = StatMath.SkillPower(owned),
                };

                // 빠른 손놀림 shortens this hero's own swing; 철벽 멘탈 is a standing damage cut.
                if (c.traitId == "swift") c.interval /= 1f + traitValue;
                if (c.traitId == "sturdy") c.baseReduction = traitValue;

                c.hp = c.maxHp;
                Heroes.Add(c);
                slot++;
            }

            // 팀 리더십 is the one trait that reads across the party, so it is folded in afterwards
            // once every member is known. It stacks — two leaders are worth two.
            var rally = Heroes.Where(h => h.traitId == "rally").Sum(h => h.traitValue);
            if (rally > 0f) foreach (var h in Heroes) h.atk = (int)(h.atk * (1f + rally));

            // 영업 마인드 / 행운의 셀 pay out at the end of the run rather than per swing.
            _goldBonus = Heroes.Where(h => h.traitId == "greedy").Sum(h => h.traitValue) + Perk("gold");
            _gemBonus = (int)Heroes.Where(h => h.traitId == "lucky").Sum(h => h.traitValue);

            SpawnWave();
        }

        float _goldBonus;
        int _gemBonus;

        /// <summary>Extra gems the party's 행운의 셀 holders earn for clearing the stage.</summary>
        public int GemBonus => _gemBonus;

        float Perk(string key) => _synergy.perks.TryGetValue(key, out var v) ? v : 0f;

        /// <summary>
        /// The opening stages run two bodies a wave instead of three. The bench had a first-ever
        /// battle taking 57 seconds, which is a bad first impression for a game whose hook is the
        /// summon screen — the player should be back at the banner quickly.
        /// </summary>
        int MinionsThisWave => Stage < 5 ? Math.Max(2, _perWave - 1) : _perWave;

        void SpawnWave()
        {
            Wave++;
            if (Wave == WaveCount) SpawnBoss();
            else for (var i = 0; i < MinionsThisWave; i++) Spawn(NewMinion(i), LaneCells + i * 1.1f);
        }

        const int EliteFromStage = 5;
        const float EliteChance = 0.18f;
        const float ChestChance = 0.06f;
        const float MimicShare = 0.30f;

        Combatant NewMinion(int slot)
        {
            var hp = StatMath.MonsterHp(Stage);
            var atk = StatMath.MonsterAtk(Stage);

            // A treasure chest joins the odd wave. It does not fight — but three in ten are mimics,
            // which hit hard and reward reading the wave before committing a cooldown.
            if (Random.value < ChestChance)
            {
                var mimic = Random.value < MimicShare;
                return new Combatant
                {
                    side = Side.Monster,
                    name = mimic ? "보물 상자?" : "보물 상자",
                    role = "melee",
                    maxHp = mimic ? hp : Math.Max(1, hp / 3), hp = mimic ? hp : Math.Max(1, hp / 3),
                    atk = mimic ? (int)(atk * 1.8f) : 0,
                    interval = 1.4f,
                    range = 1.1f,
                    elite = mimic,
                };
            }

            // From stage 5 on, crowned variants start showing up: much tougher, worth much more.
            var elite = Stage >= EliteFromStage && Random.value < EliteChance;
            var name = GameData.MonsterForStage(Stage, slot)?.name ?? "스프레드시트 오류";
            var m = new Combatant
            {
                side = Side.Monster,
                name = elite ? $"★ {name}" : name,
                role = "melee",
                maxHp = elite ? (int)(hp * 2.5f) : hp,
                atk = elite ? (int)(atk * 1.35f) : atk,
                interval = 1.1f,
                range = 1.1f,
                elite = elite,
            };
            m.hp = m.maxHp;
            return m;
        }

        /// <summary>
        /// Each phase has its own boss with its own script — 채용 공고 calls in reinforcements,
        /// 정산 로봇 heals itself, 반려 도장 shields. The stat multipliers and the move list both come
        /// from the web build, so a phase feels the same in both clients.
        /// </summary>
        void SpawnBoss()
        {
            var def = GameData.BossForStage(Stage);
            var hp = StatMath.MonsterHp(Stage);
            var atk = StatMath.MonsterAtk(Stage);

            var b = new Combatant
            {
                side = Side.Monster,
                name = def?.name ?? "긴급 티켓",
                role = "melee",
                maxHp = (int)(hp * BossHpMultiplier * (def?.hp ?? 1f)),
                atk = (int)(atk * 1.6f * (def?.atk ?? 1f)),
                interval = def?.interval ?? 2f,
                range = 1.1f,
                boss = def,
            };
            b.hp = b.maxHp;
            Spawn(b, LaneCells);
        }

        void Spawn(Combatant m, float x)
        {
            m.x = x;
            Monsters.Add(m);
            Events.Enqueue(new BattleEvent { kind = EventKind.Spawn, actor = m });
        }

        public void Tick(float dt)
        {
            if (Finished) return;

            Elapsed += dt;
            if (Elapsed >= TimeLimit)
            {
                Finished = true; Won = false; TimedOut = true;
                Events.Enqueue(new BattleEvent { kind = EventKind.Defeat, text = "시간 초과" });
                return;
            }

            foreach (var c in Heroes.Concat(Monsters)) TickStatus(c, dt);

            foreach (var h in Heroes.Where(h => h.Alive))
            {
                if (h.skillCooldown > 0f && h.skillTimer > 0f) h.skillTimer -= dt;
                if (AutoSkill && h.SkillReady && WorthFiring(h)) FireSkill(h);
                StepHero(h, dt);
                StepAttack(h, dt, Monsters);
            }

            // Snapshotted: a boss's 증원 요청 adds to Monsters from inside this loop.
            foreach (var m in Monsters.Where(m => m.Alive).ToList())
            {
                var target = FrontHero();
                if (target == null) continue;
                var reach = target.x + m.range;
                if (m.x > reach) m.x = Math.Max(reach, m.x - MonsterSpeed * dt);
                else StepAttack(m, dt, Heroes);
            }

            Monsters.RemoveAll(m => !m.Alive);

            if (!Heroes.Any(h => h.Alive)) { Finished = true; Won = false; Events.Enqueue(new BattleEvent { kind = EventKind.Defeat }); return; }

            if (Monsters.Count == 0)
            {
                if (Wave >= WaveCount)
                {
                    Finished = true; Won = true;
                    Events.Enqueue(new BattleEvent { kind = EventKind.Victory });
                }
                else
                {
                    Events.Enqueue(new BattleEvent { kind = EventKind.WaveClear, amount = Wave });
                    SpawnWave();
                }
            }
        }

        void TickStatus(Combatant c, float dt)
        {
            if (!c.Alive) return;
            if (c.burnLeft > 0f)
            {
                c.burnLeft -= dt;
                Damage(null, c, (int)(c.burnPerSec * dt), silent: true);
            }
            if (c.hasteLeft > 0f) c.hasteLeft -= dt;
            if (c.slowLeft > 0f) c.slowLeft -= dt;
            if (c.tauntLeft > 0f) c.tauntLeft -= dt;
            if (c.guardLeft > 0f) c.guardLeft -= dt;
            if (c.hasteLeft <= 0f) c.hasteAmount = 0f;
            if (c.slowLeft <= 0f) c.slowAmount = 0f;
            if (c.tauntLeft <= 0f && c.guardLeft <= 0f) c.dmgReduction = 0f;

            // 점심 시간 on the hero plus the 시설·복지 perk on the party — a slow trickle so a
            // healer-less party is not simply doomed.
            if (c.side == Side.Hero && c.hp < c.maxHp)
            {
                var regen = Perk("regen") + (c.traitId == "regen" ? c.traitValue : 0f);
                if (regen > 0f) c.hp = Math.Min(c.maxHp, c.hp + (int)MathF.Ceiling(c.maxHp * regen * dt));
            }
        }

        /// <summary>
        /// Melee closes, ranged holds. Without this the formation is a trap: monsters stop at the
        /// front hero's reach, and every melee behind them — four of five in a standard party — sits
        /// one cell short of the fight swinging at nothing. That single gap was most of the low
        /// damage, the long battles and the stalemates the bench kept turning up.
        ///
        /// Between waves everyone drifts home, so the party re-forms instead of trailing across the
        /// lane in whatever order the last fight left them.
        /// </summary>
        void StepHero(Combatant h, float dt)
        {
            var target = NearestTarget(h, Monsters);
            if (target == null)
            {
                if (Math.Abs(h.x - h.homeX) > 0.05f)
                    h.x += Math.Sign(h.homeX - h.x) * Math.Min(HeroSpeed * dt, Math.Abs(h.homeX - h.x));
                return;
            }

            // Long-range roles already have the whole lane covered; only short reach needs to walk.
            if (h.range >= 5f) return;

            var wanted = target.x - h.range * 0.85f;   // stop just inside reach, not on top of them
            if (h.x < wanted) h.x = Math.Min(wanted, h.x + HeroSpeed * dt);
        }

        void StepAttack(Combatant a, float dt, List<Combatant> enemies)
        {
            var target = NearestTarget(a, enemies);
            if (target == null) return;
            if (Math.Abs(target.x - a.x) > a.range) return;

            a.attackTimer -= dt * Math.Max(0.2f, 1f + a.hasteAmount - a.slowAmount);
            if (a.attackTimer > 0f) return;
            a.attackTimer = a.interval;

            var dmg = a.atk;
            if (a.side == Side.Hero)
            {
                // 날카로운 지적 stacks with the 영업·마케팅 부문 perk — both are a chance to double.
                var critChance = Perk("crit") + (a.traitId == "crit" ? a.traitValue : 0f);
                if (Random.value < critChance) dmg *= 2;

                // 보고서 특화 and the 재무·감사 perk both key off "is this a boss".
                if (target.boss != null)
                    dmg = (int)(dmg * (1f + Perk("boss") + (a.traitId == "focus" ? a.traitValue : 0f)));
            }

            Damage(a, target, dmg);

            if (a.side != Side.Hero) return;

            // 전체 회신 — the basic attack splashes onto everything else in reach.
            if (a.traitId == "splash")
            {
                foreach (var other in Monsters.Where(m => m.Alive && m != target && Math.Abs(m.x - target.x) <= 2f).ToList())
                    Damage(a, other, (int)(dmg * a.traitValue), silent: true);
            }

            // 커피 수혈 — the attacker drinks back a slice of what it dealt.
            if (a.traitId == "lifesteal") Heal(a, (int)(dmg * a.traitValue));

            // A boss's script is counted in its own swings, so slowing it also delays its specials.
            if (a.boss != null) BossTurn(a);
        }

        /// <summary>Runs whichever scripted moves are due on this boss's Nth attack.</summary>
        void BossTurn(Combatant b)
        {
            b.attackCount++;
            foreach (var s in b.boss.specials)
            {
                if (s.every <= 0 || b.attackCount % s.every != 0) continue;
                Events.Enqueue(new BattleEvent { kind = EventKind.Skill, actor = b, text = s.name });
                FireBossMove(b, s.kind);
            }
        }

        void FireBossMove(Combatant b, string kind)
        {
            var alive = Heroes.Where(h => h.Alive).ToList();
            if (alive.Count == 0) return;

            switch (kind)
            {
                case "volley":   // three fireballs at random heroes, 60% each
                    for (var i = 0; i < 3; i++) Damage(b, alive[Random.Range(0, alive.Count)], (int)(b.atk * 0.6f));
                    break;
                case "sweep":    // the front two take 90%
                    foreach (var h in alive.OrderByDescending(h => h.x).Take(2)) Damage(b, h, (int)(b.atk * 0.9f));
                    break;
                case "stomp":    // everyone takes 50%
                    foreach (var h in alive) Damage(b, h, (int)(b.atk * 0.5f));
                    break;
                case "throw":    // the backmost hero takes 140% — punishes parking the healer behind
                    Damage(b, alive.OrderBy(h => h.x).First(), (int)(b.atk * 1.4f));
                    break;
                case "slow":
                    foreach (var h in alive) { h.slowLeft = 4f; h.slowAmount = 0.3f; }
                    break;
                case "shield":
                    b.guardLeft = 4f;
                    b.dmgReduction = 0.45f;
                    break;
                case "heal":
                {
                    var before = b.hp;
                    b.hp = Math.Min(b.maxHp, b.hp + (int)(b.maxHp * 0.12f));
                    if (b.hp > before) Events.Enqueue(new BattleEvent { kind = EventKind.Heal, target = b, amount = b.hp - before });
                    break;
                }
                case "summon":   // adds, which is the fight asking whether the party brought any AoE
                    for (var i = 0; i < 2; i++) Spawn(NewMinion(i), Math.Min(LaneCells, b.x + 1f + i * 0.9f));
                    break;
            }
        }

        Combatant FrontHero() => Heroes.Where(h => h.Alive).OrderByDescending(h => h.tauntLeft).ThenByDescending(h => h.x).FirstOrDefault();

        /// <summary>
        /// How much a single big hit is worth spending on this target: bosses first, then elites,
        /// then whatever has the most health left. Keeps burst off the trash that a basic attack
        /// would have killed anyway.
        /// </summary>
        static float Priority(Combatant m) => (m.boss != null ? 1_000_000f : m.elite ? 10_000f : 0f) + m.hp;

        static Combatant PickPriority(List<Combatant> live) => live.OrderByDescending(Priority).First();

        /// <summary>
        /// Auto mode fires a charged skill only when it would actually do something. Without this a
        /// healer burns 웰니스 데이 on a full-health party the instant it comes off cooldown, which is
        /// the single biggest reason an auto run loses a fight it should win.
        /// </summary>
        bool WorthFiring(Combatant h)
        {
            var def = GameData.Hero(h.heroId);
            if (def == null) return false;
            var live = Monsters.Count(m => m.Alive);
            if (live == 0) return false;

            var wounded = Heroes.Where(a => a.Alive).Sum(a => a.maxHp - a.hp);
            var poolHp = Math.Max(1, Heroes.Where(a => a.Alive).Sum(a => a.maxHp));
            var hurt = wounded / (float)poolHp;

            switch (def.skillType)
            {
                case "heal":
                case "cleanse":
                case "drain":
                    return hurt >= 0.25f;                         // hold it until there is damage to undo
                case "revive":
                    return Heroes.Any(a => !a.Alive) || hurt >= 0.4f;  // or bank the 재고용 보장 before it is needed
                case "barrier":
                case "taunt":
                    return live >= 2 || Monsters.Any(m => m.boss != null);
                case "sweep":
                case "ult":
                case "burn":
                    return live >= 2 || Monsters.Any(m => m.boss != null);
                default:
                    return true;                                  // single-target damage is never wasted
            }
        }

        Combatant NearestTarget(Combatant a, List<Combatant> enemies)
        {
            Combatant best = null;
            var bestD = float.MaxValue;
            foreach (var e in enemies)
            {
                if (!e.Alive) continue;
                var d = Math.Abs(e.x - a.x);
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        void Damage(Combatant from, Combatant to, int amount, bool silent = false)
        {
            if (!to.Alive || amount <= 0) return;
            if (from != null && from.side == Side.Monster) amount = (int)(amount * EnrageMultiplier);
            amount = (int)(amount * (1f - to.Mitigation));
            if (to.shield > 0f)
            {
                var absorbed = Math.Min(to.shield, amount);
                to.shield -= absorbed;
                amount -= (int)absorbed;
            }
            if (amount <= 0) return;

            to.hp -= amount;
            if (!silent) Events.Enqueue(new BattleEvent { kind = EventKind.Damage, actor = from, target = to, amount = amount });
            if (to.hp <= 0)
            {
                to.hp = 0;
                // Gold is paid per kill rather than per wave, so a run that dies on the boss still
                // banked what it actually cleared. Elites and chests pay the premium they advertise.
                if (to.side == Side.Monster)
                {
                    Kills++;
                    if (to.elite) EliteKills++;
                    if (to.name.StartsWith("보물 상자")) ChestsOpened++;
                    var worth = StatMath.StageGold(Stage) * (to.elite ? 3f : to.atk == 0 ? 5f : 1f);
                    GoldEarned += (int)(worth * (1f + _goldBonus));
                }
                Events.Enqueue(new BattleEvent { kind = EventKind.Death, target = to });
            }
        }

        void Heal(Combatant to, int amount)
        {
            if (!to.Alive || amount <= 0) return;
            to.hp = Math.Min(to.maxHp, to.hp + amount);
            Events.Enqueue(new BattleEvent { kind = EventKind.Heal, target = to, amount = amount });
        }

        /// <summary>Player-facing: fire a charged EX skill. Safe to call when not ready — it no-ops.</summary>
        public bool FireSkill(Combatant h)
        {
            if (!h.SkillReady) return false;
            var def = GameData.Hero(h.heroId);
            var skill = GameData.Skill(def?.skillType);
            if (def == null || skill == null) return false;

            h.skillTimer = h.skillCooldown;
            // h.skillPower is the ★-boosted value; the 임원 perk adds on top of it.
            var power = h.skillPower * (1f + Perk("skill"));
            var live = Monsters.Where(m => m.Alive).ToList();
            var allies = Heroes.Where(a => a.Alive).ToList();

            switch (def.skillType)
            {
                case "strike":
                    // Single-target burst goes where it matters: the boss if one is up, else the
                    // fattest target, rather than whatever happened to be first in the list.
                    if (live.Count > 0) Damage(h, PickPriority(live), (int)(h.atk * power));
                    break;
                case "execute":
                {
                    // 저격 보고 doubles below 30%, so it hunts the most finishable target.
                    var t = live.OrderBy(m => m.hp / (float)Math.Max(1, m.maxHp)).FirstOrDefault();
                    if (t != null)
                    {
                        var mult = t.hp < t.maxHp * 0.3f ? 2f : 1f;
                        Damage(h, t, (int)(h.atk * power * mult));
                    }
                    break;
                }
                case "sweep":
                    foreach (var m in live) Damage(h, m, (int)(h.atk * power));
                    break;
                case "ult":
                    foreach (var m in live) { Damage(h, m, (int)(h.atk * power)); m.attackTimer = Math.Max(m.attackTimer, 1.2f); }
                    break;
                case "chain":
                {
                    // 참조 연쇄 loses 30% per hop, so it starts on the target worth the full hit.
                    var mult = 1f;
                    foreach (var m in live.OrderByDescending(Priority).Take(3))
                    {
                        Damage(h, m, (int)(h.atk * power * mult));
                        mult *= 0.7f;
                    }
                    break;
                }
                case "drain":
                {
                    var dealt = 0;
                    foreach (var m in live) { var d = (int)(h.atk * power); Damage(h, m, d); dealt += d; }
                    var cap = (int)(allies.Sum(a => a.maxHp) * 0.25f);
                    var restore = Math.Min((int)(dealt * 0.4f), cap) / Math.Max(1, allies.Count);
                    foreach (var a in allies) Heal(a, restore);
                    break;
                }
                case "burn":
                    foreach (var m in live) { m.burnLeft = skill.duration > 0 ? skill.duration : 4f; m.burnPerSec = h.atk * power; }
                    break;
                case "heal":
                    foreach (var a in allies) Heal(a, (int)((a.maxHp - a.hp) * power / 100f));
                    break;
                case "cleanse":
                    // 스트레스 해소 — heals, clears the boss's slow, and hastens. The answer to 정원 초과.
                    foreach (var a in allies)
                    {
                        Heal(a, (int)(a.maxHp * power / 100f));
                        a.slowLeft = 0f; a.slowAmount = 0f;
                        a.hasteLeft = 5f; a.hasteAmount = 0.35f;
                    }
                    break;
                case "buff":
                    foreach (var a in allies) { a.hasteLeft = 5f; a.hasteAmount = Math.Max(a.hasteAmount, power / 100f); }
                    break;
                case "haste":
                    foreach (var a in allies) { a.hasteLeft = skill.duration > 0 ? skill.duration : 5f; a.hasteAmount = Math.Max(a.hasteAmount, power / 100f); }
                    break;
                case "barrier":
                {
                    var pool = allies.Sum(a => a.maxHp) * power / 100f;
                    foreach (var a in allies) a.shield += pool / Math.Max(1, allies.Count);
                    break;
                }
                case "taunt":
                    h.tauntLeft = skill.duration > 0 ? skill.duration : 5f;
                    h.dmgReduction = power / 100f;
                    break;
                case "revive":
                {
                    var down = Heroes.FirstOrDefault(a => !a.Alive);
                    if (down != null) { down.hp = (int)(down.maxHp * power / 100f); Events.Enqueue(new BattleEvent { kind = EventKind.Heal, target = down, amount = down.hp }); }
                    break;
                }
                default:
                    if (live.Count > 0) Damage(h, live[0], (int)(h.atk * power));
                    break;
            }

            Events.Enqueue(new BattleEvent { kind = EventKind.Skill, actor = h, text = def.skillName });
            return true;
        }

        public bool FireSkill(string heroId)
        {
            var h = Heroes.FirstOrDefault(x => x.heroId == heroId);
            return h != null && FireSkill(h);
        }
    }
}
