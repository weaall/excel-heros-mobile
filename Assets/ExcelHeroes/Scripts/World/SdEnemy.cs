using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// Enemies v3: office objects on the heroes' chibi skeleton. The skeleton is a sample body's
    /// (the same Bip001 rig the heroes wear, so every clip, pose and hit reaction the heroes have
    /// plays on an enemy too) with its skin hidden; on its bones ride rigid parts made of the
    /// object's own stuff — the design Gemini drew for us (tools/out/design/concept_*): the HEAD is
    /// the object with its face (tools/gen_enemyhead_gemini.py → the plush head mesh SD3DM/head_&lt;id&gt;,
    /// tools/mon3d_inflate.py --heads), the torso a panel box / paper stack / soft body / glowing
    /// core by its material, the limbs segments with dark joint balls at shoulder, elbow, hip and
    /// knee, mitten hands, chunky boots, and its signature detail (cables, springs, tape, bolts,
    /// tufts, glow rings) on top. Colours and material: Resources/Data/enemybody.json.
    ///
    /// The parts are built once per enemy type at the rest pose and remembered as (bone, local
    /// transform, mesh); every later spawn only instantiates the skeleton and hangs them back on.
    /// </summary>
    public static class SdEnemy
    {
        const string Skeleton = "hayase_yuuka_gym_ver_";

        [System.Serializable] class Row { public string id = "", material = "metal", main = "", second = "", dark = "", accent = "", part = "panel"; }
        [System.Serializable] class File { public List<Row> items = new(); }
        static Dictionary<string, Row> _rows;

        static Row RowOf(string id)
        {
            if (_rows == null)
            {
                var a = Resources.Load<TextAsset>("Data/enemybody");
                _rows = a != null ? JsonUtility.FromJson<File>(a.text).items.ToDictionary(r => r.id) : new Dictionary<string, Row>();
            }
            return id != null && _rows.TryGetValue(id, out var r) ? r : null;
        }

        public static bool Has(string id) => id != null && RowOf(id) != null && SdModel.Textured("head_" + id).mesh != null && SdSample.Has(Skeleton);

        sealed class Piece { public string Bone; public Vector3 Pos; public Quaternion Rot; public Vector3 Scale; public Mesh Mesh; public Material Mat; }
        static readonly Dictionary<string, List<Piece>> Built = new();

        public static ChibiRig Build(string id, Transform parent, int layer)
        {
            if (!Has(id)) return null;
            var rig = SdSample.BuildSkeleton(Skeleton, parent, layer, "sdenemy:" + id);
            if (rig == null) return null;
            var bones = rig.Root.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            if (!Built.TryGetValue(id, out var pieces))
            {
                pieces = Make(id, rig, bones);
                Built[id] = pieces;
            }
            foreach (var p in pieces)
            {
                if (!bones.TryGetValue(p.Bone, out var bone)) continue;
                var go = MeshKit.Part("part", bone, p.Mesh, p.Mat, layer);
                go.transform.localPosition = p.Pos; go.transform.localRotation = p.Rot; go.transform.localScale = p.Scale;
                rig.Renderers.Add(go.GetComponent<MeshRenderer>());
            }
            return rig;
        }

        // ---------------------------------------------------------------- the parts, at rest

        static List<Piece> Make(string id, ChibiRig rig, Dictionary<string, Transform> bones)
        {
            var row = RowOf(id);
            Color C(string hex, Color fb) { var c = MeshKit.Hex(hex, fb); c.a = 1f; return c; }
            var main = C(row.main, new Color(0.8f, 0.82f, 0.86f));
            var second = C(row.second, Color.Lerp(main, Color.white, 0.3f));
            var dark = C(row.dark, new Color(0.18f, 0.2f, 0.26f));
            var accent = C(row.accent, new Color(1f, 0.5f, 0.3f));
            var mat = row.material;
            var h = rig.Height;
            var root = rig.Root;
            var fwd = root.forward; var up = Vector3.up; var right = root.right;
            var list = new List<Piece>();
            var toon = MeshKit.NewToon(0.0035f); toon.SetFloat("_ShadeStrength", 0.26f); toon.SetFloat("_Rim", 0.12f);

            void Emit(Transform bone, MeshKit.Builder b, Vector3 pivot, Material m = null)
            {
                if (bone == null || b.Count == 0) return;
                var go = new GameObject("tmp").transform; go.position = pivot; go.rotation = root.rotation;
                go.SetParent(bone, true);
                list.Add(new Piece { Bone = bone.name, Pos = go.localPosition, Rot = go.localRotation, Scale = go.localScale, Mesh = b.Bake("enemy:" + id), Mat = m ?? toon });
                Object.DestroyImmediate(go.gameObject);
            }
            // builders work in the root's frame, relative to a pivot (x right, y up, z forward)
            Vector3 L(Vector3 world, Vector3 pivot) { var d = world - pivot; return new Vector3(Vector3.Dot(d, right), Vector3.Dot(d, up), Vector3.Dot(d, fwd)); }

            Transform B(string n) => bones.TryGetValue(n, out var t) ? t : null;
            var pelvis = rig.Pelvis; var spine = rig.Spine; var neck = rig.Neck; var head = rig.Head;
            if (pelvis == null || neck == null || head == null) return list;
            var neckP = neck.position; var pelP = pelvis.position;
            var shL = rig.ArmL.position; var shR = rig.ArmR.position;
            var span = Vector3.Distance(shL, shR);

            // ---- limbs: segments with their material's look
            void Segment(MeshKit.Builder b, Vector3 a, Vector3 c, float r0, float r1, Color col)
            {
                var d = c - a; var len = d.magnitude; if (len < 1e-4f) return;
                var rot = Quaternion.FromToRotation(Vector3.up, d / len);
                switch (mat)
                {
                    case "paper":
                        var n = Mathf.Max(3, Mathf.RoundToInt(len / (r0 * 0.75f)));
                        for (var i = 0; i < n; i++)
                        {
                            var t0 = i / (float)n; var r = Mathf.Lerp(r0, r1, t0) * (i % 2 == 0 ? 1f : 0.93f);
                            b.M = Matrix4x4.TRS(a + d * t0, rot * Quaternion.Euler(0f, i * 23f, (i % 3 - 1) * 4f), Vector3.one);
                            b.Frustum(Vector3.zero, r, len / n * 0.92f, r, i % 2 == 0 ? Color.Lerp(Color.white, col, 0.25f) : col, 1f, 10);
                        }
                        break;
                    case "soft":
                        b.M = Matrix4x4.TRS(a + d * 0.5f, rot, Vector3.one);
                        b.Ellipsoid(Vector3.zero, new Vector3((r0 + r1) * 0.5f, len * 0.5f + r0 * 0.4f, (r0 + r1) * 0.5f * 0.95f), col, 14);
                        break;
                    case "energy":
                        for (var i = 0; i < 2; i++)
                        {
                            b.M = Matrix4x4.TRS(a + d * (0.28f + i * 0.46f), rot, Vector3.one);
                            var r = Mathf.Lerp(r0, r1, 0.3f + i * 0.4f);
                            b.Ellipsoid(Vector3.zero, new Vector3(r, len * 0.2f, r), Color.Lerp(col, Color.white, 0.25f), 12);
                        }
                        break;
                    default:   // metal, wood: a box with a panel band
                        b.M = Matrix4x4.TRS(a + d * 0.5f, rot, Vector3.one);
                        b.Box(Vector3.zero, new Vector3((r0 + r1), len * 0.96f, (r0 + r1) * 0.86f), col);
                        b.Box(new Vector3(0f, len * 0.22f, 0f), new Vector3((r0 + r1) * 1.06f, len * 0.08f, (r0 + r1) * 0.92f), mat == "wood" ? Color.Lerp(col, dark, 0.5f) : Color.Lerp(col, dark, 0.35f));
                        break;
                }
                b.M = Matrix4x4.identity;
            }
            void Joint(MeshKit.Builder b, Vector3 p, float r)
            {
                b.M = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one);
                b.Ellipsoid(Vector3.zero, Vector3.one * r, mat == "energy" ? accent : Color.Lerp(dark, main, 0.3f), 12);
                b.M = Matrix4x4.identity;
            }

            float armR = h * 0.043f, legR = h * 0.054f;
            foreach (var side in new[] { -1, 1 })
            {
                var up_ = side < 0 ? rig.ArmL : rig.ArmR; var fore = side < 0 ? rig.ForearmL : rig.ForearmR; var hand = side < 0 ? rig.HandL : rig.HandR;
                var thigh = side < 0 ? rig.LegL : rig.LegR; var calf = side < 0 ? rig.CalfL : rig.CalfR; var foot = side < 0 ? rig.FootL : rig.FootR;
                if (up_ == null || fore == null || hand == null || thigh == null || calf == null || foot == null) continue;
                var sh = up_.position; var el = fore.position; var wr = hand.position;
                var hip = thigh.position; var kn = calf.position; var an = foot.position;

                // upper arm (+ shoulder ball), forearm (+ elbow ball)
                var b1 = new MeshKit.Builder();
                Segment(b1, sh - sh + Vector3.zero, L(el, sh) , armR * 1.1f, armR, main);
                Joint(b1, Vector3.zero, armR * 1.2f);
                Detail(b1, Vector3.zero, L(el, sh), armR, "arm");
                Emit(up_, b1, sh);
                var b2 = new MeshKit.Builder();
                Segment(b2, Vector3.zero, L(wr, el), armR, armR * 1.15f, second);
                Joint(b2, Vector3.zero, armR * 1.0f);
                Emit(fore, b2, el);
                // mitten hand: along the forearm's line past the wrist, a thumb on the inside
                var b3 = new MeshKit.Builder();
                var dirH = (wr - el).normalized; var hd = L(wr + dirH, wr).normalized;
                var hc = hd * armR * 1.4f;
                b3.M = Matrix4x4.TRS(hc, Quaternion.FromToRotation(Vector3.up, hd), Vector3.one);
                b3.Ellipsoid(Vector3.zero, new Vector3(armR * 1.55f, armR * 1.85f, armR * 1.25f), mat == "soft" ? second : dark == main ? second : Color.Lerp(main, Color.white, 0.2f), 12);
                b3.Ellipsoid(new Vector3(-side * armR * 1.2f, armR * 0.3f, armR * 0.5f), new Vector3(armR * 0.6f, armR * 0.9f, armR * 0.6f), Color.Lerp(main, Color.white, 0.1f), 8);
                b3.M = Matrix4x4.identity;
                Emit(hand, b3, wr);

                // thigh (+ hip ball), calf (+ knee ball)
                var b4 = new MeshKit.Builder();
                Segment(b4, Vector3.zero, L(kn, hip), legR * 1.15f, legR, main);
                Joint(b4, Vector3.zero, legR * 1.0f);
                Detail(b4, Vector3.zero, L(kn, hip), legR, "leg");
                Emit(thigh, b4, hip);
                var b5 = new MeshKit.Builder();
                Segment(b5, Vector3.zero, L(an, kn), legR, legR * 1.1f, second);
                Joint(b5, Vector3.zero, legR * 1.02f);
                Emit(calf, b5, kn);
                // the boot: from the ankle down to the floor, reaching forward
                var b6 = new MeshKit.Builder();
                var floor = root.position.y + rig.RestFootY;
                var ay = an.y - floor;
                var bw = legR * 2.3f; var bl = legR * 3.0f; var bh = Mathf.Max(legR * 2.2f, ay + legR * 0.9f);
                b6.Box(new Vector3(0f, -ay + bh * 0.5f, legR * 0.7f), new Vector3(bw, bh, bl), mat == "energy" ? second : dark);
                b6.Box(new Vector3(0f, -ay + legR * 0.18f, legR * 0.8f), new Vector3(bw * 1.06f, legR * 0.36f, bl * 1.04f), Color.Lerp(dark, Color.black, 0.4f));
                b6.Box(new Vector3(0f, -ay + bh * 0.72f, legR * 0.7f + bl * 0.5f), new Vector3(bw * 0.7f, legR * 0.25f, legR * 0.2f), accent);
                Emit(foot, b6, an);
            }

            // ---- torso on the chest bone: from the pelvis to the neck
            {
                var b = new MeshKit.Builder();
                var top = L(neckP, neckP); var bot = L(pelP, neckP);
                var tH = top.y - bot.y; var tW = span * 1.08f; var tD = tW * 0.7f;
                var c = new Vector3(0f, (top.y + bot.y) * 0.5f + tH * 0.04f, 0f);
                switch (mat)
                {
                    case "paper":
                        for (var i = 0; i < 6; i++)
                        {
                            b.M = Matrix4x4.TRS(c + new Vector3(0f, -tH * 0.42f + i * tH * 0.16f, 0f), Quaternion.Euler(0f, (i % 3 - 1) * 5f, 0f), Vector3.one);
                            b.Box(Vector3.zero, new Vector3(tW * (1f - i * 0.02f), tH * 0.15f, tD), i % 2 == 0 ? Color.Lerp(Color.white, main, 0.3f) : main);
                        }
                        b.M = Matrix4x4.identity;
                        b.Box(c + new Vector3(0f, tH * 0.05f, tD * 0.52f), new Vector3(tW * 0.55f, tH * 0.4f, tD * 0.08f), second);
                        break;
                    case "soft":
                        b.Ellipsoid(c, new Vector3(tW * 0.55f, tH * 0.58f, tD * 0.62f), main, 18);
                        b.Ellipsoid(c + new Vector3(0f, -tH * 0.06f, tD * 0.3f), new Vector3(tW * 0.36f, tH * 0.4f, tD * 0.36f), second, 14);
                        break;
                    case "energy":
                        b.Ellipsoid(c, new Vector3(tW * 0.5f, tH * 0.55f, tD * 0.55f), main, 18);
                        b.Ellipsoid(c + new Vector3(0f, 0f, tD * 0.3f), Vector3.one * tW * 0.2f, accent, 12);
                        break;
                    default:
                        b.Box(c, new Vector3(tW, tH, tD), main);
                        b.Box(c + new Vector3(0f, tH * 0.08f, tD * 0.5f), new Vector3(tW * 0.62f, tH * 0.5f, tD * 0.06f), second);
                        b.Box(c + new Vector3(tW * 0.18f, tH * 0.18f, tD * 0.54f), new Vector3(tW * 0.16f, tH * 0.1f, tD * 0.04f), accent);
                        b.Box(c + new Vector3(0f, -tH * 0.44f, 0f), new Vector3(tW * 1.04f, tH * 0.12f, tD * 1.04f), dark);
                        break;
                }
                TorsoDetail(b, c, tW, tH, tD);
                Emit(spine ?? pelvis, b, neckP);
                // the pelvis: a dark block under the torso
                var bp = new MeshKit.Builder();
                bp.Box(new Vector3(0f, -tH * 0.06f, 0f), new Vector3(tW * 0.8f, tH * 0.32f, tD * 0.9f), mat == "soft" ? second : dark);
                Emit(pelvis, bp, pelP);
            }

            // ---- the head: the object with its face, bottom at the neck
            {
                var (hm, hmat) = SdModel.Textured("head_" + id);
                if (hm != null)
                {
                    var bb = hm.bounds;
                    var want = (h - (neckP.y - root.position.y)) * 1.02f;
                    var s = want / Mathf.Max(1e-4f, bb.size.y);
                    s = Mathf.Min(s, h * 0.78f / Mathf.Max(1e-4f, bb.size.x));
                    var go = new GameObject("tmp").transform;
                    go.position = neckP + up * (-bb.min.y * s - h * 0.02f) - right * bb.center.x * s - fwd * bb.center.z * s;
                    go.rotation = root.rotation; go.localScale = Vector3.one * s;
                    go.SetParent(head, true);
                    list.Add(new Piece { Bone = head.name, Pos = go.localPosition, Rot = go.localRotation, Scale = go.localScale, Mesh = hm, Mat = hmat });
                    Object.DestroyImmediate(go.gameObject);
                    // a neck joint
                    var bn = new MeshKit.Builder();
                    bn.Frustum(Vector3.zero - up * h * 0.03f, h * 0.045f, h * 0.06f, h * 0.04f, dark, 1f, 12);
                    Emit(neck, bn, neckP);
                }
            }
            return list;

            // the signature detail on a limb
            void Detail(MeshKit.Builder b, Vector3 a, Vector3 c, float r, string where)
            {
                var d = c - a; var len = d.magnitude; if (len < 1e-4f) return;
                var rot = Quaternion.FromToRotation(Vector3.up, d / len);
                switch (row.part)
                {
                    case "spring":
                        // a coil round the segment
                        for (var i = 0; i < 18; i++)
                        {
                            var t = i / 18f; var ang = t * Mathf.PI * 2f * 3f;
                            b.M = Matrix4x4.TRS(a + d * (0.2f + t * 0.6f) + rot * new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r * 1.35f, Quaternion.identity, Vector3.one);
                            b.Ellipsoid(Vector3.zero, Vector3.one * r * 0.28f, Color.Lerp(Color.white, dark, 0.4f), 6);
                        }
                        break;
                    case "tape":
                        foreach (var t in new[] { 0.35f, 0.65f })
                        {
                            b.M = Matrix4x4.TRS(a + d * t, rot * Quaternion.Euler(0f, 0f, 8f), Vector3.one);
                            b.Frustum(Vector3.zero, r * 1.18f, len * 0.1f, r * 1.18f, accent, 1f, 10);
                        }
                        break;
                    case "glow":
                        b.M = Matrix4x4.TRS(a + d * 0.5f, rot, Vector3.one);
                        b.Frustum(Vector3.zero, r * 1.3f, len * 0.07f, r * 1.3f, Color.Lerp(accent, Color.white, 0.4f), 1f, 12);
                        break;
                    case "fur":
                        for (var i = 0; i < 5; i++)
                        {
                            var ang = i / 5f * Mathf.PI * 2f;
                            b.M = Matrix4x4.TRS(a + d * 0.9f + rot * new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r * 1.05f, rot, Vector3.one);
                            b.Ellipsoid(Vector3.zero, new Vector3(r * 0.45f, r * 0.6f, r * 0.45f), Color.Lerp(second, Color.white, 0.3f), 6);
                        }
                        break;
                    case "bolt":
                        b.M = Matrix4x4.TRS(a + d * 0.5f + rot * new Vector3(r * 1.05f, 0f, 0f), Quaternion.identity, Vector3.one);
                        b.Ellipsoid(Vector3.zero, Vector3.one * r * 0.4f, Color.Lerp(Color.white, dark, 0.3f), 8);
                        break;
                    case "cable":
                    case "paper":
                    case "panel":
                    default:
                        if (where == "arm")
                        {
                            // a cable along the arm's back
                            b.M = Matrix4x4.TRS(a + d * 0.5f + rot * new Vector3(0f, 0f, -r * 1.05f), rot, Vector3.one);
                            b.Frustum(new Vector3(0f, -len * 0.45f, 0f), r * 0.3f, len * 0.9f, r * 0.3f, dark, 1f, 8);
                        }
                        break;
                }
                b.M = Matrix4x4.identity;
            }

            void TorsoDetail(MeshKit.Builder b, Vector3 c, float tW, float tH, float tD)
            {
                switch (row.part)
                {
                    case "paper":
                        // a sheet sticking out of the chest, as the copier concept
                        b.M = Matrix4x4.TRS(c + new Vector3(0f, tH * 0.22f, tD * 0.56f), Quaternion.Euler(-24f, 0f, 0f), Vector3.one);
                        b.Box(new Vector3(0f, -tH * 0.18f, 0f), new Vector3(tW * 0.5f, tH * 0.42f, tD * 0.03f), Color.white);
                        for (var i = 0; i < 4; i++) b.Box(new Vector3(0f, -tH * (0.06f + i * 0.08f), tD * 0.02f), new Vector3(tW * 0.36f, tH * 0.02f, tD * 0.01f), Color.Lerp(Color.white, dark, 0.5f));
                        break;
                    case "cable":
                        // cables looping from the chest round to the back
                        foreach (var sx in new[] { -1f, 1f })
                        {
                            b.M = Matrix4x4.TRS(c + new Vector3(sx * tW * 0.25f, -tH * 0.25f, tD * 0.5f), Quaternion.Euler(0f, 0f, 90f), Vector3.one);
                            b.Frustum(new Vector3(0f, -tW * 0.1f, 0f), tD * 0.06f, tW * 0.2f, tD * 0.06f, dark, 1f, 8);
                        }
                        break;
                    case "glow":
                        b.M = Matrix4x4.TRS(c + new Vector3(0f, 0f, tD * 0.5f), Quaternion.Euler(90f, 0f, 0f), Vector3.one);
                        b.Frustum(Vector3.zero, tW * 0.2f, tD * 0.05f, tW * 0.2f, Color.Lerp(accent, Color.white, 0.5f), 1f, 16);
                        break;
                    case "bolt":
                        foreach (var sx in new[] { -1f, 1f }) { b.M = Matrix4x4.TRS(c + new Vector3(sx * tW * 0.42f, tH * 0.38f, tD * 0.5f), Quaternion.identity, Vector3.one); b.Ellipsoid(Vector3.zero, Vector3.one * tD * 0.1f, Color.Lerp(Color.white, dark, 0.3f), 8); }
                        break;
                }
                b.M = Matrix4x4.identity;
            }
        }
    }
}
