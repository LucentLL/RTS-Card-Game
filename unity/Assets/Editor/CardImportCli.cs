using System;
using UnityEditor;
using UnityEngine;

namespace SpawnRowDuel.EditorPipeline
{
    /// <summary>
    /// Batchmode entry points for the card pipeline (design 03 s5.7). The CI guard this enables:
    /// someone edits the JS registry, forgets to re-export or re-import, and Unity would ship
    /// stale stats - Verify catches it as a red build instead.
    /// </summary>
    public static class CardImportCli
    {
        /// <summary>tools/regen-cards.sh: node export first, then this writes the assets.</summary>
        public static void ImportAndExit()
        {
            try
            {
                CardImporter.Run(true, false);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>CI: dry-run reimport; ANY would-be change means the committed assets are stale.</summary>
        public static void Verify()
        {
            try
            {
                var report = CardImporter.Run(false, true);
                var db = AssetDatabase.LoadAssetAtPath<SpawnRowDuel.Data.CardDatabase>(
                    CardImporter.DatabasePath);

                bool hashStale = db == null ||
                    db.SourceHash != CardImporter.HashOfFile(CardImporter.CardsJsonPath);

                if (report.Drift > 0 || hashStale)
                {
                    Debug.LogError("Card assets are STALE vs cards.json (" +
                        report.Created.Count + " new, " + report.Updated.Count + " changed, " +
                        report.Orphans.Count + " orphaned, hashStale=" + hashStale +
                        "). Run: node tools/export_cards.mjs, then bash tools/regen-cards.sh, " +
                        "and commit the result.");
                    EditorApplication.Exit(1);
                    return;
                }
                Debug.Log("[cards] Verify OK - assets match cards.json");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                EditorApplication.Exit(1);
            }
        }
    }
}
