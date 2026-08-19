# Spec 08 — Campaign Subsystem

**Source of truth:** the JS implementation in this repo. Nothing else is written down.
**Target:** Unity 6 / URP, native C# rewrite. Rules core is pure C#, UI-free, deterministic, serializable.

## 0. Files this spec was extracted from

| File | Lines | Role |
|---|---|---|
| `src/js/10_campaign_globe.js` | 223 | Hexsphere geometry generator + canvas renderer + pointer picking |
| `src/js/10_campaign_dialogue.js` | 229 | Pre-battle Fire-Emblem-style dialogue data + player |
| `src/js/10_menus_campaign.js` | 257 | Campaign state, persistence, map generation, screen flow, turn AI, battle handoff/resolution |
| `src/js/01_core_defs.js` | 31 | `ELEMENTS`, `MAJORS`, `COLORS` — the 8 playable elements |
| `src/js/04_cards_leaders.js` | 224 | `CCS` — 36 commanders (8 solo + 28 dual) that campaign banners map onto |
| `src/js/06_mana_workers.js` | 227 | `deckOf(colors)` — the deck the campaign hands to `startGame` |
| `src/js/09_game_start.js` | 49 | `startGame(youId, foeId, youDeck, foeDeck)` — the battle entry point |
| `src/js/17_turns_ai.js` | 407 | `checkWin()` at line 405 calls back into `campResolve(win)` |
| `src/js/22_fx_wrappers.js` | 327 | Surrender path (lines 286–301) clears the campaign target |
| `src/js/44_mp_lobby.js` | 244 | Lines 128, 139 defensively clear `CAMPAIGN.target` |
| `src/js/31_ui_shell.js` | 430 | Line 108 excludes `#campaign` from a capture-phase board listener |
| `index.html` | — | Line 147 `#banner`, line 165 the Campaign menu button, line 218 the empty `#campaign` host div |

There are **no tests**, no data files, and no other documentation for this subsystem.

---

## 1. Port triage — data vs. scaffolding

The single most important instruction for the implementer: **most of `10_campaign_globe.js` is thrown away.**

### 1.1 KEEP — genuine game data / rules (must be ported exactly)

| Thing | Where | Why it is real |
|---|---|---|
| Hexsphere **topology**: 162 tiles, tile→tile adjacency, tile centre unit vectors, tile corner rings | `10_campaign_globe.js:9-46` | Adjacency drives territory carving, front-line legality and border rendering. Unity needs the same graph. |
| `CAMP_FREQ = 4` and the tile-count invariant `10f²+2` | `10_campaign_globe.js:7`, `10_menus_campaign.js:26` | Save validation depends on it. |
| Map generation algorithm (22 territories, 8 empires, capitals, garrisons) | `10_menus_campaign.js:35-79` | Pure game data generation. |
| `CampaignMap` / `Territory` data model | `10_menus_campaign.js:54-78` | The save format. |
| Campaign state object + persistence + validation | `10_menus_campaign.js:21-27` | |
| Front-line attackability rule | `10_menus_campaign.js:33`, `10_campaign_globe.js:95` | Core rule. |
| Attack → dialogue → `startGame` handoff (exact arguments) | `10_menus_campaign.js:142-148` | |
| Battle resolution, capital absorption + cascade, victory latch | `10_menus_campaign.js:149-182` | Core rules. |
| End-Turn growth + rival-AI expansion | `10_menus_campaign.js:184-198` | Core rules. |
| Defeat condition + `lost` flag | `10_menus_campaign.js:196, 204-209` | |
| All dialogue text tables: `CAMP_CHAMPS`, `CAMP_LINES` (8 elements × 5 buckets × 2 lines), `CAMP_RIVALS` (8 ordered pairs) | `10_campaign_dialogue.js:7-126` | Authored content. Port verbatim into ScriptableObjects. |
| Dialogue line-assembly rules (4 lines, who speaks, when the capital bucket and the rival exchange fire) | `10_campaign_dialogue.js:129-141` | |
| Element visual identity used by the map (colour, glyph, name, lore, hp, wk) | `01_core_defs.js:15-28` | Shared with the rest of the game. |
| Screen-flow state machine (§7) | `10_menus_campaign.js:81-96, 183, 216` | |
| HUD/toast/banner **strings** | throughout | Authored content; keep the wording. |

### 1.2 DISCARD — browser rendering / DOM scaffolding

| Thing | Where | Unity replacement |
|---|---|---|
| Orthographic projection `P(v) = [CX + v.x*R, CY - v.y*R]` | `10_campaign_globe.js:89` | A real perspective/ortho `Camera`. |
| Manual `rot`/`unrot` yaw-pitch matrices | `:83-88` | `Transform.rotation` on a globe root. |
| Painter's-algorithm depth sort of tiles | `:113` | Z-buffer. |
| Corner-based back-face culling with the `z < -0.35` heuristic and the "inset seam bleed-through" workaround | `:116-119` | Normal back-face culling. |
| Per-frame extruded skirt quads drawn as 2D polygons | `:127-133` | Extruded prism mesh, built once. |
| The 7 % `INSET` corner lerp that fakes tile bevels | `:71, :136-138` | Mesh inset / bevel, or a border shader. |
| `shade()` hex-string colour maths + the 1/40 quantised cache | `:90-93` | Material colour + lighting. |
| The hand-rolled directional light `LI` and `lum = 0.62 + 0.5*max(0, n·L)` | `:94, :123` | URP light. Values are a useful *starting look reference*, nothing more. |
| Inverse-ray tile picking (`unrot`, `rr <= 1.06`, max-dot over all 162 tiles) | `:203-209` | `Physics.Raycast` onto per-tile colliders (or one mesh collider + triangle→tile lookup). |
| The `R*EXH` picking-radius correction (a bug fix for the projection) | `:200-204` | N/A. |
| `fit()`, DPR handling, `fitTick` self-heal, `campGlobeStop`, `campGlobeGen`, `campGlobeCleanup`, `campGlobeDrawNow` | `:49-56, :74-82, :102-103, :183` | N/A — all exist because a `display:none` canvas keeps `requestAnimationFrame` alive. |
| `e.stopPropagation()` on the canvas and the dialogue box; the `#campaign` exclusion in `31_ui_shell.js:108` | `:187, :198, :217`; `10_campaign_dialogue.js:172-184` | N/A — global document listeners for the battle board leaked into the map. |
| All injected CSS (`injectGlobeCSS`, `injectDlgCSS`, `injectCampaignCSS`) | `:219-222`; `10_campaign_dialogue.js:188-228`; `10_menus_campaign.js:220-255` | uGUI/UI Toolkit. Layout numbers are a look reference only. |
| `innerHTML` string templating for HUD / confirm box / turn log | `10_menus_campaign.js:98-141, 199-202` | Prefabs + data binding. |

### 1.3 KEEP AS PRESENTATION REFERENCE (re-implement, don't copy)

Idle auto-spin, drag inertia, marker pulse, the typewriter effect, portrait flip, and the "speaking" highlight are genuine *feel* decisions worth reproducing. Exact constants are given in §11 and §10.4.

---

## 2. Element / faction reference data

The campaign uses exactly the 8 **major** elements. `divine` exists in `ELEMENTS` but is explicitly excluded (`01_core_defs.js:24, 27`) and is reserved for future Ace/Boss/God NPC cards — **the campaign has no boss content today.**

`COLORS` (`01_core_defs.js:28`) is the canonical order and is load-bearing for `dualId()`:

```
0 fire   1 water   2 earth   3 wind   4 forest   5 electric   6 light   7 dark
```

| Element | Name | Glyph | Colour | HP | Workers | Lore (shown on faction select) |
|---|---|---|---|---|---|---|
| `fire` | Fire | 炎 | `#e0613f` | 10000 | 2 | A furnace-keep of slag and iron. Thick walls, single-minded fire. |
| `water` | Water | 水 | `#3fa3e0` | 10000 | 3 | A drowned tower humming with current. A fast economy behind thinner walls. |
| `earth` | Earth | 地 | `#c0863c` | 10000 | 2 | A mountain hollowed into a fortress. Roots in bedrock, walls that have never fallen. |
| `wind` | Wind | 風 | `#76c7c0` | 10000 | 3 | A wind-scoured crag of open sky and screaming updrafts. Nothing lingers; everything strikes and is gone. |
| `forest` | Forest | 森 | `#4fae5e` | 10000 | 2 | A living rampart of root and bough. Slow to rouse, impossible to clear. |
| `electric` | Electric | 雷 | `#f2cf3b` | 10000 | 3 | A crackling pylon-hold. Everything here moves first and hits like a storm. |
| `light` | Light | 光 | `#ece3c0` | 10000 | 3 | A gold-vaulted cloister where dawnlight never fails. Patient walls, unyielding grace. |
| `dark` | Dark | 闇 | `#9a5cc6` | 10000 | 2 | A sunken crypt of whispering dark. Everything here is sharpened, spent, and fed to the void. |

