# Design 01 — The Pure C# Rules Core

**Status:** proposed design, ready to implement.
**Scope:** the headless, deterministic game engine for *Spawn Row Duel* — the assembly that knows
every rule, owns all authoritative state, and has **zero** knowledge of Unity, rendering, input,
audio, timers or the network.

**Inputs:** `docs/unity/spec/01_board_geometry_state.md` … `07_turn_machine_ai.md` (read in full).
Every rule cited below is traceable to those documents; this design does not invent rules.

**Locked project decisions this design obeys**

* Unity project lives at `unity/` inside this repo.
* PC / Steam first; mouse+keyboard primary.
* Multiplayer deferred, but the core must be host-authoritative-ready with no rewrite:
  deterministic, serializable, command-driven.
* Native C# rewrite. The rules engine is a pure library, unit-testable outside Unity.
* Cards become data (ScriptableObjects generated from `docs/unity/spec/cards.json`).

---

## 0. The five design commitments

Everything else follows from these. If a later decision conflicts with one of these, the commitment
wins.

1. **The core is a plain .NET class library that Unity happens to compile.**
   It is built and tested by `dotnet test` with no Unity installed. Unity's asmdef is a second,
   redundant compile of the same `.cs` files.
2. **State contains no object references — only integer ids, enums and value coordinates.**
   No cross-object pointers, no parent links, no cached arrays. This makes clone, serialize, hash,
   redact and mirror all trivially correct, and it structurally fixes the JS bug where an attack
   declaration stored a *coordinate* and resolved against whatever later occupied that cell
   (spec 03 §17 risk 2).
3. **Every mutation goes through exactly one command pipeline, and `Execute` always re-runs
   `Validate`.** There is no "the UI already checked it" path. The JS had three divergent validation
   copies (local, MP host, AI); spec 04 §19 says to take the host validators as canonical. We have
   one.
4. **No `async`, no `Task`, no timers, no wall clock, no floating point, no ambient RNG in the core.**
   Player and AI decisions that the JS `await`ed become serializable `PendingRequest` objects
   answered by a `ChoiceResponse` command. A headless test must be able to play 10 000 turns
   synchronously.
5. **Behavioural parity with the JS is proven before any rules change is made.**
   Every open question from the spec becomes a `RulesOptions` flag whose default reproduces the JS
   exactly. Differential testing against the live JS is a first-class test tier. Flags are then
   resolved and deleted — they are a migration device, not a permanent feature.

---

## 1. Assembly layout

### 1.1 Physical directory tree

```
unity/
├─ SpawnRowDuel.sln                       # dotnet-only solution (no Unity required)
├─ Assets/
│  └─ SpawnRowDuel/
│     ├─ Rules/                           # ── SpawnRowDuel.Rules.asmdef  (NO ENGINE REFS)
│     │  ├─ Core/                         #    GameState, PlayerState, Board, ids, RulesOptions
│     │  ├─ Geometry/                     #    Board geometry, CellRef, UnitRef, zone maps
│     │  ├─ Cards/                        #    catalog records + ICardCatalog (pure data)
│     │  ├─ Commands/                     #    ICommand, handlers, CommandProcessor, Rejection
│     │  ├─ Events/                       #    GameEvent hierarchy + EventBuffer
│     │  ├─ Effects/                      #    keyword / spell / structure / trap handlers
│     │  ├─ Economy/                      #    worker math, mana, harvest, vaults
│     │  ├─ Turn/                         #    phase machine, startTurn pipeline, upkeep settle
│     │  ├─ Combat/                       #    declarations, resolver step machine, legacy focus fire
│     │  ├─ Random/                       #    Pcg32, IRandomSource
│     │  ├─ Serialization/                #    codec, hashing, redaction, migrations
│     │  └─ Util/                         #    OrderedMap, StableSort, small collections
│     ├─ Ai/                              # ── SpawnRowDuel.Ai.asmdef      (NO ENGINE REFS)
│     │  ├─ ScriptedAiPolicy.cs           #    the verbatim foeTurn port
│     │  ├─ AiTuning.cs
│     │  └─ Heuristics/
│     ├─ Testing/                         # ── SpawnRowDuel.Testing.asmdef (NO ENGINE REFS)
│     │  ├─ Scenario.cs                   #    board-building DSL used by every test tier
│     │  ├─ ScriptRunner.cs               #    command-script player + hash trace
│     │  └─ Fixtures/
│     ├─ Content/                         # ── SpawnRowDuel.Content.asmdef (Unity; SO wrappers)
│     ├─ View/                            # ── SpawnRowDuel.View.asmdef    (Unity; later phase)
│     └─ Editor/                          # ── SpawnRowDuel.Editor.asmdef  (card importer)
├─ Tests/
│  └─ EditMode/
│     └─ Rules/                           # ── SpawnRowDuel.Rules.Tests.asmdef (Unity Test Runner)
└─ Headless/
   ├─ SpawnRowDuel.Rules.csproj           # globs ../Assets/SpawnRowDuel/Rules/**/*.cs
   ├─ SpawnRowDuel.Ai.csproj
   ├─ SpawnRowDuel.Testing.csproj
   ├─ SpawnRowDuel.Rules.Tests.csproj     # NUnit; `dotnet test` — the authoritative gate
   ├─ SpawnRowDuel.Rules.DiffTests.csproj # differential tests against the JS (opt-in)
   └─ BannedSymbols.txt
```

**One source of truth, two build systems.** The `.cs` files live under `Assets/` so Unity sees them
natively (no symlinks, no Packages indirection, no copy step). The headless `.csproj` files glob the
same folders:

```xml
<!-- unity/Headless/SpawnRowDuel.Rules.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>   <!-- matches Unity 6 / .NET Standard 2.1 -->
    <LangVersion>9.0</LangVersion>                      <!-- Unity 6 C# level; records, patterns -->
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <RootNamespace>SpawnRowDuel.Rules</RootNamespace>
    <AssemblyName>SpawnRowDuel.Rules</AssemblyName>
    <Deterministic>true</Deterministic>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="..\Assets\SpawnRowDuel\Rules\**\*.cs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="3.*" PrivateAssets="all" />
    <AdditionalFiles Include="BannedSymbols.txt" />
  </ItemGroup>
</Project>
```

`netstandard2.1` is deliberate: it is the intersection Unity 6 guarantees, and it makes it impossible
to accidentally take a dependency on a .NET 8-only API that Unity's runtime lacks. `LangVersion 9.0`
matches Unity 6's Roslyn: records, `init`, pattern matching and `readonly record struct` are all
available; `required` members and file-scoped types are not — do not use them.

### 1.2 Assembly definitions

```jsonc
// Assets/SpawnRowDuel/Rules/SpawnRowDuel.Rules.asmdef
{
  "name": "SpawnRowDuel.Rules",
  "rootNamespace": "SpawnRowDuel.Rules",
  "references": [],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,      // do not auto-link precompiled DLLs
  "precompiledReferences": [],
  "autoReferenced": false,         // Assembly-CSharp must reference us EXPLICITLY
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": true       // ★ THE GUARANTEE: UnityEngine is not on the compile path
}
```

```jsonc
// Assets/SpawnRowDuel/Ai/SpawnRowDuel.Ai.asmdef
{ "name": "SpawnRowDuel.Ai", "rootNamespace": "SpawnRowDuel.Ai",
  "references": ["SpawnRowDuel.Rules"],
  "autoReferenced": false, "overrideReferences": true, "precompiledReferences": [],
  "noEngineReferences": true }
```

```jsonc
// Assets/SpawnRowDuel/Testing/SpawnRowDuel.Testing.asmdef
{ "name": "SpawnRowDuel.Testing", "rootNamespace": "SpawnRowDuel.Testing",
  "references": ["SpawnRowDuel.Rules", "SpawnRowDuel.Ai"],
  "autoReferenced": false, "overrideReferences": true, "precompiledReferences": [],
  "noEngineReferences": true }
```

```jsonc
// Tests/EditMode/Rules/SpawnRowDuel.Rules.Tests.asmdef
{ "name": "SpawnRowDuel.Rules.Tests", "rootNamespace": "SpawnRowDuel.Rules.Tests",
  "references": ["SpawnRowDuel.Rules", "SpawnRowDuel.Ai", "SpawnRowDuel.Testing",
                 "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll"],
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "includePlatforms": ["Editor"],
  "autoReferenced": false }
```

The Unity test assembly *must* reference UnityEngine (the Test Framework requires it). That is fine —
it is a consumer, not the core. The `noEngineReferences: true` on `SpawnRowDuel.Rules` /
`SpawnRowDuel.Ai` / `SpawnRowDuel.Testing` is what makes `using UnityEngine;` a **compile error**
inside them.

`Content`, `View` and `Editor` are Unity assemblies that reference `SpawnRowDuel.Rules`. The
dependency arrow only ever points *into* the core:

```
Editor ──▶ Content ──▶ Rules ◀── Ai ◀── Testing
              │          ▲                 ▲
   View ──────┴──────────┘                 │
   Rules.Tests ──────────────────────────--┘
```

### 1.3 Keeping the no-Unity guarantee from eroding

Four independent gates. Any one of them alone rots; all four together do not.

| # | Gate | What it catches | Where it runs |
|---|---|---|---|
| 1 | `noEngineReferences: true` on the core asmdefs | `using UnityEngine`, `MonoBehaviour`, `Vector3`, `Debug.Log`, `[SerializeField]` | Unity compile, every domain reload |
| 2 | `dotnet build` of `Headless/SpawnRowDuel.Rules.csproj` in CI | the same, plus anything Unity-only that slipped in via a package | CI, pre-merge |
| 3 | **BannedApiAnalyzers** with `BannedSymbols.txt`, `TreatWarningsAsErrors` | nondeterminism, not just Unity | both compiles |
| 4 | An architecture test that reflects over the built assembly | transitive references, `async` state machines, `float` fields in state | `dotnet test` |

`unity/Headless/BannedSymbols.txt` — this is the determinism contract expressed as a build error:

```
T:System.Random;                      Use IRandomSource / Pcg32 threaded through GameState.
T:System.DateTime;                    The core has no wall clock. Timing belongs to the view.
T:System.DateTimeOffset;              Same.
T:System.Diagnostics.Stopwatch;       Same.
T:System.Threading.Tasks.Task;        No async in the core. Use PendingRequest.
T:System.Threading.Thread;            Single-threaded by contract.
T:System.Guid;                        Non-deterministic ids. Use GameState.NextUid.
M:System.Object.GetHashCode;          Reference hash codes leak allocation order into iteration.
T:System.Collections.Generic.HashSet`1;   Iteration order is not specified. Use OrderedSet.
M:System.Collections.Generic.List`1.Sort; Unstable sort. Use Sorting.StableSort with a total order.
M:System.Linq.Enumerable.ToDictionary``3; Dictionary iteration order is observable here. Use OrderedMap.
```

`Dictionary<K,V>` itself is *not* banned — it is fine for by-key lookup (command handler registry,
card catalog). It is banned only as an *iteration* source, which the analyzer cannot see; the
architecture test below covers that by asserting no `foreach` over a `Dictionary` field inside
`Rules/Combat` and `Rules/Turn`. In practice we simply never store one there.

