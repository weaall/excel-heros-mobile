using System;
using ExcelHeroes.Data;
using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 야근 모드 — one sixty-second survival run a day, three stages past anything cleared, scored
    /// on how many bodies fall. Ported from the web build's startOvertime / endOvertime.
    ///
    /// It is the only fight in the game the player chooses to start, which is what makes it worth
    /// having in an idle build: everything else happens whether or not anyone is watching.
    /// </summary>
    public static class OvertimeService
    {
        /// <summary>A run in progress. Null the rest of the time.</summary>
        public class Run
        {
            public float Left;
            public int Kills;
            public int Elites;
            public int Stage;
        }

        public static Run Active { get; private set; }

        public static bool CanStart(PlayerState p) =>
            Active == null && p != null && (!p.overtimeDone || p.overtimeExtra > 0);

        /// <summary>Why the button is off, for the label.</summary>
        public static string Blocked(PlayerState p)
        {
            if (Active != null) return "진행 중";
            if (p != null && p.overtimeDone && p.overtimeExtra <= 0) return "오늘 완료";
            return "";
        }

        public static int StageFor(PlayerState p) =>
            Math.Max(1, p.maxCleared + GameData.Balance.overtimeStageOffset);

        public static Run Start(PlayerState p)
        {
            if (!CanStart(p)) return null;
            var b = GameData.Balance;

            // An extra run bought with an ad is spent first; the free one is already gone if the
            // flag is set.
            if (p.overtimeDone) p.overtimeExtra = Math.Max(0, p.overtimeExtra - 1);

            Active = new Run { Left = b.overtimeDuration, Kills = 0, Elites = 0, Stage = StageFor(p) };
            return Active;
        }

        public static void Note(bool elite)
        {
            if (Active == null) return;
            Active.Kills++;
            if (elite) Active.Elites++;
        }

        /// <summary>Counts the clock down. Returns true on the tick the run ends.</summary>
        public static bool Tick(float dt)
        {
            if (Active == null) return false;
            Active.Left -= dt;
            return Active.Left <= 0f;
        }

        public readonly struct Report
        {
            public readonly int Kills, Elites, Gems, Cards, Best;
            public Report(int kills, int elites, int gems, int cards, int best)
            { Kills = kills; Elites = elites; Gems = gems; Cards = cards; Best = best; }
        }

        /// <summary>Ends the run and pays it. The per-kill value rises with phase: deeper stages
        /// take longer per body, and without that a once-a-day reward would shrink as the run
        /// progressed — the same rule the gem drop follows.</summary>
        public static Report? End(PlayerState p)
        {
            var run = Active;
            if (run == null) return null;
            var b = GameData.Balance;

            var phase = Math.Max(0, (Math.Max(1, run.Stage) - 1) / Math.Max(1, b.bossEvery));
            var perPhase = 1f + phase * b.overtimeGemsPerPhase;
            var gems = Mathf.Min(b.overtimeMaxGems, Mathf.RoundToInt(
                (run.Kills * b.overtimeGemsPerKill + run.Elites * b.overtimeGemsPerElite) * perPhase));
            var cards = (Math.Max(0, run.Stage - 1) / Math.Max(1, b.bossEvery) + 1) * b.overtimeCardsPerPhase;

            p.gems += gems;
            p.cards += cards;
            p.overtimeDone = true;
            p.overtimes++;
            p.overtimeBest = Math.Max(p.overtimeBest, run.Kills);

            Active = null;
            return new Report(run.Kills, run.Elites, gems, cards, p.overtimeBest);
        }

        /// <summary>Drops a run without paying it — used when the screen goes away mid-run.</summary>
        public static void Abandon() => Active = null;
    }
}
