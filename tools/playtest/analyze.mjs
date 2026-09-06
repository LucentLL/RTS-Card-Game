// Turn a results file into the numbers a designer argues from.
//   node tools/playtest/analyze.mjs tools/playtest/out/full.json [--json]
import { readFileSync } from 'node:fs';

const file = process.argv[2] || 'tools/playtest/out/full.json';
const AS_JSON = process.argv.includes('--json');
const data = JSON.parse(readFileSync(file, 'utf8'));
const R = data.results.filter((r) => !r.crash);

const pct = (n, d) => (d ? (100 * n / d).toFixed(0) + '%' : '—');
const avg = (xs) => (xs.length ? xs.reduce((a, b) => a + b, 0) / xs.length : 0);
const med = (xs) => { const a = [...xs].sort((x, y) => x - y); return a.length ? a[Math.floor(a.length / 2)] : 0; };
const group = (rs, f) => rs.reduce((m, r) => { (m[f(r)] ||= []).push(r); return m; }, {});

function line(name, rs) {
  const wins = rs.filter((r) => r.winner === 'you').length;
  const losses = rs.filter((r) => r.winner === 'foe').length;
  const to = rs.filter((r) => r.winner === 'timeout').length;
  return {
    name, n: rs.length, winRate: pct(wins, rs.length), wins, losses, timeouts: to,
    medianPlies: med(rs.map((r) => r.plies)),
    avgWallDmgDealt: Math.round(avg(rs.map((r) => (r.digest || {}).youWallDamage || 0))),
    avgWallDmgTaken: Math.round(avg(rs.map((r) => (r.digest || {}).foeWallDamage || 0))),
  };
}

const out = {
  file, matches: R.length, crashes: data.results.filter((r) => r.crash).length,
  byPersona: Object.entries(group(R, (r) => r.persona)).map(([k, v]) => line(k, v)),
  byElement: Object.entries(group(R, (r) => r.youCC)).map(([k, v]) => line(k, v)),
  byFoeElement: Object.entries(group(R, (r) => r.foeCC)).map(([k, v]) => line(k, v)),
};

// economy shape: what the average game looks like at ply 5 / 11 / 21 / 41
out.economyCurve = [5, 11, 21, 41].map((ply) => {
  const at = R.map((r) => (r.snaps || []).find((s) => s.ply >= ply)).filter(Boolean);
  const f = (k) => +avg(at.map((s) => s[k])).toFixed(1);
  return {
    ply, n: at.length,
    youWorkers: f('youWorkers'), foeWorkers: f('foeWorkers'),
    youStructs: f('youStructs'), foeStructs: f('foeStructs'),
    youCreatures: f('youCreatures'), foeCreatures: f('foeCreatures'),
    youLife: f('youLife'), foeLife: f('foeLife'),
    youMana: f('youMana'), foeMana: f('foeMana'),
    youHand: f('youHand'), foeHand: f('foeHand'),
    youInTheirRows: f('youInTheirRows'), youOnSiege: f('youOnSiegeSquare'), foeInYourRows: f('foeInYourRows'),
  };
});

// how the games that DID end, ended
const ended = R.filter((r) => r.winner === 'you' || r.winner === 'foe');
out.endings = {
  playerWins: ended.filter((r) => r.winner === 'you').length,
  aiWins: ended.filter((r) => r.winner === 'foe').length,
  timeouts: R.filter((r) => r.winner === 'timeout').length,
  medianPliesWin: med(ended.filter((r) => r.winner === 'you').map((r) => r.plies)),
  medianPliesLoss: med(ended.filter((r) => r.winner === 'foe').map((r) => r.plies)),
};

// aggregate log digest
const keys = Object.keys(R.find((r) => r.digest)?.digest || {});
out.digestPerMatch = Object.fromEntries(keys.map((k) => [k, +avg(R.map((r) => (r.digest || {})[k] || 0)).toFixed(1)]));

