using System.Linq;
using System.Collections;
using System.Collections.Generic;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 모집 — the summon screen, and the reason the rest of the game exists.
    ///
    /// A pull reveals one card at a time: the art fades up behind a rarity-coloured frame, the
    /// character says their entrance line, and the player taps to move on. A ten-pull walks the same
    /// reveal ten times and then lays the whole set out at once, because seeing the row is half the
    /// payoff. Rates and the two pity floors are printed on the banner rather than buried.
    /// </summary>
    public class GachaScreen : IScreen
    {
        static List<Vector2> Star4(Vector2 c, float r)
        {
            var pts = new List<Vector2>();
            for (var i = 0; i < 8; i++)
            {
                var a = i / 8f * Mathf.PI * 2f - Mathf.PI / 2f;
                var rad = i % 2 == 0 ? r : r * 0.32f;
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad);
            }
            return pts;
        }

        static List<Vector2> Heart(Vector2 c, float r)
        {
            var pts = new List<Vector2>();
            for (var i = 0; i < 40; i++)
            {
                var t = i / 40f * Mathf.PI * 2f;
                var x = 16f * Mathf.Pow(Mathf.Sin(t), 3f);
                var y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
                pts.Add(c + new Vector2(x, -y) * (r / 16f));
            }
            return pts;
        }

        public string Cell => "D4";
        public string Formula => "=QUERY(외부_데이터!A:F, \"select * where C is not null\")";

        readonly AppRoot _app;
        VisualElement _root, _pickup;
        VisualElement _side, _cards;
        Label _pityA, _pityS, _total;
        Button _one, _ten;

        public GachaScreen(AppRoot app) { _app = app; }

        public IEnumerable<RibbonItem> Ribbon()
        {
            yield return new RibbonItem(Icons.Gacha, "1회 모집", () => Pull(1), $"◈{GachaService.CostFor(1)}");
            yield return new RibbonItem(Icons.Gacha, "10회 모집", () => Pull(10), $"◈{GachaService.CostFor(10)}");
        }

        /// <summary>
        /// The banner, as the reference's 모집 screen lays it out: the pickup students standing
        /// large across the right of a full-bleed stage, the banner's title block on the left, a
        /// rail of banner tabs down the left edge, 확률 정보 and 모집 포인트 at the bottom left and
        /// the two pulls at the bottom right. Rates, pity floors and the point exchange live in
        /// modals behind those buttons — printed, one tap away, not crowding the picture.
        /// </summary>
        void BuildPickup()
        {
            _pickup.Clear();
            if (GameData.Pickup == null) return;
            var now = System.DateTime.UtcNow;
            var featuredS = GameData.Featured("S", now);
            var featuredA = GameData.Featured("A", now);
            var sColour = GameData.Grade("S")?.Color ?? Color.white;

            // the students: A a step behind and to the left, S in front
            if (featuredA != null) Figure(featuredA.id, "gstage__fig gstage__fig--a clips");   // standing figures cut at the knees, as on the reference
            if (featuredS != null) Figure(featuredS.id, "gstage__fig gstage__fig--s clips");

            // title block
            var block = UiKit.Div("gstage__block", _pickup);
            var kicker = UiKit.Div("gstage__kicker", block);
            ModalFrame.Painted(kicker, (ctx, r) =>
                UiPaint.Fill(ctx, UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.6f, 4f), UiPaint.C(28, 44, 80)));
            UiKit.Text("PICK UP 모집", "gstage__kicker-text", kicker);
            UiKit.Text(featuredS?.name ?? "신규 모집", "gstage__title", block);
            if (featuredA != null) UiKit.Text($"+ {featuredA.name}", "gstage__sub", block);
            var days = UiKit.Div("gstage__days", block);
            ModalFrame.Painted(days, (ctx, r) => UiPaint.Fill(ctx, UiPaint.RoundRect(r, r.height * 0.5f, 6), sColour));
            UiKit.Text($"종료까지 {GameData.BannerDaysLeft(now)}일", "gstage__days-text", days);
            UiKit.Text($"S 등급 {GameData.Grade("S")?.rate ?? 0.005f:P1} 중 절반이 픽업 사원으로 · 첫 10회 모집은 S 확정",
                       "gstage__note", block);
        }

        void Figure(string id, string classes)
        {
            var fig = UiKit.Div(classes, _pickup);
            fig.pickingMode = PickingMode.Ignore;
            var standing = GameData.StandingArt(id);
            UiKit.SetArt(fig, standing ?? GameData.CardArt(id));
            fig.EnableInClassList("gstage__fig--card", standing == null);
        }

        public VisualElement Build()
        {
            _root = UiKit.Div("gacha gacha--ba");
            // no painted sky of its own: the shell's illustrated scene is the room the pick-up stands in

            _pickup = UiKit.Div("gstage", _root);
            // a soft spotlight behind the students
            ModalFrame.Painted(_pickup, (ctx, r) =>
            {
                var c = new Vector2(r.xMin + r.width * 0.66f, r.yMin + r.height * 0.52f);
                for (var i = 6; i >= 1; i--)
                    UiPaint.Fill(ctx, UiPaint.Ellipse(c, r.width * 0.07f * i, r.height * 0.1f * i), UiPaint.C(255, 255, 255, 0.07f));
            });
            BuildPickup();

            // banner tabs, down the left edge
            var tabs = UiKit.Div("gtabs", _root);
            var featured = GameData.Featured("S", System.DateTime.UtcNow);
            var tab = UiKit.Div("gtab gtab--on", tabs);
            // docked to the screen's left edge, its right end cut on the slant, a cyan bar on the
            // docked side; the pickup's face in a disc beside the words (ui_critique 09-Gacha #2)
            ModalFrame.Painted(tab, (ctx, r) =>
            {
                var slant = SkewPlate.SlantFor(r.height) * 0.45f;
                var poly = new System.Collections.Generic.List<Vector2> { new(r.xMin - 60f, r.yMin), new(r.xMax, r.yMin), new(r.xMax - slant, r.yMax), new(r.xMin - 60f, r.yMax) };
                UiPaint.Shadow(ctx, poly, new Vector2(0f, 4f), UiPaint.C(0, 20, 50, 0.25f), 8f);
                UiPaint.Fill(ctx, poly, UiPaint.Vertical(UiPaint.C(255, 255, 255, 0.98f), UiPaint.C(236, 244, 251, 0.96f), r.yMin, r.yMax));
                UiPaint.Fill(ctx, UiPaint.Clip(poly, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin - 60f, r.yMin, r.xMin + 8f, r.yMax), 0f)), UiPaint.C(46, 135, 246));
                UiPaint.Stroke(ctx, poly, UiPaint.C(255, 255, 255, 0.9f), 1.5f);
            });
            var thumb = UiKit.Div("gtab__thumb", tab);
            if (featured != null) UiKit.SetPortrait(thumb, featured.id, UiKit.Crop.Face, false, round: true);
            var words = UiKit.Div("gtab__words", tab);
            UiKit.Text("PICK UP", "gtab__en", words);
            UiKit.Text("픽업 모집", "gtab__label", words);

            // bottom left: 확률 정보 and 모집 포인트
            var info = UiKit.Div("gfoot", _root);
            var rates = UiKit.Btn("확률 정보", "gfoot__btn", OpenRates, info);
            SkewPlate.Apply(rates, SkewPlate.Kind.Glass);
            var points = UiKit.Btn("", "gfoot__points", OpenExchange, info);
            SkewPlate.Apply(points, SkewPlate.Kind.Glass);
            _total = UiKit.Text("모집 포인트 0", "gfoot__points-text", points);

            // bottom right: the two pulls, 10회 in the reference's gold
            var actions = UiKit.Div("pull-bar gpulls", _root);
            _one = UiKit.Btn("1회 모집", "pull-btn", () => Pull(1), actions);
            _ten = UiKit.Btn("10회 모집", "pull-btn pull-btn--primary", () => Pull(10), actions);
            SkewPlate.Apply(_one, SkewPlate.Kind.Light);
            SkewPlate.Apply(_ten, SkewPlate.Kind.Gold);
            // the price under the title with the drawn gem, not "· ◈900" run into the label
            // (ui_critique round 1, 09-Gacha #2)
            _oneCost = CostRow(_one); _tenCost = CostRow(_ten);
            // over the 10-pull: how far the S floor is — a real number, where the reference puts
            // its guarantee ribbon (this build guarantees nothing per ten, so it says nothing it can't keep)
            _pityRibbon = UiKit.Text("", "gpulls__ribbon", _ten);

            Refresh();
            return _root;
        }

        VisualElement _pityAFill, _pitySFill;
        Label _oneCost, _tenCost, _pityRibbon;

        static Label CostRow(Button b)
        {
            var row = UiKit.Div("pull-btn__cost", b);
            row.pickingMode = PickingMode.Ignore;
            var gem = GameData.Icon("gem");
            if (gem != null) UiKit.SetArt(UiKit.Div("pull-btn__gem", row), gem);
            return UiKit.Text("", "pull-btn__price", row);
        }

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

        /// <summary>확률 정보: every grade's rate and the two pity floors.</summary>
        void OpenRates()
        {
            AudioService.Play("tap", 0.5f);
            var body = UiKit.Modal("확률 정보", _app.CloseOverlay, out var panel);
            var rates = UiKit.Div("rates", body);
            var rateRow = UiKit.Div("rates__row", rates);
            foreach (var g in GameData.Grades)
            {
                var chip = UiKit.Div("rate-chip", rateRow);
                var id = UiKit.Text(g.id, "rate-chip__id", chip);
                id.style.backgroundColor = g.Color;
                UiKit.Text($"{g.rate:P1}", "rate-chip__rate", chip);
            }
            UiKit.Text("천장 — 이 횟수 안에 반드시 나옵니다", "rates__caption", rates);
            (_pityA, _pityAFill) = PityRow(rates, "A", GameData.Grade("A")?.Color ?? Color.white);
            (_pityS, _pitySFill) = PityRow(rates, "S", GameData.Grade("S")?.Color ?? Color.white);
            Refresh();
            _app.OpenOverlay(panel);
        }

        /// <summary>모집 포인트 교환: the featured S and A, each for its point price.</summary>
        void OpenExchange()
        {
            AudioService.Play("tap", 0.5f);
            var body = UiKit.Modal("모집 포인트 교환", _app.CloseOverlay, out var panel);
            _cards = UiKit.Div("gacha__cards", body);
            BuildExchange();
            _app.OpenOverlay(panel);
        }

        void BuildExchange()
        {
            if (_cards == null) return;
            _cards.Clear();
            var row = UiKit.Div("pickup", _cards);
            foreach (var grade in new[] { "S", "A" })
            {
                var hero = GameData.Featured(grade, System.DateTime.UtcNow);
                if (hero == null) continue;
                var colour = GameData.Grade(grade)?.Color ?? Color.white;

                var card = UiKit.Div("pcard", row);
                card.style.borderBottomColor = colour;
                var art = UiKit.Div("pcard__art", card);
                UiKit.SetPortrait(art, hero.id, UiKit.Crop.Bust, false);
                var badge = UiKit.Text(grade, "pcard__grade", art);
                badge.style.backgroundColor = colour;

                var foot = UiKit.Div("pcard__foot", card);
                UiKit.Text(hero.name, "pcard__name", foot);
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
                    _app.CloseOverlay();
                    _app.StartCoroutine(RevealSequence(new List<PullResult> { r }));
                }, line);
                take.SetEnabled(GachaService.CanSpark(Game.Player, grade));
            }
        }

        public void Refresh()
        {
            var p = Game.Player;
            var b = GameData.Balance;
            var (toA, toS) = GachaService.PityRemaining(p);

            if (_pityA != null && _pityA.panel != null)
            {
                _pityA.text = $"{toA}회";
                _pityS.text = $"{toS}회";
                _pityAFill.style.width = Length.Percent(100f * (b.pityA - toA) / Mathf.Max(1, b.pityA));
                _pitySFill.style.width = Length.Percent(100f * (b.pityS - toS) / Mathf.Max(1, b.pityS));
            }
            if (_total != null) _total.text = $"모집 포인트 {p.sparkPoints:N0}";

            if (_pickup != null) BuildPickup();
            if (_cards != null && _cards.panel != null) BuildExchange();

            UiKit.SetBtnText(_one, "1회 모집");
            UiKit.SetBtnText(_ten, "10회 모집");
            if (_oneCost != null) _oneCost.text = GachaService.CostFor(1).ToString("N0");
            if (_tenCost != null) _tenCost.text = GachaService.CostFor(10).ToString("N0");
            if (_pityRibbon != null) _pityRibbon.text = $"S 천장까지 {toS}회";
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
            var again = false;
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
                overlay.Add(BuildSummary(results, () => done = true, () => { again = true; done = true; }));
                while (!done) yield return null;
            }

            overlay.Clear();
            overlay.AddToClassList("hidden");
            _app.SetNavEnabled(true);
            Refresh();
            if (again && GachaService.CanAfford(Game.Player, results.Count)) Pull(results.Count);
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

        /// <summary>
        /// A ten-pull result built from made-up pulls — one of each grade, two marked new — for
        /// the screenshot driver. Nothing is granted and nothing is saved.
        /// </summary>
        public static VisualElement Sample(System.Action onClose) => new GachaScreen(null).SampleSummary(onClose);

        public VisualElement SampleSummary(System.Action onClose)
        {
            var results = new List<PullResult>();
            var grades = new[] { "S", "A", "B", "C", "D", "A", "B", "C", "D", "D" };
            for (var i = 0; i < grades.Length; i++)
            {
                var def = GameData.Heroes.Find(h => h.grade == grades[i] && !results.Exists(r => r.hero == h))
                          ?? GameData.Heroes.Find(h => h.grade == grades[i]);
                if (def == null) continue;
                results.Add(new PullResult { hero = def, grade = def.grade, isNew = i == 0 || i == 5, starAfter = def.grade == "S" ? 3 : def.grade == "A" ? 2 : 1 });
            }
            return BuildSummary(results, onClose);
        }

        VisualElement BuildSummary(List<PullResult> results, System.Action onClose, System.Action again = null)
        {
            // Laid out the way the reference lays a ten-pull out: five across and two down on a
            // pale field, each card a portrait over a dark plate of stars, NEW called out in the
            // corner, one wide confirm underneath and the pity points in the corner opposite.
            var view = UiKit.Div("reveal");

            var grid = UiKit.Div("reveal-grid", view);
            // The Gemini card mock-up (tools/out/design/mock_card_0): all ten in ONE row of tall slanted
            // cards, the best first, each card washed in its grade's colour (S gold, A violet, B blue,
            // C / D slate) with a beam of light behind the top grades, the grade letter large in the
            // corner, stars and the name on a dark foot band.
            int Rank(string g) => g switch { "S" => 0, "A" => 1, "B" => 2, "C" => 3, _ => 4 };
            var ordered = results.Select((r, n) => (r, n)).OrderBy(x => Rank(x.r.grade)).ThenBy(x => x.n).Select(x => x.r).ToList();
            foreach (var r in ordered)
            {
                var grade = GameData.Grade(r.grade);
                var cell = UiKit.Div("reveal-grid__cell", grid);
                var sprite = GameData.StandingArt(r.hero.id);
                var card = sprite == null ? GameData.CardArt(r.hero.id) : null;
                var isS = r.grade == "S"; var isA = r.grade == "A";
                var (top, bot) = r.grade switch
                {
                    "S" => (UiPaint.C(255, 226, 120), UiPaint.C(244, 170, 40)),
                    "A" => (UiPaint.C(206, 160, 250), UiPaint.C(128, 78, 206)),
                    "B" => (UiPaint.C(140, 190, 250), UiPaint.C(60, 112, 204)),
                    _ => (UiPaint.C(176, 188, 204), UiPaint.C(104, 118, 142)),
                };
                if (false)
                {
                    // the beam: a soft vertical shaft of the grade's light behind the card
                    var beam = UiKit.Div("reveal-grid__beam", cell);
                    beam.pickingMode = PickingMode.Ignore;
                    var t0 = Time.realtimeSinceStartup;
                    ModalFrame.Painted(beam, (ctx, rr) =>
                    {
                        var k = 0.75f + 0.25f * Mathf.Sin((Time.realtimeSinceStartup - t0) * 2.4f);
                        var c = isS ? UiPaint.C(255, 236, 150) : UiPaint.C(230, 170, 255);
                        // nested soft bands, widest faintest: a shaft of light with no hard edge
                        for (var b = 0; b < 7; b++)
                        {
                            var f = 1f - b / 7f;
                            var half = rr.width * 0.5f * f;
                            var band = UiPaint.RoundRect(Rect.MinMaxRect(rr.center.x - half, rr.yMin, rr.center.x + half, rr.yMax), half * 0.9f);
                            UiPaint.Fill(ctx, band, UiPaint.WithAlpha(c, 0.1f * k), 2f);
                        }
                    });
                    beam.schedule.Execute(() => beam.MarkDirtyRepaint()).Every(50);
                }
                var bodyEl = UiKit.Div("reveal-grid__body", cell);
                bodyEl.pickingMode = PickingMode.Ignore;
                ModalFrame.Painted(bodyEl, (ctx, rect) =>
                {
                    var slant = SkewPlate.SlantFor(rect.height) * 0.3f;
                    var outer = UiPaint.SkewRect(rect, slant, 5f);
                    // a crisp outer glow along the card's edge for the top grades, in place of the airbrushed beam
                    if (isS) UiPaint.Ring(ctx, outer, UiPaint.C(255, 196, 40, 0.75f), UiPaint.C(255, 196, 40, 0f), 26f);
                    else if (isA) UiPaint.Ring(ctx, outer, UiPaint.C(176, 96, 250, 0.6f), UiPaint.C(176, 96, 250, 0f), 18f);
                    UiPaint.Shadow(ctx, outer, new Vector2(0f, 5f), UiPaint.C(20, 40, 80, 0.3f), 10f);
                    UiPaint.Fill(ctx, outer, Color.white);
                    var inner = UiPaint.Offset(outer, -3f);
                    var bandTop = rect.yMax - rect.height * 0.24f;
                    var artPoly = UiPaint.Clip(inner, new List<Vector2>
                    {
                        new Vector2(rect.xMin - 50f, rect.yMin - 50f), new Vector2(rect.xMax + 50f, rect.yMin - 50f),
                        new Vector2(rect.xMax + 50f, bandTop), new Vector2(rect.xMin - 50f, bandTop),
                    });
                    var dest = Rect.MinMaxRect(rect.xMin, rect.yMin + rect.height * 0.04f, rect.xMax, bandTop);
                    UiPaint.Fill(ctx, artPoly, UiPaint.Vertical(Color.Lerp(top, Color.white, 0.55f), Color.Lerp(bot, Color.white, 0.25f), rect.yMin, bandTop));
                    if (sprite != null) UiKit.PaintPortrait(ctx, artPoly, sprite, r.hero.id, dest, UiKit.Crop.Bust);
                    else UiPaint.Image(ctx, artPoly, card, dest, 0.1f);
                    // the grade's colour washing up from the foot of the art
                    UiPaint.Fill(ctx, artPoly, UiPaint.Vertical(UiPaint.WithAlpha(bot, 0f), UiPaint.WithAlpha(bot, 0.45f), rect.yMin + rect.height * 0.5f, bandTop), 0f);
                    var band = UiPaint.Clip(inner, new List<Vector2>
                    {
                        new Vector2(rect.xMin - 50f, bandTop), new Vector2(rect.xMax + 50f, bandTop),
                        new Vector2(rect.xMax + 50f, rect.yMax + 50f), new Vector2(rect.xMin - 50f, rect.yMax + 50f),
                    });
                    UiPaint.Fill(ctx, band, UiPaint.Vertical(Color.Lerp(bot, UiPaint.C(20, 30, 60), 0.35f), Color.Lerp(bot, UiPaint.C(14, 20, 44), 0.6f), bandTop, rect.yMax), 0f);
                    // a thin light line where the art meets the band
                    UiPaint.Fill(ctx, UiPaint.Clip(inner, UiPaint.RoundRect(Rect.MinMaxRect(rect.xMin - 50f, bandTop - 1.5f, rect.xMax + 50f, bandTop + 1.5f), 0f)), UiPaint.WithAlpha(top, 0.95f), 0f);
                    UiPaint.Stroke(ctx, outer, UiPaint.WithAlpha(bot, 0.9f), 2f);
                });
                // the grade on a slanted plate joined to the card's top corner (ui_critique: the floating
                // outline letter read as a legacy mobile game)
                var badge = UiKit.Div("reveal-grid__badge", cell);
                badge.pickingMode = PickingMode.Ignore;
                ModalFrame.Painted(badge, (ctx, rr) =>
                {
                    var plate = UiPaint.SkewRect(rr, SkewPlate.SlantFor(rr.height), 3f);
                    UiPaint.Shadow(ctx, plate, new Vector2(0f, 2f), UiPaint.C(0, 0, 0, 0.3f), 3f);
                    UiPaint.Fill(ctx, plate, UiPaint.Vertical(Color.Lerp(top, Color.white, 0.1f), bot, rr.yMin, rr.yMax));
                    UiPaint.Stroke(ctx, plate, UiPaint.C(255, 255, 255, 0.9f), 1.5f);
                });
                UiKit.Text(r.grade, "reveal-grid__badgetext", badge).pickingMode = PickingMode.Ignore;
                var stars = UiKit.Text(new string('★', Mathf.Clamp(r.starAfter, 1, 5)), "reveal-grid__stars2", cell);
                stars.pickingMode = PickingMode.Ignore;
                if (isS)
                {
                    var fx = UiKit.Div("reveal-grid__sfx", cell);
                    fx.pickingMode = PickingMode.Ignore;
                    var t0 = Time.realtimeSinceStartup;
                    ModalFrame.Painted(fx, (ctx, rr) =>
                    {
                        var t = Time.realtimeSinceStartup - t0;
                        for (var i = 0; i < 8; i++)
                        {
                            var a = i / 8f * Mathf.PI * 2f + t * 0.6f;
                            var c = rr.center + new Vector2(Mathf.Cos(a) * rr.width * 0.58f, Mathf.Sin(a) * rr.height * 0.56f);
                            var k = 0.5f + 0.5f * Mathf.Sin(t * 5f + i * 1.7f);
                            var sz = 8f + 10f * k;
                            UiPaint.Fill(ctx, Star4(c, sz), UiPaint.C(255, 244, 200, 0.4f + 0.6f * k));
                        }
                    });
                    fx.schedule.Execute(() => fx.MarkDirtyRepaint()).Every(33);
                }

                if (r.isNew) UiKit.Text("NEW", "reveal-grid__new reveal-grid__new--on", cell).pickingMode = PickingMode.Ignore;
                UiKit.Text(r.hero.name, "reveal-grid__name", cell).pickingMode = PickingMode.Ignore;
            }

            var foot = UiKit.Div("reveal-foot", view);
            // 확인, and beside it the same pull again — the reference's result screen offers both
            // (ui_critique round 3, 21-Pull10 #3)
            var ok = UiKit.Btn("확인", "btn btn--primary reveal-foot__ok", onClose, foot);
            SkewPlate.Apply(ok, SkewPlate.Kind.Primary);
            if (again != null && Game.Player != null)
            {
                var n = results.Count;
                var more = UiKit.Btn($"{n}회 더 모집", "btn reveal-foot__again", again, foot);
                SkewPlate.Apply(more, SkewPlate.Kind.Gold);
                var cost = UiKit.Div("pull-btn__cost", more); cost.pickingMode = PickingMode.Ignore;
                var gem = GameData.Icon("gem"); if (gem != null) UiKit.SetArt(UiKit.Div("pull-btn__gem", cost), gem);
                UiKit.Text(GachaService.CostFor(n).ToString("N0"), "pull-btn__price", cost);
                more.SetEnabled(GachaService.CanAfford(Game.Player, n));
            }

            var points = UiKit.Div("reveal-points", view);
            UiKit.Text("모집 포인트", "reveal-points__label", points);
            UiKit.Text(Game.Player.sparkPoints.ToString("N0"), "reveal-points__value", points);
            return view;
        }
    }
}
