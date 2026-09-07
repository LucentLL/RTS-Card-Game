using SpawnRowDuel.Rules;

namespace SpawnRowDuel.View
{
    /// <summary>
    /// What the view CALLS a thing, where the card's own name is not the whole answer.
    ///
    /// The Base is the one structure that is somebody's: each player has exactly one root and
    /// "Base" alone does not say whose. So it is named for its owner - the player's display name
    /// in a networked match, the commander's in a solo one, which is the same name the log's
    /// "Fire vs Water" header already uses - and every other structure is called what its card
    /// is called. The card itself still reads "Base": on the inspect face and in the build tree
    /// the owner is never in doubt.
    /// </summary>
    public static class Names
    {
        static string _local = "", _remote = "";

        /// <summary>Set once per match, by whoever starts it.</summary>
        public static void SetOwners(string local, string remote)
        {
            _local = local ?? "";
            _remote = remote ?? "";
        }

        public static string Owner(Side side)
        {
            return side == Seat.Local ? _local : _remote;
        }

        public static string Of(StructureUnit b)
        {
            if (b == null) return "";
            string card = string.IsNullOrEmpty(b.Name) ? b.DefId.Value : b.Name;
            if (b.DefId.Value != "foundry") return card;

            string owner = Owner(b.Owner);
            return owner.Length == 0 ? card : owner + "'s " + card;
        }
    }
}
