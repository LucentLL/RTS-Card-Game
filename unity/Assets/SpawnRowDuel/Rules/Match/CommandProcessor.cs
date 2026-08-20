using System;
using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    public interface ICommandHandler
    {
        /// <summary>Pure - must not mutate anything.</summary>
        Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat);

        /// <summary>Runs ONLY after Validate returned None (the processor guarantees it).</summary>
        void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink events);
    }

    /// <summary>
    /// The one funnel. Execute ALWAYS re-runs Validate internally - there is no trusted entry
    /// point. The JS had a permissive local path, a stricter MP host path and a third AI path;
    /// here the host-grade validators are the only implementation and everyone uses them
    /// (design 01 s3.1).
    /// </summary>
    public sealed class CommandProcessor
    {
        private readonly Dictionary<Type, ICommandHandler> _handlers =
            new Dictionary<Type, ICommandHandler>();   // by-key lookup only, never iterated

        private readonly ICardCatalog _catalog;

        public CommandProcessor(ICardCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException("catalog");
            _catalog = catalog;
        }

        public ICardCatalog Catalog { get { return _catalog; } }

        public void Register(Type commandType, ICommandHandler handler)
        {
            if (commandType == null) throw new ArgumentNullException("commandType");
            if (handler == null) throw new ArgumentNullException("handler");
            _handlers[commandType] = handler;
        }

        public bool Handles(Type commandType)
        {
            return _handlers.ContainsKey(commandType);
        }

        /// <summary>The pipeline gates, then the handler's own validation. Pure.</summary>
        public Rejection CanExecute(GameState s, ICommand cmd)
        {
            if (cmd == null) return Rejection.UnknownCommand;
            if (s.IsOver) return Rejection.GameOver;

            // A parked choice freezes everything except the answer to that choice.
            if (s.Pending != null && !(cmd is RespondCommand)) return Rejection.ChoicePending;
            if (s.Pending == null && cmd is RespondCommand) return Rejection.NoPendingRequest;

            ICommandHandler h;
            if (!_handlers.TryGetValue(cmd.GetType(), out h)) return Rejection.UnknownCommand;
            return h.Validate(s, cmd, _catalog);
        }

        public CommandResult Execute(GameState s, ICommand cmd, EventSink events)
        {
            var why = CanExecute(s, cmd);            // never bypassed
            if (why != Rejection.None) return CommandResult.No(why);

            _handlers[cmd.GetType()].Execute(s, cmd, _catalog, events);
            return s.Pending == null ? CommandResult.Ok : CommandResult.Waiting;
        }
    }

    /// <summary>
    /// The default handler registry. Handlers register here as their milestones land, so the
    /// engine's wiring never changes shape - only this table grows.
    /// </summary>
    public static class CommandHandlers
    {
        public static CommandProcessor CreateDefault(ICardCatalog catalog)
        {
            var p = new CommandProcessor(catalog);

            // M6 - the turn machine and upkeep settlement
            p.Register(typeof(BeginTurnCommand), new BeginTurnHandler());
            p.Register(typeof(HarvestCommand), new HarvestHandler());
            p.Register(typeof(DrawForTurnCommand), new DrawForTurnHandler());
            p.Register(typeof(EndTurnCommand), new EndTurnHandler());
            p.Register(typeof(UpkeepPayCommand), new UpkeepPayHandler());
            p.Register(typeof(UpkeepSacrificeCommand), new UpkeepSacrificeHandler());
            p.Register(typeof(MoveUnitCommand), new MoveUnitHandler());

            // M7 - placement and structures
            p.Register(typeof(PlayCardCommand), new PlayCardHandler());
            p.Register(typeof(BuildStructureCommand), new BuildStructureHandler());
            p.Register(typeof(UpgradeStructureCommand), new UpgradeStructureHandler());
            p.Register(typeof(PourIntoChargeCommand), new PourIntoChargeHandler());
            p.Register(typeof(FlipChargeCommand), new FlipChargeHandler());
            p.Register(typeof(SendBankedManaCommand), new SendBankedManaHandler());

            // M8: DeclareAttack / ResolveCombat / Respond
            return p;
        }
    }
}
