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
            if (!ExcelHeroes.Data.GameData.Loaded) ExcelHeroes.Data.GameData.Load();     // the name tag's sheet needs the hero defs
            const int W = 220, H = 300;
            float[] yaws = { 180f, 145f, 90f, 0f };
            // rows: front / three-quarter / side / back, then a head close-up (front)
            var sheet = new Texture2D(W * ids.Length, H * (yaws.Length + 1), TextureFormat.RGB24, false);
            for (var i = 0; i < ids.Length; i++)
            {
                var holder = new GameObject("preview").transform;
                // SD_RAW=1: the untouched sample (its own materials), the reference for every edit
                // SD_MON=1: the ids are monster type ids, built as 3D mascots (SdModel.BuildMonster)
                var rig = System.Environment.GetEnvironmentVariable("SD_MON") == "1" ? SdModel.BuildMonster(ids[i], holder, 0)
                        : System.Environment.GetEnvironmentVariable("SD_RAW") == "1" ? RawSample(holder)
                        : SdRef.Build(ids[i], holder, 0) ?? SdBase.Build(ids[i], holder, 0);
                if (rig == null) { Debug.LogWarning("[SdBasePreview] no figure for " + ids[i]); Object.DestroyImmediate(holder.gameObject); continue; }
                // SD_POSETEST=1: column i bends forearm / calf about one axis each, to find the joint axes
                // SD_POSETEST=2: Z only; column 0/1 forearm (L same / L opposite sign), 2/3 calf likewise
                if (System.Environment.GetEnvironmentVariable("SD_POSETEST") == "2" && rig.RefModel)
                {
                    var q = Quaternion.Euler(0f, 0f, 70f); var qn = Quaternion.Euler(0f, 0f, -70f);
                    rig.Pose(rig.ForearmR, i < 2 ? q : Quaternion.identity);
                    rig.Pose(rig.ForearmL, i == 0 ? q : i == 1 ? qn : Quaternion.identity);
                    rig.Pose(rig.CalfR, i >= 2 ? q : Quaternion.identity);
                    rig.Pose(rig.CalfL, i == 2 ? q : i == 3 ? qn : Quaternion.identity);
                }
                var exprEnv = System.Environment.GetEnvironmentVariable("SD_EXPR");
                if (!string.IsNullOrEmpty(exprEnv) && rig.RefModel && rig.EyeSub >= 0)
                {
                    var exprs = exprEnv.Split(','); var ex = exprs[i % exprs.Length];
                    var b = new MaterialPropertyBlock();
                    b.SetTexture("_MainTex", SdRefLook.For(ids[i]).EyeSheet(ex));
                    rig.FaceRenderer.SetPropertyBlock(b, rig.EyeSub);
                }
                // SD_POSETEST=4: the RIGHT shoulder about +X, -X, +Y, -Y, +Z, -Z (columns 0..5), 70 degrees
                if (System.Environment.GetEnvironmentVariable("SD_POSETEST") == "4" && rig.RefModel)
                {
                    var d = i % 2 == 0 ? 70f : -70f; var ax = i / 2;
                    rig.Pose(rig.ArmR, ax == 0 ? Quaternion.Euler(d, 0f, 0f) : ax == 1 ? Quaternion.Euler(0f, d, 0f) : Quaternion.Euler(0f, 0f, d));
                }
                // SD_POSETEST=5: the cheer frame (arm up beside the head) and the relaxed idle (column 1)
                // SD_POSETEST=6: raise/swing pairs for the arm-up pose, one per column
                if (System.Environment.GetEnvironmentVariable("SD_POSETEST") == "6" && rig.RefModel)
                {
                    float[] rs = { -110f, -100f, -120f, -130f }; float[] sw = { -25f, -40f, -35f, -50f };
                    rig.Pose(rig.ArmR, Quaternion.Euler(0f, rs[i % 4], sw[i % 4])); rig.Pose(rig.ForearmR, Quaternion.Euler(0f, 0f, 8f));
                    rig.Pose(rig.ArmL, Quaternion.Euler(0f, -10f, -4f)); rig.Pose(rig.ForearmL, Quaternion.Euler(0f, 0f, 20f));
                }
                if (System.Environment.GetEnvironmentVariable("SD_POSETEST") == "5" && rig.RefModel)
                {
                    if (i % 2 == 0)
                    {
                        rig.Pose(rig.ArmR, Quaternion.Euler(0f, -125f, 10f)); rig.Pose(rig.ForearmR, Quaternion.Euler(0f, 0f, 20f));
                        rig.Pose(rig.ArmL, Quaternion.Euler(0f, -10f, -4f)); rig.Pose(rig.ForearmL, Quaternion.Euler(0f, 0f, 20f));
                        rig.Pose(rig.CalfL, Quaternion.Euler(0f, 0f, 30f)); rig.Pose(rig.CalfR, Quaternion.Euler(0f, 0f, 30f));
                    }
                    else
                    {
                        rig.Pose(rig.ArmR, Quaternion.Euler(0f, 8f, 4f)); rig.Pose(rig.ForearmR, Quaternion.Euler(0f, 0f, 14f));
                        rig.Pose(rig.ArmL, Quaternion.Euler(0f, -8f, -4f)); rig.Pose(rig.ForearmL, Quaternion.Euler(0f, 0f, 14f));
                    }
                }
                if (System.Environment.GetEnvironmentVariable("SD_POSETEST") == "3" && rig.RefModel)
                {
                    // a punch frame: R arm thrown forward (+Z) and straight, L guard, front knee bent, lean in
                    rig.Pose(rig.Body, Quaternion.Euler(0f, 0f, 8f) * Quaternion.Euler(16f, 0f, 0f));
                    rig.Pose(rig.ArmR, Quaternion.Euler(0f, -20f, 100f));
                    rig.Pose(rig.ForearmR, Quaternion.Euler(0f, 0f, 5f));
                    rig.Pose(rig.ArmL, Quaternion.Euler(0f, 10f, -30f));
                    rig.Pose(rig.ForearmL, Quaternion.Euler(0f, 0f, 70f));
                    rig.Pose(rig.LegL, Quaternion.Euler(0f, 0f, -20f));
                    rig.Pose(rig.CalfL, Quaternion.Euler(0f, 0f, 30f));
                }
                if (System.Environment.GetEnvironmentVariable("SD_POSETEST") == "1" && rig.RefModel)
                {
                    var ax = i % 3; var deg = 70f;
                    var q = ax == 0 ? Quaternion.Euler(deg, 0f, 0f) : ax == 1 ? Quaternion.Euler(0f, deg, 0f) : Quaternion.Euler(0f, 0f, deg);
                    var qn = ax == 0 ? Quaternion.Euler(-deg, 0f, 0f) : ax == 1 ? Quaternion.Euler(0f, -deg, 0f) : Quaternion.Euler(0f, 0f, -deg);
                    rig.Pose(rig.ForearmR, i < 3 ? q : Quaternion.identity);
                    rig.Pose(rig.ForearmL, i < 3 ? qn : Quaternion.identity);
                    rig.Pose(rig.CalfR, i >= 3 ? q : Quaternion.identity);
                    rig.Pose(rig.CalfL, i >= 3 ? qn : Quaternion.identity);
                }
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
