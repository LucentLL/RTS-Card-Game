# Architecture — Spawn Row Duel

One game, no bundler, no modules. `index.html` loads `src/styles/*.css` then 29 **classic**
`<script src>` tags in filename order. `tools/build_manifest.json` is the canonical order;
`tools/build.py` inlines everything back into a single `dist/spawn-row-duel.html` for portable
builds (GitHub Pages serves `index.html` + `src/` directly and never needs it).

## Module map (`src/js/`, load order = filename order)

| # | File | Owns |
|---|------|------|
| 01 | `01_core_defs.js` | Board constants (`C`, `SLOTS`, `CENTER_LANES`), `ELEMENTS` palette table |
| 02 | `02_art.js` | Parametric placeholder SVG art (`A_BG`, `phArt`) — fallback tier only |
| 03 | `03_cards_creatures.js` | Creature pools per element, spells/traps, `POOLS` |
| 04 | `04_cards_leaders.js` | Commanders (`CCS`), art pipeline (`artPath`/`spriteImg`/fallback chains, `PLACEHOLDERS`), **the global game state `G`** |
| 05 | `05_board_state.js` | Row geometry: `ROWS`, `rowArr`, cell/zone helpers |
| 06 | `06_mana_workers.js` | Generic mana pool, worker/harvest/upkeep math |
| 07 | `07_structures.js` | Structure defs + in-place upgrade chains, grave, `bootstrap()` |
| 08 | `08_battlefield.js` | `buildBattlefield` scenery layer (idempotent, survives renders) |
| 09 | `09_game_start.js` | `startGame` — seeds both players, decks, opening phase |
| 10 | `10_menus_campaign.js` | Screen router (`showScreen`), main menu, hex-territory campaign |
| 11 | `11_deck_builder.js` | Deck builder UI + localStorage saved decks |
| 12 | `12_render.js` | `render()` — the one full-board redraw everything calls |
| 13 | `13_input.js` | Hand/board clicks, card action menu, drag-marquee selection |
| 14 | `14_spells_traps.js` | Spell/trap resolution |
| 15 | `15_combat.js` | `doAttack` & friends — interception, first strike, back-row strikes |
| 16 | `16_movement.js` | One-square moves, move chain, drag-drop targets |
| 17 | `17_turns_ai.js` | Phase machine (`G.phase`), upkeep settle popups, end turn, solo AI |
| 18 | `18_inspect_viewers.js` | Tap/hover-to-inspect, deck & grave viewers |
| 20 | `20_sfx.js` | `SFX` — synthesized WebAudio, no asset files |
| 21 | `21_fx.js` | `FX` — overlay engine (flights, ribbons, splashes; injects `#fxLayer`) |
| 22 | `22_fx_wrappers.js` | Monkey-patches core verbs (`doAttack`, `render`, …) to add FX/SFX |
| 30 | `30_resp.js` | RESP pause-to-respond priority windows — wraps the *FX-wrapped* verbs |
| 31 | `31_ui_shell.js` | Viewport fit (`fitBoard`), hand fan, rotate prompt, wall layout |
| 40 | `40_mp_net.js` | `MPNET` WebRTC transport (password rendezvous, MQTT relay fallback) |
| 41 | `41_mp_sync.js` | `MPMAP` perspective mirror + `MPSER` snapshots |
| 42 | `42_mp_apply.js` | `MPAPPLY` — host re-validates every guest intent as `foe` |
| 43 | `43_mp_intents.js` | Guest intent capture — wraps the FX+RESP-wrapped verbs again |
| 44 | `44_mp_lobby.js` | Guest FX replay, host decisions, protocol pump, lobby UI |
| 99 | `99_boot.js` | `bootstrap();` — one line, must be last |

## The layer spine — LOAD ORDER IS THE ARCHITECTURE

```
core game (01–18)  →  FX wrappers (20–22)  →  RESP (30)  →  UI shell (31)  →  MP (40–44)  →  boot (99)
```

