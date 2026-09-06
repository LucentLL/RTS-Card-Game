// Does a big economy actually buy anything? Three questions, measured rather than argued:
//   1. how high does mana actually get, in-turn and at the moment the upkeep bill lands
//   2. when the turn ends with mana left over, was there anything in hand it could have bought
//   3. does mana ever pay for an aggressive position (a creature parked in the enemy rows)
//
//   node tools/playtest/probe-mana.mjs [--seeds 2]
import { newRealm, makeDeck, pump, boardView, mine, creatures } from './engine.mjs';
import { PERSONAS, installPolicies, playerTurn } from './pilot.mjs';

const argv = process.argv.slice(2);
const opt = (n, d) => { const i = argv.indexOf('--' + n); return i >= 0 && argv[i + 1] ? argv[i + 1] : d; };
const SEEDS = +opt('seeds', 2);
const CAP = +opt('turn-cap', 120);
const ELEMENTS = ['fire', 'wind', 'dark', 'earth', 'water', 'light'];
const SHAPE_FOR = { aggro: 'aggro', wallrush: 'aggro', turtle: 'control', hunter: 'control' };

const fresh = () => ({
  builds: [], upgrades: [], summons: [], sets: [], flips: [], spells: [], moves: [],
  sacrifices: [], attacks: [], harvest: [], trapsSprung: [], problems: [], blockOffers: 0, blocks: 0,
});

const rows = [];
let realm = await newRealm({});
let n = 0;

for (const pname of Object.keys(PERSONAS)) {
  for (const el of ELEMENTS) {
    for (let s = 0; s < SEEDS; s++) {
      if (n++ && n % 24 === 0) realm = await newRealm({});
      const { G, api, win } = realm;
      const persona = PERSONAS[pname];
      const telem = fresh();
      realm.setSeed(400 + s * 91);
      realm.clearLogs();
      installPolicies(realm, persona, telem);
      const rnd = () => win.Math.random();
      const foe = ELEMENTS[(ELEMENTS.indexOf(el) + 1 + s) % ELEMENTS.length];
      Object.assign(G, { build: null, sel: null, atk: [], decls: [], moveFrom: null, moveMana: null, cardMenu: null, busy: false, over: false });
      api.startGame(el, foe, makeDeck(realm, api.CCS[el].colors, SHAPE_FOR[pname] || 'midrange', rnd),
        makeDeck(realm, api.CCS[foe].colors, 'random', rnd));

      let raidTurns = 0, turns = 0;
      for (let round = 0; round < CAP && !G.over; round++) {
        if (!(await playerTurn(realm, persona, telem))) break;
        if (G.over) break;
        if (!(await pump(() => G.over || (G.turn === 'you' && !G.busy && G.phase === 'upkeep')))) break;
        turns++;
        if (creatures(mine(boardView(realm))).some((c) => c.key === 'foeFront' || c.key === 'foeBack')) raidTurns++;
      }
      rows.push({
        persona: pname, el, winner: G.over ? (G.P.foe.life <= 0 ? 'you' : 'foe') : 'timeout',
        turns, raidTurns, telem,
      });
    }
  }
}

const all = (f) => rows.flatMap(f);
const avg = (xs) => (xs.length ? xs.reduce((a, b) => a + b, 0) / xs.length : 0);
const pct = (n, d) => (d ? (100 * n / d).toFixed(0) + '%' : '—');
const q = (xs, p) => { const a = [...xs].sort((x, y) => x - y); return a.length ? a[Math.min(a.length - 1, Math.floor(a.length * p))] : 0; };

console.log(`\n${rows.length} matches · ${rows.reduce((s, r) => s + r.turns, 0)} player turns\n`);

const afterHarvest = all((r) => (r.telem.manaAfterHarvest || []).map((x) => x.mana));
console.log('MANA IN HAND AT THE TOP OF THE ACTION PHASE (the peak the player ever sees)');
console.log(`  median ◆${q(afterHarvest, .5)} · 75th ◆${q(afterHarvest, .75)} · 95th ◆${q(afterHarvest, .95)} · max ◆${Math.max(...afterHarvest)}`);
const over = (n) => pct(afterHarvest.filter((m) => m >= n).length, afterHarvest.length);
console.log(`  turns at ◆20+: ${over(20)} · ◆30+: ${over(30)} · ◆50+: ${over(50)}`);

const late = all((r) => (r.telem.manaAfterHarvest || []).filter((x) => x.turn >= 25).map((x) => x.mana));
if (late.length) console.log(`  from ply 25 on: median ◆${q(late, .5)} · 95th ◆${q(late, .95)} · max ◆${Math.max(...late)}`);

