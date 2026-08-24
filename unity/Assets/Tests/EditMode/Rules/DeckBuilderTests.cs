using System.Collections.Generic;
using NUnit.Framework;
using SpawnRowDuel.View.Decks;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// What makes a built deck legal, and what survives a save.
    ///
    /// The card KEY is the thing worth pinning. It carries the element as well as the name, and
    /// the JS learned why the hard way: a dual leader can reach the same card name through two
    /// colours, and a deck stored by name alone cannot say which one it meant.
    /// </summary>
    public class DeckBuilderTests
    {
        static ICardCatalog Cat { get { return TestData.Catalog; } }

        static SavedDeck Filled(string commander, int total)
        {
            var deck = new SavedDeck { Name = "test", Commander = new CommanderId(commander) };
            var pool = DeckRules.PoolFor(Cat, Cat.Commander(deck.Commander));
            int added = 0;
            foreach (var key in pool)
            {
                int take = System.Math.Min(DeckRules.MaxCopies, total - added);
                if (take <= 0) break;
                deck.Cards[key] = take;
                added += take;
            }
            return deck;
        }

        [Test]
        public void Pool_IsCreaturesOfYourColoursPlusNeutralSpells_AndNoStructures()
        {
            var cc = Cat.Commander(new CommanderId("fire_water"));
            var pool = DeckRules.PoolFor(Cat, cc);

            int fire = 0, water = 0, neutral = 0;
            foreach (var key in pool)
            {
                Element el; string name;
                Assert.IsTrue(DeckRules.Split(key, out el, out name), key);
                if (el == Element.Fire) fire++;
                else if (el == Element.Water) water++;
                else if (el == Element.None) neutral++;
                else Assert.Fail("a " + el + " card is off-colour for a fire/water compact");

                StructureDef sd = null;
                foreach (var s in Cat.Structures) if (s.ExportKey == name) sd = s;
                Assert.IsNull(sd, name + " is a structure - those are BUILT, never drawn");
            }

            Assert.AreEqual(8, fire, "eight creatures per element pool");
            Assert.AreEqual(8, water);
            Assert.AreEqual(Cat.Spells.Count, neutral, "every spell and trap is neutral and deckable");
        }

        [Test]
        public void Errors_ComeInOrderAndTheFirstOneIsWhatBlocks()
        {
            var deck = new SavedDeck { Name = "x", Commander = new CommanderId("nope") };
            Assert.AreEqual("Unknown leader.", DeckRules.FirstError(Cat, deck));

            deck.Commander = new CommanderId("fire");
            var water = DeckRules.Key(Element.Water, Cat.PoolOf(Element.Water)[0].Name);
            deck.Cards[water] = 1;
            StringAssert.EndsWith("is off-colour.", DeckRules.FirstError(Cat, deck));

            deck.Cards.Clear();
            var fire = DeckRules.Key(Element.Fire, Cat.PoolOf(Element.Fire)[0].Name);
            deck.Cards[fire] = 4;
            StringAssert.EndsWith("must be 1–3.", DeckRules.FirstError(Cat, deck));

            deck.Cards[fire] = 3;
            StringAssert.StartsWith("Need exactly 40 cards", DeckRules.FirstError(Cat, deck));
        }

        [Test]
        public void FortyCardsOfYourOwnColoursIsLegal()
        {
            var deck = Filled("fire", DeckRules.Size);
            Assert.AreEqual(DeckRules.Size, deck.Total);
            Assert.IsNull(DeckRules.FirstError(Cat, deck));
            Assert.IsTrue(DeckRules.IsLegal(Cat, deck));
        }

        [Test]
        public void DrawPile_HasOneEntryPerCopyAndIsShuffledDeterministically()
        {
            var deck = Filled("fire", DeckRules.Size);

            var a = DeckRules.ToDrawPile(Cat, deck, new Pcg32(7));
            var b = DeckRules.ToDrawPile(Cat, deck, new Pcg32(7));
            var c = DeckRules.ToDrawPile(Cat, deck, new Pcg32(8));

            Assert.AreEqual(DeckRules.Size, a.Count);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i].Id.Value, b[i].Id.Value, "same seed, same deal");

            bool differs = false;
            for (int i = 0; i < a.Count; i++) if (a[i].Id.Value != c[i].Id.Value) differs = true;
            Assert.IsTrue(differs, "a different seed deals differently");
        }

        [Test]
        public void Storage_RoundTripsAndDropsCardsThatNoLongerExist()
        {
            var deck = Filled("fire", DeckRules.Size);
            var list = new List<SavedDeck> { deck };

            var back = DeckRules.ReadAll(DeckRules.WriteAll(list), Cat);
            Assert.AreEqual(1, back.Count);
            Assert.AreEqual(deck.Name, back[0].Name);
            Assert.AreEqual(deck.Commander.Value, back[0].Commander.Value);
            Assert.AreEqual(deck.Total, back[0].Total);

            // a retired card, and one that was never this leader's to hold
            var json = DeckRules.WriteAll(list)
                .Replace("\"cards\":{", "\"cards\":{\"fire|Nonesuch\":3,\"water|Rippler\":2,");
            var cleaned = DeckRules.ReadAll(json, Cat);
            Assert.AreEqual(1, cleaned.Count);
            foreach (var kv in cleaned[0].Cards)
            {
                Element el; string name;
                DeckRules.Split(kv.Key, out el, out name);
                Assert.AreNotEqual("Nonesuch", name, "a card the registry no longer knows is dropped");
                Assert.AreNotEqual(Element.Water, el, "and so is one that is off-colour for the leader");
            }
        }

        [Test]
        public void Storage_KeepsAtMostFiveDecks()
        {
            var list = new List<SavedDeck>();
            for (int i = 0; i < 9; i++)
            {
                var d = Filled("fire", DeckRules.Size);
                d.Name = "deck " + i;
                list.Add(d);
            }
            Assert.AreEqual(DeckRules.MaxDecks, DeckRules.ReadAll(DeckRules.WriteAll(list), Cat).Count);
        }

        [Test]
        public void Keys_CarryTheElementSoADualLeaderCanTellTwoCardsApart()
        {
            var fire = DeckRules.Key(Element.Fire, "Longhouse");
            var water = DeckRules.Key(Element.Water, "Longhouse");
            Assert.AreNotEqual(fire, water);
            Assert.AreEqual("fire|Longhouse", fire);
            Assert.AreEqual("neutral|Ember Bolt", DeckRules.Key(Element.None, "Ember Bolt"));

            Element el; string name;
            Assert.IsTrue(DeckRules.Split("neutral|Ember Bolt", out el, out name));
            Assert.AreEqual(Element.None, el);
            Assert.AreEqual("Ember Bolt", name);
            Assert.IsFalse(DeckRules.Split("nope", out el, out name));
        }
    }
}
