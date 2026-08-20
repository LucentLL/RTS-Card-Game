using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// The immutable card catalog the engine reads rules data from. Injected into the engine so
    /// tests can supply a fixture; the Unity side builds the same records from ScriptableObjects
    /// and the SO type never crosses this boundary (design 01 s2.7).
    /// </summary>
    public interface ICardCatalog
    {
        // ---- creatures ----------------------------------------------------------------------

        /// <summary>Lookup by nm. Includes the 4 divine creatures and the Worker template.</summary>
        bool TryCreature(CardId id, out CreatureCard card);
        CreatureCard Creature(CardId id);

        /// <summary>All 68 real creatures - 64 deckable + 4 divine - in registry order.</summary>
        IReadOnlyList<CreatureCard> Creatures { get; }

        /// <summary>
        /// The 8-card pool of a deckable element, in pool order. Mirrors the JS poolFor fallback:
        /// an element with no pool (None, Divine) resolves to the Fire pool.
        /// </summary>
        IReadOnlyList<CreatureCard> PoolOf(Element element);

        /// <summary>mkVil's template: Worker, 0/1000, cost 0, upkeep 0.</summary>
        CreatureCard WorkerTemplate { get; }

        // ---- spells -------------------------------------------------------------------------

        bool TrySpell(CardId id, out SpellCard card);
        SpellCard Spell(CardId id);

        /// <summary>All 14 - 9 castable + 5 traps - in registry order. deckOf draws from ALL 14.</summary>
        IReadOnlyList<SpellCard> Spells { get; }

        // ---- commanders ---------------------------------------------------------------------

        bool TryCommander(CommanderId id, out CommanderDef commander);
        CommanderDef Commander(CommanderId id);

        /// <summary>
        /// All 36 in registry order - 8 solo then 28 dual. This IS the canonical order the
        /// random-opponent pick indexes into (design 01 s7), so it must never be re-sorted.
        /// </summary>
        IReadOnlyList<CommanderDef> Commanders { get; }

        // ---- structures ---------------------------------------------------------------------

        /// <summary>
        /// resolveStruct: forge/grandforge are families synthesised per element; every other bid
        /// is a singleton and ignores the element argument. Returns null for an unknown bid.
        /// </summary>
        StructureDef Structure(StructId bid, Element element);

        /// <summary>All 31 definitions - 13 static then 18 generated forges, registry order.</summary>
        IReadOnlyList<StructureDef> Structures { get; }

        /// <summary>The commander's build menu, resolved, in menu order. ORDER IS THE AI PRIORITY.</summary>
        IReadOnlyList<StructureDef> BuildList(CommanderId cc);

        /// <summary>
        /// bidLineage: the family id followed by its upgrade ancestors, walking `from` links with
        /// the 8-hop runaway guard (spec 05 s7.5). Prereq checks match against ANY entry, so an
        /// upgraded Keep still satisfies a 'foundry' prerequisite.
        /// </summary>
        IReadOnlyList<StructId> Lineage(StructId bid);

        // ---- elements / registry ------------------------------------------------------------

        ElementDef ElementOf(Element element);

        /// <summary>All 9, registry order (8 deckable + divine).</summary>
        IReadOnlyList<ElementDef> Elements { get; }

        /// <summary>The 78-entry deck registry: "color|nm" to card id (spec 06 s0).</summary>
        bool TryByDeckKey(DeckKey key, out CardId id);

        // ---- deck-building constants --------------------------------------------------------

        int DeckSize { get; }     // 40
        int MaxCopies { get; }    // 3 - enforced by the deck builder, NOT by deckOf
    }
}