const atUpkeep = all((r) => (r.telem.manaAtUpkeep || []).map((x) => x.mana));
console.log('\nMANA IN HAND WHEN THE UPKEEP BILL LANDS (after the end-of-turn drain)');
console.log(`  median ◆${q(atUpkeep, .5)} · 95th ◆${q(atUpkeep, .95)} · max ◆${Math.max(...atUpkeep)}`);
const owedTurns = all((r) => (r.telem.manaAtUpkeep || []).filter((x) => x.owed > 0));
console.log(`  turns with a shortfall to settle: ${owedTurns.length} of ${atUpkeep.length} (${pct(owedTurns.length, atUpkeep.length)})`);
console.log(`    of those, mana on hand ≥ the whole bill: ${pct(owedTurns.filter((x) => x.mana >= x.owed).length, owedTurns.length)}`);
console.log(`    settled by ◆ Pay: ${Math.round(avg(rows.map((r) => r.telem.upkeepPaid || 0)))} ◆/match`
  + ` · by moving: ${avg(rows.map((r) => r.telem.upkeepMoved || 0)).toFixed(1)}/match`
  + ` · by sacrifice: ${avg(rows.map((r) => r.telem.sacrifices.length)).toFixed(1)}/match`);

const ends = all((r) => r.telem.turnEnd || []);
console.log('\nWHAT THE LEFTOVER MANA COULD HAVE BOUGHT (end of the action phase)');
console.log(`  turns ending with ◆5+ unspent: ${pct(ends.filter((e) => e.left >= 5).length, ends.length)}`);
console.log(`  ...of those, turns where a card in hand was affordable and still unplayed: `
  + pct(ends.filter((e) => e.left >= 5 && e.affordable > 0).length, ends.filter((e) => e.left >= 5).length));
console.log(`  average affordable-but-unplayed cards on such a turn: ${avg(ends.filter((e) => e.left >= 5).map((e) => e.affordable)).toFixed(1)}`);
const rich = ends.filter((e) => e.left >= 5 && e.affordable > 0);
console.log('  what those affordable-but-unplayed cards are:');
console.log(`    creatures ${avg(rich.map((e) => e.affCreature)).toFixed(1)}/turn`
  + ` · spells ${avg(rich.map((e) => e.affSpell)).toFixed(1)} (${avg(rich.map((e) => e.spellHasTarget)).toFixed(1)} with a legal target)`
  + ` · traps ${avg(rich.map((e) => e.affTrap)).toFixed(1)}`);
console.log(`    free deploy slots on those turns: ${avg(rich.map((e) => e.freeSlots)).toFixed(1)} of 14`);
console.log(`    turns holding an affordable creature AND a free slot: ${pct(rich.filter((e) => e.affCreature > 0 && e.freeSlots > 0).length, rich.length)}`);

const owedT = all((r) => (r.telem.manaAtUpkeep || []).filter((x) => x.owed > 0));
const settles = rows.map((r) => (r.telem.payActions || 0) + (r.telem.upkeepMoved || 0) + r.telem.sacrifices.length);
const crisis = owedT.filter((x) => x.mana < x.owed).length;
console.log('');
console.log('HOW OFTEN DOES THE UPKEEP STEP ACTUALLY INTERRUPT YOU?');
console.log(`  turns that raise a settle prompt at all: ${pct(owedT.length, atUpkeep.length)}`);
console.log(`  settle actions per match: ${avg(settles).toFixed(1)} across a ${avg(rows.map((r) => r.turns)).toFixed(0)}-turn game`);
console.log(`    pay ${avg(rows.map((r) => r.telem.payActions || 0)).toFixed(1)} · move ${avg(rows.map((r) => r.telem.upkeepMoved || 0)).toFixed(1)} · sacrifice ${avg(rows.map((r) => r.telem.sacrifices.length)).toFixed(1)}`);
console.log(`  turns where the bill could NOT simply be paid — the only real decision: ${pct(crisis, atUpkeep.length)}`);
console.log('');
console.log('\nDID MANA EVER BUY AN AGGRESSIVE POSITION?');
const byP = {};
for (const r of rows) (byP[r.persona] ??= []).push(r);
for (const [k, rs] of Object.entries(byP)) {
  const raid = rs.reduce((s, r) => s + r.raidTurns, 0);
  const t = rs.reduce((s, r) => s + r.turns, 0);
  console.log(`  ${k.padEnd(12)} turns holding a creature in the enemy rows: ${pct(raid, t)}`
    + ` · upkeep ◆ paid ${Math.round(avg(rs.map((r) => r.telem.upkeepPaid || 0)))}/match`
    + ` · income ${(avg(rs.flatMap((r) => r.telem.harvest.map((h) => h.gained)))).toFixed(1)}/turn`
    + ` · win ${pct(rs.filter((r) => r.winner === 'you').length, rs.length)}`);
}
