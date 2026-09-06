// The PILOT: the seven playstyles the harness flies, plus the shared turn driver.
//
// Every action goes through the same function a tapping human would reach (place, placeBuild,
// castSpell, doMove, CMB.declare/resolve, endTurn), so anything these pilots trip over is
// something a player can trip over. What separates a playstyle is only WHICH of those it reaches
// for and in what order — the economy plan, the target priority, and the four answers the game
// asks a defender (block / absorb / retaliate / spring).

import { boardView, mine, theirs, creatures, buildings, deploySlots, pump } from './engine.mjs';

const ROW_OF = { front: 'youFront', back: 'youBack', center: 'center' };

// ---- small readers ------------------------------------------------------------------------

const myCreatures = (r) => creatures(mine(boardView(r)));
const foeCreatures = (r) => creatures(theirs(boardView(r)));
const foeBuildings = (r) => buildings(theirs(boardView(r)));
const myBuildings = (r) => buildings(mine(boardView(r)));
const readyAttackers = (r) => myCreatures(r).filter((c) => !c.o.sick && !c.o.tapped);

const handOf = (r) => r.G.P.you.hand.map((c, i) => ({ c, i }));
const mana = (r) => r.api.manaTotal('you');

/** how far a row sits from the enemy wall — 0 = their back row */
function advanceRank(key) {
  return { foeBack: 0, foeFront: 1, center: 2, youFront: 3, youBack: 4 }[key] ?? 9;
}

// ---- economy ------------------------------------------------------------------------------

/**
 * Where a competent player puts each structure: the Foundry/Keep chain and the forges behind the
 * line, the camp/longhouse chain (and the upgrade to Barracks it gates) on the front where the
 * army stands and needs its support, the tower wherever there are workers to crew it.
 */
const PREF_ROW = {
  foundry: 'back', forge: 'back', grandforge: 'back', vault: 'back', reliquary: 'back',
  encampment: 'front', longhouse: 'front', outpost: 'front', bulwark: 'front', tower: 'back',
};

/** Raise the first affordable structure from `wants` (bids, in priority order). */
function tryBuild(r, wants, telem, { maxStructures = 99 } = {}) {
  const { api, G } = r;
  if (api.ownBuildings('you').length >= maxStructures) return false;
  const list = api.buildList(G.P.you.cc);
  for (const bid of wants) {
    for (const def of list) {
      if (def.bid !== bid) continue;
      if (!api.canBuild('you', def)) continue;
      // an upgraded tier still counts toward its base's cap, exactly as the AI counts it
      const cap = bid === 'tower' ? 2 : 1;
      const owned = api.ownBuildings('you').filter((b) => api.bidLineage(b).includes(bid)
        && (bid !== 'forge' && bid !== 'grandforge' ? true : b.color === def.color));
      if (owned.length >= cap) continue;

      const rows = ['back', 'front', 'center'].filter((w) => {
        const arr = api.cellArr('you', w);
        return arr && arr.some((x, i) => !x && api.centerSlotOK(w, i, true)) && api.placeRowOK('you', w, def);
      });
      if (!rows.length) continue;
      const pref = PREF_ROW[bid] || 'back';
      // support goes where the workforce is short — that is what lets an army stand forward
      const short = rows.filter((w) => api.rowWorkers('you', w) < 0)
        .sort((a, b) => api.rowWorkers('you', a) - api.rowWorkers('you', b))[0];
      const which = (def.sup || 0) > 0 && short ? short : (rows.includes(pref) ? pref : rows[0]);

      const arr = api.cellArr('you', which);
      let slot = -1;
      const order = which === 'center' ? [2, 4, 0, 6] : [2, 4, 1, 5, 3, 0, 6];
      for (const i of order) if (!arr[i] && api.centerSlotOK(which, i, true)) { slot = i; break; }
      if (slot < 0) continue;
      G.build = def;
      api.placeBuild(which, slot);
      G.build = null;
      if (!arr[slot]) continue;                       // refused — try the next want
      telem.builds.push({ turn: G.turnNo, bid: def.bid, nm: def.nm, cost: def.c, row: which });
      return true;
    }
  }
  return false;
}

