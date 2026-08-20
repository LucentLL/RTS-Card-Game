namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// The explicit phase machine, identical for both sides (spec 02 s4, spec 07 s3.1).
    /// The JS left G.phase parked at 'end' for the whole AI turn and four call sites depended on
    /// the accident; here the AI runs the same Upkeep-Draw-Action-End sequence and input gating
    /// reads GameState.IsInteractive instead.
    /// </summary>
    public static class TurnMachine
    {
        public static readonly TurnPhase[] Order =
        {
            TurnPhase.Upkeep, TurnPhase.Draw, TurnPhase.Action, TurnPhase.End,
        };

        /// <summary>The ONLY writer of GameState.Phase.</summary>
        public static void SetPhase(GameState s, TurnPhase p, EventSink ev)
        {
            if (s.Phase == p) return;
            if (ev != null) ev.Add(new PhaseChanged(s.Phase, p));
            s.Phase = p;
        }

        public static Side Other(Side side)
        {
            return side == Side.You ? Side.Foe : Side.You;
        }
    }
}
