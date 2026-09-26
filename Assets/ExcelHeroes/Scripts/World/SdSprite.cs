using System.Collections.Generic;
using ExcelHeroes.Data;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// A hero as an SD (chibi) sprite standing in the 3D field: the Hugging Face chibi art
    /// (Resources/Art/SD, feet on one line) on a camera-facing quad, pivoted at the feet, with the
    /// back sheet as a second quad just behind the upper back. The motion is 2D motion — squash,
    /// stretch, hop, lean — which is what an SD figure reads by; a rigid model tilting about did not.
    ///
    /// Returns null when the hero has no SD art yet (the procedural model is the fallback).
    /// </summary>
    public static class SdSprite
    {
        public const float Height = 1.3f;          // world units, canvas top to bottom
        const float CanvasW = 768f, CanvasH = 960f, Feet = 944f;

        static readonly Dictionary<Texture, Material> Mats = new();
        static readonly Dictionary<Sprite, Mesh> Meshes = new();

        public static ChibiRig Build(string heroId, Transform parent, int layer)
        {
            var sprite = GameData.SdArt(heroId);
            return sprite == null ? null : BuildFrom(sprite, Height, CanvasW, CanvasH, Feet, parent, layer, "sd:" + heroId);
        }

        /// <summary>A bestiary monster as an SD sprite (Resources/Art/SDMonsters, 768 square, feet at 752).</summary>
        public static ChibiRig BuildMonster(string typeId, Transform parent, int layer)
        {
            var sprite = GameData.MonsterSd(typeId);
            return sprite == null ? null : BuildFrom(sprite, 0.95f, 768f, 768f, 752f, parent, layer, "sdm:" + typeId);
        }

        static ChibiRig BuildFrom(Sprite sprite, float height, float canvasW, float canvasH, float feet, Transform parent, int layer, string name)
        {
            var root = new GameObject(name) { layer = layer }.transform;
            root.SetParent(parent, false);
            var body = new GameObject("body") { layer = layer }.transform;
            body.SetParent(root, false);

            var quad = MeshKit.Part("sprite", body, MeshFor(sprite, height, canvasW, canvasH, feet), MaterialFor(sprite.texture), layer);
            var rig = new ChibiRig { Root = root, Body = body, Head = body, Height = height * (feet - 40f) / canvasH, Sprite = true };
            rig.SpriteRenderer = quad.GetComponent<MeshRenderer>();
            rig.Renderers.Add(rig.SpriteRenderer);

            var sh = new MeshKit.Builder();
            sh.Quad(new Vector3(0f, 0.004f, 0f), new Vector3(0.3f, 0f, 0f), new Vector3(0f, 0f, 0.2f), new Color(0.1f, 0.14f, 0.25f, 0.4f));
            MeshKit.Part("shadow", root, sh.Bake("shadow"), ChibiBuilder.ShadowMat, layer);
            return rig;
        }

        /// <summary>A quad the shape of the canvas, bottom edge at the soles, uv from the whole canvas.</summary>
        static Mesh MeshFor(Sprite s, float height, float canvasW, float canvasH, float feet)
        {
            if (Meshes.TryGetValue(s, out var m) && m != null) return m;
            var w = height * canvasW / canvasH;
            var below = height * (canvasH - feet) / canvasH;          // canvas below the soles
            var tex = s.texture;
            var r = s.rect;
            var uv0 = new Vector2(r.xMin / tex.width, r.yMin / tex.height);
            var uv1 = new Vector2(r.xMax / tex.width, r.yMax / tex.height);
            var b = new MeshKit.Builder();
            b.Quad(new Vector3(0f, height * 0.5f - below, 0f), new Vector3(w * 0.5f, 0f, 0f), new Vector3(0f, height * 0.5f, 0f), Color.white, uv0, uv1);
            m = b.Bake("sd:" + s.name);
            Meshes[s] = m;
            return m;
        }

        static Material MaterialFor(Texture tex)
        {
            if (Mats.TryGetValue(tex, out var m) && m != null) return m;
            m = MeshKit.NewGlass(tex);
            Mats[tex] = m;
            return m;
        }
    }
}