```csharp
// Tests/.../ArchitectureTests.cs  — runs headless, no Unity
[Test]
public void RulesAssembly_ReferencesNothingButBcl()
{
    var asm = typeof(GameState).Assembly;
    var refs = asm.GetReferencedAssemblies().Select(a => a.Name).ToArray();
    Assert.That(refs, Is.SubsetOf(new[] { "netstandard", "System.Runtime", "mscorlib" }),
        "The rules core grew a dependency: " + string.Join(", ", refs));
}

[Test]
public void RulesAssembly_ContainsNoAsyncStateMachines()
{
    var offenders = typeof(GameState).Assembly.GetTypes()
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                      BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        .Where(m => m.GetCustomAttributes(typeof(AsyncStateMachineAttribute), false).Any())
        .Select(m => m.DeclaringType!.Name + "." + m.Name).ToArray();
    Assert.That(offenders, Is.Empty);
}

[Test]
public void SerializedState_ContainsNoFloatingPoint()
{
    // Walks GameState's field graph; fails on float/double/decimal anywhere reachable.
    Assert.That(StateShapeInspector.FloatingPointFields(typeof(GameState)), Is.Empty);
}
```

Finally, a one-line CI job makes gate 2 unmissable:

```yaml
- run: dotnet test unity/Headless/SpawnRowDuel.Rules.Tests.csproj -c Release
```

If that command needs Unity, the design has already failed.

---

## 2. The state model

### 2.1 The shape decision, and why

Six decisions, each with the spec risk it neutralises.

| Decision | Rationale | Spec risk closed |
|---|---|---|
| **Board is one flat `BoardObject?[35]` indexed `row*7 + col`** — never per-player row collections | `G.P.you.front` is a *positional* row that legally holds enemy raiders. Any `Player.FrontRow` model mis-attributes upkeep, combat and cleanup. | 01 §4.1, 03 §1.3, 04 §2.3 |
| **Ownership lives only on `BoardObject.Owner`** | Same. Every "my stuff" query filters on it; nothing infers ownership from an array. | 01 §15.2 risk 2 |
| **Board objects are `class` (reference types)** | They are mutated constantly (hp, flags), identity matters (a blocker is *that* creature), and they are polymorphic across four kinds. Structs would copy-on-assign and silently lose damage. | — |
| **Coordinates, refs and ids are `readonly struct`** | `CellRef`, `PoolRef`, `UnitRef`, `Rng` are copied thousands of times per AI search ply. Allocation-free. | 07 §17.3 |
| **State holds *no* object references — only ids + coordinates** | Clone/serialize/redact/mirror become mechanical. Declarations carry `UnitId` so a moved-away attacker is detected instead of silently resolving against a different unit. | 03 §17 risk 2, 01 §15.2 risk 3 |
| **`GameState` is authoritative and serializable; `InteractionState` (selection, hover, drag) does not exist in the core at all** | The JS conflates them in `G` and the MP layer has to null seven fields on every adopt. | 01 §6.1, §13.1 |

### 2.2 Identity and coordinates

```csharp
namespace SpawnRowDuel.Rules;

public enum Side : byte { You = 0, Foe = 1 }

public enum RowKey : byte { FoeBack = 0, FoeFront = 1, Center = 2, YouFront = 3, YouBack = 4 }

/// Owner-relative half-board addressing ("which") used by deployment, build and worker code.
public enum SlotName : byte { Back = 0, Front = 1, Center = 2 }

/// Economy addressing. Enumeration order IS the settle order (spec 02 §7.1). Raid has no pool.
public enum WorkerZone : byte { Back = 0, Front = 1, Center = 2, Raid = 3 }

public enum TurnPhase : byte { Upkeep = 0, Draw = 1, Action = 2, End = 3 }

/// A board cell. Col is 0..6.
public readonly record struct CellRef(RowKey Row, byte Col)
{
    public int Index => (int)Row * Board.Columns + Col;
    public static CellRef FromIndex(int i) => new((RowKey)(i / Board.Columns), (byte)(i % Board.Columns));
    public override string ToString() => $"{Row}[{Col}]";
}

/// A worker-pool slot. Mirrors the MP wire shape {po, pw, pi} (spec 01 §10).
public readonly record struct PoolRef(Side Owner, WorkerZone Zone, byte Index);

public enum UnitRefKind : byte { None = 0, Cell = 1, Pool = 2 }

/// Discriminated union over CellRef | PoolRef, plus the unit id for identity validation.
/// Replaces the JS's duck-typed {key,i,c} vs {key,c} blocker refs (spec 01 §10, spec 03 §4.2).
public readonly record struct UnitRef
{
    public readonly UnitRefKind Kind;
    public readonly int UnitId;      // 0 == unknown; ALWAYS set when the ref names a live unit
    private readonly byte _a, _b, _c;

    public static UnitRef Cell(CellRef c, int unitId) => new(UnitRefKind.Cell, unitId, (byte)c.Row, c.Col, 0);
    public static UnitRef Pool(PoolRef p, int unitId) => new(UnitRefKind.Pool, unitId, (byte)p.Owner, (byte)p.Zone, p.Index);

    public CellRef AsCell  { get { Require(UnitRefKind.Cell); return new CellRef((RowKey)_a, _b); } }
    public PoolRef AsPool  { get { Require(UnitRefKind.Pool); return new PoolRef((Side)_a, (WorkerZone)_b, _c); } }
    // ctor + Require elided
}
```

`UnitId` on every ref is the structural fix for spec 03 risk 2 and risk 16 in one stroke: a
declaration or blocker reference resolves as "the unit with this id, currently at this coordinate",
and a mismatch is a detectable, testable condition rather than silent misbehaviour.

### 2.3 Geometry

Pure static, no state, fully table-driven, unit-testable in isolation. Owner-agnostic adjacency
(spec 04 §4.2 proves the two JS move chains are exact reverses).

```csharp
public static class Board
{
    public const int Columns = 7;             // SLOTS
    public const int Rows    = 5;             // ROWS.Length
    public const int Cells   = Rows * Columns;
    public const int BaseColumn = 3;          // BASE_COL — FX only, never a rule
    public const int FoeWallRow = -1;         // virtual
    public const int YouWallRow = Rows;       // == 5, virtual

    public static readonly RowKey[] AllRows =
        { RowKey.FoeBack, RowKey.FoeFront, RowKey.Center, RowKey.YouFront, RowKey.YouBack };

    public static bool IsLane(int col) => col == 1 || col == 3 || col == 5;

    /// A real, creature-standable cell. 31 of the 35 cells qualify.
    public static bool IsRealSlot(RowKey row, int col)
        => col >= 0 && col < Columns && (row != RowKey.Center || IsLane(col));

    /// centerSlotOK — structures take center flanks, creatures take center lanes.
    public static bool CenterSlotOk(RowKey row, int col, bool isStructure)
        => row != RowKey.Center || (isStructure ? !IsLane(col) : IsLane(col));

    public static RowKey RowFor(Side owner, SlotName which) => which switch {
        SlotName.Center => RowKey.Center,
        SlotName.Front  => owner == Side.You ? RowKey.YouFront : RowKey.FoeFront,
        _               => owner == Side.You ? RowKey.YouBack  : RowKey.FoeBack,
    };

    public static SlotName WhichOf(RowKey row) => row switch {
        RowKey.Center                        => SlotName.Center,
        RowKey.YouFront or RowKey.FoeFront   => SlotName.Front,
        _                                    => SlotName.Back,
    };

    public static WorkerZone ZoneForRow(Side owner, RowKey row) => row switch {
        RowKey.Center => WorkerZone.Center,
        _ when row == RowFor(owner, SlotName.Back)  => WorkerZone.Back,
        _ when row == RowFor(owner, SlotName.Front) => WorkerZone.Front,
        _                                           => WorkerZone.Raid,
    };

    /// CANONICAL and PLURAL. `zoneKey` (singular) in the JS disagrees for Raid; that footgun does
    /// not exist here (spec 01 §8.1, spec 04 §5.6).
    public static ReadOnlySpan<RowKey> RowsOfZone(Side owner, WorkerZone zone) { /* table lookup */ }

    /// rowsCrossedInto: the half-open interval (a, t] in travel order, clipped to real rows.
    /// Same row => empty => uninterposable point-blank duel (spec 03 §4.1).
    public static int RowsCrossedInto(int attackerRow, int targetRow, Span<RowKey> into)
    {
        if (attackerRow == targetRow) return 0;
        int step = targetRow > attackerRow ? 1 : -1, n = 0;
        for (int r = attackerRow + step; r != targetRow + step; r += step)
            if (r >= 0 && r < Rows) into[n++] = (RowKey)r;
        return n;
    }

    /// Owner-agnostic. One step in any of 8 directions into a real slot.
    public static bool Adjacent(CellRef a, CellRef b)
        => IsRealSlot(a.Row, a.Col) && IsRealSlot(b.Row, b.Col) && a != b
        && Math.Abs((int)a.Row - (int)b.Row) <= 1
        && Math.Abs(a.Col - b.Col) <= 1;

    /// CANONICAL enumeration order, pinned now so no future rule can be order-ambiguous
    /// (spec 04 §23 determinism note): ascending RowKey, then ascending Col.
    public static int Neighbours(CellRef from, Span<CellRef> into) { /* … */ }
}
```

Two deliberate deletions, per the specs: `colReach` is not ported (dead, and columns never matter in
combat), and `moveChainOf` is not ported (the owner parameter is provably redundant).

### 2.4 Board objects

```csharp
public enum UnitKind : byte { Creature = 0, Building = 1, Charge = 2, Trap = 3 }

public abstract class BoardObject
{
    public int      Id;          // from GameState.NextUid — unique, serialized, never reused
    public Side     Owner;       // THE ownership authority
    public UnitKind Kind;
    public Element  Color;       // resolved at construction (never null; falls back to owner's primary)
    public int      Bank;        // banked ◆ riding on the card
    public abstract BoardObject Clone();
}

public sealed class CreatureUnit : BoardObject
{
    public CardId Card;                        // stable id == the JS `nm`
    public string Name = "";                   // denormalised for logs; catalog is authoritative
    public int  Attack, Hp, MaxHp, Cost, Upkeep;
    public bool FirstStrike, Entrench, IsWorker, IsToken;
    public Keyword Keyword;
    public int  Detonate, Reap, WardHp, Grow, Hatch;
    public HatchFormId Into;                   // catalog key, not an object reference
    public int  ChrysalisCount;                // cnt   — persists across turns
    public int  OverchargeBank;                // oc    — persists across turns
    public int  DischargeBonus;                // _dis  — transient, cleared every resolution
    public Tribe Tribe; public Subtype Subtype;

    // per-turn flags, all cleared at the OWNER's own BeginTurn (spec 03 §2.1)
    public bool Sick, Tapped, Moved, MovedTwice, PaidUpkeep, HasBlocked;

    public int EffectiveAttack => Attack + DischargeBonus;   // effA
}

public sealed class StructureUnit : BoardObject
{
    public StructId? DefId;                    // null == legacy hand-built: NEVER upgradeable
    public int Hp, MaxHp, Cost, Value, Support; // Support may be NEGATIVE (Cannon Tower −2)
    public StructEffect Effect;
    public bool IsCommandCenter;               // always false today; keeps the guard sites alive
}

public sealed class ChargeUnit : BoardObject   // face-down creature or structure
{
    public SlotName  SetIn;
    public bool      IsStructure;              // ctype
    public CardSnapshot Card;                  // frozen value type, see §2.7
    public int Invested;                       // inv — starts at 1 (the ◆1 set cost)
    public int SetTurn;
}

public sealed class TrapUnit : BoardObject
{
    public SlotName SetIn;
    public CardId   Card;
    public SpellEffect Effect; public int Value; public TrapTrigger Trigger;
    public int SetTurn;
    public bool IsArmed(int turnNo) => turnNo > SetTurn;   // never on the turn it was set
}
```

