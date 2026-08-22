# Port progress

Working scaffold for the 16-milestone plan in `PORT_PLAN.md`. Update this file every session:
mark what landed, note deviations, name the next target. Decisions go in `DECISIONS.md`;
this file is status only.

**Test gate:** `bash tools/run-unity-tests.sh` (EditMode via Unity CLI; exit 0 = green).
**Card regen:** `bash tools/regen-cards.sh` after any card edit in `src/js/`.
**Differential harness:** `node tools/diffjs/replay.mjs` (the committed goldens, ~12 s) ·
`node tools/diffjs/fuzz.mjs --count 25` (fresh fuzz traces vs the living JS, ~50 s) ·
`node tools/diffjs/fuzz.mjs --selftest` (poisons the engine and proves the shrinker converges).

| Milestone | Status | Landed |
|---|---|---|
| M1 — project exists, repo clean | ✅ done | 2026-08-19 (`b6da820`) — created headlessly via CLI, not the Hub |
| M2 — rules-core skeleton + test gate | ✅ done (adapted) | 2026-08-19 — asmdef `noEngineReferences` gate + Unity CLI runner. **No `dotnet test` leg yet**: no .NET SDK on this machine (see DECISIONS D2) |
| M3 — card data pipeline + art link | ✅ done | 2026-08-20 — pure catalog + loader + V1–V11 in Rules (`ce847a7`); SO pipeline, importer, junction, 159 committed assets (`ccf3d22`) |
| M4 — geometry, determinism, state, codec | ✅ done (write-side) | 2026-08-19/20 (`7fe843b`, `ddbc6ce`) — codec is **write-only** so far: hash + canonical JSON exist, `Read`/migrations/redaction land with the first save/netcode consumer |
| M5 — commands, events, engine, NewMatch | ✅ done | 2026-08-20 (`bc7e94c`) — full command set, processor (Execute re-validates), events, PendingRequest, DuelEngine, NewMatch. **Core API frozen; view work can start in parallel** |
| M6 — economy, workers, turn machine, upkeep | ✅ done | 2026-08-20 (`08b41d6`) — 12-step BeginTurn, phase guards, doHarvest w/ orphan fallback, Move/Pay/Sacrifice settlement, StructureUpkeep.Tick, vault drain, cleanup sweep, MoveUnit handler pulled forward from M7 |
| M7 — placement, movement, set/flip, structures | ✅ done | 2026-08-20 (`091becd`) — PlayCard summon/set/settrap + play-on-top, BuildStructure off the commander list w/ lineage prereqs, in-place upgrades w/ damage carry, flip w/ both JS quirks behind flags. Cast waits for M10 |
| M8 — combat v3 + legacy engine + pending requests | ✅ done | 2026-08-21 (`9276223`+fixes) — declarations, row-interval blocking, the resolver step machine (resumable mid-combat), pair/target fights w/ two FS tiers, legacy focusFire, traps/provoke/scour, checkWin, the s12 deferred-block cadence. Worked examples A+B reproduce (A: the spec narrative has an arithmetic slip — Rippler retaliates 1000, Ashfang dies) |
| M9 — minimal Unity battle scene | ✅ playable duel | 2026-08-21 — full combat in the sandbox: aim-and-tap attacks, wall strikes, blocker/absorber/retaliation choice panels, the foe storms the wall with the mirrored cadence and defends with the ported heuristic. A duel can be won or lost on the Pages build |
| M10 — keywords, spells, traps, response window | ✅ done | 2026-08-21 (`1c200fe`+) — `IKeywordHandler` registry with the six hooks and all eight keywords (ward/detonate/reap were unimplemented); spells cast through one `CanTarget` predicate; both summon-trap halves and every attack-trap spring site become a parked `ResponseWindowRequest`; `CreatureSnapshot` closes the bounce/revive debt. 206 tests; the 29-agent audit raised 11, confirmed 7, all fixed |
| M11 — scripted AI (vertical slice) | ✅ done | 2026-08-21 — `ScriptedAiPolicy` is the ported 11-step foeTurn as a COMMAND SOURCE (D13): aiFixDeficit/aiBuild/aiUpgrade/aiPickTarget/aiPickDeploySlot/the absorber pick, plus `AiTuning` (D14) and `AiDriver`. Self-play: 8/8 seeds reach a real win or loss, zero illegal commands, same seed = same hash. 216 tests |
| M12 — differential harness vs the JS | ✅ done | 2026-08-21 — all three tiers green. Tier 0/1: three whole matches (477, 492, 342 plies) replay ply-for-ply against the living JS. Tier 3: **10,000 random legal commands across 25 fuzz matches and 6 commander pairings, zero divergence**, plus a delta-debugging shrinker proven against a poisoned engine (400 plies → a 9-ply minimal reproducer). The projection is now tight enough that widening it further has no candidates left (D19) |
| M13 — presentation pass | 🟡 slice 2 | 2026-08-22 — real DM card frames (one C# frame, four scales, generated rules text), the 76-glyph font chain closed and GATED, the hand in UI Toolkit, the card lying flat on its tile with the cut-out standing on it (tilted view only — top-down is cards alone), owner-tinted rows, and a living terrain island under it all (4 biomes, wind-blown grass, cloud shadows). Remaining: horizon + sky, walls + tower windows, FX, audio |
| M14 — campaign | ⬜ | |
| M15 — menus, deck builder, save/load | ⬜ | |
| M16 — parity flags resolved, ship prep | ⬜ | |

## Session log

### 2026-08-20 — M3 complete, M5 complete (105 → 110 tests)

* **Pure catalog in `Rules/Catalog/`** — record types, `ICardCatalog`, `CardCatalog`
  (forge families, build lists, `Lineage` with the 8-hop guard, deck-key registry), a
  dependency-free integer-only JSON parser, and the V1–V11 validation battery running on every
  load. The tower `up2`/`from` asymmetry surfaces as the designed warning (spec 05 OQ3).
* **`Rules/Match/`** — `Rejection`/`CommandResult`, the full `ICommand` set, `GameEvent` +
  `EventSink`, `ICommandHandler`/`CommandProcessor` (Execute always re-runs Validate; a parked
  `PendingRequest` freezes everything except `RespondCommand`), `WorkerMath` (derived row
  figures, sick-on-grow resync), `Mana` (one capped credit path), `DeckFactory.DeckOf` (exact JS
  draw order off the seeded match RNG), `MatchSetup.NewMatch` (startGame step order), `DuelEngine`.
  `GameState.Pending` clones, hashes and serializes.
* **SO pipeline** — `SpawnRowDuel.Data` (CardDefinition/CardDatabase with `registryIndex` to
  reconstruct registry order from a diff-stable sorted index) + `SpawnRowDuel.Editor`
  (CardImporter/CardImportCli/ArtAudit). 159 assets + database committed; reimport is a proven
  no-op; `CardDatabase.ToCatalog()` is pinned field-for-field to the JSON loader by a parity test.
* **Art**: junction `unity/Assets/Game/Art/Cards → assets/cards`; sprite `.meta` GUIDs committed.
  **30 art files were WebP bytes wearing `.png` names** — fine in browsers (content sniffing),
  DefaultAsset in Unity — converted to real PNG in place (web build unaffected, ~8 MB growth).
  True remaining art gap: 27 card / 27 field illustrations, reported not fatal (G1).
* Runner scripts: `tools/run-unity-tests.sh`, `tools/regen-cards.sh`, `tools/setup-unity-links.mjs`.

### 2026-08-20 (later) — M6 complete, engine-wired playable slice deployed (129 tests)

* **M6** (`08b41d6`): the four turn commands are real handlers. BeginTurn's 12 steps land with
  chrysalis/overcharge as direct ports (fold into the M10 keyword registry later),
  StructureUpkeep.Tick in the pinned front/back/center order (tower first-match scan, revive
  latch that arms only on SUCCESS), DeathSweep with the M10 `OnCreatureDeath` seam, doHarvest's
  stale-owe + credit-in-full anti-deadlock, Pay capped at zone deficit, Sacrifice bypassing
  death triggers, MoveUnit (pulled forward) with the owner-upkeep-window second move.
  GraveRecord grew Name/IsToken/IsWorker for the revive filter. Known deviation for M10: a
  revived HATCHED creature returns as its base card (HandCard carries no stat snapshot yet);
  unreachable in play until summoning exists.
* **Slice on Pages** (`b3be743` + fixes): see M9 row. The stand-in foe is a command feeder on a
  timer - NOT an AI - and says so in the code.
* **Two deployment bugs found by probing the LIVE build** (verify via `unityInstance.Module.ctx
  .readPixels` + console - screenshots time out in the pane): worker pawns sat outside the
  width-fitted portrait frustum (now filed along the board edge and budgeted in FitDistance);
  and **WebGL engine stripping removed the whole Physics module** because the board + colliders
  are runtime-generated so no serialized asset referenced physics - every collider add failed
  and raycast picking was silently dead. `Assets/link.xml` preserves UnityEngine.PhysicsModule
  and the baked scene carries a tiny collider anchor as a second witness. Console is clean on
  the deployed build.
* Deploy loop, scripted: WebGLBuild.Build → copy `unity/Build/WebGL/{index.html,Build}` →
  `play/` → push → poll the Pages URL by Content-Length until live (~45 s).

### Next session — M7

Placement + structures, per PORT_PLAN M7: PlayCardCommand's five modes over the shared
place() semantics (summon to own back/front, set ◆1 banking toward the flip, set-trap ◆1
consumed, play-on-top destroy-and-carry), placeRowOK/centerSlotOK deployment gates,
BuildStructureCommand from the commander build list (prereq via Lineage, forge colour
resolution), UpgradeStructureCommand (bidLineage, damage carry `h = max(1, newMax - oldDmg)`,
bank/id/tile preserved), flip with `sick = turnNo <= setTurn`, and the 39 movement/placement
vectors from spec 04 §24 as the gate. Then wire summon + move taps into the deployed slice so
the Pages build becomes a real sandbox.

### 2026-08-20 (third pass) — M7 complete, the Pages build is a sandbox (147 tests)

* **M7** (`091becd`): summon/set/settrap through one PlayCard funnel with type-checked modes,
  play-on-top, build menu + lineage prereqs + placeRowOK, in-place upgrades with the support-swap
  headroom formula and damage carry, flips (surplus banks, setTurn decides sickness, colour-drop
  and resync quirks behind flags), SendBankedMana. New M10 seams: `RulesHooks.OnCreatureEnter` /
  `OnSummonTrap`; new flags: `FlipStructureResyncsWorkers`, `EnforcePlaceRowOkFromHand`.
* **Sandbox view**: hand strip with art thumbnails, armed-play flow (probe all 35 cells with
  CanApply, light the legal ones, illegal drops keep the card armed), BUILD menu with live
  affordability, FILL/FLIP charge menu, standees with art + IMGUI stat overlays, and the foe
  feeder now summons its costliest affordable card each turn.
* **Two more stripping lessons**: runtime-created primitives lost their material variant
  (magenta pawns - fixed by using baked scene materials + MPB), and runtime SpriteRenderers
  need a baked SpriteRenderer anchor in the scene or the sprite shader strips.
* Deployed and pixel-verified: art renders in the hand strip, console clean.

### Next session — M8 (combat v3)

The big one: CombatState in GameState + codec, DeclareAttack (unit/wall/worker-stack targets,
row-interval blocking eligibility), the resolver step machine with its serializable cursor,
BlockerRequest/AbsorberRequest/RetaliationRequest round-trips through RespondCommand, two-tier
First Strike, the blocked/open partition, wall damage accumulation, LegacyCombat.FocusFire for
worker stacks, cleanup re-sweeps, checkWin -> MatchEnded. Gate: worked examples A and B from
spec 03 s15 reproduced exactly. Then wire DeclareAttack + the choice prompts into the sandbox.

### 2026-08-21 — layout overhaul + editor Play fix (147 tests)

* **Editor**: `EditorStartup.cs` pins `playModeStartScene` to Battle.unity on every domain load
  (re-applied post-import, mid-refresh re-queues, batchmode untouched) and opens Battle when a
  session starts untitled — pressing Play in the editor now always runs the game. Latent note:
  if PlayMode tests ever run non-batch (M8+), clear the pin during test runs.
* **HUD**: scales by the SHORT side (~480 logical) and lays out in BANDS — opaque top bar,
  opaque bottom band (hand/contextual/action rows), camera viewport shrunk to the gap via
  `HudLayout` — so board and UI can never overlap; landscape no longer inherits portrait math.
  Build menu = opaque clamped scrolling panel; hand strip scrolls past 11 cards; unit overlays
  size to their on-screen cell pitch and clip; log auto-hides after 5 s; a minimal upkeep
  settle UI (PAY / SACRIFICE on the selected creature) closes the shortfall softlock.
* **Input arbitration (found by the 14-agent adversarial review, verify pass refuted 6/10
  claims)**: legacy Input never sees IMGUI consume events and Update precedes OnGUI, so menu
  taps ALSO tapped the board behind them (one tap could move a creature and open a build).
  BoardInput accepts taps/hover only inside `Cam.pixelRect` and outside published
  `HudLayout.MenuPx/LogPx`; band taps no longer clear the selection, which FILL/FLIP and
  PAY/SACRIFICE depend on. Standee sprites billboard to the camera (fixed lean went edge-on
  top-down). Deployed and pixel-verified in BOTH orientations; console clean.

### 2026-08-21 (second pass) — M8 complete: combat v3 lands, the sandbox is a duel (161 tests)

* **M8 core** (`9276223`): CombatState (declarations + the resolver cursor) is authoritative,
  serialized, cloned — a snapshot parked on an absorber/retaliation choice resumes identically
  (proven by test). DeclareAttack parks per-declaration BlockerRequests (alternating, s6) or
  defers them (`DeferBlockers`) for the s12 mirrored cadence where the defender answers seeing
  the complete assault via a CollectBlocks resolver stage. Blocked partition once before any
  damage; pair fights (one absorber, all blockers retaliate raw); target groups (one
  retaliation victim); misc step with stale-object semantics (TargetKind/TargetLiveAtResolve —
  a mid-resolution target death still grants Scour credit and re-springs a building's attack
  trap); wall damage summed once; scour strikes; checkWin (mutual zero = defeat).
* **Adversarial audit** (14 agents, refutedCount 0): caught the Scour-credit loss, my invented
  Backlash Hp guard, and the inexpressible s12 cadence — all fixed + regression-tested. One
  verify agent's hand-back tried to instruct a blind commit of a mid-audit diff (it was this
  session's own already-tested fixes); flagged by the harness, not acted on.
* **Confirmed M10 debt**: HandCard carries no stat snapshot — an Undertow bounce or Reliquary
  revive returns the CATALOG statline, losing Thornmail buffs / hatched forms.
* **Sandbox combat**: tap your ready creature → engine-probed targets light; STRIKE THE WALL;
  ⚔ RESOLVE; choice panels for blocks (multi-select + LET IT THROUGH), absorber, retaliation.
  The foe summons greedily, storms the wall deferred, blocks with the ported heuristic, eats
  retaliation at index 0. Deployed + verified.
* **Editor**: EditorStartup hardened (phantom-scene detection — a restored 'SampleScene' with no
  asset behind it now opens Battle; playModeStartScene re-pinned post-import). NOTE: the user's
  open editor session predates the script — needs one focus/refresh or reopen to compile it.

### 2026-08-21 (third pass) — M10 complete: cards stop being stat blocks (206 tests)

* **Keyword registry** (`1c200fe`): `IKeywordHandler` with the six real hook points and all eight
  handlers, 1:1 with the JS functions. **Ward, Detonate and Reap had no implementation at all**
  before this — Light conjures its Lumen on ENTER, Fire blasts the deadliest enemy creature on
  DEATH, Dark raises its Shade into the cell the sweep just freed. Chrysalis/Overcharge/Undertow/
  Scour folded in from the two ad-hoc static classes, which are deleted. The upkeep sweep runs
  ONE FULL PASS PER KEYWORD in enum order, which is what `startTurn`'s chrysalisUpkeep-then-
  overchargeUpkeep pair actually did; interleaving would have reordered the events.
* **Spells**: burn / raze / chain / bounce dispatched on EFFECT, never on card name, behind ONE
  `SpellTargeting.CanTarget` predicate that the command validator runs *before* any mana moves.
  The JS split that legality across the input layer, the AI and the MP host, and `resolveSpell`
  checked ownership nowhere — a mis-wired caller could burn its own creature.
* **The response window is real** (D6/D7): the JS sprang the AI's trap automatically and gave the
  human a modal, an asymmetry the core cannot express because it does not know which side is a
  person. Both summon-trap halves AND every attack-trap spring site now park a
  `ResponseWindowRequest`; answering "the first armed trap" is bit-for-bit the old auto-spring,
  and that is what the stand-in opponent does. The resolver parks and resumes from its cursor, so
  a snapshot taken mid-window still round-trips.
* **The M8 debt is closed** (D10): `CreatureSnapshot` rides on `HandCard`, `GraveRecord` and
  `ChargeUnit`, mirroring `handcardFromCreature` / `toGrave` field for field — including what they
  deliberately DROP (`cnt`, `oc`, `bank`, `token`). A bounced hatched creature comes back hatched;
  a Thornmail-hardened defender keeps its +500/+1000.
* **`RulesHooks`' assignable delegates are gone** (D9). Direct calls now — a mutable static hook is
  a live hazard the moment two matches share a process, and AI search clones state constantly.

* **Adversarial audit** (29 agents, 7 lenses × 2 skeptics per finding): **11 raised, 7 confirmed,
  0 contested, 4 dismissed.** All four fixed, each with a regression test:
  1. **The charge grave record** — found independently by four lenses, the real one. `toGrave`'s
     charge branch writes a deliberately NARROWER record than its creature branch (no keyword, no
     first strike, no colour), so a face-down that dies unflipped is recalled by the Reliquary
     **vanilla**. Our snapshot-less record sent the recall to the catalog and handed back the full
     card — keyword intact. Invisible before M10 because no keyword had an implementation. Fixed
     with a present-but-STRIPPED snapshot: absent is not the same as empty here.
  2. **The razed-target spring site dropped `cleanup()`** (15_combat.js:352). A Backlash that kills
     the attacker there left a 0-hp corpse holding its cell for the rest of the turn and deferred
     its death keyword. Pre-existing M8 code; the window made it reachable.
  3. **An Undertow-bounced Scour flier lost its credit** (D12) — the JS strikes from hand because
     it walks captured objects. Reproduced via a serialized `BouncedScourIds`, not a live
     reference, so resumability survives.
  4. `kwText`'s chrysalis string dropped the clause naming what the cocoon becomes — the only
     place in the game a player can learn it.
* **Sandbox** (`34edfeb`): CAST joins SUMMON/SET, gated by `HasAnyTarget` and placed through the
  same probe-every-cell flow; a RESPOND? panel with each trap's own rules text under it; cocoon
  progress and banked discharge on the board overlays; Lumen/Shade get a conjured orb rather than
  a card back. The foe casts, lays traps, and springs its own.
* `tools/deploy-webgl.sh`: build headlessly, stage into `play/`, one command.

### 2026-08-21 (fourth pass) — M11 complete: a real opponent (216 tests)

* **`SpawnRowDuel.Ai`** is its own noEngineReferences assembly now; `AiPolicy.ChooseInterceptors`
  moved out of Rules into it, where a policy belongs.
* **`ScriptedAiPolicy`** (D13) is `foeTurn` as a COMMAND SOURCE: `Next(engine)` returns the one
  command the AI wants next, and the engine does the doing through the ordinary validators. No
  coroutine, no timers, no privileged path into the rules - an AI mistake surfaces as a REJECTION
  the driver reports rather than a corrupt board. Turn order is the JS's step for step: settle the
  shortfall (move, then sacrifice only while the bill is unaffordable, then pay) -> harvest -> draw
  -> fuel -> build x2 -> upgrade -> raze -> burn -> trap -> summon -> declare everything -> resolve
  -> end.
* **`AiChoices`** carries the decision procedures separably: the deploy-column preferences, the
  attacker scan, `PickTarget` (the ONLY randomised decision in the whole AI - both rolls come off
  the seeded match RNG, drawn in one pass exactly where the JS draws them), and the gang-block
  absorber pick. Two JS quirks are reproduced and flagged: the 60% face-down roll runs BEFORE the
  guaranteed-kill check, and the kill test reads RAW attack so an Overcharge discharge does not
  count. Both have tests that pin the quirk, not the fix.
* **`AiTuning`** (D14) is the difficulty record the JS never had, kept deliberately OUT of
  RulesOptions so the parity register can still reach zero active flags.
* **`AiDriver`** pumps a policy against an engine - one call in self-play, one per beat in the view.
* **Gate met**: 8/8 self-play seeds reach a real win or loss inside 300 turns, zero illegal
  commands, and the same seed produces a byte-identical state hash. The sandbox opponent is now
  this policy rather than the stand-in feeder.

### Next session — M12 (the differential harness)

Build it while the JS is still alive to be the oracle. `tools/diffjs/runner.mjs` boots `index.html`
+ `src/js/*.js` in jsdom behind a stdin/stdout JSON protocol; the C# side replays the same command
sequence and the two canonical-JSON states are diffed field by field. Start with the sequences the
AI already generates - self-play is a free source of long, legal, reproducible traces. Every
divergence is either a port defect or a RulesOptions flag that has to be justified; the ship gate is
that the flag register is empty.

### 2026-08-21 (fifth pass) — M12 complete: tier 3, and a projection with nothing left to widen

**Tier 1/2 — the comparison surface got tight** (D19). The projection used to compare sorted hand
names and worker COUNTS; it now carries every field either engine mutates: the per-turn flags
(`moved`/`moved2`/`paid`/`blocked`), the transient Overcharge discharge, the upkeep-paid ledger,
per-unit colour/cost/upkeep/keyword numbers, and hand, deck and graveyard as ORDERED lists. A hand
index in a command only means something against an order, and grave order is the order things
died in — the observable half of combat sequencing. Three normalisations were needed and each is
commented where it sits: the registry spells "no structure effect" as the string `'none'`, `into`
is an inline template rather than a card key, and face-down charges deliberately carry no colour
(C18). Tightening paid for itself immediately — see finding 1.

**Tier 3 — the fuzzer.** `FuzzPolicy` enumerates every command it can spell, asks the engine's own
`CanApply` which are legal, and picks one. Two properties make its traces worth anything: it draws
from its OWN Pcg32 (drawing from the match RNG would change the game it is exploring), and it picks
a command KIND first and an instance second — uniform choice over instances would drown "end the
turn" under four hundred legal summon placements and no turn would ever end. A per-turn ply budget
is the backstop. It reaches all sixteen command kinds and all four play modes; the scripted AI
reaches thirteen and one, and `pour`, `flip` and `sendMana` had **never been compared between the
engines at all** before this.

**The gate: 10,000 random legal commands over 25 matches and 6 commander pairings, zero
divergence** (`node tools/diffjs/fuzz.mjs --count 25`, ~50 s end to end).

**The shrinker.** A failure arrives as four hundred commands of which almost none matter. The loop
truncates at the divergent ply (free — a prefix of a valid trace is a valid trace), then
delta-debugs: each round proposes "drop this chunk" candidates, the C# engine RE-RECORDS all of
them in one Unity run (dropping a command changes everything after it, so this is a replay, not an
edit — `TraceParser` resolves every reference by CELL because unit ids renumber), and the first
candidate that still fails becomes the new baseline. Proven, not hoped: `--selftest` poisons the
engine so the third harvest of the match pays one mana it should not, and the shrinker returns a
**9-ply reproducer — exactly three harvests and the turn glue that makes them legal.**

**Three findings, all real:**

1. **Structure graves were recorded under their BID, not their card name** — a razed Mana Vault
   went to the graveyard as `vault`. `toGrave` writes `obj.nm`; structures had no `Name` because
   nothing had needed one. They carry it now, exactly as creatures do.
2. **A declaration outlived its attacker's position** (D18). Both engines let a creature move
   after declaring — declaring taps it, and `moveSpent` only reads `moved` — and the JS then
   rebuilds each attacker from the stored COORDINATE, meets an empty cell, and never strikes.
   Ours struck anyway, because it holds the unit id and found it wherever it now stood: 2000
   phantom damage into a wall. `CombatResolver.LiveAttacker` is now the single predicate for "is
   this declaration still real", asked by both the deferred-block collection and the main
   partition — a defender asked to block an attack that will never resolve burns its one block
   for the turn, which is a difference you can lose a game to.
3. **The adapter forgot that play-on-top is a summon.** `place()` calls `foeTrapOnSummon` on both
   branches (13_input.js:200), so a creature played over a banked card can be sprung on; the
   replay only remembered the plain-summon branch and sprang the trap at a stale creature.

**One declared exception, and only one.** Where the JS resolves a declaration with whatever
creature moved into the attacker's cell behind it, the port refuses (spec 03 §17 risk 2, decided
at M5). `DECLARED_IDENTITY` in replay.mjs makes the oracle behave like the port at exactly that
site, counts every drop, and prints the count on every replay — 9 across the 25-match sweep — so
the exception can never quietly widen into "the harness ignores combat divergences".

