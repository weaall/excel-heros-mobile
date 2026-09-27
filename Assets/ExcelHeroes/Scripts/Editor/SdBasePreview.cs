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
        /// <summary>
        /// The review sheet, one PNG per character (SD_SHEET=1): row 1 front / three-quarter /
        /// side / back, row 2 the head with the four expressions, row 3 the character's own idle,
        /// victory and attack, and a walk frame — everything the spec decides, on one page, so a
        /// changed illustration is checked by regenerating its sheet. → SD_PREVIEW_OUT/sheets/&lt;id&gt;.png
        /// </summary>
        public static void Sheet()
        {
            var outDir = System.Environment.GetEnvironmentVariable("SD_PREVIEW_OUT") ?? Path.Combine(Application.dataPath, "..", "tools", "out", "sd3d");
            outDir = Path.Combine(outDir, "sheets");
            Directory.CreateDirectory(outDir);
            var ids = (System.Environment.GetEnvironmentVariable("SD_IDS") ?? "intern").Split(',');
            Shader.SetGlobalVector("_EhLightDir", new Vector4(-0.45f, 0.85f, -0.5f, 0f));
            if (!ExcelHeroes.Data.GameData.Loaded) ExcelHeroes.Data.GameData.Load();
            const int W = 220, H = 300, COLS = 5;
            foreach (var id in ids)
            {
                var holder = new GameObject("preview").transform;
                var rig = SdRef.Build(id, holder, 0);
                if (rig == null) { Debug.LogWarning("[SdBasePreview] no figure for " + id); Object.DestroyImmediate(holder.gameObject); continue; }
                // one rig rendered many times in one editor frame: Unity skins it once per frame
                // unless told to redo the matrices per render (only the root bone moved otherwise)
                if (rig.FaceRenderer is SkinnedMeshRenderer smr) smr.forceMatrixRecalculationPerRender = true;
                var sheet = new Texture2D(W * COLS, H * 3, TextureFormat.RGB24, false);
                void Put(int col, int row, Texture2D img) { sheet.SetPixels(W * col, H * (2 - row), W, H, img.GetPixels()); Object.DestroyImmediate(img); }
                void Expr(string e)
                {
                    if (rig.EyeSub < 0) return;
                    var b = new MaterialPropertyBlock(); b.SetTexture("_MainTex", SdRefLook.For(id).EyeSheet(e)); rig.FaceRenderer.SetPropertyBlock(b, rig.EyeSub);
                }
                // row 1: the four views in the character's own idle
                var idle = SdPose.Idle(SdPose.IdleOf(id), 0.4f, 0f);
                SdPose.Apply(rig, idle);
                float[] yaws = { 180f, 145f, 90f, 0f, 235f };                 // front · ¾ · side · back · rear ¾
                for (var a = 0; a < COLS; a++) { rig.Root.rotation = Quaternion.Euler(0f, yaws[a], 0f); Put(a, 0, Shoot(SdBase.Height, W, H)); }
                // row 2: the head, four expressions (arms down so nothing covers the face)
                SdPose.Apply(rig, ExcelHeroes.World.Pose.Rest);
                rig.Root.rotation = Quaternion.Euler(0f, 180f, 0f);
                string[] exprs = { "", "happy", "hurt", "angry" };
                for (var e = 0; e < 4; e++) { Expr(exprs[e]); Put(e, 1, Shoot(SdBase.Height, W, H, 0.22f, 0.8f)); }
                Expr(""); rig.Root.rotation = Quaternion.Euler(0f, 145f, 0f); Put(4, 1, Shoot(SdBase.Height, W, H, 0.22f, 0.8f));   // the face in three-quarter
                // row 3: idle · ready · attack · victory · walk, the three-quarter that shows the right arm
                rig.Root.rotation = Quaternion.Euler(0f, 215f, 0f);
                var role = SdRef.RoleOf(id);
                var kind = SdPose.AttackOf(id, role);
                var poses = new[] { idle, SdPose.Ready(kind, 0.3f, 0f), SdPose.Attack(kind, 0.5f), SdPose.Victory(SdPose.WinOf(id), 0.55f), SdPose.Walk(Mathf.PI / 3f) };
                for (var p = 0; p < COLS; p++)
                {
                    SdPose.Apply(rig, poses[p]); rig.Root.localPosition = new Vector3(0f, poses[p].Y, 0f);
                    Expr(poses[p].Expr ?? "");
                    Put(p, 2, Shoot(SdBase.Height, W, H));
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(outDir, id + ".png"), sheet.EncodeToPNG());
                Object.DestroyImmediate(holder.gameObject);
                Debug.Log("[SdBasePreview] sheet " + id);
            }
        }

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
                if (System.Environment.GetEnvironmentVariable("SD_BONES") == "1" && i == 0)
                    foreach (var tr in rig.Root.GetComponentsInChildren<Transform>(true)) if (tr.name.StartsWith("Bip")) Debug.Log("[SdBones] " + tr.name);
                var poseEnv = System.Environment.GetEnvironmentVariable("SD_POSE");
                if (!string.IsNullOrEmpty(poseEnv) && rig.RefModel)
                {
                    var pz = poseEnv == "win" ? SdPose.Victory(i, 0.55f) : poseEnv == "attack" ? SdPose.Attack(i % 3, 0.5f)
                           : poseEnv == "walk" ? SdPose.Walk(i * Mathf.PI / 3f)
                           : poseEnv.StartsWith("skill") ? SdPose.Skill(int.Parse(poseEnv.Substring(5)), (i + 0.5f) / ids.Length)
                           : SdPose.Idle(i, 0.4f, 0f);
                    SdPose.Apply(rig, pz);
                    rig.Root.localPosition = new Vector3(0f, pz.Y, 0f);
                    if (rig.EyeSub >= 0 && !string.IsNullOrEmpty(pz.Expr))
                    {
                        var eb = new MaterialPropertyBlock(); eb.SetTexture("_MainTex", SdRefLook.For(ids[i]).EyeSheet(pz.Expr)); rig.FaceRenderer.SetPropertyBlock(eb, rig.EyeSub);
                    }
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
                // SD_POSETEST=7: the RIGHT hand about +X, -X, +Y, -Y, +Z, -Z (columns 0..5), 60 degrees, arms hanging
                if (System.Environment.GetEnvironmentVariable("SD_POSETEST") == "7" && rig.RefModel)
                {
                    SdPose.Apply(rig, ExcelHeroes.World.Pose.Rest);
                    var d = i % 2 == 0 ? 60f : -60f; var ax = i / 2;
                    rig.Pose(rig.HandR, ax == 0 ? Quaternion.Euler(d, 0f, 0f) : ax == 1 ? Quaternion.Euler(0f, d, 0f) : Quaternion.Euler(0f, 0f, d));
                }
                // SD_POSETEST=9: the LEFT hand about +Y / -Y (columns 0/1), +Z / -Z (2/3), to settle the mirror
                if (System.Environment.GetEnvironmentVariable("SD_POSETEST") == "9" && rig.RefModel)
                {
                    SdPose.Apply(rig, ExcelHeroes.World.Pose.Rest);
                    rig.Pose(rig.HandL, i == 0 ? Quaternion.Euler(0f, 60f, 0f) : i == 1 ? Quaternion.Euler(0f, -60f, 0f) : i == 2 ? Quaternion.Euler(0f, 0f, 60f) : Quaternion.Euler(0f, 0f, -60f));
                    rig.Pose(rig.HandR, Quaternion.identity);
                }
                // SD_POSETEST=10: the RIGHT fingers (index+middle) curled about +X/-X/+Y/-Y/+Z/-Z 70 (columns), arm raised so the hand shows
                if (System.Environment.GetEnvironmentVariable("SD_POSETEST") == "10" && rig.RefModel)
                {
                    var pz = ExcelHeroes.World.Pose.Rest; pz.RaiseR = 10f; pz.SwingR = 60f; pz.ElbowR = 90f; pz.InR = 0f; pz.HandFlexR = 0f;
                    SdPose.Apply(rig, pz);
                    var d = i % 2 == 0 ? 70f : -70f; var ax = i / 2;
                    var q = ax == 0 ? Quaternion.Euler(d, 0f, 0f) : ax == 1 ? Quaternion.Euler(0f, d, 0f) : Quaternion.Euler(0f, 0f, d);
                    foreach (var f in rig.FingersR) if (f != null && !f.name.EndsWith("Finger0") && !f.name.EndsWith("Finger01")) rig.Pose(f, q);
                }
                // SD_POSETEST=12: secondary motion under a real walk — 40 frames of the cycle with the root
                // advancing at 0.9 m/s, rendered on the last frame (column i picks the phase)
                if (System.Environment.GetEnvironmentVariable("SD_POSETEST") == "12" && rig.RefModel)
                {
                    var sec = rig.Root.GetComponent<SdSecondary>();
                    sec?.Settle();
                    for (var f = 0; f < 40; f++)
                    {
                        var ph = (f + i * 7) * (1f / 60f) * 9f;
                        var wp = SdPose.Walk(ph);
                        SdPose.Apply(rig, wp);
                        rig.Root.localPosition = new Vector3(f * 0.9f / 60f, wp.Y, 0f);
                        sec?.Step(1f / 60f);
                    }
                    rig.Root.localPosition = new Vector3(0f, rig.Root.localPosition.y, 0f);
                }
                // SD_POSETEST=11: secondary motion — the figure whipped round 70 degrees over 6 frames, then rendered mid-lag
                if (System.Environment.GetEnvironmentVariable("SD_POSETEST") == "11" && rig.RefModel)
                {
                    var sec = rig.Root.GetComponent<SdSecondary>();
                    SdPose.Apply(rig, ExcelHeroes.World.Pose.Rest);
                    if (sec != null)
                    {
                        sec.Settle();
                        for (var f = 0; f < 6; f++) { rig.Root.rotation = Quaternion.Euler(0f, 180f + f * 12f * (i % 2 == 0 ? 1f : -1f), 0f); rig.Root.localPosition = new Vector3(f * 0.05f, f % 2 == 0 ? 0.06f : 0f, 0f); sec.Step(1f / 60f); }
                    }
                    rig.Root.localPosition = Vector3.zero;
                }
                // SD_POSETEST=8: the RIGHT foot likewise
                if (System.Environment.GetEnvironmentVariable("SD_POSETEST") == "8" && rig.RefModel)
                {
                    SdPose.Apply(rig, ExcelHeroes.World.Pose.Rest);
                    var d = i % 2 == 0 ? 40f : -40f; var ax = i / 2;
                    rig.Pose(rig.FootR, ax == 0 ? Quaternion.Euler(d, 0f, 0f) : ax == 1 ? Quaternion.Euler(0f, d, 0f) : Quaternion.Euler(0f, 0f, d));
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
