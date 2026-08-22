// M12 tier 1: replay a recorded C# command trace against the living JS game, comparing the board
// after EVERY ply. Stops at the first divergence, or at the first command the adapter cannot yet
// perform, and reports how far parity held.
//
// The central difficulty is that the JS has no owner-generic action layer. `doHarvest`, `place`
// and `castSpell` are hardcoded to 'you'; the AI's equivalents are inlined inside foeTurn. A
// symmetric command trace cannot be replayed through either one, so the adapter re-expresses each
// action owner-generically out of the JS's OWN helpers (cellArr, minPool, payAny, syncWorkers,
// mkCre, onCreatureEnter...). That keeps the rules being executed the JS's rules, while making
// them addressable for both sides.
//
// Where the two JS paths genuinely differ, the PLAYER path is treated as canonical, because the
// port is symmetric and the player path is the fuller implementation (the AI's inline copies skip
// gates the player's honour). Those choices are marked CANON below.
//
// Usage:  node tools/diffjs/replay.mjs [golden.json ...] [--verbose]

import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { bootGame } from './boot.mjs';
import { projectJs, canonical, firstDiff, diffAll, projectionHash } from './project.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const GOLDEN = join(HERE, 'golden');
const VERBOSE = process.argv.includes('--verbose');

const ROW_KEY = {
  FoeBack: 'foeBack', FoeFront: 'foeFront', Center: 'center',
  YouFront: 'youFront', YouBack: 'youBack',
};
const SIDE = ['you', 'foe'];

function parseCell(v) {
  const [row, col] = String(v).split(':');
  return { key: ROW_KEY[row], col: +col, row };
}

/** Everything the adapter needs out of the JS scope, resolved once. */
function api(win) {
  const g = (name) => win.eval(name);
  return {
    G: g('G'),
    cellArr: g('cellArr'), rowArr: g('rowArr'), whichOf: g('whichOf'),
    minPool: g('minPool'), minYield: g('minYield'),
    payAny: g('payAny'), payCost: g('payCost'), canPay: g('canPay'), manaTotal: g('manaTotal'),
    totalDeficit: g('totalDeficit'), zoneDeficit: g('zoneDeficit'),
    syncWorkers: g('syncWorkers'), readyWorkers: g('readyWorkers'), cleanup: g('cleanup'),
    setPhase: g('setPhase'), mkCre: g('mkCre'), mkBld: g('mkBld'),
    onCreatureEnter: g('onCreatureEnter'), drawCard: g('drawCard'),
    endTurnDrain: g('endTurnDrain'), resolveSpell: g('resolveSpell'), spellRec: g('spellRec'),
    flip: g('flip'), toGrave: g('toGrave'), startTurn: g('startTurn'),
    chrysalisUpkeep: g('chrysalisUpkeep'), overchargeUpkeep: g('overchargeUpkeep'),
    ZONES: g('ZONES'), SLOTS: g('SLOTS'),
  };
}

/**
 * beginTurn: the JS startTurn, with its AI-only tail suppressed.
 *
 * CANON: startTurn's 'foe' branch also draws a card and runs aiFixDeficit, and never sets a phase
 * (spec 07 s3.2 - "the AI never enters the phase machine"). The port made both sides symmetric and
 * gave the AI the real machine, so the replay stubs those two out and sets Upkeep for both.
 */
function beginTurn(win, a, owner) {
  const saved = { draw: win.drawCard, fix: win.aiFixDeficit };
  try {
    win.drawCard = () => {};
    win.aiFixDeficit = () => {};
    a.startTurn(owner);
  } finally {
    win.drawCard = saved.draw;
    win.aiFixDeficit = saved.fix;
  }
  a.setPhase('upkeep');
}

