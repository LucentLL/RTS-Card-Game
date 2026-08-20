using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// buildingUpkeep / buildingDamage / reviveFromGrave / vaultCap / drainMana
    /// (17_turns_ai.js:1-41), in the JS's exact iteration order.
    /// </summary>
    public static class StructureUpkeep
    {
        /// <summary>
        /// The tick order is determinism-critical (spec 05 s4.3): the owner's FRONT row slots
        /// 0..6, then BACK 0..6, then owned CENTER slots 0..6. Mana adds are commutative but the
        /// once-per-turn revive latch and multi-tower firing order are not.
        ///
        /// The latch arms only on a SUCCESSFUL revive - with an empty grave, a second Reliquary
        /// still gets its own attempt, exactly like the JS short-circuit.
        /// </summary>
        public static void Tick(GameState s, Side owner, ICardCatalog cat, EventSink ev)
        {
            bool revived = false;

            var front = Board.RowFor(owner, SlotName.Front);
            var back = Board.RowFor(owner, SlotName.Back);
            var rows = new[] { front, back, RowKey.Center };

            for (int r = 0; r < rows.Length; r++)
            {
                for (int col = 0; col < Board.Columns; col++)
                {
                    var o = s.At(new CellRef(rows[r], col));
                    if (o == null || o.Owner != owner) continue;
                    var b = o as StructureUnit;
                    if (b == null) continue;

                    if (b.Effect == StructEffect.Mana)
                    {
                        Mana.Add(s, owner, b.Value, ev);
                        ev.Add(new ManaYielded(owner, b.Id, b.Value));
                    }
                    else if (b.Effect == StructEffect.Damage)
                    {
                        FireTower(s, owner, b, ev);
                    }
                    else if (b.Effect == StructEffect.Revive)
                    {
                        if (!revived) revived = ReviveFromGrave(s, owner, ev);
                    }
                }
            }
        }

        /// <summary>
        /// buildingDamage: the FIRST non-worker enemy creature scanning the foe's front row,
        /// the center, then the foe's back row, slots ascending. Only creatures - never
        /// structures, workers, or the life pool. Deaths wait for the sweep.
        /// </summary>
        private static void FireTower(GameState s, Side owner, StructureUnit tower, EventSink ev)
        {
            if (tower.Value <= 0) return;
            var foe = TurnMachine.Other(owner);

            var scan = new[]
            {
                Board.RowFor(foe, SlotName.Front),
                RowKey.Center,
                Board.RowFor(foe, SlotName.Back),
            };

            for (int r = 0; r < scan.Length; r++)
            {
                for (int col = 0; col < Board.Columns; col++)
                {
                    var o = s.At(new CellRef(scan[r], col));
                    var c = o as CreatureUnit;
                    if (c == null || c.Owner != foe || c.IsWorker) continue;

                    c.Hp -= tower.Value;
                    ev.Add(new TowerFired(tower.Id, c.Id, tower.Value));
                    ev.Add(new DamageApplied(c.Id, tower.Value, tower.Id, DamageTier.Trigger));
                    return;
                }
            }
        }

        /// <summary>
        /// reviveFromGrave: the most recently fallen non-token real creature returns to hand.
        /// Grave records store max HP, so the card comes back undamaged; workers ('villager'
        /// records) and structures are never revived.
        /// </summary>
        public static bool ReviveFromGrave(GameState s, Side owner, EventSink ev)
        {
            var grave = s.P(owner).Grave;
            for (int k = grave.Count - 1; k >= 0; k--)
            {
                var r = grave[k];
                if (r.Kind != UnitKind.Creature || r.IsToken || r.IsWorker) continue;

                grave.RemoveAt(k);
                var color = r.Color == Element.None ? s.P(owner).PrimaryColor : r.Color;
                s.P(owner).Hand.Add(new HandCard(r.Id, color));
                ev.Add(new CreatureRevived(owner, r.Id));
                return true;
            }
            return false;
        }

        /// <summary>Additive across every vault the owner has standing, wherever it stands.</summary>
        public static int VaultCapacity(GameState s, Side owner)
        {
            int cap = 0;
            foreach (var kv in s.ObjectsOf(owner))
            {
                var b = kv.Value as StructureUnit;
                if (b != null && b.Effect == StructEffect.Vault) cap += b.Value;
            }
            return cap;
        }

        /// <summary>
        /// endTurnDrain: unspent mana evaporates at the end of the OWNER's turn except what the
        /// vaults hold. Never called at turn start.
        /// </summary>
        public static void DrainMana(GameState s, Side owner, EventSink ev)
        {
            var p = s.P(owner);
            int cap = VaultCapacity(s, owner);
            int lost = p.Mana > cap ? p.Mana - cap : 0;
            p.Mana = p.Mana < cap ? p.Mana : cap;
            if (lost > 0 || p.Mana > 0)
                ev.Add(new ManaDrained(owner, p.Mana, lost));
        }
    }
}
