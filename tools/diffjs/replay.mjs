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

import { readFileSync, readdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { bootGame } from './boot.mjs';
import { projectJs, canonical, firstDiff, projectionHash } from './project.mjs';

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
  for (const ply of trace.plies) {
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
      return { matched, total: trace.plies.length, stopped: ply.cmd.t, reason: "DIVERGED",
               ply: ply.i, projection: projectJs(win) };
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
    console.log('    JS board: ' + JSON.stringify(r.projection).slice(0, 500));
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