/** Level one structure up in place, cheapest first. */
function tryUpgrade(r, telem) {
  const { api, G } = r;
  for (const b of myBuildings(r)) {
    const targets = api.upgradeTargets(b.o).filter((d) => api.canUpgradeTo('you', b.o, b.key, d));
    if (!targets.length) continue;
    const def = targets.sort((x, y) => x.c - y.c)[0];
    const before = b.o.nm;
    api.upgradeStruct(b.key, b.i, def.bid);
    if (b.o.nm !== before) {
      telem.upgrades.push({ turn: G.turnNo, from: before, to: b.o.nm, cost: def.c });
      return true;
    }
  }
  return false;
}

/** Summon creatures from hand. `order` decides the shape of the board you end up with. */
function trySummon(r, { order = 'biggest', max = 99 } = {}, telem) {
  const { api, G } = r;
  let n = 0;
  for (let guard = 0; guard < 12 && n < max; guard++) {
    const cands = handOf(r)
      .filter((x) => x.c.type === 'creature' && api.canPay('you', x.c))
      .sort((a, b) => (order === 'cheapest' ? a.c.c - b.c.c : b.c.c - a.c.c));
    if (!cands.length) break;
    const { c, i } = cands[0];
    const slots = deploySlots(r, c);
    if (!slots.length) break;
    // Upkeep discipline: stand it where the workforce can carry it, as far forward as that allows
    // (deploySlots already lists the front row first). A row that cannot carry it costs mana or a
    // body at the next upkeep, so it is the last resort, not the default.
    const spot = slots.find((s) => api.rowWorkers('you', s.which) - (c.up || 0) >= 0)
      || slots.sort((a, b) => api.rowWorkers('you', b.which) - api.rowWorkers('you', a.which))[0];
    const arr = api.cellArr('you', spot.which);
    const before = G.P.you.hand.length;
    api.place(i, 'summon', spot.which, spot.i);
    if (G.P.you.hand.length >= before) break;         // refused — stop, do not spin
    telem.summons.push({ turn: G.turnNo, nm: c.nm, cost: c.c, a: c.a, h: c.h, row: spot.which });
    n++;
  }
  return n;
}

/** Set a card face-down (banks ◆1 toward its cost, or arms a trap). */
function trySet(r, kind, telem) {
  const { api, G } = r;
  if (mana(r) < 1) return false;
  const want = handOf(r).find((x) => (kind === 'trap'
    ? (x.c.type === 'spell' && x.c.trap)
    : (x.c.type === 'creature' && x.c.c >= 3)));
  if (!want) return false;
  const slots = deploySlots(r, want.c);
  if (!slots.length) return false;
  const before = G.P.you.hand.length;
  api.place(want.i, kind === 'trap' ? 'settrap' : 'set', slots[0].which, slots[0].i);
  if (G.P.you.hand.length >= before) return false;
  telem.sets.push({ turn: G.turnNo, nm: want.c.nm, kind });
  return true;
}

/** Pour mana into a face-down card and flip it when it is funded. */
function tryFlip(r, telem) {
  const { api, G } = r;
  for (const w of ['back', 'front', 'center']) {
    const arr = api.cellArr('you', w);
    if (!arr) continue;
    for (let i = 0; i < api.SLOTS; i++) {
      const ch = arr[i];
      if (!ch || ch.kind !== 'charge' || ch.owner !== 'you') continue;
      const need = ch.card.c - ch.inv;
      if (need > 0) {
        // pour a little PAST the cost when mana is spare: the surplus banks onto the flipped card,
        // which is what makes play-on-top and ◆ Send reachable at all
        const spare = mana(r) > need + 4 ? 3 : 0;
        const pour = Math.min(need + spare, mana(r));
        if (pour <= 0) continue;
        r.win.eval('payAny')('you', pour);
        ch.inv += pour;
      }
      if (ch.inv >= ch.card.c) {
        api.flip('you', w === 'center' ? 'center' : ROW_OF[w], i);
        api.syncWorkers('you');
        telem.flips.push({ turn: G.turnNo, nm: ch.card.nm });
        return true;
      }
    }
  }
  return false;
}

/**
 * Play a card ON TOP of one of your own cards that is holding banked ◆ (13_input.js place(), the
 * `occ` branch): the bank pays the newcomer's cost, the card underneath is destroyed, surplus
 * carries. Nothing else in the pilot reaches this branch, and it is one of the game's own tricks.
 */