Note what is **absent**: `art`, `ic`, `desc`, `laid`, `cc` on creatures, `ward`, `target`. All of
those are presentation or dead fields the specs flagged (01 §15.1, 06 §11.5). `laid` in particular is
derived — the core exposes `CanActNow(state, cell)` as a *view-model query* and never stores a pose.

### 2.5 Worker pools

Workers are the one genuinely awkward part of the JS model and the spec is explicit about why
(spec 02 §6.4, spec 05 §5.2): the worker *figure* is derived every time it is read, but the worker
*pool* is a materialised list whose members carry `sick` / `tapped` / damage that must survive
resyncs. `cleanup()` deliberately does **not** resync, so a razed structure leaves harvestable
workers standing until the next `syncWorkers`. That is observable behaviour.

We model exactly that, and make the divergence explicit rather than incidental:

```csharp
public sealed class WorkerPool               // one per (Side, {Back, Front, Center}). There is no Raid pool.
{
    public readonly List<CreatureUnit> Members = new();

    /// syncWorkers: shrink by popping the TAIL (no grave record); grow by pushing SICK bodies.
    /// Called only at the enumerated sites in §4.3 — NOT from the death sweep.
    public void Resync(int target, Func<CreatureUnit> makeWorker) { /* … */ }

    public void Ready()   // readyWorkers — only at turn start
    { foreach (var m in Members) { m.Sick = false; m.Tapped = false; m.Moved = false; } }
}
```

`WorkerMath.RowWorkers(state, side, zone)` is a pure function, recomputed on demand, exactly as in
the JS — including the recurring `+CCS[cc].wk` in the Back zone and the always-zero `villager` term
(which stays in the formula, driven by data, so flipping `RulesOptions.VillagerVal` later needs no
code change).

### 2.6 Players and the root state

```csharp
public sealed class PlayerState
{
    public Element     PrimaryColor;
    public CommanderId Commander;
    public int Life, Mana;

    public readonly List<HandCard>    Hand  = new();
    public readonly List<DeckCard>    Deck  = new();   // draw from the END (Pop)
    public readonly List<GraveRecord> Grave = new();

    public readonly WorkerPool[] Workers = { new(), new(), new() };  // Back, Front, Center
    public readonly int[] UpkeepPaid = new int[4];                   // by WorkerZone; reset each BeginTurn

    public PlayerState Clone() { /* hand-written */ }
}

public sealed class GameState
{
    public const int SchemaVersion = 1;

    // ── identity / bookkeeping ──
    public int  NextUid = 1;             // MUST be serialized (spec 01 §13.1)
    public RulesOptions Options;         // frozen at match creation; part of the state hash
    public Rng  Random;                  // seeded PCG32; stream position is state

    // ── turn machine ──
    public Side      Turn;
    public int       TurnNumber = 1;     // PLY counter — one per half-turn
    public TurnPhase Phase = TurnPhase.Upkeep;
    public bool      IsOver;
    public MatchOutcome Outcome = MatchOutcome.InProgress;

    // ── board: ONE positional array. Never per-player rows. ──
    private readonly BoardObject?[] _cells = new BoardObject?[Board.Cells];

    public readonly PlayerState[] Players = { new(), new() };
    public PlayerState P(Side s) => Players[(int)s];

    // ── combat: authoritative and serialized, unlike the JS's local-only G.decls ──
    public readonly CombatState Combat = new();

    // ── the suspended-choice machine (§6) ──
    public PendingRequest? Pending;

    public BoardObject? At(CellRef c)          => _cells[c.Index];
    public void         Put(CellRef c, BoardObject? o) => _cells[c.Index] = o;

    public GameState Clone() { /* hand-written deep clone, §2.8 */ }
}
```

**Explicitly not in `GameState`:** `Busy`, `Selection`, `AttackGroup`, `MoveFrom`, `MoveMana`,
`CardMenu`, `PendingBuild`, hints, log HTML. Those live in the view. The core's only concession to
"is input allowed" is a derived predicate:

```csharp
public static bool IsInteractive(GameState s, Side side)
    => !s.IsOver && s.Pending is null && s.Turn == side
    && (s.Phase == TurnPhase.Upkeep || s.Phase == TurnPhase.Draw || s.Phase == TurnPhase.Action);
```

This replaces the JS accident where `G.phase` stayed at `'end'` for the entire AI turn and *that* was
what made the board inert (spec 02 §4.4, spec 07 §3.2). The AI now runs the real phase machine, and
input gating is an explicit predicate.

Also removed from the model, per the specs: `cmana`, `firstExtract`, `villagerUsed`, `powerMode`,
`deficit`, `minSel`, `G.upkeep` (derived from `Phase`).

### 2.7 Cards: catalog data vs instance data

The catalog is **immutable, shared, and not part of state**. Instances copy only what mutates.

```csharp
public readonly record struct CardId(string Value);            // == the JS `nm`
public readonly record struct DeckKey(Element? Color, string Name);  // "<color|neutral>|<nm>"

public sealed record CreatureCard(CardId Id, string Name, Element Element,
    int Cost, int Attack, int Health, int Upkeep,
    bool FirstStrike, bool Entrench, Keyword Keyword,
    int Detonate, int Reap, int WardHp, int Grow, int Hatch,
    HatchForm? Into, Tribe Tribe, Subtype Subtype);

public sealed record SpellCard(CardId Id, string Name, int Cost, bool IsTrap,
    SpellEffect Effect, int Value, TrapTrigger Trigger);

public sealed record StructureDef(StructId Id, string DisplayName, int Cost, int MaxHp,
    StructEffect Effect, int EffectValue, int Support, Element? Element,
    StructId[] Prereqs, StructId[] UpgradeTargets, StructId? UpgradedFrom, RowGate RowGate);

public sealed record CommanderDef(CommanderId Id, string Name, int Hp, int Workers, Element[] Colors);

public interface ICardCatalog
{
    CreatureCard Creature(CardId id);
    SpellCard    Spell(CardId id);
    CommanderDef Commander(CommanderId id);
    /// resolveStruct: forge / grandforge are synthesised per element; every other id is a singleton.
    StructureDef Structure(StructId id, Element? color);
    IReadOnlyList<StructureDef> BuildList(CommanderId cc);         // ORDER IS THE AI'S PRIORITY
    IReadOnlyList<StructId>     Lineage(StructureUnit b);          // bidLineage, 8-hop guard
    bool TryByDeckKey(DeckKey key, out CardId id);
}
```

The catalog is injected into the engine, so tests can supply a tiny fixture catalog instead of all 78
cards. The Unity side wraps `ScriptableObject` assets and hands the core plain records — the SO type
never crosses the assembly boundary.

`CardSnapshot` (used by face-down charges) is a small readonly struct copying exactly the fields the
JS snapshot copies — **plus `Color`**, which the JS drops, causing a flipped off-colour creature to
inherit the player's element (spec 04 §13.2 `[BUG]`). This is a `RulesOptions.FaceDownKeepsColor`
flag defaulting to `false` (JS-faithful) so parity testing passes, with a one-line flip once design
rules on it.

`Forge` and `GrandForge` remain single `StructId` values with an `Element` parameter — flattening
them into 18 ids would break `prereq:['forge']` matching (spec 05 §2.3).

### 2.8 Clone, serialize, hash: the mechanism and the justification

Four operations, one traversal.

**Clone — hand-written `Clone()` per type.** Not reflection, not serialize-then-deserialize.
Rationale: AI search (and, later, "what if I attack here" previews) clones the whole state thousands
of times per second; a reflective or round-trip clone is 50–100× slower and, worse, would make clone
correctness depend on serialization correctness. Hand-written clones are ~200 lines total and are
covered by a round-trip property test (§9.5). The no-object-references commitment (§0.2) makes them
purely mechanical: there is no graph to fix up.

**Serialize — one traversal, two encodings.** A single `StateCodec.Write(GameState, IStateWriter)`
walks the state in a fixed, declared order. Two writers implement the interface:

```csharp
public interface IStateWriter
{
    void BeginObject(string name);  void EndObject();
    void BeginArray(string name, int count); void EndArray();
    void Write(string name, int v);
    void Write(string name, bool v);
    void Write(string name, string? v);
    void WriteEnum<T>(string name, T v) where T : struct, Enum;
    void WriteNull(string name);
}

public sealed class BinaryStateWriter : IStateWriter { /* ignores names; varint ints; LE */ }
public sealed class CanonicalJsonStateWriter : IStateWriter { /* uses names; stable key order */ }

public static class StateCodec
{
    public const int Version = GameState.SchemaVersion;
    public static void  Write(GameState s, IStateWriter w);
    public static GameState Read(IStateReader r);                 // r.Version drives migrations
    public static byte[]    ToBytes(GameState s, SerializationView view);
    public static string    ToCanonicalJson(GameState s, SerializationView view);
    public static ulong     Hash(GameState s) => Fnv1a64(ToBytes(s, SerializationView.Full));
}

public readonly record struct SerializationView(bool Full, Side Viewer)
{
    public static SerializationView Full  => new(true, default);
    public static SerializationView For(Side s) => new(false, s);
}
```

Why a hand-written codec rather than the obvious alternatives:

| Option | Rejected because |
|---|---|
| `UnityEngine.JsonUtility` | Unity type — would break gate 1. No polymorphism, no nullables, no dictionaries. |
| `BinaryFormatter` | Obsolete, unsafe, and not deterministic across runtimes. |
| Newtonsoft / `System.Text.Json` reflection | Field *order* and default-value elision are not contractually stable across versions, so the byte stream — and therefore the replay hash — could change under a package bump. Also drags a package into a zero-dependency assembly. |
| Protobuf / FlatBuffers / MessagePack | Adds a schema toolchain and a codegen step for ~40 fields. Buys wire compactness we do not need yet, costs a build dependency in the one assembly that must have none. |
| **Hand-written codec (chosen)** | Deterministic by construction; the schema *is* the traversal; migrations are `if (r.Version < 2)` branches at the exact field; the JSON writer gives free human-diffable dumps for differential testing; zero dependencies. |

The three consumers get exactly what they need:

* **Save games** — `ToBytes(state, Full)` plus a 16-byte header (`magic`, `SchemaVersion`,
  `RulesOptions` hash). Versioned migration hooks from day one; the JS campaign save simply *deletes*
  the old key on a schema change (spec 08 risk) and we are not repeating that.
* **State hash for tests** — `Hash(state)`, a 64-bit FNV-1a over the canonical bytes. This single
  number is the workhorse of the regression suite (§9.3).
* **Netcode (deferred)** — `ToBytes(state, For(side))`. The redacted projection is designed in *now*:
  the JS host snapshot ships **both** players' full hands and decks (spec 01 §13.1, flagged as a
  cheat vector). `SerializationView.For(side)` writes the opponent's hand as a count, the opponent's
  deck as a count, and face-down `ChargeUnit`/`TrapUnit` cards as `null` with their `Invested`/
  `SetTurn` intact. The redaction rules live in one place — `StateCodec` — so they cannot drift.

**Mirroring** (guest perspective) is a separate, explicit transform: `StateMirror.Mirror(state)` maps
`RowKey r -> (RowKey)(4 - r)`, leaves columns alone, and flips each object's `Owner` **independently**.
It never re-stamps ownership from the containing array — that JS bug silently converted a foe raider
in your front row into your own unit (spec 01 §13.2).

---

## 3. Commands, validation and events

### 3.1 The pipeline

