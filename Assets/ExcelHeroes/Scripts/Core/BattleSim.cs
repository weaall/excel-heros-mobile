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
        public float attackTimer;
        public float skillTimer;       // counts down to ready
        public float skillCooldown;    // 0 = this fighter has no skill
        public bool Alive => hp > 0;

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
        public float dmgReduction;

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
        const float MonsterSpeed = 1.6f;      // cells per second
        const float HeroFrontX = 4.5f;

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

        readonly SynergyResult _synergy;
        readonly int _perWave;

        public BattleSim(PlayerState player, int stage, int waves = 3, int monstersPerWave = 4)
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

                Heroes.Add(new Combatant
                {
                    side = Side.Hero,
                    heroId = owned.id,
                    name = def.name,
                    role = def.role,
                    maxHp = (int)(StatMath.Hp(owned) * (1f + _synergy.hpBonus)),
                    hp = (int)(StatMath.Hp(owned) * (1f + _synergy.hpBonus)),
                    atk = (int)(StatMath.Atk(owned) * (1f + _synergy.atkBonus)),
                    interval = role.interval,
                    range = role.range,
                    x = HeroFrontX - slot * 0.75f,
                    skillCooldown = cooldown,
                    skillTimer = cooldown * 0.35f,   // a little charge at the bell so wave 1 has a beat
                });
                slot++;
            }

            SpawnWave();
        }

        float Perk(string key) => _synergy.perks.TryGetValue(key, out var v) ? v : 0f;

        void SpawnWave()
        {
            Wave++;
            if (Wave == WaveCount) SpawnBoss();
            else for (var i = 0; i < _perWave; i++) Spawn(NewMinion(i), LaneCells + i * 1.1f);
        }

        Combatant NewMinion(int slot)
        {
            var hp = StatMath.MonsterHp(Stage);
            return new Combatant
            {
                side = Side.Monster,
                name = GameData.MonsterForStage(Stage, slot)?.name ?? "스프레드시트 오류",
                role = "melee",
                maxHp = hp, hp = hp,
                atk = StatMath.MonsterAtk(Stage),
                interval = 1.1f,
                range = 1.1f,
            };
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
                maxHp = (int)(hp * 6 * (def?.hp ?? 1f)),
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

            foreach (var c in Heroes.Concat(Monsters)) TickStatus(c, dt);

            foreach (var h in Heroes.Where(h => h.Alive))
            {
                if (h.skillCooldown > 0f && h.skillTimer > 0f) h.skillTimer -= dt;
                if (AutoSkill && h.SkillReady) FireSkill(h);
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
                GoldEarned += StatMath.StageGold(Stage) * _perWave;
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

            // The ops perk keeps a slow trickle of health going so a healer-less party is not doomed.
            if (c.side == Side.Hero && Perk("regen") > 0f && c.hp < c.maxHp)
                c.hp = Math.Min(c.maxHp, c.hp + (int)MathF.Ceiling(c.maxHp * Perk("regen") * dt));
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
                if (Random.value < Perk("crit")) dmg *= 2;
                if (target.boss != null) dmg = (int)(dmg * (1f + Perk("boss")));
            }
            Damage(a, target, dmg);

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
            amount = (int)(amount * (1f - to.dmgReduction));
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
            var power = def.skillPower * (1f + Perk("skill"));
            var live = Monsters.Where(m => m.Alive).ToList();
            var allies = Heroes.Where(a => a.Alive).ToList();

            switch (def.skillType)
            {
                case "strike":
                    if (live.Count > 0) Damage(h, live[0], (int)(h.atk * power));
                    break;
                case "execute":
                {
                    var t = live.FirstOrDefault();
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
                    var mult = 1f;
                    foreach (var m in live.Take(3)) { Damage(h, m, (int)(h.atk * power * mult)); mult *= 0.7f; }
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
