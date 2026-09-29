using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// Builds the TextCore font assets UI Toolkit draws with.
    ///
    /// Until now the game used Unity's built-in font, which has no Korean at all — every Hangul
    /// glyph came from a fallback the editor picked, at a weight and metric that match nothing else
    /// on screen. It is the single loudest reason the UI read as a prototype: the Excel chrome, the
    /// card names and the battle log were all set in three slightly different faces.
    ///
    /// Noto Sans KR, because it is open-licensed (OFL) so it can ship in a build, it has the full
    /// Hangul range, and its proportions sit close enough to Segoe UI that the spreadsheet chrome
    /// still reads as Office while the game text reads as a game.
    ///
    /// The atlas is DYNAMIC: 11,172 Hangul syllables cannot be baked into a static atlas at a
    /// useful size, and a dynamic one rasterises only what the game actually prints.
    /// </summary>
    public static class MakeFontAssets
    {
        const string Dir = "Assets/ExcelHeroes/Fonts";

        [MenuItem("Excel Heroes/Rebuild font assets")]
        public static void Run()
        {
            foreach (var face in new[] { "NotoSansKR-Medium", "NotoSansKR-Bold", "MaterialSymbolsOutlined" })
            {
                var ttf = $"{Dir}/{face}.ttf";
                var outPath = $"{Dir}/{face} SDF.asset";

                var font = AssetDatabase.LoadAssetAtPath<Font>(ttf);
                if (font == null) { Debug.LogError($"[fonts] missing {ttf}"); continue; }

                if (File.Exists(outPath)) AssetDatabase.DeleteAsset(outPath);

                // 90px sampling into a 2048 atlas with 9px padding: big enough that the 72px banner
                // titles stay crisp, small enough that a page of 24px log rows still fits one atlas.
                var asset = FontAsset.CreateFontAsset(
                    font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                    2048, 2048, AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);

                asset.name = $"{face} SDF";
                AssetDatabase.CreateAsset(asset, outPath);

                // The atlas texture and material live inside the asset file, or the build drops them.
                asset.atlasTextures[0].name = $"{face} Atlas";
                AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
                AssetDatabase.AddObjectToAsset(asset.material, asset);

                EditorUtility.SetDirty(asset);
                Debug.Log($"[fonts] built {outPath}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Point the medium face's 700 weight at the bold one, so `-unity-font-style: bold`
            // draws the real bold cut instead of a synthesised smear of the medium.
            var medium = AssetDatabase.LoadAssetAtPath<FontAsset>($"{Dir}/NotoSansKR-Medium SDF.asset");
            var bold = AssetDatabase.LoadAssetAtPath<FontAsset>($"{Dir}/NotoSansKR-Bold SDF.asset");
            if (medium != null && bold != null)
            {
                // The table is exposed read-only but its entries are mutable in place.
                medium.fontWeightTable[7] = new FontWeightPair { regularTypeface = bold, italicTypeface = bold };
                EditorUtility.SetDirty(medium);
                AssetDatabase.SaveAssets();
                Debug.Log("[fonts] linked bold as weight 700");
            }

            Debug.Log("[fonts] done");
        }

        /// <summary>
        /// The display faces (both OFL, from github.com/google/fonts, licences beside them in
        /// Fonts/OFL): Barlow Condensed for numbers and English labels — the condensed techy
        /// numerals BA and NIKKE set their stats, costs and kickers in — and Black Han Sans for
        /// Korean headlines. Each falls back to Noto Sans KR Bold for any glyph it lacks, so a
        /// mixed "강화 UPGRADE" label never drops a character.
        /// </summary>
        [MenuItem("Excel Heroes/Rebuild display font assets")]
        public static void Display()
        {
            var bold = AssetDatabase.LoadAssetAtPath<FontAsset>($"{Dir}/NotoSansKR-Bold SDF.asset");
            foreach (var face in new[] { "BarlowCondensed-Bold", "BarlowCondensed-ExtraBoldItalic", "BarlowCondensed-SemiBold", "BlackHanSans-Regular" })
            {
                var ttf = $"{Dir}/OFL/{face}.ttf";
                var outPath = $"{Dir}/OFL/{face} SDF.asset";
                var font = AssetDatabase.LoadAssetAtPath<Font>(ttf);
                if (font == null) { Debug.LogError($"[fonts] missing {ttf}"); continue; }
                if (File.Exists(outPath)) AssetDatabase.DeleteAsset(outPath);
                var asset = FontAsset.CreateFontAsset(
                    font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                    1024, 1024, AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);
                asset.name = $"{face} SDF";
                AssetDatabase.CreateAsset(asset, outPath);
                asset.atlasTextures[0].name = $"{face} Atlas";
                AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
                AssetDatabase.AddObjectToAsset(asset.material, asset);
                if (bold != null) asset.fallbackFontAssetTable = new System.Collections.Generic.List<FontAsset> { bold };
                EditorUtility.SetDirty(asset);
                Debug.Log($"[fonts] built {outPath}");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[fonts] display done");
        }
    }
}
