using System.Collections.Generic;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The SD face, painted into a transparent texture that is projected onto the front of the head.
    ///
    /// Reference: the reference game's SD models, whose faces are paint, not geometry — two tall eyes
    /// set low and wide, a heavy upper lash line, a gradient iris with two highlights, a tiny mouth
    /// and a blush. At battle distance the eyes are most of what reads, so they are drawn big.
    /// The main hero (김인턴, male) gets narrower eyes, a lighter lash and heavier brows.
    /// </summary>
    public static class FaceTexture
    {
        const int N = 256;
        static readonly Dictionary<string, Texture2D> Cache = new();

        public struct Look
        {
            public Color Eye, Hair;
            public bool Male, Glasses, Sunglasses, Closed, Monster;
        }

        public static Texture2D For(Look k)
        {
            var key = $"{ColorUtility.ToHtmlStringRGB(k.Eye)}{ColorUtility.ToHtmlStringRGB(k.Hair)}{k.Male}{k.Glasses}{k.Sunglasses}{k.Closed}{k.Monster}";
            if (Cache.TryGetValue(key, out var t) && t != null) return t;
            t = k.Monster ? PaintMonster(k) : Paint(k);
            Cache[key] = t;
            return t;
        }

        static Texture2D Paint(Look k)
        {
            var px = new Color[N * N];
            var lash = new Color(0.12f, 0.1f, 0.14f, 1f);
            var brow = Color.Lerp(k.Hair, lash, 0.45f);
            var eyeDark = Color.Lerp(k.Eye, lash, 0.55f);
            var eyeLight = Color.Lerp(k.Eye, Color.white, 0.35f);
            // Big, as the reference's SD eyes are: about a third of the face's height.
            var ry = k.Male ? 0.11f : 0.135f;
            var rx = k.Male ? 0.084f : 0.092f;

            for (var y = 0; y < N; y++)
                for (var x = 0; x < N; x++)
                {
                    var u = (x + 0.5f) / N;
                    var v = (y + 0.5f) / N;
                    var c = new Color(0, 0, 0, 0);

                    // blush
                    for (var s = -1; s <= 1; s += 2)
                    {
                        var d = Ell(u, v, 0.5f + s * 0.25f, 0.3f, 0.07f, 0.03f);
                        if (d < 1f) c = Over(c, new Color(1f, 0.55f, 0.6f, 0.32f * (1f - d)));
                    }

                    for (var s = -1; s <= 1; s += 2)
                    {
                        var cx = 0.5f + s * 0.185f;
                        const float cy = 0.415f;

                        if (k.Closed)
                        {
                            // A happy closed eye: a downward arc.
                            var arc = Mathf.Abs(Ell(u, v, cx, cy - 0.02f, rx * 1.05f, ry * 0.55f) - 1f);
                            if (arc < 0.16f && v > cy - 0.02f) c = Over(c, lash);
                            continue;
                        }

                        var e = Ell(u, v, cx, cy, rx, ry);
                        if (e < 1f)
                        {
                            // sclera, then the iris filling most of it
                            c = Over(c, new Color(1f, 1f, 1f, 1f));
                            var ir = Ell(u, v, cx, cy - ry * 0.08f, rx * 0.86f, ry * 0.9f);
                            if (ir < 1f)
                            {
                                var g = Mathf.InverseLerp(cy + ry, cy - ry, v); // 0 top .. 1 bottom
                                var iris = Color.Lerp(eyeDark, eyeLight, Mathf.SmoothStep(0f, 1f, g));
                                if (ir > 0.82f) iris = Color.Lerp(iris, eyeDark, 0.6f);
                                c = Over(c, iris);
                                var pu = Ell(u, v, cx, cy + ry * 0.05f, rx * 0.36f, ry * 0.42f);
                                if (pu < 1f) c = Over(c, Color.Lerp(eyeDark, lash, 0.5f));
                            }
                            // highlights: a big one up and to the outer side, a small one low
                            var h1 = Ell(u, v, cx - s * rx * 0.28f, cy + ry * 0.42f, rx * 0.26f, ry * 0.2f);
                            if (h1 < 1f) c = Over(c, Color.white);
                            var h2 = Ell(u, v, cx + s * rx * 0.3f, cy - ry * 0.45f, rx * 0.14f, ry * 0.1f);
                            if (h2 < 1f) c = Over(c, new Color(1, 1, 1, 0.9f));
                        }

                        // upper lash line: a thick arc over the top of the eye, flicked at the outer end
                        var top = Ell(u, v, cx, cy - ry * 0.05f, rx * 1.12f, ry * 1.08f);
                        var lashW = k.Male ? 0.12f : 0.2f;
                        if (top < 1f && top > 1f - lashW - 0.12f && v > cy + ry * 0.35f) c = Over(c, lash);
                        if (!k.Male)
                        {
                            var flick = Ell(u, v, cx + s * rx * 1.05f, cy + ry * 0.75f, rx * 0.25f, ry * 0.12f);
                            if (flick < 1f) c = Over(c, lash);
                        }
                        // lower lash: a short faint stroke
                        var low = Ell(u, v, cx, cy + ry * 0.05f, rx * 0.95f, ry * 1.02f);
                        if (low < 1f && low > 0.9f && v < cy - ry * 0.6f && Mathf.Abs(u - cx) < rx * 0.6f)
                            c = Over(c, new Color(lash.r, lash.g, lash.b, 0.5f));

                        // brow
                        var by = cy + ry + (k.Male ? 0.07f : 0.085f) - s * 0f;
                        var bd = Ell(u, v, cx + s * 0.01f, by, rx * 1.05f, k.Male ? 0.016f : 0.011f);
                        if (bd < 1f) c = Over(c, brow);

                        if (k.Glasses)
                        {
                            var gl = RoundBox(u, v, cx, cy, rx * 1.45f, ry * 1.2f, 0.035f);
                            if (Mathf.Abs(gl) < 0.008f) c = Over(c, new Color(0.18f, 0.18f, 0.22f, 1f));
                        }
                        if (k.Sunglasses)
                        {
                            var gl = RoundBox(u, v, cx, cy + 0.01f, rx * 1.5f, ry * 0.95f, 0.04f);
                            if (gl < 0f) c = Over(c, new Color(0.08f, 0.08f, 0.12f, 0.93f));
                            if (Mathf.Abs(gl) < 0.007f) c = Over(c, new Color(0.25f, 0.25f, 0.3f, 1f));
                        }
                    }

                    if ((k.Glasses || k.Sunglasses) && Mathf.Abs(v - 0.43f) < 0.007f && Mathf.Abs(u - 0.5f) < 0.07f)
                        c = Over(c, new Color(0.18f, 0.18f, 0.22f, 1f));

                    // mouth: a small soft smile
                    var m = Ell(u, v, 0.5f, 0.255f, 0.035f, 0.02f);
                    if (m < 1f && v < 0.255f) c = Over(c, new Color(0.62f, 0.25f, 0.28f, 1f));

                    px[y * N + x] = c;
                }

            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "face", anisoLevel = 2 };
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>
        /// A spreadsheet error's face: round white eyes with small pupils looking left (towards the
        /// party), angry slanted brows, a jagged mouth. Readable at a glance as "enemy".
        /// </summary>
        static Texture2D PaintMonster(Look k)
        {
            var px = new Color[N * N];
            var ink = new Color(0.1f, 0.09f, 0.12f, 1f);
            for (var y = 0; y < N; y++)
                for (var x = 0; x < N; x++)
                {
                    var u = (x + 0.5f) / N;
                    var v = (y + 0.5f) / N;
                    var c = new Color(0, 0, 0, 0);
                    for (var s = -1; s <= 1; s += 2)
                    {
                        var cx = 0.5f + s * 0.17f;
                        const float cy = 0.52f;
                        var e = Ell(u, v, cx, cy, 0.1f, 0.11f);
                        if (e < 1.12f) c = Over(c, ink);
                        if (e < 1f) c = Over(c, Color.white);
                        var pu = Ell(u, v, cx - 0.035f, cy - 0.01f, 0.045f, 0.055f);
                        if (pu < 1f) c = Over(c, k.Eye.a > 0 ? Color.Lerp(k.Eye, ink, 0.4f) : ink);
                        if (Ell(u, v, cx - 0.05f, cy + 0.02f, 0.014f, 0.016f) < 1f) c = Over(c, Color.white);
                        // brow: a thick bar slanting down towards the middle
                        var bx = u - cx;
                        var byLine = cy + 0.14f + bx * s * 0.55f;
                        if (Mathf.Abs(bx) < 0.1f && Mathf.Abs(v - byLine) < 0.022f) c = Over(c, ink);
                    }
                    // mouth: a dark band with a zigzag of teeth
                    if (Mathf.Abs(u - 0.5f) < 0.13f && v > 0.27f && v < 0.35f)
                    {
                        c = Over(c, new Color(0.35f, 0.07f, 0.1f, 1f));
                        var tooth = Mathf.Abs(Mathf.Repeat((u - 0.37f) / 0.052f, 1f) - 0.5f) * 2f; // 0 at tip
                        if (v > 0.35f - 0.035f * (1f - tooth)) c = Over(c, Color.white);
                    }
                    px[y * N + x] = c;
                }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "mface" };
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        static float Ell(float u, float v, float cx, float cy, float rx, float ry)
        {
            var dx = (u - cx) / rx;
            var dy = (v - cy) / ry;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static float RoundBox(float u, float v, float cx, float cy, float hx, float hy, float r)
        {
            var qx = Mathf.Abs(u - cx) - hx + r;
            var qy = Mathf.Abs(v - cy) - hy + r;
            var outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
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
