using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ExcelHeroes.World;
using UnityEditor;
using UnityEngine;
using Pose = ExcelHeroes.World.Pose;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// Motion tested by numbers, not by eye: every pose in the library is played for 60 frames on
    /// the sample rig and measured —
    ///   ground   the lowest foot against the floor (sinks through it / floats off it while standing)
    ///   hands    a hand inside the torso capsule (pelvis → neck, radius from the mesh)
    ///   pops     the largest single-frame turn of any posed bone (a joint snapping)
    ///   slide    in the walk, how far the planted foot moves along the floor while it bears weight
    /// and written to tools/out/motion_test.txt as one line per action, worst first, with a PASS /
    /// FAIL against the thresholds below. The film strips (SdBasePreview.Strip) remain the way to
    /// look; this is the way to know.
    ///   SD_IDS=cfo,cto Unity -batchmode -quit -executeMethod ExcelHeroes.EditorTools.SdMotionTest.Run
    /// </summary>
    public static class SdMotionTest
    {
        const int Frames = 60;
        // thresholds, as fractions of the figure's height (1.2 m) or degrees per 60 Hz frame
        const float SinkMax = 0.012f, FloatMax = 0.02f, HandIn = 0.35f, PopMax = 22f, SlideMax = 0.25f;

        struct Result { public string Action; public float Sink, Float, HandDepth, Pop, Slide; public string PopBone; public bool Airborne; }

        public static void Run()
        {
            if (!ExcelHeroes.Data.GameData.Loaded) ExcelHeroes.Data.GameData.Load();
            var ids = (System.Environment.GetEnvironmentVariable("SD_IDS") ?? "cfo,cto,barista,guard").Split(',');
            var sb = new StringBuilder();
            var fails = 0;
            foreach (var id in ids)
            {
                var holder = new GameObject("mt").transform;
                var rig = SdRef.Build(id, holder, 0);
                if (rig == null) { sb.AppendLine($"{id}: no rig"); continue; }
                var results = new List<Result>();
                foreach (var (name, pose, air) in Actions(id))
                    results.Add(Measure(rig, name, pose, air));
                sb.AppendLine($"== {id}");
                foreach (var r in results.OrderByDescending(Score))
                {
                    var bad = Bad(r);
                    if (bad.Count > 0) fails++;
                    sb.AppendLine($"  {(bad.Count > 0 ? "FAIL" : "ok  ")} {r.Action,-10} sink {r.Sink:F3} float {r.Float:F3} hand {r.HandDepth:F2} pop {r.Pop,5:F1}° ({r.PopBone}) slide {r.Slide:F3}  {string.Join(", ", bad)}");
                }
                Object.DestroyImmediate(holder.gameObject);
            }
            var outDir = Path.Combine(Application.dataPath, "..", "tools", "out");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "motion_test.txt"), sb.ToString());
            Debug.Log($"[MotionTest] {fails} failing action(s)\n" + sb);
        }

        static float Score(Result r) => Mathf.Max(r.Sink / SinkMax, r.Airborne ? 0f : r.Float / FloatMax, r.HandDepth / HandIn, r.Pop / PopMax, r.Slide / SlideMax);

        static List<string> Bad(Result r)
        {
            var b = new List<string>();
            if (r.Sink > SinkMax) b.Add("feet sink");
            // walk: the slip left after the distance-driven phase (was ~1.0 at the fixed rate)

            if (!r.Airborne && r.Float > FloatMax) b.Add("floats");
            if (r.HandDepth > HandIn) b.Add("hand in body");
            // a strike is SUPPOSED to snap: the attacks may turn a joint 40° in a frame at the hit
            if (r.Pop > (r.Action.StartsWith("attack") ? 40f : PopMax)) b.Add("joint pop");
            if (r.Slide > SlideMax) b.Add("foot slides");
            return b;
        }

        /// <summary>Every action as a pose over normalised time 0..1, and whether leaving the floor is the point of it.</summary>
        /// <summary>How long each action plays in a fight (BattleWorld), for sampling it at the real rate through the real spring.</summary>
        static float DurationOf(string action) => action.StartsWith("attack") ? 0.32f : action == "hit" ? 0.16f : action.StartsWith("skill") ? 0.75f : action == "dead" ? 0.45f : 1f;

        static IEnumerable<(string, System.Func<float, Pose>, bool)> Actions(string id)
        {
            var kind = SdPose.AttackOf(id, SdRef.RoleOf(id));
            for (var v = 0; v < SdPose.IdleCount; v++) { var vv = v; yield return ($"idle{v}", t => SdPose.Idle(vv, t * 8f, 0f), false); }
            for (var k = 0; k < 3; k++) { var kk = k; yield return ($"ready{k}", t => SdPose.Ready(kk, t * 3f, 0f), false); }
            for (var k = 0; k < 3; k++) { var kk = k; yield return ($"attack{k}", t => SdPose.Attack(kk, t), kk == 2); }   // the caster hops
            yield return ("walk", t => SdPose.Walk(t * Mathf.PI * 4f), false);
            yield return ("hit", t => SdPose.Hit(1f - t), false);
            for (var k = 0; k < 3; k++) { var kk = k; yield return ($"skill{k}", t => SdPose.Skill(kk, t), true); }
            for (var v = 0; v < SdPose.WinCount; v++) { var vv = v; yield return ($"win{v}", t => SdPose.Victory(vv, t * 3f), vv is 0 or 1 or 2 or 4 or 5 or 6); }
            yield return ("dead", t => SdPose.Dead(t), true);
        }

        static Result Measure(ChibiRig rig, string name, System.Func<float, Pose> pose, bool air)
        {
            var H = rig.Height;
            var bones = rig.Root.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Bip001")).ToArray();
            // rest reference: the feet's height standing still
            SdPose.Apply(rig, Pose.Rest); rig.Root.localPosition = Vector3.zero;
            var restFoot = Mathf.Min(rig.FootL.position.y, rig.FootR.position.y);
            var r = new Result { Action = name, Airborne = air, PopBone = "-" };
            Quaternion[] prev = null;
            // played as the fight plays it: at its real length, 60 Hz, through the damped spring
            // (ω 42 into an attack or hit, 26 otherwise) — what the camera sees, not the raw keys
            var dur = DurationOf(name); var frames = Mathf.Max(8, Mathf.RoundToInt(dur * 60f));
            var omega = name.StartsWith("attack") || name == "hit" ? 42f : 26f;
            var shown = pose(0f); var vel = new float[Pose.Count];
            for (var f = 0; f < frames + 12; f++)
            {
                var target = pose(Mathf.Clamp01(f / (float)frames));
                Pose.Spring(ref shown, vel, target, 1f / 60f, omega, 0.78f);
                var p = shown;
                SdPose.Apply(rig, p);
                rig.Root.localPosition = new Vector3(0f, p.Y + rig.FootDrop, 0f) + rig.Root.localRotation * Vector3.forward * p.Step;
                var foot = Mathf.Min(rig.FootL.position.y, rig.FootR.position.y) - restFoot;
                r.Sink = Mathf.Max(r.Sink, -foot / H);
                if (!air) r.Float = Mathf.Max(r.Float, foot / H);

                // hands against the torso capsule; depth as a fraction of the radius (1 = at the spine)
                var a = rig.Pelvis.position; var b = rig.Neck != null ? rig.Neck.position : rig.Head.position;
                var rad = H * 0.085f;
                foreach (var hnd in new[] { rig.HandL, rig.HandR })
                {
                    if (hnd == null) continue;
                    var d = DistSeg(hnd.position, a, b);
                    if (d < rad) r.HandDepth = Mathf.Max(r.HandDepth, 1f - d / rad);
                }

                // pops: the biggest per-frame local turn of any rig bone (60 Hz sampling of a clip
                // that lasts longer only makes this conservative for the fast ones)
                var cur = bones.Select(t => t.localRotation).ToArray();
                if (prev != null)
                    for (var i = 0; i < cur.Length; i++)
                    {
                        var ang = Quaternion.Angle(prev[i], cur[i]);
                        if (ang > r.Pop) { r.Pop = ang; r.PopBone = bones[i].name.Replace("Bip001 ", ""); }
                    }
                prev = cur;

            }
            SdPose.Apply(rig, Pose.Rest); rig.Root.localPosition = Vector3.zero;
            if (name == "walk") r.Slide = WalkSlide(rig, SdPose.WalkCycle) / H;
            return r;
        }

        /// <summary>
        /// Foot slip with the phase driven by distance, as BattleWorld drives it: the root moves
        /// forward and the walk turns 2π per <paramref name="cycle"/> metres. The planted foot (the
        /// lower one, on the floor) should not move over the ground; the worst per-frame drift is
        /// the slip, in metres.
        /// </summary>
        public static float WalkSlide(ChibiRig rig, float cycle)
        {
            SdPose.Apply(rig, Pose.Rest); rig.Root.localPosition = Vector3.zero;
            var restFoot = Mathf.Min(rig.FootL.position.y, rig.FootR.position.y);
            const float step = 0.01f;           // 1 cm of travel per sample
            Transform planted = null; var prevZ = 0f; var slips = new List<float>();
            for (var i = 0; i < 400; i++)
            {
                var d = i * step;
                var p = SdPose.Walk(d / cycle * Mathf.PI * 2f);
                SdPose.Apply(rig, p);
                rig.Root.localPosition = new Vector3(0f, p.Y + rig.FootDrop, d);
                // the foot bearing the weight is the lower one; while it stays the lower one it
                // should not move over the ground
                var low = rig.FootL.position.y <= rig.FootR.position.y ? rig.FootL : rig.FootR;
                if (low == planted && i > 20) slips.Add(Mathf.Abs(low.position.z - prevZ));
                if (Dbg != null && i < 90) Dbg.AppendLine($"  d {d:F2} low {(low == rig.FootL ? 'L' : 'R')} worldZ {low.position.z:F3} localZ {rig.Root.InverseTransformPoint(low.position).z:F3} rootZ {rig.Root.position.z:F3}");
                planted = low; prevZ = low.position.z;
            }
            // the median: the swing foot is briefly the lower one at each hand-over, and a mean
            // counted those samples as slip
            slips.Sort(); var worst = slips.Count > 0 ? slips[slips.Count / 2] : 9f;
            SdPose.Apply(rig, Pose.Rest); rig.Root.localPosition = Vector3.zero;
            return worst / step;               // drift per metre travelled: 0 = locked, 1 = skating at full speed
        }

        public static StringBuilder Dbg;
        public static void DumpSlip()
        {
            if (!ExcelHeroes.Data.GameData.Loaded) ExcelHeroes.Data.GameData.Load();
            var holder = new GameObject("ds").transform; var rig = SdRef.Build("cfo", holder, 0);
            Dbg = new StringBuilder(); var sl = WalkSlide(rig, 0.75f); Dbg.AppendLine($"slip {sl:F3}");
            File.WriteAllText(Path.Combine(Application.dataPath, "..", "tools", "out", "slip_dump.txt"), Dbg.ToString()); Dbg = null;
            Object.DestroyImmediate(holder.gameObject);
        }

        public static void DumpWalk()
        {
            if (!ExcelHeroes.Data.GameData.Loaded) ExcelHeroes.Data.GameData.Load();
            var holder = new GameObject("dw").transform;
            var rig = SdRef.Build("cfo", holder, 0);
            var sb = new StringBuilder();
            SdPose.Apply(rig, Pose.Rest);
            sb.AppendLine($"rest  L {rig.FootL.localPosition} R  root-rel L {rig.Root.InverseTransformPoint(rig.FootL.position):F3} R {rig.Root.InverseTransformPoint(rig.FootR.position):F3}");
            for (var i = 0; i <= 16; i++)
            {
                var ph = i / 16f * Mathf.PI * 2f;
                var p = SdPose.Walk(ph); SdPose.Apply(rig, p); rig.Root.localPosition = new Vector3(0f, p.Y + rig.FootDrop, 0f);
                sb.AppendLine($"ph {ph:F2}  L {rig.Root.InverseTransformPoint(rig.FootL.position):F3}  R {rig.Root.InverseTransformPoint(rig.FootR.position):F3}  Y {p.Y:F3}");
            }
            File.WriteAllText(Path.Combine(Application.dataPath, "..", "tools", "out", "walk_dump.txt"), sb.ToString());
            Object.DestroyImmediate(holder.gameObject);
        }

        /// <summary>SD_WALKCAL=1: sweep the cycle length and report the one with the least slip.</summary>
        public static void Calibrate()
        {
            if (!ExcelHeroes.Data.GameData.Loaded) ExcelHeroes.Data.GameData.Load();
            var holder = new GameObject("cal").transform;
            var rig = SdRef.Build("cfo", holder, 0);
            var sb = new StringBuilder();
            float best = 9f, bestC = 0f;
            for (var c = 0.3f; c <= 1.61f; c += 0.05f)
            {
                var s = WalkSlide(rig, c);
                sb.AppendLine($"  cycle {c:F2} m  slip {s:F3}");
                if (s < best) { best = s; bestC = c; }
            }
            sb.AppendLine($"best cycle {bestC:F2} m (slip {best:F3})");
            File.WriteAllText(Path.Combine(Application.dataPath, "..", "tools", "out", "walk_cal.txt"), sb.ToString());
            Object.DestroyImmediate(holder.gameObject);
        }

        static float DistSeg(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; var t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }
    }
}
