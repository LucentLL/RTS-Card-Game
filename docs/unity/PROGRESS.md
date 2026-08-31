# Port progress

Working scaffold for the 16-milestone plan in `PORT_PLAN.md`. Update this file every session:
mark what landed, note deviations, name the next target. Decisions go in `DECISIONS.md`;
this file is status only.

**Test gate:** `bash tools/run-unity-tests.sh` (EditMode via Unity CLI; exit 0 = green).
**Live relay check:** `bash tools/run-unity-tests.sh LiveRelayTests` — [Explicit], talks to the real
public MQTT brokers. The only test that can tell you the relays are down rather than the code.
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
| M13 — presentation pass | 🟡 slice 6 | 2026-08-24 — real DM card frames, the 76-glyph font chain closed and GATED, the hand in UI Toolkit, the card lying flat on its tile with the cut-out standing on it, owner-tinted rows, a living terrain island (4 biomes, wind-blown grass, lump-built cloud shadows), and the two castle walls as the screen top and bottom edges: they slide open when looked at, carry each side vitals and their hand of backs, and the field runs behind them wall to wall. Slice 4: the cards filling their tiles with the figures planted at the front of them, the numbers on one scale, unit vitals with health bars, and the DS-Yu-Gi-Oh battle cut-in. Slice 6: the stats printed ON the card (a health meter in the stat bar, attack/workers/printed health in the ability box), the foe half turned round so each side reads its own edge, tap-to-join attack groups, and a cut-in three times the size that stacks a joint attack into one clash. Remaining: tower deck/GY piles, horizon + sky, more FX, audio |
| M14 — campaign | 🟡 first pass | 2026-08-24 — the hexsphere globe (162 tiles), map generation, absorb cascade, end-turn AI, the 4-line challenge dialogue and the save, all pure C# and tested; globe view with drag-spin and raycast picking, world-map HUD, attack confirm and battle handoff. Open: garrison affects nothing, AI never absorbs, no custom deck in campaign |
| M15 — menus, deck builder, save/load | 🟡 first pass | 2026-08-24 — main menu, banner select and a screen router that switches the battle world off; three-column deck builder with search/filter/sort, mana curve, 5 slots and a duel-with-it path. Open: no solo deck-pick screen, no settings |
| M16 — parity flags resolved, ship prep | ⬜ | **+ D42: gate SendBankedMana on phase, re-cut the goldens, delete NetSession.LocalGate's phase clause** |
| M17 — multiplayer (password-linked 1v1) | ✅ done | 2026-08-30 — deterministic command LOCKSTEP over MQTT-to-three-public-brokers, sealed with a key derived from the shared password. 62 EditMode tests: whole matches stay bit-identical across a relay that loses, duplicates and reorders; desync is caught at the ply; both directions of reconnect replay from the other peer's log. Verified live over the open internet. The view now takes a SEAT (~90 sites), so the guest plays from the far side of the board |

## Session log

### 2026-08-30 — M17: multiplayer (284 -> 346 tests)

Two people share a password and duel. No account, no server of ours, nothing to deploy.

* **Sync is deterministic command LOCKSTEP** (D40), not the JS's host-authoritative snapshots.
  Both peers run the same engine from a seed neither of them chose alone, and the wire carries
  only commands. A 200-ply match measured **200 messages and 13.8 KB**, against 25-40 KB *per
  change* in the JS. Every frame carries the sender's state hash before applying, so a divergence
  cannot be silent and is reported at the ply with the command that caused it.
* **`SpawnRowDuel.Net`** is `noEngineReferences`, references Rules only, zero packages: hand-written
  SHA-256 / HMAC / PBKDF2 / HKDF / ChaCha20-Poly1305 (pinned to RFC 6234/4231/8439 vectors), a
  varint command codec, the message envelopes, and the session state machine behind
  `IMessageTransport`. The whole protocol runs headless on a virtual clock, which is why a hostile
  relay is a test rather than a hope.
* **A hole in the ordering invariant, found by review** (D41): `GameState.IsInteractive` reads like
  the guarantee that only one side can act, and has NO CALLERS; `SendBankedManaHandler` has no
  phase gate, so at `Phase == End` both peers have a legal, non-commuting command. The M12 fuzz
  corpus contains 218 of them. `NetSession.LocalGate` closes it in the netcode; the rules fix is an
  M16 item (D42) because it re-cuts the golden corpus.
* **The transport was rewritten mid-milestone** (D44). The first design polled ntfy.sh over HTTP -
  one code path on every platform, no jslib - and its own arithmetic killed it: a 60-request burst
  refilled at one per five seconds, 250 publishes a day. Then ntfy.sh stopped answering this
  machine entirely after a few dozen probes. Shipped instead: a hand-written MQTT 3.1.1 client over
  WebSocket, connected to `broker.emqx.io`, `broker.hivemq.com` and `test.mosquitto.org` **at the
  same time**, publishing to and reading all of them. The pair meets if any one broker works for
  both. `ClientWebSocket` natively, `Plugins/WebGL/SrdWebSocket.jslib` on the web.
* **Reconnection is peer-to-peer** (D45): each peer keeps the log, `Hello` and `Join` are the same
  message in opposite directions, and whichever end still holds the match hands it back whole.
  Both directions tested; nothing depends on a relay remembering anything.