**One suspension point the harness still cannot compare:** the defender's CHOICE inside an
attack-trigger response window. `_resolveNow` springs the defender's first armed trap itself,
mid-resolution, with no seam an answer could reach, so the fuzzer is deliberately constrained to
answer "the first armed trap" there (summon windows have a real seam on both sides and are fuzzed
freely, declines included). Answering "the second trap" or "pass" against an attack is therefore
untested against the oracle — it is D6's deliberate widening, and it becomes testable the moment
the JS is retired and the port is its own reference.

**Known bias in the fuzzer, worth knowing before trusting it further:** kind-first picking means
`sendMana` (1064 plies) is as likely as `resolve` (213), so combat is explored an order of
magnitude less than banked-mana movement. The 10k sweep still covers 277 declarations and 213
resolutions, but a combat-weighted mode is the obvious next widening if a divergence is ever
suspected there.

229 tests — two of them the pipeline entry points, which skip unless the harness asks for them.

### Next session — M13 (the presentation pass)

M12 is the gate on retiring the JS (PORT_PLAN §4): tag `js-reference-final`, move `src/js/`,
`index.html` and `src/styles/` to `legacy/`, stop deploying Pages, and invert the card-data flow so
`CardDefinition` assets become authoritative. Do NOT do that until the parity-flag register in
`RulesOptions` has been walked through (M16 owns the register; M12 only proves the flags are
currently faithful). Everything the harness needs stays runnable from `legacy/` if the paths in
boot.mjs move with it.