/** doHarvest (17_turns_ai.js:147), owner-generic. */
function harvest(a, owner) {
  const owe = a.totalDeficit(owner);
  let sum = 0;
  for (const z of ['back', 'front', 'center']) {
    const pool = a.minPool(owner, z);
    const up = pool.filter((m) => !m.tapped && !m.sick).length;
    if (up <= 0) continue;
    const total = up * a.minYield(z);
    pool.forEach((m) => { if (!m.sick) m.tapped = true; });
    a.G.P[owner].mana = Math.min(99, a.G.P[owner].mana + total);
    sum += total;
  }
  if (owe > 0) {
    const pay = Math.min(owe, a.G.P[owner].mana);
    if (pay > 0) a.payAny(owner, pay);
    a.ZONES.forEach((z) => {
      const d = a.zoneDeficit(owner, z);
      if (d > 0) a.G.P[owner].upaid[z] = (a.G.P[owner].upaid[z] || 0) + d;
    });
  }
  a.setPhase('draw');
}

/** place() (13_input.js:178), owner-generic, for the four hand modes. */
function play(a, owner, cmd) {
  const card = a.G.P[owner].hand[cmd.hand];
  if (!card) throw new Error('no hand card at index ' + cmd.hand);
  const to = parseCell(cmd.to);
  const which = a.whichOf(to.key);
  const arr = a.cellArr(owner, which);
  const slot = to.col;

  if (cmd.mode === 'Cast') {
    // castSpell (14_spells_traps.js:26): resolve FIRST, pay only if it took
    const ok = a.resolveSpell(card, to.key, slot);
    if (!ok) throw new Error('spell did not resolve');
    a.payCost(owner, card);
    a.G.P[owner].hand.splice(cmd.hand, 1);
    a.G.P[owner].grave.push(a.spellRec(card));
    return;
  }

  const occ = arr[slot];
  if (occ) {                                   // the play-on-top line
    const fromBank = Math.min(occ.bank || 0, card.c);
    const need = card.c - fromBank;
    a.payAny(owner, need);
    const carry = Math.max(0, (occ.bank || 0) - card.c);
    a.toGrave(owner, occ);
    a.G.P[owner].hand.splice(cmd.hand, 1);
    const cr = a.mkCre(card, owner, false);
    cr.sick = true; cr.bank = carry; arr[slot] = cr;
    a.onCreatureEnter(cr, owner);
    a.syncWorkers(owner);
    return;
  }

  if (cmd.mode === 'Summon') {
    a.payCost(owner, card);
    a.G.P[owner].hand.splice(cmd.hand, 1);
    const cr = a.mkCre(card, owner, false);
    cr.sick = true;
    arr[slot] = cr;
    a.onCreatureEnter(cr, owner);
    a.syncWorkers(owner);
    // the summon-trap window is a separate ply in the trace, so remember what it would hit
    a.lastSummon = { owner, which, slot, cr };
    return;
  }

  if (cmd.mode === 'SetTrap') {
    a.payAny(owner, 1);
    a.G.P[owner].hand.splice(cmd.hand, 1);
    arr[slot] = {
      kind: 'trap', owner, w: which,
      card: { nm: card.nm, c: card.c, effect: card.effect, trigger: card.trigger,
              val: card.val, ic: card.ic, art: card.art, trap: true },
      setTurn: a.G.turnNo,
    };
    a.syncWorkers(owner);
    return;
  }

  if (cmd.mode === 'Set') {
    a.payAny(owner, 1);
    a.G.P[owner].hand.splice(cmd.hand, 1);
    const ctype = card.type;
    const cdata = ctype === 'building'
      ? { nm: card.nm, c: card.c, h: card.h, eff: card.eff, val: card.val, sup: card.sup, ic: card.ic, art: card.art }
      : { nm: card.nm, a: card.a, h: card.h, c: card.c, fs: card.fs, up: card.up, art: card.art,
          kw: card.kw, det: card.det, ward: card.ward, wardhp: card.wardhp, reap: card.reap,
          grow: card.grow, hatch: card.hatch, into: card.into, entrench: card.entrench,
          tribe: card.tribe, subtype: card.subtype };
    arr[slot] = { kind: 'charge', owner, w: which, ctype, card: cdata, inv: 1, setTurn: a.G.turnNo };
    a.syncWorkers(owner);
    return;
  }

  throw new Error('unsupported play mode ' + cmd.mode);
}

