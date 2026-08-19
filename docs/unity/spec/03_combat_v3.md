# Subsystem Spec 03 — COMBAT ("Combat v3")

**Source of truth:** the JavaScript in `src/js/`. This document is a complete, behaviour-preserving
transcription of that code. Where the JS is buggy, inconsistent, or accidental, this document says so
explicitly rather than silently "fixing" it — the implementer decides, with the facts in hand.

**Primary sources**

| File | What lives there |
|---|---|
| `src/js/15_combat.js` | row-interval math, interceptor eligibility, `focusFire`, `resolveCombat`, face-down/trap provocation, the whole `CMB` (Combat v3) declaration engine |
| `src/js/16_movement.js` | legacy single-shot `doAttack` / `attackBackRow`, `askBlock` / `askPick` / `askAbsorb` / `askRetaliate`, `cleanup()` (death sweep) |
| `src/js/17_turns_ai.js` | the AI's attack turn (mirror-image declaration protocol), `checkWin`, turn-start flag resets |
| `src/js/05_board_state.js` | `ROWS`, `rowIdx`, `rowArr`, worker-pool ("minion") accessors |
| `src/js/06_mana_workers.js` | `effA`, `kwOf`, keyword hooks (`applyUndertow`, `onCreatureDeath`, Overcharge discharge, `scourStrike`) |
| `src/js/14_spells_traps.js` | `findArmedTrap`, `flip` (face-down → live unit) |
| `src/js/12_render.js` | `selCres`, `canAttack`, target highlighting (presentation) |
| `src/js/13_input.js` | attacker selection / target tap routing (presentation + intent) |
| `src/js/22_fx_wrappers.js` | **monkey-patch layer** — wraps `applyDmg`, `resolveCombat`, `toGrave`, `doAttack`, `attackBackRow`, `attackMinionStack`, `springTrap`. All FX only; **no rule changes** |
| `src/js/30_resp.js` | **monkey-patch layer** — the "pause to respond" priority window; wraps `doAttack` / `attackBackRow` / `attackMinionStack` and `onCell` / `onHand` |
| `src/js/42_mp_apply.js`, `43_mp_intents.js`, `44_mp_lobby.js` | multiplayer re-implementation of the **legacy** attack path (Combat v3 is solo-only today) |

---

## 0. Executive summary — the ten load-bearing rules

1. **Columns never matter in combat.** Any attacker may target any enemy object anywhere on the board,
   or the enemy life pool directly. Reach is unlimited (`12_render.js:444-446`, `17_turns_ai.js:257`).
   Columns matter only for *movement* congestion.
2. **Blocking is row-interval based.** A block may be declared by any enemy-side creature standing in a
   row the attack **crosses into** — every row strictly past the attacker's row up to and *including*
   the target's row (`15_combat.js:7-11`).
3. **Same row = an uninterposable duel.** If attacker and target share a row, the crossed-row set is
   empty, so no block is possible (`15_combat.js:8`).
4. **Alternating declarations.** Attacks are declared one at a time and the defender answers each
   declaration with blockers *immediately*, before the next declaration. Nothing resolves until the
   attacker presses **⚔ Resolve** (`15_combat.js:235-262`, `15_combat.js:304-308`).
5. **Universal retaliation.** Every attacked creature strikes back with its full attack, whether or not
   it blocked. Retaliation is not blocking (`15_combat.js:279`, `15_combat.js:297`).
6. **No damage splitting.** Each unit deals its damage to exactly **one** enemy. A gang-blocked attacker
   picks a single **absorber** among its blockers; a jointly-attacked defender picks a single
   **retaliation target** among its attackers (`15_combat.js:275`, `15_combat.js:293`).
7. **Walls and structures never retaliate.** Damage into the life pool and into buildings is one-way
   (`15_combat.js:344`, `15_combat.js:350-352`).
8. **Damage is simultaneous within a tier.** Two tiers per fight: First Strike, then everyone else.
   All damage in a tier is accumulated and applied at once (`15_combat.js:277-282`, `15_combat.js:295-300`).
9. **Walls are virtual rows −1 and 5.** The enemy castle wall sits one row beyond their back row; a
   strike at it crosses into *every* real row between, so it can be blocked — except from inside their
   back row, where nothing can interpose (`16_movement.js:87-90`, `15_combat.js:250`).
10. **Summoning-sick creatures MAY block. Tapped creatures MAY block.** Blocking is gated once per
    turn-cycle by a separate `blocked` flag (`15_combat.js:14-16`). *Workers* are the exception —
    they must be untapped and unsick to block (`15_combat.js:18`).

---

## 1. Board geometry and coordinates

### 1.1 Rows

`05_board_state.js:4`

```js
const ROWS = ['foeBack','foeFront','center','youFront','youBack'];
```

| Index | Row key | Owner-facing name | Storage |
|---|---|---|---|
| −1 | *(virtual)* **foe castle wall** | "the enemy stronghold" | none — drains `G.P.foe.life` |
| 0 | `foeBack` | enemy base | `G.P.foe.back` |
| 1 | `foeFront` | enemy front | `G.P.foe.front` |
| 2 | `center` | the contested center | `G.center` (shared; holds units of either side) |
| 3 | `youFront` | your front line | `G.P.you.front` |
| 4 | `youBack` | your base | `G.P.you.back` |
| 5 | *(virtual)* **your castle wall** | "your stronghold" | none — drains `G.P.you.life` |

`rowIdx(key)` = `ROWS.indexOf(key)` (`05_board_state.js:13`). The virtual wall indices are produced by
hand at each call site: `-1` for the foe wall, `ROWS.length` (= 5) for the player's wall
(`16_movement.js:94`, `15_combat.js:250`, `17_turns_ai.js:321`, `42_mp_apply.js:209`).

### 1.2 Slots

`01_core_defs.js:1-3`

* `SLOTS = 7` columns per row, indices 0..6.
* `CENTER_LANES = [1,3,5]` — in the **center row only**, creatures may stand only in lanes 1/3/5;
  slots 0/2/4/6 are structure ground. This constrains *placement/movement*, never combat.
* `BASE_COL = 3` — used only as a fallback FX column when aiming at the wall (`12_render.js:330`).

`colReach(aCol,tCol)` (`01_core_defs.js:5`) still exists but is **dead code for combat** — no combat
path calls it. Do not port it into the combat rules.

### 1.3 Ownership vs. location

A row's *storage array* belongs to one side, but a cell's *occupant* carries its own `owner` tag.
A player's creature can stand in the enemy's rows (a raid), and both sides' units can stand in the
center. **Always attribute by `unit.owner`, never by which array the unit sits in**
(`05_board_state.js:46`, `16_movement.js:199`).

### 1.4 Worker pools ("minions")

Workers are **not** board cells. They live in per-zone pools `G.P[owner].min[{back,front,center}]`
(`05_board_state.js:27`). `minionsInRow(rowKey)` maps a global row to every worker logically standing
there — the center row contains *both* sides' center pools (`05_board_state.js:29-38`).

A worker unit is a creature record with `worker:true`, `a:0`, `h:1000`, `c:0`, `up:0`
(`06_mana_workers.js:93`, `03_cards_creatures.js:25`). Worker damage **persists** across turns
(`readyWorkers` only clears `sick`/`tapped`/`moved`, `05_board_state.js:81`).

---

## 2. Unit state relevant to combat

From `mkCre` (`06_mana_workers.js:90-92`) and the turn-start reset (`17_turns_ai.js:53`).

| Field | Type | Meaning in combat |
|---|---|---|
| `kind` | `'creature' \| 'building' \| 'charge' \| 'trap'` | `charge` = face-down card accumulating mana; `trap` = face-down trap |
| `owner` | `'you' \| 'foe'` | side |
| `a` | int | printed attack (scale ×500: values 0, 500, 1000 … 4500) |
| `h` | int | current HP (scale ×500: 500 … 4000) |
| `maxh` | int | printed HP; restored on bounce-to-hand |
| `fs` | bool | **First Strike** — strikes in the pre-tier |
| `kw` | keyword id or null | `detonate \| undertow \| entrench \| ward \| reap \| chrysalis \| scour \| overcharge` |
| `worker` | bool | worker/minion body |
| `token` | bool | Lumen / Shade tokens — immune to Undertow bounce |
| `entrench` | bool | immune to Undertow bounce |
| `sick` | bool | summoning sickness — **cannot attack, CAN block** |
| `tapped` | bool | spent — **cannot attack, CAN block** |
| `blocked` | bool | **has already blocked this turn-cycle** — the real block gate |
| `moved`, `moved2`, `paid` | bool | movement / upkeep bookkeeping, not combat |
| `oc` | int 0..3 | Overcharge bank |
| `_dis` | int | transient Overcharge discharge bonus, live only during one resolution |
| `bank` | int | stored mana on the card, not combat |
| `cc` | bool | **legacy/always false** — command-center cards were removed (`04_cards_leaders.js:25`). Several combat guards still test `!o.cc`; they are always true. Keep the concept only if you reintroduce a keep card. |

**Effective attack** (`06_mana_workers.js:115`):

```js
function effA(c){ return (c ? (c.a||0) : 0) + ((c && c._dis) || 0); }
```

**Raw group attack** (`15_combat.js:2`) — used only for hints and the legacy AI heuristic:

```js
function sumA(a){ return a.reduce((s,c)=>s+(c.a||0),0); }   // note: c.a, NOT effA
```

### 2.1 Flag lifecycle

`startTurn(owner)` (`17_turns_ai.js:53`) resets, **for that owner's units only**:

```
sick=false; tapped=false; moved=false; moved2=false; paid=false; blocked=false; _dis=0;
```

Consequences an implementer must preserve:

* A defender's `blocked` flag is cleared at the start of **its own** turn, not at the end of the
  attacker's turn. Therefore each creature may block **once per opponent turn**.
* `sick` clears at the owner's turn start, so a creature summoned on turn N can attack on turn N+1
  but can already block during the opponent's turn N.
* `_dis` is also cleared explicitly by `clearDischarge()` at the end of every resolution
  (`06_mana_workers.js:163`).

---

## 3. Who may attack

`12_render.js:406-407`:

```js
function selCres(){ return G.atk.map(s=>rowArr(s.k)[s.i]).filter(x=>x && x.kind==='creature' && x.owner==='you'); }
function canAttack(){ const c=selCres(); return c.length>0 && c.every(x=>!x.worker && !x.sick && !x.tapped); }
```

**Attack legality (per creature):**

1. `kind === 'creature'`
2. `owner === 'you'` (the acting side)
3. `!worker`
4. `!sick`
5. `!tapped`
6. Game gate: `G.turn === 'you' && !G.busy && !G.over && G.phase === 'action'`
   (`15_combat.js:236`).

