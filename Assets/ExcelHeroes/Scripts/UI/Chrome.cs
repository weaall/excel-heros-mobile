using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// The shell every screen sits in — backdrop, top bar, currency pills, player plate, bottom
    /// bar — dressed the way the reference screenshots in temp_images/ dress theirs.
    ///
    /// What the screenshots agree on, across 로비, 부대 편성, 학생, 현상수배 and 모집:
    ///
    ///   * The backdrop is LIGHT: pale sky blue to white, with broad diagonal bands of light and a
    ///     faint triangle lattice. Never a flat grey, never a dark band.
    ///   * There is no navy app bar. The top edge is a pale translucent strip; the back button is
    ///     a navy disc and the screen's name sits beside it in dark ink with a YELLOW underline.
    ///   * Currencies are separate slanted translucent-white pills, each with a cyan +.
    ///   * Settings and home are bare navy glyphs, not buttons in circles.
    ///   * The lobby's player plate is a navy slanted block: a small yellow italic "Lv.", a large
    ///     white number, the name, and a cyan experience bar.
    ///   * The bottom bar is one long translucent-white slanted strip of icon-over-label buttons.
    ///
    /// This build had a navy bar across the top with white pills on it, a grey page and a white
    /// rounded bottom bar — the same information in a different game's clothes.
    /// </summary>
    public static class Chrome
    {
        static Color C(int r, int g, int b, float a = 1f) => UiPaint.C(r, g, b, a);

        /// <summary>
        /// True on the lobby. The reference dresses its top edge two ways: on the lobby each
        /// currency is its own slanted pill floating over the scene; inside every other screen
        /// there are no pills at all — the numbers sit straight on a thin pale band, divided by
        /// faint slashes. One flag, read by the painters, rather than two sets of elements.
        /// </summary>
        public static bool Lobby { get; private set; } = true;

        public static void SetLobby(VisualElement root, bool lobby)
        {
            Lobby = lobby;
            root?.Q<VisualElement>("root")?.EnableInClassList("shell--lobby", lobby);
            SetScene(root?.Q<VisualElement>("root") ?? root, lobby);
            root?.Q<VisualElement>("topbar")?.MarkDirtyRepaint();
            root?.Query<VisualElement>(className: "chip").ForEach(c => c.MarkDirtyRepaint());
        }

        public static void Dress(VisualElement root)
        {
            if (root == null) return;

            ModalFrame.Painted(root, DrawBackdrop);
            SetScene(root, Lobby);

            var top = root.Q<VisualElement>("topbar");
            if (top != null) ModalFrame.Painted(top, DrawTopStrip);

            foreach (var chip in root.Query<VisualElement>(className: "chip").ToList())
                ModalFrame.Painted(chip, DrawPill);

            var plate = root.Q<VisualElement>("playerPlate");
            if (plate != null) ModalFrame.Painted(plate, DrawPlayerPlate);

            var nav = root.Q<VisualElement>("navbar");
            if (nav != null)
            {
                ModalFrame.Painted(nav, DrawBottomStrip);
                // every tab its own slanted glass tile, the lit one cyan (target_1)
                foreach (var tab in nav.Query<Button>(className: "navtab").ToList())
                {
                    var t = tab;
                    ModalFrame.Painted(t, (ctx, r) => DrawGlassTile(ctx, r, t.ClassListContains("navtab--active")));
                }
            }

            // settings and home: square glass tiles on the lobby, bare glyphs inside a screen
            // (the glyph moves to a child: a painter draws over its own element's text)
            foreach (var b in root.Query<Button>(className: "topbar__btn").ToList())
            {
                var glyph = new Label(b.text) { pickingMode = PickingMode.Ignore };
                glyph.AddToClassList("topbar__glyph");
                b.text = ""; b.Add(glyph);
                ModalFrame.Painted(b, (ctx, r) => { if (Lobby) DrawGlassTile(ctx, r, false, corner: false); });
            }
        }

        /// <summary>
        /// target_1's lobby tile: a slanted plate of frosted glass, white fading to translucent,
        /// a white rim, a small cyan triangle in the top-right corner and three faint diagonal
        /// stripes in the lower right. Lit, the same plate in cyan with white marks.
        /// </summary>
        public static void DrawGlassTile(MeshGenerationContext ctx, Rect r, bool active, bool corner = true, Color? edge = null)
        {
            var h = r.height; var w = r.width;
            var slant = Mathf.Min(Mathf.Tan(10f * Mathf.Deg2Rad) * h, w * 0.14f);
            var outer = UiPaint.SkewRect(r, slant, Mathf.Clamp(h * 0.08f, 4f, 9f));
            UiPaint.Shadow(ctx, outer, new Vector2(0f, Mathf.Clamp(h * 0.05f, 2f, 6f)), C(16, 36, 72, 0.22f), Mathf.Clamp(h * 0.09f, 4f, 10f));
            UiPaint.Fill(ctx, outer, active ? C(255, 255, 255, 0.95f) : C(255, 255, 255, 0.9f));
            var inner = UiPaint.Offset(outer, -2.5f);
            UiPaint.Fill(ctx, inner, active
                ? UiPaint.Vertical(C(118, 226, 248), C(34, 176, 232), r.yMin, r.yMax)
                : UiPaint.Vertical(C(250, 253, 255, 0.93f), C(214, 230, 242, 0.8f), r.yMin, r.yMax));
            // the upper half a touch brighter: the glass catching the light
            var gloss = new List<Vector2> { new(r.xMin - 4f, r.yMin), new(r.xMax + 4f, r.yMin), new(r.xMax + 4f, r.yMin + h * 0.42f), new(r.xMin - 4f, r.yMin + h * 0.5f) };
            UiPaint.Fill(ctx, UiPaint.Clip(gloss, inner), C(255, 255, 255, active ? 0.16f : 0.34f), 1f);
            // three stripes leaning with the slant, lower right
            var mark = active ? C(255, 255, 255, 0.4f) : C(90, 190, 230, 0.32f);
            for (var i = 0; i < 3; i++)
            {
                var x = r.xMax - w * 0.1f - i * Mathf.Max(7f, h * 0.075f);
                var sw = Mathf.Max(2f, h * 0.028f);
                var stripe = new List<Vector2> { new(x, r.yMax - h * 0.46f), new(x + sw, r.yMax - h * 0.46f), new(x + sw - h * 0.46f, r.yMax), new(x - h * 0.46f, r.yMax) };
                UiPaint.Fill(ctx, UiPaint.Clip(stripe, inner), mark, 0.8f);
            }
            // on a white panel the white rim vanishes: the caller gives it an edge colour
            if (edge.HasValue) UiPaint.Stroke(ctx, outer, edge.Value, 2f);
            if (corner)
            {
                var s = Mathf.Clamp(h * 0.11f, 7f, 14f);
                var cx = r.xMax - slant * 0.1f - s * 1.4f; var cy = r.yMin + s * 1.1f;
                UiPaint.Fill(ctx, new List<Vector2> { new(cx, cy - s * 0.5f), new(cx + s * 0.55f, cy + s * 0.45f), new(cx - s * 0.55f, cy + s * 0.45f) },
                             active ? C(255, 255, 255, 0.95f) : C(64, 200, 240, 0.9f), 0.8f);
            }
        }

        /// <summary>
        /// The illustrated scene behind every screen (Blue Archive never shows a flat colour behind
        /// its menus): a bright office, softly blurred and hazed so the UI reads (Art/Backdrop/menu),
        /// and on the lobby a sharper office lounge for the character to stand in (…/lobby). Both
        /// from tools/gemini_edit.py, processed by tools/backdrops.py. The first child of the shell
        /// root, so it draws over the root's own painted sky and under everything else; the painted
        /// sky stays as the fallback when the picture is missing.
        /// </summary>
        static void SetScene(VisualElement root, bool lobby)
        {
            if (root == null) return;
            var scene = root.Q<VisualElement>("shellScene");
            if (scene == null)
            {
                scene = new VisualElement { name = "shellScene", pickingMode = PickingMode.Ignore };
                scene.AddToClassList("shell-scene");
                root.Insert(0, scene);
            }
            var tex = Resources.Load<Texture2D>(lobby ? "Art/Backdrop/lobby" : "Art/Backdrop/menu");
            scene.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
        }

        /// <summary>A full page that must cover what is under it gets the same scene as its background (else the painted sky).</summary>
        public static void PaintScene(VisualElement el)
        {
            var tex = Resources.Load<Texture2D>("Art/Backdrop/menu");
            if (tex == null) { ModalFrame.Painted(el, DrawBackdrop); return; }
            el.style.backgroundImage = new StyleBackground(tex);
            el.AddToClassList("shell-scene--page");
        }

        /// <summary>
        /// Pale sky to near-white, broad diagonal light bands leaning with the plates, and a faint
        /// triangle lattice in the lower half — the 모집 result screen's floor, which the lobby and
        /// the menus share.
        /// </summary>
        public static void DrawBackdrop(MeshGenerationContext ctx, Rect r)
        {
            var body = UiPaint.RoundRect(r, 0f);
            UiPaint.Fill(ctx, body, UiPaint.Vertical(C(240, 249, 254), C(200, 228, 246), r.yMin, r.yMax), 0f);

            // Light bands, leaning `/`, as in every reference backdrop.
            var slant = r.height * 0.55f;
            void Band(float x, float w, float a)
            {
                var poly = new List<Vector2>
                {
                    new Vector2(x + slant, r.yMin), new Vector2(x + slant + w, r.yMin),
                    new Vector2(x + w, r.yMax), new Vector2(x, r.yMax),
                };
                UiPaint.Fill(ctx, UiPaint.Clip(poly, body), C(255, 255, 255, a), 18f);
            }
            Band(r.xMin + r.width * 0.08f, r.width * 0.10f, 0.28f);
            Band(r.xMin + r.width * 0.23f, r.width * 0.035f, 0.22f);
            Band(r.xMin + r.width * 0.58f, r.width * 0.14f, 0.24f);
            Band(r.xMin + r.width * 0.78f, r.width * 0.04f, 0.18f);

            // The triangle lattice: big, faint, upward and downward in alternation.
            const float s = 180f;
            var h = s * 0.866f;
            var row = 0;
            for (var y = r.yMin + r.height * 0.45f; y < r.yMax; y += h, row++)
                for (var x = r.xMin - s + (row % 2) * s * 0.5f; x < r.xMax; x += s)
                {
                    var up = new List<Vector2> { new Vector2(x + s * 0.5f, y), new Vector2(x + s, y + h), new Vector2(x, y + h) };
                    UiPaint.Fill(ctx, up, C(255, 255, 255, 0.10f + 0.04f * ((int)(x / s) % 3)), 1f);
                }
        }

        /// <summary>
        /// Inside a screen: a pale band 66px deep (6.1% of the height, measured), whiter at the
        /// top, with a faint darker foot. The container is taller than the band because the
        /// back disc hangs below it, as on the reference. On the lobby: nothing — the pills
        /// float over the scene.
        /// </summary>
        public const float BandHeight = 66f;

        public static void DrawTopStrip(MeshGenerationContext ctx, Rect r)
        {
            if (Lobby) return;
            var br = Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, r.yMin + BandHeight);
            UiPaint.Fill(ctx, UiPaint.RoundRect(br, 0f), UiPaint.Vertical(C(255, 255, 255, 0.94f), C(246, 250, 253, 0.86f), br.yMin, br.yMax), 0f);
            UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin, br.yMax - 2f, r.xMax, br.yMax), 0f), C(170, 190, 212, 0.55f), 1f);
            UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin, br.yMax, r.xMax, br.yMax + 8f), 0f),
                         UiPaint.Vertical(C(20, 40, 80, 0.12f), C(20, 40, 80, 0f), br.yMax, br.yMax + 8f), 0f);
        }

        /// <summary>A currency pill: slanted, translucent white, a thin blue-grey edge.</summary>
        public static void DrawPill(MeshGenerationContext ctx, Rect r)
        {
            // A pill on every screen: inside a screen it used to be bare numbers divided by a slash,
            // which read as a web toolbar (ui_critique round 1, 07-Roster #2 / 10-Quests #3).
            // Inside, it is inset a little so it sits within the 66px band.
            if (!Lobby) r = Rect.MinMaxRect(r.xMin, r.yMin + 7f, r.xMax, r.yMax - 7f);
            var poly = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.6f, 6f);
            UiPaint.Shadow(ctx, poly, new Vector2(0f, 2f), C(30, 60, 100, 0.16f), 6f);
            UiPaint.Fill(ctx, poly, C(186, 208, 228, 0.9f));
            UiPaint.Fill(ctx, UiPaint.Offset(poly, -2f), UiPaint.Vertical(C(255, 255, 255, 0.94f), C(240, 247, 252, 0.9f), r.yMin, r.yMax));
        }

        /// <summary>The lobby's player plate: target_1's glass plate, the avatar at its left end.</summary>
        public static void DrawPlayerPlate(MeshGenerationContext ctx, Rect r) => DrawGlassTile(ctx, r, false);

        public static void DrawPlayerPlateNavy(MeshGenerationContext ctx, Rect r)
        {
            var poly = UiPaint.SkewRect(new Rect(r.xMin - 40f, r.yMin, r.width + 40f, r.height), SkewPlate.SlantFor(r.height) * 0.8f, 6f);
            UiPaint.Shadow(ctx, poly, new Vector2(0f, 3f), C(10, 20, 40, 0.3f), 8f);
            UiPaint.Fill(ctx, poly, UiPaint.Vertical(C(40, 62, 108), C(22, 36, 70), r.yMin, r.yMax));
            UiPaint.Fill(ctx, UiPaint.Clip(new List<Vector2>
            {
                new Vector2(r.xMin - 40f, r.yMin), new Vector2(r.xMax + 40f, r.yMin),
                new Vector2(r.xMax + 40f, r.yMin + r.height * 0.45f), new Vector2(r.xMin - 40f, r.yMin + r.height * 0.45f),
            }, poly), C(255, 255, 255, 0.08f), 0f);
        }

        /// <summary>
        /// Under the lobby's tiles, target_1's navy band along the bottom edge of the screen: the
        /// tiles stand on it, their lower half over the band.
        /// </summary>
        public static void DrawBottomStrip(MeshGenerationContext ctx, Rect r)
        {
            var band = Rect.MinMaxRect(r.xMin - 400f, r.yMin + r.height * 0.5f, r.xMax + 400f, r.yMax + 200f);
            UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(band.xMin, band.yMin - 14f, band.xMax, band.yMin), 0f),
                         UiPaint.Vertical(C(18, 30, 58, 0f), C(18, 30, 58, 0.3f), band.yMin - 14f, band.yMin), 0f);
            UiPaint.Fill(ctx, UiPaint.RoundRect(band, 0f), UiPaint.Vertical(C(26, 40, 74, 0.94f), C(14, 24, 48, 0.97f), band.yMin, band.yMax), 0f);
            UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(band.xMin, band.yMin, band.xMax, band.yMin + 2f), 0f), C(90, 200, 240, 0.5f), 0f);
        }

        public static void DrawBottomStripWhite(MeshGenerationContext ctx, Rect r)
        {
            var poly = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.5f, 10f);
            UiPaint.Shadow(ctx, poly, new Vector2(0f, 4f), C(30, 60, 100, 0.18f), 10f);
            UiPaint.Fill(ctx, poly, C(255, 255, 255, 0.95f));
            UiPaint.Fill(ctx, UiPaint.Offset(poly, -2f), UiPaint.Vertical(C(255, 255, 255, 0.86f), C(236, 245, 251, 0.82f), r.yMin, r.yMax));
        }
    }
}
