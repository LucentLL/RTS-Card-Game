namespace SpawnRowDuel.Ai
{
    /// <summary>
    /// The difficulty knobs the JS AI never had (spec 07 s12: "the JS AI has NO difficulty
    /// scaling at all").
    ///
    /// Deliberately separate from RulesOptions. That struct is the PARITY register - flags that
    /// reproduce JS bugs and must all be off before we ship. This one is design surface: values a
    /// designer is meant to turn, forever. Mixing them would make "zero active flags" unreachable.
    ///
    /// Every default here is the JS's own behaviour, so `AiTuning.JsDefault` plays exactly like
    /// the browser build and any other profile is a deliberate departure.
    /// </summary>
    public struct AiTuning
    {
        /// <summary>The 0.6 roll: how often a well-funded face-down is worth cracking.</summary>
        public int FaceDownRollPercent;

        /// <summary>The 0.3 roll: how often the frailest structure is worth chipping.</summary>
        public int StructureRollPercent;

        /// <summary>`if(aiBuild) aiBuild` - the JS tries twice per turn.</summary>
        public int MaxBuildsPerTurn;

        /// <summary>aiUpgrade returns after its first success.</summary>
        public int MaxUpgradesPerTurn;

        /// <summary>The JS summon loop's `guard++ > 6` escape.</summary>
        public int MaxSummonsPerTurn;

        /// <summary>
        /// Structures the AI will raise before it stops STARTING new ones. Upgrades are not
        /// capped, because they replace a building in place and cost no extra square - so the
        /// AI still grows its economy after the cap, it just stops laying new foundations and
        /// starts fielding an army instead.
        ///
        /// Measured 2026-09-08: uncapped, the AI ended games with 10.8 structures standing,
        /// ~16 mana unspent and 5.6 cards still in hand, which is what "rarely plays monsters,
        /// I win while they have a full hand" looks like from the inside. A human opening is a
        /// base, two encampments, then creatures from turn three.
        /// </summary>
        public int MaxStructures;

        /// <summary>The JS lays at most one trap per turn, and only from the first it holds.</summary>
        public int MaxTrapsPerTurn;

        /// <summary>One raze and one burn per turn, each at most once.</summary>
        public int MaxSpellsPerTurn;

        public static AiTuning JsDefault
        {
            get
            {
                var t = new AiTuning();
                t.FaceDownRollPercent = 60;
                t.StructureRollPercent = 30;
                t.MaxBuildsPerTurn = 2;
                t.MaxUpgradesPerTurn = 1;
                t.MaxSummonsPerTurn = 7;
                t.MaxStructures = 5;
                t.MaxTrapsPerTurn = 1;
                t.MaxSpellsPerTurn = 1;
                return t;
            }
        }
    }
}