/** placeBuild (06_mana_workers.js:221) / aiBuild's tail, owner-generic. */
function build(win, a, owner, cmd) {
  const resolveStruct = win.eval('resolveStruct');
  const color = cmd.color === 'None' ? null : String(cmd.color).toLowerCase();
  const def = resolveStruct(cmd.def, color);
  if (!def) throw new Error('unknown structure ' + cmd.def);
  const to = parseCell(cmd.to);
  a.payAny(owner, def.c);
  a.cellArr(owner, a.whichOf(to.key))[to.col] = a.mkBld(def, owner);
  a.syncWorkers(owner);
}

/** upgradeStruct (07_structures.js:23), owner-generic. */
function upgrade(win, a, owner, cmd) {
  const upgradeTargets = win.eval('upgradeTargets');
  const at = parseCell(cmd.at);
  const o = a.rowArr(at.key)[at.col];
  if (!o || o.kind !== 'building') throw new Error('no structure at ' + cmd.at);
  const def = upgradeTargets(o).find((d) => d.bid === cmd.to);
  if (!def) throw new Error('no upgrade target ' + cmd.to);
  a.payAny(owner, def.c);
  win.eval('applyUpgrade')(o, def);
  a.syncWorkers(owner);
}

/** doMove (16_movement.js:46) - the two-move budget, second move taps. */
function move(a, owner, cmd) {
  const from = parseCell(cmd.from);
  const to = parseCell(cmd.to);
  const c = a.rowArr(from.key)[from.col];
  if (!c) throw new Error('nothing to move at ' + cmd.from);
  a.rowArr(from.key)[from.col] = null;
  if (c.moved) { c.moved2 = true; c.tapped = true; } else c.moved = true;
  a.rowArr(to.key)[to.col] = c;
  a.syncWorkers(owner);
}

/** upkeepPay (17_turns_ai.js:127) - capped at the zone's remaining deficit. */
function upkeepPay(win, a, owner, cmd) {
  const at = parseCell(cmd.at);
  const o = a.rowArr(at.key)[at.col];
  if (!o) throw new Error('nothing to pay for at ' + cmd.at);
  const z = win.eval('zoneForRow')(owner, at.key);
  const cost = Math.min(o.up || 0, a.zoneDeficit(owner, z));
  if (cost > 0) {
    a.payAny(owner, cost);
    a.G.P[owner].upaid[z] = (a.G.P[owner].upaid[z] || 0) + cost;
  }
  o.paid = true;
}

/** upkeepSac (17_turns_ai.js:138) - graves directly, so no death trigger fires. */
function upkeepSacrifice(a, owner, cmd) {
  const at = parseCell(cmd.at);
  const o = a.rowArr(at.key)[at.col];
  if (!o) throw new Error('nothing to sacrifice at ' + cmd.at);
  a.rowArr(at.key)[at.col] = null;
  a.toGrave(owner, o);
  a.syncWorkers(owner);
}

/**
 * CMB.declare's bookkeeping half (15_combat.js:235), owner-generic and WITHOUT its inline
 * blocker choice: the trace already records what the defender answered, and injecting those
 * answers keeps the harness testing rules rather than re-testing the blocking heuristic.
 */
