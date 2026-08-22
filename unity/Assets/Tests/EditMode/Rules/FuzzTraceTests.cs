using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// M12 tier 3: fuzz, and the C# half of the shrink loop.
    ///
    /// Three of these run in the ordinary gate and are cheap: they prove the fuzzer only ever
    /// proposes commands the engine accepts, that a seed reproduces its trace exactly, and that
    /// it actually reaches the command kinds the scripted AI never emits (which is the entire
    /// reason it exists).
    ///
    /// The other two are driven by environment variables and skip themselves otherwise, because
    /// they are steps in a pipeline that node orchestrates - see tools/diffjs/fuzz.mjs:
    ///
    ///   SRD_FUZZ_OUT=&lt;dir&gt;   write fuzz traces for the JS oracle to replay
    ///   SRD_FUZZ_JOB=&lt;file&gt;  re-record one shrink round's candidate traces
    ///
    /// Unity's CLI is the only way to run C# here (no .NET SDK on this machine, DECISIONS D2), so
    /// a "CLI entry point" IS a test with a filter. That is the same trick the golden regen uses.
    /// </summary>
    public class FuzzTraceTests
    {
        const int GatePlies = 300;
        const int GateBudget = 24;

        /// <summary>Commander pairings, including a dual, so deck shapes vary between seeds.</summary>
        static readonly string[][] Pairs =
        {
            new[] { "fire", "water" },
            new[] { "forest", "dark" },
            new[] { "light", "electric" },
            new[] { "earth", "wind" },
            new[] { "fire_water", "light_dark" },
            new[] { "water_forest", "earth_electric" },
        };

        static string[] PairFor(int i) { return Pairs[i % Pairs.Length]; }

        [Test]
        public void Fuzz_ProposesNothingIllegal()
        {
            // The fuzzer asks CanApply before it offers a command; if Apply then REJECTS one, the
            // validator and the executor disagree - which is a real engine defect, and one no
            // scripted trace would ever surface.
            for (ulong seed = 1; seed <= 4; seed++)
            {
                var pair = PairFor((int)seed);
                var t = TraceRecorder.RecordFuzz(TestData.Catalog, pair[0], pair[1],
                                                 100 + seed, seed, GatePlies, GateBudget);

                Assert.AreEqual(Rejection.None, t.Rejection,
                    "fuzz seed " + seed + " proposed a command CanApply had approved");
                Assert.Greater(t.Plies, 60, "fuzz seed " + seed + " stalled almost immediately");
            }
        }

        [Test]
        public void Fuzz_SameSeedSameTrace()
        {
            var a = TraceRecorder.RecordFuzz(TestData.Catalog, "fire", "water", 101, 1,
                                             GatePlies, GateBudget);
            var b = TraceRecorder.RecordFuzz(TestData.Catalog, "fire", "water", 101, 1,
                                             GatePlies, GateBudget);
            Assert.AreEqual(a.Json, b.Json, "a fuzz seed must reproduce its trace exactly");
        }

        [Test]
        public void Fuzz_ReachesCommandsTheScriptedAiNever()
        {
            var fuzzKinds = new HashSet<string>();
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var pair = PairFor((int)seed);
                CollectKinds(TraceRecorder.RecordFuzz(TestData.Catalog, pair[0], pair[1],
                                                      200 + seed, seed, GatePlies, GateBudget).Json,
                             fuzzKinds);
            }

            var aiKinds = new HashSet<string>();
            CollectKinds(TraceRecorder.RecordSelfPlay(TestData.Catalog, "fire", "water", 909, 60).Json,
                         aiKinds);

            // These three are structurally unreachable for the scripted AI: it never sets a card
            // face-down, so it never pours into or flips one, and it never moves banked mana.
            // Each is a rules path the differential harness could not otherwise compare at all.
            foreach (var kind in new[] { "pour", "flip", "sendMana" })
            {
                Assert.IsTrue(fuzzKinds.Contains(kind), "the fuzzer never reached " + kind);
                Assert.IsFalse(aiKinds.Contains(kind), "the scripted AI reached " + kind
                    + " - this assertion is now testing nothing");
            }

            Assert.IsTrue(fuzzKinds.Count >= aiKinds.Count,
                "the fuzzer covered fewer command kinds than the AI: fuzz=["
                + string.Join(",", Sorted(fuzzKinds)) + "] ai=[" + string.Join(",", Sorted(aiKinds)) + "]");

            UnityEngine.Debug.Log("fuzz kinds: " + string.Join(",", Sorted(fuzzKinds))
                + "\nai kinds:   " + string.Join(",", Sorted(aiKinds)));
        }

        // ---- pipeline entry points ------------------------------------------------------------

        [Test]
        public void GenerateFuzzTraces()
        {
            var outDir = Env("SRD_FUZZ_OUT");
            if (string.IsNullOrEmpty(outDir))
            {
                Assert.Ignore("set SRD_FUZZ_OUT to generate fuzz traces (tools/diffjs/fuzz.mjs does)");
                return;
            }

            int count = EnvInt("SRD_FUZZ_COUNT", 8);
            int plies = EnvInt("SRD_FUZZ_PLIES", 400);
            int budget = EnvInt("SRD_FUZZ_BUDGET", GateBudget);
            int seed0 = EnvInt("SRD_FUZZ_SEED0", 1);
            var poisonName = Env("SRD_FUZZ_POISON");

            Directory.CreateDirectory(outDir);
            var index = new StringBuilder();
            index.Append("{\"traces\":[");

            for (int i = 0; i < count; i++)
            {
                ulong fuzzSeed = (ulong)(seed0 + i);
                var pair = PairFor(seed0 + i);
                var t = TraceRecorder.RecordFuzz(TestData.Catalog, pair[0], pair[1],
                                                 1000 + fuzzSeed, fuzzSeed, plies, budget,
                                                 PoisonNamed(poisonName));

                var name = "fuzz-" + fuzzSeed;
                File.WriteAllText(Path.Combine(outDir, name + ".json"), t.Json);
                File.WriteAllText(Path.Combine(outDir, name + ".proj.jsonl"), t.Projections);

                if (i > 0) index.Append(',');
                index.Append("{\"name\":\"").Append(name)
                     .Append("\",\"fuzzSeed\":").Append(fuzzSeed)
                     .Append(",\"you\":\"").Append(pair[0]).Append("\",\"foe\":\"").Append(pair[1])
                     .Append("\",\"plies\":").Append(t.Plies)
                     .Append(",\"over\":").Append(t.Over ? "true" : "false")
                     .Append(",\"rejection\":\"").Append(t.Rejection).Append("\"}");

                Assert.AreEqual(Rejection.None, t.Rejection,
                    "fuzz seed " + fuzzSeed + " proposed a command CanApply had approved");
            }

            index.Append("]}");
            File.WriteAllText(Path.Combine(outDir, "index.json"), index.ToString());
        }

        /// <summary>
        /// One shrink ROUND: a job file names a source trace and a list of ply sets to drop, and
        /// every candidate is re-recorded here in a single Unity run. Batching the round matters -
        /// a Unity batchmode boot costs about as much as forty jsdom replays, so the shrink loop
        /// is designed to pay it once per round rather than once per candidate.
        /// </summary>
        [Test]
        public void RunShrinkJobs()
        {
            var jobFile = Env("SRD_FUZZ_JOB");
            if (string.IsNullOrEmpty(jobFile))
            {
                Assert.Ignore("set SRD_FUZZ_JOB to run a shrink round (tools/diffjs/fuzz.mjs does)");
                return;
            }

            var job = JsonValue.Parse(File.ReadAllText(jobFile));
            var doc = TraceParser.Parse(File.ReadAllText(job.StrReq("trace", "job")));
            var outDir = job.StrReq("out", "job");
            var poisonName = job.StrOrNull("poison");
            Directory.CreateDirectory(outDir);

            var jobs = job.ArrReq("jobs", "job");
            var result = new StringBuilder();
            result.Append("{\"results\":[");

            for (int i = 0; i < jobs.Count; i++)
            {
                var one = jobs[i];
                var id = one.StrReq("id", "job entry");
                var drop = new HashSet<int>();
                var arr = one.ArrReq("drop", "job entry");
                for (int k = 0; k < arr.Count; k++) drop.Add(arr[k].AsInt);

                var t = TraceRecorder.RecordFromCommands(TestData.Catalog, doc, drop,
                                                         PoisonNamed(poisonName));

                File.WriteAllText(Path.Combine(outDir, id + ".json"), t.Json);
                File.WriteAllText(Path.Combine(outDir, id + ".proj.jsonl"), t.Projections);

                if (i > 0) result.Append(',');
                result.Append("{\"id\":\"").Append(id)
                      .Append("\",\"plies\":").Append(t.Plies)
                      .Append(",\"rejection\":\"").Append(t.Rejection).Append("\"}");
            }

            result.Append("]}");
            File.WriteAllText(Path.Combine(outDir, "jobs-result.json"), result.ToString());
        }

        /// <summary>
        /// The one canned poison: the third harvest of the match quietly pays a mana it should
        /// not. It is a rules divergence with a single, identifiable cause and a clear minimal
        /// reproducer (three harvests and whatever makes them legal), which is exactly what a
        /// shrinker should be measured against.
        /// </summary>
        public static TraceRecorder.Poison PoisonNamed(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name == "manaOnThirdHarvest")
            {
                int harvests = 0;
                return (s, cmd, ply) =>
                {
                    if (!(cmd is HarvestCommand)) return;
                    if (++harvests == 3) s.P(cmd.Actor).Mana += 1;
                };
            }
            throw new ArgumentException("unknown poison " + name);
        }

        static void CollectKinds(string traceJson, HashSet<string> into)
        {
            const string marker = "\"cmd\":{\"t\":\"";
            int at = 0;
            while (true)
            {
                at = traceJson.IndexOf(marker, at, StringComparison.Ordinal);
                if (at < 0) return;
                at += marker.Length;
                int end = traceJson.IndexOf('"', at);
                into.Add(traceJson.Substring(at, end - at));
                at = end;
            }
        }

        static List<string> Sorted(HashSet<string> set)
        {
            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        static string Env(string name) { return Environment.GetEnvironmentVariable(name); }

        static int EnvInt(string name, int fallback)
        {
            var v = Env(name);
            int parsed;
            return !string.IsNullOrEmpty(v) && int.TryParse(v, out parsed) ? parsed : fallback;
        }
    }
}
