using System.Collections.Generic;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// An enemy's head as a real 3D toy (enemies v4), built from the primitives Gemini read off its
    /// mascot (Resources/Data/enemyhead.json, tools/gen_enemyrecipe_gemini.py): a base shape with its
    /// proportions and roundness, colour bands and a front panel, the object's own parts (ears,
    /// horns, handle, tray, antenna, crown, spikes…) and a face made of 3D parts — eyeballs with an
    /// iris, a pupil and a highlight, brows or lids for the mood, a mouth, fangs, cheeks. Unlike the
    /// inflated drawing it reads as the same object from every side. Built in the head's own frame:
    /// origin at the bottom centre, +y up, +z the face, `w` wide.
    /// </summary>
    public static class SdEnemyHead
    {
        [System.Serializable] public class Band { public float y, h; public string color; }
        [System.Serializable] public class Panel { public float y, w, h; public string color; }
        [System.Serializable] public class Part { public string kind, at, color; public float size; }
        [System.Serializable] public class Eyes { public string style = "angry", iris = "#222233"; public float size = 0.2f, y = 0.55f, gap = 0.3f; }
        [System.Serializable]
        public class Recipe
        {
            public string id, shape = "blob", color, color2, dark, mouth = "frown";
            public float sx = 1f, sy = 1f, sz = 0.9f, round = 0.5f, mouth_y = 0.3f;
            public List<Band> bands = new(); public Panel panel; public List<Part> parts = new();
            public Eyes eyes = new(); public bool brows = true, cheeks;
        }
        [System.Serializable] class File { public List<Recipe> items = new(); }
        static Dictionary<string, Recipe> _rows;

        public static Recipe For(string id)
        {
            if (_rows == null)
            {
                _rows = new Dictionary<string, Recipe>();
                var a = Resources.Load<TextAsset>("Data/enemyhead");
                if (a != null) foreach (var r in JsonUtility.FromJson<File>(a.text).items) if (!string.IsNullOrEmpty(r.id)) _rows[r.id] = r;
            }
            return id != null && _rows.TryGetValue(id, out var x) ? x : null;
        }

        static Color C(string hex, Color fb) { var c = MeshKit.Hex(hex, fb); c.a = 1f; return c; }

        /// <summary>The head's mesh, `height` tall (its width follows the recipe's proportions, capped at `maxW`).</summary>
        public static Mesh Build(Recipe r, float height, float maxW)
        {
            var h = height; var w = Mathf.Min(maxW, h * r.sx / Mathf.Max(0.2f, r.sy)); var d = w * r.sz / Mathf.Max(0.2f, r.sx);
            var main = C(r.color, new Color(0.8f, 0.8f, 0.85f)); var second = C(r.color2, Color.Lerp(main, Color.white, 0.4f)); var dark = C(r.dark, new Color(0.2f, 0.2f, 0.28f));
            var b = new MeshKit.Builder();
            var rx = w * 0.5f; var ry = h * 0.5f; var rz = d * 0.5f;
            var centre = new Vector3(0f, ry, 0f);
            var round = r.shape is "sphere" or "blob" or "egg" or "drop" ? 1f : Mathf.Clamp(r.round, 0.15f, 1f);

            // ---- the base shape
            switch (r.shape)
            {
                case "box":
                    b.RoundBox(centre, new Vector3(w, h, d), main, Mathf.Max(0.22f, round), 18); break;
                case "cylinder":
                    b.Frustum(new Vector3(0f, h * 0.06f, 0f), rx, h * 0.88f, rx, main, rz / rx, 22);
                    b.Ellipsoid(new Vector3(0f, h * 0.94f, 0f), new Vector3(rx, h * 0.07f, rz), main, 22);
                    b.Ellipsoid(new Vector3(0f, h * 0.06f, 0f), new Vector3(rx, h * 0.06f, rz), main, 22);
                    break;
                case "capsule":
                    b.Frustum(new Vector3(0f, rx, 0f), rx, h - 2f * rx, rx, main, rz / rx, 22, false);
                    b.Ellipsoid(new Vector3(0f, h - rx, 0f), new Vector3(rx, rx, rz), main, 22, _ => Mathf.PI * 0.5f);
                    b.Ellipsoid(new Vector3(0f, rx, 0f), new Vector3(rx, rx, rz), main, 22, _ => Mathf.PI, Mathf.PI * 0.5f);
                    break;
                case "cone":
                    b.Frustum(new Vector3(0f, 0f, 0f), rx, h * 0.92f, rx * 0.25f, main, rz / rx, 22);
                    b.Ellipsoid(new Vector3(0f, h * 0.92f, 0f), new Vector3(rx * 0.25f, h * 0.06f, rz * 0.25f), main, 12);
                    break;
                case "drop":
                    b.Ellipsoid(new Vector3(0f, ry * 0.8f, 0f), new Vector3(rx, ry * 0.8f, rz), main, 22, _ => Mathf.PI, Mathf.PI * 0.4f);
                    b.Frustum(new Vector3(0f, ry * 0.8f + ry * 0.8f * Mathf.Cos(Mathf.PI * 0.4f) * 0f, 0f), rx * Mathf.Sin(Mathf.PI * 0.4f), h - ry * 0.8f, rx * 0.06f, main, rz / rx, 22, false);
                    break;
                case "egg":
                    b.Ellipsoid(new Vector3(0f, ry * 0.9f, 0f), new Vector3(rx * 0.9f, h - ry * 0.9f, rz * 0.9f), main, 22, _ => Mathf.PI * 0.5f);
                    b.Ellipsoid(new Vector3(0f, ry * 0.9f, 0f), new Vector3(rx, ry * 0.9f, rz), main, 22, _ => Mathf.PI, Mathf.PI * 0.5f);
                    break;
                default:   // sphere, blob
                    b.Ellipsoid(centre, new Vector3(rx, ry, rz), main, 24); break;
            }

            // where the surface is, for the bands, the panel and the face: the front z at (x, y), and
            // the half-width at a height
            float Half(float y)
            {
                var t = Mathf.Clamp((y - ry) / ry, -1f, 1f);
                return r.shape switch
                {
                    "box" or "cylinder" => rx,
                    "cone" => Mathf.Lerp(rx, rx * 0.25f, Mathf.Clamp01(y / h)),
                    "capsule" => y < rx ? rx * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((rx - y) / rx, 2f))) : y > h - rx ? rx * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((y - h + rx) / rx, 2f))) : rx,
                    "drop" => y < ry * 0.8f * 1.8f ? rx * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((y - ry * 0.8f) / (ry * 0.8f), 2f))) : rx * 0.3f,
                    _ => rx * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t)),
                };
            }
            float FrontZ(float x, float y)
            {
                var hw = Mathf.Max(1e-4f, Half(y));
                if (r.shape == "box") return rz * (1f - 0.25f * Mathf.Pow(Mathf.Abs(x) / rx, 3f)) * (1f - 0.2f * Mathf.Pow(Mathf.Abs(y - ry) / ry, 4f));
                var u = Mathf.Clamp01(Mathf.Abs(x) / hw);
                return rz * (hw / rx) * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
            }

            // ---- bands round the shape, a hair proud of it
            foreach (var band in r.bands)
            {
                var y0 = band.y * h; var bh = Mathf.Max(h * 0.03f, band.h * h);
                var col = C(band.color, second);
                if (r.shape == "box") b.RoundBox(new Vector3(0f, y0 + bh * 0.5f, 0f), new Vector3(w * 1.015f, bh, d * 1.015f), col, Mathf.Max(0.22f, round), 18);
                else
                {
                    var hw = Half(y0 + bh * 0.5f) * 1.03f;
                    if (hw > 1e-3f) b.Frustum(new Vector3(0f, y0, 0f), Half(y0) * 1.03f + 1e-4f, bh, Half(y0 + bh) * 1.03f + 1e-4f, col, rz / rx, 22, false);
                }
            }
            // ---- a front panel / screen
            Rect panelRect = default; var panelFront = float.MinValue;
            if ((r.panel == null || r.panel.w <= 0.05f) && r.parts.Exists(q => q.kind == "screen"))
                r.panel = new Panel { y = Mathf.Clamp(r.eyes?.y ?? 0.55f, 0.35f, 0.7f) - 0.05f, w = 0.74f, h = 0.5f, color = r.parts.Find(q => q.kind == "screen").color };
            if (r.panel != null && r.panel.w > 0.05f)
            {
                var py = r.panel.y * h; var pw = r.panel.w * w * 0.92f; var ph = r.panel.h * h * 0.92f;
                panelRect = new Rect(-pw * 0.5f, py - ph * 0.5f, pw, ph); panelFront = FrontZ(0f, py) - d * 0.02f + d * 0.05f;
                b.M = Matrix4x4.TRS(new Vector3(0f, py, FrontZ(0f, py) - d * 0.02f), Quaternion.identity, Vector3.one);
                b.RoundBox(Vector3.zero, new Vector3(pw, ph, d * 0.1f), C(r.panel.color, second), 0.3f, 14);
                b.M = Matrix4x4.identity;
            }

            // ---- the object's parts
            foreach (var p in r.parts) BuildPart(b, p, w, h, d, main, second, dark, Half, FrontZ);

            // ---- the face, on the panel where it lies on one
            float FaceZ(float x, float y) => panelRect.Contains(new Vector2(x, y)) ? Mathf.Max(FrontZ(x, y), panelFront) : FrontZ(x, y);
            var e = r.eyes ?? new Eyes();
            if (r.parts.Exists(q => q.kind is "snout" or "beak") && e.y < 0.5f) e.y = 0.56f;   // above the snout, not behind it
            var ey = Mathf.Clamp(e.y, 0.2f, 0.85f) * h;
            var es = Mathf.Clamp(e.size, 0.08f, 0.4f) * w * 0.5f;
            var gap = Mathf.Clamp(e.gap, 0.1f, 0.6f) * w * 0.5f;
            var iris = C(e.iris, new Color(0.15f, 0.15f, 0.2f));
            var xs = e.style == "cyclops" ? new[] { 0f } : new[] { -gap, gap };
            if (e.style == "cyclops") es *= 1.8f;
            foreach (var x in xs)
            {
                var z = FaceZ(x, ey) + es * 0.12f;
                var side = x < 0 ? -1f : 1f;
                var ys = e.style is "sharp" ? 0.72f : e.style is "sleepy" ? 0.85f : 1.1f;
                if (e.style == "happy")
                {
                    // ^ ^ : an arc of dark
                    var path = new Vector3[7];
                    for (var i = 0; i < 7; i++) { var t = i / 6f; var ax = Mathf.Lerp(-es, es, t); path[i] = new Vector3(x + ax, ey + es * 0.6f * Mathf.Sin(t * Mathf.PI) - es * 0.2f, FrontZ(x + ax, ey) + es * 0.08f); }
                    Tube(b, path, es * 0.16f, es * 0.16f, dark);
                    continue;
                }
                b.M = Matrix4x4.TRS(new Vector3(x, ey, z - es * 0.15f), Quaternion.Euler(0f, side * -Mathf.Rad2Deg * Mathf.Atan2(Mathf.Abs(x), rz * 2f) * 0.6f, 0f), Vector3.one);
                b.Ellipsoid(Vector3.zero, new Vector3(es, es * ys, es * 0.45f), Color.white, 16);
                b.Ellipsoid(new Vector3(side * es * 0.06f, -es * 0.05f, es * 0.34f), new Vector3(es * 0.62f, es * 0.68f * ys, es * 0.16f), iris, 14);
                b.Ellipsoid(new Vector3(side * es * 0.06f, -es * 0.05f, es * 0.44f), new Vector3(es * 0.32f, es * 0.36f * ys, es * 0.1f), Color.Lerp(iris, Color.black, 0.75f), 12);
                b.Ellipsoid(new Vector3(-es * 0.22f, es * 0.26f * ys, es * 0.5f), new Vector3(es * 0.17f, es * 0.17f, es * 0.06f), Color.white, 8);
                if (e.style == "sleepy")
                    b.Ellipsoid(new Vector3(0f, es * 0.28f, es * 0.06f), new Vector3(es * 1.08f, es * 0.72f, es * 0.52f), main, 14, _ => Mathf.PI * 0.5f);
                if (r.brows && e.style is "angry" or "sharp" or "cyclops" or "round")
                {
                    var tilt = e.style == "round" ? side * -6f : side * 22f;   // inner ends down: a scowl
                    b.M = Matrix4x4.TRS(new Vector3(x, ey + es * ys * 1.05f, z + es * 0.12f), Quaternion.Euler(0f, 0f, tilt), Vector3.one);
                    b.RoundBox(Vector3.zero, new Vector3(es * 2.1f, es * 0.34f, es * 0.4f), dark, 0.45f, 10);
                }
                b.M = Matrix4x4.identity;
            }
            // the mouth
            var my = Mathf.Min(Mathf.Clamp(r.mouth_y, 0.05f, 0.6f) * h, ey - es * 1.7f);   // always under the eyes
            var mw = w * 0.2f;
            Vector3 On(float x, float y, float lift) => new(x, y, FaceZ(x, y) + lift);
            switch (r.mouth)
            {
                case "none": break;
                case "open":
                case "grin":
                case "teeth":
                case "fangs":
                {
                    var open = r.mouth is "open" ? 0.55f : 0.34f;
                    b.M = Matrix4x4.TRS(On(0f, my, -mw * 0.1f), Quaternion.identity, Vector3.one);
                    b.Ellipsoid(Vector3.zero, new Vector3(mw * 1.05f, mw * open, mw * 0.3f), Color.Lerp(dark, new Color(0.45f, 0.08f, 0.12f), 0.5f), 14);
                    if (r.mouth is "open") b.Ellipsoid(new Vector3(0f, -mw * open * 0.4f, mw * 0.12f), new Vector3(mw * 0.55f, mw * 0.25f, mw * 0.18f), new Color(0.95f, 0.45f, 0.5f), 10);
                    if (r.mouth is "grin" or "teeth") b.RoundBox(new Vector3(0f, mw * open * 0.35f, mw * 0.2f), new Vector3(mw * 1.6f, mw * open * 0.55f, mw * 0.12f), Color.white, 0.3f, 10);
                    if (r.mouth is "fangs")
                        foreach (var sx in new[] { -1f, 1f })
                        {
                            b.M = Matrix4x4.TRS(On(sx * mw * 0.55f, my + mw * open * 0.5f, mw * 0.05f), Quaternion.Euler(180f, 0f, 0f), Vector3.one);
                            b.Frustum(Vector3.zero, mw * 0.18f, mw * 0.42f, mw * 0.01f, Color.white, 0.7f, 8);
                        }
                    b.M = Matrix4x4.identity;
                    break;
                }
                default:
                {
                    // a line: a smile curves up at the ends, a frown down, a wavy one zig-zags
                    var path = new Vector3[9];
                    for (var i = 0; i < 9; i++)
                    {
                        var t = i / 8f; var x = Mathf.Lerp(-mw, mw, t); var u = t * 2f - 1f;
                        var dy = r.mouth switch { "smile" => u * u * mw * 0.35f - mw * 0.1f, "frown" => -u * u * mw * 0.3f + mw * 0.1f, _ => Mathf.Sin(t * Mathf.PI * 3f) * mw * 0.14f };
                        path[i] = On(x, my + dy, mw * 0.04f);
                    }
                    Tube(b, path, mw * 0.08f, mw * 0.08f, dark);
                    break;
                }
            }
            if (r.cheeks)
                foreach (var sx in new[] { -1f, 1f })
                {
                    var cx = sx * (gap + es * 0.7f); var cy = ey - es * 1.3f;
                    b.M = Matrix4x4.TRS(On(cx, cy, -es * 0.05f), Quaternion.identity, Vector3.one);
                    b.Ellipsoid(Vector3.zero, new Vector3(es * 0.55f, es * 0.32f, es * 0.14f), new Color(1f, 0.6f, 0.66f), 10);
                    b.M = Matrix4x4.identity;
                }
            return b.Bake("enemyhead:" + r.id);
        }

        static void BuildPart(MeshKit.Builder b, Part p, float w, float h, float d, Color main, Color second, Color dark,
                              System.Func<float, float> half, System.Func<float, float, float> frontZ)
        {
            var col = C(p.color, second);
            var s = Mathf.Clamp(p.size, 0.05f, 0.8f) * w;
            var sides = p.at == "both" ? new[] { -1f, 1f } : p.at == "left" ? new[] { -1f } : p.at == "right" ? new[] { 1f } : new[] { 0f };
            foreach (var sx in sides)
            {
                // the anchor on the surface for this placement
                Vector3 at; Vector3 outward;
                switch (p.at)
                {
                    // the face is the front's middle: front parts sit low on it (an output tray, a slot, a sheet)
                    case "front":
                    {
                        // upright, sticking forward out of the low front: a tray is a shelf, paper a printout leaning out
                        var fy = h * 0.14f; at = new Vector3(0f, fy, frontZ(0f, fy)); outward = Vector3.up;
                        if (p.kind == "paper") outward = new Vector3(0f, 0.35f, 1f).normalized;
                        if (p.kind is "button" or "lens" or "handle" or "chain") outward = Vector3.forward;
                        break;
                    }
                    case "back": at = new Vector3(0f, h * 0.55f, -frontZ(0f, h * 0.55f)); outward = Vector3.back; break;
                    case "top": at = new Vector3(0f, h * 0.98f, 0f); outward = Vector3.up; break;
                    default:
                        if (p.kind is "ear_round" or "ear_pointed" or "ear_long" or "horn" or "antenna" or "wing" or "leaf" or "tuft" or "flame" or "bow")
                        { var y = h * 0.86f; at = new Vector3(sx * half(y) * 0.8f, y, 0f); outward = new Vector3(sx * 0.6f, 0.8f, 0f).normalized; }
                        else { var y = h * 0.5f; at = new Vector3(sx * half(y), y, 0f); outward = new Vector3(sx, 0f, 0f); }
                        break;
                }
                var rot = Quaternion.FromToRotation(Vector3.up, outward);
                b.M = Matrix4x4.TRS(at, rot, Vector3.one);
                switch (p.kind)
                {
                    case "ear_round": b.Ellipsoid(new Vector3(0f, s * 0.4f, 0f), new Vector3(s * 0.55f, s * 0.55f, s * 0.22f), col, 14);
                        b.Ellipsoid(new Vector3(0f, s * 0.42f, s * 0.12f), new Vector3(s * 0.34f, s * 0.34f, s * 0.12f), Color.Lerp(col, new Color(1f, 0.7f, 0.75f), 0.6f), 10); break;
                    case "ear_pointed": b.Frustum(Vector3.zero, s * 0.42f, s * 1.0f, s * 0.03f, col, 0.45f, 12); break;
                    case "ear_long": b.Ellipsoid(new Vector3(0f, s * 0.9f, 0f), new Vector3(s * 0.32f, s * 1.0f, s * 0.16f), col, 14);
                        b.Ellipsoid(new Vector3(0f, s * 0.9f, s * 0.1f), new Vector3(s * 0.18f, s * 0.78f, s * 0.08f), new Color(1f, 0.75f, 0.8f), 10); break;
                    case "horn":
                        b.Frustum(Vector3.zero, s * 0.3f, s * 0.55f, s * 0.2f, col, 1f, 12);
                        b.M = Matrix4x4.TRS(at + rot * new Vector3(0f, s * 0.55f, 0f), rot * Quaternion.Euler(0f, 0f, -Mathf.Sign(sx == 0 ? 1 : sx) * 28f), Vector3.one);
                        b.Frustum(Vector3.zero, s * 0.2f, s * 0.55f, s * 0.02f, col, 1f, 12); break;
                    case "antenna": b.Frustum(Vector3.zero, s * 0.06f, s * 1.1f, s * 0.05f, dark, 1f, 8);
                        b.Ellipsoid(new Vector3(0f, s * 1.15f, 0f), Vector3.one * s * 0.2f, col, 10); break;
                    case "crown":
                    {
                        var rr = Mathf.Min(w * 0.3f, s * 1.2f);
                        b.Frustum(Vector3.zero, rr, s * 0.35f, rr * 1.1f, col, 1f, 16);
                        for (var i = 0; i < 5; i++)
                        {
                            var a = i / 5f * Mathf.PI * 2f + Mathf.PI / 2f;
                            b.Ellipsoid(new Vector3(Mathf.Cos(a) * rr * 1.05f, s * 0.5f, Mathf.Sin(a) * rr * 1.05f), new Vector3(s * 0.12f, s * 0.28f, s * 0.12f), col, 8);
                            b.Ellipsoid(new Vector3(Mathf.Cos(a) * rr * 1.1f, s * 0.18f, Mathf.Sin(a) * rr * 1.1f), Vector3.one * s * 0.08f, new Color(0.9f, 0.2f, 0.3f), 6);
                        }
                        break;
                    }
                    case "handle":
                    {
                        b.M = Matrix4x4.identity;
                        var path = new Vector3[9];
                        for (var i = 0; i < 9; i++) { var a = Mathf.Lerp(0.15f, Mathf.PI - 0.15f, i / 8f); path[i] = at + new Vector3(Mathf.Cos(a) * s * 1.1f, Mathf.Sin(a) * s * 1.1f - s * 0.1f, 0f); }
                        Tube(b, path, s * 0.14f, s * 0.14f, col); break;
                    }
                    case "tray": b.RoundBox(new Vector3(0f, s * 0.1f, 0f), new Vector3(w * 0.75f, s * 0.2f, s * 0.9f), col, 0.25f, 10);
                        b.RoundBox(new Vector3(0f, s * 0.22f, 0f), new Vector3(w * 0.66f, s * 0.08f, s * 0.8f), Color.white, 0.2f, 8); break;
                    case "slot": b.RoundBox(new Vector3(0f, 0.002f, 0f), new Vector3(w * 0.6f, s * 0.12f, s * 0.35f), dark, 0.25f, 8); break;
                    case "button":
                        for (var i = 0; i < 3; i++) b.Frustum(new Vector3((i - 1) * s * 0.4f, 0f, 0f), s * 0.12f, s * 0.1f, s * 0.12f, i == 1 ? new Color(0.9f, 0.3f, 0.3f) : col, 1f, 10);
                        break;
                    case "screen": break;   // the face's panel (see Build)
                    case "spike":
                    {
                        b.M = Matrix4x4.identity;
                        for (var i = 0; i < 12; i++)
                        {
                            var th = (i % 6) / 6f * Mathf.PI * 2f; var ph = i < 6 ? 0.55f : 1.25f;
                            var dir = new Vector3(Mathf.Sin(ph) * Mathf.Cos(th), Mathf.Cos(ph), Mathf.Sin(ph) * Mathf.Sin(th));
                            var basep = new Vector3(0f, h * 0.5f, 0f) + Vector3.Scale(dir, new Vector3(w * 0.48f, h * 0.48f, d * 0.48f));
                            b.M = Matrix4x4.TRS(basep, Quaternion.FromToRotation(Vector3.up, dir), Vector3.one);
                            b.Frustum(Vector3.zero, s * 0.22f, s * 0.55f, s * 0.02f, col, 1f, 8);
                            b.Ellipsoid(new Vector3(0f, s * 0.55f, 0f), Vector3.one * s * 0.1f, Color.Lerp(col, Color.white, 0.4f), 6);
                        }
                        break;
                    }
                    case "leaf": b.Ellipsoid(new Vector3(0f, s * 0.6f, 0f), new Vector3(s * 0.35f, s * 0.7f, s * 0.08f), col, 12); break;
                    case "flame":
                        for (var i = 0; i < 3; i++)
                        {
                            b.M = Matrix4x4.TRS(at + new Vector3((i - 1) * s * 0.35f, 0f, 0f), Quaternion.Euler(0f, 0f, (i - 1) * -18f), Vector3.one);
                            b.Frustum(Vector3.zero, s * 0.3f, s * (i == 1 ? 1.1f : 0.75f), s * 0.02f, i == 1 ? new Color(1f, 0.85f, 0.3f) : col, 0.8f, 10);
                        }
                        break;
                    case "tuft":
                        for (var i = 0; i < 3; i++) b.Ellipsoid(new Vector3((i - 1) * s * 0.3f, s * 0.35f, 0f), new Vector3(s * 0.18f, s * 0.45f, s * 0.18f), col, 8);
                        break;
                    case "wing": b.M = Matrix4x4.TRS(at, rot * Quaternion.Euler(-30f, 0f, 0f), Vector3.one);
                        b.Ellipsoid(new Vector3(0f, s * 0.7f, -s * 0.2f), new Vector3(s * 0.28f, s * 0.8f, s * 0.08f), col, 12); break;
                    case "cap":
                    {
                        b.M = Matrix4x4.identity;
                        var cr = w * (0.6f + p.size * 0.5f);
                        b.Ellipsoid(new Vector3(0f, h * 0.8f, 0f), new Vector3(cr, h * 0.42f, cr * 0.95f), col, 22, _ => Mathf.PI * 0.5f);
                        b.Disc(new Vector3(0f, h * 0.8f, 0f), cr, cr * 0.95f, Color.Lerp(col, Color.white, 0.5f), false, 22);
                        for (var i = 0; i < 6; i++) { var a = i / 6f * Mathf.PI * 2f; b.Ellipsoid(new Vector3(Mathf.Cos(a) * cr * 0.6f, h * 0.8f + h * 0.3f, Mathf.Sin(a) * cr * 0.55f), new Vector3(cr * 0.16f, h * 0.08f, cr * 0.16f), Color.white, 8); }
                        break;
                    }
                    case "bow":
                        foreach (var bx in new[] { -1f, 1f }) b.Ellipsoid(new Vector3(bx * s * 0.45f, s * 0.2f, 0f), new Vector3(s * 0.42f, s * 0.26f, s * 0.14f), col, 10);
                        b.Ellipsoid(new Vector3(0f, s * 0.2f, 0f), Vector3.one * s * 0.14f, Color.Lerp(col, Color.black, 0.2f), 8); break;
                    case "chain":
                        for (var i = 0; i < 4; i++) b.Frustum(new Vector3(0f, i * s * 0.35f, 0f), s * 0.18f, s * 0.08f, s * 0.18f, new Color(0.7f, 0.72f, 0.78f), 0.5f, 10, false);
                        break;
                    case "lid": b.M = Matrix4x4.identity; b.RoundBox(new Vector3(0f, h * 0.98f, 0f), new Vector3(w * 1.06f, h * 0.1f, d * 1.06f), col, 0.3f, 14); break;
                    case "paper": b.RoundBox(new Vector3(0f, s * 0.45f, 0f), new Vector3(w * 0.5f, s * 0.9f, s * 0.05f), Color.white, 0.15f, 8); break;
                    case "lens": b.Ellipsoid(new Vector3(0f, s * 0.1f, 0f), new Vector3(s * 0.45f, s * 0.2f, s * 0.45f), new Color(0.55f, 0.8f, 1f), 14); break;
                    case "beak": b.M = Matrix4x4.TRS(new Vector3(0f, h * 0.38f, frontZ(0f, h * 0.38f)), Quaternion.FromToRotation(Vector3.up, Vector3.forward), Vector3.one);
                        b.Frustum(Vector3.zero, s * 0.3f, s * 0.55f, s * 0.02f, new Color(1f, 0.7f, 0.2f), 0.8f, 10); break;
                    case "snout": b.M = Matrix4x4.TRS(new Vector3(0f, h * 0.3f, frontZ(0f, h * 0.3f) - s * 0.1f), Quaternion.identity, Vector3.one);
                        b.Ellipsoid(Vector3.zero, new Vector3(s * 0.55f, s * 0.38f, s * 0.35f), col, 14);
                        foreach (var nx in new[] { -1f, 1f }) b.Ellipsoid(new Vector3(nx * s * 0.18f, 0f, s * 0.3f), Vector3.one * s * 0.07f, dark, 6); break;
                    case "tongue": b.M = Matrix4x4.TRS(new Vector3(0f, h * 0.22f, frontZ(0f, h * 0.22f)), Quaternion.Euler(20f, 0f, 0f), Vector3.one);
                        b.Ellipsoid(new Vector3(0f, -s * 0.2f, s * 0.1f), new Vector3(s * 0.28f, s * 0.35f, s * 0.08f), new Color(0.95f, 0.4f, 0.5f), 10); break;
                    case "tail": b.Frustum(Vector3.zero, s * 0.2f, s * 0.8f, s * 0.04f, col, 1f, 10); break;
                }
                b.M = Matrix4x4.identity;
            }
        }

        static void Tube(MeshKit.Builder b, Vector3[] path, float r0, float r1, Color col)
        {
            var n = path.Length - 1;
            b.Grid(n * 3, 8, (u, t) =>
            {
                var x = u * n; var i = Mathf.Min(n - 1, (int)x); var f = x - i;
                var p = Vector3.Lerp(path[i], path[i + 1], f);
                var along = (path[i + 1] - path[i]).normalized;
                var side = Vector3.Cross(along, Mathf.Abs(along.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
                var up2 = Vector3.Cross(side, along);
                var ang = t * Mathf.PI * 2f;
                var nrm = side * Mathf.Cos(ang) + up2 * Mathf.Sin(ang);
                return (p + nrm * Mathf.Lerp(r0, r1, u), nrm, new Vector2(u, t));
            }, col);
        }
    }
}
