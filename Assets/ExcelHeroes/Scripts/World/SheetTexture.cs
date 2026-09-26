using System.Collections.Generic;
using ExcelHeroes.UI;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The back sheet (BackSheet.Spec) as a texture, for the 3D figure to carry. Same reading as the
    /// UI version: grade sets the grid and the frame, level/★ fill the cells, the trait marks them,
    /// A and up get a bar chart, S gets gold and stacked tabs. Flat rectangles here — the quad
    /// itself is tilted in 3D, which is what the UI version's slant was imitating.
    /// </summary>
    public static class SheetTexture
    {
        const int W = 256, H = 184;
        static readonly Dictionary<string, Texture2D> Cache = new();

        public static Texture2D For(BackSheet.Spec s, string key)
        {
            key = $"{key}:{s.Cols}x{s.Rows}:{s.Filled}:{s.Bars}:{s.Trait}";
            if (Cache.TryGetValue(key, out var t) && t != null) return t;
            t = Paint(s);
            Cache[key] = t;
            return t;
        }

        static Texture2D Paint(BackSheet.Spec s)
        {
            var px = new Color[W * H];
            var accent = s.Accent;
            var glass = Color.Lerp(Color.white, accent, 0.2f);

            void Rect(float x0, float y0, float x1, float y1, Color c)
            {
                // y measured from the TOP, as the UI version lays it out.
                var ix0 = Mathf.Clamp(Mathf.RoundToInt(x0), 0, W); var ix1 = Mathf.Clamp(Mathf.RoundToInt(x1), 0, W);
                var iy0 = Mathf.Clamp(Mathf.RoundToInt(y0), 0, H); var iy1 = Mathf.Clamp(Mathf.RoundToInt(y1), 0, H);
                for (var y = iy0; y < iy1; y++)
                    for (var x = ix0; x < ix1; x++)
                    {
                        var i = (H - 1 - y) * W + x;
                        px[i] = Over(px[i], c);
                    }
            }

            // body: translucent glass, brighter at the top
            for (var y = 0; y < H; y++)
            {
                var k = y / (float)H;
                var c = new Color(glass.r, glass.g, glass.b, Mathf.Lerp(0.34f, 0.58f, k));
                for (var x = 0; x < W; x++) px[y * W + x] = c;
            }
            // frame
            var frame = s.GoldFrame ? new Color(1f, 0.81f, 0.24f, 1f) : new Color(accent.r, accent.g, accent.b, 0.95f);
            var fw = s.GoldFrame ? 6 : 4;
            Rect(0, 0, W, fw, frame); Rect(0, H - fw, W, H, frame);
            Rect(0, 0, fw, H, frame); Rect(W - fw, 0, W, H, frame);

            const float pad = 12f;
            float ix = pad, iy = pad, iw = W - pad * 2, ih = H - pad * 2;
            var yy = iy;
            if (s.FormulaBar)
            {
                var fh = ih * 0.11f;
                Rect(ix, yy, ix + iw, yy + fh, new Color(1, 1, 1, 0.8f));
                Rect(ix + 3, yy + 3, ix + fh - 3, yy + fh - 3, new Color(accent.r, accent.g, accent.b, 0.95f));
                Rect(ix + fh + 4, yy + fh * 0.4f, ix + iw * 0.6f, yy + fh * 0.6f,
                     s.Gold ? new Color(0.9f, 0.67f, 0.08f, 0.95f) : new Color(0.3f, 0.36f, 0.48f, 0.6f));
                yy += fh + 5;
            }

            var chartW = s.Chart ? iw * 0.22f : 0f;
            var gx = ix + (s.HeaderCol ? iw * 0.08f : 0f);
            var gw = iw - (gx - ix) - chartW - (s.Chart ? 6f : 0f);
            if (s.HeaderRow)
            {
                var hh = ih * 0.09f;
                Rect(gx, yy, gx + gw, yy + hh, new Color(accent.r, accent.g, accent.b, 0.45f));
                yy += hh + 3;
            }

            var rows = BackSheet.RowsMax;
            var totalRow = s.Trait == "rally";
            var gh = iy + ih - yy - (totalRow ? ih * 0.1f : 0f);
            var cw = gw / s.Cols;
            var ch = gh / rows;
            if (s.HeaderCol) Rect(ix, yy, gx - 3, yy + gh, new Color(accent.r, accent.g, accent.b, 0.32f));

            var fill = s.Gold ? new Color(1f, 0.78f, 0.16f, 0.95f) : new Color(accent.r, accent.g, accent.b, 0.88f);
            var idx = 0;
            for (var r = 0; r < rows; r++)
            {
                var open = r < s.Rows;
                for (var c = 0; c < s.Cols; c++, idx += open ? 1 : 0)
                {
                    var x0 = gx + c * cw + 1.5f; var y0 = yy + r * ch + 1.5f;
                    var x1 = x0 + cw - 3f; var y1 = y0 + ch - 3f;
                    if (s.Trait == "splash" && c % 2 == 0 && c + 1 < s.Cols) x1 = x0 + cw * 2 - 3f;
                    else if (s.Trait == "splash" && c % 2 == 1) continue;
                    if (!open) { Rect(x0, y0, x1, y1, new Color(0.47f, 0.51f, 0.59f, 0.14f)); continue; }
                    var filled = idx < s.Filled;
                    var col = !filled ? new Color(1, 1, 1, 0.6f)
                        : s.Trait == "crit" && idx % 4 == 2 ? new Color(0.9f, 0.27f, 0.27f, 0.95f)
                        : fill;
                    Rect(x0, y0, x1, y1, col);
                }
            }
            if (totalRow) Rect(gx, yy + gh + 3, gx + gw, iy + ih, new Color(accent.r, accent.g, accent.b, 0.9f));

            if (s.Chart)
            {
                var cx = ix + iw - chartW;
                Rect(cx, yy, cx + chartW, yy + gh, new Color(1, 1, 1, 0.62f));
                var bw = (chartW - 8f) / 4f;
                for (var b = 0; b < 4; b++)
                {
                    var hgt = Mathf.Clamp01(0.25f + 0.15f * (b + s.Bars)) * (gh - 8f);
                    Rect(cx + 4 + b * bw + 1.5f, yy + gh - 4 - hgt, cx + 4 + (b + 1) * bw - 1.5f, yy + gh - 4,
                         s.Gold ? new Color(0.94f, 0.7f, 0.12f, 0.95f) : new Color(accent.r, accent.g, accent.b, 0.95f));
                }
            }

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "sheet", anisoLevel = 4 };
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        static Color Over(Color dst, Color src)
        {
            var a = src.a + dst.a * (1f - src.a);
            if (a <= 0.0001f) return new Color(0, 0, 0, 0);
            var rgb = (new Vector3(src.r, src.g, src.b) * src.a + new Vector3(dst.r, dst.g, dst.b) * dst.a * (1f - src.a)) / a;
            return new Color(rgb.x, rgb.y, rgb.z, a);
        }
    }
}
