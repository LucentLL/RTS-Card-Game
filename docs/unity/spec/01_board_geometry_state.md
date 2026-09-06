# Subsystem Spec 01 — Board Geometry & Canonical Game State

**Source of truth:** the JavaScript in `src/js/`. This document is an exhaustive extraction of the
board geometry, the shape of the mutable game state (`G`), and the shape of every object that can
occupy a board cell. It is written for an implementer porting to a pure C# rules library (no
`UnityEngine` dependency) who has never read the JS.

**Primary files:** `src/js/01_core_defs.js`, `src/js/05_board_state.js`, `src/js/08_battlefield.js`,
`src/js/09_game_start.js`.
**Cross-cutting files that mutate this subsystem:** `04_cards_leaders.js` (declares `G`),
`06_mana_workers.js` (unit factories), `07_structures.js` (grave records, upgrades),
`11_deck_builder.js` (draw/deal), `12_render.js` (zone→row mapping), `13_input.js` (deployment
legality), `14_spells_traps.js` (face-down objects, flip), `15_combat.js` (row-interval geometry),
`16_movement.js` (adjacency), `17_turns_ai.js` (turn-boundary resets), `22_fx_wrappers.js`
(monkey-patches), `41_mp_sync.js` (serialization + perspective mirror), `42_mp_apply.js`
(host-side re-validation), `44_mp_lobby.js` (blocker refs).

Citations are `file:line` against the current tree.

Throughout, three categories are distinguished explicitly:

* **(R) RULE** — game rule; must be reproduced exactly in the C# core.
* **(P) PRESENTATION** — view-layer only; must NOT enter the rules core.
* **(W) BROWSER WORKAROUND** — exists only because of DOM/CSS/pointer-event behaviour; must be
  dropped or replaced with a Unity-native equivalent.

---

## 1. Overview of the spatial model

The board is **5 rows × 7 columns = 35 cells**. Rows run top→bottom from the opponent's back line to
the player's back line. Each cell holds **at most one object** (`null` when empty).

```
row idx 0   foeBack     [0][1][2][3][4][5][6]     <- opponent stronghold row ("their castle wall" is beyond this)
row idx 1   foeFront    [0][1][2][3][4][5][6]
row idx 2   center      [0][1][2][3][4][5][6]     <- SHARED, contested; lanes at 1/3/5
row idx 3   youFront    [0][1][2][3][4][5][6]
row idx 4   youBack     [0][1][2][3][4][5][6]     <- player stronghold row ("your castle wall" is beyond this)
```

Two **virtual rows** exist for combat targeting only. They have no cells and hold no objects:

| Virtual row | Index used | Meaning |
|---|---|---|
| opponent's castle wall | `-1` | one row beyond `foeBack`; the target that drains `G.P.foe.life` |
| player's castle wall | `ROWS.length` = `5` | one row beyond `youBack`; drains `G.P.you.life` |

Source: `15_combat.js:5-6`, `16_movement.js:94`, `15_combat.js:250`, `17_turns_ai.js:321`,
`42_mp_apply.js:209`, `44_mp_lobby.js:36`.

**(R)** Columns exist for *movement congestion and placement only*. **Columns never matter in
combat.** Any defender in a crossed row may intercept regardless of its column
(`15_combat.js:13`, `17_turns_ai.js:257`, `16_movement.js:90`). The legacy helper `colReach`
(`01_core_defs.js:5`) is **dead code — zero call sites in the entire tree**. Do not port it.

---

## 2. Core geometric constants

From `src/js/01_core_defs.js`:

| Name | Value | Line | Meaning |
|---|---|---|---|
| `C` | `7` | 1 | column count. **Referenced nowhere else** — dead alias of `SLOTS`. |
| `SLOTS` | `7` | 1 | cells per row. Used everywhere as the row array length and bound check. |
| `CENTER_LANES` | `[1,3,5]` | 2 | the three center columns that creatures may stand in |
| `isLane(i)` | `CENTER_LANES.includes(i)` | 3 | lane test |
| `BASE_COL` | `3` | 4 | back-centre column; used **only** as the fallback FX column when striking the enemy wall (`12_render.js:330`) |
| `colReach(a,t)` | `abs(a-t) <= 1` | 5 | **dead** |
| `centerSlotOK(which,slot,isBld)` | see below | 7 | center placement legality |
| `uid` | starts at `1`, `uid++` | 8 | global monotonically increasing instance id counter |

```
centerSlotOK(which, slot, isBuilding):
    if which != 'center'  -> true            // side rows accept anything
    if isBuilding         -> !isLane(slot)   // structures on the 4 flanks (0,2,4,6)
    else                  -> isLane(slot)    // creatures in the 3 lanes (1,3,5)
```

**(R) Center cell taxonomy.** The contested center is described in-fiction as a mountain pass:
**monster lanes at columns 1, 3, 5** and **structure slots at columns 0, 2, 4, 6**
(`01_core_defs.js:2,6`, `12_render.js:336`).

**(R) Consequences of the lane rule:**

* A creature may **never** occupy center columns 0/2/4/6. `slotExists('center', i)` returns false for
  non-lanes (`16_movement.js:5`), so movement can never target them, and `handDeployOK`/`place`
  reject them (`13_input.js:47,181`).
* A structure may **never** occupy center columns 1/3/5 (`06_mana_workers.js:222`,
  `42_mp_apply.js:181`, `centerSlotOK`).
* Occupiable-cell census: **31 creature-legal cells** (28 side-row + 3 center lanes) and
  **32 structure-legal cells** (28 side-row + 4 center flanks), out of 35 total cells.

---

## 3. Rows: the `ROWS` array and the key vocabulary

```js
const ROWS = ['foeBack','foeFront','center','youFront','youBack'];   // 05_board_state.js:4
function rowIdx(key){ return ROWS.indexOf(key); }                     // 05_board_state.js:13
```

**(R)** Row distance is `|rowIdx(a) - rowIdx(b)|`; adjacent rows are 1 apart
(`05_board_state.js:2`).

The codebase uses **three parallel naming systems**. Getting these straight is the single most
important thing in this subsystem.

### 3.1 Global row key (`key`) — absolute board position

`'foeBack' | 'foeFront' | 'center' | 'youFront' | 'youBack'`. This is the *physical* row.
It is what `rowArr`, `unitAt`, `rowIdx`, `G.atk[].k`, `G.moveFrom.k`, `G.cardMenu.k` and every
DOM element id use.

### 3.2 Owner-relative slot name (`which`) — a half-board addressing scheme

`'back' | 'front' | 'center'`. Combined with an owner (`'you' | 'foe'`) it names a storage array.

| Function | Line | Mapping |
|---|---|---|
| `rowKeyFor(owner,which)` | `05:17` | `(you,front)→youFront`, `(you,back)→youBack`, `(foe,front)→foeFront`, `(foe,back)→foeBack`, `(*,center)→center` |
| `whichOf(key)` | `15_combat.js:3` | `center→center`; key ends with `"Front"` → `front`; else `back` |
| `whichForKey(owner,key)` | `05:39-43` | inverse of `rowKeyFor`, returns `null` if `key` is not one of that owner's rows. Used once, at `44_mp_lobby.js:47`, to canonicalise a worker-pool blocker reference. |
| `mineKey(which)` | `05:25` | shorthand for `rowKeyFor('you', which)` — used only by the FX layer (`22_fx_wrappers.js:99,107`) **(P)** |

### 3.3 Worker zone name (`z`) — an economy addressing scheme

`'back' | 'front' | 'center' | 'raid'`. See §8. `'raid'` has **no storage array** — it is a *view*
over the enemy's two rows.

### 3.4 Dead vocabulary (do not port)

| Symbol | Line | Status |
|---|---|---|
| `ownRows(owner)` | `05:16` | zero call sites |
| `canDeploy(owner,which)` | `05:22` | zero call sites |
| `MINE` | `05:24` | zero call sites |
| `C` | `01:1` | zero call sites |
| `colReach` | `01:5` | zero call sites |

---

## 4. Storage model: per-owner arrays vs. the shared center

This is a **positional** model wearing an ownership-shaped costume. Read carefully.

```js
function rowArr(key){                     // 05_board_state.js:5-12
  if(key==='center')   return G.center;
  if(key==='foeBack')  return G.P.foe.back;
  if(key==='foeFront') return G.P.foe.front;
  if(key==='youFront') return G.P.you.front;
  if(key==='youBack')  return G.P.you.back;
  return null;
}
function cellArr(owner,which){            // 05_board_state.js:21
  return which==='center' ? G.center : (G.P[owner] ? G.P[owner][which] : null);
}
function unitAt(key,i){ return rowArr(key)[i]; }   // 05_board_state.js:14
```

There are exactly **five backing arrays**, each of length `SLOTS` (7):

| Array | Row key | Row index |
|---|---|---|
| `G.P.foe.back` | `foeBack` | 0 |
| `G.P.foe.front` | `foeFront` | 1 |
| `G.center` | `center` | 2 |
| `G.P.you.front` | `youFront` | 3 |
| `G.P.you.back` | `youBack` | 4 |

### 4.1 (R) CRITICAL: arrays are addressed by ROW, not by unit ownership

`G.P.you.front` means *"the array for the physical row named youFront"*, **not** *"the units you
own"*. Because the enemy back row is enterable and all middle rows are contested, a **foe-owned
creature can and routinely does live inside `G.P.you.front` and `G.P.you.back`**, and vice versa.

The codebase is explicit about this: `05_board_state.js:46` — *"fronts are contested — always filter
by the unit's own tag"* — and `16_movement.js:199`, `13_input.js:170-171`.

Therefore **every** enumeration of "my stuff" filters on `o.owner`:

```js
function ownUnits(owner){                                       // 05_board_state.js:46
  const out=[];
  ROWS.forEach(k => rowArr(k).forEach(o => { if(o && o.owner===owner) out.push(o); }));
  return out;
}
function structuresOf(owner){ return ownUnits(owner).filter(o=>o.kind==='building'); }  // 05:47
```

