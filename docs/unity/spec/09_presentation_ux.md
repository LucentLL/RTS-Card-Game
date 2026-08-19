# 09 — Presentation & UX Subsystem Specification

**Source of truth:** the JS/CSS in `src/js/` + `src/styles/` of `RTS-Card-Game`.
**Target:** Unity 6 (6000.5.5f1), URP, PC/Steam first, mouse + keyboard primary.
**Audience:** an implementer who has never seen the JS. Everything needed is here.

Every claim is cited as `file:line`. Numbers are exact — where the source says `44`, this document
says `44`, not "about 40".

---

## 0. How to read this document

Three tags are used throughout to separate concerns, because the port must keep the first and
discard the third:

| Tag | Meaning |
| --- | --- |
| **[REQ]** | A real design requirement. It must exist in Unity in some form. |
| **[PRES]** | A presentation *choice* (a look, a timing, a color). Port it, but it is art-directable. |
| **[DOM]** | A browser/DOM/CSS workaround. Unity makes it unnecessary. **Do not port.** Listed so the implementer recognises the JS when they read it and does not mistake it for a rule. |

Section 21 collects every **[DOM]** item in one table.

---

## 1. Scope and architecture of the existing view layer

### 1.1 Files covered

| File | Lines | Role |
| --- | --- | --- |
| `src/js/12_render.js` | 457 | Full-board rebuild; every visual state of every cell; card HTML; standees; worker chips; hand; walls; phase track; hit-test snapping |
| `src/js/13_input.js` | 237 | Hand selection, action menu, cell click routing, card placement |
| `src/js/18_inspect_viewers.js` | 155 | Tap/hover-to-inspect, the full-size card renderer, deck/graveyard viewer |
| `src/js/31_ui_shell.js` | 430 | Board fitting/sizing, castle-wall raise/retract, drag-drop, RTS marquee, hover-inspect, selection preview, fullscreen/rotate |
| `src/js/11_deck_builder.js` | 255 | Deck builder (search/filter/sort/curve), solo deck+opponent pickers, menu embers, pool hover-zoom |
| `src/js/20_sfx.js` | 60 | Synthesized Web-Audio SFX bank (23 cues) |
| `src/js/21_fx.js` | 220 | FX overlay engine (15 primitives) + ELEMFX elemental impacts (9 elements) |
| `src/js/22_fx_wrappers.js` | 327 | **Monkey-patch layer**: 27 core functions wrapped to attach FX/SFX; settings overlay; board-angle switch |
| `src/styles/00_base.css` | 130 | Tokens, palette, global input suppression, HUD chrome, phase track |
| `src/styles/01_board.css` | 256 | Mat, matmain, rows, cells, card frames on board, standees, worker tokens |
| `src/styles/02_walls_hud.css` | 139 | The two castle walls, tower windows, deck/graveyard piles, hint box |
| `src/styles/03_cards.css` | 134 | The DM_Template hand-card frame, big inspect card, card action menu |
| `src/styles/04_panels_menus.css` | 242 | Modal panels, viewer panel, FX layer CSS, settings |
| `src/styles/05_overlays_screens.css` | 535 | Board angles, drag/marquee, battlefield scenery, screens, deck builder, sleeves, targeting mode |

Cross-cutting files that mutate presentation and **must** be read together with the above:
`15_combat.js` (injects CSS at runtime, `12_render.js` reads `G.decls`), `16_movement.js`
(block/pick modals), `30_resp.js` (respond bar), `41–44` multiplayer (wraps `render`, freezes input).

### 1.2 The rendering model — immediate-mode, full rebuild

There is **one** function, `render()` (`src/js/12_render.js:6`), and it rebuilds the entire board DOM
from `G` every time anything changes. Every row's `innerHTML` is cleared and re-created
(`src/js/12_render.js:287`, `:333`, `:355`). Event listeners are attached per-cell inside
`decorate()` (`src/js/12_render.js:410`) during that rebuild, so listeners die and are re-created
each frame.

**[REQ]** The view is a *pure function of game state*. There is no view-owned mutable state except:
`G.sel`, `G.atk`, `G.moveFrom`, `G.moveMana`, `G.build`, `G.cardMenu`, `G.decls` — which live on `G`
and are *interaction* state, not rules state. This is the single most important property to preserve:
in Unity, the view subscribes to a state snapshot and reconciles; it never owns truth.

**[DOM]** The full-rebuild strategy itself. Unity should diff/reconcile (pool cell views, update in
place) rather than destroy and recreate. The JS does it because innerHTML replacement was cheaper to
write than diffing.

### 1.3 The wrapper (monkey-patch) spine — critical to understand

`22_fx_wrappers.js` reassigns 27 global functions to versions that (a) capture screen rectangles
*before* the rules mutate state, (b) call the original, (c) fire FX/SFX using the captured rects.
`30_resp.js` then wraps *those*, and `43_mp_intents.js` wraps *those*. Load order is significant and
documented in `src/js/30_resp.js:2-3`.

The net layering, outermost first:

```
MP intents (43)  →  RESP windows (30)  →  FX/SFX (22)  →  core rules (12–17)
```

**[REQ]** The *architecture* this expresses is real and should be kept: **the rules core must not
know about FX.** In Unity this becomes an **event bus**: the rules library emits typed domain events
(`DamageApplied`, `UnitDied`, `CardPlayed`, `AttackDeclared`, `ManaGained`, `TurnStarted`, …) and a
presentation subscriber turns them into animations and sounds. Delete the subscriber and the game
still plays — exactly the property the JS comment claims at `src/js/18_inspect_viewers.js:150-153`.

**[DOM]** Monkey-patching itself, and the "capture the rect before mutation" dance
(`src/js/22_fx_wrappers.js:18-23`, `:177-181`) — that exists purely because the DOM node is destroyed
by the re-render. Unity keeps stable transforms; the event payload carries board coordinates.

---

## 2. Screen / flow map

Screens are full-viewport overlays toggled by `hideAllScreens()` / `showScreen(id)`
(`src/js/10_menus_campaign.js:2-4`). Only one is visible at a time; the duel board is always mounted
underneath.

| Screen id | Purpose | Source |
| --- | --- | --- |
| `mainMenu` | Title, 5 nav buttons (Solo, Campaign, Deck Builder, Multiplayer, Rules) | `index.html:149-172` |
| `soloSelect` | Two-step: pick your deck → pick opponent | `src/js/11_deck_builder.js:194-247` |
| `deckBuilder` | Three-column builder | `index.html:219-253`, `src/js/11_deck_builder.js` |
| `charsel` | Legacy two-step commander picker (still reachable) | `src/js/09_game_start.js:27-48` |
| `campaign` | Hexsphere globe world map (separate spec) | `src/js/10_campaign_globe.js` |
| `mpLobby`, `mpDrop` | Multiplayer (deferred) | `index.html:176-217` |
| *(board)* | The duel itself — never a "screen", always present | `index.html:28-116` |

Modal panels layered over the board (not screens): `buildPanel`, `cpanel` (charge/fund),
`contestPanel` (block chooser / pick chooser / trap prompt), `harvestPanel` (legacy, now unused for
allocation), `viewerPanel` (inspect + deck/GY viewer), `logPanel`, `rulesPanel`, `settingsOverlay`,
`banner` (victory/defeat), `respBar` (priority window).

**[PRES]** Screen entry animation: `.screen-in` fades the backdrop over 0.26 s and rises the box
14 px with `cubic-bezier(.22,.9,.3,1)` over 0.34 s (`src/styles/05_overlays_screens.css:200-204`).

---

## 3. Board presentation

### 3.1 Geometry — the invariant grid

**[REQ]** The board is **7 columns × 5 rows**. `SLOTS = 7`, `C = 7` (`src/js/01_core_defs.js:1`).

Rows, top → bottom (`src/js/05_board_state.js:4`):

| Index | Row key | Owner | Notes |
| --- | --- | --- | --- |
| 0 | `foeBack` | opponent | Opponent stronghold. Enterable by you (siege square). Strikes here drain their life. |
| 1 | `foeFront` | opponent | Contested |
| 2 | `center` | shared | Contested; **only columns 1, 3, 5 hold creatures** |
| 3 | `youFront` | you | Contested |
| 4 | `youBack` | you | Your stronghold |

**[REQ]** Center lanes: `CENTER_LANES = [1,3,5]` (`src/js/01_core_defs.js:2`). Columns 1/3/5 are
creature lanes; columns 0/2/4/6 are structure-only flanking ground
(`centerSlotOK`, `src/js/01_core_defs.js:7`). The center row still renders all 7 cells so column
alignment is preserved across rows (`src/js/12_render.js:332-345`); the 4 flank cells render as
**bare ground** (no border, no background) until they are occupied or become a live target
(`src/styles/01_board.css:177-182`).

**[REQ]** `BASE_COL = 3` — the notional keep column, used as the default wall-strike column
(`src/js/01_core_defs.js:4`, `src/js/12_render.js:329`).

DOM element ids per row: `foeBack`, `foeFront`, `center`, `youFront`, `youBack`
(`index.html:35-48`). The center is wrapped in `.centerwrap` which carries a vertical rotated label
"⚔ CONTESTED CENTER ⚔" (`index.html:40`, styling `src/styles/01_board.css:9-13`).

### 3.2 Board sizing — `fitBoard()`

`src/js/31_ui_shell.js:3-61`. Runs on load, on `resize`, on `fullscreenchange` (+120 ms), and 60 ms
after `startGame` (`src/js/22_fx_wrappers.js:245`). It sets two CSS variables on `.wrap`: `--ch`
(cell height) and `--cw` (cell width). Cell size drives *everything* (fonts, badges, standees all use
`clamp()` against `--ch`/`--cw` or container-query units).

**Flat (Top-Down) path — numbered algorithm:**

1. `topChrome = 6`
2. `handReserve = min(150, max(64, innerHeight × 0.16))`
3. `boardChrome = 28`
4. `ch = min( 280, (innerHeight − topChrome − handReserve − boardChrome) / 4.7, ((innerWidth − 60) / 7.4) / 0.74 )`
5. `ch = max(30, ch)`
6. Set `--ch`.
7. Overflow guard, up to **12** iterations while `ch > 30`: if the document scrolls vertically or
   `.main` scrolls horizontally, `ch −= 3` and re-set. (Skipped entirely in tilted mode.)
8. `gpW = min(9, max(3, innerWidth × 0.007))` — the column gap estimate.
9. `rowAvail = innerWidth − 36 − 6 × gpW`
10. `--cw = max( ch × 0.74, min( rowAvail / 7, ch × 1.5 ) )` — portrait floor 0.74·ch, landscape cap 1.5·ch.

**Tilted ("extreme") path** (`src/js/31_ui_shell.js:29-56`):

1. Disable the `.matmain` transition so a settled (not mid-animation) frame is measured.
2. `--extscale = 1`; `availW = innerWidth − 10`.
3. `hf = 1.36`, `s = 1`. Up to **3** passes:
   a. Set `.matmain` height to `hf × 100%`, force layout.
   b. `che = max(40, youBack.offsetHeight − 2)`; `--ch = che`.
   c. `--cw = min(rowAvail / 7, che × 1.5)`.
   d. `s = max(0.6, min(1, availW / youBack.getBoundingClientRect().width))` — the projected width
      after perspective magnification.
   e. `want = min(1.7, 1.36 / s)`; break if `|want − hf| < 0.02`; else `hf = want`.
4. `--extscale = s`; restore the transition.

**[REQ]** The design requirement here: *the entire 7×5 field plus the hand strip must always be
visible with no scrolling, at any window size, with no letterboxing.* The tilted mode specifically
must grow the field's depth until the projected image fills the mat vertically, then uniformly shrink
if the magnified near row overruns the width.

**[DOM]** The whole iterative measure-and-shrink loop, the `void offsetWidth` layout flushes, and the
`hf`/`extscale` feedback loop. **Unity replaces all of it with a camera.** See §22.

### 3.3 The two board angles — the signature look

Only two angles exist. Persisted in `localStorage` key `srd.angle`; any legacy value that is not the
literal string `'topdown'` folds into `'extreme'` (`src/js/22_fx_wrappers.js:277`).

| Setting label | Internal class | Transform | Perspective |
| --- | --- | --- | --- |
| **Top-Down** | `body.board-topdown` | `translateY(var(--wallY,0%))`, height 100% | none (`src/styles/05_overlays_screens.css:27-28`) |
| **Tilted** (the diorama) | `body.board-extreme` | `translateY(calc(-2% + var(--wallY,0%))) rotateX(45deg) scale(var(--extscale,1))`, `transform-origin: 50% 50%`, height 100%, `transform-style: preserve-3d` | `perspective: 260vh`, `perspective-origin: 50% 44%` on `.mat` (`src/styles/05_overlays_screens.css:34-36`) |

A third, dead legacy mode `body.board-tilt` (`rotateX(32deg)`, orthographic, height 106%) exists in
CSS at `src/styles/05_overlays_screens.css:25-26` and as the static default at
`src/styles/01_board.css:73`. **Do not port it.** The user's locked decision is exactly two angles.

**[REQ] Tilted mode rules:**

1. `--tiltx = 45deg` (`src/styles/05_overlays_screens.css:34`).
2. A 3D context is preserved all the way down: `.row`, `.centerwrap`, `.side`, `.rowsbox`,
   `.cell.hasSprite`, `.spritewrap`, `.spritebob` all get `transform-style: preserve-3d`
   (`src/styles/05_overlays_screens.css:62-64`).
3. **Standees billboard**: a non-laid figure gets `rotateX(calc(-1 × var(--tiltx)))` with
   `transform-origin: 50% 100%` — it cancels the board tilt and stands upright out of the plane
   (`src/styles/05_overlays_screens.css:65`). Its shadow stays flat on the ground
   (`:66`, opacity .85).
4. A **laid** (idle) creature stays in the board plane: `rotateX(6deg)` in tilted mode
   (`:68`), `scale(.86) translateY(6%)` in top-down (`:69`).
5. Scenery props with class `bf-prop.up` billboard the same way
   (`src/styles/05_overlays_screens.css:180-181`).
6. Switching to Tilted **force-enables standees** if they were off, because the diorama is
   meaningless without them (`src/js/22_fx_wrappers.js:281`).

**[REQ]** `--wallY`: a single CSS variable that shifts the whole field vertically to make room for
whichever castle wall is extended. It is folded into each angle's own `translateY` because CSS
transforms are not additive.

| Condition | `--wallY` (top-down) | `--wallY` (tilted) | Source |
| --- | --- | --- | --- |
| Player wall open (`body.wall-open`) | `-14%` | `-12%` | `src/styles/05_overlays_screens.css:44-45` |
| Foe wall open (`body.foewall-open` / foe wall or foe hand hovered) | `+9%` | `+8%` | `:42-43` |
| Draw or Upkeep phase (deck must stay reachable) | `-14%` | `-12%` | `src/styles/02_walls_hud.css:39-40` |
| Touch: a hand card is selected | `-14%` | `-12%` | `src/styles/05_overlays_screens.css:516-517` |

Board shift transition: `0.24s cubic-bezier(.34,1.18,.5,1)` (`src/styles/05_overlays_screens.css:41`).

### 3.4 Battlefield scenery layer

`buildBattlefield(youEl, foeEl)` (`src/js/08_battlefield.js:15-56`), called once per match from
`startGame` (`src/js/09_game_start.js:14`). It injects two siblings as the **first two children** of
`.matmain`, under the rows: `#battlefield` (clipped flat ground) and `#battlefieldProps` (unclipped,
so props can billboard). Both are `pointer-events: none`, `z-index: 0`, inset `-3% -2.5%`
(`src/styles/05_overlays_screens.css:117-119`).

Contents, all seeded from a deterministic PRNG (`bfRng`, `src/js/08_battlefield.js:7-8`) that is
itself seeded randomly per match:

