using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// startGame (09_game_start.js:1-19), spec 01 s12, in the JS's exact step order. Turn 1 does
    /// NOT run the BeginTurn pipeline: the opening turn is entered directly at Upkeep with the
    /// workforce already settled and ready, so turn 1 can harvest immediately.
    /// </summary>
    public static class MatchSetup
    {
        public const int OpeningHandSize = 4;

        /// <summary>Default decks: deckOf(commander.Colors) drawn from the match RNG.</summary>
        public static GameState NewMatch(ICardCatalog cat, CommanderId you, CommanderId foe,
                                         ulong seed, RulesOptions options)
        {
            return NewMatch(cat, you, foe, null, null, seed, options);
        }

        public static GameState NewMatch(ICardCatalog cat, CommanderId you, CommanderId foe,
                                         List<HandCard> youDeck, List<HandCard> foeDeck,
                                         ulong seed, RulesOptions options)
        {
            return NewMatch(cat, you, foe, youDeck, foeDeck, seed, options, Side.You);
        }

        /// <summary>
        /// The same, saying who moves FIRST.
        ///
        /// It used to be You, always, which in a duel means the HOST always opens - a real edge,
        /// handed to whoever happened to create the room. The side is a parameter rather than a
        /// roll taken here because the match RNG is a contract: it draws You's deck before Foe's
        /// and every committed trace depends on it, so spending a number on a coin flip would
        /// move every card in every existing game. The caller decides, and in a duel it decides
        /// from the shared seed (<see cref="MatchConfig.FirstMoveFrom"/>) so both peers agree
        /// without a word on the wire.
        /// </summary>
        public static GameState NewMatch(ICardCatalog cat, CommanderId you, CommanderId foe,
                                         List<HandCard> youDeck, List<HandCard> foeDeck,
                                         ulong seed, RulesOptions options, Side first)
        {
            var cy = cat.Commander(you);
            var cf = cat.Commander(foe);

            var s = new GameState();
            s.Options = options;
            s.Random = new Pcg32(seed);

            // steps 2-3: reset both player records
            SetupPlayer(s.P(Side.You), cy);
            SetupPlayer(s.P(Side.Foe), cf);

            // step 4: the turn machine. Board starts empty (35 nulls) - no command centre card.
            s.Turn = first;
            s.TurnNumber = 1;
            s.Phase = TurnPhase.Upkeep;
            s.IsOver = false;
            s.Outcome = MatchOutcome.InProgress;

            // steps 6-7: pools materialise to CCS[cc].wk back-row workers, then ready up -
            // the opening workforce is NOT sick, so turn 1's harvest works.
            WorkerMath.Resync(s, Side.You, cat);
            WorkerMath.Resync(s, Side.Foe, cat);
            s.P(Side.You).ReadyWorkers();
            s.P(Side.Foe).ReadyWorkers();

            // step 8: decks. RNG draw order - you before foe - is part of the contract.
            s.P(Side.You).Deck.AddRange(youDeck ?? DeckFactory.DeckOf(cat, cy.Colors, s.Random));
            s.P(Side.Foe).Deck.AddRange(foeDeck ?? DeckFactory.DeckOf(cat, cf.Colors, s.Random));

            // step 9: dealOpening - clear hand, draw 4 (11_deck_builder.js:247-249)
            DealOpening(s, Side.You);
            DealOpening(s, Side.Foe);

            return s;
        }

        private static void SetupPlayer(PlayerState p, CommanderDef cc)
        {
            p.PrimaryColor = cc.Colors[0];
            p.Commander = cc.Id;
            p.Life = cc.Hp;
            p.Mana = 0;
            p.Hand.Clear();
            p.Deck.Clear();
            p.Grave.Clear();
            for (int i = 0; i < p.Workers.Length; i++) p.Workers[i].Members.Clear();
            for (int i = 0; i < p.UpkeepPaid.Length; i++) p.UpkeepPaid[i] = 0;
        }

        public static void DealOpening(GameState s, Side owner)
        {
            s.P(owner).Hand.Clear();
            for (int i = 0; i < OpeningHandSize; i++) DrawCard(s, owner, null);
        }

        /// <summary>
        /// drawCard: pops from the END of the deck. An empty deck simply yields nothing - there
        /// is no deck-out loss in this game (spec 02 s4.2).
        /// </summary>
        public static bool DrawCard(GameState s, Side owner, EventSink events)
        {
            var p = s.P(owner);
            if (p.Deck.Count == 0) return false;

            var card = p.Deck[p.Deck.Count - 1];
            p.Deck.RemoveAt(p.Deck.Count - 1);
            p.Hand.Add(card);
            if (events != null) events.Add(new CardDrawn(owner, card.Id));
            return true;
        }
    }
}
