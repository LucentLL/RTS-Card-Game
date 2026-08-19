# 07 — Turn Machine & AI Opponent

**Subsystem:** `ai` (turn sequencing, phase machine, upkeep settlement, end-of-turn drain, and the
complete singleplayer AI opponent policy).

**Primary source:** `src/js/17_turns_ai.js` (407 lines).

**Mandatory cross-file sources** (behaviour for this subsystem is spread across the shared global
scope; every one of these files mutates or overrides something owned by `17_turns_ai.js`):

| File | What it contributes to this subsystem |
|---|---|
| `src/js/01_core_defs.js` | `SLOTS`, `CENTER_LANES`, `isLane`, `BASE_COL`, element table |
| `src/js/03_cards_creatures.js` | `STRUCT_DEFS`, `forgeDef`, `grandForgeDef`, `buildList` — the AI's build menu and its ordering |
| `src/js/04_cards_leaders.js` | `CCS` (commander HP / starting workers), the `G` root state object |
| `src/js/05_board_state.js` | `ROWS`, `rowArr`, `rowIdx`, `ZONES`, `zoneKey`, `rowWorkers`, `syncWorkers`, `readyWorkers`, `zoneDeficit`, `deficitRows`, `totalDeficit`, `creaturesInRow` |
| `src/js/06_mana_workers.js` | `manaTotal`, `canPay`, `payAny`, `payCost`, `mkCre`, `mkVil`, `mkBld`, keyword upkeep hooks (`chrysalisUpkeep`, `overchargeUpkeep`), `canBuild`, `placeRowOK` |
| `src/js/07_structures.js` | **`aiBuild`**, **`aiUpgrade`**, `toGrave`, `buildingLoc`, `canUpgradeTo`, `applyUpgrade` |
| `src/js/09_game_start.js` | `startGame` — the initial turn-machine state (turn 1 bypasses `startTurn`) |
| `src/js/12_render.js` | `renderPhaseTrack`, `zoneForRow`, the Harvest-button lock, `selCres`/`canAttack` |
| `src/js/13_input.js` | Player action gating by phase (`onHand`, `onCell`), `spellRec` |
| `src/js/14_spells_traps.js` | `resolveSpell`, `findArmedTrap`, `flip`, the original `playerTrapOnSummon` (**replaced** by RESP) |
| `src/js/15_combat.js` | **`aiChooseInterceptors`** (the AI's blocking policy), `eligibleInterceptors`, `focusFire`, `applyDmg`, `resolveCombat`, `CMB.pairFight`, `CMB.targetFight`, `springAttackTrap`, `provokeFaceDown`, `springTrap`, `cleanup` |
| `src/js/16_movement.js` | `adjCells`/`adjacentK`, `slotExists`, `freeDeploySlot`, **`aiPickDeploySlot`**, `askBlock`, `askAbsorb`, `askRetaliate` |
| `src/js/22_fx_wrappers.js` | **Monkey-patches** `startTurn`, `checkWin`, `doHarvest`, `aiBuild`, `aiMoveCreature`, `applyRes`, `drawCard`, `render`, `resolveCombat`, `toGrave`, `applyDmg` (presentation only — but see §14 for a real argument-signature bug) |
| `src/js/30_resp.js` | `RESP.defendWindow` / `RESP.actingGate` / `RESP.springAttackTrapRef`; **replaces** `playerTrapOnSummon` wholesale. Injects blocking pauses into the AI turn. |
| `src/js/41–44 (MP)` | Disable the AI entirely and re-drive the turn machine from network intents |
| `src/js/10_menus_campaign.js` | `campResolve` — where `checkWin` routes a campaign duel |

Statistics used throughout: creature attack values are 500–4500, creature HP 500–4000, structure HP
2500–9000, player life 10000, mana is a small integer (0–99). Keep this scale in mind — several AI
thresholds are written against a pre-rescale (1–10) stat scale and are now **effectively constant**
(see §11.7 and §18).

---

## 1. Scope boundary

This document owns:

* the **phase machine** (`upkeep → draw → action(+combat) → end`) and the ply counter;
* **`startTurn` / `endTurn` / `foeTurn`** — turn entry, hand-off, and the AI turn coroutine;
* **start-of-turn structure effects** (mana yield, tower fire, Reliquary revive);
* the **worker-shortfall settlement (upkeep)** flow for the human player and its AI counterpart;
* the **harvest** step;
* the **end-of-turn mana drain** and the Mana Vault carry-over;
* **every AI decision** and its heuristic;
* **`checkWin`** and match termination.

It does **not** own: combat damage resolution (see the combat spec — `15_combat.js`), movement
legality (`16_movement.js`), card/keyword data, or the worker-derivation model itself
(`05_board_state.js`). Those are referenced here only where the turn machine calls into them.

---

## 2. State owned by the turn machine

### 2.1 Global game state (`G`) — `04_cards_leaders.js:214-223`

| Field | Type (JS) | Meaning | Written by |
|---|---|---|---|
| `G.turn` | `'you' \| 'foe'` | Side to act | `startGame`, `startTurn`, MP adopt |
| `G.busy` | bool | Input latch — true while the AI acts or an animation/window runs | `endTurn`, `foeTurn`, `RESP.actingGate`, `CMB._resolveNow` |
| `G.over` | bool | Match finished | `checkWin`, `doSurrender` |
| `G.turnNo` | int | **Ply** counter (increments once per half-turn), starts at 1 | `startTurn` (`G.turnNo++`) |
| `G.phase` | `'upkeep'\|'draw'\|'action'\|'end'` | Current phase | **only** `setPhase` |
| `G.upkeep` | bool | Mirror of `phase === 'upkeep'` | **only** `setPhase` |
| `G.decls` | array | Player's committed attack declarations awaiting Resolve | `CMB.declare`, `startTurn` (cleared) |
| `G.atk` | array of `{k,i}` | Player's current attacker *selection* (pre-declaration) | input layer |
| `G.sel`, `G.moveFrom`, `G.moveMana`, `G.cardMenu`, `G.build`, `G.minSel` | — | Transient UI selections, all cleared at `startTurn` / `endTurn` | various |

Vestigial fields that must **not** be ported: `P.firstExtract` (written, never read for any
decision), `P.villagerUsed` (never read), `G.powerMode` (never assigned), `G.deficit` (read once at
`15_combat.js:149`, never assigned → always falsy), `P.cmana` (seeded, inert — colored mana was
removed).

### 2.2 Per-player state relevant here

| Field | Meaning |
|---|---|
| `P.mana` | Single generic mana pool, hard-capped at **99** on every credit path |
| `P.life` | Stronghold life pool (starts at `CCS[cc].hp` = **10000**) |
| `P.upaid` | `{back, front, center, raid}` — mana already paid this upkeep against each zone's shortfall. **Reset to all-zero at every `startTurn`.** |
| `P.min` | `{back:[], front:[], center:[]}` worker pools (derived, not owned here) |
| `P.hand`, `P.deck`, `P.grave` | Deck is a **stack**: `drawCard` uses `deck.pop()` (`11_deck_builder.js:250`) |

### 2.3 Per-unit turn flags reset at `startTurn` (`17_turns_ai.js:53`)

```
ownUnits(owner).forEach(o => { if (o.kind === 'creature') {
  o.sick = false; o.tapped = false; o.moved = false;
  o.moved2 = false; o.paid = false; o.blocked = false; o._dis = 0;
}});
```

`ownUnits(owner)` (`05_board_state.js:46`) walks **all five rows** and filters by `o.owner === owner`
— so a creature standing behind enemy lines still refreshes with its controller.

| Flag | Meaning |
|---|---|
| `sick` | Summoning sickness — may not attack (may still move and may still block) |
| `tapped` | Spent its action this turn |
| `moved` | Has used its one move |
| `moved2` | Has used a *second* (upkeep-only) move — which also taps it |
| `paid` | Its keep has been explicitly paid this upkeep (blocks a second `upkeepPay`) |
| `blocked` | Has already interposed this turn (blocking is once-per-turn, independent of `tapped`) |
| `_dis` | Overcharge discharge bonus applied for this strike only |

---

## 3. The phase machine

`17_turns_ai.js:43-48`

```js
const PHASE_ORDER = ['upkeep','draw','action','end'];
const PHASE_LABEL = {draw:'Draw', upkeep:'Upkeep', action:'Action', combat:'Combat', end:'End'};
function setPhase(p){ G.phase = p; G.upkeep = (p === 'upkeep'); }
function acting(){ return G.turn==='you' && !G.busy && !G.over && G.phase==='action'; }
function shownPhase(){ return (G.phase==='action' && (G.atk.length || (G.decls && G.decls.length))) ? 'combat' : G.phase; }
```

* `'combat'` is **not** a real phase — it is a *display* sub-state of `action`, entered as soon as the
  player has either selected attackers (`G.atk`) or committed declarations (`G.decls`).
* `setPhase` is the **only** writer of `G.phase` / `G.upkeep` (the sole exception is `startGame`
  `09_game_start.js:5`, which assigns both directly and then immediately calls `setPhase('upkeep')`
  at line 18 — redundant, not a second code path).
* `acting()` is the master gate for every discretionary player action (build menu
  `06_mana_workers.js:200`, structure upgrade `07_structures.js:24`, End Turn button enablement
  `12_render.js:15`).

### 3.1 Legal phase transitions

| From | Trigger | To | Source |
|---|---|---|---|
| *(match start)* | `startGame` | `upkeep` (player) | `09_game_start.js:18` |
| `upkeep` | `doHarvest()` succeeds | `draw` | `17_turns_ai.js:170` |
| `draw` | `doDraw()` (deck click) | `action` | `17_turns_ai.js:82` |
| `action` | `endTurn()` (no pending declarations) | `end` | `17_turns_ai.js:230` |
| `end` | 380 ms later, `startTurn('foe')` | *(unchanged — see §3.2)* | `17_turns_ai.js:239` |
| *(AI turn ends)* | 650 ms later, `startTurn('you')` | `upkeep` | `17_turns_ai.js:389`, `:61` |

There is **no** way to skip Upkeep or Draw: `endTurn` short-circuits and re-shows the phase hint if
`G.phase` is `'draw'` or `'upkeep'` (`17_turns_ai.js:224-225`).

### 3.2 CRITICAL: the AI never enters the phase machine

`startTurn('foe')` in solo play takes the third branch (`17_turns_ai.js:67-70`), which **does not
call `setPhase`**. Therefore, for the entire duration of the AI turn:

* `G.phase === 'end'` (left over from the player's own `endTurn`);
* `G.upkeep === false`;
* `G.busy === true`.

This is load-bearing: it is what makes the board inert during the AI turn
(`13_input.js:97`, `12_render.js:412` both early-return on `phase==='draw'||phase==='end'`) and what
blanks the phase tracker (`12_render.js:62`: `yours = G.turn==='you' && !G.over`).

> **Port decision required.** In C# the AI turn should run through the *same* phase machine
> (`Upkeep → Draw → Action → End`) rather than leaving the phase stuck at `End`. The gating that the
> JS gets "for free" from `phase==='end'` must be replaced by an explicit
> `IsInteractive(currentPlayer)` predicate. Keep `G.busy` as an explicit `InputLocked` flag.

---

## 4. `startTurn(owner)` — the ordered start-of-turn algorithm

`17_turns_ai.js:49-71`. **Order is normative.**

1. `G.turnNo++` — ply counter.
2. `G.turn = owner`.
3. Clear transient UI state: `G.cardMenu = null`, `G.moveMana = null`, `G.decls = []`.
4. `P.firstExtract = true` *(vestigial — do not port)*.
5. `P.upaid = {back:0, front:0, center:0, raid:0}` — **last turn's keep payments expire**; shortfalls
   are re-settled from scratch every upkeep.
6. Reset all of `owner`'s creature turn flags (§2.3).
7. `chrysalisUpkeep(owner)` (`06_mana_workers.js:144`) — Chrysalis creatures gain `grow` counters;
   at `cnt >= hatch` they morph into `into` (name/atk/maxHp/hp/upkeep/firstStrike/keyword swapped)
   and are re-made **summoning-sick**; otherwise they are re-made sick (cocoons can never attack).
8. `overchargeUpkeep(owner)` (`06_mana_workers.js:154`) — every Overcharge creature banks
   `oc = min(3, oc + 1)`.
9. `buildingUpkeep(owner)` — §5.
10. `cleanup()` (`16_movement.js:193`) — sweep anything a damage tower just killed, firing death
    keywords; loops until stable, guard 40 iterations.
11. `syncWorkers(owner)` (`05_board_state.js:71`) — rebuild each of the three worker pools to
    `max(0, rowWorkers(owner, which))`, popping surplus and pushing new **sick** workers.
12. `readyWorkers(owner)` (`05_board_state.js:81`) — clear `sick`/`tapped`/`moved` on every worker in
    `back`/`front`/`center`. *This runs only here, which is why workers a structure adds mid-turn stay
    sick until the following turn.*
13. Branch on `owner`:
    * **`owner === 'you'`** → `setPhase('upkeep')`; log the Upkeep banner; `upkeepHint()`; then
      `const off = upkeepOffender(); if (off) upkeepPick(off.key, off.i);` — auto-opens the
      Move/Pay/Sacrifice menu on the first over-extended creature.
    * **MP active and `MP.started`** → `setPhase('upkeep')` only; the remote player drives `foe`
      through intents. **No AI.**
    * **else (solo AI)** → `drawCard('foe')`; `aiFixDeficit('foe')`; `readyWorkers('foe')`.

### 4.1 Asymmetries between the two branches (must be decided in the port)

| Aspect | Player (`you`) | AI (`foe`) |
|---|---|---|
| Draw | Explicit, in the Draw phase, by clicking the deck | **Automatic, inside `startTurn`**, before anything else |
| Shortfall settlement | Explicit per-creature Move/Pay/Sacrifice, gated by the Harvest button | `aiFixDeficit` — automatic move → sacrifice → pay |
| Post-settlement `readyWorkers` | **Not** re-run — workers created by a sacrifice/move stay sick this turn | **Re-run** (`17_turns_ai.js:69`) — newly-derived workers harvest immediately |
| Harvest | Manual `doHarvest()` in Upkeep | Automatic, inside `foeTurn`, **after** its charge-fuelling step |
| Phase progression | Full machine | None (§3.2) |

The `readyWorkers` re-run is a genuine AI advantage. Flagged in §18.

---

## 5. Start-of-turn structure effects — `buildingUpkeep`

`17_turns_ai.js:2-11`

```
buildingUpkeep(owner):
  revived = false
  tick(o):
    if o.kind != 'building': return
    if o.eff == 'mana':    P.mana = min(99, P.mana + o.val); log
    if o.eff == 'damage':  buildingDamage(owner, o.val || 0, o.nm)
    if o.eff == 'revive':  if !revived: revived = reviveFromGrave(owner)   // at most ONCE per turn,
                                                                          // regardless of how many
                                                                          // Reliquaries you own
  for w in ['front','back']: for o in P[w]: tick(o)
  for o in G.center where o.owner == owner: tick(o)
```

**Iteration order is normative** and is a determinism hazard: `front` row slots 0..6, then `back` row
slots 0..6, then `center` slots 0..6.

**Latent bug to fix in the port:** the `front`/`back` pass does **not** check `o.owner === owner` (only
the `center` pass does). Today no foreign building can occupy a player's own row array — buildings are
placed exclusively via `cellArr(owner, which)` — so it is unreachable. Port it with an explicit owner
check.

### 5.1 Structure effect table (from `03_cards_creatures.js:53-71`)

| `eff` | Structures | Start-of-turn behaviour |
|---|---|---|
| `mana` | Foundry (◆1), Keep (◆2), Citadel (◆3), Forge (◆2), Grand Forge (◆3) | `P.mana = min(99, P.mana + val)` |
| `damage` | Cannon Tower (`val` = 1000) | `buildingDamage` — §5.2 |
| `revive` | Reliquary | `reviveFromGrave` — §5.3, once per turn total |
| `vault` | Mana Vault (`val` 4), Grand Vault (`val` 10) | No upkeep tick; contributes to `vaultCap` — §8 |
| `villager` | Longhouse, Barracks | No upkeep tick; contributes `+val` to `rowWorkers` (`05_board_state.js:64`) |
| `wall`, `none`, `command` | Bulwark, Bastion, Encampment, Outpost | No upkeep tick |

### 5.2 `buildingDamage(owner, val, nm)` — `17_turns_ai.js:25-31`

```
if val <= 0: return
foe = opposite(owner)
tgt = null
for w in ['front', 'center', 'back']:            // NOTE: front, then center, then back
    arr = (w == 'center') ? G.center : G.P[foe][w]
    for x in arr (slot order 0..6):
        if x && x.owner == foe && x.kind == 'creature' && !x.worker: tgt = x; break
    if tgt: break
if tgt: tgt.h -= val; log
```

Notes: it strikes exactly one creature; workers are immune; it never targets structures, charges or
traps; it does **not** call `cleanup()` itself (`startTurn` step 10 does). "Nearest" is defined by the
defender's **own** array ownership, so a raider of yours parked in the enemy front row is skipped
(`x.owner === foe` fails).

### 5.3 `reviveFromGrave(owner)` — `17_turns_ai.js:13-23`

Scan the owner's graveyard **from the end backwards** (most recently interred first) for the first
record with `type === 'creature' && !token`. Splice it out and push a fresh `handcard` into the owner's
hand carrying: `nm, a, h, c, fs, up, art, kw, det, ward, wardhp, reap, grow, hatch, into, entrench,
tribe, subtype`, plus `color = r.color || P.color`. Returns `true` if something was returned.

Tokens (Lumen wards, Shades) and non-creature records are never revived.

---

## 6. Upkeep — the worker-shortfall settlement (player side)

### 6.1 The shortfall model (from `05_board_state.js`, restated for context)

* Worker **zones**: `ZONES = ['back','front','center','raid']`.
* `rowWorkers(owner, z)` = Σ(structure `sup` + villager `val`) − Σ(monster `up`) over the rows the
  zone spans; the `back` zone additionally gets `CCS[P.cc].wk` (the homeland's base workforce).
* `raid` spans **both** enemy rows (`raidKeys`, `05_board_state.js:58`) and has **no** worker pool —
  its figure is never positive, so an army camped behind enemy lines is a pure upkeep bill.
* `zoneDeficit(owner, z) = max(0, max(0, -rowWorkers(owner,z)) - (P.upaid[z] || 0))`.
* `deficitRows(owner)` = zones with `zoneDeficit > 0`, **in `ZONES` order**.
* `totalDeficit(owner)` = Σ over `ZONES`.

### 6.2 `upkeepOffender()` — `17_turns_ai.js:95-100`

```
for z in ZONES (back, front, center, raid):
    if zoneDeficit('you', z) <= 0: continue
    cres = creaturesInRow('you', z) filtered !o.paid, sorted by o.up DESCENDING
    if cres non-empty: return cres[0]
return null
```

`creaturesInRow` (`05_board_state.js:87`) enumerates `zoneKeys(owner,z)` in order, then slots 0..6,
excluding workers. **The sort must be stable** (see §16).

### 6.3 `orphanDeficit(owner)` — `17_turns_ai.js:103-107`

Sum of `zoneDeficit(owner,z)` over zones that have a positive deficit **and no unpaid creature to
settle**. This is the shortfall a razed support structure leaves behind — nothing can be moved or
sacrificed, so `doHarvest` is permitted to pay it directly out of the harvest proceeds. The Harvest
button is locked iff `totalDeficit('you') - orphanDeficit('you') > 0` (`12_render.js:20`).

### 6.4 The three settle actions

**`upkeepPick(key, i)`** — `17_turns_ai.js:116-125`. Opens the per-card menu. `payN = min(o.up || 0,
zoneDeficit('you', z))` where `z = zoneForRow('you', key)` (`12_render.js:184`). The Pay button is
shown iff `payN > 0 && !o.paid`, and disabled if `manaTotal('you') < payN`.

**`upkeepPay(key, i)`** — `17_turns_ai.js:127-137`:

```
guard: G.upkeep && G.turn=='you' && !G.busy && !G.over
o must be your creature and !o.paid
z = zoneForRow('you', key); if !z: return
cost = min(o.up || 0, zoneDeficit('you', z))
if cost <= 0: close menu; upkeepNext(); return
if manaTotal('you') < cost: hint; return           // no partial payment
payAny('you', cost)
P.upaid[z] += cost
o.paid = true
upkeepNext()
```

**`upkeepSac(key, i)`** — `17_turns_ai.js:138-144`: remove the creature from its slot, `toGrave('you',
o)`, `syncWorkers('you')`, `upkeepNext()`. **No mana refund, no death-keyword trigger** (`toGrave` is
called directly, not through `cleanup()`, so `onCreatureDeath` — Detonate/Reap — does **not** fire).
That is a deliberate rules distinction: sacrificing at upkeep is not a death.

**Move** — routed through the ordinary `startMove`/`doMove` (`16_movement.js:41,46`). `moveSpent(c)`
(`16_movement.js:26`) grants a **second** move during upkeep only:
`return !!c.moved && !(G.upkeep && !c.moved2 && !c.tapped)`. The second move sets `moved2 = true` and
`tapped = true` — it spends the creature's entire turn. `doMove` calls `upkeepNext()` when
`G.upkeep`.

**`upkeepNext()`** — `17_turns_ai.js:109-115`: refresh the hint, then auto-open the menu on the next
offender unless a move is in progress (`G.moveFrom`).

### 6.5 `doHarvest()` — `17_turns_ai.js:147-174`

```
1. guard: G.phase=='upkeep' && G.turn=='you' && !G.busy && !G.over
2. owe = totalDeficit('you')
3. if owe > 0 and upkeepOffender() exists:
       hint "shortfall unsettled"; upkeepPick(offender); RETURN      // hard block
   (if owe > 0 but no offender exists, fall through — purely structural shortfall)
4. sum = 0
   for z in ['back','front','center']:                              // NOT 'raid'
       pool = minPool('you', z)
       up   = count of pool where !tapped && !sick
       if up <= 0: continue
       total = up * minYield(z)                                     // minYield == 1 for EVERY row
       for m in pool: if !m.sick: m.tapped = true
       P.mana = min(99, P.mana + total); sum += total
5. if owe > 0:                                                       // structural remainder
       pay = min(owe, P.mana); if pay > 0: payAny('you', pay)
       for z in ZONES: d = zoneDeficit('you', z); if d > 0: P.upaid[z] += d
       log (full payment vs. partial)
6. setPhase('draw'); G.moveFrom = null; G.cardMenu = null
7. log harvest total; drawHint(); render()
```

**Rule note on step 5:** the `upaid` bookkeeping marks the *entire* remaining deficit as settled even
when `pay < owe`. An unpayable structural remainder is simply forgiven for that turn — this is
deliberate (comment at `17_turns_ai.js:162-163`: "so it can never dead-lock the turn").

**Harvest yield is flat:** `minYield(which)` returns `1` for every row (`15_combat.js:145`);
`extractYield` likewise returns `1` (`12_render.js:455`). There is no positional bonus.

### 6.6 Dead code in this area

`harvestRow` (`15_combat.js:148`) and `applyHarvest` (`15_combat.js:155`) implement a *per-row* manual
harvest. Their only caller is `workerTokEl` inside `renderMinions` (`12_render.js:75-107`), and
`renderMinions` is **never called** by `render()`. In solo play these paths are unreachable; they
survive only because the MP intent layer wraps them (`43_mp_intents.js:19-21`). **Do not port**
`harvestRow`/`applyHarvest`/`renderMinions`. Likewise `hvCancel` (`17_turns_ai.js:175`) and
`doExtract`/`extractSel`/`doExtractAs` (`15_combat.js:120-142`, dead because `canExtract()` returns
`false` unconditionally at `12_render.js:408`).

---

## 7. Draw phase

`17_turns_ai.js:72-84`

* `youDeckClick()` — if `G.turn==='you' && !G.busy && !G.over && G.phase==='draw'` → `doDraw()`;
  otherwise it opens the deck viewer (presentation).
* `doDraw()` — if the deck is non-empty, `drawCard('you')` and log; if empty, log "nothing to draw".
  **Either way** `setPhase('action')`. An empty deck is *not* a loss condition anywhere in this
  codebase — decking out simply stops drawing.

`drawCard(o)` (`11_deck_builder.js:250`) pops from the **end** of the deck array and constructs a
`handcard` record. There is no hand-size limit anywhere.

---

## 8. End phase, hand-off, and the mana drain

### 8.1 `endTurn()` — `17_turns_ai.js:222-243`

```
1. if G.turn != 'you' || G.busy || G.over: return
2. if G.phase == 'draw':   drawHint(); render(); return          // must draw first
3. if G.phase == 'upkeep': upkeepHint(); render(); return        // must harvest first
4. if G.phase != 'action': return
5. if CMB.hasDecls(): CMB.hint(); render(); return               // must resolve declared combat
6. G.sel = null; G.atk = []; G.moveFrom = null; G.moveMana = null
7. setPhase('end'); log "— End phase —"
8. endPhaseEffects('you')                                        // EMPTY HOOK (17:245)
9. endTurnDrain('you')
10. if MP: [guest sends {a:'end'}]; startTurn('foe'); log; render(); RETURN   // no AI, no busy latch
11. G.busy = true; render()
12. setTimeout(380 ms):
        startTurn('foe'); log "— Opponent's turn —"; render()
        setTimeout(foeTurn, 650 ms)
```

`endPhaseEffects(owner)` is an intentionally empty extension point (`17_turns_ai.js:245`) — port it as
a hook so end-of-turn keyword triggers have a home.

The End Turn button is wired once at load (`17_turns_ai.js:220`,
`$('endBtn').addEventListener('click', endTurn)`); the MP layer removes and re-adds it late-bound
(`43_mp_intents.js:197`).

### 8.2 Mana drain and Mana Vaults — `17_turns_ai.js:33-41`

```js
vaultCap(owner)  = Σ over ownUnits(owner) where kind=='building' && eff=='vault' of (val || 0)
drainMana(owner) = { lost = max(0, P.mana - cap); P.mana = min(P.mana, cap); }
endTurnDrain(owner) = drainMana + a log line
```

| Structure | `eff` | `val` (vault capacity) |
|---|---|---|
| Mana Vault | `vault` | 4 |
| Grand Vault (upgrade of Mana Vault) | `vault` | 10 |

**Rule:** all unspent mana evaporates at end of turn except what the owner's vaults can hold; capacity
is the *sum* of every vault. `endTurnDrain` is called for the player at `17_turns_ai.js:232` and for
the AI at `17_turns_ai.js:388` (and in MP at `42_mp_apply.js:264`). Both sides drain; there is no
carry-over otherwise.

### 8.3 Hand-off timing

| Delay | Between |
|---|---|
| 380 ms | `setPhase('end')` and `startTurn('foe')` |
| 650 ms | `startTurn('foe')` and `foeTurn()` |
| 650 ms | end of `foeTurn` body and `G.busy = false; startTurn('you')` (`17_turns_ai.js:389`) |

**These are pacing only.** In the deterministic C# core they must become *zero-cost state
transitions*; the view layer schedules the beats. See §17.

---

## 9. `foeTurn()` — the AI turn, in exact order

`17_turns_ai.js:267-390`. `foeTurn` is `async`; it **suspends** at three kinds of point where the human
player must answer. Line 268: `if (MP active && MP.started) return;` — **there is no AI in
multiplayer.**

### Step 0 — Fuel face-down charges (`:271-272`)

```
for i in 0..SLOTS-1:  ch = G.P.foe.front[i]
    if ch is a foe 'charge':
        pour = min(manaTotal('foe'), ch.card.c - ch.inv)
        payAny('foe', pour); ch.inv += pour
        if ch.inv >= ch.card.c: flip('foe', 'foeFront', i)
for i in 0..SLOTS-1:  ch = G.center[i]     // same, flipping via ('foe','center',i)
```

Notes: the **back row is never fuelled**. In solo play the AI never *creates* a charge (it only sets
traps, Step 5), so this loop is currently unreachable — it exists for MP-adopted state. Port it
anyway, but **clamp `pour` at 0**: if `ch.inv > ch.card.c`, `pour` is negative and `payAny` with a
negative argument *increases* mana (`06_mana_workers.js:8`). See §18.

### Step 1 — Automatic harvest (`:274-281`)

```
for w in ['back','front','center']:
    ups = P.foe.min[w] where !sick && !tapped
    if ups is empty: continue
    total = ups.length * minYield(w)          // minYield == 1
    for c in ups: c.tapped = true
    log; applyRes(total, 'foe', null); render()
```

`applyRes` (`16_movement.js:183`) credits `P.mana = min(99, P.mana + base)`.

### Step 2 — `cleanup()` (`:282`)

### Step 3 — Build (`:285`)

`if (aiBuild('foe')) aiBuild('foe');` → **at most two structures per turn**, and the second only if the
first succeeded. See §11.3.

### Step 4 — Upgrade (`:286`)

`aiUpgrade('foe')` → **at most one in-place upgrade per turn**. See §11.4.

### Step 5 — Spells and traps

**Raze (`:288-290`)** — first hand card with `type==='spell' && effect==='raze' && canPay`. Target
selection:

```
tk = null; ti = -1
for key in ROWS (foeBack, foeFront, center, youFront, youBack):
    for j in 0..6:
        if cell is owned by 'you' and kind=='building': tk = key; ti = j     // NO break — LAST wins
```

→ **the last player structure in row-major scan order**, i.e. the one furthest into the player's own
back row, rightmost column. `payCost` runs *before* `resolveSpell`, so a failing resolve still spends
the card. `checkWin(); if (G.over) return;`

**Burn (`:292-295`)** — first hand card with `effect==='burn' && canPay`. Target = the player creature
with the **strictly highest `a`** (raw attack, not `effA`), ties broken by first-found in row-major
order; workers excluded. One burn per turn. Same pay-then-resolve order.

**The AI never casts `chain` or `bounce` spells.** Those cards accumulate in its hand forever.

**Trap (`:297-301`)** — first hand card with `type==='spell' && trap`, if `manaTotal('foe') >= 1`:

```
w='back', s = first empty index in P.foe.back
if s < 0: w='front', s = first empty index in P.foe.front
if s >= 0:
    splice card from hand; payAny('foe', 1)
    P.foe[w][s] = { kind:'trap', owner:'foe', w, card:{nm,c,effect,trigger,val,ic,art,trap:true},
                    setTurn: G.turnNo }
```

One trap set per turn. Cost is a flat **◆1** (the "set face-down" price), *not* the card's cost.
`setTurn = G.turnNo` — a trap is armed only when `G.turnNo > setTurn` (`14_spells_traps.js:36`,
`30_resp.js:13`), i.e. from the next ply onward.

### Step 6 — Summon creatures (`:303-313`)

```
guard = 0
cands = P.foe.hand entries with type=='creature' && canPay('foe', c)
        sorted by c.c DESCENDING            // most expensive first
for {c} in cands:
    if guard++ > 6: break                   // ⇒ at most 7 summon attempts per turn
    idx = hand.indexOf(c); if idx < 0 || !canPay('foe', c): continue   // mana re-checked live
    key = 'foeFront'; empty = aiPickDeploySlot('foe','front')
    if empty < 0: key = 'foeBack'; empty = aiPickDeploySlot('foe','back')
    if empty < 0: continue
    payCost('foe', c); hand.splice(idx,1)
    cr = mkCre(c, 'foe', false); cr.sick = true; rowArr(key)[empty] = cr
    log; onCreatureEnter(cr, 'foe'); syncWorkers('foe'); render()
    AWAIT playerTrapOnSummon(cr, whichOf(key), empty)     // ← suspends for the human
    if G.over: return
render()
```

* The AI **never summons into the center** and never sets creatures face-down.
* It **ignores upkeep entirely** when summoning — it will happily over-extend and pay for it at its
  next `startTurn` via `aiFixDeficit`.
* `onCreatureEnter` fires Ward (spawns a Lumen token in the first empty cell).
* `playerTrapOnSummon` has been **replaced** by `30_resp.js:124` — see §10.

### Step 7 — Declare attacks (`:317-324`)

```
declared = []
for atk in aiAttackers():                          // §11.1
    m = unitAt(atk.key, atk.i); if !m || m.tapped: continue
    tref = aiPickTarget(m, atk.i); if !tref: continue          // §11.6
    m.tapped = true
    declared.push({ m,
                    a:    {k: atk.key, i: atk.i},
                    aIdx: rowIdx(atk.key),
                    tIdx: tref.base ? ROWS.length : rowIdx(tref.key),
                    tref, blockers: [] })
    log the declaration
```

Row indices: `ROWS = ['foeBack'(0), 'foeFront'(1), 'center'(2), 'youFront'(3), 'youBack'(4)]`; the
player's castle wall is the virtual index `ROWS.length` = **5**; the AI's wall is **-1**.

**Every eligible AI creature attacks, every turn.** There is no holding back for defence, no
evaluation of trades, no consideration of the player's board strength.

### Step 8 — One priority window over the whole declaration set (`:325-329`)

```
if declared.length:
    render()
    springRef = AWAIT RESP.defendWindow('attack', {desc: "<n> attacks declared…"})
    if G.over: return
```

This is the **anti-tell pause**: exactly one window per AI turn, covering all declarations, whose
*duration is constant whether or not the player holds a trap* (`30_resp.js:57-85`). It returns the trap
the player chose to arm, or `null`.

### Step 9 — Player assigns blockers, one declaration at a time (`:332-342`)

```
bn = 0
for d in declared:
    bn++
    if kwOf(d.m) == 'scour': continue            // Wind fliers cannot be interposed
    if d.aIdx == d.tIdx:     continue            // same row = point-blank duel, uninterposable
    elig = eligibleInterceptors('foe', d.aIdx, d.tIdx).filter(r => r.c !== d.tref.o)
    if elig is empty: continue
    blk = AWAIT askBlock({attacker: d.m, elig, title: `Incoming attack ${bn}/${declared.length}`, desc})
    if G.over: return
    for r in blk: c = r.c || unitAt(r.key, r.i); if c: c.blocked = true; d.blockers.push({...r, c})
    if blk.length: log; render()
```

`eligibleInterceptors(attackerOwner, aIdx, tIdx)` (`15_combat.js:21`) = the union of
`untappedInterceptors` over `rowsCrossedInto(aIdx,tIdx)` (`15_combat.js:7`), which is every row
**strictly past** the attacker's up to and including the target's, clamped to real rows (a wall index
contributes no row of its own but *does* extend the crossed set).

`untappedInterceptors(key, attackerOwner)` (`15_combat.js:15`):
* board creatures in that row with `owner !== attackerOwner && !c.blocked` — **tapped and
  summoning-sick creatures may block**; the once-per-turn gate is `blocked`, not `tapped`;
* plus every worker in that row's pools with `owner !== attackerOwner && !tapped && !sick` — worker
  stacks screen their whole row.
* **Columns never matter.**

The target of the declaration is excluded from its own blocker list (it retaliates instead — that is
not "blocking").

`askBlock` (`16_movement.js:115`) is a modal; in solo it has **no timeout** (`opts.ms` is undefined),
so the AI turn suspends indefinitely until the player answers.

### Step 10 — Simultaneous resolution (`:344-384`)

```
dischargeOvercharge(declared.map(d => d.m))          // Electric attackers spend banked ◆ as +atk

blockedD = declared where some blocker has h > 0
openD    = declared not in blockedD                  // partition happens ONCE, before any damage

(A) BLOCKED DECLARATIONS — pair fights
for d in blockedD:
    blks = live blockers of d
    ab = 0
    if blks.length > 1:
        kill = blks with h <= effA(d.m), sorted by h ASCENDING, take first
        ab = kill ? kill.index : (blks sorted by h DESCENDING)[0].index
    log; AWAIT CMB.pairFight(d.m, live blocker refs, ab, d.a)
    if G.over: return

(B) UNBLOCKED STRIKES ON CREATURES — grouped by target, ONE retaliation each
byT = Map<targetCreature, [declarations]>            // built from openD where
                                                     //   !tref.base && tref.o.kind=='creature' && d.m.h>0
for (T, ds) in byT (INSERTION ORDER):
    grp = ds.map(d => d.m) where h > 0
    if grp empty || T.h <= 0: continue
    if springRef: RESP.springAttackTrapRef('you', springRef, grp, T); springRef = null   // consumed once
    ri = 0
    if grp.length > 1: ri = AWAIT askRetaliate(T, grp)     // ← the PLAYER directs the retaliation
    log; AWAIT CMB.targetFight(grp, T, ri, fxCell, srcRefs)
    if G.over: return
    for d in ds: if kwOf(d.m)=='scour' && d.m.h > 0: scourStrike(d.m, 'you')
    cleanup()

(C) EVERYTHING ELSE UNBLOCKED — walls, structures, face-downs, traps
wallDmg = 0; scourHits = []
for d in openD:
    if d.m.h <= 0: continue
    if d.tref.base: wallDmg += effA(d.m); if scour: scourHits.push(d.m); continue
    o = d.tref.o
    if o.kind == 'creature': continue                        // already fought in (B)
    if o.kind == 'building':
        if springRef: RESP.springAttackTrapRef('you', springRef, [d.m], o); springRef = null
        log; applyDmg(focusFire([d.m],[o])); cleanup()
    else if o.kind == 'charge': provokeFaceDown('you', d.tref.key, d.tref.i, [d.m])
    else if o.kind == 'trap':   springTrap('you', d.tref.key, d.tref.i, [d.m])
    if kwOf(d.m)=='scour' && d.m.h > 0: scourHits.push(d.m)

if wallDmg > 0:
    G.P.you.life = max(0, G.P.you.life - wallDmg); log; [FX]

for a in scourHits: if a.h > 0: scourStrike(a, 'you')
if scourHits.length: cleanup()
clearDischarge(declared.map(d => d.m))
render(); checkWin(); if G.over: return
```

### Step 11 — Close the turn (`:386-389`)

```
cleanup(); render(); checkWin(); if G.over: return
endTurnDrain('foe')
setTimeout(650 ms): G.busy = false; startTurn('you'); render()
```

**If the game ends mid-AI-turn (`G.over`), `G.busy` is never cleared and `startTurn('you')` never
runs.** That is intentional (the victory banner takes over) but must be modelled explicitly in C#.

### 9.1 The `springRef` trap-consumption rule

The trap the player armed in Step 8 is consumed at **most once per AI turn**, at the **first** of:

1. the first grouped unblocked strike on one of the player's creatures (Step 10-B), or
2. the first unblocked strike on one of the player's structures (Step 10-C).

If the AI's entire attack is blocked, or consists only of wall strikes / face-down provocations /
trap detonations, the armed trap is **never consumed and stays on the board**. Verify with the design
owner whether this is intended.

`RESP.springAttackTrapRef` (`30_resp.js:92-99`) re-validates that the trap card is still in its cell,
then applies:
* `thornmail` → the *defender* gains `a += 500`, `maxh += 1000`, `h += 1000` (permanent);
* `burn` → every attacker in the group takes `card.val` damage;
then graves the trap card and empties its cell.

---

## 10. RESP — the pause-to-respond layer injected into the AI turn

`30_resp.js` loads *after* the FX wrappers and re-binds several functions. Relevant to the AI:

| Setting | Window duration |
|---|---|
| `'off'` | 0 ms |
| `'3'` | 3000 ms |
| `'4'` | **4000 ms (default)** |
| `'6'` | 6000 ms |
| MP active | forced 4000 ms |

Persisted in `localStorage['srd.respwin']`. Pause button grants a fresh **15000 ms**
(`30_resp.js:68`).

**`RESP.defendWindow(trigger, ctx)`** (`30_resp.js:57`) resolves with a trap ref or `null`:
* enumerates `findArmedTraps('you', trigger)` — traps in `front`/`back`/`center` with matching
  `card.trigger` and `G.turnNo > (setTurn ?? 0)`;
* if duration is 0, not MP, **and the player holds no matching trap**, it resolves `null`
  immediately — otherwise it shows the bar. So with duration 0 but a trap in hand, the bar appears
  with **no timeout** and waits for a click;
* timeout ⇒ auto-pass.

**`playerTrapOnSummon` is REPLACED, not wrapped** (`30_resp.js:124-133`). Every AI summon (Step 6)
opens a `defendWindow('summon', …)`. With the default 4 s setting and up to 7 summons, an AI turn can
cost the player **28 seconds of windows even when they hold no traps** — the anti-tell guarantee.
If the player springs the trap: the summoned creature goes to the grave, its cell is emptied, the
trap card is graved and its cell emptied, then `cleanup(); render()`.

**Input lock** (`30_resp.js:102-103`): `onCell` and `onHand` are wrapped to no-op while
`RESP.active`.

> **Port note.** `RESP` is a *presentation-layer priority window*, but its outcome (which trap the
> defender springs) is a **rules input**. In C# model it as an explicit `RespondToAttackDeclarations`
> / `RespondToSummon` request emitted by the core and answered by an `IResponder` (human UI or AI).
> The *timer* belongs to the view. The anti-tell property (constant timing regardless of hand
> contents) must be preserved by the UI, not the core.

---

## 11. The AI decision procedures, one by one

### 11.1 `aiAttackers()` — `17_turns_ai.js:247-250`

```
out = []
for key in ROWS (foeBack, foeFront, center, youFront, youBack):
    for (c, i) in rowArr(key) (slot order 0..6):
        if c && c.owner=='foe' && c.kind=='creature' && !c.worker && !c.sick && !c.tapped:
            out.push({key, i})
```

Enumeration order is normative (it is the declaration order and therefore the block-prompt order).
The AI attacks with **every** eligible creature, including ones standing in the player's own rows.

### 11.2 `yourFieldTargets()` — `17_turns_ai.js:252-255`

Every object on any row with `owner === 'you'`: creatures (incl. tokens), buildings, `charge`
face-downs, `trap` face-downs. Worker pools are **not** included — the AI never attacks worker stacks
(the player can, via `routeAttack('workers',…)`).

### 11.3 `aiBuild(owner)` — `07_structures.js:50-66`

```
list = buildList(ccId)
CAP  = {foundry:1, encampment:1, longhouse:1, vault:1, outpost:1, bulwark:1, tower:2, reliquary:1}
for def in list (IN ORDER):
    if CAP[def.bid] and (count of ownBuildings whose bidLineage contains def.bid) >= CAP[def.bid]: continue
    if def.bid=='forge'      and any owned building whose lineage contains 'forge' with same color: continue
    if def.bid=='grandforge' and any owned building with bid=='grandforge' and same color:          continue
    if !canBuild(owner, def): continue
    which = first of ['back','front'] with freeDeploySlot(owner,w) >= 0 and placeRowOK(owner,w,def)
    if !which: continue
    slot = aiPickDeploySlot(owner, which)
    payAny(owner, def.c); cellArr(owner,which)[slot] = mkBld(def, owner); syncWorkers(owner)
    log; return true
return false
```

**`buildList(ccId)` order** (`03_cards_creatures.js:73-79`) — this *is* the AI's build priority:

| # | Structure | `bid` | Cost ◆ | HP | `eff` / `val` | `sup` | Prereq |
|---|---|---|---|---|---|---|---|
| 1 | The Foundry | `foundry` | 2 | 3000 | mana 1 | +2 | — |
| 2 | *(colour 1)* Forge | `forge` | 3 | 2500 | mana 2 | +2 | foundry |
| 2b | *(colour 2, dual leaders only)* Forge | `forge` | 3 | 2500 | mana 2 | +2 | foundry |
| 3 | Encampment | `encampment` | 2 | 2500 | none | +2 | foundry |
| 4 | Longhouse | `longhouse` | 4 | 3000 | villager | +3 | foundry (front row) |
| 5 | Mana Vault | `vault` | 4 | 3000 | vault 4 | 0 | foundry |
| 6 | Outpost | `outpost` | 2 | 3000 | none | +1 | forge |
| 7 | Bulwark | `bulwark` | 5 | 6000 | wall | +1 | forge |
| 8 | Cannon Tower | `tower` | 4 | 4000 | damage 1000 | **−2** | forge |
| 9 | Reliquary | `reliquary` | 5 | 3500 | revive | +1 | longhouse |
| 10 | *(colour 1)* Grand Forge | `grandforge` | 6 | 3500 | mana 3 | +3 | forge |
| 10b | *(colour 2)* Grand Forge | `grandforge` | 6 | 3500 | mana 3 | +3 | forge |

`canBuild` (`06_mana_workers.js:198`) = `manaTotal >= def.c && prereqMet && hasPlacement`.
`prereqMet` uses `bidLineage` so an upgraded tier still satisfies its base's prereq (a Keep still
counts as a Foundry). `placeRowOK` (`06_mana_workers.js:196`) forbids a negative-`sup` structure in a
row that would go negative.

**The AI never builds in the center row.** The `hasPlacement` check *does* consider the center, so the
AI can burn a loop iteration on a def it can only place centrally and then skip it.

Note `def.row` (row-gated tiers Keep/Citadel/Barracks) is not checked in `aiBuild` — safe today
because those tiers are upgrade-only and never appear in `buildList`.

### 11.4 `aiUpgrade(owner)` — `07_structures.js:38-48`

```
for b in ownBuildings(owner):                  // ownUnits order: ROWS × slots, excluding cc
    loc = buildingLoc(owner, b)                // searches back, front, center in that order
    if !loc: continue
    def = first entry of upgradeTargets(b) that passes canUpgradeTo(owner, b, loc.key, def)
    if !def: continue
    payAny(owner, def.c); applyUpgrade(b, def); syncWorkers(owner); log; return true
return false
```

`upgradeWhy` (`07_structures.js:9-14`) rejects when: the tier's `row` doesn't match the structure's
current row; `manaTotal < def.c`; or a negative-`sup` tier would push the row's workers below zero
after swapping out the old `sup`.

`applyUpgrade` (`07_structures.js:16-22`): swaps `bid/nm/eff/val/sup/ic/c/art` (and `color` if the tier
defines one) on the **same unit object** (id, owner, banked ◆ and board position preserved). Damage
carries through: `dmg = max(0, (o.maxh ?? def.h) - o.h)`; `o.maxh = def.h`; `o.h = max(1, def.h - dmg)`
— **upgrading repairs nothing**, it only adds the new tier's extra max HP.

Upgrade chains (`up2` / `from` in `03_cards_creatures.js`):
`foundry → keep → citadel` (back row) · `encampment → longhouse → barracks` (front row) ·
`vault → grandvault` · `outpost → {tower | bastion}` (branch: first affordable target wins) ·
`forge → grandforge`.

### 11.5 `aiPickDeploySlot(owner, which)` — `16_movement.js:20-23`

Preference orders (column index):

| Row | Order |
|---|---|
| `center` | 3, 1, 5 *(the three monster lanes)* |
| `front` | 3, 4, 2, 5, 1, 6, 0 |
| `back` | 2, 4, 3, 1, 5, 0, 6 |

Falls back to `freeDeploySlot` (first index that is empty and legal) if none of the preferred indices
is free. Used by both `aiBuild` and the AI's summon step.

### 11.6 `aiPickTarget(m, aCol)` — `17_turns_ai.js:256-266`

**The only randomised decision in the entire AI.**

```
fld = yourFieldTargets()

1. ch = fld where kind=='charge' && inv >= 2, sorted by inv DESCENDING, take first
   if ch exists AND Math.random() < 0.6:  return ch          // 60 % — crack a well-funded face-down

2. kill = fld where kind=='creature' && !worker && m.a >= t.o.h,
          sorted by h ASCENDING, take first
   if kill exists: return kill                                // 100 % — always take a guaranteed kill

3. bld = fld where kind=='building', sorted by h ASCENDING, take first
   if bld exists AND Math.random() < 0.3:  return bld         // 30 % — chip the weakest structure

4. return {key:'youBack', i: clamp(aCol, 0, SLOTS-1), base:true, o:null}   // storm the castle wall
```

Notes:
* Step 1 is checked **before** the guaranteed kill, so a 60 % roll can pass up a free kill.
* Step 2 uses **raw `m.a`, not `effA(m)`** — an Overcharge attacker's banked discharge is *not*
  counted when deciding whether a kill is lethal.
* Step 4's `i` is FX-only (`17_turns_ai.js:265` comment). Columns never matter in combat.
* Traps (`kind === 'trap'`) are never deliberately targeted; they can only be hit as the wall-adjacent
  fallback never selects them. In practice the AI attacks a set trap only if `yourFieldTargets`
  returned one and it happened to be… it cannot — no branch selects `kind==='trap'`. Step 10-C's
  `o.kind === 'trap'` branch is therefore unreachable from AI declarations in solo.

### 11.7 `aiChooseInterceptors(attackers, info)` — `15_combat.js:70-84` (the AI as **defender**)

This is the AI's *blocking policy*, invoked when the **player** attacks.

```
elig = info.elig || []; if empty: return []
P = (info.power != null) ? info.power : sumA(attackers)

if info.cc:                                        // the target is the castle wall / life pool
    if !(P >= G.P.foe.life || P >= 4): return []    // ← see the note below
    survivor = elig where c.h > P, sorted by c.h ASCENDING, first
    if survivor: return [survivor]
    return elig sorted by c.h ASCENDING, first TWO       // throw two chumps

if info.kind == 'charge':                          // worth a body to save a funded face-down
    survivor = elig where c.h > P, sorted by c.h ASCENDING, first
    if survivor: return [survivor]

return []                                          // otherwise let it land
```

**The `P >= 4` gate is dead.** With the ×500 stat rescale, any attacking creature has `a >= 500`, so
`P >= 4` is always true. The intended reading was "only interpose if the hit is meaningful"; today the
AI **always** commits blockers when its life pool is threatened. Decide in the port whether to
re-scale this threshold (e.g. `P >= 2000`, or a fraction of `P.life`) or drop it.

Behavioural summary of the AI defender:
* It **never** blocks to save one of its own creatures.
* It blocks to save a funded face-down only if a blocker survives the hit.
* It blocks the castle wall always, preferring the cheapest survivor, else sacrificing the two
  lowest-HP eligible bodies.
* `elig.sort(...)` **mutates the caller's array** (`15_combat.js:77,82`) — irrelevant today, but do
  not reproduce the aliasing in C#.

Call sites: `15_combat.js:182` (worker-stack attack), `15_combat.js:254` (Combat v3 declaration —
the primary solo path, called **per attacker** with `power = effA(A)` and
`cc: kind === 'wall'`), `16_movement.js:71` and `:100` (legacy MP single-shot paths).
`43_mp_intents.js:151` **replaces** it in MP with the guest's pre-fetched choice.

### 11.8 The AI's gang-block absorber choice — `17_turns_ai.js:349-351`

When one of the AI's attackers is gang-blocked, the AI picks which blocker eats its blow:

```
if blockers.length > 1:
    kill = blockers where h <= effA(attacker), sorted by h ASCENDING, first
    absorber = kill ? kill : (blockers sorted by h DESCENDING, first)
else absorber = blockers[0]
```

i.e. **kill the cheapest killable blocker; if nothing is killable, dump the damage on the toughest**
(rather than the weakest, which would be closer to lethal). Flagged in §18 as probably a
mis-heuristic.

### 11.9 The AI's retaliation direction

When the **player** jointly attacks one AI creature, `CMB._resolveNow` hardcodes
`ri = 0` (`15_combat.js:337`, comment: "AI retaliation is auto (its own pick)") — the AI always
retaliates against the **first attacker in the group's declaration order**. There is no heuristic at
all. The symmetric human choice is `askRetaliate` (`16_movement.js:180`).

### 11.10 `aiFixDeficit(owner)` — `17_turns_ai.js:188-219`

Runs at the AI's `startTurn`. Three sequential passes.

```
ZONE GRAPH: MOVE_ADJ = { back:['front'], front:['back','center'], center:['front'], raid:['center'] }

PASS 1 — reposition (guard: max 40 iterations)
while deficitRows(owner).length and guard++ < 40:
    which = deficitRows(owner)[0]                       // first zone in ZONES order with a deficit
    cres  = creaturesInRow(owner, which) sorted by o.up DESCENDING
    if cres empty: break
    {key, i, o} = cres[0]                               // the heaviest upkeep in that zone
    moved = false
    for to in MOVE_ADJ[which] (in order):
        if to == 'raid': continue                       // never rebalance INTO enemy rows
        if rowWorkers(owner, to) - (o.up||0) >= 0 and aiMoveCreature(owner, key, i, to):
            log; moved = true; break
    if !moved: break
    syncWorkers(owner)

PASS 2 — sacrifice (guard: max 40)
while totalDeficit(owner) > manaTotal(owner) and guard++ < 40:
    which = deficitRows(owner)[0]; if !which: break
    cres  = creaturesInRow(owner, which) sorted by o.up DESCENDING
    if cres empty: break
    {key,i,o} = cres[0]
    rowArr(key)[i] = null; toGrave(owner, o); log; syncWorkers(owner)

PASS 3 — pay
owe = totalDeficit(owner)
if owe > 0 and manaTotal(owner) >= owe:
    payAny(owner, owe)
    for z in ZONES: d = zoneDeficit(owner, z); if d > 0: P.upaid[z] += d
    log
```

Notes:
* Pass 2 sacrifices **without firing death keywords** (`toGrave` directly, not via `cleanup`) — same
  rule as the player's `upkeepSac`.
* If pass 2 exits because there is nothing left to sacrifice (an orphan/structural deficit) while
  `owe > manaTotal`, pass 3 is skipped entirely and the AI simply carries the shortfall into the next
  turn. There is **no AI counterpart to `doHarvest`'s structural-remainder payment** (§6.5 step 5).
* Pass 1 acceptance test `rowWorkers(owner,to) - o.up >= 0` is evaluated *before* the move; it does not
  account for the source row's simultaneous improvement.

### 11.11 `aiMoveCreature(owner, fromKey, i, toZ)` — `17_turns_ai.js:178-187`

```
arr = rowArr(fromKey); o = arr[i]; if !o: return false
if o.moved && (o.moved2 || o.tapped): return false      // two moves max — same budget as the player
dstKey = zoneKey(owner, toZ); dst = rowArr(dstKey)
slot = -1
for j in [i, i-1, i+1]:                                  // straight, then left, then right
    if 0 <= j < SLOTS && !dst[j] && slotExists(dstKey,j) && adjacentK(owner, fromKey, i, dstKey, j):
        slot = j; break
if slot < 0: return false
arr[i] = null
if o.moved: o.moved2 = true; o.tapped = true             // the second forced move spends its turn
else:       o.moved  = true
dst[slot] = o; return true
```

`fromKey` is a **global row key**; `toZ` is a **zone name**. `slotExists(w,i)` (`16_movement.js:5`)
requires `i` in `[0,SLOTS)` and, for the center, that `i` is one of the three lanes `{1,3,5}`.
`adjacentK` (`16_movement.js:15`) uses `adjCells`, which permits one square in any of the eight
directions along the owner's move chain
`moveChainOf('foe') = ['foeBack','foeFront','center','youFront','youBack']`.

### 11.12 Decisions the AI does **not** make

For completeness, so the port does not silently "improve" the opponent:

* It never repositions during its Action phase — movement happens **only** inside `aiFixDeficit`.
* It never retreats, never re-forms a line, never contests the center on purpose (it only reaches the
  center via `aiFixDeficit` pass 1).
* It never sets a creature or structure face-down (`charge`).
* It never casts `chain` or `bounce` spells.
* It never pours mana into a charge except in Step 0.
* It never sends banked mana between cards.
* It never attacks worker stacks.
* It never chooses *which* trap to set beyond "the first one in hand".
* It never considers its own upkeep before summoning.
* It never evaluates whether an attack is a good trade.

---

## 12. Difficulty scaling

**There is none.** Grep of the whole tree finds no difficulty setting, no per-opponent AI parameter,
no handicap, and no campaign-driven modifier reaching `foeTurn`.

What *does* vary between opponents:

* **Commander identity** (`CCS[foeId]`) → starting life `hp` (uniformly **10000** for every element,
  and `round((hpA+hpB)/2)` = 10000 for duals) and starting back-row workers `wk` (2 or 3 by element;
  duals use `round((wkA+wkB)/2)`) — `01_core_defs.js:16-25`, `04_cards_leaders.js:11-21`.
* **Deck contents** — in solo the AI's deck is always `deckOf(cf.colors)`
  (`09_game_start.js:11`), a randomly-generated 40-card deck (§13). Campaign battles pass
  `foeDeck = undefined` (`10_menus_campaign.js:147`), so the same generator runs. The campaign
  territory's `garrison` value is **not** wired into the duel at all — it only affects the map layer.

If difficulty tiers are wanted, `aiPickTarget`'s two probabilities, the `aiChooseInterceptors`
threshold, the summon guard (7), the build guard (2) and the `aiFixDeficit` policy are the natural
knobs.

---

## 13. Randomness inventory (every source, exhaustively)

### 13.1 Inside the turn machine / AI

| Location | Call | Effect |
|---|---|---|
| `17_turns_ai.js:259` | `Math.random() < 0.6` | AI attacks a funded face-down instead of anything else |
| `17_turns_ai.js:263` | `Math.random() < 0.3` | AI attacks the weakest structure instead of the wall |

**That is the complete list of randomness in `foeTurn`.** Everything else the AI does is deterministic
given board state.

### 13.2 Reachable from match setup (affects the AI's resources)

| Location | Call | Effect |
|---|---|---|
| `06_mana_workers.js:25` | `rng(a)` — uniform pick | Card template selection in `deckOf` |
| `06_mana_workers.js:26-35` | `deckOf` | Builds a 40-card deck: `round(28/n)` creatures **per colour** from that colour's pool + `round(12/n)` neutral spells per colour, padded to `DECK_SIZE` with colour-1 creatures, then Fisher–Yates shuffled, then `slice(0, 40)` |
| `06_mana_workers.js:34` | Fisher–Yates | Deck shuffle |
| `06_mana_workers.js:87` | Fisher–Yates | `expandDeck` shuffle (custom decks) |
| `09_game_start.js:42`, `11_deck_builder.js:240` | `Math.random()` | "Random opponent" leader pick |

### 13.3 Presentation-only (must NOT be in the deterministic core)

`08_battlefield.js:29` (battlefield scenery seed), `10_campaign_dialogue.js:133` (line picking),
`10_menus_campaign.js:44-191` (world-map generation and the strategic-layer AI — a **separate**
system from the duel AI), `11_deck_builder.js:161-163` (menu motes), `20_sfx.js:13` (noise buffer),
`21_fx.js:27-77` (particles), `40_mp_net.js:133` (MQTT client id), `44_mp_lobby.js:131` (MP coin
flip).

### 13.4 Determinism requirement

> The C# core must take a **seeded PRNG** injected at match construction and must expose the RNG
> stream position as part of serialised state. Every consumer above that is inside the core (13.1 and
> 13.2) draws from it, in a **fixed call order**. Presentation RNG (13.3) must use a *separate*,
> unsynchronised generator so that turning particles on/off cannot desynchronise a replay or a future
> netcode session.

---

## 14. Non-RNG determinism hazards

### 14.1 Object-iteration order

* `ZONES`, `ROWS`, `MOVE_ADJ` lookups and `buildList` are **arrays** — order is defined and must be
  preserved verbatim in C# (`string[]` / `IReadOnlyList<>`, never `HashSet`/`Dictionary` enumeration).
* `PHASE_ORDER` is only used for the "done" styling in the tracker (`12_render.js:68`).
* `Object.keys(CCS)` is used for the random-opponent pick (`09_game_start.js:42`) — JS integer-like
  vs string key ordering. In C# use an explicit ordered list of commander ids.
* `byT` in `foeTurn` Step 10-B is a `Map` — **insertion-ordered**, and insertion order is the
  declaration order from `aiAttackers()`. Use an order-preserving structure (`List<(T, List<Decl>)>`
  or `OrderedDictionary`), **not** `Dictionary<,>`.
* `dmg` in `focusFire` / `CMB.pairFight` / `CMB.targetFight` are `Map`s iterated with `forEach` —
  insertion-ordered. Damage application order matters when two units die in the same tier.

### 14.2 Sort stability

`Array.prototype.sort` is **stable** in every modern JS engine (spec-mandated since ES2019). These
sorts rely on it to break ties by board position:

| Location | Sort key | Tie-break relies on |
|---|---|---|
| `17_turns_ai.js:97` | `up` DESC | `creaturesInRow` enumeration order |
| `17_turns_ai.js:192, 208` | `up` DESC | same |
| `17_turns_ai.js:258` | `charge.inv` DESC | `yourFieldTargets` order |
| `17_turns_ai.js:260` | creature `h` ASC | same |
| `17_turns_ai.js:262` | building `h` ASC | same |
| `17_turns_ai.js:350-351` | blocker `h` ASC / DESC | blocker list order |
| `15_combat.js:28,30,36,43` | `effA` DESC / `h` ASC | `focusFire` dealer & target order |
| `15_combat.js:75,77,80,82` | blocker `h` ASC | `eligibleInterceptors` order |
| `06_mana_workers.js:127-128` | `(b.a-a.a) || (a.h-b.h)` | Detonate target choice |

**`List<T>.Sort` in .NET is UNSTABLE.** Use `OrderBy`/`ThenBy` (LINQ's sort is stable), or add an
explicit board-position tie-breaker to every comparator. This is the single most likely source of a
silent behavioural divergence in the port.

### 14.3 Snapshot-vs-live list hazards

* `foeTurn` Step 6 snapshots `cands` once, then re-validates each entry with `hand.indexOf(c)` and
  `canPay` before use — **identity-based**, safe.
* `aiAttackers()` is snapshotted before the declaration loop, then re-read via `unitAt`. Nothing moves
  during that loop, so it is safe today, but the C# version should re-validate.
* `aiFixDeficit` recomputes `deficitRows` and `creaturesInRow` every iteration — safe.
* `blockedD`/`openD` are partitioned **once, before any damage** (`17_turns_ai.js:345-346`, mirrored at
  `15_combat.js:317-318`): *a blocked attacker stays blocked even if it kills its whole blocking gang.*
  This is a real rule, not an optimisation.

### 14.4 Async/timing dependence

`foeTurn` is `async` and `await`s four things: `playerTrapOnSummon` (per summon),
`RESP.defendWindow` (once), `askBlock` (per interposable declaration), `askRetaliate` (per
multi-attacker group), plus `CMB.pairFight` / `CMB.targetFight` (which `await` FX lunges). Between
those awaits the DOM is live and timers can fire.

The core must not depend on any of this. See §17 for the required shape.

### 14.5 The `aiMoveCreature` FX-wrapper signature bug

`22_fx_wrappers.js:144-150` wraps `aiMoveCreature` with the parameter named `fromZ` and then does
`rowArr(zoneKey(owner, fromZ))[i]`. But the real second argument is a **global row key**, not a zone
name. `zoneKey('foe','youFront')` → `rowKeyFor('foe','youFront')` → `'foeBack'`. The wrapper therefore
reads the wrong cell when computing the FX source rectangle.

**Impact: cosmetic only.** The wrapper forwards the *original* arguments to `_aiMoveCreature`, so the
move itself is correct. Do not reproduce; do not "fix" it in a way that changes the rules.

### 14.6 `payAny` with a negative amount

`payAny(o, n)` (`06_mana_workers.js:8`) is `const g = Math.min(P.mana, n); P.mana -= g; return g >= n;`
— a negative `n` **adds** mana. Reachable from `foeTurn` Step 0 if `ch.inv > ch.card.c`. Clamp in the
port.

---

## 15. Monkey-patch layer — what actually changes behaviour

`22_fx_wrappers.js` rebinds many globals at load time. **All of them are presentation** except where
noted. The ones touching this subsystem:

| Wrapped | File:line | Behavioural effect |
|---|---|---|
| `startTurn` | `22:224-229` | Adds a turn ribbon + SFX. None. |
| `checkWin` | `22:247-253` | Re-reads the outcome from `G.P.*.life` **after** `campResolve` may have rewritten the banner, then plays win/lose SFX. None on rules. |
| `doHarvest` | `22:200-201` | Mana-pop FX. None. |
| `aiBuild` | `22:168-175` | Build SFX + a 40 ms deferred ring. None. |
| `aiMoveCreature` | `22:144-150` | Move trail FX; **buggy source lookup** (§14.5). None on rules. |
| `applyRes`, `applyHarvest`, `drawCard`, `dealOpening`, `render`, `toGrave`, `applyDmg`, `resolveCombat`, `place`, `flip`, `castSpell`, `springTrap`, `doMove`, `onCreatureEnter`, `placeBuild`, `resolveSpell`, `trainVillager`, `startGame`, `renderCharSel` | `22:*` | FX/SFX only. |

`30_resp.js` — **does** change behaviour:

| Rebinding | Effect |
|---|---|
| `onCell`, `onHand` (`30:102-103`) | No-op while a response window is open (input lock). |
| `doAttack`, `attackBackRow`, `attackMinionStack` (`30:107-112`) | Wrapped in `RESP.actingGate('attack', …)` — inserts a constant-duration pause **before** resolution. Legacy/MP paths only in solo Combat v3. |
| `foeTrapOnSummon` (`30:118-121`) | Player's summon: the AI's auto-spring is deferred to the end of the window. |
| **`playerTrapOnSummon` (`30:124-133`)** | **Replaced wholesale.** The old modal Yes/No prompt becomes the RESP bar. This is the AI-summon trap interaction (§10). |

`43_mp_intents.js` — only active when MP is connected; replaces `aiChooseInterceptors` with the
guest's answer (`43:151-159`) and freezes `endTurn`/`onCell`/`onHand`/`doMove` while awaiting the
peer.

---

## 16. Multiplayer interaction (context only — MP is deferred)

* `startTurn` MP branch (`17:65-66`): sets `upkeep` and stops. No AI.
* `foeTurn` (`17:268`): returns immediately.
* `endTurn` MP branch (`17:233-237`): no `G.busy` latch, no timers — `startTurn('foe')` runs inline.
* `42_mp_apply.js` re-implements every phase transition for the remote side (`harvest`, `draw`, `move`,
  `place`, `build`, `upgrade`, `attack`, `end`). Its `end` handler (`42:261-266`) mirrors
  `endTurn`'s tail exactly: `setPhase('end'); endPhaseEffects('foe'); endTurnDrain('foe');
  startTurn('you')`.

**Port implication:** the fact that MP had to re-implement the whole phase machine as a second code
path is precisely the thing the C# rewrite must avoid. Model every turn transition as a
**command** applied to the core; both the AI policy and a future network peer emit the same commands.

---

## 17. Portable policy interface — recommended C# shape

### 17.1 Separate the three concerns the JS conflates

`foeTurn` currently mixes (a) rules mutation, (b) AI decision-making, and (c) human-in-the-loop
prompts. Split them:

```csharp
// --- (a) RULES CORE: pure, deterministic, no UnityEngine reference -------------
public sealed class DuelState { /* board, players, phase, turnNo, rngState … */ }

public interface IGameCommand { }               // serialisable intent
// Turn machine commands
public sealed record BeginTurn(PlayerId Owner) : IGameCommand;
public sealed record SettleUpkeepPay(RowKey Row, int Slot) : IGameCommand;
public sealed record SettleUpkeepSacrifice(RowKey Row, int Slot) : IGameCommand;
public sealed record MoveUnit(RowKey From, int FromSlot, RowKey To, int ToSlot) : IGameCommand;
public sealed record Harvest() : IGameCommand;
public sealed record DrawForTurn() : IGameCommand;
public sealed record EndTurn() : IGameCommand;
// Action-phase commands
public sealed record SummonCreature(int HandIndex, RowKey Row, int Slot) : IGameCommand;
public sealed record BuildStructure(StructureId Bid, ElementId? Color, RowKey Row, int Slot) : IGameCommand;
public sealed record UpgradeStructure(RowKey Row, int Slot, StructureId ToBid) : IGameCommand;
public sealed record CastSpell(int HandIndex, RowKey Row, int Slot) : IGameCommand;
public sealed record SetTrap(int HandIndex, RowKey Row, int Slot) : IGameCommand;
public sealed record PourIntoCharge(RowKey Row, int Slot, int Amount) : IGameCommand;
public sealed record DeclareAttack(RowKey From, int FromSlot, AttackTarget Target) : IGameCommand;
public sealed record ResolveCombat() : IGameCommand;

public sealed class DuelEngine {
    public DuelState State { get; }
    public CommandResult Apply(IGameCommand cmd);          // pure w.r.t. State + seeded RNG
    public IReadOnlyList<GameEvent> DrainEvents();         // for the view layer
    public PendingRequest? Pending { get; }                // see (c)
}

// --- (b) POLICY: the AI, and also a future network peer ------------------------
public interface ITurnPolicy {
    IEnumerable<IGameCommand> PlanTurn(DuelStateView s, IDeterministicRandom rng);
    IReadOnlyList<UnitRef> ChooseInterceptors(BlockContext ctx, IDeterministicRandom rng);
    int ChooseAbsorber(UnitRef attacker, IReadOnlyList<UnitRef> blockers, IDeterministicRandom rng);
    int ChooseRetaliationTarget(UnitRef defender, IReadOnlyList<UnitRef> attackers, IDeterministicRandom rng);
    TrapRef? RespondToWindow(ResponseWindowContext ctx, IDeterministicRandom rng);
}

public sealed class ScriptedAiPolicy : ITurnPolicy { /* §9 + §11, verbatim */ }
public sealed class HumanPolicy      : ITurnPolicy { /* awaits UI, feeds the same interface */ }

// --- (c) INTERACTION: requests the core emits and someone must answer ----------
public abstract record PendingRequest;
public sealed record BlockerRequest(UnitRef Attacker, IReadOnlyList<UnitRef> Eligible,
                                    int Index, int Total)                        : PendingRequest;
public sealed record AbsorberRequest(UnitRef Attacker, IReadOnlyList<UnitRef> Blockers) : PendingRequest;
public sealed record RetaliationRequest(UnitRef Defender, IReadOnlyList<UnitRef> Attackers) : PendingRequest;
public sealed record ResponseWindowRequest(ResponseTrigger Trigger, string Description,
                                           IReadOnlyList<TrapRef> ArmedTraps)    : PendingRequest;
```

The turn becomes a **state machine over commands**, not a coroutine over `await`s: the engine advances
until it needs an answer, publishes a `PendingRequest`, and blocks. The view layer (or a headless
test) supplies the answer. **`Task`/`async` must not appear in the core** — a headless test must be
able to run 10 000 turns synchronously.

### 17.2 The AI turn as a command sequence

`ScriptedAiPolicy.PlanTurn` yields, in order (mirroring §9):

1. `PourIntoCharge` × n (front row 0..6, then center 0..6)
2. `Harvest` (or a dedicated `AiAutoHarvest` since the AI's harvest differs from `doHarvest` in that it
   has no shortfall gate)
3. `BuildStructure` × ≤2
4. `UpgradeStructure` × ≤1
5. `CastSpell` (raze) × ≤1
6. `CastSpell` (burn) × ≤1
7. `SetTrap` × ≤1
8. `SummonCreature` × ≤7
9. `DeclareAttack` × n (all eligible attackers)
10. `ResolveCombat`
11. `EndTurn`

The engine handles the response windows and blocker prompts between 9 and 10.

### 17.3 Determinism contract for tests

* Same seed + same command sequence ⇒ byte-identical `DuelState` (verify with a stable hash).
* `DuelState` must round-trip through serialisation with no behavioural difference (this is the
  precondition for the deferred host-authoritative netcode).
* Every AI decision must be reproducible from `(DuelStateView, rngStreamPosition)`.

---

## 18. Known bugs, quirks, and open decisions

| # | Location | Issue | Suggested action |
|---|---|---|---|
| 1 | `15_combat.js:74` | `P >= 4` gate in `aiChooseInterceptors` is always true at the ×500 stat scale | Re-scale (e.g. `>= 2000` or `>= P.life/8`) or delete; **ask the design owner** |
| 2 | `17_turns_ai.js:260` | Kill check uses raw `m.a`, not `effA(m)` — Overcharge bonus ignored | Use effective attack |
| 3 | `17_turns_ai.js:258-259` | The 60 % face-down roll runs *before* the guaranteed-kill check | Probably should run after; confirm |
| 4 | `17_turns_ai.js:351` | Gang-block fallback absorber is the **toughest** blocker | Almost certainly should be the weakest; confirm |
| 5 | `17_turns_ai.js:271-272` | `pour` can be negative ⇒ `payAny` *adds* mana | `Math.Max(0, …)` |
| 6 | `17_turns_ai.js:9` | `buildingUpkeep` front/back pass has no owner check | Add explicit owner check |
| 7 | `17_turns_ai.js:69` | AI re-runs `readyWorkers` after settling; the player does not | Pick one and apply symmetrically |
| 8 | `17_turns_ai.js:216` | If the AI cannot afford its shortfall it silently doesn't pay (no structural fallback like `doHarvest`) | Mirror `doHarvest` step 5 for the AI |
| 9 | §9.1 | The armed trap from the AI-attack window is not consumed when the AI only hits the wall | Confirm intent |
| 10 | `15_combat.js:337` | The AI's retaliation direction is hardcoded to attacker index 0 | Give it a heuristic (e.g. the attacker it can kill) |
| 11 | `17_turns_ai.js:290,295` | Spells are paid for *before* `resolveSpell`; a failing resolve still burns the card and the mana | Validate before paying |
| 12 | `17_turns_ai.js:289` | Raze target scan has no `break`, so it picks the **last** structure found | Confirm — "cheapest/most valuable" would be a real heuristic |
| 13 | `13_input.js:153` vs solo | Multi-row joint attacks are legal in solo but rejected in MP | Unify on the solo rule |
| 14 | Whole file | The AI's declarations are stored in a **local** array, never in `G.decls`, so the board shows no `declAtk`/`declTgt`/`declBlk` outlines during the AI turn (the code comment claims they are "visible") | In the port, publish AI declarations to the same view state the player's use |
| 15 | `17_turns_ai.js:398` | `checkWin` scores a simultaneous double-KO as **DEFEAT** (`win = foeOut && !youOut`) | Confirm; consider a draw state |
| 16 | `12_render.js:75-107`, `15_combat.js:120-155` | `renderMinions`, `harvestRow`, `applyHarvest`, `doExtract*` are unreachable in solo | Do not port |

---

## 19. `checkWin()` and match termination

`17_turns_ai.js:392-407`

```
if G.over: return
youOut = (G.P.you.life <= 0); foeOut = (G.P.foe.life <= 0)
if foeOut or youOut:
    G.over = true
    win = foeOut && !youOut                        // simultaneous zero ⇒ DEFEAT
    banner text = win ? 'VICTORY' : 'DEFEAT'
    subtitle    = win ? 'The enemy stronghold has fallen.' : 'Your stronghold has fallen.'
    show banner
    if CAMPAIGN && CAMPAIGN.target != null: campResolve(win)     // id 0 is valid — test != null
```

* **The only loss condition is the life pool reaching 0.** There is no deck-out loss, no
  structure-destruction loss (command-center cards were removed — `findCC` returns `null`,
  `04_cards_leaders.js:25`), and no turn limit.
* Life is drained only by wall strikes: `17_turns_ai.js:379` (AI → player),
  `15_combat.js:358` (player → AI), `16_movement.js:108` (legacy path), `42_mp_apply.js:242` (MP).
* `checkWin` is called from ~20 sites; it is idempotent via the `G.over` guard.
* Surrender (`22_fx_wrappers.js:285`) sets `G.over` **without** calling `checkWin`, and therefore
  clears `CAMPAIGN.target` manually so a stale target cannot be resolved by the *next* match.
* `campResolve` (`10_menus_campaign.js:149`) transfers the territory, cascades capital absorption, and
  rewrites the banner — hence the FX wrapper re-reads the outcome from `G.P.*.life` rather than the
  banner text (`22_fx_wrappers.js:251`).

---

## 20. Constant reference (for the implementer)

| Constant | Value | Source |
|---|---|---|
| `SLOTS` / `C` | 7 | `01_core_defs.js:1` |
| `CENTER_LANES` | `{1,3,5}` (creatures); structures use `{0,2,4,6}` | `01_core_defs.js:2,7` |
| `BASE_COL` | 3 | `01_core_defs.js:4` |
| `ROWS` | `foeBack, foeFront, center, youFront, youBack` (indices 0..4) | `05_board_state.js:4` |
| Wall indices | foe wall −1, player wall 5 (`ROWS.length`) | `16_movement.js:94`, `17_turns_ai.js:321` |
| `ZONES` | `back, front, center, raid` | `05_board_state.js:56` |
| `MOVE_ADJ` | `back→[front]`, `front→[back,center]`, `center→[front]`, `raid→[center]` | `17_turns_ai.js:177` |
| Mana cap | 99 | `17_turns_ai.js:5,160`; `16_movement.js:184` |
| `minYield` / `extractYield` | 1, every row | `15_combat.js:145`, `12_render.js:455` |
| Face-down / trap set cost | ◆1 | `13_input.js:221,227`; `17_turns_ai.js:299` |
| Overcharge bank cap | 3 | `06_mana_workers.js:156` |
| `DECK_SIZE` | 40 | `06_mana_workers.js:37` |
| Opening hand | 4 | `11_deck_builder.js:248` |
| Starting life | 10000 (every commander) | `01_core_defs.js:16-25` |
| Starting back-row workers | 2 or 3 by element (`CCS[cc].wk`) | `01_core_defs.js:16-25`, `05_board_state.js:66` |
| AI build cap per turn | 2 | `17_turns_ai.js:285` |
| AI upgrade cap per turn | 1 | `07_structures.js:45` |
| AI summon cap per turn | 7 (`guard++ > 6`) | `17_turns_ai.js:305` |
| AI raze / burn / trap-set per turn | 1 each | `17_turns_ai.js:288,292,297` |
| `aiFixDeficit` loop guards | 40 each pass | `17_turns_ai.js:190,206` |
| `cleanup` loop guard | 40 | `16_movement.js:194` |
| `bidLineage` walk guard | 8 | `06_mana_workers.js:191` |
| AI structure caps | foundry 1, encampment 1, longhouse 1, vault 1, outpost 1, bulwark 1, **tower 2**, reliquary 1; forges 1 per colour; grand forges 1 per colour | `07_structures.js:53-57` |
| AI target probabilities | face-down 0.6, structure 0.3 | `17_turns_ai.js:259,263` |
| Vault capacities | Mana Vault 4, Grand Vault 10 | `03_cards_creatures.js:58,68` |
| Cannon Tower damage | 1000/turn, `sup` −2 | `03_cards_creatures.js:61` |
| Hand-off delays | 380 ms, 650 ms, 650 ms | `17_turns_ai.js:239,241,389` |
| Response window | off / 3000 / 4000 (default) / 6000 ms; pause 15000 ms | `30_resp.js:6,22,68` |
