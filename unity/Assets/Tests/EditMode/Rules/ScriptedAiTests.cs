using System.Collections.Generic;
using NUnit.Framework;
using SpawnRowDuel.Ai;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// M11's gate: the scripted AI plays whole matches through the ordinary command pipeline,
    /// deterministically, without ever proposing an illegal move (spec 07 s17.3).
    /// </summary>
    public class ScriptedAiTests
    {
        static DuelEngine Engine(out GameState s, ulong seed = 909,
                                 string you = "fire", string foe = "water")
        {
            s = MatchSetup.NewMatch(TestData.Catalog,
                new CommanderId(you), new CommanderId(foe), seed, RulesOptions.JsParity);
            return new DuelEngine(s, TestData.Catalog);
        }

        static AiDriver.Report SelfPlay(out GameState s, ulong seed, int turns)
        {
            var e = Engine(out s, seed);
            var driver = new AiDriver(e,
                new ScriptedAiPolicy(Side.You), new ScriptedAiPolicy(Side.Foe));
            return driver.Run(turns);
        }

        [Test]
        public void SelfPlay_RunsAFullMatch_WithoutEverProposingAnIllegalCommand()
        {
            GameState s;
            var report = SelfPlay(out s, 909, 200);

            Assert.AreEqual(Rejection.None, report.FirstRejection,
                "the AI proposed an illegal " + report.FirstRejectionCommand
                + " - a policy bug, not a recoverable situation");
            Assert.Greater(report.CommandsApplied, 100, "it actually played");
            Assert.IsTrue(report.Finished, "reached a conclusion rather than seizing up");
        }

        [Test]
        public void SelfPlay_IsDeterministic_SameSeedSameHash()
        {
            GameState a, b;
            var ra = SelfPlay(out a, 4242, 60);
            var rb = SelfPlay(out b, 4242, 60);

            Assert.AreEqual(ra.CommandsApplied, rb.CommandsApplied);
            Assert.AreEqual(ra.Turns, rb.Turns);
            Assert.AreEqual(StateCodec.Hash(a), StateCodec.Hash(b),
                "same seed, same policy, byte-identical state - the whole determinism contract");
        }

        [Test]
        public void SelfPlay_DifferentSeeds_Diverge()
        {
            GameState a, b;
            SelfPlay(out a, 1, 60);
            SelfPlay(out b, 2, 60);
            Assert.AreNotEqual(StateCodec.Hash(a), StateCodec.Hash(b),
                "the seed has to matter, or the RNG is not wired to anything");
        }

        [Test]
        public void SelfPlay_ReachesADecision_AndSomebodyWins()
        {
            // Every seed should terminate; most should terminate in a real result rather than by
            // exhausting the turn budget.
            int decided = 0;
            for (ulong seed = 1; seed <= 8; seed++)
            {
                GameState s;
                var report = SelfPlay(out s, seed, 300);
                Assert.AreEqual(Rejection.None, report.FirstRejection,
                    "seed " + seed + " proposed an illegal " + report.FirstRejectionCommand);
                if (s.IsOver) decided++;
            }
            Assert.AreEqual(8, decided, "every one of eight self-play matches should reach a "
                + "real result inside 300 turns rather than time out");
        }

        [Test]
        public void TheAi_Builds_UpgradesAndSummons_NotJustAttacks()
        {
            GameState s;
            var report = SelfPlay(out s, 77, 40);
            Assert.AreEqual(Rejection.None, report.FirstRejection);

            int structures = 0, creatures = 0;
            foreach (var kv in s.Objects())
            {
                if (kv.Value is StructureUnit) structures++;
                var c = kv.Value as CreatureUnit;
                if (c != null && !c.IsWorker) creatures++;
            }
            Assert.Greater(structures, 0, "it techs up from its commander's build menu");
            Assert.Greater(creatures, 0, "and puts bodies on the board");
        }

        [Test]
        public void TheAi_HarvestsThroughTheRealPhaseMachine()
        {
            // The JS AI never entered the phase machine at all (spec 07 s3.2) - it harvested by
            // hand and left G.phase sitting at 'end'. Ours runs the same machine the player does.
            GameState s;
            var e = Engine(out s);
            var foe = new ScriptedAiPolicy(Side.Foe);

            Assert.IsTrue(e.Apply(new HarvestCommand(Side.You)).Applied);
            Assert.IsTrue(e.Apply(new DrawForTurnCommand(Side.You)).Applied);
            Assert.IsTrue(e.Apply(new EndTurnCommand(Side.You)).Applied);
            Assert.IsTrue(e.Apply(new BeginTurnCommand(Side.Foe)).Applied);
            Assert.AreEqual(TurnPhase.Upkeep, s.Phase);

            var driver = new AiDriver(e, foe);
            var report = new AiDriver.Report();
            var seen = new List<TurnPhase>();
            for (int i = 0; i < 40 && driver.Step(report); i++)
                if (seen.Count == 0 || seen[seen.Count - 1] != s.Phase) seen.Add(s.Phase);

            Assert.AreEqual(Rejection.None, report.FirstRejection);
            Assert.Contains(TurnPhase.Draw, seen, "it drew for the turn");
            Assert.Contains(TurnPhase.Action, seen, "and reached its action phase");
            Assert.Greater(s.P(Side.Foe).Mana + s.P(Side.Foe).Hand.Count, 0);
        }

        [Test]
        public void PickTarget_TakesAGuaranteedKill_AndStormsTheWallWhenThereIsNothingToHit()
        {
            GameState s;
            var e = Engine(out s);

            var attacker = UnitFactory.MakeCreature(s, Side.Foe,
                TestData.Catalog.Creature(new CardId("Scorchling")), Element.Fire);  // 1500 atk
            s.Put(new CellRef(RowKey.FoeFront, 3), attacker);

            var wall = AiChoices.PickTarget(s, Side.Foe, attacker, AiTuning.JsDefault);
            Assert.IsInstanceOf<WallTarget>(wall, "an empty enemy board leaves only the wall");

            var frail = UnitFactory.MakeCreature(s, Side.You,
                TestData.Catalog.Creature(new CardId("Mistling")), Element.Water);   // 1000 hp
            s.Put(new CellRef(RowKey.YouFront, 1), frail);

            var kill = AiChoices.PickTarget(s, Side.Foe, attacker, AiTuning.JsDefault);
            var unit = kill as UnitTarget;
            Assert.IsNotNull(unit, "1500 attack into 1000 hp is a guaranteed kill - never declined");
            Assert.AreEqual(frail.Id, unit.UnitId);
        }

        [Test]
        public void PickTarget_ReadsRawAttack_NotTheOverchargeDischarge()
        {
            // spec 07 s18 bug 2, reproduced: a discharge that WOULD make the blow lethal is not
            // counted when the AI decides whether a kill is on.
            GameState s;
            var e = Engine(out s);

            var volt = UnitFactory.MakeCreature(s, Side.Foe,
                TestData.Catalog.Creature(new CardId("Volt")), Element.Electric);    // 1000/1000
            s.Put(new CellRef(RowKey.FoeFront, 3), volt);
            volt.DischargeBonus = 3;                       // effA is 1003 - still not 1500

            var tough = UnitFactory.MakeCreature(s, Side.You,
                TestData.Catalog.Creature(new CardId("Maelstrom")), Element.Water);  // 2500 hp
            tough.Hp = 1001;                               // beyond raw 1000, within nothing
            s.Put(new CellRef(RowKey.YouFront, 1), tough);

            var pick = AiChoices.PickTarget(s, Side.Foe, volt, AiTuning.JsDefault);
            Assert.IsInstanceOf<WallTarget>(pick,
                "raw 1000 < 1001, so no kill is seen and the wall gets stormed instead");
        }

        [Test]
        public void PickAbsorber_DumpsOnAKill_ElseOnTheToughest()
        {
            GameState s;
            var e = Engine(out s);

            var attacker = UnitFactory.MakeCreature(s, Side.Foe,
                TestData.Catalog.Creature(new CardId("Scorchling")), Element.Fire);  // 1500
            s.Put(new CellRef(RowKey.FoeFront, 3), attacker);

            var weak = UnitFactory.MakeCreature(s, Side.You,
                TestData.Catalog.Creature(new CardId("Mistling")), Element.Water);   // 1000 hp
            var tough = UnitFactory.MakeCreature(s, Side.You,
                TestData.Catalog.Creature(new CardId("Maelstrom")), Element.Water);  // 2500 hp
            s.Put(new CellRef(RowKey.YouFront, 1), weak);
            s.Put(new CellRef(RowKey.YouFront, 2), tough);

            var refs = new[]
            {
                UnitRef.Cell(new CellRef(RowKey.YouFront, 2), tough.Id),
                UnitRef.Cell(new CellRef(RowKey.YouFront, 1), weak.Id),
            };
            var req = new AbsorberRequest(Side.Foe, attacker.Id, refs);
            Assert.AreEqual(1, AiChoices.PickAbsorber(s, req), "1500 kills the Mistling - take it");

            tough.Hp = 2500;
            weak.Hp = 2000;                                // now nothing dies to 1500
            Assert.AreEqual(0, AiChoices.PickAbsorber(s, req),
                "no kill available: the JS dumps on the TOUGHEST, which is the flagged quirk");
        }

        [Test]
        public void DeployOrder_IsTheJsColumnPreference()
        {
            Assert.AreEqual(new[] { 3, 1, 5 }, AiChoices.CenterOrder);
            Assert.AreEqual(new[] { 3, 4, 2, 5, 1, 6, 0 }, AiChoices.FrontOrder);
            Assert.AreEqual(new[] { 2, 4, 3, 1, 5, 0, 6 }, AiChoices.BackOrder);

            GameState s;
            var e = Engine(out s);
            Assert.AreEqual(2, AiChoices.PickDeploySlot(s, Side.Foe, SlotName.Back),
                "an empty back row takes the middle-out preference's first entry");

            var row = Board.RowFor(Side.Foe, SlotName.Back);
            for (int col = 0; col < Board.Columns; col++)
                s.Put(new CellRef(row, col), UnitFactory.MakeCreature(s, Side.Foe,
                    TestData.Catalog.Creature(new CardId("Cinderling")), Element.Fire));
            Assert.AreEqual(-1, AiChoices.PickDeploySlot(s, Side.Foe, SlotName.Back),
                "a full row has nowhere to deploy");
        }
    }
}
