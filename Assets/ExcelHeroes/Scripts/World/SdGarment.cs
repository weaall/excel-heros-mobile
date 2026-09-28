using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// Reshapes a sample outfit into the hero's own: the sample tops and lowers are only nine and
    /// six, so the garments are cut and re-fitted per hero instead of worn as-is —
    ///   trousers   the skirt drawn up into a snug hip and the legs thickened into trouser legs,
    ///              painted the hero's bottom colour (41 of the illustrations wear trousers)
    ///   skirt      its length and flare scaled (sdspec "garment" skirt= / flare=)
    ///   legwear    knee socks on the calves, tights on the whole leg, or bare
    ///   shoes      painted the hero's shoe colour
    /// The vertices are moved in the bind pose (the pose a sample is built in), so the skinning
    /// and the skirt's secondary motion carry the new shape. Parameters: "skirt=0.8;flare=0.3;
    /// legs=tights;pants=wide" (SD_GARMENT overrides); unset ones follow the spec's bottomType /
    /// legwear.
    /// </summary>
    public static class SdGarment
    {
        enum Zone { None, Hip, Skirt, Thigh, Calf, Foot }

        static Zone ZoneOf(string bone)
        {
            var n = bone.ToLowerInvariant();
            if (n.Contains("skirt")) return Zone.Skirt;
            if (n.Contains("foot") || n.Contains("toe")) return Zone.Foot;
            if (n.Contains("calf") || n.Contains("knee")) return Zone.Calf;
            if (n.Contains("thigh")) return Zone.Thigh;
            if (n == "bip001 pelvis") return Zone.Hip;
            return Zone.None;
        }

        static readonly Dictionary<Color, Texture2D> Solids = new();

        public static Texture2D SolidOf(Color c) => Solid(c);

        static Texture2D Solid(Color c)
        {
            if (Solids.TryGetValue(c, out var t) && t != null) return t;
            t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "solid", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[16]; for (var i = 0; i < 16; i++) px[i] = c;
            t.SetPixels(px); t.Apply(false, true);
            return Solids[c] = t;
        }

        public static void Apply(ChibiRig rig, SdLook k, IEnumerable<SkinnedMeshRenderer> renderers)
        {
            var text = System.Environment.GetEnvironmentVariable("SD_GARMENT") ?? k.Garment ?? "";
            var addG = System.Environment.GetEnvironmentVariable("SD_GARMENTADD");   // preview: extra keys for this build
            if (!string.IsNullOrEmpty(addG)) text += ";" + addG;
            var p = new Dictionary<string, string>(); foreach (var kv0 in text.Split(';').Select(x => x.Split('=')).Where(x => x.Length == 2)) p[kv0[0].Trim()] = kv0[1].Trim();
            float F(string key, float fb) => p.TryGetValue(key, out var v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : fb;
            string S(string key, string fb) => p.TryGetValue(key, out var v) && v != "" ? v : fb;

            var pants = S("pants", k.Pants && !k.Dress ? "slim" : "none");
            var legs = pants != "none" ? "pants" : S("legs", k.Legwear switch { "socks" => "socks", "tights" => "tights", _ => "bare" });
            var skirtLen = pants != "none" ? 0.3f : F("skirt", 1f);
            // sleeves drawn in toward the arm (Yuuka's jacket has puffed sleeves that read as armour)
            var sleeve = Mathf.Clamp(F("sleeve", 1f), 0.6f, 1.2f);
            // a pencil skirt drawn in no tighter than the thighs it covers
            var flare = pants != "none" ? -0.25f : Mathf.Max(float.Parse(System.Environment.GetEnvironmentVariable("SD_FLAREMIN") ?? "-0.15", System.Globalization.CultureInfo.InvariantCulture), F("flare", 0f));
            if (rig.Pelvis == null) return;
            var pelvis = rig.Pelvis.position;
            var h = rig.Height;

            foreach (var r in renderers)
            {
                if (r == null || r.sharedMesh == null || !r.sharedMesh.isReadable) continue;
                var mesh = Object.Instantiate(r.sharedMesh); mesh.name = r.sharedMesh.name + "+garment";
                var bones = r.bones; var bw = mesh.boneWeights;
                if (bw.Length != mesh.vertexCount) continue;
                var zone = new Zone[bw.Length];
                var l2w = r.transform.localToWorldMatrix; var w2l = r.transform.worldToLocalMatrix;
                var v = mesh.vertices; var nrm = mesh.normals;
                for (var i = 0; i < bw.Length; i++)
                {
                    zone[i] = ZoneOf(Dom(bw[i], bones));
                    // the pelvis skin below the waist (the sample's under-shorts): the skirt's or trousers' colour
                    if (zone[i] == Zone.Hip && l2w.MultiplyPoint3x4(v[i]).y > pelvis.y + h * 0.03f) zone[i] = Zone.None;
                }
                if (!zone.Any(z => z != Zone.None)) continue;
                // a lower with no skirt chains (a dress hem skinned to the hips and thighs, Kayoko's,
                // Haruka's): its hem is cloth on the pelvis and thigh bones, so there the legwear only
                // goes on the SKIN texels, and the hips keep their own paint
                var skirted = zone.Any(z => z == Zone.Skirt);
                if (!skirted) for (var i = 0; i < zone.Length; i++) if (zone[i] == Zone.Hip) zone[i] = Zone.None;
                var uv = mesh.uv;
                var dbg = System.Environment.GetEnvironmentVariable("SD_GARMENTDBG") == "1";
                if (dbg)
                {
                    var names = new Dictionary<string, int>();
                    for (var i = 0; i < bw.Length; i++) { var d = Dom(bw[i], bones); names[d] = names.TryGetValue(d, out var c0) ? c0 + 1 : 1; }
                    Debug.Log($"[garment] {k.Id} {r.name}: " + string.Join(", ", names.OrderByDescending(x => x.Value).Select(x => $"{x.Key}={x.Value}")));
                }
                // the skirt: the top of it stays at the waist, everything below scaled by the length
                // and pushed out (flare) or in (a snug hip) with the depth
                var sk = Enumerable.Range(0, v.Length).Where(i => zone[i] == Zone.Skirt).ToArray();
                if (sk.Length > 0 && (Mathf.Abs(skirtLen - 1f) > 0.01f || Mathf.Abs(flare) > 0.01f))
                {
                    var ws = sk.Select(i => l2w.MultiplyPoint3x4(v[i])).ToArray();
                    var top = ws.Max(x => x.y); var bot = ws.Min(x => x.y); var span = Mathf.Max(1e-4f, top - bot);
                    // a pencil skirt has no pleats: the radius at each height and heading pulled to the mean of
                    // its band and eighth of the circle, which keeps the oval and irons out the folds
                    var iron = Mathf.Clamp01(-F("flare", 0f) / 0.3f) * (pants != "none" ? 0f : 1f);
                    const int bands = 10, sectors = 8;
                    var sum = new float[bands, sectors]; var cnt = new int[bands, sectors];
                    int Band(float y) => Mathf.Clamp((int)((top - y) / span * bands), 0, bands - 1);
                    int Sector(Vector3 q) => Mathf.Clamp((int)((Mathf.Atan2(q.z, q.x) / (Mathf.PI * 2f) + 0.5f) * sectors), 0, sectors - 1);
                    foreach (var w in ws) { var q = new Vector3(w.x - pelvis.x, 0f, w.z - pelvis.z); sum[Band(w.y), Sector(q)] += q.magnitude; cnt[Band(w.y), Sector(q)]++; }
                    for (var j = 0; j < sk.Length; j++)
                    {
                        var w = ws[j]; var t = (top - w.y) / span;
                        var radial = new Vector3(w.x - pelvis.x, 0f, w.z - pelvis.z);
                        if (iron > 0f && radial.sqrMagnitude > 1e-10f)
                        {
                            int bb = Band(w.y), ss = Sector(radial);
                            var mean = sum[bb, ss] / Mathf.Max(1, cnt[bb, ss]);
                            radial = radial.normalized * Mathf.Lerp(radial.magnitude, mean, iron * 0.85f);
                        }
                        var y = top - (top - w.y) * skirtLen;
                        radial *= 1f + flare * t;
                        v[sk[j]] = w2l.MultiplyPoint3x4(new Vector3(pelvis.x, y, pelvis.z) + radial);
                    }
                }
                // trouser legs: the leg skin thickened, a little wider at the ankle
                if (legs == "pants")
                {
                    var wide = pants == "wide" ? 0.018f : 0.006f;
                    for (var i = 0; i < v.Length; i++)
                    {
                        if (zone[i] != Zone.Thigh && zone[i] != Zone.Calf) continue;
                        var w = l2w.MultiplyPoint3x4(v[i]);
                        var n = l2w.MultiplyVector(nrm[i]); n.y = 0f; n = n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.zero;
                        var ankle = zone[i] == Zone.Calf ? Mathf.Clamp01(1f - (w.y - (pelvis.y - h * 0.34f)) / (h * 0.2f)) : 0f;
                        w += n * h * (0.009f + wide * ankle);
                        v[i] = w2l.MultiplyPoint3x4(w);
                    }
                }
                if (Mathf.Abs(sleeve - 1f) > 0.01f)
                {
                    // each vertex on an arm bone pulled toward that arm's bone line (shoulder → elbow → wrist)
                    (Vector3 a, Vector3 b)? Seg(Transform x, Transform y) => x != null && y != null ? (x.position, y.position) : null;
                    var segs = new[] { Seg(rig.ArmL, rig.ForearmL), Seg(rig.ForearmL, rig.HandL), Seg(rig.ArmR, rig.ForearmR), Seg(rig.ForearmR, rig.HandR) };
                    for (var i = 0; i < v.Length; i++)
                    {
                        var dn = Dom(bw[i], bones).ToLowerInvariant();
                        if (!(dn.Contains("upperarm") || dn.Contains("forearm") || dn.Contains("elbow"))) continue;
                        var w = l2w.MultiplyPoint3x4(v[i]);
                        Vector3? best = null; var bd = float.MaxValue;
                        foreach (var sg in segs)
                        {
                            if (sg == null) continue;
                            var (a, b) = sg.Value; var ab = b - a;
                            var t = Mathf.Clamp01(Vector3.Dot(w - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
                            var q = a + ab * t; var dd = (w - q).sqrMagnitude;
                            if (dd < bd) { bd = dd; best = q; }
                        }
                        if (best == null) continue;
                        v[i] = w2l.MultiplyPoint3x4(best.Value + (w - best.Value) * sleeve);
                    }
                }
                mesh.vertices = v; mesh.RecalculateBounds();

                // repaint by zone: new submeshes for the garment pieces, cut from the sheet-painted ones
                var mats = r.sharedMaterials.ToList();
                var groups = new Dictionary<Color, List<int>>();
                Color? Paint(Zone z) => dbg ? z switch { Zone.Hip => Color.red, Zone.Skirt => Color.yellow, Zone.Thigh => Color.green, Zone.Calf => Color.cyan, Zone.Foot => Color.magenta, _ => null } : z switch
                {
                    Zone.Skirt when legs == "pants" => k.Bottom,
                    Zone.Hip => k.Bottom,
                    // bare legs are skin: the samples' own stockings and garters painted there showed as marks
                    Zone.Thigh => legs == "pants" ? k.Bottom : legs == "tights" ? Color.Lerp(k.Socks, k.Skin, 0.12f) : k.Skin,
                    Zone.Calf => legs == "pants" ? k.Bottom : legs == "socks" ? k.Socks : legs == "tights" ? Color.Lerp(k.Socks, k.Skin, 0.12f) : k.Skin,
                    Zone.Foot => k.Shoes,
                    _ => null,
                };
                var subs = mesh.subMeshCount;
                var keep = new List<int>[subs];
                for (var s = 0; s < subs; s++)
                {
                    var tris = mesh.GetTriangles(s); keep[s] = new List<int>(tris.Length);
                    var eye = s < mats.Count && mats[s] != null && mats[s].renderQueue > 2001;   // the face layers
                    var sheet = s < mats.Count && mats[s] != null ? mats[s].mainTexture as Texture2D : null;
                    if (sheet != null && !sheet.isReadable) sheet = null;
                    bool Skin(int a, int b2, int c2)
                    {
                        if (sheet == null || uv.Length != v.Length) return true;
                        var col = sheet.GetPixelBilinear((uv[a].x + uv[b2].x + uv[c2].x) / 3f, (uv[a].y + uv[b2].y + uv[c2].y) / 3f);
                        Color.RGBToHSV(col, out var hh, out var ss, out var vv);
                        return (hh < 0.11f || hh > 0.95f) && ss > 0.06f && ss < 0.5f && vv > 0.6f;
                    }
                    for (var t = 0; t < tris.Length; t += 3)
                    {
                        // painted when all three corners are; the colour of the lowest corner's zone
                        // (so a knee or an ankle takes the calf's / the shoe's, not a band of skin)
                        Color? c = null;
                        if (!eye)
                        {
                            var z = zone[tris[t]]; var z1 = zone[tris[t + 1]]; var z2 = zone[tris[t + 2]];
                            var c0 = Paint(z); var c1 = Paint(z1); var c2 = Paint(z2);
                            if (c0 != null && c1 != null && c2 != null) c = (Zone)Mathf.Max((int)z, Mathf.Max((int)z1, (int)z2)) == z ? c0 : (Zone)Mathf.Max((int)z1, (int)z2) == z1 ? c1 : c2;
                            // on a skirt-less lower the thighs' cloth (the dress hem) is left as painted
                            if (c != null && !skirted && legs != "pants" && (z == Zone.Thigh || z1 == Zone.Thigh || z2 == Zone.Thigh) && !Skin(tris[t], tris[t + 1], tris[t + 2])) c = null;
                            else if (z == Zone.Hip || z1 == Zone.Hip || z2 == Zone.Hip) c = null;
                        }
                        if (c == null) { keep[s].Add(tris[t]); keep[s].Add(tris[t + 1]); keep[s].Add(tris[t + 2]); continue; }
                        if (!groups.TryGetValue(c.Value, out var g)) groups[c.Value] = g = new List<int>();
                        g.Add(tris[t]); g.Add(tris[t + 1]); g.Add(tris[t + 2]);
                    }
                }
                if (groups.Count > 0)
                {
                    mesh.subMeshCount = subs + groups.Count;
                    for (var s = 0; s < subs; s++) mesh.SetTriangles(keep[s], s, false);
                    var gi = subs;
                    foreach (var kv in groups)
                    {
                        mesh.SetTriangles(kv.Value, gi++, false);
                        var m = MeshKit.NewToon(0.0025f, Solid(kv.Key));   // a thinner hull: the full one poked through the knee's crease as black marks
                        m.SetFloat("_Cutoff", 0f); m.SetFloat("_ShadeStrength", 0.3f); m.SetColor("_ShadeTint", SdRefLook.ShadeOf(kv.Key)); m.SetFloat("_Rim", 0.12f);
                        mats.Add(m);
                    }
                }
                r.sharedMesh = mesh;
                r.sharedMaterials = mats.ToArray();
            }
        }

        static string Dom(BoneWeight w, Transform[] bones)
        {
            var i = w.boneIndex0; var m = w.weight0;
            if (w.weight1 > m) { i = w.boneIndex1; m = w.weight1; }
            if (w.weight2 > m) { i = w.boneIndex2; m = w.weight2; }
            if (w.weight3 > m) i = w.boneIndex3;
            return i >= 0 && i < bones.Length && bones[i] != null ? bones[i].name : "";
        }
    }
}