function declare(win, a, owner, cmd) {
  const from = parseCell(cmd.from);
  const A = a.rowArr(from.key)[from.col];
  if (!A || A.kind !== 'creature') throw new Error('no attacker at ' + cmd.from);
  A.tapped = true;                                   // the attacker taps AT DECLARATION

  const t = String(cmd.target);
  const d = { a: { k: from.key, i: from.col }, blockers: [] };
  if (t.startsWith('wall:')) {
    d.kind = 'wall';
  } else if (t.startsWith('workers:')) {
    const [, , zone] = t.split(':');
    d.kind = 'workers';
    d.wWhich = zone.toLowerCase();
  } else {
    const at = parseCell(t.split('@')[1]);
    d.kind = 'unit';
    d.tk = at.key;
    d.ti = at.col;
  }
  a.G.decls.push(d);
}

/**
 * CMB._resolveNow (15_combat.js:309) made owner-generic - the C# CombatResolver is a port of THIS
 * function, so this is the right thing to replay against. The JS hardcodes 'foe' as the defender
 * throughout; every such site becomes `def` here. Absorber and retaliation answers come from the
 * trace instead of the modals the JS awaits.
 */
async function resolveCombat(win, a, atk, answers) {
  const def = atk === 'you' ? 'foe' : 'you';
  const G = a.G;
  const CMB = win.eval('CMB');
  const unitAt = win.eval('unitAt');
  const effA = win.eval('effA');
  const kwOf = win.eval('kwOf');
  const springAttackTrap = win.eval('springAttackTrap');
  const provokeFaceDown = win.eval('provokeFaceDown');
  const springTrap = win.eval('springTrap');
  const focusFire = win.eval('focusFire');
  const applyDmg = win.eval('applyDmg');
  const resolveCombatLegacy = win.eval('resolveCombat');
  const scourStrike = win.eval('scourStrike');

  const decls = G.decls;
  G.decls = [];
  const live = decls
    .map((d) => ({ ...d, A: a.rowArr(d.a.k)[d.a.i],
                   tgt: d.kind === 'unit' ? unitAt(d.tk, d.ti) : null }))
    .filter((x) => x.A && x.A.kind === 'creature' && x.A.h > 0);
  const attackers = live.map((x) => x.A);
  win.eval('dischargeOvercharge')(attackers);

  const blocked = live.filter((x) => x.blockers.some((r) => r.c && r.c.h > 0));
  const open = live.filter((x) => !blocked.includes(x));

  for (const x of blocked) {
    const blks = x.blockers.map((r) => r.c).filter((b) => b && b.h > 0);
    if (!blks.length) continue;
    const ab = blks.length > 1 ? answers.nextIndex() : 0;
    await CMB.pairFight(x.A, x.blockers.filter((r) => r.c && r.c.h > 0), ab, x.a);
    if (G.over) return;
  }

  const byT = new Map();
  for (const x of open) {
    if (x.kind === 'unit' && x.tgt && x.tgt.kind === 'creature' && x.A.h > 0) {
      if (!byT.has(x.tgt)) byT.set(x.tgt, []);
      byT.get(x.tgt).push(x);
    }
  }
  for (const [T, xs] of byT) {
    const grp = xs.map((x) => x.A).filter((A) => A.h > 0);
    if (!grp.length || T.h <= 0) continue;
    springAttackTrap(def, grp, T);
    const ri = grp.length > 1 ? answers.nextIndex() : 0;
    await CMB.targetFight(grp, T, ri, null, xs.map((x) => x.a));
    if (G.over) return;
  }

  let wallDmg = 0;
  const scourHits = [];
  for (const x of open) {
    if (x.A.h <= 0) continue;
    const scour = kwOf(x.A) === 'scour';
    if (x.kind === 'wall') {
      wallDmg += effA(x.A);
      if (scour) scourHits.push(x.A);
      continue;
    }
    if (x.kind === 'workers') {
      resolveCombatLegacy([x.A], a.minPool(def, x.wWhich).slice());
      if (scour && x.A.h > 0) scourHits.push(x.A);
      continue;
    }
    const o = x.tgt;
    if (!o) continue;
    if (o.kind === 'creature') {
      if (scour && x.A.h > 0) scourHits.push(x.A);
      continue;
    }
    if (o.kind === 'building') {
      springAttackTrap(def, [x.A], o);
      applyDmg(focusFire([x.A], [o]));
      a.cleanup();
    } else if (o.kind === 'charge') provokeFaceDown(def, x.tk, x.ti, [x.A]);
    else if (o.kind === 'trap') springTrap(def, x.tk, x.ti, [x.A]);
    if (scour && x.A.h > 0) scourHits.push(x.A);
  }

  if (wallDmg > 0) G.P[def].life = Math.max(0, G.P[def].life - wallDmg);
  scourHits.forEach((A) => { if (A.h > 0) scourStrike(A, def); });
  if (scourHits.length) a.cleanup();
  win.eval('clearDischarge')(attackers);
  a.cleanup();
  win.eval('checkWin')();
}

