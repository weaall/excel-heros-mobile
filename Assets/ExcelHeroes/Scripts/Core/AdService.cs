using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;
using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 광고 보상 — the rewarded-ad menu, ported from the web build's `adOffers`/`adReward`.
    ///
    /// Every reward here is FLAT. Not one of them multiplies what the run earned while the game
    /// was closed, and that is a rule rather than a detail: a reward that scales with time spent
    /// away makes shutting the app the better play, and an idle game that pays you for not
    /// playing it has stopped being a game. The same reasoning caps 백그라운드 정산 below 1x.
    ///
    /// There is no ad network wired up. The offers, the daily caps and the payouts are all real;
    /// what a press does today is wait out a placeholder and pay. Dropping a real SDK in later
    /// only has to call Grant once the ad reports finished.
    /// </summary>
    public static class AdService
    {
        public readonly struct Offer
        {
            public readonly AdOfferDef Def;
            public readonly string Value;    // what this press pays, in words
            public readonly int Left;        // presses left today for this offer
            public readonly bool Can;
            public readonly string Reason;   // why not, when Can is false

            public Offer(AdOfferDef def, string value, int left, bool can, string reason)
            { Def = def; Value = value; Left = left; Can = can; Reason = reason; }
        }

        /// <summary>Presses left across every offer. The per-offer cap is on top of this one.</summary>
        public static int LeftToday(PlayerState p) =>
            Mathf.Max(0, GameData.Balance.adPerDay - (p?.AdsUsedTotal() ?? 0));

        public static int LeftFor(PlayerState p, AdOfferDef o) =>
            o == null ? 0 : Mathf.Min(LeftToday(p), Mathf.Max(0, o.perDay - p.AdsUsed(o.id)));

        public static List<Offer> Offers(PlayerState p)
        {
            var list = new List<Offer>();
            foreach (var o in GameData.Balance.adOffers)
            {
                var left = LeftFor(p, o);
                var can = left > 0;
                var reason = can ? "" : "오늘 소진";
                string value;

                switch (o.id)
                {
                    case "gold":
                        value = $"+₩{GoldFor(p, o):N0}";
                        break;
                    case "gems":
                        value = $"보석 +{o.amount}";
                        break;
                    case "cards":
                        value = $"강화 카드 +{o.amount}";
                        break;
                    case "dispatch":
                        var di = DispatchService.Read(p);
                        if (di.Active && !di.Done)
                        {
                            value = $"남은 {DispatchService.Remaining(di.Remaining)} → 즉시 복귀";
                        }
                        else
                        {
                            value = "출장 중일 때만";
                            can = false;
                            reason = "진행 중인 출장이 없음";
                        }
                        break;
                    default:
                        // 야근 is not ported yet, so its offer is listed and disabled rather than
                        // hidden: a menu that changes shape as features land teaches a player the
                        // wrong shape twice.
                        value = "준비 중";
                        can = false;
                        reason = "아직 열리지 않은 기능";
                        break;
                }

                list.Add(new Offer(o, value, left, can, reason));
            }
            return list;
        }

        /// <summary>The gold offer pays a flat number of hours at the CURRENT rate — it is worked
        /// out when pressed, not accrued, so it never competes with 백그라운드 정산.</summary>
        public static long GoldFor(PlayerState p, AdOfferDef o) =>
            (long)Mathf.Floor(IdleService.GoldPerSec(p, p.stage) * o.hours * 3600f);

        /// <summary>Pays an offer out, after the ad. Returns what the player should be told.</summary>
        public static string Grant(PlayerState p, string id)
        {
            var o = GameData.Balance.adOffers.FirstOrDefault(x => x.id == id);
            if (o == null || LeftFor(p, o) <= 0) return null;

            string told;
            switch (o.id)
            {
                case "gold":
                    var gold = GoldFor(p, o);
                    var room = int.MaxValue - p.gold;
                    var paid = (int)Mathf.Min(gold, Mathf.Max(0, room));
                    p.gold += paid;
                    p.totalGold += paid;
                    told = $"광고 보상 · 골드 +{paid:N0}";
                    break;
                case "gems":
                    p.gems += o.amount;
                    told = $"광고 보상 · 보석 +{o.amount}";
                    break;
                case "cards":
                    p.cards += o.amount;
                    told = $"광고 보상 · 강화 카드 +{o.amount}";
                    break;
                case "dispatch":
                    if (!DispatchService.FinishNow(p)) return null;
                    told = "출장 복귀 완료 — 검토에서 보상을 받으세요";
                    break;
                default:
                    return null;
            }

            p.NoteAd(o.id);
            return told;
        }
    }
}
