using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// Set traps in combat (spec 03 s10): the defender's attack-trigger trap auto-springs (it
    /// can only help the defender - the anti-tell CHOICE window is view machinery layered at
    /// M10), an attacked trap card springs on its attackers, and an attacked face-down charge
    /// is provoked - flipping to meet the strike, or dying half-formed.
    /// </summary>
    public static class Traps
    {
        /// <summary>
        /// findArmedTrap: the owner's front row 0..6, back row 0..6, then owned center slots -
        /// fixed deterministic order. A trap is armed only from the turn AFTER it was set.
        /// </summary>
        public static TrapUnit FindArmedTrap(GameState s, Side owner, TrapTrigger trigger,
                                             out CellRef at)
        {
            var rows = new[]
            {
                Board.RowFor(owner, SlotName.Front),
                Board.RowFor(owner, SlotName.Back),
                RowKey.Center,
            };
            for (int r = 0; r < rows.Length; r++)
                for (int col = 0; col < Board.Columns; col++)
                {
                    var cell = new CellRef(rows[r], col);
                    var t = s.At(cell) as TrapUnit;
                    if (t == null || t.Owner != owner) continue;
                    if (t.Trigger != trigger || !t.IsArmed(s.TurnNumber)) continue;
                    at = cell;
                    return t;
                }
            at = default(CellRef);
            return null;
        }

        /// <summary>
        /// springAttackTrap (15_combat.js:110-118): fires the defender's armed attack trap.
        /// thornmail permanently buffs the struck defender (+500 attack, +1000 hp and max);
        /// burn damages every attacker. Deliberately does NOT sweep - the fight that follows
        /// runs the next cleanup. Each call re-finds, so several traps can spring per resolution.
        /// </summary>
        public static void SpringAttackTrap(GameState s, Side defOwner,
                                            List<CreatureUnit> attackers, BoardObject defender,
                                            EventSink ev)
        {
            CellRef at;
            var t = FindArmedTrap(s, defOwner, TrapTrigger.Attack, out at);
            if (t == null) return;

            ev.Add(new TrapSprung(defOwner, t.Card, at));

            if (t.Effect == SpellEffect.Thornmail)
            {
                var cr = defender as CreatureUnit;
                if (cr != null && !cr.IsWorker)
                {
                    cr.Attack += 500;
                    cr.MaxHp += 1000;
                    cr.Hp += 1000;                                  // PERMANENT
                }
            }
            else if (t.Effect == SpellEffect.Burn)
            {
                for (int i = 0; i < attackers.Count; i++)
                {
                    attackers[i].Hp -= t.Value;
                    ev.Add(new DamageApplied(attackers[i].Id, t.Value, t.Id, DamageTier.Trigger));
                }
            }

            s.Put(at, null);
            DeathSweep.ToGrave(s, defOwner, t);
        }

        /// <summary>
        /// springTrap (15_combat.js:100-109): the trap CARD was attacked. The trap is removed
        /// regardless of attacker power; attackers deal no damage and are simply exposed.
        /// pitfall destroys the highest RAW-attack attacker outright.
        /// </summary>
        public static void SpringTrap(GameState s, Side defOwner, CellRef at,
                                      List<CreatureUnit> attackers, ICardCatalog cat, EventSink ev)
        {
            var t = s.At(at) as TrapUnit;
            if (t == null) return;

            ev.Add(new TrapSprung(defOwner, t.Card, at));

            if (t.Effect == SpellEffect.Pitfall)
            {
                CreatureUnit victim = null;                    // highest raw a; ties keep order
                for (int i = 0; i < attackers.Count; i++)
                    if (victim == null || attackers[i].Attack > victim.Attack)
                        victim = attackers[i];
                if (victim != null)
                {
                    victim.Hp = 0;
                    ev.Add(new DamageApplied(victim.Id, victim.MaxHp, t.Id, DamageTier.Trigger));
                }
            }
            else if (t.Effect == SpellEffect.Burn)
            {
                for (int i = 0; i < attackers.Count; i++)
                {
                    attackers[i].Hp -= t.Value;
                    ev.Add(new DamageApplied(attackers[i].Id, t.Value, t.Id, DamageTier.Trigger));
                }
            }
            // thornmail has no creature defender here - it simply fizzles

            s.Put(at, null);
            DeathSweep.ToGrave(s, defOwner, t);
            DeathSweep.Cleanup(s, cat, ev);
        }

        /// <summary>
        /// provokeFaceDown (15_combat.js:86-99): an attacked face-down charge. Under-funded -
        /// the strike catches a half-formed card: it dies, its investment is lost, and the
        /// attacker neither deals nor takes damage. Funded - it FLIPS (battle-ready when set on
        /// an earlier turn) and a creature fights back at full power through the legacy engine;
        /// a structure just takes the blow one-way.
        /// </summary>
        public static void ProvokeFaceDown(GameState s, Side defOwner, CellRef at,
                                           List<CreatureUnit> attackers, ICardCatalog cat,
                                           EventSink ev)
        {
            var ch = s.At(at) as ChargeUnit;
            if (ch == null) return;

            if (ch.Invested < ch.Card.Cost)
            {
                s.Put(at, null);
                DeathSweep.ToGrave(s, defOwner, ch);
                ev.Add(new UnitDestroyed(ch.Id, at, true, defOwner, UnitKind.Charge));
                DeathSweep.Cleanup(s, cat, ev);
                return;
            }

            ChargeOps.Flip(s, defOwner, at, cat, ev);
            var now = s.At(at);

            var cr = now as CreatureUnit;
            if (cr != null)
            {
                LegacyCombat.Resolve(s, attackers, new List<CreatureUnit> { cr }, cat, ev);
                return;
            }
            var b = now as StructureUnit;
            if (b != null)
            {
                var map = LegacyCombat.FocusFire(attackers, new List<BoardObject> { b });
                LegacyCombat.ApplyDamage(map, ev);
                DeathSweep.Cleanup(s, cat, ev);
            }
        }
    }
}
