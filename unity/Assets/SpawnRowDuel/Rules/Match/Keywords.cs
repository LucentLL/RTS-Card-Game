using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// One creature keyword, as a handler. Six hook points, because the JS has exactly six real
    /// trigger sites (spec 06 s6.0) - anything else a keyword "does" is a passive flag test.
    ///
    /// Three hooks are per-unit (ENTER, DEATH, UPKEEP) and three are combat-scoped (PRE-COMBAT,
    /// ATTACK-PREP, ON-HIT), because that is how the JS wrote them: applyUndertow takes the whole
    /// defending group and fires ONCE for it however many wardens stand there, while
    /// dischargeOvercharge walks the attacker list. Forcing the group hooks to be per-unit would
    /// quietly change how often they fire.
    ///
    /// Handlers are stateless singletons and must stay that way - all mutation goes through the
    /// GameState they are handed.
    /// </summary>
    public interface IKeywordHandler
    {
        Keyword Keyword { get; }

        /// <summary>The card's own rules text, with this instance's numbers filled in (kwText).</summary>
        string Text(CreatureUnit self, ICardCatalog cat);

        /// <summary>The short label the hand card's ability box shows (kwName).</summary>
        string Label(CreatureUnit self);

        /// <summary>ENTER - after a creature is summoned or flipped onto the board.</summary>
        void OnEnter(GameState s, CreatureUnit self, Side owner, ICardCatalog cat, EventSink ev);

        /// <summary>DEATH - inside the sweep, with the dying creature's cell already freed.</summary>
        void OnDeath(GameState s, CreatureUnit self, Side owner, ICardCatalog cat, EventSink ev);

        /// <summary>UPKEEP - the owner's turn start, one full pass per keyword.</summary>
        void OnUpkeep(GameState s, CreatureUnit self, Side owner, ICardCatalog cat, EventSink ev);

        /// <summary>Does this keyword do anything at upkeep? Keeps the sweep off the board
        /// entirely for the six keywords that do not.</summary>
        bool HasUpkeep { get; }

        /// <summary>
        /// PRE-COMBAT - before any damage in a fight where at least one DEFENDER carries this
        /// keyword. Called once per fight, never once per warden.
        /// </summary>
        void OnPreCombat(GameState s, List<CreatureUnit> attackers, List<CreatureUnit> defenders,
                         ICardCatalog cat, EventSink ev);

        bool HasPreCombat { get; }

        /// <summary>ATTACK-PREP - each attacker, once, as resolution begins.</summary>
        void OnAttackPrep(GameState s, CreatureUnit self, EventSink ev);

        /// <summary>Undo whatever ATTACK-PREP staged. Runs for every attacker of the resolution.</summary>
        void OnAttackEnd(GameState s, CreatureUnit self);

        /// <summary>ON-HIT - a surviving unblocked attacker connected with the defending side.</summary>
        void OnHit(GameState s, CreatureUnit self, Side defender, ICardCatalog cat, EventSink ev);

        bool HasOnHit { get; }
    }

    /// <summary>No-op base: a handler overrides only the hooks its keyword actually uses.</summary>
    public abstract class KeywordHandler : IKeywordHandler
    {
        public abstract Keyword Keyword { get; }
        public abstract string Text(CreatureUnit self, ICardCatalog cat);
        public abstract string Label(CreatureUnit self);

        public virtual void OnEnter(GameState s, CreatureUnit self, Side owner, ICardCatalog cat, EventSink ev) { }
        public virtual void OnDeath(GameState s, CreatureUnit self, Side owner, ICardCatalog cat, EventSink ev) { }
        public virtual void OnUpkeep(GameState s, CreatureUnit self, Side owner, ICardCatalog cat, EventSink ev) { }
        public virtual bool HasUpkeep { get { return false; } }

        public virtual void OnPreCombat(GameState s, List<CreatureUnit> attackers,
                                        List<CreatureUnit> defenders, ICardCatalog cat, EventSink ev) { }
        public virtual bool HasPreCombat { get { return false; } }

        public virtual void OnAttackPrep(GameState s, CreatureUnit self, EventSink ev) { }
        public virtual void OnAttackEnd(GameState s, CreatureUnit self) { }

        public virtual void OnHit(GameState s, CreatureUnit self, Side defender, ICardCatalog cat, EventSink ev) { }
        public virtual bool HasOnHit { get { return false; } }
    }

    // -- the eight -----------------------------------------------------------------------------

    /// <summary>
    /// Fire. On DEATH, blast the deadliest enemy creature - highest attack, ties to the most
    /// wounded - or, only if the enemy fields no creature at all, their frailest structure.
    /// Never touches the life pool (onCreatureDeath, 06_mana_workers.js:125-129).
    /// </summary>
    public sealed class DetonateHandler : KeywordHandler
    {
        public override Keyword Keyword { get { return Keyword.Detonate; } }

        public override string Text(CreatureUnit o, ICardCatalog cat)
        {
            return "Detonate " + o.Detonate + ". When destroyed, deals " + o.Detonate
                 + " to the deadliest enemy creature (or an enemy structure). Never hits a command center.";
        }

        public override string Label(CreatureUnit o) { return "Detonate " + o.Detonate; }

        public override void OnDeath(GameState s, CreatureUnit self, Side owner, ICardCatalog cat,
                                     EventSink ev)
        {
            int n = self.Detonate;
            if (n <= 0) return;

            var cres = BoardScan.LiveEnemyCreatures(s, owner);
            BoardScan.SortDeadliestFirst(cres);

            BoardObject tgt = cres.Count > 0 ? (BoardObject)cres[0] : null;
            if (tgt == null)
            {
                var blds = BoardScan.LiveEnemyStructures(s, owner);      // frailest first
                Sorting.StableSort(blds, delegate (StructureUnit a, StructureUnit b)
                {
                    return a.Hp.CompareTo(b.Hp);
                });
                if (blds.Count > 0) tgt = blds[0];
            }
            if (tgt == null) return;

            var c = tgt as CreatureUnit;
            if (c != null) c.Hp -= n; else ((StructureUnit)tgt).Hp -= n;
            ev.Add(new DamageApplied(tgt.Id, n, self.Id, DamageTier.Trigger));
        }
    }

    /// <summary>
    /// Water. Before any damage, the costliest eligible attacker is hurled back to its owner's
    /// hand - carrying its live statline - and takes no further part in the fight. Fires once per
    /// fight however many wardens defend, and fires whether the warden BLOCKED or was simply the
    /// target (applyUndertow, 06_mana_workers.js:135-142).
    /// </summary>
    public sealed class UndertowHandler : KeywordHandler
    {
        public override Keyword Keyword { get { return Keyword.Undertow; } }

        public override string Text(CreatureUnit o, ICardCatalog cat)
        {
            return "Undertow. When this blocks or is attacked, the strongest attacking creature "
                 + "is hurled back to its owner's hand (re-summoning-sick).";
        }

        public override string Label(CreatureUnit o) { return "Undertow"; }

        public override bool HasPreCombat { get { return true; } }

        public override void OnPreCombat(GameState s, List<CreatureUnit> groupA,
                                         List<CreatureUnit> groupB, ICardCatalog cat, EventSink ev)
        {
            CreatureUnit mark = null;            // highest COST, not highest attack; ties keep order
            for (int i = 0; i < groupA.Count; i++)
            {
                var a = groupA[i];
                if (a == null || a.Hp <= 0 || a.IsWorker || a.IsToken || a.Entrench) continue;
                if (mark == null || a.Cost > mark.Cost) mark = a;
            }
            if (mark == null) return;

            var owner = s.RemoveById(mark.Id);
            if (owner == null) return;

            s.P(owner.Value).Hand.Add(HandCard.OfCreature(mark));    // live statline, at max HP
            groupA.Remove(mark);
            ev.Add(new UnitBounced(mark.Id, owner.Value, BounceCause.Undertow));

            // The resolver walks attackers by id and so loses sight of one that is now a hand
            // card; the JS held the object and kept striking with it. Record the departure so
            // the misc step can still hand out this flier's Scour credit - serialized, because
            // a resolution parked on a choice has to survive a snapshot.
            if (s.Combat.Resolving && KeywordEngine.HasOnHit(mark))
                s.Combat.BouncedScourIds.Add(mark.Id);
        }
    }

    /// <summary>
    /// Earth. Immovable - a passive flag, not an active hook. It costs Undertow and Riptide their
    /// grip; it does NOT stop the owner walking the unit anywhere it likes (16_movement.js never
    /// consults it).
    /// </summary>
    public sealed class EntrenchHandler : KeywordHandler
    {
        public override Keyword Keyword { get { return Keyword.Entrench; } }

        public override string Text(CreatureUnit o, ICardCatalog cat)
        {
            return "Entrench. Immovable - cannot be bounced or pushed; effects like Undertow slide off.";
        }

        public override string Label(CreatureUnit o) { return "Entrench"; }
    }

    /// <summary>
    /// Light. On ENTER, conjure a 0/wardhp Lumen token in the owner's first free cell - which is
    /// usually, but by no means always, beside the warder. With no room the ward is simply lost
    /// (onCreatureEnter, 06_mana_workers.js:118-123).
    /// </summary>
    public sealed class WardHandler : KeywordHandler
    {
        public const string TokenName = "Lumen";

        public override Keyword Keyword { get { return Keyword.Ward; } }

        public override string Text(CreatureUnit o, ICardCatalog cat)
        {
            return "Ward. On entry, conjures a 0/" + o.WardHp + " Lumen token blocker beside it.";
        }

        public override string Label(CreatureUnit o) { return "Ward"; }

        public override void OnEnter(GameState s, CreatureUnit self, Side owner, ICardCatalog cat,
                                     EventSink ev)
        {
            CellRef spot;
            if (!BoardScan.FirstEmptyCell(s, owner, out spot)) return;    // no room - ward lost

            var tok = UnitFactory.MakeToken(s, owner, TokenName, 0, self.WardHp, self.Color);
            tok.Sick = true;
            s.Put(spot, tok);
            ev.Add(new TokenSpawned(tok.Id, spot, owner, TokenName, 0, tok.Hp));
        }
    }

    /// <summary>
    /// Dark. On DEATH, raise a reap/reap Shade in the owner's first free cell. The sweep clears
    /// the corpse's cell BEFORE calling this, which is why the Shade usually rises where its
    /// parent fell (onCreatureDeath, 06_mana_workers.js:130-133).
    /// </summary>
    public sealed class ReapHandler : KeywordHandler
    {
        public const string TokenName = "Shade";

        public override Keyword Keyword { get { return Keyword.Reap; } }

        public override string Text(CreatureUnit o, ICardCatalog cat)
        {
            return "Reap " + o.Reap + ". When destroyed, raises a " + o.Reap + "/" + o.Reap
                 + " Shade token in its place.";
        }

        public override string Label(CreatureUnit o) { return "Reap " + o.Reap; }

        public override void OnDeath(GameState s, CreatureUnit self, Side owner, ICardCatalog cat,
                                     EventSink ev)
        {
            CellRef spot;
            if (!BoardScan.FirstEmptyCell(s, owner, out spot)) return;

            int a = self.Reap > 0 ? self.Reap : 1;             // the JS `cr.reap||1` default
            var tok = UnitFactory.MakeToken(s, owner, TokenName, a, a, self.Color);
            tok.Sick = true;
            s.Put(spot, tok);
            ev.Add(new TokenSpawned(tok.Id, spot, owner, TokenName, a, a));
        }
    }

    /// <summary>
    /// Forest. A cocoon that swells every upkeep and RE-SICKS itself, so it can never attack -
    /// though summoning-sick units may still block and still reposition. At the threshold it
    /// mutates IN PLACE (same id, owner, bank, cell), heals to the new maximum and clears its
    /// keyword, which is what stops the loop; the swell counter is deliberately never reset
    /// (chrysalisUpkeep, 06_mana_workers.js:144-152).
    /// </summary>
    public sealed class ChrysalisHandler : KeywordHandler
    {
        public override Keyword Keyword { get { return Keyword.Chrysalis; } }

        public override string Text(CreatureUnit o, ICardCatalog cat)
        {
            // kwText names the form it becomes and its statline - which is the only place in the
            // game a player can learn what a cocoon is worth. The hatch form is not a registry
            // card, so it has to be read off the base card the same way OnUpkeep reads it.
            string into = "";
            CreatureCard baseCard;
            if (cat != null && cat.TryCreature(o.Card, out baseCard) && baseCard.Into != null)
                into = " " + baseCard.Into.Name + " (attack " + baseCard.Into.Attack
                     + "/health " + baseCard.Into.Health + ")";

            return "Chrysalis " + o.ChrysalisCount + "/" + (o.Hatch > 0 ? o.Hatch : 3)
                 + ". Cannot attack; swells +" + (o.Grow > 0 ? o.Grow : 1)
                 + " each of your turns, then hatches into" + into + ".";
        }

        public override string Label(CreatureUnit o) { return "Chrysalis"; }

        public override bool HasUpkeep { get { return true; } }

        public override void OnUpkeep(GameState s, CreatureUnit c, Side owner, ICardCatalog cat,
                                      EventSink ev)
        {
            int grow = c.Grow > 0 ? c.Grow : 1;
            int hatchAt = c.Hatch > 0 ? c.Hatch : 3;
            c.ChrysalisCount += grow;

            if (c.ChrysalisCount >= hatchAt)
            {
                // The hatched form lives on the CATALOG card (name/attack/health only, spec 06
                // s6.6) - hatch forms are not registry cards, so the instance mutates in place.
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

    /// <summary>
    /// Wind. A flier: interceptors never get the chance to block it (that gate lives in
    /// CombatEligibility, per-attacker), and a connecting strike shatters one card out of the
    /// defender's back row - face-downs and traps first, then the frailest thing standing
    /// (scourStrike, 06_mana_workers.js:165-173).
    /// </summary>
    public sealed class ScourHandler : KeywordHandler
    {
        public override Keyword Keyword { get { return Keyword.Scour; } }

        public override string Text(CreatureUnit o, ICardCatalog cat)
        {
            return "Scour. Flier - ignores interceptors and shatters an enemy back-row card on attack.";
        }

        public override string Label(CreatureUnit o) { return "Scour"; }

        public override bool HasOnHit { get { return true; } }

        public override void OnHit(GameState s, CreatureUnit attacker, Side defender,
                                   ICardCatalog cat, EventSink ev)
        {
            Shatter(s, attacker.Id, defender, ev);
        }

        /// <summary>
        /// The strike itself, keyed on the attacker's ID rather than its body - because
        /// scourStrike reads nothing off the attacker but its name, and the JS lets a flier
        /// Undertow has already hurled back to hand deliver one anyway.
        /// </summary>
        public static void Shatter(GameState s, int attackerId, Side defender, EventSink ev)
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
                    ev.Add(new DamageApplied(b.Id, b.MaxHp, attackerId, DamageTier.Trigger));
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Electric. Banks a charge each upkeep to a hard cap of 3, and dumps the whole bank as bonus
    /// attack the moment it declares. The discharge rides on effA - so it counts when this
    /// creature STRIKES but never when it RETALIATES, which reads raw attack
    /// (06_mana_workers.js:154-163).
    ///
    /// The +1..+3 against a x500 stat scale is the JS's own missed rescale, preserved on purpose
    /// (spec 06 s11.2); changing it is a balance decision for the flag register, not a port fix.
    /// </summary>
    public sealed class OverchargeHandler : KeywordHandler
    {
        public const int Cap = 3;

        public override Keyword Keyword { get { return Keyword.Overcharge; } }

        public override string Text(CreatureUnit o, ICardCatalog cat)
        {
            return "Overcharge. Banks a charge each of your turns (up to " + Cap
                 + "); when it attacks it discharges them as bonus attack.";
        }

        public override string Label(CreatureUnit o) { return "Overcharge"; }

        public override bool HasUpkeep { get { return true; } }

        public override void OnUpkeep(GameState s, CreatureUnit c, Side owner, ICardCatalog cat,
                                      EventSink ev)
        {
            int before = c.OverchargeBank;
            c.OverchargeBank = before >= Cap ? Cap : before + 1;
            if (c.OverchargeBank != before) ev.Add(new Overcharged(c.Id, c.OverchargeBank));
        }

        public override void OnAttackPrep(GameState s, CreatureUnit a, EventSink ev)
        {
            if (a.OverchargeBank <= 0) return;
            a.DischargeBonus = a.OverchargeBank;
            a.OverchargeBank = 0;
        }

        public override void OnAttackEnd(GameState s, CreatureUnit a) { a.DischargeBonus = 0; }
    }

    // -- the registry --------------------------------------------------------------------------

    /// <summary>
    /// The dispatcher every rules site calls. Keyed by the Keyword enum, so lookup is an array
    /// index and iteration over handlers follows the enum's declared order - which is what pins
    /// the upkeep pass order (Chrysalis before Overcharge, exactly as startTurn runs them).
    ///
    /// kwOf: only a non-worker creature has a keyword at all. Tokens are built keyword-less.
    /// </summary>
    public static class KeywordEngine
    {
        static readonly IKeywordHandler[] _byKeyword = Build();

        static IKeywordHandler[] Build()
        {
            var a = new IKeywordHandler[9];
            Put(a, new DetonateHandler());
            Put(a, new UndertowHandler());
            Put(a, new EntrenchHandler());
            Put(a, new WardHandler());
            Put(a, new ReapHandler());
            Put(a, new ChrysalisHandler());
            Put(a, new ScourHandler());
            Put(a, new OverchargeHandler());
            return a;
        }

        static void Put(IKeywordHandler[] a, IKeywordHandler h) { a[(int)h.Keyword] = h; }

        /// <summary>All eight, in Keyword enum order (index 0 is null). Never re-ordered.</summary>
        public static IReadOnlyList<IKeywordHandler> All { get { return _byKeyword; } }

        /// <summary>kwOf(o) - null for workers, tokens, structures and vanilla creatures.</summary>
        public static IKeywordHandler Of(CreatureUnit c)
        {
            if (c == null || c.IsWorker) return null;
            int k = (int)c.Keyword;
            return k > 0 && k < _byKeyword.Length ? _byKeyword[k] : null;
        }

        public static IKeywordHandler Of(Keyword k)
        {
            int i = (int)k;
            return i > 0 && i < _byKeyword.Length ? _byKeyword[i] : null;
        }

        /// <summary>Rules text for the inspect panel, "" when the creature has no keyword. The
        /// catalog is needed because Chrysalis names the form it hatches into.</summary>
        public static string TextOf(CreatureUnit c, ICardCatalog cat)
        {
            var h = Of(c);
            return h == null ? "" : h.Text(c, cat);
        }

        /// <summary>Short ability-box label, "" when the creature has no keyword.</summary>
        public static string LabelOf(CreatureUnit c)
        {
            var h = Of(c);
            return h == null ? "" : h.Label(c);
        }

        // ---- hook entry points --------------------------------------------------------------

        /// <summary>ENTER: summon, play-on-top, or flip. Never called for a card merely set.</summary>
        public static void OnEnter(GameState s, CreatureUnit cr, Side owner, ICardCatalog cat,
                                   EventSink ev)
        {
            var h = Of(cr);
            if (h != null) h.OnEnter(s, cr, owner, cat, ev);
        }

        /// <summary>DEATH: from the sweep, cell already freed. Workers never trigger.</summary>
        public static void OnDeath(GameState s, CreatureUnit dead, Side owner, ICardCatalog cat,
                                   EventSink ev)
        {
            var h = Of(dead);
            if (h != null) h.OnDeath(s, dead, owner, cat, ev);
        }

        /// <summary>
        /// UPKEEP: ONE FULL PASS PER KEYWORD, in enum order, exactly as startTurn calls
        /// chrysalisUpkeep then overchargeUpkeep. Interleaving them into a single walk would
        /// reorder the events and let one keyword observe another's mutation. The unit list is
        /// snapshotted per pass so a hatch cannot disturb the walk.
        /// </summary>
        public static void UpkeepTick(GameState s, Side owner, ICardCatalog cat, EventSink ev)
        {
            for (int k = 0; k < _byKeyword.Length; k++)
            {
                var h = _byKeyword[k];
                if (h == null || !h.HasUpkeep) continue;

                var units = new List<CreatureUnit>();
                foreach (var kv in s.ObjectsOf(owner))
                {
                    var c = kv.Value as CreatureUnit;
                    if (c != null && !c.IsWorker && c.Keyword == h.Keyword) units.Add(c);
                }
                for (int i = 0; i < units.Count; i++)
                    h.OnUpkeep(s, units[i], owner, cat, ev);
            }
        }

        /// <summary>
        /// PRE-COMBAT: for each keyword present among the DEFENDERS its handler fires once - not
        /// once per unit carrying it. attackers may be mutated (Undertow removes its mark).
        /// </summary>
        public static void PreCombat(GameState s, List<CreatureUnit> attackers,
                                     List<CreatureUnit> defenders, ICardCatalog cat, EventSink ev)
        {
            for (int k = 0; k < _byKeyword.Length; k++)
            {
                var h = _byKeyword[k];
                if (h == null || !h.HasPreCombat) continue;

                bool present = false;
                for (int i = 0; i < defenders.Count && !present; i++)
                {
                    var d = defenders[i];
                    present = d != null && d.Hp > 0 && !d.IsWorker && d.Keyword == h.Keyword;
                }
                if (present) h.OnPreCombat(s, attackers, defenders, cat, ev);
            }
        }

        /// <summary>ATTACK-PREP: every live attacker of this resolution, in declaration order.</summary>
        public static void AttackPrep(GameState s, List<int> attackerIds, EventSink ev)
        {
            for (int i = 0; i < attackerIds.Count; i++)
            {
                CellRef at;
                bool onBoard;
                var a = s.FindById(attackerIds[i], out at, out onBoard) as CreatureUnit;
                var h = Of(a);
                if (h != null) h.OnAttackPrep(s, a, ev);
            }
        }

        /// <summary>Tear down whatever ATTACK-PREP staged, for the same attacker set.</summary>
        public static void AttackEnd(GameState s, List<int> attackerIds)
        {
            for (int i = 0; i < attackerIds.Count; i++)
            {
                CellRef at;
                bool onBoard;
                var a = s.FindById(attackerIds[i], out at, out onBoard) as CreatureUnit;
                var h = Of(a);
                if (h != null) h.OnAttackEnd(s, a);
            }
        }

        /// <summary>ON-HIT: a surviving unblocked attacker connected. Returns true if the hook
        /// ran, so the caller knows whether a sweep is owed.</summary>
        public static bool OnHit(GameState s, CreatureUnit attacker, Side defender,
                                 ICardCatalog cat, EventSink ev)
        {
            var h = Of(attacker);
            if (h == null || !h.HasOnHit) return false;
            h.OnHit(s, attacker, defender, cat, ev);
            return true;
        }

        /// <summary>Does this attacker carry an ON-HIT keyword worth remembering for later?</summary>
        public static bool HasOnHit(CreatureUnit attacker)
        {
            var h = Of(attacker);
            return h != null && h.HasOnHit;
        }
    }
}
