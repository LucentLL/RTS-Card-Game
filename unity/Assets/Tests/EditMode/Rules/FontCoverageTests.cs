using System.Collections.Generic;
using NUnit.Framework;
using SpawnRowDuel.EditorPipeline;
using UnityEditor;
using UnityEngine.TextCore.Text;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// The tofu gate. GAPS listed "the font/glyph plan for the 76 non-ASCII glyphs" as an open P0,
    /// and the failure mode it describes is silent: a card reads "Detonate □□□□", a screenshot
    /// looks fine at thumbnail size, and nobody notices until someone plays it.
    ///
    /// So the glyph vocabulary is generated (tools/export_glyphs.mjs → docs/unity/spec/glyphs.txt)
    /// and asserted here against the built font chain. Adding a glyph to the UI without a font that
    /// can draw it is a red test, not a discovery.
    /// </summary>
    public class FontCoverageTests
    {
        static FontAsset Load(string name)
        {
            return AssetDatabase.LoadAssetAtPath<FontAsset>("Assets/Game/Fonts/" + name + ".asset");
        }

        static readonly string[] Faces =
        {
            "SRD-Display-Regular", "SRD-Display-Bold", "SRD-Display-Black",
            "SRD-Body-Regular", "SRD-Body-Bold", "SRD-Body-Italic",
        };

        [Test]
        public void EveryFaceExists_AndCarriesTheFallbackChain()
        {
            foreach (var name in Faces)
            {
                var face = Load(name);
                Assert.IsNotNull(face, name + " is missing - run tools/regen-fonts.sh");
                Assert.IsNotEmpty(face.fallbackFontAssetTable,
                    name + " has no fallback chain, so every symbol and kanji renders as tofu");
            }
        }

        [Test]
        public void EveryRequiredGlyphHasAHome()
        {
            var glyphs = FontPipeline.RequiredGlyphs();
            Assert.Greater(glyphs.Length, 50, "the generated glyph list looks empty");

            foreach (var name in Faces)
            {
                var face = Load(name);
                Assert.IsNotNull(face, name + " is missing - run tools/regen-fonts.sh");

                var chain = new List<FontAsset>(face.fallbackFontAssetTable);
                var missing = FontPipeline.MissingGlyphs(glyphs, face, chain);

                Assert.IsEmpty(missing, name + " cannot draw: " + string.Join(", ", missing));
            }
        }

        [Test]
        public void BakedFallbacksCarryNoSourceFont()
        {
            // A static fallback that kept its source font drags the whole 5.3 MB kanji face into
            // the player build to draw eleven characters.
            foreach (var name in new[] { "SRD-Symbols2", "SRD-Symbols", "SRD-Math", "SRD-Emoji", "SRD-CJK" })
            {
                var font = Load(name);
                if (font == null) continue;             // routing may leave a fallback unused
                Assert.AreEqual(AtlasPopulationMode.Static, font.atlasPopulationMode, name);
                Assert.IsNull(font.sourceFontFile, name + " still references its TTF");
            }
        }
    }
}
