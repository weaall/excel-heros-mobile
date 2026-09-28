using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// The reference sheet's modal window and the pieces that go inside it, as painted meshes.
    ///
    /// Measured off panel (5) "CHAPTER 1: COMPLETE!":
    ///
    ///   FRAME   — a soft cyan glow, a white 3px border with rounded corners, a frosted-glass body,
    ///             and short cyan L-brackets just outside all four corners.
    ///   HEAD    — a pale-blue gradient band across the top with a thick YELLOW line along its
    ///             foot, a navy wedge and a thin yellow slash at the left end, the same mirrored at
    ///             the right, a heavy italic navy title, and a navy ✕.
    ///   RIBBON  — a navy parallelogram section label ("REWARDS RECEIVED") with thin navy slashes
    ///             either side.
    ///   INSET   — a darker glass well the reward tiles sit in.
    ///   TILE    — a white rounded square with a count in heavy italic at its foot.
    ///
    /// The previous "modals" were four different white rounded boxes, one per screen — a white
    /// card with a thin grey line, a white card with a thick blue bottom border, and so on. None
    /// had a head, and the first thing anyone noticed was that they were not the same object.
    /// </summary>
    public static class ModalFrame
    {
        static Color C(int r, int g, int b, float a = 1f) => UiPaint.C(r, g, b, a);

        static readonly Color Navy = C(28, 44, 80);
        static readonly Color Yellow = C(255, 214, 58);
        static readonly Color Cyan = C(82, 220, 255);

        /// <summary>
        /// Makes `el` draw with `paint` and nothing else: its USS background and border are
        /// cleared inline, because anything a stylesheet paints under a mesh shows through
        /// wherever the mesh is not — the rounded corners, the slant.
        /// </summary>
        public static void Painted(VisualElement el, Action<MeshGenerationContext, Rect> paint)
        {
            el.style.backgroundColor = Color.clear;
            el.style.backgroundImage = StyleKeyword.None;
            el.style.borderTopWidth = el.style.borderBottomWidth = el.style.borderLeftWidth = el.style.borderRightWidth = 0f;
            el.generateVisualContent += ctx =>
            {
                var w = el.layout.width;
                var h = el.layout.height;
                if (float.IsNaN(w) || float.IsNaN(h) || w < 4f || h < 4f) return;
                paint(ctx, new Rect(0, 0, w, h));
            };
            el.RegisterCallback<GeometryChangedEvent>(_ => el.MarkDirtyRepaint());
        }

        // ---------------------------------------------------------------- frame

        public static void Frame(VisualElement el) => Painted(el, DrawFrame);

        public static void DrawFrame(MeshGenerationContext ctx, Rect r)
        {
            const float radius = 16f;
            var outer = UiPaint.RoundRect(r, radius, 6);
            UiPaint.Ring(ctx, outer, C(80, 215, 255, 0.40f), C(80, 215, 255, 0f), 18f);
            UiPaint.Shadow(ctx, outer, new Vector2(0f, 8f), C(6, 16, 36, 0.35f), 22f);
            // frosted glass, not paper: the scene behind every screen shows faintly through
            UiPaint.Fill(ctx, outer, C(255, 255, 255, 0.92f), 1.2f);
            var body = UiPaint.Offset(outer, -3f);
            UiPaint.Fill(ctx, body, UiPaint.Vertical(C(246, 250, 254, 0.86f), C(228, 238, 248, 0.84f), r.yMin, r.yMax));
            // faint facets in the corners, the mock-up's glass
            UiPaint.Fill(ctx, UiPaint.Clip(new List<Vector2> { new(r.xMin, r.yMin), new(r.xMin + r.width * 0.22f, r.yMin), new(r.xMin, r.yMin + r.height * 0.4f) }, body), C(200, 228, 248, 0.45f), 0.8f);
            UiPaint.Fill(ctx, UiPaint.Clip(new List<Vector2> { new(r.xMax, r.yMax), new(r.xMax - r.width * 0.28f, r.yMax), new(r.xMax, r.yMax - r.height * 0.45f) }, body), C(200, 228, 248, 0.45f), 0.8f);
            UiPaint.Fill(ctx, UiPaint.Clip(new List<Vector2> { new(r.xMax, r.yMax - r.height * 0.45f), new(r.xMax - r.width * 0.12f, r.yMax - r.height * 0.2f), new(r.xMax, r.yMax - r.height * 0.1f) }, body), C(214, 236, 250, 0.4f), 0.8f);

            // L-brackets, 10px outside each corner.
            const float len = 46f, t = 6f, gap = 10f;
            Bracket(ctx, new Vector2(r.xMin - gap, r.yMin - gap), 1, 1, len, t);
            Bracket(ctx, new Vector2(r.xMax + gap, r.yMin - gap), -1, 1, len, t);
            Bracket(ctx, new Vector2(r.xMin - gap, r.yMax + gap), 1, -1, len, t);
            Bracket(ctx, new Vector2(r.xMax + gap, r.yMax + gap), -1, -1, len, t);
        }

        static void Bracket(MeshGenerationContext ctx, Vector2 corner, int sx, int sy, float len, float t)
        {
            var hx = Rect.MinMaxRect(Mathf.Min(corner.x, corner.x + sx * len), Mathf.Min(corner.y, corner.y + sy * t),
                                     Mathf.Max(corner.x, corner.x + sx * len), Mathf.Max(corner.y, corner.y + sy * t));
            var vy = Rect.MinMaxRect(Mathf.Min(corner.x, corner.x + sx * t), Mathf.Min(corner.y, corner.y + sy * len),
                                     Mathf.Max(corner.x, corner.x + sx * t), Mathf.Max(corner.y, corner.y + sy * len));
            UiPaint.Ring(ctx, UiPaint.RoundRect(hx, 2f, 2), C(82, 220, 255, 0.5f), C(82, 220, 255, 0f), 6f);
            UiPaint.Ring(ctx, UiPaint.RoundRect(vy, 2f, 2), C(82, 220, 255, 0.5f), C(82, 220, 255, 0f), 6f);
            UiPaint.Fill(ctx, UiPaint.RoundRect(hx, 2f, 2), Cyan);
            UiPaint.Fill(ctx, UiPaint.RoundRect(vy, 2f, 2), Cyan);
        }

        // ---------------------------------------------------------------- head

        public static void Head(VisualElement el) => Painted(el, DrawHead);

        /// <summary>
        /// The head sits inside the frame's 3px border, flush with its top, so its top corners
        /// are rounded to match the frame and its bottom corners are square.
        /// </summary>
        public static void DrawHead(MeshGenerationContext ctx, Rect r)
        {
            // The Gemini modal mock-up (tools/out/design/mock_modal_0): a navy slanted strip set IN from
            // the frame, with pale slashes at both ends, on the white glass — not a full-width band
            var h = r.height;
            var strip = Rect.MinMaxRect(r.xMin + r.width * 0.05f, r.yMin + h * 0.2f, r.xMax - r.width * 0.05f, r.yMax - h * 0.06f);
            var slant = SkewPlate.SlantFor(strip.height);
            var band = UiPaint.SkewRect(strip, slant, 4f);
            UiPaint.Shadow(ctx, band, new Vector2(0f, 3f), C(10, 20, 50, 0.22f), 6f);
            UiPaint.Fill(ctx, band, UiPaint.Vertical(C(44, 66, 112), C(24, 40, 78), strip.yMin, strip.yMax));
            UiPaint.Fill(ctx, UiPaint.Clip(new List<Vector2> { new(strip.xMin - 4f, strip.yMin - 4f), new(strip.xMax + 4f, strip.yMin - 4f), new(strip.xMax + 4f, strip.yMin + 3f), new(strip.xMin - 4f, strip.yMin + 3f) }, band), C(120, 150, 210, 0.7f), 0f);
            var sr = Rect.MinMaxRect(r.xMin, strip.yMin, r.xMax, strip.yMax);
            Slash(ctx, sr, strip.xMin + slant * 0.2f - 30f, 10f, slant, C(160, 186, 230, 0.9f));
            Slash(ctx, sr, strip.xMin + slant * 0.2f - 14f, 5f, slant, C(160, 186, 230, 0.6f));
            Slash(ctx, sr, strip.xMax - slant * 0.8f + 18f, 10f, slant, C(160, 186, 230, 0.9f));
            Slash(ctx, sr, strip.xMax - slant * 0.8f + 34f, 5f, slant, C(160, 186, 230, 0.6f));
        }

        static List<Vector2> Expand(Rect r)
            => UiPaint.RoundRect(Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, r.yMax), 13f, 6);

        static void Slash(MeshGenerationContext ctx, Rect r, float x, float w, float slant, Color c)
        {
            var poly = new List<Vector2>
            {
                new Vector2(x + slant, r.yMin), new Vector2(x + slant + w, r.yMin),
                new Vector2(x + w, r.yMax), new Vector2(x, r.yMax),
            };
            UiPaint.Fill(ctx, poly, c);
        }

        // ---------------------------------------------------------------- inside pieces

        public static void Ribbon(VisualElement el) => Painted(el, DrawRibbon);

        public static void DrawRibbon(MeshGenerationContext ctx, Rect r)
        {
            var slant = SkewPlate.SlantFor(r.height);
            var body = UiPaint.SkewRect(r, slant, 3f, 3);
            UiPaint.Shadow(ctx, body, new Vector2(0f, 2f), C(10, 20, 40, 0.25f), 5f);
            UiPaint.Fill(ctx, body, UiPaint.Vertical(C(40, 60, 104), C(22, 36, 68), r.yMin, r.yMax));
            // Thin slashes either side, as on the sheet.
            Slash(ctx, r, r.xMin - 22f, 7f, slant, Navy);
            Slash(ctx, r, r.xMax + 12f, 7f, slant, Navy);
        }

        public static void Inset(VisualElement el) => Painted(el, (ctx, r) =>
        {
            var poly = UiPaint.RoundRect(r, 14f, 6);
            UiPaint.Fill(ctx, poly, C(255, 255, 255, 0.55f), 1f);
            UiPaint.Fill(ctx, UiPaint.Offset(poly, -2f), UiPaint.Vertical(C(150, 168, 192, 0.30f), C(130, 150, 176, 0.34f), r.yMin, r.yMax));
        });

        public static void Tile(VisualElement el, Color? glow = null) => Painted(el, (ctx, r) =>
        {
            var poly = UiPaint.RoundRect(r, 12f, 6);
            if (glow.HasValue) UiPaint.Ring(ctx, poly, glow.Value, UiPaint.WithAlpha(glow.Value, 0f), 16f);
            UiPaint.Shadow(ctx, poly, new Vector2(0f, 4f), C(10, 24, 50, 0.28f), 8f);
            UiPaint.Fill(ctx, poly, C(206, 220, 234));
            UiPaint.Fill(ctx, UiPaint.Offset(poly, -2f), UiPaint.Vertical(C(255, 255, 255), C(236, 243, 249), r.yMin, r.yMax));
            // The faint light-blue sheen wedge across the top-left, as on the sheet's tiles.
            var sheen = new List<Vector2>
            {
                new Vector2(r.xMin, r.yMin), new Vector2(r.xMin + r.width * 0.7f, r.yMin),
                new Vector2(r.xMin + r.width * 0.35f, r.yMin + r.height * 0.3f), new Vector2(r.xMin, r.yMin + r.height * 0.3f),
            };
            UiPaint.Fill(ctx, UiPaint.Clip(sheen, UiPaint.Offset(poly, -2f)), C(200, 232, 250, 0.45f), 1f);
        });
    }
}
