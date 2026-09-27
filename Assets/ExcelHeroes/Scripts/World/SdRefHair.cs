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

        public static void Apply(SkinnedMeshRenderer body, string style, int hairSub, int bodySub = 0)
        {
            if (hairSub < 0) return;
            var src = body.sharedMesh;
            var key = (src, style ?? "short");
            if (!Cache.TryGetValue(key, out var mesh))
            {
                mesh = Edit(src, hairSub, bodySub, style ?? "short");
                Cache[key] = mesh;
            }
            body.sharedMesh = mesh;
        }

        static Mesh Edit(Mesh src, int hairSub, int bodySub, string style)
        {
            var keepBelow = style switch
            {
                "long" => 0.0f,
                "ponytail" => 0.35f, "twin" => 0.42f,
                "side" => 0.5f, "curly" => 0.52f, "bob" => 0.62f,
                _ => 0.66f,
            };
            if (keepBelow <= 0f) return src;
            var keepTie = style is "ponytail" or "twin";

            // The mesh is in the FBX's native frame: bone_root is rotated 270° about X, so mesh
            // +Z is world UP and mesh +Y is world BACK (the face looks along mesh -Y). Height and
            // "behind the head" are read on those axes.
            var v = src.vertices;
            var zmin = v.Min(p => p.z); var H = v.Max(p => p.z) - zmin;
            var tris = src.GetTriangles(hairSub);
            var comps = Components(tris);
            var drop = new HashSet<int>();
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
                if (behind && (lo < keepBelow || ((tie || tail) && !keepTie))) foreach (var i in c) drop.Add(i);
            }
            var kept = new List<int>(tris.Length);
            for (var t = 0; t < tris.Length; t += 3)
            {
                if (drop.Contains(tris[t]) || drop.Contains(tris[t + 1]) || drop.Contains(tris[t + 2])) continue;
                kept.Add(tris[t]); kept.Add(tris[t + 1]); kept.Add(tris[t + 2]);
            }
            var m = Object.Instantiate(src);
            m.name = src.name + ":" + style;
            m.SetTriangles(kept.ToArray(), hairSub);
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