/**
 * Attach the recorded blocker answers to the declarations that were ASKED. C# parks a request
 * only when the declaration has at least one eligible interceptor, so the Nth answer belongs to
 * the Nth declaration that clears the same bar - which is why eligibility is recomputed here out
 * of the JS's own eligibleInterceptors rather than assumed positional.
 */
function assignBlockers(win, a, atk, answers) {
  if (!answers.length) return;
  const def = atk === 'you' ? 'foe' : 'you';
  const eligibleInterceptors = win.eval('eligibleInterceptors');
  const rowIdx = win.eval('rowIdx');
  const kwOf = win.eval('kwOf');
  const unitAt = win.eval('unitAt');

  let n = 0;
  for (const d of a.G.decls) {
    if (n >= answers.length) break;
    const A = a.rowArr(d.a.k)[d.a.i];
    if (!A || A.h <= 0) continue;

    const aIdx = rowIdx(d.a.k);
    // the wall's virtual row index is ASYMMETRIC in the JS: -1 above the foe's back row for a
    // player attack (CMB.declare), ROWS.length below yours for the foe's (foeTurn:321)
    const tIdx = d.kind === 'unit' ? rowIdx(d.tk) : (atk === 'you' ? -1 : 5);
    if (kwOf(A) === 'scour' || aIdx === tIdx) continue;   // never offered to blockers

    const tgt = d.kind === 'unit' ? unitAt(d.tk, d.ti) : null;
    const elig = eligibleInterceptors(atk, aIdx, tIdx).filter((r) => r.c !== tgt);
    if (!elig.length) continue;

    // the answer names blockers by CELL, which is the only identity the two engines share
    const cells = answers[n++].split('+').filter(Boolean);
    for (const spec of cells) {
      const want = parseCell(spec);
      const r = elig.find((x) => x.key === want.key && x.i === want.col);
      if (!r || !r.c) throw new Error('blocker at ' + spec + ' is not eligible in the JS');
      r.c.blocked = true;
      d.blockers.push(r);
    }
  }
}

