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
        /// <summary>
        /// The middle row is SEVEN cells, like every other row.
        ///
        /// It used to be three creature lanes at columns 1/3/5 and four builder flanks, so the
        /// contested row was three wide for an army and four wide for a builder, and four of the
        /// thirty-five cells could hold nothing at all.
        /// </summary>
        [Test]
        public void EveryCellIsReal_TheCentreRowHasNoLanes()
        {
            int real = 0;
            foreach (var row in Board.AllRows)
                for (int c = 0; c < Board.Columns; c++)
                    if (Board.IsRealSlot(row, c)) real++;

            Assert.AreEqual(35, Board.Cells, "board is 7x5");
            Assert.AreEqual(35, real, "and all 35 of them are standable");

            for (int c = 0; c < Board.Columns; c++)
            {
                Assert.IsTrue(Board.CenterSlotOk(RowKey.Center, c, isStructure: false),
                    "a creature may stand in centre column " + c);
                Assert.IsTrue(Board.CenterSlotOk(RowKey.Center, c, isStructure: true),
                    "and so may a structure");
            }
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

        /// <summary>
        /// A move crosses AT MOST ONE ROW and any distance along it.
        ///
        /// The row limit is the front line and is kept; the column limit was a shuffle and is
        /// gone. Both halves are asserted, because it is the ASYMMETRY that is the rule - drop
        /// either one and the board is a different game.
        /// </summary>
        [Test]
        public void AStep_CrossesOneRow_AndAnyNumberOfColumns()
        {
            var from = new CellRef(RowKey.YouBack, 0);

            Assert.IsTrue(Board.InStepRange(from, new CellRef(RowKey.YouBack, 6)),
                "the whole of its own row");
            Assert.IsTrue(Board.InStepRange(from, new CellRef(RowKey.YouFront, 6)),
                "and the whole of the next one");
            Assert.IsFalse(Board.InStepRange(from, new CellRef(RowKey.Center, 0)),
                "but never two rows, however short the walk");
            Assert.IsFalse(Board.InStepRange(from, from), "and never nowhere");
        }

        [Test]
        public void StepRange_IsSymmetric_AndOwnerAgnostic()
        {
            foreach (var ra in Board.AllRows)
            foreach (var rb in Board.AllRows)
            for (int ca = 0; ca < Board.Columns; ca++)
            for (int cb = 0; cb < Board.Columns; cb++)
            {
                var a = new CellRef(ra, ca);
                var b = new CellRef(rb, cb);
                Assert.AreEqual(Board.InStepRange(a, b), Board.InStepRange(b, a),
                    "step range must be symmetric for " + a + " / " + b);
            }
        }

        [Test]
        public void StepTargets_AreEnumeratedInCanonicalOrder()
        {
            Span<CellRef> buf = stackalloc CellRef[Board.MaxStepTargets];
            int n = Board.StepTargets(new CellRef(RowKey.YouFront, 3), buf);

            Assert.AreEqual(3 * Board.Columns - 1, n,
                "three rows of seven, less the cell it is standing in");
            for (int i = 1; i < n; i++)
            {
                bool ordered = (int)buf[i - 1].Row < (int)buf[i].Row
                            || ((int)buf[i - 1].Row == (int)buf[i].Row && buf[i - 1].Col < buf[i].Col);
                Assert.IsTrue(ordered, "ascending RowKey then ascending Col");
            }
        }

        [Test]
        public void StepTargets_AgreeWithStepRange_BothWays()
        {
            Span<CellRef> buf = stackalloc CellRef[Board.MaxStepTargets];
            foreach (var row in Board.AllRows)
            for (int c = 0; c < Board.Columns; c++)
            {
                var from = new CellRef(row, c);
                int n = Board.StepTargets(from, buf);
                for (int i = 0; i < n; i++)
                    Assert.IsTrue(Board.InStepRange(from, buf[i]),
                        "StepTargets emitted " + buf[i] + " which InStepRange rejects from " + from);

                int expected = 0;
                foreach (var r2 in Board.AllRows)
                    for (int c2 = 0; c2 < Board.Columns; c2++)
                        if (Board.InStepRange(from, new CellRef(r2, c2))) expected++;
                Assert.AreEqual(expected, n, "and emitted every one of them, from " + from);
            }
        }

        /// <summary>A short buffer truncates rather than overruns - the contract every caller
        /// leans on when it sizes with MaxStepTargets.</summary>
        [Test]
        public void StepTargets_NeverOverrunsAShortBuffer()
        {
            Span<CellRef> tiny = stackalloc CellRef[4];
            int n = Board.StepTargets(new CellRef(RowKey.Center, 3), tiny);
            Assert.AreEqual(4, n);
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
