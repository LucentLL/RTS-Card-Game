using System.Collections.Generic;
using SpawnRowDuel.Data;
using UnityEngine;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// Name → art, built once. The card database is an ordered list keyed for the importer's
    /// benefit, not the view's, and the view asks by DISPLAY NAME because that is the identity the
    /// rules and the registry both use.
    ///
    /// Missing art is EXPECTED and not an error: 27 card and 27 field illustrations are absent by
    /// decision (GAPS G1, "ship placeholders"), so every lookup can legitimately return null and
    /// the frame draws its element wash instead.
    /// </summary>
    public sealed class CardArtIndex
    {
        readonly Dictionary<string, CardDefinition> _byName =
            new Dictionary<string, CardDefinition>(256);

        public CardArtIndex(CardDatabase db)
        {
            if (db == null) return;
            for (int i = 0; i < db.All.Count; i++)
            {
                var def = db.All[i];
                if (def == null || string.IsNullOrEmpty(def.DisplayName)) continue;
                _byName[def.DisplayName] = def;      // last wins; names are unique (validated V2)
            }
        }

        public CardDefinition Find(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return null;
            CardDefinition def;
            return _byName.TryGetValue(displayName, out def) ? def : null;
        }

        public Sprite CardArt(string displayName)
        {
            var def = Find(displayName);
            return def != null ? def.CardArt : null;
        }

        /// <summary>The standee cut-out; falls back to the card illustration (art tier 2 of 3).</summary>
        public Sprite FieldArt(string displayName)
        {
            var def = Find(displayName);
            if (def == null) return null;
            return def.FieldArt != null ? def.FieldArt : def.CardArt;
        }
    }
}