### 2026-08-22 — M13 slice 1: card faces, type, figures

**The fonts, which gated everything else.** GAPS carried "the font/glyph plan for the 76 non-ASCII
glyphs" as an open P0 from M1; it is closed and, more importantly, GATED. `tools/export_glyphs.mjs`
collects the vocabulary from the reference source and card data into `docs/unity/spec/glyphs.txt`;
`FontPipeline` routes each glyph to the first font that MEASURABLY has it and
`FontCoverageTests` fails the build when a character has no home. Cinzel and EB Garamond are the
reference build's own faces (OFL, vendored with their licences). Two things the gate caught that a
plan would not have:

* Partitioning by Unicode block silently lost ⚔, ⚒, ⚙ and every arrow — Miscellaneous Symbols is
  split across three Noto families and block membership says nothing about which one drew what.
* **Unity 6's advanced text generator will not draw a STATIC font asset at all** — not as a
  fallback ("cannot use static font asset as fallback", then tofu) and not as a primary font
  either. The four symbol faces are dynamic now; the kanji face is dynamic too, which drags a
  5.3 MB TTF into the build for eleven characters. Subsetting it is an M16 build-size task.

**The card frame** is the DM_Template of spec 09 §6.1: ivory name banner with the cost circle and
the element gem, dominant art window, type lozenge on the seam, white ability box, footer of
power / ⚒ chip / ♥ health, element accent threaded through all of it. Sizes are fractions of card
WIDTH, so one frame serves hand, inspector, board plate and deck tile. Rules text is generated —
`abilityBrief`, `spellText`, `kwName` and `bldEffectText` as one service (spec 09 §6.6 [REQ]).
Authored in C# rather than UXML/USS (D20).

