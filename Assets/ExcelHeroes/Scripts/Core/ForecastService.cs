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

            var ratio = dps / Mathf.Max(1f, enemyHp) / (enemyDps / hp);

            var b = GameData.Balance;
            var lo = Mathf.Max(0.01f, b.forecastBossLo);
            var hi = Mathf.Max(lo + 0.01f, b.forecastBossHi);
            var prob = Mathf.Clamp01(Mathf.Log(Mathf.Max(1e-9f, ratio) / lo) / Mathf.Log(hi / lo));

            var eta = (MinionWaves + 1) * Approach
                      + (minionHp * MinionWaves + bossHp) / dps;
            eta *= b.forecastEtaBoss;

            var label = prob >= 0.7f ? "유리" : prob >= b.safeAdvanceMin ? "접전" : "불리";
            return new Result(stage, ratio, prob, eta, label);
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
