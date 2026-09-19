using UnityEngine;
using UnityEditor;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// Import settings for the generated art. It is dropped straight into Resources by
    /// tools/gen-art.mjs with no settings of its own, so everything it needs is pinned here.
    ///
    /// The compression is the point. Leaving format on Automatic produced RGB24 — uncompressed
    /// 24-bit — across the whole set, which the art audit measured at 670 MB of runtime memory for
    /// 339 textures. Automatic is a negotiation with the platform, and on an unset build target it
    /// resolves to "no compression at all". Naming a format per platform is the only way to be sure,
    /// and it is worth roughly 6x.
    ///
    /// Bump `Stamp` whenever these settings change: the guard below skips anything already
    /// configured, so without a new stamp existing assets keep their old settings forever.
    /// </summary>
    public class ArtImportSettings : AssetPostprocessor
    {
        const string ArtRoot = "Assets/ExcelHeroes/Resources/Art/";
        const string SpriteRoot = ArtRoot + "Sprites/";
        const string SheetRoot = ArtRoot + "Sheets/";
        public const string Stamp = "excel-heroes-art-v4";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtRoot)) return;
            var importer = (TextureImporter)assetImporter;
            if (importer.userData == Stamp) return;

            // Pixel art has to be left alone: point filtering, no compression, no mip chain. A
            // 144x28 strip costs 16 KB uncompressed, so there is nothing to save by block-compressing
            // it, and DXT would smear the one-pixel outlines that make the sprites readable at all.
            if (assetPath.StartsWith(SpriteRoot) || assetPath.StartsWith(SheetRoot))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.isReadable = false;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.userData = Stamp;
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;      // everything is drawn 1:1 in UI Toolkit
            importer.alphaIsTransparency = true;
            importer.isReadable = false;         // a CPU copy would double the cost for nothing

            // Card art is shown full-bleed on the home screen of a 1080-wide phone, so 1024 is the
            // smallest size that does not visibly soften it.
            // 896x1200 halves cleanly to 448x600 — both multiples of 4, which DXT requires. Fitting
            // to 1024 instead lands on 765 wide and silently falls back to uncompressed RGBA32.
            Apply(importer, 2048);
            importer.userData = Stamp;
        }

        /// <summary>
        /// Pins a concrete compressed format per platform. DXT1 covers desktop; ASTC 6x6 is the
        /// mobile equivalent and is what an Android build actually ships.
        /// </summary>
        public static void Apply(TextureImporter importer, int maxSize)
        {
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.Compressed;

            foreach (var (platform, format) in new[]
            {
                ("Standalone", TextureImporterFormat.DXT5),
                ("Android", TextureImporterFormat.ASTC_6x6),
                ("iPhone", TextureImporterFormat.ASTC_6x6),
            })
            {
                var settings = importer.GetPlatformTextureSettings(platform);
                settings.overridden = true;
                settings.maxTextureSize = maxSize;
                settings.format = format;
                settings.compressionQuality = 50;
                importer.SetPlatformTextureSettings(settings);
            }
        }
    }
}
