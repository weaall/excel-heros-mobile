using System;
using ExcelHeroes.Data;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 스카우트 and 조각 변환 — ported from the web build's `scoutInfo` / `scoutShard` /
    /// `convertShards`, with `scoutCost` from `balance.js`.
    ///
    /// These are the two ends of the duplicate economy, and the game needs both.
    ///
    /// **스카우트** buys one duplicate with gold, three times a day. It is the only thing in the
    /// game that turns gold into ★, and without it gold has exactly one use — levelling — while ★
    /// depends entirely on the gacha. A player sitting on millions with a card one copy short of
    /// ★4 has nothing to spend it on, which is the state this build was in.
    ///
    /// **변환** is the other direction: duplicates of a card already at ★5 are dead weight, and
    /// this turns them into 강화 카드, which 각성 and 스킬 레벨 now have plenty of uses for.
    ///
    /// The price is deliberately tied to the levelling curve rather than being a flat number: it is
    /// a fraction of everything it costs to walk that card to its current ceiling, so it keeps pace
    /// into the late game instead of becoming pocket change.
    /// </summary>
    public static class ScoutService
    {
        /// <summary>
        /// What one duplicate costs. The web's `scoutCost`: the accumulated gold it takes to level
        /// this card to its cap, times a percentage, times a per-grade multiplier — floored at a
        /// minimum so an early D is not free.
        /// </summary>
        public static int Cost(PlayerState p, OwnedHero o)
        {
            var b = GameData.Balance;
            var def = GameData.Hero(o?.id);
            if (o == null || def == null) return int.MaxValue;

            var cap = StatMath.LevelCap(o);

            // The sunk cost of the whole ladder, not of the next rung — and WITHOUT the grade
            // tier, because the grade is applied once at the end by GradeMult. The web's
            // `upgradeCost(level)` takes only the level for exactly this reason; multiplying by
            // tier here as well charged an A card four times over.
            double sunk = 0;
            for (var lv = 1; lv < cap; lv++)
                sunk += Math.Floor(b.upgradeCostBase * Math.Pow(b.upgradeCostGrowth, lv - 1));

            var cost = Math.Floor(sunk * b.scoutCostPct * GradeMult(def.grade));
            return (int)Math.Min(int.MaxValue, Math.Max(b.scoutMinCost, cost));
        }

        /// <summary>
        /// Whether this card's scout price is one a player could ever pay.
        ///
        /// The price grows with the level ceiling, and the ceiling grows with ★, so it outruns
        /// what this build can even hold: `gold` is an `int`, capped near 2.1 billion, while the
        /// web keeps gold in a double and has no such limit. From ★3 up the honest price is tens
        /// of billions, the clamp pins it to int.MaxValue, and the button is one the player can
        /// look at for the rest of the save without ever affording it.
        ///
        /// This is not a scout problem — it is what happens to the whole late-game economy when
        /// gold is an int — but this is the first screen where it shows, so it is named here and
        /// in the handoff rather than being quietly clamped.
        /// </summary>
        public static bool PriceIsReachable(OwnedHero o) => Cost(null, o) < int.MaxValue;


        static float GradeMult(string grade)
        {
            var b = GameData.Balance;
            var i = grade == null || b.scoutGradeGrades == null
                  ? -1 : Array.IndexOf(b.scoutGradeGrades, grade);
            return i < 0 || b.scoutGradeValues == null || i >= b.scoutGradeValues.Length
                 ? 1f : b.scoutGradeValues[i];
        }

        public static int Left(PlayerState p)
        {
            QuestService.EnsureToday(p);
            return Math.Max(0, GameData.Balance.scoutPerDay - p.scoutUsed);
        }

        /// <summary>Why this card cannot be scouted right now — empty when it can.</summary>
        public static string Blocked(PlayerState p, OwnedHero o)
        {
            if (o == null) return "보유하지 않은 카드입니다";
            if (o.id == GameData.MainId) return "주인공은 조각을 쓰지 않습니다";
            if (o.star >= GameData.Balance.maxStar) return "이미 ★ 최대입니다";
            if (!PriceIsReachable(o)) return "이 ★에서는 스카우트 값이 보유 가능한 골드를 넘습니다";
            if (Left(p) <= 0) return $"오늘 스카우트를 다 썼습니다 (하루 {GameData.Balance.scoutPerDay}회)";
            if (p.gold < Cost(p, o)) return $"골드가 모자랍니다";
            return "";
        }

        public static bool Can(PlayerState p, OwnedHero o) => Blocked(p, o).Length == 0;

        /// <summary>Buys one duplicate. Gold out, one copy in, one of the day's three used.</summary>
        public static bool Scout(PlayerState p, OwnedHero o)
        {
            if (!Can(p, o)) return false;
            p.gold -= Cost(p, o);
            o.copies++;
            p.scoutUsed++;
            return true;
        }

        /// <summary>What one spare copy of this card is worth in 강화 카드.</summary>
        public static int CardValue(OwnedHero o)
        {
            var b = GameData.Balance;
            var grade = GameData.Hero(o?.id)?.grade;
            var i = grade == null || b.shardCardGrades == null
                  ? -1 : b.shardCardGrades.IndexOf(grade);
            return i < 0 || b.shardCardValues == null || i >= b.shardCardValues.Count
                 ? 1 : b.shardCardValues[i];
        }

        /// <summary>
        /// Copies a card cannot use. At ★5 that is all of them; below ★5 it is whatever is left
        /// after the next promotion is paid for, so converting never eats a promotion.
        /// </summary>
        public static int Spare(OwnedHero o)
        {
            if (o == null) return 0;
            if (o.star >= GameData.Balance.maxStar) return o.copies;
            var need = GachaService.PromoteCost(o);
            return need <= 0 ? o.copies : Math.Max(0, o.copies - need);
        }

        /// <summary>Turns spare copies into 강화 카드. Returns how many cards were paid.</summary>
        public static int Convert(PlayerState p, OwnedHero o, int count = -1)
        {
            var spare = Spare(o);
            var take = count < 0 ? spare : Math.Min(spare, count);
            if (take <= 0) return 0;

            var cards = take * CardValue(o);
            o.copies -= take;
            p.cards += cards;
            return cards;
        }
    }
}