function tryPlayOnTop(r, telem) {
  const { api, G } = r;
  const holder = mine(boardView(r)).filter((c) => (c.o.bank || 0) > 0
    && (c.key === 'youBack' || c.key === 'youFront'))[0];
  if (!holder) return false;
  const want = handOf(r).filter((x) => x.c.type === 'creature'
    && x.c.c - Math.min(holder.o.bank, x.c.c) <= mana(r))
    .sort((a, b) => b.c.c - a.c.c)[0];
  if (!want) return false;
  const which = api.whichOf(holder.key);
  const before = G.P.you.hand.length;
  api.place(want.i, 'summon', which, holder.i);
  if (G.P.you.hand.length >= before) return false;
  telem.onTop = (telem.onTop || 0) + 1;
  return true;
}

/** Move a card's banked ◆ onto another of your cards (14_spells_traps.js startSendMana/doSendMana). */
function trySendMana(r, telem) {
  const { win, api } = r;
  const cells = mine(boardView(r));
  const src = cells.find((c) => (c.o.bank || 0) > 0);
  const dst = cells.find((c) => c !== src && (c.o.kind === 'creature' || c.o.kind === 'building'));
  if (!src || !dst) return false;
  r.G.moveMana = { k: src.key, i: src.i };
  win.eval('doSendMana')(dst.key, dst.i);
  r.G.moveMana = null;
  telem.sendMana = (telem.sendMana || 0) + 1;
  return true;
}

/** Cast an offensive spell if it has a target worth spending on. */
function tryCast(r, telem) {
  const { api, G } = r;
  const cands = handOf(r).filter((x) => x.c.type === 'spell' && !x.c.trap && api.canPay('you', x.c));
  for (const { c, i } of cands) {
    let best = null;
    for (const t of theirs(boardView(r))) {
      if (!api.validSpellTarget(c, t.o)) continue;
      const score = t.o.kind === 'creature' ? (t.o.a || 0) + (c.effect === 'burn' && c.val >= t.o.h ? 5000 : 0)
        : t.o.kind === 'building' ? 2500 : 500;
      if (!best || score > best.score) best = { ...t, score };
    }
    if (!best) continue;
    const before = G.P.you.hand.length;
    api.castSpell(i, best.key, best.i);
    if (G.P.you.hand.length < before) {
      telem.spells.push({ turn: G.turnNo, nm: c.nm, effect: c.effect, on: best.o.nm || best.o.kind });
      return true;
    }
  }
  return false;
}

/** March creatures one square toward the enemy wall (fewer crossed rows = fewer interceptors). */
function tryAdvance(r, telem, { keepHome = 0 } = {}) {
  const { api, G } = r;
  const movers = myCreatures(r)
    .filter((c) => !c.o.moved && !c.o.tapped && !c.o.sick)
    .sort((a, b) => advanceRank(a.key) - advanceRank(b.key));
  let moved = 0;
  for (const m of movers) {
    if (myCreatures(r).filter((c) => advanceRank(c.key) >= 3).length <= keepHome
        && advanceRank(m.key) >= 3) continue;
    const here = advanceRank(m.key);
    const options = api.adjCells('you', m.key, m.i)
      .filter(([k, j]) => !api.rowArr(k)[j] && advanceRank(k) < here)
      .sort((a, b) => advanceRank(a[0]) - advanceRank(b[0]));
    if (!options.length) continue;
    const [k, j] = options[0];
    G.moveFrom = { k: m.key, i: m.i };
    api.doMove(k, j);
    G.moveFrom = null;
    if (api.rowArr(k)[j] === m.o) { moved++; telem.moves.push({ turn: G.turnNo, nm: m.o.nm, to: k }); }
  }
  return moved;
}

// ---- attacking ----------------------------------------------------------------------------

/**
 * Turn a list of ready attackers into declarations. `plan` is the playstyle's target priority.
 * Declaring taps the attacker and the AI answers with its blockers immediately (CMB.declare),
 * exactly as it does for a human.
 */
function declare(r, refs, target) {
  const { G, api } = r;
  G.atk = refs.map((x) => ({ k: x.key, i: x.i }));
  if (target.kind === 'wall') api.CMB.declare('wall', null, null);
  else if (target.kind === 'workers') api.CMB.declare('workers', target.key, null, target.wWhich);
  else api.CMB.declare('unit', target.key, target.i);
  G.atk = [];
}

/** The enemy worker stacks, as attack targets: killing one is -1 mana a turn, forever. */
function workerTargets(r) {
  const { api } = r;
  return [['foeBack', 'back'], ['foeFront', 'front'], ['center', 'center']]
    .filter(([, w]) => api.minPool('foe', w).length)
    .map(([key, w]) => ({ kind: 'workers', key, wWhich: w, o: { nm: 'workers:' + w } }));
}

