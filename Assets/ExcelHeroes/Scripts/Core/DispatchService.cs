using System;
using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;
using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 출장 — bench staff sent away for a few hours, ported from the web build's
    /// `dispatchInfo` / `startDispatch` / `claimDispatch`.
    ///
    /// It is the one system that pays for owning people you do not field. Gems scale with who was
    /// sent, cards with how far the run has got, and the travellers come back having grown closer
    /// to the player — so a spare S is worth pulling even when the party is full, which is the
    /// whole reason the roster has fifty-five cards in it.
    ///
    /// The clock is real time, not play time: a run started before bed finishes overnight. That is
    /// also why the block outlives the daily reset — only the day's start count resets.
    /// </summary>
    public static class DispatchService
    {
        public readonly struct Info
        {
            public readonly bool Active;
            public readonly List<string> HeroIds;
            public readonly long Remaining;   // seconds
            public readonly bool Done;
            public readonly int StartsLeft;
            public readonly List<OwnedHero> Bench;
            public readonly bool CanStart;

            public Info(bool active, List<string> ids, long remaining, bool done,
                        int left, List<OwnedHero> bench, bool canStart)
            {
                Active = active; HeroIds = ids; Remaining = remaining; Done = done;
                StartsLeft = left; Bench = bench; CanStart = canStart;
            }
        }

        public static bool IsAway(PlayerState p, string heroId) =>
            p != null && p.dispatchHeroIds.Contains(heroId);

        public static Info Read(PlayerState p)
        {
            var b = GameData.Balance;
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var count = p.dispatchDate == today ? p.dispatchCount : 0;

            var active = p.dispatchHeroIds.Count > 0;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var remaining = active ? Math.Max(0, p.dispatchEndsUnix - now) : 0;

            // The bench is everyone owned who is neither in the party nor already away. 김인턴 is
            // excluded: the main hero is the player's own desk and cannot be sent anywhere.
            var bench = p.owned
                .Where(o => o.id != "main" && !p.party.Contains(o.id) && !p.dispatchHeroIds.Contains(o.id))
                .OrderByDescending(StatMath.Power)
                .ToList();

            var left = Math.Max(0, b.dispatchMaxPerDay - count);
            return new Info(active, new List<string>(p.dispatchHeroIds), remaining,
                            active && remaining == 0, left, bench,
                            !active && left > 0 && bench.Count > 0);
        }

        /// <summary>What a given group would bring back. Used for the preview and for the payout,
        /// so the number a player is shown is the number they get.</summary>
        public static (int Gems, int Cards) Preview(PlayerState p, IEnumerable<string> heroIds)
        {
            var b = GameData.Balance;
            var gems = b.dispatchGemsBase;
            foreach (var id in heroIds)
            {
                var def = GameData.Hero(id);
                if (def == null) continue;
                var i = b.dispatchGemGrades.IndexOf(def.grade);
                if (i >= 0 && i < b.dispatchGemValues.Count) gems += b.dispatchGemValues[i];
            }
            var phase = Math.Max(1, (Math.Max(1, p.maxCleared) - 1) / Math.Max(1, b.bossEvery) + 1);
            return (gems, phase * b.dispatchCardsPerPhase);
        }

        public static bool Start(PlayerState p, IEnumerable<string> heroIds)
        {
            var b = GameData.Balance;
            var info = Read(p);
            if (!info.CanStart) return false;

            var pick = heroIds.Distinct()
                              .Where(id => info.Bench.Any(o => o.id == id))
                              .Take(b.dispatchSlots)
                              .ToList();
            if (pick.Count == 0) return false;

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            p.dispatchHeroIds = pick;
            p.dispatchEndsUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                                 + (long)(b.dispatchHours * 3600);
            p.dispatchCount = (p.dispatchDate == today ? p.dispatchCount : 0) + 1;
            p.dispatchDate = today;
            return true;
        }

        /// <summary>Brings them back. Returns what they brought, or null if they are still out.</summary>
        public static (int Gems, int Cards, List<string> HeroIds)? Claim(PlayerState p)
        {
            var info = Read(p);
            if (!info.Done) return null;

            var ids = new List<string>(p.dispatchHeroIds);
            var (gems, cards) = Preview(p, ids);

            p.gems += gems;
            p.cards += cards;
            foreach (var id in ids)
            {
                var o = p.Find(id);
                if (o != null) AffectionService.AddXp(o, GameData.Balance.dispatchAffectionXp);
            }

            p.dispatches++;
            p.dispatchHeroIds = new List<string>();
            p.dispatchEndsUnix = 0;
            return (gems, cards, ids);
        }

        /// <summary>The ad offer that ends a run early — it brings the clock forward, nothing else.</summary>
        public static bool FinishNow(PlayerState p)
        {
            var info = Read(p);
            if (!info.Active || info.Done) return false;
            p.dispatchEndsUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return true;
        }

        public static string Remaining(long seconds)
        {
            var h = seconds / 3600;
            var m = seconds % 3600 / 60;
            if (h > 0) return $"{h}시간 {m}분";
            if (m > 0) return $"{m}분";
            return "곧 복귀";
        }
    }
}
