namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// mkCre / mkBld (06_mana_workers.js:90-95): catalog template to board instance. This is
    /// where the registry's nulls collapse exactly the way the JS defaults collapse them -
    /// det/reap/grow/hatch to 0, wardhp to 2 (the un-rescaled `t.wardhp||2` quirk, spec 06
    /// s6.3), colour to the card's own or else the owner's primary.
    /// </summary>
    public static class UnitFactory
    {
        public static CreatureUnit MakeCreature(GameState s, Side owner, CreatureCard t,
                                                Element color)
        {
            var c = Fill(new CreatureUnit(), t);
            c.Id = s.NewUid();
            c.Owner = owner;
            c.Color = color != Element.None ? color
                : (t.Element != Element.None ? t.Element : s.P(owner).PrimaryColor);
            return c;
        }

        /// <summary>
        /// The same instance with no match behind it: no id, no owner, nobody's board.
        ///
        /// For the TEXT producers only. Every keyword's rules sentence is written against an
        /// instance (`KeywordEngine.TextOf`) because that is where its numbers live once the
        /// registry's nulls have collapsed - and a card in hand, in the deck builder or under the
        /// inspect cursor has no instance yet. Building a throwaway one here keeps that sentence
        /// in exactly one place instead of growing a second, printed-card copy of it that drifts.
        ///
        /// Id stays 0, which is not a value <see cref="GameState.NewUid"/> ever hands out, so one
        /// of these can never be mistaken for a unit that is really standing somewhere.
        /// </summary>
        public static CreatureUnit Printed(CreatureCard t)
        {
            var c = Fill(new CreatureUnit(), t);
            c.Color = t.Element;
            return c;
        }

        /// <summary>Everything a creature instance takes from its template, including the way the
        /// registry's nulls collapse. One copy, so the printed and the played agree.</summary>
        static CreatureUnit Fill(CreatureUnit c, CreatureCard t)
        {
            c.Card = t.Id;
            c.Name = t.Name;
            c.Attack = t.Attack;
            c.Hp = t.Health;
            c.MaxHp = t.Health;
            c.Cost = t.Cost;
            c.Upkeep = t.Upkeep;
            c.FirstStrike = t.FirstStrike;
            c.Entrench = t.Entrench;
            c.Keyword = t.Keyword;
            c.Detonate = t.Detonate ?? 0;
            c.Reap = t.Reap ?? 0;
            c.WardHp = t.WardHp ?? 2;          // the JS default - deliberately NOT x500
            c.Grow = t.Grow ?? 0;
            c.Hatch = t.Hatch ?? 0;
            c.Into = t.Into != null ? new CardId(t.Into.Name) : CardId.None;
            c.Tribe = t.Tribe;
            c.Subtype = t.Subtype;
            return c;
        }

        /// <summary>
        /// The same mkCre, fed by a card that has already been a creature (a bounce or a
        /// Reliquary recall). Everything comes from the snapshot; nothing is re-read from the
        /// registry, which is the whole point. `token` is NOT carried - the JS's
        /// handcardFromCreature omits it, so a bounced Lumen returns as an ordinary card.
        /// </summary>
        public static CreatureUnit MakeCreature(GameState s, Side owner, CardId card,
                                                CreatureSnapshot snap, Element color)
        {
            var c = new CreatureUnit();
            c.Id = s.NewUid();
            c.Owner = owner;
            c.Color = color != Element.None ? color : s.P(owner).PrimaryColor;
            c.Card = card;
            c.Name = snap.Name;
            c.Attack = snap.Attack;
            c.Hp = snap.Health;
            c.MaxHp = snap.Health;
            c.Cost = snap.Cost;
            c.Upkeep = snap.Upkeep;
            c.FirstStrike = snap.FirstStrike;
            c.Entrench = snap.Entrench;
            c.Keyword = snap.Keyword;
            c.Detonate = snap.Detonate;
            c.Reap = snap.Reap;
            c.WardHp = snap.WardHp;
            c.Grow = snap.Grow;
            c.Hatch = snap.Hatch;
            c.Into = snap.Into;
            c.Tribe = snap.Tribe;
            c.Subtype = snap.Subtype;
            return c;                       // cnt / oc deliberately restart at 0, as in the JS
        }

        /// <summary>
        /// mkToken (06_mana_workers.js:114): a nameless-in-the-registry body - Ward's Lumen and
        /// Reap's Shade. Cost and upkeep are 0, there is no catalog card behind it, and `token`
        /// keeps it out of the Reliquary.
        /// </summary>
        public static CreatureUnit MakeToken(GameState s, Side owner, string name, int attack,
                                             int hp, Element color)
        {
            var c = new CreatureUnit();
            c.Id = s.NewUid();
            c.Owner = owner;
            c.Color = color != Element.None ? color : s.P(owner).PrimaryColor;
            c.Card = CardId.None;
            c.Name = name ?? "";
            c.Attack = attack;
            c.Hp = hp;
            c.MaxHp = hp;
            c.WardHp = 2;                   // mkCre's `t.wardhp||2` default, inert on a token
            c.IsToken = true;
            return c;
        }

        public static StructureUnit MakeStructure(GameState s, Side owner, StructureDef d)
        {
            var b = new StructureUnit();
            b.Id = s.NewUid();
            b.Owner = owner;
            b.Color = d.Element != Element.None ? d.Element : s.P(owner).PrimaryColor;
            b.DefId = d.Bid;
            b.Name = d.Name;
            b.Hp = d.MaxHp;
            b.MaxHp = d.MaxHp;
            b.Cost = d.Cost;
            b.Value = d.Value;
            b.Support = d.Support;
            b.Effect = d.Effect;
            return b;
        }
    }
}