| Element | Count | Placement | Source |
| --- | --- | --- | --- |
| Ground plane, 3 bands: foe territory (top) / churned no-man's-land / your territory (bottom); tinted from `ELEMENTS[el].bg` | 1 | full bleed | `:23`, CSS `src/styles/05_overlays_screens.css:123-127` |
| Grass tuft texture, masked out of the churned middle (mask 0→34%, transparent 42–58%, 66%→) | 1 | territories only | CSS `:129-132` |
| Trench ridges at row seams (35.9% and 63.4–64.1%) + stronghold glow radials | — | ground `::after` | CSS `:134-143` |
| Lane paths down columns 1/3/5, width `--cw × 0.62`, centered via the measured column gap | 3 | `left: calc(50% + k×(--cw + --bfgap))`, `k ∈ {-2,0,2}` | `:24`, `:54-55` |
| Scorched center band (cracks, 3 crater rims, ember patch) | 1 | top 36% → bottom 36% | `:25`, CSS `:150-158` |
| Ember glows (pulse 3.8 s) | 2 | 31%/49%, 73%/52% (2nd delayed −1.9 s) | `:26` |
| Drifting smoke wisps (30 s and 39 s alternate loops) | 2 | left 14%, left 56% | `:27` |
| One huge sweeping cloud shadow (64 s) | 1 | 160%×140% oversize | `:27` |
| Ambient motes, 4 per half, tinted by that side's accent, 6–11 s rise | 8 | random | `:46-52` |
| Rocks / tufts in the side margins (outside the 7-column block) | 6 | x ∈ [1.5,7] ∪ [93,98.5], y ∈ [10,90] | `:33-36` |
| Small tufts along the row seams | 4 | y ∈ [33,36] ∪ [64,67] | `:37-38` |
| Fallen war banners on the frontier | 3 | x ∈ [12,88], y ∈ [46,55] | `:39-40` |
| Braziers with animated flames | 4 | (4.5,10) (95.5,10) (4.5,91) (95.5,91) | `:41-43` |
| Camp tents + stake lines at each edge | 4 | foe y≈7/6.2, yours y≈101.5/101 | `:44-45` |

Degradation: under `prefers-reduced-motion` all animation stops and motes/cloud hide
(`src/styles/05_overlays_screens.css:112-114`); under 880 px wide or 430 px tall the cloud, second
smoke wisp, and motes 5+ are dropped (`:115-118`).

**[REQ]** The design requirement: the board sits on a *diegetic battlefield* — two element-tinted
territories with a scorched contested frontier between them, worn paths down the three lanes, and
scattered war debris. **[PRES]** the specific prop list and counts.

**[DOM]** Building the scenery from CSS gradients and inline SVG. Unity: this is a textured mesh /
decal set, authored once, tinted per-match.

### 3.5 Mat and vignette

`.mat` is the surrounding table: diagonal hatch over a brown gradient, inset shadow
(`src/styles/01_board.css:2-6`). `.mat::after` paints a radial vignette at `z-index: 32`, and `.mat`
carries `isolation: isolate` so that z-index does **not** paint over the walls (23), card menu (30),
or hand (25) (`src/styles/05_overlays_screens.css:184-187`). **[DOM]** — the isolation hack is a
stacking-context fix.

### 3.6 Cell visual states — the complete table

Every cell is `.cell` sized `var(--cw) × var(--ch)`, `container-type: size` (so children can size in
container-query units), `overflow: hidden`, 8 px radius, 1 px dashed border
(`src/styles/01_board.css:110-111`). Data attributes carried for hit-testing: `data-key` (row id),
`data-owner`, `data-which`, `data-slot` (`src/js/12_render.js:292`, `:337`).

| Class | Meaning | Visual | Source |
| --- | --- | --- | --- |
| `backcell` | a back-row cell | solid faint border, faint fill | `src/styles/01_board.css:157` |
| `centercell` | any center cell | cyan-tinted solid border + inner glow | `:184` |
| `centerlane` | center col 1/3/5 | amber inner glow, amber border | `:174` |
| `centerstruct` | center col 0/2/4/6 | **fully transparent** — bare ground — until occupied/targetable | `:177-182` |
| `mineHere` | occupied by you | inset 2 px gold ring | `:185` |
| `foeHere` | occupied by opponent | inset 2 px blue ring; **card content rotated 180°** | `:186`, `:122-124` |
| `tappable` | a legal destination/action for the current interaction | gold border + gold glow, pointer cursor | `:112` |
| `target` | a legal attack/spell target | red border + red glow | `:113` |
| `selected` | generic pick source (move source, send-mana source) | 2 px gold border, 18 px gold glow | `:115` |
| `atksel` | this creature is in the attack group | **2 px gold border + 1.1 s pulse animation + glowing standee + a ⚔ badge pinned top-center** | `:117-120` |
| `intercept` | eligible interceptor | cyan border + cyan glow | `:187` |
| `declAtk` | a declared attacker (Combat v3) | 2 px solid `#d4af37` outline, offset −2 | `src/js/15_combat.js:202` |
| `declTgt` | a declared target | 2 px solid `#e35b4f` outline | `src/js/15_combat.js:203` |
| `declBlk` | a committed blocker | 2 px **dashed** `#7fd0f5` outline | `src/js/15_combat.js:204` |
| `draghover` | pointer is over this cell during a drag | 2 px gold outline, offset −2 | `src/styles/05_overlays_screens.css:53` |
| `marqhi` | inside the live marquee rectangle | 2 px green outline `rgba(120,220,150,.95)` + green glow | `:59` |
| `hasSprite` | has a standee | `overflow: visible`, `z-index: 5` | `src/styles/01_board.css:126` |
| `bldSprite` | standee is a structure | taller/planted standee sizing | `src/styles/05_overlays_screens.css:71-74` |
| `laid` | creature cannot act → figure lies down | grayscale .4, brightness .6, no bob, shadow .4 | `:70-72` |

Targeting mode (`body.targeting`) **overrides** `.cell.target` with a much louder treatment: fill
`rgba(255,150,90,.10)`, 2 px `rgba(255,120,80,.75)` ring, 22 px glow, 1.1 s pulse
(`src/styles/05_overlays_screens.css:531-533`). Rationale in-source: far-row targets must punch
through the vignette.

Press feedback: `.cell.tappable:active`, `.cell.target:active` → `scale(.95)`
(`src/styles/01_board.css:114`).

**[REQ]** All of the above states are gameplay-legibility requirements. The *specific* colors are
**[PRES]**, but the semantic mapping is load-bearing and must survive: **gold = your legal action,
red = enemy target, cyan = interception, green = marquee, dashed = committed block.**

### 3.7 Card rendering on the board

`cardHTML(o, me)` (`src/js/12_render.js:132-166`) produces a mini card that fills the cell
(`position: absolute; inset: 2px`, `src/styles/01_board.css:121`). Four variants:

**Creature** (`:137-147`), classes `card crt <element>-c`, plus `sick` / `tapped`:
- optional banked-mana badge `◆N` top-left (`.bank`)
- optional `FS` badge top-right (`.fsbadge`)
- status chip column top-right (`.stch`): `💤` summoning-sick, `⤧`/`⤧²` moved (once/twice), `⟳` tapped
- `⚒` marker for workers (`.wk`)
- name plate (`.nm`) — Cinzel 700, single line, ellipsis, bottom border in the element color
- art window (`.artwin`) if art exists
- stat bar (`.stats`): attack number left (`.atk`), middle group (`.mid` — element gem + `⚒-N`
  upkeep chip), `♥HP` right (`.hp`)

**Structure** (`:149-155`), class `card bld`: name, art or a large glyph icon (`.bic`), stat bar with
an effect glyph on the left (`◆+N` forge / `⚒train` longhouse / `⚔N` tower / `◈N` vault / `▣` wall /
`☩` reliquary / `⌂` default), `⚒+N` support chip, `♥HP`.

**Command center** (`:133-136`), class `card bld ccx`: gold inset ring, element pips top-right, a
`COMMAND` ribbon along the bottom. *(Command centers were removed from the rules — see
`src/js/04_cards_leaders.js:20`, `findCC` returns null — but the render path survives. Port only if
the campaign reintroduces them.)*

**Face-down charge** (`:157-160`), class `card charge`, `mine` if yours, `ready` if fully funded:
your own shows the card name and `◆inv/cost ✓`; the opponent's shows `?` and the public invested
total. `ready` pulses with the `gleam` animation.

**Face-down trap** (`:161-164`), class `card charge trap`: yours shows `⚠ <name>` + "trap · armed";
theirs shows `⚠` + "set".

Sick/tapped filters: `.sick` = `saturate(.5) brightness(.78)` plus a "zzz"; `.tapped` =
`saturate(.45) brightness(.6)` plus a bottom-centered `⟳` (`src/styles/01_board.css:216-219`).

**Element accent** is delivered by one CSS variable `--ec`, set per element class
(`src/styles/05_overlays_screens.css:444-451`), and reused by the border ring, name-plate underline,
cost circle, and type lozenge. **[REQ]** — one accent color per card drives the whole frame.

**[REQ] Opponent cards render rotated 180°** so they face their owner, Master-Duel style. This is
owner-tagged *per cell*, not per row, because the middle three rows are contested and either side's
card may stand anywhere (`src/styles/01_board.css:122-124`).

### 3.8 Standees (field-art cut-outs) — the diorama's core

`attachSprite(cell, o, me, key, i)` (`src/js/12_render.js:168-179`).

**[REQ] Rules:**
1. Global toggle `window.SPRITES_ON` (default `true`, `src/js/04_cards_leaders.js:111`), flipped by
   the 🧍 Figures button (`src/js/12_render.js:180`). Turning on Tilted forces it back on.
2. Standees are attached to **creatures and structures only**. Workers never get one. Face-down
   traps/charges never get one (`src/js/12_render.js:171-172`).
3. A creature that **cannot act right now** gets `.laid` — the figure lies down.
   `canActNow(o, key, i)` (`src/js/16_movement.js:30-38`):
   - non-creature / worker → always "up" (no pose)
   - on its controller's turn: tapped → down; not sick → up; sick → up **only if** it still has a
     move available and an adjacent empty cell exists
   - on the opponent's turn: up iff `canBlockNow` — i.e. `!o.blocked` (summoning-sick may block;
     tapped may block once)
   Structures never lie down.
4. Structure figures never bob (`src/styles/01_board.css:135`).

Layout (`src/styles/01_board.css:127-134`):
- `.spritewrap` fills the cell (`inset: 0`), `pointer-events: none`, `z-index: 3`
- `.spriteshadow`: elliptical radial gradient, `58cqw × 12cqh`, anchored `bottom: 11%`, centered
- `.spritebob`: bob animation 3.4 s ease-in-out, translateY 0 → −7% → 0, origin `50% 100%`
- `.spritefig`: `height: min(150cqh, 120cqw)`, `max-width: 165cqw`, `object-fit: contain`,
  `object-position: bottom`, drop shadow `0 7px 5px rgba(0,0,0,.5)`
- `.spritefig.fromart` (borrowed square card art, not a true cut-out): `height: min(104cqh, 84cqw)`,
  9 px radius, framed with a box shadow — it reads as a *framed standee* rather than a cut-out
- structure standees (`.cell.bldSprite`): `bottom: 12%`, `height: min(122cqh, 102cqw)`,
  `max-width: 132cqw`; `.fromart` variant `min(110cqh, 92cqw)`

The `cqw`/`cqh` cap exists because tilted-mode cells grow very deep; the width cap stops standees
inflating with depth (`src/styles/01_board.css:131` comment).

**[REQ]** Design requirement: **an on-field unit is represented by a standing cut-out figure hovering
above its card slot, casting a ground shadow, idling with a gentle bob, and lying flat when it cannot
act.** In Unity this is a billboarded quad (or a low-poly/flat 3D piece) with a blob-shadow projector
and an animator with two poses.

**[DOM]** Container-query unit sizing, the `preserve-3d` chain, and the counter-rotate billboard.
Unity billboards natively.

### 3.9 Deck and graveyard on the board — dead code

`positionDeck()` (`src/js/12_render.js:267-275`), `positionGrave()` (`:277-285`),
`.deckslot`/`.graveslot` CSS (`src/styles/01_board.css:159-172`), `renderMinions()` (`:75-93`),
`workerChipRow()` (`:214-237`), and `GUARDIAN_SVG` (`:301-310`) are **defined but never called**
anywhere in `src/js`. Deck and graveyard moved into the castle-wall tower windows (§4.3).
**Do not port.** Flagged so the implementer does not waste time on them.

---

## 4. The castle walls — the tower-window layout

