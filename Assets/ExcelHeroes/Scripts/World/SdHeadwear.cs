using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The hero's own head accessories from the illustration (sdspec "head", tools/sdspec_head_gemini.py):
    /// hair clips, a headband, a bow, a flower, a beret, a cap, a headset, a crown, a bandana, an
    /// ahoge, earrings — built from primitives and FITTED to the hair the hero actually wears: the
    /// hair's surface is measured along each direction from the middle of the skull, so a clip sits
    /// on the fringe whatever its volume, a band follows the crown. Parented to the head bone.
    /// "hairclip:#f5c542:left,ahoge" (SD_HEAD overrides).
    /// </summary>
    public static class SdHeadwear
    {
        public static void Apply(ChibiRig rig, SdLook k, Transform root, int layer, bool shortHair)
        {
            var text = System.Environment.GetEnvironmentVariable("SD_HEAD") ?? k.Head ?? "";
            if (string.IsNullOrEmpty(text) || rig.Head == null) return;
            // the hair surface, at rest (the hair was just skinned in its bind pose)
            var pts = new List<Vector3>();
            foreach (var r in rig.Renderers)
            {
                if (r == null || !r.name.StartsWith("hair:")) continue;
                Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (m == null || !m.isReadable) continue;
                var l2w = r.transform.localToWorldMatrix;
                var tris = Enumerable.Range(0, m.subMeshCount).SelectMany(i => m.GetTriangles(i)).Distinct();
                var v = m.vertices;
                foreach (var i in tris) pts.Add(l2w.MultiplyPoint3x4(v[i]));
            }
            var h = rig.Height;
            var c = rig.Head.position + Vector3.up * h * 0.1f;
            var right = root.right; var up = root.up; var fwd = root.forward;
            var local = pts.Select(p => { var d = p - c; return new Vector3(Vector3.Dot(d, right), Vector3.Dot(d, up), Vector3.Dot(d, fwd)); }).ToArray();
            // the crown's height without the odd stray strand (an ahoge, a tuft): the 98th percentile
            var ys = local.Select(p => p.y).OrderBy(y => y).ToArray();
            var crownR = ys.Length > 0 ? ys[(int)(ys.Length * 0.98f)] : h * 0.2f;
            // how far the hair reaches along a direction (head frame: x right, y up, z forward)
            float Reach(Vector3 dir)
            {
                dir.Normalize(); var best = 0f;
                foreach (var p in local)
                {
                    var len = p.magnitude; if (len < 1e-5f) continue;
                    if (Vector3.Dot(p / len, dir) > 0.975f && len > best) best = len;
                }
                return best > 0f ? best : crownR;
            }
            Vector3 On(Vector3 dir, float lift = 1f) => dir.normalized * Reach(dir) * lift;

            var b = new MeshKit.Builder();
            foreach (var item in text.Split(','))
            {
                var f = item.Trim().Split(':');
                var kind = f[0];
                var col = f.Length > 1 && f[1].StartsWith("#") ? MeshKit.Hex(f[1], k.Accent) : k.Accent; col.a = 1f;
                var side = f.Length > 2 ? f[2] : f.Length > 1 && !f[1].StartsWith("#") ? f[1] : "";
                // the character's left is the root's −x
                var sx = side == "right" ? 1f : -1f;
                var both = side == "both";
                var R = crownR;
                switch (kind)
                {
                    case "hairclip":
                        foreach (var s in both ? new[] { -1f, 1f } : new[] { sx })
                            for (var j = 0; j < 2; j++)
                            {
                                var dir = new Vector3(s * 0.62f, 0.5f - j * 0.16f, 0.62f);
                                var p = On(dir, 1.01f);
                                b.M = Matrix4x4.TRS(p, Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, s * (35f + j * 12f)), Vector3.one);
                                b.Box(Vector3.zero, new Vector3(R * 0.26f, R * 0.055f, R * 0.04f), col);
                            }
                        break;
                    case "ribbon":
                        foreach (var s in both ? new[] { -1f, 1f } : new[] { sx })
                        {
                            var dir = side == "back" ? new Vector3(0f, 0.35f, -1f) : new Vector3(s * 0.75f, 0.6f, -0.15f);
                            Bow(b, On(dir, 1.02f), dir, R * (both ? 0.3f : 0.38f), col);
                        }
                        break;
                    case "flower":
                        foreach (var s in both ? new[] { -1f, 1f } : new[] { sx })
                        {
                            var dir = new Vector3(s * 0.8f, 0.42f, 0.3f);
                            var p = On(dir, 1.01f);
                            b.M = Matrix4x4.TRS(p, Quaternion.LookRotation(dir.normalized, Vector3.up), Vector3.one);
                            for (var i = 0; i < 5; i++)
                            {
                                var a = i / 5f * Mathf.PI * 2f;
                                b.Ellipsoid(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * R * 0.1f, new Vector3(R * 0.085f, R * 0.085f, R * 0.035f), col, 8);
                            }
                            b.Ellipsoid(new Vector3(0f, 0f, R * 0.03f), Vector3.one * R * 0.055f, new Color(0.98f, 0.86f, 0.35f), 8);
                        }
                        break;
                    case "headband":
                        Band(b, On, R * 0.07f, R * 0.035f, col, 0.35f, 1.02f);
                        break;
                    case "headset":
                        {
                            var dark = new Color(0.16f, 0.17f, 0.22f);
                            Band(b, On, R * 0.06f, R * 0.04f, dark, 0f, 1.04f);
                            foreach (var s in new[] { -1f, 1f })
                            {
                                var p = On(new Vector3(s, -0.15f, 0.05f), 1.02f);
                                b.M = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one);
                                b.Ellipsoid(Vector3.zero, new Vector3(R * 0.12f, R * 0.2f, R * 0.2f), dark, 12);
                                b.Ellipsoid(new Vector3(s * R * 0.09f, 0f, 0f), new Vector3(R * 0.05f, R * 0.14f, R * 0.14f), col, 10);
                                if (s == sx || both)
                                {
                                    var a1 = new Vector3(-s * R * 0.35f, -R * 0.45f, R * 0.7f);
                                    Tube(b, new[] { Vector3.zero, a1 * 0.5f + new Vector3(0f, 0f, R * 0.1f), a1 }, R * 0.022f, R * 0.022f, dark);
                                    b.M = Matrix4x4.TRS(p + a1, Quaternion.identity, Vector3.one);
                                    b.Ellipsoid(Vector3.zero, new Vector3(R * 0.05f, R * 0.05f, R * 0.07f), col, 8);
                                }
                            }
                        }
                        break;
                    case "neckphones":
                        {
                            // headphones resting round the neck: the cups under the jaw, the band behind the nape
                            var dark = new Color(0.16f, 0.17f, 0.22f);
                            var neckY = rig.Neck != null ? Vector3.Dot(rig.Neck.position - c, up) : -R * 0.9f;
                            var y0 = neckY + R * 0.14f;   // at the jaw line, over the collar
                            var path = new Vector3[13];
                            for (var i = 0; i < path.Length; i++)
                            {
                                var a = Mathf.Lerp(-1.6f, 1.6f, i / (float)(path.Length - 1));
                                path[i] = new Vector3(Mathf.Sin(a) * R * 0.5f, y0 + R * 0.04f, -Mathf.Cos(a) * R * 0.4f + R * 0.05f);
                            }
                            Tube(b, path, R * 0.035f, R * 0.035f, dark);
                            foreach (var s in new[] { -1f, 1f })
                            {
                                b.M = Matrix4x4.TRS(new Vector3(s * R * 0.5f, y0, R * 0.14f), Quaternion.Euler(0f, s * 25f, s * 20f), Vector3.one);
                                b.Ellipsoid(Vector3.zero, new Vector3(R * 0.1f, R * 0.17f, R * 0.17f), dark, 12);
                                b.Ellipsoid(new Vector3(0f, 0f, R * 0.07f), new Vector3(R * 0.12f, R * 0.12f, R * 0.04f), col, 10);
                            }
                        }
                        break;
                    case "beret":
                        {
                            var p = On(new Vector3(-sx * 0.25f, 1f, -0.1f), 0.84f);
                            b.M = Matrix4x4.TRS(p, Quaternion.Euler(-8f, 0f, sx * 16f), Vector3.one);
                            b.Ellipsoid(Vector3.zero, new Vector3(R * 1.0f, R * 0.32f, R * 0.95f), col, 20);
                            b.Frustum(new Vector3(0f, R * 0.28f, 0f), R * 0.04f, R * 0.1f, R * 0.02f, MeshKit.Shade(col, 0.85f), 1f, 8);
                        }
                        break;
                    case "cap":
                        {
                            // the dome as wide as the hair at the temples, its top just over the crown
                            var wx = (Reach(new Vector3(1f, 0.45f, 0f)) + Reach(new Vector3(-1f, 0.45f, 0f))) * 0.5f * 0.95f;
                            var wz = Reach(new Vector3(0f, 0.45f, 1f)) * 0.98f;
                            var capH = R * 0.5f;
                            var p = Vector3.up * (R * 1.03f - capH);
                            b.M = Matrix4x4.TRS(p, Quaternion.Euler(-6f, 0f, 0f), Vector3.one);
                            b.Ellipsoid(Vector3.zero, new Vector3(wx, capH, wz), col, 20, _ => Mathf.PI * 0.5f);
                            var brim = MeshKit.Shade(col, 0.85f);
                            b.Grid(8, 1, (u, t) =>
                            {
                                var a = Mathf.Lerp(-1.1f, 1.1f, u); var rr = Mathf.Lerp(wz * 0.92f, wz * 1.38f, t);
                                return (new Vector3(Mathf.Sin(a) * rr * wx / wz * 0.9f, -t * R * 0.06f, Mathf.Cos(a) * rr), Vector3.up, new Vector2(u, t));
                            }, brim);
                            b.Grid(8, 1, (u, t) =>
                            {
                                var a = Mathf.Lerp(-1.1f, 1.1f, u); var rr = Mathf.Lerp(wz * 0.92f, wz * 1.38f, t);
                                return (new Vector3(Mathf.Sin(a) * rr * wx / wz * 0.9f, -t * R * 0.06f - R * 0.02f, Mathf.Cos(a) * rr), Vector3.down, new Vector2(u, t));
                            }, MeshKit.Shade(col, 0.7f));
                            b.Ellipsoid(new Vector3(0f, capH, 0f), Vector3.one * R * 0.06f, brim, 8);
                        }
                        break;
                    case "bandana":
                        {
                            // a kerchief laid ON the hair: a shell over the crown that follows the hair's own
                            // surface (a rigid dome stood off fluffy hair like a mushroom cap), down to the
                            // brow at the front and lower behind, knotted at the nape
                            const int nu = 20, nv = 8;
                            b.Grid(nu, nv, (u, t) =>
                            {
                                var phi = u * Mathf.PI * 2f;
                                var back = Mathf.Max(0f, -Mathf.Cos(phi));             // 1 behind, 0 in front
                                var th = t * Mathf.Lerp(0.95f, 1.35f, back);         // from the crown down
                                var dir = new Vector3(Mathf.Sin(th) * Mathf.Sin(phi), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Cos(phi));
                                var p = On(dir, 1.03f);
                                return (p, dir, new Vector2(u, t));
                            }, col);
                            var back2 = On(new Vector3(0f, 0.1f, -1f), 1.04f);
                            b.M = Matrix4x4.TRS(back2, Quaternion.identity, Vector3.one);
                            b.Ellipsoid(Vector3.zero, Vector3.one * R * 0.09f, MeshKit.Shade(col, 0.85f), 8);
                            foreach (var s2 in new[] { -1f, 1f })
                            {
                                b.M = Matrix4x4.TRS(back2 + new Vector3(s2 * R * 0.08f, -R * 0.14f, -R * 0.02f), Quaternion.Euler(0f, 0f, s2 * 20f), Vector3.one);
                                b.Box(Vector3.zero, new Vector3(R * 0.1f, R * 0.26f, R * 0.02f), col);
                            }
                        }
                        break;
                    case "crown":
                        {
                            var p = On(Vector3.up, 0.94f);
                            b.M = Matrix4x4.TRS(p, Quaternion.Euler(-10f, 0f, 0f), Vector3.one);
                            var rr = R * 0.34f;
                            b.Frustum(Vector3.zero, rr, R * 0.13f, rr * 1.08f, col, 1f, 16);
                            for (var i = 0; i < 5; i++)
                            {
                                var a = i / 5f * Mathf.PI * 2f + Mathf.PI / 2f;
                                b.Ellipsoid(new Vector3(Mathf.Cos(a) * rr * 1.05f, R * 0.18f, Mathf.Sin(a) * rr * 1.05f), new Vector3(R * 0.045f, R * 0.1f, R * 0.045f), col, 8);
                                b.Ellipsoid(new Vector3(Mathf.Cos(a) * rr * 1.1f, R * 0.07f, Mathf.Sin(a) * rr * 1.1f), Vector3.one * R * 0.03f, new Color(0.85f, 0.2f, 0.3f), 6);
                            }
                        }
                        break;
                    case "ahoge":
                        {
                            var p0 = On(new Vector3(0.08f, 1f, 0.12f), 0.97f);
                            var hair = k.Hair; hair.a = 1f;
                            Tube(b, new[] { p0, p0 + new Vector3(R * 0.04f, R * 0.25f, R * 0.02f), p0 + new Vector3(R * 0.16f, R * 0.42f, R * 0.1f), p0 + new Vector3(R * 0.3f, R * 0.4f, R * 0.2f) },
                                 R * 0.055f, R * 0.008f, hair);
                        }
                        break;
                    case "earring":
                        if (!shortHair) break;
                        foreach (var s in both ? new[] { -1f, 1f } : new[] { sx })
                        {
                            var p = On(new Vector3(s, -0.55f, 0.1f), 0.92f);
                            b.M = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one);
                            b.Ellipsoid(Vector3.zero, Vector3.one * R * 0.04f, col, 8);
                            b.Ellipsoid(new Vector3(0f, -R * 0.08f, 0f), new Vector3(R * 0.035f, R * 0.06f, R * 0.035f), col, 8);
                        }
                        break;
                }
                b.M = Matrix4x4.identity;
            }
            if (b.Count == 0) return;
            var mat = MeshKit.NewToon(0.003f);
            mat.SetFloat("_ShadeStrength", 0.22f);
            var go = MeshKit.Part("headwear", rig.Head, b.Bake("headwear"), mat, layer);
            var ls = rig.Head.lossyScale;
            go.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
            go.transform.position = c; go.transform.rotation = root.rotation;
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
        }

        /// <summary>A band over the crown from ear to ear, `tilt` forward, `lift` proud of the hair.</summary>
        static void Band(MeshKit.Builder b, System.Func<Vector3, float, Vector3> on, float width, float thick, Color col, float tilt, float lift)
        {
            const int n = 18;
            var path = new Vector3[n + 1];
            for (var i = 0; i <= n; i++)
            {
                var a = Mathf.Lerp(-1.5f, 1.5f, i / (float)n);
                path[i] = on(new Vector3(Mathf.Sin(a), Mathf.Cos(a), tilt), lift);
            }
            b.Grid(n, 4, (u, t) =>
            {
                var i = Mathf.Min(n - 1, (int)(u * n)); var f = u * n - i;
                var p = Vector3.Lerp(path[i], path[i + 1], f);
                var outward = p.normalized; var along = (path[i + 1] - path[i]).normalized;
                var across = Vector3.Cross(along, outward).normalized;
                var ang = t * Mathf.PI * 2f;
                var nrm = outward * Mathf.Cos(ang) + across * Mathf.Sin(ang);
                return (p + outward * Mathf.Cos(ang) * thick + across * Mathf.Sin(ang) * width, nrm, new Vector2(u, t));
            }, col);
        }

        /// <summary>A tube through the points, its radius from r0 to r1.</summary>
        static void Tube(MeshKit.Builder b, Vector3[] path, float r0, float r1, Color col)
        {
            var n = path.Length - 1;
            b.Grid(n * 4, 8, (u, t) =>
            {
                var x = u * n; var i = Mathf.Min(n - 1, (int)x); var f = x - i;
                var p = Vector3.Lerp(path[i], path[i + 1], f);
                var along = (path[i + 1] - path[i]).normalized;
                var side = Vector3.Cross(along, Mathf.Abs(along.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
                var up2 = Vector3.Cross(side, along);
                var ang = t * Mathf.PI * 2f;
                var nrm = side * Mathf.Cos(ang) + up2 * Mathf.Sin(ang);
                return (p + nrm * Mathf.Lerp(r0, r1, u), nrm, new Vector2(u, t));
            }, col);
        }

        static void Bow(MeshKit.Builder b, Vector3 p, Vector3 dir, float size, Color col)
        {
            var dark = MeshKit.Shade(col, 0.82f);
            var rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            foreach (var s in new[] { -1f, 1f })
            {
                b.M = Matrix4x4.TRS(p + rot * new Vector3(s * size * 0.55f, size * 0.1f, 0f), rot * Quaternion.Euler(0f, 0f, s * 14f), Vector3.one);
                b.Ellipsoid(Vector3.zero, new Vector3(size * 0.55f, size * 0.32f, size * 0.16f), col, 12);
                b.M = Matrix4x4.TRS(p + rot * new Vector3(s * size * 0.25f, -size * 0.5f, 0f), rot * Quaternion.Euler(0f, 0f, s * -16f), Vector3.one);
                b.Box(Vector3.zero, new Vector3(size * 0.22f, size * 0.75f, size * 0.05f), dark);
            }
            b.M = Matrix4x4.TRS(p, rot, Vector3.one);
            b.Ellipsoid(Vector3.zero, Vector3.one * size * 0.2f, dark, 8);
        }
    }
}
