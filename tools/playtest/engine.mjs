// Headless PLAYTEST harness — drives whole matches through the REAL player-facing code paths.
//
// The differential harness (tools/diffjs) re-expresses the rules owner-generically so a C# trace
// can be replayed. This is the opposite errand: here we want the game exactly as a human meets it,
// so the player side calls doHarvest / doDraw / place / placeBuild / castSpell / doMove /
// CMB.declare / CMB.resolve / endTurn, and the opponent is the shipped `foeTurn` AI. The only
// things replaced are the four places the game STOPS AND ASKS A HUMAN (askBlock, askAbsorb /
// askRetaliate, playerTrapOnSummon, RESP.defendWindow) — those become policy callbacks, which is
// precisely what a "playstyle" is.
//
// Speed comes from three stubs that change no rule: setTimeout collapses to 0, render() is a
// no-op (it is pure view — see 12_render.js), and log() goes to an array instead of the DOM.

import { bootGame } from '../diffjs/boot.mjs';

/** deterministic PRNG (mulberry32) so any match can be replayed from its seed */
export function mulberry32(a) {
  return function () {
    a |= 0; a = (a + 0x6D2B79F5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const strip = (s) => String(s == null ? '' : s)
  .replace(/<[^>]*>/g, '').replace(/&nbsp;/g, ' ').replace(/&amp;/g, '&').trim();

/**
 * Boot one game realm and install the harness stubs. One realm can host many matches
 * (startGame resets the state that matters); callers re-boot every N matches anyway so a
 * leak between matches can never masquerade as a rules bug for long.
 */
export async function newRealm({ liveRender = false } = {}) {
  const { win, problems } = await bootGame();
  if (problems.length) throw new Error('boot problems: ' + problems.join(' | '));

  const G = win.eval('G');
  const api = {};
  for (const name of [
    'CCS', 'ELEMENTS', 'COLORS', 'MAJORS', 'POOLS', 'SPELL_NEUTRAL', 'STRUCT_DEFS', 'ROWS',
    'SLOTS', 'ZONES', 'CENTER_LANES', 'DECK_SIZE',
  ]) api[name] = win.eval(name);
  const fn = (name) => (...a) => win.eval(name)(...a);   // late-bound: FX/RESP re-wrap globals
  for (const name of [
    'startGame', 'startTurn', 'endTurn', 'doHarvest', 'doDraw', 'place', 'placeBuild', 'castSpell',
    'doMove', 'upgradeStruct', 'flip', 'cleanup', 'checkWin', 'rowArr', 'cellArr', 'unitAt',
    'whichOf', 'minPool', 'rowWorkers', 'totalWorkers', 'workerCap', 'zoneDeficit', 'totalDeficit',
    'deficitRows', 'orphanDeficit', 'upkeepOffender', 'upkeepPay', 'upkeepSac', 'zoneForRow',
    'canPay', 'manaTotal', 'canBuild', 'buildList', 'resolveStruct', 'upgradeTargets',
    'canUpgradeTo', 'ownBuildings', 'ownUnits', 'structuresOf', 'freeDeploySlot', 'adjCells',
    'adjacentK', 'slotExists', 'isLane', 'placeRowOK', 'centerSlotOK', 'validSpellTarget',
    'spellHasTarget', 'deckOf', 'rowIdx', 'eligibleInterceptors', 'effA', 'kwOf', 'syncWorkers',
    'creaturesInRow', 'minYield', 'canMoveCard', 'findArmedTrap', 'vaultCap', 'rowName',
    'bidLineage', 'hasBuild', 'prereqMet', 'moveChainOf',
  ]) api[name] = fn(name);
  // spellHasTarget() is 'you'-only in the game; the pilot needs the same question per card
  api.validSpellTargetAny = (card) => {
    for (const key of api.ROWS) {
      const arr = api.rowArr(key);
      for (let i = 0; i < api.SLOTS; i++) {
        const o = arr[i];
        if (o && o.owner === 'foe' && api.validSpellTarget(card, o)) return true;
      }
    }
    return false;
  };
  api.CMB = win.CMB;
  api.G = G;
  api.win = win;

  // ---- speed + capture stubs -------------------------------------------------------------
  const realSetTimeout = win.setTimeout.bind(win);
  win.setTimeout = (f, _ms) => realSetTimeout(f, 0);
  win.setInterval = () => 0;             // the RESP countdown ticker has nothing to paint
  win.clearInterval = () => {};

  const logs = [];
  win.log = (html) => { logs.push(strip(html)); };
  win.setHint = () => {};
  if (!liveRender) win.render = () => {};

  // Deterministic randomness for BOTH sides (deck shuffles, the AI's dice rolls).
  let rand = mulberry32(1);
  win.Math.random = () => rand();

  return {
    win, G, api, logs,
    setSeed(s) { rand = mulberry32(s >>> 0); },
    clearLogs() { logs.length = 0; },
  };
}

/** Let jsdom's (now zero-delay) timers and the pending promise chain run. */
const tick = () => new Promise((r) => setImmediate(r));

export async function pump(cond, { limit = 20000 } = {}) {
  for (let i = 0; i < limit; i++) {
    if (cond()) return true;
    await tick();
  }
  return false;
}

// ---- deck construction -------------------------------------------------------------------

/**
 * Build a 40-card deck for `colors`. `shape` biases the curve so a playstyle can actually be
 * played: an aggro pilot with a hand of 6-drops is not an aggro pilot.
 *   random  — what the game itself deals (uniform over the pool)
 *   aggro   — cheap creatures, few spells
 *   control — expensive creatures, more spells/traps
 *   midrange— an even curve
 */
export function makeDeck(realm, colors, shape, rnd) {
  const { POOLS, SPELL_NEUTRAL, DECK_SIZE } = realm.api;
  if (shape === 'random') return realm.api.deckOf(colors);

  const pick = (arr) => arr[Math.floor(rnd() * arr.length)];
  const spellN = shape === 'control' ? 14 : shape === 'aggro' ? 6 : 10;
  const creatureN = DECK_SIZE - spellN;
  const weight = (c) => {
    if (shape === 'aggro') return c.c <= 2 ? 5 : c.c === 3 ? 3 : c.c === 4 ? 1 : 0.25;
    if (shape === 'control') return c.c <= 2 ? 1 : c.c === 3 ? 2 : c.c <= 5 ? 3 : 3;
    return 2;                                   // midrange: flat
  };
  const pool = [];
  for (const col of colors) for (const t of POOLS[col]) {
    const w = Math.max(1, Math.round(weight(t) * 4));
    for (let i = 0; i < w; i++) pool.push({ type: 'creature', color: col, ...t });
  }
  const deck = [];
  const counts = new Map();
  let guard = 0;
  while (deck.length < creatureN && guard++ < 4000) {
    const c = pick(pool);
    const n = counts.get(c.nm) || 0;
    if (n >= 3) continue;                        // deck-builder legality: max 3 copies
    counts.set(c.nm, n + 1);
    deck.push(c);
  }
  const spells = shape === 'control'
    ? SPELL_NEUTRAL.slice()
    : SPELL_NEUTRAL.filter((s) => !s.trap || shape !== 'aggro');
  const scount = new Map();
  guard = 0;
  while (deck.length < DECK_SIZE && guard++ < 4000) {
    const s = pick(spells);
    const n = scount.get(s.nm) || 0;
    if (n >= 3) continue;
    scount.set(s.nm, n + 1);
    deck.push({ type: 'spell', color: null, ...s });
  }
  for (let i = deck.length - 1; i > 0; i--) {
    const j = Math.floor(rnd() * (i + 1));
    [deck[i], deck[j]] = [deck[j], deck[i]];
  }
  return deck;
}

// ---- board reading helpers the policies share ---------------------------------------------

export function boardView(realm) {
  const { G, api } = realm;
  const cells = [];
  for (const key of api.ROWS) {
    const arr = api.rowArr(key);
    for (let i = 0; i < api.SLOTS; i++) if (arr[i]) cells.push({ key, i, o: arr[i] });
  }
  return cells;
}

export const mine = (cells) => cells.filter((c) => c.o.owner === 'you');
export const theirs = (cells) => cells.filter((c) => c.o.owner === 'foe');
export const creatures = (cells) => cells.filter((c) => c.o.kind === 'creature' && !c.o.worker);
export const buildings = (cells) => cells.filter((c) => c.o.kind === 'building');

/** Every cell a new card of `card` may legally be dropped into, in a sensible order. */
export function deploySlots(realm, card) {
  const { api } = realm;
  const out = [];
  const isB = card.type === 'building';
  for (const which of ['front', 'back']) {
    const arr = api.cellArr('you', which);
    const order = which === 'front' ? [3, 2, 4, 1, 5, 0, 6] : [2, 4, 1, 5, 3, 0, 6];
    for (const i of order) if (!arr[i] && api.centerSlotOK(which, i, isB)) out.push({ which, i });
  }
  return out;
}

// ---- invariants ---------------------------------------------------------------------------

/**
 * Rules-level truths that must hold at every turn boundary. A violation here is a bug in the
 * game, not in the pilot, which is why they are checked from OUTSIDE any policy.
 */
export function checkInvariants(realm, phaseLabel, { strictWorkers = false } = {}) {
  const { G, api } = realm;
  const bad = [];
  const say = (code, msg) => bad.push({ code, msg: `${phaseLabel}: ${msg}` });

  for (const o of ['you', 'foe']) {
    const P = G.P[o];
    if (P.mana < 0) say('MANA_NEGATIVE', `${o} mana ${P.mana}`);
    if (P.mana > 99) say('MANA_OVERCAP', `${o} mana ${P.mana}`);
    if (P.life < 0) say('LIFE_NEGATIVE', `${o} life ${P.life}`);
    if (P.life > api.CCS[P.cc].hp) say('LIFE_OVERMAX', `${o} life ${P.life}`);
    if (P.deck.length < 0) say('DECK_NEGATIVE', `${o} deck ${P.deck.length}`);
  }

  const seen = new Map();
  for (const key of api.ROWS) {
    const arr = api.rowArr(key);
    for (let i = 0; i < api.SLOTS; i++) {
      const u = arr[i];
      if (!u) continue;
      if (seen.has(u)) say('UNIT_DUPLICATED', `${u.nm || u.kind} in ${seen.get(u)} and ${key}:${i}`);
      seen.set(u, `${key}:${i}`);
      if ((u.kind === 'creature' || u.kind === 'building') && u.h <= 0)
        say('DEAD_UNIT_ON_BOARD', `${u.nm} h=${u.h} at ${key}:${i}`);
      if (key === 'center') {
        if (u.kind === 'creature' && !api.isLane(i)) say('CENTER_CREATURE_OFF_LANE', `${u.nm} at center:${i}`);
        if (u.kind === 'building' && api.isLane(i)) say('CENTER_BUILDING_IN_LANE', `${u.nm} at center:${i}`);
      }
      if (u.kind === 'creature' && u.maxh != null && u.h > u.maxh && !u._hardened)
        say('HP_OVER_MAX', `${u.nm} h=${u.h} maxh=${u.maxh} at ${key}:${i}`);
    }
  }

  // Worker pools mirror the derived per-row figure — but only for the side whose turn it is:
  // syncWorkers runs in startTurn, so the other side's pool is legitimately stale until then
  // (a razed forge's workers keep standing, and keep screening, until their owner's next turn).
  for (const o of [G.turn]) {
    for (const w of ['back', 'front', 'center']) {
      const want = Math.max(0, api.rowWorkers(o, w));
      const have = api.minPool(o, w).length;
      // pool > derived is the dangerous direction: workers that no longer have support are still
      // standing, still screening the row and still harvesting. pool < derived is only lag —
      // the row has earned workers that settle at its owner's next turn start.
      if (have > want) say('WORKER_PHANTOM', `${o}.${w} pool=${have} derived=${want}`);
      else if (strictWorkers && have !== want) say('WORKER_POOL_DESYNC', `${o}.${w} pool=${have} derived=${want}`);
    }
  }

  if (G.P.you.life <= 0 || G.P.foe.life <= 0) {
    if (!G.over) say('WIN_NOT_DETECTED', `life you=${G.P.you.life} foe=${G.P.foe.life} but G.over=false`);
  }
  return bad;
}

/** Cards that came out of a 40-card deck, counted wherever they now live. */
export function cardConservation(realm, owner) {
  const { G, api } = realm;
  const P = G.P[owner];
  let n = P.deck.length + P.hand.length;
  // structures are raised from the commander menu, never drawn — their grave records are not deck
  // cards, and neither are dead workers or tokens
  n += P.grave.filter((r) => r.type !== 'villager' && r.type !== 'building' && !r.token).length;
  for (const key of api.ROWS) {
    const arr = api.rowArr(key);
    for (const u of arr) {
      if (!u || u.owner !== owner) continue;
      // structures are BUILT from the commander menu, never drawn, so they are not deck cards
      if (u.kind === 'creature' && !u.worker && !u.token) n++;
      else if (u.kind === 'charge' || u.kind === 'trap') n++;
    }
  }
  return n;
}
