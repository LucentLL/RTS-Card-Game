// A/B experiments: the same playtest, one rule changed, so a design question gets an answer with a
// number attached instead of an opinion. Each variant boots its own realm, tweaks the live rules
// objects, and plays the same matrix of playstyles and elements.
//
//   node tools/playtest/experiments.mjs                       # every variant
//   node tools/playtest/experiments.mjs --only no-tower,baseline
//   node tools/playtest/experiments.mjs --seeds 3 --out tools/playtest/out/experiments.json

import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { newRealm, makeDeck, pump } from './engine.mjs';
import { PERSONAS, installPolicies, playerTurn } from './pilot.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const argv = process.argv.slice(2);
const opt = (n, d) => { const i = argv.indexOf('--' + n); return i >= 0 && argv[i + 1] ? argv[i + 1] : d; };
const SEEDS = +opt('seeds', 2);
const CAP = +opt('turn-cap', 120);
const OUT = opt('out', join(HERE, 'out/experiments.json'));
const ONLY = (opt('only', '') || '').split(',').filter(Boolean);

const ELEMENTS = ['fire', 'wind', 'dark', 'earth'];          // one from each corner of the stat space
const SHAPE_FOR = { aggro: 'aggro', wallrush: 'aggro', turtle: 'control', hunter: 'control' };

/** Each variant is one rule change, applied to the live rules objects after boot. */
const VARIANTS = [
  { key: 'baseline', what: 'the game as it ships', apply() {} },

  {
    key: 'no-tower',
    what: 'Cannon Tower removed from both build menus (and from the Outpost upgrade branch)',
    apply(realm) {
      const { win, api } = realm;
      api.STRUCT_DEFS.outpost.up2 = ['bastion'];
      const orig = win.buildList;
      win.buildList = (cc) => orig(cc).filter((d) => d.bid !== 'tower');
    },
  },
  {
    key: 'no-reliquary',
    what: 'Reliquary removed — no free creature back from the grave every upkeep',
    apply(realm) {
      const orig = realm.win.buildList;
      realm.win.buildList = (cc) => orig(cc).filter((d) => d.bid !== 'reliquary');
    },
  },
  {
    key: 'no-drain',
    what: 'unspent mana is kept across the turn instead of draining to the vault cap',
    apply(realm) {
      realm.win.drainMana = (owner) => ({ keep: realm.G.P[owner].mana, lost: 0 });
    },
  },
  {
    key: 'life-5000',
    what: 'both life pools halved to 5000 — how much of the game is the length of the race?',
    apply(realm) { for (const id of Object.keys(realm.api.CCS)) realm.api.CCS[id].hp = 5000; },
  },
  {
    key: 'towers-500',
    what: 'Cannon Tower fires for 500 instead of 1000 — under the HP of most 1-drops',
    apply(realm) { realm.api.STRUCT_DEFS.tower.val = 500; },
  },
  {
    key: 'untapped-workers',
    what: 'harvesting no longer taps the workers, so a worker stack can still screen its row',
    apply(realm) {
      const { win, G, api } = realm;
      const orig = win.doHarvest;
      win.doHarvest = function () {
        const before = ['back', 'front', 'center'].map((w) => api.minPool('you', w).map((m) => m.tapped));
        orig();
        ['back', 'front', 'center'].forEach((w, ix) => api.minPool('you', w)
          .forEach((m, i) => { if (before[ix][i] === false) m.tapped = false; }));
      };
      // the AI harvests inline in foeTurn; untap its stacks again right after it does
      const st = win.startTurn;
      win.startTurn = function (owner) {
        st(owner);
        if (owner === 'foe') setTimeout(() => ['back', 'front', 'center']
          .forEach((w) => api.minPool('foe', w).forEach((m) => { if (!m.sick) m.tapped = false; })), 0);
      };
    },
  },
  {
    key: 'go-second',
    what: 'the player concedes the opening turn — is turn order worth anything?',
    apply(realm) { realm.skipFirstTurn = true; },
  },

  /* ---- structure economy: how much mana a structure is allowed to print each upkeep ----
     Two candidate readings of "a structure may not yield more than half what it cost".
     An upgrade tier is bought with its own `c` on top of everything already paid for the tier
     below, so "what it cost" is either that upgrade price alone (strict) or the whole chain
     (cumulative). The forge is the one base structure that breaks the rule either way. */
  {
    key: 'forge-1',
    what: 'element Forge yields ◆1 instead of ◆2 — no BASE structure prints more than ◆1 (cumulative reading)',
    apply(realm) {
      const { win } = realm;
      const orig = win.forgeDef;
      win.forgeDef = (el) => ({ ...orig(el), val: 1 });
    },
  },
  {
    key: 'half-cost-strict',
    what: 'every tier capped at half its own price: Forge ◆1, Keep ◆1, Citadel ◆2 (strict reading)',
    apply(realm) {
      const { win, api } = realm;
      const orig = win.forgeDef;
      win.forgeDef = (el) => ({ ...orig(el), val: 1 });
      api.STRUCT_DEFS.keep.val = 1;
      api.STRUCT_DEFS.citadel.val = 2;
    },
  },
  {
    key: 'no-struct-mana',
    what: 'the far bracket — no structure prints mana at all, the economy is workers only',
    apply(realm) {
      const { win, api } = realm;
      const orig = win.forgeDef, origG = win.grandForgeDef;
      win.forgeDef = (el) => ({ ...orig(el), val: 0 });
      win.grandForgeDef = (el) => ({ ...origG(el), val: 0 });
      for (const b of ['foundry', 'keep', 'citadel']) api.STRUCT_DEFS[b].val = 0;
    },
  },
];

