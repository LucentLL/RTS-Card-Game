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

        public static void Take(Side local) { Local = local; }

        /// <summary>Is this side me?</summary>
        public static bool Mine(Side side) { return side == Local; }
    }
}
