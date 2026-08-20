using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// The whole match, and nothing but the match.
    ///
    /// The board is ONE positional array indexed by CellRef, never per-player row collections.
    /// The JS kept G.P.you.front / G.P.foe.front, which read as "units this player owns" but are
    /// actually POSITIONAL rows that enemy raiders legally occupy. Every rule that reads ownership
    /// from the containing array is wrong; here ownership lives on the object and position lives in
    /// the array, so the two cannot be confused (spec 01 s13.2).
    ///
    /// Explicitly NOT here, because they are view concerns: Busy, Selection, AttackGroup, MoveFrom,
    /// MoveMana, CardMenu, PendingBuild, hints, log HTML. Also removed per the specs: cmana,
    /// firstExtract, villagerUsed, powerMode, deficit, minSel, and G.upkeep (derived from Phase).
    /// </summary>
    public sealed class GameState
    {
        public const int SchemaVersion = 1;

        // ---- identity / bookkeeping ----------------------------------------------------------

        /// <summary>MUST be serialized. Ids are never reused, so a save that resets this corrupts refs.</summary>
        public int NextUid = 1;

        /// <summary>Frozen at match creation and folded into the state hash.</summary>
        public RulesOptions Options;

        /// <summary>Seeded PCG32. Its stream POSITION is part of the state.</summary>
        public Pcg32 Random = new Pcg32(0UL);

        // ---- turn machine ---------------------------------------------------------------------

        public Side Turn;

        /// <summary>The ply counter - one per HALF turn, matching the JS.</summary>
        public int TurnNumber = 1;

        public TurnPhase Phase = TurnPhase.Upkeep;
        public bool IsOver;
        public MatchOutcome Outcome = MatchOutcome.InProgress;

        /// <summary>
        /// The parked choice, when the engine is waiting on a RespondCommand. Serialized, so a
        /// snapshot taken mid-resolution is complete and resumable - the property the JS could
        /// never have because its choices lived in an await chain (design 01 s6).
        /// PendingRequest instances are immutable; Clone shares the reference.
        /// </summary>
        public PendingRequest Pending;

        // ---- board ----------------------------------------------------------------------------

        private readonly BoardObject[] _cells = new BoardObject[Board.Cells];

        public readonly PlayerState[] Players = { new PlayerState(), new PlayerState() };

        public PlayerState P(Side s) { return Players[(int)s]; }

        public BoardObject At(CellRef c) { return _cells[c.Index]; }

        public void Put(CellRef c, BoardObject o) { _cells[c.Index] = o; }

        public bool IsEmpty(CellRef c) { return _cells[c.Index] == null; }

        /// <summary>
        /// Every object on the board in CANONICAL order - ascending cell index, which is ascending
        /// RowKey then ascending Col. Pinned because iteration order decides which unit a rule
        /// picks when several tie, and an unordered walk is exactly how two engines silently
        /// diverge.
        /// </summary>
        public IEnumerable<KeyValuePair<CellRef, BoardObject>> Objects()
        {
            for (int i = 0; i < _cells.Length; i++)
                if (_cells[i] != null)
                    yield return new KeyValuePair<CellRef, BoardObject>(CellRef.FromIndex(i), _cells[i]);
        }

        /// <summary>Objects belonging to a side, wherever they physically stand.</summary>
        public IEnumerable<KeyValuePair<CellRef, BoardObject>> ObjectsOf(Side side)
        {
            foreach (var kv in Objects())
                if (kv.Value.Owner == side) yield return kv;
        }

        public int NewUid() { return NextUid++; }

        // ---- derived --------------------------------------------------------------------------

        /// <summary>
        /// Whether a side may act at all. This replaces the JS accident where G.phase sat at 'end'
        /// for the whole AI turn and THAT was what made the board inert (spec 02 s4.4, spec 07
        /// s3.2). The AI now runs the real phase machine and input gating is explicit.
        /// </summary>
        public bool IsInteractive(Side side)
        {
            return !IsOver
                && Turn == side
                && (Phase == TurnPhase.Upkeep || Phase == TurnPhase.Draw || Phase == TurnPhase.Action);
        }

        // ---- clone ----------------------------------------------------------------------------

        /// <summary>
        /// Hand-written deep clone. Not reflection, not serialize-then-deserialize: AI search
        /// clones the state thousands of times per second, and a round-trip clone would both be
        /// far slower and make clone correctness depend on codec correctness.
        ///
        /// There is no object graph to fix up - board objects hold ids and catalog keys, never
        /// references to each other - so this stays purely mechanical.
        /// </summary>
        public GameState Clone()
        {
            var g = new GameState
            {
                NextUid = NextUid,
                Options = Options,
                Random = Random.Clone(),
                Turn = Turn,
                TurnNumber = TurnNumber,
                Phase = Phase,
                IsOver = IsOver,
                Outcome = Outcome,
                Pending = Pending,   // immutable by contract - safe to share
            };

            for (int i = 0; i < _cells.Length; i++)
                g._cells[i] = _cells[i] == null ? null : _cells[i].Clone();

            for (int i = 0; i < Players.Length; i++)
                g.Players[i] = Players[i].Clone();

            return g;
        }
    }
}
