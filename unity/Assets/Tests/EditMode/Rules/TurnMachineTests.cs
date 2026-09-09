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

        // EmptyDeck_DrawStillAdvances_NoDeckOutLoss lived here until 2026-09-07. Deck-out is a
        // loss now; DrawForTurn_OnAnEmptyDeck_LosesTheMatch below pins the rule that replaced it.

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

            Assert.AreEqual(1, s.P(Side.You).Mana, "the Base yields ◆1 at its owner's upkeep");
            Assert.AreEqual(3, s.P(Side.You).Workers[(int)WorkerZone.Back].Count,
                "wk 2 + Base sup 1");
            Assert.AreEqual(3, s.P(Side.You).Workers[(int)WorkerZone.Back].ReadyCount,
                "turn-start workers are readied");
        }

        /// <summary>
        /// Deck-out is a loss (2026-09-07): the draw a player cannot make ends the match against
        /// them. The opening deal is exempt - MatchSetupTests pins that an empty deck deals an
        /// empty hand and nothing more.
        /// </summary>
        [Test]
        public void DrawForTurn_OnAnEmptyDeck_LosesTheMatch()
        {
            var e = Engine(7);
            var s = e.State;
            s.P(Side.You).Deck.Clear();

            MustApply(e, new HarvestCommand(Side.You));
            Assert.IsFalse(s.IsOver, "harvesting with an empty deck is fine; drawing is the problem");

            var r = e.Apply(new DrawForTurnCommand(Side.You));
            Assert.IsTrue(r.Applied, "the draw is a legal command that ends the game, not a rejection");
            Assert.IsTrue(s.IsOver);
            Assert.AreEqual(MatchOutcome.FoeWin, s.Outcome, "the player who could not draw loses");
            Assert.AreEqual(TurnPhase.Draw, s.Phase, "the phase never advances past the draw that ended it");

            bool decked = false, ended = false;
            foreach (var ev in e.Events)
            {
                if (ev is DeckedOut && ((DeckedOut)ev).Side == Side.You) decked = true;
                if (ev is MatchEnded) ended = true;
            }
            Assert.IsTrue(decked, "DeckedOut says why");
            Assert.IsTrue(ended, "MatchEnded says who");

            Assert.AreEqual(Rejection.GameOver, e.CanApply(new EndTurnCommand(Side.You)),
                "nothing is playable once the match is over");
        }

        [Test]
        public void Tower_FiresAtTheFirstEnemyCreature_FrontBeforeBack_AndTheSweepFollows()
        {
            var e = Engine(6);
            var s = e.State;
            var cat = TestData.Catalog;

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

            // Turn 1: harvest and draw with no tower standing, then raise it in the ACTION phase
            // the way a real build lands - so it has to survive the foe's turn before its first
            // shot. A tower put down before the first harvest would fire at that harvest, which
            // is what a pre-existing tower does and what a built one never can.
            MustApply(e, new HarvestCommand(Side.You));
            MustApply(e, new DrawForTurnCommand(Side.You));
            var tower = cat.Structure(new StructId("tower"), Element.None);
            s.Put(new CellRef(RowKey.YouBack, 6), UnitFactory.MakeStructure(s, Side.You, tower));
            MustApply(e, new EndTurnCommand(Side.You));
            MustApply(e, new BeginTurnCommand(Side.Foe));
            MustApply(e, new HarvestCommand(Side.Foe));
            MustApply(e, new DrawForTurnCommand(Side.Foe));
            MustApply(e, new EndTurnCommand(Side.Foe));
            e.DrainEvents();
            MustApply(e, new BeginTurnCommand(Side.You));

            // Towers fire at HARVEST, not at turn start: the target is still standing here.
            Assert.IsNotNull(s.At(new CellRef(RowKey.FoeFront, 4)),
                "turn start does not fire the tower any more");

            MustApply(e, new HarvestCommand(Side.You));
            Assert.IsNull(s.At(new CellRef(RowKey.FoeFront, 4)),
                "the tower fires at harvest and its kill is swept before the workers dig");
            var survivor = s.At(new CellRef(RowKey.FoeBack, 0)) as CreatureUnit;
            Assert.IsNotNull(survivor, "front -> center -> back scan stops at the FIRST match");
            Assert.AreEqual(500, survivor.Hp);
            Assert.AreEqual(TurnPhase.Draw, s.Phase, "and the harvest still moved the phase on");
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

        /// <summary>
        /// "They must be built, survive a turn, and have adequate resources to fire" (2026-09-07).
        /// A tower whose row cannot crew it is silent; the orphaned shortfall it causes is paid out
        /// of the harvest AFTER the shot would have gone, so it stays silent until the row can
        /// carry it - here, until an Encampment moves in beside it.
        /// </summary>
        [Test]
        public void Tower_StaysSilent_WhileItsRowCannotCrewIt()
        {
            var e = Engine(8);
            var s = e.State;
            var cat = TestData.Catalog;

            // a lone tower in YOUR FRONT row: figure 0 - 1 = -1, an orphaned shortfall
            var tower = cat.Structure(new StructId("tower"), Element.None);
            s.Put(new CellRef(RowKey.YouFront, 6), UnitFactory.MakeStructure(s, Side.You, tower));
            var sparkimp = cat.Creature(new CardId("Sparkimp"));
            s.Put(new CellRef(RowKey.FoeFront, 4),
                UnitFactory.MakeCreature(s, Side.Foe, sparkimp, Element.None));
            var camp = cat.Structure(new StructId("encampment"), Element.None);
            s.Put(new CellRef(RowKey.FoeFront, 6), UnitFactory.MakeStructure(s, Side.Foe, camp));

            Assert.AreEqual(1, Upkeep.ZoneDeficit(s, Side.You, WorkerZone.Front, cat),
                "the crew is one short");
            MustApply(e, new HarvestCommand(Side.You));        // orphaned: harvests through
            Assert.IsNotNull(s.At(new CellRef(RowKey.FoeFront, 4)),
                "an uncrewed tower does not fire, even though the harvest went ahead");

            // give the row a crew, come back around
            s.Put(new CellRef(RowKey.YouFront, 0), UnitFactory.MakeStructure(s, Side.You, camp));
            MustApply(e, new DrawForTurnCommand(Side.You));
            MustApply(e, new EndTurnCommand(Side.You));
            MustApply(e, new BeginTurnCommand(Side.Foe));
            MustApply(e, new HarvestCommand(Side.Foe));
            MustApply(e, new DrawForTurnCommand(Side.Foe));
            MustApply(e, new EndTurnCommand(Side.Foe));
            MustApply(e, new BeginTurnCommand(Side.You));

            Assert.AreEqual(0, Upkeep.ZoneDeficit(s, Side.You, WorkerZone.Front, cat),
                "encampment +2, tower -1: one to spare");
            MustApply(e, new HarvestCommand(Side.You));
            Assert.IsNull(s.At(new CellRef(RowKey.FoeFront, 4)),
                "crewed, it fires at the next harvest");
        }

        [Test]
        public void Sanctuary_MendsTheWorstHurtCreatureInItsOwnRow()
        {
            var e = Engine(7);
            var s = e.State;
            var cat = TestData.Catalog;

            // A Sanctuary in the BACK row, and three hurt creatures: two beside it and one in the
            // front row. The front one is the worst hurt of the three and must be ignored - the
            // mend is bound to the Sanctuary's own row, which is what makes where you put it a
            // decision rather than a formality.
            var sanctuary = cat.Structure(new StructId("reliquary"), Element.None);
            s.Put(new CellRef(RowKey.YouBack, 0), UnitFactory.MakeStructure(s, Side.You, sanctuary));

            var near = Hurt(s, cat, new CellRef(RowKey.YouBack, 2), 200);   // 200 missing
            var worst = Hurt(s, cat, new CellRef(RowKey.YouBack, 3), 900);  // 900 missing - wins
            var offRow = Hurt(s, cat, new CellRef(RowKey.YouFront, 3), 1500);

            int nearHp = near.Hp, worstHp = worst.Hp, offHp = offRow.Hp;

            // The upkeep tick DIRECTLY, not a whole turn. Three Magmaws is ⚒9 of upkeep, so a
            // turn taken the long way is refused at Harvest for an unsettled shortfall - which
            // tests the worker economy, not the mend. This is the step the rule lives in.
            StructureUpkeep.Tick(s, Side.You, cat, new EventSink());

            Assert.AreEqual(worstHp + 500, worst.Hp,
                "the worst-hurt creature in the Sanctuary's row is mended by its value");
            Assert.AreEqual(nearHp, near.Hp, "and only one of them - the mend is not a row heal");
            Assert.AreEqual(offHp, offRow.Hp,
                "a creature in another row is never touched, however badly hurt");
        }

        /// <summary>A creature standing at full health less <paramref name="missing"/>.
        /// Magmaw prints ♥2500, so every wound here has to stay well inside that.</summary>
        static CreatureUnit Hurt(GameState s, ICardCatalog cat, CellRef at, int missing)
        {
            var c = UnitFactory.MakeCreature(s, Side.You, cat.Creature(new CardId("Magmaw")),
                                             Element.Fire);
            c.Hp = c.MaxHp - missing;
            s.Put(at, c);
            return c;
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

            // Two hundred turns was the budget when an empty deck drew nothing and played on.
            // Deck-out ends the match now, so the loop runs until it does - which is itself part
            // of the contract: both engines must end on the same turn, for the same reason.
            for (int t = 0; t < 200 && !a.State.IsOver; t++)
            {
                var side = a.State.Turn;
                MustApply(a, new HarvestCommand(side));
                MustApply(a, new DrawForTurnCommand(side));
                if (a.State.IsOver) { hashesA.Add(a.Hash()); }
                else
                {
                    MustApply(a, new EndTurnCommand(side));
                    MustApply(a, new BeginTurnCommand(TurnMachine.Other(side)));
                    hashesA.Add(a.Hash());
                }

                var sideB = b.State.Turn;
                MustApply(b, new HarvestCommand(sideB));
                MustApply(b, new DrawForTurnCommand(sideB));
                if (b.State.IsOver) { hashesB.Add(b.Hash()); }
                else
                {
                    MustApply(b, new EndTurnCommand(sideB));
                    MustApply(b, new BeginTurnCommand(TurnMachine.Other(sideB)));
                    hashesB.Add(b.Hash());
                }
            }

            Assert.IsTrue(a.State.IsOver, "a match nobody attacks in ends when the first deck runs dry");
            Assert.AreEqual(MatchOutcome.FoeWin, a.State.Outcome,
                "You drew first, so You drew from an empty deck first");
            Assert.AreEqual(0, a.State.P(Side.You).Deck.Count);
            Assert.Greater(a.State.TurnNumber, 70, "36 cards after the opening hand is 36 draws");
            Assert.AreEqual(a.State.TurnNumber, b.State.TurnNumber, "both engines end on the same turn");
            CollectionAssert.AreEqual(hashesA, hashesB,
                "the per-turn hash trace is the determinism contract");
        }
    }
}
