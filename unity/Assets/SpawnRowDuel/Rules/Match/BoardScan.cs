using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// The board queries the keyword and spell engines share: firstEmptyCell, liveEnemyCreatures
    /// and liveEnemyStructures (06_mana_workers.js:101-108).
    ///
    /// Every scan runs in the canonical cell order - ascending cell index, which is exactly the
    /// JS's ownUnits() walk over the global ROWS list - because these lists are then STABLY
    /// sorted, so their input order decides which unit a tie picks.
    /// </summary>
    public static class BoardScan
    {
        /// <summary>
        /// firstEmptyCell: the owner's BACK row 0..6, then their FRONT row 0..6, then the first
        /// empty centre LANE (1, 3, 5). Where Ward's Lumen and Reap's Shade land. False when the
        /// owner has nowhere left to put a body.
        ///
        /// Note this is the owner's own rows in owner order - NOT the global cell order the other
        /// scans use.
        /// </summary>
        public static bool FirstEmptyCell(GameState s, Side owner, out CellRef cell)
        {
            var zones = new[] { Board.RowFor(owner, SlotName.Back), Board.RowFor(owner, SlotName.Front) };
            for (int r = 0; r < zones.Length; r++)
                for (int col = 0; col < Board.Columns; col++)
                {
                    var c = new CellRef(zones[r], col);
                    if (s.At(c) == null) { cell = c; return true; }
                }

            for (int col = 0; col < Board.Columns; col++)
            {
                var c = new CellRef(RowKey.Center, col);        // all seven now, not three lanes
                if (s.At(c) == null) { cell = c; return true; }
            }

            cell = default(CellRef);
            return false;
        }

        /// <summary>
        /// liveEnemyCreatures(owner): every living non-worker creature owned by the OTHER side,
        /// wherever it stands. Workers live in pools, never in cells, so the filter is belt and
        /// braces - as it is in the JS.
        /// </summary>
        public static List<CreatureUnit> LiveEnemyCreatures(GameState s, Side owner)
        {
            return LiveCreaturesOf(s, TurnMachine.Other(owner));
        }

        public static List<CreatureUnit> LiveCreaturesOf(GameState s, Side side)
        {
            var outp = new List<CreatureUnit>();
            foreach (var kv in s.ObjectsOf(side))
            {
                var c = kv.Value as CreatureUnit;
                if (c != null && !c.IsWorker && c.Hp > 0) outp.Add(c);
            }
            return outp;
        }

        /// <summary>liveEnemyStructures(owner) - note it does NOT exclude command centers.</summary>
        public static List<StructureUnit> LiveEnemyStructures(GameState s, Side owner)
        {
            var foe = TurnMachine.Other(owner);
            var outp = new List<StructureUnit>();
            foreach (var kv in s.ObjectsOf(foe))
            {
                var b = kv.Value as StructureUnit;
                if (b != null && b.Hp > 0) outp.Add(b);
            }
            return outp;
        }

        /// <summary>
        /// "Deadliest first": highest attack, ties broken by LOWEST hp, ties after that by board
        /// order. Detonate's victim and Arc's two marks both read from this one ordering
        /// (06_mana_workers.js:127, 14_spells_traps.js:15) - and both sort RAW attack, never the
        /// Overcharge-discharged figure.
        /// </summary>
        public static void SortDeadliestFirst(List<CreatureUnit> list)
        {
            Sorting.StableSort(list, delegate (CreatureUnit a, CreatureUnit b)
            {
                if (a.Attack != b.Attack) return b.Attack.CompareTo(a.Attack);
                return a.Hp.CompareTo(b.Hp);
            });
        }
    }
}
