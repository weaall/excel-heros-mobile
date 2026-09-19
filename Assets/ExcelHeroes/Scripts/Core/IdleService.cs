using System;
using System.Linq;
using ExcelHeroes.Data;
using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 백그라운드 정산 — what the party earned while the game was shut.
    ///
    /// Ported from the web build's `idleReport`/`applyOffline` and `offlineGold`. The numbers are
    /// its numbers on purpose: ten hours capped, at 60% of the rate the party earns while you are
    /// watching, with a one-minute floor so closing the app to answer the door does not pop a
    /// dialog. The efficiency is deliberately under 1 — the whole point is that leaving the game
    /// running is never the worse play, which is the same rule the ad rewards follow.
    /// </summary>
    public static class IdleService
    {
        /// <summary>Estimated gold per second the party earns farming `stage`.</summary>
        public static float GoldPerSec(PlayerState p, int stage)
        {
            var dps = Mathf.Max(1f, PartyDps(p));
            // Kill time is health over damage, plus the respawn wait and about a second of walking
            // in. Capped so an absurd DPS cannot claim more kills per second than there are
            // monsters on the field.
            var killTime = StatMath.MonsterHp(stage) / dps + RespawnDelay + 1f;
            var killsPerSec = Mathf.Min(MaxMonsters, 1f / killTime);
            return StatMath.StageGold(stage) * GoldMultiplier(p) * killsPerSec;
        }

        const float RespawnDelay = 1f;
        const float MaxMonsters = 5f;

        static float PartyDps(PlayerState p)
        {
            var syn = StatMath.Synergy(p);
            var atk = p.PartyMembers().Sum(o => (float)StatMath.Atk(o));
            return atk * (1f + syn.atkBonus);
        }

        /// <summary>The office upgrades' gold bonus, which an idle hour earns as well.</summary>
        static float GoldMultiplier(PlayerState p) =>
            1f + TeamUpgrades.Gold(p) + PrestigeService.Bonus(p);

        public readonly struct Report
        {
            public readonly long Elapsed;   // seconds actually away
            public readonly long Seconds;   // seconds paid for, after the cap
            public readonly bool Capped;
            public readonly long Gold;

            public Report(long elapsed, long seconds, bool capped, long gold)
            { Elapsed = elapsed; Seconds = seconds; Capped = capped; Gold = gold; }

            public bool Worth => Gold > 0 && Seconds >= MinSeconds;
        }

        public const long CapSeconds = 10 * 3600;
        public const float Efficiency = 0.6f;
        public const long MinSeconds = 60;

        /// <summary>What was earned over a gap, without granting it.</summary>
        public static Report For(PlayerState p, long elapsed)
        {
            if (p == null || elapsed <= 0) return new Report(0, 0, false, 0);
            var seconds = Math.Min(elapsed, CapSeconds);
            var gold = (long)Mathf.Floor(GoldPerSec(p, p.stage) * seconds * Efficiency);
            return new Report(elapsed, seconds, elapsed > CapSeconds, gold);
        }

        /// <summary>The gap since the save was last written, from the clock.</summary>
        public static Report SinceLastSeen(PlayerState p)
        {
            if (p == null || p.lastSeenUnix <= 0) return new Report(0, 0, false, 0);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return For(p, Math.Max(0, now - p.lastSeenUnix));
        }

        /// <summary>Pays a report out. Returns false when there was nothing worth paying.</summary>
        public static bool Grant(PlayerState p, Report r)
        {
            if (p == null || !r.Worth) return false;
            // gold is an int and ten hours at a late-stage rate overflows it, so the payout is
            // clamped to whatever room is left rather than wrapping the player's balance negative.
            var room = int.MaxValue - p.gold;
            var paid = (int)Math.Min(r.Gold, Math.Max(0, room));
            p.gold += paid;
            p.totalGold += paid;
            return paid > 0;
        }

        /// <summary>"8시간 12분" — the gap, in the units a player thinks in.</summary>
        public static string Duration(long seconds)
        {
            var h = seconds / 3600;
            var m = seconds % 3600 / 60;
            if (h > 0) return m > 0 ? $"{h}시간 {m}분" : $"{h}시간";
            return $"{Math.Max(1, m)}분";
        }
    }
}
