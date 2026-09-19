using System;
using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;

namespace ExcelHeroes.Core
{
    /// <summary>Synergy in effect for the current party, recomputed whenever the party changes.</summary>
    public class SynergyResult
    {
        public float atkBonus;                       // additive fraction, e.g. 0.17 = +17%
        public float hpBonus;
        public bool balanced;                        // all four roles present
        public readonly List<string> lines = new();  // human-readable, shown on the party screen
        public readonly Dictionary<string, float> perks = new();  // perk key → value
    }

    /// <summary>
    /// Stat and synergy formulas, ported verbatim from the web build's balance.js so a card is worth
    /// the same in both clients:
    ///   ATK = floor(gradeBase * roleMult * atkGrowth^(level-1) * starMult(star))
    /// The web build also folds in enhance levels and equipment; those systems are not in this slice,
    /// so their multipliers are simply absent rather than approximated.
    /// </summary>
    public static class StatMath
    {
        public static float StarMult(int star)
        {
            var t = GameData.Balance.starMult;
            if (t == null || t.Length == 0) return 1f;
            return t[Math.Clamp(star, 1, t.Length) - 1];
        }

        public static int LevelCap(int star)
        {
            var t = GameData.Balance.levelCapByStar;
            if (t == null || t.Length == 0) return 80;
            return t[Math.Clamp(star, 1, t.Length) - 1];
        }

        public static int Atk(OwnedHero o)
        {
            var def = GameData.Hero(o.id);
            var grade = GameData.Grade(def?.grade);
            var role = GameData.Role(def?.role);
            if (grade == null || role == null) return 0;
            var b = GameData.Balance;
            var v = grade.baseAtk * role.atk
                    * MathF.Pow(b.heroAtkGrowth, Math.Max(1, o.level) - 1)
                    * StarMult(o.star)
                    * (1f + o.affection * 0.01f);
            return Math.Max(1, (int)MathF.Floor(v));
        }

        public static int Hp(OwnedHero o)
        {
            var def = GameData.Hero(o.id);
            var grade = GameData.Grade(def?.grade);
            var role = GameData.Role(def?.role);
            if (grade == null || role == null) return 0;
            var b = GameData.Balance;
            var v = grade.baseHp * role.hp
                    * MathF.Pow(b.heroHpGrowth, Math.Max(1, o.level) - 1)
                    * StarMult(o.star)
                    * (1f + o.affection * 0.01f);
            return Math.Max(1, (int)MathF.Floor(v));
        }

        /// <summary>Trait strength scales with ★ (+12% per star in the web build).</summary>
        public static float TraitValue(OwnedHero o)
        {
            var def = GameData.Hero(o.id);
            var trait = GameData.Trait(def?.trait);
            if (trait == null) return 0f;
            return trait.value * (1f + GameData.Balance.traitPerStar * (o.star - 1));
        }

        /// <summary>Skills unlock at ★2 and get a boost at ★4 — the reason duplicates matter.</summary>
        public static bool SkillUnlocked(OwnedHero o) => o.star >= GameData.Balance.skillUnlockStar;

        public static float SkillPower(OwnedHero o)
        {
            var def = GameData.Hero(o.id);
            if (def == null || !SkillUnlocked(o)) return 0f;
            var boosted = o.star >= GameData.Balance.skillBoostStar;
            return def.skillPower * (boosted ? 1.5f : 1f);
        }

        /// <summary>Total power, the single number the roster sorts and the party header shows.</summary>
        public static int Power(OwnedHero o) => Atk(o) * 2 + Hp(o) / 2;

        /// <summary>
        /// 부문 시너지: 2+ heroes of one division buff the whole party, 3+ buff it more, and fielding
        /// all four roles adds health on top. Each qualifying division also unlocks its perk.
        /// </summary>
        public static SynergyResult Synergy(PlayerState p)
        {
            var r = new SynergyResult();
            var s = GameData.Synergy;
            if (s == null) return r;

            var members = p.PartyMembers().ToList();
            var byDivision = new Dictionary<string, int>();
            var roles = new HashSet<string>();

            foreach (var o in members)
            {
                var def = GameData.Hero(o.id);
                if (def == null) continue;
                byDivision.TryGetValue(def.division, out var n);
                byDivision[def.division] = n + 1;
                roles.Add(def.role);
            }

            foreach (var (divisionId, count) in byDivision.OrderByDescending(kv => kv.Value))
            {
                if (count < 2) continue;
                var name = GameData.Division(divisionId)?.name ?? divisionId;
                if (count >= 3)
                {
                    r.atkBonus += s.trioAtk; r.hpBonus += s.trioHp;
                    r.lines.Add($"{name} {count}인 · 공격 +{s.trioAtk:P0} 체력 +{s.trioHp:P0}");
                }
                else
                {
                    r.atkBonus += s.pairAtk; r.hpBonus += s.pairHp;
                    r.lines.Add($"{name} 2인 · 공격 +{s.pairAtk:P0}");
                }

                var perk = GameData.Perk(divisionId);
                if (perk != null)
                {
                    r.perks.TryGetValue(perk.key, out var have);
                    r.perks[perk.key] = have + perk.value;
                    r.lines.Add($"　└ {perk.desc}");
                }
            }

            if (roles.Count >= 4)
            {
                r.balanced = true;
                r.hpBonus += s.balancedHp;
                r.lines.Add($"균형 편성 · 체력 +{s.balancedHp:P0}");
            }

            return r;
        }

        /// <summary>Monster health for a stage, straight from the GDD formula.</summary>
        public static int MonsterHp(int stage)
        {
            var b = GameData.Balance;
            return (int)MathF.Floor(b.monsterHpBase * MathF.Pow(b.monsterHpGrowth, Math.Max(1, stage) - 1));
        }

        public static int StageGold(int stage)
        {
            var b = GameData.Balance;
            return (int)MathF.Floor(b.goldBase * MathF.Pow(b.goldGrowth, Math.Max(1, stage) - 1));
        }

        /// <summary>
        /// Monster attack ramps in over the first stages so a lone starting hero is not deleted, then
        /// tracks a fraction of monster health (same shape as MONSTER_ATK_RAMP in the web build).
        /// </summary>
        public static int MonsterAtk(int stage)
        {
            var b = GameData.Balance;
            var ramp = Math.Clamp((stage - b.monsterAtkRampFull) / (float)Math.Max(1, b.monsterAtkRampByStage), 0f, 1f);
            return Math.Max(1, (int)MathF.Floor(MonsterHp(stage) * 0.06f * (0.35f + 0.65f * ramp)));
        }
    }
}