This is the signature HUD. Two crenellated stone battlements slide in from the bottom (yours) and top
(opponent's).

### 4.1 Silhouette and geometry

`#hudbar` (player) — `src/styles/02_walls_hud.css:6-18`:
- fixed, full width, height `clamp(170px, 26vh, 250px)`, `z-index: 23`, `pointer-events: none`
- rest position `translateY(calc(100% - 18px))` — only an 18 px rail shows
- open position `translateY(0)`
- transition `0.24s cubic-bezier(.34,1.18,.5,1)`
- stone: layered repeating gradients (48 px block pitch, 26 px course height) over a radial base
- silhouette: a 60-point `clip-path` polygon giving **tall square towers at 0–21% and 79–100%** and a
  **low crenellated wall across 21–79%** with 8 merlons
- element-tinted top rail (5 px on / 19 px off repeating stripe in `--youelem`) at 9 px tall

`#hudbarFoe` (opponent) — `src/styles/02_walls_hud.css:49-60`: an exact vertical mirror. Height
`clamp(140px, 21vh, 210px)`, rest `translateY(calc(-100% + 46px))` — a 46 px rail shows (taller,
because the foe vitals live on it).

Element tints come from `applyCharacterUI()` (`src/js/07_structures.js:85-100`): it sets
`--youelem` / `--foeelem` on `:root` and writes the element kanji glyph into the four `.hudglyph`
ornaments (two per wall, at 1.6% from each edge, opacity 0.09).

### 4.2 The three windows — **[REQ] the layout contract**

Each wall has two framed dark "windows" (`.twin`) set into its tower squares, plus the hand in the
middle span:

| Zone | Position | Contents | Source |
| --- | --- | --- | --- |
| **Left tower** | `left: 1.6%`, `width: 17.8%` | **Player info**: leader name, ♥ life pool, ◆ mana + ◈ vault cap, ⌂ structures, ⚒ workers, the **⚒ Build** button, and the 5-row worker column | `src/styles/02_walls_hud.css:19-23`, `src/styles/01_board.css:44-46`, `src/js/12_render.js:310-331` |
| **Center span** | 21–79% | The **hand** | `src/styles/03_cards.css:3-4` |
| **Right tower** | `right: 1.6%`, `width: 17.8%` | **Deck pile + graveyard pile**, side by side | `src/styles/02_walls_hud.css:81-84` |

The foe wall mirrors it: foe vitals in its left tower (`.wallvit.foe`,
`src/styles/02_walls_hud.css:75-80`), foe deck + graveyard in its right tower, foe hand backs across
the top edge.

Window vertical insets: player `top:16% bottom:8%`; foe `top:8% bottom:16%`
(`src/styles/02_walls_hud.css:22-23`).

Pile placement inside the right tower: each `.wallzone` is `width: max(8.2%, 44px)`; the deck sits at
`right: calc(2.4% + max(8.2%,44px) + 6px)` and the graveyard at `right: 2.4%`
(`src/styles/02_walls_hud.css:84`, `:90`).

Commander cluster: `.cmdzone.you` is `bottom: 24px; left: 2.2%; width: max(16.6%, 130px)` and slides
with the wall (`translateY(150%)` at rest → `0`) (`src/styles/01_board.css:45`,
`src/styles/02_walls_hud.css:31-32`). `.cmdzone.foe` sits at `top: 48px; right: 14px`.

### 4.3 Deck and graveyard as real card piles

`renderWalls()` (`src/js/12_render.js:34-58`). **[REQ]**

1. Layer count = `min(cardCount, 10)` — one thin div per card, capped at 10
   (`src/js/12_render.js:43`).
2. Each layer `i` offsets by `--li × −1.2px` on the player wall (rising up) and `+1.2px` on the foe
   wall (hanging down) (`src/styles/02_walls_hud.css:101-105`).
3. The **top** layer of a deck pile draws an ornamented, element-tinted **card back**: an emblem halo
   radial, a 135° diagonal weave, a tinted body gradient, a double border, and a `❖` emblem centered
   (`src/styles/02_walls_hud.css:107-117`).
4. The **top** layer of a graveyard pile shows the **actual face-up card art** of the most recently
   destroyed card, desaturated (`saturate(.4) brightness(.82)`) (`src/js/12_render.js:45`,
   `src/styles/02_walls_hud.css:119-121`).
5. An empty pile is a flat dashed vacant slot with a `0` badge (`src/js/12_render.js:40`,
   `src/styles/02_walls_hud.css:123-124`).
6. A count badge rides the top card's outer corner — top-right on yours, bottom-right on the foe's
   hanging pile (`src/styles/02_walls_hud.css:126-131`).
7. Pile aspect ratio 0.72, width 78% of the zone.
8. Tooltip: `"Your deck — N cards"` etc. (`src/js/12_render.js:51`).
9. At rest (wall down) the piles are hidden and a compact `.decline` text line shows instead:
   `Deck: N   GY: M` (`src/js/12_render.js:55-57`, CSS `src/styles/02_walls_hud.css:85-89`).

**[REQ] Deck click behaviour**: `youDeckClick()` (`src/js/17_turns_ai.js:73-77`) — during the **Draw
phase** clicking the deck **draws**; otherwise it opens the deck viewer. During the draw phase the
deck gets a gold ring + 1.4 s pulse (`src/styles/02_walls_hud.css:42`).

### 4.4 Wall raise/retract logic — **[REQ] with heavy [DOM] implementation**

Two independent controllers.

**Hover devices** (`src/js/31_ui_shell.js:69-101`), guarded by `matchMedia('(hover: hover)')`:
- entering the hand pins the wall open (`body.wall-open`)
- while pinned, the wall stays open as long as the pointer is below `bandTop = innerHeight − hudbarHeight − 6`, **or** within 8 px of the commander cluster's rect, **or** over
  `#hand, .cmdzone.you, .wallzone, .wallvit, #cardActions`
- when not pinned, moving within 64 px of the bottom edge reveals the wall
- foe wall: `clientY <= 28` opens it; `clientY > hudbarFoeHeight + 6` closes it; **suppressed while
  `body.targeting`** so aiming at the far row does not raise the wall over your targets

**Touch devices** (`src/js/31_ui_shell.js:102-131`), capture-phase `pointerdown`:
- `EDGE = 36`: tapping within 36 px of the bottom opens your wall; within 36 px of the top opens the
  foe wall (again suppressed while targeting)
- **exactly one wall open at a time** (`openWall` toggles both classes)
- excluded from the edge handler: `button, .inspect, .wallzone, .wallvit, #cardActions, #campaign`

**Off-click (all devices)** (`src/js/31_ui_shell.js:118-130`): a click on empty board ground
retracts both walls **and** deselects a held hand card + its menu. It explicitly **never** clears
`G.atk` or `G.moveFrom` — a fat-fingered miss must not cancel an attack or move mid-action.

**Phase-driven**: during `draw` and `upkeep` the player's wall is force-open so the deck and worker
column are reachable (`src/styles/02_walls_hud.css:34-38`).

**Mutual exclusion**: a selected player card or a pinned player wall forces the foe wall shut with
`!important` (`src/styles/02_walls_hud.css:63`); `body.targeting` then re-asserts a full
`translateY(-100%)` lift on the foe wall at matching specificity
(`src/styles/05_overlays_screens.css:524-527`).

**[REQ]** The design requirement: *the walls are the HUD; they auto-reveal when you reach for them
and get out of the way of the board otherwise; only one is ever open; the board slides to make room.*

**[DOM]** `:has()` selectors, `!important` specificity duels, capture-phase listeners, the
`pointerleave` fallbacks, and the whole "which CSS rule wins" arms race. Unity: one state machine
with an enum `{None, Player, Foe}` and an animation on each wall.

### 4.5 Vitals display

Rendered by `renderCmdZone(owner)` (`src/js/12_render.js:310-331`):
- `♜ <leader name>` in the leader's element color
- `♥ <life>` — large; **when you hold attackers and it is the enemy's**, it becomes `.lifeaim`: a red
  ring, red tinted background, 1.1 s pulse, pointer cursor, and clicking it routes a wall attack
  (`src/js/12_render.js:327-330`). Its hit area is padded out by `12px 16px` with negative margins so
  it is a comfortable thumb target (`src/js/15_combat.js:209`).
- For you only: `◆mana` + `◈vaultCap` chip, then `⌂structures  ⚒workers`, then the **⚒ Build** button
  (disabled outside the action phase), then the worker column.
- `manaStr(owner)` (`src/js/12_render.js:2-5`) shows `◆N`, and if `vaultCap > 0`, a dimmed `◈cap`
  chip whose tooltip explains that unspent mana above the cap drains at end of turn.
- Corner badge `.pbadge.you` (`index.html:58-61`) duplicates the vitals when the wall is *down*, and
  is hidden when the wall is up (`src/styles/00_base.css:56`).

Aiming raises `.cmdzone.foe` to `z-index: 46` and ghosts `#turnLabel` to opacity .15 because they
overlap the enemy ♥ (`src/js/15_combat.js:207-208`).

---

## 5. The hand

### 5.1 Rest / expanded states

`.hand` is absolutely positioned, bottom-anchored, horizontally centered, `gap: 3px`, `padding: 0 24px` (`src/styles/03_cards.css:3-4`). Each card `.hc`:

- **rest height** `--peek` = `clamp(34px, calc(var(--hch) × .27), 58px)` — only the name+cost banner
  strip shows (`src/styles/00_base.css:41`, `src/styles/03_cards.css:6`)
- **expanded height** `--hch` = `clamp(96px, calc(var(--ch) × 1.34), 230px)`; width `--hcw` = `--hch × 0.72`
- expands when the hand is hovered, focused-within, or contains a selected card
  (`src/styles/03_cards.css:16-18`)
- individual hover: `translateY(-8%) scale(1.1)`, `z-index: 55`, overflow visible (`:19`)
- selected: `z-index: 56`, 2 px gold outline, 26 px gold glow (`:20-21`)
- transition `height .18s ease, transform .14s ease`, `transform-origin: 50% 100%`

Stacking: the fan arc was **deliberately removed**; cards stack left-to-right with ascending
`z-index` (10 + index) so the peek headers overlap cleanly
(`src/js/31_ui_shell.js:354-360`).

On screens ≥ 700 px the hand is capped at `max-width: 58vw` and cards flex-shrink with
`min-width: 44px`, keeping a 10-card hand inside the wall's centre span between the towers
(`src/styles/03_cards.css:88-91`).

`--handpad` = `--peek + 16px` reserves board space for the resting strip only
(`src/styles/00_base.css:42`).

### 5.2 Opponent's hand

`renderFoeHand()` (`src/js/12_render.js:346-353`): a **flat row** (no fan) of face-down backs flush
with the top edge, capped at **10** backs, plus a count badge showing the true count. At rest each
back is `clamp(14px, 2.4vh, 24px)` tall; hovering the strip (or opening the foe wall) drops them to
full `--hch` (`src/styles/01_board.css:57-63`, `src/styles/02_walls_hud.css:66`).

**[DOM]/[REQ] hybrid**: while `body.targeting`, the foe hand goes `pointer-events: none; opacity: .2`
(`src/styles/05_overlays_screens.css:522`). The *reason* is a DOM bug (the strip sat over the back row
and ate clicks), but the *effect* — the enemy hand fades out of the way while you aim — is good design
and should be kept as an intentional behaviour.

### 5.3 `body.placing`

When a hand card is armed with a board-drop mode, `body.placing` makes **every non-selected hand
card** `pointer-events: none; opacity: .35` (`src/js/15_combat.js:211`, set in
`src/js/31_ui_shell.js:417-419`). Rationale: on small screens the hand overlaps the near rows.
**[REQ]** as a *behaviour* (ghost the hand while placing), **[DOM]** as an implementation.

---

## 6. The card frame (DM_Template) — full specification

One frame design is reused at four scales. Source of the markup:
`renderHand` (`src/js/12_render.js:354-381`) and `inspCardHTML` (`src/js/18_inspect_viewers.js:46-63`);
styling `src/styles/03_cards.css:22-86`.

### 6.1 Anatomy, top to bottom

| Part | Class | Content | Styling |
| --- | --- | --- | --- |
| **Name banner** (doubles as the rest-state peek strip) | `.hchead` | cost circle · element gem · name + type line | Ivory paper plate: greyscale turbulence noise multiplied over `linear-gradient(180deg,#f8f3e6,#e4ddc8 62%,#cfc7ae)`, 1 px bottom border in `--ec`, inner top highlight (`src/styles/03_cards.css:24-28`) |
| **Cost circle** | `.cost` | the ◆ number, no symbol | Circle sized `clamp(18px, peek×.6, 30px)`, radial gradient built from `--ec` (45% ec + 55% white at 35%/28%, ec at 58%, ec+black at edge), 2 px near-black border, inner bevel (`:29-33`) |
| **Element gem** | `.costgem` | inline SVG kanji-in-gem | `clamp(13px, peek×.46, 22px)`, drop shadow (`:35-36`); the gem itself is `elemBadge()` — a radial-gradient circle (accent→color→deep) with the element kanji stroked in white over a dark outline (`src/js/02_art.js:42-45`) |
| **Name** | `.nm` | card name | Cinzel 900, `clamp(9px, hcw×.115, 14px)`, dark `#1a140a` on the ivory plate, single-line ellipsis at rest, wraps when hovered/selected (`:38-40`) |
| **Race / type line** | `.tl` | tribe + subtype, or `Structure` / `Spell` / `Trap` / `Creature` | Hidden at rest; uppercase, letter-spacing .14em, colored `--ec` (`:41-44`). Content from `typeLine()` (`src/js/03_cards_creatures.js:29`) — `"<tribe> <subtype>"`, e.g. `Human Wizard` |
| **Art window** | `.artwin` | card art, `object-fit: cover` | `flex: 3.3` — the **dominant** panel (~55% of the body, DM proportions); 1 px black border, inset ring in `mix(--ec 60%, black 40%)`, 20 px inner vignette (`:46-48`) |
| **Type lozenge** | `.ribbon` | `CREATURE` / `STRUCTURE` / `✦ SPELL` / `⚠ TRAP` | Left-aligned, riding the art/text seam with a negative top margin of `hcw × -.055`; pill radius `8px/60%`; gradient from `mix(--ec 72%, white)` to `mix(--ec 70%, black)`; white text (`:50-56`) |
| **Ability box** | `.rules` | keyword / effect text | White plate with the same paper noise, plus a faint `--ec` watermark radial; 1 px black border, inner white bevel, 6 px radius; body text in EB Garamond `clamp(8px, hcw×.10, 12px)`, **clamped to 3 lines** on hand cards (`:65-70`) |
| **Footer stat bar** | `.stats` | power (left) · ⚒± chip (middle) · ♥HP (right) | (`:58-64`) |
| — power | `.atk` | attack number, **no symbol** | Italic Cinzel 900, `clamp(12px, hcw×.20, 25px)`, **white with a 1 px black outline on all four sides** plus a drop shadow — the DM power number (`:59-60`) |
| — worker chip | `.cap.plus` / `.cap.neg` | `⚒+N` (structures) / `⚒−N` (creature upkeep) | green-on-dark / cream-on-brown pills (`src/styles/03_cards.css:106-108`) |
| — health | `.hp` | `♥N` | `#ff9a8a`, Cinzel 900, black outline (`:61-62`) |

**[REQ]** The frame grammar is a real requirement: **cost circle top-left, element gem beside it, name
centered on an ivory banner, race line under the name, dominant art panel, type lozenge on the seam,
white ability box, and a footer of power / worker-chip / health.** The element color threads through
cost circle, banner underline, art frame, lozenge, and outer ring.

### 6.2 The big (inspect) card

`.hc.big` (`src/styles/03_cards.css:73-80`): fixed `--hcw: 250px`, `--peek: 40px`, width
`min(84%, 250px)`, **aspect ratio 744/1033** (the physical card proportion), always expanded, name
wraps, type line always shown, ability box un-clamped and scrollable at 12.5 px.

### 6.3 The board mini-card

Same conceptual frame compressed into a cell. It uses **container query units** (`cqmin`, `cqw`) so
every element scales with the cell (`src/styles/01_board.css:136-141`,
`src/styles/05_overlays_screens.css:453-464`). The element gem is **hidden below a 74 px container
width** and shown at `15cqmin` above it (`src/styles/05_overlays_screens.css:466-469`).

### 6.4 The deck-builder tile

`dbTileInner()` (`src/js/11_deck_builder.js:88-96`): square art on top, name plate, then the same DM
bottom bar (power / ⚒ chip / ♥) — or a `✦ SPELL` / `⚠ TRAP` ribbon for spells
(`src/styles/05_overlays_screens.css:333-341`). Cost circle top-left, identical recipe to the hand
card's.

### 6.5 Card backs and sleeves

**[REQ]** One card-back design is shared by **three** surfaces: the opponent's hand backs, the deck
pile's top card, and face-down set cards on the board
(`src/styles/05_overlays_screens.css:487-507`). Recipe: emblem halo radial at 50%/44%, 135° diagonal
weave, tinted body gradient (`mix(--sleeve 22%, #131c2e)` → `#0a0f1c`), double border (tinted line +
black gutter), `❖` emblem. `--sleeve` = the owner's element tint.

**[REQ] Optional image skins**: `probeSleeves()` (`src/js/04_cards_leaders.js:167-194`) silently
probes `assets/sleeves/cardback.(png|webp)` and `assets/sleeves/frame_<element>.(png|webp)`. If found,
`html.sleeve-img` / `html.frame-img-<el>` flip on and the procedural art is replaced by the image,
with the card *chrome* (name plate, lozenge, stat bar, rings) still drawn on top
(`src/styles/05_overlays_screens.css:509-519`, frame CSS injected at
`src/js/04_cards_leaders.js:195-208`). **This is a real requirement** — the frame is skinnable per
element without touching card code.

### 6.6 Ability text generation

Two functions produce all rules text shown on cards:

`abilityBrief(card)` (`src/js/13_input.js:78-93`) — the short form on hand cards:
- spells/traps → `spellText(card)` (`src/js/13_input.js:62-71`), a hardcoded sentence per effect:
  `burn` → "**Bolt.** Deal **N** damage to an enemy creature, structure, or face-down card.";
  `raze` → "**Sunder.** Destroy a target enemy **structure**."; `pitfall` → "**Snare.** …";
  `chain` → "**Arc.** Deal **N** to the two highest-attack enemy creatures.";
  `bounce` → "**Riptide.** … (Entrench resists)."; `thornmail` → "**Overgrowth.** … gains **+500/+1000** permanently."
- structures → `Forge ◆N each turn` / `Longhouse. Trains a worker each turn` / `Tower. ⚔N each turn` /
  `Bulwark. Screens the line` / `Reliquary. Recalls the fallen`, joined with ` · ` and a `⚒+N` clause
- creatures → `Upkeep ⚒-N` · `First Strike` · `<keyword name>`, joined with ` · `

`kwName(c)` (`src/js/13_input.js:73-77`) maps the 8 keywords to short labels: `Detonate <n>`,
`Undertow`, `Entrench`, `Ward`, `Reap <n>`, `Chrysalis`, `Scour`, `Overcharge`.

`bldEffectText(eff, val, sup)` (`src/js/18_inspect_viewers.js:20-29`) — the long form in the inspector,
one full paragraph per structure effect plus a support clause.

**[REQ]** Rules text is **generated from card data**, not authored per card. Port this as a
localizable string-template system in C#, not as per-card prose fields.

---

## 7. Card art pipeline

**[REQ]** Art is resolved **from the card's name**, with a documented fallback chain, so an artist can
drop a file in and it appears. Do not replace this with a hand-maintained table.

`slugify(name)` = lowercase, drop a leading `"the "`, strip all non-`[a-z0-9]`
(`src/js/04_cards_leaders.js:53`). `Magmaw → magmaw`; `Snare Pit → snarepit`; `The Tide Spire → tidespire`.

Directory resolution (`src/js/04_cards_leaders.js:56-77`) builds a lazy slug→folder table from the
card data itself:

| Card type | Folder |
| --- | --- |
| creature | `assets/cards/Creatures/<Element>/` |
| spell | `assets/cards/Spells/` |
| trap | `assets/cards/Traps/` |
| structure (incl. forges + grand forges) | `assets/cards/Structures/` |

Probe order = typed folder first, then flat `assets/cards/`, each × extensions.

| Asset kind | Suffix | Extensions (in order) | Function |
| --- | --- | --- | --- |
| Card art (square) | `_cardart` | png, jpg, jpeg, webp | `artURLs` `:79-82` |
| Field cut-out (standee) | `_fieldart` | png, webp, jpg | `fieldURLs` `:119-121` |
| Sprite (legacy, `assets/sprites/`) | `_sprite` | png, webp, jpg | `spritePath` `:113` |

**Card-art fallback chain** (`artFallback`, `src/js/04_cards_leaders.js:92-105`):
1. embedded data-URI (portable build) → 2. remaining typed-folder extensions → 3. flat-folder
extensions → 4. the built-in procedural SVG placeholder from `PLACEHOLDERS[name]` → 5. no src.

**Standee fallback chain** (`spriteFallback`, `src/js/04_cards_leaders.js:130-148`) — **[REQ] the
3-tier art system**:
1. `_fieldart` cut-out, all extensions
2. **borrow the square card art**, adding class `fromart` so it renders as a *framed* standee rather
   than a cut-out
3. built-in placeholder

A negative cache `FIELD_MISS[slug]` records "no cut-out exists" so later renders skip the 404
(`:126`, `:139`).

Procedural placeholder art (`src/js/02_art.js`): every element gets a parametric SVG — an
element-tinted radial background with a kanji watermark at 9% opacity, plus a generic creature body
whose scale and horn count grow with tier (`creInner`, `src/js/02_art.js:19-26`), a forge silhouette
for structures (`:27-32`), and a keep tower for command centers (`:33-38`). Roughly 20 hand-drawn
inline-SVG art pieces exist for specific cards (`ART`, `src/js/02_art.js:55-76`).

**Unity port:** an addressables/Resources lookup by slug with the same 3-tier fallback, resolved
**once at load into a ScriptableObject** (not per-frame, and not via failed requests). The procedural
placeholders become a small generator or a single "art missing" card frame.

---

## 8. Worker column and worker chips

### 8.1 The 5-row worker column (left tower) — **[REQ]**

`workerColumn()` (`src/js/12_render.js:238-265`). A vertical list of **five** rows, ordered to match
the board top→bottom, so the player reads the column against the field:

| # | Label | Your zone | Harvests? | Foe zone shown beside it |
| --- | --- | --- | --- | --- |
| 1 | `Enemy Base` | *(none — you cannot staff it)* | no | foe `back` |
| 2 | `Raid` | `raid` | no | foe `front` |
| 3 | `Center` | `center` | yes | `center` |
| 4 | `Front` | `front` | yes | — |
| 5 | `Base` | `back` | yes | — |

Each row shows `<label>  ⚒<N>`, plus a `<up>✓` ready-count chip when some workers have already
harvested, plus a dimmed `· ⚒<foeN>` chip (opacity .55) for the opponent's count in that physical row.
**The foe's own wall no longer shows worker chips at all** — `#foeWorkerChips` is explicitly emptied
each render (`src/js/12_render.js:54`).

State styling: `.short` (negative) → dark red background `rgba(60,18,20,.9)`; `.none` (zero) →
opacity .5 (`src/styles/00_base.css:110-114`).

Tooltips carry the actual rules explanation per state: shortfall → "settle it at upkeep (move,
sacrifice, or pay)"; positive+ready → "harvests ◆N at upkeep"; raid → "creatures behind enemy lines —
their upkeep (◆N) is paid every turn".