* **The view took a seat** (D43). `Seat.Local` / `Seat.Remote` replaced ~90 `Side.You`-means-me
  reads and the camera yaws 180° for the guest, because mirroring the wire is impossible here -
  `NewMatch` draws You's deck before Foe's off one shared stream. `BoardView.RowMaterial` had to be
  fixed by hand: it keys the warm/cold ground wash off `RowKey`, so a substitution sweep walks
  straight past it and the guest would have seen enemy ground under their own units.
  `battle-guest-seat.png` in the probe is the proof.
* **The AI cannot play over the network** (D47): `AiChoices` rolls off the MATCH RNG, so running it
  on one peer desyncs the other. Human duels have no AI in them, so this costs nothing today.
* Known, unrelated: `node tools/diffjs/replay.mjs` now diverges on the third golden. That is the
  UNCOMMITTED JS balance work in the tree (`OC_PER_CHARGE=500`, walls interposing, `placeRowOK`
  enforcing `def.row`), not a port regression - the only Rules change this session is 26 lines of
  pure addition to `RulesOptions`. Those changes want porting to C# and the goldens re-cutting.

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

### 2026-08-22 (eighth pass) — M13 slice 2d: the rail, the tufts, and a tap that hit twice

Four things from the phone.

* **"When I press the summon button over the card, the card deselects."** A real input bug, and a
  structural one: SUMMON sat in a reserved band that the picked card rose straight through, so the
  button and the card occupied the same pixels. IMGUI and UI Toolkit are separate input paths that
  never learn about each other's handled events, so the tap ran BOTH - the button armed the play,
  and the card's own `PointerDown` toggled the selection off underneath it. The mode row clears the
  risen card's full height now. No overlap, no double delivery.

* **"The End Turn and Build are taking up too much space. Refer to HTML position for phasing."**
  Ported the reference's shape. `.hand` is at `bottom: 0` and `#boardBtns` hugs the right edge at
  mid-height - its own comment calls it the Master Duel coin position - so the bottom band is the
  hand PEEK alone (46 units, was 120) and the board keeps the height the buttons used to cost it.
  The rail carries the reference's `#phaseTrack` too: a compact vertical list lighting the current
  phase, with Combat indented as the sub-phase of Action that it is. `HudLayout.RailPx` blocks board
  taps inside it, the same way the build menu and log already do.

* **"The grass stretches, not compresses down."** A missing clamp. The bend direction comes from
  the gradient of the press field, which can be arbitrarily steep, and it was applied raw: a
  0.11-tall blade was being dragged up to two world units sideways - eighteen blade-lengths - which
  draws streaks across a field, not grass lying over. The bend is a direction now, capped at about
  one blade-length and measured against the blade's NATURAL height so a fully flattened blade still
  lies over instead of shrinking into its own root.

* **"Do I need to find grass assets, because these look very unprofessional?"** Fair, and the
  answer was yes-ish: one tapered quad per instance is a spike, and a field of spikes reads as
  spikes however you tune it. `GrassTextures` generates a four-variant TUFT atlas instead - six to
  eight blades per tuft, each with its own lean, height and taper, soft-edged, with a root-to-tip
  luminance ramp for the shader to tint. Same approach as `CardTextures`: the art is drawn in code.
  Hand-drawn art would still beat it and drops in as a texture swap if you want that.

  Worth writing down, because it bit immediately: **a tuft quad is ~70% transparent**, so the
  density that filled a field with solid blades leaves about 4% ground cover. Density and quad size
  had to go up together (9 → 24 per square unit, blade cap 16k → 22k) to get back to a lawn.

**Not verified headlessly:** the rail, the phase track and the mode row are all IMGUI, and OnGUI
has no target texture, so none of them appear in the probe shots. Layout there is reasoned, not
seen. That gap is now the biggest hole in the visual gate.

**Still open:** the reference's extending/retracting castle walls (tower windows, `--wallY`), which
the same feedback asked to refer to. The phase track and rail position came across; the walls are a
whole surface and stay on the M13 remaining list.

233 passing.

### 2026-08-22 (ninth pass) — M13 slice 3: the walls are the screen edges

Five things from the phone, and four of them turned out to be one thing.

* **"I don't see the opponent's hand."** It was never drawn — only a count, buried in a corner
  block. Their hand is a fan of face-down sleeves across the middle span of their wall now, tinted
  by their element, full backs rather than the peek yours uses: a peek works for your hand because
  the banner it shows names the card, and the back of a card has nothing to read. What has to carry
  from across the board is how many.

* **"You/Foe information should be split. Foe info top, player info bottom."** Done, and the old
  bar is gone rather than moved. One block listing both sides' numbers reads as a scoreboard; a
  wall each reads as two keeps facing each other. Life, mana, piles and workers sit in each wall's
  left tower, the turn read-out in the foe wall's right tower (spec 09 §4.2).

  It also fixed a defect nobody had reported: that bar was IMGUI, **and IMGUI's built-in font has
  no ♥, ◆ or ⚒**. The gated 76-glyph chain covers UI Toolkit, not OnGUI, so the deployed build has
  been rendering "FOE 10000 0 hand 4" with the symbols silently dropped for as long as the bar has
  existed. The vitals are UI Toolkit labels now and the glyphs are simply there.

