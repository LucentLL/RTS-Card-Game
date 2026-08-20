using System.Collections.Generic;
using NUnit.Framework;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// The M5 "done when": NewMatch produces an empty 35-cell board, each side's back pool sized
    /// CCS[cc].wk and READY, an opening hand of 4, and a reproducible hash for a fixed seed.
    /// </summary>
    public class MatchSetupTests
    {
        private static GameState Fresh(ulong seed)
        {
            return MatchSetup.NewMatch(TestData.Catalog,
                new CommanderId("fire"), new CommanderId("water"), seed, RulesOptions.JsParity);
        }

        [Test]
        public void NewMatch_BoardIsCompletelyEmpty()
        {
            var s = Fresh(1);
            int occupied = 0;
            foreach (var kv in s.Objects()) occupied++;
            Assert.AreEqual(0, occupied, "no command centre card is placed - the board starts as 35 nulls");
        }

        [Test]
        public void NewMatch_TurnMachineOpensAtUpkeep_PlayerFirst()
        {
            var s = Fresh(1);
            Assert.AreEqual(Side.You, s.Turn);
            Assert.AreEqual(1, s.TurnNumber);
            Assert.AreEqual(TurnPhase.Upkeep, s.Phase);
            Assert.IsFalse(s.IsOver);
            Assert.AreEqual(MatchOutcome.InProgress, s.Outcome);
            Assert.IsNull(s.Pending);
        }

        [Test]
        public void NewMatch_PlayersStartWithLifeManaAndColors()
        {
            var s = Fresh(1);
            Assert.AreEqual(10000, s.P(Side.You).Life);
            Assert.AreEqual(10000, s.P(Side.Foe).Life);
            Assert.AreEqual(0, s.P(Side.You).Mana);
            Assert.AreEqual(0, s.P(Side.Foe).Mana);
            Assert.AreEqual(Element.Fire, s.P(Side.You).PrimaryColor);
            Assert.AreEqual(Element.Water, s.P(Side.Foe).PrimaryColor);
        }

        [Test]
        public void NewMatch_BackPoolsAreSizedByCommander_AndReady()
        {
            var s = Fresh(1);

            // fire wk=2, water wk=3
            var you = s.P(Side.You);
            var foe = s.P(Side.Foe);

            Assert.AreEqual(2, you.Workers[(int)WorkerZone.Back].Count, "fire commander fields 2");
            Assert.AreEqual(3, foe.Workers[(int)WorkerZone.Back].Count, "water commander fields 3");
            Assert.AreEqual(0, you.Workers[(int)WorkerZone.Front].Count);
            Assert.AreEqual(0, you.Workers[(int)WorkerZone.Center].Count);

            // step 7: the opening workforce is settled and harvest-ready on turn 1
            foreach (var w in you.Workers[(int)WorkerZone.Back].Members)
            {
                Assert.IsFalse(w.Sick, "opening workers are NOT summoning-sick");
                Assert.IsFalse(w.Tapped);
                Assert.IsTrue(w.IsWorker);
                Assert.AreEqual(0, w.Attack);
                Assert.AreEqual(1000, w.Hp);
            }
        }

        [Test]
        public void NewMatch_DualCommander_GetsTheRoundedWorkforce()
        {
            var s = MatchSetup.NewMatch(TestData.Catalog,
                new CommanderId("fire_water"), new CommanderId("earth"), 5, RulesOptions.JsParity);
            Assert.AreEqual(3, s.P(Side.You).Workers[(int)WorkerZone.Back].Count,
                "fire(2)+water(3) rounds half-UP to 3");
        }

        [Test]
        public void NewMatch_DealsFourAndLeavesThirtySix()
        {
            var s = Fresh(1);
            Assert.AreEqual(4, s.P(Side.You).Hand.Count);
            Assert.AreEqual(4, s.P(Side.Foe).Hand.Count);
            Assert.AreEqual(36, s.P(Side.You).Deck.Count);
            Assert.AreEqual(36, s.P(Side.Foe).Deck.Count);
        }

        [Test]
        public void DeckOf_BuildsTheCanonicalComposition()
        {
            var cat = TestData.Catalog;
            var s = Fresh(7);

            // Solo commander: 28 creatures of the colour + 12 neutral spells. Shuffling does not
            // change the composition, so count through the catalog.
            int creatures = 0, spells = 0;
            var all = new List<HandCard>();
            all.AddRange(s.P(Side.You).Deck);
            all.AddRange(s.P(Side.You).Hand);

            foreach (var card in all)
            {
                CreatureCard c;
                SpellCard sp;
                if (cat.TryCreature(card.Id, out c))
                {
                    creatures++;
                    Assert.AreEqual(Element.Fire, card.Color, "solo fire deck creatures are fire");
                }
                else if (cat.TrySpell(card.Id, out sp))
                {
                    spells++;
                    Assert.AreEqual(Element.None, card.Color, "spells are neutral");
                }
                else Assert.Fail("unknown card in deck: " + card.Id);
            }

            Assert.AreEqual(40, all.Count);
            Assert.AreEqual(28, creatures);
            Assert.AreEqual(12, spells);
        }

        [Test]
        public void DeckOf_DualCommander_SplitsPerColour()
        {
            var cat = TestData.Catalog;
            var s = MatchSetup.NewMatch(TestData.Catalog,
                new CommanderId("fire_water"), new CommanderId("earth"), 11, RulesOptions.JsParity);

            int fire = 0, water = 0, spells = 0;
            var all = new List<HandCard>();
            all.AddRange(s.P(Side.You).Deck);
            all.AddRange(s.P(Side.You).Hand);

            foreach (var card in all)
            {
                if (card.Color == Element.Fire) fire++;
                else if (card.Color == Element.Water) water++;
                else if (card.Color == Element.None) spells++;
            }

            Assert.AreEqual(40, all.Count);
            Assert.AreEqual(14, fire, "round(28/2) fire creatures");
            Assert.AreEqual(14, water, "round(28/2) water creatures");
            Assert.AreEqual(12, spells, "2 x round(12/2) neutral spells");
        }

        [Test]
        public void RoundDiv_ReproducesJsMathRound()
        {
            Assert.AreEqual(28, DeckFactory.RoundDiv(28, 1));
            Assert.AreEqual(14, DeckFactory.RoundDiv(28, 2));
            Assert.AreEqual(9, DeckFactory.RoundDiv(28, 3), "28/3 = 9.33 rounds DOWN");
            Assert.AreEqual(12, DeckFactory.RoundDiv(12, 1));
            Assert.AreEqual(6, DeckFactory.RoundDiv(12, 2));
            Assert.AreEqual(4, DeckFactory.RoundDiv(12, 3), "12/3 = 4 exactly");
            Assert.AreEqual(3, DeckFactory.RoundDiv(5, 2), "2.5 rounds half-UP, not banker's");
        }

        [Test]
        public void NewMatch_SameSeed_IsByteIdentical()
        {
            var a = Fresh(123456789UL);
            var b = Fresh(123456789UL);
            Assert.AreEqual(StateCodec.ToCanonicalJson(a), StateCodec.ToCanonicalJson(b));
            Assert.AreEqual(StateCodec.Hash(a), StateCodec.Hash(b));
        }

        [Test]
        public void NewMatch_DifferentSeeds_Diverge()
        {
            var a = Fresh(1);
            var b = Fresh(2);
            Assert.AreNotEqual(StateCodec.Hash(a), StateCodec.Hash(b),
                "different seeds must shuffle different decks");
        }

        [Test]
        public void ExplicitDecks_BypassTheFactory()
        {
            var cat = TestData.Catalog;
            var deck = new List<HandCard>();
            for (int i = 0; i < 10; i++) deck.Add(new HandCard(new CardId("Sparkimp"), Element.Fire));

            var s = MatchSetup.NewMatch(cat, new CommanderId("fire"), new CommanderId("water"),
                new List<HandCard>(deck), new List<HandCard>(deck), 3, RulesOptions.JsParity);

            Assert.AreEqual(4, s.P(Side.You).Hand.Count);
            Assert.AreEqual(6, s.P(Side.You).Deck.Count);
            foreach (var c in s.P(Side.You).Hand) Assert.AreEqual("Sparkimp", c.Id.Value);
        }

        [Test]
        public void DrawCard_PopsFromTheEnd_AndEmptyDeckIsNotALoss()
        {
            var cat = TestData.Catalog;
            var deck = new List<HandCard>();
            deck.Add(new HandCard(new CardId("Sparkimp"), Element.Fire));   // bottom
            deck.Add(new HandCard(new CardId("Magmaw"), Element.Fire));     // top - drawn first

            var s = MatchSetup.NewMatch(cat, new CommanderId("fire"), new CommanderId("water"),
                deck, new List<HandCard>(), 3, RulesOptions.JsParity);

            // dealOpening drew both (top first), then two empty draws did nothing
            Assert.AreEqual(2, s.P(Side.You).Hand.Count);
            Assert.AreEqual("Magmaw", s.P(Side.You).Hand[0].Id.Value, "the END of the list is the top");
            Assert.AreEqual(0, s.P(Side.You).Deck.Count);
            Assert.IsFalse(s.IsOver, "there is no deck-out loss");

            Assert.AreEqual(0, s.P(Side.Foe).Hand.Count, "an empty deck deals an empty hand");
        }

        [Test]
        public void Pending_PerturbsTheHash_AndCloneShares()
        {
            var s = Fresh(42);
            ulong idle = StateCodec.Hash(s);

            s.Pending = new BlockerRequest(Side.Foe, attackerId: 7, declarationIndex: 0,
                declarationCount: 1,
                eligible: new[] { UnitRef.Cell(new CellRef(RowKey.FoeFront, 2), 9) });

            ulong parked = StateCodec.Hash(s);
            Assert.AreNotEqual(idle, parked, "a parked choice must never hash like a free engine");

            var clone = s.Clone();
            Assert.AreEqual(parked, StateCodec.Hash(clone));

            s.Pending = null;
            Assert.AreEqual(idle, StateCodec.Hash(s));
            Assert.AreEqual(parked, StateCodec.Hash(clone), "the clone keeps its own pending view");
        }

        [Test]
        public void Clone_IsFullyIndependent_AfterNewMatch()
        {
            var s = Fresh(99);
            var clone = s.Clone();
            Assert.AreEqual(StateCodec.Hash(s), StateCodec.Hash(clone));

            // mutate the original across every subsystem NewMatch touched
            s.P(Side.You).Mana = 50;
            s.P(Side.You).Hand.RemoveAt(0);
            s.P(Side.You).Workers[(int)WorkerZone.Back].Members[0].Tapped = true;
            s.Random.NextInt(10);
            s.TurnNumber = 5;

            Assert.AreNotEqual(StateCodec.Hash(s), StateCodec.Hash(clone));

            var reclone = Fresh(99);
            Assert.AreEqual(StateCodec.Hash(reclone), StateCodec.Hash(clone),
                "the clone still matches a pristine rebuild - nothing leaked through shared references");
        }
    }
}
