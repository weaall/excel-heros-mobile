using System.Linq;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 편성 — the five slots taken into battle, and the 부문 시너지 they unlock.
    /// The synergy readout is deliberately loud: it is the thing that makes a "worse" card worth
    /// fielding, and the reason collecting breadth matters as much as chasing S ranks.
    /// </summary>
    public class PartyScreen : IScreen
    {
        public string Cell => "F2";
        public string Formula => "=선택 영역 요약";

        readonly AppRoot _app;
        VisualElement _root;

        public PartyScreen(AppRoot app) { _app = app; }

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

            // Two columns, as the reference's 부대 편성 is: the line-up on the left at a size where
            // you can read a face, everything the line-up adds up to on the right.
            var cols = UiKit.Div("party-cols", _root);
            var left = UiKit.Div("party-cols__left", cols);
            var right = UiKit.Div("party-cols__right", cols);

            var slots = UiKit.Div("party-slots", left);
            for (var i = 0; i < p.party.Count; i++)
            {
                var id = p.party[i];
                if (string.IsNullOrEmpty(id))
                {
                    var empty = UiKit.Div("slot slot--empty", slots);
                    UiKit.Text("+", "slot__plus", empty);
                    UiKit.Text("비어 있음", "slot__hint", empty);
                    empty.RegisterCallback<ClickEvent>(_ => OpenPicker());
                    continue;
                }

                var def = GameData.Hero(id);
                var owned = p.Find(id);
                if (def == null || owned == null) continue;
                slots.Add(BuildSlot(def, owned, id));
            }

            // The action rail, on the far right, stacked — which is where the reference keeps
            // 편성 · 대표 · 지원. They used to sit in a row UNDER the line-up, where four of them
            // did not fit on one line and wrapped, so the last one hung below the others with
            // nothing beside it. A rail has no line to run out of.
            var actions = UiKit.Div("party-rail", cols);

            // 자동 편성 goes first because it is the one most players will press. Choosing by hand
            // means opening 55 cards, and the best party is not the five biggest numbers — 부문
            // 시너지, a healer and a spread of roles all beat raw 전투력, and none of them shows on
            // a card by itself.
            var auto = UiKit.Btn("자동 편성", "btn", () =>
            {
                AutoPartyService.Auto(Game.Player);
                AudioService.Play("bond");
                _app.SetStatus($"자동 편성 · 편성 점수 {AutoPartyService.Score(Game.Player, Game.Player.party):N0}");
                Game.Touch();
            }, actions);
            auto.SetEnabled(p.owned.Any(o => o.id != GameData.MainId));

            // 비품 자동 장착 sits next to it: both answer "just put the good stuff on", and
            // four slots across five heroes is the other thing nobody compares by hand.
            var gear = UiKit.Btn("비품 장착", "btn", () =>
            {
                var (heroes, slotsChanged) = AutoEquipService.EquipParty(Game.Player);
                AudioService.Play("upgrade", 0.6f);
                _app.SetStatus(slotsChanged > 0
                    ? $"비품 자동 장착 · {heroes}명 · {slotsChanged}부위"
                    : "바꿀 비품이 없습니다");
                Game.Touch();
            }, actions);
            gear.SetEnabled(Game.Player.items.Count > 0 && p.PartyCount() > 0);

            UiKit.Btn("빈 칸 채우기", "btn", OpenPicker, actions);
            var bulk = UiKit.Btn("일괄 강화", "btn btn--primary", () =>
            {
                var before = Game.Player.gold;
                foreach (var member in Game.Player.PartyMembers())
                    StatMath.LevelUpMax(Game.Player, member, 999);
                AudioService.Play("upgrade", 0.6f);
                // The label had to shrink to fit the shape, so the sentence moves here — the
                // status line is where this build already explains what a button just did.
                _app.SetStatus($"편성 전원 일괄 강화 · 골드 -{before - Game.Player.gold:N0}");
                Game.Touch();
            }, actions);
            bulk.SetEnabled(p.PartyCount() > 0);

            var power = p.PartyMembers().Sum(StatMath.Power);
            var readout = UiKit.Div("power-readout", right);
            UiKit.Text(power.ToString("N0"), "power-readout__value", readout);
            UiKit.Text("총 전투력", "power-readout__label", readout);

            var syn = StatMath.Synergy(p);
            var panel = UiKit.Div("panel", right);
            UiKit.Text("부문 시너지", "section-title", panel);
            if (syn.lines.Count == 0)
            {
                UiKit.Text("같은 부문 2명 이상을 함께 넣으면 시너지가 열립니다.", "muted", panel);
            }
            else
            {
                foreach (var line in syn.lines) UiKit.Text(line, "synergy-line", panel);
                UiKit.Text($"합계 · 공격 +{syn.atkBonus:P0} 체력 +{syn.hpBonus:P0}", "section-title", panel);
            }

            var composition = UiKit.Div("panel", right);
            UiKit.Text("편성 구성", "section-title", composition);
            foreach (var role in GameData.Roles)
            {
                var n = p.PartyMembers().Count(o => GameData.Hero(o.id)?.role == role.id);
                UiKit.StatRow(role.name, n > 0 ? $"{n}명" : "없음", composition);
            }
        }

        /// <summary>
        /// 대기 인원, as a panel rather than as a second grid under the line-up. Nothing scrolls
        /// held sideways, and a bench of fifty is a page of its own however it is arranged.
        /// </summary>
        /// <summary>
        /// One line-up slot, in the reference's shape: the portrait, and a plate UNDER it rather
        /// than over it — a tag row (역할 · 부문) above a line carrying the level and the name.
        ///
        /// UiKit.Card is not reused here on purpose. Its plate is an overlay across the bottom of
        /// the art, which is right for a grid of 14 where space is the constraint and wrong for
        /// five figures at full size, where the reference gives each one a caption of its own and
        /// the picture stays uncovered.
        /// </summary>
        VisualElement BuildSlot(HeroDef def, OwnedHero owned, string id)
        {
            var grade = GameData.Grade(def.grade);
            var slot = UiKit.Div("pslot");
            slot.style.borderTopColor = slot.style.borderBottomColor =
                slot.style.borderLeftColor = slot.style.borderRightColor = grade?.Color ?? Color.gray;

            var art = UiKit.Div("pslot__art", slot);
            UiKit.SetArt(art, GameData.WornCardArt(id));
            var gradeBadge = UiKit.Text(def.grade, "pslot__grade", art);
            gradeBadge.style.backgroundColor = grade?.Color ?? Color.gray;

            var plate = UiKit.Div("pslot__plate", slot);
            var tags = UiKit.Div("pslot__tags", plate);
            UiKit.Text(UiKit.RoleName(def.role), "pslot__role", tags);
            UiKit.Text(GameData.Division(def.division)?.name ?? def.division ?? "", "pslot__dept", tags);

            // The name gets its own line, which the reference does not need and this build does:
            // its names are 츠바키 and 시로코, three characters, while these are job titles —
            // "VLOOKUP 분석가" wanted 229px of a 158px label and the audit said so. Lv and ★ share
            // the line underneath instead.
            UiKit.Text(def.name, "pslot__name", plate);

            var line = UiKit.Div("pslot__line", plate);
            UiKit.Text($"Lv.{owned.level}", "pslot__lv", line);
            var stars = UiKit.Text(UiKit.Stars(owned.star), "pslot__stars", line);
            stars.style.color = grade?.Color ?? Color.white;

            slot.RegisterCallback<ClickEvent>(_ => _app.OpenDetail(id));
            return slot;
        }

        void OpenPicker()
        {
            var p = Game.Player;
            var pane = UiKit.Div("picker");
            UiKit.Text("대기 인원", "section-title", pane);

            // Anyone on 출장 is not on the bench: they are away, and a slot filled with
            // someone who is not here is the kind of bug a player reads as the game losing a hero.
            var benched = p.owned
                .Where(o => !p.party.Contains(o.id) && !DispatchService.IsAway(p, o.id))
                .OrderByDescending(StatMath.Power).ToList();

            var pages = new Pages<OwnedHero>(pane, "roster-grid picker-grid", 12)
                .Empty("대기 중인 사원이 없습니다");
            pages.Fill(benched, (o, grid) =>
            {
                var def = GameData.Hero(o.id);
                if (def == null) return;
                grid.Add(UiKit.Card(def, o, () =>
                {
                    if (!Game.Player.AddToParty(o.id)) return;
                    AudioService.Play("tap", 0.6f);
                    Game.Touch();
                    _app.CloseOverlay();
                }));
            });

            UiKit.Btn("닫기", "btn btn--ghost", _app.CloseOverlay, pane);
            _app.OpenOverlay(pane);
        }
    }
}
