using System;
using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// The concrete catalog. Built once - by CardCatalogBuilder from cards.json, or by the Unity
    /// data layer from the imported ScriptableObjects - then read forever. All lookups are
    /// ordinal-string dictionaries; all list orders are registry order and are part of the
    /// behavioural contract (pool order feeds deckOf, commander order feeds the random pick,
    /// build-list order feeds the AI).
    /// </summary>
    public sealed class CardCatalog : ICardCatalog
    {
        private readonly List<CreatureCard> _creatures;                 // 68: 64 deckable + 4 divine
        private readonly List<SpellCard> _spells;                       // 14
        private readonly List<CommanderDef> _commanders;                // 36
        private readonly List<StructureDef> _structures;                // 31: 13 static + 18 forges
        private readonly List<ElementDef> _elements;                    // 9
        private readonly CreatureCard _workerTemplate;

        private readonly Dictionary<string, CreatureCard> _creatureByName;
        private readonly Dictionary<string, SpellCard> _spellByName;
        private readonly Dictionary<string, CommanderDef> _commanderById;
        private readonly Dictionary<string, StructureDef> _structureByKey;   // "foundry", "forge:fire"
        private readonly Dictionary<string, StructId> _familyFrom;           // bid -> from bid ("" = root)
        private readonly Dictionary<string, CardId> _byDeckKey;              // "fire|Sparkimp" -> nm
        private readonly Dictionary<Element, List<CreatureCard>> _pools;     // 8 deckable pools
        private readonly Dictionary<string, StructureDef[]> _buildLists;     // commander id -> resolved menu
        private readonly ElementDef[] _elementByEnum = new ElementDef[10];

        private readonly int _deckSize;
        private readonly int _maxCopies;

        public CardCatalog(List<CreatureCard> creatures, List<SpellCard> spells,
                           List<CommanderDef> commanders, List<StructureDef> structures,
                           List<ElementDef> elements, CreatureCard workerTemplate,
                           int deckSize, int maxCopies)
        {
            _creatures = creatures; _spells = spells; _commanders = commanders;
            _structures = structures; _elements = elements; _workerTemplate = workerTemplate;
            _deckSize = deckSize; _maxCopies = maxCopies;

            _creatureByName = new Dictionary<string, CreatureCard>(StringComparer.Ordinal);
            foreach (var c in _creatures) _creatureByName[c.Name] = c;
            if (workerTemplate != null) _creatureByName[workerTemplate.Name] = workerTemplate;

            _spellByName = new Dictionary<string, SpellCard>(StringComparer.Ordinal);
            foreach (var s in _spells) _spellByName[s.Name] = s;

            _commanderById = new Dictionary<string, CommanderDef>(StringComparer.Ordinal);
            foreach (var cc in _commanders) _commanderById[cc.Id.Value] = cc;

            _structureByKey = new Dictionary<string, StructureDef>(StringComparer.Ordinal);
            _familyFrom = new Dictionary<string, StructId>(StringComparer.Ordinal);
            foreach (var b in _structures)
            {
                _structureByKey[b.ExportKey] = b;
                // Family-level upgrade links: the 18 forge rows repeat the same forge/grandforge
                // family data 9 times, so writing them repeatedly is harmless.
                _familyFrom[b.Bid.Value] = b.UpgradedFrom;
            }

            _pools = new Dictionary<Element, List<CreatureCard>>();
            foreach (var c in _creatures)
            {
                if (!c.Deckable) continue;
                List<CreatureCard> pool;
                if (!_pools.TryGetValue(c.Element, out pool))
                {
                    pool = new List<CreatureCard>();
                    _pools[c.Element] = pool;
                }
                pool.Add(c);
            }

            _byDeckKey = new Dictionary<string, CardId>(StringComparer.Ordinal);
            foreach (var c in _creatures)
                if (c.Deckable) _byDeckKey[c.DeckKey.ToString()] = c.Id;
            foreach (var s in _spells)
                _byDeckKey[s.DeckKey.ToString()] = s.Id;

            foreach (var e in _elements) _elementByEnum[(int)e.El] = e;

            _buildLists = new Dictionary<string, StructureDef[]>(StringComparer.Ordinal);
            foreach (var cc in _commanders)
            {
                var list = new StructureDef[cc.BuildListRaw.Length];
                for (int i = 0; i < cc.BuildListRaw.Length; i++)
                {
                    var entry = ParseBuildEntry(cc.BuildListRaw[i]);
                    list[i] = Structure(entry.Key, entry.Value);
                    if (list[i] == null)
                        throw new CardsJsonException(
                            "commander '" + cc.Id + "' buildList entry '" + cc.BuildListRaw[i] +
                            "' does not resolve to a structure");
                }
                _buildLists[cc.Id.Value] = list;
            }
        }

        /// <summary>"forge:fire" splits into (forge, Fire); "foundry" is (foundry, None).</summary>
        public static KeyValuePair<StructId, Element> ParseBuildEntry(string raw)
        {
            int colon = raw == null ? -1 : raw.IndexOf(':');
            if (colon < 0) return new KeyValuePair<StructId, Element>(new StructId(raw), Element.None);
            return new KeyValuePair<StructId, Element>(
                new StructId(raw.Substring(0, colon)),
                ElementNames.Parse(raw.Substring(colon + 1)));
        }

        // ---- creatures ----------------------------------------------------------------------

        public bool TryCreature(CardId id, out CreatureCard card)
        {
            return _creatureByName.TryGetValue(id.Value, out card);
        }

        public CreatureCard Creature(CardId id)
        {
            CreatureCard c;
            if (!_creatureByName.TryGetValue(id.Value, out c))
                throw new KeyNotFoundException("no creature named '" + id.Value + "' in the catalog");
            return c;
        }

        public IReadOnlyList<CreatureCard> Creatures { get { return _creatures; } }

        public IReadOnlyList<CreatureCard> PoolOf(Element element)
        {
            List<CreatureCard> pool;
            if (_pools.TryGetValue(element, out pool)) return pool;
            // poolFor falls back to the fire pool for anything without one (04_cards_leaders.js:26).
            return _pools[Element.Fire];
        }

        public CreatureCard WorkerTemplate { get { return _workerTemplate; } }

        // ---- spells -------------------------------------------------------------------------

        public bool TrySpell(CardId id, out SpellCard card)
        {
            return _spellByName.TryGetValue(id.Value, out card);
        }

        public SpellCard Spell(CardId id)
        {
            SpellCard s;
            if (!_spellByName.TryGetValue(id.Value, out s))
                throw new KeyNotFoundException("no spell named '" + id.Value + "' in the catalog");
            return s;
        }

        public IReadOnlyList<SpellCard> Spells { get { return _spells; } }

        // ---- commanders ---------------------------------------------------------------------

        public bool TryCommander(CommanderId id, out CommanderDef commander)
        {
            return _commanderById.TryGetValue(id.Value, out commander);
        }

        public CommanderDef Commander(CommanderId id)
        {
            CommanderDef cc;
            if (!_commanderById.TryGetValue(id.Value, out cc))
                throw new KeyNotFoundException("no commander '" + id.Value + "' in the catalog");
            return cc;
        }

        public IReadOnlyList<CommanderDef> Commanders { get { return _commanders; } }

        // ---- structures ---------------------------------------------------------------------

        public StructureDef Structure(StructId bid, Element element)
        {
            string key = bid.Value;
            if (key == "forge" || key == "grandforge")
                key = key + ":" + ElementNames.ToKey(element);

            StructureDef def;
            return _structureByKey.TryGetValue(key, out def) ? def : null;
        }

        public IReadOnlyList<StructureDef> Structures { get { return _structures; } }

        public IReadOnlyList<StructureDef> BuildList(CommanderId cc)
        {
            StructureDef[] list;
            if (!_buildLists.TryGetValue(cc.Value, out list))
                throw new KeyNotFoundException("no commander '" + cc.Value + "' in the catalog");
            return list;
        }

        public IReadOnlyList<StructId> Lineage(StructId bid)
        {
            var outList = new List<StructId>(4);
            var cur = bid;
            int hops = 0;
            while (!cur.IsNone && hops < 8)
            {
                outList.Add(cur);
                StructId from;
                if (!_familyFrom.TryGetValue(cur.Value, out from)) break;
                cur = from;
                hops++;
            }
            return outList;
        }

        // ---- elements / registry ------------------------------------------------------------

        public ElementDef ElementOf(Element element)
        {
            var def = _elementByEnum[(int)element];
            if (def == null)
                throw new KeyNotFoundException("no element definition for " + element);
            return def;
        }

        public IReadOnlyList<ElementDef> Elements { get { return _elements; } }

        public bool TryByDeckKey(DeckKey key, out CardId id)
        {
            return _byDeckKey.TryGetValue(key.ToString(), out id);
        }

        public int DeckSize { get { return _deckSize; } }
        public int MaxCopies { get { return _maxCopies; } }
    }
}
