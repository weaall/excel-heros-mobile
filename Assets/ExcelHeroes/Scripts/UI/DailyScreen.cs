using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 일일 업무 — the check-in stamp, today's six tasks, and the all-clear bonus.
    ///
    /// This screen is the gem economy. Everything else in the game spends gems; almost nothing else
    /// pays them. A player who opens the app, stamps in, and finishes the list walks away with
    /// roughly half a ten-pull — which is the only reason tomorrow exists.
    /// </summary>
    public class DailyScreen : IScreen
    {
        public string Cell => "C3";
        public string Formula => "=COUNTIF(일일_점검!E2:E10,TRUE)";

        readonly AppRoot _app;
        VisualElement _root;

        public DailyScreen(AppRoot app) { _app = app; }

        public IEnumerable<RibbonItem> Ribbon()
        {
            yield return new RibbonItem("✓", "일일 점검", () => _app.Rebuild(), "새로 고침");
        }

        public VisualElement Build()
        {
            _root = UiKit.Div("screen-body");
            Refresh();
            return _root;
        }

        public void Refresh()
        {
            if (_root == null) return;
            _root.Clear();

            var p = Game.Player;
            QuestService.EnsureToday(p);
            var file = GameData.Quests;

            // --- 출근 도장 ---------------------------------------------------------------
            //
            // A stamp card, not a row of dots. Each day is a cell with its number and what it pays,
            // today is outlined, and stamped days carry the mark — so the thing the screen is
            // actually selling (come back tomorrow, it pays more) is legible without reading a
            // sentence underneath. The dots version could not show what any given day was worth.
            var stamp = UiKit.Div("stamp", _root);

            var stampHead = UiKit.Div("stamp__head", stamp);
            UiKit.Text("출근 도장", "stamp__title", stampHead);
            UiKit.Div("spacer", stampHead);
            UiKit.Text($"연속 {p.streak}일", "stamp__streak", stampHead);

            var days = UiKit.Div("stamp__grid", stamp);
            for (var i = 1; i <= file.streakMaxDays + 1; i++)
            {
                var stamped = p.streak >= i;
                var today = p.streak + 1 == i && QuestService.CanCheckIn(p);
                var bonus = Mathf.Min(i - 1, file.streakMaxDays);

                var cell = UiKit.Div("stamp-day", days);
                cell.EnableInClassList("stamp-day--on", stamped);
                cell.EnableInClassList("stamp-day--today", today);

                UiKit.Text(i > file.streakMaxDays ? "이후" : $"{i}일", "stamp-day__n", cell);
                UiKit.Text($"◈{file.loginGems + bonus * file.streakGemsPerDay}", "stamp-day__gems", cell);
                if (stamped) UiKit.Text("승인", "stamp-day__mark", cell);
            }

            if (QuestService.CanCheckIn(p))
            {
                var todayGems = file.loginGems
                              + Mathf.Min(Mathf.Max(0, p.streak), file.streakMaxDays) * file.streakGemsPerDay;
                UiKit.Btn($"출근하기 · 보석 {todayGems}", "btn btn--primary stamp__go", () =>
                {
                    var (gems, gold) = QuestService.CheckIn(p);
                    if (gems > 0) AudioService.Play("victory", 0.6f);
                    Game.Touch();
                }, stamp);
            }
            else
            {
                UiKit.Text("오늘 출근 완료 — 내일 또 오세요.", "stamp__done", stamp);
            }

            // --- 오늘의 업무 -------------------------------------------------------------
            var list = UiKit.Scroll(null, _root);

            foreach (var q in p.quests)
            {
                var def = GameData.Quest(q.id);
                if (def == null) continue;

                var row = UiKit.Div("panel quest", list);
                row.EnableInClassList("quest--done", q.claimed);

                UiKit.Text(def.name, "section-title", row);
                UiKit.Text(def.desc, "muted", row);

                var track = UiKit.Div("quest__track", row);
                var fill = UiKit.Div("quest__fill", track);
                fill.style.width = Length.Percent(Mathf.Clamp01(q.count / (float)def.target) * 100f);

                var reward = def.goldKills > 0
                    ? $"◈{def.gems} · ₩{def.goldKills * StatMath.StageGold(Mathf.Max(1, p.stage)):N0}"
                    : $"◈{def.gems}";
                UiKit.Text($"{q.count} / {def.target}　·　{reward}", "quest__meta", row);

                if (q.claimed) UiKit.Text("수령 완료", "muted", row);
                else
                {
                    var claim = UiKit.Btn("보상 수령", "btn btn--primary", () =>
                    {
                        var (gems, _) = QuestService.Claim(p, q.id);
                        if (gems > 0) AudioService.Play("bond");
                        Game.Touch();
                    }, row);
                    claim.SetEnabled(QuestService.CanClaim(p, q.id));
                }
            }

            // --- 전체 완료 ---------------------------------------------------------------
            var all = UiKit.Div("panel", list);
            UiKit.Text("전체 완료 보너스", "section-title", all);
            UiKit.Text($"오늘의 업무를 모두 수령하면 보석 {file.allClearGems}", "muted", all);
            if (p.allClearClaimed) UiKit.Text("수령 완료", "muted", all);
            else
            {
                var btn = UiKit.Btn($"보석 {file.allClearGems} 수령", "btn btn--primary", () =>
                {
                    if (QuestService.ClaimAllClear(p) > 0) AudioService.Play("victory");
                    Game.Touch();
                }, all);
                btn.SetEnabled(QuestService.CanClaimAllClear(p));
            }
        }
    }
}
