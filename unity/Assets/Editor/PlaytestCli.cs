using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SpawnRowDuel.Ai;
using SpawnRowDuel.Rules;
using UnityEditor;
using UnityEngine;

namespace SpawnRowDuel.EditorPipeline
{
    /// <summary>
    /// Headless AI-vs-AI self-play over the SHIPPED rules, with the numbers a designer argues from.
    ///
    /// `tools/playtest/` plays the living JS game through a human's entry points with eight
    /// scripted playstyles - the richer lens for "what strategies emerge". But the JS is the
    /// reference the port deliberately left behind (the open middle row, row gates on builds, the
    /// sacrifice gate), so its verdicts describe a cousin of the game on Pages. This runs the C#
    /// engine the player actually gets, every mono commander against every other, N seeds each,
    /// and writes one JSON record per match: outcome, length, both economies broken down into
    /// harvested versus structure-printed mana, what was built and upgraded, what died, how the
    /// walls were struck, and what was standing at the end.
    ///
    ///   "C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe" -batchmode -nographics
    ///       -projectPath unity -executeMethod SpawnRowDuel.EditorPipeline.PlaytestCli.SelfPlayAndExit
    ///   env: SRD_PLAYTEST_SEEDS (4)  SRD_PLAYTEST_TURNS (300)  SRD_PLAYTEST_OUT (tools/playtest/out/csharp-selfplay.json)
    ///
    /// Events are drained after EVERY command rather than read off the engine at the end, so the
    /// tally never depends on how long the engine keeps them - and so a StructureRaised can be
    /// attributed to its owner while the unit is still standing on the cell the event names.
    /// </summary>
    public static class PlaytestCli
    {
        const string DatabasePath = "Assets/Game/Data/CardDatabase.asset";

        public static void SelfPlayAndExit()
        {
            try
            {
                Run();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                EditorApplication.Exit(1);
            }
        }

        static int Env(string name, int fallback)
        {
            var v = Environment.GetEnvironmentVariable(name);
            int n;
            return !string.IsNullOrEmpty(v) && int.TryParse(v, out n) ? n : fallback;
        }

        static void Run()
        {
            int seeds = Env("SRD_PLAYTEST_SEEDS", 4);
            int turns = Env("SRD_PLAYTEST_TURNS", 300);
            string outPath = Environment.GetEnvironmentVariable("SRD_PLAYTEST_OUT");
            if (string.IsNullOrEmpty(outPath))
                outPath = Path.GetFullPath(Path.Combine(Application.dataPath,
                    "../../tools/playtest/out/csharp-selfplay.json"));

            var db = AssetDatabase.LoadAssetAtPath<SpawnRowDuel.Data.CardDatabase>(DatabasePath);
            if (db == null) throw new FileNotFoundException(DatabasePath);
            var cat = db.ToCatalog();

            var monos = new List<CommanderDef>();
            foreach (var cc in cat.Commanders) if (!cc.Dual) monos.Add(cc);

            var sb = new StringBuilder(1 << 20);
            sb.Append("{\"seeds\":").Append(seeds).Append(",\"turnCap\":").Append(turns)
              .Append(",\"commanders\":[");
            for (int i = 0; i < monos.Count; i++)
                sb.Append(i > 0 ? "," : "").Append('"').Append(monos[i].Id.Value).Append('"');
            sb.Append("],\"matches\":[\n");

            int played = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int a = 0; a < monos.Count; a++)
                for (int b = 0; b < monos.Count; b++)
                    for (ulong seed = 1; seed <= (ulong)seeds; seed++)
                    {
                        if (played > 0) sb.Append(",\n");
                        Play(cat, monos[a], monos[b], seed, turns, sb);
                        played++;
                    }

            sb.Append("\n],\"elapsedSec\":").Append(sw.Elapsed.TotalSeconds.ToString("F1")).Append("}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("[playtest] " + played + " matches in " + sw.Elapsed.TotalSeconds.ToString("F0")
                + "s -> " + outPath);
        }

        /// <summary>Per-side counters. Index 0 = You, 1 = Foe.</summary>
        sealed class Tally
        {
            public int Harvest, StructMana, Drained, Kept, WallHitsTaken, WallDamageTaken,
                       Summons, CreatureDeaths, StructuresLost, Spells, Traps, Revives,
                       AttacksDeclared, Shortfalls;
            public readonly Dictionary<string, int> Built = new Dictionary<string, int>();
            public readonly Dictionary<string, int> Upgraded = new Dictionary<string, int>();
        }

        static int Ix(Side s) { return s == Side.You ? 0 : 1; }

