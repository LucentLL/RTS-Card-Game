using System.Collections.Generic;

namespace SpawnRowDuel.Rules
{
    /// <summary>Which damage step a hit belongs to. First Strike is a real two-tier step in every
    /// damage engine, not a keyword hook (design 01 s5.2).</summary>
    public enum DamageTier : byte { Normal = 0, FirstStrike = 1, Trigger = 2 }

    public enum BounceCause : byte { Undertow = 0, Spell = 1 }

    public enum SettleKind : byte { Move = 0, Pay = 1, Sacrifice = 2 }

    /// <summary>
    /// What the rules emit for the view to animate. Derived from the 27 FX wrapper hook points in
    /// spec 09 s18 - the ONLY definition of every FX/SFX trigger in the game - plus the rules
    /// events the log needs. The contract: render(GameState) is always the complete truth at
    /// rest; react(GameEvent) is transient flair that never reads back into state.
    /// </summary>
    public abstract class GameEvent
    {
    }

    // ---- turn machine ---------------------------------------------------------------------

    public sealed class TurnStarted : GameEvent
    {
        public readonly Side Side;
        public readonly int TurnNumber;
        public TurnStarted(Side side, int turnNumber) { Side = side; TurnNumber = turnNumber; }
    }

    public sealed class PhaseChanged : GameEvent
    {
        public readonly TurnPhase From;
        public readonly TurnPhase To;
        public PhaseChanged(TurnPhase from, TurnPhase to) { From = from; To = to; }
    }

    /// <summary>The single clamped mana credit/debit record (design 01 s4.4).</summary>
    public sealed class ManaChanged : GameEvent
    {
        public readonly Side Side;
        public readonly int Before;
        public readonly int After;
        public ManaChanged(Side side, int before, int after) { Side = side; Before = before; After = after; }
    }

    /// <summary>A structure's upkeep yield (foundry/keep/citadel/forges).</summary>
    public sealed class ManaYielded : GameEvent
    {
        public readonly Side Side;
        public readonly int UnitId;
        public readonly int Amount;
        public ManaYielded(Side side, int unitId, int amount) { Side = side; UnitId = unitId; Amount = amount; }
    }

    /// <summary>End-of-turn drain: mana evaporates except what Mana Vaults hold.</summary>
    public sealed class ManaDrained : GameEvent
    {
        public readonly Side Side;
        public readonly int Kept;
        public readonly int Lost;
        public ManaDrained(Side side, int kept, int lost) { Side = side; Kept = kept; Lost = lost; }
    }

    public sealed class HarvestCollected : GameEvent
    {
        public readonly Side Side;
        public readonly WorkerZone Zone;
        public readonly int Amount;
        public HarvestCollected(Side side, WorkerZone zone, int amount) { Side = side; Zone = zone; Amount = amount; }
    }

    public sealed class TowerFired : GameEvent
    {
        public readonly int TowerId;
        public readonly int TargetId;
        public readonly int Amount;
        public TowerFired(int towerId, int targetId, int amount) { TowerId = towerId; TargetId = targetId; Amount = amount; }
    }

    public sealed class CreatureRevived : GameEvent
    {
        public readonly Side Side;
        public readonly CardId Card;
        public CreatureRevived(Side side, CardId card) { Side = side; Card = card; }
    }

    public sealed class WorkerShortfallSettled : GameEvent
    {
        public readonly Side Side;
        public readonly WorkerZone Zone;
        public readonly SettleKind How;
        public readonly int UnitId;
        public WorkerShortfallSettled(Side side, WorkerZone zone, SettleKind how, int unitId)
        { Side = side; Zone = zone; How = how; UnitId = unitId; }
    }

    // ---- board ----------------------------------------------------------------------------

    public sealed class CardDrawn : GameEvent
    {
        public readonly Side Side;
        public readonly CardId Card;
        public CardDrawn(Side side, CardId card) { Side = side; Card = card; }
    }

    public sealed class UnitSummoned : GameEvent
    {
        public readonly int UnitId;
        public readonly CellRef At;
        public UnitSummoned(int unitId, CellRef at) { UnitId = unitId; At = at; }
    }

    public sealed class UnitMoved : GameEvent
    {
        public readonly int UnitId;
        public readonly CellRef From;
        public readonly CellRef To;
        public readonly bool SpentTurn;      // true when this was the upkeep second move
        public UnitMoved(int unitId, CellRef from, CellRef to, bool spentTurn)
        { UnitId = unitId; From = from; To = to; SpentTurn = spentTurn; }
    }

    public sealed class StructureRaised : GameEvent
    {
        public readonly int UnitId;
        public readonly CellRef At;
        public readonly StructId Def;
        public StructureRaised(int unitId, CellRef at, StructId def) { UnitId = unitId; At = at; Def = def; }
    }

    public sealed class StructureUpgraded : GameEvent
    {
        public readonly int UnitId;
        public readonly StructId From;
        public readonly StructId To;
        public StructureUpgraded(int unitId, StructId from, StructId to) { UnitId = unitId; From = from; To = to; }
    }

    public sealed class CardFlipped : GameEvent
    {
        public readonly int UnitId;
        public readonly CellRef At;
        public readonly bool Sick;
        public CardFlipped(int unitId, CellRef at, bool sick) { UnitId = unitId; At = at; Sick = sick; }
    }

    /// <summary>A cocoon swelled but did not hatch. It re-sicks every upkeep.</summary>
    public sealed class ChrysalisGrew : GameEvent
    {
        public readonly int UnitId;
        public readonly int Count;
        public readonly int HatchAt;
        public ChrysalisGrew(int unitId, int count, int hatchAt)
        { UnitId = unitId; Count = count; HatchAt = hatchAt; }
    }

