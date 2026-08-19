using System.Collections.Generic;
using NUnit.Framework;
using SpawnRowDuel.Rules;
using SpawnRowDuel.View;
using UnityEngine;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// The view is generated from the rules geometry, so these tests exist to catch the view
    /// drifting out of agreement with the engine - the failure mode where the picture shows a
    /// move that the rules would reject, or hides one they would allow.
    /// </summary>
    public class BoardViewTests
    {
        private GameObject _go;
        private BoardView _view;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("BoardUnderTest");
            _view = _go.AddComponent<BoardView>();
            _view.Build();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void BuildsEveryCenterLaneAndFlank_AndNothingElse()
        {
            // 4 non-center rows x 7 + center's 3 lanes + center's 4 structure flanks
            Assert.AreEqual(35, _view.Cells.Count);
            Assert.AreEqual(31, _view.CreatureSlotCount, "only lanes are creature-standable in the center");
        }

        [Test]
        public void EveryCreatureSlotFromTheRules_HasACellObject()
        {
            foreach (var row in Board.AllRows)
                for (int c = 0; c < Board.Columns; c++)
                    if (Board.IsRealSlot(row, c))
                        Assert.IsTrue(_view.Cells.ContainsKey(new CellRef(row, c)),
                            "rules say " + new CellRef(row, c) + " is standable but the view has no cell");
        }

        [Test]
        public void CellPositions_AreUnique()
        {
            var seen = new HashSet<Vector3>();
            foreach (var kv in _view.Cells)
                Assert.IsTrue(seen.Add(_view.WorldOf(kv.Key)), "two cells share a position: " + kv.Key);
        }

        [Test]
        public void RowOrder_RunsFoeToYou_AlongPositiveToNegativeZ()
        {
            float foeBack = _view.WorldOf(new CellRef(RowKey.FoeBack, 3)).z;
            float center = _view.WorldOf(new CellRef(RowKey.Center, 3)).z;
            float youBack = _view.WorldOf(new CellRef(RowKey.YouBack, 3)).z;

            Assert.Greater(foeBack, center, "the enemy back row sits furthest from the player camera");
            Assert.Greater(center, youBack);
        }

        [Test]
        public void ColumnOrder_RunsLeftToRight()
        {
            float c0 = _view.WorldOf(new CellRef(RowKey.YouFront, 0)).x;
            float c6 = _view.WorldOf(new CellRef(RowKey.YouFront, 6)).x;
            Assert.Less(c0, c6);
        }

        [Test]
        public void BoardIsCenteredOnTheBaseColumn()
        {
            float x = _view.WorldOf(new CellRef(RowKey.YouBack, Board.BaseColumn)).x;
            Assert.AreEqual(0f, x, 0.0001f, "column 3 is the middle of a 7-wide board");
        }

        [Test]
        public void TryCellOf_RoundTripsEveryCell()
        {
            foreach (var kv in _view.Cells)
            {
                CellRef found;
                Assert.IsTrue(_view.TryCellOf(kv.Value, out found));
                Assert.AreEqual(kv.Key, found);
            }
        }

        [Test]
        public void TryCellOf_RejectsForeignTransforms()
        {
            var stray = new GameObject("stray");
            try
            {
                CellRef found;
                Assert.IsFalse(_view.TryCellOf(stray.transform, out found));
            }
            finally { Object.DestroyImmediate(stray); }
        }

        [Test]
        public void EveryCellHasACollider_SoRaycastPickingCanWork()
        {
            foreach (var kv in _view.Cells)
                Assert.IsNotNull(kv.Value.GetComponent<Collider>(),
                    kv.Key + " has no collider, so it could never be picked");
        }

        [Test]
        public void AdjacencyPreview_MatchesTheRules_ForEveryCreatureSlot()
        {
            // What the player would see highlighted on selection must be exactly Board.Neighbours,
            // and every highlighted cell must actually exist in the view.
            foreach (var row in Board.AllRows)
            for (int c = 0; c < Board.Columns; c++)
            {
                if (!Board.IsRealSlot(row, c)) continue;
                var from = new CellRef(row, c);

                var buf = new CellRef[8];
                int n = Board.Neighbours(from, buf);

                for (int i = 0; i < n; i++)
                    Assert.IsTrue(_view.Cells.ContainsKey(buf[i]),
                        "selecting " + from + " would highlight " + buf[i] + ", which the view lacks");
            }
        }

        [Test]
        public void StructureFlanks_AreBuiltButNeverOfferedAsMoveTargets()
        {
            var flank = new CellRef(RowKey.Center, 2);
            Assert.IsTrue(_view.Cells.ContainsKey(flank), "the flank is visible - structures stand there");

            var buf = new CellRef[8];
            foreach (var row in Board.AllRows)
            for (int c = 0; c < Board.Columns; c++)
            {
                if (!Board.IsRealSlot(row, c)) continue;
                int n = Board.Neighbours(new CellRef(row, c), buf);
                for (int i = 0; i < n; i++)
                    Assert.AreNotEqual(flank, buf[i], "a creature must never be offered a structure flank");
            }
        }
    }
}
