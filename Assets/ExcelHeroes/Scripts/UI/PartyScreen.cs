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
                var card = UiKit.Card(def, owned, () => _app.OpenDetail(id));
                card.AddToClassList("slot");
                slots.Add(card);
            }

            // One button under the line-up, which is where the reference puts 자동 and 확인.
            var actions = UiKit.Div("party-actions", left);
            UiKit.Btn("대기 인원에서 채우기", "btn", OpenPicker, actions);
            var bulk = UiKit.Btn("골드 소진까지 일괄 강화", "btn btn--primary", () =>
            {
                foreach (var member in Game.Player.PartyMembers())
                    StatMath.LevelUpMax(Game.Player, member, 999);
                AudioService.Play("upgrade", 0.6f);
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
        void OpenPicker()
        {
            var p = Game.Player;
            var pane = UiKit.Div("picker");
            UiKit.Text("대기 인원", "section-title", pane);

            var benched = p.owned.Where(o => !p.party.Contains(o.id))
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