/** Sum every ◆ figure a log pattern captures — the economy's size, read out of the narration. */
const sumLog = (logs, re) => logs.reduce((s, l) => {
  let m, t = 0; const r = new RegExp(re.source, 'g');
  while ((m = r.exec(l))) t += +m[1];
  return s + t;
}, 0);

const freshTelemetry = () => ({
  builds: [], upgrades: [], summons: [], sets: [], flips: [], spells: [], moves: [],
  sacrifices: [], attacks: [], harvest: [], trapsSprung: [], problems: [], blockOffers: 0, blocks: 0,
});

async function playMatch(realm, cfg) {
  const { G, api } = realm;
  realm.setSeed(cfg.seed);
  realm.clearLogs();
  const telem = freshTelemetry();
  installPolicies(realm, cfg.persona, telem);
  const rnd = () => realm.win.Math.random();
  Object.assign(G, { build: null, sel: null, atk: [], decls: [], moveFrom: null, moveMana: null, cardMenu: null, busy: false, over: false });
  api.startGame(cfg.youCC, cfg.foeCC,
    makeDeck(realm, api.CCS[cfg.youCC].colors, cfg.shape, rnd),
    makeDeck(realm, api.CCS[cfg.foeCC].colors, 'random', rnd));

  let first = true;
  for (let round = 0; round < CAP && !G.over; round++) {
    const persona = (realm.skipFirstTurn && first) ? { ...cfg.persona, action: async () => {} } : cfg.persona;
    first = false;
    if (!(await playerTurn(realm, persona, telem))) break;
    if (G.over) break;
    if (!(await pump(() => G.over || (G.turn === 'you' && !G.busy && G.phase === 'upkeep')))) break;
  }
  const winner = G.over ? (G.P.foe.life <= 0 && G.P.you.life > 0 ? 'you' : (G.P.you.life <= 0 ? 'foe' : 'both')) : 'timeout';
  const wallDealt = telem.attacks.reduce((s, a) => s + (a.wallDamage || 0), 0);
  return {
    variant: cfg.variant, persona: cfg.persona.name, youCC: cfg.youCC, foeCC: cfg.foeCC, seed: cfg.seed,
    winner, plies: G.turnNo, youLife: G.P.you.life, foeLife: G.P.foe.life,
    wallDealt, attacks: telem.attacks.length, summons: telem.summons.length,
    builds: telem.builds.length, creaturesLost: telem.sacrifices.length,
    // the economy, read out of the narration: what the structures printed vs what the workers dug up
    structMana: sumLog(realm.logs, /yields ◆(\d+)/),
    harvestYou: sumLog(realm.logs, /Harvest: ◆(\d+)/),
    harvestFoe: sumLog(realm.logs, /Enemy workers harvest ◆(\d+)/),
    towerShots: realm.logs.filter((l) => l.includes('fires for')).length,
    revives: realm.logs.filter((l) => l.includes('Reliquary returns')).length,
    drained: realm.logs.filter((l) => l.includes('unspent mana drains')).length,
  };
}

const jobs = [];
for (const p of Object.keys(PERSONAS)) for (const e of ELEMENTS) for (let s = 0; s < SEEDS; s++) {
  jobs.push({ persona: p, youCC: e, foeCC: ELEMENTS[(ELEMENTS.indexOf(e) + 1 + s) % ELEMENTS.length], seed: 300 + s * 61 });
}

const chosen = VARIANTS.filter((v) => !ONLY.length || ONLY.includes(v.key));
console.log(`experiments: ${chosen.length} variants x ${jobs.length} matches`);
const results = [];
for (const v of chosen) {
  const t0 = Date.now();
  let realm = await newRealm({});
  v.apply(realm);
  let n = 0;
  for (const j of jobs) {
    if (n++ && n % 28 === 0) { realm = await newRealm({}); v.apply(realm); }
    try {
      results.push(await playMatch(realm, {
        ...j, variant: v.key, persona: PERSONAS[j.persona], shape: SHAPE_FOR[j.persona] || 'midrange',
      }));
    } catch (e) {
      results.push({ variant: v.key, persona: j.persona, youCC: j.youCC, foeCC: j.foeCC, winner: 'error', err: String(e && e.message) });
      realm = await newRealm({}); v.apply(realm);
    }
  }
  const rs = results.filter((r) => r.variant === v.key);
  const wins = rs.filter((r) => r.winner === 'you').length;
  const to = rs.filter((r) => r.winner === 'timeout').length;
  const avg = (f) => (rs.reduce((s, r) => s + (f(r) || 0), 0) / rs.length).toFixed(1);
  console.log(`  ${v.key.padEnd(17)} player wins ${String(Math.round(100 * wins / rs.length)).padStart(3)}%`
    + ` · median ${[...rs].sort((a, b) => a.plies - b.plies)[Math.floor(rs.length / 2)].plies} plies`
    + ` · timeouts ${to} · wall dmg ${avg((r) => r.wallDealt)} · summons ${avg((r) => r.summons)}`
    + ` · builds ${avg((r) => r.builds)} · struct ◆${avg((r) => r.structMana)}`
    + ` · dug ◆${avg((r) => r.harvestYou + r.harvestFoe)}`
    + ` · tower shots ${avg((r) => r.towerShots)}  (${((Date.now() - t0) / 1000).toFixed(0)}s)  — ${v.what}`);
}
mkdirSync(dirname(OUT), { recursive: true });
writeFileSync(OUT, JSON.stringify({ generated: new Date().toISOString(), variants: chosen.map((v) => ({ key: v.key, what: v.what })), results }, null, 1));
console.log('→ ' + OUT);
