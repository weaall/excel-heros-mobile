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
            /// <summary>The hero's own mark in A1 (Sigs) — what tells two sheets apart at a glance.</summary>
            public readonly string Glyph;

            public Spec(int filled, Color accent, Color frame, Pattern pattern, bool gold, bool left, string glyph = null)
            {
                Filled = Mathf.Clamp(filled, 0, CellCount); Accent = accent; Frame = frame; Pattern = pattern; Gold = gold; Left = left; Glyph = glyph;
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
            // each hero's own colour and mark (Sigs); the art's accent only for an id not listed
            var sigId = def == null ? null : def.id == GameData.MainId || GameData.MainJob(def.id) != null ? "main" : def.id;
            var sig = sigId != null && Sigs.TryGetValue(sigId, out var sg) ? sg : ("", def?.colorAccent ?? "");
            var accent = ColorUtility.TryParseHtmlString(sig.Item2, out var c) ? c : C(80, 210, 255);
            // A pale accent vanishes into the empty cells; pull it towards navy until it reads.
            var lum = 0.299f * accent.r + 0.587f * accent.g + 0.114f * accent.b;
            if (lum > 0.72f) accent = Color.Lerp(accent, C(40, 60, 110), Mathf.InverseLerp(0.72f, 1f, lum) * 0.55f + 0.2f);
            var pattern = (Pattern)(Hash(def?.id) / 2 % 6);
            return new Spec(filled, accent, FrameFor(grade), pattern, owned?.awakened ?? false, LeftSide(def?.id), sig.Item1);
        }

        /// <summary>
        /// Every hero's sheet mark: a line icon of what they do, drawn in A1, and a colour of their
        /// own — the grid stays one simple row of four, the mark is the character. No two share
        /// both; colours are picked apart on the hue wheel where the jobs would otherwise collide.
        /// </summary>
        static readonly Dictionary<string, (string, string)> Sigs = new()
        {
            ["staff_park"] = ("pen", "#4F8CFF"),        ["parttime"] = ("clock", "#55E6C1"),
            ["guard"] = ("shield", "#5E7CE2"),          ["barista"] = ("cup", "#E58E4F"),
            ["courier"] = ("box", "#F5A623"),           ["contract"] = ("calendar", "#6F8FB5"),
            ["vlookup"] = ("search", "#00B894"),        ["pivot"] = ("swap", "#2ED573"),
            ["macro"] = ("gear", "#00CEC9"),            ["hr_jung"] = ("heart", "#FF6B81"),
            ["audit_han"] = ("check", "#6C5CE7"),       ["acct_lead"] = ("sum", "#C4E538"),
            ["dev_lead"] = ("code", "#0FB9B1"),         ["ga_lead"] = ("key", "#54A0FF"),
            ["welfare"] = ("gift", "#FF7EDB"),          ["cfo"] = ("won", "#F9CA24"),
            ["cto"] = ("bolt", "#00D2A0"),              ["coo"] = ("target", "#3C8CE7"),
            ["ceo"] = ("crown", "#FF5E57"),             ["chairman"] = ("diamond", "#7158E2"),
            ["helpdesk"] = ("at", "#48DBFB"),           ["cleaner"] = ("sparkle", "#F8A5C2"),
            ["sales_kang"] = ("trend", "#FFA502"),      ["legal_yoon"] = ("scale", "#A29BFE"),
            ["pm_lead"] = ("list", "#1DD1A1"),          ["design_lead"] = ("shapes", "#FF8FC7"),
            ["cmo"] = ("megaphone", "#FF4D6D"),         ["founder"] = ("rocket", "#EE5A24"),
            ["intern_seo"] = ("star", "#FD79A8"),       ["pr_yoo"] = ("bubble", "#E84393"),
            ["nurse_han"] = ("cross", "#7BED9F"),       ["lab_park"] = ("flask", "#0984E3"),
            ["chro"] = ("link", "#FFB142"),             ["cso"] = ("compass", "#8C7AE6"),
            ["ai_lead"] = ("nodes", "#18DCFF"),         ["union_chief"] = ("flag", "#FF3F34"),
            ["hacker"] = ("unlock", "#32FF7E"),         ["intern_min"] = ("bookmark", "#FFA8A8"),
            ["security_yang"] = ("lock", "#70A1FF"),    ["mail_cho"] = ("envelope", "#7ED6DF"),
            ["qa_lee"] = ("bug", "#26DE81"),            ["reception_go"] = ("bell", "#FDA7DF"),
            ["trainer_seok"] = ("book", "#45AAF2"),     ["translator_ji"] = ("globe", "#4BCFFA"),
            ["secretary_yun"] = ("clipboard", "#D980FA"), ["logistics_bae"] = ("route", "#6AB04C"),
            ["cro"] = ("warning", "#B33771"),           ["cpo"] = ("layers", "#1ABC9C"),
            ["ir_lead"] = ("pie", "#C56CF0"),           ["labor_atty"] = ("gavel", "#FF7F50"),
            ["bd_lead"] = ("bars", "#3B3B98"),          ["cdo"] = ("db", "#0ABDE3"),
            ["cco"] = ("smile", "#FF6FA8"),             ["chief_of_staff"] = ("hourglass", "#82CCDD"),
            ["chairwoman"] = ("medal", "#D6A2E8"),
            // the player's own 김인턴 and every job they rise to: the ID card on the lanyard
            ["main"] = ("idcard", "#00A8FF"),
        };

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
        ///   one row of four cells (A1:D1) leaning in perspective — frame, then fainter inner lines;
        ///   the hero's own mark (Sigs) in A1, in the hero's own colour;
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
            // one row of four (A1:D1): the cells a little wider than tall
            const float gw = 0.64f, gh = 0.16f;
            var U = new Vector2(gw, -0.1f * gw * dir);       // along the columns, rising to the outer side
            var V = new Vector2(0.3f * gh * dir, gh);         // down the row, leaning
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

            // the hero's mark in A1: a few lines of light, the same colour a touch whiter
            if (!string.IsNullOrEmpty(s.Glyph))
            {
                var gc = Color.Lerp(ink, Color.white, 0.25f);
                Vector2 Q(float x, float y) => G(0.25f * (0.08f + 0.84f * x), 0.08f + 0.84f * y);
                Glyph(s.Glyph, Q, (a, b) => Line(a, b, gc, 3.4f, 7f));
            }

            if (grade >= 1)
            {
                // Excel's selection brackets, just outside the four corners
                const float e = 0.035f, l = 0.1f;
                foreach (var (u, v) in new[] { (0f, 0f), (1f, 0f), (1f, 1f), (0f, 1f) })
                {
                    var su = u < 0.5f ? -1f : 1f; var sv = v < 0.5f ? -1f : 1f;
                    var p = G(u + su * e, v + sv * e * 2.2f);
                    Line(p, p - U.normalized * (su * l * gw), ink, 3.2f, 7f);
                    Line(p, p - V.normalized * (sv * 0.045f), ink, 3.2f, 7f);
                }
            }
            if (grade >= 2)
            {
                // the active cell A1, heavier, and its fill handle
                var a1 = Cell(0f, 0f, 0.25f, 1f);
                for (var i = 0; i < 4; i++) Line(a1[i], a1[(i + 1) % 4], Color.Lerp(ink, Color.white, 0.3f), 5f, 10f);
                var h = G(0.25f, 1f); const float hs = 0.022f;
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

        /// <summary>
        /// The marks, in a unit cell (y down) mapped through `Q`, as straight segments — arcs are cut
        /// into short ones. Kept to a handful of strokes each so they read at badge size.
        /// </summary>
        static void Glyph(string name, System.Func<float, float, Vector2> Q, System.Action<Vector2, Vector2> seg)
        {
            void L(float x0, float y0, float x1, float y1) => seg(Q(x0, y0), Q(x1, y1));
            void P(params float[] xy) { for (var i = 0; i < xy.Length; i += 2) { var j = (i + 2) % xy.Length; L(xy[i], xy[i + 1], xy[j], xy[j + 1]); } }
            void Arc(float cx, float cy, float rx, float ry, float from = 0f, float to = 360f)
            {
                var n = Mathf.Max(6, Mathf.RoundToInt(Mathf.Abs(to - from) / 20f));
                for (var i = 0; i < n; i++)
                {
                    var a0 = Mathf.Lerp(from, to, i / (float)n) * Mathf.Deg2Rad; var a1 = Mathf.Lerp(from, to, (i + 1) / (float)n) * Mathf.Deg2Rad;
                    L(cx + Mathf.Cos(a0) * rx, cy + Mathf.Sin(a0) * ry, cx + Mathf.Cos(a1) * rx, cy + Mathf.Sin(a1) * ry);
                }
            }
            void O(float cx, float cy, float r) => Arc(cx, cy, r, r);
            void R(float x0, float y0, float x1, float y1) => P(x0, y0, x1, y0, x1, y1, x0, y1);
            switch (name)
            {
                case "pen": L(.28f, .72f, .72f, .28f); L(.62f, .2f, .8f, .38f); L(.28f, .72f, .2f, .8f); break;
                case "clock": O(.5f, .5f, .32f); L(.5f, .5f, .5f, .28f); L(.5f, .5f, .66f, .58f); break;
                case "shield": P(.5f, .18f, .8f, .3f, .75f, .6f, .5f, .82f, .25f, .6f, .2f, .3f); break;
                case "cup": L(.22f, .34f, .66f, .34f); L(.24f, .34f, .3f, .78f); L(.3f, .78f, .6f, .78f); L(.6f, .78f, .66f, .34f); Arc(.68f, .54f, .12f, .12f, -90f, 90f); break;
                case "box": R(.22f, .3f, .78f, .78f); L(.22f, .44f, .78f, .44f); L(.44f, .3f, .44f, .44f); L(.56f, .3f, .56f, .44f); break;
                case "calendar": R(.2f, .28f, .8f, .8f); L(.2f, .42f, .8f, .42f); L(.36f, .18f, .36f, .34f); L(.64f, .18f, .64f, .34f); break;
                case "search": O(.42f, .42f, .22f); L(.58f, .58f, .8f, .8f); break;
                case "swap": L(.22f, .36f, .78f, .36f); L(.78f, .36f, .64f, .24f); L(.78f, .64f, .22f, .64f); L(.22f, .64f, .36f, .76f); break;
                case "gear":
                    O(.5f, .5f, .17f);
                    for (var k = 0; k < 8; k++) { var a = k * 45f * Mathf.Deg2Rad; L(.5f + Mathf.Cos(a) * .24f, .5f + Mathf.Sin(a) * .24f, .5f + Mathf.Cos(a) * .34f, .5f + Mathf.Sin(a) * .34f); }
                    break;
                case "heart": Arc(.36f, .4f, .14f, .14f, 160f, 360f); Arc(.64f, .4f, .14f, .14f, 180f, 380f); L(.23f, .46f, .5f, .8f); L(.77f, .46f, .5f, .8f); break;
                case "check": L(.2f, .52f, .42f, .74f); L(.42f, .74f, .8f, .28f); break;
                case "sum": L(.74f, .22f, .28f, .22f); L(.28f, .22f, .54f, .5f); L(.54f, .5f, .28f, .78f); L(.28f, .78f, .74f, .78f); break;
                case "code": L(.34f, .3f, .16f, .5f); L(.16f, .5f, .34f, .7f); L(.66f, .3f, .84f, .5f); L(.84f, .5f, .66f, .7f); L(.57f, .24f, .43f, .76f); break;
                case "key": O(.32f, .5f, .15f); L(.47f, .5f, .84f, .5f); L(.72f, .5f, .72f, .64f); L(.82f, .5f, .82f, .6f); break;
                case "gift": R(.22f, .4f, .78f, .8f); L(.5f, .4f, .5f, .8f); Arc(.4f, .3f, .1f, .08f); Arc(.6f, .3f, .1f, .08f); break;
                case "won": L(.16f, .26f, .3f, .76f); L(.3f, .76f, .5f, .36f); L(.5f, .36f, .7f, .76f); L(.7f, .76f, .84f, .26f); L(.18f, .46f, .82f, .46f); L(.2f, .58f, .8f, .58f); break;
                case "bolt": L(.6f, .16f, .32f, .54f); L(.32f, .54f, .58f, .5f); L(.58f, .5f, .4f, .84f); break;
                case "target": O(.5f, .5f, .33f); O(.5f, .5f, .18f); O(.5f, .5f, .04f); break;
                case "crown": P(.2f, .72f, .2f, .32f, .36f, .52f, .5f, .24f, .64f, .52f, .8f, .32f, .8f, .72f); break;
                case "diamond": P(.5f, .82f, .18f, .42f, .32f, .22f, .68f, .22f, .82f, .42f); L(.18f, .42f, .82f, .42f); break;
                case "at": O(.5f, .5f, .13f); Arc(.5f, .5f, .32f, .32f, 20f, 330f); L(.63f, .44f, .63f, .6f); break;
                case "sparkle": P(.5f, .14f, .58f, .42f, .86f, .5f, .58f, .58f, .5f, .86f, .42f, .58f, .14f, .5f, .42f, .42f); break;
                case "trend": L(.18f, .74f, .42f, .5f); L(.42f, .5f, .56f, .62f); L(.56f, .62f, .82f, .3f); L(.82f, .3f, .66f, .3f); L(.82f, .3f, .82f, .46f); break;
                case "scale": L(.5f, .2f, .5f, .8f); L(.3f, .8f, .7f, .8f); L(.2f, .32f, .8f, .32f); P(.2f, .32f, .1f, .56f, .3f, .56f); P(.8f, .32f, .7f, .56f, .9f, .56f); break;
                case "list": foreach (var y in new[] { .28f, .5f, .72f }) { L(.18f, y, .26f, y); L(.36f, y, .82f, y); } break;
                case "shapes": O(.32f, .34f, .15f); P(.7f, .2f, .86f, .48f, .54f, .48f); R(.4f, .6f, .66f, .84f); break;
                case "megaphone": P(.18f, .4f, .34f, .4f, .76f, .2f, .76f, .8f, .34f, .6f, .18f, .6f); L(.34f, .6f, .42f, .82f); break;
                case "rocket": L(.22f, .78f, .78f, .22f); L(.78f, .22f, .5f, .22f); L(.78f, .22f, .78f, .5f); L(.28f, .54f, .46f, .72f); break;
                case "star":
                    {
                        var pts = new float[20];
                        for (var k = 0; k < 10; k++) { var a = (-90f + k * 36f) * Mathf.Deg2Rad; var rr = k % 2 == 0 ? .36f : .15f; pts[k * 2] = .5f + Mathf.Cos(a) * rr; pts[k * 2 + 1] = .54f + Mathf.Sin(a) * rr; }
                        P(pts);
                    }
                    break;
                case "bubble": P(.18f, .24f, .82f, .24f, .82f, .62f, .46f, .62f, .28f, .8f, .32f, .62f, .18f, .62f); break;
                case "cross": P(.4f, .18f, .6f, .18f, .6f, .4f, .82f, .4f, .82f, .6f, .6f, .6f, .6f, .82f, .4f, .82f, .4f, .6f, .18f, .6f, .18f, .4f, .4f, .4f); break;
                case "flask": L(.4f, .18f, .6f, .18f); L(.44f, .18f, .44f, .42f); L(.56f, .18f, .56f, .42f); L(.44f, .42f, .22f, .8f); L(.56f, .42f, .78f, .8f); L(.22f, .8f, .78f, .8f); L(.32f, .64f, .68f, .64f); break;
                case "link": Arc(.36f, .5f, .2f, .12f); Arc(.64f, .5f, .2f, .12f); break;
                case "compass": O(.5f, .5f, .33f); P(.5f, .24f, .58f, .5f, .5f, .76f, .42f, .5f); break;
                case "nodes": O(.26f, .7f, .08f); O(.52f, .26f, .08f); O(.78f, .64f, .08f); L(.31f, .64f, .47f, .32f); L(.57f, .32f, .74f, .57f); L(.34f, .69f, .7f, .65f); break;
                case "flag": L(.28f, .16f, .28f, .84f); P(.28f, .2f, .8f, .32f, .28f, .52f); break;
                case "unlock": R(.26f, .48f, .74f, .82f); Arc(.5f, .48f, .16f, .2f, 180f, 330f); break;
                case "lock": R(.26f, .48f, .74f, .82f); Arc(.5f, .48f, .16f, .2f, 180f, 360f); L(.5f, .6f, .5f, .7f); break;
                case "bookmark": P(.3f, .18f, .7f, .18f, .7f, .82f, .5f, .64f, .3f, .82f); break;
                case "envelope": R(.18f, .28f, .82f, .74f); L(.18f, .28f, .5f, .54f); L(.5f, .54f, .82f, .28f); break;
                case "bug": Arc(.5f, .56f, .17f, .23f); O(.5f, .27f, .08f); L(.5f, .34f, .5f, .78f); L(.34f, .48f, .18f, .4f); L(.66f, .48f, .82f, .4f); L(.34f, .64f, .18f, .72f); L(.66f, .64f, .82f, .72f); break;
                case "bell": Arc(.5f, .5f, .22f, .26f, 180f, 360f); L(.28f, .5f, .22f, .72f); L(.72f, .5f, .78f, .72f); L(.18f, .72f, .82f, .72f); O(.5f, .8f, .05f); break;
                case "book": L(.5f, .3f, .5f, .8f); P(.16f, .24f, .5f, .3f, .5f, .8f, .16f, .72f); P(.84f, .24f, .5f, .3f, .5f, .8f, .84f, .72f); break;
                case "globe": O(.5f, .5f, .33f); Arc(.5f, .5f, .14f, .33f); L(.17f, .5f, .83f, .5f); break;
                case "clipboard": R(.24f, .24f, .76f, .84f); R(.4f, .16f, .6f, .3f); L(.36f, .48f, .64f, .48f); L(.36f, .64f, .64f, .64f); break;
                case "route": O(.24f, .74f, .07f); O(.76f, .26f, .07f); L(.3f, .72f, .62f, .64f); L(.62f, .64f, .4f, .44f); L(.4f, .44f, .7f, .3f); break;
                case "warning": P(.5f, .16f, .86f, .8f, .14f, .8f); L(.5f, .38f, .5f, .6f); O(.5f, .7f, .025f); break;
                case "layers": P(.5f, .2f, .84f, .38f, .5f, .56f, .16f, .38f); L(.16f, .54f, .5f, .72f); L(.5f, .72f, .84f, .54f); L(.16f, .7f, .5f, .88f); L(.5f, .88f, .84f, .7f); break;
                case "pie": O(.5f, .5f, .33f); L(.5f, .5f, .5f, .17f); L(.5f, .5f, .79f, .66f); break;
                case "gavel": P(.36f, .16f, .64f, .44f, .52f, .56f, .24f, .28f); L(.58f, .5f, .84f, .76f); L(.16f, .84f, .54f, .84f); break;
                case "bars": R(.18f, .56f, .34f, .82f); R(.42f, .38f, .58f, .82f); R(.66f, .2f, .82f, .82f); break;
                case "db": Arc(.5f, .26f, .28f, .09f); L(.22f, .26f, .22f, .74f); L(.78f, .26f, .78f, .74f); Arc(.5f, .74f, .28f, .09f, 0f, 180f); Arc(.5f, .5f, .28f, .09f, 0f, 180f); break;
                case "smile": O(.5f, .5f, .33f); Arc(.5f, .52f, .17f, .15f, 20f, 160f); O(.4f, .42f, .03f); O(.6f, .42f, .03f); break;
                case "hourglass": L(.28f, .18f, .72f, .18f); L(.28f, .82f, .72f, .82f); L(.32f, .18f, .68f, .82f); L(.68f, .18f, .32f, .82f); break;
                case "idcard": R(.2f, .26f, .8f, .8f); O(.38f, .48f, .08f); L(.54f, .44f, .72f, .44f); L(.54f, .58f, .72f, .58f); L(.28f, .7f, .48f, .7f); L(.44f, .14f, .5f, .26f); L(.56f, .14f, .5f, .26f); break;
                case "medal": O(.5f, .62f, .2f); L(.34f, .16f, .44f, .44f); L(.66f, .16f, .56f, .44f); O(.5f, .62f, .08f); break;
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
