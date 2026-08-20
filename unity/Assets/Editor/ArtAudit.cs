using System.IO;
using UnityEditor;
using UnityEngine;

namespace SpawnRowDuel.EditorPipeline
{
    /// <summary>Diagnostics for the art junction: what the AssetDatabase actually sees.</summary>
    public static class ArtAudit
    {
        public static void Run()
        {
            var texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { CardImporter.ArtRoot });
            var spriteGuids = AssetDatabase.FindAssets("t:Sprite", new[] { CardImporter.ArtRoot });
            Debug.Log("[artaudit] t:Texture2D=" + texGuids.Length + " t:Sprite=" + spriteGuids.Length);

            string probe = CardImporter.ArtRoot + "/Creatures/Fire/sparkimp_cardart.png";
            Debug.Log("[artaudit] probe guid for " + probe + " = '" +
                      AssetDatabase.AssetPathToGUID(probe) + "'");
            var obj = AssetDatabase.LoadMainAssetAtPath(probe);
            Debug.Log("[artaudit] probe main asset = " + (obj == null ? "NULL" : obj.GetType().Name));
            var imp = AssetImporter.GetAtPath(probe) as TextureImporter;
            Debug.Log("[artaudit] probe importer = " +
                      (imp == null ? "NULL" : imp.textureType.ToString()));

            int nonSprite = 0;
            foreach (var g in texGuids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti != null && ti.textureType != TextureImporterType.Sprite)
                {
                    nonSprite++;
                    if (nonSprite <= 5) Debug.Log("[artaudit] non-sprite: " + p);
                }
            }
            Debug.Log("[artaudit] non-sprite count=" + nonSprite);
            EditorApplication.Exit(0);
        }
    }
}
