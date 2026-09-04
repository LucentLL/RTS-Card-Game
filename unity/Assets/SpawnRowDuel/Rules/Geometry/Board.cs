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

        /// <summary>
        /// A real, standable cell: ALL 35 of them.
        ///
        /// The centre row used to be split - three CENTER_LANES at columns 1/3/5 where creatures
        /// could stand, four flanks where only structures could build - and the contested row was
        /// therefore three cells wide for an army and four for a builder. That split is gone: the
        /// middle row is seven cells like every other row, and anything may occupy any of them.
        ///
        /// Kept as a method rather than deleted because it is the one question every placement,
        /// movement and scan site asks, and a board that grows a hole again should have one place
        /// to say so.
        /// </summary>
        public static bool IsRealSlot(RowKey row, int col)
        {
            return col >= 0 && col < Columns;
        }

        /// <summary>
        /// Whether this cell will take this kind of card. Nothing is barred any more - it exists
        /// so the call sites that asked keep asking, and it is where a future restriction lands.
        /// </summary>
        public static bool CenterSlotOk(RowKey row, int col, bool isStructure)
        {
            return IsRealSlot(row, col);
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
            {
                if (r < 0 || r >= Rows) continue;
                if (n >= into.Length) break;      // the caller sizes the span; do not trust it
                into[n++] = (RowKey)r;
            }
            return n;
        }

        /// <summary>
        /// The most cells one move can ever reach: three rows of seven, less the cell it is
        /// standing in. Every caller sizes its buffer with this.
        /// </summary>
        public const int MaxStepTargets = 3 * Columns - 1;

        /// <summary>
        /// May this creature's ONE move of the turn carry it from a to b?
        ///
        /// A move travels AT MOST ONE ROW - forward, back, or staying put - and any distance
        /// along that row. It used to be one square in any of eight directions, which made
        /// crossing the board a five-turn walk and made the column a creature happened to be
        /// standing in a commitment rather than a position.
        ///
        /// Row and column are deliberately asymmetric, and that asymmetry is the whole rule: the
        /// rows are the front line - who is in front of whom decides blocking, raiding and what
        /// a wall strike can reach - so advancing is still paced at a row a turn. Columns decide
        /// nothing but congestion, so sliding along one costs nothing to give away.
        /// </summary>
        public static bool InStepRange(CellRef a, CellRef b)
        {
            return IsRealSlot(a.Row, a.Col) && IsRealSlot(b.Row, b.Col) && a != b
                && Math.Abs((int)a.Row - (int)b.Row) <= 1;
        }

        /// <summary>
        /// Every cell one move could reach, ignoring who is standing there.
        ///
        /// CANONICAL enumeration order, pinned so no future rule can be order-ambiguous
        /// (spec 04 s23 determinism note): ascending RowKey, then ascending Col.
        ///
        /// Contract: exactly the set for which <see cref="InStepRange"/>(from, x) holds. Size the
        /// buffer with <see cref="MaxStepTargets"/> - a short one is filled and truncated rather
        /// than overrun, which silently hides legal moves, so do not pass a guess.
        /// </summary>
        public static int StepTargets(CellRef from, Span<CellRef> into)
        {
            if (!IsRealSlot(from.Row, from.Col)) return 0;

            int n = 0;
            for (int r = (int)from.Row - 1; r <= (int)from.Row + 1; r++)
            {
                if (r < 0 || r >= Rows) continue;
                for (int c = 0; c < Columns; c++)
                {
                    var cell = new CellRef((RowKey)r, c);
                    if (cell == from) continue;
                    if (!IsRealSlot(cell.Row, cell.Col)) continue;
                    if (n >= into.Length) return n;   // as above: the buffer is not ours
                    into[n++] = cell;
                }
            }
            return n;
        }
    }
}
