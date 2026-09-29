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
    /// Two banners, 픽업 and 일반. A pull plays one tell, then deals the cards face down and turns
    /// them over; an A or S stops the deal for its full-illustration entrance, SKIP turns them all
    /// (GachaFx). Every result card and pick-up figure opens the member's card. Rates and the two
    /// pity floors are printed on the banner rather than buried.
    /// </summary>
    public class GachaScreen : IScreen
    {
        public string Cell => "D4";
        public string Formula => "=QUERY(외부_데이터!A:F, \"select * where C is not null\")";

        readonly AppRoot _app;
        VisualElement _root, _pickup;
        // 픽업 모집 (the featured pair, half of S / A on them, 모집 포인트) or 일반 모집 (the whole cast evenly)
        bool _normal;
        VisualElement _tabPick, _tabNormal;
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
            if (_normal) { BuildNormal(); return; }
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
            // a sharp slanted chip, red when the banner is about to end (ui_critique 09-Gacha #2)
            var soon = GameData.BannerDaysLeft(now) <= 3;
            ModalFrame.Painted(days, (ctx, r) => UiPaint.Fill(ctx, UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.7f, 3f),
                soon ? UiPaint.C(255, 83, 112) : UiPaint.C(43, 112, 224)));
            UiKit.Text($"종료까지 {GameData.BannerDaysLeft(now)}일", "gstage__days-text", days);
            var noteBox = UiKit.Div("gstage__notebox", block);
            // a dark glass strip with a cyan edge, white type (ui_critique r6 09-Gacha #2)
            ModalFrame.Painted(noteBox, (ctx, r) =>
            {
                UiPaint.Fill(ctx, UiPaint.RoundRect(r, 4f), UiPaint.C(255, 255, 255, 0.9f));
                UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin, r.yMin, r.xMin + 5f, r.yMax), 0f), UiPaint.C(0, 212, 255));
            });
            UiKit.Text($"S 등급 {GameData.Grade("S")?.rate ?? 0.005f:P1} 중 절반이 픽업 사원으로 · 첫 10회 모집은 S 확정",
                       "gstage__note", noteBox);
        }

        /// <summary>
        /// A pick-up member from the waist up (the user: the whole standing figure crossing its legs
        /// read as a poster; the upper body reads as a person), in a window that crops the legs.
        /// Tapping her opens her card.
        /// </summary>
        void Figure(string id, string classes)
        {
            var win = UiKit.Div(classes.Replace("gstage__fig", "gstage__bust").Replace(" clips", ""), _pickup);
            var standing = GameData.StandingArt(id);
            var fig = UiKit.Div(standing != null ? "gstage__bust-art" : "gstage__bust-art gstage__bust-art--card", win);
            fig.pickingMode = PickingMode.Ignore;
            UiKit.SetArt(fig, standing ?? GameData.CardArt(id));
            win.RegisterCallback<ClickEvent>(_ => OpenInfo(id));
            var hint = UiKit.Text("정보 보기", "gstage__bust-hint", win); hint.pickingMode = PickingMode.Ignore;
        }

        void OpenInfo(string id)
        {
            if (_app == null) return;
            AudioService.Play("tap", 0.5f);
            _app.OpenOverlay(GachaFx.Info(id, _app.CloseOverlay));
        }

        /// <summary>일반 모집: no one featured — a line of the cast's SD figures, and what the banner is.</summary>
        void BuildNormal()
        {
            var cast = GameData.Heroes.Where(h => GameData.SdArt(h.id) != null && h.id != GameData.MainId).ToList();
            if (cast.Count == 0) return;
            var day = System.DateTime.UtcNow.DayOfYear;
            var pick = Enumerable.Range(0, 9).Select(i => cast[(day * 7 + i * 13) % cast.Count]).Distinct().Take(6).ToList();
            var line = UiKit.Div("gstage__sdline", _pickup);
            for (var i = 0; i < pick.Count; i++)
            {
                var sd = UiKit.Div("gstage__sd", line);
                UiKit.SetArt(sd, GameData.SdArt(pick[i].id));
                sd.style.translate = new Translate(0f, i % 2 == 0 ? 0f : -46f);
                var id = pick[i].id;
                sd.RegisterCallback<ClickEvent>(_ => OpenInfo(id));
            }
            var block = UiKit.Div("gstage__block", _pickup);
            var kicker = UiKit.Div("gstage__kicker", block);
            ModalFrame.Painted(kicker, (ctx, r) =>
                UiPaint.Fill(ctx, UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.6f, 4f), UiPaint.C(28, 44, 80)));
            UiKit.Text("STANDARD 모집", "gstage__kicker-text", kicker);
            UiKit.Text("일반 모집", "gstage__title", block);
            UiKit.Text("모든 사원이 같은 확률로", "gstage__sub", block);
            var noteBox = UiKit.Div("gstage__notebox", block);
            ModalFrame.Painted(noteBox, (ctx, r) =>
            {
                UiPaint.Fill(ctx, UiPaint.RoundRect(r, 4f), UiPaint.C(255, 255, 255, 0.9f));
                UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin, r.yMin, r.xMin + 5f, r.yMax), 0f), UiPaint.C(0, 212, 255));
            });
            UiKit.Text("픽업 없음 · 모집 포인트 없음 · 천장은 두 모집 공통", "gstage__note", noteBox);
        }

        void SetBanner(bool normal)
        {
            if (_normal == normal) return;
            _normal = normal;
            AudioService.Play("tap", 0.5f);
            _tabPick?.EnableInClassList("gtab--on", !normal);
            _tabNormal?.EnableInClassList("gtab--on", normal);
            _tabPick?.MarkDirtyRepaint(); _tabNormal?.MarkDirtyRepaint();
            BuildPickup();
        }

        void PaintTab(VisualElement tab, System.Func<bool> on)
        {
            ModalFrame.Painted(tab, (ctx, r) =>
            {
                var slant = SkewPlate.SlantFor(r.height) * 0.45f;
                var lit = on();
                var poly = new System.Collections.Generic.List<Vector2> { new(r.xMin - 60f, r.yMin), new(r.xMax, r.yMin), new(r.xMax - slant, r.yMax), new(r.xMin - 60f, r.yMax) };
                UiPaint.Shadow(ctx, poly, new Vector2(0f, 4f), UiPaint.C(0, 20, 50, 0.25f), 8f);
                UiPaint.Fill(ctx, poly, lit ? UiPaint.Vertical(UiPaint.C(255, 255, 255, 0.98f), UiPaint.C(236, 244, 251, 0.96f), r.yMin, r.yMax)
                                            : UiPaint.Vertical(UiPaint.C(196, 208, 226, 0.45f), UiPaint.C(180, 194, 214, 0.45f), r.yMin, r.yMax));   // the closed banner recedes (ui_gate 09-Gacha)
                if (lit) UiPaint.Fill(ctx, UiPaint.Clip(poly, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin - 60f, r.yMin, r.xMin + 14f, r.yMax), 0f)), UiPaint.C(46, 135, 246));
                if (lit) UiPaint.Ring(ctx, poly, UiPaint.C(0, 200, 255, 0.35f), UiPaint.C(0, 200, 255, 0f), 10f);
                UiPaint.Stroke(ctx, poly, UiPaint.C(255, 255, 255, 0.9f), 1.5f);
            });
        }

        /// <summary>The open banner screen (the screenshot driver switches its banner).</summary>
        public static GachaScreen Current { get; private set; }
        public void DebugBanner(bool normal) => SetBanner(normal);

        public VisualElement Build()
        {
            Current = this;
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
            // two banners (the user: 픽업 and 일반 must be told apart), each docked to the screen's left
            // edge, its right end cut on the slant, a cyan bar on the docked side of the open one
            var tab = _tabPick = UiKit.Div("gtab gtab--on", tabs);
            tab.RegisterCallback<ClickEvent>(_ => SetBanner(false));
            PaintTab(tab, () => !_normal);
            var thumb = UiKit.Div("gtab__thumb", tab);
            if (featured != null) UiKit.SetPortrait(thumb, featured.id, UiKit.Crop.Face, false, round: true);
            var words = UiKit.Div("gtab__words", tab);
            UiKit.Text("PICK UP", "gtab__en", words);
            UiKit.Text("픽업 모집", "gtab__label", words);

            var tab2 = _tabNormal = UiKit.Div("gtab", tabs);
            tab2.RegisterCallback<ClickEvent>(_ => SetBanner(true));
            PaintTab(tab2, () => _normal);
            var thumb2 = UiKit.Div("gtab__thumb", tab2);
            var anyone = GameData.Heroes.FirstOrDefault(h => h.grade == "B" && GameData.StandingArt(h.id) != null);
            if (anyone != null) UiKit.SetPortrait(thumb2, anyone.id, UiKit.Crop.Face, false, round: true);
            var words2 = UiKit.Div("gtab__words", tab2);
            UiKit.Text("STANDARD", "gtab__en", words2);
            UiKit.Text("일반 모집", "gtab__label", words2);

            // bottom left: 확률 정보 and 모집 포인트
            var info = UiKit.Div("gfoot", _root);
            var rates = UiKit.Btn("확률 정보", "gfoot__btn", OpenRates, info);
            SkewPlate.Apply(rates, SkewPlate.Kind.Glass);
            var points = UiKit.Btn("", "gfoot__points", OpenExchange, info);
            SkewPlate.Apply(points, SkewPlate.Kind.Glass);   // the same family as 확률 정보 beside it (ui_gate 09-Gacha)
            _total = UiKit.Text("모집 포인트 0", "gfoot__points-text", points);

            // bottom right: the two pulls, 10회 in the reference's gold
            var actions = UiKit.Div("pull-bar gpulls", _root);
            _one = UiKit.Btn("1회 모집", "pull-btn", () => Pull(1), actions);
            _ten = UiKit.Btn("10회 모집", "pull-btn pull-btn--primary", () => Pull(10), actions);
            SkewPlate.Apply(_one, SkewPlate.Kind.Light);
            SkewPlate.Apply(_ten, SkewPlate.Kind.Gold);
            // the price under the title with the drawn gem, not "· ◈900" run into the label
            // (ui_critique round 1, 09-Gacha #2)
            UiKit.Text("RECRUIT ×1", "pull-btn__en", _one).pickingMode = PickingMode.Ignore;
            UiKit.Text("RECRUIT ×10", "pull-btn__en", _ten).pickingMode = PickingMode.Ignore;
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
            // the price on a navy bar along the button's foot (the r5 gacha redesign)
            var row = UiKit.Div("pull-btn__cost pull-btn__cost--bar", b);
            row.pickingMode = PickingMode.Ignore;
            ModalFrame.Painted(row, (ctx, r) => UiPaint.Fill(ctx, UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.6f, 3f), UiPaint.C(28, 44, 76)));
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
            var results = GachaService.Buy(Game.Player, count, pickup: !_normal);
            if (results == null) return;       // buttons are disabled when broke; this is belt and braces
            QuestService.Note(Game.Player, "pull", count);
            Game.Touch();
            _app.StartCoroutine(RevealSequence(results));
        }

        /// <summary>
        /// The pull, as Blue Archive stages it (the user: tapping through ten cards one by one dragged):
        /// ONE tell for the batch in its best grade's colour, then the cards dealt face down and turned
        /// one after another. An A or S charges up in its colour, turns with a burst and stops the deal
        /// for its entrance (GachaFx.Intro). SKIP turns everything at once and goes to the result; a
        /// single pull is its entrance. Any result card opens the member's card.
        /// </summary>
        IEnumerator RevealSequence(List<PullResult> results)
        {
            var again = false;
            var overlay = _app.Overlay;
            overlay.Clear();
            overlay.RemoveFromClassList("hidden");
            _app.SetNavEnabled(false);
            AudioService.Play("pull");

            var skipAll = false; var tapped = false;
            Button SkipBtn(VisualElement host)
            {
                var b = UiKit.Btn("SKIP  ▶▶", "gx-skip", () => skipAll = true, host);
                SkewPlate.Apply(b, SkewPlate.Kind.Glass);
                return b;
            }

            // The tell runs first, once. A pull with no build-up is just a list of names appearing.
            var best = results.OrderByDescending(x => GameData.GradeRank(x.grade)).First();
            var tell = BuildTell(best);
            overlay.Add(tell);
            tell.RegisterCallback<ClickEvent>(_ => tapped = true);
            SkipBtn(tell);
            foreach (var step in PlayTell(tell, best, () => tapped || skipAll)) yield return step;
            yield return null;

            if (results.Count == 1)
            {
                var r0 = results[0];
                overlay.Clear();
                var intro = GachaFx.Intro(r0);
                overlay.Add(intro);
                AudioService.Play(AudioService.RevealId(r0.grade));
                if (r0.promoted) AudioService.Play("promote", 0.7f);
                var done0 = false;
                intro.RegisterCallback<ClickEvent>(_ => done0 = true);
                while (!done0) yield return null;
            }
            else
            {
                overlay.Clear();
                var cards = new List<GachaFx.Card>();
                var done = false; var dealing = true;
                VisualElement infoLayer = null;
                VisualElement view = null;
                view = BuildSummary(results, cards, false, () => done = true, () => { again = true; done = true; }, r =>
                {
                    if (dealing || infoLayer != null) return;
                    AudioService.Play("tap", 0.5f);
                    infoLayer = GachaFx.Info(r.hero.id, () => { infoLayer?.RemoveFromHierarchy(); infoLayer = null; });
                    view.Add(infoLayer);
                });
                overlay.Add(view);
                var foot = view.Q(className: "gx-foot");
                if (foot != null) foot.style.visibility = Visibility.Hidden;
                var skip = SkipBtn(view);
                yield return new WaitForSeconds(0.25f);
                foreach (var c in cards)
                {
                    if (skipAll) break;
                    if (GachaFx.Big(c.R.grade))
                    {
                        // it lights up and shivers in its colour, turns with a burst, then makes its entrance
                        var len = c.R.grade == "S" ? 0.55f : 0.38f;
                        GachaFx.Charge(c, len);
                        AudioService.Play("tap", 0.6f);
                        var w = 0f;
                        while (w < len && !skipAll) { w += Time.deltaTime; yield return null; }
                        c.Charging = false;
                        GachaFx.Turn(c, skipAll);
                        AudioService.Play(AudioService.RevealId(c.R.grade));
                        if (skipAll) break;
                        yield return new WaitForSeconds(0.25f);
                        var intro = GachaFx.Intro(c.R);
                        var go = false;
                        intro.RegisterCallback<ClickEvent>(_ => go = true);
                        view.Add(intro); skip.BringToFront();
                        if (c.R.promoted) AudioService.Play("promote", 0.7f);
                        while (!go && !skipAll) yield return null;
                        intro.RemoveFromHierarchy();
                        yield return null;
                    }
                    else
                    {
                        GachaFx.Turn(c);
                        AudioService.Play("tap", 0.3f + 0.05f * GameData.GradeRank(c.R.grade));
                        yield return new WaitForSeconds(0.1f);
                    }
                }
                foreach (var c in cards) { c.Charging = false; GachaFx.Turn(c, true); }
                skip.RemoveFromHierarchy();
                dealing = false;
                if (foot != null) foot.style.visibility = Visibility.Visible;
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

        /// <summary>
        /// A ten-pull result built from made-up pulls — one of each grade, two marked new — for
        /// the screenshot driver. Nothing is granted and nothing is saved.
        /// </summary>
        public static VisualElement Sample(System.Action onClose) => new GachaScreen(null).SampleSummary(onClose);

        static List<PullResult> SampleResults()
        {
            var results = new List<PullResult>();
            var grades = new[] { "S", "A", "B", "C", "D", "A", "B", "C", "D", "D" };
            for (var i = 0; i < grades.Length; i++)
            {
                var def = GameData.Heroes.Find(h => h.grade == grades[i] && !results.Exists(r => r.hero == h))
                          ?? GameData.Heroes.Find(h => h.grade == grades[i]);
                if (def == null) continue;
                results.Add(new PullResult { hero = def, grade = def.grade, isNew = i == 0 || i == 5, isPickup = i == 0, starAfter = def.grade == "S" ? 3 : def.grade == "A" ? 2 : 1 });
            }
            return results;
        }

        public VisualElement SampleSummary(System.Action onClose)
        {
            var cards = new List<GachaFx.Card>();
            VisualElement view = null;
            view = BuildSummary(SampleResults(), cards, true, onClose, null,
                r => view.Add(GachaFx.Info(r.hero.id, () => view.Q(className: "gx-info")?.RemoveFromHierarchy())));
            return view;
        }

        /// <summary>The sample S's entrance, for the screenshot driver.</summary>
        public static VisualElement SampleIntro() => GachaFx.Intro(SampleResults()[0]);

        /// <summary>A member's card over the result, for the screenshot driver.</summary>
        public static VisualElement SampleInfo(System.Action onClose)
        {
            var v = new GachaScreen(null).SampleSummary(onClose);
            v.Add(GachaFx.Info(SampleResults()[1].hero.id, onClose));
            return v;
        }

        VisualElement BuildSummary(List<PullResult> results, List<GachaFx.Card> cards, bool open, System.Action onClose, System.Action again, System.Action<PullResult> onCard)
        {
            // five across and two down on a lit stage (the user: one row of ten upright strips cut the
            // names and hid the art); each card a hard-leaning parallelogram, the grade at its top right
            var view = UiKit.Div("reveal gx-result");
            var stage = UiKit.Div("reveal-stage", view); stage.pickingMode = PickingMode.Ignore;
            ModalFrame.Painted(stage, (ctx, r) =>
            {
                UiPaint.Fill(ctx, UiPaint.RoundRect(r, 0f), UiPaint.Vertical(UiPaint.C(232, 242, 252), UiPaint.C(198, 222, 246), r.yMin, r.yMax));
                UiPaint.Fill(ctx, UiPaint.Ellipse(r.center + new Vector2(0f, -r.height * 0.05f), r.width * 0.42f, r.height * 0.36f), UiPaint.C(255, 255, 255, 0.55f), r.height * 0.3f);
                for (var i = 0; i < 6; i++)
                {
                    var x = r.xMin + r.width * (0.1f + i * 0.17f); var w = r.width * (i % 2 == 0 ? 0.05f : 0.025f);
                    UiPaint.Fill(ctx, new List<Vector2> { new(x, r.yMin), new(x + w, r.yMin), new(x + w - r.height * 0.45f, r.yMax), new(x - r.height * 0.45f, r.yMax) }, UiPaint.C(255, 255, 255, 0.16f), 0f);
                }
            });
            var rtitle = UiKit.Div("reveal-title", view); rtitle.pickingMode = PickingMode.Ignore;
            UiKit.Text("RECRUIT RESULT", "reveal-title__en", rtitle);
            UiKit.Text("모집 결과", "reveal-title__ko", rtitle);

            var grid = GachaFx.Grid(results, cards, open);
            view.Add(grid);
            foreach (var c in cards) { var cc = c; c.Root.RegisterCallback<ClickEvent>(_ => { if (cc.Open) onCard?.Invoke(cc.R); }); }
            UiKit.Text("카드를 누르면 사원 정보", "gx-result__hint", view).pickingMode = PickingMode.Ignore;

            var foot = UiKit.Div("reveal-foot gx-foot", view);
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
            UiKit.Text((Game.Player?.sparkPoints ?? 0).ToString("N0"), "reveal-points__value", points);
            return view;
        }
    }
}
