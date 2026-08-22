using System.Collections.Generic;
using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// The element accent - `--ec` in the reference stylesheet - resolved from the CATALOG rather
    /// than a table copied into the view. `ElementDef` already carries colour, accent, deep and the
    /// kanji, imported from the same registry the rules read, so a palette change is a data change.
    ///
    /// Every element-tinted thing on a card threads through here: the cost circle, the banner
    /// underline, the art frame ring, the type lozenge and the outer border (spec 09 §6.1).
    /// </summary>
    public sealed class ElementPalette
    {
        public struct Swatch
        {
            public Color Color;      // the element's mid tone - the accent line
            public Color Accent;     // its highlight
            public Deep Deep;        // its shadow, as a struct so the default is never black-on-black
            public string Glyph;     // the kanji, drawn in the gem
            public string Name;
        }

        public struct Deep
        {
            public Color Value;
            public static implicit operator Color(Deep d) { return d.Value; }
        }

        readonly Dictionary<Element, Swatch> _swatches = new Dictionary<Element, Swatch>();
        readonly Swatch _neutral;

        public ElementPalette(ICardCatalog catalog)
        {
            // Neutral cards (spells, most structures) borrow a parchment gold rather than an element
            _neutral = new Swatch
            {
                Color = Hex("#b9a26a"), Accent = Hex("#e6d6a8"),
                Deep = new Deep { Value = Hex("#5c4a22") }, Glyph = "◇", Name = "Neutral",
            };

            if (catalog == null) return;
            foreach (var el in catalog.Elements)
            {
                _swatches[el.El] = new Swatch
                {
                    Color = Hex(el.ColorHex),
                    Accent = Hex(el.AccentHex),
                    Deep = new Deep { Value = Hex(el.DeepHex) },
                    Glyph = el.Glyph,
                    Name = el.Name,
                };
            }
        }

        public Swatch Of(Element el)
        {
            Swatch s;
            return _swatches.TryGetValue(el, out s) ? s : _neutral;
        }

        public static Color Hex(string hex)
        {
            Color c;
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out c)) return c;
            return new Color(0.72f, 0.64f, 0.42f);
        }

        /// <summary>`color-mix(in srgb, a P%, b)` - the stylesheet's own blend, in one place.</summary>
        public static Color Mix(Color a, Color b, float aPercent)
        {
            return Color.Lerp(b, a, Mathf.Clamp01(aPercent));
        }
    }
}
