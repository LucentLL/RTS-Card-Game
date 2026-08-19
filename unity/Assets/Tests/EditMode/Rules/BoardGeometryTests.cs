using System;
using NUnit.Framework;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// Geometry is the foundation every other rule stands on, so it is pinned hard here.
    /// Claims verified against docs/unity/spec/01_board_geometry_state.md,
    /// 03_combat_v3.md s4.1 and 04_movement_placement.md.
    /// </summary>
    public class BoardGeometryTests
    {
        [Test]
        public void CenterRow_HasLanesAtOneThreeFive_Only()
        {
            Assert.IsTrue(Board.IsLane(1));
            Assert.IsTrue(Board.IsLane(3));
            Assert.IsTrue(Board.IsLane(5));
            Assert.IsFalse(Board.IsLane(0));
            Assert.IsFalse(Board.IsLane(2));
            Assert.IsFalse(Board.IsLane(4));
            Assert.IsFalse(Board.IsLane(6));
        }

        [Test]
        public void RealSlots_Are31_Of35()
        {
            int real = 0;
            foreach (var row in Board.AllRows)
                for (int c = 0; c < Board.Columns; c++)
                    if (Board.IsRealSlot(row, c)) real++;

            Assert.AreEqual(35, Board.Cells, "board is 7x5");
            Assert.AreEqual(31, real, "center row contributes only its 3 lanes");
        }

        [Test]
        public void CenterSlotOk_SplitsCreaturesIntoLanes_AndStructuresOntoFlanks()
        {
            // creatures fight in the lanes
            Assert.IsTrue(Board.CenterSlotOk(RowKey.Center, 3, isStructure: false));
            Assert.IsFalse(Board.CenterSlotOk(RowKey.Center, 2, isStructure: false));

            // structures build on the flanking ground
            Assert.IsTrue(Board.CenterSlotOk(RowKey.Center, 2, isStructure: true));
            Assert.IsFalse(Board.CenterSlotOk(RowKey.Center, 3, isStructure: true));

            // outside the center the distinction does not exist
            Assert.IsTrue(Board.CenterSlotOk(RowKey.YouFront, 2, isStructure: true));
            Assert.IsTrue(Board.CenterSlotOk(RowKey.YouFront, 2, isStructure: false));
        }

        // ---- row-interval blocking (spec 03 s4.1) --------------------------------------------

        [Test]
        public void RowsCrossedInto_SameRow_IsEmpty_TheUninterposableDuel()
        {
            Span<RowKey> buf = stackalloc RowKey[Board.Rows];
            int n = Board.RowsCrossedInto((int)RowKey.Center, (int)RowKey.Center, buf);
            Assert.AreEqual(0, n, "a same-row attack crosses into nothing, so nothing may interpose");
        }

        [Test]
        public void RowsCrossedInto_IsHalfOpen_ExcludesAttacker_IncludesTarget()
        {
            Span<RowKey> buf = stackalloc RowKey[Board.Rows];
            int n = Board.RowsCrossedInto((int)RowKey.YouBack, (int)RowKey.FoeFront, buf);

            Assert.AreEqual(3, n);
            Assert.AreEqual(RowKey.YouFront, buf[0]);
            Assert.AreEqual(RowKey.Center, buf[1]);
            Assert.AreEqual(RowKey.FoeFront, buf[2]);
        }

        [Test]
        public void RowsCrossedInto_IsOrderedByTravel_NotByRowIndex()
        {
            Span<RowKey> up = stackalloc RowKey[Board.Rows];
            int nUp = Board.RowsCrossedInto((int)RowKey.YouBack, (int)RowKey.FoeBack, up);

            Span<RowKey> down = stackalloc RowKey[Board.Rows];
            int nDown = Board.RowsCrossedInto((int)RowKey.FoeBack, (int)RowKey.YouBack, down);

            Assert.AreEqual(4, nUp);
            Assert.AreEqual(4, nDown);
            Assert.AreEqual(RowKey.YouFront, up[0], "travelling up starts adjacent to the attacker");
            Assert.AreEqual(RowKey.FoeFront, down[0], "travelling down starts adjacent to the attacker");
        }

        [Test]
        public void RowsCrossedInto_ClipsVirtualWallRows()
        {
            Span<RowKey> buf = stackalloc RowKey[Board.Rows];

            // an attack at the foe wall (virtual row -1) must not emit a row index of -1
            int n = Board.RowsCrossedInto((int)RowKey.YouBack, Board.FoeWallRow, buf);
            for (int i = 0; i < n; i++)
                Assert.IsTrue((int)buf[i] >= 0 && (int)buf[i] < Board.Rows, "clipped to real rows");
            Assert.AreEqual(4, n, "all four real rows above YouBack are crossed");
        }

        // ---- movement -------------------------------------------------------------------------

        [Test]
        public void Adjacent_DiagonalsReachTheCenterLanes()
        {
            var fromFlank = new CellRef(RowKey.YouFront, 0);
            var lane = new CellRef(RowKey.Center, 1);
            Assert.IsTrue(Board.Adjacent(fromFlank, lane), "a diagonal step reaches the lane");
        }

        [Test]
        public void Adjacent_RejectsNonLaneCenterCells()
        {
            var from = new CellRef(RowKey.YouFront, 1);
            var flank = new CellRef(RowKey.Center, 2);
            Assert.IsFalse(Board.Adjacent(from, flank), "center col 2 is not a creature slot");
        }

        [Test]
        public void Adjacent_IsSymmetric_AndOwnerAgnostic()
        {
            foreach (var ra in Board.AllRows)
            foreach (var rb in Board.AllRows)
            for (int ca = 0; ca < Board.Columns; ca++)
            for (int cb = 0; cb < Board.Columns; cb++)
            {
                var a = new CellRef(ra, ca);
                var b = new CellRef(rb, cb);
                Assert.AreEqual(Board.Adjacent(a, b), Board.Adjacent(b, a),
                    "adjacency must be symmetric for " + a + " / " + b);
            }
        }

        [Test]
        public void Adjacent_IsNeverSelf()
        {
            var c = new CellRef(RowKey.YouFront, 3);
            Assert.IsFalse(Board.Adjacent(c, c));
        }

        [Test]
        public void Neighbours_AreEnumeratedInCanonicalOrder()
        {
            Span<CellRef> buf = stackalloc CellRef[8];
            int n = Board.Neighbours(new CellRef(RowKey.YouFront, 3), buf);

            Assert.Greater(n, 0);
            for (int i = 1; i < n; i++)
            {
                bool ordered = (int)buf[i - 1].Row < (int)buf[i].Row
                            || ((int)buf[i - 1].Row == (int)buf[i].Row && buf[i - 1].Col < buf[i].Col);
                Assert.IsTrue(ordered, "ascending RowKey then ascending Col");
            }
        }

        [Test]
        public void Neighbours_AgreeWithAdjacent()
        {
            Span<CellRef> buf = stackalloc CellRef[8];
            foreach (var row in Board.AllRows)
            for (int c = 0; c < Board.Columns; c++)
            {
                var from = new CellRef(row, c);
                int n = Board.Neighbours(from, buf);
                for (int i = 0; i < n; i++)
                    Assert.IsTrue(Board.Adjacent(from, buf[i]),
                        "Neighbours emitted " + buf[i] + " which Adjacent rejects from " + from);
            }
        }

        // ---- zones ----------------------------------------------------------------------------

        [Test]
        public void Raid_SpansBothEnemyRows_ForEitherSide()
        {
            var youRaid = Board.RowsOfZone(Side.You, WorkerZone.Raid);
            CollectionAssert.AreEquivalent(new[] { RowKey.FoeFront, RowKey.FoeBack }, youRaid);

            var foeRaid = Board.RowsOfZone(Side.Foe, WorkerZone.Raid);
            CollectionAssert.AreEquivalent(new[] { RowKey.YouFront, RowKey.YouBack }, foeRaid);
        }

        [Test]
        public void ZoneForRow_MapsEnemyRowsToRaid()
        {
            Assert.AreEqual(WorkerZone.Back, Board.ZoneForRow(Side.You, RowKey.YouBack));
            Assert.AreEqual(WorkerZone.Front, Board.ZoneForRow(Side.You, RowKey.YouFront));
            Assert.AreEqual(WorkerZone.Center, Board.ZoneForRow(Side.You, RowKey.Center));
            Assert.AreEqual(WorkerZone.Raid, Board.ZoneForRow(Side.You, RowKey.FoeFront));
            Assert.AreEqual(WorkerZone.Raid, Board.ZoneForRow(Side.You, RowKey.FoeBack));

            Assert.AreEqual(WorkerZone.Back, Board.ZoneForRow(Side.Foe, RowKey.FoeBack));
            Assert.AreEqual(WorkerZone.Raid, Board.ZoneForRow(Side.Foe, RowKey.YouBack));
        }

        [Test]
        public void RowFor_And_WhichOf_RoundTrip()
        {
            foreach (Side side in new[] { Side.You, Side.Foe })
            foreach (SlotName which in new[] { SlotName.Back, SlotName.Front, SlotName.Center })
            {
                var row = Board.RowFor(side, which);
                Assert.AreEqual(which, Board.WhichOf(row), side + "/" + which);
            }
        }

        // ---- refs -----------------------------------------------------------------------------

        [Test]
        public void CellRef_IndexRoundTrips()
        {
            foreach (var row in Board.AllRows)
            for (int c = 0; c < Board.Columns; c++)
            {
                var cell = new CellRef(row, c);
                Assert.AreEqual(cell, CellRef.FromIndex(cell.Index));
            }
        }

        [Test]
        public void UnitRef_CarriesIdentity_AndRejectsWrongAccessor()
        {
            var r = UnitRef.Cell(new CellRef(RowKey.Center, 3), unitId: 42);

            Assert.AreEqual(42, r.UnitId);
            Assert.IsTrue(r.IsCell);
            Assert.AreEqual(new CellRef(RowKey.Center, 3), r.AsCell);
            Assert.Throws<InvalidOperationException>(() => { var _ = r.AsPool; });
        }

        [Test]
        public void UnitRef_SameCoordinateDifferentUnit_IsNotEqual()
        {
            var cell = new CellRef(RowKey.YouFront, 2);
            Assert.AreNotEqual(UnitRef.Cell(cell, 1), UnitRef.Cell(cell, 2),
                "identity is part of the ref, so a stale declaration is detectable");
        }
    }
}
