using ExcelHeroes.Data;
using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 업무 상성 — the game's answer to Blue Archive's attack / armour types, the one rule that makes
    /// who goes out a decision rather than "the highest power five".
    ///
    ///   공격 유형 (hero)      수식 · 매크로 · 검토
    ///   방어 유형 (error)     서식 오류 · 참조 오류 · 논리 오류
    ///
    /// Each attack is 효과적 against the error of its own colour (×1.5), 저항 against the next one
    /// round (×0.7), plain against the third. A phase's errors all share one armour, shown before the
    /// fight, so the party screen can say which attack to bring — and it changes Phase to Phase.
    /// </summary>
    public static class Affinity
    {
        public const int Formula = 0, Macro = 1, Review = 2;

        public const float Effective = 1.5f, Resisted = 0.7f;

        static readonly string[] AtkNames = { "수식", "매크로", "검토" };
        static readonly string[] AtkEn = { "FORMULA", "MACRO", "REVIEW" };
        static readonly string[] ArmorNames = { "서식 오류", "참조 오류", "논리 오류" };
        static readonly string[] ArmorShort = { "서식", "참조", "논리" };
        static readonly Color[] Colors =
        {
            new Color(0.91f, 0.27f, 0.33f),   // red
            new Color(0.95f, 0.64f, 0.05f),   // amber
            new Color(0.18f, 0.50f, 0.95f),   // blue
        };

        public static string AtkName(int t) => AtkNames[Mathf.Clamp(t, 0, 2)];
        public static string AtkEnName(int t) => AtkEn[Mathf.Clamp(t, 0, 2)];
        public static string ArmorName(int t) => ArmorNames[Mathf.Clamp(t, 0, 2)];
        public static string ArmorShortName(int t) => ArmorShort[Mathf.Clamp(t, 0, 2)];
        public static Color ColorOf(int t) => Colors[Mathf.Clamp(t, 0, 2)];
        public static string Hex(int t) => "#" + ColorUtility.ToHtmlStringRGB(ColorOf(t));

        /// <summary>
        /// A hero's attack from what they do: the numbers people (재무·영업) write formulas, the
        /// builders and the hands-on crew automate, the rest — admin, people, the executive office —
        /// read and review. The player's own 김 follows their track (영업·재무 → 수식, else 검토).
        /// </summary>
        public static int AtkOf(HeroDef def)
        {
            if (def == null) return Review;
            switch (def.id)
            {
                case "ceo": case "cso": return Formula;
                case "founder": case "chairman": case "logistics_bae": case "courier": case "parttime":
                case "security_yang": case "guard": return Macro;
            }
            if (GameData.MainJob(def.id) != null || def.id == GameData.MainId)
                return def.id.StartsWith("sales") || def.id.StartsWith("finance") ? Formula : Review;
            return def.division switch
            {
                "finance" or "market" => Formula,
                "tech" => Macro,
                _ => Review,
            };
        }

        public static int AtkOf(string heroId) => AtkOf(GameData.Hero(heroId));

        // one armour per Phase (stage), in an order that does not simply rotate (so no single type is always
        // next); Phase 1 is 논리 — weak to 검토, which is what a new player's first hires mostly are
        static readonly int[] PhaseArmor = { Review, Formula, Macro, Macro, Review, Formula, Formula, Macro, Review };

        public static int ArmorOfStage(int stage) => PhaseArmor[(Mathf.Max(1, stage) - 1) % PhaseArmor.Length];

        public static float Mult(int atk, int armor) =>
            atk == armor ? Effective : (atk + 1) % 3 == armor ? Resisted : 1f;

        /// <summary>+1 효과적, -1 저항, 0 보통.</summary>
        public static int Verdict(int atk, int armor) => atk == armor ? 1 : (atk + 1) % 3 == armor ? -1 : 0;

        /// <summary>The attack that is effective against an armour (same colour).</summary>
        public static int CounterOf(int armor) => armor;
    }
}
