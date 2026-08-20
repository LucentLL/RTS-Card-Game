using System.IO;
using UnityEngine;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// The one place tests load the real registry from. cards.json lives outside Assets/ on
    /// purpose (one copy, no dead TextAsset import), so the path is repo-relative.
    /// </summary>
    public static class TestData
    {
        private static CardCatalog _catalog;
        private static ValidationReport _report;

        public static string CardsJsonPath
        {
            get
            {
                return Path.GetFullPath(
                    Path.Combine(Application.dataPath, "../../docs/unity/spec/cards.json"));
            }
        }

        public static CardCatalog Catalog
        {
            get
            {
                EnsureLoaded();
                return _catalog;
            }
        }

        public static ValidationReport Report
        {
            get
            {
                EnsureLoaded();
                return _report;
            }
        }

        private static void EnsureLoaded()
        {
            if (_catalog != null) return;
            var json = File.ReadAllText(CardsJsonPath);
            _catalog = CardsJsonCatalog.Load(json, out _report);
        }
    }
}
