using System.Collections.Generic;
using NUnit.Framework;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// M7: hand plays, face-downs, and the play-on-top line - the spec 04 s9/s10 rules through
    /// the real engine.
    /// </summary>
    public class PlacementTests
    {
        private static DuelEngine Engine(out GameState s, string you = "fire", string foe = "water")
        {
            s = MatchSetup.NewMatch(TestData.Catalog,
                new CommanderId(you), new CommanderId(foe), 31, RulesOptions.JsParity);
            return new DuelEngine(s, TestData.Catalog);
        }

        /// <summary>Hand index of a named card, -1 when absent.</summary>
        private static int InHand(GameState s, Side side, string name)
        {
            var hand = s.P(side).Hand;
            for (int i = 0; i < hand.Count; i++)
                if (hand[i].Id.Value == name) return i;
            return -1;
        }

        private static void ToAction(DuelEngine e, GameState s)
        {
            Assert.IsTrue(e.Apply(new HarvestCommand(Side.You)).Applied);
            Assert.IsTrue(e.Apply(new DrawForTurnCommand(Side.You)).Applied);
            Assert.AreEqual(TurnPhase.Action, s.Phase);
        }

        private static int GiveCard(GameState s, string name, Element color)
        {
            s.P(Side.You).Hand.Add(new HandCard(new CardId(name), color));
            return s.P(Side.You).Hand.Count - 1;
        }

        [Test]
        public void Summon_ToOwnRows_PaysAndArrivesSick()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 5;
            int idx = GiveCard(s, "Sparkimp", Element.Fire);

            var r = e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Summon,
                new CellRef(RowKey.YouFront, 2)));
            Assert.IsTrue(r.Applied, r.Rejection.ToString());

            var cr = s.At(new CellRef(RowKey.YouFront, 2)) as CreatureUnit;
            Assert.IsNotNull(cr);
            Assert.AreEqual("Sparkimp", cr.Name);
            Assert.IsTrue(cr.Sick, "summons arrive summoning-sick");
            Assert.AreEqual(4, s.P(Side.You).Mana, "paid the printed cost");
            Assert.AreEqual(1, Upkeep.ZoneDeficit(s, Side.You, WorkerZone.Front, TestData.Catalog),
                "afterDeploy resynced: the front row now carries its upkeep");
        }

        [Test]
        public void Summon_NeverIntoTheCenter_NorEnemyGround()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;
            int idx = GiveCard(s, "Sparkimp", Element.Fire);

            Assert.AreEqual(Rejection.DestinationNotDeployable,
                e.CanApply(new PlayCardCommand(Side.You, idx, PlayMode.Summon, new CellRef(RowKey.Center, 3))),
                "creatures march to the center - they never deploy there");
            Assert.AreEqual(Rejection.DestinationNotDeployable,
                e.CanApply(new PlayCardCommand(Side.You, idx, PlayMode.Summon, new CellRef(RowKey.FoeFront, 3))));
            Assert.AreEqual(Rejection.DestinationNotDeployable,
                e.CanApply(new PlayCardCommand(Side.You, idx, PlayMode.Summon, new CellRef(RowKey.FoeBack, 3))));
        }

        [Test]
        public void Summon_ModeAndFundsAreValidated()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 0;
            int idx = GiveCard(s, "Magmaw", Element.Fire);   // cost 6

            Assert.AreEqual(Rejection.NotEnoughMana,
                e.CanApply(new PlayCardCommand(Side.You, idx, PlayMode.Summon, new CellRef(RowKey.YouBack, 0))));

            int trapIdx = GiveCard(s, "Snare Pit", Element.None);
            Assert.AreEqual(Rejection.WrongPlayMode,
                e.CanApply(new PlayCardCommand(Side.You, trapIdx, PlayMode.Summon, new CellRef(RowKey.YouBack, 0))),
                "a trap is not a creature - the mode is validated against the card type");
            Assert.AreEqual(Rejection.WrongPlayMode,
                e.CanApply(new PlayCardCommand(Side.You, trapIdx, PlayMode.Cast, new CellRef(RowKey.YouBack, 0))),
                "Cast waits for the spell resolver (M10)");
        }

        [Test]
        public void SetFaceDown_CostsOne_AndBanksTowardTheFlip()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 1;
            int idx = GiveCard(s, "Cinderling", Element.Fire);

            var at = new CellRef(RowKey.YouBack, 4);
            var r = e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Set, at));
            Assert.IsTrue(r.Applied, r.Rejection.ToString());

            var ch = s.At(at) as ChargeUnit;
            Assert.IsNotNull(ch);
            Assert.AreEqual(1, ch.Invested, "the set's own ◆1 banks toward the cost");
            Assert.AreEqual(0, s.P(Side.You).Mana);
            Assert.AreEqual(s.TurnNumber, ch.SetTurn);
            Assert.IsFalse(ch.IsStructure);

            int idx2 = GiveCard(s, "Sparkimp", Element.Fire);
            Assert.AreEqual(Rejection.NeedsOneMana,
                e.CanApply(new PlayCardCommand(Side.You, idx2, PlayMode.Set, new CellRef(RowKey.YouBack, 5))),
                "no free hand-dumping");
        }

        [Test]
        public void SetTrap_ConsumesOne_AndIsNotArmedUntilNextTurn()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 1;
            int idx = GiveCard(s, "Snare Pit", Element.None);

            var at = new CellRef(RowKey.YouBack, 6);
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.SetTrap, at)).Applied);

            var trap = s.At(at) as TrapUnit;
            Assert.IsNotNull(trap);
            Assert.AreEqual(TrapTrigger.Summon, trap.Trigger);
            Assert.AreEqual(0, s.P(Side.You).Mana, "the trap's ◆1 is consumed, banked toward nothing");
            Assert.IsFalse(trap.IsArmed(s.TurnNumber), "a trap never springs the turn it was set");
            Assert.IsTrue(trap.IsArmed(s.TurnNumber + 1));
        }

        [Test]
        public void PourAndFlip_SameTurnIsSick_SurplusBanks()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 6;
            int idx = GiveCard(s, "Cinderling", Element.Fire);   // cost 2

            var at = new CellRef(RowKey.YouBack, 4);
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Set, at)).Applied);
            var ch = (ChargeUnit)s.At(at);

            Assert.AreEqual(Rejection.ChargeUnderfunded,
                e.CanApply(new FlipChargeCommand(Side.You, at, ch.Id)), "inv 1 < cost 2");

            Assert.IsTrue(e.Apply(new PourIntoChargeCommand(Side.You, at, ch.Id, 3)).Applied);
            Assert.AreEqual(4, ch.Invested);
            Assert.AreEqual(2, s.P(Side.You).Mana);

            Assert.IsTrue(e.Apply(new FlipChargeCommand(Side.You, at, ch.Id)).Applied);
            var cr = s.At(at) as CreatureUnit;
            Assert.IsNotNull(cr);
            Assert.AreEqual("Cinderling", cr.Name);
            Assert.IsTrue(cr.Sick, "flipped the same turn it was set");
            Assert.AreEqual(2, cr.Bank, "inv 4 - cost 2 rides on as banked mana");
        }

        [Test]
        public void Pour_AcceptsLessThanIsOwed_SoAFaceDownCanBeDripFed()
        {
            // The whole point of setting a card face-down is funding it a little at a time when
            // you cannot afford it outright. A UI that only ever offers "fill to cost" makes this
            // unreachable, because the engine rejects a pour bigger than your pool - so the rule
            // that matters here is that a SMALL pour is legal and accumulates.
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 1;
            int idx = GiveCard(s, "Magmaw", Element.Fire);            // cost 6

            var at = new CellRef(RowKey.YouBack, 3);
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Set, at)).Applied);
            var ch = (ChargeUnit)s.At(at);
            Assert.AreEqual(1, ch.Invested, "the set's own ◆1 banks toward the cost");
            Assert.AreEqual(0, s.P(Side.You).Mana);

            Assert.AreEqual(Rejection.NotEnoughMana,
                e.CanApply(new PourIntoChargeCommand(Side.You, at, ch.Id, 5)),
                "filling to cost is refused outright when the pool cannot cover it");

            for (int drip = 0; drip < 5; drip++)                      // a turn's worth at a time
            {
                s.P(Side.You).Mana = 1;
                Assert.IsTrue(e.Apply(new PourIntoChargeCommand(Side.You, at, ch.Id, 1)).Applied,
                    "a partial pour is legal - this is the mechanic");
                Assert.AreEqual(0, s.P(Side.You).Mana);
                if (ch.Invested < 6)
                    Assert.AreEqual(Rejection.ChargeUnderfunded,
                        e.CanApply(new FlipChargeCommand(Side.You, at, ch.Id)));
            }

            Assert.AreEqual(6, ch.Invested, "five drips on top of the set's own ◆1");
            Assert.IsTrue(e.Apply(new FlipChargeCommand(Side.You, at, ch.Id)).Applied);

            var cr = (CreatureUnit)s.At(at);
            Assert.AreEqual("Magmaw", cr.Name, "a ◆6 creature paid for one mana at a time");
            Assert.AreEqual(0, cr.Bank, "exactly funded - nothing left over to bank");
        }

        [Test]
        public void Flip_OnALaterTurn_IsBattleReady()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 2;
            int idx = GiveCard(s, "Cinderling", Element.Fire);
            var at = new CellRef(RowKey.YouBack, 4);
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Set, at)).Applied);
            var chargeId = s.At(at).Id;
            Assert.IsTrue(e.Apply(new PourIntoChargeCommand(Side.You, at, chargeId, 1)).Applied);

            // run the round: your end, foe's whole turn, your next upkeep/draw
            Assert.IsTrue(e.Apply(new EndTurnCommand(Side.You)).Applied);
            Assert.IsTrue(e.Apply(new BeginTurnCommand(Side.Foe)).Applied);
            Assert.IsTrue(e.Apply(new HarvestCommand(Side.Foe)).Applied);
            Assert.IsTrue(e.Apply(new DrawForTurnCommand(Side.Foe)).Applied);
            Assert.IsTrue(e.Apply(new EndTurnCommand(Side.Foe)).Applied);
            Assert.IsTrue(e.Apply(new BeginTurnCommand(Side.You)).Applied);
            Assert.IsTrue(e.Apply(new HarvestCommand(Side.You)).Applied);
            Assert.IsTrue(e.Apply(new DrawForTurnCommand(Side.You)).Applied);

            Assert.IsTrue(e.Apply(new FlipChargeCommand(Side.You, at, chargeId)).Applied);
            var cr = (CreatureUnit)s.At(at);
            Assert.IsFalse(cr.Sick, "set on turn N, flipped on N+2 - battle-ready is the payoff");
        }

        [Test]
        public void Flip_DropsTheCardsColour_UnlessTheFlagSaysOtherwise()
        {
            // You are the WATER commander holding a FIRE card - the JS snapshot loses the colour
            // and mkCre falls back to the player's element (spec 04 s13.2 BUG).
            GameState s;
            var e = Engine(out s, "water", "fire");
            ToAction(e, s);
            s.P(Side.You).Mana = 2;
            int idx = GiveCard(s, "Sparkimp", Element.Fire);
            var at = new CellRef(RowKey.YouBack, 0);
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Set, at)).Applied);
            Assert.IsTrue(e.Apply(new FlipChargeCommand(Side.You, at, s.At(at).Id)).Applied);
            Assert.AreEqual(Element.Water, s.At(at).Color, "JS-faithful: the flip loses the colour");

            // and the fix, behind its flag
            var opts = RulesOptions.JsParity;
            opts.FaceDownKeepsColor = true;
            var s2 = MatchSetup.NewMatch(TestData.Catalog, new CommanderId("water"),
                new CommanderId("fire"), 31, opts);
            var e2 = new DuelEngine(s2, TestData.Catalog);
            ToAction(e2, s2);
            s2.P(Side.You).Mana = 2;
            s2.P(Side.You).Hand.Add(new HandCard(new CardId("Sparkimp"), Element.Fire));
            Assert.IsTrue(e2.Apply(new PlayCardCommand(Side.You, s2.P(Side.You).Hand.Count - 1,
                PlayMode.Set, at)).Applied);
            Assert.IsTrue(e2.Apply(new FlipChargeCommand(Side.You, at, s2.At(at).Id)).Applied);
            Assert.AreEqual(Element.Fire, s2.At(at).Color);
        }

        [Test]
        public void PlayOnTop_DestroysTheCovered_PaysFromItsBank_CarriesSurplus()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 1;

            // a banked structure to build over: bank 8 covers Magmaw's 6 with 2 to spare
            var foundry = TestData.Catalog.Structure(new StructId("foundry"), Element.None);
            var b = UnitFactory.MakeStructure(s, Side.You, foundry);
            b.Bank = 8;
            var at = new CellRef(RowKey.YouBack, 3);
            s.Put(at, b);

            int idx = GiveCard(s, "Magmaw", Element.Fire);
            var r = e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Summon, at));
            Assert.IsTrue(r.Applied, r.Rejection.ToString());

            var cr = s.At(at) as CreatureUnit;
            Assert.IsNotNull(cr);
            Assert.AreEqual("Magmaw", cr.Name);
            Assert.AreEqual(2, cr.Bank, "the surplus rides onto the newcomer");
            Assert.AreEqual(1, s.P(Side.You).Mana, "the bank covered the whole cost");
            Assert.AreEqual(1, s.P(Side.You).Grave.Count, "the covered card is destroyed");
            // the grave holds the CARD name, as toGrave's `nm` does - never the bid
            Assert.AreEqual("The Foundry", s.P(Side.You).Grave[0].Name);
        }

        /// <summary>
        /// A card may be played over ANY card of your own - including a structure, and including
        /// one with nothing banked on it. The covered card is razed to make room.
        ///
        /// It used to need a bank, because the line existed to spend one. That made your own
        /// board able to lock you out of your own deploy rows, which is the fault this widening
        /// answers; what it must NOT widen to is covering an enemy card, which would hand every
        /// creature in the deck a free removal spell.
        /// </summary>
        [Test]
        public void PlayOnTop_OverAnyCardOfYourOwn_ButNeverATheirs()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;
            int idx = GiveCard(s, "Sparkimp", Element.Fire);

            var bankless = UnitFactory.MakeCreature(s, Side.You,
                TestData.Catalog.Creature(new CardId("Cinderling")), Element.None);
            s.Put(new CellRef(RowKey.YouBack, 1), bankless);
            Assert.AreEqual(Rejection.None,
                e.CanApply(new PlayCardCommand(Side.You, idx, PlayMode.Summon, new CellRef(RowKey.YouBack, 1))),
                "a card of yours with no bank is still yours to build over");

            var yourForge = UnitFactory.MakeStructure(s, Side.You,
                TestData.Catalog.Structure(new StructId("foundry"), Element.None));
            s.Put(new CellRef(RowKey.YouBack, 2), yourForge);
            Assert.AreEqual(Rejection.None,
                e.CanApply(new PlayCardCommand(Side.You, idx, PlayMode.Summon, new CellRef(RowKey.YouBack, 2))),
                "and so is a structure");
            Assert.AreEqual(Rejection.None,
                e.CanApply(new PlayCardCommand(Side.You, idx, PlayMode.Set, new CellRef(RowKey.YouBack, 2))),
                "setting face-down covers it too, not just summoning");

            var foes = UnitFactory.MakeCreature(s, Side.Foe,
                TestData.Catalog.Creature(new CardId("Mistling")), Element.None);
            foes.Bank = 5;
            s.Put(new CellRef(RowKey.YouFront, 6), foes);   // a raider standing in YOUR row
            Assert.AreEqual(Rejection.CoveredCardNotYours,
                e.CanApply(new PlayCardCommand(Side.You, idx, PlayMode.Summon, new CellRef(RowKey.YouFront, 6))));
        }

        /// <summary>
        /// A FACE-DOWN's pot carries over too.
        ///
        /// A charge banks in Invested, not in Bank - the pot is the thing it is being paid for
        /// with - so a covering play that reads Bank finds zero and silently burns every point
        /// poured into it. Cheap to get wrong, and it looks exactly like mana going missing.
        /// </summary>
        [Test]
        public void PlayOnTop_OfAFaceDown_InheritsWhatWasPouredIntoIt()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 8;

            int setIdx = GiveCard(s, "Cinderling", Element.Fire);
            var at = new CellRef(RowKey.YouBack, 4);
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, setIdx, PlayMode.Set, at)).Applied);
            var ch = (ChargeUnit)s.At(at);
            Assert.IsTrue(e.Apply(new PourIntoChargeCommand(Side.You, at, ch.Id, 5)).Applied);
            Assert.AreEqual(6, ch.Invested, "◆1 for the set plus 5 poured");

            int cost = TestData.Catalog.Creature(new CardId("Sparkimp")).Cost;
            int idx = GiveCard(s, "Sparkimp", Element.Fire);
            int pool = s.P(Side.You).Mana;

            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Summon, at)).Applied);

            var now = s.At(at) as CreatureUnit;
            Assert.IsNotNull(now);
            Assert.AreEqual(6 - cost, now.Bank, "the rest of the pot rides on");
            Assert.AreEqual(pool, s.P(Side.You).Mana, "and the pot paid for all of it");
        }

        /// <summary>The covered card goes to the grave and its bank pays for what replaces it -
        /// the half of the play-on-top line that did not change.</summary>
        [Test]
        public void PlayOnTop_RazesTheCoveredCard_AndInheritsItsBank()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 0;                 // the bank pays for ALL of it
            int idx = GiveCard(s, "Sparkimp", Element.Fire);
            int cost = TestData.Catalog.Creature(new CardId("Sparkimp")).Cost;

            var forge = UnitFactory.MakeStructure(s, Side.You,
                TestData.Catalog.Structure(new StructId("foundry"), Element.None));
            forge.Bank = cost + 3;
            var at = new CellRef(RowKey.YouBack, 1);
            s.Put(at, forge);

            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Summon, at)).Applied);

            var now = s.At(at) as CreatureUnit;
            Assert.IsNotNull(now, "the newcomer is standing there");
            Assert.AreEqual("Sparkimp", now.Name);
            Assert.AreEqual(3, now.Bank, "the surplus rides on");
            Assert.AreEqual(0, s.P(Side.You).Mana, "and no pool mana was touched");
            Assert.AreEqual(1, s.P(Side.You).Grave.Count, "the forge was razed");
        }

        [Test]
        public void SendBankedMana_MovesTheWholeBank_EvenDuringUpkeep()
        {
            GameState s;
            var e = Engine(out s);
            // still in Upkeep - the transfer has no phase gate beyond it being your turn
            var a = UnitFactory.MakeCreature(s, Side.You,
                TestData.Catalog.Creature(new CardId("Sparkimp")), Element.None);
            a.Bank = 4;
            var bDef = TestData.Catalog.Structure(new StructId("foundry"), Element.None);
            var b = UnitFactory.MakeStructure(s, Side.You, bDef);
            s.Put(new CellRef(RowKey.YouBack, 0), a);
            s.Put(new CellRef(RowKey.YouBack, 1), b);

            var r = e.Apply(new SendBankedManaCommand(Side.You,
                new CellRef(RowKey.YouBack, 0), new CellRef(RowKey.YouBack, 1)));
            Assert.IsTrue(r.Applied, r.Rejection.ToString());
            Assert.AreEqual(0, a.Bank);
            Assert.AreEqual(4, b.Bank);
        }
    }
}
