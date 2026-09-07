// Turn a C# self-play file (PlaytestCli) into the numbers a designer argues from.
//   node tools/playtest/analyze-csharp.mjs [tools/playtest/out/csharp-selfplay.json] [--json]
import { readFileSync } from 'node:fs';

const file = process.argv[2] && !process.argv[2].startsWith('--') ? process.argv[2] : 'tools/playtest/out/csharp-selfplay.json';
const AS_JSON = process.argv.includes('--json');
const data = JSON.parse(readFileSync(file, 'utf8'));
const M = data.matches;

const pct = (n, d) => (d ? Math.round(100 * n / d) + '%' : '—');
const avg = (xs) => (xs.length ? xs.reduce((a, b) => a + b, 0) / xs.length : 0);
const med = (xs) => { const a = [...xs].sort((x, y) => x - y); return a.length ? a[Math.floor(a.length / 2)] : 0; };
const r1 = (x) => Math.round(x * 10) / 10;
const group = (rs, f) => rs.reduce((m, r) => { (m[f(r)] ||= []).push(r); return m; }, {});
const sumDict = (dicts) => dicts.reduce((m, d) => { for (const [k, v] of Object.entries(d || {})) m[k] = (m[k] || 0) + v; return m; }, {});

// ── outcomes ────────────────────────────────────────────────────────────────────────────
const decided = M.filter((m) => m.outcome === 'you' || m.outcome === 'foe');
const out = {
  file, matches: M.length, seeds: data.seeds, turnCap: data.turnCap, elapsedSec: data.elapsedSec,
  outcomes: {
    firstPlayerWins: M.filter((m) => m.outcome === 'you').length,   // "you" always opens
    secondPlayerWins: M.filter((m) => m.outcome === 'foe').length,
    timeouts: M.filter((m) => m.outcome === 'timeout').length,
    rejected: M.filter((m) => m.outcome === 'rejected').length,
    firstPlayerWinRate: pct(M.filter((m) => m.outcome === 'you').length, decided.length),
  },
  length: {
    medianTurnsDecided: med(decided.map((m) => m.turns)),
    p10: [...decided.map((m) => m.turns)].sort((a, b) => a - b)[Math.floor(decided.length * 0.1)] ?? 0,
    p90: [...decided.map((m) => m.turns)].sort((a, b) => a - b)[Math.floor(decided.length * 0.9)] ?? 0,
    medianCommands: med(decided.map((m) => m.commands)),
  },
};

// ── per commander: wins whether it sat first or second ──────────────────────────────────
const cc = {};
for (const m of M) {
  for (const [id, side] of [[m.you, 'you'], [m.foe, 'foe']]) {
    const c = (cc[id] ||= { id, n: 0, wins: 0, losses: 0, timeouts: 0, mirrorsExcluded: 0, wallDamageDealt: [], wallDamageTaken: [], harvest: [], structMana: [], turns: [] });
    if (m.you === m.foe) { c.mirrorsExcluded++; continue; }
    c.n++;
    if (m.outcome === side) c.wins++; else if (m.outcome === 'timeout') c.timeouts++; else if (m.outcome !== 'rejected') c.losses++;
    const me = side === 'you' ? m.y : m.f, them = side === 'you' ? m.f : m.y;
    c.wallDamageDealt.push(them.wallDamageTaken); c.wallDamageTaken.push(me.wallDamageTaken);
    c.harvest.push(me.harvest); c.structMana.push(me.structMana); c.turns.push(m.turns);
  }
}
out.byCommander = Object.values(cc).map((c) => ({
  id: c.id, n: c.n, winRate: pct(c.wins, c.n), wins: c.wins, losses: c.losses, timeouts: c.timeouts,
  avgWallDmgDealt: Math.round(avg(c.wallDamageDealt)), avgWallDmgTaken: Math.round(avg(c.wallDamageTaken)),
  avgHarvest: Math.round(avg(c.harvest)), avgStructMana: Math.round(avg(c.structMana)),
})).sort((a, b) => parseInt(b.winRate) - parseInt(a.winRate));

// ── matchup grid (row = first player, col = second), win rate of the ROW ────────────────
const ids = data.commanders;
out.matchupGrid = ids.map((a) => {
  const row = { first: a };
  for (const b of ids) {
    const cell = M.filter((m) => m.you === a && m.foe === b);
    const dec = cell.filter((m) => m.outcome !== 'timeout' && m.outcome !== 'rejected');
    row[b] = dec.length ? pct(dec.filter((m) => m.outcome === 'you').length, dec.length) : '—';
  }
  return row;
});

// ── economy: where the mana came from, and how much was thrown away ────────────────────
const both = M.flatMap((m) => [m.y, m.f]);
const harvest = both.reduce((s, p) => s + p.harvest, 0);
const struct = both.reduce((s, p) => s + p.structMana, 0);
out.economy = {
  harvestedMana: harvest, structureMana: struct,
  harvestShare: pct(harvest, harvest + struct), structureShare: pct(struct, harvest + struct),
  drainedPerSidePerMatch: r1(avg(both.map((p) => p.drained))),
  keptPerSidePerMatch: r1(avg(both.map((p) => p.kept))),
  manaPerTurnPerSide: r1((harvest + struct) / Math.max(1, M.reduce((s, m) => s + m.turns, 0))),
  workersAtEndPerSide: [0, 1, 2].map((z) => r1(avg(both.map((p) => p.workers[z])))),
  shortfallSettlementsPerSide: r1(avg(both.map((p) => p.shortfalls))),
};