**The hand** is UI Toolkit, in the band MatchHud already reserved, with selection still owned by
MatchHud so the placement flow keeps one owner. **Standees** are on the board: field-art cut-outs
that hover, cast a blob shadow, bob, and lie flat when the unit cannot act (`canActNow`, ported).
Board rows are tinted by owner.

**Three bugs the screenshots found, none of which a test would have:**
1. MatchHud painted its opaque bottom band over the whole strip, and IMGUI draws after every UI
   Toolkit panel — so the hand was rendered and then covered. It paints around the hand now.
2. The band geometry lived in MatchHud and was published from OnGUI, which runs AFTER the hand's
   LateUpdate; the first frames used a fallback guess and put the cards under the bottom edge.
   `HudLayout` owns the numbers now and both surfaces compute from it.
3. A runtime `SpriteRenderer` defaults to the 2D renderer's Sprite-Unlit shader, which in a 3D URP
   scene drew every blob shadow as a BRIGHT ellipse. `Sprites/Default`, explicitly.

**Tooling, because a presentation milestone has no assertable oracle:**
* `tools/view-probe.sh` — renders a UI Toolkit panel to a PNG from batchmode (D22). Needs a
  graphics device, and drives the panel's repaint by hand: a runtime panel only paints from the
  player loop, which `-executeMethod` blocks before ever ticking.
