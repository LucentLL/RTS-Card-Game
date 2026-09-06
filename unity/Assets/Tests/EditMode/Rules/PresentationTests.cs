using System.Collections.Generic;
using NUnit.Framework;
using SpawnRowDuel.Campaign;
using SpawnRowDuel.View;
using SpawnRowDuel.View.Cards;
using SpawnRowDuel.View.Campaign;
using SpawnRowDuel.View.World;
using UnityEngine;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// The three presentation faults reported from the phone, each pinned by the smallest thing
    /// that can fail: the scale the numbers are printed at, the card's footprint on its tile, and
    /// which way the globe's triangles face.
    ///
    /// The last one is the reason this file exists. "The campaign globe is hollow" was not a
    /// missing surface - every tile was there, wound INWARD, so the near half of the sphere was
    /// culled away and what you were looking at was the inside of the far half. It rendered as a
    /// plausible globe for as long as nothing was underneath it, which is exactly the kind of
    /// wrong a screenshot cannot fail and a dot product can.
    /// </summary>
    public class PresentationTests
    {
        static Vector3 ToVector3(Vec3 v) { return new Vector3((float)v.X, (float)v.Y, (float)v.Z); }

        // ── the display scale ─────────────────────────────────────────────────────────────

        [Test]
        public void DisplayScale_IsOneTenth_AcrossTheWholeGame()
        {
            Assert.AreEqual(10, StatScale.Divisor);
            Assert.AreEqual(300, Stat.Show(3000), "a 3000-attack dragon reads as 300");
            Assert.AreEqual(1000, Stat.Show(10000), "a full life pool reads as 1000");
            Assert.AreEqual(250, Stat.Show(2500), "a keep reads as 250");
            Assert.AreEqual("⚔300", Stat.Atk(3000));
            Assert.AreEqual("♥250", Stat.Hp(2500));
            Assert.AreEqual("300/250", Stat.Line(3000, 2500));
        }

        [Test]
        public void DisplayScale_NeverPrintsZeroForSomethingStillAlive()
        {
            // the ×500 scale divides exactly; the ceiling is for the values the JS never rescaled
            Assert.AreEqual(1, Stat.Show(2), "a default wardhp of 2 is not nothing");
            Assert.AreEqual(1, Stat.Show(1));
            Assert.AreEqual(50, Stat.Show(500), "a creature clinging on at 500 hp is not dead");
            Assert.AreEqual(0, Stat.Show(0), "and nothing is still nothing");
        }

        // ── the battlefield ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Every duel was fought in a meadow: Grass is the static default and nothing but the
        /// settings menu ever changed it. The field is now rolled from the opening state hash,
        /// which is the one number both peers have computed and agreed on before either has
        /// moved - so the two of them are standing in the same place without a word on the wire.
        ///
        /// Pinned as a mapping rather than as a match, because "it picked a different one this
        /// time" is not something a test can watch for.
        /// </summary>
        [Test]
        public void Battlefield_IsRolled_AndReachesEveryField()
        {
            var seen = new HashSet<BiomeId>();
            for (ulong h = 1; h <= 400; h++) seen.Add(MatchController.BattlefieldFor(h));

            // Bound to the list rather than to a number, so adding a field to the roll cannot
            // leave this assertion quietly testing less than it says it does.
            Assert.GreaterOrEqual(seen.Count, MatchController.Battlefields.Length,
                "every battlefield in the list comes up");
            Assert.IsFalse(seen.Contains(BiomeId.Shore),
                "Shore is the tide biome - half its board spends the match under water");

            for (ulong h = 1; h <= 50; h++)
                Assert.AreEqual(MatchController.BattlefieldFor(h), MatchController.BattlefieldFor(h),
                    "and the same match always lands in the same field");
        }

        /// <summary>
        /// Every field in the list is its OWN field.
        ///
        /// `Biomes.Of` ends in `default: return Grass()`, so a BiomeId added to the enum and to
        /// `All` but not to the switch comes back as a meadow under its own name - no compiler
        /// warning, no failing test, and a probe shot that looks perfectly reasonable. Distinct
        /// names are the cheapest thing that cannot be true by accident.
        /// </summary>
        [Test]
        public void EveryBattlefield_HasAnEntryOfItsOwn()
        {
            var names = new HashSet<string>();
            foreach (var id in Biomes.All)
            {
                var look = Biomes.Of(id);
                Assert.IsFalse(string.IsNullOrEmpty(look.Name), id + " has no name");
                Assert.IsTrue(names.Add(look.Name),
                    id + " came back as \"" + look.Name + "\" - it is missing its case in Biomes.Of");
            }
            Assert.AreEqual(Biomes.All.Length, names.Count);
        }

        /// <summary>
        /// A hollow is filled back in by MATERIAL, a ply at a time, so every field has to say how
        /// much material it has. Zero is legal and means ground that keeps every mark for the whole
        /// duel - but it is a decision, and no field should arrive at it by forgetting to set one.
        /// </summary>
        [Test]
        public void EveryBattlefield_SaysHowFastItForgets()
        {
            foreach (var id in Biomes.All)
            {
                var look = Biomes.Of(id);
                Assert.Greater(look.RefillRate, 0f,
                    look.Name + " never fills a card's hollow back in");
                Assert.Less(look.RefillRate, 0.5f,
                    look.Name + " erases a hollow in under three plies, which is a spring not a field");

                // ...AND FAST ENOUGH TO BE WATCHED. This is the other wall, and it is the one the
                // player walked into: a meadow at 0.05 took twenty plies - ten full rounds - to
                // erase a print, so in a duel that ends around turn 11 nobody had ever seen a
                // hollow fill, only hollows that were still there. "The hole should fill over
                // time" was a report about a rate, not about a missing feature.
                //
                // Fifteen plies is the ceiling: long enough that a battlefield still remembers
                // where the fighting was, short enough that the memory is visibly fading while
                // the fight is still going on.
                Assert.LessOrEqual(1f / look.RefillRate, 15f,
                    look.Name + " takes " + Mathf.CeilToInt(1f / look.RefillRate)
                    + " plies to fill a hollow - longer than most duels, so the fill is never seen");
            }
        }

        /// <summary>
        /// ...and REAL matches land in different ones. The mapping being well spread is not the
        /// same claim as the hashes it is fed being well spread, and it is the second one that
        /// was broken: every duel was a meadow.
        /// </summary>
        [Test]
        public void Battlefield_DiffersBetweenMatches()
        {
            var seen = new HashSet<BiomeId>();
            for (ulong seed = 1; seed <= 24; seed++)
            {
                var s = MatchSetup.NewMatch(TestData.Catalog, new CommanderId("fire"),
                    new CommanderId("water"), seed, RulesOptions.JsParity);
                seen.Add(MatchController.BattlefieldFor(new DuelEngine(s, TestData.Catalog).Hash()));
            }

            Assert.Greater(seen.Count, 1,
                "two dozen openings all picked the same field - the roll is not reaching the hash");
        }

        // ── what a card says it does ──────────────────────────────────────────────────────

        /// <summary>
        /// The inspect card EXPLAINS every rule it names.
        ///
        /// Reported from a live game: "you could see a card had First Strike Chrysalis, but there
        /// was no way to see what that effect did". The labels were all the game ever printed -
        /// the sentences existed, one per keyword handler, and nothing in the view read them.
        /// </summary>
        [Test]
        public void InspectText_SpellsOutEveryRuleTheCardNames()
        {
            var text = new CardTextService(TestData.Catalog);

            // a cocoon: two named rules on one card, which is the case that was reported
            var pod = text.Full(new CardId("Sap Pod"));
            StringAssert.Contains("Chrysalis", pod);
            StringAssert.Contains("Cannot attack", pod, "and what a Chrysalis actually does");
            StringAssert.Contains("Canopy Beast", pod, "and what it becomes");

            // upkeep is a flag, not a keyword - it has no handler, so its sentence lives in the
            // view next to the label that names it
            var magmaw = text.Full(new CardId("Magmaw"));
            StringAssert.Contains("Upkeep", magmaw);
            StringAssert.Contains("worker", magmaw, "an upkeep number means workers held off a row");

            // ...and nothing says its own name twice: the brief line is a list of the same labels
            Assert.AreEqual(1, Occurrences(magmaw, "Upkeep"), "the label is not repeated");
        }

        static int Occurrences(string haystack, string needle)
        {
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0)
            { n++; i += needle.Length; }
            return n;
        }

        // ── the card on its tile ──────────────────────────────────────────────────────────

        [Test]
        public void PlateFillsItsTile_Exactly()
        {
            var go = new GameObject("BoardUnderTest");
            try
            {
                var board = go.AddComponent<BoardView>();
                var foot = CardPlateLayer.Footprint(board);

                // BoardView scales a cell (CellSize, thickness, CellSize * RowStretch)
                Assert.AreEqual(board.CellSize, foot.x, 0.0001f, "the card is the tile's width");
                Assert.AreEqual(board.CellSize * board.RowStretch, foot.y, 0.0001f,
                    "the card is the tile's depth");

                // it is a tile, not a pitch: the gutter between tiles stays bare ground
                Assert.Less(foot.x, board.ColPitch);
                Assert.Less(foot.y, board.RowPitch);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void StandeeStandsAtTheFrontOfItsOwnTile()
        {
            var go = new GameObject("BoardUnderTest");
            try
            {
                var board = go.AddComponent<BoardView>();
                float half = board.CellSize * board.RowStretch * 0.5f;

                foreach (bool structure in new[] { false, true })
                {
                    float feet = StandeeLayer.FeetOffset(board, structure);
                    Assert.Greater(feet, 0f, "the figure stands in FRONT of its cell centre");
                    Assert.Less(feet, half, "and never past its own tile's near edge");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// ...and "the front" is the end of the tile the PLAYER is at, which is not the same world
        /// direction for the two of them.
        ///
        /// This is the guest's half of "structures and monsters appear far above the cards". The
        /// board never mirrors - the camera yaws 180 instead - so a feet offset written as a bare
        /// world -Z plants the guest's figures at the FAR edge of every tile, leaning a billboard
        /// a tile and a half tall off the back of its own card and onto the row behind it. It is
        /// invisible in solo, where Seat.Local is always You, and it is every unit on the board
        /// the moment somebody joins.
        /// </summary>
        [Test]
        public void StandeeFront_TurnsRoundWithTheSeat()
        {
            var go = new GameObject("BoardUnderTest");
            var was = Seat.Local;
            try
            {
                var board = go.AddComponent<BoardView>();

                Seat.Take(Side.You);
                Assert.AreEqual(-1f, Seat.TowardCamera, "the host looks up the board from -Z");
                float host = StandeeLayer.FeetShift(board, false).z;
                Assert.Less(host, 0f, "the host's figures stand at the -Z edge of their tiles");

                Seat.Take(Side.Foe);
                Assert.AreEqual(1f, Seat.TowardCamera, "the guest's camera is yawed a half turn");
                float guest = StandeeLayer.FeetShift(board, false).z;
                Assert.Greater(guest, 0f, "so the guest's figures stand at the +Z edge instead");

                Assert.AreEqual(-host, guest, 0.0001f, "same offset, opposite ends");

                foreach (bool structure in new[] { false, true })
                    Assert.AreEqual(StandeeLayer.FeetOffset(board, structure),
                        Mathf.Abs(StandeeLayer.FeetShift(board, structure).z), 0.0001f,
                        "the seat decides the direction and nothing else");
            }
            finally
            {
                Seat.Take(was);                    // never leave a seat behind for the next test
                Object.DestroyImmediate(go);
            }
        }

        // ── taps that must not reach the board ────────────────────────────────────────────

        /// <summary>
        /// A control drawn over the field blocks the field.
        ///
        /// Legacy Input cannot see IMGUI consume an event, so BoardInput has to be TOLD where the
        /// HUD is. It used to be told about panels only, by hand, which is why tapping ⚔ WALL
        /// aimed at nothing and selected the card underneath it instead. Registration lives in
        /// MatchHud.Btn now - drawing a control registers it - and this pins the mechanism the
        /// buttons rely on.
        /// </summary>
        [Test]
        public void ARegisteredControl_BlocksTheBoardUnderIt()
        {
            HudLayout.Recompute();
            HudLayout.ClearControls();

            float s = HudLayout.Scale;
            HudLayout.Control(new Rect(100f, 50f, 60f, 24f));      // GUI units

            // Blocks() takes a bottom-left-origin mouse position, as legacy Input reports it
            Assert.IsTrue(HudLayout.Blocks(Mouse(130f * s, 60f * s)), "inside the control");
            Assert.IsFalse(HudLayout.Blocks(Mouse(130f * s, 200f * s)), "well below the control");

            HudLayout.ClearControls();
            Assert.IsFalse(HudLayout.Blocks(Mouse(130f * s, 60f * s)),
                "a control that stopped being drawn stops blocking");
        }

        /// <summary>GUI space is top-left origin; Input.mousePosition is bottom-left.</summary>
        static Vector2 Mouse(float x, float yFromTop)
        {
            return new Vector2(x, Screen.height - yFromTop);
        }

        // ── the globe ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Every triangle of the globe faces OUT of the sphere.
        ///
        /// This is the whole of "the campaign globe is hollow". The tile fans were wound the wrong
        /// way round, so `Cull Back` removed the near hemisphere and left you looking at the inside
        /// of the far one - a shell with the lights on, which is what "hollow" looks like when the
        /// tiles are all still there. It only became obvious when a crust was added underneath and
        /// the crust drew over the plates it was supposed to be beneath.
        /// </summary>
        [Test]
        public void EveryGlobeTriangleFacesOutOfTheSphere()
        {
            var go = new GameObject("GlobeUnderTest");
            try
            {
                var globe = go.AddComponent<GlobeView>();
                var map = new CampaignMapGenerator().Generate(Element.Fire, new Pcg32(20260824));
                globe.Build(map, Element.Fire);

                var mesh = globe.TileMesh;
                Assert.IsNotNull(mesh, "the globe built no tile mesh");

                var verts = mesh.vertices;
                var tris = mesh.triangles;
                var owner = globe.TriangleTiles;
                var tiles = map.Sphere.Tiles;
                Assert.Greater(tris.Length, 0);
                Assert.AreEqual(tris.Length / 3, owner.Length, "every triangle names its tile");

                int inward = 0;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var a = verts[tris[i]];
                    var b = verts[tris[i + 1]];
                    var c = verts[tris[i + 2]];

                    // Unity's front-face normal for (v0,v1,v2) is cross(v1-v0, v2-v0).
                    var n = Vector3.Cross(b - a, c - a).normalized;
                    var centroid = ((a + b + c) / 3f).normalized;

                    // A tile has two kinds of face and they point out of the sphere in two
                    // different senses: a plate's face and its crust point RADIALLY, and the
                    // plate's side points sideways, away from its own tile's axis - where the
                    // radial test reads zero and its sign is noise. Whichever sense a triangle
                    // belongs to, it has to be pointing out in that sense.
                    var axis = ToVector3(tiles[owner[i / 3]].Center);
                    var sideways = (centroid - axis * Vector3.Dot(centroid, axis)).normalized;

                    float outward = Mathf.Max(Vector3.Dot(n, centroid), Vector3.Dot(n, sideways));
                    if (outward < 0.2f) inward++;
                }

                Assert.AreEqual(0, inward,
                    inward + " of " + (tris.Length / 3) + " globe triangles face inward - the near "
                    + "side is being culled and you are seeing the inside of the far side");
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// The crust closes the sphere. Every tile carries a full-width fan at the sphere's own
        /// radius under its inset plate, so the gaps between plates have a floor rather than a
        /// view of the skybox.
        /// </summary>
        [Test]
        public void EveryTileCarriesACrustAtTheSpheresOwnRadius()
        {
            var go = new GameObject("GlobeUnderTest");
            try
            {
                var globe = go.AddComponent<GlobeView>();
                var map = new CampaignMapGenerator().Generate(Element.Water, new Pcg32(7));
                globe.Build(map, Element.Water);

                var verts = globe.TileMesh.vertices;
                int atSurface = 0, extruded = 0;
                for (int i = 0; i < verts.Length; i++)
                {
                    float r = verts[i].magnitude;
                    if (Mathf.Abs(r - GlobeView.Radius) < 0.001f) atSurface++;
                    else if (Mathf.Abs(r - GlobeView.Extrude) < 0.001f) extruded++;
                    else Assert.Fail("a globe vertex sits at radius " + r);
                }

                // per tile: 1 + ring extruded (the plate), ring + 1 + ring at the surface
                Assert.Greater(extruded, 0);
                Assert.Greater(atSurface, extruded, "the crust and the skirt outnumber the plate");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
