# 02 — Economy, Turn Phases, Upkeep & Harvest

**Subsystem spec for the Unity 6 / C# port of _Spawn Row Duel_.**

Source of truth: the JavaScript in `src/js/` at commit `8b90375` (branch `main`).
Every rule below was read out of that source; every claim carries a `file:line` citation so an
implementer can verify it. Where the JS contains a latent bug, dead code, or a browser/DOM
workaround, it is explicitly labelled as such — **do not port those verbatim**.

This document covers:

* the explicit phase machine (`G.phase`)
* turn-start (upkeep) ordering
* the derived worker model (workers are **not** trained and do **not** move)
* the explicit Move / Pay / Sacrifice upkeep settlement and the Harvest lock
* harvest (player, AI, and the multiplayer mirror)
* generic mana: every income source, every cost sink, the end-of-turn drain, Mana Vaults
* the `raid` pay-to-stay zone
* structure build/upgrade economy (the only mana sink that is not a card)
* deck-click draw

It does **not** cover combat resolution (see the combat spec), card keyword effects beyond their
economy hooks, campaign, or rendering.

---

## 1. Source map

| Concern | File | Notes |
|---|---|---|
| Mana primitives, build menu, keyword upkeep hooks | `src/js/06_mana_workers.js` | 227 lines |
| Board/zone geometry, worker derivation, deficits | `src/js/05_board_state.js` | 91 lines |
| Structure defs, build list, creature stat table | `src/js/03_cards_creatures.js` | |
| Element table, commander (CC) table, `G` root object | `src/js/01_core_defs.js`, `src/js/04_cards_leaders.js` | |
| Structure upgrade + AI build/upgrade + `toGrave` | `src/js/07_structures.js` | |
| Match initialisation | `src/js/09_game_start.js` | |
| **Phase machine, upkeep settlement, harvest, draw, end turn, AI turn** | `src/js/17_turns_ai.js` | primary file |
| `minYield`, legacy per-row harvest | `src/js/15_combat.js:144-170` | |
| `applyRes` (mana credit), movement + `moveSpent` | `src/js/16_movement.js` | |
| Costs paid on hand plays / sets / play-on-top | `src/js/13_input.js:178-237` | |
| Spell cast cost, charge pour/flip, banked-mana transfer | `src/js/14_spells_traps.js` | |
| Presentation of mana/workers/phase track | `src/js/12_render.js` | **presentation only** |
| FX monkey-patches over economy functions | `src/js/22_fx_wrappers.js:199-229` | **presentation only** |
| Multiplayer re-implementation of every economy step | `src/js/41_mp_sync.js`, `src/js/42_mp_apply.js`, `src/js/43_mp_intents.js` | deferred, but see §13 |

---

## 2. Root state shape

`G` is the single global mutable game state (`src/js/04_cards_leaders.js:214-223`).

```js
G = {
  turn:'you'|'foe', busy:bool, over:bool, turnNo:int,
  phase:'upkeep'|'draw'|'action'|'end', upkeep:bool,   // upkeep is a derived duplicate of (phase==='upkeep')
  sel, atk[], decls[], moveFrom, moveMana,             // selection / UI intent state
  center: Array(7),                                    // the shared contested row
  P: { you:{...}, foe:{...} }
}
```

Per-player economy fields (`src/js/09_game_start.js:3-4`):

| Field | Type | Meaning |
|---|---|---|
| `mana` | int | **the** generic mana pool. Capped at 99 on every credit. |
| `cmana` | `{fire:0,water:0,…}` | **DEAD.** Legacy colored pool, seeded by `zc()` and never read. Do not port. |
| `life` | int | stronghold life pool (10000 at start, see §3) |
| `hand`, `deck`, `grave` | arrays | |
| `front`, `back` | `Array(7)` of cell occupants | this player's two board rows |
| `min` | `{back:[], front:[], center:[]}` | **derived worker pools** (§6). No `raid` pool exists. |
| `upaid` | `{back:0,front:0,center:0,raid:0}` | mana already paid this upkeep toward each zone's shortfall |
| `firstExtract` | bool | **DEAD.** Set/cleared, never read for rules. |
| `villagerUsed` | bool | **DEAD.** Never read anywhere. |
| `cc` | commander id | e.g. `'fire'` or `'fire_water'` |
| `color` | element id | first color of the commander; a *synergy/art attribute only* |

Per-unit economy fields:

| Field | On | Meaning |
|---|---|---|
| `bank` | creature, building | mana **stored on the card** — survives the end-of-turn drain (§9.4) |
| `inv` | charge (face-down card) | mana invested toward flipping it face-up |
| `oc` | creature with `kw:'overcharge'` | banked ◆ that converts to bonus **attack**, never back to mana |
| `up` | creature | this creature's **worker upkeep** (⚒ cost in its row) |
| `sup` | building | this structure's **worker support** (⚒ granted in its row); may be negative |
| `eff`, `val` | building | per-turn effect and its magnitude (§10) |
| `paid` | creature | already settled by an explicit ◆ Pay this upkeep |
| `moved`, `moved2`, `tapped`, `sick`, `blocked` | creature | action budget flags |

---

## 3. Starting values

Elements (`src/js/01_core_defs.js:15-26`). All nine elements share `hp:10000`; only the starting
worker count differs.

| Element | `hp` | `wk` (starting workers) |
|---|---|---|
| fire | 10000 | 2 |
| water | 10000 | 3 |
| earth | 10000 | 2 |
| wind | 10000 | 3 |
| forest | 10000 | 2 |
| electric | 10000 | 3 |
| light | 10000 | 3 |
| dark | 10000 | 2 |
| divine (non-deckable, boss/NPC only) | 10000 | 2 |

Commanders (`CCS`, `src/js/04_cards_leaders.js:9-22`): 8 solo (id = element id) + 28 dual
(id = `elemA_elemB`, alphabetically-ordered pairs from `COLORS`) = **36**.
Dual: `hp = round((hpA+hpB)/2)` = 10000; `wk = round((wkA+wkB)/2)` — JS `Math.round` rounds .5 **up**,
so fire(2)+water(3) → 3. Reproduce `MidpointRounding.AwayFromZero` for positive halves.

`startGame` (`src/js/09_game_start.js:1-19`) — **turn 1 does NOT run `startTurn`**:

1. Reset both `P` records: `mana=0`, `life=CCS[cc].hp`, empty hand/deck/grave, empty 7-slot
   `front`/`back`, empty `min` pools, `upaid` zeroed.
2. `G.turn='you'; G.turnNo=1; G.phase='upkeep'; G.upkeep=true;` `G.center` = 7 empty slots.
3. `syncWorkers('you'); syncWorkers('foe');` then `readyWorkers('you'); readyWorkers('foe');`
   → each player starts with exactly `CCS[cc].wk` **ready** workers in the **back** zone
   (§6.2 shows why: the back zone gets a free `+wk`), and 0 in front/center.
4. Decks assigned (custom deck or `deckOf(colors)`), `dealOpening` deals **4** cards each
   (`src/js/11_deck_builder.js:247-248`). `DECK_SIZE = 40`, `MAX_COPIES = 3`
   (`src/js/06_mana_workers.js:37`).
5. `setPhase('upkeep')`.

**Consequence:** on turn 1 there are no structures, so the only income is the worker harvest of
`wk` (2 or 3 ◆). No structure mana yield, no chrysalis/overcharge ticks, no `upaid` reset call —
they were already initialised.

`G.turnNo` increments once per **player turn** (half-round), not per round
(`src/js/17_turns_ai.js:50`).

---

## 4. The phase machine

### 4.1 Definition

```js
const PHASE_ORDER = ['upkeep','draw','action','end'];              // 17_turns_ai.js:43
const PHASE_LABEL = {draw:'Draw',upkeep:'Upkeep',action:'Action',combat:'Combat',end:'End'};
function setPhase(p){ G.phase=p; G.upkeep=(p==='upkeep'); }        // 17_turns_ai.js:45
function acting(){ return G.turn==='you' && !G.busy && !G.over && G.phase==='action'; }  // :46
function shownPhase(){ return (G.phase==='action' && (G.atk.length || G.decls.length)) ? 'combat' : G.phase; }  // :48
```

`combat` is **not** a real phase — it is a *display* label for "we are in `action` and attackers are
declared". `PHASE_ORDER` contains only the four real phases.

`G.upkeep` is a redundant mirror of `phase === 'upkeep'`, maintained solely by `setPhase`.
**In C# keep one enum and delete the boolean.**

### 4.2 Transition table

