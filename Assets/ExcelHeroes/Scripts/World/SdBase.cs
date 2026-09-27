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
            [B.Neck] = new(0f, 0.585f, 0f), [B.Head] = new(0f, 0.62f, 0f),
            [B.UpperArmL] = new(0.074f, 0.572f, 0f), [B.ForearmL] = new(0.156f, 0.51f, 0f), [B.HandL] = new(0.247f, 0.439f, 0.009f),
            [B.UpperArmR] = new(-0.074f, 0.572f, 0f), [B.ForearmR] = new(-0.156f, 0.51f, 0f), [B.HandR] = new(-0.247f, 0.439f, 0.009f),
            [B.ThighL] = new(0.052f, 0.345f, 0f), [B.CalfL] = new(0.062f, 0.193f, 0.004f), [B.FootL] = new(0.068f, 0.047f, 0f),
            [B.ThighR] = new(-0.052f, 0.345f, 0f), [B.CalfR] = new(-0.062f, 0.193f, 0.004f), [B.FootR] = new(-0.068f, 0.047f, 0f),
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
        static readonly Vector3 HeadC = new(0f, 0.765f, -0.03f);
        static readonly Vector3 HeadR = new(0.158f, 0.17f, 0.152f);

        // ---------------------------------------------------------------- builder --
        class SB
        {
            public readonly List<Vector3> V = new(), N = new();
            public readonly List<Vector2> UV = new();
            public readonly List<Color> C = new();
            public readonly List<BoneWeight> W = new();
            public readonly List<int> T = new();

            public int Add(Vector3 p, Vector3 n, Vector2 uv, Color c, BoneWeight w)
            {
                V.Add(p); N.Add(n.normalized); UV.Add(uv); C.Add(c); W.Add(w); return V.Count - 1;
            }

            public void Quad(int a, int b, int c, int d) { T.Add(a); T.Add(b); T.Add(c); T.Add(a); T.Add(c); T.Add(d); }
        }

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
                sb.Add(r.C + r.R * (Mathf.Cos(a) * r.Rx) + r.F * (Mathf.Sin(a) * r.Rz), axis, uvRect.center, c, w);
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
        static readonly Rect FaceUV = new(0f, 0.5f, 0.5f, 0.5f);
        static readonly Rect WhiteUV = new(0.76f, 0.76f, 0.001f, 0.001f);
        static readonly Rect TorsoUV = new(0.5f, 0.5f, 0.25f, 0.5f);

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
            BuildBody(sb, look);
            BuildHead(sb, look);
            BuildHair(sb, look);
            BuildOutfit(sb, look);

            FixWinding(sb);
            var mesh = new Mesh { name = "sdb:" + heroId, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var verts = new List<Vector3>(sb.V.Count);
            foreach (var v in sb.V) verts.Add(v * Height);
            var lin = new List<Color>(sb.C.Count);
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            foreach (var c in sb.C) lin.Add(linear ? new Color(c.linear.r, c.linear.g, c.linear.b, 1f) : c);
            mesh.SetVertices(verts); mesh.SetNormals(sb.N); mesh.SetUVs(0, sb.UV); mesh.SetColors(lin); mesh.SetTriangles(sb.T, 0);
            mesh.boneWeights = sb.W.ToArray();
            var bind = new Matrix4x4[BoneCount];
            for (var i = 0; i < BoneCount; i++) bind[i] = bones[i].worldToLocalMatrix * root.localToWorldMatrix;
            mesh.bindposes = bind;
            mesh.RecalculateBounds();

            var go = new GameObject("mesh") { layer = layer };
            go.transform.SetParent(root, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bones[(int)B.Hips];
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mat = MeshKit.NewToon(0.0045f, Atlas(look));
            mat.SetFloat("_ShadeStrength", 0.28f);
            mat.SetFloat("_Rim", 0.14f);
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

        static void BuildBody(SB sb, SdLook k)
        {
            var skin = k.Skin;
            // legs: thigh → knee → calf → ankle, both sides; legwear colour by height
            for (var s = -1; s <= 1; s += 2)
            {
                var side = s > 0;
                var rings = new List<Ring>();
                float[] ys = { 0.36f, 0.31f, 0.26f, 0.215f, 0.193f, 0.16f, 0.11f, 0.07f, 0.05f };
                float[] rs = { 0.05f, 0.046f, 0.04f, 0.034f, 0.032f, 0.033f, 0.028f, 0.022f, 0.021f };
                for (var i = 0; i < ys.Length; i++)
                {
                    var x = ys[i] > 0.193f ? Mathf.Lerp(0.062f, 0.05f, Mathf.InverseLerp(0.193f, 0.36f, ys[i])) : Mathf.Lerp(0.068f, 0.062f, Mathf.InverseLerp(0.047f, 0.193f, ys[i]));
                    rings.Add(Flat(new Vector3(x * s, ys[i], ys[i] < 0.21f ? 0.004f : 0f), rs[i], rs[i] * 0.95f));
                }
                B thigh = side ? B.ThighL : B.ThighR, calf = side ? B.CalfL : B.CalfR, foot = side ? B.FootL : B.FootR;
                Loft(sb, rings, 14,
                    (i, t) => ys[i] > 0.2f ? W2(thigh, calf, Mathf.InverseLerp(0.23f, 0.17f, ys[i])) : W2(calf, foot, Mathf.InverseLerp(0.07f, 0.045f, ys[i])),
                    (u, t) => LegColor(k, Mathf.Lerp(ys[0], ys[ys.Length - 1], t)), WhiteUV);
                // shoe: a rounded toe box
                var shoe = new List<Ring>();
                for (var i = 0; i <= 6; i++)
                {
                    var t = i / 6f;
                    var z = Mathf.Lerp(-0.022f, 0.05f, t);
                    var w = 0.022f * Mathf.Sin(Mathf.Lerp(0.35f, 3.0f, t)) + 0.004f;
                    var h = 0.022f * Mathf.Sin(Mathf.Lerp(0.3f, 2.9f, t)) + 0.004f;
                    shoe.Add(new Ring { C = new Vector3(0.068f * s, 0.024f, z), R = Vector3.right, F = Vector3.up, Rx = w * 1.15f, Rz = h * 1.05f });
                }
                Loft(sb, shoe, 14, (i, t) => W1(foot), (u, t) => k.Shoes, WhiteUV, true, true);
            }

            // torso: hips → waist → chest → shoulders, a little flat front to back
            var tr = new List<Ring>();
            float[] ty = { 0.32f, 0.356f, 0.40f, 0.44f, 0.48f, 0.52f, 0.55f, 0.575f, 0.59f };
            float[] tx = { 0.082f, 0.088f, 0.078f, 0.07f, 0.074f, 0.08f, 0.082f, 0.07f, 0.034f };
            float[] tz = { 0.062f, 0.064f, 0.058f, 0.054f, 0.058f, 0.062f, 0.058f, 0.05f, 0.03f };
            for (var i = 0; i < ty.Length; i++) tr.Add(Flat(new Vector3(0f, ty[i], 0f), tx[i], tz[i]));
            Loft(sb, tr, 20,
                (i, t) => ty[i] < 0.42f ? W2(B.Hips, B.Spine, Mathf.InverseLerp(0.36f, 0.42f, ty[i]))
                        : ty[i] < 0.51f ? W2(B.Spine, B.Chest, Mathf.InverseLerp(0.45f, 0.51f, ty[i]))
                        : W2(B.Chest, B.Neck, Mathf.InverseLerp(0.57f, 0.6f, ty[i])),
                (u, t) => TorsoColor(k, u, Mathf.Lerp(ty[0], ty[ty.Length - 1], t)), TorsoUV, true, false);
            // neck
            var nk = new List<Ring> { Flat(new Vector3(0f, 0.57f, 0f), 0.032f, 0.03f), Flat(new Vector3(0f, 0.65f, -0.01f), 0.03f, 0.028f) };
            Loft(sb, nk, 12, (i, t) => W2(B.Neck, B.Head, t), (u, t) => skin, WhiteUV);

            // arms, A-pose
            for (var s = -1; s <= 1; s += 2)
            {
                var side = s > 0;
                var sh = new Vector3(0.07f * s, 0.568f, 0f);
                var el = new Vector3(0.156f * s, 0.51f, 0f);
                var wr = new Vector3(0.24f * s, 0.444f, 0.009f);
                var up = new List<Ring>();
                for (var i = 0; i <= 6; i++)
                {
                    var t = i / 6f;
                    var p = t < 0.5f ? Vector3.Lerp(sh, el, t * 2f) : Vector3.Lerp(el, wr, (t - 0.5f) * 2f);
                    var dir = t < 0.5f ? el - sh : wr - el;
                    var r = Mathf.Lerp(0.034f, 0.024f, t);
                    up.Add(Along(p, dir, r, r * 0.95f));
                }
                B ua = side ? B.UpperArmL : B.UpperArmR, fa = side ? B.ForearmL : B.ForearmR, hd = side ? B.HandL : B.HandR;
                Loft(sb, up, 12, (i, t) => t < 0.5f ? W2(ua, fa, Mathf.InverseLerp(0.35f, 0.6f, t)) : W2(fa, hd, Mathf.InverseLerp(0.9f, 1f, t)),
                     (u, t) => t > (k.ShortSleeve ? 0.35f : 0.93f) ? skin : (t > 0.86f && k.Cuff.a > 0 ? k.Cuff : k.Sleeve), WhiteUV, true, false);
                // hand: a soft mitten
                var dirH = (wr - el).normalized;
                var hr = new List<Ring>();
                for (var i = 0; i <= 5; i++)
                {
                    var t = i / 5f;
                    var r = 0.026f * Mathf.Sin(Mathf.Lerp(0.5f, 3.0f, t)) + 0.004f;
                    hr.Add(Along(wr + dirH * (t * 0.055f), dirH, r * 1.1f, r * 0.72f));
                }
                Loft(sb, hr, 10, (i, t) => W1(hd), (u, t) => skin, WhiteUV, true, true);
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
            if (y < 0.405f) return k.Pants || k.Skirt ? k.Bottom : k.Top;
            // the front strip (u ≈ 0.25) shows the shirt under an open jacket; the V at the neck
            var front = Mathf.Abs(u - 0.25f);
            if (k.Jacket && front < Mathf.Lerp(0.02f, 0.065f, Mathf.InverseLerp(0.47f, 0.575f, y)) && y > 0.43f) return k.Shirt;
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
                    // face uv: planar from the front, only meaningful where z > 0
                    var fu = 0.5f + p.x / (HeadR.x * 2.1f);
                    var fv = 0.5f + (p.y + 0.02f) / (HeadR.y * 2.1f);
                    var uv = unit.z > -0.1f ? new Vector2(FaceUV.xMin + Mathf.Clamp01(fu) * FaceUV.width, FaceUV.yMin + Mathf.Clamp01(fv) * FaceUV.height) : WhiteUV.center;
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
                Loft(sb, ring, 10, (i, t) => W1(B.Head), (u, t) => k.Skin, WhiteUV, false, true);
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
            float sideY = k.Style switch { "long" => 0.42f, "bob" => 0.6f, "curly" => 0.55f, "side" => 0.52f, _ => k.Male ? 0.7f : 0.62f };
            const float browY = 0.785f;
            // the fringe sits at the brows, a little higher at the parting, longer at the temples
            var fringe = browY + 0.02f * Mathf.Pow(front, 8f);
            var y = fringe * Mathf.Pow(front, 1.5f) + sideY * side + backY * Mathf.Pow(back, 0.9f);
            var wsum = Mathf.Pow(front, 1.5f) + side + Mathf.Pow(back, 0.9f);
            return y / Mathf.Max(1e-3f, wsum);
        }

        /// <summary>Pointed strand tips cut into the lower edge: a saw of spikes, 18 round the head.</summary>
        static float Teeth(float phi, SdLook k)
        {
            var n = k.Style == "spiky" ? 12f : 18f;
            var saw = Mathf.Abs(Mathf.Repeat(phi / (Mathf.PI * 2f) * n, 1f) - 0.5f) * 2f;   // 1 at a tip, 0 between
            var amp = Mathf.Sin(phi) > 0.2f ? 0.03f : 0.05f;                                   // shorter spikes in the fringe
            return Mathf.Pow(saw, 1.8f) * amp;
        }

        static void BuildHair(SB sb, SdLook k)
        {
            const int NP = 72, NS = 22;      // around, down
            const float lift = 1.13f, thick = 0.024f;
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
                while (last.y > end + 0.004f && front < 0.85f)
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
            var o0 = sb.V.Count;
            for (var i = 0; i <= NP; i++)
                for (var j = 0; j <= NS; j++)
                {
                    var phi = i / (float)NP * Mathf.PI * 2f;
                    var p = outer[i, j];
                    var n = (p - (HeadC + Vector3.down * 0.05f)); n.y *= 0.6f;
                    var t = tvals[i, j];
                    var col = Color.Lerp(k.Hair, k.HairTip, t * t);
                    sb.Add(p, n, WhiteUV.center, col, W2(B.Head, Bone(t, phi), Mathf.Clamp01((t - 0.5f) * 2f)));
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
                    var q = p + towards * thick * (0.6f + 0.4f * (1f - t)) + Vector3.down * 0.002f;
                    var n = -(p - (HeadC + Vector3.down * 0.05f));
                    sb.Add(q, n, WhiteUV.center, MeshKit.Shade(k.Hair, 0.72f), W2(B.Head, Bone(t, phi), Mathf.Clamp01((t - 0.5f) * 2f)));
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
                Loft(sb, rs, 16, (q, t) => W1(B.Head), (u, t) => k.Hair, WhiteUV);
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
                Loft(sb, rings, 6, (q, t) => W1(B.Head), (u, t) => k.Hair, WhiteUV, false, true);
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
            Loft(sb, rings, 12, (i, t) => W2(B.Head, B.HairBack, Mathf.Clamp01(t * 1.5f)), (u, t) => Color.Lerp(k.Hair, k.HairTip, t), WhiteUV, true, true);
            // the hair tie
            var tie = new List<Ring> { Along(anchor - dir.normalized * 0.01f, dir, r0 * 0.8f, r0 * 0.7f), Along(anchor + dir.normalized * 0.015f, dir, r0 * 0.8f, r0 * 0.7f) };
            Loft(sb, tie, 10, (i, t) => W1(B.Head), (u, t) => k.Accent, WhiteUV, true, true);
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
                    var y = Mathf.Lerp(0.42f, 0.42f - len, t);
                    var rx = Mathf.Lerp(0.082f, 0.15f + len * 0.2f, Mathf.Pow(t, 0.8f));
                    var rz = Mathf.Lerp(0.062f, 0.115f + len * 0.2f, Mathf.Pow(t, 0.8f));
                    var pleat = 0.05f * t;
                    rings.Add(Flat(new Vector3(0f, y, 0.004f), rx, rz, a => 1f + pleat * Mathf.Sin(a * 14f)));
                }
                Loft(sb, rings, 42, (i, t) => W2(B.Hips, B.Spine, 0.2f), (u, t) => Color.Lerp(k.Bottom, k.Bottom * 0.86f, t * 0.6f), WhiteUV);
                if (k.SkirtHem.a > 0f)
                {
                    var hem = new List<Ring> { rings[5], rings[6] };
                    Loft(sb, hem, 42, (i, t) => W1(B.Hips), (u, t) => k.SkirtHem, WhiteUV);
                }
            }
            // jacket / coat: a shell over the torso, open at the front, with a hem
            if (k.Jacket)
            {
                var rings = new List<Ring>();
                float[] ys = { k.Coat ? 0.24f : 0.35f, 0.40f, 0.45f, 0.5f, 0.55f, 0.578f };
                float[] rx = { k.Coat ? 0.125f : 0.095f, 0.085f, 0.077f, 0.083f, 0.088f, 0.076f };
                float[] rz = { k.Coat ? 0.095f : 0.072f, 0.064f, 0.06f, 0.066f, 0.066f, 0.056f };
                for (var i = 0; i < ys.Length; i++) rings.Add(Flat(new Vector3(0f, ys[i], 0f), rx[i], rz[i]));
                Loft(sb, rings, 24,
                     (i, t) => ys[i] < 0.42f ? W2(B.Hips, B.Spine, 0.4f) : W2(B.Spine, B.Chest, Mathf.InverseLerp(0.45f, 0.51f, ys[i])),
                     (u, t) => k.Top, WhiteUV, false, false, 0.21f, 0.29f);
                // lapels: two thin dark strips either side of the opening
                for (var s = -1; s <= 1; s += 2)
                {
                    var lap = new List<Ring>();
                    for (var i = 0; i <= 4; i++)
                    {
                        var y = Mathf.Lerp(0.46f, 0.575f, i / 4f);
                        lap.Add(Along(new Vector3(0.02f * s * (1f + i * 0.15f), y, 0.066f), Vector3.up, 0.009f, 0.004f));
                    }
                    Loft(sb, lap, 6, (i, t) => W1(B.Chest), (u, t) => k.Lapel, WhiteUV, true, true);
                }
            }
            // collar: two flaps at the throat
            if (k.Collar)
                for (var s = -1; s <= 1; s += 2)
                {
                    var c = new Vector3(0.024f * s, 0.588f, 0.03f);
                    var fl = new List<Ring> { Along(c, new Vector3(0.6f * s, -0.4f, 0.5f), 0.018f, 0.004f), Along(c + new Vector3(0.022f * s, -0.018f, 0.012f), new Vector3(0.6f * s, -0.4f, 0.5f), 0.012f, 0.003f) };
                    Loft(sb, fl, 8, (i, t) => W1(B.Chest), (u, t) => k.CollarColor, WhiteUV, true, true);
                }
            // tie / ribbon
            if (k.Tie.a > 0f)
            {
                var r = new List<Ring>();
                for (var i = 0; i <= 5; i++) { var t = i / 5f; r.Add(Flat(new Vector3(0f, Mathf.Lerp(0.578f, 0.47f, t), 0.064f + t * 0.004f), Mathf.Lerp(0.009f, 0.016f, t) * (t > 0.85f ? 0.5f : 1f), 0.004f)); }
                Loft(sb, r, 8, (i, t) => W1(B.Chest), (u, t) => k.Tie, WhiteUV, true, true);
            }
        }

        // ---------------------------------------------------------------- texture --
        static readonly Dictionary<string, Texture2D> Atlases = new();

        static Texture2D Atlas(SdLook k)
        {
            if (Atlases.TryGetValue(k.Id, out var t) && t != null) return t;
            const int N = 512;
            var px = new Color[N * N];
            for (var i = 0; i < px.Length; i++) px[i] = Color.white;
            // the face: skin under FaceTexture's eyes/brows/mouth, composited into the atlas
            var face = FaceTexture.For(new FaceTexture.Look { Eye = k.Eye, Hair = k.Hair, Male = k.Male, Glasses = k.Glasses, Sunglasses = k.Sunglasses });
            var fp = face.GetPixels();
            var fs = face.width;
            for (var y = 0; y < N / 2; y++)
                for (var x = 0; x < N / 2; x++)
                {
                    // head uv: fu, fv in 0..1 across the front; the face texture covers the middle
                    var fu = x / (float)(N / 2); var fv = y / (float)(N / 2);
                    var su = (fu - 0.5f) * 1.02f + 0.5f; var sv = (fv - 0.5f) * 1.02f + 0.5f;
                    var c = Color.white;       // skin is the vertex colour; white keeps it
                    if (su >= 0f && su < 1f && sv >= 0f && sv < 1f)
                    {
                        var f = fp[Mathf.Clamp((int)(sv * fs), 0, fs - 1) * fs + Mathf.Clamp((int)(su * fs), 0, fs - 1)];
                        // over the skin: the face texture's colour where it has alpha, divided out of the skin tint
                        var s = k.Skin;
                        var tint = new Color(f.r / Mathf.Max(0.05f, s.r), f.g / Mathf.Max(0.05f, s.g), f.b / Mathf.Max(0.05f, s.b));
                        c = Color.Lerp(Color.white, new Color(Mathf.Min(1, tint.r), Mathf.Min(1, tint.g), Mathf.Min(1, tint.b)), f.a);
                    }
                    px[(y + N / 2) * N + x] = c;
                }
            t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "sdb:" + k.Id, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels(px);
            t.Apply(true);
            Atlases[k.Id] = t;
            return t;
        }
    }
}
