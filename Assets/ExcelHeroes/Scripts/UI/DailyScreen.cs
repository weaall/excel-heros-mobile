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
        public string Title => "일일 업무";

        readonly AppRoot _app;
        VisualElement _root;

        public DailyScreen(AppRoot app) { _app = app; }

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
            var stamp = UiKit.Div("panel stamp", _root);
            UiKit.Text("출근 도장", "section-title", stamp);

            var streakRow = UiKit.Div("stamp__row", stamp);
            for (var i = 1; i <= file.streakMaxDays + 1; i++)
            {
                var dot = UiKit.Div("stamp__dot", streakRow);
                dot.EnableInClassList("stamp__dot--on", p.streak >= i);
            }

            var bonusDays = Mathf.Min(Mathf.Max(0, p.streak), file.streakMaxDays);
            UiKit.Text($"연속 {p.streak}일 · 오늘 보석 {file.loginGems + bonusDays * file.streakGemsPerDay}",
                "muted", stamp);

            if (QuestService.CanCheckIn(p))
            {
                UiKit.Btn("출근하기", "btn btn--primary", () =>
                {
                    var (gems, gold) = QuestService.CheckIn(p);
                    if (gems > 0) AudioService.Play("victory", 0.6f);
                    Game.Touch();
                }, stamp);
            }
            else
            {
                UiKit.Text("오늘 출근 완료 — 내일 또 오세요.", "muted", stamp);
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
