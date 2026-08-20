using System;
using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// Upkeep-shortfall arithmetic (spec 02 s7). A zone's effective shortfall is its raw
    /// negative worker figure minus what has already been paid into it THIS upkeep; the paid
    /// slate wipes at every BeginTurn.
    /// </summary>
    public static class Upkeep
    {
        /// <summary>Enumeration order IS the settle order: back, front, center, raid.</summary>
        public static readonly WorkerZone[] SettleOrder =
        {
            WorkerZone.Back, WorkerZone.Front, WorkerZone.Center, WorkerZone.Raid,
        };

        public static int ZoneDeficit(GameState s, Side owner, WorkerZone zone, ICardCatalog cat)
        {
            int raw = -WorkerMath.RowWorkers(s, owner, zone, cat);
            if (raw < 0) raw = 0;
            int paid = s.P(owner).UpkeepPaid[(int)zone];
            int net = raw - paid;
            return net > 0 ? net : 0;
        }

        public static int TotalDeficit(GameState s, Side owner, ICardCatalog cat)
        {
            int sum = 0;
            for (int z = 0; z < SettleOrder.Length; z++)
                sum += ZoneDeficit(s, owner, SettleOrder[z], cat);
            return sum;
        }

        /// <summary>
        /// upkeepOffender: in the first deficit zone (settle order), the highest-upkeep UNPAID
        /// non-worker creature. The JS relies on a stable sort; the walk here is explicitly
        /// total-ordered - upkeep DESC, then cell index ASC - so ties break the same way on
        /// every runtime.
        /// </summary>
        public static bool TryFindOffender(GameState s, Side owner, ICardCatalog cat,
                                           out CellRef cell, out int unitId)
        {
            for (int z = 0; z < SettleOrder.Length; z++)
            {
                var zone = SettleOrder[z];
                if (ZoneDeficit(s, owner, zone, cat) <= 0) continue;

                CreatureUnit best = null;
                CellRef bestCell = default(CellRef);
                var rows = Board.RowsOfZone(owner, zone);
                for (int r = 0; r < rows.Length; r++)
                {
                    for (int col = 0; col < Board.Columns; col++)
                    {
                        var at = new CellRef(rows[r], col);
                        var c = s.At(at) as CreatureUnit;
                        if (c == null || c.Owner != owner || c.IsWorker || c.PaidUpkeep) continue;
                        if (best == null || c.Upkeep > best.Upkeep) { best = c; bestCell = at; }
                    }
                }

                if (best != null)
                {
                    cell = bestCell;
                    unitId = best.Id;
                    return true;
                }
            }

            cell = default(CellRef);
            unitId = 0;
            return false;
        }

        /// <summary>
        /// orphanDeficit: the part of the shortfall with NO settleable creature anywhere in its
        /// zone - Harvest pays this out of its own proceeds instead of dead-locking the turn
        /// (spec 02 s7.4). Arises when a zone goes negative purely from structures (Cannon
        /// Tower's -2 after its support is razed).
        /// </summary>
        public static int OrphanDeficit(GameState s, Side owner, ICardCatalog cat)
        {
            int sum = 0;
            for (int z = 0; z < SettleOrder.Length; z++)
            {
                var zone = SettleOrder[z];
                int deficit = ZoneDeficit(s, owner, zone, cat);
                if (deficit <= 0) continue;
                if (!ZoneHasSettleableCreature(s, owner, zone)) sum += deficit;
            }
            return sum;
        }

        private static bool ZoneHasSettleableCreature(GameState s, Side owner, WorkerZone zone)
        {
            var rows = Board.RowsOfZone(owner, zone);
            for (int r = 0; r < rows.Length; r++)
                for (int col = 0; col < Board.Columns; col++)
                {
                    var c = s.At(new CellRef(rows[r], col)) as CreatureUnit;
                    if (c != null && c.Owner == owner && !c.IsWorker && !c.PaidUpkeep) return true;
                }
            return false;
        }

        /// <summary>The lock is on the OFFENDER, not the deficit - an orphan shortfall harvests through.</summary>
        public static bool HarvestUnlocked(GameState s, Side owner, ICardCatalog cat)
        {
            CellRef cell;
            int unitId;
            return !TryFindOffender(s, owner, cat, out cell, out unitId);
        }
    }
}
