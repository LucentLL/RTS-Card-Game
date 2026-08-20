namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// The two upkeep-phase keyword ticks (06_mana_workers.js:144-157), run inside BeginTurn
    /// steps 6-7. Direct ports for now; when M10 lands the keyword registry these become the
    /// Chrysalis / Overcharge handlers' OnUpkeep hooks with identical behaviour.
    /// </summary>
    public static class UpkeepKeywords
    {
        /// <summary>
        /// chrysalisUpkeep: every cocoon swells by its grow rate and RE-SICKS (a cocoon can
        /// never attack); at the hatch threshold it mutates IN PLACE - same id, owner, bank,
        /// cell - into its grown form, keyword cleared to stop the loop, and comes out sick.
        /// </summary>
        public static void ChrysalisTick(GameState s, Side owner, ICardCatalog cat, EventSink ev)
        {
            foreach (var kv in s.ObjectsOf(owner))
            {
                var c = kv.Value as CreatureUnit;
                if (c == null || c.IsWorker || c.Keyword != Keyword.Chrysalis) continue;

                int grow = c.Grow > 0 ? c.Grow : 1;
                int hatchAt = c.Hatch > 0 ? c.Hatch : 3;
                c.ChrysalisCount += grow;

                if (c.ChrysalisCount >= hatchAt)
                {
                    // The hatch form lives on the CATALOG card (name/attack/health only, spec 06
                    // s6.6) - hatch forms are not registry cards, so the instance mutates IN
                    // PLACE: same id, owner, bank, cell. Keyword clears to stop the loop.
                    CreatureCard baseCard;
                    if (cat.TryCreature(c.Card, out baseCard) && baseCard.Into != null)
                    {
                        c.Name = baseCard.Into.Name;
                        c.Attack = baseCard.Into.Attack;
                        c.MaxHp = baseCard.Into.Health;
                        c.Hp = baseCard.Into.Health;
                    }
                    c.Keyword = Keyword.None;
                    c.Sick = true;
                    ev.Add(new CreatureHatched(c.Id, c.Name, c.Attack, c.Hp));
                }
                else
                {
                    c.Sick = true;
                    ev.Add(new ChrysalisGrew(c.Id, c.ChrysalisCount, hatchAt));
                }
            }
        }

        /// <summary>overchargeUpkeep: oc = min(3, oc + 1). Discharge happens at attack time (M8).</summary>
        public static void OverchargeTick(GameState s, Side owner, EventSink ev)
        {
            foreach (var kv in s.ObjectsOf(owner))
            {
                var c = kv.Value as CreatureUnit;
                if (c == null || c.IsWorker || c.Keyword != Keyword.Overcharge) continue;

                int before = c.OverchargeBank;
                c.OverchargeBank = before >= 3 ? 3 : before + 1;
                if (c.OverchargeBank != before)
                    ev.Add(new Overcharged(c.Id, c.OverchargeBank));
            }
        }
    }
}
