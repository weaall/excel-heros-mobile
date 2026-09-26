using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The battle set: an open-plan office floor seen from a high three-quarter camera, as the
    /// reference's street fights are. The fight runs left to right along the middle of the floor;
    /// desks, partitions, monitors and plants frame it at the back and the near edge so the lane
    /// reads as a place rather than a void. The floor is a spreadsheet — white cells, grey
    /// gridlines, a green selection border round the party's line — which is the disguise
    /// showing through.
    ///
    /// The mood shifts by stage: daytime blue through the windows, evening orange from Phase 20,
    /// overtime night from Phase 40.
    /// </summary>
    public static class OfficeStage
    {
        public static Transform Build(Transform parent, int layer, int stage)
        {
            var root = new GameObject("office") { layer = layer }.transform;
            root.SetParent(parent, false);

            var mood = stage >= 40 ? 2 : stage >= 20 ? 1 : 0;
            Color sky0 = mood switch { 2 => new Color(0.1f, 0.13f, 0.28f), 1 => new Color(1f, 0.72f, 0.5f), _ => new Color(0.62f, 0.82f, 1f) };
            Color sky1 = mood switch { 2 => new Color(0.2f, 0.22f, 0.42f), 1 => new Color(0.98f, 0.86f, 0.7f), _ => new Color(0.86f, 0.94f, 1f) };

            // ---------------------------------------------------------------- floor --
            var floorMat = MeshKit.NewToon(0f, FloorTexture());
            floorMat.SetFloat("_ShadeStrength", 0f);
            floorMat.SetFloat("_Rim", 0f);
            var fb = new MeshKit.Builder();
            fb.Quad(new Vector3(0f, 0f, 1.5f), new Vector3(16f, 0f, 0f), new Vector3(0f, 0f, 9f), Color.white,
                    Vector2.zero, new Vector2(32f, 18f));
            MeshKit.Part("floor", root, fb.Bake("floor"), floorMat, layer);

            // the party's line, marked the way Excel marks a selected range
            var sel = new MeshKit.Builder();
            var green = new Color(0.13f, 0.45f, 0.27f, 0.85f);
            sel.Quad(new Vector3(-2.2f, 0.006f, -1.35f), new Vector3(2.4f, 0f, 0f), new Vector3(0f, 0f, 0.025f), green);
            sel.Quad(new Vector3(-2.2f, 0.006f, 1.35f), new Vector3(2.4f, 0f, 0f), new Vector3(0f, 0f, 0.025f), green);
            sel.Quad(new Vector3(-4.6f, 0.006f, 0f), new Vector3(0.025f, 0f, 0f), new Vector3(0f, 0f, 1.35f), green);
            sel.Quad(new Vector3(0.2f, 0.006f, 0f), new Vector3(0.025f, 0f, 0f), new Vector3(0f, 0f, 1.35f), green);
            sel.Quad(new Vector3(0.2f, 0.007f, -1.35f), new Vector3(0.07f, 0f, 0f), new Vector3(0f, 0f, 0.07f), green);
            MeshKit.Part("selection", root, sel.Bake("sel"), MeshKit.NewGlass(null), layer);

            var set = new MeshKit.Builder();

            // ---------------------------------------------------------------- back wall --
            var wall = new Color(0.93f, 0.95f, 0.98f);
            set.Box(new Vector3(0f, 1.9f, 6.2f), new Vector3(34f, 3.8f, 0.3f), wall);
            set.Box(new Vector3(0f, 0.08f, 6.02f), new Vector3(34f, 0.16f, 0.06f), new Color(0.7f, 0.76f, 0.84f));
            for (var i = -7; i <= 7; i++)
            {
                var x = i * 2.3f;
                // window: two panes of sky with a frame
                for (var k = 0; k < 2; k++)
                {
                    var paneX = x - 0.5f + k * 1f;
                    set.Box(new Vector3(paneX, 1.3f, 6.03f), new Vector3(0.92f, 1.0f, 0.04f), sky0);
                    set.Box(new Vector3(paneX, 2.35f, 6.03f), new Vector3(0.92f, 0.9f, 0.04f), sky1);
                }
                set.Box(new Vector3(x, 1.8f, 6.0f), new Vector3(2.08f, 0.06f, 0.06f), new Color(0.78f, 0.83f, 0.9f));
                set.Box(new Vector3(x, 1.8f, 6.0f), new Vector3(0.06f, 2.0f, 0.06f), new Color(0.78f, 0.83f, 0.9f));
                // pillar between windows
                set.Box(new Vector3(x + 1.15f, 1.9f, 5.95f), new Vector3(0.24f, 3.8f, 0.3f), new Color(0.88f, 0.9f, 0.95f));
                // city silhouettes in the lower panes
                var h = 0.25f + (Mathf.Abs(i * 37) % 5) * 0.12f;
                set.Box(new Vector3(x - 0.5f, 0.8f + h * 0.5f, 6.05f), new Vector3(0.5f, h, 0.02f), MeshKit.Shade(sky0, 0.82f));
                set.Box(new Vector3(x + 0.55f, 0.8f + h * 0.35f, 6.05f), new Vector3(0.35f, h * 0.7f, 0.02f), MeshKit.Shade(sky0, 0.86f));
            }

            // ---------------------------------------------------------------- desks at the back --
            for (var i = -5; i <= 5; i++)
                Desk(set, new Vector3(i * 2.6f + 0.6f, 0f, 4.1f), i % 2 == 0, mood);
            for (var i = -5; i <= 5; i++)
                set.Box(new Vector3(i * 2.6f + 0.6f, 0.45f, 3.35f), new Vector3(2.4f, 0.9f, 0.06f), new Color(0.72f, 0.82f, 0.9f));
            for (var i = -4; i <= 4; i += 2)
                Plant(set, new Vector3(i * 2.6f + 1.9f, 0f, 3.05f));

            // ---------------------------------------------------------------- near edge --
            // Low cabinets and a printer at the edge of frame: foreground the camera looks over.
            for (var i = -3; i <= 3; i++)
            {
                var x = i * 3.4f + 0.3f;
                set.Box(new Vector3(x, 0.32f, -3.2f), new Vector3(1.4f, 0.64f, 0.55f), new Color(0.86f, 0.89f, 0.94f));
                set.Box(new Vector3(x, 0.66f, -3.2f), new Vector3(1.44f, 0.04f, 0.58f), new Color(0.62f, 0.68f, 0.78f));
                for (var k = 0; k < 2; k++)
                    set.Box(new Vector3(x, 0.18f + k * 0.26f, -2.92f), new Vector3(1.2f, 0.018f, 0.01f), new Color(0.55f, 0.6f, 0.7f));
                if (i % 2 == 0)
                {
                    set.Box(new Vector3(x - 0.3f, 0.8f, -3.2f), new Vector3(0.5f, 0.24f, 0.4f), new Color(0.95f, 0.96f, 0.98f));
                    set.Box(new Vector3(x - 0.3f, 0.93f, -3.12f), new Vector3(0.4f, 0.02f, 0.2f), new Color(1f, 1f, 1f));
                }
                else Plant(set, new Vector3(x + 0.45f, 0.66f, -3.2f), 0.6f);
            }

            var setMat = MeshKit.NewToon(0.008f);
            setMat.SetFloat("_ShadeStrength", 0.18f);
            MeshKit.Part("set", root, set.Bake("set"), setMat, layer);

            // monitors glow a little: their screens are a second, unshaded mesh
            var screens = new MeshKit.Builder();
            for (var i = -5; i <= 5; i++)
                for (var k = -1; k <= 1; k += 2)
                    screens.Box(new Vector3(i * 2.6f + 0.6f + k * 0.55f, 1.02f, 3.96f), new Vector3(0.62f, 0.34f, 0.01f),
                                mood == 2 ? new Color(0.45f, 0.95f, 1f) : new Color(0.72f, 0.95f, 0.85f));
            var screenMat = MeshKit.NewToon(0f);
            screenMat.SetFloat("_ShadeStrength", 0f);
            MeshKit.Part("screens", root, screens.Bake("screens"), screenMat, layer);

            return root;
        }

        static void Desk(MeshKit.Builder b, Vector3 at, bool alt, int mood)
        {
            var top = new Color(0.96f, 0.95f, 0.92f);
            var leg = new Color(0.55f, 0.6f, 0.68f);
            b.Box(at + new Vector3(0f, 0.74f, 0f), new Vector3(2.3f, 0.05f, 0.8f), top);
            for (var sx = -1; sx <= 1; sx += 2)
                b.Box(at + new Vector3(sx * 1.1f, 0.36f, 0f), new Vector3(0.05f, 0.72f, 0.7f), leg);
            for (var k = -1; k <= 1; k += 2)
            {
                var m = at + new Vector3(k * 0.55f, 0f, -0.14f);
                b.Box(m + new Vector3(0f, 1.02f, 0.02f), new Vector3(0.68f, 0.4f, 0.04f), new Color(0.16f, 0.18f, 0.22f));
                b.Box(m + new Vector3(0f, 0.84f, 0.06f), new Vector3(0.06f, 0.16f, 0.05f), new Color(0.3f, 0.32f, 0.38f));
                b.Box(m + new Vector3(0f, 0.77f, 0.06f), new Vector3(0.24f, 0.02f, 0.14f), new Color(0.3f, 0.32f, 0.38f));
                b.Box(m + new Vector3(0f, 0.775f, -0.2f), new Vector3(0.42f, 0.015f, 0.13f), new Color(0.85f, 0.87f, 0.9f));
                // chair back, facing the camera side
                b.Box(m + new Vector3(0f, 0.72f, -0.62f), new Vector3(0.5f, 0.5f, 0.07f), alt ? new Color(0.24f, 0.34f, 0.56f) : new Color(0.3f, 0.3f, 0.36f));
                b.Box(m + new Vector3(0f, 0.46f, -0.5f), new Vector3(0.5f, 0.07f, 0.45f), alt ? new Color(0.24f, 0.34f, 0.56f) : new Color(0.3f, 0.3f, 0.36f));
                b.Frustum(m + new Vector3(0f, 0.06f, -0.5f), 0.03f, 0.4f, 0.03f, leg, 1f, 8);
            }
            // paperwork and a mug
            b.Box(at + new Vector3(0.95f, 0.8f, 0.1f), new Vector3(0.24f, 0.08f, 0.32f), Color.white);
            b.Frustum(at + new Vector3(-0.98f, 0.765f, 0.1f), 0.04f, 0.1f, 0.045f, alt ? new Color(0.95f, 0.45f, 0.4f) : new Color(0.4f, 0.65f, 0.95f), 1f, 10);
        }

        static void Plant(MeshKit.Builder b, Vector3 at, float scale = 1f)
        {
            b.Frustum(at, 0.15f * scale, 0.32f * scale, 0.19f * scale, new Color(0.93f, 0.93f, 0.95f), 1f, 12);
            var g0 = new Color(0.36f, 0.7f, 0.45f);
            var g1 = new Color(0.46f, 0.8f, 0.52f);
            for (var i = 0; i < 5; i++)
            {
                var a = i / 5f * Mathf.PI * 2f;
                b.Ellipsoid(at + new Vector3(Mathf.Cos(a) * 0.12f, 0.5f + (i % 2) * 0.12f, Mathf.Sin(a) * 0.12f) * scale,
                            new Vector3(0.14f, 0.2f, 0.14f) * scale, i % 2 == 0 ? g0 : g1, 10);
            }
        }

        /// <summary>One tile of spreadsheet: a white cell with grey gridlines, a little darker every fifth.</summary>
        static Texture2D FloorTexture()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "floor", anisoLevel = 8, filterMode = FilterMode.Trilinear };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var line = x < 2 || y < 2;
                    px[y * n + x] = line ? new Color32(206, 213, 224, 255) : new Color32(247, 249, 252, 255);
                }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }
    }
}
