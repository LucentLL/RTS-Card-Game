using SpawnRowDuel.Rules;

namespace SpawnRowDuel.View
{
    /// <summary>
    /// The display side of <see cref="StatScale"/>: the same divisor, plus the glyphs the view
    /// wraps a number in.
    ///
    /// It exists because the DISPLAY was wrong in two different ways at once. The board overlays
    /// and the deck builder divided by 500, so a 3000-attack dragon read as "6"; the card frames
    /// and the wall rails printed the raw number, so the same dragon read "3000" in the hand and
    /// the wall it was hitting read "10000". Two scales on one screen, with no relationship a
    /// player could hold in their head.
    ///
    /// One divisor now: ten. The dragon is 300, a keep is 250, a life pool is 1000, and a hit for
    /// 1500 takes 150 off it - so three hits reads as three hits.
    ///
    /// The engine is untouched. See <see cref="StatScale"/>.
    /// </summary>
    public static class Stat
    {
        public const int Divisor = StatScale.Divisor;

        /// <summary>An engine number as the player sees it.</summary>
        public static int Show(int raw) { return StatScale.Show(raw); }

        public static string Num(int raw) { return StatScale.Str(raw); }

        /// <summary>"⚔300" - attack, with the swords.</summary>
        public static string Atk(int raw) { return "⚔" + StatScale.Str(raw); }

        /// <summary>"♥250" - hit points, with the heart.</summary>
        public static string Hp(int raw) { return "♥" + StatScale.Str(raw); }

        /// <summary>"300/250" - the statline as the board overlay writes it.</summary>
        public static string Line(int attack, int hp)
        {
            return StatScale.Str(attack) + "/" + StatScale.Str(hp);
        }
    }
}
