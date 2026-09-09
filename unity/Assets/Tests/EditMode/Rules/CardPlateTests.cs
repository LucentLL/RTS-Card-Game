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
        /// The sheet has a cell for every square a face-up card can stand on. If it did not, a
        /// busy board would start handing units the rastered fallback frame - which is the look
        /// this whole change exists to get rid of, appearing only in the late game, on some cards,
        /// which is the worst way for it to come back.
        /// </summary>
        [Test]
        public void TheCardSheet_CannotRunOutOfCells()
        {
            Assert.GreaterOrEqual(PlateFaceAtlas.Cells, SpawnRowDuel.Rules.Board.Cells,
                "the card sheet is smaller than the board it has to cover");
        }

        /// <summary>Each cell's UV rectangle is its own, inside the sheet, and tiles with its
        /// neighbours - one transposed row or column and every card wears another card's face.
        /// </summary>
        [Test]
        public void EachSheetCell_HasItsOwnPatchOfTheTexture()
        {
            for (int i = 0; i < PlateFaceAtlas.Cells; i++)
            {
                var uv = PlateFaceAtlas.UvOf(i);
                Assert.AreEqual(1f / PlateFaceAtlas.Cols, uv.width, 0.0001f);
                Assert.AreEqual(1f / PlateFaceAtlas.Rows, uv.height, 0.0001f);
                Assert.GreaterOrEqual(uv.xMin, -0.0001f);
                Assert.GreaterOrEqual(uv.yMin, -0.0001f);
                Assert.LessOrEqual(uv.xMax, 1.0001f);
                Assert.LessOrEqual(uv.yMax, 1.0001f);

                // by CENTRES, not by Rect.Overlaps: neighbouring cells share an edge exactly, and
                // whether a shared edge counts as an overlap is a question about float rounding
                // rather than about the sheet
                var mid = uv.center;
                for (int j = 0; j < i; j++)
                    Assert.IsFalse(PlateFaceAtlas.UvOf(j).Contains(mid),
                        "cells " + j + " and " + i + " sample the same patch");
            }

            // row 0 is the TOP of the sheet, because UI Toolkit measures down from the top left
            // and a texture measures up from the bottom left
            Assert.AreEqual(1f, PlateFaceAtlas.UvOf(0).yMax, 0.0001f,
                "the first row is not at the top of the sheet");
        }

        /// <summary>
        /// THE SAME ROTATION FOR BOTH SEATS, which is the 2026-09-08 change and the one worth a
        /// test of its own, because "the foe's cards are upside down" was a deliberate feature for
        /// two weeks and its removal looks like a regression.
        ///
        /// It was bought by a pair of quaternions: the foe's plate turned a half circle, every
        /// readout on it counter-rotated so no figure came out upside down. That needs the numbers
        /// to be separate objects from the card, and the plate is one composited texture now - so
        /// the only choice left was a foe card that reads or one that does not.
        /// </summary>
        [Test]
        public void EveryPlate_LiesTheSameWayUp_WhoeverOwnsIt()
        {
            Assert.IsNull(typeof(CardPlateLayer).GetMethod("RotationFor"),
                "something is still choosing a plate's rotation by who OWNS the card");

            SpawnRowDuel.View.Seat.Take(Side.You);
            Assert.AreEqual(CardPlateLayer.FlatOnTile, CardPlateLayer.FacingTheSeat);

            // ...but it still answers to the SEAT. The guest's camera is yawed a half turn, so a
            // plate pinned to the board's absolute geometry lies upside down for them - the whole
            // board of them, which is exactly what the first version of this shipped.
            SpawnRowDuel.View.Seat.Take(Side.Foe);
            Assert.AreEqual(CardPlateLayer.HalfTurnOnTile, CardPlateLayer.FacingTheSeat);
            Assert.That(CardPlateLayer.HalfTurnOnTile * Vector3.up,
                        Is.EqualTo(Vector3.back).Using(V3()), "the far seat's card is not turned round");

            // a rotation preserves handedness; a mirror flips it - and a mirrored card reads as a
            // rotated one until an asymmetric illustration lands in it
            Assert.That(Vector3.Dot(Vector3.Cross(CardPlateLayer.HalfTurnOnTile * Vector3.right,
                                                  CardPlateLayer.HalfTurnOnTile * Vector3.up),
                                    CardPlateLayer.HalfTurnOnTile * Vector3.forward),
                        Is.EqualTo(Vector3.Dot(Vector3.Cross(CardPlateLayer.FlatOnTile * Vector3.right,
                                                             CardPlateLayer.FlatOnTile * Vector3.up),
                                               CardPlateLayer.FlatOnTile * Vector3.forward)).Within(0.001f),
                        "the far seat's plate is mirrored, not rotated");

            SpawnRowDuel.View.Seat.Take(Side.You);
        }
        /// <summary>
        /// Each plaque is laid straight over its own band, so each has to BE that band's shape -
        /// anything else stretches its contents by the difference. They are two different shapes:
        /// the ability box is 0.211 of the card's height and the stat strip 0.155.
        ///
        /// Checked in TEXELS rather than as an aspect ratio, because an aspect is a quotient and
        /// the height is an integer: the same half-texel of rounding is 0.008 of the ability box's
        /// aspect and 0.028 of the strip's, so one tolerance cannot mean one thing for both. A
        /// texel is the error, so a texel is what to bound.
        ///
        /// And the two bands are not the same WIDTH. The stat strip runs the card's full width
        /// less its border; the ability box is inset by ArtInsetX on each side, the same as the
        /// art window above it - a plaque rastered at the card's width overhangs it by a third.
        /// </summary>
        [Test]
        public void EachPlaque_HasTheAspectOfTheBandItLandsIn()
        {
            Assert.AreEqual(CardPlateTextures.RuleBoxW * CardPlateTextures.RulesH
                            * CardPlateTextures.H
                            / (CardPlateTextures.W * (1f - 2f * CardPlateTextures.ArtInsetX)),
                CardPlateTextures.RuleBoxH, 0.5f, "the ability plaque is not the shape of its band");

            Assert.AreEqual(CardPlateTextures.StatBoxW * CardPlateTextures.StatsH
                            * CardPlateTextures.H / (float)CardPlateTextures.W,
                CardPlateTextures.StatBoxH, 0.5f, "the stat plaque is not the shape of its band");
        }

        /// <summary>
        /// It prints, and it prints INSIDE the plaque. A layout slip puts the whole line past the
        /// right edge, where every clipped draw is silently dropped and the texture comes out as
        /// a bare strip - which looks like a card with no stats rather than like a bug.
        /// </summary>
        [Test]
        public void StatLineRaster_PrintsInkInsideItsOwnBox()
        {
            var sprite = CardPlateTextures.StatLine(300, -2, 450, true, true, Color.white);
            var px = sprite.texture.GetPixels();
            int w = sprite.texture.width, h = sprite.texture.height;

            // The strip is BLACK now rather than parchment, so its ink is what is lighter than
            // it - and the worker chip's pill is dark, which would pass a dark-pixel test on its
            // own while every number was drawn into the void.
            int ink = Lit(px, w, 4, w - 4, 4, h - 4);
            Assert.Greater(ink, 400, "the statline drew (almost) nothing");

            // and it did not all end up jammed against the right edge
            Assert.Less(Lit(px, w, w - 5, w - 1, 2, h - 2), 4,
                "the statline is running off the edge of its plaque");
        }

        /// <summary>Light, opaque texels in a window - the strip is the card's black footer, so
        /// what is printed on it is what is brighter than it.</summary>
        static int Lit(Color[] px, int w, int x0, int x1, int y0, int y1)
        {
            int n = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    var c = px[y * w + x];
                    if (c.a > 0.5f && c.r + c.g + c.b > 1.5f) n++;
                }
            return n;
        }

        /// <summary>Dark, opaque texels in a window - for the plaques that ARE parchment.</summary>
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
            var three = CardPlateTextures.StatLine(300, -2, 450, true, true, Color.white);
            var two = CardPlateTextures.StatLine(0, 0, 450, false, false, Color.white);

            Assert.AreNotSame(three, two, "the two statlines share a cache entry");
            Assert.AreSame(two, CardPlateTextures.StatLine(0, 0, 450, false, false, Color.white),
                "the same statline rastered twice");
        }

        /// <summary>
        /// The health TINT is part of the cache key. It is what the meter's colour became, so a
        /// unit that drops past a threshold has to re-raster - and a unit that does not must not,
        /// or a long fight costs a texture per point of damage.
        /// </summary>
        [Test]
        public void StatLineRaster_KeysOnTheHealthTint()
        {
            var hot = CardPlateTextures.StatLine(300, 0, 450, true, false, Color.red);
            var cool = CardPlateTextures.StatLine(300, 0, 450, true, false, Color.green);

            Assert.AreNotSame(hot, cool, "the two tints share a cache entry");
            Assert.AreSame(hot, CardPlateTextures.StatLine(300, 0, 450, true, false, Color.red));
        }

        /// <summary>
        /// The ability line prints on parchment, and an empty one is NO SPRITE - a blank plaque
        /// laid over the frame's own ability box is a visible seam saying nothing.
        /// </summary>
        [Test]
        public void BriefRaster_PrintsOnParchment_AndIsNothingWhenEmpty()
        {
            Assert.IsNull(CardPlateTextures.Brief(""), "an empty ability line rastered a plaque");
            Assert.IsNull(CardPlateTextures.Brief(null));

            var one = CardPlateTextures.Brief("UPKEEP -3");
            Assert.AreSame(one, CardPlateTextures.Brief("UPKEEP -3"), "rastered the same line twice");

            var px = one.texture.GetPixels();
            int w = one.texture.width, h = one.texture.height;
            Assert.Greater(Ink(px, w, 4, w - 4, 4, h - 4), 200, "the ability line drew nothing");
            Assert.Less(Ink(px, w, w - 5, w - 1, 2, h - 2), 4,
                "the ability line is running off the edge of its plaque");
        }

        /// <summary>
        /// Two lines have to fit the box one line fits - the raster is a fixed size, so the only
        /// thing that can give is the cell. A second row that overflows is silently clipped, and
        /// a rule read half way is a different rule.
        /// </summary>
        [Test]
        public void BriefRaster_ShrinksToFitASecondRow()
        {
            var two = CardPlateTextures.Brief("UPKEEP -3\nOVERCHARGE");
            var px = two.texture.GetPixels();
            int w = two.texture.width, h = two.texture.height;

            // ink in BOTH halves of the plaque, and none of it in the margins
            Assert.Greater(Ink(px, w, 4, w - 4, 4, h / 2), 100, "the first row is missing");
            Assert.Greater(Ink(px, w, 4, w - 4, h / 2, h - 4), 100, "the second row is missing");
            Assert.Less(Ink(px, w, w - 5, w - 1, 2, h - 2), 4, "a row ran off the right edge");
        }

        /// <summary>
        /// The mana cost prints DARK on a PALE ring - the disc it lands in is a light tint of the
        /// element, and the board's other numbers are white ringed black, which would sink into
        /// it. Cached by value, since the ring rather than the element carries the contrast.
        /// </summary>
        [Test]
        public void CostPip_IsDarkOnALightRing_AndCachedByValue()
        {
            var pip = CardPlateTextures.Pip(5);
            Assert.AreSame(pip, CardPlateTextures.Pip(5));
            Assert.AreNotSame(pip, CardPlateTextures.Pip(6));

            var px = pip.texture.GetPixels();
            int light = 0, dark = 0;
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a < 0.5f) continue;
                if (px[i].r > 0.8f) light++; else dark++;
            }
            Assert.Greater(dark, 40, "the figure itself never got drawn");
            Assert.Greater(light, 40, "the figure has no ring, so it vanishes into the disc");
        }

        /// <summary>The health figure's three colours turn at the quarter and the half - not at
        /// some other pair of numbers - and the healthy one is the hand card's own salmon, so a
        /// card at full health reads the same in both places.</summary>
        [Test]
        public void HpInk_TurnsAtAQuarterAndAHalf()
        {
            Assert.AreEqual(CardPlateTextures.HpInk(100, 100), CardPlateTextures.HpInk(51, 100));
            Assert.AreNotEqual(CardPlateTextures.HpInk(51, 100), CardPlateTextures.HpInk(50, 100));
            Assert.AreEqual(CardPlateTextures.HpInk(50, 100), CardPlateTextures.HpInk(26, 100));
            Assert.AreNotEqual(CardPlateTextures.HpInk(26, 100), CardPlateTextures.HpInk(25, 100));

            Assert.AreEqual(new Color(1f, 0.60f, 0.54f), CardPlateTextures.HpInk(100, 100),
                "a healthy card no longer matches the hand card's heart");
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
