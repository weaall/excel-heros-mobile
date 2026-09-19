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
    /// The art is letterboxed, not cropped. These backdrops are 912x624 landscape and the phone is
    /// portrait, so filling the band meant cropping about a third of the width away — which is the
    /// third the composition is in. It now sits at its own aspect ratio across the full width, with
    /// black above and below, the way a widescreen shot sits inside a phone.
    /// </summary>
    public class PrologueScreen
    {
        readonly AppRoot _app;
        readonly Action _onDone;
        VisualElement _root, _stage, _art, _dots;
        Label _title, _body, _counter;
        Button _prev, _next;
        int _page;

        // 912x624 — every story backdrop is baked at the same size, and the measured sprite
        // overrides this anyway. It only decides the band's height for the first frame.
        float _aspect = 912f / 624f;

        public PrologueScreen(AppRoot app, Action onDone = null)
        {
            _app = app;
            _onDone = onDone;
        }

        public static bool Needed(PlayerState p) => p != null && !p.prologueSeen && GameData.Prologue.Count > 0;

        public void Show()
        {
            _root = UiKit.Div("prologue");

            // The band and the picture are separate elements: the band is the letterbox, sized from
            // the picture's aspect ratio once the panel knows how wide it is.
            _stage = UiKit.Div("prologue__stage", _root);
            _art = UiKit.Div("prologue__art", _stage);
            _stage.RegisterCallback<GeometryChangedEvent>(OnStageMeasured);

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

        /// <summary>
        /// The band is as large as the picture can be without being cut or distorted.
        ///
        /// Height used to follow width alone, which is right on a phone held upright and wrong held
        /// sideways: a 912x624 backdrop across a 2400-wide screen wants to be 1640 tall, and there
        /// are only 1080. So it takes whichever axis runs out first — the frame letterboxes top and
        /// bottom in portrait and left and right in landscape, and the picture is never cropped.
        /// </summary>
        void Fit()
        {
            if (_root == null || _stage == null) return;

            var width = _stage.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 1f) return;

            var room = _root.resolvedStyle.height * ArtShare;
            if (float.IsNaN(room) || room <= 1f) { _stage.style.height = width / _aspect; return; }

            var byWidth = width / _aspect;
            if (byWidth <= room) { _stage.style.height = byWidth; _stage.style.width = Length.Percent(100); }
            else { _stage.style.height = room; _stage.style.width = room * _aspect; }
        }

        /// <summary>How much of the screen the picture may take before the words need the rest.</summary>
        const float ArtShare = 0.56f;

        void OnStageMeasured(GeometryChangedEvent e) => Fit();

        void Go(int page)
        {
            if (page >= GameData.Prologue.Count) { Finish(); return; }
            _page = Mathf.Clamp(page, 0, GameData.Prologue.Count - 1);

            var scene = GameData.Prologue[_page];
            var sprite = Resources.Load<Sprite>($"Art/Story/{scene.id}");
            UiKit.SetArt(_art, sprite);
            if (sprite != null && sprite.rect.height > 0f)
            {
                _aspect = sprite.rect.width / sprite.rect.height;
                Fit();
            }

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
