// Two claims about interception, checked against the live engine rather than by reading:
//   1. a creature that has already attacked (tapped) can still block — attacking costs nothing
//      defensively, so there is no attack-or-defend tension;
//   2. a worker cannot block once it has harvested — and harvesting taps the whole stack every
//      turn, so the "worker stacks screen their row" rule is unreachable in normal play;
//   3. a creature standing in the enemy BACK row hits the wall with no row to be intercepted from.
import { newRealm } from './engine.mjs';

const realm = await newRealm({});
const { win, G, api } = realm;
realm.setSeed(3);
api.startGame('fire', 'water', undefined, undefined);

const mkCre = win.eval('mkCre');
const put = (key, i, card, owner) => { const c = mkCre(card, owner, false); api.rowArr(key)[i] = c; return c; };

const atk = put('youFront', 3, { nm: 'Attacker', a: 1500, h: 1000, c: 3, up: 1 }, 'you');
const def = put('foeFront', 3, { nm: 'Defender', a: 1000, h: 1000, c: 2, up: 1 }, 'foe');

const elig = () => api.eligibleInterceptors('you', api.rowIdx('youFront'), api.rowIdx('foeBack'))
  .map((r) => (r.c.worker ? 'worker@' + r.key : r.c.nm + '@' + r.key));

console.log('1. TAPPED CREATURE AS A BLOCKER');
console.log('   fresh defender is eligible:      ', elig());
def.tapped = true;
console.log('   after it has attacked (tapped):  ', elig(), '  <- still offered');
def.sick = true;
console.log('   summoning-sick as well:          ', elig(), '  <- still offered (by design)');
def.blocked = true;
console.log('   once it has already blocked:     ', elig(), '  <- the only gate is `blocked`');
def.blocked = false; def.tapped = false; def.sick = false;

console.log('\n2. WORKERS AS A SCREEN');
const pool = api.minPool('foe', 'front');
console.log('   foe front worker pool size:', pool.length, '(rowWorkers =', api.rowWorkers('foe', 'front') + ')');
api.cellArr('foe', 'front')[1] = win.eval('mkBld')(api.STRUCT_DEFS.encampment, 'foe');
api.syncWorkers('foe');
win.eval('readyWorkers')('foe');
console.log('   with an Encampment behind them:', api.minPool('foe', 'front').length, 'workers, eligible:', elig());
api.minPool('foe', 'front').forEach((m) => { m.tapped = true; });      // exactly what harvesting does
console.log('   after their owner harvests:    ', elig(), '  <- the stack has vanished as a screen');

console.log('\n3. THE SIEGE SQUARE');
const rowsCrossed = win.eval('rowsCrossedInto');
console.log('   attacking the wall from your own front row crosses:', JSON.stringify(rowsCrossed(api.rowIdx('youFront'), -1)));
console.log('   attacking the wall from THEIR back row crosses:    ', JSON.stringify(rowsCrossed(api.rowIdx('foeBack'), -1)),
  '  <- nothing may interpose');
console.log('   ...and their own back row is a legal square to move into:',
  api.adjCells('you', 'foeFront', 3).some(([k]) => k === 'foeBack'));
