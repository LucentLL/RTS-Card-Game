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
            var c = new CreatureUnit();
            c.Id = s.NewUid();
            c.Owner = owner;
            c.Color = color != Element.None ? color
                : (t.Element != Element.None ? t.Element : s.P(owner).PrimaryColor);
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

        public static StructureUnit MakeStructure(GameState s, Side owner, StructureDef d)
        {
            var b = new StructureUnit();
            b.Id = s.NewUid();
            b.Owner = owner;
            b.Color = d.Element != Element.None ? d.Element : s.P(owner).PrimaryColor;
            b.DefId = d.Bid;
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
