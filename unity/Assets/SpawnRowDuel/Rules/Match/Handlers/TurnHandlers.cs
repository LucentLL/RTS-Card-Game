namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// End --BeginTurn(other)--> Upkeep. The opening turn never comes through here: NewMatch
    /// enters turn 1 directly at Upkeep (spec 01 s12).
    /// </summary>
    public sealed class BeginTurnHandler : ICommandHandler
    {
        public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
        {
            if (s.Phase != TurnPhase.End) return Rejection.WrongPhase;
            if (cmd.Actor != TurnMachine.Other(s.Turn)) return Rejection.NotYourTurn;
            return Rejection.None;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            TurnPipeline.BeginTurn(s, cmd.Actor, cat, ev);
        }
    }

    /// <summary>
    /// Upkeep --Harvest--> Draw, locked while a settleable offender remains. The orphan
    /// (structure-only) remainder harvests through and settles out of the proceeds.
    /// </summary>
    public sealed class HarvestHandler : ICommandHandler
    {
        public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
        {
            if (s.Turn != cmd.Actor) return Rejection.NotYourTurn;
            if (s.Phase != TurnPhase.Upkeep) return Rejection.WrongPhase;
            if (!Upkeep.HarvestUnlocked(s, cmd.Actor, cat)) return Rejection.ShortfallUnsettled;
            return Rejection.None;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            TurnPipeline.Harvest(s, cmd.Actor, cat, ev);
        }
    }

    /// <summary>
    /// Draw --DrawForTurn--> Action. Advances EVEN ON AN EMPTY DECK - there is no deck-out
    /// loss in this game (spec 02 s4.2).
    /// </summary>
    public sealed class DrawForTurnHandler : ICommandHandler
    {
        public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
        {
            if (s.Turn != cmd.Actor) return Rejection.NotYourTurn;
            if (s.Phase != TurnPhase.Draw) return Rejection.WrongPhase;
            return Rejection.None;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            MatchSetup.DrawCard(s, cmd.Actor, ev);
            TurnMachine.SetPhase(s, TurnPhase.Action, ev);
        }
    }

    /// <summary>
    /// Action --EndTurn--> End, then the drain. Refusals from Upkeep/Draw are the same
    /// WrongPhase the JS turns into its "harvest first" / "draw first" nudges - the view owns
    /// the wording. The declared-combat guard joins at M8 when declarations exist.
    /// </summary>
    public sealed class EndTurnHandler : ICommandHandler
    {
        public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
        {
            if (s.Turn != cmd.Actor) return Rejection.NotYourTurn;
            if (s.Phase != TurnPhase.Action) return Rejection.WrongPhase;
            if (s.Combat.HasDeclarations) return Rejection.DeclarationsPending;   // resolve first
            return Rejection.None;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            TurnMachine.SetPhase(s, TurnPhase.End, ev);
            // endPhaseEffects: reserved in the JS, empty here too - the seam exists on purpose.
            StructureUpkeep.DrainMana(s, cmd.Actor, ev);
        }
    }
}
