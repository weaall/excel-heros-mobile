using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// The skyline and the damaged buildings behind the fight, ported from the drawSkyline and
    /// drawBuildings passes of the web build's cityBackdrop.js.
    ///
    /// These are what make the street read as a city that something happened to rather than a
    /// coloured band: broken rooflines, lit and blown-out windows, cracks running up the facades.
    /// Everything is anchored by hashing the column index, exactly as in the original, so a given
    /// phase always draws the same city.
    ///
    /// The web build draws these with canvas paths. The shapes are all convex columns with an
    /// irregular top edge, so a per-column top-edge lookup fills them without a general polygon
    /// rasteriser: for each x, work out where the roof is, then fill down to the horizon.
    /// </summary>
    public static class CityBuildings
    {
        const int W = CityBackdrop.CanvasW;
        const int H = CityBackdrop.CanvasH;
        const int Horizon = CityBackdrop.Horizon;

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

        /// <summary>Far silhouettes: one jagged column every 56px, drawn flat and half-faded.</summary>
        public static void DrawSkyline(Color32[] px, Color color, float alpha)
        {
            const int unit = 56, seed = 1000;
            for (var col = -1; col <= W / unit + 2; col++)
            {
                var x = col * unit;
                var h = 60 + Rnd(seed, col) * 120;
                var w = unit - 6 - Rnd(seed, col + 1) * 14;
                var top = Horizon - h;

                // Four steps across the roof, some of them dropped — this is the broken edge.
                for (var i = 0; i < (int)w; i++)
                {
                    var step = Mathf.Clamp(i * 4 / (int)w, 0, 4);
                    var dy = Rnd(seed, col * 10 + step) < 0.35f ? Rnd(seed, col * 20 + step) * 26f : 0f;
                    Blend(px, x + i, Mathf.RoundToInt(top + dy), 1, Mathf.RoundToInt(Horizon - top - dy), color, alpha);
                }
            }
        }

        /// <summary>Mid-ground buildings: facades, windows, cracks and a rubble line at the base.</summary>
        public static void DrawBuildings(Color32[] px, CityBackdrop.Theme t)
        {
            const int unit = 96, seed = 2000;
            for (var col = -1; col <= W / unit + 2; col++)
            {
                var x = col * unit;
                var w = (int)(70 + Rnd(seed, col) * 22);
                var h = (int)(90 + Rnd(seed, col + 3) * 110);
                var top = Horizon - h;
                var broken = Rnd(seed, col + 7) < 0.55f;   // most of them lost a corner

                // Roof profile. A broken building steps down across four points; an intact one is flat.
                var cut = 0.35f + Rnd(seed, col + 9) * 0.4f;
                for (var i = 0; i < w; i++)
                {
                    var k = i / (float)w;
                    float roof;
                    if (!broken) roof = top;
                    else if (k < cut * 0.5f) roof = Mathf.Lerp(top + 18, top + 30, k / Mathf.Max(0.001f, cut * 0.5f));
                    else if (k < cut) roof = Mathf.Lerp(top + 30, top + 4, (k - cut * 0.5f) / Mathf.Max(0.001f, cut * 0.5f));
                    else if (k < cut + 0.2f) roof = Mathf.Lerp(top + 4, top + 22, (k - cut) / 0.2f);
                    else roof = Mathf.Lerp(top + 22, top + 10, (k - cut - 0.2f) / Mathf.Max(0.001f, 0.8f - cut));

                    var y = Mathf.RoundToInt(roof);
                    Fill(px, x + i, y, 1, Horizon - y, t.Mid);
                }

                Blend(px, x + w - 10, top + 24, 10, h - 24, Color.black, 0.18f);   // facade shade strip

                // Windows: lit, blown out, or just dark.
                var cols = (w - 12) / 12;
                var rows = (h - 40) / 14;
                for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                {
                    var wx = x + 8 + c * 12;
                    var wy = top + 30 + r * 14;
                    if (broken && wy < top + 34) continue;
                    var k = Rnd(seed, col * 1000 + r * 40 + c);
                    if (k < 0.12f)
                    {
                        Fill(px, wx, wy, 6, 8, t.Window);
                        Blend(px, wx + 1, wy + 1, 2, 3, Color.white, 0.35f);
                    }
                    else if (k < 0.3f)
                    {
                        Fill(px, wx - 1, wy - 1, 8, 10, new Color(0x0c / 255f, 0x0f / 255f, 0x16 / 255f));
                        Blend(px, wx - 2, wy + 8, 10, 3, Color.black, 0.5f);
                    }
                    else Blend(px, wx, wy, 6, 8, Color.black, 0.35f);
                }

                if (Rnd(seed, col + 13) < 0.6f)
                {
                    var cx = x + 10 + Rnd(seed, col + 17) * (w - 20);
                    float cy = Horizon - 10;
                    for (var s = 0; s < 5; s++)
                    {
                        var nx = cx + (Rnd(seed, col * 31 + s) - 0.5f) * 18;
                        var ny = cy - (12 + Rnd(seed, col * 37 + s) * 14);
                        Line(px, cx, cy, nx, ny, Color.black, 0.55f);
                        cx = nx; cy = ny;
                    }
                }

                Blend(px, x - 4, Horizon - 6, w + 8, 6, Color.black, 0.25f);   // rubble at the base
            }
        }

        // ------------------------------------------------------------------ raster helpers

        static void Fill(Color32[] px, int x0, int y0, int w, int h, Color c)
        {
            var col = (Color32)c;
            var x1 = Mathf.Min(W, x0 + w);
            var y1 = Mathf.Min(H, y0 + h);
            x0 = Mathf.Max(0, x0);
            y0 = Mathf.Max(0, y0);
            for (var y = y0; y < y1; y++)
            {
                var row = (H - 1 - y) * W;
                for (var x = x0; x < x1; x++) px[row + x] = col;
            }
        }

        static void Blend(Color32[] px, int x0, int y0, int w, int h, Color c, float a)
        {
            var x1 = Mathf.Min(W, x0 + w);
            var y1 = Mathf.Min(H, y0 + h);
            x0 = Mathf.Max(0, x0);
            y0 = Mathf.Max(0, y0);
            for (var y = y0; y < y1; y++)
            {
                var row = (H - 1 - y) * W;
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