| From | Trigger | To | Guard | Citation |
|---|---|---|---|---|
| *(match start)* | `startGame` | `Upkeep` | — | `09_game_start.js:5,18` |
| — | `startTurn('you')` | `Upkeep` | always, for the human side | `17_turns_ai.js:61` |
| — | `startTurn('foe')` in **multiplayer** | `Upkeep` | remote player drives via intents | `17_turns_ai.js:66` |
| — | `startTurn('foe')` vs **AI** | *(phase left untouched — stays `End`)* | see §4.4 | `17_turns_ai.js:67-70` |
| `Upkeep` | `doHarvest()` | `Draw` | every creature-settleable shortfall settled (§7.4) | `17_turns_ai.js:147-174` |
| `Draw` | `doDraw()` (deck click) | `Action` | `turn==='you' && !busy && !over && phase==='draw'` — **advances even if the deck is empty** | `17_turns_ai.js:78-84` |
| `Action` | `endTurn()` | `End` | no undeclared combat pending (`!CMB.hasDecls()`) | `17_turns_ai.js:222-243` |
| `End` | `startTurn(other)` | `Upkeep` (human/MP) | after `endTurnDrain` and a 380 ms beat | `17_turns_ai.js:238-242` |

There is **no** way to skip Upkeep, and **no** way to reach `Action` without passing through `Draw`.
`endTurn` explicitly refuses and bounces the player back to the pending step:

```js
if(G.phase==='draw'){ drawHint(); render(); return; }      // must draw first    :224
if(G.phase==='upkeep'){ upkeepHint(); render(); return; }  // harvest first      :225
if(G.phase!=='action') return;                                                   :226
```

### 4.3 What is legal in each phase

| Capability | Upkeep | Draw | Action | End |
|---|---|---|---|---|
| Play a card from hand (summon / build / set / set-trap / cast) | ✖ | ✖ | ✔ | ✖ |
| Open the ⚒ Build menu | ✖ | ✖ | ✔ | ✖ |
| Upgrade a structure | ✖ | ✖ | ✔ | ✖ |
| Attack / declare combat | ✖ | ✖ | ✔ | ✖ |
| Move a creature | ✔ (up to **2** moves, see below) | ✖ | ✔ (1 move) | ✖ |
| Tap a creature to open its card menu | ✔ (Move/Pay/Sacrifice only) | ✖ | ✔ | ✖ |
| Sacrifice a creature | ✔ | ✖ | ✖ | ✖ |
| Pay a creature's keep | ✔ | ✖ | ✖ | ✖ |
| ⛏ Harvest | ✔ (gated) | ✖ | ✖ | ✖ |
| Click deck to draw | ✖ (opens the deck **viewer** instead) | ✔ | ✖ (viewer) | ✖ (viewer) |
| End Turn button | disabled | disabled | enabled | — |
| Board is inert (no click handlers wired at all) | — | ✔ | — | ✔ |

Enforcement points:

* hand plays: `onHand` returns immediately unless `G.phase==='action'` (`13_input.js:3`)
* board clicks: `onCell` returns on `draw`/`end` (`13_input.js:97`); during `upkeep` it routes
  **only** to `upkeepPick` for your own creatures (`13_input.js:102`)
* cell decoration wires no handlers on `draw`/`end` (`12_render.js:412`) and only the upkeep
  settle handler during upkeep (`12_render.js:426-429`)
* build menu / upgrades / attacks all gate on `acting()` → `phase==='action'`
  (`06_mana_workers.js:200`, `07_structures.js:24`, `15_combat.js:236,305`)
* End Turn button `disabled = !acting()` (`12_render.js:15`)
* pointer drag: no drags in `draw`/`end`; hand drags only in `action`; marquee only in `action`
  (`31_ui_shell.js:175,178,191`)

**Movement budget nuance** (`src/js/16_movement.js:26,51-52`):

```js
function moveSpent(c){ return !!c.moved && !(G.upkeep && !c.moved2 && !c.tapped); }
```

* In **Action**: a creature may move **once** per turn (`moved` set).
* In **Upkeep**: a creature that has already moved may move a **second** time; the second move sets
  `moved2 = true` **and** `tapped = true` — it has spent its whole turn and can no longer attack.
* A creature that moved during upkeep (once) still has `moved=true` entering Action, so it cannot
  move again there, but it can still attack.

### 4.4 The AI-turn phase anomaly (**a real quirk — decide deliberately in the port**)

When the opponent is the AI, `startTurn('foe')` falls into the third branch
(`17_turns_ai.js:67-70`) which never calls `setPhase`. The last `setPhase` was `setPhase('end')`
inside the player's `endTurn`. **Therefore `G.phase === 'end'` for the entire AI turn.**

This is benign in the JS only because every gate also tests `G.turn === 'you'`. In C# this is a
landmine: model the AI's turn as running through the same phase sequence (`Upkeep → Draw → Action →
End`) and drive the AI as a command source, or give each player its own phase field. The multiplayer
path already does the right thing (`setPhase('upkeep')` for the remote `foe`, `17_turns_ai.js:66`).

---

## 5. Turn start — exact ordering

`startTurn(owner)` — `src/js/17_turns_ai.js:49-71`. **This ordering is load-bearing.**

1. `G.turnNo++`; `G.turn = owner`.
2. Clear transient UI/intent state: `G.cardMenu = null`, `G.moveMana = null`, `G.decls = []`.
3. `P.firstExtract = true` *(dead flag)*.
4. **`P.upaid = {back:0, front:0, center:0, raid:0}`** — last turn's keep payments expire.
   Shortfalls are settled **anew every upkeep**; there is no persistent "already paid" state.
5. **Refresh every one of `owner`'s creatures** (`ownUnits(owner)` walks all five global rows and
   filters by the unit's own `owner` tag — fronts and the center are contested, so never assume a
   row's occupants belong to that row's side):
   `sick=false, tapped=false, moved=false, moved2=false, paid=false, blocked=false, _dis=0`.
6. `chrysalisUpkeep(owner)` (`06_mana_workers.js:144-152`) — every `kw:'chrysalis'` unit gains
   `grow` (default 1) counters; at `cnt >= hatch` (default 3) it transforms in place into `into`
   (name/atk/maxhp/hp/upkeep/first-strike/keyword all replaced) and is set `sick = true`; otherwise
   it is set `sick = true` anyway (a cocoon can never attack).
7. `overchargeUpkeep(owner)` (`06_mana_workers.js:154-157`) — every `kw:'overcharge'` unit does
   `oc = min(3, oc+1)`. **This is a combat resource, not mana**; it is spent as bonus attack by
   `dischargeOvercharge` and never converts to ◆.
8. **`buildingUpkeep(owner)`** (`17_turns_ai.js:2-11`) — see §9.2/§10.3. Structure `eff` effects fire
   here: `mana` yields ◆, `damage` (Cannon Tower) fires, `revive` (Reliquary) fires **at most once
   per turn regardless of how many Reliquaries you own** (the `revived` latch is local to this call).
   Iteration order: `P.front` (slots 0→6), then `P.back` (0→6), then `G.center` (0→6, owner-filtered).
9. `cleanup()` (`16_movement.js:193-205`) — sweep anything the Cannon Tower just killed, firing
   death keywords, looping until stable (guard 40 iterations).
10. **`syncWorkers(owner)`** — re-derive the worker pools from the cards now standing in each row (§6.3).
11. **`readyWorkers(owner)`** — clear `sick`/`tapped`/`moved` on every worker so they can harvest (§6.4).
12. Branch:
    * `owner === 'you'` → `setPhase('upkeep')`, log the upkeep banner, `upkeepHint()`, then
      **auto-open the settle menu on the first offender**: `const off = upkeepOffender(); if(off) upkeepPick(off.key, off.i);`
    * multiplayer `foe` → `setPhase('upkeep')` only; the remote player sends intents.
    * AI `foe` → `drawCard('foe')` (**the AI draws here, at turn start, not in a Draw phase**),
      then `aiFixDeficit('foe')`, then **`readyWorkers('foe')` a second time** (re-settle after the
      AI's rebalancing possibly created new worker rows).

> **Ordering rule to preserve:** structure income (step 8) lands **before** worker derivation
> (step 10) and **before** any upkeep settlement (step 12). This is what makes "pay-to-stay is
> funded out of vault carry-over + this turn's forge yields, *before* you harvest" true for both
> the player and the AI.

---

## 6. Workers — a derived figure, not an entity

### 6.1 The rule in one sentence

> Workers are **not trained** and **do not move**. Each of your zones has an integer worker figure
> equal to (Σ structure support in that zone) − (Σ monster upkeep in that zone), plus a free
> `+CCS[cc].wk` in your **back** zone. The visible worker tokens are a pool rebuilt to match that
> figure; they exist only so they can be tapped for harvest, intercept a strike, and be raided.

### 6.2 Zones

```js
const ZONES = ['back','front','center','raid'];                      // 05_board_state.js:56
function raidKeys(owner){ return owner==='you' ? ['foeFront','foeBack'] : ['youFront','youBack']; }  // :58
function zoneKeys(owner,z){ return z==='raid' ? raidKeys(owner) : [zoneKey(owner,z)]; }              // :59
function zoneKey(owner,z){ return z==='center' ? 'center'
                          : z==='raid' ? (owner==='you'?'foeFront':'youFront')
                          : rowKeyFor(owner,z); }                                                     // :60
