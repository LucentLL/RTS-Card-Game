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
