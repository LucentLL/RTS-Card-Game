namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// The parity flag register.
    ///
    /// Every flag defaults to JS behaviour, INCLUDING the JS bugs. That is the point: while the
    /// differential harness runs against the live JS build, any intentional behaviour change is
    /// indistinguishable from a port defect, so improvements go behind a flag and stay off until
    /// the harness is green. The ship gate is that this struct is back to all-defaults with the
    /// flags deleted - a flag still being read in a release build means an unresolved divergence.
    ///
    /// Options are frozen at match creation and are part of the state hash: two engines that
    /// disagree about a flag must not be able to produce the same hash and look equivalent.
    /// </summary>
    public struct RulesOptions
    {
        /// <summary>
        /// JS drops the card's own colour when setting face-down, so a flipped off-colour creature
        /// inherits the player's element (spec 04 s13.2). false == reproduce that.
        /// </summary>
        public bool FaceDownKeepsColor;

        /// <summary>
        /// JS picks the toughest gang-blocker as the damage absorber. Dumping on the weakest is
        /// the better play, but it changes AI-visible outcomes, so it waits for M12.
        /// </summary>
        public bool AbsorberIsWeakestBlocker;

        /// <summary>
        /// JS takes a 0.6-probability face-down roll BEFORE checking for a guaranteed kill, so it
        /// sometimes passes up lethal. Reordering changes self-play traces.
        /// </summary>
        public bool AiTakesGuaranteedKillFirst;

        /// <summary>JS `raze` targets whatever structure it happened to find last.</summary>
        public bool AiRazeUsesHeuristic;

        /// <summary>
        /// Solo doHarvest forgives a purely structural shortfall; the MP host validator does not.
        /// Which one is canonical is an open decision.
        /// </summary>
        public bool AiUsesStructuralRemainderFallback;

        /// <summary>JS retreat targeting ignores real row adjacency.</summary>
        public bool AiRetreatsOnRealAdjacency;

        /// <summary>JS AI never casts chain/bounce and never sets creatures face-down.</summary>
        public bool AiUsesFullSpellSet;

        /// <summary>
        /// flip()'s structure branch returns before syncWorkers, so a flipped structure's support
        /// does not register until the next resync (spec 04 s14.2 BUG). false == reproduce that.
        /// </summary>
        public bool FlipStructureResyncsWorkers;

        /// <summary>
        /// A structure played FROM HAND into your own rows skips the placeRowOK worker-support
        /// gate the build menu enforces (spec 04 s9.2 INCONSISTENT). Unreachable today - no
        /// structure card is deckable - but place() supports it. false == JS-faithful skip.
        /// </summary>
        public bool EnforcePlaceRowOkFromHand;

        /// <summary>All-defaults: byte-for-byte JS behaviour. This is the shipping target.</summary>
        public static RulesOptions JsParity { get { return default(RulesOptions); } }

        /// <summary>
        /// Folded into the state hash. Any flag that is on must perturb the hash, otherwise two
        /// engines configured differently could compare as equal.
        /// </summary>
        public int FlagBits
        {
            get
            {
                int b = 0;
                if (FaceDownKeepsColor) b |= 1 << 0;
                if (AbsorberIsWeakestBlocker) b |= 1 << 1;
                if (AiTakesGuaranteedKillFirst) b |= 1 << 2;
                if (AiRazeUsesHeuristic) b |= 1 << 3;
                if (AiUsesStructuralRemainderFallback) b |= 1 << 4;
                if (AiRetreatsOnRealAdjacency) b |= 1 << 5;
                if (AiUsesFullSpellSet) b |= 1 << 6;
                if (FlipStructureResyncsWorkers) b |= 1 << 7;
                if (EnforcePlaceRowOkFromHand) b |= 1 << 8;
                return b;
            }
        }

        /// <summary>
        /// The exact inverse of <see cref="FlagBits"/>. Netcode needs it: two peers must build
        /// engines configured identically, and the handshake sends the bits rather than nine
        /// booleans. A bit this build does not know about is REFUSED rather than ignored - a
        /// peer running a newer flag set is a peer whose rules differ, and silently dropping the
        /// difference is how a desync becomes mysterious.
        /// </summary>
        public const int KnownFlagMask = (1 << 9) - 1;

        public static bool TryFromFlagBits(int bits, out RulesOptions options)
        {
            options = default(RulesOptions);
            if ((bits & ~KnownFlagMask) != 0) return false;

            options.FaceDownKeepsColor = (bits & (1 << 0)) != 0;
            options.AbsorberIsWeakestBlocker = (bits & (1 << 1)) != 0;
            options.AiTakesGuaranteedKillFirst = (bits & (1 << 2)) != 0;
            options.AiRazeUsesHeuristic = (bits & (1 << 3)) != 0;
            options.AiUsesStructuralRemainderFallback = (bits & (1 << 4)) != 0;
            options.AiRetreatsOnRealAdjacency = (bits & (1 << 5)) != 0;
            options.AiUsesFullSpellSet = (bits & (1 << 6)) != 0;
            options.FlipStructureResyncsWorkers = (bits & (1 << 7)) != 0;
            options.EnforcePlaceRowOkFromHand = (bits & (1 << 8)) != 0;
            return true;
        }

        /// <summary>How many divergences are still outstanding. Must be 0 to ship.</summary>
        public int ActiveFlagCount
        {
            get
            {
                int n = 0, b = FlagBits;
                while (b != 0) { n += b & 1; b >>= 1; }
                return n;
            }
        }
    }
}