### 8.2 Floating on-board worker chips

`rowFloatChips(key, aiming)` / `wkSlotEl(...)` (`src/js/12_render.js:189-213`). Worker rails were
removed from the board sides; a chip now floats over a row's outer edge **only when it is actionable
right there** (`src/js/12_render.js:203`):

- an **enemy worker stack you can strike** while an attack group is held (class `target`, red border,
  1.1 s pulse) — clicking routes `routeAttack('workers', rowId, owner, which)`
- a **shortfall warning** (`n < 0`, class `short`, red pill with a 1.3 s expanding-ring pulse)

Otherwise `wkSlotEl` returns `null` and the row keeps its full width. Positioned absolutely at
`top: 50%`, `left: 3px` (your side) or `right: 3px` (foe side), `z-index: 7`
(`src/styles/01_board.css:16-17`).

Zone mapping (`zoneForRow`, `src/js/12_render.js:184-188`): for `you` — `youBack`→`back`,
`youFront`→`front`, `foeFront`/`foeBack`→`raid` (raid spans **both** enemy rows), `center`→`center`.

Right-click / long-press any worker chip opens `inspectMinion(owner, which)`
(`src/js/12_render.js:104`, `:194`), which shows a full explanation of how the row's worker figure is
derived (`src/js/15_combat.js:165-171`).

---

## 9. Phase track, board buttons, hint

### 9.1 Phase track

`#phaseTrack` (`index.html:73-79`) — a compact vertical pill list, 5 steps:
`Upkeep`, `Draw`, `Action`, `Combat` (indented sub-step with a `↳` prefix), `End`.

`renderPhaseTrack()` (`src/js/12_render.js:60-73`) — **[REQ] rules:**

1. The track is lit **only on your turn** (`G.turn === 'you' && !G.over`) — no highlight while the
   opponent acts.
2. `shownPhase()` (`src/js/17_turns_ai.js:48`): if the phase is `action` **and** any attackers are
   selected or declared, the shown phase is `combat`.
3. When `combat` is current, **both** `Combat` and `Action` light up — combat is a sub-phase.
4. A step is `done` when its index in `PHASE_ORDER = ['upkeep','draw','action','end']`
   (`src/js/17_turns_ai.js:43`) is less than the current phase's. `Combat` is never marked done.
5. `render()` also stamps `body.phase-<name>` on the document body — this is what drives the
   draw-phase deck pulse and the forced wall-open during draw/upkeep.

Styling (`src/styles/00_base.css:78-83`): idle `#8a8298`; `done` green `#6a8a5f`; `on` = gold gradient
fill with dark text and a 12 px gold glow; `combat.on` uses an **orange** gradient instead
(`#e79a6a → #b5623a`) to distinguish the sub-phase.

### 9.2 Board buttons

`#boardBtns` (`index.html:71-88`) hugs the right screen edge at 44% height, `z-index: 24`, column
layout, `pointer-events: none` with children re-enabled (`src/styles/00_base.css:72-73`).

| Control | Visibility rule | Source |
| --- | --- | --- |
| `#turnLabel` | Always. Text: `Your Turn` / `Opponent…`. Ghosted to opacity .15 while targeting. | `src/js/12_render.js:14`, `src/js/15_combat.js:207` |
| `#phaseTrack` | Always | §9.1 |
| `#harvestBtn` (⛏ Harvest) | Shown only when `turn==='you' && phase==='upkeep' && !busy && !over`. **Disabled** while `totalDeficit('you') − orphanDeficit('you') > 0`, with the tooltip "Settle the worker shortfall first — Move, Pay, or Sacrifice the flagged creatures". Green gradient. | `src/js/12_render.js:16-21` |
| `#endBtn` (End Turn) | Always visible; `disabled = !acting()` — enabled only in the Action phase. Gold gradient, 4 px hard bottom shadow, 2 px press travel. | `src/js/12_render.js:15` |
| `#conscriptBtn` (⚒ Train) | **Permanently hidden** — `display:'none'` every render. Dead control. | `src/js/12_render.js:22` |
| `📜 Log` / `❔ Rules` / `🧍 Figures` | Always. Figures toggles standees and gets a struck-through, 50%-opacity look when off. | `index.html:83-87`, `src/styles/00_base.css:127` |
| `⚙` settings gear | Fixed top-right, 34 px circle, `z-index: 65` | `src/js/22_fx_wrappers.js:305`, CSS `src/styles/04_panels_menus.css:229-231` |

### 9.3 The hint line

`#hint` / `.handhint` (`index.html:111`) — fixed at `right: 10px; bottom: 16%`, width
`clamp(150px, 17vw, 250px)`, italic dim text in a dark rounded box; **hidden when empty**
(`src/styles/02_walls_hud.css:135-137`). `setHint(html)` writes it (`src/js/12_render.js:454`);
`defaultHint()` clears it (`:456`). It may contain inline `<button>`s — e.g. `⚔ Resolve combat`
(`src/js/15_combat.js:233`) and `cancel` during a build placement
(`src/js/06_mana_workers.js:219`).

**[REQ]** A persistent, context-sensitive instruction line is a real UX requirement. Its content is
the primary teaching surface. Full inventory of hint strings worth porting:

| Situation | Text (abridged) | Source |
| --- | --- | --- |
| Hand card selected | `<name> — choose an action above the card.` | `src/js/13_input.js:24` |
| Mode = build | "Tap an empty slot to build (your rows, or a dark center flank) — or tap one of your cards holding ◆ to raise it on top…" | `:32` |
| Mode = summon | "Tap an empty slot in your rows to summon — or tap one of your cards holding ◆ to play on top…" | `:33` |
| Mode = settrap | "Tap an empty slot in your rows to set your trap face-down — ◆1 is placed on it…" | `:34` |
| Mode = cast | "Tap a highlighted enemy target." | `:35` |
| Mode = set | "Tap an empty slot in your rows to set it face-down — ◆1 is banked toward its cost." | `:36` |
| Illegal center deploy | "New cards can't deploy to the contested center — summon to your rows, then march forward." | `:113` |
| Illegal center build | "Build on the dark flanking slots — the glowing lanes are for marching monsters." | `:113` |
| Occupied slot while placing | "That spot is taken — tap a highlighted open slot, or tap the selected card again to cancel." | `:117` |
| Move source picked | "Tap an open space one square away — sideways, forward, back, or diagonal, all the way into the enemy back row. Tap the unit again to cancel." | `src/js/16_movement.js:41` |
| Second upkeep move | "Second move this upkeep — it will **tap** the creature (both actions spent)…" | `:40` |
| Bad move destination | "One square only — sideways, forward, back, or diagonal; monsters stand in the center's three glowing lanes." | `src/js/12_render.js:424` |
| 1 attacker | "**1** attacker · ⚔N — strike any foe or their ♥ life, tap row-mates to join the attack…" | `src/js/13_input.js:159` |
| N attackers | "**N** attackers · ⚔S combined — tap a target to strike, or tap a glowing creature to drop it. (Move is solo only.)" | `:161` |
| Declarations pending | "**N** attacks declared — add more attackers and tap targets to join, then [⚔ Resolve combat]" | `src/js/15_combat.js:233` |
| Draw phase | "**Draw phase.** Click your **deck** to draw a card and begin your Action phase." | `src/js/17_turns_ai.js:72` |
| Upkeep, balanced | "**Upkeep.** Reposition creatures now if you wish (spends their move), then press **⛏ Harvest**…" | `:251` |
| Upkeep, shortfall | "**Upkeep — shortfall ⚒N (<zones>).** Settle each flagged creature — **⤧ Move**, **◆ Pay**, or **✖ Sacrifice** — then ⛏ Harvest." | `:249` |
| Structure tapped | "Structures hold the base — they don't move or fight." | `src/js/13_input.js:135` |
| Set trap tapped | "A set **trap** — it springs on its own when provoked (a summon or an attack) on your opponent's turn." | `:122` |
| Send banked mana | "Move ◆N — tap one of your creatures or structures to store it there (or tap this card to cancel)." | `src/js/14_spells_traps.js:72` |
| Empty back-row aim | "The castle itself is struck via the enemy **♥** — or march a creature into their back row to besiege it." | `src/js/13_input.js:168` |

---

## 10. Inspect system

### 10.1 Two entry paths — **[REQ]**

`FINE_POINTER = matchMedia('(hover:hover) and (pointer:fine)').matches`
(`src/js/18_inspect_viewers.js:2`). This single flag forks the whole inspect UX.

**Fine pointer (PC — the primary target):** `addInspect(host, fn, own)` stores the inspect closure on
the node (`host._inspect`) and attaches **no** click handler
(`src/js/18_inspect_viewers.js:3-5`). A single delegated `mouseover` listener
(`src/js/31_ui_shell.js:294-353`) drives **hover-to-inspect**:

1. Delays: `SHOW_MS = 180` before showing, `HIDE_MS = 120` grace before hiding.
2. Keyed by card identity (`h<index>` for hand, `<rowKey>|<slot>` for board) so re-entering the same
   card does not re-trigger.
3. **Suppressed** when: `body.dragging`; the game is over; a *real* modal is open (the viewer panel
   visible **without** the `hover` class); or any of 15 screen overlays is displayed
   (`mainMenu, charsel, soloSelect, deckBuilder, campaign, banner, buildPanel, settingsOverlay, cpanel, rulesPanel, logPanel, contestPanel, harvestPanel, mpLobby, mpDrop`).
4. An open card-action menu does **not** suppress it — the panel and the menu do not overlap and the
   card text must stay readable while weighing the choices (`src/js/31_ui_shell.js:306-308`).
5. On hide, if a selection preview is pending it falls back to *that* card rather than blanking.

**Touch:** `addInspect` attaches a click handler that inspects **only when the board is inert** — game
over, busy, opponent's turn, draw/end phase, an active RESP window, MP freeze, or (during upkeep) a
foe-owned card (`src/js/18_inspect_viewers.js:11-18`). During your own action phase, taps keep their
game meaning and `onCell`/`onHand` show card text instead. The old `ⓘ` button is gone
(`src/styles/04_panels_menus.css:144` hides any stragglers on fine pointers).

### 10.2 The inspect panel

`#viewerPanel` in `.left` mode (`src/styles/04_panels_menus.css:114-122`): left-anchored, width
`clamp(244px, 25vw, 328px)`, max 46 vw, scrollable, gold-deep border. In `.hover` mode it additionally
becomes **non-blocking**: no backdrop, no blur, `pointer-events: none`, no Close button, and it slides
in from −8 px over 0.14 s (`src/styles/04_panels_menus.css:139-142`,
`src/js/18_inspect_viewers.js:36-41`).

Two renderers:
- `showInspect(title, body)` (`:30-42`) — a titled prose panel; the title splits on `·` into a
  heading and a dim italic subheading.
- `showInspectCard(html, extra)` (`:64-69`) — the full DM-framed card via `inspCardHTML`, plus an
  optional chip strip / funding line beneath it.

`inspectRef(owner, which, i)` (`:70-111`) picks the right presentation per object kind:

| Object | Panel content |
| --- | --- |
| Opponent's face-down charge | Prose only: identity concealed; banked ◆ is public; "could be a creature *or* a structure"; explains the attack-to-provoke rule |
| Your face-down charge | Full card + `face-down — banked ◆N ✓ funded` / `· ◆M more to fund` |
| Opponent's face-down trap | Prose: concealed; may be a trap; "Probe with care." |
| Your face-down trap | Full card + `armed — springs on your opponent's turn` |
| Structure | Full card, HP as `cur/max`, effect text, **plus the upgrade path** (`⬆ Upgrades to: …` with costs and row requirements) for your own structures; a gold `◆N` chip if it holds banked mana |
| Worker | Prose: "**Harvester.** Harvests with its row. Blocks; cannot attack." |
| Creature | Full card, HP `cur/max`, abilities (`Upkeep ⚒-N`, `First Strike`, keyword text), and a **chip strip** beneath: `💤` sick, `⤧`/`⤧×2` moved, `⟳` tapped, gold `◆N` bank, `⚑` contesting the center |

**[REQ]** Status is communicated by **symbol chips under the card, never as prose on the card face**
(`src/js/18_inspect_viewers.js:97`).

### 10.3 Selection preview

`selPreviewKey()` / `showSelPreview()` (`src/js/31_ui_shell.js:387-407`), driven by a `render` wrapper
(`:409-429`): whenever a hand card is selected (menu open, or cast-targeting) **or** a placed card's
menu is open, that card is shown in the left panel so its text stays readable while deciding.
Suppressed while dragging (it would blanket the drop slots) and, on touch only, while targeting.
It never fights a real modal.

### 10.4 Deck / graveyard viewer

`openViewer(zone, owner)` (`src/js/18_inspect_viewers.js:122-148`). Centered modal, 344 px box.

- The **opponent's deck** shows only a count: "Hidden — N cards remaining."
- Otherwise cards are **grouped by `name|cost|type`**, sorted by cost then name, and rendered as a
  wrapping grid of 66 px mini-tiles with an `×N` badge for duplicates.
- Subtitle: `N cards · order hidden, grouped by type` (deck) or `N cards · everything destroyed so far`
  (graveyard).

---

## 11. Input — the tap pipeline, drag-drop, and marquee

### 11.1 The tap pipeline (the canonical path)

Everything routes through two functions: `onHand(i)` (`src/js/13_input.js:2-25`) and
`onCell(key, i, o)` (`src/js/13_input.js:96-176`). **All other input methods (drag, marquee, snap,
off-click rescue) funnel back into these**, which is why every rule, cost check, trap trigger,
win-check, animation and sound stays identical regardless of input method
(`src/js/31_ui_shell.js:133-137` states this explicitly).

**`onHand(i)`** — **[REQ]**:
1. Ignored outside the action phase.
2. Clears `G.atk`, `G.moveFrom`, `G.moveMana`.
3. Tapping the already-selected card **deselects**.
4. Otherwise selects `{kind:'hand', idx:i, mode:null}` and builds the **action menu** — a row of large
   circular icon buttons, each with an icon, label, and a cost/reason sub-label:

| Card type | Buttons | Enabled when |
| --- | --- | --- |
| Structure | `🜂 Build ◆cost`, `⊡ Set ◆1` | `canPay` / `mana ≥ 1` |
| Trap | `⊡ Set ◆1` | `mana ≥ 1` |
| Spell | `✦ Cast ◆cost` | affordable **and** a legal target exists |
| Creature | `⬆ Summon ◆cost`, `⊡ Set ◆1` | `canPay` / `mana ≥ 1` |

Disabled buttons render greyed with the *reason* as the sub-label ("not enough mana", "needs ◆1",
"no legal target") (`src/js/13_input.js:8-9`, CSS `src/styles/03_cards.css:124-134`). The `Set`
button gets a **blue** icon; all others gold (`src/styles/03_cards.css:127`).

**`chooseMode(m)`** (`src/js/13_input.js:27-40`): arms the mode, closes the menu, **drops both castle
walls** (the raised hand covers the near rows; the next tap is a board tap), and writes the mode's
hint.

**`onCell(key, i, o)`** — priority order, first match wins (`src/js/13_input.js:96-176`):

1. Inert during `draw` / `end` phases.
2. `G.upkeep` → tapping your own creature opens the settle menu (`upkeepPick`).
3. `G.build` (build placement armed) → empty deploy cell → `placeBuild`; anything else cancels.
4. A hand card is selected:
   - mode `cast` + legal foe target → `castSpell`; else deselect
   - mode `settrap` on a legal empty cell → `place`
   - mode `summon`/`build` on **your own card holding banked ◆** in a deploy row → play on top
   - any placement mode on a legal empty cell → `place`
   - empty but illegal cell → **explain why** in the hint; keep the selection
   - occupied/illegal while placing → "That spot is taken…"; **keep the selection** (a fat-finger miss
     must not cancel the play)
5. Your set trap → explanatory hint (+ inspect on touch).
6. Your face-down charge with no attack group → open the funding panel.
7. Your structure → build a menu of upgrade buttons (`⬆ <name> ◆cost`, disabled with a reason
   tooltip) plus `◆ Send N` if it holds banked mana; else a hint (+ inspect on touch).
8. Your creature:
   - sick / tapped → menu with only `⤧ Move` and/or `◆ Send`, plus a status tap-hint
   - ready → **toggle** membership in the attack group `G.atk`. First attacker also opens a card menu
     with `⤧ Move`, `◆ Send`, and the tap-hint "⚔ tap any enemy unit, face-down, structure, or their
     ♥ castle wall to strike".
   - **Multiplayer only** constraint: group attackers must share a row (`src/js/13_input.js:152`).
     Solo (Combat v3) allows mixed rows.
9. Attack group held + foe object tapped → `routeAttack('unit', key, i)`.
10. Attack group held + empty `foeBack` cell → hint pointing at the ♥.
11. Touch fallback: tapping a foe card with no group held inspects it.

**[REQ] Addressing subtlety** (easy to get wrong in the port): `inspectRef` is addressed by the **row's
owner**, not the occupant's — a foe raider standing in your front row lives in `cellArr('you','front')`
(`src/js/13_input.js:170-172`, `src/js/31_ui_shell.js:402-404`).

### 11.2 Tap forgiveness — `snapLegalCell`

`snapLegalCell(x, y)` (`src/js/12_render.js:383-392`). **[REQ] as a behaviour; [DOM] in its cause.**

Algorithm:
1. Enumerate every `.cell.tappable` and `.cell.target` (i.e. every *legal* cell for the current
   interaction).
2. For each, compute squared distance from the point to its **projected screen rect**:
   `dx = max(rect.left − x, x − rect.right, 0)`, same for `dy`, `d = dx² + dy²`.
3. Ties (a point inside two overlapping rects, `d = 0`) break by squared distance to the rect centre,
   scaled by `1e-6` so it only ever acts as a tiebreak.
4. Return the best cell if `d ≤ 44² = 1936` (a 44 px radius, the standard touch target).

Applied in three places:
- `onCellRouted` (`src/js/12_render.js:394-405`): only **empty**-cell taps snap, and only when the tap
  landed on a cell that is *not* lit; a tap on an occupied card is always an intentional card
  interaction.
- `decorate`'s move branch (`src/js/12_render.js:422-426`): a near-miss during a move becomes a real
  move rather than a dead tap.
- The global off-click handler (`src/js/31_ui_shell.js:122-126`): a near-miss on bare mat beside a lit
  cell lands **on that cell** instead of deselecting.

`snapContext()` (`src/js/12_render.js:393`) gates it: only while moving, building, placing a hand card
(non-cast), or holding a valid attack group.

**Why it exists**: the tilted board's CSS 3D transform defeats `elementFromPoint`, so a tap on a cell's
*visual* position can resolve to the mat. Lit cells are legal by construction, so routing any nearby
tap to one is always safe. **In Unity a proper 3D raycast against the cell colliders removes the root
cause**, but the *forgiveness* itself (snap to nearest legal cell within ~44 px screen radius) is still
worth keeping for controller/mouse comfort — it is an accessibility win, not just a patch.

### 11.3 Drag and drop

`src/js/31_ui_shell.js:137-292`. Three drag kinds share one gesture machine.

**Begin** (`begin(e)`, `:170-194`) on `pointerdown` (capture phase):
- rejected if not the primary pointer, not the left button, not your turn, busy, over, or in
  draw/end phase; also rejected on `.inspect` and `button` targets
- `.hc[data-hand]` → `kind: 'hand'` (action phase only)
- `.cell[data-key]` holding **your ready creature**, and **`G.atk` is empty**, and `canMoveCard(k,i)`
  → `kind: 'board'`
- otherwise, on non-touch pointers, in the action phase, with no move/hand selection, and the target
  inside `.mat` but not on a hand card / worker chip / card menu → `kind: 'marquee'`

**[REQ]** "No board drag while an attack group is held" is a deliberate rule: building the group is
tap-tap-tap and a slightly rolled tap must not become `startMove` (which would wipe `G.atk`).

**Threshold** (`:277`): Manhattan distance `|dx| + |dy| >` **7** for mouse/pen, **15** for touch. Below
threshold the gesture stays a click.

**Start** (`startDrag()`, `:220-243`):
- Hand: derive the mode from the card type (`building→build`, `trap→settrap`, `spell→cast`,
  else `summon`), then **mirror the action menu's affordability gate** — refuse to begin a drag that
  could not legally drop, writing the reason to the hint. Then create the ghost, set
  `body.dragging`, set `G.sel`, clear `G.atk`/`G.moveFrom`, and `render()` to light the legal slots.
- Board: create the ghost and call `startMove(k, i)` — which lights the legal destinations.
- Marquee: create the selection box element, set `body.marqueeing`.

**Ghost** (`makeGhost()`, `:163-169`): a clone of the source node (for a board drag, the inner `.card`,
not the cell), fixed-positioned, `translate(-50%,-56%) rotate(-2deg) scale(1.05)`, opacity .92, drop
shadow; the inspect button and standee are stripped from the clone
(`src/styles/05_overlays_screens.css:48-51`).

**Hover target** (`cellUnder(x,y)`, `:147-162`): `elementFromPoint` first; if that hit is a legal cell,
use it; otherwise fall back to the **same 44 px nearest-legal-rect snap** as `snapLegalCell`.

**Drop** (`dropOn(e)`, `:245-256`):
- hand drag → `onCell(k, i, occupant)` — **exactly the tap path**
- board drag → only onto a lit `tappable` cell → `doMove(k, i)`; otherwise fall through and cancel
- a failed drop cancels the selection (`cancelSel`) rather than leaving it armed

**Pointer identity** (`mine(e)`, `:276`): only the pointer that began the drag drives or ends it — a
second finger cannot hijack or end it.

**Click suppression** (`:289`): a capture-phase click listener eats the click the pointer sequence
emits after a drag.

**[DOM]** `grabCapture()` / `dropCapture()` (`:141-145`): the drag re-renders the DOM, which destroys
the node holding implicit pointer capture and fires `pointercancel`, self-aborting the gesture. The
fix moves capture to `<html>`. Unity has no equivalent problem.

### 11.4 RTS marquee group selection — **[REQ], the PC signature interaction**

`src/js/31_ui_shell.js:196-219`. Mouse/pen only (touch keeps tap-select).

1. Drag from empty board ground during the **Action phase**, with no move source and no hand card held.
2. `body.marqueeing` sets a crosshair cursor; a `.marquee` box is drawn: 1.5 px
   `rgba(120,220,150,.95)` border, `rgba(120,220,150,.16)` fill, 2 px radius, inner glow
   (`src/styles/05_overlays_screens.css:56-58`).
3. Live highlight: every **own ready creature** (`kind==='creature' && owner==='you' && !worker && !sick && !tapped`) whose screen rect intersects the marquee gets `.cell.marqhi` — a green outline.
4. On release (`finishMarquee`, `:206-219`):
   - **No hits** → clear any existing attack group.
   - **Hits** → `G.atk` becomes exactly that set. Combat v3 allows **mixed rows**.
   - **Multiplayer only**: reduce to the single row with the most hits (legacy MP attack needs one
     shared row).
   - Clears `G.sel`, `G.moveFrom`, `G.cardMenu`; writes the attacker-count hint.

**Unity:** this is a straight screen-space rect → world-object test. Keep the green-box visual language;
it is what tells a PC player this is an RTS.

### 11.5 Attack aim arrow

`src/js/22_fx_wrappers.js:264-273`: hovering any `.cell.target` while an attack group is held draws a
persistent quadratic-Bézier **aim arrow** from the first attacker's cell to the hovered target
(`FX.aimArrow`, arc apex 44 px above the higher endpoint, `src/js/21_fx.js:36-44`). It is red
(`#e35b4f`), dashed `7 9`, with a 0.7 s marching-ants dash animation
(`src/styles/04_panels_menus.css:156-157`). Cleared on mouseout and on every `render()`
(`src/js/22_fx_wrappers.js:233`).

### 11.6 Global input suppression — **[DOM]**

