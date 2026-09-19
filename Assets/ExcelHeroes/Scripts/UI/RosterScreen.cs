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
            var bar = UiKit.Div("filter-bar", _root);
            _gradeRow = UiKit.Div("filter-row", bar);

            var right = UiKit.Div("filter-row", bar);
            Chips(right, "역할", new[] { ("", "전체"), ("tank", "탱커"), ("melee", "근접"), ("ranged", "원거리"), ("healer", "힐러") },
                  () => _role, v => _role = v);
            Chips(right, "보유", new[] { ("", "전체"), ("1", "보유만"), ("0", "미보유"), ("party", "편성") },
                  () => _owned, v => _owned = v);
            UiKit.Div("spacer", right);
            _counter = UiKit.Text("", "filter-row__count", right);

            // A fixed grid rather than a scroll. Held sideways there is room for fourteen cards at
            // a size a thumb can hit, and a page turn keeps a place the way a scroll position does
            // not — come back to 인사 and you are on the page you left, not at the top again.
            _grid = UiKit.Div("roster-grid", _root);

            var pager = UiKit.Div("pager", _root);
            _prev = UiKit.Btn("◀", "pager__btn", () => Turn(-1), pager);
            _pageLabel = UiKit.Text("", "pager__label", pager);
            _next = UiKit.Btn("▶", "pager__btn", () => Turn(1), pager);

            Refresh();
            return _root;
        }

        /// <summary>Seven across, two down: what fits at a size a thumb can hit without a scroll.</summary>
        const int PageSize = 14;

        string _grade = "", _role = "", _owned = "";
        int _page;
        VisualElement _grid, _gradeRow;
        Button _prev, _next;
        Label _pageLabel;

        void Turn(int by)
        {
            _page += by;
            AudioService.Play("nav", 0.4f);
            Refresh();
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
                var chip = UiKit.Btn(text, "filter-chip", () => { set(v); _page = 0; Refresh(); }, parent);
                chip.EnableInClassList("filter-chip--on", get() == v);
            }
        }

        /// <summary>The 등급 chips, rebuilt each time because each one shows a live count.</summary>
        void BuildGradeChips(PlayerState p)
        {
            _gradeRow.Clear();
            UiKit.Text("등급", "filter-row__label", _gradeRow);

            Chip("", "전체", GameData.Heroes.Count, GameData.Heroes.Count(h => p.Owns(h.id)));
            foreach (var grade in GameData.Grades.OrderByDescending(g => GameData.GradeRank(g.id)))
            {
                var inGrade = GameData.Heroes.Where(h => h.grade == grade.id).ToList();
                if (inGrade.Count == 0) continue;
                var chip = Chip(grade.id, grade.id, inGrade.Count, inGrade.Count(h => p.Owns(h.id)));
                // The grade's own colour, so the row reads as the grade ladder it is.
                if (_grade == grade.id) chip.style.backgroundColor = grade.Color;
            }

            Button Chip(string value, string text, int total, int have)
            {
                var v = value;
                var chip = UiKit.Btn($"{text} {have}/{total}", "filter-chip",
                    () => { _grade = v; _page = 0; Refresh(); }, _gradeRow);
                chip.EnableInClassList("filter-chip--on", _grade == v);
                return chip;
            }
        }

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
            _counter.text = $"{p.owned.Count}/{GameData.Heroes.Count}";

            var pages = Mathf.Max(1, Mathf.CeilToInt(shown.Count / (float)PageSize));
            _page = Mathf.Clamp(_page, 0, pages - 1);

            _grid.Clear();
            foreach (var def in shown.Skip(_page * PageSize).Take(PageSize))
                _grid.Add(UiKit.Card(def, p.Find(def.id), () => _app.OpenDetail(def.id)));

            // The last page is padded so four cards do not stretch across a row built for seven.
            for (var i = shown.Count - _page * PageSize; i < PageSize; i++)
                UiKit.Div("card card--ghost", _grid);

            _pageLabel.text = shown.Count == 0 ? "조건에 맞는 사원이 없습니다" : $"{_page + 1} / {pages}";
            _prev.SetEnabled(_page > 0);
            _next.SetEnabled(_page < pages - 1);
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

            var view = UiKit.Div("detail");

            // The whole illustration, not a crop of it. The card is the thing the player pulled;
            // showing them the middle third of it in a box of a fixed height is the one place in
            // the game where cropping is simply wrong. The plate takes its height from the
            // picture's own aspect ratio, so nothing is cut and there are no bars either.
            var art = UiKit.Div("detail__art", view);
            UiKit.SetArt(art, GameData.CardArt(heroId));
            ArtMotion.Breathe(art);

            // Everything except the picture lives in the right column.
            var right = UiKit.Div("detail__right", view);
            var head = UiKit.Div("detail__head", right);
            var name = UiKit.Text(def.name, "detail__name", head);
            name.style.color = grade?.Color ?? Color.white;
            UiKit.Text($"{def.nick} · {def.dept}", "detail__nick", head);

            // Pill tabs, as the reference puts them on its own card sheet. Everything this
            // modal knows about a hero — stats, levelling, the skill, the trait, the bond, the
            // record — is four screens of reading, and held sideways there is no scroll to put it
            // in. Three tabs is how it fits, and it is also how a player actually reads it: they
            // came here to level someone, or to check a skill, not to read all of it.
            var tabs = UiKit.Div("dtabs", right);
            Tab(tabs, "info", "정보", heroId, onClose);
            Tab(tabs, "power", "강화", heroId, onClose);
            Tab(tabs, "bond", "호감도", heroId, onClose);

            var body = UiKit.Div("detail__body", right);

            if (owned != null && _tab == "info")
            {
                UiKit.Text(UiKit.Stars(owned.star), "reveal__grade", body);
                UiKit.StatRow("공격력", StatMath.Atk(owned).ToString("N0"), body);
                UiKit.StatRow("체력", StatMath.Hp(owned).ToString("N0"), body);
                UiKit.StatRow("전투력", StatMath.Power(owned).ToString("N0"), body);
                UiKit.StatRow("레벨", $"{owned.level} / {StatMath.LevelCap(owned.star)}", body);
                var need = GachaService.PromoteCost(owned);
                UiKit.StatRow("승급", need > 0 ? $"중복 {owned.copies} / {need}장" : "최대 ★", body);
            }

            if (owned != null && _tab == "power")
            {
                // 강화 — the gold sink. ★ raises the ceiling, gold walks the hero up to it.
                var levelPanel = UiKit.Div("panel", body);
                UiKit.Text("강화", "section-title", levelPanel);
                if (StatMath.AtLevelCap(owned))
                {
                    UiKit.Text($"★{owned.star} 레벨 상한 도달 — 승급하면 상한이 올라갑니다.", "muted", levelPanel);
                }
                else
                {
                    var cost = StatMath.LevelUpCost(owned);
                    UiKit.Text($"다음 레벨 ₩{cost:N0} · 보유 ₩{Game.Player.gold:N0}", "muted", levelPanel);

                    var one = UiKit.Btn($"레벨 +1 · ₩{cost:N0}", "btn", () =>
                    {
                        if (StatMath.TryLevelUp(Game.Player, owned))
                        {
                            QuestService.Note(Game.Player, "upgrades");
                            QuestService.Note(Game.Player, "enhance");
                            Game.Touch(); Reopen(heroId, onClose);
                        }
                    }, levelPanel);
                    one.SetEnabled(Game.Player.gold >= cost);

                    var max = UiKit.Btn("골드 소진까지 강화", "btn btn--primary", () =>
                    {
                        var levels = StatMath.LevelUpMax(Game.Player, owned);
                        if (levels > 0)
                        {
                            QuestService.Note(Game.Player, "upgrades", levels);
                            QuestService.Note(Game.Player, "enhance", levels);
                            Game.Touch(); Reopen(heroId, onClose);
                        }
                    }, levelPanel);
                    max.SetEnabled(Game.Player.gold >= cost);
                }
            }
            if (owned == null)
                UiKit.Text("아직 모집하지 않은 사원입니다.", "muted", body);

            if (_tab == "info")
            {
                UiKit.StatRow("등급", $"{def.grade} · {grade?.label}", body);
                UiKit.StatRow("역할", UiKit.RoleName(def.role), body);
                UiKit.StatRow("부문", division?.name ?? def.division, body);
            }

            if (_tab == "power")
            {
            var skillPanel = UiKit.Div("panel", body);
            UiKit.Text($"스킬 · {def.skillName}", "section-title", skillPanel);
            var skillText = UiKit.Text(GameData.SkillText(def), null, skillPanel);
            skillText.style.whiteSpace = WhiteSpace.Normal;
            if (owned != null && !StatMath.SkillUnlocked(owned))
                UiKit.Text($"★{GameData.Balance.skillUnlockStar} 부터 사용할 수 있습니다.", "muted", skillPanel);

            var trait = GameData.Trait(def.trait);
            if (trait != null)
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

            // 편성 is the one action that belongs to the whole sheet rather than to a tab, so it
            // sits on its own row at the foot, where the reference keeps 확인.
            if (owned != null)
            {
                var foot = UiKit.Div("detail__foot", right);
                var inParty = Game.Player.party.Contains(heroId);
                UiKit.Btn(inParty ? "편성에서 빼기" : "편성에 넣기",
                    inParty ? "btn" : "btn btn--primary", () =>
                {
                    if (inParty) Game.Player.RemoveFromParty(heroId);
                    else Game.Player.AddToParty(heroId);
                    Game.Touch();
                    Close(onClose);
                }, foot);
            }

            UiKit.Btn(Icons.Close, "detail__close icon", () => Close(onClose), view);
            return view;
        }

        /// <summary>Which of the three pages is open. Kept across a reopen so levelling a hero
        /// does not throw you back to the stat page you levelled them from.</summary>
        string _tab = "info";

        void Tab(VisualElement parent, string id, string label, string heroId, System.Action onClose)
        {
            var b = UiKit.Btn(label, "dtab", () => { _tab = id; Reopen(heroId, onClose); }, parent);
            b.EnableInClassList("dtab--on", _tab == id);
        }

        /// <summary>Rebuilds the sheet in place so levelling shows the new numbers immediately.</summary>
        void Reopen(string heroId, System.Action onClose) => _app.OpenDetail(heroId);

        void Close(System.Action onClose) => onClose?.Invoke();
    }
}
