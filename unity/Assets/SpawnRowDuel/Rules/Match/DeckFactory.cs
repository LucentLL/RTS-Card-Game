using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// Random deck generation - deckOf(colors), 06_mana_workers.js:26-35 ported exactly, with the
    /// seeded match RNG replacing Math.random. Draw ORDER is part of the determinism contract:
    /// per colour, round(28/n) creatures then round(12/n) spells; pad from the first colour's
    /// pool; one descending Fisher-Yates; slice to DECK_SIZE.
    ///
    /// Uniform WITH replacement - deckOf enforces no copy limit; MAX_COPIES binds only the deck
    /// builder (spec 06 s3.3).
    /// </summary>
    public static class DeckFactory
    {
        public static List<HandCard> DeckOf(ICardCatalog cat, Element[] colors, Pcg32 rng)
        {
            var d = new List<HandCard>(cat.DeckSize);
            int n = colors.Length;

            for (int ci = 0; ci < colors.Length; ci++)
            {
                var col = colors[ci];
                var pool = cat.PoolOf(col);

                int creatures = RoundDiv(28, n);
                for (int i = 0; i < creatures; i++)
                    d.Add(new HandCard(pool[rng.NextInt(pool.Count)].Id, col));

                int spells = RoundDiv(12, n);
                for (int i = 0; i < spells; i++)
                    d.Add(new HandCard(cat.Spells[rng.NextInt(cat.Spells.Count)].Id, Element.None));
            }

            var pad = cat.PoolOf(colors[0]);
            while (d.Count < cat.DeckSize)
                d.Add(new HandCard(pad[rng.NextInt(pad.Count)].Id, colors[0]));

            rng.Shuffle(d);

            if (d.Count > cat.DeckSize)
                d.RemoveRange(cat.DeckSize, d.Count - cat.DeckSize);

            return d;
        }

        /// <summary>
        /// Math.round(a/n) for positive operands, in integers: JS rounds .5 UP, C# Math.Round
        /// banks - so neither is used. (2a+n)/(2n) reproduces half-up exactly.
        /// </summary>
        public static int RoundDiv(int a, int n)
        {
            return (2 * a + n) / (2 * n);
        }
    }
}
