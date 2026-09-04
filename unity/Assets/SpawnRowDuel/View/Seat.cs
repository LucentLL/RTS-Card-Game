using SpawnRowDuel.Rules;

namespace SpawnRowDuel.View
{
    /// <summary>
    /// Which side of the board the person holding this screen is sitting on.
    ///
    /// The rules core has been perspective-neutral since M4: one positional board, ownership on
    /// the object, <c>Board.RowFor(owner, which)</c> owner-generic. The VIEW was not - about
    /// ninety places read <c>Side.You</c> to mean "me", which was true for as long as there was
    /// only ever one human and they were always You.
    ///
    /// Multiplayer makes that false for exactly half the players. The guest's engine has them as
    /// <c>Side.Foe</c> - and it must, because the two engines have to be bit-identical and
    /// <c>MatchSetup.NewMatch</c> draws You's deck before Foe's off one shared RNG stream, so a
    /// mirrored setup diverges on the first shuffle. Determinism is the thing being protected, so
    /// the view is what moves.
    ///
    /// Hence: <c>Side.You</c> meaning "me" becomes <see cref="Local"/>, <c>Side.Foe</c> meaning
    /// "them" becomes <see cref="Remote"/>, and the camera gains a half turn when the two differ.
    /// Places where <c>Side.You</c> genuinely names an absolute half of the board - the flat
    /// rotation a card plate lies at, the row a unit deploys to - are left alone, because those
    /// are geometry, not identity.
    ///
    /// Static, like <c>MatchController.HoldUntil</c> and <c>MatchHud.ShellSuppressed</c>, because
    /// every painter needs it and threading it through eight layers buys nothing. It is ASSIGNED
    /// on every match start rather than reset on exit: a seat that leaks out of a multiplayer game
    /// into the next campaign battle would yaw the camera, draw the AI's hand as yours, and get
    /// every command rejected - so there is no path that starts a match without stating the seat.
    /// </summary>
    public static class Seat
    {
        /// <summary>The side this player commands. Solo is always <c>Side.You</c>.</summary>
        public static Side Local { get; private set; }

        /// <summary>The other one.</summary>
        public static Side Remote { get { return Local == Side.You ? Side.Foe : Side.You; } }

        /// <summary>True when this player is sitting at the far edge and the world is upside
        /// down relative to the way it was authored. Read it for anything SCREEN-relative.</summary>
        public static bool Flipped { get { return Local != Side.You; } }

        /// <summary>Half a turn of yaw for the far seat, in degrees.</summary>
        public static float CameraYaw { get { return Flipped ? 180f : 0f; } }

        /// <summary>
        /// Which way along world +Z is TOWARD the person holding this screen: -1 at the near
        /// seat, +1 at the far one.
        ///
        /// The board is one absolute grid and the CAMERA is what turns round, so a painter asking
        /// for "the near edge of this tile" or "just in front of that card" is asking about the
        /// SEAT, not about the world. Written as a bare -Z the answer is right for exactly one of
        /// the two players: a figure planted at -Z of its cell stands at the front of its tile for
        /// the host and at the BACK of it for the guest - a billboard a tile and a half tall
        /// leaning off its own card onto the row behind. That is "structures and monsters appear
        /// far above the cards", and it is every card on the board, theirs and yours alike,
        /// because what flipped was the whole camera.
        ///
        /// Multiply any depth offset that means "towards me" by this.
        /// </summary>
        public static float TowardCamera { get { return Flipped ? 1f : -1f; } }

        public static void Take(Side local) { Local = local; }

        /// <summary>Is this side me?</summary>
        public static bool Mine(Side side) { return side == Local; }
    }
}
