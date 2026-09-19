using System.Linq;
using ExcelHeroes.Data;
using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 자동 진행 · 안전 진행 · 자동 강화 — ported from the web build's `setAutoAdvance`,
    /// `setSafeAdvance`, `setAutoUpgrade` and `upgradeCheapestLoop`.
    ///
    /// Until now this port advanced the Phase on every win, with no way to say otherwise. That is
    /// one behaviour where the web has three, and the two it was missing are the ones a player
    /// reaches for when they stop winning:
    ///
    /// **파밍** (자동 진행 off) keeps the party on the Phase it has already beaten. Gold, 비품
    /// drops and quest progress all come from clears, so standing still is a real strategy and not
    /// a pause — without it the only answer to a wall is to watch the party lose the same fight.
    ///
    /// **안전 진행** is the same thing decided automatically: push while the next Phase is still
    /// quick, stop when it is not. It exists because the wall does not announce itself — a party
    /// that clears Phase 40 comfortably can be four Phases from a three-minute run.
    ///
    /// IT GATES ON THE ETA, NOT ON 승산, and that is a deliberate divergence. The web reads
    /// `승산 >= SAFE_ADVANCE.min`; this port's ForecastService returns no probability at all,
    /// because the web's thresholds were measured against its own encounter and this build fights
    /// two waves and a boss at every Phase. Prob is a hard 0 here, so the web's gate would hold
    /// the party on its current Phase forever — which is exactly what the self-test caught. The
    /// ETA is the number this port computes honestly, and by the web's own note it was always the
    /// one carrying the signal: "what grows continuously is the TIME a stage takes, and that
    /// growth IS the wall".
    ///
    /// **자동 강화** is the other half. Gold that arrives during a fight is gold the player has to
    /// be on the right screen to spend, and this spends it on whoever is cheapest to level, which
    /// is the same thing 일괄 강화 does by hand.
    /// </summary>
    public static class AutoPlayService
    {
        // ------------------------------------------------------------------ 자동 진행

        /// <summary>Why the party is staying on this Phase — empty when it is moving on.</summary>
        public static string HeldBack(PlayerState p, int clearedStage)
        {
            if (p == null) return "";
            if (!p.autoAdvance) return "자동 진행 꺼짐 · 현재 구간에서 계속 처리합니다";
            if (!p.safeAdvance) return "";

            var next = clearedStage + 1;
            var max = GameData.Balance.safeAdvanceEtaMax;
            if (max <= 0f) return "";

            var eta = ForecastService.For(p, next).Eta;

            // A gate fails CLOSED. An ETA that is not a finite positive number means the forecast
            // could not answer, and "I do not know how long this will take" is not a reason to
            // push into it — it is the strongest reason not to. Read the other way round, one bad
            // number would wave the party through every Phase between here and the end of the
            // save, which is the failure a player would notice last and forgive least.
            if (!float.IsFinite(eta) || eta <= 0f)
                return $"Phase {next} 예상 시간을 계산할 수 없습니다 · 안전 진행이 막았습니다";

            if (eta <= max) return "";

            return $"Phase {next} 예상 {ForecastService.Eta(eta)} · 안전 진행이 막았습니다 "
                 + $"(기준 {ForecastService.Eta(max)})";
        }

        /// <summary>Whether the win just scored should move the party to the next Phase.</summary>
        public static bool ShouldAdvance(PlayerState p, int clearedStage) =>
            HeldBack(p, clearedStage).Length == 0;

        // ------------------------------------------------------------------ 자동 강화

        static float _timer;

        /// <summary>
        /// Called every frame while a run is on. Spends on the cheapest party level it can afford,
        /// once every `autoUpgradeInterval` seconds.
        ///
        /// The timer is reset whenever the setting is off rather than left to run, so switching it
        /// on does not immediately spend a second's worth of gold the player has not agreed to yet.
        /// </summary>
        public static int Tick(PlayerState p, float dt)
        {
            if (p == null || !p.autoUpgrade) { _timer = 0f; return 0; }

            var every = Mathf.Max(0.1f, GameData.Balance.autoUpgradeInterval);
            _timer += dt;
            if (_timer < every) return 0;
            _timer = 0f;

            return UpgradeCheapest(p);
        }

        /// <summary>
        /// Buys levels for whichever party member is cheapest to level, until the gold runs out.
        ///
        /// Cheapest first rather than weakest first, because the cost curve is per level: the card
        /// furthest behind is also the one whose next level costs least, so the two orderings agree
        /// and the cheap one needs no notion of "behind". A card at its ★ cap drops out of the
        /// running entirely — gold stops there and the rest is 조각's job.
        /// </summary>
        public static int UpgradeCheapest(PlayerState p, int max = 50)
        {
            var n = 0;
            while (n < max)
            {
                var cheapest = p.party
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Select(p.Find)
                    .Where(o => o != null && !StatMath.AtLevelCap(o))
                    .OrderBy(StatMath.LevelUpCost)
                    .FirstOrDefault();

                if (cheapest == null) break;
                if (!StatMath.TryLevelUp(p, cheapest)) break;
                n++;
            }

            if (n > 0) QuestService.Note(p, "upgrades", n);
            return n;
        }
    }
}