/**
 * A playstyle's target priority. Every plan CONVERTS: when the class of thing it prefers is not on
 * the board, it swings at the wall, because the wall is the only target that ends a game. A pilot
 * that idles instead measures its own stubbornness rather than the strategy.
 */
function pickTarget(r, plan, attacker) {
  const cres = foeCreatures(r);
  const blds = foeBuildings(r);
  const facedown = theirs(boardView(r)).filter((t) => t.o.kind === 'charge' || t.o.kind === 'trap');
  const power = r.api.effA(attacker.o);
  const killable = cres.filter((t) => power >= t.o.h).sort((a, b) => b.o.a - a.o.a)[0];
  const WALL = { kind: 'wall', fallback: true };

  switch (plan) {
    case 'wall':
      return { kind: 'wall' };
    case 'workers': {
      const w = workerTargets(r);
      return w.length ? w[0] : WALL;
    }
    case 'creature': {
      const t = killable || cres.sort((a, b) => b.o.a - a.o.a)[0];
      return t ? { kind: 'unit', ...t } : WALL;          // nothing left to hunt — take the wall
    }
    case 'building':
      if (blds.length) return { kind: 'unit', ...blds.sort((a, b) => a.o.h - b.o.h)[0] };
      if (facedown.length) return { kind: 'unit', ...facedown[0] };   // a set card is a structure in waiting
      return WALL;                                        // nothing left to raze — convert
    case 'value':
    default: {
      if (killable) return { kind: 'unit', ...killable };
      // a fat blocker left alive will just eat the next swing; otherwise race the wall
      const wall = { kind: 'wall' };
      const threat = cres.filter((t) => t.o.a >= 2000)[0];
      if (threat && power >= threat.o.h) return { kind: 'unit', ...threat };
      return wall;
    }
  }
}

/** Declare + resolve one combat step. Returns what was declared, for telemetry. */
async function attack(r, plan, telem, { joint = false } = {}) {
  const { G, api } = r;
  const ready = readyAttackers(r);
  if (!ready.length) return 0;

  const declared = [];
  if (joint) {
    // one target, the whole army — the joint-attack / one-retaliation path
    const target = pickTarget(r, plan, ready[0]);
    if (!target) return 0;
    declare(r, ready, target);
    declared.push({ n: ready.length, target: target.kind === 'wall' ? 'wall' : (target.o.nm || target.o.kind) });
    telem[target.fallback ? 'swingsConverted' : 'swingsOnPlan'] = (telem[target.fallback ? 'swingsConverted' : 'swingsOnPlan'] || 0) + 1;
  } else {
    for (const a of ready) {
      if (a.o.tapped || a.o.h <= 0) continue;
      const target = pickTarget(r, plan, a);
      if (!target) continue;
      declare(r, [a], target);
      declared.push({ n: 1, target: target.kind === 'wall' ? 'wall' : (target.o.nm || target.o.kind) });
      telem[target.fallback ? 'swingsConverted' : 'swingsOnPlan'] = (telem[target.fallback ? 'swingsConverted' : 'swingsOnPlan'] || 0) + 1;
    }
  }
  if (!api.CMB.hasDecls()) return 0;
  const lifeBefore = G.P.foe.life;
  const before = {
    mine: myCreatures(r).length, theirs: foeCreatures(r).length,
    theirBuildings: foeBuildings(r).length,
  };
  api.CMB.resolve();
  const ok = await pump(() => !r.combatBusy && !G.busy && !api.CMB.hasDecls());
  telem.attacks.push({
    turn: G.turnNo, plan, joint, declared,
    wallDamage: lifeBefore - G.P.foe.life,
    // what the swing actually cost and bought — the only honest measure of a target plan
    lostMine: Math.max(0, before.mine - myCreatures(r).length),
    killedTheirs: Math.max(0, before.theirs - foeCreatures(r).length),
    razedTheirs: Math.max(0, before.theirBuildings - foeBuildings(r).length),
    stalled: !ok,
  });
  if (!ok) telem.problems.push({ code: 'RESOLVE_STALLED', msg: `combat did not settle on turn ${G.turnNo}` });
  return declared.length;
}

// ---- upkeep -------------------------------------------------------------------------------

