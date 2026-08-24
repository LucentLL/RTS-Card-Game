using System.Collections.Generic;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Campaign
{
    /// <summary>
    /// A territory: a contiguous clump of tiles with one owner and a garrison.
    ///
    /// There are no other node types. No fortresses, no resources, no fog of war. A territory is
    /// either plain or the designated capital of some element, and that designation lives in the
    /// map rather than here, because it never changes hands even when the ground does.
    ///
    /// Ids are 0-BASED AND ZERO IS VALID. Never truthiness-test one; the JS had to say this out
    /// loud too, and the bug it prevents (territory 0 silently unattackable) is invisible in play.
    /// </summary>
    public sealed class Territory
    {
        public int Id;
        public int[] Tiles;
        public int[] Adjacent;
        public Element Owner;
        public int Garrison;
        public int AnchorTile;      // where the marker sits; also the position used for seeding
    }

    /// <summary>The drawn world. Geometry is NOT stored - it is rebuilt from Frequency.</summary>
    public sealed class CampaignMap
    {
        public int Frequency;
        public int[] TileTerritory;                 // tile id -> territory id, length 10f²+2
        public Territory[] Territories;             // 22
        public Dictionary<Element, int> Capitals;   // 8, fixed at generation and never mutated

        public HexSphere Sphere { get { return HexSphere.Get(Frequency); } }

        public Territory Of(int id)
        {
            return id >= 0 && id < Territories.Length ? Territories[id] : null;
        }

        /// <summary>
        /// The one check that matters on load: a save whose tile list does not match the sphere
        /// its own frequency rebuilds would index past the end of the world on the first render.
        /// </summary>
        public bool Validate()
        {
            if (Frequency <= 0 || TileTerritory == null || Territories == null || Capitals == null)
                return false;
            if (TileTerritory.Length != HexSphere.TileCount(Frequency)) return false;
            if (Territories.Length == 0) return false;
            for (int i = 0; i < TileTerritory.Length; i++)
                if (TileTerritory[i] < 0 || TileTerritory[i] >= Territories.Length) return false;
            for (int i = 0; i < Territories.Length; i++)
                if (Territories[i] == null || Territories[i].Id != i) return false;
            foreach (var kv in Capitals)
                if (Of(kv.Value) == null) return false;
            return true;
        }
    }

    /// <summary>
    /// A campaign in progress. One save slot, whole-object write, no partial updates.
    ///
    /// <see cref="TargetTerritory"/> is the flag that routes a finished duel back into the
    /// campaign, and it is nullable rather than -1 for exactly the reason above: territory 0 is
    /// real ground and a falsy test would make it unresolvable.
    /// </summary>
    public sealed class CampaignState
    {
        public const int SchemaVersion = 3;

        public Element Faction;
        public int Turn = 1;
        public CampaignMap Map;
        public readonly HashSet<Element> Allies = new HashSet<Element>();
        public int? TargetTerritory;
        public bool Completed;      // latch: "the realm is united" fires once
        public bool Lost;           // a dead run must not resume on reload
        public ulong Seed;          // what the map was drawn from, so it can be re-derived

        public bool IsValid
        {
            get { return Faction != Element.None && Map != null && Map.Validate(); }
        }

        public IEnumerable<Territory> Owned(Element el)
        {
            for (int i = 0; i < Map.Territories.Length; i++)
                if (Map.Territories[i].Owner == el) yield return Map.Territories[i];
        }
    }

    /// <summary>Something the campaign layer did that a player should be told about.</summary>
    public struct CampaignEvent
    {
        public CampaignEventKind Kind;
        public Element Actor;        // who moved
        public Element From;         // who lost the ground (Element.None when nobody did)
        public int Territory;
        public string Text;          // the authored line, already assembled

        public static CampaignEvent Of(CampaignEventKind kind, Element actor, Element from,
                                       int territory, string text)
        {
            return new CampaignEvent { Kind = kind, Actor = actor, From = from, Territory = territory, Text = text };
        }
    }

    public enum CampaignEventKind : byte
    {
        AiCaptured = 0,
        AiRepulsed = 1,
        TerritoryWon = 2,
        AssaultRepelled = 3,
        CapitalTaken = 4,
        ElementAbsorbed = 5,
        RealmUnited = 6,
        Defeat = 7,
    }

    /// <summary>What the campaign hands the duel. The duel reads the two commanders and the deck
    /// seed; territory and garrison are context it deliberately ignores (spec 08 §9.4).</summary>
    public struct BattleLaunchRequest
    {
        public CommanderId PlayerCommander;
        public CommanderId EnemyCommander;   // always the defender's element as a SOLO commander
        public ulong DeckSeed;
        public int TerritoryId;
    }

    public enum BattleOutcome : byte { PlayerWon = 0, PlayerLost = 1, Abandoned = 2 }
}
