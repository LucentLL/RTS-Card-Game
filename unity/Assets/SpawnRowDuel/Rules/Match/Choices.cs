using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>Discriminator for serializing GameState.Pending.</summary>
    public enum PendingKind : byte
    {
        None = 0, Blocker = 1, Absorber = 2, Retaliation = 3, ResponseWindow = 4,
    }

    /// <summary>
    /// A suspended choice. The engine advances until it needs an answer, writes one of these into
    /// GameState, and returns AwaitingChoice; a RespondCommand resumes it. This replaces the JS's
    /// await-chain modals - the single biggest structural obstacle to a deterministic, snapshot-
    /// able core (spec 03 s17 risk 10, design 01 s6).
    ///
    /// Requests are IMMUTABLE once constructed. Clone and snapshot may share the instance; a new
    /// decision point always builds a new request with freshly recomputed eligibility.
    /// </summary>
    public abstract class PendingRequest
    {
        public readonly Side Responder;

        protected PendingRequest(Side responder) { Responder = responder; }

        public abstract PendingKind Kind { get; }
    }

    /// <summary>"This attacker crosses into your rows - assign blockers (possibly none)."</summary>
    public sealed class BlockerRequest : PendingRequest
    {
        public readonly int AttackerId;
        public readonly int DeclarationIndex;
        public readonly int DeclarationCount;
        public readonly UnitRef[] Eligible;

        public BlockerRequest(Side responder, int attackerId, int declarationIndex,
                              int declarationCount, UnitRef[] eligible) : base(responder)
        {
            AttackerId = attackerId;
            DeclarationIndex = declarationIndex;
            DeclarationCount = declarationCount;
            Eligible = eligible ?? new UnitRef[0];
        }

        public override PendingKind Kind { get { return PendingKind.Blocker; } }
    }

    /// <summary>"Your gang-blocked attacker deals damage to exactly ONE blocker - which?"</summary>
    public sealed class AbsorberRequest : PendingRequest
    {
        public readonly int AttackerId;
        public readonly UnitRef[] Blockers;

        public AbsorberRequest(Side responder, int attackerId, UnitRef[] blockers) : base(responder)
        {
            AttackerId = attackerId;
            Blockers = blockers ?? new UnitRef[0];
        }

        public override PendingKind Kind { get { return PendingKind.Absorber; } }
    }

    /// <summary>"Your creature was attacked by a group - it strikes back at ONE attacker."</summary>
    public sealed class RetaliationRequest : PendingRequest
    {
        public readonly int DefenderId;
        public readonly UnitRef[] Attackers;

        public RetaliationRequest(Side responder, int defenderId, UnitRef[] attackers) : base(responder)
        {
            DefenderId = defenderId;
            Attackers = attackers ?? new UnitRef[0];
        }

        public override PendingKind Kind { get { return PendingKind.Retaliation; } }
    }

    /// <summary>
    /// The anti-tell response window: may a set trap spring? Only the DECISION lives here; the
    /// constant-duration timer that hides whether a trap is even held is entirely the view's
    /// obligation (spec 03 s10.1).
    ///
    /// This is the core-side replacement for BOTH JS halves of the summon trap. The JS sprang the
    /// AI's trap automatically and gave the human a modal (later, a RESP bar) - two code paths
    /// with different timing. Here the defender is always ASKED, whoever they are; a policy that
    /// answers "spring the first one" reproduces the old auto-spring exactly, and the human gets
    /// the choice the RESP layer already gave them (spec 06 s7.4, s7.6).
    ///
    /// Subject is what provoked the window: the freshly summoned creature for a Summon trigger,
    /// or the struck defender for an Attack trigger (UnitRef.None when the blow landed on
    /// something that is not a unit).
    /// </summary>
    public sealed class ResponseWindowRequest : PendingRequest
    {
        public readonly TrapTrigger Trigger;
        public readonly UnitRef[] ArmedTraps;
        public readonly UnitRef Subject;

        public ResponseWindowRequest(Side responder, TrapTrigger trigger, UnitRef[] armedTraps)
            : this(responder, trigger, armedTraps, UnitRef.None)
        {
        }

        public ResponseWindowRequest(Side responder, TrapTrigger trigger, UnitRef[] armedTraps,
                                     UnitRef subject) : base(responder)
        {
            Trigger = trigger;
            ArmedTraps = armedTraps ?? new UnitRef[0];
            Subject = subject;
        }

        public override PendingKind Kind { get { return PendingKind.ResponseWindow; } }
    }

    // ---- answers --------------------------------------------------------------------------

    public abstract class ChoiceResponse
    {
    }

    public sealed class BlockersChosen : ChoiceResponse
    {
        public readonly UnitRef[] Blockers;     // empty == let it through

        public BlockersChosen(UnitRef[] blockers) { Blockers = blockers ?? new UnitRef[0]; }
    }

    public sealed class IndexChosen : ChoiceResponse
    {
        public readonly int Index;

        public IndexChosen(int index) { Index = index; }
    }

    public sealed class TrapChosen : ChoiceResponse
    {
        public readonly bool Pass;              // true == decline the window
        public readonly UnitRef Trap;

        public TrapChosen(UnitRef trap) { Pass = false; Trap = trap; }
        private TrapChosen() { Pass = true; Trap = UnitRef.None; }

        public static readonly TrapChosen Passed = new TrapChosen();
    }
}
