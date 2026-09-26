using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// Anti-aliased convex shapes for UI Toolkit, drawn as raw meshes.
    ///
    /// WHY NOT PAINTER2D
    /// -----------------
    /// Painter2D anti-aliases but fills with one flat colour. The reference sheet's plates are
    /// never flat: every one has a vertical gradient, a soft outer glow and a soft drop shadow,
    /// and none of those is a flat fill. Raw `MeshGenerationContext.Allocate` triangles take a
    /// colour per vertex — so gradients, glows and shadows come free — but they are NOT
    /// anti-aliased, which is why the first mesh plate had staircase edges and needed Painter2D
    /// strokes laid over them to hide it.
    ///
    /// This does both in one mesh: every filled shape gets a one-pixel FRINGE — a ring of
    /// vertices just outside the edge with the same colour at alpha 0. The GPU interpolates
    /// across it, which is exactly what an anti-aliased edge is. Widen the same fringe and it is
    /// a glow; offset the shape and widen it and it is a soft shadow.
    ///
    /// Everything here is CONVEX (rounded rectangles, parallelograms, triangles, ellipses),
    /// which keeps triangulation to a fan and polygon clipping to Sutherland–Hodgman. The UI
    /// needs nothing concave.
    ///
    /// Coordinates are the element's local space, y down. Polygons wind clockwise on screen
    /// (top-left, top-right, bottom-right, bottom-left).
    /// </summary>
    public static class UiPaint
    {
        public delegate Color Shade(Vector2 p);

        public static Color C(int r, int g, int b, float a = 1f) => new Color(r / 255f, g / 255f, b / 255f, a);

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        /// <summary>A vertical gradient between two colours across [y0, y1].</summary>
        public static Shade Vertical(Color top, Color bottom, float y0, float y1)
            => p => Color.Lerp(top, bottom, Mathf.InverseLerp(y0, y1, p.y));

        /// <summary>A horizontal gradient between two colours across [x0, x1].</summary>
        public static Shade Horizontal(Color left, Color right, float x0, float x1)
            => p => Color.Lerp(left, right, Mathf.InverseLerp(x0, x1, p.x));

        public static Shade Flat(Color c) => _ => c;

        // ---------------------------------------------------------------- shapes

        /// <summary>
        /// A parallelogram leaning like `/ /` — the TOP edge sits `slant` to the right of the
        /// bottom one, which is how the reference leans every plate. Corners rounded by `radius`.
        /// </summary>
        public static List<Vector2> SkewRect(Rect r, float slant, float radius, int seg = 5)
        {
            var tl = new Vector2(r.xMin + slant, r.yMin);
            var tr = new Vector2(r.xMax, r.yMin);
            var br = new Vector2(r.xMax - slant, r.yMax);
            var bl = new Vector2(r.xMin, r.yMax);
            return Round(new[] { tl, tr, br, bl }, radius, seg);
        }

        public static List<Vector2> RoundRect(Rect r, float radius, int seg = 5)
            => Round(new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin),
                             new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) }, radius, seg);

        public static List<Vector2> Ellipse(Vector2 c, float rx, float ry, int seg = 28)
        {
            var pts = new List<Vector2>(seg);
            for (var i = 0; i < seg; i++)
            {
                var a = -Mathf.PI / 2f + i * Mathf.PI * 2f / seg;
                pts.Add(new Vector2(c.x + Mathf.Cos(a) * rx, c.y + Mathf.Sin(a) * ry));
            }
            return pts;
        }

        /// <summary>
        /// Rounds every corner of a convex polygon with a quadratic curve. The curve is tangent
        /// to both edges, so the result stays convex, and the radius is clamped to half of each
        /// neighbouring edge so short sides never fold over.
        /// </summary>
        public static List<Vector2> Round(IList<Vector2> poly, float radius, int seg = 5)
        {
            var n = poly.Count;
            var outPts = new List<Vector2>(n * (seg + 1));
            if (radius <= 0.01f) { outPts.AddRange(poly); return outPts; }
            for (var i = 0; i < n; i++)
            {
                var p = poly[i];
                var a = poly[(i + n - 1) % n];
                var b = poly[(i + 1) % n];
                var ra = Mathf.Min(radius, (a - p).magnitude * 0.5f);
                var rb = Mathf.Min(radius, (b - p).magnitude * 0.5f);
                var p1 = p + (a - p).normalized * ra;
                var p2 = p + (b - p).normalized * rb;
                for (var s = 0; s <= seg; s++)
                {
                    var t = s / (float)seg;
                    var u = 1f - t;
                    outPts.Add(u * u * p1 + 2f * u * t * p + t * t * p2);
                }
            }
            return outPts;
        }

        /// <summary>Moves every edge of a convex polygon outward by `d` (inward if negative).</summary>
        public static List<Vector2> Offset(IList<Vector2> poly, float d)
        {
            var n = poly.Count;
            var res = new List<Vector2>(n);
            for (var i = 0; i < n; i++) res.Add(poly[i] + Miter(poly, i) * d);
            return res;
        }

        /// <summary>Everything moved by `delta` — for shadows.</summary>
        public static List<Vector2> Shift(IList<Vector2> poly, Vector2 delta)
        {
            var res = new List<Vector2>(poly.Count);
            foreach (var p in poly) res.Add(p + delta);
            return res;
        }

        /// <summary>
        /// Clips `subject` to the inside of the convex polygon `clip` (Sutherland–Hodgman). Used to
        /// keep a corner triangle or a highlight band inside a rounded plate — without it the
        /// triangle's point pokes out past the rounded corner.
        /// </summary>
        public static List<Vector2> Clip(IList<Vector2> subject, IList<Vector2> clip)
        {
            var output = new List<Vector2>(subject);
            var n = clip.Count;
            for (var i = 0; i < n && output.Count > 0; i++)
            {
                var a = clip[i];
                var b = clip[(i + 1) % n];
                var input = output;
                output = new List<Vector2>(input.Count + 4);
                for (var j = 0; j < input.Count; j++)
                {
                    var cur = input[j];
                    var prev = input[(j + input.Count - 1) % input.Count];
                    var curIn = Side(a, b, cur) >= 0f;
                    var prevIn = Side(a, b, prev) >= 0f;
                    if (curIn)
                    {
                        if (!prevIn) output.Add(Intersect(prev, cur, a, b));
                        output.Add(cur);
                    }
                    else if (prevIn) output.Add(Intersect(prev, cur, a, b));
                }
            }
            return output;
        }

        // ---------------------------------------------------------------- drawing

        /// <summary>
        /// Fills a convex polygon, anti-aliased. The fringe straddles the edge — half inside,
        /// half outside — so the visible edge lands where the polygon says it is.
        /// </summary>
        public static void Fill(MeshGenerationContext ctx, IList<Vector2> poly, Shade shade, float feather = 1f)
        {
            var n = poly.Count;
            if (n < 3) return;
            var half = feather * 0.5f;

            var centre = Vector2.zero;
            foreach (var p in poly) centre += p;
            centre /= n;

            var mesh = ctx.Allocate(1 + n * 2, n * 3 + n * 6);
            mesh.SetNextVertex(V(centre, shade(centre)));
            for (var i = 0; i < n; i++)
            {
                var m = Miter(poly, i);
                var inner = poly[i] - m * half;
                mesh.SetNextVertex(V(inner, shade(inner)));
            }
            for (var i = 0; i < n; i++)
            {
                var m = Miter(poly, i);
                var outer = poly[i] + m * half;
                mesh.SetNextVertex(V(outer, WithAlpha(shade(outer), 0f)));
            }

            for (var i = 0; i < n; i++)
            {
                var a = (ushort)(1 + i);
                var b = (ushort)(1 + (i + 1) % n);
                mesh.SetNextIndex(0); mesh.SetNextIndex(a); mesh.SetNextIndex(b);
            }
            for (var i = 0; i < n; i++)
            {
                var ia = (ushort)(1 + i);
                var ib = (ushort)(1 + (i + 1) % n);
                var oa = (ushort)(1 + n + i);
                var ob = (ushort)(1 + n + (i + 1) % n);
                mesh.SetNextIndex(ia); mesh.SetNextIndex(oa); mesh.SetNextIndex(ob);
                mesh.SetNextIndex(ob); mesh.SetNextIndex(ib); mesh.SetNextIndex(ia);
            }
        }

        public static void Fill(MeshGenerationContext ctx, IList<Vector2> poly, Color c, float feather = 1f)
            => Fill(ctx, poly, Flat(c), feather);

        /// <summary>
        /// A ring from the polygon's edge outward by `width`, fading from `inner` to `outer`
        /// colour. With `outer` at alpha 0 this is the reference's soft outer glow.
        /// </summary>
        public static void Ring(MeshGenerationContext ctx, IList<Vector2> poly, Color inner, Color outer, float width)
        {
            var n = poly.Count;
            if (n < 3 || width <= 0f) return;
            var mesh = ctx.Allocate(n * 2, n * 6);
            for (var i = 0; i < n; i++) mesh.SetNextVertex(V(poly[i], inner));
            for (var i = 0; i < n; i++) mesh.SetNextVertex(V(poly[i] + Miter(poly, i) * width, outer));
            for (var i = 0; i < n; i++)
            {
                var ia = (ushort)i;
                var ib = (ushort)((i + 1) % n);
                var oa = (ushort)(n + i);
                var ob = (ushort)(n + (i + 1) % n);
                mesh.SetNextIndex(ia); mesh.SetNextIndex(oa); mesh.SetNextIndex(ob);
                mesh.SetNextIndex(ob); mesh.SetNextIndex(ib); mesh.SetNextIndex(ia);
            }
        }

        /// <summary>
        /// A band of `width` along the inside of the edge — a border that follows rounded corners
        /// exactly. Drawn as the outer shape with the inner punched out would need a concave fill;
        /// a ring between two offsets of the same convex path needs none.
        /// </summary>
        public static void Stroke(MeshGenerationContext ctx, IList<Vector2> poly, Color c, float width)
        {
            var inner = Offset(poly, -width);
            var n = poly.Count;
            if (n < 3 || width <= 0f) return;
            // Two anti-alias fringes (outside and inside) plus the solid band.
            var outerA = Offset(poly, 0.5f);
            var outerS = Offset(poly, -0.5f);
            var innerS = Offset(poly, -width + 0.5f);
            var innerA = Offset(poly, -width - 0.5f);
            Band(ctx, outerA, outerS, WithAlpha(c, 0f), c);
            Band(ctx, outerS, innerS, c, c);
            Band(ctx, innerS, innerA, c, WithAlpha(c, 0f));
            _ = inner;
        }

        static void Band(MeshGenerationContext ctx, IList<Vector2> a, IList<Vector2> b, Color ca, Color cb)
        {
            var n = a.Count;
            var mesh = ctx.Allocate(n * 2, n * 6);
            for (var i = 0; i < n; i++) mesh.SetNextVertex(V(a[i], ca));
            for (var i = 0; i < n; i++) mesh.SetNextVertex(V(b[i], cb));
            for (var i = 0; i < n; i++)
            {
                var ia = (ushort)i;
                var ib = (ushort)((i + 1) % n);
                var oa = (ushort)(n + i);
                var ob = (ushort)(n + (i + 1) % n);
                mesh.SetNextIndex(ia); mesh.SetNextIndex(ob); mesh.SetNextIndex(oa);
                mesh.SetNextIndex(ia); mesh.SetNextIndex(ib); mesh.SetNextIndex(ob);
            }
        }

        /// <summary>
        /// A soft shadow: the shape shifted by `delta`, filled with a wide fringe so it fades out
        /// over `blur` pixels. Not a Gaussian, and at these sizes nobody can tell.
        /// </summary>
        public static void Shadow(MeshGenerationContext ctx, IList<Vector2> poly, Vector2 delta, Color c, float blur)
            => Fill(ctx, Offset(Shift(poly, delta), blur * 0.25f), Flat(c), blur);

        /// <summary>
        /// Draws a sprite CLIPPED to a convex polygon, undistorted — the image is placed as
        /// `scale-and-crop` would place it over `dest` (anchored `anchorY` of the way down), and
        /// the polygon's vertices sample it where they sit. UI Toolkit can only clip an image to a
        /// rectangle or a rounded one; the reference's ten-pull cards are parallelograms with the
        /// portrait cut to the slant, and this is how that is drawn.
        /// </summary>
        /// <summary>
        /// Like Image, but zoomed onto one point of the picture: the image is `zoom` times the
        /// width of dest, and the point `focusY` of the way down it sits at dest's centre. This is
        /// how a full-body cut-out becomes a face (zoom ~2.8) or a bust (~1.75) without a crop file.
        /// </summary>
        public static void ImageFocus(MeshGenerationContext ctx, IList<Vector2> poly, Sprite sprite, Rect dest, float zoom, float focusY, Color? tint = null)
        {
            if (sprite == null || poly.Count < 3) return;
            var tex = sprite.texture;
            // rect, not textureRect: a tight sprite's textureRect is trimmed to its opaque pixels,
            // and the crop is defined on the whole normalised canvas (feet at the same line for all).
            var tr = sprite.rect;
            var uvMin = new Vector2(tr.xMin / tex.width, tr.yMin / tex.height);
            var uvSize = new Vector2(tr.width / tex.width, tr.height / tex.height);
            var aspect = tr.width / tr.height;
            var w = dest.width * zoom;
            var h = w / aspect;
            if (h < dest.height) { h = dest.height; w = h * aspect; }
            var y = dest.center.y - focusY * h;
            y = Mathf.Min(y, dest.yMin);                 // never leave empty space above the picture
            var img = new Rect(dest.center.x - w * 0.5f, y, w, h);
            var c = tint ?? Color.white;
            var n = poly.Count;
            var mesh = ctx.Allocate(n + 1, n * 3, tex);
            var centre = Vector2.zero;
            foreach (var p in poly) centre += p;
            centre /= n;
            mesh.SetNextVertex(TexV(centre, img, uvMin, uvSize, c));
            foreach (var p in poly) mesh.SetNextVertex(TexV(p, img, uvMin, uvSize, c));
            for (var i = 0; i < n; i++)
            {
                mesh.SetNextIndex(0);
                mesh.SetNextIndex((ushort)(1 + i));
                mesh.SetNextIndex((ushort)(1 + (i + 1) % n));
            }
        }

        public static void Image(MeshGenerationContext ctx, IList<Vector2> poly, Sprite sprite, Rect dest, float anchorY = 0f, Color? tint = null)
        {
            if (sprite == null || poly.Count < 3) return;
            var tex = sprite.texture;
            var tr = sprite.textureRect;
            var uvMin = new Vector2(tr.xMin / tex.width, tr.yMin / tex.height);
            var uvSize = new Vector2(tr.width / tex.width, tr.height / tex.height);

            // scale-and-crop: cover dest, keep aspect.
            var aspect = tr.width / tr.height;
            var w = dest.width;
            var h = w / aspect;
            if (h < dest.height) { h = dest.height; w = h * aspect; }
            var img = new Rect(dest.center.x - w * 0.5f, dest.yMin - (h - dest.height) * anchorY, w, h);

            var c = tint ?? Color.white;
            var n = poly.Count;
            var mesh = ctx.Allocate(n + 1, n * 3, tex);
            var centre = Vector2.zero;
            foreach (var p in poly) centre += p;
            centre /= n;
            mesh.SetNextVertex(TexV(centre, img, uvMin, uvSize, c));
            foreach (var p in poly) mesh.SetNextVertex(TexV(p, img, uvMin, uvSize, c));
            for (var i = 0; i < n; i++)
            {
                mesh.SetNextIndex(0);
                mesh.SetNextIndex((ushort)(1 + i));
                mesh.SetNextIndex((ushort)(1 + (i + 1) % n));
            }
        }

        static Vertex TexV(Vector2 p, Rect img, Vector2 uvMin, Vector2 uvSize, Color c)
        {
            // UI space is y-down, texture space is y-up.
            var u = (p.x - img.xMin) / img.width;
            var v = 1f - (p.y - img.yMin) / img.height;
            return new Vertex
            {
                position = new Vector3(p.x, p.y, Vertex.nearZ),
                tint = c,
                uv = new Vector2(uvMin.x + u * uvSize.x, uvMin.y + v * uvSize.y),
            };
        }

        // ---------------------------------------------------------------- helpers

        static Vertex V(Vector2 p, Color c) => new Vertex { position = new Vector3(p.x, p.y, Vertex.nearZ), tint = c };

        static float Side(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        static Vector2 Intersect(Vector2 p0, Vector2 p1, Vector2 a, Vector2 b)
        {
            var d = p1 - p0;
            var e = b - a;
            var den = d.x * e.y - d.y * e.x;
            if (Mathf.Abs(den) < 1e-6f) return p1;
            var t = ((a.x - p0.x) * e.y - (a.y - p0.y) * e.x) / den;
            return p0 + d * t;
        }

        /// <summary>
        /// The outward miter direction at vertex i, scaled so that moving the vertex by it moves
        /// both neighbouring EDGES by one unit. Clamped, because a near-180° corner (a point
        /// in the middle of a rounded arc) must not shoot off, and a sharp one must not either.
        /// </summary>
        static Vector2 Miter(IList<Vector2> poly, int i)
        {
            var n = poly.Count;
            var p = poly[i];
            var a = poly[(i + n - 1) % n];
            var b = poly[(i + 1) % n];
            var n1 = OutNormal(a, p);
            var n2 = OutNormal(p, b);
            if (n1 == Vector2.zero) n1 = n2;
            if (n2 == Vector2.zero) n2 = n1;
            var m = (n1 + n2).normalized;
            var dot = Vector2.Dot(m, n1);
            return m / Mathf.Max(0.35f, dot);
        }

        /// <summary>Outward normal of edge a→b for a clockwise-on-screen (y down) polygon.</summary>
        static Vector2 OutNormal(Vector2 a, Vector2 b)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-8f) return Vector2.zero;
            return new Vector2(d.y, -d.x).normalized;
        }
    }
}
