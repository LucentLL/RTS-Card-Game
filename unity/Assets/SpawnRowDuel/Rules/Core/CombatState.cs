using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    public enum DeclarationKind : byte { Unit = 0, Wall = 1, WorkerStack = 2 }

    /// <summary>
    /// One attack declaration. The attacker is stored as UnitId AND coordinate - identity is
    /// authoritative at resolve time, which structurally fixes the JS bug where a declaration
    /// stored only a coordinate and resolved against whatever later moved into the cell
    /// (spec 03 s17 risk 2). Blockers bind to THIS declaration: the union-of-crossed-rows
    /// behaviour of a joint attack emerges per-declaration, never as a merged interval.
    /// </summary>
    public sealed class AttackDeclaration
    {
        public CellRef Attacker;
        public int AttackerUnitId;
        public DeclarationKind Kind;

        public CellRef TargetCell;      // Unit kind only
        public int TargetUnitId;        // Unit kind only
        public Side TargetSide;         // Wall / WorkerStack: whose wall / whose stack
        public WorkerZone TargetZone;   // WorkerStack only

        /// <summary>What the unit target WAS at declaration. The JS captured target OBJECTS at
        /// resolve start, so a target dying mid-resolution still granted the Scour credit and a
        /// razed building still re-sprang the defender's attack trap - this snapshot carries
        /// those stale-object semantics through our by-id resolution.</summary>
        public UnitKind TargetKind;

        /// <summary>Set at resolution entry: was the unit target alive when resolution began?
        /// (The JS's step-2 capture - a target already dead at resolve start does nothing.)</summary>
        public bool TargetLiveAtResolve;

        /// <summary>This declaration's blocker answer is still owed - collected at resolve
        /// start (the s12 mirrored cadence). Cleared when answered.</summary>
        public bool BlockersDeferred;

        public readonly List<UnitRef> Blockers = new List<UnitRef>();

        public AttackDeclaration Clone()
        {
            var d = new AttackDeclaration
            {
                Attacker = Attacker,
                AttackerUnitId = AttackerUnitId,
                Kind = Kind,
                TargetCell = TargetCell,
                TargetUnitId = TargetUnitId,
                TargetSide = TargetSide,
                TargetZone = TargetZone,
                TargetKind = TargetKind,
                TargetLiveAtResolve = TargetLiveAtResolve,
                BlockersDeferred = BlockersDeferred,
            };
            d.Blockers.AddRange(Blockers);
            return d;
        }
    }

    public enum CombatStage : byte
    {
        Idle = 0,
        BlockedPairFights = 1,
        UnblockedCreatureGroups = 2,
        UnblockedMisc = 3,
        ApplyWallDamage = 4,
        ScourStrikes = 5,

        /// <summary>Deferred blocker answers being collected before anything fights - the s12
        /// mirrored cadence, entered only when a declaration deferred its blocks.</summary>
        CollectBlocks = 6,
    }

    /// <summary>
    /// Authoritative, serializable combat state (the JS kept G.decls local and its resolver in
    /// an await chain, which is exactly why multiplayer had to bypass Combat v3 - spec 03 s14).
    /// The resolver's cursor lives HERE, so a snapshot taken mid-resolution - parked on an
    /// absorber or retaliation choice - is complete and resumable.
    /// </summary>
    public sealed class CombatState
    {
        public readonly List<AttackDeclaration> Declarations = new List<AttackDeclaration>();

        public CombatStage Stage;
        public int Cursor;                       // index into the current stage's working list
        public int SubCursor;                    // per-group latch (attack-trap sprung)
        public int AccumulatedWallDamage;

        /// <summary>Partitioned ONCE at resolve start, before any damage - a blocked attacker
        /// stays blocked even if it kills its whole gang (spec 03 s7 step 4).</summary>
        public readonly List<int> BlockedDeclIndices = new List<int>();
        public readonly List<int> OpenDeclIndices = new List<int>();

        /// <summary>The byT grouping, frozen at stage entry: per group one target unit id and a
        /// slice of declaration indices (flat list + offsets, insertion order).</summary>
        public readonly List<int> GroupTargetIds = new List<int>();
        public readonly List<int> GroupOffsets = new List<int>();      // start index per group
        public readonly List<int> GroupDeclIndices = new List<int>();  // flat

        /// <summary>Every live attacker at resolve start - discharge is cleared for exactly these.</summary>
        public readonly List<int> ResolutionAttackerIds = new List<int>();

        public readonly List<int> ScourHitUnitIds = new List<int>();

        /// <summary>Attackers Undertow hurled back to hand DURING this resolution that carry an
        /// on-hit keyword. The JS kept striking with the captured object; resolving by id needs
        /// this to find them again (spec 06 s6.2).</summary>
        public readonly List<int> BouncedScourIds = new List<int>();

        /// <summary>The answer slot a RespondCommand fills for the resolver to consume.</summary>
        public bool HasAnswer;
        public int AnsweredIndex;

        /// <summary>The attack-trigger response window's answer, waiting for the spring site
        /// that asked for it. ChosenTrap is UnitRef.None when the defender passed.</summary>
        public bool TrapAnswered;
        public UnitRef ChosenTrap;

        public bool HasDeclarations { get { return Declarations.Count > 0; } }

        public bool Resolving { get { return Stage != CombatStage.Idle; } }

        public void Clear()
        {
            Declarations.Clear();
            Stage = CombatStage.Idle;
            Cursor = 0;
            SubCursor = 0;
            AccumulatedWallDamage = 0;
            BlockedDeclIndices.Clear();
            OpenDeclIndices.Clear();
            GroupTargetIds.Clear();
            GroupOffsets.Clear();
            GroupDeclIndices.Clear();
            ResolutionAttackerIds.Clear();
            ScourHitUnitIds.Clear();
            BouncedScourIds.Clear();
            HasAnswer = false;
            AnsweredIndex = 0;
            TrapAnswered = false;
            ChosenTrap = UnitRef.None;
        }

        public CombatState Clone()
        {
            var c = new CombatState
            {
                Stage = Stage,
                Cursor = Cursor,
                SubCursor = SubCursor,
                AccumulatedWallDamage = AccumulatedWallDamage,
                HasAnswer = HasAnswer,
                AnsweredIndex = AnsweredIndex,
                TrapAnswered = TrapAnswered,
                ChosenTrap = ChosenTrap,
            };
            foreach (var d in Declarations) c.Declarations.Add(d.Clone());
            c.BlockedDeclIndices.AddRange(BlockedDeclIndices);
            c.OpenDeclIndices.AddRange(OpenDeclIndices);
            c.GroupTargetIds.AddRange(GroupTargetIds);
            c.GroupOffsets.AddRange(GroupOffsets);
            c.GroupDeclIndices.AddRange(GroupDeclIndices);
            c.ResolutionAttackerIds.AddRange(ResolutionAttackerIds);
            c.ScourHitUnitIds.AddRange(ScourHitUnitIds);
            c.BouncedScourIds.AddRange(BouncedScourIds);
            return c;
        }
    }
}
