// Playtest runner: plays whole matches headlessly and writes a machine-readable result set.
//
//   node tools/playtest/run.mjs --matches 8 --out tools/playtest/out/smoke.json
//   node tools/playtest/run.mjs --matrix full --seeds 3 --out .../full.json
//   node tools/playtest/run.mjs --matches 4 --live-render      # keeps render() live: view crashes
//
// Every match is (playstyle + element deck) vs the shipped AI, played through the real player
// entry points. What comes out is one JSON record per match: the outcome, per-turn snapshots,
// every action the pilot took, and every invariant violation or stall the engine produced.

import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { newRealm, makeDeck, checkInvariants, cardConservation, pump, boardView, mine, theirs, creatures, buildings } from './engine.mjs';
import { PERSONAS, installPolicies, playerTurn } from './pilot.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, '../..');

const argv = process.argv.slice(2);
const flag = (n) => argv.includes('--' + n);
const opt = (n, d) => { const i = argv.indexOf('--' + n); return i >= 0 && argv[i + 1] ? argv[i + 1] : d; };

const TURN_CAP = +opt('turn-cap', 160);          // plies, not rounds
const SEEDS = +opt('seeds', 2);
const MATCHES = +opt('matches', 0);
const OUT = opt('out', join(HERE, 'out/run.json'));
const LIVE = flag('live-render');
const MATRIX = opt('matrix', 'core');
const REBOOT_EVERY = +opt('reboot-every', 24);
const VERBOSE = flag('verbose');

const ELEMENTS = ['fire', 'water', 'earth', 'wind', 'forest', 'electric', 'light', 'dark'];
const DUALS = ['fire_water', 'earth_forest', 'wind_electric', 'light_dark'];
const SHAPE_FOR = { aggro: 'aggro', wallrush: 'aggro', turtle: 'control', hunter: 'control', balanced: 'midrange', multiattack: 'midrange', sapper: 'midrange' };

/** Counters read straight out of the narrated log — the things no pilot can see from state. */
function digest(logs) {
  const d = {
    towerShots: 0, manaDrained: 0, drainEvents: 0, reliquaryReturns: 0, aiInterposes: 0,
    youWallHits: 0, youWallDamage: 0, foeWallHits: 0, foeWallDamage: 0,
    yourDeaths: 0, theirDeaths: 0, razed: 0, trapSprings: 0, undertows: 0, detonates: 0,
    hatches: 0, wards: 0, reaps: 0, scours: 0, overcharges: 0, bounces: 0, aiSacrifices: 0,
    emptyDraws: 0, harvestLines: 0,
  };
  for (const l of logs) {
    if (l.includes('fires for')) d.towerShots++;
    else if (l.includes('unspent mana drains away')) { d.drainEvents++; d.manaDrained += +(l.match(/◆([0-9]+) unspent/) || [0, 0])[1]; }
    else if (l.includes('Reliquary returns')) d.reliquaryReturns++;
    else if (l.includes('interposes')) d.aiInterposes++;
    else if (l.startsWith('You storm the castle wall')) { d.youWallHits++; d.youWallDamage += +(l.match(/⚔([0-9]+)/) || [0, 0])[1]; }
    else if (l.includes('storms your castle wall')) { d.foeWallHits++; d.foeWallDamage += +(l.match(/⚔([0-9]+)/) || [0, 0])[1]; }
    else if (l.startsWith('Your ') && (l.includes(' falls') || l.includes('is razed'))) d.yourDeaths++;
    else if (l.startsWith('Their ') && (l.includes(' falls') || l.includes('is razed'))) d.theirDeaths++;
    else if (l.includes('brings down')) d.razed++;
    else if (l.includes('springs')) d.trapSprings++;
    else if (l.startsWith('Undertow!')) d.undertows++;
    else if (l.startsWith('Detonate!')) d.detonates++;
    else if (l.includes('hatches!')) d.hatches++;
    else if (l.includes('conjures a Lumen')) d.wards++;
    else if (l.startsWith('Reap.')) d.reaps++;
    else if (l.startsWith('Scour!')) d.scours++;
    else if (l.startsWith('Overcharge!')) d.overcharges++;
    else if (l.includes('drags') && l.includes('back to')) d.bounces++;
    else if (l.includes('cannot pay its keep')) d.aiSacrifices++;
    else if (l.includes('nothing to draw')) d.emptyDraws++;
    else if (l.startsWith('Harvest:')) d.harvestLines++;
  }
  return d;
}