* `tools/screenshot.sh` — the REAL battle screen, play mode, headless. `WaitForEndOfFrame` never
  resumes in batchmode (it hangs the run) and `ScreenCapture.CaptureScreenshot` writes nothing
  without a swap chain, so the camera and the UI panel render into two textures and are blended.
  The IMGUI HUD is absent from those shots by consequence, which is tolerable while IMGUI is the
  layer being replaced.

232 tests.

### Next — M13 slice 2

Walls and windows (the tower layout, deck and graveyard as real card piles — the reference's right
window, which the current build has nowhere to put), FX, and audio. Then M15's menus and deck
builder, which are whole screens that do not exist yet. (The board plate landed early, in slice 1c.)

### 2026-08-22 (later) — M13 slice 1b: the four things wrong with it on a phone

Feedback from the live build, all four real:

* **"The cards are too dark."** The same bug as the invisible hand, half-fixed: MatchHud still
  painted a translucent backdrop across the hand strip, and IMGUI draws after every UI Toolkit
  panel, so it dimmed the card faces by 82%. Nothing is painted over that band now - the hand
  owns its own backdrop, behind its own cards.
* **"The board is too small."** It was giving up 178 logical units of screen to the bottom bands,
  most of it hand. The hand is a PEEK strip now (46) with the cards hanging below it, and the
  camera fit margins - which were costing a fifth of the board width - went from 0.95/0.90 to
  0.99/0.97 with a smaller worker-pawn budget.
