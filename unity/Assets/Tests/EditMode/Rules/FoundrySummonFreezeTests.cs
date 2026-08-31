using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using SpawnRowDuel.Ai;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// FIELD REPORT REPRO (2026-08-31): "the game crashed after I played a foundry and tried to
    /// summon a monster. The game is frozen in browser."
    ///
    /// The screenshots pin the situation exactly: turn 5, YOUR TURN, upkeep with a single HARVEST
    /// button; your side holds an Encampment on the centre flank, an Emberforge and one Foundry;
    /// the foe's back row holds Encampment / Foundry / Brinekin / Tidewell. Harvest, draw Ember
    /// Bolt, BUILD A SECOND FOUNDRY ("the foundry rises"), mana 9, hand = Cinderling, Infernox,
    /// Sparkimp, Magmaw, Emberfly, Emberfly, Ember Bolt. Then a summon, then the freeze.
    ///
    /// This file drives that sequence THROUGH THE RULES ONLY - no MonoBehaviour, no frame loop -
    /// so it can answer one question and only one: does the deterministic engine hang, throw, or
    /// park an unanswerable choice on that path? Every engine call runs on a background thread
    /// with a hard 2 s join, so an unbounded loop FAILS the test instead of wedging the runner,
    /// and every driving loop carries its own iteration budget.
    ///
    /// If these pass, the freeze is in the VIEW, not the rules.
    /// </summary>
    public class FoundrySummonFreezeTests
    {
        /// <summary>Any single engine call that takes longer than this is treated as a hang.</summary>
        const int BudgetMs = 2000;

        /// <summary>The hand the screenshot shows, in screenshot order.</summary>
        static readonly string[] ScreenshotHand =
        {
            "Cinderling", "Infernox", "Sparkimp", "Magmaw", "Emberfly", "Emberfly", "Ember Bolt",
        };

        [OneTimeSetUp]
        public void LoadCatalogOnTheMainThread()
        {
            // TestData.Catalog reads Application.dataPath - warm it here so no worker thread
            // touches a UnityEngine API.
            Assert.IsNotNull(TestData.Catalog);
        }

        // ── guarded engine calls ─────────────────────────────────────────────────────────────

        static void RunGuarded(ThreadStart body, string what)
        {
            Exception err = null;
            var t = new Thread(delegate ()
            {
                try { body(); }
                catch (Exception ex) { err = ex; }
            });
            t.IsBackground = true;          // a genuinely hung thread must not hold the runner open
            t.Start();

            if (!t.Join(BudgetMs))
                Assert.Fail("HANG: " + what + " did not return inside " + BudgetMs
                            + " ms - that is an unbounded loop inside the rules");
            if (err != null)
                Assert.Fail("THREW: " + what + " -> " + err);
        }

        static CommandResult Apply(DuelEngine e, ICommand cmd, string what)
        {
            var res = CommandResult.No(Rejection.UnknownCommand);
            RunGuarded(delegate { res = e.Apply(cmd); }, what + " [" + cmd.GetType().Name + "]");
            return res;
        }

        static CommandResult MustApply(DuelEngine e, ICommand cmd, string what)
        {
            var r = Apply(e, cmd, what);
            Assert.IsTrue(r.Applied, what + " was refused: " + r.Rejection);
            return r;
        }

        static bool AiStep(AiDriver ai, AiDriver.Report rep, string what)
        {
            bool moved = false;
            RunGuarded(delegate { moved = ai.Step(rep); }, what);
            return moved;
        }

        /// <summary>
        /// Exactly MatchController.ProbeLegalCells (View/MatchController.cs:327-336): the view
        /// lights a play by asking CanApply about all 35 cells. Guarded as ONE unit, because that
        /// is one frame's worth of work in the real client.
        /// </summary>
        static List<CellRef> ProbeLegalCells(DuelEngine e, Func<CellRef, ICommand> make, string what)
        {
            List<CellRef> legal = null;
            RunGuarded(delegate
            {
                var acc = new List<CellRef>();
                for (int i = 0; i < Board.Cells; i++)
                {
                    var cell = CellRef.FromIndex(i);
                    if (e.CanApply(make(cell)) == Rejection.None) acc.Add(cell);
                }
                legal = acc;
            }, what);
            return legal ?? new List<CellRef>();
        }

        // ── the board the screenshots show ───────────────────────────────────────────────────

        static DuelEngine NewGame(out GameState s, ulong seed)
        {
            s = MatchSetup.NewMatch(TestData.Catalog, new CommanderId("fire"),
                                    new CommanderId("water"), seed, RulesOptions.JsParity);
            return new DuelEngine(s, TestData.Catalog);
        }

        static void Place(GameState s, CellRef at, Side owner, string bid, Element color)
        {
            s.Put(at, UnitFactory.MakeStructure(s, owner,
                TestData.Catalog.Structure(new StructId(bid), color)));
        }

        /// <summary>The two boards from the screenshots, planted whole.</summary>
        static void PlantScreenshotBoard(GameState s)
        {
            Place(s, new CellRef(RowKey.YouBack, 2), Side.You, "foundry", Element.None);
            Place(s, new CellRef(RowKey.YouBack, 4), Side.You, "forge", Element.Fire);   // Emberforge
            Place(s, new CellRef(RowKey.Center, 2), Side.You, "encampment", Element.None);

            Place(s, new CellRef(RowKey.FoeBack, 1), Side.Foe, "encampment", Element.None);
            Place(s, new CellRef(RowKey.FoeBack, 2), Side.Foe, "foundry", Element.None);
            Place(s, new CellRef(RowKey.FoeBack, 4), Side.Foe, "forge", Element.Water);  // Tidewell
            s.Put(new CellRef(RowKey.FoeBack, 3), UnitFactory.MakeCreature(
                s, Side.Foe, TestData.Catalog.Creature(new CardId("Brinekin")), Element.Water));

            WorkerMath.Resync(s, Side.You, TestData.Catalog);
            WorkerMath.Resync(s, Side.Foe, TestData.Catalog);
            s.P(Side.You).ReadyWorkers();
            s.P(Side.Foe).ReadyWorkers();
        }

        static void DealScreenshotHand(GameState s)
        {
            var p = s.P(Side.You);
            p.Hand.Clear();
            for (int i = 0; i < ScreenshotHand.Length; i++)
                p.Hand.Add(new HandCard(new CardId(ScreenshotHand[i]),
                    ScreenshotHand[i] == "Ember Bolt" ? Element.None : Element.Fire));
        }

        /// <summary>
        /// Advance to YOUR ply N with the real scripted AI playing the foe, so the enemy board -
        /// set traps included - is whatever the shipping opponent actually builds. Hard step
        /// budget, so a stall fails rather than spins.
        /// </summary>
        static void PlayToYourPly(DuelEngine e, GameState s, int ply)
        {
            var ai = new AiDriver(e, new ScriptedAiPolicy(Side.Foe));
            var rep = new AiDriver.Report();

            for (int step = 0; !(s.Turn == Side.You && s.TurnNumber >= ply); step++)
            {
                Assert.Less(step, 2000, "the drive to ply " + ply + " never got there - stalled at "
                            + s.Turn + " ply " + s.TurnNumber + " " + s.Phase);
                Assert.IsFalse(s.IsOver, "the match ended before ply " + ply);

                if (s.Pending != null)
                {
                    Assert.AreEqual(Side.Foe, s.Pending.Responder,
                        "a " + s.Pending.Kind + " parked on the PLAYER outside their own action - "
                        + "that is a soft lock with no UI behind it");
                    Assert.IsTrue(AiStep(ai, rep, "AI answers its own " + s.Pending.Kind),
                        "the AI could not answer the " + s.Pending.Kind + " parked on it: "
                        + rep.FirstRejection + " on " + rep.FirstRejectionCommand);
                    continue;
                }

                if (s.Turn == Side.Foe)
                {
                    if (AiStep(ai, rep, "foe AI step")) continue;
                    Assert.AreEqual(Rejection.None, rep.FirstRejection,
                        "the AI proposed an illegal " + rep.FirstRejectionCommand);
                    Assert.AreEqual(TurnPhase.End, s.Phase, "the foe fell silent in " + s.Phase);
                    MustApply(e, new BeginTurnCommand(Side.You), "hand back to you");
                    continue;
                }

                switch (s.Phase)
                {
                    case TurnPhase.Upkeep:
                        MustApply(e, new HarvestCommand(Side.You), "your harvest"); break;
                    case TurnPhase.Draw:
                        MustApply(e, new DrawForTurnCommand(Side.You), "your draw"); break;
                    case TurnPhase.Action:
                        MustApply(e, new EndTurnCommand(Side.You), "your end turn"); break;
                    default:
                        MustApply(e, new BeginTurnCommand(Side.Foe), "hand off to the foe"); break;
                }
            }
        }

        /// <summary>
        /// A parked window is only survivable if SOMEBODY can answer it. Anything parked on the
        /// player during their own summon has no HUD behind it (MatchHud only renders blocker /
        /// absorber / retaliation / response prompts for Seat.Local when it knows the shape), and
        /// CommandProcessor.cs:55 then refuses EVERY other command - which is the exact signature
        /// of "the tab paints but nothing responds".
        /// </summary>
        static void AssertPendingIsAnswerable(DuelEngine e, GameState s, string context)
        {
            if (s.Pending == null) return;

            Assert.AreEqual(Side.Foe, s.Pending.Responder,
                context + ": the summon parked a " + s.Pending.Kind + " on the PLAYER. "
                + "Every subsequent command is now ChoicePending (CommandProcessor.cs:55).");

            var ai = new AiDriver(e, new ScriptedAiPolicy(Side.Foe));
            var rep = new AiDriver.Report();
            for (int i = 0; i < 8 && s.Pending != null; i++)
                if (!AiStep(ai, rep, context + ": AI answers " + s.Pending.Kind)) break;

            Assert.IsNull(s.Pending, context + ": the " + " window parked by the summon was still "
                + "unanswered after 8 AI steps (" + rep.FirstRejection + ")");
        }

        // ── 1. the literal screenshot sequence ───────────────────────────────────────────────

        [Test]
        public void Turn5_SecondFoundry_ThenEverySummonInHand_NeverHangs()
        {
            GameState s;
            var e = NewGame(out s, 5150);

            // ply 5, your turn, upkeep - with the screenshot's board planted on it
            PlayToYourPly(e, s, 5);
            PlantScreenshotBoard(s);
            s.P(Side.You).Mana = 3;                                   // the first screenshot's ◆3
            Assert.AreEqual(TurnPhase.Upkeep, s.Phase);
            Assert.AreEqual(Side.You, s.Turn);
            Assert.AreEqual(5, s.TurnNumber);

            MustApply(e, new HarvestCommand(Side.You), "turn-5 harvest");
            MustApply(e, new DrawForTurnCommand(Side.You), "turn-5 draw");
            Assert.AreEqual(TurnPhase.Action, s.Phase);

            DealScreenshotHand(s);
            s.P(Side.You).Mana = 9;                                   // the second screenshot's ◆9

            // "The foundry rises" - the SECOND Foundry, beside the first
            var buildCells = ProbeLegalCells(e,
                cell => new BuildStructureCommand(Side.You, new StructId("foundry"),
                                                  Element.None, cell),
                "probe 35 cells for the second Foundry");
            Assert.Contains(new CellRef(RowKey.YouBack, 3), buildCells,
                "the cell beside the standing Foundry has to be a legal build site");
            MustApply(e, new BuildStructureCommand(Side.You, new StructId("foundry"), Element.None,
                new CellRef(RowKey.YouBack, 3)), "raise the second Foundry");

            int foundries = 0;
            foreach (var kv in s.Objects())
            {
                var b = kv.Value as StructureUnit;
                if (b != null && b.Owner == Side.You && b.DefId.Value == "foundry") foundries++;
            }
            Assert.AreEqual(2, foundries, "two Foundries side by side, as in the screenshot");

            // ...and now the summon. Every card, every cell the view would light.
            int summonsApplied = 0;
            for (int h = 0; h < s.P(Side.You).Hand.Count; h++)
            {
                string name = s.P(Side.You).Hand[h].Id.Value;

                var cells = ProbeLegalCells(e,
                    cell => new PlayCardCommand(Side.You, h, PlayMode.Summon, cell),
                    "probe 35 cells to summon " + name);

                // every legal drop, on a CLONE, so one summon cannot mask the next
                foreach (var cell in cells)
                {
                    var branch = s.Clone();
                    var be = new DuelEngine(branch, TestData.Catalog);
                    var r = Apply(be, new PlayCardCommand(Side.You, h, PlayMode.Summon, cell),
                                  "summon " + name + " to " + cell);
                    Assert.IsTrue(r.Applied, "a cell the probe lit refused the play: "
                                  + name + " -> " + cell + " (" + r.Rejection + ")");
                    AssertPendingIsAnswerable(be, branch, "summon " + name + " to " + cell);
                }

                if (cells.Count > 0 && s.P(Side.You).Mana >= 1)
                {
                    var r = Apply(e, new PlayCardCommand(Side.You, h, PlayMode.Summon, cells[0]),
                                  "REAL summon " + name + " to " + cells[0]);
                    if (r.Applied)
                    {
                        summonsApplied++;
                        AssertPendingIsAnswerable(e, s, "real summon " + name);
                        h--;                                    // the hand shifted under us
                    }
                }
            }

            Assert.Greater(summonsApplied, 0, "the sequence never actually summoned anything - "
                + "the repro did not reach the reported moment");
        }

        // ── 2. the same shape, across seeds, with the real AI's board opposite ───────────────

        [Test]
        public void AcrossSeeds_SecondFoundryThenSummon_NeverHangsAndNeverParksOnThePlayer()
        {
            for (ulong seed = 1; seed <= 8; seed++)
            {
                GameState s;
                var e = NewGame(out s, seed);
                PlayToYourPly(e, s, 5);
                PlantScreenshotBoard(s);

                MustApply(e, new HarvestCommand(Side.You), "seed " + seed + " harvest");
                MustApply(e, new DrawForTurnCommand(Side.You), "seed " + seed + " draw");

                DealScreenshotHand(s);
                s.P(Side.You).Mana = 30;

                var sites = ProbeLegalCells(e,
                    cell => new BuildStructureCommand(Side.You, new StructId("foundry"),
                                                      Element.None, cell),
                    "seed " + seed + " probe build sites");
                Assert.Greater(sites.Count, 0, "seed " + seed + " had nowhere to raise a Foundry");
                MustApply(e, new BuildStructureCommand(Side.You, new StructId("foundry"),
                    Element.None, sites[0]), "seed " + seed + " second Foundry");

                for (int h = 0; h < s.P(Side.You).Hand.Count; h++)
                {
                    string name = s.P(Side.You).Hand[h].Id.Value;
                    var cells = ProbeLegalCells(e,
                        cell => new PlayCardCommand(Side.You, h, PlayMode.Summon, cell),
                        "seed " + seed + " probe " + name);

                    foreach (var cell in cells)
                    {
                        var branch = s.Clone();
                        var be = new DuelEngine(branch, TestData.Catalog);
                        Apply(be, new PlayCardCommand(Side.You, h, PlayMode.Summon, cell),
                              "seed " + seed + " summon " + name + " -> " + cell);
                        AssertPendingIsAnswerable(be, branch,
                            "seed " + seed + " summon " + name + " -> " + cell);
                    }
                }
            }
        }

        // ── 3. the pathological economy: many Foundries, then a summon ───────────────────────

        /// <summary>
        /// The one thing a second Foundry demonstrably changes is the size of the back-row worker
        /// POOL (WorkerPool.Resync, Core/PlayerState.cs:95-105, driven from
        /// WorkerMath.Resync, Match/WorkerMath.cs:88-96). Push it far past two and summon anyway:
        /// if pool growth is where the loop lives, this is where it shows.
        /// </summary>
        [Test]
        public void AWholeBackRowOfFoundries_ThenASummon_StillTerminates()
        {
            GameState s;
            var e = NewGame(out s, 4242);
            PlayToYourPly(e, s, 5);

            for (int col = 0; col < Board.Columns; col++)
                Place(s, new CellRef(RowKey.YouBack, col), Side.You, "foundry", Element.None);
            for (int col = 0; col < Board.Columns; col += 2)
                Place(s, new CellRef(RowKey.Center, col), Side.You, "encampment", Element.None);
            WorkerMath.Resync(s, Side.You, TestData.Catalog);
            s.P(Side.You).ReadyWorkers();

            MustApply(e, new HarvestCommand(Side.You), "harvest a nine-structure economy");
            MustApply(e, new DrawForTurnCommand(Side.You), "draw");

            DealScreenshotHand(s);
            s.P(Side.You).Mana = 60;

            Assert.Greater(s.P(Side.You).Workers[(int)WorkerZone.Back].Count, 10,
                "the point of this test is a big pool");

            for (int h = 0; h < 3; h++)
            {
                var cells = ProbeLegalCells(e,
                    cell => new PlayCardCommand(Side.You, h, PlayMode.Summon, cell),
                    "probe with a huge worker pool");
                Assert.Greater(cells.Count, 0, "the front row is empty - something must be legal");

                var branch = s.Clone();
                var be = new DuelEngine(branch, TestData.Catalog);
                MustApply(be, new PlayCardCommand(Side.You, h, PlayMode.Summon, cells[0]),
                          "summon into a huge-pool board");
                AssertPendingIsAnswerable(be, branch, "huge-pool summon");
            }
        }

        // ── 4. every creature in the game, summoned onto that board ─────────────────────────

        /// <summary>
        /// The widest version of the report: it was "a monster", and the hand held seven of them,
        /// so rule out a per-card ENTER keyword (KeywordEngine.OnEnter via Triggers.cs:36) being
        /// the thing that loops. All 68 catalog creatures, each onto every cell the view would
        /// light, on the two-Foundry board.
        /// </summary>
        [Test]
        public void EveryCreatureInTheCatalog_SummonedOntoTheTwoFoundryBoard_Terminates()
        {
            GameState s;
            var e = NewGame(out s, 31337);
            PlayToYourPly(e, s, 5);
            PlantScreenshotBoard(s);

            MustApply(e, new HarvestCommand(Side.You), "harvest");
            MustApply(e, new DrawForTurnCommand(Side.You), "draw");
            s.P(Side.You).Mana = 60;
            MustApply(e, new BuildStructureCommand(Side.You, new StructId("foundry"), Element.None,
                new CellRef(RowKey.YouBack, 3)), "the second Foundry");

            var creatures = TestData.Catalog.Creatures;
            Assert.Greater(creatures.Count, 60, "the whole registry, not a fixture");

            for (int i = 0; i < creatures.Count; i++)
            {
                var card = creatures[i];
                var branch = s.Clone();
                var be = new DuelEngine(branch, TestData.Catalog);
                var p = branch.P(Side.You);
                p.Hand.Clear();
                p.Hand.Add(new HandCard(card.Id, card.Element));
                p.Mana = 60;

                var cells = ProbeLegalCells(be,
                    cell => new PlayCardCommand(Side.You, 0, PlayMode.Summon, cell),
                    "probe 35 cells for " + card.Name);
                Assert.Greater(cells.Count, 0, card.Name + " had nowhere legal to land");

                foreach (var cell in cells)
                {
                    var leaf = branch.Clone();
                    var le = new DuelEngine(leaf, TestData.Catalog);
                    var r = Apply(le, new PlayCardCommand(Side.You, 0, PlayMode.Summon, cell),
                                  "summon " + card.Name + " -> " + cell);
                    Assert.IsTrue(r.Applied, card.Name + " -> " + cell + ": " + r.Rejection);
                    AssertPendingIsAnswerable(le, leaf, "summon " + card.Name + " -> " + cell);
                }
            }
        }

        // ── 5. the summon-trap window, which IS the engine's one soft-lock shape ────────────

        /// <summary>
        /// PlayCardHandler.cs:147 -> Triggers.cs:37 -> Traps.OfferSummonWindow (Match/Traps.cs:100)
        /// parks a ResponseWindowRequest on the DEFENDER when they hold an armed summon trap.
        /// From that instant CommandProcessor.cs:55 refuses every command that is not the answer.
        /// In solo the defender is the AI and MatchController.Autopilot (View/MatchController.cs:610)
        /// answers it on the next 0.35 s beat - so this must hold: the window is parked on the FOE
        /// and the shipped policy answers it.
        /// </summary>
        [Test]
        public void SummonIntoAnArmedFoeTrap_ParksOnTheFoe_AndTheShippedPolicyAnswersIt()
        {
            GameState s;
            var e = NewGame(out s, 909);
            PlayToYourPly(e, s, 5);
            PlantScreenshotBoard(s);

            // an armed summon trap on the foe's side (set on an earlier turn, so it is armed)
            SpellCard trapDef;
            Assert.IsTrue(TestData.Catalog.TrySpell(new CardId("Snare Pit"), out trapDef),
                "the catalog must still hold the summon trap 'Snare Pit'");
            Assert.AreEqual(TrapTrigger.Summon, trapDef.Trigger);
            var trap = new TrapUnit
            {
                Id = s.NewUid(),
                Owner = Side.Foe,
                Color = Element.Water,
                SetIn = SlotName.Front,
                Card = trapDef.Id,
                Effect = trapDef.Effect,
                Value = trapDef.Value ?? 0,
                Trigger = trapDef.Trigger,
                SetTurn = 1,
            };
            s.Put(new CellRef(RowKey.FoeFront, 0), trap);

            MustApply(e, new HarvestCommand(Side.You), "harvest");
            MustApply(e, new DrawForTurnCommand(Side.You), "draw");
            DealScreenshotHand(s);
            s.P(Side.You).Mana = 9;

            MustApply(e, new BuildStructureCommand(Side.You, new StructId("foundry"), Element.None,
                new CellRef(RowKey.YouBack, 3)), "the second Foundry");

            var r = Apply(e, new PlayCardCommand(Side.You, 0, PlayMode.Summon,
                new CellRef(RowKey.YouFront, 3)), "summon Cinderling into an armed Snare Pit");
            Assert.IsTrue(r.Applied, "the summon itself was refused: " + r.Rejection);

            Assert.IsNotNull(s.Pending, "the armed Snare Pit should have parked a response window "
                + "- without one this test proves nothing");
            Assert.AreEqual(PendingKind.ResponseWindow, s.Pending.Kind);
            Assert.AreEqual(Side.Foe, s.Pending.Responder,
                "a summon window must never be parked on the summoner");
            Assert.AreEqual(Rejection.ChoicePending,
                e.CanApply(new EndTurnCommand(Side.You)),
                "while it is parked the whole board is inert - this IS the soft-lock shape");
            AssertPendingIsAnswerable(e, s, "summon into an armed Snare Pit");

            // and the board must still accept commands afterwards
            Assert.AreEqual(Rejection.None, e.CanApply(new EndTurnCommand(Side.You)),
                "after the window resolves the turn must be playable again");
        }
    }
}
