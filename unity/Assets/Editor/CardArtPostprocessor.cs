using UnityEditor;
using UnityEngine;

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

            // 512, down from the 2048 default.
            //
            // CardDatabase holds a direct Sprite reference for every card, so Unity loads ALL of
            // them the moment the database loads - 212 illustrations and 184 cut-outs, nearly four
            // hundred textures resident for the whole session. At 1024 square that is hundreds of
            // megabytes of texture memory on a phone, and when the graphics device cannot get what
            // it asks for the failure does not arrive as a tidy error: it arrives as a wasm trap
            // inside SetVertexStateGLES with torn geometry on screen a frame earlier.
            //
            // Nothing draws one bigger than this. The largest use is the inspect card, whose art
            // box is 0.68 of a card that is at most a third of the screen - a few hundred pixels.
            importer.maxTextureSize = 512;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;

            // FULL RECT, not the importer default of Tight.
            //
            // A tight sprite carries its OWN mesh, trimmed to the opaque pixels. Three renderers
            // consume this art and only one of them survives that. The standee assigns the sprite
            // to a SpriteRenderer, which draws whatever mesh it is given and looks right. The
            // board plate re-cuts it through Sprite.Create and passes SpriteMeshType.FullRect
            // explicitly, with a comment saying why - so it looks right too. The HAND hands the
            // raw sprite to a UI Toolkit background with background-size Cover, and Cover is not
            // defined against an arbitrary trimmed hull: it comes out as white shards and black
            // triangles, which is precisely what the corrupted hand cards looked like while the
            // board beside them was perfect.
            //
            // Fixing it at the importer fixes every consumer at once, and makes the FullRect the
            // plate layer passes redundant rather than load bearing.
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
        }

        /// <summary>
        /// Bumped when the settings above change, because an AssetPostprocessor only re-runs on
        /// assets whose importer version differs - without this the two hundred card arts already
        /// in the project keep the mesh type they were first imported with.
        /// </summary>
        public override uint GetVersion() { return 3; }
    }
}
