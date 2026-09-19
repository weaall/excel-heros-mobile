using UnityEditor;
using UnityEngine;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// Battle sprites arrive from the art pipeline standing on flat magenta, because the image model
    /// cannot produce an alpha channel. This cuts the key colour out at import.
    ///
    /// Magenta is the key because nothing in the cast is near it — skin, hair and office wear all sit
    /// far from pure (1,0,1), so a wide tolerance is safe. The part that actually matters is the
    /// fringe: the model antialiases the character against magenta, leaving a pink halo one or two
    /// pixels wide that a hard threshold leaves behind and which reads as a cheap cut-out. Those
    /// pixels get partial alpha AND their magenta contribution removed, which is what makes the edge
    /// look drawn rather than keyed.
    /// </summary>
    public class SpriteChromaKey : AssetPostprocessor
    {
        const string SpriteRoot = "Assets/ExcelHeroes/Resources/Art/Sprites/";

        // How magenta a pixel has to be before it is background at all, and before it is fully gone.
        const float FringeStart = 0.10f;
        const float FullyKeyed = 0.45f;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(SpriteRoot)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 512;
            // The key has to be read before compression mangles it, so keep this one uncompressed.
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = true;
            importer.userData = "excel-heroes-sprite";
        }

        void OnPostprocessTexture(Texture2D texture)
        {
            if (!assetPath.StartsWith(SpriteRoot)) return;

            var pixels = texture.GetPixels();
            var keyed = 0;

            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];

                // Magenta is high red AND high blue with low green. The weakest of the two
                // "magenta" channels minus green is a stable measure that ignores brightness.
                var magenta = Mathf.Min(p.r, p.b) - p.g;
                if (magenta <= FringeStart) continue;

                if (magenta >= FullyKeyed)
                {
                    pixels[i] = new Color(p.r, p.g, p.b, 0f);
                    keyed++;
                    continue;
                }

                // Antialiased edge: fade it out, and pull the magenta back out of what remains so the
                // surviving pixel is the character's own colour rather than a pink blend of it.
                var t = Mathf.InverseLerp(FringeStart, FullyKeyed, magenta);
                var unfringe = magenta - FringeStart;
                pixels[i] = new Color(
                    Mathf.Clamp01(p.r - unfringe),
                    p.g,
                    Mathf.Clamp01(p.b - unfringe),
                    p.a * (1f - t));
            }

            texture.SetPixels(pixels);
            texture.Apply();

            if (keyed == 0)
                Debug.LogWarning($"[SpriteChromaKey] {assetPath} had no magenta to key — was it generated on the right background?");
        }
    }
}
