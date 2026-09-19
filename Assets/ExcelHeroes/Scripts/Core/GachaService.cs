using System;
using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;
using Random = UnityEngine.Random;

namespace ExcelHeroes.Core
{
    /// <summary>What one pull produced, in the shape the reveal animation needs.</summary>
    public class PullResult
    {
        public HeroDef hero;
        public string grade;
        public bool isNew;          // first copy — the reveal plays the big version
        public int copiesAfter;     // spare copies held after this pull
        public bool promoted;       // this pull pushed the card up a ★
        public int starAfter;
        public string pityReason;   // "" | "천장 A" | "천장 S"
    }

    /// <summary>
    /// Pure gacha logic, ported from the web build's GachaManager so both clients roll the same way:
    /// published rates, a 50-pull A-or-better floor and a 120-pull S floor, duplicates stacking into ★.
    /// Deliberately free of Unity UI so it can be unit tested.
    /// </summary>
    public static class GachaService
    {
        /// <summary>Rolls a grade honouring both pity counters. Counters are updated by Pull().</summary>
        static string RollGrade(PlayerState p, out string pityReason)
        {
            var b = GameData.Balance;
            pityReason = "";

            if (p.pullsSinceS + 1 >= b.pityS) { pityReason = "천장 S"; return "S"; }
            if (p.pullsSinceA + 1 >= b.pityA)
            {
                pityReason = "천장 A";
                // The A floor guarantees "A or better", so an S can still drop on the same pull.
                return Random.value < SRateWithinTopTier() ? "S" : "A";
            }

            var roll = Random.value;
            var acc = 0f;
            foreach (var g in GameData.Grades)       // D → S, the order the rates were authored in
            {
                acc += g.rate;
                if (roll < acc) return g.id;
            }
            return GameData.Grades[0].id;
        }

        /// <summary>Relative chance of S inside the A-or-better floor, so the floor does not erase S pulls.</summary>
        static float SRateWithinTopTier()
        {
            var a = GameData.Grade("A")?.rate ?? 0.05f;
            var s = GameData.Grade("S")?.rate ?? 0.005f;
            return a + s <= 0f ? 0f : s / (a + s);
        }

        public static PullResult Pull(PlayerState p)
        {
            var grade = RollGrade(p, out var pityReason);
            var pool = GameData.OfGrade(grade).ToList();
            if (pool.Count == 0) pool = GameData.Heroes;           // data safety net, never expected
            var hero = pool[Random.Range(0, pool.Count)];

            p.totalPulls++;
            p.pullsSinceA = GameData.GradeRank(grade) >= GameData.GradeRank("A") ? 0 : p.pullsSinceA + 1;
            p.pullsSinceS = grade == "S" ? 0 : p.pullsSinceS + 1;

            var result = new PullResult { hero = hero, grade = grade, pityReason = pityReason };
            var owned = p.Find(hero.id);
            if (owned == null)
            {
                owned = new OwnedHero(hero.id);
                p.owned.Add(owned);
                result.isNew = true;
                p.AddToParty(hero.id);                            // first cards fill the party for free
            }
            else
            {
                owned.copies++;
                owned.isNew = true;
                result.promoted = TryPromote(owned);
            }

            result.copiesAfter = owned.copies;
            result.starAfter = owned.star;
            return result;
        }

        /// <summary>Spends spare copies for the next ★ when enough have piled up.</summary>
        public static bool TryPromote(OwnedHero owned)
        {
            var def = GameData.Hero(owned.id);
            var grade = GameData.Grade(def?.grade);
            if (grade?.promote == null || owned.star >= GameData.Balance.maxStar) return false;

            var need = PromoteCost(owned);
            if (need <= 0 || owned.copies < need) return false;
            owned.copies -= need;
            owned.star++;
            return true;
        }

        /// <summary>Copies needed for the next ★, or 0 when already maxed.</summary>
        public static int PromoteCost(OwnedHero owned)
        {
            var def = GameData.Hero(owned.id);
            var grade = GameData.Grade(def?.grade);
            if (grade?.promote == null || owned.star >= GameData.Balance.maxStar) return 0;
            var i = owned.star - 1;
            if (i < 0 || i >= grade.promote.Length) return 0;
            // The web build counts promotion in same-card copies; its shard table divided by the
            // unlock size is that copy count.
            return Math.Max(1, grade.promote[i] / Math.Max(1, GameData.Balance.unlockShards));
        }

        public static List<PullResult> PullMany(PlayerState p, int count)
        {
            var results = new List<PullResult>(count);
            for (var i = 0; i < count; i++) results.Add(Pull(p));
            return results;
        }

        public static int CostFor(int count) =>
            count >= 10 ? GameData.Balance.gachaTenCost : GameData.Balance.gachaSingleCost * count;

        public static bool CanAfford(PlayerState p, int count) => p.gems >= CostFor(count);

        /// <summary>Spends gems and rolls. Returns null when the player cannot pay.</summary>
        public static List<PullResult> Buy(PlayerState p, int count)
        {
            if (!CanAfford(p, count)) return null;
            p.gems -= CostFor(count);
            return PullMany(p, count);
        }

        /// <summary>Pulls left before each floor triggers — shown on the banner so the player can plan.</summary>
        public static (int toA, int toS) PityRemaining(PlayerState p) =>
            (Math.Max(0, GameData.Balance.pityA - p.pullsSinceA),
             Math.Max(0, GameData.Balance.pityS - p.pullsSinceS));
    }
}
