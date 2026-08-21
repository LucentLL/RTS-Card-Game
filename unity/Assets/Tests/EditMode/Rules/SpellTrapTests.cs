using System.Collections.Generic;
using NUnit.Framework;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// M10: the four castable spell effects, the one target-legality predicate, and the summon
    /// trap's response window (spec 06 s7). Attack-trigger traps live with the combat tests,
    /// where the resolver that offers them is exercised.
    /// </summary>
    public class SpellTrapTests
    {
        static DuelEngine Engine(out GameState s)
        {
            s = MatchSetup.NewMatch(TestData.Catalog,
                new CommanderId("fire"), new CommanderId("water"), 41, RulesOptions.JsParity);
            return new DuelEngine(s, TestData.Catalog);
        }

        static void ToAction(DuelEngine e, GameState s)
        {
            Assert.IsTrue(e.Apply(new HarvestCommand(Side.You)).Applied);
            Assert.IsTrue(e.Apply(new DrawForTurnCommand(Side.You)).Applied);
            Assert.AreEqual(TurnPhase.Action, s.Phase);
        }

        static CreatureUnit Place(GameState s, Side side, string name, RowKey row, int col)
        {
            var c = UnitFactory.MakeCreature(s, side,
                TestData.Catalog.Creature(new CardId(name)), Element.None);
            s.Put(new CellRef(row, col), c);
            return c;
        }

        static int GiveCard(GameState s, Side side, string name)
        {
            s.P(side).Hand.Add(new HandCard(new CardId(name), Element.None));
            return s.P(side).Hand.Count - 1;
        }

        static TrapUnit SetTrap(GameState s, Side side, string card, CellRef at, int setTurn)
        {
            var spell = TestData.Catalog.Spell(new CardId(card));
            var t = new TrapUnit();
            t.Id = s.NewUid();
            t.Owner = side;
            t.Color = Element.None;
            t.SetIn = Board.WhichOf(at.Row);
            t.Card = spell.Id;
            t.Effect = spell.Effect;
            t.Value = spell.Value ?? 0;
            t.Trigger = spell.Trigger;
            t.SetTurn = setTurn;
            s.Put(at, t);
            return t;
        }

        // ── burn ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Burn_DamagesACreature_AndSweepsItIfLethal()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            var t = Place(s, Side.Foe, "Mistling", RowKey.FoeFront, 3);      // 500/1000
            int idx = GiveCard(s, Side.You, "Ember Bolt");                   // burn 1500, ◆2

            var at = new CellRef(RowKey.FoeFront, 3);
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Cast, at)).Applied);

            Assert.IsNull(s.At(at), "1500 through 1000 hp - swept by the spell's own cleanup");
            Assert.AreEqual(7, s.P(Side.You).Mana, "paid ◆2 AFTER the effect took");
            Assert.AreEqual(0, s.P(Side.You).Hand.Count - CountExcept(s, Side.You, "Ember Bolt"),
                "the card left hand");
        }

        static int CountExcept(GameState s, Side side, string name)
        {
            int n = 0;
            foreach (var c in s.P(side).Hand) if (c.Id.Value != name) n++;
            return n;
        }

        [Test]
        public void Burn_OnAFaceDown_DestroysItOutright_AndTheInvestedManaIsLost()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            var ch = new ChargeUnit();
            ch.Id = s.NewUid();
            ch.Owner = Side.Foe;
            ch.Color = Element.Water;
            ch.SetIn = SlotName.Back;
            ch.Card = new CardSnapshot(new CardId("Mistling"), "Mistling", Element.Water,
                1, 500, 1000, 1, Keyword.None, false, false, StructId.None);
            ch.Invested = 6;                                       // heavily funded
            ch.SetTurn = 0;
            var at = new CellRef(RowKey.FoeBack, 4);
            s.Put(at, ch);

            int idx = GiveCard(s, Side.You, "Ember Bolt");
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Cast, at)).Applied);

            Assert.IsNull(s.At(at), "a face-down has no hp - it is simply destroyed");
            Assert.AreEqual(0, s.P(Side.Foe).Mana, "the ◆6 poured into it is gone, not refunded");
            bool graved = false;
            foreach (var g in s.P(Side.Foe).Grave) if (g.Name == "Mistling") graved = true;
            Assert.IsTrue(graved);
        }

        [Test]
        public void Burn_DamagesAStructure()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            var b = UnitFactory.MakeStructure(s, Side.Foe,
                TestData.Catalog.Structure(new StructId("foundry"), Element.None));
            var at = new CellRef(RowKey.FoeBack, 2);
            s.Put(at, b);
            int hp = b.Hp;

            int idx = GiveCard(s, Side.You, "Searing Brand");                // burn 2000
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Cast, at)).Applied);
            Assert.AreEqual(hp - 2000, b.Hp);
        }

        // ── raze ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Raze_DestroysAStructureOutright_IgnoringItsHitPoints()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            var b = UnitFactory.MakeStructure(s, Side.Foe,
                TestData.Catalog.Structure(new StructId("bulwark"), Element.None));
            var at = new CellRef(RowKey.FoeBack, 2);
            s.Put(at, b);
            Assert.Greater(b.Hp, 3000, "fixture: a wall far tougher than the spell costs");

            int idx = GiveCard(s, Side.You, "Cave-In");                      // raze, ◆3
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Cast, at)).Applied);

            Assert.IsNull(s.At(at), "hp is irrelevant - raze is destruction, not damage");
            Assert.AreEqual(6, s.P(Side.You).Mana);
        }

        [Test]
        public void Raze_RejectsANonStructureTarget_BeforeAnyManaMoves()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            var t = Place(s, Side.Foe, "Mistling", RowKey.FoeFront, 3);
            int idx = GiveCard(s, Side.You, "Cave-In");

            var cmd = new PlayCardCommand(Side.You, idx, PlayMode.Cast, new CellRef(RowKey.FoeFront, 3));
            Assert.AreEqual(Rejection.TargetKindIllegal, e.CanApply(cmd));
            Assert.AreEqual(9, s.P(Side.You).Mana, "an illegal target costs nothing");
            Assert.AreEqual(1000, t.Hp);
        }

        // ── chain ────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Chain_HitsTheTwoDeadliest_NotNecessarilyTheOneClicked()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            var clicked = Place(s, Side.Foe, "Mistling", RowKey.FoeFront, 1);     // 500/1000
            var big = Place(s, Side.Foe, "Leviath", RowKey.FoeFront, 2);          // 2000/3500
            var mid = Place(s, Side.Foe, "Maelstrom", RowKey.FoeFront, 3);        // 1500/2500

            int idx = GiveCard(s, Side.You, "Arc Flash");                         // chain 1000
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Cast,
                new CellRef(RowKey.FoeFront, 1))).Applied);

            Assert.AreEqual(3500 - 1000, big.Hp);
            Assert.AreEqual(2500 - 1000, mid.Hp);
            Assert.AreEqual(1000, clicked.Hp,
                "the clicked creature only picked the SIDE - it took nothing");
        }

        // ── bounce ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void Bounce_ReturnsTheCreatureToItsOwnersHand_CarryingItsLiveStatline()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            var t = Place(s, Side.Foe, "Maelstrom", RowKey.FoeFront, 3);          // 1500/2500
            t.Attack += 500;                                                      // hardened
            t.MaxHp += 1000;
            t.Hp = 200;                                                           // and wounded
            int foeHand = s.P(Side.Foe).Hand.Count;

            int idx = GiveCard(s, Side.You, "Riptide");
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Cast,
                new CellRef(RowKey.FoeFront, 3))).Applied);

            Assert.IsNull(s.At(new CellRef(RowKey.FoeFront, 3)));
            Assert.AreEqual(foeHand + 1, s.P(Side.Foe).Hand.Count, "to its OWNER's hand");

            var card = s.P(Side.Foe).Hand[s.P(Side.Foe).Hand.Count - 1];
            Assert.IsTrue(card.Snapshot.HasValue);
            Assert.AreEqual(2000, card.Snapshot.Attack, "the buff came home with it");
            Assert.AreEqual(3500, card.Snapshot.Health, "at MAX hp - a card in hand is undamaged");
            Assert.AreEqual(Keyword.Undertow, card.Snapshot.Keyword);
        }

        [Test]
        public void Bounce_SlidesOffEntrench_ButTheSpellIsStillSpent()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            var t = Place(s, Side.Foe, "Mosshide", RowKey.FoeFront, 3);           // entrench
            int foeHand = s.P(Side.Foe).Hand.Count;

            int idx = GiveCard(s, Side.You, "Riptide");
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Cast,
                new CellRef(RowKey.FoeFront, 3))).Applied);

            Assert.AreSame(t, s.At(new CellRef(RowKey.FoeFront, 3)), "immovable");
            Assert.AreEqual(foeHand, s.P(Side.Foe).Hand.Count);
            Assert.AreEqual(6, s.P(Side.You).Mana, "and it cost the caster ◆3 all the same");
            bool spent = false;
            foreach (var g in s.P(Side.You).Grave) if (g.Name == "Riptide") spent = true;
            Assert.IsTrue(spent, "the card is in the grave");
        }

        // ── casting legality ─────────────────────────────────────────────────────────────────

        [Test]
        public void Cast_RefusesOwnUnits_Traps_Creatures_AndTheWrongPhase()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            var mine = Place(s, Side.You, "Cinderling", RowKey.YouFront, 3);
            var theirs = Place(s, Side.Foe, "Mistling", RowKey.FoeFront, 3);

            int bolt = GiveCard(s, Side.You, "Ember Bolt");
            Assert.AreEqual(Rejection.TargetNotEnemy, e.CanApply(new PlayCardCommand(
                Side.You, bolt, PlayMode.Cast, new CellRef(RowKey.YouFront, 3))));
            Assert.AreEqual(Rejection.NoLegalTarget, e.CanApply(new PlayCardCommand(
                Side.You, bolt, PlayMode.Cast, new CellRef(RowKey.FoeFront, 0))));

            int snare = GiveCard(s, Side.You, "Snare Pit");
            Assert.AreEqual(Rejection.WrongPlayMode, e.CanApply(new PlayCardCommand(
                Side.You, snare, PlayMode.Cast, new CellRef(RowKey.FoeFront, 3))),
                "a trap is set, never cast");

            int creature = GiveCard(s, Side.You, "Cinderling");
            Assert.AreEqual(Rejection.WrongPlayMode, e.CanApply(new PlayCardCommand(
                Side.You, creature, PlayMode.Cast, new CellRef(RowKey.FoeFront, 3))));

            Assert.AreEqual(Rejection.WrongPlayMode, e.CanApply(new PlayCardCommand(
                Side.You, bolt, PlayMode.Set, new CellRef(RowKey.YouBack, 0))),
                "and a non-trap spell can never be set face-down");
        }

        [Test]
        public void Cast_RefusedWithoutTheMana()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 1;
            Place(s, Side.Foe, "Mistling", RowKey.FoeFront, 3);

            int idx = GiveCard(s, Side.You, "Ember Bolt");                        // ◆2
            Assert.AreEqual(Rejection.NotEnoughMana, e.CanApply(new PlayCardCommand(
                Side.You, idx, PlayMode.Cast, new CellRef(RowKey.FoeFront, 3))));
        }

        [Test]
        public void Targeting_IsOnePredicate_SharedByTheViewAndTheValidator()
        {
            GameState s;
            var e = Engine(out s);
            var bolt = TestData.Catalog.Spell(new CardId("Ember Bolt"));
            var raze = TestData.Catalog.Spell(new CardId("Cave-In"));
            var snare = TestData.Catalog.Spell(new CardId("Snare Pit"));

            Assert.IsFalse(SpellTargeting.HasAnyTarget(s, bolt, Side.You), "empty board");

            Place(s, Side.Foe, "Mistling", RowKey.FoeFront, 3);
            Assert.IsTrue(SpellTargeting.HasAnyTarget(s, bolt, Side.You));
            Assert.IsFalse(SpellTargeting.HasAnyTarget(s, raze, Side.You), "no structure yet");
            Assert.IsFalse(SpellTargeting.HasAnyTarget(s, snare, Side.You),
                "pitfall is a trap payload with no castable branch at all");

            var b = UnitFactory.MakeStructure(s, Side.Foe,
                TestData.Catalog.Structure(new StructId("foundry"), Element.None));
            s.Put(new CellRef(RowKey.FoeBack, 1), b);
            Assert.IsTrue(SpellTargeting.HasAnyTarget(s, raze, Side.You));
            Assert.AreEqual(2, SpellTargeting.Targets(s, bolt, Side.You).Count,
                "Bolt reaches creatures, structures and face-downs alike");
        }

        // ── summon traps ─────────────────────────────────────────────────────────────────────

        [Test]
        public void SummonTrap_OffersTheDefenderAWindow_AndSpringingKillsTheNewcomer()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            var trapAt = new CellRef(RowKey.FoeBack, 2);
            var trap = SetTrap(s, Side.Foe, "Snare Pit", trapAt, 0);
            Assert.IsTrue(trap.IsArmed(s.TurnNumber));

            int idx = GiveCard(s, Side.You, "Cinderling");
            var at = new CellRef(RowKey.YouFront, 3);
            var r = e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Summon, at));
            Assert.AreEqual(CommandStatus.AwaitingChoice, r.Status);

            var win = (ResponseWindowRequest)s.Pending;
            Assert.AreEqual(Side.Foe, win.Responder);
            Assert.AreEqual(TrapTrigger.Summon, win.Trigger);
            Assert.AreEqual(1, win.ArmedTraps.Length);
            var summoned = (CreatureUnit)s.At(at);
            Assert.AreEqual(summoned.Id, win.Subject.UnitId);

            Assert.AreEqual(Rejection.ChoicePending,
                e.CanApply(new EndTurnCommand(Side.You)), "a parked window freezes everything");

            Assert.IsTrue(e.Apply(new RespondCommand(Side.Foe,
                new TrapChosen(win.ArmedTraps[0]))).Applied);

            Assert.IsNull(s.At(at), "dragged down as it formed");
            Assert.IsNull(s.At(trapAt), "and the trap is spent");
            bool creatureGraved = false, trapGraved = false;
            foreach (var g in s.P(Side.You).Grave) if (g.Name == "Cinderling") creatureGraved = true;
            foreach (var g in s.P(Side.Foe).Grave) if (g.Name == "Snare Pit") trapGraved = true;
            Assert.IsTrue(creatureGraved, "the victim graves to ITS OWN owner");
            Assert.IsTrue(trapGraved, "the spent trap graves to the defender");
        }

        [Test]
        public void SummonTrap_PassingLeavesBothStanding()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            var trapAt = new CellRef(RowKey.FoeBack, 2);
            SetTrap(s, Side.Foe, "Snare Pit", trapAt, 0);

            int idx = GiveCard(s, Side.You, "Cinderling");
            var at = new CellRef(RowKey.YouFront, 3);
            Assert.AreEqual(CommandStatus.AwaitingChoice,
                e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Summon, at)).Status);

            Assert.IsTrue(e.Apply(new RespondCommand(Side.Foe, TrapChosen.Passed)).Applied);

            Assert.IsNotNull(s.At(at), "held - the creature lives");
            Assert.IsNotNull(s.At(trapAt), "and the trap is still lying there");
            Assert.IsNull(s.Pending);
        }

        [Test]
        public void SummonTrap_SetThisTurn_IsNotOffered()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            SetTrap(s, Side.Foe, "Snare Pit", new CellRef(RowKey.FoeBack, 2), s.TurnNumber);

            int idx = GiveCard(s, Side.You, "Cinderling");
            var at = new CellRef(RowKey.YouFront, 3);
            Assert.AreEqual(CommandStatus.Applied,
                e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Summon, at)).Status);
            Assert.IsNull(s.Pending, "a trap never springs on the turn it was set");
            Assert.IsNotNull(s.At(at));
        }

        [Test]
        public void SummonTrap_IgnoresItsOwnEffect_AndBypassesDeathTriggers()
        {
            // Any trigger:'summon' trap simply destroys the newcomer, whatever its effect says -
            // and the victim is graved directly, so its own death keyword never fires.
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            var trapAt = new CellRef(RowKey.FoeBack, 2);
            var trap = SetTrap(s, Side.Foe, "Snare Pit", trapAt, 0);
            trap.Effect = SpellEffect.Burn;                       // deliberately not pitfall
            trap.Value = 1;

            int idx = GiveCard(s, Side.You, "Wraithling");        // reap 500
            var at = new CellRef(RowKey.YouFront, 3);
            Assert.AreEqual(CommandStatus.AwaitingChoice,
                e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Summon, at)).Status);
            var win = (ResponseWindowRequest)s.Pending;
            Assert.IsTrue(e.Apply(new RespondCommand(Side.Foe,
                new TrapChosen(win.ArmedTraps[0]))).Applied);

            Assert.IsNull(s.At(at), "destroyed outright despite the burn effect");
            foreach (var kv in s.ObjectsOf(Side.You))
            {
                var c = kv.Value as CreatureUnit;
                Assert.IsFalse(c != null && c.IsToken, "no Shade - this is not a death sweep");
            }
        }

        [Test]
        public void SummonTrap_FiresAfterTheEnterKeyword_SoAWardersTokenSurvivesIt()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            SetTrap(s, Side.Foe, "Snare Pit", new CellRef(RowKey.FoeBack, 2), 0);

            int idx = GiveCard(s, Side.You, "Gleamward");
            var at = new CellRef(RowKey.YouFront, 3);
            Assert.AreEqual(CommandStatus.AwaitingChoice,
                e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Summon, at)).Status);
            var win = (ResponseWindowRequest)s.Pending;
            Assert.IsTrue(e.Apply(new RespondCommand(Side.Foe,
                new TrapChosen(win.ArmedTraps[0]))).Applied);

            Assert.IsNull(s.At(at), "the warder was dragged down");
            var tok = s.At(new CellRef(RowKey.YouBack, 0)) as CreatureUnit;
            Assert.IsNotNull(tok, "but its Lumen had already entered play");
            Assert.AreEqual("Lumen", tok.Name);
        }

        [Test]
        public void FlippingAFaceDown_DoesNotProvokeASummonTrap()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            SetTrap(s, Side.Foe, "Snare Pit", new CellRef(RowKey.FoeBack, 2), 0);

            var ch = new ChargeUnit();
            ch.Id = s.NewUid();
            ch.Owner = Side.You;
            ch.Color = Element.Fire;
            ch.SetIn = SlotName.Back;
            ch.Card = new CardSnapshot(new CardId("Cinderling"), "Cinderling", Element.Fire,
                1, 1000, 1000, 1, Keyword.None, false, false, StructId.None);
            ch.Invested = 1;
            ch.SetTurn = 0;
            var at = new CellRef(RowKey.YouBack, 5);
            s.Put(at, ch);

            Assert.IsTrue(e.Apply(new FlipChargeCommand(Side.You, at, ch.Id)).Applied);
            Assert.IsNull(s.Pending, "flip immunity is the mechanical payoff of setting");
            Assert.IsNotNull(s.At(at) as CreatureUnit);
        }

        [Test]
        public void ARespondCommand_MustNameATrapTheWindowActuallyOffered()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            SetTrap(s, Side.Foe, "Snare Pit", new CellRef(RowKey.FoeBack, 2), 0);
            var decoy = SetTrap(s, Side.Foe, "Overgrowth", new CellRef(RowKey.FoeBack, 4), 0);

            int idx = GiveCard(s, Side.You, "Cinderling");
            Assert.AreEqual(CommandStatus.AwaitingChoice, e.Apply(new PlayCardCommand(
                Side.You, idx, PlayMode.Summon, new CellRef(RowKey.YouFront, 3))).Status);

            var win = (ResponseWindowRequest)s.Pending;
            Assert.AreEqual(1, win.ArmedTraps.Length, "only the summon-trigger trap is offered");

            var bogus = UnitRef.Cell(new CellRef(RowKey.FoeBack, 4), decoy.Id);
            Assert.AreEqual(Rejection.WrongResponseShape,
                e.CanApply(new RespondCommand(Side.Foe, new TrapChosen(bogus))));
            Assert.AreEqual(Rejection.WrongResponseShape,
                e.CanApply(new RespondCommand(Side.Foe, new IndexChosen(0))),
                "and the response shape must match the request");
            Assert.AreEqual(Rejection.NotYourTurn,
                e.CanApply(new RespondCommand(Side.You, TrapChosen.Passed)),
                "only the responder may answer");
        }
    }
}
