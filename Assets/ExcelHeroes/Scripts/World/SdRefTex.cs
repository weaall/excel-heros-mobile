using System.Collections.Generic;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// Per-character textures painted onto the sample model's own UV layout (measured with
    /// Assets/_Ref/Editor/Uv.cs), so the sample's face and eye quads keep working and only the
    /// paint changes — the way the reference does its expression swaps.
    ///
    ///   EyeMouth (64 x 64, black ground):
    ///     iris disc      uv 0.25..0.97 x 0.01..0.98   — the character's eye colour, gradient, pupil, highlights
    ///     eye white      uv 0.04..0.23 x 0.68..0.85
    ///     mouth strip    uv 0.01..0.24 x 0.04..0.21
    ///     small glint    uv 0.03..0.12 x 0.90..0.96
    ///   Face (256 x 256, skin ground):
    ///     brows          uv 0.59..0.97 x 0.76..0.96 (both sides share)   — hair colour, dark
    ///     cheek region   uv 0.32..0.41 x 0.75..0.84 (the sample's star mark: left plain, a blush)
    ///     mouth/inner    uv 0.33..0.41 x 0.75..0.83 small; the rest skin
    /// </summary>
    public static class SdRefTex
    {
        static readonly Dictionary<string, Texture2D> Eyes = new(), Faces = new();

        public static Texture2D EyeMouth(SdLook k)
        {
            if (Eyes.TryGetValue(k.Id, out var t) && t != null) return t;
            const int N = 128;
            var px = new Color[N * N];
            var black = new Color(0f, 0f, 0f, 1f);
            for (var i = 0; i < px.Length; i++) px[i] = black;
            var eye = k.Eye;
            var dark = Color.Lerp(eye, new Color(0.08f, 0.06f, 0.12f), 0.6f);
            var light = Color.Lerp(eye, Color.white, 0.35f);
            // iris: ellipse in its box, dark rim, gradient dark(top) → light(bottom), pupil, two highlights
            Rect box = new(0.25f, 0.01f, 0.72f, 0.97f);
            var cx = box.center.x; var cy = box.center.y; var rx = box.width * 0.5f; var ry = box.height * 0.5f;
            for (var y = 0; y < N; y++)
                for (var x = 0; x < N; x++)
                {
                    var u = (x + 0.5f) / N; var v = (y + 0.5f) / N;
                    var d = Mathf.Sqrt(((u - cx) / rx) * ((u - cx) / rx) + ((v - cy) / ry) * ((v - cy) / ry));
                    if (d >= 1f) continue;
                    var g = Mathf.InverseLerp(cy + ry, cy - ry, v);            // 0 top .. 1 bottom
                    var c = Color.Lerp(dark, light, Mathf.SmoothStep(0f, 1f, g * 1.1f));
                    if (d > 0.86f) c = Color.Lerp(c, dark, Mathf.InverseLerp(0.86f, 1f, d));
                    // pupil: a tall slit, a warm accent
                    var pu = Mathf.Sqrt(((u - cx) / (rx * 0.16f)) * ((u - cx) / (rx * 0.16f)) + ((v - (cy + ry * 0.05f)) / (ry * 0.34f)) * ((v - (cy + ry * 0.05f)) / (ry * 0.34f)));
                    if (pu < 1f) c = Color.Lerp(new Color(0.95f, 0.25f, 0.45f), dark, Mathf.Clamp01(pu * 1.2f - 0.4f));
                    // lower crescent light
                    var lc = Mathf.Sqrt(((u - cx) / (rx * 0.6f)) * ((u - cx) / (rx * 0.6f)) + ((v - (cy - ry * 0.35f)) / (ry * 0.42f)) * ((v - (cy - ry * 0.35f)) / (ry * 0.42f)));
                    if (lc < 1f && v < cy - ry * 0.1f) c = Color.Lerp(c, Color.Lerp(light, Color.white, 0.6f), 0.55f * (1f - lc));
                    // small glint upper right
                    var hl = Mathf.Sqrt(((u - (cx + rx * 0.45f)) / (rx * 0.12f)) * ((u - (cx + rx * 0.45f)) / (rx * 0.12f)) + ((v - (cy - ry * 0.3f)) / (ry * 0.08f)) * ((v - (cy - ry * 0.3f)) / (ry * 0.08f)));
                    if (hl < 1f) c = Color.white;
                    px[y * N + x] = c;
                }
            // eye white
            Fill(px, N, new Rect(0.03f, 0.67f, 0.21f, 0.19f), (u, v) => new Color(0.97f, 0.97f, 1f));
            // mouth: a small smile line on a pale mouth block
            Fill(px, N, new Rect(0.01f, 0.04f, 0.23f, 0.17f), (u, v) =>
            {
                var lu = (u - 0.01f) / 0.23f; var lv = (v - 0.04f) / 0.17f;
                var curve = 0.55f - Mathf.Pow((lu - 0.5f) * 2f, 2f) * 0.25f;
                if (Mathf.Abs(lv - curve) < 0.09f && lu > 0.15f && lu < 0.85f) return new Color(0.55f, 0.22f, 0.3f);
                return k.Skin;
            });
            // glint
            Fill(px, N, new Rect(0.03f, 0.9f, 0.09f, 0.06f), (u, v) => Color.white);
            t = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "eyemouth:" + k.Id, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels(px); t.Apply();
            return Eyes[k.Id] = t;
        }

        public static Texture2D Face(SdLook k)
        {
            if (Faces.TryGetValue(k.Id, out var t) && t != null) return t;
            const int N = 256;
            var px = new Color[N * N];
            var skin = k.Skin;
            for (var i = 0; i < px.Length; i++) px[i] = skin;
            var brow = Color.Lerp(k.Hair, new Color(0.15f, 0.1f, 0.12f), 0.45f);
            // the brow strip region (both brows sample here): a thick arc
            Fill(px, N, new Rect(0.59f, 0.76f, 0.38f, 0.2f), (u, v) =>
            {
                var lu = (u - 0.59f) / 0.38f; var lv = (v - 0.76f) / 0.2f;
                var arc = 0.55f + (k.Male ? 0f : 0.15f) * Mathf.Sin(lu * Mathf.PI);
                var thick = k.Male ? 0.22f : 0.14f;
                return Mathf.Abs(lv - arc) < thick * (0.6f + 0.4f * Mathf.Sin(lu * Mathf.PI)) ? brow : skin;
            });
            // cheek region: a soft blush (the sample's star mark is Yuuka's; ours have none)
            Fill(px, N, new Rect(0.30f, 0.73f, 0.13f, 0.13f), (u, v) =>
            {
                var d = new Vector2((u - 0.365f) / 0.06f, (v - 0.795f) / 0.05f).magnitude;
                return d < 1f ? Color.Lerp(skin, new Color(1f, 0.55f, 0.6f), 0.35f * (1f - d)) : skin;
            });
            t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "face:" + k.Id, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels(px); t.Apply(true);
            return Faces[k.Id] = t;
        }

        static bool Blob(float u, float v, float x, float y, float w, float h)
        {
            var d = new Vector2((u - (x + w * 0.5f)) / (w * 0.5f), (v - (y + h * 0.5f)) / (h * 0.5f)).magnitude;
            return d < 1f;
        }

        static void Fill(Color[] px, int n, Rect r, System.Func<float, float, Color?> f)
        {
            int x0 = Mathf.Clamp((int)(r.xMin * n), 0, n), x1 = Mathf.Clamp(Mathf.CeilToInt(r.xMax * n), 0, n);
            int y0 = Mathf.Clamp((int)(r.yMin * n), 0, n), y1 = Mathf.Clamp(Mathf.CeilToInt(r.yMax * n), 0, n);
            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                {
                    var c = f((x + 0.5f) / n, (y + 0.5f) / n);
                    if (c.HasValue) px[y * n + x] = c.Value;
                }
        }
    }
}
