using System.Collections.Generic;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// Hair from the other samples, mounted on the base head. Every sample shares the Bip001 rig
    /// with the head bone at the same spot, so a hair mesh saved in head-bone-local space
    /// (Assets/_Ref/Editor/HairLibExtract.cs → Resources/Art/SDBase/hairlib/&lt;name&gt;.asset, git-
    /// ignored like the base) sits right when parented to our Head bone at identity. Its own hair
    /// sheet is repainted in the character's colour (SdRefTex.Hair). Rigid (no chain bones) —
    /// the base cap's chains are hidden with the base hair.
    /// </summary>
    public static class SdRefHairLib
    {
        static readonly Dictionary<string, Mesh> Meshes = new();
        static readonly Dictionary<string, Texture2D> Sheets = new();
        static readonly HashSet<string> Missing = new();

        /// <summary>Which sample's hair a style takes, by a stable hash of the id; null = keep the base cap.</summary>
        public static string Pick(string style, string id)
        {
            var h = SdPose.Hash(id) / 13;
            return style switch
            {
                "twin" => h % 2 == 0 ? "miku" : "haruka",
                "long" => (h % 3) switch { 0 => "kayoko", 1 => "hikari", _ => "mika" },
                "side" => "natsu",
                "ponytail" => h % 2 == 0 ? "yuuka" : null,
                "curly" => "reisa",
                _ => null,
            };
        }

        public static bool Has(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "base" || Missing.Contains(name)) return false;
            if (Meshes.ContainsKey(name)) return true;
            var m = Resources.Load<Mesh>("Art/SDBase/hairlib/" + name);
            var t = Resources.Load<Texture2D>("Art/SDBase/hairlib/" + name + "_hair");
            if (m == null || t == null) { Missing.Add(name); return false; }
            Meshes[name] = m; Sheets[name] = t;
            return true;
        }

        /// <summary>Mounts the named hair on the head bone in the character's colour. False when the library lacks it.</summary>
        public static bool Mount(ChibiRig rig, string name, SdLook k, int layer)
        {
            if (rig.Head == null || !Has(name)) return false;
            var mat = MeshKit.NewToon(0.004f, SdRefTex.Hair(k, Sheets[name]));
            mat.SetFloat("_ShadeStrength", 0.22f);
            var go = MeshKit.Part("hair:" + name, rig.Head, Meshes[name], mat, layer);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            rig.Renderers.Add(go.GetComponent<MeshRenderer>());
            return true;
        }
    }
}
