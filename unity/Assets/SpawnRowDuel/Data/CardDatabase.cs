using System;
using System.Collections.Generic;
using UnityEngine;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Data
{
    /// <summary>
    /// The generated index over every CardDefinition asset, plus the deck-building constants
    /// lifted from cards.json so the core never hardcodes them. GENERATED - never hand-edited.
    ///
    /// ToCatalog() is the runtime bridge: it rebuilds the exact same pure CardCatalog the JSON
    /// loader produces, reconstructing registry order from each row's registryIndex (the index
    /// array itself is sorted by exportKey for stable diffs). A parity test asserts the two
    /// construction paths agree field-for-field, so they cannot drift.
    /// </summary>
    [CreateAssetMenu(menuName = "Spawn Row Duel/Card Database", fileName = "CardDatabase")]
    public sealed class CardDatabase : ScriptableObject
    {
        [SerializeField] internal string sourceHash = "";        // SHA-256 of cards.json sans generatedAt
        [SerializeField] internal string sourceGeneratedAt = ""; // provenance, informational
        [SerializeField] internal CardDefinition[] all = new CardDefinition[0];

        [SerializeField] internal int deckSize = 40;
        [SerializeField] internal int maxCopies = 3;
        [SerializeField] internal int boardSlots = 7;
        [SerializeField] internal int baseColumn = 3;
        [SerializeField] internal int[] centerLanes = { 1, 3, 5 };

        public string SourceHash { get { return sourceHash; } }
        public string SourceGeneratedAt { get { return sourceGeneratedAt; } }
        public IReadOnlyList<CardDefinition> All { get { return all; } }
        public int DeckSize { get { return deckSize; } }
        public int MaxCopies { get { return maxCopies; } }

        private Dictionary<string, CardDefinition> _byDeckKey;
        private Dictionary<string, CardDefinition> _byExportKey;

        /// <summary>Deck-list resolution for the deck builder / save loader.</summary>
        public bool TryByDeckKey(string deckKey, out CardDefinition def)
        {
            EnsureIndex();
            return _byDeckKey.TryGetValue(deckKey, out def);
        }

        public bool TryByExportKey(string exportKey, out CardDefinition def)
        {
            EnsureIndex();
            return _byExportKey.TryGetValue(exportKey, out def);
        }

        private void EnsureIndex()
        {
            if (_byDeckKey != null) return;
            _byDeckKey = new Dictionary<string, CardDefinition>(StringComparer.Ordinal);
            _byExportKey = new Dictionary<string, CardDefinition>(StringComparer.Ordinal);
            for (int i = 0; i < all.Length; i++)
            {
                var d = all[i];
                if (d == null) continue;
                _byExportKey[d.ExportKey] = d;
                // The deck registry holds the 64 deckable creatures + 14 spells - never divine
                // (isPlayable false), never tokens (spec 06 s0).
                if ((d.Kind == CardKind.Creature && d.isPlayable) || d.Kind == CardKind.Spell)
                    _byDeckKey[d.DeckKeyString] = d;
            }
        }

        /// <summary>
        /// Build the pure catalog the engine consumes. The SO type never crosses the assembly
        /// boundary - only plain records do (design 01 s2.7).
        /// </summary>
        public CardCatalog ToCatalog()
        {
            var creatures = SortedByRegistryIndex(CardKind.Creature);
            var spells = SortedByRegistryIndex(CardKind.Spell);
            var commanders = SortedByRegistryIndex(CardKind.Commander);
            var structures = SortedByRegistryIndex(CardKind.Structure);
            var elements = SortedByRegistryIndex(CardKind.Element);
            var tokens = SortedByRegistryIndex(CardKind.Token);

            var creatureRecords = new List<CreatureCard>(creatures.Count);
            foreach (var d in creatures) creatureRecords.Add(d.ToCreatureCard());

            var spellRecords = new List<SpellCard>(spells.Count);
            foreach (var d in spells) spellRecords.Add(d.ToSpellCard());

            var commanderRecords = new List<CommanderDef>(commanders.Count);
            foreach (var d in commanders) commanderRecords.Add(d.ToCommanderDef());

            var structureRecords = new List<StructureDef>(structures.Count);
            foreach (var d in structures) structureRecords.Add(d.ToStructureDef());

            var elementRecords = new List<ElementDef>(elements.Count);
            foreach (var d in elements) elementRecords.Add(d.ToElementDef());

            CreatureCard worker = null;
            foreach (var d in tokens)
                if (d.DisplayName == "Worker") worker = d.ToCreatureCard();

            return new CardCatalog(creatureRecords, spellRecords, commanderRecords,
                structureRecords, elementRecords, worker, deckSize, maxCopies);
        }

        private List<CardDefinition> SortedByRegistryIndex(CardKind kind)
        {
            var rows = new List<CardDefinition>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].Kind == kind) rows.Add(all[i]);
            rows.Sort(CompareRegistryIndex);   // total order: registryIndex is unique per kind
            return rows;
        }

        private static int CompareRegistryIndex(CardDefinition a, CardDefinition b)
        {
            int c = a.RegistryIndex.CompareTo(b.RegistryIndex);
            if (c != 0) return c;
            return string.CompareOrdinal(a.ExportKey, b.ExportKey);
        }
    }
}
