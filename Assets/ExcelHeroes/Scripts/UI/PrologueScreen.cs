using System;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 프롤로그 — the opening, told the way the reference tells its story: the scene fills the
    /// screen and drifts in slowly, the narration types out in a dark box along the bottom (a
    /// title where the speaker's name goes, "프롤로그" where their school would), one line at a
    /// time, and a tap finishes the line or moves on. AUTO and 건너뛰기 sit top right as white pills.
    ///
    /// The premise is the sheet on the back, not a halo: from the scene where it appears, the game
    /// draws a translucent spreadsheet floating behind the character's shoulder (the illustration
    /// only carries its cyan light), and its cells fill in as the lines go by.
    /// </summary>
    public class PrologueScreen
    {
        readonly AppRoot _app;
        readonly Action _onDone;
        VisualElement _root, _art, _sheet, _box, _more;
        Label _title, _sub, _body, _chapter, _autoLabel;
        Button _auto;
        int _page, _line;
        string _full = "";
        float _typed, _idle, _zoom, _sheetFill, _sheetAlpha;
        bool _autoOn;

        const float CharsPerSecond = 38f;
        const float AutoDelay = 1.6f;

        public PrologueScreen(AppRoot app, Action onDone = null)
        {
            _app = app;
            _onDone = onDone;
        }

        public static bool Needed(PlayerState p) => p != null && !p.prologueSeen && GameData.Prologue.Count > 0;

        /// <summary>Where the game draws the back sheet on each scene (fractions of the screen), or null.</summary>
        // The sheet is painted into the scene art now (tools/bake_story.py); the game no longer
        // floats one over it.
        static Rect? SheetSpot(string id) => null;
        static Rect? SheetSpotOld(string id) => id switch
        {
            // beside the head, over the shoulder — never across the face
            "sheet" => new Rect(0.67f, 0.12f, 0.22f, 0.29f),
            "awaken" => new Rect(0.66f, 0.1f, 0.2f, 0.27f),
            "roster" => new Rect(0.5f, 0.12f, 0.18f, 0.24f),
            _ => null,
        };

        public void Show()
        {
            _root = UiKit.Div("prologue prologue--ba");
            _root.RegisterCallback<ClickEvent>(_ => Advance());

            _art = UiKit.Div("prologue__art", _root);
            _art.pickingMode = PickingMode.Ignore;

            // the sheet, drawn by the game over the scene
            _sheet = UiKit.Div("prologue__sheet", _root);
            _sheet.pickingMode = PickingMode.Ignore;
            ModalFrame.Painted(_sheet, (ctx, r) =>
            {
                if (_sheetAlpha <= 0.01f) return;
                var spec = new BackSheet.Spec(Mathf.RoundToInt(_sheetFill * BackSheet.CellCount), UiPaint.C(90, 200, 255),
                                              BackSheet.FrameFor("B"), BackSheet.Pattern.Diagonal, false, true);
                BackSheet.DrawHalo(ctx, r, spec);
            });

            // the text box
            _box = UiKit.Div("pbox", _root);
            _box.pickingMode = PickingMode.Ignore;
            ModalFrame.Painted(_box, (ctx, r) =>
            {
                var band = UiPaint.RoundRect(r, 0f, 1);
                UiPaint.Fill(ctx, band, UiPaint.Vertical(UiPaint.C(8, 14, 30, 0f), UiPaint.C(8, 14, 30, 0.86f), r.yMin, r.yMin + r.height * 0.45f));
            });
            var nameRow = UiKit.Div("pbox__name", _box);
            _title = UiKit.Text("", "pbox__title", nameRow);
            _sub = UiKit.Text("프롤로그", "pbox__sub", nameRow);
            var rule = UiKit.Div("pbox__rule", _box);
            ModalFrame.Painted(rule, (ctx, r) =>
                UiPaint.Fill(ctx, UiPaint.RoundRect(r, 0f, 1), UiPaint.Horizontal(UiPaint.C(255, 255, 255, 0.7f), UiPaint.C(255, 255, 255, 0f), r.xMin, r.xMax)));
            _body = UiKit.Text("", "pbox__text", _box);
            _more = UiKit.Div("pbox__more", _box);
            ModalFrame.Painted(_more, (ctx, r) =>
                UiPaint.Fill(ctx, new System.Collections.Generic.List<Vector2>
                    { new(r.xMin, r.yMin), new(r.xMax, r.yMin), new(r.center.x, r.yMax) }, UiPaint.C(255, 255, 255)));

            // top: chapter caption on the left, AUTO / 건너뛰기 on the right
            _chapter = UiKit.Text("", "prologue__chapter", _root);
            var pills = UiKit.Div("prologue__pills", _root);
            _auto = Pill(pills, "AUTO", ToggleAuto, out _autoLabel);
            Pill(pills, "건너뛰기", Finish, out _);

            _app.OpenOverlay(_root);
            _root.schedule.Execute(Tick).Every(16);
            Scene(0);
        }

        Button Pill(VisualElement parent, string text, Action click, out Label label)
        {
            var b = UiKit.Btn("", "prologue__pill", click, parent);
            b.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            var on = false;
            ModalFrame.Painted(b, (ctx, r) =>
            {
                on = b.ClassListContains("prologue__pill--on");
                var poly = UiPaint.RoundRect(r, r.height * 0.5f, 6);
                UiPaint.Shadow(ctx, poly, new Vector2(0f, 2f), UiPaint.C(0, 0, 0, 0.25f), 6f);
                UiPaint.Fill(ctx, poly, on ? UiPaint.C(110, 219, 255, 0.95f) : UiPaint.C(255, 255, 255, 0.88f));
            });
            label = UiKit.Text(text, "prologue__pill-text", b);
            label.pickingMode = PickingMode.Ignore;
            return b;
        }

        void ToggleAuto()
        {
            _autoOn = !_autoOn;
            _auto.EnableInClassList("prologue__pill--on", _autoOn);
            _auto.MarkDirtyRepaint();
            AudioService.Play("tap", 0.4f);
        }

        void Scene(int page)
        {
            if (page >= GameData.Prologue.Count) { Finish(); return; }
            _page = page;
            var scene = GameData.Prologue[_page];
            UiKit.SetArt(_art, Resources.Load<Sprite>($"Art/Story/{scene.id}"));
            _art.RemoveFromClassList("prologue__art--in");
            _art.schedule.Execute(() => _art.AddToClassList("prologue__art--in")).StartingIn(30);
            _zoom = 0f;

            var spot = SheetSpot(scene.id);
            if (spot.HasValue)
            {
                var s = spot.Value;
                _sheet.style.left = Length.Percent(s.x * 100f);
                _sheet.style.top = Length.Percent(s.y * 100f);
                _sheet.style.width = Length.Percent(s.width * 100f);
                _sheet.style.height = Length.Percent(s.height * 100f);
            }
            if (scene.id == "sheet") _sheetFill = 0f;
            else if (spot.HasValue) _sheetFill = Mathf.Max(_sheetFill, 0.6f);

            _title.text = scene.title;
            _chapter.text = $"PROLOGUE  {_page + 1} / {GameData.Prologue.Count}";
            AudioService.Play("nav", 0.4f);
            Line(0);
        }

        void Line(int line)
        {
            var scene = GameData.Prologue[_page];
            if (line >= scene.lines.Count) { Scene(_page + 1); return; }
            _line = line;
            _full = scene.lines[line];
            _typed = 0f;
            _idle = 0f;
            _body.text = "";
            _more.style.visibility = Visibility.Hidden;
            if (scene.id == "sheet") _sheetFill = (line + 1f) / scene.lines.Count;
        }

        void Advance()
        {
            if (_typed < _full.Length) { _typed = _full.Length; _body.text = _full; return; }
            AudioService.Play("tap", 0.3f);
            Line(_line + 1);
        }

        void Tick()
        {
            if (_root?.panel == null) return;
            const float dt = 0.016f;

            // slow push-in on the scene
            _zoom = Mathf.Min(1f, _zoom + dt / 9f);
            var s = 1.02f + _zoom * 0.06f;
            _art.style.scale = new Scale(new Vector3(s, s, 1f));

            // the sheet fades in where the scene has one, and bobs
            var scene = GameData.Prologue[_page];
            var want = SheetSpot(scene.id).HasValue ? 1f : 0f;
            _sheetAlpha = Mathf.MoveTowards(_sheetAlpha, want, dt * 1.5f);
            _sheet.style.opacity = _sheetAlpha;
            _sheet.style.translate = new Translate(0, Mathf.Sin(Time.realtimeSinceStartup * 1.6f) * 8f);
            _sheet.MarkDirtyRepaint();

            if (_typed < _full.Length)
            {
                _typed = Mathf.Min(_full.Length, _typed + dt * CharsPerSecond);
                _body.text = _full.Substring(0, Mathf.FloorToInt(_typed));
                return;
            }
            _more.style.visibility = Visibility.Visible;
            _more.style.translate = new Translate(0, Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 4f)) * 6f);
            if (_autoOn && (_idle += dt) > AutoDelay) Line(_line + 1);
        }

        /// <summary>For the screenshot driver: jump to a scene with its line fully typed.</summary>
        public void Jump(int page, int line)
        {
            Scene(Mathf.Clamp(page, 0, GameData.Prologue.Count - 1));
            Line(Mathf.Clamp(line, 0, GameData.Prologue[_page].lines.Count - 1));
            _typed = _full.Length;
            _body.text = _full;
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
