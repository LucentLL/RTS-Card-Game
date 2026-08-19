# 04 — Movement, Placement, Summoning, Set & Flip

**Subsystem spec extracted from the JS source for the Unity 6 / C# port.**
Source of truth: `src/js/*.js` (29 classic scripts, one shared global scope). Every rule below was read
out of the running code, not from design notes. Citations are `file:line` against the repo at
commit `8b90375` (branch `main`).

> **Reading contract for the implementer.** This document is the *only* specification for this
> subsystem. Where the JS has a bug, an inconsistency, or dead code, it is called out explicitly
> under **[BUG]**, **[INCONSISTENT]**, or **[DEAD]** rather than silently normalised — you decide
> what to keep. Where behaviour exists only to work around the browser/DOM, it is marked
> **[DOM-ONLY]** and must **not** be ported into the rules core.

---

## 0. Table of contents

1. [Scope](#1-scope)
2. [Board geometry — the authoritative model](#2-board-geometry--the-authoritative-model)
3. [State: the objects movement/placement touches](#3-state-the-objects-movementplacement-touches)
4. [Movement — adjacency](#4-movement--adjacency)
5. [Movement — the complete legality predicate](#5-movement--the-complete-legality-predicate)
6. [Movement — execution and side effects](#6-movement--execution-and-side-effects)
7. [The move budget: once per turn, twice at upkeep](#7-the-move-budget-once-per-turn-twice-at-upkeep)
8. [Congestion: columns, walls, and impassable rows](#8-congestion-columns-walls-and-impassable-rows)
9. [Deployment — where new cards may enter](#9-deployment--where-new-cards-may-enter)
10. [Placement — hand play modes and the full `place()` algorithm](#10-placement--hand-play-modes-and-the-full-place-algorithm)
11. [Structure building from the build menu](#11-structure-building-from-the-build-menu)
12. [Structure upgrades in place](#12-structure-upgrades-in-place)
13. [Set face-down (charge) and set trap](#13-set-face-down-charge-and-set-trap)
14. [Flip rules](#14-flip-rules)
15. [Summoning sickness — full lifecycle](#15-summoning-sickness--full-lifecycle)
16. [The `laid` standee pose (`canActNow`)](#16-the-laid-standee-pose-canactnow)
17. [The no-zero-cost-card rule](#17-the-no-zero-cost-card-rule)
18. [AI movement and AI deployment](#18-ai-movement-and-ai-deployment)
19. [Multiplayer re-validation (host-authoritative)](#19-multiplayer-re-validation-host-authoritative)
20. [FX monkey-patch layer — what it does and does not change](#20-fx-monkey-patch-layer--what-it-does-and-does-not-change)
21. [Presentation / DOM workarounds that must NOT enter the rules core](#21-presentation--dom-workarounds-that-must-not-enter-the-rules-core)
22. [Dead code inventory](#22-dead-code-inventory)
23. [Suggested C# types](#23-suggested-c-types)
24. [Test vectors](#24-test-vectors)
25. [Open questions](#25-open-questions)

---

## 1. Scope

This spec covers everything that puts a unit on a cell or moves it between cells:

* one-square any-direction movement (`adjCells`, `adjacentK`, `doMove`),
* the geometry that makes diagonals load-bearing (center lanes),
* deployment rows and slot legality (`handDeployOK`, `centerSlotOK`, `placeRowOK`),
* summoning (`place` mode `summon`), building (`place` mode `build`, `placeBuild`, `upgradeStruct`),
* setting face-down (`place` mode `set` / `settrap`) and flipping (`flip`, charge panel, `provokeFaceDown`),
* summoning-sickness application and clearing,
* the `laid` standee predicate (`canActNow`) which is *derived* from move/attack legality.

It does **not** cover combat resolution, interception, retaliation, worker economy maths, or upkeep
settle payments beyond the points where they intersect movement (worker resync after a move, the
upkeep second move).

---

## 2. Board geometry — the authoritative model

### 2.1 Rows

`src/js/05_board_state.js:4`

```js
const ROWS=['foeBack','foeFront','center','youFront','youBack'];
```

Rows are listed **top → bottom** as drawn. Row index = index into `ROWS` (`rowIdx`,
`05_board_state.js:13`). Distance between rows = `|rowIdx difference|`.

| idx | key         | display name (`rowName`, `05_board_state.js:19`) | belongs to | 7 slots? |
|-----|-------------|--------------------------------------------------|------------|----------|
| 0   | `foeBack`   | "enemy base"                                     | foe (home) | yes      |
| 1   | `foeFront`  | "enemy front"                                    | foe (home) | yes      |
| 2   | `center`    | "the contested center"                           | shared     | **no — 3 usable** |
| 3   | `youFront`  | "your front line"                                | you (home) | yes      |
| 4   | `youBack`   | "your base"                                      | you (home) | yes      |

Two **virtual wall rows** exist for combat only and have **no slots**: index `-1` (beyond `foeBack`,
the foe's castle wall) and index `ROWS.length === 5` (beyond `youBack`, your castle wall).
`16_movement.js:94`, `15_combat.js:6-11`. **No unit can ever occupy a wall row** — movement never
produces those indices because `moveChainOf` only contains the five real rows.

### 2.2 Columns / slots

`src/js/01_core_defs.js:1-3,7`

```js
const C=7, SLOTS=7;
const CENTER_LANES=[1,3,5];
const isLane=i=>CENTER_LANES.includes(i);
function centerSlotOK(which,slot,isBld){ return which!=='center' || (isBld ? !isLane(slot) : isLane(slot)); }
```

* Every row is a fixed array of **7** slots, indices `0..6`.
* On the four side rows all 7 slots are real, standable cells.
* On the **center** row only columns **1, 3, 5** are *lanes* (creature-standable). Columns
  **0, 2, 4, 6** are *flanking ground* — structures may be **built** there but **no creature may ever
  stand or move there** (`slotExists` rejects them, §4.1).

`centerSlotOK(which, slot, isBuilding)`:

| which     | isBuilding | slot on lane (1/3/5) | slot on flank (0/2/4/6) |
|-----------|-----------|-----------------------|--------------------------|
| back/front| either    | OK                    | OK                       |
| center    | true      | **rejected**          | OK                       |
| center    | false     | OK                    | **rejected**             |

Total real, creature-standable cells on the board: `4 × 7 + 3 = 31`.
Total placeable cells including center flanks (structures): `4 × 7 + 7 = 35`.

`BASE_COL = 3` (`01_core_defs.js:4`) is used **only** as the default FX column when aiming at the
enemy wall (`12_render.js:331`). It is not a rule. `colReach()` (`01_core_defs.js:5`) is **[DEAD]** —
never called anywhere in the repo. Columns have **zero** effect on combat reach.

### 2.3 Storage — rows are board rows, not ownership zones

`05_board_state.js:5-12,21`

```js
function rowArr(key){ center→G.center, foeBack→G.P.foe.back, foeFront→G.P.foe.front,
                      youFront→G.P.you.front, youBack→G.P.you.back }
function cellArr(owner,which){ return which==='center'?G.center:G.P[owner][which]; }
```

**Critical for the port:** `G.P.you.front` is *the board row physically in front of you*, **not** "your
units". A foe creature that marched into your front row is stored in `G.P.you.front[i]` with
`owner === 'foe'`. The codebase repeats the warning "fronts are contested — attribute by the unit's
own tag" (`05_board_state.js:46`, `16_movement.js:199`). Ownership is **always** read from
`unit.owner`, never inferred from which array holds it.

The center array `G.center` holds **both** sides' units simultaneously.

`whichOf(key)` (`15_combat.js:3`) maps a global row key to the owner-relative slot name used by
`cellArr`: `center → 'center'`, `*Front → 'front'`, `*Back → 'back'`.
`rowKeyFor(owner,which)` (`05_board_state.js:17`) is the inverse.

---

## 3. State: the objects movement/placement touches

### 3.1 Cell occupant kinds

A cell holds `null` or exactly one object with a `kind` discriminator:

| `kind`       | created by | movable? | notes |
|--------------|-----------|----------|-------|
| `'creature'` | `mkCre` (`06_mana_workers.js:90`), `mkToken` (`:114`) | **yes** | the only movable kind |
| `'building'` | `mkBld` (`06_mana_workers.js:94`) | no | "Structures hold the base — they don't move or fight" (`13_input.js:134`) |
| `'charge'`   | `place()` mode `set` (`13_input.js:233`) | no | face-down creature *or* building, accumulating `inv` |
| `'trap'`     | `place()` mode `settrap` (`13_input.js:224`) | no | face-down spell with a `trigger` |

Workers/minions are **not** cell occupants. They live in per-row pools `G.P[owner].min[{back,front,center}]`
(`05_board_state.js:27`) and are explicitly **"not trained and do not move"** (`15_combat.js:169`
inspect text; `05_board_state.js:51-53`). They are derived each turn from structure support minus
monster upkeep in that row (`rowWorkers`, `05_board_state.js:61-68`).

### 3.2 Creature fields that gate movement

From `mkCre` (`06_mana_workers.js:90-92`) plus fields added later:

| field      | init | meaning | cleared |
|-----------|------|---------|---------|
| `owner`   | arg  | `'you'` / `'foe'` | never |
| `kind`    | `'creature'` | | never |
| `worker`  | bool | worker token (pool-only in practice) | never |
| `sick`    | `false`, set `true` on summon/flip/token/hatch | summoning sick — **cannot attack**, **can still move**, **can still block** | owner's `startTurn` (`17_turns_ai.js:53`) |
| `tapped`  | `false` | has acted (attacked / blocked / second-moved) — cannot attack; **can still move** | owner's `startTurn` |
| `moved`   | `false` | used its move this turn | owner's `startTurn` |
| `moved2`  | *(absent)* | used the upkeep second move | owner's `startTurn` (set to `false`) |
| `blocked` | `false` | already interposed this turn — gates blocking once per turn | owner's `startTurn` |
| `paid`    | *(absent)* | paid its upkeep keep this turn | owner's `startTurn` |
| `bank`    | `0`  | stored ◆ riding on the card | spent when played over |
| `entrench`| card | immune to bounce/Undertow — **does NOT prevent voluntary movement** (§5.6) |

`startTurn` reset, `17_turns_ai.js:53`:
```js
ownUnits(owner).forEach(o=>{if(o.kind==='creature'){o.sick=false;o.tapped=false;o.moved=false;
                            o.moved2=false;o.paid=false;o.blocked=false;o._dis=0;}});
```
`ownUnits(owner)` (`05_board_state.js:46`) sweeps **all five rows** and filters on `o.owner===owner`,
so a creature stranded deep in enemy territory still refreshes on its own controller's turn.

### 3.3 Global interaction state (`G`)

`04_cards_leaders.js:214-223`. Movement/placement read or write:

| field | type | meaning |
|-------|------|---------|
| `G.turn` | `'you'` \| `'foe'` | whose turn |
| `G.busy` | bool | AI/animation latch — blocks all player input |
| `G.over` | bool | game finished |
| `G.phase` | `'upkeep'` \| `'draw'` \| `'action'` \| `'end'` | `17_turns_ai.js:41` |
| `G.upkeep` | bool | mirror of `phase==='upkeep'` (`setPhase`, `17_turns_ai.js:45`) |
| `G.turnNo` | int | increments **every** `startTurn`, i.e. once per player-turn |
| `G.moveFrom` | `{k,i}` \| null | the move currently being aimed |
| `G.sel` | `{kind:'hand',idx,mode}` \| null | held hand card + chosen play mode |
| `G.build` | struct def \| null | structure chosen in the build menu awaiting a slot |
| `G.atk` | `[{k,i}]` | selected attack group (movement clears it) |
| `G.moveMana` | `{k,i}` \| null | banked-◆ transfer being aimed |
| `G.cardMenu` | `{k,i,html}` \| null | floating action menu (**[DOM-ONLY]**) |

`acting()` (`17_turns_ai.js:46`) `= G.turn==='you' && !G.busy && !G.over && G.phase==='action'`.

---

## 4. Movement — adjacency

### 4.1 `slotExists` — is `(row, col)` a real standable cell?

`16_movement.js:5`
```js
function slotExists(w,i){ return i>=0 && i<SLOTS && (w!=='center'||isLane(i)); }
```
Works for both owner-relative names (`'center'`) and global keys (`'center'`) — the only name it
special-cases is the literal string `center`, which is identical in both spaces.

### 4.2 `moveChainOf` — the row ordering used for "one row forward/back"

`16_movement.js:3`
```js
function moveChainOf(owner){ return owner==='you'
  ? ['youBack','youFront','center','foeFront','foeBack']
  : ['foeBack','foeFront','center','youFront','youBack']; }
```

`moveChainOf('foe')` **is** `ROWS`; `moveChainOf('you')` is `ROWS` reversed.

> **KEY INSIGHT FOR THE PORT.** Because one list is the reverse of the other, the *set* of adjacent
> rows is identical for both owners. **Adjacency is owner-agnostic.** The `owner` argument to
> `adjCells` / `adjacentK` has **no effect on legality whatsoever** — it exists only because the
> original code walked a per-owner chain. In C# you can (and should) drop the owner parameter and
> use `|rowIndex - rowIndex| ≤ 1` over `ROWS` directly. Verified: `moveChainOf('you').indexOf('center')===2`
> with neighbours `youFront`/`foeFront`; `moveChainOf('foe').indexOf('center')===2` with neighbours
> `foeFront`/`youFront` — same unordered pair.

### 4.3 `adjCells` — the one-square neighbourhood

`16_movement.js:8-14`
```js
function adjCells(owner,key,i){ const out=[];
  for(const dj of [-1,1]){ const j=i+dj; if(slotExists(key,j)) out.push([key,j]); }   // lateral
  const ch=moveChainOf(owner), k=ch.indexOf(key);
  for(const dk of [-1,1]){ const nk=(k>=0)?ch[k+dk]:null; if(!nk)continue;
    for(const dj of [-1,0,1]){ const j=i+dj; if(slotExists(nk,j)) out.push([nk,j]); } }
  return out;
}
function adjacentK(owner,k1,i1,k2,i2){ return adjCells(owner,k1,i1).some(([k,i])=>k===k2&&i===i2); }
```

Semantics: **exactly one square in any of the 8 compass directions**, where "one row" means one step
along the row chain and "one column" means ±1 slot index — *and the destination must be a real slot*.
The source cell itself is never emitted (lateral uses `dj=±1`; row steps use a different row).

Equivalent closed form (use this in C#):

```
Adjacent(from, to)  ⇔
      IsRealSlot(from) ∧ IsRealSlot(to)
    ∧ |RowIndex(to) − RowIndex(from)| ≤ 1
    ∧ |to.Col − from.Col| ≤ 1
    ∧ (from ≠ to)
```

Where `IsRealSlot(r,c) ⇔ 0 ≤ c ≤ 6 ∧ (r ≠ Center ∨ c ∈ {1,3,5})`.

### 4.4 Adjacent row pairs (exhaustive)

| pair | adjacent? | note |
|------|-----------|------|
| `foeBack` ↔ `foeFront` | **yes** | |
| `foeFront` ↔ `center`  | **yes** | |
| `center` ↔ `youFront`  | **yes** | |
| `youFront` ↔ `youBack` | **yes** | |
| `foeFront` ↔ `youFront`| **no**  | the center must be crossed |
| `foeBack` ↔ `center`   | **no**  | |
| any row ↔ itself       | lateral ±1 only |
| any row ↔ a wall row   | **no**  | walls are not in the chain |

There is **no** "forward only" restriction. A creature may move sideways, forward, backward, or
diagonally, in either direction, on any turn — including retreating out of the enemy back row.

### 4.5 Diagonals are load-bearing (why they exist)

Because center lanes are at 1/3/5 and side-row slots are at 0..6, a creature standing on an **even**
column of `youFront`/`foeFront` has **no** straight-ahead center cell. Its only entry into the center
is a diagonal. Concretely:

| from `youFront` col | center destinations reachable in one step |
|---------------------|--------------------------------------------|
| 0 | `center[1]` (diagonal) |
| 1 | `center[1]` (straight) |
| 2 | `center[1]`, `center[3]` (both diagonal) |
| 3 | `center[3]` (straight) |
| 4 | `center[3]`, `center[5]` (both diagonal) |
| 5 | `center[5]` (straight) |
| 6 | `center[5]` (diagonal) |

Symmetrically for `foeFront`. **Removing diagonals would make columns 0, 2, 4, 6 unable to reach the
center at all.**

### 4.6 The center row has NO lateral moves

`slotExists('center', c±1)` for `c ∈ {1,3,5}` yields `{0,2,4,6}` — all non-lanes — so **a creature in
a center lane can never side-step to another center lane**. It must step out to `youFront`/`foeFront`
and back in (2 moves, i.e. 2 turns outside upkeep). This is an emergent but load-bearing rule.

### 4.7 Neighbour counts (verification table)

| row | col | # legal neighbour cells |
|-----|-----|------------------------|
| `foeBack` / `youBack` | 0 | 3 |
| `foeBack` / `youBack` | 1–5 | 5 |
| `foeBack` / `youBack` | 6 | 3 |
| `foeFront` / `youFront` | 0 | 4 |
| `foeFront` / `youFront` | 1 | 6 |
| `foeFront` / `youFront` | 2 | 7 |
| `foeFront` / `youFront` | 3 | 6 |
| `foeFront` / `youFront` | 4 | 7 |
| `foeFront` / `youFront` | 5 | 6 |
| `foeFront` / `youFront` | 6 | 4 |
| `center` | 1, 3, 5 | 6 (three into each flanking side row; zero lateral) |

Use this table as a unit test of the C# adjacency function.

---

## 5. Movement — the complete legality predicate

### 5.1 `moveSpent` — the per-turn move budget

`16_movement.js:26`
```js
function moveSpent(c){ return !!c.moved && !(G.upkeep && !c.moved2 && !c.tapped); }
```

Truth table:

| `c.moved` | `G.upkeep` | `c.moved2` | `c.tapped` | `moveSpent` | can move? |
|-----------|-----------|-----------|-----------|-------------|-----------|
| false | any | any | any | **false** | yes (first move) |
| true | false | any | any | **true** | no |
| true | true | false | false | **false** | yes (second move, will tap it) |
| true | true | true | any | **true** | no |
| true | true | false | true | **true** | no |

### 5.2 `canMoveCard` — may this unit start a move at all?

`16_movement.js:27`
```js
function canMoveCard(key,i){ const c=rowArr(key)[i];
  if(!c||c.kind!=='creature'||c.owner!=='you'||moveSpent(c))return false;
  return adjCells('you',key,i).some(([k,j])=>!rowArr(k)[j]); }
```

Requirements:
1. cell occupied,
2. occupant is a **creature**,
3. occupant is **owned by the acting player** (hard-coded `'you'` in the solo/local path; the MP host
   path uses `'foe'`, `42_mp_apply.js:65`),
4. `!moveSpent`,
5. **at least one adjacent cell is empty** (otherwise the button/drag is not offered).

**NOT required** (verify this against your instinct — the JS deliberately omits them):
* `!sick` — **summoning-sick creatures may move.** `13_input.js:142` explicitly renders the Move
  button for a sick creature; `16_movement.js:35` documents "summoning-sick, but can still reposition".
* `!tapped` — **a creature that already attacked or blocked may still move.** `13_input.js:145`
  renders the Move button for a tapped creature.
* `!worker` — workers never occupy cells, so the check is unnecessary in practice.
* `!entrench` — Entrench only resists *involuntary* displacement (bounce / Undertow),
  `06_mana_workers.js:137,179`. It does not stop the owner moving the unit.
* any keyword gate (`chrysalis`, `scour`, …) — none exist for movement.

### 5.3 `doMove` — the destination check

`16_movement.js:46-57`
```js
function doMove(toK,toI){
  if(!G.moveFrom)return; const {k,i}=G.moveFrom; const c=rowArr(k)[i];
  if(!c||c.kind!=='creature'||moveSpent(c)){ G.moveFrom=null; defaultHint(); render(); return; }
  if(rowArr(toK)[toI]||!adjacentK('you',k,i,toK,toI)){ setHint('Pick an open space one square away.'); return; }
  …
}
```

Note `doMove` **re-checks** `moveSpent` but does **not** re-check `c.owner`. The owner check lives in
`canMoveCard`, enforced when the move is armed (`startMove`, `16_movement.js:41`).

### 5.4 The portable predicate (write this in C#)

```
bool CanMove(GameState S, Cell from, Cell to, PlayerId mover)
{
  // ── source ──
  if (!IsRealSlot(from)) return false;
  var u = S.At(from);
  if (u == null)                       return false;
  if (u.Kind != Kind.Creature)         return false;
  if (u.Owner != mover)                return false;
  if (MoveSpent(S, u))                 return false;     // §5.1

  // ── destination ──
  if (!IsRealSlot(to))                 return false;     // col 0..6; center ⇒ col ∈ {1,3,5}
  if (S.At(to) != null)                return false;     // ANY occupant blocks: creature,
                                                         // structure, face-down charge, or trap
  // ── geometry ──
  if (Abs(RowIndex(to) - RowIndex(from)) > 1) return false;
  if (Abs(to.Col       - from.Col)      > 1) return false;
  if (from == to)                            return false;

  return true;
}

bool MoveSpent(GameState S, Unit u)
  => u.Moved && !(S.Phase == Phase.Upkeep && S.Turn == u.Owner && !u.Moved2 && !u.Tapped);

// Extra gate applied only when ARMING a move (UI affordance, and used by canActNow):
bool CanBeginMove(GameState S, Cell from, PlayerId mover)
  => CanMoveSource(S, from, mover) && Neighbours(from).Any(n => S.At(n) == null);
```

> **Caution on `MoveSpent`.** The JS reads the *global* `G.upkeep` flag, which is only ever true
> during the **local player's own** upkeep phase (`setPhase`, `17_turns_ai.js:45`; the AI branch of
> `startTurn` never calls `setPhase`, `17_turns_ai.js:60-70`). The `S.Turn == u.Owner` clause above
> makes that implicit condition explicit and is the behaviour you want.

### 5.5 What blocks a move — exhaustive

| blocker | where enforced |
|---------|----------------|
| destination has **any** occupant (creature / building / charge / trap, either owner) | `doMove` `rowArr(toK)[toI]` truthy, `16_movement.js:49` |
| destination is a center **flank** (col 0/2/4/6) | `slotExists`, `16_movement.js:5` |
| destination column out of `0..6` | `slotExists` |
| destination more than 1 row and/or 1 column away | `adjacentK`, `16_movement.js:15` |
| destination is a wall row | walls are not in `moveChainOf` |
| source already moved (and not eligible for the upkeep second move) | `moveSpent`, `16_movement.js:26` |
| source is not a creature | `canMoveCard` / `doMove` |
| source is not owned by the mover | `canMoveCard` |
| not your turn / `G.busy` / `G.over` | `startMove`, `16_movement.js:41` |
| phase is `draw` or `end` | `onCell` early return `13_input.js:97`; drag `begin` `31_ui_shell.js:175`; MP `42_mp_apply.js:63` |
| MP connection frozen | `43_mp_intents.js:195` |

**There is no swap, no push, no displacement, and no move-to-attack.** A creature cannot enter an
occupied cell under any circumstance.

### 5.6 Entering the enemy back row is legal and intentional

`16_movement.js:1-2` (file header):
> "every row is reachable — the middle rows are contested and the **enemy BACK row may now be
> entered**: the siege square, adjacent to their castle wall"

Consequences:
* A creature in `foeBack` is adjacent (rowIdx 0) to the foe's **virtual wall row** at index `-1`, so
  its strike at the wall crosses **zero** rows and **cannot be intercepted** (`16_movement.js:87-89`,
  `15_combat.js:6-11`).
* Both enemy rows count as the **`raid` worker zone** (`05_board_state.js:56-60`) — `raidKeys(owner)`
  returns **both** enemy rows. A raid zone's worker figure is never positive, so a camped army is
  billed every upkeep (Move / Pay / Sacrifice).
* `zoneKey(owner,'raid')` returns only the enemy **front** row (`05_board_state.js:60`) — see
  **[INCONSISTENT]** in §18.2.

---

## 6. Movement — execution and side effects

`16_movement.js:46-57` (`doMove`), with the MP mirror at `42_mp_apply.js:62-72`.

**Algorithm — `DoMove(from, to)`**

1. Require an armed move (`G.moveFrom`). *(UI state; in C# pass `from` explicitly.)*
2. Re-read `c = board[from]`. If it is missing, not a creature, or `MoveSpent(c)` → **abort**, clear
   the armed move.
3. If `board[to] != null` **or** `!Adjacent(from,to)` → **reject with a hint, keep the move armed**
   (the player may pick a different cell). *This is a real rule distinction: an illegal destination
   does not cancel the move.*
4. `board[from] = null`.
5. **Budget bookkeeping** — order matters:
   * if `c.moved` was already `true` → `c.moved2 = true; c.tapped = true;` *(second move spends the
     creature's whole turn)*
   * else → `c.moved = true;`
6. `board[to] = c`.
7. Log `"<name> repositions to <rowName(to.Row)>"`, appending `" — its turn is spent"` when `moved2`.
8. Clear the armed move.
9. **`syncWorkers(mover)`** (`05_board_state.js:71-78`) — the unit's `up` (monster upkeep) left one
   row's worker figure and joined another's, so both pools must be re-derived.
   New workers created by the resync are born `sick = true`.
10. If in upkeep → `upkeepNext()` (re-hint + pop the settle menu on the next offender,
    `17_turns_ai.js:110-116`); else → default hint + render.

**Not done by `doMove`:** no `checkWin`, no trap trigger, no keyword hook, no combat. Moving is inert
apart from the worker resync.

**Ordering note for determinism:** step 4 (vacate) happens *before* step 6 (occupy), and the worker
resync happens *after* both. A C# implementation must preserve that order or a same-row move would
transiently double-count `up`.

---

## 7. The move budget: once per turn, twice at upkeep

* **Action phase:** every creature gets **exactly one** move per turn.
* **Upkeep phase (the owner's own):** a creature that already moved this turn may move a **second**
  time; the second move sets `moved2 = true` **and** `tapped = true`, spending its entire turn (it can
  no longer attack). `16_movement.js:24-26,51`; UI label "Move again (taps it)" `16_movement.js:40`.
* A creature that moved once during upkeep and *not* again keeps its attack: `moved=true, tapped=false`.
  In the action phase `moveSpent` is then `true`, so it cannot move again but **can** attack.
* A creature that moved twice at upkeep is `tapped` and can neither attack nor move for the rest of
  the turn.

The upkeep second move exists so the player can rescue a worker-shortfall row by relocating a
high-`up` monster twice (`17_turns_ai.js:88-91`, `upkeepHint`). The AI shares the same 2-move cap in
principle (`17_turns_ai.js:180`) — see **[INCONSISTENT]** §18.1.

---

## 8. Congestion: columns, walls, and impassable rows

The design note is explicit (`combat-v3` era): **columns matter for movement congestion only; they
never matter in combat.** (`16_movement.js:90`, `12_render.js:444`, `17_turns_ai.js:262`.)

Emergent congestion rules — all of them fall out of "destination must be empty" + adjacency:

1. **A full row is a wall.** To advance from row `r` to row `r+2` a creature must pass through row
   `r+1`. If every real slot of row `r+1` is occupied, no unit can cross it. There is no jump, no
   flanking around a row.
2. **The center is the tightest choke point.** It has only **3** usable cells. Three bodies parked on
   `center[1]`, `center[3]`, `center[5]` completely seal the board: neither side can move between
   `youFront` and `foeFront`. This is the intended "mountain pass" (`01_core_defs.js:2`,
   `12_render.js:334`).
3. **Structures on center flanks never block movement** — flank slots (0/2/4/6) are not standable, so
   occupying them costs the mover nothing. They are pure economy/board presence.
4. **Column congestion within a row:** a creature can only shift ±1 column per turn, so a wall of
   bodies in adjacent columns forces a detour through another row (2+ turns).
5. **A creature may retreat.** Movement is bidirectional, so congestion can be relieved by pulling
   back, which is exactly what the upkeep settle loop asks for.
6. **The enemy back row can be sealed too** — 7 slots, so denying a siege requires 7 bodies (or just
   filling `foeFront`, which is cheaper).

There is **no** stacking, no zone-of-control, no attack-of-opportunity, and no movement cost other
than the once-per-turn budget.

---

## 9. Deployment — where new cards may enter

### 9.1 The authoritative rule

`13_input.js:101` — `const deployKey = key==='youBack' || key==='youFront';` — comment:
"**new cards enter only your back + front rows**".

`13_input.js:43-48`
```js
function handDeployOK(key,i){
  if(!(G.sel&&G.sel.kind==='hand'))return false;
  const c=G.P.you.hand[G.sel.idx]; if(!c)return false;
  if(key==='youBack'||key==='youFront')return true;
  return key==='center'&&G.sel.mode==='build'&&!isLane(i)&&placeRowOK('you','center',c);
}
```

| destination row | summon (creature) | build (structure) | set (face-down) | set trap |
|-----------------|-------------------|-------------------|-----------------|----------|
| `youBack`       | ✅ any col 0–6    | ✅ any col 0–6    | ✅ any col 0–6  | ✅ any col 0–6 |
| `youFront`      | ✅ any col 0–6    | ✅ any col 0–6    | ✅ any col 0–6  | ✅ any col 0–6 |
| `center` lanes (1/3/5) | ❌ | ❌ (`centerSlotOK`) | ❌ | ❌ |
| `center` flanks (0/2/4/6) | ❌ | ✅ *if* `placeRowOK('you','center',def)` | ❌ | ❌ |
| `foeFront`, `foeBack` | ❌ | ❌ | ❌ | ❌ |

**Creatures can never be summoned into the center.** They must be summoned into your rows and *march*
(`13_input.js:115`: "New cards can't deploy to the contested center — summon to your rows, then march
forward.").

### 9.2 `placeRowOK` — the worker-support gate

`06_mana_workers.js:196`
```js
function placeRowOK(owner,which,def){ return (def.sup||0)>=0 || (rowWorkers(owner,which)+(def.sup||0))>=0; }
```
Only structures with **negative** `sup` (currently only `tower` / Cannon Tower, `sup:-2`,
`03_cards_creatures.js:61`) are gated: the target row's worker figure must stay ≥ 0 after the build.

**[INCONSISTENT]** `handDeployOK` returns `true` unconditionally for `youBack`/`youFront`, so a
structure played **from hand** into your own rows **skips `placeRowOK`** entirely. Only the center
flank branch checks it. The build-menu path (`placeBuild`, `06_mana_workers.js:223`) and the MP path
(`42_mp_apply.js:110`) *do* check it. In practice this is unreachable today because no structure card
is deckable (`CARD_REG` is creatures + spells only, `06_mana_workers.js:38-43`), but `place()` fully
supports `mode==='build'` from hand. **Decide in the port:** either enforce `placeRowOK` on all
structure placements (recommended) or keep hand-built structures exempt.

### 9.3 AI deploy slot preference (not a rule, a heuristic)

`16_movement.js:17-23`
```js
function freeDeploySlot(owner,which){ first index where !occupied && !(center && !isLane) }
function aiPickDeploySlot(owner,which){
  const order = which==='center' ? [3,1,5]
              : which==='front'  ? [3,4,2,5,1,6,0]
              :                    [2,4,3,1,5,0,6];      // 'back'
  first i in order with i<SLOTS && !a[i] && slotExists(which,i); else freeDeploySlot(...)
}
```
Front order pushes toward the middle columns (so the next move can reach a center lane); back order
biases off-center (col 2 first) to leave column 3 free.

### 9.4 Vestigial deployment helpers — **[DEAD]**

`ownRows` (`05_board_state.js:16`), `canDeploy` (`:22`), `MINE` (`:24`), `hasEmptyDeploy`
(`06_mana_workers.js:194`) are defined and **never called** anywhere in `src/` or `index.html`
(verified by repo-wide grep). Do **not** port them; in particular `ownRows` implies creatures may
deploy to the center, which is false under the current rules.

---

## 10. Placement — hand play modes and the full `place()` algorithm

### 10.1 The action menu — which modes a hand card offers

`13_input.js:2-26` (`onHand`). Gated by `G.phase==='action'` and `!G.powerMode`.

```js
const can1 = manaTotal('you')>=1;   // setting face-down demands ◆1 — "no free hand-dumping"
```

| card | buttons | enabled when |
|------|---------|--------------|
| `type==='building'` | **Build** ◆`c` · **Set** ◆1 | `canPay` / `mana ≥ 1` |
| `type==='spell'` & `trap` | **Set** ◆1 | `mana ≥ 1` |
| `type==='spell'` & !`trap` | **Cast** ◆`c` | `canPay && spellHasTarget` |
| creature (anything else) | **Summon** ◆`c` · **Set** ◆1 | `canPay` / `mana ≥ 1` |

Non-trap spells **cannot** be set face-down. Traps **cannot** be cast.

`chooseMode(m)` (`13_input.js:27-40`) stores `G.sel.mode` and, **[DOM-ONLY]**, drops the raised castle
wall so the next tap reaches the board.

Mana helpers (`06_mana_workers.js:5-9`): one generic pool `P.mana`; `canPay(o,card) = P.mana >= card.c`;
`payAny(o,n)` subtracts `min(P.mana, n)`; `payCost` = `payAny(card.c)`. Colour never gates cost.

### 10.2 `onCell` dispatch while a hand card is held

`13_input.js:107-121`, in order:

1. `mode==='cast'` → if the tapped cell holds a **foe** unit and `validSpellTarget` → `castSpell`;
   otherwise deselect.
2. `mode==='settrap'` && empty cell && `handDeployOK` → `place(idx,'settrap',which,i)`
   *(redundant with rule 4 — same effect)*.
3. `(mode==='summon' || mode==='build')` && cell holds **your** unit with `bank > 0` && `deployKey`
   → **play on top** (`place`, §10.4).
4. `mode` (not cast) && empty cell && `handDeployOK` → `place`.
5. `mode` (not cast) && empty cell but **not** a legal drop → show an explanatory hint, **keep the
   selection**.
6. `mode` (not cast) && occupied illegal cell → "That spot is taken", **keep the selection**
   (explicitly so a fat-finger miss cannot cancel the play).

Rules 5 and 6 are a real UX rule worth porting: **an illegal drop never silently cancels a held card.**

### 10.3 `place()` — full algorithm

`13_input.js:178-237`. Signature `place(handIdx, mode, which, slot)` where `which ∈ {back,front,center}`.

```
PLACE(idx, mode, which, slot):
  card ← hand[idx]

  1. if !centerSlotOK(which, slot, card.type=='building'):
        hint("Build on the dark flanking slots …" / "Monsters fight in the glowing lanes (1,3,5)")
        ABORT (selection kept)

  2. arr ← cellArr('you', which);  occ ← arr[slot]

  3. IF occ != null:                                  // ── PLAY ON TOP OF A BANKED CARD ──
       a. if occ.cc                → hint("can't build over your own command center"); ABORT   [DEAD guard]
       b. if mode ∉ {summon,build} → hint("That slot is taken."); ABORT
       c. if !(occ.bank > 0)       → hint("That slot is taken."); ABORT
       d. fromBank ← min(occ.bank, card.c)
          need     ← card.c − fromBank
          if need > mana → hint("Short by ◆(need−mana) — the bank beneath covers ◆fromBank"); ABORT
       e. payAny(need)
       f. carry ← max(0, occ.bank − card.c)           // surplus rides onto the newcomer
       g. toGrave('you', occ)                         // the covered card is DESTROYED; its own
                                                      // summon mana is gone
       h. hand.splice(idx,1)
       i. if mode == summon:
              cr ← mkCre(card,'you',false); cr.sick ← TRUE; cr.bank ← carry; arr[slot] ← cr
              onCreatureEnter(cr,'you')               // Ward token, etc.
              foeTrapOnSummon(cr, which, slot)        // enemy 'summon' trap may destroy it
          else (build):
              b ← mkBld(card,'you'); b.bank ← carry; arr[slot] ← b
       j. G.sel ← null; afterDeploy('you'); render; checkWin;  RETURN

  4. ELSE (empty slot):
       mode == 'build':
           if card.c > mana → hint("Not enough mana."); ABORT
           payAny(card.c); hand.splice(idx,1); arr[slot] ← mkBld(card,'you')
           // NOTE: placeRowOK is NOT checked here — see §9.2 [INCONSISTENT]

       mode == 'summon':
           if mana < card.c  → hint("Not enough mana."); ABORT
           if !canPay(card)  → hint("<nm> needs ◆c — you have ◆mana…"); ABORT   (same test, twice)
           payCost(card); hand.splice(idx,1)
           cr ← mkCre(card,'you',false); cr.sick ← TRUE; arr[slot] ← cr
           onCreatureEnter(cr,'you'); foeTrapOnSummon(cr, which, slot)

       mode == 'settrap':
           if mana < 1 → hint("Setting a card face-down costs ◆1 — placed on the card."); ABORT
           payAny(1); hand.splice(idx,1)
           arr[slot] ← { kind:'trap', owner:'you', w:which,
                         card:{nm,c,effect,trigger,val,ic,art,trap:true},
                         setTurn: G.turnNo }

       mode == 'set'  (default branch — creature OR structure):
           if mana < 1 → hint("Setting a card face-down costs ◆1 — it banks toward the card's cost."); ABORT
           payAny(1); hand.splice(idx,1)
           ctype ← card.type
           cdata ← (ctype=='building') ? {nm,c,h,eff,val,sup,ic,art}
                                       : {nm,a,h,c,fs,up,art,kw,det,ward,wardhp,reap,grow,hatch,
                                          into,entrench,tribe,subtype}
           arr[slot] ← { kind:'charge', owner:'you', w:which, ctype, card:cdata,
                         inv: 1, setTurn: G.turnNo }

  5. G.sel ← null; defaultHint(); afterDeploy('you'); render(); checkWin()
```

`afterDeploy(owner)` (`06_mana_workers.js:23`) is just `syncWorkers(owner)`.

### 10.4 Play-on-top ("raise it on top, spending that stored mana")

This is a distinctive rule worth restating plainly:

* Any of **your** creatures or structures may carry banked ◆ (`unit.bank`), gained from a face-down
  surplus (§14) or transferred with **Send Mana** (`14_spells_traps.js:72-80`).
* Playing a **summon** or **build** onto that occupied cell **destroys the card underneath**
  (it goes to your graveyard, its own casting cost is lost) and pays the newcomer's cost from
  `occ.bank` first, the rest from your pool.
* Any surplus (`occ.bank − card.c`) rides onto the **new** unit as its `bank`.
* Only allowed in `youBack` / `youFront` (`deployKey`, `13_input.js:111`) — never in the center.
* Only over a card **you** own with `bank > 0`.

### 10.5 Mana/cost summary for placement

| action | cost paid | notes |
|--------|-----------|-------|
| Summon | `card.c` | full cost from the generic pool |
| Build (hand) | `card.c` | |
| Build (menu) | `def.c` | `placeBuild` |
| Set face-down (creature or structure) | **◆1** | banks as `inv:1` toward the flip cost |
| Set trap | **◆1** | consumed, not banked toward anything |
| Cast spell | `card.c` | |
| Play on top | `card.c − min(occ.bank, card.c)` | remainder from pool; surplus carried |
| Upgrade in place | `def.c` | `07_structures.js:28` |
| Move | 0 | free |

Mana cap is `99` on gain (`16_movement.js:184`, `17_turns_ai.js:5`). Unspent mana **drains at end of
turn** except what Mana Vaults hold (`17_turns_ai.js:32-39`).

---

## 11. Structure building from the build menu

`06_mana_workers.js:200-227`, `12_render.js:436`, `13_input.js:103-106`.

1. `openBuildMenu()` requires `acting()`. It clears `G.sel`, `G.atk`, `G.moveFrom`, `G.moveMana`,
   `G.cardMenu`, `G.build`.
2. `buildList(ccId)` (`03_cards_creatures.js:73-79`) is the ordered catalogue:
   `foundry`, one **forge per commander colour**, `encampment`, `longhouse`, `vault`, `outpost`,
   `bulwark`, `tower`, `reliquary`, then one **grand forge per colour**.
3. `canBuild(owner,def)` (`06_mana_workers.js:198`)
   `= manaTotal ≥ def.c ∧ prereqMet(owner,def) ∧ hasPlacement(owner,def)`.
   * `prereqMet` — every id in `def.prereq` satisfied by `hasBuild`, which walks a structure's
     **lineage** (`bidLineage`, `06_mana_workers.js:191`) so an upgraded tier still satisfies the
     prereq its base unlocked (a Keep still counts as a Foundry).
   * `hasPlacement` — some row among `back/front/center` has an empty slot **and** passes `placeRowOK`.
4. `buildPick` stores `G.build = def` and asks for a slot.
5. `placeBuild(which, i)` (`06_mana_workers.js:221-227`):
   * reject `which==='center' && isLane(i)`,
   * reject occupied slot or `!placeRowOK(owner,which,def)`,
   * re-check `canBuild` (cancel silently if it now fails),
   * `payAny(def.c)`, `cellArr[i] = mkBld(def,'you')`, `afterDeploy`.
6. `onCell` while `G.build` is set (`13_input.js:103-106`): tapping an **empty** cell in `youBack`,
   `youFront`, **or `center`** calls `placeBuild`; tapping anything else **cancels the build**.
7. `decorate` lights a cell when `(deployKey || (center && !isLane)) && empty && placeRowOK`
   (`12_render.js:436`).

Structures **never move** and never gain `sick`/`tapped` semantics.

---

## 12. Structure upgrades in place

`07_structures.js:4-31`. Included here because it is a *placement* operation: the unit stays on its
tile and keeps its `id`, `owner`, and `bank`.

* `upgradeTargets(o)` — requires `o.kind==='building' && !o.cc && o.bid`; returns
  `resolveStruct(bid, o.color)` for each id in the source def's `up2`.
* `upgradeWhy(owner,o,key,def)` returns the first blocking reason, `''` when legal:
  1. `def.row` set and `whichOf(key) !== def.row` → "only in your back/front row"
     (row-gated tiers: `keep`/`citadel` → `back`; `longhouse`/`barracks` → `front`,
     `03_cards_creatures.js:57,64-66`),
  2. `manaTotal(owner) < def.c` → "need ◆c",
  3. `def.sup < 0` and `rowWorkers(owner, whichOf(key)) − o.sup + def.sup < 0` → "row has no ⚒ to spare".
* `applyUpgrade(o,def)` (`07_structures.js:16-22`) mutates in place: new `bid/nm/eff/val/sup/ic/c/art`
  and optional `color`. **Damage carries through the rebuild**:
  `dmg = max(0, (o.maxh ?? def.h) − o.h); o.maxh = def.h; o.h = max(1, def.h − dmg);`
  — an upgrade repairs nothing; it only adds the new tier's extra max HP.
* `upgradeStruct` requires `acting()`, `o.owner==='you'`, `!o.cc`; then `payAny(def.c)`,
  `applyUpgrade`, `syncWorkers`, `afterDeploy`, `checkWin`.

---

## 13. Set face-down (charge) and set trap

### 13.1 The ◆1 set cost — the anti-hand-dump rule

`13_input.js:11` — `const can1 = manaTotal('you')>=1;   // setting face-down demands ◆1 placed on the
card — no free hand-dumping`.

* Setting a **creature or structure** face-down costs **◆1**, and that ◆1 is *placed on the card* as
  `inv: 1`, counting toward the card's flip cost (`13_input.js:227-233`).
* Setting a **trap** also costs **◆1**, but the trap has no `inv` — the mana is simply consumed
  (`13_input.js:220-225`).
* The drag pipeline mirrors the gate before it even starts a drag (`31_ui_shell.js:230`).
* The MP host re-validates it (`42_mp_apply.js:126,133`), and the solo AI observes it when arming a
  trap (`17_turns_ai.js:298`).

### 13.2 The face-down `charge` object

```js
{ kind:'charge', owner:'you', w:which, ctype:'creature'|'building',
  card:{…snapshot of the card…}, inv:1, setTurn:G.turnNo }
```
`13_input.js:233`. Note the object carries a **snapshot** of the card data (`cdata`), not a reference.
The creature snapshot preserves: `nm,a,h,c,fs,up,art,kw,det,ward,wardhp,reap,grow,hatch,into,
entrench,tribe,subtype`. The building snapshot preserves: `nm,c,h,eff,val,sup,ic,art`.
**`color` is not copied** — on flip, `mkCre`/`mkBld` fall back to `G.P[owner].color`
(`06_mana_workers.js:90,94`). **[BUG]** a face-down off-colour creature loses its element on flip.

`w` (owner-relative row name) is recorded but the flip path addresses the cell by global key, so `w`
is only used by `toGrave` bookkeeping and the trap search.

### 13.3 The face-down `trap` object

```js
{ kind:'trap', owner:'you', w:which,
  card:{nm,c,effect,trigger,val,ic,art,trap:true}, setTurn:G.turnNo }
```
`13_input.js:224`.

`findArmedTrap(owner,trigger)` (`14_spells_traps.js:34-40`) searches, in order, `G.P[owner].front`,
`G.P[owner].back` (slots `0..6`), then `G.center` filtered by `o.owner===owner`, and requires
**`G.turnNo > o.setTurn`** — *a trap can never spring on the turn it was set.*

Triggers in the data: `'summon'` (pitfall — destroy the summoned creature) and `'attack'`
(thornmail / backlash) — `03_cards_creatures.js:86-95`.

`foeTrapOnSummon(cr, w, i)` (`14_spells_traps.js:42-50`) is called **only** from the two `place()`
summon branches (`13_input.js:200,219`). `playerTrapOnSummon` (`:52-69`) is the interactive mirror
used when the **AI** summons (`17_turns_ai.js:312`) and in MP (`42_mp_apply.js:100,124`).

> **RULE: flipping a face-down creature does NOT provoke a `summon` trap.** `flip()` never calls
> `foeTrapOnSummon`. This is the main mechanical payoff of setting.

---

## 14. Flip rules

### 14.1 Charging a face-down (the charge panel)

`14_spells_traps.js:83-109`.

* `openCharge(key,slot)` requires `G.turn==='you' && !G.busy && !G.over`. Reached from `onCell` when
  you tap **your own** `charge` **and `G.atk.length === 0`** (`13_input.js:125`); the board is inert
  during draw/end and the upkeep branch returns before it, so effectively **action phase only**.
* `camtPour()` — `p = min(camt, manaTotal)`; `payAny(p)`; `ch.inv += p`. No cap beyond your pool.
* `camtFlip()` — requires `ch.inv >= ch.card.c`; calls `flip('you', key, slot)`.
* UI helpers: `camtFill` fills to exactly the remaining cost, `camtMax` offers all your mana.

### 14.2 `flip(owner, key, slot)`

`14_spells_traps.js:110-127`

```
FLIP(owner, key, slot):
  ch ← rowArr(key)[slot]

  IF ch.ctype == 'building':
      b ← mkBld(ch.card, owner)
      b.bank ← max(0, ch.inv − ch.card.c)          // surplus becomes banked ◆ on the structure
      rowArr(key)[slot] ← b
      log "… rises — structure online"
      RETURN                                        // ⚠ does NOT call syncWorkers  [BUG]

  // creature branch
  bank ← max(0, ch.inv − ch.card.c)
  sick ← (G.turnNo <= (ch.setTurn ?? G.turnNo))     // same turn as set ⇒ summoning sick
  cr ← mkCre(ch.card, owner, false)
  cr.bank ← bank;  cr.sick ← sick
  rowArr(key)[slot] ← cr
  log "… surges into being!" (+ "Must rest this turn." | "Battle-ready!")
  onCreatureEnter(cr, owner)                        // Ward token etc.
  syncWorkers(owner)
  RETURN cr
```

Key rules:

1. **Surplus banks.** Any `inv` above the card's cost becomes `bank` on the resulting unit.
2. **Sickness on flip is decided by `setTurn`.** `G.turnNo` increments on **every** `startTurn`, i.e.
   once per player-turn (`17_turns_ai.js:50`). So:
   * set and flipped on the *same* turn → `turnNo == setTurn` → **sick**;
   * set on turn `N`, flipped on your next turn (`turnNo == N+2`) → **not sick, battle-ready**.
   This is the reward for setting: a flipped card can attack immediately.
3. **[BUG] the building branch returns before `syncWorkers`.** A face-down structure's `sup` does not
   register until the next `syncWorkers` call (next `startTurn`, next deploy, next move). Port
   decision: call the worker resync on both branches.
4. `flip` never triggers summon traps (§13.3) and never calls `checkWin`.

### 14.3 Provoked flip (attacked face-down)

`15_combat.js:87-98` (`provokeFaceDown`), called from the attack paths.

* If `o.inv < o.card.c` → **interrupted**: the card is destroyed, `toGrave`, cell cleared, and all
  banked ◆ is lost.
* Else → `flip(defOwner, key, slot)` and the freshly revealed unit immediately participates:
  a creature fights back via `resolveCombat`; a structure just eats the damage.

### 14.4 Other flip paths

* **AI auto-fuel** (`17_turns_ai.js:271-273`): each turn the AI pours `min(mana, c − inv)` into every
  `foe`-owned charge in `foeFront` and in `center`, flipping when funded. *(The solo AI never creates
  charges itself, so this only fires for MP-driven or scripted states.)*
* **Scour** (`06_mana_workers.js:165-173`) shatters a back-row `charge` or `trap` outright on a
  connecting strike.
* **Burn spells** destroy a `charge` outright (`14_spells_traps.js:7`).

---

## 15. Summoning sickness — full lifecycle

### 15.1 Where `sick = true` is applied

| site | file:line |
|------|-----------|
| `place()` summon, empty slot | `13_input.js:216` |
| `place()` summon, play-on-top | `13_input.js:197` |
| AI summon | `17_turns_ai.js:310` |
| MP guest summon (both branches) | `42_mp_apply.js:97,119` |
| `flip()` creature, same turn as set | `14_spells_traps.js:120-121` |
| Ward token (`Lumen`) on enter | `06_mana_workers.js:120` |
| Reap token (`Shade`) on death | `06_mana_workers.js:131` |
| Chrysalis swell **and** hatch | `06_mana_workers.js:149,151` |
| New workers created by `syncWorkers` | `05_board_state.js:76` |
| `trainVillager` | `14_spells_traps.js:130` |

### 15.2 Where sickness clears

* **Board creatures:** `startTurn(owner)` clears `sick`, `tapped`, `moved`, `moved2`, `paid`,
  `blocked`, `_dis` for every creature **owned by** `owner`, anywhere on the board
  (`17_turns_ai.js:53`). This happens *before* the upkeep phase begins.
* **Worker pools:** `readyWorkers(owner)` (`05_board_state.js:81`) clears `sick`, `tapped`, `moved`
  for pool workers. Called at the end of `startTurn` (`17_turns_ai.js:58`) and again after the AI's
  deficit fix (`:69`) and at `startGame` (`09_game_start.js:10`). Because it runs only at turn start,
  **workers a structure adds mid-turn stay sick until the next turn** (`05_board_state.js:79-80`).

### 15.3 What sickness actually forbids

| capability | sick creature |
|-----------|---------------|
| attack | **NO** — filtered out by `canAttack` (`12_render.js:407`), `aiAttackers` (`17_turns_ai.js:248`), `CMB.declare` (`15_combat.js:243`), `doAttack`/`attackBackRow` (`16_movement.js:60,92`) |
| **move** | **YES, allowed** (`16_movement.js:27,35`; `13_input.js:142`) |
| **block / interpose** | **YES, allowed** for board creatures (`untappedInterceptors`, `15_combat.js:15-16`: only `!c.blocked` and `c.owner!==attackerOwner` are checked) |
| block, as a **worker minion** | **NO** — minions need `!tapped && !sick` (`15_combat.js:18`) |
| harvest (workers) | **NO** — `doHarvest` counts `!tapped && !sick` (`17_turns_ai.js:157`) |
| be targeted / take damage | yes, normally |

This asymmetry — sick units may move and block but not attack — is deliberate and repeated in the
UI copy ("Summoning-sick — it can act next turn", `13_input.js:144`).

---

## 16. The `laid` standee pose (`canActNow`)

`16_movement.js:28-38`, applied at `12_render.js:175`
(`if(o.kind==='creature' && !canActNow(o,key,i)) cell.classList.add('laid');`).

```js
function canBlockNow(o){ return !!(o&&o.kind==='creature'&&!o.blocked); }
function canActNow(o,key,i){
  if(!o||o.kind!=='creature'||o.worker) return true;   // non-creatures & workers have no pose
  if(G.turn===o.owner){
    if(o.tapped) return false;
    if(!o.sick)  return true;
    return !moveSpent(o) && adjCells(o.owner,key,i).some(([k,j])=>!rowArr(k)[j]);
  }
  return canBlockNow(o);
}
```

Meaning: **a creature stands up when it can still do something relevant right now, and lies down when
it cannot.** On its controller's turn: tapped ⇒ down; ready ⇒ up; summoning-sick ⇒ up only if it can
still reposition. On the opponent's turn: up iff it has not yet blocked this turn.

The `.laid` class itself is **purely cosmetic** (`src/styles/05_overlays_screens.css:68-72`: the
standee tilts flat, the idle bob animation stops, the sprite is desaturated and dimmed). Port
`canActNow` as a **view-model query** on the pure rules core (it reads only rules state), and let the
Unity view drive the standee pose from it.

---

## 17. The no-zero-cost-card rule

**Rule:** no card that can appear in a deck or hand may cost ◆0.

Enforcement in the current build is by **data invariant**, not by a runtime check:

| card class | cost range | source |
|-----------|-----------|--------|
| creatures (all 8 elements, 8 each) | `c: 1..6` | `03_cards_creatures.js:5-19` |
| Divine creatures (non-deckable) | `c: 1,3,4,6` | `:22` |
| neutral spells | `c: 2..3` | `:82-93` |
| neutral traps | `c: 1` | `:86,87,91,94,95` |
| structures | `c: 2..6` | `:53-70` |
| **Worker** template | `c: 0` | `:25` — pool-only, never in a deck or hand |
| tokens (`mkToken`) | `c: 0` | `06_mana_workers.js:114` — created on board, never in hand |
| `mkCC` command centre | `c: 0` | `04_cards_leaders.js:24` — **[DEAD]**, never called |

Rationale recorded in the project's own notes and in the source comments: setting a card face-down
costs ◆1 "placed on the card" specifically to prevent free hand-dumping (`13_input.js:11`). A 0-cost
card would make both `Summon` and `Set` free and break that. The deck builder validates count,
colour, and copies (`06_mana_workers.js:67-79`) but **does not** validate cost — so if you port the
registry, add an assertion `c >= 1` for every deckable entry.

---

## 18. AI movement and AI deployment

### 18.1 `aiMoveCreature`

`17_turns_ai.js:178-187`
```js
function aiMoveCreature(owner,fromKey,i,toZ){
  const arr=rowArr(fromKey); const o=arr&&arr[i]; if(!o)return false;
  if(o.moved&&(o.moved2||o.tapped))return false;                     // "two moves max"
  const dstKey=zoneKey(owner,toZ); const dst=rowArr(dstKey);
  let slot=-1;
  for(const j of [i,i-1,i+1]){                                       // straight first, then diagonals
    if(j>=0&&j<SLOTS&&!dst[j]&&slotExists(dstKey,j)&&adjacentK(owner,fromKey,i,dstKey,j)){slot=j;break;} }
  if(slot<0)return false;
  arr[i]=null;
  if(o.moved){ o.moved2=true; o.tapped=true; } else o.moved=true;
  dst[slot]=o; return true;
}
```

The destination geometry check is **identical** to the player's (`slotExists` + `adjacentK`), so the
AI obeys the same one-square rule. Column preference is `[i, i−1, i+1]` — straight ahead first, then
the left diagonal, then the right.

**[INCONSISTENT] the AI's move budget is looser than the player's.** The player's `moveSpent` allows a
second move *only during the player's own upkeep phase*; the AI's guard is
`o.moved && (o.moved2 || o.tapped)`, which has no phase condition. `aiFixDeficit` runs from
`startTurn('foe')` (`17_turns_ai.js:69`) where `G.upkeep` is `false` (the AI branch never calls
`setPhase`). Net effect: **the AI can always take two moves; the player can only during upkeep.**
Decide which is canon before porting.

**[BUG] the FX wrapper for `aiMoveCreature` has a stale signature.** `22_fx_wrappers.js:145-146`
computes the source element with `rowArr(zoneKey(owner,fromZ))[i]`, treating the second argument as a
**zone**, while the real function takes a **global row key**. This misidentifies the source rect for
raid-row moves. Cosmetic only (the wrapper's return value is the real result), but do not copy it.

### 18.2 `aiFixDeficit` — the AI upkeep rebalance

`17_turns_ai.js:177,188-215`
```js
const MOVE_ADJ={back:['front'],front:['back','center'],center:['front'],raid:['center']};
```
Loop 1 (≤ 40 iterations): for the first deficit zone, take its highest-`up` creature and try each
target zone in `MOVE_ADJ` order, skipping `raid` (never rebalance *into* enemy rows), and only if
`rowWorkers(owner,to) − o.up >= 0`.
Loop 2: while `totalDeficit > mana`, sacrifice the highest-`up` creature in the first deficit zone.
Step 3: pay the remainder and record it in `P.upaid[zone]`.

**[GAP]** `MOVE_ADJ.raid = ['center']` combined with `zoneKey(owner,'raid')` = the enemy **front** row
means a creature stranded in the enemy **back** row can never be rebalanced: `creaturesInRow` finds it
(because `raidKeys` spans both enemy rows, `05_board_state.js:58`) but
`adjacentK(owner,'foeBack',i,'center',j)` is false (rows 0 and 2 are not adjacent), so
`aiMoveCreature` always returns false and the AI falls through to sacrificing it. Worth fixing in the
port: the correct retreat from `foeBack` is `foeFront`.

### 18.3 AI deployment

`17_turns_ai.js:302-313`: candidates are creature cards it can pay for, sorted **most expensive
first**, capped at 6 attempts per turn. Destination: `foeFront` via `aiPickDeploySlot('foe','front')`,
falling back to `foeBack`. Each summon sets `sick = true`, fires `onCreatureEnter`, `syncWorkers`,
then awaits the player's optional summon-trap.

`aiBuild` (`07_structures.js:50-66`) picks the first affordable `buildList` entry under per-bid caps
`{foundry:1, encampment:1, longhouse:1, vault:1, outpost:1, bulwark:1, tower:2, reliquary:1}`
(an upgraded tier still counts toward its base's cap via `bidLineage`), one forge per colour, then
places into the first of `['back','front']` that has a free slot **and** passes `placeRowOK`.
The AI **never builds into the center**. It builds up to twice per turn (`17_turns_ai.js:285`) and
then tries one `aiUpgrade`.

---

## 19. Multiplayer re-validation (host-authoritative)

Deferred for the port, but the shape matters because the rules core must stay command-driven.

**Guest side** (`43_mp_intents.js`) wraps the local functions and emits an intent only when the local
call verifiably succeeded:

| local call | intent | success test |
|-----------|--------|--------------|
| `doMove(toK,toI)` | `{a:'move', fk, fi, tk, ti}` | `rowArr(toK)[toI] === c` (`:54-57`) |
| `place(idx,mode,which,slot)` | `{a:'place', idx, mode, w, i}` | hand shrank (`:35-42`) |
| `placeBuild(which,i)` | `{a:'build', bid, color, w, i}` | the new unit's `bid` matches (`:83-91`) |
| `upgradeStruct` | `{a:'upgrade', k, i, bid, color}` | the unit's `bid` changed (`:94-99`) |
| `camtPour` / `camtFlip` | `{a:'pour'|'flip', k, i, amt}` | `inv` grew / was funded (`:69-76`) |

Row keys are mirrored across the wire by `MPMAP.k` (`41_mp_sync.js:2-6`):
`youBack↔foeBack`, `youFront↔foeFront`, `center↔center`. Owner-relative `w` values are **not** mapped.

**Host side** (`42_mp_apply.js`) re-validates from scratch as `'foe'`:

* `move` (`:62-72`) — phase must be `action` **or** `upkeep`; unit must be a `foe` creature;
  `!moveSpent`; destination empty; `adjacentK('foe', …)`. Then the same
  `moved/moved2/tapped` bookkeeping and `syncWorkers('foe')`.
* `placeI` (`:74-142`) — mirrors `place()` including play-on-top, and **adds** checks the local path
  omits: `which ∈ {back,front}` or (`center` **and** `mode==='build'`); `0 ≤ slot < SLOTS`;
  `centerSlotOK`; card type must match the mode; `placeRowOK` for structures; owner of the covered
  card must be the guest.
* `build` (`:176-187`) — rejects center lanes, occupied slots, `!placeRowOK`, `!canBuild`.

**Port guidance:** the host validators are the *stricter, more complete* expression of the same rules.
Where the local path and the host path disagree, prefer the **host** version — it is what a
netcode-ready rules core needs anyway.

---

## 20. FX monkey-patch layer — what it does and does not change

`22_fx_wrappers.js` re-binds 20+ globals. Relevant to this subsystem:

| wrapped | line | rule impact |
|---------|------|-------------|
| `place` | `:96-110` | **none** — captures rects, calls through, then plays fly/ring/flash/SFX |
| `flip` | `:112-121` | **none** — summon ring / Master-Duel splash for `c>=4` or First Strike |
| `doMove` | `:135-143` | **none** — trail + fly + ring + slide SFX when a creature actually landed |
| `aiMoveCreature` | `:144-150` | **none** (but see the signature bug, §18.1) |
| `onCreatureEnter` | `:152-159` | **none** — AI summon FX parity |
| `placeBuild` | `:161-167` | **none** — construction beat |
| `aiBuild` | `:168-175` | **none** |

Every wrapper is `capture → call original → play FX`. **No wrapper alters legality, cost, or state.**
Confirmed by reading all 327 lines.

---

## 21. Presentation / DOM workarounds that must NOT enter the rules core

| behaviour | file:line | classification |
|-----------|-----------|----------------|
| `snapLegalCell(x,y)` — reroute a near-miss tap to the nearest **lit** cell within **44 px** (projected-rect distance, ties by centre) | `12_render.js:383-390` | **[DOM-ONLY]** works around the tilted-board `elementFromPoint` quirk |
| `snapContext()` — snapping only applies while moving / building / placing / aiming | `12_render.js:393` | **[DOM-ONLY]** |
| `onCellRouted` — snap **only** on taps that land on an *empty* non-legal cell | `12_render.js:394-402` | **[DOM-ONLY]** |
| off-board click snapping | `31_ui_shell.js:118-124` | **[DOM-ONLY]** |
| `cellUnder(x,y)` drop resolution with the same 44 px fallback | `31_ui_shell.js:147-165` | **[DOM-ONLY]** |
| drag threshold 7 px mouse / **15 px touch** | `31_ui_shell.js:139,279` | **[DOM-ONLY]** |
| pointer capture moved to `documentElement` so a re-render does not abort the drag | `31_ui_shell.js:141-145` | **[DOM-ONLY]** |
| no board-drag while `G.atk` is held (a rolled tap must not become `startMove` and wipe the group) | `31_ui_shell.js:183-187` | **[DOM-ONLY]**, but the *underlying* rule "Move is solo only" is real |
| `body.placing` ghosts non-selected hand cards | `15_combat.js:210-212`, `31_ui_shell.js:415-418` | presentation |
| `chooseMode` drops the raised castle wall | `13_input.js:32` | presentation |
| `.laid` CSS | `05_overlays_screens.css:68-72` | presentation (predicate is rules, §16) |
| status chips ⤧ / ⤧² / ⟳ / 💤 / ◆n | `12_render.js:141-142`, `18_inspect_viewers.js:104-107` | presentation of rules state |
| `G.cardMenu`, hints, `setHint` copy | throughout | presentation |
| marquee selection of the attack group | `31_ui_shell.js:191-222` | input only; **solo may mix rows, MP must share one row** (`31_ui_shell.js:213-219`) |

**Rules that hide inside the input layer and must be kept:**
* **Move is solo only** — arming a move clears `G.atk`, and the multi-attacker hint says "(Move is
  solo only.)" (`16_movement.js:41`, `13_input.js:161`).
* **An illegal drop keeps the held card selected** (`13_input.js:113-119`).
* **Drags are allowed in upkeep and action, never in draw or end** (`31_ui_shell.js:175`).
* **Hand plays are action-phase only** (`13_input.js:3`; upkeep is intercepted earlier at `:102`).

---

## 22. Dead code inventory

Do not port these; they encode superseded rules.

| symbol | file:line | why dead |
|--------|-----------|----------|
| `ownRows(owner)` | `05_board_state.js:16` | never called; claims creatures may deploy to the center |
| `canDeploy(owner,which)` | `05_board_state.js:22` | never called |
| `MINE` | `05_board_state.js:24` | never called |
| `hasEmptyDeploy(owner)` | `06_mana_workers.js:194` | never called |
| `colReach(aCol,tCol)` | `01_core_defs.js:5` | never called; columns do not gate combat |
| `mkCC(def,owner)` | `04_cards_leaders.js:23-24` | never called — **no command-centre unit exists**; every `o.cc` guard is therefore always false (`13_input.js:186`, `07_structures.js:5,25`, `06_mana_workers.js:168,188`) |
| `canExtract()` | `12_render.js:408` | hard-coded `false`; creatures no longer extract mana |
| `extractColors`, `colorNeed`, `manaGlyph` colour logic | `06_mana_workers.js:6,10,11` | colour no longer gates cost |
| `trainVillager` | `14_spells_traps.js:128-133` | workers are derived, not trained (the UI button is gone, `17_turns_ai.js:221`) |
| `G.P[*].cmana`, `zc()` | `01_core_defs.js:30`, `04_cards_leaders.js:218` | legacy colour pools, seeded but inert |
| `doExtract`, `extractSel`, `doExtractAs` | `15_combat.js:121-146` | unreachable (`canExtract` is false) |

---

## 23. Suggested C# types

Everything below is **pure** — no `UnityEngine` references, fully serialisable, deterministic.

```csharp
// ── geometry ─────────────────────────────────────────────────────────────
public enum RowId : byte { FoeBack = 0, FoeFront = 1, Center = 2, YouFront = 3, YouBack = 4 }
// virtual wall rows used ONLY by combat targeting; never a movement destination
public static class WallRow { public const int FoeWall = -1; public const int YouWall = 5; }

public readonly struct Cell : IEquatable<Cell> {
    public readonly RowId Row; public readonly byte Col;      // Col 0..6
    public Cell(RowId r, byte c) { Row = r; Col = c; }
}

public static class Board {
    public const int Slots = 7;
    public static readonly byte[] CenterLanes = { 1, 3, 5 };
    public static bool IsLane(int col) => col == 1 || col == 3 || col == 5;

    /// A real, standable cell: 0..6, and on the Center row only the three lanes.
    public static bool IsRealSlot(RowId r, int col)
        => col >= 0 && col < Slots && (r != RowId.Center || IsLane(col));

    /// Structures may claim Center flanks; creatures may not. Mirrors centerSlotOK().
    public static bool CenterSlotOk(RowId r, int col, bool isStructure)
        => r != RowId.Center || (isStructure ? !IsLane(col) : IsLane(col));

    /// Owner-agnostic (see §4.2). One step in any of 8 directions.
    public static bool Adjacent(Cell a, Cell b)
        => IsRealSlot(a.Row, a.Col) && IsRealSlot(b.Row, b.Col)
        && Math.Abs((int)a.Row - (int)b.Row) <= 1
        && Math.Abs(a.Col - b.Col) <= 1
        && !(a.Row == b.Row && a.Col == b.Col);

    public static IEnumerable<Cell> Neighbours(Cell c);        // deterministic order — see note below
}
```

> **Determinism note.** Emit neighbours in a fixed order. The JS order is: `(row, col−1)`,
> `(row, col+1)`, then for each adjacent row in chain order `(row′, col−1)`, `(row′, col)`,
> `(row′, col+1)`. Because the chain differs by owner, the *sequence* differs between `'you'` and
> `'foe'` even though the set does not — if any future rule ever picks "the first legal neighbour",
> that ordering becomes rules-relevant. Pin a single canonical order (recommended: ascending
> `RowId`, then ascending `Col`) and document it.

```csharp
// ── occupants ────────────────────────────────────────────────────────────
public enum OccupantKind : byte { Creature, Structure, FaceDownCharge, FaceDownTrap }
public enum PlayerId : byte { You = 0, Foe = 1 }
public enum FaceDownType : byte { Creature, Structure }

public abstract class Occupant {
    public int Id;                    // uid++ equivalent — stable across serialisation
    public OccupantKind Kind;
    public PlayerId Owner;            // ALWAYS authoritative; never infer from the row
    public int Bank;                  // stored ◆ riding on this card
}

public sealed class CreatureUnit : Occupant {
    public CardRef Card;              // ScriptableObject reference / id
    public string Name; public ElementId Color;
    public int Atk, Hp, MaxHp, Cost, Upkeep;   // 'a','h','maxh','c','up'
    public bool FirstStrike, IsWorkerToken, IsToken, Entrench;
    public KeywordId Keyword; /* + det, ward, wardHp, reap, grow, hatch, into, oc, cnt */

    // ── movement / action state ──
    public bool Sick;      // summoning sick: cannot attack; MAY move; MAY block
    public bool Tapped;    // has acted: cannot attack; MAY still move
    public bool Moved;     // used its move this turn
    public bool Moved2;    // used the upkeep second move (implies Tapped)
    public bool Blocked;   // already interposed this turn
    public bool PaidKeep;  // settled its upkeep this turn
}

public sealed class StructureUnit : Occupant {
    public string Bid;                // structure def id — drives upgrades / lineage
    public string Name; public ElementId? Color;
    public int Hp, MaxHp, Cost, Support /*sup*/, Value /*val*/;
    public StructEffect Effect;       // None, Mana, Villager, Damage, Wall, Revive, Vault, Command
}

public sealed class FaceDownCard : Occupant {   // kind == FaceDownCharge
    public FaceDownType ContentType;  // 'ctype'
    public CardSnapshot Card;         // frozen copy, NOT a live reference
    public int Invested;              // 'inv' — starts at 1 (the ◆1 set cost)
    public int SetTurn;               // G.turnNo at the moment it was set
}

public sealed class FaceDownTrap : Occupant {   // kind == FaceDownTrap
    public CardSnapshot Card;         // effect, trigger, val
    public TrapTrigger Trigger;       // Summon | Attack
    public int SetTurn;
}
```

```csharp
// ── commands (netcode-ready intents) ─────────────────────────────────────
public interface IGameCommand { PlayerId Actor { get; } }

public sealed class MoveCommand      : IGameCommand { public Cell From, To; }
public sealed class SummonCommand    : IGameCommand { public int HandIndex; public Cell To; }
public sealed class BuildFromHandCommand : IGameCommand { public int HandIndex; public Cell To; }
public sealed class SetFaceDownCommand   : IGameCommand { public int HandIndex; public Cell To; }
public sealed class SetTrapCommand       : IGameCommand { public int HandIndex; public Cell To; }
public sealed class BuildFromMenuCommand : IGameCommand { public string Bid; public ElementId? Color; public Cell To; }
public sealed class UpgradeStructureCommand : IGameCommand { public Cell At; public string TargetBid; }
public sealed class PourIntoFaceDownCommand : IGameCommand { public Cell At; public int Amount; }
public sealed class FlipFaceDownCommand     : IGameCommand { public Cell At; }
public sealed class SendBankedManaCommand   : IGameCommand { public Cell From, To; }

// ── the pure rules surface ───────────────────────────────────────────────
public interface IMovementRules {
    bool CanBeginMove(GameState s, Cell from, PlayerId mover);      // canMoveCard
    bool CanMove(GameState s, Cell from, Cell to, PlayerId mover);  // §5.4
    IReadOnlyList<Cell> LegalDestinations(GameState s, Cell from, PlayerId mover);
    MoveResult ApplyMove(GameState s, Cell from, Cell to, PlayerId mover);  // §6
    bool MoveSpent(GameState s, CreatureUnit u);
}

public interface IPlacementRules {
    IReadOnlyList<PlayMode> LegalModes(GameState s, HandCard c, PlayerId p); // §10.1
    bool CanDeployTo(GameState s, HandCard c, PlayMode m, Cell to, PlayerId p); // handDeployOK + centerSlotOK
    PlaceResult ApplyPlace(GameState s, int handIndex, PlayMode m, Cell to, PlayerId p);
    bool CanPlayOnTop(GameState s, HandCard c, PlayMode m, Cell occupied, PlayerId p);
}

public enum PlayMode : byte { Summon, Build, Set, SetTrap, Cast }
public enum PlaceRejection : byte { None, NotYourRow, CenterLaneForStructure, CenterFlankForCreature,
                                    SlotTaken, NotEnoughMana, NeedsOneMana, RowLacksWorkers,
                                    NoLegalTarget, WrongPhase, CoveredCardNotYours, CoveredCardHasNoBank }
```

**View-model helpers** (read-only, drive the presentation, live in the rules assembly so tests can
assert them):

```csharp
bool CanActNow(GameState s, Cell at);   // §16 — drives the standee "laid" pose
bool CanBlockNow(CreatureUnit u);       // kind == Creature && !Blocked
```

---

## 24. Test vectors

Port these as unit tests of the rules core.

**Adjacency**

| # | from | to | expected |
|---|------|----|----------|
| 1 | `youFront[0]` | `center[1]` | legal (diagonal) |
| 2 | `youFront[0]` | `center[0]` | **illegal** — flank, not a real slot |
| 3 | `youFront[2]` | `center[1]` | legal |
| 4 | `youFront[2]` | `center[3]` | legal |
| 5 | `youFront[2]` | `center[5]` | **illegal** — 3 columns away |
| 6 | `center[1]` | `center[3]` | **illegal** — no lateral move in the center |
| 7 | `center[3]` | `youFront[2..4]` | all three legal |
| 8 | `center[3]` | `foeFront[2..4]` | all three legal |
| 9 | `youFront[3]` | `foeFront[3]` | **illegal** — rows 3 and 1 are not adjacent |
| 10 | `foeFront[6]` | `foeBack[6]` | legal |
| 11 | `foeFront[6]` | `foeBack[5]` | legal (diagonal) |
| 12 | `youBack[0]` | `youBack[-1]` | **illegal** — column bound |
| 13 | `foeBack[3]` | wall row | **illegal** — walls have no slots |
| 14 | any cell | itself | **illegal** |

**Neighbour counts:** assert the table in §4.7 exactly.

**Budget**

15. Creature moves once in the action phase → second move rejected.
16. Creature moves once during its owner's upkeep → second move accepted, sets `Moved2` **and**
    `Tapped`; a third is rejected.
17. Creature moves once during upkeep, then tries to move in the action phase → rejected;
    it can still **attack** (not tapped).
18. Creature moves twice during upkeep → cannot attack (tapped).
19. Creature attacks (tapped), then moves → **accepted** (tapped does not block movement).
20. Summoning-sick creature moves → **accepted**.
21. Entrenched creature moves → **accepted** (Entrench only resists bounce).

**Occupancy**

22. Destination holds an enemy creature → rejected (no attack-by-move).
23. Destination holds a friendly structure / face-down / trap → rejected.
24. All three center lanes occupied → no creature can move between `youFront` and `foeFront`.

**Placement**

25. Summon into `center[1]` → rejected (`centerSlotOK` false for a creature? — *no*: lane is legal for
    a creature by `centerSlotOK`, but `handDeployOK` rejects the center for summon. Assert the
    rejection reason is **NotYourRow**, not the slot type.)
26. Build from the menu into `center[2]` with a `sup >= 0` structure → accepted.
27. Build a Cannon Tower (`sup:-2`) into a row whose `rowWorkers` is `1` → rejected (`placeRowOK`).
28. Set a creature face-down with ◆0 → rejected; with ◆1 → accepted, occupant is a `FaceDownCharge`
    with `Invested == 1` and `SetTurn == turnNo`.
29. Set on turn `N`, flip on turn `N` → the revealed creature is **sick**.
30. Set on turn `N`, flip on turn `N+2` → the revealed creature is **not** sick.
31. Flip with `Invested == cost + 3` → revealed unit has `Bank == 3`.
32. Attack an under-funded face-down (`inv < cost`) → destroyed, banked ◆ lost, no flip.
33. Summon a creature while the opponent holds an armed `summon` trap set on an **earlier** turn →
    the creature is destroyed. Same trap set **this** turn → does not fire.
34. **Flip** a face-down creature while the opponent holds an armed `summon` trap → trap does **not**
    fire.
35. Play a 3-cost creature on top of your own unit holding `Bank == 5` → pays 0 from the pool, the
    covered card is graved, the newcomer has `Bank == 2` and `Sick == true`.
36. Play a 3-cost creature on top of a unit with `Bank == 1` and pool `= 1` → rejected
    ("short by ◆1").
37. Play on top in the **center** → rejected (only `youBack`/`youFront`).

**Side effects**

38. Move a creature with `Upkeep 2` from `youFront` to `center` → `rowWorkers('you','front')` rises by
    2 and `rowWorkers('you','center')` falls by 2, and the worker pools are re-derived, with any new
    worker born `Sick`.
39. Move into `foeFront` or `foeBack` → the `raid` zone figure goes negative and appears in
    `deficitRows`.

---

## 25. Open questions

1. **Player vs AI move budget (§18.1).** Should the second move be upkeep-only for both sides, or
   always available to both? The JS gives the AI a permanent two-move allowance by accident.
2. **`placeRowOK` on hand-played structures (§9.2).** Enforce for all placements, or keep the
   hand path exempt? Currently unreachable (no deckable structures), but the port will likely want
   deckable structures eventually.
3. **`flip()` on a structure skips `syncWorkers` (§14.2).** Bug or a deliberate one-turn delay before
   a revealed structure's support counts? Everything else in the file resyncs immediately, so it
   reads as a bug.
4. **Face-down cards lose `color` on flip (§13.2).** The snapshot omits `color`, so the flipped unit
   inherits the player's element. Intentional (face-downs are "generic") or a bug?
5. **AI cannot retreat from the enemy back row (§18.2).** Fix `MOVE_ADJ`/`zoneKey` so `raid` retreats
   to the enemy front row first, or accept that deep raiders get sacrificed?
6. **Center lateral movement (§4.6).** The three lanes being mutually unreachable in one step is
   emergent, not stated anywhere. Confirm it is intended before the port locks it in.
7. **Should `startMove` gate on phase?** It only checks `turn/busy/over`; every *reachable* entry
   point is already phase-gated, but a direct command in the C# port would not be. Recommend adding
   `phase ∈ {Upkeep, Action}` explicitly (this is what the MP host already does,
   `42_mp_apply.js:63`).
8. **Neighbour enumeration order (§23).** Pin a canonical order now, before any rule starts depending
   on "the first legal cell".
