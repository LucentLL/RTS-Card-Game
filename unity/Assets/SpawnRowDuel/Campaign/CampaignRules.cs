using System.Collections.Generic;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Campaign
{
    /// <summary>
    /// The campaign's rules, such as they are: who may be attacked, which throne a territory is,
    /// and which banners you may march under. Pure and shared - the map renderer asks the same
    /// questions the gameplay does, and the two answering differently is how a map ends up
    /// lighting a territory it will then refuse.
    /// </summary>
    public static class CampaignRules
    {
        /// <summary>The 8 deckable elements, in the canonical order. Divine is not one of them:
        /// it is reserved for Ace/Boss cards and has no campaign content.</summary>
        public static readonly Element[] Majors =
        {
            Element.Fire, Element.Water, Element.Earth, Element.Wind,
            Element.Forest, Element.Electric, Element.Light, Element.Dark,
        };

        public static int MajorIndex(Element el)
        {
            for (int i = 0; i < Majors.Length; i++) if (Majors[i] == el) return i;
            return -1;
        }

        public static bool IsMajor(Element el) { return MajorIndex(el) >= 0; }

        /// <summary>The commander id of a solo banner: the element's own name, lowercased.</summary>
        public static CommanderId Solo(Element el)
        {
            return new CommanderId(Name(el).ToLowerInvariant());
        }

        /// <summary>
        /// The dual commander id for two elements, in canonical colour order - "fire_water", never
        /// "water_fire". The order is load-bearing: only 28 of the 56 orderings exist as cards.
        /// </summary>
        public static CommanderId Dual(Element a, Element b)
        {
            return MajorIndex(a) < MajorIndex(b)
                ? new CommanderId(Name(a).ToLowerInvariant() + "_" + Name(b).ToLowerInvariant())
                : new CommanderId(Name(b).ToLowerInvariant() + "_" + Name(a).ToLowerInvariant());
        }

        public static string Name(Element el)
        {
            switch (el)
            {
                case Element.Fire: return "Fire";
                case Element.Water: return "Water";
                case Element.Earth: return "Earth";
                case Element.Wind: return "Wind";
                case Element.Forest: return "Forest";
                case Element.Electric: return "Electric";
                case Element.Light: return "Light";
                case Element.Dark: return "Dark";
                case Element.Divine: return "Divine";
                default: return "Neutral";
            }
        }

        public static Element FromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return Element.None;
            switch (name.ToLowerInvariant())
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

        /// <summary>
        /// Any enemy territory bordering any territory you hold. That is the whole rule: no range,
        /// no supply, no movement, no stacks.
        /// </summary>
        public static bool IsAttackable(CampaignMap map, Element faction, int territoryId)
        {
            var t = map.Of(territoryId);
            if (t == null || t.Owner == faction) return false;
            for (int i = 0; i < t.Adjacent.Length; i++)
            {
                var u = map.Of(t.Adjacent[i]);
                if (u != null && u.Owner == faction) return true;
            }
            return false;
        }

        /// <summary>Whose throne this territory IS, by fixed designation - not who holds it.</summary>
        public static Element CapitalDesignation(CampaignMap map, int territoryId)
        {
            foreach (var kv in map.Capitals)
                if (kv.Value == territoryId) return kv.Key;
            return Element.None;
        }

        /// <summary>
        /// The element you would absorb by taking this territory: its designated owner, unless
        /// that is you. One helper for the confirm box, the dialogue and the resolution - the JS
        /// had three sites with three subtly different rules.
        /// </summary>
        public static Element CapitalPrize(CampaignState s, int territoryId)
        {
            var c = CapitalDesignation(s.Map, territoryId);
            return c != Element.None && c != s.Faction ? c : Element.None;
        }

        public static int PlayerTerritoryCount(CampaignState s)
        {
            int n = 0;
            for (int i = 0; i < s.Map.Territories.Length; i++)
                if (s.Map.Territories[i].Owner == s.Faction) n++;
            return n;
        }

        public static int CapitalsHeld(CampaignState s)
        {
            int n = 0;
            foreach (var kv in s.Map.Capitals)
            {
                var t = s.Map.Of(kv.Value);
                if (t != null && t.Owner == s.Faction) n++;
            }
            return n;
        }

        /// <summary>
        /// The banners available to march under: your own, plus one dual per absorbed ally. Late
        /// in a campaign this reaches 8 - one solo and seven duals.
        /// </summary>
        public static List<CommanderId> AvailableCommanders(CampaignState s)
        {
            var list = new List<CommanderId> { Solo(s.Faction) };
            for (int i = 0; i < Majors.Length; i++)
            {
                var el = Majors[i];
                if (el == s.Faction || !s.Allies.Contains(el)) continue;
                list.Add(Dual(s.Faction, el));
            }
            return list;
        }
    }
}
