using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// CMB.declare (15_combat.js:235-262): one declaration at a time, ALTERNATING - the
    /// defender answers each declaration's blockers immediately (as a parked BlockerRequest)
    /// before the next may be declared. The attacker taps AT DECLARATION, not at resolution.
    /// </summary>
    public sealed class DeclareAttackHandler : ICommandHandler
    {
        public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
        {
            var m = (DeclareAttackCommand)cmd;
            if (s.Turn != m.Actor) return Rejection.NotYourTurn;
            if (s.Phase != TurnPhase.Action) return Rejection.WrongPhase;
            if (s.Combat.Resolving) return Rejection.ChoicePending;

            var a = s.At(m.Attacker) as CreatureUnit;
            if (a == null) return Rejection.NotACreature;
            if (a.Id != m.UnitId) return Rejection.NoSuchUnit;
            if (a.Owner != m.Actor) return Rejection.NotYourUnit;
            if (a.IsWorker) return Rejection.AttackerIsWorker;
            if (a.Sick) return Rejection.AttackerSick;
            if (a.Tapped) return Rejection.AttackerTapped;
            if (a.Hp <= 0) return Rejection.NoSuchUnit;

            var ut = m.Target as UnitTarget;
            if (ut != null)
            {
                CellRef at;
                bool onBoard;
                var t = s.FindById(ut.UnitId, out at, out onBoard);
                if (t == null || !onBoard) return Rejection.NoSuchUnit;
                if (t.Owner == m.Actor) return Rejection.TargetNotEnemy;
                return Rejection.None;
            }

            var wt = m.Target as WallTarget;
            if (wt != null)
                return wt.Defender != m.Actor ? Rejection.None : Rejection.TargetKindIllegal;

            var st = m.Target as WorkerStackTarget;
            if (st != null)
            {
                if (st.Owner == m.Actor) return Rejection.TargetNotEnemy;
                if (st.Zone == WorkerZone.Raid) return Rejection.TargetKindIllegal;
                return Rejection.None;
            }

            return Rejection.TargetKindIllegal;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            var m = (DeclareAttackCommand)cmd;
            var a = (CreatureUnit)s.At(m.Attacker);
            a.Tapped = true;                                    // taps at declaration time

            var d = new AttackDeclaration
            {
                Attacker = m.Attacker,
                AttackerUnitId = a.Id,
            };

            var ut = m.Target as UnitTarget;
            var wt = m.Target as WallTarget;
            var st = m.Target as WorkerStackTarget;
            if (ut != null)
            {
                CellRef at;
                bool onBoard;
                var t = s.FindById(ut.UnitId, out at, out onBoard);
                d.Kind = DeclarationKind.Unit;
                d.TargetCell = at;
                d.TargetUnitId = ut.UnitId;
                d.TargetSide = t.Owner;
                d.TargetKind = t.Kind;      // for the stale-object semantics at resolve time
            }
            else if (wt != null)
            {
                d.Kind = DeclarationKind.Wall;
                d.TargetSide = wt.Defender;
            }
            else
            {
                d.Kind = DeclarationKind.WorkerStack;
                d.TargetSide = st.Owner;
                d.TargetZone = st.Zone;
            }

            s.Combat.Declarations.Add(d);
            int index = s.Combat.Declarations.Count - 1;
            ev.Add(new AttackDeclared(a.Id, m.Target, index));

            if (m.DeferBlockers)
            {
                d.BlockersDeferred = true;      // collected at resolve start (the s12 cadence)
                return;
            }

            // the defender answers IMMEDIATELY - a parked choice - unless nothing may block
            var eligible = CombatEligibility.ForDeclaration(s, d, m.Actor);
            if (eligible.Count > 0)
                s.Pending = new BlockerRequest(TurnMachine.Other(m.Actor), a.Id, index,
                    s.Combat.Declarations.Count, eligible.ToArray());
        }
    }

    /// <summary>Entry gate for resolution: declarations exist, nothing is mid-flight.</summary>
    public sealed class ResolveCombatHandler : ICommandHandler
    {
        public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
        {
            if (s.Turn != cmd.Actor) return Rejection.NotYourTurn;
            if (s.Phase != TurnPhase.Action) return Rejection.WrongPhase;
            if (!s.Combat.HasDeclarations) return Rejection.NothingDeclared;
            if (s.Combat.Resolving) return Rejection.ChoicePending;
            return Rejection.None;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            CombatResolver.Begin(s, cat, ev);
        }
    }

    /// <summary>
    /// Stand the whole assault down. The mirror of DeclareAttackHandler: every declaration goes,
    /// and every attacker one of them tapped is untapped.
    ///
    /// It is exact rather than approximate. Validate refuses an attacker whose Tapped flag is not
    /// this handler's to clear, so the ONLY thing untapped here is a creature this turn's
    /// declarations tapped - a creature that spent its second move (MoveUnitHandler taps that one
    /// too) could never have declared in the first place, and one that had already blocked is not
    /// in this list at all.
    /// </summary>
    public sealed class WithdrawAttackHandler : ICommandHandler
    {
        public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
        {
            if (s.Turn != cmd.Actor) return Rejection.NotYourTurn;
            if (s.Phase != TurnPhase.Action) return Rejection.WrongPhase;
            if (!s.Combat.HasDeclarations) return Rejection.NothingDeclared;
            if (s.Combat.Resolving) return Rejection.ChoicePending;

            // Once the defender has spent a blocker on one of these, the attack has been answered
            // and taking it back would un-ask a question they have already paid for.
            var decls = s.Combat.Declarations;
            for (int i = 0; i < decls.Count; i++)
                if (decls[i].Blockers.Count > 0) return Rejection.BlockersCommitted;

            return Rejection.None;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            var decls = s.Combat.Declarations;
            for (int i = 0; i < decls.Count; i++)
            {
                bool onBoard;
                var a = s.FindById(decls[i].AttackerUnitId, out _, out onBoard) as CreatureUnit;

                // by ID, and only if it is still standing on the board: an attacker that has been
                // bounced or destroyed since it declared has no Tapped flag left to clear, and the
                // cell it declared from may hold somebody else by now (the s17 risk-2 rule).
                if (a != null && onBoard && a.Owner == cmd.Actor) a.Tapped = false;
            }

            int n = decls.Count;
            s.Combat.Clear();
            ev.Add(new AttackWithdrawn(cmd.Actor, n));
        }
    }

    /// <summary>
    /// The one door through a parked choice. The response SHAPE is validated against the
    /// outstanding request, and blocker refs are re-validated by id against a FRESHLY
    /// recomputed eligibility list - what a host must do against a malicious guest.
    /// </summary>
    public sealed class RespondHandler : ICommandHandler
    {
        public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
        {
            var m = (RespondCommand)cmd;
            var pending = s.Pending;
            if (pending == null) return Rejection.NoPendingRequest;
            if (pending.Responder != m.Actor) return Rejection.NotYourTurn;

            var blockerReq = pending as BlockerRequest;
            if (blockerReq != null)
            {
                var chosen = m.Response as BlockersChosen;
                if (chosen == null) return Rejection.WrongResponseShape;

                var d = s.Combat.Declarations[blockerReq.DeclarationIndex];
                var fresh = CombatEligibility.ForDeclaration(s, d, TurnMachine.Other(m.Actor));
                for (int i = 0; i < chosen.Blockers.Length; i++)
                {
                    bool ok = false;
                    for (int j = 0; j < fresh.Count && !ok; j++)
                        ok = fresh[j].UnitId == chosen.Blockers[i].UnitId;
                    if (!ok) return Rejection.WrongResponseShape;

                    for (int j = 0; j < i; j++)                       // no double-commit
                        if (chosen.Blockers[j].UnitId == chosen.Blockers[i].UnitId)
                            return Rejection.WrongResponseShape;
                }
                return Rejection.None;
            }

            var absorber = pending as AbsorberRequest;
            if (absorber != null)
            {
                var idx = m.Response as IndexChosen;
                if (idx == null) return Rejection.WrongResponseShape;
                if (idx.Index < 0 || idx.Index >= absorber.Blockers.Length)
                    return Rejection.WrongResponseShape;
                return Rejection.None;
            }

            var retaliation = pending as RetaliationRequest;
            if (retaliation != null)
            {
                var idx = m.Response as IndexChosen;
                if (idx == null) return Rejection.WrongResponseShape;
                if (idx.Index < 0 || idx.Index >= retaliation.Attackers.Length)
                    return Rejection.WrongResponseShape;
                return Rejection.None;
            }

            var window = pending as ResponseWindowRequest;
            if (window != null)
            {
                var pick = m.Response as TrapChosen;
                if (pick == null) return Rejection.WrongResponseShape;
                if (pick.Pass) return Rejection.None;

                // the trap must be one this window actually offered...
                bool offered = false;
                for (int i = 0; i < window.ArmedTraps.Length && !offered; i++)
                    offered = window.ArmedTraps[i].UnitId == pick.Trap.UnitId;
                if (!offered) return Rejection.WrongResponseShape;

                // ...and must still be there, armed, and the responder's - re-checked live, the
                // way a host re-checks a guest's answer
                CellRef at;
                if (Traps.ResolveTrapRef(s, m.Actor, pick.Trap, window.Trigger, out at) == null)
                    return Rejection.WrongResponseShape;
                return Rejection.None;
            }

            return Rejection.WrongResponseShape;
        }

        public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
        {
            var m = (RespondCommand)cmd;

            var blockerReq = s.Pending as BlockerRequest;
            if (blockerReq != null)
            {
                var chosen = (BlockersChosen)m.Response;
                var d = s.Combat.Declarations[blockerReq.DeclarationIndex];
                var ids = new int[chosen.Blockers.Length];
                for (int i = 0; i < chosen.Blockers.Length; i++)
                {
                    var b = s.FindById(chosen.Blockers[i].UnitId, out _, out _) as CreatureUnit;
                    if (b == null) continue;
                    b.HasBlocked = true;         // out of the pool for every later declaration
                    d.Blockers.Add(chosen.Blockers[i]);
                    ids[i] = b.Id;
                }
                ev.Add(new BlockersAssigned(blockerReq.DeclarationIndex, ids));
                s.Pending = null;

                // a deferred answer arrived mid-CollectBlocks: mark it settled and let the
                // resolver park the next declaration's request or fall through to the fights
                if (s.Combat.Stage == CombatStage.CollectBlocks)
                {
                    d.BlockersDeferred = false;
                    CombatResolver.Step(s, cat, ev);
                }
                return;
            }

            var window = s.Pending as ResponseWindowRequest;
            if (window != null)
            {
                var pick = (TrapChosen)m.Response;
                s.Pending = null;

                if (window.Trigger == TrapTrigger.Summon)
                {
                    // the summon that opened this window is otherwise complete either way
                    if (!pick.Pass)
                        Traps.SpringSummonTrap(s, m.Actor, pick.Trap, window.Subject, cat, ev);
                    CombatResolver.CheckWin(s, ev);
                    return;
                }

                // attack trigger: the answer belongs to the spring site the resolver parked on
                s.Combat.TrapAnswered = true;
                s.Combat.ChosenTrap = pick.Pass ? UnitRef.None : pick.Trap;
                CombatResolver.Step(s, cat, ev);
                return;
            }

            // absorber / retaliation: hand the answer to the resolver and let it continue
            var idxChosen = (IndexChosen)m.Response;
            s.Combat.HasAnswer = true;
            s.Combat.AnsweredIndex = idxChosen.Index;
            s.Pending = null;
            CombatResolver.Step(s, cat, ev);
        }
    }
}