/** The commands the adapter can perform today. Anything else stops the replay, loudly. */
function apply(win, a, cmd) {
  const owner = SIDE[cmd.a];
  switch (cmd.t) {
    case 'beginTurn': beginTurn(win, a, owner); return true;
    case 'harvest': harvest(a, owner); return true;
    case 'draw': a.drawCard(owner); a.setPhase('action'); return true;
    case 'endTurn': a.setPhase('end'); a.endTurnDrain(owner); return true;
    case 'play': play(a, owner, cmd); return true;
    case 'build': build(win, a, owner, cmd); return true;
    case 'upgrade': upgrade(win, a, owner, cmd); return true;
    case 'move': move(a, owner, cmd); return true;
    case 'upkeepPay': upkeepPay(win, a, owner, cmd); return true;
    case 'upkeepSacrifice': upkeepSacrifice(a, owner, cmd); return true;
    case 'pour': {
      const at = parseCell(cmd.at);
      const ch = a.rowArr(at.key)[at.col];
      if (!ch || ch.kind !== 'charge') throw new Error('no face-down at ' + cmd.at);
      a.payAny(owner, cmd.amount);
      ch.inv += cmd.amount;
      return true;
    }
    case 'flip': {
      const at = parseCell(cmd.at);
      a.flip(owner, at.key, at.col);
      a.syncWorkers(owner);
      return true;
    }
    case 'declare': declare(win, a, owner, cmd); return true;

    // A respond OUTSIDE a resolve group is the summon-trap window: the defender chose to spring
    // a set trap at the creature that just formed. foeTrapOnSummon (14_spells_traps.js:42) is
    // 'you'-summons-only, so this is its owner-generic form - and it ignores the trap's own
    // effect exactly as the JS does, destroying the newcomer whatever the card says.
    case 'respond': {
      const ans = String(cmd.answer || '');
      if (!ans.startsWith('trap:') || ans === 'trap:pass') return true;
      const s = a.lastSummon;
      if (!s) throw new Error('a summon-trap window with nothing summoned');

      const trapAt = parseCell(ans.slice(5));
      const t = a.rowArr(trapAt.key)[trapAt.col];
      if (!t || t.kind !== 'trap') throw new Error('no trap at ' + ans.slice(5));

      const arr = a.cellArr(s.owner, s.which);
      if (arr[s.slot] !== s.cr) return true;              // it already left - the JS holds too
      a.toGrave(s.owner, s.cr);
      arr[s.slot] = null;
      a.G.P[owner].grave.push(a.spellRec(t.card));
      a.rowArr(trapAt.key)[trapAt.col] = null;
      a.cleanup();
      // NO syncWorkers: foeTrapOnSummon ends at cleanup(), so the destroyed creature's upkeep
      // stays counted in the worker figure until the next resync - and the port reproduces it
      return true;
    }
    case 'sendMana': {
      const from = parseCell(cmd.from);
      const to = parseCell(cmd.to);
      const src = a.rowArr(from.key)[from.col];
      const dst = a.rowArr(to.key)[to.col];
      if (!src || !dst) throw new Error('sendMana endpoints missing');
      dst.bank = (dst.bank || 0) + (src.bank || 0);
      src.bank = 0;
      return true;
    }
    default: return false;
  }
}

