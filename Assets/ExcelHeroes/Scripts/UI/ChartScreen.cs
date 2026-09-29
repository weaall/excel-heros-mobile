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
        VisualElement _body, _kpis;

        public ChartScreen(AppRoot app) => _app = app;

        public VisualElement Build()
        {
            _root = UiKit.Div("charts");
            // Three charts side by side rather than one above the other: each is a short list
            // of bars, and a wide frame fits them all without anything having to move.
            // the headline numbers first, as a row of tiles (ui_score 15-Chart: the charts alone left
            // two-thirds of the screen empty)
            _kpis = UiKit.Div("charts__kpis", _root);
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

            _kpis.Clear();
            var totalPower = p.PartyMembers().Sum(StatMath.Power);
            Kpi("보유 사원", "EMPLOYEES", $"{owned.Count}", $"/ {GameData.Heroes.Count}", owned.Count / (float)Mathf.Max(1, GameData.Heroes.Count), UiPaint.C(0, 170, 240));
            Kpi("편성 전투력", "PARTY POWER", totalPower.ToString("N0"), "", -1f, UiPaint.C(255, 176, 0));
            Kpi("최고 Phase", "BEST PHASE", $"{Mathf.Max(p.stage, p.maxCleared)}", "", -1f, UiPaint.C(120, 90, 230));
            var awakened = p.owned.Count(o => o.awakened);
            Kpi("각성 사원", "AWAKENED", $"{awakened}", $"/ {owned.Count}", owned.Count == 0 ? 0f : awakened / (float)owned.Count, UiPaint.C(230, 160, 20));

            // S first, in each grade's own colour on a slanted badge (ui_score 15-Chart #1, #2)
            Chart("등급별 보유", "BY GRADE", GameData.GradeOrder.AsEnumerable().Reverse()
                .Select(g => (Label: g, Value: (float)owned.Count(h => h.grade == g), Colour: GameData.Grade(g)?.Color ?? Color.gray, Badge: true))
                .ToList(), "장");

            var byDivision = GameData.Divisions
                .Select(d => (Label: d.name, Value: (float)owned.Count(h => h.division == d.id), Colour: d.Color, Badge: false))
                .Where(x => x.Value > 0)
                .OrderByDescending(x => x.Value)
                .ToList();
            if (byDivision.Count > 0) Chart("부문별 보유", "BY DIVISION", byDivision, "명");

            var party = p.PartyMembers()
                .Select(o => (Label: GameData.Hero(o.id)?.name ?? o.id, Value: (float)StatMath.Power(o),
                              Colour: Affinity.ColorOf(Affinity.AtkOf(o.id)), Badge: false))
                .OrderByDescending(x => x.Value)
                .ToList();
            if (party.Count > 0) Chart("편성 전투력", "PARTY POWER", party, "");
        }

        void Kpi(string label, string en, string value, string suffix, float progress, Color accent)
        {
            var t = UiKit.Div("kpi", _kpis);
            ModalFrame.Painted(t, (ctx, r) =>
            {
                var box = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.25f, 8f);
                UiPaint.Shadow(ctx, box, new Vector2(0f, 5f), UiPaint.C(20, 40, 80, 0.14f), 10f);
                UiPaint.Fill(ctx, box, UiPaint.Vertical(UiPaint.C(255, 255, 255, 0.95f), UiPaint.C(236, 244, 251, 0.92f), r.yMin, r.yMax));
                UiPaint.Fill(ctx, UiPaint.Clip(box, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin - 20f, r.yMin, r.xMin + 22f, r.yMax), 0f)), accent, 0f);
                UiPaint.Stroke(ctx, box, UiPaint.C(206, 222, 238), 1.5f);
                if (progress >= 0f)
                {
                    var bar = Rect.MinMaxRect(r.xMin + 60f, r.yMax - 26f, r.xMax - 40f, r.yMax - 16f);
                    UiPaint.Fill(ctx, UiPaint.RoundRect(bar, 3f), UiPaint.C(222, 232, 243));
                    UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(bar.xMin, bar.yMin, bar.xMin + bar.width * Mathf.Clamp01(progress), bar.yMax), 3f), accent);
                }
            });
            var head = UiKit.Div("kpi__head", t);
            UiKit.Text(label, "kpi__label", head);
            UiKit.Text(en, "kpi__en", head);
            var row = UiKit.Div("kpi__row", t);
            UiKit.Text(value, "kpi__value", row);
            if (suffix != "") UiKit.Text(suffix, "kpi__suffix", row);
        }

        void Chart(string title, string en, List<(string Label, float Value, Color Colour, bool Badge)> rows, string unit)
        {
            var obj = UiKit.Div("chart-obj", _body);
            var head = UiKit.Div("chart-obj__head", obj);
            UiKit.Text(title, "chart-obj__title", head);
            UiKit.Text(en, "chart-obj__en", head);

            var max = rows.Count == 0 ? 1f : Mathf.Max(1f, rows.Max(r => r.Value));
            var total = rows.Sum(r => r.Value);
            foreach (var (label, value, colour, badge) in rows)
            {
                var row = UiKit.Div("chart-row", obj);
                if (badge)
                {
                    var b = UiKit.Div("chart-row__badge", row);
                    var col = colour;
                    ModalFrame.Painted(b, (ctx, r) => UiPaint.Fill(ctx, UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.6f, 3f), col));
                    UiKit.Text(label, "chart-row__badge-text", b).pickingMode = PickingMode.Ignore;
                }
                else
                {
                    var dot = UiKit.Div("chart-row__dot", row);
                    dot.style.backgroundColor = colour;
                    UiKit.Text(label, "chart-row__label", row);
                }
                // the bar painted: the item's colour into a lighter tint, a gloss along its top
                var track = UiKit.Div("chart-row__track", row);
                var share = value / max; var c = colour;
                ModalFrame.Painted(track, (ctx, r) =>
                {
                    UiPaint.Fill(ctx, UiPaint.RoundRect(r, 3f), UiPaint.C(225, 234, 244));
                    if (share <= 0f) return;
                    var fr = Rect.MinMaxRect(r.xMin, r.yMin, r.xMin + Mathf.Max(r.height, r.width * share), r.yMax);
                    var bar = UiPaint.SkewRect(fr, r.height * 0.4f, 2f);
                    UiPaint.Fill(ctx, bar, UiPaint.Horizontal(c, Color.Lerp(c, Color.white, 0.35f), fr.xMin, fr.xMax));
                    UiPaint.Fill(ctx, UiPaint.Clip(bar, UiPaint.RoundRect(Rect.MinMaxRect(fr.xMin, fr.yMin, fr.xMax, fr.yMin + r.height * 0.38f), 0f)), UiPaint.C(255, 255, 255, 0.28f), 0f);
                });
                var v = UiKit.Div("chart-row__nums", row);
                UiKit.Text(value.ToString("N0") + unit, "chart-row__value", v);
                if (total > 0f && unit != "") UiKit.Text($"{value / total:P0}", "chart-row__pct", v);
            }
        }
    }
}
