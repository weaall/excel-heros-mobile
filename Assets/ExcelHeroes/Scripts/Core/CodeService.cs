using System;
using System.Globalization;
using System.Linq;
using System.Text;
using ExcelHeroes.Data;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 보석 코드 — promo codes, ported from the web build's `codes.js`.
    ///
    /// The table ships with the game, and that is the design rather than an oversight: the web
    /// build's own note says to treat a code as a coupon you chose to publish, never as a secret.
    ///
    /// Redemption is recorded locally here. The web records it per account on its Worker so that
    /// clearing storage cannot pay twice; this build has no account, so it behaves the way the web
    /// does when signed out — the local record is all there is. Wiring an account in later only
    /// has to check the same code id against the server before granting.
    /// </summary>
    public static class CodeService
    {
        /// <summary>
        /// Codes are typed by hand, so case and spacing are forgiven — but nothing else is.
        /// Punctuation stays: "REF!" is a real code and stripping the ! would break it.
        /// </summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw.ToUpperInvariant())
            {
                if (char.IsWhiteSpace(c) || c == '-') continue;
                sb.Append(c);
                if (sb.Length >= 32) break;
            }
            return sb.ToString();
        }

        public readonly struct Result
        {
            public readonly bool Ok;
            public readonly string Message;
            public readonly int Gems, Cards, Gold;

            public Result(bool ok, string message, int gems = 0, int cards = 0, int gold = 0)
            { Ok = ok; Message = message; Gems = gems; Cards = cards; Gold = gold; }
        }

        /// <summary>Checks a code without granting it.</summary>
        public static Result Check(PlayerState p, string raw)
        {
            var code = Normalize(raw);
            if (code.Length == 0) return new Result(false, "코드를 입력하세요");

            var def = GameData.Codes.FirstOrDefault(c => c.id == code);
            if (def == null) return new Result(false, "없는 코드입니다");
            if (p.redeemedCodes.Contains(code)) return new Result(false, "이미 사용한 코드입니다");

            if (!string.IsNullOrEmpty(def.until)
                && DateTime.TryParse(def.until, CultureInfo.InvariantCulture,
                                     DateTimeStyles.None, out var until)
                && DateTime.Now.Date > until.Date)
                return new Result(false, "기간이 지난 코드입니다");

            return new Result(true, def.label, def.gems, def.cards, def.gold);
        }

        /// <summary>Checks and grants. Returns what to tell the player either way.</summary>
        public static Result Redeem(PlayerState p, string raw)
        {
            var r = Check(p, raw);
            if (!r.Ok) return r;

            p.redeemedCodes.Add(Normalize(raw));
            p.gems += r.Gems;
            p.cards += r.Cards;
            p.gold += r.Gold;
            return r;
        }

        /// <summary>"보석 +3000 · 강화 카드 +5" — what a result paid, for the toast.</summary>
        public static string Paid(Result r)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (r.Gems > 0) parts.Add($"보석 +{r.Gems:N0}");
            if (r.Cards > 0) parts.Add($"강화 카드 +{r.Cards:N0}");
            if (r.Gold > 0) parts.Add($"골드 +{r.Gold:N0}");
            return parts.Count > 0 ? string.Join(" · ", parts) : "보상 없음";
        }
    }
}
