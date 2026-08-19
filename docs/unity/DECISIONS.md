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
