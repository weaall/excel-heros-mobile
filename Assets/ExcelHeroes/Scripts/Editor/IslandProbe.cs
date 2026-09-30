using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.World;
using UnityEngine;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// Debug: every SkinnedMeshRenderer of a built figure split into its connected pieces (welded by
    /// position), and for each small piece its size, where it hangs (baked, root space) and the bones it is
    /// skinned to — to find what the stray specks beside some figures are.
    ///   SD_IDS=macro bash tools/unity.sh ExcelHeroes.EditorTools.IslandProbe.Run probe
    /// </summary>
    public static class IslandProbe
    {
        public static void Run()
        {
            if (!ExcelHeroes.Data.GameData.Loaded) ExcelHeroes.Data.GameData.Load();
            foreach (var id in (System.Environment.GetEnvironmentVariable("SD_IDS") ?? "macro").Split(','))
            {
                var holder = new GameObject("probe").transform;
                var rig = SdRef.Build(id, holder, 0);
                if (rig == null) continue;
                SdPose.Apply(rig, ExcelHeroes.World.Pose.Rest);
                foreach (var smr in rig.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    // SD_BONES=1: every non-Biped bone the renderer's vertices ride, with its vertex count and centre
                    if (System.Environment.GetEnvironmentVariable("SD_BONES") == "1" && smr.enabled && smr.gameObject.activeInHierarchy && smr.sharedMesh != null)
                    {
                        var bk = new Mesh(); smr.BakeMesh(bk); var vv = bk.vertices; var bww = smr.sharedMesh.boneWeights; var bb = smr.bones;
                        var agg = new Dictionary<string, (int n, Vector3 c)>();
                        for (var i = 0; i < vv.Length && i < bww.Length; i++)
                        {
                            var bi = bww[i].boneIndex0; if (bi >= bb.Length || bb[bi] == null) continue;
                            var nm = bb[bi].name; if (nm.StartsWith("Bip001")) continue;
                            agg.TryGetValue(nm, out var a); agg[nm] = (a.n + 1, a.c + rig.Root.InverseTransformPoint(smr.transform.TransformPoint(vv[i])));
                        }
                        foreach (var kv in agg.OrderBy(x => x.Key)) Debug.Log($"[BONE] {id} {smr.name} {kv.Key} v{kv.Value.n} at {kv.Value.c / kv.Value.n:F2}");
                    }
                    if (!smr.enabled || smr.sharedMesh == null) continue;
                    var baked = new Mesh(); smr.BakeMesh(baked);
                    var v = baked.vertices; var bw = smr.sharedMesh.boneWeights; var bones = smr.bones;
                    var weld = new Dictionary<Vector3Int, int>(); var key = new int[v.Length];
                    for (var i = 0; i < v.Length; i++) { var q = Vector3Int.RoundToInt(v[i] * 2000f); if (!weld.TryGetValue(q, out var k)) { k = weld.Count; weld[q] = k; } key[i] = k; }
                    var par = Enumerable.Range(0, weld.Count).ToArray();
                    int F(int a) { while (par[a] != a) { par[a] = par[par[a]]; a = par[a]; } return a; }
                    var tris = baked.triangles;
                    for (var t = 0; t < tris.Length; t += 3) { var a = F(key[tris[t]]); var b = F(key[tris[t + 1]]); if (a != b) par[b] = a; var c = F(key[tris[t + 2]]); a = F(a); if (a != c) par[c] = a; }
                    var groups = new Dictionary<int, List<int>>();
                    for (var t = 0; t < tris.Length; t += 3) { var r = F(key[tris[t]]); if (!groups.TryGetValue(r, out var l)) groups[r] = l = new List<int>(); l.Add(t); }
                    var total = tris.Length / 3;
                    foreach (var g in groups.Values.Where(g => g.Count < total * 0.05f).OrderBy(g => g.Count).Take(40))
                    {
                        var pts = g.SelectMany(t => new[] { tris[t], tris[t + 1], tris[t + 2] }).Distinct().ToList();
                        var c = pts.Aggregate(Vector3.zero, (s, i) => s + smr.transform.TransformPoint(v[i])) / pts.Count;
                        var names = pts.Where(i => i < bw.Length).Select(i => { var w = bw[i]; var bi = w.weight0 >= w.weight1 ? w.boneIndex0 : w.boneIndex1; return bi < bones.Length && bones[bi] != null ? bones[bi].name : "?"; })
                                       .GroupBy(n => n).OrderByDescending(x => x.Count()).Take(2).Select(x => x.Key);
                        var lc = rig.Root.InverseTransformPoint(c);
                        Debug.Log($"[ISL] {id} {smr.name} tris {g.Count} at {lc:F2} bones {string.Join("/", names)}");
                    }
                }
                Object.DestroyImmediate(holder.gameObject);
            }
        }
    }
}
