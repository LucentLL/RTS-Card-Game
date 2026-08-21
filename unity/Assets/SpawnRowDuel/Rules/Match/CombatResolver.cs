using System;
using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// CMB._resolveNow (15_combat.js:309-366) as an explicit step machine. The JS interleaved
    /// rules mutation with awaited modals; here the resolver advances until it needs an answer,
    /// parks a PendingRequest, and resumes from the cursor in CombatState - so a mid-combat
    /// snapshot is complete and resumable, which is what lets netcode drop in later.
    ///
    /// The strict order, verbatim from the spec: blocked pair fights (declaration order) ->
    /// unblocked creature target groups (insertion order) -> misc unblocked (walls accumulate,
    /// worker stacks via the legacy engine, structures one-way, face-downs provoke, traps
    /// spring) -> summed wall damage applied once -> Scour strikes -> the win check. A death
    /// sweep runs after each individual fight: "simultaneous" is per-tier-per-fight, never
    /// global.
    /// </summary>
    public static class CombatResolver
    {
        /// <summary>
        /// Resolution entry. Declarations that DEFERRED their blocker answers (the s12
        /// mirrored cadence - the whole assault visible before any block commits) collect them
        /// first; then the main resolution starts: live-filter, discharge, partition ONCE.
        /// </summary>
        public static void Begin(GameState s, ICardCatalog cat, EventSink ev)
        {
            var c = s.Combat;
            bool anyDeferred = false;
            for (int i = 0; i < c.Declarations.Count; i++)
                if (c.Declarations[i].BlockersDeferred) anyDeferred = true;

            if (anyDeferred)
            {
                c.Stage = CombatStage.CollectBlocks;
                c.Cursor = 0;
                Step(s, cat, ev);
                return;
            }

            StartMainResolution(s, cat, ev);
            Step(s, cat, ev);
        }

        /// <summary>Live-filter, discharge Overcharge, partition ONCE - the JS step 1-4.</summary>
        static void StartMainResolution(GameState s, ICardCatalog cat, EventSink ev)
        {
            var c = s.Combat;
            c.BlockedDeclIndices.Clear();
            c.OpenDeclIndices.Clear();
            c.GroupTargetIds.Clear();
            c.GroupOffsets.Clear();
            c.GroupDeclIndices.Clear();
            c.ResolutionAttackerIds.Clear();
            c.ScourHitUnitIds.Clear();
            c.AccumulatedWallDamage = 0;
            c.HasAnswer = false;
            c.AnsweredIndex = 0;

            for (int i = 0; i < c.Declarations.Count; i++)
            {
                var d = c.Declarations[i];

                // the JS's step-2 capture: what a unit-declaration does in the misc step
                // depends on whether its target was alive when resolution BEGAN
                if (d.Kind == DeclarationKind.Unit)
                {
                    CellRef tAt;
                    bool tOnBoard;
                    var tObj = s.FindById(d.TargetUnitId, out tAt, out tOnBoard);
                    var tCre = tObj as CreatureUnit;
                    d.TargetLiveAtResolve = tObj != null && tOnBoard
                        && (tCre == null || tCre.Hp > 0)
                        && !(tObj is StructureUnit && ((StructureUnit)tObj).Hp <= 0);
                }

                var a = s.FindById(d.AttackerUnitId, out _, out _) as CreatureUnit;
                if (a == null || a.Hp <= 0) continue;          // dead declarations drop silently
                c.ResolutionAttackerIds.Add(a.Id);

                bool blocked = false;
                for (int b = 0; b < d.Blockers.Count && !blocked; b++)
                {
                    var blk = s.FindById(d.Blockers[b].UnitId, out _, out _) as CreatureUnit;
                    blocked = blk != null && blk.Hp > 0;
                }
                // "a blocked attacker stays blocked even if it kills its whole gang"
                if (blocked) c.BlockedDeclIndices.Add(i);
                else c.OpenDeclIndices.Add(i);
            }

            KeywordEngine.AttackPrep(s, c.ResolutionAttackerIds, ev);
            c.Stage = CombatStage.BlockedPairFights;
            c.Cursor = 0;
            c.SubCursor = 0;
        }

        /// <summary>Advance as far as possible; parks on s.Pending when a choice is needed.</summary>
        public static void Step(GameState s, ICardCatalog cat, EventSink ev)
        {
            var c = s.Combat;
            var actor = s.Turn;
            var defender = TurnMachine.Other(actor);

            while (true)
            {
                switch (c.Stage)
                {
                    case CombatStage.Idle:
                        return;

                    // ── STEP 0: collect deferred blocker answers, declaration order ───────
                    case CombatStage.CollectBlocks:
                        {
                            if (c.Cursor >= c.Declarations.Count)
                            {
                                StartMainResolution(s, cat, ev);
                                continue;
                            }

                            var d = c.Declarations[c.Cursor];
                            if (!d.BlockersDeferred)
                            {
                                c.Cursor++;
                                continue;
                            }

                            var a = s.FindById(d.AttackerUnitId, out _, out _) as CreatureUnit;
                            if (a == null || a.Hp <= 0)
                            {
                                d.BlockersDeferred = false;    // a dead assault asks nothing
                                c.Cursor++;
                                continue;
                            }

                            var eligible = CombatEligibility.ForDeclaration(s, d, actor);
                            if (eligible.Count == 0)
                            {
                                d.BlockersDeferred = false;
                                c.Cursor++;
                                continue;
                            }

                            // the defender answers seeing the COMPLETE assault (s12); the
                            // HasBlocked cascade still holds because each answer lands before
                            // the next request is parked
                            s.Pending = new BlockerRequest(defender, a.Id, c.Cursor,
                                s.Combat.Declarations.Count, eligible.ToArray());
                            return;
                        }

                    // ── STEP 1: blocked declarations, pair fights, declaration order ──────
                    case CombatStage.BlockedPairFights:
                        {
                            if (c.Cursor >= c.BlockedDeclIndices.Count)
                            {
                                BuildTargetGroups(s, c);
                                c.Stage = CombatStage.UnblockedCreatureGroups;
                                c.Cursor = 0;
                                c.SubCursor = 0;
                                continue;
                            }

                            var d = c.Declarations[c.BlockedDeclIndices[c.Cursor]];
                            var a = s.FindById(d.AttackerUnitId, out _, out _) as CreatureUnit;
                            var blks = LiveBlockers(s, d);
                            if (a == null || a.Hp <= 0 || blks.Count == 0)
                            {
                                c.Cursor++;
                                continue;
                            }

                            int ab = 0;
                            if (blks.Count > 1)
                            {
                                if (!c.HasAnswer)
                                {
                                    // the ATTACKER assigns the blow among its gang-blockers
                                    s.Pending = new AbsorberRequest(actor, a.Id, Refs(s, blks));
                                    return;
                                }
                                ab = Clamp(c.AnsweredIndex, blks.Count);
                                c.HasAnswer = false;
                            }

                            PairFight(s, a, blks, ab, cat, ev);
                            c.Cursor++;
                            continue;
                        }

                    // ── STEP 2: unblocked strikes on creatures, grouped by target ─────────
                    case CombatStage.UnblockedCreatureGroups:
                        {
                            if (c.Cursor >= c.GroupTargetIds.Count)
                            {
                                c.Stage = CombatStage.UnblockedMisc;
                                c.Cursor = 0;
                                c.SubCursor = 0;
                                continue;
                            }

                            var t = s.FindById(c.GroupTargetIds[c.Cursor], out _, out _) as CreatureUnit;
                            var grp = GroupAttackers(s, c, c.Cursor);
                            if (t == null || t.Hp <= 0 || grp.Count == 0)
                            {
                                c.Cursor++;
                                c.SubCursor = 0;
                                continue;
                            }

                            if (c.SubCursor == 0)              // the defender's attack trap, once
                            {
                                if (!TrapDecisionReady(s, c, defender, UnitRefOf(s, t)))
                                    return;                     // parked on the response window
                                ConsumeTrapDecision(s, c, defender, grp, t, ev);
                                c.SubCursor = 1;
                                for (int i = grp.Count - 1; i >= 0; i--)      // Backlash may kill
                                    if (grp[i].Hp <= 0) grp.RemoveAt(i);
                                if (grp.Count == 0 || t.Hp <= 0)
                                {
                                    DeathSweep.Cleanup(s, cat, ev);
                                    c.Cursor++;
                                    c.SubCursor = 0;
                                    continue;
                                }
                            }

                            int ri = 0;
                            if (grp.Count > 1)
                            {
                                if (!c.HasAnswer)
                                {
                                    // the DEFENDER picks who its creature strikes back at.
                                    // (The JS AI never chose - it always ate index 0; that
                                    // policy answers 0 here, same outcome, netcode-ready shape.)
                                    s.Pending = new RetaliationRequest(defender, t.Id, Refs(s, grp));
                                    return;
                                }
                                ri = Clamp(c.AnsweredIndex, grp.Count);
                                c.HasAnswer = false;
                            }

                            TargetFight(s, grp, t, ri, cat, ev);
                            c.Cursor++;
                            c.SubCursor = 0;
                            continue;
                        }

                    // ── STEP 3: everything else unblocked, declaration order ──────────────
                    case CombatStage.UnblockedMisc:
                        {
                            while (c.Cursor < c.OpenDeclIndices.Count)
                            {
                                var d = c.Declarations[c.OpenDeclIndices[c.Cursor]];
                                var a = s.FindById(d.AttackerUnitId, out _, out _) as CreatureUnit;

                                // The JS walks CAPTURED attacker objects, so one that Undertow
                                // hurled back to hand during an earlier fight still passes its
                                // `x.A.h>0` test and still collects its Scour credit - a strike
                                // delivered by a flier that is no longer on the board. Resolving
                                // by id cannot see a hand card, so the bounce is recorded at the
                                // moment it happens and re-read here (spec 06 s6.2; the JS's own
                                // quirk, reproduced deliberately).
                                if (a == null && c.BouncedScourIds.Contains(d.AttackerUnitId))
                                {
                                    c.ScourHitUnitIds.Add(d.AttackerUnitId);
                                    NextMisc(c);
                                    continue;
                                }
                                if (a == null || a.Hp <= 0) { NextMisc(c); continue; }
                                bool scour = KeywordEngine.HasOnHit(a);

                                if (d.Kind == DeclarationKind.Wall)
                                {
                                    c.AccumulatedWallDamage += a.EffectiveAttack;
                                    if (scour) c.ScourHitUnitIds.Add(a.Id);
                                    NextMisc(c);
                                    continue;
                                }

                                if (d.Kind == DeclarationKind.WorkerStack)
                                {
                                    var pool = s.P(d.TargetSide).Workers[(int)d.TargetZone];
                                    var stack = new List<CreatureUnit>(pool.Members);
                                    LegacyCombat.Resolve(s, new List<CreatureUnit> { a }, stack, cat, ev);
                                    if (scour && a.Hp > 0) c.ScourHitUnitIds.Add(a.Id);
                                    NextMisc(c);
                                    continue;
                                }

                                // a target already dead when resolution BEGAN does nothing -
                                // the JS's step-2 capture came back null (spec 03 s7 step 7)
                                if (!d.TargetLiveAtResolve) { NextMisc(c); continue; }

                                CellRef at;
                                bool onBoard;
                                var o = s.FindById(d.TargetUnitId, out at, out onBoard);
                                var single = new List<CreatureUnit> { a };

                                if (o == null || !onBoard)
                                {
                                    // died DURING this resolution: the JS held the captured
                                    // object, so the declaration still plays out against the
                                    // corpse - a razed building still re-springs the attack
                                    // trap, and every kind still grants the Scour credit
                                    if (d.TargetKind == UnitKind.Building)
                                    {
                                        if (!TrapDecisionReady(s, c, defender, UnitRef.None))
                                            return;
                                        ConsumeTrapDecision(s, c, defender, single, null, ev);
                                        // the JS sweeps here too (15_combat.js:352) - so a
                                        // Backlash that kills the attacker at THIS site graves
                                        // it and fires its death keyword inside the combat,
                                        // instead of leaving a 0-hp corpse holding its cell
                                        DeathSweep.Cleanup(s, cat, ev);
                                    }
                                    if (scour && a.Hp > 0) c.ScourHitUnitIds.Add(a.Id);
                                    NextMisc(c);
                                    continue;
                                }

                                if (o is CreatureUnit)         // fought already in step 2
                                {
                                    if (scour && a.Hp > 0) c.ScourHitUnitIds.Add(a.Id);
                                    NextMisc(c);
                                    continue;
                                }

                                var b = o as StructureUnit;
                                if (b != null)
                                {
                                    if (!TrapDecisionReady(s, c, defender, UnitRef.Cell(at, b.Id)))
                                        return;
                                    ConsumeTrapDecision(s, c, defender, single, b, ev);
                                    // NO alive guard: a Backlash-killed attacker's blow still
                                    // lands - the JS strikes with no re-check
                                    var map = LegacyCombat.FocusFire(single,
                                        new List<BoardObject> { b });
                                    LegacyCombat.ApplyDamage(map, ev);       // ONE-WAY
                                    DeathSweep.Cleanup(s, cat, ev);
                                }
                                else if (o is ChargeUnit)
                                    Traps.ProvokeFaceDown(s, defender, at, single, cat, ev);
                                else if (o is TrapUnit)
                                    Traps.SpringTrap(s, defender, at, single, cat, ev);

                                if (scour && a.Hp > 0) c.ScourHitUnitIds.Add(a.Id);
                                NextMisc(c);
                            }

                            c.Stage = CombatStage.ApplyWallDamage;
                            continue;
                        }

                    // ── STEP 4: wall damage, aggregated and applied once ──────────────────
                    case CombatStage.ApplyWallDamage:
                        {
                            if (c.AccumulatedWallDamage > 0)
                            {
                                var p = s.P(defender);
                                p.Life = Math.Max(0, p.Life - c.AccumulatedWallDamage);
                                ev.Add(new WallStruck(defender, c.AccumulatedWallDamage, p.Life));
                            }
                            c.Stage = CombatStage.ScourStrikes;
                            continue;
                        }

                    // ── STEP 5: Scour aftermath, then the win check ───────────────────────
                    case CombatStage.ScourStrikes:
                        {
                            bool any = false;
                            for (int i = 0; i < c.ScourHitUnitIds.Count; i++)
                            {
                                var a = s.FindById(c.ScourHitUnitIds[i], out _, out _) as CreatureUnit;
                                if (a != null && a.Hp <= 0) continue;
                                if (a != null) KeywordEngine.OnHit(s, a, defender, cat, ev);
                                else                       // bounced to hand, but still striking
                                    ScourHandler.Shatter(s, c.ScourHitUnitIds[i], defender, ev);
                                any = true;
                            }
                            if (any) DeathSweep.Cleanup(s, cat, ev);

                            KeywordEngine.AttackEnd(s, c.ResolutionAttackerIds);
                            c.Clear();
                            CheckWin(s, ev);
                            return;
                        }
                }
            }
        }

        /// <summary>The only loss is a life pool at zero; mutual zero counts as YOUR defeat
        /// (checkWin, 17_turns_ai.js:392-407). No deck-out loss exists.</summary>
        public static void CheckWin(GameState s, EventSink ev)
        {
            if (s.IsOver) return;
            bool youOut = s.P(Side.You).Life <= 0;
            bool foeOut = s.P(Side.Foe).Life <= 0;
            if (!youOut && !foeOut) return;

            s.IsOver = true;
            s.Outcome = youOut ? MatchOutcome.FoeWin : MatchOutcome.YouWin;
            ev.Add(new MatchEnded(s.Outcome));
        }

        // ── fights ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// CMB.pairFight: the blocked attacker's ENTIRE blow lands on ONE absorber; EVERY
        /// blocker retaliates in full with raw attack (never the discharge bonus); two tiers,
        /// conditions read at tier start; blocking taps the blocker; a bounced attacker ends
        /// the fight before any damage.
        /// </summary>
        public static void PairFight(GameState s, CreatureUnit a, List<CreatureUnit> blks,
                                     int absorberIndex, ICardCatalog cat, EventSink ev)
        {
            for (int i = 0; i < blks.Count; i++) blks[i].Tapped = true;

            var group = new List<CreatureUnit> { a };
            KeywordEngine.PreCombat(s, group, blks, cat, ev);
            if (group.Count == 0 || a.Hp <= 0)
            {
                DeathSweep.Cleanup(s, cat, ev);
                return;
            }

            var absorber = blks[Clamp(absorberIndex, blks.Count)];
            var batch = new DamageBatch();

            for (int tier = 0; tier < 2; tier++)
            {
                bool fs = tier == 0;
                if (a.FirstStrike == fs && a.Hp > 0 && absorber.Hp > 0)
                    batch.Hit(absorber, a.EffectiveAttack);
                for (int i = 0; i < blks.Count; i++)
                {
                    var b = blks[i];
                    if (b.FirstStrike == fs && b.Hp > 0 && a.Hp > 0)
                        batch.Hit(a, b.Attack);                // every blocker strikes A, raw a
                }
                batch.ApplyAndClear(fs ? DamageTier.FirstStrike : DamageTier.Normal, ev);
            }

            DeathSweep.Cleanup(s, cat, ev);
        }

        /// <summary>
        /// CMB.targetFight: every attacker's blow lands on the target in full - no splitting,
        /// no spillover; the target retaliates ONCE, at full raw attack, against exactly one
        /// attacker; two tiers, conditions read at tier start.
        /// </summary>
        public static void TargetFight(GameState s, List<CreatureUnit> grp, CreatureUnit t,
                                       int retaliationIndex, ICardCatalog cat, EventSink ev)
        {
            KeywordEngine.PreCombat(s, grp, new List<CreatureUnit> { t }, cat, ev);
            for (int i = grp.Count - 1; i >= 0; i--)
                if (grp[i] == null || grp[i].Hp <= 0) grp.RemoveAt(i);
            if (grp.Count == 0 || t == null || t.Hp <= 0)
            {
                DeathSweep.Cleanup(s, cat, ev);
                return;
            }

            var back = grp[Clamp(retaliationIndex, grp.Count)];
            var batch = new DamageBatch();

            for (int tier = 0; tier < 2; tier++)
            {
                bool fs = tier == 0;
                for (int i = 0; i < grp.Count; i++)
                {
                    var a = grp[i];
                    if (a.FirstStrike == fs && a.Hp > 0 && t.Hp > 0)
                        batch.Hit(t, a.EffectiveAttack);
                }
                if (t.FirstStrike == fs && t.Hp > 0 && back.Hp > 0)
                    batch.Hit(back, t.Attack);
                batch.ApplyAndClear(fs ? DamageTier.FirstStrike : DamageTier.Normal, ev);
            }

            DeathSweep.Cleanup(s, cat, ev);
        }

        // ── the attack-trigger response window ──────────────────────────────────────────────

        /// <summary>
        /// Every site where the JS would have auto-sprung the defender's attack trap now OFFERS
        /// it instead. False means the resolver parked a ResponseWindowRequest and the caller
        /// must return; true means an answer is waiting (or the defender holds nothing at all)
        /// and ConsumeTrapDecision may run.
        ///
        /// A site is offered EVERY time it is reached, exactly as the JS re-ran findArmedTrap at
        /// each one - so a defender holding two traps can still spring both across a resolution.
        /// Answering "the first armed trap" every time reproduces the old auto-spring outcome.
        /// The constant-length pause that hides whether a trap is even held is the view's job.
        /// </summary>
        static bool TrapDecisionReady(GameState s, CombatState c, Side defender, UnitRef subject)
        {
            if (c.TrapAnswered) return true;                     // an answer is waiting
            var armed = Traps.FindArmedTraps(s, defender, TrapTrigger.Attack);
            if (armed.Count == 0) return true;                   // nothing to ask about

            s.Pending = new ResponseWindowRequest(defender, TrapTrigger.Attack,
                                                  armed.ToArray(), subject);
            return false;
        }

        /// <summary>Spend the parked answer. A pass, or no window at all, does nothing.</summary>
        static void ConsumeTrapDecision(GameState s, CombatState c, Side defender,
                                        List<CreatureUnit> attackers, BoardObject target,
                                        EventSink ev)
        {
            if (!c.TrapAnswered) return;
            var chosen = c.ChosenTrap;
            c.TrapAnswered = false;
            c.ChosenTrap = UnitRef.None;
            if (chosen.Kind != UnitRefKind.Cell) return;         // passed
            Traps.SpringAttackTrap(s, defender, chosen, attackers, target, ev);
        }

        static void NextMisc(CombatState c) { c.Cursor++; c.SubCursor = 0; }

        static UnitRef UnitRefOf(GameState s, BoardObject o)
        {
            if (o == null) return UnitRef.None;
            CellRef at;
            bool onBoard;
            s.FindById(o.Id, out at, out onBoard);
            return onBoard ? UnitRef.Cell(at, o.Id) : UnitRef.None;
        }

        // ── helpers ─────────────────────────────────────────────────────────────────────────

        /// <summary>byT: open declarations on living enemy CREATURES, grouped by target
        /// identity in insertion order, frozen at stage entry (spec 03 s7 step 6).</summary>
        static void BuildTargetGroups(GameState s, CombatState c)
        {
            c.GroupTargetIds.Clear();
            c.GroupOffsets.Clear();
            c.GroupDeclIndices.Clear();

            var perTarget = new List<KeyValuePair<int, List<int>>>();   // targetId -> decl indices
            for (int i = 0; i < c.OpenDeclIndices.Count; i++)
            {
                int di = c.OpenDeclIndices[i];
                var d = c.Declarations[di];
                if (d.Kind != DeclarationKind.Unit) continue;

                var a = s.FindById(d.AttackerUnitId, out _, out _) as CreatureUnit;
                if (a == null || a.Hp <= 0) continue;
                var t = s.FindById(d.TargetUnitId, out _, out _) as CreatureUnit;
                if (t == null || t.Hp <= 0) continue;

                List<int> bucket = null;
                for (int g = 0; g < perTarget.Count; g++)
                    if (perTarget[g].Key == t.Id) { bucket = perTarget[g].Value; break; }
                if (bucket == null)
                {
                    bucket = new List<int>();
                    perTarget.Add(new KeyValuePair<int, List<int>>(t.Id, bucket));
                }
                bucket.Add(di);
            }

            for (int g = 0; g < perTarget.Count; g++)
            {
                c.GroupTargetIds.Add(perTarget[g].Key);
                c.GroupOffsets.Add(c.GroupDeclIndices.Count);
                c.GroupDeclIndices.AddRange(perTarget[g].Value);
            }
        }

        static List<CreatureUnit> GroupAttackers(GameState s, CombatState c, int group)
        {
            var grp = new List<CreatureUnit>();
            int start = c.GroupOffsets[group];
            int end = group + 1 < c.GroupOffsets.Count ? c.GroupOffsets[group + 1]
                                                       : c.GroupDeclIndices.Count;
            for (int i = start; i < end; i++)
            {
                var d = c.Declarations[c.GroupDeclIndices[i]];
                var a = s.FindById(d.AttackerUnitId, out _, out _) as CreatureUnit;
                if (a != null && a.Hp > 0 && !grp.Contains(a)) grp.Add(a);
            }
            return grp;
        }

        static List<CreatureUnit> LiveBlockers(GameState s, AttackDeclaration d)
        {
            var live = new List<CreatureUnit>();
            for (int i = 0; i < d.Blockers.Count; i++)
            {
                var b = s.FindById(d.Blockers[i].UnitId, out _, out _) as CreatureUnit;
                if (b != null && b.Hp > 0 && !live.Contains(b)) live.Add(b);
            }
            return live;
        }

        static UnitRef[] Refs(GameState s, List<CreatureUnit> units)
        {
            var refs = new UnitRef[units.Count];
            for (int i = 0; i < units.Count; i++)
            {
                CellRef at;
                bool onBoard;
                s.FindById(units[i].Id, out at, out onBoard);
                refs[i] = onBoard ? UnitRef.Cell(at, units[i].Id)
                                  : UnitRef.Pool(default(PoolRef), units[i].Id);
            }
            return refs;
        }

        static int Clamp(int v, int count)
        {
            if (v < 0) return 0;
            if (v >= count) return count - 1;
            return v;
        }
    }

    /// <summary>
    /// Insertion-ordered damage accumulator - the JS used a Map whose iteration order is
    /// observable (spec 03 s17 risk 12). Damage within a tier is accumulated here and applied
    /// at once, which is what makes the tier simultaneous.
    /// </summary>
    public sealed class DamageBatch
    {
        private readonly List<CreatureUnit> _targets = new List<CreatureUnit>();
        private readonly List<int> _amounts = new List<int>();

        public void Hit(CreatureUnit target, int amount)
        {
            for (int i = 0; i < _targets.Count; i++)
                if (ReferenceEquals(_targets[i], target))
                {
                    _amounts[i] += amount;
                    return;
                }
            _targets.Add(target);
            _amounts.Add(amount);
        }

        public void ApplyAndClear(DamageTier tier, EventSink ev)
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                if (_amounts[i] <= 0) continue;
                _targets[i].Hp -= _amounts[i];
                ev.Add(new DamageApplied(_targets[i].Id, _amounts[i], 0, tier));
            }
            _targets.Clear();
            _amounts.Clear();
        }
    }
}
