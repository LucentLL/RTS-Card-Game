using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// The keyword pieces combat cannot be separated from (spec 03 s9): Undertow's pre-damage
    /// bounce, Overcharge's discharge, Scour's on-hit strike. Direct ports; the M10 keyword
    /// registry absorbs them later without behaviour change.
    /// </summary>
    public static class CombatKeywords
    {
        /// <summary>
        /// applyUndertow (06_mana_workers.js:135-142). Fires ONCE per call regardless of warden
        /// count, before any damage, in all three damage engines. Bounces the highest-MANA-COST
        /// eligible attacker (not highest attack) back to its owner's hand at FULL printed HP.
        /// Immune: workers, tokens, entrenched. Removes the bounced unit from the live group.
        /// </summary>
        public static void ApplyUndertow(GameState s, List<CreatureUnit> groupA,
                                         List<CreatureUnit> groupB, ICardCatalog cat, EventSink ev)
        {
            bool anyWarden = false;
            for (int i = 0; i < groupB.Count; i++)
            {
                var b = groupB[i];
                if (b != null && b.Hp > 0 && !b.IsWorker && b.Keyword == Keyword.Undertow)
                {
                    anyWarden = true;
                    break;
                }
            }
            if (!anyWarden) return;

            CreatureUnit mark = null;                    // highest cost; ties keep group order
            for (int i = 0; i < groupA.Count; i++)
            {
                var a = groupA[i];
                if (a == null || a.Hp <= 0 || a.IsWorker || a.IsToken || a.Entrench) continue;
                if (mark == null || a.Cost > mark.Cost) mark = a;
            }
            if (mark == null) return;

            var owner = s.RemoveById(mark.Id);
            if (owner == null) return;

            s.P(owner.Value).Hand.Add(new HandCard(mark.Card, mark.Color));   // full printed HP
            groupA.Remove(mark);
            ev.Add(new UnitBounced(mark.Id, owner.Value, BounceCause.Undertow));
        }

        /// <summary>dischargeOvercharge: _dis := oc, oc := 0, attackers only, this resolution only.
        /// The raw +1..+3 against the x500 scale is the JS's own missed conversion - preserved
        /// (spec 03 s9.1; the OverchargeScale decision belongs to the flag register).</summary>
        public static void DischargeOvercharge(GameState s, List<int> attackerIds, EventSink ev)
        {
            for (int i = 0; i < attackerIds.Count; i++)
            {
                var a = s.FindById(attackerIds[i], out _, out _) as CreatureUnit;
                if (a == null || a.IsWorker || a.Keyword != Keyword.Overcharge) continue;
                if (a.OverchargeBank <= 0) continue;
                a.DischargeBonus = a.OverchargeBank;
                a.OverchargeBank = 0;
            }
        }

        public static void ClearDischarge(GameState s, List<int> attackerIds)
        {
            for (int i = 0; i < attackerIds.Count; i++)
            {
                var a = s.FindById(attackerIds[i], out _, out _) as CreatureUnit;
                if (a != null) a.DischargeBonus = 0;
            }
        }

        /// <summary>
        /// scourStrike (06_mana_workers.js:165-173): after a connecting strike, shatter the
        /// first face-down or trap in the defender's BACK row; failing that, set the first
        /// non-command-center building there to 0 HP (the sweep collects it).
        /// </summary>
        public static void ScourStrike(GameState s, CreatureUnit attacker, Side defender,
                                       ICardCatalog cat, EventSink ev)
        {
            var back = Board.RowFor(defender, SlotName.Back);

            for (int col = 0; col < Board.Columns; col++)
            {
                var cell = new CellRef(back, col);
                var o = s.At(cell);
                if (o is ChargeUnit || o is TrapUnit)
                {
                    s.Put(cell, null);
                    DeathSweep.ToGrave(s, o.Owner, o);
                    ev.Add(new UnitDestroyed(o.Id, cell, true, o.Owner, o.Kind));
                    return;
                }
            }
            for (int col = 0; col < Board.Columns; col++)
            {
                var b = s.At(new CellRef(back, col)) as StructureUnit;
                if (b != null && !b.IsCommandCenter)
                {
                    b.Hp = 0;                                  // swept by the caller's cleanup
                    ev.Add(new DamageApplied(b.Id, b.MaxHp, attacker.Id, DamageTier.Trigger));
                    return;
                }
            }
        }
    }
}
