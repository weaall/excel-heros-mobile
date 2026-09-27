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
            var mat = MeshKit.NewToon(0.007f);
            mat.SetFloat("_ShadeStrength", 0.3f);
            var go = Attach("skirt", rig.Pelvis, b.Bake("skirt"), mat, layer, root.position, root.rotation);
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
        }

        /// <summary>The hand prop by role: melee a rolled document, ranged a tablet, healer a coffee cup, tank a clipboard.</summary>
        public static void HandProp(ChibiRig rig, Transform root, string role, SdLook k, int layer)
        {
            if (rig.HandR == null) return;
            var b = new MeshKit.Builder();
            var paper = new Color(0.96f, 0.96f, 0.94f);
            var ink = new Color(0.2f, 0.22f, 0.3f);
            var accent = k.Accent; accent.a = 1f;
            switch (role)
            {
                case "melee":
                    // a rolled-up document along the fingers, a coloured band around it
                    b.M = Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, -90f));            // frustum builds along +Y → +X
                    b.Frustum(new Vector3(0f, -0.06f, 0f), 0.026f, 0.2f, 0.026f, paper, 1f, 12);
                    b.Frustum(new Vector3(0f, 0.02f, 0f), 0.028f, 0.03f, 0.028f, accent, 1f, 12, false);
                    b.M = Matrix4x4.identity;
                    break;
                case "healer":
                    // a coffee cup standing in the hand: cup, a dark top, a handle
                    b.M = Matrix4x4.Translate(new Vector3(0.05f, 0f, 0f));
                    b.Frustum(new Vector3(0f, -0.045f, 0f), 0.03f, 0.09f, 0.036f, paper, 1f, 12);
                    b.Disc(new Vector3(0f, 0.046f, 0f), 0.034f, 0.034f, new Color(0.35f, 0.22f, 0.14f), true, 12);
                    b.Frustum(new Vector3(0f, 0.005f, 0f), 0.036f, 0.012f, 0.036f, accent, 1f, 12, false);
                    b.Box(new Vector3(0.045f, 0.005f, 0f), new Vector3(0.012f, 0.045f, 0.01f), paper);
                    b.Box(new Vector3(0.03f, 0.025f, 0f), new Vector3(0.03f, 0.01f, 0.01f), paper);
                    b.Box(new Vector3(0.03f, -0.015f, 0f), new Vector3(0.03f, 0.01f, 0.01f), paper);
                    b.M = Matrix4x4.identity;
                    break;
                case "tank":
                    // a clipboard held flat against the forearm: board, clip, a sheet with lines
                    b.Box(new Vector3(0.05f, 0f, 0.012f), new Vector3(0.13f, 0.17f, 0.012f), new Color(0.55f, 0.4f, 0.28f));
                    b.Quad(new Vector3(0.05f, -0.008f, 0.019f), new Vector3(0.055f, 0f, 0f), new Vector3(0f, 0.07f, 0f), paper);
                    for (var i = 0; i < 4; i++) b.Quad(new Vector3(0.05f, 0.025f - i * 0.02f, 0.0195f), new Vector3(0.04f, 0f, 0f), new Vector3(0f, 0.003f, 0f), ink);
                    b.Box(new Vector3(0.05f, 0.075f, 0.02f), new Vector3(0.05f, 0.02f, 0.02f), new Color(0.6f, 0.62f, 0.66f));
                    break;
                default:
                    // a tablet: a dark slab with a glowing sheet on its face, pointed along the fingers
                    b.Box(new Vector3(0.09f, 0f, 0f), new Vector3(0.2f, 0.13f, 0.012f), ink);
                    b.Quad(new Vector3(0.09f, 0f, 0.0065f), new Vector3(0.09f, 0f, 0f), new Vector3(0f, 0.055f, 0f), new Color(0.85f, 0.95f, 1f));
                    for (var i = 0; i < 3; i++) b.Quad(new Vector3(0.09f, 0.03f - i * 0.025f, 0.007f), new Vector3(0.07f, 0f, 0f), new Vector3(0f, 0.004f, 0f), Color.Lerp(accent, Color.white, 0.3f));
                    break;
            }
            var mat = MeshKit.NewToon(0.003f);
            mat.SetFloat("_ShadeStrength", 0.18f);
            // built along the wrapper's +X; the hand bone's +X runs along the fingers, so align the
            // prop's frame to the bone's: world rotation = bone rotation
            var go = Attach("prop:" + role, rig.HandR, b.Bake("prop"), mat, layer, rig.HandR.position, rig.HandR.rotation);
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
        }
    }
}