There is **no** row restriction on attacking, **no** column restriction, and **no** requirement that a
group of attackers share a row (in solo). The MP legacy path *does* require one shared row
(`13_input.js:153`, `31_ui_shell.js:213-217`, `42_mp_apply.js:201`).

Chrysalis creatures cannot attack because `chrysalisUpkeep` re-applies `sick = true` every upkeep
(`06_mana_workers.js:146-151`), not through any combat check.

### 3.1 The attack group `G.atk`

`G.atk` is an ordered list of `{k: rowKey, i: slot}` refs. It is built by:

* tapping own ready creatures one at a time (`13_input.js:149-155`) — tapping an already-selected cell
  removes it;
* a mouse **marquee drag** over own ready cells (`31_ui_shell.js:197-223`) — in solo the selection may
  mix rows; in MP it collapses to the row with the most hits.

`clearAtk()` empties it (`13_input.js:94`).

---

## 4. Who may block — eligibility

### 4.1 Rows an attack crosses into

`15_combat.js:4-11` — **the single most important function in the subsystem.**

```js
// rows an attack CROSSES INTO: every row past the attacker's, up to and INCLUDING the target row
// (same row = none — a point-blank duel can't be interposed). tIdx may be a virtual WALL index
// (-1 beyond foeBack / ROWS.length beyond youBack): walls have no slots, so only real rows count.
function rowsCrossedInto(aIdx,tIdx){
  const o=[];
  if(aIdx===tIdx) return o;
  const step = tIdx>aIdx ? 1 : -1;
  for(let r=aIdx+step; r!==tIdx+step; r+=step)
    if(r>=0 && r<ROWS.length) o.push(ROWS[r]);
  return o;
}
```

Formally: the half-open interval `(aIdx, tIdx]` walked in the direction of travel, then clipped to
`[0, 4]`. The attacker's own row is **never** included. The target's row **always** is (when real).

**Exhaustive table.** `A` = attacker row index, `T` = target row index (including virtual walls).
Result is the ordered list of crossed rows, which is also the order eligible blockers are enumerated.

| A \ T | −1 (foe wall) | 0 foeBack | 1 foeFront | 2 center | 3 youFront | 4 youBack | 5 (your wall) |
|---|---|---|---|---|---|---|---|
| **0 foeBack** | *(none)* | *(same row)* | 1 | 1,2 | 1,2,3 | 1,2,3,4 | 1,2,3,4 |
| **1 foeFront** | 0 | 0 | *(same row)* | 2 | 2,3 | 2,3,4 | 2,3,4 |
| **2 center** | 1,0 | 1,0 | 1 | *(same row)* | 3 | 3,4 | 3,4 |
| **3 youFront** | 2,1,0 | 2,1,0 | 2,1 | 2 | *(same row)* | 4 | 4 |
| **4 youBack** | 3,2,1,0 | 3,2,1,0 | 3,2,1 | 3,2 | 3 | *(same row)* | *(none)* |

Read the two "none" corners carefully:

* From `foeBack` (index 0) at the **foe wall** (−1) the interval clips to empty → **the strike cannot be
  interposed**. This is the intended "besiege from inside their base" rule
  (`16_movement.js:87-90`).
* Symmetrically, from `youBack` (4) at **your wall** (5) — the AI attacking your wall from inside your
  base — nothing can block.

### 4.2 Eligible interceptors in one row

`15_combat.js:12-20`

```js
function untappedInterceptors(key, attackerOwner){
  const out=[];
  rowArr(key).forEach((c,i)=>{
    if(c && c.kind==='creature' && !c.blocked && c.owner!==attackerOwner) out.push({key,i,c});
  });
  minionsInRow(key).forEach(g=>{
    if(g.owner!==attackerOwner && !g.c.tapped && !g.c.sick) out.push({key,c:g.c});
  });
  return out;
}
```

> The function name is a **misnomer** — it does not check `tapped` for board creatures. Do not "fix"
> this while porting unless you intend a rules change; the comment above it states the intent
> explicitly: *"A creature may block even tapped or summoning-sick; blocking is gated once-per-turn by
> `blocked`."*

**Board-creature blocker predicate:**

| Condition | Required |
|---|---|
| `kind === 'creature'` | yes |
| `owner !== attackerOwner` | yes — note this is *not* "the defending player"; it is "anyone not on the attacking side", so a third-party unit standing in the center counts |
| `!blocked` | yes |
| `tapped` | **irrelevant** |
| `sick` | **irrelevant** |
| `worker` (board slot) | irrelevant — workers never occupy board slots in practice |
| column | **irrelevant** |

**Worker-stack blocker predicate** (a worker "screens its whole row"):

| Condition | Required |
|---|---|
| `owner !== attackerOwner` | yes |
| `!tapped` | **yes** |
| `!sick` | **yes** |
| `blocked` | **not checked** — workers are not gated by `blocked` at all |

Note the two asymmetries (board creature vs worker) are inverted from each other. Both are load-bearing
as written.

**Ref shape:** board blockers yield `{key, i, c}`; worker blockers yield `{key, c}` with **no `i`**.
Any C# ref type must model "cell blocker" and "pool blocker" as distinct cases.

### 4.3 The full eligibility set

`15_combat.js:21`

```js
function eligibleInterceptors(attackerOwner, aIdx, tIdx){
  let out=[];
  rowsCrossedInto(aIdx,tIdx).forEach(key=>{ out = out.concat(untappedInterceptors(key, attackerOwner)); });
  return out;
}
```

Enumeration order (deterministic, and it matters because the AI slices from it):
**crossed rows in travel order → within a row, board slots 0..6 ascending → then that row's workers
(you-pool before foe-pool in the center, `05_board_state.js:36`).**

### 4.4 Per-declaration exclusions

Applied by the caller, not by `eligibleInterceptors`:

| Exclusion | Where | Rule |
|---|---|---|
| The attack's **target itself** (`r.c !== tgt`) | `15_combat.js:253`, `16_movement.js:70`, `17_turns_ai.js:334`, `42_mp_apply.js:226` | the target *retaliates*, it does not "block" |
| The **targeted worker pool** (`minPool(defender, wWhich).includes(r.c)`) | `15_combat.js:253`, `15_combat.js:181` | a stack cannot screen itself |
| Everything, if the attacker has **Scour** | `15_combat.js:251`, `16_movement.js:69`, `17_turns_ai.js:333` | fliers are unblockable |
| Everything, if `aIdx === tIdx` | `15_combat.js:251` etc. | point-blank duel |

**Scour granularity differs between paths:**
* Combat v3 (`CMB.declare`) evaluates `kwOf(A) !== 'scour'` **per attacker** (`15_combat.js:251`).
* The legacy path uses `groupIsScour(attackers)` = *every* attacker must be Scour
  (`06_mana_workers.js:174`, `16_movement.js:65`, `16_movement.js:96`).

---

## 5. Targets

Any of these may be declared as an attack target:

| Target | Declaration kind | `tIdx` | Notes |
|---|---|---|---|
| Enemy creature | `'unit'` | `rowIdx(targetRow)` | retaliates |
| Enemy building | `'unit'` | `rowIdx(targetRow)` | never retaliates |
| Enemy face-down charge | `'unit'` | `rowIdx(targetRow)` | provoked — flips and may fight back |
| Enemy set trap | `'unit'` | `rowIdx(targetRow)` | springs, deals no combat damage back |
| Enemy **castle wall / life pool** | `'wall'` | `-1` (foe) / `5` (you) | one-way damage to `P.life` |
| Enemy **worker stack** of one zone | `'workers'` | `rowIdx(zoneRow)` | resolved by the legacy `resolveCombat` |

Routing (`15_combat.js:216-224`):

```js
window.routeAttack = function(kind,a,b,c){
  if(inMPGame()){ /* legacy single-shot path */ }
  if(kind==='unit')       CMB.declare('unit', a, b);
  else if(kind==='wall')  CMB.declare('wall', null, null);
  else                    CMB.declare('workers', WELL2ROW[a]||a, null, c);
};
```

`WELL2ROW` maps FX strip ids to row keys (`15_combat.js:172`):
`wellFoeBack→foeBack, wellFoeFront→foeFront, wellCenter→center, wellYouFront→youFront, wellYouBack→youBack`.

Call sites: enemy cell tap (`13_input.js:166`), enemy ♥ tap (`12_render.js:330`), enemy worker chip tap
(`12_render.js:103`, `12_render.js:208`).

---

## 6. The declaration protocol — player attacking (Combat v3)

### 6.1 State machine

```
                    ┌──────────────────────────────────────────────┐
                    │              ACTION PHASE (idle)             │
                    │  G.atk = [] , G.decls = []                   │
                    └──────────────┬───────────────────────────────┘
        select attacker(s)         │  (tap own ready creature / marquee)
                                   ▼
                    ┌──────────────────────────────────────────────┐
                    │              AIMING                          │
                    │  G.atk = [refs] , canAttack() == true        │
                    │  every enemy object + enemy ♥ is lit         │
                    └──────────────┬───────────────────────────────┘
        tap a target               │
                                   ▼
        ┌──────────────────────────────────────────────────────────┐
        │  DECLARE (CMB.declare)  — one pass over G.atk:           │
        │    for each attacker A (in selection order):             │
        │       validate A; A.tapped = true                        │
        │       push declaration d {A-ref, kind, target, blockers} │
        │       DEFENDER ANSWERS IMMEDIATELY:                      │
        │         compute crossed rows -> eligible interceptors    │
        │         AI picks blockers; each gets blocked = true      │
        │         and is appended to d.blockers                    │
        │    clearAtk()                                            │
        └──────────────┬────────────────────────────┬──────────────┘
      more attackers?  │                            │ press "⚔ Resolve"
      (back to AIMING) │                            ▼
                       │        ┌────────────────────────────────────┐
                       │        │  RESPONSE WINDOW (RESP.actingGate) │
                       │        │  3/4/6 s "Opponent may respond…"   │
                       │        │  (setting `srd.respwin`; 'off'=0)  │
                       │        └───────────────┬────────────────────┘
                       │                        ▼
                       │        ┌────────────────────────────────────┐
                       │        │  RESOLVE (CMB._resolveNow)         │
                       │        │  G.busy = true, G.decls = []       │
                       │        │  §7                                │
                       │        └───────────────┬────────────────────┘
                       │                        ▼
                       └───────────────►  back to ACTION PHASE (idle)
```

**End-turn is blocked while declarations are pending** (`17_turns_ai.js:227`):
`if (CMB.hasDecls()) { CMB.hint(); render(); return; }`.

`G.decls` is wiped at every `startTurn` (`17_turns_ai.js:50`) and at `startGame` (`09_game_start.js:5`).

