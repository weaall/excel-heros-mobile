using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 통계_차트 — chart objects sitting on a sheet, the way 삽입 › 차트 leaves them.
    ///
    /// The bars are plain elements with a width percentage rather than a drawn canvas: at this size
    /// a bar chart is a row of rectangles, and doing it in USS keeps it themeable and costs nothing.
    /// </summary>
    public class ChartScreen : IScreen
    {
        public string Cell => "A1";
        public string Formula => "=SUMPRODUCT((인사_명단!C:C=\"S\")*1)";

        readonly AppRoot _app;
        VisualElement _root;
        VisualElement _body;

        public ChartScreen(AppRoot app) => _app = app;

        public VisualElement Build()
        {
            _root = UiKit.Div("charts");
            // Three charts side by side rather than one above the other: each is a short list
            // of bars, and a wide frame fits them all without anything having to move.
            _body = UiKit.Div("charts__cols", _root);
            Refresh();
            return _root;
        }

        public void Refresh()
        {
            if (_body == null) return;
            _body.Clear();

            var p = Game.Player;
            var owned = p.owned.Select(o => GameData.Hero(o.id)).Where(h => h != null).ToList();

            Chart("등급별 보유", GameData.GradeOrder
                .Select(g => (Label: g, Value: (float)owned.Count(h => h.grade == g)))
                .ToList(), "장");

            var byDivision = GameData.Divisions
                .Select(d => (Label: d.name, Value: (float)owned.Count(h => h.division == d.id)))
                .Where(x => x.Value > 0)
                .OrderByDescending(x => x.Value)
                .ToList();
            if (byDivision.Count > 0) Chart("부문별 보유", byDivision, "명");

            var party = p.PartyMembers()
                .Select(o => (Label: GameData.Hero(o.id)?.name ?? o.id, Value: (float)StatMath.Power(o)))
                .ToList();
            if (party.Count > 0) Chart("편성 전투력", party, "");
        }

        void Chart(string title, List<(string Label, float Value)> rows, string unit)
        {
            var obj = UiKit.Div("chart-obj", _body);
            UiKit.Text(title, "chart-obj__title", obj);

            var max = rows.Count == 0 ? 1f : Mathf.Max(1f, rows.Max(r => r.Value));
            foreach (var (label, value) in rows)
            {
                var row = UiKit.Div("chart-row", obj);
                UiKit.Text(label, "chart-row__label", row);
                var track = UiKit.Div("chart-row__track", row);
                var fill = UiKit.Div("chart-row__fill", track);
                fill.style.width = Length.Percent(value / max * 100f);
                UiKit.Text(value.ToString("N0") + unit, "chart-row__value", row);
            }
        }
    }
}