```

Global rows, top → bottom (`05_board_state.js:4`):
`['foeBack','foeFront','center','youFront','youBack']`, each 7 columns (`C = SLOTS = 7`).
The center row has monster lanes only at columns **1, 3, 5**; columns 0/2/4/6 are structure slots
(`01_core_defs.js:2-7`).

| Zone (from `you`'s view) | Global row(s) read | Has a worker pool? | Harvests? | Free `+wk`? |
|---|---|---|---|---|
| `back` | `youBack` | ✔ `P.min.back` | ✔ | ✔ `+CCS[cc].wk` |
| `front` | `youFront` | ✔ `P.min.front` | ✔ | ✖ |
| `center` | `center` | ✔ `P.min.center` | ✔ | ✖ |
| `raid` | **both** `foeFront` **and** `foeBack` | ✖ (no pool exists) | ✖ | ✖ |

Note the asymmetry, and preserve it: `zoneKeys('you','raid')` spans **both** enemy rows (so a deep
siege into the enemy back row is charged the same as one into their front row), but `zoneKey`
(singular, used only as a *movement destination*) resolves `raid` to the enemy **front** row only.

### 6.3 The worker figure

```js
function rowWorkers(owner,which){                                    // 05_board_state.js:61-68
  let s = 0;
  zoneKeys(owner,which).forEach(k => rowArr(k).forEach(o => {
    if(!o || o.owner !== owner) return;
    if(o.kind === 'building')      s += (o.sup||0) + (o.eff==='villager' ? (o.val||0) : 0);
    else if(o.kind === 'creature' && !o.worker) s -= (o.up||0);
  }));
  if(which === 'back') s += CCS[G.P[owner].cc].wk;                   // the homeland staffs the back row
  return s;                                                          // MAY BE NEGATIVE
}
```

Facts:

* Only `kind==='building'` and non-worker `kind==='creature'` contribute. Face-down `charge` cards,
  face-down `trap` cards, and worker tokens contribute **nothing**.
* `eff==='villager'` adds `val` — but **every** `villager` structure in the data has `val: 0`
  (Longhouse `03_cards_creatures.js:57`, Barracks `:66`). So this term is always 0 today.
  The "trains a Worker each turn" text on those cards is **stale flavour**; their real contribution
  is their large `sup` (+3 / +4).
* The `raid` zone can only ever be ≤ 0 for you (you cannot build in enemy rows), so a raiding army
  is pure upkeep with no offsetting support — that *is* the pay-to-stay mechanic.
* `totalWorkers(owner) = Σ over {back,front,center} of max(0, rowWorkers)` — the HUD ⚒ number
  (`05_board_state.js:69`). Raid is excluded and negatives clamp to 0.

### 6.4 Pool synchronisation

```js
function syncWorkers(owner){                                         // 05_board_state.js:71-78
  ['back','front','center'].forEach(which => {
    const target = Math.max(0, rowWorkers(owner,which));
    const pool = G.P[owner].min[which];
    while(pool.length > target) pool.pop();                          // trims from the END
    while(pool.length < target){ const w = mkVil(owner); w.sick = true; pool.push(w); }
  });
}
function readyWorkers(owner){                                        // :81
  ['back','front','center'].forEach(w =>
    G.P[owner].min[w].forEach(m => { m.sick=false; m.tapped=false; m.moved=false; }));
}
```

* New workers enter **summoning-sick**, so a structure raised mid-turn cannot harvest that turn.
* `readyWorkers` runs **only** at turn start (step 11 of §5, plus once more for the AI after
  `aiFixDeficit`). Therefore any worker created after that — by a mid-upkeep sacrifice, by a
  mid-action build, by an upgrade — stays `sick` and does **not** harvest this turn.
* A worker is `mkVil(owner)` → `mkCre({nm:'Worker', a:0, h:1000, c:0, up:0}, owner, /*worker*/true)`
  (`06_mana_workers.js:93`). Attack 0, HP 1000, no upkeep, no cost.
* **Killed workers regenerate.** Combat splices dead workers out of the pool (`16_movement.js:202-203`),
  but the next `syncWorkers` at that player's turn start refills the pool to the derived figure and
  `readyWorkers` immediately un-sicks them. **Raiding an enemy worker stack therefore denies at most
  one turn of that row's harvest** (and only if it lands before they harvest). This is a genuine
  rule, not an accident — port it.

**Call sites of `syncWorkers` (the complete list — a missing call leaves the pool stale):**

| Site | File:line |
|---|---|
| `startTurn` step 10 | `17_turns_ai.js:58` |
| `startGame` (both players) | `09_game_start.js:9` |
| `afterDeploy(owner)` (thin wrapper: `afterDeploy = syncWorkers`) — after every hand play, every menu build, every upgrade | `06_mana_workers.js:23,227`; `13_input.js:205,236`; `07_structures.js:30`; `42_mp_apply.js:106,142,187,197` |
| `doMove` (player) / `move` intent (MP) | `16_movement.js:55`; `42_mp_apply.js:72` |
| `upkeepSac` | `17_turns_ai.js:143` |
| `aiFixDeficit` (after each rebalance move and each sacrifice) | `17_turns_ai.js:203,213` |
| `aiBuild`, `aiUpgrade` | `07_structures.js:43,61` |
| AI summon | `17_turns_ai.js:311` |
| `flip()` — **creature branch only** | `14_spells_traps.js:125` |

> ### ⚠ Bug 1 — `flip()` skips `syncWorkers` for structures
> `flip()` (`14_spells_traps.js:110-127`) `return`s at line 117 for `ctype==='building'`, **before**
> the `syncWorkers(owner)` call at line 125. A face-down structure raised face-up therefore leaves
> the worker pools stale until the next `syncWorkers`. Latent today (no building cards exist in any
> deck — `CARD_REG` excludes them, `06_mana_workers.js:40`), but **fix it in the port**.

> ### ⚠ Bug 2 — `cleanup()` never re-syncs
> When a structure is razed or a creature dies mid-combat, the worker *figure* changes immediately
> (it is computed live) but the worker *pool* does not. Harmless in practice because harvest only
> happens at upkeep, after a `syncWorkers`. In C#, make the pool a pure projection recomputed on
> demand and the class of bug disappears.

### 6.5 Dead worker code — **do not port**

`workerCap`, `structSupport`, `monsterUpkeep` (`05_board_state.js:47-50`), `minionCount`,
`canTrain`, `enforceCap` (`06_mana_workers.js:12-22`), `trainVillager` (`14_spells_traps.js:128-133`).
These are the **previous** model (a single global worker cap you trained into). `enforceCap` and
`trainVillager` have **zero call sites**; `canTrain` is called only by `trainVillager`; `workerCap`
only by `canTrain`/`enforceCap`. The in-game "Train Worker" button was removed
(`17_turns_ai.js:221`: *"Train Worker removed — workers are now auto-derived"*).

---

## 7. Upkeep — explicit Move / Pay / Sacrifice settlement

### 7.1 Deficit arithmetic

```js
function zoneDeficit(owner,z){                                       // 05_board_state.js:84
  const paid = (G.P[owner].upaid||{})[z] || 0;
  return Math.max(0, Math.max(0, -rowWorkers(owner,z)) - paid);
}
function deficitRows(owner){ return ZONES.filter(w => zoneDeficit(owner,w) > 0); }   // :85  — order: back, front, center, raid
function totalDeficit(owner){ return ZONES.reduce((s,w) => s + zoneDeficit(owner,w), 0); }  // :86
```

`upaid` is **per-zone, per-turn, and reset at every turn start** (§5 step 4). A zone's *effective*
shortfall is its raw negative figure minus what has already been paid into it **this** upkeep.

### 7.2 Which creature is flagged next

```js
function upkeepOffender(){                                           // 17_turns_ai.js:95-100
  for(const z of ZONES){                                             // back, front, center, raid — in that order
    if(zoneDeficit('you',z) <= 0) continue;
    const cres = creaturesInRow('you',z).filter(r => !r.o.paid)
                   .sort((a,b) => (b.o.up||0) - (a.o.up||0));        // highest upkeep first
    if(cres.length) return cres[0];
  }
  return null;
}
```

`creaturesInRow(owner,which)` (`05_board_state.js:87-91`) walks `zoneKeys` and returns
`{which, key, i, o}` for every **non-worker** creature of `owner` in the zone — so for `raid` it
sweeps both enemy rows.

The settle menu is auto-popped on the first offender at turn start (`17_turns_ai.js:64`) and again
after every settle (`upkeepNext`, `:109-115`), and re-popped by the multiplayer snapshot adopter
(`41_mp_sync.js:55,57`) because adopting a snapshot wipes the menu.

### 7.3 The three settle actions

`upkeepPick(key,i)` (`17_turns_ai.js:116-125`) builds the menu for one of your creatures. Available
on **any** of your creatures during upkeep, in **any** row, even if that row is balanced.

| Action | Cost | Effect | Citation |
|---|---|---|---|
| **⤧ Move** | its move (a second move this upkeep also **taps** it, spending its whole turn) | `doMove` relocates one square in any direction (including diagonals, and into the enemy back row); then `syncWorkers('you')` and `upkeepNext()` | `16_movement.js:39-57` |
| **◆ Pay** | `cost = min(creature.up, zoneDeficit(zone))` from `P.mana` | `payAny('you',cost)`, `upaid[z] += cost`, `o.paid = true`. Refused with a hint if `mana < cost` (no partial pay). If `cost <= 0` the menu just closes. | `17_turns_ai.js:127-137` |
| **✖ Sacrifice** | the creature | cell set to `null`, `toGrave('you', o)`, `syncWorkers('you')`, `upkeepNext()`. **No mana refund. Not restricted to deficit zones.** | `17_turns_ai.js:138-144` |

The **Pay** button is only rendered when `payN > 0 && !o.paid`; it is rendered `disabled` when
`manaTotal('you') < payN` (`17_turns_ai.js:121`).

`upkeepPay` marks `o.paid = true` even when the creature's `up` exceeds the remaining zone deficit
(it pays only the capped amount). A creature can therefore be "settled" for less than its full
upkeep when it is the last one in a partially-covered zone.

### 7.4 Harvest is LOCKED until the workforce is settled

```js
window.doHarvest = function(){                                       // 17_turns_ai.js:147-174
  if(G.phase!=='upkeep' || G.turn!=='you' || G.busy || G.over) return;
  const owe = totalDeficit('you');
  if(owe > 0){                       // no silent auto-pay
    const off = upkeepOffender();
    if(off){ setHint('Shortfall ⚒'+owe+' unsettled…'); upkeepPick(off.key, off.i); return; }   // REFUSED
  }
  …
};
```

**The lock is on `upkeepOffender()`, not on `totalDeficit`.** If a shortfall exists but *no
settleable creature* remains anywhere (an "orphan" shortfall), Harvest proceeds and pays it out of
the harvest proceeds — this is deliberate anti-deadlock design.

```js
function orphanDeficit(owner){                                       // 17_turns_ai.js:103-107
  return ZONES.reduce((s,z) => {
    if(zoneDeficit(owner,z) <= 0) return s;
    const cres = creaturesInRow(owner,z).filter(r => !r.o.paid);
    return s + (cres.length ? 0 : zoneDeficit(owner,z));
  }, 0);
}
```

The Harvest **button** mirrors this: `locked = (totalDeficit('you') - orphanDeficit('you')) > 0`
(`12_render.js:20`) — presentation, but it encodes the same rule.

**How an orphan shortfall arises:** a zone whose net figure went negative purely from *structures*.
The only structure with negative support is the **Cannon Tower** (`sup: -2`). Build it in a row that
can afford it, then let the supporting structure be razed → that row is now at e.g. −2 with no
creature to move, pay, or sacrifice. (`placeRowOK` prevents *creating* that state, §10.4, but
combat can produce it.)

### 7.5 `doHarvest` — full algorithm

`src/js/17_turns_ai.js:147-174`. Order matters; note the deliberately stale `owe`.

1. Guard: `phase === 'upkeep' && turn === 'you' && !busy && !over`.
2. `owe = totalDeficit('you')` — **captured before harvesting**.
3. If `owe > 0` **and** `upkeepOffender()` is non-null → refuse, pop that creature's menu, return.
4. `sum = 0`. **For each zone in the fixed order `['back','front','center']`** (raid excluded):
   * `up = pool.filter(m => !m.tapped && !m.sick).length`
   * if `up <= 0` → `continue`
   * `total = up * minYield(zone)`; **`minYield(which)` returns `1` for every row**
     (`15_combat.js:145`, comment: *"every row harvests the same — no front/center bonus"*).
     `extractYield` likewise returns 1 (`12_render.js:455`).
   * tap **every non-sick worker** in the pool: `pool.forEach(m => { if(!m.sick) m.tapped = true; })`
   * `P.mana = min(99, P.mana + total)`; `sum += total`
5. If the captured `owe > 0` (i.e. a purely structural/orphan shortfall):
   * `pay = min(owe, P.mana)`; if `pay > 0` → `payAny('you', pay)`
   * for **every** zone with `zoneDeficit > 0`: `upaid[z] += that deficit`
     — **credited in full even when only partially paid**, so the turn can never dead-lock.
   * Log distinguishes fully-paid vs "the crews idle unpaid".
6. `setPhase('draw')`; clear `G.moveFrom`, `G.cardMenu`.
7. Log the harvest total; show the draw hint; `render()`.

**Harvest yield formula (final):** `◆ = Σ over {back, front, center} of (count of workers in that
zone that are neither `sick` nor `tapped`) × 1`.

### 7.6 AI upkeep settlement — `aiFixDeficit`

`src/js/17_turns_ai.js:177-219`. Runs inside `startTurn('foe')`, **before** the AI's harvest (which
happens at the top of `foeTurn`).

```js
const MOVE_ADJ = {back:['front'], front:['back','center'], center:['front'], raid:['center']};
```

**Phase 1 — rebalance by moving** (guard: 40 iterations)
1. `which = deficitRows(owner)[0]` (zone order back → front → center → raid).
2. Sort that zone's creatures by `up` descending; take the first.
3. For each candidate destination in `MOVE_ADJ[which]`, **skipping `'raid'`** (never rebalance
   *into* enemy territory — but note `MOVE_ADJ.raid = ['center']`, so a raiding creature *can*
   be pulled back to the center):
   * require `rowWorkers(owner, to) - o.up >= 0` (the destination must stay non-negative), then
   * `aiMoveCreature(owner, key, i, to)` — refuses if `o.moved && (o.moved2 || o.tapped)`
     (same two-move budget the player gets); tries destination slots `[i, i-1, i+1]` requiring the
     slot to exist (`slotExists`, center lanes only at 1/3/5), be empty, and be `adjacentK`
     (one real square). Second move sets `moved2 = true; tapped = true`.
4. `syncWorkers(owner)`; loop. Break if nothing moved.

**Phase 2 — sacrifice, only while the bill is unaffordable** (guard: 40)
`while (totalDeficit(owner) > manaTotal(owner))`: take the highest-`up` creature in the first
deficit zone, remove it, `toGrave`, `syncWorkers`. Break if the zone has no creatures.

**Phase 3 — pay the remainder**
`owe = totalDeficit(owner)`; if `owe > 0 && manaTotal(owner) >= owe`: `payAny(owner, owe)` and credit
`upaid[z] += zoneDeficit(z)` for every deficit zone.

If phase 2 broke early on an empty zone, the AI may end with an unpaid deficit and simply carries it.

Then `readyWorkers('foe')` runs again (`17_turns_ai.js:69`) so any workers created by the
rebalancing are harvest-ready.

---

## 8. Harvest — the three implementations

| Path | Where | Gated by deficit? | Notes |
|---|---|---|---|
| **Player** `doHarvest()` | `17_turns_ai.js:147` | ✔ (§7.4) | ⛏ button (`index.html:80`); advances `Upkeep → Draw` |
| **AI** inline loop | `17_turns_ai.js:273-281`, at the top of `foeTurn` | ✖ — unconditional | identical arithmetic (`ups.length * minYield(w)`), credits via `applyRes`, logs per row |
| **Multiplayer** `MPAPPLY.harvest` | `42_mp_apply.js:10-27` | ✔ but stricter: rejects unless `orphanDeficit('foe') >= owe && manaTotal('foe') >= owe` | mirrors `doHarvest` for the remote player; then `setPhase('draw')` |

`applyRes(base, owner, creature, type)` (`16_movement.js:183-186`) is the shared credit helper:
`P.mana = Math.min(99, P.mana + base)`. It also clears the dead `firstExtract` flag.

### 8.1 Legacy per-row harvest — **vestigial, do not port**

`harvestRow(which)` / `applyHarvest(which, alloc, total)` (`15_combat.js:146-163`) let you tap a
single row's worker stack to harvest just that row. Its only caller is `workerTokEl`
(`12_render.js:102`) inside `renderMinions` — and **`renderMinions` is never called** by `render()`
(`12_render.js:6-30` calls `renderRow ×4`, `renderCenter`, `renderHand`, `renderFoeHand`,
`renderCmdZone ×2`, `renderWalls`, `placeCardMenu` — no `renderMinions`). `workerChipRow`
(`12_render.js:214`) is likewise defined and never called; the live UI is `workerColumn`
(`12_render.js:238`, called at `:327`).

`harvestRow` also tests `G.deficit`, a flag that is **never assigned anywhere** (always `undefined`).

The multiplayer protocol still carries a `harvestRow` intent (`42_mp_apply.js:29-37`,
`43_mp_intents.js:19-21`) — dead weight from the same era.

Also fully dead: `doExtract` / `extractSel` / `doExtractAs` / `extractChoiceHTML`
(`15_combat.js:120-142`) because `canExtract()` hard-returns `false` (`12_render.js:408`,
comment: *"creatures no longer extract mana — only workers harvest their row"*), plus the
`#harvestPanel` DOM element and `hvCancel` (`17_turns_ai.js:175`).

