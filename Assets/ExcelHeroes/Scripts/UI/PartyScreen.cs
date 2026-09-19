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

            var slots = UiKit.Div("party-slots", _root);
            foreach (var id in p.party)
            {
                if (string.IsNullOrEmpty(id))
                {
                    var empty = UiKit.Div("slot slot--empty", slots);
                    UiKit.Text("+", "slot__plus", empty);
                    empty.RegisterCallback<ClickEvent>(_ => _app.Show(AppRoot.Sheet.Roster));
                    continue;
                }

                var def = GameData.Hero(id);
                var owned = p.Find(id);
                if (def == null || owned == null) continue;
                var card = UiKit.Card(def, owned, () => _app.OpenDetail(id));
                card.AddToClassList("slot");
                slots.Add(card);
            }

            var power = p.PartyMembers().Sum(StatMath.Power);
            var readout = UiKit.Div("power-readout", _root);
            UiKit.Text(power.ToString("N0"), "power-readout__value", readout);
            UiKit.Text("총 전투력", "power-readout__label", readout);

            var syn = StatMath.Synergy(p);
            var panel = UiKit.Div("panel", _root);
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

            var composition = UiKit.Div("panel", _root);
            UiKit.Text("편성 구성", "section-title", composition);
            foreach (var role in GameData.Roles)
            {
                var n = p.PartyMembers().Count(o => GameData.Hero(o.id)?.role == role.id);
                UiKit.StatRow(role.name, n > 0 ? $"{n}명" : "없음", composition);
            }

            var bench = UiKit.Div("panel", _root);
            UiKit.Text("대기 인원", "section-title", bench);
            var benchGrid = UiKit.Div("roster-grid", bench);
            var benched = p.owned.Where(o => !p.party.Contains(o.id)).ToList();
            if (benched.Count == 0)
            {
                UiKit.Text("대기 중인 사원이 없습니다.", "muted", bench);
            }
            else
            {
                foreach (var o in benched.OrderByDescending(o => StatMath.Power(o)))
                {
                    var def = GameData.Hero(o.id);
                    if (def == null) continue;
                    benchGrid.Add(UiKit.Card(def, o, () =>
                    {
                        if (Game.Player.AddToParty(o.id)) Game.Touch();
                    }));
                }
            }
        }
    }
}
