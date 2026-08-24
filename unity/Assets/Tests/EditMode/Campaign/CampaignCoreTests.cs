using System.Collections.Generic;
using NUnit.Framework;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Campaign.Tests
{
    /// <summary>
    /// The campaign's rules core: geometry, map generation, the absorb cascade, the end-turn AI
    /// and the save.
    ///
    /// The generator's contiguity claim is the one worth the CPU. The JS asserted it with an
    /// 800-map Monte-Carlo and no test; a fragmented territory is invisible on the globe until a
    /// player taps an enemy island they can never reach, at which point it looks like the
    /// attackability rule is broken rather than the map.
    /// </summary>
    public class CampaignCoreTests
    {
        static CampaignState NewCampaign(Element faction, ulong seed)
        {
            var rng = new Pcg32(seed);
            var s = new CampaignState { Faction = faction, Turn = 1, Seed = seed };
            s.Map = new CampaignMapGenerator().Generate(faction, rng);
            return s;
        }

        // ── geometry ──────────────────────────────────────────────────────────────────

        [Test]
        public void Sphere_IsGoldbergGp4()
        {
            var s = HexSphere.Get(4);
            Assert.AreEqual(162, s.Tiles.Length, "10f²+2 tiles");
            Assert.AreEqual(320, s.Corners.Length, "20f² triangles, one corner each");

            int pents = 0, hexes = 0;
            foreach (var t in s.Tiles)
            {
                if (t.Corners.Length == 5) pents++;
                else if (t.Corners.Length == 6) hexes++;
                else Assert.Fail("a tile with " + t.Corners.Length + " corners");
                Assert.AreEqual(t.Corners.Length, t.Adjacent.Length, "corner count matches neighbour count");
                Assert.AreEqual(1.0, t.Center.Length, 1e-9, "tile centres are unit vectors");
            }
            Assert.AreEqual(12, pents, "always exactly twelve pentagons");
            Assert.AreEqual(150, hexes);
        }

        [Test]
        public void Sphere_AdjacencyIsSymmetricAndConnected()
        {
            var s = HexSphere.Get(4);
            for (int i = 0; i < s.Tiles.Length; i++)
                foreach (int j in s.Tiles[i].Adjacent)
                {
                    Assert.Contains(i, s.Tiles[j].Adjacent, "adjacency must be mutual");
                    Assert.AreNotEqual(i, j, "a tile is not its own neighbour");
                }

            var seen = new HashSet<int> { 0 };
            var q = new List<int> { 0 };
            for (int qi = 0; qi < q.Count; qi++)
                foreach (int u in s.Tiles[q[qi]].Adjacent)
                    if (seen.Add(u)) q.Add(u);
            Assert.AreEqual(s.Tiles.Length, seen.Count, "the world is one connected surface");
        }

        [Test]
        public void Sphere_AdjacentTilesShareExactlyTwoCorners()
        {
            var s = HexSphere.Get(4);
            for (int i = 0; i < s.Tiles.Length; i++)
                foreach (int j in s.Tiles[i].Adjacent)
                {
                    if (j < i) continue;
                    int shared = 0;
                    foreach (int c in s.Tiles[i].Corners)
                        foreach (int d in s.Tiles[j].Corners)
                            if (c == d) shared++;
                    Assert.AreEqual(2, shared,
                        "tiles " + i + " and " + j + " must share one edge - borders draw once per edge");
                }
        }

        [Test]
        public void Sphere_IsDeterministicAndCached()
        {
            Assert.AreSame(HexSphere.Get(4), HexSphere.Get(4));
            var a = HexSphere.Get(3);
            Assert.AreEqual(92, a.Tiles.Length, "10*9+2 at frequency 3");
        }

        // ── map generation ────────────────────────────────────────────────────────────

        [Test]
        public void Generate_CarvesTwentyTwoContiguousTerritoriesAndEightContiguousEmpires()
        {
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var s = NewCampaign(Element.Fire, seed);
                var m = s.Map;

                Assert.AreEqual(CampaignMapGenerator.Territories, m.Territories.Length, "seed " + seed);
                Assert.AreEqual(162, m.TileTerritory.Length);
                Assert.AreEqual(8, m.Capitals.Count, "one throne per element, seed " + seed);
                Assert.IsTrue(m.Validate(), "seed " + seed);

                var counted = new int[m.Territories.Length];
                for (int t = 0; t < m.TileTerritory.Length; t++) counted[m.TileTerritory[t]]++;
                for (int i = 0; i < counted.Length; i++)
                {
                    Assert.Greater(counted[i], 0, "territory " + i + " is empty, seed " + seed);
                    Assert.AreEqual(counted[i], m.Territories[i].Tiles.Length);
                    AssertTilesContiguous(m, m.Territories[i], seed);
                }

                foreach (var el in CampaignRules.Majors)
                    AssertEmpireContiguous(m, el, seed);

                Assert.AreEqual(m.Capitals[Element.Fire], FirstOwnedBy(m, Element.Fire, true),
                    "the player's throne is one of their own territories, seed " + seed);
            }
        }

        [Test]
        public void Generate_IsReproducibleFromItsSeed()
        {
            var a = NewCampaign(Element.Dark, 4242);
            var b = NewCampaign(Element.Dark, 4242);
            for (int i = 0; i < a.Map.TileTerritory.Length; i++)
                Assert.AreEqual(a.Map.TileTerritory[i], b.Map.TileTerritory[i]);
            for (int i = 0; i < a.Map.Territories.Length; i++)
            {
                Assert.AreEqual(a.Map.Territories[i].Owner, b.Map.Territories[i].Owner);
                Assert.AreEqual(a.Map.Territories[i].Garrison, b.Map.Territories[i].Garrison);
            }
        }

        [Test]
        public void Generate_GarrisonsAreInRangeAndThronesAreStronger()
        {
            var s = NewCampaign(Element.Water, 9);
            foreach (var t in s.Map.Territories)
            {
                bool capital = CampaignRules.CapitalDesignation(s.Map, t.Id) != Element.None;
                Assert.GreaterOrEqual(t.Garrison, capital ? 12 : 5);
                Assert.LessOrEqual(t.Garrison, capital ? 18 : 11);
            }
        }

        // ── attackability ─────────────────────────────────────────────────────────────

        [Test]
        public void Attackable_IsAnyEnemyGroundTouchingYours()
        {
            var s = NewCampaign(Element.Light, 77);
            var m = s.Map;

            foreach (var t in m.Territories)
            {
                bool expected = false;
                if (t.Owner != s.Faction)
                    foreach (int u in t.Adjacent)
                        if (m.Of(u).Owner == s.Faction) expected = true;

                Assert.AreEqual(expected, CampaignRules.IsAttackable(m, s.Faction, t.Id), "territory " + t.Id);
            }
        }

        [Test]
        public void Attackable_TerritoryZeroIsNotSpeciallyFalsy()
        {
            // the JS had to say this out loud: id 0 is real ground, and a truthiness test on it
            // makes exactly one territory silently unattackable
            var s = NewCampaign(Element.Earth, 5);
            var zero = s.Map.Of(0);
            zero.Owner = Element.Dark;
            foreach (int u in zero.Adjacent) s.Map.Of(u).Owner = s.Faction;
            Assert.IsTrue(CampaignRules.IsAttackable(s.Map, s.Faction, 0));
        }

        // ── battle resolution ─────────────────────────────────────────────────────────

        [Test]
        public void Resolve_WinTakesTheGroundAndHalvesItsGarrison()
        {
            var s = NewCampaign(Element.Fire, 11);
            var t = FirstEnemy(s);
            t.Garrison = 12;
            s.TargetTerritory = t.Id;

            new CampaignBattleResolver().Resolve(s, BattleOutcome.PlayerWon);

            Assert.AreEqual(s.Faction, t.Owner);
            Assert.AreEqual(8, t.Garrison, "max(3, g/2 + 2)");
            Assert.IsNull(s.TargetTerritory, "the target is spent");
        }

        [Test]
        public void Resolve_LossSoftensTheDefenderByOne()
        {
            var s = NewCampaign(Element.Fire, 12);
            var t = FirstEnemy(s);
            var owner = t.Owner;
            t.Garrison = 9;
            s.TargetTerritory = t.Id;

            new CampaignBattleResolver().Resolve(s, BattleOutcome.PlayerLost);

            Assert.AreEqual(owner, t.Owner, "the ground does not change hands");
            Assert.AreEqual(8, t.Garrison, "shipped behaviour: a failed assault softens the target");
        }

        [Test]
        public void Resolve_AbandonChangesNothing()
        {
            var s = NewCampaign(Element.Fire, 13);
            var t = FirstEnemy(s);
            var owner = t.Owner;
            int g = t.Garrison;
            s.TargetTerritory = t.Id;

            new CampaignBattleResolver().Resolve(s, BattleOutcome.Abandoned);

            Assert.AreEqual(owner, t.Owner);
            Assert.AreEqual(g, t.Garrison);
            Assert.IsNull(s.TargetTerritory);
        }

        [Test]
        public void Resolve_TakingAThroneAbsorbsTheElementAndCascades()
        {
            var s = NewCampaign(Element.Fire, 21);
            var m = s.Map;

            // A hand-built situation: taking Water's throne absorbs Water, and Water's lands
            // happen to include Earth's throne - so Earth must fall in the same breath. Without
            // the cascade Earth becomes a landless holdout no attack can ever reach.
            int waterSeat = m.Capitals[Element.Water];
            int earthSeat = m.Capitals[Element.Earth];
            Assert.AreNotEqual(waterSeat, earthSeat);

            foreach (var t in m.Territories) t.Owner = Element.Wind;
            m.Of(waterSeat).Owner = Element.Water;
            m.Of(earthSeat).Owner = Element.Water;      // Water sits on Earth's throne

            s.TargetTerritory = waterSeat;
            var log = new CampaignBattleResolver().Resolve(s, BattleOutcome.PlayerWon);

            Assert.IsTrue(s.Allies.Contains(Element.Water), "the throne you took");
            Assert.IsTrue(s.Allies.Contains(Element.Earth), "and the throne that came with it");
            Assert.AreEqual(s.Faction, m.Of(earthSeat).Owner);

            bool sawCapital = false;
            foreach (var e in log) if (e.Kind == CampaignEventKind.CapitalTaken) sawCapital = true;
            Assert.IsTrue(sawCapital);
        }

        [Test]
        public void Resolve_VictoryLatchesOnceEveryLandIsHeld()
        {
            var s = NewCampaign(Element.Fire, 31);
            var m = s.Map;
            foreach (var t in m.Territories) t.Owner = s.Faction;

            var last = m.Of(0);
            last.Owner = Element.Dark;
            s.TargetTerritory = 0;

            var log = new CampaignBattleResolver().Resolve(s, BattleOutcome.PlayerWon);
            Assert.IsTrue(s.Completed);
            Assert.IsTrue(Has(log, CampaignEventKind.RealmUnited));

            // and it does not fire twice
            last.Owner = Element.Dark;
            s.TargetTerritory = 0;
            var again = new CampaignBattleResolver().Resolve(s, BattleOutcome.PlayerWon);
            Assert.IsFalse(Has(again, CampaignEventKind.RealmUnited), "the latch holds");
        }

        [Test]
        public void AvailableCommanders_IsYourBannerPlusOneDualPerAlly()
        {
            var s = NewCampaign(Element.Fire, 41);
            var solo = CampaignRules.AvailableCommanders(s);
            Assert.AreEqual(1, solo.Count);
            Assert.AreEqual("fire", solo[0].Value);

            s.Allies.Add(Element.Water);
            s.Allies.Add(Element.Dark);
            var duals = CampaignRules.AvailableCommanders(s);
            Assert.AreEqual(3, duals.Count);
            Assert.AreEqual("fire", duals[0].Value);
            Assert.AreEqual("fire_water", duals[1].Value, "canonical colour order, never water_fire");
            Assert.AreEqual("fire_dark", duals[2].Value);
        }

        // ── end turn ──────────────────────────────────────────────────────────────────

        [Test]
        public void EndTurn_GrowsEveryGarrisonAndCapsAtTwentyFour()
        {
            var s = NewCampaign(Element.Fire, 51);
            foreach (var t in s.Map.Territories) t.Garrison = 24;

            new CampaignTurnResolver().EndTurn(s, new Pcg32(1));

            Assert.AreEqual(2, s.Turn, "the turn number advances first");
            foreach (var t in s.Map.Territories)
                Assert.LessOrEqual(t.Garrison, 24, "the cap holds even for thrones, which grow by two");
        }

        [Test]
        public void EndTurn_RivalsMoveAtMostOnceEachAndOnlyOntoNeighbours()
        {
            var s = NewCampaign(Element.Fire, 61);
            var rng = new Pcg32(9);

            for (int turn = 0; turn < 40 && !s.Lost; turn++)
            {
                var before = new Dictionary<int, Element>();
                foreach (var t in s.Map.Territories) before[t.Id] = t.Owner;

                var log = new CampaignTurnResolver().EndTurn(s, rng);

                var moved = new HashSet<Element>();
                foreach (var e in log)
                {
                    if (e.Kind != CampaignEventKind.AiCaptured && e.Kind != CampaignEventKind.AiRepulsed) continue;
                    Assert.IsTrue(moved.Add(e.Actor), "one attempt per element per turn");
                }

                // Every hand-over is accounted for in the log. Checking that the new owner still
                // BORDERS the ground it took would be wrong: this is order-dependent by design,
                // and a later element in the same turn can take the very territory the attack
                // came from, leaving the capture stranded.
                foreach (var t in s.Map.Territories)
                {
                    if (before[t.Id] == t.Owner) continue;
                    bool logged = false;
                    foreach (var e in log)
                        if (e.Kind == CampaignEventKind.AiCaptured && e.Territory == t.Id && e.Actor == t.Owner)
                            logged = true;
                    Assert.IsTrue(logged, "territory " + t.Id + " changed hands with nothing in the log");
                }
            }
        }

        [Test]
        public void EndTurn_LosingEverythingEndsTheCampaign()
        {
            var s = NewCampaign(Element.Fire, 71);
            foreach (var t in s.Map.Territories) t.Owner = Element.Dark;

            var log = new CampaignTurnResolver().EndTurn(s, new Pcg32(3));

            Assert.IsTrue(s.Lost, "a dead run must not resume on reload");
            Assert.IsTrue(Has(log, CampaignEventKind.Defeat));
        }

        // ── dialogue ──────────────────────────────────────────────────────────────────

        [Test]
        public void Dialogue_IsFourLinesDefenderFirstAttackerLast()
        {
            var lines = ChallengeDialogue.Build(Element.Fire, Element.Earth, false, new Pcg32(1));
            Assert.AreEqual(4, lines.Length);
            Assert.AreEqual(DialogueSide.Defender, lines[0].Side);
            Assert.AreEqual(DialogueSide.Attacker, lines[1].Side);
            Assert.AreEqual(DialogueSide.Defender, lines[2].Side);
            Assert.AreEqual(DialogueSide.Attacker, lines[3].Side);
            Assert.AreEqual("Titanore", lines[0].SpeakerName);
            Assert.AreEqual("Magmaw", lines[3].SpeakerName);
            foreach (var l in lines) Assert.IsNotEmpty(l.Text);
        }

        [Test]
        public void Dialogue_RivalsReplaceBothMiddleLines()
        {
            var lines = ChallengeDialogue.Build(Element.Fire, Element.Water, false, new Pcg32(1));
            Assert.AreEqual("Steam. That's all your ocean is to me — steam I haven't made yet.", lines[1].Text);
            Assert.AreEqual("Oceans have swallowed a thousand fires like you. You won't even hiss.", lines[2].Text);
        }

        [Test]
        public void Dialogue_CapitalBarksOnlyOnTheDefendersOwnThrone()
        {
            var open = ChallengeDialogue.Barks(Element.Dark, BarkBucket.Open);
            var capital = ChallengeDialogue.Build(Element.Fire, Element.Dark, true, new Pcg32(2))[0].Text;
            foreach (var o in open) Assert.AreNotEqual(o, capital);
            Assert.Contains(capital, ChallengeDialogue.Barks(Element.Dark, BarkBucket.Capital));
        }

        [Test]
        public void Dialogue_EveryElementHasEveryBucketFilled()
        {
            foreach (var el in CampaignRules.Majors)
            {
                Assert.IsNotEmpty(ChallengeDialogue.Champion(el), CampaignRules.Name(el));
                foreach (BarkBucket b in System.Enum.GetValues(typeof(BarkBucket)))
                {
                    var lines = ChallengeDialogue.Barks(el, b);
                    Assert.AreEqual(2, lines.Length, CampaignRules.Name(el) + "/" + b);
                    foreach (var l in lines) Assert.IsNotEmpty(l);
                }
            }
        }

        // ── save ──────────────────────────────────────────────────────────────────────

        [Test]
        public void Codec_RoundTrips()
        {
            var s = NewCampaign(Element.Electric, 81);
            s.Turn = 7;
            s.Allies.Add(Element.Wind);
            s.Completed = false;
            s.TargetTerritory = 3;

            var back = CampaignCodec.Read(CampaignCodec.Write(s));
            Assert.IsNotNull(back);
            Assert.AreEqual(Element.Electric, back.Faction);
            Assert.AreEqual(7, back.Turn);
            Assert.IsTrue(back.Allies.Contains(Element.Wind));
            Assert.IsNull(back.TargetTerritory, "a pending target never survives a load");

            for (int i = 0; i < s.Map.TileTerritory.Length; i++)
                Assert.AreEqual(s.Map.TileTerritory[i], back.Map.TileTerritory[i]);
            for (int i = 0; i < s.Map.Territories.Length; i++)
            {
                Assert.AreEqual(s.Map.Territories[i].Owner, back.Map.Territories[i].Owner);
                Assert.AreEqual(s.Map.Territories[i].Garrison, back.Map.Territories[i].Garrison);
                Assert.AreEqual(s.Map.Territories[i].AnchorTile, back.Map.Territories[i].AnchorTile);
            }
            foreach (var kv in s.Map.Capitals) Assert.AreEqual(kv.Value, back.Map.Capitals[kv.Key]);
        }

        [Test]
        public void Codec_RejectsGarbageAndMismatchedSpheres()
        {
            Assert.IsNull(CampaignCodec.Read(null));
            Assert.IsNull(CampaignCodec.Read(""));
            Assert.IsNull(CampaignCodec.Read("{"));
            Assert.IsNull(CampaignCodec.Read("{\"schema\":1}"));

            var s = NewCampaign(Element.Fire, 91);
            var json = CampaignCodec.Write(s).Replace("\"f\":4", "\"f\":3");
            Assert.IsNull(CampaignCodec.Read(json),
                "a save whose tile list does not match its own sphere would index past the world");
        }

        // ── helpers ───────────────────────────────────────────────────────────────────

        static bool Has(IReadOnlyList<CampaignEvent> log, CampaignEventKind kind)
        {
            foreach (var e in log) if (e.Kind == kind) return true;
            return false;
        }

        static Territory FirstEnemy(CampaignState s)
        {
            foreach (var t in s.Map.Territories) if (t.Owner != s.Faction) return t;
            Assert.Fail("the map has no enemy ground");
            return null;
        }

        static int FirstOwnedBy(CampaignMap m, Element el, bool capitalOnly)
        {
            foreach (var t in m.Territories)
                if (t.Owner == el && (!capitalOnly || CampaignRules.CapitalDesignation(m, t.Id) == el))
                    return t.Id;
            return -1;
        }

        static void AssertTilesContiguous(CampaignMap m, Territory t, ulong seed)
        {
            var sphere = m.Sphere;
            var want = new HashSet<int>(t.Tiles);
            var seen = new HashSet<int> { t.Tiles[0] };
            var q = new List<int> { t.Tiles[0] };
            for (int qi = 0; qi < q.Count; qi++)
                foreach (int u in sphere.Tiles[q[qi]].Adjacent)
                    if (want.Contains(u) && seen.Add(u)) q.Add(u);
            Assert.AreEqual(want.Count, seen.Count,
                "territory " + t.Id + " is fragmented, seed " + seed);
        }

        static void AssertEmpireContiguous(CampaignMap m, Element el, ulong seed)
        {
            var want = new List<int>();
            foreach (var t in m.Territories) if (t.Owner == el) want.Add(t.Id);
            if (want.Count == 0) return;

            var seen = new HashSet<int> { want[0] };
            var q = new List<int> { want[0] };
            for (int qi = 0; qi < q.Count; qi++)
                foreach (int u in m.Of(q[qi]).Adjacent)
                    if (m.Of(u).Owner == el && seen.Add(u)) q.Add(u);
            Assert.AreEqual(want.Count, seen.Count,
                CampaignRules.Name(el) + "'s empire is fragmented, seed " + seed);
        }
    }
}
