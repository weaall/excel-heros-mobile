using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// What varies between characters on the common SD base (SdBase): hair style and colour,
    /// eyes, skin, outfit type and colours, accessories. Style flags come from the paper-doll
    /// spec (Resources/Data/dolls.json); colours, when present, from the character's own SD
    /// illustration (Resources/Data/looks.json, sampled by tools/sample_looks.py) so the 3D model
    /// wears exactly what the SD art wears.
    /// </summary>
    public class SdLook
    {
        public string Id, Style;
        public bool Male, Pants, Skirt, Dress, Jacket, Coat, Collar, ShortSleeve, Ahoge, Glasses, Sunglasses;
        public Color Skin, Hair, HairTip, Eye, Top, Shirt, Sleeve, Bottom, Socks, Shoes, Tie, Cuff, Lapel, CollarColor, SkirtHem, Accent;
        public float SockTop = 0.16f;
        // from the spec (sdspec.json): -1 / "" = not set, decided by hash or role
        public int Fringe = -1, Idle = -1, Win = -1;
        public string Attack = "";
        public Color Cap = new(0, 0, 0, 0);     // alpha 0 = no cap
        public bool Headset;
        public string Outfit = "suit";          // suit | shirt | vest | cardigan | hoodie | coat | labcoat | apron | dress
        public string Legwear = "";             // skirts: bare (short socks) | socks (knee-high) | tights; "" = trousers
        public string Eyes = "almond";          // round | almond | sharp | droop
        public string GlassesStyle = "square";  // square | round | oval | half | cat | rimless
        public string Body = "", Lower = "", HairRecipe = "", Accessories = "", Garment = "", Head = "", Prop = "", Face = "";   // Face: another sample's face (SdSample.SwapFace)
        public float Scale = 1f;                // the whole figure, feet on the floor (SdSample)   // Lower: another sample for skirt / legs / shoes (SdSample.SwapLower)                // a sample body (SdSample) worn as-is; "" = the base with our outfit pieces
        public string Iris = "";                // a sample's painted iris (SdRefTex.IrisOf); "" = by eye shape, "none" = our own painted one
        public string HairLib = "";             // a sample's hair mounted on the head (SdRefHairLib); "" = by style, "base" = the base cap only
        public Color GlassesColor = new(0.17f, 0.17f, 0.21f);

        [Serializable] class Row { public string id, hair, top, shirt, bottom, legs, shoes, eye, skin; }
        [Serializable] class File { public List<Row> items = new(); }
        static Dictionary<string, Row> _rows;
        static readonly Dictionary<string, SdLook> Cache = new();

        static Row Sampled(string id)
        {
            if (_rows == null)
            {
                var a = Resources.Load<TextAsset>("Data/looks");
                _rows = a != null ? JsonUtility.FromJson<File>(a.text).items.ToDictionary(r => r.id) : new Dictionary<string, Row>();
            }
            return _rows.TryGetValue(id, out var r) ? r : null;
        }

        static Color H(string hex, Color fb) => MeshKit.Hex(hex, fb);

        public static SdLook For(string heroId)
        {
            var key = heroId == Data.GameData.MainId ? "intern" : heroId;
            if (Cache.TryGetValue(key, out var k)) return k;
            var d = DollData.For(key == "intern" ? "intern" : key);
            var s = Sampled(key);
            var sp = SdSpec.For(key);
            k = new SdLook { Id = key, Style = d.hair ?? "short", Male = key == "intern" };
            if (sp != null && sp.style != "") k.Style = sp.style;
            k.Skin = H(s?.skin, d.skin switch { "light" => H("#f7dccb", Color.white), "tan" => H("#e2b894", Color.white), _ => H("#fbe3d4", Color.white) });
            k.Hair = H(s?.hair, H(d.hairColor, new Color(0.25f, 0.2f, 0.2f)));
            if (k.Male) k.Hair = H("#1d1f2a", Color.black);     // 김인턴: black hair, always (CLAUDE.md)
            k.HairTip = Color.Lerp(k.Hair, Color.white, 0.12f);
            k.Eye = H(s?.eye, H(d.eye, new Color(0.35f, 0.55f, 0.85f)));
            k.Top = H(s?.top, H(d.top, new Color(0.2f, 0.3f, 0.5f)));
            k.Shirt = H(d.shirt, Color.white);
            k.Bottom = H(s?.bottom, H(d.bottomColor, new Color(0.17f, 0.2f, 0.27f)));
            k.Shoes = H(s?.shoes, new Color(0.16f, 0.16f, 0.2f));
            var outfit = d.outfit ?? "suit";
            if (sp != null && sp.outfit != "") outfit = sp.outfit;
            k.Outfit = outfit;
            k.Skirt = !k.Male && d.bottom == "skirt" && outfit != "dress";
            k.Dress = outfit == "dress";
            k.Pants = !k.Skirt && !k.Dress;
            k.Jacket = outfit is "suit" or "coat" or "labcoat" or "cardigan";
            k.Coat = outfit is "coat" or "labcoat";
            k.Collar = outfit is not "hoodie";
            k.ShortSleeve = outfit == "shirt" && key == "intern";
            k.Sleeve = outfit is "vest" or "apron" ? k.Shirt : k.Top;
            k.Socks = H(s?.legs, k.Skirt || k.Dress ? new Color(0.14f, 0.15f, 0.2f) : k.Bottom);
            k.SockTop = k.Skirt ? 0.2f : 0.08f;
            k.Tie = d.Has("tie") ? H(d.AccColor("tie", "#2f3d5c"), Color.blue) : d.Has("scarf") ? H(d.AccColor("scarf", "#ffb7a1"), Color.red) : new Color(0, 0, 0, 0);
            k.Cuff = d.Has("trim") ? H(d.AccColor("trim", "#d4a017"), Color.yellow) : new Color(0, 0, 0, 0);
            k.Lapel = MeshKit.Shade(k.Top, 0.78f);
            k.CollarColor = outfit == "shirt" ? k.Top : k.Shirt;
            k.SkirtHem = d.Has("trim") ? k.Cuff : new Color(0, 0, 0, 0);
            k.Accent = H(d.AccColor("lanyard", "#3b5bd6"), new Color(0.3f, 0.4f, 0.9f));
            k.Glasses = d.Has("glasses");
            k.Sunglasses = d.Has("sunglasses");
            k.Ahoge = BackSheetHash(key) % 3 == 0;
            {
                var eh = 0; foreach (var ch in key) eh = eh * 31 + ch;
                var r = Mathf.Abs(eh / 3) % 100;
                k.Eyes = k.Male ? "sharp" : r < 38 ? "almond" : r < 64 ? "round" : r < 84 ? "sharp" : "droop";
                var g = Mathf.Abs(eh / 7) % 100;
                k.GlassesStyle = g < 30 ? "square" : g < 50 ? "round" : g < 65 ? "oval" : g < 80 ? "half" : g < 92 ? "cat" : "rimless";
                var gc = Mathf.Abs(eh / 11) % 100;
                // navy 35 / black 30 / gold 20 / red 15 % — the frames that read on a chibi face in the
                // Gemini glasses mock-ups; brown and silver vanished into hair and skin
                k.GlassesColor = gc < 35 ? new Color(0.13f, 0.18f, 0.36f) : gc < 65 ? new Color(0.1f, 0.1f, 0.13f) : gc < 85 ? new Color(0.86f, 0.66f, 0.24f) : new Color(0.8f, 0.14f, 0.18f);
            }
            k.Cap = d.Has("cap") ? H(d.AccColor("cap", "#3b5bd6"), Color.blue) : d.Has("hardhat") ? H(d.AccColor("hardhat", "#f5c542"), Color.yellow) : new Color(0, 0, 0, 0);
            k.Headset = d.Has("headset") || d.Has("headphones");
            if (sp != null)
            {
                k.Tie = sp.tie != "" ? H(sp.tie, Color.blue) : new Color(0, 0, 0, 0);
                k.Cap = sp.cap != "" ? H(sp.cap, Color.blue) : new Color(0, 0, 0, 0);
                k.Headset = sp.headset;
                k.Legwear = sp.legwear ?? "";
                if (!string.IsNullOrEmpty(sp.eyes)) k.Eyes = sp.eyes;
                if (!string.IsNullOrEmpty(sp.glassesStyle)) k.GlassesStyle = sp.glassesStyle;
                if (!string.IsNullOrEmpty(sp.hairLib)) k.HairLib = sp.hairLib;
                if (!string.IsNullOrEmpty(sp.body)) k.Body = sp.body;
                if (!string.IsNullOrEmpty(sp.lower)) k.Lower = sp.lower;
                if (!string.IsNullOrEmpty(sp.hairParts)) k.HairRecipe = sp.hairParts;
                if (!string.IsNullOrEmpty(sp.acc)) k.Accessories = sp.acc;
                if (!string.IsNullOrEmpty(sp.garment)) k.Garment = sp.garment;
                if (!string.IsNullOrEmpty(sp.head)) k.Head = sp.head;
                if (!string.IsNullOrEmpty(sp.prop)) k.Prop = sp.prop;
                if (!string.IsNullOrEmpty(sp.face)) k.Face = sp.face;
                if (sp.scale > 0.5f && sp.scale < 1.5f) k.Scale = sp.scale;
                if (!string.IsNullOrEmpty(sp.glassesColor)) k.GlassesColor = H(sp.glassesColor, k.GlassesColor);
                // the spec row is the source of truth for whatever it carries; colours re-derived
                // from it so the shirt / legs / shoes follow a hand edit
                if (sp.hair != "") k.Hair = H(sp.hair, k.Hair);
                if (k.Male) k.Hair = H("#1d1f2a", Color.black);
                k.HairTip = Color.Lerp(k.Hair, Color.white, 0.12f);
                if (sp.eye != "") k.Eye = H(sp.eye, k.Eye);
                if (sp.skin != "") k.Skin = H(sp.skin, k.Skin);
                if (sp.top != "") k.Top = H(sp.top, k.Top);
                if (sp.shirt != "") k.Shirt = H(sp.shirt, k.Shirt);
                if (sp.bottom != "") k.Bottom = H(sp.bottom, k.Bottom);
                if (sp.legs != "") k.Socks = H(sp.legs, k.Socks);
                if (sp.shoes != "") k.Shoes = H(sp.shoes, k.Shoes);
                if (sp.bottomType != "") { k.Skirt = !k.Male && sp.bottomType == "skirt" && !k.Dress; k.Pants = !k.Skirt && !k.Dress; }
                k.Glasses = sp.glasses; k.Sunglasses = sp.sunglasses; k.Ahoge = sp.ahoge;
                k.Fringe = sp.fringe; k.Idle = sp.idle; k.Win = sp.win; k.Attack = sp.attack ?? "";
                k.Lapel = MeshKit.Shade(k.Top, 0.78f);
            }
            Cache[key] = k;
            return k;
        }

        static int BackSheetHash(string id) { var h = 0; foreach (var c in id) h += c; return h; }
    }
}
