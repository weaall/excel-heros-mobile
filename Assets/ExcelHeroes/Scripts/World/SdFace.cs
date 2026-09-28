using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The Blue Archive SD face, drawn the way it is built. The eye is layered plates, and the
    /// sheets have no alpha: the eye WHITE is a plate that samples a white block at the corner of
    /// the EyeMouth sheet, the IRIS a larger plate in front of it that samples the whole iris
    /// square — dark surround included — and is meant to show only inside the white; the lash
    /// lines are small plates on the Face sheet's colour swatches, in front again; the brows sit
    /// just behind the fringe and are meant to read over it (measured: EyeParts, tools/out/eye_parts.txt).
    ///
    /// Drawn as one opaque pass, the iris square showed round the eye and the fringe cut the brows.
    /// Split: the eyemouth submesh into white / iris / mouth by each piece's UV box; the white
    /// writes stencil 2, the iris tests it; brows and lines pull their depth toward the camera.
    /// </summary>
    public static class SdFace
    {
        public const int StencilEye = 2;

        public enum Part { Skin, White, Iris, IrisFree, Highlight, Line, Mouth, Brow, Other }

        /// <summary>
        /// A copy of the body mesh with the eyemouth submesh split into white / iris / mouth, and a
        /// Part for every resulting submesh (the others classified by material name).
        /// </summary>
        public static (Mesh mesh, Part[] parts, string[] names) Split(Mesh src, string[] matNames, System.Func<Vector2, float> eyeSheetLuma = null)
        {
            var mesh = Object.Instantiate(src);
            mesh.name = src.name + "+face";
            var uv = src.uv;
            var subs = new List<int[]>(); var parts = new List<Part>(); var names = new List<string>();
            for (var s = 0; s < src.subMeshCount; s++)
            {
                var n = s < matNames.Length ? (matNames[s] ?? "").ToLowerInvariant() : "";
                var tris = src.GetTriangles(s);
                if (n.Contains("eyemouth"))
                {
                    var white = new List<int>(); var iris = new List<int>(); var mouth = new List<int>(); var line = new List<int>();
                    // the submesh's own centre and spread in mesh space: the mouth plate sits on the
                    // face's midline, each eye's plates well off it
                    var vtx = src.vertices; var c0 = Vector3.zero; var n0 = 0;
                    foreach (var t in tris) { c0 += vtx[t]; n0++; }
                    c0 /= Mathf.Max(1, n0);
                    var spread = 0f; foreach (var t in tris) spread = Mathf.Max(spread, (vtx[t] - c0).magnitude);
                    // which way the plates face (their mean normal): depth along it tells a highlight
                    // (in front of the iris) from the white (behind it), whatever the mesh's axes
                    var nrm = src.normals; var fwd = Vector3.zero; foreach (var t in tris) fwd += nrm[t];
                    fwd = fwd.sqrMagnitude > 1e-8f ? fwd.normalized : Vector3.forward;
                    foreach (var piece in Pieces(tris))
                    {
                        float x0 = 1, x1 = 0, y0 = 1, y1 = 0;
                        foreach (var t in piece) { var u = uv[t]; x0 = Mathf.Min(x0, u.x); x1 = Mathf.Max(x1, u.x); y0 = Mathf.Min(y0, u.y); y1 = Mathf.Max(y1, u.y); }
                        // the sheet's layout (the same on every sample): the iris square on the right,
                        // the mouth plate's block bottom-left, and everything else — the white blocks,
                        // the white plates, the highlights — on the left. Highlights counted as white is
                        // harmless: they sit in front of the iris, so the depth test keeps them over it.
                        if (x1 - x0 > 0.4f && y1 - y0 > 0.4f) iris.AddRange(piece);          // the iris square: a big box
                        else if (y1 < 0.3f && x1 < 0.3f && Midline(piece, vtx, c0, spread)) mouth.AddRange(piece);
                        // small plates: a bright block is the white, a dark one a lid shadow / line (miku)
                        else if (eyeSheetLuma != null && eyeSheetLuma(new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f)) < 0.45f) line.AddRange(piece);
                        else white.AddRange(piece);
                    }
                    // highlights: white-side pieces in front of the iris's mean depth
                    var hi = new List<int>();
                    if (iris.Count > 0 && white.Count > 0)
                    {
                        float Depth(List<int> ids) { var d = 0f; foreach (var t in ids) d += Vector3.Dot(vtx[t], fwd); return d / Mathf.Max(1, ids.Count); }
                        var dIris = Depth(iris);
                        var keep = new List<int>();
                        foreach (var piece in Pieces(white.ToArray())) (Depth(piece) > dIris + spread * 0.01f ? hi : keep).AddRange(piece);
                        white = keep;
                    }
                    // a real white plate is about as big as the iris it holds; tiny ones (reisa: the white is
                    // painted on the face sheet) are just lid marks — then the iris goes free
                    float Area(List<int> ids) { var a2 = 0f; for (var q = 0; q + 2 < ids.Count; q += 3) a2 += Vector3.Cross(vtx[ids[q + 1]] - vtx[ids[q]], vtx[ids[q + 2]] - vtx[ids[q]]).magnitude; return a2 * 0.5f; }
                    if (white.Count > 0 && iris.Count > 0 && Area(white) < Area(iris) * 0.3f) { line.AddRange(white); white = new List<int>(); }
                    if (white.Count > 0) { subs.Add(white.ToArray()); parts.Add(Part.White); names.Add(n + ":white"); }
                    // with no white plate (reisa: the white is painted on the face sheet) the highlights
                    // are plain plates over the iris
                    if (hi.Count > 0) { subs.Add(hi.ToArray()); parts.Add(white.Count > 0 ? Part.Highlight : Part.Line); names.Add(n + ":highlight"); }
                    // no white plates here (reisa's face renderer: its "whites" sample dark blocks — lid
                    // shadows): the iris draws on its own, depth-tested, over the face's painted white
                    if (iris.Count > 0) { subs.Add(iris.ToArray()); parts.Add(white.Count > 0 ? Part.Iris : Part.IrisFree); names.Add(n + ":iris"); }
                    if (mouth.Count > 0) { subs.Add(mouth.ToArray()); parts.Add(Part.Mouth); names.Add(n + ":mouth"); }
                    if (line.Count > 0) { subs.Add(line.ToArray()); parts.Add(Part.Line); names.Add(n + ":line"); }
                    continue;
                }
                subs.Add(tris);
                parts.Add(n.Contains("eyebrow") ? Part.Brow : n.Contains("face") ? Part.Skin : n.Contains("alpha") ? Part.Other : Part.Other);
                names.Add(n);
            }
            mesh.subMeshCount = subs.Count;
            for (var i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i, false);
            mesh.RecalculateBounds();
            return (mesh, parts.ToArray(), names.ToArray());
        }

        /// <summary>Sets the stencil / order / depth-pull on a Toon material for its face part.</summary>
        public static void Configure(Material m, Part part, float height)
        {
            switch (part)
            {
                case Part.White:
                    m.SetFloat("_StencilRef", StencilEye); m.SetFloat("_StencilComp", 8f); m.SetFloat("_StencilPass", 2f);   // Always, Replace
                    // a hair's breadth toward the camera: on some samples the white is coplanar with the skin (reisa)
                    m.SetFloat("_DepthPull", 0.004f * height);
                    m.renderQueue = 2001; break;
                case Part.Iris:
                    // inside the white whatever the depth: on some samples the iris plate sits behind it
                    m.SetFloat("_StencilRef", StencilEye); m.SetFloat("_StencilComp", 3f); m.SetFloat("_StencilPass", 0f);   // Equal, Keep
                    m.SetFloat("_ZTest", 8f);   // Always
                    m.renderQueue = 2002; break;
                case Part.IrisFree:
                    // over the face's painted white, clipped by nothing: the iris sheet's dark surround is
                    // the eye's own outline there. Pulled just in front of the skin it lies on.
                    m.SetFloat("_DepthPull", 0.03f * height);   // reisa's sits well behind the skin it shows through
                    m.renderQueue = 2002; break;
                case Part.Highlight:
                    m.SetFloat("_StencilRef", StencilEye); m.SetFloat("_StencilComp", 3f);
                    m.SetFloat("_ZTest", 8f);
                    m.renderQueue = 2003; break;
                case Part.Line:
                    m.renderQueue = 2003; break;          // over the iris, depth-tested
                case Part.Brow:
                    m.SetFloat("_DepthPull", 0.05f * height); m.renderQueue = 2004; break;
                case Part.Skin:
                    m.renderQueue = 2000; break;
                case Part.Mouth:
                    // the plate the expressions swap mouth shapes onto: blank (a white or dark
                    // block) at rest, and the lines on the Face sheet already draw the closed mouth
                    m.SetFloat("_Cutoff", 2f); break;
            }
        }

        // is a piece on the face's midline? (its centroid's offset from the submesh centre is small
        // against the spread, in the direction the two eyes separate — whichever axis that is)
        static bool Midline(List<int> piece, Vector3[] vtx, Vector3 c0, float spread)
        {
            var c = Vector3.zero; foreach (var t in piece) c += vtx[t]; c /= piece.Count;
            var d = c - c0;
            var lateral = Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z) * 0f);   // the eyes separate along mesh x in every sample
            return lateral < spread * 0.12f;
        }

        // connected pieces of a triangle list, as flat vertex-index triples
        static IEnumerable<List<int>> Pieces(int[] tris)
        {
            var parent = new Dictionary<int, int>();
            int Find(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }
            foreach (var t in tris) if (!parent.ContainsKey(t)) parent[t] = t;
            for (var i = 0; i < tris.Length; i += 3) { var a = Find(tris[i]); parent[Find(tris[i + 1])] = a; parent[Find(tris[i + 2])] = a; }
            var groups = new Dictionary<int, List<int>>();
            for (var i = 0; i < tris.Length; i += 3)
            {
                var r = Find(tris[i]);
                if (!groups.TryGetValue(r, out var l)) groups[r] = l = new List<int>();
                l.Add(tris[i]); l.Add(tris[i + 1]); l.Add(tris[i + 2]);
            }
            return groups.Values;
        }
    }
}