function freshTelemetry() {
  return {
    builds: [], upgrades: [], summons: [], sets: [], flips: [], spells: [], moves: [],
    sacrifices: [], attacks: [], harvest: [], trapsSprung: [], problems: [],
    blockOffers: 0, blocks: 0,
  };
}

function snapshot(r) {
  const { G, api } = r;
  const cells = boardView(r);
  const mineC = mine(cells);
  const foeC = theirs(cells);
  return {
    ply: G.turnNo,
    youLife: G.P.you.life, foeLife: G.P.foe.life,
    youMana: G.P.you.mana, foeMana: G.P.foe.mana,
    youWorkers: api.totalWorkers('you'), foeWorkers: api.totalWorkers('foe'),
    youStructs: buildings(mineC).length, foeStructs: buildings(foeC).length,
    youCreatures: creatures(mineC).length, foeCreatures: creatures(foeC).length,
    youHand: G.P.you.hand.length, foeHand: G.P.foe.hand.length,
    youDeck: G.P.you.deck.length, foeDeck: G.P.foe.deck.length,
    youGrave: G.P.you.grave.length, foeGrave: G.P.foe.grave.length,
    youVault: api.vaultCap('you'), foeVault: api.vaultCap('foe'),
    // who is standing in whose half — the siege square (their back row) is the unblockable
    // firing position, and the AI never marches into yours
    youInTheirRows: creatures(mineC).filter((c) => c.key === 'foeFront' || c.key === 'foeBack').length,
    youOnSiegeSquare: creatures(mineC).filter((c) => c.key === 'foeBack').length,
    foeInYourRows: creatures(foeC).filter((c) => c.key === 'youFront' || c.key === 'youBack').length,
  };
}

async function playMatch(realm, cfg) {
  const { G, api } = realm;
  realm.setSeed(cfg.seed);
  realm.clearLogs();
  const telem = freshTelemetry();
  installPolicies(realm, cfg.persona, telem);

  const rnd = () => realm.win.Math.random();
  const youDeck = makeDeck(realm, api.CCS[cfg.youCC].colors, cfg.shape, rnd);
  const foeDeck = makeDeck(realm, api.CCS[cfg.foeCC].colors, cfg.foeShape || 'random', rnd);

  // a fresh match must not inherit a half-finished interaction from the last one
  Object.assign(G, { build: null, sel: null, atk: [], decls: [], moveFrom: null, moveMana: null, cardMenu: null, busy: false, over: false });
  api.startGame(cfg.youCC, cfg.foeCC, youDeck, foeDeck);

  const violations = [];
  const snaps = [];
  const seenViolation = new Set();
  const record = (list, where) => {
    for (const v of list) {
      const k = v.code + '|' + v.msg.replace(/\d+/g, '#');
      if (seenViolation.has(k)) continue;
      seenViolation.add(k);
      violations.push({ ...v, where, ply: G.turnNo });
    }
  };

  const conserve0 = { you: cardConservation(realm, 'you'), foe: cardConservation(realm, 'foe') };
  let stalled = null;

  for (let round = 0; round < TURN_CAP && !G.over; round++) {
    record(checkInvariants(realm, `pre-turn ply ${G.turnNo}`, { strictWorkers: true }), 'pre-turn');

    const ok = await playerTurn(realm, cfg.persona, telem);
    if (!ok) { stalled = 'player-turn'; break; }
    if (G.over) break;

    record(checkInvariants(realm, `post-player ply ${G.turnNo}`), 'post-player');

    const back = await pump(() => G.over || (G.turn === 'you' && !G.busy && G.phase === 'upkeep'));
    if (!back) { stalled = 'ai-turn'; break; }
    snaps.push(snapshot(realm));
    if (G.over) break;

    for (const side of ['you', 'foe']) {
      const now = cardConservation(realm, side);
      if (now > conserve0[side]) {
        record([{ code: 'CARDS_MULTIPLIED', msg: `${side} deck-cards ${conserve0[side]} → ${now}` }], 'conservation');
        conserve0[side] = now;                                // report the first jump only
      }
    }
  }

  const winner = G.over ? (G.P.foe.life <= 0 && G.P.you.life > 0 ? 'you' : (G.P.you.life <= 0 ? 'foe' : 'both')) : 'timeout';
  return {
    ...cfg, persona: cfg.persona.name,
    winner, plies: G.turnNo, stalled,
    youLife: G.P.you.life, foeLife: G.P.foe.life,
    telem, snaps, violations, digest: digest(realm.logs),
    fullLog: cfg.keepLog ? realm.logs.slice() : null,
    logTail: (winner === 'timeout' || stalled || violations.length) ? realm.logs.slice(-60) : [],
    logLen: realm.logs.length,
  };
}

