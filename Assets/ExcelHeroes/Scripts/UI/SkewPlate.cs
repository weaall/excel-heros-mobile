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
        const float SlantDegrees = 16f;

        public static float SlantFor(float height) => Mathf.Tan(SlantDegrees * Mathf.Deg2Rad) * height;

        /// <summary>
        /// Light = the white glass plate (REWARD INFO / GO!). Primary = cyan (OK). Navy = CANCEL.
        /// Glow = the navy plate with the cyan core (START). Glass = the see-through one (the
        /// arrow row). Gold = the recruit call to action. Off = disabled.
        /// </summary>
        public enum Kind { Light, Primary, Navy, Gold, Off, Glow, Glass }

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

        /// <summary>Sampled from the reference sheet, one row per plate.</summary>
        static Look LookFor(Kind kind) => kind switch
        {
            Kind.Primary => new Look(C(96, 230, 255), C(22, 190, 242), C(18, 128, 186), C(255, 255, 255, 0.85f), 2.5f,
                                     Color.white, C(70, 215, 255, 0.55f), 9f, 0.22f),
            Kind.Navy    => new Look(C(40, 60, 104), C(24, 38, 72), C(10, 18, 38), C(70, 102, 156), 2f,
                                     Yellow, C(20, 40, 80, 0.18f), 4f, 0.08f),
            Kind.Glow    => new Look(C(34, 58, 110), C(20, 44, 92), C(10, 18, 38), C(62, 120, 190), 2f,
                                     Cyan, C(70, 215, 255, 0.6f), 11f, 0.06f, core: true),
            Kind.Gold    => new Look(C(255, 222, 92), C(255, 170, 30), C(190, 110, 0), C(255, 248, 220, 0.9f), 2.5f,
                                     Color.white, C(255, 200, 80, 0.35f), 7f, 0.22f),
            Kind.Glass   => new Look(C(236, 248, 253, 0.62f), C(214, 238, 250, 0.55f), C(120, 200, 232), C(255, 255, 255, 0.8f), 2f,
                                     Cyan, C(70, 215, 255, 0.5f), 9f, 0.16f),
            Kind.Off     => new Look(C(226, 231, 238), C(206, 213, 224), C(168, 178, 194), C(244, 247, 250), 2f,
                                     C(190, 198, 210), C(0, 0, 0, 0f), 0f, 0f),
            _            => new Look(C(252, 254, 255), C(226, 241, 250), C(150, 190, 216), C(255, 255, 255), 2.5f,
                                     Yellow, C(70, 215, 255, 0.45f), 8f, 0.14f),
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
            if (el == null || el.ClassListContains("plate")) return;
            el.AddToClassList("plate");
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

            el.generateVisualContent += ctx => Paint(el, el.enabledInHierarchy ? kind : disabledKind, ctx);

            el.RegisterCallback<AttachToPanelEvent>(_ => el.MarkDirtyRepaint());
            el.RegisterCallback<GeometryChangedEvent>(_ => el.MarkDirtyRepaint());

            // Press feedback. A plate that does not move under a finger reads as a picture of a
            // button. PointerLeave has to release it too, or dragging off leaves it stuck small.
            el.RegisterCallback<PointerDownEvent>(_ => el.style.scale = new Scale(new Vector2(0.96f, 0.96f)), TrickleDown.TrickleDown);
            el.RegisterCallback<PointerUpEvent>(_ => el.style.scale = new Scale(Vector2.one));
            el.RegisterCallback<PointerLeaveEvent>(_ => el.style.scale = new Scale(Vector2.one));
        }

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
            var slant = Mathf.Min(SlantFor(h), r.width * 0.25f);
            var radius = Mathf.Clamp(h * 0.12f, 3f, 10f);

            var outer = UiPaint.SkewRect(r, slant, radius);

            // 1. glow and shadow, outside the plate
            if (look.HaloWidth > 0f)
                UiPaint.Ring(ctx, outer, look.Halo, UiPaint.WithAlpha(look.Halo, 0f), look.HaloWidth);
            UiPaint.Shadow(ctx, outer, new Vector2(0f, 3f), C(20, 40, 80, kind == Kind.Off ? 0.08f : 0.22f), 6f);

            // 2. the dark edge is the outer shape itself; 3. the rim; 4. the body inside both
            UiPaint.Fill(ctx, outer, look.Edge);
            var rim = UiPaint.Offset(outer, -1.5f);
            UiPaint.Fill(ctx, rim, look.Rim);
            var body = UiPaint.Offset(rim, -look.RimWidth);
            UiPaint.Fill(ctx, body, UiPaint.Vertical(look.Top, look.Bottom, r.yMin, r.yMax));

            // START's cyan core: an ellipse low in the middle, fading out, inside the body.
            if (look.Core)
            {
                var core = UiPaint.Ellipse(new Vector2(r.center.x, r.yMin + h * 0.72f), r.width * 0.34f, h * 0.36f);
                var clipped = UiPaint.Clip(core, body);
                UiPaint.Fill(ctx, clipped, p =>
                {
                    var d = new Vector2((p.x - r.center.x) / (r.width * 0.34f), (p.y - (r.yMin + h * 0.72f)) / (h * 0.36f)).magnitude;
                    return C(60, 200, 255, Mathf.Clamp01(0.55f * (1f - d)));
                }, 0f);
                UiPaint.Fill(ctx, UiPaint.Clip(UiPaint.Ellipse(new Vector2(r.center.x, r.yMin + h * 0.72f), r.width * 0.2f, h * 0.2f), body),
                             C(110, 225, 255, 0.28f), 6f);
            }

            // Sheen: the top half a touch lighter, and one diagonal streak parallel to the slant.
            if (look.Sheen > 0f)
            {
                var top = new List<Vector2>
                {
                    new Vector2(r.xMin - 4f, r.yMin - 4f), new Vector2(r.xMax + 4f, r.yMin - 4f),
                    new Vector2(r.xMax + 4f, r.yMin + h * 0.46f), new Vector2(r.xMin - 4f, r.yMin + h * 0.46f),
                };
                UiPaint.Fill(ctx, UiPaint.Clip(top, body), UiPaint.Vertical(
                    new Color(1f, 1f, 1f, look.Sheen), new Color(1f, 1f, 1f, 0f), r.yMin, r.yMin + h * 0.46f), 0f);

                var x0 = r.xMin + slant + h * 0.55f;
                var streak = new List<Vector2>
                {
                    new Vector2(x0, r.yMin), new Vector2(x0 + h * 0.16f, r.yMin),
                    new Vector2(x0 + h * 0.16f - slant, r.yMax), new Vector2(x0 - slant, r.yMax),
                };
                UiPaint.Fill(ctx, UiPaint.Clip(streak, body), new Color(1f, 1f, 1f, look.Sheen * 0.45f), 1f);
            }

            // Corner accents: right triangles in the top-left and bottom-right, clipped to the
            // body so the point follows the rounded corner instead of poking through it.
            if (accents)
            {
                // Measured: the OK plate's white corner is 0.36h along the top and 0.26h down the
                // slanted side. The corner vertex sits on the plate's own corner (outside the
                // rounded body) and the clip trims it to the curve.
                var ax = Mathf.Clamp(h * 0.40f, 10f, 34f);
                var ay = Mathf.Clamp(h * 0.30f, 7f, 24f);
                var tl = new Vector2(r.xMin + slant, r.yMin);
                var br = new Vector2(r.xMax - slant, r.yMax);
                var down = new Vector2(-slant, h) / h;          // one pixel of height down the left edge
                var t1 = new List<Vector2> { tl, tl + new Vector2(ax, 0f), tl + down * ay };
                var t2 = new List<Vector2> { br, br - down * ay, br - new Vector2(ax, 0f) };
                UiPaint.Fill(ctx, UiPaint.Clip(t1, body), look.Accent);
                UiPaint.Fill(ctx, UiPaint.Clip(t2, body), look.Accent);
            }
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
            return Kind.Light;
        }
    }
}
