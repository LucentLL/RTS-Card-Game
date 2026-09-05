using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// A player-expressible intent. This closed set is the ONLY way state ever mutates - the
    /// human UI, the AI policy and (later) the network host all submit these through the same
    /// CommandProcessor, which is what makes host-authoritative netcode a drop-in (design 01 s3.5).
    ///
    /// Every command that names a unit carries BOTH the coordinate and the UnitId, and Validate
    /// checks they agree - the structural fix for the JS bug where a declaration stored only a
    /// coordinate and resolved against whatever later moved into the cell (spec 03 s17 risk 2).
    /// </summary>
    public interface ICommand
    {
        Side Actor { get; }
    }

    public abstract class CommandBase : ICommand
    {
        private readonly Side _actor;
        protected CommandBase(Side actor) { _actor = actor; }
        public Side Actor { get { return _actor; } }
    }

    // ---- turn machine ---------------------------------------------------------------------

    public sealed class BeginTurnCommand : CommandBase
    {
        public BeginTurnCommand(Side actor) : base(actor) { }
    }

    public sealed class HarvestCommand : CommandBase
    {
        public HarvestCommand(Side actor) : base(actor) { }
    }

    public sealed class DrawForTurnCommand : CommandBase
    {
        public DrawForTurnCommand(Side actor) : base(actor) { }
    }

    public sealed class EndTurnCommand : CommandBase
    {
        public EndTurnCommand(Side actor) : base(actor) { }
    }

    // ---- upkeep settlement ----------------------------------------------------------------

    public sealed class UpkeepPayCommand : CommandBase
    {
        public readonly CellRef Target;
        public readonly int UnitId;

        public UpkeepPayCommand(Side actor, CellRef target, int unitId) : base(actor)
        {
            Target = target; UnitId = unitId;
        }
    }

    public sealed class UpkeepSacrificeCommand : CommandBase
    {
        public readonly CellRef Target;
        public readonly int UnitId;

        public UpkeepSacrificeCommand(Side actor, CellRef target, int unitId) : base(actor)
        {
            Target = target; UnitId = unitId;
        }
    }

    // ---- board ----------------------------------------------------------------------------

    public sealed class MoveUnitCommand : CommandBase
    {
        public readonly CellRef From;
        public readonly CellRef To;
        public readonly int UnitId;

        public MoveUnitCommand(Side actor, CellRef from, CellRef to, int unitId) : base(actor)
        {
            From = from; To = to; UnitId = unitId;
        }
    }

    // ---- hand plays -----------------------------------------------------------------------

    /// <summary>How a hand card is being played. Validated against the card's type - a check the
    /// JS local path skipped and only the MP host performed (spec 04 s19).</summary>
    public enum PlayMode : byte { Summon = 0, Build = 1, Set = 2, SetTrap = 3, Cast = 4 }

    public sealed class PlayCardCommand : CommandBase
    {
        public readonly int HandIndex;
        public readonly PlayMode Mode;
        public readonly CellRef To;

        public PlayCardCommand(Side actor, int handIndex, PlayMode mode, CellRef to) : base(actor)
        {
            HandIndex = handIndex; Mode = mode; To = to;
        }
    }

    // ---- structures -----------------------------------------------------------------------

    public sealed class BuildStructureCommand : CommandBase
    {
        public readonly StructId Def;
        public readonly Element Color;      // None when the family is not element-parameterised
        public readonly CellRef To;

        public BuildStructureCommand(Side actor, StructId def, Element color, CellRef to) : base(actor)
        {
            Def = def; Color = color; To = to;
        }
    }

    public sealed class UpgradeStructureCommand : CommandBase
    {
        public readonly CellRef At;
        public readonly int UnitId;
        public readonly StructId Target;

        public UpgradeStructureCommand(Side actor, CellRef at, int unitId, StructId target) : base(actor)
        {
            At = at; UnitId = unitId; Target = target;
        }
    }

    // ---- banked mana ----------------------------------------------------------------------

    public sealed class PourIntoChargeCommand : CommandBase
    {
        public readonly CellRef At;
        public readonly int UnitId;
        public readonly int Amount;

        public PourIntoChargeCommand(Side actor, CellRef at, int unitId, int amount) : base(actor)
        {
            At = at; UnitId = unitId; Amount = amount;
        }
    }

    public sealed class FlipChargeCommand : CommandBase
    {
        public readonly CellRef At;
        public readonly int UnitId;

        public FlipChargeCommand(Side actor, CellRef at, int unitId) : base(actor)
        {
            At = at; UnitId = unitId;
        }
    }

    public sealed class SendBankedManaCommand : CommandBase
    {
        public readonly CellRef From;
        public readonly CellRef To;

        public SendBankedManaCommand(Side actor, CellRef from, CellRef to) : base(actor)
        {
            From = from; To = to;
        }
    }

    // ---- combat ---------------------------------------------------------------------------

    /// <summary>What an attack declaration points at.</summary>
    public abstract class AttackTarget
    {
    }

    public sealed class UnitTarget : AttackTarget
    {
        public readonly CellRef Cell;
        public readonly int UnitId;

        public UnitTarget(CellRef cell, int unitId) { Cell = cell; UnitId = unitId; }
    }

    /// <summary>The defender's castle wall - a direct life strike when the column is open.</summary>
    public sealed class WallTarget : AttackTarget
    {
        public readonly Side Defender;

        public WallTarget(Side defender) { Defender = defender; }
    }

    public sealed class WorkerStackTarget : AttackTarget
    {
        public readonly Side Owner;
        public readonly WorkerZone Zone;

        public WorkerStackTarget(Side owner, WorkerZone zone) { Owner = owner; Zone = zone; }
    }

    /// <summary>
    /// One declaration. A joint attack is N declarations sharing a target, regrouped by target
    /// IDENTITY at resolve time - there is deliberately no AttackWithGroupCommand (spec 03 s6.2).
    /// </summary>
    public sealed class DeclareAttackCommand : CommandBase
    {
        public readonly CellRef Attacker;
        public readonly int UnitId;
        public readonly AttackTarget Target;

        /// <summary>
        /// The spec 03 s12 mirrored cadence: an ATTACKING AI declares its whole assault first
        /// and the defender answers blockers afterwards, one declaration at a time, seeing the
        /// complete attack. true skips the immediate per-declaration BlockerRequest; the
        /// resolver collects the answers at resolve time instead. The player-attacking flow
        /// (s6) keeps the default immediate parking.
        /// </summary>
        public readonly bool DeferBlockers;

        public DeclareAttackCommand(Side actor, CellRef attacker, int unitId, AttackTarget target,
                                    bool deferBlockers = false)
            : base(actor)
        {
            Attacker = attacker; UnitId = unitId; Target = target; DeferBlockers = deferBlockers;
        }
    }

    public sealed class ResolveCombatCommand : CommandBase
    {
        public ResolveCombatCommand(Side actor) : base(actor) { }
    }

    /// <summary>
    /// Take the whole assault back: every declaration is dropped and every attacker it tapped
    /// stands up again, as though the attacker had never pointed at anything.
    ///
    /// This is only sound because of the s12 deferred cadence. Until ResolveCombat a declaration
    /// asks the defender NOTHING - no BlockerRequest is parked, no blocker is spent, no damage is
    /// staged - so while the attacker is still choosing, the assault is an unanswered intent and
    /// withdrawing it costs the defender nothing they have paid for. The moment they have
    /// committed a blocker to one of them that stops being true, and the handler refuses.
    ///
    /// Note what this does NOT claim. In a duel the declaration is on the wire the instant it is
    /// made (the session applies and sends in one call), so the defender has seen it: their log
    /// carries the line and the attacker's figure lies down and stands back up again. That is a
    /// tell, and it is the accepted one - this game's multiplayer already holds both hands on both
    /// peers by design, and an attack that cannot be taken back is worse than a feint that can be
    /// read. What matters here is that nothing the defender has ANSWERED is undone.
    ///
    /// Everything else on this board is either reversible or obviously what it looks like, and
    /// picking an attacker was the one tap that silently spent a creature for the turn with no way
    /// back. Confirming an attack must be a decision; opening one must not be.
    /// </summary>
    public sealed class WithdrawAttackCommand : CommandBase
    {
        public WithdrawAttackCommand(Side actor) : base(actor) { }
    }

    // ---- the suspended-choice answer ------------------------------------------------------

    public sealed class RespondCommand : CommandBase
    {
        public readonly ChoiceResponse Response;

        public RespondCommand(Side actor, ChoiceResponse response) : base(actor)
        {
            Response = response;
        }
    }
}
