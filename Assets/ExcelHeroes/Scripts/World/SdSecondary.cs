using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// Secondary motion on the sample rig. The FBX skins its hair (bone_hair_b_l/m/r_01..03 down
    /// the back, bone_hair_Bt_00..02 the ponytail, bone_hair_f/fL the fringe, bone_hair_m_l/r the
    /// side locks), the gym shorts' hem (bone_skirt_*) and the chest tag (bone_Nameplate_00..02)
    /// to chains of small bones that the reference drives with physics. Here each bone is a
    /// damped spring: its tip wants to be where the rigid rest would put it, lags when the head
    /// or body ACCELERATES (the damping is relative to the rest point's own motion, so hair rides
    /// along at a steady walk instead of streaming back like drag), sags a little under gravity,
    /// and never bends past MaxBend. Runs in LateUpdate, after the frame's pose has been applied,
    /// so the chains trail the motion.
    /// </summary>
    public class SdSecondary : MonoBehaviour
    {
        const float Stiffness = 700f, Damping = 38f, MaxBend = 20f, MaxStep = 1f / 90f;   // ~4 Hz, ζ≈0.7: a soft bounce that settles in a few frames
        static readonly Vector3 Gravity = new(0f, -2f, 0f);

        class Node
        {
            public Transform T; public Quaternion RestLocal; public Vector3 RestTipLocal; public float Len; public Vector3 Tip, Vel, PrevTarget; public float Weight; public bool HasPrev;
            public float[] LegRest;   // a skirt bone: its tip's rest distance from each thigh (0 = that thigh is not its neighbour)
        }
        // the thighs as segments (hip → knee): a skirt bone's tip is never let closer to a thigh than it
        // hangs at rest, so a leg stepping forward (the walk, a flinch) pushes the hem out instead of
        // coming through it (the strip audit: "skirt torn, leg clipping")
        Transform[] _thigh, _knee;
        // a shawl / cape skinned to the UPPER ARMS (CH0247's bone_shawl_L/R) rode up with a raised arm and
        // flipped its dark lining out behind the back (the strip audit: "black shape behind head"): it
        // follows the arm only partly, the rest of the way it stays with the chest
        readonly List<(Transform t, Transform chest, Quaternion rel, Quaternion restLocal)> _drape = new();
        const float DrapeFollow = 0.3f;
        readonly List<Node> _nodes = new();     // root-first
        bool _ready;

        public void Init(Transform model, string style = "")
        {
            _nodes.Clear();
            var all = model.GetComponentsInChildren<Transform>(true);
            // a library hair (SdRefHairLib) brings its own chain, tagged; the base cap is hidden then,
            // so its hair bones swing nothing and are left out
            var lib = all.Any(t => t.name.EndsWith(SdRefHairLib.Tag));
            bool Dyn(Transform t) => t.name.EndsWith(SdRefHairLib.Tag)
                || (!lib && t.name.StartsWith("bone_hair_")) || t.name.StartsWith("bone_skirt_") || t.name.StartsWith("bone_Nameplate_");
            // depth order so parents are stepped before children
            foreach (var t in all.Where(Dyn).OrderBy(Depth))
            {
                var child = Enumerable.Range(0, t.childCount).Select(t.GetChild).FirstOrDefault(Dyn) ?? (t.childCount > 0 ? t.GetChild(0) : null);
                Vector3 tipWorld;
                if (child != null) tipWorld = child.position;
                else if (t.parent != null) tipWorld = t.position + (t.position - t.parent.position);     // a chain end: extrapolate the parent segment
                else continue;
                var len = (tipWorld - t.position).magnitude;
                if (len < 1e-5f) continue;
                // the tag and the shorts swing less than hair; the fringe barely at all
                var w = t.name.StartsWith("bone_hair_f") ? 0.35f : t.name.StartsWith("bone_hair_m") ? 0.6f : t.name.StartsWith("bone_skirt_") ? 0.5f : t.name.StartsWith("bone_Nameplate_") ? 0.7f : 1f;
                // the ponytail chain: its bones sit at the crown, so a twin-tail copy hanging out to the
                // side swings on a long lever — keep that chain stiff for twins, moderate otherwise
                // a library hair's tails are real chains hanging where they belong, not copies on a
                // borrowed lever, so they keep the plain weights
                var own = t.name.EndsWith(SdRefHairLib.Tag);
                if (own) w = LibWeight(t.name);
                else if (t.name.StartsWith("bone_hair_Bt")) w = style == "twin" ? 0.25f : 0.6f;
                // the twin copies hang off the back-hair chains too, out to the sides: the same long lever
                if (style == "twin" && !own && t.name.StartsWith("bone_hair_b_")) w = 0.3f;
                _nodes.Add(new Node { T = t, RestLocal = t.localRotation, RestTipLocal = t.InverseTransformPoint(tipWorld), Len = len, Tip = tipWorld, Weight = w });
            }
            Transform F(string n) => all.FirstOrDefault(t => t.name == n);
            _thigh = new[] { F("Bip001 L Thigh"), F("Bip001 R Thigh") }; _knee = new[] { F("Bip001 L Calf"), F("Bip001 R Calf") };
            if (_thigh.All(t => t != null) && _knee.All(t => t != null))
            {
                var gap = (_thigh[0].position - _thigh[1].position).magnitude;
                foreach (var nd in _nodes.Where(x => x.T.name.StartsWith("bone_skirt_")))
                {
                    nd.LegRest = new float[2];
                    for (var j = 0; j < 2; j++) { var d = (nd.Tip - Closest(_thigh[j].position, _knee[j].position, nd.Tip)).magnitude; nd.LegRest[j] = d < gap * 1.3f ? d * 0.95f : 0f; }
                }
            }
            _drape.Clear();
            var chest = F("Bip001 Spine1");
            if (chest != null)
                foreach (var t in all.Where(t => t.name.StartsWith("bone_shawl_") && t.parent != null && t.parent.name.Contains("UpperArm")))
                    _drape.Add((t, chest, Quaternion.Inverse(chest.rotation) * t.rotation, t.localRotation));
            _ready = _nodes.Count > 0 || _drape.Count > 0;
        }

        static Vector3 Closest(Vector3 a, Vector3 b, Vector3 p)
        {
            var ab = b - a; var t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude)); return a + ab * t;
        }

        static int Depth(Transform t) { var d = 0; while (t.parent != null) { d++; t = t.parent; } return d; }

        /// <summary>
        /// How freely a library hair bone swings, from its name. The samples do not agree on names
        /// (bone_hair_F_01, Bone_hair_BR_03, bone_CH0242_hair_bl_04, bone_hair_F_R_00 …), so the
        /// name is reduced to its part letters first: f = fringe, m / l / r = side locks, t = the
        /// crown, b… = back hair and tails, dango = a bun (a lump on the head, barely moves).
        /// </summary>
        static float LibWeight(string name)
        {
            var s = name.ToLowerInvariant().Replace(SdRefHairLib.Tag, "");
            if (!s.Contains("hair")) return s.Contains("ribbon") || s.Contains("ribborn") ? 0.7f : 0.3f;   // ribbons, a shawl root
            s = s.Substring(s.IndexOf("hair") + 4).Trim('_');
            if (s.StartsWith("dango")) return 0.25f;
            if (s.StartsWith("fb")) return 0.6f;                       // a long lock that starts at the front
            if (s.StartsWith("f") || s.StartsWith("t")) return 0.35f;
            if (s.StartsWith("b")) return 1f;
            if (s.StartsWith("m") || s.StartsWith("l") || s.StartsWith("r")) return 0.65f;
            return 0.6f;
        }

        void LateUpdate()
        {
            if (!_ready) return;
            Step(Time.deltaTime);
        }

        /// <summary>One simulation step (split into sub-steps when large); public so a preview can run it by hand.</summary>
        public void Step(float dt)
        {
            if (!_ready || dt <= 0f) return;
            foreach (var (t, chest, rel, restLocal) in _drape)
            {
                t.localRotation = restLocal;
                t.rotation = Quaternion.Slerp(chest.rotation * rel, t.rotation, DrapeFollow);
            }
            var n = Mathf.Clamp(Mathf.CeilToInt(dt / MaxStep), 1, 8);
            var h = dt / n;
            for (var i = 0; i < n; i++) Sub(h);
        }

        void Sub(float dt)
        {
            foreach (var nd in _nodes)
            {
                var t = nd.T;
                t.localRotation = nd.RestLocal;                            // back to the rest each step: the target must not include last step's swing
                var target = t.TransformPoint(nd.RestTipLocal);           // where the rigid rest puts the tip, given the parent's current pose
                var restDir = (target - t.position).normalized;
                var targetVel = nd.HasPrev ? (target - nd.PrevTarget) / dt : Vector3.zero;
                nd.PrevTarget = target; nd.HasPrev = true;
                nd.Vel += (target - nd.Tip) * (Stiffness * dt);
                nd.Vel += Gravity * (nd.Weight * dt);
                var rel = (nd.Vel - targetVel) * Mathf.Exp(-Damping * dt);   // damp only the motion relative to the rest point
                nd.Vel = targetVel + rel;
                nd.Tip += nd.Vel * dt;
                var dir = nd.Tip - t.position;
                if (dir.sqrMagnitude < 1e-10f) dir = restDir; else dir.Normalize();
                var bend = MaxBend * nd.Weight;
                if (Vector3.Angle(restDir, dir) > bend) dir = Vector3.RotateTowards(restDir, dir, bend * Mathf.Deg2Rad, 0f);
                nd.Tip = t.position + dir * nd.Len;
                if (nd.LegRest != null)
                    for (var j = 0; j < 2; j++)
                    {
                        if (nd.LegRest[j] <= 0f) continue;
                        var c = Closest(_thigh[j].position, _knee[j].position, nd.Tip); var off = nd.Tip - c; var d = off.magnitude;
                        if (d >= nd.LegRest[j] || d < 1e-6f) continue;
                        var pushed = c + off * (nd.LegRest[j] / d);
                        dir = (pushed - t.position).normalized; nd.Tip = t.position + dir * nd.Len;
                    }
                t.rotation = Quaternion.FromToRotation(restDir, dir) * t.rotation;
            }
        }

        /// <summary>Puts every chain back to rest (after a teleport, so hair does not whip across the stage).</summary>
        public void Settle()
        {
            foreach (var nd in _nodes) { nd.T.localRotation = nd.RestLocal; nd.Tip = nd.T.TransformPoint(nd.RestTipLocal); nd.Vel = Vector3.zero; nd.HasPrev = false; }
        }
    }
}