function buildMatrix() {
  const jobs = [];
  const names = Object.keys(PERSONAS);
  if (MATRIX === 'smoke') {
    for (const p of names) jobs.push({ persona: p, youCC: 'fire', foeCC: 'water', seed: 11 });
    return jobs;
  }
  if (MATRIX === 'grid') {
    // every playstyle against every element matchup — the clean read on deck power
    for (const p of names) for (const a of ELEMENTS) for (const b of ELEMENTS) {
      for (let s = 0; s < SEEDS; s++) jobs.push({ persona: p, youCC: a, foeCC: b, seed: 500 + s * 101 });
    }
    return jobs;
  }
  const foeFor = (i) => ELEMENTS[(i + 3) % ELEMENTS.length];
  for (const p of names) {
    for (let i = 0; i < ELEMENTS.length; i++) {
      for (let s = 0; s < SEEDS; s++) {
        jobs.push({ persona: p, youCC: ELEMENTS[i], foeCC: foeFor(i + s), seed: 1000 + s * 37 + i });
      }
    }
  }
  if (MATRIX === 'full') {
    for (const p of names) {
      for (let i = 0; i < DUALS.length; i++) {
        jobs.push({ persona: p, youCC: DUALS[i], foeCC: ELEMENTS[(i * 2 + 1) % 8], seed: 7000 + i });
        jobs.push({ persona: p, youCC: ELEMENTS[i], foeCC: DUALS[i], seed: 7100 + i });
      }
    }
    // mirror matches: the same element on both sides, where balance shows up cleanly
    for (const p of names) for (const e of ELEMENTS) jobs.push({ persona: p, youCC: e, foeCC: e, seed: 8000 });
  }
  return jobs;
}

