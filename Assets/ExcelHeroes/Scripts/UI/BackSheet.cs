using System.Collections.Generic;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 등 뒤의 시트 — the game's own device where the reference has a halo: a translucent
    /// spreadsheet floating behind every hero, leaning out past one shoulder, that fills up as the
    /// hero grows. Drawn by the game, never by the illustration (see the web repo's
    /// docs/CHARACTER_DESIGN.md and src/data/design.js — this is the same rule set):
    ///
    ///   grade      → columns (D 3 … S 7) and chrome (header row, formula bar, gold frame, tabs)
    ///   ★          → open rows (one per star, five at ★5)
    ///   level/cap  → filled cells, left to right, top to bottom, across the open rows
    ///   awakened   → the filled cells turn gold
    ///   skill lv   → the bar chart's heights (A and up)
    ///   trait      → a cell pattern, one spreadsheet feature per trait
    ///   accent     → the character's palette.W tints the whole sheet
    ///   side       → left or right, fixed per id by the same hash the web uses
    ///
    /// Behind the hero, never above the head: its top edge sits between the shoulder and the ear.
    /// </summary>
    public static class BackSheet
    {
        static Color C(int r, int g, int b, float a = 1f) => UiPaint.C(r, g, b, a);

        public readonly struct Spec
        {
            public readonly int Cols, Rows, Filled;
            public readonly bool Gold, HeaderRow, HeaderCol, FormulaBar, GoldFrame, Chart, Tabs;
            public readonly string Trait;
            public readonly Color Accent;
            public readonly bool Left;
            public readonly int Bars;

            public Spec(int cols, int rows, int filled, bool gold, string grade, string trait, Color accent, bool left, int bars)
            {
                Cols = cols; Rows = rows; Filled = filled; Gold = gold; Trait = trait; Accent = accent; Left = left; Bars = bars;
                var rank = "DCBAS".IndexOf(grade ?? "C");
                HeaderRow = rank >= 1; HeaderCol = rank >= 2; FormulaBar = rank >= 2;
                GoldFrame = rank >= 3; Chart = rank >= 3; Tabs = rank >= 4;
            }
        }

        public const int RowsMax = 5;

        static int ColsFor(string grade) => grade switch { "D" => 3, "C" => 4, "B" => 5, "A" => 6, "S" => 7, _ => 4 };

        /// <summary>Sum of the id's UTF-16 code units — the same hash as design.js, so the side agrees.</summary>
        public static int Hash(string id)
        {
            var h = 0;
            foreach (var ch in id ?? "") h += ch;
            return h;
        }

        public static bool LeftSide(string id) => Hash(id) % 2 == 0;

        public static Spec For(HeroDef def, OwnedHero owned)
        {
            var grade = def?.grade ?? "D";
            var cols = ColsFor(grade);
            var star = Mathf.Clamp(owned?.star ?? 1, 1, RowsMax);
            var cap = owned != null ? Mathf.Max(1, StatMath.LevelCap(owned)) : 80;
            var ratio = Mathf.Clamp01((owned?.level ?? 0) / (float)cap);
            var filled = Mathf.RoundToInt(cols * star * ratio);
            var accent = ColorUtility.TryParseHtmlString(def?.colorAccent ?? "", out var c) ? c : C(80, 210, 255);
            // A pale accent (cream, light yellow) vanishes into the white cells, and a filled cell
            // that cannot be told from an empty one defeats the sheet. Past a luminance of 0.72
            // the accent is pulled toward navy until it reads.
            var lum = 0.299f * accent.r + 0.587f * accent.g + 0.114f * accent.b;
            if (lum > 0.72f) accent = Color.Lerp(accent, C(40, 60, 110), Mathf.InverseLerp(0.72f, 1f, lum) * 0.55f + 0.2f);
            return new Spec(cols, star, filled, owned?.awakened ?? false, grade, def?.trait, accent, LeftSide(def?.id), owned?.skillLv ?? 0);
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

        public static void Draw(MeshGenerationContext ctx, Rect r, Spec s)
        {
            var slant = SkewPlate.SlantFor(r.height) * 0.35f * (s.Left ? -1f : 1f);
            var accent = s.Accent;
            var glass = Color.Lerp(Color.white, accent, 0.18f);

            // S: two more tabs of the same sheet stacked behind, offset up and out.
            if (s.Tabs)
                for (var t = 2; t >= 1; t--)
                {
                    var off = new Vector2((s.Left ? -1 : 1) * 18f * t, -14f * t);
                    var back = Sheet(new Rect(r.position + off, r.size), slant, 10f);
                    UiPaint.Fill(ctx, back, UiPaint.WithAlpha(glass, 0.28f));
                    UiPaint.Stroke(ctx, back, UiPaint.WithAlpha(accent, 0.55f), 2f);
                }

            var outer = Sheet(r, slant, 10f);
            UiPaint.Ring(ctx, outer, UiPaint.WithAlpha(accent, 0.35f), UiPaint.WithAlpha(accent, 0f), 14f);
            UiPaint.Fill(ctx, outer, UiPaint.Vertical(UiPaint.WithAlpha(glass, 0.62f), UiPaint.WithAlpha(glass, 0.42f), r.yMin, r.yMax));
            UiPaint.Stroke(ctx, outer, s.GoldFrame ? C(255, 206, 60, 0.95f) : UiPaint.WithAlpha(accent, 0.9f), s.GoldFrame ? 4f : 3f);

            // Layout inside the sheet, top to bottom: formula bar, header row, the grid.
            var pad = 12f;
            var inner = new Rect(r.xMin + pad, r.yMin + pad, r.width - pad * 2, r.height - pad * 2);
            var y = inner.yMin;
            if (s.FormulaBar)
            {
                var fb = new Rect(inner.xMin, y, inner.width, inner.height * 0.1f);
                UiPaint.Fill(ctx, Cell(fb, slant, r), UiPaint.WithAlpha(Color.white, 0.75f));
                // "fx" mark and a formula line: a small accent block and a long thin bar.
                UiPaint.Fill(ctx, Cell(new Rect(fb.xMin + 4f, fb.yMin + 4f, fb.height - 8f, fb.height - 8f), slant, r), UiPaint.WithAlpha(accent, 0.9f));
                if (s.Gold)
                    UiPaint.Fill(ctx, Cell(new Rect(fb.xMin + fb.height + 4f, fb.center.y - 2f, fb.width * 0.55f, 4f), slant, r), C(230, 170, 20, 0.9f));
                y += fb.height + 6f;
            }

            var chartW = s.Chart ? inner.width * 0.22f : 0f;
            var gridX = inner.xMin + (s.HeaderCol ? inner.width * 0.07f : 0f);
            var gridW = inner.width - (gridX - inner.xMin) - chartW - (s.Chart ? 8f : 0f);
            if (s.HeaderRow)
            {
                var hr = new Rect(gridX, y, gridW, inner.height * 0.08f);
                UiPaint.Fill(ctx, Cell(hr, slant, r), UiPaint.WithAlpha(accent, 0.35f));
                y += hr.height + 3f;
            }

            var rowsShown = RowsMax;
            var rowTotal = s.Trait == "rally" ? 1 : 0;   // 팀 리더십: a bold total row under the grid
            var gridH = inner.yMax - y - (rowTotal > 0 ? inner.height * 0.1f : 0f);
            var cw = gridW / s.Cols;
            var ch = gridH / rowsShown;
            if (s.HeaderCol)
                UiPaint.Fill(ctx, Cell(new Rect(inner.xMin, y, gridX - inner.xMin - 3f, gridH), slant, r), UiPaint.WithAlpha(accent, 0.25f));

            var fillColor = s.Gold ? C(255, 200, 40, 0.9f) : UiPaint.WithAlpha(accent, 0.8f);
            var idx = 0;
            for (var row = 0; row < rowsShown; row++)
            {
                var open = row < s.Rows;
                for (var col = 0; col < s.Cols; col++, idx += open ? 1 : 0)
                {
                    var cell = new Rect(gridX + col * cw + 1.5f, y + row * ch + 1.5f, cw - 3f, ch - 3f);
                    // 전체 회신: cells merge in pairs across the row.
                    if (s.Trait == "splash" && col % 2 == 0 && col + 1 < s.Cols) cell.width = cw * 2 - 3f;
                    else if (s.Trait == "splash" && col % 2 == 1) continue;
                    var poly = Cell(cell, slant, r);
                    if (!open)
                    {
                        UiPaint.Fill(ctx, poly, C(120, 130, 150, 0.12f), 0.8f);
                        continue;
                    }
                    var filled = idx < s.Filled;
                    UiPaint.Fill(ctx, poly, filled ? CellColor(s, idx, fillColor) : UiPaint.WithAlpha(Color.white, 0.55f), 0.8f);
                    if (filled) TraitMark(ctx, s, cell, slant, r, idx);
                }
            }

            if (rowTotal > 0)
            {
                var tr = new Rect(gridX, y + gridH + 3f, gridW, inner.height * 0.1f - 3f);
                UiPaint.Fill(ctx, Cell(tr, slant, r), UiPaint.WithAlpha(accent, 0.85f));
            }
            // 철벽 멘탈: a heavy outer border and a freeze-pane line.
            if (s.Trait == "sturdy")
            {
                UiPaint.Stroke(ctx, Cell(new Rect(gridX, y, gridW, gridH), slant, r), UiPaint.WithAlpha(accent, 1f), 4f);
                UiPaint.Fill(ctx, Cell(new Rect(gridX + cw - 2f, y, 3f, gridH), slant, r), C(40, 60, 100, 0.8f));
            }

            // A and up: a small bar chart beside the grid; skill level sets the bar heights.
            if (s.Chart)
            {
                var cr = new Rect(inner.xMax - chartW, y, chartW, gridH);
                UiPaint.Fill(ctx, Cell(cr, slant, r), UiPaint.WithAlpha(Color.white, 0.6f));
                for (var b = 0; b < 4; b++)
                {
                    var hgt = Mathf.Clamp01(0.25f + 0.15f * (b + s.Bars)) * (cr.height - 10f);
                    var bw = (cr.width - 10f) / 4f;
                    var bar = new Rect(cr.xMin + 5f + b * bw + 2f, cr.yMax - 5f - hgt, bw - 4f, hgt);
                    UiPaint.Fill(ctx, Cell(bar, slant, r), s.Gold ? C(240, 180, 30, 0.9f) : UiPaint.WithAlpha(accent, 0.9f));
                }
            }
        }

        static Color CellColor(Spec s, int idx, Color fill) => s.Trait switch
        {
            // 날카로운 지적: top-10% conditional format — the odd cell burns red.
            "crit" when idx % 4 == 2 => C(230, 70, 70, 0.9f),
            // 영업 마인드: currency format in gold.
            "greedy" => C(250, 196, 50, 0.88f),
            _ => fill,
        };

        static void TraitMark(MeshGenerationContext ctx, Spec s, Rect cell, float slant, Rect r, int idx)
        {
            switch (s.Trait)
            {
                case "swift":   // icon set: an arrow in every cell
                {
                    var c = cell.center;
                    var k = Mathf.Min(cell.width, cell.height) * 0.18f;
                    UiPaint.Fill(ctx, new List<Vector2> { Map(c + new Vector2(-k, -k), r, slant), Map(c + new Vector2(k, 0f), r, slant), Map(c + new Vector2(-k, k), r, slant) }, C(255, 255, 255, 0.95f));
                    break;
                }
                case "lifesteal":   // coffee data bar
                case "regen":       // green data bar
                {
                    var len = 0.35f + 0.6f * ((idx * 37) % 10) / 10f;
                    var bar = new Rect(cell.xMin + 2f, cell.center.y - cell.height * 0.2f, (cell.width - 4f) * len, cell.height * 0.4f);
                    UiPaint.Fill(ctx, Cell(bar, slant, r), s.Trait == "regen" ? C(60, 190, 110, 0.95f) : C(140, 90, 50, 0.95f));
                    break;
                }
                case "focus":   // comment marker in the corner
                {
                    var k = Mathf.Min(cell.width, cell.height) * 0.35f;
                    UiPaint.Fill(ctx, new List<Vector2> { Map(new Vector2(cell.xMax - k, cell.yMin), r, slant), Map(new Vector2(cell.xMax, cell.yMin), r, slant), Map(new Vector2(cell.xMax, cell.yMin + k), r, slant) }, C(220, 50, 50, 0.95f));
                    break;
                }
                case "lucky":   // a star dot
                    UiPaint.Fill(ctx, UiPaint.Ellipse(Map(cell.center, r, slant), cell.height * 0.18f, cell.height * 0.18f, 10), C(255, 230, 90, 0.98f));
                    break;
            }
        }

        /// <summary>
        /// One shear for the frame and every cell in it. A point's height decides how far its row
        /// is pushed sideways, so the grid leans exactly with its frame: positive `slant` leans the
        /// top to the right (`/`), negative leans it left (`\`), and the width shrinks by the lean
        /// so the sheet stays inside its box either way.
        /// </summary>
        static Vector2 Map(Vector2 p, Rect sheet, float slant)
        {
            var t = Mathf.Clamp01((p.y - sheet.yMin) / sheet.height);
            var a = Mathf.Abs(slant);
            var u = (p.x - sheet.xMin) / sheet.width;
            var left = slant >= 0f ? sheet.xMin + a * (1f - t) : sheet.xMin + a * t;
            return new Vector2(left + u * (sheet.width - a), p.y);
        }

        /// <summary>The sheet's outline: a rounded parallelogram leaning away from the hero.</summary>
        static List<Vector2> Sheet(Rect r, float slant, float radius)
            => UiPaint.Round(new[]
            {
                Map(new Vector2(r.xMin, r.yMin), r, slant), Map(new Vector2(r.xMax, r.yMin), r, slant),
                Map(new Vector2(r.xMax, r.yMax), r, slant), Map(new Vector2(r.xMin, r.yMax), r, slant),
            }, radius, 4);

        /// <summary>A cell sheared with the sheet.</summary>
        static List<Vector2> Cell(Rect c, float slant, Rect sheet) => new List<Vector2>
        {
            Map(new Vector2(c.xMin, c.yMin), sheet, slant), Map(new Vector2(c.xMax, c.yMin), sheet, slant),
            Map(new Vector2(c.xMax, c.yMax), sheet, slant), Map(new Vector2(c.xMin, c.yMax), sheet, slant),
        };
    }
}