### 6.2 `CMB.declare` — exact algorithm

`15_combat.js:235-262`

```
INPUT: kind ∈ {unit, wall, workers}, tk (target row key or null), ti (target slot or null),
       wWhich (worker zone name or undefined)

 1. GUARD: if G.turn != 'you' or G.busy or G.over or G.phase != 'action' -> return (no-op)
 2. refs := copy of G.atk
 3. tgt := (kind == 'unit') ? unitAt(tk, ti) : null
 4. if kind == 'unit' and tgt is null -> clearAtk(); render(); return       // target vanished
 5. any := false
 6. FOR EACH ref IN refs, in order:
      a. A := rowArr(ref.k)[ref.i]
      b. if A is null OR A.kind != 'creature' OR A.owner != 'you'
            OR A.worker OR A.sick OR A.tapped   -> skip this ref (continue)
      c. A.tapped := true ; any := true
      d. d := { a: ref, kind, tk, ti, wWhich, blockers: [] }
      e. append d to G.decls
      f. log "⚔ <A.nm> declares an attack on <target name>."
      g. aIdx := rowIdx(ref.k)
         tIdx := (kind == 'wall') ? -1 : rowIdx(tk)
      h. IF kwOf(A) != 'scour' AND aIdx != tIdx:
            elig := eligibleInterceptors('you', aIdx, tIdx)
                      filtered by  r.c != tgt
                      and by      NOT (kind == 'workers' AND minPool('foe', wWhich) contains r.c)
            chosen := aiChooseInterceptors([A], {
                          kind:  (kind=='wall') ? 'base' : (tgt ? tgt.kind : 'creature'),
                          cc:    (kind == 'wall'),
                          elig,
                          power: effA(A)
                      })
            FOR EACH r IN chosen:
                r.c.blocked := true
                append r to d.blockers
                log "The enemy interposes <name> against <A.nm>!"
 7. clearAtk()
 8. if any -> CMB.hint()  else defaultHint()
 9. render()
```

Notes an implementer must not lose:

* **The attacker taps at declaration time**, not at resolution (`15_combat.js:244`).
* **Blockers are NOT tapped at declaration time.** They are tapped inside `CMB.pairFight`
  (`15_combat.js:268`). They *are* flagged `blocked` immediately, which removes them from the
  eligibility pool for every subsequent declaration in the same joint attack — so one creature can
  never block two attackers.
* A "joint attack" is simply **N declarations that share the same `(tk, ti)`**. They are regrouped by
  target *object identity* at resolve time (`15_combat.js:330-331`). There is no group object.
* Each declaration in a joint attack gets its **own independent blocker answer**. The union-of-crossed-
  rows behaviour therefore emerges naturally: attacker #1 in `youBack` and attacker #2 in `center`
  hitting the same `foeFront` target expose crossed-row sets `{youFront, center, foeFront}` and
  `{foeFront}` respectively; the defender may block each from its own set. **There is no single merged
  interval** — the union is per-declaration, not computed globally. (The phrase "union of crossed rows"
  describes the *observable* result: across the whole joint attack, the defender may commit blockers
  drawn from the union of the individual intervals, but each blocker is bound to one specific attacker.)
* The declaration stores the attacker as **board coordinates** `{k,i}`, not as an object reference.
  See §12.1 for the consequences.

### 6.3 `aiChooseInterceptors` — the defending AI's block policy

`15_combat.js:70-84`

```js
function aiChooseInterceptors(attackers, info){
  const elig = info.elig || [];  if(!elig.length) return [];
  const P = info.power != null ? info.power : sumA(attackers);

  if(info.cc){                                   // defending the life pool / castle wall
    if(!(P >= G.P.foe.life || P >= 4)) return [];
    const survivor = elig.filter(r => r.c.h > P).sort((a,b) => a.c.h - b.c.h)[0];
    if(survivor) return [survivor];              // one blocker that survives, cheapest such
    return elig.sort((a,b) => a.c.h - b.c.h).slice(0,2);   // otherwise chump with the two weakest
  }
  if(info.kind === 'charge'){                    // worth a body to save a funded face-down
    const survivor = elig.filter(r => r.c.h > P).sort((a,b) => a.c.h - b.c.h)[0];
    if(survivor) return [survivor];
  }
  return [];                                     // never trade a body to save a single creature
}
```

| `info.kind` / `info.cc` | Behaviour |
|---|---|
| `cc: true` (target is the **wall/life pool**) | Threshold `P >= foe.life || P >= 4`. **On the current ×500 stat scale `P >= 4` is true for every non-zero attacker**, so the AI effectively *always* defends its wall. Prefers the single lowest-HP blocker that would survive (`h > P`); if none survives, chump-blocks with the **two** lowest-HP eligible units — this is the canonical gang-block. |
| `kind: 'charge'` (face-down target) | Blocks with the lowest-HP survivor, or not at all. |
| anything else (creature, building, trap, workers) | **Never blocks.** |

`info.kind` for a workers declaration is `'creature'` (because `tgt` is null) → **the AI never blocks a
worker-stack strike in Combat v3** (`15_combat.js:254`).

In multiplayer this function is monkey-patched to consume the remote guest's choice instead of running
the heuristic (`43_mp_intents.js:150-159`), re-validating each ref by *object identity* against the
eligibility list.

---

## 7. Resolution — `CMB._resolveNow`

`15_combat.js:309-366`. This is an **async** routine: it awaits FX lunges and player prompts.
Port it as a coroutine / explicit state machine, not as a synchronous function.

```
 0. Entry gate (CMB.resolve, 15_combat.js:304-308):
      require G.turn=='you' && !G.over && G.phase=='action' && G.decls.length > 0
      run through RESP.actingGate('attack', …) if that layer is loaded (see §10.1)

 1. decls := G.decls ; G.decls := []            // re-entrancy safe
    G.busy := true

 2. live := decls
        .map(d => { ...d, A: rowArr(d.a.k)[d.a.i],
                          tgt: d.kind=='unit' ? unitAt(d.tk, d.ti) : null })
        .filter(x => x.A && x.A.kind=='creature' && x.A.h > 0)
    attackers := live.map(x => x.A)

 3. dischargeOvercharge(attackers)              // sets _dis := oc, oc := 0 for Overcharge units

 4. PARTITION (order matters — computed BEFORE any damage):
      blocked := live.filter(x => x.blockers.some(r => r.c && r.c.h > 0))
      open    := live.filter(x => !blocked.includes(x))
    // "a blocked attacker stays blocked even if it kills its whole gang in the fight"

 5. STEP 1 — blocked declarations (pair fights), in declaration order:
      FOR EACH x IN blocked:
          blks := x.blockers.map(r=>r.c).filter(b => b && b.h > 0)
          if blks is empty -> continue
          ab := 0
          if blks.length > 1:
              G.busy := false
              ab := await askAbsorb(x.A, blks)      // PLAYER chooses the absorber
              G.busy := true
          await CMB.pairFight(x.A, x.blockers.filter(r=>r.c && r.c.h>0), ab, x.a)
          if G.over -> { G.busy := false; return }

 6. STEP 2 — unblocked strikes on CREATURES, grouped by target object:
      byT := ordered map target -> [declarations]
      FOR EACH x IN open:
          if x.kind=='unit' && x.tgt && x.tgt.kind=='creature' && x.A.h > 0:
              byT[x.tgt].push(x)
      FOR EACH (T, xs) IN byT (insertion order):
          grp := xs.map(x=>x.A).filter(a => a.h > 0)
          if grp empty or T.h <= 0 -> continue
          springAttackTrap('foe', grp, T)          // defender's trigger:'attack' trap — auto
          log "You attack <T> with <grp.length> creature(s)."
          await CMB.targetFight(grp, T, /*ri=*/0, fxTargetCell, srcRefs)
          if G.over -> { G.busy := false; return }

 7. STEP 3 — everything else unblocked, in declaration order:
      wallDmg := 0 ; scourHits := []
      FOR EACH x IN open:
          if x.A.h <= 0 -> continue
          CASE x.kind == 'wall':
              wallDmg += effA(x.A)
              if kwOf(x.A)=='scour' -> scourHits.push(x.A)
              continue
          CASE x.kind == 'workers':
              log "<A> strikes the enemy Minions."
              resolveCombat([x.A], minPool('foe', x.wWhich).slice())      // legacy engine, §8
              if kwOf(x.A)=='scour' && x.A.h>0 -> scourHits.push(x.A)
              continue
          o := x.tgt ; if o is null -> continue
          CASE o.kind == 'creature':                                       // already fought in STEP 2
              if kwOf(x.A)=='scour' && x.A.h>0 -> scourHits.push(x.A)
              continue
          CASE o.kind == 'building':
              springAttackTrap('foe', [x.A], o)
              log "You strike the enemy <o.nm>."
              clashFx([x.A],[o])                                           // FX only
              applyDmg(focusFire([x.A],[o])) ; cleanup()                   // ONE-WAY, no retaliation
          CASE o.kind == 'charge':  provokeFaceDown('foe', x.tk, x.ti, [x.A])
          CASE o.kind == 'trap':    springTrap('foe', x.tk, x.ti, [x.A])
          if kwOf(x.A)=='scour' && x.A.h>0 -> scourHits.push(x.A)

 8. WALL DAMAGE (applied once, after all of step 7):
      if wallDmg > 0:
          G.P.foe.life := max(0, G.P.foe.life - wallDmg)
          log "You storm the castle wall — ⚔<wallDmg> …(♥<life> remains)"

 9. scourHits.forEach(a => { if(a.h>0) scourStrike(a,'foe'); })
    if scourHits.length -> cleanup()

10. clearDischarge(attackers)     // _dis := 0
    G.busy := false
    defaultHint(); render(); checkWin()
```

### 7.1 Ordering guarantees

* **Damage is simultaneous only within a tier of a single fight.** Across declarations, resolution is
  strictly sequential in the order listed above, with a `cleanup()` (death sweep + death triggers)
  after each fight. A creature killed in step 5 cannot participate in step 6.
* Wall damage is the **only** aggregated quantity — every wall declaration's `effA` is summed and
  applied once, after all other combat.
* `checkWin()` runs **only at the very end** (step 10). In solo, nothing inside steps 5–7 can set
  `G.over` (neither `cleanup()` nor `provokeFaceDown` calls `checkWin`), so the `if (G.over) return`
  guards are defensive only. Preserve them anyway for future netcode.

### 7.2 `CMB.pairFight` — a blocked attacker vs. its gang

`15_combat.js:263-284`