    /// <summary>In-place hatch: same unit id, new name and stats.</summary>
    public sealed class CreatureHatched : GameEvent
    {
        public readonly int UnitId;
        public readonly string NewName;
        public readonly int Attack;
        public readonly int Hp;
        public CreatureHatched(int unitId, string newName, int attack, int hp)
        { UnitId = unitId; NewName = newName ?? ""; Attack = attack; Hp = hp; }
    }

    /// <summary>An Overcharge creature banked another point toward its next discharge.</summary>
    public sealed class Overcharged : GameEvent
    {
        public readonly int UnitId;
        public readonly int Bank;
        public Overcharged(int unitId, int bank) { UnitId = unitId; Bank = bank; }
    }

    /// <summary>A Ward Lumen or a Reap Shade. Tokens have no registry card, so the event carries
    /// the statline the view needs rather than a CardId it could not resolve.</summary>
    public sealed class TokenSpawned : GameEvent
    {
        public readonly int UnitId;
        public readonly CellRef At;
        public readonly Side Owner;
        public readonly string Name;
        public readonly int Attack;
        public readonly int Hp;

        public TokenSpawned(int unitId, CellRef at, Side owner, string name, int attack, int hp)
        {
            UnitId = unitId; At = at; Owner = owner; Name = name ?? ""; Attack = attack; Hp = hp;
        }
    }

    // ---- combat ---------------------------------------------------------------------------

    public sealed class AttackDeclared : GameEvent
    {
        public readonly int AttackerId;
        public readonly AttackTarget Target;
        public readonly int DeclarationIndex;
        public AttackDeclared(int attackerId, AttackTarget target, int declarationIndex)
        { AttackerId = attackerId; Target = target; DeclarationIndex = declarationIndex; }
    }

    /// <summary>The assault was taken back before it was confirmed - nothing was struck and every
    /// attacker stood up again.</summary>
    public sealed class AttackWithdrawn : GameEvent
    {
        public readonly Side Attacker;
        public readonly int DeclarationCount;
        public AttackWithdrawn(Side attacker, int declarationCount)
        { Attacker = attacker; DeclarationCount = declarationCount; }
    }

    public sealed class BlockersAssigned : GameEvent
    {
        public readonly int DeclarationIndex;
        public readonly int[] BlockerIds;
        public BlockersAssigned(int declarationIndex, int[] blockerIds)
        { DeclarationIndex = declarationIndex; BlockerIds = blockerIds ?? new int[0]; }
    }

    public sealed class DamageApplied : GameEvent
    {
        public readonly int TargetId;
        public readonly int Amount;
        public readonly int SourceId;
        public readonly DamageTier Tier;
        public DamageApplied(int targetId, int amount, int sourceId, DamageTier tier)
        { TargetId = targetId; Amount = amount; SourceId = sourceId; Tier = tier; }
    }

    public sealed class UnitDestroyed : GameEvent
    {
        public readonly int UnitId;
        public readonly CellRef At;         // where it stood; pool workers report their zone row
        public readonly bool OnBoard;       // false for pool workers
        public readonly Side Owner;
        public readonly UnitKind Kind;
        public UnitDestroyed(int unitId, CellRef at, bool onBoard, Side owner, UnitKind kind)
        { UnitId = unitId; At = at; OnBoard = onBoard; Owner = owner; Kind = kind; }
    }

    public sealed class WallStruck : GameEvent
    {
        public readonly Side Defender;
        public readonly int Amount;
        public readonly int LifeRemaining;
        public WallStruck(Side defender, int amount, int lifeRemaining)
        { Defender = defender; Amount = amount; LifeRemaining = lifeRemaining; }
    }

    public sealed class UnitBounced : GameEvent
    {
        public readonly int UnitId;
        public readonly Side ToHand;
        public readonly BounceCause Cause;
        public UnitBounced(int unitId, Side toHand, BounceCause cause)
        { UnitId = unitId; ToHand = toHand; Cause = cause; }
    }

    public sealed class TrapSprung : GameEvent
    {
        public readonly Side Owner;
        public readonly CardId Card;
        public readonly CellRef At;
        public TrapSprung(Side owner, CardId card, CellRef at) { Owner = owner; Card = card; At = at; }
    }

    public sealed class SpellResolved : GameEvent
    {
        public readonly Side Caster;
        public readonly CardId Card;
        public readonly bool HasTarget;
        public readonly CellRef Target;
        public SpellResolved(Side caster, CardId card, bool hasTarget, CellRef target)
        { Caster = caster; Card = card; HasTarget = hasTarget; Target = target; }
    }

    public sealed class MatchEnded : GameEvent
    {
        public readonly MatchOutcome Outcome;
        public MatchEnded(MatchOutcome outcome) { Outcome = outcome; }
    }

    /// <summary>
    /// The transient event buffer. NOT part of GameState: a dropped event costs an animation,
    /// never a wrong board, because the view can always re-render from state.
    /// </summary>
    public sealed class EventSink
    {
        private readonly List<GameEvent> _events = new List<GameEvent>();

        public void Add(GameEvent e)
        {
            if (e != null) _events.Add(e);
        }

        public int Count { get { return _events.Count; } }

        public IReadOnlyList<GameEvent> Events { get { return _events; } }

        /// <summary>Hand the accumulated events to the consumer and reset.</summary>
        public List<GameEvent> Drain()
        {
            var copy = new List<GameEvent>(_events);
            _events.Clear();
            return copy;
        }

        public void Clear() { _events.Clear(); }
    }
}