**Port implication:** in C#, do **not** model the board as `Player.FrontRow`. Model it as one
`BoardCell[5][7]` indexed by `(RowIndex, Column)`, with per-object `Owner`. Keep a mapping helper
for the legacy `(owner, which)` addressing because the deployment/build/worker code is written in
those terms.

### 4.2 (R) The center is genuinely shared

`G.center` is one array. Both players' units occupy it; `renderCenter` reads `o.owner` per cell
(`12_render.js:340`), `minionsInRow('center')` pushes both sides' worker pools
(`05_board_state.js:36`), and `findArmedTrap`'s center branch is the **only** trap scan that checks
`o.owner === owner` (`14_spells_traps.js:38`) — the side-row branches (`14_spells_traps.js:35`,
`30_resp.js:12`) rely on the array being nominally owned, because face-downs are only ever written
into the setter's own two rows.

**Port risk:** if C# ever lets a player set a face-down into a contested row it does not nominally
own, the side-row trap scan becomes wrong. Add the `owner` check unconditionally in the port.

---

## 5. The 9 ELEMENTS table

Declared at `01_core_defs.js:15-26`. Every element carries display name, kanji glyph, a colour
palette, and a *command-centre identity* (`hp`, `wk`).

| id | name | glyph | color | accent | deep | bg (3 radial stops) | hp | wk |
|---|---|---|---|---|---|---|---|---|
| `fire` | Fire | 炎 | `#e0613f` | `#ff8a1f` | `#86291c` | `#5e1d10`,`#2a0f08`,`#080403` | 10000 | 2 |
| `water` | Water | 水 | `#3fa3e0` | `#7fd0f5` | `#0e5a7a` | `#0f3a52`,`#0a2230`,`#03090f` | 10000 | 3 |
| `earth` | Earth | 地 | `#c0863c` | `#e5b66a` | `#7a5320` | `#4a3413`,`#2a1c0a`,`#0a0704` | 10000 | 2 |
| `wind` | Wind | 風 | `#76c7c0` | `#cdeeea` | `#2f726b` | `#123d3a`,`#0c2422`,`#04100f` | 10000 | 3 |
| `forest` | Forest | 森 | `#4fae5e` | `#a6f0ac` | `#27692f` | `#173d1d`,`#0d250f`,`#041206` | 10000 | 2 |
| `electric` | Electric | 雷 | `#f2cf3b` | `#fff7a8` | `#9a7a16` | `#3e3408`,`#241d05`,`#0a0802` | 10000 | 3 |
| `light` | Light | 光 | `#ece3c0` | `#ffffff` | `#b0a45e` | `#3a3622`,`#221f12`,`#0a0905` | 10000 | 3 |
| `dark` | Dark | 闇 | `#9a5cc6` | `#caa0ec` | `#56307a` | `#2e1a40`,`#1a0f26`,`#080510` | 10000 | 2 |
| `divine` | Divine | 神 | `#c9d4ec` | `#ffffff` | `#5a6a96` | `#2b3450`,`#171d2e`,`#070a12` | 10000 | 2 |

Each entry also has a `lore` string (used for the command-centre description text).

```js
const MAJORS = ['fire','water','earth','wind','forest','electric','light','dark'];  // 01:27
const COLORS = MAJORS.slice();                                                       // 01:28
const clsOf  = { <every key of ELEMENTS incl. divine> : key + '-c' };                // 01:29  (P)
function zc(){ return { fire:0, water:0, ..., dark:0 }; }                             // 01:30
```

### 5.1 (R) Why `divine` is excluded from `MAJORS`

`01_core_defs.js:24` — *"Divine is NOT a major/deckable element — reserved for Ace / Boss / God cards
(e.g. a campaign 'God' NPC)."*

Concrete downstream consequences:

1. **No commander.** `CCS` is generated by iterating `COLORS` (`04_cards_leaders.js:10,15-16`), so
   there are 8 solo commanders and C(8,2)=28 dual commanders = **36 total**. No divine commander.
2. **Not deck-buildable.** `CARD_REG` is built from `POOLS[el] for el of COLORS`
   (`06_mana_workers.js:40`), so divine creatures never enter the registry, never pass
   `cardColorOK`, and cannot be added to a saved deck.
3. **No forge.** `buildList` adds a forge per commander colour (`03_cards_creatures.js:75,77`);
   `FORGE_NAMES.divine = 'Empyreum'` exists (`03:23`) but is unreachable through `buildList`.
4. **Art & CSS still cover it.** `clsOf` is built from `Object.keys(ELEMENTS)` (all 9), and the
   sleeve/frame system iterates all 9 elements plus `'neutral'` (`04_cards_leaders.js:169`), so a
   divine card would still render correctly if injected. `DIVINE` roster exists at
   `03_cards_creatures.js:22` (4 cards: Cherub, Valkar, Archon, Empyrean) and is referenced nowhere.

**Port instruction:** model elements as a 9-value enum; keep a separate `Deckable`/`IsMajor` flag.
Do not hard-code "8 elements".

### 5.2 Command centres (`CCS`) — the only consumer of `hp`/`wk`

`04_cards_leaders.js:9-22`. For each element `el` in `COLORS`:
`CCS[el] = { id, name, hp: E.hp, wk: E.wk, colors:[el], desc: E.lore }`.
For each unordered pair `(a,b)` with `a` before `b` in `COLORS`:
`CCS[a+'_'+b] = { id, name: "A / B", hp: round((hpA+hpB)/2), wk: round((wkA+wkB)/2), colors:[a,b], desc: DUAL_LORE[n++] }`.

Only two fields of `CCS` matter to this subsystem:

* **(R)** `cc.hp` → the player's starting `life` (`09_game_start.js:3-4`).
* **(R)** `cc.wk` → a **flat additive bonus to the back-row worker figure, every time it is
  computed** (`05_board_state.js:66`). It is not a one-time grant.

`mkCC()` (`04_cards_leaders.js:23-24`) builds a command-centre *card object*, and `findCC()`
(`04:25`) returns `null` unconditionally. **No command-centre card is ever placed on the board.**
The back row *is* the stronghold; `life` is a standalone pool (`09_game_start.js:7-8`,
`12_render.js:7`). `mkCC` has **zero call sites** — it is dead. The `o.cc === true` guards scattered
through the code (`06_mana_workers.js:137,168,188`, `07_structures.js:5,25`, `13_input.js:55,186`,
`42_mp_apply.js:85,191`) are therefore all permanently false in the current build. Port them as a
flag on the unit type anyway if a boss/keep unit is planned; otherwise delete.

---

## 6. The `G` global — canonical mutable game state

Declared literal at `04_cards_leaders.js:214-223`; re-initialised at `09_game_start.js:3-6`.
The literal's values (`life:25`, `cc:'fire'/'water'`) are **pre-game placeholders** overwritten by
`startGame`.

### 6.1 Root fields

| Field | Type | Declared | Reset by | Serialized? | Category | Meaning |
|---|---|---|---|---|---|---|
| `turn` | `'you' \| 'foe'` | `04:215` | `09:5`, `17:50` | **yes** | R | whose turn it is |
| `busy` | bool | `04:215` | `09:5` | no | R (input gate) | an async resolution is running; blocks input |
| `over` | bool | `04:215` | `09:5`, set at `17:398` | **yes** | R | game finished |
| `turnNo` | int | `04:215` | `09:5` (=1), `17:50` (`++`) | **yes** | R | **ply counter, not round counter** — incremented on *every* `startTurn`, for both players |
| `phase` | `'upkeep'\|'draw'\|'action'\|'end'` | `04:216` | `setPhase` `17:45` | **yes** | R | see §11 |
| `upkeep` | bool | `04:216` | `setPhase` `17:45` | no (derived) | R | **strictly derived**: `phase === 'upkeep'`. Redundant duplicate — collapse in C#. |
| `sel` | `null \| {kind:'hand', idx:int, mode:string\|null}` | `04:217` | many | no | R+P | held hand card + chosen action mode (`summon\|build\|set\|settrap\|cast`) |
| `atk` | `Array<{k:rowKey, i:int}>` | `04:217` | many | no | R+P | the selected attack group (references, not objects) |
| `decls` | `Array<Declaration>` | `04:217` | `09:5`, `17:50`, `15:310` | **no** | R | committed Combat-v3 attack declarations awaiting Resolve |
| `moveFrom` | `null \| {k:rowKey, i:int}` | `04:217` | many | no | R+P | creature currently being repositioned |
| `moveMana` | `null \| {k:rowKey, i:int}` | `04:217` | many | no | R+P | card whose banked ◆ is being transferred |
| `cardMenu` | `null \| {k?:rowKey, i:int, hand?:bool, html:string}` | assigned `13:23` etc. | many | no | **P** | floating action-menu anchor + raw HTML. Pure view state; do not port the `html` field. |
| `build` | `null \| StructDef` | assigned `06:218` | `06:220,224,227` | no | R+P | structure definition awaiting placement |
| `center` | `Array(7)` of cell objects/null | `04:218` | `09:6` (**new array**) | **yes** | R | the shared contested row |
| `P` | `{ you: PlayerState, foe: PlayerState }` | `04:219-222` | `09:3-4` (`Object.assign`, keeps identity) | **yes** | R | per-player state |
| `minSel` | always `null` | never assigned non-null | — | no | dead | vestigial minion-selection cursor; only ever cleared (`13:4,94,98`, `15:152`, `31:218,234`, `41:51`) |
| `powerMode` | never assigned | read once `13_input.js:3` | — | no | dead | always `undefined`; vestigial commander-power gate |
| `deficit` | never assigned | read once `15_combat.js:149` | — | no | dead | always `undefined`; vestigial harvest lock |

`Declaration` (built at `15_combat.js:245`):
```
{ a: {k:rowKey, i:int},        // the attacker's cell reference
  kind: 'unit'|'wall'|'workers',
  tk: rowKey|null,             // target row key   (unit)
  ti: int|null,                // target column    (unit)
  wWhich: 'back'|'front'|'center'|undefined,  // worker-pool zone (workers)
  blockers: Array<{key:rowKey, i?:int, c:UnitObject}> }
```

### 6.2 `PlayerState` (`G.P.you` / `G.P.foe`)

