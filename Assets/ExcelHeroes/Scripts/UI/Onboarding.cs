using System.Collections.Generic;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 입사 안내 — the first-run tutorial, the way the reference runs its own: a guide stands
    /// large at the left, speaks in the story box along the bottom, and at the end the screen dims
    /// except for the one button to press, with a bouncing pointer on it.
    ///
    /// The guide is HR (인사팀 정대리): it is her job to meet the new intern. What she explains is
    /// the premise as it now stands — the translucent sheet on the back, not a halo — and she
    /// ends on the first thing to do: 모집, paid for by the welcome grant.
    /// </summary>
    public class Onboarding
    {
        const string GuideId = "hr_jung";
        readonly AppRoot _app;
        VisualElement _root, _spot;
        Label _body;
        int _step;
        List<string> _lines;
        Rect _hole;
        bool _pointing;

        public Onboarding(AppRoot app) { _app = app; }

        public static bool Needed(PlayerState p) => p.owned.Count == 0;

        public void Show() => Show(0);

        /// <summary>`step` lets the screenshot driver open straight onto the pointer.</summary>
        public void Show(int step)
        {
            var guide = GameData.Hero(GuideId);
            var gems = Game.Player.gems;
            _lines = new List<string>
            {
                "입사를 환영해요, 김인턴 씨! 저는 인사팀 정대리예요. 오늘부터 잘 부탁해요.",
                "요즘 서울엔 스프레드시트 오류들이 쏟아지고 있어요. 맞설 수 있는 건 등 뒤에 시트가 떠오른 사람뿐이에요.",
                "시트의 칸은 일할수록 차요. 레벨을 올리고, 승급하고, 특성을 익히면 — 찬 칸만큼 강해지죠.",
                $"혼자선 무리예요. 입사 지원금으로 보석 {gems:N0}개를 드렸으니, 먼저 함께 일할 동료를 모집해 봐요!",
            };

            _root = UiKit.Div("guide");
            _root.RegisterCallback<ClickEvent>(OnClick);

            // the dim with a hole (only in the last step)
            _spot = UiKit.Div("guide__dim", _root);
            _spot.pickingMode = PickingMode.Ignore;
            ModalFrame.Painted(_spot, DrawSpot);

            var fig = UiKit.Div("guide__figure", _root);
            fig.pickingMode = PickingMode.Ignore;
            UiKit.SetArt(fig, GameData.StandingArt(GuideId) ?? GameData.CardArt(GuideId));

            var box = UiKit.Div("pbox guide__box", _root);
            box.pickingMode = PickingMode.Ignore;
            ModalFrame.Painted(box, (ctx, r) =>
                UiPaint.Fill(ctx, UiPaint.RoundRect(r, 0f, 1),
                             UiPaint.Vertical(UiPaint.C(8, 14, 30, 0f), UiPaint.C(8, 14, 30, 0.86f), r.yMin, r.yMin + r.height * 0.45f)));
            var nameRow = UiKit.Div("pbox__name", box);
            UiKit.Text(guide?.name ?? "인사팀 정대리", "pbox__title", nameRow);
            UiKit.Text("인사팀", "pbox__sub", nameRow);
            UiKit.Div("pbox__rule guide__rule", box);
            _body = UiKit.Text("", "pbox__text", box);

            var skip = UiKit.Btn("", "prologue__pill guide__skip", Close, _root);
            skip.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            ModalFrame.Painted(skip, (ctx, r) =>
                UiPaint.Fill(ctx, UiPaint.RoundRect(r, r.height * 0.5f, 6), UiPaint.C(255, 255, 255, 0.88f)));
            UiKit.Text("건너뛰기", "prologue__pill-text", skip).pickingMode = PickingMode.Ignore;

            _app.OpenOverlay(_root);
            // the overlay's own scrim would cover the hole; this screen paints its own dim
            _app.Overlay.AddToClassList("overlay--clear");
            _root.schedule.Execute(() => _spot.MarkDirtyRepaint()).Every(33);
            Step(step);
        }

        void Step(int i)
        {
            _step = Mathf.Clamp(i, 0, _lines.Count - 1);
            _body.text = _lines[_step];
            _pointing = _step == _lines.Count - 1;
            AudioService.Play("tap", 0.35f);
        }

        /// <summary>Where the lobby's 모집 button is, in the overlay's own space.</summary>
        Rect Target()
        {
            var cta = _root.panel?.visualTree.Q<Button>("tabGacha");
            if (cta == null) return default;
            var wb = cta.worldBound;
            var tl = _root.WorldToLocal(wb.position);
            return new Rect(tl, wb.size);
        }

        void OnClick(ClickEvent e)
        {
            if (!_pointing) { Step(_step + 1); return; }
            // last step: only the lit button does anything
            var local = _root.WorldToLocal(e.position);
            if (_hole.width > 0f && _hole.Contains(local))
            {
                Close();
                _app.Show(AppRoot.Sheet.Gacha);
            }
        }

        void DrawSpot(MeshGenerationContext ctx, Rect r)
        {
            var dim = UiPaint.C(6, 12, 28, 0.55f);
            var t = _pointing ? Target() : default;
            if (t.width <= 0f)
            {
                UiPaint.Fill(ctx, UiPaint.RoundRect(r, 0f, 1), UiPaint.C(6, 12, 28, 0.35f), 0f);
                return;
            }
            _hole = new Rect(t.xMin - 14f, t.yMin - 14f, t.width + 28f, t.height + 28f);
            // four rects around the hole
            UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, _hole.yMin), 0f, 1), dim, 0f);
            UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin, _hole.yMax, r.xMax, r.yMax), 0f, 1), dim, 0f);
            UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin, _hole.yMin, _hole.xMin, _hole.yMax), 0f, 1), dim, 0f);
            UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(_hole.xMax, _hole.yMin, r.xMax, _hole.yMax), 0f, 1), dim, 0f);
            // a pulsing cyan frame round the button, and a bouncing pointer above it
            var pulse = 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * 5f);
            var frame = UiPaint.RoundRect(_hole, 18f, 6);
            UiPaint.Ring(ctx, frame, UiPaint.C(90, 220, 255, 0.35f + 0.35f * pulse), UiPaint.C(90, 220, 255, 0f), 16f);
            UiPaint.Stroke(ctx, frame, UiPaint.C(120, 230, 255), 4f);
            var bob = Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 4f)) * 16f;
            var tip = new Vector2(_hole.center.x, _hole.yMin - 12f - bob);
            var arrow = new List<Vector2> { tip + new Vector2(-26f, -40f), tip + new Vector2(26f, -40f), tip };
            UiPaint.Fill(ctx, arrow, UiPaint.C(255, 214, 60));
            UiPaint.Stroke(ctx, arrow, UiPaint.C(160, 110, 0), 2f);
        }

        void Close()
        {
            _app.Overlay.RemoveFromClassList("overlay--clear");
            _app.CloseOverlay();
        }
    }
}
