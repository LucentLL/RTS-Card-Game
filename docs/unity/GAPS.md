# GAPS — completeness review of `docs/unity/`

**Role of this document.** Adversarial pass over `docs/unity/spec/01`–`09`, `docs/unity/design/01`–`03`
and `docs/unity/PORT_PLAN.md`, looking only for what is **missing or wrong**. Everything good is
deliberately omitted. Findings are ordered by how much damage they do if left unaddressed.

**Verdict up front.** The *rules extraction is sound*. I spot-checked eleven high-stakes claims against
the JS with grep (combat v3 row-interval blocking, block eligibility, retaliation, the blocked/open
partition, the 12-step turn start, upkeep settlement, `doHarvest`'s stale `owe`, mana-vault drain,
`moveSpent`, `doMove` ordering, and five constants) and **found zero incorrect claims** — see
[Appendix A](#appendix-a--accuracy-audit). All 31 files in `src/js/` appear in the disposition table and
their dispositions are defensible. The monkey-patch and MP-apply layers were *not* missed — both are
covered, including the two places they genuinely change behaviour.

The gaps are therefore **not in the rules**. They are in (1) content production, (2) everything a
retail PC release needs that a browser prototype never had, and (3) a handful of mechanical defects in
the plan itself.

---

## P0 — will break the plan as written

### G1. Art production is not a milestone, and the shortfall is ~3× larger than the plan states

`PORT_PLAN.md` M3 states: *"Art coverage is partial today — 83 `_cardart`, 69 `_fieldart`; Dark and
Electric creatures have none."* The file counts are right. The scope claim is not.

Verified by enumerating `assets/cards/` against `docs/unity/spec/cards.json`:

| Group | Count | Has `_cardart` | Has `_fieldart` | Missing |
|---|---|---|---|---|
| Creatures | 64 | **41** | **41** | 8 Dark, 8 Electric, **7 of 8 Forest** |
| Spells + traps | 14 | 14 | 0 (not needed) | — |
| Structures | 13 | 12 | 12 | `grandvault` (Grand Vault) |
| Forges | 18 | 16 | 16 | `empyreum`, `grandempyreum` (divine) |
| Divine creatures | 4 | 0 | 0 | all 4 |
| Tokens (Lumen, Shade) | 2 | 0 | 0 | both — they are board creatures via `mkToken` |

`assets/cards/Creatures/Forest/` contains exactly two files (`sapling_cardart.png`,
`sapling_fieldart.png`). **Forest is 1/8, not 8/8** — the plan does not mention it at all.

**Playable-set shortfall: 24 card illustrations + 24 field cut-outs = ~48 assets** (52 with divine).

Consequences the plan does not price in:

* M11 is declared *"VERTICAL SLICE COMPLETE… play a complete battle with real cards from
  `CardDatabase`"*. A player who picks **Dark or Electric — 2 of 8 solo commanders, and 14 of the 36
  commanders include one of them** — gets an all-placeholder deck. Forest is 7/8 placeholder.
* In campaign, 3 of the 8 element empires (`spec/08` §map generation) defend with placeholder art.
* Open decision **A3** only asks about the *provenance* of the 83 files that exist. Nothing asks who
  **produces** the missing 48, on what schedule, at what cost. It is the one work item on the whole
  project a programmer cannot do.
* M13 is sized **XL / 10 units** covering card faces, board shader, standees, walls, 9 VFX
  compositions, 23 audio cues, the event bus, damage numbers and rendered AI declarations — with zero
  units allocated to illustration.

**Action.** (a) Add a per-slug art backlog file. (b) Add an open decision A12: *commission, generate,
or ship-with-placeholders?* — answer before M11, not M13. (c) Make M13's "done when" include either
100% art coverage or a signed-off placeholder list.

### G2. No font / glyph atlas plan — 76 distinct non-ASCII glyphs, 9 CJK, 12+ emoji

Zero occurrences of `font`, `TMP`, `TextMeshPro`, `atlas`, or `glyph` anywhere in `docs/unity/`.

Inventoried from `src/js/*.js` + `index.html`: **76 distinct non-ASCII characters**, including

* **CJK ideographs** 炎 水 地 風 森 雷 光 闇 神 — the element glyphs, `01_core_defs.js:16-25`, rendered
  on every element badge and in the deck builder (`11_deck_builder.js:200,207,228`).
* **Symbols** ◆ ⚒ ♥ ⚔ ✦ ⛏ ⌂ ⓘ ⤧ ◈ ♜ ⟳ ▣ ☩ ⛭ ⊡ ⛨ ⛺ 🜂 Σ ² ≤ ≠ ° — these carry *rules meaning*
  (mana, workers, life, attack, harvest, vault, revive, forge). ◆ alone appears 157 times.
* **Emoji** 🏳 🔊 🔇 🧍 🎲 💤 ⛓ 📜 ❔ ⏸ ⚙ ✎ ↩ — UI affordances.

The browser silently fell back to Segoe UI Symbol / Segoe UI Emoji. **Unity renders tofu** unless a
font asset with an explicit fallback chain is authored. This bites at **M9**, not M13 — the "ugly but
playable" build still needs ◆ and ⚒ in its labels.

Also unaddressed: font **licensing for redistribution** is a Steam-submission item, and a CJK+emoji
fallback chain is a real build-size contributor (design 03 §7 discusses build size but not fonts).

**Action.** Add to M9: pick the UI typeface; generate the glyph inventory from the string table; build
the TMP fallback chain (Latin → symbols → CJK → emoji); add a test asserting every literal in the
string table resolves to a glyph. Add the font licence to the M16 provenance audit alongside the art.

### G3. Six AI-behaviour decisions have no parity flag — and they collide head-on with M12

`design/01_rules_core.md` §8 defines the register: **exactly 20 flags**, all defaulting to JS
behaviour, with a test asserting `JsParity == default` and a ship-build assertion that the count is
zero. The mechanism is good. Its coverage is not.

`PORT_PLAN.md` §6 recommends changing observable AI behaviour in at least **six** places that have no
corresponding flag:

| Decision | Recommended change | Flag exists? |
|---|---|---|
| **B13** | Gang-block absorber: dump on the *weakest* blocker, not the toughest | ❌ |
| **B14** | Reorder `aiPickTarget` — take the guaranteed kill before the 0.6 face-down roll | ❌ |
| **C10** | `raze` targeting: real heuristic instead of "last structure found" | ❌ |
| **C11** | Give the AI `doHarvest`'s structural-remainder fallback | ❌ |
| **C14** | Retreat graph on real row adjacency (foeBack → foeFront → center) | ❌ |
| **C15** | AI casts `chain`/`bounce` and sets creatures face-down | ❌ |

(The register *does* cover B12 `AiChoosesRetaliationTarget`, B15 `AiWallDefenceThreshold`,
C12 `AiReadiesWorkersAfterSettle`, and `AiDrawsAtTurnStart`.)

**Why this is P0.** M12's gate is *"a 200-turn AI self-play run produces byte-identical canonical state
in both engines."* Any one of those six, implemented unflagged, makes that gate **permanently
unreachable** — and the failure will read as a port bug, which is exactly the ambiguity §4 of the plan
identifies as destroying the harness's value. Meanwhile M16's forcing function ("flag count = 0")
never sees them, because they are not flags.

**Action.** (a) Add the six flags with JS-parity defaults. (b) Add a plan-level rule: *no behaviour
change of any kind lands before M12 is green* — state it in §4 next to the HTML-freeze policy, since
it is the same argument applied to the C# side. (c) Sweep the remaining ~80 decisions in §6 and tag
each one either `flag`, `data`, or `post-M12`.

### G4. M15 and M16 have no exit criteria

Every milestone M1–M14 states **Goal / Deliverables / Done when / Unblocks**. **M15 and M16 state Goal
and Deliverables only** — no "Done when", no "Unblocks".

Those two milestones contain nearly every ship blocker: accessibility (which risk #12 calls a
*"shipping blocker"*), save/load with migration, settings persistence, the IL2CPP release build, the
Company/Product name lock, the art-provenance audit that *"gates Steam submission"*, and the
parity-flag closure. The plan's own forcing functions live in milestones with no definition of done.

**Action.** Write both. Minimum for M15: *every action reachable by keyboard and gamepad with a visible
focus ring; cell state legible with colour disabled; settings survive a restart; a campaign save
written by build N loads in build N+1.* Minimum for M16: *`RulesOptions` is empty; an IL2CPP build
runs a full campaign battle; every shipped asset has recorded provenance.*

---

## P1 — production requirements nobody owns

### G5. No tutorial, no onboarding, no in-game rules reference

Zero hits for `tutorial`, `onboarding`, `codex`, `glossary`, `first-run` across `docs/unity/` (the one
`tutorial` hit is `Assets/TutorialInfo/` in a delete-this list).

This game teaches itself today through two surfaces, and the port keeps neither:

1. **`setHint()` contextual prose** (`spec/09` §9.3) — ported as localizable strings, which preserves
   the words but is not onboarding.
2. **The static rules panel** in `index.html` — and `spec/02` §14.14 records that this panel is
   **factually wrong** on the single most important economy rule (it still says *"Mana persists
   between turns"*, which Combat v3 reversed), still describes the removed command center as an attack
   target, and misstates the Barracks ⚒ value.

So the Unity build ships with **no correct teaching surface at all**. The rules that need teaching are
not trivial: derived workers, the explicit Move/Pay/Sacrifice upkeep, row-interval blocking, the
absorber and retaliation picks, mana that evaporates unless vaulted, and face-down banking.

Cheap half of the fix already exists as data: `cards.json` carries `keywords[].inspectText` for all 8
keywords. A codex screen is nearly free. A guided first battle is not.

**Action.** Decide scope (codex only vs. codex + scripted first battle), assign it to M15, and rewrite
the rules text from the code — never from `index.html`.

### G6. Music does not exist, is not budgeted, and has no mixer path

One mention in the entire corpus: `design/02_view_layer.md:83` —
`│  │  │  └─ Music/ (empty for now — no music in the browser build)`.

* The audio architecture (`design/02` §11.4) defines exactly two mixer groups, `SFX` and `UI`, under
  `Master`. There is no `Music` group.
* The settings table (`design/02` §13) exposes a single **Master volume** — no Music/SFX/UI split,
  which is a baseline expectation on Steam.
* M13's audio deliverable is *"re-author 23 sound cues from scratch"*. Silence between cues is the
  actual shipped experience.

**Action.** Either (a) add a `Music` mixer group, three volume sliders, and a scored brief (menu /
campaign globe / battle / victory / defeat ≈ 5 tracks) as an M13 deliverable with its own units, or
(b) record "shipping without music" as an explicit decision so it is a choice, not an oversight.

### G7. No mid-battle save/resume — and the quit-mid-battle rule is undecided

The *capability* is designed and the *feature* is not scheduled. `design/01` §680 already specifies
`ToBytes(state, Full)` for save games, and the whole point of putting the combat resolver cursor
inside `GameState` is that *"a snapshot taken mid-resolution is complete and resumable."* But:

* M15's deliverable is one line — *"Save system with `SchemaVersion` and migration; Steam Auto-Cloud
  paths"* — with no statement of **what** is saved.
* The persistence table (`design/02` §13) lists settings, saved decks, and campaign state. **No battle
  state.**
* No autosave, no "Continue", no pause-and-quit.

A campaign battle is a 20–40 minute commitment on a PC where alt-tabbing away is normal.

Worse, there is an undecided rule underneath it. In the JS, quitting mid-battle is a **free retry**:
`CAMPAIGN.target` is nulled on load (`10_menus_campaign.js:26`), so the assault simply never happened.
`spec/08` §274 documents the behaviour correctly, but **no Tier-D question asks what it should be**.
On Steam this is a one-click undo for any losing campaign battle.

**Action.** Add a Tier-D decision: *battle autosave + Continue*, or *abandonment forfeits the
territory*. Then add the chosen one to M15 with a done-when.

### G8. No error/crash policy at the engine boundary, and no diagnostics

`Rejection` (design 01 §3) covers *invalid commands*. Nothing anywhere covers an **invariant
violation in a release build**:

* `cleanup()`'s 40-iteration guard exhausting (`16_movement.js:194`)
* `bidLineage`'s 8-hop guard (`06_mana_workers.js:191`)
* a `StateCodec` / `SchemaVersion` mismatch on load
* a card id present in a save but absent from `CardDatabase` after a data regen
* an unhandled exception anywhere inside `DuelEngine.Apply`

Undefined today: log file location, whether an IL2CPP release build asserts or swallows, whether the
player sees anything, and — most valuable for a solo developer — whether a failure produces a
**reproducible dump**. The whole architecture makes this nearly free: seed + `RulesOptions` + the
command log reproduces any state exactly.

**Action.** Add to M5 (cheap there, expensive later): an `IDiagnostics` sink; a top-level catch at the
engine boundary that writes `{seed, RulesOptions, SchemaVersion, command log, state hash}` to disk;
and a decision on whether to ship a crash reporter at all.

### G9. Localization is asserted in the design and absent from the plan

`design/02` §5.3 says card rules text becomes *"one localisable string-template system"*; §7.7 says
*"every string… into the localisation table verbatim"*; `spec/09` §684 repeats it. But there is **no
milestone deliverable, no target-language decision, no pseudo-loc test**, and no accounting for the
content volume: 80 campaign barks + 8 rival exchanges (`spec/08`), 23 hint strings, 22 territory
labels, all card names and flavour text.

Two specific hazards the docs do not name:

1. The rules-text **generator** composes English word order from card data (`abilityBrief`,
   `spellText`, `bldEffectText`). That is the exact pattern that cannot be localized after the fact —
   it must be template-with-arguments from the first line of code, not string concatenation.
2. It compounds G2: a real language list determines the font fallback chain and the build size.

**Action.** Decide explicitly — *"English at launch, string-table-clean, no per-language work"* is a
perfectly good answer — and add a pseudo-localization pass (long strings + accents) to M15 so layout
breakage surfaces before it is expensive.

---

## P2 — coverage holes in the disposition table

### G10. The disposition table covers JS only; six categories of tracked file have no disposition

`PORT_PLAN.md` §3 is titled *"Disposition of every JS file"* and delivers on that — 31/31 present and
correct. Nothing gives a PORT / REBUILD / DISCARD row for:

| Unaccounted | Size | Where it *is* mentioned | Why it matters |
|---|---|---|---|
| `src/styles/*.css` (6 files) | **1,436 lines / 156 KB** | Inventoried in `spec/09` §1; absorbed in prose by `design/02` | No dispositions. §1.1's *"~35% of the JS is presentation glue"* and the 84-unit sizing are computed over **6,092 JS lines only** — excluding 1,723 lines of CSS+HTML that `spec/09` needs 1,791 lines to specify. |
| `index.html` | 287 lines | Cited by 8 spec files | Never dispositioned. It is the DOM skeleton, every element id the JS binds to, and the **factually wrong** rules panel (G5). Appears only in the M12 "move to `legacy/`" rule. |
| `sw.js`, `manifest.webmanifest`, `icon.svg`, `.nojekyll` | — | `design/02` §14 discards "Service worker / PWA manifest" in prose | Nothing says who deletes them or when GitHub Pages stops deploying. These are the files that break a half-finished retirement. |
| `tools/build.py`, `embed-art.py`, `split_monolith.py`, `build_manifest.json`, `dist/` | — | `embed-art.py` cited once (`spec/06` §1075) | The HTML build pipeline. No statement that it retires with the JS. |
| `assets/sprites/` (1 file) | — | — | Every record in `cards.json` still carries `spriteBase`. M3 tells the importer to ignore the `art` field; it says nothing about `spriteBase` / `cardArtUrls` / `fieldArtUrls`, which are equally dead once art resolves at import. |
| `spawn-row-duel-v26.portable.html` + `.7z` | **38 MB** committed at repo root | — | A Unity project's `.meta` churn is about to share a repo with 38 MB of committed binary. No repo-hygiene item anywhere. |

**Action.** Retitle §3 *"Disposition of every tracked file"* and add a §3.1 covering the six rows
above. Fifteen minutes; removes the entire class of "I thought that was handled."

### G11. `22_fx_wrappers.js` is not purely presentational — its row says "DISCARD → table"

The claim *"Zero rules changes (verified line by line)"* is correct **about the FX mechanism**, and I
re-verified it. But the file also *owns* three non-FX behaviours, and the disposition row does not say
where they go:

* **The entire surrender flow** — `doSurrender()` at `22_fx_wrappers.js:285` sets `G.over = true`,
  clears `CAMPAIGN.target`, saves, and routes to the world map, plus the two-step confirm at `:294`.
  (Correctly specified in `spec/07` §19 and `spec/08` §724 — but a reader working from the
  disposition table alone deletes the file and loses surrender.)
* **Two persisted settings** — `srd.angle` (`:277-284`) and `srd.cutins` (`:24, :324`). Absorbed by
  `design/02` §13, unreferenced from the row.

**Action.** One-line edit to the row: *"DISCARD the mechanism → §18 event table; **re-home**
`doSurrender` (→ `MatchOutcome.Abandoned`, M14) and the two settings (→ `design/02` §13)."*

### G12. A command the plan discards is the one MP will need

`harvestRowI` (`42_mp_apply.js:29-37`) keeps the per-row harvest path alive. The plan lists
`harvestRow`/`applyHarvest` under *"Also discarded… unreachable in solo"* — true for solo, but it is
the guest's path in MP, and the locked decision is that *"host-authoritative netcode can be layered on
later without a rewrite."*

Harmless while MP is deferred; a small avoidable trap later.

**Action.** Model the primitive as `HarvestZone(zone)` and make "harvest all" a loop over
`{back, front, center}`. Costs nothing now, removes a future re-derivation.

### G13. M16 is a code checklist, not a release checklist

M16 covers IL2CPP, Company/Product lock, Steamworks/AppID, art provenance, and balance. `design/03`
§7.5 additionally covers depot upload and the VDF. Not mentioned anywhere:

* Store page assets — capsules, screenshots, trailer (all gate the store page, all take weeks)
* **Steam Input** — mentioned once as a *reason* for the Input System (`design/02:680`), never as a
  deliverable; a Steam Deck verified pass needs it
* An **achievement list** — `ISteamServices.UnlockAchievement` is stubbed (`design/03:1458`) with
  nothing to unlock
* Age rating / EULA / privacy policy
* A **playtest / QA plan** — "balance pass" is the only mention; nothing about who plays it before
  strangers do

**Action.** Split M16 into M16a (code: flags, IL2CPP, AppID) and M16b (release: store, ratings,
playtest, Steam Input, achievements), and give M16b its own units. It is the part most likely to be
discovered late.

---

## P3 — minor, but cheap to fix now

### G14. Line-number citations will drift, and the JS is about to freeze forever

I spot-checked ~15 citations; **one was off**: `spec/02` §7.6 cites `17_turns_ai.js:177-219` for
`aiFixDeficit`, which is at `:188` (`:177` is `MOVE_ADJ`). `spec/07` §11.10 has it right.

Low impact today — but the plan freezes the JS at M4 and retires it after M12, at which point the
specs become the *only* reference and every line number becomes permanently unverifiable.

**Action.** One citation-audit pass, executed **in the M4 freeze commit**, after which every line
number is correct forever.

### G15. M3's acceptance count omits tokens

M3's "done when" enumerates *"78 registry entries, 36 commanders, 64+4 creatures, 14 spells/traps, 13
structures, 18 forges."* `cards.json` also carries `worker` + `tokens[2]` (Lumen, Shade), and
`counts.tokens = 3`. `design/03` §5.2 marks them *"descriptive only, stats derive from the creating
keyword"* — which is correct, but Lumen and Shade are real board creatures created by `mkToken`
(`06_mana_workers.js:114`), so they need a template, a standee, and art (they have none). Workers
render as `⚒n` glyph chips (`12_render.js:90`), so they need no art — that is worth stating, not
leaving implicit.

**Action.** Add tokens to M3's acceptance count with a note on which need art.

### G16. No performance target or minimum spec

Zero hits for `frame rate`, `performance budget`, `min spec`. `design/02` §4.4 picks URP lighting
settings with no target to justify them. Low risk for a 35-cell card game, but it is the input that
decides shadow/MSAA/post settings and the Steam store's system requirements field.

**Action.** One line in M13: *target 60 fps at 1080p on integrated graphics.*

---

## Appendix A — accuracy audit

Eleven claims verified against the JS with grep. **All correct.**

| # | Claim | Spec | Source verified | Result |
|---|---|---|---|---|
| 1 | Rows crossed into = every row past the attacker up to and including the target; same row ⇒ empty; wall indices clamped out | `spec/03` §0.2, §4.1 | `15_combat.js:7-11` | ✅ exact |
| 2 | Creatures may block while **sick or tapped** — gated only by `blocked` + ownership; **workers** additionally require `!tapped && !sick` | `spec/03` §0.10, §4.2 | `15_combat.js:14-19` | ✅ exact |
| 3 | Universal retaliation; retaliation uses **raw `a`** while attacker damage uses `effA()` (the B2 asymmetry) | `spec/03` §0.5, §7.2-7.3 | `15_combat.js:279, 297` | ✅ exact, asymmetry real |
| 4 | Blocked/open partition is taken **before** the fight, so a blocked attacker stays blocked even after killing its gang | `spec/03` §7 | `15_combat.js:~300` (`const blocked = live.filter(...)`) | ✅ exact |
| 5 | Scour bypass is evaluated **per attacker** in v3 (`kwOf(A)!=='scour' && aIdx!==tIdx`) | `spec/03` §4.4 / B3 | `15_combat.js:251` | ✅ exact |
| 6 | 12-step `BeginTurn`: ply++ → clear decls/cardMenu/moveMana → reset `upaid` → reset unit flags → chrysalis → overcharge → building upkeep → cleanup → syncWorkers → readyWorkers → branch | `PORT_PLAN` M6, `spec/07` §4 | `17_turns_ai.js:49-70` | ✅ exact |
| 7 | `buildingUpkeep` iterates **front → back → center**; `buildingDamage` scans **front → center → back** of the enemy's own rows | `PORT_PLAN` M6 | `17_turns_ai.js:9-10, 27` | ✅ exact |
| 8 | `doHarvest`: `owe` captured **before** harvest and used after; zones `['back','front','center']`; `upaid` credited **in full** even on partial payment; refusal gated on `upkeepOffender()`, not `totalDeficit` | `spec/02` §7.4-7.5 | `17_turns_ai.js:147-174` | ✅ exact, including the stale `owe` |
| 9 | Mana vault: `vaultCap` sums `eff==='vault'` `val` over `ownUnits`; end-of-turn drain clamps `P.mana` to cap | `spec/02` §9.5, `spec/07` §8.2 | `17_turns_ai.js:33-41`, called at `:232`, `:388`, `42_mp_apply.js:264` | ✅ exact |
| 10 | `MoveSpent(c) = c.moved && !(upkeep && !c.moved2 && !c.tapped)`; movement **not** gated by `sick`/`tapped`; `doMove` = vacate → flags → occupy → `syncWorkers` | `PORT_PLAN` M7, `spec/04` | `16_movement.js:26-27, 46-56` | ✅ exact |
| 11 | Constants: opening hand 4, Overcharge cap 3, AI summon guard `>6`, hand-off 380/650 ms, `cleanup` guard 40, mana cap 99 | `spec/07` §20 | `11_deck_builder.js:247`, `06:156`, `17:305`, `17:239,241`, `16:194` | ✅ all exact |

Also confirmed: `checkWin` has **no deck-out and no turn limit** (`17_turns_ai.js:392-407`), matching
`spec/07` §19 and decision C21.

## Appendix B — file coverage matrix

**`src/js/` — 31/31 accounted for.** Verified one-by-one against `PORT_PLAN.md` §3. The "29 scripts"
figure in the project notes is stale (the campaign split into three `10_*` files); the plan already
says so.

**`src/styles/` — 0/6 in the disposition table** (all 6 are inventoried in `spec/09` §1 and covered in
prose by `design/02`). See G10.

**Monkey-patch layer (`22_fx_wrappers.js`) — checked independently, not missed.** `spec/07` §15 tables
every rebinding; `spec/09` §18 turns them into the event list. The residual issue is re-homing, not
omission — see G11.

**MP apply layer (`42_mp_apply.js`) — checked independently, not missed.** The host validators are
correctly nominated as the `CommandProcessor.Validate` specification, and the MP-only divergences are
documented, including the non-obvious one: `MPAPPLY.harvest` pays the orphan deficit **before**
harvesting and rejects when mana is short, whereas solo `doHarvest` harvests first and forgives the
remainder (`spec/02` §8 table, `42_mp_apply.js:10-27`). One residual: G12.

**Not covered anywhere:** `index.html`, `sw.js`, `manifest.webmanifest`, `icon.svg`, `tools/*`,
`dist/`, `assets/sprites/`, the two 38 MB portable-build artifacts. See G10.
