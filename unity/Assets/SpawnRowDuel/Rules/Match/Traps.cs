using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// Set traps: arming, the response window that offers them, and the four ways one leaves the
    /// board (spec 03 s10, spec 06 s7.4).
    ///
    /// The DECISION is always the defender's, through a parked ResponseWindowRequest. The JS had
    /// two different code paths - the AI's trap sprang itself, the human's opened a modal and
    /// later a RESP bar - and that asymmetry is not portable: the core has no idea which side is
    /// a person. A policy that answers "spring the first armed trap" reproduces the old
    /// auto-spring outcome exactly, and it is the AI's default.
    ///
    /// A trap struck directly, or provoked as a face-down, is NOT a choice - it goes off.
    /// </summary>
    public static class Traps
    {
        /// <summary>
        /// findArmedTrap (14_spells_traps.js:34-40): the owner's FRONT row 0..6, then BACK 0..6,
        /// then their centre slots - a fixed deterministic order. A trap is armed only from the
        /// turn AFTER it was set, so it can never spring on the turn it was laid.
        /// </summary>
        public static TrapUnit FindArmedTrap(GameState s, Side owner, TrapTrigger trigger,
                                             out CellRef at)
        {
            var rows = ScanRows(owner);
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
        /// RESP.findArmedTraps (30_resp.js:10): every armed trap for the trigger, same scan
        /// order. This is what populates a response window - the plural sibling, not a different
        /// arming rule.
        /// </summary>
        public static List<UnitRef> FindArmedTraps(GameState s, Side owner, TrapTrigger trigger)
        {
            var outp = new List<UnitRef>();
            var rows = ScanRows(owner);
            for (int r = 0; r < rows.Length; r++)
                for (int col = 0; col < Board.Columns; col++)
                {
                    var cell = new CellRef(rows[r], col);
                    var t = s.At(cell) as TrapUnit;
                    if (t == null || t.Owner != owner) continue;
                    if (t.Trigger != trigger || !t.IsArmed(s.TurnNumber)) continue;
                    outp.Add(UnitRef.Cell(cell, t.Id));
                }
            return outp;
        }

        static RowKey[] ScanRows(Side owner)
        {
            return new[]
            {
                Board.RowFor(owner, SlotName.Front),
                Board.RowFor(owner, SlotName.Back),
                RowKey.Center,
            };
        }

        /// <summary>
        /// The window still names a trap that is really there, really armed, and really the
        /// defender's. Everything that consumes a chosen ref re-checks through here, because the
        /// board can move between the offer and the answer (RESP's `cellArr(...)[t.i] !== t.o`
        /// guard, 30_resp.js:94).
        /// </summary>
        public static TrapUnit ResolveTrapRef(GameState s, Side owner, UnitRef trapRef,
                                              TrapTrigger trigger, out CellRef at)
        {
            at = default(CellRef);
            if (trapRef.Kind != UnitRefKind.Cell) return null;
            var cell = trapRef.AsCell;
            var t = s.At(cell) as TrapUnit;
            if (t == null || t.Id != trapRef.UnitId) return null;
            if (t.Owner != owner || t.Trigger != trigger) return null;
            if (!t.IsArmed(s.TurnNumber)) return null;
            at = cell;
            return t;
        }

        // ── summon ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A creature was SUMMONED (never flipped - flip immunity is the whole point of setting).
        /// If the defender holds an armed summon trap, park their window; otherwise nothing
        /// happens and play continues in the same command.
        /// </summary>
        public static void OfferSummonWindow(GameState s, CreatureUnit summoned, CellRef at,
                                             Side summoner, ICardCatalog cat, EventSink ev)
        {
            var defender = TurnMachine.Other(summoner);
            var armed = FindArmedTraps(s, defender, TrapTrigger.Summon);
            if (armed.Count == 0) return;

            s.Pending = new ResponseWindowRequest(defender, TrapTrigger.Summon, armed.ToArray(),
                                                  UnitRef.Cell(at, summoned.Id));
        }

        /// <summary>
        /// foeTrapOnSummon (14_spells_traps.js:42-50) / the RESP replacement of
        /// playerTrapOnSummon: the newcomer is dragged down as it forms.
        ///
        /// Two JS quirks reproduced deliberately: the trap's `effect` is IGNORED - any
        /// trigger:'summon' trap simply destroys the summoned creature, whatever it says on the
        /// card (all three are Snare today, so it has never shown) - and the victim is graved
        /// DIRECTLY rather than swept, so its own death keyword never fires.
        /// </summary>
        public static void SpringSummonTrap(GameState s, Side defender, UnitRef trapRef,
                                            UnitRef subject, ICardCatalog cat, EventSink ev)
        {
            CellRef trapAt;
            var t = ResolveTrapRef(s, defender, trapRef, TrapTrigger.Summon, out trapAt);
            if (t == null) return;

            // the summoned creature must still be standing where it formed
            if (subject.Kind != UnitRefKind.Cell) return;
            var victimAt = subject.AsCell;
            var victim = s.At(victimAt) as CreatureUnit;
            if (victim == null || victim.Id != subject.UnitId) return;

            ev.Add(new TrapSprung(defender, t.Card, trapAt));

            s.Put(victimAt, null);
            DeathSweep.ToGrave(s, victim.Owner, victim);          // no death trigger: not a sweep
            ev.Add(new UnitDestroyed(victim.Id, victimAt, true, victim.Owner, UnitKind.Creature));

            s.Put(trapAt, null);
            DeathSweep.ToGrave(s, defender, t);

            DeathSweep.Cleanup(s, cat, ev);
        }

        // ── attack ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// springAttackTrap (15_combat.js:110-118), taking an already-chosen trap the way
        /// RESP.springAttackTrapRef does. Thornmail permanently hardens the struck defender
        /// (+500 attack, +1000 hp and max); Burn damages every attacker. Deliberately does NOT
        /// sweep - the fight that follows runs the next cleanup.
        /// </summary>
        public static void SpringAttackTrap(GameState s, Side defOwner, UnitRef trapRef,
                                            List<CreatureUnit> attackers, BoardObject defender,
                                            EventSink ev)
        {
            CellRef at;
            var t = ResolveTrapRef(s, defOwner, trapRef, TrapTrigger.Attack, out at);
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

        // ── struck directly ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// springTrap (15_combat.js:100-109): the trap CARD was attacked. No window - a trap you
        /// walked into goes off. It is removed regardless of attacker power; the attackers deal
        /// no damage and are simply exposed. This path IGNORES the trigger entirely: hitting any
        /// set trap springs it. Pitfall destroys the highest RAW-attack attacker outright.
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
        /// provokeFaceDown (15_combat.js:86-99): an attacked face-down charge. Under-funded - the
        /// strike catches a half-formed card: it dies, its investment is lost, and the attacker
        /// neither deals nor takes damage. Funded - it FLIPS (battle-ready when set on an earlier
        /// turn) and a creature fights back at full power through the legacy engine; a structure
        /// just takes the blow one-way.
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
