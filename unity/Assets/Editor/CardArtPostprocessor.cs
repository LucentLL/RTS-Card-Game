using UnityEditor;

namespace SpawnRowDuel.EditorPipeline
{
    /// <summary>
    /// Every texture under the card-art junction imports as a Sprite. Without this, a fresh
    /// import in a 3D project defaults to texture type Default and the importer's
    /// FindAssets("t:Sprite") sees nothing.
    /// </summary>
    public sealed class CardArtPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(CardImporter.ArtRoot)) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
        }
    }
}