Full field list, from the literal `04_cards_leaders.js:220-221` and the `startGame` assign
`09_game_start.js:3-4`:

| Field | Type | Initial (startGame) | Serialized? | Category | Meaning |
|---|---|---|---|---|---|
| `color` | element id | `cc.colors[0]` | yes | R | default colour stamped onto units with no own colour (`06:90,94,114`) |
| `cc` | CCS id | the chosen commander id | yes | R | commander identity; drives `buildList` and the back-row `wk` bonus |
| `life` | int | `cc.hp` (**10000**) | yes | R | standalone life pool; 0 ⇒ loss (`17:392-406`) |
| `mana` | int | `0` | yes | R | single generic pool, hard-capped at **99** on every credit (`15:158`, `16:184`, `17:5`, `42:23,35`) |
| `cmana` | `{fire:0,...,dark:0}` | `zc()` | yes | **inert** | legacy per-colour pool; seeded, never read or written after init (`06_mana_workers.js:1-4`) |
| `hand` | `HandCard[]` | `[]` then 4 draws | yes | R | no hand-size limit anywhere in the code |
| `deck` | `CardTemplate[]` | `deckOf(cc.colors)` or the supplied deck | yes | R | drawn from the **end** (`.pop()`, `11:250`) |
| `grave` | `GraveRecord[]` | `[]` | yes | R | push-only except `reviveFromGrave` (`17:16`) |
| `front` | `Array(7)` | all `null` | yes | R | the physical `<owner>Front` row |
| `back` | `Array(7)` | all `null` | yes | R | the physical `<owner>Back` row |
| `min` | `{back:[], front:[], center:[]}` | empty | yes | R | worker (minion) pools — see §7 |
| `firstExtract` | bool | `true` | yes | **inert** | set `true` each `startTurn` (`17:51`), set `false` on any mana gain (`15:161`, `16:185`); never read |
| `villagerUsed` | bool | `false` | yes | **inert** | never read or written after init |
| `upaid` | `{back:int,front:int,center:int,raid:int}` | all `0` | yes | R | mana already paid this upkeep per zone; subtracted from the raw shortfall (`05:84`) |

**Note:** `G.P.you`/`G.P.foe` object *identity* is preserved by `startGame` (it uses
`Object.assign`), but `front`/`back`/`min` are replaced with fresh arrays, and `G.center` is
replaced with a fresh array (`09:6`). Any cached array reference is invalidated at game start.

---

## 7. On-board object shapes ("unit instance" model)

Every board cell holds `null` or exactly one object with a `kind` discriminator. There are **four
live kinds** on the board (`creature`, `building`, `charge`, `trap`), one hand-only kind
(`handcard`), and one dead kind (`building` with `cc:true`).

The tables below were produced by grepping **every** property assignment across all of `src/js`,
not just the constructors.

### 7.1 `kind: 'creature'` — `mkCre(t, owner, worker)` (`06_mana_workers.js:90-92`)

| Property | Type | Init | Written by | Category | Meaning |
|---|---|---|---|---|---|
| `kind` | `'creature'` | literal | — | R | discriminator |
| `id` | int | `uid++` | — | R | unique instance id |
| `owner` | `'you'\|'foe'` | arg | flipped wholesale in MP adopt (`41:45,47-48`) | R | **the only authority on ownership** |
| `worker` | bool | `!!worker` | — | R | a worker/minion body (see §7.7) |
| `color` | element id \| null | `t.color ?? G.P[owner].color` | `06:114` (tokens), `07:21` (upgrade) | R | element attribute (synergy/art only; does **not** gate cost) |
| `nm` | string | `t.nm` | `06:149` (chrysalis hatch), `07:17` | R | display name; also the art-slug source |
| `a` | int | `t.a` | `15:115` (`+500` thornmail), `06:149` | R | attack |
| `h` | int | `t.h` | damage everywhere (`15:46,105,106`, `06:129`), `07:19` | R | current HP |
| `maxh` | int | `t.h` | `15:115` (`+1000`), `06:149`, `07:19` | R | max HP |
| `c` | int | `t.c` | `07:20` | R | mana cost |
| `fs` | bool | `!!t.fs` | `06:149` | R | First Strike |
| `up` | int | `t.up \|\| 0` | `06:149` | R | **worker upkeep** — subtracted from its row's worker figure |
| `sick` | bool | `false` (set `true` on summon: `13:197,216`, `17:310`, `42:97,119`) | `17:53` (clear), `06:149,151` (re-set), `14:121` | R | summoning sickness |
| `tapped` | bool | `false` | `15:126,157,184,268`, `16:51,64,73,95,102`, `17:53,185,278,320`, `42:216,235` | R | has acted this turn |
| `moved` | bool | `false` | `16:52`, `17:53,185`, `42:69` | R | used its move this turn |
| `moved2` | bool | *absent until set* | `16:51`, `17:53,185`, `42:69` | R | used a **second** (upkeep-only) move; also taps |
| `paid` | bool | *absent until set* | `17:53,134`, `42:53` | R | its upkeep keep has been paid this upkeep |
| `blocked` | bool | `false` | `15:184,255`, `16:73,102`, `17:53,340`, `42:235` | R | has already interposed this turn (once-per-turn block gate) |
| `bank` | int | `0` | `13:197,202`, `14:77,114,121` | R | mana stored on the card |
| `art` | string (data URI / path) | `t.art` | `07:20`, nulled by MP strip (`41:13`) | **P** | |
| `kw` | keyword id \| null | `t.kw \|\| null` | `06:149` | R | one of `detonate\|undertow\|entrench\|ward\|reap\|chrysalis\|scour\|overcharge` |
| `det` | int | `t.det \|\| 0` | — | R | Detonate damage |
| `ward` | int | `t.ward \|\| 0` | — | R | (declared; only `wardhp` is used) |
| `wardhp` | int | `t.wardhp \|\| 2` | — | R | HP of the Lumen token |
| `reap` | int | `t.reap \|\| 0` | — | R | Shade token stats |
| `grow` | int | `t.grow \|\| 0` | — | R | Chrysalis counters per turn |
| `hatch` | int | `t.hatch \|\| 0` | — | R | Chrysalis threshold |
| `into` | `{nm,a,h,up?,fs?,kw?}` \| null | `t.into \|\| null` | — | R | Chrysalis hatch form |
| `cnt` | int | `t.cnt \|\| 0` | `06:147` | R | Chrysalis counter |
| `oc` | int | `t.oc \|\| 0` | `06:156,160` | R | banked Overcharge ◆ (cap 3) |
| `entrench` | bool | `!!t.entrench` | — | R | immune to bounce/Undertow |
| `token` | bool | `!!t.token` | `06:114` | R | token body; not revivable, no grave keywords |
| `tribe` | string \| null | `t.tribe \|\| null` | — | R | `'Human'\|'Dragon'` |
| `subtype` | string \| null | `t.subtype \|\| null` | — | R | `'Wizard'\|'Warrior'` |
| `_dis` | int | *absent until set* | `06:160,163`, `17:53` | R | transient Overcharge discharge bonus, added by `effA` |
| `cc` | — | never set on creatures | — | dead | |

`effA(c) = (c.a || 0) + (c._dis || 0)` (`06_mana_workers.js:115`) is the **effective attack** used
by combat and wall damage. `c.a` alone is used for retaliation damage (`15:279,297`) and for the
blocker-power readout — an inconsistency worth preserving or deliberately fixing.

### 7.2 `kind: 'building'` — `mkBld(t, owner)` (`06_mana_workers.js:94`)

| Property | Type | Init | Written by | Category | Meaning |
|---|---|---|---|---|---|
| `kind` | `'building'` | literal | — | R | |
| `id` | int | `uid++` | — | R | |
| `owner` | `'you'\|'foe'` | arg | MP adopt | R | |
| `color` | element id \| null | `t.color ?? G.P[owner].color` | `07:21` | R | |
| `nm` | string | `t.nm` | `07:17` | R | |
| `h` | int | `t.h` | damage, `07:19` | R | current HP |
| `maxh` | int | `t.h` | `07:19` | R | |
| `c` | int | `t.c` | `07:20` | R | build cost |
| `eff` | string | `t.eff` | `07:17` | R | `mana\|villager\|damage\|vault\|wall\|revive\|none` |
| `val` | int | `t.val \|\| 0` | `07:17` | R | magnitude of `eff` |
| `sup` | int | `t.sup \|\| 0` | `07:17` | R | **worker support** — added to its row's worker figure. May be **negative** (Cannon Tower `-2`). |
| `ic` | string (glyph) | `t.ic` | `07:17` | **P** | |
| `art` | string | `t.art` | `07:20` | **P** | |
| `bank` | int | `0` | `13:202`, `14:77,114` | R | stored mana |
| `bid` | string \| null | `t.bid \|\| null` | `07:17` | R | build id; drives the upgrade tree and `bidLineage` prereqs |
| `cc` | bool | only on the dead `mkCC` object | — | dead | command centre marker |
| `colors` | `string[]` | only on `mkCC` | — | dead | |

Structures **never move, never attack, and never retaliate** (`13:134`, `15:199`).

`STRUCT_DEFS` (`03_cards_creatures.js:53-69`) plus `forgeDef(el)`/`grandForgeDef(el)`
(`03:70-71`) supply the definitions. Fields relevant to geometry:

