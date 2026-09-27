using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The one SD body every character is built on — the reference's approach: a single base
    /// (same skeleton, same proportions, same size), and per character only hair, outfit, colours
    /// and the face texture change. So every figure is the same size and every motion
    /// (World/SdMotion) works on every figure.
    ///
    /// Proportions, fractions of height — measured from the reference FBX meshes (nine models,
    /// Assets/_Ref/Editor/Deep.cs: joints averaged, limb radius from the vertices each bone owns):
    ///   pelvis 0.356 · spine 0.446 · chest 0.513 · shoulder 0.572 (x 0.074) · neck 0.616 ·
    ///   head bone 0.647 · chin ≈0.60 · brows 0.78 · skull top ≈0.93 · hair top 1.0 ·
    ///   elbow 0.51 (x 0.156) · wrist 0.44 (x 0.247) · hip 0.356 (x 0.071) · knee 0.193 (x 0.08) ·
    ///   ankle 0.047 (x 0.09). Radii: thigh 0.048 · calf 0.033 · upper arm 0.037 · torso ≈0.075.
    ///   Face ±0.13 wide, front at z 0.115. Hair: 1–11 sculpted shells, not strands.
    ///
    /// Built from smooth lofted surfaces with analytic normals (no primitive soup): legs, torso,
    /// neck, A-pose arms, head with a tapered chin, hair as clumps that follow the scalp and fall,
    /// skirt with pleats, jacket/coat shells, collar, tie. The face is painted into the head's own
    /// texture (FaceTexture), not floated in front of it.
    /// </summary>
    public static class SdBase
    {
        public const float Height = 1.2f;

        // ---------------------------------------------------------------- skeleton --
        public enum B { Hips, Spine, Chest, Neck, Head, UpperArmL, ForearmL, HandL, UpperArmR, ForearmR, HandR, ThighL, CalfL, FootL, ThighR, CalfR, FootR, HairBack }
        static readonly int BoneCount = Enum.GetValues(typeof(B)).Length;

        // bone heads (joint positions), in units of height, character facing +Z, its left = +X
        static readonly Dictionary<B, Vector3> Joint = new()
        {
            [B.Hips] = new(0f, 0.356f, 0f), [B.Spine] = new(0f, 0.446f, 0f), [B.Chest] = new(0f, 0.513f, 0f),
            [B.Neck] = new(0f, 0.616f, 0f), [B.Head] = new(0f, 0.647f, 0.005f),
            [B.UpperArmL] = new(-0.074f, 0.572f, 0f), [B.ForearmL] = new(-0.156f, 0.51f, 0f), [B.HandL] = new(-0.247f, 0.439f, 0.009f),
            [B.UpperArmR] = new(0.074f, 0.572f, 0f), [B.ForearmR] = new(0.156f, 0.51f, 0f), [B.HandR] = new(0.247f, 0.439f, 0.009f),
            [B.ThighL] = new(-0.071f, 0.356f, 0f), [B.CalfL] = new(-0.08f, 0.193f, 0.004f), [B.FootL] = new(-0.09f, 0.047f, 0f),
            [B.ThighR] = new(0.071f, 0.356f, 0f), [B.CalfR] = new(0.08f, 0.193f, 0.004f), [B.FootR] = new(0.09f, 0.047f, 0f),
            [B.HairBack] = new(0f, 0.80f, -0.12f),
        };
        static readonly Dictionary<B, B> Parent = new()
        {
            [B.Spine] = B.Hips, [B.Chest] = B.Spine, [B.Neck] = B.Chest, [B.Head] = B.Neck,
            [B.UpperArmL] = B.Chest, [B.ForearmL] = B.UpperArmL, [B.HandL] = B.ForearmL,
            [B.UpperArmR] = B.Chest, [B.ForearmR] = B.UpperArmR, [B.HandR] = B.ForearmR,
            [B.ThighL] = B.Hips, [B.CalfL] = B.ThighL, [B.FootL] = B.CalfL,
            [B.ThighR] = B.Hips, [B.CalfR] = B.ThighR, [B.FootR] = B.CalfR, [B.HairBack] = B.Head,
        };

        // head shape
        // measured: face ±0.131 wide, front at z 0.115, chin ≈0.61, skull top ≈0.93
        static readonly Vector3 HeadC = new(0f, 0.77f, -0.015f);
        static readonly Vector3 HeadR = new(0.14f, 0.16f, 0.14f);

        // ---------------------------------------------------------------- builder --
        class SB
        {
            public readonly List<Vector3> V = new(), N = new();
            public readonly List<Vector2> UV = new(), UV2 = new();
            /// <summary>Marks the last added vertex (uv2.x); unmarked vertices are padded with zero at bake.</summary>
            public void Flag(float f) { UV2[UV2.Count - 1] = new Vector2(f, 0f); }
            public readonly List<Color> C = new();
            public readonly List<BoneWeight> W = new();
            public readonly List<int> T = new();

            public int Add(Vector3 p, Vector3 n, Vector2 uv, Color c, BoneWeight w)
            {
                V.Add(p); N.Add(n.normalized); UV.Add(uv); C.Add(c); W.Add(w);
                UV2.Add(Vector2.zero);                       // kept in step; Flag() overwrites the last entry
                return V.Count - 1;
            }

            public void Quad(int a, int b, int c, int d) { T.Add(a); T.Add(b); T.Add(c); T.Add(a); T.Add(c); T.Add(d); }
        }

        /// <summary>
        /// Shelf packer for the texture atlas: every surface gets its own rectangle, sized to its
        /// extent so texel density is even. 2048 square, 4 px gutters.
        /// </summary>
        class Packer
        {
            public const int Size = 2048;
            public static bool Log = System.Environment.GetEnvironmentVariable("SD_PACK_LOG") == "1";
            const int Gutter = 4;
            const float TexelsPerUnit = 800f;           // px per unit of character height
            int _x, _y, _rowH;
            public Rect Take(float wUnits, float hUnits)
            {
                var w = Mathf.Clamp(Mathf.CeilToInt(wUnits * TexelsPerUnit), 8, Size - Gutter * 2);
                var h = Mathf.Clamp(Mathf.CeilToInt(hUnits * TexelsPerUnit), 8, Size - Gutter * 2);
                if (_x + w + Gutter > Size) { _x = 0; _y += _rowH + Gutter; _rowH = 0; }
                if (_y + h + Gutter > Size) { Debug.LogWarning("[SdBase] atlas full"); _y = 0; }
                var r = new Rect((_x + Gutter) / (float)Size, (_y + Gutter) / (float)Size, (w - Gutter) / (float)Size, (h - Gutter) / (float)Size);
                _x += w + Gutter; _rowH = Mathf.Max(_rowH, h);
                if (Log) Debug.Log($"[SdBase.pack] {r.xMin:F3},{r.yMin:F3} {r.width:F3}x{r.height:F3}");
                return r;
            }
        }

        [ThreadStatic] static Packer _pack;

        struct Ring
        {
            public Vector3 C, R, F;   // centre, right axis, forward axis (unit)
            public float Rx, Rz;
            public Func<float, float> Shape;   // radius multiplier by angle (pleats), may be null
        }

        static BoneWeight W1(B a) => new() { boneIndex0 = (int)a, weight0 = 1f };
        static BoneWeight W2(B a, B b, float t)
        {
            t = Mathf.Clamp01(t);
            return new BoneWeight { boneIndex0 = (int)a, weight0 = 1f - t, boneIndex1 = (int)b, weight1 = t };
        }

        /// <summary>
        /// A loft through rings. Angle 0 = +R, 90° = +F. uvRect maps (angle, ring) into the atlas.
        /// Front-facing (+F) is u = 0.25 of the rect. Normals analytic from the ellipse.
        /// </summary>
        static void Loft(SB sb, IList<Ring> rings, int seg, Func<int, float, BoneWeight> weight, Func<float, float, Color> color,
                         Rect uvRect, bool capStart = false, bool capEnd = false, float openFrom = -1f, float openTo = -1f)
        {
            if (uvRect == Rect.zero && _pack != null)
            {
                // extent: mean perimeter across, path length down
                var per = 0f; var path = 0f;
                for (var i = 0; i < rings.Count; i++)
                {
                    var r = rings[i]; var p = 0f; Vector3 prev = default;
                    for (var j = 0; j <= 12; j++)
                    {
                        var a = j / 12f * Mathf.PI * 2f; var k = r.Shape?.Invoke(a) ?? 1f;
                        var q = r.C + r.R * (Mathf.Cos(a) * r.Rx * k) + r.F * (Mathf.Sin(a) * r.Rz * k);
                        if (j > 0) p += Vector3.Distance(q, prev);
                        prev = q;
                    }
                    per += p / rings.Count;
                    if (i > 0) path += Vector3.Distance(rings[i].C, rings[i - 1].C);
                }
                uvRect = _pack.Take(per, Mathf.Max(path, 0.01f));
            }
            var start = sb.V.Count;
            for (var i = 0; i < rings.Count; i++)
            {
                var r = rings[i];
                var tv = i / (float)(rings.Count - 1);
                for (var j = 0; j <= seg; j++)
                {
                    var u = j / (float)seg;
                    var a = u * Mathf.PI * 2f;
                    var k = r.Shape?.Invoke(a) ?? 1f;
                    var ca = Mathf.Cos(a); var sa = Mathf.Sin(a);
                    var p = r.C + r.R * (ca * r.Rx * k) + r.F * (sa * r.Rz * k);
                    var n = r.R * (ca / Mathf.Max(1e-4f, r.Rx)) + r.F * (sa / Mathf.Max(1e-4f, r.Rz));
                    sb.Add(p, n, new Vector2(uvRect.xMin + u * uvRect.width, uvRect.yMin + tv * uvRect.height), color(u, tv), weight(i, tv));
                }
            }
            var stride = seg + 1;
            for (var i = 0; i < rings.Count - 1; i++)
                for (var j = 0; j < seg; j++)
                {
                    var u = (j + 0.5f) / seg;
                    if (openFrom >= 0f && u > openFrom && u < openTo) continue;     // an open front (jacket)
                    var a = start + i * stride + j;
                    var b = a + stride;
                    sb.Quad(a, a + 1, b + 1, b);
                }
            if (capStart) Cap(sb, rings[0], seg, weight(0, 0f), color(0.25f, 0f), uvRect, false);
            if (capEnd) Cap(sb, rings[rings.Count - 1], seg, weight(rings.Count - 1, 1f), color(0.25f, 1f), uvRect, true);
        }

        static void Cap(SB sb, Ring r, int seg, BoneWeight w, Color c, Rect uvRect, bool end)
        {
            var axis = Vector3.Cross(r.F, r.R).normalized * (end ? 1f : -1f);
            var centre = sb.Add(r.C, axis, uvRect.center, c, w);
            var first = sb.V.Count;
            for (var j = 0; j <= seg; j++)
            {
                var a = j / (float)seg * Mathf.PI * 2f;
                var k = r.Shape?.Invoke(a) ?? 1f;
                sb.Add(r.C + r.R * (Mathf.Cos(a) * r.Rx * k) + r.F * (Mathf.Sin(a) * r.Rz * k), axis, uvRect.center, c, w);
            }
            for (var j = 0; j < seg; j++)
            {
                if (end) { sb.T.Add(centre); sb.T.Add(first + j + 1); sb.T.Add(first + j); }
                else { sb.T.Add(centre); sb.T.Add(first + j); sb.T.Add(first + j + 1); }
            }
        }

        static Ring Flat(Vector3 c, float rx, float rz, Func<float, float> shape = null) =>
            new() { C = c, R = Vector3.right, F = Vector3.forward, Rx = rx, Rz = rz, Shape = shape };

        /// <summary>A ring whose plane is perpendicular to `dir` (for limbs).</summary>
        static Ring Along(Vector3 c, Vector3 dir, float rx, float rz)
        {
            dir.Normalize();
            var f = Vector3.forward - Vector3.Dot(Vector3.forward, dir) * dir;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.right;
            f.Normalize();
            var r = Vector3.Cross(f, dir).normalized;
            return new Ring { C = c, R = r, F = f, Rx = rx, Rz = rz };
        }

        // ---------------------------------------------------------------- atlas --
        // Texture regions (u, v in 0..1): face 0..0.5 x 0.5..1, plain white elsewhere.
        // Rect.zero = "take a rect from the packer". The head and hair grids take theirs directly.
        static readonly Rect WhiteUV = Rect.zero;
        static Rect _headRect;
        /// <summary>Atlas rect of the face plate (eyes, brows, mouth). Public for the bake.</summary>
        public static Rect FaceRect { get; private set; }
        /// <summary>The face plate's box on the front of the head, fractions of height (x, y): the reference's EyeMouth+Eyebrow extent.</summary>
        public static readonly Rect FaceBox = Rect.MinMaxRect(-0.118f, 0.615f, 0.118f, 0.805f);

        // ---------------------------------------------------------------- build --
        public static ChibiRig Build(string heroId, Transform parent, int layer)
        {
            var look = SdLook.For(heroId);
            var root = new GameObject("sdb:" + heroId) { layer = layer }.transform;
            root.SetParent(parent, false);

            // skeleton
            var bones = new Transform[BoneCount];
            for (var i = 0; i < BoneCount; i++)
            {
                var id = (B)i;
                var t = new GameObject(id.ToString()) { layer = layer }.transform;
                bones[i] = t;
            }
            for (var i = 0; i < BoneCount; i++)
            {
                var id = (B)i;
                bones[i].SetParent(Parent.TryGetValue(id, out var p) ? bones[(int)p] : root, false);
            }
            for (var i = 0; i < BoneCount; i++) bones[i].position = root.TransformPoint(Joint[(B)i] * Height);

            var sb = new SB();
            _pack = new Packer();
            _headRect = _pack.Take(HeadR.x * 2f * Mathf.PI, HeadR.y * Mathf.PI);
            FaceRect = _pack.Take(FaceBox.width * 1.6f, FaceBox.height * 1.6f);
            BuildBody(sb, look);
            BuildHead(sb, look);
            BuildFacePlate(sb, look);
            BuildHair(sb, look);
            BuildOutfit(sb, look);

            FixWinding(sb);
            var mesh = new Mesh { name = "sdb:" + heroId, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var verts = new List<Vector3>(sb.V.Count);
            foreach (var v in sb.V) verts.Add(v * Height);
            var lin = new List<Color>(sb.C.Count);
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var hasBaked = Baked(look.Id) != null;
            // with a baked atlas the texture is the paint (white vertex colour) — except hair, whose
            // atlas texels are painted FROM the vertex colour (SdTexBake.PaintHairTri) and whose
            // shading still comes from it
            for (var vi = 0; vi < sb.C.Count; vi++)
            {
                var c = sb.C[vi];
                var hair = vi < sb.UV2.Count && sb.UV2[vi].x > 1.5f;
                lin.Add(hasBaked && !hair ? Color.white : linear ? new Color(c.linear.r, c.linear.g, c.linear.b, 1f) : c);
            }
            while (sb.UV2.Count < sb.V.Count) sb.UV2.Add(Vector2.zero);
            mesh.SetVertices(verts); mesh.SetNormals(sb.N); mesh.SetUVs(0, sb.UV); mesh.SetUVs(1, sb.UV2); mesh.SetColors(lin); mesh.SetTriangles(sb.T, 0);
            mesh.boneWeights = sb.W.ToArray();
            var bind = new Matrix4x4[BoneCount];
            for (var i = 0; i < BoneCount; i++) bind[i] = bones[i].worldToLocalMatrix * root.localToWorldMatrix;
            mesh.bindposes = bind;
            mesh.RecalculateBounds();
            var bad = 0; foreach (var v in verts) if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || v.magnitude > 10f) bad++;
            if (bad > 0 || mesh.bounds.size.magnitude > 5f) Debug.LogWarning($"[SdBase] {heroId}: bounds {mesh.bounds} bad verts {bad}/{verts.Count}");

            var go = new GameObject("mesh") { layer = layer };
            go.transform.SetParent(root, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bones[(int)B.Hips];
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mat = MeshKit.NewToon(0.0045f, Atlas(look));
            // a baked (painted) atlas already carries its own shading; the toon light stays soft
            mat.SetFloat("_ShadeStrength", hasBaked ? 0.12f : 0.28f);
            mat.SetFloat("_Rim", hasBaked ? 0.06f : 0.14f);
            smr.sharedMaterial = mat;

            var sh = new MeshKit.Builder();
            sh.Quad(new Vector3(0f, 0.004f, 0f), new Vector3(0.26f, 0f, 0f), new Vector3(0f, 0f, 0.18f), new Color(0.1f, 0.14f, 0.25f, 0.4f));
            MeshKit.Part("shadow", root, sh.Bake("shadow"), ChibiBuilder.ShadowMat, layer);

            var rig = new ChibiRig
            {
                Root = root, Body = bones[(int)B.Hips], Head = bones[(int)B.Head],
                ArmL = bones[(int)B.UpperArmL], ArmR = bones[(int)B.UpperArmR], LegL = bones[(int)B.ThighL], LegR = bones[(int)B.ThighR],
                Height = Height, Model3D = true, Base = bones,
            };
            rig.Renderers.Add(smr);
            return rig;
        }

        /// <summary>
        /// Every triangle faces the way its vertices' normals say it should. The lofts run up or
        /// down depending on the part, which flips their winding; checking each triangle against
        /// its own normals is simpler and cannot be wrong for a new part.
        /// </summary>
        static void FixWinding(SB sb)
        {
            var t = sb.T;
            for (var i = 0; i < t.Count; i += 3)
            {
                var a = sb.V[t[i]]; var b = sb.V[t[i + 1]]; var c = sb.V[t[i + 2]];
                var geo = Vector3.Cross(b - a, c - a);
                var n = sb.N[t[i]] + sb.N[t[i + 1]] + sb.N[t[i + 2]];
                if (Vector3.Dot(geo, n) < 0f) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
            }
        }

        // ---------------------------------------------------------------- body --
        static Color Solid(Color c) => c;

        /// <summary>
        /// One body segment lofted from a measured cross-section table: rings along p0→p1, each
        /// point at the table's radius for its angle. Ring angle a runs from -x (the character's
        /// left) through +z (front); the table's angle 0 is the back and 0.5 the front, so the
        /// table is read at 0.25 + a.
        /// </summary>
        static void Seg(SB sb, float[,] table, Vector3 p0, Vector3 p1, Func<float, BoneWeight> weight, Func<float, float, Color> color,
                        Rect uv, float t0 = -0.1f, float t1 = 1.05f, int n = 10, int seg = 18, bool capStart = false, bool capEnd = false,
                        float scale = 1f, float minR = 0f)
        {
            var rings = new List<Ring>();
            var dir = (p1 - p0).normalized;
            for (var q = 0; q <= n; q++)
            {
                var t = Mathf.Lerp(t0, t1, q / (float)n);
                var ring = Along(p0 + (p1 - p0) * t, dir, 1f, 1f);
                var tt = t;
                ring.Shape = a => Mathf.Max(minR, SdProfile.R(table, tt, 0.25f + a / (Mathf.PI * 2f)) * scale);
                rings.Add(ring);
            }
            Loft(sb, rings, seg, (qi, tv) => weight(Mathf.Lerp(t0, t1, tv)), (u, tv) => color(u, Mathf.Lerp(t0, t1, tv)), uv, capStart, capEnd);
        }

        static void BuildBody(SB sb, SdLook k)
        {
            var skin = k.Skin;
            Vector3 J(B b) => Joint[b];

            // legs
            for (var side = 0; side < 2; side++)
            {
                var l = side == 0;
                B thigh = l ? B.ThighL : B.ThighR, calf = l ? B.CalfL : B.CalfR, foot = l ? B.FootL : B.FootR;
                var hip = J(thigh); var knee = J(calf); var ankle = J(foot);
                Seg(sb, SdProfile.Thigh, hip, knee, t => W2(thigh, calf, Mathf.InverseLerp(0.8f, 1.05f, t)),
                    (u, t) => LegColor(k, Mathf.Lerp(hip.y, knee.y, t)), Rect.zero, -0.15f, 1.05f, 8, 16, true, false);
                Seg(sb, SdProfile.Calf, knee, ankle, t => t < 0.2f ? W2(thigh, calf, Mathf.InverseLerp(-0.1f, 0.2f, t)) : W2(calf, foot, Mathf.InverseLerp(0.85f, 1.05f, t)),
                    (u, t) => LegColor(k, Mathf.Lerp(knee.y, ankle.y, t)), Rect.zero, -0.1f, 1.02f, 8, 16, false, false);
                // foot: ankle down and forward to the toe
                var toe = ankle + new Vector3(0f, -0.03f, 0.07f);
                Seg(sb, SdProfile.Foot, ankle + new Vector3(0f, 0f, -0.012f), toe, t => W1(foot), (u, t) => k.Shoes, Rect.zero, -0.2f, 1.0f, 8, 14, true, true, 1f, 0.01f);
            }

            // torso: pelvis → spine → chest, one colour function by height
            Seg(sb, SdProfile.Pelvis, J(B.Hips), J(B.Spine), t => W2(B.Hips, B.Spine, Mathf.InverseLerp(0.5f, 1.0f, t)),
                (u, t) => TorsoColor(k, u, Mathf.Lerp(J(B.Hips).y, J(B.Spine).y, t)), Rect.zero, -0.25f, 1.0f, 8, 24, true, false);
            Seg(sb, SdProfile.Spine, J(B.Spine), J(B.Chest), t => W2(B.Spine, B.Chest, Mathf.InverseLerp(0.4f, 1.0f, t)),
                (u, t) => TorsoColor(k, u, Mathf.Lerp(J(B.Spine).y, J(B.Chest).y, t)), Rect.zero, -0.05f, 1.0f, 6, 24);
            Seg(sb, SdProfile.Spine1, J(B.Chest), J(B.Neck), t => W2(B.Chest, B.Neck, Mathf.InverseLerp(0.7f, 1.0f, t)),
                (u, t) => TorsoColor(k, u, Mathf.Lerp(J(B.Chest).y, J(B.Neck).y, t)), Rect.zero, -0.05f, 1.0f, 8, 24, false, true);
            // neck
            Seg(sb, SdProfile.Neck, J(B.Neck), J(B.Head) + new Vector3(0f, 0.03f, 0f), t => W2(B.Neck, B.Head, Mathf.InverseLerp(0.3f, 1.0f, t)),
                (u, t) => skin, Rect.zero, 0f, 1.0f, 4, 12);

            // arms, A-pose, from the shoulder joint
            for (var side = 0; side < 2; side++)
            {
                var l = side == 0;
                B ua = l ? B.UpperArmL : B.UpperArmR, fa = l ? B.ForearmL : B.ForearmR, hd = l ? B.HandL : B.HandR;
                var sh = J(ua); var el = J(fa); var wr = J(hd);
                Seg(sb, SdProfile.UpperArm, sh, el, t => W2(ua, fa, Mathf.InverseLerp(0.8f, 1.05f, t)),
                    (u, t) => k.ShortSleeve && t > 0.45f ? skin : k.Sleeve, Rect.zero, -0.15f, 1.05f, 6, 14, true, false);
                Seg(sb, SdProfile.Forearm, el, wr, t => t < 0.2f ? W2(ua, fa, Mathf.InverseLerp(-0.1f, 0.2f, t)) : W2(fa, hd, Mathf.InverseLerp(0.9f, 1.05f, t)),
                    (u, t) => k.ShortSleeve ? skin : (t > 0.86f && k.Cuff.a > 0f ? k.Cuff : t > 0.95f ? skin : k.Sleeve), Rect.zero, -0.1f, 1.0f, 6, 14);
                var tip = wr + (wr - el).normalized * 0.06f;
                Seg(sb, SdProfile.Hand, wr, tip, t => W1(hd), (u, t) => skin, Rect.zero, -0.1f, 1.0f, 6, 12, false, true, 1f, 0.006f);
            }
        }

        static Color LegColor(SdLook k, float y)
        {
            if (k.Pants) return y < 0.05f ? k.Socks : k.Bottom;
            if (y < k.SockTop) return k.Socks;
            return k.Skin;
        }

        static Color TorsoColor(SdLook k, float u, float y)
        {
            if (y < 0.4f) return k.Pants || k.Skirt ? k.Bottom : k.Top;
            // the front strip (u ≈ 0.25) shows the shirt under an open jacket; the V at the neck
            var front = Mathf.Abs(u - 0.25f);
            // ring u: 0 = the character's left, 0.25 = front
            if (k.Jacket && front < Mathf.Lerp(0.02f, 0.07f, Mathf.InverseLerp(0.47f, 0.6f, y)) && y > 0.43f) return k.Shirt;
            return k.Top;
        }

        // ---------------------------------------------------------------- head --
        static void BuildHead(SB sb, SdLook k)
        {
            const int NS = 28, NT = 20;
            var start = sb.V.Count;
            for (var i = 0; i <= NS; i++)
            {
                var phi = i / (float)NS * Mathf.PI * 2f;       // 0 = +x, 90° = +z (face)
                for (var j = 0; j <= NT; j++)
                {
                    var th = j / (float)NT * Mathf.PI;          // 0 top .. PI bottom
                    var unit = new Vector3(Mathf.Sin(th) * Mathf.Cos(phi), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(phi));
                    var p = Vector3.Scale(unit, HeadR);
                    // anime head: the lower front narrows to a small chin and comes forward a touch
                    var down = Mathf.Clamp01(-unit.y);
                    var frontness = Mathf.Clamp01(unit.z * 1.2f + 0.2f);
                    p.x *= Mathf.Lerp(1f, 0.62f, Mathf.Pow(down, 1.6f) * frontness);
                    p.z *= Mathf.Lerp(1f, 0.85f, Mathf.Pow(down, 1.4f));
                    p.y *= Mathf.Lerp(1f, 1.05f, Mathf.Pow(down, 2f) * frontness);
                    // the back of the skull is fuller
                    if (unit.z < 0f) p.z *= 1f + 0.08f * (-unit.z);
                    var pos = HeadC + p;
                    var n = new Vector3(unit.x / HeadR.x, unit.y / HeadR.y, unit.z / HeadR.z);
                    // uv: (phi, theta) across the head's own atlas rect; the face is painted into it
                    var uv = new Vector2(_headRect.xMin + (i / (float)NS) * _headRect.width, _headRect.yMin + (1f - j / (float)NT) * _headRect.height);
                    sb.Add(pos, n, uv, k.Skin, W1(B.Head));
                }
            }
            var stride = NT + 1;
            for (var i = 0; i < NS; i++)
                for (var j = 0; j < NT; j++)
                {
                    var a = start + i * stride + j;
                    var b = a + stride;
                    sb.Quad(a, b, b + 1, a + 1);
                }
            // ears
            for (var s = -1; s <= 1; s += 2)
            {
                var c = HeadC + new Vector3(HeadR.x * 0.96f * s, -0.035f, -0.01f);
                var ring = new List<Ring> { Along(c - new Vector3(0.01f * s, 0, 0), Vector3.right * s, 0.012f, 0.02f), Along(c + new Vector3(0.012f * s, 0, 0), Vector3.right * s, 0.008f, 0.016f) };
                Loft(sb, ring, 10, (i, t) => W1(B.Head), (u, t) => k.Skin, Rect.zero, false, true);
            }
        }

        /// <summary>
        /// The face plate. The reference does not paint eyes onto the skull: EyeMouth is a thin
        /// separate shell a hair in front of the face carrying the front-view drawing, so the
        /// features stay crisp from every angle. Same here: a curved patch hugging the head over
        /// FaceBox, lifted 0.006, its uv a plain rectangle in FaceRect, flagged in uv2 so the
        /// projection bake leaves it alone.
        /// </summary>
        static void BuildFacePlate(SB sb, SdLook k)
        {
            const int NX = 16, NY = 16;
            var start = sb.V.Count;
            for (var j = 0; j <= NY; j++)
                for (var i = 0; i <= NX; i++)
                {
                    var u = i / (float)NX; var v = j / (float)NY;
                    var x = Mathf.Lerp(FaceBox.xMin, FaceBox.xMax, u);
                    var y = Mathf.Lerp(FaceBox.yMin, FaceBox.yMax, v);
                    var dx = (x - HeadC.x) / HeadR.x; var dy = (y - HeadC.y) / HeadR.y;
                    var inside = Mathf.Max(0f, 1f - dx * dx - dy * dy);
                    var down = Mathf.Clamp01(-dy);
                    var z = HeadC.z + HeadR.z * Mathf.Sqrt(inside) * Mathf.Lerp(1f, 0.85f, Mathf.Pow(down, 1.4f));
                    var pos = new Vector3(x * Mathf.Lerp(1f, 0.62f, Mathf.Pow(down, 1.6f)), y, z + 0.006f);
                    var n = new Vector3(dx / HeadR.x, dy / HeadR.y, Mathf.Sqrt(inside) / HeadR.z).normalized;
                    sb.Add(pos, n, new Vector2(FaceRect.xMin + u * FaceRect.width, FaceRect.yMin + v * FaceRect.height), Color.white, W1(B.Head));
                    sb.Flag(1f);
                }
            var stride = NX + 1;
            for (var j = 0; j < NY; j++)
                for (var i = 0; i < NX; i++)
                {
                    var a = start + j * stride + i; var b = a + stride;
                    sb.Quad(a, a + 1, b + 1, b);
                }
        }

        // ---------------------------------------------------------------- hair --
        /// <summary>
        /// Where the hair ends at angle phi (0 = the character's left, 90° = the forehead), as a
        /// height: brows at the front, the jaw or lower at the sides, the style's length behind.
        /// </summary>
        static float HairEnd(SdLook k, float phi)
        {
            var front = Mathf.Max(0f, Mathf.Sin(phi));          // 1 at the forehead
            var back = Mathf.Max(0f, -Mathf.Sin(phi));          // 1 at the nape
            var side = 1f - Mathf.Abs(Mathf.Sin(phi));          // 1 over the ears
            float backY = k.Style switch
            {
                "long" => 0.36f, "bob" => 0.6f, "curly" => 0.52f, "side" => 0.5f,
                "ponytail" or "bun" or "twin" => 0.66f, "spiky" => 0.68f, _ => k.Male ? 0.66f : 0.6f,
            };
            float sideY = k.Style switch { "long" => 0.5f, "bob" => 0.64f, "curly" => 0.6f, "side" => 0.58f, _ => k.Male ? 0.72f : 0.66f };
            const float browY = 0.79f;
            // measured: the brows sit at 0.77-0.79 and the eyes 0.63-0.75. The fringe ends at the
            // brows, the temples at the eye line, the sides at the ear, the back by style.
            var wf = Mathf.Pow(front, 2.5f); var ws = Mathf.Pow(side, 1.2f) * (1f - wf); var wb = Mathf.Pow(back, 1.2f);
            var y = browY * wf + sideY * ws + backY * wb;
            var wsum = wf + ws + wb;
            return y / Mathf.Max(1e-3f, wsum);
        }

        /// <summary>Pointed strand tips cut into the lower edge: a saw of spikes, 18 round the head.</summary>
        static float Teeth(float phi, SdLook k)
        {
            var n = k.Style == "spiky" ? 12f : 18f;
            var saw = Mathf.Abs(Mathf.Repeat(phi / (Mathf.PI * 2f) * n, 1f) - 0.5f) * 2f;   // 1 at a tip, 0 between
            var amp = Mathf.Sin(phi) > 0.2f ? 0.012f : 0.045f;                                 // barely in the fringe
            return Mathf.Pow(saw, 1.8f) * amp;
        }

        static void BuildHair(SB sb, SdLook k)
        {
            const int NP = 72, NS = 22;      // around, down
            const float lift = 1.16f, thick = 0.03f;
            var outer = new Vector3[NP + 1, NS + 1];
            var tvals = new float[NP + 1, NS + 1];
            for (var i = 0; i <= NP; i++)
            {
                var phi = i / (float)NP * Mathf.PI * 2f;
                var end = HairEnd(k, phi) - Teeth(phi, k);
                // path: over the skull from the crown, then hanging straight down to `end`
                var path = new List<Vector3>();
                for (var t = 0f; t <= Mathf.PI * 0.62f; t += 0.04f)
                {
                    var unit = new Vector3(Mathf.Sin(t) * Mathf.Cos(phi), Mathf.Cos(t), Mathf.Sin(t) * Mathf.Sin(phi));
                    var p = HeadC + Vector3.Scale(unit, HeadR) * lift;
                    if (p.y < end) break;
                    path.Add(p);
                }
                var last = path[path.Count - 1];
                var outward = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                var front = Mathf.Max(0f, Mathf.Sin(phi));
                while (last.y > end + 0.004f && front < 0.45f)
                {
                    // falling hair flares out a touch and swings a little behind
                    last += Vector3.down * 0.02f + outward * 0.0045f + Vector3.back * 0.001f;
                    path.Add(last);
                }
                // resample to NS+1 points by arc length
                var len = new float[path.Count];
                for (var q = 1; q < path.Count; q++) len[q] = len[q - 1] + Vector3.Distance(path[q], path[q - 1]);
                for (var j = 0; j <= NS; j++)
                {
                    var target = len[len.Length - 1] * j / NS;
                    var q = 1;
                    while (q < len.Length - 1 && len[q] < target) q++;
                    var a = Mathf.InverseLerp(len[q - 1], len[q], target);
                    outer[i, j] = Vector3.Lerp(path[q - 1], path[q], a);
                    tvals[i, j] = j / (float)NS;
                }
            }
            B Bone(float t, float phi) => Mathf.Sin(phi) < -0.2f && t > 0.55f ? B.HairBack : B.Head;
            // outer layer
            var hairRect = _pack.Take(HeadR.x * 2.2f * Mathf.PI, 0.55f);
            var hairInRect = _pack.Take(HeadR.x * 2.2f * Mathf.PI, 0.55f);
            var o0 = sb.V.Count;
            for (var i = 0; i <= NP; i++)
                for (var j = 0; j <= NS; j++)
                {
                    var phi = i / (float)NP * Mathf.PI * 2f;
                    var p = outer[i, j];
                    var n = (p - (HeadC + Vector3.down * 0.05f)); n.y *= 0.6f;
                    var t = tvals[i, j];
                    var col = Color.Lerp(k.Hair, k.HairTip, t * t);
                    sb.Add(p, n, new Vector2(hairRect.xMin + i / (float)NP * hairRect.width, hairRect.yMin + (1f - t) * hairRect.height), col, W2(B.Head, Bone(t, phi), Mathf.Clamp01((t - 0.5f) * 2f)));
                    sb.Flag(2f);
                }
            // inner layer, pulled in towards the head axis, facing inward
            var i0 = sb.V.Count;
            for (var i = 0; i <= NP; i++)
                for (var j = 0; j <= NS; j++)
                {
                    var phi = i / (float)NP * Mathf.PI * 2f;
                    var p = outer[i, j];
                    var towards = new Vector3(HeadC.x - p.x, 0f, HeadC.z - p.z).normalized;
                    var t = tvals[i, j];
                    var q = p + towards * thick * (0.55f + 0.45f * (1f - t)) + Vector3.down * 0.002f;
                    var n = -(p - (HeadC + Vector3.down * 0.05f));
                    sb.Add(q, n, new Vector2(hairInRect.xMin + i / (float)NP * hairInRect.width, hairInRect.yMin + (1f - t) * hairInRect.height), MeshKit.Shade(k.Hair, 0.72f), W2(B.Head, Bone(t, phi), Mathf.Clamp01((t - 0.5f) * 2f)));
                    sb.Flag(2f);
                }
            var stride = NS + 1;
            for (var i = 0; i < NP; i++)
                for (var j = 0; j < NS; j++)
                {
                    var a = o0 + i * stride + j; var b = a + stride;
                    sb.Quad(a, a + 1, b + 1, b);                   // outer faces out
                    var c = i0 + i * stride + j; var d = c + stride;
                    sb.Quad(c, d, d + 1, c + 1);                   // inner faces in
                }
            // the rim joining the two layers along the tips
            for (var i = 0; i < NP; i++)
            {
                var a = o0 + i * stride + NS; var b = o0 + (i + 1) * stride + NS;
                var c = i0 + i * stride + NS; var d = i0 + (i + 1) * stride + NS;
                sb.Quad(a, b, d, c);
            }

            // tails
            if (k.Style == "ponytail")
                Tail(sb, k, HeadC + new Vector3(0f, 0.08f, -0.16f), new Vector3(0f, -1f, -0.35f), 0.36f, 0.06f);
            if (k.Style == "twin")
                for (var s = -1; s <= 1; s += 2)
                    Tail(sb, k, HeadC + new Vector3(0.14f * s, 0.06f, -0.06f), new Vector3(0.35f * s, -1f, -0.1f), 0.34f, 0.05f);
            if (k.Style == "bun")
            {
                var c = HeadC + new Vector3(0f, 0.16f, -0.1f);
                var rs = new List<Ring>();
                for (var q = 0; q <= 8; q++) { var a = q / 8f * Mathf.PI; rs.Add(Flat(c + Vector3.up * (-Mathf.Cos(a) * 0.06f), Mathf.Sin(a) * 0.066f + 0.002f, Mathf.Sin(a) * 0.066f + 0.002f)); }
                Loft(sb, rs, 16, (q, t) => W1(B.Head), (u, t) => k.Hair, Rect.zero);
            }
            if (k.Ahoge)
            {
                var rings = new List<Ring>();
                var p = HeadC + new Vector3(0f, HeadR.y * lift, 0.01f);
                var d = new Vector3(0f, 1f, 0.5f).normalized;
                for (var q = 0; q <= 6; q++)
                {
                    var t = q / 6f;
                    rings.Add(Along(p, d, 0.014f * (1f - t * 0.9f), 0.005f));
                    d = Vector3.Slerp(d, new Vector3(0f, -0.6f, 1f).normalized, 0.25f).normalized;
                    p += d * 0.014f;
                }
                Loft(sb, rings, 6, (q, t) => W1(B.Head), (u, t) => k.Hair, Rect.zero, false, true);
            }
        }

        /// <summary>A bundle of hair from an anchor, thick at the tie, tapering, swaying on HairBack.</summary>
        static void Tail(SB sb, SdLook k, Vector3 anchor, Vector3 dir, float len, float r0)
        {
            var rings = new List<Ring>();
            var p = anchor;
            var d = dir.normalized;
            for (var i = 0; i <= 10; i++)
            {
                var t = i / 10f;
                var r = r0 * (t < 0.15f ? Mathf.Lerp(0.7f, 1f, t / 0.15f) : 1f - Mathf.Pow((t - 0.15f) / 0.85f, 1.5f) * 0.9f);
                rings.Add(Along(p, d, r, r * 0.8f));
                d = Vector3.Slerp(d, Vector3.down, 0.15f).normalized;
                p += d * (len / 10f);
            }
            Loft(sb, rings, 12, (i, t) => W2(B.Head, B.HairBack, Mathf.Clamp01(t * 1.5f)), (u, t) => Color.Lerp(k.Hair, k.HairTip, t), Rect.zero, true, true);
            // the hair tie
            var tie = new List<Ring> { Along(anchor - dir.normalized * 0.01f, dir, r0 * 0.8f, r0 * 0.7f), Along(anchor + dir.normalized * 0.015f, dir, r0 * 0.8f, r0 * 0.7f) };
            Loft(sb, tie, 10, (i, t) => W1(B.Head), (u, t) => k.Accent, Rect.zero, true, true);
        }

        // ---------------------------------------------------------------- outfit --
        static void BuildOutfit(SB sb, SdLook k)
        {
            // skirt: flared, pleated
            if (k.Skirt || k.Dress)
            {
                var rings = new List<Ring>();
                var len = k.Dress ? 0.2f : 0.13f;
                for (var i = 0; i <= 6; i++)
                {
                    var t = i / 6f;
                    var y = Mathf.Lerp(0.43f, 0.43f - len, t);
                    var rx = Mathf.Lerp(0.09f, 0.155f + len * 0.2f, Mathf.Pow(t, 0.8f));
                    var rz = Mathf.Lerp(0.07f, 0.12f + len * 0.2f, Mathf.Pow(t, 0.8f));
                    var pleat = 0.05f * t;
                    rings.Add(Flat(new Vector3(0f, y, 0.004f), rx, rz, a => 1f + pleat * Mathf.Sin(a * 14f)));
                }
                Loft(sb, rings, 42, (i, t) => W2(B.Hips, B.Spine, 0.2f), (u, t) => Color.Lerp(k.Bottom, k.Bottom * 0.86f, t * 0.6f), Rect.zero);
                if (k.SkirtHem.a > 0f)
                {
                    var hem = new List<Ring> { rings[5], rings[6] };
                    Loft(sb, hem, 42, (i, t) => W1(B.Hips), (u, t) => k.SkirtHem, Rect.zero);
                }
            }
            // jacket / coat: a shell over the torso, open at the front, with a hem
            if (k.Jacket)
            {
                var rings = new List<Ring>();
                float[] ys = { k.Coat ? 0.24f : 0.35f, 0.40f, 0.45f, 0.5f, 0.55f, 0.6f };
                float[] rx = { k.Coat ? 0.125f : 0.1f, 0.09f, 0.082f, 0.086f, 0.094f, 0.08f };
                float[] rz = { k.Coat ? 0.095f : 0.076f, 0.068f, 0.064f, 0.068f, 0.07f, 0.058f };
                for (var i = 0; i < ys.Length; i++) rings.Add(Flat(new Vector3(0f, ys[i], 0f), rx[i], rz[i]));
                Loft(sb, rings, 24,
                     (i, t) => ys[i] < 0.42f ? W2(B.Hips, B.Spine, 0.4f) : W2(B.Spine, B.Chest, Mathf.InverseLerp(0.45f, 0.51f, ys[i])),
                     (u, t) => k.Top, Rect.zero, false, false, 0.21f, 0.29f);
                // lapels: two thin dark strips either side of the opening
                for (var s = -1; s <= 1; s += 2)
                {
                    var lap = new List<Ring>();
                    for (var i = 0; i <= 4; i++)
                    {
                        var y = Mathf.Lerp(0.46f, 0.575f, i / 4f);
                        lap.Add(Along(new Vector3(0.02f * s * (1f + i * 0.15f), y, 0.066f), Vector3.up, 0.009f, 0.004f));
                    }
                    Loft(sb, lap, 6, (i, t) => W1(B.Chest), (u, t) => k.Lapel, Rect.zero, true, true);
                }
            }
            // collar: two flaps at the throat
            if (k.Collar)
                for (var s = -1; s <= 1; s += 2)
                {
                    var c = new Vector3(0.024f * s, 0.588f, 0.03f);
                    var fl = new List<Ring> { Along(c, new Vector3(0.6f * s, -0.4f, 0.5f), 0.018f, 0.004f), Along(c + new Vector3(0.022f * s, -0.018f, 0.012f), new Vector3(0.6f * s, -0.4f, 0.5f), 0.012f, 0.003f) };
                    Loft(sb, fl, 8, (i, t) => W1(B.Chest), (u, t) => k.CollarColor, Rect.zero, true, true);
                }
            // tie / ribbon
            if (k.Tie.a > 0f)
            {
                var r = new List<Ring>();
                for (var i = 0; i <= 5; i++) { var t = i / 5f; r.Add(Flat(new Vector3(0f, Mathf.Lerp(0.578f, 0.47f, t), 0.064f + t * 0.004f), Mathf.Lerp(0.009f, 0.016f, t) * (t > 0.85f ? 0.5f : 1f), 0.004f)); }
                Loft(sb, r, 8, (i, t) => W1(B.Chest), (u, t) => k.Tie, Rect.zero, true, true);
            }
        }

        // ---------------------------------------------------------------- texture --
        static readonly Dictionary<string, Texture2D> Atlases = new();

        /// <summary>A baked texture (Resources/Art/SDTex/&lt;id&gt;, from tools/gen_sdtex_gemini.py + SdTexBake), if any.</summary>
        public static Texture2D Baked(string id) => Resources.Load<Texture2D>("Art/SDTex/" + id);

        static Texture2D Atlas(SdLook k)
        {
            var baked = Baked(k.Id);
            if (baked != null) return baked;
            if (Atlases.TryGetValue(k.Id, out var t) && t != null) return t;
            const int N = 1024;
            var px = new Color[N * N];
            for (var i = 0; i < px.Length; i++) px[i] = Color.white;
            // the procedural face, into the head rect: texel → (phi, theta) → point on the head →
            // front-planar face coordinates → FaceTexture, divided out of the skin tint
            var face = FaceTexture.For(new FaceTexture.Look { Eye = k.Eye, Hair = k.Hair, Male = k.Male, Glasses = k.Glasses, Sunglasses = k.Sunglasses });
            var fp = face.GetPixels();
            var fs = face.width;
            // the face plate's rect: transparent except where the drawing is (the toon shader
            // clips at alpha 0.5); FaceTexture covers the whole front of the head, the plate is
            // its middle band
            var r = FaceRect;
            int x0 = (int)(r.xMin * N), x1 = (int)(r.xMax * N), y0 = (int)(r.yMin * N), y1 = (int)(r.yMax * N);
            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                {
                    var u = (x - x0) / (float)(x1 - x0); var v = (y - y0) / (float)(y1 - y0);
                    var su = 0.5f + (u - 0.5f) * 0.86f; var sv = 0.235f + v * 0.62f;
                    var f = fp[Mathf.Clamp((int)(sv * fs), 0, fs - 1) * fs + Mathf.Clamp((int)(su * fs), 0, fs - 1)];
                    px[y * N + x] = new Color(f.r, f.g, f.b, f.a);
                }
            t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "sdb:" + k.Id, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels(px);
            t.Apply(true);
            Atlases[k.Id] = t;
            return t;
        }
    }
}
