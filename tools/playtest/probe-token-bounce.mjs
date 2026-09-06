// Repro: Riptide (bounce) on a TOKEN turns the token into a permanent hand card.
// applyUndertow filters tokens out (06_mana_workers.js, `!c.token`); resolveSpell's bounce branch
// (14_spells_traps.js) does not — so a 0-cost Lumen/Shade becomes a real, re-summonable card.
import { newRealm } from './engine.mjs';

const realm = await newRealm({});
const { win, G, api } = realm;
realm.setSeed(7);
api.startGame('water', 'light', undefined, undefined);

// stand a Lumen ward token in the foe's front row, exactly as onCreatureEnter would
const mkToken = win.eval('mkToken');
const tok = mkToken('foe', 'Lumen', 0, 2000, 'light');
api.cellArr('foe', 'front')[3] = tok;

const before = {
  hand: G.P.foe.hand.length,
  deck: G.P.foe.deck.length,
  onBoard: api.cellArr('foe', 'front')[3] && api.cellArr('foe', 'front')[3].nm,
};

const riptide = win.eval('SPELL_NEUTRAL').find((s) => s.effect === 'bounce');
const ok = win.eval('resolveSpell')({ ...riptide, type: 'spell' }, 'foeFront', 3);

const gained = G.P.foe.hand[G.P.foe.hand.length - 1];
console.log('bounce resolved:', ok);
console.log('before:', before);
console.log('after :', { hand: G.P.foe.hand.length, deck: G.P.foe.deck.length, cell: api.cellArr('foe', 'front')[3] });
console.log('card now in their hand:', gained && {
  nm: gained.nm, type: gained.type, cost: gained.c, atk: gained.a, hp: gained.h, token: gained.token,
});
console.log(gained && gained.nm === 'Lumen' && gained.token === undefined
  ? '\nBUG: the token is now an ordinary 0-cost creature card in hand — summonable every turn, forever.'
  : '\nno duplication (token flag survived)');
