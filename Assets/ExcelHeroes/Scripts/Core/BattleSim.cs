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

        // transient combat state
        public float burnLeft, burnPerSec;
        public float shield;
        public float hasteLeft, hasteAmount;
        public float tauntLeft;
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
            var hp = StatMath.MonsterHp(Stage);
            var atk = StatMath.MonsterAtk(Stage);
            var isBoss = Wave == WaveCount;

            var count = isBoss ? 1 : _perWave;
            for (var i = 0; i < count; i++)
            {
                var m = new Combatant
                {
                    side = Side.Monster,
                    name = isBoss ? "긴급 티켓" : MonsterName(i),
                    role = "melee",
                    maxHp = isBoss ? hp * 6 : hp,
                    hp = isBoss ? hp * 6 : hp,
                    atk = isBoss ? (int)(atk * 1.6f) : atk,
                    interval = isBoss ? 1.4f : 1.1f,
                    range = 1.1f,
                    x = LaneCells + i * 1.1f,
                };
                Monsters.Add(m);
                Events.Enqueue(new BattleEvent { kind = EventKind.Spawn, actor = m });
            }
        }

        static readonly string[] MonsterNames = { "순환 참조", "#N/A 오류", "깨진 수식", "중복 행", "미믹 상자" };
        static string MonsterName(int i) => MonsterNames[i % MonsterNames.Length];

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

            foreach (var m in Monsters.Where(m => m.Alive))
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
            if (c.tauntLeft > 0f) c.tauntLeft -= dt;
            if (c.hasteLeft <= 0f) c.hasteAmount = 0f;
            if (c.tauntLeft <= 0f) c.dmgReduction = 0f;

            // The ops perk keeps a slow trickle of health going so a healer-less party is not doomed.
            if (c.side == Side.Hero && Perk("regen") > 0f && c.hp < c.maxHp)
                c.hp = Math.Min(c.maxHp, c.hp + (int)MathF.Ceiling(c.maxHp * Perk("regen") * dt));
        }

        void StepAttack(Combatant a, float dt, List<Combatant> enemies)
        {
            var target = NearestTarget(a, enemies);
            if (target == null) return;
            if (Math.Abs(target.x - a.x) > a.range) return;

            a.attackTimer -= dt * (1f + a.hasteAmount);
            if (a.attackTimer > 0f) return;
            a.attackTimer = a.interval;

            var dmg = a.atk;
            if (a.side == Side.Hero)
            {
                if (Random.value < Perk("crit")) dmg *= 2;
                if (target.maxHp > StatMath.MonsterHp(Stage) * 3) dmg = (int)(dmg * (1f + Perk("boss")));
            }
            Damage(a, target, dmg);
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
                    foreach (var a in allies) { Heal(a, (int)(a.maxHp * power / 100f)); a.hasteLeft = 5f; a.hasteAmount = 0.35f; }
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