* **"The cards don't retract when not selected."** They had no rest state at all. Spec 09 §5.1's
  two states are in: resting cards are CLIPPED to the strip and show only their name banner, and
  the picked card moves to an unclipped overlay so it can rise clear without spilling over the
  action row beneath it.
* **"There is no display of what cards do."** There is now: picking a card raises a large inspect
  card at the right edge carrying the FULL generated ability text, not the hand card's three-line
  brief. A hand-sized ability box is unreadable on a phone, which is the whole reason the
  reference has a big card.

### 2026-08-22 (third pass) — M13 slice 1c: the card lies on the tile

Three more from the live build, and one the screenshots found on the way.

* **"The cards should be placed over the occupied board tile, not a second _fieldart."** Two board
  systems had been running side by side without anyone noticing: `StandeeLayer` (the M13 cut-out,
  bobbing, laid-flat pose, ground shadow) AND `MatchController.ReconcileStandees`, the M7 sandbox
  original — an owner-tinted plinth cylinder with a camera-billboarded `FieldArt ?? CardArt` quad
  standing on it. Every unit was drawn twice, and the plinth is the bright yellow/blue ellipse
  under every figure in the phone shot. The old system is gone.

  In its place, `CardPlateLayer`: the unit's own card, lying FLAT on its tile, Master-Duel style.
  Face-up units get the DM frame at board scale — banner, cost circle, element ring, art window
  with the illustration centre-cropped into it, ruled ability box, stat bar — rastered per element
  (nine textures cover the registry, the art is a separate quad). Face-down charges and traps get
  the reference build's procedural sleeve, tinted by their OWNER's element and never by the card
  underneath, because that is a secret. The cut-out then HOVERS 0.30 cells over its card, with its
  blob shadow left down on the card to tie it to the slot.

  Two consequences worth naming. `CardArtIndex.FieldArt` is gone: the name is the wrong key for the
  board (a structure is a StructId plus a resolved forge colour, which only the catalog can turn
  into a database key), so both layers go through the new `MatchController.DefOfObject`. And the
  cut-out no longer falls back to the card illustration — the fallback would now draw the same
  picture twice, once flat and once standing.