// ── what gets built, and what it becomes ───────────────────────────────────────────────
const built = sumDict(both.map((p) => p.built));
const upgraded = sumDict(both.map((p) => p.upgraded));
const standing = sumDict(both.map((p) => p.standing));
const perSide = (d) => Object.fromEntries(Object.entries(d).sort((a, b) => b[1] - a[1]).map(([k, v]) => [k, r1(v / both.length)]));
out.builds = { perSidePerMatch: perSide(built), upgradesPerSidePerMatch: perSide(upgraded), standingAtEndPerSide: perSide(standing) };

// ── combat: what kills, what dies, how walls fall ──────────────────────────────────────
out.combat = {
  towerShotsPerMatch: r1(avg(M.map((m) => m.towerShots))),
  towerDamagePerMatch: Math.round(avg(M.map((m) => m.towerDamage))),
  towerShareOfCreatureDeaths: pct(M.reduce((s, m) => s + m.towerShots, 0), both.reduce((s, p) => s + p.creatureDeaths, 0)),
  summonsPerSide: r1(avg(both.map((p) => p.summons))),
  creatureDeathsPerSide: r1(avg(both.map((p) => p.creatureDeaths))),
  structuresLostPerSide: r1(avg(both.map((p) => p.structuresLost))),
  attacksDeclaredPerSide: r1(avg(both.map((p) => p.attacks))),
  wallHitsTakenPerSide: r1(avg(both.map((p) => p.wallHitsTaken))),
  wallDamageTakenPerSide: Math.round(avg(both.map((p) => p.wallDamageTaken))),
  spellsPerSide: r1(avg(both.map((p) => p.spells))), trapsPerSide: r1(avg(both.map((p) => p.traps))),
  revivesPerSide: r1(avg(both.map((p) => p.revives))),
  creaturesStandingAtEndPerSide: r1(avg(both.map((p) => p.creatures))),
};

// ── how decided games ended: the loser's board when the wall fell ───────────────────────
const losers = decided.map((m) => (m.outcome === 'you' ? m.f : m.y));
const winners = decided.map((m) => (m.outcome === 'you' ? m.y : m.f));
out.endings = {
  winnerLifeLeft: Math.round(avg(winners.map((p) => p.life))),
  loserCreaturesStanding: r1(avg(losers.map((p) => p.creatures))),
  winnerCreaturesStanding: r1(avg(winners.map((p) => p.creatures))),
  loserHand: r1(avg(losers.map((p) => p.hand))), winnerHand: r1(avg(winners.map((p) => p.hand))),
  loserDeckLeft: r1(avg(losers.map((p) => p.deck))),
  loserHarvest: Math.round(avg(losers.map((p) => p.harvest))), winnerHarvest: Math.round(avg(winners.map((p) => p.harvest))),
  loserStructMana: Math.round(avg(losers.map((p) => p.structMana))), winnerStructMana: Math.round(avg(winners.map((p) => p.structMana))),
};

// ── the timeouts: what a stall looks like ───────────────────────────────────────────────
const to = M.filter((m) => m.outcome === 'timeout');
out.stalls = {
  n: to.length,
  avgLifeYou: Math.round(avg(to.map((m) => m.y.life))), avgLifeFoe: Math.round(avg(to.map((m) => m.f.life))),
  avgCreaturesYou: r1(avg(to.map((m) => m.y.creatures))), avgCreaturesFoe: r1(avg(to.map((m) => m.f.creatures))),
  avgAttacksYou: r1(avg(to.map((m) => m.y.attacks))), avgAttacksFoe: r1(avg(to.map((m) => m.f.attacks))),
  avgDeckYou: r1(avg(to.map((m) => m.y.deck))), avgDeckFoe: r1(avg(to.map((m) => m.f.deck))),
  pairings: Object.entries(group(to, (m) => m.you + ' v ' + m.foe)).map(([k, v]) => k + ' x' + v.length),
};

if (AS_JSON) { console.log(JSON.stringify(out, null, 1)); }
else {
  const show = (title, obj) => { console.log('\n== ' + title); console.log(JSON.stringify(obj, null, 1).replace(/^\s*[{}\[\]],?$/gm, '').replace(/\n\s*\n/g, '\n')); };
  console.log(file + ': ' + M.length + ' matches, ' + data.elapsedSec + 's');
  show('outcomes', out.outcomes); show('length', out.length);
  console.log('\n== byCommander'); console.table(out.byCommander);
  console.log('\n== matchupGrid (row = first player, cell = row win rate)'); console.table(out.matchupGrid);
  show('economy', out.economy); show('builds', out.builds); show('combat', out.combat); show('endings', out.endings); show('stalls', out.stalls);
}
