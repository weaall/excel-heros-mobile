using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 검토 — 업적 and 마일스톤, the two things a long idle run collects on the way up.
    ///
    /// Two columns, because they are two different shapes and reading them as one list is what the
    /// web build does only because it has a scroll. An achievement has tiers and keeps paying; a
    /// milestone is a mark passed once. The left column pages through the achievements, the right
    /// shows what has been reached and what is next.
    /// </summary>
    public class ProgressScreen : IScreen
    {
        public string Cell => "A2";
        public string Formula => "=검토";

        readonly AppRoot _app;
        VisualElement _root;
        Pages<AchievementDef> _pages;

        public ProgressScreen(AppRoot app) => _app = app;

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

            var cols = UiKit.Div("prog-cols", _root);
            var left = UiKit.Div("prog-cols__left", cols);
            var right = UiKit.Div("prog-cols__right", cols);

            BuildAchievements(left, p);
            BuildMilestones(right, p);
        }

        void BuildAchievements(VisualElement parent, PlayerState p)
        {
            var head = UiKit.Div("prog-head", parent);
            UiKit.Text("업적", "section-title", head);
            UiKit.Div("spacer", head);
            var ready = ProgressService.ClaimableCount(p);
            UiKit.Text(ready > 0 ? $"수령 가능 {ready}" : "수령 가능 없음", "muted", head);

            // Claimable first: the whole reason to open this screen is to collect, and a list that
            // buries what is ready under what is not makes the player hunt for it.
            var sorted = GameData.Achievements
                .OrderByDescending(a => ProgressService.CanClaim(p, a))
                .ThenBy(a => ProgressService.IsMaxed(p, a))
                .ToList();

            _pages = new Pages<AchievementDef>(parent, "prog-list", 5).Empty("업적이 없습니다");
            _pages.Fill(sorted, (a, list) => Row(list, p, a));
        }

        void Row(VisualElement parent, PlayerState p, AchievementDef a)
        {
            var row = UiKit.Div("arow", parent);
            var tier = ProgressService.ClaimedTiers(p, a.id);
            var maxed = ProgressService.IsMaxed(p, a);
            var target = ProgressService.NextTarget(p, a);
            var value = ProgressService.Value(p, a);

            var text = UiKit.Div("arow__text", row);
            UiKit.Text($"{a.name}　{Tier(tier, a.tiers?.Length ?? 0)}", "arow__name", text);

            var track = UiKit.Div("arow__track", text);
            var fill = UiKit.Div("arow__fill", track);
            fill.style.width = Length.Percent(maxed
                ? 100f
                : Mathf.Clamp01(target <= 0 ? 0f : value / (float)target) * 100f);

            UiKit.Text(maxed
                ? $"{a.desc}　·　전 단계 완료"
                : $"{a.desc}　·　{ProgressService.Format(value, a.unit)} / {ProgressService.Format(target, a.unit)}",
                "arow__meta", text);

            if (maxed)
            {
                UiKit.Text("완료", "arow__done", row);
                return;
            }

            var id = a.id;
            var gems = tier < (a.gems?.Length ?? 0) ? a.gems[tier] : 0;
            var claim = UiKit.Btn($"◈{gems}", "arow__claim", () =>
            {
                if (ProgressService.Claim(Game.Player, id) <= 0) return;
                AudioService.Play("bond");
                Game.Touch();
                Refresh();
            }, row);
            claim.SetEnabled(ProgressService.CanClaim(p, a));
        }

        static string Tier(int claimed, int total) =>
            total <= 0 ? "" : $"{claimed}/{total}";

        void BuildMilestones(VisualElement parent, PlayerState p)
        {
            var head = UiKit.Div("prog-head", parent);
            UiKit.Text("마일스톤", "section-title", head);
            UiKit.Div("spacer", head);
            UiKit.Text($"{ProgressService.ClaimedCount(p)} / {GameData.Milestones.Count}　·　강화 카드 {p.cards:N0}",
                "muted", head);

            var pending = ProgressService.Pending(p);
            var panel = UiKit.Div("panel", parent);

            if (pending.Count == 0)
            {
                UiKit.Text("아직 받을 마일스톤이 없습니다.", "muted", panel);
            }
            else
            {
                foreach (var m in pending.Take(4))
                    UiKit.Text($"{m.name}　·　◈{m.gems} 카드 {m.cards}", "synergy-line", panel);
                if (pending.Count > 4) UiKit.Text($"외 {pending.Count - 4}건", "muted", panel);

                var take = UiKit.Btn($"{pending.Count}건 한꺼번에 수령", "btn btn--primary", () =>
                {
                    var (count, gems, cards) = ProgressService.GrantPending(Game.Player);
                    if (count == 0) return;
                    AudioService.Play("victory", 0.6f);
                    _app.SetStatus($"마일스톤 {count}건: 보석 +{gems} · 강화 카드 +{cards}");
                    Game.Touch();
                    Refresh();
                }, panel);
            }

            // What is next, per kind. A list of only what has been passed says nothing about where
            // the run is going.
            var next = UiKit.Div("panel", parent);
            UiKit.Text("다음 목표", "section-title", next);
            var upcoming = ProgressService.Upcoming(p);
            if (upcoming.Count == 0)
            {
                UiKit.Text("모든 마일스톤을 지났습니다.", "muted", next);
            }
            else
            {
                foreach (var m in upcoming)
                    UiKit.StatRow(m.name, $"{ProgressService.Value(p, m):N0} / {m.target:N0}", next);
            }

        }
    }
}
