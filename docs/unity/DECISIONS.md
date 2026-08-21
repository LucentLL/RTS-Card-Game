# Answered decisions

Decisions the open-questions register in `PORT_PLAN.md` and `GAPS.md` raised, and how they were
answered. Anything here is settled — do not re-open it in planning docs.

| # | Question | Answer | Date |
|---|---|---|---|
| — | Where does the Unity project live? | `unity/` inside this repo | 2026-08-17 |
| — | Primary release target | PC / Steam first; mobile is a later port | 2026-08-17 |
| — | Multiplayer | Deferred. Campaign + AI ships first; rules core stays deterministic + command-driven so host-authoritative netcode layers on later | 2026-08-17 |
| — | Port strategy | Native C# rewrite with a pure, UI-free, deterministic rules core. No WebView | 2026-08-17 |
| **G1 / A12** | **Who produces the ~48 missing card art assets?** | **Nobody — ship placeholders.** The art currently in `assets/cards/` is itself placeholder, so Dark / Electric / Forest being empty is not a content regression, it is the same state as everything else. Art is NOT a milestone blocker and must not gate M11 or M13. | 2026-08-19 |

## Consequences of G1

* `GAPS.md` G1 is **closed**. Its proposed action (add an art backlog, gate M13's "done when" on
  coverage) is **rejected** — M13 ships with whatever art exists.
* The 3-tier art fallback (`spec/06`, from `02_art.js`) is therefore **load-bearing in the shipped
  build**, not just in development. It must be ported faithfully and must look deliberate rather
  than broken: a card with no `_cardart` still needs a readable frame, name, cost and stats.
* The importer must not error or warn-spam on a missing texture — a missing art file is the
  expected case, not an exception.
* Reopen only if the release scope changes (e.g. a paid Steam launch where placeholder art becomes
  a store-page problem). That is a business decision, not an engineering one.

## Session decisions — 2026-08-20 (M3 + M5)

| # | Question | Answer | Date |
|---|---|---|---|
| **D1** | Newtonsoft for the importer (design 03 §5.4 note)? | **No — hand-written integer-only JSON parser in `Rules/Catalog/Json/`.** The registry's null-vs-0 distinction survives, the parser rejects floats (they are banned in the core anyway), and the one assembly that must stay dependency-free stays dependency-free. The importer, the tests and any future headless CI all share the same loader + V1–V11 battery, so there is exactly one parse of truth. | 2026-08-20 |
| **D2** | `dotnet test` fast loop (M2 deliverable, design 03 §8.3)? | **Deferred, not dropped** — no .NET SDK is installed on this machine. All tests run through `tools/run-unity-tests.sh` (Unity CLI EditMode, ~60 s). Revisit when the suite is slow enough to hurt or when CI moves off this machine; the asmdef `noEngineReferences` gate already enforces the no-Unity boundary. | 2026-08-20 |
| **D3** | Where does registry ORDER live once cards are assets? | On every `CardDefinition` as `registryIndex` (per kind). The database index array stays sorted by export key for stable YAML diffs; `ToCatalog()` re-sorts by `registryIndex`. Order is load-bearing (pool order feeds `deckOf`, commander order feeds the random pick, spell order feeds spell draws), and the SO↔JSON parity test pins it. | 2026-08-20 |
| **D4** | 30 art files were WebP bytes named `.png` (browsers sniff, Unity trusts extensions → DefaultAsset). | **Converted in place to real PNG** (ffmpeg, lossless). The web build reads PNG identically; repo grew ~8 MB; the alternative (renaming to `.webp`) would have kept Unity art-less for those 30 cards since Unity cannot import WebP at all. | 2026-08-20 |
| **D5** | Commander/element rows have no art or slug — do they get assets? | Yes — same `CardDefinition` type, kinds `Commander`/`Element`/`Token`, because the character-select, HUD and battlefield theming all need their data (hp/wk/colors/buildList, glyphs/palette). 159 assets total: 68 creatures + 14 spells + 31 structures + 36 commanders + 9 elements + worker. | 2026-08-20 |

## Session decisions — 2026-08-21 (M10)

