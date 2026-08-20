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
| M6 — economy, workers, turn machine, upkeep | ▶ **next** | WorkerMath + Mana + deck/draw already landed with M5; remaining: 12-step BeginTurn, phase transitions + guards, upkeep settlement, StructureUpkeep.Tick, vault drain, cleanup sweep |
| M7 — placement, movement, set/flip, structures | ⬜ | |
| M8 — combat v3 + legacy engine + pending requests | ⬜ | request/response types + combat command shapes already exist from M5 |
| M9 — minimal Unity battle scene | ◔ early slice | interactive deployed board generated from rules geometry (`74c503d`) predates the engine; must be rewired to consume `DuelEngine` events once M6 lands |
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

### Next session — M6

Turn machine + economy, per PORT_PLAN M6 and design 01 §4: `TurnMachine.SetPhase` as the only
phase writer, the legal-transition table, the 12-step `BeginTurn` pipeline (stub the keyword /
structure-upkeep steps it calls with TODO-throwing seams if M7/M10 content is not ready),
harvest with the stale-`owe` anti-deadlock rule, upkeep Move/Pay/Sacrifice settlement,
end-of-turn mana drain (vault capacity), and the 200-empty-turns stability test with a per-turn
hash trace. That makes `BeginTurn`/`Harvest`/`DrawForTurn`/`EndTurn` the first four real
handlers in `CommandHandlers.CreateDefault`.