/** Settle every over-extended creature, then harvest. Preference order is a playstyle trait. */
function settleUpkeep(r, persona, telem) {
  const { api, G } = r;
  let guard = 0;
  // what the purse actually holds when the bill lands — the drain has already run
  telem.manaAtUpkeep = telem.manaAtUpkeep || [];
  telem.manaAtUpkeep.push({ turn: G.turnNo, mana: mana(r), owed: api.totalDeficit('you') });
  while (api.totalDeficit('you') - api.orphanDeficit('you') > 0 && guard++ < 40) {
    const off = api.upkeepOffender();
    if (!off) break;
    const zone = api.zoneForRow('you', off.key);
    const cost = Math.min(off.o.up || 0, api.zoneDeficit('you', zone));
    let settled = false;
    for (const how of persona.upkeep) {
      if (how === 'pay' && cost > 0 && mana(r) >= cost) {
        api.upkeepPay(off.key, off.i);
        settled = off.o.paid === true;
        if (settled) { telem.upkeepPaid = (telem.upkeepPaid || 0) + cost; telem.payActions = (telem.payActions || 0) + 1; }
      } else if (how === 'move') {
        const options = api.adjCells('you', off.key, off.i)
          .filter(([k, j]) => !api.rowArr(k)[j])
          .map(([k, j]) => ({ k, j, z: api.zoneForRow('you', k) }))
          .filter((x) => x.z && x.z !== 'raid' && api.rowWorkers('you', x.z) - (off.o.up || 0) >= 0);
        if (options.length) {
          G.moveFrom = { k: off.key, i: off.i };
          api.doMove(options[0].k, options[0].j);
          G.moveFrom = null;
          settled = api.rowArr(options[0].k)[options[0].j] === off.o;
          if (settled) telem.upkeepMoved = (telem.upkeepMoved || 0) + 1;
        }
      } else if (how === 'sac') {
        api.upkeepSac(off.key, off.i);
        settled = api.rowArr(off.key)[off.i] !== off.o;
        if (settled) telem.sacrifices.push({ turn: G.turnNo, nm: off.o.nm });
      }
      if (settled) break;
    }
    if (!settled) {
      api.upkeepSac(off.key, off.i);                  // last resort: the rules always allow this
      if (api.rowArr(off.key)[off.i] === off.o) {
        telem.problems.push({ code: 'UPKEEP_UNSETTLEABLE', msg: `turn ${G.turnNo}: ${off.o.nm} at ${off.key}:${off.i}` });
        break;
      }
    }
  }
  G.cardMenu = null;
  G.moveFrom = null;
}

// ---- the playstyles -----------------------------------------------------------------------

const BUILD_RUSH = ['foundry', 'encampment', 'forge', 'longhouse', 'vault', 'outpost', 'bulwark', 'reliquary', 'tower'];
const BUILD_LEAN = ['foundry', 'encampment', 'forge'];          // just enough ⚒ to field an army
const BUILD_MID = ['foundry', 'encampment', 'forge', 'longhouse', 'outpost'];

