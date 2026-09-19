using System;
using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;
using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 방출 — turning a card back into 강화 카드, ported from the web build's `dismiss` and
    /// `dismissCandidates`.
    ///
    /// Fifty-five cards and five slots means most of what a ten-pull returns is a duplicate, and a
    /// collection that only ever grows turns the roster into a wall of cards nobody looks at.
    /// Releasing one pays cards by grade, refunds the gold spent levelling it, and clears its
    /// progress.
    ///
    /// The rails matter more than the payout. The main hero cannot go; nor can anyone in the party;
    /// nor can a card with no duplicate shards, which is what protects the one copy of something
    /// pulled once. A collection game that lets a mis-tap delete a card the player waited a hundred
    /// pulls for has taken the whole reason to keep playing away.
    /// </summary>
    public static class DismissService
    {
        public const string MainHeroId = "main";

        /// <summary>Why this card cannot be released, or "" when it can.</summary>
        public static string Blocked(PlayerState p, string heroId)
        {
            if (heroId == MainHeroId) return "주인공은 방출할 수 없습니다";
            var owned = p.Find(heroId);
            if (owned == null) return "보유하지 않은 사원입니다";
            if (p.party.Contains(heroId)) return "편성에서 먼저 빼주세요";
            if (owned.copies < 1) return "조각이 1개 이상일 때만 방출할 수 있습니다";
            return "";
        }

        public static bool Can(PlayerState p, string heroId) => Blocked(p, heroId) == "";

        /// <summary>Cards a release would pay: a flat bonus plus the shards it carries, valued by grade.</summary>
        public static int Cards(PlayerState p, string heroId)
        {
            var owned = p.Find(heroId);
            var def = GameData.Hero(heroId);
            if (owned == null || def == null || heroId == MainHeroId) return 0;

            var b = GameData.Balance;
            var i = b.shardCardGrades.IndexOf(def.grade);
            var per = i >= 0 && i < b.shardCardValues.Count ? b.shardCardValues[i] : 1;
            return (b.dismissCardBonus + owned.copies) * per;
        }

        /// <summary>
        /// Whether this card's levels would be swept. The button that offers the sweep asks the
        /// same question, so it lives here: a button that lights up for a hero the sweep then skips
        /// reports "회수할 레벨이 없습니다" and looks broken.
        /// </summary>
        public static bool CanReclaim(PlayerState p, OwnedHero o) =>
            o != null && o.level > 1
            && o.id != GameData.MainId
            && !p.party.Contains(o.id)
            && !DispatchService.IsAway(p, o.id)
            && !p.favorites.Contains(o.id);

        /// <summary>
        /// 대기 사원 레벨 회수 — takes every bench hero back to level 1 and returns the gold.
        ///
        /// Levelling the wrong card is the mistake this game makes easiest: gold spent on a D at
        /// Phase 3 is gold not spent on the S pulled at Phase 12, and without this the only way to
        /// get it back is 방출, which destroys the card. The refund is full, so moving gold between
        /// cards costs nothing but the tap.
        ///
        /// Four kinds of hero are left alone: 김인턴, anyone in the party, anyone away on 출장, and
        /// anyone the player has marked 즐겨찾기 — that mark means "I left this one levelled on
        /// purpose", and a sweep that ignored it would be the thing it exists to prevent.
        /// </summary>
        public static (int Heroes, int Gold) ReclaimBench(PlayerState p)
        {
            var heroes = 0;
            var gold = 0;

            foreach (var o in p.owned.ToList())
            {
                if (!CanReclaim(p, o)) continue;

                var back = LevelRefund(p, o.id);
                if (back <= 0) continue;

                p.gold += back;
                o.level = 1;
                gold += back;
                heroes++;
            }

            return (heroes, gold);
        }

        /// <summary>Gold back from the levels bought on this card — every level, at its own price.</summary>
        public static int LevelRefund(PlayerState p, string heroId)
        {
            var owned = p.Find(heroId);
            if (owned == null) return 0;

            var b = GameData.Balance;

            // The grade tier belongs here. StatMath.LevelUpCost charges base * TIER * growth^(l-1)
            // and this was refunding base * growth^(l-1), so dismissing an S card handed back a
            // fifth of what levelling it had cost. The refund has to mirror the charge exactly or
            // the two drift apart silently — nothing fails, the player is simply robbed.
            var tier = Math.Max(1, GameData.GradeRank(GameData.Hero(heroId)?.grade) + 1);
            var rate = b.levelRefund <= 0f ? 1f : b.levelRefund;

            long gold = 0;
            for (var lv = 1; lv < owned.level; lv++)
                gold += (long)Mathf.Floor(b.upgradeCostBase * tier * Mathf.Pow(b.upgradeCostGrowth, lv - 1) * rate);
            return (int)Mathf.Min(gold, int.MaxValue - p.gold);
        }

        /// <summary>Releases one. Returns the cards paid, or 0 when it was not allowed.</summary>
        public static (int Cards, int Gold) Release(PlayerState p, string heroId)
        {
            if (!Can(p, heroId)) return (0, 0);

            var cards = Cards(p, heroId);
            var gold = LevelRefund(p, heroId);
            var owned = p.Find(heroId);

            // Anything the card was wearing goes back in the bag rather than out of the game with
            // it: the items were earned separately and are worth more than the duplicate.
            foreach (var slot in GameData.Equipment.slots)
                p.SetWorn(heroId, slot.id, 0);

            p.owned.Remove(owned);
            p.cards += cards;
            p.gold += gold;
            return (cards, gold);
        }

        /// <summary>
        /// 일괄 방출 — everyone at or below a grade who is safe to release. Sorted weakest first, so
        /// a player reading the list before confirming sees what they are actually giving up.
        /// </summary>
        public static List<OwnedHero> Candidates(PlayerState p, string grade)
        {
            var cut = GameData.GradeRank(grade);
            if (cut < 0) return new List<OwnedHero>();

            return p.owned
                .Where(o => Can(p, o.id))
                .Where(o =>
                {
                    var def = GameData.Hero(o.id);
                    return def != null && GameData.GradeRank(def.grade) <= cut;
                })
                .OrderBy(StatMath.Power)
                .ToList();
        }

        public static (int Count, int Cards, int Gold) ReleaseAll(PlayerState p, string grade)
        {
            int count = 0, cards = 0, gold = 0;
            foreach (var o in Candidates(p, grade).ToList())
            {
                var (c, g) = Release(p, o.id);
                if (c <= 0) continue;
                count++;
                cards += c;
                gold += g;
            }
            return (count, cards, gold);
        }
    }
}
