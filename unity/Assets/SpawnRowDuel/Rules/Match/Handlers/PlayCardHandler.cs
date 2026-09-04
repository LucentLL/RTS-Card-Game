using System;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// place() (13_input.js:178-237) and castSpell (14_spells_traps.js:26-33): the one funnel for
    /// hand plays - summon, set face-down, set trap, cast, and the play-on-top line. The mode is
    /// validated against the card's actual type, a check the JS local path skipped and only the
    /// MP host performed (spec 04 s19).
    ///
    /// A hand card may carry a CreatureSnapshot - it has been on the board before, bounced or
    /// recalled - and when it does, that statline wins over the registry everywhere: the cost
    /// checked, the body summoned, and the data frozen into a face-down set.
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
            bool inCatalog = cat.TryCreature(card.Id, out creature);

            // a card carrying a snapshot has BEEN a creature; it can never be a spell, whatever
            // name collision the registry might hold
            SpellCard spell = null;
            if (!card.Snapshot.HasValue)
            {
                SpellCard found;
                if (cat.TrySpell(card.Id, out found)) spell = found;
            }
            bool isSpell = spell != null;

            // a snapshot IS a creature, whatever the registry knows - a bounced token has no
            // catalog entry at all
            bool isCreature = card.Snapshot.HasValue || inCatalog;
            int creatureCost = card.Snapshot.HasValue ? card.Snapshot.Cost
                             : (inCatalog ? creature.Cost : 0);

            if (m.To.Col >= Board.Columns) return Rejection.CellNotReal;

            // ── cast: the only mode that targets the ENEMY half of the board ──
            if (m.Mode == PlayMode.Cast)
            {
                if (!isSpell || spell.IsTrap) return Rejection.WrongPlayMode;
                if (p.Mana < spell.Cost) return Rejection.NotEnoughMana;

                var target = s.At(m.To);
                if (target == null) return Rejection.NoLegalTarget;
                if (target.Owner == m.Actor) return Rejection.TargetNotEnemy;
                if (!SpellTargeting.CanTarget(spell, target, m.Actor))
                    return Rejection.TargetKindIllegal;
                return Rejection.None;
            }

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
                default:
                    return Rejection.WrongPlayMode;      // Build-from-hand: no structure cards exist
            }

            if (!Placement.IsOwnDeployRow(m.Actor, m.To.Row)) return Rejection.DestinationNotDeployable;

            int cost = m.Mode == PlayMode.Summon ? creatureCost : 1;   // a set costs ◆1, always

            // ── PLAYING OVER ONE OF YOUR OWN CARDS ──
            //
            // The old rule was narrower on both axes: summon only, and only over a card holding
            // BANKED mana, because the line existed to spend that bank. Both limits went with
            // "let a player put a card on a structure they own" - a board of your own buildings
            // should not be able to lock you out of your own deploy rows, and which of your cards
            // is in the way is not the interesting decision.
            //
            // What has not changed is whose card it may be. Covering an ENEMY card would be a
            // removal spell that every creature has for free.
            var occ = s.At(m.To);
            if (occ != null)
            {
                if (occ.Owner != m.Actor) return Rejection.CoveredCardNotYours;

                int fromBank = Math.Min(Mana.OnCard(occ), cost);
                if (cost - fromBank > p.Mana)
                    return m.Mode == PlayMode.Summon ? Rejection.NotEnoughMana
                                                     : Rejection.NeedsOneMana;
                return Rejection.None;
            }

            if (p.Mana < cost)
                return m.Mode == PlayMode.Summon ? Rejection.NotEnoughMana
                                                 : Rejection.NeedsOneMana;  // no free hand-dumping

            return Rejection.None;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            var m = (PlayCardCommand)cmd;
            var p = s.P(m.Actor);
            var card = p.Hand[m.HandIndex];

            if (m.Mode == PlayMode.Cast)
            {
                Cast(s, m, card, cat, ev);
                return;
            }

            // Cost, then the ground. Anything of ours already standing there is razed to make
            // room; its bank pays for what replaces it and any surplus rides on (spec 04 s10.4,
            // widened - see Validate).
            int cost = m.Mode == PlayMode.Summon ? CostOf(card, cat) : 1;
            var occ = s.At(m.To);
            int carry = 0;

            if (occ != null)
            {
                int banked = Mana.OnCard(occ);
                int fromBank = Math.Min(banked, cost);
                carry = Math.Max(0, banked - cost);
                if (cost - fromBank > 0) Mana.TrySpend(s, m.Actor, cost - fromBank);

                s.Put(m.To, null);
                DeathSweep.ToGrave(s, m.Actor, occ);      // no death triggers - it is not killed
                ev.Add(new UnitDestroyed(occ.Id, m.To, true, m.Actor, occ.Kind));
            }
            else Mana.TrySpend(s, m.Actor, cost);

            p.Hand.RemoveAt(m.HandIndex);

            switch (m.Mode)
            {
                case PlayMode.Summon:
                    {
                        var cr = Materialise(s, m.Actor, card, cat);
                        cr.Sick = true;
                        cr.Bank = carry;
                        s.Put(m.To, cr);
                        ev.Add(new UnitSummoned(cr.Id, m.To));

                        // afterDeploy runs BEFORE the trap resolves - which is the RESP-layer
                        // ordering, not the bare place() one. RESP.actingGate defers
                        // foeTrapOnSummon to the end of its window (30_resp.js:118-121), so the
                        // synchronous tail - syncWorkers included - has already run by the time
                        // the trap springs. It is observable: a trapped creature's upkeep is
                        // counted into the worker figure and stays counted until the next
                        // resync, because cleanup() deliberately does not resync.
                        WorkerMath.Resync(s, m.Actor, cat);
                        Triggers.CreatureSummoned(s, cr, m.To, m.Actor, cat, ev);
                        return;
                    }

                case PlayMode.Set:
                    {
                        var charge = new ChargeUnit();
                        charge.Id = s.NewUid();
                        charge.Owner = m.Actor;
                        charge.Color = card.Color != Element.None ? card.Color : p.PrimaryColor;
                        charge.SetIn = Board.WhichOf(m.To.Row);
                        charge.IsStructure = false;
                        charge.Card = SnapshotFor(card, cat);
                        charge.Snap = card.Snapshot;       // empty for an ordinary deck card
                        // the set's own ◆1 banks toward the flip, and so does whatever was banked
                        // on the card it replaced - that mana was on this square already
                        charge.Invested = 1 + carry;
                        charge.SetTurn = s.TurnNumber;
                        s.Put(m.To, charge);
                        break;
                    }

                case PlayMode.SetTrap:
                    {
                        var t = cat.Spell(card.Id);        // a trap banks nothing; carry is lost
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
        /// castSpell: the effect resolves FIRST and the mana is spent only if it took - which is
        /// why an illegal target costs nothing. Validate has already proved the target legal, so
        /// a fizzle here would be a rules bug, not a player mistake, and the card is spent either
        /// way rather than silently vanishing from the pipeline.
        ///
        /// No worker resync: razing a structure leaves its workers standing until the next sync,
        /// and reproducing that is a requirement (spec 02 s6.4 Bug 2).
        /// </summary>
        private void Cast(GameState s, PlayCardCommand m, HandCard card, ICardCatalog cat,
                          EventSink ev)
        {
            var p = s.P(m.Actor);
            var spell = cat.Spell(card.Id);

            SpellEngine.Resolve(s, m.Actor, spell, m.To, cat, ev);

            Mana.TrySpend(s, m.Actor, spell.Cost);
            p.Hand.RemoveAt(m.HandIndex);
            p.Grave.Add(SpellEngine.SpellRecord(spell, s.TurnNumber));
            ev.Add(new SpellResolved(m.Actor, spell.Id, true, m.To));

            CombatResolver.CheckWin(s, ev);
        }

        /// <summary>The statline that governs this play: the card's own history, else the registry.</summary>
        static CreatureUnit Materialise(GameState s, Side owner, HandCard card, ICardCatalog cat)
        {
            return card.Snapshot.HasValue
                ? UnitFactory.MakeCreature(s, owner, card.Id, card.Snapshot, card.Color)
                : UnitFactory.MakeCreature(s, owner, cat.Creature(card.Id), card.Color);
        }

        static int CostOf(HandCard card, ICardCatalog cat)
        {
            return card.Snapshot.HasValue ? card.Snapshot.Cost : cat.Creature(card.Id).Cost;
        }

        /// <summary>The frozen face of a face-down card - from the snapshot when it has one.</summary>
        static CardSnapshot SnapshotFor(HandCard card, ICardCatalog cat)
        {
            if (card.Snapshot.HasValue)
            {
                var k = card.Snapshot;
                return new CardSnapshot(card.Id, k.Name, card.Color, k.Cost, k.Attack, k.Health,
                    k.Upkeep, k.Keyword, k.FirstStrike, k.Entrench, StructId.None);
            }
            var t = cat.Creature(card.Id);
            return new CardSnapshot(t.Id, t.Name, card.Color, t.Cost, t.Attack, t.Health,
                t.Upkeep, t.Keyword, t.FirstStrike, t.Entrench, StructId.None);
        }
    }
}