export const PERSONAS = {
  /** few structures, creatures early, storm the wall */
  aggro: {
    name: 'aggro',
    upkeep: ['move', 'pay', 'sac'],
    blocking: 'never',
    async action(r, telem) {
      tryBuild(r, BUILD_LEAN, telem, { maxStructures: 3 });     // few structures, by design
      trySummon(r, { order: 'cheapest' }, telem);
      tryCast(r, telem);
      tryAdvance(r, telem, { keepHome: 0 });
      await attack(r, 'wall', telem);
      trySummon(r, { order: 'cheapest' }, telem);      // spend the rest after combat
    },
  },

  /** structures first: out-economy the AI, then close */
  turtle: {
    name: 'turtle',
    upkeep: ['pay', 'move', 'sac'],
    blocking: 'always',
    async action(r, telem) {
      tryBuild(r, BUILD_RUSH, telem);
      tryUpgrade(r, telem);
      tryBuild(r, BUILD_RUSH, telem);
      trySet(r, 'trap', telem);
      trySet(r, 'card', telem);                       // bank ◆1 on a creature, flip it later
      tryCast(r, telem);
      trySummon(r, { order: 'biggest', max: r.G.turnNo < 8 ? 1 : 99 }, telem);
      tryFlip(r, telem);
      tryPlayOnTop(r, telem);
      trySendMana(r, telem);
      // hold the line early, then convert: a turtle that never swings is not a playstyle, it is a
      // stalemate machine — from turn 14 it takes any favourable trade it is offered
      const army = readyAttackers(r);
      if (army.length && (r.G.turnNo >= 14 || army.length > foeCreatures(r).length)) {
        await attack(r, 'value', telem);
      }
    },
  },

  /** economy and army in step — the default a new player converges on */
  balanced: {
    name: 'balanced',
    upkeep: ['pay', 'move', 'sac'],
    blocking: 'smart',
    async action(r, telem) {
      tryBuild(r, BUILD_MID, telem);
      tryUpgrade(r, telem);
      tryCast(r, telem);
      trySummon(r, { order: 'biggest' }, telem);
      tryFlip(r, telem);
      tryAdvance(r, telem, { keepHome: 1 });
      await attack(r, 'value', telem);
    },
  },

  /** everything swings at one target, every turn — the joint-attack path */
  multiattack: {
    name: 'multiattack',
    upkeep: ['pay', 'move', 'sac'],
    blocking: 'smart',
    async action(r, telem) {
      tryBuild(r, BUILD_MID, telem);
      tryUpgrade(r, telem);
      trySummon(r, { order: 'biggest' }, telem);
      tryAdvance(r, telem, { keepHome: 1 });
      await attack(r, 'value', telem, { joint: true });
    },
  },

  /** raze their base: structures are the only thing worth hitting */
  sapper: {
    name: 'sapper',
    upkeep: ['pay', 'move', 'sac'],
    blocking: 'smart',
    async action(r, telem) {
      tryBuild(r, BUILD_MID, telem);
      tryCast(r, telem);                              // raze spells go on structures too
      trySummon(r, { order: 'biggest' }, telem);
      tryAdvance(r, telem, { keepHome: 1 });
      await attack(r, 'building', telem);
    },
  },

  /** never touch a unit — the wall is the only target that ends a game */
  wallrush: {
    name: 'wallrush',
    upkeep: ['move', 'pay', 'sac'],
    blocking: 'never',
    async action(r, telem) {
      tryBuild(r, BUILD_LEAN, telem, { maxStructures: 3 });
      trySummon(r, { order: 'biggest' }, telem);
      tryAdvance(r, telem, { keepHome: 0 });
      await attack(r, 'wall', telem);
    },
  },

  /** starve them out: kill the workforce that pays for everything, then take the wall */
  raider: {
    name: 'raider',
    upkeep: ['pay', 'move', 'sac'],
    blocking: 'smart',
    async action(r, telem) {
      tryBuild(r, BUILD_MID, telem);
      trySummon(r, { order: 'biggest' }, telem);
      tryAdvance(r, telem, { keepHome: 1 });
      await attack(r, 'workers', telem);
    },
  },

  /** kill every creature they play and win on attrition */
  hunter: {
    name: 'hunter',
    upkeep: ['pay', 'move', 'sac'],
    blocking: 'always',
    async action(r, telem) {
      tryBuild(r, BUILD_MID, telem);
      tryUpgrade(r, telem);
      tryCast(r, telem);
      trySummon(r, { order: 'biggest' }, telem);
      await attack(r, 'creature', telem);
    },
  },
};

// ---- the four answers the game asks a defender --------------------------------------------

