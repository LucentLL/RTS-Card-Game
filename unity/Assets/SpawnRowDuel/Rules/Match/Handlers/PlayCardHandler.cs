using System;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// place() (13_input.js:178-237): the one funnel for hand plays - summon, set face-down,
    /// set trap, and the play-on-top line. The mode is validated against the card's actual type,
    /// a check the JS local path skipped and only the MP host performed (spec 04 s19).
    ///
    /// Cast is refused until the spell resolver lands (M10) - non-trap spells are the only
    /// cards it would apply to, and they can neither be set nor summoned meanwhile.
    /// </summary>
    public sealed class PlayCardHandler : ICommandHandler
    {
        public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
        {
            var m = (PlayCardCommand)cmd;
            if (s.Turn != m.Actor) return Rejection.NotYourTurn;
            if (s.Phase != TurnPhase.Action) return Rejection.WrongPhase;

            var p = s.P(m.Actor);
            if (m.HandIndex < 0 || m.HandIndex >= p.Hand.Count) return Rejection.HandIndexOutOfRange;
            var card = p.Hand[m.HandIndex];

            CreatureCard creature;
            SpellCard spell;
            bool isCreature = cat.TryCreature(card.Id, out creature);
            bool isSpell = cat.TrySpell(card.Id, out spell);

            switch (m.Mode)
            {
                case PlayMode.Summon:
                    if (!isCreature) return Rejection.WrongPlayMode;
                    break;
                case PlayMode.Set:
                    // creatures (and structure cards, if any ever become deckable) set; a
                    // non-trap spell can NEVER be set face-down (spec 04 s10.1)
                    if (!isCreature) return Rejection.WrongPlayMode;
                    break;
                case PlayMode.SetTrap:
                    if (!isSpell || !spell.IsTrap) return Rejection.WrongPlayMode;
                    break;
                case PlayMode.Cast:
                    return Rejection.WrongPlayMode;      // M10 - the spell resolver
                default:
                    return Rejection.WrongPlayMode;      // Build-from-hand: no structure cards exist
            }

            if (!Placement.IsOwnDeployRow(m.Actor, m.To.Row)) return Rejection.DestinationNotDeployable;
            if (m.To.Col >= Board.Columns) return Rejection.CellNotReal;

            var occ = s.At(m.To);
            if (occ != null)
            {
                // ── play on top of a banked card - summon only, over your own bank ──
                if (m.Mode != PlayMode.Summon) return Rejection.CellOccupied;
                if (occ.Owner != m.Actor) return Rejection.CoveredCardNotYours;
                if (occ.Bank <= 0) return Rejection.CoveredCardHasNoBank;

                int fromBank = Math.Min(occ.Bank, creature.Cost);
                if (creature.Cost - fromBank > p.Mana) return Rejection.NotEnoughMana;
                return Rejection.None;
            }

            switch (m.Mode)
            {
                case PlayMode.Summon:
                    if (p.Mana < creature.Cost) return Rejection.NotEnoughMana;
                    break;
                case PlayMode.Set:
                case PlayMode.SetTrap:
                    if (p.Mana < 1) return Rejection.NeedsOneMana;   // no free hand-dumping
                    break;
            }

            return Rejection.None;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            var m = (PlayCardCommand)cmd;
            var p = s.P(m.Actor);
            var card = p.Hand[m.HandIndex];
            var occ = s.At(m.To);

            if (occ != null)
            {
                PlayOnTop(s, m, card, occ, cat, ev);
                return;
            }

            switch (m.Mode)
            {
                case PlayMode.Summon:
                    {
                        var t = cat.Creature(card.Id);
                        Mana.TrySpend(s, m.Actor, t.Cost);
                        p.Hand.RemoveAt(m.HandIndex);

                        var cr = UnitFactory.MakeCreature(s, m.Actor, t, card.Color);
                        cr.Sick = true;
                        s.Put(m.To, cr);
                        ev.Add(new UnitSummoned(cr.Id, m.To));

                        if (RulesHooks.OnCreatureEnter != null)
                            RulesHooks.OnCreatureEnter(s, cr, m.Actor, cat, ev);
                        if (RulesHooks.OnSummonTrap != null)
                            RulesHooks.OnSummonTrap(s, cr, m.To, cat, ev);
                        break;
                    }

                case PlayMode.Set:
                    {
                        var t = cat.Creature(card.Id);
                        Mana.TrySpend(s, m.Actor, 1);
                        p.Hand.RemoveAt(m.HandIndex);

                        var charge = new ChargeUnit();
                        charge.Id = s.NewUid();
                        charge.Owner = m.Actor;
                        charge.Color = card.Color != Element.None ? card.Color : p.PrimaryColor;
                        charge.SetIn = Board.WhichOf(m.To.Row);
                        charge.IsStructure = false;
                        charge.Card = new CardSnapshot(t.Id, t.Name, card.Color, t.Cost, t.Attack,
                            t.Health, t.Upkeep, t.Keyword, t.FirstStrike, t.Entrench, StructId.None);
                        charge.Invested = 1;                // the set's own ◆1 banks toward the flip
                        charge.SetTurn = s.TurnNumber;
                        s.Put(m.To, charge);
                        break;
                    }

                case PlayMode.SetTrap:
                    {
                        var t = cat.Spell(card.Id);
                        Mana.TrySpend(s, m.Actor, 1);       // consumed - a trap banks nothing
                        p.Hand.RemoveAt(m.HandIndex);

                        var trap = new TrapUnit();
                        trap.Id = s.NewUid();
                        trap.Owner = m.Actor;
                        trap.Color = p.PrimaryColor;
                        trap.SetIn = Board.WhichOf(m.To.Row);
                        trap.Card = t.Id;
                        trap.Effect = t.Effect;
                        trap.Value = t.Value ?? 0;
                        trap.Trigger = t.Trigger;
                        trap.SetTurn = s.TurnNumber;
                        s.Put(m.To, trap);
                        break;
                    }
            }

            WorkerMath.Resync(s, m.Actor, cat);             // afterDeploy
        }

        /// <summary>
        /// The play-on-top line (spec 04 s10.4): the covered card is DESTROYED - straight to the
        /// grave, its own summon mana gone, no death triggers - its bank pays the newcomer first,
        /// and any surplus rides onto the new unit.
        /// </summary>
        private void PlayOnTop(GameState s, PlayCardCommand m, HandCard card, BoardObject occ,
                               ICardCatalog cat, EventSink ev)
        {
            var p = s.P(m.Actor);
            var t = cat.Creature(card.Id);

            int fromBank = Math.Min(occ.Bank, t.Cost);
            int need = t.Cost - fromBank;
            int carry = Math.Max(0, occ.Bank - t.Cost);

            if (need > 0) Mana.TrySpend(s, m.Actor, need);
            s.Put(m.To, null);
            DeathSweep.ToGrave(s, m.Actor, occ);
            ev.Add(new UnitDestroyed(occ.Id, m.To, true, m.Actor, occ.Kind));

            p.Hand.RemoveAt(m.HandIndex);
            var cr = UnitFactory.MakeCreature(s, m.Actor, t, card.Color);
            cr.Sick = true;
            cr.Bank = carry;
            s.Put(m.To, cr);
            ev.Add(new UnitSummoned(cr.Id, m.To));

            if (RulesHooks.OnCreatureEnter != null)
                RulesHooks.OnCreatureEnter(s, cr, m.Actor, cat, ev);
            if (RulesHooks.OnSummonTrap != null)
                RulesHooks.OnSummonTrap(s, cr, m.To, cat, ev);

            WorkerMath.Resync(s, m.Actor, cat);
        }
    }
}
