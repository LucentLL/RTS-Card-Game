using System.Collections.Generic;
using NUnit.Framework;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// M10: one table of cases per keyword hook (spec 06 s6.2). The combat-scoped halves -
    /// Undertow's bounce, Overcharge's discharge, Scour's bypass and strike - are exercised
    /// through the real resolver in CombatRulesTests; what lives here is the per-unit hooks and
    /// the ordering rules that surround them.
    /// </summary>
    public class KeywordTests
    {
        static DuelEngine Engine(out GameState s, string you = "light", string foe = "dark")
        {
            s = MatchSetup.NewMatch(TestData.Catalog,
                new CommanderId(you), new CommanderId(foe), 77, RulesOptions.JsParity);
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

        static int GiveCard(GameState s, Side side, string name, Element color)
        {
            s.P(side).Hand.Add(new HandCard(new CardId(name), color));
            return s.P(side).Hand.Count - 1;
        }

        static void Fill(GameState s, Side side, RowKey row)
        {
            for (int col = 0; col < Board.Columns; col++)
                if (s.At(new CellRef(row, col)) == null)
                    Place(s, side, "Cinderling", row, col);
        }

        // ── ENTER: ward ──────────────────────────────────────────────────────────────────────

        [Test]
        public void Ward_OnSummon_ConjuresALumenInTheFirstFreeCell()
        {
            GameState s;
            var e = Engine(out s);
            ToAction(e, s);
            s.P(Side.You).Mana = 9;

            int idx = GiveCard(s, Side.You, "Gleamward", Element.Light);   // ward 1000
            var at = new CellRef(RowKey.YouFront, 3);
            Assert.IsTrue(e.Apply(new PlayCardCommand(Side.You, idx, PlayMode.Summon, at)).Applied);

            // firstEmptyCell scans the owner's BACK row first, so the ward lands behind
            var tok = s.At(new CellRef(RowKey.YouBack, 0)) as CreatureUnit;
            Assert.IsNotNull(tok, "Lumen conjured into the first free back-row slot");
            Assert.AreEqual("Lumen", tok.Name);
            Assert.IsTrue(tok.IsToken);
            Assert.AreEqual(0, tok.Attack);
            Assert.AreEqual(1000, tok.Hp, "wardhp from the card, not the legacy ||2 default");
            Assert.IsTrue(tok.Sick);
            Assert.AreEqual(Side.You, tok.Owner);
            Assert.AreEqual(Keyword.None, tok.Keyword, "tokens are keyword-inert");
        }

        [Test]
        public void Ward_ScanOrder_BackThenFrontThenCentreLanes()
        {
            GameState s;
            var e = Engine(out s);
            Fill(s, Side.You, RowKey.YouBack);
            Fill(s, Side.You, RowKey.YouFront);
            s.Put(new CellRef(RowKey.YouFront, 5), null);            // one hole in the FRONT

            var ev = new EventSink();
            var warder = UnitFactory.MakeCreature(s, Side.You,
                TestData.Catalog.Creature(new CardId("Gleamward")), Element.Light);
            KeywordEngine.OnEnter(s, warder, Side.You, TestData.Catalog, ev);

            var tok = s.At(new CellRef(RowKey.YouFront, 5)) as CreatureUnit;
            Assert.IsNotNull(tok, "back row full - the front row's hole takes it");
            Assert.AreEqual("Lumen", tok.Name);
        }

        [Test]
        public void Ward_WithNoRoomAnywhere_IsSimplyLost()
        {
            GameState s;
            var e = Engine(out s);
            Fill(s, Side.You, RowKey.YouBack);
            Fill(s, Side.You, RowKey.YouFront);
            for (int col = 0; col < Board.Columns; col++)
                if (Board.IsLane(col)) Place(s, Side.You, "Cinderling", RowKey.Center, col);

            var ev = new EventSink();
            var warder = UnitFactory.MakeCreature(s, Side.You,
                TestData.Catalog.Creature(new CardId("Gleamward")), Element.Light);
            KeywordEngine.OnEnter(s, warder, Side.You, TestData.Catalog, ev);

            foreach (var kv in s.Objects())
            {
                var c = kv.Value as CreatureUnit;
                Assert.IsFalse(c != null && c.IsToken, "nowhere to put it - no token appears");
            }
        }

        // ── DEATH: detonate / reap ───────────────────────────────────────────────────────────

        [Test]
        public void Detonate_HitsTheDeadliestEnemyCreature_HighestAttackThenLowestHp()
        {
            GameState s;
            var e = Engine(out s);

            var bomb = Place(s, Side.You, "Emberfly", RowKey.YouFront, 0);      // det 1000
            var weak = Place(s, Side.Foe, "Cinderling", RowKey.FoeFront, 1);    // 1000/1000
            var deadly = Place(s, Side.Foe, "Scorchling", RowKey.FoeFront, 2);  // 1500/500
            var alsoDeadly = Place(s, Side.Foe, "Scorchling", RowKey.FoeFront, 3);
            alsoDeadly.Hp = 1000;                                    // same attack, tougher

            bomb.Hp = 0;
            DeathSweep.Cleanup(s, TestData.Catalog, new EventSink());

            Assert.AreEqual(1000, weak.Hp, "untouched - lower attack");
            Assert.AreEqual(1000, alsoDeadly.Hp, "untouched - same attack but more hp");
            Assert.IsNull(s.At(new CellRef(RowKey.FoeFront, 2)),
                "the 1500/500 took 1000 and was swept in the same pass");
        }

        [Test]
        public void Detonate_FallsBackToTheFrailestStructure_OnlyWhenNoCreatureStands()
        {
            GameState s;
            var e = Engine(out s);

            var bomb = Place(s, Side.You, "Infernox", RowKey.YouFront, 0);      // det 1500
            var tough = UnitFactory.MakeStructure(s, Side.Foe,
                TestData.Catalog.Structure(new StructId("foundry"), Element.None));   // 3000
            s.Put(new CellRef(RowKey.FoeBack, 1), tough);
            var frail = UnitFactory.MakeStructure(s, Side.Foe,
                TestData.Catalog.Structure(new StructId("forge"), Element.Fire));
            s.Put(new CellRef(RowKey.FoeBack, 2), frail);

            int frailBefore = frail.Hp, toughBefore = tough.Hp;
            Assert.Less(frail.Hp, tough.Hp, "fixture assumes the forge is the frailer of the two");

            bomb.Hp = 0;
            DeathSweep.Cleanup(s, TestData.Catalog, new EventSink());

            Assert.AreEqual(toughBefore, tough.Hp);
            Assert.AreEqual(frailBefore - 1500, frail.Hp, "the weakest structure eats it");
        }

        [Test]
        public void Detonate_NeverTouchesTheLifePool()
        {
            GameState s;
            var e = Engine(out s);
            var bomb = Place(s, Side.You, "Emberfly", RowKey.YouFront, 0);
            int life = s.P(Side.Foe).Life;

            bomb.Hp = 0;
            DeathSweep.Cleanup(s, TestData.Catalog, new EventSink());

            Assert.AreEqual(life, s.P(Side.Foe).Life, "no creature, no structure - nothing happens");
        }

        [Test]
        public void Reap_RaisesAShade_InTheCellTheCorpseJustLeft()
        {
            GameState s;
            var e = Engine(out s);
            Fill(s, Side.You, RowKey.YouBack);
            Fill(s, Side.You, RowKey.YouFront);

            var reaper = UnitFactory.MakeCreature(s, Side.You,
                TestData.Catalog.Creature(new CardId("Grimfang")), Element.Dark);   // reap 500
            var at = new CellRef(RowKey.YouFront, 4);
            s.Put(at, reaper);                                        // replaces the filler

            reaper.Hp = 0;
            DeathSweep.Cleanup(s, TestData.Catalog, new EventSink());

            var tok = s.At(at) as CreatureUnit;
            Assert.IsNotNull(tok, "the sweep frees the cell BEFORE the death trigger fires");
            Assert.AreEqual("Shade", tok.Name);
            Assert.AreEqual(500, tok.Attack);
            Assert.AreEqual(500, tok.Hp);
            Assert.IsTrue(tok.IsToken);
            Assert.IsTrue(tok.Sick);
        }

        [Test]
        public void Reap_WithNoRoom_RaisesNothing()
        {
            GameState s;
            var e = Engine(out s);
            var reaper = UnitFactory.MakeCreature(s, Side.You,
                TestData.Catalog.Creature(new CardId("Grimfang")), Element.Dark);
            s.Put(new CellRef(RowKey.FoeBack, 3), reaper);            // dies deep in enemy ground
            Fill(s, Side.You, RowKey.YouBack);
            Fill(s, Side.You, RowKey.YouFront);
            for (int col = 0; col < Board.Columns; col++)
                if (Board.IsLane(col)) Place(s, Side.You, "Cinderling", RowKey.Center, col);

            reaper.Hp = 0;
            DeathSweep.Cleanup(s, TestData.Catalog, new EventSink());

            foreach (var kv in s.Objects())
            {
                var c = kv.Value as CreatureUnit;
                Assert.IsFalse(c != null && c.IsToken, "no free cell - the Shade never rises");
            }
        }

        [Test]
        public void DeathTriggers_ChainInOneSweep()
        {
            GameState s;
            var e = Engine(out s);

            // a Detonate death that kills a Reap creature: both resolve in the same cleanup
            var bomb = Place(s, Side.You, "Infernox", RowKey.YouFront, 0);       // det 1500
            var victim = Place(s, Side.Foe, "Wraithling", RowKey.FoeFront, 1);   // 1000/500 reap500

            bomb.Hp = 0;
            DeathSweep.Cleanup(s, TestData.Catalog, new EventSink());

            Assert.IsNull(s.At(new CellRef(RowKey.FoeFront, 1)), "the Wraithling was blasted");
            CreatureUnit shade = null;
            foreach (var kv in s.ObjectsOf(Side.Foe))
            {
                var c = kv.Value as CreatureUnit;
                if (c != null && c.IsToken) shade = c;
            }
            Assert.IsNotNull(shade, "its own Reap fired inside the same re-sweeping loop");
            Assert.AreEqual("Shade", shade.Name);
        }

        // ── UPKEEP: chrysalis / overcharge ───────────────────────────────────────────────────

        [Test]
        public void Chrysalis_SwellsThenHatchesInPlace_KeepingItsIdentity()
        {
            GameState s;
            var e = Engine(out s);
            var pod = Place(s, Side.You, "Sap Pod", RowKey.YouFront, 2);   // 0/1500 grow1 hatch3
            int id = pod.Id;
            pod.Bank = 4;

            var ev = new EventSink();
            KeywordEngine.UpkeepTick(s, Side.You, TestData.Catalog, ev);
            Assert.AreEqual(1, pod.ChrysalisCount);
            Assert.IsTrue(pod.Sick, "a cocoon can never attack");
            Assert.AreEqual("Sap Pod", pod.Name);

            KeywordEngine.UpkeepTick(s, Side.You, TestData.Catalog, ev);
            Assert.AreEqual(2, pod.ChrysalisCount);
            Assert.AreEqual(Keyword.Chrysalis, pod.Keyword);

            KeywordEngine.UpkeepTick(s, Side.You, TestData.Catalog, ev);
            Assert.AreSame(pod, s.At(new CellRef(RowKey.YouFront, 2)), "mutates IN PLACE");
            Assert.AreEqual(id, pod.Id, "same instance, same id");
            Assert.AreEqual(4, pod.Bank, "banked mana rides through the hatch");
            Assert.AreEqual("Canopy Beast", pod.Name);
            Assert.AreEqual(2500, pod.Attack);
            Assert.AreEqual(2000, pod.MaxHp);
            Assert.AreEqual(2000, pod.Hp, "full heal to the new maximum");
            Assert.AreEqual(Keyword.None, pod.Keyword, "the cleared keyword is what stops the loop");
            Assert.IsTrue(pod.Sick, "it cannot attack the turn it hatches");
            Assert.AreEqual(3, pod.ChrysalisCount, "the counter is deliberately never reset");

            KeywordEngine.UpkeepTick(s, Side.You, TestData.Catalog, ev);
            Assert.AreEqual(3, pod.ChrysalisCount, "and never ticks again");
        }

        [Test]
        public void Chrysalis_OnlyTicksOnItsOwnersTurn()
        {
            GameState s;
            var e = Engine(out s);
            var pod = Place(s, Side.You, "Sap Pod", RowKey.YouFront, 2);

            KeywordEngine.UpkeepTick(s, Side.Foe, TestData.Catalog, new EventSink());
            Assert.AreEqual(0, pod.ChrysalisCount, "the enemy's upkeep does not swell it");
        }

        [Test]
        public void Overcharge_BanksToACapOfThree()
        {
            GameState s;
            var e = Engine(out s);
            var volt = Place(s, Side.You, "Volt", RowKey.YouFront, 2);

            for (int i = 0; i < 5; i++)
                KeywordEngine.UpkeepTick(s, Side.You, TestData.Catalog, new EventSink());

            Assert.AreEqual(3, volt.OverchargeBank);
        }

        [Test]
        public void Overcharge_DischargesIntoTheStrike_ButNeverIntoRetaliation()
        {
            GameState s;
            var e = Engine(out s);
            var volt = Place(s, Side.You, "Volt", RowKey.YouFront, 2);      // 1000/1000
            volt.OverchargeBank = 3;

            var ids = new List<int> { volt.Id };
            KeywordEngine.AttackPrep(s, ids, new EventSink());

            Assert.AreEqual(0, volt.OverchargeBank, "the bank empties into the blow");
            Assert.AreEqual(3, volt.DischargeBonus);
            Assert.AreEqual(1003, volt.EffectiveAttack, "effA carries the discharge");
            Assert.AreEqual(1000, volt.Attack, "raw attack - what a retaliation reads - is untouched");

            KeywordEngine.AttackEnd(s, ids);
            Assert.AreEqual(0, volt.DischargeBonus, "cleared when the resolution ends");
        }

        [Test]
        public void UpkeepPasses_RunOneKeywordAtATime_InEnumOrder()
        {
            // Chrysalis (6) sweeps the whole board before Overcharge (8) starts, exactly as
            // startTurn calls chrysalisUpkeep then overchargeUpkeep.
            GameState s;
            var e = Engine(out s);
            Place(s, Side.You, "Sap Pod", RowKey.YouFront, 1);
            Place(s, Side.You, "Volt", RowKey.YouFront, 2);
            Place(s, Side.You, "Sap Pod", RowKey.YouFront, 3);

            var ev = new EventSink();
            KeywordEngine.UpkeepTick(s, Side.You, TestData.Catalog, ev);

            var order = new List<string>();
            foreach (var g in ev.Events)
            {
                if (g is ChrysalisGrew) order.Add("chrysalis");
                else if (g is Overcharged) order.Add("overcharge");
            }
            Assert.AreEqual(new[] { "chrysalis", "chrysalis", "overcharge" }, order.ToArray());
        }

        [Test]
        public void Workers_NeverCarryKeywords()
        {
            GameState s;
            var e = Engine(out s);
            var w = UnitFactory.MakeCreature(s, Side.You,
                TestData.Catalog.Creature(new CardId("Grimfang")), Element.Dark);
            w.IsWorker = true;
            Assert.IsNull(KeywordEngine.Of(w), "kwOf gates on !worker");
        }

        // ── the rules text every card explains itself with ───────────────────────────────────

        [Test]
        public void EveryKeyword_HasATextAndALabel()
        {
            GameState s;
            var e = Engine(out s);
            var names = new Dictionary<Keyword, string>
            {
                { Keyword.Detonate, "Emberfly" },
                { Keyword.Undertow, "Undertow" },
                { Keyword.Entrench, "Mosshide" },
                { Keyword.Ward, "Gleamward" },
                { Keyword.Reap, "Grimfang" },
                { Keyword.Chrysalis, "Sap Pod" },
                { Keyword.Scour, "Zephyr" },
                { Keyword.Overcharge, "Volt" },
            };

            foreach (var kv in names)
            {
                var c = UnitFactory.MakeCreature(s, Side.You,
                    TestData.Catalog.Creature(new CardId(kv.Value)), Element.None);
                Assert.AreEqual(kv.Key, c.Keyword, kv.Value + " should carry " + kv.Key);
                Assert.IsNotEmpty(KeywordEngine.TextOf(c, TestData.Catalog),
                    kv.Key + " has inspect text");
                Assert.IsNotEmpty(KeywordEngine.LabelOf(c), kv.Key + " has a short label");
                Assert.IsNotNull(KeywordEngine.Of(kv.Key), kv.Key + " is registered");
            }

            Assert.IsNull(KeywordEngine.Of(Keyword.None));
            Assert.AreEqual(9, KeywordEngine.All.Count, "eight handlers, index 0 reserved for None");
        }

        [Test]
        public void ChrysalisText_NamesTheFormItHatchesInto()
        {
            // kwText is the ONLY in-game documentation of what a cocoon is worth (spec 06 s6.3),
            // so the hatch clause is part of the port, not decoration.
            GameState s;
            var e = Engine(out s);
            var pod = Place(s, Side.You, "Sap Pod", RowKey.YouFront, 2);

            var text = KeywordEngine.TextOf(pod, TestData.Catalog);
            StringAssert.Contains("Chrysalis 0/3", text);
            StringAssert.Contains("swells +1", text);
            StringAssert.Contains("Canopy Beast", text);
            // the statline is printed on the DISPLAY scale (StatScale): 2500/2000 engine units
            StringAssert.Contains("250", text);
            StringAssert.Contains("200", text);
        }

        /// <summary>
        /// Every keyword says what it DOES for a card nobody has played yet.
        ///
        /// The sentences were written against an instance and had no caller outside the tests, so
        /// a card in hand printed "First Strike Chrysalis" - two rules named and neither explained
        /// - which is what the playtesters could not look up. The inspector reads them through
        /// this overload, and a keyword that answers it with an empty string is a card that goes
        /// back to naming a rule it never states.
        /// </summary>
        [Test]
        public void EveryKeyword_ExplainsItselfOnAnUnplayedCard()
        {
            var names = new Dictionary<Keyword, string>
            {
                { Keyword.Detonate, "Emberfly" },
                { Keyword.Undertow, "Undertow" },
                { Keyword.Entrench, "Mosshide" },
                { Keyword.Ward, "Gleamward" },
                { Keyword.Reap, "Grimfang" },
                { Keyword.Chrysalis, "Sap Pod" },
                { Keyword.Scour, "Zephyr" },
                { Keyword.Overcharge, "Volt" },
            };

            foreach (var kv in names)
            {
                var card = TestData.Catalog.Creature(new CardId(kv.Value));
                Assert.AreEqual(kv.Key, card.Keyword, kv.Value + " should carry " + kv.Key);

                var printed = KeywordEngine.TextOf(card, TestData.Catalog);
                Assert.IsNotEmpty(printed, kv.Value + " explains its keyword before it is played");
                StringAssert.Contains(".", printed, "it is a sentence, not a label");
            }

            // ...and the cocoon still names what it becomes, which is the one thing about it that
            // cannot be read off the board at all
            var pod = KeywordEngine.TextOf(TestData.Catalog.Creature(new CardId("Sap Pod")),
                                           TestData.Catalog);
            StringAssert.Contains("Canopy Beast", pod);
            StringAssert.Contains("Chrysalis 0/3", pod, "an unplayed cocoon has swelled nothing");

            Assert.IsEmpty(KeywordEngine.TextOf((CreatureCard)null, TestData.Catalog));
        }

        /// <summary>A printed instance is for TEXT only - it has no id, so it can never be
        /// mistaken for something standing on the board.</summary>
        [Test]
        public void PrintedInstance_HasNoUnitId()
        {
            var card = TestData.Catalog.Creature(new CardId("Sap Pod"));
            var printed = UnitFactory.Printed(card);

            Assert.AreEqual(0, printed.Id, "GameState.NewUid never hands out 0");
            Assert.AreEqual(card.Name, printed.Name);
            Assert.AreEqual(card.Attack, printed.Attack);
            Assert.AreEqual(card.Health, printed.MaxHp);
            Assert.AreEqual(card.Keyword, printed.Keyword);
        }
    }
}