// pilot action volume
const T = (r, k) => ((r.telem || {})[k] || []).length;
out.actionsPerMatch = Object.fromEntries(
  ['builds', 'upgrades', 'summons', 'sets', 'flips', 'spells', 'moves', 'sacrifices', 'attacks', 'trapsSprung']
    .map((k) => [k, +avg(R.map((r) => T(r, k))).toFixed(1)]));
out.blockRate = pct(R.reduce((s, r) => s + ((r.telem || {}).blocks || 0), 0),
  R.reduce((s, r) => s + ((r.telem || {}).blockOffers || 0), 0));

// attack outcomes: how much of a declared attack actually lands on the wall
const atk = R.flatMap((r) => (r.telem || {}).attacks || []);
out.attacks = {
  total: atk.length,
  joint: atk.filter((a) => a.joint).length,
  zeroDamage: pct(atk.filter((a) => !a.wallDamage).length, atk.length),
  avgWallDamage: Math.round(avg(atk.map((a) => a.wallDamage || 0))),
  byPlan: Object.entries(group(atk, (a) => a.plan)).map(([k, v]) => ({
    plan: k, n: v.length,
    avgWallDamage: Math.round(avg(v.map((a) => a.wallDamage || 0))),
    creaturesLostPerSwing: +avg(v.map((a) => a.lostMine || 0)).toFixed(2),
    creaturesKilledPerSwing: +avg(v.map((a) => a.killedTheirs || 0)).toFixed(2),
    structuresRazedPerSwing: +avg(v.map((a) => a.razedTheirs || 0)).toFixed(2),
  })),
  jointVsSingle: ['joint', 'single'].map((k) => {
    const v = atk.filter((a) => (k === 'joint') === !!a.joint);
    return {
      mode: k, n: v.length,
      avgWallDamage: Math.round(avg(v.map((a) => a.wallDamage || 0))),
      creaturesLostPerSwing: +avg(v.map((a) => a.lostMine || 0)).toFixed(2),
      creaturesKilledPerSwing: +avg(v.map((a) => a.killedTheirs || 0)).toFixed(2),
    };
  }),
};

// problems
const probs = {};
for (const r of R) {
  for (const v of r.violations || []) probs[v.code] = (probs[v.code] || 0) + 1;
  for (const p of ((r.telem || {}).problems) || []) probs[p.code] = (probs[p.code] || 0) + 1;
  if (r.stalled) probs['STALL_' + r.stalled] = (probs['STALL_' + r.stalled] || 0) + 1;
}
out.problems = probs;
out.sampleProblemMatches = R.filter((r) => (r.violations || []).length || r.stalled)
  .slice(0, 8).map((r) => ({ persona: r.persona, youCC: r.youCC, foeCC: r.foeCC, seed: r.seed, stalled: r.stalled, violations: r.violations }));

if (AS_JSON) { console.log(JSON.stringify(out, null, 1)); process.exit(0); }

const table = (rows) => {
  const cols = Object.keys(rows[0]);
  const w = cols.map((c) => Math.max(c.length, ...rows.map((r) => String(r[c]).length)));
  console.log(cols.map((c, i) => c.padEnd(w[i])).join('  '));
  for (const r of rows) console.log(cols.map((c, i) => String(r[c]).padEnd(w[i])).join('  '));
};

console.log(`\n=== ${out.matches} matches (${out.crashes} crashes) — ${file}\n`);
console.log('— by playstyle —'); table(out.byPersona);
console.log('\n— by your element —'); table(out.byElement);
console.log('\n— by opponent element —'); table(out.byFoeElement);
console.log('\n— average board at ply N —'); table(out.economyCurve);
console.log('\n— endings —', JSON.stringify(out.endings));
console.log('\n— per-match log digest (averages) —', JSON.stringify(out.digestPerMatch, null, 1));
console.log('\n— pilot actions per match —', JSON.stringify(out.actionsPerMatch));
console.log('  pilot blocked', out.blockRate, 'of the strikes it was offered a blocker against');
console.log('\n— attacks —', JSON.stringify(out.attacks, null, 1));
console.log('\n— problems —', JSON.stringify(out.problems));
