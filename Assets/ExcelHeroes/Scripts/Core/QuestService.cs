using System;
using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 일일 업무 — six of nine tasks each day, a check-in stamp, and a streak.
    ///
    /// This is not decoration: it is the gem economy. A battle win pays 5 gems and a ten-pull costs
    /// 900, so before this existed a player needed ~180 battles per ten-pull. With the daily list the
    /// ceiling is about 500 a day, which puts a ten-pull roughly two days apart — a cadence that
    /// gives someone a reason to open the app tomorrow instead of grinding today.
    ///
    /// Which six run is seeded off the date, so everyone shares a list and it cannot be rerolled.
    /// </summary>
    public static class QuestService
    {
        /// <summary>Local date, because "today" for a daily reset means the player's midnight.</summary>
        public static string TodayKey() => DateTime.Now.ToString("yyyy-MM-dd");

        /// <summary>
        /// Today's six. 오류 처리 is always on the list (it is the one you finish by playing at all);
        /// the rest are a date-seeded shuffle. Ported from the web build so both clients agree.
        /// </summary>
        public static List<string> TodayQuestIds(string dateKey)
        {
            var file = GameData.Quests;
            if (file == null) return new List<string>();

            uint h = 2166136261;
            foreach (var ch in dateKey) { h ^= ch; h *= 16777619; }

            var rest = file.items.Where(q => q.id != "kills").Select(q => q.id).ToList();
            for (var i = rest.Count - 1; i > 0; i--)
            {
                h = unchecked(h * 1103515245 + 12345);
                var j = (int)(h % (uint)(i + 1));
                (rest[i], rest[j]) = (rest[j], rest[i]);
            }

            var take = Math.Max(0, Math.Min(rest.Count, file.perDay - 1));
            var ids = new List<string> { "kills" };
            ids.AddRange(rest.Take(take));
            return ids;
        }

        /// <summary>Rolls the list over at local midnight. Safe to call on every screen open.</summary>
        public static void EnsureToday(PlayerState p)
        {
            var today = TodayKey();
            if (p.dailyDate == today) return;

            p.dailyDate = today;
            p.quests.Clear();
            foreach (var id in TodayQuestIds(today)) p.quests.Add(new QuestProgress { id = id });
            p.allClearClaimed = false;
        }

        public static bool CanCheckIn(PlayerState p) => p.checkInDate != TodayKey();

        /// <summary>
        /// 출근 도장. The streak only survives if yesterday was stamped — miss a day and it resets,
        /// which is what makes the streak worth protecting.
        /// </summary>
        public static (int gems, int gold) CheckIn(PlayerState p)
        {
            if (!CanCheckIn(p)) return (0, 0);
            var file = GameData.Quests;
            var today = DateTime.Now.Date;

            var continued = !string.IsNullOrEmpty(p.checkInDate)
                            && DateTime.TryParse(p.checkInDate, out var last)
                            && (today - last.Date).Days == 1;
            p.streak = continued ? p.streak + 1 : 1;
            p.checkInDate = TodayKey();

            var bonusDays = Math.Min(Math.Max(0, p.streak - 1), file.streakMaxDays);
            var gems = file.loginGems + bonusDays * file.streakGemsPerDay;
            var gold = file.loginGoldKills * StatMath.StageGold(Math.Max(1, p.stage));

            p.gems += gems;
            p.gold += gold;
            return (gems, gold);
        }

        /// <summary>Records progress against whichever of today's quests tracks that kind.</summary>
        public static void Note(PlayerState p, string kind, int amount = 1)
        {
            if (amount <= 0) return;
            EnsureToday(p);
            var q = p.quests.FirstOrDefault(x => x.id == kind);
            if (q == null || q.claimed) return;

            var def = GameData.Quest(kind);
            q.count = Math.Min(q.count + amount, def?.target ?? q.count + amount);
        }

        public static bool IsDone(PlayerState p, string id)
        {
            var q = p.quests.FirstOrDefault(x => x.id == id);
            var def = GameData.Quest(id);
            return q != null && def != null && q.count >= def.target;
        }

        public static bool CanClaim(PlayerState p, string id)
        {
            var q = p.quests.FirstOrDefault(x => x.id == id);
            return q != null && !q.claimed && IsDone(p, id);
        }

        public static (int gems, int gold) Claim(PlayerState p, string id)
        {
            if (!CanClaim(p, id)) return (0, 0);
            var q = p.quests.First(x => x.id == id);
            var def = GameData.Quest(id);
            q.claimed = true;

            var gold = def.goldKills * StatMath.StageGold(Math.Max(1, p.stage));
            p.gems += def.gems;
            p.gold += gold;
            return (def.gems, gold);
        }

        public static bool AllClaimed(PlayerState p) =>
            p.quests.Count > 0 && p.quests.All(q => q.claimed);

        public static bool CanClaimAllClear(PlayerState p) => !p.allClearClaimed && AllClaimed(p);

        public static int ClaimAllClear(PlayerState p)
        {
            if (!CanClaimAllClear(p)) return 0;
            p.allClearClaimed = true;
            var gems = GameData.Quests.allClearGems;
            p.gems += gems;
            return gems;
        }

        /// <summary>How many of today's tasks are finished — drives the badge on the nav bar.</summary>
        public static int ReadyCount(PlayerState p) =>
            p.quests.Count(q => !q.claimed && IsDone(p, q.id)) + (CanClaimAllClear(p) ? 1 : 0)
            + (CanCheckIn(p) ? 1 : 0);
    }
}