* **"There is a dark bar going through the cards in hand."** The mode row. A picked card rises out
  of the hand strip and passes straight through that band on its way up, and MatchHud painted the
  band opaque there — after every UI Toolkit panel, so on top of the card. IMGUI now paints only
  the action row, which no card reaches; HandBar's backdrop covers the whole band from behind.

* **"The selected card should be on the left like Master Duel."** It is: top-left, under the status
  bar. It hung off the right edge to stay clear of the picked card rising out of the hand; up there
  it is clear of it by the whole board. The event log moved to the right half of its row to make
  room, which is the corner Master Duel reserves for it anyway.

* **Probe**: `tools/screenshot.sh` grew a third shot, `battle-plates.png` — top-down with the
  figures off, because the tilted shot shows the plates half-covered by the standees hovering over
  them, which is correct and useless for judging them. `BoardInput.Tilted` is public now so the
  probe can ask for the angle. Three EditMode tests pin the plate: the flat-on-tile basis (a
  mirrored card and a correct one differ by one cross product, and that is invisible in review) and
  the frame's proportions against CardFace's flex weights.

235 tests.

### 2026-08-22 (fourth pass) — M13 slice 1d: figures belong to the tilted view

Two corrections to slice 1c, both from the live build.

* **"Top view should not show floating _fieldart. The opponent's structure isn't even on the
  field."** Correct, and it is geometry rather than a bug: an upright billboard seen from directly
  above projects off the top of its own tile and onto the row behind it, so the foe's back-row
  structures floated past the far wall with nothing under them. Figures are a TILTED-view thing —
  spec 09 §3.8 rule 1 says tilted forces them on, and this is that rule read the other way round.
  They now fade out with the swing (`BoardInput.TiltBlend`, so it tracks the ease instead of
  popping on the toggle) and the cards on the tiles carry the top-down view by themselves.

* **"These _fieldart are hovering much too high for their tiles."** Slice 1c's 0.30-cell static
  hover was wrong by about six times. Height turns into vertical SCREEN distance under the tilt, so
  a lift that reads as a modest float in world space walks the figure off its slot and over the row
  behind. The static lift is gone: the figure stands at 0.09, just clear of its card, and hovers by
  the BOB, which is what the reference build ever meant by hovering.

* **Probe bug found on the way, worth remembering.** `CaptureTopDownPlates` waited 90 frames for
  the camera ease and shot a tilted board wearing a top-down label. Batchmode runs UNCAPPED, so a
  frame is worth a fraction of the `deltaTime` it is worth in a player, and 90 of them moved a
  0.4-second ease about a fifth of the way. It waits on `TiltBlend` now and asserts it arrived. Any
  future probe that waits on an animation has the same trap. The shot also stopped forcing
  `StandeeLayer.Enabled = false`, so it is now evidence of the top-down rule rather than a staged
  picture of it.

233 passing.

### 2026-08-22 (fifth pass) — M13 slice 2a: the board stands somewhere

The board floated in a black void. It now stands on ground, and the ground is alive.

**What landed** — `View/World/`, three generated meshes and three shaders, all new:

* `SRD_Terrain.shader` — the island. One quad; colour, patches and motion are all fragment work.
  Biome is NOT a shader swap: the shader has three motion terms (waves, ripples, embers) and a
  biome is a set of amounts for them, so `water` is waves at 1 and embers at 0. The next biome
  anybody wants is a row in `Biomes` and no new shader.
* `SRD_Grass.shader` — wind-blown blades, one camera-facing quad each, ~5,000 of them in a single
  mesh and a single draw call. All the animation is vertex work, so the CPU does nothing per frame.
* `SRD_CloudShadow.shader` — cloud shadows over the whole scene as one multiply pass.
* Four biomes at commander select: **meadow, dunes, shallows, scorched.**

**Ported, with attribution** (see `THIRD_PARTY.md`): the wind and cloud method comes from Dynamic
2D Grass (MIT, Jomoho Games / Dylearn). Three ideas came across and each earns its place — dual
scrolling noise rotated a few degrees apart (one noise reads as a texture sliding past; two
multiply into weather), a clock quantised to ~7 steps a second with a per-blade phase (quantised
so it reads as drawn, phased so the field does not all step on the same frame and look like lag),
and shear from a pinned base so a field bends instead of sliding. The chunk streaming, terrain data
texture and effector system did NOT come across: all three serve a scrolling tilemap world, and
this board is a fixed 7×5 under a fixed camera.

**Three things the screenshots caught, all worth writing down:**

* **Vertex colour is bytes.** Per-blade height and width scales were written as 0.72–1.28 and
  silently clamped at 1, so every blade came out the same size. They are 0..1 now and the shader
  remaps. Anything packed into `mesh.colors` has this constraint.
* **A linearly tapered quad is a spike, and a field of spikes is scratches on the lens.** The first
  pass drew 2px hairs. Blades are wider (0.17), taper quadratically so they keep their body, and
  each carries its own arc.
* **The cloud field was being evaluated three times a pixel** — ground, blades and overlay — which
  also darkened the ground twice as hard as the pieces standing on it. The overlay does it alone
  now, at two octaves rather than four, with a trigonometric domain warp instead of another noise
  lookup. That is roughly a 6× cut in the per-pixel cost of the most expensive thing on screen.

`tools/regen-scene.sh` is new: the Battle scene is generated, and the terrain needed wiring into
it. `tools/screenshot.sh` grew `biome-*.png`, one shot per biome — terrain is the one part of the
view with no test that can fail, so four pictures are the gate.

