using System;

namespace SpawnRowDuel.Rules
{
    /// <summary>camtPour: any amount up to your pool, no cap, onto your own face-down.</summary>
    public sealed class PourIntoChargeHandler : ICommandHandler
    {
        public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
        {
            var m = (PourIntoChargeCommand)cmd;
            if (s.Turn != m.Actor) return Rejection.NotYourTurn;
            if (s.Phase != TurnPhase.Action) return Rejection.WrongPhase;

            var ch = s.At(m.At) as ChargeUnit;
            if (ch == null) return Rejection.NotAFaceDown;
            if (ch.Id != m.UnitId) return Rejection.NoSuchUnit;
            if (ch.Owner != m.Actor) return Rejection.NotYourUnit;
            if (m.Amount <= 0) return Rejection.NothingToPay;
            if (s.P(m.Actor).Mana < m.Amount) return Rejection.NotEnoughMana;
            return Rejection.None;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            var m = (PourIntoChargeCommand)cmd;
            var ch = (ChargeUnit)s.At(m.At);
            Mana.TrySpend(s, m.Actor, m.Amount);
            ch.Invested += m.Amount;
        }
    }

    /// <summary>
    /// flip() (14_spells_traps.js:110-127). Surplus investment banks onto the unit; sickness is
    /// decided by setTurn (same turn = sick, a later turn = battle-ready - the payoff for
    /// setting); flipping NEVER provokes a summon trap. The JS drops the card's colour and
    /// skips the structure-branch worker resync - both preserved behind their RulesOptions
    /// flags, default JS-faithful.
    /// </summary>
    public sealed class FlipChargeHandler : ICommandHandler
    {
        public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
        {
            var m = (FlipChargeCommand)cmd;
            if (s.Turn != m.Actor) return Rejection.NotYourTurn;
            if (s.Phase != TurnPhase.Action) return Rejection.WrongPhase;

            var ch = s.At(m.At) as ChargeUnit;
            if (ch == null) return Rejection.NotAFaceDown;
            if (ch.Id != m.UnitId) return Rejection.NoSuchUnit;
            if (ch.Owner != m.Actor) return Rejection.NotYourUnit;
            if (ch.Invested < ch.Card.Cost) return Rejection.ChargeUnderfunded;
            return Rejection.None;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            var m = (FlipChargeCommand)cmd;
            ChargeOps.Flip(s, m.Actor, m.At, cat, ev);    // shared with the provoked path
        }
    }

    /// <summary>
    /// doSendMana: the WHOLE bank moves from one of your board cards to another. No phase gate
    /// beyond it being your live turn - this deliberately works during Upkeep too (spec 02
    /// s9.4). The JS forgot to owner-check the SOURCE; the host semantics here check both.
    /// </summary>
    public sealed class SendBankedManaHandler : ICommandHandler
    {
        public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
        {
            var m = (SendBankedManaCommand)cmd;
            if (s.Turn != m.Actor) return Rejection.NotYourTurn;
            if (m.From == m.To) return Rejection.TargetKindIllegal;

            var src = s.At(m.From);
            if (src == null) return Rejection.NoSuchUnit;
            if (src.Owner != m.Actor) return Rejection.NotYourUnit;
            if (src.Bank <= 0) return Rejection.CoveredCardHasNoBank;

            var dst = s.At(m.To);
            if (dst == null) return Rejection.NoSuchUnit;
            if (dst.Owner != m.Actor) return Rejection.NotYourUnit;
            if (!(dst is CreatureUnit) && !(dst is StructureUnit)) return Rejection.TargetKindIllegal;

            return Rejection.None;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            var m = (SendBankedManaCommand)cmd;
            var src = s.At(m.From);
            var dst = s.At(m.To);
            dst.Bank += src.Bank;
            src.Bank = 0;
        }
    }
}