| # | Question | Answer | Date |
|---|---|---|---|
| **D6** | The JS springs traps two different ways — the AI's auto-spring (`foeTrapOnSummon`, `springAttackTrap`) and the human's choice (`playerTrapOnSummon`, replaced at load time by `RESP.defendWindow`). Which one is canonical? | **Neither: the defender is always ASKED.** The core has no idea which side is a person, so an asymmetry keyed on "is this the AI" is not expressible in it. Both halves become a parked `ResponseWindowRequest` the defender answers with `TrapChosen`. A policy that answers "the first armed trap" reproduces the old auto-spring outcome exactly, and that is what the stand-in opponent does. The human gains the choice the RESP layer already gave them. | 2026-08-21 |
| **D7** | The JS opened ONE response window per *action* (`foeTurn` consumes `springRef` once) but auto-sprang per *spring site* when the AI held the trap. Which granularity does the port use? | **Per spring site.** The resolver offers the window at every point where `springAttackTrap` would have fired, and re-reads the armed list each time. This is the strict generalisation: answering "spring" at every site is bit-for-bit the auto path, and a defender holding two attack traps can still spring both in one resolution, as the JS auto path could. A defender holding none is never asked, so combat with no traps is unchanged. | 2026-08-21 |
| **D8** | `trigger:'summon'` traps ignore `card.effect` entirely in the JS — any of them just destroys the newcomer. Port the quirk or honour the effect? | **Port the quirk.** All three summon traps are Snare today so it is invisible, and the data model implying otherwise is not worth a divergence while the differential harness (M12) is still the gate. Pinned by a test that gives a summon trap a `burn` effect and asserts the creature is destroyed anyway. | 2026-08-21 |
| **D9** | `RulesHooks.OnCreatureEnter` / `OnSummonTrap` were assignable static delegates left as M10 seams. Keep the indirection? | **Deleted.** They are direct calls into `Triggers` / `KeywordEngine` now. A mutable static hook is a live hazard the moment two matches share a process (AI search clones state thousands of times a second), and with the keyword milestone landed there is nothing left to defer. `DeathSweep.OnCreatureDeath` went the same way. | 2026-08-21 |
| **D10** | A bounced or revived creature came back as its CATALOG card, losing hatched forms and Thornmail buffs (the M8 debt). | **Cards carry a `CreatureSnapshot`.** `HandCard`, `GraveRecord` and `ChargeUnit` each hold the live statline when the card has been on the board, mirroring the JS's `handcardFromCreature` / `toGrave` object literals field for field — including what they deliberately DROP (`cnt`, `oc`, `bank`, `token`). A card that never left the deck carries none and still resolves through the catalog. The flag is hashed, so a card with history cannot hash equal to a fresh copy. | 2026-08-21 |
| **D11** | PORT_PLAN M10 asked for `ISpellEffectHandler` × 6, mirroring the keyword registry. | **A switch on `SpellEffect` instead.** The keyword registry earns an interface because it has six hook points to implement; a spell has exactly one operation and one predicate, and two of the six effects (pitfall, thornmail) have no castable branch at all — six near-empty classes would be shape without substance. The requirement that actually mattered — dispatch on EFFECT, never on card name, and hold target legality in one place — is met: adding an effect touches `SpellTargeting.CanTarget` and `SpellEngine.Resolve`, nothing else. | 2026-08-21 |
| **D12** | The 14-agent audit of M10 confirmed that a Scour flier Undertow hurls back to hand *mid-resolution* still shatters a back-row card in the JS — it strikes from its owner's hand, because the JS resolver walks captured attacker OBJECTS while ours walks ids. Reproduce or fix? | **Reproduce.** It is the JS's own bug, and parity is the contract until the M12 differential harness is green. Implemented without breaking resumability: the bounce is recorded in `CombatState.BouncedScourIds` (serialized, cloned, hashed) rather than by holding a live object reference a snapshot would lose. Pinned by `AnUndertowBouncedFlier_StillShattersTheBackRow`. Improving it later means deleting the list, not unpicking a design. | 2026-08-21 |