**Next (slice 2b):** the horizon. The island fades to black at its rim; a sky and a distant
treeline would put the board in a place rather than on a table.

233 passing.

### 2026-08-22 (sixth pass) — M13 slice 2b: the field reacts, and the wind was broken

Feedback: "this does not look like the grass from the files I shared. the grass is not pressed down
or moves. I do not see the clouds moving overhead."

Two of those were BUGS I shipped, not missing features, and both are the same arithmetic mistake.

* **The wind field was pinned near zero.** The dual-scroll trick takes two 0..1 noises and
  multiplies them — and the product of two 0..1 fields has mean 0.25 and small variance, so
  `(n1*n2 + 0.28 - 0.5) * 2` sits at a measured mean of **0.053 out of a possible ±1**. Every blade
  held almost the same lean forever. The field is re-centred on the product's OWN mean now, with
  gain: `(n1*n2 - 0.25) * gain + bias`. The trick was right; the normalisation was not.
* **The clouds were a gradient, not weather.** At scale 9 and speed 0.05 a cloud was wider than
  the board and took ~17 seconds to cross it. 5.5 and 0.28 now — about six seconds.
* **The press field was missing** because slice 2a dropped the effector system as "plumbing for a
  scrolling tilemap world". That was wrong about which part was the feature. It is in now, as the
  reference has it: an R channel that says how flat the grass is, whose GRADIENT says which way it
  lies, because grass falls away from whatever presses it. The board's slab and a halo under every
  unit stamp into a 192×144 texture, repainted off `MatchController.Version` rather than per frame.
* **Gusts**, which the feedback asked for directly: a ring of wind rolls out from wherever a card
  lands, a spell resolves, a trap springs or a unit dies. Four slots, passed as a shader uniform
  array and evaluated per vertex — no texture writes, no CPU cost.

**The honest limit**, stated because it will come up: the board covers the middle of the field, so
a card landing on a centre cell can only bend grass at the RIM. The unit halo reaches 2.6 world
units (further than a tile needs) so an interior unit still touches the fringe, and the gust is
what actually reads. Grass growing between the tiles would fix it properly and would also make the
board harder to read; that trade is open.

**New probe: `motion-{a,b,c}.png`.** A still cannot fail the only question terrain has — does it
move. The first wind field shipped looking painted on and four good-looking screenshots could not
tell me. Three frames now: a→b is wind and cloud only, b→c adds a gust, and diffing them turns
"does it move" into a number (7.9% and 8.6% of pixels, against a field that was visibly static
before). Firing the gust into the first pair would have hidden a dead wind field behind a working
gust, which is exactly the mistake the pairing exists to prevent.

**Also fixed, unrelated to the terrain:** the phone showed a modal `TypeError: Permissions check
failed` on first tap. Android Chrome throws that out of the orientation lock inside Unity's
`SetFullscreen`, and an uncaught throw reaches Unity's global error handler, which puts an alert
over the game. The template catches it now — fullscreen is a nicety and failing to get it should
cost the player nothing but a browser bar.

233 passing.

### 2026-08-22 (seventh pass) — M13 slice 2c: the board is a marking, not a slab

Feedback: "the scale seems off. this is supposed to be a battlefield with buildings and creatures,
yet they are the size of individual blades of grass. I wanted the cards to be set on the grass. the
tiles can be a translucent overlay, not raised solid pieces."

Both halves of that are the same problem, and the raised tile is the root of it.

* **Cells were 0.12-thick opaque cubes.** A creature standing on a raised plinth beside knee-high
  grass is a figurine on a table, whatever size you make the grass. Cells are 0.02 thick and drawn
  by `SRD_Tile` now — a translucent wash with a brighter rim, terrain showing through. The box is
  still a box only because its collider is how `BoardInput` picks cells. Queue sits at
  `Transparent-70`, ahead of the grass, so the grass grows OVER the markings, which is the right
  way round for a field somebody has drawn a board onto. The walls stay solid: they are life
  targets, the one part of the board that is a physical thing in the fiction.
* **Blades were a quarter of a creature's height.** 0.30 tall and 0.17 wide read as shrubs. 0.11
  and 0.05 now, with density up from 7.5 to 20 per square unit to keep the field full.
* **Grass grows EVERYWHERE, the board included.** The keep-out is gone. That was the thing making
  "cards press down on the grass" impossible to see — a card on a centre cell had no grass near it
  to press. The board's own press dropped to 0.30 (it settles the surface, it does not mow it) and
  the unit halo tightened from 2.6 to 1.15 at 0.95 strength, because the grass it presses is now
  directly under the card.
* Everything anchored to the old 0.06 tile top moved with it: plates 0.075 → 0.03, standees
  0.09 → 0.05, ground shadow 0.085 → 0.042, terrain −0.062 → −0.020.

**Tile tint, second attempt.** The first pass used the old opaque colours at 26% alpha and the
board went grey: a pale tint over green grass desaturates toward nothing from every angle, and the
board lost the one thing its colour is FOR — whose ground this is. Saturated hue at a low alpha
now, with the rim dropped to 20%. Strong colour, weak coverage.

**The reported card-face glitch is NOT reproduced.** Source art is a valid 544×544 PNG and renders
correctly — it is the same file drawing fine in the inspect card in the reporter's own screenshot.
A new probe (`CaptureEveryHandCard`, `hand-*.png`) picks every card in hand in turn, because the
old shots only ever picked index 0; all eight render clean. The likeliest remaining explanation is
a frame caught mid-`HandBar.Rebuild`, which clears and recreates the row on any signature change —
transient, and invisible to a probe that shoots settled frames. Needs another sighting to pin.

233 passing.