Each element also carries `accent`, `deep`, and a 3-stop `bg` array used by the badge/art code (`01_core_defs.js:16-25`) — presentation only.

### 2.1 Commander ids (`CCS`)

`04_cards_leaders.js:9-22` builds 36 commanders:
- 8 solo: `id === element id`, `colors = [el]`, `hp = ELEMENTS[el].hp`, `wk = ELEMENTS[el].wk`.
- 28 dual: `id = a + "_" + b` where `a` precedes `b` in `COLORS` order; `hp = round((hpA+hpB)/2)`, `wk = round((wkA+wkB)/2)`, `colors = [a,b]`.

`dualId(a,b)` (`10_menus_campaign.js:29`) normalises pair order against `COLORS`:
```
dualId(a,b) = COLORS.indexOf(a) < COLORS.indexOf(b) ? a+"_"+b : b+"_"+a
```

---

## 3. World geometry — Goldberg polyhedron GP(4,0)

### 3.1 What it is

The world is the **dual of a frequency-4 subdivided icosahedron**. Tiles = vertices of the triangle mesh; tile corners = triangle centroids.

| Quantity | Formula | Value at f=4 |
|---|---|---|
| Tiles | `10f² + 2` | **162** |
| Pentagonal tiles | always 12 | **12** |
| Hexagonal tiles | `10f² − 10` | **150** |
| Triangles (= corner points) | `20f²` | **320** |
| Corner–tile incidences | `3 × 20f²` | 960 (= 12·5 + 150·6) |

Geometry is **fully deterministic from `f`**, which is why saves store only the tile→territory assignment (`10_campaign_globe.js:2-6`, `10_menus_campaign.js:18-20`).

### 3.2 Generation algorithm (`10_campaign_globe.js:9-46`)

Results are cached in `CAMP_SPHERES[f]` (`:8, :45`).

1. **Icosahedron vertices** `IV` (`:12`) — 12 entries built from φ = (1+√5)/2:
   ```
   (-1,φ,0) (1,φ,0) (-1,-φ,0) (1,-φ,0) (0,-1,φ) (0,1,φ)
   (0,-1,-φ) (0,1,-φ) (φ,0,-1) (φ,0,1) (-φ,0,-1) (-φ,0,1)
   ```
2. **Icosahedron faces** `IF` (`:13`) — 20 index triples, in this exact order (order determines tile indices, which are load-bearing for save compatibility):
   ```
   [0,11,5] [0,5,1] [0,1,7] [0,7,10] [0,10,11]
   [1,5,9]  [5,11,4] [11,10,2] [10,7,6] [7,1,8]
   [3,9,4]  [3,4,2]  [3,2,6]   [3,6,8]  [3,8,9]
   [4,9,5]  [2,4,11] [6,2,10]  [8,6,7]  [9,8,1]
   ```
3. For each face (A,B,C) — each normalised first — build a triangular lattice:
   ```
   for i in 0..f:
     for j in 0..i:
       p = A + (B-A)*(i/f) + (C-B)*(j/f)      // j/f is 0 when i == 0
       grid[i][j] = addVertex(normalize(p))
   ```
   `addVertex` normalises, then **de-duplicates by the string key** `x.toFixed(6),y.toFixed(6),z.toFixed(6)` (`:16`). This is what welds shared edges between adjacent icosahedron faces. New vertices are appended in first-seen order; **that append order defines tile indices 0..161.**
4. Emit triangles (`:23-26`):
   ```
   for i in 1..f:
     for j in 0..i-1:
       tri( grid[i-1][j], grid[i][j], grid[i][j+1] )
       if j < i-1: tri( grid[i-1][j], grid[i][j+1], grid[i-1][j+1] )
   ```
5. **Corners** = normalised centroid of each triangle's 3 vertices (`:28`). 320 of them, indexed by triangle index.
6. **Incidence** `inc[v]` = list of triangle indices touching vertex `v` (`:29-30`).
7. **Adjacency** `adjSet[v]` = every other vertex sharing a triangle with `v` (`:31-32`). This is the tile adjacency graph (5 neighbours for the 12 pentagons, 6 for the rest).
8. **Tile record** (`:35-44`): for each vertex `vi` with unit position `c`:
   - Build a tangent basis: `u = normalize(cross(c, |c.x| < 0.9 ? (1,0,0) : (0,1,0)))`, `v = cross(c, u)`.
   - Sort `inc[vi]` by `atan2(dot(cornerPos, v), dot(cornerPos, u))` ascending → **CCW when viewed from outside**.
   - Result: `{ c: Vector3, corners: int[] (5 or 6, CCW), adj: int[] (5 or 6) }`.

### 3.3 Unity replacement

Build the same topology in C# once, at authoring time or at boot, and **bake it** (ScriptableObject or binary asset) so tile indices can never drift.

- Replace the `toFixed(6)` string-key weld with either (a) a topological subdivision that shares edge vertices by construction, or (b) a spatial hash with an explicit epsilon (`1e-6` matches the JS). **Do not** rely on float formatting.
- Emit a real mesh: one prism per tile (top face inset+extruded, side skirts) or one merged mesh with a per-tile submesh/vertex-colour channel.
- Picking: give each tile a collider, or raycast a single mesh collider and map `RaycastHit.triangleIndex → tileId` via a baked lookup table.
- Keep `Tile.Center` (unit vector), `Tile.Corners` (CCW), `Tile.Adjacent` — these are the only three fields the game logic reads.

---

## 4. World map data model

### 4.1 `CampaignMap` (`10_menus_campaign.js:78`)

| Field | Type | Meaning |
|---|---|---|
| `f` | int | Sphere frequency. Always `CAMP_FREQ = 4`. |
| `tileTerr` | int[162] | Tile index → territory id. Every entry is `0..K-1`; no `-1` survives generation. |
| `terr` | map id → Territory | Keyed by numeric id (serialises to `{"0":…,"1":…}` in JSON). |
| `ids` | int[] | `[0 … K-1]`. The canonical territory iteration order. |
| `capitals` | map element → territory id | Exactly 8 entries, one per element. **Fixed at generation and never mutated.** |

### 4.2 `Territory` (`10_menus_campaign.js:55`)

| Field | Type | Meaning |
|---|---|---|
| `id` | int | 0-based. **Territory ids are numeric and `0` is valid — never truthiness-test them.** (`10_menus_campaign.js:18-19`, `17_turns_ai.js:405`) |
| `tiles` | int[] | Tile indices belonging to this territory. Guaranteed non-empty and contiguous. |
| `adj` | int[] | Neighbouring territory ids (derived from tile adjacency crossing a boundary). |
| `owner` | element id | Current holder. Never null after generation. |
| `garrison` | int | Map-layer troop count. See §4.4. |
| `anchor` | int | The tile index where the marker/label is drawn — also the "position" used for empire seeding. |

**There are no other node types.** No fortresses, no resource nodes, no special tiles, no fog of war. A territory is either a plain territory or the designated capital of some element (`capitals` lookup). That's the whole taxonomy.

### 4.3 Constants

| Constant | Value | Source |
|---|---|---|
| `CAMP_FREQ` | 4 | `10_campaign_globe.js:7` |
| `K` (territory count) | `min(22, tileCount)` = **22** | `10_menus_campaign.js:41` |
| Empire count | **8** (all majors) | `10_menus_campaign.js:70, 72` |
| Mitchell candidate count | **8** | `10_menus_campaign.js:47` |
| `CAMP_KEY` | `"srd.campaign.v3"` | `10_menus_campaign.js:21` |

### 4.4 Garrison numbers (complete table)

