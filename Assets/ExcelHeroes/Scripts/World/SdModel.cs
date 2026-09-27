using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The 3D SD model of a hero: a mesh reconstructed from the hero's own 2D SD art (TripoSR, see
    /// tools/sd3d.py), so the 3D figure has the same face, hair, outfit and head size as the 2D
    /// one — auto-rigged here on the skeleton measured from the reference SD models:
    ///
    ///   head bone 62% of height · neck 59% · shoulders 55% · hips 34% · knees 18% · ankles 4%
    ///
    /// Format (Resources/Art/SD3D/&lt;id&gt;.bytes): "SDM1", int vertexCount, int triangleCount,
    /// then per vertex float3 position (metres, feet at y = 0, facing +Z), float3 normal,
    /// byte4 colour; then int3 per triangle. Colours are sRGB.
    /// </summary>
    public static class SdModel
    {
        public const float Height = 1.2f;

        // measured on the reference SD skeletons (fractions of total height)
        const float HeadF = 0.62f, NeckF = 0.59f, ShoulderF = 0.545f, ChestF = 0.49f, SpineF = 0.43f, HipF = 0.336f, KneeF = 0.18f, AnkleF = 0.044f;

        static readonly Dictionary<string, Mesh> Cache = new();
        static Material _mat;
        static Material Mat3D
        {
            get
            {
                if (_mat != null) return _mat;
                _mat = MeshKit.NewToon(0.0035f);
                _mat.SetFloat("_ShadeStrength", 0.2f);
                _mat.SetFloat("_Rim", 0.12f);
                return _mat;
            }
        }

        public static bool Has(string heroId) => Resources.Load<TextAsset>($"Art/SD3D/{Key(heroId)}") != null;
        static string Key(string heroId) => heroId == Data.GameData.MainId ? "intern" : heroId;

        /// <summary>Builds the rigged figure, or null if this hero has no 3D SD yet.</summary>
        public static ChibiRig Build(string heroId, Transform parent, int layer)
        {
            var asset = Resources.Load<TextAsset>($"Art/SD3D/{Key(heroId)}");
            if (asset == null) return null;
            var mesh = LoadMesh(Key(heroId), asset.bytes);
            if (mesh == null) return null;

            var root = new GameObject("sd3d:" + heroId) { layer = layer }.transform;
            root.SetParent(parent, false);
            var h = mesh.bounds.max.y;

            // skeleton — the same names ChibiRig.Bind looks for
            Transform Bone(string name, Transform p, Vector3 pos)   // pos in the root's space
            {
                var t = new GameObject(name) { layer = layer }.transform;
                t.SetParent(p, false);
                t.position = root.TransformPoint(pos);
                return t;
            }
            var shoulderX = ShoulderHalfWidth(mesh, h);
            var hipX = shoulderX * 0.42f;
            var body = Bone("body", root, new Vector3(0f, h * HipF, 0f));
            var spine = Bone("spine", body, new Vector3(0f, h * SpineF, 0f));
            var chest = Bone("chest", spine, new Vector3(0f, h * ChestF, 0f));
            var head = Bone("head", chest, new Vector3(0f, h * NeckF, 0f));
            var armL = Bone("armL", chest, new Vector3(shoulderX, h * ShoulderF, 0f));
            var armR = Bone("armR", chest, new Vector3(-shoulderX, h * ShoulderF, 0f));
            var legL = Bone("legL", body, new Vector3(hipX, h * HipF, 0f));
            var legR = Bone("legR", body, new Vector3(-hipX, h * HipF, 0f));
            var bones = new[] { body, spine, chest, head, armL, armR, legL, legR };

            var weights = Weights(mesh, h, shoulderX, hipX);
            var skinned = Object.Instantiate(mesh);
            skinned.boneWeights = weights;
            skinned.bindposes = System.Array.ConvertAll(bones, b => b.worldToLocalMatrix * root.localToWorldMatrix);

            var go = new GameObject("mesh") { layer = layer };
            go.transform.SetParent(root, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = skinned;
            smr.bones = bones;
            smr.rootBone = body;
            // a hairline outline: the reconstructed surface is not a clean sculpt, and a thick
            // inverted hull pokes through its small bumps as black specks
            smr.sharedMaterial = Mat3D;
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var sh = new MeshKit.Builder();
            sh.Quad(new Vector3(0f, 0.004f, 0f), new Vector3(0.28f, 0f, 0f), new Vector3(0f, 0f, 0.2f), new Color(0.1f, 0.14f, 0.25f, 0.4f));
            MeshKit.Part("shadow", root, sh.Bake("shadow"), ChibiBuilder.ShadowMat, layer);

            var rig = new ChibiRig { Root = root, Body = body, Head = head, ArmL = armL, ArmR = armR, LegL = legL, LegR = legR, Height = h, Model3D = true };
            rig.Renderers.Add(smr);
            return rig;
        }

        /// <summary>
        /// A 3D monster: the mascot's mesh (tools/mon3d.py, Resources/Art/SD3DM/&lt;id&gt;.bytes) on a
        /// body pivot so the sprite-era hop, lean and flinch still apply; no limbs. Height 1.0,
        /// facing +Z (BattleWorld turns it toward the squad). Null when the mascot has no mesh.
        /// </summary>
        public static ChibiRig BuildMonster(string typeId, Transform parent, int layer)
        {
            if (typeId == null) return null;
            var asset = Resources.Load<TextAsset>($"Art/SD3DM/{typeId}");
            if (asset == null) return null;
            var mesh = LoadMesh("m:" + typeId, asset.bytes);
            if (mesh == null) return null;
            var root = new GameObject("sd3dm:" + typeId) { layer = layer }.transform;
            root.SetParent(parent, false);
            var body = new GameObject("body") { layer = layer }.transform;
            body.SetParent(root, false);
            var part = MeshKit.Part("mesh", body, mesh, Mat3D, layer);
            var rig = new ChibiRig { Root = root, Body = body, Head = body, Height = mesh.bounds.max.y, Model3D = true };
            rig.Renderers.Add(part.GetComponent<MeshRenderer>());
            var sh = new MeshKit.Builder();
            var w = Mathf.Max(0.2f, mesh.bounds.extents.x * 1.1f);
            sh.Quad(new Vector3(0f, 0.004f, 0f), new Vector3(w, 0f, 0f), new Vector3(0f, 0f, w * 0.7f), new Color(0.1f, 0.14f, 0.25f, 0.4f));
            MeshKit.Part("shadow", root, sh.Bake("shadow"), ChibiBuilder.ShadowMat, layer);
            return rig;
        }

        static Mesh LoadMesh(string key, byte[] bytes)
        {
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            using var r = new BinaryReader(new MemoryStream(bytes));
            if (new string(r.ReadChars(4)) != "SDM1") return null;
            int nv = r.ReadInt32(), nt = r.ReadInt32();
            var pos = new Vector3[nv]; var nor = new Vector3[nv]; var col = new Color[nv];
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            for (var i = 0; i < nv; i++)
            {
                pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                nor[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                var c = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                Color cf = c;
                col[i] = linear ? new Color(cf.linear.r, cf.linear.g, cf.linear.b, 1f) : cf;
            }
            var tris = new int[nt * 3];
            for (var i = 0; i < tris.Length; i++) tris[i] = r.ReadInt32();
            m = new Mesh { name = "sd3d:" + key, indexFormat = nv > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(pos); m.SetNormals(nor); m.SetColors(col); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            Cache[key] = m;
            return m;
        }

        /// <summary>Half the shoulder span: the width of the figure in a thin slice at shoulder height.</summary>
        static float ShoulderHalfWidth(Mesh m, float h)
        {
            var v = m.vertices;
            var xs = new List<float>();
            foreach (var p in v) if (Mathf.Abs(p.y - h * (ShoulderF - 0.03f)) < h * 0.02f) xs.Add(Mathf.Abs(p.x));
            if (xs.Count == 0) return h * 0.12f;
            xs.Sort();
            return xs[(int)(xs.Count * 0.8f)] * 0.72f;
        }

        /// <summary>
        /// Region weights with soft seams: above the neck → head; outside the torso between hips
        /// and shoulders → that side's arm; below the hips → that side's leg; the rest → spine
        /// and chest by height. Two influences per vertex, blended over a short band.
        /// </summary>
        static BoneWeight[] Weights(Mesh m, float h, float shoulderX, float hipX)
        {
            var v = m.vertices;
            var w = new BoneWeight[v.Length];
            var torsoX = shoulderX * 0.8f;
            for (var i = 0; i < v.Length; i++)
            {
                var p = v[i];
                var y = p.y / h;
                var ax = Mathf.Abs(p.x);
                var side = p.x >= 0f ? 0 : 1;
                // torso blend between spine (1) and chest (2)
                var chestK = Mathf.InverseLerp(SpineF, ChestF + 0.02f, y);
                var torsoBone = chestK > 0.5f ? 2 : 1;
                var bw = new BoneWeight();

                if (y > NeckF - 0.02f)
                {
                    var k = Mathf.Clamp01((y - (NeckF - 0.02f)) / 0.04f);
                    Set(ref bw, 3, k, 2, 1f - k);
                }
                else if (y > HipF - 0.02f && y < ShoulderF + 0.03f && ax > torsoX)
                {
                    var k = Mathf.Clamp01((ax - torsoX) / (shoulderX * 0.25f));
                    Set(ref bw, 4 + side, k, 2, 1f - k);
                }
                else if (y < HipF + 0.01f)
                {
                    var centre = Mathf.Clamp01(1f - ax / Mathf.Max(0.001f, hipX * 0.5f));   // 1 on the midline
                    var k = Mathf.Clamp01((HipF + 0.01f - y) / 0.05f) * (1f - centre * 0.5f);
                    Set(ref bw, 6 + side, k, 0, 1f - k);
                }
                else
                {
                    var k = chestK;
                    Set(ref bw, 2, k, 1, 1f - k);
                }
                w[i] = bw;
                _ = torsoBone;
            }
            return w;
        }

        static void Set(ref BoneWeight bw, int a, float wa, int b, float wb)
        {
            if (wa < wb) { (a, b) = (b, a); (wa, wb) = (wb, wa); }
            var s = Mathf.Max(0.0001f, wa + wb);
            bw.boneIndex0 = a; bw.weight0 = wa / s;
            bw.boneIndex1 = b; bw.weight1 = wb / s;
        }
    }
}
