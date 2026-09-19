using System;
using ExcelHeroes.Core;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 위장 모드에서 실제로 보이는 화면 — the boss key.
    ///
    /// Relabelling the sheet tabs was not a disguise. Someone glancing over your shoulder does not
    /// read the tabs; they read the middle of the screen, and the middle of the screen was a gacha
    /// banner with anime portraits on it. So when the boss key is on, the sheet is replaced outright
    /// by what it claims to be: a worksheet of quarterly figures.
    ///
    /// The numbers are derived from the save rather than random, so the sheet is stable — it does
    /// not reshuffle every time you glance at it, which is its own tell — and it still adds up,
    /// because a column of figures whose total is wrong is the first thing an accountant notices.
    /// </summary>
    public class StealthSheet : IScreen
    {
        public string Cell => "D14";
        public string Formula => "=SUM(D2:D13)";

        static readonly string[] Rows =
        {
            "영업1팀", "영업2팀", "영업3팀", "기술본부", "인사팀", "총무팀",
            "재무팀", "감사팀", "마케팅", "고객지원", "물류", "법무",
        };

        readonly AppRoot _app;
        VisualElement _root;

        public StealthSheet(AppRoot app) => _app = app;

        public VisualElement Build()
        {
            _root = UiKit.Div("stealth-sheet");

            var head = UiKit.Div("xl-row xl-row--head", _root);
            UiKit.Text("부서", "xl-cell xl-cell--wide", head);
            UiKit.Text("3분기", "xl-cell xl-cell--num", head);
            UiKit.Text("4분기", "xl-cell xl-cell--num", head);
            UiKit.Text("증감", "xl-cell xl-cell--num", head);

            // Seeded from the save so the same workbook is on screen every time it is opened.
            var seed = (Game.Player?.gold ?? 0) + (Game.Player?.stage ?? 1) * 7919;
            long q3Total = 0, q4Total = 0;

            foreach (var name in Rows)
            {
                seed = unchecked(seed * 1103515245 + 12345);
                var q3 = 1200 + Math.Abs(seed / 65536 % 8800);
                seed = unchecked(seed * 1103515245 + 12345);
                var q4 = q3 + Math.Abs(seed / 65536 % 2400) - 900;
                q3Total += q3;
                q4Total += q4;

                var row = UiKit.Div("xl-row", _root);
                UiKit.Text(name, "xl-cell xl-cell--wide", row);
                UiKit.Text($"{q3:N0}", "xl-cell xl-cell--num", row);
                UiKit.Text($"{q4:N0}", "xl-cell xl-cell--num", row);
                var delta = q4 - q3;
                var cell = UiKit.Text($"{(delta >= 0 ? "+" : "")}{delta:N0}", "xl-cell xl-cell--num", row);
                cell.AddToClassList(delta >= 0 ? "xl-cell--up" : "xl-cell--down");
            }

            var total = UiKit.Div("xl-row xl-row--total", _root);
            UiKit.Text("합계", "xl-cell xl-cell--wide", total);
            UiKit.Text($"{q3Total:N0}", "xl-cell xl-cell--num", total);
            UiKit.Text($"{q4Total:N0}", "xl-cell xl-cell--num", total);
            UiKit.Text($"{(q4Total - q3Total >= 0 ? "+" : "")}{q4Total - q3Total:N0}", "xl-cell xl-cell--num", total);

            return _root;
        }

        public void Refresh() { }
    }
}
