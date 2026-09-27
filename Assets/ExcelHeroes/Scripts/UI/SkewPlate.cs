using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// The reference sheet's button plate, drawn as a mesh.
    ///
    /// WHAT IT IS, MEASURED OFF THE SHEET
    /// ----------------------------------
    /// A parallelogram leaning `/ /` with rounded corners, built from four layers outside in:
    ///
    ///   1. a soft outer GLOW (cyan on most plates),
    ///   2. a thin dark EDGE,
    ///   3. a light inner RIM,
    ///   4. the gradient BODY, with a faint diagonal sheen,
    ///
    /// and a small accent TRIANGLE tucked into the top-left and the bottom-right corners — white
    /// on the cyan plate, yellow on navy and white, cyan on the glowing navy one.
    ///
    /// The previous plate was one of those layers (a white stroke) plus a single gold bar in one
    /// corner, leaning the wrong way at the wrong angle. Every button in the game read as a flat
    /// sticker beside the reference, and that was the difference the user kept pointing at.
    ///
    /// WHY A MESH
    /// ----------
    /// A 9-sliced picture only keeps its slant at one height, and everything decorative has to fit
    /// inside the fixed caps. A mesh computes the slant from the element's own height every paint,
    /// so a 60px chip and a 140px dialog button lean at the same angle. See UiPaint for how the
    /// mesh is anti-aliased without Painter2D.
    /// </summary>
    public static class SkewPlate
    {
        /// <summary>
        /// The lean. Measured off the reference sheet: the OK plate's top edge starts 37px right
        /// of its bottom edge on a 126px-tall plate, tan⁻¹(37/126) ≈ 16°. It had been 12° and
        /// leaning the other way (`\ \`), which is the single biggest reason the buttons looked
        /// like a different game — the reference leans every plate `/ /`.
        /// </summary>
        const float SlantDegrees = 12f;

        public static float SlantFor(float height) => Mathf.Tan(SlantDegrees * Mathf.Deg2Rad) * height;

        /// <summary>
        /// Light = the white glass plate (REWARD INFO / GO!). Primary = cyan (OK). Navy = CANCEL.
        /// Glow = the navy plate with the cyan core (START). Glass = the see-through one (the
        /// arrow row). Gold = the recruit call to action. Off = disabled.
        /// </summary>
        public enum Kind { Light, Primary, Navy, Gold, Off, Glow, Glass, Ivory }

        public readonly struct Look
        {
            public readonly Color Top, Bottom, Edge, Rim, Accent, Halo;
            public readonly float RimWidth, HaloWidth, Sheen;
            public readonly bool Core;

            public Look(Color top, Color bottom, Color edge, Color rim, float rimWidth, Color accent,
                        Color halo, float haloWidth, float sheen, bool core = false)
            {
                Top = top; Bottom = bottom; Edge = edge; Rim = rim; RimWidth = rimWidth; Accent = accent;
                Halo = halo; HaloWidth = haloWidth; Sheen = sheen; Core = core;
            }
        }

        static Color C(int r, int g, int b, float a = 1f) => UiPaint.C(r, g, b, a);

        static readonly Color Yellow = C(255, 214, 58);
        static readonly Color Cyan = C(72, 222, 255);

        /// <summary>
        /// Sampled from the reference's own screenshots (확인, 모집, the student page), not from the
        /// generated asset sheet: the real plates are matte — a soft top-to-bottom gradient, a thin
        /// border, a couple of large faint triangles in the fill — with no glow, gloss streak or
        /// corner flags. Accent carries the pattern tint; Sheen its strength.
        /// </summary>
        static Look LookFor(Kind kind) => kind switch
        {
            Kind.Primary => new Look(C(122, 223, 255), C(80, 196, 247), C(56, 156, 212, 0.55f), C(255, 255, 255, 0.55f), 1.5f,
                                     Color.white, C(0, 0, 0, 0f), 0f, 0.2f),
            Kind.Navy or Kind.Glow => new Look(C(54, 74, 118), C(34, 50, 88), C(16, 26, 54, 0.7f), C(120, 150, 200, 0.45f), 1.5f,
                                     Color.white, C(0, 0, 0, 0f), 0f, 0.07f),
            Kind.Gold    => new Look(C(255, 236, 112), C(255, 204, 48), C(214, 150, 16, 0.6f), C(255, 252, 230, 0.7f), 1.5f,
                                     Color.white, C(0, 0, 0, 0f), 0f, 0.22f),
            // target_2's page buttons: white, a gold rim (강화 / 편성)
            Kind.Ivory   => new Look(C(255, 255, 255), C(244, 246, 248), C(214, 166, 52, 0.95f), C(255, 255, 255, 0.95f), 3f,
                                     C(240, 200, 110), C(0, 0, 0, 0f), 0f, 0.12f),
            Kind.Glass   => new Look(C(255, 255, 255, 0.74f), C(238, 246, 252, 0.62f), C(255, 255, 255, 0.85f), C(255, 255, 255, 0.6f), 1.5f,
                                     C(150, 210, 240), C(0, 0, 0, 0f), 0f, 0.18f),
            // off = an OUTLINE, not a grey lump: pale glass with a clear blue-grey border, so a
            // not-yet-claimable button reads as "later", not as broken (ui_critique round 1)
            Kind.Off     => new Look(C(244, 247, 250, 0.82f), C(232, 238, 244, 0.78f), C(170, 186, 206, 0.95f), C(255, 255, 255, 0.5f), 2.2f,
                                     Color.white, C(0, 0, 0, 0f), 0f, 0f),
            _            => new Look(C(255, 255, 255), C(233, 241, 248), C(168, 190, 214, 0.85f), C(255, 255, 255, 0.9f), 1.5f,
                                     C(150, 205, 238), C(0, 0, 0, 0f), 0f, 0.2f),
        };

        /// <summary>
        /// Draws this element as a plate. The element keeps its own padding and layout — only
        /// the background is taken over, so a Button stays a Button.
        ///
        /// `disabledKind` is painted whenever the element is disabled, which is why the caller
        /// never has to keep a second class in sync with SetEnabled.
        ///
        /// Applying twice is a no-op: a plate painted twice draws its glow twice and comes out
        /// visibly heavier, and one caller (the battle result) did exactly that.
        /// </summary>
        public static void Apply(VisualElement el, Kind kind, Kind disabledKind = Kind.Off)
        {
            if (el == null) return;

            // Applying again CHANGES the kind rather than painting a second plate on top: a
            // screen that builds a `btn` (Light) and then asks for Primary means Primary. A
            // second delegate would draw two glows and the first kind underneath.
            if (el.userData is PlateState existing)
            {
                el.RemoveFromClassList(KindClass(existing.Kind));
                existing.Kind = kind;
                existing.Disabled = disabledKind;
                el.AddToClassList(KindClass(kind));
                el.MarkDirtyRepaint();
                return;
            }
            var state = new PlateState { Kind = kind, Disabled = disabledKind };
            el.userData = state;
            el.AddToClassList("skewplate");
            el.AddToClassList(KindClass(kind));

            // The plate IS the background. Anything a stylesheet paints under it shows through
            // the triangles outside the slant — the "background sneaking out behind the button"
            // the user saw. Inline, because it has to beat every screen's own `.x .btn` rule.
            el.style.backgroundColor = Color.clear;
            el.style.backgroundImage = StyleKeyword.None;
            el.style.borderTopWidth = el.style.borderBottomWidth = el.style.borderLeftWidth = el.style.borderRightWidth = 0f;

            // THE TEXT HAS TO MOVE INTO A CHILD.
            //
            // A TextElement paints its own text as part of its content, and the
            // generateVisualContent delegate runs AFTER that — so the plate is drawn over the
            // label. Children paint after the delegate, so a Label child lands on top.
            if (el is TextElement te && !string.IsNullOrEmpty(te.text))
            {
                var label = new Label(te.text);
                label.AddToClassList("plate__label");
                label.pickingMode = PickingMode.Ignore;
                te.text = null;
                el.Add(label);
            }

            el.generateVisualContent += ctx => Paint(el, el.enabledInHierarchy ? state.Kind : state.Disabled, ctx);

            el.RegisterCallback<AttachToPanelEvent>(_ => el.MarkDirtyRepaint());
            el.RegisterCallback<GeometryChangedEvent>(_ => el.MarkDirtyRepaint());

            // Press feedback lives in Juice (sink, then spring back past 1). An inline scale here
            // would outrank the stylesheet's and freeze the spring.
            Juice.Press(el);
        }

        sealed class PlateState { public Kind Kind; public Kind Disabled; }

        /// <summary>USS hook for the ink colour, which must match the plate underneath.</summary>
        static string KindClass(Kind k) => "plate--" + k.ToString().ToLowerInvariant();

        public static void Paint(VisualElement el, Kind kind, MeshGenerationContext ctx)
        {
            var w = el.layout.width;
            var h = el.layout.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 4f || h <= 4f) return;
            DrawPlate(ctx, new Rect(0, 0, w, h), kind);
        }

        /// <summary>The plate itself, for any rect — the modal and the tabs reuse it.</summary>
        public static void DrawPlate(MeshGenerationContext ctx, Rect r, Kind kind, bool accents = true)
        {
            var look = LookFor(kind);
            var h = r.height;
            var w = r.width;
            var slant = Mathf.Min(Mathf.Tan(12f * Mathf.Deg2Rad) * h, w * 0.2f);
            var radius = Mathf.Clamp(h * 0.1f, 3f, 8f);
            var outer = UiPaint.SkewRect(r, slant, radius);

            // a soft shadow under the plate — the only depth the reference gives a button
            UiPaint.Shadow(ctx, outer, new Vector2(0f, Mathf.Clamp(h * 0.05f, 2f, 5f)),
                           C(20, 40, 80, kind == Kind.Off ? 0.08f : 0.2f), Mathf.Clamp(h * 0.08f, 4f, 8f));
            UiPaint.Fill(ctx, outer, UiPaint.Vertical(look.Top, look.Bottom, r.yMin, r.yMax));

            // the reference's faint triangle mosaic: two large facets on the right, one on the left
            if (look.Sheen > 0f)
            {
                var tint = UiPaint.WithAlpha(look.Accent, look.Sheen);
                var tintSoft = UiPaint.WithAlpha(look.Accent, look.Sheen * 0.55f);
                var f1 = new List<Vector2> { new(r.xMin + w * 0.52f, r.yMax), new(r.xMax, r.yMin + h * 0.15f), new(r.xMax, r.yMax) };
                var f2 = new List<Vector2> { new(r.xMin + w * 0.7f, r.yMin), new(r.xMax, r.yMin), new(r.xMax - w * 0.06f, r.yMin + h * 0.58f) };
                var f3 = new List<Vector2> { new(r.xMin, r.yMax), new(r.xMin + w * 0.2f, r.yMax), new(r.xMin + w * 0.08f, r.yMin + h * 0.42f) };
                UiPaint.Fill(ctx, UiPaint.Clip(f1, outer), tint, 0.8f);
                UiPaint.Fill(ctx, UiPaint.Clip(f2, outer), tintSoft, 0.8f);
                UiPaint.Fill(ctx, UiPaint.Clip(f3, outer), tintSoft, 0.8f);
            }

            // a hairline of light along the top edge, and the thin border
            var topBand = new List<Vector2>
            {
                new(r.xMin - 4f, r.yMin - 4f), new(r.xMax + 4f, r.yMin - 4f),
                new(r.xMax + 4f, r.yMin + 2.5f), new(r.xMin - 4f, r.yMin + 2.5f),
            };
            UiPaint.Fill(ctx, UiPaint.Clip(topBand, UiPaint.Offset(outer, -1f)), look.Rim, 0.6f);
            UiPaint.Stroke(ctx, outer, look.Edge, look.RimWidth);
        }

        /// <summary>
        /// The text on a plated element, which is a child rather than the element's own text.
        /// </summary>
        public static void SetText(VisualElement el, string text)
        {
            var label = el?.Q<Label>(className: "plate__label");
            if (label != null) label.text = text;
            else if (el is TextElement te) te.text = text;
        }

        /// <summary>
        /// Which plate a button's classes ask for, or null for anything that is not one.
        ///
        /// Only the ones carrying the plain `btn` class are these slanted plates — UiKit.Btn also
        /// builds filter chips, sheet tabs, pagers and icon buttons, and plating those turned the
        /// roster's filter row into a row of blank parallelograms.
        /// </summary>
        public static Kind? KindFor(string classes)
        {
            if (string.IsNullOrEmpty(classes)) return null;
            var isPlate = false;
            foreach (var t in classes.Split(' ')) if (t == "btn") isPlate = true;
            if (!isPlate) return null;

            if (classes.Contains("btn--primary")) return Kind.Primary;
            if (classes.Contains("btn--glow")) return Kind.Glow;
            if (classes.Contains("btn--ghost") || classes.Contains("btn--navy")) return Kind.Navy;
            if (classes.Contains("btn--gold")) return Kind.Gold;
            if (classes.Contains("btn--glass")) return Kind.Glass;
            if (classes.Contains("btn--ivory")) return Kind.Ivory;
            return Kind.Light;
        }
    }
}
