using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;
using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 업적 and 마일스톤 — ported from the web build's AchievementManager.js and
    /// MilestoneManager.js rather than reinvented, because both are pure functions over the save
    /// and the only thing that matters about them is that they agree with the web's numbers.
    ///
    /// The two differ in shape on purpose. An achievement has tiers and is claimed one tier at a
    /// time, so it keeps paying out; a milestone is a single mark passed once. Between them they
    /// are what gives a long idle run something to collect on the way up.
    /// </summary>
    public static class ProgressService
    {
        // ------------------------------------------------------------------ 업적

        /// <summary>The current value of whatever an achievement is counting.</summary>
        public static long Value(PlayerState p, AchievementDef a)
        {
            switch (a.stat)
            {
                case "collection": return p.owned.Count;
                case "maxCleared": return p.maxCleared;
                case "bestiary":
                    // Elites are recorded under "<id>!" beside their base type; the codex counts
                    // kinds met, so those doubles do not count twice.
                    return p.bestiaryIds.Count(k => !k.EndsWith("!"));
                case "totalKills": return p.totalKills;
                case "totalGold": return p.totalGold;
                case "bossKills": return p.bossKills;
                case "chestsOpened": return p.chestsOpened;
                case "enhances": return p.enhances;
                case "overtimes": return p.overtimes;
                case "playSeconds": return p.playSeconds;
                case "totalPulls": return p.totalPulls;
                default: return 0;
            }
        }

        public static int ClaimedTiers(PlayerState p, string id) => p.AchievementTier(id);

        public static bool IsMaxed(PlayerState p, AchievementDef a) =>
            ClaimedTiers(p, a.id) >= (a.tiers?.Length ?? 0);

        /// <summary>The next tier's target, or -1 when every tier has been taken.</summary>
        public static long NextTarget(PlayerState p, AchievementDef a) =>
            IsMaxed(p, a) ? -1 : a.tiers[ClaimedTiers(p, a.id)];

        public static bool CanClaim(PlayerState p, AchievementDef a)
        {
            var target = NextTarget(p, a);
            return target >= 0 && Value(p, a) >= target;
        }

        public static int ClaimableCount(PlayerState p) =>
            GameData.Achievements.Count(a => CanClaim(p, a));

        /// <summary>Takes the next tier. Returns the gems paid, or 0 when there was nothing to take.</summary>
        public static int Claim(PlayerState p, string id)
        {
            var a = GameData.Achievements.FirstOrDefault(x => x.id == id);
            if (a == null || !CanClaim(p, a)) return 0;

            var tier = ClaimedTiers(p, a.id);
            var gems = tier < (a.gems?.Length ?? 0) ? a.gems[tier] : 0;
            p.SetAchievementTier(a.id, tier + 1);
            p.gems += gems;
            return gems;
        }

        // ------------------------------------------------------------------ 마일스톤

        public static bool IsClaimed(PlayerState p, string id) => p.milestonesClaimed.Contains(id);

        /// <summary>What a milestone of this kind compares its target against.</summary>
        public static int Value(PlayerState p, MilestoneDef m)
        {
            switch (m.kind)
            {
                case "stage": return p.maxCleared;
                case "level": return p.Find("main")?.level ?? 1;
                case "party": return p.PartyMembers().Sum(o => o.level);
                default: return 0;
            }
        }

        public static bool Reached(PlayerState p, MilestoneDef m) => Value(p, m) >= m.target;

        /// <summary>Reached but not yet granted.</summary>
        public static List<MilestoneDef> Pending(PlayerState p) =>
            GameData.Milestones.Where(m => !IsClaimed(p, m.id) && Reached(p, m)).ToList();

        /// <summary>The next unreached mark of each kind, which is what the list is for: it says
        /// what is coming, not only what has been.</summary>
        public static List<MilestoneDef> Upcoming(PlayerState p)
        {
            var outList = new List<MilestoneDef>();
            foreach (var kind in new[] { "stage", "level", "party" })
            {
                var next = GameData.Milestones.FirstOrDefault(
                    m => m.kind == kind && !IsClaimed(p, m.id) && !Reached(p, m));
                if (next != null) outList.Add(next);
            }
            return outList;
        }

        public static int ClaimedCount(PlayerState p) =>
            GameData.Milestones.Count(m => IsClaimed(p, m.id));

        /// <summary>Grants every pending milestone at once. Returns what was paid.</summary>
        public static (int Count, int Gems, int Cards) GrantPending(PlayerState p)
        {
            int count = 0, gems = 0, cards = 0;
            foreach (var m in Pending(p))
            {
                p.milestonesClaimed.Add(m.id);
                p.gems += m.gems;
                p.cards += m.cards;
                count++;
                gems += m.gems;
                cards += m.cards;
            }
            return (count, gems, cards);
        }

        /// <summary>
        /// Everything waiting to be collected, for the badge on the tab. Milestones are counted
        /// even though they can be taken in one press: the number is "things to collect", and a
        /// player who sees 0 does not open the screen.
        /// </summary>
        public static int ReadyCount(PlayerState p) => ClaimableCount(p) + Pending(p).Count;

        /// <summary>Formats an achievement's value the way its unit asks — hours for time.</summary>
        public static string Format(long value, string unit) =>
            unit == "time" ? $"{value / 3600f:0.#}시간" : value.ToString("N0");
    }
}
