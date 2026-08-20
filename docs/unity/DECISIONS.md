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
