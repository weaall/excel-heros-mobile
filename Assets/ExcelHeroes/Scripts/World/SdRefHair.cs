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

        public static void Apply(SkinnedMeshRenderer body, string style, int hairSub)
        {
            if (hairSub < 0) return;
            var src = body.sharedMesh;
            var key = (src, style ?? "short");
            if (!Cache.TryGetValue(key, out var mesh))
            {
                mesh = Edit(src, hairSub, style ?? "short");
                Cache[key] = mesh;
            }
            body.sharedMesh = mesh;
        }

        static Mesh Edit(Mesh src, int hairSub, string style)
        {
            var keepBelow = style switch
            {
                "long" => 0.0f,
                "ponytail" => 0.35f, "twin" => 0.42f,
                "side" => 0.5f, "curly" => 0.52f, "bob" => 0.62f,
                _ => 0.66f,
            };
            if (keepBelow <= 0f) return src;

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
                // hanging hair: reaches below the style's line AND stays behind the face
                if (lo < keepBelow && frontMost > -0.03f && hi < 0.97f) foreach (var i in c) drop.Add(i);
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
