using System.Linq;
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
        static readonly Dictionary<string, Texture2D> Eyes = new(), Faces = new(), Bodies = new(), Hairs = new();

        /// <summary>
        /// The eye shapes the samples vary between: the white's proportions and how far its outer
        /// corner rises (a "tsurime" sharp eye) or falls (a "tareme" droop), the iris's size in
        /// its plate, and the upper lash's thickness and slant. u runs from the OUTER corner.
        /// </summary>
        struct EyeStyle { public float Rx, Ry, Tilt, Exp, Iris, Lash, LashTilt, Lift; }
        static EyeStyle StyleOf(string eyes) => eyes switch
        {
            "round" => new EyeStyle { Rx = 0.40f, Ry = 0.50f, Tilt = 0.00f, Exp = 2.2f, Iris = 1.00f, Lash = 0.18f, LashTilt = 0.00f, Lift = 0.54f },
            "sharp" => new EyeStyle { Rx = 0.47f, Ry = 0.37f, Tilt = 0.16f, Exp = 3.2f, Iris = 0.86f, Lash = 0.27f, LashTilt = 0.14f, Lift = 0.56f },
            "droop" => new EyeStyle { Rx = 0.44f, Ry = 0.43f, Tilt = -0.13f, Exp = 2.4f, Iris = 0.98f, Lash = 0.20f, LashTilt = -0.12f, Lift = 0.50f },
            _ => new EyeStyle { Rx = 0.43f, Ry = 0.44f, Tilt = 0.06f, Exp = 2.6f, Iris = 0.95f, Lash = 0.21f, LashTilt = 0.06f, Lift = 0.54f },
        };
        /// <summary>Inside the styled white: a superellipse tilted so the outer corner (u=0) moves by Tilt.</summary>
        static bool InWhite(in EyeStyle e, float lu, float lv)
        {
            var cy = e.Lift + (0.5f - lu) * e.Tilt;                 // the centreline slopes toward the outer corner
            var dx = Mathf.Abs(lu - 0.5f) / e.Rx; var dy = Mathf.Abs(lv - cy) / e.Ry;
            return Mathf.Pow(dx, e.Exp) + Mathf.Pow(dy, e.Exp) < 1f;
        }
        static Color[] _hairSrc; static int _hairN; static float _hairMeanLum;

        /// <summary>
        /// The hair sheet in the character's colour. The sample's sheet is a flat purple with
        /// lighter highlight streaks; tinting it with _Color multiplies the purple in (every
        /// colour came out dark and muddy), so instead each texel's brightness relative to the
        /// sheet's mean is applied to the character's hair colour, and the brightest streaks
        /// blend toward the tip colour so even black hair keeps its shine.
        /// </summary>
        static readonly Dictionary<string, (Color[] px, int n, float mean)> HairSrcs = new();

        /// <summary>0..1: how far the hair's strand shading is folded to two flat tones (EH_HAIRCEL; A/B hook).</summary>
        public static readonly float HairCel = float.TryParse(System.Environment.GetEnvironmentVariable("EH_HAIRCEL"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hc) ? hc : 1f;   // on: the flat two-tone hair read cleaner (the judge split 5/10 with both sides praising it)

        public static Texture2D Hair(SdLook k, Texture2D source = null)
        {
            var srcName = source != null ? source.name : "base_hair";
            var id = ColorUtility.ToHtmlStringRGB(k.Hair) + ":" + srcName;
            if (Hairs.TryGetValue(id, out var t) && t != null) return t;
            if (!HairSrcs.TryGetValue(srcName, out var src))
            {
                var tex = source != null ? source : Resources.Load<Texture2D>("Art/SDBase/base_hair");
                var pxs = tex.GetPixels();
                // a two-tone sheet (Kayoko's black / white halves) flattened to one tone: each texel's
                // luminance taken relative to its neighbourhood's (a wide box), so only the strokes remain
                if (srcName.ToLowerInvariant().Contains("kayoko") || srcName.ToLowerInvariant().Contains("ch0239")) pxs = LocalTone(pxs, tex.width, tex.height);
                var sum = 0f; foreach (var c in pxs) sum += Lum(c);
                src = (pxs, tex.width, Mathf.Max(0.02f, sum / pxs.Length));
                HairSrcs[srcName] = src;
            }
            _hairSrc = src.px; _hairN = src.n; _hairMeanLum = src.mean;
            var px = new Color[_hairSrc.Length];
            var hair = k.Hair; hair.a = 1f;
            var tip = Color.Lerp(k.Hair, Color.white, 0.45f); tip.a = 1f;
            for (var i = 0; i < px.Length; i++)
            {
                // the sheet is nearly flat (lum 0.35–0.42), so the contrast is amplified; the top
                // few percent of texels are the highlight streaks. (Mathf.SmoothStep(a, b, t) is an
                // ease between a and b, NOT a threshold — an explicit one here.)
                var ratio = Lum(_hairSrc[i]) / _hairMeanLum;
                // in HSV, the hero's colour as the sheet's mid-tone with room above and below it: white or
                // silver hair used to sit at the top already, so its shadows only reached 70 % and the
                // highlights nowhere — a flat pale blob with no strands. Light hair now takes its mid-tone
                // at 0.9 value, the strand lines and shadows down to about 55 %, the streaks to white.
                Color.RGBToHSV(hair, out var hh, out var hs, out var hv);
                var baseV = Mathf.Min(hv, 0.9f);
                var f = Mathf.Clamp(Mathf.Pow(ratio, 2.6f), hv > 0.85f ? 0.62f : 0.5f, 1.5f);
                // cel hair (HairCel): the strands' continuous shading folded to two tones — the lit tone and one
                // shadow tone, a soft edge between — as BA's hair reads: flat colour, a shadow shape, one shine band
                if (HairCel > 0f)
                {
                    var shadowT = hv > 0.85f ? 0.8f : 0.74f;
                    var lit = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((f - 0.8f) / 0.14f));
                    f = Mathf.Lerp(f, Mathf.Lerp(shadowT, 1.02f, lit), HairCel);
                }
                var shadowSat = f < 1f ? Mathf.Lerp(1.15f, 1f, f) : 1f;
                var c = Color.HSVToRGB(hh, Mathf.Clamp01(hs * shadowSat), Mathf.Clamp01(baseV * f)); c.a = 1f;
                var u = Mathf.Clamp01((ratio - 1.08f) / 0.1f);
                var shine = u * u * (3f - 2f * u);
                px[i] = Color.Lerp(c, tip, shine * 0.65f);
            }
            var hw = _hairN; var hh2 = src.px.Length / _hairN;
            // small sheets (Hikari's 256²) are seen magnified on the big chibi head and read soft:
            // doubled (bicubic) and lightly sharpened, so the strand lines stay crisp
            if (hw < 1024) { px = Upscale2(px, hw, hh2); hw *= 2; hh2 *= 2; }
            t = new Texture2D(hw, hh2, TextureFormat.RGBA32, true) { name = "hair:" + id, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            t.SetPixels(px); t.Apply(true);
            return Hairs[id] = t;
        }
        static float Lum(Color c) => 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;

        static float Cubic(float a, float b, float c, float d, float t)
            => b + 0.5f * t * (c - a + t * (2f * a - 5f * b + 4f * c - d + t * (3f * (b - c) + d - a)));

        /// <summary>Twice the size by Catmull-Rom, then a light unsharp mask.</summary>
        public static Color[] Upscale2(Color[] px, int w, int h)
        {
            int W = w * 2, H = h * 2;
            var o = new Color[W * H];
            Color P(int x, int y) => px[Mathf.Clamp(y, 0, h - 1) * w + Mathf.Clamp(x, 0, w - 1)];
            for (var y = 0; y < H; y++)
                for (var x = 0; x < W; x++)
                {
                    float fx = (x + 0.5f) / 2f - 0.5f, fy = (y + 0.5f) / 2f - 0.5f;
                    int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy); float tx = fx - ix, ty = fy - iy;
                    var c = new Color();
                    for (var ch = 0; ch < 4; ch++)
                    {
                        var col = new float[4];
                        for (var j = -1; j <= 2; j++) col[j + 1] = Cubic(P(ix - 1, iy + j)[ch], P(ix, iy + j)[ch], P(ix + 1, iy + j)[ch], P(ix + 2, iy + j)[ch], tx);
                        c[ch] = Mathf.Clamp01(Cubic(col[0], col[1], col[2], col[3], ty));
                    }
                    o[y * W + x] = c;
                }
            var sh = (Color[])o.Clone();
            for (var y = 1; y < H - 1; y++)
                for (var x = 1; x < W - 1; x++)
                {
                    var i = y * W + x;
                    var m = (o[i - 1] + o[i + 1] + o[i - W] + o[i + W]) * 0.25f;
                    var c = o[i] + (o[i] - m) * 0.5f; c.a = o[i].a;
                    sh[i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), c.a);
                }
            return sh;
        }

        static Color[] LocalTone(Color[] px, int w, int h)
        {
            var r = Mathf.Max(4, w / 24);
            var lum = px.Select(Lum).ToArray();
            // summed-area table for the box mean
            var sat = new double[(w + 1) * (h + 1)];
            for (var y = 0; y < h; y++)
            {
                double row = 0;
                for (var x = 0; x < w; x++) { row += lum[y * w + x]; sat[(y + 1) * (w + 1) + x + 1] = sat[y * (w + 1) + x + 1] + row; }
            }
            var mean = lum.Average();
            var o = new Color[px.Length];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    int x0 = Mathf.Max(0, x - r), x1 = Mathf.Min(w, x + r + 1), y0 = Mathf.Max(0, y - r), y1 = Mathf.Min(h, y + r + 1);
                    var box = (sat[y1 * (w + 1) + x1] - sat[y0 * (w + 1) + x1] - sat[y1 * (w + 1) + x0] + sat[y0 * (w + 1) + x0]) / ((x1 - x0) * (y1 - y0));
                    var l = (float)(lum[y * w + x] / System.Math.Max(0.03, box) * mean);
                    o[y * w + x] = new Color(l, l, l, px[y * w + x].a);
                }
            return o;
        }
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
            // thighs and shins: trousers run down both; a skirt shows skin on the thigh with knee
            // socks below (bare = short socks, so skin on both; tights = the sock colour on both)
            var sock = k.Socks; sock.a = 1f;
            var skinC = k.Skin; skinC.a = 1f;
            var thigh = k.Pants ? k.Socks : k.Legwear == "tights" ? sock : skinC; thigh.a = 1f;
            var shin = k.Pants ? k.Socks : k.Legwear == "bare" ? skinC : sock; shin.a = 1f;
            Fill(px, n, new Rect(0.60f, 0.28f, 0.04f, 0.04f), (u, v) => shin);
            var legs = thigh;
            Fill(px, n, new Rect(0.235f, 0f, 0.022f, 0.03f), (u, v) => shirt);
            // the sample wears its jacket off the shoulders, so the shoulder/neck pieces sample the
            // shirt swatches (u .125–.19): on a tailored outfit the jacket covers the shoulders
            if (tailored) { var topS = MeshKit.Shade(k.Top, 0.97f); topS.a = 1f; Fill(px, n, new Rect(0.125f, 0f, 0.065f, 0.03f), (u, v) => topS); }
            Fill(px, n, new Rect(0.185f, 0f, 0.03f, 0.03f), (u, v) => bottom);
            // the sleeves: the big jacket region (u .02–.96, v .34–.98) is the sample's sleeves unwrapped
            // (measured with SD_UVDEBUG), and it carries the gym jacket's white stripes and navy cuffs —
            // the single thing that made the whole cast read as wearing one tracksuit. On everything
            // but sportswear it becomes the outfit's sleeve colour: the jacket's on a suit, coat, lab
            // coat, cardigan or hoodie, the shirt's on a shirt or vest; the old texel's light and dark
            // survive only as a faint fold shading
            if (k.Outfit is not ("" or "sport" or "track"))
            {
                var sleeve = k.Outfit is "shirt" or "vest" ? k.Shirt : k.Top; sleeve.a = 1f;
                Fill(px, n, new Rect(0.02f, 0.34f, 0.94f, 0.64f), (u, v) =>
                {
                    var src = _bodySrc[Mathf.Clamp((int)(v * n), 0, n - 1) * n + Mathf.Clamp((int)(u * n), 0, n - 1)];
                    var fold = Mathf.Clamp(Lum(src) / 0.62f, 0.75f, 1.12f);
                    var c = sleeve * Mathf.Lerp(1f, fold, 0.35f); c.a = 1f;
                    return c;
                });
            }
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
            // SD_UVDEBUG=u|v (preview diagnostics): the sheet in ten colour bands along u or v, to read
            // off which region a part of the mesh samples — red orange yellow green cyan blue purple
            // pink white black = 0.0 … 0.9
            var uvDebug = System.Environment.GetEnvironmentVariable("SD_UVDEBUG");
            if (uvDebug is "u" or "v")
            {
                Color[] bands = { Color.red, new(1f, 0.55f, 0f), Color.yellow, Color.green, Color.cyan, Color.blue, new(0.6f, 0.1f, 0.9f), new(1f, 0.5f, 0.8f), Color.white, Color.black };
                for (var y = 0; y < n; y++)
                    for (var x = 0; x < n; x++)
                    {
                        var f = uvDebug == "u" ? (x + 0.5f) / n : (y + 0.5f) / n;
                        px[y * n + x] = bands[Mathf.Clamp((int)(f * 10f), 0, 9)];
                    }
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
            var id = k.Id + ":" + expr + ":" + k.Eyes + ":" + IrisOf(k);
            if (Eyes.TryGetValue(id, out var t) && t != null) return t;
            var shut = expr is "happy" or "hurt" or "blink" or "dizzy";
            var es = StyleOf(k.Eyes);
            const int N = 128;
            var px = new Color[N * N];
            var clear = new Color(0f, 0f, 0f, 0f);
            for (var i = 0; i < px.Length; i++) px[i] = clear;
            var eye = k.Eye;
            var dark = Color.Lerp(eye, new Color(0.08f, 0.06f, 0.12f), 0.62f);
            // saturated, not greyed: the samples' irises are the brightest colour on the face
            Color.RGBToHSV(eye, out var eh, out var esat, out var ev);
            var vivid = Color.HSVToRGB(eh, Mathf.Clamp01(esat * 1.35f + 0.1f), Mathf.Clamp01(ev * 1.1f + 0.08f));
            var light = Color.Lerp(vivid, Color.white, 0.22f);
            // iris: ellipse in its box, dark rim, gradient dark(top) → light(bottom), slit pupil, lower crescent light, glint
            Rect box = new(0.251f, 0.011f, 0.718f, 0.973f);
            // …or a SAMPLE's own painted iris, gradient-mapped to this character's colour (IrisOf)
            var lib = IrisSheet(IrisOf(k));
            var darkI = Color.Lerp(vivid, new Color(0.06f, 0.05f, 0.1f), 0.72f);
            var lightI = Color.Lerp(vivid, Color.white, 0.5f);
            var cx = box.center.x; var cy = box.center.y; var rx = box.width * 0.5f * es.Iris; var ry = box.height * 0.5f * es.Iris;
            for (var y = 0; y < N; y++)
                for (var x = 0; x < N; x++)
                {
                    var u = (x + 0.5f) / N; var v = (y + 0.5f) / N;
                    var d = Mathf.Sqrt(((u - cx) / rx) * ((u - cx) / rx) + ((v - cy) / ry) * ((v - cy) / ry));
                    if (d >= 1f || shut) continue;                                   // shut: the iris plate is clipped away
                    if (expr == "angry" && v > cy + ry * 0.55f) continue;            // narrowed: the top of the iris hidden
                    if (lib.px != null)
                    {
                        // the sample's painting: its light and dark kept, its hue replaced; the white
                        // glints (low saturation, very light) stay white
                        var sc = SampleBilinear(lib, u, v);
                        var L = Mathf.InverseLerp(lib.lo, lib.hi, Lum(sc));
                        Color.RGBToHSV(sc, out _, out var ss, out var sv);
                        var ci = ss < 0.2f && sv > 0.86f ? Color.white
                               : L < 0.5f ? Color.Lerp(darkI, vivid, L * 2f) : Color.Lerp(vivid, lightI, (L - 0.5f) * 2f);
                        ci.a = 1f;
                        px[y * N + x] = ci;
                        continue;
                    }
                    var g = Mathf.InverseLerp(cy + ry, cy - ry, v);            // 0 top .. 1 bottom
                    var c = Color.Lerp(dark, light, Mathf.SmoothStep(0f, 1f, g * 1.15f));
                    if (d > 0.78f) c = Color.Lerp(c, dark, Mathf.InverseLerp(0.78f, 1f, d));
                    if (g < 0.22f) c = Color.Lerp(c, new Color(0.05f, 0.04f, 0.08f), (0.22f - g) / 0.22f * 0.7f);   // the lid's shadow over the top
                    var pu = Mathf.Sqrt(((u - cx) / (rx * 0.17f)) * ((u - cx) / (rx * 0.17f)) + ((v - (cy + ry * 0.05f)) / (ry * 0.36f)) * ((v - (cy + ry * 0.05f)) / (ry * 0.36f)));
                    if (pu < 1f) c = Color.Lerp(new Color(0.95f, 0.3f, 0.5f), dark, Mathf.Clamp01(pu * 1.3f - 0.45f));
                    var lc = Mathf.Sqrt(((u - cx) / (rx * 0.62f)) * ((u - cx) / (rx * 0.62f)) + ((v - (cy - ry * 0.38f)) / (ry * 0.4f)) * ((v - (cy - ry * 0.38f)) / (ry * 0.4f)));
                    if (lc < 1f && v < cy - ry * 0.12f) c = Color.Lerp(c, Color.Lerp(light, Color.white, 0.7f), 0.7f * (1f - lc * 0.5f));
                    var g2 = Mathf.Sqrt(((u - (cx - rx * 0.35f)) / (rx * 0.16f)) * ((u - (cx - rx * 0.35f)) / (rx * 0.16f)) + ((v - (cy - ry * 0.55f)) / (ry * 0.11f)) * ((v - (cy - ry * 0.55f)) / (ry * 0.11f)));
                    if (g2 < 1f) c = Color.Lerp(c, Color.white, 0.85f);              // a second, lower glint
                    var hl = Mathf.Sqrt(((u - (cx + rx * 0.42f)) / (rx * 0.2f)) * ((u - (cx + rx * 0.42f)) / (rx * 0.2f)) + ((v - (cy - ry * 0.28f)) / (ry * 0.14f)) * ((v - (cy - ry * 0.28f)) / (ry * 0.14f)));
                    if (hl < 1f) c = Color.white;
                    c.a = 1f;
                    px[y * N + x] = c;
                }
            // eye white: the socket plug — opaque skin with the white blob giving the eye its shape
            var skinO = k.Skin; skinO.a = 1f;
            var lashC = Color.Lerp(k.Hair, new Color(0.1f, 0.07f, 0.09f), 0.8f); lashC.a = 1f;
            Fill(px, N, new Rect(0.036f, 0.677f, 0.195f, 0.171f), (u, v) =>
            {
                var lu = (u - 0.036f) / 0.195f; var lv = (v - 0.677f) / 0.171f;
                if (shut)
                {
                    // eyes shut: a line drawn on the plug — an arch (happy) or a > chevron (hurt)
                    var e = (lu - 0.5f) * 2f;
                    if (expr == "dizzy")
                    {
                        // a spiral: the reference's knocked-out eyes
                        var dx = (lu - 0.5f) / 0.42f; var dy = (lv - 0.5f) / 0.46f; var r = Mathf.Sqrt(dx * dx + dy * dy);
                        if (r > 1f) return skinO;
                        var turn = Mathf.Repeat(r * 2.6f - Mathf.Atan2(dy, dx) / (2f * Mathf.PI), 1f);
                        return turn < 0.28f ? lashC : skinO;
                    }
                    var line = expr == "happy" ? 0.62f - e * e * 0.3f : expr == "blink" ? 0.56f - e * e * 0.06f : 0.55f - Mathf.Abs(e) * 0.28f;
                    return Mathf.Abs(lv - line) < (expr == "blink" ? 0.075f : 0.1f) && Mathf.Abs(e) < 0.8f ? lashC : skinO;
                }
                // the white in the character's eye shape (the iris, its own mesh, sits over it), with
                // the lids drawn on it the way the samples do: a heavy dark line along the upper edge
                // (thicker toward the outer corner), a faint shadow under it, a thin lower line
                if (expr == "angry" && lv > 0.74f) return skinO;                     // narrowed lid
                if (!InWhite(es, lu, lv)) return skinO;
                var cyl = es.Lift + (0.5f - lu) * es.Tilt;
                var dxn = Mathf.Abs(lu - 0.5f) / es.Rx;
                var topEdge = cyl + es.Ry * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(dxn, es.Exp)), 1f / es.Exp);
                var botEdge = cyl - es.Ry * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(dxn, es.Exp)), 1f / es.Exp);
                var lidThick = 0.055f + 0.05f * (1f - lu) + 0.02f * (1f - dxn);      // thicker at the outer corner (u = 0) and the middle
                if (topEdge - lv < lidThick) return lashC;
                if (topEdge - lv < lidThick + 0.09f) return Color.Lerp(new Color(0.99f, 0.99f, 1f, 1f), new Color(0.72f, 0.7f, 0.82f, 1f), 0.55f);
                if (lv - botEdge < 0.03f) return Color.Lerp(new Color(0.99f, 0.99f, 1f, 1f), lashC, 0.35f);
                return new Color(0.99f, 0.99f, 1f, 1f);
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
                    case "smile":
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
                // Blue Archive's resting mouth is SMALL: a short soft curve in the middle of the plate.
                // The old one ran 62 % of the plate at 16 % of its height and read on the face as a
                // wide open "\_/" block (compare.png against the samples).
                var curve = 0.45f + e * e * 0.3f;           // v is bottom-up: corners higher = a smile
                var w = 1f - Mathf.Abs(e) / 0.34f;          // tapers to nothing at the corners
                if (w > 0f && Mathf.Abs(lv - curve) < 0.035f + 0.03f * w) return new Color(0.62f, 0.26f, 0.32f, 1f);
                return null;
            });
            // glint quads
            Fill(px, N, new Rect(0.033f, 0.901f, 0.087f, 0.062f), (u, v) => new Color(1f, 1f, 1f, 1f));
            t = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "eyemouth:" + id, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels(px); t.Apply();
            return Eyes[id] = t;
        }

        // ---------------------------------------------------------------- iris library
        static readonly Dictionary<string, (Color[] px, int w, int h, float lo, float hi)> Irises = new();

        /// <summary>
        /// Which sample's iris a character wears: the spec's (SdLook.Iris) if set, else one matched
        /// to the eye shape — round eyes take the round, glossy ones (miku, hikari), sharp eyes the
        /// narrow dark ones (kayoko, natsu), droopy eyes the soft ones (reisa, mika), the rest the
        /// base family (yuuka, base) — picked within the pair by the id's hash.
        /// </summary>
        public static string IrisOf(SdLook k)
        {
            if (!string.IsNullOrEmpty(k.Iris)) return k.Iris == "none" ? null : k.Iris;
            var pair = k.Eyes switch { "round" => new[] { "miku", "hikari" }, "sharp" => new[] { "kayoko", "natsu" }, "droop" => new[] { "reisa", "mika" }, _ => new[] { "yuuka", "base" } };
            return pair[SdPose.Hash(k.Id) % 2];
        }

        static (Color[] px, int w, int h, float lo, float hi) IrisSheet(string name)
        {
            if (string.IsNullOrEmpty(name)) return default;
            if (Irises.TryGetValue(name, out var c)) return c;
            var t = Resources.Load<Texture2D>("Art/SDBase/irislib/" + name);
            if (t == null || !t.isReadable) { Irises[name] = default; return default; }
            var px = t.GetPixels();
            // the luminance range INSIDE the iris ellipse, so the map spans the painting's own contrast
            float lo = 1f, hi = 0f;
            for (var y = 0; y < t.height; y++)
                for (var x = 0; x < t.width; x++)
                {
                    float u = (x + 0.5f) / t.width, v = (y + 0.5f) / t.height;
                    float du = (u - 0.61f) / 0.33f, dv = (v - 0.5f) / 0.44f;
                    if (du * du + dv * dv > 1f) continue;
                    var l = Lum(px[y * t.width + x]); if (l < lo) lo = l; if (l > hi) hi = l;
                }
            if (hi - lo < 0.05f) { lo = 0f; hi = 1f; }
            return Irises[name] = (px, t.width, t.height, lo, hi);
        }

        static Color SampleBilinear((Color[] px, int w, int h, float lo, float hi) s, float u, float v)
        {
            var fx = Mathf.Clamp(u * s.w - 0.5f, 0f, s.w - 1f); var fy = Mathf.Clamp(v * s.h - 0.5f, 0f, s.h - 1f);
            int x0 = (int)fx, y0 = (int)fy, x1 = Mathf.Min(x0 + 1, s.w - 1), y1 = Mathf.Min(y0 + 1, s.h - 1);
            float tx = fx - x0, ty = fy - y0;
            var a = Color.Lerp(s.px[y0 * s.w + x0], s.px[y0 * s.w + x1], tx);
            var b = Color.Lerp(s.px[y1 * s.w + x0], s.px[y1 * s.w + x1], tx);
            return Color.Lerp(a, b, ty);
        }

        public static Texture2D Face(SdLook k)
        {
            if (Faces.TryGetValue(k.Id + ":" + k.Eyes, out var t) && t != null) return t;
            const int N = 256;
            var px = new Color[N * N];
            var skin = k.Skin; skin.a = 1f;
            for (var i = 0; i < px.Length; i++) px[i] = skin;
            var lash = Color.Lerp(k.Hair, new Color(0.16f, 0.1f, 0.12f), 0.55f); lash.a = 1f;
            var es = StyleOf(k.Eyes);
            var brow = Color.Lerp(k.Hair, new Color(0.2f, 0.13f, 0.14f), 0.4f); brow.a = 1f;
            // lash plates: clear except the upper lash — a crescent along the top of the region,
            // thick at the high-u end, tapering to a point at the low-u end (the sample's shape)
            Fill(px, N, new Rect(0.589f, 0.763f, 0.384f, 0.2f), (u, v) =>
            {
                var lu = (u - 0.589f) / 0.384f; var lv = (v - 0.763f) / 0.2f;
                // the crescent follows the eye shape: its outer end (high u) rises for a sharp eye, drops for a droop
                var top = 0.62f + 0.3f * Mathf.Sin(Mathf.Clamp01(lu) * Mathf.PI * 0.9f + 0.15f) + (lu - 0.5f) * es.LashTilt;
                // the samples' upper lid is a heavy line the whole width, thickest at the outer end,
                // reaching down over the top of the iris; ours tapered to a hairline at the inner corner
                var thick = Mathf.Lerp(0.14f, es.Lash + 0.14f, Mathf.SmoothStep(0f, 1f, lu));
                if (lu > 0.04f && lu < 0.98f && lv < top && lv > top - thick) return lash;
                // a soft lid shadow under the line, then a small lower lash tick at the outer end
                if (lu > 0.06f && lu < 0.96f && lv < top - thick && lv > top - thick - 0.07f) return Color.Lerp(skin, lash, 0.25f);
                if (lu > 0.7f && lu < 0.92f && lv < top - thick - 0.42f && lv > top - thick - 0.5f) return Color.Lerp(skin, lash, 0.7f);
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
            return Faces[k.Id + ":" + k.Eyes] = t;
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