```
ICommand ──▶ CommandProcessor.Validate ──▶ Rejection?          (pure; no mutation)
                    │ Ok
                    ▼
             CommandProcessor.Execute  ──▶ mutates GameState
                    │                  ──▶ appends GameEvent[]
                    │                  ──▶ may set GameState.Pending
                    ▼
             CommandResult { Status, Rejection, EventCount }
```

**`Execute` always calls `Validate` first, internally.** There is no "trusted" entry point. This is
the single most important structural fix: the JS had a permissive local path, a stricter MP host path
and a third AI path, and the specs repeatedly note the local path relies on the UI having filtered
cases (spec 04 §19, spec 02 §13). The host validators are the specification; we have exactly one
implementation of them, used by the human UI, by the AI, and later by the network host.

```csharp
public interface ICommand { Side Actor { get; } }

public enum CommandStatus : byte { Applied, Rejected, AwaitingChoice }

public enum Rejection : ushort
{
    None = 0,
    NotYourTurn, WrongPhase, GameOver, ChoicePending,
    NoSuchUnit, NotYourUnit, NotACreature, NotAStructure,
    CellOccupied, CellNotReal, CenterLaneForStructure, CenterFlankForCreature,
    NotAdjacent, MoveAlreadySpent, DestinationNotDeployable,
    NotEnoughMana, NeedsOneMana, RowLacksWorkers, MissingPrereq, NoOpenSlot,
    NotAnUpgradeTarget, NotUpgradeable, WrongRowForTier,
    NoLegalTarget, TargetNotEnemy, TargetKindIllegal,
    HandIndexOutOfRange, WrongPlayMode, CoveredCardNotYours, CoveredCardHasNoBank,
    ShortfallUnsettled, DeclarationsPending, NothingDeclared,
    AttackerSick, AttackerTapped, AttackerIsWorker,
    ChargeUnderfunded, NotAFaceDown, NoPendingRequest, WrongResponseShape,
}

public readonly record struct CommandResult(CommandStatus Status, Rejection Rejection)
{
    public static readonly CommandResult Ok = new(CommandStatus.Applied, Rejection.None);
    public static CommandResult No(Rejection r) => new(CommandStatus.Rejected, r);
    public static readonly CommandResult Waiting = new(CommandStatus.AwaitingChoice, Rejection.None);
}
```

`Rejection` is an enum, never a string. The JS returns display prose from `upgradeWhy` /
`drawBuild`, which the UI then parses or re-renders (spec 05 §18). Localisation and testing both need
the enum.

### 3.2 The full command set

One record per player-expressible intent. These are exactly the operations the MP layer already
models (spec 02 §13, spec 04 §19, spec 05 §15), which is the proof that the set is complete.

```csharp
// ── turn machine ──
public sealed record BeginTurnCommand(Side Actor)                                    : ICommand;
public sealed record HarvestCommand(Side Actor)                                      : ICommand;
public sealed record DrawForTurnCommand(Side Actor)                                  : ICommand;
public sealed record EndTurnCommand(Side Actor)                                      : ICommand;

// ── upkeep settlement ──
public sealed record UpkeepPayCommand(Side Actor, CellRef Target, int UnitId)        : ICommand;
public sealed record UpkeepSacrificeCommand(Side Actor, CellRef Target, int UnitId)  : ICommand;

// ── board ──
public sealed record MoveUnitCommand(Side Actor, CellRef From, CellRef To, int UnitId) : ICommand;

// ── hand plays ──
public enum PlayMode : byte { Summon, Build, Set, SetTrap, Cast }
public sealed record PlayCardCommand(Side Actor, int HandIndex, PlayMode Mode, CellRef To) : ICommand;

// ── structures ──
public sealed record BuildStructureCommand(Side Actor, StructId Def, Element? Color, CellRef To) : ICommand;
public sealed record UpgradeStructureCommand(Side Actor, CellRef At, int UnitId, StructId Target) : ICommand;

// ── banked mana ──
public sealed record PourIntoChargeCommand(Side Actor, CellRef At, int UnitId, int Amount) : ICommand;
public sealed record FlipChargeCommand(Side Actor, CellRef At, int UnitId)                 : ICommand;
public sealed record SendBankedManaCommand(Side Actor, CellRef From, CellRef To)           : ICommand;

// ── combat ──
public abstract record AttackTarget;
public sealed record UnitTarget(CellRef Cell, int UnitId)      : AttackTarget;
public sealed record WallTarget(Side Defender)                 : AttackTarget;
public sealed record WorkerStackTarget(Side Owner, WorkerZone Zone) : AttackTarget;

public sealed record DeclareAttackCommand(Side Actor, CellRef Attacker, int UnitId, AttackTarget Target) : ICommand;
public sealed record ResolveCombatCommand(Side Actor)                                     : ICommand;

// ── the suspended-choice answer (§6) ──
public sealed record RespondCommand(Side Actor, ChoiceResponse Response)                  : ICommand;
```

Deliberate shape notes:

* Every command that names a unit carries **both** the coordinate and `UnitId`. `Validate` checks
  they agree. That is the fix for the JS's coordinate-only declarations.
* There is no `AttackWithGroupCommand`. A joint attack is N `DeclareAttackCommand`s sharing a target,
  regrouped by target *identity* at resolve time — exactly the JS model (spec 03 §6.2), and the only
  model that makes each declaration's blocker answer independent.
* Attacker selection (`G.atk`) and the RTS marquee are **view state**. They never reach the core.
* `PlayCardCommand` unifies summon/build/set/settrap/cast because `place()` does, and because the
  play-on-top branch is shared. The mode is validated against the card type (a check the JS local
  path skips and the MP host performs — spec 04 §19).

### 3.3 Handlers

```csharp
public interface ICommandHandler
{
    Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat);
    void      Execute (GameState s, ICommand cmd, ICardCatalog cat, EventSink events);
}

public sealed class CommandProcessor
{
    private readonly Dictionary<Type, ICommandHandler> _handlers;   // by-key lookup only, never iterated
    private readonly ICardCatalog _catalog;

    public Rejection CanExecute(GameState s, ICommand cmd)
    {
        if (s.IsOver)                                     return Rejection.GameOver;
        if (s.Pending is not null && cmd is not RespondCommand) return Rejection.ChoicePending;
        if (!_handlers.TryGetValue(cmd.GetType(), out var h)) return Rejection.WrongPlayMode;
        return h.Validate(s, cmd, _catalog);
    }

    public CommandResult Execute(GameState s, ICommand cmd, EventSink events)
    {
        var why = CanExecute(s, cmd);                     // ★ never bypassed
        if (why != Rejection.None) return CommandResult.No(why);
        _handlers[cmd.GetType()].Execute(s, cmd, _catalog, events);
        return s.Pending is null ? CommandResult.Ok : CommandResult.Waiting;
    }
}
```

A worked handler, showing the shape and the ported rules verbatim:

```csharp
public sealed class MoveUnitHandler : ICommandHandler
{
    public Rejection Validate(GameState s, ICommand cmd, ICardCatalog _)
    {
        var m = (MoveUnitCommand)cmd;

        // Phase gate is EXPLICIT here. The JS `startMove` omitted it and relied on the UI;
        // the MP host added it (spec 04 §19, open question 7). Host semantics win.
        if (s.Turn != m.Actor)                                        return Rejection.NotYourTurn;
        if (s.Phase != TurnPhase.Action && s.Phase != TurnPhase.Upkeep) return Rejection.WrongPhase;

        if (s.At(m.From) is not CreatureUnit u)                       return Rejection.NotACreature;
        if (u.Id != m.UnitId)                                         return Rejection.NoSuchUnit;
        if (u.Owner != m.Actor)                                       return Rejection.NotYourUnit;
        if (MoveRules.MoveSpent(s, u))                                return Rejection.MoveAlreadySpent;

        if (!Board.IsRealSlot(m.To.Row, m.To.Col))                    return Rejection.CellNotReal;
        if (s.At(m.To) is not null)                                   return Rejection.CellOccupied;
        if (!Board.Adjacent(m.From, m.To))                            return Rejection.NotAdjacent;

        return Rejection.None;   // NOT gated by Sick, Tapped, IsWorker or Entrench — spec 04 §5.2
    }

    public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink ev)
    {
        var m = (MoveUnitCommand)cmd;
        var u = (CreatureUnit)s.At(m.From)!;

        s.Put(m.From, null);                                  // vacate FIRST (spec 04 §6)
        if (u.Moved) { u.MovedTwice = true; u.Tapped = true; } // second move spends the whole turn
        else         { u.Moved = true; }
        s.Put(m.To, u);

        WorkerMath.Resync(s, m.Actor, cat);                   // `up` migrated between row figures
        ev.Add(new UnitMoved(u.Id, m.From, m.To, u.MovedTwice));
    }
}

public static class MoveRules
{
    /// moveSpent, with the JS's implicit global `G.upkeep` made explicit as
    /// "the OWNER's own upkeep" (spec 04 §5.4 caution). Closes the AI's accidental
    /// permanent two-move allowance (spec 04 §18.1) when Options.SecondMoveIsUpkeepOnly is true.
    public static bool MoveSpent(GameState s, CreatureUnit u)
    {
        if (!u.Moved) return false;
        bool upkeepWindow = s.Options.SecondMoveIsUpkeepOnly
            ? (s.Phase == TurnPhase.Upkeep && s.Turn == u.Owner)
            : (s.Turn == u.Owner);
        return !(upkeepWindow && !u.MovedTwice && !u.Tapped);
    }
}
```

### 3.4 Events: how the view stays a pure function of state

```csharp
public abstract record GameEvent;

// turn machine
public sealed record TurnStarted(Side Side, int TurnNumber)             : GameEvent;
public sealed record PhaseChanged(TurnPhase From, TurnPhase To)         : GameEvent;
public sealed record ManaYielded(Side Side, int UnitId, int Amount)     : GameEvent;
public sealed record ManaDrained(Side Side, int Kept, int Lost)         : GameEvent;
public sealed record HarvestCollected(Side Side, WorkerZone Zone, int Amount) : GameEvent;
public sealed record TowerFired(int TowerId, int TargetId, int Amount)  : GameEvent;
public sealed record CreatureRevived(Side Side, CardId Card)            : GameEvent;
public sealed record WorkerShortfallSettled(Side Side, WorkerZone Zone, SettleKind How, int UnitId) : GameEvent;

// board
public sealed record CardDrawn(Side Side, CardId Card)                  : GameEvent;
public sealed record UnitSummoned(int UnitId, CellRef At, bool Splashy) : GameEvent;
public sealed record UnitMoved(int UnitId, CellRef From, CellRef To, bool SpentTurn) : GameEvent;
public sealed record StructureRaised(int UnitId, CellRef At, StructId Def) : GameEvent;
public sealed record StructureUpgraded(int UnitId, StructId From, StructId To) : GameEvent;
public sealed record CardFlipped(int UnitId, CellRef At, bool Sick)      : GameEvent;
public sealed record TokenSpawned(int UnitId, CellRef At, CardId Kind)   : GameEvent;

// combat
public sealed record AttackDeclared(int AttackerId, AttackTarget Target, int DeclarationIndex) : GameEvent;
public sealed record BlockersAssigned(int DeclarationIndex, IReadOnlyList<int> BlockerIds)     : GameEvent;
public sealed record DamageApplied(int TargetId, int Amount, int SourceId, DamageTier Tier)    : GameEvent;
public sealed record UnitDestroyed(int UnitId, CellRef? At, Side Owner, UnitKind Kind)         : GameEvent;
public sealed record WallStruck(Side Defender, int Amount, int LifeRemaining)                  : GameEvent;
public sealed record UnitBounced(int UnitId, Side ToHand, BounceCause Cause)                   : GameEvent;
public sealed record TrapSprung(Side Owner, CardId Card, CellRef At)                           : GameEvent;
public sealed record SpellResolved(Side Caster, CardId Card, CellRef? Target)                  : GameEvent;
public sealed record MatchEnded(MatchOutcome Outcome)                                          : GameEvent;
```