`src/styles/00_base.css:19-27` and `src/js/20_sfx.js:55-59`:
- `user-select: none` game-wide (this is load-bearing on mobile — it stops Android's "search this
  word" popup — and on PC it frees click-drag for the marquee); real `<input>`s opt back in
- `-webkit-user-drag: none` on images/SVG so a PC drag does not "pick up" a translucent copy of card
  art; `dragstart` and `selectstart` are cancelled at capture phase
- `::selection { background: transparent }`
- `-webkit-tap-highlight-color: transparent`
- `.hc, .cell { touch-action: none }` so cards/cells own the gesture instead of scrolling
  (`src/styles/05_overlays_screens.css:47`)

**None of this ports.** Unity has no text selection, no native drag ghost, no scroll.

---

## 12. Card action menu (`#cardActions`)

A floating popover anchored to a card. `placeCardMenu()` (`src/js/12_render.js:108-131`).

**[REQ] Positioning algorithm:**
1. Resolve the anchor: `hand` menus anchor to `#hand.children[i]`; board menus anchor by **global row
   key** and slot (fronts are contested, so the key matters).
2. If the anchor or its card is gone, hide the menu and null `G.cardMenu`.
3. `left = clamp(anchorCentreX − menuWidth/2, 6, viewportWidth − menuWidth − 6)`
4. `top = anchorTop − menuHeight − 12`; if that is `< 6`, flip below: `top = anchorBottom + 12` and add
   class `below`.
5. The menu draws a triangular pointer (8 px CSS border triangle) on the side facing the card, flipping
   with the `below` class (`src/styles/03_cards.css:117-119`).

Hidden entirely when it is not your turn, or the game is busy/over.

Two skins: the board menu (gold gradient buttons, `src/styles/03_cards.css:120-121`) and the
`handmenu` (blue-bordered, large circular icon buttons, `:123-134`).

A `.taphint` italic line under the buttons carries the contextual instruction.

---

## 13. Modal panels

| Panel | Trigger | Content | Source |
| --- | --- | --- | --- |
| **Build** `#buildPanel` | ⚒ Build button | A scrollable list of structure rows: icon, name (with an element dot), italic description, and a `◆cost` button. Unavailable rows go 50% opacity with the button disabled and a *reason* tooltip: "needs a Foundry + a Forge", "need ◆N", "no open space", "no row with ⚒ to spare". Header shows `◆N available`. | `src/js/06_mana_workers.js:201-215`, CSS `src/styles/04_panels_menus.css:2-15` |
| **Charge / fund** `#cpanel` | tapping your face-down charge | Stepper (± 44 px circular buttons), quick-amount buttons, current `◆inv/cost`, `Pour` (purple) and `Flip` (gold) actions | `src/js/14_spells_traps.js:85-109`, CSS `:37-52` |
| **Block chooser** `#contestPanel` | AI attack crosses rows you hold | Title, description, an italic explainer ("Interpose units from any row the strike crosses into…"), a grid of eligible interceptor buttons (name, ⚔/♥, and the row name), a live meta line `your interceptors ⚔D · incoming ⚔A`, an `Interpose N (deal ⚔D)` button (disabled at 0) and a `Let it through` pass. In MP a visible countdown is appended to the pass button. Selected blockers get a gold outline (`.bon`). | `src/js/16_movement.js:115-157` |
| **Single pick** (same panel) | gang-block absorb / retaliation direction | A grid of unit buttons; resolves an index | `src/js/16_movement.js:161-179` |
| **Trap prompt** (same panel) | legacy trap offer | Yes/No | `src/js/14_spells_traps.js:58-67` |
| **Harvest** `#harvestPanel` | legacy colored-mana allocation | Per-color steppers. **Now vestigial** — harvest is automatic and generic (`applyHarvest` just hides it, `src/js/15_combat.js:162`). Do not port. | CSS `src/styles/04_panels_menus.css:16-31` |
| **Respond bar** `#respBar` | priority window | See §13.1 | `src/js/30_resp.js` |
| **Log** `#logPanel` | 📜 Log | Reverse-chronological rich-text battle log, colored by side (`y` gold = you, `e` red = enemy, `s` cyan = resources, `p` purple = spells) | `index.html:138-142`, `src/js/11_deck_builder.js:254` |
| **Rules** `#rulesPanel` | ❔ Rules | A long static how-to-play write-up | `index.html:119-135` |
| **Settings** `#settingsOverlay` | ⚙ | See §19 | `src/js/22_fx_wrappers.js:305-327` |
| **Banner** `#banner` | game over | `VICTORY` (gold) / `DEFEAT` (red) in huge Cinzel, an italic subtitle, and a `Duel Again` button | `src/js/17_turns_ai.js:392-407` |

### 13.1 The respond bar (pause-to-respond)

`src/js/30_resp.js`. A DotP-style priority window. Two faces:

**Acting side** (`RESP.actingGate`, `:35-51`): a slim pill reading "Opponent may respond…" with a
countdown, **no buttons** — you already committed. It sets `G.busy = true` for the duration. The AI's
answer executes *exactly* at window end whether or not it holds a trap — a deliberate **anti-tell**
guarantee (`:49-50`).

**Defending side** (`RESP.defendWindow`, `:57-85`): "RESPOND?" plus one button per armed trap
(`⚠ <trap name>`), a `⏸ Pause` button that swaps in a fresh **15 000 ms** timer to actually think, and
a `Pass`. Timeout = auto-pass.

Duration: multiplayer is always **4000 ms**; solo honours the setting `off | 3 | 4 | 6` seconds,
persisted at `srd.respwin`, default `'4'` (`:6`, `:22`).

Countdown visual: a `scaleX`-only shrinking bar (compositor-friendly); under
`prefers-reduced-motion` it becomes a numeric `Ns` tick updated every 250 ms (`:26-30`,
CSS `src/styles/05_overlays_screens.css:16-21`).

Input lock: `onCell` and `onHand` are wrapped to no-op while `RESP.active` (`:102-103`).

**[REQ]** Port the whole concept: it is the interaction that makes traps meaningful, and its
constant-timing anti-tell property is a genuine design decision, not incidental.

---

## 14. Deck builder

`src/js/11_deck_builder.js` + `index.html:219-253` + `src/styles/05_overlays_screens.css:250-430`.

### 14.1 Layout

Full-screen, three columns via CSS grid `minmax(260px, 0.9fr) 1.3fr 1.7fr`, 14 px gap
(`src/styles/05_overlays_screens.css:266`). Each column is a glass panel with gold corner brackets
(`:267-269`).

| Column | Contents |
| --- | --- |
| **Left — Detail** | The selected card rendered as a **full DM-framed card** (`inspCardHTML`), plus a `− N +` stepper. Empty state: "Select a card to see its details." |
| **Centre — Deck** | Leader (element) picker, deck stats + mana curve, then the deck list as tiles |
| **Right — Pool** | Header with a live match count, search box, sort dropdown, filter chip rows, then the pool grid |

Top bar (`.dbbar`): back chevron, "Deck Builder" title, an inline deck-name input with a ✎ pencil, the
**counter ring**, and a `Save Deck` button.

Counter ring (`refreshDbCounter`, `src/js/11_deck_builder.js:143-151`): a 44 px circle whose
`conic-gradient` fills to `pct = round(total / 40 × 100)`; green (`#9ad17f`) when the deck is valid,
salmon (`#e0a59a`) when not; shows `<total>/<40>`.

Bottom hint line reports the first blocking problem: "Name your deck." → "You have 5 decks — edit or
delete one." → the first validation error → "Ready to save."

### 14.2 State

```js
dbState = { cc, name, cards:{key:count}, editIndex, back, sel,
            filter:{q,type,elem,cost,kw,tag}, sort }
```
(`src/js/11_deck_builder.js:8-9`). `back` is `'menu'` or `'solo'` and controls where Cancel returns.

### 14.3 Constraints — **[REQ]**

`DECK_SIZE = 40`, `MAX_COPIES = 3`, `MAX_DECKS = 5`, storage key `srd.decks.v1`
(`src/js/06_mana_workers.js:37`).

`deckErrors(deck)` (`src/js/06_mana_workers.js:67-79`) returns, in order: unknown leader / unknown card
/ `<name> is off-color` / `<name> must be 1–3` / `Need exactly 40 cards (have N)`.

Only **creatures and spells/traps** are deckable — structures are built, not drawn
(`src/js/06_mana_workers.js:38-43`). Card keys are `<color>|<name>`, spells use `neutral|<name>`.

Changing the leader (`dbPickCC`, `:152`) **removes every now-off-color card** from the deck and clears
the element/keyword/tag filters.

### 14.4 Search, filters, sort — **[REQ]**

**Search** (`:98-107`): case-insensitive substring over the concatenation
`"<name> <type> <color> <keyword> <tribe> <subtype> [first strike]"` — so typing "first strike",
"dragon", or "wizard" all work.

**Filters**, all toggle-off-on-repeat:

| Row | Options |
| --- | --- |
| Type | `All`, `⚔ Creatures`, `✦ Spells` |
| Element | one gem chip per leader color, plus a `◇ Neutral` chip |
| Cost | `◆1 ◆2 ◆3 ◆4 ◆5 ◆6+` — 6 means "cost ≥ 6" |
| Ability | only keywords actually present in the current pool, in fixed order: **First Strike, Detonate, Undertow, Entrench, Scour, Chrysalis, Overcharge, Ward, Reap** (`src/js/11_deck_builder.js:22-25`) |
| Tribe | only tags present, from `TRIBES = ['Human','Dragon']` + `SUBTYPES = ['Wizard','Warrior']` (`src/js/03_cards_creatures.js:27-28`) |

A `Clear ✕` button appears when any filter is active.

**Sort** (`DB_SORT`, `src/js/11_deck_builder.js:16-22`), 5 options, each with deterministic tiebreaks:

| Value | Order |
| --- | --- |
| `type` (default) | type rank (creature 0, building 1, spell 2) → cost ↑ → name |
| `cost` | cost ↑ → name |
| `costdesc` | cost ↓ → name |
| `name` | name |
| `atk` | attack ↓ → name |

**Duplicate-name disambiguation**: when a name appears more than once across the pool (e.g. `Longhouse`
on a dual leader), tiles append the element: `Longhouse (Fire)` (`:85-86`).

### 14.5 Mana curve

`renderDbStats()` (`src/js/11_deck_builder.js:51-61`). **[REQ]**

- Two counters: `⚔ Creatures N`, `✦ Spells N`.
- Seven buckets, costs **0 through 6**, where bucket 6 means "6+" (`Math.min(6, cost)`).
- Bar height = `max(8, round(count / maxBucket × 100))` percent of a 42 px track (52 px at ≥1200 px);
  zero-count buckets render an empty grey stub.
- The count is printed inside the bar; the cost is labelled beneath.
- Tooltip per bar: `◆<c>: N cards`.

### 14.6 Interactions

- **Pool click** adds one copy and selects the card; if the add fails, the hint explains why ("Deck is
  full — 40 cards." / "Already at 3 copies of X.") (`:139-140`).
- **Deck-tile click** selects (does not remove); the tile's `✕` corner removes one copy
  (`stopPropagation` so it does not also select) (`:121-125`).
- **Hover zoom** (`:169-192`), fine pointers only: a 250 px floating preview card follows the cursor,
  preferring the **left** side of the pointer (the pool is the right column), clamped to the viewport,
  showing big art, name, cost+stats line, and the descriptive blurb. It is `pointer-events: none`.
- **Responsive**: at ≥1200 px every font/size steps up (the builder was designed at phone scale); at
  ≤860 px columns compress and the title hides.

### 14.7 Deck / opponent pickers

`renderSoloDeckPick()` (`src/js/11_deck_builder.js:196-227`): a "deck box" grid,
`repeat(auto-fill, minmax(236px, 1fr))`. Each box is painted with a two-stop element gradient derived
from the leader's palette (`deckBoxBg`, `:195`), carries a huge translucent element glyph watermark
(98 px, 13% opacity, bottom-right), element pips, a badge (`♥hp · ⚒workers` for premades, `N/40` or
`invalid` for customs), the name in the element accent color, an italic subtitle, and a `Play ▶`
button that fades in on hover (always visible on coarse pointers). Custom decks add `✎ Edit` and `✕`
buttons; invalid decks are desaturated and unclickable.

Then `renderSoloFoePick()` (`:229-243`) — the same grid for the opponent, plus a `🎲 Random` box.

---

## 15. Main menu ornamentation

`index.html:149-172`, CSS `src/styles/05_overlays_screens.css:206-249`.

- **Rays**: a `repeating-conic-gradient` (0.045 alpha gold, 5° on / 9° off) 120 vmax wide, masked to a
  radial falloff, rotating once per **160 s**.
- **Ring**: an SVG of two concentric circles (r=188 solid, r=154 dashed `3 9`) with the **8 element
  kanji** placed at 45° intervals, counter-rotating once per **90 s**.
- **Embers**: **16** procedural particles, 2–5.5 px, rising 108 vh over 9–23 s with random negative
  delays; 35% of them are gold with a glow (`src/js/11_deck_builder.js:157-165`). Skipped entirely
  under `prefers-reduced-motion`.
- **Title**: Cinzel 900, `clamp(30px, 6.5vw, 54px)`, filled with a metal gradient via
  `background-clip: text`, double drop shadow.
- **Buttons**: icon tile + label + italic sub-label; a diagonal sheen sweeps across on hover
  (`translateX(-130%) → 130%` over 0.5 s).
- Wide screens (≥900×520) left-anchor the nav MTG-Arena style and move the ring to 66%/50%.
- Short screens (≤430 px tall) switch the nav to a 2-column grid and hide the ring/footer.

`renderCharSel` is additionally decorated by the FX layer with a title treatment: `SPAWN ROW DUEL` +
the tagline "raze their base · hold the center · feed the army", pulsing over 3.2 s
(`src/js/22_fx_wrappers.js:255-262`).

---

## 16. SFX — synthesized Web-Audio bank

`src/js/20_sfx.js`. **No audio assets exist.** Every sound is built live from oscillators and
filtered noise.

Two primitives:
- `tone({f, f2, type, a, d, v, delay})` — an oscillator with an optional exponential frequency sweep
  `f → f2`, through an ADSR-ish gain: linear attack to `v` over `a`, exponential decay to 0.0001 over
  `d` (`src/js/20_sfx.js:7-10`)
- `noise({d, v, from, to, type, q, delay})` — a white-noise buffer through a biquad filter whose
  cutoff sweeps exponentially `from → to` over `d` (`:11-17`)

Master gain 0.5, lazily created, resumed on first `pointerdown` (`:4-5`, `:53`).

**Complete cue table (23 cues)** — port these as a synth bank or re-author as samples with the same
character:

| Cue | Recipe | Used for |
| --- | --- | --- |
| `click` | 900 Hz triangle, 50 ms, v .1 | any button press (global capture listener, `:54`) |
| `draw` | noise 1200→5200 Hz, 150 ms | drawing a card (yours only, not the opening deal) |
| `place` | 160→70 Hz + two noise ticks | summoning a creature |
| `set` | 300→120 Hz, 120 ms | setting a card face-down; also the RESP window open |
| `summon` | 220→660 sawtooth + 880 Hz @120 ms + 1320 Hz @200 ms | a face-down flipping up; AI summons |
| `raise` | 90→58 Hz heavy + noise 500→120 + 523 Hz chime @180 ms | raising a structure |
| `whoosh` | noise 2600→300 Hz, 180 ms | generic movement air |
| `hit` | noise 1800→150 + 120→48 Hz | **you take life damage** |
| `clash` | whoosh + 120→48 Hz + noise, both delayed 80 ms | combat resolution |
| `raze` | 78→30 Hz, 700 ms + lowpass noise 700→60, 600 ms | a structure destroyed |
| `spell` | four ascending tones 660/880/1100/1320 Hz, 50 ms apart | casting a spell |
| `trap` | 1400→200 Hz square + noise 3000→500 | a trap springing; a charge destroyed |
| `mana` | 1046 Hz + 1568 Hz @40 ms | mana gained |
| `train` | two 520→380 Hz square blips 120 ms apart | a worker trained |
| `turnYou` | 392 → 587 Hz triangle | your turn begins |
| `turnFoe` | 330 → 247 Hz triangle (descending) | opponent's turn begins |
| `win` | 523/659/784/1046 Hz arpeggio 130 ms apart + 1318 Hz @550 ms | victory |
| `lose` | 392/330/262/196 Hz descending, 160 ms apart | defeat |
| `move` | rising bandpass noise 600→1500 + 220→340 Hz | a creature repositions |
| `block` | 2400→1200 square + 1800→900 triangle + tight noise burst | a defender survives the clash (the parry beat) |
| `swing` | noise 1500→240 + 170→70 sawtooth | attack wind-up |
| `build` | 90→60 Hz + lowpass noise + 392 Hz + 523 Hz chimes | structure construction |
| `shuffle` | 5 descending noise ticks 45 ms apart | duel start |

API: `toggle()`, `isMuted()`, `setMuted(m)`, `setVolume(0..1)`, `getVolume()`, `unlock()`. Every cue is
wrapped in a mute check and a try/catch so audio failure never breaks the game (`:44`).

---

## 17. FX — the overlay engine

`src/js/21_fx.js:2-68`. A fixed full-screen layer `#fxLayer` at `z-index: 60`, `pointer-events: none`,
containing an SVG child sized to the viewport for path effects, plus three siblings appended to
`<body>`: `#hurtVig`, `#turnRibbon`, `#splashFx`.

Every primitive early-returns under `prefers-reduced-motion` except `pop` and `ribbon`
(`src/js/21_fx.js:3`).

### 17.1 The 15 FX primitives

| Primitive | Signature | Behaviour | Duration |
| --- | --- | --- | --- |
| `flyRect` | `(fromRect, toRect, html, ms=300)` | Clones markup at the source rect and transitions it to the target centre at scale .88, fading to opacity .15. Easing `cubic-bezier(.5,-.2,.6,1.15)` (an anticipation curve). | 300 ms + 80 |
| `pop` | `(rect, text, cls)` | Floating number at the rect centre. Rises −30% → −65% (scale 1.2) → −200% (scale .95) while fading. Classes: `dmg` `#ff6a55`, `heal` `#7fe08a`, `mana` cyan (smaller). | 950 ms |
| `slash` | `(rect)` | A 72 px radial white→orange burst, `mix-blend-mode: screen`, scaling .3 → 1.55. | 340 ms |
| `ring` | `(rect)` | A 62 px gold ring expanding .32 → 1.65 while fading. | 600 ms |
| `burstRect` | `(rect, color='#ffd98a', n=10)` | `n` 5 px sparks flung to random angles at 22–62 px. | 620 ms |
| `shake` | `()` | Shakes `.mat` ±4 px on a 5-keyframe 300 ms linear loop. | 300 ms |
| `hurt` | `()` | Red radial vignette `#hurtVig` flashes in over 22% of 550 ms then fades. | 550 ms |
| `ribbon` | `(text, color)` | Full-width banner sliding −48 px → 0 → +48 px, holding 76% of the duration. Cinzel 900, letter-spacing .22em. | 1500 ms |
| `arrow` | `(fromRect, toRect)` | One-shot quadratic Bézier (apex 44 px above the higher endpoint), gold, dashed, marching-ants dash-offset animation. | 440 ms |
| `aimArrow` / `clearAim` | `(fromRect, toRect)` | Persistent red aim arrow with a looping 0.7 s dash animation. Cleared every render. | persistent |
| `splash` | `(unit, owner)` | Master-Duel style card reveal: big square art (min(46vw, 205px)) with a 3 px border and 55 px glow tinted gold (you) / blue (foe), the name in 24 px Cinzel, and a stat line `⚔A / ♥H · FIRST STRIKE` or `STRUCTURE · ♥H`. Pops scale .6 → 1.06 → 1. | 1100 ms |
| `confetti` | `()` | 26 falling 8×13 px strips in 4 colors, random delays up to .6 s, falling 112 vh with a 720° spin. | 2400 ms |
| `flash` | `(rect, color, size=88)` | Soft colored radial puff, `screen` blend, scale .35 → 1.45. | 420 ms |
| `trail` | `(fromRect, toRect, color)` | **5** shrinking after-images placed along the path at `t = i/6`, each 60% of the source size, staggered 22 ms. | ~560 ms |

### 17.2 ELEMFX — elemental impact FX

`src/js/21_fx.js:70-219`. Nine hand-authored signature impacts plus a neutral fallback. Each uses a
tinted core flash (`FX.flash` with the element color) plus ≤12 particles plus one "signature piece".
Particles are tiny inline SVG shapes (10 shapes defined at `:109-119`: teardrop, plume, drop, puff
ellipse, shard, crescent, streak, leaf, square bit, mote) driven by **5 generic CSS motion classes**
parameterised by CSS variables `--dx --dy --rot --dur --psc --fall`
(`src/styles/04_panels_menus.css:177-193`):

| Motion class | Curve |
| --- | --- |
| *(default)* `efxmove` | translate + rotate + scale out, `cubic-bezier(.2,.7,.4,1)` |
| `.grav` | up then **fall** past the origin (46% keyframe up, 100% down by `--fall`) |
| `.in` | **implosion** — starts displaced, converges to the origin at scale .25 |
| `.sway` | 4-keyframe drifting fall with alternating rotation (leaves) |
| `.efx-ray` | a 2 px gradient ray, `scaleX` .2 → 1 from a rotated origin |

| Element | Impact composition |
| --- | --- |
| `fire` | 9 rising teardrop flames + one central plume; flames drift ±14 px and rise 28–66 px |
| `water` | 10 droplets arcing out (alternating sides) then **falling** 46–70 px, plus a splash ellipse at 28% of the cell height |
| `earth` | 9 tumbling shards under gravity (rotating up to ±260°) + a second brown dust flash |
| `wind` | 3 spinning crescents flung to random angles + 8 thin streaks aligned to their own travel direction |
| `forest` | 9 sway-falling leaves (2 of 3 in the base color, 1 in the accent) + a **thorn whip** SVG lash |
| `electric` | a **jagged bolt striking down** onto the cell from 78–110 px above + 10 square sparks |
| `light` | 8 rays in a starburst at 45° intervals + 5 rising motes |
| `dark` | **implosion** — 10 motes converge from a ring, then after 170 ms a violet void ring blooms |
| `divine` | oversized white flood flash + 10 gold/white rays at 36° intervals + 4 rising white motes |
| *(none / unknown)* | `FX.burstRect` with the default gold, 10 or 14 particles |

`big = true` scales displacement by 1.5 and enlarges the flashes (used for a base breach).

`elemShot(fromRect, toRect, el, ms=260)` (`:187-205`): an element-tinted **comet** — a teardrop
trail SVG plus a bright core — rotated to face the target and translated across, leaving 2 trail
embers at t=0.35 and t=0.65. **Electric is special-cased**: an instant jagged bolt instead of a comet.

`trapSnap(rect)` (`:206-218`): red flash + 10 converging purple/red motes + a red ring after 140 ms.

**Unity:** every one of these is a VFX Graph / particle system. The table above is the authoring brief.

### 17.3 Battle cut-in

`showBattle(A, B)` (`src/js/22_fx_wrappers.js:29-40`): a DS-Yu-Gi-Oh style clash overlay at
`z-index: 150`. Up to **3** cards per side (creatures and structures only, no workers/tokens), sliding
in from ±70 px with a ±6° rotation over 0.45 s, a `⚔` VS glyph that pops scale 0→1.35→1 with a −40°→12°
spin, holding for a total of **1100 ms**. Requires at least one card on each side. Toggleable in
settings, persisted at `srd.cutins` (default on).

---

## 18. The FX wrapper table — every hooked function

`src/js/22_fx_wrappers.js`. This is the complete inventory of *when* FX and SFX fire. In Unity each row
becomes an event the rules core emits.

| # | Wrapped function | Line | Presentation triggered |
| --- | --- | --- | --- |
| 1 | `applyDmg(map)` | 18 | `FX.pop('-N','dmg')` on every damaged unit's cell, **captured before the state mutates** |
| 2 | `resolveCombat(A,B)` | 43 | Battle cut-in + `SFX.clash` + `FX.shake` + `FX.slash` + `ELEMFX.elemBurst` at the defender, tinted by the attacker's element. **After**: if the defender survived, `SFX.block` + a blue `FX.flash(72)` + slash — the parry beat |
| 3 | `toGrave(owner,obj)` | 49 | building → `SFX.raze` + grey burst(14) + shake; charge → `SFX.trap` + blue burst; creature → burst in its element color (workers gold) |
| 4 | `doAttack(tgtKey,ti)` | 72 | `fxLunge`: staggered `flyRect` per attacker (70 ms apart), `FX.arrow`, `SFX.swing`, `ELEMFX.elemShot`; the real attack runs after 280 ms |
| 5 | `attackBackRow(defOwner,col)` | 78 | Same lunge; **if life actually dropped**, a `big` elemental burst + shake — the base-breach beat |
| 6 | `attackMinionStack(key,owner,which)` | 89 | Same lunge onto the worker well |
| 7 | `place(idx,mode,which,slot)` | 96 | Card flies hand→slot (300 ms); `SFX.set`/`raise`/`place` by mode; for non-set modes: ring + element-colored flash(92) + burst(9) |
| 8 | `flip(owner,key,slot)` | 112 | Creature: `SFX.summon` + ring + flash + burst; **splash reveal if cost ≥ 4 or First Strike**. Structure: `SFX.raise` + ring + blue flash; splash if cost ≥ 4 |
| 9 | `castSpell(idx,key,i)` | 123 | `SFX.spell` (visuals fire in `resolveSpell`) |
| 10 | `springTrap(...)` | 129 | `ELEMFX.trapSnap` + `SFX.trap` |
| 11 | `doMove(toK,toI)` | 135 | `FX.trail` (bluish) + `flyRect`(240 ms) + ring + `SFX.move` |
| 12 | `aiMoveCreature(...)` | 144 | Same, with a reddish trail |
| 13 | `onCreatureEnter(cr,owner)` | 152 | AI summon parity: `SFX.summon` + ring + flash + burst; splash if cost ≥ 4 |
| 14 | `placeBuild(which,i)` | 161 | `SFX.build` + ring + blue flash + burst |
| 15 | `aiBuild(owner)` | 168 | `SFX.build`, then after 40 ms find the new structure and ring/flash it |
| 16 | `resolveSpell(card,key,i)` | 177 | Per-effect visuals, **target rects captured before resolution**: `burn` → comet in from the caster's side then a flame burst; `raze` → big burst + shake; `chain` → sequential electric bursts on the two highest-attack targets, 110 ms apart; `bounce` → water burst; default → slash + purple burst |
| 17 | `doHarvest()` | 200 | `SFX.mana` + `FX.pop('+N','mana')` at the mana readout |
| 18 | `applyHarvest(...)` | 202 | Same |
| 19 | `applyRes(...)` | 205 | Same, for either player |
| 20 | `trainVillager(owner)` | 212 | `SFX.train` |
| 21 | `dealOpening(o)` | 219 | Sets a flag so the opening deal is silent |
| 22 | `drawCard(o)` | 221 | `SFX.draw` for your draws outside the opening deal |
| 23 | `startTurn(owner)` | 224 | `FX.ribbon('YOUR TURN', gold)` + `SFX.turnYou` / `FX.ribbon("OPPONENT'S TURN", blue)` + `SFX.turnFoe` |
| 24 | `render()` | 231 | Clears the aim arrow; diffs both life totals against the previous frame and pops `±N` (`dmg`/`heal`); **your** loss also fires `FX.hurt` + `SFX.hit` |
| 25 | `startGame(...)` | 240 | Resets life tracking; `SFX.shuffle` + `FX.ribbon('DUEL START')` + `SFX.turnYou`; refits the board after 60 ms |
| 26 | `checkWin()` | 247 | On the transition into game-over: `SFX.win` + `FX.confetti` or `SFX.lose`. Reads the outcome **from state**, not from the banner text, because the campaign may have rewritten the banner |
| 27 | `renderCharSel()` | 255 | Prepends the animated title treatment |

`fxLunge` (`:61-70`) also sets `_atkBusy` for 280 ms, and all three attack wrappers early-return while
it is set — a re-entrancy guard.

`cellElFor(obj)` (`:5-13`) resolves a live game object back to its DOM cell by scanning all five rows
then the worker pools. **[DOM]** — in Unity the object *is* the view reference.

---

## 19. Settings, persistence, and toggles

Settings overlay built at `src/js/22_fx_wrappers.js:305-327`, plus one row injected by the RESP layer
(`src/js/30_resp.js:136-147`).

| Row | Control | Persisted key | Default |
| --- | --- | --- | --- |
| Volume | 0–100 slider (moving it off 0 also unmutes) | *(not persisted)* | 50 |
| Sound | `🔊 On` / `🔇 Muted` toggle | *(not persisted)* | on |
| Board angle | `Top-Down` \| `Tilted` segmented buttons | `srd.angle` | `extreme` (Tilted) |
| Battle cut-ins | `On` / `Off` | `srd.cutins` | on |
| Response window | `Off` \| `3s` \| `4s` \| `6s` (locked in multiplayer) | `srd.respwin` | `4` |
| Surrender | `🏳 Surrender / quit match` → **in-app** two-step confirm | — | — |

The surrender confirm is deliberately in-app rather than `confirm()` because a native dialog
misbehaves inside an installed PWA (`src/js/22_fx_wrappers.js:294-295`) **[DOM]**. The two-step
confirmation itself is **[REQ]**. Campaign surrender routes back to the world map and clears
`CAMPAIGN.target` so the *next* match's `checkWin` does not wrongly resolve it
(`:285-290`) — that is a **rules-adjacent bug fix worth remembering**.

Other persisted UI state: saved decks at `srd.decks.v1` (§14.3).

**Not in settings but toggleable**: `🧍 Figures` (standees) on the board button rail — session-only,
not persisted, and force-enabled by Tilted mode.

---

## 20. Reduced motion and accessibility

`prefers-reduced-motion: reduce` is honoured in four places:

1. FX/ELEMFX primitives early-return (`src/js/21_fx.js:3`, `:72`).
2. CSS hides `.fx-fly, .fx-slash, .fx-ring, .fx-spark, .fx-conf, .efx-*` and disables shake/vignette/
   ribbon/splash animations; the turn ribbon degrades to a simple fade
   (`src/styles/05_overlays_screens.css:102-111`).
3. Battlefield scenery animation stops; motes and cloud hide
   (`src/styles/05_overlays_screens.css:112-114`).
4. The respond-bar countdown becomes a numeric tick instead of a shrinking bar
   (`src/js/30_resp.js:26-30`).
5. Main-menu embers are never created (`src/js/11_deck_builder.js:157`).

**[REQ]** Unity should expose the same switch (a settings toggle, since Unity cannot read the OS
preference portably) and gate the same categories.

**Gaps to fix in the port** (currently absent): no keyboard navigation of any kind, no focus rings, no
screen-reader semantics beyond one `aria-label` on the phase track (`index.html:73`), no colorblind
mode despite red/gold/green/cyan carrying meaning. PC/Steam release should add: full keyboard control,
gamepad support, and a colorblind-safe state palette.

---

## 21. [DOM] inventory — what NOT to port

| # | JS/CSS mechanism | Where | Why it exists | Unity replacement |
| --- | --- | --- | --- | --- |
| 1 | `fitBoard()`'s 12-iteration shrink loop + `--extscale` feedback loop | `src/js/31_ui_shell.js:14-56` | CSS 3D projection size cannot be predicted; must be measured | Orthographic/perspective camera framing a fixed-size board mesh |
| 2 | `void el.offsetWidth` layout flushes | throughout `21_fx.js`, `31_ui_shell.js` | force reflow to restart a CSS animation | Animator/`Play()` restart |
| 3 | Full `innerHTML` rebuild every render | `12_render.js` | cheaper than diffing in JS | Pooled cell views updated in place |
| 4 | Monkey-patching 27 globals | `22_fx_wrappers.js` | no event system existed | Typed event bus / C# events |
| 5 | `cellElFor(obj)` reverse-lookup scans | `22_fx_wrappers.js:5-13` | DOM nodes are anonymous and recreated | Views hold a reference to their model |
| 6 | Capturing screen rects **before** mutation | `22_fx_wrappers.js:18-23`, `:177-181` | the node dies on re-render | Stable transforms; event carries coordinates |
| 7 | `elementFromPoint` fallback + 44 px rect snap for **drops** | `31_ui_shell.js:147-162` | CSS 3D defeats hit-testing | Physics raycast (keep the forgiveness radius as a *feature*, drop the workaround framing) |
| 8 | Pointer capture moved to `<html>` | `31_ui_shell.js:141-145` | re-render destroys the implicit-capture node | N/A |
| 9 | `user-select: none`, `-webkit-user-drag: none`, `dragstart`/`selectstart` cancels, transparent `::selection`, `-webkit-tap-highlight-color` | `00_base.css:19-27`, `20_sfx.js:55-59` | browser text selection / native drag ghost / Android "search this word" | N/A |
| 10 | `touch-action: none` on cards/cells | `05_overlays_screens.css:47` | stop page scrolling | N/A |
| 11 | `isolation: isolate` on `.mat` | `05_overlays_screens.css:184` | contain a z-index:32 pseudo-element | Explicit render order |
| 12 | `!important` specificity duels between wall rules and targeting rules | `02_walls_hud.css:63`, `05_overlays_screens.css:524-527` | CSS cascade conflicts | One state machine |
| 13 | `body:has(...)` selectors driving wall/board state | `02_walls_hud.css` passim | no state variable existed | Explicit UI state enum |
| 14 | `clip-path` polygon for the battlement silhouette | `02_walls_hud.css:18`, `:60` | vector shape in CSS | A sprite / mesh |
| 15 | Card art `<img>` 404-walking fallback chains | `04_cards_leaders.js:92-148` | probing the filesystem over HTTP | Addressables lookup resolved at build/load |
| 16 | `probeSleeves()` `Image()` existence probes | `04_cards_leaders.js:167-194` | same | Asset presence check |
| 17 | Data-URI SVG textures inlined in CSS | `05_overlays_screens.css:123-158` | no asset pipeline | Real textures |
| 18 | Container-query units (`cqmin`, `cqw`, `cqh`) for card/standee scaling | `01_board.css` passim | make one card frame work at any size | Anchors + `CanvasScaler`, or world-space size |
| 19 | `#rotateNote` portrait-lock prompt, fullscreen + orientation-lock on first tap | `31_ui_shell.js:363-384` | mobile browser chrome | N/A on PC |
| 20 | Service worker / PWA manifest | `index.html:7-16` | web install + art caching | N/A |
| 21 | In-app surrender confirm replacing `confirm()` | `22_fx_wrappers.js:294-295` | native dialog breaks in PWAs | Normal Unity dialog (keep the two-step) |
| 22 | `renderMinions`, `workerChipRow`, `positionDeck`, `positionGrave`, `GUARDIAN_SVG`, `#conscriptBtn`, `#harvestPanel` allocation UI | `12_render.js:75,214,267,277,301`; `index.html:82` | dead code from earlier designs | Do not port |

---

## 22. Unity implementation plan — per surface

Guiding split: **the board is a 3D scene; everything else is UI Toolkit.** Justification per surface
below. (uGUI is recommended only where world-space follow behaviour is needed and UI Toolkit's
world-space support in 6000.5 is still awkward.)

| Surface | Recommendation | Justification |
| --- | --- | --- |
| **Board plane, cells, battlefield scenery** | **World-space 3D** (URP scene). One board root with a 7×5 grid of cell anchors; scenery is a textured quad/mesh with element-tinted material properties. | The Tilted diorama *is* a 3D scene faked in CSS. Making it genuinely 3D deletes `fitBoard`, `--extscale`, `preserve-3d`, and the hit-test workarounds outright. Cell highlight states become emissive materials / decal projectors. |
| **Board angle switch** | **Two camera rigs + a Cinemachine blend.** Top-Down = orthographic straight down; Tilted = perspective at ~45° pitch with the framing that fills the viewport. `--wallY` becomes a camera dolly offset. | A camera transition is one animation curve instead of four interacting CSS rules. Keep the 0.24 s duration and the overshoot ease (`cubic-bezier(.34,1.18,.5,1)` ≈ a slight back-out). |
| **Standees** | **World-space billboarded quads** with a blob-shadow projector; two poses (up / laid) on an Animator; idle bob as a small sine offset (3.4 s period, 7% amplitude). | This is literally what the CSS emulates. Billboarding is free. The 3-tier art fallback resolves at load into a material. |
| **On-board mini card** | **World-space quad with a generated texture**, or a small world-space UI Toolkit document per cell. Prefer the former: bake the card face once per card+state and swap the texture. | 35 cells × per-frame layout in UI is wasteful; card faces change rarely (HP, tapped, bank). Bake on state change. |
| **Cell interaction highlights** | **Materials/decals driven by an enum**, not UI overlays. | They must sit *in* the tilted plane and read correctly under perspective. |
| **Castle walls + tower windows** | **UI Toolkit**, screen-anchored, one `VisualElement` per wall with a slide transition. | Pure 2D screen furniture. UI Toolkit's flexbox handles the tower/centre/tower split natively; USS transitions give the slide. The battlement silhouette becomes a 9-sliced sprite. |
| **Hand** | **UI Toolkit**, with drag handled by manipulators. | Cards must overlay the board and reorder by z; UI Toolkit does this cleanly. The rest/expand height animation is a USS transition. |
| **Card frame (all four scales)** | **One UI Toolkit `VisualTreeAsset` + USS**, parameterised by a `--ec` custom property, instantiated at 4 sizes. | Exactly mirrors the current architecture (one frame, one accent variable, four scales) and is the single highest-leverage reuse in the whole UI. |
| **Deck builder** | **UI Toolkit**, `ListView`/`GridView` for pool and deck. | Hundreds of tiles need virtualisation; UI Toolkit's `ListView` provides it. The three-column grid maps 1:1 to flexbox. |
| **Main menu & screens** | **UI Toolkit**, with the rotating ring/rays as a shader or a `VisualElement` with a rotating background, and embers as a small particle system behind the canvas. | Static layout; the ornamentation is decorative. |
| **Inspect panel** | **UI Toolkit**, left-anchored, two modes (blocking modal / non-blocking hover). | Text-heavy; must not eat clicks in hover mode (`pickingMode = Ignore`). |
| **Card action menu** | **UI Toolkit popover positioned from a world-to-screen projection** of the anchoring cell. | Must track a cell that lives in the 3D scene; `Camera.WorldToScreenPoint` + the same clamp/flip algorithm from §12. |
| **Phase track, buttons, hint, vitals** | **UI Toolkit**, screen-anchored. | Static HUD. |
| **Marquee** | **UI Toolkit overlay rect** + a screen-space rect test against each unit's `WorldToScreenPoint` bounds. | Same algorithm, no DOM. |
| **Drag ghost** | **UI Toolkit element following the pointer** for hand drags; for board drags, lift the standee slightly and follow a raycast against the board plane. | Board drags feel better in-world; hand drags start in UI. |
| **FX / ELEMFX** | **VFX Graph** (or Shuriken for the simple ones) prefabs, one per effect, spawned at the target cell anchor; screen-space effects (`hurt` vignette, `ribbon`, `splash`, `confetti`, battle cut-in) as UI Toolkit + full-screen URP renderer features. | §17.2 is a ready-made authoring brief. Damage numbers should be world-space TMP that billboards, so they sit correctly on the tilted plane. |
| **SFX** | **Re-author as audio clips** driven by an `AudioCue` ScriptableObject bank, one per row of §16. | Synthesizing in Unity is possible (`OnAudioFilterRead`) but pointless; the table describes the *character* to recreate. Keep the exact set of 23 cues and their trigger points so the game's rhythm survives. |
| **Event plumbing** | A `IPresentationEvents` interface implemented by the view; the rules library emits into it. | Replaces §18's monkey patches. Every row of that table is one event. |

### 22.1 Board ↔ screen bridge

Because the board is 3D and the HUD is 2D, exactly three bridging services are needed:

1. `BoardRaycaster` — screen point → `CellRef` (row, column), with a configurable **forgiveness
   radius** (default 44 px screen-space) that snaps to the nearest *currently legal* cell. Port
   §11.2's tie-break rule.
2. `CellAnchorProjector` — `CellRef` → screen rect, for popovers, floating chips, and the aim arrow.
3. `MarqueeSelector` — screen rect → set of `CellRef` whose projected bounds intersect it.

---

## 23. Suggested C# types

```csharp
// ---------- board geometry (shared with the rules core) ----------
public enum RowKey { FoeBack = 0, FoeFront = 1, Center = 2, YouFront = 3, YouBack = 4 }
public readonly struct CellRef { public readonly RowKey Row; public readonly int Col; }   // Col 0..6
public static class BoardGeometry {
    public const int Columns = 7, Rows = 5, BaseColumn = 3;
    public static readonly int[] CenterLanes = { 1, 3, 5 };
    public static bool IsLane(int col);
    public static bool CenterSlotOk(int col, bool isStructure);   // structure ⇔ !IsLane
}

// ---------- presentation-only view state ----------
public enum BoardAngle { TopDown, Tilted }
public enum WallState { None, Player, Foe }
public enum CellHighlight { None, Tappable, Target, Selected, AttackSelected,
                            Intercept, DeclaredAttacker, DeclaredTarget, DeclaredBlocker,
                            DragHover, MarqueeHighlight }
public enum StandeePose { Up, Laid, Hidden }
public enum HandCardState { Rest, Expanded, Hovered, Selected, Ghosted /* body.placing */ }

// ---------- interaction state (mirrors G.sel / G.atk / G.moveFrom / G.build / G.cardMenu) ----------
public enum InteractionMode { Idle, HandSelected, Placing, Casting, Moving, SendingMana,
                              Building, Attacking, UpkeepSettling }
public sealed class InteractionState {
    public InteractionMode Mode;
    public int? SelectedHandIndex;
    public PlayMode? ArmedPlayMode;              // Summon | Build | Set | SetTrap | Cast
    public CellRef? MoveSource, ManaSource;
    public readonly List<CellRef> AttackGroup = new();
    public CardMenuAnchor? OpenMenu;
}
public enum PlayMode { Summon, Build, Set, SetTrap, Cast }

// ---------- card view model ----------
public sealed class CardFrameData {
    public string Name, TypeLine, RulesText;     // RulesText generated, see §6.6
    public int Cost; public int? Attack, Health, MaxHealth;
    public Element? Element; public CardKind Kind;   // Creature | Structure | Spell | Trap | Charge | Worker
    public int WorkerDelta;                      // +support (structures) / −upkeep (creatures)
    public int BankedMana; public bool FirstStrike, Sick, Tapped, Moved, MovedTwice;
    public Color Accent;                         // the --ec equivalent
}

// ---------- art ----------
public interface ICardArtResolver {
    Sprite CardArt(string cardName);             // _cardart, typed folder → flat → placeholder
    Sprite FieldArt(string cardName, out bool isBorrowedCardArt);  // _fieldart → cardart → placeholder
    Sprite CardBack(Element owner);
    Sprite FrameOverlay(Element element);        // optional skin, see §6.5
}

// ---------- presentation events emitted by the rules core (replaces §18) ----------
public interface IPresentationEvents {
    void DamageApplied(CellRef target, int amount);
    void UnitDied(CellRef at, CardKind kind, Element? element, bool isWorker);
    void CombatResolved(IReadOnlyList<CellRef> attackers, IReadOnlyList<CellRef> defenders,
                        bool anyDefenderSurvived);
    void AttackLunge(IReadOnlyList<CellRef> attackers, CellRef target, Element? attackerElement);
    void BaseBreached(PlayerId defender, int lifeLost, Element? attackerElement);
    void CardPlayed(int handIndex, CellRef destination, PlayMode mode, Element? element, int cost);
    void CardFlipped(CellRef at, CardKind kind, bool bigReveal);   // bigReveal = cost>=4 || firstStrike
    void SpellResolved(SpellEffect effect, CellRef primaryTarget,
                       IReadOnlyList<CellRef> chainTargets, Element? element);
    void TrapSprung(CellRef at);
    void UnitMoved(CellRef from, CellRef to, PlayerId owner);
    void StructureRaised(CellRef at);
    void ManaGained(PlayerId who, int amount);
    void WorkerTrained(PlayerId who);
    void CardDrawn(PlayerId who, bool isOpeningDeal);
    void TurnStarted(PlayerId who);
    void LifeChanged(PlayerId who, int delta);
    void GameStarted();
    void GameEnded(bool playerWon);
}

// ---------- settings ----------
public sealed class PresentationSettings {
    public BoardAngle Angle = BoardAngle.Tilted;
    public bool Standees = true, BattleCutIns = true, ReducedMotion = false;
    public float MasterVolume = 0.5f; public bool Muted = false;
    public ResponseWindow Response = ResponseWindow.FourSeconds;  // Off | 3s | 4s | 6s
}

// ---------- deck builder ----------
public enum DeckSort { Type, CostAsc, CostDesc, Name, AttackDesc }
public sealed class DeckBuilderFilter {
    public string Query = ""; public CardKind? Type; public Element? ElementOrNeutral;
    public int? Cost;               // 6 means "6 or more"
    public string Keyword, Tag;
}
public static class DeckRules {
    public const int DeckSize = 40, MaxCopies = 3, MaxSavedDecks = 5;
}

// ---------- bridge services ----------
public interface IBoardRaycaster { bool TryPick(Vector2 screenPoint, bool forgiving, out CellRef cell); }
public interface ICellProjector  { Rect ScreenRect(CellRef cell); }
public interface IMarqueeSelector{ IReadOnlyList<CellRef> Inside(Rect screenRect, Func<CellRef,bool> filter); }
```

---

## 24. Load-bearing constants — quick reference

| Constant | Value | Source |
| --- | --- | --- |
| Columns / rows | 7 / 5 | `01_core_defs.js:1`, `05_board_state.js:4` |
| Center creature lanes | columns 1, 3, 5 | `01_core_defs.js:2` |
| Base column | 3 | `01_core_defs.js:4` |
| Deck size / max copies / max saved decks | 40 / 3 / 5 | `06_mana_workers.js:37` |
| Opening hand | 4 cards | `11_deck_builder.js:249` |
| Deck-pile visual layer cap | 10 | `12_render.js:43` |
| Foe hand back cap | 10 | `12_render.js:349` |
| Tap/drop snap radius | 44 px | `12_render.js:391`, `31_ui_shell.js:160` |
| Drag threshold (mouse/pen ‖ touch) | 7 ‖ 15 (Manhattan) | `31_ui_shell.js:277` |
| Hover-inspect show / hide delay | 180 ms / 120 ms | `31_ui_shell.js:296` |
| Wall edge tap zone (touch) | 36 px | `31_ui_shell.js:104` |
| Wall reveal zone (hover, player / foe) | 64 px / 28 px | `31_ui_shell.js:87`, `:98` |
| Tilted board pitch | 45° | `05_overlays_screens.css:34` |
| Tilted perspective / origin | 260 vh / 50% 44% | `05_overlays_screens.css:36` |
| `--wallY` player / foe / phase | −14% (−12% tilted) / +9% (+8%) / −14% (−12%) | `05_overlays_screens.css:42-45`, `02_walls_hud.css:39-40` |
| Wall + board transition | 0.24 s `cubic-bezier(.34,1.18,.5,1)` | `02_walls_hud.css:6`, `05_overlays_screens.css:41` |
| Max cell height | 280 px | `31_ui_shell.js:11` |
| Cell width bounds | `0.74×ch` … `1.5×ch` | `31_ui_shell.js:26` |
| Attack lunge stagger / duration | 70 ms per attacker / 280 ms total | `22_fx_wrappers.js:65-69` |
| Card fly duration (place / move) | 300 ms / 240 ms | `22_fx_wrappers.js:104`, `:141` |
| Battle cut-in duration / cards per side | 1100 ms / 3 | `22_fx_wrappers.js:38`, `:31` |
| Splash reveal threshold | cost ≥ 4, or First Strike | `22_fx_wrappers.js:117` |
| Turn ribbon duration | 1500 ms | `04_panels_menus.css:216` |
| Response window (MP / solo default) | 4000 ms / 4000 ms; pause = 15000 ms | `30_resp.js:22`, `:68` |
| Standee bob period / amplitude | 3.4 s / 7% | `01_board.css:135` |
| SFX master gain | 0.5 | `20_sfx.js:4` |
| Mana in-turn cap | 99 | `15_combat.js:159` |

---

## 25. Open questions for the port

1. **Command centers.** The render path (`cardHTML`'s `ccx` branch, `renderCmdZone`'s `CCS[P.cc]`
   lookup, the gold COMMAND ribbon) still exists, but `findCC()` returns `null`
   (`04_cards_leaders.js:20`) and the back row is now the stronghold. Should the CC card frame be
   ported at all, or only the *leader identity* (name, element, life, workers) shown in the tower?
2. **The `board-tilt` (32°) middle angle** is dead in settings but alive in CSS. Confirmed dropped per
   project memory — but the CSS default at `01_board.css:73` still specifies it, which will confuse
   anyone reading the source. Recommend deleting it from the JS build before the port begins.
3. **Worker chip inspect via right-click** (`12_render.js:104`) is the only right-click binding in the
   game. On PC, right-click is a natural "inspect" verb — should it become the *global* inspect gesture,
   replacing hover-delay?
4. **Keyboard/gamepad control** does not exist. For a Steam release the phase buttons, hand selection,
   cell navigation, and the marquee all need bindings. Not specified anywhere in the source.
5. **Colorblind accessibility**: gold/red/cyan/green currently carry distinct meanings with no
   secondary encoding. A shape or pattern channel should be added to the cell highlight enum.
6. **Damage-number placement in 3D**: on the tilted board, cell centres project to very different
   screen heights front-to-back. Should popped numbers be world-space (correct perspective, may
   overlap) or screen-space at the projected point (current behaviour)?
7. **`applyCharacterUI` mixes rules and presentation** (`07_structures.js:85-100`): it writes the
   element tint variables *and* the rules-panel text. Split cleanly in the port.
8. **The harvest allocation panel** (`#harvestPanel`) is fully styled but functionally dead since mana
   became generic. Confirm it will never return before deleting the layout.
9. **MP-only presentation branches** (shared-row marquee reduction, block-window countdowns,
   `MP.frozen` input locks) are written but multiplayer is deferred. Keep the *hooks* in the view
   (a `IsInputFrozen` flag, an optional countdown on modal choices) so layering netcode later does not
   require touching the view again.
