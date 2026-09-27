using System.Collections.Generic;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 등 뒤의 시트 — the game's own device where the reference has a halo: a small spreadsheet
    /// grid (A1:D3) drawn in light behind the hero's shoulder, by the game, never by the art.
    /// See Halo() for the geometry; both painters (DrawHalo for the UI, SheetTexture for 3D) use it.
    ///
    ///   ★ + level   → how many of the four columns are lit (at least one once owned)
    ///   accent      → the neon colour of the grid
    ///   grade       → the ornament: C brackets, B the active cell, A an arc ring, S ring + sparkles
    ///   awakened    → everything turns gold
    ///   side        → left or right shoulder, fixed per id by the same hash the web uses
    ///
    /// Before this it was four filled, patterned cells, and before that a whole spreadsheet; both
    /// read as noise. A halo is lines of light, not boxes.
    /// </summary>
    public static class BackSheet
    {
        static Color C(int r, int g, int b, float a = 1f) => UiPaint.C(r, g, b, a);

        public const int CellCount = 4;

        public enum Pattern { Stripes, Dots, Check, Diagonal, Grid, Waves }

        public readonly struct Spec
        {
            public readonly int Filled;
            public readonly Color Accent, Frame;
            public readonly Pattern Pattern;
            public readonly bool Gold, Left;

            public Spec(int filled, Color accent, Color frame, Pattern pattern, bool gold, bool left)
            {
                Filled = Mathf.Clamp(filled, 0, CellCount); Accent = accent; Frame = frame; Pattern = pattern; Gold = gold; Left = left;
            }
        }

        /// <summary>Sum of the id's UTF-16 code units — the same hash as design.js, so the side agrees.</summary>
        public static int Hash(string id)
        {
            var h = 0;
            foreach (var ch in id ?? "") h += ch;
            return h;
        }

        public static bool LeftSide(string id) => Hash(id) % 2 == 0;

        public static Color FrameFor(string grade) => grade switch
        {
            "S" => C(255, 200, 50), "A" => C(176, 110, 236), "B" => C(70, 150, 240), "C" => C(60, 190, 120), _ => C(150, 160, 176),
        };

        public static Spec For(HeroDef def, OwnedHero owned)
        {
            var grade = def?.grade ?? "D";
            var filled = 0;
            if (owned != null)
            {
                var star = Mathf.Clamp(owned.star, 1, 5);
                var cap = Mathf.Max(1, StatMath.LevelCap(owned));
                var lv = Mathf.Clamp01(owned.level / (float)cap);
                filled = Mathf.Clamp(Mathf.RoundToInt(CellCount * (0.6f * (star - 1) / 4f + 0.4f * lv)), 1, CellCount);
            }
            var accent = ColorUtility.TryParseHtmlString(def?.colorAccent ?? "", out var c) ? c : C(80, 210, 255);
            // A pale accent vanishes into the empty cells; pull it towards navy until it reads.
            var lum = 0.299f * accent.r + 0.587f * accent.g + 0.114f * accent.b;
            if (lum > 0.72f) accent = Color.Lerp(accent, C(40, 60, 110), Mathf.InverseLerp(0.72f, 1f, lum) * 0.55f + 0.2f);
            var pattern = (Pattern)(Hash(def?.id) / 2 % 6);
            return new Spec(filled, accent, FrameFor(grade), pattern, owned?.awakened ?? false, LeftSide(def?.id));
        }

        /// <summary>
        /// Adds a painted sheet layer to `parent`. Add it BEFORE the portrait: UI Toolkit has no
        /// z-index, so build order puts it behind.
        /// </summary>
        public static VisualElement Add(VisualElement parent, HeroDef def, OwnedHero owned, string classes = "backsheet")
        {
            var spec = For(def, owned);
            var el = UiKit.Div(classes + (spec.Left ? " backsheet--left" : " backsheet--right"), parent);
            el.pickingMode = PickingMode.Ignore;
            ModalFrame.Painted(el, (ctx, r) => DrawHalo(ctx, r, spec));
            return el;
        }

        // ------------------------------------------------------------ the halo sheet (v3)
        //
        // Designed against Gemini mock-ups of our own party screen (tools/gemini_edit.py, halo_*):
        // what read as a halo there was a small spreadsheet GRID drawn in saturated neon line — the
        // character's colour, a soft glow of it, white only at the core — leaning in perspective
        // behind one shoulder, head-and-shoulders sized; rank as the rings and sparkles a halo
        // carries. One geometry, in a unit square (y down), drawn by both painters: SheetTexture for
        // the 3D figure (distance-field raster) and DrawHalo for the UI (strokes + glow rings).

        public struct Stroke { public Vector2 A, B; public Color C; public float Width, Glow; public bool Arc; }

        /// <summary>
        /// The sheet as strokes and washes in the unit square:
        ///   a 4×3 grid (A1:D3) leaning in perspective — frame, then fainter inner lines;
        ///   ★ + level lights columns left to right (a faint wash);
        ///   C+ corner brackets (Excel's selection), B+ the active cell A1 and its fill handle,
        ///   A an arc ring in the grade colour, S the full ring, an inner ring and sparkles.
        /// Awakened turns everything gold.
        /// </summary>
        public static void Halo(Spec s, List<Stroke> strokes, List<(List<Vector2> poly, Color c)> washes)
        {
            var dir = s.Left ? -1f : 1f;
            var gold = C(255, 204, 64);
            var ink = Neon(s.Gold ? gold : s.Accent);
            var ring = Neon(s.Gold ? gold : s.Frame);
            var grade = s.Frame == FrameFor("S") ? 4 : s.Frame == FrameFor("A") ? 3 : s.Frame == FrameFor("B") ? 2 : s.Frame == FrameFor("C") ? 1 : 0;

            var centre = new Vector2(0.5f, 0.5f);
            const float gw = 0.64f, gh = 0.42f;
            var U = new Vector2(gw, -0.1f * gw * dir);       // along the columns, rising to the outer side
            var V = new Vector2(0.16f * gh * dir, gh);        // down the rows, leaning
            var O = centre - U * 0.5f - V * 0.5f;
            Vector2 G(float u, float v) => O + U * u + V * v;
            void Line(Vector2 a, Vector2 b, Color c, float w, float glow) => strokes.Add(new Stroke { A = a, B = b, C = c, Width = w, Glow = glow });
            List<Vector2> Cell(float u0, float v0, float u1, float v1) => new() { G(u0, v0), G(u1, v0), G(u1, v1), G(u0, v1) };

            // lit columns
            for (var c = 0; c < Mathf.Min(s.Filled, CellCount); c++)
                washes.Add((Cell(c / 4f, 0f, (c + 1) / 4f, 1f), WithA(ink, 0.2f)));
            // the grid
            Line(G(0, 0), G(1, 0), ink, 4f, 10f); Line(G(1, 0), G(1, 1), ink, 4f, 10f);
            Line(G(1, 1), G(0, 1), ink, 4f, 10f); Line(G(0, 1), G(0, 0), ink, 4f, 10f);
            for (var c = 1; c < 4; c++) Line(G(c / 4f, 0), G(c / 4f, 1), WithA(ink, 0.75f), 2.2f, 6f);
            for (var r = 1; r < 3; r++) Line(G(0, r / 3f), G(1, r / 3f), WithA(ink, 0.75f), 2.2f, 6f);

            if (grade >= 1)
            {
                // Excel's selection brackets, just outside the four corners
                const float e = 0.035f, l = 0.1f;
                foreach (var (u, v) in new[] { (0f, 0f), (1f, 0f), (1f, 1f), (0f, 1f) })
                {
                    var su = u < 0.5f ? -1f : 1f; var sv = v < 0.5f ? -1f : 1f;
                    var p = G(u + su * e, v + sv * e * 1.4f);
                    Line(p, p - U.normalized * (su * l * gw), ink, 3.2f, 7f);
                    Line(p, p - V.normalized * (sv * l * gh * 1.2f), ink, 3.2f, 7f);
                }
            }
            if (grade >= 2)
            {
                // the active cell A1, heavier, and its fill handle
                var a1 = Cell(0f, 0f, 0.25f, 1f / 3f);
                for (var i = 0; i < 4; i++) Line(a1[i], a1[(i + 1) % 4], Color.Lerp(ink, Color.white, 0.3f), 5f, 10f);
                var h = G(0.25f, 1f / 3f); const float hs = 0.022f;
                washes.Add((new List<Vector2> { h + new Vector2(-hs, -hs), h + new Vector2(hs, -hs), h + new Vector2(hs, hs), h + new Vector2(-hs, hs) }, Color.Lerp(ink, Color.white, 0.4f)));
            }
            if (grade >= 3)
            {
                // the ring behind: an arc for A, the whole ring (and an inner one) for S
                var rx = 0.47f; var ry = 0.4f;
                var from = grade >= 4 ? 0f : 35f; var to = grade >= 4 ? 360f : 325f;
                Ellipse(strokes, centre, rx, ry, from, to, ring, 3.2f, 7f);
                if (grade >= 4)
                {
                    Ellipse(strokes, centre, rx * 0.9f, ry * 0.9f, 0f, 360f, WithA(ring, 0.6f), 1.8f, 4f);
                    foreach (var (a, size) in new[] { (-50f, 0.055f), (140f, 0.04f), (230f, 0.05f) })
                    {
                        var p = centre + new Vector2(Mathf.Cos(a * Mathf.Deg2Rad) * rx, Mathf.Sin(a * Mathf.Deg2Rad) * ry);
                        Line(p + new Vector2(-size, 0f), p + new Vector2(size, 0f), Color.Lerp(ring, Color.white, 0.5f), 3f, 9f);
                        Line(p + new Vector2(0f, -size * 1.3f), p + new Vector2(0f, size * 1.3f), Color.Lerp(ring, Color.white, 0.5f), 3f, 9f);
                    }
                }
            }
        }

        static void Ellipse(List<Stroke> o, Vector2 c, float rx, float ry, float fromDeg, float toDeg, Color col, float w, float glow)
        {
            const int n = 48;
            for (var i = 0; i < n; i++)
            {
                var a0 = Mathf.Lerp(fromDeg, toDeg, i / (float)n) * Mathf.Deg2Rad; var a1 = Mathf.Lerp(fromDeg, toDeg, (i + 1) / (float)n) * Mathf.Deg2Rad;
                o.Add(new Stroke { A = c + new Vector2(Mathf.Cos(a0) * rx, Mathf.Sin(a0) * ry), B = c + new Vector2(Mathf.Cos(a1) * rx, Mathf.Sin(a1) * ry), C = col, Width = w, Glow = glow, Arc = true });
            }
        }

        /// <summary>A colour made to read as neon on a light scene: saturated, bright, opaque.</summary>
        static Color Neon(Color c)
        {
            Color.RGBToHSV(c, out var h, out var sat, out var v);
            var n = Color.HSVToRGB(h, Mathf.Clamp(sat * 1.25f, 0.72f, 1f), Mathf.Clamp(v * 1.15f, 0.88f, 1f));
            n.a = 1f;
            return n;
        }
        static Color WithA(Color c, float a) { c.a *= a; return c; }

        /// <summary>
        /// The UI painter for the halo sheet: fitted as a square into r. Stroke widths are in
        /// pixels of a 320-px sheet and scale with it; each stroke is a thin quad with a glow ring.
        /// </summary>
        public static void DrawHalo(MeshGenerationContext ctx, Rect r, Spec s)
        {
            var side = Mathf.Min(r.width, r.height);
            var box = new Rect(r.center.x - side * 0.5f, r.center.y - side * 0.5f, side, side);
            // on the light UI backdrops the neon needs more body than on the 3D stage: heavier lines,
            // a stronger glow, and a core that stays in the colour rather than going to white
            var k = side / 320f * 1.35f;
            var strokes = new List<Stroke>(); var washes = new List<(List<Vector2>, Color)>();
            Halo(s, strokes, washes);
            Vector2 M(Vector2 p) => new(box.xMin + p.x * side, box.yMin + p.y * side);
            foreach (var (poly, c) in washes) UiPaint.Fill(ctx, poly.ConvertAll(M), c, 0.8f);
            // glow per stroke; not on arcs, whose many short segments would each ring and saw the curve
            foreach (var st in strokes)
            {
                if (st.Arc) continue;
                var a = M(st.A); var b = M(st.B);
                var n = new Vector2(-(b - a).y, (b - a).x).normalized * Mathf.Max(0.6f, st.Width * k * 0.5f);
                var along = (b - a).normalized * n.magnitude;
                var q = new List<Vector2> { a - along + n, b + along + n, b + along - n, a - along - n };
                UiPaint.Ring(ctx, q, WithA(st.C, 0.62f * st.C.a), WithA(st.C, 0f), st.Glow * k);
            }
            foreach (var st in strokes)
            {
                var a = M(st.A); var b = M(st.B);
                var n = new Vector2(-(b - a).y, (b - a).x).normalized * Mathf.Max(0.6f, st.Width * k * 0.5f);
                var along = (b - a).normalized * n.magnitude;
                UiPaint.Fill(ctx, new List<Vector2> { a - along + n, b + along + n, b + along - n, a - along - n }, Color.Lerp(st.C, new Color(0.1f, 0.2f, 0.4f), 0.12f), 0.6f);
            }
        }

    }
}
