using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The per-character SD model spec (Resources/Data/sdspec.json, written by tools/sd_spec.py):
    /// the one row everything about a character's 3D SD is read from — hair style and fringe,
    /// the sampled colours, props, outfit type, and which idle / victory / attack it uses. Empty
    /// strings and -1 mean "not set" (SdLook falls back to dolls.json / looks.json / a hash).
    /// </summary>
    [Serializable]
    public class SdSpecRow
    {
        public string id = "", style = "", hair = "", eye = "", skin = "", top = "", shirt = "", bottom = "", legs = "", shoes = "";
        public string outfit = "", bottomType = "", attack = "", tie = "", cap = "", legwear = "", eyes = "";
        public int fringe = -1, idle = -1, win = -1;
        public bool ahoge, glasses, sunglasses, headset;
        public string[] manual = Array.Empty<string>();
        public bool Has(string field) => manual != null && manual.Contains(field);
    }

    public static class SdSpec
    {
        [Serializable] class File { public List<SdSpecRow> items = new(); }
        static Dictionary<string, SdSpecRow> _rows;

        public static SdSpecRow For(string id)
        {
            if (_rows == null)
            {
                var a = Resources.Load<TextAsset>("Data/sdspec");
                _rows = a != null ? JsonUtility.FromJson<File>(a.text).items.Where(r => !string.IsNullOrEmpty(r.id)).GroupBy(r => r.id).ToDictionary(g => g.Key, g => g.First())
                                  : new Dictionary<string, SdSpecRow>();
            }
            return id != null && _rows.TryGetValue(id, out var r) ? r : null;
        }
    }
}
