using System.Collections.Generic;
using ExcelHeroes.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// "Now Loading" on the way into a fight, as the reference's: a navy page with a faint triangle lattice,
    /// the progress bar along the top, and the cast's SD gag panels (Art/Loading, tools/loading_panels_gemini.py)
    /// on white cards with a title each — the SD art in a scene, not a line-up (the BA cross-check on the SDs).
    /// Built over everything on the document root, gone after a beat; a tap skips it.
    /// </summary>
    public static class LoadingScreen
    {
        /// <summary>The capture driver turns the automatic one off (it opens it itself for its shot).</summary>
        public static bool Suppress;

        static readonly string[] Titles = { "엑셀이 또 멈췄다……", "부장님은 어디에?", "커피는 생명수", "회의 중의 일상", "월말 결산의 밤" };

        public static VisualElement Build(out System.Action<float> progress)
        {
            var page = UiKit.Div("loading");
            ModalFrame.Painted(page, (ctx, r) =>
            {
                UiPaint.Fill(ctx, UiPaint.RoundRect(r, 0f), UiPaint.Vertical(UiPaint.C(58, 92, 168), UiPaint.C(34, 58, 122), r.yMin, r.yMax), 0f);
                // the reference's faint triangle lattice
                const float s = 120f;
                for (var y = 0; y * s * 0.87f < r.height + s; y++)
                    for (var x = -1; x * s < r.width + s; x++)
                    {
                        var ox = r.xMin + x * s + (y % 2) * s * 0.5f; var oy = r.yMin + y * s * 0.87f;
                        if (((x * 7 + y * 3) & 3) != 0) continue;
                        UiPaint.Fill(ctx, new List<Vector2> { new(ox, oy + s * 0.87f), new(ox + s * 0.5f, oy), new(ox + s, oy + s * 0.87f) }, UiPaint.C(255, 255, 255, 0.05f), 0f);
                    }
            });
            var top = UiKit.Div("loading__top", page);
            UiKit.Text("EXCEL HEROES", "loading__logo", top);
            var bar = UiKit.Div("loading__bar", top);
            var fill = UiKit.Div("loading__fill", bar);
            UiKit.Text("Now Loading…", "loading__now", top);
            progress = t => fill.style.width = Length.Percent(Mathf.Clamp01(t) * 100f);

            var grid = UiKit.Div("loading__grid", page);
            var rows = new[] { UiKit.Div("loading__row", grid), UiKit.Div("loading__row", grid) };
            for (var i = 0; i < Titles.Length; i++)
            {
                var art = Resources.Load<Sprite>("Art/Loading/panel_" + i) ?? (Sprite)null;
                var tex = art == null ? Resources.Load<Texture2D>("Art/Loading/panel_" + i) : null;
                var card = UiKit.Div("loading__card", rows[i < 2 ? 0 : 1]);
                UiKit.Text(Titles[i], "loading__title", card);
                UiKit.Div("loading__rule", card);
                var pic = UiKit.Div("loading__pic", card);
                if (art != null) pic.style.backgroundImage = new StyleBackground(art);
                else if (tex != null) pic.style.backgroundImage = new StyleBackground(tex);
            }
            UiKit.Text("© EXCEL HEROES  ·  사내 공지 없이 무단 야근 금지", "loading__foot", page);
            return page;
        }

        /// <summary>Puts the page over the whole document for `seconds`, the bar filling as it goes.</summary>
        public static VisualElement Show(VisualElement root, float seconds = 1.4f)
        {
            if (root == null) return null;
            var page = Build(out var progress);
            root.Add(page);
            var t0 = Time.realtimeSinceStartup;
            IVisualElementScheduledItem tick = null;
            void Close() { tick?.Pause(); page.RemoveFromHierarchy(); }
            page.RegisterCallback<ClickEvent>(_ => Close());
            tick = page.schedule.Execute(() =>
            {
                var k = (Time.realtimeSinceStartup - t0) / Mathf.Max(0.1f, seconds);
                progress(Mathf.SmoothStep(0f, 1f, k));
                if (k >= 1.15f) Close();
            }).Every(30);
            return page;
        }
    }
}
