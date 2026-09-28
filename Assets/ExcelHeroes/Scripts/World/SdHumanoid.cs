using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The sample rig (3ds Max Biped, "Bip001 …") as a Unity Humanoid avatar, built at runtime.
    /// With it any humanoid animation clip (Mixamo, a CC0 library, mocap) plays on our SD figures
    /// through Mecanim's retargeting — the procedural poses in SdPose were hand-written joint
    /// angles and could not reach the weight and timing of keyframed motion.
    ///
    /// The mapping is by name (Biped's names are fixed). The skeleton description is read from the
    /// rig's rest pose; AvatarBuilder wants that pose close to a T-pose, which the samples are
    /// (arms out ~40° below horizontal: Mecanim tolerates it; the muscles are measured from it).
    /// </summary>
    public static class SdHumanoid
    {
        static readonly (string human, string bip)[] Map =
        {
            ("Hips", "Bip001 Pelvis"), ("Spine", "Bip001 Spine"), ("Chest", "Bip001 Spine1"), ("Neck", "Bip001 Neck"), ("Head", "Bip001 Head"),
            ("LeftShoulder", "Bip001 L Clavicle"), ("LeftUpperArm", "Bip001 L UpperArm"), ("LeftLowerArm", "Bip001 L Forearm"), ("LeftHand", "Bip001 L Hand"),
            ("RightShoulder", "Bip001 R Clavicle"), ("RightUpperArm", "Bip001 R UpperArm"), ("RightLowerArm", "Bip001 R Forearm"), ("RightHand", "Bip001 R Hand"),
            ("LeftUpperLeg", "Bip001 L Thigh"), ("LeftLowerLeg", "Bip001 L Calf"), ("LeftFoot", "Bip001 L Foot"), ("LeftToes", "Bip001 L Toe0"),
            ("RightUpperLeg", "Bip001 R Thigh"), ("RightLowerLeg", "Bip001 R Calf"), ("RightFoot", "Bip001 R Foot"), ("RightToes", "Bip001 R Toe0"),
            ("Left Thumb Proximal", "Bip001 L Finger0"), ("Left Thumb Intermediate", "Bip001 L Finger01"),
            ("Left Index Proximal", "Bip001 L Finger1"), ("Left Index Intermediate", "Bip001 L Finger11"),
            ("Left Middle Proximal", "Bip001 L Finger2"), ("Left Middle Intermediate", "Bip001 L Finger21"),
            ("Right Thumb Proximal", "Bip001 R Finger0"), ("Right Thumb Intermediate", "Bip001 R Finger01"),
            ("Right Index Proximal", "Bip001 R Finger1"), ("Right Index Intermediate", "Bip001 R Finger11"),
            ("Right Middle Proximal", "Bip001 R Finger2"), ("Right Middle Intermediate", "Bip001 R Finger21"),
        };

        static readonly Dictionary<string, Avatar> Cache = new();

        /// <summary>The avatar for a model root (one per distinct skeleton, cached by the root's name).</summary>
        public static Avatar Build(Transform modelRoot, string cacheKey)
        {
            if (Cache.TryGetValue(cacheKey, out var cached) && cached != null) return cached;
            var all = modelRoot.GetComponentsInChildren<Transform>(true);
            var byName = new Dictionary<string, Transform>();
            foreach (var t in all) if (!byName.ContainsKey(t.name)) byName[t.name] = t;

            var human = new List<HumanBone>();
            foreach (var (h, b) in Map)
            {
                if (!byName.ContainsKey(b)) continue;
                human.Add(new HumanBone { humanName = h, boneName = b, limit = new HumanLimit { useDefaultValues = true } });
            }
            // The skeleton description must be a T-pose: Mecanim measures every muscle from it. The
            // samples rest in an A-pose (arms ~40° down, a little forward), which made every clip's
            // arms cross in front of the chest. So the limbs are straightened into a T for the
            // description only, and put back.
            var saved = all.ToDictionary(t => t, t => t.localRotation);
            TPose(byName, modelRoot);
            var skeleton = all.Select(t => new SkeletonBone
            {
                name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale,
            }).ToArray();
            foreach (var kv in saved) kv.Key.localRotation = kv.Value;
            var desc = new HumanDescription
            {
                human = human.ToArray(), skeleton = skeleton,
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
            };
            var avatar = AvatarBuilder.BuildHumanAvatar(modelRoot.gameObject, desc);
            avatar.name = "sdhuman:" + cacheKey;
            Cache[cacheKey] = avatar;
            return avatar;
        }

        static void Aim(Transform bone, Transform child, Vector3 worldDir)
        {
            if (bone == null || child == null) return;
            var cur = child.position - bone.position;
            if (cur.sqrMagnitude < 1e-12f) return;
            bone.rotation = Quaternion.FromToRotation(cur, worldDir) * bone.rotation;
        }

        static void TPose(Dictionary<string, Transform> b, Transform root)
        {
            Transform T(string n) => b.TryGetValue(n, out var t) ? t : null;
            var pelvis = T("Bip001 Pelvis"); var head = T("Bip001 Head");
            if (pelvis == null || head == null) return;
            var up = (head.position - pelvis.position).normalized;
            var lr = (T("Bip001 L UpperArm").position - T("Bip001 R UpperArm").position);
            var left = Vector3.ProjectOnPlane(lr, up).normalized;             // toward the figure's left
            foreach (var (side, dir) in new[] { ("L", left), ("R", -left) })
            {
                Aim(T($"Bip001 {side} UpperArm"), T($"Bip001 {side} Forearm"), dir);
                Aim(T($"Bip001 {side} Forearm"), T($"Bip001 {side} Hand"), dir);
                Aim(T($"Bip001 {side} Thigh"), T($"Bip001 {side} Calf"), -up);
                Aim(T($"Bip001 {side} Calf"), T($"Bip001 {side} Foot"), -up);
            }
        }

        public static IEnumerable<string> MissingBones(Transform modelRoot)
        {
            var names = new HashSet<string>(modelRoot.GetComponentsInChildren<Transform>(true).Select(t => t.name));
            return Map.Where(m => !names.Contains(m.bip)).Select(m => m.bip);
        }
    }
}