That list is derived from the 27 monkey-patched functions in `22_fx_wrappers.js` catalogued in
spec 09 §18, plus the rules events the view needs for logs. Spec 09 is explicit that those 27 wrappers
are the *only* definition of every FX and SFX trigger point in the game — an implementer reading only
the rules files would ship a silent game. The event list is therefore a hard checklist: a test asserts
that a scripted "kitchen sink" match emits at least one of every event type.

**The view contract, precisely:**

```
render(GameState)          -> the complete, correct picture at rest    (pure, idempotent)
react(GameEvent)           -> transient animation, sound, damage number (never reads back into state)
```

The view may derive anything it wants from state (`CanActNow` for the standee pose, `IsInteractive`
for input gating, harvest-lock from `WorkerMath`), but it never *stores* rules state and never mutates
it except by submitting a command. Consequences that fall out for free:

* Re-rendering from state at any moment is always correct, so a dropped event is a missing animation,
  never a wrong board.
* A netcode snapshot adoption is just "replace state, re-render" — no reconciliation.
* A headless test needs no view at all.

### 3.5 Why this is a host-authoritative netcode drop-in

The deferred netcode needs four things, and all four already exist as a by-product:

1. **A closed, serializable intent set** — the command records. A guest sends `ICommand`; the host
   runs the *same* `CommandProcessor.Execute`. There is no second validator to write, which is
   precisely what went wrong in the JS (`42_mp_apply.js` re-implements every economy step).
2. **Authoritative state that round-trips** — `StateCodec`, with `SchemaVersion` and migrations.
3. **Per-recipient redaction** — `SerializationView.For(side)`, designed in from day one instead of
   retrofitted onto a snapshot that leaks both hands.
4. **Serializable suspension points** — `PendingRequest` (§6). The JS could not serialize mid-combat
   because declarations were local and the resolver was an `await` chain; MP had to bypass Combat v3
   entirely (spec 03 §14). Here, `CombatState` and the resolver's cursor are both in `GameState`.

The only thing the netcode layer adds is transport, a clock, and a policy for *who* may answer a given
`PendingRequest`. No rules code changes.

---

## 4. The turn / phase state machine

### 4.1 Explicit, testable, and the same for both sides

```csharp
public sealed class TurnMachine
{
    public static readonly TurnPhase[] Order =
        { TurnPhase.Upkeep, TurnPhase.Draw, TurnPhase.Action, TurnPhase.End };

    /// The ONLY writer of GameState.Phase.
    public static void SetPhase(GameState s, TurnPhase p, EventSink ev)
    {
        if (s.Phase == p) return;
        ev.Add(new PhaseChanged(s.Phase, p));
        s.Phase = p;
    }

    public static bool CanAdvance(GameState s, TurnPhase from, TurnPhase to, out Rejection why) { /* table */ }
}
```

Legal transitions (spec 02 §4.2, spec 07 §3.1), enforced as data:

| From | Command | To | Guard |
|---|---|---|---|
| — (match start) | `NewMatch` | Upkeep | — |
| Upkeep | `HarvestCommand` | Draw | no creature-settleable shortfall remains |
| Draw | `DrawForTurnCommand` | Action | advances **even on an empty deck** — there is no deck-out loss |
| Action | `EndTurnCommand` | End | `!Combat.HasDeclarations` |
| End | `BeginTurnCommand(other)` | Upkeep | — |

Upkeep and Draw can never be skipped. `EndTurnCommand` from Draw or Upkeep is a `Rejection.WrongPhase`
that the view turns into the same nudge the JS shows.

**The AI runs this machine.** Spec 07 §3.2 flags that the JS AI never calls `setPhase`, leaving
`G.phase === 'end'` for its whole turn, and that the codebase accidentally depends on that for input
gating. We replace the accident with `IsInteractive(state, side)` (§2.6) and give the AI a real
Upkeep → Draw → Action → End sequence. Two consequences must be handled deliberately, and both are
`RulesOptions` flags defaulting to JS behaviour:

* the AI draws inside `BeginTurn` in the JS, not in a Draw phase (`AiDrawsAtTurnStart`, default `true`);
* the AI re-runs `readyWorkers` after settling and the player does not, a straight AI advantage
  (`AiReadiesWorkersAfterSettle`, default `true`).

### 4.2 `BeginTurn` — the normative 12-step pipeline

Spec 02 §5 and spec 07 §4 both call this ordering load-bearing. It is implemented as one method with
one numbered step per line and a test that asserts observable side effects in order.

```csharp
public static void BeginTurn(GameState s, Side owner, ICardCatalog cat, EventSink ev)
{
    s.TurnNumber++;                                   // 1. PLY counter (not a round counter)
    s.Turn = owner;                                   // 2.
    s.Combat.Clear();                                 // 3. declarations do not survive a turn boundary
    Array.Clear(s.P(owner).UpkeepPaid, 0, 4);         // 4. last turn's keep payments EXPIRE

    foreach (var u in s.OwnedCreatures(owner))        // 5. board units of THIS side only
    { u.Sick = false; u.Tapped = false; u.Moved = false;
      u.MovedTwice = false; u.PaidUpkeep = false; u.HasBlocked = false; u.DischargeBonus = 0; }

    Keywords.ChrysalisUpkeep(s, owner, cat, ev);      // 6. may hatch; always re-sicks the cocoon
    Keywords.OverchargeUpkeep(s, owner, ev);          // 7. oc = min(3, oc+1)
    StructureUpkeep.Tick(s, owner, cat, ev);          // 8. mana → tower fire → revive (once)
    DeathSweep.Cleanup(s, cat, ev);                   // 9. sweep anything the tower killed
    WorkerMath.Resync(s, owner, cat);                 // 10. rebuild pools from the board
    s.P(owner).ReadyWorkers();                        // 11. the ONLY un-sick/un-tap of workers

    TurnMachine.SetPhase(s, TurnPhase.Upkeep, ev);    // 12. both sides — no phase anomaly
    ev.Add(new TurnStarted(owner, s.TurnNumber));
}
```

`StructureUpkeep.Tick` iterates in the JS's exact order — the owner's Front slots 0..6, then Back
0..6, then owned Center slots 0..6 — because the once-per-turn Reliquary latch and multi-tower firing
order depend on it (spec 05 §4.3). Unlike the JS it owner-filters the Front/Back pass unconditionally
(spec 07 §18 item 6): unreachable today, fragile forever.

### 4.3 Where the worker pool resyncs

The complete, enumerated list (spec 02 §6.4). A missing call leaves a stale pool; an *extra* call is a
behaviour change. Both directions are covered by a test that counts `Resync` invocations for a scripted
turn.

`BeginTurn` step 10 · `NewMatch` · after every hand play / menu build / upgrade (`AfterDeploy`) ·
`MoveUnit` · `UpkeepSacrifice` · AI deficit rebalance (per move, per sacrifice) · `Flip` **(both
branches — the JS structure branch returns early, spec 02 Bug 1)**.

And explicitly **not** from `DeathSweep.Cleanup` — a mid-combat raze leaves stale workers standing
until the next resync, which is observable (they still harvest and still block). `RulesOptions
.CleanupResyncsWorkers` defaults to `false` to preserve it.

### 4.4 Upkeep settlement

```csharp
public static class Upkeep
{
    public static readonly WorkerZone[] SettleOrder =
        { WorkerZone.Back, WorkerZone.Front, WorkerZone.Center, WorkerZone.Raid };

    public static int ZoneDeficit(GameState s, Side o, WorkerZone z)
        => Math.Max(0, Math.Max(0, -WorkerMath.RowWorkers(s, o, z)) - s.P(o).UpkeepPaid[(int)z]);

    /// The highest-upkeep UNPAID creature in the first deficit zone. The sort must be TOTAL:
    /// Upkeep DESC, then row index ASC, then column ASC — the JS relies on stable sort here.
    public static bool TryFindOffender(GameState s, Side o, out CellRef cell, out int unitId);

    /// The portion of the shortfall with no settleable creature — Harvest pays this out of
    /// its own proceeds rather than dead-locking the turn (spec 02 §7.4).
    public static int OrphanDeficit(GameState s, Side o);

    public static bool HarvestUnlocked(GameState s, Side o) => !TryFindOffender(s, o, out _, out _);
}
```

