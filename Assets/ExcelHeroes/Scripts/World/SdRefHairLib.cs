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
                // not kayoko: her hair sheet is split black / white, which survives any recolour as a stripe
                "long" => h % 2 == 0 ? "hikari" : "mika",
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
        /// <summary>
        /// A sample's hair cut shorter: triangles wholly below `cutY` (world height) go, so a long
        /// straight fall becomes a bob or a short crop with the sample's own cap, fringe and strands —
        /// the samples have no short hair, and the base figure's trimmed cap looked lumpy beside them.
        /// </summary>
        [System.Flags] public enum Region { Cap = 1, Front = 2, Side = 4, Back = 8, Extra = 16, Tails = 32, All = 63 }

        // the chains that are TAILS (twin tails, a side ponytail) rather than locks or a fall, per sample by
        // the tag after "hair_" — mounted only when a recipe asks for them (tails=), never with a side or back
        static readonly Dictionary<string, string[]> TailTags = new()
        {
            ["yuuka"] = new[] { "l", "r" },            // her high twin tails; m_l / m_r are the side locks
            ["reisa"] = new[] { "bl", "br" },
            ["miku"] = new[] { "b_l", "b_r" },
            ["natsu"] = new[] { "r", "r_m", "r_f" },   // the side ponytail
        };

        static string _lib = "";

        /// <summary>Which part of the head a hair bone moves (the samples' own names: f front, l/r/m sides, b back, dango / ribbon / t extras).</summary>
        public static Region RegionOf(string bone)
        {
            if (string.IsNullOrEmpty(bone) || bone.StartsWith("Bip001")) return Region.Cap;
            var n = bone.ToLowerInvariant().Replace("ch0242_", "");
            var ti = n.IndexOf("hair_");
            if (ti >= 0 && TailTags.TryGetValue(_lib, out var tails))
            {
                var full = System.Text.RegularExpressions.Regex.Replace(n.Substring(ti + 5), @"_?\d+$", "");
                if (System.Array.IndexOf(tails, full) >= 0) return Region.Tails;
            }
            if (n.Contains("dango") || n.Contains("ribbo") || n.Contains("shawl") || n.Contains("earing")) return Region.Extra;
            var m = System.Text.RegularExpressions.Regex.Match(n, @"hair_([a-z]+)");
            var tag = m.Success ? m.Groups[1].Value : "";
            if (tag.StartsWith("b")) return Region.Back;
            if (tag == "t" || tag == "ft" || tag == "f" || tag == "ff") return Region.Front;
            if (tag.StartsWith("f") && tag.Length > 1) return tag == "fb" ? Region.Side : Region.Front;
            if (tag.StartsWith("l") || tag.StartsWith("r") || tag.StartsWith("m")) return Region.Side;
            return Region.Cap;
        }

        static Region _regions = Region.All;

        /// <summary>Only the given regions of a sample's hair (a fringe from one sample, the back from another).</summary>
        public static bool MountParts(ChibiRig rig, string name, SdLook k, int layer, Region regions, float cutY = float.NaN)
        {
            _regions = regions; _cutY = cutY; _lib = name;
            try { return Mount(rig, name, k, layer); } finally { _regions = Region.All; _cutY = float.NaN; _lib = ""; }
        }

        static Mesh Filter(Mesh src, SdHairBones lib)
        {
            if (_regions == Region.All || lib?.bones == null) return src;
            var bw = src.boneWeights; if (bw.Length != src.vertexCount) return src;
            var reg = new Region[bw.Length];
            for (var i = 0; i < bw.Length; i++)
            {
                var w = bw[i]; var bi = w.boneIndex0; var mx = w.weight0;
                if (w.weight1 > mx) { bi = w.boneIndex1; mx = w.weight1; }
                if (w.weight2 > mx) { bi = w.boneIndex2; mx = w.weight2; }
                if (w.weight3 > mx) bi = w.boneIndex3;
                reg[i] = bi >= 0 && bi < lib.bones.Length ? RegionOf(lib.bones[bi].n) : Region.Cap;
            }
            var m = Object.Instantiate(src);
            for (var s = 0; s < m.subMeshCount; s++)
            {
                var tris = m.GetTriangles(s); var keep = new List<int>(tris.Length);
                for (var t = 0; t < tris.Length; t += 3)
                {
                    // a triangle goes with the region most of its corners belong to
                    Region a = reg[tris[t]], b2 = reg[tris[t + 1]], c = reg[tris[t + 2]];
                    var r = a == b2 || a == c ? a : b2 == c ? b2 : a;
                    if ((_regions & r) != 0) { keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]); }
                }
                m.SetTriangles(keep, s, false);
            }
            return m;
        }

        public static bool MountCut(ChibiRig rig, string name, SdLook k, int layer, float cutY)
        {
            _cutY = cutY;
            try { return Mount(rig, name, k, layer); } finally { _cutY = float.NaN; }
        }
        static float _cutY = float.NaN;

        /// <summary>
        /// The hero's own hair shape over a sample's: Volume puffs it out sideways around the skull
        /// (0.94 sleek .. 1.14 fluffy), Fall stretches what hangs below the jaw (0.8 .. 1.3). Set by
        /// SdSample.Recipe for the parts it mounts, identity otherwise.
        /// </summary>
        public static float Volume = 1f, Fall = 1f;

        static Mesh Shape(Mesh src, Matrix4x4 toWorld, ChibiRig rig)
        {
            if (Mathf.Abs(Volume - 1f) < 0.005f && Mathf.Abs(Fall - 1f) < 0.005f) return src;
            var m = src == null ? null : Object.Instantiate(src); if (m == null) return src;
            var toLocal = toWorld.inverse;
            var v = m.vertices;
            var head = rig.Head.position; var h = rig.Height;
            var skull = head + Vector3.up * h * 0.1f;           // the middle of the big chibi head
            var jaw = rig.Neck != null ? rig.Neck.position.y : head.y - h * 0.02f;
            for (var i = 0; i < v.Length; i++)
            {
                var w = toWorld.MultiplyPoint3x4(v[i]);
                // sideways puff, strongest at the skull's height and fading toward the crown and the tips
                var along = Mathf.Clamp01(1f - Mathf.Abs(w.y - skull.y) / (h * 0.3f));
                var s = 1f + (Volume - 1f) * (0.4f + 0.6f * along);
                w.x = skull.x + (w.x - skull.x) * s; w.z = skull.z + (w.z - skull.z) * s;
                if (w.y < jaw) w.y = jaw - (jaw - w.y) * Fall;
                v[i] = toLocal.MultiplyPoint3x4(w);
            }
            m.vertices = v; m.RecalculateBounds();
            return m;
        }

        static Mesh Cut(Mesh src, Matrix4x4 toWorld)
        {
            if (float.IsNaN(_cutY)) return src;
            var m = Object.Instantiate(src);
            var v = src.vertices;
            var wy = new float[v.Length];
            for (var i = 0; i < v.Length; i++) wy[i] = toWorld.MultiplyPoint3x4(v[i]).y;
            for (var s = 0; s < m.subMeshCount; s++)
            {
                var tris = m.GetTriangles(s); var keep = new List<int>(tris.Length);
                for (var t = 0; t < tris.Length; t += 3)
                    // a strand crossing the line goes when most of it hangs below (its centre): keeping
                    // every crossing triangle left the ends a hand's width past the cut
                    if ((wy[tris[t]] + wy[tris[t + 1]] + wy[tris[t + 2]]) / 3f > _cutY)
                    { keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]); }
                m.SetTriangles(keep, s, false);
            }
            m.RecalculateBounds();
            return m;
        }

        public static bool Mount(ChibiRig rig, string name, SdLook k, int layer)
        {
            if (rig.Head == null || !Has(name)) return false;
            var mat = MeshKit.NewToon(0.004f, SdRefTex.Hair(k, Sheets[name]));
            mat.SetFloat("_ShadeStrength", 0.22f);
            if (Bones.TryGetValue(name, out var lib) && Skinned(rig, name, lib, mat, layer)) return true;
            var go = MeshKit.Part("hair:" + name, rig.Head, Meshes[name], mat, layer);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            go.GetComponent<MeshFilter>().sharedMesh = Shape(Cut(Meshes[name], go.transform.localToWorldMatrix), go.transform.localToWorldMatrix, rig);
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
            var toMesh = go.transform.localToWorldMatrix;
            var filtered = Filter(Meshes[name], lib);
            var cut = Shape(Cut(filtered, toMesh), toMesh, rig);
            var mesh = cut != Meshes[name] ? cut : Object.Instantiate(Meshes[name]);
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
