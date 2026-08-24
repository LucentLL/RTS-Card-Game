using System.Collections.Generic;
using System.IO;
using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View.Decks
{
    /// <summary>
    /// Where the player's five decks live. A file rather than PlayerPrefs: this is a document, not
    /// a preference, and a document belongs somewhere a player can back up and a support request
    /// can ask for.
    /// </summary>
    public static class DeckStore
    {
        public const string FileName = "decks.json";

        static string Path { get { return System.IO.Path.Combine(Application.persistentDataPath, FileName); } }

        public static List<SavedDeck> Load(ICardCatalog cat)
        {
            try
            {
                if (!File.Exists(Path)) return new List<SavedDeck>();
                return DeckRules.ReadAll(File.ReadAllText(Path), cat);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[decks] unreadable, starting empty: " + e.Message);
                return new List<SavedDeck>();
            }
        }

        public static void Save(List<SavedDeck> decks)
        {
            try { File.WriteAllText(Path, DeckRules.WriteAll(decks)); }
            catch (System.Exception e) { Debug.LogWarning("[decks] could not save: " + e.Message); }
        }
    }
}
