using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// The side-view battlefield's backdrop, ported from the web build's cityBackdrop.js.
    ///
    /// The monsters turned up overnight and the staff are fighting through the districts, so the
    /// fight happens on a ruined street rather than on a blank field. Everything is world-anchored
    /// by hashing the column index, exactly as in the original, which is what lets the layers scroll
    /// at different rates and never pop.
    ///
    /// Sky, sun, skyline, buildings and street. Rubble, wrecked cars and drifting smoke are still to
    /// come; they are props rather than structure and can land later without changing anything here.
    ///
    /// It draws into a Texture2D once per phase rather than per frame. The web can afford to
    /// repaint a canvas every frame; a phone cannot, and none of this layer moves except the road
    /// markings, which scroll as a texture offset instead.
    /// </summary>
    public static class CityBackdrop
    {
        // Straight from the web build: the canvas is 13x8 cells of 64x52.
        public const int CanvasW = 832;
        public const int CanvasH = 416;
        public const int Horizon = 262;     // where the road meets the buildings
        public const int Sidewalk = 292;    // sidewalk / road boundary
        public const int GroundY = 318;     // baseline the party stands on

        /// <summary>The same 32-bit mix the web build hashes column indices with, so layouts match.</summary>
        static float Hash(int n)
        {
            unchecked
            {
                var x = (uint)(n * 2654435761);
                x ^= x >> 15;
                x = (uint)(x * 2246822519);
                x ^= x >> 13;
                return x / 4294967296f;
            }
        }

        static float Rnd(int seed, int i) => Hash(seed * 7919 + i * 104729);

        public struct Theme
        {
            public Color SkyTop, SkyHorizon, Glow, Far, Mid, Window, Road, Walk;
        }

        /// <summary>
        /// Phase palettes. The web build keeps these in stages.js; they are duplicated here rather
        /// than exported through the data pipeline because they are colour, not balance — nothing
        /// about a fight changes if a district is bluer.
        /// </summary>
        public static Theme ThemeFor(int stage)
        {
            var phase = Mathf.Max(0, (stage - 1) / 10);
            switch (phase % 5)
            {
                case 0: return new Theme {   // 해질녘 도심
                    SkyTop = C(0x2b3a55), SkyHorizon = C(0xe07a5f), Glow = C(0xffd79a), Far = C(0x3c4b63),
                    Mid = C(0x4a5568), Window = C(0xffd166), Road = C(0x2f3238), Walk = C(0x5c6068) };
                case 1: return new Theme {   // 밤 · 정전
                    SkyTop = C(0x0e1424), SkyHorizon = C(0x25304a), Glow = C(0x9fb6d8), Far = C(0x1b2436),
                    Mid = C(0x27324a), Window = C(0x8fd3ff), Road = C(0x21242a), Walk = C(0x3e434c) };
                case 2: return new Theme {   // 새벽 안개
                    SkyTop = C(0x4a5b72), SkyHorizon = C(0xb9c6d4), Glow = C(0xf2f6ff), Far = C(0x6c7c90),
                    Mid = C(0x7b8a9c), Window = C(0xffe9a8), Road = C(0x4a4f57), Walk = C(0x767d88) };
                case 3: return new Theme {   // 화재 구역
                    SkyTop = C(0x3a1c16), SkyHorizon = C(0xd1542f), Glow = C(0xffb066), Far = C(0x4a2a22),
                    Mid = C(0x5c3830), Window = C(0xffb347), Road = C(0x33292a), Walk = C(0x5f5049) };
                default: return new Theme {  // 침수 구역
                    SkyTop = C(0x14323a), SkyHorizon = C(0x4f8a94), Glow = C(0xbdf0f2), Far = C(0x1d444d),
                    Mid = C(0x2a5a63), Window = C(0xa8f0ff), Road = C(0x27343a), Walk = C(0x46595f) };
            }
        }

        static Color C(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        /// <summary>Builds the static part of the backdrop for one phase.</summary>
        public static Texture2D Build(int stage)
        {
            var theme = ThemeFor(stage);
            var px = new Color32[CanvasW * CanvasH];

            // Back to front, the way the web build layers it: sky, sun, the far silhouettes, the
            // damaged buildings, then the street they all stand on.
            DrawSky(px, theme);
            DrawSun(px, theme);
            CityBuildings.DrawSkyline(px, theme.Far, 0.55f);
            CityBuildings.DrawBuildings(px, theme);
            DrawRoad(px, theme);

            var tex = new Texture2D(CanvasW, CanvasH, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,   // pixel art: never let it interpolate
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        static void Fill(Color32[] px, int x0, int y0, int w, int h, Color32 c)
        {
            var x1 = Mathf.Min(CanvasW, x0 + w);
            var y1 = Mathf.Min(CanvasH, y0 + h);
            x0 = Mathf.Max(0, x0);
            y0 = Mathf.Max(0, y0);
            for (var y = y0; y < y1; y++)
            {
                var row = (CanvasH - 1 - y) * CanvasW;   // canvas y grows down, texture y grows up
                for (var x = x0; x < x1; x++) px[row + x] = c;
            }
        }

        static void Blend(Color32[] px, int x0, int y0, int w, int h, Color c, float a)
        {
            var x1 = Mathf.Min(CanvasW, x0 + w);
            var y1 = Mathf.Min(CanvasH, y0 + h);
            x0 = Mathf.Max(0, x0);
            y0 = Mathf.Max(0, y0);
            for (var y = y0; y < y1; y++)
            {
                var row = (CanvasH - 1 - y) * CanvasW;
                for (var x = x0; x < x1; x++)
                {
                    var d = px[row + x];
                    px[row + x] = new Color32(
                        (byte)(d.r + (c.r * 255f - d.r) * a),
                        (byte)(d.g + (c.g * 255f - d.g) * a),
                        (byte)(d.b + (c.b * 255f - d.b) * a),
                        255);
                }
            }
        }

        /// <summary>Twelve flat bands rather than a smooth gradient — the sky is pixel art too.</summary>
        static void DrawSky(Color32[] px, Theme t)
        {
            const int bands = 12;
            for (var i = 0; i < bands; i++)
            {
                var k = i / (float)(bands - 1);
                var c = Color.Lerp(t.SkyTop, t.SkyHorizon, k);
                Fill(px, 0, Horizon * i / bands, CanvasW, Horizon / bands + 1, c);
            }
        }

        /// <summary>A low sun with a stepped haze, drawn as discs so the steps stay visible.</summary>
        static void DrawSun(Color32[] px, Theme t)
        {
            const int sx = (int)(CanvasW * 0.72f), sy = 120;
            foreach (var (r, a) in new[] { (120, 0.08f), (86, 0.12f), (56, 0.18f), (34, 0.3f) })
                Disc(px, sx, sy, r, t.Glow, a);
            Disc(px, sx, sy, 22, t.Glow, 0.95f);
            Blend(px, sx - 10, sy - 14, 8, 4, Color.white, 0.35f);
        }

        static void Disc(Color32[] px, int cx, int cy, int r, Color c, float a)
        {
            for (var y = -r; y <= r; y++)
            {
                var half = Mathf.FloorToInt(Mathf.Sqrt(r * r - y * y));
                Blend(px, cx - half, cy + y, half * 2, 1, c, a);
            }
        }

        static void DrawRoad(Color32[] px, Theme t)
        {
            // sidewalk with slabs
            Fill(px, 0, Horizon, CanvasW, Sidewalk - Horizon, t.Walk);
            Blend(px, 0, Horizon, CanvasW, 3, Color.black, 0.25f);
            Blend(px, 0, Sidewalk - 4, CanvasW, 4, Color.black, 0.25f);
            for (var x = 0; x <= CanvasW; x += 48)
                Blend(px, x, Horizon + 3, 1, Sidewalk - 4 - (Horizon + 3), Color.black, 0.25f);

            // asphalt, darker at the kerb and at the bottom of frame
            Fill(px, 0, Sidewalk, CanvasW, CanvasH - Sidewalk, t.Road);
            for (var y = Sidewalk; y < CanvasH; y++)
            {
                var k = (y - Sidewalk) / (float)(CanvasH - Sidewalk);
                var a = k < 0.4f ? Mathf.Lerp(0.25f, 0f, k / 0.4f) : Mathf.Lerp(0f, 0.3f, (k - 0.4f) / 0.6f);
                if (a > 0.005f) Blend(px, 0, y, CanvasW, 1, Color.black, a);
            }

            // The road recedes.
            //
            // It used to be a flat band with one dashed line across the middle, which is a
            // side-view brawler's floor: nothing about it said which way was "further away", so
            // the fight read as figures pinned to a wall. Drawing it in perspective — lane lines
            // fanning out of a vanishing point, cross-marks that bunch up towards the horizon —
            // turns the same band into a plane the cast can stand on at different depths.
            var vpx = CanvasW * 0.5f;
            var vpy = (float)Horizon;

            // Lane lines. Each one leaves the vanishing point and reaches the bottom edge at a
            // different offset, so they splay apart as they come towards the viewer.
            for (var i = -3; i <= 3; i++)
            {
                if (i == 0) continue;
                var bottomX = vpx + i * 210f;
                var lit = Mathf.Abs(i) == 1 ? 0.5f : 0.28f;
                Line(px, vpx, vpy, bottomX, CanvasH, new Color(228 / 255f, 214 / 255f, 176 / 255f), lit);
            }

            // Cross-marks, spaced by a squared term so they crowd towards the horizon the way
            // evenly spaced lines on a real road do.
            for (var i = 1; i <= 7; i++)
            {
                var k = i / 7f;
                var y = Mathf.Lerp(Sidewalk + 6, CanvasH, k * k);
                var half = Mathf.Lerp(40f, CanvasW * 0.5f, k * k);
                Blend(px, Mathf.RoundToInt(vpx - half), Mathf.RoundToInt(y),
                      Mathf.RoundToInt(half * 2f), Mathf.Max(1, Mathf.RoundToInt(1 + k * 4)),
                      Color.black, 0.16f);
            }

            // The centre dashes ride the same perspective: longer and fatter as they approach.
            for (var i = 1; i <= 6; i++)
            {
                if (Hash(i * 13) < 0.2f) continue;
                var k = i / 6f;
                var y = Mathf.Lerp(Sidewalk + 10, CanvasH - 8, k * k);
                var len = Mathf.Lerp(10f, 76f, k);
                var thick = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, 6f, k)));
                Blend(px, Mathf.RoundToInt(vpx - len * 0.5f), Mathf.RoundToInt(y),
                      Mathf.RoundToInt(len), thick,
                      new Color(230 / 255f, 200 / 255f, 90 / 255f), 0.5f);
            }

            // cracks in the asphalt
            for (var i = 0; i <= CanvasW / 160 + 1; i++)
            {
                if (Hash(i * 17) > 0.6f) continue;
                float x = i * 160 + Hash(i * 19) * 100;
                float y = Sidewalk + 10 + Hash(i * 23) * 80;
                for (var s = 0; s < 5; s++)
                {
                    var nx = x + 10 + Hash(i * 29 + s) * 22;
                    var ny = y + (Hash(i * 31 + s) - 0.5f) * 30;
                    Line(px, x, y, nx, ny, Color.black, 0.5f);
                    x = nx; y = ny;
                }
            }
        }

        static void Line(Color32[] px, float x0, float y0, float x1, float y1, Color c, float a)
        {
            var steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)));
            for (var i = 0; i <= steps; i++)
            {
                var k = steps == 0 ? 0f : i / (float)steps;
                Blend(px, Mathf.RoundToInt(Mathf.Lerp(x0, x1, k)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, k)), 2, 2, c, a);
            }
        }
    }
}