| bid | c | h | eff | val | **sup** | prereq | from | **row gate** | up2 |
|---|---|---|---|---|---|---|---|---|---|
| `foundry` | 2 | 3000 | mana | 1 | **+2** | — | — | — | keep |
| `forge` (per element) | 3 | 2500 | mana | 1 | **+2** | foundry | — | — | grandforge |
| `encampment` | 2 | 2500 | none | 0 | **+2** | foundry | — | — | longhouse |
| `longhouse` | 4 | 3000 | villager | 0 | **+3** | foundry | — | **front** | barracks |
| `vault` | 4 | 3000 | vault | 4 | **0** | foundry | — | — | grandvault |
| `bulwark` | 5 | 6000 | wall | 0 | **+1** | forge | — | — | — |
| `outpost` | 2 | 3000 | none | 0 | **+1** | forge | — | — | tower, bastion |
| `tower` | 4 | 4000 | damage | 1000 | **−2** | forge | — | — | — |
| `reliquary` | 5 | 3500 | revive | 0 | **+1** | longhouse | — | — | — |
| `keep` | 3 | 5000 | mana | 1 | **+3** | — | foundry | **back** | citadel |
| `citadel` | 4 | 7500 | mana | 2 | **+4** | — | keep | **back** | — |
| `barracks` | 3 | 5000 | villager | 0 | **+4** | — | longhouse | **front** | — |
| `bastion` | 3 | 9000 | wall | 0 | **+2** | — | outpost | — | — |
| `grandforge` (per element) | 6 | 3500 | mana | 3 | **+3** | forge | forge | — | — |
| `grandvault` | 5 | 4500 | vault | 10 | **0** | — | vault | — | — |

**(R)** `def.row` gates *which* row an upgrade tier may live in (`07_structures.js:10`) — checked
against `whichOf(key)`, so `'center'` never satisfies `'back'`/`'front'`.

### 7.3 `kind: 'charge'` — a face-down creature or structure

Created at `13_input.js:233` (player) and `42_mp_apply.js:139` (MP guest):

```
{ kind:'charge', owner:'you'|'foe', w:'back'|'front',   // the OWNER-RELATIVE slot name at set time
  ctype:'creature'|'building',
  card:{ ...frozen template snapshot... },
  inv: int,                                             // ◆ invested so far; starts at 1
  setTurn: G.turnNo }
```

`card` payload for `ctype==='building'`: `{nm,c,h,eff,val,sup,ic,art}`.
`card` payload for `ctype==='creature'`: `{nm,a,h,c,fs,up,art,kw,det,ward,wardhp,reap,grow,hatch,into,entrench,tribe,subtype}`.

**(R)** Rules attached to `charge`:
* Setting costs **◆1**, which is *banked onto the card* (`13:227-228`, `inv:1`).
* `inv >= card.c` ⇒ funded ⇒ can be flipped (`14:108`).
* Attacked while under-funded ⇒ destroyed, banked ◆ lost (`15:90-92`).
* Attacked while funded ⇒ flips and fights back (`15:94-98`).
* On flip: surplus `inv - card.c` becomes the new unit's `bank`; the creature is `sick` iff
  `G.turnNo <= setTurn` (`14:119-121`).

### 7.4 `kind: 'trap'` — a face-down spell with a trigger

Created at `13_input.js:224`, `17_turns_ai.js:300`, `42_mp_apply.js:129`:

```
{ kind:'trap', owner:'you'|'foe', w:'back'|'front',
  card:{ nm, c, effect, trigger, val, ic, art, trap:true },
  setTurn: G.turnNo }
```

**(R)** A trap is *armed* only when `G.turnNo > setTurn` (`14:36,38`, `30_resp.js:13,15`). Because
`turnNo` is a ply counter, this means "armed from the opponent's very next turn onward".
`trigger` is `'summon'` or `'attack'`.

### 7.5 `kind: 'handcard'` — hand-only

Created by `drawCard` (`11_deck_builder.js:250-251`), `handcardFromCreature`
(`06_mana_workers.js:112-113`), and `reviveFromGrave` (`17_turns_ai.js:17-18`).

Fields: `kind:'handcard', id:uid++, type:'creature'|'building'|'spell', color, nm, a, h, c, fs, up,
sup, eff, val, ic, art, trap, effect, target, trigger, kw, det, ward, wardhp, reap, grow, hatch,
into, entrench, tribe, subtype`.

Note it uses `type`, not `kind`, as the card-class discriminator — the board objects use `kind`.
Both spellings are live and must be preserved in the port's mapping layer.
Spells are colour-stamped `null` on draw (`11:250`).

### 7.6 `GraveRecord` — `toGrave(owner, obj)` (`07_structures.js:67-76`)

A **template-shaped** record, not the live object. Four branches:

| source `kind` | record |
|---|---|
| `creature` | `{type: worker?'villager':'creature', nm,a,h:maxh??h,c,up,fs,art,color,token, kw:(token?null:kw), det,ward,wardhp,reap,grow,hatch,into,entrench,tribe,subtype}` |
| `building` | `{type:'building', nm, h:maxh??h, c, eff, val, sup, ic}` |
| `charge` | `{type: obj.ctype ?? 'creature', nm,a,h,c,up,sup,eff,val,ic}` (read off `obj.card`) |
| `trap` | `{type:'spell', nm, c, trap:true, effect, val, ic}` |

Anything else is silently dropped. `spellRec(card)` (`13_input.js:71`) makes the same shape for a
cast spell.

**(R)** Note `h` is restored to **maxh** in the grave record — a damaged creature returns to the
graveyard at full health, and `reviveFromGrave` therefore returns it to hand at full health.

### 7.7 Worker bodies — `mkVil(owner)` (`06_mana_workers.js:93`)

`mkVil` = `mkCre({nm:'Worker', a:0, h:1000, c:0, up:0, art:ART.villager}, owner, true)`.

A worker is a full creature object with `worker: true`, **but it never occupies a board cell**. It
lives in a per-owner, per-zone pool (§8.3). Consequences:

* `kwOf(o)` returns `null` for workers (`06:98`) — no keywords.
* Excluded from `monsterUpkeep`, from attackers, from `creaturesInRow`.
* `canActNow` returns `true` for workers (no stand/lie pose) (`16:31`) **(P)**.
* It *can* intercept (`15:18`) and *can* be attacked as a stack (`15:173`).
* Its `h` is 1000 and `a` is 0, so it dies to any hit and deals no damage.
* Inspect text calls it *"Harvester. Harvests with its row. Blocks; cannot attack."* (`18:104`).

### 7.8 The `laid` visual state **(P)**

`12_render.js:175` adds CSS class `laid` when `canActNow(o,key,i)` is false. `canActNow`
(`16_movement.js:30-38`) is pure presentation logic:

```
canActNow(o, key, i):
  if o is not a non-worker creature   -> true
  if G.turn == o.owner:
      if o.tapped   -> false
      if !o.sick    -> true
      else          -> (!moveSpent(o)) AND (some adjacent cell is empty)
  else:
      return canBlockNow(o)  ==  !o.blocked
```

`laid` is **not** a state field on the object. Do not add one; derive it in the view.

---

## 8. Zones, raid keys, and the worker (minion) economy

### 8.1 The `ZONES` model

```js
const ZONES = ['back','front','center','raid'];              // 05_board_state.js:56
function raidKeys(owner){ return owner==='you' ? ['foeFront','foeBack'] : ['youFront','youBack']; }  // 05:58
function zoneKeys(owner,z){ return z==='raid' ? raidKeys(owner) : [zoneKey(owner,z)]; }              // 05:59
function zoneKey(owner,z){                                                                            // 05:60
  return z==='center' ? 'center'
       : z==='raid'   ? (owner==='you' ? 'foeFront' : 'youFront')
       : rowKeyFor(owner, z);
}
```

**(R)** `'raid'` is the zone for *your units standing in the enemy's two rows*. It has **no
structures behind it**, so its figure is never positive; an army camped there must be paid for (or
pulled back) at every upkeep (`05:53-55`).