        static void Bump(Dictionary<string, int> d, string k)
        {
            int n;
            d[k] = d.TryGetValue(k, out n) ? n + 1 : 1;
        }

        static void Play(ICardCatalog cat, CommanderDef you, CommanderDef foe, ulong seed,
                         int maxTurns, StringBuilder sb)
        {
            // Decks dealt the way TraceRecorder deals them, so a seed here is a seed there.
            var deckRng = new Pcg32(seed);
            var youDeck = DeckFactory.DeckOf(cat, you.Colors, deckRng);
            var foeDeck = DeckFactory.DeckOf(cat, foe.Colors, deckRng);
            var s = MatchSetup.NewMatch(cat, you.Id, foe.Id, youDeck, foeDeck, seed,
                                        RulesOptions.JsParity);
            var engine = new DuelEngine(s, cat);
            var policies = new[] { new ScriptedAiPolicy(Side.You), new ScriptedAiPolicy(Side.Foe) };

            var t = new[] { new Tally(), new Tally() };
            int towerShots = 0, towerDamage = 0, commands = 0, turnsPlayed = 0;
            var firstRejection = Rejection.None;
            string rejectedCmd = "";
            var ownerOf = new Dictionary<int, Side>();   // unit id -> owner, learned as they appear

            // AiDriver.Run, inlined, with an event drain after every applied command.
            while (!s.IsOver && turnsPlayed < maxTurns)
            {
                bool acted = false;
                for (int i = 0; i < policies.Length && !acted; i++)
                {
                    var cmd = policies[i].Next(engine);
                    if (cmd == null) continue;
                    var r = engine.Apply(cmd);
                    if (r.Status == CommandStatus.Rejected)
                    {
                        if (firstRejection == Rejection.None)
                        {
                            firstRejection = r.Rejection;
                            rejectedCmd = cmd.GetType().Name;
                        }
                        goto done;
                    }
                    commands++;
                    acted = true;
                    Drain(engine, s, t, ownerOf, ref towerShots, ref towerDamage);
                }
                if (acted) continue;

                if (s.Pending == null && s.Phase == TurnPhase.End)
                {
                    var next = TurnMachine.Other(s.Turn);
                    if (engine.Apply(new BeginTurnCommand(next)).Applied)
                    {
                        turnsPlayed++;
                        commands++;
                        Drain(engine, s, t, ownerOf, ref towerShots, ref towerDamage);
                        continue;
                    }
                }
                break;
            }
            done:

            string outcome = s.Outcome == MatchOutcome.YouWin ? "you"
                           : s.Outcome == MatchOutcome.FoeWin ? "foe"
                           : firstRejection != Rejection.None ? "rejected" : "timeout";

            sb.Append("{\"you\":\"").Append(you.Id.Value).Append("\",\"foe\":\"").Append(foe.Id.Value)
              .Append("\",\"seed\":").Append(seed)
              .Append(",\"outcome\":\"").Append(outcome).Append('"')
              .Append(",\"turns\":").Append(turnsPlayed).Append(",\"turnNumber\":").Append(s.TurnNumber)
              .Append(",\"commands\":").Append(commands)
              .Append(",\"rejection\":\"").Append(firstRejection == Rejection.None ? "" : firstRejection + ":" + rejectedCmd).Append('"')
              .Append(",\"towerShots\":").Append(towerShots).Append(",\"towerDamage\":").Append(towerDamage);

            var sides = new[] { Side.You, Side.Foe };
            for (int i = 0; i < 2; i++)
            {
                var p = s.P(sides[i]);
                var k = i == 0 ? "y" : "f";
                sb.Append(",\"").Append(k).Append("\":{")
                  .Append("\"life\":").Append(p.Life).Append(",\"mana\":").Append(p.Mana)
                  .Append(",\"hand\":").Append(p.Hand.Count).Append(",\"deck\":").Append(p.Deck.Count)
                  .Append(",\"grave\":").Append(p.Grave.Count)
                  .Append(",\"harvest\":").Append(t[i].Harvest).Append(",\"structMana\":").Append(t[i].StructMana)
                  .Append(",\"drained\":").Append(t[i].Drained).Append(",\"kept\":").Append(t[i].Kept)
                  .Append(",\"wallHitsTaken\":").Append(t[i].WallHitsTaken).Append(",\"wallDamageTaken\":").Append(t[i].WallDamageTaken)
                  .Append(",\"summons\":").Append(t[i].Summons).Append(",\"creatureDeaths\":").Append(t[i].CreatureDeaths)
                  .Append(",\"structuresLost\":").Append(t[i].StructuresLost)
                  .Append(",\"spells\":").Append(t[i].Spells).Append(",\"traps\":").Append(t[i].Traps)
                  .Append(",\"revives\":").Append(t[i].Revives).Append(",\"attacks\":").Append(t[i].AttacksDeclared)
                  .Append(",\"shortfalls\":").Append(t[i].Shortfalls);

                // workers standing in each zone at the end, and what is on the board
                sb.Append(",\"workers\":[");
                for (int z = 0; z < 3; z++) sb.Append(z > 0 ? "," : "").Append(p.Workers[z].Members.Count);
                sb.Append(']');

                int creatures = 0;
                var standing = new Dictionary<string, int>();
                foreach (var kv in s.ObjectsOf(sides[i]))
                {
                    var c = kv.Value as CreatureUnit;
                    if (c != null && !c.IsWorker) { creatures++; continue; }
                    var b = kv.Value as StructureUnit;
                    if (b != null) Bump(standing, b.DefId.Value);
                }
                sb.Append(",\"creatures\":").Append(creatures);
                Dict(sb, "standing", standing);
                Dict(sb, "built", t[i].Built);
                Dict(sb, "upgraded", t[i].Upgraded);
                sb.Append('}');
            }
            sb.Append('}');
        }