* **"The red bars should not be on the field. They should be the top/bottom bars of the screen
  where fully retracted castle walls are."** `BoardView.BuildWall` is deleted. The bands are the
  walls: procedural crenellated stone (`WallTextures`), tall towers at 0–21% and 79–100%, eight
  merlons across the middle span, an element-tinted rail along the crest, and a 14-unit overhang
  drawn with alpha so the field shows between the merlons. Drawn in UI Toolkit under the hand,
  because IMGUI paints after every UI Toolkit panel and a band painted there lands ON the cards.

* **"The battlefield should fill the gap from top to bottom"**, with a reference image. Three
  changes, because the first two were not enough:

  1. **The fit fills instead of fitting.** Solving for distance alone with the camera aimed at the
     board's centre is not the same as filling the screen: under perspective the near edge projects
     far larger than the far edge, so pulling back until the near edge fits strands the far edge
     near mid-screen — that gap was the empty grass above the board. `Frame()` solves two unknowns,
     distance and a slide along the camera's own up-axis, and centres on the board's ground
     corners with standee headroom as a distance constraint only. Including headroom in the
     CENTRING was holding a hundred pixels of empty air over the foe's back row.
  2. **Width is the expensive axis.** The picture is width-limited at the near corners, where
     perspective magnifies most, so the 0.85-unit allowance for the worker files was costing a
     fifth of the board's screen depth. The files still stand off each row — they just are not
     budgeted for any more, because sitting back along their own rows they clear the constraint
     for free.
  3. **Rows are 1.45× deeper than columns are wide** (`BoardView.RowStretch`). Square cells are the
     obvious choice and the wrong one: at the tilted angle depth foreshortens by sin(42°) ≈ 0.67,
     so square ground cells project as tiles twice as wide as they are tall, and a 7×5 board of
     them is a letterbox that can only ever fill a fraction of the height. Stretching the rows
     spends the slack the width limit leaves, and a cell now reads as a square because on screen
     it is one.

  Vertical fill went 65% → 81% of the gap and the board runs about 92% of the width. The reference
  image was a vertically stretched crop of the reporter's own screenshot, which is exactly this.
  **Top-down pays for it**: that angle sees the board near-square and is height-limited, so it now
  has wider side margins. An acceptable trade — top-down is the secondary angle — but it is the one
  thing this slice made worse.

* **"The clouds come across the screen with sharp lines on one side, like a sheet of paper with
  clouds drawn on it."** Exactly right, and the cause was two deep.

  The field was `saturate(fbm × fbm + threshold)` pushed through a contrast term and clamped to a
  shadow floor — and with those constants most of the field sat PINNED at the floor. The flat grey
  was the paper, the moving shapes were the LIT gaps, and the clamp gave them a step for an edge.
  Under that, `SrdHash` was `frac(p * 123.34)`, which is only well behaved on a fractional domain;
  value noise feeds it integer lattice points, and `frac(i × 123.34)` is `frac(i × 0.34)` — a ramp
  with a period of fifty. The "noise" carried a repeating diagonal structure.

  The hash is Dave Hoskins' now (which also fixes the wind field), and a cloud is built as a cloud:
  `SrdCloudCover` is a jittered grid where some cells carry one round lump, the falloff is smooth
  all the way out, and neighbours ADD — a lone cell is a puff, a run of them merges into a lobed
  cumulus. Every edge is a gradient, and a gradient cannot draw a straight line. The outline is
  WARPED rather than overprinted: a second additive layer of smaller lumps is the obvious way to
  get raggedness, and it sprays free-floating specks wherever the base is mid-valued. The field is
  LIT by default at ~17% mean cover, which is the way round a sunlit battlefield works.

**The probe was lying, twice.** `Shoot()` forced the camera to the whole screen "so the shot shows
everything" — but the viewport is exactly what the wall bands take away, so every shot was of a
layout the game never renders. And batchmode's game view is 640×480: the bands take a fixed share
of the height, so a 4:3 probe judges the framing against a gap far narrower than a phone's. It
keeps the real viewport and forces 1600×900 now.

**Still open:** the walls only RETRACT — no raise/hover, no `--wallY` board shift, no deck and
graveyard piles in the right towers (counts only). Worker pawns remain M9 placeholder capsules.

233 passing.

### 2026-08-23 — M13 slice 3b: the walls slide, and a set card carries its own number

Five more from the phone.

* **"The castle walls should extend when viewed, and retract when not."** Spec 09 §4.4, ported.
  A wall rises while it is being looked at — hovered on a mouse, tapped on a phone, with a 1.6 s
  linger there because a finger that has lifted is still looking — and sinks when it is not. The
  trigger is the TOWER spans only, never the middle: the middle is where the hands are, and a wall
  that opened every time you reached for a card would spend the match sitting on the board.

  The furniture lives INSIDE the stone and stacks away from the crest, so what shows when the wall
  is down is decided by the same slide that draws it. There is no second layout that can disagree
  about the retracted state. The stone is rastered at FULL height and translated, the way the
  reference wall does it — resizing it would stretch the courses and betray the animation.

