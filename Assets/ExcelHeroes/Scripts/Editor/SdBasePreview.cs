using UnityEditor;
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
                    SdPose.Apply(rig, poses[p]); rig.Root.localPosition = new Vector3(0f, poses[p].Y + rig.FootDrop, 0f);
                    Expr(poses[p].Expr ?? "");
                    Put(p, 2, Shoot(SdBase.Height, W, H));
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(outDir, id + ".png"), sheet.EncodeToPNG());
                Object.DestroyImmediate(holder.gameObject);
                Debug.Log("[SdBasePreview] sheet " + id);
            }
        }

        /// <summary>
        /// Filmstrips: for each id in SD_IDS, one row per action in SD_ACTIONS, frames across the
        /// action's time, three-quarter view showing the right arm, the root's hop and spin applied
        /// — the way to judge an arc, not a single frame. "seq" rows simulate the RUNTIME blend:
        /// ready → attack → ready → hit → ready through Pose.Spring at 60 Hz with the hair springs
        /// stepping, sampled every 0.07 s (24 frames), so joint pops and settling show as they
        /// would in the fight. → SD_PREVIEW_OUT/strip_&lt;id&gt;.png
        /// </summary>
        public static void Strip()
        {
            var outDir = System.Environment.GetEnvironmentVariable("SD_PREVIEW_OUT") ?? Path.Combine(Application.dataPath, "..", "tools", "out", "sd3d");
            Directory.CreateDirectory(outDir);
            var ids = (System.Environment.GetEnvironmentVariable("SD_IDS") ?? "intern").Split(',');
            var actions = (System.Environment.GetEnvironmentVariable("SD_ACTIONS") ?? "idle0,idle1,ready0,attack0,attack1,attack2,hit,walk,win0,win1,win3,skill0,skill1,skill2,dead").Split(',');
            Shader.SetGlobalVector("_EhLightDir", new Vector4(-0.45f, 0.85f, -0.5f, 0f));
            if (!ExcelHeroes.Data.GameData.Loaded) ExcelHeroes.Data.GameData.Load();
            const int W = 150, H = 220;
            foreach (var id in ids)
            {
                var holder = new GameObject("preview").transform;
                var rig = SdRef.Build(id, holder, 0);
                if (rig == null) { Debug.LogWarning("[SdBasePreview] no figure for " + id); Object.DestroyImmediate(holder.gameObject); continue; }
                if (rig.FaceRenderer is SkinnedMeshRenderer smr) smr.forceMatrixRecalculationPerRender = true;
                var sec = rig.Root.GetComponent<SdSecondary>();
                var kind = SdPose.AttackOf(id, SdRef.RoleOf(id));
                // frames per row: a plain action gets 8 across its time; a seq gets 24 at 0.07 s
                var rows = new System.Collections.Generic.List<(string name, System.Collections.Generic.List<(ExcelHeroes.World.Pose p, float dt)>)>();
                foreach (var act in actions)
                {
                    var frames = new System.Collections.Generic.List<(ExcelHeroes.World.Pose, float)>();
                    if (act == "seq")
                    {
                        // the runtime blend: targets by time, springs at 60 Hz, a frame kept every 0.07 s
                        var shown = SdPose.Ready(kind, 0f, 0f); var vel = new float[ExcelHeroes.World.Pose.Count];
                        var next = 0f;
                        for (var t = 0f; t < 1.68f; t += 1f / 60f)
                        {
                            ExcelHeroes.World.Pose target; float omega;
                            if (t < 0.3f) { target = SdPose.Ready(kind, t, 0f); omega = 26f; }
                            else if (t < 0.62f) { target = SdPose.Attack(kind, (t - 0.3f) / 0.32f); omega = 42f; }
                            else if (t < 1.0f) { target = SdPose.Ready(kind, t, 0f); omega = 26f; }
                            else if (t < 1.16f) { target = SdPose.Hit(1f - (t - 1.0f) / 0.16f); omega = 42f; }
                            else { target = SdPose.Ready(kind, t, 0f); omega = 26f; }
                            ExcelHeroes.World.Pose.Spring(ref shown, vel, target, 1f / 60f, omega, 0.78f);
                            if (t >= next) { frames.Add((shown, 1f / 60f)); next += 0.07f; }
                            else frames.Add((shown, -1f));          // simulated but not shown
                        }
                    }
                    else
                        for (var f = 0; f < 8; f++)
                        {
                            var u = f / 7f;
                            ExcelHeroes.World.Pose p;
                            if (act.StartsWith("idle")) p = SdPose.Idle(int.Parse(act.Substring(4)), u * 6f, 0f);
                            else if (act.StartsWith("ready")) p = SdPose.Ready(int.Parse(act.Substring(5)), u * 4f, 0f);
                            else if (act.StartsWith("attack")) p = SdPose.Attack(int.Parse(act.Substring(6)), u);
                            else if (act == "hit") p = SdPose.Hit(1f - u);
                            else if (act == "walk") p = SdPose.Walk(f / 8f * Mathf.PI * 2f);
                            else if (act.StartsWith("win")) p = SdPose.Victory(int.Parse(act.Substring(3)), u * 1.4f);
                            else if (act.StartsWith("skill")) p = SdPose.Skill(int.Parse(act.Substring(5)), u);
                            else if (act == "dead") p = SdPose.Dead(u);
                            else p = ExcelHeroes.World.Pose.Rest;
                            frames.Add((p, 1f / 12f));
                        }
                    rows.Add((act, frames));
                }
                var cols = rows.Max(r => r.Item2.Count(fr => fr.dt >= 0f));
                var sheet = new Texture2D(W * cols, H * rows.Count, TextureFormat.RGB24, false);
                var grey = Enumerable.Repeat(new Color(0.25f, 0.25f, 0.28f), W * cols * H * rows.Count).ToArray();
                sheet.SetPixels(grey);
                for (var r = 0; r < rows.Count; r++)
                {
                    sec?.Settle();
                    var col = 0;
                    foreach (var (p, dt) in rows[r].Item2)
                    {
                        SdPose.Apply(rig, p);
                        rig.Root.localPosition = new Vector3(p.Step, p.Y + rig.FootDrop, 0f);
                        rig.Root.rotation = Quaternion.Euler(0f, 215f + p.Yaw, 0f);
                        if (dt < 0f) { sec?.Step(1f / 60f); continue; }
                        if (rig.EyeSub >= 0) { var eb = new MaterialPropertyBlock(); eb.SetTexture("_MainTex", SdRefLook.For(id).EyeSheet(p.Expr ?? "")); rig.FaceRenderer.SetPropertyBlock(eb, rig.EyeSub); }
                        var steps = Mathf.Max(1, Mathf.RoundToInt(dt * 60f));
                        for (var k = 0; k < steps; k++) sec?.Step(1f / 60f);
                        var img = Shoot(SdBase.Height, W, H, 0.72f, 0.58f);
                        sheet.SetPixels(W * col, H * (rows.Count - 1 - r), W, H, img.GetPixels());
                        Object.DestroyImmediate(img);
                        col++;
                    }
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(outDir, "strip_" + id + ".png"), sheet.EncodeToPNG());
                Object.DestroyImmediate(holder.gameObject);
                Debug.Log("[SdBasePreview] strip " + id);
            }
        }

        /// <summary>
        /// SD_POSE=set:SwingL=0,30,60;ElbowL=40 — the rest pose with named Pose channels set per
        /// column (a list is indexed by column, a single value holds for all): the calibration
        /// tool for which channel moves which joint which way.
        /// </summary>
        static ExcelHeroes.World.Pose SetPose(string spec, int column)
        {
            object boxed = ExcelHeroes.World.Pose.Rest;
            foreach (var part in spec.Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                var f = typeof(ExcelHeroes.World.Pose).GetField(kv[0].Trim());
                if (f == null || f.FieldType != typeof(float)) { Debug.LogWarning("[SetPose] no channel " + kv[0]); continue; }
                var vals = kv[1].Split(',');
                f.SetValue(boxed, float.Parse(vals[Mathf.Min(column, vals.Length - 1)], System.Globalization.CultureInfo.InvariantCulture));
            }
            return (ExcelHeroes.World.Pose)boxed;
        }

        static void SampleClip(ChibiRig rig, string id, string clipName, float k)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(UalImport.Fbx).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == clipName || c.name.EndsWith("|" + clipName));
            if (clip == null) { Debug.LogWarning("[SD_ANIM] no clip " + clipName); return; }
            if (!rig.Model.gameObject.TryGetComponent<Animator>(out var anim)) anim = rig.Model.gameObject.AddComponent<Animator>();
            anim.avatar = SdHumanoid.Build(rig.Model, id);
            anim.applyRootMotion = false;
            // a humanoid clip sampled on a humanoid Animator retargets through the avatar
            // the model carries a 114.8x import scale, so the clip's body position lands 44 m up: keep the
            // clip's rotations only, the pelvis at its rest place, and plant the feet as SdPose does
            var pelvisRest = rig.Pelvis.localPosition;
            clip.SampleAnimation(rig.Model.gameObject, clip.length * k);
            rig.Pelvis.localPosition = pelvisRest;
            // the sample faces the model's −Z, Mecanim drives the body toward +Z: turn it back round
            rig.Pelvis.rotation = Quaternion.AngleAxis(180f, Vector3.up) * rig.Pelvis.rotation;
            var lowest = Mathf.Min(rig.FootL.position.y, rig.FootR.position.y);
            rig.Root.localPosition = new Vector3(0f, rig.RestFootY - lowest, 0f);
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
                // SD_GLASSES=square,round,…: force a glasses style per column (the look is mutable and read at build)
                // SD_IRIS=miku,kayoko,…: force a sample iris per column (the look is read at build)
                // SD_HAIRX=wave=1|spread=0.8,…: extra hair-recipe keys per column ('|' between keys)
                var hxEnv = System.Environment.GetEnvironmentVariable("SD_HAIRX");
                if (!string.IsNullOrEmpty(hxEnv)) System.Environment.SetEnvironmentVariable("SD_HAIRADD", hxEnv.Split(',')[i % hxEnv.Split(',').Length].Replace('|', ';'));
                var irEnv = System.Environment.GetEnvironmentVariable("SD_IRIS");
                if (!string.IsNullOrEmpty(irEnv)) { var ir = irEnv.Split(','); SdLook.For(ids[i]).Iris = ir[i % ir.Length]; }
                var hlEnv = System.Environment.GetEnvironmentVariable("SD_HAIRLIB");
                if (!string.IsNullOrEmpty(hlEnv)) { var hs = hlEnv.Split(','); SdLook.For(ids[i]).HairLib = hs[i % hs.Length]; }
                var glEnv = System.Environment.GetEnvironmentVariable("SD_GLASSES");
                if (!string.IsNullOrEmpty(glEnv))
                {
                    var gs = glEnv.Split(','); var lk = SdLook.For(ids[i]);
                    lk.Glasses = true; lk.Sunglasses = false; lk.GlassesStyle = gs[i % gs.Length];
                    var gcEnv = System.Environment.GetEnvironmentVariable("SD_GLASSES_COLOR");
                    if (!string.IsNullOrEmpty(gcEnv)) { var cs = gcEnv.Split(','); lk.GlassesColor = MeshKit.Hex(cs[i % cs.Length], lk.GlassesColor); }
                }
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
                // SD_HANDDUMP=1: the right hand's frame, measured — each finger bone and its child in
                // hand-local space (metres ÷ hand scale), open (fist 0) and closed (fist 1), and where
                // the forearm and the palm's surface are, so a prop can be placed in the grip
                if (System.Environment.GetEnvironmentVariable("SD_HANDDUMP") == "1" && i == 0 && rig.HandR != null)
                {
                    var hr = rig.HandR; var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"[HandDump] hand lossyScale {hr.lossyScale:F5} worldAxes x{hr.right:F2} y{hr.up:F2} z{hr.forward:F2} forearm@{hr.InverseTransformPoint(hr.parent.position):F5}");
                    foreach (var fist in new[] { 0f, 1f })
                    {
                        var pz = ExcelHeroes.World.Pose.Rest; pz.FistR = fist; SdPose.Apply(rig, pz);
                        foreach (var f in rig.FingersR)
                        {
                            if (f == null) continue;
                            var tip = f.childCount > 0 ? f.GetChild(0).position : f.position;
                            sb.AppendLine($"  fist{fist:0} {f.name}: root {hr.InverseTransformPoint(f.position):F5} child {hr.InverseTransformPoint(tip):F5} ({(f.childCount > 0 ? f.GetChild(0).name : "-")})");
                        }
                    }
                    // the hand's own skin, in hand-local space through its bind pose: every vertex that
                    // leans on the hand or a finger bone. The thinnest extent is the palm's normal.
                    if (rig.FaceRenderer is SkinnedMeshRenderer bsm)
                    {
                        var mesh = bsm.sharedMesh; var bw = mesh.boneWeights; var vs = mesh.vertices; var bp = mesh.bindposes;
                        var bi = System.Array.IndexOf(bsm.bones, hr);
                        var fingerIdx = rig.FingersR.Where(f => f != null).Select(f => System.Array.IndexOf(bsm.bones, f)).ToHashSet();
                        var mn = new Vector3(9, 9, 9); var mx = -mn; var n = 0; var palmMn = mn; var palmMx = mx;
                        for (var v = 0; v < vs.Length; v++)
                        {
                            var w = bw[v];
                            var onHand = (w.boneIndex0 == bi && w.weight0 > 0.5f);
                            var onFinger = fingerIdx.Contains(w.boneIndex0) && w.weight0 > 0.5f;
                            if (!onHand && !onFinger) continue;
                            var lp = bp[bi].MultiplyPoint3x4(vs[v]);
                            mn = Vector3.Min(mn, lp); mx = Vector3.Max(mx, lp); n++;
                            if (onHand) { palmMn = Vector3.Min(palmMn, lp); palmMx = Vector3.Max(palmMx, lp); }
                        }
                        sb.AppendLine($"  hand+finger skin: {n} verts, hand-local box {mn:F5}..{mx:F5}");
                        sb.AppendLine($"  palm-only skin box {palmMn:F5}..{palmMx:F5}");
                    }
                    // which finger-local axis curls: Finger1 posed 70 degrees about each, where its knuckle goes
                    SdPose.Apply(rig, ExcelHeroes.World.Pose.Rest);
                    foreach (var (a, c) in new[] { (0, 1), (2, 3), (4, 5) })     // thumb, index, middle: the root bone and the joint after it
                    {
                        var f1 = rig.FingersR.Length > c ? rig.FingersR[a] : null; var f11 = rig.FingersR.Length > c ? rig.FingersR[c] : null;
                        if (f1 == null || f11 == null) continue;
                        var baseP = hr.InverseTransformPoint(f11.position);
                        foreach (var (lbl, q) in new[] { ("+X", Quaternion.Euler(70, 0, 0)), ("-X", Quaternion.Euler(-70, 0, 0)), ("+Y", Quaternion.Euler(0, 70, 0)), ("-Y", Quaternion.Euler(0, -70, 0)), ("+Z", Quaternion.Euler(0, 0, 70)), ("-Z", Quaternion.Euler(0, 0, -70)) })
                        {
                            rig.Pose(f1, q);
                            sb.AppendLine($"  {f1.name} bone-local {lbl}: next joint moves {(hr.InverseTransformPoint(f11.position) - baseP):F5}");
                        }
                        rig.Pose(f1, Quaternion.identity);
                    }
                    Debug.Log(sb.ToString());
                    SdPose.Apply(rig, ExcelHeroes.World.Pose.Rest);
                }
                if (System.Environment.GetEnvironmentVariable("SD_BONES") == "1" && i == 0)
                    foreach (var tr in rig.Root.GetComponentsInChildren<Transform>(true)) if (tr.name.StartsWith("Bip")) Debug.Log("[SdBones] " + tr.name);
                // SD_ANIM=Clip_Name — a UAL clip (Anim/UAL) retargeted onto the figure through SdHumanoid,
                // sampled across the columns in time
                var animEnv = System.Environment.GetEnvironmentVariable("SD_ANIM");
                if (!string.IsNullOrEmpty(animEnv) && rig.RefModel) SampleClip(rig, ids[i], animEnv, (i + 0.5f) / ids.Length);
                var poseEnv = System.Environment.GetEnvironmentVariable("SD_POSE");
                if (!string.IsNullOrEmpty(poseEnv) && rig.RefModel)
                {
                    // SD_POSE=atk:K — attack kind K across the columns in time (a = (i+0.5)/n), one id repeated
                    var pz = poseEnv.StartsWith("set:") ? SetPose(poseEnv.Substring(4), i)
                           : poseEnv.StartsWith("atk:") ? SdPose.Attack(int.Parse(poseEnv.Substring(4)), (i + 0.5f) / ids.Length)
                           : poseEnv == "win" ? SdPose.Victory(i, 0.55f) : poseEnv == "attack" ? SdPose.Attack(i % 3, 0.5f)
                           : poseEnv == "ready" ? SdPose.Ready(SdPose.AttackOf(ids[i], SdRef.RoleOf(ids[i])), 0.2f, 0f)
                           : poseEnv == "attackrole" ? SdPose.Attack(SdPose.AttackOf(ids[i], SdRef.RoleOf(ids[i])), 0.5f)
                           : poseEnv == "walk" ? SdPose.Walk(i * Mathf.PI / 3f)
                           : poseEnv.StartsWith("skill") ? SdPose.Skill(int.Parse(poseEnv.Substring(5)), (i + 0.5f) / ids.Length)
                           : SdPose.Idle(i, 0.4f, 0f);
                    SdPose.Apply(rig, pz);
                    rig.Root.localPosition = new Vector3(0f, pz.Y + rig.FootDrop, 0f);
                    if (rig.EyeSub >= 0 && !string.IsNullOrEmpty(pz.Expr))
                    {
                        var eb = new MaterialPropertyBlock(); eb.SetTexture("_MainTex", SdRefLook.For(ids[i]).EyeSheet(pz.Expr)); rig.FaceRenderer.SetPropertyBlock(eb, rig.EyeSub);
                    }
                }
                var eyesEnv = System.Environment.GetEnvironmentVariable("SD_EYES");
                if (!string.IsNullOrEmpty(eyesEnv) && rig.RefModel && rig.EyeSub >= 0)
                {
                    // the style forced per column: repaint both sheets on a copy of the look
                    var ey = eyesEnv.Split(',')[i % eyesEnv.Split(',').Length];
                    var lk = SdLook.For(ids[i]); var saved = lk.Eyes; lk.Eyes = ey;
                    var mats = rig.FaceRenderer.sharedMaterials;
                    for (var mi = 0; mi < mats.Length; mi++)
                    {
                        if (mi == rig.EyeSub) { var eb = new MaterialPropertyBlock(); eb.SetTexture("_MainTex", SdRefTex.EyeMouth(lk, "")); rig.FaceRenderer.SetPropertyBlock(eb, mi); }
                        else if (mats[mi] != null && mats[mi].mainTexture != null && mats[mi].mainTexture.name.StartsWith("face:")) { var fb = new MaterialPropertyBlock(); fb.SetTexture("_MainTex", SdRefTex.Face(lk)); rig.FaceRenderer.SetPropertyBlock(fb, mi); }
                    }
                    lk.Eyes = saved;
                }
                var exprEnv = System.Environment.GetEnvironmentVariable("SD_EXPR");
                if (!string.IsNullOrEmpty(exprEnv) && rig.Blink != null) rig.Blink.Express(exprEnv.Split(',')[i % exprEnv.Split(',').Length]);
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
                // SD_POSETEST=13/14/15: Spine1 / R Clavicle / Head about +X -X +Y -Y +Z -Z (columns 0..5), 40 degrees, arms down
                if (System.Environment.GetEnvironmentVariable("SD_POSETEST") is "13" or "14" or "15" && rig.RefModel)
                {
                    SdPose.Apply(rig, ExcelHeroes.World.Pose.Rest);
                    var which = System.Environment.GetEnvironmentVariable("SD_POSETEST");
                    var bone = which == "13" ? rig.Spine : which == "14" ? rig.ClavR : rig.Head;
                    var d = i % 2 == 0 ? 40f : -40f; var ax = i / 2;
                    rig.Pose(bone, ax == 0 ? Quaternion.Euler(d, 0f, 0f) : ax == 1 ? Quaternion.Euler(0f, d, 0f) : Quaternion.Euler(0f, 0f, d));
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
                        rig.Root.localPosition = new Vector3(f * 0.9f / 60f, wp.Y + rig.FootDrop, 0f);
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
                // SD_HANDCAM=<yaw>: the bottom row is the right hand close up instead of the face (grip checks)
                // SD_HANDSIDE=L: the left hand instead
                var handEnv = System.Environment.GetEnvironmentVariable("SD_HANDCAM");
                var camHand = System.Environment.GetEnvironmentVariable("SD_HANDSIDE") == "L" ? rig.HandL : rig.HandR;
                if (!string.IsNullOrEmpty(handEnv) && camHand != null) rig.Root.rotation = Quaternion.Euler(0f, float.Parse(handEnv), 0f);
                var head = !string.IsNullOrEmpty(handEnv) && camHand != null
                    ? Shoot(SdBase.Height, W, H, 0.1f, camHand.position.y / SdBase.Height, camHand.position.x)
                    : Shoot(SdBase.Height, W, H, 0.22f, 0.8f);
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
            File.WriteAllBytes(Path.Combine(outDir, "tex_hair.png"), SdRefTex.Hair(look).EncodeToPNG());
            Debug.Log($"[SdBasePreview] hair colour {look.Hair} tex linear? {(SdRefTex.Hair(look).isDataSRGB ? "sRGB" : "linear")} body tex sRGB? {Resources.Load<Texture2D>("Art/SDBase/base_hair").isDataSRGB}");
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

        static Texture2D Shoot(float h, int w, int hh, float size = 0.55f, float centre = 0.5f, float x = 0f)
        {
            var go = new GameObject("cam");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = h * size;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.62f, 0.85f);
            cam.transform.position = new Vector3(x, h * centre, -5f);
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
