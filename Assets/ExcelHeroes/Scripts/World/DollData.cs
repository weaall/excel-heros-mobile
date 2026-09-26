using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// One character's paper-doll spec, exported from the web build's dollSprites.js (with the
    /// card art's own colours laid over it) by tools/export-data.mjs → Resources/Data/dolls.json.
    /// It is the blueprint for the 3D SD model: the same spec that drew the pixel doll.
    /// </summary>
    [Serializable]
    public class DollDef
    {
        public string id;
        public string hair;        // short | bob | long | ponytail | bun | twin | spiky | curly | side
        public string hairColor;
        public string skin;        // light | fair | tan
        public string outfit;      // suit | cardigan | shirt | hoodie | apron | dress | coat | labcoat | vest
        public string top, shirt;
        public string bottom;      // pants | skirt
        public string bottomColor;
        public string eye;
        public string sheetSide;   // left | right
        public List<string> acc = new();

        public bool Has(string name) => acc != null && acc.Any(a => a == name || a.StartsWith(name + ":"));

        public string AccColor(string name, string fallback)
        {
            var a = acc?.FirstOrDefault(x => x.StartsWith(name + ":"));
            return a == null ? fallback : a.Substring(name.Length + 1);
        }
    }

    [Serializable]
    class DollFile { public List<DollDef> items = new(); }

    public static class DollData
    {
        static Dictionary<string, DollDef> _byId;

        public static DollDef For(string id)
        {
            if (_byId == null)
            {
                var asset = Resources.Load<TextAsset>("Data/dolls");
                var file = asset != null ? JsonUtility.FromJson<DollFile>(asset.text) : new DollFile();
                _byId = file.items.Where(d => !string.IsNullOrEmpty(d.id)).GroupBy(d => d.id).ToDictionary(g => g.Key, g => g.First());
            }
            if (id != null && _byId.TryGetValue(id, out var d)) return d;
            // Anyone without a spec still gets a figure: a neutral office outfit, hair from the id.
            var h = Mathf.Abs((id ?? "x").GetHashCode());
            string[] styles = { "short", "bob", "long", "ponytail", "bun" };
            return new DollDef
            {
                id = id, hair = styles[h % styles.Length], hairColor = "#3a2f2a", skin = "fair", outfit = "suit",
                top = "#2f4a7a", shirt = "#f4f4f4", bottom = h % 2 == 0 ? "skirt" : "pants", bottomColor = "#2c3345",
                eye = "#5b8fd6", sheetSide = h % 2 == 0 ? "left" : "right",
            };
        }
    }
}
