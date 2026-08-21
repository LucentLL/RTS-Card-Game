using System.Collections.Generic;
using NUnit.Framework;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// M8 unit coverage around the worked examples: attacker gates, blocker eligibility's
    /// inverted predicates, Scour bypass and strike, wall aggregation and the win check,
    /// worker-stack strikes through the legacy engine, and attacked traps / face-downs.
    /// </summary>
    public class CombatRulesTests
    {
        private static DuelEngine Engine(out GameState s)
        {
            s = MatchSetup.NewMatch(TestData.Catalog,
                new CommanderId("fire"), new CommanderId("water"), 61, RulesOptions.JsParity);
            var e = new DuelEngine(s, TestData.Catalog);
            Assert.IsTrue(e.Apply(new HarvestCommand(Side.You)).Applied);
            Assert.IsTrue(e.Apply(new DrawForTurnCommand(Side.You)).Applied);
            return e;
        }

        private static CreatureUnit Place(GameState s, Side side, string name, RowKey row, int col)
        {
            var c = UnitFactory.MakeCreature(s, side,
                TestData.Catalog.Creature(new CardId(name)), Element.None);
            s.Put(new CellRef(row, col), c);
            return c;
        }

        private static void TapWorkers(GameState s, Side side)
        {
            foreach (var pool in s.P(side).Workers)
                foreach (var w in pool.Members) w.Tapped = true;
        }

        private static void PassBlocks(DuelEngine e, GameState s)
        {
            var req = (BlockerRequest)s.Pending;
            Assert.IsTrue(e.Apply(new RespondCommand(req.Responder,
                new BlockersChosen(new UnitRef[0]))).Applied);
        }

        [Test]
        public void Declare_GatesOnSickTappedAndIdentity()
        {
            GameState s;
            var e = Engine(out s);
            var a = Place(s, Side.You, "Cinderling", RowKey.YouFront, 2);
            var t = Place(s, Side.Foe, "Mistling", RowKey.FoeFront, 3);

            a.Sick = true;
            Assert.AreEqual(Rejection.AttackerSick, e.CanApply(new DeclareAttackCommand(Side.You,
                new CellRef(RowKey.YouFront, 2), a.Id,
                new UnitTarget(new CellRef(RowKey.FoeFront, 3), t.Id))));

            a.Sick = false;
            a.Tapped = true;
            Assert.AreEqual(Rejection.AttackerTapped, e.CanApply(new DeclareAttackCommand(Side.You,
                new CellRef(RowKey.YouFront, 2), a.Id,
                new UnitTarget(new CellRef(RowKey.FoeFront, 3), t.Id))));

            a.Tapped = false;
            Assert.AreEqual(Rejection.NoSuchUnit, e.CanApply(new DeclareAttackCommand(Side.You,
                new CellRef(RowKey.YouFront, 2), 99999,
                new UnitTarget(new CellRef(RowKey.FoeFront, 3), t.Id))),
                "identity travels with the declaration");

            Assert.AreEqual(Rejection.TargetNotEnemy, e.CanApply(new DeclareAttackCommand(Side.You,
                new CellRef(RowKey.YouFront, 2), a.Id,
                new UnitTarget(new CellRef(RowKey.YouFront, 2), a.Id))));
        }

        [Test]
        public void Blockers_TappedAndSickCreaturesMay_WorkersMustBeReady()
        {
            GameState s;
            var e = Engine(out s);
            var a = Place(s, Side.You, "Cinderling", RowKey.YouBack, 2);
            var t = Place(s, Side.Foe, "Mistling", RowKey.FoeBack, 3);

            var tappedBlk = Place(s, Side.Foe, "Rippler", RowKey.FoeFront, 1);
            tappedBlk.Tapped = true;
            var sickBlk = Place(s, Side.Foe, "Brinekin", RowKey.Center, 3);
            sickBlk.Sick = true;

            // foe back-zone workers are READY at match start - they screen their row
            var r = e.Apply(new DeclareAttackCommand(Side.You, new CellRef(RowKey.YouBack, 2),
                a.Id, new UnitTarget(new CellRef(RowKey.FoeBack, 3), t.Id)));
            Assert.AreEqual(CommandStatus.AwaitingChoice, r.Status);

            var req = (BlockerRequest)s.Pending;
            var ids = new List<int>();
            int poolRefs = 0;
            foreach (var re in req.Eligible)
            {
                ids.Add(re.UnitId);
                if (re.IsPool) poolRefs++;
            }
            Assert.Contains(tappedBlk.Id, ids, "tapped board creatures MAY block");
            Assert.Contains(sickBlk.Id, ids, "summoning-sick board creatures MAY block");
            Assert.AreEqual(3, poolRefs, "the foe's three ready back workers all screen the row");

            // ...but tapped workers may not: pass here, resolve, then re-declare at a fresh
            // target after tapping the pools (the first fight killed Mistling)
            PassBlocks(e, s);
            Assert.IsTrue(e.Apply(new ResolveCombatCommand(Side.You)).Applied);

            TapWorkers(s, Side.Foe);
            var t2 = Place(s, Side.Foe, "Tidecaller", RowKey.FoeBack, 5);
            var a2 = Place(s, Side.You, "Sparkimp", RowKey.YouBack, 4);
            var r2 = e.Apply(new DeclareAttackCommand(Side.You, new CellRef(RowKey.YouBack, 4),
                a2.Id, new UnitTarget(new CellRef(RowKey.FoeBack, 5), t2.Id)));
            Assert.AreEqual(CommandStatus.AwaitingChoice, r2.Status,
                "the tapped Rippler and sick Brinekin still make a blocker window");
            var req2 = (BlockerRequest)s.Pending;
            int pool2 = 0;
            foreach (var re in req2.Eligible) if (re.IsPool) pool2++;
            Assert.AreEqual(0, pool2, "tapped workers cannot block");
        }

        [Test]
        public void Scour_IgnoresAllInterceptors_AndShattersTheBackRowOnAWallHit()
        {
            GameState s;
            var e = Engine(out s);
            TapWorkers(s, Side.Foe);
            var scour = Place(s, Side.You, "Zephyr", RowKey.YouFront, 2);   // wind, kw scour
            Place(s, Side.Foe, "Rippler", RowKey.FoeFront, 3);              // would-be blocker

            var charge = new ChargeUnit();
            charge.Id = s.NewUid();
            charge.Owner = Side.Foe;
            charge.Color = Element.Water;
            charge.SetIn = SlotName.Back;
            charge.Card = new CardSnapshot(new CardId("Mistling"), "Mistling", Element.Water,
                1, 500, 1000, 1, Keyword.None, false, false, StructId.None);
            charge.Invested = 1;
            charge.SetTurn = 0;
            s.Put(new CellRef(RowKey.FoeBack, 5), charge);

            var r = e.Apply(new DeclareAttackCommand(Side.You, new CellRef(RowKey.YouFront, 2),
                scour.Id, new WallTarget(Side.Foe)));
            Assert.AreEqual(CommandStatus.Applied, r.Status, "fliers are unblockable - no window");

            Assert.IsTrue(e.Apply(new ResolveCombatCommand(Side.You)).Applied);
            Assert.AreEqual(10000 - scour.Attack, s.P(Side.Foe).Life, "the wall took the hit");
            Assert.IsNull(s.At(new CellRef(RowKey.FoeBack, 5)),
                "the connecting Scour strike shattered the face-down in the back row");
        }

        [Test]
        public void Wall_DamageAggregates_AndTheWinCheckFires()
        {
            GameState s;
            var e = Engine(out s);
            TapWorkers(s, Side.Foe);
            s.P(Side.Foe).Life = 2500;

            var a1 = Place(s, Side.You, "Ashfang", RowKey.YouFront, 2);     // 1500
            var a2 = Place(s, Side.You, "Cinderling", RowKey.YouFront, 4);  // 1000

            Assert.IsTrue(e.Apply(new DeclareAttackCommand(Side.You, new CellRef(RowKey.YouFront, 2),
                a1.Id, new WallTarget(Side.Foe))).Applied);
            Assert.IsTrue(e.Apply(new DeclareAttackCommand(Side.You, new CellRef(RowKey.YouFront, 4),
                a2.Id, new WallTarget(Side.Foe))).Applied);

            Assert.IsTrue(e.Apply(new ResolveCombatCommand(Side.You)).Applied);

            Assert.AreEqual(0, s.P(Side.Foe).Life, "1500 + 1000, summed and applied once");
            Assert.IsTrue(s.IsOver);
            Assert.AreEqual(MatchOutcome.YouWin, s.Outcome);

            bool ended = false;
            foreach (var ev in e.DrainEvents()) if (ev is MatchEnded) ended = true;
            Assert.IsTrue(ended);

            Assert.AreEqual(Rejection.GameOver, e.CanApply(new EndTurnCommand(Side.You)));
        }

        [Test]
        public void BlockedAttacker_ContributesNoWallDamage_EvenAfterKillingItsGang()
        {
            GameState s;
            var e = Engine(out s);
            TapWorkers(s, Side.Foe);

            var a = Place(s, Side.You, "Magmaw", RowKey.YouFront, 3);       // 3000
            var blk = Place(s, Side.Foe, "Mistling", RowKey.FoeFront, 0);   // 500/1000

            Assert.AreEqual(CommandStatus.AwaitingChoice,
                e.Apply(new DeclareAttackCommand(Side.You, new CellRef(RowKey.YouFront, 3),
                    a.Id, new WallTarget(Side.Foe))).Status);
            var req = (BlockerRequest)s.Pending;
            Assert.IsTrue(e.Apply(new RespondCommand(Side.Foe, new BlockersChosen(
                new[] { UnitRef.Cell(new CellRef(RowKey.FoeFront, 0), blk.Id) }))).Applied);

            Assert.IsTrue(e.Apply(new ResolveCombatCommand(Side.You)).Applied);

            Assert.IsNull(s.At(new CellRef(RowKey.FoeFront, 0)), "the blocker died for it");
            Assert.AreEqual(10000, s.P(Side.Foe).Life,
                "the partition is computed once, before any damage - a blocked attacker "
                + "contributes zero wall damage even after killing its whole gang");
            Assert.AreEqual(2000, a.Hp, "it took the blocker's 500 retaliation on the way");
        }

        [Test]
        public void WorkerStackStrike_OneWorkerSoaks_NoneRetaliate()
        {
            GameState s;
            var e = Engine(out s);
            var a = Place(s, Side.You, "Cinderling", RowKey.YouBack, 2);    // 1000 attack

            // clear the crossed rows of blockers: foe back pool is the TARGET (self-screen
            // excluded); foe front/center pools are empty at match start
            var r = e.Apply(new DeclareAttackCommand(Side.You, new CellRef(RowKey.YouBack, 2),
                a.Id, new WorkerStackTarget(Side.Foe, WorkerZone.Back)));
            Assert.AreEqual(CommandStatus.Applied, r.Status,
                "a stack cannot screen itself and nothing else stands in the way");

            int before = s.P(Side.Foe).Workers[(int)WorkerZone.Back].Count;
            Assert.AreEqual(3, before);

            Assert.IsTrue(e.Apply(new ResolveCombatCommand(Side.You)).Applied);

            Assert.AreEqual(2, s.P(Side.Foe).Workers[(int)WorkerZone.Back].Count,
                "focusFire kills exactly one 1000-hp worker with the 1000 blow");
            Assert.AreEqual(1000, a.Hp, "workers deal no retaliation damage");
            bool workerGraved = false;
            foreach (var g in s.P(Side.Foe).Grave) if (g.IsWorker) workerGraved = true;
            Assert.IsTrue(workerGraved, "raided workers grave as villagers (and regrow next sync)");
        }

        [Test]
        public void AttackedTrap_Springs_PitfallDestroysTheStrongestAttacker()
        {
            GameState s;
            var e = Engine(out s);

            var a = Place(s, Side.You, "Ashfang", RowKey.FoeBack, 2);       // raiding, 1500 raw

            var trap = new TrapUnit();
            trap.Id = s.NewUid();
            trap.Owner = Side.Foe;
            trap.Color = Element.Water;
            trap.SetIn = SlotName.Back;
            trap.Card = new CardId("Snare Pit");
            trap.Effect = SpellEffect.Pitfall;
            trap.Trigger = TrapTrigger.Summon;
            trap.SetTurn = 0;
            s.Put(new CellRef(RowKey.FoeBack, 5), trap);

            // same row - uninterposable
            Assert.IsTrue(e.Apply(new DeclareAttackCommand(Side.You, new CellRef(RowKey.FoeBack, 2),
                a.Id, new UnitTarget(new CellRef(RowKey.FoeBack, 5), trap.Id))).Applied);
            Assert.IsTrue(e.Apply(new ResolveCombatCommand(Side.You)).Applied);

            Assert.IsNull(s.At(new CellRef(RowKey.FoeBack, 5)), "the trap is spent regardless");
            Assert.IsNull(s.At(new CellRef(RowKey.FoeBack, 2)), "pitfall destroyed the attacker outright");
            bool graved = false;
            foreach (var g in s.P(Side.You).Grave) if (g.Name == "Ashfang") graved = true;
            Assert.IsTrue(graved);
        }

        [Test]
        public void ProvokedFaceDown_Underfunded_DiesHalfFormed_NobodyTakesDamage()
        {
            GameState s;
            var e = Engine(out s);
            var a = Place(s, Side.You, "Cinderling", RowKey.FoeBack, 2);

            var charge = new ChargeUnit();
            charge.Id = s.NewUid();
            charge.Owner = Side.Foe;
            charge.Color = Element.Water;
            charge.SetIn = SlotName.Back;
            charge.Card = new CardSnapshot(new CardId("Rippler"), "Rippler", Element.Water,
                2, 1000, 1000, 1, Keyword.None, false, false, StructId.None);
            charge.Invested = 1;                               // 1 < cost 2 - half-formed
            charge.SetTurn = 0;
            s.Put(new CellRef(RowKey.FoeBack, 5), charge);

            Assert.IsTrue(e.Apply(new DeclareAttackCommand(Side.You, new CellRef(RowKey.FoeBack, 2),
                a.Id, new UnitTarget(new CellRef(RowKey.FoeBack, 5), charge.Id))).Applied);
            Assert.IsTrue(e.Apply(new ResolveCombatCommand(Side.You)).Applied);

            Assert.IsNull(s.At(new CellRef(RowKey.FoeBack, 5)), "interrupted - the investment is lost");
            Assert.AreEqual(1000, a.Hp, "the attacker neither deals nor takes damage");
        }

        [Test]
        public void ScourCredit_Survives_TheTargetDyingDuringResolution()
        {
            // The JS captured target OBJECTS at resolve start, so a Scour attacker whose target
            // died in the group fight still got its on-hit strike in the misc step. (Audit
            // finding: by-id resolution silently dropped it.)
            GameState s;
            var e = Engine(out s);
            TapWorkers(s, Side.Foe);

            var zephyr = Place(s, Side.You, "Talonwind", RowKey.FoeFront, 2); // 2500/1000 scour - survives the 500 retaliation
            var t = Place(s, Side.Foe, "Mistling", RowKey.FoeFront, 5);      // 500/1000 - dies

            var charge = new ChargeUnit();
            charge.Id = s.NewUid();
            charge.Owner = Side.Foe;
            charge.Color = Element.Water;
            charge.SetIn = SlotName.Back;
            charge.Card = new CardSnapshot(new CardId("Rippler"), "Rippler", Element.Water,
                2, 1000, 1000, 1, Keyword.None, false, false, StructId.None);
            charge.Invested = 2;
            charge.SetTurn = 0;
            s.Put(new CellRef(RowKey.FoeBack, 3), charge);

            Assert.IsTrue(e.Apply(new DeclareAttackCommand(Side.You, new CellRef(RowKey.FoeFront, 2),
                zephyr.Id, new UnitTarget(new CellRef(RowKey.FoeFront, 5), t.Id))).Applied,
                "scour is unblockable - no window even cross-row");
            Assert.IsTrue(e.Apply(new ResolveCombatCommand(Side.You)).Applied);

            Assert.IsNull(s.At(new CellRef(RowKey.FoeFront, 5)), "the target died in the group fight");
            Assert.IsNull(s.At(new CellRef(RowKey.FoeBack, 3)),
                "the connecting strike's Scour credit survived the target's death - the "
                + "face-down in the back row shattered");
        }

        [Test]
        public void BacklashKilledAttacker_StillLandsItsBlowOnTheBuilding()
        {
            // The JS building branch strikes with NO alive re-check after the trap springs -
            // the dying attacker's blow lands anyway. (Audit finding: an invented Hp guard.)
            GameState s;
            var e = Engine(out s);

            var a = Place(s, Side.You, "Cinderling", RowKey.FoeBack, 2);     // 1000/1000

            var foundry = UnitFactory.MakeStructure(s, Side.Foe,
                TestData.Catalog.Structure(new StructId("foundry"), Element.None));
            s.Put(new CellRef(RowKey.FoeBack, 6), foundry);                  // 3000 hp

            var trap = new TrapUnit();
            trap.Id = s.NewUid();
            trap.Owner = Side.Foe;
            trap.Color = Element.Water;
            trap.SetIn = SlotName.Front;
            trap.Card = new CardId("Backlash");
            trap.Effect = SpellEffect.Burn;
            trap.Trigger = TrapTrigger.Attack;
            trap.Value = 1500;
            trap.SetTurn = 0;
            s.Put(new CellRef(RowKey.FoeFront, 0), trap);

            Assert.IsTrue(e.Apply(new DeclareAttackCommand(Side.You, new CellRef(RowKey.FoeBack, 2),
                a.Id, new UnitTarget(new CellRef(RowKey.FoeBack, 6), foundry.Id))).Applied);
            Assert.AreEqual(CommandStatus.AwaitingChoice,
                e.Apply(new ResolveCombatCommand(Side.You)).Status,
                "the defender is offered Backlash at the building's spring site");
            var win = (ResponseWindowRequest)s.Pending;
            Assert.AreEqual(foundry.Id, win.Subject.UnitId);
            Assert.IsTrue(e.Apply(new RespondCommand(Side.Foe, new TrapChosen(win.ArmedTraps[0]))).Applied);

            Assert.IsNull(s.At(new CellRef(RowKey.FoeBack, 2)), "Backlash's 1500 killed the attacker");
            Assert.AreEqual(2000, foundry.Hp, "its 1000 blow landed anyway - no alive re-check");
        }

        [Test]
        public void ProvokedFaceDown_Funded_FlipsBattleReady_AndFightsBack()
        {
            GameState s;
            var e = Engine(out s);
            var a = Place(s, Side.You, "Cinderling", RowKey.FoeBack, 2);    // 1000/1000

            var charge = new ChargeUnit();
            charge.Id = s.NewUid();
            charge.Owner = Side.Foe;
            charge.Color = Element.Water;
            charge.SetIn = SlotName.Back;
            charge.Card = new CardSnapshot(new CardId("Mistling"), "Mistling", Element.Water,
                1, 500, 1000, 1, Keyword.None, false, false, StructId.None);
            charge.Invested = 3;                               // funded, +2 surplus banks
            charge.SetTurn = 0;                                // set an earlier turn: battle-ready
            s.Put(new CellRef(RowKey.FoeBack, 5), charge);

            Assert.IsTrue(e.Apply(new DeclareAttackCommand(Side.You, new CellRef(RowKey.FoeBack, 2),
                a.Id, new UnitTarget(new CellRef(RowKey.FoeBack, 5), charge.Id))).Applied);
            Assert.IsTrue(e.Apply(new ResolveCombatCommand(Side.You)).Applied);

            Assert.IsNull(s.At(new CellRef(RowKey.FoeBack, 5)),
                "the flipped Mistling died to the 1000 blow (simultaneous legacy exchange)");
            bool mistlingGraved = false;
            foreach (var g in s.P(Side.Foe).Grave) if (g.Name == "Mistling") mistlingGraved = true;
            Assert.IsTrue(mistlingGraved);
            Assert.AreEqual(500, a.Hp, "it fought back at full strength on the way down");
        }

        [Test]
        public void RazedTargetSpringSite_StillSweeps_SoADeathKeywordFiresInsideTheCombat()
        {
            // Two declarations on one structure: the first razes it, the second lands on the
            // corpse and springs a second trap that kills its attacker. The JS sweeps at that
            // site (15_combat.js:352), so the dying attacker graves and its Reap fires DURING
            // the resolution instead of leaving a 0-hp body squatting on its cell.
            GameState s;
            var e = Engine(out s);

            var a = Place(s, Side.You, "Cinderling", RowKey.FoeBack, 2);      // 1000/1000
            var b = Place(s, Side.You, "Grimfang", RowKey.FoeBack, 3);        // 1500/500, reap 500

            var forge = UnitFactory.MakeStructure(s, Side.Foe,
                TestData.Catalog.Structure(new StructId("foundry"), Element.None));
            forge.Hp = 500;                                    // frail enough for one blow
            s.Put(new CellRef(RowKey.FoeBack, 6), forge);

            foreach (var col in new[] { 0, 1 })                // TWO armed Backlashes
            {
                var t = new TrapUnit();
                t.Id = s.NewUid();
                t.Owner = Side.Foe;
                t.Color = Element.Water;
                t.SetIn = SlotName.Front;
                t.Card = new CardId("Backlash");
                t.Effect = SpellEffect.Burn;
                t.Trigger = TrapTrigger.Attack;
                t.Value = 1500;
                t.SetTurn = 0;
                s.Put(new CellRef(RowKey.FoeFront, col), t);
            }

            // same row as the target: uninterposable, so no blocker windows
            Assert.IsTrue(e.Apply(new DeclareAttackCommand(Side.You, new CellRef(RowKey.FoeBack, 2),
                a.Id, new UnitTarget(new CellRef(RowKey.FoeBack, 6), forge.Id))).Applied);
            Assert.IsTrue(e.Apply(new DeclareAttackCommand(Side.You, new CellRef(RowKey.FoeBack, 3),
                b.Id, new UnitTarget(new CellRef(RowKey.FoeBack, 6), forge.Id))).Applied);

            Assert.AreEqual(CommandStatus.AwaitingChoice,
                e.Apply(new ResolveCombatCommand(Side.You)).Status);
            var w1 = (ResponseWindowRequest)s.Pending;
            Assert.AreEqual(CommandStatus.AwaitingChoice,
                e.Apply(new RespondCommand(Side.Foe, new TrapChosen(w1.ArmedTraps[0]))).Status,
                "the razed-corpse site offers the second trap");

            var w2 = (ResponseWindowRequest)s.Pending;
            Assert.IsTrue(e.Apply(new RespondCommand(Side.Foe,
                new TrapChosen(w2.ArmedTraps[0]))).Applied);

            Assert.IsNull(s.At(new CellRef(RowKey.FoeBack, 6)), "the structure was razed");
            Assert.IsNull(s.At(new CellRef(RowKey.FoeBack, 3)),
                "Backlash killed the second attacker and the site swept it");

            foreach (var kv in s.Objects())
            {
                var c = kv.Value as CreatureUnit;
                Assert.IsFalse(c != null && c.Hp <= 0,
                    "no 0-hp corpse is left holding a cell for the rest of the turn");
            }

            CreatureUnit shade = null;
            foreach (var kv in s.ObjectsOf(Side.You))
            {
                var c = kv.Value as CreatureUnit;
                if (c != null && c.IsToken) shade = c;
            }
            Assert.IsNotNull(shade, "its Reap fired inside the combat, as the JS sweep makes it");
            Assert.AreEqual("Shade", shade.Name);
        }

        [Test]
        public void AnUndertowBouncedFlier_StillShattersTheBackRow()
        {
            // The JS walks CAPTURED attacker objects, so a Scour flier hurled back to hand during
            // the fight still passes `x.A.h>0`, still collects its credit, and still shatters a
            // card at step 9 - a strike delivered from hand. Its own bug; parity is the contract.
            GameState s;
            var e = Engine(out s);

            var flier = Place(s, Side.You, "Zephyr", RowKey.FoeFront, 2);     // 1500/500, Scour
            var warden = Place(s, Side.Foe, "Undertow", RowKey.FoeFront, 4);  // bounces the mark

            var charge = new ChargeUnit();                                    // the shatter victim
            charge.Id = s.NewUid();
            charge.Owner = Side.Foe;
            charge.Color = Element.Water;
            charge.SetIn = SlotName.Back;
            charge.Card = new CardSnapshot(new CardId("Mistling"), "Mistling", Element.Water,
                1, 500, 1000, 1, Keyword.None, false, false, StructId.None);
            charge.Invested = 1;
            charge.SetTurn = 0;
            var chargeAt = new CellRef(RowKey.FoeBack, 3);
            s.Put(chargeAt, charge);

            // Scour is never offered to blockers, so this declaration needs no answer
            Assert.IsTrue(e.Apply(new DeclareAttackCommand(Side.You, new CellRef(RowKey.FoeFront, 2),
                flier.Id, new UnitTarget(new CellRef(RowKey.FoeFront, 4), warden.Id))).Applied);
            Assert.IsTrue(e.Apply(new ResolveCombatCommand(Side.You)).Applied);

            Assert.IsNull(s.At(new CellRef(RowKey.FoeFront, 2)), "Undertow hurled the flier back");
            var card = s.P(Side.You).Hand[s.P(Side.You).Hand.Count - 1];
            Assert.AreEqual(new CardId("Zephyr"), card.Id);
            Assert.AreEqual(1500, warden.Hp, "the bounced attacker dealt no damage");

            Assert.IsNull(s.At(chargeAt),
                "and it shattered the back row anyway - from its owner's hand");
        }
    }
}
