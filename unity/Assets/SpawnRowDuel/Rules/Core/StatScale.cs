namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// Engine units → the numbers a player reads. Pure, and deliberately in Rules rather than in
    /// the view: the rules text producers (<see cref="SpellEngine.TextOf"/>,
    /// <see cref="KeywordEngine.TextOf"/>) print statlines into sentences, and a card that reads
    /// "Deal 1000 damage" beside a creature showing ♥200 is worse than either scale alone.
    ///
    /// This changes NOTHING the engine computes. The registry keeps the JS's ×500 stat scale and
    /// the 10000-point life pool, which is what the differential harness pins ply-for-ply; only
    /// the printed figure moves. See <c>View.Stat</c> for the display-side wrapper.
    /// </summary>
    public static class StatScale
    {
        /// <summary>Engine units per displayed point. 3000 attack prints as 300.</summary>
        public const int Divisor = 10;

        /// <summary>
        /// CEILING. A creature clinging on with 200 of 3000 hp must not print ♥0, and the few
        /// values the JS never rescaled (a default wardhp of 2, the +1..+3 Overcharge quirk) must
        /// not vanish either. Everything actually on the ×500 scale divides exactly, so the
        /// rounding only shows where the engine is already off-scale.
        /// </summary>
        public static int Show(int raw)
        {
            if (raw == 0) return 0;
            if (raw < 0) return -((-raw + Divisor - 1) / Divisor);
            return (raw + Divisor - 1) / Divisor;
        }

        public static string Str(int raw) { return Show(raw).ToString(); }
    }
}
