using System.Collections.Generic;
using NUnit.Framework;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// M6: the phase machine, harvest, drain, structure upkeep and the 12-step BeginTurn -
    /// through the real engine and the real handlers, never through back doors.
    /// </summary>
    public class TurnMachineTests
    {
        private static DuelEngine Engine(ulong seed)
        {
            var s = MatchSetup.NewMatch(TestData.Catalog,
                new CommanderId("fire"), new CommanderId("water"), seed, RulesOptions.JsParity);
            return new DuelEngine(s, TestData.Catalog);
        }

        private static void MustApply(DuelEngine e, ICommand cmd)
        {
            var r = e.Apply(cmd);
            Assert.IsTrue(r.Applied, cmd.GetType().Name + " rejected: " + r.Rejection);
        }

        [Test]
        public void FirstTurn_HarvestDrawEnd_WalksThePhases()
        {
            var e = Engine(1);
            var s = e.State;

            // Upkeep: fire commander has 2 ready back workers
            MustApply(e, new HarvestCommand(Side.You));
            Assert.AreEqual(2, s.P(Side.You).Mana, "2 workers x 1 each");
            Assert.AreEqual(TurnPhase.Draw, s.Phase);
            Assert.AreEqual(0, s.P(Side.You).Workers[(int)WorkerZone.Back].ReadyCount,
                "harvest taps every non-sick worker");

            MustApply(e, new DrawForTurnCommand(Side.You));
            Assert.AreEqual(5, s.P(Side.You).Hand.Count);
            Assert.AreEqual(35, s.P(Side.You).Deck.Count);
            Assert.AreEqual(TurnPhase.Action, s.Phase);

            MustApply(e, new EndTurnCommand(Side.You));
            Assert.AreEqual(TurnPhase.End, s.Phase);
            Assert.AreEqual(0, s.P(Side.You).Mana, "no vaults - unspent mana drains to nothing");

            MustApply(e, new BeginTurnCommand(Side.Foe));
            Assert.AreEqual(Side.Foe, s.Turn);
            Assert.AreEqual(2, s.TurnNumber, "the ply counter counts half-rounds");
            Assert.AreEqual(TurnPhase.Upkeep, s.Phase, "BOTH sides run the real phase machine");
        }

        [Test]
        public void PhaseGuards_RefuseOutOfOrderCommands()
        {
            var e = Engine(2);

            Assert.AreEqual(Rejection.WrongPhase, e.CanApply(new EndTurnCommand(Side.You)),
                "cannot end from Upkeep - harvest first");
            Assert.AreEqual(Rejection.WrongPhase, e.CanApply(new DrawForTurnCommand(Side.You)),
                "cannot draw from Upkeep");
            Assert.AreEqual(Rejection.NotYourTurn, e.CanApply(new HarvestCommand(Side.Foe)));
            Assert.AreEqual(Rejection.WrongPhase, e.CanApply(new BeginTurnCommand(Side.Foe)),
                "the next turn starts only from End");

            MustApply(e, new HarvestCommand(Side.You));
            Assert.AreEqual(Rejection.WrongPhase, e.CanApply(new HarvestCommand(Side.You)),
                "no second harvest - the phase moved on");
            Assert.AreEqual(Rejection.WrongPhase, e.CanApply(new EndTurnCommand(Side.You)),
                "cannot end from Draw - draw first");

            MustApply(e, new DrawForTurnCommand(Side.You));
            MustApply(e, new EndTurnCommand(Side.You));
            Assert.AreEqual(Rejection.NotYourTurn, e.CanApply(new BeginTurnCommand(Side.You)),
                "the side whose turn just ended cannot begin the next one");
        }

        [Test]
        public void EmptyDeck_DrawStillAdvances_NoDeckOutLoss()
        {
            var cat = TestData.Catalog;
            var s = MatchSetup.NewMatch(cat, new CommanderId("fire"), new CommanderId("water"),
                new List<HandCard>(), new List<HandCard>(), 3, RulesOptions.JsParity);
            var e = new DuelEngine(s, cat);

            MustApply(e, new HarvestCommand(Side.You));
            MustApply(e, new DrawForTurnCommand(Side.You));
            Assert.AreEqual(0, s.P(Side.You).Hand.Count);
            Assert.AreEqual(TurnPhase.Action, s.Phase);
            Assert.IsFalse(s.IsOver);
        }

        [Test]
        public void Drain_KeepsWhatTheVaultsHold()
        {
            var e = Engine(4);
            var s = e.State;
            var vault = TestData.Catalog.Structure(new StructId("vault"), Element.None);
            s.Put(new CellRef(RowKey.YouBack, 5),
                UnitFactory.MakeStructure(s, Side.You, vault));
            s.P(Side.You).Mana = 7;

            MustApply(e, new HarvestCommand(Side.You));      // +2 -> 9
            MustApply(e, new DrawForTurnCommand(Side.You));
            MustApply(e, new EndTurnCommand(Side.You));

            Assert.AreEqual(4, s.P(Side.You).Mana, "the Mana Vault holds ◆4 through the drain");

            bool drained = false;
            foreach (var ev in e.DrainEvents())
            {
                var d = ev as ManaDrained;
                if (d != null) { drained = true; Assert.AreEqual(4, d.Kept); Assert.AreEqual(5, d.Lost); }
            }
            Assert.IsTrue(drained);
        }

        [Test]
        public void BeginTurn_YieldsStructureMana_AndResyncsWorkers()
        {
            var e = Engine(5);
            var s = e.State;
            var foundry = TestData.Catalog.Structure(new StructId("foundry"), Element.None);
            s.Put(new CellRef(RowKey.YouBack, 0),
                UnitFactory.MakeStructure(s, Side.You, foundry));

            // run You's turn out, then Foe's whole turn, then come back around to You
            MustApply(e, new HarvestCommand(Side.You));
            MustApply(e, new DrawForTurnCommand(Side.You));
            MustApply(e, new EndTurnCommand(Side.You));
            MustApply(e, new BeginTurnCommand(Side.Foe));
            MustApply(e, new HarvestCommand(Side.Foe));
            MustApply(e, new DrawForTurnCommand(Side.Foe));
            MustApply(e, new EndTurnCommand(Side.Foe));
            MustApply(e, new BeginTurnCommand(Side.You));

            Assert.AreEqual(1, s.P(Side.You).Mana, "the Foundry yields ◆1 at its owner's upkeep");
            Assert.AreEqual(4, s.P(Side.You).Workers[(int)WorkerZone.Back].Count,
                "wk 2 + Foundry sup 2");
            Assert.AreEqual(4, s.P(Side.You).Workers[(int)WorkerZone.Back].ReadyCount,
                "turn-start workers are readied");
        }

        [Test]
        public void Tower_FiresAtTheFirstEnemyCreature_FrontBeforeBack_AndTheSweepFollows()
        {
            var e = Engine(6);
            var s = e.State;
            var cat = TestData.Catalog;

            var tower = cat.Structure(new StructId("tower"), Element.None);
            s.Put(new CellRef(RowKey.YouBack, 6), UnitFactory.MakeStructure(s, Side.You, tower));

            // 500 HP in the foe FRONT row (dies), 500 HP in the foe BACK row (must survive).
            // An Encampment (sup 2) keeps the foe's front-row upkeep solvent so its own
            // harvest is not locked by the shortfall rule while the turn cycles.
            var sparkimp = cat.Creature(new CardId("Sparkimp"));
            var frontling = UnitFactory.MakeCreature(s, Side.Foe, sparkimp, Element.None);
            var backling = UnitFactory.MakeCreature(s, Side.Foe, sparkimp, Element.None);
            s.Put(new CellRef(RowKey.FoeFront, 4), frontling);
            s.Put(new CellRef(RowKey.FoeBack, 0), backling);
            var camp = cat.Structure(new StructId("encampment"), Element.None);
            s.Put(new CellRef(RowKey.FoeFront, 6), UnitFactory.MakeStructure(s, Side.Foe, camp));

            // hand the turn around so You begins a fresh turn
            MustApply(e, new HarvestCommand(Side.You));
            MustApply(e, new DrawForTurnCommand(Side.You));
            MustApply(e, new EndTurnCommand(Side.You));
            MustApply(e, new BeginTurnCommand(Side.Foe));
            MustApply(e, new HarvestCommand(Side.Foe));
            MustApply(e, new DrawForTurnCommand(Side.Foe));
            MustApply(e, new EndTurnCommand(Side.Foe));
            e.DrainEvents();
            MustApply(e, new BeginTurnCommand(Side.You));

            Assert.IsNull(s.At(new CellRef(RowKey.FoeFront, 4)),
                "the tower's kill is swept before the worker resync");
            var survivor = s.At(new CellRef(RowKey.FoeBack, 0)) as CreatureUnit;
            Assert.IsNotNull(survivor, "front -> center -> back scan stops at the FIRST match");
            Assert.AreEqual(500, survivor.Hp);
            Assert.AreEqual(1, s.P(Side.Foe).Grave.Count);

            bool fired = false, destroyed = false;
            foreach (var ev in e.DrainEvents())
            {
                if (ev is TowerFired) { fired = true; Assert.AreEqual(frontling.Id, ((TowerFired)ev).TargetId); }
                if (ev is UnitDestroyed && ((UnitDestroyed)ev).UnitId == frontling.Id) destroyed = true;
            }
            Assert.IsTrue(fired);
            Assert.IsTrue(destroyed);
        }

        [Test]
        public void Reliquary_RevivesTheMostRecentRealCreature_OncePerTurn()
        {
            var e = Engine(7);
            var s = e.State;
            var cat = TestData.Catalog;

            var reliquary = cat.Structure(new StructId("reliquary"), Element.None);
            s.Put(new CellRef(RowKey.YouBack, 0), UnitFactory.MakeStructure(s, Side.You, reliquary));
            s.Put(new CellRef(RowKey.YouBack, 1), UnitFactory.MakeStructure(s, Side.You, reliquary));

            s.P(Side.You).Grave.Add(new GraveRecord(new CardId("Sparkimp"), "Sparkimp",
                Element.Fire, UnitKind.Creature, false, false, 1));
            s.P(Side.You).Grave.Add(new GraveRecord(new CardId("Worker"), "Worker",
                Element.Fire, UnitKind.Creature, false, true, 1));
            s.P(Side.You).Grave.Add(new GraveRecord(new CardId("Magmaw"), "Magmaw",
                Element.Fire, UnitKind.Creature, false, false, 2));

            int handBefore = s.P(Side.You).Hand.Count;

            MustApply(e, new HarvestCommand(Side.You));
            MustApply(e, new DrawForTurnCommand(Side.You));
            MustApply(e, new EndTurnCommand(Side.You));
            MustApply(e, new BeginTurnCommand(Side.Foe));
            MustApply(e, new HarvestCommand(Side.Foe));
            MustApply(e, new DrawForTurnCommand(Side.Foe));
            MustApply(e, new EndTurnCommand(Side.Foe));
            MustApply(e, new BeginTurnCommand(Side.You));

            // one revive despite two Reliquaries: Magmaw (most recent), skipping the worker
            var hand = s.P(Side.You).Hand;
            Assert.AreEqual(handBefore + 1 + 1, hand.Count, "one draw + ONE revive");
            Assert.AreEqual("Magmaw", hand[hand.Count - 1].Id.Value,
                "the most recently fallen real creature comes back");
            Assert.AreEqual(2, s.P(Side.You).Grave.Count, "the worker record stays");
        }

        [Test]
        public void Chrysalis_SwellsResicks_ThenHatchesInPlace()
        {
            var cat = TestData.Catalog;
            var s = MatchSetup.NewMatch(cat, new CommanderId("forest"), new CommanderId("water"),
                11, RulesOptions.JsParity);

            var sapPod = UnitFactory.MakeCreature(s, Side.You,
                cat.Creature(new CardId("Sap Pod")), Element.None);
            s.Put(new CellRef(RowKey.YouFront, 2), sapPod);
            int id = sapPod.Id;

            var ev = new EventSink();

            TurnPipeline.BeginTurn(s, Side.You, cat, ev);      // cnt 1 - swells, re-sicks
            Assert.AreEqual(1, sapPod.ChrysalisCount);
            Assert.IsTrue(sapPod.Sick, "a cocoon can never act");
            Assert.AreEqual("Sap Pod", sapPod.Name);

            TurnPipeline.BeginTurn(s, Side.You, cat, ev);      // cnt 2
            TurnPipeline.BeginTurn(s, Side.You, cat, ev);      // cnt 3 - hatch

            var hatched = s.At(new CellRef(RowKey.YouFront, 2)) as CreatureUnit;
            Assert.IsNotNull(hatched);
            Assert.AreEqual(id, hatched.Id, "hatching mutates IN PLACE - same unit id");
            Assert.AreEqual("Canopy Beast", hatched.Name);
            Assert.AreEqual(2500, hatched.Attack);
            Assert.AreEqual(2000, hatched.Hp);
            Assert.AreEqual(Keyword.None, hatched.Keyword, "the keyword clears to stop the loop");
            Assert.IsTrue(hatched.Sick, "it hatches summoning-sick");

            TurnPipeline.BeginTurn(s, Side.You, cat, ev);
            Assert.AreEqual("Canopy Beast", hatched.Name, "hatched form is stable");
            Assert.IsFalse(hatched.Sick, "a hatched creature readies like anything else");
        }

        [Test]
        public void Overcharge_BanksToThreeAndStops()
        {
            var cat = TestData.Catalog;
            var s = MatchSetup.NewMatch(cat, new CommanderId("electric"), new CommanderId("water"),
                12, RulesOptions.JsParity);

            CreatureUnit sparky = null;
            foreach (var c in cat.PoolOf(Element.Electric))
                if (c.Keyword == Keyword.Overcharge)
                {
                    sparky = UnitFactory.MakeCreature(s, Side.You, c, Element.None);
                    break;
                }
            Assert.IsNotNull(sparky, "the Electric pool has an Overcharge creature");
            s.Put(new CellRef(RowKey.YouFront, 0), sparky);

            var ev = new EventSink();
            for (int i = 0; i < 5; i++) TurnPipeline.BeginTurn(s, Side.You, cat, ev);
            Assert.AreEqual(3, sparky.OverchargeBank, "oc = min(3, oc+1)");
        }

        [Test]
        public void TwoHundredTurns_StableAndDeterministic()
        {
            var a = Engine(777);
            var b = Engine(777);

            var hashesA = new List<ulong>();
            var hashesB = new List<ulong>();

            for (int t = 0; t < 200; t++)
            {
                var side = a.State.Turn;
                MustApply(a, new HarvestCommand(side));
                MustApply(a, new DrawForTurnCommand(side));
                MustApply(a, new EndTurnCommand(side));
                MustApply(a, new BeginTurnCommand(TurnMachine.Other(side)));
                hashesA.Add(a.Hash());

                var sideB = b.State.Turn;
                MustApply(b, new HarvestCommand(sideB));
                MustApply(b, new DrawForTurnCommand(sideB));
                MustApply(b, new EndTurnCommand(sideB));
                MustApply(b, new BeginTurnCommand(TurnMachine.Other(sideB)));
                hashesB.Add(b.Hash());
            }

            Assert.AreEqual(201, a.State.TurnNumber);
            Assert.AreEqual(0, a.State.P(Side.You).Deck.Count, "the deck ran dry long ago, harmlessly");
            CollectionAssert.AreEqual(hashesA, hashesB,
                "the per-turn hash trace is the determinism contract");
        }
    }
}
