using System;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// flip() (14_spells_traps.js:110-127), shared by the FlipCharge command and the provoked
    /// face-down path. Surplus investment banks onto the unit; sickness is decided by setTurn
    /// (same turn = sick, a later turn = battle-ready); the JS colour-drop and the missing
    /// structure-branch resync stay behind their RulesOptions flags, default faithful.
    /// </summary>
    public static class ChargeOps
    {
        public static void Flip(GameState s, Side owner, CellRef at, ICardCatalog cat, EventSink ev)
        {
            var ch = s.At(at) as ChargeUnit;
            if (ch == null) return;

            int bank = Math.Max(0, ch.Invested - ch.Card.Cost);

            if (ch.IsStructure)
            {
                var def = cat.Structure(ch.Card.StructDef,
                    s.Options.FaceDownKeepsColor ? ch.Card.Color : Element.None);
                var b = UnitFactory.MakeStructure(s, owner, def);
                b.Bank = bank;
                s.Put(at, b);
                ev.Add(new CardFlipped(b.Id, at, false));

                if (s.Options.FlipStructureResyncsWorkers)
                    WorkerMath.Resync(s, owner, cat);          // the JS forgets this (spec 02 Bug 1)
                return;
            }

            var t = cat.Creature(ch.Card.Id);
            var color = s.Options.FaceDownKeepsColor
                ? ch.Card.Color
                : s.P(owner).PrimaryColor;                     // mkCre's fallback - the colour-drop bug
            var cr = UnitFactory.MakeCreature(s, owner, t, color);
            cr.Bank = bank;
            cr.Sick = s.TurnNumber <= ch.SetTurn;
            s.Put(at, cr);
            ev.Add(new CardFlipped(cr.Id, at, cr.Sick));

            if (RulesHooks.OnCreatureEnter != null)
                RulesHooks.OnCreatureEnter(s, cr, owner, cat, ev);
            // deliberately NO summon-trap hook - flip immunity is the point of setting

            WorkerMath.Resync(s, owner, cat);
        }
    }
}
