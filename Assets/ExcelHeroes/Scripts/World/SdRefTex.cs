using System.Collections.Generic;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// Per-character textures painted onto the sample model's own UV layout, so the sample's
    /// face pieces keep working and only the paint changes — the way the reference does its
    /// expression swaps. Measured on CH0184 (Assets/_Ref/Editor/HairDump.cs, per piece):
    ///
    ///   EyeMouth (the rip lost its alpha; ours carries one, the material cuts at 0.5):
    ///     iris piece     uv 0.251..0.969 x 0.011..0.984  shaped mesh; the iris disc + pupil + lights
    ///     eye white      uv 0.036..0.231 x 0.677..0.848  the socket filler: the face front has a hole per eye and this
    ///                    mesh plugs it, so it is painted OPAQUE — skin around a white blob; the paint decides the eye's shape
    ///     mouth          uv 0.009..0.241 x 0.043..0.207  a wide plate; alpha draws the mouth line
    ///     glint          uv 0.033..0.120 x 0.901..0.963  small quads over the iris
    ///   Face (256, alpha cut too):
    ///     face front     uv 0.319..0.411 x 0.749..0.838  plain skin block
    ///     lash plates    uv 0.589..0.973 x 0.763..0.963  one per eye (mirrored geometry), over the socket: opaque skin + the upper lash crescent
    ///     lid line       uv 0.645..0.899 x 0.781..0.831  inside the lash region
    ///     brow bars      uv 0.918..0.987 x 0.700..0.712 (main) and 0.925..0.988 x 0.652..0.663 (through-hair tint)
    ///     ears           uv 0.032..0.230 x 0.593..0.954 and 0.234..0.458 x 0.903..0.989
    ///     lower face     uv 0.029..0.987 x 0.074..0.680 (skin, the cheek mark lives here)
    /// </summary>
    public static class SdRefTex
    {
        static readonly Dictionary<string, Texture2D> Eyes = new(), Faces = new(), Bodies = new();
        static Color[] _bodySrc; static int _bodyN;

        /// <summary>
        /// The sample's body sheet (flat colour blocks: jacket, stripes, shorts, cuffs, name tag,
        /// and a swatch strip for skin / shoes) recoloured per character by colour class —
        /// jacket → Top, shorts → Bottom, cuffs → dark Top, warm swatches → Skin, dark → Shoes;
        /// white stripes and the tag stay. Each texel keeps its own shading relative to its class.
        /// </summary>
        public static Texture2D Body(SdLook k, Texture2D sheet = null)
        {
            var id = k.Id + ":" + (sheet != null ? sheet.name : "-");
            if (Bodies.TryGetValue(id, out var t) && t != null) return t;
            if (_bodySrc == null)
            {
                var src = Resources.Load<Texture2D>("Art/SDBase/base_body");
                _bodyN = src.width; _bodySrc = src.GetPixels();
            }
            var n = _bodyN;
            var px = new Color[_bodySrc.Length];
            var tailored = k.Outfit is "suit" or "coat" or "labcoat" or "shirt" or "vest" or "dress";
            for (var i = 0; i < px.Length; i++)
            {
                var c = _bodySrc[i]; var o = c;
                float mx = Mathf.Max(c.r, c.g, c.b), mn = Mathf.Min(c.r, c.g, c.b);
                var sat = mx - mn;
                if (mx > 0.85f && sat < 0.12f) o = tailored ? Scale(k.Top, c, 1f) : c;         // white stripes, zipper: kept on sportswear, jacket-coloured on a suit/coat (the tag is painted after)
                else if (mx < 0.25f) o = Scale(k.Shoes, c, 0.12f);                                // near-black: shoes, outlines
                else if (c.r > c.b + 0.08f && c.r > c.g) o = Scale(k.Skin, c, 0.9f);           // warm: skin swatches
                else if (c.b > 0.8f && c.g > 0.65f) o = Scale(k.Top, c, 0.8f);                  // light blue: the jacket
                else if (c.b > 0.45f && c.r < 0.3f && c.g < 0.4f) o = Scale(Color.Lerp(k.Top, Color.black, 0.4f), c, 0.32f);   // navy: cuffs, collar
                else if (c.b > c.r + 0.1f && mx < 0.6f) o = Scale(k.Bottom, c, 0.42f);          // blue-grey: shorts
                o.a = 1f;
                px[i] = o;
            }
            // the palette strip, explicitly: the shirt under the jacket, the shorts, and the legs'
            // own cell (SdRefMesh.LegsUV) — trousers continue the shorts' colour, a skirt gets the
            // sampled stocking/skin colour
            var shirt = k.Shirt; shirt.a = 1f;
            // trousers: the shorts cell and the legs share the sampled LEG colour (the "bottom" sample
            // is unreliable on waist-up art); a skirt keeps its own colour over stockings/skin
            var bottom = k.Pants ? k.Socks : k.Bottom; bottom.a = 1f;
            var legs = k.Socks; legs.a = 1f;
            Fill(px, n, new Rect(0.235f, 0f, 0.022f, 0.03f), (u, v) => shirt);
            Fill(px, n, new Rect(0.185f, 0f, 0.03f, 0.03f), (u, v) => bottom);
            Fill(px, n, new Rect(0.60f, 0.22f, 0.04f, 0.04f), (u, v) => legs);
            // the name tag on the chest (u .72–.98, v .03–.35 on the sheet): the sample's school
            // badge becomes the character's own — white card, an accent band, the 4-cell sheet
            var card = new Color(0.97f, 0.97f, 0.98f, 1f);
            var band = k.Top; band.a = 1f;
            Fill(px, n, new Rect(0.72f, 0.03f, 0.26f, 0.32f), (u, v) => card);
            Fill(px, n, new Rect(0.72f, 0.31f, 0.26f, 0.04f), (u, v) => band);
            Fill(px, n, new Rect(0.745f, 0.06f, 0.21f, 0.012f), (u, v) => new Color(0.75f, 0.76f, 0.8f, 1f));
            if (sheet != null)
            {
                var sp = sheet.GetPixels(); var sw = sheet.width; var sh = sheet.height;
                Fill(px, n, new Rect(0.745f, 0.17f, 0.21f, 0.047f), (u, v) =>
                {
                    var su = Mathf.Clamp((int)((u - 0.745f) / 0.21f * sw), 0, sw - 1);
                    var sv = Mathf.Clamp((int)((v - 0.17f) / 0.047f * sh), 0, sh - 1);
                    var c = sp[sv * sw + su];
                    return c.a < 0.5f ? card : new Color(c.r, c.g, c.b, 1f);
                });
            }
            t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "body:" + id, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels(px); t.Apply(true);
            return Bodies[id] = t;
        }

        /// <summary>Target colour carrying the texel's shading: luminance relative to the class's own level.</summary>
        static Color Scale(Color target, Color texel, float classLum)
        {
            var l = 0.3f * texel.r + 0.59f * texel.g + 0.11f * texel.b;
            var f = Mathf.Clamp(l / Mathf.Max(0.05f, classLum), 0.55f, 1.25f);
            return new Color(Mathf.Clamp01(target.r * f), Mathf.Clamp01(target.g * f), Mathf.Clamp01(target.b * f), 1f);
        }

        /// <summary>
        /// The eye/mouth sheet per expression, the reference's way (same plates, another sheet):
        /// "" normal · "happy" eyes shut in arcs, open smile · "hurt" &gt;_&lt; · "angry" narrowed, set mouth.
        /// </summary>
        public static Texture2D EyeMouth(SdLook k, string expr = "")
        {
            var id = k.Id + ":" + expr;
            if (Eyes.TryGetValue(id, out var t) && t != null) return t;
            var shut = expr is "happy" or "hurt";
            const int N = 128;
            var px = new Color[N * N];
            var clear = new Color(0f, 0f, 0f, 0f);
            for (var i = 0; i < px.Length; i++) px[i] = clear;
            var eye = k.Eye;
            var dark = Color.Lerp(eye, new Color(0.08f, 0.06f, 0.12f), 0.62f);
            var light = Color.Lerp(eye, Color.white, 0.3f);
            // iris: ellipse in its box, dark rim, gradient dark(top) → light(bottom), slit pupil, lower crescent light, glint
            Rect box = new(0.251f, 0.011f, 0.718f, 0.973f);
            var cx = box.center.x; var cy = box.center.y; var rx = box.width * 0.5f; var ry = box.height * 0.5f;
            for (var y = 0; y < N; y++)
                for (var x = 0; x < N; x++)
                {
                    var u = (x + 0.5f) / N; var v = (y + 0.5f) / N;
                    var d = Mathf.Sqrt(((u - cx) / rx) * ((u - cx) / rx) + ((v - cy) / ry) * ((v - cy) / ry));
                    if (d >= 1f || shut) continue;                                   // shut: the iris plate is clipped away
                    if (expr == "angry" && v > cy + ry * 0.55f) continue;            // narrowed: the top of the iris hidden
                    var g = Mathf.InverseLerp(cy + ry, cy - ry, v);            // 0 top .. 1 bottom
                    var c = Color.Lerp(dark, light, Mathf.SmoothStep(0f, 1f, g * 1.15f));
                    if (d > 0.84f) c = Color.Lerp(c, dark, Mathf.InverseLerp(0.84f, 1f, d));
                    var pu = Mathf.Sqrt(((u - cx) / (rx * 0.17f)) * ((u - cx) / (rx * 0.17f)) + ((v - (cy + ry * 0.05f)) / (ry * 0.36f)) * ((v - (cy + ry * 0.05f)) / (ry * 0.36f)));
                    if (pu < 1f) c = Color.Lerp(new Color(0.95f, 0.3f, 0.5f), dark, Mathf.Clamp01(pu * 1.3f - 0.45f));
                    var lc = Mathf.Sqrt(((u - cx) / (rx * 0.62f)) * ((u - cx) / (rx * 0.62f)) + ((v - (cy - ry * 0.38f)) / (ry * 0.4f)) * ((v - (cy - ry * 0.38f)) / (ry * 0.4f)));
                    if (lc < 1f && v < cy - ry * 0.12f) c = Color.Lerp(c, Color.Lerp(light, Color.white, 0.65f), 0.6f * (1f - lc * 0.6f));
                    var hl = Mathf.Sqrt(((u - (cx + rx * 0.45f)) / (rx * 0.13f)) * ((u - (cx + rx * 0.45f)) / (rx * 0.13f)) + ((v - (cy - ry * 0.3f)) / (ry * 0.09f)) * ((v - (cy - ry * 0.3f)) / (ry * 0.09f)));
                    if (hl < 1f) c = Color.white;
                    c.a = 1f;
                    px[y * N + x] = c;
                }
            // eye white: the socket plug — opaque skin with the white blob giving the eye its shape
            var skinO = k.Skin; skinO.a = 1f;
            var lashC = Color.Lerp(k.Hair, new Color(0.12f, 0.08f, 0.1f), 0.75f); lashC.a = 1f;
            Fill(px, N, new Rect(0.036f, 0.677f, 0.195f, 0.171f), (u, v) =>
            {
                var lu = (u - 0.036f) / 0.195f; var lv = (v - 0.677f) / 0.171f;
                if (shut)
                {
                    // eyes shut: a line drawn on the plug — an arch (happy) or a > chevron (hurt)
                    var e = (lu - 0.5f) * 2f;
                    var line = expr == "happy" ? 0.62f - e * e * 0.3f : 0.55f - Mathf.Abs(e) * 0.28f;
                    return Mathf.Abs(lv - line) < 0.1f && Mathf.Abs(e) < 0.8f ? lashC : skinO;
                }
                // the white sits a little high and narrow, so the iris (its own mesh) fills most of it
                var d = new Vector2((lu - 0.5f) / 0.4f, (lv - 0.54f) / 0.45f).magnitude;
                if (expr == "angry" && lv > 0.78f) return skinO;                     // narrowed lid
                return d < 1f ? new Color(0.99f, 0.99f, 1f, 1f) : skinO;
            });
            // mouth: a small smile line; the rest of the plate stays clear
            Fill(px, N, new Rect(0.009f, 0.043f, 0.232f, 0.164f), (u, v) =>
            {
                var lu = (u - 0.009f) / 0.232f; var lv = (v - 0.043f) / 0.164f;
                var e = (lu - 0.5f) * 2f;
                var mouth = new Color(0.5f, 0.18f, 0.26f, 1f);
                switch (expr)
                {
                    case "happy":                                   // an open smile: a filled D, tongue-pink inside
                        {
                            var top = 0.62f; var bottom = 0.62f - (1f - e * e) * 0.34f;
                            if (Mathf.Abs(e) < 0.7f && lv < top && lv > bottom)
                                return lv < bottom + 0.06f || lv > top - 0.06f || Mathf.Abs(e) > 0.6f ? mouth : new Color(0.85f, 0.35f, 0.42f, 1f);
                            return null;
                        }
                    case "hurt":                                    // a small wobble, set low
                        return Mathf.Abs(lv - (0.36f + Mathf.Sin(e * 6f) * 0.05f)) < 0.07f && Mathf.Abs(e) < 0.45f ? mouth : null;
                    case "angry":                                   // a set line
                        return Mathf.Abs(lv - 0.42f) < 0.07f && Mathf.Abs(e) < 0.5f ? mouth : null;
                }
                var curve = 0.42f + e * e * 0.22f;          // v is bottom-up: corners higher = a smile
                if (Mathf.Abs(lv - curve) < 0.08f && Mathf.Abs(e) < 0.62f) return mouth;
                return null;
            });
            // glint quads
            Fill(px, N, new Rect(0.033f, 0.901f, 0.087f, 0.062f), (u, v) => new Color(1f, 1f, 1f, 1f));
            t = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "eyemouth:" + id, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels(px); t.Apply();
            return Eyes[id] = t;
        }

        public static Texture2D Face(SdLook k)
        {
            if (Faces.TryGetValue(k.Id, out var t) && t != null) return t;
            const int N = 256;
            var px = new Color[N * N];
            var skin = k.Skin; skin.a = 1f;
            for (var i = 0; i < px.Length; i++) px[i] = skin;
            var lash = Color.Lerp(k.Hair, new Color(0.16f, 0.1f, 0.12f), 0.55f); lash.a = 1f;
            var brow = Color.Lerp(k.Hair, new Color(0.2f, 0.13f, 0.14f), 0.4f); brow.a = 1f;
            // lash plates: clear except the upper lash — a crescent along the top of the region,
            // thick at the high-u end, tapering to a point at the low-u end (the sample's shape)
            Fill(px, N, new Rect(0.589f, 0.763f, 0.384f, 0.2f), (u, v) =>
            {
                var lu = (u - 0.589f) / 0.384f; var lv = (v - 0.763f) / 0.2f;
                var top = 0.62f + 0.3f * Mathf.Sin(Mathf.Clamp01(lu) * Mathf.PI * 0.9f + 0.15f);
                var thick = Mathf.Lerp(0.03f, k.Male ? 0.16f : 0.22f, Mathf.SmoothStep(0f, 1f, lu));
                if (lu > 0.04f && lu < 0.98f && lv < top && lv > top - thick) return lash;
                return skin;
            });
            // brow bars: the main bar in the brow colour, the through-hair bar lighter
            Fill(px, N, new Rect(0.91f, 0.695f, 0.08f, 0.022f), (u, v) => brow);
            Fill(px, N, new Rect(0.92f, 0.648f, 0.075f, 0.02f), (u, v) => Color.Lerp(brow, k.Hair, 0.5f));
            // a soft blush on the lower face
            Fill(px, N, new Rect(0.68f, 0.09f, 0.2f, 0.11f), (u, v) =>
            {
                var d = new Vector2((u - 0.78f) / 0.09f, (v - 0.145f) / 0.045f).magnitude;
                return d < 1f ? Color.Lerp(skin, new Color(1f, 0.55f, 0.6f, 1f), 0.3f * (1f - d)) : (Color?)null;
            });
            t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "face:" + k.Id, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels(px); t.Apply(true);
            return Faces[k.Id] = t;
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
