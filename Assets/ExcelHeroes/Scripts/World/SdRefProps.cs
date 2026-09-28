using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The pieces added ON the sample rig per character — built in metres in the wrapper's frame,
    /// then parented to a bone with the bone's scale cancelled (the scalp and the glasses do the
    /// same): a skirt on the pelvis for skirt outfits, and a hand prop by role, the office
    /// counterpart of the reference's guns — a rolled document, a tablet, a coffee cup, a clipboard.
    /// The hand bone's X runs along the fingers (Bip001), so props are built along +X.
    /// </summary>
    public static class SdRefProps
    {
        /// <summary>Parents a metre-built mesh to a bone at a world position, cancelling the bone's scale.</summary>
        static GameObject Attach(string name, Transform bone, Mesh mesh, Material mat, int layer, Vector3 worldPos, Quaternion worldRot)
        {
            var go = MeshKit.Part(name, bone, mesh, mat, layer);
            var ls = bone.lossyScale;
            go.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
            go.transform.position = worldPos;
            go.transform.rotation = worldRot;
            return go;
        }

        /// <summary>
        /// A flared skirt from the hips (y 0.43 of a 1.2 figure) to above the knee, pleated by
        /// alternating shade, with the outfit's hem stripe when it has one. Sits on the pelvis so
        /// it turns and leans with the body.
        /// </summary>
        public static void Skirt(ChibiRig rig, Transform root, SdLook k, int layer)
        {
            if (rig.Pelvis == null) return;
            var b = new MeshKit.Builder();
            float top = 0.44f, bottom = 0.27f, r0 = 0.125f, r1 = 0.2f;
            var col = k.Bottom; col.a = 1f;
            var dark = MeshKit.Shade(col, 0.86f);
            const int seg = 20;
            // Office wear gets a PENCIL skirt: straight from the hips to just above the knee, a
            // touch narrower at the hem, no pleats — flared and pleated read as a school uniform
            // (the Gemini outfit mock-ups), which these adults are not. Casual outfits keep the pleats.
            if (k.Outfit is "suit" or "shirt" or "vest" or "coat" or "labcoat" or "dress")
            {
                const float pb = 0.25f, hip = 0.138f, hem = 0.128f;
                b.Grid(seg, 4, (s, t) =>
                {
                    var a = s * Mathf.PI * 2f;
                    // hips round out a little below the waist, then the line runs straight and tapers
                    var r = t < 0.25f ? Mathf.Lerp(r0, hip, Mathf.SmoothStep(0f, 1f, t / 0.25f)) : Mathf.Lerp(hip, hem, (t - 0.25f) / 0.75f);
                    var p = new Vector3(Mathf.Cos(a) * r, Mathf.Lerp(top, pb, t), Mathf.Sin(a) * r * 0.85f);
                    return (p, new Vector3(Mathf.Cos(a), 0.05f, Mathf.Sin(a)).normalized, new Vector2(s, t));
                }, col);
                b.Frustum(new Vector3(0f, top - 0.022f, 0f), r0 * 1.02f, 0.022f, r0 * 1.02f, dark, 0.85f, seg, false);   // the waistband
                if (k.SkirtHem.a > 0f) b.Frustum(new Vector3(0f, pb - 0.002f, 0f), hem * 1.01f, 0.01f, hem * 1.01f, k.SkirtHem, 0.85f, seg, false);
                b.Disc(new Vector3(0f, pb, 0f), hem, hem * 0.85f, dark, false, seg);
                bottom = pb; r1 = hem;
                goto skinned;
            }
            // pleats: a lofted band whose colour alternates per segment
            b.Grid(seg, 1, (s, t) =>
            {
                var a = s * Mathf.PI * 2f;
                var r = Mathf.Lerp(r0, r1, t) * (1f + 0.035f * Mathf.Sin(a * 10f));
                var y = Mathf.Lerp(top, bottom, t);
                var p = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r * 0.85f);
                var n = new Vector3(Mathf.Cos(a), (r1 - r0) / (top - bottom), Mathf.Sin(a)).normalized;
                return (p, n, new Vector2(s, t));
            }, col);
            // the pleats: every other segment a darker, slightly proud strip, lofted with outward normals
            for (var i = 0; i < seg; i += 2)
            {
                var i0 = i; var i1 = i + 1;
                b.Grid(1, 1, (s, t) =>
                {
                    var a = Mathf.Lerp(i0, i1, s) / seg * Mathf.PI * 2f;
                    var r = Mathf.Lerp(r0, r1, t) * (1.004f + 0.035f * Mathf.Sin(a * 10f));
                    var y = Mathf.Lerp(top, bottom, t);
                    var p = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r * 0.85f);
                    var n = new Vector3(Mathf.Cos(a), (r1 - r0) / (top - bottom), Mathf.Sin(a)).normalized;
                    return (p, n, new Vector2(s, t));
                }, dark);
            }
            if (k.SkirtHem.a > 0f) b.Frustum(new Vector3(0f, bottom - 0.004f, 0f), r1 * 1.01f, 0.012f, r1 * 0.99f, k.SkirtHem, 0.85f, seg, false);
            b.Disc(new Vector3(0f, bottom, 0f), r1, r1 * 0.85f, dark, false, seg);       // the underside, so it is never hollow from below
            skinned:
            var mat = MeshKit.NewToon(0.007f);
            mat.SetFloat("_ShadeStrength", 0.3f);
            // skinned, not rigid: the hem follows the thighs (85 % at the bottom, by side), so a
            // walking or kicking leg carries its side of the skirt instead of poking through it
            var mesh = b.Bake("skirt");
            var go = new GameObject("skirt") { layer = layer };
            go.transform.SetParent(rig.Pelvis, false);
            var ls = rig.Pelvis.lossyScale;
            go.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
            go.transform.position = root.position; go.transform.rotation = root.rotation;
            var bones = new[] { rig.Pelvis, rig.LegL ?? rig.Pelvis, rig.LegR ?? rig.Pelvis };
            var verts = mesh.vertices; var bw = new BoneWeight[verts.Length];
            for (var i = 0; i < verts.Length; i++)
            {
                var t = Mathf.Clamp01((top - verts[i].y) / (top - bottom));
                var sR = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.06f, 0.06f, verts[i].x));   // the character's right is +x
                var leg = t * 0.85f;
                bw[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f - leg, boneIndex1 = 1, weight1 = leg * (1f - sR), boneIndex2 = 2, weight2 = leg * sR };
            }
            mesh.boneWeights = bw;
            var bind = new Matrix4x4[bones.Length];
            for (var i = 0; i < bones.Length; i++) bind[i] = bones[i].worldToLocalMatrix * go.transform.localToWorldMatrix;
            mesh.bindposes = bind;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh; smr.bones = bones; smr.rootBone = rig.Pelvis; smr.sharedMaterial = mat;
            smr.updateWhenOffscreen = true; smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; smr.receiveShadows = false;
            rig.Renderers.Add(smr);
        }

        /// <summary>
        /// A necktie on the chest bone (Spine1): a knot at the collar (mesh z 0.0061, y −0.00105) and
        /// a blade hanging to z 0.0044, kept a hair in front of the jacket (front profile −0.00137).
        /// Mesh space: z up, −y forward; world = body.TransformPoint.
        /// </summary>
        public static void Tie(ChibiRig rig, SkinnedMeshRenderer body, Transform root, Color col, int layer)
        {
            if (rig.Spine == null || col.a <= 0f) return;
            var s = body.transform.lossyScale.x;
            col.a = 1f;
            var knot = body.transform.TransformPoint(new Vector3(0f, -0.00118f, 0.00605f));
            var b = new MeshKit.Builder();
            // sizes in mesh units × the figure's scale; built in the wrapper's frame (+z forward, +y up)
            b.Box(new Vector3(0f, 0f, 0f), new Vector3(0.00026f * s, 0.00022f * s, 0.00012f * s), MeshKit.Shade(col, 0.85f));   // the knot
            // the blade: a strip widening then tapering, tilted so its bottom sits in front of the jacket
            b.Grid(1, 6, (u, t) =>
            {
                var w = Mathf.Lerp(0.00010f, 0.00018f, Mathf.Sin(t * Mathf.PI * 0.85f)) * s;
                var y = -0.00012f * s - t * 0.00158f * s;
                var z = Mathf.Lerp(0f, 0.00028f, t) * s;
                return (new Vector3((u - 0.5f) * 2f * w, y, z), Vector3.forward, new Vector2(u, t));
            }, col);
            var mat = MeshKit.NewToon(0.0025f);
            mat.SetFloat("_ShadeStrength", 0.2f);
            var go = Attach("tie", rig.Spine, b.Bake("tie"), mat, layer, knot, root.rotation);
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
        }

        /// <summary>
        /// A tube lofted along a closed or open path (a circle of <paramref name="r"/> around each
        /// point, the ring's frame from the path tangent), for frames and temples.
        /// </summary>
        static void Tube(MeshKit.Builder b, Vector3[] path, bool closed, float r, Color col, int around = 8)
        {
            var n = path.Length;
            var segs = closed ? n : n - 1;
            b.Grid(segs, around, (s, t) =>
            {
                var fi = s * segs; var i0 = Mathf.Min((int)fi, n - 1) % n; var i1 = (i0 + 1) % n; var f = fi - Mathf.Floor(fi);
                if (!closed && i0 == n - 1) { i1 = i0; f = 0f; }
                var p = Vector3.Lerp(path[i0], path[i1], f);
                var tan = (path[(i0 + 1) % n] - path[i0 == 0 && !closed ? 0 : (i0 - 1 + n) % n]).normalized;
                if (tan.sqrMagnitude < 1e-8f) tan = Vector3.right;
                var side = Vector3.Cross(tan, Vector3.forward).normalized; if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(tan, Vector3.up).normalized;
                var upv = Vector3.Cross(side, tan).normalized;
                var ang = t * Mathf.PI * 2f;
                var nrm = side * Mathf.Cos(ang) + upv * Mathf.Sin(ang);
                return (p + nrm * r, nrm, new Vector2(s, t));
            }, col);
        }

        /// <summary>
        /// The face's own depth across the eyes, measured: the face submesh's vertices in a band at
        /// eye height, brought into the glasses' frame (origin between the eyes, x across, y up, +z
        /// out of the face) at rest, binned by x — each bin keeps its most forward point. The glasses
        /// are then laid ON that surface. A circle standing in for the face either left the far lens
        /// floating past the cheek in a three-quarter view (too flat) or turned the lenses sideways
        /// into rings on the cheeks (too round); the real profile does neither.
        /// </summary>
        static float[] FaceProfile(SkinnedMeshRenderer body, Vector3 centre, Quaternion frame, float halfW, int bins)
        {
            var prof = new float[bins]; for (var i = 0; i < bins; i++) prof[i] = float.NaN;
            var mesh = body.sharedMesh; var faceSub = -1;
            var mats = body.sharedMaterials;
            // the brow strips share the face sheet: take the face-textured submesh with the most triangles
            var most = 0;
            for (var i = 0; i < mats.Length && i < mesh.subMeshCount; i++)
                if (mats[i] && mats[i].mainTexture && mats[i].mainTexture.name.StartsWith("face:") && mesh.GetTriangles(i).Length > most) { most = mesh.GetTriangles(i).Length; faceSub = i; }
            if (faceSub < 0) { Debug.LogWarning("[glasses] no face submesh found"); return prof; }
            var verts = mesh.vertices; var inv = Quaternion.Inverse(frame);
            var band = halfW * 0.3f;
            foreach (var idx in mesh.GetTriangles(faceSub))
            {
                var p = inv * (body.transform.TransformPoint(verts[idx]) - centre);
                if (Mathf.Abs(p.y) > band || Mathf.Abs(p.x) >= halfW) continue;
                var bi = Mathf.Clamp((int)((p.x / halfW * 0.5f + 0.5f) * bins), 0, bins - 1);
                if (float.IsNaN(prof[bi]) || p.z > prof[bi]) prof[bi] = p.z;
            }
            // fill empty bins from their neighbours, then smooth
            for (var pass = 0; pass < bins; pass++)
                for (var i = 0; i < bins; i++)
                    if (float.IsNaN(prof[i])) { var l = i > 0 ? prof[i - 1] : float.NaN; var r = i < bins - 1 ? prof[i + 1] : float.NaN; prof[i] = float.IsNaN(l) ? r : float.IsNaN(r) ? l : (l + r) * 0.5f; }
            if (System.Environment.GetEnvironmentVariable("SD_GLASSDUMP") == "1") Debug.Log($"[glasses] faceSub {faceSub} profile/halfW: {string.Join(" ", System.Array.ConvertAll(prof, z => (z / halfW).ToString("F2")))}");
            var sm = (float[])prof.Clone();
            for (var i = 1; i < bins - 1; i++) sm[i] = (prof[i - 1] + prof[i] * 2f + prof[i + 1]) * 0.25f;
            return sm;
        }

        /// <summary>Lays glasses built flat onto the measured face profile (drop relative to the middle), a hair above the skin.</summary>
        static Mesh WrapToFace(Mesh m, (float[] prof, float halfW, float lift) f)
        {
            var (prof, halfW, lift) = f;
            if (prof.Length == 0 || float.IsNaN(prof[prof.Length / 2])) return m;
            float Depth(float x)
            {
                var t = Mathf.Clamp01(x / halfW * 0.5f + 0.5f) * (prof.Length - 1);
                var i = Mathf.Min((int)t, prof.Length - 2);
                return Mathf.Lerp(prof[i], prof[i + 1], t - i);
            }
            var mid = Depth(0f);
            var v = m.vertices;
            for (var i = 0; i < v.Length; i++) v[i].z += Depth(v[i].x) - mid + lift;
            m.vertices = v; m.RecalculateBounds();
            return m;
        }

        static Vector3[] RoundedRect(Vector3 c, float w, float h, float rad, int perCorner = 5)
        {
            var pts = new System.Collections.Generic.List<Vector3>();
            var corners = new[] { new Vector3(w - rad, h - rad), new Vector3(-(w - rad), h - rad), new Vector3(-(w - rad), -(h - rad)), new Vector3(w - rad, -(h - rad)) };
            for (var k = 0; k < 4; k++)
                for (var i = 0; i < perCorner; i++)
                {
                    var a = (k * 90f + i * 90f / (perCorner - 1)) * Mathf.Deg2Rad;
                    pts.Add(c + corners[k] + new Vector3(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad, 0f));
                }
            return pts.ToArray();
        }

        static Vector3[] Ellipse(Vector3 c, float rx, float ry, int n = 24)
        {
            var pts = new Vector3[n];
            for (var i = 0; i < n; i++) { var a = i / (float)n * Mathf.PI * 2f; pts[i] = c + new Vector3(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry, 0f); }
            return pts;
        }

        /// <summary>
        /// Glasses in six styles: square (rounded rectangle), round, oval, half (rim on the top
        /// only), cat (the outer top corner swept up) and rimless (a thin pale edge). v4, against
        /// Gemini mock-ups of these very faces (tools/gemini_edit.py, glasses_*): what made the old
        /// ones ugly was a thin frame, a blue-grey tint that muddied the eyes, and hardware (hinge
        /// beads, nose pads, temples to the ears) that a chibi face has no room for. Now: a bold
        /// solid frame hugging each eye (mesh x ±0.00075, y −0.00128, z 0.00752), a short bridge,
        /// stub temples that vanish into the hair, CLEAR lenses carrying only a glint (sunglasses
        /// stay dark). On the head bone, counter-scaled.
        /// </summary>
        /// <summary>Off (user, 2026-09-28: "the glasses still look silly — take them all off"). The illustrations keep theirs.</summary>
        public static bool Enabled = false;

        public static void Glasses(ChibiRig rig, SkinnedMeshRenderer body, Transform root, bool dark, string style, Color frame, int layer)
        {
            if (!Enabled) return;
            if (rig.Head == null) return;
            var s = body.transform.lossyScale.x;
            var centre = body.transform.TransformPoint(new Vector3(0f, -0.00128f, 0.00752f));
            frame.a = 1f;
            if (dark) frame = Color.Lerp(frame, Color.black, 0.5f);
            var rimless = style == "rimless";
            // the face's measured depth across the eyes (see FaceProfile); the glasses sit on it
            var profile = (FaceProfile(body, centre, root.rotation, 0.0013f * s, 26), 0.0013f * s, 0.00004f * s);
            var b = new MeshKit.Builder();
            float ex = 0.00075f * s, w = 0.00027f * s, h = 0.00021f * s, tube = (dark ? 0.000036f : 0.000028f) * s;
            if (style == "round") { w = 0.00025f * s; h = 0.00025f * s; }
            if (style == "oval") { w = 0.00029f * s; h = 0.00019f * s; }
            foreach (var sx in new[] { -1f, 1f })
            {
                var cx = sx * ex;
                Vector3[] path;
                var closed = true;
                switch (style)
                {
                    case "round": path = Ellipse(new Vector3(cx, 0f, 0f), w, h, 40); break;
                    case "oval": path = Ellipse(new Vector3(cx, 0f, 0f), w, h, 40); break;
                    case "half":
                        {
                            // the rim over the top only: the rounded-rect points above 40 % down, in order from the outer bottom
                            var full = RoundedRect(new Vector3(cx, 0f, 0f), w, h, 0.00010f * s, 9);
                            var keep = new System.Collections.Generic.List<Vector3>();
                            var start = 0; for (var k = 0; k < full.Length; k++) if (full[k].y > -h * 0.4f && full[(k + full.Length - 1) % full.Length].y <= -h * 0.4f && sx * (full[k].x - cx) > 0f) start = k;
                            for (var k = 0; k < full.Length; k++) { var q = full[(start + k) % full.Length]; if (q.y > -h * 0.4f) keep.Add(q); else if (keep.Count > 0) break; }
                            path = keep.ToArray(); closed = false; break;
                        }
                    case "cat":
                        {
                            path = RoundedRect(new Vector3(cx, 0f, 0f), w, h, 0.00010f * s, 9);
                            for (var k = 0; k < path.Length; k++)
                            {
                                var outer = Mathf.Clamp01(sx * (path[k].x - cx) / w);
                                if (path[k].y > 0f) { path[k].y += 0.00013f * s * outer * outer; path[k].x += sx * 0.00007f * s * outer * outer; }
                            }
                            break;
                        }
                    default: path = RoundedRect(new Vector3(cx, 0f, 0f), w, h, 0.00010f * s, 9); break;
                }
                if (rimless) Tube(b, path, closed, tube * 0.45f, new Color(0.8f, 0.84f, 0.9f), 8);
                else Tube(b, path, closed, tube, frame, 10);
                // a stub temple from the outer top corner, straight back into the hair
                var x0 = cx + sx * w;
                Tube(b, new[] { new Vector3(x0, h * 0.35f, 0f), new Vector3(x0 + sx * 0.00006f * s, h * 0.35f, -0.00035f * s) }, false, tube * 0.8f, frame, 6);
            }
            // the bridge: an arch between the inner edges
            var gapIn = ex - w;
            Tube(b, new[] { new Vector3(-gapIn * 0.98f, h * 0.3f, 0f), new Vector3(0f, h * 0.48f, -0.00002f * s), new Vector3(gapIn * 0.98f, h * 0.3f, 0f) }, false, tube, frame, 6);
            // no outline pass: the hull drew a dark ring round every rim, so a red frame came out as a
            // red frame inside a black one — doubled, cartoon-thick spectacles
            var mat = MeshKit.NewToon(0f);
            mat.SetFloat("_ShadeStrength", 0.05f);
            var go = Attach(dark ? "sunglasses" : "glasses:" + style, rig.Head, WrapToFace(b.Bake("glasses"), profile), mat, layer, centre, root.rotation);
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
            // lenses + glints, transparent, as a second part under the same anchor
            var lb = new MeshKit.Builder();
            var lensCol = dark ? new Color(0.10f, 0.09f, 0.16f, 0.74f) : new Color(1f, 1f, 1f, 0.03f);     // clear: the eyes must stay bright
            var glint = new Color(1f, 1f, 1f, dark ? 0.35f : 0.6f);
            foreach (var sx in new[] { -1f, 1f })
            {
                var cx = sx * ex;
                if (style is "round" or "oval")
                {
                    lb.M = Matrix4x4.TRS(new Vector3(cx, 0f, -0.00001f * s), Quaternion.Euler(-90f, 0f, 0f), Vector3.one);
                    lb.Disc(Vector3.zero, w - tube, h - tube, lensCol, true, 20);
                    lb.M = Matrix4x4.identity;
                }
                else lb.Quad(new Vector3(cx, style == "cat" ? h * 0.05f : 0f, -0.00001f * s), new Vector3(w - tube, 0f, 0f), new Vector3(0f, h - tube + (style == "cat" ? h * 0.05f : 0f), 0f), lensCol);
                lb.Quad(new Vector3(cx - sx * w * 0.35f, h * 0.35f, 0.00001f * s), new Vector3(w * 0.22f, h * 0.18f, 0f), new Vector3(-w * 0.05f, h * 0.06f, 0f), glint);
            }
            var lens = MeshKit.NewGlass(null, Color.white);
            var lgo = MeshKit.Part("lenses", go.transform, WrapToFace(lb.Bake("lenses"), profile), lens, layer);
            lgo.transform.localPosition = Vector3.zero; lgo.transform.localRotation = Quaternion.identity; lgo.transform.localScale = Vector3.one;
            rig.Renderers.Add(lgo.GetComponent<MeshRenderer>());
        }

        /// <summary>
        /// The collar that tells a suit from a tracksuit: on the chest bone, a V of two lapel
        /// strips in the jacket colour (a shade darker) opening from the collar (mesh z 0.0061)
        /// down to z 0.0049, with the shirt showing inside the V and a small shirt collar at the
        /// neck. "shirt" and "vest" outfits get the shirt collar only. Sits just in front of the
        /// front profile (−0.00122 at that height).
        /// </summary>
        public static void Collar(ChibiRig rig, SkinnedMeshRenderer body, Transform root, SdLook k, int layer)
        {
            if (rig.Spine == null) return;
            var s = body.transform.lossyScale.x;
            var anchor = body.transform.TransformPoint(new Vector3(0f, -0.00128f, 0.00612f));
            var b = new MeshKit.Builder();
            var shirt = k.Shirt; shirt.a = 1f;
            var lapel = MeshKit.Shade(k.Top, 0.8f); lapel.a = 1f;
            var withLapels = k.Outfit is "suit" or "coat" or "labcoat" or "dress";
            // closed thin boxes, not single-sided strips: the outline pass draws a hull around a
            // box the way it does the tie's knot, whereas a lone strip can end up drawn only by it
            void Slab(Vector3 c, Vector3 size, float zDeg, Color col)
            {
                b.M = Matrix4x4.TRS(c * s, Quaternion.Euler(0f, 0f, zDeg), Vector3.one);
                b.Box(Vector3.zero, size * s, col);
                b.M = Matrix4x4.identity;
            }
            if (withLapels)
            {
                Slab(new Vector3(0f, -0.00072f, 0.00003f), new Vector3(0.00062f, 0.00140f, 0.00003f), 0f, shirt);              // the shirt inside the V
                foreach (var sx in new[] { -1f, 1f })
                    Slab(new Vector3(sx * 0.00031f, -0.00078f, 0.00006f), new Vector3(0.00024f, 0.00158f, 0.00004f), sx * -21f, lapel);   // a lapel, top out at the shoulder
                b.Ellipsoid(new Vector3(0f, -0.00152f, 0.00008f) * s, new Vector3(0.00005f, 0.00005f, 0.00004f) * s, MeshKit.Shade(k.Top, 0.5f), 8);   // the button
            }
            else
                Slab(new Vector3(0f, -0.00030f, 0.00003f), new Vector3(0.00040f, 0.00050f, 0.00003f), 0f, shirt);              // an open shirt neck
            // the shirt collar: two wings at the neck, tips pointing down and out
            foreach (var sx in new[] { -1f, 1f })
                Slab(new Vector3(sx * 0.00024f, -0.00013f, 0.00009f), new Vector3(0.00036f, 0.00015f, 0.00004f), sx * -32f, MeshKit.Shade(shirt, 0.94f));
            var mat = MeshKit.NewToon(0.002f);
            mat.SetFloat("_ShadeStrength", 0.18f);
            var go = Attach("collar", rig.Spine, b.Bake("collar"), mat, layer, anchor, root.rotation);
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
        }

        /// <summary>
        /// Coat tails for coat / labcoat outfits: a lofted band from the hips (y 0.42 of the 1.2 m
        /// figure) to mid-thigh (0.24), open at the front (the coat hangs unbuttoned), in the
        /// jacket colour, on the pelvis so it swings with the body.
        /// </summary>
        public static void CoatTail(ChibiRig rig, Transform root, SdLook k, int layer)
        {
            if (rig.Pelvis == null) return;
            var b = new MeshKit.Builder();
            var col = k.Top; col.a = 1f;
            var inner = MeshKit.Shade(col, 0.72f);
            float top = 0.43f, bottom = 0.24f, r0 = 0.15f, r1 = 0.185f;
            const int seg = 14;
            // from 50° past the front on one side round the back to 50° past the front on the other: angles measured from +z (front)
            b.Grid(seg, 1, (s, t) =>
            {
                var a = Mathf.Lerp(0.85f, Mathf.PI * 2f - 0.85f, s);
                var r = Mathf.Lerp(r0, r1, t);
                var p = new Vector3(Mathf.Sin(a) * r, Mathf.Lerp(top, bottom, t), Mathf.Cos(a) * r * 0.82f);
                var n = new Vector3(Mathf.Sin(a), (r1 - r0) / (top - bottom), Mathf.Cos(a)).normalized;
                return (p, n, new Vector2(s, t));
            }, col);
            b.Grid(seg, 1, (s, t) =>
            {
                var a = Mathf.Lerp(0.85f, Mathf.PI * 2f - 0.85f, s);
                var r = Mathf.Lerp(r0, r1, t) * 0.985f;
                var p = new Vector3(Mathf.Sin(a) * r, Mathf.Lerp(top, bottom, t), Mathf.Cos(a) * r * 0.82f);
                var n = -new Vector3(Mathf.Sin(a), (r1 - r0) / (top - bottom), Mathf.Cos(a)).normalized;
                return (p, n, new Vector2(s, t));
            }, inner);
            var mat = MeshKit.NewToon(0.006f);
            mat.SetFloat("_ShadeStrength", 0.26f);
            var go = Attach("coat", rig.Pelvis, b.Bake("coat"), mat, layer, root.position, root.rotation);
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
        }

        /// <summary>
        /// A cap on the head bone: a dome over the crown (hair top mesh z 0.01018, skull centre
        /// (0, 0.0006, 0.0081)) with a brim forward, in the accessory's colour; a hard hat is the
        /// same dome, taller and without the brim, with a short rim all round.
        /// </summary>
        public static void Cap(ChibiRig rig, SkinnedMeshRenderer body, Transform root, Color col, bool hard, int layer)
        {
            if (rig.Head == null || col.a <= 0f) return;
            var s = body.transform.lossyScale.x;
            col.a = 1f;
            var centre = body.transform.TransformPoint(new Vector3(0f, 0.00045f, 0.00905f));
            var b = new MeshKit.Builder();
            var rx = 0.00168f * s; var ry = (hard ? 0.00115f : 0.00082f) * s; var rz = 0.0018f * s;
            // the dome: the upper half of an ellipsoid (thetaMax π/2 everywhere), a little lower at the back
            b.Ellipsoid(Vector3.zero, new Vector3(rx, ry, rz), col, 18, phi => Mathf.PI * 0.52f);
            if (hard)
                b.Disc(new Vector3(0f, -0.0001f * s, 0f), rx * 1.12f, rz * 1.12f, MeshKit.Shade(col, 0.9f), true, 18);
            else
            {
                // the brim: a flat half-disc forward of the dome, drawn double-sided
                b.Grid(8, 1, (u, t) =>
                {
                    var a = Mathf.Lerp(-1.2f, 1.2f, u);
                    var r = Mathf.Lerp(rz * 0.9f, rz * 1.75f, t);
                    return (new Vector3(Mathf.Sin(a) * r * 0.85f, -0.00008f * s - t * 0.00006f * s, Mathf.Cos(a) * r), Vector3.up, new Vector2(u, t));
                }, MeshKit.Shade(col, 0.88f));
                b.Grid(8, 1, (u, t) =>
                {
                    var a = Mathf.Lerp(-1.2f, 1.2f, u);
                    var r = Mathf.Lerp(rz * 0.9f, rz * 1.75f, t);
                    return (new Vector3(Mathf.Sin(a) * r * 0.85f, -0.00011f * s - t * 0.00006f * s, Mathf.Cos(a) * r), Vector3.down, new Vector2(u, t));
                }, MeshKit.Shade(col, 0.7f));
                b.Box(new Vector3(0f, ry * 1.02f, 0f), new Vector3(0.00012f * s, 0.00012f * s, 0.00012f * s), MeshKit.Shade(col, 0.8f));   // the button
            }
            var mat = MeshKit.NewToon(0.004f);
            mat.SetFloat("_ShadeStrength", 0.22f);
            var go = Attach(hard ? "hardhat" : "cap", rig.Head, b.Bake("cap"), mat, layer, centre, root.rotation);
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
        }

        /// <summary>A headset: a band over the crown from ear to ear, two ear cups, a mic arm on the right.</summary>
        public static void Headset(ChibiRig rig, SkinnedMeshRenderer body, Transform root, Color accent, int layer)
        {
            if (rig.Head == null) return;
            var s = body.transform.lossyScale.x;
            var centre = body.transform.TransformPoint(new Vector3(0f, 0.0005f, 0.0079f));
            var dark = new Color(0.16f, 0.17f, 0.22f);
            accent.a = 1f;
            var b = new MeshKit.Builder();
            var R = 0.00236f * s; var Rz = 0.0002f * s;                    // outside the hair at the sides (hair reaches x ±0.0023) …
            // the band: a strip along an arc in the x–y plane, width w along z, slightly proud of the hair
            var w = 0.00016f * s;
            b.Grid(16, 1, (u, t) =>
            {
                var a = Mathf.Lerp(-1.5f, 1.5f, u);
                var p = new Vector3(Mathf.Sin(a) * R, Mathf.Cos(a) * R * 0.9f, Rz + (t - 0.5f) * 2f * w);    // … and sunk into the crown
                return (p, new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f), new Vector2(u, t));
            }, dark);
            b.Grid(16, 1, (u, t) =>
            {
                var a = Mathf.Lerp(-1.5f, 1.5f, u);
                var p = new Vector3(Mathf.Sin(a) * R * 0.97f, Mathf.Cos(a) * R * 0.87f, Rz + (t - 0.5f) * 2f * w);
                return (p, new Vector3(-Mathf.Sin(a), -Mathf.Cos(a), 0f), new Vector2(u, t));
            }, dark);
            // the cups, over the ears (mesh x ±0.0013 at z 0.0072–0.0078)
            foreach (var sx in new[] { -1f, 1f })
            {
                var c = new Vector3(sx * 0.00232f * s, -0.0006f * s, -0.0003f * s);
                b.Ellipsoid(c, new Vector3(0.00028f * s, 0.00042f * s, 0.00042f * s), dark, 12);
                b.Ellipsoid(c + new Vector3(sx * 0.00022f * s, 0f, 0f), new Vector3(0.0001f * s, 0.00028f * s, 0.00028f * s), accent, 10);
            }
            // the mic arm from the right cup, forward and down to the mouth's side
            // the mic arm: from the right cup forward-down to beside the mouth, as a thin lofted tube
            var a0 = new Vector3(0.00232f * s, -0.0007f * s, -0.0001f * s); var a1 = new Vector3(0.0012f * s, -0.0016f * s, 0.0016f * s);
            b.Grid(1, 8, (u, t) =>
            {
                var p = Vector3.Lerp(a0, a1, t); var ang = u * Mathf.PI * 2f;
                var side = Vector3.Cross((a1 - a0).normalized, Vector3.up).normalized; var upv = Vector3.Cross(side, (a1 - a0).normalized);
                var q = p + (side * Mathf.Cos(ang) + upv * Mathf.Sin(ang)) * 0.00005f * s;
                return (q, (side * Mathf.Cos(ang) + upv * Mathf.Sin(ang)), new Vector2(u, t));
            }, dark);
            b.Ellipsoid(a1, new Vector3(0.00009f * s, 0.00009f * s, 0.00013f * s), accent, 8);
            var mat = MeshKit.NewToon(0.002f);
            mat.SetFloat("_ShadeStrength", 0.2f);
            var go = Attach("headset", rig.Head, b.Bake("headset"), mat, layer, centre, root.rotation);
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
        }

        /// <summary>A belt on trousers: a dark band round the hips (pelvis bone) with a small buckle at the front.</summary>
        public static void Belt(ChibiRig rig, Transform root, SdLook k, int layer)
        {
            if (rig.Pelvis == null) return;
            var b = new MeshKit.Builder();
            var leather = new Color(0.16f, 0.14f, 0.15f);
            b.Frustum(new Vector3(0f, 0.448f, 0f), 0.142f, 0.018f, 0.142f, leather, 0.86f, 20, false);
            b.Box(new Vector3(0f, 0.457f, 0.124f), new Vector3(0.028f, 0.02f, 0.008f), new Color(0.75f, 0.68f, 0.4f));
            var mat = MeshKit.NewToon(0.003f);
            mat.SetFloat("_ShadeStrength", 0.2f);
            var go = Attach("belt", rig.Pelvis, b.Bake("belt"), mat, layer, root.position, root.rotation);
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
        }

        /// <summary>
        /// A ribbon bow at the collar (the school-uniform touch the samples wear): two loops, two
        /// tails and a knot in the accent colour, on the chest bone in front of the collar.
        /// </summary>
        public static void Ribbon(ChibiRig rig, SkinnedMeshRenderer body, Transform root, Color col, int layer)
        {
            if (rig.Spine == null) return;
            var s = body.transform.lossyScale.x;
            col.a = 1f;
            var dark = MeshKit.Shade(col, 0.8f);
            var anchor = body.transform.TransformPoint(new Vector3(0f, -0.00132f, 0.00598f));
            var b = new MeshKit.Builder();
            foreach (var sx in new[] { -1f, 1f })
            {
                // a loop: a flattened ellipsoid out to the side, tilted up a little
                b.M = Matrix4x4.TRS(new Vector3(sx * 0.00026f, 0.00004f, 0f) * s, Quaternion.Euler(0f, 0f, sx * 12f), Vector3.one);
                b.Ellipsoid(Vector3.zero, new Vector3(0.00024f, 0.00011f, 0.00005f) * s, col, 12);
                // a tail: a thin slab hanging down and out
                b.M = Matrix4x4.TRS(new Vector3(sx * 0.00012f, -0.00028f, 0.00001f) * s, Quaternion.Euler(0f, 0f, sx * -18f), Vector3.one);
                b.Box(Vector3.zero, new Vector3(0.00011f, 0.0004f, 0.00003f) * s, dark);
            }
            b.M = Matrix4x4.identity;
            b.Ellipsoid(Vector3.zero, new Vector3(0.00007f, 0.00007f, 0.00006f) * s, dark, 8);   // the knot
            var mat = MeshKit.NewToon(0.0025f);
            mat.SetFloat("_ShadeStrength", 0.2f);
            var go = Attach("ribbon", rig.Spine, b.Bake("ribbon"), mat, layer, anchor, root.rotation);
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
        }

        // The right hand, measured on the base rig (SD_HANDDUMP), in the hand bone's own units:
        // the fingers run along −X (the forearm is at +X), the palm faces +Y, the thumb and index
        // are on +Z. The palm's skin reaches y ≈ 0.00023; the finger roots sit at x ≈ −0.0006.
        const float PalmY = 0.00023f, FistX = -0.0006f, FlatX = -0.00045f, HandMidZ = 0.00006f;
        // the grip frame the props are built in, in metres: +x to the fingertips, +y out of the
        // palm, +z to the little finger (−z the thumb side). As a turn of the hand bone that is
        // 180° about its Y (x → −X, z → −Z, y stays).
        static readonly Quaternion GripFrame = Quaternion.Euler(0f, 180f, 0f);

        /// <summary>
        /// The hand prop by role — melee a rolled document, ranged a tablet, healer a coffee cup,
        /// tank a clipboard — placed IN the grip and the hand closed on it (rig.GripR, which
        /// SdPose.Apply holds whatever the pose). A rod or a cup lies across the palm at the finger
        /// roots and the fist wraps it; a tablet or a board rests on the palm under a light grip.
        /// </summary>
        public static void HandProp(ChibiRig rig, Transform root, string role, SdLook k, int layer)
        {
            if (rig.HandR == null) return;
            var b = new MeshKit.Builder();
            var paper = new Color(0.96f, 0.96f, 0.94f);
            var ink = new Color(0.2f, 0.22f, 0.3f);
            var accent = k.Accent; accent.a = 1f;
            var across = Quaternion.Euler(-90f, 0f, 0f);    // a frustum's +Y → the grip's −z (out of the thumb side)
            float lift, x;                                   // how far off the palm the prop's centre sits (m), and where along the hand
            // the hero's own item from the illustration (sdspec "prop", tools/sdspec_prop_gemini.py), else by role
            var kind = k.Prop switch { "document" => "melee", "cup" => "healer", "clipboard" => "tank", "tablet" => "ranged", null or "" => role, _ => k.Prop };
            switch (kind)
            {
                case "phone":
                {
                    // a phone on the palm, screen out: the slab, the lit screen, the camera dot
                    const float t = 0.007f;
                    b.Box(new Vector3(0.012f, 0f, -0.004f), new Vector3(0.085f, t, 0.045f), accent);
                    b.Quad(new Vector3(0.012f, t * 0.5f + 0.0008f, -0.004f), new Vector3(0.038f, 0f, 0f), new Vector3(0f, 0f, 0.019f), new Color(0.82f, 0.93f, 1f));
                    b.Box(new Vector3(0.045f, -t * 0.5f - 0.001f, 0.008f), new Vector3(0.008f, 0.002f, 0.008f), ink);
                    lift = t * 0.5f + 0.002f; x = FlatX; rig.GripR = 0.2f;
                    break;
                }
                case "pen":
                {
                    // a pen in the fist, the nib out past the thumb: barrel, clip, cap end
                    const float r = 0.007f;
                    b.M = Matrix4x4.Rotate(across);
                    b.Frustum(new Vector3(0f, -0.05f, 0f), r, 0.12f, r, accent, 1f, 10);
                    b.Frustum(new Vector3(0f, 0.07f, 0f), r, 0.018f, 0.001f, ink, 1f, 10);
                    b.Box(new Vector3(r + 0.002f, -0.03f, 0f), new Vector3(0.003f, 0.04f, 0.004f), new Color(0.8f, 0.8f, 0.84f));
                    b.M = Matrix4x4.identity;
                    lift = r; x = FistX; rig.GripR = 0.85f;
                    break;
                }
                case "folder":
                {
                    // a closed file folder on the palm: two covers, papers between, a tab
                    const float t = 0.012f;
                    b.Box(new Vector3(0.03f, 0f, -0.01f), new Vector3(0.16f, t, 0.12f), accent);
                    b.Box(new Vector3(0.028f, 0f, -0.012f), new Vector3(0.155f, t * 0.7f, 0.118f), paper);
                    b.Box(new Vector3(0.115f, 0f, 0.03f), new Vector3(0.02f, t, 0.03f), MeshKit.Shade(accent, 0.8f));
                    lift = t * 0.5f + 0.002f; x = FlatX; rig.GripR = 0.15f;
                    break;
                }
                case "calculator":
                {
                    // a desk calculator on the palm: body, the display, a grid of keys
                    const float t = 0.012f;
                    b.Box(new Vector3(0.02f, 0f, -0.006f), new Vector3(0.1f, t, 0.07f), new Color(0.3f, 0.32f, 0.38f));
                    b.Quad(new Vector3(0.05f, t * 0.5f + 0.0008f, -0.006f), new Vector3(0.012f, 0f, 0f), new Vector3(0f, 0f, 0.028f), new Color(0.75f, 0.85f, 0.7f));
                    for (var i = 0; i < 4; i++)
                        for (var j = 0; j < 4; j++)
                            b.Box(new Vector3(0.025f - i * 0.016f, t * 0.5f + 0.001f, -0.03f + j * 0.016f), new Vector3(0.011f, 0.003f, 0.011f), i == 3 && j == 3 ? accent : paper);
                    lift = t * 0.5f + 0.002f; x = FlatX; rig.GripR = 0.15f;
                    break;
                }
                case "laptop":
                {
                    // a closed laptop carried flat: the lid in the accent colour, a logo dot
                    const float t = 0.014f;
                    b.Box(new Vector3(0.04f, 0f, -0.012f), new Vector3(0.2f, t, 0.14f), new Color(0.75f, 0.77f, 0.8f));
                    b.Box(new Vector3(0.04f, t * 0.5f + 0.001f, -0.012f), new Vector3(0.19f, 0.002f, 0.13f), accent);
                    b.Disc(new Vector3(0.04f, t * 0.5f + 0.0025f, -0.012f), 0.012f, 0.012f, paper, true, 10);
                    lift = t * 0.5f + 0.002f; x = FlatX; rig.GripR = 0.15f;
                    break;
                }
                case "melee":
                {
                    // a rolled-up document across the fist, the long end out past the thumb, a band near it
                    const float r = 0.018f;
                    b.M = Matrix4x4.Rotate(across);
                    b.Frustum(new Vector3(0f, -0.045f, 0f), r, 0.2f, r, paper, 1f, 12);
                    b.Frustum(new Vector3(0f, 0.09f, 0f), r + 0.002f, 0.025f, r + 0.002f, accent, 1f, 12, false);
                    b.M = Matrix4x4.identity;
                    lift = r; x = FistX; rig.GripR = 0.75f;
                    break;
                }
                case "healer":
                {
                    // a coffee cup held round its body, its mouth up past the thumb: cup, coffee, sleeve, handle
                    const float r = 0.03f;
                    b.M = Matrix4x4.Rotate(across);
                    b.Frustum(new Vector3(0f, -0.04f, 0f), r * 0.85f, 0.085f, r, paper, 1f, 14);
                    b.Disc(new Vector3(0f, 0.0455f, 0f), r * 0.94f, r * 0.94f, new Color(0.35f, 0.22f, 0.14f), true, 14);
                    b.Frustum(new Vector3(0f, -0.005f, 0f), r * 0.97f, 0.022f, r * 1.0f, accent, 1f, 14, false);
                    // the handle, off the fingertip side
                    b.Box(new Vector3(r + 0.012f, 0.002f, 0f), new Vector3(0.008f, 0.04f, 0.008f), paper);
                    b.Box(new Vector3(r + 0.005f, 0.018f, 0f), new Vector3(0.016f, 0.008f, 0.008f), paper);
                    b.Box(new Vector3(r + 0.005f, -0.014f, 0f), new Vector3(0.016f, 0.008f, 0.008f), paper);
                    b.M = Matrix4x4.identity;
                    lift = r; x = FistX; rig.GripR = 0.55f;
                    break;
                }
                case "tank":
                {
                    // a clipboard resting on the palm, its face out: board, the sheet with lines, the clip at the far end
                    const float t = 0.01f;
                    b.Box(new Vector3(0.03f, 0f, -0.01f), new Vector3(0.17f, t, 0.13f), new Color(0.55f, 0.4f, 0.28f));
                    b.Quad(new Vector3(0.025f, t * 0.5f + 0.0008f, -0.01f), new Vector3(0.07f, 0f, 0f), new Vector3(0f, 0f, 0.055f), paper);
                    for (var i = 0; i < 4; i++) b.Quad(new Vector3(0.05f - i * 0.02f, t * 0.5f + 0.0012f, -0.01f), new Vector3(0f, 0f, 0.04f), new Vector3(0.003f, 0f, 0f), ink);
                    b.Box(new Vector3(0.105f, t * 0.5f + 0.006f, -0.01f), new Vector3(0.02f, 0.012f, 0.05f), new Color(0.6f, 0.62f, 0.66f));
                    lift = t * 0.5f + 0.002f; x = FlatX; rig.GripR = 0.15f;
                    break;
                }
                default:
                {
                    // a tablet resting on the palm, screen out: the slab, the glowing sheet, a few rows
                    const float t = 0.01f;
                    b.Box(new Vector3(0.025f, 0f, -0.005f), new Vector3(0.15f, t, 0.105f), ink);
                    b.Quad(new Vector3(0.025f, t * 0.5f + 0.0008f, -0.005f), new Vector3(0.066f, 0f, 0f), new Vector3(0f, 0f, 0.045f), new Color(0.85f, 0.95f, 1f));
                    for (var i = 0; i < 3; i++) b.Quad(new Vector3(0.05f - i * 0.025f, t * 0.5f + 0.0012f, -0.005f), new Vector3(0f, 0f, 0.035f), new Vector3(0.004f, 0f, 0f), Color.Lerp(accent, Color.white, 0.3f));
                    lift = t * 0.5f + 0.002f; x = FlatX; rig.GripR = 0.15f;
                    break;
                }
            }
            var mat = MeshKit.NewToon(0.003f);
            mat.SetFloat("_ShadeStrength", 0.18f);
            var hand = rig.HandR;
            var at = hand.TransformPoint(new Vector3(x, PalmY + lift / hand.lossyScale.y, HandMidZ));
            var go = Attach("prop:" + role, hand, b.Bake("prop"), mat, layer, at, hand.rotation * GripFrame);
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
        }
    }
}
