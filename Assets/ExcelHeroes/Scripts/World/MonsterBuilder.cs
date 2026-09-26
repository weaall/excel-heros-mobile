using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The spreadsheet errors, in 3D: each bestiary shape (monsters.json) becomes a chunky toon
    /// figure with a painted angry face, built facing +X like the cast and turned round by the
    /// actor to face the party. Colour comes from the type id so a wave reads as a family.
    /// Bosses are the same builder at three times the size with a crown of red error flags.
    /// </summary>
    public static class MonsterBuilder
    {
        static readonly Color[] Palette =
        {
            new(0.94f, 0.42f, 0.42f), new(0.98f, 0.66f, 0.3f), new(0.62f, 0.5f, 0.92f), new(0.36f, 0.72f, 0.95f),
            new(0.45f, 0.82f, 0.55f), new(0.95f, 0.5f, 0.75f), new(0.55f, 0.6f, 0.7f), new(0.98f, 0.82f, 0.3f),
        };

        public static Color ColorFor(string typeId)
        {
            var h = 0;
            foreach (var ch in typeId ?? "x") h = h * 31 + ch;
            return Palette[Mathf.Abs(h) % Palette.Length];
        }

        /// <summary>Builds the figure; returns its root and (out) its height for the HP bar.</summary>
        public static Transform Build(string typeId, string shape, bool boss, Transform parent, int layer, out float height)
        {
            var root = new GameObject("monster:" + typeId) { layer = layer }.transform;
            root.SetParent(parent, false);
            var body = new GameObject("body") { layer = layer }.transform;
            body.SetParent(root, false);

            var mat = MeshKit.Toon;
            var col = boss ? new Color(0.86f, 0.24f, 0.3f) : ColorFor(typeId);
            var dark = MeshKit.Shade(col, 0.72f);
            var b = new MeshKit.Builder();

            // Every shape declares where its face goes: a centre and the radii it is projected on.
            Vector3 fc; Vector3 fr;
            switch (shape)
            {
                case "cube":
                    b.Box(new Vector3(0f, 0.3f, 0f), new Vector3(0.5f, 0.5f, 0.5f), col);
                    b.Box(new Vector3(0f, 0.57f, 0f), new Vector3(0.5f, 0.04f, 0.5f), new Color(0.95f, 0.97f, 1f));
                    fc = new Vector3(0f, 0.3f, 0f); fr = new Vector3(0.25f, 0.25f, 0.25f);
                    break;
                case "diamond":
                    b.Frustum(new Vector3(0f, 0.08f, 0f), 0.005f, 0.28f, 0.27f, col, 1f, 4);
                    b.Frustum(new Vector3(0f, 0.36f, 0f), 0.27f, 0.28f, 0.005f, MeshKit.Shade(col, 1.1f), 1f, 4);
                    fc = new Vector3(0f, 0.36f, 0f); fr = new Vector3(0.2f, 0.2f, 0.2f);
                    break;
                case "spike":
                    b.Ellipsoid(new Vector3(0f, 0.3f, 0f), Vector3.one * 0.27f, col, 20);
                    for (var i = 0; i < 10; i++)
                    {
                        var dir = Quaternion.Euler(i * 36f, i * 67f, 0f) * Vector3.up;
                        if (dir.x > 0.6f) continue; // keep the face clear
                        var save = b.M;
                        b.M = save * Matrix4x4.TRS(new Vector3(0f, 0.3f, 0f) + dir * 0.24f, Quaternion.FromToRotation(Vector3.up, dir), Vector3.one);
                        b.Frustum(Vector3.zero, 0.06f, 0.13f, 0.003f, dark, 1f, 6);
                        b.M = save;
                    }
                    fc = new Vector3(0f, 0.3f, 0f); fr = Vector3.one * 0.27f;
                    break;
                case "sheet":
                    b.Box(new Vector3(0f, 0.34f, 0f), new Vector3(0.06f, 0.6f, 0.46f), new Color(0.96f, 0.97f, 0.99f));
                    for (var i = 0; i < 4; i++)
                        b.Box(new Vector3(0.032f, 0.14f + i * 0.13f, 0f), new Vector3(0.006f, 0.012f, 0.44f), col);
                    fc = new Vector3(0.03f, 0.4f, 0f); fr = new Vector3(0.2f, 0.2f, 0.2f);
                    break;
                case "ghost":
                    b.Ellipsoid(new Vector3(0f, 0.42f, 0f), new Vector3(0.24f, 0.24f, 0.24f), col, 20, _ => 1.7f);
                    b.Frustum(new Vector3(0f, 0.1f, 0f), 0.3f, 0.34f, 0.235f, col, 1f, 20, false);
                    for (var i = 0; i < 6; i++)
                    {
                        var a = i / 6f * Mathf.PI * 2f;
                        b.Ellipsoid(new Vector3(Mathf.Cos(a) * 0.26f, 0.1f, Mathf.Sin(a) * 0.26f), Vector3.one * 0.07f, col, 10);
                    }
                    fc = new Vector3(0f, 0.42f, 0f); fr = Vector3.one * 0.24f;
                    break;
                case "chart":
                    b.Box(new Vector3(0f, 0.05f, 0f), new Vector3(0.5f, 0.1f, 0.4f), dark);
                    b.Box(new Vector3(0f, 0.3f, 0.13f), new Vector3(0.14f, 0.4f, 0.12f), col);
                    b.Box(new Vector3(0f, 0.38f, 0f), new Vector3(0.14f, 0.56f, 0.12f), MeshKit.Shade(col, 1.1f));
                    b.Box(new Vector3(0f, 0.24f, -0.13f), new Vector3(0.14f, 0.28f, 0.12f), col);
                    fc = new Vector3(0.02f, 0.4f, 0f); fr = new Vector3(0.18f, 0.18f, 0.18f);
                    break;
                case "hourglass":
                    b.Frustum(new Vector3(0f, 0.05f, 0f), 0.22f, 0.25f, 0.05f, col, 1f, 16);
                    b.Frustum(new Vector3(0f, 0.3f, 0f), 0.05f, 0.25f, 0.22f, MeshKit.Shade(col, 1.1f), 1f, 16);
                    b.Frustum(new Vector3(0f, 0.02f, 0f), 0.26f, 0.04f, 0.26f, dark, 1f, 16);
                    b.Frustum(new Vector3(0f, 0.55f, 0f), 0.26f, 0.04f, 0.26f, dark, 1f, 16);
                    fc = new Vector3(0f, 0.42f, 0f); fr = new Vector3(0.18f, 0.16f, 0.18f);
                    break;
                case "lock":
                    b.Box(new Vector3(0f, 0.24f, 0f), new Vector3(0.36f, 0.34f, 0.44f), col);
                    for (var i = 0; i <= 10; i++)
                    {
                        var a = Mathf.Lerp(0f, Mathf.PI, i / 10f);
                        b.Ellipsoid(new Vector3(0f, 0.41f + Mathf.Sin(a) * 0.15f, Mathf.Cos(a) * 0.15f), Vector3.one * 0.04f, new Color(0.75f, 0.78f, 0.84f), 8);
                    }
                    fc = new Vector3(0f, 0.24f, 0f); fr = new Vector3(0.18f, 0.18f, 0.18f);
                    break;
                case "bug":
                    b.Ellipsoid(new Vector3(-0.05f, 0.2f, 0f), new Vector3(0.28f, 0.17f, 0.22f), col, 20);
                    b.Ellipsoid(new Vector3(0.18f, 0.25f, 0f), new Vector3(0.15f, 0.14f, 0.15f), dark, 16);
                    for (var i = 0; i < 3; i++)
                        for (var s = -1; s <= 1; s += 2)
                            b.Frustum(new Vector3(-0.15f + i * 0.12f, 0f, s * 0.2f), 0.015f, 0.14f, 0.02f, dark, 1f, 6);
                    for (var s = -1; s <= 1; s += 2)
                        b.Ellipsoid(new Vector3(0.28f, 0.43f, s * 0.07f), new Vector3(0.02f, 0.08f, 0.02f), dark, 6);
                    fc = new Vector3(0.18f, 0.25f, 0f); fr = new Vector3(0.15f, 0.14f, 0.15f);
                    break;
                case "cloud":
                    for (var i = 0; i < 6; i++)
                    {
                        var a = i / 6f * Mathf.PI * 2f;
                        b.Ellipsoid(new Vector3(Mathf.Cos(a) * 0.12f, 0.36f + Mathf.Sin(a * 2f) * 0.04f, Mathf.Sin(a) * 0.18f), Vector3.one * 0.16f,
                                    new Color(0.93f, 0.95f, 1f), 14);
                    }
                    b.Ellipsoid(new Vector3(0f, 0.34f, 0f), new Vector3(0.24f, 0.2f, 0.26f), col, 18);
                    fc = new Vector3(0f, 0.34f, 0f); fr = new Vector3(0.24f, 0.2f, 0.26f);
                    break;
                case "cursor":
                    b.Frustum(new Vector3(0f, 0.08f, 0f), 0.24f, 0.46f, 0.005f, Color.white, 0.35f, 3);
                    b.Box(new Vector3(0f, 0.04f, 0f), new Vector3(0.1f, 0.12f, 0.08f), Color.white);
                    fc = new Vector3(0.04f, 0.2f, 0f); fr = new Vector3(0.12f, 0.12f, 0.12f);
                    break;
                case "monkey":
                case "bull":
                    b.Ellipsoid(new Vector3(0f, 0.26f, 0f), new Vector3(0.26f, 0.25f, 0.27f), col, 20);
                    b.Ellipsoid(new Vector3(0.16f, 0.2f, 0f), new Vector3(0.12f, 0.1f, 0.15f), MeshKit.Shade(col, 1.15f), 14);
                    for (var s = -1; s <= 1; s += 2)
                        if (shape == "monkey")
                            b.Ellipsoid(new Vector3(0f, 0.34f, s * 0.28f), new Vector3(0.05f, 0.08f, 0.04f), dark, 10);
                        else
                        {
                            var save = b.M;
                            b.M = save * Matrix4x4.TRS(new Vector3(0f, 0.44f, s * 0.2f), Quaternion.Euler(s * -40f, 0f, 0f), Vector3.one);
                            b.Frustum(Vector3.zero, 0.05f, 0.16f, 0.004f, new Color(0.95f, 0.92f, 0.85f), 1f, 8);
                            b.M = save;
                        }
                    fc = new Vector3(0f, 0.3f, 0f); fr = new Vector3(0.26f, 0.25f, 0.27f);
                    break;
                default: // blob
                    b.Ellipsoid(new Vector3(0f, 0.22f, 0f), new Vector3(0.3f, 0.23f, 0.3f), col, 22);
                    b.Ellipsoid(new Vector3(-0.02f, 0.38f, 0f), new Vector3(0.12f, 0.08f, 0.12f), MeshKit.Shade(col, 1.12f), 12);
                    fc = new Vector3(0f, 0.22f, 0f); fr = new Vector3(0.3f, 0.23f, 0.3f);
                    break;
            }

            if (boss)
                for (var i = 0; i < 3; i++)
                {
                    // three red "!" flags standing on its head
                    var z = (i - 1) * 0.11f;
                    b.Frustum(new Vector3(-0.02f, fc.y + fr.y * 0.9f, z), 0.012f, 0.18f, 0.012f, new Color(0.3f, 0.3f, 0.35f), 1f, 6);
                    b.Box(new Vector3(-0.02f, fc.y + fr.y * 0.9f + 0.15f, z + 0.045f), new Vector3(0.012f, 0.06f, 0.08f), new Color(1f, 0.25f, 0.25f));
                }

            MeshKit.Part("mesh", body, b.Bake("monster"), mat, layer);

            var face = FaceTexture.For(new FaceTexture.Look { Eye = new Color(0, 0, 0, 0), Hair = Color.black, Monster = true });
            MeshKit.Part("face", body, FaceOn(fc, fr, shape == "cube" || shape == "sheet" || shape == "lock" || shape == "chart"),
                         MeshKit.NewGlass(face), layer);

            var sh = new MeshKit.Builder();
            sh.Quad(new Vector3(0f, 0.004f, 0f), new Vector3(0.32f, 0f, 0f), new Vector3(0f, 0f, 0.26f), new Color(0.1f, 0.12f, 0.2f, 0.35f));
            MeshKit.Part("shadow", root, sh.Bake("shadow"), ChibiBuilder.ShadowMat, layer);

            height = fc.y + fr.y + (boss ? 0.2f : 0.05f);
            return root;
        }

        /// <summary>A face projected on +X: a curved patch over an ellipsoid, or a flat plate on a box.</summary>
        static Mesh FaceOn(Vector3 c, Vector3 r, bool flat)
        {
            var b = new MeshKit.Builder();
            var s = Mathf.Max(r.y, r.z) * 0.95f;
            if (flat)
            {
                b.Quad(c + new Vector3(r.x + 0.004f, 0f, 0f), new Vector3(0f, 0f, s), new Vector3(0f, s, 0f), Color.white);
                return b.Bake("face");
            }
            var rr = r * 1.012f;
            b.Grid(14, 14, (u, v) =>
            {
                var phi = Mathf.Lerp(-1.1f, 1.1f, u);
                var th = Mathf.Lerp(0.5f, 2.6f, v);
                var unit = new Vector3(Mathf.Sin(th) * Mathf.Cos(phi), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(phi));
                var p = c + Vector3.Scale(unit, rr);
                return (p, unit, new Vector2(0.5f + (p.z - c.z) / (2f * s), 0.5f + (p.y - c.y) / (2f * s)));
            }, Color.white);
            return b.Bake("face");
        }
    }
}
