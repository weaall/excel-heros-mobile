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
            var right = UiKit.Scroll("prog-cols__right", cols);

            BuildAchievements(left, p);
            BuildPrestige(right, p);
            BuildDispatch(right, p);
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
            var claim = UiKit.Btn("", "arow__claim", () =>
            {
                if (ProgressService.Claim(Game.Player, id) <= 0) return;
                AudioService.Play("bond");
                Game.Touch();
                Refresh();
            }, row);
            // the drawn gem and the amount, not "◈50" (the glyph read as a bullet point)
            var gemArt = GameData.Icon("gem");
            if (gemArt != null) { var gi = UiKit.Div("arow__gem", claim); gi.pickingMode = PickingMode.Ignore; UiKit.SetArt(gi, gemArt); }
            UiKit.Text(gems.ToString(), "arow__amount", claim).pickingMode = PickingMode.Ignore;
            claim.SetEnabled(ProgressService.CanClaim(p, a));
            SkewPlate.Apply(claim, SkewPlate.Kind.Primary);   // the kit's plate, not a flat grey box
        }

        static string Tier(int claimed, int total) =>
            total <= 0 ? "" : $"{claimed}/{total}";

        /// <summary>
        /// 회사 이전 — the reset. It sits at the top of this column because it is the decision the
        /// rest of the column is building towards, and because a player who does not know it is
        /// there will grind a stage they cannot clear instead.
        /// </summary>
        void BuildPrestige(VisualElement parent, PlayerState p)
        {
            var b = GameData.Balance;
            var head = UiKit.Div("prog-head", parent);
            UiKit.Text("회사 이전", "section-title", head);
            UiKit.Div("spacer", head);
            UiKit.Text($"지분 {p.prestigeShares}　·　{p.prestigeCount}회", "muted", head);

            var panel = UiKit.Div("panel", parent);
            var bonus = PrestigeService.Bonus(p);
            UiKit.Text($"현재 지분 효과 · 공격력 +{bonus:P0} 골드 +{bonus:P0}", "synergy-line", panel);

            // The stage time is the signal this decision is actually made on: when a stage starts
            // taking minutes, pushing further stops being worth it and a reset is the faster route.
            var f = ForecastService.For(p, p.stage);
            UiKit.Text($"현재 스테이지 예상 소요 {ForecastService.Eta(f.Eta)}", "muted", panel);

            var gain = PrestigeService.Gain(p);
            if (gain <= 0)
            {
                UiKit.Text($"스테이지 {b.prestigeMinCleared} 클리어부터 이전할 수 있습니다 (현재 {p.maxCleared}).",
                    "muted", panel);
                return;
            }

            UiKit.Text($"지금 이전하면 지분 +{gain}", "muted", panel);
            UiKit.Btn($"회사 이전 · 지분 +{gain}", "btn btn--primary", () => Confirm(gain), panel);

            void Confirm(int shares)
            {
                // Confirmed, because it throws the run away. Everything it keeps is spelled out:
                // a player who is surprised by what a reset took will not press it a second time.
                var pane = UiKit.Div("onboard__card idle");
                UiKit.Text("회사 이전", "onboard__title", pane);
                UiKit.Text($"지분 +{shares}을 받고 스테이지·레벨·골드·사무실 개선을 처음부터 시작합니다.",
                    "muted", pane);
                UiKit.Text("사원, ★, 각성, 비품, 호감도는 그대로 남습니다.", "muted", pane);

                var row = UiKit.Div("party-actions", pane);
                UiKit.Btn("취소", "btn", _app.CloseOverlay, row);
                UiKit.Btn("이전한다", "btn btn--primary", () =>
                {
                    var got = PrestigeService.Reset(Game.Player);
                    if (got <= 0) { _app.CloseOverlay(); return; }
                    AudioService.Play("victory", 0.7f);
                    _app.SetStatus($"회사 이전 완료 · 지분 +{got}");
                    Game.Touch();
                    _app.CloseOverlay();
                    _app.Show(AppRoot.Sheet.Home);
                }, row);

                _app.OpenOverlay(pane);
            }
        }

        /// <summary>
        /// 출장 — the one system that pays for owning staff you never field, so it belongs beside
        /// the other things a long run collects rather than buried in the roster.
        /// </summary>
        void BuildDispatch(VisualElement parent, PlayerState p)
        {
            var info = DispatchService.Read(p);
            var b = GameData.Balance;

            var head = UiKit.Div("prog-head", parent);
            UiKit.Text("출장", "section-title", head);
            UiKit.Div("spacer", head);
            UiKit.Text($"오늘 {info.StartsLeft} / {b.dispatchMaxPerDay}회", "muted", head);

            var panel = UiKit.Div("panel", parent);

            if (info.Done)
            {
                var (gems, cards) = DispatchService.Preview(p, info.HeroIds);
                UiKit.Text($"복귀 · 보석 +{gems} 강화 카드 +{cards}", "synergy-line", panel);
                UiKit.Btn("보상 수령", "btn btn--primary", () =>
                {
                    var r = DispatchService.Claim(Game.Player);
                    if (r == null) return;
                    AudioService.Play("victory", 0.6f);
                    _app.SetStatus($"출장 복귀 · 보석 +{r.Value.Gems} · 강화 카드 +{r.Value.Cards}");
                    Game.Touch();
                    Refresh();
                }, panel);
                return;
            }

            if (info.Active)
            {
                var names = string.Join(", ", info.HeroIds.Select(id => GameData.Hero(id)?.name ?? id));
                UiKit.Text(names, "synergy-line", panel);
                UiKit.Text($"복귀까지 {DispatchService.Remaining(info.Remaining)}", "muted", panel);
                return;
            }

            if (!info.CanStart)
            {
                UiKit.Text(info.StartsLeft <= 0
                    ? "오늘 출장은 모두 보냈습니다."
                    : "편성에 들어 있지 않은 사원이 있어야 보낼 수 있습니다.", "muted", panel);
                return;
            }

            // The pick is the top of the bench, which is what a player would choose anyway — the
            // reward reads off grade, and the bench is already sorted by power.
            var picked = info.Bench.Take(b.dispatchSlots).Select(o => o.id).ToList();
            var preview = DispatchService.Preview(p, picked);
            // who goes and what they bring back on the left, the send button beside it at a fixed
            // size (ui_critique 11-Progress #2 — a full-width slab wasted the panel)
            var sendRow = UiKit.Div("disp-row", panel);
            var sendText = UiKit.Div("disp-row__text", sendRow);
            UiKit.Text(string.Join(", ", picked.Select(id => GameData.Hero(id)?.name ?? id)),
                "synergy-line", sendText);
            UiKit.Text($"{b.dispatchHours:0}시간 · 보석 +{preview.Gems} 강화 카드 +{preview.Cards}",
                "muted", sendText);

            UiKit.Btn("출장 보내기", "btn btn--primary disp-row__go", () =>
            {
                if (!DispatchService.Start(Game.Player, picked)) return;
                AudioService.Play("tap", 0.6f);
                _app.SetStatus($"출장 시작 · {b.dispatchHours:0}시간 후 복귀");
                Game.Touch();
                Refresh();
            }, sendRow);
        }

        void BuildMilestones(VisualElement parent, PlayerState p)
        {
            var head = UiKit.Div("prog-head", parent);
            UiKit.Text("마일스톤", "section-title", head);
            UiKit.Div("spacer", head);
            UiKit.Text($"{ProgressService.ClaimedCount(p)} / {GameData.Milestones.Count}　·　강화 카드 {p.cards:N0}",
                "muted", head);

            var pending = ProgressService.Pending(p);

            if (pending.Count > 0)
                UiKit.Btn($"{pending.Count}건 수령", "head-btn head-btn--notice", () =>
                {
                    var (count, gems, cards) = ProgressService.GrantPending(Game.Player);
                    if (count == 0) return;
                    AudioService.Play("victory", 0.6f);
                    _app.SetStatus($"마일스톤 {count}건: 보석 +{gems} · 강화 카드 +{cards}");
                    Game.Touch();
                    Refresh();
                }, head);

            var panel = UiKit.Div("panel", parent);

            if (pending.Count == 0)
            {
                UiKit.Text("아직 받을 마일스톤이 없습니다.", "muted", panel);
            }
            else
            {
                foreach (var m in pending.Take(2))
                    UiKit.Text($"{m.name}　·　◈{m.gems} 카드 {m.cards}", "synergy-line", panel);
                if (pending.Count > 2) UiKit.Text($"외 {pending.Count - 2}건", "muted", panel);
            }

            // What is next, inside the same panel rather than a second one. A list of only what
            // has been passed says nothing about where the run is going, but the column already
            // carries 회사 이전 and 출장 above this and has no room for another box.
            var upcoming = ProgressService.Upcoming(p);
            var soon = upcoming.FirstOrDefault();
            if (soon != null)
                UiKit.Text($"다음 · {soon.name}　{ProgressService.Value(p, soon):N0} / {soon.target:N0}",
                    "muted", panel);

        }
    }
}
