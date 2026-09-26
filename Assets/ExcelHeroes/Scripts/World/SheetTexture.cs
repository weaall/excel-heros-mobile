using System.Collections.Generic;
using ExcelHeroes.UI;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The back sheet (BackSheet.Spec) as a texture for the 3D figure: the same four-cell strip the
    /// UI draws — frame by grade, filled cells in the character's colour with their solid pattern,
    /// empty cells translucent white.
    /// </summary>
    public static class SheetTexture
    {
        const int W = 288, H = 64;
        public const float Aspect = W / (float)H;
        static readonly Dictionary<string, Texture2D> Cache = new();

        public static Texture2D For(BackSheet.Spec s, string key)
        {
            key = $"{key}:{s.Filled}:{s.Gold}:{s.Pattern}";
            if (Cache.TryGetValue(key, out var t) && t != null) return t;
            t = Paint(s);
            Cache[key] = t;
            return t;
        }

        static Texture2D Paint(BackSheet.Spec s)
        {
            var px = new Color[W * H];
            void Put(int x, int y, Color c)
            {
                if (x < 0 || y < 0 || x >= W || y >= H) return;
                var i = (H - 1 - y) * W + x;   // y from the top
                px[i] = Over(px[i], c);
            }
            void Rect(float x0, float y0, float x1, float y1, Color c)
            {
                for (var y = Mathf.RoundToInt(y0); y < Mathf.RoundToInt(y1); y++)
                    for (var x = Mathf.RoundToInt(x0); x < Mathf.RoundToInt(x1); x++) Put(x, y, c);
            }

            // body and frame
            Rect(0, 0, W, H, new Color(1f, 1f, 1f, 0.55f));
            var f = s.Frame; f.a = 0.95f;
            Rect(0, 0, W, 3, f); Rect(0, H - 3, W, H, f); Rect(0, 0, 3, H, f); Rect(W - 3, 0, W, H, f);

            const float pad = 7f, gap = 5f;
            var cw = (W - pad * 2f - gap * (BackSheet.CellCount - 1)) / BackSheet.CellCount;
            var ch = H - pad * 2f;
            for (var i = 0; i < BackSheet.CellCount; i++)
            {
                var x0 = pad + i * (cw + gap);
                if (i >= s.Filled)
                {
                    Rect(x0, pad, x0 + cw, pad + ch, new Color(0.86f, 0.9f, 0.96f, 0.7f));
                    continue;
                }
                var col = BackSheet.CellColor(s, i); col.a = 1f;
                var ink = Color.Lerp(col, Color.white, 0.38f); ink.a = 0.92f;
                for (var y = 0; y < Mathf.RoundToInt(ch); y++)
                    for (var x = 0; x < Mathf.RoundToInt(cw); x++)
                    {
                        var u = x / cw; var v = y / ch;
                        Put(Mathf.RoundToInt(x0) + x, Mathf.RoundToInt(pad) + y, BackSheet.Ink(s.Pattern, u, v) ? ink : col);
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
