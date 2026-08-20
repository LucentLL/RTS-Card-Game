using NUnit.Framework;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// The state model's two load-bearing properties: a clone is independent of its source, and the
    /// hash distinguishes any state the rules can distinguish. Everything downstream - AI search,
    /// save games, golden-scenario regression, the differential harness against the JS - is built
    /// on those two, so they are tested harder than anything else here.
    /// </summary>
    public class StateModelTests
    {
        static CreatureUnit Creature(int id, Side owner, int atk = 500, int hp = 500)
        {
            return new CreatureUnit
            {
                Id = id, Owner = owner, Color = Element.Fire,
                Card = new CardId("Sparkimp"), Name = "Sparkimp",
                Attack = atk, Hp = hp, MaxHp = hp, Cost = 1, Upkeep = 1,
                Keyword = Keyword.Detonate, Detonate = 2,
            };
        }

        static GameState Fresh()
        {
            var s = new GameState { Random = new Pcg32(1234UL) };
            s.P(Side.You).Life = 10000;
            s.P(Side.Foe).Life = 10000;
            s.P(Side.You).PrimaryColor = Element.Fire;
            s.P(Side.Foe).PrimaryColor = Element.Water;
            return s;
        }

        // ---- clone independence ----------------------------------------------------------------

        [Test]
        public void Clone_ProducesAnIndependentBoard()
        {
            var s = Fresh();
            var cell = new CellRef(RowKey.YouFront, 3);
            s.Put(cell, Creature(s.NewUid(), Side.You));

            var copy = s.Clone();
            ((CreatureUnit)copy.At(cell)).Hp = 1;

            Assert.AreEqual(500, ((CreatureUnit)s.At(cell)).Hp, "mutating the clone changed the original");
        }

        [Test]
        public void Clone_DoesNotShareUnitInstances()
        {
            var s = Fresh();
            var cell = new CellRef(RowKey.Center, 3);
            s.Put(cell, Creature(s.NewUid(), Side.You));

            var copy = s.Clone();
            Assert.AreNotSame(s.At(cell), copy.At(cell));
        }

        [Test]
        public void Clone_DoesNotShareHandDeckOrGrave()
        {
            var s = Fresh();
            s.P(Side.You).Hand.Add(new HandCard(new CardId("Emberfly"), Element.Fire));
            s.P(Side.You).Deck.Add(new HandCard(new CardId("Cinderling"), Element.Fire));
            s.P(Side.You).Grave.Add(new GraveRecord(new CardId("Ashwing"), Element.Fire, UnitKind.Creature, 3));

            var copy = s.Clone();
            copy.P(Side.You).Hand.Clear();
            copy.P(Side.You).Deck.Clear();
            copy.P(Side.You).Grave.Clear();

            Assert.AreEqual(1, s.P(Side.You).Hand.Count);
            Assert.AreEqual(1, s.P(Side.You).Deck.Count);
            Assert.AreEqual(1, s.P(Side.You).Grave.Count);
        }

        [Test]
        public void Clone_DoesNotShareWorkerPools()
        {
            var s = Fresh();
            var pool = s.P(Side.You).Pool(WorkerZone.Back);
            pool.Members.Add(Creature(s.NewUid(), Side.You));

            var copy = s.Clone();
            copy.P(Side.You).Pool(WorkerZone.Back).Members[0].Tapped = true;

            Assert.IsFalse(pool.Members[0].Tapped, "worker state leaked between clones");
        }

        [Test]
        public void Clone_DoesNotShareUpkeepPaid()
        {
            var s = Fresh();
            s.P(Side.You).UpkeepPaid[(int)WorkerZone.Raid] = 4;

            var copy = s.Clone();
            copy.P(Side.You).UpkeepPaid[(int)WorkerZone.Raid] = 99;

            Assert.AreEqual(4, s.P(Side.You).UpkeepPaid[(int)WorkerZone.Raid]);
        }

        [Test]
        public void Clone_PreservesRngStreamPosition()
        {
            var s = Fresh();
            for (int i = 0; i < 5; i++) s.Random.NextInt(100);

            var copy = s.Clone();

            // Advancing the clone must not move the original, and both must produce the same next value.
            int fromCopy = copy.Random.NextInt(1000);
            int fromOrig = s.Random.NextInt(1000);
            Assert.AreEqual(fromOrig, fromCopy, "clone resumed from a different stream position");
        }

        [Test]
        public void Clone_PreservesNextUid_SoIdsAreNeverReused()
        {
            var s = Fresh();
            s.NewUid(); s.NewUid(); s.NewUid();

            var copy = s.Clone();
            Assert.AreEqual(s.NextUid, copy.NextUid);
            Assert.AreEqual(s.NextUid, copy.NewUid(), "the clone would have reissued a live id");
        }

        // ---- hash sensitivity -------------------------------------------------------------------

        [Test]
        public void Hash_IsStableForAnUnchangedState()
        {
            var s = Fresh();
            s.Put(new CellRef(RowKey.YouFront, 3), Creature(s.NewUid(), Side.You));
            Assert.AreEqual(StateCodec.Hash(s), StateCodec.Hash(s));
        }

        [Test]
        public void Hash_OfACloneMatchesItsSource()
        {
            var s = Fresh();
            s.Put(new CellRef(RowKey.YouFront, 2), Creature(s.NewUid(), Side.You));
            s.Put(new CellRef(RowKey.FoeFront, 4), Creature(s.NewUid(), Side.Foe));
            s.P(Side.You).Hand.Add(new HandCard(new CardId("Emberfly"), Element.Fire));
            s.P(Side.You).Pool(WorkerZone.Back).Members.Add(Creature(s.NewUid(), Side.You));

            Assert.AreEqual(StateCodec.Hash(s), StateCodec.Hash(s.Clone()),
                "a clone that hashes differently is not a clone");
        }

        [Test]
        public void Hash_NoticesAUnitMovingCell()
        {
            var s = Fresh();
            var u = Creature(s.NewUid(), Side.You);
            s.Put(new CellRef(RowKey.YouFront, 2), u);
            ulong before = StateCodec.Hash(s);

            s.Put(new CellRef(RowKey.YouFront, 2), null);
            s.Put(new CellRef(RowKey.YouFront, 3), u);

            Assert.AreNotEqual(before, StateCodec.Hash(s));
        }

        [Test]
        public void Hash_NoticesAnOwnershipFlip()
        {
            var s = Fresh();
            var cell = new CellRef(RowKey.Center, 1);
            s.Put(cell, Creature(s.NewUid(), Side.You));
            ulong before = StateCodec.Hash(s);

            s.At(cell).Owner = Side.Foe;
            Assert.AreNotEqual(before, StateCodec.Hash(s), "ownership is the authority - it must hash");
        }

        [Test]
        public void Hash_NoticesASingleFlagBit()
        {
            var s = Fresh();
            ulong before = StateCodec.Hash(s);

            s.Options.AbsorberIsWeakestBlocker = true;

            Assert.AreNotEqual(before, StateCodec.Hash(s),
                "two engines configured differently must never compare equal");
        }

        [Test]
        public void Hash_NoticesRngAdvance()
        {
            var s = Fresh();
            ulong before = StateCodec.Hash(s);
            s.Random.NextInt(10);
            Assert.AreNotEqual(before, StateCodec.Hash(s), "rng position is state");
        }

        [Test]
        public void Hash_NoticesPerTurnFlags()
        {
            var s = Fresh();
            var cell = new CellRef(RowKey.YouBack, 0);
            s.Put(cell, Creature(s.NewUid(), Side.You));
            ulong before = StateCodec.Hash(s);

            ((CreatureUnit)s.At(cell)).Tapped = true;
            Assert.AreNotEqual(before, StateCodec.Hash(s));
        }

        [Test]
        public void Hash_DistinguishesEmptyBoardFromShiftedBoard()
        {
            var a = Fresh();
            a.Put(new CellRef(RowKey.YouBack, 0), Creature(1, Side.You));

            var b = Fresh();
            b.Put(new CellRef(RowKey.YouBack, 1), Creature(1, Side.You));

            Assert.AreNotEqual(StateCodec.Hash(a), StateCodec.Hash(b),
                "empty cells must be written explicitly or a shift can hash equal");
        }

        [Test]
        public void CanonicalJson_IsDeterministicAndParseable()
        {
            var s = Fresh();
            s.Put(new CellRef(RowKey.Center, 5), Creature(s.NewUid(), Side.Foe));

            string a = StateCodec.ToCanonicalJson(s);
            string b = StateCodec.ToCanonicalJson(s.Clone());

            Assert.AreEqual(a, b);
            Assert.IsTrue(a.StartsWith("{") && a.EndsWith("}"));
            Assert.IsTrue(a.Contains("\"schema\":1"));
        }

        // ---- worker pool semantics --------------------------------------------------------------

        [Test]
        public void Resync_GrowsWithSickBodies_SoNewWorkersCannotHarvestThisTurn()
        {
            var pool = new WorkerPool();
            int next = 1;
            pool.Resync(3, () => Creature(next++, Side.You));

            Assert.AreEqual(3, pool.Count);
            Assert.AreEqual(0, pool.ReadyCount, "a worker created this turn is sick");
        }

        [Test]
        public void Resync_ShrinksFromTheTail()
        {
            var pool = new WorkerPool();
            int next = 1;
            pool.Resync(3, () => Creature(next++, Side.You));
            int firstId = pool.Members[0].Id;

            pool.Resync(1, () => Creature(next++, Side.You));

            Assert.AreEqual(1, pool.Count);
            Assert.AreEqual(firstId, pool.Members[0].Id, "shrink must pop the tail, not the head");
        }

        [Test]
        public void Ready_ClearsSickAndTapped()
        {
            var pool = new WorkerPool();
            int next = 1;
            pool.Resync(2, () => Creature(next++, Side.You));
            pool.Members[0].Tapped = true;

            pool.Ready();

            Assert.AreEqual(2, pool.ReadyCount);
        }

        [Test]
        public void RaidZone_HasNoPool()
        {
            var s = Fresh();
            Assert.IsNull(s.P(Side.You).Pool(WorkerZone.Raid),
                "there is no support behind enemy lines");
        }

        // ---- board addressing --------------------------------------------------------------------

        [Test]
        public void ObjectsAreEnumeratedInCanonicalCellOrder()
        {
            var s = Fresh();
            s.Put(new CellRef(RowKey.YouBack, 5), Creature(1, Side.You));
            s.Put(new CellRef(RowKey.FoeBack, 2), Creature(2, Side.Foe));
            s.Put(new CellRef(RowKey.Center, 3), Creature(3, Side.You));

            var order = new System.Collections.Generic.List<int>();
            foreach (var kv in s.Objects()) order.Add(kv.Key.Index);

            for (int i = 1; i < order.Count; i++)
                Assert.Less(order[i - 1], order[i], "enumeration must be ascending cell index");
        }

        [Test]
        public void ObjectsOf_FindsRaidersStandingInEnemyRows()
        {
            var s = Fresh();
            // A "You" raider physically standing in the foe's front row - the exact case that the
            // JS per-player row arrays got wrong.
            s.Put(new CellRef(RowKey.FoeFront, 3), Creature(s.NewUid(), Side.You));

            int mine = 0;
            foreach (var kv in s.ObjectsOf(Side.You)) mine++;

            Assert.AreEqual(1, mine, "ownership travels with the object, never with the row");
        }

        [Test]
        public void IsInteractive_IsFalseDuringTheOpponentsTurn()
        {
            var s = Fresh();
            s.Turn = Side.You;
            s.Phase = TurnPhase.Action;

            Assert.IsTrue(s.IsInteractive(Side.You));
            Assert.IsFalse(s.IsInteractive(Side.Foe));
        }

        [Test]
        public void IsInteractive_IsFalseInEndPhaseAndAfterGameOver()
        {
            var s = Fresh();
            s.Turn = Side.You;

            s.Phase = TurnPhase.End;
            Assert.IsFalse(s.IsInteractive(Side.You));

            s.Phase = TurnPhase.Action;
            s.IsOver = true;
            Assert.IsFalse(s.IsInteractive(Side.You));
        }
    }
}
