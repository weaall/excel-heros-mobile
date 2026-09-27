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

        /// <summary>
        /// A necktie on the chest bone (Spine1): a knot at the collar (mesh z 0.0061, y −0.00105) and
        /// a blade hanging to z 0.0044, kept a hair in front of the jacket (front profile −0.00137).
        /// Mesh space: z up, −y forward; world = body.TransformPoint.
        /// </summary>
        public static void Tie(ChibiRig rig, SkinnedMeshRenderer body, Transform root, Color col, int layer)
        {
            if (rig.Spine == null || col.a <= 0f) return;
            var s = body.transform.lossyScale.x;
            col.a = 1f;
            var knot = body.transform.TransformPoint(new Vector3(0f, -0.00118f, 0.00605f));
            var b = new MeshKit.Builder();
            // sizes in mesh units × the figure's scale; built in the wrapper's frame (+z forward, +y up)
            b.Box(new Vector3(0f, 0f, 0f), new Vector3(0.00026f * s, 0.00022f * s, 0.00012f * s), MeshKit.Shade(col, 0.85f));   // the knot
            // the blade: a strip widening then tapering, tilted so its bottom sits in front of the jacket
            b.Grid(1, 6, (u, t) =>
            {
                var w = Mathf.Lerp(0.00010f, 0.00018f, Mathf.Sin(t * Mathf.PI * 0.85f)) * s;
                var y = -0.00012f * s - t * 0.00158f * s;
                var z = Mathf.Lerp(0f, 0.00028f, t) * s;
                return (new Vector3((u - 0.5f) * 2f * w, y, z), Vector3.forward, new Vector2(u, t));
            }, col);
            var mat = MeshKit.NewToon(0.0025f);
            mat.SetFloat("_ShadeStrength", 0.2f);
            var go = Attach("tie", rig.Spine, b.Bake("tie"), mat, layer, knot, root.rotation);
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
        }

        /// <summary>
        /// The collar that tells a suit from a tracksuit: on the chest bone, a V of two lapel
        /// strips in the jacket colour (a shade darker) opening from the collar (mesh z 0.0061)
        /// down to z 0.0049, with the shirt showing inside the V and a small shirt collar at the
        /// neck. "shirt" and "vest" outfits get the shirt collar only. Sits just in front of the
        /// front profile (−0.00122 at that height).
        /// </summary>
        public static void Collar(ChibiRig rig, SkinnedMeshRenderer body, Transform root, SdLook k, int layer)
        {
            if (rig.Spine == null) return;
            var s = body.transform.lossyScale.x;
            var anchor = body.transform.TransformPoint(new Vector3(0f, -0.00128f, 0.00612f));
            var b = new MeshKit.Builder();
            var shirt = k.Shirt; shirt.a = 1f;
            var lapel = MeshKit.Shade(k.Top, 0.8f); lapel.a = 1f;
            var withLapels = k.Outfit is "suit" or "coat" or "labcoat" or "dress";
            // closed thin boxes, not single-sided strips: the outline pass draws a hull around a
            // box the way it does the tie's knot, whereas a lone strip can end up drawn only by it
            void Slab(Vector3 c, Vector3 size, float zDeg, Color col)
            {
                b.M = Matrix4x4.TRS(c * s, Quaternion.Euler(0f, 0f, zDeg), Vector3.one);
                b.Box(Vector3.zero, size * s, col);
                b.M = Matrix4x4.identity;
            }
            if (withLapels)
            {
                Slab(new Vector3(0f, -0.00072f, 0.00003f), new Vector3(0.00062f, 0.00140f, 0.00003f), 0f, shirt);              // the shirt inside the V
                foreach (var sx in new[] { -1f, 1f })
                    Slab(new Vector3(sx * 0.00031f, -0.00078f, 0.00006f), new Vector3(0.00024f, 0.00158f, 0.00004f), sx * -21f, lapel);   // a lapel, top out at the shoulder
                b.Ellipsoid(new Vector3(0f, -0.00152f, 0.00008f) * s, new Vector3(0.00005f, 0.00005f, 0.00004f) * s, MeshKit.Shade(k.Top, 0.5f), 8);   // the button
            }
            else
                Slab(new Vector3(0f, -0.00030f, 0.00003f), new Vector3(0.00040f, 0.00050f, 0.00003f), 0f, shirt);              // an open shirt neck
            // the shirt collar: two wings at the neck, tips pointing down and out
            foreach (var sx in new[] { -1f, 1f })
                Slab(new Vector3(sx * 0.00024f, -0.00013f, 0.00009f), new Vector3(0.00036f, 0.00015f, 0.00004f), sx * -32f, MeshKit.Shade(shirt, 0.94f));
            var mat = MeshKit.NewToon(0.002f);
            mat.SetFloat("_ShadeStrength", 0.18f);
            var go = Attach("collar", rig.Spine, b.Bake("collar"), mat, layer, anchor, root.rotation);
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
        }

        /// <summary>
        /// Coat tails for coat / labcoat outfits: a lofted band from the hips (y 0.42 of the 1.2 m
        /// figure) to mid-thigh (0.24), open at the front (the coat hangs unbuttoned), in the
        /// jacket colour, on the pelvis so it swings with the body.
        /// </summary>
        public static void CoatTail(ChibiRig rig, Transform root, SdLook k, int layer)
        {
            if (rig.Pelvis == null) return;
            var b = new MeshKit.Builder();
            var col = k.Top; col.a = 1f;
            var inner = MeshKit.Shade(col, 0.72f);
            float top = 0.43f, bottom = 0.24f, r0 = 0.15f, r1 = 0.185f;
            const int seg = 14;
            // from 50° past the front on one side round the back to 50° past the front on the other: angles measured from +z (front)
            b.Grid(seg, 1, (s, t) =>
            {
                var a = Mathf.Lerp(0.85f, Mathf.PI * 2f - 0.85f, s);
                var r = Mathf.Lerp(r0, r1, t);
                var p = new Vector3(Mathf.Sin(a) * r, Mathf.Lerp(top, bottom, t), Mathf.Cos(a) * r * 0.82f);
                var n = new Vector3(Mathf.Sin(a), (r1 - r0) / (top - bottom), Mathf.Cos(a)).normalized;
                return (p, n, new Vector2(s, t));
            }, col);
            b.Grid(seg, 1, (s, t) =>
            {
                var a = Mathf.Lerp(0.85f, Mathf.PI * 2f - 0.85f, s);
                var r = Mathf.Lerp(r0, r1, t) * 0.985f;
                var p = new Vector3(Mathf.Sin(a) * r, Mathf.Lerp(top, bottom, t), Mathf.Cos(a) * r * 0.82f);
                var n = -new Vector3(Mathf.Sin(a), (r1 - r0) / (top - bottom), Mathf.Cos(a)).normalized;
                return (p, n, new Vector2(s, t));
            }, inner);
            var mat = MeshKit.NewToon(0.006f);
            mat.SetFloat("_ShadeStrength", 0.26f);
            var go = Attach("coat", rig.Pelvis, b.Bake("coat"), mat, layer, root.position, root.rotation);
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
        }

        /// <summary>
        /// A cap on the head bone: a dome over the crown (hair top mesh z 0.01018, skull centre
        /// (0, 0.0006, 0.0081)) with a brim forward, in the accessory's colour; a hard hat is the
        /// same dome, taller and without the brim, with a short rim all round.
        /// </summary>
        public static void Cap(ChibiRig rig, SkinnedMeshRenderer body, Transform root, Color col, bool hard, int layer)
        {
            if (rig.Head == null || col.a <= 0f) return;
            var s = body.transform.lossyScale.x;
            col.a = 1f;
            var centre = body.transform.TransformPoint(new Vector3(0f, 0.00045f, 0.00905f));
            var b = new MeshKit.Builder();
            var rx = 0.00168f * s; var ry = (hard ? 0.00115f : 0.00082f) * s; var rz = 0.0018f * s;
            // the dome: the upper half of an ellipsoid (thetaMax π/2 everywhere), a little lower at the back
            b.Ellipsoid(Vector3.zero, new Vector3(rx, ry, rz), col, 18, phi => Mathf.PI * 0.52f);
            if (hard)
                b.Disc(new Vector3(0f, -0.0001f * s, 0f), rx * 1.12f, rz * 1.12f, MeshKit.Shade(col, 0.9f), true, 18);
            else
            {
                // the brim: a flat half-disc forward of the dome, drawn double-sided
                b.Grid(8, 1, (u, t) =>
                {
                    var a = Mathf.Lerp(-1.2f, 1.2f, u);
                    var r = Mathf.Lerp(rz * 0.9f, rz * 1.75f, t);
                    return (new Vector3(Mathf.Sin(a) * r * 0.85f, -0.00008f * s - t * 0.00006f * s, Mathf.Cos(a) * r), Vector3.up, new Vector2(u, t));
                }, MeshKit.Shade(col, 0.88f));
                b.Grid(8, 1, (u, t) =>
                {
                    var a = Mathf.Lerp(-1.2f, 1.2f, u);
                    var r = Mathf.Lerp(rz * 0.9f, rz * 1.75f, t);
                    return (new Vector3(Mathf.Sin(a) * r * 0.85f, -0.00011f * s - t * 0.00006f * s, Mathf.Cos(a) * r), Vector3.down, new Vector2(u, t));
                }, MeshKit.Shade(col, 0.7f));
                b.Box(new Vector3(0f, ry * 1.02f, 0f), new Vector3(0.00012f * s, 0.00012f * s, 0.00012f * s), MeshKit.Shade(col, 0.8f));   // the button
            }
            var mat = MeshKit.NewToon(0.004f);
            mat.SetFloat("_ShadeStrength", 0.22f);
            var go = Attach(hard ? "hardhat" : "cap", rig.Head, b.Bake("cap"), mat, layer, centre, root.rotation);
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
        }

        /// <summary>A headset: a band over the crown from ear to ear, two ear cups, a mic arm on the right.</summary>
        public static void Headset(ChibiRig rig, SkinnedMeshRenderer body, Transform root, Color accent, int layer)
        {
            if (rig.Head == null) return;
            var s = body.transform.lossyScale.x;
            var centre = body.transform.TransformPoint(new Vector3(0f, 0.0005f, 0.0079f));
            var dark = new Color(0.16f, 0.17f, 0.22f);
            accent.a = 1f;
            var b = new MeshKit.Builder();
            var R = 0.00236f * s; var Rz = 0.0002f * s;                    // outside the hair at the sides (hair reaches x ±0.0023) …
            // the band: a strip along an arc in the x–y plane, width w along z, slightly proud of the hair
            var w = 0.00016f * s;
            b.Grid(16, 1, (u, t) =>
            {
                var a = Mathf.Lerp(-1.5f, 1.5f, u);
                var p = new Vector3(Mathf.Sin(a) * R, Mathf.Cos(a) * R * 0.9f, Rz + (t - 0.5f) * 2f * w);    // … and sunk into the crown
                return (p, new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f), new Vector2(u, t));
            }, dark);
            b.Grid(16, 1, (u, t) =>
            {
                var a = Mathf.Lerp(-1.5f, 1.5f, u);
                var p = new Vector3(Mathf.Sin(a) * R * 0.97f, Mathf.Cos(a) * R * 0.87f, Rz + (t - 0.5f) * 2f * w);
                return (p, new Vector3(-Mathf.Sin(a), -Mathf.Cos(a), 0f), new Vector2(u, t));
            }, dark);
            // the cups, over the ears (mesh x ±0.0013 at z 0.0072–0.0078)
            foreach (var sx in new[] { -1f, 1f })
            {
                var c = new Vector3(sx * 0.00232f * s, -0.0006f * s, -0.0003f * s);
                b.Ellipsoid(c, new Vector3(0.00028f * s, 0.00042f * s, 0.00042f * s), dark, 12);
                b.Ellipsoid(c + new Vector3(sx * 0.00022f * s, 0f, 0f), new Vector3(0.0001f * s, 0.00028f * s, 0.00028f * s), accent, 10);
            }
            // the mic arm from the right cup, forward and down to the mouth's side
            // the mic arm: from the right cup forward-down to beside the mouth, as a thin lofted tube
            var a0 = new Vector3(0.00232f * s, -0.0007f * s, -0.0001f * s); var a1 = new Vector3(0.0012f * s, -0.0016f * s, 0.0016f * s);
            b.Grid(1, 8, (u, t) =>
            {
                var p = Vector3.Lerp(a0, a1, t); var ang = u * Mathf.PI * 2f;
                var side = Vector3.Cross((a1 - a0).normalized, Vector3.up).normalized; var upv = Vector3.Cross(side, (a1 - a0).normalized);
                var q = p + (side * Mathf.Cos(ang) + upv * Mathf.Sin(ang)) * 0.00005f * s;
                return (q, (side * Mathf.Cos(ang) + upv * Mathf.Sin(ang)), new Vector2(u, t));
            }, dark);
            b.Ellipsoid(a1, new Vector3(0.00009f * s, 0.00009f * s, 0.00013f * s), accent, 8);
            var mat = MeshKit.NewToon(0.002f);
            mat.SetFloat("_ShadeStrength", 0.2f);
            var go = Attach("headset", rig.Head, b.Bake("headset"), mat, layer, centre, root.rotation);
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
