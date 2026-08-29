using NUnit.Framework;
using SpawnRowDuel.View.Cards;
using UnityEngine;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// The two things about a card lying on a tile that a screenshot answers slowly and a test
    /// answers instantly: which way round it faces, and whether its frame still adds up.
    ///
    /// Orientation is worth pinning because it is the one part of the plate layer where a sign
    /// error is invisible in code review and obvious only in a render - a mirrored card and a
    /// correct one differ by one cross product.
    /// </summary>
    public class CardPlateTests
    {
        [Test]
        public void PlateLiesFlat_UnmirroredAndRightWayUp()
        {
            var q = CardPlateLayer.FlatOnTile;

            // the card's own right edge points to the viewer's right - a mirrored plate fails here
            Assert.That(q * Vector3.right, Is.EqualTo(Vector3.right).Using(V3()),
                "the plate is mirrored left-to-right");

            // and its top edge points AWAY from the camera, which sits on the -Z side of the board
            Assert.That(q * Vector3.up, Is.EqualTo(Vector3.forward).Using(V3()),
                "the plate is upside down");

            // consequence, stated so it is not mistaken for a bug later: the quad faces DOWN, and
            // renders only because Sprites/Default is Cull Off
            Assert.That(q * Vector3.forward, Is.EqualTo(Vector3.down).Using(V3()));
        }

        [Test]
        public void PlateFrame_PartitionsTheWholeCard()
        {
            float sum = CardPlateTextures.BannerH + CardPlateTextures.ArtH
                      + CardPlateTextures.RulesH + CardPlateTextures.StatsH;
            Assert.AreEqual(1f, sum, 0.002f, "the four bands of the frame must tile the card");
        }

        /// <summary>
        /// The raster frame is the CardFace frame at another scale, so its proportions have to be
        /// the ones CardFace's flex weights resolve to: banner and stats are each 0.215 of the
        /// WIDTH, and what is left splits 3.3 : 1.45 between the art window and the ability box.
        /// </summary>
        [Test]
        public void PlateFrame_MatchesTheCardFaceProportions()
        {
            float band = 0.215f / CardFace.Aspect;              // width fraction -> height fraction
            float rest = 1f - 2f * band;

            Assert.AreEqual(band, CardPlateTextures.BannerH, 0.006f);
            Assert.AreEqual(band, CardPlateTextures.StatsH, 0.006f);
            Assert.AreEqual(rest * 3.3f / 4.75f, CardPlateTextures.ArtH, 0.006f);
            Assert.AreEqual(rest * 1.45f / 4.75f, CardPlateTextures.RulesH, 0.006f);
        }

        /// <summary>
        /// The foe's card is UPSIDE DOWN, not mirrored. Those two differ by a determinant and by
        /// nothing you can see on a symmetrical frame - a reflected plate reads as a rotated one
        /// until an asymmetric illustration lands in it, which is a bug found in a screenshot
        /// weeks later.
        /// </summary>
        [Test]
        public void FoePlate_IsAHalfTurnOfYours_NotAMirror()
        {
            var you = CardPlateLayer.FlatOnTile;
            var foe = CardPlateLayer.FoeOnTile;

            // the card's own top now points at the near edge of the board: upside down from here
            Assert.That(foe * Vector3.up, Is.EqualTo(Vector3.back).Using(V3()),
                "the foe's card is not turned round");
            Assert.That(foe * Vector3.right, Is.EqualTo(Vector3.left).Using(V3()));

            // a rotation preserves the basis's handedness; a mirror flips it
            Assert.That(Vector3.Dot(Vector3.Cross(foe * Vector3.right, foe * Vector3.up),
                                    foe * Vector3.forward),
                        Is.EqualTo(Vector3.Dot(Vector3.Cross(you * Vector3.right, you * Vector3.up),
                                               you * Vector3.forward)).Within(0.001f),
                "the foe's plate is mirrored, not rotated");

            // and it is still lying flat, facing the same way as yours
            Assert.That(foe * Vector3.forward, Is.EqualTo(you * Vector3.forward).Using(V3()));
        }

        /// <summary>
        /// The whole point of the counter-rotation: a health meter on a foe card is the same way
        /// up as one on yours. A card can be upside down; a number cannot.
        /// </summary>
        [Test]
        public void AReadoutOnAFoePlate_ComesOutTheSameWayUpAsOnYours()
        {
            var readout = CardPlateLayer.FoeOnTile * CardPlateLayer.UprightOnFoeCard;

            Assert.That(readout * Vector3.up,
                        Is.EqualTo(CardPlateLayer.FlatOnTile * Vector3.up).Using(V3()));
            Assert.That(readout * Vector3.right,
                        Is.EqualTo(CardPlateLayer.FlatOnTile * Vector3.right).Using(V3()));
        }

        /// <summary>
        /// The statline texture is laid straight over the ability box, so it has to BE that box's
        /// shape - anything else stretches the numbers by the difference.
        /// </summary>
        [Test]
        public void StatLineRaster_HasTheAbilityBoxAspect()
        {
            float box = CardPlateTextures.W / (CardPlateTextures.H * CardPlateTextures.RulesH);
            float raster = CardPlateTextures.RuleBoxW / (float)CardPlateTextures.RuleBoxH;
            Assert.AreEqual(box, raster, 0.02f, "the statline plaque is not the shape of its band");
        }

        /// <summary>
        /// It prints, and it prints INSIDE the plaque. A layout slip puts the whole line past the
        /// right edge, where every clipped draw is silently dropped and the texture comes out as
        /// blank parchment - which looks like a card with no stats rather than like a bug.
        /// </summary>
        [Test]
        public void StatLineRaster_PrintsInkInsideItsOwnBox()
        {
            var sprite = CardPlateTextures.StatLine(300, -2, 450, true, true);
            var px = sprite.texture.GetPixels();
            int w = sprite.texture.width, h = sprite.texture.height;

            // INSIDE the plaque's own border, which is ink by any colour test and would pass this
            // on its own while the numbers were drawn into the void
            int ink = Ink(px, w, 4, w - 4, 4, h - 4);
            Assert.Greater(ink, 400, "the statline drew (almost) nothing");

            // and it did not all end up jammed against the right edge
            Assert.Less(Ink(px, w, w - 5, w - 1, 2, h - 2), 4,
                "the statline is running off the edge of its plaque");
        }

        /// <summary>Dark, opaque texels in a window - the plaque is parchment, so ink is what is
        /// darker than it.</summary>
        static int Ink(Color[] px, int w, int x0, int x1, int y0, int y1)
        {
            int n = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    var c = px[y * w + x];
                    if (c.a > 0.5f && c.r + c.g + c.b < 1.2f) n++;
                }
            return n;
        }

        /// <summary>A structure has no attack, so it prints two fields rather than three - and a
        /// creature that neither draws nor eats a worker prints two as well.</summary>
        [Test]
        public void StatLineRaster_DropsTheFieldsAUnitDoesNotHave()
        {
            var three = CardPlateTextures.StatLine(300, -2, 450, true, true);
            var two = CardPlateTextures.StatLine(0, 0, 450, false, false);

            Assert.AreNotSame(three, two, "the two statlines share a cache entry");
            Assert.AreSame(two, CardPlateTextures.StatLine(0, 0, 450, false, false),
                "the same statline rastered twice");
        }

        /// <summary>
        /// The health number is cached by VALUE - which is what keeps the meter from costing a
        /// texture for every (hp, max) pair a long fight reaches.
        /// </summary>
        [Test]
        public void HealthNumber_IsRingedAndCachedByValue()
        {
            var num = CardPlateTextures.Num(275);
            Assert.AreSame(num, CardPlateTextures.Num(275));
            Assert.AreNotSame(num, CardPlateTextures.Num(276));

            var px = num.texture.GetPixels();
            int light = 0, dark = 0;
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a < 0.5f) continue;
                if (px[i].r > 0.8f) light++; else dark++;
            }
            Assert.Greater(light, 40, "the number itself never got drawn");
            Assert.Greater(dark, 40, "the number has no ring, so it vanishes over a green fill");
        }

        /// <summary>The three fill colours are the three the vitals chips have always used, and
        /// they change at the quarter and the half - not at some other pair of numbers.</summary>
        [Test]
        public void HealthTint_TurnsAtAQuarterAndAHalf()
        {
            Assert.AreEqual(CardPlateTextures.HealthTint(1f), CardPlateTextures.HealthTint(0.51f));
            Assert.AreNotEqual(CardPlateTextures.HealthTint(0.51f), CardPlateTextures.HealthTint(0.5f));
            Assert.AreEqual(CardPlateTextures.HealthTint(0.5f), CardPlateTextures.HealthTint(0.26f));
            Assert.AreNotEqual(CardPlateTextures.HealthTint(0.26f), CardPlateTextures.HealthTint(0.25f));
        }

        static System.Collections.IComparer V3()
        {
            return new Vector3Within(0.0005f);
        }

        sealed class Vector3Within : System.Collections.IComparer
        {
            readonly float _eps;
            public Vector3Within(float eps) { _eps = eps; }

            public int Compare(object a, object b)
            {
                return Vector3.Distance((Vector3)a, (Vector3)b) <= _eps ? 0 : 1;
            }
        }
    }
}
