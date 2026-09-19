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

        public GachaScreen(AppRoot app) { _app = app; }

        public IEnumerable<RibbonItem> Ribbon()
        {
            yield return new RibbonItem(Icons.Gacha, "1행 가져오기", () => Pull(1), $"◈{GachaService.CostFor(1)}");
            yield return new RibbonItem(Icons.Gacha, "10행 가져오기", () => Pull(10), $"◈{GachaService.CostFor(10)}");
        }

        /// <summary>
        /// The banner and the two featured cards.
        ///
        /// Laid out against a design reference (docs/design/pickup.md): a full-bleed splash of the
        /// S pickup with the title plate sitting on it, then the two cards at a size where the face
        /// is actually the subject, then the pull buttons hard-anchored at the foot. What was here
        /// before put two 150px thumbnails and three grey chips in the middle of the sheet with
        /// half the screen empty underneath, which read as a form rather than as a banner.
        ///
        /// Every trick is a flat one, because USS has no gradients and no shadows: thick bottom
        /// borders stand in for elevation and carry the rarity colour, a skewed tag breaks the grid,
        /// and an oversized low-opacity word fills the dead space behind the title.
        /// </summary>
        void BuildPickup()
        {
            _pickup.Clear();
            if (GameData.Pickup == null) return;

            var featuredS = GameData.Featured("S", System.DateTime.UtcNow);
            var sColour = GameData.Grade("S")?.Color ?? Color.white;

            // ---- the splash -------------------------------------------------------------
            var banner = UiKit.Div("banner", _pickup);
            if (featuredS != null) UiKit.SetArt(banner, GameData.CardArt(featuredS.id));

            // Fills the height the splash leaves empty, and gives the block an editorial spine.
            UiKit.Text("RECRUITMENT", "banner__watermark", banner);

            var tagWrap = UiKit.Div("banner__tag", banner);
            tagWrap.style.backgroundColor = sColour;
            UiKit.Text($"{GameData.BannerDaysLeft(System.DateTime.UtcNow)}일 남음", "banner__tag-text", tagWrap);

            var plate = UiKit.Div("banner__plate", banner);
            plate.style.borderLeftColor = sColour;
            UiKit.Text("PICK UP", "banner__kicker", plate);
            UiKit.Text("데이터_가져오기", "banner__title", plate);

            // ---- the two featured cards -------------------------------------------------
            var row = UiKit.Div("pickup", _pickup);
            foreach (var grade in new[] { "S", "A" })
            {
                var hero = GameData.Featured(grade, System.DateTime.UtcNow);
                if (hero == null) continue;
                var g = GameData.Grade(grade);
                var colour = g?.Color ?? Color.white;

                var card = UiKit.Div("pcard", row);
                card.style.borderBottomColor = colour;

                var art = UiKit.Div("pcard__art", card);
                UiKit.SetArt(art, GameData.CardArt(hero.id));
                var badge = UiKit.Text(grade, "pcard__grade", art);
                badge.style.backgroundColor = colour;

                var foot = UiKit.Div("pcard__foot", card);
                UiKit.Text(hero.name, "pcard__name", foot);

                // 모집 포인트 as a bar. A pity counter is a progress bar written as a fraction;
                // drawing it as one is the whole reason a player can tell how close they are.
                var cost = GameData.SparkCost(grade);
                var have = Game.Player.sparkPoints;
                var track = UiKit.Div("pcard__track", foot);
                var fill = UiKit.Div("pcard__fill", track);
                fill.style.width = Length.Percent(Mathf.Clamp01(have / (float)cost) * 100f);
                fill.style.backgroundColor = colour;

                var line = UiKit.Div("pcard__line", foot);
                UiKit.Text($"{have} / {cost}", "pcard__points", line);
                UiKit.Div("spacer", line);
                var take = UiKit.Btn("교환", "pcard__take", () =>
                {
                    var r = GachaService.Spark(Game.Player, grade);
                    if (r == null) return;
                    AudioService.Play("spark");
                    Game.Touch();
                    _app.StartCoroutine(RevealSequence(new List<PullResult> { r }));
                }, line);
                take.SetEnabled(GachaService.CanSpark(Game.Player, grade));
            }
        }

        public VisualElement Build()
        {
            _root = UiKit.Div("gacha");

            var scroll = UiKit.Scroll("gacha__scroll", _root);
            _pickup = UiKit.Div(null, scroll);
            BuildPickup();

            // Rates and the two pity floors, as one block of numbers under the banner rather than
            // as chips floating over it. Printed, not buried: they are what the banner is selling.
            var rates = UiKit.Div("rates", scroll);
            var rateRow = UiKit.Div("rates__row", rates);
            foreach (var g in GameData.Grades)
            {
                var chip = UiKit.Div("rate-chip", rateRow);
                var id = UiKit.Text(g.id, "rate-chip__id", chip);
                id.style.backgroundColor = g.Color;
                UiKit.Text($"{g.rate:P1}", "rate-chip__rate", chip);
            }

            (_pityA, _pityAFill) = PityRow(rates, "A", GameData.Grade("A")?.Color ?? Color.white);
            (_pityS, _pitySFill) = PityRow(rates, "S", GameData.Grade("S")?.Color ?? Color.white);

            // ---- the foot: points on the left, the two pulls on the right ---------------
            var actions = UiKit.Div("pull-bar", _root);
            var points = UiKit.Div("pull-bar__points", actions);
            UiKit.Text("모집 포인트", "pull-bar__label", points);
            _total = UiKit.Text("0", "pull-bar__value", points);

            _one = UiKit.Btn("", "pull-btn", () => Pull(1), actions);
            _ten = UiKit.Btn("", "pull-btn pull-btn--primary", () => Pull(10), actions);

            Refresh();
            return _root;
        }

        VisualElement _pityAFill, _pitySFill;

        static (Label, VisualElement) PityRow(VisualElement parent, string grade, Color colour)
        {
            var row = UiKit.Div("pity__row", parent);
            var tag = UiKit.Text(grade, "pity__tag", row);
            tag.style.backgroundColor = colour;
            var track = UiKit.Div("pity__track", row);
            var fill = UiKit.Div("pity__fill", track);
            fill.style.backgroundColor = colour;
            var label = UiKit.Text("", "pity__label", row);
            return (label, fill);
        }

        public void Refresh()
        {
            var p = Game.Player;
            var b = GameData.Balance;
            var (toA, toS) = GachaService.PityRemaining(p);

            _pityA.text = $"{toA}회";
            _pityS.text = $"{toS}회";
            _pityAFill.style.width = Length.Percent(100f * (b.pityA - toA) / Mathf.Max(1, b.pityA));
            _pitySFill.style.width = Length.Percent(100f * (b.pityS - toS) / Mathf.Max(1, b.pityS));
            _total.text = p.sparkPoints.ToString("N0");

            if (_pickup != null) BuildPickup();

            _one.text = $"1행 · ◈{GachaService.CostFor(1)}";
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