---

## 9. Mana

### 9.1 The pool

```js
function manaTotal(o){ return G.P[o].mana; }                         // 06_mana_workers.js:5
function colorNeed(card){ return false; }                            // element no longer gates cost  :6
function canPay(o,card){ return G.P[o].mana >= card.c; }             // :7
function payAny(o,n){ const P=G.P[o]; const g=Math.min(P.mana,n); P.mana -= g; return g >= n; }   // :8
function payCost(o,card){ payAny(o, card.c); }                       // :9
function manaGlyph(t){ return '◆'; }                                 // :10
function extractColors(owner,which){ return []; }                    // :11
```

* **One generic pool per player.** Colored mana was removed. `card.color` / element is now purely a
  synergy + art + deck-legality attribute (`06_mana_workers.js:1-4`; deck legality: a card whose
  `color !== null` must match one of the commander's colors, `06_mana_workers.js:47-48,73`).
* **`payAny` is a partial payment**: it deducts `min(mana, n)` and *returns* whether the full amount
  was covered. **Every call site ignores the return value.** Every call site is preceded by its own
  affordability check, so this is safe today — but in C# make the pay operation atomic and
  fail-fast (`bool TrySpend(int n)`), never a silent partial debit.
* Income is clamped: `mana = min(99, mana + gain)` at every credit point
  (`17_turns_ai.js:5,160`; `15_combat.js:158`; `16_movement.js:184`; `42_mp_apply.js:23,35`).
  There is no lower-bound clamp because `payAny` cannot go below zero.

### 9.2 Every income source

| # | Source | Amount | When | Citation |
|---|---|---|---|---|
| 1 | **Worker harvest** | `+1` per non-sick, non-tapped worker, in each of back/front/center | player: ⛏ Harvest during Upkeep. AI: automatically at the top of its turn. | `17_turns_ai.js:154-161`, `:273-281`; `15_combat.js:145` |
| 2 | **Structure `eff:'mana'` yield** | `+val` per structure, every turn | `buildingUpkeep` at turn start (§5 step 8) | `17_turns_ai.js:5` |
| 2a | └ The Foundry | `+1` | | `03_cards_creatures.js:55` |
| 2b | └ Forge (per element) | `+1` | | `:70` |
| 2c | └ Keep (Foundry upgrade) | `+1` | | `:64` |
| 2d | └ Citadel (Keep upgrade) | `+2` | | `:65` |
| 2e | └ Grand Forge | `+3` | | `:71` |
| 3 | **Carry-over through the drain** | up to `vaultCap` (§9.5) | end of your turn | `17_turns_ai.js:33-41` |
| 4 | **Banked ◆ on a card** (`unit.bank`) | recovered only by playing a card on top of that unit | Action phase | `13_input.js:184-205` |
| 5 | **Invested ◆ on a face-down card** (`charge.inv`) | not recoverable as mana; converts into the flipped card, surplus becomes the new unit's `bank` | Action phase | `14_spells_traps.js:110-127` |

There is **no** per-turn baseline mana grant. `startTurn`'s comment says it outright:
*"no generic income — mana comes from worker harvest + forge yields"* (`17_turns_ai.js:51`).

### 9.3 Every cost the player can pay

| # | Cost | Amount | Phase | Citation |
|---|---|---|---|---|
| 1 | Summon a creature from hand | `card.c` | Action | `13_input.js:212-219` |
| 2 | Build a structure from the ⚒ Build menu | `def.c` | Action | `06_mana_workers.js:225` |
| 3 | Build a structure **card** from hand | `card.c` | Action | `13_input.js:207-211` — **unreachable today**: `CARD_REG` excludes buildings (`06_mana_workers.js:40`) and `deckOf` only makes creatures + spells (`:26-35`) |
| 4 | Upgrade a structure in place | target tier's `def.c` | Action | `07_structures.js:28` |
| 5 | Cast a spell | `card.c` | Action | `14_spells_traps.js:31` |
| 6 | **Set a creature/structure face-down** | **`◆1`** — becomes `charge.inv = 1`, banked toward the card's cost | Action | `13_input.js:226-234` |
| 7 | **Set a trap face-down** | **`◆1`** — *consumed*; the trap object has no `inv` field | Action | `13_input.js:220-225` |
| 8 | Pour mana into a face-down charge | any amount ≤ current mana | Action | `14_spells_traps.js:107` |
| 9 | Play a card **on top of** one of your banked cards | `card.c − min(occ.bank, card.c)`; surplus `occ.bank − card.c` carries onto the newcomer; the card underneath is destroyed (`toGrave`) with its summon mana lost | Action | `13_input.js:184-205` |
| 10 | **Upkeep ◆ Pay** (keep for an over-extended creature) | `min(creature.up, zoneDeficit(zone))` | Upkeep | `17_turns_ai.js:127-137` |
| 11 | **Structural (orphan) shortfall settled by Harvest** | `min(pre-harvest totalDeficit, mana after harvesting)` | Upkeep (inside `doHarvest`) | `17_turns_ai.js:162-169` |

AI-only equivalents (same costs, different driver): fuel a face-down charge to full
(`17_turns_ai.js:271-272`), build up to twice a turn + one upgrade (`:285-286`), cast one raze and
one burn (`:288-295`), set one trap for ◆1 (`:297-301`), summon greedily by descending cost
(`:303-312`).

**No card costs 0.** Creature costs run 1,1,2,2,3,4,5,6 per element pool; spells cost 1–3; the
cheapest structure is ◆2.

### 9.4 Two mana stores that survive the drain

Besides Mana Vaults, mana can be parked **on cards** and is then invisible to `drainMana` (which
only touches `P.mana`):

* `unit.bank` — created by (a) flipping a charge with surplus investment
  (`14_spells_traps.js:114,119-121`), or (b) the carry when a card is played on top of a banked card
  (`13_input.js:192`). It can be **moved between your own creatures/structures** at will via
  `startSendMana`/`doSendMana` (`14_spells_traps.js:72-80`) — no phase gate beyond
  `turn==='you' && !busy && !over`, so **this works during Upkeep too**. It is spendable only by
  playing a card on top of the holder. `applyUpgrade` explicitly preserves `bank`
  (`07_structures.js:16`).
* `charge.inv` — mana poured into a face-down card. Persists until the card is flipped or destroyed.
  Destroying a charge sends the card to the grave and the invested mana is simply lost.

**Design consequence:** `bank` + `inv` are an *uncapped, un-raidable* mana store that trivially
bypasses the vault economy. Flag for design review (§14).

Charge flip timing: `sick = (G.turnNo <= charge.setTurn)` (`14_spells_traps.js:120`) — a charge
flipped on a **later** turn than it was set produces a **battle-ready** creature; flipped on the
same turn it produces a summoning-sick one. That is the payoff for the ◆1 set + pour line.

### 9.5 End-of-turn drain and Mana Vaults

```js
function vaultCap(owner){                                            // 17_turns_ai.js:33
  return ownUnits(owner).filter(o => o.kind==='building' && o.eff==='vault')
                        .reduce((s,o) => s + (o.val||0), 0);
}
function drainMana(owner){                                           // :34-36
  const P = G.P[owner], cap = vaultCap(owner);
  const lost = Math.max(0, P.mana - cap);
  P.mana = Math.min(P.mana, cap);
  return {keep:P.mana, lost};
}
function endTurnDrain(owner){ … }                                    // :38-41 (adds the log line)
```

**Rule: unspent mana evaporates at the end of your own turn, except for what your Mana Vaults can
hold.** Vault capacities are **additive** across all your vaults, wherever they stand (own rows or
your units in the center — `ownUnits` walks all five rows filtered by owner tag).

| Vault tier | Cost | HP | Capacity `val` | ⚒ `sup` | Reached by |
|---|---|---|---|---|---|
| Mana Vault | ◆4 | 3000 | **◆4** | 0 | built from the menu (prereq: Foundry) |
| Grand Vault | ◆5 | 4500 | **◆10** | 0 | upgrading a Mana Vault in place |

(`03_cards_creatures.js:58,68`)

Drain call sites — **exactly three**, all at end-of-turn, never at turn start:

* player `endTurn` → `endTurnDrain('you')` (`17_turns_ai.js:232`)
* AI `foeTurn` tail → `endTurnDrain('foe')` (`:388`)
* MP `end` intent → `endTurnDrain('foe')` (`42_mp_apply.js:264`)

**Note the asymmetry:** the drain happens in `endTurn`/`foeTurn`, i.e. **only when a turn ends
normally**. If the game ends (`checkWin`) mid-turn no drain occurs — irrelevant, but be aware.

Vaults are also flagged in the design notes as *"un-retired raid targets"*: they are ordinary
structures with 3000/4500 HP, attackable and razable like any other.

---

## 10. Structures — the non-card mana sink

Structures are **not deck cards**. They are built from the commander's build menu, paying mana,
gated by a prerequisite tech tree (`03_cards_creatures.js:30-34`).

### 10.1 Complete structure table

`STRUCT_DEFS` (`03_cards_creatures.js:53-69`) plus the two generated per-element defs (`:70-71`).

| bid | Name | ◆ cost | ♥ HP | `eff` | `val` | ⚒ `sup` | `prereq` | `row` gate | `up2` (upgrade targets) | `from` (upgrade-only) |
|---|---|---|---|---|---|---|---|---|---|---|
| `foundry` | The Foundry | 2 | 3000 | mana | 1 | **+2** | — | — | `keep` | — |
| `forge` | *(per element, e.g. Emberforge)* | 3 | 2500 | mana | 1 | **+2** | `foundry` | — | `grandforge` | — |
| `encampment` | Encampment | 2 | 2500 | none | 0 | **+2** | `foundry` | — | `longhouse` | — |
| `longhouse` | Longhouse | 4 | 3000 | villager | 0 | **+3** | `foundry` | **front** | `barracks` | — |
| `vault` | Mana Vault | 4 | 3000 | vault | 4 | 0 | `foundry` | — | `grandvault` | — |
| `outpost` | Outpost | 2 | 3000 | none | 0 | **+1** | `forge` | — | `tower`, `bastion` | — |
| `bulwark` | Bulwark | 5 | 6000 | wall | 0 | **+1** | `forge` | — | — | — |
| `tower` | Cannon Tower | 4 | 4000 | damage | 1000 | **−2** | `forge` | — | — | — |
| `reliquary` | Reliquary | 5 | 3500 | revive | 0 | **+1** | `longhouse` | — | — | — |
| `grandforge` | Grand *(forge name)* | 6 | 3500 | mana | 3 | **+3** | `forge` | — | — | `forge` |
| `keep` | Keep | 3 | 5000 | mana | 1 | **+3** | — | **back** | `citadel` | `foundry` |
| `citadel` | Citadel | 4 | 7500 | mana | 2 | **+4** | — | **back** | — | `keep` |
| `barracks` | Barracks | 3 | 5000 | villager | 0 | **+4** | — | **front** | — | `longhouse` |
| `bastion` | Bastion | 3 | 9000 | wall | 0 | **+2** | — | — | — | `outpost` |
| `grandvault` | Grand Vault | 5 | 4500 | vault | 10 | 0 | — | — | — | `vault` |

Per-element forge names (`03_cards_creatures.js:23`):
fire → Emberforge, water → Tidewell, earth → Stonewell, wind → Galewell, forest → Thornwell,
electric → Stormforge, light → Dawnwell, dark → Gloomwell, divine → Empyreum.

### 10.2 Build menu ordering

```js
function buildList(ccId){                                            // 03_cards_creatures.js:73-79
  const cols = ccColors(ccId), out = [STRUCT_DEFS.foundry];
  cols.forEach(el => out.push(forgeDef(el)));
  out.push(encampment, longhouse, vault, outpost, bulwark, tower, reliquary);
  cols.forEach(el => out.push(grandForgeDef(el)));
  return out;
}
```

A dual commander therefore sees **two** forge entries and **two** grand-forge entries.
`grandforge` appears in the build list **and** as an upgrade target of `forge`, so a player who
already owns a Forge may either upgrade it (◆6, in place) or **build a second Grand Forge from the
menu** (also ◆6). This is almost certainly unintended; see §14.

### 10.3 `buildingUpkeep` effect dispatch

`src/js/17_turns_ai.js:2-11`, run at turn start.

| `eff` | Effect at turn start | Notes |
|---|---|---|
| `mana` | `P.mana = min(99, P.mana + val)` | Foundry/Forge/Keep/Citadel/Grand Forge |
| `damage` | `buildingDamage(owner, val, nm)` — hits the **first** enemy creature found scanning the enemy's `front`, then `center`, then `back` (slots 0→6), for `val` damage | Cannon Tower, 1000. **Scans `G.P[foe][w]`, so it misses an enemy creature standing in *your* rows.** |
| `revive` | `reviveFromGrave(owner)` — returns the **most recently graved non-token creature** to hand as a fresh `handcard`; **at most once per turn no matter how many Reliquaries** | `17_turns_ai.js:7,13-23` |
| `villager` | *nothing* | Longhouse/Barracks — the "trains a Worker" text is stale (§6.3) |
| `vault` | *nothing here* — read only by `vaultCap` at end of turn | |
| `wall` | *nothing* — Bulwark/Bastion are pure bodies | |
| `none` | *nothing* | Encampment, Outpost |
| `command` | *nothing* | legacy CC record; command centers were removed (`04_cards_leaders.js:25`) |

Iteration order is `P.front` → `P.back` → `G.center`(owner-filtered).
**Port note:** the `front`/`back` loops do *not* check `o.owner === owner`. Safe today (structures
never move and are only ever placed in their owner's rows), but **always filter by owner** in C#.

### 10.4 Build legality

```js
function ownBuildings(owner){ return ownUnits(owner).filter(o => o.kind==='building' && !o.cc); }   // 06:188
function bidLineage(b){ /* b.bid, then walk def.from up to 8 levels */ }                            // 06:191
function hasBuild(owner,bid){ return ownBuildings(owner).some(b => bidLineage(b).indexOf(bid) >= 0); }
function prereqMet(owner,def){ return (def.prereq||[]).every(p => hasBuild(owner,p)); }             // 06:193
function placeRowOK(owner,which,def){ return (def.sup||0) >= 0 || (rowWorkers(owner,which) + (def.sup||0)) >= 0; }  // 06:196
function hasPlacement(owner,def){ return ['back','front','center'].some(w => { const a=cellArr(owner,w); return a && a.some(x=>!x) && placeRowOK(owner,w,def); }); }
function canBuild(owner,def){ return manaTotal(owner) >= def.c && prereqMet(owner,def) && hasPlacement(owner,def); }  // 06:198
```

* **`bidLineage`** is the tech-tree keystone: an upgraded tier still satisfies the prereqs its base
  unlocked. A Keep still counts as a Foundry; a Grand Forge still counts as a Forge. The walk
  follows `def.from` and is capped at 8 hops.
* **`placeRowOK`** is the only guard on worker-costing structures: a structure with negative `sup`
  (only the Cannon Tower, −2) may be placed only in a row that stays ≥ 0 afterwards. Positive-`sup`
  structures may go anywhere.
* **Placement geometry** (`placeBuild`, `06_mana_workers.js:221-227`): your `back` and `front` rows,
  plus the center's **non-lane** slots (columns 0/2/4/6). Center lanes (1/3/5) are for creatures
  only; `centerSlotOK` enforces this both ways (`01_core_defs.js:7`).
* **Payment happens after all checks**: `payAny('you', def.c)` then place then
  `afterDeploy('you')` → `syncWorkers`.

### 10.5 In-place upgrades

```js
function upgradeWhy(owner,o,key,def){                                // 07_structures.js:9-14
  if(def.row && whichOf(key) !== def.row) return 'only in your back/front row';
  if(manaTotal(owner) < def.c) return 'need ◆'+def.c;
  if((def.sup||0) < 0 && (rowWorkers(owner, whichOf(key)) - (o.sup||0) + (def.sup||0)) < 0)
    return 'row has no ⚒ to spare';
  return '';
}
function applyUpgrade(o,def){                                        // :16-22
  o.bid=def.bid; o.nm=def.nm; o.eff=def.eff; o.val=def.val||0; o.sup=def.sup||0; o.ic=def.ic;
  const dmg = Math.max(0, (o.maxh ?? def.h) - o.h);                  // damage carries through
  o.maxh = def.h; o.h = Math.max(1, def.h - dmg);
  o.c = def.c; o.art = def.art;
  if(def.color) o.color = def.color;
}
```

* Same unit object: **`id`, `owner`, `bank`, and its board tile are preserved.**
* **Upgrading repairs nothing.** Accumulated damage carries across the rebuild; the structure gains
  only the new tier's *extra* max HP, and its HP floors at 1.
* Row gates: Keep/Citadel are **back**-row-only; Barracks is **front**-row-only; Longhouse is
  **front**-row-only (it is `row:'front'` in `STRUCT_DEFS` but is *built*, not upgraded into — note
  `placeBuild` **does not check `def.row`**, so the Longhouse's row gate is not enforced at build
  time, only at upgrade time. See §14).
* `upgradeStruct` (`07_structures.js:23-31`): `payAny` → `applyUpgrade` → `syncWorkers` →
  `afterDeploy` (a second `syncWorkers`) → `render` → `checkWin`. Gated on `acting()`.
* Command centers (`o.cc`) can never be upgraded — but no CC objects exist any more.

### 10.6 AI build policy

`aiBuild` (`07_structures.js:50-66`) walks `buildList` in order and builds the **first** affordable,
prereq-met, placeable entry, respecting per-bid caps:

```js
const CAP = {foundry:1, encampment:1, longhouse:1, vault:1, outpost:1, bulwark:1, tower:2, reliquary:1};
```

An upgraded tier still counts toward its base's cap (via `bidLineage`). One Forge (or its Grand
upgrade) per color; one Grand Forge per color. Placement: first of `['back','front']` with a free
deploy slot that satisfies `placeRowOK`, choosing the column by `aiPickDeploySlot`
(`16_movement.js:20-23`: front prefers `[3,4,2,5,1,6,0]`, back prefers `[2,4,3,1,5,0,6]`).
Called up to **twice** per AI turn (`17_turns_ai.js:285`). `aiUpgrade` then upgrades **one**
eligible structure, first-affordable-target-in-chain-order (`07_structures.js:38-48`, `:286`).

---

## 11. Draw

```js
window.youDeckClick = function(){                                    // 17_turns_ai.js:74-77
  if(G.turn==='you' && !G.busy && !G.over && G.phase==='draw'){ doDraw(); return; }
  openViewer('deck','you');                                          // any other time: browse the deck
};
function doDraw(){                                                   // :78-84
  if(G.turn!=='you' || G.busy || G.over || G.phase!=='draw') return;
  if(G.P.you.deck.length){ drawCard('you'); log('You draw a card.'); }
  else log('Your deck is empty — nothing to draw.');
  setPhase('action');                                                // ADVANCES REGARDLESS
  defaultHint(); render();
}
```

* The player draws by **clicking their own deck pile** during the Draw phase. There is no auto-draw
  and no other button. The `phase-draw` body class drives a deck pulse and keeps the castle wall
  open so the deck is reachable — **presentation** (`12_render.js:70-73`).
* **There is no deck-out / mill loss condition.** An empty deck simply draws nothing and the phase
  advances. The only loss condition is `life <= 0` (`17_turns_ai.js:392-407`).
* `drawCard(o)` pops from the **end** of the deck array (`deck.pop()`) and pushes a `handcard`
  projection (`11_deck_builder.js:250-251`).
* The **AI does not have a Draw phase**: it draws inside `startTurn` (`17_turns_ai.js:68`).
  The **multiplayer remote player does** (`42_mp_apply.js:57-60`).
* Opening hand: 4 cards, dealt in `startGame` before the first Upkeep (`11_deck_builder.js:247-248`).

---

## 12. End turn

`endTurn()` — `src/js/17_turns_ai.js:222-243`, bound to `#endBtn` at `:220`.

1. Guard `turn==='you' && !busy && !over`.
2. Phase bounce-backs (§4.2): `draw` → re-show draw hint; `upkeep` → re-show upkeep hint;
   anything other than `action` → return.
3. If `CMB.hasDecls()` (attack declarations pending) → refuse and re-hint (`15_combat.js:231`).
4. Clear `G.sel`, `G.atk`, `G.moveFrom`, `G.moveMana`.
5. `setPhase('end')`, log the End-phase banner.
6. `endPhaseEffects('you')` — **an empty stub today** (`17_turns_ai.js:245`,
   *"reserved for end-of-turn keyword triggers"*). Keep the hook in C#.
7. **`endTurnDrain('you')`** — the mana drain (§9.5).
8. Hand-off:
   * multiplayer: (guest sends the `end` intent) → `startTurn('foe')` immediately, no `G.busy` latch;
   * solo: `G.busy = true`; after **380 ms** → `startTurn('foe')`; after a further **650 ms** →
     `foeTurn()`. **These are pacing delays (presentation).** The rules engine must be synchronous;
     put the beats in the view layer.

---

## 13. Multiplayer / determinism notes (deferred, but design for it)

MP is deferred, but the JS already proves what a host-authoritative layer needs, and the C# rules
core must not make it harder:

* **Every economy mutation is re-implemented host-side as a validated intent**
  (`42_mp_apply.js`): `harvest`, `harvestRow`, `sac`, `pay`, `draw`, `move`, `place`, `pour`,
  `flip`, `sendmana`, `cast`, `build`, `upgrade`, `attack`, `end`. Each begins with a phase guard
  (`if(!foesTurn() || G.phase!=='action') return bad(m.q,'phase')`). Model these as a closed set of
  **command records** in C# and drive the local player, the AI, and the network through the same set.
* **The serialised state** (`MPSER.pSnap`, `41_mp_sync.js:29-31`) is exactly:
  `color, cc, life, mana, cmana, hand, deck, grave, front, back, min, firstExtract, villagerUsed,
  upaid` per player, plus `turn, over, turnNo, phase, uid, center`. Note `min` (the derived worker
  pools) **is serialised** rather than recomputed. In C# prefer recomputing the pool from the board
  and serialising only the tapped/sick bits, or make the pool authoritative — but be explicit.
  Also note `uid` (the monotonic object-id counter) is part of the snapshot; keep an equivalent.
* **`G.phase` is a single shared field**, so a snapshot carries the *active* player's phase. Adopting
  a snapshot calls `setPhase(S.phase)` and re-pops the upkeep settle menu if needed
  (`41_mp_sync.js:50,55,57`).
* **Randomness**: deck shuffles use `Math.random` (`06_mana_workers.js:34,87`), as do several AI
  choices (`17_turns_ai.js:259,263`). For determinism, thread a seeded PRNG through the rules core
  and forbid ambient RNG.
* **No floating point anywhere in the economy** — all integers. Keep it that way.

---

## 14. Bugs, oddities and design questions to resolve before implementing

1. **`flip()` skips `syncWorkers` for structures** (`14_spells_traps.js:117`) — §6.4 Bug 1.
2. **`cleanup()` never re-syncs worker pools** — §6.4 Bug 2. Solved for free if the pool becomes a
   pure projection.
3. **AI turns run with `G.phase === 'end'`** — §4.4. Give the AI a real phase sequence.
4. **`payAny` is a silent partial debit.** Replace with `TrySpend(int) → bool`.
5. **`placeBuild` does not enforce `def.row`.** The Longhouse declares `row:'front'` but can be
   built in the back row; only *upgrades* check the row gate (`07_structures.js:10`). Decide whether
   the gate applies to building too.
6. **`grandforge` is both a build-menu entry and an upgrade target** (§10.2). Almost certainly a
   duplicate path; pick one.
7. **`bank`/`inv` mana bypasses the vault economy entirely** (§9.4) — an uncapped store that also
   moves freely between your own cards during upkeep. Decide whether that is intended.
8. **`buildingDamage` scans only the enemy's own rows** (`17_turns_ai.js:28`), so a Cannon Tower
   ignores an enemy creature standing in your front row or the center-as-your-side. Probably a bug.
9. **`buildingUpkeep` does not owner-filter `P.front`/`P.back`** (`17_turns_ai.js:9`). Harmless today,
   fragile forever.
10. **Harvest taps `if(!m.sick)` across the whole pool**, not just the counted `up` set
    (`17_turns_ai.js:159`). Equivalent today (a non-sick worker is either untapped-and-counted or
    already tapped) but write it as "tap exactly the workers you counted".
11. **The stale `owe` in `doHarvest`** (`17_turns_ai.js:149` captured, used at `:162-168`). Confirm
    the intent: the structural bill is sized *before* harvesting and paid *after*.
12. **`upkeepPay` marks `paid` even for a partial payment** (§7.3). Confirm.
13. **Mana cap 99** — arbitrary and undocumented in design notes; confirm it should survive the port.
14. **The in-game rules panel is stale on the single most important economy rule.**
    `index.html:129` still reads: *"Mana persists between turns — it keeps building until you spend
    it (or spend it keeping over-extended lines fed)."* That was true before Combat v3; the code now
    drains everything above `vaultCap` at end of turn (§9.5). The rules panel also still describes
    "the command center itself" as an attack target (`index.html:131`) though CCs were removed
    (`04_cards_leaders.js:25`), and describes the Longhouse tier as "a Barracks might be ⚒+3"
    (`:128`) when Barracks is ⚒+4. **The code is authoritative; the rules panel text must be
    rewritten for the port.**
15. **Dead UI element:** `#conscriptBtn` ("⚒ Train", `index.html:82`) is unconditionally hidden every
    render (`12_render.js:22`) — the removed worker-training button. Do not recreate it.
    `#harvestPanel` (`index.html:144`) and `hvCancel` are likewise vestiges of the removed
    colour-allocation pop-up.

---

## 15. Suggested C# shape

All of the following live in the **pure, UI-free, deterministic rules assembly** — no
`UnityEngine`, no `MonoBehaviour`, unit-testable outside Unity.

### 15.1 Enums

```csharp
public enum TurnPhase { Upkeep, Draw, Action, End }
public enum PlayerSide { You, Foe }                     // rename to P1/P2 for the netcode
public enum BoardRow { FoeBack, FoeFront, Center, YouFront, YouBack }   // ordinal == distance metric
public enum WorkerZone { Back, Front, Center, Raid }     // enumeration order IS the settle order
public enum StructureEffect { None, Mana, Villager, Damage, Wall, Vault, Revive, Command }
public enum CellKind { Empty, Creature, Building, Charge, Trap }
public enum UpkeepSettleAction { Move, Pay, Sacrifice }
```

### 15.2 Immutable data (ScriptableObject-backed at the edge, plain records inside)

```csharp
public sealed record ElementDef(string Id, string Name, int StartingHp, int StartingWorkers);
public sealed record CommanderDef(string Id, string Name, int Hp, int Workers, IReadOnlyList<string> Colors);

public sealed record StructureDef(
    string Bid, string Name, int ManaCost, int Hp,
    StructureEffect Effect, int Value,          // Value = mana yield / vault capacity / tower damage
    int WorkerSupport,                          // may be negative (Cannon Tower = -2)
    IReadOnlyList<string> Prereqs,
    WorkerZone? RowGate,                        // Back / Front, or null
    IReadOnlyList<string> UpgradeTargets,       // up2
    string UpgradedFrom,                        // from — null for menu-buildable base tiers
    string ColorId);                            // null = colourless; set for forges
```

### 15.3 Mutable runtime state

```csharp
public sealed class PlayerState {
    public int Mana;                            // THE generic pool, clamped to ManaCap on credit
    public int Life;
    public string CommanderId;
    public readonly int[] UpkeepPaid = new int[4];   // indexed by WorkerZone — reset every turn start
    public List<Card> Hand, Deck, Graveyard;
    public BoardCell[] Back = new BoardCell[7], Front = new BoardCell[7];
    public WorkerPool BackWorkers, FrontWorkers, CenterWorkers;   // no Raid pool — by design
}

public sealed class GameState {
    public int TurnNumber;                      // increments once per PLAYER turn
    public PlayerSide ActiveSide;
    public TurnPhase Phase;
    public bool IsOver;
    public BoardCell[] Center = new BoardCell[7];
    public PlayerState You, Foe;
    public IDeterministicRng Rng;
    public bool CombatDeclared => PendingDeclarations.Count > 0;   // drives the "Combat" display label
}
```

### 15.4 Services

```csharp
public interface IEconomyRules {
    int RowWorkers(GameState g, PlayerSide s, WorkerZone z);       // may be negative
    int ZoneDeficit(GameState g, PlayerSide s, WorkerZone z);
    int TotalDeficit(GameState g, PlayerSide s);
    int OrphanDeficit(GameState g, PlayerSide s);                  // shortfall with no settleable creature
    bool HarvestUnlocked(GameState g, PlayerSide s);               // == FindNextOffender(...) is null
    UnitRef? FindNextOffender(GameState g, PlayerSide s);          // zone order, then highest Upkeep first
    int VaultCapacity(GameState g, PlayerSide s);
    int HarvestYield(GameState g, PlayerSide s);                   // Σ ready workers × WorkerYield (=1)
}

public interface IPhaseMachine {
    void BeginTurn(GameState g, PlayerSide s);                     // the §5 ordered sequence
    bool TryAdvance(GameState g, TurnPhase from, TurnPhase to, out string refusal);
}

// One closed command set drives local player, AI, and (later) network.
public abstract record EconomyCommand;
public sealed record HarvestCommand()                              : EconomyCommand;
public sealed record DrawCommand()                                 : EconomyCommand;
public sealed record UpkeepPayCommand(UnitRef Unit)                : EconomyCommand;
public sealed record UpkeepSacrificeCommand(UnitRef Unit)          : EconomyCommand;
public sealed record MoveCommand(UnitRef Unit, CellRef To)         : EconomyCommand;
public sealed record BuildCommand(string Bid, string ColorId, CellRef At) : EconomyCommand;
public sealed record UpgradeCommand(UnitRef Structure, string Bid) : EconomyCommand;
public sealed record PlayCardCommand(int HandIndex, PlayMode Mode, CellRef At) : EconomyCommand;
public sealed record PourCommand(UnitRef Charge, int Amount)       : EconomyCommand;
public sealed record TransferBankCommand(UnitRef From, UnitRef To) : EconomyCommand;
public sealed record EndTurnCommand()                              : EconomyCommand;
```

### 15.5 Constants

```csharp
public static class EconomyConstants {
    public const int BoardColumns   = 7;
    public const int DeckSize       = 40;
    public const int MaxCopies      = 3;
    public const int OpeningHand    = 4;
    public const int ManaCap        = 99;      // applied on every credit
    public const int WorkerYield    = 1;       // per ready worker, EVERY zone — no positional bonus
    public const int SetFaceDownCost = 1;      // creature/structure set → becomes charge.inv
    public const int SetTrapCost     = 1;      // consumed outright
    public const int MaxOverchargeBank = 3;
    public const int UpkeepMovesPerCreature = 2;   // 2nd upkeep move also taps
    public static readonly int[] CenterLanes = { 1, 3, 5 };
    public static readonly WorkerZone[] SettleOrder =
        { WorkerZone.Back, WorkerZone.Front, WorkerZone.Center, WorkerZone.Raid };
}
```

---

## 16. Quick-reference: the load-bearing ordering, in one list

1. `TurnNumber++`, active side set.
2. **`UpkeepPaid` cleared for all four zones.**
3. All the active side's creatures refreshed (`sick`, `tapped`, `moved`, `moved2`, `paid`, `blocked`, discharge).
4. Chrysalis ticks (may hatch; always re-sicks).
5. Overcharge ticks (`+1`, cap 3).
6. **Structure upkeep effects: mana yields → tower damage → reliquary revive (once).**
7. `cleanup()` sweeps deaths caused by step 6.
8. **Workers re-derived from the board.**
9. **Workers settled (un-sicked, untapped) — this is the only time it happens.**
10. Phase set to **Upkeep**; the first over-extended creature's settle menu opens automatically.
11. Player settles every flagged creature by **Move**, **◆ Pay**, or **✖ Sacrifice**
    (each Move/Sacrifice re-derives workers; any worker created now stays sick this turn).
12. **⛏ Harvest** — refused while any settleable offender remains. Harvest credits
    `Σ ready workers × 1` into the generic pool, then pays any purely structural shortfall out of
    the proceeds, then advances to **Draw**.
13. **Draw** — click the deck. Advances to **Action** even on an empty deck.
14. **Action** — everything else: summon, set, cast, build, upgrade, pour, move, attack.
15. **End Turn** — refused while combat declarations are pending. Sets **End**, runs the
    end-phase hook, then **drains all unspent mana down to the total Mana Vault capacity**,
    then hands off.
