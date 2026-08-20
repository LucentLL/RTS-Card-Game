namespace SpawnRowDuel.Rules
{
    /// <summary>
    /// Why a command was refused. ALWAYS an enum, never display prose: the JS returned UI strings
    /// from its validators (upgradeWhy, drawBuild) which the interface then re-parsed, making the
    /// rules untestable and unlocalisable (spec 05 s18). The view owns the wording.
    /// </summary>
    public enum Rejection : ushort
    {
        None = 0,

        // pipeline gates
        NotYourTurn, WrongPhase, GameOver, ChoicePending, UnknownCommand,

        // identity
        NoSuchUnit, NotYourUnit, NotACreature, NotAStructure,

        // geometry
        CellOccupied, CellNotReal, CenterLaneForStructure, CenterFlankForCreature,
        NotAdjacent, MoveAlreadySpent, DestinationNotDeployable,

        // economy
        NotEnoughMana, NeedsOneMana, RowLacksWorkers, MissingPrereq, NoOpenSlot,

        // structures
        NotAnUpgradeTarget, NotUpgradeable, WrongRowForTier,

        // targeting
        NoLegalTarget, TargetNotEnemy, TargetKindIllegal,

        // hand plays
        HandIndexOutOfRange, WrongPlayMode, CoveredCardNotYours, CoveredCardHasNoBank,

        // turn machine
        ShortfallUnsettled, DeclarationsPending, NothingDeclared,

        // combat
        AttackerSick, AttackerTapped, AttackerIsWorker,

        // face-downs and choices
        ChargeUnderfunded, NotAFaceDown, NoPendingRequest, WrongResponseShape,

        // upkeep settlement
        NothingToPay,
    }

    public enum CommandStatus : byte
    {
        Applied = 0,
        Rejected = 1,

        /// <summary>Applied so far, but the engine parked on a PendingRequest and needs a
        /// RespondCommand before anything else may run.</summary>
        AwaitingChoice = 2,
    }

    public readonly struct CommandResult
    {
        public readonly CommandStatus Status;
        public readonly Rejection Rejection;

        public CommandResult(CommandStatus status, Rejection rejection)
        {
            Status = status; Rejection = rejection;
        }

        public bool Applied { get { return Status != CommandStatus.Rejected; } }

        public static readonly CommandResult Ok =
            new CommandResult(CommandStatus.Applied, Rejection.None);

        public static readonly CommandResult Waiting =
            new CommandResult(CommandStatus.AwaitingChoice, Rejection.None);

        public static CommandResult No(Rejection r)
        {
            return new CommandResult(CommandStatus.Rejected, r);
        }

        public override string ToString()
        {
            return Status == CommandStatus.Rejected ? "Rejected(" + Rejection + ")" : Status.ToString();
        }
    }
}
