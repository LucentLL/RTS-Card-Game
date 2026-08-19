using System;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// Board geometry. Pure static, no state, fully table-driven, unit-testable in isolation.
    /// Source: docs/unity/spec/01_board_geometry_state.md and 04_movement_placement.md.
    ///
    /// Two deliberate deletions from the JS, per the specs: colReach is not ported (dead code -
    /// columns never constrain combat), and moveChainOf is not ported (its owner parameter is
    /// provably redundant; the two JS move chains are exact reverses of each other).
    /// </summary>
    public static class Board
    {
        public const int Columns = 7;      // SLOTS
        public const int Rows = 5;         // ROWS.Length
        public const int Cells = Rows * Columns;
        public const int BaseColumn = 3;   // BASE_COL - presentation only, never a rule
        public const int FoeWallRow = -1;  // virtual: life target, not a real row
        public const int YouWallRow = Rows;

        public static readonly RowKey[] AllRows =
        {
            RowKey.FoeBack, RowKey.FoeFront, RowKey.Center, RowKey.YouFront, RowKey.YouBack
        };

        /// <summary>CENTER_LANES - creatures fight in the lanes, structures build on the flanks.</summary>
        public static bool IsLane(int col) { return col == 1 || col == 3 || col == 5; }

        /// <summary>A real, creature-standable cell. 31 of the 35 cells qualify.</summary>
        public static bool IsRealSlot(RowKey row, int col)
        {
            return col >= 0 && col < Columns && (row != RowKey.Center || IsLane(col));
        }

        /// <summary>centerSlotOK - structures take center flanks, creatures take center lanes.</summary>
        public static bool CenterSlotOk(RowKey row, int col, bool isStructure)
        {
            if (row != RowKey.Center) return true;
            return isStructure ? !IsLane(col) : IsLane(col);
        }

        public static RowKey RowFor(Side owner, SlotName which)
        {
            switch (which)
            {
                case SlotName.Center: return RowKey.Center;
                case SlotName.Front: return owner == Side.You ? RowKey.YouFront : RowKey.FoeFront;
                default: return owner == Side.You ? RowKey.YouBack : RowKey.FoeBack;
            }
        }

        public static SlotName WhichOf(RowKey row)
        {
            switch (row)
            {
                case RowKey.Center: return SlotName.Center;
                case RowKey.YouFront:
                case RowKey.FoeFront: return SlotName.Front;
                default: return SlotName.Back;
            }
        }

        /// <summary>
        /// Which economy zone a row reads as, from the owner's perspective. Enemy rows are Raid:
        /// an army camped there has no structures behind it, so it is paid for at every upkeep.
        /// </summary>
        public static WorkerZone ZoneForRow(Side owner, RowKey row)
        {
            if (row == RowKey.Center) return WorkerZone.Center;
            if (row == RowFor(owner, SlotName.Back)) return WorkerZone.Back;
            if (row == RowFor(owner, SlotName.Front)) return WorkerZone.Front;
            return WorkerZone.Raid;
        }

        private static readonly RowKey[] YouRaid = { RowKey.FoeFront, RowKey.FoeBack };
        private static readonly RowKey[] FoeRaid = { RowKey.YouFront, RowKey.YouBack };
        private static readonly RowKey[] CenterOnly = { RowKey.Center };
        private static readonly RowKey[] YouFrontOnly = { RowKey.YouFront };
        private static readonly RowKey[] FoeFrontOnly = { RowKey.FoeFront };
        private static readonly RowKey[] YouBackOnly = { RowKey.YouBack };
        private static readonly RowKey[] FoeBackOnly = { RowKey.FoeBack };

        /// <summary>
        /// CANONICAL and PLURAL. Raid spans BOTH enemy rows now that the enemy back row is
        /// enterable. The JS zoneKey (singular) disagrees for Raid; that footgun does not exist
        /// here (spec 01 s8.1, spec 04 s5.6).
        /// </summary>
        public static RowKey[] RowsOfZone(Side owner, WorkerZone zone)
        {
            switch (zone)
            {
                case WorkerZone.Center: return CenterOnly;
                case WorkerZone.Raid: return owner == Side.You ? YouRaid : FoeRaid;
                case WorkerZone.Front: return owner == Side.You ? YouFrontOnly : FoeFrontOnly;
                default: return owner == Side.You ? YouBackOnly : FoeBackOnly;
            }
        }

        /// <summary>
        /// rowsCrossedInto: the half-open interval (attacker, target] in travel order, clipped to
        /// real rows. Same row means empty, which means an uninterposable point-blank duel
        /// (spec 03 s4.1). This is the heart of row-interval blocking - a block may only come from
        /// a row the attack crosses INTO.
        /// </summary>
        public static int RowsCrossedInto(int attackerRow, int targetRow, Span<RowKey> into)
        {
            if (attackerRow == targetRow) return 0;
            int step = targetRow > attackerRow ? 1 : -1;
            int n = 0;
            for (int r = attackerRow + step; r != targetRow + step; r += step)
                if (r >= 0 && r < Rows) into[n++] = (RowKey)r;
            return n;
        }

        /// <summary>Owner-agnostic. One step in any of 8 directions into a real slot.</summary>
        public static bool Adjacent(CellRef a, CellRef b)
        {
            return IsRealSlot(a.Row, a.Col) && IsRealSlot(b.Row, b.Col) && a != b
                && Math.Abs((int)a.Row - (int)b.Row) <= 1
                && Math.Abs(a.Col - b.Col) <= 1;
        }

        /// <summary>
        /// CANONICAL enumeration order, pinned so no future rule can be order-ambiguous
        /// (spec 04 s23 determinism note): ascending RowKey, then ascending Col.
        ///
        /// Contract: this is a creature-movement query and is exactly the set for which
        /// Adjacent(from, x) holds. A cell that is not itself creature-standable - the center
        /// flanks at cols 0/2/4/6, which hold structures - has no movement neighbours, because
        /// no creature can ever be standing there to move out of.
        /// </summary>
        public static int Neighbours(CellRef from, Span<CellRef> into)
        {
            if (!IsRealSlot(from.Row, from.Col)) return 0;

            int n = 0;
            for (int r = (int)from.Row - 1; r <= (int)from.Row + 1; r++)
            {
                if (r < 0 || r >= Rows) continue;
                for (int c = from.Col - 1; c <= from.Col + 1; c++)
                {
                    if (c < 0 || c >= Columns) continue;
                    var cell = new CellRef((RowKey)r, c);
                    if (cell == from) continue;
                    if (!IsRealSlot(cell.Row, cell.Col)) continue;
                    into[n++] = cell;
                }
            }
            return n;
        }
    }
}
