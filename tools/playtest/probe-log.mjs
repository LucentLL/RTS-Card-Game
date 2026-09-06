// Dump the narrated log of one match so a human (or Claude) can read what actually happened.
//   node tools/playtest/probe-log.mjs <persona> <youCC> <foeCC> <seed> [--from N] [--to N]
import { newRealm, makeDeck, pump } from './engine.mjs';
import { PERSONAS, installPolicies, playerTurn } from './pilot.mjs';

const [persona = 'balanced', youCC = 'fire', foeCC = 'water', seed = '11'] = process.argv.slice(2);
const argv = process.argv.slice(2);
const opt = (n, d) => { const i = argv.indexOf('--' + n); return i >= 0 ? +argv[i + 1] : d; };
const FROM = opt('from', 0), TO = opt('to', 1e9), CAP = opt('cap', 160);
const SHAPE = { aggro: 'aggro', wallrush: 'aggro', turtle: 'control', hunter: 'control' }[persona] || 'midrange';

const realm = await newRealm({});
const { G, api, win } = realm;
realm.setSeed(+seed);
const telem = { builds: [], upgrades: [], summons: [], sets: [], flips: [], spells: [], moves: [], sacrifices: [], attacks: [], harvest: [], trapsSprung: [], problems: [], blockOffers: 0, blocks: 0 };
const P = PERSONAS[persona];
installPolicies(realm, P, telem);
const rnd = () => win.Math.random();
api.startGame(youCC, foeCC, makeDeck(realm, api.CCS[youCC].colors, SHAPE, rnd), makeDeck(realm, api.CCS[foeCC].colors, 'random', rnd));

const marked = [];
const base = win.log;
win.log = (html) => { base(html); marked.push({ ply: G.turnNo, line: realm.logs[realm.logs.length - 1] }); };

for (let i = 0; i < CAP && !G.over; i++) {
  if (!(await playerTurn(realm, P, telem))) break;
  if (G.over) break;
  if (!(await pump(() => G.over || (G.turn === 'you' && !G.busy && G.phase === 'upkeep')))) break;
}
for (const m of marked) if (m.ply >= FROM && m.ply <= TO) console.log(String(m.ply).padStart(4), m.line);
console.log('---', { persona, youCC, foeCC, plies: G.turnNo, you: G.P.you.life, foe: G.P.foe.life, over: G.over });
