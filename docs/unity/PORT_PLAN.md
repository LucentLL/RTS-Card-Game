# Spawn Row Duel — Master Migration Plan (HTML/JS → Unity 6 / C# / Steam)

**Status:** authoritative. This is the document you work from.
**Companions:** `docs/unity/spec/01`–`09` (what the game *is*), `docs/unity/design/01`–`03` (how the
port is *built*). This plan sequences them and decides what happens when.
**Target:** Unity 6000.5.5f1, URP, PC/Steam first, mouse+keyboard, AI singleplayer + campaign.
Multiplayer deferred but designed for.

---

## 1. Honest scope assessment

### 1.1 What this actually is

This is not a port. It is a **rewrite of a working game against a written specification**, where the
specification happens to be complete and the reference implementation happens to still run.

The JS build is ~6,100 lines across 31 files. That number is misleading in both directions:

* **It undercounts the work.** The JS gets away with things C# cannot: one global mutable `G`, duck-typed
  refs, `async` interleaved with UI prompts, monkey-patching instead of an event system, `Math.random()`
  everywhere, and three divergent copies of the same validation logic. Every one of those is a shortcut
  the port must pay back with real architecture. The rules core alone will land at **2–3× the line count
  of the JS rules files** because it adds commands, validation, events, serialization, cloning, hashing,
  a step-machine combat resolver, and a determinism contract.
* **It overcounts the work.** Roughly **35% of the JS is presentation glue and browser workarounds that
  are explicitly deleted**, not ported — `fitBoard`'s measure-shrink loop, `elementFromPoint` fallbacks,
  27 monkey patches, `innerHTML` rebuilds, art 404-probing, PWA plumbing, the whole orthographic canvas
  globe renderer, and the entire multiplayer layer (deferred). Plus a documented inventory of genuinely
  dead code.

### 1.2 The three things that make this bigger than it looks

1. **Fidelity is the product.** The specs found ~30 places where the JS does something by accident
   (stable sort ties, `Map` insertion order, `Math.round` half-up, the AI's phase-stuck-at-`end` input
   gate, `payAny` with a negative argument). C# will silently do the *other* thing in every one of them.
   Catching those requires a differential test harness against the live JS — real infrastructure, not a
   nice-to-have.
2. **Combat v3 is genuinely hard.** Two coexisting damage engines, a two-tier First Strike step inside
   each fight, per-fight-not-global simultaneity, four suspension points that are currently `await`s and
   must become serializable requests, and a resolver cursor that has to live inside game state so a
   snapshot mid-resolution is resumable. This is the single largest milestone in the plan.
3. **The presentation is the game's identity.** The tilted diorama, the two castle walls, the standees,
   the nine hand-authored elemental impact FX, the 23 synthesized sound cues. None of the audio exists as
   assets — all 23 cues are Web-Audio recipes that must be re-authored. A "correct but ugly" Unity build
   is not the product.

### 1.3 Sizing

Relative T-shirt sizes only. No calendar claims.

| Size | Relative units | Character |
|---|---|---|
| **S** | 1 | A focused sitting. One clear deliverable, no unknowns. |
| **M** | 3 | A few sittings. Known shape, some transcription volume. |
| **L** | 6 | Multi-week. Real design inside it, or high transcription volume. |
| **XL** | 10 | The hard ones. Expect to redo part of it. |

**16 milestones, ≈84 relative units.** If one unit is a focused working day, this is a multi-month
solo project. Treat that as a shape, not a schedule — the unit size is yours to calibrate after M1–M3,
which are deliberately small and will tell you your actual rate.

Three playability checkpoints, deliberately front-loaded:

| Checkpoint | Milestone | Cumulative | What you can do |
|---|---|---|---|
| **Headless playable** | M8 | ≈36 / 84 (43%) | Play a complete duel in a terminal against a stub opponent |
| **On-screen playable** | M9 | ≈42 / 84 (50%) | Play it with the mouse in the Unity editor — ugly, but real |
| **Vertical slice** | M11 | ≈54 / 84 (64%) | One battle, real AI opponent, real cards, keywords and spells |

Everything after M11 is content, polish, campaign and ship prep — large, but low-risk and independently
shippable.

### 1.4 Where the risk is concentrated

```
       risk
        ▲
   XL   │            M8 Combat
   L    │      M6/M7 Rules    M13 Presentation
   M    │  M4/M5 Core         M11 AI      M14 Campaign
   S    │ M1 M2 M3                              M15 M16
        └────────────────────────────────────────────────▶ time
```

M4–M8 are the load-bearing span. Get the state model, determinism and command pipeline right and
everything after is transcription. Get them wrong and you rewrite everything downstream.

---

## 2. Milestones

Strict dependency order. Each states **Goal / Deliverables / Done when / Unblocks**.

> **Parallelism:** the plan is a chain, but after **M5** the view track (M9, M13) can run in parallel
> with the rules track (M6–M8, M10–M11) because the core's command/event/state API is frozen at M5.
> If you ever have a second pair of hands, that is the fork point. Solo, follow the chain — the ordering
> below already front-loads playability.

---

### M1 — Unity project exists, repo is clean · **S** · *do this today*

**Goal.** A Unity 6 URP project at `<repo>/unity`, committed, with Git configured so nothing about
Unity can corrupt the repo or the HTML build.

**Deliverables**
1. Unity Hub → Create project. **Project name `unity`, Location `C:\Users\mcgee\code\RTS-Card-Game`,
   template Universal 3D, editor 6000.5.5f1, Unity Cloud OFF, Unity Version Control OFF.**
   (Verified: `unity/` does not exist, so Hub will not refuse the folder.)
2. Verify: `unity/ProjectSettings/ProjectVersion.txt` says `6000.5.5f1`; `unity/Packages/manifest.json`
   contains `com.unity.render-pipelines.universal`. If URP is missing you picked plain 3D — delete and
   redo, do not retrofit.
3. Editor settings: **Asset Serialization = Force Text**, **Version Control Mode = Visible Meta Files**,
   API Compatibility Level = **.NET Standard 2.1**.
4. Apply the `.gitignore` block from design 03 §4.2 — anchored to `unity/`, **with the
   `!unity/[Aa]ssets/**/dist/` and `!unity/[Aa]ssets/**/build/` negation guards**. Leave the existing
   unanchored `dist/`/`build/` rules on lines 18–19 alone; the HTML build depends on them.
5. Add `.gitattributes` (design 03 §4.4): LF + `-merge` on Unity YAML, `binary` on images.
6. **Do NOT enable Git LFS.** GitHub Pages does not resolve LFS objects — it would instantly break every
   card image on the Pages URL, which is currently your only mobile test surface.
7. Commit.
8. **Start the Unity Hub download for "Windows Build Support (IL2CPP)" and the Visual Studio 2022
   "Desktop development with C++" workload now, in the background.** Verified absent on this machine;
   they are long downloads and no shipping build is possible without them.

**Done when** `git status` is clean, the project opens, and `git check-ignore -v unity/Library` reports
ignored while `unity/Assets/**` does not.

**Unblocks** everything.

---

### M2 — Rules-core skeleton and the headless test gate · **S**

**Goal.** The architectural boundary that the whole project rests on, proven by tooling rather than
discipline: a pure C# assembly that *cannot* acquire a Unity dependency.

**Deliverables**
1. Folder tree + asmdefs per design 01 §1.1: `SpawnRowDuel.Rules`, `.Ai`, `.Testing` with
   `noEngineReferences: true`; `SpawnRowDuel.Data`, `.View`, `.Editor` as normal Unity assemblies.
2. `unity/Headless/*.csproj` globbing the same `.cs` files, `netstandard2.1`, `LangVersion 9.0`.
3. `BannedSymbols.txt` + `Microsoft.CodeAnalysis.BannedApiAnalyzers` + `TreatWarningsAsErrors`:
   ban `System.Random`, `DateTime.Now`, `Task`, `Guid`, `List<T>.Sort`, `HashSet<T>` iteration,
   `float`/`double` in state types.
4. `dotnet test` green on one trivial test.
5. CI script running: headless build → `dotnet test`.

