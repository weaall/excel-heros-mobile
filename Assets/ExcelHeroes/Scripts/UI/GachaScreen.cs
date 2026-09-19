using System.Collections;
using System.Collections.Generic;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 데이터 가져오기 — the summon screen, and the reason the rest of the game exists.
    ///
    /// A pull reveals one card at a time: the art fades up behind a rarity-coloured frame, the
    /// character says their entrance line, and the player taps to move on. A ten-pull walks the same
    /// reveal ten times and then lays the whole set out at once, because seeing the row is half the
    /// payoff. Rates and the two pity floors are printed on the banner rather than buried.
    /// </summary>
    public class GachaScreen : IScreen
    {
        public string Title => "모집";

        readonly AppRoot _app;
        VisualElement _root;
        Label _pityA, _pityS, _total;
        Button _one, _ten;

        public GachaScreen(AppRoot app) { _app = app; }

        public VisualElement Build()
        {
            _root = UiKit.Div("gacha");
            UiKit.Div("gacha__bg", _root);

            var banner = UiKit.Div("gacha__banner", _root);
            UiKit.Text("사원 모집 공고", "gacha__banner-title", banner);
            UiKit.Text("스프레드시트 괴물과 싸울 사람을 찾습니다", "gacha__banner-sub", banner);

            var rates = UiKit.Div("gacha__pity", banner);
            foreach (var g in GameData.Grades)
            {
                var chip = UiKit.Text($"{g.id} {g.rate:P1}", "pity-chip", rates);
                chip.style.color = g.Color;
            }

            var pity = UiKit.Div("gacha__pity", banner);
            _pityA = UiKit.Text("", "pity-chip", pity);
            _pityS = UiKit.Text("", "pity-chip", pity);
            _total = UiKit.Text("", "pity-chip", pity);

            var actions = UiKit.Div("gacha__actions", _root);
            _one = UiKit.Btn("", "btn", () => Pull(1), actions);
            _ten = UiKit.Btn("", "btn btn--primary", () => Pull(10), actions);

            Refresh();
            return _root;
        }

        public void Refresh()
        {
            var p = Game.Player;
            var (toA, toS) = GachaService.PityRemaining(p);
            _pityA.text = $"A 확정까지 {toA}";
            _pityS.text = $"S 확정까지 {toS}";
            _total.text = $"누적 {p.totalPulls}회";

            _one.text = $"1회 모집 · ◈{GachaService.CostFor(1)}";
            _ten.text = $"10회 모집 · ◈{GachaService.CostFor(10)}";
            _one.SetEnabled(GachaService.CanAfford(p, 1));
            _ten.SetEnabled(GachaService.CanAfford(p, 10));
        }

        void Pull(int count)
        {
            var results = GachaService.Buy(Game.Player, count);
            if (results == null) return;       // buttons are disabled when broke; this is belt and braces
            Game.Touch();
            _app.StartCoroutine(RevealSequence(results));
        }

        IEnumerator RevealSequence(List<PullResult> results)
        {
            var overlay = _app.Overlay;
            overlay.Clear();
            overlay.RemoveFromClassList("hidden");
            _app.SetNavEnabled(false);

            foreach (var r in results)
            {
                var skip = false;
                overlay.Clear();
                var view = BuildReveal(r, results.Count);
                overlay.Add(view);
                view.RegisterCallback<ClickEvent>(_ => skip = true);

                // Long enough to read the line, short enough that a ten-pull never drags.
                var hold = r.grade == "S" ? 2.2f : r.grade == "A" ? 1.5f : 0.85f;
                var t = 0f;
                while (t < hold && !skip) { t += Time.deltaTime; yield return null; }
                yield return null;   // swallow the click that ended this card
            }

            if (results.Count > 1)
            {
                var done = false;
                overlay.Clear();
                overlay.Add(BuildSummary(results, () => done = true));
                while (!done) yield return null;
            }

            overlay.Clear();
            overlay.AddToClassList("hidden");
            _app.SetNavEnabled(true);
            Refresh();
        }

        VisualElement BuildReveal(PullResult r, int batch)
        {
            var grade = GameData.Grade(r.grade);
            var view = UiKit.Div("reveal");

            var card = UiKit.Div("reveal__card", view);
            card.style.borderTopColor = card.style.borderBottomColor =
                card.style.borderLeftColor = card.style.borderRightColor = grade?.Color ?? Color.white;

            var art = UiKit.Div("reveal__art", card);
            UiKit.SetArt(art, GameData.CardArt(r.hero.id));

            var plate = UiKit.Div("reveal__plate", card);
            var g = UiKit.Text($"{r.grade} · {grade?.label}", "reveal__grade", plate);
            g.style.color = grade?.Color ?? Color.white;
            UiKit.Text(r.hero.name, "reveal__name", plate);
            UiKit.Text($"{r.hero.nick} · {r.hero.dept}", "reveal__nick", plate);
            if (!string.IsNullOrEmpty(r.hero.line)) UiKit.Text($"“{r.hero.line}”", "reveal__line", plate);

            if (r.isNew) UiKit.Text("NEW", "reveal__badge", plate);
            else if (r.promoted) UiKit.Text($"승급! ★{r.starAfter}", "reveal__badge", plate);
            else UiKit.Text($"중복 · 승급 조각 {r.copiesAfter}", "reveal__badge", plate);

            if (!string.IsNullOrEmpty(r.pityReason)) UiKit.Text(r.pityReason, "reveal__badge", plate);

            UiKit.Text(batch > 1 ? "탭하여 다음" : "탭하여 닫기", "reveal__hint", view);

            // A small pop on appear so each reveal lands rather than cuts.
            card.style.scale = new StyleScale(new Scale(new Vector3(0.9f, 0.9f, 1f)));
            card.schedule.Execute(() => card.style.scale = new StyleScale(new Scale(Vector3.one))).ExecuteLater(16);
            return view;
        }

        VisualElement BuildSummary(List<PullResult> results, System.Action onClose)
        {
            var view = UiKit.Div("reveal");
            UiKit.Text("모집 결과", "reveal__grade", view);

            var grid = UiKit.Div("reveal-grid", view);
            foreach (var r in results)
            {
                var grade = GameData.Grade(r.grade);
                var cell = UiKit.Div("reveal-grid__cell", grid);
                cell.style.borderTopColor = cell.style.borderBottomColor =
                    cell.style.borderLeftColor = cell.style.borderRightColor = grade?.Color ?? Color.gray;

                UiKit.SetArt(UiKit.Div("reveal-grid__art", cell), GameData.CardArt(r.hero.id));
                var plate = UiKit.Div("reveal-grid__plate", cell);
                var g = UiKit.Text(r.grade, "reveal-grid__grade", plate);
                g.style.color = grade?.Color ?? Color.white;
                UiKit.Text(r.hero.name, "reveal-grid__name", plate);
            }

            UiKit.Btn("확인", "btn btn--primary", onClose, view);
            return view;
        }
    }
}
