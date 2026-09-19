using System;
using System.Linq;
using ExcelHeroes.Data;
using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 승산 — how the party stacks up against a stage, ported from the web build's
    /// `challengeForecast`.
    ///
    /// The web's own note on this is the important part, and it is kept: the win probability is
    /// "유리" about 97% of the time, so it carries almost no information. What grows continuously
    /// is the TIME a stage takes, and that growth IS the wall — it is the signal that says when to
    /// stop pushing and go and 회사 이전 instead. So the ETA is the headline here and the
    /// probability is the footnote, which is the opposite of how a forecast usually gets built.
    ///
    /// The web also has a 도전/파밍 toggle whose meaning depends on a boss appearing only on boss
    /// stages. This port's fight is three waves ending in a boss at every stage, so that toggle
    /// would not mean the same thing and is deliberately not carried over.
    /// </summary>
    public static class ForecastService
    {
        public readonly struct Result
        {
            public readonly int Stage;
            public readonly float Ratio;
            public readonly float Prob;
            public readonly float Eta;     // seconds to clear, including walking in
            public readonly string Label;

            public Result(int stage, float ratio, float prob, float eta, string label)
            { Stage = stage; Ratio = ratio; Prob = prob; Eta = eta; Label = label; }
        }

        /// <summary>Roughly how long a wave spends walking in from off-screen before it can be hit.</summary>
        const float Approach = 2.2f;

        /// <summary>This build's fight: two waves of bodies, then the boss.</summary>
        const int MinionWaves = 2;
        const int MinionsPerWave = 3;
        const float BossHpMultiplier = 4f;

        public static Result For(PlayerState p, int stage)
        {
            var syn = StatMath.Synergy(p);
            var dps = Mathf.Max(1f, p.PartyMembers().Sum(o => (float)StatMath.Atk(o)) * (1f + syn.atkBonus));
            var hp = Mathf.Max(1f, p.PartyMembers().Sum(o => (float)StatMath.Hp(o)) * (1f + syn.hpBonus));

            var boss = GameData.BossForStage(stage);
            var minionHp = StatMath.MonsterHp(stage) * (float)MinionsPerWave;
            var bossHp = StatMath.MonsterHp(stage) * BossHpMultiplier * (boss?.hp ?? 1f);
            var enemyHp = minionHp * MinionWaves + bossHp;

            // The bodies hit about once a second each; the boss hits harder and slower.
            var enemyDps = StatMath.MonsterAtk(stage) * MinionsPerWave / 1.1f;

            // The ratio is against ONE encounter — the boss, as the hardest of the three — not
            // against the whole stage. Summing every wave into it made a party that clears the
            // stage on every run read 불리, because it was being asked to kill three waves at once.
            // Total HP still drives the ETA below, which is what the total is actually for.
            var ratio = dps / Mathf.Max(1f, bossHp) / (enemyDps / hp);

            var b = GameData.Balance;

            var eta = (MinionWaves + 1) * Approach
                      + (minionHp * MinionWaves + bossHp) / dps;
            eta *= b.forecastEtaBoss;

            // No 유리/접전/불리 label.
            //
            // The web's thresholds ([3,15]) were measured against ITS encounter — one wave, or one
            // boss on a boss stage. This build's fight is two waves and a boss at every stage, so
            // the same ratio means something different here and those constants do not transfer.
            // Shipping a label calibrated for another game read 불리 on a stage this party clears
            // every single run, which is worse than saying nothing.
            //
            // Calibrating it honestly means measuring this sim, which is a bench run and a
            // separate piece of work. Until then the ETA carries the information — and by the
            // web's own note that was always the part that did.
            return new Result(stage, ratio, 0f, eta, "");
        }

        /// <summary>"1분 20초" — the number a player actually reads off this.</summary>
        public static string Eta(float seconds)
        {
            if (seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return "—";
            var s = Mathf.RoundToInt(seconds);
            if (s < 60) return $"{s}초";
            var m = s / 60;
            var rest = s % 60;
            if (m < 60) return rest > 0 ? $"{m}분 {rest}초" : $"{m}분";
            return $"{m / 60}시간 {m % 60}분";
        }
    }
}
