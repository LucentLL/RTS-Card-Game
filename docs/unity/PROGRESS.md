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
| M13 — presentation pass | 🟡 slice 1 | 2026-08-22 — real DM card frames (one C# frame, four scales, generated rules text), the 76-glyph font chain closed and GATED, the hand in UI Toolkit, standees on the board, owner-tinted rows. Remaining: walls + tower windows, board mini-card plates, FX, audio |
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
window, which the current build has nowhere to put), the board mini-card plate under each standee,
FX, and audio. Then M15's menus and deck builder, which are whole screens that do not exist yet.
