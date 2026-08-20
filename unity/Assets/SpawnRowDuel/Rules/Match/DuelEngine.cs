using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// The facade the view, the AI and (later) the network host all talk to. Zero async, zero
    /// UnityEngine, zero wall clock: the engine either applies a command, rejects it with a
    /// reason, or parks on a PendingRequest and waits for a RespondCommand.
    ///
    /// After M5 this surface is FROZEN - view work builds against it in parallel while the
    /// remaining handlers land behind it (PORT_PLAN M5).
    /// </summary>
    public sealed class DuelEngine
    {
        private readonly GameState _state;
        private readonly CommandProcessor _processor;
        private readonly EventSink _events = new EventSink();

        public DuelEngine(GameState state, ICardCatalog catalog)
            : this(state, CommandHandlers.CreateDefault(catalog))
        {
        }

        public DuelEngine(GameState state, CommandProcessor processor)
        {
            if (state == null) throw new System.ArgumentNullException("state");
            if (processor == null) throw new System.ArgumentNullException("processor");
            _state = state;
            _processor = processor;
        }

        public GameState State { get { return _state; } }

        public ICardCatalog Catalog { get { return _processor.Catalog; } }

        /// <summary>The parked choice, or null when the engine is free to act.</summary>
        public PendingRequest Pending { get { return _state.Pending; } }

        /// <summary>Events accumulated since the last drain. The view consumes these; rules never read them.</summary>
        public IReadOnlyList<GameEvent> Events { get { return _events.Events; } }

        /// <summary>Pure what-if: would this command be accepted right now?</summary>
        public Rejection CanApply(ICommand cmd)
        {
            return _processor.CanExecute(_state, cmd);
        }

        public CommandResult Apply(ICommand cmd)
        {
            return _processor.Execute(_state, cmd, _events);
        }

        public List<GameEvent> DrainEvents()
        {
            return _events.Drain();
        }

        /// <summary>The canonical 64-bit state hash - the regression suite's workhorse.</summary>
        public ulong Hash()
        {
            return StateCodec.Hash(_state);
        }
    }
}
