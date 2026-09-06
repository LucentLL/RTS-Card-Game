// STRUCT_DEFS.longhouse is the only upgrade tier with no `from`, so bidLineage() cannot see the
// Encampment it grew out of. Everything that counts structures by lineage — the AI's build caps
// most of all — therefore treats an upgraded camp as if the camp were gone.
import { newRealm } from './engine.mjs';

const realm = await newRealm({});
const { win, G, api } = realm;
realm.setSeed(5);
api.startGame('fire', 'water', undefined, undefined);

const bidLineage = win.eval('bidLineage');
const tiers = ['keep', 'citadel', 'barracks', 'bastion', 'grandvault', 'grandforge', 'longhouse'];
console.log('upgrade-only tiers and the base they remember:');
for (const t of tiers) {
  const def = api.resolveStruct(t, 'fire');
  console.log(`  ${t.padEnd(11)} from: ${def && def.from ? def.from : '(none)'}`);
}

// give the foe a Foundry + Encampment, then upgrade the camp exactly as aiUpgrade would
G.P.foe.mana = 99;
api.cellArr('foe', 'back')[0] = win.eval('mkBld')(api.STRUCT_DEFS.foundry, 'foe');
api.cellArr('foe', 'front')[0] = win.eval('mkBld')(api.STRUCT_DEFS.encampment, 'foe');
api.syncWorkers('foe');

const camp = api.cellArr('foe', 'front')[0];
console.log('\nbefore upgrade: lineage', JSON.stringify(bidLineage(camp)),
  '· hasBuild(encampment) =', win.eval('hasBuild')('foe', 'encampment'));
win.eval('applyUpgrade')(camp, api.resolveStruct('longhouse', null));
api.syncWorkers('foe');
console.log('after  upgrade: lineage', JSON.stringify(bidLineage(camp)),
  '· hasBuild(encampment) =', win.eval('hasBuild')('foe', 'encampment'));

// the AI's own cap test, verbatim from aiBuild (07_structures.js)
const CAP = { foundry: 1, encampment: 1, longhouse: 1, vault: 1, outpost: 1, bulwark: 1, tower: 2, reliquary: 1 };
const capped = (bid) => win.eval('ownBuildings')('foe')
  .filter((b) => bidLineage(b).indexOf(bid) >= 0).length >= CAP[bid];
console.log('\nAI cap says "already have an Encampment"? ', capped('encampment'));
console.log('AI cap says "already have a Longhouse"?   ', capped('longhouse'));

let cycles = 0;
for (let i = 0; i < 6; i++) {
  if (capped('encampment')) break;
  if (!win.eval('aiBuild')('foe')) break;
  win.eval('aiUpgrade')('foe');
  api.syncWorkers('foe');
  cycles++;
}
const camps = win.eval('ownBuildings')('foe').map((b) => b.nm);
console.log('\nafter letting aiBuild/aiUpgrade run', cycles, 'more times, the foe owns:');
console.log('  ' + camps.join(', '));
console.log('  workers now:', api.totalWorkers('foe'));
console.log('\nEncampment(⚒2, ◆2) → Longhouse(⚒3, ◆4) → Barracks(⚒4, ◆3) is repeatable without limit:');
console.log('  every cycle is ⚒+4 for ◆9, and the cap that was meant to stop it never sees the camp.');
