using System.Collections.Generic;
using NUnit.Framework;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// M10: a card that has BEEN a creature carries its live statline off the board and back on
    /// again (spec 06 s4.1 handcardFromCreature, s4.6 toGrave). Rebuilding from the registry
    /// instead silently un-hatches and un-buffs creatures - the debt M8 recorded and this closes.
    /// </summary>
    public class CardSnapshotTests
    {
        static DuelEngine Engine(out GameState s)
        {
            s = MatchSetup.NewMatch(TestData.Catalog,
                new CommanderId("forest"), new CommanderId("water"), 53, RulesOptions.JsParity);
            return new DuelEngine(s, TestData.Catalog);
        }

        static void ToAction(DuelEngine e, GameState s)
        {
            Assert.IsTrue(e.Apply(new HarvestCommand(Side.You)).Applied);
            Assert.IsTrue(e.Apply(new DrawForTurnCommand(Side.You)).Applied);
        }

        static CreatureUnit Place(GameState s, Side side, string name, RowKey row, int col)
        {
            var c = UnitFactory.MakeCreature(s, side,
                TestData.Catalog.Creature(new CardId(name)), Element.None);
            s.Put(new CellRef(row, col), c);
            return c;
        }

        [Test]
        public void ABouncedHatchedCreature_ComesBackHatched_AndReSummonsAtTheCocoonsCost()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            var pod = Place(s, Side.Foe, "Sap Pod", RowKey.FoeFront, 3);      // ◆2, 0/1500
            for (int i = 0; i < 3; i++)
                KeywordEngine.UpkeepTick(s, Side.Foe, TestData.Catalog, new EventSink());
            Assert.AreEqual("Canopy Beast", pod.Name, "fixture: it hatched");

            s.P(Side.You).Hand.Add(new HandCard(new CardId("Riptide"), Element.None));
            int idx = s.P(Side.You).Hand.Count - 1;
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Cast,
                new CellRef(RowKey.FoeFront, 3))).Applied);

            var card = s.P(Side.Foe).Hand[s.P(Side.Foe).Hand.Count - 1];
            Assert.AreEqual("Canopy Beast", card.Snapshot.Name, "not the registry's Sap Pod");
            Assert.AreEqual(2500, card.Snapshot.Attack);
            Assert.AreEqual(2000, card.Snapshot.Health);
            Assert.AreEqual(Keyword.None, card.Snapshot.Keyword, "hatching cleared it for good");
            Assert.AreEqual(2, card.Snapshot.Cost, "still costs what the cocoon cost");
            Assert.AreEqual(new CardId("Sap Pod"), card.Id, "the catalog origin is preserved");

            // re-summon it (from our own hand, to keep the test to one turn)
            s.P(Side.You).Hand.Add(card);
            int again = s.P(Side.You).Hand.Count - 1;
            int mana = s.P(Side.You).Mana;
            var at = new CellRef(RowKey.YouFront, 2);
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, again, PlayMode.Summon, at)).Applied);

            var cr = (CreatureUnit)s.At(at);
            Assert.AreEqual("Canopy Beast", cr.Name);
            Assert.AreEqual(2500, cr.Attack);
            Assert.AreEqual(2000, cr.Hp);
            Assert.AreEqual(Keyword.None, cr.Keyword, "it does not restart its chrysalis");
            Assert.AreEqual(0, cr.ChrysalisCount, "and the swell counter does NOT come home");
            Assert.AreEqual(mana - 2, s.P(Side.You).Mana, "paid the snapshot's cost");
        }

        [Test]
        public void TheReliquary_ReturnsTheStatlineTheCreatureDiedWith()
        {
            GameState s;
            var e = Engine(out s);

            var c = Place(s, Side.You, "Mistling", RowKey.YouFront, 3);       // 500/1000
            c.Attack += 500;                                                  // Overgrowth hardening
            c.MaxHp += 1000;
            c.Hp = 1;
            c.Hp = 0;
            DeathSweep.Cleanup(s, TestData.Catalog, new EventSink());

            Assert.IsTrue(StructureUpkeep.ReviveFromGrave(s, Side.You, new EventSink()));

            var card = s.P(Side.You).Hand[s.P(Side.You).Hand.Count - 1];
            Assert.IsTrue(card.Snapshot.HasValue);
            Assert.AreEqual(1000, card.Snapshot.Attack, "the permanent buff survived the grave");
            Assert.AreEqual(2000, card.Snapshot.Health, "max hp, not the 0 it died on");
        }

        [Test]
        public void TheReliquary_NeverReturnsTokensOrWorkers()
        {
            GameState s;
            var e = Engine(out s);

            var tok = UnitFactory.MakeToken(s, Side.You, "Shade", 500, 500, Element.Dark);
            s.Put(new CellRef(RowKey.YouFront, 1), tok);
            var worker = UnitFactory.MakeCreature(s, Side.You,
                TestData.Catalog.WorkerTemplate, Element.None);
            worker.IsWorker = true;
            s.Put(new CellRef(RowKey.YouFront, 2), worker);

            tok.Hp = 0;
            worker.Hp = 0;
            DeathSweep.Cleanup(s, TestData.Catalog, new EventSink());
            Assert.AreEqual(2, s.P(Side.You).Grave.Count);

            Assert.IsFalse(StructureUpkeep.ReviveFromGrave(s, Side.You, new EventSink()),
                "both grave records are flagged out of reach");
        }

        [Test]
        public void ASnapshotCardSetFaceDown_FlipsBackWithItsCarriedStats()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            var snap = new CreatureSnapshot("Canopy Beast", 2500, 2000, 2, 1, false, false,
                Keyword.None, 0, 0, 2, 0, 0, CardId.None, Tribe.None, Subtype.None);
            s.P(Side.You).Hand.Add(new HandCard(new CardId("Sap Pod"), Element.Forest, snap));
            int idx = s.P(Side.You).Hand.Count - 1;

            var at = new CellRef(RowKey.YouBack, 4);
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Set, at)).Applied);

            var ch = (ChargeUnit)s.At(at);
            Assert.IsTrue(ch.Snap.HasValue, "place() snapshots the whole card, history included");
            Assert.AreEqual(2500, ch.Card.Attack, "and the face it will show matches");

            ch.Invested = 2;                                       // funded
            ch.SetTurn = 0;                                        // set earlier: battle-ready
            Assert.IsTrue(e.Apply(new FlipChargeCommand(Side.You, at, ch.Id)).Applied);

            var cr = (CreatureUnit)s.At(at);
            Assert.AreEqual("Canopy Beast", cr.Name);
            Assert.AreEqual(2500, cr.Attack);
            Assert.AreEqual(2000, cr.MaxHp);
        }

        [Test]
        public void UndertowBounce_CarriesTheStatlineToo()
        {
            GameState s;
            var e = Engine(out s);

            var warden = Place(s, Side.Foe, "Undertow", RowKey.FoeFront, 3);
            var raider = Place(s, Side.You, "Maelstrom", RowKey.FoeFront, 2);   // cost 5
            raider.Attack += 500;
            raider.MaxHp += 1000;
            raider.Hp = 300;

            var attackers = new List<CreatureUnit> { raider };
            var defenders = new List<CreatureUnit> { warden };
            KeywordEngine.PreCombat(s, attackers, defenders, TestData.Catalog, new EventSink());

            Assert.AreEqual(0, attackers.Count, "hurled out of the fight before any damage");
            var card = s.P(Side.You).Hand[s.P(Side.You).Hand.Count - 1];
            Assert.AreEqual(2000, card.Snapshot.Attack);
            Assert.AreEqual(3500, card.Snapshot.Health);
        }

        [Test]
        public void AnOrdinaryDeckCard_CarriesNoSnapshot_AndStillResolvesThroughTheCatalog()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            s.P(Side.You).Hand.Add(new HandCard(new CardId("Sap Pod"), Element.Forest));
            int idx = s.P(Side.You).Hand.Count - 1;
            Assert.IsFalse(s.P(Side.You).Hand[idx].Snapshot.HasValue);

            var at = new CellRef(RowKey.YouFront, 2);
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Summon, at)).Applied);

            var cr = (CreatureUnit)s.At(at);
            Assert.AreEqual("Sap Pod", cr.Name);
            Assert.AreEqual(Keyword.Chrysalis, cr.Keyword);
            Assert.AreEqual(1500, cr.MaxHp);
        }

        [Test]
        public void ACarriedStatline_PerturbsTheStateHash()
        {
            GameState s;
            var e = Engine(out s);
            var plain = s.Clone();

            var snap = new CreatureSnapshot("Canopy Beast", 2500, 2000, 2, 1, false, false,
                Keyword.None, 0, 0, 2, 0, 0, CardId.None, Tribe.None, Subtype.None);
            s.P(Side.You).Hand.Add(new HandCard(new CardId("Sap Pod"), Element.Forest, snap));
            plain.P(Side.You).Hand.Add(new HandCard(new CardId("Sap Pod"), Element.Forest));

            Assert.AreNotEqual(StateCodec.Hash(plain), StateCodec.Hash(s),
                "a card with history must never hash equal to a fresh copy of it");
        }
    }
}