export function installPolicies(r, persona, telem) {
  const { win, api, G } = r;

  // CMB._resolveNow drops G.busy while it awaits the absorber choice, so "not busy" is not the
  // same as "combat is over". Track the resolve itself, or the pilot walks into a live combat.
  if (!api.CMB._playtestWrapped) {
    const inner = api.CMB._resolveNow;
    api.CMB._resolveNow = async function (...a) {
      r.combatBusy = (r.combatBusy || 0) + 1;
      try { return await inner.apply(this, a); } finally { r.combatBusy--; }
    };
    api.CMB._playtestWrapped = true;
  }

  // 1) blockers against each declared enemy strike
  win.askBlock = async (opts) => {
    const elig = (opts.elig || []).map((ref) => ({ ...ref, c: ref.c || api.unitAt(ref.key, ref.i) }))
      .filter((x) => x.c);
    if (!elig.length || persona.blocking === 'never') return [];
    const power = api.effA(opts.attacker);
    telem.blockOffers++;
    if (persona.blocking === 'always') {
      const survivor = elig.filter((x) => x.c.h > power).sort((a, b) => a.c.h - b.c.h)[0];
      const chosen = survivor || elig.sort((a, b) => a.c.h - b.c.h)[0];
      telem.blocks++;
      return [chosen];
    }
    // 'smart': block only when the body survives, or when the blow would otherwise kill something big
    const survivor = elig.filter((x) => x.c.h > power && !x.c.worker).sort((a, b) => b.c.a - a.c.a)[0];
    if (survivor) { telem.blocks++; return [survivor]; }
    const chump = elig.filter((x) => x.c.worker || (x.c.a || 0) === 0)[0];
    if (chump && power >= 2000) { telem.blocks++; return [chump]; }
    return [];
  };

  // 2) my gang-blocked attacker picks whose face it lands on
  win.askAbsorb = async (A, blks) => {
    const power = api.effA(A);
    const kill = blks.map((b, ix) => ({ b, ix })).filter((x) => x.b.h <= power)
      .sort((x, y) => y.b.a - x.b.a)[0];
    return kill ? kill.ix : 0;
  };

  // 3) my jointly-attacked creature picks who it strikes back at
  win.askRetaliate = async (T, grp) => {
    const kill = grp.map((a, ix) => ({ a, ix })).filter((x) => x.a.h <= (T.a || 0))
      .sort((x, y) => y.a.a - x.a.a)[0];
    return kill ? kill.ix : 0;
  };

  // 4) spring a summon trap on the AI's newest creature?
  win.playerTrapOnSummon = async (cr, w, i) => {
    const t = api.findArmedTrap('you', 'summon');
    const arr = api.cellArr('foe', w);
    if (!t || !cr || arr[i] !== cr) return;
    if ((cr.c || 0) < 3 && persona.name !== 'hunter') return;      // save it for something real
    win.eval('toGrave')('foe', cr);
    arr[i] = null;
    G.P.you.grave.push(win.eval('spellRec')(t.o.card));
    api.cellArr('you', t.w)[t.i] = null;
    api.cleanup();
    telem.trapsSprung.push({ turn: G.turnNo, on: cr.nm, trap: t.o.card.nm });
  };

  // 5) the anti-tell response window: hold, or spring an attack-trigger trap
  win.RESP.defendWindow = async () => api.findArmedTrap('you', 'attack') || null;
  win.RESP.actingGate = (trigger, then) => { then(null); };
}

// ---- the turn --------------------------------------------------------------------------

export async function playerTurn(r, persona, telem) {
  const { api, G } = r;
  settleUpkeep(r, persona, telem);

  const manaBefore = mana(r);
  api.doHarvest();
  telem.harvest.push({ turn: G.turnNo, gained: mana(r) - manaBefore, workers: api.totalWorkers('you') });
  if (G.phase !== 'draw') {
    telem.problems.push({ code: 'HARVEST_BLOCKED', msg: `turn ${G.turnNo}: phase stuck at ${G.phase}, deficit ${api.totalDeficit('you')}` });
    return false;
  }

  api.doDraw();
  if (G.phase !== 'action') {
    telem.problems.push({ code: 'DRAW_BLOCKED', msg: `turn ${G.turnNo}: phase stuck at ${G.phase}` });
    return false;
  }

  telem.manaAfterHarvest = telem.manaAfterHarvest || [];
  telem.manaAfterHarvest.push({ turn: G.turnNo, mana: mana(r) });

  await persona.action(r, telem);
  if (G.over) return true;

  // end of the action phase: what is left, and what could still have been bought with it
  const left = mana(r);
  const aff = G.P.you.hand.filter((c) => (c.c || 0) <= left);
  const slots = api.cellArr('you', 'front').filter((x) => !x).length
    + api.cellArr('you', 'back').filter((x) => !x).length;
  telem.turnEnd = telem.turnEnd || [];
  telem.turnEnd.push({
    turn: G.turnNo, left, hand: G.P.you.hand.length, affordable: aff.length,
    // why an affordable card stayed in hand: no room, no target, or nothing stopping it
    affCreature: aff.filter((c) => c.type === 'creature').length,
    affSpell: aff.filter((c) => c.type === 'spell' && !c.trap).length,
    affTrap: aff.filter((c) => c.type === 'spell' && c.trap).length,
    spellHasTarget: aff.filter((c) => c.type === 'spell' && !c.trap && api.validSpellTargetAny(c)).length,
    freeSlots: slots,
  });

  api.endTurn();
  return true;
}
