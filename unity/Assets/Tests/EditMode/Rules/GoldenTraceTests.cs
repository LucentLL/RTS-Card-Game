using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// M12 tier 0: committed golden traces. A full self-play match - every command and the state
    /// hash after each one - is written down and compared byte for byte on every run.
    ///
    /// This earns its keep before the JS side of the harness exists. Right now the engine's
    /// behaviour is pinned only by tests that assert what somebody thought to assert; a golden
    /// trace pins ALL of it, so a refactor that quietly changes an iteration order or a tie-break
    /// fails here with the exact ply number rather than surviving to confuse the differential run
    /// later. It is also the exact artefact the JS replay will consume.
    ///
    /// Regenerating: delete the file (or set SRD_REGEN_GOLDEN=1) and run the suite once. Do that
    /// ONLY when the change in behaviour is intended, and read the diff before committing it.
    /// </summary>
    public class GoldenTraceTests
    {
        const int Turns = 60;

        static string GoldenDir
        {
            get
            {
                return Path.GetFullPath(
                    Path.Combine(Application.dataPath, "../../tools/diffjs/golden"));
            }
        }

        static void CheckGolden(string name, ulong seed, string you, string foe)
        {
            var trace = TraceRecorder.RecordSelfPlay(TestData.Catalog, you, foe, seed, Turns);

            Assert.AreEqual(Rejection.None, trace.Rejection,
                "the AI proposed an illegal command while recording " + name);
            Assert.Greater(trace.Plies, 40, "a trace this short records nothing useful");

            Directory.CreateDirectory(GoldenDir);
            var path = Path.Combine(GoldenDir, name + ".json");

            // A per-ply projection dump, for pinpointing a divergence the hashes only flag. Big,
            // gitignored, and written only when asked for.
            if (System.Environment.GetEnvironmentVariable("SRD_TRACE_PROJ") == "1")
                File.WriteAllText(Path.Combine(GoldenDir, name + ".proj.jsonl"), trace.Projections);

            bool regen = System.Environment.GetEnvironmentVariable("SRD_REGEN_GOLDEN") == "1";

            if (regen || !File.Exists(path))
            {
                File.WriteAllText(path, trace.Json);
                Assert.Ignore("wrote a new golden trace at " + path
                    + " - review the diff and commit it deliberately");
                return;
            }

            var expected = File.ReadAllText(path).Replace("\r\n", "\n");
            var actual = trace.Json.Replace("\r\n", "\n");
            if (expected == actual) return;

            // Point at the first divergent line: with one ply per line, that IS the ply.
            var e = expected.Split('\n');
            var a = actual.Split('\n');
            int i = 0;
            while (i < e.Length && i < a.Length && e[i] == a[i]) i++;
            Assert.Fail("golden trace " + name + " diverged at line " + (i + 1) + "\n  expected: "
                + (i < e.Length ? e[i] : "<end of file>") + "\n  actual:   "
                + (i < a.Length ? a[i] : "<end of file>"));
        }

        [Test]
        public void Golden_FireVsWater_IsUnchanged()
        {
            CheckGolden("selfplay-fire-water", 909, "fire", "water");
        }

        [Test]
        public void Golden_ForestVsDark_IsUnchanged()
        {
            // a different pair reaches different keywords: chrysalis on one side, reap on the other
            CheckGolden("selfplay-forest-dark", 4242, "forest", "dark");
        }

        [Test]
        public void Golden_LightVsElectric_IsUnchanged()
        {
            // ward tokens and overcharge discharges
            CheckGolden("selfplay-light-electric", 77, "light", "electric");
        }

        [Test]
        public void ATraceReplaysToTheSameHashes_FromTheSameSeed()
        {
            // The property the differential harness rests on: the recording is reproducible, so
            // any difference the JS side shows is the JS's, not recording noise.
            var a = TraceRecorder.RecordSelfPlay(TestData.Catalog, "fire", "water", 909, Turns);
            var b = TraceRecorder.RecordSelfPlay(TestData.Catalog, "fire", "water", 909, Turns);
            Assert.AreEqual(a.Json, b.Json);
        }

        [Test]
        public void EveryCommandTypeHasAWireForm()
        {
            // An UNKNOWN here means the JS replay could not be asked to perform it, which would
            // show up as a silent coverage hole in the harness rather than a failure.
            var cell = new CellRef(RowKey.YouBack, 2);
            ICommand[] all =
            {
                new BeginTurnCommand(Side.You), new HarvestCommand(Side.You),
                new DrawForTurnCommand(Side.You), new EndTurnCommand(Side.You),
                new ResolveCombatCommand(Side.You),
                new UpkeepPayCommand(Side.You, cell, 1), new UpkeepSacrificeCommand(Side.You, cell, 1),
                new MoveUnitCommand(Side.You, cell, cell, 1),
                new PlayCardCommand(Side.You, 0, PlayMode.Summon, cell),
                new BuildStructureCommand(Side.You, new StructId("foundry"), Element.None, cell),
                new UpgradeStructureCommand(Side.You, cell, 1, new StructId("keep")),
                new PourIntoChargeCommand(Side.You, cell, 1, 2),
                new FlipChargeCommand(Side.You, cell, 1),
                new SendBankedManaCommand(Side.You, cell, cell),
                new DeclareAttackCommand(Side.You, cell, 1, new WallTarget(Side.Foe)),
                new DeclareAttackCommand(Side.You, cell, 1, new UnitTarget(cell, 2)),
                new DeclareAttackCommand(Side.You, cell, 1,
                    new WorkerStackTarget(Side.Foe, WorkerZone.Back)),
                new RespondCommand(Side.You, new BlockersChosen(new[] { UnitRef.Cell(cell, 3) })),
                new RespondCommand(Side.You, new IndexChosen(1)),
                new RespondCommand(Side.You, TrapChosen.Passed),
            };

            foreach (var cmd in all)
            {
                var wire = TraceRecorder.Describe(cmd);
                Assert.IsFalse(wire.Contains("UNKNOWN"), cmd.GetType().Name + " has no wire form");
                StringAssert.StartsWith("{\"t\":\"", wire);
            }
        }
    }
}