Three settle actions, ported exactly: **Move** (routes through `MoveUnitCommand`; the upkeep second
move sets `MovedTwice` and `Tapped`), **Pay** (`min(Upkeep, ZoneDeficit)`, no partial payment, sets
`PaidUpkeep` even when the capped amount is less than the creature's full upkeep), and **Sacrifice**
(`ToGrave` **directly, not through `Cleanup`** — so Detonate and Reap do **not** fire; that is a
deliberate rules distinction, spec 07 §6.4).

`HarvestCommand` reproduces `doHarvest` including its deliberately stale `owe` (captured *before*
harvesting, paid *after*) and the "credit the full remaining deficit into `UpkeepPaid` even when only
partially paid" anti-deadlock rule.

Mana credit funnels through exactly one method, closing the five-duplicate-cap-sites problem
(spec 01 §15.2 item 10):

```csharp
public static void AddMana(GameState s, Side o, int amount, EventSink ev)
{
    if (amount <= 0) return;                 // negative credits are impossible by construction
    var p = s.P(o);
    int before = p.Mana;
    p.Mana = Math.Min(ManaCap, p.Mana + amount);   // ManaCap = 99
    if (p.Mana != before) ev.Add(new ManaChanged(o, before, p.Mana));
}

public static bool TrySpend(GameState s, Side o, int amount)   // NEVER a silent partial debit
{
    var p = s.P(o);
    if (amount < 0 || p.Mana < amount) return false;
    p.Mana -= amount; return true;
}
```

`payAny`'s partial-debit-and-ignored-return-value pattern (spec 02 §14 item 4, spec 05 §17 item 9) is
gone: `TrySpend` fails loudly, and a `false` return aborts the command.

---

## 5. Card effects: the representation decision

### 5.1 The options, weighed against the actual content

| Approach | What it buys | What it costs here |
|---|---|---|
| **(a) Data-driven effect graph** — cards compose primitives (`Deal(N) → To(Selector)`), authored as data | New cards with no code; designers author effects | An expression language, a selector DSL, an evaluator, a serializer for partially-evaluated effects, and an editor. Every one of the eight keywords needs an escape hatch anyway (§5.2). |
| **(b) One scripted behaviour per card** | Total freedom per card | 64 creature classes for 8 behaviours. Impossible to diff-test against the JS, which has 8 functions. |
| **(c) Hybrid — data for parameters, a small closed set of hand-written C# handlers selected by enum** | Exact 1:1 correspondence with the JS functions; trivially diff-testable; zero infrastructure | New *mechanics* need code (new *cards* do not) |

**Chosen: (c), the hybrid.** The justification is entirely from the actual keyword list in spec 06,
not from taste:

1. **The behaviour set is tiny and closed.** Eight creature keywords, six spell effects, two trap
   triggers, seven structure effects, one flag (First Strike). Twenty-four behaviours total. An effect
   graph's fixed cost exceeds twenty-four hand-written handlers before it pays back anything.
2. **Keywords do not compose.** `kw` is explicitly *single-valued* (spec 06 §2.1) and `kwOf` returns it
   only for non-worker creatures. There is no stacking, no ordering, no interaction matrix. The single
   biggest reason to build an effect graph — combinatorial interaction — does not exist.
3. **Cards are already fully parameterised by data.** `det`, `reap`, `wardhp`, `grow`, `hatch`, `into`
   are numbers on the template. The 64 creatures genuinely are eight behaviours × varying integers,
   and the `into` hatch form is data too. So the *card* layer is data-driven either way; the argument
   is only about the *behaviour* layer.
4. **Half the keywords do things a graph could not express without escape hatches.** Undertow removes a
   unit from the board *or a worker pool* and pushes a hand card at full printed HP, choosing the
   victim by **mana cost, not attack**. Ward and Reap place a token at `firstEmptyCell`, whose scan
   order (back 0..6, front 0..6, then center lanes only) is itself a rule. Chrysalis mutates the same
   instance in place, preserving id/owner/bank/cell, and clears its own keyword to stop the loop.
   Scour prefers a face-down in the defender's back row, else the first non-CC structure, and sets
   `h = 0` regardless of HP. Each of these would become a bespoke graph primitive — i.e. C# code with
   a data wrapper around it. That is strictly worse than C# code.
5. **Fidelity is the project's dominant risk, and the hybrid maximises it.** Each handler is a
   line-by-line port of one named JS function, so a reviewer can diff them side by side and the
   differential test suite (§9.4) can target them individually. An effect graph would re-express the
   same behaviour in a different vocabulary, making "is this the same rule?" unanswerable by inspection.
6. **Extensibility is preserved where it is actually wanted.** New cards = new rows in `cards.json`.
   New *keywords* = one new enum value + one handler class registered in a table, which is the honest
   cost of a new mechanic.

### 5.2 The shape

```csharp
public enum Keyword : byte
{ None = 0, Detonate, Undertow, Entrench, Ward, Reap, Chrysalis, Scour, Overcharge }

/// The six real hook points, from spec 06 §6.0. Nothing else exists — do not invent a seventh.
public interface IKeywordHandler
{
    Keyword Keyword { get; }

    /// ENTER — summon or flip. (Ward)
    void OnEnter(GameState s, CreatureUnit self, EffectContext ctx) { }

    /// DEATH — fired from Cleanup AFTER the cell is already cleared. (Detonate, Reap)
    void OnDeath(GameState s, CreatureUnit self, Side owner, EffectContext ctx) { }

    /// UPKEEP — the owner's BeginTurn. (Chrysalis, Overcharge)
    void OnUpkeep(GameState s, CreatureUnit self, EffectContext ctx) { }

    /// PRE-COMBAT — before ANY damage, in all three damage engines. (Undertow)
    void OnBeforeDamage(GameState s, UnitGroup attackers, UnitGroup defenders, EffectContext ctx) { }

    /// DECLARE — may this attacker be interposed at all? (Scour)
    bool IgnoresInterceptors => false;

    /// ON-HIT — after a surviving unblocked strike connects. (Scour)
    void OnHitConnected(GameState s, CreatureUnit self, Side defender, EffectContext ctx) { }
}

public sealed class KeywordRegistry
{
    private readonly IKeywordHandler?[] _byKeyword = new IKeywordHandler?[9];   // indexed by enum
    public IKeywordHandler? For(Keyword k) => _byKeyword[(int)k];

    /// kwOf: keywords apply ONLY to non-worker creatures. Workers, tokens and structures are inert.
    public IKeywordHandler? For(BoardObject o)
        => o is CreatureUnit { IsWorker: false } c ? For(c.Keyword) : null;
}
```

`EffectContext` carries the catalog, the event sink and the RNG — never the view, never a timer.

Two handlers in full, to show that the port really is 1:1:

```csharp
public sealed class UndertowHandler : IKeywordHandler
{
    public Keyword Keyword => Keyword.Undertow;

    /// applyUndertow (06_mana_workers.js:135-142). Fires ONCE per combat call regardless of how
    /// many wardens are present. Selects by highest MANA COST, not attack. Immune: workers,
    /// tokens, entrenched units. Bounced unit returns at FULL printed HP.
    public void OnBeforeDamage(GameState s, UnitGroup attackers, UnitGroup defenders, EffectContext ctx)
    {
        bool anyWarden = false;
        foreach (var d in defenders)
            if (d is CreatureUnit { Hp: > 0 } c && ctx.Keywords.For(c)?.Keyword == Keyword.Undertow)
            { anyWarden = true; break; }
        if (!anyWarden) return;

        CreatureUnit? mark = null;
        foreach (var a in attackers)                    // total order: Cost DESC, then group index ASC
            if (a is CreatureUnit { Hp: > 0, IsWorker: false, IsToken: false, Entrench: false } c
                && (mark is null || c.Cost > mark.Cost))
                mark = c;
        if (mark is null) return;

        var owner = BoardOps.RemoveFromBoardOrPool(s, mark);
        if (owner is null) return;
        s.P(owner.Value).Hand.Add(HandCard.FromCreature(mark, ctx.Catalog));   // full MaxHp
        attackers.Remove(mark);
        ctx.Events.Add(new UnitBounced(mark.Id, owner.Value, BounceCause.Undertow));
    }
}

public sealed class DetonateHandler : IKeywordHandler
{
    public Keyword Keyword => Keyword.Detonate;

    /// onCreatureDeath (06_mana_workers.js:124-129). Creatures are strictly preferred; only when
    /// none is alive does it hit the WEAKEST enemy structure. Never touches the life pool.
    public void OnDeath(GameState s, CreatureUnit self, Side owner, EffectContext ctx)
    {
        int n = self.Detonate; if (n <= 0) return;

        // deadliest first, then frailest — TOTAL order with a board-position tiebreak.
        var target = s.EnumerateEnemyCreatures(owner)              // ROWS order, then slot order
                      .MinBy(TargetKey.DeadliestThenFrailest)      // stable by construction
                   ?? (BoardObject?)s.EnumerateEnemyStructures(owner).MinBy(TargetKey.LowestHp);
        if (target is null) return;

        Damage.Apply(s, target, n, self.Id, DamageTier.Trigger, ctx.Events);
    }
}
```

Spells and structure effects use the same pattern with their own tiny interfaces
(`ISpellEffectHandler` keyed by `SpellEffect`, dispatched on `effect` and **never** on card name —
spec 06 §7.3 — and `IStructureEffectHandler` keyed by `StructEffect`). Target legality lives in one
place, `Targeting.CanTarget(effect, target, caster)`, called *by the resolver itself*, closing the JS
split where three separate callers enforced legality and `resolveSpell` enforced none (spec 06 §11.1).
Cost is charged only after a successful resolve (JS behaviour, and correct).

First Strike stays a **flag**, not a keyword, because it creates a real two-tier damage step in every
damage engine rather than hooking a trigger point.

---

## 6. Suspended choices: replacing `async` with a serializable state machine

The JS interleaves rules mutation with `await`ed modals and FX timers — `_resolveNow`, `pairFight`,
`targetFight`, `foeTurn`, `playerTrapOnSummon`, `RESP.defendWindow`. Spec 03 §17 risk 10 and spec 07
§17 both call this the single biggest structural obstacle to a deterministic core.

**The replacement:** the engine advances until it needs an answer, writes a `PendingRequest` into
`GameState`, and returns `CommandStatus.AwaitingChoice`. Someone — the human UI or the AI policy —
submits a `RespondCommand`. The engine resumes from a cursor that is *also* in `GameState`, so a
snapshot taken mid-resolution is complete and resumable.

```csharp
public abstract record PendingRequest(Side Responder);

public sealed record BlockerRequest(Side Responder, int AttackerId, int DeclarationIndex,
    int DeclarationCount, IReadOnlyList<UnitRef> Eligible)                     : PendingRequest(Responder);

public sealed record AbsorberRequest(Side Responder, int AttackerId,
    IReadOnlyList<UnitRef> Blockers)                                          : PendingRequest(Responder);

public sealed record RetaliationRequest(Side Responder, int DefenderId,
    IReadOnlyList<UnitRef> Attackers)                                         : PendingRequest(Responder);

public sealed record ResponseWindowRequest(Side Responder, ResponseTrigger Trigger,
    IReadOnlyList<UnitRef> ArmedTraps)                                        : PendingRequest(Responder);

public abstract record ChoiceResponse;
public sealed record BlockersChosen(IReadOnlyList<UnitRef> Blockers) : ChoiceResponse;
public sealed record IndexChosen(int Index)                          : ChoiceResponse;
public sealed record TrapChosen(UnitRef? Trap)                       : ChoiceResponse;   // null == pass
```

`RespondCommand` validates the response *shape* against the outstanding request and re-validates
every referenced unit by id against a freshly recomputed eligibility list — exactly what the JS MP
layer does by object identity (spec 03 §14), and what a host must do against a malicious guest.

**The anti-tell response window** (`RESP`) is split correctly: the *decision* (which trap springs) is a
rules input and lives in the core as `ResponseWindowRequest`; the *timer* (3/4/6 s, the 15 s pause,
the constant duration whether or not a trap is held) lives entirely in the view. Spec 03 §10.1 is
explicit that constant duration is the anti-tell guarantee — that is a UI obligation, documented in
the view design, and the core is timing-free.

**Combat resolution as an explicit step machine.** The resolver's cursor is state, not a call stack:

```csharp
public enum CombatStage : byte
{ Idle, AwaitingResponseWindow, BlockedPairFights, UnblockedCreatureGroups,
  UnblockedMisc, ApplyWallDamage, ScourStrikes, Complete }

public sealed class CombatState                     // serialized as part of GameState
{
    public readonly List<AttackDeclaration> Declarations = new();
    public CombatStage Stage;
    public int  Cursor;                             // index into the stage's working list
    public int  SubCursor;                          // tier / blocker index within one fight
    public int  AccumulatedWallDamage;
    public readonly List<int> ScourHitUnitIds = new();
    public readonly List<int> BlockedDeclIndices = new();   // partitioned ONCE, before any damage
    public readonly List<int> OpenDeclIndices    = new();
    public UnitRef? CommittedResponseTrap;          // springRef — consumed at most once
    public bool HasDeclarations => Declarations.Count > 0;
}

public sealed class AttackDeclaration
{
    public CellRef Attacker; public int AttackerUnitId;     // BOTH — identity is authoritative
    public DeclarationKind Kind;                            // Unit | Wall | WorkerStack
    public CellRef? TargetCell; public int TargetUnitId;
    public Side TargetSide; public WorkerZone TargetZone;
    public readonly List<UnitRef> Blockers = new();
}

public static class CombatResolver
{
    /// Advances as far as it can. Returns Waiting when it parks on a PendingRequest.
    public static CommandResult Step(GameState s, ICardCatalog cat, EventSink ev);
}
```

`Step` is a loop over `switch (s.Combat.Stage)` that either advances a cursor or parks. Everything the
spec calls load-bearing survives verbatim:

* the blocked/open partition happens **once, before any damage** — a blocked attacker stays blocked
  even if it kills its whole gang, and contributes zero wall damage;
* resolution order: blocked pair fights (declaration order) → unblocked creature groups (target
  insertion order) → unblocked misc (declaration order) → summed wall damage → Scour → win check;
* a death sweep after each individual fight, so "simultaneous" is per-tier-per-fight, not global;
* two damage tiers with conditions read at tier start;
* wall damage aggregated and applied once.

`RulesOptions.CheckWinAfterEachDamage` (default `false`) and `RulesOptions.GlobalSimultaneousDamage`
(default `false`) hold the two open design questions without contaminating the parity build.

**The legacy engine is ported too.** `focusFire` / `resolveCombat` remain live in the JS for worker-stack
strikes, provoked face-downs and the whole MP path (spec 03 §8). They go into
`LegacyCombat.FocusFire` / `LegacyCombat.Resolve`, named unmistakably, with a comment stating exactly
which three cases reach them and that unifying the two engines is a rules change gated behind
`RulesOptions.WorkerStacksUseTieredCombat`.

Ordered accumulation, because `Map` insertion order is observable (spec 03 §17 risk 12):

```csharp
/// Insertion-ordered damage accumulator. Replaces the JS Map so that both tier simultaneity
/// and application order are preserved. Never a Dictionary.
public sealed class DamageBatch
{
    private readonly List<(BoardObject Target, int Amount)> _entries = new();
    public void Hit(BoardObject t, int amount);          // accumulates in place, preserving first-seen order
    public void ApplyAndClear(GameState s, int sourceId, DamageTier tier, EventSink ev);
}
```

---

## 7. Determinism

Every nondeterminism source the extraction flagged, and its disposition.

| Source (spec citation) | Disposition |
|---|---|
| `Math.random()` in `aiPickTarget` — 0.6 face-down, 0.3 structure (07 §13.1) | `rng.Chance(60, 100)` / `rng.Chance(30, 100)` — integer, no floats |
| `deckOf` / `expandDeck` Fisher–Yates (07 §13.2) | Seeded `Rng`, fixed loop direction (`i = n-1 → 1`), documented as part of the contract |
| Random-opponent commander pick via `Object.keys(CCS)` (07 §13.2, §14.1) | An explicit `CommanderIds` ordered array + `rng.NextInt(count)` |
| Battlefield scenery, particles, campaign map, dialogue lines, MP coin flip (07 §13.3) | A **separate** presentation RNG in the view. Toggling FX can never desync a replay. |
| `List<T>.Sort` instability vs stable JS sort (03 §17.1, 06 §10, 07 §14.2) | Every comparator is a **total order** with an explicit board-position tiebreak, plus `Sorting.StableSort`. Belt and braces. |
| `Map` iteration order in `byT` and the damage maps (03 §17.12, 07 §14.1) | `OrderedMap` / `DamageBatch` — never `Dictionary` where iteration is observable |
| `ZONES`, `ROWS`, `MOVE_ADJ`, `buildList`, `PHASE_ORDER` as arrays (07 §14.1) | Static `readonly` arrays, never `HashSet`/`Dictionary` |
| `setTimeout` 380/650/650 ms, 4 s window, 15 s pause, 280 ms lunge (07 §8.3, 03 §10.1) | Zero-cost state transitions in the core; the view schedules every beat |
| `async`/`await` in the resolver and the AI turn (03 §17.10, 07 §14.4) | `PendingRequest` + step machine; `Task` is a banned symbol |
| `Math.round` half-up vs banker's rounding (05 §17.10, 06 §2.4) | Integer arithmetic `(a + b + 1) / 2` — no `Math.Round` anywhere; a test asserts all 36 commanders |
| Neighbour enumeration order differs by owner (04 §23) | Pinned canonical order: ascending `RowKey`, then ascending `Col` |
| Reference `GetHashCode` leaking allocation order | Banned symbol; ids are the only identity |
| Floating point anywhere | Banned by the state-shape test; the economy is already all-integer |

```csharp
/// PCG32. Chosen over xoshiro/Mersenne for a small serializable state (two ulongs), no tables,
/// and identical results on every platform because it uses only ulong arithmetic.
public struct Rng
{
    public ulong State, Inc;                 // both serialized; Inc is the stream selector

    public static Rng FromSeed(ulong seed, ulong stream = 1);

    public uint NextUInt();                  // the one primitive

    /// Unbiased, no floats, no modulo bias — fixed rejection-sampling algorithm.
    public int NextInt(int exclusiveMax);

    /// Integer probability. rng.Chance(60, 100) replaces `Math.random() < 0.6`.
    public bool Chance(int numerator, int denominator) => NextInt(denominator) < numerator;

    /// Fisher–Yates, descending, in place. The ONLY shuffle in the core.
    public void Shuffle<T>(IList<T> list);
}
```

`Rng` lives inside `GameState`, so its stream position is part of the snapshot and part of the hash.
Every consumer draws from it in a fixed call order. There is exactly one `Rng` in the core and exactly
one, unrelated, in the view.

**The determinism contract, stated as three testable properties:**

1. Same `(seed, RulesOptions, command sequence)` ⇒ byte-identical `StateCodec.ToBytes` at every step.
2. `Read(Write(s))` is indistinguishable from `s` under `Hash` and under any subsequent command
   sequence.
3. `Clone(s)` then applying the same commands to both copies yields equal hashes (catches accidental
   shared mutable substructure — the JS's aliased arrays after `startGame`, spec 01 §15).

---

## 8. `RulesOptions` — the parity flag register

Every open question from the seven specs becomes a boolean or small enum, frozen at match creation,
included in the state hash. **All defaults reproduce the JS exactly.** This is what makes differential
testing meaningful: divergences are bugs in our port, never intended design changes.

```csharp
public sealed record RulesOptions
{
    // ── parity flags (default = JS behaviour). Each MUST be resolved and deleted before ship. ──
    public bool WallStructuresCanBlock          { get; init; } = false; // 05 §9  Bulwark/Bastion inert
    public bool VillagerStructuresTrainWorkers  { get; init; } = false; // 05 §17.2
    public int  OverchargeScale                 { get; init; } = 1;     // 03 §9.1  set to 500 to fix
    public bool RetaliationUsesEffectiveAttack  { get; init; } = false; // 01 §15.2.7
    public bool ScourBypassIsPerAttacker        { get; init; } = true;  // 03 §4.4  v3 semantics
    public bool SecondMoveIsUpkeepOnly          { get; init; } = true;  // 04 §18.1  applied to BOTH sides
    public bool EnforcePlaceRowOkFromHand       { get; init; } = false; // 04 §9.2
    public bool FlipStructureResyncsWorkers     { get; init; } = false; // 04 §14.2 [BUG]
    public bool FaceDownKeepsColor              { get; init; } = false; // 04 §13.2 [BUG]
    public bool CleanupResyncsWorkers           { get; init; } = false; // 05 §10.2
    public bool GlobalSimultaneousDamage        { get; init; } = false; // 03 §18.1
    public bool CheckWinAfterEachDamage         { get; init; } = false; // 03 §17.19
    public bool WallStrikeSpringsAttackTrap     { get; init; } = false; // 03 §18.4
    public bool WorkerStacksUseTieredCombat     { get; init; } = false; // 03 §18.6
    public bool DoubleKoIsDraw                  { get; init; } = false; // 07 §18.15
    public bool AiChoosesRetaliationTarget      { get; init; } = false; // 03 §17.6  JS hardcodes index 0
    public bool AiReadiesWorkersAfterSettle     { get; init; } = true;  // 07 §4.1  AI advantage
    public bool AiDrawsAtTurnStart              { get; init; } = true;  // 07 §4.1
    public int  AiWallDefenceThreshold          { get; init; } = 4;     // 03 §17.4  dead at ×500 scale
    public bool PlayerHasStructureCaps          { get; init; } = false; // 05 §19.5

    public static RulesOptions JsParity => new();
    public static RulesOptions Shipping => JsParity with { /* filled in once design rules */ };
}
```

A test asserts that `JsParity` is the default-constructed value, so adding a flag with a
non-JS default is a compile-time-visible mistake. A second test enumerates the flags and fails with a
reminder listing any still outstanding at the point a ship build is configured.

Two things are **not** flags because the specs are unambiguous that they are bugs with no design
content: the MP owner re-stamp (01 §13.2) and the `payAny` negative-argument mana gain (07 §18.5).
Those are fixed outright.

---

## 9. Testing

### 9.1 What the jsdom harness covered, and what replaces it

`.srdtest/harness.js` (444 lines, gitignored) boots the *old monolith* `spawn-row-duel-v26.html` in
jsdom, injects a trailing `<script>` shim to reach top-level `const` bindings, stubs `AudioContext`,
`canvas`, `matchMedia` and `requestAnimationFrame`, then runs ~42 assertions. Its coverage clusters
into five groups:

| Harness cluster | Example assertions | Replacement tier |
|---|---|---|
| Boot / DOM shape | no load errors, `#center` has 7 cells with 3 lanes | **Deleted.** DOM shape is a view concern; Unity has its own view tests. |
| Geometry constants | `SLOTS === 7`, `CENTER_LANES == [1,3,5]` | Tier 1 unit tests (§9.2) |
| Keyword behaviour | detonate hits the deadliest, undertow bounces, ward/reap tokens, chrysalis hatch, overcharge discharge, scour shatter | Tier 1 + Tier 2 golden scenarios |
| Spell behaviour | chain hits the top two, bounce, entrench resists, reliquary revive | Tier 1 + Tier 2 |
| Economy / settings | mana at start, harvest yield, board-angle toggle | Tier 1 (economy) / deleted (settings) |

It is also **stale**: it asserts `colReach`, an on-board keep at `back[3]` with `findCC` returning it,
colored `cmana` harvest, and `wardhp === 2` — four rules that no longer exist. It targets a file that
is now a redirect stub. It should be retired, not migrated: its *intent* survives as Tier 1 and Tier 2
below, at higher fidelity, running in milliseconds without jsdom.

### 9.2 Tier 1 — rule unit tests (fast, exhaustive, table-driven)

The specs hand us finished test tables. Each becomes a `[TestCaseSource]`.

```csharp
[Test]
public void RowsCrossedInto_MatchesSpecTable_ForAll35Pairs()
{
    // spec 03 §4.1 — every (attacker, target) pair including the two virtual wall rows.
    foreach (var (a, t, expected) in CrossedRowTable.All)
    {
        Span<RowKey> buf = stackalloc RowKey[5];
        int n = Board.RowsCrossedInto(a, t, buf);
        Assert.That(buf[..n].ToArray(), Is.EqualTo(expected), $"a={a} t={t}");
    }
}

[Test]
public void Adjacency_NeighbourCounts_MatchSpecTable()
{
    // spec 04 §4.7 — e.g. youFront col 2 has 7 neighbours, center lanes have 6 and no lateral move.
    foreach (var (cell, count) in NeighbourCountTable.All)
        Assert.That(Board.CountNeighbours(cell), Is.EqualTo(count), cell.ToString());
}

[Test]
public void DualCommanders_WorkerRounding_IsHalfUp()
{
    // spec 05 §17.10 / 06 §2.4 — banker's rounding would silently cost 16 of 36 commanders a worker.
    foreach (var cc in Catalog.Commanders.Where(c => c.Colors.Length == 2))
    {
        int expected = (Elements[cc.Colors[0]].Wk + Elements[cc.Colors[1]].Wk + 1) / 2;
        Assert.That(cc.Workers, Is.EqualTo(expected), cc.Id.ToString());
    }
}

[Test]
public void CombatValues_AreOnTheX500Scale()
{
    // spec 03 §17.20 — catches a missed conversion in the card export.
    foreach (var c in Catalog.Creatures)
    {
        Assert.That(c.Attack % 500, Is.Zero, c.Name);
        Assert.That(c.Health % 500, Is.Zero, c.Name);
        Assert.That(c.Cost, Is.GreaterThanOrEqualTo(1), $"{c.Name}: no deckable card may cost 0");
    }
}
```

Direct table sources already written in the specs: crossed rows (03 §4.1), neighbour counts and the
39 movement/placement vectors (04 §4.7, §24), upgrade damage-carry examples (05 §7.4), `bidLineage`
per structure (05 §7.5), the pool-shape invariant "8 creatures at costs 1,1,2,2,3,4,5,6 with the
cost-3 card always First Strike" (06 §2.1), `firstEmptyCell` scan order (06 §6.2), `buildingUpkeep`
and `buildingDamage` iteration orders (05 §4.1, §4.3), the `moveSpent` truth table (04 §5.1),
`aiPickDeploySlot` preferences (07 §11.5).

### 9.3 Tier 2 — golden scenarios and hash regression

A terse scenario DSL, so a test reads like the spec's worked examples (03 §15.1, §15.2 port directly).

```csharp
[Test]
public void Example_A_GangBlockedWallAssault_WithUndertow()
{
    var s = Scenario.New(seed: 1)
        .Commanders(you: "fire", foe: "water")
        .Creature(Side.You, Cell(YouFront, 2), "Ashfang")     // 1500/1000 First Strike
        .Creature(Side.You, Cell(YouFront, 3), "Magmaw")      // 3000/2500 cost 6
        .Creature(Side.Foe, Cell(FoeFront, 0), "Mistling", tapped: true)
        .Creature(Side.Foe, Cell(FoeBack,  4), "Rippler")
        .Creature(Side.Foe, Cell(Center,   1), "Undertow")
        .TapAllWorkers(Side.Foe)
        .Phase(TurnPhase.Action, Side.You)
        .Build();

    var e = new DuelEngine(s, Catalog);
    e.Do(new DeclareAttackCommand(Side.You, Cell(YouFront, 2), Ids.Ashfang, new WallTarget(Side.Foe)));
    e.Answer(new BlockersChosen(...));                        // AI policy answers via the same path
    e.Do(new DeclareAttackCommand(Side.You, Cell(YouFront, 3), Ids.Magmaw,  new WallTarget(Side.Foe)));
    e.Answer(new BlockersChosen(...));
    e.DoAll(new ResolveCombatCommand(Side.You));              // pumps every PendingRequest via a script

    Assert.That(e.State.P(Side.Foe).Life, Is.EqualTo(10000), "a defended wall costs bodies, not life");
    Assert.That(e.Hand(Side.You), Does.Contain("Magmaw"),     "Undertow bounced it at full HP");
    Assert.That(e.Unit("Ashfang").Hp, Is.EqualTo(500));
    Assert.That(e.Grave(Side.Foe), Does.Contain("Mistling"));
}
```

On top of that, **hash regression**: a set of recorded command scripts (`Tests/Scripts/*.srdscript`)
each with a `.hashes` sidecar containing `StateCodec.Hash` after every command. The runner replays and
compares; `dotnet test -- --bless` regenerates the sidecars, and the diff in review shows exactly which
scenarios a change moved. This is the cheapest possible whole-engine regression net: one 64-bit number
per step catches any state divergence anywhere.

### 9.4 Tier 3 — differential testing against the JS

This is the tier that makes the port trustworthy, and it is only possible for as long as the JS runs.
Build it early; retire it when the JS is deleted.

```
tools/diffjs/
  runner.mjs        # loads index.html + src/js/*.js in jsdom, exposes a stdin/stdout JSON protocol
  adapter.mjs       # maps our ICommand records onto JS calls; scripts every modal
  dump.mjs          # emits the canonical JSON state in OUR field names
```

Mechanics:

* `runner.mjs` boots the **current modular** build (`index.html` + `src/js/`), not the retired
  monolith. It stubs `AudioContext`, `canvas`, `matchMedia`, `requestAnimationFrame` (reusing the
  existing harness's stubs, which are good), and forces every timer to zero.
* Every suspension point is scripted from the same answer queue the C# side uses: `askBlock`,
  `askAbsorb`, `askRetaliate`, `RESP.defendWindow`, `playerTrapOnSummon` are replaced with functions
  that pop the next scripted answer. `30_resp.js` is **kept loaded** (it genuinely changes rules
  timing, spec 06 §11.1) with its durations set to 0. `22_fx_wrappers.js` may be loaded or not — it
  provably changes no rules, so a test asserting identical results with and without it is itself a
  useful check of that claim.
* `Math.random` is monkey-patched to draw from the *same* PCG32 stream the C# side uses, so deck
  shuffles and the AI's two probability rolls line up. This is the one place the JS must be modified
  for testing, and it is a test-harness override, not a source change.
* Both sides emit canonical JSON through a shared field-name mapping (`nm→Name`, `a→Attack`, `h→Hp`,
  `min→Workers`, …). The comparison is a plain string diff, so failures point at a field.

Three modes:

1. **Scripted parity** — the same `.srdscript` files as Tier 2, run on both engines, state compared
   after every command. These are the cases we designed on purpose.
2. **AI self-play parity** — seed a match, let `ScriptedAiPolicy` drive *both* sides against the JS
   `foeTurn`, compare after every step. This exercises long tails no hand-written test reaches.
3. **Fuzz + shrink** — generate random *legal* command sequences (enumerate legal commands from state,
   pick one with the seeded RNG), run both, and on the first divergence shrink the script to a minimal
   reproducer. This is where sort-stability and iteration-order bugs surface, because they need a tie
   to exist before they show.

A divergence is triaged into exactly one of: (a) a port bug — fix; (b) a known JS bug the specs
flagged — assert it in a `[Category("KnownJsDivergence")]` test naming the `RulesOptions` flag that
will fix it later; (c) a new JS bug — record it in the spec and pick a side. Category (b) is the
mechanism that lets parity testing coexist with deliberate improvement.

### 9.5 Tier 4 — property and architecture tests

```csharp
[Test] public void Serialization_RoundTripsExactly([Values(0,1,2,…)] int scenarioId);
[Test] public void Clone_IsIndependent([Values] …);                 // mutate one, hash both
[Test] public void ReplayIsDeterministic_AcrossProcessRestarts();   // hash trace equality
[Test] public void Redaction_NeverLeaksOpponentHandOrDeck();
[Test] public void EveryGameEventType_IsEmittedByTheKitchenSinkMatch();  // the 27-hook checklist
[Test] public void EveryCommandType_HasAHandlerAndARejectionPath();
[Test] public void EveryKeyword_HasAHandler_AndEveryHandlerHasACard();
```

Plus the four architecture tests from §1.3 (no non-BCL references, no async state machines, no
floating point in state, `RulesOptions.JsParity` is the default).

### 9.6 What a rules regression test looks like, end to end

The complete workflow for "Cannon Tower should fire before the death sweep":

```csharp
[Test]
public void CannonTower_FiresAtUpkeep_AndItsKillIsSweptBeforeWorkersResync()
{
    var s = Scenario.New(seed: 7)
        .Commanders(you: "fire", foe: "earth")
        .Structure(Side.You, Cell(YouBack, 0), StructId.Tower)        // 1000 damage, sup −2
        .Creature (Side.Foe, Cell(FoeFront, 4), "Sparkling", hp: 500) // dies to the tower
        .Creature (Side.Foe, Cell(FoeFront, 6), "Loamhide")           // must NOT be hit
        .Phase(TurnPhase.End, Side.Foe)
        .Build();

    var e = new DuelEngine(s, Catalog);
    e.Do(new BeginTurnCommand(Side.You));

    Assert.That(e.Events.OfType<TowerFired>().Single().TargetId, Is.EqualTo(e.Id("Sparkling")));
    Assert.That(e.State.At(Cell(FoeFront, 4)), Is.Null, "swept by step 9, before the worker resync");
    Assert.That(e.Unit("Loamhide").Hp, Is.EqualTo(2000), "front→center→back scan takes the FIRST match");
    Assert.That(e.State.P(Side.You).Workers[(int)WorkerZone.Back].Members.Count,
                Is.EqualTo(Catalog.Commander(e.State.P(Side.You).Commander).Workers - 2),
                "the tower's −2 support is reflected after step 10");
    Assert.That(StateCodec.Hash(e.State), Is.EqualTo(Golden.CannonTowerUpkeep));
}
```

Three things are asserted at three levels: the **event** (the view will animate it), the **state** (the
board is right), and the **hash** (nothing else moved). That triple is the house style.

---

## 10. Implementation order

Sequenced so that something is testable at the end of every step and nothing is written twice.

| # | Deliverable | Gate |
|---|---|---|
| 1 | Assemblies, csproj glob, banned symbols, CI `dotnet test` running one trivial test | Gates 1–4 of §1.3 all green |
| 2 | Geometry + refs + `Rng` + `Sorting`/`OrderedMap` | The spec's geometry tables pass (Tier 1) |
| 3 | Card catalog records + a `cards.json` → ScriptableObject importer + the JSON→record loader | 78 registry entries, 36 commanders, ×500 scale test, rounding test |
| 4 | State model, clone, `StateCodec`, hashing, redaction | Round-trip and clone-independence properties |
| 5 | Command pipeline, events, `DuelEngine`, `NewMatch` | Empty board, `wk` workers, opening hand of 4 |
| 6 | Economy: worker math, resync, harvest, vault drain, mana | Tier 1 economy tests; upkeep settle scenarios |
| 7 | Turn machine + upkeep settlement + structures/upgrades | The 12-step ordering test; upgrade damage-carry table |
| 8 | Placement, movement, set/flip, play-on-top | The 39 movement/placement vectors from spec 04 §24 |
| 9 | Keyword + spell + trap handlers | The old harness's keyword assertions, at higher fidelity |
| 10 | Combat: declarations, resolver step machine, legacy focus fire, pending requests | Worked examples A and B from spec 03 §15 |
| 11 | `ScriptedAiPolicy` (the 11-step `foeTurn` port) | AI self-play runs 200 turns without an illegal command |
| 12 | Differential harness (Tier 3) | Scripted parity, then self-play parity, then fuzz |
| 13 | Resolve and delete the `RulesOptions` parity flags with design | Flag register empty |

Steps 1–5 are the load-bearing ones: get the assembly boundary, determinism and the command pipeline
right and the rest is transcription. Step 12 should start as soon as step 10 lands, while the JS is
still the living reference.

---

## 11. Risks this design does not eliminate

Stated plainly so they are tracked rather than assumed away.

* **Transcription fidelity.** Nine specs, hundreds of rules. The differential harness is the mitigation
  and it must exist early; without it, "faithful" is an assertion, not a fact.
* **The keyword handler set is closed by design.** If the game later wants composable, stacking or
  conditional abilities, the hybrid must be revisited. The `IKeywordHandler` seam and the enum registry
  keep that a contained refactor, but it is a refactor.
* **Hand-written codec drift.** Adding a state field and forgetting the codec silently changes nothing
  until a save or a snapshot loses it. Mitigation: a reflection test that asserts every serializable
  field of every state type is visited by `StateCodec` (walk the field graph, compare against a
  recorded manifest, fail on unvisited fields).
* **`RulesOptions` becoming permanent.** Twenty flags are a migration device; twenty flags shipped are
  twenty untested combinations. The register must reach zero. The flag-count test with a ship-build
  assertion is the forcing function.
* **AI policy fidelity is the hardest part to test.** Its decisions depend on stable sorts and
  iteration order in six places (spec 07 §14.2). Self-play differential testing is the only realistic
  net; hand-written AI tests will not find these.
* **Spec 08 (campaign) and 09 (presentation) are out of scope here** but both reach into the core:
  `checkWin` currently calls `campResolve` directly (spec 08 risk). This design inverts that — the core
  emits `MatchEnded(MatchOutcome)` and the campaign subscribes — but the campaign design must actually
  adopt it, including `BattleOutcome.Abandoned` as a first-class value rather than the JS's nulled
  field.
