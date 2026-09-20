using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// The reference sheet's button plate — a parallelogram with a white rim, a gold notch in one
    /// corner and a diagonal highlight — drawn as a MESH rather than stretched from a picture.
    ///
    /// WHY THIS REPLACES THE 9-SLICE PNGs
    /// ----------------------------------
    /// The first version of this baked the plates as PNGs and sliced them horizontally. It worked
    /// and it had two faults that are structural rather than fixable:
    ///
    /// 1. **The caps have a capacity.** Everything decorative has to fit inside the fixed end
    ///    slices, because whatever lands in the middle is stretched with it. The first cut put the
    ///    gold notch a few pixels past the boundary, so every wide button came out with a gold bar
    ///    running its entire top edge. Widening the cap to 88px bought room, but the constraint
    ///    never goes away — it just moves.
    /// 2. **The slant is only correct at one height.** A 9-slice stretches vertically too, so a
    ///    98px rail button and a 136px dialog button drawn from the same 120px plate have
    ///    different slant angles. Nothing looks broken; the set simply stops being one set.
    ///
    /// A mesh has neither problem. The slant is computed from the element's own height every time
    /// it paints, so the angle is identical at any size, and the decorations are placed in the
    /// element's coordinates rather than inside a slice budget. It also costs no texture memory
    /// and no import settings.
    ///
    /// ANTI-ALIASING, AND WHY THIS IS A HYBRID
    /// ---------------------------------------
    /// Raw triangles from `MeshGenerationContext.Allocate` are NOT anti-aliased, so a 12-degree
    /// edge comes out as a staircase. `Painter2D` is anti-aliased but fills with one flat colour,
    /// and the sheet's plates have a vertical gradient.
    ///
    /// So: the body is raw geometry, which gets the gradient for free from per-vertex tints, and
    /// everything with a visible outer edge — the rim, the notch, the highlight — is Painter2D.
    /// The rim is a stroke along the same path as the body, so it covers the body's jagged
    /// diagonal with a smooth one. Each technique is used for the thing it is good at.
    /// </summary>
    public static class SkewPlate
    {
        /// <summary>
        /// The lean, in degrees off vertical. The horizontal run is tan(angle) times the element's
        /// HEIGHT, which is what keeps every plate in the set at the same angle whatever its size
        /// — the thing a stretched 9-slice could not do.
        /// </summary>
        const float SlantDegrees = 12f;

        static float SlantFor(float height) => Mathf.Tan(SlantDegrees * Mathf.Deg2Rad) * height;

        public enum Kind { Light, Primary, Navy, Gold, Off }

        public enum Notch { None, TopLeft, BottomRight }

        public readonly struct Look
        {
            public readonly Color Top, Bottom, Rim;
            public readonly float RimWidth, Sheen;
            public readonly Notch Corner;

            public Look(Color top, Color bottom, Color rim, float rimWidth, float sheen, Notch corner)
            { Top = top; Bottom = bottom; Rim = rim; RimWidth = rimWidth; Sheen = sheen; Corner = corner; }
        }

        static Color C(int r, int g, int b, float a = 1f) => new Color(r / 255f, g / 255f, b / 255f, a);

        static readonly Color Gold = C(255, 205, 60);

        /// <summary>
        /// The sheet's palette. It lives here rather than in USS because a mesh painter needs
        /// numbers at paint time, and five custom properties resolved per element per repaint is
        /// more machinery than one table for a set that is five entries long and closed.
        /// </summary>
        static Look LookFor(Kind kind) => kind switch
        {
            Kind.Primary => new Look(C(126, 224, 250), C(58, 176, 232), Color.white, 5f, 0.30f, Notch.TopLeft),
            Kind.Navy    => new Look(C(52, 80, 128), C(28, 46, 82), Color.white, 5f, 0.30f, Notch.TopLeft),
            Kind.Gold    => new Look(C(255, 214, 96), C(240, 170, 24), C(255, 246, 214), 5f, 0.34f, Notch.None),
            Kind.Off     => new Look(C(222, 227, 234), C(198, 206, 218), C(176, 186, 200), 4f, 0f, Notch.None),
            _            => new Look(Color.white, C(222, 235, 248), C(150, 180, 214), 4f, 0.18f, Notch.BottomRight),
        };

        /// <summary>
        /// Draws this element as a plate. The element keeps its own text, padding and layout —
        /// only the background is taken over, so a Button stays a Button.
        ///
        /// `disabledKind` is painted whenever the element is disabled, which is why the caller
        /// never has to keep a second class in sync with SetEnabled.
        /// </summary>
        public static void Apply(VisualElement el, Kind kind, Kind disabledKind = Kind.Off)
        {
            if (el == null) return;

            // THE TEXT HAS TO MOVE INTO A CHILD.
            //
            // A TextElement paints its own text as part of its content, and the
            // generateVisualContent delegate runs AFTER that — so the plate is drawn over the
            // label and every button on every screen came out blank. Children paint after the
            // delegate, so a Label child lands on top of the plate where it belongs.
            //
            // Font size, colour, alignment and white-space are all inherited in UI Toolkit, so
            // the child needs no styling of its own beyond filling the box.
            if (el is TextElement te && !string.IsNullOrEmpty(te.text))
            {
                var label = new Label(te.text);
                label.AddToClassList("plate__label");
                label.pickingMode = PickingMode.Ignore;
                te.text = null;
                el.Add(label);
            }

            el.generateVisualContent += ctx => Paint(el, el.enabledInHierarchy ? kind : disabledKind, ctx);

            // A plate that does not repaint when the element is enabled or disabled shows the
            // wrong one until something else happens to dirty it. Hover is the same story.
            el.RegisterCallback<PointerEnterEvent>(_ => el.MarkDirtyRepaint());
            el.RegisterCallback<AttachToPanelEvent>(_ => el.MarkDirtyRepaint());

            // Press feedback. A plate that does not move under a finger reads as a picture of a
            // button rather than a button; the reference is a phone UI and every control in it
            // takes the tap visibly. PointerLeave has to release it too, or dragging off a
            // button leaves it stuck at 96%.
            el.RegisterCallback<PointerDownEvent>(_ => el.style.scale = new Scale(new Vector2(0.96f, 0.96f)));
            el.RegisterCallback<PointerUpEvent>(_ => el.style.scale = new Scale(Vector2.one));
            el.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                el.style.scale = new Scale(Vector2.one);
                el.MarkDirtyRepaint();
            });
        }

        static void Paint(VisualElement el, Kind kind, MeshGenerationContext ctx)
        {
            var r = el.contentRect;
            if (r.width <= 1f || r.height <= 1f) return;

            // contentRect excludes padding, and the plate is the whole element. Painting in local
            // coordinates from the border box keeps the shape behind the text rather than inside
            // the text's box, which is a 44px difference on these buttons.
            var w = el.resolvedStyle.width;
            var h = el.resolvedStyle.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 1f || h <= 1f) return;

            var look = LookFor(kind);
            var slant = SlantFor(h);

            // The four corners. Top edge is pushed right by the slant, which is what makes the
            // vertical edges lean — exactly the transform USS cannot express.
            var tl = new Vector2(slant, 0f);
            var tr = new Vector2(w, 0f);
            var br = new Vector2(w - slant, h);
            var bl = new Vector2(0f, h);

            FillGradient(ctx, tl, tr, br, bl, look.Top, look.Bottom);

            var p = ctx.painter2D;

            // The highlight, before the rim so the rim draws over its ends. Two bands parallel to
            // the slant, the second narrower and fainter — the sheet's plates read as glass, and
            // one flat band reads as a stripe.
            if (look.Sheen > 0f)
            {
                Band(p, slant, h, w, 0.06f, 0.20f, look.Sheen);
                Band(p, slant, h, w, 0.25f, 0.31f, look.Sheen * 0.55f);
            }

            // The gold corner, clipped to the plate by being drawn inside it.
            if (look.Corner != Notch.None) NotchShape(p, look.Corner, slant, w, h);

            // The rim last, so it covers the body's un-anti-aliased diagonal with a smooth stroke.
            p.strokeColor = look.Rim;
            p.lineWidth = look.RimWidth;
            p.lineJoin = LineJoin.Miter;
            p.BeginPath();
            p.MoveTo(tl); p.LineTo(tr); p.LineTo(br); p.LineTo(bl);
            p.ClosePath();
            p.Stroke();
        }

        /// <summary>
        /// The body: two triangles with the top pair tinted `top` and the bottom pair `bottom`.
        /// This is the one thing Painter2D cannot do — it fills with a single colour — and the one
        /// thing raw vertices do for free.
        /// </summary>
        static void FillGradient(MeshGenerationContext ctx, Vector2 tl, Vector2 tr, Vector2 br,
                                 Vector2 bl, Color top, Color bottom)
        {
            var mesh = ctx.Allocate(4, 6);
            mesh.SetNextVertex(new Vertex { position = new Vector3(tl.x, tl.y, Vertex.nearZ), tint = top });
            mesh.SetNextVertex(new Vertex { position = new Vector3(tr.x, tr.y, Vertex.nearZ), tint = top });
            mesh.SetNextVertex(new Vertex { position = new Vector3(br.x, br.y, Vertex.nearZ), tint = bottom });
            mesh.SetNextVertex(new Vertex { position = new Vector3(bl.x, bl.y, Vertex.nearZ), tint = bottom });
            mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
            mesh.SetNextIndex(2); mesh.SetNextIndex(3); mesh.SetNextIndex(0);
        }

        /// <summary>One highlight band, given as fractions of the width along the top edge.</summary>
        static void Band(Painter2D p, float slant, float h, float w, float from, float to, float alpha)
        {
            // Measured from the left edge in absolute pixels rather than as a fraction of the
            // width: a fraction would make the streak on a 900px dialog button eight times wider
            // than the one on a 110px rail button, and they are meant to be the same object.
            var x0 = slant + 10f + from * h;
            var x1 = slant + 10f + to * h;
            if (x1 >= w - slant) return;       // no room on a very narrow plate; skip rather than smear

            p.fillColor = new Color(1f, 1f, 1f, alpha);
            p.BeginPath();
            p.MoveTo(new Vector2(x0, 0f));
            p.LineTo(new Vector2(x1, 0f));
            p.LineTo(new Vector2(x1 - slant, h));
            p.LineTo(new Vector2(x0 - slant, h));
            p.ClosePath();
            p.Fill();
        }

        static void NotchShape(Painter2D p, Notch corner, float slant, float w, float h)
        {
            var len = Mathf.Min(44f, (w - slant * 2f) * 0.5f);
            if (len <= 6f) return;
            var thick = Mathf.Min(15f, h * 0.14f);

            p.fillColor = Gold;
            p.BeginPath();
            if (corner == Notch.TopLeft)
            {
                p.MoveTo(new Vector2(slant, 0f));
                p.LineTo(new Vector2(slant + len, 0f));
                p.LineTo(new Vector2(slant + len - thick * 0.8f, thick));
                p.LineTo(new Vector2(slant - thick * 0.45f, thick));
            }
            else
            {
                p.MoveTo(new Vector2(w - slant - len + thick * 0.8f, h - thick));
                p.LineTo(new Vector2(w - slant + thick * 0.45f, h - thick));
                p.LineTo(new Vector2(w - slant, h));
                p.LineTo(new Vector2(w - slant - len, h));
            }
            p.ClosePath();
            p.Fill();
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
        /// UiKit.Btn builds every clickable thing in this UI — filter chips, sheet tabs, pagers,
        /// icon buttons — and only the ones carrying the plain `btn` class are meant to be these
        /// slanted plates. Applying it to all of them turned the roster's filter row into a row
        /// of blank parallelograms.
        /// </summary>
        public static Kind? KindFor(string classes)
        {
            if (string.IsNullOrEmpty(classes)) return null;
            var tokens = classes.Split(' ');
            var isPlate = false;
            foreach (var t in tokens) if (t == "btn") isPlate = true;
            if (!isPlate) return null;

            if (classes.Contains("btn--primary")) return Kind.Primary;
            if (classes.Contains("btn--ghost")) return Kind.Navy;
            if (classes.Contains("btn--gold")) return Kind.Gold;
            return Kind.Light;
        }
    }
}
