# Spawn Row Duel — De-Monolith Migration Plan

**Source:** `spawn-row-duel-v26.html` (6,896 lines, single self-contained file)
**Split date:** 2026-07-16
**Targets:** maintainable module tree now; Tauri (desktop/Steam) + Capacitor (Android) later — see `docs/PUBLISHING.md`.

---

## TL;DR

The monolith is now `index.html` + `src/styles/*.css` + 29 ordered classic scripts
(`src/js/01_core_defs.js` … `99_boot.js`, load order = filename order, recorded in
`tools/build_manifest.json`). Reassembly is **byte-identical** via `tools/build.py`, so the split
itself cannot have changed behavior. Everything after Stage 1 is incremental hygiene, each stage
independently shippable.

**Classic-script hazard (the one thing to watch):** top-level names in one script are visible to
later scripts, but function declarations now hoist only within their own file. Any code that
*executes during load* of module N and references a name defined in module M>N breaks. Runtime
references (function bodies, event handlers, render calls) are safe. Keep load-time work in
`99_boot.js`.

---

## Stage 1 — Mechanical split *(DONE 2026-07-16)*

- **Goal:** one file → `index.html` + CSS + 29 numbered classic scripts, zero behavior change.
- **Tools:** `tools/split_monolith.py` (cuts the monolith on marked boundaries),
  `tools/build.py` (reassembles from `tools/build_manifest.json`).
- **Why it's safe:** rebuild is proven byte-identical to the monolith; every module passes
  `node --check`; game boots and plays with zero console errors.
- **Done when:** ✅ byte-identity proven, ✅ modules parse, ✅ manual play-through clean.

## Stage 2 — Module hygiene

- **Goal:** each module becomes `'use strict'` + IIFE where safe, with **explicit
  `window.X = X` exports** for every name other modules consume. Shrink the implicit global
  surface to a documented API per module.
- **Why it's safe:** done one module at a time, lowest-risk first (leaf modules like `20_sfx.js`,
  `02_art.js` before hubs like `05_board_state.js`, `12_render.js`). The monkey-patch layers
  (`22_fx_wrappers.js`, `30_resp.js`, `31_ui_shell.js`, `40–44` MP) reassign earlier names —
  those reassignments must go through `window.*` so the patch is visible to all callers.
- **Done when:** every cross-module reference resolves via an explicit `window.*` export;
  game plays identically; a grep for accidental implicit globals comes back empty.

## Stage 3 — ESM conversion

- **Goal:** `<script type="module">` with real `import`/`export`; drop the load-order-by-filename
  convention.
- **Blocker to clear first:** imported bindings are read-only, so the monkey-patch pattern
  (`const orig = render; render = function(...)`) dies under ESM. Refactor to a **patch
  registry**: modules export hookable functions (or an `installPatch(name, wrapper)` registry)
  and FX/RESP/MP layers register wrappers instead of reassigning globals. Do this while still
  classic scripts (end of Stage 2) so it's testable before the syntax change.
- **Why it's safe:** by this point every dependency is explicit (Stage 2), so imports are a
  mechanical rewrite of the `window.*` API; the patch registry is verified under classic
  scripts first.
- **Done when:** no classic scripts remain, no `window.*` writes except deliberate debug
  exports, dev serves via a bundler (Vite) with the same play-through passing.

## Stage 4 — TypeScript (incremental)

- **Goal:** `tsconfig.json` with `allowJs: true` + `checkJs` opt-in per file; rename modules to
  `.ts` one at a time, starting with data-only modules (`01_core_defs`, `03/04` card data) where
  types are pure documentation.
- **Why it's safe:** `allowJs` means zero-risk coexistence; each renamed file compiles or it
  doesn't — no runtime change until types start catching real bugs.
- **Done when:** core game-state types (`G`, `P`, card/creature/structure shapes) are declared
  and the hub modules (`05`, `12`, `15`, `17`) type-check under `strict: false`, tightening later.

## Stage 5 — Packaging

- **Goal:** ship desktop (Tauri → Steam) and mobile (Capacitor → Android) from the Vite build.
  Details, store checklists, and asset strategy live in `docs/PUBLISHING.md`.
- **Why it's safe:** wrappers consume the built web app unchanged; the GitHub Pages build
  (the current test surface) remains the canonical fallback throughout.
- **Done when:** Tauri binary and Android build both run the same play-through as the web
  version; Pages deploy still works from the same source tree.

---

## Working rules

1. **Never edit the monolith again** — `spawn-row-duel-v26.html` is frozen history;
   `src/` is the source of truth and `tools/build.py` produces any single-file artifact needed.
2. One module per commit during Stage 2/3; verify boot + a scripted play-through after each.
3. New load-time code goes in `99_boot.js` only.
4. Pages (`https://lucentll.github.io/RTS-Card-Game/`) stays the mobile test surface —
   commit + push after each verified step.