```
INPUT: A (attacker unit), blkRefs (blocker refs), ab (absorber index), aRef (attacker board ref)

 1. blks := blkRefs.map(r => r.c || r).filter(b => b && b.h > 0)
 2. if blks empty OR A is null OR A.h <= 0 -> return
 3. FOR EACH b IN blks: b.tapped := true          // blocking taps the blocker
 4. await FX lunge (presentation)
 5. group := [A]
    applyUndertow(group, blks)                    // §9.2 — a blocking Undertow warden bounces A
 6. if group is empty OR A.h <= 0 -> cleanup(); render(); return
 7. absorber := blks[ clamp(ab, 0, blks.length-1) ]
 8. dmg := ordered map unit -> int ; hit(u,d) := dmg[u] += d
 9. TIER(fs):
        if (A.fs == fs) and A.h > 0 and absorber.h > 0:
            hit(absorber, effA(A))                // the attacker's ENTIRE blow, to ONE blocker
        FOR EACH b IN blks:
            if (b.fs == fs) and b.h > 0 and A.h > 0:
                hit(A, b.a)                       // EVERY blocker retaliates, raw `a` (no _dis)
        apply: for (u,d) in dmg: u.h -= d
        clear dmg
10. TIER(true)      // First Strike tier
11. TIER(false)     // main tier
12. cleanup(); render()
```

Key rules encoded here:

* **No damage splitting for the attacker:** `A` deals `effA(A)` in full to exactly one blocker.
* **Every blocker retaliates in full**, simultaneously, using raw `a` — blockers never benefit from
  Overcharge discharge (only attackers call `dischargeOvercharge`).
* **First Strike is a full pre-tier.** A First-Strike attacker that kills the absorber in tier 1 still
  takes retaliation from the *other* surviving blockers in tier 2 (their `A.h > 0` check passes).
  A First-Strike blocker that kills `A` in tier 1 stops `A` from ever striking (in tier 2 the
  `A.h > 0` guard fails).
