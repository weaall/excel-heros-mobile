using System;
using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;
using UnityEngine;
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
        public int star = 1;            // ★, kept on the combatant because 엄호 scales with it
        public float guardAt = -999f;   // when this tank last spent its guaranteed save
        public string traitId;          // 특성, resolved once at setup; value already scaled by ★
        public float traitValue;
        public float skillPower;        // ★-boosted, from StatMath.SkillPower
        public bool elite;              // crowned wave enemy: tougher, pays better
        public string typeId;           // monster type, for its sprite; null on heroes

        // Ported from the web build's entity: where a dashing melee hero is headed (0 = home),
        // whether a monster has finished walking in, and how far out it stops.
        public float y;
        public float dashTo;
        public float dashT;
        public bool arrived;
        public float standoff;
        public bool shoots;             // ranged monster: fires instead of closing

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

    /// <summary>
    /// A blow in flight. In the web build nothing lands the instant its timer runs out: a ranged
    /// hero throws something that hits on arrival, and even a melee swing is a short delay so the
    /// number appears when the blade does rather than at the start of the wind-up. Damage is rolled
    /// when it lands, not when it is fired, so a buff that goes up mid-flight counts.
    /// </summary>
    public class Shot
    {
        public Combatant From, To;
        public float T, Duration;
        public string Kind;             // shot · paper · bar · drop · slash · heal
        public bool Hostile;
        public float Progress => Duration <= 0f ? 1f : Math.Clamp(T / Duration, 0f, 1f);
    }

    public enum EventKind { Damage, Heal, Death, Skill, WaveClear, Victory, Defeat, Spawn, Fx, Warn, Braced }

    /// <summary>
    /// The effect vocabulary, straight from the web build's Renderer#drawEffects. The simulation
    /// names the effect and where it happens; BattleFx decides what it looks like.
    /// </summary>
    public enum FxKind { Slash, Puff, Ring, Sparkle, Impact, Crit, Muzzle }

    public struct BattleEvent
    {
        public EventKind kind;
        public Combatant actor;
        public Combatant target;
        public int amount;
        public string text;
        public bool crit;

        // Fx only: what to draw, where, in what colour and how big.
        public FxKind fx;
        public float x, y, radius;
        public Color color;
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
        // Field geometry, in the web build's own pixels on its 832x416 canvas. Abstract "lane cells"
        // were a reimplementation, and they produced a different fight: heroes walked forward to
        // meet the wave. The web build is explicit that they do not — "the party stands in one row
        // on the left and never actually moves" — melee heroes dash out to strike and snap back.
        public const float FieldW = 832f;
        public const float CellW = 64f;
        public const float GroundY = 318f;
        const float FrontX = 400f;            // x of the front-most hero
        const float LineGap = 60f;            // spacing between heroes in the line
        const float MeleeReach = 9f * CellW;  // melee and tanks reach any monster that has arrived
        const float DashGap = 38f;            // how close a dashing melee hero stops
        const float MonsterSpeed = 170f;      // px/s

        /// <summary>Formation order: tanks at the front, ranged at the back.</summary>
        static int RolePriority(string role) => role switch
        {
            "tank" => 0, "melee" => 1, "healer" => 2, _ => 3,
        };

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

        /// <summary>
        /// 재계산 예산 — the shared pool every EX skill is paid out of, ported from Blue Archive's
        /// Cost (see docs/BLUE_ARCHIVE_NOTES.md).
        ///
        /// Independent per-hero cooldowns gave the player no decision: each button was pressed the
        /// moment it lit up. One shared pool turns every few seconds into a question — spend it on
        /// the cheap skill now, or hold for the expensive one. Recovery is per-hero and additive,
        /// so a full party charges faster, which is a second reason to field five.
        /// </summary>
        public const float MaxCost = 10f;
        const float CostPerHeroPerSecond = 0.27f;

        /// <summary>Everything currently in the air, for the battle screen to draw.</summary>
        public readonly List<Shot> Shots = new();

        public float Cost { get; private set; }
        public float CostRate => LivingHeroes * CostPerHeroPerSecond;
        int LivingHeroes => Heroes.Count(h => h.Alive);

        /// <summary>
        /// What one hero's EX costs. Rarer cards hit harder, so they cost more — the same shape as
        /// the source game, where a 1-cost utility skill and an 8-cost nuke sit on the same bar.
        /// </summary>
        public static int CostOf(Combatant h)
        {
            var def = GameData.Hero(h?.heroId);
            return def?.grade switch
            {
                "S" => 6,
                "A" => 5,
                "B" => 4,
                "C" => 3,
                _ => 2,
            };
        }

        /// <summary>
        /// Shared cost pool check. In manual or auto-mode, the skill can only be cast if the
        /// current Cost is greater than or equal to the cost of the hero's skill.
        /// </summary>
        public bool CanAfford(Combatant h) => Cost >= CostOf(h);
        public bool Finished { get; private set; }
        public bool Won { get; private set; }
        public int GoldEarned { get; private set; }

        /// <summary>
        /// How hard the field is shaking, in pixels, decaying at 30/s — the same number and the
        /// same decay as the web build's `em.shake`. Bosses landing, enrage and heavy skills all
        /// push it up; it is most of why a hit there reads as a hit.
        /// </summary>
        public float Shake { get; private set; }

        public void AddShake(float amount) => Shake = Math.Max(Shake, amount);

        /// <summary>
        /// 콤보 — consecutive landed hits, worth a little damage each up to a cap, and reset by a
        /// few seconds without one. The party gets faster the longer a fight goes well.
        /// </summary>
        public int Combo { get; private set; }
        float _comboTimer;

        public float ComboBonus => Math.Min(GameData.Balance?.comboMax ?? 0.25f,
                                            Combo * (GameData.Balance?.comboPerHit ?? 0.005f));

        /// <summary>Monsters put down this run — what 호감도 is paid on.</summary>
        public int Kills { get; private set; }

        /// <summary>Broken out for the daily tasks that ask for elites or chests specifically.</summary>
        public int EliteKills { get; private set; }
        public int ChestsOpened { get; private set; }

        readonly SynergyResult _synergy;
        readonly int _perWave;

        /// <summary>
        /// 야근 모드 — the once-a-day survival run. It changes three things about a fight: the
        /// bodies come thicker and softer, crowned ones are common rather than rare, and losing
        /// does not end it. Everything else, including the party, is the same fight.
        /// </summary>
        public bool Overtime { get; set; }

        /// <summary>
        /// Kept only so a level bought DURING a run can be felt in it. Every stat below is a
        /// snapshot taken at the bell, which is what makes the fight cheap to run off-screen; the
        /// player is the one thing that can change underneath it.
        /// </summary>
        readonly PlayerState _player;

        /// <summary>
        /// Re-reads 공격력 and 체력 for the heroes still standing, after 자동 강화 or 일괄 강화 has
        /// bought levels mid-fight. Current HP is carried across as a FRACTION rather than a
        /// number: a hero at half health who gains 200 max HP should still be at half, and copying
        /// the old absolute value would make a level-up a stealth heal — or, on a max-HP drop,
        /// a stealth execution.
        ///
        /// Only 공격력 and 체력 move. Interval, range, traits and skill power come from the card's
        /// definition and its ★, and none of those can change without a new run anyway.
        /// </summary>
        public void RefreshHeroStats()
        {
            if (_player == null) return;

            foreach (var c in Heroes)
            {
                var owned = _player.Find(c.heroId);
                if (owned == null || c.hp <= 0) continue;

                var maxHp = (int)(StatMath.Hp(owned) * (1f + _synergy.hpBonus + TeamUpgrades.Health(_player)));
                if (maxHp <= 0) continue;

                var share = c.maxHp > 0 ? (float)c.hp / c.maxHp : 1f;
                c.maxHp = maxHp;
                c.hp = Math.Max(1, (int)Math.Round(maxHp * share));
                c.atk = (int)(StatMath.Atk(owned) * (1f + _synergy.atkBonus));
            }

            ApplyLeadership();
        }

        public BattleSim(PlayerState player, int stage, int waves = 3, int monstersPerWave = 3)
        {
            Stage = stage;
            WaveCount = waves;
            _perWave = monstersPerWave;
            _player = player;
            _synergy = StatMath.Synergy(player);
            Cost = 4f;

            var slot = 0;
            foreach (var owned in player.PartyMembers())
            {
                var def = GameData.Hero(owned.id);
                var role = GameData.Role(def?.role);
                if (def == null || role == null) continue;

                var skill = GameData.Skill(def.skillType);
                var hasSkill = StatMath.SkillUnlocked(owned) && skill != null;
                // 스킬 레벨 takes 3% off the charge time per level, on top of the tech perk.
                var cooldown = hasSkill
                    ? skill.cooldown * (1f - Perk("cooldown")) * StatMath.SkillCooldownMult(owned)
                    : 0f;
                var traitValue = StatMath.TraitValue(owned);   // already scaled by ★

                var c = new Combatant
                {
                    side = Side.Hero,
                    heroId = owned.id,
                    name = def.name,
                    role = def.role,
                    // 인체공학 의자 and 커피 머신 land here: the office upgrades are party-wide, so
                    // they multiply the same numbers synergy does rather than being a fifth stat.
                    maxHp = (int)(StatMath.Hp(owned) * (1f + _synergy.hpBonus + TeamUpgrades.Health(player))),
                    atk = (int)(StatMath.Atk(owned) * (1f + _synergy.atkBonus)),
                    interval = role.interval / (1f + TeamUpgrades.AttackSpeed(player)),
                    range = def.role is "tank" or "melee" ? MeleeReach : role.range * CellW,
                    x = FrontX - slot * LineGap,
                    homeX = FrontX - slot * LineGap,
                    y = GroundY + (slot % 2 == 1 ? 6f : -6f),
                    skillCooldown = cooldown,
                    skillTimer = cooldown * 0.35f,   // a little charge at the bell so wave 1 has a beat
                    traitId = def.trait,
                    traitValue = traitValue,
                    skillPower = StatMath.SkillPower(owned),
                    star = owned.star,
                };

                // 빠른 손놀림 shortens this hero's own swing; 철벽 멘탈 is a standing damage cut.
                if (c.traitId == "swift") c.interval /= 1f + traitValue;
                if (c.traitId == "sturdy") c.baseReduction = traitValue;

                c.hp = c.maxHp;
                Heroes.Add(c);
                slot++;
            }

            ApplyLeadership();

            // 영업 마인드 / 행운의 셀 pay out at the end of the run rather than per swing.
            _goldBonus = Heroes.Where(h => h.traitId == "greedy").Sum(h => h.traitValue) + Perk("gold")
                         + TeamUpgrades.Gold(player);
            _gemBonus = (int)Heroes.Where(h => h.traitId == "lucky").Sum(h => h.traitValue);
            _gemDropBonus = TeamUpgrades.GemChance(player);

            SpawnWave();
        }

        /// <summary>
        /// 팀 리더십 is the one trait that reads across the party, so it is folded in once every
        /// member is known rather than while they are being built. It stacks — two leaders are
        /// worth two. It multiplies 공격력, so anything that recomputes 공격력 has to end here or
        /// the bonus quietly disappears mid-fight.
        /// </summary>
        void ApplyLeadership()
        {
            var rally = Heroes.Where(h => h.traitId == "rally").Sum(h => h.traitValue);
            if (rally > 0f) foreach (var h in Heroes) h.atk = (int)(h.atk * (1f + rally));
        }

        float _goldBonus;
        int _gemBonus;
        float _gemDropBonus;

        /// <summary>Gems that fell off monsters during the run, paid out win or lose.</summary>
        public int GemsDropped { get; private set; }

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
            else for (var i = 0; i < MinionsThisWave; i++) Spawn(NewMinion(i), FieldW + 60f + i * 58f);
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
            // 야근 makes them common and every body soft: the run is scored on how many fall in a
            // minute, so it has to be survivable at a difficulty three stages past anything cleared.
            var b = GameData.Balance;
            if (Overtime) hp = Math.Max(1, (int)(hp * b.overtimeHpMult));
            var elite = Overtime
                ? Random.value < b.overtimeElite
                : Stage >= EliteFromStage && Random.value < EliteChance;
            var type = GameData.MonsterForStage(Stage, slot);
            var name = type?.name ?? "스프레드시트 오류";
            var m = new Combatant
            {
                side = Side.Monster,
                // Carried so the battle can draw the right creature; without it every monster was
                // an empty box, which is what "몬스터 디자인이 없다" was looking at.
                typeId = type?.id,
                name = elite ? $"★ {name}" : name,
                role = "melee",
                maxHp = elite ? (int)(hp * 2.5f) : hp,
                atk = elite ? (int)(atk * 1.35f) : atk,
                interval = 1.1f,
                // Ranged monsters hang back and shoot; melee ones close to arm's length, each a
                // little further out than the last so the wave arrives as a line.
                range = (elite ? 1.2f : 0.9f) * CellW,
                standoff = 0.9f * 0.9f * CellW + slot * 34f,
                y = GroundY + (slot % 2 == 1 ? 10f : -8f),
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
            Spawn(b, FieldW + 60f);
        }

        /// <summary>
        /// Screenshot driver only: clears the field and brings this run's boss on now, at
        /// `hpFrac` of its health, so the boss HUD can be photographed without playing to it.
        /// </summary>
        public void DebugBossNow(float hpFrac)
        {
            if (Finished) return;
            _tickMonsters.Clear();
            foreach (var mm in Monsters) if (mm.Alive) _tickMonsters.Add(mm);
            foreach (var m in _tickMonsters)
            {
                m.hp = 0;
                Events.Enqueue(new BattleEvent { kind = EventKind.Death, target = m });
            }
            Wave = WaveCount - 1;
            SpawnWave();
            var boss = Monsters.LastOrDefault(m => m.boss != null);
            // the capture pass seeds a levelled party against an early stage, which would drop the
            // boss before it is photographed: a much deeper pool, only here
            if (boss != null) { boss.maxHp = (int)Math.Min(int.MaxValue / 2L, boss.maxHp * 40L); boss.hp = Math.Max(1, (int)(boss.maxHp * hpFrac)); }
        }

        /// <summary>Monsters fielded so far this run, summons included.</summary>
        public int Spawned { get; private set; }

        /// <summary>
        /// Everything this run will field, for the kill counter: what has come on so far plus what
        /// the waves still to come will bring (a boss is one; its summons join when they arrive).
        /// −1 in 야근, which has no end to count towards.
        /// </summary>
        public int EnemyTotal
        {
            get
            {
                if (Overtime) return -1;
                var ahead = 0;
                for (var w = Wave + 1; w <= WaveCount; w++) ahead += w == WaveCount ? 1 : MinionsThisWave;
                return Spawned + ahead;
            }
        }

        /// <summary>Capture pass only: ordinary monsters take this many times their hp, so a fight lasts long enough to photograph them.</summary>
        public static int DebugTanky;

        void Spawn(Combatant m, float x)
        {
            m.x = x;
            if (DebugTanky > 1 && m.boss == null) { m.maxHp = (int)Math.Min(int.MaxValue / 2L, (long)m.maxHp * DebugTanky); m.hp = m.maxHp; }
            Spawned++;
            Monsters.Add(m);
            Events.Enqueue(new BattleEvent { kind = EventKind.Spawn, actor = m });
        }

        readonly List<Combatant> _tickMonsters = new();

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

            Cost = Math.Min(MaxCost, Cost + CostRate * dt);

            // Same decay rates as the web build: the shake dies in a third of a second, the combo
            // holds for a few seconds of not landing anything.
            Shake = Math.Max(0f, Shake - dt * 30f);
            if (Combo > 0)
            {
                _comboTimer -= dt;
                if (_comboTimer <= 0f) Combo = 0;
            }

            StepShots(dt);

            // plain loops: the same order and the same checks as the LINQ this was, without the
            // enumerator garbage every tick (PerfProbe: ~26 KB a frame across the fight)
            for (var i = 0; i < Heroes.Count; i++) TickStatus(Heroes[i], dt);
            for (var i = 0; i < Monsters.Count; i++) TickStatus(Monsters[i], dt);

            foreach (var h in Heroes)
            {
                if (!h.Alive) continue;
                if (h.skillCooldown > 0f && h.skillTimer > 0f) h.skillTimer -= dt;
                if (AutoSkill && h.SkillReady && CanAfford(h) && WorthFiring(h)) FireSkill(h);
                StepHero(h, dt);
                StepAttack(h, dt, Monsters);
            }

            // Snapshotted: a boss's 증원 요청 adds to Monsters from inside this loop.
            foreach (var m in Monsters.Where(m => m.Alive).ToList())
            {
                var front = FrontHero();
                if (front == null) continue;

                // Monsters hold a standoff from the front of the line rather than walking onto it,
                // which is what keeps a wave readable as a line instead of a pile.
                var stopX = front.x + m.standoff;
                if (m.x > stopX + 2f) { m.x = Math.Max(stopX, m.x - MonsterSpeed * dt); continue; }
                m.arrived = true;
                StepAttack(m, dt, Heroes);
            }

            Monsters.RemoveAll(m => !m.Alive);

            if (!Heroes.Any(h => h.Alive))
            {
                // 야근 중 전원 번아웃 — the run keeps going. It is scored on kills over a minute,
                // and ending it early on a wipe would make the reward a coin flip on one bad wave.
                if (Overtime) { foreach (var h in Heroes) h.hp = h.maxHp; return; }
                Finished = true; Won = false; Events.Enqueue(new BattleEvent { kind = EventKind.Defeat }); return;
            }

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
        /// <summary>
        /// The party does not walk. In the web build the line holds its formation and the dungeon
        /// scrolls under it; a melee hero lunges out to strike and snaps back, which is a 0.45s
        /// animation rather than movement. My first version had heroes advancing to meet the wave,
        /// and that is a different fight — the formation dissolved the moment anything arrived.
        /// </summary>
        void StepHero(Combatant h, float dt)
        {
            if (h.dashT <= 0f) { h.dashTo = 0f; return; }
            h.dashT -= dt;
            if (h.dashT <= 0f) { h.dashT = 0f; h.dashTo = 0f; }
        }

        void StepAttack(Combatant a, float dt, List<Combatant> enemies)
        {
            var target = NearestTarget(a, enemies);
            if (target == null) return;

            var dist = target.x - a.x;
            if (a.side == Side.Hero)
            {
                // Ported verbatim: a hero can reach a monster that has finished walking in, or one
                // that is close enough to lunge at, and ranged roles are never blocked on arrival.
                var reaches = Math.Abs(dist) <= a.range
                              && (target.arrived || a.role is "ranged" or "healer" || Math.Abs(dist) < 110f);
                if (!reaches) return;
            }
            else if (Math.Abs(dist) > a.range) return;

            a.attackTimer -= dt * Math.Max(0.2f, 1f + a.hasteAmount - a.slowAmount);
            if (a.attackTimer > 0f) return;
            a.attackTimer = a.interval;

            if (a.side == Side.Hero && a.role is not ("ranged" or "healer"))
            {
                a.dashTo = target.x - DashGap;   // lunge out; the renderer eases it and back
                a.dashT = 0.45f;
            }

            var ranged = a.side == Side.Hero && a.role is "ranged" or "healer";
            Shots.Add(new Shot
            {
                From = a,
                To = target,
                Duration = a.side != Side.Hero ? 0.5f : ranged ? 0.28f : 0.16f,
                Kind = ranged ? "shot" : a.side == Side.Hero ? "slash" : "drop",
                Hostile = a.side != Side.Hero,
            });

            // A boss's script is counted in its own swings, so slowing it also delays its specials.
            if (a.boss != null) BossTurn(a);
        }

        /// <summary>Lands one blow that was in flight. Nothing is rolled until this point.</summary>
        void Land(Shot shot)
        {
            var a = shot.From;
            var target = shot.To;
            if (a == null || target == null || !a.Alive || !target.Alive) return;

            var dmg = a.atk;
            var crit = false;
            if (a.side == Side.Hero)
            {
                // Every landed hit so far makes this one hurt a little more, up to the cap.
                dmg = (int)(dmg * (1f + ComboBonus));

                // 날카로운 지적 stacks with the 영업·마케팅 부문 perk — both are a chance to double.
                var critChance = Perk("crit") + (a.traitId == "crit" ? a.traitValue : 0f);
                if (Random.value < critChance) { dmg *= 2; crit = true; }

                // 보고서 특화 and the 재무·감사 perk both key off "is this a boss".
                if (target.boss != null)
                    dmg = (int)(dmg * (1f + Perk("boss") + (a.traitId == "focus" ? a.traitValue : 0f)));
            }

            // The impact, before the damage, so the effect is already on screen when the number
            // pops. A melee swing cuts, a ranged hit puffs — which is the difference the web build
            // draws and this port had replaced with a class toggle and nothing else.
            var colour = a.side == Side.Hero
                ? GameData.Grade(GameData.Hero(a.heroId)?.grade)?.Color ?? Color.white
                : new Color(0.91f, 0.30f, 0.24f);

            if (shot.Kind == "slash")
                Fx(FxKind.Slash, target.x, target.y - 6f, colour, a.boss != null || target.boss != null ? 1f : 0f);
            else
                Fx(FxKind.Puff, target.x + 10f, target.y - 12f, colour);

            Fx(FxKind.Impact, target.x, target.y - 18f, colour, target.boss != null ? 1f : 0f);
            if (crit) Fx(FxKind.Crit, target.x, target.y - 30f, new Color(0.95f, 0.77f, 0.25f));

            if (a.side == Side.Hero) Damage(a, target, dmg, crit: crit);
            else SplitToTank(a, target, dmg, crit);

            if (a.side == Side.Hero)
            {
                Combo++;
                _comboTimer = GameData.Balance?.comboDecay ?? 3f;
                AddShake(crit ? 5f : 1.6f);
            }

            if (a.side != Side.Hero) return;

            // 전체 회신 — the basic attack splashes onto everything else in reach.
            if (a.traitId == "splash")
            {
                foreach (var other in Monsters.Where(m => m.Alive && m != target && Math.Abs(m.x - target.x) <= 2f * CellW).ToList())
                    Damage(a, other, (int)(dmg * a.traitValue), silent: true);
            }

            // 커피 수혈 — the attacker drinks back a slice of what it dealt.
            if (a.traitId == "lifesteal") Heal(a, (int)(dmg * a.traitValue));
        }

        void StepShots(float dt)
        {
            for (var i = Shots.Count - 1; i >= 0; i--)
            {
                var shot = Shots[i];
                shot.T += dt;
                if (shot.T < shot.Duration) continue;
                Shots.RemoveAt(i);
                Land(shot);
            }
        }

        /// <summary>
        /// 검산 — the player has answered the formula, so the NEXT boss special lands soft.
        ///
        /// It is a single charge and it is spent by the next special whether or not that special
        /// is the one that was announced: holding it over would let a player bank one during a
        /// slow move and cash it against the heavy one.
        /// </summary>
        public bool Braced { get; private set; }

        public void MarkBraced() => Braced = true;

        /// <summary>
        /// Runs whichever scripted moves are due on this boss's Nth attack, and ANNOUNCES the
        /// ones due on the next.
        ///
        /// The telegraph is new here and it is what 괄호 수식 hangs on: the web opens the formula
        /// the moment a warning appears, and this port had no warning at all — every special
        /// simply landed on the beat it was due. A move you cannot see coming is one no reaction
        /// can answer, so the minigame had nothing to attach to.
        /// </summary>
        void BossTurn(Combatant b)
        {
            b.attackCount++;
            foreach (var s in b.boss.specials)
            {
                if (s.every <= 0 || b.attackCount % s.every != 0) continue;
                Events.Enqueue(new BattleEvent { kind = EventKind.Skill, actor = b, text = s.name });
                FireBossMove(b, s.kind);
            }

            // Look one swing ahead. When two specials coincide the rarer one is announced, which
            // is the same tie-break the web uses for which one fires.
            BossSpecialDef next = null;
            foreach (var s in b.boss.specials)
            {
                if (s.every <= 0 || (b.attackCount + 1) % s.every != 0) continue;
                if (next == null || s.every > next.every) next = s;
            }
            if (next != null)
                Events.Enqueue(new BattleEvent { kind = EventKind.Warn, actor = b, text = next.name });
        }

        void FireBossMove(Combatant b, string kind)
        {
            var alive = Heroes.Where(h => h.Alive).ToList();
            if (alive.Count == 0) return;

            // 검산: a correct answer takes BALANCE.BRACE.reduce off this one move. It is applied
            // by scaling the boss's attack for the duration of the move and putting it back
            // afterwards, so every branch below is reduced without any of them knowing about it —
            // the alternative is eight multiplications that have to stay in step.
            var braced = Braced;
            var atkWas = b.atk;
            if (braced)
            {
                Braced = false;
                b.atk = Math.Max(1, (int)(b.atk * (1f - GameData.Balance.braceReduce)));
                Events.Enqueue(new BattleEvent { kind = EventKind.Braced, actor = b });
            }
            try
            {

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
                    for (var i = 0; i < 2; i++) Spawn(NewMinion(i), Math.Min(FieldW + 60f, b.x + 64f + i * 58f));
                    break;
            }
            }
            finally { if (braced) b.atk = atkWas; }
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

        /// <summary>Queues one effect for the renderer. `big` is 1 for the heavy version.</summary>
        void Fx(FxKind kind, float x, float y, Color color, float radius = 0f) =>
            Events.Enqueue(new BattleEvent { kind = EventKind.Fx, fx = kind, x = x, y = y, color = color, radius = radius });

        /// <summary>
        /// 엄호 — the tank in front steps in front of a hit meant for someone else.
        ///
        /// Ported from the web build's #splitToTank. It is not a damage share: it either happens or
        /// it does not, and when it does the ally takes nothing at all while the tank eats the whole
        /// hit at a reduction. Both the chance and the reduction climb with the tank's ★.
        ///
        /// The part that matters is the second rule. A pure chance roll left the tank worth *less*
        /// than another attacker in the web build's own measurements, because healing covers chip
        /// damage and only a one-shot actually loses a fight — and a coin flip cannot be relied on to
        /// catch the one-shot. So a blow that would put a colleague down is intercepted for certain,
        /// on a cooldown. That is what makes fielding a tank a decision rather than a tax.
        /// </summary>
        void SplitToTank(Combatant from, Combatant to, int amount, bool crit)
        {
            var b = GameData.Balance;
            if (b == null || to.side != Side.Hero || to.role == "tank") { Damage(from, to, amount, crit: crit); return; }

            // The frontmost living tank that is not the target.
            Combatant tank = null;
            foreach (var h in Heroes)
                if (h.Alive && h.role == "tank" && h != to && (tank == null || h.x > tank.x)) tank = h;
            if (tank == null) { Damage(from, to, amount, crit: crit); return; }

            var step = Math.Max(0, tank.star - 1);
            var chance = Math.Min(b.tankChanceMax, b.tankChance + b.tankChancePerStar * step);
            var reduce = Math.Min(b.tankReduceMax, b.tankReduce + b.tankReducePerStar * step);
            var taken = (int)(amount * (1f - reduce));

            var lethal = amount >= to.hp;
            if (lethal && Elapsed >= tank.guardAt + b.tankSaveCd && tank.hp > taken)
            {
                tank.guardAt = Elapsed;
                Damage(from, tank, taken);
                Fx(FxKind.Ring, tank.x, tank.y - 10f, new Color(0.36f, 0.68f, 0.89f), 70f);
                Events.Enqueue(new BattleEvent { kind = EventKind.Skill, actor = tank, text = "엄호!" });
                return;
            }

            if (Random.value >= chance) { Damage(from, to, amount, crit: crit); return; }

            Damage(from, tank, taken);
            Fx(FxKind.Puff, tank.x, tank.y - 14f, new Color(0.36f, 0.68f, 0.89f));
            Events.Enqueue(new BattleEvent { kind = EventKind.Skill, actor = tank, text = "엄호" });
        }

        void Damage(Combatant from, Combatant to, int amount, bool silent = false, bool crit = false)
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
            if (!silent) Events.Enqueue(new BattleEvent { kind = EventKind.Damage, actor = from, target = to, amount = amount, crit = crit });
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
                    // A death is worth a burst and a shove of the camera — the moment the fight
                    // actually resolves should be the loudest one in it.
                    Fx(FxKind.Sparkle, to.x, to.y - 20f, new Color(0.98f, 0.85f, 0.45f));
                    AddShake(to.boss != null ? 10f : to.elite ? 5f : 2f);

                    var worth = StatMath.StageGold(Stage) * (to.elite ? 3f : to.atk == 0 ? 5f : 1f);
                    GoldEarned += (int)(worth * (1f + _goldBonus));

                    // 보석 드롭 — a small chance per kill, tripled on elites, and the only thing
                    // 성과급 제도 buys. Paid per kill rather than per clear on purpose: a run that
                    // stalls against a wall still earns, which is what keeps deep farming worth it.
                    var chance = (GameData.Balance?.gemDropBase ?? 0.005f) + _gemDropBonus;
                    if (to.elite) chance *= 3f;
                    if (Random.value < chance) GemsDropped++;
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
        /// <summary>Capture pass only: fire member i's EX now, cost and cooldown aside.</summary>
        public bool DebugFire(int i)
        {
            if (i < 0 || i >= Heroes.Count || !Heroes[i].Alive) return false;
            Cost = MaxCost; Heroes[i].skillTimer = 0f;
            return FireSkill(Heroes[i]);
        }

        public bool FireSkill(Combatant h)
        {
            if (!h.SkillReady || !CanAfford(h)) return false;
            var def = GameData.Hero(h.heroId);
            var skill = GameData.Skill(def?.skillType);
            if (def == null || skill == null) return false;

            Cost -= CostOf(h);
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