**⚠ Discrepancy to resolve in the port:** `zoneKeys` (plural) correctly returns **both** enemy rows
for `'raid'` (`05:59`, comment at `05:57` — *"'raid' spans BOTH enemy rows now that the enemy back
row is enterable"*), but `zoneKey` (singular) returns **only the enemy FRONT row** (`05:60`). The
singular form is used for:
* the upkeep hint's row label (`17_turns_ai.js:91`) — cosmetic,
* the AI's move destination (`17:181`) — but `'raid'` is never a move destination (`17:196`),
* the MP worker-attack target row index (`42_mp_apply.js:211`) — never `'raid'`, only
  `back/front/center`,
* the FX layer (`22_fx_wrappers.js:146`) — cosmetic **(P)**.

So the discrepancy is currently benign, but it *is* a latent bug. In C#, define
`RowsOfZone(owner, zone) -> RowIndex[]` and derive any single-row label from `[0]`.

### 8.2 Reverse map: which zone of `owner` lives in a given row

```js
function zoneForRow(owner,key){                              // 12_render.js:184-188
  if(key==='center') return 'center';
  if(owner==='you')  return key==='youBack' ? 'back'
                          : key==='youFront' ? 'front'
                          : (key==='foeFront'||key==='foeBack') ? 'raid' : null;
  return key==='foeBack' ? 'back'
       : key==='foeFront' ? 'front'
       : (key==='youFront'||key==='youBack') ? 'raid' : null;
}
```

**(R)** This is a *rule* function despite living in the render file: the upkeep pay/sacrifice flow
routes through it (`17_turns_ai.js:119,130`, `42_mp_apply.js:49`). Move it into the C# core.

Full mapping (every row is covered for both owners; `null` never occurs):

| row key | zone for `you` | zone for `foe` |
|---|---|---|
| `foeBack` | `raid` | `back` |
| `foeFront` | `raid` | `front` |
| `center` | `center` | `center` |
| `youFront` | `front` | `raid` |
| `youBack` | `back` | `raid` |

### 8.3 Minion pools (workers are NOT board slots)

```js
function minPool(owner,which){ return G.P[owner].min[which] || []; }   // 05:27
```

`G.P[owner].min` has exactly three keys: `back`, `front`, `center`. **There is no `min.raid`** —
`minPool(owner,'raid')` returns `[]` via the `||[]` fallback ("no support behind enemy lines",
`05:27`).

```js
function minionsInRow(key){                                            // 05:29-38
  // returns [{owner, which, c:UnitObject}, ...]
  foeBack  -> foe/back      foeFront -> foe/front
  youFront -> you/front     youBack  -> you/back
  center   -> you/center AND foe/center      // both sides
}
```

Note the asymmetry: `minionsInRow` maps a *row* to whichever owner nominally owns it, i.e. worker
pools are **not** contested — an enemy raider standing in your front row does not bring workers.

### 8.4 (R) Per-row worker figure — the live economy formula

```js
function rowWorkers(owner, which){                                     // 05:61-68
  let s = 0;
  for (const k of zoneKeys(owner, which))
    for (const o of rowArr(k)) {
      if (!o || o.owner !== owner) continue;
      if (o.kind === 'building')  s += (o.sup || 0) + (o.eff === 'villager' ? (o.val || 0) : 0);
      else if (o.kind === 'creature' && !o.worker) s -= (o.up || 0);
    }
  if (which === 'back') s += CCS[G.P[owner].cc].wk;   // homeland staffs the back row
  return s;
}
```

Points an implementer must not get wrong:

1. **Ownership-filtered.** Only objects with `o.owner === owner` count, even in that owner's own
   nominal rows. Enemy raiders in your front row do not drag your figure down; **your** raiders in
   their rows drag your `raid` figure down.
2. **Faceless cards do not count.** `charge` and `trap` objects have neither `sup` nor `up` and are
   skipped by the `kind` tests.
3. **The `villager` bonus is currently a no-op.** Longhouse and Barracks are the only `eff:'villager'`
   structures and both have `val: 0` (`03_cards_creatures.js:56,66`). The `+ (o.val||0)` term
   therefore always adds 0. Related: `buildingUpkeep` (`17_turns_ai.js:2-11`) handles `mana`,
   `damage`, and `revive` but **not** `villager` — so villager structures do nothing per turn today
   beyond their `sup`. `trainVillager` (`14_spells_traps.js:128-133`) exists but has **zero call
   sites in game logic** (only the FX wrapper at `22:212-217`). Decide in the port whether to fix or
   delete.
4. **The commander bonus is recurring**, applied on every `back` computation, not once at setup.
5. `rowWorkers` may be **negative**; that negative value is the shortfall.

```js
function totalWorkers(owner){                                          // 05:69
  return ['back','front','center'].reduce((s,w)=>s+Math.max(0,rowWorkers(owner,w)), 0);
}
```
`'raid'` is deliberately excluded from the total (it is never positive).

### 8.5 (R) Pool synchronisation and readiness

```js
function syncWorkers(owner){                                           // 05:71-78
  for (const which of ['back','front','center']) {
    const target = Math.max(0, rowWorkers(owner, which));
    const pool = G.P[owner].min[which];
    while (pool.length > target) pool.pop();                 // shrink: drop from the END, no grave record
    while (pool.length < target) { const w = mkVil(owner); w.sick = true; pool.push(w); }
  }
}
function readyWorkers(owner){                                          // 05:81
  for (const w of ['back','front','center'])
    for (const m of G.P[owner].min[w]) { m.sick=false; m.tapped=false; m.moved=false; }
}
```

* `syncWorkers` **preserves the `tapped`/`sick` state of surviving workers** (it only pops the tail).
* New workers enter **summoning-sick**, so a structure cannot harvest with workers it created this
  turn (`05:76`).
* `readyWorkers` runs only at turn start, after upkeep balancing (`05:79-80`,
  `09_game_start.js:10`, `17_turns_ai.js:59,69`), so mid-turn additions stay sick until next turn.
* Workers removed by `syncWorkers` are **not** sent to the graveyard. Workers killed by damage **are**
  (`16_movement.js:202-203`).

`syncWorkers` call sites (all must be reproduced): `06:23 afterDeploy`, `07:30 upgradeStruct`,
`07:43 aiUpgrade`, `07:61 aiBuild`, `09:9 startGame`, `14:125 flip`, `16:55 doMove`,
`17:58 startTurn`, `17:143 upkeepSac`, `17:203,213 aiFixDeficit`, `42:44,72,197` (MP).

### 8.6 (R) Deficits and the `upaid` ledger

```js
function zoneDeficit(owner,z){                                          // 05:84
  const paid = (G.P[owner].upaid || {})[z] || 0;
  return Math.max(0, Math.max(0, -rowWorkers(owner,z)) - paid);
}
function deficitRows(owner){ return ZONES.filter(w => zoneDeficit(owner,w) > 0); }   // 05:85
function totalDeficit(owner){ return ZONES.reduce((s,w)=>s+zoneDeficit(owner,w), 0); } // 05:86
function creaturesInRow(owner,which){                                    // 05:87-91
  // [{which, key, i, o}] for every non-worker creature of `owner` in the zone's row(s)
}
```

`upaid` is **reset to all-zeroes at the start of each of that player's turns** (`17_turns_ai.js:52`),
i.e. keep payments expire and shortfalls are settled anew each upkeep.

`orphanDeficit(owner)` (`17_turns_ai.js:103-107`) is the portion of the shortfall in zones with **no
settle-able creature** (e.g. a Cannon Tower whose supporting structure was razed). Harvest is allowed
to pay that portion directly rather than deadlocking the turn (`17:162-169`, `12_render.js:20`).

### 8.7 The DEAD global worker-cap system

`05_board_state.js:47-50` and `06_mana_workers.js:12-22` define a second, older, **global** worker
economy:

```js
structSupport(owner) = Σ over ALL of owner's buildings of (sup||0)          // 05:48
monsterUpkeep(owner) = Σ over ALL of owner's non-worker creatures of (up||0) // 05:49
workerCap(owner)     = structSupport - monsterUpkeep                        // 05:50
minionCount(owner)   = min.back.length + min.front.length + min.center.length // 06:12
canTrain(owner)      = minionCount < workerCap                              // 06:13
enforceCap(owner)    = cull minions (front, then center, then back) down to the cap // 06:15-22
```

**None of these are reachable from live game logic.** `canTrain` is called only by `trainVillager`,
which is called only by its own FX wrapper; `enforceCap` has zero call sites; `workerCap` is called
only by `canTrain`/`enforceCap`. Notably `workerCap` **omits the commander `wk` bonus** while
`rowWorkers` includes it — the two systems would disagree if both were live. `structuresOf` is still
live (structure count readout, `12_render.js:10`).

**Port instruction:** implement only the per-row model (§8.4–8.6). Do not port `workerCap`.

---

## 9. Adjacency and movement geometry

```js
function moveChainOf(owner){                                             // 16_movement.js:3
  return owner === 'you'
    ? ['youBack','youFront','center','foeFront','foeBack']
    : ['foeBack','foeFront','center','youFront','youBack'];
}
function slotExists(w,i){ return i>=0 && i<SLOTS && (w!=='center' || isLane(i)); }  // 16:5
function adjCells(owner,key,i){                                          // 16:8-14
  const out=[];
  for (const dj of [-1,1]) { const j=i+dj; if (slotExists(key,j)) out.push([key,j]); }   // lateral
  const ch = moveChainOf(owner), k = ch.indexOf(key);
  for (const dk of [-1,1]) {
    const nk = (k>=0) ? ch[k+dk] : null; if (!nk) continue;
    for (const dj of [-1,0,1]) { const j=i+dj; if (slotExists(nk,j)) out.push([nk,j]); } // straight + diagonal
  }
  return out;
}
function adjacentK(owner,k1,i1,k2,i2){ return adjCells(owner,k1,i1).some(([k,i]) => k===k2 && i===i2); }  // 16:15
```

**(R) Movement rule:** **one square in any direction** — sideways, forward, backward, or diagonal —
per move, and the enemy back row is enterable (the siege square). Diagonals are load-bearing: they
are how a creature in an even column reaches the center's odd lanes (`16:6-7`).

**Note:** the two move chains are exact reverses of each other, so the neighbour *set* is identical
for both owners. `adjacentK('you',…)` ≡ `adjacentK('foe',…)`. **The `owner` argument is redundant**
for adjacency; keep it only if you plan asymmetric movement later.

**(R) Move budget** (`16_movement.js:26,51-52`, `17:180,185`, `42:69`):

```
moveSpent(c) = c.moved && !(G.upkeep && !c.moved2 && !c.tapped)
```
i.e. one move per turn normally; during **upkeep only**, an untapped creature that has already moved
may move a **second** time, which sets `moved2 = true` **and** `tapped = true` (spending its whole
turn). A third move is impossible.

`doMove` (`16:46-57`) additionally requires the destination to be empty and calls `syncWorkers` after
(the move can change both rows' worker figures). It routes to `upkeepNext()` when `G.upkeep`.

### 9.1 Deployment slot search

```js
function freeDeploySlot(owner,which){                                    // 16:17-18
  const a = cellArr(owner,which); if (!a) return -1;
  return a.findIndex((x,i) => !x && !(which==='center' && !isLane(i)));
}
function aiPickDeploySlot(owner,which){                                  // 16:20-23
  const order = which==='center' ? [3,1,5]
              : which==='front'  ? [3,4,2,5,1,6,0]
              :                    [2,4,3,1,5,0,6];        // 'back'
  for (const i of order) if (i<SLOTS && !a[i] && slotExists(which,i)) return i;
  return freeDeploySlot(owner,which);
}
```
**⚠** the `center` preference order `[3,1,5]` lists a **lane** first (3 is a lane), but
`freeDeploySlot('…','center')` also only ever returns lanes. Structures reaching the center go
through `placeBuild`, not these helpers.

`firstEmptyCell(owner)` (`06_mana_workers.js:105-107`) is the token-placement search used by
Ward/Reap: scan `back`, then `front`, then the center **lanes only**; returns `{arr, i}` or `null`.

`removeUnitFromBoard(unit)` (`06:108-111`) scans all five row arrays by identity, then all six worker
pools; returns the owner tag of the array it was removed from (**the row's nominal owner for pools,
but the row array index for cells**) or `null`. Used by bounce/Undertow.

`buildingLoc(owner,unit)` (`07_structures.js:33-36`) finds `{key, i}` for a placed unit by scanning
`cellArr(owner, 'back'|'front'|'center')`.

### 9.2 (R) Deployment legality (where a card may enter the board)

| Destination | Creature summon | Structure build | Face-down `set` | Trap `settrap` |
|---|---|---|---|---|
| `youBack` / `youFront` (own two rows) | ✔ | ✔ | ✔ | ✔ |
| `center` lanes (1,3,5) | ✘ | ✘ | ✘ | ✘ |
| `center` flanks (0,2,4,6) | ✘ | ✔ | ✘ | ✘ |
| any enemy row | ✘ | ✘ | ✘ | ✘ |

Sources: `handDeployOK` (`13_input.js:43-48`), `centerSlotOK` (`01:7`), `place` (`13:181`),
`placeBuild` (`06:221-227`), MP validator (`42:78-80`). Hint text confirms the intent:
*"New cards can't deploy to the contested center — summon to your rows, then march forward"*
(`13:115`).

Additional gates:

* `placeRowOK(owner,which,def)` (`06_mana_workers.js:196`): a structure with **negative** `sup` may
  only be built where `rowWorkers(owner,which) + def.sup >= 0`.
* `hasPlacement` / `hasEmptyDeploy` (`06:194,197`) drive the Build menu's enabled state.
* **Play-on-top:** a `summon` or `build` may target an **occupied** own cell in `youBack`/`youFront`
  if the occupant has `bank > 0`; the occupant is destroyed (to grave), its bank pays part of the
  cost, surplus carries to the newcomer (`13_input.js:185-205`, `42:83-106`). Not allowed on `cc`
  cards, not allowed in the center.

---

## 10. Combat geometry (row intervals) — the part owned by this subsystem

Full combat resolution is another subsystem's spec; what belongs here is **which cells can
participate**.

```js
function rowsCrossedInto(aIdx,tIdx){                                     // 15_combat.js:7-11
  const o=[];
  if (aIdx === tIdx) return o;                    // same row = point-blank duel, uninterposable
  const step = tIdx > aIdx ? 1 : -1;
  for (let r = aIdx+step; r !== tIdx+step; r += step)
    if (r >= 0 && r < ROWS.length) o.push(ROWS[r]);   // virtual wall indices are clipped out
  return o;
}
```

**(R)** The rows an attack crosses into = every row past the attacker's, **up to and including** the
target's row. Same row ⇒ empty list ⇒ no interposition possible.

Worked examples (ROWS indices: foeBack 0, foeFront 1, center 2, youFront 3, youBack 4):

| Attacker | Target | aIdx | tIdx | crossed rows |
|---|---|---|---|---|
| youFront | foeFront creature | 3 | 1 | center, foeFront |
| youFront | **foe castle wall** | 3 | −1 | center, foeFront, foeBack |
| youBack | **foe castle wall** | 4 | −1 | youFront, center, foeFront, foeBack |
| foeBack | **your castle wall** | 0 | 5 | foeFront, center, youFront, youBack |
| center | center creature | 2 | 2 | *(none — point-blank)* |
| foeFront | your worker stack in `youFront` | 1 | 3 | center, youFront |

```js
function untappedInterceptors(key, attackerOwner){                       // 15:15-20
  const out=[];
  rowArr(key).forEach((c,i) => { if (c && c.kind==='creature' && !c.blocked && c.owner!==attackerOwner)
                                   out.push({key, i, c}); });
  minionsInRow(key).forEach(g => { if (g.owner!==attackerOwner && !g.c.tapped && !g.c.sick)
                                     out.push({key, c: g.c}); });        // NOTE: no `i`
  return out;
}
function eligibleInterceptors(attackerOwner, aIdx, tIdx){                // 15:21
  return rowsCrossedInto(aIdx,tIdx).flatMap(key => untappedInterceptors(key, attackerOwner));
}
```

**(R) Interception rules encoded here:**

1. **Columns are irrelevant.** Any defender in a crossed row qualifies (`15:13`).
2. A creature may block **even when tapped or summoning-sick**; the gate is the once-per-turn
   `blocked` flag (`15:14`, `16:29`).
3. Board-cell interceptors must be `kind === 'creature'`. **Structures cannot intercept**, despite
   Bulwark/Bastion's `eff:'wall'` description claiming they "screen the line"
   (`03_cards_creatures.js:58,68`). This is a real rules/flavour mismatch — flag it for design.
4. Worker-pool interceptors use `!tapped && !sick` (not `blocked`), so a worker stack can screen
   repeatedly within a turn but not after harvesting.
5. **Reference shape asymmetry:** cell interceptors carry `{key, i, c}`; worker interceptors carry
   `{key, c}` with **no index**. Downstream code copes via `r.c || unitAt(r.key, r.i)`
   (`16:117`, `17:340`, `42:234`). For MP, a worker ref is canonicalised to `{po, pw, pi}`
   (pool-owner, pool-which, pool-index) at `44_mp_lobby.js:47-48` and resolved at
   `42_mp_apply.js:7-8`.

**Port instruction:** define a discriminated `UnitRef` — `CellRef(RowIndex,Column)` vs
`PoolRef(Owner,Zone,Index)` — rather than the JS's duck-typed object.

Related targeting maps:

```js
const WELL2ROW = { wellFoeBack:'foeBack', wellFoeFront:'foeFront', wellCenter:'center',
                   wellYouFront:'youFront', wellYouBack:'youBack' };     // 15_combat.js:172  (P/W)
const FX_STRIP = <inverse of WELL2ROW>;                                  // 22_fx_wrappers.js:2  (P)
```
These translate the legacy worker-strip DOM element ids to row keys. **(P)** — drop in Unity; keep
only the `(owner, zone)` pair as the worker-stack target identity.

---

## 11. Phases and turn-boundary state (needed to define "state")

```js
const PHASE_ORDER = ['upkeep','draw','action','end'];                     // 17_turns_ai.js:43
function setPhase(p){ G.phase = p; G.upkeep = (p === 'upkeep'); }         // 17:45
function acting(){ return G.turn==='you' && !G.busy && !G.over && G.phase==='action'; }  // 17:46
```
`'combat'` is **not** a stored phase; it is a display state shown while `G.atk` or `G.decls` is
non-empty during `action` (`shownPhase`, `17:48`). **(P)**

`startTurn(owner)` (`17_turns_ai.js:49-71`) — the authoritative turn-boundary mutation:

```
 1. G.turnNo++                              // ply counter
 2. G.turn = owner
 3. G.cardMenu = null; G.moveMana = null; G.decls = []
 4. P.firstExtract = true                                   [inert]
 5. P.upaid = {back:0, front:0, center:0, raid:0}           // keep payments expire
 6. for every unit of `owner` on the board with kind==='creature':
        sick=false; tapped=false; moved=false; moved2=false; paid=false; blocked=false; _dis=0
 7. chrysalisUpkeep(owner)     // counters grow / hatch; hatched or growing units are re-set sick
 8. overchargeUpkeep(owner)    // oc = min(3, oc+1)
 9. buildingUpkeep(owner)      // mana / damage / revive effects (NOT 'villager')
10. cleanup()                  // sweep anything a damage tower killed
11. syncWorkers(owner)         // re-derive worker pools from the cards now in each row
12. readyWorkers(owner)        // un-sick, un-tap this turn's workers
13. branch:
      owner==='you'  -> setPhase('upkeep'); pop the settle menu on the first offender
      MP + started   -> setPhase('upkeep')  (remote player drives via intents)
      else (AI)      -> drawCard('foe'); aiFixDeficit('foe'); readyWorkers('foe')
```

**(R) Step 6 is board-only.** It iterates `ownUnits(owner)`, which walks the five row arrays — worker
pools are handled separately by step 12. Note also that `sick`/`tapped` clear only for the player
**whose turn is starting**, so the opponent's units keep their flags.

`endTurn` (`17:222-243`): refuses to advance from `draw`/`upkeep`; refuses while `CMB.hasDecls()`;
clears `sel/atk/moveFrom/moveMana`; `setPhase('end')`; `endPhaseEffects`; `endTurnDrain` (mana above
the vault cap evaporates, `17:32-41`); then hands off.

---

## 12. Game setup — `startGame` (`09_game_start.js:1-19`)

**(R)** Exact ordered algorithm:

```
startGame(youId, foeId, youDeck?, foeDeck?):
 1. cy = CCS[youId]; cf = CCS[foeId]
 2. Object.assign(G.P.you, {
        color: cy.colors[0], cc: youId, life: cy.hp, mana: 0, cmana: zc(),
        hand: [], deck: [], grave: [],
        front: Array(7).fill(null), back: Array(7).fill(null),
        min: {back:[], front:[], center:[]},
        firstExtract: true, villagerUsed: false,
        upaid: {back:0, front:0, center:0, raid:0} })
 3. same for G.P.foe with cf / foeId
 4. G.turn='you'; G.busy=false; G.over=false; G.turnNo=1;
    G.sel=null; G.atk=[]; G.decls=[]; G.moveFrom=null; G.moveMana=null; G.cardMenu=null;
    G.phase='upkeep'; G.upkeep=true
 5. G.center = Array(7).fill(null)                       // NEW array — old references die
 6. syncWorkers('you'); syncWorkers('foe')               // pools = CCS.wk workers in each back row
 7. readyWorkers('you'); readyWorkers('foe')             // opening workforce is settled + harvest-ready on turn 1
 8. G.P.you.deck = youDeck ?? deckOf(cy.colors)
    G.P.foe.deck = foeDeck ?? deckOf(cf.colors)
 9. dealOpening('you'); dealOpening('foe')               // hand=[]; draw 4 (11_deck_builder.js:247-249)
10. applyCharacterUI()          (P)
11. buildBattlefield(cy.colors[0], cf.colors[0])   (P)
12. hideAllScreens(); clear the log; print the opening log line   (P)
13. setPhase('upkeep'); upkeepHint(); render()
```

**(R) Derived initial state:** the board is completely empty (35 nulls). The only non-empty state is
each player's `min.back` pool, whose size is `max(0, rowWorkers(owner,'back'))` = `CCS[cc].wk`
(2 or 3, or the rounded average for a dual commander), because no structures or creatures exist yet.
`G.P.you.min.front` and `.center` are empty. Both players' opening workers are **not sick and not
tapped** (step 7), so turn 1's Upkeep can harvest immediately for ◆(wk).

**No command centre card is placed** (`09:7-8`).

Also note **`G.turnNo` starts at 1 and `startTurn` is not called for the opening turn** — the player's
first turn is entered directly by `startGame` at `phase='upkeep'`. `startTurn('foe')` is what pushes
`turnNo` to 2.

Deck construction (`deckOf`, `06_mana_workers.js:26-35`): for each of the commander's `n` colours,
push `round(28/n)` random creatures from that colour's pool and `round(12/n)` random neutral spells;
top up with the first colour's pool until length reaches `DECK_SIZE` (**40**); Fisher–Yates shuffle;
slice to 40. `expandDeck` (`06:81-89`) is the custom-deck path. `MAX_COPIES = 3`.

`dealOpening(o)` = clear hand, draw **4** (`11_deck_builder.js:247-249`).
`drawCard(o)` pops from the **end** of `deck` (`11:250`).

### 12.1 `buildBattlefield` — **(P) entirely presentation**

`08_battlefield.js` is 100% view code and contributes **zero rules**. It injects a full-bleed scenery
`<div>` layer as the first children of `.matmain`: element-tinted territories, a scorched frontier,
three lane paths, and ~20 seeded props. Everything worth carrying to Unity:

* **Lane paths** are drawn at column offsets `-2, 0, +2` from centre (`08:24`), i.e. under columns
  **1, 3, 5** — a visual echo of `CENTER_LANES`. If a Unity battlefield decal is built, key it off
  `CENTER_LANES`, not off a hard-coded `[-2,0,2]`.
* Territory colours come from `ELEMENTS[el].bg[0..1]` and `.color`; prop tints from `.deep` and
  `.accent` (`08:18-22,36,40,49`).
* Prop counts: 6 margin rocks/tufts, 4 seam tufts, 3 fallen banners on the frontier, 4 braziers at
  `[4.5,10] [95.5,10] [4.5,91] [95.5,91]` percent, 2 tents + 2 stake lines, 8 ambient motes
  (4 per half) (`08:33-52`).
* `bfRng(seed)` is a deterministic PRNG (`08:7-8`) — but it is **seeded with `Math.random()`**
  (`08:29`), so scenery is *not* reproducible. If you want deterministic scenery in Unity, seed it
  from a match seed.
* It is idempotent (removes `#battlefield`/`#battlefieldProps` first) and survives re-renders because
  `renderRow` only clears the row `<div>`s (`08:4-6`).
* `08:54-55` reads the live computed `column-gap` of `#center` to align the lane decals — a pure
  **(W)** CSS-measurement workaround.

---

## 13. Serialization: what must persist for save / netcode

The existing multiplayer layer already defines a canonical snapshot. Treat it as the *minimum*
serializable set.

```js
function pSnap(o){ const P = G.P[o];                                     // 41_mp_sync.js:29-31
  return { color, cc, life, mana, cmana, hand, deck, grave,
           front, back, min, firstExtract, villagerUsed, upaid }; }
function snapshot(){                                                     // 41_mp_sync.js:32-36
  const s = deepCopy({ turn: G.turn, over: G.over, turnNo: G.turnNo, phase: G.phase,
                       uid,                          // the GLOBAL id counter
                       center: G.center,
                       P: { you: pSnap('you'), foe: pSnap('foe') } });
  strip(s);   // null out every `.art` except on tokens (rebuilt locally)
  return s;
}
```

### 13.1 Classification table — global vs per-player vs transient

| Datum | Scope | Must serialize | Notes |
|---|---|---|---|
| `uid` counter | **global** | **YES** | `41:33,42` — adoption raises the local counter to `max(local, remote)` so new ids never collide |
| `G.turn` | global | yes | |
| `G.turnNo` | global | yes | ply counter |
| `G.phase` | global | yes | `G.upkeep` is derived from it — do not store |
| `G.over` | global | yes | |
| `G.center[7]` | global (shared row) | yes | |
| `G.busy` | global | **no** | transient async latch; explicitly reset to `false` on adopt (`41:51`) |
| `G.sel`, `G.atk`, `G.moveFrom`, `G.moveMana`, `G.cardMenu`, `G.build`, `G.minSel` | global, **UI-local** | **no** | all nulled on adopt (`41:51`) |
| `G.decls` | global | **NOT serialized today** | see risk §15.2 |
| `P.color`, `P.cc` | per-player | yes | |
| `P.life`, `P.mana` | per-player | yes | |
| `P.cmana` | per-player | serialized but inert | drop in the port |
| `P.hand`, `P.deck`, `P.grave` | per-player | yes | **hidden-information hazard**: the current host snapshot sends *both* full hands and decks to the guest |
| `P.front[7]`, `P.back[7]` | **positional row**, stored per-player | yes | see §4.1 |
| `P.min.{back,front,center}` | per-player | yes | worker pools |
| `P.upaid` | per-player | yes | |
| `P.firstExtract`, `P.villagerUsed` | per-player | serialized but inert | drop |
| unit `.art` | per-object | **no** | stripped and rebuilt from the registry (`41:10-27`) |
| unit `._dis` | per-object | transient | present in the object so it *is* serialized; cleared each turn |

### 13.2 Perspective mirroring (`MPMAP`)

```js
MPMAP.k: youBack<->foeBack, youFront<->foeFront, center->center,
         wellYouBack<->wellFoeBack, wellYouFront<->wellFoeFront, wellCenter->wellCenter  // 41:3-4
```

**Slot indices map by identity — columns are NOT mirrored** (`41_mp_sync.js:1`). Guest adoption
(`41:37-59`) does a wholesale swap: `G.P.you ← S.P.foe`, `G.P.foe ← S.P.you`, `G.center ← S.center`
with every center occupant's `owner` flipped, then re-stamps `owner` on every unit in each side's
`front`/`back`/`min` to match the array it now lives in.

**⚠ Port risk:** that re-stamp (`41:46-48`) assumes each side's `front`/`back` arrays contain only
that side's units. It is **wrong for raiders** — a foe creature standing in `youFront` will have its
`owner` rewritten to the array's nominal owner on every adopt, silently converting a raider into a
defender. Do not replicate this. In C#, mirror by transforming `RowIndex` (`r -> 4-r`) and flipping
each object's `Owner` field independently.

---

## 14. Presentation, and browser workarounds to drop

### 14.1 Board layout constants **(P)**

* `--ch: clamp(50px, (100dvh - 70px)/5.4, 196px)`, `--cw: --ch * 0.74`
  (`src/styles/00_base.css:39`) — cell aspect ratio **0.74 : 1** (w : h).
* Rows are flex containers, 7 cells centred, gap `clamp(3px,.7vw,9px)` (`01_board.css:12,104`).
* Vertical proportions: each 2-row side stack is `flex:2`, the centre wrap is `flex:1`
  (`01_board.css:9,18`) ⇒ **five equal-height rows**.
* DOM ids equal the row keys exactly: `#foeBack #foeFront #center #youFront #youBack`
  (`index.html:35,36,42,47,48`). `12_render.js` calls `$(key)` directly, so key and element id are
  one namespace.
* Cells carry `data-key`, `data-owner`, `data-which`, `data-slot` for drag-drop
  (`12_render.js:293,339`).
* Board angle: exactly two options, `board-topdown` and `board-extreme` (labelled "Tilted"), stored
  in `localStorage['srd.angle']` (`22_fx_wrappers.js:277-284`).

### 14.2 Browser workarounds **(W)** — delete in Unity

| Workaround | Location | Why it exists |
|---|---|---|
| `snapLegalCell(x,y)` — 44px projected-rect snap to the nearest lit cell | `12_render.js:383-392` | the tilted 3D CSS transform makes `elementFromPoint` unreliable; forgiving thumb taps |
| `onCellRouted` — snap only for taps on *empty, non-legal* cells | `12_render.js:394-405` | preserves intentional card taps |
| `cellUnder(x,y)` — same projected-rect fallback for drops | `31_ui_shell.js:147-162` | same 3D transform problem |
| pointer capture moved to `documentElement` | `31_ui_shell.js:143-145` | re-render removes the pointerdown target, cancelling the drag on touch |
| drag threshold 7px (mouse) / 15px (touch) | `31_ui_shell.js:139,279` | separates tap from drag |
| `body.placing` ghosts unselected hand cards | `15_combat.js:210-212` | the hand strip overlaps the near rows on phones |
| `body.targeting` ghosts `#turnLabel`, raises the foe cmd cluster, pads the ♥ hit area | `15_combat.js:206-209` | make the enemy life pool a reachable thumb target |
| `no board-drag while `G.atk` is held` | `31_ui_shell.js:183-187` | a rolled tap must not become `startMove` and wipe the attack group |
| RTS marquee (mouse/pen only, skipped on touch) | `31_ui_shell.js:191-194,196-223` | PC group-select |
| `getComputedStyle(...).columnGap` read to align lane decals | `08_battlefield.js:54-55` | CSS measurement |

### 14.3 Dead render paths **(P)**

`renderMinions` (`12_render.js:75-93`), `workerTokEl` (`12:94-107`) and `workerChipRow`
(`12:214-235`) are **never called** — `render()` (`12:6-30`) calls only `renderRow` ×4,
`renderCenter`, `renderHand`, `renderFoeHand`, `renderCmdZone` ×2, `renderWalls`, `placeCardMenu`.
The live worker readout is `workerColumn()` (`12:238-262`, five board-aligned rows in the left
tower: *Enemy Base · Raid · Center · Front · Base*) plus `rowFloatChips`/`wkSlotEl`
(`12:189-211`, floating chips shown only when actionable).

---

## 15. Known discrepancies, dead code, and port risks

### 15.1 Dead / vestigial (safe to delete)
`C`, `colReach`, `ownRows`, `canDeploy`, `MINE`, `mkCC`, `findCC`, `G.minSel`, `G.powerMode`,
`G.deficit`, `P.cmana`, `P.firstExtract`, `P.villagerUsed`, `workerCap`/`structSupport`/
`monsterUpkeep`/`canTrain`/`enforceCap`/`trainVillager`, `renderMinions`/`workerTokEl`/
`workerChipRow`, `DIVINE` roster, `extractColors`/`colorNeed`/`doExtract`/`canExtract`
(returns `false`, `12:408`), `WELL2ROW`/`FX_STRIP` (DOM-id only).

### 15.2 Real bugs / inconsistencies found while extracting

1. **`zoneKey('…','raid')` returns one row, `zoneKeys` returns two.** (`05:59-60`) Currently benign;
   fix by making the plural form canonical.
2. **MP guest adopt re-stamps `owner` from the array's nominal owner** (`41:46-48`), destroying
   raider ownership. Must not be replicated.
3. **`G.decls` is not serialized** (`41:33`). Combat-v3 declarations mutate units (`A.tapped = true`,
   `blocker.blocked = true`) at declaration time, so a snapshot taken mid-declaration shows tapped
   attackers and blocked blockers with no declarations to explain them. MP sidesteps this by running
   the legacy single-shot attack path (`15:216-224`), but a future netcode port must serialize
   declarations or forbid snapshots inside the declaration window.
4. **`aiMoveCreature` FX wrapper passes the wrong argument.** The real function takes a *global row
   key* (`17:178`), but the wrapper computes the source cell with `zoneKey(owner, fromZ)`
   (`22_fx_wrappers.js:146`), which returns garbage for a global key. Presentation-only; the move
   itself is correct.
5. **`eff:'wall'` structures (Bulwark, Bastion) claim to "screen the line" but cannot intercept**
   — `untappedInterceptors` filters `kind === 'creature'` (`15:16`).
6. **`eff:'villager'` structures do nothing.** `buildingUpkeep` has no `villager` branch (`17:4-8`)
   and both villager structures have `val: 0`.
7. **`effA` vs `.a` inconsistency in Combat v3.** Attacker damage uses `effA` (Overcharge included)
   but retaliation damage uses raw `.a` (`15:279,297`).
8. **`c.a` used for interceptor power readout** in `askBlock` (`16:142-144`) while resolution uses
   `effA` — cosmetic mismatch.
9. **`toGrave` restores `h` to `maxh`.** Deliberate or not, it means the graveyard/Reliquary loop
   heals creatures (`07:69,71`).
10. **99-mana cap is applied on credit only**, in five separate places (`15:158`, `16:184`, `17:5`,
    `42:23,35`). Centralise it in the port.
11. **Side-row trap scan omits the `owner` check** (`14:35`, `30_resp.js:12`) while the center scan
    includes it (`14:38`). Safe today only because face-downs can only be set into one's own rows.

### 15.3 Open behaviours worth confirming with the designer

* Is `'raid'` meant to cover both enemy rows for the *label* as well as the arithmetic?
* Should `wall`-effect structures actually be able to intercept?
* Should `villager` structures train workers (restore `trainVillager`), or is the per-row derived
  model the final answer?
* Should the AI's `center` deploy preference `[3,1,5]` ever be reachable, given creatures cannot be
  summoned into the center?

---

## 16. Suggested C# types

All types below are **pure**: no `UnityEngine` references, `[Serializable]`, deterministic.

```csharp
// ---------- geometry ----------
public enum RowKey { FoeBack = 0, FoeFront = 1, Center = 2, YouFront = 3, YouBack = 4 }
public enum SlotName { Back, Front, Center }           // owner-relative "which"
public enum WorkerZone { Back, Front, Center, Raid }   // ZONES
public enum Side { You, Foe }                          // 'you' | 'foe'

public static class BoardGeometry {
    public const int Columns = 7;                      // SLOTS
    public const int Rows    = 5;                      // ROWS.Length
    public const int BaseColumn = 3;                   // BASE_COL (FX fallback only)
    public static readonly int[] CenterLanes = { 1, 3, 5 };
    public const int FoeWallRowIndex = -1;             // virtual
    public const int YouWallRowIndex = 5;              // virtual == Rows

    public static bool IsLane(int col);
    public static bool SlotExists(RowKey row, int col);          // 16:5
    public static bool CenterSlotOk(RowKey row, int col, bool isBuilding); // 01:7
    public static RowKey RowFor(Side owner, SlotName which);     // 05:17
    public static SlotName WhichOf(RowKey row);                  // 15_combat.js:3
    public static SlotName? WhichForKey(Side owner, RowKey row); // 05:39
    public static WorkerZone? ZoneForRow(Side owner, RowKey row);// 12:184
    public static IReadOnlyList<RowKey> RowsOfZone(Side owner, WorkerZone z); // 05:59 (canonical, plural)
    public static IReadOnlyList<RowKey> RowsCrossedInto(int aIdx, int tIdx);  // 15:7
    public static IEnumerable<CellRef> AdjacentCells(RowKey row, int col);    // 16:8 (owner-independent)
    public static bool AreAdjacent(CellRef a, CellRef b);
}

public readonly struct CellRef { public readonly RowKey Row; public readonly int Col; }
public readonly struct PoolRef { public readonly Side Owner; public readonly WorkerZone Zone; public readonly int Index; }
public abstract record UnitRef;                         // CellRef | PoolRef discriminated union

// ---------- elements ----------
public enum Element { Fire, Water, Earth, Wind, Forest, Electric, Light, Dark, Divine }
public sealed class ElementDef {                        // ScriptableObject-backed data, but pure in the core
    public Element Id; public string Name; public string Glyph;
    public string Color, Accent, Deep; public string[] Bg;      // presentation
    public int Hp; public int Wk; public string Lore;
    public bool IsMajor;                                        // false only for Divine
}

// ---------- board objects ----------
public enum UnitKind { Creature, Building, Charge, Trap }
public enum CreatureKeyword { None, Detonate, Undertow, Entrench, Ward, Reap, Chrysalis, Scour, Overcharge }
public enum StructureEffect { None, Mana, Villager, Damage, Vault, Wall, Revive }
public enum TrapTrigger { Summon, Attack }
public enum CardType { Creature, Building, Spell }

public abstract class BoardObject {
    public int Id;                 // uid++
    public Side Owner;             // THE authority on ownership; rows are positional
    public UnitKind Kind;
    public string Name;            // nm
    public Element? Color;
    public int Bank;               // stored mana
}

public sealed class CreatureInstance : BoardObject {
    public bool IsWorker, IsToken;
    public int Attack, Hp, MaxHp, Cost, Upkeep;                 // a, h, maxh, c, up
    public bool FirstStrike;                                    // fs
    public bool Sick, Tapped, Moved, MovedTwice, Paid, Blocked; // per-turn flags
    public int DischargeBonus;                                  // _dis (transient)
    public CreatureKeyword Keyword;
    public int Detonate, Ward, WardHp, Reap, Grow, Hatch, Counters, Overcharge; // det,ward,wardhp,reap,grow,hatch,cnt,oc
    public HatchForm Into;                                      // into
    public bool Entrench;
    public string Tribe, Subtype;
    public int EffectiveAttack => Attack + DischargeBonus;      // effA
}

public sealed class BuildingInstance : BoardObject {
    public int Hp, MaxHp, Cost;
    public StructureEffect Effect; public int Value;            // eff, val
    public int Support;                                         // sup — MAY BE NEGATIVE
    public string BuildId;                                      // bid
    public bool IsCommandCenter;                                // cc — dead today
}

public sealed class ChargeInstance : BoardObject {              // face-down creature or structure
    public SlotName SetIn;          // w
    public CardType ChargeType;     // ctype
    public CardTemplate Card;       // frozen snapshot
    public int Invested;            // inv
    public int SetTurn;
}

public sealed class TrapInstance : BoardObject {
    public SlotName SetIn; public CardTemplate Card; public int SetTurn;
    public bool IsArmed(int turnNo) => turnNo > SetTurn;
}

// ---------- state ----------
public sealed class PlayerState {
    public Element PrimaryColor;             // color
    public string CommanderId;               // cc  (key into CCS)
    public int Life, Mana;                   // mana capped at 99 on credit
    public List<HandCard> Hand;
    public List<CardTemplate> Deck;          // drawn from the END
    public List<GraveRecord> Grave;
    public BoardObject[] Front = new BoardObject[7];   // POSITIONAL row, not "my units"
    public BoardObject[] Back  = new BoardObject[7];
    public Dictionary<WorkerZone, List<CreatureInstance>> MinionPools; // Back/Front/Center only
    public Dictionary<WorkerZone, int> UpkeepPaid;                     // upaid — includes Raid
}

public enum TurnPhase { Upkeep, Draw, Action, End }

public sealed class GameState {                          // the C# `G`
    public int NextUid = 1;                              // MUST be serialized
    public Side Turn; public int TurnNumber;             // ply counter, starts at 1
    public TurnPhase Phase;                              // IsUpkeep => Phase == Upkeep
    public bool IsOver;
    public BoardObject[] Center = new BoardObject[7];    // SHARED contested row
    public PlayerState You, Foe;
    public List<AttackDeclaration> Declarations;         // G.decls — serialize it (see risk 15.2)
    // NOT part of serialized state: Busy, Selection, AttackGroup, MoveFrom, MoveMana,
    // CardMenu, PendingBuild  -> keep these in a separate UiIntentState object.
}

public sealed class AttackDeclaration {
    public CellRef Attacker; public DeclarationKind Kind;   // Unit | Wall | Workers
    public CellRef? Target; public WorkerZone? TargetPool;
    public List<UnitRef> Blockers;
}
```

**Structural recommendations for the port**

1. Store the board as `BoardObject[5][7]` indexed by `RowKey`, and expose
   `Span<BoardObject> RowOf(RowKey)`; provide `PlayerState.Front/Back` as *views* into it if you want
   to keep the legacy addressing, or drop the per-player row fields entirely and add
   `RowFor(owner, which)`.
2. Make `UnitRef` a real discriminated union so worker refs can never be confused with cell refs.
3. Centralise the mana-credit cap and the `turnNo`/ply semantics.
4. Keep `NextUid` inside `GameState` (not a static) so a save/load round-trip is exact.
5. Split `GameState` (deterministic, serializable, command-driven) from `UiState`
   (`Busy/Selection/AttackGroup/MoveFrom/MoveMana/CardMenu/PendingBuild/hints`) — the JS conflates
   them inside `G` and the MP layer already has to null all of them on every adopt (`41:51`).
