using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// Editing the sample's hair per character. The sample hair is 44 sculpted pieces; measured
    /// (Assets/_Ref/Editor/Uv.cs): the long ponytail and the hanging back hair are the pieces
    /// whose box reaches below y 0.62 (of height) behind the head (z &lt; -0.03); the front fringe,
    /// the cap and the side locks are above the chin. Per style: short / bob / spiky keep only the
    /// cap and fringe; long keeps everything; others keep the cap, fringe and the back pieces down
    /// to their own length. Removal is by dropping triangles from the hair submesh.
    /// </summary>
    public static class SdRefHair
    {
        static readonly Dictionary<(Mesh, string), Mesh> Cache = new();
        /// <summary>
        /// The skull under the hair, in the sample's mesh space (z up, face at -y): sized from the
        /// face front (x ±0.0011, y -0.0011..-0.0002, z 0.0072..0.0090), the chin (z 0.0065) and
        /// the cap (z to 0.0100) — an ellipsoid set back so it stays behind the face front and only
        /// grazes the temples, which the side locks cover.
        /// </summary>
        public static readonly Vector3 HeadCentre = new(0f, 0.0006f, 0.0081f);
        public static readonly Vector3 HeadRadii = new(0.0015f, 0.0011f, 0.0017f);

        /// <summary>
        /// <paramref name="fringe"/>: 0 the sample's, 1 longer, 2 swept left, 3 swept right, 4 short and
        /// parted — the fringe tips (front vertices around the hairline) pulled down or sideways.
        /// <paramref name="ahoge"/>: keep the cowlick strand on the crown.
        /// </summary>
        public static void Apply(SkinnedMeshRenderer body, string style, int hairSub, int bodySub = 0, int fringe = 0, bool ahoge = true)
        {
            if (hairSub < 0) return;
            var src = body.sharedMesh;
            var key = (src, (style ?? "short") + ":" + fringe + ":" + (ahoge ? "a" : "-"));
            if (!Cache.TryGetValue(key, out var mesh))
            {
                mesh = Edit(src, hairSub, bodySub, style ?? "short", fringe, ahoge);
                Cache[key] = mesh;
            }
            body.sharedMesh = mesh;
        }

        static Mesh Edit(Mesh src, int hairSub, int bodySub, string style, int fringe, bool ahoge)
        {
            var keepBelow = style switch
            {
                "long" => 0.0f,
                "ponytail" => 0.35f, "twin" => 0.66f,
                "side" => 0.5f, "curly" => 0.52f, "bob" => 0.62f,
                _ => 0.66f,
            };
            if (keepBelow <= 0f && fringe == 0 && ahoge) return src;
            var twin = style == "twin";                          // the tail is moved to both sides instead of dropped
            var cropSides = style is "short" or "spiky";         // no chin-length side locks on short hair
            var keepTie = style is "ponytail" or "twin" or "bun";
            var keepBunch = style == "bun";                     // the tail's top bunch reads as a bun once the tail is gone

            // The mesh is in the FBX's native frame: bone_root is rotated 270° about X, so mesh
            // +Z is world UP and mesh +Y is world BACK (the face looks along mesh -Y). Height and
            // "behind the head" are read on those axes.
            var v = src.vertices;
            var zmin = v.Min(p => p.z); var H = v.Max(p => p.z) - zmin;
            var tris = src.GetTriangles(hairSub);
            var comps = Components(tris);
            var drop = new HashSet<int>();
            var tailVerts = new HashSet<int>();
            foreach (var c in comps)
            {
                var lo = c.Min(i => (v[i].z - zmin) / H);
                var hi = c.Max(i => (v[i].z - zmin) / H);
                var frontMost = c.Min(i => v[i].y / H);          // most negative y = furthest forward
                var behind = frontMost > -0.03f;                  // the cap and fringe reach the forehead; the tail does not
                // hanging hair: reaches below the style's line AND stays behind the face. The
                // tail's top bunch reaches the crown (hi = 1), so no ceiling test; the ribbon at
                // the crown (lo > 0.9, fully behind) goes with the tail unless the style ties hair.
                var tie = lo > 0.9f && frontMost > 0.1f;
                // the ribbon's loose tails: tiny pieces well behind the head
                var tail = c.Count < 12 && frontMost > 0.1f;
                if (keepBunch && hi > 0.97f) continue;
                // the cowlick: a small strand on the crown, in front
                if (!ahoge && c.Count < 30 && lo > 0.9f && frontMost < -0.1f) { foreach (var i in c) drop.Add(i); continue; }
                if (keepBelow <= 0f) continue;
                if (cropSides && !behind && lo < 0.70f && hi < 0.95f) { foreach (var i in c) drop.Add(i); continue; }
                if (behind && (lo < keepBelow || ((tie || tail) && !keepTie)))
                {
                    foreach (var i in c) drop.Add(i);
                    if (twin && (lo < keepBelow || hi > 0.97f)) foreach (var i in c) tailVerts.Add(i);
                }
            }
            var kept = new List<int>(tris.Length);
            for (var t = 0; t < tris.Length; t += 3)
            {
                if (drop.Contains(tris[t]) || drop.Contains(tris[t + 1]) || drop.Contains(tris[t + 2])) continue;
                kept.Add(tris[t]); kept.Add(tris[t + 1]); kept.Add(tris[t + 2]);
            }
            var m = Object.Instantiate(src);
            m.name = src.name + ":" + style;
            if (twin && tailVerts.Count > 0) kept.AddRange(Twin(m, tris, tailVerts, v, H));
            m.SetTriangles(kept.ToArray(), hairSub);
            if (fringe != 0) Fringe(m, kept, fringe, zmin, H);
            // the scrunchie on the crown is part of the body submesh: the only body piece that high
            if (!keepTie && bodySub >= 0)
            {
                var bt = src.GetTriangles(bodySub);
                var bdrop = new HashSet<int>();
                foreach (var c in Components(bt))
                    if (c.Min(i => (v[i].z - zmin) / H) > 0.9f) foreach (var i in c) bdrop.Add(i);
                var bkept = new List<int>(bt.Length);
                for (var t = 0; t < bt.Length; t += 3)
                {
                    if (bdrop.Contains(bt[t]) || bdrop.Contains(bt[t + 1]) || bdrop.Contains(bt[t + 2])) continue;
                    bkept.Add(bt[t]); bkept.Add(bt[t + 1]); bkept.Add(bt[t + 2]);
                }
                m.SetTriangles(bkept.ToArray(), bodySub);
            }
            Debug.Log($"[SdRefHair] {style}: H {H:F4} keepBelow {keepBelow} pieces {comps.Count} dropped verts {drop.Count} tris {tris.Length / 3} -> {kept.Count / 3}");
            return m;
        }

        /// <summary>
        /// The fringe variants: hair vertices in front of the face (y &lt; -0.06 H) between the brow
        /// and the hairline (z 0.66..0.82 H) are the fringe tips; weighted by how far down the
        /// tip they are, they move down (longer), sideways (swept) or up and apart (short, parted).
        /// </summary>
        static void Fringe(Mesh m, List<int> hairTris, int fringe, float zmin, float H)
        {
            var v = m.vertices;
            var idx = new HashSet<int>(hairTris);
            foreach (var i in idx)
            {
                var h = (v[i].z - zmin) / H;
                if (v[i].y > -0.06f * H || h < 0.62f || h > 0.83f) continue;
                var t = Mathf.Clamp01((0.83f - h) / 0.17f);            // 0 at the hairline .. 1 at the tips
                var p = v[i];
                switch (fringe)
                {
                    case 1: p.z -= t * 0.022f * H; break;                                          // longer (not over the eyes)
                    case 2: p.x -= t * 0.05f * H; p.z += t * 0.01f * H * Mathf.Abs(Mathf.Sin(p.x / H * 20f)); break;   // swept left
                    case 3: p.x += t * 0.05f * H; p.z += t * 0.01f * H * Mathf.Abs(Mathf.Sin(p.x / H * 20f)); break;   // swept right
                    case 4: p.z += t * 0.03f * H; p.x += t * Mathf.Sign(p.x) * 0.02f * H; break;  // short, parted
                }
                v[i] = p;
            }
            m.vertices = v;
        }

        /// <summary>
        /// Twin tails: the sample's single tail (its hanging pieces and the crown bunch) copied
        /// twice, to either side of the head and a little forward and lower, the copies keeping
        /// their bone weights so they swing with the head. Returns the copies' triangles.
        /// </summary>
        static List<int> Twin(Mesh m, int[] tris, HashSet<int> tailVerts, Vector3[] v, float H)
        {
            var N = m.normals; var UV = m.uv; var BW = m.boneWeights; var TG = m.tangents;
            var nv = new List<Vector3>(v); var nn = new List<Vector3>(N); var nuv = new List<Vector2>(UV);
            var nbw = new List<BoneWeight>(BW); var ntg = new List<Vector4>(TG);
            var outTris = new List<int>();
            var cx = tailVerts.Average(i => v[i].x);
            foreach (var side in new[] { -1f, 1f })
            {
                var map = new Dictionary<int, int>();
                var offset = new Vector3(side * H * 0.17f - cx, -H * 0.03f, -H * 0.05f);   // out to the side, forward, lower
                foreach (var i in tailVerts)
                {
                    map[i] = nv.Count;
                    var p = v[i] + offset;
                    p.x = cx + (p.x - cx) * 0.85f;                    // a little slimmer than the single tail
                    nv.Add(p); nn.Add(N[i]); nuv.Add(UV[i]);
                    if (BW.Length > 0) nbw.Add(BW[i]);
                    if (TG.Length > 0) ntg.Add(TG[i]);
                }
                for (var t = 0; t < tris.Length; t += 3)
                    if (tailVerts.Contains(tris[t]) && tailVerts.Contains(tris[t + 1]) && tailVerts.Contains(tris[t + 2]))
                    { outTris.Add(map[tris[t]]); outTris.Add(map[tris[t + 1]]); outTris.Add(map[tris[t + 2]]); }
            }
            m.SetVertices(nv); m.SetNormals(nn); m.SetUVs(0, nuv);
            if (TG.Length > 0) m.SetTangents(ntg);
            if (BW.Length > 0) m.boneWeights = nbw.ToArray();
            return outTris;
        }

        static List<List<int>> Components(int[] tris)
        {
            var parent = new Dictionary<int, int>();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            foreach (var i in tris) if (!parent.ContainsKey(i)) parent[i] = i;
            for (var t = 0; t < tris.Length; t += 3) { var a = Find(tris[t]); parent[Find(tris[t + 1])] = a; parent[Find(tris[t + 2])] = a; }
            return parent.Keys.ToList().GroupBy(Find).Select(g => g.ToList()).ToList();
        }
    }
}
