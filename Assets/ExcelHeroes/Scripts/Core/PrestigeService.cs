using System;
using ExcelHeroes.Data;
using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 회사 이전 — the reset that makes the next run faster, ported from the web build's
    /// `prestigeShares` / `prestigeInfo` / `prestige`.
    ///
    /// Everything about progress goes back to the start: stage, levels, gold, office upgrades.
    /// What survives is the roster, everything spent on the cards themselves (★, 각성, 비품,
    /// 호감도) and 지분 — permanent shares that add to attack and to gold for good.
    ///
    /// Drawing that line is the whole design. A prestige that took the cards would make the gacha
    /// worthless, and one that kept the stage would not be a reset; what it resets is the part of
    /// the run that is only time, and what it keeps is the part that was a decision.
    /// </summary>
    public static class PrestigeService
    {
        /// <summary>Shares a reset would pay at this depth: 30 → 5, 50 → 11, 100 → 31.</summary>
        public static int Shares(int maxCleared) =>
            maxCleared < GameData.Balance.prestigeMinCleared
                ? 0
                : (int)Mathf.Floor(Mathf.Pow(maxCleared / 10f, 1.5f));

        /// <summary>The permanent bonus the shares already earned are paying, as a fraction.</summary>
        public static float Bonus(PlayerState p) =>
            (p?.prestigeShares ?? 0) * GameData.Balance.prestigeBonusPerShare;

        public static int Gain(PlayerState p) => Shares(p?.maxCleared ?? 0);

        public static bool Eligible(PlayerState p) => Gain(p) > 0;

        /// <summary>
        /// Does it. Returns the shares gained, or 0 when the run is not deep enough yet.
        ///
        /// The roster is deliberately untouched — including ★, 각성, 비품 and 호감도, which are
        /// what the player actually spent their pulls and their attention on. Levels go, because
        /// levels are bought with the gold that also goes.
        /// </summary>
        public static int Reset(PlayerState p)
        {
            var gain = Gain(p);
            if (gain <= 0) return 0;

            p.prestigeShares += gain;
            p.prestigeCount++;

            p.stage = 1;
            p.maxCleared = 0;
            p.gold = 0;
            foreach (var o in p.owned) o.level = 1;
            p.teamIds.Clear();
            p.teamLevels.Clear();

            return gain;
        }
    }
}
