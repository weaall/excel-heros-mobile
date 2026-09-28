using System;
using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 일일 업무 — the check-in stamp, today's tasks, and the all-clear bonus.
    ///
    /// Laid out against the web build's own sheet (index.html #sheet-quests) rather than invented:
    /// a header carrying the business date, the reset countdown, the streak and — the part that was
    /// missing — 한꺼번에 수령, then a body broken into titled `qs-block` sections.
    ///
    /// That header button is how anyone actually collects in the web build. Six tasks with six
    /// separate 수령 buttons is six taps for a decision nobody is making, and it was the entire
    /// interaction on this screen.
    ///
    /// The web lays its blocks in two columns; a phone gets one, in the same order.
    ///
    /// This screen is the gem economy. Everything else in the game spends gems and almost nothing
    /// else pays them, so a player who opens the app, stamps in and clears the list walks away with
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
            yield return new RibbonItem(Icons.Check, "일일 점검", () => _app.Rebuild(), "새로 고침");
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

            BuildHead(p);

            // Two columns: the calendar and the week's bonus on the left, the day's list on
            // the right. Stacked they needed a scroll, and the thing a player opens this sheet for
            // — what is ready to collect — was the part below the fold.
            var cols = UiKit.Div("qs-cols", _root);
            var left = UiKit.Div("qs-col", cols);
            var right = UiKit.Scroll("qs-col qs-col--wide", cols);

            BuildStamp(left, p, file);
            BuildAllClear(left, p, file);
            BuildTasks(right, p);
        }

        /// <summary>
        /// The sheet header: what day it is, how long until it rolls over, and one button that
        /// collects everything that is ready.
        /// </summary>
        void BuildHead(PlayerState p)
        {
            var head = UiKit.Div("sheet-head sheet-head--tight", _root);

            var left = UiKit.Div(null, head);
            UiKit.Text($"업무일 {DateTime.Now:MM-dd}", "sheet-head__title", left);
            var midnight = DateTime.Today.AddDays(1) - DateTime.Now;
            UiKit.Text($"초기화까지 {midnight.Hours:00}:{midnight.Minutes:00}", "sheet-head__sub", left);

            UiKit.Div("spacer", head);

            var ready = QuestService.ClaimableCount(p);
            var claimAll = UiKit.Btn($"한꺼번에 수령 {ready}", "btn btn--primary claim-all", () =>
            {
                var (count, gems, gold) = QuestService.ClaimEverything(p);
                if (count == 0) { _app.SetStatus("수령할 보상이 없습니다"); return; }
                AudioService.Play("victory", 0.6f);
                _app.SetStatus($"한꺼번에 수령 {count}건: 보석 +{gems}" + (gold > 0 ? $" · 골드 +{gold:N0}" : ""));
                Game.Touch();
            }, head);
            claimAll.SetEnabled(ready > 0);
        }

        /// <summary>
        /// 출근 도장 — a card of days rather than a row of dots. Each cell says which day it is and
        /// what that day pays, so "come back tomorrow, it is worth more" is legible without a
        /// sentence underneath saying it.
        /// </summary>
        void BuildStamp(VisualElement parent, PlayerState p, QuestFile file)
        {
            var block = UiKit.Div("qs-block", parent);
            Title(block, "출근 도장", $"연속 {p.streak}일 · 2일째부터 하루당 보석 +{file.streakGemsPerDay}");

            var days = UiKit.Div("stamp__grid", block);
            for (var i = 1; i <= file.streakMaxDays + 1; i++)
            {
                var stamped = p.streak >= i;
                var today = p.streak + 1 == i && QuestService.CanCheckIn(p);
                var bonus = Mathf.Min(i - 1, file.streakMaxDays);

                var cell = UiKit.Div("stamp-day", days);
                cell.EnableInClassList("stamp-day--on", stamped);
                cell.EnableInClassList("stamp-day--today", today);

                UiKit.Text(i > file.streakMaxDays ? "이후" : $"{i}일", "stamp-day__n", cell);
                var gem = GameData.Icon("gem");
                if (gem != null) UiKit.SetArt(UiKit.Div("stamp-day__icon", cell), gem);
                UiKit.Text($"{file.loginGems + bonus * file.streakGemsPerDay}", "stamp-day__gems", cell);
                if (today) UiKit.Text("TODAY", "stamp-day__today", cell);
                if (stamped) UiKit.Text("승인", "stamp-day__mark", cell);
            }

            if (QuestService.CanCheckIn(p))
            {
                var todayGems = file.loginGems
                              + Mathf.Min(Mathf.Max(0, p.streak), file.streakMaxDays) * file.streakGemsPerDay;
                UiKit.Btn($"출근 도장 찍기 · 보석 {todayGems}", "btn btn--primary stamp__go", () =>
                {
                    var (gems, _) = QuestService.CheckIn(p);
                    if (gems > 0) AudioService.Play("victory", 0.6f);
                    Game.Touch();
                }, block);
            }
            else UiKit.Text("오늘 출근 완료 — 내일 또 오세요.", "stamp__done", block);
        }

        /// <summary>
        /// 일일 업무, as the web's table: one row per task with its progress, its reward and its own
        /// button. The header's 한꺼번에 수령 is the fast path; these stay because a player still
        /// wants to see which line paid what.
        /// </summary>
        void BuildTasks(VisualElement parent, PlayerState p)
        {
            var block = UiKit.Div("qs-block", parent);
            Title(block, "일일 업무", "매일 자정 교체");

            foreach (var q in p.quests)
            {
                var def = GameData.Quest(q.id);
                if (def == null) continue;

                var row = UiKit.Div("qrow", block);
                row.EnableInClassList("qrow--done", q.claimed);

                // The description line is gone. It restates the name in other words — "몬스터
                // 100마리 처치" under "오류 100건 처리" — and it was the line that pushed five rows
                // past the height a landscape frame has for them.
                // Name and reward share a line, with the bar under both. Stacked on three
                // lines these rows were 108px each and the sixth quest of the day — there are
                // always six — fell out of the bottom of the panel with no way to reach it,
                // because a landscape build has no scrollbar to rescue it.
                var text = UiKit.Div("qrow__text", row);
                var head = UiKit.Div("qrow__head", text);
                UiKit.Text(def.name, "qrow__name", head);

                UiKit.Text($"{q.count} / {def.target}", "qrow__meta", head);

                var track = UiKit.Div("qrow__track", text);
                var fill = UiKit.Div("qrow__fill", track);
                fill.style.width = Length.Percent(Mathf.Clamp01(q.count / (float)def.target) * 100f);

                // the rewards as chips with their drawn icons, between the bar and the button —
                // "◈20 · ₩360" run into the progress text did not read as something to be won
                // (ui_critique round 3, 10-Quests #1)
                var rewards = UiKit.Div("qrow__rewards", row);
                RewardChip(rewards, "gem", def.gems.ToString("N0"));
                if (def.goldKills > 0) RewardChip(rewards, "gold", (def.goldKills * StatMath.StageGold(Mathf.Max(1, p.stage))).ToString("N0"));

                if (q.claimed) UiKit.Text("완료", "qrow__done", row);
                else
                {
                    var claim = UiKit.Btn("수령", "qrow__claim", () =>
                    {
                        var (gems, _) = QuestService.Claim(p, q.id);
                        if (gems > 0) AudioService.Play("bond");
                        Game.Touch();
                    }, row);
                    claim.SetEnabled(QuestService.CanClaim(p, q.id));
                    SkewPlate.Apply(claim, SkewPlate.Kind.Primary);
                }
            }
        }

        static void RewardChip(VisualElement parent, string icon, string amount)
        {
            var chip = UiKit.Div("rchip", parent);
            var art = GameData.Icon(icon);
            if (art != null) UiKit.SetArt(UiKit.Div("rchip__icon", chip), art);
            UiKit.Text(amount, "rchip__amount", chip);
        }

        void BuildAllClear(VisualElement parent, PlayerState p, QuestFile file)
        {
            var block = UiKit.Div("qs-block", parent);
            Title(block, "전체 완료 보너스", $"오늘의 업무를 모두 수령하면 보석 {file.allClearGems}");

            if (p.allClearClaimed) UiKit.Text("수령 완료", "stamp__done", block);
            else
            {
                var btn = UiKit.Btn($"보석 {file.allClearGems} 수령", "btn btn--primary", () =>
                {
                    if (QuestService.ClaimAllClear(p) > 0) AudioService.Play("victory");
                    Game.Touch();
                }, block);
                btn.SetEnabled(QuestService.CanClaimAllClear(p));
            }
        }

        /// <summary>A section heading, the web's `qs-title`: a name and a muted note beside it.</summary>
        static void Title(VisualElement parent, string name, string note)
        {
            var row = UiKit.Div("qs-title", parent);
            UiKit.Text(name, "qs-title__name", row);
            UiKit.Text(note, "qs-title__note", row);
        }
    }
}
