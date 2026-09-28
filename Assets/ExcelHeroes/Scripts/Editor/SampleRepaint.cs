using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExcelHeroes.World;
using UnityEngine;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// Each hero's own outfit painted onto the sample body they wear, from their illustration:
    ///
    ///   Views — renders the hero's sample body (recoloured, flat, body submeshes only) from the
    ///           front, back, left and right → tools/out/samplepaint/views/&lt;id&gt;_&lt;view&gt;.png
    ///   (tools/gen_samplepaint_gemini.py has Gemini repaint each view as the illustration's outfit)
    ///   Bake  — projects the painted views back into the body sheet's UVs, depth-tested per view,
    ///           and writes Resources/Art/SDBase/painted/&lt;id&gt;.png (git-ignored: derived from the
    ///           sample's UV layout), which SdSample wears instead of the recolour.
    ///
    ///   Unity -batchmode -quit -executeMethod ExcelHeroes.EditorTools.SampleRepaint.Views   (no -nographics)
    ///   Unity -batchmode -quit -executeMethod ExcelHeroes.EditorTools.SampleRepaint.Bake
    /// SD_IDS=a,b limits the cast.
    /// </summary>
    public static class SampleRepaint
    {
        const int ViewPx = 1024;
        const float Ortho = 1.2f * 0.55f, CentreY = 0.6f;
        static readonly (string name, float yaw)[] ViewsDef = { ("front", 180f), ("back", 0f), ("left", 270f), ("right", 90f) };
        static string Root => Path.Combine(Application.dataPath, "..", "tools", "out", "samplepaint");

        static IEnumerable<string> Ids()
        {
            if (!Data.GameData.Loaded) Data.GameData.Load();
            var env = System.Environment.GetEnvironmentVariable("SD_IDS");
            var all = Data.GameData.Heroes.Select(h => h.id).Where(id => SdSample.Has(SdLook.For(id).Body));
            return string.IsNullOrEmpty(env) ? all : env.Split(',').Where(id => SdSample.Has(SdLook.For(id).Body));
        }

        // the rig with everything but the body submeshes hidden, and the body sheet
        static (ChibiRig rig, SkinnedMeshRenderer body, int[] subs) Build(string id, Transform holder)
        {
            SdSample.SkipPainted = true;
            var rig = SdRef.Build(id, holder, 0);
            SdSample.SkipPainted = false;
            var body = rig.FaceRenderer as SkinnedMeshRenderer;
            var subs = new List<int>();
            var mats = body.sharedMaterials;
            for (var i = 0; i < mats.Length; i++)
                if (mats[i] != null && mats[i].mainTexture != null && mats[i].mainTexture.name.Contains("+outfit")) subs.Add(i);
            return (rig, body, subs.ToArray());
        }

        public static void Views()
        {
            var dir = Path.Combine(Root, "views"); Directory.CreateDirectory(dir);
            foreach (var id in Ids())
            {
                var holder = new GameObject("v").transform;
                var (rig, body, subs) = Build(id, holder);
                // flat: every renderer off but the body; on the body, only the outfit submeshes drawn
                foreach (var r in holder.GetComponentsInChildren<Renderer>(true)) if (r is not SkinnedMeshRenderer && r.name != "hair") r.enabled = false;
                var mats = body.sharedMaterials.Select((m, i) =>
                {
                    // the whole figure (head and hair too): Gemini paints a whole character anyway, and a
                    // headless render made it invent a head and throw the fit off; only the outfit is baked back
                    return MeshKit.NewGlass(m != null ? m.mainTexture : null);
                }).ToArray();
                body.sharedMaterials = mats;
                foreach (var (name, yaw) in ViewsDef)
                {
                    rig.Root.rotation = Quaternion.Euler(0f, yaw, 0f);
                    var t = Shoot();
                    File.WriteAllBytes(Path.Combine(dir, $"{id}_{name}.png"), t.EncodeToPNG());
                    Object.DestroyImmediate(t);
                }
                Object.DestroyImmediate(holder.gameObject);
                Debug.Log("[SampleRepaint] views " + id);
            }
        }

        static Texture2D Shoot()
        {
            var go = new GameObject("cam");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = Ortho; cam.aspect = 1f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.transform.position = new Vector3(0f, CentreY, -5f);
            var rt = new RenderTexture(ViewPx, ViewPx, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var t = new Texture2D(ViewPx, ViewPx, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, ViewPx, ViewPx), 0, 0); t.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
            return t;
        }

        public static void Bake()
        {
            var painted = Path.Combine(Root, "painted");
            var outDir = Path.Combine(Application.dataPath, "ExcelHeroes", "Resources", "Art", "SDBase", "painted");
            Directory.CreateDirectory(outDir);
            foreach (var id in Ids())
            {
                var views = new List<(float yaw, Color32[] px, int w, int h)>();
                foreach (var (name, yaw) in ViewsDef)
                {
                    var f = Path.Combine(painted, $"{id}_{name}.png");
                    if (!File.Exists(f)) continue;
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false); t.LoadImage(File.ReadAllBytes(f));
                    views.Add((yaw, Sharpen(t.GetPixels32(), t.width, t.height), t.width, t.height)); Object.DestroyImmediate(t);
                }
                if (views.Count == 0) { Debug.Log("[SampleRepaint] no painted views for " + id); continue; }
                var holder = new GameObject("b").transform;
                var (rig, body, subs) = Build(id, holder);
                var baseTex = body.sharedMaterials[subs[0]].mainTexture as Texture2D;
                // twice the sheet's own size: the paintings carry more detail than a 512 sheet holds (the
                // outfits read soft), the sample's own texels upsampled under them where no view reaches
                var W = baseTex.width * Up; var H = baseTex.height * Up;
                // the rest mesh in the root's space (root at the origin, unrotated)
                var baked = new Mesh(); body.BakeMesh(baked, true);
                var m2r = holder.worldToLocalMatrix * body.transform.localToWorldMatrix;
                var v = baked.vertices.Select(p => m2r.MultiplyPoint3x4(p)).ToArray();
                var n = baked.normals.Select(p => m2r.MultiplyVector(p).normalized).ToArray();
                var uv = body.sharedMesh.uv;
                var all = Enumerable.Range(0, body.sharedMesh.subMeshCount).SelectMany(s => body.sharedMesh.GetTriangles(s)).ToArray();
                var mats = views.Select(x => Matrix4x4.Rotate(Quaternion.Euler(0f, x.yaw, 0f))).ToList();
                var zbufs = mats.Select(m => DepthBuffer(v, all, m)).ToList();
                var atlas = new Color[W * H];
                for (var y = 0; y < H; y++) for (var x = 0; x < W; x++) atlas[y * W + x] = baseTex.GetPixelBilinear((x + 0.5f) / W, (y + 0.5f) / H);
                var wsum = new float[atlas.Length]; var acc = new Color[atlas.Length];
                foreach (var s in subs)
                {
                    var tris = body.sharedMesh.GetTriangles(s);
                    for (var t = 0; t < tris.Length; t += 3) Raster(tris[t], tris[t + 1], tris[t + 2], v, n, uv, views, mats, zbufs, acc, wsum, W, H);
                }
                for (var i = 0; i < atlas.Length; i++) if (wsum[i] > 0f) { var c = acc[i] / wsum[i]; c.a = atlas[i].a; atlas[i] = c; }
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false); tex.SetPixels(atlas); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, id + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex); Object.DestroyImmediate(holder.gameObject);
                Debug.Log($"[SampleRepaint] baked {id} from {views.Count} views");
            }
            UnityEditor.AssetDatabase.Refresh();
        }

        const int Up = 2;

        static Color Bilinear(Color32[] px, int w, int h, float x, float y)
        {
            int x0 = Mathf.Clamp((int)Mathf.Floor(x), 0, w - 1), y0 = Mathf.Clamp((int)Mathf.Floor(y), 0, h - 1);
            int x1 = Mathf.Min(w - 1, x0 + 1), y1 = Mathf.Min(h - 1, y0 + 1);
            float fx = Mathf.Clamp01(x - x0), fy = Mathf.Clamp01(y - y0);
            Color a = px[y0 * w + x0], b = px[y0 * w + x1], c = px[y1 * w + x0], d = px[y1 * w + x1];
            // an edge texel (alpha 0 beside) must not bleed the background in
            if (a.a < 0.5f || b.a < 0.5f || c.a < 0.5f || d.a < 0.5f) return px[Mathf.Clamp((int)(y + 0.5f), 0, h - 1) * w + Mathf.Clamp((int)(x + 0.5f), 0, w - 1)];
            return Color.Lerp(Color.Lerp(a, b, fx), Color.Lerp(c, d, fx), fy);
        }

        /// <summary>A light unsharp mask on a painted view: the image model paints soft, and a soft
        /// painting projected and filtered twice reads as a blur on the figure.</summary>
        static Color32[] Sharpen(Color32[] src, int w, int h)
        {
            var o = (Color32[])src.Clone();
            for (var y = 1; y < h - 1; y++)
                for (var x = 1; x < w - 1; x++)
                {
                    var i = y * w + x; var c = src[i]; if (c.a < 128) continue;
                    int r = 0, g = 0, b = 0, n = 0;
                    foreach (var j in new[] { i - 1, i + 1, i - w, i + w }) { var q = src[j]; if (q.a < 128) continue; r += q.r; g += q.g; b += q.b; n++; }
                    if (n < 4) continue;
                    const float k = 0.6f;
                    byte S(int v, int m) => (byte)Mathf.Clamp(Mathf.RoundToInt(v + k * (v - m / 4f)), 0, 255);
                    o[i] = new Color32(S(c.r, r), S(c.g, g), S(c.b, b), c.a);
                }
            return o;
        }

        static Vector2 Px(Vector3 w) => new(((w.x / Ortho) * 0.5f + 0.5f) * ViewPx, ((w.y - CentreY) / Ortho * 0.5f + 0.5f) * ViewPx);

        static float[] DepthBuffer(Vector3[] v, int[] tris, Matrix4x4 m)
        {
            var z = Enumerable.Repeat(float.MaxValue, ViewPx * ViewPx).ToArray();
            for (var t = 0; t < tris.Length; t += 3)
            {
                var a = m.MultiplyPoint3x4(v[tris[t]]); var b = m.MultiplyPoint3x4(v[tris[t + 1]]); var c = m.MultiplyPoint3x4(v[tris[t + 2]]);
                Vector2 A = Px(a), B = Px(b), C = Px(c);
                var det = (B.x - A.x) * (C.y - A.y) - (C.x - A.x) * (B.y - A.y);
                if (Mathf.Abs(det) < 1e-6f) continue;
                int x0 = Mathf.Max(0, (int)Mathf.Min(A.x, B.x, C.x)), x1 = Mathf.Min(ViewPx - 1, (int)Mathf.Max(A.x, B.x, C.x) + 1);
                int y0 = Mathf.Max(0, (int)Mathf.Min(A.y, B.y, C.y)), y1 = Mathf.Min(ViewPx - 1, (int)Mathf.Max(A.y, B.y, C.y) + 1);
                for (var y = y0; y <= y1; y++)
                    for (var x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        var w0 = ((B.x - px) * (C.y - py) - (C.x - px) * (B.y - py)) / det;
                        var w1 = ((C.x - px) * (A.y - py) - (A.x - px) * (C.y - py)) / det;
                        var w2 = 1f - w0 - w1;
                        if (w0 < -0.002f || w1 < -0.002f || w2 < -0.002f) continue;
                        var d = w0 * a.z + w1 * b.z + w2 * c.z; var i = y * ViewPx + x;
                        if (d < z[i]) z[i] = d;
                    }
            }
            return z;
        }

        static void Raster(int i0, int i1, int i2, Vector3[] v, Vector3[] n, Vector2[] uv, List<(float yaw, Color32[] px, int w, int h)> views,
                           List<Matrix4x4> mats, List<float[]> zbufs, Color[] acc, float[] wsum, int W, int H)
        {
            Vector2 A = new(uv[i0].x * W, uv[i0].y * H), B = new(uv[i1].x * W, uv[i1].y * H), C = new(uv[i2].x * W, uv[i2].y * H);
            var det = (B.x - A.x) * (C.y - A.y) - (C.x - A.x) * (B.y - A.y);
            if (Mathf.Abs(det) < 1e-9f) return;
            int x0 = Mathf.Max(0, (int)Mathf.Min(A.x, B.x, C.x) - 1), x1 = Mathf.Min(W - 1, (int)Mathf.Max(A.x, B.x, C.x) + 1);
            int y0 = Mathf.Max(0, (int)Mathf.Min(A.y, B.y, C.y) - 1), y1 = Mathf.Min(H - 1, (int)Mathf.Max(A.y, B.y, C.y) + 1);
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    var w0 = ((B.x - px) * (C.y - py) - (C.x - px) * (B.y - py)) / det;
                    var w1 = ((C.x - px) * (A.y - py) - (A.x - px) * (C.y - py)) / det;
                    var w2 = 1f - w0 - w1;
                    if (w0 < -0.1f || w1 < -0.1f || w2 < -0.1f) continue;
                    var pos = v[i0] * w0 + v[i1] * w1 + v[i2] * w2;
                    var nor = (n[i0] * w0 + n[i1] * w1 + n[i2] * w2).normalized;
                    var i = y * W + x;
                    for (var k = 0; k < views.Count; k++)
                    {
                        var m = mats[k];
                        var facing = -m.MultiplyVector(nor).z;
                        if (facing < 0.25f) continue;
                        var wp = m.MultiplyPoint3x4(pos);
                        var sx = (wp.x / Ortho) * 0.5f + 0.5f; var sy = ((wp.y - CentreY) / Ortho) * 0.5f + 0.5f;
                        if (sx < 0f || sx >= 1f || sy < 0f || sy >= 1f) continue;
                        var zi = Mathf.Clamp((int)(sy * ViewPx), 0, ViewPx - 1) * ViewPx + Mathf.Clamp((int)(sx * ViewPx), 0, ViewPx - 1);
                        if (wp.z > zbufs[k][zi] + 0.006f) continue;
                        var vw = views[k];
                        var c = Bilinear(vw.px, vw.w, vw.h, sx * vw.w - 0.5f, sy * vw.h - 0.5f);
                        if (c.a < 0.5f) continue;
                        // the view facing it most nearly wins: an even blend of four ghosted the seams
                        var wgt = Mathf.Pow(facing, 10f);
                        acc[i] += new Color(c.r, c.g, c.b) * wgt; wsum[i] += wgt;
                    }
                }
        }
    }
}
