# Port progress

Working scaffold for the 16-milestone plan in `PORT_PLAN.md`. Update this file every session:
mark what landed, note deviations, name the next target. Decisions go in `DECISIONS.md`;
this file is status only.

**Test gate:** `bash tools/run-unity-tests.sh` (EditMode via Unity CLI; exit 0 = green).
**Card regen:** `bash tools/regen-cards.sh` after any card edit in `src/js/`.

| Milestone | Status | Landed |
|---|---|---|
| M1 — project exists, repo clean | ✅ done | 2026-08-19 (`b6da820`) — created headlessly via CLI, not the Hub |
| M2 — rules-core skeleton + test gate | ✅ done (adapted) | 2026-08-19 — asmdef `noEngineReferences` gate + Unity CLI runner. **No `dotnet test` leg yet**: no .NET SDK on this machine (see DECISIONS D2) |
| M3 — card data pipeline + art link | ✅ done | 2026-08-20 — pure catalog + loader + V1–V11 in Rules (`ce847a7`); SO pipeline, importer, junction, 159 committed assets (`ccf3d22`) |
| M4 — geometry, determinism, state, codec | ✅ done (write-side) | 2026-08-19/20 (`7fe843b`, `ddbc6ce`) — codec is **write-only** so far: hash + canonical JSON exist, `Read`/migrations/redaction land with the first save/netcode consumer |
| M5 — commands, events, engine, NewMatch | ✅ done | 2026-08-20 (`bc7e94c`) — full command set, processor (Execute re-validates), events, PendingRequest, DuelEngine, NewMatch. **Core API frozen; view work can start in parallel** |
| M6 — economy, workers, turn machine, upkeep | ✅ done | 2026-08-20 (`08b41d6`) — 12-step BeginTurn, phase guards, doHarvest w/ orphan fallback, Move/Pay/Sacrifice settlement, StructureUpkeep.Tick, vault drain, cleanup sweep, MoveUnit handler pulled forward from M7 |
| M7 — placement, movement, set/flip, structures | ✅ done | 2026-08-20 (`091becd`) — PlayCard summon/set/settrap + play-on-top, BuildStructure off the commander list w/ lineage prereqs, in-place upgrades w/ damage carry, flip w/ both JS quirks behind flags. Cast waits for M10 |
| M8 — combat v3 + legacy engine + pending requests | ▶ **next** | request/response types + combat command shapes exist from M5; the whole step machine, blockers, retaliation, damage tiers, checkWin remain |
| M9 — minimal Unity battle scene | ◕ sandbox | 2026-08-20 — hand strip w/ art thumbnails, tap-to-summon/set/build/flip/move over CanApply-probed lit cells, standees w/ field/card art + stat overlays, greedy-summon foe feeder. Remaining: combat interactions (M8) + settle menus |
| M10 — keywords, spells, traps, response window | ⬜ | |
| M11 — scripted AI (vertical slice) | ⬜ | |
| M12 — differential harness vs the JS | ⬜ | build as soon as M8 lands, while the JS is still the living oracle |
| M13 — presentation pass | ⬜ | |
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
