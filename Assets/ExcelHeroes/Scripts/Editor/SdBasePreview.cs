using System.IO;
using System.Linq;
using ExcelHeroes.World;
using UnityEngine;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// Line-up of characters on the common SD base (World/SdBase), front / three-quarter / side,
    /// one row per angle, into SD_PREVIEW_OUT/sdbase_lineup.png. Batch mode without -nographics:
    ///   Unity -batchmode -quit -executeMethod ExcelHeroes.EditorTools.SdBasePreview.Run
    /// SD_IDS=a,b,c picks the cast (default: a spread of hair styles and outfits).
    /// </summary>
    public static class SdBasePreview
    {
        public static void Run()
        {
            var outDir = System.Environment.GetEnvironmentVariable("SD_PREVIEW_OUT") ?? Path.Combine(Application.dataPath, "..", "tools", "out", "sd3d");
            Directory.CreateDirectory(outDir);
            var ids = (System.Environment.GetEnvironmentVariable("SD_IDS") ?? "intern,cfo,ceo,vlookup,hr_jung,guard,barista,cto,macro,welfare").Split(',');
            Shader.SetGlobalVector("_EhLightDir", new Vector4(-0.45f, 0.85f, -0.5f, 0f));
            const int W = 220, H = 300;
            float[] yaws = { 180f, 145f, 90f, 0f };
            // rows: front / three-quarter / side / back, then a head close-up (front)
            var sheet = new Texture2D(W * ids.Length, H * (yaws.Length + 1), TextureFormat.RGB24, false);
            for (var i = 0; i < ids.Length; i++)
            {
                var holder = new GameObject("preview").transform;
                // SD_RAW=1: the untouched sample (its own materials), the reference for every edit
                var rig = System.Environment.GetEnvironmentVariable("SD_RAW") == "1" ? RawSample(holder)
                        : SdRef.Build(ids[i], holder, 0) ?? SdBase.Build(ids[i], holder, 0);
                for (var a = 0; a < yaws.Length; a++)
                {
                    rig.Root.rotation = Quaternion.Euler(0f, yaws[a], 0f);
                    var img = Shoot(SdBase.Height, W, H);
                    sheet.SetPixels(W * i, H * (yaws.Length - a), W, H, img.GetPixels());
                    Object.DestroyImmediate(img);
                }
                rig.Root.rotation = Quaternion.Euler(0f, 180f, 0f);
                var head = Shoot(SdBase.Height, W, H, 0.22f, 0.8f);
                sheet.SetPixels(W * i, 0, W, H, head.GetPixels());
                Object.DestroyImmediate(head);
                Object.DestroyImmediate(holder.gameObject);
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(outDir, "sdbase_lineup.png"), sheet.EncodeToPNG());
            // the painted sheets of the first character, for checking the layout and alpha
            var look = SdLook.For(ids[0]);
            File.WriteAllBytes(Path.Combine(outDir, "tex_eyemouth.png"), SdRefTex.EyeMouth(look).EncodeToPNG());
            File.WriteAllBytes(Path.Combine(outDir, "tex_face.png"), SdRefTex.Face(look).EncodeToPNG());
            Debug.Log("[SdBasePreview] done");
        }

        static ChibiRig RawSample(Transform holder)
        {
            var prefab = Resources.Load<GameObject>("Art/SDBase/base");
            var go = Object.Instantiate(prefab, holder);
            var rends = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var body = rends.OrderByDescending(r => r.sharedMesh.subMeshCount).First();
            foreach (var r in rends) if (r != body) r.gameObject.SetActive(false);
            body.updateWhenOffscreen = true;
            // the FBX's materials carry no textures (they were not imported): dress the parts with
            // the sample's own sheets on our toon shader, eye/mouth and brows cut by alpha
            var mats = new Material[body.sharedMesh.subMeshCount];
            for (var i = 0; i < mats.Length; i++)
            {
                var n = (body.sharedMaterials[i] ? body.sharedMaterials[i].name : "").ToLowerInvariant();
                var tex = n.Contains("eyemouth") ? "base_eyemouth" : n.Contains("hair") ? "base_hair" : n.Contains("face") || n.Contains("eyebrow") ? "base_face" : "base_body";
                var m = MeshKit.NewToon(n.Contains("eye") ? 0f : 0.004f, Resources.Load<Texture2D>("Art/SDBase/" + tex));
                if (n.Contains("eye")) { m.SetFloat("_Cutoff", 0.5f); m.SetFloat("_OutlineWidth", 0f); m.SetFloat("_ShadeStrength", 0.05f); }
                // SD_DEBUG=1: one flat colour per submesh, to tell the pieces apart
                if (System.Environment.GetEnvironmentVariable("SD_DEBUG") == "1")
                {
                    m = MeshKit.NewToon(0f, null);
                    m.SetColor("_Color", MeshKit.Lin(i switch { 0 => new Color(0.6f, 0.6f, 0.6f), 1 => new Color(1f, 0.85f, 0.7f), 2 => new Color(0.2f, 0.2f, 0.35f), 3 => Color.magenta, _ => Color.cyan }));
                    m.SetFloat("_OutlineWidth", 0f);
                }
                mats[i] = m;
            }
            body.sharedMaterials = mats;
            if (System.Environment.GetEnvironmentVariable("SD_NOHAIR") == "1")
            {
                var mesh = Object.Instantiate(body.sharedMesh);
                for (var i = 0; i < mesh.subMeshCount; i++)
                    if (body.sharedMaterials[i] && body.sharedMaterials[i].name.ToLowerInvariant().Contains("hair")) mesh.SetTriangles(new int[0], i);
                body.sharedMesh = mesh;
            }
            var wb = body.bounds;
            var s = SdRef.Height / wb.size.y;
            go.transform.localScale = Vector3.one * s;
            go.transform.localPosition = new Vector3(-wb.center.x * s, -wb.min.y * s, -wb.center.z * s);
            return new ChibiRig { Root = holder, Height = SdRef.Height };
        }

        static Texture2D Shoot(float h, int w, int hh, float size = 0.55f, float centre = 0.5f)
        {
            var go = new GameObject("cam");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = h * size;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.62f, 0.85f);
            cam.transform.position = new Vector3(0f, h * centre, -5f);
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
