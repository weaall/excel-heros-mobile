using System.Collections.Generic;
using System.IO;
using ExcelHeroes.World;
using UnityEngine;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// Texturing the common SD base by projection, in two halves round a Gemini pass:
    ///
    ///   Views — renders each character's untextured base (flat colours, no shading) from the
    ///           front, back, left and right into tools/out/sdtex/views/&lt;id&gt;_&lt;view&gt;.png (1024,
    ///           transparent), with the camera in views.json. tools/gen_sdtex_gemini.py then has
    ///           Nano Banana repaint every view as hand-painted cloth, hair and face.
    ///   Bake  — projects the painted views back onto the mesh through the same camera, with a
    ///           per-view depth test so hidden surfaces do not take the colour of what covers them,
    ///           and writes the atlas to Resources/Art/SDTex/&lt;id&gt;.png. SdBase picks it up.
    ///
    ///   Unity -batchmode -quit -executeMethod ExcelHeroes.EditorTools.SdTexBake.Views   (no -nographics)
    ///   Unity -batchmode -quit -nographics -executeMethod ExcelHeroes.EditorTools.SdTexBake.Bake
    /// SD_IDS=a,b,c limits the cast.
    /// </summary>
    public static class SdTexBake
    {
        const int ViewPx = 1024;
        const float Ortho = SdBase.Height * 0.55f;
        const float CentreY = SdBase.Height * 0.5f;
        static readonly (string name, float yaw)[] ViewsDef = { ("front", 180f), ("back", 0f), ("left", 270f), ("right", 90f) };

        static string Root => Path.Combine(Application.dataPath, "..", "tools", "out", "sdtex");
        static string[] Ids()
        {
            var env = System.Environment.GetEnvironmentVariable("SD_IDS");
            if (!string.IsNullOrEmpty(env)) return env.Split(',');
            var list = new List<string>();
            foreach (var t in Resources.LoadAll<Texture2D>("Art/SD")) list.Add(t.name);
            return list.ToArray();
        }

        public static void Views()
        {
            var dir = Path.Combine(Root, "views");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(Root, "views.json"), "{\"px\":" + ViewPx + ",\"ortho\":" + Ortho + ",\"centreY\":" + CentreY + "}");
            foreach (var id in Ids())
            {
                var holder = new GameObject("v").transform;
                var rig = SdBase.Build(id, holder, 0);
                var smr = rig.Renderers[0] as SkinnedMeshRenderer;
                // flat: vertex colour x atlas, no light, no outline
                smr.sharedMaterial = MeshKit.NewGlass(smr.sharedMaterial.mainTexture);
                foreach (var (name, yaw) in ViewsDef)
                {
                    rig.Root.rotation = Quaternion.Euler(0f, yaw, 0f);
                    var t = Shoot();
                    File.WriteAllBytes(Path.Combine(dir, $"{id}_{name}.png"), t.EncodeToPNG());
                    Object.DestroyImmediate(t);
                }
                Object.DestroyImmediate(holder.gameObject);
                Debug.Log("[SdTexBake] views " + id);
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

        // ---------------------------------------------------------------- bake --
        const int Atlas = 2048;

        public static void Bake()
        {
            var painted = Path.Combine(Root, "painted");
            var outDir = Path.Combine(Application.dataPath, "ExcelHeroes", "Resources", "Art", "SDTex");
            Directory.CreateDirectory(outDir);
            foreach (var id in Ids())
            {
                var views = new List<(float yaw, Color32[] px, int w, int h)>();
                foreach (var (name, yaw) in ViewsDef)
                {
                    var f = Path.Combine(painted, $"{id}_{name}.png");
                    if (!File.Exists(f)) continue;
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    t.LoadImage(File.ReadAllBytes(f));
                    views.Add((yaw, t.GetPixels32(), t.width, t.height));
                    Object.DestroyImmediate(t);
                }
                if (views.Count == 0) { Debug.Log("[SdTexBake] no painted views for " + id); continue; }

                var holder = new GameObject("b").transform;
                var rig = SdBase.Build(id, holder, 0);
                var smr = rig.Renderers[0] as SkinnedMeshRenderer;
                var mesh = smr.sharedMesh;
                var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv; var col = mesh.colors; var tris = mesh.triangles;

                // per-view depth buffers (model space, root at the origin, identity)
                var zbufs = new List<float[]>();
                var viewMats = new List<Matrix4x4>();
                foreach (var (yaw, _, _, _) in views)
                {
                    var m = Matrix4x4.Rotate(Quaternion.Euler(0f, yaw, 0f));
                    viewMats.Add(m);
                    zbufs.Add(DepthBuffer(v, tris, m));
                }

                var atlas = new Color[Atlas * Atlas];
                var filled = new bool[Atlas * Atlas];
                for (var t = 0; t < tris.Length; t += 3)
                    RasterTri(tris[t], tris[t + 1], tris[t + 2], v, n, uv, col, views, viewMats, zbufs, atlas, filled);
                Dilate(atlas, filled, 6);

                var tex = new Texture2D(Atlas, Atlas, TextureFormat.RGBA32, false);
                tex.SetPixels(atlas); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, id + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(holder.gameObject);
                Debug.Log($"[SdTexBake] baked {id} from {views.Count} views");
            }
            UnityEditor.AssetDatabase.Refresh();
        }

        static Vector2 Project(Vector3 p, Matrix4x4 m)
        {
            var w = m.MultiplyPoint3x4(p);
            return new Vector2((w.x / Ortho) * 0.5f + 0.5f, ((w.y - CentreY) / Ortho) * 0.5f + 0.5f);
        }

        static float[] DepthBuffer(Vector3[] v, int[] tris, Matrix4x4 m)
        {
            var z = new float[ViewPx * ViewPx];
            for (var i = 0; i < z.Length; i++) z[i] = float.MaxValue;
            for (var t = 0; t < tris.Length; t += 3)
            {
                var a = m.MultiplyPoint3x4(v[tris[t]]); var b = m.MultiplyPoint3x4(v[tris[t + 1]]); var c = m.MultiplyPoint3x4(v[tris[t + 2]]);
                Vector2 A = Px(a), B = Px(b), C = Px(c);
                var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.x, B.x, C.x))); var maxX = Mathf.Min(ViewPx - 1, Mathf.CeilToInt(Mathf.Max(A.x, B.x, C.x)));
                var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.y, B.y, C.y))); var maxY = Mathf.Min(ViewPx - 1, Mathf.CeilToInt(Mathf.Max(A.y, B.y, C.y)));
                var det = (B.x - A.x) * (C.y - A.y) - (C.x - A.x) * (B.y - A.y);
                if (Mathf.Abs(det) < 1e-6f) continue;
                for (var y = minY; y <= maxY; y++)
                    for (var x = minX; x <= maxX; x++)
                    {
                        var px = x + 0.5f; var py = y + 0.5f;
                        var w0 = ((B.x - px) * (C.y - py) - (C.x - px) * (B.y - py)) / det;
                        var w1 = ((C.x - px) * (A.y - py) - (A.x - px) * (C.y - py)) / det;
                        var w2 = 1f - w0 - w1;
                        if (w0 < -0.002f || w1 < -0.002f || w2 < -0.002f) continue;
                        var depth = w0 * a.z + w1 * b.z + w2 * c.z;      // camera looks along +z: smaller z is nearer
                        var i = y * ViewPx + x;
                        if (depth < z[i]) z[i] = depth;
                    }
            }
            return z;
        }

        static Vector2 Px(Vector3 w) => new(((w.x / Ortho) * 0.5f + 0.5f) * ViewPx, ((w.y - CentreY) / Ortho * 0.5f + 0.5f) * ViewPx);

        static void RasterTri(int i0, int i1, int i2, Vector3[] v, Vector3[] n, Vector2[] uv, Color[] col,
                              List<(float yaw, Color32[] px, int w, int h)> views, List<Matrix4x4> mats, List<float[]> zbufs, Color[] atlas, bool[] filled)
        {
            Vector2 A = uv[i0] * Atlas, B = uv[i1] * Atlas, C = uv[i2] * Atlas;
            var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.x, B.x, C.x)) - 1); var maxX = Mathf.Min(Atlas - 1, Mathf.CeilToInt(Mathf.Max(A.x, B.x, C.x)) + 1);
            var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.y, B.y, C.y)) - 1); var maxY = Mathf.Min(Atlas - 1, Mathf.CeilToInt(Mathf.Max(A.y, B.y, C.y)) + 1);
            var det = (B.x - A.x) * (C.y - A.y) - (C.x - A.x) * (B.y - A.y);
            if (Mathf.Abs(det) < 1e-9f) return;
            for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                {
                    var px = x + 0.5f; var py = y + 0.5f;
                    var w0 = ((B.x - px) * (C.y - py) - (C.x - px) * (B.y - py)) / det;
                    var w1 = ((C.x - px) * (A.y - py) - (A.x - px) * (C.y - py)) / det;
                    var w2 = 1f - w0 - w1;
                    // a little outside the triangle too, so the gutters have colour
                    if (w0 < -0.15f || w1 < -0.15f || w2 < -0.15f) continue;
                    var inside = w0 >= 0f && w1 >= 0f && w2 >= 0f;
                    var i = y * Atlas + x;
                    if (filled[i] && !inside) continue;
                    var pos = v[i0] * w0 + v[i1] * w1 + v[i2] * w2;
                    var nor = (n[i0] * w0 + n[i1] * w1 + n[i2] * w2).normalized;
                    var basec = col.Length > 0 ? col[i0] * w0 + col[i1] * w1 + col[i2] * w2 : Color.white;
                    var acc = Color.black; var wsum = 0f;
                    for (var k = 0; k < views.Count; k++)
                    {
                        var m = mats[k];
                        var wn = m.MultiplyVector(nor);
                        var facing = -wn.z;                        // toward the camera at -z
                        if (facing < 0.3f) continue;
                        var wp = m.MultiplyPoint3x4(pos);
                        var sx = (wp.x / Ortho) * 0.5f + 0.5f; var sy = ((wp.y - CentreY) / Ortho) * 0.5f + 0.5f;
                        if (sx < 0f || sx >= 1f || sy < 0f || sy >= 1f) continue;
                        var zi = Mathf.Clamp((int)(sy * ViewPx), 0, ViewPx - 1) * ViewPx + Mathf.Clamp((int)(sx * ViewPx), 0, ViewPx - 1);
                        if (wp.z > zbufs[k][zi] + 0.006f) continue;   // hidden behind something nearer
                        var c = Sample(views[k].px, views[k].w, views[k].h, sx, sy);
                        if (c.a < 0.5f) continue;
                        var w = Mathf.Pow(facing, 4f);
                        acc += new Color(c.r, c.g, c.b) * w; wsum += w;
                    }
                    var final = wsum > 0f ? acc / wsum : new Color(basec.r, basec.g, basec.b);
                    // the flat base colour is linear in the mesh; the atlas is sRGB
                    if (wsum <= 0f && QualitySettings.activeColorSpace == ColorSpace.Linear) final = final.gamma;
                    final.a = 1f;
                    atlas[i] = final; filled[i] = true;
                }
        }

        static Color Sample(Color32[] px, int w, int h, float u, float v)
        {
            var x = u * (w - 1); var y = v * (h - 1);
            int x0 = (int)x, y0 = (int)y; var x1 = Mathf.Min(x0 + 1, w - 1); var y1 = Mathf.Min(y0 + 1, h - 1);
            float fx = x - x0, fy = y - y0;
            Color c00 = px[y0 * w + x0], c10 = px[y0 * w + x1], c01 = px[y1 * w + x0], c11 = px[y1 * w + x1];
            return Color.Lerp(Color.Lerp(c00, c10, fx), Color.Lerp(c01, c11, fx), fy);
        }

        static void Dilate(Color[] atlas, bool[] filled, int passes)
        {
            var src = (bool[])filled.Clone();
            for (var p = 0; p < passes; p++)
            {
                var next = (bool[])src.Clone();
                for (var y = 1; y < Atlas - 1; y++)
                    for (var x = 1; x < Atlas - 1; x++)
                    {
                        var i = y * Atlas + x;
                        if (src[i]) continue;
                        Color acc = Color.black; var cnt = 0;
                        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            var j = (y + dy) * Atlas + x + dx;
                            if (!src[j]) continue;
                            acc += atlas[j]; cnt++;
                        }
                        if (cnt == 0) continue;
                        atlas[i] = acc / cnt; atlas[i].a = 1f; next[i] = true;
                    }
                src = next;
            }
        }
    }
}
