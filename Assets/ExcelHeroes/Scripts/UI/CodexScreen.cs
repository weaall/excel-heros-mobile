using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 오류_도감 — the bestiary, as a spreadsheet table rather than a card gallery, because that is
    /// what it is disguised as: a log of errors found while recalculating.
    ///
    /// An entry unlocks by being reached rather than by being counted. Per-kill tallies need save
    /// state this build does not keep yet, and a column of zeroes reads worse than no column.
    /// </summary>
    public class CodexScreen : IScreen
    {
        public string Cell => "A2";
        public string Formula => "=COUNTIF(오류_로그!D:D,\"처리됨\")";

        readonly AppRoot _app;
        VisualElement _root;
        ScrollView _body;
        Label _count;

        public CodexScreen(AppRoot app) => _app = app;

        public VisualElement Build()
        {
            _root = UiKit.Div("codex");

            var head = UiKit.Div("sheet-head", _root);
            _count = UiKit.Text("", "sheet-head__stat", head);
            UiKit.Text("지나온 구간에서 만난 오류가 기록됩니다", "muted", head);

            var header = UiKit.Div("xl-row xl-row--head", _root);
            UiKit.Text("오류", "xl-cell xl-cell--wide", header);
            UiKit.Text("분류", "xl-cell", header);
            UiKit.Text("출현", "xl-cell xl-cell--num", header);

            _body = UiKit.Scroll("xl-body", _root);
            Refresh();
            return _root;
        }

        public void Refresh()
        {
            if (_body == null) return;
            _body.Clear();

            var stage = Game.Player.stage;
            var types = GameData.MonsterTypes;
            var found = 0;

            for (var i = 0; i < types.Count; i++)
            {
                // Types are listed roughly in the order they show up, so the first few are always
                // known and the rest fill in as the player gets further along.
                var firstSeen = 1 + i * 2;
                var known = stage >= firstSeen;
                if (known) found++;

                var row = UiKit.Div(known ? "xl-row" : "xl-row xl-row--locked", _body);
                UiKit.Text(known ? types[i].name : "— 미발견 —", "xl-cell xl-cell--wide", row);
                UiKit.Text(known ? types[i].shape : "", "xl-cell", row);
                UiKit.Text(known ? $"Phase {firstSeen}" : "", "xl-cell xl-cell--num", row);
            }

            // Bosses are listed in phase order and one guards the end of each phase, so a boss's
            // index is the phase you meet it in — there is no phase field on the definition itself.
            for (var i = 0; i < GameData.Bosses.Count; i++)
            {
                var boss = GameData.Bosses[i];
                var phase = i + 1;
                var known = stage >= phase;
                if (known) found++;
                var row = UiKit.Div(known ? "xl-row xl-row--boss" : "xl-row xl-row--locked", _body);
                UiKit.Text(known ? boss.name : "— 미발견 —", "xl-cell xl-cell--wide", row);
                UiKit.Text(known ? "대용량 수식" : "", "xl-cell", row);
                UiKit.Text(known ? $"Phase {phase}" : "", "xl-cell xl-cell--num", row);
            }

            _count.text = $"발견한 오류 {found} / {types.Count + GameData.Bosses.Count}";
        }
    }
}