* **"The opponent's castle wall extends too far — should show just enough to display their life
  points."** Both rails are 30 units now (was a fixed 58-unit band). Life and mana share the rail
  line; piles and workers arrive when the wall rises. Mana is on the rail rather than behind the
  hover because "what can I afford" is asked every turn, and a number you have to open a wall to
  read is a number you will misremember.

* **"Cards don't touch the bottom of the screen, and should extend a little past the castle
  wall."** They do both now. The hand sat at `bottom: HandBandBottomPx`, a stone lip under it,
  which is what "stuck to the wall" looks like; it is at `bottom: 0` and its peek (48) is taller
  than the rail (30), so the cards stand proud of the battlements the way a held hand does. Their
  hand is the mirror: hung from above the screen edge, showing its lower band, standing the same
  amount proud of their rail.

  This is also why **the camera renders the whole screen now**. It used to be inset to the gap
  between the bands, which is tidy and wrong: the field stops at a bar instead of running behind
  the battlements, and every gap between two merlons shows a strip of nothing. `Frame()` fits the
  board into a WINDOW between the two hand peeks instead — off-centre whenever they differ, which
  is why the fit carries a `mid` term and `NeedV` asks how far back a point needs to be to land
  inside a window that is not centred on the screen.

* **"When placing a building, it gives me the option to place it on the opponent's side (even
  though it doesn't allow me)."** Not a rules bug: `Build_LightsOwnRowsAndCentreFlanksOnly` pins
  the legal set at your two rows plus the four centre flanks, and their ground answers
  `DestinationNotDeployable`. It was the HOVER: `UpdateHover` painted any hovered cell with
  `HoverMaterial` — the same green the armed-play highlight uses — so a finger resting on their
  ground lit it up exactly like a legal drop. Hover no longer paints anything the engine would
  refuse while a play is armed.

* **"Set cards should not have a floating SET # — just display the stored mana on the card."** The
  label is gone and the sleeve carries a badge: an element-tinted gem and the number, drawn on the
  card. Two problems in four characters, as it turned out — the card's own number was being
  printed on the BOARD, and the ◆ in "SET ◆1" was never drawn at all, because that overlay is
  IMGUI and IMGUI's built-in font has no diamond. Every other ♥ and ◆ in those overlays was
  dropping the same way; they are ASCII now, and banked mana moved onto the card for creatures and
  structures too. The badge's digits are a 3×5 bitmap font: nothing in world space can reach the
  SDF chain, and eleven glyphs of bitmap is cheaper than the machinery that would let it.

  Sized at 0.34 of the card's length rather than 0.20, because a flat card is foreshortened by
  sin(42°) and a badge that reads on the texture reads at two thirds of that on the board.

**Also:** the worker pawns were wearing the TILE material — a translucent marking wash — which is
why a worker read as a grey smear. They have an opaque URP Lit material of their own, are smaller,
and file five abreast along their row without wandering onto the board's outer column.

**Probes added:** `walls-open.png` (the extended state, via `WallBands.ForceOpen` — a wall that
only rises when looked at cannot be looked at by a batchmode still) and `set-card.png` (a creature
set face-down with 12 poured into it, which is the only witness the badge has).

234 passing.

### 2026-08-24 — M14 + M15 (first pass): the campaign globe and the deck builder

Both ported from the JS. The game has a FRONT now — menu, banner select, world map, challenge,
deck builder — where before it booted straight into its own commander select and that was the
whole product.

**The campaign core is pure C# and tested** (`SpawnRowDuel.Campaign`, engine-free asmdef):

* `HexSphere` — Goldberg GP(4,0), the dual of a frequency-4 subdivided icosahedron: 162 tiles, 12
  of them pentagons, 320 corners, deterministic from the frequency alone. That determinism is why
  a save is three kilobytes: it stores the tile-to-territory assignment and rebuilds the geometry.
  It only holds while tile INDEX ORDER is stable, so the icosahedron face table is frozen and the
  vertex weld is a quantised lattice key rather than the JS's `toFixed(6)` string — that string
  makes -1e-9 and +1e-9 different vertices ("-0.000000" vs "0.000000") and would silently produce
  a sphere with the wrong number of tiles.
* `CampaignMapGenerator` — Mitchell best-candidate territory seeds, multi-source BFS carve,
  farthest-point empire seeds, a second flood for the empires, garrisons. Contiguity is a property
  of the construction (a graph-distance Voronoi on a connected graph cannot island), and the test
  proves it over 200 seeds × 22 territories × 8 empires rather than trusting it. The JS asserted
  the same claim with an 800-map Monte-Carlo and no test.
* `CampaignRules` / `CampaignBattleResolver` / `CampaignTurnResolver` — attackability, the capital
  prize, the absorb CASCADE (taking one throne can hand you another; without it that element
  lingers as a landless holdout no attack can reach and the campaign is quietly unwinnable), the
  victory latch, End Turn growth, the one-attempt-per-rival AI, and defeat at zero territories.
* `ChallengeDialogue` — the 80 authored barks, 8 rival exchanges and the four-line assembly,
  verbatim.