**Done when** all four gates from design 01 §1.3 are live and a deliberately-added `using UnityEngine;`
in `Rules/` fails *both* the Unity compile and the headless build.

**Unblocks** every rules milestone. **Do not skip or defer this** — it is the cheapest possible
insurance against the single failure mode that would force a rewrite.

---

### M3 — Card data pipeline and art link · **M**

**Goal.** Real cards, as data, in Unity — diffable, regenerable, validated.

**Deliverables**
1. `tools/setup-unity-links.mjs` — creates the directory junction
   `unity/Assets/Game/Art/Cards → <repo>/assets/cards` (`mklink /J`, no admin needed), idempotent.
   The junction is git-ignored; `Cards.meta` is tracked. Unity writes `.meta` files into the **real**
   `assets/cards/` directory, so GUIDs are committed and survive a fresh clone.
2. `CardDefinition` + `CardDatabase` ScriptableObjects; `CardImporter` (menu item + `CardImportCli`)
   reading `docs/unity/spec/cards.json` (356 KB, verified complete), **ignoring the `art` field**
   (≈250 KB of dead placeholder SVG data URIs).
3. Idempotent import: load-then-mutate, never delete-then-create, with a JSON snapshot before/after so a
   no-op import touches zero files and leaves `git status` clean.
4. The **12 import-time validations** from design 03 §5.6 as build failures, especially:
   * **V4** — every combat value is 0 or a multiple of 500 (catches the incomplete ×500 rescale).
   * **V7** — dual-commander workers use `MidpointRounding.AwayFromZero` (JS `Math.round` is half-up;
     C# banker's rounding silently costs 16 of 36 commanders a worker).
   * **V2/V3** — slug and asset-path collision checks (slugs are not unique-checked anywhere in the JS).
   * **V11** — unknown enum strings throw rather than defaulting to `None`.
5. `tools/regen-cards.sh`: export → import → `git status`, one command.
6. Newtonsoft (`com.unity.nuget.newtonsoft-json`) — `JsonUtility` cannot parse `cards.json` because the
   null-vs-0 distinction is load-bearing for `wardhp`, `reap`, `grow`, `hatch`, `val`.

**Done when** ~150 `.asset` files + `CardDatabase.asset` are committed; 78 registry entries, 36
commanders, 64+4 creatures, 14 spells/traps, 13 structures, 18 forges all present; re-running the
importer produces an empty diff.

**Unblocks** M4 (the catalog record types), M11 (the AI needs real cards), M13 (art).

> Art coverage is **partial today** — 83 `_cardart`, 69 `_fieldart`; Dark and Electric creatures have
> none. Missing art must be *reported, never fatal*, and the 3-tier fallback must exist from day one or
> the board renders broken cells instead of placeholders.

---

### M4 — Geometry, determinism primitives, state model, codec · **M**

**Goal.** The five design commitments made concrete. Nothing here is gameplay; everything downstream
depends on it being exactly right.

**Deliverables**
1. `Board` geometry: `Columns=7, Rows=5, BaseColumn=3, CenterLanes={1,3,5}`, wall rows −1 and 5,
   `IsRealSlot`, `IsLane`, `CenterSlotOk`, `RowFor`, `WhichOf`, `ZoneForRow`, `RowsOfZone`,
   `RowsCrossedInto`, `Adjacent`, `Neighbours` (**pinned canonical order**: ascending row, then column).
2. `CellRef`, `PoolRef`, `UnitRef` as a real discriminated union carrying a `UnitId`.
3. `Rng` — serializable PCG32 inside `GameState`, integer-only `Chance(numerator, denominator)`.
   No floating point anywhere in the core.
4. `OrderedMap`, `OrderedSet`, `Sorting.StableSort`, total-order comparators with an explicit
   `(rowIndex, slotIndex, unitId)` tiebreak baked in.
5. `GameState` as **one flat positional `BoardObject?[35]`** indexed `row*7+col`. **Never** per-player row
   collections. Ownership read only from `BoardObject.Owner`.
6. Hand-written `Clone`, `StateCodec` (one traversal, two backends: binary for saves + FNV-1a hash,
   canonical JSON for diffing), `SerializationView.For(side)` redaction, `StateMirror`, `SchemaVersion`.
7. The **codec-coverage reflection test**: walk every serializable field of every state type against a
   recorded manifest, fail on any field the codec does not visit. Write this *with* the codec.

**Done when** spec 01 §10 / spec 03 §4.1's crossed-row table passes for all 35 (a,t) pairs; spec 04 §4.7's
neighbour-count table passes; clone-independence and codec round-trip properties pass; a state hash is
stable across process restarts.

**Unblocks** everything. This is the milestone where a mistake is most expensive.

---

### M5 — Command pipeline, events, engine, match setup · **M**

**Goal.** The one funnel through which the human, the AI, and future netcode all act — and the event
stream the view will consume. **After this milestone the core's public API is frozen and view work can
start in parallel.**

**Deliverables**
1. `ICommand` records (full set from design 01 §3.2), `ICommandHandler`, `CommandProcessor` where
   **`Execute` always internally re-runs `Validate`**. No trusted entry point. Enum `Rejection`, never
   strings.
2. `GameEvent` hierarchy derived from the 27-row FX wrapper table (spec 09 §18), `EventBuffer`.
3. `DuelEngine`: `Apply(ICommand) → CommandResult`, `State`, `DrainEvents()`, `Pending`.
   **Zero `async`, zero `UnityEngine`, zero wall clock.**
4. `NewMatch(commanderIds, decks, seed, RulesOptions)`.
5. `RulesOptions` parity-flag register (design 01 §8), all defaults = JS behaviour, included in the
   state hash, plus the test asserting `JsParity == default`.

**Done when** `NewMatch` produces an empty 35-cell board, each player's `min.back` pool sized `CCS[cc].wk`
(un-sick, un-tapped), an opening hand of 4, and a reproducible state hash for a fixed seed.

**Unblocks** M6–M8, and **unblocks M9's view work in parallel**.

---

### M6 — Economy, workers, turn machine, upkeep · **L**

**Goal.** A turn can begin, harvest, draw, and end — for both sides, through the *same* phase machine.

**Deliverables**
1. `WorkerMath.RowWorkers` (the per-row model only — delete `workerCap`/`structSupport`/`monsterUpkeep`/
   `canTrain`/`enforceCap`/`trainVillager`, all verified unreachable) and `SyncWorkers` with pool growth
   pushing sick bodies and shrink popping the tail with no grave record.
2. The **12-step `BeginTurn` pipeline** in normative order (design 01 §4.2): ply++ → clear
   decls/cardMenu/moveMana → reset `upaid` → reset unit flags → chrysalis → overcharge → building upkeep →
   cleanup → syncWorkers → readyWorkers → branch. Run by **both** sides.
3. `IsInteractive(state, side)` as an **explicit predicate** — replacing the JS accident where `G.phase`
   sticks at `'end'` for the whole AI turn and four call sites depend on it.
4. Upkeep settlement: per-zone deficit in fixed order back→front→center→raid, `upkeepOffender`,
   Move/Pay/Sacrifice, Harvest locked until creature-settleable shortfall is zero, and doHarvest's
   structural-orphan fallback (pay from proceeds, forgive the remainder — the no-deadlock rule).
5. `Mana.AddMana(side, amount)` as the **single clamped credit path** (the JS duplicates the 99 cap
   across five call sites), `ManaVault.Capacity`/`Drain`, end-of-turn drain.
6. `StructureUpkeep.Tick` with the exact front(0–6) → back(0–6) → center(0–6) iteration order, the
   once-per-turn Reliquary revive latch, and Cannon Tower's front→center→back target scan.

**Done when** the economy tests from spec 02 pass; the 12-step ordering test passes; a headless game can
run 200 empty turns with a stable per-turn hash.

**Unblocks** M7, M8.

---

### M7 — Placement, movement, set/flip, structures, upgrades · **L**

**Goal.** Cards get onto the board and move around it. Structures get built and upgraded.

**Deliverables**
1. Movement as the portable predicate: `creature ∧ owned ∧ !moveSpent ∧ real slot ∧ empty ∧ adjacent`.
   **Not** gated by `sick` or `tapped`. `MoveSpent(c) = c.moved && !(upkeep && !c.moved2 && !c.tapped)`.
2. `doMove` in exact order: vacate → apply flags → occupy → `SyncWorkers(mover)`.
3. Deployment: own back/front rows only; structures additionally on center flanks 0/2/4/6; creatures
   **never** into the center. `centerSlotOK`, `placeRowOK`.
4. Set face-down (◆1 → `inv:1`, banks toward flip), set trap (◆1, banks nothing), `flip()` with
   `sick = (turnNo <= setTurn)`, play-on-top (destroy covered card, pay from its bank, carry surplus).
5. `StructureCatalog`: 13 fixed defs + the two element-parameterised forge families (`resolveStruct`),
   `BuildList(cc)`, `BidLineage` with the 8-hop guard, `applyUpgrade` preserving id/owner/tile/bank and
   carrying damage through (`h = max(1, newMax − oldDamage)`).
6. `Board.Neighbours` order used everywhere — no "first legal cell" logic reading a per-owner chain.

**Done when** the **39 movement/placement test vectors from spec 04 §24** pass, plus the upgrade
damage-carry and `bidLineage` tables from spec 05.

**Unblocks** M8.

---

### M8 — Combat v3 + legacy engine + pending requests · **XL** · ⚑ *headless playable*

**Goal.** The hardest milestone. When it lands, a full duel can be played end-to-end in a terminal.

**Deliverables**
1. `CombatState` as **authoritative, serializable** game state (the JS keeps `G.decls` local, which is
   exactly why MP had to bypass Combat v3).
2. `AttackDeclaration` storing the attacker as a **`UnitId` + coordinate**, not a coordinate alone —
   structurally fixing the "move a declared attacker and it resolves against whatever moved in" bug.
3. `CombatResolver` as a **step machine** whose cursor (`CombatStage`, `Cursor`, `SubCursor`, accumulated
   wall damage, scour list, committed response trap) lives inside `GameState`, so a mid-combat snapshot
   is resumable. **No `async` anywhere.**
4. `PendingRequest` / `ChoiceResponse`: `BlockerRequest`, `AbsorberRequest`, `RetaliationRequest`,
   `ResponseWindowRequest`.
5. The strict resolution order: blocked pair fights (declaration order) → unblocked creature target
   groups (insertion order) → misc unblocked (wall accumulation, worker stacks, structures, face-downs,
   traps) → summed wall damage → Scour strikes → `checkWin`. `cleanup()` after each individual fight.
6. Two-tier First Strike inside each fight, tier conditions read at tier start.
7. `LegacyCombat.FocusFire` / `ResolveCombat` — **both engines**, named distinctly, for worker-stack
   strikes and provoked face-downs.
8. `cleanup()` sweep: ROWS order, slots 0..6, then worker pools; cell freed **before** the death trigger
   fires; re-sweep up to 40 times.
9. `ITurnPolicy` interface + a **stub random-legal-move policy** — enough to play against.
10. `Testing/ScriptRunner` + a text console that plays a duel from stdin.

**Done when** worked examples **A and B from spec 03 §15** reproduce exactly, and you can play a
complete duel to a life-zero win in the terminal against the stub policy.

**Unblocks** M9 (there is now something to render), M10, M11, M12.

> **This is the milestone to slow down on.** Everything the specs flag about sort stability, `Map`
> insertion order and per-fight-not-global simultaneity lands here.

---

### M9 — Minimal Unity battle scene · **L** · ⚑ *on-screen playable*

**Goal.** The duel from M8, on screen, with mouse input. Deliberately ugly: grey boxes, no FX, no walls,
no card art. Prove the view↔core contract before investing in looks.

**Deliverables**
1. Scene set: `00_Boot` (single) + persistent additive `01_Shell` + `30_Battle`.
2. `BoardRoot` at the world contract: 1×1 cells, `x = (col−3)*1.06`, `z = (2−rowIndex)*1.06`, virtual
   wall rows at `z = ±3.18`.
3. One perspective camera, two presets (Top-Down = 78° pitch / 20° FOV; Tilted = ~45°), Cinemachine
   blend, `BoardFramer` replacing `fitBoard()` entirely.
4. `BoardRaycaster` with the **44 px forgiveness snap ported exactly** (rect-distance metric,
   centre-distance tiebreak, applied only to activations on empty non-lit cells).
5. `IIntentSink` — the single funnel. Every input path emits the same `ICommand`.
6. Placeholder cell highlights, placeholder unit quads with text labels, a minimal hand strip,
   phase buttons, and modal prompts for the four `PendingRequest` types.
7. `PresentationDirector` skeleton: drains `GameEvent`s, gates input via `IsInputFrozen`, surfaces
   `PendingRequest`s only when the timeline is idle.

**Done when** you can play a full duel with the mouse in the editor against the stub policy, and
`IntentFunnelTests` proves click/key/drag/marquee/snap all produce identical commands.

**Unblocks** M13 (all polish attaches to this skeleton), and gives you a real feedback surface.

---

### M10 — Keywords, spells, traps, response window · **L**

**Goal.** Cards stop being vanilla stat blocks.

**Deliverables**
1. `IKeywordHandler` with the **six real hook points** (ENTER, DEATH, PRE-COMBAT, UPKEEP, ATTACK-PREP,
   ON-HIT/DECLARE) and 8 handlers, 1:1 with the JS functions for reviewability.
   ⚠ **The keyword engine is not in the card files** — it is `06_mana_workers.js:96-185`. An implementer
   working from `03`/`04`/`14` alone ships zero keywords.
2. `ISpellEffectHandler` × 6 dispatched on `effect`, **never on card name**.
3. Traps: flat ◆1 set cost, armed only when `turnNo > setTurn`, one trap per trigger event,
   summon-triggered traps ignore `card.effect` entirely, `flip()` does not provoke summon traps.
4. **One `CanTarget(effect, unit, caster)` predicate in the core** — the JS splits target legality across
   the input layer, the AI, and the MP host. Validate *before* paying.
5. `ResponseWindow` as a first-class core state machine (a suspended continuation), not a UI concern —
   `30_resp.js` genuinely changes rules timing and *replaces* `playerTrapOnSummon` wholesale.
6. First Strike as a flag creating a real two-tier step in **every** damage path.

**Done when** the old jsdom harness's keyword assertions pass at higher fidelity, plus a table test per
keyword and per spell effect.

**Unblocks** M11 (the AI's spell decisions), M12.

---

### M11 — Scripted AI · **L** · ⚑ **VERTICAL SLICE COMPLETE**

**Goal.** One battle. Real AI opponent. Real cards. This is the milestone the whole plan is sequenced
toward.

**Deliverables**
1. `ScriptedAiPolicy` — the verbatim 11-step `foeTurn` port, as a **command/pending-request state
   machine**, not a coroutine.
2. `aiFixDeficit` (3 passes), `aiBuild` (buildList order + per-bid caps via lineage), `aiUpgrade`
   (≤1/turn), `aiPickTarget` (the 0.6 / 0.3 rolls on the **seeded** RNG), `aiChooseInterceptors`,
   `aiPickDeploySlot`, the absorber pick, `aiMoveCreature`.
3. `AiTuning` record — the difficulty knobs the JS never had, defaulted to JS behaviour.
4. Deterministic AI self-play: 200 turns, zero illegal commands, reproducible hash.

**Done when** you can start the Unity build, pick a commander, and play a complete battle against the AI
with real cards from `CardDatabase`, and win or lose.

**Unblocks** everything else, and — critically — **unblocks actually playing your own game again**.

> Do not gold-plate the AI here. The JS AI has *no difficulty scaling at all*; reproduce it faithfully,
> then tune later behind `AiTuning`.

---

### M12 — Differential harness vs the JS · **L**

**Goal.** Convert "faithful port" from an assertion into a fact. **Start this as soon as M8 lands** —
do not wait for M11 — because it is only possible while the JS is a living reference.

**Deliverables**
1. `tools/diffjs/runner.mjs` — boots `index.html` + `src/js/*.js` in jsdom, JSON stdin/stdout protocol.
2. `adapter.mjs` — maps `ICommand` records onto JS calls; **scripts all five suspension points**
   (`askBlock`, `askAbsorb`, `askRetaliate`, `RESP.defendWindow`, `playerTrapOnSummon`).
3. `Math.random` monkey-patched onto the same PCG32 stream in the JS.
4. `dump.mjs` — canonical JSON state in the C# field names.
5. Three tiers: scripted scenario parity → AI self-play parity → fuzz + shrink.
6. `tools/gen_golden.mjs` + committed `tests/golden/*.json` per-ply hash fixtures.

**Done when** a 200-turn AI self-play run produces byte-identical canonical state in both engines, and a
fuzz run of 10,000 random legal commands finds no divergence.

**Unblocks** M16 (resolving parity flags with confidence).

> The state hash must serialize in the pinned `cleanup()` sweep order or it is incidental rather than
> meaningful, and will produce false failures that erode trust in the whole harness.

---

### M13 — The presentation pass · **XL**

**Goal.** Make it look and sound like Spawn Row Duel.

**Deliverables**
1. **Card faces:** one `CardFrame.uxml` (DM_Template), baked via `CardFaceBaker` into a RenderTexture
   atlas, serving all four scales (hand / big inspect at 744:1033 / board mini / deck-builder tile).
   Rules text **generated** from card data, never authored per-card.
2. **Board:** cell highlights in the board surface shader from a 7×5 state texture (11 states + a
   colorblind shape channel), battlefield scenery, mat and vignette.
3. **Standees:** alpha-clipped opaque quads, up/laid poses from `CanActNow`, blob shadows, the 165cqw
   width cap preserved as an explicit max, 3-tier art fallback resolved at import.
4. **Walls:** two crenellated castle walls, three windows each (info left / deck+GY right / hand center),
   `WallState { None, Player, Foe }` enum replacing the `:has()`/`!important` cascade, `--wallY` as an
   animated **physical-camera lens shift**, 0.24 s slide with the back-out ease.
5. **Diegetic wall props** at the virtual rows with click colliders; the HUD ♥ kept as a second route to
   the identical `DeclareAttack(WallTarget)` command.
6. **FX:** 15 primitives + **9 hand-authored elemental impact compositions** as VFX Graph prefabs
   (fire rises, water arcs then falls, earth tumbles, dark implodes then blooms, electric strikes down,
   divine floods white). A single tinted burst erases the game's elemental identity.
7. **Audio:** **re-author 23 sound cues from scratch** using spec 09 §16 as the design brief. There are
   no audio assets — every cue is a Web-Audio recipe today.
8. **Presentation event bus:** all 27 wrapper rows mapped to `GameEvent` handlers, verified by
   `PresentationCoverageTests` (fails when a new event has no presentation).
9. World-space billboarded damage numbers with per-cell stack offset (0.18 u lateral / 0.1 u vertical)
   and a 4-number merge cap.
10. AI declarations **rendered** with the same declAtk/declTgt/declBlk language — the browser never
    painted them, and the Combat v3 design calls for visible alternating declarations.

**Done when** the visual parity checklist (design 02 §15.1) passes side-by-side against the browser
build, and the framing matrix (§15.2) shows no scrolling or letterboxing at 1280×720, 1920×1080,
2560×1080, 3440×1440, 3840×2160 and 1600×1200 in both angle presets and all wall states.

**Unblocks** a build you would show someone.

---

### M14 — Campaign · **L**

**Goal.** The world-conquest metagame on a real 3D globe.

**Deliverables**
1. **Bake `HexSphereAsset_f4`** in the editor: GP(4,0), exactly 162 tiles / 320 corners, CCW corner rings,
   adjacency, merged prism mesh, `triangleIndex → tileId` LUT. **Freeze the tile index order** and guard
   it with a golden-fixture test — saves store `tileTerr` as a raw index array, so any renumbering
   silently corrupts every save. Weld vertices with a 1e-6 spatial hash, not `toFixed(6)` string keys.
2. `CampaignMapGenerator`: Mitchell best-candidate seeding (8 candidates) + multi-source BFS → 22
   contiguous territories; farthest-point empire seeding + second BFS → 8 contiguous element empires.
   Unit-test contiguity over ≥1000 seeds.
3. `CampaignRules`, `CampaignTurnResolver`, `CampaignBattleResolver` as pure C# — attackability, the
   **absorb cascade**, the `completed` latch, the zero-territory defeat, the exact End Turn ordering
   (turn++ → garrison growth → shuffled rival attacks with mutate-as-you-go garrisons → defeat check).
4. **Invert the battle↔campaign coupling**: the duel emits `MatchEnded(MatchOutcome)`, the campaign
   subscribes. `BattleOutcome.Abandoned` is a first-class value, not a nulled field.
5. Globe view: mesh + orbit camera + `Physics.Raycast` picking. **Discard** the projection maths, painter
   sort, culling heuristics, skirt quads and the `R*EXH` picking correction. **Keep the feel constants:**
   drag 0.005 rad/px, pitch clamp ±1.25 rad, inertia seed dx*0.0009 with 0.93 decay, idle spin
   0.0011 rad/frame after 2600 ms, tap thresholds 7 px mouse / 15 px touch. Add mouse-wheel zoom.
6. Challenge dialogue: 8 `ElementBarkSet` + 8 `RivalExchange` ScriptableObjects, exactly 4 lines with the
   fixed speaker pattern, typewriter at 14 ms/char, skip.
7. `CampaignState` JSON with **`SchemaVersion` and a real migration hook** — the JS just deletes the old
   key, which on Steam Cloud becomes synced cross-machine data loss.

**Done when** you can start a campaign, take a territory, absorb a capital, unlock its dual banner, and
complete or lose a campaign; and a golden-seed test asserts the full post-EndTurn map state.

---

### M15 — Menus, deck builder, settings, save/load, accessibility · **L**

**Goal.** Everything around the battle.

**Deliverables**
1. Main menu, faction select, solo deck+opponent pickers, banner/result screens.
2. Deck builder: 40 cards exactly, max 3 copies, max 5 saved decks, search / type filter / element
   filter / cost filter (6 = "6 or more") / 5 sort orders with their documented tiebreaks, mana curve
   (bar height `max(8, round(count/maxBucket*100))%`), leader change deleting off-colour cards.
3. Settings: board angle, standees, cut-ins, reduced motion, volume, mute, response window, **animation
   speed**, **colorblind shapes**. Persisted.
4. Save system with `SchemaVersion` and migration; Steam Auto-Cloud paths.
5. **Accessibility — treat as a ship blocker, not polish:** full keyboard + gamepad bindings for phase
   actions, hand selection, cell navigation, attack-group building and the marquee (none exist today);
   a shape/pattern secondary channel for cell state (colour is currently the only encoding);
   right-click as the global inspect gesture.

---

### M16 — Resolve parity flags, balance, ship prep · **M/L**

**Goal.** Stop being a port and start being a product.

**Deliverables**
1. **Drive the `RulesOptions` flag register to zero.** Each flag gets a decision, a code change, a test,
   and a deletion. A flag-count assertion in the ship-build configuration is the forcing function.
2. Difficulty tiers built on `AiTuning` (the JS has none).
3. IL2CPP release build; Player Settings **Company/Product name locked before any save is written**
   (it is baked into `%LOCALLOW%\<Company>\<Product>\` and into Steam Auto-Cloud config — changing it
   post-release orphans every player's save).
4. Steamworks.NET behind `ISteamServices` / `NullSteamServices`, **exactly one file may
   `using Steamworks;`**. Real AppID replacing 480 in `steam_appid.txt`, the VDF, and
   `RestartAppIfNecessary`.
5. **Art provenance audit.** `assets/structures/` holds 43 Warcraft II sprite rips and `assets/elements/`
   a Yu-Gi-Oh fan asset — never link into Unity, never ship. The 83 files under `assets/cards/` need the
   same audit. This gates Steam submission.
6. Balance pass now that flags are resolved.

---

## 3. Disposition of every JS file

`PORT` = translate the logic faithfully into C#.
`REBUILD` = the *behaviour* survives, the implementation is written fresh for Unity.
`DISCARD` = does not exist in the Unity build.

*(31 files — the "29 scripts" count in project notes predates the campaign split into three `10_*` files.)*

| # | File | L | Disposition | Destination / notes |
|---|---|---|---|---|
| 01 | `01_core_defs.js` | 31 | **PORT** | `Rules/Geometry/Board.cs`. Constants + `isLane` + `BASE_COL`. Drop `colReach` (dead in combat). |
| 02 | `02_art.js` | 77 | **DISCARD** | Procedural placeholder SVG generators. Replaced by an imported placeholder sprite table (M3/M13). |
| 03 | `03_cards_creatures.js` | 98 | **PORT → data** | Already exported to `cards.json` → `CardDefinition` assets (M3). The JS file stays authoritative until the HTML build retires. |
| 04 | `04_cards_leaders.js` | 224 | **SPLIT** | Commander table + `slugify` + dual generation → **PORT** (mind `MidpointRounding.AwayFromZero`). `mkCC`/`findCC` → **DISCARD** (dead). Art 404-walk chain + `probeSleeves` → **DISCARD**, replaced by `CardArtTable` resolved at import. ⚠ The *art resolution* logic named as `02_art.js` actually lives here at `:49-158`. |
| 05 | `05_board_state.js` | 91 | **PORT** | `Rules/Geometry` + `Rules/Economy/WorkerMath`. Delete `workerCap`/`structSupport`/`monsterUpkeep`. |
| 06 | `06_mana_workers.js` | 227 | **PORT** ⚑ | **The single most under-labelled file.** Mana, `CARD_REG`, `deckOf`/`expandDeck`, `mkCre`/`mkBld`/`mkVil`, `canBuild`, `placeBuild`, **and the entire keyword engine at `:96-185`**. Splits across `Economy/`, `Cards/`, `Effects/`. Delete `canTrain`/`enforceCap`/`trainVillager`. |
| 07 | `07_structures.js` | 100 | **PORT** | `Rules/Structures` + `Ai/Heuristics`. `upgradeTargets`, `upgradeWhy`, `applyUpgrade`, `bidLineage`, `toGrave`, `aiBuild`, `aiUpgrade`. |
| 08 | `08_battlefield.js` | 56 | **REBUILD** | Scenery is a real 3D layer (M13). The seeded-prop *idea* survives; the DOM injection does not. |
| 09 | `09_game_start.js` | 49 | **PORT** | `NewMatch`. ⚠ Do **not** reproduce "replace all row arrays with fresh instances" — that aliasing bug is designed out. |
| 10a | `10_campaign_dialogue.js` | 229 | **PORT → data** | 80 barks + 8 rival exchanges → `ElementBarkSet` / `RivalExchange` ScriptableObjects. Presentation **REBUILT** (M14). |
| 10b | `10_campaign_globe.js` | 223 | **SPLIT** | Goldberg GP(4,0) generation → **PORT** into an editor baker, index order frozen. Canvas renderer / `rot`/`unrot`/`P` / painter sort / culling / skirt quads / `R*EXH` picking → **DISCARD**. Orbit feel constants → **PORT** verbatim. |
| 10c | `10_menus_campaign.js` | 257 | **SPLIT** | `campGenMap`, `campResolve`, `campEndTurn`, `campAttackableTerr`, `campCapitalPrize` → **PORT** into pure C#. Screen show/hide, `display:none` hacks, `hideAllScreens` choke point → **DISCARD**. Menus → **REBUILD** (M15). |
| 11 | `11_deck_builder.js` | 255 | **REBUILD** | Rules (40 / max 3 / max 5 decks / filters / sorts / curve) → **PORT** as `DeckRules`. UI → UI Toolkit (M15). |
| 12 | `12_render.js` | 457 | **REBUILD** | Board view (M9/M13). `snapLegalCell`'s 44 px forgiveness → **PORT** as an explicit tunable feature. `renderMinions`, `workerChipRow`, `positionDeck`, `positionGrave` → **DISCARD** (dead). |
| 13 | `13_input.js` | 237 | **SPLIT** | `place()`, `handDeployOK`, `validSpellTarget`, the set/trap snapshot path → **PORT** into commands. Tap routing / action menu → **REBUILD** as the intent funnel (M9). |
| 14 | `14_spells_traps.js` | 134 | **PORT** | `Rules/Effects`. `resolveSpell`, `castSpell`, `flip`, charge funding, trap springing. Fix: validate before paying. |
| 15 | `15_combat.js` | 367 | **PORT** ⚑ | The M8 core. `rowsCrossedInto`, `untappedInterceptors`, `CMB.*`, `pairFight`, `targetFight`, `focusFire`, `resolveCombat`, `applyUndertow`, `springTrap`, `provokeFaceDown`, `aiChooseInterceptors`. `async` → step machine. |
| 16 | `16_movement.js` | 206 | **PORT** | `Rules/Movement` + `cleanup()`. Drop `moveChainOf`'s per-owner walk in favour of the owner-agnostic predicate. |
| 17 | `17_turns_ai.js` | 407 | **PORT** ⚑ | `Rules/Turn` (`startTurn`, `buildingUpkeep`, upkeep settle, `doHarvest`, `endTurn`, drain, `checkWin`) + `Ai/ScriptedAiPolicy` (`foeTurn` and friends). ⚠ `checkWin`'s direct call into `campResolve` is **inverted**, not ported. |
| 18 | `18_inspect_viewers.js` | 155 | **REBUILD** | Inspect panel, deck/GY viewer (M13/M15). Ability-text *generation* → **PORT** as `RulesText`. |
| 20 | `20_sfx.js` | 60 | **REBUILD** ⚑ | 23 synthesized cues → **23 re-authored .wav clips**. No assets exist. Spec 09 §16 is the audio design brief. |
| 21 | `21_fx.js` | 220 | **REBUILD** | 15 primitives + 9 `ELEMFX` element compositions → VFX Graph prefabs (M13). The compositions are hand-authored identity, not a generic particle system. |
| 22 | `22_fx_wrappers.js` | 327 | **DISCARD → table** | Zero rules changes (verified line by line). The **27-row wrapper table becomes the definitive `GameEvent` list** (spec 09 §18). Do not port the mechanism. ⚠ Its `aiMoveCreature` wrapper has a parameter-signature bug — do **not** reproduce it. |
| 30 | `30_resp.js` | 148 | **PORT** ⚑ | **This one genuinely changes rules timing.** Wraps `doAttack`/`attackBackRow`/`attackMinionStack`/`foeTrapOnSummon` and *replaces* `playerTrapOnSummon`. → `ResponseWindow` as core state, not UI. |
| 31 | `31_ui_shell.js` | 430 | **REBUILD** | `fitBoard` + `--extscale` → `BoardFramer`. Wall raise/retract → `WallState` enum. Drag-drop, RTS marquee, hover-inspect → Input System (M9). `cellUnder`'s snap → the same tunable forgiveness. Fullscreen/rotate/PWA → **DISCARD**. |
| 40 | `40_mp_net.js` | 197 | **DISCARD (deferred)** | WebRTC/ntfy transport. Netcode is layered on the command pipeline later, from scratch. |
| 41 | `41_mp_sync.js` | 104 | **REFERENCE** | `MPMAP` mirroring informs `StateMirror`. ⚠ Its owner re-stamp is a **bug** — mirror by transforming row index and flipping `Owner` independently. |
| 42 | `42_mp_apply.js` | 281 | **PORT → validators** ⚑ | **Take the host validators as the specification** — they are the complete predicate (slot bounds, type-vs-mode match, `placeRowOK`, covered-card ownership, `centerSlotOK`, explicit phase gate). They become `CommandProcessor.Validate`. |
| 43 | `43_mp_intents.js` | 199 | **DISCARD (deferred)** | The intent-capture *shape* is already the command pipeline. |
| 44 | `44_mp_lobby.js` | 244 | **DISCARD (deferred)** | Lobby UI + FX replay. |
| 99 | `99_boot.js` | 2 | **DISCARD** | `bootstrap()`. |

**Also discarded, called out so nobody spends a day on them:** `renderMinions`, `workerChipRow`,
`positionDeck`, `positionGrave`, `GUARDIAN_SVG`, `#conscriptBtn` (⚒ Train), the `#harvestPanel`
colour-allocation UI, the command-center card frame (`ccx`, COMMAND ribbon), the 32° `board-tilt` middle
angle, `canExtract`/`doExtract`/`extractSel`, `P.firstExtract`, `P.villagerUsed`, `P.cmana`,
`G.powerMode`, `G.deficit`, `harvestRow`/`applyHarvest` (unreachable in solo), and the entire
`.srdtest/harness.js` (stale — it asserts `colReach`, an on-board keep, coloured `cmana`, and `wardhp:2`).

**Not ported but not dead:** the `o.cc` command-centre guards. Keep an
`IsCommandCenter` flag defaulted false and implement every guard predicate, so a campaign boss keep can be
switched on later without archaeology.

---

## 4. The HTML build during the port — policy

**Recommendation: FREEZE the HTML build as a reference oracle at the start of M4. Maintain it for
bug-fix parity only until M12 completes. Retire it after M12.**

### Why not "keep developing it"
The HTML build is your design sandbox and it is genuinely useful for that. But every design change you
make in JS during the port has to be re-implemented in C# *and* re-specified, and — much worse — it
**invalidates the differential harness**, which is the only mechanism that proves the port is faithful.
The moment the JS and the C# are allowed to diverge by intent, every harness failure becomes ambiguous:
is that a port bug or a design change? That ambiguity destroys the harness's value entirely, and the
harness is the highest-value piece of test infrastructure in this project.

### Why not "delete it now"
Because M12 needs it *alive and runnable*. It is the only executable specification of ~30 accidental
behaviours (sort tie-breaks, iteration order, the AI's phase quirk) that no document fully captures.

### The policy, concretely

| Phase | HTML build status | Rule |
|---|---|---|
| M1–M3 | **Live, unrestricted** | Change whatever you like. The C# has no behaviour yet. |
| **M4 onward** | **FROZEN — reference oracle** | Only two kinds of commit: (a) a crash/blocker fix, and (b) `tools/export_cards.mjs` regeneration when card *data* changes. **No rules changes. No new features. No balance changes.** Each such commit re-runs the golden fixtures. |
| M12 complete | **Retire** | Tag the final commit `js-reference-final`. Move `src/js/`, `index.html` and `src/styles/` to `legacy/` and stop deploying Pages. Unity becomes the only client and the card-data flow inverts: `CardDefinition` assets become authoritative and `cards.json` becomes an export *from* Unity. |

### Two consequences to plan for

1. **You lose your mobile test surface.** GitHub Pages is currently the only way you test on a phone.
   Mobile is a locked later port anyway, but between M4 and M11 you have *no* playable build at all.
   This is the strongest argument for the milestone ordering above — **M8 gives you a terminal-playable
   game and M9 gives you an on-screen one, both well before the halfway point.** Get there.
2. **Design ideas need somewhere to go.** Do not lose them and do not implement them in the JS. Open
   `docs/unity/DESIGN_BACKLOG.md` on day one and write them there. Anything in that file is a candidate
   for M16, after the parity flags are resolved — which is the correct time to change the game anyway,
   because you will finally be able to A/B test a change against a provably-faithful baseline.

---

## 5. Top risks, ranked

| # | Risk | Why it is ranked here | Mitigation |
|---|---|---|---|
| 1 | **Silent behavioural divergence from the JS.** C# `List<T>.Sort` is unstable where JS `sort` is stable; `Dictionary` does not preserve insertion order where JS `Map` does; `Math.Round` is banker's where JS is half-up. This hits `focusFire`, `aiPickTarget`, `aiChooseInterceptors`, `applyUndertow`, Detonate targeting, `chain`'s top-two, the `byT` grouping, and all 36 commanders' worker counts. | It produces **no error and no test failure**. You find it months later as "the AI feels wrong". | Structural, not disciplinary: mandatory total-order comparators with a `(row, slot, unitId)` tiebreak; `OrderedMap`/`DamageBatch` instead of `Dictionary` wherever iteration is observable; `MidpointRounding.AwayFromZero` enforced by import validation V7; **and M12's differential harness as the actual proof.** Banned-symbol analyzer forbids `List<T>.Sort` outright. |
| 2 | **M12 slips.** The differential harness is the only mitigation for risk 1, and it is the easiest thing in the plan to postpone because it produces no visible progress. | If it slips past the JS retirement, "faithful port" becomes permanently unverifiable. | **Start M12 the moment M8 lands, not after M11.** Treat it as a hard gate on retiring the JS. Own it as infrastructure with a maintainer, not as a one-off script. |
| 3 | **M4/M5 architectural mistakes.** State holding object references, or per-player row collections, or `async` in the core, or an unseeded RNG. | Each one forces a rewrite of everything downstream, and each is *easy* to get wrong because it is the natural C# instinct. | The four gates from design 01 §1.3 (asmdef flag, headless build, banned-symbol analyzer, reflection architecture tests) plus the codec-coverage manifest test. Write the tests *with* the code, never after. **Board is `BoardObject?[35]`; ownership only on the object. This is non-negotiable.** |
| 4 | **Combat v3 (M8) is underestimated.** Two engines, two damage tiers, per-fight simultaneity, four suspension points, a resolver cursor in state. | It is the game's core loop and the most intricate thing in the codebase. Getting it 90% right is not useful. | Budget it as XL and expect to redo part of it. Gate it on spec 03 §15's two worked examples reproducing *exactly*, not approximately. Build the step machine before the rules, not after. |
| 5 | **IL2CPP is not installed** (verified) and neither is the VS2022 C++ workload. | No shipping build is possible until both land, and they are long downloads. | Started in **M1 step 8**, in the background, before anything else. Rule: any build a player touches is IL2CPP, including nightlies, so stripping regressions surface within a day rather than in ship week. |
| 6 | **Card art provenance.** `assets/structures/` is 43 Warcraft II sprite rips; `assets/elements/` is a Yu-Gi-Oh fan asset. The 83 files under `assets/cards/` are unaudited. | This gates **Steam submission**, not the port. Discovering it late means shipping is blocked by an art commission. | Audit now (M1-adjacent, it costs an hour). Never link `assets/structures/` or `assets/elements/` into Unity. Get the `assets/cards/` provenance answered as **open decision A3** before M13 invests in the art pipeline. |
| 7 | **The 20 `RulesOptions` parity flags become permanent.** | Twenty flags shipped are twenty untested rule combinations, and the register is exactly the kind of thing that quietly never gets closed. | The flag-count assertion in the ship-build configuration is the forcing function. **M16 cannot complete with a non-empty register.** Each flag resolution is a decision + code + test + deletion. |
| 8 | **Presentation scope (M13) balloons.** 23 sound cues from scratch, 9 hand-authored VFX compositions, a baked card-face pipeline, two castle walls, standees. | It is XL and it is the part with the least written-down specification of *quality*, only of behaviour. | Land M9's ugly-but-playable build first and live with it. Then attack M13 in the order: card faces → walls/HUD → standees → cell states → audio → FX. Each is independently shippable. The visual parity checklist (design 02 §15.1) is the definition of done — resist adding to it. |
| 9 | **Save-format instability.** Campaign saves have no migration (the JS deletes the old key), and Company/Product name is baked into the save path. | On Steam Cloud a bad save syncs across machines — a local annoyance becomes cross-machine data loss. | `SchemaVersion` + a real migration hook from the **first** save write (M14/M15), and Company/Product locked in M16 step 3 before any persistent data exists. |
| 10 | **The card-data three-layer drift** (JS registry → `cards.json` → `.asset` files). | Editing the JS and forgetting a step ships Unity stats that silently disagree with the reference oracle — which corrupts M12. | CI steps: `node tools/export_cards.mjs && git diff --exit-code docs/unity/spec/cards.json`, then `CardImportCli.Verify`. `tools/regen-cards.sh` makes the correct path the easy path. `CardDatabase.SourceHash` computed with `generatedAt` **excluded**. |
| 11 | **Art junction not recreated after a fresh clone.** | Unity imports an empty art folder, the importer finds no sprites, and the result looks like a data bug rather than a setup step. | `tools/setup-unity-links.mjs` is the documented **first line** of the README, and the importer emits a loud warning (not a silent null) when the junction is missing. |
| 12 | **Accessibility retrofit.** No keyboard, no gamepad, no focus indication, and colour as the only cell-state channel. | On a PC/Steam target this is a shipping blocker, and retrofitting input into a finished UI is far more expensive than designing it in. | Bindings and the shape channel are designed in **M9** (funnel) and **M13** (shape channel), delivered in M15 — not discovered at polish time. |

---

## 6. Open decisions

Every question the specs and designs left for a human. Grouped by **when it blocks work**.
Each has a **recommended default** — adopt them all and you have a coherent game; override individually.

### Tier A — decide before M4 (they shape the architecture or the pipeline)

| # | Question | Recommended default |
|---|---|---|
| A1 | Company Name / Product Name (baked into save path + Steam Cloud; changing it later orphans saves) | **`LucentLL` / `Spawn Row Duel`.** Lock now. |
| A2 | Steam AppID | Use **480 (Spacewar)** until a real one exists; make replacing it an M16 checklist item. |
| A3 | Provenance of the 83 files in `assets/cards/` — original, commissioned, or generated? | **Answer before M13.** If not provably yours, budget an art commission now. `assets/structures/` and `assets/elements/` are definitively not shippable. |
| A4 | When does the JS build retire? | **After M12.** See §4. This determines whether the card pipeline's one-way generator is permanent or scaffolding. |
| A5 | Keep the `divine` plumbing (4 creatures, Empyreum forge, no reachable path)? | **Yes** — import with `isPlayable = false`. It is the natural campaign-boss hook and costs nothing. |
| A6 | Hand-overrides for card art — forbidden, or an explicit `artOverride` field? | **Explicit `artOverride` field** the importer never touches. Silent slug-divergence is unmaintainable. |
| A7 | Canonical neighbour enumeration order | **Ascending row, then ascending column.** Pin it now, before any rule depends on "the first legal cell". |
| A8 | Delete `P.cmana`, `P.firstExtract`, `P.villagerUsed`, `G.powerMode`, `G.deficit`? | **Delete all.** They would bloat the serialized snapshot netcode depends on. |
| A9 | The `o.cc` command-centre guards | **Keep `IsCommandCenter` (always false today) and implement every guard.** Cheap, and preserves the campaign-boss hook. |
| A10 | Make the no-zero-cost rule a runtime assertion? | **Yes** — import-time validation, not a hand-maintained data invariant. |
| A11 | Should the dead 32° `board-tilt` angle be deleted from the JS before the port? | **Yes**, in the M4 freeze commit. The source should stop contradicting the locked two-angle decision. |

### Tier B — decide before M8 (combat semantics; each is a `RulesOptions` flag with a JS-parity default until then)

| # | Question | Recommended default |
|---|---|---|
| B1 | **Overcharge**: rescale the discharge ×500, or cut the keyword? | **Rescale to `oc*500`.** As written it grants +1..+3 against 1000+ HP bodies — i.e. nothing. It is a missed conversion, not a design. |
| B2 | Should retaliation use effective attack (incl. Overcharge) like attacker damage does? | **Yes.** Consistency; the current split is an oversight. |
| B3 | Scour bypass: per-attacker (v3) or group-wide (legacy)? | **Per-attacker.** The two paths disagree; v3 is the newer, deliberate design. |
| B4 | "Simultaneous damage": make it truly global (collect all packets, apply once, sweep once)? | **No — keep per-fight.** Real rules change with wide blast radius. Instead, **document the sequencing precisely in-game**; the current in-game text overpromises. |
| B5 | Should `checkWin` fire after each damage application? | **Yes.** Costs nothing today (wall damage is last) and removes a latent bug the moment resolution order changes. |
| B6 | Should a wall strike spring the defender's `trigger:'attack'` trap? | **Yes.** Today a committed response trap is dead against a pure wall rush, which makes the response window a trap for the player. |
| B7 | Should a blocked attacker push excess damage through after killing every blocker? | **No.** Blocking cancels the strike. Clean, teachable, and makes chump-blocking meaningful. |
| B8 | Should worker-stack strikes use the tiered v3 model instead of legacy `focusFire`? | **Yes, eventually** — but flag it and defer past M12. Two engines producing different outcomes for the same board is invisible to the player and indefensible long-term. |
| B9 | Is "one block per creature per opponent turn, regardless of tapped/sick" intended? | **Yes** — port exactly as written, with a prominent comment. The source comment says it is deliberate; the function name lies. |
| B10 | Should a provoked face-down flip in summoning-sick? | **No** — it fights at full power. Meaningful tempo rule; keep it. |
| B11 | Should attacking a set trap deal damage to anything? | **No.** "The trap springs and is removed, no damage exchanged" is intended and readable. |
| B12 | AI retaliation target when jointly attacked — still hardcoded to attacker index 0? | **Give it a real heuristic** (retaliate against an attacker it can kill, tie-break lowest HP). Index 0 is trivially exploitable. |
| B13 | AI's gang-block absorber: dump on the toughest blocker when none is killable? | **Change to weakest.** Chip damage that leaves a body alive is strictly worse than progress toward a kill. |
| B14 | `aiPickTarget` rolls the 60% face-down check **before** the guaranteed-kill check | **Reorder** — take the free kill first. |
| B15 | AI wall-defence threshold `P >= 4` is dead at ×500 | **Re-express as policy**: block if incoming ≥ 20% of remaining life, or is lethal. |
| B16 | Double-KO scores as DEFEAT. Add a Draw? | **Yes, add `MatchOutcome.Draw`** — but score it as a campaign loss so no campaign logic changes. |
| B17 | Multi-row joint attacks: solo allows them, MP rejects them | **Solo is canonical.** Multi-row joint attacks are legal. |
| B18 | Should the anti-tell response window exist in a PC single-player build? | **Yes, but default it OFF in solo** (keep Off/3/4/6 in settings). There is no opponent decision behind the pause; on PC it reads as lag. Locked ON at 4 s whenever netcode arrives. |

### Tier C — decide before M11 (rules/AI behaviour; also parity flags)

| # | Question | Recommended default |
|---|---|---|
| C1 | Should `eff:'wall'` structures (Bulwark ♥6000, Bastion ♥9000) actually intercept? | **No — reword the cards.** Structures have no `a`, `tapped` or `blocked` fields and the retaliation model assumes blockers strike back. Bulwark then needs a real ability; give it one in M16. |
| C2 | Should `eff:'villager'` (Longhouse/Barracks) train workers? | **Delete the effect.** Give both a non-zero `sup` instead — `sup` is already the whole story. |
| C3 | Should `tower` gain `from:'outpost'`? | **Yes.** It is a data inconsistency (`bastion` has it), and the importer should validate `up2`/`from` symmetry so it cannot recur. |
| C4 | Is the total absence of structure repair intended? | **Yes for now.** Damage carrying through an upgrade is a good cost. Revisit in M16. |
| C5 | Should the human player get the AI's per-structure caps? | **Yes.** Nothing today prevents a Foundry-spam opening. |
| C6 | Grand Forge is both a ◆6 direct build and a ◆6 upgrade from a ◆3 Forge (◆9 total) | **Remove the direct build** from `buildList`. |
| C7 | Is `longhouse.row:'front'` meant to gate direct builds too? | **Yes.** Enforce the row gate on builds as well as upgrades. |
| C8 | Should summon-triggered traps honour `card.effect`? | **Yes.** All three current summon traps are `pitfall` so nothing changes today, but the data model implies the intent. |
| C9 | Is `raze`'s unconditional HP-ignoring destruction intended? | **Keep it** — it is the answer to a 9000-HP Bastion and the only such effect. Raise its cost to ◆4 in M16 if it proves oppressive. |
| C10 | The AI's `raze` targets the *last* structure found (missing `break`) | **Real heuristic**: highest `sup`, then highest cost. |
| C11 | Should the AI get `doHarvest`'s structural-remainder fallback? | **Yes.** Symmetry; it can currently carry an unpayable shortfall forever. |
| C12 | AI re-runs `readyWorkers` after settling; the player does not | **Remove the AI's extra call.** It is a straight AI advantage arising from a duplicated line. |
| C13 | Should the upkeep second move be upkeep-only for **both** sides? | **Yes.** The AI's permanent two-move allowance is an accident. Express the phase condition explicitly, never via a global flag. |
| C14 | Should the AI be able to retreat from the enemy **back** row? | **Yes.** Model the retreat graph on real row adjacency (foeBack → foeFront → center), not the zone graph. Today it sacrifices deep raiders. |
| C15 | Should the AI cast `chain`/`bounce` and set creatures face-down? | **Yes, add both.** Those cards are dead weight in its hand forever. |
| C16 | Should `placeRowOK` be enforced on structures played from hand into your own rows? | **Yes.** Unreachable today, but the host path already does it and `place()` supports the mode. |
| C17 | Is `flip()` skipping `syncWorkers` on the structure branch a bug? | **Bug — fix it.** Resync on both branches. |
| C18 | Face-down snapshots omit `color`, so a flipped creature inherits the player's element | **Bug — fix it.** One-field change; art, FX and future synergy all read wrong today. |
| C19 | Is `toGrave` restoring `h` to `maxh` intentional (the graveyard heals)? | **Intentional — keep.** The Reliquary reviving a full-HP creature is a fine, legible rule. |
| C20 | Should `Thornmail`'s permanent +500/+1000 be trackable/removable? | **Yes** — model it as a tracked modifier with a source. It stacks indefinitely today with no provenance. |
| C21 | Is the absence of a deck-out loss intended? | **Yes — no deck-out.** Life is the only loss condition. Drawing from an empty deck stays a silent no-op. |
| C22 | Is the center's lack of lateral movement intended? | **Yes — make it explicit and document it.** It is emergent from lane spacing and is a good rule. |
| C23 | Should `startMove` gate on phase explicitly? | **Yes** (`upkeep` or `action`). Every UI path already does; a direct command would not. |
| C24 | Should tribes/subtypes gain mechanical weight? Keep the unused `Human` tribe? | **Keep as flavour + deck-builder filters.** Drop `Human` if nothing uses it by M16. |
| C25 | Does `raid` span both enemy rows for labels/UI as well as arithmetic? | **Yes, both rows.** Make `RowsOfZone` the only API; `zoneKey` singular is deleted. |
| C26 | Is a hand-size limit intended? | **No limit** — the anti-hand-dump rule is the ◆1 set cost, which already works. |
| C27 | Should the AI's center deploy preference `[3,1,5]` exist at all? | **Delete it.** Creatures cannot be summoned into the center; structures reach it only via `placeBuild`. |
| C28 | Resurrect the dead deployment helpers (`ownRows`, `canDeploy`, `MINE`, `hasEmptyDeploy`)? | **Drop them.** `ownRows` implies creatures may deploy to the center, which is false. |
| C29 | Are center-flank structures meant to feed the owner's `center` worker zone and be raidable? | **Yes, both** — that is the current behaviour and it reads correctly. |
| C30 | What difficulty tiers does the Steam release want? | **Three: Recruit / Veteran / Warlord**, moving only `AiTuning` knobs (target probabilities, summon/build caps, block threshold, whether attackers are held back). No stat cheating. |

### Tier D — decide before M13/M14/M15 (presentation and campaign)

| # | Question | Recommended default |
|---|---|---|
| D1 | Should the AI's declarations be rendered on the board? | **Yes.** The design calls for visible alternating declarations; the browser silently never painted them. |
| D2 | Damage numbers: world-space or screen-space? | **World-space, billboarded**, with per-cell stack offset and a 4-number merge cap. |
| D3 | Port the command-center card frame? | **No** — leader identity only (name, element, life, workers) in the left tower. A boss keep would be a new prefab. |
| D4 | Right-click as the global inspect gesture? | **Yes**, retaining the 180 ms hover delay as a second route. |
| D5 | Colourblind secondary channel for cell state | **Required.** Shape/pattern channel in the board shader, exposed as a setting. |
| D6 | Is the `#harvestPanel` colour-allocation UI truly dead? | **Yes — delete.** Confirm coloured mana will never return; the digest says it was removed deliberately. |
| D7 | Keep the multiplayer presentation hooks (`IsInputFrozen`, modal countdowns, post-state notification)? | **Yes, keep the neutral hooks** from day one. Leave the MP policy unimplemented. Re-touching every view file later is far worse. |
| D8 | Standee toggle — remove on PC, or persist? | **Persist it.** Tilted mode force-enables standees; the toggle only matters in Top-Down. |
| D9 | `applyCharacterUI` sets element tints **and** writes rules prose | **Split**: element theming is view; rules text is generated from card data in the core. |
| D10 | Should garrison affect the duel at all? | **Yes — make it matter.** Recommend: garrison ≥ capital threshold grants the defender +1 opening mana and one free Foundry. It is displayed everywhere, grows every turn, and currently does nothing. |
| D11 | Should an assault cost something? | **Yes — one assault per campaign turn.** Today a player can conquer all 22 territories without ever pressing End Turn. |
| D12 | Is a failed assault meant to reduce the defender's garrison by 1? | **No — invert.** A repulsed attack should not reward the attacker. Give the defender +1 instead. |
| D13 | Should AI empires absorb capitals and snowball? | **Yes.** Otherwise no rival can ever threaten you and the campaign has no pressure. |
| D14 | Should losing your own capital do anything? | **Yes** — lose the dual-banner unlocks it granted until retaken. |
| D15 | Should the player bring a saved custom deck into a campaign battle? | **Yes.** Currently the deck is always freshly rolled, unlike solo — an inconsistency players will read as a bug. |
| D16 | Should the enemy scale with progression? | **Yes** — capitals field the defender's dual commander (once absorbed) and a hand-built deck; ordinary territories keep the rolled mono deck. `BattleLaunchRequest` already carries `TerritoryId` for exactly this. |
| D17 | Is `divine` a late-campaign boss? | **Yes** — one Divine "God" territory unlocked after 18/22 territories. Keeps the plumbing honest. |
| D18 | Should territory (22) / empire (8) counts scale? | **Fixed.** Do not add a variable. |
| D19 | More than one campaign save slot? | **Three slots.** With `SchemaVersion` and migration from the first write. |
| D20 | A persistent post-battle log / history on the map? | **Yes, small** — last 20 events. Cheap and adds a lot of campaign texture. `CampaignState.battleAs` (currently written and never read) is the natural place to record which banner fought. |
| D21 | Confirm design 02's 11 view decisions (square cells, perspective Top-Down, wall props, baked card faces, shader cell states, rendered AI declarations, world-space numbers, right-click inspect, persisted settings, in-world board drags, globe wheel zoom) | **Accept all 11.** They are individually defensible; the only collective risk is look-and-feel drift, which the visual parity checklist catches. |

---

## 7. The next hour, concretely

1. Unity Hub → Create project → name **`unity`**, location **`C:\Users\mcgee\code\RTS-Card-Game`**,
   template **Universal 3D**, editor **6000.5.5f1**, Cloud OFF, Version Control OFF.
2. In Unity Hub → Installs → 6000.5.5f1 → Add Modules → check **Windows Build Support (IL2CPP)**.
   Start the download and leave it running. Separately, start the Visual Studio 2022 installer and add
   **Desktop development with C++**.
3. Verify: `cat unity/ProjectSettings/ProjectVersion.txt` → `6000.5.5f1`;
   `grep render-pipelines.universal unity/Packages/manifest.json` → a hit.
4. Editor → Project Settings: Asset Serialization **Force Text**, Version Control **Visible Meta Files**,
   API Compatibility Level **.NET Standard 2.1**.
5. Apply the `.gitignore` block from design 03 §4.2 **including both negation guards**; add
   `.gitattributes` from §4.4.
6. `git add` + commit: `"Unity 6 project scaffold (URP) at unity/; Git config for Unity YAML"`.
7. Create `docs/unity/DESIGN_BACKLOG.md` with one line in it, so every design idea you have between now
   and M16 has a home that is not the JS build.
8. Answer decisions **A1** and **A2** in this document (Company/Product name, AppID placeholder). They
   take two minutes and are expensive to change later.

Then M2. Do not start writing rules code before the assembly boundary and `dotnet test` are green.
