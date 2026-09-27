using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The 3D street the squad fights on, in the reference's manner: a low-poly toon set — road
    /// with lane stripes and a crosswalk, a raised sidewalk with a curb, a row of pastel office
    /// buildings and shops with window panes and awnings, potted trees, lamp posts, a bus stop
    /// and a power pole — in front of the painted backdrop, which stays for the sky and the far
    /// city. Everything is vertex-coloured on the toon shader (the set without an outline, the
    /// props with one). The road runs along X; the squad stands around z -1..1, the sidewalk
    /// starts at z 2.6 and the buildings at z 4.4. Mood 0 day / 1 evening / 2 night tints the
    /// set and lights the windows.
    /// </summary>
    public static class StreetSet
    {
        public static Transform Build(Transform parent, int layer, int mood)
        {
            var root = new GameObject("street") { layer = layer }.transform;
            root.SetParent(parent, false);
            var tint = mood == 2 ? new Color(0.5f, 0.55f, 0.75f) : mood == 1 ? new Color(1.05f, 0.9f, 0.78f) : Color.white;
            Color T(Color c) => new(Mathf.Clamp01(c.r * tint.r), Mathf.Clamp01(c.g * tint.g), Mathf.Clamp01(c.b * tint.b), 1f);
            var glassCol = mood == 2 ? new Color(1f, 0.9f, 0.6f) : T(new Color(0.62f, 0.78f, 0.92f));

            // ---- ground: road, stripes, crosswalk, sidewalk, curb ----
            var g = new MeshKit.Builder();
            var asphalt = T(new Color(0.55f, 0.6f, 0.66f));
            var asphaltFar = T(new Color(0.6f, 0.65f, 0.71f));
            g.Grid(1, 8, (s, t) => (new Vector3(Mathf.Lerp(-30f, 30f, s), 0f, Mathf.Lerp(-6f, 2.6f, t)), Vector3.up, new Vector2(s, t)), asphalt);
            var white = T(new Color(0.93f, 0.94f, 0.95f));
            for (var x = -8f; x < 30f; x += 2.4f)                                    // the centre line, dashed (from the corner)
                g.Quad(new Vector3(x + 0.7f, 0.003f, -1.5f), new Vector3(0.7f, 0f, 0f), new Vector3(0f, 0f, 0.07f), white);
            // the cross street at the left: its centre line runs away from the camera, its own stop line
            for (var z = -6f; z < 2.6f; z += 2.4f)
                g.Quad(new Vector3(-12.2f, 0.003f, z + 0.7f), new Vector3(0.07f, 0f, 0f), new Vector3(0f, 0f, 0.7f), white);
            // asphalt patches and a manhole, to break the flat grey
            var patch = T(new Color(0.52f, 0.57f, 0.63f));
            g.Quad(new Vector3(3.5f, 0.002f, 0.4f), new Vector3(1.6f, 0f, 0f), new Vector3(0f, 0f, 0.9f), patch);
            g.Quad(new Vector3(9.5f, 0.002f, -1.2f), new Vector3(1.1f, 0f, 0f), new Vector3(0f, 0f, 0.7f), patch);
            g.Disc(new Vector3(1.2f, 0.004f, 1.6f), 0.32f, 0.32f, T(new Color(0.42f, 0.45f, 0.5f)), true, 16);
            g.Disc(new Vector3(1.2f, 0.005f, 1.6f), 0.24f, 0.24f, T(new Color(0.48f, 0.51f, 0.56f)), true, 16);
            g.Box(new Vector3(-3.5f, 0.004f, 2.35f), new Vector3(0.7f, 0.004f, 0.3f), T(new Color(0.38f, 0.4f, 0.44f)));   // a drain grate at the curb
            g.Quad(new Vector3(0f, 0.003f, 2.1f), new Vector3(30f, 0f, 0f), new Vector3(0f, 0f, 0.06f), white);   // the edge line
            for (var x = -7f; x <= -2f; x += 0.9f)                                   // a crosswalk at the left
                g.Quad(new Vector3(x, 0.004f, -1.9f), new Vector3(0.3f, 0f, 0f), new Vector3(0f, 0f, 3.6f), white);
            g.Quad(new Vector3(-8.3f, 0.004f, -1.9f), new Vector3(0.12f, 0f, 0f), new Vector3(0f, 0f, 3.6f), white);   // the stop line
            g.Quad(new Vector3(-12.2f, 0.004f, 2.0f), new Vector3(3.6f, 0f, 0f), new Vector3(0f, 0f, 0.12f), white);   // the cross street's
            for (var x = 2f; x <= 14f; x += 6f)                                       // lane arrows, painted flat
            {
                g.Quad(new Vector3(x, 0.004f, -3.9f), new Vector3(0.5f, 0f, 0f), new Vector3(0f, 0f, 0.09f), white);
                g.Quad(new Vector3(x + 0.55f, 0.004f, -3.9f), new Vector3(0.22f, 0f, 0f), new Vector3(0f, 0f, 0.3f), white);
            }
            var pave = T(new Color(0.78f, 0.79f, 0.8f));
            var curb = T(new Color(0.86f, 0.87f, 0.88f));
            // the sidewalk from the corner (x -8.5) to the right; the cross street continues left
            g.Box(new Vector3(10.75f, 0.06f, 3.6f), new Vector3(38.5f, 0.12f, 2.0f), pave);     // the sidewalk slab
            g.Box(new Vector3(10.75f, 0.065f, 2.63f), new Vector3(38.5f, 0.13f, 0.08f), curb);  // its curb
            g.Box(new Vector3(-8.5f, 0.065f, 3.6f), new Vector3(0.08f, 0.13f, 2.0f), curb);     // the corner's side curb
            g.Box(new Vector3(-20f, 0.06f, 4.6f), new Vector3(23f, 0.12f, 0.1f), pave);         // the far pavement across the cross street
            var joint = T(new Color(0.7f, 0.71f, 0.73f));
            for (var x = -8f; x < 30f; x += 1.0f)                                    // paving tiles
                g.Quad(new Vector3(x, 0.121f, 3.6f), new Vector3(0.012f, 0f, 0f), new Vector3(0f, 0f, 1f), joint);
            for (var z = 2.7f; z < 4.6f; z += 1.0f)
                g.Quad(new Vector3(10.75f, 0.121f, z), new Vector3(19.25f, 0f, 0f), new Vector3(0f, 0f, 0.012f), joint);
            MeshKit.Part("ground", root, g.Bake("ground"), MeshKit.ToonFlat, layer);

            // ---- buildings: a row of pastel blocks with window panes, along z 4.4.. ----
            var b = new MeshKit.Builder();
            var glass = new MeshKit.Builder();
            Color[] walls =
            {
                new(0.9f, 0.86f, 0.78f), new(0.8f, 0.9f, 0.83f), new(0.92f, 0.8f, 0.84f),
                new(0.86f, 0.88f, 0.92f), new(0.8f, 0.86f, 0.93f), new(0.93f, 0.9f, 0.82f),
            };
            var x0 = -8.3f;
            var rnd = new System.Random(7);
            for (var i = 0; i < 8; i++)
            {
                var w = 3.2f + (float)rnd.NextDouble() * 1.4f;
                var floors = 2 + rnd.Next(3);
                var h = floors * 1.35f + 0.3f;
                var d = 4f;
                var cx = x0 + w * 0.5f; var cz = 4.4f + d * 0.5f;
                var wall = T(walls[i % walls.Length]);
                b.Box(new Vector3(cx, h * 0.5f, cz), new Vector3(w, h, d), wall);
                b.Box(new Vector3(cx, h + 0.08f, cz), new Vector3(w + 0.16f, 0.16f, d + 0.16f), T(wall * 0.85f));   // the roof lip
                b.Box(new Vector3(cx, h + 0.06f, cz), new Vector3(w - 0.3f, 0.12f, d - 0.3f), T(new Color(0.62f, 0.64f, 0.68f)));  // the roof slab
                b.Box(new Vector3(cx - w * 0.25f, h + 0.35f, cz + 0.6f), new Vector3(0.7f, 0.5f, 0.6f), T(new Color(0.8f, 0.82f, 0.85f)));  // an AC unit
                b.Box(new Vector3(cx + w * 0.3f, h + 0.5f, cz - 0.4f), new Vector3(0.35f, 0.8f, 0.35f), T(new Color(0.55f, 0.58f, 0.62f)));  // a water tank
                // window panes on the front face, per floor; the ground floor a shop front
                var frame = T(new Color(0.35f, 0.38f, 0.45f));
                for (var f = 0; f < floors; f++)
                {
                    var y = 0.3f + f * 1.35f + 0.7f;
                    if (f == 0)
                    {
                        glass.Quad(new Vector3(cx, 0.75f, 4.4f - 0.01f), new Vector3(w * 0.42f, 0f, 0f), new Vector3(0f, 0.55f, 0f), glassCol);
                        b.Box(new Vector3(cx, 1.42f, 4.4f - 0.02f), new Vector3(w * 0.9f, 0.08f, 0.06f), frame);
                        var sign = T(i % 3 == 0 ? new Color(0.25f, 0.4f, 0.75f) : i % 3 == 1 ? new Color(0.9f, 0.9f, 0.9f) : new Color(0.35f, 0.6f, 0.45f));
                        b.Box(new Vector3(cx, 1.75f, 4.4f - 0.06f), new Vector3(w * 0.85f, 0.4f, 0.1f), sign);   // the signboard
                        // an awning on the shops (every other block)
                        if (i % 2 == 1)
                        {
                            var awn = T(i % 4 == 1 ? new Color(0.95f, 0.55f, 0.3f) : new Color(0.3f, 0.55f, 0.85f));
                            b.Quad(new Vector3(cx, 1.55f, 4.4f - 0.35f), new Vector3(w * 0.45f, 0f, 0f), new Vector3(0f, 0.12f, -0.35f), awn);
                        }
                        continue;
                    }
                    var n = Mathf.Max(2, (int)(w / 0.9f));
                    for (var k = 0; k < n; k++)
                    {
                        var wx = cx - w * 0.5f + w * (k + 0.5f) / n;
                        glass.Quad(new Vector3(wx, y, 4.4f - 0.01f), new Vector3(0.28f, 0f, 0f), new Vector3(0f, 0.42f, 0f), glassCol);
                        b.Box(new Vector3(wx, y - 0.46f, 4.4f - 0.02f), new Vector3(0.66f, 0.05f, 0.05f), frame);   // the sill
                    }
                }
                x0 += w + 0.35f;
            }
            MeshKit.Part("buildings", root, b.Bake("buildings"), MeshKit.ToonFlat, layer);
            var gm = MeshKit.NewToon(0f);
            gm.SetFloat("_ShadeStrength", 0.08f);
            MeshKit.Part("glass", root, glass.Bake("glass"), gm, layer);

            // ---- props on the sidewalk edge ----
            var p = new MeshKit.Builder();
            var pole = T(new Color(0.42f, 0.45f, 0.5f));
            var leaf = T(new Color(0.42f, 0.68f, 0.4f));
            var leafDark = T(new Color(0.3f, 0.55f, 0.33f));
            for (var x = -5.5f; x <= 16.5f; x += 5.5f)
            {
                // a potted tree: pot, trunk, two-lobed canopy
                var pot = T(x % 11f == 0f ? new Color(0.93f, 0.6f, 0.65f) : new Color(0.75f, 0.85f, 0.75f));
                p.Frustum(new Vector3(x, 0.12f, 2.95f), 0.3f, 0.45f, 0.36f, pot);
                p.Frustum(new Vector3(x, 0.55f, 2.95f), 0.06f, 0.6f, 0.05f, T(new Color(0.45f, 0.32f, 0.22f)));
                p.Ellipsoid(new Vector3(x, 1.45f, 2.95f), new Vector3(0.5f, 0.55f, 0.5f), leaf, 14);
                p.Ellipsoid(new Vector3(x + 0.22f, 1.2f, 2.85f), new Vector3(0.36f, 0.34f, 0.36f), leafDark, 12);
                // a lamp post between the trees
                p.Frustum(new Vector3(x + 2.75f, 0.12f, 2.8f), 0.05f, 2.9f, 0.04f, pole, seg: 8);
                p.Box(new Vector3(x + 2.75f, 3.05f, 2.8f), new Vector3(0.28f, 0.14f, 0.2f), pole);
                p.Box(new Vector3(x + 2.75f, 2.96f, 2.8f), new Vector3(0.22f, 0.04f, 0.16f), mood == 2 ? new Color(1f, 0.95f, 0.7f) : white);
            }
            // a bus stop: two posts, a roof and a bench, at the right
            p.Frustum(new Vector3(6.2f, 0.12f, 3.9f), 0.04f, 2.2f, 0.04f, pole, seg: 8);
            p.Frustum(new Vector3(8.2f, 0.12f, 3.9f), 0.04f, 2.2f, 0.04f, pole, seg: 8);
            p.Box(new Vector3(7.2f, 2.35f, 3.5f), new Vector3(2.6f, 0.08f, 1.2f), T(new Color(0.3f, 0.5f, 0.8f)));
            p.Box(new Vector3(7.2f, 0.55f, 4.0f), new Vector3(2.0f, 0.08f, 0.4f), T(new Color(0.55f, 0.42f, 0.3f)));
            // the traffic light at the corner: a pole, an arm over the road, a three-lamp box
            p.Frustum(new Vector3(-8.0f, 0.12f, 2.9f), 0.07f, 4.2f, 0.06f, pole, seg: 8);
            p.Box(new Vector3(-8.0f, 4.25f, 1.6f), new Vector3(0.1f, 0.1f, 2.8f), pole);
            p.Box(new Vector3(-8.0f, 4.0f, 0.4f), new Vector3(0.3f, 0.32f, 0.95f), T(new Color(0.2f, 0.22f, 0.26f)));
            p.Ellipsoid(new Vector3(-8.18f, 4.0f, 0.75f), new Vector3(0.05f, 0.1f, 0.1f), new Color(0.95f, 0.25f, 0.2f), 8);
            p.Ellipsoid(new Vector3(-8.18f, 4.0f, 0.4f), new Vector3(0.05f, 0.1f, 0.1f), T(new Color(0.85f, 0.7f, 0.2f)), 8);
            p.Ellipsoid(new Vector3(-8.18f, 4.0f, 0.05f), new Vector3(0.05f, 0.1f, 0.1f), T(new Color(0.25f, 0.6f, 0.4f)), 8);
            // a power pole with a cross arm, across the cross street
            p.Frustum(new Vector3(-16.5f, 0.12f, 4.8f), 0.09f, 6.5f, 0.07f, T(new Color(0.5f, 0.47f, 0.45f)), seg: 8);
            p.Box(new Vector3(-16.5f, 6.2f, 4.8f), new Vector3(1.6f, 0.08f, 0.08f), T(new Color(0.5f, 0.47f, 0.45f)));
            // a road sign
            p.Frustum(new Vector3(3.2f, 0.12f, 2.85f), 0.03f, 2.0f, 0.03f, pole, seg: 6);
            p.Quad(new Vector3(3.2f, 2.35f, 2.85f), new Vector3(0.28f, 0f, 0f), new Vector3(0f, 0.28f, 0f), T(new Color(0.9f, 0.25f, 0.25f)));
            var pm = MeshKit.NewToon(0.006f);
            pm.SetFloat("_ShadeStrength", 0.22f);
            MeshKit.Part("props", root, p.Bake("props"), pm, layer);
            return root;
        }
    }
}