* Everything takes a seeded RNG. The JS used bare `Math.random` in nine places and so could never
  re-derive or audit a map; the save stores its seed.

**The coupling is inverted.** In the JS the battle's own win check reached into the campaign layer
and called `campResolve` directly — which is why multiplayer had to remember to defensively clear
the campaign's pending target before starting. Here the duel knows nothing: `GameShell` launches a
battle, watches for `IsOver`, and brings the outcome back. "Abandoned" is a real outcome rather
than the JS trick of nulling the target.

**The globe is a mesh, not a projection.** The JS drew it with an orthographic canvas projection,
painter-sorted quads, a hand-rolled light and an inverse-ray pick that had to be corrected against
the extruded radius because dividing by the plain one mis-picked about one tap in seven. A prism
mesh, a z-buffer and a `Physics.Raycast` do the same job and cannot drift apart. What survives is
the FEEL: drag-spin with inertia, the ±1.25 rad pitch clamp, the idle spin after 2.6 s. Borders are
quads over the shared edge of two tiles — adjacent tiles share exactly two corners, which is a
property of the dual and is what lets an edge be drawn once instead of twice with z-fighting.
Markers are UI projected from anchors, because the only thing that can draw 炎 is the gated font
chain.

**The deck builder** is the three-column layout: detail (a real `CardFace`), deck (leader, curve,
list) and pool (search, filters, sort, tiles). 40 cards, max 3 copies, 5 slots, `element|name`
keys — the key carries the element because a dual leader can reach one name through two colours.
Changing leader drops what is now off-colour. A card the registry no longer knows is DROPPED on
load rather than kept: a deck that silently references a retired card fails to start a match long
after the edit that broke it.

**The shell** switches the battle world off while a menu is up — board, terrain, duel camera and
the duel's IMGUI are all one `SetActive`, and `MatchHud.ShellSuppressed` stops the old commander
select from drawing over the main menu.

**Not done, and worth saying:** garrison still has no effect on the duel (spec 08 §16.3 — flagged
to design, ported as-is); the AI never absorbs capitals; a campaign battle still rolls a fresh deck
rather than letting you bring a built one; and there is no save-slot UI beyond load/overwrite.

265 passing. New probes: `shell-menu`, `shell-faction`, `shell-worldmap`, `shell-challenge`,
`shell-deckbuilder`.

### 2026-08-24 (later) — M13 slice 4: the numbers, the tiles, and the clash

Five from the phone, and one of them had been wrong since the globe was built.

* **"Attack and HP should be reduced by 10 — 3000 ATK becomes 300."** The display had two
  scales, not one: the board overlays and the deck builder divided by 500 (a 3000-attack dragon
  read as "6") while the card frames and the wall rails printed the raw number (the same dragon
  read "3000", and the wall it was hitting read "10000"). One divisor now, ten, everywhere —
  `StatScale.Show` in Rules, because the keyword and spell text producers print statlines into
  sentences and "Deal 1000 damage" beside a creature showing ♥200 is worse than either scale
  alone; `View.Stat` wraps it with the glyphs. **The engine is untouched** (D23): the ×500
  registry and the 10000 life pool are what the M12 harness pins against the living JS.

* **"The cards on the field should fill their respective tiles, no gaps."** They did not come
  close: `0.98` of a cell "along the card's long axis" was then divided by the card's own aspect,
  so a 0.72 × 0.98 card sat on a 1.00 × 1.45 tile and covered under half of it. The plate is the
  tile's face exactly now, at a cost of 4% of stretch along the card's length (D24).

