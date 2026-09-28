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
        const string MonsterRoot = ArtRoot + "Monsters/";

        /// <summary>
        /// The illustrated UI icons from tools/gen_icons_gemini.py. Cut out, so the alpha is the
        /// whole point — the default branch below drops alpha for the opaque card art, and an icon
        /// imported that way comes out on a black square.
        /// </summary>
        const string IconRoot = ArtRoot + "Icons/";

        /// <summary>
        /// The transparent full-body standing art (web repo tools/art_pipeline.sh). Alpha is the
        /// point — it goes in front of the painted backdrop and the back sheet — so it is DXT5
        /// (BC3) rather than the opaque cards' DXT1. 768x1344 divides by 4, as block compression
        /// needs.
        /// </summary>
        const string StandingRoot = ArtRoot + "Standing/";

        /// <summary>SD battle sprites and stage backdrops: seen small, so they keep mipmaps.</summary>
        const string SdRoot = ArtRoot + "SD/";
        const string BattleRoot = ArtRoot + "Battle/";
        const string SdMonRoot = ArtRoot + "SDMonsters/";

        /// <summary>The 3D monsters' textures (tools/mon3d_pack.py): wrapped on a mesh, so a plain texture with mipmaps.</summary>
        const string Sd3dmRoot = ArtRoot + "SD3DM/";

        /// <summary>The Excel attack sprites (tools/gen_fx_tex.py): alpha cards on quads; the ants dash tiles along x.</summary>
        const string FxRoot = ArtRoot + "Fx/";

        /// <summary>
        /// The UI skin from tools/gen-ui-skin.py. It lives OUTSIDE Resources/Art because nothing
        /// loads it by name at runtime — App.uss references it by path — and it needs the opposite
        /// of what the illustrations need: an alpha channel (the slanted shape is transparent at
        /// the corners), bilinear filtering, and no compression, because DXT on a 178x120 sprite
        /// saves nothing and puts blocking artefacts along a diagonal edge, which is the one place
        /// they are impossible not to see.
        /// </summary>
        const string UiRoot = "Assets/ExcelHeroes/Art/UI/";

        public const string Stamp = "excel-heroes-art-v10";

        /// <summary>
        /// Unity re-imports the assets a postprocessor handles only when this number changes.
        /// Without it, editing the settings above does nothing at all: the .meta files keep what
        /// they were given the first time and the built payload comes out byte for byte identical.
        ///
        /// That is not hypothetical. `Stamp` was added to stop settings being re-applied to assets
        /// that already had them, which is a reasonable guard — but with no version here nothing
        /// ever re-imported, so every change to this file after the first was inert, and the one
        /// that finally got measured had been sitting in the repo doing nothing.
        ///
        /// **Bump this AND `Stamp` together whenever the settings change.** The version makes Unity
        /// re-import; the stamp makes this code agree to touch the asset again.
        /// </summary>
        public override uint GetVersion() => 9;

        // the sample bodies are cut and split at runtime (SdFace, SdSample.StripKit): their meshes
        // must be readable in the player, where FBX meshes default to GPU-only
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ArtRoot + "SDBase/")) return;
            var mi = (ModelImporter)assetImporter;
            if (!mi.isReadable) mi.isReadable = true;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtRoot) && !assetPath.StartsWith(UiRoot)) return;
            var importer = (TextureImporter)assetImporter;
            if (importer.userData == Stamp) return;

            if (assetPath.StartsWith(UiRoot))
            {
                // Texture2D rather than Sprite: App.uss slices these with -unity-slice-left/right,
                // and a Sprite would carry its own border settings as a second, silently
                // disagreeing source of truth for the same numbers.
                importer.textureType = TextureImporterType.Default;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.isReadable = false;
                importer.maxTextureSize = 256;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                ClearPlatformOverrides(importer);
                importer.userData = Stamp;
                return;
            }

            // the sample bodies (git-ignored, third-party): read at runtime to recolour the hair and iris
            if (assetPath.StartsWith(ArtRoot + "SDBase/bodies/") || assetPath.StartsWith(ArtRoot + "SDBase/painted/"))
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;             // the figure is seen from above at a slant in the fight
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.sRGBTexture = true;
                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                ClearPlatformOverrides(importer);
                importer.userData = Stamp;
                return;
            }

            if (assetPath.StartsWith(FxRoot))
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = assetPath.EndsWith("ants.png") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.isReadable = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                ClearPlatformOverrides(importer);
                importer.userData = Stamp;
                return;
            }

            if (assetPath.StartsWith(Sd3dmRoot))
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.sRGBTexture = true;
                importer.isReadable = false;
                importer.maxTextureSize = 1024;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                ClearPlatformOverrides(importer);
                importer.userData = Stamp;
                return;
            }

            if (assetPath.StartsWith(SdRoot) || assetPath.StartsWith(BattleRoot) || assetPath.StartsWith(SdMonRoot))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.isReadable = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                Apply(importer, 2048, TextureImporterFormat.DXT5);
                importer.userData = Stamp;
                return;
            }

            if (assetPath.StartsWith(StandingRoot))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.isReadable = false;
                Apply(importer, 2048, TextureImporterFormat.DXT5);
                importer.userData = Stamp;
                return;
            }

            if (assetPath.StartsWith(IconRoot))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.isReadable = false;
                importer.maxTextureSize = 256;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                ClearPlatformOverrides(importer);
                importer.userData = Stamp;
                return;
            }

            // Pixel art has to be left alone: point filtering, no compression, no mip chain. A
            // 144x28 strip costs 16 KB uncompressed, so there is nothing to save by block-compressing
            // it, and DXT would smear the one-pixel outlines that make the sprites readable at all.
            if (assetPath.StartsWith(SpriteRoot) || assetPath.StartsWith(SheetRoot)
                || assetPath.StartsWith(MonsterRoot))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.isReadable = false;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                ClearPlatformOverrides(importer);
                importer.userData = Stamp;
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;      // everything is drawn 1:1 in UI Toolkit
            importer.isReadable = false;         // a CPU copy would double the cost for nothing

            // Every one of the 198 illustrations is fully opaque — checked, not assumed — so the
            // alpha channel is dropped rather than compressed. DXT5 carries alpha at 8bpp; DXT1
            // does not and costs 4. That is half the texture payload for a channel that is 255
            // everywhere. Rewriting the PNGs as RGB did nothing on its own: the format is named
            // below, so the importer never looks at the file's channel count.
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;

            // Card art is shown full-bleed on the home screen of a 1080-wide phone, so 1024 is the
            // smallest size that does not visibly soften it.
            // 896x1200 halves cleanly to 448x600 — both multiples of 4, which DXT requires. Fitting
            // to 1024 instead lands on 765 wide and silently falls back to uncompressed RGBA32.
            Apply(importer, 2048, TextureImporterFormat.DXT1);
            importer.userData = Stamp;
        }

        /// <summary>
        /// Hands a texture back to its default settings on every platform. Needed because an
        /// override is sticky: it lives in the .meta and outranks whatever the default says, so a
        /// texture that once went through Apply() keeps that format until the override is removed.
        /// </summary>
        static void ClearPlatformOverrides(TextureImporter importer)
        {
            foreach (var platform in new[] { "Standalone", "Android", "iPhone" })
            {
                var settings = importer.GetPlatformTextureSettings(platform);
                if (!settings.overridden) continue;
                settings.overridden = false;
                importer.SetPlatformTextureSettings(settings);
            }
        }

        /// <summary>
        /// Pins a concrete compressed format per platform, because Automatic on an unset build
        /// target resolves to no compression at all.
        ///
        /// `desktop` is the desktop format and says whether alpha is carried: DXT1 for opaque art
        /// at 4bpp, DXT5 for anything that needs a real alpha channel at 8. ASTC 6x6 is the mobile
        /// equivalent either way — it stores alpha without a separate format and is what an Android
        /// build actually ships.
        /// </summary>
        public static void Apply(TextureImporter importer, int maxSize,
                                 TextureImporterFormat desktop = TextureImporterFormat.DXT5)
        {
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.Compressed;

            foreach (var (platform, format) in new[]
            {
                ("Standalone", desktop),
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
