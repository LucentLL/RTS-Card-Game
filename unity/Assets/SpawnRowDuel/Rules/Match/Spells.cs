using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// ONE target-legality predicate for the whole program (PORT_PLAN M10 s4).
    ///
    /// The JS split this across three places that had to agree and did not: validSpellTarget in
    /// the input layer (13_input.js:53-61) decided what the player could click, resolveSpell
    /// (14_spells_traps.js:2-25) re-decided some of it and checked ownership nowhere at all, and
    /// the AI searched its own targets by hand. A mis-wired caller could burn its own creature.
    /// Here the rule lives once, the command validator runs it BEFORE any mana moves, and the
    /// resolver may assume it passed.
    ///
    /// Ownership is part of legality: every castable spell is an enemy-only spell.
    /// </summary>
    public static class SpellTargeting
    {
        public static bool CanTarget(SpellCard card, BoardObject target, Side caster)
        {
            if (target == null) return false;
            if (target.Owner == caster) return false;          // onCell's `o.owner === 'foe'` gate

            var b = target as StructureUnit;
            if (b != null && b.IsCommandCenter) return false;  // "felled by combat, never a spell"

            var c = target as CreatureUnit;
            switch (card.Effect)
            {
                case SpellEffect.Raze: return b != null;
                case SpellEffect.Burn: return c != null || b != null || target is ChargeUnit;
                case SpellEffect.Chain: return c != null && !c.IsWorker;
                case SpellEffect.Bounce: return c != null && !c.IsWorker;
                default: return false;              // pitfall and thornmail are trap-only effects
            }
        }

        /// <summary>
        /// spellHasTarget (13_input.js:49): is there anything on the board this spell could hit?
        /// The view greys the Cast button with it; the AI uses it to skip dead cards.
        /// </summary>
        public static bool HasAnyTarget(GameState s, SpellCard card, Side caster)
        {
            foreach (var kv in s.Objects())
                if (CanTarget(card, kv.Value, caster)) return true;
            return false;
        }

        /// <summary>Every legal target, canonical board order - what the view lights up.</summary>
        public static List<CellRef> Targets(GameState s, SpellCard card, Side caster)
        {
            var outp = new List<CellRef>();
            foreach (var kv in s.Objects())
                if (CanTarget(card, kv.Value, caster)) outp.Add(kv.Key);
            return outp;
        }
    }

    /// <summary>
    /// resolveSpell (14_spells_traps.js:2-25), dispatched on EFFECT and never on card name, so a
    /// new Bolt variant needs no code. Four castable effects; pitfall and thornmail exist only as
    /// trap payloads and have no branch here, exactly as in the JS.
    ///
    /// Cost is paid by the CALLER, and only after this returns true - an illegal target costs
    /// nothing (castSpell's ordering, spec 06 s7.2).
    /// </summary>
    public static class SpellEngine
    {
        /// <summary>
        /// Apply the effect. Returns false when the effect fizzled on a target it cannot use, in
        /// which case nothing was mutated and the card must NOT be spent. Always sweeps.
        /// </summary>
        public static bool Resolve(GameState s, Side caster, SpellCard card, CellRef at,
                                   ICardCatalog cat, EventSink ev)
        {
            var o = s.At(at);
            if (o == null) return false;

            switch (card.Effect)
            {
                case SpellEffect.Burn: return Burn(s, card, at, o, cat, ev);
                case SpellEffect.Raze: return Raze(s, card, at, o, cat, ev);
                case SpellEffect.Chain: return Chain(s, caster, card, o, cat, ev);
                case SpellEffect.Bounce: return Bounce(s, card, at, o, cat, ev);
                default: return false;
            }
        }

        /// <summary>
        /// Bolt. A face-down is destroyed OUTRIGHT and the mana invested in it is simply lost -
        /// no HP is involved. Anything else takes the damage and waits for the sweep.
        /// </summary>
        static bool Burn(GameState s, SpellCard card, CellRef at, BoardObject o, ICardCatalog cat,
                         EventSink ev)
        {
            int val = card.Value ?? 0;
            var ch = o as ChargeUnit;
            if (ch != null)
            {
                s.Put(at, null);
                DeathSweep.ToGrave(s, ch.Owner, ch);
                ev.Add(new UnitDestroyed(ch.Id, at, true, ch.Owner, UnitKind.Charge));
                DeathSweep.Cleanup(s, cat, ev);
                return true;
            }

            var c = o as CreatureUnit;
            if (c != null) c.Hp -= val;
            else
            {
                var b = o as StructureUnit;
                if (b == null) return false;
                b.Hp -= val;
            }
            ev.Add(new DamageApplied(o.Id, val, 0, DamageTier.Trigger));
            DeathSweep.Cleanup(s, cat, ev);
            return true;
        }

        /// <summary>
        /// Sunder. Destroys the structure outright and IGNORES its hit points entirely - a
        /// 9000-HP Bastion falls to a three-mana card. That is the design, not an oversight.
        /// </summary>
        static bool Raze(GameState s, SpellCard card, CellRef at, BoardObject o, ICardCatalog cat,
                         EventSink ev)
        {
            var b = o as StructureUnit;
            if (b == null) return false;

            s.Put(at, null);
            DeathSweep.ToGrave(s, b.Owner, b);
            ev.Add(new UnitDestroyed(b.Id, at, true, b.Owner, UnitKind.Building));
            DeathSweep.Cleanup(s, cat, ev);
            return true;
        }

        /// <summary>
        /// Arc. The clicked creature only picks the SIDE: the damage lands on the two deadliest
        /// creatures that side fields, which may not include the one that was targeted at all.
        /// </summary>
        static bool Chain(GameState s, Side caster, SpellCard card, BoardObject o, ICardCatalog cat,
                          EventSink ev)
        {
            var c = o as CreatureUnit;
            if (c == null || c.IsWorker) return false;

            var cres = BoardScan.LiveEnemyCreatures(s, caster);
            BoardScan.SortDeadliestFirst(cres);
            if (cres.Count == 0) return false;

            int val = card.Value ?? 0;
            int n = cres.Count < 2 ? cres.Count : 2;
            for (int i = 0; i < n; i++)
            {
                cres[i].Hp -= val;
                ev.Add(new DamageApplied(cres[i].Id, val, 0, DamageTier.Trigger));
            }
            DeathSweep.Cleanup(s, cat, ev);
            return true;
        }

        /// <summary>
        /// Riptide. Entrench does not merely resist it - the spell is SPENT and nothing happens,
        /// which is a real cost to the caster and the reason Entrench reads as immovable.
        /// The bounced card carries its live statline home.
        /// </summary>
        static bool Bounce(GameState s, SpellCard card, CellRef at, BoardObject o, ICardCatalog cat,
                           EventSink ev)
        {
            var c = o as CreatureUnit;
            if (c == null || c.IsWorker) return false;

            if (c.Entrench)
            {
                DeathSweep.Cleanup(s, cat, ev);
                return true;                                   // spent, and it slides off
            }

            var owner = s.RemoveById(c.Id);
            if (owner != null)
            {
                s.P(owner.Value).Hand.Add(HandCard.OfCreature(c));
                ev.Add(new UnitBounced(c.Id, owner.Value, BounceCause.Spell));
            }
            DeathSweep.Cleanup(s, cat, ev);
            return true;
        }

        /// <summary>
        /// spellText (13_input.js:63-70). The only in-game documentation a spell or trap has, so
        /// the strings are part of the port, not decoration - a player deciding whether to spring
        /// a trap is reading this. Keyed on effect, like everything else here.
        /// </summary>
        public static string TextOf(SpellCard card)
        {
            if (card == null) return "";
            int v = card.Value ?? 0;
            switch (card.Effect)
            {
                case SpellEffect.Burn:
                    return card.IsTrap
                        ? "Backlash. When your line is struck, every attacker takes " + v + "."
                        : "Bolt. Deal " + v + " damage to an enemy creature, structure, or face-down card.";
                case SpellEffect.Raze: return "Sunder. Destroy a target enemy structure.";
                case SpellEffect.Pitfall: return "Snare. When your opponent summons a creature, destroy it.";
                case SpellEffect.Chain: return "Arc. Deal " + v + " to the two highest-attack enemy creatures.";
                case SpellEffect.Bounce: return "Riptide. Return target enemy creature to its owner's hand (Entrench resists).";
                case SpellEffect.Thornmail: return "Overgrowth. When your line is struck, the defending creature gains +500/+1000 permanently.";
                default: return "A spell.";
            }
        }

        /// <summary>
        /// spellRec (13_input.js:71): a spent spell leaves a card in the grave. It shares the
        /// Trap kind because the JS graves both as `type:'spell'` - and because that is exactly
        /// what keeps the Reliquary, which only recalls creatures, from ever returning one.
        /// </summary>
        public static GraveRecord SpellRecord(SpellCard card, int turn)
        {
            return new GraveRecord(card.Id, card.Name, Element.None, UnitKind.Trap,
                                   false, false, turn);
        }
    }
}
