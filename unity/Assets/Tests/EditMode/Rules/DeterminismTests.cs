using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// Determinism is a shipped property, not a convention. If these fail, save games, replays,
    /// the differential harness against the JS, and any future netcode all fail with them.
    /// </summary>
    public class DeterminismTests
    {
        [Test]
        public void SameSeed_ProducesIdenticalSequences()
        {
            var a = new Pcg32(12345UL);
            var b = new Pcg32(12345UL);

            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(a.NextInt(100), b.NextInt(100), "divergence at draw " + i);
        }

        [Test]
        public void DifferentSeeds_Diverge()
        {
            var a = new Pcg32(1UL);
            var b = new Pcg32(2UL);

            bool differed = false;
            for (int i = 0; i < 100 && !differed; i++)
                if (a.NextInt(1000) != b.NextInt(1000)) differed = true;

            Assert.IsTrue(differed, "distinct seeds must not produce the same stream");
        }

        [Test]
        public void DifferentStreams_FromSameSeed_Diverge()
        {
            var a = new Pcg32(7UL, sequence: 1UL);
            var b = new Pcg32(7UL, sequence: 2UL);

            bool differed = false;
            for (int i = 0; i < 100 && !differed; i++)
                if (a.NextInt(1000) != b.NextInt(1000)) differed = true;

            Assert.IsTrue(differed, "the sequence selector must actually select a distinct stream");
        }

        [Test]
        public void NextInt_StaysInRange()
        {
            var rng = new Pcg32(99UL);
            for (int i = 0; i < 10000; i++)
            {
                int v = rng.NextInt(7);
                Assert.GreaterOrEqual(v, 0);
                Assert.Less(v, 7);
            }
        }

        [Test]
        public void NextInt_BoundOfOne_IsAlwaysZero()
        {
            var rng = new Pcg32(3UL);
            for (int i = 0; i < 100; i++) Assert.AreEqual(0, rng.NextInt(1));
        }

        [Test]
        public void NextInt_RejectsNonPositiveBound()
        {
            var rng = new Pcg32(1UL);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(-1));
        }

        [Test]
        public void NextInt_HasNoGrossModuloBias()
        {
            // 7 does not divide 2^32, so a naive modulo would skew the low buckets.
            const int buckets = 7;
            const int draws = 700000;
            var counts = new int[buckets];

            var rng = new Pcg32(2024UL);
            for (int i = 0; i < draws; i++) counts[rng.NextInt(buckets)]++;

            double expected = (double)draws / buckets;
            for (int i = 0; i < buckets; i++)
            {
                double drift = Math.Abs(counts[i] - expected) / expected;
                Assert.Less(drift, 0.02, "bucket " + i + " drifted " + (drift * 100).ToString("F2") + "%");
            }
        }

        [Test]
        public void State_AdvancesAndIsObservable()
        {
            var rng = new Pcg32(5UL);
            ulong before = rng.State;
            rng.NextInt(10);
            Assert.AreNotEqual(before, rng.State, "state must be snapshottable for save/replay");
        }

        [Test]
        public void EnumOrder_IsPinned_BecauseItIsLoadBearing()
        {
            // WorkerZone enumeration order IS the upkeep settle order (spec 02 s7.1).
            Assert.AreEqual(0, (int)WorkerZone.Back);
            Assert.AreEqual(1, (int)WorkerZone.Front);
            Assert.AreEqual(2, (int)WorkerZone.Center);
            Assert.AreEqual(3, (int)WorkerZone.Raid);

            // RowKey order IS board order top-to-bottom; distance is |difference|.
            Assert.AreEqual(0, (int)RowKey.FoeBack);
            Assert.AreEqual(1, (int)RowKey.FoeFront);
            Assert.AreEqual(2, (int)RowKey.Center);
            Assert.AreEqual(3, (int)RowKey.YouFront);
            Assert.AreEqual(4, (int)RowKey.YouBack);

            Assert.AreEqual(0, (int)TurnPhase.Upkeep);
            Assert.AreEqual(1, (int)TurnPhase.Draw);
            Assert.AreEqual(2, (int)TurnPhase.Action);
            Assert.AreEqual(3, (int)TurnPhase.End);
        }

        [Test]
        public void CellRef_IsUsableAsADictionaryKey()
        {
            var map = new Dictionary<CellRef, int>();
            foreach (var row in Board.AllRows)
                for (int c = 0; c < Board.Columns; c++)
                    map[new CellRef(row, c)] = new CellRef(row, c).Index;

            Assert.AreEqual(Board.Cells, map.Count, "no hash collisions collapsed distinct cells");
            Assert.AreEqual(new CellRef(RowKey.Center, 3).Index, map[new CellRef(RowKey.Center, 3)]);
        }
    }
}
