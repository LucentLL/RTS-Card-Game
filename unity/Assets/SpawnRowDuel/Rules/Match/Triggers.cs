namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// What happens to the board the instant a creature arrives on it - the two lines that follow
    /// every `arr[slot] = cr` in the JS (13_input.js:199, :218; 14_spells_traps.js:124).
    ///
    /// Kept as one named seam rather than inlined at four call sites because the ORDER is a rule:
    /// the newcomer's own ENTER keyword resolves first (a Ward gets its Lumen), and only then may
    /// the defender's summon trap drag it down - so a warder that is trapped away still leaves
    /// its token behind, exactly as the JS does.
    ///
    /// These used to be assignable delegates awaiting M10. They are direct calls now: a mutable
    /// static hook is a hazard the moment two matches exist in one process, and there is no
    /// longer anything to defer.
    /// </summary>
    public static class Triggers
    {
        /// <summary>
        /// A face-down card FLIPPED up. It gets its ENTER keyword and nothing else - immunity to
        /// summon traps is the mechanical payoff of having set it (spec 04 s13.3).
        /// </summary>
        public static void CreatureFlipped(GameState s, CreatureUnit cr, Side owner,
                                           ICardCatalog cat, EventSink ev)
        {
            KeywordEngine.OnEnter(s, cr, owner, cat, ev);
        }

        /// <summary>
        /// A creature SUMMONED from hand (including the play-on-top line). ENTER first, then the
        /// defender is offered their armed summon traps - which parks a response window and
        /// suspends the command until it is answered.
        /// </summary>
        public static void CreatureSummoned(GameState s, CreatureUnit cr, CellRef at, Side owner,
                                            ICardCatalog cat, EventSink ev)
        {
            KeywordEngine.OnEnter(s, cr, owner, cat, ev);
            Traps.OfferSummonWindow(s, cr, at, owner, cat, ev);
        }
    }
}
