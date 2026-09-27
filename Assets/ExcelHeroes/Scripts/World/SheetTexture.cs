using System.Collections.Generic;
using ExcelHeroes.UI;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The back sheet as a texture for the 3D figure: BackSheet.Halo's strokes and washes (the
    /// same geometry the UI paints) rasterised as distance fields — a crisp core going towards
    /// white, then an exponential glow in the stroke's colour, crossings screened so light adds
    /// to light. Square, with the grid in the middle and room around it for the rank rings.
    /// </summary>
    public static class SheetTexture
    {
        const int N = 320;
        public const float Aspect = 1f;
        static readonly Dictionary<string, Texture2D> Cache = new();

        public static Texture2D For(BackSheet.Spec s, string key)
        {
            key = $"{key}:{s.Filled}:{s.Gold}:{s.Frame}:{s.Left}";
            if (Cache.TryGetValue(key, out var t) && t != null) return t;
            t = Paint(s);
            Cache[key] = t;
            return t;
        }

        static Texture2D Paint(BackSheet.Spec s)
        {
            var strokes = new List<BackSheet.Stroke>(); var washes = new List<(List<Vector2> poly, Color c)>();
            BackSheet.Halo(s, strokes, washes);
            // to pixels, and a bounding box per stroke so each pixel only visits the strokes near it
            var segs = new List<(Vector2 a, Vector2 b, Color c, float w, float g, Rect box)>(strokes.Count);
            // the figure shows this at ~70 px, so every stroke is drawn 1.7x as heavy as the UI draws it
            const float Heavy = 1.7f, HeavyGlow = 1.15f;     // the glow grows less, or a ring turns into a blob
            foreach (var st in strokes)
            {
                var a = st.A * N; var b = st.B * N; var reach = st.Glow * HeavyGlow * 3.5f + st.Width * Heavy;
                segs.Add((a, b, st.C, st.Width * Heavy, st.Glow * HeavyGlow, Rect.MinMaxRect(Mathf.Min(a.x, b.x) - reach, Mathf.Min(a.y, b.y) - reach, Mathf.Max(a.x, b.x) + reach, Mathf.Max(a.y, b.y) + reach)));
            }
            var polys = washes.ConvertAll(w => (w.poly.ConvertAll(p => p * N), w.c));

            var px = new Color[N * N];
            for (var y = 0; y < N; y++)
                for (var x = 0; x < N; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var c = new Color(0, 0, 0, 0);
                    foreach (var (poly, wc) in polys) if (Inside(poly, p)) c = Over(c, wc);
                    foreach (var sg in segs)
                    {
                        if (!sg.box.Contains(p)) continue;
                        var d = Dist(p, sg.a, sg.b);
                        var core = Mathf.Clamp01(sg.w * 0.5f + 0.5f - d);          // the line, ~1 px antialiased
                        var glow = Mathf.Exp(-d / sg.g) * 0.7f;                       // the light around it
                        var a = Mathf.Max(core, glow) * sg.c.a;
                        if (a < 0.003f) continue;
                        var rgb = Color.Lerp(sg.c, Color.white, core * 0.45f);
                        c = Screen(c, new Color(rgb.r, rgb.g, rgb.b, a));
                    }
                    px[(N - 1 - y) * N + x] = c;
                }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "sheet", anisoLevel = 4 };
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        static float Dist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        static bool Inside(IList<Vector2> poly, Vector2 p)
        {
            var inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        /// <summary>Light adding onto light: alpha as "over", colour pulled towards screen, so crossings brighten rather than muddy.</summary>
        static Color Screen(Color dst, Color src)
        {
            var a = src.a + dst.a * (1f - src.a);
            if (a <= 0.0001f) return new Color(0, 0, 0, 0);
            var s = new Vector3(src.r, src.g, src.b); var d = new Vector3(dst.r, dst.g, dst.b);
            var scr = Vector3.one - Vector3.Scale(Vector3.one - s, Vector3.one - d);
            var rgb = (s * src.a + d * dst.a * (1f - src.a)) / a;
            rgb = Vector3.Lerp(rgb, scr, Mathf.Min(src.a, dst.a));
            return new Color(rgb.x, rgb.y, rgb.z, a);
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
