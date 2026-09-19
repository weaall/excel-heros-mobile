using System;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 프롤로그 — the seven-page opening the web build shows once on a new save.
    ///
    /// It is the only place the premise is actually told: a meteor, data breaking, errors crawling
    /// out of it, and a halo that appears over anyone who has ever stayed late fixing someone
    /// else's spreadsheet. Without it a new player lands on a gacha banner and a battle and has to
    /// infer all of that, which is what this build was doing.
    ///
    /// It covers the whole screen, chrome included — the web build does the same, and a story beat
    /// framed by a ribbon and a formula bar is not a story beat.
    /// </summary>
    public class PrologueScreen
    {
        readonly AppRoot _app;
        readonly Action _onDone;
        VisualElement _root, _art, _dots;
        Label _title, _body, _counter;
        Button _prev, _next;
        int _page;

        public PrologueScreen(AppRoot app, Action onDone = null)
        {
            _app = app;
            _onDone = onDone;
        }

        public static bool Needed(PlayerState p) => p != null && !p.prologueSeen && GameData.Prologue.Count > 0;

        public void Show()
        {
            _root = UiKit.Div("prologue");

            _art = UiKit.Div("prologue__art", _root);
            // USS has no gradients, and a single translucent box draws a hard line straight across
            // the middle of the illustration. Four stacked bands fade instead.
            for (var i = 0; i < 4; i++) UiKit.Div($"prologue__scrim prologue__scrim--{i}", _root);

            var head = UiKit.Div("prologue__head", _root);
            _counter = UiKit.Text("", "prologue__counter", head);
            _title = UiKit.Text("", "prologue__title", head);

            _body = UiKit.Text("", "prologue__body", _root);

            var foot = UiKit.Div("prologue__foot", _root);
            UiKit.Btn("건너뛰기", "prologue__skip", Finish, foot);
            _dots = UiKit.Div("prologue__dots", foot);
            for (var i = 0; i < GameData.Prologue.Count; i++) UiKit.Div("prologue__dot", _dots);
            _prev = UiKit.Btn("이전", "prologue__nav", () => Go(_page - 1), foot);
            _next = UiKit.Btn("다음", "prologue__nav prologue__nav--primary", () => Go(_page + 1), foot);

            Go(0);
            _app.OpenOverlay(_root);
        }

        void Go(int page)
        {
            if (page >= GameData.Prologue.Count) { Finish(); return; }
            _page = Mathf.Clamp(page, 0, GameData.Prologue.Count - 1);

            var scene = GameData.Prologue[_page];
            UiKit.SetArt(_art, Resources.Load<Sprite>($"Art/Story/{scene.id}"));
            _title.text = scene.title;
            _counter.text = (_page + 1).ToString();
            // One line per line, as written — the narration is paced by its line breaks.
            _body.text = string.Join("\n", scene.lines);

            _prev.SetEnabled(_page > 0);
            _next.text = _page == GameData.Prologue.Count - 1 ? "시작하기" : "다음";

            for (var i = 0; i < _dots.childCount; i++)
                _dots[i].EnableInClassList("prologue__dot--on", i <= _page);

            AudioService.Play("nav", 0.4f);
        }

        void Finish()
        {
            Game.Player.prologueSeen = true;
            Game.Touch();
            _app.CloseOverlay();
            _onDone?.Invoke();
        }
    }
}
