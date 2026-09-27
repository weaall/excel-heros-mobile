using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The joints and materials of one built figure, for ChibiActor to animate.
    /// </summary>
    public class ChibiRig
    {
        public Transform Root, Body, Head, ArmL, ArmR, LegL, LegR, Sheet;
        public Renderer SheetRenderer;
        public bool Sprite;                 // an SD sprite (SdSprite) rather than a built model
        public bool Model3D;
        public Transform[] Base;            // the common SD base skeleton (SdBase.B order), when built on it
        public bool RefModel;               // built on the sample FBX (SdRef): Bip001 bones, extra joints below
        public Transform Spine, ForearmL, ForearmR, CalfL, CalfR;
        public Renderer FaceRenderer;       // the renderer carrying the eye/mouth submesh (SdRef)
        public int EyeSub = -1;             // its submesh index; -1 = no expression swaps
        public string Expression = "";       // the sheet currently shown
        /// <summary>Rest local rotations of the posed bones (the sample's Bip001 rest pose is not
        /// identity): animation offsets are multiplied ONTO these, never assigned over them.</summary>
        public readonly Dictionary<Transform, Quaternion> Rest = new();
        public void Pose(Transform bone, Quaternion offset)
        {
            if (bone == null) return;
            if (!Rest.TryGetValue(bone, out var rest)) { rest = bone.localRotation; Rest[bone] = rest; }
            bone.localRotation = rest * offset;
        }                // the reconstructed, auto-rigged 3D SD (SdModel)
        public Renderer SpriteRenderer;
        public float Height = 0.95f;
        public int SheetSide = 1;           // +1 = the character's left (+Z), -1 = right
        public readonly List<Renderer> Renderers = new();

        /// <summary>
        /// Finds the joints of a built (or instantiated) figure by name. Templates are built once
        /// per character and cloned, so the references have to be found again on every clone.
        /// </summary>
        public static ChibiRig Bind(Transform root, float height)
        {
            var r = new ChibiRig { Root = root, Height = height };
            r.Body = root.Find("body");
            r.Head = root.Find("body/head") ?? r.Body;
            r.ArmL = root.Find("body/armL");
            r.ArmR = root.Find("body/armR");
            r.LegL = root.Find("body/legL");
            r.LegR = root.Find("body/legR");
            r.Sheet = root.Find("sheet");
            foreach (var x in root.GetComponentsInChildren<Renderer>(true)) r.Renderers.Add(x);
            return r;
        }
    }

    /// <summary>
    /// Builds a 3D SD (super-deformed) figure from a paper-doll spec.
    ///
    /// Proportions follow the reference's SD models rather than its standing art: about 2.4 heads,
    /// a big round head with the face painted on, a short tube of a body, stubby limbs, feet that
    /// point where the figure faces. Local space: +X forward, +Y up, +Z the figure's left; the feet
    /// stand on y = 0 and the top of the hair sits near y = 0.95.
    ///
    /// The back sheet is part of the rig — a translucent spreadsheet fixed behind the back and
    /// leaning out past one shoulder (DollDef.sheetSide) — so it follows every pose for free.
    /// </summary>
    public static class ChibiBuilder
    {
        static readonly Color Outline = new(0.106f, 0.114f, 0.145f);
        static readonly Color ShoeCol = new(0.14f, 0.15f, 0.18f);

        static Color SkinOf(string skin) => skin switch
        {
            "light" => MeshKit.Hex("#f6d7c3", Color.white),
            "tan" => MeshKit.Hex("#e0b48a", Color.white),
            _ => MeshKit.Hex("#f9e0cf", Color.white),
        };

        public const float HeadR = 0.2f;
        static readonly Vector3 NeckPivot = new(0f, 0.52f, 0f);
        static readonly Vector3 HeadC = new(0f, 0.2f, 0f);   // relative to the neck pivot

        /// <summary>Builds the figure (without its sheet: that depends on the hero's level, see AddSheet).</summary>
        public static Transform Build(DollDef d, Transform parent, int layer, bool male)
        {
            var root = new GameObject("chibi:" + d.id) { layer = layer }.transform;
            root.SetParent(parent, false);
            var mat = MeshKit.Toon;

            var skin = SkinOf(d.skin);
            var hair = MeshKit.Hex(d.hairColor, new Color(0.2f, 0.17f, 0.15f));
            var top = MeshKit.Hex(d.top, new Color(0.2f, 0.3f, 0.5f));
            var shirt = MeshKit.Hex(d.shirt, Color.white);
            var bottomCol = MeshKit.Hex(d.bottomColor, new Color(0.17f, 0.2f, 0.27f));
            var skirt = d.bottom == "skirt" && !male;
            var dress = d.outfit == "dress";
            var longCoat = d.outfit == "coat" || d.outfit == "labcoat";

            // What the arms and the torso are wearing.
            var torsoCol = d.outfit switch
            {
                "shirt" => top, "vest" => shirt, "apron" => shirt, _ => top,
            };
            var sleeveCol = d.outfit switch
            {
                "vest" => shirt, "apron" => shirt, _ => top,
            };

            var body = new GameObject("body") { layer = layer }.transform;
            body.SetParent(root, false);

            // ---------------------------------------------------------------- legs --
            var legCol = skirt || dress ? skin : bottomCol;
            Leg("legL", body, new Vector3(0f, 0.3f, 0.055f), legCol, skirt || dress, mat, layer);
            Leg("legR", body, new Vector3(0f, 0.3f, -0.055f), legCol, skirt || dress, mat, layer);

            // ---------------------------------------------------------------- torso --
            var t = new MeshKit.Builder();
            var w = male ? 1.06f : 1f;
            // hips / waist
            if (!skirt && !dress)
                t.Frustum(new Vector3(0f, 0.255f, 0f), 0.1f * w, 0.07f, 0.1f * w, bottomCol, 0.78f, 18);
            t.Frustum(new Vector3(0f, 0.29f, 0f), 0.098f * w, 0.2f, 0.092f * w, torsoCol, 0.74f, 18);
            // round shoulders
            t.Ellipsoid(new Vector3(0f, 0.475f, 0f), new Vector3(0.078f, 0.05f, 0.118f * w), torsoCol, 16);
            // neck
            t.Frustum(new Vector3(0f, 0.48f, 0f), 0.03f, 0.06f, 0.028f, skin, 1f, 10);

            if (skirt || dress)
                t.Frustum(new Vector3(0f, 0.17f, 0f), 0.165f, 0.14f, 0.1f, dress ? top : bottomCol, 0.86f, 22);
            if (longCoat)
                t.Frustum(new Vector3(0f, 0.13f, 0f), 0.15f, 0.18f, 0.104f * w, top, 0.82f, 20, false);

            // what shows at the front
            switch (d.outfit)
            {
                case "suit":
                case "coat":
                case "labcoat":
                    // the shirt in the jacket's V
                    t.Box(new Vector3(0.069f, 0.425f, 0f), new Vector3(0.012f, 0.1f, 0.05f), shirt);
                    break;
                case "cardigan":
                    t.Box(new Vector3(0.069f, 0.39f, 0f), new Vector3(0.012f, 0.18f, 0.085f), shirt);
                    break;
                case "hoodie":
                    t.Ellipsoid(new Vector3(-0.055f, 0.5f, 0f), new Vector3(0.06f, 0.045f, 0.1f), MeshKit.Shade(top, 0.88f), 14);
                    t.Box(new Vector3(0.072f, 0.33f, 0f), new Vector3(0.01f, 0.06f, 0.11f), MeshKit.Shade(top, 0.9f));
                    break;
                case "apron":
                    t.Box(new Vector3(0.072f, 0.33f, 0f), new Vector3(0.012f, 0.2f, 0.12f), top);
                    break;
                case "vest":
                    t.Box(new Vector3(0.07f, 0.39f, 0.035f), new Vector3(0.012f, 0.19f, 0.045f), top);
                    t.Box(new Vector3(0.07f, 0.39f, -0.035f), new Vector3(0.012f, 0.19f, 0.045f), top);
                    break;
            }

            // Collar: two small white flaps at the throat, which is most of what makes a tube of
            // colour read as a shirt. Jackets also get dark lapels either side of the V.
            if (d.outfit != "hoodie")
            {
                var collar = d.outfit == "shirt" ? top : shirt;
                for (var cs = -1; cs <= 1; cs += 2)
                {
                    var save = t.M;
                    t.M = save * Matrix4x4.TRS(new Vector3(0.074f, 0.492f, cs * 0.03f), Quaternion.Euler(cs * 32f, 0f, -24f), Vector3.one);
                    t.Box(Vector3.zero, new Vector3(0.034f, 0.02f, 0.055f), MeshKit.Shade(collar, 1.02f));
                    t.M = save;
                }
            }
            if (d.outfit == "suit" || d.outfit == "coat" || d.outfit == "labcoat" || d.outfit == "cardigan")
                for (var cs = -1; cs <= 1; cs += 2)
                {
                    var save = t.M;
                    t.M = save * Matrix4x4.TRS(new Vector3(0.076f, 0.43f, cs * 0.034f), Quaternion.Euler(cs * -18f, 0f, 0f), Vector3.one);
                    t.Box(Vector3.zero, new Vector3(0.008f, 0.1f, 0.016f), MeshKit.Shade(top, 0.78f));
                    t.M = save;
                }

            if (d.Has("tie"))
                t.Box(new Vector3(0.078f, 0.41f, 0f), new Vector3(0.01f, 0.12f, 0.022f), MeshKit.Hex(d.AccColor("tie", "#2f3d5c"), Color.blue));
            if (d.Has("suspenders"))
            {
                var sc = new Color(0.15f, 0.16f, 0.2f);
                t.Box(new Vector3(0.071f, 0.39f, 0.045f), new Vector3(0.008f, 0.2f, 0.012f), sc);
                t.Box(new Vector3(0.071f, 0.39f, -0.045f), new Vector3(0.008f, 0.2f, 0.012f), sc);
            }
            if (d.Has("lanyard"))
            {
                var lc = MeshKit.Hex(d.AccColor("lanyard", "#3b5bd6"), Color.blue);
                t.Box(new Vector3(0.074f, 0.43f, 0.03f), new Vector3(0.008f, 0.09f, 0.008f), lc);
                t.Box(new Vector3(0.074f, 0.43f, -0.03f), new Vector3(0.008f, 0.09f, 0.008f), lc);
                t.Box(new Vector3(0.08f, 0.37f, 0f), new Vector3(0.01f, 0.05f, 0.04f), Color.white);
                t.Box(new Vector3(0.086f, 0.385f, 0f), new Vector3(0.004f, 0.012f, 0.04f), lc);
            }
            if (d.Has("badge"))
                t.Box(new Vector3(0.074f, 0.44f, 0.05f), new Vector3(0.01f, 0.028f, 0.024f), new Color(1f, 0.85f, 0.35f));
            if (d.Has("scarf"))
            {
                var sc = MeshKit.Hex(d.AccColor("scarf", "#ffb7a1"), Color.red);
                t.Frustum(new Vector3(0f, 0.47f, 0f), 0.07f, 0.045f, 0.06f, sc, 0.9f, 16);
                t.Box(new Vector3(0.075f, 0.43f, 0.03f), new Vector3(0.014f, 0.08f, 0.03f), sc);
            }
            MeshKit.Part("torso", body, t.Bake("torso"), mat, layer);

            // ---------------------------------------------------------------- arms --
            var cuff = d.Has("trim") ? MeshKit.Hex(d.AccColor("trim", "#d4a017"), Color.yellow) : (Color?)null;
            Arm("armL", body, new Vector3(0f, 0.47f, 0.118f * w), sleeveCol, skin, cuff, mat, layer, 1f);
            Arm("armR", body, new Vector3(0f, 0.47f, -0.118f * w), sleeveCol, skin, cuff, mat, layer, -1f);

            // ---------------------------------------------------------------- head --
            var head = new GameObject("head") { layer = layer }.transform;
            head.SetParent(body, false);
            head.localPosition = NeckPivot;

            var hb = new MeshKit.Builder();
            hb.Ellipsoid(HeadC, new Vector3(HeadR * 0.98f, HeadR * 0.95f, HeadR * 1.02f), skin, 26);
            // ears, mostly hidden by hair
            hb.Ellipsoid(HeadC + new Vector3(-0.01f, -0.01f, 0.2f), new Vector3(0.03f, 0.04f, 0.018f), skin, 10);
            hb.Ellipsoid(HeadC + new Vector3(-0.01f, -0.01f, -0.2f), new Vector3(0.03f, 0.04f, 0.018f), skin, 10);
            if (d.Has("earring"))
            {
                hb.Ellipsoid(HeadC + new Vector3(-0.01f, -0.055f, 0.21f), Vector3.one * 0.012f, new Color(1f, 0.8f, 0.2f), 8);
                hb.Ellipsoid(HeadC + new Vector3(-0.01f, -0.055f, -0.21f), Vector3.one * 0.012f, new Color(1f, 0.8f, 0.2f), 8);
            }
            MeshKit.Part("skull", head, hb.Bake("skull"), mat, layer);

            // face: painted, projected onto the front of the head
            var faceTex = FaceTexture.For(new FaceTexture.Look
            {
                Eye = MeshKit.Hex(d.eye, new Color(0.35f, 0.55f, 0.85f)), Hair = hair, Male = male,
                Glasses = d.Has("glasses"), Sunglasses = d.Has("sunglasses"),
            });
            var face = MeshKit.Part("face", head, FaceMesh(), MeshKit.NewGlass(faceTex), layer);
            face.GetComponent<MeshRenderer>().sortingOrder = 1;

            var hairB = new MeshKit.Builder();
            Hair(hairB, d.hair, hair, male);
            HeadAcc(hairB, d, hair);
            MeshKit.Part("hair", head, hairB.Bake("hair"), mat, layer);

            // contact shadow
            var sh = new MeshKit.Builder();
            sh.Quad(new Vector3(0f, 0.004f, 0f), new Vector3(0.2f, 0f, 0f), new Vector3(0f, 0f, 0.16f), new Color(0.1f, 0.14f, 0.25f, 0.35f));
            MeshKit.Part("shadow", root, sh.Bake("shadow"), ShadowMat, layer);
            return root;
        }

        static Material _shadow;
        public static Material ShadowMat => _shadow ??= MeshKit.NewGlass(MeshKit.Blob);
        static Mesh _sheetQuad;

        /// <summary>
        /// The back sheet: a translucent spreadsheet fixed behind the back, beside one shoulder.
        /// Its own material, since the texture is the hero's level and rank.
        /// </summary>
        public static void AddSheet(ChibiRig rig, Texture tex, int side, int layer)
        {
            rig.SheetSide = side;
            var pivot = new GameObject("sheet") { layer = layer }.transform;
            pivot.SetParent(rig.Root, false);
            pivot.localPosition = rig.Sprite ? SpriteSheetSpot(rig, side) : new Vector3(-0.17f, 0.66f, side * 0.13f);
            if (rig.Sprite) pivot.localScale = Vector3.one * 1.15f;
            rig.Sheet = pivot;
            if (_sheetQuad == null)
            {
                var sb = new MeshKit.Builder();
                // the four-cell strip (SheetTexture.Aspect ≈ 4.5 : 1)
                sb.Quad(Vector3.zero, new Vector3(0.27f, 0f, 0f), new Vector3(0f, 0.27f / SheetTexture.Aspect, 0f), Color.white);
                _sheetQuad = sb.Bake("sheet");
            }
            var go = MeshKit.Part("sheetQuad", pivot, _sheetQuad, MeshKit.NewGlass(tex), layer);
            rig.SheetRenderer = go.GetComponent<MeshRenderer>();
            rig.Renderers.Add(rig.SheetRenderer);
        }

        /// <summary>On a sprite: just behind the upper back, above the shoulder on the back side.</summary>
        public static Vector3 SpriteSheetSpot(ChibiRig rig, int side) =>
            new(-0.2f, rig.Height * (side > 0 ? 0.6f : 0.52f), 0.06f);

        static Transform Leg(string name, Transform parent, Vector3 pivot, Color col, bool bare, Material mat, int layer)
        {
            var p = new GameObject(name) { layer = layer }.transform;
            p.SetParent(parent, false);
            p.localPosition = pivot;
            var b = new MeshKit.Builder();
            b.Frustum(new Vector3(0f, -0.26f, 0f), 0.036f, 0.26f, 0.046f, col, 1f, 12);
            if (bare) // knee socks: the reference's uniforms nearly always have them
                b.Frustum(new Vector3(0f, -0.265f, 0f), 0.039f, 0.11f, 0.038f, new Color(0.18f, 0.19f, 0.24f), 1f, 12, false);
            b.Ellipsoid(new Vector3(0.022f, -0.262f, 0f), new Vector3(0.062f, 0.034f, 0.045f), ShoeCol, 14);
            MeshKit.Part("mesh", p, b.Bake(name), mat, layer);
            return p;
        }

        static Transform Arm(string name, Transform parent, Vector3 pivot, Color sleeve, Color skin, Color? cuff,
                             Material mat, int layer, float side)
        {
            var p = new GameObject(name) { layer = layer }.transform;
            p.SetParent(parent, false);
            p.localPosition = pivot;
            // hang slightly away from the body
            p.localRotation = Quaternion.Euler(-side * 8f, 0f, 0f);
            var b = new MeshKit.Builder();
            b.Frustum(new Vector3(0f, -0.17f, 0f), 0.034f, 0.17f, 0.038f, sleeve, 1f, 12);
            if (cuff.HasValue) b.Frustum(new Vector3(0f, -0.172f, 0f), 0.037f, 0.022f, 0.037f, cuff.Value, 1f, 12, false);
            b.Ellipsoid(new Vector3(0f, -0.195f, 0f), new Vector3(0.034f, 0.036f, 0.032f), skin, 12);
            MeshKit.Part("mesh", p, b.Bake(name), mat, layer);
            return p;
        }

        /// <summary>The front of the head, a hair's breadth out, with a planar uv facing +X.</summary>
        static Mesh FaceMesh()
        {
            var b = new MeshKit.Builder();
            var r = new Vector3(HeadR * 0.98f, HeadR * 0.95f, HeadR * 1.02f) * 1.012f;
            const float s = HeadR * 0.92f;
            b.Grid(16, 16, (u, v) =>
            {
                var phi = Mathf.Lerp(-1.25f, 1.25f, u);
                var th = Mathf.Lerp(0.55f, 2.7f, v);
                var unit = new Vector3(Mathf.Sin(th) * Mathf.Cos(phi), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(phi));
                var p = HeadC + Vector3.Scale(unit, r);
                var uv = new Vector2(0.5f + (p.z - HeadC.z) / (2f * s), 0.5f + (p.y - HeadC.y) / (2f * s));
                return (p, unit, uv);
            }, Color.white);
            return b.Bake("face");
        }

        // ------------------------------------------------------------------ hair --

        /// <summary>A cap that ends at `front` radians from the top at the forehead, `back` behind.</summary>
        static void Cap(MeshKit.Builder b, Color c, float front, float side, float back, float scale = 1.07f)
        {
            var r = new Vector3(HeadR * 1.0f, HeadR * 0.98f, HeadR * 1.04f) * scale;
            b.Ellipsoid(HeadC + new Vector3(-0.006f, 0.008f, 0f), r, c, 28, phi =>
            {
                var k = (1f - Mathf.Cos(phi)) * 0.5f;  // 0 front, 1 back
                return k < 0.5f ? Mathf.Lerp(front, side, k * 2f) : Mathf.Lerp(side, back, (k - 0.5f) * 2f);
            });
        }

        static Vector3 OnHead(float theta, float phi, float out_ = 1.05f)
        {
            var unit = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
            return HeadC + unit * HeadR * out_;
        }

        /// <summary>
        /// The fringe: one smooth shell over the forehead whose lower edge is cut into `n` points,
        /// the way SD hair is sculpted. (It was a row of beads first, which read as a caterpillar.)
        /// </summary>
        static void Bangs(MeshKit.Builder b, Color c, int n, float drop = 0f)
        {
            var r = new Vector3(HeadR * 1.0f, HeadR * 0.98f, HeadR * 1.04f) * 1.09f;
            var baseEdge = 1.36f + drop * 3f;
            b.Grid(40, 8, (u, v) =>
            {
                var phi = Mathf.Lerp(-1.15f, 1.15f, u);
                // pointed tips: a triangle wave across the width, longest in the middle
                var tri = 1f - Mathf.Abs(Mathf.Repeat(u * n, 1f) - 0.5f) * 2f;
                var edge = baseEdge + tri * 0.16f - Mathf.Abs(phi) * 0.1f;
                var th = Mathf.Lerp(0.25f, edge, v);
                // the fringe stands a little off the forehead at its tips
                var lift = 1f + v * v * 0.05f;
                var unit = new Vector3(Mathf.Sin(th) * Mathf.Cos(phi), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(phi));
                var p = HeadC + new Vector3(-0.006f, 0.008f, 0f) + Vector3.Scale(unit, r) * lift;
                return (p, unit, new Vector2(u, v));
            }, c);
        }

        static void SideLocks(MeshKit.Builder b, Color c, float length)
        {
            for (var s = -1; s <= 1; s += 2)
                b.Ellipsoid(HeadC + new Vector3(0.05f, -0.07f - length * 0.5f, s * 0.185f), new Vector3(0.04f, 0.09f + length * 0.5f, 0.032f), c, 12);
        }

        static void Hair(MeshKit.Builder b, string style, Color c, bool male)
        {
            var dark = MeshKit.Shade(c, 0.86f);
            // The back of an SD head is a round bulb of hair a size up from the skull; without it
            // every style reads as a helmet from the three-quarter camera.
            b.Ellipsoid(HeadC + new Vector3(-0.045f, 0.01f, 0f), new Vector3(HeadR * 0.98f, HeadR * 1.0f, HeadR * 1.06f), c, 22,
                        phi => Mathf.Cos(phi) > 0.2f ? 0.0001f : 2.1f);
            switch (style)
            {
                case "bob":
                    Cap(b, c, 0.95f, 1.95f, 2.2f, 1.1f);
                    Bangs(b, c, 5);
                    SideLocks(b, c, 0.06f);
                    break;
                case "long":
                    Cap(b, c, 0.95f, 1.9f, 2.2f, 1.08f);
                    Bangs(b, c, 5);
                    SideLocks(b, c, 0.16f);
                    b.Ellipsoid(HeadC + new Vector3(-0.11f, -0.2f, 0f), new Vector3(0.09f, 0.27f, 0.19f), dark, 18);
                    break;
                case "ponytail":
                    Cap(b, c, 0.95f, 1.7f, 2.0f);
                    Bangs(b, c, 4);
                    SideLocks(b, c, 0.02f);
                    b.Ellipsoid(HeadC + new Vector3(-0.2f, 0.07f, 0f), Vector3.one * 0.03f, dark, 10);
                    b.Ellipsoid(HeadC + new Vector3(-0.25f, -0.09f, 0f), new Vector3(0.065f, 0.17f, 0.065f), c, 16);
                    break;
                case "bun":
                    Cap(b, c, 0.95f, 1.7f, 1.95f);
                    Bangs(b, c, 4);
                    SideLocks(b, c, 0f);
                    b.Ellipsoid(HeadC + new Vector3(-0.13f, 0.16f, 0f), Vector3.one * 0.085f, c, 16);
                    break;
                case "twin":
                    Cap(b, c, 0.95f, 1.75f, 2.0f);
                    Bangs(b, c, 5);
                    for (var s = -1; s <= 1; s += 2)
                    {
                        b.Ellipsoid(HeadC + new Vector3(-0.06f, 0.1f, s * 0.2f), Vector3.one * 0.035f, dark, 10);
                        b.Ellipsoid(HeadC + new Vector3(-0.08f, -0.1f, s * 0.26f), new Vector3(0.06f, 0.2f, 0.06f), c, 16);
                    }
                    break;
                case "spiky":
                    Cap(b, c, 0.9f, 1.6f, 1.95f);
                    Bangs(b, c, 4, 0.01f);
                    for (var i = 0; i < 7; i++)
                    {
                        var phi = Mathf.PI * (0.35f + i * 0.22f);
                        var th = 0.55f + (i % 2) * 0.35f;
                        var basePt = OnHead(th, phi, 0.98f);
                        var dir = (basePt - HeadC).normalized;
                        var save = b.M;
                        b.M = save * Matrix4x4.TRS(basePt, Quaternion.FromToRotation(Vector3.up, dir), Vector3.one);
                        b.Frustum(Vector3.zero, 0.05f, 0.09f, 0.004f, c, 1f, 8);
                        b.M = save;
                    }
                    break;
                case "curly":
                    Cap(b, c, 0.95f, 1.8f, 2.05f, 1.1f);
                    for (var i = 0; i < 12; i++)
                    {
                        var phi = i / 12f * Mathf.PI * 2f;
                        var th = Mathf.Abs(Mathf.Cos(phi)) > 0.8f && Mathf.Cos(phi) > 0f ? 0.9f : 1.75f;
                        b.Ellipsoid(OnHead(th, phi, 1.08f), Vector3.one * 0.055f, i % 2 == 0 ? c : dark, 10);
                    }
                    Bangs(b, c, 4);
                    break;
                case "side":
                    Cap(b, c, 0.95f, 1.8f, 2.05f);
                    Bangs(b, c, 3);
                    b.Ellipsoid(HeadC + new Vector3(0.06f, -0.02f, 0.17f), new Vector3(0.05f, 0.15f, 0.05f), c, 14);
                    break;
                default: // short
                    Cap(b, c, 0.95f, male ? 1.55f : 1.65f, male ? 1.85f : 1.95f);
                    Bangs(b, c, male ? 4 : 5, male ? 0.012f : 0f);
                    if (!male) SideLocks(b, c, 0f);
                    break;
            }
        }

        static void HeadAcc(MeshKit.Builder b, DollDef d, Color hair)
        {
            if (d.Has("headset") || d.Has("headphones"))
            {
                var col = MeshKit.Hex(d.AccColor(d.Has("headphones") ? "headphones" : "headset", "#2f3440"), new Color(0.2f, 0.2f, 0.25f));
                for (var s = -1; s <= 1; s += 2)
                    b.Ellipsoid(HeadC + new Vector3(0f, -0.005f, s * 0.225f), new Vector3(0.045f, 0.055f, 0.03f), col, 12);
                for (var i = 0; i <= 8; i++)
                {
                    var a = Mathf.Lerp(-1.45f, 1.45f, i / 8f);
                    b.Ellipsoid(HeadC + new Vector3(0f, Mathf.Cos(a) * HeadR * 1.14f, Mathf.Sin(a) * HeadR * 1.14f), Vector3.one * 0.018f, col, 8);
                }
                if (d.Has("headset"))
                    b.Frustum(HeadC + new Vector3(0.02f, -0.09f, -0.2f), 0.007f, 0.06f, 0.007f, col, 1f, 6);
            }
            if (d.Has("cap"))
            {
                var col = MeshKit.Hex(d.AccColor("cap", "#3b5bd6"), Color.blue);
                b.Ellipsoid(HeadC + new Vector3(-0.005f, 0.03f, 0f), new Vector3(HeadR * 1.1f, HeadR * 1.02f, HeadR * 1.13f), col, 22, _ => 1.15f);
                b.Ellipsoid(HeadC + new Vector3(0.17f, 0.075f, 0f), new Vector3(0.11f, 0.014f, 0.13f), MeshKit.Shade(col, 0.9f), 16);
            }
            if (d.Has("hardhat"))
            {
                var col = new Color(1f, 0.8f, 0.15f);
                b.Ellipsoid(HeadC + new Vector3(0f, 0.04f, 0f), new Vector3(HeadR * 1.12f, HeadR * 1.05f, HeadR * 1.14f), col, 22, _ => 1.4f);
                b.Frustum(HeadC + new Vector3(0f, 0.08f, 0f), 0.26f, 0.012f, 0.26f, col, 1f, 20);
            }
            if (d.Has("crown"))
            {
                var gold = new Color(1f, 0.8f, 0.2f);
                b.Frustum(HeadC + new Vector3(0f, 0.19f, 0f), 0.085f, 0.04f, 0.095f, gold, 1f, 16);
                for (var i = 0; i < 5; i++)
                {
                    var a = i / 5f * Mathf.PI * 2f;
                    b.Ellipsoid(HeadC + new Vector3(Mathf.Cos(a) * 0.09f, 0.25f, Mathf.Sin(a) * 0.09f), new Vector3(0.018f, 0.028f, 0.018f), gold, 8);
                }
            }
            if (d.Has("tiara"))
                b.Frustum(HeadC + new Vector3(0.02f, 0.16f, 0f), 0.15f, 0.02f, 0.14f, new Color(0.9f, 0.9f, 1f), 1f, 18, false);
            if (d.Has("flower"))
            {
                var col = MeshKit.Hex(d.AccColor("flower", "#ffd166"), Color.yellow);
                for (var i = 0; i < 5; i++)
                {
                    var a = i / 5f * Mathf.PI * 2f;
                    b.Ellipsoid(HeadC + new Vector3(0.03f, 0.15f + Mathf.Sin(a) * 0.025f, 0.17f + Mathf.Cos(a) * 0.025f), Vector3.one * 0.02f, col, 8);
                }
                b.Ellipsoid(HeadC + new Vector3(0.035f, 0.15f, 0.17f), Vector3.one * 0.014f, Color.white, 8);
            }
        }
    }
}
