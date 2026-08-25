using NUnit.Framework;
using SpawnRowDuel.Campaign;
using SpawnRowDuel.View;
using SpawnRowDuel.View.Cards;
using SpawnRowDuel.View.Campaign;
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