async function replayTrace(file) {
  const trace = JSON.parse(readFileSync(file, 'utf8'));
  const label = file.replace(/\\/g, '/').split('/').pop();
  const { win, problems } = await bootGame();
  if (problems.length) console.log('  ! boot: ' + problems.slice(0, 2).join(' | '));

  const byKey = win.eval('CARD_BY_KEY');
  const deck = (keys) => keys.map((k) => {
    const e = byKey[k];
    if (!e) throw new Error('unknown deck key ' + k);
    return { type: e.type, color: e.color, ...e.tpl };
  });

  win.eval('startGame')(trace.you, trace.foe, deck(trace.youDeck), deck(trace.foeDeck));
  const a = api(win);

  let matched = 0;
  for (let pi = 0; pi < trace.plies.length; pi++) {
    const ply = trace.plies[pi];

    // Combat is the one place the two engines do not step in lockstep. C# parks a choice, takes
    // a RespondCommand, and hashes after each; the JS resolves the whole thing in one call. So
    // the resolve handler consumes the answers that FOLLOW it in the trace and the group is
    // compared as a unit - outcomes are still checked exactly, only the mid-resolution plies go
    // uncompared, because the JS has no state to compare there.
    if (ply.cmd.t === 'resolve') {
      const owner = SIDE[ply.cmd.a];
      let j = pi + 1;
      const idx = [];
      const blockerAnswers = [];
      while (j < trace.plies.length && trace.plies[j].cmd.t === 'respond') {
        const ans = String(trace.plies[j].cmd.answer || '');
        if (ans.startsWith('index:')) idx.push(+ans.slice(6));
        else if (ans.startsWith('blockers:')) blockerAnswers.push(ans.slice(9));
        j++;
      }
      assignBlockers(win, a, owner, blockerAnswers);

      let k = 0;
      const answers = { nextIndex: () => (k < idx.length ? idx[k++] : 0) };
      try {
        await resolveCombat(win, a, owner, answers);
      } catch (e) {
        console.log(`✗ ${label}: ply ${ply.i} (resolve) threw — ${e.message}`);
        return { matched, total: trace.plies.length, stopped: 'resolve', reason: 'threw' };
      }

      const last = trace.plies[j - 1];
      const expect = last && last.p ? last.p : ply.p;
      const mine = projectionHash(win);
      if (expect && mine !== expect) {
        return { matched, total: trace.plies.length, stopped: 'resolve', reason: 'DIVERGED',
                 ply: ply.i, comparedPly: (j - 1 >= 0 ? trace.plies[j - 1].i : ply.i),
                 projection: projectJs(win) };
      }
      matched += j - pi;
      pi = j - 1;
      continue;
    }

    let ok;
    try {
      ok = apply(win, a, ply.cmd);
    } catch (e) {
      console.log(`✗ ${label}: ply ${ply.i} (${ply.cmd.t}) threw — ${e.message}`);
      return { matched, total: trace.plies.length, stopped: ply.cmd.t, reason: 'threw' };
    }
    if (!ok) {
      return { matched, total: trace.plies.length, stopped: ply.cmd.t, reason: 'unsupported' };
    }
    // the real test: does the board AGREE after this ply, not merely "did it not throw"
    const mine = projectionHash(win);
    if (ply.p && mine !== ply.p) {
      return { matched, total: trace.plies.length, stopped: ply.cmd.t, reason: 'DIVERGED',
               ply: ply.i, comparedPly: ply.i, projection: projectJs(win) };
    }
    matched++;
    if (VERBOSE && matched % 25 === 0) console.log(`    … plies`);
  }

  return { matched, total: trace.plies.length, stopped: null, reason: 'complete' };
}

/** After the supported prefix, does the board still agree? */
async function checkPrefix(file) {
  const r = await replayTrace(file);
  const label = file.replace(/\\/g, '/').split('/').pop();
  const pct = Math.round((100 * r.matched) / r.total);
  if (r.reason === 'complete') console.log(`✓ ${label}: replayed all ${r.total} plies`);
  else if (r.reason === 'DIVERGED') {
    console.log('✗ ' + label + ': DIVERGED at ply ' + r.ply + ' (' + r.stopped
      + ') after ' + r.matched + ' matching plies');

    // If the C# projection dump is sitting next to the golden, name the exact field.
    const projFile = file.replace(/\.json$/, '.proj.jsonl');
    if (existsSync(projFile)) {
      const lines = readFileSync(projFile, 'utf8').split('\n');
      const csharp = JSON.parse(lines[r.comparedPly - 1]);
      const ds = diffAll(csharp, r.projection);
      console.log('    ' + ds.length + ' differing field(s):');
      for (const d of ds.slice(0, 8)) {
        console.log('      ' + d.path + '  C#=' + JSON.stringify(d.a) + '  JS=' + JSON.stringify(d.b));
      }
    } else {
      console.log('    (run the gate with SRD_TRACE_PROJ=1 to get a field-level diff)');
    }
  } else {
    console.log('· ' + label + ': ' + r.matched + '/' + r.total + ' plies (' + pct
      + '%) — stopped at ' + r.stopped + ' (' + r.reason + ')');
  }
  return r;
}

const args = process.argv.slice(2).filter((x) => !x.startsWith('--'));
const files = args.length
  ? args.map((x) => resolve(x))
  : readdirSync(GOLDEN).filter((f) => f.endsWith('.json')).map((f) => join(GOLDEN, f));

const results = [];
for (const f of files) results.push(await checkPrefix(f));

const need = new Set(results.map((r) => r.stopped).filter(Boolean));
if (need.size) console.log('\nnext commands the adapter needs: ' + [...need].join(', '));