        static void Dict(StringBuilder sb, string key, Dictionary<string, int> d)
        {
            sb.Append(",\"").Append(key).Append("\":{");
            bool first = true;
            foreach (var kv in d)
            {
                sb.Append(first ? "" : ",").Append('"').Append(kv.Key).Append("\":").Append(kv.Value);
                first = false;
            }
            sb.Append('}');
        }

        static void Drain(DuelEngine engine, GameState s, Tally[] t, Dictionary<int, Side> ownerOf,
                          ref int towerShots, ref int towerDamage)
        {
            var events = engine.DrainEvents();
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];

                var h = ev as HarvestCollected;
                if (h != null) { t[Ix(h.Side)].Harvest += h.Amount; continue; }

                var y = ev as ManaYielded;
                if (y != null) { t[Ix(y.Side)].StructMana += y.Amount; continue; }

                var d = ev as ManaDrained;
                if (d != null) { t[Ix(d.Side)].Drained += d.Lost; t[Ix(d.Side)].Kept += d.Kept; continue; }

                var w = ev as WallStruck;
                if (w != null) { t[Ix(w.Defender)].WallHitsTaken++; t[Ix(w.Defender)].WallDamageTaken += w.Amount; continue; }

                var tf = ev as TowerFired;
                if (tf != null) { towerShots++; towerDamage += tf.Amount; continue; }

                var raised = ev as StructureRaised;
                if (raised != null)
                {
                    var o = s.At(raised.At);
                    if (o != null) { ownerOf[raised.UnitId] = o.Owner; Bump(t[Ix(o.Owner)].Built, raised.Def.Value); }
                    continue;
                }

                var up = ev as StructureUpgraded;
                if (up != null)
                {
                    Side owner;
                    if (ownerOf.TryGetValue(up.UnitId, out owner)) Bump(t[Ix(owner)].Upgraded, up.To.Value);
                    continue;
                }

                var sum = ev as UnitSummoned;
                if (sum != null)
                {
                    var o = s.At(sum.At);
                    if (o != null) { ownerOf[sum.UnitId] = o.Owner; t[Ix(o.Owner)].Summons++; }
                    continue;
                }

                var dead = ev as UnitDestroyed;
                if (dead != null)
                {
                    if (!dead.OnBoard) continue;
                    if (dead.Kind == UnitKind.Creature) t[Ix(dead.Owner)].CreatureDeaths++;
                    else if (dead.Kind == UnitKind.Building) t[Ix(dead.Owner)].StructuresLost++;
                    continue;
                }

                var sp = ev as SpellResolved;
                if (sp != null) { t[Ix(sp.Caster)].Spells++; continue; }

                var tr = ev as TrapSprung;
                if (tr != null) { t[Ix(tr.Owner)].Traps++; continue; }

                var rv = ev as CreatureRevived;
                if (rv != null) { t[Ix(rv.Side)].Revives++; continue; }

                var at = ev as AttackDeclared;
                if (at != null)
                {
                    Side owner;
                    if (ownerOf.TryGetValue(at.AttackerId, out owner)) t[Ix(owner)].AttacksDeclared++;
                    continue;
                }

                var sf = ev as WorkerShortfallSettled;
                if (sf != null) { t[Ix(sf.Side)].Shortfalls++; continue; }
            }
        }
    }
}
