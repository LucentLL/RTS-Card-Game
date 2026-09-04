namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// The ◆ Pay settle action (upkeepPay, 17_turns_ai.js:127-137). Pays min(creature.up,
    /// zone deficit) - no partial payment against insufficient mana - and marks the creature
    /// settled even when the capped amount is less than its full upkeep.
    /// </summary>
    public sealed class UpkeepPayHandler : ICommandHandler
    {
        public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
        {
            var m = (UpkeepPayCommand)cmd;
            if (s.Turn != m.Actor) return Rejection.NotYourTurn;
            if (s.Phase != TurnPhase.Upkeep) return Rejection.WrongPhase;

            var c = s.At(m.Target) as CreatureUnit;
            if (c == null) return Rejection.NotACreature;
            if (c.Id != m.UnitId) return Rejection.NoSuchUnit;
            if (c.Owner != m.Actor) return Rejection.NotYourUnit;
            if (c.PaidUpkeep) return Rejection.NothingToPay;

            int cost = PayAmount(s, m.Actor, m.Target, c, cat);
            if (cost <= 0) return Rejection.NothingToPay;
            if (s.P(m.Actor).Mana < cost) return Rejection.NotEnoughMana;
            return Rejection.None;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            var m = (UpkeepPayCommand)cmd;
            var c = (CreatureUnit)s.At(m.Target);
            var zone = Board.ZoneForRow(m.Actor, m.Target.Row);

            int cost = PayAmount(s, m.Actor, m.Target, c, cat);
            Mana.TrySpend(s, m.Actor, cost);
            s.P(m.Actor).UpkeepPaid[(int)zone] += cost;
            c.PaidUpkeep = true;
            ev.Add(new WorkerShortfallSettled(m.Actor, zone, SettleKind.Pay, c.Id));
        }

        private static int PayAmount(GameState s, Side actor, CellRef at, CreatureUnit c,
                                     ICardCatalog cat)
        {
            var zone = Board.ZoneForRow(actor, at.Row);
            int deficit = Upkeep.ZoneDeficit(s, actor, zone, cat);
            return c.Upkeep < deficit ? c.Upkeep : deficit;
        }
    }

    /// <summary>
    /// The ✖ Sacrifice settle action (upkeepSac, 17_turns_ai.js:138-144). Straight ToGrave,
    /// NOT through the death sweep - Detonate and Reap deliberately do NOT fire on a
    /// sacrifice (spec 07 s6.4). No mana refund, and not restricted to deficit zones.
    /// </summary>
    public sealed class UpkeepSacrificeHandler : ICommandHandler
    {
        public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
        {
            var m = (UpkeepSacrificeCommand)cmd;
            if (s.Turn != m.Actor) return Rejection.NotYourTurn;
            if (s.Phase != TurnPhase.Upkeep) return Rejection.WrongPhase;

            var c = s.At(m.Target) as CreatureUnit;
            if (c == null) return Rejection.NotACreature;
            if (c.Id != m.UnitId) return Rejection.NoSuchUnit;
            if (c.Owner != m.Actor) return Rejection.NotYourUnit;
            return Rejection.None;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            var m = (UpkeepSacrificeCommand)cmd;
            var c = (CreatureUnit)s.At(m.Target);
            var zone = Board.ZoneForRow(m.Actor, m.Target.Row);

            s.Put(m.Target, null);
            DeathSweep.ToGrave(s, m.Actor, c);
            WorkerMath.Resync(s, m.Actor, cat);
            ev.Add(new UnitDestroyed(c.Id, m.Target, true, m.Actor, UnitKind.Creature));
            ev.Add(new WorkerShortfallSettled(m.Actor, zone, SettleKind.Sacrifice, c.Id));
        }
    }

    public static class MoveRules
    {
        /// <summary>
        /// moveSpent, with the JS's implicit global G.upkeep made explicit as "the OWNER's own
        /// upkeep window" (spec 04 s5.4). One free move per turn; a second move exists only
        /// during your own upkeep and taps the creature, spending its whole turn.
        /// </summary>
        public static bool MoveSpent(GameState s, CreatureUnit u)
        {
            if (!u.Moved) return false;
            bool upkeepWindow = s.Phase == TurnPhase.Upkeep && s.Turn == u.Owner;
            return !(upkeepWindow && !u.MovedTwice && !u.Tapped);
        }
    }

    /// <summary>
    /// One-square any-direction movement (doMove, 16_movement.js:39-57). Deliberately NOT
    /// gated by Sick, Tapped, IsWorker or Entrench (spec 04 s5.2) - and the phase gate the JS
    /// left to its UI is explicit here, host semantics (spec 04 s19).
    /// </summary>
    public sealed class MoveUnitHandler : ICommandHandler
    {
        public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
        {
            var m = (MoveUnitCommand)cmd;
            if (s.Turn != m.Actor) return Rejection.NotYourTurn;
            if (s.Phase != TurnPhase.Action && s.Phase != TurnPhase.Upkeep)
                return Rejection.WrongPhase;

            var u = s.At(m.From) as CreatureUnit;
            if (u == null) return Rejection.NotACreature;
            if (u.Id != m.UnitId) return Rejection.NoSuchUnit;
            if (u.Owner != m.Actor) return Rejection.NotYourUnit;
            if (MoveRules.MoveSpent(s, u)) return Rejection.MoveAlreadySpent;

            if (!Board.IsRealSlot(m.To.Row, m.To.Col)) return Rejection.CellNotReal;
            if (!Board.InStepRange(m.From, m.To)) return Rejection.NotAdjacent;

            // A CARD OF YOUR OWN IN THE WAY IS NOT A WALL - but only once there is no way round
            // it. A move crosses a whole row now, so the only thing that can stop a march is your
            // own board being in front of it, and "I cannot advance because my Forge is there" is
            // a rule about furniture rather than about the enemy. So: if the destination row is
            // FULL, a creature may take one of your cards' places, destroying it.
            //
            // The full-row condition is not only the stated rule, it is what keeps the board
            // tappable: without it every one of your own cards would be a move destination as
            // well as a thing to select, and selecting your own units is the commonest tap there
            // is. With it, the two only collide in a row with no space left in it.
            //
            // An ENEMY card is still a wall: walking through one would be combat without a fight.
            var occ = s.At(m.To);
            if (occ != null)
            {
                if (occ.Owner != m.Actor) return Rejection.CellOccupied;
                if (HasRoomIn(s, m.To.Row)) return Rejection.CellOccupied;
            }

            return Rejection.None;
        }

        /// <summary>Is any cell of this row still empty? A move that razes is a last resort.</summary>
        static bool HasRoomIn(GameState s, RowKey row)
        {
            for (int c = 0; c < Board.Columns; c++)
                if (s.At(new CellRef(row, c)) == null) return true;
            return false;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            var m = (MoveUnitCommand)cmd;
            var u = (CreatureUnit)s.At(m.From);

            // whatever of ours was standing there is razed to make room, and any mana banked on
            // it rides on with the creature that displaced it rather than evaporating
            var occ = s.At(m.To);
            if (occ != null)
            {
                s.Put(m.To, null);
                DeathSweep.ToGrave(s, m.Actor, occ);
                ev.Add(new UnitDestroyed(occ.Id, m.To, true, m.Actor, occ.Kind));
                u.Bank += Mana.OnCard(occ);        // a face-down banks in Invested, not Bank
            }

            s.Put(m.From, null);                            // vacate FIRST (spec 04 s6)
            if (u.Moved) { u.MovedTwice = true; u.Tapped = true; }   // the second move taps
            else u.Moved = true;
            s.Put(m.To, u);

            WorkerMath.Resync(s, m.Actor, cat);             // upkeep migrates between row figures
            ev.Add(new UnitMoved(u.Id, m.From, m.To, u.MovedTwice));
        }
    }
}
