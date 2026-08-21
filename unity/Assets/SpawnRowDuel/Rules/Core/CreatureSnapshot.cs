namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// The in-play statline a creature carries when it leaves the board and stays a card:
    /// Undertow's bounce, Riptide's bounce, and every trip through the graveyard (which the
    /// Reliquary reads back out).
    ///
    /// This exists because the JS never returned the CATALOG card - handcardFromCreature
    /// (06_mana_workers.js:112) and toGrave (07_structures.js:67) both copy the LIVE fields, so a
    /// hatched Chrysalis form comes back hatched and a Thornmail-hardened defender keeps its
    /// +500/+1000. Reconstructing from the registry instead silently un-buffs and un-hatches
    /// creatures (the M8 debt this closes).
    ///
    /// Deliberately NOT copied, exactly as the JS omits them: `cnt` (a bounced cocoon restarts its
    /// swell), `oc` (banked Overcharge is lost), `bank`, and `token` - a bounced token comes back
    /// as an ordinary card. Health is MAX health: a card in hand is never damaged.
    ///
    /// `HasValue` false is the normal case - a deck card has no history and resolves through the
    /// catalog. Making it a field rather than Nullable&lt;T&gt; keeps hand/deck/grave arrays flat
    /// for the codec and the hash.
    /// </summary>
    public readonly struct CreatureSnapshot
    {
        public readonly bool HasValue;

        public readonly string Name;
        public readonly int Attack, Health, Cost, Upkeep;
        public readonly bool FirstStrike, Entrench;
        public readonly Keyword Keyword;
        public readonly int Detonate, Reap, WardHp, Grow, Hatch;
        public readonly CardId Into;
        public readonly Tribe Tribe;
        public readonly Subtype Subtype;

        public CreatureSnapshot(string name, int attack, int health, int cost, int upkeep,
                                bool firstStrike, bool entrench, Keyword keyword,
                                int detonate, int reap, int wardHp, int grow, int hatch,
                                CardId into, Tribe tribe, Subtype subtype)
        {
            HasValue = true;
            Name = name ?? "";
            Attack = attack; Health = health; Cost = cost; Upkeep = upkeep;
            FirstStrike = firstStrike; Entrench = entrench;
            Keyword = keyword;
            Detonate = detonate; Reap = reap; WardHp = wardHp; Grow = grow; Hatch = hatch;
            Into = into; Tribe = tribe; Subtype = subtype;
        }

        /// <summary>handcardFromCreature / toGrave's creature branch: the LIVE statline, at max HP.</summary>
        public static CreatureSnapshot From(CreatureUnit c)
        {
            return new CreatureSnapshot(c.Name, c.Attack, c.MaxHp, c.Cost, c.Upkeep,
                c.FirstStrike, c.Entrench, c.Keyword,
                c.Detonate, c.Reap, c.WardHp, c.Grow, c.Hatch,
                c.Into, c.Tribe, c.Subtype);
        }
    }
}
