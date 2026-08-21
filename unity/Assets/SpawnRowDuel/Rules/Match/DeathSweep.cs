using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// The settling loop - cleanup() (16_movement.js:193-207) - and the graveyard writer.
    ///
    /// Sweep order is determinism-critical and pinned: global ROWS order (FoeBack, FoeFront,
    /// Center, YouFront, YouBack), slots 0..6 - i.e. ascending cell index - then both players'
    /// worker pools. A dead creature's cell is freed BEFORE its death trigger fires, and the
    /// whole sweep repeats (guard 40) so chained kills resolve in one call.
    ///
    /// Deliberately NOT here: WorkerMath.Resync. A mid-combat raze leaves stale workers standing
    /// until the next sync - observable, and reproducing it is a requirement (spec 02 s6.4 Bug 2).
    /// </summary>
    public static class DeathSweep
    {
        public static void Cleanup(GameState s, ICardCatalog cat, EventSink ev)
        {
            bool any = true;
            int guard = 0;
            while (any && guard++ < 40)
            {
                any = false;

                for (int i = 0; i < Board.Cells; i++)
                {
                    var cell = CellRef.FromIndex(i);
                    var o = s.At(cell);
                    if (o == null) continue;

                    var cre = o as CreatureUnit;
                    var bld = o as StructureUnit;
                    if (cre != null && cre.Hp <= 0)
                    {
                        s.Put(cell, null);                       // cell freed BEFORE the trigger
                        if (!cre.IsWorker)
                            KeywordEngine.OnDeath(s, cre, cre.Owner, cat, ev);
                        ToGrave(s, cre.Owner, cre);
                        ev.Add(new UnitDestroyed(cre.Id, cell, true, cre.Owner, UnitKind.Creature));
                        any = true;
                    }
                    else if (bld != null && bld.Hp <= 0)
                    {
                        s.Put(cell, null);
                        ToGrave(s, bld.Owner, bld);
                        ev.Add(new UnitDestroyed(bld.Id, cell, true, bld.Owner, UnitKind.Building));
                        any = true;
                    }
                    // face-down charges and traps have no HP and are never swept here
                }

                for (int side = 0; side < 2; side++)
                {
                    var p = s.Players[side];
                    for (int z = 0; z < p.Workers.Length; z++)
                    {
                        var pool = p.Workers[z].Members;
                        for (int i = pool.Count - 1; i >= 0; i--)
                        {
                            if (pool[i].Hp > 0) continue;
                            var w = pool[i];
                            ToGrave(s, (Side)side, w);
                            pool.RemoveAt(i);
                            ev.Add(new UnitDestroyed(w.Id, default(CellRef), false, (Side)side,
                                                     UnitKind.Creature));
                            any = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// toGrave (07_structures.js:67-75). Grave kinds collapse the way the JS types do:
        /// a face-down charge graves as what it would have been, a trap graves as its spell.
        /// Workers grave flagged IsWorker (the JS 'villager' type) so the Reliquary never
        /// returns them; tokens keep IsToken for the same reason.
        /// </summary>
        public static void ToGrave(GameState s, Side owner, BoardObject obj)
        {
            if (obj == null) return;

            var cre = obj as CreatureUnit;
            if (cre != null)
            {
                // the record carries the LIVE statline (toGrave, 07_structures.js:69) so a
                // Reliquary recall returns the hatched / hardened form, not the registry card
                s.P(owner).Grave.Add(new GraveRecord(cre.Card, cre.Name, cre.Color,
                    UnitKind.Creature, cre.IsToken, cre.IsWorker, s.TurnNumber,
                    CreatureSnapshot.From(cre)));
                return;
            }

            var bld = obj as StructureUnit;
            if (bld != null)
            {
                s.P(owner).Grave.Add(new GraveRecord(new CardId(bld.DefId.Value), bld.DefId.Value,
                    bld.Color, UnitKind.Building, false, false, s.TurnNumber));
                return;
            }

            var charge = obj as ChargeUnit;
            if (charge != null)
            {
                s.P(owner).Grave.Add(new GraveRecord(charge.Card.Id, charge.Card.Name, charge.Card.Color,
                    charge.IsStructure ? UnitKind.Building : UnitKind.Creature, false, false,
                    s.TurnNumber));
                return;
            }

            var trap = obj as TrapUnit;
            if (trap != null)
            {
                s.P(owner).Grave.Add(new GraveRecord(trap.Card, trap.Card.Value, trap.Color,
                    UnitKind.Trap, false, false, s.TurnNumber));
            }
        }
    }
}
