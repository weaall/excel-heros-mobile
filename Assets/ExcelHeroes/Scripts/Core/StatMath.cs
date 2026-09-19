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

        /// <summary>
        /// A hero's level ceiling — from ★ for the 55 recruits, and from the JOB TIER for the main
        /// hero, which is the web's rule and not an embellishment: "주인공은 ★ 대신 직급으로 열린다".
        ///
        /// It has to work that way. ★ is bought with duplicates, 김인턴 is never in the recruit
        /// pool, so he can never be given one. Capping him by ★ leaves him stuck at 80 forever —
        /// and 과장 → 부장 needs level 140, so the last promotion would simply be unreachable.
        /// </summary>
        public static int LevelCap(OwnedHero o)
        {
            if (o == null) return 80;

            // 각성 raises the ceiling on top of whatever set it — that is most of why it is worth
            // 400 cards on an S.
            var awaken = o.awakened ? GameData.Balance.awakenLevelCap : 0;

            if (o.id != GameData.MainId) return LevelCap(o.star) + awaken;

            var t = GameData.Balance.mainLevelCapByTier;
            var tier = PromotionService.Job(Game.Player)?.tier ?? 0;
            if (t == null || t.Length == 0) return LevelCap(o.star) + awaken;
            return t[Math.Clamp(tier, 0, t.Length - 1)] + awaken;
        }

        /// <summary>
        /// 각성 — a ★5 card spends 강화 카드 once and is permanently stronger: +25% ATK and HP,
        /// trait x1.5, skill x1.25, and +50 levels of ceiling. It cannot be undone, which is the
        /// point: it is where a finished card goes when there is nothing left to buy for it.
        /// </summary>
        public static int AwakenCost(OwnedHero o)
        {
            var b = GameData.Balance;
            var grade = GameData.Hero(o?.id)?.grade;
            var i = grade == null || b.awakenCostGrades == null
                  ? -1 : Array.IndexOf(b.awakenCostGrades, grade);
            if (i < 0 || b.awakenCostValues == null || i >= b.awakenCostValues.Length) return int.MaxValue;
            return b.awakenCostValues[i];
        }

        public static bool CanAwaken(OwnedHero o, PlayerState p) =>
            o != null && !o.awakened && o.star >= GameData.Balance.awakenStar && p.cards >= AwakenCost(o);

        public static bool Awaken(OwnedHero o, PlayerState p)
        {
            if (!CanAwaken(o, p)) return false;
            p.cards -= AwakenCost(o);
            o.awakened = true;
            return true;
        }

        /// <summary>
        /// 스킬 레벨 — the other thing 강화 카드 buy, and the one that scales with use rather than
        /// with rarity. Each level adds 10% power and takes 3% off the cooldown; the cost per level
        /// rises with the level and with the grade.
        /// </summary>
        public static int SkillUpCost(OwnedHero o)
        {
            var b = GameData.Balance;
            if (o == null || o.skillLv >= b.skillLevelMax) return int.MaxValue;
            var grade = GameData.Hero(o.id)?.grade;
            var i = grade == null || b.skillCostGrades == null
                  ? -1 : Array.IndexOf(b.skillCostGrades, grade);
            if (i < 0 || b.skillCostValues == null || i >= b.skillCostValues.Length) return int.MaxValue;
            return b.skillCostValues[i] * (o.skillLv + 1);
        }

        public static bool CanUpgradeSkill(OwnedHero o, PlayerState p) =>
            o != null && SkillUnlocked(o) && o.skillLv < GameData.Balance.skillLevelMax
            && p.cards >= SkillUpCost(o);

        public static bool UpgradeSkill(OwnedHero o, PlayerState p)
        {
            if (!CanUpgradeSkill(o, p)) return false;
            p.cards -= SkillUpCost(o);
            o.skillLv++;
            return true;
        }

        /// <summary>How much less time the skill takes to charge, as a multiplier on its cooldown.</summary>
        public static float SkillCooldownMult(OwnedHero o) =>
            Math.Max(0.25f, 1f - (o?.skillLv ?? 0) * GameData.Balance.skillCooldownPerLevel);

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
                    * (o.awakened ? 1f + GameData.Balance.awakenAtk : 1f)
                    * (1f + o.affection * GameData.Balance.affectionBonusPerLevel)
                    * (1f + EquipPct(o.id).Atk / 100f)
                    * (1f + PrestigeService.Bonus(Game.Player));
            return Math.Max(1, (int)MathF.Floor(v));
        }

        /// <summary>
        /// 비품's contribution, read off whoever is playing. It is looked up rather than passed in
        /// because every caller of Atk/Hp already has the hero and none of them has the save — and
        /// a stat that silently ignores equipment is worse than no equipment at all.
        /// </summary>
        static EquipService.Bonus EquipPct(string heroId) =>
            Game.Player == null ? default : EquipService.Stats(Game.Player, heroId);

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
                    * (o.awakened ? 1f + b.awakenHp : 1f)
                    * (1f + o.affection * GameData.Balance.affectionBonusPerLevel)
                    * (1f + EquipPct(o.id).Hp / 100f);
            return Math.Max(1, (int)MathF.Floor(v));
        }

        /// <summary>Trait strength scales with ★ (+12% per star in the web build).</summary>
        public static float TraitValue(OwnedHero o)
        {
            var def = GameData.Hero(o.id);
            var trait = GameData.Trait(def?.trait);
            if (trait == null) return 0f;
            return trait.value * (1f + GameData.Balance.traitPerStar * (o.star - 1))
                   * (o.awakened ? GameData.Balance.awakenTrait : 1f);
        }

        /// <summary>Skills unlock at ★2 and get a boost at ★4 — the reason duplicates matter.</summary>
        public static bool SkillUnlocked(OwnedHero o) => o.star >= GameData.Balance.skillUnlockStar;

        public static float SkillPower(OwnedHero o)
        {
            var def = GameData.Hero(o.id);
            if (def == null || !SkillUnlocked(o)) return 0f;
            var b = GameData.Balance;
            var boosted = o.star >= b.skillBoostStar;
            return def.skillPower
                   * (boosted ? 1.5f : 1f)
                   * (1f + o.skillLv * b.skillPowerPerLevel)
                   * (o.awakened ? b.awakenSkill : 1f)
                   * (1f + EquipPct(o.id).Skill / 100f);
        }

        /// <summary>Total power, the single number the roster sorts and the party header shows.</summary>
        public static int Power(OwnedHero o) => Atk(o) * 2 + Hp(o) / 2;

        /// <summary>
        /// Gold cost of the next level, from the GDD's cost curve. Rarer cards cost more per level,
        /// so a spare D is the cheap way to fill a slot and an S is the investment.
        ///
        /// This is the axis that makes gold a currency: without it the only way a party gets stronger
        /// is pulling, ★ caps out at 2.8x, and the monster curve (1.18 per stage) runs away from the
        /// party within a handful of stages — which is exactly the stalemate the balance bench found.
        /// </summary>
        public static int LevelUpCost(OwnedHero o)
        {
            var b = GameData.Balance;
            var grade = GameData.Hero(o.id)?.grade;
            var tier = Math.Max(1, GameData.GradeRank(grade) + 1);   // D=1 … S=5
            return Math.Max(1, (int)MathF.Floor(b.upgradeCostBase * tier * MathF.Pow(b.upgradeCostGrowth, Math.Max(1, o.level) - 1)));
        }

        public static bool AtLevelCap(OwnedHero o) => o.level >= LevelCap(o);

        /// <summary>Spends gold for one level. Returns false when capped or short of gold.</summary>
        public static bool TryLevelUp(PlayerState player, OwnedHero o)
        {
            if (AtLevelCap(o)) return false;
            var cost = LevelUpCost(o);
            if (player.gold < cost) return false;
            player.gold -= cost;
            o.level++;
            return true;
        }

        /// <summary>
        /// Spends everything affordable in one go, up to the ★ cap. The roster gets big enough that
        /// tapping level-by-level stops being a decision and starts being a chore.
        /// </summary>
        public static int LevelUpMax(PlayerState player, OwnedHero o, int limit = 200)
        {
            var n = 0;
            while (n < limit && TryLevelUp(player, o)) n++;
            return n;
        }

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
                if (def == null || string.IsNullOrEmpty(def.division)) continue;
                byDivision.TryGetValue(def.division, out var n);
                byDivision[def.division] = n + 1;
                if (!string.IsNullOrEmpty(def.role)) roles.Add(def.role);
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
        ///
        /// The fraction was 6% with a 0.35 floor, which left early monsters so harmless that a party
        /// too weak to clear a stage could not lose it either — the balance bench turned up whole rows
        /// of 40/40 timeouts. A fight the player cannot win should kill them and say so, so both the
        /// fraction and the floor are up. The grace period itself is kept: the ramp still starts late.
        /// </summary>
        public static int MonsterAtk(int stage)
        {
            var b = GameData.Balance;
            var ramp = Math.Clamp((stage - b.monsterAtkRampFull) / (float)Math.Max(1, b.monsterAtkRampByStage), 0f, 1f);
            return Math.Max(1, (int)MathF.Floor(MonsterHp(stage) * 0.09f * (0.5f + 0.5f * ramp)));
        }
    }
}
