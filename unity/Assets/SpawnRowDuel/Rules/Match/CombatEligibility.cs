using System;
using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// Who may block (spec 03 s4). Row-interval based: a block may come only from a row the
    /// attack CROSSES INTO - Board.RowsCrossedInto - so a same-row strike is an uninterposable
    /// point-blank duel, and a wall strike from inside the enemy back row cannot be stopped.
    ///
    /// The two blocker predicates are deliberately INVERTED from each other and both are
    /// load-bearing as written (spec 03 s4.2):
    ///   board creature: not-yet-blocked required; tapped and summoning-sick are IRRELEVANT
    ///   pool worker:    untapped AND un-sick required; the blocked flag is NOT checked
    /// </summary>
    public static class CombatEligibility
    {
        /// <summary>
        /// Enumeration order is the contract: crossed rows in travel order, board slots 0..6
        /// ascending, then that row's worker stacks - You's pool before Foe's (the JS enumerates
        /// you-then-foe in the shared center row).
        /// </summary>
        public static List<UnitRef> EligibleInterceptors(GameState s, Side attackerOwner,
                                                         int aIdx, int tIdx)
        {
            var outRefs = new List<UnitRef>();
            Span<RowKey> crossed = stackalloc RowKey[5];
            int n = Board.RowsCrossedInto(aIdx, tIdx, crossed);

            for (int r = 0; r < n; r++)
            {
                var row = crossed[r];

                for (int col = 0; col < Board.Columns; col++)
                {
                    var cell = new CellRef(row, col);
                    var c = s.At(cell) as CreatureUnit;
                    if (c == null || c.Owner == attackerOwner || c.HasBlocked) continue;
                    outRefs.Add(UnitRef.Cell(cell, c.Id));
                }

                for (int p = 0; p < 2; p++)                    // You before Foe
                {
                    var owner = (Side)p;
                    if (owner == attackerOwner) continue;
                    var zone = Board.ZoneForRow(owner, row);
                    if (zone == WorkerZone.Raid) continue;     // no pool behind enemy lines
                    if (Board.RowsOfZone(owner, zone)[0] != row) continue;

                    var pool = s.P(owner).Workers[(int)zone].Members;
                    for (int i = 0; i < pool.Count; i++)
                    {
                        var w = pool[i];
                        if (w.Tapped || w.Sick) continue;
                        outRefs.Add(UnitRef.Pool(new PoolRef(owner, zone, (byte)i), w.Id));
                    }
                }
            }
            return outRefs;
        }

        /// <summary>
        /// The full per-declaration eligibility, exclusions applied (spec 03 s4.4): none for a
        /// Scour attacker or a same-row duel; the target itself never "blocks" (it retaliates);
        /// a targeted worker stack cannot screen itself.
        /// </summary>
        public static List<UnitRef> ForDeclaration(GameState s, AttackDeclaration d, Side actor)
        {
            var attacker = s.FindById(d.AttackerUnitId, out _, out _) as CreatureUnit;
            if (attacker == null) return new List<UnitRef>();
            if (attacker.Keyword == Keyword.Scour && !attacker.IsWorker)
                return new List<UnitRef>();                    // fliers are unblockable

            int aIdx = (int)d.Attacker.Row;
            int tIdx = TargetRowIndex(s, d);
            if (aIdx == tIdx) return new List<UnitRef>();

            var elig = EligibleInterceptors(s, actor, aIdx, tIdx);

            for (int i = elig.Count - 1; i >= 0; i--)
            {
                if (d.Kind == DeclarationKind.Unit && elig[i].UnitId == d.TargetUnitId)
                    elig.RemoveAt(i);
                else if (d.Kind == DeclarationKind.WorkerStack && elig[i].IsPool)
                {
                    var pr = elig[i].AsPool;
                    if (pr.Owner == d.TargetSide && pr.Zone == d.TargetZone) elig.RemoveAt(i);
                }
            }
            return elig;
        }

        /// <summary>The target's row index, walls as the virtual -1 / 5.</summary>
        public static int TargetRowIndex(GameState s, AttackDeclaration d)
        {
            switch (d.Kind)
            {
                case DeclarationKind.Wall:
                    return d.TargetSide == Side.Foe ? Board.FoeWallRow : Board.YouWallRow;
                case DeclarationKind.WorkerStack:
                    return (int)Board.RowsOfZone(d.TargetSide, d.TargetZone)[0];
                default:
                    // by identity when the target still stands; the declared cell otherwise
                    CellRef at;
                    bool onBoard;
                    var t = s.FindById(d.TargetUnitId, out at, out onBoard);
                    return t != null && onBoard ? (int)at.Row : (int)d.TargetCell.Row;
            }
        }
    }
}
