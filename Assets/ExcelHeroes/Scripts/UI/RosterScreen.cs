using System.Collections.Generic;
using System.Collections;
using System.Linq;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 인사 명단 — the collection. Every card in the game is listed; the ones the player has not
    /// pulled are dimmed silhouettes, because an empty slot is what sells the next pull.
    /// Tapping a card opens the detail view.
    /// </summary>
    public class RosterScreen : IScreen
    {
        public string Cell => "B2";
        public string Formula => "=IFERROR(VLOOKUP(A2,직원_목록!$A:$L,8,FALSE),\"\")";

        readonly AppRoot _app;
        VisualElement _root;
        Label _counter;
        ScrollView _scroll;

        public RosterScreen(AppRoot app) { _app = app; }

        // 편성 and 일괄 강화 used to hang off the ribbon, which went with the Excel chrome.
        // They live on the 편성 screen now, which is where a player looks for them anyway.

        public VisualElement Build()
        {
            _root = UiKit.Div("screen-body roster");

            // ▼ 필터 — the web build's filter bar, on one line. Fifty-five cards is a haystack: the
            // question a player asks is "who is my B-grade healer", and without this the only way
            // to answer it is to read every card. The 등급 row doubles as the grade sections the
            // list used to be cut into — each chip carries its own owned/total count, so picking
            // one IS opening that section.
            // ONE toolbar row, as the reference's student list has: the grade tabs on the left,
            // and 필터 · 회수 · the count on the right. Role and ownership used to be a second
            // row of chips, which is how a web page filters; a phone game puts them behind one
            // button and a modal, and gives the cards the height back.
            var bar = UiKit.Div("roster-bar", _root);
            _gradeRow = UiKit.Div("roster-bar__tabs", bar);
            UiKit.Div("spacer", bar);

            _filterBtn = UiKit.Btn("필터", "btn roster-bar__btn", OpenFilter, bar);

            // 레벨 회수 lives on the roster because that is where a player sees the low-grade card
            // they levelled at Phase 3 and now regrets. The refund is full.
            var reclaim = UiKit.Btn("레벨 회수", "btn roster-bar__btn", () =>
            {
                var (heroes, gold) = DismissService.ReclaimBench(Game.Player);
                _app.SetStatus(heroes > 0
                    ? $"대기 사원 {heroes}명 레벨 회수 · 골드 +{gold:N0} (전액 환급)"
                    : "회수할 레벨이 없습니다");
                if (heroes > 0) AudioService.Play("bond");
                Game.Touch();
            }, bar);
            reclaim.SetEnabled(Game.Player.owned.Any(o => DismissService.CanReclaim(Game.Player, o)));

            _counter = UiKit.Text("", "roster-bar__count", bar);

            // A fixed grid rather than a scroll. Held sideways there is room for fourteen cards at
            // a size a thumb can hit, and a page turn keeps a place the way a scroll position does
            // not — come back to 인사 and you are on the page you left, not at the top again.
            // A vertical scroll, as the reference's student list: pages of ten with a "< 1 / 6 >"
            // bar read as a web admin table (ui_critique round 9, 07-Roster #2). The scroll offset
            // is kept across refreshes, so coming back from a card lands where it was.
            _scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, verticalScrollerVisibility = ScrollerVisibility.Hidden };
            _scroll.AddToClassList("roster-scroll");
            _scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
            _root.Add(_scroll);
            _grid = UiKit.Div("roster-grid", _scroll);
            _pageLabel = UiKit.Text("", "roster-empty hidden", _root);

            Refresh();
            return _root;
        }

        /// <summary>
        /// Five across, two down. It was seven across at 13.4% each, which fit more faces on a
        /// page at a size where the name under them had to be abbreviated. Ten larger cards is
        /// the design spec's call and it is the right one for Korean job titles — "VLOOKUP
        /// 분석가" needs room that a seventh column takes away.
        ///
        /// 55 heroes over 10 a page is six pages, where it used to be four.
        /// </summary>
        const int PageSize = 10;

        string _grade = "", _role = "", _owned = "";
        int _page;
        VisualElement _grid, _gradeRow;
        Button _filterBtn;
        Label _pageLabel;

        void Turn(int by)
        {
            _page += by;
            AudioService.Play("nav", 0.4f);
            Refresh();
        }

        /// <summary>역할 and 보유, in a modal behind the 필터 button.</summary>
        void OpenFilter()
        {
            var body = UiKit.Modal("필터", _app.CloseOverlay, out var panel);
            BuildFilterRows(body);
            _app.OpenOverlay(panel);
        }

        void BuildFilterRows(VisualElement body)
        {
            body.Clear();
            var roles = UiKit.Div("filter-sheet__row", body);
            Chips(roles, "역할", new[] { ("", "전체"), ("tank", "탱커"), ("melee", "근접"), ("ranged", "원거리"), ("healer", "힐러") },
                  () => _role, v => { _role = v; BuildFilterRows(body); });
            var owned = UiKit.Div("filter-sheet__row", body);
            Chips(owned, "보유", new[] { ("", "전체"), ("1", "보유만"), ("0", "미보유"), ("party", "편성") },
                  () => _owned, v => { _owned = v; BuildFilterRows(body); });
            var acts = UiKit.Div("modal__acts", body);
            UiKit.Btn("초기화", "btn", () => { _role = ""; _owned = ""; _page = 0; Refresh(); _app.CloseOverlay(); }, acts);
            UiKit.Btn("확인", "btn btn--primary", () => { AudioService.Play("confirm", 0.5f); _app.CloseOverlay(); }, acts);
            Juice.PressAll(body);
        }

        /// <summary>One row of the filter bar. Chips rather than dropdowns: a select on a phone is
        /// two taps and a modal, and these are all short lists.</summary>
        void Chips(VisualElement parent, string label, (string Value, string Text)[] options,
                   System.Func<string> get, System.Action<string> set)
        {
            UiKit.Text(label, "filter-row__label", parent);
            foreach (var (value, text) in options)
            {
                var v = value;
                var chip = UiKit.Btn(text, "filter-chip", () => { _page = 0; set(v); Refresh(); }, parent);
                chip.EnableInClassList("filter-chip--on", get() == v);
            }
        }

        /// <summary>The 등급 chips, rebuilt each time because each one shows a live count.</summary>
        void BuildGradeChips(PlayerState p)
        {
            _gradeRow.Clear();
            Tab("", "전체");
            foreach (var grade in GameData.Grades.OrderByDescending(g => GameData.GradeRank(g.id)))
                if (GameData.Heroes.Any(h => h.grade == grade.id)) Tab(grade.id, grade.id);

            void Tab(string value, string text)
            {
                var v = value;
                var on = _grade == v;
                var tab = UiKit.Btn(text, "roster-tab", () => { _grade = v; _page = 0; AudioService.Play("tap", 0.5f); Refresh(); }, _gradeRow);
                SkewPlate.Apply(tab, on ? SkewPlate.Kind.Navy : SkewPlate.Kind.Light);
                Juice.Press(tab);
            }
        }

        /// <summary>How many of 역할 / 보유 are narrowing the list, for the 필터 button's label.</summary>
        int ActiveFilters => (_role != "" ? 1 : 0) + (_owned != "" ? 1 : 0);

        public void Refresh()
        {
            var p = Game.Player;

            var shown = GameData.Heroes.Where(h =>
                (_grade == "" || h.grade == _grade) &&
                (_role == "" || h.role == _role) &&
                (_owned switch
                {
                    "1" => p.Owns(h.id),
                    "0" => !p.Owns(h.id),
                    "party" => p.party.Contains(h.id),
                    _ => true,
                }))
                // Best grade first and owned before locked, so the cards worth looking at are on
                // the first page — the order the grade sections used to give for free.
                .OrderByDescending(h => GameData.GradeRank(h.grade))
                .ThenByDescending(h => p.Owns(h.id))
                .ThenBy(h => h.name)
                .ToList();

            BuildGradeChips(p);
            _counter.text = $"보유 {p.owned.Count}/{GameData.Heroes.Count}";
            UiKit.SetBtnText(_filterBtn, ActiveFilters > 0 ? $"필터 · {ActiveFilters}" : "필터");
            SkewPlate.Apply(_filterBtn, ActiveFilters > 0 ? SkewPlate.Kind.Primary : SkewPlate.Kind.Light);

            var offset = _scroll.scrollOffset;
            _grid.Clear();
            foreach (var def in shown)
                _grid.Add(UiKit.Card(def, p.Find(def.id), () => _app.OpenDetail(def.id)));
            // the last row padded so a short row keeps the grid's columns
            for (var i = shown.Count; i % 5 != 0; i++) UiKit.Div("card card--ghost", _grid);
            _scroll.schedule.Execute(() => _scroll.scrollOffset = offset);

            _pageLabel.text = "조건에 맞는 사원이 없습니다";
            _pageLabel.EnableInClassList("hidden", shown.Count > 0);
        }
    }

    /// <summary>
    /// The card detail sheet. The illustration cross-fades between the generated motion frames
    /// (breathe / blink / talk / smile) so a still image reads as a living character — the same
    /// trick the web build could not do, and the reason the art pipeline renders pose variants that
    /// line up pixel for pixel with the base image.
    /// </summary>
    public class HeroDetail
    {
        readonly AppRoot _app;

        public HeroDetail(AppRoot app) { _app = app; }

        public VisualElement Build(string heroId, System.Action onClose)
        {
            var def = GameData.Hero(heroId);
            var owned = Game.Player.Find(heroId);
            var grade = GameData.Grade(def.grade);
            var division = GameData.Division(def.division);

            // A page, as the reference's 학생 screen is, not a modal over the roster: the shell's
            // own backdrop behind it, the top bar still above it, the back arrow closing it.
            var view = UiKit.Div("detail");
            // the shell's illustrated scene, repeated here because the page sits over the roster
            // grid and must hide it (Chrome.SetScene; the painted sky was the fallback)
            Chrome.PaintScene(view);

            // The whole illustration, not a crop of it. The card is the thing the player pulled;
            // showing them the middle third of it in a box of a fixed height is the one place in
            // the game where cropping is simply wrong. The plate takes its height from the
            // picture's own aspect ratio, so nothing is cut and there are no bars either.
            var art = UiKit.Div("detail__art", view);
            // the halo sheet behind the figure, beside the head (BackSheet.Halo). The old standing
            // art had the sheet painted into the picture (tools/bake_sheet.py); v2 is clean, so
            // the game draws it — and draws it the same as everywhere else
            BackSheet.Add(art, def, owned, "backsheet detail__sheet");
            var figure = UiKit.Div("detail__figure", art);
            var standing = GameData.StandingArt(heroId);
            UiKit.SetArt(figure, standing ?? GameData.WornCardArt(heroId));
            figure.EnableInClassList("detail__figure--standing", standing != null);
            ArtMotion.Breathe(figure);

            // The name plate sits ON the picture, bottom-left, the way the reference does it.
            // It used to be a navy bar at the top of the RIGHT column, which spent a band of the
            // information side on something the illustration had room for, and left the portrait
            // captionless. Everything identifying the hero is here now: 부문, 등급, name, level,
            // ★ and the bond — so the right column is nothing but what you came to read.
            var plate = UiKit.Div("dplate", art);
            ModalFrame.Painted(plate, (ctx, r) =>
            {
                // a white slanted badge with a blue bar down its left (ui_critique 17-Detail #1: the navy
                // plate with gold stripes read as another game's)
                var band = Rect.MinMaxRect(r.xMin, r.yMin + 44f, r.xMax, r.yMax);
                var poly = UiPaint.SkewRect(band, SkewPlate.SlantFor(band.height) * 0.5f, 6f);
                UiPaint.Shadow(ctx, poly, new Vector2(0f, 5f), UiPaint.C(30, 80, 160, 0.2f), 12f);
                UiPaint.Fill(ctx, poly, UiPaint.C(255, 255, 255, 0.96f));
                UiPaint.Fill(ctx, UiPaint.Clip(poly, UiPaint.RoundRect(Rect.MinMaxRect(band.xMin - 40f, band.yMin, band.xMin + 34f, band.yMax), 0f)), UiPaint.C(24, 119, 242), 0f);
                UiPaint.Fill(ctx, UiPaint.Clip(new System.Collections.Generic.List<Vector2> { new(band.xMax, band.yMax), new(band.xMax - band.width * 0.28f, band.yMax), new(band.xMax, band.yMin) }, poly), UiPaint.C(226, 238, 250, 0.9f), 0.8f);
                UiPaint.Fill(ctx, UiPaint.Clip(poly, UiPaint.RoundRect(Rect.MinMaxRect(band.xMin - 40f, band.yMax - 4f, band.xMax + 40f, band.yMax), 0f)), UiPaint.C(64, 196, 240), 0f);
                var tag = UiPaint.SkewRect(Rect.MinMaxRect(r.xMin + 10f, r.yMin, r.xMin + r.width * 0.62f, r.yMin + 42f), 8f, 3f, 2);
                UiPaint.Fill(ctx, tag, UiPaint.C(30, 43, 69, 0.92f));
            });
            var plateTop = UiKit.Div("dplate__top", plate);
            UiKit.Text(def.dept ?? "", "dplate__dept", plateTop);
            var gradeChip = UiKit.Text(grade?.label ?? def.grade, "dplate__grade", plateTop);
            gradeChip.style.backgroundColor = grade?.Color ?? Color.gray;

            var nameRow = UiKit.Div("dplate__namerow", plate);
            UiKit.Text(def.name, "dplate__name", nameRow);
            if (owned != null) UiKit.Text("♥", "dplate__heart", nameRow);

            var metaRow = UiKit.Div("dplate__meta", plate);
            if (owned != null)
            {
                UiKit.Text($"Lv.{owned.level}", "dplate__lv", metaRow);
                var st = UiKit.Text(UiKit.Stars(owned.star), "dplate__stars", metaRow);
                st.style.color = UiPaint.C(255, 199, 0);   // gold, as target_2, whatever the grade
                var b0 = GameData.Balance;
                UiKit.Text($"호감도 {owned.affection}/{b0.affectionMax}", "dplate__bond", metaRow);
            }
            else UiKit.Text("미보유", "dplate__lv", metaRow);

            // Everything except the picture lives in the right column.
            var right = UiKit.Div("detail__right", view);
            ModalFrame.Painted(right, (ctx, r) =>
            {
                var body = UiPaint.RoundRect(Rect.MinMaxRect(r.xMin, r.yMin + 70f, r.xMax, r.yMax), 14f, 6);
                UiPaint.Shadow(ctx, body, new Vector2(0f, 6f), UiPaint.C(20, 40, 80, 0.22f), 16f);
                UiPaint.Fill(ctx, body, UiPaint.C(255, 255, 255, 0.95f));
            });

            // Pill tabs, as the reference puts them on its own card sheet. Everything this
            // modal knows about a hero — stats, levelling, the skill, the trait, the bond, the
            // record — is four screens of reading, and held sideways there is no scroll to put it
            // in. Three tabs is how it fits, and it is also how a player actually reads it: they
            // came here to level someone, or to check a skill, not to read all of it.
            var tabs = UiKit.Div("dtabs", right);
            Tab(tabs, "info", "정보", heroId, onClose);
            Tab(tabs, "power", "강화", heroId, onClose);
            Tab(tabs, "bond", "호감도", heroId, onClose);
            Tab(tabs, "equip", "비품", heroId, onClose);
            Tab(tabs, "skin", "스킨", heroId, onClose);
            if (heroId == GameData.MainId) Tab(tabs, "promo", "승진", heroId, onClose);

            var body = UiKit.Scroll("detail__body", right);

            if (owned != null && _tab == "info") BuildInfo(body, def, owned, heroId, grade, division);

            if (owned != null && _tab == "power")
            {
                // 강화 holds three things now — gold levels the hero, 강화 카드 buy the skill
                // and buy 각성 — so all three are one row apiece. Two full-width buttons for the
                // first of them used the whole sheet and pushed the other two off the bottom.
                BuildLevelUp(body, owned, heroId, onClose);

                BuildSkillLevel(body, owned, heroId, onClose);
                BuildAwaken(body, owned, heroId, onClose);
                BuildScout(body, owned, heroId, onClose);
            }
            if (owned == null)
                UiKit.Text("아직 모집하지 않은 사원입니다.", "muted", body);

            // 등급, 역할 and 부문 used to be three more rows here, on top of the six above them.
            // Nine rows do not fit a landscape sheet, so 역할 and 부문 were being cut off inside the
            // body — not off the screen, which is why the audit never said a word about it.
            //
            // They are dropped rather than paged, because all three are already on screen: the
            // grade is the badge on the card and the colour of the name, the role is the other
            // badge, and the 부문 is the line under the name in the header. An unowned card has no
            // stats to show, so it keeps them — it has the room.
            if (_tab == "info" && owned == null)
            {
                UiKit.StatRow("등급", $"{def.grade} · {grade?.label}", body);
                UiKit.StatRow("역할", UiKit.RoleName(def.role), body);
                UiKit.StatRow("부문", division?.name ?? def.division, body);
            }

            if (_tab == "power")
            {
            // An unowned card has no rows to spend on, so it gets the description instead.
            if (owned == null)
            {
                var skillPanel = UiKit.Div("panel", body);
                UiKit.Text($"스킬 · {def.skillName}", "section-title", skillPanel);
                var skillText = UiKit.Text(GameData.SkillText(def), null, skillPanel);
                skillText.style.whiteSpace = WhiteSpace.Normal;
            }

            var trait = GameData.Trait(def.trait);
            if (trait != null && owned == null)
            {
                var traitPanel = UiKit.Div("panel", body);
                UiKit.Text($"특성 · {trait.name}", "section-title", traitPanel);
                var td = UiKit.Text(trait.desc, null, traitPanel);
                td.style.whiteSpace = WhiteSpace.Normal;
            }
            }

            // 호감도 — the bond, and the text it buys. The locked rows stay visible on purpose:
            // knowing there IS a private message at Lv5 is what makes Lv4 worth reaching.
            if (owned != null && _tab == "bond")
            {
                var bond = UiKit.Div("panel", body);
                var b = GameData.Balance;
                UiKit.Text($"호감도 Lv{owned.affection} / {b.affectionMax}", "section-title", bond);

                var need = AffectionService.XpForNext(owned.affection);
                UiKit.Text(need > 0 ? $"다음까지 {owned.affectionXp} / {need}" : "최대 호감도", "muted", bond);

                var extra = GameData.Affection(heroId);
                if (AffectionService.SecretUnlocked(owned) && !string.IsNullOrEmpty(extra?.secret))
                {
                    var s = UiKit.Text($"사무실 비화 — {extra.secret}", null, bond);
                    s.style.whiteSpace = WhiteSpace.Normal;
                }
                else UiKit.Text($"사무실 비화 — Lv{b.affectionUnlockSecret} 해금", "muted", bond);

                if (AffectionService.LineUnlocked(owned) && !string.IsNullOrEmpty(extra?.line2))
                {
                    var l = UiKit.Text($"“{extra.line2}”", null, bond);
                    l.style.whiteSpace = WhiteSpace.Normal;
                    l.style.color = new Color(1f, 0.67f, 0.74f);
                }
                else UiKit.Text($"개인 메시지 — Lv{b.affectionUnlockLine} 해금", "muted", bond);

                if (!AffectionService.AtMax(owned))
                {
                    var cost = AffectionService.GiftCost(Game.Player);
                    var gift = UiKit.Btn($"간식 사주기 · ₩{cost:N0}", "btn", () =>
                    {
                        if (AffectionService.Gift(Game.Player, owned) >= 0)
                        {
                            AudioService.Play("bond");
                            Game.Touch(); Reopen(heroId, onClose);
                        }
                    }, bond);
                    gift.SetEnabled(Game.Player.gold >= cost);
                }

                var lead = UiKit.Btn(Game.Player.leadHeroId == heroId ? "대표 사원 ✓" : "대표 사원으로", "btn btn--ghost", () =>
                {
                    Game.Player.leadHeroId = Game.Player.leadHeroId == heroId ? "" : heroId;
                    Game.Touch();
                    Reopen(heroId, onClose);
                }, bond);
            }

            // 인사 기록 rides with 호감도 rather than 정보: the stat page is already seven rows
            // and a star line, and one more panel silently fell off the bottom of it.
            if (_tab == "bond")
            {
                var bioPanel = UiKit.Div("panel", body);
                UiKit.Text("인사 기록", "section-title", bioPanel);
                var bio = UiKit.Text(def.bio, null, bioPanel);
                bio.style.whiteSpace = WhiteSpace.Normal;
                if (!string.IsNullOrEmpty(def.ult))
                {
                    var ult = UiKit.Text($"“{def.ult}”", "muted", bioPanel);
                    ult.style.whiteSpace = WhiteSpace.Normal;
                }
            }

            if (owned != null && _tab == "equip") BuildEquip(body, heroId, onClose);

            if (owned != null && _tab == "skin") BuildSkins(body, heroId, onClose);
            if (heroId == GameData.MainId && _tab == "promo") BuildPromotion(body, onClose);

            // 편성 is the one action that belongs to the whole sheet rather than to a tab, so it
            // sits on its own row at the foot, where the reference keeps 확인.
            if (owned != null)
            {
                var foot = UiKit.Div("detail__foot", right);

                // 방출 lives on the 비품 page rather than beside 편성: it is the destructive one,
                // and putting it next to the button a player presses every session is asking for
                // the mis-tap that loses a card.
                if (_tab == "equip")
                {
                    var can = DismissService.Can(Game.Player, heroId);
                    var cards = DismissService.Cards(Game.Player, heroId);
                    var release = UiKit.Btn(
                        can ? $"방출 · 강화 카드 +{cards}" : DismissService.Blocked(Game.Player, heroId),
                        "btn detail__release", () => ConfirmRelease(heroId, onClose), foot);
                    release.SetEnabled(can);
                }
                // 즐겨찾기 and 편성 share one row: both answer "what should happen to this one",
                // and on a landscape sheet two full-width buttons stacked eat a third of the
                // panel to say what fits comfortably side by side. 방출 stays on its own line
                // above — its label is a whole sentence when it is blocked, and a sentence in a
                // third of a row is an ellipsis.
                var acts = UiKit.Div("detail__acts", foot);

                var fav = Game.Player.favorites.Contains(heroId);
                UiKit.Btn(fav ? "★ 즐겨찾기 해제" : "☆ 즐겨찾기", "btn", () =>
                {
                    if (fav) Game.Player.favorites.Remove(heroId);
                    else Game.Player.favorites.Add(heroId);
                    Game.Touch();
                    Reopen(heroId, onClose);
                }, acts);

                var inParty = Game.Player.party.Contains(heroId);
                UiKit.Btn(inParty ? "편성에서 빼기" : "편성에 넣기",
                    "btn btn--primary", () =>
                {
                    if (inParty) Game.Player.RemoveFromParty(heroId);
                    else Game.Player.AddToParty(heroId);
                    Game.Touch();
                    Close(onClose);
                }, acts);
            }

            UiKit.Btn(Icons.Close, "detail__close icon", () => Close(onClose), view);
            return view;
        }

        /// <summary>
        /// Asks before releasing. A collection game that lets a mis-tap delete a card someone
        /// waited a hundred pulls for has taken away the reason to keep playing.
        /// </summary>
        void ConfirmRelease(string heroId, System.Action onClose)
        {
            var p = Game.Player;
            var def = GameData.Hero(heroId);
            var cards = DismissService.Cards(p, heroId);
            var gold = DismissService.LevelRefund(p, heroId);

            var pane = UiKit.Div("onboard__card idle");
            UiKit.Text("사원 방출", "onboard__title", pane);
            UiKit.Text($"{def?.name}을(를) 방출하고 강화 카드 {cards}장" +
                       (gold > 0 ? $", 레벨 골드 ₩{gold:N0}을 돌려받습니다." : "을 받습니다."),
                "muted", pane);
            UiKit.Text("★, 레벨, 조각은 사라집니다. 착용 중인 비품은 창고로 돌아갑니다.", "muted", pane);

            var row = UiKit.Div("party-actions", pane);
            UiKit.Btn("취소", "btn", _app.CloseOverlay, row);
            UiKit.Btn("방출한다", "btn btn--danger", () =>
            {
                var (got, back) = DismissService.Release(Game.Player, heroId);
                if (got <= 0) { _app.CloseOverlay(); return; }
                AudioService.Play("tap", 0.6f);
                _app.SetStatus($"방출 · 강화 카드 +{got}" + (back > 0 ? $" · 골드 +{back:N0}" : ""));
                Game.Touch();
                _app.CloseOverlay();
                Close(onClose);
            }, row);

            _app.OpenOverlay(pane);
        }

        /// <summary>
        /// 비품 — four slots and what is in them. Each row is one slot: what is worn, what it
        /// gives, and the two things that can be done to it. The set bonus is printed at the top
        /// because filling all four is worth more than any one piece, and a player who cannot see
        /// that will never chase it.
        /// </summary>
        void BuildEquip(VisualElement body, string heroId, System.Action onClose)
        {
            var p = Game.Player;
            var bonus = EquipService.Stats(p, heroId);

            var head = UiKit.Div("prog-head", body);
            UiKit.Text(string.IsNullOrEmpty(bonus.SetName) ? "세트 없음" : bonus.SetName,
                "section-title", head);
            UiKit.Div("spacer", head);
            UiKit.Text($"공{bonus.Atk:0.#}% 체{bonus.Hp:0.#}% 스킬{bonus.Skill:0.#}% 속도{bonus.Speed:0.#}%",
                "muted", head);

            foreach (var slot in EquipService.Slots)
            {
                var row = UiKit.Div("arow", body);
                var text = UiKit.Div("arow__text", row);

                var worn = EquipService.Worn(p, heroId, slot.id);
                if (worn == null)
                {
                    UiKit.Text($"{slot.name} — 비어 있음", "arow__name", text);
                    UiKit.Text(slot.label, "arow__meta", text);
                }
                else
                {
                    var name = UiKit.Text(EquipService.Label(worn), "arow__name", text);
                    var grade = GameData.Grade(worn.grade);
                    if (grade != null) name.style.color = grade.Color;
                    UiKit.Text($"{slot.label} +{EquipService.Pct(worn):0.#}%　·　" +
                               (worn.lv >= GameData.Balance.equipMaxLevel
                                   ? "최대 강화"
                                   : $"강화 ₩{EquipService.UpgradeCost(p, worn):N0}"),
                        "arow__meta", text);
                }

                var slotId = slot.id;

                if (worn != null && worn.lv < GameData.Balance.equipMaxLevel)
                {
                    var cost = EquipService.UpgradeCost(p, worn);
                    var up = UiKit.Btn("강화", "arow__claim", () =>
                    {
                        if (!EquipService.Upgrade(Game.Player, worn.id)) return;
                        AudioService.Play("upgrade", 0.6f);
                        Game.Touch();
                        Reopen(heroId, onClose);
                    }, row);
                    up.SetEnabled(p.gold >= cost);
                }

                UiKit.Btn(worn == null ? "장착" : "교체", "arow__claim", () =>
                    OpenItemPicker(heroId, slotId, onClose), row);
            }
        }

        /// <summary>The bag, filtered to one slot. Anything on someone else's desk is not offered:
        /// an item is one object, and lending it out silently is how the numbers stop adding up.</summary>
        void OpenItemPicker(string heroId, string slotId, System.Action onClose)
        {
            var p = Game.Player;
            var slot = EquipService.Slot(slotId);
            var pane = UiKit.Div("picker ad-menu");

            var head = UiKit.Div("prog-head", pane);
            UiKit.Text($"{slot?.name} 선택", "section-title", head);
            UiKit.Div("spacer", head);
            UiKit.Text($"보관 {p.items.Count} / {GameData.Balance.equipInventoryMax}", "muted", head);

            var free = EquipService.Free(p, slotId);
            if (free.Count == 0) UiKit.Text("보관 중인 비품이 없습니다.", "muted", pane);

            foreach (var it in free.Take(5))
            {
                var row = UiKit.Div("arow", pane);
                var text = UiKit.Div("arow__text", row);
                var name = UiKit.Text(EquipService.Label(it), "arow__name", text);
                var g = GameData.Grade(it.grade);
                if (g != null) name.style.color = g.Color;
                UiKit.Text($"{slot?.label} +{EquipService.Pct(it):0.#}%　·　분해 ₩{EquipService.DismantleGold(p, it):N0}",
                    "arow__meta", text);

                var id = it.id;
                UiKit.Btn("장착", "arow__claim", () =>
                {
                    EquipService.Equip(Game.Player, heroId, id);
                    AudioService.Play("tap", 0.6f);
                    Game.Touch();
                    _app.CloseOverlay();
                    Reopen(heroId, onClose);
                }, row);

                UiKit.Btn("분해", "arow__claim arow__claim--ghost", () =>
                {
                    var gold = EquipService.Dismantle(Game.Player, id);
                    if (gold <= 0) return;
                    AudioService.Play("tap", 0.5f);
                    _app.SetStatus($"비품 분해 · 골드 +{gold:N0}");
                    Game.Touch();
                    _app.CloseOverlay();
                    OpenItemPicker(heroId, slotId, onClose);
                }, row);
            }

            if (free.Count > 5) UiKit.Text($"외 {free.Count - 5}개", "muted", pane);

            var current = EquipService.Worn(p, heroId, slotId);
            if (current != null)
                UiKit.Btn("해제", "btn btn--ghost", () =>
                {
                    EquipService.Unequip(Game.Player, heroId, slotId);
                    Game.Touch();
                    _app.CloseOverlay();
                    Reopen(heroId, onClose);
                }, pane);

            UiKit.Btn("닫기", "btn", _app.CloseOverlay, pane);
            _app.OpenOverlay(pane);
        }

        /// <summary>Which of the three pages is open. Kept across a reopen so levelling a hero
        /// does not throw you back to the stat page you levelled them from.</summary>
        string _tab = "info";

        /// <summary>Opens the sheet on a named tab. The screenshot driver uses it to reach the
        /// panes a default open never shows.</summary>
        public void ShowTab(string tab) => _tab = tab;

        /// <summary>
        /// 스카우트 and 조각 변환 — the two ends of the duplicate economy, on the page that already
        /// shows the copy count, because both are answers to the number on that line.
        ///
        /// 스카우트 is the only thing in the game that turns gold into ★. Without it a player
        /// sitting on millions with a card one copy short has nothing to spend them on.
        /// </summary>
        void BuildScout(VisualElement body, OwnedHero owned, string heroId, System.Action onClose)
        {
            var p = Game.Player;
            if (owned.id == GameData.MainId) return;    // 주인공은 조각을 쓰지 않습니다

            var spare = ScoutService.Spare(owned);
            if (spare > 0)
            {
                var conv = Row(body, $"남는 중복 {spare}장",
                    $"강화 카드 {spare * ScoutService.CardValue(owned):N0}장으로 바꿉니다");
                UiKit.Btn("변환", "skin-row__btn", () =>
                {
                    var got = ScoutService.Convert(p, owned);
                    if (got <= 0) return;
                    AudioService.Play("bond");
                    _app.SetStatus($"조각 변환 · 강화 카드 +{got:N0}");
                    Game.Touch();
                    Reopen(heroId, onClose);
                }, conv);
            }

            if (owned.star >= GameData.Balance.maxStar) return;

            var cost = ScoutService.Cost(p, owned);
            var why = ScoutService.Blocked(p, owned);
            var reachable = ScoutService.PriceIsReachable(owned);
            var row = Row(body, $"스카우트 · 오늘 {ScoutService.Left(p)} / {GameData.Balance.scoutPerDay}회",
                why.Length > 0 ? why : $"중복 1장 · ₩{cost:N0}");

            // A clamped price is not a price. Showing ₩2,147,483,647 would read as a number the
            // player could save towards, and it is not one.
            var go = UiKit.Btn(reachable ? $"₩{cost:N0}" : "—", "skin-row__btn skin-row__btn--buy", () =>
            {
                if (!ScoutService.Scout(p, owned)) return;
                AudioService.Play("bond");
                _app.SetStatus($"스카우트 · {GameData.Hero(heroId)?.name} 중복 +1");
                Game.Touch();
                Reopen(heroId, onClose);
            }, row);
            go.SetEnabled(ScoutService.Can(p, owned));
        }

        /// <summary>
        /// 강화 — the gold sink. ★ raises the ceiling, gold walks the hero up to it.
        /// </summary>
        void BuildLevelUp(VisualElement body, OwnedHero owned, string heroId, System.Action onClose)
        {
            var p = Game.Player;
            var cap = StatMath.LevelCap(owned);

            if (StatMath.AtLevelCap(owned))
            {
                // At ★5 there is no promotion left to sell, and 각성 is what opens the ceiling
                // instead. Saying "승급하면" there would be pointing at a door that is gone.
                var maxStar = owned.star >= GameData.Balance.maxStar;
                Row(body, $"레벨 {owned.level} / {cap}",
                    maxStar && !owned.awakened
                        ? $"상한 도달 — 각성하면 +{GameData.Balance.awakenLevelCap}"
                        : maxStar ? "상한 도달"
                        : "상한 도달 — 승급하면 올라갑니다");
                return;
            }

            var cost = StatMath.LevelUpCost(owned);
            var row = Row(body, $"레벨 {owned.level} / {cap}", $"다음 ₩{cost:N0} · 보유 ₩{p.gold:N0}", "levelup");
            CostSlot(row, "gold", p.gold, cost);

            var one = UiKit.Btn("+1", "skin-row__btn skin-row__btn--narrow", () =>
            {
                if (!StatMath.TryLevelUp(p, owned)) return;
                QuestService.Note(p, "upgrades");
                QuestService.Note(p, "enhance");
                Game.Touch(); Reopen(heroId, onClose);
            }, row);
            one.SetEnabled(p.gold >= cost);
            SkewPlate.Apply(one, SkewPlate.Kind.Light);

            var max = UiKit.Btn("골드 소진", "skin-row__btn skin-row__btn--buy", () =>
            {
                var levels = StatMath.LevelUpMax(p, owned);
                if (levels <= 0) return;
                QuestService.Note(p, "upgrades", levels);
                QuestService.Note(p, "enhance", levels);
                Game.Touch(); Reopen(heroId, onClose);
            }, row);
            max.SetEnabled(p.gold >= cost);
            SkewPlate.Apply(max, SkewPlate.Kind.Primary);
        }

        /// <summary>
        /// What a button will spend, before it: the currency's drawn icon in a small square, and
        /// have / need under it — red when short (ui_critique round 9, 17-Enhance #1).
        /// </summary>
        static void CostSlot(VisualElement row, string icon, long have, long need)
        {
            var slot = UiKit.Div("cost-slot", row);
            var art = GameData.Icon(icon);
            var box = UiKit.Div("cost-slot__box", slot);
            if (art != null) UiKit.SetArt(UiKit.Div("cost-slot__icon", box), art);
            var n = UiKit.Text($"{Short(have)} / {Short(need)}", "cost-slot__n", slot);
            n.EnableInClassList("cost-slot__n--short", have < need);
        }

        static string Short(long v) => v >= 1_000_000 ? $"{v / 1_000_000f:0.#}M" : v >= 10_000 ? $"{v / 1000f:0.#}K" : v.ToString("N0");

        /// <summary>
        /// 스킬 레벨 — the 강화 카드 sink that scales with use rather than rarity: +10% power and
        /// -3% charge time per level, five levels deep.
        ///
        /// It is shown even before ★2 unlocks the skill, saying so, because a card's skill is most
        /// of the reason to take it to ★2 and a blank space says nothing about that.
        /// </summary>
        void BuildSkillLevel(VisualElement body, OwnedHero owned, string heroId, System.Action onClose)
        {
            var p = Game.Player;
            var b = GameData.Balance;
            var max = owned.skillLv >= b.skillLevelMax;
            var locked = !StatMath.SkillUnlocked(owned);

            var def = GameData.Hero(owned.id);
            var row = Row(body, $"{def?.skillName} · Lv {owned.skillLv} / {b.skillLevelMax}",
                locked ? $"★{b.skillUnlockStar}에서 열립니다 · {GameData.SkillText(def)}"
                : max ? $"최대 레벨 · {GameData.SkillText(def)}"
                : $"위력 +{owned.skillLv * b.skillPowerPerLevel:P0} · 충전 -{owned.skillLv * b.skillCooldownPerLevel:P0}", "card");

            if (locked || max) return;

            var cost = StatMath.SkillUpCost(owned);
            CostSlot(row, "card", p.cards, cost);
            var up = UiKit.Btn($"카드 {cost:N0}", "skin-row__btn", () =>
            {
                if (!StatMath.UpgradeSkill(owned, p)) return;
                AudioService.Play("bond");
                Game.Touch();
                Reopen(heroId, onClose);
            }, row);
            up.SetEnabled(StatMath.CanUpgradeSkill(owned, p));
            SkewPlate.Apply(up, SkewPlate.Kind.Primary);
        }

        /// <summary>
        /// One line of "here is the thing, here is its state", with room for a button on the right.
        /// The same shape 스킨 and 승진 use — three stacked panels ran 496px off the bottom of a
        /// landscape sheet, and this tab now holds three things instead of one.
        /// </summary>
        VisualElement Row(VisualElement body, string name, string desc, string icon = null)
        {
            var row = UiKit.Div("skin-row", body);
            // the row's own drawn icon at its left end, as the reference's growth panels have
            // (ui_critique round 9, 17-Enhance #1)
            var art = icon != null ? GameData.Icon(icon) : null;
            if (art != null) UiKit.SetArt(UiKit.Div("skin-row__icon", row), art);
            var text = UiKit.Div("skin-row__text", row);
            UiKit.Text(name, "skin-row__name", text);
            UiKit.Text(desc, "skin-row__desc", text);
            return row;
        }

        /// <summary>
        /// 각성 — where a finished card goes when there is nothing left to buy for it. ★5 only,
        /// costs 강화 카드 once, and cannot be undone: +25% ATK and HP, trait x1.5, skill x1.25,
        /// and the level ceiling opens by another 50.
        /// </summary>
        void BuildAwaken(VisualElement body, OwnedHero owned, string heroId, System.Action onClose)
        {
            var p = Game.Player;
            var b = GameData.Balance;
            if (owned.star < b.awakenStar && !owned.awakened) return;   // nothing useful to say yet

            if (owned.awakened)
            {
                Row(body, "각성 완료",
                    $"공격·체력 +{b.awakenAtk:P0} · 특성 x{b.awakenTrait} · 스킬 x{b.awakenSkill}")
                    .AddToClassList("skin-row--on");
                return;
            }

            var cost = StatMath.AwakenCost(owned);
            var row = Row(body, "각성",
                $"+{b.awakenAtk:P0} · 특성 x{b.awakenTrait} · 상한 +{b.awakenLevelCap} · 되돌릴 수 없음", "awaken");
            CostSlot(row, "card", p.cards, cost);
            var go = UiKit.Btn($"카드 {cost:N0}", "skin-row__btn skin-row__btn--buy", () =>
            {
                if (!StatMath.Awaken(owned, p)) return;
                AudioService.Play("victory", 0.7f);
                _app.SetStatus($"{GameData.Hero(heroId)?.name} 각성");
                Game.Touch();
                Reopen(heroId, onClose);
            }, row);
            go.SetEnabled(StatMath.CanAwaken(owned, p));
            SkewPlate.Apply(go, SkewPlate.Kind.Gold);
        }

        /// <summary>
        /// 승진 — the main hero's career, and the one fork in this game that cannot be undone.
        ///
        /// Three conditions are listed whether they are met or not, because the panel's job before
        /// a promotion is possible is to say what to go and do. Right after 사원 the three tracks
        /// appear as three buttons with what each one is for written under it, and the confirm says
        /// plainly that the other two close for good.
        /// </summary>
        void BuildPromotion(VisualElement body, System.Action onClose)
        {
            var p = Game.Player;
            var job = PromotionService.Job(p);
            var info = PromotionService.For(p);

            if (info.Maxed)
            {
                UiKit.Text("더 오를 자리가 없습니다. 트랙을 끝까지 걸었습니다.", "muted", body);
                return;
            }

            var me = p.Find(GameData.MainId);
            var cond = UiKit.Div("panel", body);
            UiKit.Text($"{job?.title} → 승진 조건", "section-title", cond);
            Condition(cond, $"강화 카드 {info.Cards:N0}", $"{p.cards:N0} / {info.Cards:N0}", info.HasCards);
            Condition(cond, $"최고 클리어 {info.Stage}", $"{p.maxCleared} / {info.Stage}", info.HasStage);
            Condition(cond, $"레벨 {info.Level}", $"{me?.level ?? 0} / {info.Level}", info.HasLevel);

            // The fork. One option is a promotion; three is a decision, and it says so.
            if (info.Options.Count > 1)
                UiKit.Text("한 번 고르면 되돌릴 수 없습니다.", "muted", body);

            foreach (var next in info.Options)
            {
                var row = UiKit.Div("promo-row", body);
                var text = UiKit.Div("promo-row__text", row);
                UiKit.Text($"{next.title}　({next.grade}급)", "promo-row__name", text);
                UiKit.Text(PromotionService.TrackDesc(next.track), "promo-row__desc", text);

                var target = next;
                var go = UiKit.Btn(info.Ok ? "승진" : "조건 미달",
                    info.Ok ? "promo-row__btn promo-row__btn--go" : "promo-row__btn",
                    () => ConfirmPromotion(target, info, onClose), row);
                go.SetEnabled(info.Ok);
            }
        }

        /// <summary>One requirement line: what it is, where the player is, and whether it is met.</summary>
        void Condition(VisualElement parent, string what, string progress, bool met)
        {
            var row = UiKit.Div("promo-cond" + (met ? " promo-cond--met" : ""), parent);
            UiKit.Text(met ? "✓" : "·", "promo-cond__mark", row);
            UiKit.Text(what, "promo-cond__what", row);
            UiKit.Text(progress, "promo-cond__num", row);
        }

        /// <summary>
        /// The confirm. It exists because the track choice is permanent — the other two tracks are
        /// gone for this save, not merely later — and a mis-tap on a button labelled 승진 would
        /// otherwise decide the rest of the run.
        /// </summary>
        void ConfirmPromotion(MainJobDef target, PromotionService.Info info, System.Action onClose)
        {
            var pane = UiKit.Div("onboard__card idle");
            UiKit.Text($"{target.title}(으)로 승진", "onboard__title", pane);
            UiKit.Text($"강화 카드 {info.Cards:N0}장을 사용합니다.", "muted", pane);
            if (info.Options.Count > 1)
                UiKit.Text($"{PromotionService.TrackName(target.track)}을(를) 선택합니다. " +
                           "나머지 트랙은 이 저장 파일에서 다시 고를 수 없습니다.", "muted", pane);

            var row = UiKit.Div("party-actions", pane);
            UiKit.Btn("취소", "btn", () => _app.CloseOverlay(), row);
            UiKit.Btn("승진", "btn btn--primary", () =>
            {
                if (!PromotionService.Promote(Game.Player, target.id)) { _app.CloseOverlay(); return; }
                AudioService.Play("victory", 0.7f);
                _app.SetStatus($"김인턴이 {target.title}(으)로 승진했습니다");
                Game.Touch();
                _app.CloseOverlay();
            }, row);

            _app.OpenOverlay(pane);
        }

        /// <summary>
        /// 스킨 — the two alternate looks, and the row that puts one on.
        ///
        /// The two unlock in ways that cannot substitute for each other, and the rows say which:
        /// 퇴근 사복 is the reward at the top of 호감도 and cannot be bought, 회사 정장 is bought
        /// with gems and cannot be earned. A locked row stays visible and states its price or its
        /// requirement, because a skin nobody can see is a skin nobody levels towards.
        /// </summary>
        void BuildSkins(VisualElement body, string heroId, System.Action onClose)
        {
            var p = Game.Player;
            var skins = SkinService.For(heroId);
            if (skins.Count == 0)
            {
                UiKit.Text("이 사원에게는 스킨이 없습니다.", "muted", body);
                return;
            }

            // 기본 — taking a skin OFF needs a row of its own, or the only way back to the hero's
            // own look is to not have unlocked anything.
            var active = SkinService.Active(p, heroId);
            var baseRow = UiKit.Div("skin-row" + (active.Length == 0 ? " skin-row--on" : ""), body);
            var baseText = UiKit.Div("skin-row__text", baseRow);
            UiKit.Text("기본", "skin-row__name", baseText);
            UiKit.Text("처음 그려진 모습", "skin-row__desc", baseText);
            if (active.Length == 0) UiKit.Text("착용 중", "skin-row__on", baseRow);
            else
                UiKit.Btn("착용", "skin-row__btn", () =>
                {
                    SkinService.Equip(p, heroId, null);
                    Game.Touch();
                    Reopen(heroId, onClose);
                }, baseRow);

            foreach (var sk in skins)
            {
                var owned = SkinService.Owns(p, heroId, sk.id);
                var on = owned && active == sk.id;

                var row = UiKit.Div("skin-row" + (on ? " skin-row--on" : "") + (owned ? "" : " skin-row--locked"), body);
                if (ColorUtility.TryParseHtmlString(sk.frame, out var frame)) row.style.borderLeftColor = frame;

                var text = UiKit.Div("skin-row__text", row);
                UiKit.Text(sk.name, "skin-row__name", text);
                UiKit.Text(sk.desc, "skin-row__desc", text);

                if (on) { UiKit.Text("착용 중", "skin-row__on", row); continue; }

                if (owned)
                {
                    UiKit.Btn("착용", "skin-row__btn", () =>
                    {
                        SkinService.Equip(p, heroId, sk.id);
                        Game.Touch();
                        Reopen(heroId, onClose);
                    }, row);
                    continue;
                }

                var why = SkinService.Blocked(p, heroId, sk);
                var can = why.Length == 0;
                var label = can
                    ? (sk.unlockGems > 0 ? $"◈{sk.unlockGems} 해금" : "해금")
                    : why;
                var btn = UiKit.Btn(label, can ? "skin-row__btn skin-row__btn--buy" : "skin-row__btn", () =>
                {
                    if (!SkinService.Unlock(p, heroId, sk.id)) return;
                    SkinService.Equip(p, heroId, sk.id);
                    AudioService.Play("bond");
                    _app.SetStatus($"{GameData.Hero(heroId)?.name} 스킨 「{sk.name}」 해금");
                    Game.Touch();
                    Reopen(heroId, onClose);
                }, row);
                btn.SetEnabled(can);
            }
        }

        /// <summary>
        /// 정보 — laid out the way the reference lays out a student's basic page, because a list
        /// of label/value rows is not the same screen even when it carries the same numbers.
        ///
        /// The reference groups: a captioned block of core stats in TWO columns, a row of small
        /// chips for the categorical facts, then the skill and the equipment as CARDS in a row
        /// with their levels on them. That shape fits a landscape screen — four stats take two
        /// lines instead of four, which is what buys the room for the skill and the kit to be on
        /// the same page rather than behind another tab.
        ///
        /// It was eight stacked rows here, and three of them (등급 · 역할 · 부문) had already been
        /// deleted for not fitting.
        /// </summary>
        void BuildInfo(VisualElement body, HeroDef def, OwnedHero owned, string heroId,
                       GradeDef grade, DivisionDef division)
        {
            // ---- 기본 능력치, two columns ------------------------------------------------
            // target_2: four stats in two columns, each a navy glyph beside a small caption and a
            // BIG number, straight on the glass, no captioned box around them
            var statGrid = UiKit.Div("bigstats", body);
            BigStat(statGrid, "\uf889", "공격력", StatMath.Atk(owned).ToString("N0"));             // swords
            BigStat(statGrid, "\ue87e", "체력", StatMath.Hp(owned).ToString("N0"));                // favorite
            BigStat(statGrid, "\uef55", "전투력", StatMath.Power(owned).ToString("N0"));           // local_fire_department
            BigStat(statGrid, "\ue8e5", "레벨", $"{owned.level}/{StatMath.LevelCap(owned)}");      // trending_up

            // ---- the categorical facts, as chips -----------------------------------------
            // 등급, 역할 and 부문 are back. They were dropped when this was a list because nine
            // rows did not fit; three chips on one line do.
            var chips = UiKit.Div("chiprow", body);
            // slanted tags, not pills (ui_critique 17-Detail): the grade in its colour, the rest pale
            SlantChip(chips, $"{def.grade} · {grade?.label}", grade?.Color ?? Color.gray, true);
            SlantChip(chips, UiKit.RoleName(def.role), UiPaint.C(226, 235, 245), false);
            SlantChip(chips, division?.name ?? def.division, UiPaint.C(226, 235, 245), false);
            var need = GachaService.PromoteCost(owned);
            SlantChip(chips, need > 0 ? $"승급 {owned.copies}/{need}" : "최대 ★", UiPaint.C(226, 235, 245), false);

            // ---- 스킬 and 특성, as cards with their levels --------------------------------
            var cards = UiKit.Div("minicards", body);

            var skillCard = UiKit.Div("minicard", cards);
            MiniIcon(skillCard, Icons.Bolt);
            UiKit.Text("스킬", "minicard__kind", skillCard);
            UiKit.Text(def.skillName ?? "—", "minicard__name", skillCard);
            UiKit.Text(StatMath.SkillUnlocked(owned)
                       ? $"Lv.{owned.skillLv + 1} / {GameData.Balance.skillLevelMax + 1}"
                       : "미해금", "minicard__lv", skillCard);

            var trait = GameData.Trait(def.trait);
            var traitCard = UiKit.Div("minicard", cards);
            MiniIcon(traitCard, "\ue7af");   // workspace_premium
            UiKit.Text("특성", "minicard__kind", traitCard);
            UiKit.Text(trait?.name ?? "—", "minicard__name", traitCard);
            UiKit.Text(owned.star > 0 ? $"★{owned.star}" : "", "minicard__lv", traitCard);

            // ---- 비품, the four slots as tiles -------------------------------------------
            // The reference puts the equipment on the basic page too, not only on its own. Four
            // empty sockets are the clearest possible statement of what this hero is missing.
            var kit = UiKit.Div("block block--bare", body);
            var strip = UiKit.Div("kitstrip", kit);
            foreach (var slot in EquipService.Slots)
            {
                var worn = EquipService.Worn(Game.Player, heroId, slot.id);
                // a socket, the way the reference shows gear: the part's own picture in a square frame
                // (faint and with a + when empty), the slot name beside it (ui_critique round 3, 16-Detail #2)
                var tile = UiKit.Div("kittile" + (worn == null ? " kittile--empty" : ""), strip);
                var sock = UiKit.Div("kittile__sock", tile);
                ModalFrame.Painted(sock, (ctx, r) => Chrome.DrawGlassTile(ctx, r, false, corner: true, edge: UiPaint.C(176, 200, 226)));   // target_2: square glass sockets
                var pic = GameData.Icon("eq_" + slot.id);
                if (pic != null) UiKit.SetArt(UiKit.Div("kittile__pic", sock), pic);
                if (worn == null) UiKit.Text("+", "kittile__plus", sock);
                var words = UiKit.Div("kittile__words", tile);
                UiKit.Text(slot.name, "kittile__slot", words);
                UiKit.Text(worn == null ? "비어 있음" : EquipService.Label(worn), "kittile__name", words);
                if (worn != null) UiKit.Text($"+{EquipService.Pct(worn) * 100f:0}%", "kittile__val", words);
            }
        }

        static void SlantChip(VisualElement parent, string text, Color fill, bool strong)
        {
            var chip = UiKit.Div("schip" + (strong ? " schip--strong" : ""), parent);
            ModalFrame.Painted(chip, (ctx, r) =>
            {
                var box = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.7f, 3f);
                UiPaint.Fill(ctx, box, fill);
                if (!strong) UiPaint.Stroke(ctx, box, UiPaint.C(196, 212, 230), 1f);
            });
            UiKit.Text(text, "schip__text", chip).pickingMode = PickingMode.Ignore;
        }

        static void BigStat(VisualElement parent, string glyph, string key, string value)
        {
            var cell = UiKit.Div("bigstat", parent);
            UiKit.Text(glyph, "icon bigstat__icon", cell);
            var words = UiKit.Div("bigstat__words", cell);
            UiKit.Text(key, "bigstat__key", words);
            UiKit.Text(value, "bigstat__val", words);
        }

        /// <summary>target_2's skill-card icon: a navy square, a cyan glyph, a thin cyan rim.</summary>
        static void MiniIcon(VisualElement card, string glyph)
        {
            card.AddToClassList("minicard--icon");
            var sq = UiKit.Div("minicard__icon", card);
            ModalFrame.Painted(sq, (ctx, r) =>
            {
                var poly = UiPaint.RoundRect(r, 8f);
                UiPaint.Fill(ctx, poly, UiPaint.C(120, 214, 250, 0.9f));
                UiPaint.Fill(ctx, UiPaint.Offset(poly, -2f), UiPaint.Vertical(UiPaint.C(44, 66, 112), UiPaint.C(22, 36, 70), r.yMin, r.yMax));
                UiPaint.Fill(ctx, UiPaint.Clip(new System.Collections.Generic.List<Vector2> { new(r.xMin, r.yMin), new(r.xMax, r.yMin), new(r.xMin, r.yMax) }, UiPaint.Offset(poly, -2f)), UiPaint.C(255, 255, 255, 0.08f), 0.8f);
            });
            UiKit.Text(glyph, "icon minicard__glyph", sq).pickingMode = PickingMode.Ignore;
        }

        void Tab(VisualElement parent, string id, string label, string heroId, System.Action onClose)
        {
            // The label is a child: a painted element covers its own text (see UiKit.Ribbon).
            var b = UiKit.Btn("", "dtab", () => { _tab = id; Reopen(heroId, onClose); }, parent);
            b.EnableInClassList("dtab--on", _tab == id);
            UiKit.Text(label, "dtab__label", b);
            // Folder tabs, as the reference's 학생 panel has: a rectangle with a slanted right
            // edge, the open one white and joined to the panel below it, the rest pale blue-grey.
            var on = _tab == id;
            ModalFrame.Painted(b, (ctx, r) =>
            {
                var slant = r.height * 0.35f;
                var poly = new System.Collections.Generic.List<Vector2>
                {
                    new Vector2(r.xMin, r.yMin), new Vector2(r.xMax - slant, r.yMin),
                    new Vector2(r.xMax, r.yMax + (on ? 2f : 0f)), new Vector2(r.xMin, r.yMax + (on ? 2f : 0f)),
                };
                // the open tab lit cyan with white type and a small pointer under it (target_2, and the
                // lobby's lit nav tile); the others pale glass — ui_critique round 9, 17-Enhance #3
                // flat folder tabs that sit ON the panel (ui_critique 17-Detail #1): the open one solid BA
                // blue with a cyan line along its foot, the others a flat pale grey with a hairline top
                var shape = UiPaint.Round(poly, 4f, 3);
                if (on)
                {
                    UiPaint.Fill(ctx, shape, UiPaint.C(40, 134, 229));
                    UiPaint.Fill(ctx, UiPaint.Clip(shape, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin - 20f, r.yMax - 4f, r.xMax + 20f, r.yMax + 3f), 0f)), UiPaint.C(0, 240, 255), 0f);
                }
                else
                {
                    UiPaint.Fill(ctx, shape, UiPaint.C(221, 229, 238));
                    UiPaint.Fill(ctx, UiPaint.Clip(shape, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin - 20f, r.yMin, r.xMax + 20f, r.yMin + 1.5f), 0f)), UiPaint.C(186, 202, 217), 0f);
                }
            });
            Juice.Press(b);
        }

        /// <summary>Rebuilds the sheet in place so levelling shows the new numbers immediately.</summary>
        void Reopen(string heroId, System.Action onClose) => _app.OpenDetail(heroId);

        void Close(System.Action onClose) => onClose?.Invoke();
    }
}