| Event | New garrison | Source |
|---|---|---|
| Generation, ordinary territory | `5 + floor(rand()*7)` → **5..11** | `:77` |
| Generation, capital territory | `5 + floor(rand()*7) + 7` → **12..18** | `:77` |
| End Turn growth, ordinary | `min(24, g + 1)` | `:185` |
| End Turn growth, capital | `min(24, g + 2)` | `:185` |
| Player wins a battle for it | `max(3, floor(g/2) + 2)` | `:154` |
| Player loses a battle for it | `max(1, g - 1)` (defender's garrison) | `:173` |
| AI attack succeeds — attacker source | `max(1, a.g - mv)` where `mv = max(2, floor(a.g/2))` | `:192` |
| AI attack succeeds — captured target | `mv` | `:192` |
| AI attack fails — attacker source | `max(1, floor(a.g * 0.8))` | `:194` |

> **CRITICAL DESIGN NOTE.** Garrison has **zero** effect on the actual card duel. It is never passed to `startGame`, never read by the battle, and never modifies life/decks/board. It only (a) gates the AI's expansion heuristic, (b) is displayed on the map, and (c) is bumped/decayed by results. Flag this to design before porting — it currently reads as a stat that should matter but does not.

---

## 5. Map generation — `campGenMap(faction)` (`10_menus_campaign.js:35-79`)

All randomness is `Math.random()`. **For Unity, thread a seeded RNG through this whole function** (see §14).

1. `sphere = getSphere(CAMP_FREQ)`; `T = sphere.tiles`; `n = T.length` (162).
2. `K = min(22, n)` = 22.
3. **Territory seeds — Mitchell's best-candidate sampling** (`:44-49`):
   - `seeds = [ floor(rand()*n) ]`.
   - While `seeds.length < K`:
     - Draw **8** candidate tiles uniformly at random. Skip any already in `seeds`.
     - For each surviving candidate compute `d = min over s in seeds of chord(cand, s)` where `chord` is the **Euclidean 3D distance between tile centres** (`:45`), not great-circle.
     - Keep the candidate with the largest `d`. If all 8 draws collided with existing seeds (`best < 0`), retry the whole round.
   - Rationale (comment `:41-43`): organic like pure random, but avoids clustered seeds → avoids a giant blob beside a sliver.
4. **Territory carve — multi-source BFS flood** (`:50-53`):
   - `tileTerr = new int[n]` filled with `-1`.
   - Seed `i` claims its tile: `tileTerr[seeds[i]] = i`; push all seeds into one shared FIFO **in seed order**.
   - Pop `t`; for each `u in T[t].adj` with `tileTerr[u] < 0`: `tileTerr[u] = tileTerr[t]`; push `u`.
   - Because all seeds start in one queue, this is a graph-distance Voronoi and every territory is a single connected blob **by construction**.
5. **Build territory records** (`:54-56`): create `K` empty `Territory` structs, then push every tile into its territory's `tiles`.
6. **Territory adjacency** (`:57-58`): for every tile `t` and neighbour `u`, if `tileTerr[t] != tileTerr[u]` add each to the other's adjacency set. Store as arrays.
7. **Anchor** (`:59-65`): for each territory, sum the unit centres of its tiles, normalise → centroid direction `cn`; the anchor is the member tile whose centre has the **largest dot product with `cn`**.
8. **Empire seeds — farthest-point sampling on anchors** (`:66-70`):
   - `pos(i) = T[terr[i].anchor].c`; `cd(a,b)` = Euclidean chord between anchor positions.
   - `eseeds = [ ids[floor(rand()*ids.length)] ]` — a uniformly random territory.
   - While `eseeds.length < 8`: pick the territory maximising `min distance to any existing eseed`. Deterministic given the first pick (first max wins ties, scanning `ids` in order).
9. **Assign elements to seeds** (`:71-74`):
   - `others = COLORS without faction`, **Fisher–Yates shuffled** (loop `i` from `len-1` down to `1`, `j = floor(rand()*(i+1))`, swap).
   - `elemsForSeeds = [faction, ...others]` truncated to 8.
   - **The player's faction always takes `eseeds[0]`**, i.e. the uniformly random first seed. The other 7 empires are the farthest-point picks, assigned in shuffled element order.
   - For seed `i`: `owner[tid] = el`, `capitals[el] = tid`.
10. **Empire flood** (`:75`): multi-source BFS over the *territory* adjacency graph from all 8 capitals in one shared queue. Every territory takes the owner of whoever reaches it first.
11. **Orphan fallback** (`:76`): any territory still unowned (topologically impossible on a sphere, but defensive) takes the owner of the nearest empire seed by chord distance.
12. **Garrisons** (`:77`): see §4.4.
13. Return `{ f: 4, tileTerr, terr, ids, capitals }`.

**Validation claim from the source** (`:39`): an 800-map Monte-Carlo run produced **0 fragmented territories and 0 fragmented empires**. Contiguity is guaranteed for territories by BFS construction; empire contiguity follows because the empire flood also runs on a connected graph from single seeds.

---

## 6. Campaign state and persistence

### 6.1 The `CAMPAIGN` object

| Field | Type | Persisted? | Reset on load? | Notes |
|---|---|---|---|---|
| `faction` | element id | yes | no | The player's banner. Chosen once. |
| `turn` | int ≥ 1 | yes | defaults to 1 if falsy | Incremented at the **start** of `campEndTurn`. |
| `map` | `CampaignMap` | yes | no | |
| `allies` | `{element: true}` | yes | defaults to `{}` | Absorbed elements. Unlocks their dual banner. |
| `target` | territory id or `null` | yes (always written as null) | **forced to `null`** | The territory a launched battle is fighting for. `!= null` is the flag that routes `checkWin` into `campResolve`. |
| `battleAs` | commander id or `null` | yes | **forced to `null`** | **DEAD FIELD** — written at `:143`, cleared at `:150,:176`, and never read anywhere in the codebase. Drop it in the port, or make it meaningful. |
| `completed` | bool (absent until set) | yes | no | Latch: set once when every territory is held. Prevents the "realm united" banner re-firing. |
| `lost` | bool (absent until set) | yes | no | Set by `campDefeat`. Makes `menuCampaign` route to faction select instead of a dead map. |

### 6.2 Load / validate (`10_menus_campaign.js:22-26`)

On script load:
1. `localStorage.removeItem('srd.campaign.v2')` — hard migration wipe of the previous schema. **No v2→v3 upgrade path exists.**
2. Read `srd.campaign.v3`, `JSON.parse`.
3. Accept only if **all** hold:
   - `c.faction` truthy **and** `CCS[c.faction]` exists
   - `c.map` and `c.map.tileTerr` and `c.map.f` and `c.map.terr` and `c.map.ids` and `c.map.capitals` all truthy
   - `c.map.tileTerr.length === 10 * c.map.f * c.map.f + 2` — guards against a sphere/save mismatch that would index past the tile list on every render.
4. Then normalise: `allies ||= {}`, `target = null`, `battleAs = null`, `turn ||= 1`.
5. Any failure or exception → `CAMPAIGN = null` (fresh start).

`campSave()` (`:27`) is a whole-object `JSON.stringify` into the same key, wrapped in try/catch (quota/private-mode safe). It is called at: `campStart`, `showCampaignMap`, `campAttack`, `campResolve` (twice), `campEndTurn`, `campDefeat`, and from `doSurrender` in `22_fx_wrappers.js:288`.

**Unity:** JSON file under `Application.persistentDataPath` (preferred over `PlayerPrefs` for a blob this size). Keep an explicit `schemaVersion` int and a real migration hook rather than a "delete the old key" wipe.

### 6.3 Suggested Unity save shape

Serialise the map as `f` + `tileTerr` + territory records + capitals, exactly as JS does. Do **not** serialise sphere geometry. **Additionally store the generation seed** so the map can be re-derived and audited; JS has no seed and cannot.

---

## 7. Screen flow / state machine

Screens are sibling DOM divs toggled by `display`. The set is `['mainMenu','charsel','soloSelect','deckBuilder','campaign','mpLobby']` (`10_menus_campaign.js:3`).

```
                       ┌──────────────┐
                       │  Main Menu   │  index.html:165  "Campaign / conquer the living map"
                       └──────┬───────┘
                              │ menuCampaign()                       :81
             ┌────────────────┴─────────────────┐
   CAMPAIGN valid && !lost              otherwise
             │                                  │
             ▼                                  ▼
     ┌───────────────┐                 ┌──────────────────┐
     │  World Map    │ ◄──campStart()──│  Faction Select  │  :82-91
     │ showCampaignMap :96              └──────────────────┘   8 element buttons
     └───┬───────┬───┘                          ▲
         │       │  campReset() → confirm → campDoReset()  :210-216
         │       │                                │
         │       └────────────────────────────────┘
         │
         │ tap territory  → campTerrClick   :119
         │      ├─ own            → toast, stay
         │      ├─ not on front   → toast, stay
         │      └─ attackable     → campOpenAttack (confirm overlay)  :128
         │                              └─ pick banner → campAttack   :142
         │                                       └─ campDialogue      (dialogue overlay)
         │                                              └─ onDone → startGame(...)
         │
         │ End Turn → campEndTurn :184 → (growth + AI) → turn-log overlay
         │                              └─ player has 0 territories → campDefeat :204
         │
         └─ Menu button → showMainMenu()

   BATTLE  ──(checkWin, 17_turns_ai.js:405)──► campResolve(win)  :149
                   │ writes the end-of-match banner + actions
                   └─ "↩ World Map" → campReturn() :183 → showCampaignMap()
                   └─ (if completed) "New Campaign" → campDoReset()
   BATTLE  ──(surrender, 22_fx_wrappers.js:286-290)──► target=null, save, showCampaignMap()
```

Notes:
- The campaign screen is shown by setting `display:flex` **directly**, not via `showScreen()` — so it deliberately skips the `screen-in` fade/rise animation (`:82, :96`).
- `showCampaignMap` **clears `target` and `battleAs` and saves before rendering** (`:96`). This is the safety net that stops a stale target from resolving a later match.
- **Display must precede render** (`:93-95`): `campGlobeMount` measures its parent to size the canvas. Unity: N/A.
- `hideAllScreens()` calls `campGlobeStop()` first (`:2`) — the single choke point for tearing down the render loop. Unity: disable/destroy the globe root, or just leave the scene.

---

## 8. World-map screen contents

### 8.1 Faction select (`renderFactionSelect`, `:83-91`)

- Heading: **"Choose Your Banner"**
- Sub: *"Campaign — hold one home realm on a freshly-drawn world, then conquer it territory by territory. Take an element's capital to absorb its lands and unlock its dual deck."*
- One button per element in `COLORS` order, each showing: badge + name (in element colour), `ELEMENTS[e].lore`, and `♥ <hp> · ⚒ <wk> workers · <Name> banner`.
- `← menu` returns to the main menu.
- Clicking calls `campStart(e)`.

### 8.2 `campStart(e)` (`:92`)

1. Reject if `!CCS[e]` or `e` not in `COLORS`.
2. `CAMPAIGN = { faction:e, turn:1, target:null, battleAs:null, allies:{}, map: campGenMap(e) }`.
3. `campSave()`, `campGlobeResetView()` (drop the persisted camera angle so the new map aims at the new capital), `showCampaignMap()`.

### 8.3 World map HUD (`renderCampaignMap`, `:98-118`)

Left cluster:
| Element | Content |
|---|---|
| Faction | badge + `ELEMENTS[fac].name`, tinted with the element colour |
| Turn | `Turn <b>{turn}</b>` |
| Lands | `Lands <b>{held}/{total}</b>` where `held = territories owned by faction`, `total = map.ids.length` (22) |
| Capitals | `Capitals <b>{heldCaps}/{capsAll}</b>` — `heldCaps` counts capitals whose territory is **currently owned** by the player (a capital you took counts even after you absorbed the element); `capsAll` = 8 |
| Allies | element badges for each `allies[e] === true`, or the italic dim text `none yet` |

Right cluster: `End Turn ▶` (green), `New` (ghost → `campReset`), `Menu` (ghost → `showMainMenu`).

Below the globe: a legend reading *"drag the globe · tap a territory"* followed by all 8 element badges + names.

Three overlay hosts are created: `#campConfirm`, `#campTurnLog` (both click-outside-to-close), and `#campToast`.

### 8.4 Territory click (`campTerrClick`, `:119-123`)

1. Resolve territory; bail if missing.
2. **Owned by player** → toast: `Your territory — garrison <b>{g}</b>.` plus, if this territory is the player's own designated capital, ` <span style="color:var(--gold)">Your capital.</span>`. Return.
3. **Not attackable** → toast: `{Owner} land — not on your front. Advance to a bordering territory first.` Return.
4. Otherwise → `campOpenAttack(tid)`.

Toast auto-hides after **2600 ms** (`:219`), and a new toast resets the timer.

### 8.5 Attackability rule (`campAttackableTerr`, `:33`; mirrored in the renderer at `10_campaign_globe.js:95`)

```
attackable(t) := t.owner != playerFaction
              && ANY u in t.adj : terr[u].owner == playerFaction
```
That is: **any enemy territory bordering any territory you own.** There is no range limit, no supply, no movement, no unit stack. The two implementations must stay in sync — in C#, expose a single `CampaignRules.IsAttackable(map, faction, territoryId)` and have both gameplay and rendering call it.

---

## 9. Attack confirmation and battle handoff

### 9.1 Capital prize helper (`campCapitalPrize`, `:124-127`)

```
capitalPrize(tid) := let c = capitalOwnerDesignation(tid)      // which element's capital is this, by fixed designation
                     return (c != null && c != playerFaction) ? c : null
```
Keyed off the **fixed designation in `map.capitals`**, not the current holder — so a throne a rival already seized still pays out when you take it. One helper serves the confirm box, the dialogue, and the resolution so the three cannot drift.

### 9.2 Confirm overlay (`campOpenAttack`, `:128-141`)

Banner (deck) options are built as:
```
combos = [[faction]] ++ [ [faction, ally] for each ally where allies[ally] === true ]
commanderId = combos.length == 1 ? combos[0] : dualId(combos[0], combos[1])
```
Any combo whose `CCS[id]` is missing renders as empty string (never happens — all 28 duals exist). Late in a campaign this list can reach **8 entries** (1 solo + 7 duals); the CSS scrolls the list and pins Cancel (`:247`).

Each option button shows: badges + `CCS[cid].name`, then `♥{hp} · ⚒{wk} · {Colour} + {Colour}`.

Header text, in priority order:
| Case | Title suffix | Body note |
|---|---|---|
| `prize != null` (an enemy element's designated capital) | ` — {ELEMENT} CAPITAL` in gold | `. Take it to <b>absorb {Element}</b> — its remaining lands and its dual deck become yours.` |
| the territory is the **player's own** designated capital, currently held by someone else | ` — YOUR CAPITAL` in gold | `. <b>Your throne</b>, held by another — retake it.` |
| otherwise | none | `.` |

Always shown: `Garrison <b>{g}</b>` and `March under which banner?`. Cancel closes the overlay.

### 9.3 `campAttack(tid, cid)` (`:142-148`)

1. Bail if no `CAMPAIGN` or `CCS[cid]` unknown or territory missing.
2. Close the confirm overlay.
3. `CAMPAIGN.target = tid; CAMPAIGN.battleAs = cid; campSave();`
4. Play the dialogue with:
   ```
   atkEl   = CAMPAIGN.faction
   defEl   = territory.owner                              // CURRENT holder
   capital = (capitalDesignation(tid) === territory.owner) // owner-relative!
   ```
   **The `capital` flag is owner-relative on purpose** (`:144-146`): the defender's capital barks are written in the first person about *their own* throne, so a rival-seized capital must not trigger them.
5. On dialogue completion:
   ```
   startGame( cid,                                   // youId  — the chosen banner commander
              territory.owner,                       // foeId  — the DEFENDER'S ELEMENT as a solo commander id
              deckOf(CCS[cid].colors.slice()),       // youDeck — freshly generated
              undefined )                            // foeDeck — undefined ⇒ startGame builds deckOf(foe.colors)
   ```

### 9.4 What the battle receives — and what it does NOT

| Passed | Value |
|---|---|
| Player commander | The chosen banner (solo or dual) — sets life (`hp`), starting workers (`wk`), colour identity |
| Enemy commander | `CCS[defenderElement]` — always the **solo** commander of the defending element |
| Player deck | `deckOf(colors)` — 40 cards, randomly rolled from the pools (see `06_mana_workers.js:26-35`) |
| Enemy deck | Built inside `startGame` as `deckOf(foeCommander.colors)` — mono-element, randomly rolled |

**NOT passed:** garrison, territory id, capital status, turn number, ally list, difficulty, any AI tuning. The duel is identical to a solo skirmish. `startGame` (`09_game_start.js:1-19`) resets both players fully, calls `hideAllScreens()` (which stops the globe), builds the battlefield scenery from `you.colors[0]` / `foe.colors[0]`, and opens at the upkeep phase of turn 1.

Notably the player **cannot use a saved custom deck** in campaign — the deck is always rolled fresh from `deckOf`. (Solo mode does allow saved decks, `11_deck_builder.js:193`.) Flag as a likely design gap.

---

## 10. Pre-battle challenge dialogue

A Fire-Emblem-style 4-line exchange overlaid on the campaign screen before the duel. Data is in `10_campaign_dialogue.js`.

### 10.1 Champions (`CAMP_CHAMPS`, `:7-8`)

Each element speaks through its flagship (cost-6) creature.

| Element | Champion | Card exists in |
|---|---|---|
| fire | Magmaw | `03_cards_creatures.js:5` |
| water | Leviath | `:7` |
| earth | Titanore | `:9` |
| wind | Tempest | `:11` |
| forest | Hive Cradle | `:13` |
| electric | Galvanwyrm | `:15` |
| light | Seraphine | `:17` |
| dark | Voidwyrm | `:19` |

Fallback if a champion is missing: `ELEMENTS[el].name` (`:132`). Never triggers today.

### 10.2 Bark table (`CAMP_LINES`, `:10-107`)

8 elements × 5 buckets × **exactly 2 alternatives each** = 80 authored lines.

| Bucket | Spoken by | When |
|---|---|---|
| `open` | Defender | Line 1, when the target is *not* the defender's own capital |
| `capital` | Defender | Line 1, when the target **is** the defender's own designated capital |
| `taunt` | Attacker | Line 2, when there is no rival exchange for this ordered pair |
| `retort` | Defender | Line 3, when there is no rival exchange |
| `close` | Attacker | Line 4, always |

Full text (port verbatim — this is authored content):

**fire** — open: *"Who dares scorch their boots on my doorstep? Speak fast — the ground here eats the slow."* / *"You smell that? Slag and ash. That's what becomes of banners that march on Fire."* · capital: *"This is the Furnace-Keep itself. Every army that reached these walls is part of the walls now."* / *"You bring an army to the heart of the forge? Good. We were running low on fuel."* · taunt: *"Burn it all down. What's left standing, we keep."* / *"I'll give your line one chance to run. One. It's more than the last ones got."* · retort: *"Then come closer. Everything you love is kindling."* / *"Ha! Stoke the coals. This one thinks it can outlast a furnace."* · close: *"Enough talk. Light the field."* / *"Then it's settled — by fire, as all things are."*

**water** — open: *"The tide brought you to us. The tide will carry what's left of you away."* / *"Still waters, stranger. Turn back before they remember how to drown."* · capital: *"You stand before the Drowned Tower. Deeper powers than you have broken on this current."* / *"The throne of the deep does not fall. It closes over, and is calm again."* · taunt: *"Every wall erodes. Yours simply erodes today."* / *"We are patient as rain and sudden as the flood. Choose which one meets you."* · retort: *"Come then. The undertow is patient, and you look tired already."* / *"Waves do not argue with stone. They simply return, and return, and return."* · close: *"The current has decided. Let it pull."* / *"Enough. Let the water speak."*

**earth** — open: *"You are standing on me, little thing. That is as far as you will ever get."* / *"Turn around. The mountain has outlasted better invasions than yours."* · capital: *"This is the Hollow Mountain. Its walls have never fallen. You will not be the first to see them fall."* / *"You march on bedrock. Bedrock does not surrender."* · taunt: *"I do not need to be fast. You will tire, and I will still be here."* / *"Stone remembers every siege. Yours will be a short memory."* · retort: *"Dig in, then. We will see whose roots go deeper."* / *"Strike. The mountain will count your blows and forget them."* · close: *"The earth has spoken. It says: stay down."* / *"Come. Break yourself against me."*

**wind** — open: *"You're slow. Everything about you is slow. This will be over before your banners unfurl."* / *"The updrafts carried word of your little march. We laughed, mostly."* · capital: *"This crag belongs to the sky. You'd need wings to take it, and I don't see any on you."* / *"The Screaming Crag stands because nothing can catch it. Certainly not you."* · taunt: *"Try to hit me. Go on. I'll wait — no, actually, I won't."* / *"We'll scour your back line before your front line knows we've passed."* · retort: *"Catch the wind, then. Others have tried. Their bones make lovely whistles."* / *"You brought walls to a sky fight. Adorable."* · close: *"Skies darken. Time to fly."* / *"Enough hovering. Strike like a gale."*

**forest** — open: *"The grove counted your soldiers as they crossed the treeline. The grove is patient. We are patient."* / *"Root and bough remember every axe. Yours will join the mulch."* · capital: *"This is the First Grove. Everything you see grew from it. Everything you see will defend it."* / *"The Cradle wakes. The brood stirs. You should not have come here."* · taunt: *"We grow through everything, given time. Your walls are no different."* / *"The canopy closes over all things. Today it closes over you."* · retort: *"Then the vines will take you slowly, as they take all impatient things."* / *"Hatch, my broodlings. Show them what patience becomes."* · close: *"The forest marches. Root by root."* / *"Grow. Strangle. Bloom."*

**electric** — open: *"Signal detected. Response time: instant. That's the difference between us, friend."* / *"You walked here? We ARRIVED. Before you finished deciding to come."* · capital: *"This is the Pylon-Hold. Ten thousand volts of no-you-don't. Touch the fence and find out."* / *"The storm's heart doesn't get conquered. It gets survived. Briefly."* · taunt: *"I've already won this fight nine times in my head. Care to see the live version?"* / *"First strike, last laugh. That's the whole doctrine."* · retort: *"Cute speech. I overcharged during it. Your move."* / *"Thunder answers lightning. Try to keep up."* · close: *"Storm's rolling in. Let's ride it."* / *"Charge to full. DISCHARGE."*

**light** — open: *"Dawn finds all who trespass here. Lay down your banner and be forgiven — this once."* / *"The cloister's light does not flicker for armies. Approach, and be seen for what you are."* · capital: *"You stand before the Gold Vault of Dawn. Its light has never failed, and never will."* / *"The dawnlight judges all who reach these gates. Few are found worthy. None by force."* · taunt: *"We come not in anger, but in certainty. The light goes where it will."* / *"Grace has an edge, stranger. You are about to see it drawn."* · retort: *"Then the ward is raised, and the judgement is begun."* / *"Radiance does not yield. It reveals. Stand in it, if you dare."* · close: *"By dawn's mandate — advance."* / *"Let the light fall where it may."*

**dark** — open: *"Ah. Fresh souls, walking themselves to the crypt. How considerate."* / *"The dark whispered your coming days ago. It also whispered how you end."* · capital: *"This is the Sunken Crypt. Everything that enters feeds it. You will feed it magnificently."* / *"The void keeps its throne the old way: it simply never gives anything back."* · taunt: *"Everything you field, I harvest. Your army is just my army, waiting."* / *"The void is patient and I am not. Lucky for you, only one of us is merciful. Unlucky: it's neither."* · retort: *"Yes... struggle. The reaping is sweeter when the crop resists."* / *"Every soldier you lose joins my line. Do the arithmetic, then despair."* · close: *"The dark is done whispering."* / *"Reap them all."*

### 10.3 Rival exchanges (`CAMP_RIVALS`, `:109-126`)

Keyed by the **ordered** string `"{attacker}>{defender}"`. Each value is `[attackerTaunt, defenderRetort]` and replaces **both** line 2 and line 3. There are exactly **8** entries (4 opposed pairs, both directions):

| Key | Attacker taunt (line 2) | Defender retort (line 3) |
|---|---|---|
| `fire>water` | "Steam. That's all your ocean is to me — steam I haven't made yet." | "Oceans have swallowed a thousand fires like you. You won't even hiss." |
| `water>fire` | "Every forge goes cold, ember. Yours goes cold today." | "Come and try, puddle. I've boiled seas for less." |
| `light>dark` | "The dark is only the absence of my arrival. I have arrived." | "Little candle, the dark was here before you and will be here after. Come — be snuffed." |
| `dark>light` | "Every dawn ends, Seraphine. I am what it ends INTO." | "The dark has knelt at every sunrise since the first. Kneel again." |
| `earth>wind` | "Even the wind must land somewhere, breeze. And everywhere it lands is mine." | "Landing? Sweet old rock — why would I ever come down for you?" |
| `wind>earth` | "Mountains erode, boulder. I am the thing that erodes them. Grain by grain." | "Blow, then. When you tire, the mountain will still be counting." |
| `forest>electric` | "Wood does not conduct, storm-worm. But it burns SLOWLY, and grows back faster." | "Nature's rebuttal to a tree: lightning. Ask any tall one what it thinks of me." |
| `electric>forest` | "Tallest thing on the field gets the bolt, cradle. Guess what you are." | "Strike, spark. The grove has drunk a million storms and grown from every one." |

There are **no other rivalries** — the remaining 48 ordered pairs always use the generic taunt/retort buckets.

### 10.4 Line assembly (`campDialogue`, `:129-141`)

```
A = attackerElement, D = defenderElement
an = CAMP_CHAMPS[A] ?? ELEMENTS[A].name
dn = CAMP_CHAMPS[D] ?? ELEMENTS[D].name
rival = CAMP_RIVALS["A>D"]            // may be null

line[0] = { speaker: D, name: dn, side: Defender,
            text: randomOf( capital ? (CAMP_LINES[D].capital ?? CAMP_LINES[D].open) : CAMP_LINES[D].open ) }
line[1] = { speaker: A, name: an, side: Attacker,
            text: rival ? rival[0] : randomOf(CAMP_LINES[A].taunt) }
line[2] = { speaker: D, name: dn, side: Defender,
            text: rival ? rival[1] : randomOf(CAMP_LINES[D].retort) }
line[3] = { speaker: A, name: an, side: Attacker,
            text: randomOf(CAMP_LINES[A].close) }
```
`randomOf` is uniform over the 2 alternatives. **Exactly 4 lines. Defender always opens and closes-out line 3; attacker always has the last word.** There is **no branching, no player choice, no state, no conditions beyond `capital` and the rival lookup.**

If the campaign host element is missing, `campDialogue` immediately calls `onDone()` and shows nothing (`:130`).

### 10.5 Dialogue presentation (re-implement, don't copy)

| Aspect | Behaviour | Source |
|---|---|---|
| Header strip | `{badgeA} <b>{ElementA}</b> marches on [the <gold>capital</gold> of ]<b>{ElementB}</b> {badgeB}` | `:144-146` |
| Portraits | `spriteImg({nm: championName})` — probes `<slug>_fieldart.<ext>` in the typed folder then flat, then falls back to `<slug>_cardart.<ext>`, then a built-in SVG placeholder (`04_cards_leaders.js:124-148`) | `:150-151` |
| Attacker portrait | bottom-left | CSS `:204` |
| Defender portrait | bottom-right, **mirrored** `scaleX(-1)` | CSS `:205-206` |
| Non-speaking portrait | `brightness(.45) saturate(.7)` | CSS `:202-203` |
| Speaking portrait | full brightness + drop shadow + `translateY(-6px) scale(1.04)` | CSS `:207` |
| Ambient glow | radial gradient in the speaker's element colour behind each figure (`{color}33` = ~20 % alpha) | `:148-149` |
| Text reveal | typewriter, **one character every 14 ms** | `:168-169` |
| Advance-indicator | bobbing `▼`, hidden while typing, shown when the line completes | `:152, :165, :169` |
| Tap while typing | completes the current line instantly | `:182` |
| Tap when complete | advances to the next line; past the last line → `finish()` | `:183` |
| Swipe guard | a pointer that travels more than **7 px (mouse) / 15 px (touch)** Manhattan distance does not advance | `:177-181` |
| `Skip ▸▸` button | top-right, ends the whole scene regardless of travel | `:147, :175` |
| Fade-in | 0.35 s opacity | CSS `:191-192` |
| Layer | `z-index: 43`, `position:absolute; inset:0` inside `#campaign` | CSS `:189` |
| Teardown | `finish()` is idempotent (`done` latch), clears the typewriter interval, removes the node, then calls `onDone()` exactly once | `:157-158` |

---

## 11. Globe renderer and input (browser scaffolding — reference only)

Keep this section for behaviour parity of the *feel*; discard the maths.

### 11.1 View state (`campView`)

`{ yaw, pitch, vyaw }`, module-scoped so it survives re-mounts (End Turn re-renders the whole screen). Reset only by `campGlobeResetView()`, which is called only from `campStart` (`:92`).

`campGlobeAimAt(c)` (`10_campaign_globe.js:57-61`) returns the yaw/pitch that place unit vector `c` dead-centre facing the viewer:
```
yaw   = atan2(-c.x, c.z)
z1    = -c.x*sin(yaw) + c.z*cos(yaw)
pitch = atan2(c.y, z1)
vyaw  = 0
```
On first mount the camera aims at **the anchor tile of the player faction's designated capital** (`:69`). Unity equivalent: orbit the camera so that territory faces the viewer.

### 11.2 Constants

| Name | Value | Meaning |
|---|---|---|
| `H` | 0.05 | Tile extrusion height |
| `EXH` | 1.05 | Extruded radius multiplier (`1 + H`) |
| `INSET` | 0.93 | Top face corners lerped 7 % toward the tile centre |
| `R` | `min(W,H) * 0.42` | Globe radius in CSS px |
| `DPR` | `min(2, devicePixelRatio)` | |
| light dir | normalize(−0.45, 0.55, 0.72) | **In view space** — light is fixed to the camera, not the world |
| luminance | `0.62 + 0.5 * max(0, n·L)` | `n` = rotated tile centre |
| own-tile boost | `× 1.18` | Player-owned tiles are brighter |
| skirt shade | `× 0.42` | |
| back-face cull | `centreZ < -0.35` **or** every corner `z < 0` | Corner test needed because centre-only culling leaked far-side tiles through the 7 % inset seams |
| tile outline | `rgba(0,0,0,0.25)`, 0.6 px | |
| glow ring | radial gradient `rgba(96,118,210,0.16)` → transparent, from `R*0.88` to `R*1.2` | |
| ocean disc | `#0a1424` filled circle at `R*1.001` | |

### 11.3 Border rendering (`:143-157`)

For each ordered pair `i<j` of adjacent tiles, both with rotated `z >= 0.02`, whose territories differ, take the **exactly 2 shared corner indices** (skip otherwise) and stroke the extruded projected segment:

| Case | Stroke | Width |
|---|---|---|
| Same owner, different territory | `rgba(0,0,0,0.35)` | 1.1 |
| Different owner, one side is the player | `#d9b64a` (gold) | 3 |
| Different owner, neither is the player | `rgba(244,240,255,0.85)` | 2.2 |

### 11.4 Territory markers (`:158-176`)

Drawn at the **anchor tile centre × (EXH + 0.02) = 1.07**, skipped when rotated `z < 0.18`.

- `mk = clamp(R / 240, 0.8, 1.25)` — a global size scalar.
- Radius `= (isCapital ? 16 : 11.5) * mk * min(1, 0.75 + z*0.35)`.
- Fill `rgba(8,6,14,0.78)`.
- Ring: attackable → gold `rgba(217,182,74, pulse)` at width 2.6, where `pulse = 0.55 + 0.45*sin(t/450)`; else player-owned → `#fff` at width 2; else `rgba(255,255,255,0.3)` at width 1.4.
- Label: capital → element **glyph** at `13*mk` px with the **garrison number** at `9.5*mk` px below (offset `+10*mk`); non-capital → garrison number at `12*mk` px (offset `+4*mk`).

### 11.5 Input (`:184-217`)

| Behaviour | Value |
|---|---|
| Drag → yaw | `yaw += dx * 0.005` |
| Drag → pitch | `pitch = clamp(pitch − dy*0.005, −1.25, +1.25)` (±71.6°) |
| Inertia seed | `vyaw = dx * 0.0009` on each move |
| Inertia decay | `vyaw *= 0.93` per frame; `yaw += vyaw` |
| Idle auto-spin | after **2600 ms** with no interaction and not dragging: `yaw += 0.0011` per frame |
| Tap vs. drag | Manhattan `|dx| + |dy| >` **7** (mouse) / **15** (touch) marks it a drag |
| Second pointer | ignored — a 2nd finger must not hijack the drag or reset `moved` into a spurious pick |
| Pick | invert against `R*EXH`, accept `x²+y² <= 1.06`, `z = sqrt(1 − min(1, x²+y²))`, `v = unrot(x,y,z)`, choose the tile maximising `dot(tile.c, v)`, then report `map.tileTerr[tile]` |
| Cancel handling | `pointercancel` and `lostpointercapture` both clear the drag, guarded on pointer id, nulling state **before** releasing capture (releasing re-fires `lostpointercapture`) |

Two of these are pure bug-fixes for the browser projection and should not survive: the `R*EXH` correction (dividing by `R` alone biased the ray outward and mis-picked roughly 1 tap in 7, `:200-202`) and the `1.06` slop radius.

In Unity: raycast the globe collider, map hit → tile → territory. Keep the drag-orbit, the ±1.25 rad pitch clamp, the inertia and the idle spin as feel.

---

## 12. Battle resolution — `campResolve(win)` (`10_menus_campaign.js:149-182`)

Invoked from `checkWin()` (`17_turns_ai.js:405`) **only when `CAMPAIGN.target != null`** — id `0` is a valid territory so the test must be `!= null`, never truthiness.

```
1.  if CAMPAIGN missing or target == null: return
2.  tid = CAMPAIGN.target;  t = terr[tid]
3.  if t missing: clear target/battleAs, save, return          // defensive
4.  defEl = t.owner        (captured BEFORE mutation)
5.  if win:
      prize = capitalPrize(tid)                                 // §9.1
      t.owner    = playerFaction
      t.garrison = max(3, floor(t.garrison / 2) + 2)
      if prize != null:
        absorbed = 0; gained = []
        swallow(el):
           allies[el] = true;  gained.push(el)
           for every territory u with u.owner == el:  u.owner = playerFaction; absorbed++
        swallow(prize)
        // CASCADE — absorbing one element's lands can hand you ANOTHER element's throne
        repeat until no change:
          for each element el in map.capitals:
            if el == playerFaction or allies[el]: continue
            if terr[ capitals[el] ].owner == playerFaction: swallow(el)
      done = (!CAMPAIGN.completed) && (playerTerritoryCount == map.ids.length)
      if done: CAMPAIGN.completed = true
      banner = done ? "THE REALM IS UNITED" : (prize ? "CAPITAL TAKEN" : "TERRITORY WON")   // gold
    else:
      t.garrison = max(1, t.garrison - 1)
      banner = "ASSAULT REPELLED"                                                            // #e35b4f
6.  CAMPAIGN.target = null;  CAMPAIGN.battleAs = null;  campSave()
7.  write the sub-line and the banner action buttons
```

### 12.1 Sub-line text

- **Win:** `Your banner rises over {DefenderElement} ground.` plus, when a capital fell:
  `<br>The {Prize} capital falls — [its {N} remaining land(s) bow to you, and ]the {DeckName}[ and {DeckName}] deck(s) is/are yours to field.`
  where deck names come from `CCS[dualId(faction, gainedElement)].name` (falling back to the capitalised element name).
  Plus, when `done`: `<br><b>Every land is yours — the eight elements united under one throne.</b>`
- **Loss:** `{DefenderElement} holds the line. Regroup and strike again.`

### 12.2 Banner actions

| Condition | Buttons |
|---|---|
| `CAMPAIGN.completed` | `New Campaign` (→ `campDoReset`) and `↩ World Map` (→ `campReturn`) |
| otherwise | `↩ World Map` only |

`campReturn()` (`:183`) hides the banner, restores the default action row (`Duel Again` → page reload — the non-campaign default), and calls `showCampaignMap()`.

### 12.3 Rules consequences worth calling out

- **Winning a battle never costs you anything.** No garrison spend, no attrition, no cooldown. You may attack every turn, as many times as you like, without ever pressing End Turn.
- **Losing a battle costs the defender 1 garrison** — i.e. losing *helps* you slightly. Probably unintended; flag to design.
- **Absorption is player-only.** The AI never absorbs capitals (§13).
- **The cascade exists** because absorbing one element's lands can hand you another element's throne; without it that element would linger as a landless holdout that no attack could ever reach (`:161-162`).
- Victory is **holding every territory**, not merely every capital (`:167`). It is latched via `completed` so it cannot re-fire on later wins (which can happen after a reset-free continue).
- Losing your own capital has **no special effect** — no penalty, no game-over. Only holding **zero** territories ends the campaign.

### 12.4 Abort paths

| Path | Behaviour | Source |
|---|---|---|
| Surrender (`doSurrender`) | `G.over = true`; if `CAMPAIGN.target != null` → set `target = null`, `campSave()`, `showCampaignMap()` and return (skipping the main-menu route). **No territory or garrison change** — the assault simply never happened. | `22_fx_wrappers.js:286-290` |
| Surrender confirm copy | When in campaign the prompt reads *"Surrender this match and return to the world map?"* with *"Yes, abandon the assault"*; otherwise *"…to the main menu?"* / *"Yes, quit to menu"*. | `22_fx_wrappers.js:297-301` |
| Multiplayer match start (host and guest) | Defensively clears `CAMPAIGN.target` so an MP result can never resolve a campaign territory. | `44_mp_lobby.js:128, 139` |

**Port implication:** `checkWin` calling `campResolve` is a hard dependency from the battle core into the campaign layer. In C# this must be inverted — the battle emits a `BattleFinished(bool playerWon)` event/result and the campaign layer subscribes. Also model "aborted" as a distinct outcome (`BattleOutcome.Abandoned`) instead of the JS trick of nulling the target.

---

## 13. End Turn — `campEndTurn()` (`10_menus_campaign.js:184-198`)

Ordered algorithm. All randomness is `Math.random()`.

```
1.  bail if no CAMPAIGN or no map
2.  CAMPAIGN.turn += 1                         // BEFORE growth — the turn number shown is the new one
3.  GROWTH — for every territory (including the player's):
        garrison = min(24, garrison + (isDesignatedCapital ? 2 : 1))
4.  AI ROSTER = every element in COLORS except the player faction
                 that currently owns >= 1 territory,
                 Fisher–Yates shuffled (i from len-1 down to 1)
5.  for each AI element el, in shuffled order:
      best = null
      for each territory t owned by el:
        for each neighbour u of t with u.owner != el:
          sc = t.garrison - u.garrison
          if best == null or sc > best.sc:  best = {from:t, to:u, sc, def:u.owner}   // strict >, first max wins
      if best != null and best.sc > -2 and random() < 0.7:
          aw = best.from.garrison * (0.7 + 0.6*random())     // ∈ [0.7g, 1.3g)
          dw = best.to.garrison   * (0.7 + 0.6*random())
          if aw > dw:                                        // CAPTURE
             wasPlayer = (best.to.owner == playerFaction)
             from      = best.to.owner
             best.to.owner    = el
             mv                = max(2, floor(best.from.garrison / 2))
             best.from.garrison = max(1, best.from.garrison - mv)
             best.to.garrison   = mv
             log("{El} overran {your | {From}'s} territory.")
          else:                                              // REPULSED
             best.from.garrison = max(1, floor(best.from.garrison * 0.8))
6.  if the player now owns ZERO territories: campDefeat(); return   (no save/render below runs)
7.  campSave(); renderCampaignMap(); campTurnLog(logs)
```

Properties to preserve:
- **One attack attempt per AI element per turn, maximum.**
- The heuristic is purely `attackerGarrison − defenderGarrison`; the `> -2` gate lets an AI attack into a slightly stronger neighbour.
- The 70 % engagement roll means an AI often does nothing even with a good target.
- AI battles are **auto-resolved**; the player never plays defence. Losing a territory to the AI is not a duel.
- AI elements do **not** absorb capitals, do **not** gain allies, and do **not** get eliminated by any special rule — they simply drop out of the roster once they own nothing.
- Absorbed allies own nothing (their lands became the player's), so they are automatically excluded from the AI roster.
- **Note the ordering bug risk:** growth applies before the AI moves, and `best` is computed against garrisons that earlier AI elements in the same turn may already have changed. This is order-dependent and the order is shuffled — deliberate or not, it must be reproduced exactly if you want save-compatible behaviour, and it *must* be driven by a seeded RNG to be replayable.

### 13.1 Turn log overlay (`campTurnLog`, `:199-203`)

Title: `Turn {turn} — the world stirs` (gold). Body: one row per capture log, or the italic dim line *"The map lies quiet this turn."* when nothing happened. A single `Continue` button closes it. Log rows are HTML with element-coloured names; the player's losses read `your` in `#e0a59a`.

### 13.2 Defeat (`campDefeat`, `:204-209`)

1. `CAMPAIGN.lost = true`; `campSave()`; `hideAllScreens()`.
2. Banner: **"YOUR BANNER HAS FALLEN"** in `#e35b4f`, sub-line *"The last of your holdings is lost. The campaign is over."*
3. Banner actions: a single `New Campaign` → `campDoReset()`.
4. Show the banner.

The `lost` flag makes a page reload route to faction select rather than a dead map (`:81`).

### 13.3 Reset (`campReset` / `campDoReset`, `:210-217`)

- `campReset()` opens a confirm overlay: title *"Abandon this campaign?"* (in `#e0a59a`), body *"Your conquered lands and alliances are lost, and a new world is drawn."*, buttons `Start over` (red) and `Keep playing`.
- `campDoReset()` sets `CAMPAIGN = null`, deletes the localStorage key, hides the battle banner, restores the default banner action row, closes the confirm overlay, and shows faction select.

---

## 14. Determinism, RNG and netcode readiness

The JS campaign uses bare `Math.random()` in **nine** places:

| Site | Source |
|---|---|
| Territory seed initial pick + 8 candidates per round | `:44, :47` |
| Empire seed initial pick | `:69` |
| Element shuffle for empire seeds | `:71` |
| Garrison rolls | `:77` |
| AI roster shuffle | `:187` |
| AI engagement roll (`< 0.7`) | `:190` |
| AI attack/defence strength rolls (2×) | `:191` |
| Dialogue line choice | `10_campaign_dialogue.js:133` |
| Deck generation (`deckOf`, `rng`) | `06_mana_workers.js:26-35` |

For the C# port, **all campaign-layer randomness must go through an injected deterministic RNG** (e.g. a `Pcg32`/xorshift struct stored in `CampaignState` as `ulong Seed; ulong Stream;`). Then:
- Map generation becomes reproducible from a seed (and the seed can go in the save alongside `tileTerr` as a cross-check).
- End Turn becomes a deterministic function of `(state, seed)` — replayable, testable, and safe to run authoritatively on a host later.
- Dialogue line choice should use a **separate** presentation RNG so re-showing a scene never desyncs simulation state.
- Deck generation should be seeded per-battle and the seed recorded in the battle launch request (also needed for the future host-authoritative netcode, since the host must shuffle both decks).

Design the campaign layer as command/intent driven, matching the rest of the port:

```
AttackTerritoryCommand { int TerritoryId; string CommanderId; }
EndCampaignTurnCommand { }
ResolveBattleCommand   { BattleOutcome Outcome; }
StartCampaignCommand   { ElementId Faction; ulong Seed; }
AbandonCampaignCommand { }
```
Each returns a list of events (`TerritoryCaptured`, `ElementAbsorbed`, `CampaignCompleted`, `CampaignLost`, `GarrisonChanged`) the view layer renders.

---

## 15. Suggested C# types

```csharp
// ---------- geometry (baked, immutable) ----------
public enum ElementId { Fire, Water, Earth, Wind, Forest, Electric, Light, Dark, Divine }

public readonly struct HexTile {
    public readonly Vector3 Center;      // unit vector
    public readonly int[]   Corners;     // 5 or 6, CCW viewed from outside; indices into HexSphere.Corners
    public readonly int[]   Adjacent;    // 5 or 6 tile ids
}

public sealed class HexSphere {          // cache by frequency; bake at author time
    public int Frequency { get; }        // 4
    public HexTile[]  Tiles   { get; }   // 162
    public Vector3[]  Corners { get; }   // 320
    public static HexSphere Get(int frequency);
    public static int TileCount(int f) => 10 * f * f + 2;
}

// ---------- campaign data ----------
[Serializable] public sealed class Territory {
    public int       Id;
    public int[]     Tiles;
    public int[]     Adjacent;
    public ElementId Owner;
    public int       Garrison;
    public int       AnchorTile;
}

[Serializable] public sealed class CampaignMap {
    public int         Frequency;                       // 4
    public int[]       TileTerritory;                   // length == HexSphere.TileCount(Frequency)
    public Territory[] Territories;                     // 22
    public SerializableDictionary<ElementId,int> Capitals;   // 8 entries, immutable after generation
    public bool Validate();                             // mirrors 10_menus_campaign.js:24-26
}

[Serializable] public sealed class CampaignState {
    public int         SchemaVersion;                   // 3
    public ElementId   Faction;
    public int         Turn;                            // >= 1
    public CampaignMap Map;
    public HashSet<ElementId> Allies;
    public int?        TargetTerritory;                 // null when no battle is pending
    public bool        Completed;                       // latch
    public bool        Lost;
    public ulong       RngState;
}

// ---------- rules (pure, no UnityEngine) ----------
public static class CampaignRules {
    public static bool      IsAttackable(CampaignMap m, ElementId faction, int territoryId);
    public static ElementId? CapitalDesignation(CampaignMap m, int territoryId);
    public static ElementId? CapitalPrize(CampaignState s, int territoryId);
    public static int       PlayerTerritoryCount(CampaignState s);
    public static IReadOnlyList<string> AvailableCommanderIds(CampaignState s);   // solo + one dual per ally
}

public sealed class CampaignMapGenerator {                 // §5
    public CampaignMap Generate(ElementId playerFaction, ref Rng rng);
}

public sealed class CampaignTurnResolver {                 // §13
    public IReadOnlyList<CampaignEvent> EndTurn(CampaignState s, ref Rng rng);
}

public sealed class CampaignBattleResolver {               // §12
    public IReadOnlyList<CampaignEvent> Resolve(CampaignState s, bool playerWon);
    public void Abandon(CampaignState s);                  // surrender path
}

// ---------- battle handoff ----------
public sealed class BattleLaunchRequest {
    public string    PlayerCommanderId;     // "fire" or "fire_water"
    public string    EnemyCommanderId;      // always a solo element id — the defender's element
    public ulong     DeckSeed;
    public int       TerritoryId;           // context only; the duel ignores it today
}
public enum BattleOutcome { PlayerWon, PlayerLost, Abandoned }

// ---------- dialogue (ScriptableObjects) ----------
public enum DialogueSide { Attacker, Defender }
public enum BarkBucket   { Open, Capital, Taunt, Retort, Close }

[CreateAssetMenu] public sealed class ElementBarkSet : ScriptableObject {
    public ElementId Element;
    public string    ChampionCardName;      // "Magmaw", …
    public string[]  Open, Capital, Taunt, Retort, Close;   // 2 entries each today
}
[CreateAssetMenu] public sealed class RivalExchange : ScriptableObject {
    public ElementId Attacker, Defender;
    public string    AttackerTaunt, DefenderRetort;
}
public readonly struct DialogueLine {
    public readonly ElementId Speaker; public readonly string SpeakerName;
    public readonly DialogueSide Side; public readonly string Text;
}
public static class ChallengeDialogueBuilder {              // §10.4
    public static DialogueLine[] Build(ElementId attacker, ElementId defender,
                                       bool defenderOwnCapital, ref Rng presentationRng);
}

// ---------- view ----------
public enum CampaignScreen { MainMenu, FactionSelect, WorldMap, Challenge, Battle, ResultBanner }
```

---

## 16. Bugs, dead code and gaps found during extraction

| # | Finding | Location |
|---|---|---|
| 1 | `CAMPAIGN.battleAs` is written and cleared but **never read**. Dead field. | `:143, :150, :176` |
| 2 | `const capEl = campIsCapital(tid)` in `campResolve` is assigned but never used. Dead local. | `:151` |
| 3 | **Garrison has no effect on the duel** — it is a purely cosmetic/AI number from the player's perspective. | §4.4 |
| 4 | Losing a battle *reduces the defender's garrison by 1*, so failed assaults soften the target. Likely unintended. | `:173` |
| 5 | Attacking is **free and unlimited** — no per-turn attack budget, no cost. The player can conquer the world without ever pressing End Turn. | §12.3 |
| 6 | The player cannot bring a **saved custom deck** into a campaign battle; the deck is always randomly rolled by `deckOf`. | `:147` |
| 7 | The enemy always fields a **mono-element solo commander** with a random deck, regardless of whether it is a capital, an ally-rich empire, or turn 1 vs. turn 50. No difficulty curve. | `:147`, `09_game_start.js:11` |
| 8 | Losing your own capital carries **no penalty at all**. | §12.3 |
| 9 | AI elements never absorb capitals or gain allies — the absorb/cascade mechanic is player-only, so AI empires can never snowball the way the player does. | `:184-198` |
| 10 | `CAMP_LINES[D].capital || CAMP_LINES[D].open` fallback is unreachable (all 8 elements define `capital`). | `10_campaign_dialogue.js:137` |
| 11 | `divine` is defined as an element and explicitly reserved for a campaign "God" NPC, but **nothing implements it**. | `01_core_defs.js:24` |
| 12 | `campGenMap` uses `CAMP_FREQ` directly while `campGlobeMount` uses `M.f || CAMP_FREQ` — a save with a different `f` would render a sphere the generator can no longer reproduce. Harmless today (f is always 4) but a latent trap. | `:40` vs `10_campaign_globe.js:67` |
| 13 | Battle→campaign coupling runs the wrong way (`checkWin` reaches into the campaign layer). Must be inverted for a clean pure-C# rules core. | `17_turns_ai.js:405` |
| 14 | There is no v2→v3 save migration; the old key is simply deleted. | `:22` |

---

## 17. Implementation checklist for Unity

1. Bake the GP(4,0) hexsphere (162 tiles) as an asset: tile centres, CCW corner rings, adjacency, plus a triangle→tile lookup for raycast picking. Freeze the tile index order.
2. Author the 8 `ElementBarkSet` and 8 `RivalExchange` ScriptableObjects from §10.2/§10.3 verbatim.
3. Implement `CampaignMapGenerator` per §5 against a seeded RNG; unit-test contiguity of all 22 territories and all 8 empires over ≥1000 seeds (JS claims 0 failures in 800).
4. Implement `CampaignRules`, `CampaignTurnResolver`, `CampaignBattleResolver` as pure C# with no `UnityEngine` reference; unit-test the absorb cascade (construct a state where taking one throne hands you a second), the `completed` latch, and the zero-territory defeat.
5. Build the globe view: mesh + orbit camera + raycast picking + territory colouring/borders/markers. Feel constants in §11.
6. Build the challenge scene (portraits, typewriter at 14 ms/char, skip, tap-to-advance/complete).
7. Wire the battle handoff as a request/outcome pair, never a direct call into the duel's win check.
8. Persist `CampaignState` as JSON with an explicit `SchemaVersion` and a real migration hook.
9. Decide the open questions in §18 before shipping — several are one-line changes now and balance rewrites later.

---

## 18. Open questions for design

1. Should garrison affect the duel at all (starting life, a free structure, extra opening mana, a shorter deck)? Right now it is decorative.
2. Should an assault cost something (a per-turn attack budget, garrison spend, an attrition roll)? Currently a player can win the campaign in one "turn".
3. Should the AI absorb capitals and unlock dual decks the way the player does?
4. Should losing your own capital do anything?
5. Should the player be able to bring a saved custom deck into a campaign battle?
6. Should the enemy commander scale — dual banners for absorbed AI empires, a stronger AI, or a hand-built deck for capitals?
7. Is a losing assault meant to weaken the defender by 1 garrison, or should that be removed / inverted?
8. Is `divine` intended to appear as a late-campaign boss ("God" NPC), and if so where in the progression?
9. Should the number of territories (22) or empires (8) scale with anything, or stay fixed?
10. Should there be more than one campaign save slot?