(async () => {
  let jobs = buildMatrix();
  if (MATCHES > 0) jobs = jobs.slice(0, MATCHES);
  console.log(`playtest: ${jobs.length} matches (matrix=${MATRIX}, cap=${TURN_CAP} plies, render=${LIVE ? 'live' : 'stubbed'})`);

  const results = [];
  const keptLog = new Set();
  let realm = await newRealm({ liveRender: LIVE });
  const t0 = Date.now();

  for (let n = 0; n < jobs.length; n++) {
    if (n > 0 && n % REBOOT_EVERY === 0) realm = await newRealm({ liveRender: LIVE });
    const j = jobs[n];
    const cfg = {
      ...j,
      persona: PERSONAS[j.persona],
      shape: SHAPE_FOR[j.persona] || 'midrange',
      foeShape: 'random',
      keepLog: !keptLog.has(j.persona),                        // one readable match per playstyle
    };
    keptLog.add(j.persona);
    let res;
    try {
      res = await playMatch(realm, cfg);
    } catch (e) {
      res = {
        ...j, winner: 'error', crash: String((e && e.stack) || e),
        plies: realm.G.turnNo, logTail: realm.logs.slice(-40),
      };
      realm = await newRealm({ liveRender: LIVE });          // a thrown match leaves the realm dirty
    }
    results.push(res);
    if (VERBOSE || res.winner === 'error' || res.stalled || (res.violations || []).length) {
      console.log(`  [${n + 1}/${jobs.length}] ${j.persona} ${j.youCC} vs ${j.foeCC} → ${res.winner}`
        + ` ${res.plies}p${res.stalled ? ' STALLED:' + res.stalled : ''}`
        + `${(res.violations || []).length ? ' violations:' + res.violations.length : ''}`
        + `${res.crash ? ' CRASH' : ''}`);
    } else if ((n + 1) % 10 === 0) {
      console.log(`  … ${n + 1}/${jobs.length} (${((Date.now() - t0) / 1000).toFixed(0)}s)`);
    }
  }

  mkdirSync(dirname(OUT), { recursive: true });
  for (const r of results) {
    if (!r.fullLog) continue;
    const head = `# ${r.persona} ${r.youCC} vs ${r.foeCC} seed ${r.seed} → ${r.winner} in ${r.plies} plies`;
    writeFileSync(OUT.replace(/[.]json$/, '') + `.${r.persona}.log`, [head, ...r.fullLog].join('\n'));
    r.fullLog = null;                                          // keep the JSON readable
  }
  writeFileSync(OUT, JSON.stringify({
    generated: new Date().toISOString(), matrix: MATRIX, turnCap: TURN_CAP, liveRender: LIVE,
    results,
  }, null, 1));

  // ---- headline summary --------------------------------------------------------------
  const by = (f) => results.reduce((m, r) => { (m[f(r)] ||= []).push(r); return m; }, {});
  const rate = (rs) => (100 * rs.filter((r) => r.winner === 'you').length / rs.length).toFixed(0) + '%';
  console.log(`\ndone in ${((Date.now() - t0) / 1000).toFixed(0)}s → ${OUT}`);
  console.log('\nwin rate by playstyle:');
  for (const [k, rs] of Object.entries(by((r) => r.persona)))
    console.log(`  ${k.padEnd(12)} ${rate(rs).padStart(4)}  (${rs.length} matches, ${rs.filter((r) => r.winner === 'timeout').length} timeouts, median ${median(rs.map((r) => r.plies))} plies)`);
  console.log('\nwin rate by element:');
  for (const [k, rs] of Object.entries(by((r) => r.youCC)))
    console.log(`  ${k.padEnd(12)} ${rate(rs).padStart(4)}  (${rs.length})`);
  const probs = results.flatMap((r) => (r.violations || []).map((v) => v.code))
    .concat(results.flatMap((r) => ((r.telem && r.telem.problems) || []).map((p) => p.code)))
    .concat(results.filter((r) => r.crash).map(() => 'CRASH'))
    .concat(results.filter((r) => r.stalled).map((r) => 'STALL_' + r.stalled));
  const counts = probs.reduce((m, c) => { m[c] = (m[c] || 0) + 1; return m; }, {});
  console.log('\nproblems:', Object.keys(counts).length ? '' : ' none');
  for (const [c, n] of Object.entries(counts).sort((a, b) => b[1] - a[1])) console.log(`  ${c}: ${n}`);
})();

function median(xs) {
  const a = [...xs].sort((x, y) => x - y);
  return a.length ? a[Math.floor(a.length / 2)] : 0;
}