* Condition freshness: `A.h`, `b.h`, `absorber.h` are read at the **start of the tier** (before that
  tier's damage lands), which is what makes the tier simultaneous.

### 7.3 `CMB.targetFight` — an unblocked joint attack on one creature

`15_combat.js:285-302`

```
INPUT: grp (attackers), T (target creature), ri (retaliation index), fxTo, srcRefs

 1. applyUndertow(grp, [T])           // an Undertow TARGET bounces the costliest attacker
 2. grp := grp.filter(a => a && a.h > 0)
 3. if grp empty OR T null OR T.h <= 0 -> cleanup(); render(); return
 4. await FX lunge (presentation)
 5. back := grp[ clamp(ri, 0, grp.length-1) ]      // the single retaliation victim
 6. dmg := ordered map ; hit(u,d) := dmg[u] += d
 7. TIER(fs):
        FOR EACH a IN grp:
            if (a.fs == fs) and a.h > 0 and T.h > 0: hit(T, effA(a))
        if (T.fs == fs) and T.h > 0 and back.h > 0: hit(back, T.a)
        apply; clear
 8. TIER(true); TIER(false)
 9. cleanup(); render()
```

* Every attacker's blow lands **on the target** — full value each, no splitting, no overkill spillover.
* The target retaliates **once**, at full `T.a`, against exactly one attacker.
* **Who picks the retaliation target:**
  * When the *player* attacks, `ri` is hard-coded to `0` (`15_combat.js:337`) → the AI defender
    always retaliates against the **first-declared** attacker in that target's group. The AI does not
    choose. (The comment "AI retaliation is auto (its own pick)" overstates it.)
  * When the *AI* attacks, the player is prompted via `askRetaliate` whenever the group has >1 member
    (`17_turns_ai.js:362-363`).

### 7.4 Player choice prompts

`16_movement.js:160-181`

| Prompt | Signature | Shown when | Returns |
|---|---|---|---|
| `askAbsorb(A, blks)` | "Assign the blow — `A` (⚔effA) is gang-blocked — choose which blocker takes its damage. Every blocker still strikes it back." | attacker (player) has >1 live blocker on a declaration | index into `blks` |
| `askRetaliate(T, grp)` | "Strike back — your `T` is attacked by N — choose which attacker it retaliates against (full ⚔`T.a`)." | defender (player) is jointly attacked by >1 | index into `grp` |
| `askBlock(opts)` | full blocker chooser with multi-select and a "Let it through" pass | defender (player) answers an AI declaration | list of blocker refs |

`askPick` auto-resolves to `0` when `units.length <= 1` without showing UI (`16_movement.js:162`).
`askBlock` supports an optional `opts.ms` deadline (MP only) that auto-passes (`16_movement.js:151-154`).

---

## 8. The legacy damage engine (`resolveCombat` / `focusFire`)

Combat v3 replaced this for creature-vs-creature fights, but it is **still live** in four places:

1. worker-stack strikes in Combat v3 step 3 (`15_combat.js:346`),
2. provoked face-downs that flip into creatures (`15_combat.js:97`),
3. the legacy solo `doAttack` / `attackBackRow` (`16_movement.js:59-113`) — still reachable through
   `routeAttack` when `inMPGame()` is true,
4. the entire multiplayer path (`42_mp_apply.js:199-259`).

You must port it too, or explicitly redesign those four cases.

### 8.1 `focusFire(dealers, targets) -> Map<target,int>`

`15_combat.js:23-45`. "Assign each dealer to ONE target (no spillover). Greedy lethal-first."

```
 1. dmg := ordered map; every target -> 0
 2. if targets empty -> return dmg
 3. avail := dealers with effA > 0, sorted DESCENDING by effA
 4. used := empty set
 5. order := targets sorted ASCENDING by current h        // cheapest kill first
 6. FOR EACH t IN order:
        need := t.h - dmg[t]
        if need <= 0 -> continue
        free := avail minus used, sorted ASCENDING by effA
        tryUse := [] ; n := need
        FOR EACH d IN free:
            if n <= 0 -> break
            tryUse.push(d) ; n -= effA(d)
        if n <= 0:                                        // lethal is reachable
            FOR EACH d IN tryUse: used.add(d); dmg[t] += effA(d)
        // else: commit NOTHING to this target
 7. leftover := avail minus used
    if leftover non-empty:
        t := targets sorted DESCENDING by h, take [0]     // the toughest target
        FOR EACH d IN leftover: dmg[t] += effA(d)
 8. return dmg
```

Behavioural consequences:

* Dealers with `effA == 0` (e.g. workers, Sap Pod) are dropped entirely and never appear in `leftover`.
* A target that cannot be killed by the remaining free dealers receives **nothing** in the main loop —
  chip damage only ever lands on the single toughest target, via the leftover rule.
* Because `dmg[t]` starts at 0 and each target is visited once, `need == t.h` always.
* Sorting is on **current** `h`, read before any damage is applied.

`applyDmg(map)` (`15_combat.js:46`) is just `for (t,d) in map: t.h -= d`.

### 8.2 `resolveCombat(groupA, groupB)`

`15_combat.js:48-65`

```
 1. applyUndertow(groupA, groupB)                 // defenders' Undertow fires FIRST
 2. live(arr) := arr.filter(c => c && c.h > 0)
 3. aFS := groupA.filter(c => c.fs) ; bFS := groupB.filter(c => c.fs)
    if aFS or bFS non-empty:
        dA := focusFire(aFS, live(groupB))
        dB := focusFire(bFS, live(groupA))
        applyDmg(dA); applyDmg(dB)                // simultaneous First-Strike exchange
 4. mainA := groupA.filter(c => !c.fs && c.h > 0)
    mainB := groupB.filter(c => !c.fs && c.h > 0)
    dA := focusFire(mainA, live(groupB))
    dB := focusFire(mainB, live(groupA))
    applyDmg(dA); applyDmg(dB)
 5. cleanup()
```

* First-Strike units strike **once**, in the pre-tier only; they do not strike again in the main step.
* Anything killed in the pre-tier never strikes back (it is excluded by `c.h > 0` / `live()`).
* `resolveClash(attackers, blockers)` (`15_combat.js:67`) is a one-line alias. Nothing calls it.
* Worker stacks as `groupB`: every worker has `a = 0`, so `focusFire(mainB, …)` produces an all-zero
  map — **workers deal no retaliation damage**, but they do soak. With one attacker, at most **one**
  worker takes damage per strike (the lowest-HP one if lethal, else the highest-HP one via leftover).

---

## 9. Keywords that touch combat

`06_mana_workers.js`. Definitions in `03_cards_creatures.js:5-19`.

| Keyword | Element | Combat hook | Exact effect |
|---|---|---|---|
| **First Strike** (`fs`, not a `kw`) | one cost-3 card per element | tiering | strikes in the pre-tier; see §7.2/§7.3/§8.2 |
| **Scour** | wind | pre-block + on-hit | attacker ignores all interceptors; after a connecting strike, `scourStrike` destroys the first face-down/trap in the defender's **back row**, else sets `h = 0` on the first non-`cc` building there (`06_mana_workers.js:165-173`) |
| **Undertow** | water | defensive, pre-damage | see §9.2 |
| **Entrench** | earth | defensive | immune to the Undertow bounce (and to the `bounce` spell) — checked as `!c.entrench` (`06_mana_workers.js:137`) |
| **Overcharge** | electric | attack prep | banks `oc` (max 3) each upkeep (`06_mana_workers.js:154-157`); on attacking, `_dis := oc; oc := 0` (`06_mana_workers.js:159-162`), adding `_dis` to `effA` for that resolution only |
| **Detonate N** | fire | on death | see §11.2 |
| **Reap N** | dark | on death | see §11.2 |
| **Ward** | light | on entry | not combat (spawns a 0/`wardhp` Lumen token blocker) |
| **Chrysalis** | forest | upkeep | re-applies `sick` each upkeep → effectively "cannot attack" (`06_mana_workers.js:144-152`) |

### 9.1 Overcharge scale bug (must decide during port)

`o.oc = Math.min(3, (o.oc||0)+1)` and `a._dis = a.oc`. Attack values are on a ×500 scale (500 … 4500)
but the discharge bonus is **+1, +2 or +3 raw points** — three orders of magnitude too small to matter.
This is almost certainly a missed conversion when the stat scale was multiplied by 500. Either scale it
(`_dis = oc * 500`) or delete the mechanic; do not port it as-is without a decision.

### 9.2 `applyUndertow(groupA, groupB)`

`06_mana_workers.js:135-142`

```
 1. wardens := groupB.filter(c => c && kwOf(c)=='undertow' && c.h > 0)
    if none -> return
 2. marks := groupA.filter(c => c && c.kind=='creature' && c.h > 0
                            && !c.worker && !c.token && !c.entrench && !c.cc)
                   .sort DESCENDING by (c.c || 0)          // by MANA COST, not by attack
 3. a := marks[0] ; if none -> return
 4. ow := removeUnitFromBoard(a)                            // clears its board cell (or pool entry)
 5. if ow:
        G.P[ow].hand.push(handcardFromCreature(a))          // returns to OWNER's hand at FULL maxh
        remove a from groupA (splice)
        log "Undertow! <warden> hurls <a> back to <owner>'s hand."
```

* Fires **before any damage**, in `resolveCombat` (step 1), `CMB.pairFight` (step 5) and
  `CMB.targetFight` (step 1).
* "Strongest" in the code comment means **highest mana cost**, not highest attack. Ties resolve by
  the array's existing order (JS `sort` is stable).
* Exactly **one** creature is bounced per call, regardless of how many wardens are present.
* In `CMB.pairFight`, if the bounced creature is the attacker `A`, the fight ends immediately —
  **the blockers take and deal no damage at all** (`15_combat.js:274`).
* In `CMB.targetFight`, the bounced attacker is filtered out and the rest of the group still fights.
* The bounced card returns as a **hand card at full printed HP** (`handcardFromCreature`,
  `06_mana_workers.js:112-113`), so it heals, and it will be summoning-sick when replayed.

---

## 10. Traps and the response window

### 10.1 The response window (RESP layer, `30_resp.js`)

This is a monkey-patch layer loaded after the FX wrappers. It is *anti-tell* machinery: it inserts a
constant-length pause so the opponent's decision time never leaks information.

**Acting side** (`30_resp.js:35-51`) — `RESP.actingGate(trigger, then)`:

```
 if G.over or RESP.active                 -> return (the action is silently dropped)
 if G.turn != 'you'                       -> then(null); return
 if multiplayer active                    -> then(null); return       // MP owns its own windows
 d := RESP.dur()                          // 'off'->0, else 3000/4000/6000 ms; MP is always 4000
 if d <= 0                                -> then(null); return
 RESP.active := true ; G.busy := true
 show "Opponent may respond…" pill with a countdown
 after d ms: hide, RESP.active := false, G.busy := false, if !G.over then(null)
```

`CMB.resolve` routes through this (`15_combat.js:307`). Because `RESP.actingGate` **drops the call**
when `RESP.active` is already true, a double-press of ⚔ Resolve during the window is a no-op and the
declarations survive (they are only consumed inside `_resolveNow`).

**Defending side** (`30_resp.js:57-85`) — `RESP.defendWindow(trigger, ctx)` returns a promise of a
chosen trap ref (or null). It offers one button per armed trap plus **⏸ Pause** (a fresh 15 s timer)
and **Pass**. Timeout = auto-pass. Setting key: `localStorage['srd.respwin']`, default `'4'`.

The RESP layer also re-wraps `onCell` / `onHand` to ignore input while a window is open
(`30_resp.js:102-103`), and wraps the **legacy** `doAttack` / `attackBackRow` / `attackMinionStack`
(`30_resp.js:107-112`). It does **not** wrap `CMB.declare`; declaring is free and instantaneous.

> **Design decision for the port:** in a PC single-player build the acting-side window is pure delay
> with no opponent decision behind it (the AI's trap auto-springs inside `then`). It exists solely so
> the AI's "did it hold a trap?" cannot be read from timing. Keep it if you keep the AI's hidden traps
> and intend to layer MP on; otherwise make it a settings-driven zero.

### 10.2 Armed-trap discovery

`14_spells_traps.js:34-40`

```js
function findArmedTrap(owner, trigger){
  for(const w of ['front','back'])
    for(let i=0;i<SLOTS;i++){ const o = G.P[owner][w][i];
      if(o && o.kind==='trap' && o.card.trigger===trigger && G.turnNo > (o.setTurn ?? 0)) return {o,w,i}; }
  for(let i=0;i<SLOTS;i++){ const o = G.center[i];
    if(o && o.kind==='trap' && o.owner===owner && o.card.trigger===trigger && G.turnNo > (o.setTurn ?? 0)) return {o,w:'center',i}; }
  return null;
}
```

Search order is fixed and deterministic: **front 0..6 → back 0..6 → center 0..6**. A trap is *armed*
only from the turn **after** it was set (`G.turnNo > setTurn`).
`RESP` has a plural sibling `findArmedTraps` returning all of them (`30_resp.js:10-17`).

### 10.3 Trap cards (`03_cards_creatures.js:86-95`)

| Name | Cost | Trigger | Effect | Value |
|---|---|---|---|---|
| Snare Pit | 1 | `summon` | `pitfall` — destroy the summoned creature | — |
| Whirl Trap | 1 | `summon` | `pitfall` | — |
| Collapsing Floor | 1 | `summon` | `pitfall` | — |
| **Overgrowth** | 1 | `attack` | `thornmail` — the defender permanently gains **+500 ⚔ / +1000 ♥** (and +1000 maxh) | — |
| **Backlash** | 1 | `attack` | `burn` — every attacker takes damage | **1500** |

Setting a card face-down costs ◆1 (`13_input.js:220-225`).

### 10.4 `springAttackTrap(defOwner, attackers, defender)`

`15_combat.js:110-118` — fires the defender's `trigger:'attack'` trap. **Auto-resolving** (it can only
help the defender, so there is no choice UI on the AI side).

```
 t := findArmedTrap(defOwner, 'attack') ; if none -> return
 card := t.o.card
 log "<card.nm> springs as <side>'s line is struck!"
 if card.effect == 'thornmail':
     if defender is a creature and !defender.cc:
         defender.a    += 500
         defender.maxh += 1000
         defender.h    += 1000                     // PERMANENT
 else if card.effect == 'burn':
     for each a in attackers: a.h -= (card.val || 0)
 push spellRec(card) to G.P[defOwner].grave
 clear the trap's cell
```

**It does not call `cleanup()`.** Creatures killed by Backlash are swept by the next `cleanup()`
(inside the fight that immediately follows).

**Where it fires in Combat v3 (player attacking):**

| Situation | Trap fires? | Source |
|---|---|---|
| Unblocked attack on a creature (once per target group, before the fight) | ✅ | `15_combat.js:335` |
| Unblocked attack on a building (before the one-way damage) | ✅ | `15_combat.js:350` |
| **Blocked** declaration (pair fight) | ❌ | never called in step 5 |
| Attack on the castle wall | ❌ | never called in step 7's wall branch |
| Attack on a worker stack | ❌ | — |
| Attack on a face-down charge or a set trap | ❌ | — |

Because each call re-runs `findArmedTrap`, **multiple traps can spring in one resolution** — one per
target group and one per struck building, in that order.

### 10.5 `springTrap(defOwner, key, slot, attackers)` — the trap CARD is attacked

`15_combat.js:100-109`

```
 t := arr[slot] ; require t.kind == 'trap'
 log "<card.nm> springs on the attacker(s)!"
 if card.effect == 'pitfall':
     v := attackers sorted DESCENDING by raw `a`, take [0]
     if v: v.h := 0                          // destroyed outright
 else if card.effect == 'burn':
     for each a in attackers: a.h -= card.val
 // 'thornmail' has no creature defender here — it simply fizzles
 push spellRec(card) to grave ; clear the cell ; cleanup()
```

Note: attacking a set trap deals **no attacker damage to anything** — the trap is removed regardless of
the attacker's power, and the attacker is simply exposed to the trap's effect. `pitfall` uses raw `a`,
not `effA`.

### 10.6 `provokeFaceDown(defOwner, key, slot, attackers)` — a face-down CHARGE is attacked

`15_combat.js:86-99`

```
 o := arr[slot] ; require o.kind == 'charge'
 IF o.inv < o.card.c:                       // under-funded → INTERRUPTED
     log "The strike catches a half-formed card — interrupted! ◆<inv> banked is lost."
     toGrave(defOwner, o) ; arr[slot] := null ; cleanup() ; return
     // the attacker deals no damage anywhere and takes none
 log "Provoked! <side>'s face-down erupts to meet the attack."
 flip(defOwner, key, slot)                  // becomes a live unit; surplus ◆ banks onto it
 now := arr[slot]
 IF now is a creature: resolveCombat(attackers, [now])       // §8.2 — it fights back at FULL power
 ELSE IF now:          applyDmg(focusFire(attackers,[now])); cleanup()   // a structure just takes it
```

`flip` (`14_spells_traps.js:110-127`) sets `sick = (G.turnNo <= setTurn)` — a card set on an earlier
turn flips **not sick** and therefore fights at full strength. It also runs `onCreatureEnter` (Ward
token) and `syncWorkers`.

---

## 11. Death, cleanup, and triggers

### 11.1 `cleanup()`

`16_movement.js:190-205`

```
 any := true ; guard := 0
 WHILE any AND guard++ < 40:
     any := false
     FOR EACH key IN ROWS (foeBack, foeFront, center, youFront, youBack):
         b := rowArr(key)
         FOR i := 0 .. SLOTS-1:
             c := b[i]
             if c and (c.kind=='creature' or c.kind=='building') and c.h <= 0:
                 o := c.owner
                 log "<Your|Their> <c.nm> <is razed|falls>."
                 b[i] := null                                    // free the cell FIRST
                 if c.kind=='creature' and !c.worker: onCreatureDeath(c, o)
                 toGrave(o, c)
                 any := true
     FOR EACH owner IN ['you','foe']:
         FOR EACH w IN ['back','front','center']:
             pool := G.P[owner].min[w]
             FOR i := pool.length-1 DOWNTO 0:
                 if pool[i].h <= 0: toGrave(owner, pool[i]); pool.splice(i,1); any := true
```

Load-bearing details:

* **Deterministic order:** rows in `ROWS` order, slots ascending, then worker pools (you before foe,
  zones back → front → center, iterated backwards within a pool).
* **The cell is freed before the death trigger fires** — so a Reap token can be placed into the very
  cell that just emptied (`firstEmptyCell` scans back → front → center lanes,
  `06_mana_workers.js:105-107`).
* **Re-sweeping loop** so chained kills (Detonate hitting something to 0) resolve in one call, capped at
  40 iterations.
* Buildings do **not** fire a death trigger. Workers do **not** fire `onCreatureDeath`.
* `toGrave` is wrapped by the FX layer for death visuals only (`22_fx_wrappers.js:49-58`).

### 11.2 `onCreatureDeath(cr, owner)`

`06_mana_workers.js:124-133`

```
 if kwOf(cr) == 'detonate':
     n := cr.det || 0 ; if n <= 0 -> return
     cres := liveEnemyCreatures(owner) sorted by (b.a - a.a) then (a.h - b.h)   // deadliest, then frailest
     tgt := cres[0] || liveEnemyStructures(owner) sorted ASC by h, take [0]
     if tgt: tgt.h -= n ; log "Detonate! <cr> bursts for <n> into <tgt>."
 else if kwOf(cr) == 'reap':
     spot := firstEmptyCell(owner)
     if spot:
         a := cr.reap || 1
         token := mkToken(owner, 'Shade', a, a, cr.color) ; token.sick := true
         spot.arr[spot.i] := token
```

* Detonate never hits a command center (`!o.cc`; vacuously true today).
* `liveEnemyCreatures(owner)` excludes workers (`06_mana_workers.js:101-102`).
* Reap tokens enter **summoning-sick** — they cannot attack that turn but *can* block.
* Detonate values on cards: 1000 (Emberfly, Scorchling), 1500 (Infernox).
  Reap values: 500 (Wraithling, Grimfang), 1000 (Dreadmaw), 1500 (Maledict), 2000 (Voidwyrm).

### 11.3 `checkWin()`

`17_turns_ai.js:392-407`

```
 if G.over -> return
 youOut := G.P.you.life <= 0 ; foeOut := G.P.foe.life <= 0
 if foeOut or youOut:
     G.over := true
     win := foeOut AND !youOut          // mutual zero counts as a DEFEAT
     show banner; if a campaign duel is active, resolve the territory
```

The only loss condition is the life pool reaching 0. There is no deck-out loss (`doDraw` on an empty
deck merely logs, `17_turns_ai.js:80-81`).

---

## 12. The AI's attack turn — the mirrored protocol

`17_turns_ai.js:314-385`. Structurally the mirror image of §6/§7, but the **declaration order differs**:
the AI declares *everything* first, then the player answers each declaration in sequence.

```
 1. declared := []
    FOR EACH atk IN aiAttackers():                       // any untapped, unsick, non-worker foe creature,
                                                         // enumerated in ROWS order then slot order
        m := unitAt(atk.key, atk.i) ; if m null or m.tapped -> continue
        tref := aiPickTarget(m, atk.i) ; if null -> continue
        m.tapped := true
        declared.push({ m, a:{k,i}, aIdx: rowIdx(atk.key),
                        tIdx: tref.base ? ROWS.length : rowIdx(tref.key),
                        tref, blockers: [] })
        log "⚔ <m> declares an attack on <target> from <row>."

 2. if declared is empty -> skip the whole combat block

 3. ONE anti-tell response window over the WHOLE set:
        springRef := await RESP.defendWindow('attack', {desc: "<N> attacks declared…"})
    (the player may commit ONE trigger:'attack' trap here, or pass)

 4. PLAYER BLOCKS, one declaration at a time, in declaration order:
        FOR EACH d IN declared:
            if kwOf(d.m) == 'scour' OR d.aIdx == d.tIdx -> continue
            elig := eligibleInterceptors('foe', d.aIdx, d.tIdx).filter(r => r.c != d.tref.o)
            if elig empty -> continue
            blk := await askBlock({attacker:d.m, elig, title:"Incoming attack n/N", desc:…})
            FOR EACH r IN blk: r.c.blocked := true ; d.blockers.push({...r, c})
            // note: blockers are NOT tapped here either

 5. dischargeOvercharge(declared.map(d=>d.m))

 6. blockedD := declared with a live blocker ; openD := the rest

 7. FOR EACH d IN blockedD:                                // pair fights, AI picks its own absorber
        blks := live blockers
        ab := 0
        if blks.length > 1:
            kill := the lowest-HP blocker with h <= effA(d.m)            // kill the weakest killable
            ab := kill ? kill.index : index of the HIGHEST-HP blocker    // else hit the toughest
        await CMB.pairFight(d.m, live blocker refs, ab, d.a)

 8. byT := group openD by target creature object
    FOR EACH (T, ds) IN byT:
        grp := live attackers
        if springRef: RESP.springAttackTrapRef('you', springRef, grp, T); springRef := null   // ONCE
        ri := 0 ; if grp.length > 1: ri := await askRetaliate(T, grp)     // PLAYER chooses
        await CMB.targetFight(grp, T, ri, …)
        for each d in ds: if Scour and alive -> scourStrike(d.m,'you')
        cleanup()

 9. wallDmg := 0 ; scourHits := []
    FOR EACH d IN openD with d.m.h > 0:
        if d.tref.base:      wallDmg += effA(d.m); if Scour -> scourHits.push; continue
        if target is creature: continue                                  // fought in step 8
        if target is building: if springRef {springAttackTrapRef; springRef := null}
                               applyDmg(focusFire([d.m],[o])); cleanup()
        else if target is charge: provokeFaceDown('you', key, i, [d.m])
        else if target is trap:   springTrap('you', key, i, [d.m])
        if Scour and alive -> scourHits.push(d.m)

10. if wallDmg > 0: G.P.you.life := max(0, G.P.you.life - wallDmg)
11. scour strikes; clearDischarge; render(); checkWin()
```

### 12.1 AI target choice — `aiPickTarget(m, aCol)`

`17_turns_ai.js:256-266`. **Uses `Math.random()` — must be replaced with a seeded PRNG for
determinism.**

```
 fld := every board object owned by 'you', in ROWS order then slot order
 1. ch := funded-ish face-downs (kind=='charge' && inv >= 2), sorted DESC by inv, take [0]
    if ch and random() < 0.6 -> return ch
 2. kill := creatures (non-worker) with m.a >= o.h, sorted ASC by h, take [0]     // raw `a`, not effA
    if kill -> return kill
 3. bld := buildings sorted ASC by h, take [0]
    if bld and random() < 0.3 -> return bld
 4. otherwise -> {key:'youBack', i: clamp(aCol,0,6), base:true, o:null}           // storm the wall
```

The AI never targets worker stacks and never targets set traps deliberately (only incidentally via
step 1's face-down pick, which is `kind=='charge'`, a different thing).

### 12.2 Player-vs-AI protocol differences (do not accidentally unify them)

| Aspect | Player attacking | AI attacking |
|---|---|---|
| Declaration cadence | one target-tap at a time; defender answers **immediately** per declaration | all declarations made up front; defender answers **afterwards**, per declaration |
| Blocker choice | AI heuristic (`aiChooseInterceptors`) | player UI (`askBlock`), multi-select, "Let it through" |
| Absorber choice (gang block) | **player** picks (`askAbsorb`) | AI heuristic (weakest killable, else toughest) |
| Retaliation target (joint attack) | hard-coded `ri = 0` (first-declared attacker) | **player** picks (`askRetaliate`) |
| `trigger:'attack'` trap | AI's trap auto-springs, **once per target group and once per struck building** | player commits **one** trap in a single response window; it fires on the **first** creature group, else the first struck building |
| Response window | one `actingGate` pause before resolution | one `defendWindow` after all declarations |
| Resolution trigger | explicit **⚔ Resolve** button | automatic, immediately after blocks |

---

## 13. Rules vs. presentation vs. DOM workarounds

Everything in this list is **not** a rule. Do not port it as game logic.

### 13.1 Pure presentation

* `22_fx_wrappers.js` in its entirety — it wraps `applyDmg`, `resolveCombat`, `toGrave`, `doAttack`,
  `attackBackRow`, `attackMinionStack`, `springTrap`, `place`, `flip`, `doMove`, `render`, `startTurn`,
  `checkWin` and adds only sound, particles, damage numbers, screen shake, and the "battle cut-in"
  card flash. **No wrapper mutates game state.** Verified line by line.
* `CMB.pairFight` / `CMB.targetFight` `await lungeP(...)` (`15_combat.js:269-271`, `291-292`) — the
  lunge animation; `lungeP` resolves immediately when the FX layer is absent (`15_combat.js:226-228`).
* `clashFx`, `showBattle`, `ELEMFX.*`, `FX.*`, `SFX.*`.
* The `col` argument to `attackBackRow` — "kept for the FX layer's target rect only — columns never
  matter in combat" (`16_movement.js:90`).
* `combatV3CSS` (`15_combat.js:201-213`): `.declAtk` (gold outline = declared attacker), `.declTgt`
  (red outline = declared target), `.declBlk` (dashed blue = committed blocker). Applied in
  `12_render.js:447-451`.
* `shownPhase()` (`17_turns_ai.js:48`) — the tracker lights "Combat" alongside "Action" whenever
  `G.atk` or `G.decls` is non-empty. There is **no** separate combat phase in the state machine;
  `G.phase` stays `'action'`.

### 13.2 DOM / touch workarounds (drop entirely in Unity)

* `body.targeting` ghosting `#turnLabel` and raising the foe command cluster so the enemy ♥ is a
  reachable thumb target; `.keephp.lifeaim` padding out the heart's hit area (`15_combat.js:205-209`).
* `body.placing` making unselected hand cards inert (`15_combat.js:210-212`).
* `snapLegalCell` — 44 px projected-rect snapping for tap forgiveness on the tilted board
  (`12_render.js:383-392`).
* `rowCellEl(row, i)` skipping worker slots so DOM index lines up with the column
  (`22_fx_wrappers.js:4`).
* The "no board-drag while `G.atk` is held" guard (`31_ui_shell.js:185`) — stops a rolled tap from
  becoming a move and wiping the attack group.
* `_atkBusy` re-entrancy latch in `fxLunge` (`22_fx_wrappers.js:60-77`).
* `justDragged` click suppression (`31_ui_shell.js:289`).

### 13.3 Genuinely rules-relevant UI gates

* `G.busy` — a global "input locked, an async resolution is running" latch. In Unity this becomes an
  explicit resolver state, not a boolean on the model.
* `endTurn` refusing to advance while declarations are pending (`17_turns_ai.js:227`).
* `G.phase === 'action'` gating declaration and resolution.

---

## 14. Multiplayer notes (deferred, but shapes the design)

Combat v3 is **solo only**. `routeAttack` diverts to the legacy single-shot path whenever
`inMPGame()` (`15_combat.js:214-224`). MP therefore has:

* one attack per Resolve (no declaration list),
* attackers forced to share one row (`13_input.js:153`, `42_mp_apply.js:201`),
* host-authoritative validation of the guest's attack intent (`42_mp_apply.js:199-259`),
* the guest's blocker choice fetched by `askGuest('block', …)` and injected by overriding
  `aiChooseInterceptors` (`43_mp_intents.js:150-159`), re-validated by **object identity** against a
  freshly recomputed eligibility list,
* the guest's block deadline (20 s) deliberately shorter than the host's auto-pass (25 s)
  (`44_mp_lobby.js:43`, `41_mp_sync.js:96-103`),
* snapshot serialization of the whole duel state (`41_mp_sync.js:29-36`) — note that
  **`G.atk` and `G.decls` are NOT serialized**; they are local-only interaction state and are cleared
  on adopt (`41_mp_sync.js:51`).

**Implication for the C# design:** the rules core must expose combat as (a) a serializable
`CombatDeclarationSet` that *is* part of authoritative state, and (b) a resolver driven by explicit
choice requests. The current JS gets away with local-only declarations only because MP bypasses
Combat v3 entirely.

---

## 15. Worked examples

### 15.1 Example A — joint attack on the castle wall, gang-blocked, with retaliation and Undertow

**Board.** Foe life = 10000.

| Unit | Side | Location | Row idx | Stats |
|---|---|---|---|---|
| A1 Ashfang | you | `youFront` col 2 | 3 | ⚔1500 / ♥1000, **First Strike** |
| A2 Magmaw | you | `youFront` col 3 | 3 | ⚔3000 / ♥2500, cost 6 |
| B1 Mistling | foe | `foeFront` col 0 | 1 | ⚔500 / ♥1000, **tapped** (attacked last turn) |
| B2 Rippler | foe | `foeBack` col 4 | 0 | ⚔1000 / ♥1000 |
| B3 Undertow | foe | `center` col 1 | 2 | ⚔500 / ♥1500, kw **undertow** |

All foe workers already harvested (tapped) → no worker blockers.

**Step 1 — selection.** Player marquee-selects A1 and A2 (`G.atk = [{youFront,2},{youFront,3}]`).
`canAttack()` passes. Every foe object and the foe ♥ light up.

**Step 2 — declaration.** Player taps the enemy ♥ → `routeAttack('wall', 3)` → `CMB.declare('wall',null,null)`.

*Declaration 1 — A1:*
* `A1.tapped = true`; `d1 = {a:{youFront,2}, kind:'wall', blockers:[]}`.
* `aIdx = 3`, `tIdx = -1`. Not Scour, `3 ≠ -1` → crossed rows = `rowsCrossedInto(3,-1)`:
  `r = 2 (center) → 1 (foeFront) → 0 (foeBack) → -1 (clipped out) → stop`.
  **Crossed = [center, foeFront, foeBack].**
* Eligible interceptors, in enumeration order: **[B3 (center, ♥1500), B1 (foeFront, ♥1000, tapped —
  still eligible), B2 (foeBack, ♥1000)]**.
* `aiChooseInterceptors([A1], {kind:'base', cc:true, power:1500})`:
  * `1500 >= 10000`? no. `1500 >= 4`? **yes** → the AI defends.
  * survivors (`h > 1500`): none (1500 > 1500 is false).
  * fallback: sort ascending by h → `[B1(1000), B2(1000), B3(1500)]`, take **2** → **[B1, B2]**.
* `B1.blocked = B2.blocked = true`; `d1.blockers = [B1, B2]`. **A1 is gang-blocked.**

*Declaration 2 — A2:*
* `A2.tapped = true`; `d2 = {a:{youFront,3}, kind:'wall', blockers:[]}`.
* Same crossed rows, but B1 and B2 now carry `blocked = true` → **eligible = [B3]**.
* `aiChooseInterceptors([A2], {cc:true, power:3000})`: no survivor (1500 > 3000 false) → chump with the
  two weakest of one → **[B3]**. `B3.blocked = true`; `d2.blockers = [B3]`.

Hint: *"**2** attacks declared — … then ⚔ Resolve combat"*.

**Step 3 — Resolve.** Player presses ⚔ Resolve → 4 s "Opponent may respond…" pill → `_resolveNow`.

* `live` = both; `attackers = [A1, A2]`; no Overcharge.
* `blocked = [d1, d2]`, `open = []`.

*Pair fight 1 (d1: A1 vs B1+B2):*
1. `blks = [B1, B2]` → both `tapped = true`.
2. `applyUndertow([A1], [B1,B2])` — no warden among the blockers → nothing.
3. `blks.length > 1` → **askAbsorb** prompt. Player picks **B1** → `ab = 0`, `absorber = B1`.
4. **Tier(First Strike):** `A1.fs == true` → `hit(B1, effA(A1) = 1500)`. No blocker has FS.
   Apply → **B1.h = 1000 − 1500 = −500**.
5. **Tier(main):** `A1.fs == false`? no → A1 does not strike again.
   Blockers with `fs == false`: B1 (`h = −500`, skipped), B2 (`h = 1000 > 0`, `A1.h = 1000 > 0`) →
   `hit(A1, B2.a = 500)`. Apply → **A1.h = 1000 − 500 = 500**.
6. `cleanup()` — B1 dies (foeFront col 0 freed, no keyword, to foe grave).

**Result:** A1 survives at ♥500 but is *blocked* — it contributes **zero** wall damage. B2 survives
untouched (only the absorber took the blow). B1 is dead.

*Pair fight 2 (d2: A2 vs B3):*
1. `blks = [B3]` → `B3.tapped = true`. Single blocker → no prompt, `ab = 0`.
2. `applyUndertow([A2], [B3])` — B3 has `kw = 'undertow'` and `h > 0`.
   Marks = A2 (creature, alive, not worker/token/entrench). **A2 is removed from `youFront` col 3 and
   returned to the player's hand at full ♥2500.** `group` becomes empty.
3. `if (!group.length) { cleanup(); render(); return; }` → **the fight ends before any damage.**
   B3 takes nothing and deals nothing.

*Steps 6–8:* `open` is empty → `byT` empty → `wallDmg = 0`. **The foe's life pool is untouched.**

*Step 10:* `clearDischarge`, `G.busy = false`, `checkWin()` → life 10000/… → game continues.

**Net result of the turn's combat:** foe loses Mistling; player loses Magmaw from the board (recoverable
from hand, resummonable and summoning-sick); Ashfang is at ♥500 and tapped; **no life damage at all**.
This is the intended texture of Combat v3 — a defended wall costs bodies, not life.

### 15.2 Example B — same-row joint attack, uninterposable, universal retaliation, Overgrowth

**Board.** Player is raiding: two of the player's creatures already stand in `foeFront`.

| Unit | Side | Location | Row idx | Stats |
|---|---|---|---|---|
| A1 Ashfang | you | `foeFront` col 2 | 1 | ⚔1500 / ♥1000, **First Strike** |
| A2 Cinderling | you | `foeFront` col 4 | 1 | ⚔1000 / ♥1000 |
| T Surgeling | foe | `foeFront` col 6 | 1 | ⚔2000 / ♥1500 |
| Overgrowth (trap) | foe | `foeBack` col 1 | — | `trigger:'attack'`, `thornmail`, armed |

**Declaration.** Player selects A1 + A2, taps T.
For both declarations `aIdx == tIdx == 1` → `rowsCrossedInto(1,1) = []` → **no blockers may be
declared, by anyone, from anywhere**. This is the same-row uninterposable duel. `d1.blockers` and
`d2.blockers` stay empty.

**Resolve.**
* `blocked = []`, `open = [d1, d2]`.
* `byT` groups both under **T** → `grp = [A1, A2]` (declaration order).
* `springAttackTrap('foe', [A1,A2], T)` finds Overgrowth (front 0..6 empty of traps → back col 1 hit).
  `thornmail` → **T becomes ⚔2500 / ♥2500 (maxh 2500)**. Trap to grave, cell cleared.
* `targetFight([A1,A2], T, ri = 0, …)`:
  1. `applyUndertow([A1,A2], [T])` — T has no keyword → nothing.
  2. `back = grp[0] = A1` (the AI defender does **not** choose; the first-declared attacker eats the
     retaliation).
  3. **Tier(FS):** A1 (`fs`) → `hit(T, 1500)`. T is not FS. Apply → **T.h = 2500 − 1500 = 1000**.
  4. **Tier(main):** A2 (`!fs`, alive, `T.h = 1000 > 0`) → `hit(T, 1000)`.
     T (`!fs`, `T.h = 1000 > 0`, `back = A1.h = 1000 > 0`) → `hit(A1, T.a = 2500)`.
     Apply **both at once** → **T.h = 0**, **A1.h = 1000 − 2500 = −1500**.
  5. `cleanup()` sweeps `foeFront` slots ascending: col 2 → **A1 dies** (player's grave — attribution by
     `owner`, not by which array it sat in); col 6 → **T dies** (foe's grave).
* Step 7: both declarations have `o.kind == 'creature'` → skipped (already fought). `wallDmg = 0`.

**Net:** T and A1 trade; **A2 is untouched** — the target retaliated against exactly one attacker, with
no splitting. Overgrowth turned a clean 2-for-0 into a 1-for-1.

---

## 16. Suggested C# types

All of this must live in a UI-free, `UnityEngine`-free assembly.

```csharp
// ---------- geometry ----------
public enum RowKey { FoeBack = 0, FoeFront = 1, Center = 2, YouFront = 3, YouBack = 4 }

public readonly struct RowIndex {          // wraps an int so wall indices are first-class
    public const int FoeWall = -1;
    public const int YouWall = 5;          // == RowCount
    public const int RowCount = 5;
    public readonly int Value;
    public bool IsRealRow => Value >= 0 && Value < RowCount;
    public bool IsWall    => Value == FoeWall || Value == YouWall;
}

public readonly struct BoardRef { public readonly RowKey Row; public readonly int Slot; }  // Slot 0..6
public enum Side { You, Foe }
public enum WorkerZone { Back, Front, Center, Raid }

// ---------- units ----------
public enum UnitKind { Creature, Building, FaceDownCharge, SetTrap }
public enum Keyword { None, Detonate, Undertow, Entrench, Ward, Reap, Chrysalis, Scour, Overcharge }

public sealed class Unit {
    public int Id; public Side Owner; public UnitKind Kind; public string Name; public Element Element;
    public int Attack; public int Hp; public int MaxHp; public int Cost;
    public bool FirstStrike; public Keyword Keyword; public int KeywordValue;   // det / reap / wardHp
    public bool IsWorker, IsToken, IsEntrenched;
    public bool Sick, Tapped, HasBlockedThisCycle, Moved, MovedTwice, UpkeepPaid;
    public int OverchargeBank;          // oc
    public int DischargeBonus;          // _dis — transient, cleared by ClearDischarge
    public int EffectiveAttack => Attack + DischargeBonus;
}

// ---------- declarations ----------
public enum DeclarationKind { Unit, Wall, WorkerStack }

public readonly struct BlockerRef {                 // cell blocker OR pool blocker — never both
    public readonly BoardRef? Cell;                 // board creature
    public readonly (Side Owner, WorkerZone Zone, int Index)? Pool;   // worker
    public readonly Unit Unit;                      // identity is authoritative
}

public sealed class AttackDeclaration {
    public BoardRef Attacker;                       // NB: the JS stores coordinates, not identity — see risks
    public Unit AttackerUnit;                       // ADD THIS in the port
    public DeclarationKind Kind;
    public BoardRef Target;                         // valid when Kind == Unit
    public WorkerZone TargetZone;                   // valid when Kind == WorkerStack
    public readonly List<BlockerRef> Blockers = new();
}

public sealed class CombatState {                   // part of authoritative, serializable game state
    public readonly List<AttackDeclaration> Declarations = new();
    public bool HasDeclarations => Declarations.Count > 0;
}

// ---------- resolution ----------
public enum CombatResolveStage {
    Idle, AwaitingResponseWindow,
    BlockedPairFights, UnblockedCreatureGroups, UnblockedMisc,
    ApplyWallDamage, ScourStrikes, Complete
}

public readonly struct DamageEntry { public readonly Unit Target; public readonly int Amount; }

// deterministic, insertion-ordered accumulator — do NOT use a plain Dictionary for anything
// whose ITERATION ORDER is observable (see byT grouping and focusFire).
public sealed class DamageBatch {
    public void Hit(Unit u, int amount);
    public void ApplyAndClear();
    public IReadOnlyList<DamageEntry> Entries { get; }
}

// ---------- choices ----------
public interface ICombatChoiceProvider {                       // async; AI and human both implement
    Task<IReadOnlyList<BlockerRef>> ChooseBlockers(BlockContext ctx);
    Task<int> ChooseAbsorber(Unit attacker, IReadOnlyList<Unit> blockers);
    Task<int> ChooseRetaliationTarget(Unit defender, IReadOnlyList<Unit> attackers);
    Task<TrapRef> ChooseResponseTrap(ResponseWindowContext ctx);
}

public readonly struct BlockContext {
    public readonly Side AttackerSide; public readonly Unit Attacker;
    public readonly int AttackerRowIndex, TargetRowIndex;
    public readonly DeclarationKind Kind; public readonly Unit TargetUnit;   // may be null
    public readonly IReadOnlyList<BlockerRef> Eligible;
}

// ---------- static rules ----------
public static class CombatGeometry {
    public static IReadOnlyList<RowKey> RowsCrossedInto(int attackerRow, int targetRow);
    public static IReadOnlyList<BlockerRef> EligibleInterceptors(GameState g, Side attackerSide,
                                                                int attackerRow, int targetRow);
    public static bool CanAttack(Unit u);      // creature, own side, !worker, !sick, !tapped
    public static bool CanBlock(Unit u);       // creature, !HasBlockedThisCycle  (workers: also !tapped && !sick)
}

public sealed class CombatResolver {           // step-driven; no coroutine leakage into the model
    public CombatResolveStage Stage { get; }
    public Task ResolveAll(GameState g, CombatState c, ICombatChoiceProvider you,
                           ICombatChoiceProvider foe, ICombatLog log);
}

// legacy engine, still needed for worker stacks / provoked face-downs / any MP path
public static class LegacyCombat {
    public static DamageBatch FocusFire(IReadOnlyList<Unit> dealers, IReadOnlyList<Unit> targets);
    public static void ResolveCombat(IList<Unit> groupA, IList<Unit> groupB, GameState g);
}
```

---

## 17. Port risks and traps

| # | Risk | Why it bites | Suggested handling |
|---|---|---|---|
| 1 | **`List<T>.Sort` is not stable in C#** | `focusFire`, `aiChooseInterceptors`, `applyUndertow`, `aiPickTarget` and `onCreatureDeath` all sort on keys with frequent ties (equal HP, equal cost). JS `Array.prototype.sort` is stable since ES2019, so the JS outcome is fully determined; a C# `Sort` will silently pick a different unit and diverge. | Use `OrderBy`/`ThenBy` (stable), or add an explicit tiebreaker on unit id / enumeration index everywhere. |
| 2 | **Declarations store attacker *coordinates*, not identity** (`15_combat.js:245`, `15_combat.js:312`) | After declaring, the attacker's cell is re-read at resolve time. The Move button is still offered on a tapped-but-not-moved declared attacker (`16_movement.js:26-27`, `13_input.js:145`), so the player can move a declared attacker; the declaration then resolves against **whatever now sits in that cell** (nothing, or a different unit that moved in). | Store the `Unit` reference (and validate the coordinate as a cross-check). Or forbid moving a declared attacker. |
| 3 | **Overcharge discharge is off by ×500** | `_dis` is +1..+3 against attack values of 500..4500 (`06_mana_workers.js:156,160`). The mechanic is currently inert. | Decide: scale to `oc * 500`, or cut the keyword. Do not silently port. |
| 4 | **`P >= 4` in the wall-defence AI is a dead threshold** | Same scale bug (`15_combat.js:74`). The AI always defends the wall, which is probably the intended behaviour but by accident. | Re-express as a real policy (e.g. "block if the incoming damage exceeds X% of remaining life"). |
| 5 | **`untappedInterceptors` ignores `tapped` for board creatures but enforces it for workers** | Looks like a bug, is documented as intentional (`15_combat.js:14`). The two predicates are literally inverted between the two branches of the same function. | Port exactly, with a comment. Any "cleanup" here is a balance change. |
| 6 | **`ri = 0` hard-codes the AI's retaliation target** | `15_combat.js:337` — the defending AI always retaliates against the first-declared attacker, which the player can exploit by declaring a throwaway first. | Either keep (documented) or give the AI a real `ChooseRetaliationTarget`. The interface already exists for the player's side. |
| 7 | **The player's committed response trap can silently never fire** | On the AI's turn the player commits one trap in `RESP.defendWindow`; `springRef` is consumed only by the first creature-target group or the first struck building (`17_turns_ai.js:361,373`). If every AI attack is a wall strike or a face-down/trap poke, the trap is **never used and never returned** — it just stays armed but the window is spent. | Make trap commitment explicit and reversible, or fire it on the wall strike too. |
| 8 | **`springAttackTrap` fires once per target group, not once per combat** | `15_combat.js:335` re-runs `findArmedTrap` each group. Three separate target groups can spring three separate traps in one Resolve. | Confirm intent; a "one trap per combat" rule would be a change. |
| 9 | **"Simultaneous" is only within a tier of a single fight** | The header comment says "⚔ Resolve lands ALL damage" but resolution is a sequence of independent fights each followed by `cleanup()` (`15_combat.js:320-339`). A creature killed in the first pair fight cannot retaliate in the second. | Document loudly in-game or redesign to a true global simultaneity model. The latter is a real rules change with wide blast radius. |
| 10 | **Async/`await` inside the rules core** | `_resolveNow`, `pairFight`, `targetFight` and the AI turn are `async` and interleave FX awaits with player prompts. Porting this as `async Task` into a deterministic rules library couples the model to a scheduler. | Make the resolver a **step machine** that yields typed `ChoiceRequest` objects; the view layer answers them. This is also exactly what host-authoritative netcode will need. |
| 11 | **`Math.random()` in `aiPickTarget`** (`17_turns_ai.js:259,263`) | Non-deterministic; breaks replay, netcode and unit tests. | Seeded PRNG stored in game state. |
| 12 | **Map iteration order is observable** | `byT` (`15_combat.js:329-332`) determines the order target groups resolve; JS `Map` is insertion-ordered. `Dictionary<K,V>` in C# is not. | Ordered dictionary / list-of-pairs. |
| 13 | **Wall damage floors at 0 but never overflows** | `Math.max(0, life - dmg)` (`15_combat.js:358`) — excess damage is discarded, so there is no "overkill" statistic. | Fine, but note it if you ever add margin-of-victory scoring. |
| 14 | **`cc` guards are vacuous** | `!o.cc`, `info.cc`, `validSpellTarget`'s `if(o.cc) return false` — command-center cards were deleted (`04_cards_leaders.js:25`) but `mkCC` and the guards remain. | Delete `cc` from unit state, keep the *semantic* (`DeclarationKind.Wall`) which is what `info.cc` actually means today. |
| 15 | **Blockers are flagged but not tapped at declaration time** | `blocked = true` at declaration, `tapped = true` only inside `pairFight` (`15_combat.js:255`, `15_combat.js:268`). A blocker whose attacker dies before its pair fight (impossible today, possible with future effects) would end up flagged-but-untapped. | Make tapping part of the block commitment, or keep and document. |
| 16 | **Worker blocker refs carry no slot index** | `{key, c}` with no `i` (`15_combat.js:18`), so `decorate`'s `.declBlk` highlight silently never matches a worker blocker (`12_render.js:450`). | Model pool refs as a distinct case (already in the suggested `BlockerRef`). |
| 17 | **`resolveCombat` and `CMB.*` are two different damage models coexisting** | Worker-stack strikes and provoked face-downs use greedy `focusFire`; everything else uses the tiered single-target model. Their outcomes differ for the same board. | Decide whether to unify (a real rules change) or keep both and name them clearly. |
| 18 | **`G.busy` is toggled off mid-resolution to allow the absorber prompt** (`15_combat.js:324`) | For those milliseconds the board is nominally interactive. The RESP layer's `onCell` guard is what actually saves it, and only when RESP is loaded. | The step-machine design removes this class of bug entirely. |
| 19 | **`checkWin` runs only after the whole resolution** | Lethal wall damage from declaration #1 does not stop declarations #2..N. In the current code wall damage is applied last anyway, so it never manifests — but any reordering exposes it. | Check win after each damage application in the port. |
| 20 | **Attack values are ×500 but keyword/trap values are mixed-scale** | Thornmail `+500/+1000` and Backlash `1500` are correctly scaled; Detonate/Reap are scaled; Overcharge is not (risk 3). | Audit every numeric constant against the ×500 scale during the data export. |

---

## 18. Open questions for the designer

1. **Should "simultaneous damage" be global?** Today it is per-fight. A true simultaneous model
   (collect every damage packet across all declarations, then apply once, then sweep deaths once) is
   cleaner to explain and to net-sync — but changes outcomes wherever two fights interact.
2. **Should the AI choose its retaliation target?** (risk 6)
3. **Overcharge:** scale it, or cut it? (risk 3)
4. **Should a wall strike be able to trigger the defender's `trigger:'attack'` trap?** Today it cannot,
   which makes the player's committed response trap dead against a pure wall assault (risk 7).
5. **Should a blocked attacker be able to hit the wall/target with its "excess"?** Today no —
   being blocked cancels the strike entirely, even if the attacker kills every blocker.
6. **Should worker-stack strikes use the tiered model instead of `focusFire`?** (risk 17)
7. **Should Scour be per-attacker (v3) or per-group (legacy)?** The two paths disagree today.
8. **Is "one block per creature per opponent turn" the intended cap**, or should tapped creatures be
   excluded from blocking after all? The comment says the former is deliberate.
9. **Does a face-down that flips via provocation count as "summoning-sick" for retaliation?**
   Today it does not (it fights at full), which is a meaningful tempo rule worth confirming.
