using NUnit.Framework;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// The Move / Pay / Sacrifice settlement loop and the harvest lock (spec 02 s7). A creature
    /// camped beyond its supply line must be settled before the economy moves - except the
    /// orphan (structure-only) shortfall, which harvests through and pays from the proceeds.
    /// </summary>
    public class UpkeepSettlementTests
    {
        private static DuelEngine Engine(out GameState s)
        {
            s = MatchSetup.NewMatch(TestData.Catalog,
                new CommanderId("fire"), new CommanderId("water"), 21, RulesOptions.JsParity);
            return new DuelEngine(s, TestData.Catalog);
        }

        /// <summary>A Magmaw (upkeep 3) in a center lane: the center zone reads -3.</summary>
        private static CreatureUnit PlaceMagmawInCenter(GameState s)
        {
            var magmaw = UnitFactory.MakeCreature(s, Side.You,
                TestData.Catalog.Creature(new CardId("Magmaw")), Element.None);
            s.Put(new CellRef(RowKey.Center, 3), magmaw);
            return magmaw;
        }

        [Test]
        public void ADeficit_LocksHarvest_UntilSettled()
        {
            GameState s;
            var e = Engine(out s);
            PlaceMagmawInCenter(s);

            Assert.AreEqual(3, Upkeep.ZoneDeficit(s, Side.You, WorkerZone.Center, TestData.Catalog));
            Assert.AreEqual(Rejection.ShortfallUnsettled, e.CanApply(new HarvestCommand(Side.You)));
        }

        [Test]
        public void Offender_IsTheHighestUpkeepUnpaid_InTheFirstDeficitZone()
        {
            GameState s;
            Engine(out s);
            var magmaw = PlaceMagmawInCenter(s);

            var cinder = UnitFactory.MakeCreature(s, Side.You,
                TestData.Catalog.Creature(new CardId("Sparkimp")), Element.None);   // upkeep 1
            s.Put(new CellRef(RowKey.Center, 1), cinder);

            CellRef cell;
            int unitId;
            Assert.IsTrue(Upkeep.TryFindOffender(s, Side.You, TestData.Catalog, out cell, out unitId));
            Assert.AreEqual(magmaw.Id, unitId, "highest upkeep first");
            Assert.AreEqual(new CellRef(RowKey.Center, 3), cell);
        }

        [Test]
        public void Pay_CapsAtTheZoneDeficit_AndMarksSettled()
        {
            GameState s;
            var e = Engine(out s);
            var magmaw = PlaceMagmawInCenter(s);
            s.P(Side.You).Mana = 5;

            var r = e.Apply(new UpkeepPayCommand(Side.You, new CellRef(RowKey.Center, 3), magmaw.Id));
            Assert.IsTrue(r.Applied, r.Rejection.ToString());

            Assert.AreEqual(2, s.P(Side.You).Mana, "paid min(up 3, deficit 3)");
            Assert.AreEqual(3, s.P(Side.You).UpkeepPaid[(int)WorkerZone.Center]);
            Assert.IsTrue(magmaw.PaidUpkeep);
            Assert.AreEqual(0, Upkeep.ZoneDeficit(s, Side.You, WorkerZone.Center, TestData.Catalog));
            Assert.AreEqual(Rejection.None, e.CanApply(new HarvestCommand(Side.You)),
                "harvest unlocks once the offender is settled");

            Assert.AreEqual(Rejection.NothingToPay,
                e.CanApply(new UpkeepPayCommand(Side.You, new CellRef(RowKey.Center, 3), magmaw.Id)),
                "a settled creature cannot pay twice");
        }

        [Test]
        public void Pay_RefusesWithoutTheFullAmount_NoPartialDebit()
        {
            GameState s;
            var e = Engine(out s);
            var magmaw = PlaceMagmawInCenter(s);
            s.P(Side.You).Mana = 2;

            var r = e.Apply(new UpkeepPayCommand(Side.You, new CellRef(RowKey.Center, 3), magmaw.Id));
            Assert.AreEqual(Rejection.NotEnoughMana, r.Rejection);
            Assert.AreEqual(2, s.P(Side.You).Mana, "nothing was deducted");
        }

        [Test]
        public void Sacrifice_GravesDirectly_NoDeathTriggers_AndUnlocksHarvest()
        {
            GameState s;
            var e = Engine(out s);
            var magmaw = PlaceMagmawInCenter(s);

            var r = e.Apply(new UpkeepSacrificeCommand(Side.You, new CellRef(RowKey.Center, 3), magmaw.Id));
            Assert.IsTrue(r.Applied, r.Rejection.ToString());

            Assert.IsNull(s.At(new CellRef(RowKey.Center, 3)));
            Assert.AreEqual(1, s.P(Side.You).Grave.Count);
            Assert.AreEqual("Magmaw", s.P(Side.You).Grave[0].Name);
            Assert.AreEqual(Rejection.None, e.CanApply(new HarvestCommand(Side.You)));
        }

        [Test]
        public void Move_SettlesByRelocating_TheUpkeepSecondMoveTaps()
        {
            GameState s;
            var e = Engine(out s);
            var magmaw = PlaceMagmawInCenter(s);

            // center(3) -> youFront(3): the deficit follows the body into the front zone,
            // where the same worker figure math applies - front has no free workforce either.
            var r = e.Apply(new MoveUnitCommand(Side.You,
                new CellRef(RowKey.Center, 3), new CellRef(RowKey.YouFront, 3), magmaw.Id));
            Assert.IsTrue(r.Applied, r.Rejection.ToString());
            Assert.IsTrue(magmaw.Moved);
            Assert.IsFalse(magmaw.Tapped, "the first move is free");

            // second move during the owner's own upkeep is legal but spends the whole turn
            var r2 = e.Apply(new MoveUnitCommand(Side.You,
                new CellRef(RowKey.YouFront, 3), new CellRef(RowKey.YouBack, 3), magmaw.Id));
            Assert.IsTrue(r2.Applied, r2.Rejection.ToString());
            Assert.IsTrue(magmaw.MovedTwice);
            Assert.IsTrue(magmaw.Tapped);

            // back zone: wk 2 - upkeep 3 = -1 -> still a deficit, but a THIRD move is spent
            var r3 = e.CanApply(new MoveUnitCommand(Side.You,
                new CellRef(RowKey.YouBack, 3), new CellRef(RowKey.YouBack, 2), magmaw.Id));
            Assert.AreEqual(Rejection.MoveAlreadySpent, r3);

            Assert.AreEqual(1, Upkeep.ZoneDeficit(s, Side.You, WorkerZone.Back, TestData.Catalog));
        }

        /// <summary>
        /// A full row can be marched into by razing one of your own cards - and only then.
        ///
        /// The gate is the whole reason the board is still tappable: without it every card of
        /// yours would be a move destination as well as a thing to select, and picking up your
        /// own units is the commonest tap in the game. With it, the two readings of a tap only
        /// collide in a row that has no space left in it, where the view asks which was meant.
        /// </summary>
        [Test]
        public void Move_IntoAFullRow_RazesOneOfYourOwn_ButOnlyWhenThereIsNoRoom()
        {
            GameState s;
            var e = Engine(out s);
            var magmaw = PlaceMagmawInCenter(s);

            var forge = UnitFactory.MakeStructure(s, Side.You,
                TestData.Catalog.Structure(new StructId("foundry"), Element.None));
            forge.Bank = 4;
            var at = new CellRef(RowKey.YouFront, 3);
            s.Put(at, forge);

            var move = new MoveUnitCommand(Side.You, new CellRef(RowKey.Center, 3), at, magmaw.Id);
            Assert.AreEqual(Rejection.CellOccupied, e.CanApply(move),
                "the row has six other cells - go round it");

            for (int c = 0; c < Board.Columns; c++)
                if (s.At(new CellRef(RowKey.YouFront, c)) == null)
                    s.Put(new CellRef(RowKey.YouFront, c), UnitFactory.MakeCreature(s, Side.You,
                        TestData.Catalog.Creature(new CardId("Sparkimp")), Element.None));

            Assert.IsTrue(e.Apply(move).Applied, "with the row full, it takes the forge's place");
            Assert.AreSame(magmaw, s.At(at), "and is standing there");
            Assert.AreEqual(4, magmaw.Bank, "the razed forge's bank rides on rather than evaporating");
            Assert.IsTrue(s.P(Side.You).Grave.Count > 0, "the forge went to the grave");
        }

        /// <summary>An ENEMY card is still a wall, full row or not: walking through one would be
        /// combat without a fight.</summary>
        [Test]
        public void Move_NeverRazesTheirs_EvenInAFullRow()
        {
            GameState s;
            var e = Engine(out s);
            var magmaw = PlaceMagmawInCenter(s);

            for (int c = 0; c < Board.Columns; c++)
                s.Put(new CellRef(RowKey.YouFront, c), UnitFactory.MakeCreature(s, Side.Foe,
                    TestData.Catalog.Creature(new CardId("Mistling")), Element.None));

            Assert.AreEqual(Rejection.CellOccupied, e.CanApply(new MoveUnitCommand(Side.You,
                new CellRef(RowKey.Center, 3), new CellRef(RowKey.YouFront, 3), magmaw.Id)));
        }

        [Test]
        public void Move_GeometryGuards_Hold()
        {
            GameState s;
            var e = Engine(out s);
            var magmaw = PlaceMagmawInCenter(s);

            Assert.AreEqual(Rejection.NotAdjacent, e.CanApply(new MoveUnitCommand(Side.You,
                new CellRef(RowKey.Center, 3), new CellRef(RowKey.YouBack, 3), magmaw.Id)),
                "two rows in one step is still not a move");

            Assert.AreEqual(Rejection.None, e.CanApply(new MoveUnitCommand(Side.You,
                new CellRef(RowKey.Center, 3), new CellRef(RowKey.Center, 0), magmaw.Id)),
                "but the whole of its own row is, however far along it");

            // one of YOURS is a wall only while the row still has somewhere else to go
            var blocker = UnitFactory.MakeCreature(s, Side.You,
                TestData.Catalog.Creature(new CardId("Sparkimp")), Element.None);
            s.Put(new CellRef(RowKey.YouFront, 3), blocker);
            Assert.AreEqual(Rejection.CellOccupied, e.CanApply(new MoveUnitCommand(Side.You,
                new CellRef(RowKey.Center, 3), new CellRef(RowKey.YouFront, 3), magmaw.Id)));

            Assert.AreEqual(Rejection.NoSuchUnit, e.CanApply(new MoveUnitCommand(Side.You,
                new CellRef(RowKey.Center, 3), new CellRef(RowKey.YouFront, 2), 99999)),
                "identity travels with every command - a stale id is detected, not resolved");
        }

        [Test]
        public void OrphanShortfall_HarvestsThrough_AndPaysFromProceeds()
        {
            GameState s;
            var e = Engine(out s);

            // A lone Cannon Tower in the FRONT row: sup -2 with no free workforce and no
            // creature to settle - the anti-deadlock case (spec 02 s7.4).
            var tower = TestData.Catalog.Structure(new StructId("tower"), Element.None);
            s.Put(new CellRef(RowKey.YouFront, 0), UnitFactory.MakeStructure(s, Side.You, tower));

            Assert.AreEqual(2, Upkeep.ZoneDeficit(s, Side.You, WorkerZone.Front, TestData.Catalog));
            Assert.AreEqual(2, Upkeep.OrphanDeficit(s, Side.You, TestData.Catalog));
            Assert.AreEqual(Rejection.None, e.CanApply(new HarvestCommand(Side.You)),
                "the lock is on the OFFENDER, not the deficit");

            var r = e.Apply(new HarvestCommand(Side.You));
            Assert.IsTrue(r.Applied);
            Assert.AreEqual(0, s.P(Side.You).Mana, "harvested 2, then the crews' wages took it");
            Assert.AreEqual(2, s.P(Side.You).UpkeepPaid[(int)WorkerZone.Front],
                "the deficit is credited IN FULL so the turn can never dead-lock");
            Assert.AreEqual(TurnPhase.Draw, s.Phase);
        }

        [Test]
        public void RaidZone_ChargesBothEnemyRows()
        {
            GameState s;
            Engine(out s);

            var raiderA = UnitFactory.MakeCreature(s, Side.You,
                TestData.Catalog.Creature(new CardId("Sparkimp")), Element.None);   // up 1
            var raiderB = UnitFactory.MakeCreature(s, Side.You,
                TestData.Catalog.Creature(new CardId("Magmaw")), Element.None);     // up 3
            s.Put(new CellRef(RowKey.FoeFront, 2), raiderA);
            s.Put(new CellRef(RowKey.FoeBack, 5), raiderB);

            Assert.AreEqual(4, Upkeep.ZoneDeficit(s, Side.You, WorkerZone.Raid, TestData.Catalog),
                "a deep siege is charged the same as a shallow one - both enemy rows are Raid");
        }
    }
}
