using System.Collections.Generic;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 등 뒤의 시트 — the game's own device where the reference has a halo: one row of four
    /// cells (A1:D1) floating behind the hero's shoulder, drawn by the game, never by the art.
    ///
    ///   ★ + level   → how many of the four cells are filled (at least one once owned)
    ///   accent      → the filled cells' colour, shifted a little from cell to cell
    ///   id          → the solid pattern inside every filled cell (stripes, dots, check, …)
    ///   grade       → the frame (D grey, C green, B blue, A purple, S gold)
    ///   awakened    → the filled cells turn gold
    ///   side        → left or right shoulder, fixed per id by the same hash the web uses
    ///
    /// It used to be a whole spreadsheet (header rows, formula bar, chart); at the size it is seen
    /// it read as noise. Four cells read at a glance.
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

        /// <summary>The colour of cell i: the accent, walked a few degrees round the hue wheel.</summary>
        public static Color CellColor(Spec s, int i)
        {
            if (s.Gold) return Color.Lerp(C(255, 214, 70), C(255, 180, 30), i / 3f);
            Color.RGBToHSV(s.Accent, out var h, out var sat, out var v);
            return Color.HSVToRGB(Mathf.Repeat(h + (i - 1.5f) * 0.045f, 1f), Mathf.Clamp01(sat * 0.95f + 0.05f), Mathf.Clamp01(v * (1.02f - i * 0.04f)));
        }

        /// <summary>Is point (u, v) — 0..1 inside a cell — on the pattern's ink?</summary>
        public static bool Ink(Pattern p, float u, float v)
        {
            const float k = 5f;
            switch (p)
            {
                case Pattern.Stripes: return Mathf.Repeat(v * k, 1f) < 0.38f;
                case Pattern.Dots:
                {
                    var du = Mathf.Repeat(u * k, 1f) - 0.5f; var dv = Mathf.Repeat(v * k, 1f) - 0.5f;
                    return du * du + dv * dv < 0.07f;
                }
                case Pattern.Check: return (Mathf.FloorToInt(u * 4f) + Mathf.FloorToInt(v * 4f)) % 2 == 0;
                case Pattern.Diagonal: return Mathf.Repeat((u + v) * k * 0.8f, 1f) < 0.35f;
                case Pattern.Grid: return Mathf.Repeat(u * k, 1f) < 0.16f || Mathf.Repeat(v * k, 1f) < 0.16f;
                default: return Mathf.Repeat(v * k + Mathf.Sin(u * Mathf.PI * 4f) * 0.25f, 1f) < 0.36f;
            }
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
            ModalFrame.Painted(el, (ctx, r) => Draw(ctx, r, spec));
            return el;
        }

        /// <summary>The strip, fitted into r at its own aspect (about 4.5 : 1) and centred.</summary>
        public static void Draw(MeshGenerationContext ctx, Rect r, Spec s)
        {
            const float aspect = 4.5f;
            var w = Mathf.Min(r.width, r.height * aspect);
            var h = w / aspect;
            var box = new Rect(r.center.x - w * 0.5f, r.center.y - h * 0.5f, w, h);
            var slant = h * 0.18f * (s.Left ? -1f : 1f);

            var outer = UiPaint.SkewRect(box, slant, Mathf.Min(8f, h * 0.12f));
            UiPaint.Ring(ctx, outer, UiPaint.WithAlpha(s.Frame, 0.4f), UiPaint.WithAlpha(s.Frame, 0f), Mathf.Min(14f, h * 0.2f));
            UiPaint.Fill(ctx, outer, C(255, 255, 255, 0.42f));
            UiPaint.Stroke(ctx, outer, UiPaint.WithAlpha(s.Frame, 0.95f), Mathf.Clamp(h * 0.05f, 2f, 4f));

            var pad = h * 0.1f;
            var gap = h * 0.07f;
            var cw = (w - pad * 2f - gap * (CellCount - 1)) / CellCount;
            var ch = h - pad * 2f;
            for (var i = 0; i < CellCount; i++)
            {
                var cell = new Rect(box.xMin + pad + i * (cw + gap), box.yMin + pad, cw, ch);
                var poly = Skew(cell, box, slant);
                if (i >= s.Filled)
                {
                    UiPaint.Fill(ctx, poly, C(255, 255, 255, 0.5f), 0.8f);
                    UiPaint.Stroke(ctx, poly, C(150, 170, 196, 0.5f), 1.2f);
                    continue;
                }
                var col = CellColor(s, i);
                UiPaint.Fill(ctx, poly, UiPaint.WithAlpha(col, 0.92f), 0.8f);
                // the pattern: the same hue, a step lighter, as small quads clipped to the cell
                var ink = UiPaint.WithAlpha(Color.Lerp(col, Color.white, 0.38f), 0.9f);
                const int n = 10;
                for (var yy = 0; yy < n; yy++)
                    for (var xx = 0; xx < n; xx++)
                    {
                        var u = (xx + 0.5f) / n; var v = (yy + 0.5f) / n;
                        if (!Ink(s.Pattern, u, v)) continue;
                        var q = new Rect(cell.xMin + xx * cell.width / n, cell.yMin + yy * cell.height / n, cell.width / n + 0.3f, cell.height / n + 0.3f);
                        UiPaint.Fill(ctx, UiPaint.Clip(Skew(q, box, slant), poly), ink, 0f);
                    }
                UiPaint.Stroke(ctx, poly, UiPaint.WithAlpha(Color.Lerp(col, Color.black, 0.25f), 0.8f), 1.2f);
            }
        }

        /// <summary>A rect inside the strip, sheared with it (x moves with height).</summary>
        static List<Vector2> Skew(Rect q, Rect box, float slant)
        {
            float Sx(float x, float y) => x + slant * (1f - (y - box.yMin) / box.height) - slant * 0.5f;
            return new List<Vector2>
            {
                new(Sx(q.xMin, q.yMin), q.yMin), new(Sx(q.xMax, q.yMin), q.yMin),
                new(Sx(q.xMax, q.yMax), q.yMax), new(Sx(q.xMin, q.yMax), q.yMax),
            };
        }
    }
}