Layers extend the game by **reassigning top-level bindings**:

```js
const _doAttack = doAttack;
doAttack = function(...args){ /* fx / pause / intent */ return _doAttack(...args); };
```

Every call site invokes `doAttack` late (at runtime), so it always gets the outermost wrapper.
RESP deliberately wraps the FX-wrapped versions; MP wraps the FX+RESP-wrapped versions. That
onion only assembles correctly because the files load in this order — which is why:

- **New modules must slot into the numbering.** Core gameplay goes in 01–18, effects in the
  20s, response/priority logic at 30, presentation shell at 31, multiplayer in the 40s.
  Gaps in the numbers are reserved on purpose.
- Adding a file = add it to `tools/build_manifest.json` **and** the `<script>` block in
  `index.html`, in the same position. `build.py` fails loudly if they drift.

## Classic-script semantics (the one hazard class)

Top-level `function`/`var`/`let`/`const` in one script **are** visible to all later scripts
(shared global scope). But function declarations hoist **only within their own file** — in the
old monolith they hoisted across everything.

So the only load-order bug possible: code that **executes during load** of module N (an IIFE
body, a top-level call, a `const x = f()` initializer) referencing a name defined in module
M > N. References that only run at runtime — inside functions called after load, event
handlers, render paths — are always safe. `typeof X === 'function'` guards and tolerant
`window.X` reads are also safe. When in doubt, define data early (01–07) and call late.

## Asset / art pipeline

Three-tier fallback, resolved per `<img>` via `onerror` chains (`04_cards_leaders.js`):

1. **Field cut-out** — `assets/cards/<slug>_fieldart.<ext>` (on-board standees; `spriteImg`)
2. **Card art** — `assets/cards/<slug>_cardart.<ext>` (exts tried: png, jpg, jpeg, webp)
3. **`PLACEHOLDERS[name]`** — built-in SVG data URIs auto-synced from every card/structure def

`slugify(name)` maps card name → filename; drop a file in `assets/cards/` and it just works,
no code change. `EMBEDDED`/`EMBEDDED_FIELD` maps let `tools/embed-art.py` inline art as data
URIs for the portable single-file build. `FIELD_MISS` caches 404s so re-renders don't refetch.

`sw.js` cache policy: `assets/**` is **cache-first forever** (bump `ART_CACHE` to invalidate);
HTML/JS is **network-first** with cache as offline fallback. Iterating on code never
re-downloads the ~14 MB of art — but renaming an art file requires an `ART_CACHE` bump.

## How to add a feature

1. **New game rule / mechanic** — put state on `G` (defined in 04), logic in the matching
   core module (combat → 15, movement → 16, phases → 17), draw it in `12_render.js`'s
   `render()`, wire input in `13_input.js`. Call `render()` after every state change.
2. **Visual/audio flair** — don't touch core. Add a helper to `21_fx.js` / `20_sfx.js`, then
   wrap the core verb in `22_fx_wrappers.js` (save old binding, reassign, call through).
3. **Anything that must pause for the opponent** — extend `30_resp.js`; wrap the FX-wrapped name.
4. **Multiplayer support for a new action** — guest: capture an intent in `43_mp_intents.js`;
   host: validate + apply it in `42_mp_apply.js`; if it has FX the guest must replay, add an
   event to `44_mp_lobby.js`'s `mpReplayFx`. Snapshots (`MPSER`) carry `G` wholesale, so pure
   state usually syncs for free — intents are only needed for *initiating* actions.
5. **New module** — pick a number inside the correct layer band, register it in
   `tools/build_manifest.json` + `index.html`, keep load-time code free of forward references,
   and verify with `node --check` and `py tools/build.py`.

Verify changes on the live surface: commit, push, then test https://lucentll.github.io/RTS-Card-Game/
(mobile testing happens only through Pages; the service worker keeps art cached between builds).
