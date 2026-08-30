using System.Collections.Generic;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Net
{
    /// <summary>
    /// A canonical, ordered view of every card id in the catalog, plus a fingerprint of that
    /// order. Two jobs, both about keeping frames small and honest.
    ///
    /// **Small**: a deck crosses the wire as 40 indices into this list, not 40 "fire|Sparkimp"
    /// strings - about 120 bytes instead of about 600. ntfy caps a message at 4 KB, and the
    /// handshake carries BOTH decks, so this is not a micro-optimisation.
    ///
    /// **Honest**: indices are only meaningful if both peers hold identical card data, so the
    /// handshake exchanges the fingerprint first. Two builds with different cards then fail with
    /// "your card data differs from your opponent's" in the lobby, instead of desyncing at a ply
    /// twenty turns later when someone draws the card that moved. Registry order is already
    /// load-bearing in this project (deckOf's draw order, the commander pick), so it is exactly
    /// the right thing to pin.
    /// </summary>
    public sealed class CardRegistry
    {
        readonly CardId[] _ids;
        readonly Dictionary<CardId, int> _index;
        readonly ulong _fingerprint;

        public CardRegistry(ICardCatalog cat)
        {
            var ids = new List<CardId>(cat.Creatures.Count + cat.Spells.Count);
            for (int i = 0; i < cat.Creatures.Count; i++) ids.Add(cat.Creatures[i].Id);
            for (int i = 0; i < cat.Spells.Count; i++) ids.Add(cat.Spells[i].Id);

            _ids = ids.ToArray();
            _index = new Dictionary<CardId, int>(_ids.Length);
            for (int i = 0; i < _ids.Length; i++) _index[_ids[i]] = i;   // first wins; order is the contract

            var h = Sha256.Hash(Digest(cat).ToArray());
            ulong f = 0;
            for (int i = 0; i < 8; i++) f |= (ulong)h[i] << (i * 8);
            _fingerprint = f;
        }

        public int Count { get { return _ids.Length; } }

        /// <summary>64 bits over every RULE-BEARING value in the catalog, in registry order.</summary>
        public ulong Fingerprint { get { return _fingerprint; } }

        /// <summary>
        /// Every number and flag the engine can read out of the catalog, in registry order.
        ///
        /// Ids alone would not do. Two builds where one card's attack changed have identical id
        /// lists AND identical opening state hashes - the opening board is empty, hands carry
        /// only id and colour, and no statline reaches the codec until a creature is summoned.
        /// So the opening-hash check cannot see a rebalanced card, and the peers would play
        /// happily until the ply that card first hits the board. Hashing the VALUES is what turns
        /// that into a sentence in the lobby.
        ///
        /// Presentation is deliberately excluded - slugs, glyphs, lore and descriptions. A new
        /// piece of card art must not stop two friends playing.
        /// </summary>
        static ByteWriter Digest(ICardCatalog cat)
        {
            var w = new ByteWriter(8192);

            w.Int(cat.DeckSize);
            w.Int(cat.MaxCopies);

            w.VarInt((ulong)cat.Creatures.Count);
            for (int i = 0; i < cat.Creatures.Count; i++)
            {
                var c = cat.Creatures[i];
                w.String(c.Id.Value); w.String(c.Name);
                w.Byte((byte)c.Element); w.Int(c.PoolIndex);
                w.Int(c.Cost); w.Int(c.Attack); w.Int(c.Health); w.Int(c.Upkeep);
                w.Bool(c.FirstStrike); w.Bool(c.Entrench); w.Byte((byte)c.Keyword);
                Nullable(w, c.Detonate); Nullable(w, c.Reap); Nullable(w, c.WardHp);
                Nullable(w, c.Grow); Nullable(w, c.Hatch);
                w.Byte((byte)c.Tribe); w.Byte((byte)c.Subtype); w.Bool(c.Deckable);
            }

            w.VarInt((ulong)cat.Spells.Count);
            for (int i = 0; i < cat.Spells.Count; i++)
            {
                var s = cat.Spells[i];
                w.String(s.Id.Value); w.String(s.Name);
                w.Int(s.Cost); w.Bool(s.IsTrap); w.Byte((byte)s.Effect);
                Nullable(w, s.Value); w.Byte((byte)s.Target); w.Byte((byte)s.Trigger);
            }

            w.VarInt((ulong)cat.Structures.Count);
            for (int i = 0; i < cat.Structures.Count; i++)
            {
                var b = cat.Structures[i];
                w.String(b.ExportKey); w.String(b.Bid.Value); w.String(b.Name);
                w.Int(b.Cost); w.Int(b.MaxHp); w.Byte((byte)b.Effect);
                w.Int(b.Value); w.Int(b.Support); w.Byte((byte)b.Element);
                Strings(w, b.Prereqs);
                w.String(b.UpgradedFrom.Value);
                Strings(w, b.UpgradeTargets);
                w.Byte((byte)b.RowGate); w.Bool(b.Buildable);
            }

            w.VarInt((ulong)cat.Commanders.Count);
            for (int i = 0; i < cat.Commanders.Count; i++)
            {
                var c = cat.Commanders[i];
                w.String(c.Id.Value); w.String(c.Name);
                w.Int(c.Hp); w.Int(c.Workers); w.Bool(c.Dual);
                w.VarInt((ulong)c.Colors.Length);
                for (int k = 0; k < c.Colors.Length; k++) w.Byte((byte)c.Colors[k]);
                Strings(w, c.BuildListRaw);
            }

            w.VarInt((ulong)cat.Elements.Count);
            for (int i = 0; i < cat.Elements.Count; i++)
            {
                var e = cat.Elements[i];
                w.Byte((byte)e.El); w.String(e.Key);
                w.Int(e.Hp); w.Int(e.Workers); w.Bool(e.Deckable);
            }

            return w;
        }

        /// <summary>The registry's null-vs-zero distinction is a rule, so it is in the digest.</summary>
        static void Nullable(ByteWriter w, int? v)
        {
            w.Bool(v.HasValue);
            w.Int(v.HasValue ? v.Value : 0);
        }

        static void Strings(ByteWriter w, string[] items)
        {
            w.VarInt((ulong)items.Length);
            for (int i = 0; i < items.Length; i++) w.String(items[i]);
        }

        public bool TryIndexOf(CardId id, out int index)
        {
            return _index.TryGetValue(id, out index);
        }

        public CardId At(int index)
        {
            if (index < 0 || index >= _ids.Length)
                throw new WireFormatException("card index out of range: " + index);
            return _ids[index];
        }

        // ---- deck lists ---------------------------------------------------------------------

        public void WriteDeck(ByteWriter w, IReadOnlyList<HandCard> deck)
        {
            w.VarInt((ulong)deck.Count);
            for (int i = 0; i < deck.Count; i++)
            {
                int index;
                if (!TryIndexOf(deck[i].Id, out index))
                    throw new WireFormatException("deck holds a card the catalog does not: "
                                                  + deck[i].Id.Value);
                w.VarInt((ulong)index);
                w.Byte((byte)deck[i].Color);
            }
        }

        /// <summary>Null for "no deck sent" - the commander rolls one, as it always has.</summary>
        public List<HandCard> ReadDeck(ByteReader r)
        {
            ulong n = r.VarInt();
            if (n == 0) return null;
            if (n > 400) throw new WireFormatException("absurd deck size " + n);

            var deck = new List<HandCard>((int)n);
            for (ulong i = 0; i < n; i++)
            {
                int index = (int)r.VarInt();
                var color = (Element)r.Enum(10, "deck card colour");
                deck.Add(new HandCard(At(index), color));
            }
            return deck;
        }
    }
}
