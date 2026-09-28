using System;
using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;
using UnityEngine;

namespace ExcelHeroes.Core
{
    [Serializable]
    public class MailItem
    {
        public string id, title, from, body;
        public int gems, gold, sparkPoints;
        public string itemSlot = "", itemGrade = "";   // an equipment piece, when set
        public bool claimed;
        public string day;                             // the day it arrived (QuestService.TodayKey)
    }

    /// <summary>
    /// 상점: four shelves of goods (the Gemini shop mock-up, tools/out/design/mock_shop_0) — 일반 (gold),
    /// 보석 (gems), 교환소 (recruit points) and 무료 보급 (free daily) — each good with a daily limit that
    /// resets at midnight (QuestService.TodayKey). Office equipment comes as boxes of one slot at a
    /// set grade (EquipService.Make); gold is scaled to the player's phase so a pouch stays worth it.
    /// </summary>
    public static class ShopService
    {
        public enum Pay { Gold, Gems, Points, Free }

        public class Good
        {
            public string Id, Tab, Name, Icon, Slot, Grade;
            public Pay Pay; public int Price, Limit;
            public Func<PlayerState, int> Gold = _ => 0, Gems = _ => 0;
            public string Amount(PlayerState p) => Slot != null ? $"{Grade}급" : Gold(p) > 0 ? $"×{Gold(p):N0}" : $"×{Gems(p):N0}";
        }

        public static readonly string[] Tabs = { "일반 상점", "보석 상점", "교환소", "무료 보급" };

        static int Stage(PlayerState p) => Mathf.Max(1, p.stage);

        public static readonly List<Good> Goods = new()
        {
            new Good { Id = "g_monitor", Tab = "일반 상점", Name = "모니터 상자", Icon = "eq_monitor", Slot = "monitor", Grade = "C", Pay = Pay.Gold, Price = 4000, Limit = 3 },
            new Good { Id = "g_keyboard", Tab = "일반 상점", Name = "키보드 상자", Icon = "eq_keyboard", Slot = "keyboard", Grade = "C", Pay = Pay.Gold, Price = 4000, Limit = 3 },
            new Good { Id = "g_chair", Tab = "일반 상점", Name = "의자 상자", Icon = "eq_chair", Slot = "chair", Grade = "C", Pay = Pay.Gold, Price = 4000, Limit = 3 },
            new Good { Id = "g_badge", Tab = "일반 상점", Name = "사원증 상자", Icon = "eq_badge", Slot = "badge", Grade = "C", Pay = Pay.Gold, Price = 4000, Limit = 3 },
            new Good { Id = "g_monitor_b", Tab = "일반 상점", Name = "고급 모니터 상자", Icon = "eq_monitor", Slot = "monitor", Grade = "B", Pay = Pay.Gold, Price = 18000, Limit = 1 },
            new Good { Id = "g_chair_b", Tab = "일반 상점", Name = "고급 의자 상자", Icon = "eq_chair", Slot = "chair", Grade = "B", Pay = Pay.Gold, Price = 18000, Limit = 1 },
            new Good { Id = "m_pouch", Tab = "보석 상점", Name = "골드 주머니", Icon = "gold", Pay = Pay.Gems, Price = 20, Limit = 5, Gold = p => StatMath.StageGold(Stage(p)) * 25 },
            new Good { Id = "m_sack", Tab = "보석 상점", Name = "골드 자루", Icon = "gold", Pay = Pay.Gems, Price = 80, Limit = 2, Gold = p => StatMath.StageGold(Stage(p)) * 120 },
            new Good { Id = "m_box_a", Tab = "보석 상점", Name = "A급 비품 상자", Icon = "eq_keyboard", Slot = "keyboard", Grade = "A", Pay = Pay.Gems, Price = 180, Limit = 1 },
            new Good { Id = "m_box_b", Tab = "보석 상점", Name = "B급 사원증 상자", Icon = "eq_badge", Slot = "badge", Grade = "B", Pay = Pay.Gems, Price = 60, Limit = 2 },
            new Good { Id = "x_gems", Tab = "교환소", Name = "보석 묶음", Icon = "gem", Pay = Pay.Points, Price = 20, Limit = 5, Gems = _ => 60 },
            new Good { Id = "x_gold", Tab = "교환소", Name = "골드 주머니", Icon = "gold", Pay = Pay.Points, Price = 10, Limit = 5, Gold = p => StatMath.StageGold(Stage(p)) * 30 },
            new Good { Id = "x_box_a", Tab = "교환소", Name = "A급 모니터 상자", Icon = "eq_monitor", Slot = "monitor", Grade = "A", Pay = Pay.Points, Price = 60, Limit = 1 },
            new Good { Id = "f_gems", Tab = "무료 보급", Name = "오늘의 보석", Icon = "gem", Pay = Pay.Free, Price = 0, Limit = 1, Gems = _ => 30 },
            new Good { Id = "f_gold", Tab = "무료 보급", Name = "오늘의 골드", Icon = "gold", Pay = Pay.Free, Price = 0, Limit = 1, Gold = p => StatMath.StageGold(Stage(p)) * 20 },
            new Good { Id = "f_box", Tab = "무료 보급", Name = "비품 보급 상자", Icon = "eq_chair", Slot = "chair", Grade = "D", Pay = Pay.Free, Price = 0, Limit = 1 },
        };

        static void Roll(PlayerState p)
        {
            var today = QuestService.TodayKey();
            if (p.shopDate == today) return;
            p.shopDate = today; p.shopBought.Clear();
        }

