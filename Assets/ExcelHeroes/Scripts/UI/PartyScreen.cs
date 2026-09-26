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

            var slots = UiKit.Div("party-slots party-slots--sd", left);
            // The row of SD figures: measured once the slots are laid out, so every figure stands
            // exactly over its own plate.
            slots.RegisterCallback<GeometryChangedEvent>(_ => ShowLineup(slots));
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

            // The actions live at the FOOT OF THE RIGHT COLUMN, under 부문 시너지 and 편성 구성,
            // which is where the layout spec puts them. They were briefly a third column of their
            // own; two columns and a rail is 2500px of content in a 2280px working width, and the
            // spec's split — 1500 for the line-up, 700 for everything it adds up to — leaves no
            // room for a third. Stacked, so no line can run out the way the original row did.
            var actions = UiKit.Div("party-rail", right);

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
                // A FOOTER, not a second header. This was a `section-title`, which is now a 66px
                // navy bar — so this panel carried two of them, one at the top and one in the
                // middle, and the 66px it spent on the second was exactly what pushed the synergy
                // lines under the panel's own clip. Two headers in one panel was wrong before it
                // was expensive.
                UiKit.Text($"합계 · 공격 +{syn.atkBonus:P0} 체력 +{syn.hpBonus:P0}", "synergy-total", panel);
            }

            // Two columns, not four rows.
            //
            // The right column has 788px and wants about 920: the readout, both panels and the
            // buttons do not all fit. Making the panels shrink kept the buttons on screen, which
            // was the right call, but it meant 힐러 vanished off the bottom of this panel and half
            // of a synergy line went with it — and clipping INSIDE an overflow:hidden container is
            // the one thing LayoutAudit provably cannot see. Trading a failure the audit catches
            // for one it cannot is a bad trade.
            //
            // So the content gets smaller instead of being hidden: four roles in two lines saves
            // 120px and the column fits honestly.
            var composition = UiKit.Div("panel", right);
            UiKit.Text("편성 구성", "section-title", composition);
            var compGrid = UiKit.Div("statgrid", composition);
            foreach (var role in GameData.Roles)
            {
                var n = p.PartyMembers().Count(o => GameData.Hero(o.id)?.role == role.id);
                UiKit.StatCell(role.name, n > 0 ? $"{n}명" : "없음", compGrid);
            }

            // The buttons were built first because they need `p`, and they belong last because
            // the spec reads 시너지 → 구성 → 액션 from the top. Moved rather than reordered in
            // code: UI Toolkit lays out and DRAWS in child order, so where an element sits in the
            // tree is the only thing that decides both.
            actions.RemoveFromHierarchy();
            right.Add(actions);
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
            var gradeColor = grade?.Color ?? Color.gray;
            var slot = UiKit.Div("pslot");
            // The reference's 부대 편성 has no cards: the members STAND on the floor, feet on one
            // line, a soft shadow under each, and a white plate under that. The cut-out standing
            // art makes that possible; a member without one keeps the card look.
            // The figure itself is the 3D SD model (World/Lineup3D), drawn behind the whole row;
            // the slot keeps only the space it stands in and the plates under it.
            const bool standing = true;
            slot.AddToClassList("pslot--stand");

            var art = UiKit.Div("pslot__art", slot);
            if (standing) { }
            else
            {
                UiKit.SetArt(art, GameData.WornCardArt(id));
                UiKit.GradeBadge(def.grade, gradeColor, "card__grade", slot);
                UiKit.RoleBadge(def.role, "card__role", slot);
                if (owned.star > 0)
                    UiKit.Text(new string('★', System.Math.Clamp(owned.star, 0, 5)), "card__stars pslot__stars", slot);
            }

            // The navy band under the portrait, as on the reference's line-up: the department
            // above, then the name and the level. The name gets a line of its own — these are
            // job titles, and "VLOOKUP 분석가" does not fit beside anything.
            // The reference's line-up tags each member with where they stand — FRONT, MIDDLE,
            // BACK on a dark slanted plate — joined to a coloured plate with what they do.
            // Position follows the role: tanks hold the front, melee the middle, the rest the back.
            var pos = def.role == "tank" ? "FRONT" : def.role == "melee" ? "MIDDLE" : "BACK";
            var roleColor = def.role switch
            {
                "tank" => UiPaint.C(214, 140, 30),
                "melee" => UiPaint.C(200, 40, 60),
                "healer" => UiPaint.C(40, 150, 90),
                _ => UiPaint.C(40, 110, 200),
            };
            var tag = UiKit.Div("pslot__tag", slot);
            ModalFrame.Painted(tag, (ctx, r) =>
            {
                var split = r.xMin + r.width * 0.52f;
                var slant = SkewPlate.SlantFor(r.height);
                var left = UiPaint.SkewRect(Rect.MinMaxRect(r.xMin, r.yMin, split + slant * 0.5f, r.yMax), slant, 2f, 2);
                var right = UiPaint.SkewRect(Rect.MinMaxRect(split - slant * 0.5f, r.yMin, r.xMax, r.yMax), slant, 2f, 2);
                UiPaint.Fill(ctx, left, UiPaint.C(28, 40, 70));
                UiPaint.Fill(ctx, right, roleColor);
            });
            UiKit.Text(pos, "pslot__tag-pos", tag).pickingMode = PickingMode.Ignore;
            UiKit.Text(UiKit.RoleName(def.role), "pslot__tag-role", tag).pickingMode = PickingMode.Ignore;

            var plate = UiKit.Div("pslot__plate", slot);
            if (standing != null)
            {
                // White glass plate with the grade as a small coloured badge at its left edge —
                // where the reference puts the star count.
                ModalFrame.Painted(plate, (ctx, r) =>
                {
                    var body = UiPaint.RoundRect(r, 8f, 4);
                    UiPaint.Shadow(ctx, body, new Vector2(0f, 3f), UiPaint.C(20, 40, 80, 0.2f), 8f);
                    UiPaint.Fill(ctx, body, UiPaint.C(255, 255, 255, 0.96f));
                });
                var row = UiKit.Div("pslot__row", plate);
                var badge = UiKit.Div("pslot__grade", row);
                badge.style.backgroundColor = gradeColor;
                UiKit.Text(def.grade, "pslot__grade-text", badge);
                var lines = UiKit.Div("pslot__lines", row);
                UiKit.Text($"Lv.{owned.level}", "pslot__lv", lines);
                UiKit.Text(def.name, "pslot__name", lines);
                if (owned.star > 0)
                    UiKit.Text(new string('★', System.Math.Clamp(owned.star, 0, 5)), "pslot__stars-inline", plate);
            }
            else
            {
                UiKit.Text(GameData.Division(def.division)?.name ?? def.division ?? "", "pslot__dept", plate);
                var line = UiKit.Div("card__line", plate);
                UiKit.Text(def.name, "card__name pslot__name", line);
                UiKit.Text($"Lv.{owned.level}", "card__level", line);
                UiKit.CardFrame(gradeColor, slot);
            }

            slot.RegisterCallback<ClickEvent>(_ => _app.OpenDetail(id));
            return slot;
        }

        void ShowLineup(VisualElement slots)
        {
            var box = slots.worldBound;
            if (box.width < 10f || box.height < 10f || slots.panel == null) return;
            var panelW = slots.panel.visualTree.worldBound.width;
            var px = panelW > 1f ? Screen.width / panelW : 1f;
            var ids = new System.Collections.Generic.List<string>();
            var centres = new System.Collections.Generic.List<float>();
            var floor = 0.78f;
            var slotW = 0.2f;
            var p = Game.Player;
            var i = 0;
            foreach (var child in slots.Children())
            {
                var id = i < p.party.Count ? p.party[i] : null;
                i++;
                var b = child.worldBound;
                centres.Add((b.center.x - box.xMin) / box.width);
                slotW = b.width / box.width;
                ids.Add(child.ClassListContains("pslot") ? id : null);
                var tag = child.Q(className: "pslot__tag");
                if (tag != null) floor = (tag.worldBound.yMin - box.yMin) / box.height;
            }
            var rt = World.Lineup3D.Instance.Show(slots, ids, centres, slotW,
                                                 Mathf.RoundToInt(box.width * px), Mathf.RoundToInt(box.height * px), floor);
            slots.style.backgroundImage = Background.FromRenderTexture(rt);
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