* **"Buildings are still floating far too high above their tiles."** The feet were at the CELL
  CENTRE, which is where the cell is and not where the ground looks like it is — with the card
  now covering the whole tile, its near half sits below the point the figure stood on. They stand
  at the front of their own tile (the reference's `bottom: 11%`), the shadow moved with them and
  is sized to the figure, and the sprite is lifted by its RENDERED height rather than its budgeted
  one, which is what made a width-clamped cut-out hover by the difference (D25).

* **"The campaign globe and hex grid is hollow."** It was, literally: **every tile fan was wound
  inward**, so `Cull Back` removed the near hemisphere and the globe you were looking at was the
  inside of the far one. It passed for a planet for a whole milestone because a lit shell looks
  like one — until something is put underneath it, which is exactly what happened when a crust was
  added and drew straight over the plates it was meant to lie beneath. The plates, their side
  walls and the new crust all face out now, the sphere is closed at its own radius so the chasms
  have a floor, and `EveryGlobeTriangleFacesOutOfTheSphere` checks all 3840 triangles — in both
  senses, because a side wall's normal is tangential and the naive radial test reads zero for it
  (D26). The shader also declares its light mode and a `DepthOnly` pass, since the globe had never
  been in URP's depth prepass at all and was being drawn by `Cull Back` and index order (D27).

* **"I can't see the health of buildings I am attacking. I don't see attacks when they happen.
  The cards that are attacking and defending should pop up like in old Yu-Gi-Oh games."** Three
  things, and they landed as three:

  1. `UnitVitals` replaces the IMGUI overlay. That overlay floated 1.45 cells ABOVE each slot,
     which under the tilt is most of a row up the screen, so the foe's back row put its numbers
     behind the castle wall and the layer's own answer was to drop them — the row you attack into
     was the row with no numbers. They hang off each tile's near edge now, in UI Toolkit, where ♥
     and ⚔ have a font that can draw them (IMGUI's has none and drops them silently, which is why
     the old overlay read "6 hp"), with a health bar under the line and a red TARGET ring on
     anything your selected attacker may legally hit (D30).
  2. Damage numbers. Every `DamageApplied` and every `WallStruck` throws a "-150" off the thing
     that took it — which covers Bolts, Cannon Towers and Backlash as well as combat.
  3. The battle cut-in (`CombatTheatre` + `BattleCard`), ported from `#battleView` in the
     reference build: the attacker's card flies in from the left, the defender's from the right,
     ⚔ lands between them, and each card shows what it hit for, what it has left, and a DESTROYED
     stamp if it did not survive. It fires on the RESOLUTION rather than the declaration (D29) and
     holds the AI for as long as it runs, because `AiDriver` pumps a command every 0.35 s and
     would otherwise talk over it.

  Drawing a fight at all needed a fix under the floor: the loser is graved inside the same
  `Apply`, so nothing that reads `GameState` can draw it. `CombatTheatre` keeps a one-frame-old
  snapshot of every unit — and for that to be the board BEFORE the blow, `MatchController` had to
  stop pumping events once per frame at the top of `Update`. It drains them immediately after
  every command now (D28); the frame pump stays as a catch-all.

271 passing (six new). New probe: `battle-cutin.png` — a staged same-row duel, resolved, caught
mid-clash. It is the only witness the cut-in has, and getting it to render at all is what turned up
both the snapshot timing bug and the fact that declaring is not fighting.

### 2026-08-24 (later still) — M13 slice 5: the number on a set card, the tap that missed, the far row

Three more from the phone.

* **"Set cards, even if they cost 1, should display how much mana was used to play them
  face-down."** Set CREATURES did (a charge banks its ◆1 and shows it); set TRAPS showed nothing,
  because a trap consumes its ◆1 rather than banking it and the badge read the bank. It reports
  the ◆1 it cost now. The bigger half of the change is that the number is shown to BOTH players
  (D32): over-paying a face-down is a bluff, a bluff nobody can read is not one, and a face-down
  reading ◆1 being either a trap or an unfunded creature is the guess the mechanic is made of.
  The rule behind it was already right and is worth stating: `Traps.ProvokeFaceDown` destroys an
  underfunded charge outright rather than flipping it, so a card that cannot pay when it is turned
  over simply fails.

* **"I'm unable to attack Wall because the button is over a card... this issue was already fixed
  for another button, it should be fixed across the game."** Right on both counts. BoardInput
  cannot see IMGUI consume a tap, so every control over the field has to publish its rect - and
  only the three PANELS did, by hand. `MatchHud.Btn` registers what it draws (D31), so all
  twenty-nine loose controls block the board now and the next one cannot be forgotten. Pinned by
  `ARegisteredControl_BlocksTheBoardUnderIt`.

* **"Buildings in the opponent's back row are extending far outside of the tile."** They were:
  1.64× their tile's height at the back row against 0.76× at the front. A billboard's screen
  height falls off as 1/z and a tile's falls off faster - the tile is lying down, so its near edge
  is nearer than its far one - and a figure sized in world units therefore grows relative to its
  own ground the further away it stands. Standees are sized against the tile AS IT PROJECTS now,
  which is what the reference stylesheet's `cqh`/`cqw` units always meant. Finding it turned up a
  second error in the same place: a standee is a camera-facing billboard, so it grows along the
  CAMERA's up axis, and measuring against world +Y understates that by cos(42°) - which had every
  figure coming out half again too big to compensate (D33).

272 passing. `set-card.png` now sets a trap beside the poured-into charge, so the shot witnesses
both halves of the bluff: ◆12 and ◆1, side by side.

### 2026-08-28 — the last three creature pools get their art

Dark, Electric and Forest were the three pools G1 named as empty, and they are the three the user
just filled in locally. All 64 elemental creatures now carry both a `_cardart` and a `_fieldart`;
no pool is a placeholder pool any more.

* **Twenty-one of the forty-six new files were not PNGs.** Fourteen were WebP and seven were JPEG,
  all wearing `.png`. This is the same trap as the M3 batch (thirty files then), and it splits the
  same way: a browser sniffs content and renders all of them, so the web build was never wrong,
  while Unity decodes JPEG and cannot decode WebP at all. The fourteen WebP files therefore
  imported as texture type Default, went invisible to the importer's `FindAssets("t:Sprite")`, and
  their cards came out with `cardArt: {fileID: 0}` while every `_fieldart` beside them bound fine.
  Converted in place with ffmpeg (~0.5 MB → ~3.6 MB; dimensions unchanged, and the largest of them
  is still smaller than the Fire art already committed). The seven JPEGs are left alone —
  `magmaw` and `pyrewing` have shipped that way since M3 and both pipelines read them correctly.

* **`FixArtImporters()` could not have saved this one.** Its self-heal scans `t:Texture2D`, and a
  file Unity failed to decode is not indexed as any type, so the repair pass found nothing to
  repair on two consecutive runs. Worth remembering that the function covers the ordering race it
  was written for and not a decode failure — the symptom (`0 updated`, art still missing) looks
  identical from the log.

* Four Forest files also arrived slugged as their display names — `hive cradle_cardart.png`,
  `sap pod_fieldart.png`. `slugify` strips non-alphanumerics, so the probe wants `hivecradle` and
  `sappod`; renamed.

23 CardDefinition assets rebound, 272 passing, no code changed. The remaining art gap is the four
Divine creatures, three structures and the worker — still G1, still not fatal.


### 2026-08-29 — M13 slice 6: the numbers on the card, the attack group, the clash

Three from the phone, and the third arrived while the first was being built.

* **"The black bars under the card should display health — a meter with a number in it. Above the
  black bar, the Attack, Worker Amount (+ or -) and Base HP."** They do. The frame was drawing a
  stat bar with nothing in it and three ruled lines standing in for text; the stats band is a
  health meter with the number printed across it now, and the ability box is a plaque reading
  ✕attack ⚒±workers ♥printed-health. The frame's own argument against this — no text is legible in
  a band twelve pixels tall — was answered by measuring the other axis: the band is the widest
  thing on the card (D34).

  Three measurements decide whether it reads (D36). The digits are STRETCHED, about 1 : 1.4,
  because the strip is width-limited and the height was going spare. The raster is twice the
  band's own resolution, because the cell size is an integer and at 192 texels the step from a
  size that fits to one that does not threw away a fifth of the width. And everything carrying a
  number is sorted OVER the standee, because the figure stands at the front of its own tile and
  its shins cross exactly the two bands the numbers live in. The meter is quads — a raster would
  cost a texture per (hp, max) pair the fight reaches — and only the number is rastered, keyed by
  value. The attack mark is an X: an upright sword drawn in a dozen pixels is a blade three of
  them wide.

  **The foe's cards are upside down now** (D35), which is what puts each side's meter on its own
  edge of the board. Every readout inside is counter-rotated, because a card can be upside down
  and a number cannot. The vitals chip that used to carry ⚔/♥ and a bar is a NAMEPLATE: the name,
  the two keyword states that change, and the target ring. The same health in two places a
  finger's width apart is not redundancy, it is two things to check.

* **"When attacking with multiple monsters, I shouldn't have to individually select their target
  to be the same to attack together."** A declared target opens an ASSAULT; tapping your other
  ready creatures joins it (D37). The rules did not move a point — a joint attack has always been
  N declarations sharing a target, regrouped at resolve time, and there is deliberately no group
  command — what moved is that re-picking a target you have already picked is not a decision. All
  four declaration paths (board tap, ⚔ WALL, the three worker stacks) go through one funnel, so
  the buttons open a group exactly as a tap does; the group dies with its declarations, with your
  action phase, or on DONE. Joiners wear a gold ring on their nameplate, because a lit CELL is
  under the card that covers the whole tile.

  The half of it that is not UI: a declaration parks a blocker choice on the defender, and the AI
  was answering it on its own 0.35 s beat — so two of any three quick taps landed inside a window
  where every command is rejected as ChoicePending. The defender's answer is part of resolving
  your command and no longer waits for a beat (D38), narrowly: blocker requests parked on the foe
  and nothing else, so the cut-in's hold is untouched.

* **"When combat occurs, the cards should be larger... when multi-combat happens, the cards can be
  stacked."** A cut-in card is three tenths of the screen wide, or as much as its height allows —
  which on any landscape screen is what decides. Scaling it needed the type unclamped first: every
  font size inside a battle card was capped at 14 / 18 / 34 px, so a bigger card alone would have
  been a poster with eight-point type on it. A joint attack is told as ONE cut-in with the
  attackers fanned against the card they are all hitting (D39) — which needed the theatre to read
  its own declaration list rather than the queue, since only the first declaration against a
  defender is ever resolved by name.

281 tests (279 passing, 2 skipped as before), nine new. Two new probes: `attack-group.png` (a live
group — the target ringed red, the creature that may still join ringed gold, two declarations
standing) and `battle-stack.png` (all three told as one clash). Worth remembering about the probe
harness: it composites the camera's render texture with the UI Toolkit panel's, so IMGUI is not in
a probe shot at all — the mode row's join hint and the side rail have never been in one and cannot
be checked there.

### 2026-08-31 — Terrain, second pass: the grass, the ash that lands, the tide

Five notes from the phone, all of them about the ground, and four of the five were the same
complaint in different weather: the effect was drawn rather than happening.

* **"I really do not like the grass you keep adding to backgrounds. I was hoping for something
  more like this."** The reference is HAIR — long thin blades, many of them, overlapping into a
  swept surface. What shipped was a 40×64 tuft of six fat blades on a quad 0.175 wide by 0.185
  tall: a near-square sprite of near-square blades, at which size a meadow reads as a field of
  cabbages. The cell is 44×112 now, a blade is under two texels across against 112 of height,
  there are twenty of them in a clump, and the quad matches the cell's 1 : 2.3. The atlas has
  MIPS (hair-thin blades with none crawl the moment the camera moves) and a gutter either side of
  every variant so the mip chain cannot bleed one into the next. Six variants, not four.

  The bushes were the other half of the cabbages: 0.92 wide against 0.70 tall is a shrub. They are
  TUSSOCKS now — 0.34 by 0.95, the same clump grown tall — so the canopy has a height to it
  without a hedge in it. And the wind field gained a term: the dual-scroll noise gives a meadow
  that breathes in patches, which is right, but what the eye reads as wind on long grass is a
  coherent band travelling downwind, and that is one sine along the wind direction.

* **"I'm not a fan of the blocky ashflakes slowly going diagonally. They should fall randomly onto
  the ground (cards and tiles, partially covering cards until they move)."** Both halves of that
  are one mistake: the fall was a screen-space pass. A screen-space disc has no perspective, so a
  flake over the far wall is drawn the size of one by the camera — that is the blockiness — and a
  pass with no ground in it has nowhere to land, so everything left the bottom of the frame still
  falling.

  The flakes are in the WORLD now: one quad each, with a fixed landing point on the terrain (or on
  the cards, over the board), flown down to it by the vertex shader on its own clock, its own
  wobble, its own rate. In the last few percent of its cycle a flake lies flat, fades, and hands
  its coverage to a new layer.

  That layer (`SRD_Settle`) is what accumulates. A sheet that follows the ground, drawn AFTER the
  cards so it lies on them, with coverage from one channel of a texture the CPU grows over time.
  Growth is a THRESHOLD against fixed noise rather than a rising alpha, because ash arrives in
  patches that spread and join rather than as a wash getting stronger. "Until they move" needs no
  special case: a cell whose occupant changed has its patch of the field set to zero and starts
  again. Coverage stops growing well short of full — a field at 1 is uniform, a field at 0.58 is
  patchy, and patchy is what settled ash looks like — and it thins hard away from the board,
  because scorched ground is DARK and burying the horizon in pale grey loses the biome.

  The one thing it needed that is not weather: a mask. The standees are sprites that write no
  depth, so a sheet drawn after them would paint ash across every figure's knees. G channel carries
  the strip of ground each standing piece hides — from its own front edge to about a unit behind
  it, which at 42° is exactly what its billboard covers — and the sheet skips it. Nothing visible
  is lost, because a figure is standing in front of all of it.

* **"Shore is decent, but it would be nice if the tide came in and out instead of always flowing
  one way. There should also be wave lines."** The tide is a waterline on one axis: a slow breath,
  a faster swash riding on it, and a low-frequency noise bending it so the shore is not ruled
  straight. Wave lines march shoreward, packed and standing taller as they shoal, and break in a
  band of foam pinned to the water's edge wherever it currently is. Behind the retreating water the
  sand stays dark and holds a lace of foam.

  It runs ACROSS THE BOARD, and that is not a liberty — it is what the framing leaves. The camera
  frames the board to FILL the viewport, so everything past the far row compresses into sixty rows
  of pixels: the first build put a sea out there and it was a bright sliver under the wall band
  that read as a bug. A beach flat enough to fight on is a beach the wash runs over. Over the tiles
  the water thins to a wet film and the foam does the describing, because a card under three
  quarters of an opaque water tint is a card you cannot read.

  `TerrainField.TideFreeze` pins the cycle for the probe. A twenty-second tide photographed at the
  wrong second is an empty beach, and "is the sea there" should not be a question a test answers by
  luck: `tide-in.png` and `tide-out.png` are the pair.

* **"Ripples or land displacement should be relatively card shaped, not just circles — rectangles
  with soft rounded edges."** They are. `SrdRoundBox` is one SDF call where `length()` was, and it
  now shapes the hollow a piece presses into the ground, the rim of material shoved out of it, and
  the ring of wind a landing throws through the grass and the veil. Same cost, right shape. While
  the board's footprint was being read properly for the press, the plateau was fixed with it: it
  had assumed a square 1.08 pitch and the rows are stretched by 1.45, so the flat area the board
  stands on was a third too shallow and the back row had the foot of a dune in it.

* **"Dunes looks nice, but the wind with grains seems poorly done by comparison."** Sand does not
  travel as dots. It saltates — a grain hops downwind in a long flat arc — so what a camera catches
  is a dash, and a field of round specks at one size and one speed is a noise texture. Two passes
  of dashes now, each grain sliding through its own cell over its own short life so it fades in,
  runs and fades out; only on the lowest sheets, where sand travels; and gated by the sheet field,
  so grains stream inside the gusts and the air between them is clear.

  The first attempt at this was a downpour, and the reason is worth writing down: the dune wind runs
  nearly AWAY from the camera, so a dash elongated along it projects as a vertical streak. Blowing
  sand came out looking like rain. The elongation has to stay small at this angle, the grains have
  to be rare, and they need a far fade — a horizontal sheet at a grazing angle packs its far half
  into a few rows of pixels, and without one the top of the frame silts up with everything the
  sheet holds.

346 tests (341 passing, 5 skipped), no rules touched — this is all scenery. Four new probes:
`settled-scorched.png` and `settled-drifts.png` (a board that weather has been landing on, via
`TerrainField.PrimeSettle`, because waiting twenty-five seconds of match for a screenshot is a
probe nobody runs) and the tide pair.
