namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// The 9 elements. Divine is NOT deckable - it is reserved for Ace/Boss/God cards.
    /// None is the neutral identity carried by spells and traps, which have no element.
    ///
    /// Since the colored-mana removal, element is a SYNERGY ATTRIBUTE ONLY: it drives theming
    /// and elemental keyword effects, never what mana a card costs.
    /// </summary>
    public enum Element : byte
    {
        None = 0,
        Fire = 1, Water = 2, Earth = 3, Wind = 4,
        Forest = 5, Electric = 6, Light = 7, Dark = 8,
        Divine = 9,
    }

    /// <summary>The 8 creature keywords. Exactly one per creature, or None.</summary>
    public enum Keyword : byte
    {
        None = 0,
        Detonate = 1,
        Undertow = 2,
        Entrench = 3,
        Ward = 4,
        Reap = 5,
        Chrysalis = 6,
        Scour = 7,
        Overcharge = 8,
    }

    public enum Tribe : byte { None = 0, Dragon = 1 }

    public enum Subtype : byte { None = 0, Warrior = 1, Wizard = 2 }

    public enum SpellEffect : byte
    {
        None = 0,
        Burn = 1, Raze = 2, Pitfall = 3, Chain = 4, Bounce = 5, Thornmail = 6,
    }

    /// <summary>When a face-down trap may fire. A trap is never armed on the turn it was set.</summary>
    public enum TrapTrigger : byte { None = 0, Summon = 1, Attack = 2 }

    public enum StructEffect : byte
    {
        None = 0,
        Mana = 1,
        Villager = 2,
        Vault = 3,
        Wall = 4,
        Damage = 5,
        Revive = 6,
    }

    public enum MatchOutcome : byte
    {
        InProgress = 0,
        YouWin = 1,
        FoeWin = 2,
        Abandoned = 3,
    }

    /// <summary>Which rows a structure may be built in.</summary>
    public enum RowGate : byte { Any = 0, BackOnly = 1, FrontOnly = 2, CenterOnly = 3 }
}
