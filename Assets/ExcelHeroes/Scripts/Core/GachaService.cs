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
        public bool isPickup;       // landed on today's featured card
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

        /// <param name="pickup">the 픽업 모집 banner (half of S / A on the featured card, 모집 포인트 accrue);
        /// false is 일반 모집 — every card of the grade equally, no points (they buy the featured card)</param>
        public static PullResult Pull(PlayerState p, bool pickup = true)
        {
            var grade = RollGrade(p, out var pityReason);
            var pool = GameData.OfGrade(grade).ToList();
            if (pool.Count == 0) pool = GameData.Heroes;           // data safety net, never expected

            // 오늘의 픽업: half of every S and A lands on the featured card instead of rolling
            // uniformly across the grade. This is what makes a banner worth showing up for.
            var featured = GameData.Featured(grade, DateTime.UtcNow);
            var onPickup = pickup && featured != null && Random.value < (GameData.Pickup?.rate ?? 0.5f);
            var hero = onPickup ? featured : pool[Random.Range(0, pool.Count)];

            // 모집 포인트 accrue on every pull and never expire, so even the worst run converges
            // on the card eventually — the floor under the floor.
            if (pickup) p.sparkPoints++;

            p.totalPulls++;
            p.pullsSinceA = GameData.GradeRank(grade) >= GameData.GradeRank("A") ? 0 : p.pullsSinceA + 1;
            p.pullsSinceS = grade == "S" ? 0 : p.pullsSinceS + 1;

            var result = new PullResult { hero = hero, grade = grade, pityReason = pityReason, isPickup = onPickup };
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

        public static List<PullResult> PullMany(PlayerState p, int count, bool pickup = true)
        {
            var results = new List<PullResult>(count);
            for (var i = 0; i < count; i++) results.Add(Pull(p, pickup));
            return results;
        }

        // Ten at once is ten singles at once — no discount, as in the reference (1,200 for ten, 120
        // for one). The exported gachaTenCost (900) is ignored on purpose, so a re-export of the
        // web balance does not bring the discount back.
        public static int CostFor(int count) => GameData.Balance.gachaSingleCost * count;

        public static bool CanAfford(PlayerState p, int count) => p.gems >= CostFor(count);

        /// <summary>Spends gems and rolls. Returns null when the player cannot pay.</summary>
        public static List<PullResult> Buy(PlayerState p, int count, bool pickup = true)
        {
            if (!CanAfford(p, count)) return null;
            p.gems -= CostFor(count);
            return PullMany(p, count, pickup);
        }

        /// <summary>Pulls left before each floor triggers — shown on the banner so the player can plan.</summary>
        public static (int toA, int toS) PityRemaining(PlayerState p) =>
            (Math.Max(0, GameData.Balance.pityA - p.pullsSinceA),
             Math.Max(0, GameData.Balance.pityS - p.pullsSinceS));

        /// <summary>
        /// 모집 포인트 교환 — spend banked points to simply take the featured card. The points never
        /// expire, so a player who keeps missing still gets there; it is the promise that chasing a
        /// specific character has an end.
        /// </summary>
        public static bool CanSpark(PlayerState p, string grade) =>
            GameData.Featured(grade, DateTime.UtcNow) != null && p.sparkPoints >= GameData.SparkCost(grade);

        public static PullResult Spark(PlayerState p, string grade)
        {
            var hero = GameData.Featured(grade, DateTime.UtcNow);
            if (hero == null) return null;
            var cost = GameData.SparkCost(grade);
            if (p.sparkPoints < cost) return null;

            p.sparkPoints -= cost;
            var result = new PullResult { hero = hero, grade = grade, isPickup = true, pityReason = "모집 포인트 교환" };

            var owned = p.Find(hero.id);
            if (owned == null)
            {
                owned = new OwnedHero(hero.id);
                p.owned.Add(owned);
                result.isNew = true;
                p.AddToParty(hero.id);
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
    }
}
