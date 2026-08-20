using System;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// The single mana funnel. The JS duplicated the 99 cap across five call sites and payAny
    /// could be handed a negative amount and CREDIT it (spec 01 s15.2, spec 07 s18.5). Here:
    /// exactly one clamped credit path, and spending either succeeds in full or fails loudly -
    /// never a silent partial debit.
    /// </summary>
    public static class Mana
    {
        public const int Cap = 99;

        public static void Add(GameState s, Side owner, int amount, EventSink events)
        {
            if (amount <= 0) return;    // negative credits are impossible by construction
            var p = s.P(owner);
            int before = p.Mana;
            p.Mana = Math.Min(Cap, p.Mana + amount);
            if (p.Mana != before && events != null)
                events.Add(new ManaChanged(owner, before, p.Mana));
        }

        /// <summary>All-or-nothing. A false return must abort the command that asked.</summary>
        public static bool TrySpend(GameState s, Side owner, int amount)
        {
            var p = s.P(owner);
            if (amount < 0 || p.Mana < amount) return false;
            p.Mana -= amount;
            return true;
        }
    }
}
