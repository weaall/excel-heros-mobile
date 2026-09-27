using System.Collections.Generic;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// A library hair's bones (hairlib/&lt;name&gt;_bones.json, written by HairLibExtract), index-aligned
    /// with the mesh's bone weights. A Bip001 bone (p = "") is found by name on our rig; any other
    /// bone is rebuilt under its parent from the sample's local TRS. Parents come before children.
    /// </summary>
    [System.Serializable]
    public class SdHairBones
    {
        [System.Serializable]
        public class Bone { public string n, p; public Vector3 pos; public Quaternion rot; public Vector3 scl; }
        public Bone[] bones;
    }

    /// <summary>
    /// Hair from the other samples, mounted on the base head. Every sample shares the Bip001 rig
    /// with the head bone at the same spot, so a hair mesh saved in head-bone-local space
    /// (Assets/_Ref/Editor/HairLibExtract.cs → Resources/Art/SDBase/hairlib/&lt;name&gt;.asset, git-
    /// ignored like the base) sits right when parented to our Head bone at identity. Its own hair
    /// sheet is repainted in the character's colour (SdRefTex.Hair). With its bone file the hair is
    /// skinned to its own chain, rebuilt under our head (names suffixed <see cref="Tag"/>) so
    /// SdSecondary swings it; without one it is rigid.
    /// </summary>
    public static class SdRefHairLib
    {
        /// <summary>Suffix on the chain bones a library hair brings; SdSecondary tells them from the base cap's by it.</summary>
        public const string Tag = "#lib";

        static readonly Dictionary<string, Mesh> Meshes = new();
        static readonly Dictionary<string, Texture2D> Sheets = new();
        static readonly Dictionary<string, SdHairBones> Bones = new();
        static readonly HashSet<string> Missing = new();

        /// <summary>Which sample's hair a style takes, by a stable hash of the id; null = keep the base cap.</summary>
        public static string Pick(string style, string id)
        {
            var h = SdPose.Hash(id) / 13;
            return style switch
            {
                "twin" => h % 2 == 0 ? "miku" : "haruka",
                "long" => (h % 3) switch { 0 => "kayoko", 1 => "hikari", _ => "mika" },
                "side" => "natsu",
                "ponytail" => h % 2 == 0 ? "yuuka" : null,
                "curly" => "reisa",
                _ => null,
            };
        }

        public static bool Has(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "base" || Missing.Contains(name)) return false;
            if (Meshes.ContainsKey(name)) return true;
            var m = Resources.Load<Mesh>("Art/SDBase/hairlib/" + name);
            var t = Resources.Load<Texture2D>("Art/SDBase/hairlib/" + name + "_hair");
            if (m == null || t == null) { Missing.Add(name); return false; }
            Meshes[name] = m; Sheets[name] = t;
            var j = Resources.Load<TextAsset>("Art/SDBase/hairlib/" + name + "_bones");
            var b = j != null ? JsonUtility.FromJson<SdHairBones>(j.text) : null;
            if (b?.bones != null && b.bones.Length > 0 && m.boneWeights.Length == m.vertexCount) Bones[name] = b;
            return true;
        }

        /// <summary>Mounts the named hair on the head bone in the character's colour. False when the library lacks it.</summary>
        public static bool Mount(ChibiRig rig, string name, SdLook k, int layer)
        {
            if (rig.Head == null || !Has(name)) return false;
            var mat = MeshKit.NewToon(0.004f, SdRefTex.Hair(k, Sheets[name]));
            mat.SetFloat("_ShadeStrength", 0.22f);
            if (Bones.TryGetValue(name, out var lib) && Skinned(rig, name, lib, mat, layer)) return true;
            var go = MeshKit.Part("hair:" + name, rig.Head, Meshes[name], mat, layer);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
            return true;
        }

        /// <summary>
        /// Rebuilds the sample's hair bones under ours and skins the mesh to them. Must run with the
        /// rig at rest: the bind poses are taken from the bones as they stand, so the rest shape is
        /// exactly the rigid head-local mesh whatever small differences the samples' rigs have.
        /// </summary>
        static bool Skinned(ChibiRig rig, string name, SdHairBones lib, Material mat, int layer)
        {
            var rigBones = new Dictionary<string, Transform>();
            foreach (var t in rig.Root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Bip001") && !rigBones.ContainsKey(t.name)) rigBones[t.name] = t;
            var made = new Dictionary<string, Transform>();
            var bones = new Transform[lib.bones.Length];
            for (var i = 0; i < lib.bones.Length; i++)
            {
                var e = lib.bones[i];
                if (string.IsNullOrEmpty(e.p))
                {
                    if (!rigBones.TryGetValue(e.n, out bones[i])) bones[i] = rig.Head;   // a Bip001 bone our rig lacks: ride the head
                    continue;
                }
                var parent = made.TryGetValue(e.p, out var mp) ? mp : rigBones.TryGetValue(e.p, out var rp) ? rp : rig.Head;
                var b = new GameObject(e.n + Tag).transform;
                b.SetParent(parent, false);
                b.localPosition = e.pos; b.localRotation = e.rot; b.localScale = e.scl;
                made[e.n] = b; bones[i] = b;
            }
            var go = new GameObject("hair:" + name) { layer = layer };
            go.transform.SetParent(rig.Head, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            // a per-rig copy: the bind poses belong to this rig's rest, not to the shared asset
            var mesh = Object.Instantiate(Meshes[name]);
            var toMesh = go.transform.localToWorldMatrix;
            var bind = new Matrix4x4[bones.Length];
            for (var i = 0; i < bones.Length; i++) bind[i] = bones[i].worldToLocalMatrix * toMesh;
            mesh.bindposes = bind;
            smr.sharedMesh = mesh; smr.bones = bones; smr.rootBone = rig.Head;
            smr.sharedMaterial = mat;
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rig.Renderers.Add(smr);
            return true;
        }
    }
}
