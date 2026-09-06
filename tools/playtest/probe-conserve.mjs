// Why does a 40-card deck become 41? Play one match, count after every log line, and print the
// exact line that changed the total.
import { newRealm, makeDeck, cardConservation, pump } from './engine.mjs';
import { PERSONAS, installPolicies, playerTurn } from './pilot.mjs';

const realm = await newRealm({});
const { G, api, win } = realm;
realm.setSeed(11);
const telem = { builds: [], upgrades: [], summons: [], sets: [], flips: [], spells: [], moves: [], sacrifices: [], attacks: [], harvest: [], trapsSprung: [], problems: [], blockOffers: 0, blocks: 0 };
const persona = PERSONAS[process.argv[2] || 'aggro'];
installPolicies(realm, persona, telem);

const rnd = () => win.Math.random();
const youDeck = makeDeck(realm, api.CCS.fire.colors, 'aggro', rnd);
const foeDeck = makeDeck(realm, api.CCS.water.colors, 'random', rnd);
api.startGame('fire', 'water', youDeck, foeDeck);

let last = { you: cardConservation(realm, 'you'), foe: cardConservation(realm, 'foe') };
const recent = [];
const baseLog = win.log;
win.log = (html) => {
  baseLog(html);
  recent.push(realm.logs[realm.logs.length - 1]);
  if (recent.length > 6) recent.shift();
  for (const side of ['you', 'foe']) {
    const now = cardConservation(realm, side);
    if (now !== last[side]) {
      if (now > last[side]) {
        console.log(`\n*** ${side} ${last[side]} → ${now} at ply ${G.turnNo}`);
        console.log(recent.map((l) => '    · ' + l).join('\n'));
      }
      last[side] = now;
    }
  }
};

for (let i = 0; i < 200 && !G.over; i++) {
  if (!(await playerTurn(realm, persona, telem))) break;
  if (G.over) break;
  if (!(await pump(() => G.over || (G.turn === 'you' && !G.busy && G.phase === 'upkeep')))) break;
}
console.log('\nfinal', { plies: G.turnNo, you: cardConservation(realm, 'you'), foe: cardConservation(realm, 'foe'), over: G.over });
