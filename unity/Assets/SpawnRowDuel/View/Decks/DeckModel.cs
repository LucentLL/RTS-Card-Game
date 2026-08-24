using System.Collections.Generic;
using System.Text;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.View.Decks
{
    /// <summary>
    /// A deck the player built and named. Cards are counted by KEY, not listed one per line: a
    /// deck is "three of these" far more often than it is forty distinct things.
    ///
    /// The key is `element|name`, with spells filed under `neutral`. It carries the element
    /// because two commanders can put the same card name in reach through different colours, and
    /// a deck that stored only names could not tell a Fire Longhouse from a Water one.
    /// </summary>
    public sealed class SavedDeck
    {
        public string Name = "";
        public CommanderId Commander = new CommanderId("fire");
        public readonly Dictionary<string, int> Cards = new Dictionary<string, int>();

        public int Total
        {
            get { int n = 0; foreach (var kv in Cards) n += kv.Value; return n; }
        }

        public SavedDeck Clone()
        {
            var d = new SavedDeck { Name = Name, Commander = Commander };
            foreach (var kv in Cards) d.Cards[kv.Key] = kv.Value;
            return d;
        }

        public int CountOf(string key)
        {
            int n;
            return Cards.TryGetValue(key, out n) ? n : 0;
        }

        public void Set(string key, int count)
        {
            if (count <= 0) Cards.Remove(key);
            else Cards[key] = count;
        }
    }

    /// <summary>
    /// What makes a deck legal, and what a card key means. Shared by the builder, the store and
    /// whoever turns a deck into a real draw pile - three copies of this rule is how a deck that
    /// saves cleanly fails to load.
    /// </summary>
    public static class DeckRules
    {
        public const int Size = 40;
        public const int MaxCopies = 3;
        public const int MaxDecks = 5;

        public static string Key(Element el, string name)
        {
            return (el == Element.None ? "neutral" : SpawnRowDuel.Campaign.CampaignRules.Name(el).ToLowerInvariant())
                   + "|" + name;
        }

        public static bool Split(string key, out Element el, out string name)
        {
            el = Element.None; name = null;
            if (string.IsNullOrEmpty(key)) return false;
            int bar = key.IndexOf('|');
            if (bar <= 0 || bar >= key.Length - 1) return false;
            var head = key.Substring(0, bar);
            el = head == "neutral" ? Element.None : SpawnRowDuel.Campaign.CampaignRules.FromName(head);
            name = key.Substring(bar + 1);
            return true;
        }

        /// <summary>Every card a commander may put in a deck: its colours' creature pools, plus
        /// the neutral spells. Structures are NOT deckable - the commander builds those.</summary>
        public static List<string> PoolFor(ICardCatalog cat, CommanderDef cc)
        {
            var keys = new List<string>();
            if (cat == null || cc == null) return keys;

            foreach (var col in cc.Colors)
                foreach (var c in cat.PoolOf(col))
                    keys.Add(Key(col, c.Name));

            foreach (var s in cat.Spells) keys.Add(Key(Element.None, s.Name));
            return keys;
        }

        /// <summary>
        /// The first thing wrong with this deck, or null. Order matters: a player fixing a deck
        /// wants the blocking problem, not a wall of them.
        /// </summary>
        public static string FirstError(ICardCatalog cat, SavedDeck deck)
        {
            if (deck == null) return "No deck.";
            CommanderDef cc;
            if (cat == null || !cat.TryCommander(deck.Commander, out cc)) return "Unknown leader.";

            var legal = new HashSet<string>(PoolFor(cat, cc));

            foreach (var kv in deck.Cards)
            {
                Element el; string name;
                if (!Split(kv.Key, out el, out name)) return "Unknown card.";
                if (!legal.Contains(kv.Key)) return name + " is off-colour.";
                if (kv.Value < 1 || kv.Value > MaxCopies) return name + " must be 1–" + MaxCopies + ".";
            }

            int total = deck.Total;
            if (total != Size) return "Need exactly " + Size + " cards (have " + total + ").";
            return null;
        }

        public static bool IsLegal(ICardCatalog cat, SavedDeck deck)
        {
            return FirstError(cat, deck) == null;
        }

        /// <summary>Turn a saved deck into a draw pile, shuffled by the match's own RNG so the
        /// same seed deals the same opening whether the deck was built or rolled.</summary>
        public static List<HandCard> ToDrawPile(ICardCatalog cat, SavedDeck deck, Pcg32 rng)
        {
            var pile = new List<HandCard>(Size);
            foreach (var kv in deck.Cards)
            {
                Element el; string name;
                if (!Split(kv.Key, out el, out name)) continue;
                for (int i = 0; i < kv.Value; i++) pile.Add(new HandCard(new CardId(name), el));
            }
            if (rng != null) rng.Shuffle(pile);
            return pile;
        }

        // ── storage format ──────────────────────────────────────────────────────────────

        public static string WriteAll(List<SavedDeck> decks)
        {
            var sb = new StringBuilder(2048);
            sb.Append("{\"schema\":1,\"decks\":[");
            for (int i = 0; i < decks.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var d = decks[i];
                sb.Append("{\"name\":").Append(JsonString(d.Name));
                sb.Append(",\"cc\":").Append(JsonString(d.Commander.Value));
                sb.Append(",\"cards\":{");
                bool first = true;
                foreach (var kv in d.Cards)
                {
                    if (!first) sb.Append(',');
                    sb.Append(JsonString(kv.Key)).Append(':').Append(kv.Value);
                    first = false;
                }
                sb.Append("}}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static List<SavedDeck> ReadAll(string json, ICardCatalog cat)
        {
            var list = new List<SavedDeck>();
            if (string.IsNullOrEmpty(json)) return list;
            try
            {
                var root = JsonValue.Parse(json);
                var arr = root != null ? root.Get("decks") : null;
                if (arr == null || arr.Type != JsonType.Array) return list;

                for (int i = 0; i < arr.Count; i++)
                {
                    var o = arr[i];
                    var d = new SavedDeck
                    {
                        Name = o.StrOrNull("name") ?? "",
                        Commander = new CommanderId(o.StrOrNull("cc") ?? "fire"),
                    };

                    CommanderDef cc;
                    if (cat != null && !cat.TryCommander(d.Commander, out cc)) continue;

                    // A card that no longer exists is DROPPED rather than kept: registries change,
                    // and a deck that silently references a retired card is a deck that fails to
                    // start a match long after the edit that broke it.
                    var legal = cat != null ? new HashSet<string>(PoolFor(cat, cat.Commander(d.Commander))) : null;

                    var cards = o.Get("cards");
                    if (cards != null && cards.Type == JsonType.Object)
                        foreach (var key in cards.Keys)
                        {
                            if (legal != null && !legal.Contains(key)) continue;
                            int n = cards.Get(key).AsInt;
                            if (n > 0) d.Cards[key] = n > MaxCopies ? MaxCopies : n;
                        }

                    list.Add(d);
                    if (list.Count >= MaxDecks) break;
                }
            }
            catch
            {
                return list;
            }
            return list;
        }

        static string JsonString(string s)
        {
            var sb = new StringBuilder(s == null ? 2 : s.Length + 2);
            sb.Append('"');
            if (s != null)
                foreach (var ch in s)
                {
                    if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                    else if (ch < ' ') sb.Append(' ');
                    else sb.Append(ch);
                }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
