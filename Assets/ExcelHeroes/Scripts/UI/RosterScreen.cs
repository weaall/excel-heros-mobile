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

        public IEnumerable<RibbonItem> Ribbon()
        {
            yield return new RibbonItem("▤", "선택 영역 편집", OpenParty, "편성");
            yield return new RibbonItem("Σ", "자동 합계", UpgradeParty, "일괄 강화");
        }

        /// <summary>
        /// 자동 합계 — spend gold on the party until it runs out, cheapest level first, which is what
        /// the web build's button does. Levelling one hero to the cap and stranding the rest is the
        /// wrong shape: the party's weakest link is what the stage checks.
        /// </summary>
        void UpgradeParty()
        {
            foreach (var member in Game.Player.PartyMembers())
                StatMath.LevelUpMax(Game.Player, member, 999);
            Game.Touch();
        }

        void OpenParty()
        {
            var pane = UiKit.Div("task-pane");
            pane.Add(new PartyScreen(_app).Build());
            UiKit.Btn("닫기", "btn btn--ghost", _app.CloseOverlay, pane);
            _app.OpenOverlay(pane);
        }

        public VisualElement Build()
        {
            _root = UiKit.Div("screen-body");

            var head = UiKit.Div("panel", _root);
            _counter = UiKit.Text("", "section-title", head);
            UiKit.Text("카드를 누르면 상세 정보가 열립니다. 같은 카드를 또 뽑으면 ★가 오릅니다.", "muted", head);

            _scroll = UiKit.Scroll(null, _root);
            Refresh();
            return _root;
        }

        public void Refresh()
        {
            var p = Game.Player;
            var ownedCount = p.owned.Count;
            _counter.text = $"도감 {ownedCount} / {GameData.Heroes.Count}";

            _scroll.Clear();
            var grid = UiKit.Div("roster-grid", _scroll);

            // Owned first, strongest grade first — the shelf the player actually browses.
            var ordered = GameData.Heroes
                .OrderByDescending(h => p.Owns(h.id))
                .ThenByDescending(h => GameData.GradeRank(h.grade))
                .ThenBy(h => h.name);

            foreach (var def in ordered)
            {
                var owned = p.Find(def.id);
                grid.Add(UiKit.Card(def, owned, () => _app.OpenDetail(def.id)));
            }
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

            var art = UiKit.Div("detail__art", view);
            UiKit.SetArt(art, GameData.CardArt(heroId));
            ArtMotion.Breathe(art);

            // Outfits, not motion frames: the web build ships three illustrations per hero and no
            // in-between poses, and a generated tween between two drawings of the same face lands
            // in the uncanny gap rather than reading as breathing.
            var skins = GameData.SkinsOf(heroId);
            if (skins.Count > 1)
            {
                var slot = 0;
                var switcher = UiKit.Div("detail__skins", view);
                foreach (var skin in skins)
                {
                    var which = skin;
                    var label = which switch { "casual" => "캐주얼", "formal" => "정장", _ => "기본" };
                    UiKit.Btn(label, "detail__skin", () => UiKit.SetArt(art, GameData.CardArt(heroId, which)), switcher);
                    slot++;
                }
            }

            UiKit.Div("detail__scrim", view);

            var head = UiKit.Div("detail__head", view);
            var name = UiKit.Text(def.name, "detail__name", head);
            name.style.color = grade?.Color ?? Color.white;
            UiKit.Text($"{def.nick} · {def.dept}", "detail__nick", head);

            var body = UiKit.Scroll("detail__body", view);

            if (owned != null)
            {
                UiKit.Text(UiKit.Stars(owned.star), "reveal__grade", body);
                UiKit.StatRow("공격력", StatMath.Atk(owned).ToString("N0"), body);
                UiKit.StatRow("체력", StatMath.Hp(owned).ToString("N0"), body);
                UiKit.StatRow("전투력", StatMath.Power(owned).ToString("N0"), body);
                UiKit.StatRow("레벨", $"{owned.level} / {StatMath.LevelCap(owned.star)}", body);
                var need = GachaService.PromoteCost(owned);
                UiKit.StatRow("승급", need > 0 ? $"중복 {owned.copies} / {need}장" : "최대 ★", body);

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
            else
            {
                UiKit.Text("아직 모집하지 않은 사원입니다.", "muted", body);
            }

            UiKit.StatRow("등급", $"{def.grade} · {grade?.label}", body);
            UiKit.StatRow("역할", UiKit.RoleName(def.role), body);
            UiKit.StatRow("부문", division?.name ?? def.division, body);

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

            // 호감도 — the bond, and the text it buys. The locked rows stay visible on purpose:
            // knowing there IS a private message at Lv5 is what makes Lv4 worth reaching.
            if (owned != null)
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

            var bioPanel = UiKit.Div("panel", body);
            UiKit.Text("인사 기록", "section-title", bioPanel);
            var bio = UiKit.Text(def.bio, null, bioPanel);
            bio.style.whiteSpace = WhiteSpace.Normal;
            if (!string.IsNullOrEmpty(def.ult))
            {
                var ult = UiKit.Text($"“{def.ult}”", "muted", bioPanel);
                ult.style.whiteSpace = WhiteSpace.Normal;
            }

            if (owned != null)
            {
                var inParty = Game.Player.party.Contains(heroId);
                UiKit.Btn(inParty ? "편성에서 빼기" : "편성에 넣기", "btn", () =>
                {
                    if (inParty) Game.Player.RemoveFromParty(heroId);
                    else Game.Player.AddToParty(heroId);
                    Game.Touch();
                    Close(onClose);
                }, body);
            }

            UiKit.Btn("✕", "detail__close", () => Close(onClose), view);
            return view;
        }

        /// <summary>Rebuilds the sheet in place so levelling shows the new numbers immediately.</summary>
        void Reopen(string heroId, System.Action onClose) => _app.OpenDetail(heroId);

        void Close(System.Action onClose) => onClose?.Invoke();
    }
}
