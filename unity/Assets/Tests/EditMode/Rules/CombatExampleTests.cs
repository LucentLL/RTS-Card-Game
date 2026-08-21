using System.Collections.Generic;
using NUnit.Framework;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// The M8 gate: worked examples A and B from spec 03 s15, reproduced EXACTLY through the
    /// real engine - declarations, parked choices, the resolver step machine, and the sweep.
    /// Every card is the real registry card; every number below is from the spec.
    /// </summary>
    public class CombatExampleTests
    {
        private static DuelEngine Engine(out GameState s)
        {
            s = MatchSetup.NewMatch(TestData.Catalog,
                new CommanderId("fire"), new CommanderId("water"), 51, RulesOptions.JsParity);
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

        private static void AnswerBlocksWithHeuristic(DuelEngine e, GameState s)
        {
            var req = (BlockerRequest)s.Pending;
            var picks = AiPolicy.ChooseInterceptors(s, req);
            var r = e.Apply(new RespondCommand(req.Responder, new BlockersChosen(picks)));
            Assert.IsTrue(r.Applied, r.Rejection.ToString());
        }

        [Test]
        public void ExampleA_GangBlockedWallAssault_WithAbsorberAndUndertow()
        {
            GameState s;
            var e = Engine(out s);

            var a1 = Place(s, Side.You, "Ashfang", RowKey.YouFront, 2);    // 1500/1000 FS
            var a2 = Place(s, Side.You, "Magmaw", RowKey.YouFront, 3);     // 3000/2500 cost 6
            var b1 = Place(s, Side.Foe, "Mistling", RowKey.FoeFront, 0);   // 500/1000
            b1.Tapped = true;                                              // attacked last turn
            var b2 = Place(s, Side.Foe, "Rippler", RowKey.FoeBack, 4);     // 1000/1000
            var b3 = Place(s, Side.Foe, "Undertow", RowKey.Center, 1);     // 500/1500 kw undertow
            TapWorkers(s, Side.Foe);                                       // all already harvested

            // ── declaration 1: Ashfang at the wall ──────────────────────────────────────
            var r1 = e.Apply(new DeclareAttackCommand(Side.You,
                new CellRef(RowKey.YouFront, 2), a1.Id, new WallTarget(Side.Foe)));
            Assert.AreEqual(CommandStatus.AwaitingChoice, r1.Status);
            Assert.IsTrue(a1.Tapped, "the attacker taps at declaration, not at resolution");

            var req1 = (BlockerRequest)s.Pending;
            Assert.AreEqual(3, req1.Eligible.Length, "B3 (center), B1 (foeFront, tapped but "
                + "eligible - blocking ignores tapped), B2 (foeBack); tapped workers are not");
            Assert.AreEqual(b3.Id, req1.Eligible[0].UnitId, "crossed rows in travel order");
            Assert.AreEqual(b1.Id, req1.Eligible[1].UnitId);
            Assert.AreEqual(b2.Id, req1.Eligible[2].UnitId);

            AnswerBlocksWithHeuristic(e, s);       // no survivor of 1500 -> chump the two weakest
            Assert.IsTrue(b1.HasBlocked);
            Assert.IsTrue(b2.HasBlocked);
            Assert.IsFalse(b3.HasBlocked);

            // ── declaration 2: Magmaw at the wall - only B3 remains eligible ────────────
            var r2 = e.Apply(new DeclareAttackCommand(Side.You,
                new CellRef(RowKey.YouFront, 3), a2.Id, new WallTarget(Side.Foe)));
            Assert.AreEqual(CommandStatus.AwaitingChoice, r2.Status);
            var req2 = (BlockerRequest)s.Pending;
            Assert.AreEqual(1, req2.Eligible.Length, "one creature can never block two attackers");
            Assert.AreEqual(b3.Id, req2.Eligible[0].UnitId);
            AnswerBlocksWithHeuristic(e, s);
            Assert.IsTrue(b3.HasBlocked);

            // ── resolve: pair fight 1 parks the absorber choice with the ATTACKER ───────
            var r3 = e.Apply(new ResolveCombatCommand(Side.You));
            Assert.AreEqual(CommandStatus.AwaitingChoice, r3.Status);
            var absorb = (AbsorberRequest)s.Pending;
            Assert.AreEqual(Side.You, absorb.Responder);
            Assert.AreEqual(a1.Id, absorb.AttackerId);
            Assert.AreEqual(2, absorb.Blockers.Length);

            // mid-combat snapshot is complete and resumable: clone at the park, answer the
            // same choice on both, and the states stay byte-identical
            var clone = s.Clone();
            Assert.AreEqual(StateCodec.Hash(s), StateCodec.Hash(clone));

            var r4 = e.Apply(new RespondCommand(Side.You, new IndexChosen(0)));   // B1 absorbs
            Assert.AreEqual(CommandStatus.Applied, r4.Status, "resolution ran to completion");

            var cloneEngine = new DuelEngine(clone, TestData.Catalog);
            Assert.IsTrue(cloneEngine.Apply(new RespondCommand(Side.You, new IndexChosen(0))).Applied);
            Assert.AreEqual(StateCodec.Hash(s), StateCodec.Hash(clone),
                "replaying the same answer on the mid-combat clone reproduces the state exactly");

            // ── the spec's net result, line by line ─────────────────────────────────────
            Assert.AreEqual(10000, s.P(Side.Foe).Life,
                "a defended wall costs bodies, not life - zero wall damage landed");

            Assert.IsNull(s.At(new CellRef(RowKey.FoeFront, 0)), "Mistling absorbed 1500 and died");
            bool mistlingGraved = false;
            foreach (var g in s.P(Side.Foe).Grave)
                if (g.Name == "Mistling") mistlingGraved = true;
            Assert.IsTrue(mistlingGraved);

            // NOTE - the spec's own narrative here contains an arithmetic slip: its setup table
            // gives Rippler 1000 attack, but its tier walkthrough retaliates with "B2.a = 500"
            // (Mistling's attack) and concludes Ashfang survives at 500. Following the pair-fight
            // ALGORITHM with the real card stats: B1 dies to the 1500 FS blow, then B2's full
            // 1000 retaliation kills the 1000-hp Ashfang. Every blocker retaliates in full.
            Assert.IsNull(s.At(new CellRef(RowKey.YouFront, 2)),
                "Ashfang fell to Rippler's full 1000 retaliation");
            bool ashfangGraved = false;
            foreach (var g in s.P(Side.You).Grave)
                if (g.Name == "Ashfang") ashfangGraved = true;
            Assert.IsTrue(ashfangGraved);
            Assert.AreEqual(1000, b2.Hp, "only the absorber took the attacker's blow");
            Assert.IsTrue(b2.Tapped, "blocking taps the blocker");

            Assert.IsNull(s.At(new CellRef(RowKey.YouFront, 3)),
                "Undertow hurled Magmaw off the board before any damage");
            bool magmawInHand = false;
            foreach (var h in s.P(Side.You).Hand)
                if (h.Id.Value == "Magmaw") magmawInHand = true;
            Assert.IsTrue(magmawInHand, "the bounced card returns to hand at full printed HP");
            Assert.AreEqual(1500, b3.Hp, "the warden's fight ended before any damage");
            Assert.IsTrue(b3.Tapped, "it still spent its block");

            Assert.IsFalse(s.Combat.HasDeclarations);
            Assert.AreEqual(CombatStage.Idle, s.Combat.Stage);
            Assert.IsFalse(s.IsOver);
        }

        [Test]
        public void ExampleA_DeferredCadence_SameOutcome_WithTheFullAssaultVisible()
        {
            // The s12 mirrored protocol: both declarations land BEFORE any blocker answer;
            // the defender then answers per declaration at resolve time, seeing the complete
            // assault. Same heuristic answers - identical net outcome to the alternating flow.
            GameState s;
            var e = Engine(out s);

            var a1 = Place(s, Side.You, "Ashfang", RowKey.YouFront, 2);
            var a2 = Place(s, Side.You, "Magmaw", RowKey.YouFront, 3);
            var b1 = Place(s, Side.Foe, "Mistling", RowKey.FoeFront, 0);
            b1.Tapped = true;
            var b2 = Place(s, Side.Foe, "Rippler", RowKey.FoeBack, 4);
            var b3 = Place(s, Side.Foe, "Undertow", RowKey.Center, 1);
            TapWorkers(s, Side.Foe);

            Assert.AreEqual(CommandStatus.Applied, e.Apply(new DeclareAttackCommand(Side.You,
                new CellRef(RowKey.YouFront, 2), a1.Id, new WallTarget(Side.Foe), true)).Status,
                "deferred - no immediate blocker window");
            Assert.AreEqual(CommandStatus.Applied, e.Apply(new DeclareAttackCommand(Side.You,
                new CellRef(RowKey.YouFront, 3), a2.Id, new WallTarget(Side.Foe), true)).Status);

            var r = e.Apply(new ResolveCombatCommand(Side.You));
            Assert.AreEqual(CommandStatus.AwaitingChoice, r.Status);
            var req1 = (BlockerRequest)s.Pending;
            Assert.AreEqual(2, s.Combat.Declarations.Count,
                "the defender answers seeing BOTH declarations");
            Assert.AreEqual(0, req1.DeclarationIndex);
            AnswerBlocksWithHeuristic(e, s);                   // [B1, B2] for Ashfang

            var req2 = (BlockerRequest)s.Pending;
            Assert.AreEqual(1, req2.DeclarationIndex, "then the second declaration's answer");
            Assert.AreEqual(1, req2.Eligible.Length, "the HasBlocked cascade held: only B3 left");
            AnswerBlocksWithHeuristic(e, s);                   // [B3] for Magmaw

            var absorb = (AbsorberRequest)s.Pending;           // straight into the pair fights
            Assert.AreEqual(Side.You, absorb.Responder);
            Assert.IsTrue(e.Apply(new RespondCommand(Side.You, new IndexChosen(0))).Applied);

            // identical net outcome to the alternating flow
            Assert.AreEqual(10000, s.P(Side.Foe).Life);
            Assert.IsNull(s.At(new CellRef(RowKey.YouFront, 3)), "Magmaw bounced");
            Assert.IsNull(s.At(new CellRef(RowKey.FoeFront, 0)), "Mistling died absorbing");
            Assert.IsNull(s.At(new CellRef(RowKey.YouFront, 2)), "Ashfang fell to Rippler's 1000");
            Assert.AreEqual(CombatStage.Idle, s.Combat.Stage);
        }

        [Test]
        public void ExampleB_SameRowJointAttack_Uninterposable_WithOvergrowth()
        {
            GameState s;
            var e = Engine(out s);

            // the player is raiding: two creatures already stand in the foe's front row
            var a1 = Place(s, Side.You, "Ashfang", RowKey.FoeFront, 2);      // 1500/1000 FS
            var a2 = Place(s, Side.You, "Cinderling", RowKey.FoeFront, 4);   // 1000/1000
            var t = Place(s, Side.Foe, "Surgeling", RowKey.FoeFront, 6);     // 2000/1500

            var trap = new TrapUnit();
            trap.Id = s.NewUid();
            trap.Owner = Side.Foe;
            trap.Color = Element.Water;
            trap.SetIn = SlotName.Back;
            trap.Card = new CardId("Overgrowth");
            trap.Effect = SpellEffect.Thornmail;
            trap.Trigger = TrapTrigger.Attack;
            trap.SetTurn = 0;                                  // set before this turn - armed
            s.Put(new CellRef(RowKey.FoeBack, 1), trap);

            // same row: rowsCrossedInto is empty - no blockers may be declared by anyone
            var r1 = e.Apply(new DeclareAttackCommand(Side.You, new CellRef(RowKey.FoeFront, 2),
                a1.Id, new UnitTarget(new CellRef(RowKey.FoeFront, 6), t.Id)));
            Assert.AreEqual(CommandStatus.Applied, r1.Status, "uninterposable - no blocker window");
            var r2 = e.Apply(new DeclareAttackCommand(Side.You, new CellRef(RowKey.FoeFront, 4),
                a2.Id, new UnitTarget(new CellRef(RowKey.FoeFront, 6), t.Id)));
            Assert.AreEqual(CommandStatus.Applied, r2.Status);

            Assert.AreEqual(Rejection.DeclarationsPending, e.CanApply(new EndTurnCommand(Side.You)),
                "end-turn is blocked while declarations are pending");

            // resolve: the joint group parks the retaliation pick with the DEFENDER
            var r3 = e.Apply(new ResolveCombatCommand(Side.You));
            Assert.AreEqual(CommandStatus.AwaitingChoice, r3.Status);

            Assert.AreEqual(2500, t.Attack, "Overgrowth sprang first: +500 attack, permanent");
            Assert.AreEqual(2500, t.Hp, "+1000 hp and max");
            Assert.IsNull(s.At(new CellRef(RowKey.FoeBack, 1)), "the trap is spent");

            var ret = (RetaliationRequest)s.Pending;
            Assert.AreEqual(Side.Foe, ret.Responder);
            Assert.AreEqual(t.Id, ret.DefenderId);
            Assert.AreEqual(2, ret.Attackers.Length);

            // the JS AI defender never chose - it always ate index 0 (the first declared)
            var r4 = e.Apply(new RespondCommand(Side.Foe, new IndexChosen(0)));
            Assert.AreEqual(CommandStatus.Applied, r4.Status);

            // net result: T and A1 trade; A2 untouched - retaliation hit exactly one attacker
            Assert.IsNull(s.At(new CellRef(RowKey.FoeFront, 2)), "Ashfang fell to the 2500 blow");
            bool ashfangInYourGrave = false;
            foreach (var g in s.P(Side.You).Grave)
                if (g.Name == "Ashfang") ashfangInYourGrave = true;
            Assert.IsTrue(ashfangInYourGrave,
                "graves attribute by the unit's own owner, not by whose row it stood in");

            Assert.IsNull(s.At(new CellRef(RowKey.FoeFront, 6)),
                "1500 FS + 1000 main broke the buffed 2500");
            bool surgelingGraved = false, overgrowthGraved = false;
            foreach (var g in s.P(Side.Foe).Grave)
            {
                if (g.Name == "Surgeling") surgelingGraved = true;
                if (g.Name == "Overgrowth") overgrowthGraved = true;
            }
            Assert.IsTrue(surgelingGraved);
            Assert.IsTrue(overgrowthGraved);

            Assert.AreEqual(1000, a2.Hp, "Cinderling untouched");
            Assert.AreEqual(10000, s.P(Side.Foe).Life);
            Assert.AreEqual(CombatStage.Idle, s.Combat.Stage);
        }
    }
}
