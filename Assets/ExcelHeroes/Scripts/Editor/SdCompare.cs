using System.IO;
using System.Linq;
using ExcelHeroes.World;
using UnityEditor;
using UnityEngine;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// The quality gap, on one page: every sample SD model under Assets/_Ref rendered raw with
    /// its own sheets (row 1 full, row 2 head, row 3 three-quarter), then our characters the same
    /// way (rows 4–6), same scale and camera. Batch mode without -nographics:
    ///   SD_IDS=a,b,… SD_PREVIEW_OUT=… Unity -batchmode -quit -executeMethod ExcelHeroes.EditorTools.SdCompare.Run
    /// → compare.png. The samples are third-party reference: never shipped, never committed.
    /// </summary>
    public static class SdCompare
    {
        public static void Run()
        {
            var outDir = System.Environment.GetEnvironmentVariable("SD_PREVIEW_OUT") ?? Path.Combine(Application.dataPath, "..", "tools", "out", "sd3d");
            Directory.CreateDirectory(outDir);
            var ids = (System.Environment.GetEnvironmentVariable("SD_IDS") ?? "intern,ceo,cfo,barista,welfare,hr_jung,guard,pm_lead").Split(',');
            Shader.SetGlobalVector("_EhLightDir", new Vector4(-0.45f, 0.85f, -0.5f, 0f));
            if (!ExcelHeroes.Data.GameData.Loaded) ExcelHeroes.Data.GameData.Load();
            var samples = AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Ref" }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.Contains("Halo") && p.EndsWith(".fbx")).OrderBy(p => p).ToArray();
            const int W = 220, H = 300;
            var cols = Mathf.Max(samples.Length, ids.Length);
            var sheet = new Texture2D(W * cols, H * 6, TextureFormat.RGB24, false);
            var grey = Enumerable.Repeat(new Color(0.25f, 0.25f, 0.28f), W * H * cols * 6).ToArray();
            sheet.SetPixels(grey);
            void Put(int col, int row, Texture2D img) { sheet.SetPixels(W * col, H * (5 - row), W, H, img.GetPixels()); Object.DestroyImmediate(img); }

            for (var i = 0; i < samples.Length; i++)
            {
                var holder = new GameObject("sample").transform;
                var ok = RawSample(samples[i], holder);
                if (!ok) { Object.DestroyImmediate(holder.gameObject); continue; }
                holder.rotation = Quaternion.Euler(0f, 180f, 0f); Put(i, 0, Shoot(W, H, 0.55f, 0.5f));
                Put(i, 1, Shoot(W, H, 0.22f, 0.8f));
                holder.rotation = Quaternion.Euler(0f, 145f, 0f); Put(i, 2, Shoot(W, H, 0.55f, 0.5f));
                Object.DestroyImmediate(holder.gameObject);
                Debug.Log("[SdCompare] sample " + samples[i]);
            }
            for (var i = 0; i < ids.Length; i++)
            {
                var holder = new GameObject("ours").transform;
                var rig = SdRef.Build(ids[i], holder, 0);
                if (rig == null) { Object.DestroyImmediate(holder.gameObject); continue; }
                SdPose.Apply(rig, ExcelHeroes.World.Pose.Rest);
                rig.Root.rotation = Quaternion.Euler(0f, 180f, 0f); Put(i, 3, Shoot(W, H, 0.55f, 0.5f));
                Put(i, 4, Shoot(W, H, 0.22f, 0.8f));
                rig.Root.rotation = Quaternion.Euler(0f, 145f, 0f); Put(i, 5, Shoot(W, H, 0.55f, 0.5f));
                Object.DestroyImmediate(holder.gameObject);
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(outDir, "compare.png"), sheet.EncodeToPNG());
            Debug.Log("[SdCompare] done");
        }

        /// <summary>
        /// A sample FBX as it is, dressed in its own sheets from the same folder (the FBX's
        /// materials carry no textures): body / face / hair / eyemouth by material name, on our
        /// toon shader with the eye plates cut by alpha where the sheet has any.
        /// </summary>
        static bool RawSample(string path, Transform holder)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return false;
            var go = Object.Instantiate(prefab, holder);
            var rends = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (rends.Length == 0) return false;
            var body = rends.OrderByDescending(r => r.sharedMesh.subMeshCount).First();
            var dir = Path.GetDirectoryName(path).Replace('\\', '/');
            var pngs = Directory.GetFiles(Path.GetFullPath(Path.Combine(Application.dataPath, "..", dir)), "*.png").Select(p => p.Replace('\\', '/')).ToArray();
            Texture2D Tex(params string[] keys)
            {
                foreach (var k in keys)
                {
                    var hit = pngs.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).ToLowerInvariant().EndsWith(k) && !p.ToLowerInvariant().Contains("mask") && !p.ToLowerInvariant().Contains("spec"));
                    if (hit != null)
                    {
                        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(hit.Substring(hit.LastIndexOf("Assets/")));
                        if (t == null) Debug.LogWarning("[SdCompare] no texture at " + hit);
                        return t;
                    }
                }
                return null;
            }
            foreach (var r in rends)
            {
                var keep = r == body || r.name.ToLowerInvariant().Contains("hair") || r.name.ToLowerInvariant().Contains("face");
                if (!keep) { r.gameObject.SetActive(false); continue; }
                var mats = new Material[r.sharedMesh.subMeshCount];
                for (var i = 0; i < mats.Length; i++)
                {
                    var n = (i < r.sharedMaterials.Length && r.sharedMaterials[i] ? r.sharedMaterials[i].name : r.name).ToLowerInvariant();
                    Texture2D tex = n.Contains("eyemouth") ? Tex("eyemouth", "body_eyemouth") : n.Contains("hair") ? Tex("hair", "hair_2") : n.Contains("face") || n.Contains("eyebrow") ? Tex("face") : Tex("body");
                    var m = MeshKit.NewToon(n.Contains("eye") ? 0f : 0.004f, tex);
                    // the rips' eye sheets lost their alpha (all 255): draw the plates opaque, no hull
                    if (n.Contains("eye")) { m.SetFloat("_Cutoff", tex != null && HasAlpha(tex) ? 0.5f : 0f); m.SetFloat("_OutlineWidth", 0f); m.SetFloat("_ShadeStrength", 0.05f); }
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
                r.updateWhenOffscreen = true;
            }
            var wb = body.bounds;
            var s = SdRef.Height / wb.size.y;
            go.transform.localScale = Vector3.one * s;
            var pelvis = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Bip001 Pelvis");
            var cx = pelvis != null ? pelvis.position.x : wb.center.x; var cz = pelvis != null ? pelvis.position.z : wb.center.z;
            go.transform.localPosition = new Vector3(-cx * s, -wb.min.y * s, -cz * s);
            return true;
        }

        static bool HasAlpha(Texture2D t)
        {
            try { var px = t.GetPixels32(); for (var i = 0; i < px.Length; i += 37) if (px[i].a < 128) return true; return false; }
            catch (System.Exception) { return false; }
        }

        static Texture2D Shoot(int w, int hh, float size, float centre)
        {
            var go = new GameObject("cam");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = SdRef.Height * size;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.62f, 0.85f);
            cam.transform.position = new Vector3(0f, SdRef.Height * centre, -5f);
            var rt = new RenderTexture(w, hh, 24) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.aspect = w / (float)hh;
            cam.Render();
            RenderTexture.active = rt;
            var t = new Texture2D(w, hh, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, w, hh), 0, 0); t.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
            return t;
        }
    }
}
