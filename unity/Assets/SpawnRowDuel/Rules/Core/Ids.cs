using System;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// A card's stable identity - the JS `nm` field. Wrapped rather than passed as a bare string so
    /// a card id can never be silently confused with a display name or a structure id.
    /// </summary>
    public readonly struct CardId : IEquatable<CardId>
    {
        public readonly string Value;
        public CardId(string value) { Value = value ?? ""; }

        public bool IsNone { get { return string.IsNullOrEmpty(Value); } }
        public static readonly CardId None = new CardId("");

        public bool Equals(CardId o) { return string.Equals(Value, o.Value, StringComparison.Ordinal); }
        public override bool Equals(object obj) { return obj is CardId o && Equals(o); }
        public override int GetHashCode() { return Value == null ? 0 : Value.GetHashCode(); }
        public static bool operator ==(CardId a, CardId b) { return a.Equals(b); }
        public static bool operator !=(CardId a, CardId b) { return !a.Equals(b); }
        public override string ToString() { return Value; }
    }

    /// <summary>
    /// A structure definition id. Forge and GrandForge stay SINGLE ids carrying a separate Element
    /// parameter - flattening them into 18 per-element ids would break prereq matching against
    /// 'forge' (spec 05 s2.3).
    /// </summary>
    public readonly struct StructId : IEquatable<StructId>
    {
        public readonly string Value;
        public StructId(string value) { Value = value ?? ""; }

        public bool IsNone { get { return string.IsNullOrEmpty(Value); } }
        public static readonly StructId None = new StructId("");

        public bool Equals(StructId o) { return string.Equals(Value, o.Value, StringComparison.Ordinal); }
        public override bool Equals(object obj) { return obj is StructId o && Equals(o); }
        public override int GetHashCode() { return Value == null ? 0 : Value.GetHashCode(); }
        public static bool operator ==(StructId a, StructId b) { return a.Equals(b); }
        public static bool operator !=(StructId a, StructId b) { return !a.Equals(b); }
        public override string ToString() { return Value; }
    }

    public readonly struct CommanderId : IEquatable<CommanderId>
    {
        public readonly string Value;
        public CommanderId(string value) { Value = value ?? ""; }

        public bool IsNone { get { return string.IsNullOrEmpty(Value); } }
        public static readonly CommanderId None = new CommanderId("");

        public bool Equals(CommanderId o) { return string.Equals(Value, o.Value, StringComparison.Ordinal); }
        public override bool Equals(object obj) { return obj is CommanderId o && Equals(o); }
        public override int GetHashCode() { return Value == null ? 0 : Value.GetHashCode(); }
        public static bool operator ==(CommanderId a, CommanderId b) { return a.Equals(b); }
        public static bool operator !=(CommanderId a, CommanderId b) { return !a.Equals(b); }
        public override string ToString() { return Value; }
    }

    /// <summary>
    /// The registry key, "&lt;color|neutral&gt;|&lt;name&gt;". Deck lists are stored as these, so the
    /// format is load-bearing for save compatibility.
    /// </summary>
    public readonly struct DeckKey : IEquatable<DeckKey>
    {
        public readonly Element Color;   // Element.None == the literal "neutral" prefix
        public readonly string Name;

        public DeckKey(Element color, string name) { Color = color; Name = name ?? ""; }

        public static DeckKey Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return new DeckKey(Element.None, "");
            int bar = s.IndexOf('|');
            if (bar < 0) return new DeckKey(Element.None, s);

            string head = s.Substring(0, bar);
            string tail = s.Substring(bar + 1);
            return new DeckKey(ElementNames.Parse(head), tail);
        }

        public override string ToString() { return ElementNames.ToKey(Color) + "|" + Name; }

        public bool Equals(DeckKey o)
        {
            return Color == o.Color && string.Equals(Name, o.Name, StringComparison.Ordinal);
        }
        public override bool Equals(object obj) { return obj is DeckKey o && Equals(o); }
        public override int GetHashCode() { return ((int)Color * 397) ^ (Name == null ? 0 : Name.GetHashCode()); }
        public static bool operator ==(DeckKey a, DeckKey b) { return a.Equals(b); }
        public static bool operator !=(DeckKey a, DeckKey b) { return !a.Equals(b); }
    }

    /// <summary>
    /// The lowercase wire names for elements. These appear in saved decks and exported card data,
    /// so they are a compatibility surface, not a display concern.
    /// </summary>
    public static class ElementNames
    {
        public static string ToKey(Element e)
        {
            switch (e)
            {
                case Element.Fire: return "fire";
                case Element.Water: return "water";
                case Element.Earth: return "earth";
                case Element.Wind: return "wind";
                case Element.Forest: return "forest";
                case Element.Electric: return "electric";
                case Element.Light: return "light";
                case Element.Dark: return "dark";
                case Element.Divine: return "divine";
                default: return "neutral";
            }
        }

        public static Element Parse(string s)
        {
            switch (s)
            {
                case "fire": return Element.Fire;
                case "water": return Element.Water;
                case "earth": return Element.Earth;
                case "wind": return Element.Wind;
                case "forest": return Element.Forest;
                case "electric": return Element.Electric;
                case "light": return Element.Light;
                case "dark": return Element.Dark;
                case "divine": return Element.Divine;
                default: return Element.None;
            }
        }

        /// <summary>The 8 deckable elements, in canonical order. Divine is excluded.</summary>
        public static readonly Element[] Majors =
        {
            Element.Fire, Element.Water, Element.Earth, Element.Wind,
            Element.Forest, Element.Electric, Element.Light, Element.Dark,
        };
    }
}
