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
        public string Cell => "D4";
        public string Formula => "=QUERY(외부_데이터!A:F, \"select * where C is not null\")";

        readonly AppRoot _app;
        VisualElement _root, _pickup;
        Label _pityA, _pityS, _total;
        Button _one, _ten;

        /// <summary>
        /// 오늘의 픽업. Two featured cards, a countdown, and — the part that matters — a 모집 포인트
        /// price next to each. Every pull banks a point, the points never expire, so a player chasing
        /// one character can see exactly how far away they are instead of guessing at a 0.5% wall.
        /// </summary>
        void BuildPickup()
        {
            _pickup.Clear();
            if (GameData.Pickup == null) return;

            var left = GameData.BannerDaysLeft(System.DateTime.UtcNow);
            UiKit.Text($"오늘의 픽업 · {left}일 남음 · 해당 등급의 {GameData.Pickup.rate:P0}는 픽업으로",
                "pickup__timer", _pickup);

            var row = UiKit.Div("pickup", _pickup);
            foreach (var grade in new[] { "S", "A" })
            {
                var hero = GameData.Featured(grade, System.DateTime.UtcNow);
                if (hero == null) continue;
                var g = GameData.Grade(grade);

                var card = UiKit.Div("pickup__card", row);
                card.style.borderTopColor = card.style.borderBottomColor =
                    card.style.borderLeftColor = card.style.borderRightColor = g?.Color ?? Color.white;
                UiKit.SetArt(UiKit.Div("pickup__art", card), GameData.CardArt(hero.id));
                UiKit.Text($"PICK UP {grade}", "pickup__tag", card);

                var plate = UiKit.Div("pickup__plate", card);
                UiKit.Text(hero.name, "pickup__name", plate);

                var cost = GameData.SparkCost(grade);
                var have = Game.Player.sparkPoints;
                UiKit.Text($"모집 포인트 {have}/{cost}", "pickup__spark", plate);

                var take = UiKit.Btn("교환", "btn btn--primary", () =>
                {
                    var r = GachaService.Spark(Game.Player, grade);
                    if (r == null) return;
                    AudioService.Play("spark");
                    Game.Touch();
                    _app.StartCoroutine(RevealSequence(new List<PullResult> { r }));
                }, plate);
                take.SetEnabled(GachaService.CanSpark(Game.Player, grade));
            }
        }

        public GachaScreen(AppRoot app) { _app = app; }

        public IEnumerable<RibbonItem> Ribbon()
        {
            yield return new RibbonItem("⇩", "1행 가져오기", () => Pull(1), $"◈{GachaService.CostFor(1)}");
            yield return new RibbonItem("⇓", "10행 가져오기", () => Pull(10), $"◈{GachaService.CostFor(10)}");
        }

        public VisualElement Build()
        {
            _root = UiKit.Div("gacha");

            var banner = UiKit.Div("gacha__banner", _root);
            UiKit.Text("외부 데이터 가져오기", "gacha__banner-title", banner);
            UiKit.Text("인사 시스템에서 사원 레코드를 가져옵니다", "gacha__banner-sub", banner);

            _pickup = UiKit.Div(null, _root);
            BuildPickup();

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
            _total.text = $"모집 포인트 {p.sparkPoints:N0}";
            if (_pickup != null) BuildPickup();

            _one.text = $"1행 가져오기 · ◈{GachaService.CostFor(1)}";
            _ten.text = $"10행 가져오기 · ◈{GachaService.CostFor(10)}";
            _one.SetEnabled(GachaService.CanAfford(p, 1));
            _ten.SetEnabled(GachaService.CanAfford(p, 10));
        }

        void Pull(int count)
        {
            var results = GachaService.Buy(Game.Player, count);
            if (results == null) return;       // buttons are disabled when broke; this is belt and braces
            QuestService.Note(Game.Player, "pull", count);
            Game.Touch();
            _app.StartCoroutine(RevealSequence(results));
        }

        IEnumerator RevealSequence(List<PullResult> results)
        {
            var overlay = _app.Overlay;
            overlay.Clear();
            overlay.RemoveFromClassList("hidden");
            _app.SetNavEnabled(false);

            AudioService.Play("pull");

            foreach (var r in results)
            {
                var skip = false;
                overlay.Clear();

                // The tell runs first. A pull with no build-up is just a list of names appearing.
                var tell = BuildTell(r);
                overlay.Add(tell);
                tell.RegisterCallback<ClickEvent>(_ => skip = true);
                foreach (var step in PlayTell(tell, r, () => skip)) yield return step;

                overlay.Clear();
                var view = BuildReveal(r, results.Count);
                overlay.Add(view);
                view.RegisterCallback<ClickEvent>(_ => skip = true);
                AudioService.Play(AudioService.RevealId(r.grade));
                if (r.promoted) AudioService.Play("promote", 0.7f);

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

        /// <summary>
        /// The build-up. A beam of light rises in the rarity's colour, rings pulse out of it, and
        /// only then does the card land. The beam is the whole trick: by the time the art appears
        /// the player already knows what they got, so the reveal confirms a feeling instead of
        /// delivering information.
        ///
        /// Every rarity gets the same shape so the moment reads the same — what changes is how long
        /// it takes and how many rings, and that difference is the tension.
        /// </summary>
        VisualElement BuildTell(PullResult r)
        {
            var view = UiKit.Div("reveal");
            var tell = UiKit.Div("tell", view);
            var colour = GameData.Grade(r.grade)?.Color ?? Color.white;

            var beam = UiKit.Div("tell__beam", tell);
            beam.style.backgroundColor = new Color(colour.r, colour.g, colour.b, 0.55f);

            for (var i = 0; i < 3; i++)
            {
                var ring = UiKit.Div("tell__ring", tell);
                ring.style.borderTopColor = ring.style.borderBottomColor =
                    ring.style.borderLeftColor = ring.style.borderRightColor = colour;
                _rings.Add(ring);
            }

            _tellLabel = UiKit.Text("", "tell__label", tell);
            _tellLabel.style.color = colour;
            return view;
        }

        readonly List<VisualElement> _rings = new();
        Label _tellLabel;

        IEnumerable<object> PlayTell(VisualElement tell, PullResult r, System.Func<bool> skipped)
        {
            var rank = GameData.GradeRank(r.grade);
            // A D resolves almost immediately; an S makes you wait for it.
            var beats = Mathf.Clamp(rank, 0, 4);
            var beam = tell.Q(className: "tell__beam");

            yield return new WaitForSeconds(0.05f);
            beam?.AddToClassList("tell__beam--up");

            for (var i = 0; i <= beats && !skipped(); i++)
            {
                if (i < _rings.Count)
                {
                    _rings[i].AddToClassList("tell__ring--in");
                    _rings[i].schedule.Execute(() =>
                    {
                        _rings[i].RemoveFromClassList("tell__ring--in");
                        _rings[i].AddToClassList("tell__ring--out");
                    }).ExecuteLater(220);
                }
                AudioService.Play("tap", 0.35f + 0.15f * i);
                yield return new WaitForSeconds(rank >= 3 ? 0.30f : 0.16f);
            }

            // Naming the tier before the card lands is the payoff of the build-up.
            if (rank >= 3 && !skipped())
            {
                _tellLabel.text = r.grade == "S" ? "전설" : "영웅";
                _tellLabel.AddToClassList("tell__label--on");
                yield return new WaitForSeconds(0.45f);
            }

            _rings.Clear();
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

            if (r.isPickup) UiKit.Text("PICK UP", "reveal__badge", plate);
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