        public static int Bought(PlayerState p, Good g)
        {
            Roll(p);
            var key = g.Id + ":";
            var e = p.shopBought.FirstOrDefault(x => x.StartsWith(key));
            return e != null && int.TryParse(e.Substring(key.Length), out var n) ? n : 0;
        }

        public static int Left(PlayerState p, Good g) => Mathf.Max(0, g.Limit - Bought(p, g));

        public static bool CanPay(PlayerState p, Good g) => g.Pay switch
        {
            Pay.Gold => p.gold >= g.Price, Pay.Gems => p.gems >= g.Price, Pay.Points => p.sparkPoints >= g.Price, _ => true,
        };

        public static bool CanBuy(PlayerState p, Good g) => Left(p, g) > 0 && CanPay(p, g);

        /// <summary>Buys one; returns what it gave as a line for the status bar, or null.</summary>
        public static string Buy(PlayerState p, Good g)
        {
            if (!CanBuy(p, g)) return null;
            switch (g.Pay) { case Pay.Gold: p.gold -= g.Price; break; case Pay.Gems: p.gems -= g.Price; break; case Pay.Points: p.sparkPoints -= g.Price; break; }
            var n = Bought(p, g) + 1;
            p.shopBought.RemoveAll(x => x.StartsWith(g.Id + ":"));
            p.shopBought.Add($"{g.Id}:{n}");
            string got;
            if (g.Slot != null) { var it = EquipService.Make(p, g.Slot, g.Grade); got = EquipService.Label(it); }
            else if (g.Gold(p) > 0) { var gold = g.Gold(p); p.gold += gold; got = $"골드 +{gold:N0}"; }
            else { var gems = g.Gems(p); p.gems += gems; got = $"보석 +{gems:N0}"; }
            return got;
        }

        public static string ResetIn()
        {
            var now = DateTime.Now; var left = now.Date.AddDays(1) - now;
            return $"{(int)left.TotalHours:00}:{left.Minutes:00}";
        }

        public static bool HasFree(PlayerState p) => Goods.Any(g => g.Pay == Pay.Free && Left(p, g) > 0);
    }

    /// <summary>
    /// 우편함: gifts that arrive and wait to be taken — a welcome box the first time, an attendance
    /// gift each day, a note from the office for each update. Seeded when the mailbox opens or the
    /// lobby is built; claimed one by one or all at once.
    /// </summary>
    public static class MailService
    {
        public static void Seed(PlayerState p)
        {
            var today = QuestService.TodayKey();
            void Add(MailItem m) { if (p.mail.All(x => x.id != m.id)) { m.day = today; p.mail.Add(m); } }
            Add(new MailItem { id = "welcome", title = "입사를 환영합니다!", from = "인사팀", body = "엑셀 히어로즈 사무실에 오신 것을 환영합니다. 첫 출근 선물을 준비했어요.", gems = 300, gold = 5000 });
            Add(new MailItem { id = "update_3d", title = "[업데이트] 적 3D 개편 기념", from = "운영팀", body = "오류들이 입체로 돌아왔습니다! 새 상점과 우편함도 열렸어요. 기념 보상을 받아 주세요.", gems = 150, itemSlot = "badge", itemGrade = "B" });
            Add(new MailItem { id = "daily_" + today, title = "오늘의 출근 보상", from = "총무팀", body = "오늘도 출근해 주셔서 감사합니다.", gems = 20, gold = StatMath.StageGold(Mathf.Max(1, p.stage)) * 10 });
            // keep the box tidy: claimed mail older than a week goes
            p.mail.RemoveAll(m => m.claimed && string.CompareOrdinal(m.day, DateTime.Now.AddDays(-7).ToString("yyyy-MM-dd")) < 0);
        }

        public static int Unclaimed(PlayerState p) => p.mail.Count(m => !m.claimed);

        public static string Claim(PlayerState p, MailItem m)
        {
            if (m == null || m.claimed) return null;
            m.claimed = true;
            p.gems += m.gems; p.gold += m.gold; p.sparkPoints += m.sparkPoints;
            var parts = new List<string>();
            if (m.gems > 0) parts.Add($"보석 +{m.gems:N0}");
            if (m.gold > 0) parts.Add($"골드 +{m.gold:N0}");
            if (m.sparkPoints > 0) parts.Add($"모집 포인트 +{m.sparkPoints}");
            if (!string.IsNullOrEmpty(m.itemSlot)) parts.Add(EquipService.Label(EquipService.Make(p, m.itemSlot, m.itemGrade)));
            return string.Join(" · ", parts);
        }

        public static (int n, string line) ClaimAll(PlayerState p)
        {
            var lines = new List<string>(); var n = 0;
            foreach (var m in p.mail.Where(x => !x.claimed).ToList()) { var l = Claim(p, m); if (l != null) { lines.Add(l); n++; } }
            return (n, string.Join(" / ", lines));
        }
    }

    /// <summary>공지: the office's notices (Resources/Data/notices.json) and which the player has read.</summary>
    public static class NoticeService
    {
        [Serializable] public class Notice { public string id, tag, title, date, body; }
        [Serializable] class File { public List<Notice> items = new(); }
        static List<Notice> _all;

        public static List<Notice> All
        {
            get
            {
                if (_all != null) return _all;
                var a = Resources.Load<TextAsset>("Data/notices");
                _all = a != null ? JsonUtility.FromJson<File>(a.text).items : new List<Notice>();
                return _all;
            }
        }

        public static int Unread(PlayerState p) => All.Count(n => !p.readNotices.Contains(n.id));
        public static void Read(PlayerState p, Notice n) { if (n != null && !p.readNotices.Contains(n.id)) p.readNotices.Add(n.id); }
    }
}
