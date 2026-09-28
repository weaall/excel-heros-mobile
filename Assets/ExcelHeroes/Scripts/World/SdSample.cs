using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// A hero wearing one of the sample bodies as it was made (Resources/Art/SDBase/bodies/&lt;key&gt;,
    /// git-ignored, mirrored on Drive): its own outfit, hair and face, the face layered the way BA
    /// layers it (SdFace), the hair and the iris recoloured to the hero, the body sheet recoloured
    /// by its colour clusters (SdSampleTex). Everything that hangs on the Bip001 skeleton — the
    /// clips (SdClips), the poses, the hand prop, the glasses, the secondary chains — works as on
    /// the base, which is the point: the base was a gym outfit under every hero.
    /// </summary>
    public static class SdSample
    {
        static readonly Dictionary<string, GameObject> Prefabs = new();

        public static bool Has(string key) => !string.IsNullOrEmpty(key) && Prefab(key) != null;

        static GameObject Prefab(string key)
        {
            if (Prefabs.TryGetValue(key, out var p)) return p;
            p = Resources.LoadAll<GameObject>("Art/SDBase/bodies/" + key).FirstOrDefault();
            return Prefabs[key] = p;
        }

        static Texture2D Sheet(string key, string part)
        {
            var all = Resources.LoadAll<Texture2D>("Art/SDBase/bodies/" + key);
            return all.FirstOrDefault(t => t.name.ToLowerInvariant().EndsWith("_" + part) || t.name.ToLowerInvariant().EndsWith("_" + part + "_2"))
                ?? all.FirstOrDefault(t => t.name.ToLowerInvariant().Contains(part));
        }

        // the hair library name of each body's own hair (HairLibExtract)
        static string OwnLib(string key) => key switch
        {
            "hayase_yuuka" or "hayase_yuuka_gym_ver_" => "yuuka", "kayoko_dress_ver_" => "kayoko", "hatsune_miku" => "miku",
            "yutori_natsu" => "natsu", _ => key,
        };

        /// <summary>
        /// The hero's hair, not the sample's: a long style from the hair library (the samples' own
        /// hairs, skinned, with their chains), or — for the short, bob, bun and spiky styles the
        /// library has no sample for — the base figure's trimmed hair, built for this hero and moved
        /// across on the head bone. The sample's own hair is hidden unless it is already the pick.
        /// </summary>
        /// <summary>
        /// The hero's own hair assembled from parts of the samples' hair (SdRefHairLib regions): the
        /// cap and fringe from one, the sides from one, the back from one, the extras (bun, ribbons)
        /// from one, cut to the illustrated length. Recipe "front=miku;side=natsu;back=mika;extra=mika;len=long"
        /// (sdspec "hair", tools/sdspec_hair_gemini.py; SD_HAIR overrides).
        /// </summary>
        static bool Recipe(ChibiRig rig, SdLook k, List<SkinnedMeshRenderer> kept, int layer)
        {
            var text = System.Environment.GetEnvironmentVariable("SD_HAIR") ?? k.HairRecipe;
            if (string.IsNullOrEmpty(text)) return false;
            var add = System.Environment.GetEnvironmentVariable("SD_HAIRADD");   // preview: extra keys for this build (later keys win)
            if (!string.IsNullOrEmpty(add)) text += ";" + add;
            var r = new Dictionary<string, string>();
            foreach (var kv2 in text.Split(';').Select(x => x.Split('=')).Where(x => x.Length == 2)) r[kv2[0].Trim()] = kv2[1].Trim();
            string Get(string key2, string fallback) => r.TryGetValue(key2, out var v) && v != "" ? v : fallback;
            var front = Get("front", "haruka");
            if (!SdRefHairLib.Has(front)) return false;
            HideOwnHair(kept);
            var cutY = float.NaN;
            if (rig.Neck != null && rig.Head != null)
            {
                var neckY = rig.Neck.position.y; var headY = rig.Head.position.y; var span = headY - neckY;
                cutY = Get("len", "long") switch
                {
                    "short" => neckY + span * 0.15f,
                    "bob" => neckY - span * 0.35f,
                    "shoulder" => neckY - span * 0.9f,
                    _ => float.NaN,
                };
            }
            // parts from the same sample go on together, so a sample's own cap is mounted once
            var parts = new Dictionary<string, SdRefHairLib.Region>();
            void Add(string lib, SdRefHairLib.Region reg) { if (lib == "none" || !SdRefHairLib.Has(lib)) return; parts[lib] = (parts.TryGetValue(lib, out var e) ? e : 0) | reg; }
            Add(front, SdRefHairLib.Region.Cap | SdRefHairLib.Region.Front);
            // Yuuka's cap is open where her side locks join it (a black hole of outline hull showed
            // through there under anyone else's), so her fringe or her back always brings those locks
            var back = Get("back", front);
            Add(front == "yuuka" || back == "yuuka" ? "yuuka" : Get("side", front), SdRefHairLib.Region.Side);
            // tails (twin tails, a side ponytail) only when asked: they were riding in with sides and backs
            Add(Get("tails", "none"), SdRefHairLib.Region.Tails);
            // its cap too: the back hangs off it — but not Kayoko's, whose crown keeps her two-tone streak
            Add(Get("back", front), SdRefHairLib.Region.Back | (Get("back", front) == "kayoko" && front != "kayoko" ? 0 : SdRefHairLib.Region.Cap));
            Add(Get("extra", "none"), SdRefHairLib.Region.Extra);
            // the hero's own volume and fall (recipe vol= / fall=; unset: a stable spread by id), so two
            // heroes with the same parts still wear a different head of hair
            var hh = Mathf.Abs(SdPose.Hash(k.Id));
            float Num(string key2, float fb) => float.TryParse(Get(key2, ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : fb;
            SdRefHairLib.Volume = Mathf.Clamp(Num("vol", 0.95f + hh % 9 * 0.022f), 0.9f, 1.18f);
            SdRefHairLib.Fall = Mathf.Clamp(Num("fall", Get("len", "long") == "long" ? 0.86f + hh / 9 % 8 * 0.055f : 1f), 0.75f, 1.35f);
            SdRefHairLib.Wave = Mathf.Clamp(Num("wave", 0f), 0f, 1.2f);
            SdRefHairLib.Spread = Mathf.Clamp(Num("spread", 0f), -1f, 1f);
            SdRefHairLib.Gather = Mathf.Clamp(Num("gather", 0f), 0f, 1f);
            SdRefHairLib.Curl = Mathf.Clamp(Num("curl", 0f), -1f, 1f);
            try
            {
                foreach (var kv in parts)
                {
                    // the extras (a bun, a ribbon) are never cut: they sit where their sample put them
                    var reg = kv.Value;
                    // tails hang whatever the hair's length: never cut
                    const SdRefHairLib.Region Uncut = SdRefHairLib.Region.Extra | SdRefHairLib.Region.Tails;
                    if ((reg & ~Uncut) != 0) SdRefHairLib.MountParts(rig, kv.Key, k, layer, reg & ~Uncut, cutY);
                    if ((reg & SdRefHairLib.Region.Tails) != 0) SdRefHairLib.MountParts(rig, kv.Key, k, layer, SdRefHairLib.Region.Tails);
                    if ((reg & SdRefHairLib.Region.Extra) != 0) { var fall = SdRefHairLib.Fall; SdRefHairLib.Fall = 1f; SdRefHairLib.MountParts(rig, kv.Key, k, layer, SdRefHairLib.Region.Extra); SdRefHairLib.Fall = fall; }
                }
            }
            finally { SdRefHairLib.Volume = 1f; SdRefHairLib.Fall = 1f; SdRefHairLib.Wave = SdRefHairLib.Spread = SdRefHairLib.Gather = SdRefHairLib.Curl = 0f; }
            return true;
        }

        static void HideOwnHair(List<SkinnedMeshRenderer> kept)
        {
            foreach (var r in kept)
            {
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].mainTexture != null && mats[i].mainTexture.name.Contains("+hair")) mats[i].SetFloat("_Cutoff", 2f);
            }
        }

        static void DressHair(ChibiRig rig, string heroId, string key, SdLook k, List<SkinnedMeshRenderer> kept, int layer)
        {
            if (Recipe(rig, k, kept, layer)) return;
            var lib = k.HairLib != "" ? (k.HairLib == "base" ? null : k.HairLib) : SdRefHairLib.Pick(k.Style, heroId);
            if (lib == OwnLib(key)) return;                       // the sample's hair is the right one
            foreach (var r in kept)
            {
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].mainTexture != null && mats[i].mainTexture.name.Contains("+hair")) mats[i].SetFloat("_Cutoff", 2f);
            }
            if (lib != null && SdRefHairLib.Has(lib)) { SdRefHairLib.Mount(rig, lib, k, layer); return; }
            // short styles: a sample's straight hair cut to length (the jaw for short / bun / spiky,
            // a little below for a bob); the base figure's hair only if the library is missing
            var donorLib = System.Environment.GetEnvironmentVariable("SD_SHORTLIB") ?? "haruka";   // haruka: one tone, a full fringe, strands that end cleanly when cut
            if (SdRefHairLib.Has(donorLib) && rig.Neck != null && rig.Head != null)
            {
                var neckY = rig.Neck.position.y; var headY = rig.Head.position.y;
                var jaw = neckY + (headY - neckY) * 0.15f;
                var cutY = k.Style == "bob" ? neckY - (headY - neckY) * 0.35f : jaw;
                SdRefHairLib.MountCut(rig, donorLib, k, layer, cutY);
                return;
            }
            // short styles: the base figure's hair for this hero, baked in its head bone's space
            var temp = new GameObject("hairdonor").transform;
            try
            {
                var donor = SdRef.BuildBase(heroId, temp, layer);
                if (donor?.Head == null || rig.Head == null) return;
                var body = donor.FaceRenderer as SkinnedMeshRenderer;
                if (body == null) return;
                var hairMat = SdRefLook.For(heroId).MaterialFor("hair");
                var sub = System.Array.IndexOf(body.sharedMaterials, hairMat);
                var baked = new Mesh(); body.BakeMesh(baked, true);
                if (sub < 0) return;
                var tris = baked.GetTriangles(sub); var vs = baked.vertices; var ns = baked.normals; var uv = baked.uv;
                // into the donor head's space, then onto ours
                var toHead = donor.Head.worldToLocalMatrix * body.transform.localToWorldMatrix;
                var map = new Dictionary<int, int>(); var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>(); var nt = new List<int>();
                foreach (var t in tris)
                {
                    if (!map.TryGetValue(t, out var j)) { j = map[t] = nv.Count; nv.Add(toHead.MultiplyPoint3x4(vs[t])); nn.Add(toHead.MultiplyVector(ns[t]).normalized); nu.Add(uv[t]); }
                    nt.Add(j);
                }
                var m = new Mesh { name = "hair:" + heroId, indexFormat = nv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(nv); m.SetNormals(nn); m.SetUVs(0, nu); m.SetTriangles(nt, 0); m.RecalculateBounds();
                var go = new GameObject("hair") { layer = layer };
                go.transform.SetParent(rig.Head, false);
                // the donor's head bone and ours carry different lossy scales (each model was normalised to 1.2)
                var ratio = donor.Head.lossyScale.x / Mathf.Max(1e-6f, rig.Head.lossyScale.x);
                go.transform.localScale = Vector3.one * ratio;
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = hairMat;
                rig.Renderers.Add(mr);
                // the scalp ball too, when the donor made one
                foreach (var sc in donor.Head.GetComponentsInChildren<MeshRenderer>(true).Where(x => x.name == "scalp"))
                {
                    var copy = Object.Instantiate(sc.gameObject, rig.Head, false);
                    copy.transform.localPosition = sc.transform.localPosition * ratio; copy.transform.localRotation = sc.transform.localRotation;
                    copy.transform.localScale = sc.transform.localScale * ratio;
                    rig.Renderers.Add(copy.GetComponent<MeshRenderer>());
                }
            }
            finally { Object.DestroyImmediate(temp.gameObject); }
        }

        /// <summary>
        /// The sample's character kit off the outfit: pieces of the BODY submesh above the neck (caps,
        /// horns, ear devices — the face and the hair are their own submeshes) and well behind the
        /// hips (tails). An office worker in Hikari's cap or with Kayoko's tail read as cosplay.
        /// </summary>
        /// <summary>
        /// Each eye's plates (white, iris, highlight, lash line) re-skinned to a bone of its own at
        /// the eye's lower edge, so a blink squashes the eye shut onto its lower lid on every
        /// sample alike (the samples' own lid bones close some rigs and not others). SdBlink drives them.
        /// </summary>
        static void EyeBones(SkinnedMeshRenderer r, SdFace.Part[] parts, Transform head, Transform root, List<(Transform bone, Transform lid, Vector3 drop)> made, int layer)
        {
            if (head == null) return;
            var mesh = r.sharedMesh; var bw = mesh.boneWeights;
            if (bw.Length != mesh.vertexCount) return;
            var l2w = r.transform.localToWorldMatrix; var v = mesh.vertices;
            var eyeV = new HashSet<int>();
            for (var s = 0; s < mesh.subMeshCount && s < parts.Length; s++)
                if (parts[s] is SdFace.Part.White or SdFace.Part.Iris or SdFace.Part.IrisFree or SdFace.Part.Highlight or SdFace.Part.Line)
                    foreach (var t in mesh.GetTriangles(s)) eyeV.Add(t);
            if (eyeV.Count == 0) return;
            var cx = eyeV.Average(i => Vector3.Dot(l2w.MultiplyPoint3x4(v[i]), root.right));
            var bones = r.bones.ToList(); var binds = mesh.bindposes.ToList();
            var skinV = new HashSet<int>();
            for (var s = 0; s < mesh.subMeshCount && s < parts.Length; s++)
                if (parts[s] == SdFace.Part.Skin) foreach (var t in mesh.GetTriangles(s)) skinV.Add(t);
            var rb = r.bones;
            foreach (var side in new[] { -1f, 1f })
            {
                var mine = eyeV.Where(i => (Vector3.Dot(l2w.MultiplyPoint3x4(v[i]), root.right) - cx) * side > 0f).ToList();
                if (mine.Count == 0) continue;
                var w = mine.Select(i => l2w.MultiplyPoint3x4(v[i])).ToList();
                var low = w.Min(p => p.y); var top = w.Max(p => p.y); var ctr = w.Aggregate(Vector3.zero, (a, p) => a + p) / w.Count;
                var x0 = w.Min(p => Vector3.Dot(p, root.right)); var x1 = w.Max(p => Vector3.Dot(p, root.right));
                // the upper lid: the bone (other than the head) most of the skin just over this eye rides —
                // BA closes an eye by sliding that skin down over it
                var eh = top - low; var votes = new Dictionary<int, float>();
                foreach (var i in skinV)
                {
                    var p = l2w.MultiplyPoint3x4(v[i]); var px = Vector3.Dot(p, root.right);
                    if (px < x0 || px > x1 || p.y < top - eh * 0.25f || p.y > top + eh * 0.3f) continue;
                    void Vote(int bi2, float wt) { if (wt > 0.2f && bi2 >= 0 && bi2 < rb.Length && rb[bi2] != null && rb[bi2] != head && !rb[bi2].name.Contains("Head")) votes[bi2] = (votes.TryGetValue(bi2, out var o) ? o : 0f) + wt; }
                    var q = bw[i]; Vote(q.boneIndex0, q.weight0); Vote(q.boneIndex1, q.weight1); Vote(q.boneIndex2, q.weight2); Vote(q.boneIndex3, q.weight3);
                }
                // none (Mika's lid skin rides the head; her eye_L_1 tears the face): that eye does not blink
                if (votes.Count == 0) continue;
                var lid = rb[votes.OrderByDescending(x => x.Value).First().Key];
                var b = new GameObject(side < 0 ? "blink_L" : "blink_R") { layer = layer }.transform;
                b.SetParent(head, true);
                b.position = new Vector3(ctr.x, low + (ctr.y - low) * 0.25f, ctr.z); b.rotation = root.rotation;
                var bi = bones.Count; bones.Add(b); binds.Add(b.worldToLocalMatrix * l2w);
                foreach (var i in mine) bw[i] = new BoneWeight { boneIndex0 = bi, weight0 = 1f };
                if (System.Environment.GetEnvironmentVariable("SD_LIDDBG") == "1") Debug.Log($"[lid] {r.name} side {side} lid {(lid ? lid.name : "-")} votes {string.Join(",", votes.OrderByDescending(x => x.Value).Take(4).Select(x => rb[x.Key].name + "=" + x.Value.ToString("0.0")))}");
                made.Add((b, lid, -root.up * eh * 0.85f));
            }
            mesh.boneWeights = bw; mesh.bindposes = binds.ToArray();
            r.bones = bones.ToArray();
        }

        static bool FacePart(string mat) { var n = mat.ToLowerInvariant(); return n.Contains("face") || n.Contains("eyemouth") || n.Contains("eyebrow"); }

        /// <summary>
        /// Another sample's face on this body: its face plate, eye plates and brows (the submeshes named
        /// face / eyemouth / eyebrow of its body and of its separate face renderer), placed so its head
        /// bone sits on ours and re-skinned to our bones by name (the head where we lack one). Our own
        /// face submeshes are emptied and a separate face renderer hidden. The new renderer then goes
        /// through the same face split and eye rig as a body's own.
        /// </summary>
        static SkinnedMeshRenderer SwapFace(string donorKey, SkinnedMeshRenderer body, List<SkinnedMeshRenderer> kept, Transform root, Transform model, Transform[] ours, int layer)
        {
            var prefab = Prefab(donorKey); if (prefab == null) return null;
            var head = ours.FirstOrDefault(t => t.name == "Bip001 Head"); if (head == null) return null;
            var temp = new GameObject("facedonor").transform; temp.SetParent(root, false);
            var dgo = Object.Instantiate(prefab, temp);
            var drs = dgo.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var dbody = drs.OrderByDescending(r => r.sharedMesh.subMeshCount).ThenByDescending(r => r.sharedMesh.vertexCount).First();
            dbody.updateWhenOffscreen = true;
            var wb = dbody.bounds; var lo = temp.InverseTransformPoint(wb.min); var hi = temp.InverseTransformPoint(wb.max);
            // the donor at our height (the samples share one proportion), then its head bone on ours
            var ourH = body.bounds.size.y / Mathf.Max(1e-5f, root.lossyScale.y);
            dgo.transform.localScale = Vector3.one * (ourH / Mathf.Max(1e-5f, hi.y - lo.y));
            var dhead = dgo.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Bip001 Head");
            if (dhead == null) { Object.DestroyImmediate(temp.gameObject); return null; }
            dgo.transform.position += head.position - dhead.position;
            var sources = drs.Where(r => r == dbody || (r.name.Contains("Face_Outline") && !r.name.Contains("Face0"))).ToList();
            var byName = ours.GroupBy(t => t.name).ToDictionary(gp => gp.Key, gp => gp.First());
            var verts = new List<Vector3>(); var nrms = new List<Vector3>(); var uvs = new List<Vector2>(); var bws = new List<BoneWeight>();
            var bones = new List<Transform>(); var boneIx = new Dictionary<Transform, int>();
            int Bone(Transform d)
            {
                var t = d != null && d.name.StartsWith("Bip001") && byName.TryGetValue(d.name, out var o) ? o : head;
                if (!boneIx.TryGetValue(t, out var i)) { i = bones.Count; bones.Add(t); boneIx[t] = i; }
                return i;
            }
            var subs = new List<(string name, List<int> tris)>();
            foreach (var r in sources)
            {
                var m = r.sharedMesh; var mats = r.sharedMaterials; var l2w = r.transform.localToWorldMatrix;
                var mv = m.vertices; var mn = m.normals; var mu = m.uv; var mb = m.boneWeights; var rb = r.bones;
                Transform B(int bi) => bi >= 0 && bi < rb.Length ? rb[bi] : null;
                for (var sIx = 0; sIx < m.subMeshCount && sIx < mats.Length; sIx++)
                {
                    if (mats[sIx] == null || !FacePart(mats[sIx].name)) continue;
                    var map = new Dictionary<int, int>(); var tris = new List<int>();
                    foreach (var t in m.GetTriangles(sIx))
                    {
                        if (!map.TryGetValue(t, out var ni))
                        {
                            ni = verts.Count; map[t] = ni;
                            verts.Add(l2w.MultiplyPoint3x4(mv[t]));
                            nrms.Add(mn.Length == mv.Length ? l2w.MultiplyVector(mn[t]).normalized : Vector3.forward);
                            uvs.Add(mu.Length == mv.Length ? mu[t] : Vector2.zero);
                            var w = mb.Length == mv.Length ? mb[t] : new BoneWeight { weight0 = 1f };
                            bws.Add(new BoneWeight
                            {
                                boneIndex0 = Bone(B(w.boneIndex0)), weight0 = w.weight0, boneIndex1 = Bone(B(w.boneIndex1)), weight1 = w.weight1,
                                boneIndex2 = Bone(B(w.boneIndex2)), weight2 = w.weight2, boneIndex3 = Bone(B(w.boneIndex3)), weight3 = w.weight3,
                            });
                        }
                        tris.Add(ni);
                    }
                    subs.Add((mats[sIx].name, tris));
                }
            }
            Object.DestroyImmediate(temp.gameObject);
            if (subs.Count == 0) return null;
            // our own face out: the body's face submeshes emptied, a separate face renderer hidden
            foreach (var r in kept.ToList())
            {
                if (r != body) { r.gameObject.SetActive(false); kept.Remove(r); continue; }
                var m = Object.Instantiate(r.sharedMesh); var mats = r.sharedMaterials;
                for (var sIx = 0; sIx < m.subMeshCount && sIx < mats.Length; sIx++) if (mats[sIx] != null && FacePart(mats[sIx].name)) m.SetTriangles(new int[0], sIx, false);
                r.sharedMesh = m;
            }
            var go = new GameObject("face:" + donorKey) { layer = layer };
            go.transform.SetParent(model, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            var w2l = go.transform.worldToLocalMatrix;
            var mesh = new Mesh { name = "face:" + donorKey, indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(verts.Select(p => w2l.MultiplyPoint3x4(p)).ToList());
            mesh.SetNormals(nrms.Select(n => w2l.MultiplyVector(n).normalized).ToList());
            mesh.SetUVs(0, uvs);
            mesh.boneWeights = bws.ToArray();
            mesh.subMeshCount = subs.Count;
            for (var i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i].tris, i, false);
            mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * go.transform.localToWorldMatrix).ToArray();
            mesh.RecalculateBounds();
            smr.sharedMesh = mesh; smr.bones = bones.ToArray(); smr.rootBone = head;
            smr.sharedMaterials = subs.Select(x => new Material(MeshKit.ToonFlat) { name = x.name }).ToArray();
            smr.updateWhenOffscreen = true;
            return smr;
        }

        static void StripKit(SkinnedMeshRenderer body, Transform neck, Transform pelvis)
        {
            if (neck == null || pelvis == null) return;
            var mesh = Object.Instantiate(body.sharedMesh);
            var baked = new Mesh(); body.BakeMesh(baked, true);
            var w = baked.vertices.Select(p => body.transform.TransformPoint(p)).ToArray();
            var fwd = body.transform.root.forward;
            var names = body.sharedMaterials.Select(m => m ? m.name.ToLowerInvariant() : "").ToArray();
            var neckY = neck.position.y; var hipZ = Vector3.Dot(pelvis.position, fwd);
            var bw = mesh.boneWeights; var bones = body.bones;
            var height = w.Max(p => p.y) - w.Min(p => p.y);
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                if (s >= names.Length || !names[s].Contains("body")) continue;
                var tris = mesh.GetTriangles(s); var keep = new List<int>(tris.Length);
                foreach (var piece in SdFacePieces(tris))
                {
                    var c = Vector3.zero; foreach (var t in piece) c += w[t]; c /= piece.Count;
                    var above = c.y > neckY + height * 0.02f;
                    var behind = Vector3.Dot(c, fwd) < hipZ - height * 0.14f && c.y < neckY;
                    // wings (Mika's): by their bones
                    // … and the kit on its own bones: Hikari's bag and cross straps (the "tactical harness"
                    // Gemini kept seeing on office suits), Natsu's phone
                    var wing = 0;
                    foreach (var t in piece)
                    {
                        var dn = Dominant(bw[t], bones).ToLowerInvariant();
                        if (dn.Contains("wing") || dn.Contains("_bag") || dn.Contains("acc_0") || dn.Contains("phone")) wing++;
                    }
                    if (wing * 2 > piece.Count) behind = true;
                    if (above || behind) continue;
                    // kit joined to the outfit (Hikari's bag and pouch hang off her jacket): by triangle
                    for (var q = 0; q < piece.Count; q += 3)
                    {
                        bool Kit(int vi) { var dn = Dominant(bw[vi], bones).ToLowerInvariant(); return dn.Contains("_bag") || dn.Contains("acc_0") || dn.Contains("phone"); }
                        if (Kit(piece[q]) && Kit(piece[q + 1]) && Kit(piece[q + 2])) continue;
                        keep.Add(piece[q]); keep.Add(piece[q + 1]); keep.Add(piece[q + 2]);
                    }
                }
                mesh.SetTriangles(keep, s, false);
            }
            body.sharedMesh = mesh;
        }

        static IEnumerable<List<int>> SdFacePieces(int[] tris)
        {
            var parent = new Dictionary<int, int>();
            int Root(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }
            foreach (var t in tris) if (!parent.ContainsKey(t)) parent[t] = t;
            for (var i = 0; i < tris.Length; i += 3) { var a = Root(tris[i]); parent[Root(tris[i + 1])] = a; parent[Root(tris[i + 2])] = a; }
            var groups = new Dictionary<int, List<int>>();
            for (var i = 0; i < tris.Length; i += 3)
            {
                var r = Root(tris[i]);
                if (!groups.TryGetValue(r, out var l)) groups[r] = l = new List<int>();
                l.Add(tris[i]); l.Add(tris[i + 1]); l.Add(tris[i + 2]);
            }
            return groups.Values;
        }

        /// <summary>The sample exactly as made (its own colours, hair, kit) — for side-by-side comparisons.</summary>
        /// <summary>Set by the repaint tool while it renders the recolour it repaints from.</summary>
        public static bool SkipPainted;

        public static ChibiRig BuildRaw(string key, Transform parent, int layer)
        {
            _raw = true;
            try { return Build("raw:" + key, key, parent, layer); } finally { _raw = false; }
        }
        static bool _raw;

        static bool Lower(string bone)
        {
            var n = bone.ToLowerInvariant();
            return n.Contains("skirt") || n.Contains("thigh") || n.Contains("calf") || n.Contains("knee") || n.Contains("foot") || n.Contains("toe");   // knee: its helper bones left the own knees behind as black marks
        }

        static string Dominant(BoneWeight w, Transform[] bones)
        {
            var i = w.boneIndex0; var m = w.weight0;
            if (w.weight1 > m) { i = w.boneIndex1; m = w.weight1; }
            if (w.weight2 > m) { i = w.boneIndex2; m = w.weight2; }
            if (w.weight3 > m) i = w.boneIndex3;
            return i >= 0 && i < bones.Length && bones[i] != null ? bones[i].name : "";
        }

        /// <summary>
        /// The lower body (skirt, legs, shoes: everything skinned to skirt, thigh, calf, foot or toe
        /// bones) from another sample: cut from the hero's body, the donor's put on with its skirt
        /// bone chains rebuilt under our pelvis. The nine samples share one Biped skeleton and are
        /// normalised to the same height, so a Yuuka jacket over a Natsu skirt stands together.
        /// </summary>
        static void SwapLower(ChibiRig rig, SkinnedMeshRenderer body, string donorKey, SdLook k, Transform root, int layer)
            => Transplant(rig, body, donorKey, Lower, true, false, k, root, layer);

        /// <summary>The sample accessories, by the bones they ride (their own names in each sample).</summary>
        static readonly Dictionary<string, (string donor, string[] bones)> Accessories = new()
        {
            ["choker"] = ("kayoko_dress_ver_", new[] { "choker" }),
            ["nameplate"] = ("hayase_yuuka", new[] { "nameplate", "pocket" }),
            ["shawl"] = ("haruka", new[] { "shawl" }),
            ["ribbon"] = ("haruka", new[] { "ribborn" }),
            ["bag"] = ("hikari", new[] { "bag", "acc_01", "acc_02" }),
            ["bows"] = ("hatsune_miku", new[] { "ribbon_t" }),
        };

        static void Accessory(ChibiRig rig, SkinnedMeshRenderer body, string name, SdLook k, Transform root, int layer)
        {
            if (!Accessories.TryGetValue(name, out var a) || !Has(a.donor)) return;
            Transplant(rig, body, a.donor, b => { var n = b.ToLowerInvariant(); return a.bones.Any(x => n.Contains(x)); }, false, true, k, root, layer);
        }

        /// <summary>
        /// Moves the part of a donor sample skinned to the bones `take` accepts onto the hero: the
        /// donor-only bones (skirt chains, a choker's, a bag's) rebuilt under our skeleton by name,
        /// the donor's sheet in the hero's colours. `cutOwn` removes our own triangles of that kind
        /// first (the lower body). An accessory takes every vertex with some weight on its bones.
        /// </summary>
        static void Transplant(ChibiRig rig, SkinnedMeshRenderer body, string donorKey, System.Func<string, bool> take, bool cutOwn, bool accessory, SdLook k, Transform root, int layer)
        {
            var prefab = Prefab(donorKey); if (prefab == null) return;
            if (cutOwn)
            {
                var mesh = body.sharedMesh; var bw = mesh.boneWeights; var bones = body.bones;
                var isLow = bw.Select(w => take(Dominant(w, bones))).ToArray();
                var cut = Object.Instantiate(mesh);
                for (var s = 0; s < cut.subMeshCount; s++)
                {
                    var tris = cut.GetTriangles(s); var keep = new List<int>(tris.Length);
                    for (var t = 0; t < tris.Length; t += 3)
                        if (!(isLow[tris[t]] && isLow[tris[t + 1]] && isLow[tris[t + 2]])) { keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]); }
                    cut.SetTriangles(keep, s, false);
                }
                body.sharedMesh = cut;
            }

            var temp = new GameObject("donor").transform; temp.SetParent(root.parent, false);
            temp.position = root.position; temp.rotation = root.rotation;
            var dgo = Object.Instantiate(prefab, temp);
            var drs = dgo.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var dbody = drs.OrderByDescending(r => r.sharedMesh.subMeshCount).ThenByDescending(r => r.sharedMesh.vertexCount).First();
            dbody.updateWhenOffscreen = true;
            dgo.transform.localPosition = Vector3.zero; dgo.transform.localRotation = Quaternion.identity; dgo.transform.localScale = Vector3.one;
            var wb = dbody.bounds; var lo = temp.InverseTransformPoint(wb.min); var hi = temp.InverseTransformPoint(wb.max);
            var sc = SdRef.Height / Mathf.Max(1e-5f, hi.y - lo.y) * k.Scale;   // at the hero's own height, as the body
            dgo.transform.localScale = Vector3.one * sc;
            dgo.transform.localPosition = new Vector3(-(lo.x + hi.x) * 0.5f * sc, -lo.y * sc, -(lo.z + hi.z) * 0.5f * sc);
            var dpel = dgo.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Bip001 Pelvis");
            if (dpel != null) { var pl = temp.InverseTransformPoint(dpel.position); dgo.transform.localPosition -= new Vector3(pl.x, 0f, pl.z); }
            // the donor's pelvis at our pelvis height (the waistband meets the jacket); an accessory at
            // our NECK height instead (a choker, a lanyard, a shawl hang from there)
            var dneck = dgo.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Bip001 Neck");
            if (accessory && dneck != null && rig.Neck != null) dgo.transform.position += Vector3.up * (rig.Neck.position.y - dneck.position.y);
            else if (dpel != null && rig.Pelvis != null) dgo.transform.position += Vector3.up * (rig.Pelvis.position.y - dpel.position.y);

            var dm = dbody.sharedMesh; var dbw = dm.boneWeights; var dbones = dbody.bones;
            bool Rides(BoneWeight w)
            {
                if (!accessory) return take(Dominant(w, dbones));
                bool B(int i, float wt) => wt > 0.2f && i >= 0 && i < dbones.Length && dbones[i] != null && take(dbones[i].name);
                return B(w.boneIndex0, w.weight0) || B(w.boneIndex1, w.weight1) || B(w.boneIndex2, w.weight2) || B(w.boneIndex3, w.weight3);
            }
            var dLow = dbw.Select(Rides).ToArray();
            // a lower never brings the donor's kit hanging at the hips (Natsu's phone, a bag)
            if (!accessory)
                for (var i = 0; i < dLow.Length; i++)
                {
                    var n = Dominant(dbw[i], dbones).ToLowerInvariant();
                    if (n.Contains("acc") || n.Contains("phone") || n.Contains("bag")) dLow[i] = false;
                }
            var part = Object.Instantiate(dm);
            var dnames = dbody.sharedMaterials.Select(m => m ? m.name.ToLowerInvariant() : "").ToArray();
            for (var s = 0; s < part.subMeshCount; s++)
            {
                var tris = dm.GetTriangles(s); var keep = new List<int>();
                if (s < dnames.Length && dnames[s].Contains("body"))
                    for (var t = 0; t < tris.Length; t += 3)
                        if (accessory ? dLow[tris[t]] && dLow[tris[t + 1]] && dLow[tris[t + 2]] : (dLow[tris[t]] ? 1 : 0) + (dLow[tris[t + 1]] ? 1 : 0) + (dLow[tris[t + 2]] ? 1 : 0) >= 2) { keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]); }
                part.SetTriangles(keep, s, false);
            }

            // bones: ours by name; donor-only bones (its skirt chains) rebuilt under the nearest mapped parent
            var ours = rig.Root.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var map = new Dictionary<Transform, Transform>();
            Transform Map(Transform d)
            {
                if (d == null) return rig.Pelvis;
                if (map.TryGetValue(d, out var mm)) return mm;
                if (d.name.StartsWith("Bip001") && ours.TryGetValue(d.name, out var o)) return map[d] = o;
                var parent = Map(d.parent);
                var nb = new GameObject(d.name + "#low") { layer = layer }.transform;
                nb.SetParent(parent, true);
                nb.position = d.position; nb.rotation = d.rotation;
                return map[d] = nb;
            }
            var newBones = dbones.Select(Map).ToArray();
            var go = new GameObject((accessory ? "acc:" : "lower:") + donorKey) { layer = layer };
            go.transform.SetParent(rig.Model, true);
            go.transform.position = dbody.transform.position; go.transform.rotation = dbody.transform.rotation;
            var ls = dbody.transform.lossyScale; var ps = rig.Model.lossyScale;
            go.transform.localScale = new Vector3(ls.x / ps.x, ls.y / ps.y, ls.z / ps.z);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            var toMesh = go.transform.localToWorldMatrix;
            var bind = new Matrix4x4[newBones.Length];
            for (var i = 0; i < newBones.Length; i++) bind[i] = newBones[i].worldToLocalMatrix * toMesh;
            part.bindposes = bind;
            smr.sharedMesh = part; smr.bones = newBones; smr.rootBone = rig.Pelvis;
            var tex = SdSampleTex.For(donorKey, k, Sheet(donorKey, "body"), Sheet(donorKey, "hair"), Sheet(donorKey, "eyemouth"), null, paintedOk: false);
            // a lower is the hero's SKIRT: its cloth all in the bottom colour, the pleats' shading kept
            // (the outfit clusters put the top's colour on Natsu's skirt)
            var sheetL = accessory ? tex.Body : SdSampleTex.Tint(Sheet(donorKey, "body"), k.Bottom, donorKey + ":" + k.Id) ?? tex.Body;
            // the tint only on the skirt (skirt-chain and hip triangles); the legs and shoes keep the
            // outfit recolour, whose skin test is the sheet's own (the tint darkened shaded knees)
            var skirtSub = new bool[part.subMeshCount];
            if (!accessory && sheetL != tex.Body)
            {
                var subsN = part.subMeshCount; var extra = new List<List<int>>();
                var isSkirt = dbw.Select(w => { var n = Dominant(w, dbones).ToLowerInvariant(); return n.Contains("skirt") || n == "bip001 pelvis"; }).ToArray();
                var lists = new List<int>[subsN];
                for (var si = 0; si < subsN; si++)
                {
                    var tris = part.GetTriangles(si); var legs = new List<int>(); var sk = new List<int>();
                    for (var t = 0; t < tris.Length; t += 3)
                        (isSkirt[tris[t]] || isSkirt[tris[t + 1]] || isSkirt[tris[t + 2]] ? sk : legs).AddRange(new[] { tris[t], tris[t + 1], tris[t + 2] });
                    lists[si] = legs; extra.Add(sk);
                }
                part.subMeshCount = subsN * 2;
                for (var si = 0; si < subsN; si++) { part.SetTriangles(lists[si], si, false); part.SetTriangles(extra[si], subsN + si, false); }
                skirtSub = Enumerable.Range(0, subsN * 2).Select(i => i >= subsN).ToArray();
                smr.sharedMesh = part;
            }
            var mats = new Material[part.subMeshCount];
            for (var i = 0; i < mats.Length; i++)
            {
                var m = MeshKit.NewToon(0.005f, skirtSub[i] ? sheetL : tex.Body);
                m.SetFloat("_Cutoff", 0f); m.SetFloat("_ShadeStrength", 0.24f); m.SetColor("_ShadeTint", SdRefLook.WarmShade); m.SetFloat("_Rim", 0.1f);
                mats[i] = m;
            }
            smr.sharedMaterials = mats;
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rig.Renderers.Add(smr);
            Object.DestroyImmediate(temp.gameObject);
        }

        public static ChibiRig Build(string heroId, string key, Transform parent, int layer)
        {
            var prefab = Prefab(key);
            if (prefab == null) return null;
            var root = new GameObject("sdsample:" + heroId) { layer = layer }.transform;
            // built unparented, at the origin and scale 1: the battle spawns heroes at scale ~0 and pops
            // them in, and every fitted piece (headwear, trouser legs, hair volume) is sized in world units
            // at build — it grew a hundredfold with the pop. Parented once done.
            var go = Object.Instantiate(prefab, root);
            go.name = "model";
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

            var rends = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var body = rends.OrderByDescending(r => r.sharedMesh.subMeshCount).ThenByDescending(r => r.sharedMesh.vertexCount).First();
            // the face may be its own renderer; Face00/01 and Eyebrow02 are BA's expression alternates
            var faceEnv = System.Environment.GetEnvironmentVariable("SD_FACEPICK");
            var kept = rends.Where(r => r == body || (faceEnv != null ? r.name.Contains(faceEnv) : r.name.Contains("Face_Outline") && !r.name.Contains("Face0"))).ToList();
            foreach (var r in rends) if (!kept.Contains(r)) r.gameObject.SetActive(false);
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true)) mr.gameObject.SetActive(false);   // halos, props

            // 1.2 tall, feet at 0, centred on the hips (as SdRef)
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            body.updateWhenOffscreen = true;
            var wb = body.bounds;
            var lo = root.InverseTransformPoint(wb.min); var hi = root.InverseTransformPoint(wb.max);
            // the hero's own height against the cast (petite .. tall), the feet still on the floor
            var s = SdRef.Height / Mathf.Max(1e-5f, hi.y - lo.y) * (_raw ? 1f : SdLook.For(heroId).Scale);
            go.transform.localScale = Vector3.one * s;
            go.transform.localPosition = new Vector3(-(lo.x + hi.x) * 0.5f * s, -lo.y * s, -(lo.z + hi.z) * 0.5f * s);
            var all = go.GetComponentsInChildren<Transform>(true);
            Transform Find(string n) => all.FirstOrDefault(t => t.name == n);
            var pelvis = Find("Bip001 Pelvis");
            if (pelvis != null) { var pl = root.InverseTransformPoint(pelvis.position); go.transform.localPosition -= new Vector3(pl.x, 0f, pl.z); }

            if (!_raw) StripKit(body, Find("Bip001 Neck"), pelvis);
            var k = SdLook.For(heroId);
            var tex = _raw ? new SdSampleTex.Set { Body = Sheet(key, "body"), Hair = Sheet(key, "hair"), EyeMouth = Sheet(key, "eyemouth"), EyeMouthSrc = Sheet(key, "eyemouth"), Face = Sheet(key, "face") }
                           : SdSampleTex.For(key, k, Sheet(key, "body"), Sheet(key, "hair"), Sheet(key, "eyemouth"), Sheet(key, "face"));
            // the hero's own face from another sample (sdspec "face": its eyes, brows, face plate), in place of the body's
            SkinnedMeshRenderer donorFace = null; SdSampleTex.Set donorTex = null;
            var faceKey = _raw ? null : System.Environment.GetEnvironmentVariable("SD_FACE") ?? k.Face;
            if (!string.IsNullOrEmpty(faceKey) && faceKey != key && Has(faceKey))
            {
                donorFace = SwapFace(faceKey, body, kept, root, go.transform, all, layer);
                if (donorFace != null)
                {
                    kept.Add(donorFace);
                    donorTex = SdSampleTex.For(faceKey, k, Sheet(faceKey, "body"), Sheet(faceKey, "hair"), Sheet(faceKey, "eyemouth"), Sheet(faceKey, "face"));
                }
            }
            var face = tex.Face;
            var blinkBones = new List<(Transform bone, Transform lid, Vector3 drop)>();
            Vector3? mouthAt = null;
            var bodyTex = tex;
            foreach (var r in kept)
            {
                tex = r == donorFace ? donorTex : bodyTex; face = tex.Face;
                var names = r.sharedMaterials.Select(m => m ? m.name : "").ToArray();
                var em = tex.EyeMouthSrc;
                System.Func<Vector2, float> luma = em == null || !em.isReadable ? null : uv => { var c = em.GetPixelBilinear(uv.x, uv.y); return c.r * 0.3f + c.g * 0.59f + c.b * 0.11f; };
                var (mesh, parts, subNames) = SdFace.Split(r.sharedMesh, names, luma);
                r.sharedMesh = mesh;
                var mats = new Material[mesh.subMeshCount];
                for (var i = 0; i < mats.Length; i++)
                {
                    var n = subNames[i];
                    var eye = parts[i] is SdFace.Part.White or SdFace.Part.Iris or SdFace.Part.IrisFree or SdFace.Part.Highlight or SdFace.Part.Line or SdFace.Part.Mouth or SdFace.Part.Brow;
                    var sheet = n.Contains("eyemouth") ? tex.EyeMouth : n.Contains("hair") ? tex.Hair : n.Contains("face") || n.Contains("eyebrow") ? face : n.Contains("alpha") ? tex.Body : tex.Body;
                    // brows in the hair's own dark tone, as BA draws them (a donor face kept Mika's pink ones)
                    if (parts[i] == SdFace.Part.Brow && !_raw) sheet = SdGarment.SolidOf(Color.Lerp(k.Hair, Color.black, 0.45f));
                    var m = MeshKit.NewToon(eye ? 0f : 0.005f, sheet);
                    m.SetFloat("_Cutoff", 0f);
                    if (eye) { m.SetFloat("_OutlineWidth", 0f); m.SetFloat("_ShadeStrength", 0.02f); m.SetFloat("_Rim", 0f); }
                    else
                    {
                        m.SetFloat("_ShadeStrength", n.Contains("face") ? 0.06f : 0.24f);
                        m.SetColor("_ShadeTint", n.Contains("hair") ? SdRefLook.ShadeOf(k.Hair) : SdRefLook.WarmShade);
                        m.SetFloat("_Rim", 0.1f);
                    }
                    SdFace.Configure(m, parts[i], SdRef.Height);
                    if (System.Environment.GetEnvironmentVariable("SD_FACEDBG") == "1")
                    {
                        Debug.Log($"[facedbg] {key} {r.name} sub {i} '{n}' {parts[i]} tris {mesh.GetTriangles(i).Length / 3} q {m.renderQueue}");
                        var col = parts[i] switch { SdFace.Part.White => Color.green, SdFace.Part.Iris or SdFace.Part.IrisFree => Color.red, SdFace.Part.Highlight => Color.yellow, SdFace.Part.Line => Color.magenta, SdFace.Part.Mouth => Color.cyan, _ => Color.white };
                        if (col != Color.white) m.SetColor("_Color", col);
                        if (parts[i] == SdFace.Part.IrisFree) m.SetFloat("_ZTest", 8f);
                    }
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
                r.updateWhenOffscreen = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (!_raw) EyeBones(r, parts, Find("Bip001 Head"), root, blinkBones, layer);
                if (!_raw && mouthAt == null)
                {
                    // where the (hidden) mouth plate sits: the expression mouths go there
                    var mv = mesh.vertices; var l2w = r.transform.localToWorldMatrix; var acc = Vector3.zero; var cnt = 0;
                    for (var si = 0; si < mesh.subMeshCount && si < parts.Length; si++)
                        if (parts[si] == SdFace.Part.Mouth) foreach (var t in mesh.GetTriangles(si)) { acc += l2w.MultiplyPoint3x4(mv[t]); cnt++; }
                    if (cnt > 0) mouthAt = acc / cnt;
                }
            }

            var rig = new ChibiRig { Root = root, Height = SdRef.Height, Model3D = true, RefModel = true, Model = go.transform, ModelScale = go.transform.localScale, ModelPos = go.transform.localPosition };
            rig.Body = pelvis ?? root; rig.Head = Find("Bip001 Head") ?? rig.Body;
            rig.ArmL = Find("Bip001 L UpperArm"); rig.ArmR = Find("Bip001 R UpperArm");
            rig.LegL = Find("Bip001 L Thigh"); rig.LegR = Find("Bip001 R Thigh");
            rig.Spine = Find("Bip001 Spine1") ?? Find("Bip001 Spine");
            rig.ForearmL = Find("Bip001 L Forearm"); rig.ForearmR = Find("Bip001 R Forearm");
            rig.CalfL = Find("Bip001 L Calf"); rig.CalfR = Find("Bip001 R Calf");
            rig.HandL = Find("Bip001 L Hand"); rig.HandR = Find("Bip001 R Hand");
            rig.FootL = Find("Bip001 L Foot"); rig.FootR = Find("Bip001 R Foot");
            rig.Pelvis = pelvis;
            rig.ClavL = Find("Bip001 L Clavicle"); rig.ClavR = Find("Bip001 R Clavicle"); rig.Neck = Find("Bip001 Neck");
            rig.FingersL = new[] { "Bip001 L Finger0", "Bip001 L Finger01", "Bip001 L Finger1", "Bip001 L Finger11", "Bip001 L Finger2", "Bip001 L Finger21" }.Select(Find).ToArray();
            rig.FingersR = new[] { "Bip001 R Finger0", "Bip001 R Finger01", "Bip001 R Finger1", "Bip001 R Finger11", "Bip001 R Finger2", "Bip001 R Finger21" }.Select(Find).ToArray();
            if (rig.Pelvis != null) rig.PelvisRest = rig.Pelvis.localPosition;
            foreach (var r in kept) rig.Renderers.Add(r);
            rig.FaceRenderer = body;
            rig.EyeSub = -1;   // the layered eye is the sample's own: no sheet swaps — expressions shut the lids (SdBlink)

            var lowerKey = System.Environment.GetEnvironmentVariable("SD_LOWER") ?? k.Lower;
            if (!_raw && !string.IsNullOrEmpty(lowerKey) && lowerKey != key && Has(lowerKey)) SwapLower(rig, body, lowerKey, k, root, layer);
            var accEnv = System.Environment.GetEnvironmentVariable("SD_ACC") ?? k.Accessories;
            if (!_raw && !string.IsNullOrEmpty(accEnv)) foreach (var acc in accEnv.Split(',')) Accessory(rig, body, acc.Trim(), k, root, layer);
            if (!_raw) SdGarment.Apply(rig, k, rig.Renderers.OfType<SkinnedMeshRenderer>().Where(r => r == body || r.name.StartsWith("lower:")).ToList());
            if (!_raw) DressHair(rig, heroId, key, k, kept, layer);
            var hairEnv = System.Environment.GetEnvironmentVariable("SD_HAIR") ?? k.HairRecipe ?? "";
            if (!_raw) SdHeadwear.Apply(rig, k, root, layer, hairEnv.Contains("len=short") || hairEnv.Contains("len=bob"));

            var sh = new MeshKit.Builder();
            sh.Quad(new Vector3(0f, 0.004f, 0f), new Vector3(0.26f, 0f, 0f), new Vector3(0f, 0f, 0.18f), new Color(0.1f, 0.14f, 0.25f, 0.4f));
            MeshKit.Part("shadow", root, sh.Bake("shadow"), ChibiBuilder.ShadowMat, layer);
            if (k.Glasses || k.Sunglasses) SdRefProps.Glasses(rig, body, root, k.Sunglasses, k.GlassesStyle, k.GlassesColor, layer);
            if (!_raw) SdRefProps.HandProp(rig, root, SdRef.RoleOf(heroId), k, layer);

            SdPose.Apply(rig, Pose.Rest);
            if (rig.FootL != null && rig.FootR != null) rig.RestFootY = Mathf.Min(root.InverseTransformPoint(rig.FootL.position).y, root.InverseTransformPoint(rig.FootR.position).y);
            if (!_raw && blinkBones.Count > 0) { rig.Blink = SdBlink.Attach(root.gameObject, blinkBones.ToArray(), System.Environment.GetEnvironmentVariable("SD_LID")); if (mouthAt != null) rig.Blink.AddMouth(Find("Bip001 Head"), mouthAt.Value, root, layer); }
            root.SetParent(parent, false);   // before the secondary motion takes its first positions
            root.gameObject.AddComponent<SdSecondary>().Init(go.transform, "sample:" + key);
            return rig;
        }
    }

    public static partial class SdSampleHair { }

    /// <summary>
    /// The sample's sheets in the hero's colours, keeping every stroke of their shading: the hair
    /// by a luminance gradient map onto the hero's hair colour; the iris square (the right three
    /// quarters of the eye sheet) by hue onto the eye colour; the body by colour clusters — each
    /// saturated or dark cluster of the outfit onto one of the hero's outfit colours, skin kept.
    /// </summary>
    public static class SdSampleTex
    {
        public class Set { public Texture2D Body, Hair, EyeMouth, EyeMouthSrc, Face; }
        static readonly Dictionary<string, Set> Cache = new();

        public static Set For(string key, SdLook k, Texture2D body, Texture2D hair, Texture2D eyemouth, Texture2D face = null, bool paintedOk = true)
        {
            var id = key + ":" + k.Id + (SdSample.SkipPainted || !paintedOk ? ":raw" : "");
            if (Cache.TryGetValue(id, out var set)) return set;
            set = new Set { EyeMouthSrc = eyemouth };
            set.Hair = hair != null && hair.isReadable ? GradientMap(hair, k.Hair) : hair;
            set.EyeMouth = eyemouth != null && eyemouth.isReadable ? IrisHue(eyemouth, k.Eye) : eyemouth;
            set.Body = body != null && body.isReadable ? Outfit(body, k) : body;
            // the hero's own outfit painted from the illustration onto this sheet (Editor/SampleRepaint), when there is one
            var painted = SdSample.SkipPainted || !paintedOk ? null : Resources.Load<Texture2D>("Art/SDBase/painted/" + k.Id);
            if (painted != null) set.Body = painted;
            set.Face = face != null && face.isReadable ? Swatches(face, k.Hair) : face;
            return Cache[id] = set;
        }

        static float Luma(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

        static readonly Dictionary<string, Texture2D> Tints = new();

        /// <summary>Every cloth pixel of the sheet in one colour (its hue and saturation, the value
        /// carried by each pixel's ratio to the sheet's mean); skin left as it is.</summary>
        public static Texture2D Tint(Texture2D src, Color target, string id)
        {
            if (src == null || !src.isReadable) return null;
            if (Tints.TryGetValue(id, out var t) && t != null) return t;
            var px = src.GetPixels();
            Color.RGBToHSV(target, out var th, out var ts, out var tv);
            bool Skin(float h, float s, float v) => (h < 0.12f || h > 0.94f) && s > 0.04f && s < 0.5f && v > 0.5f;   // shaded skin too
            float sum = 0f; var n = 0;
            foreach (var c in px) { Color.RGBToHSV(c, out var h, out var s, out var v); if (!Skin(h, s, v)) { sum += v; n++; } }
            var mean = n > 0 ? sum / n : 0.5f;
            for (var i = 0; i < px.Length; i++)
            {
                Color.RGBToHSV(px[i], out var h, out var s, out var v);
                if (Skin(h, s, v)) continue;
                var ratio = Mathf.Clamp(v / Mathf.Max(0.05f, mean), 0.45f, 1.5f);
                var c = Color.HSVToRGB(th, ts, Mathf.Clamp01(tv * ratio));
                c.a = px[i].a; px[i] = c;
            }
            return Tints[id] = Copy(src, px, src.name + "+tint");
        }

        static Texture2D Copy(Texture2D src, Color[] px, string name)
        {
            var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels(px); t.Apply(true, !Application.isEditor);   // readable in the editor (SampleRepaint bakes from it)
            return t;
        }

        /// <summary>Luminance → the hero's colour: the sheet's darkest to a deep tone, its lightest to a near-white tint.</summary>
        static Texture2D GradientMap(Texture2D src, Color target)
        {
            var px = src.GetPixels();
            float lo = 1f, hi = 0f;
            foreach (var c in px) { var l = Luma(c); lo = Mathf.Min(lo, l); hi = Mathf.Max(hi, l); }
            Color.RGBToHSV(target, out var h, out var sat, out var v);
            // the hero's colour is the sheet's MID-TONE (most of a hair sheet sits there): shadows a
            // deeper tone of it, highlights a lighter one — never lifted to near-white, which turned
            // navy and black hair grey
            var dark = Color.HSVToRGB(h, Mathf.Clamp01(sat * 1.1f + 0.08f), Mathf.Clamp01(v * 0.5f));
            var light = Color.HSVToRGB(h, Mathf.Clamp01(sat * 0.7f), Mathf.Clamp01(v + (1f - v) * 0.45f));
            // where the sheet's mid-tone sits (its median luminance), so that maps to the colour
            var ls = px.Select(Luma).OrderBy(x => x).ToArray(); var mid = Mathf.InverseLerp(lo, hi, ls[ls.Length / 2]);
            // the range from the 8th to the 97th percentile: a sample's two-tone streak (Kayoko's black
            // half) or stray dark pixels would otherwise stretch the map and draw a stripe
            lo = ls[(int)(ls.Length * 0.08f)]; hi = Mathf.Max(lo + 0.05f, ls[(int)(ls.Length * 0.97f)]);
            mid = Mathf.InverseLerp(lo, hi, ls[ls.Length / 2]);
            for (var i = 0; i < px.Length; i++)
            {
                var t = Mathf.InverseLerp(lo, hi, Luma(px[i]));
                var c = t < mid ? Color.Lerp(dark, target, t / Mathf.Max(0.05f, mid)) : Color.Lerp(target, light, (t - mid) / Mathf.Max(0.05f, 1f - mid));
                c.a = px[i].a; px[i] = c;
            }
            return Copy(src, px, src.name + "+hair");
        }

        /// <summary>
        /// The face sheet's colour swatches (brow and lash colours — the sample's hair colour) onto
        /// the hero's hair, by value; skin and blush left.
        /// </summary>
        static Texture2D Swatches(Texture2D src, Color hair)
        {
            var px = src.GetPixels();
            Color.RGBToHSV(hair, out var hh, out var hs, out var hv);
            for (var i = 0; i < px.Length; i++)
            {
                Color.RGBToHSV(px[i], out var h, out var s, out var v);
                var skin = (h < 0.1f || h > 0.92f) && s < 0.5f && v > 0.55f;
                if (skin || s < 0.18f) continue;
                var c = Color.HSVToRGB(hh, Mathf.Clamp01(hs * 0.9f + 0.1f), Mathf.Clamp01(hv * Mathf.Lerp(0.55f, 1.1f, v)));
                c.a = px[i].a; px[i] = c;
            }
            return Copy(src, px, src.name + "+swatch");
        }

        /// <summary>The iris square's coloured pixels onto the eye colour's hue; the white and line blocks untouched.</summary>
        static Texture2D IrisHue(Texture2D src, Color eye)
        {
            var px = src.GetPixels(); var w = src.width;
            Color.RGBToHSV(eye, out var eh, out var es, out _);
            for (var i = 0; i < px.Length; i++)
            {
                if ((i % w) < w * 0.22f) continue;
                Color.RGBToHSV(px[i], out _, out var s, out var v);
                if (s < 0.08f) continue;
                var c = Color.HSVToRGB(eh, Mathf.Clamp01(Mathf.Lerp(s, es, 0.6f) + 0.05f), v);
                c.a = px[i].a; px[i] = c;
            }
            return Copy(src, px, src.name + "+iris");
        }

        /// <summary>
        /// The outfit's colours onto the hero's: pixels bucketed by hue (12 × saturated, plus dark
        /// and pale neutrals); skin-like pixels are left; the biggest coloured bucket takes the top
        /// colour, the next the bottom, the next the accent; darks follow the darkest of the hero's
        /// colours and pales stay pale. Shading is kept by carrying each pixel's value ratio.
        /// </summary>
        static Texture2D Outfit(Texture2D src, SdLook k)
        {
            var px = src.GetPixels();
            var bucket = new int[px.Length];
            var count = new int[14];
            for (var i = 0; i < px.Length; i++)
            {
                Color.RGBToHSV(px[i], out var h, out var s, out var v);
                var skin = h > 0.0f && h < 0.11f && s > 0.08f && s < 0.45f && v > 0.62f;
                var b = skin ? -1 : s > 0.22f && v > 0.18f ? Mathf.Min(11, (int)(h * 12f)) : v < 0.35f ? 12 : 13;
                bucket[i] = b; if (b >= 0) count[b]++;
            }
            var order = Enumerable.Range(0, 12).Where(b => count[b] > px.Length / 200).OrderByDescending(b => count[b]).ToList();
            var targets = new[] { k.Top, k.Bottom, k.Accent, k.Shirt };
            var map = new Dictionary<int, Color>();
            for (var j = 0; j < order.Count; j++) map[order[j]] = targets[Mathf.Min(j, targets.Length - 1)];
            // mean value per bucket, so the new colour sits at the same place in the shading
            var mean = new float[14]; var n = new int[14];
            for (var i = 0; i < px.Length; i++) { if (bucket[i] < 0) continue; Color.RGBToHSV(px[i], out _, out _, out var v); mean[bucket[i]] += v; n[bucket[i]]++; }
            for (var b = 0; b < 14; b++) mean[b] = n[b] > 0 ? mean[b] / n[b] : 0.5f;
            // Keep the sample's own contrast — that is most of why it reads well: the two biggest
            // coloured clusters take the hero's top / bottom HUE and saturation, their value only half
            // way toward the hero's; the smaller clusters (piping, accents) turn to the accent hue with
            // their own saturation and value; blacks, whites and skin stay the sample's.
            for (var i = 0; i < px.Length; i++)
            {
                var b = bucket[i];
                if (b < 0 || b >= 12 || !map.TryGetValue(b, out var target)) continue;
                var rank = order.IndexOf(b);
                Color.RGBToHSV(px[i], out _, out var s0, out var v);
                Color.RGBToHSV(target, out var th, out var ts, out var tv);
                Color c;
                if (rank < 2)
                {
                    var ratio = v / Mathf.Max(0.05f, mean[b]);
                    c = Color.HSVToRGB(th, Mathf.Lerp(s0, ts, 0.8f), Mathf.Clamp01(Mathf.Lerp(v, tv * ratio, 0.5f)));
                }
                else
                {
                    Color.RGBToHSV(k.Accent, out var ah, out _, out _);
                    c = Color.HSVToRGB(ah, s0, v);
                }
                c.a = px[i].a; px[i] = c;
            }
            return Copy(src, px, src.name + "+outfit");
        }
    }
}
