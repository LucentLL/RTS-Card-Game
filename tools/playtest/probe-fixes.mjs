// One assertion per bug the playtest turned up. Run it after touching the rules:
//   node tools/playtest/probe-fixes.mjs      (exit 0 = every fix still holds)
import { newRealm } from './engine.mjs';

let pass = 0, fail = 0;
const check = (name, ok, detail) => {
  if (ok) { pass++; console.log(`  ok    ${name}`); }
  else { fail++; console.log(`  FAIL  ${name}\n        ${detail}`); }
};

const realm = await newRealm({});
const { win, G, api } = realm;
const ev = (n) => win.eval(n);
const fresh = (you = 'fire', foe = 'water') => {
  realm.setSeed(7);
  Object.assign(G, { build: null, sel: null, atk: [], decls: [], moveFrom: null, moveMana: null, cardMenu: null, busy: false, over: false });
  api.startGame(you, foe, undefined, undefined);
};

// ── 1. a bounced token must not become a permanent card ────────────────────────────────────────
fresh('water', 'light');
{
  const tok = ev('mkToken')('foe', 'Lumen', 0, 2000, 'light');
  api.cellArr('foe', 'front')[3] = tok;
  const riptide = { ...ev('SPELL_NEUTRAL').find((s) => s.effect === 'bounce'), type: 'spell' };
  const handBefore = G.P.foe.hand.length;
  const resolved = ev('resolveSpell')(riptide, 'foeFront', 3);
  check('bounce refuses a token target',
    resolved === false && G.P.foe.hand.length === handBefore && api.cellArr('foe', 'front')[3] === tok,
    `resolved=${resolved} hand ${handBefore}→${G.P.foe.hand.length}`);
  check('the targeting filter hides tokens from Riptide',
    ev('validSpellTarget')(riptide, tok) === false && ev('validSpellTarget')(riptide, api.rowArr('foeFront')[3]) === false,
    'validSpellTarget still offers the token');
  // and a real creature is still a legal target
  const real = ev('mkCre')({ nm: 'Rippler', a: 1000, h: 1000, c: 2, up: 1 }, 'foe', false);
  api.cellArr('foe', 'front')[1] = real;
  check('bounce still works on a printed creature',
    ev('validSpellTarget')(riptide, real) === true && ev('resolveSpell')(riptide, 'foeFront', 1) === true
      && G.P.foe.hand.length === handBefore + 1,
    'a normal creature no longer bounces');
}

// ── 2. build caps must see a base through its upgrades ─────────────────────────────────────────
fresh();
{
  const fam = ev('bidFamily');
  check('bidFamily walks forward through up2',
    fam('encampment', null).join(',') === 'encampment,longhouse,barracks'
    && fam('outpost', null).join(',') === 'outpost,tower,bastion'
    && fam('foundry', null).join(',') === 'foundry,keep,citadel',
    `encampment→${fam('encampment', null)} outpost→${fam('outpost', null)}`);

  G.P.foe.mana = 99;
  api.cellArr('foe', 'back')[0] = ev('mkBld')(api.STRUCT_DEFS.foundry, 'foe');
  api.cellArr('foe', 'front')[0] = ev('mkBld')(api.STRUCT_DEFS.encampment, 'foe');
  api.syncWorkers('foe');
  ev('applyUpgrade')(api.cellArr('foe', 'front')[0], api.resolveStruct('longhouse', null));
  api.syncWorkers('foe');
  const camps = () => ev('ownBuildings')('foe').filter((b) => ['encampment', 'longhouse', 'barracks'].includes(b.bid)).length;
  const before = camps();
  for (let i = 0; i < 6; i++) { G.P.foe.mana = 99; if (!ev('aiBuild')('foe')) break; }
  check('the AI stops rebuilding a camp it already upgraded',
    camps() === before, `camp-family structures ${before} → ${camps()} after six build attempts`);
}

// ── 3. Overcharge on the ×500 stat scale ───────────────────────────────────────────────────────
fresh('electric', 'fire');
{
  const volt = ev('mkCre')({ nm: 'Volt', a: 1000, h: 1000, c: 2, up: 1, kw: 'overcharge' }, 'you', false);
  api.cellArr('you', 'front')[3] = volt;
  ev('overchargeUpkeep')('you'); ev('overchargeUpkeep')('you'); ev('overchargeUpkeep')('you');
  const banked = volt.oc;
  ev('dischargeOvercharge')([volt]);
  check('a full Overcharge discharge is worth a creature-tier blow',
    banked === 3 && api.effA(volt) === 1000 + 1500,
    `banked ${banked}, effA ${api.effA(volt)} (was 1000+3 before the fix)`);
  ev('clearDischarge')([volt]);
}

// ── 4. the AI directs its own counterstrike ────────────────────────────────────────────────────
fresh();
{
  const idx = ev('aiRetaliationIndex');
  const T = { a: 1500, h: 2000 };
  const grp = [{ nm: 'tough', a: 500, h: 3000 }, { nm: 'killable', a: 2000, h: 1000 }];
  check('a jointly-attacked creature strikes the attacker it can kill',
    idx(T, grp) === 1, `picked index ${idx(T, grp)} instead of the killable attacker`);
  check('with nothing killable it strikes the hardest hitter',
    idx({ a: 100, h: 900 }, grp) === 1, `picked ${idx({ a: 100, h: 900 }, grp)}`);
}

// ── 5. a row-gated structure cannot be built off its row ───────────────────────────────────────
fresh();
{
  const lh = api.STRUCT_DEFS.longhouse;
  check('Longhouse is refused in the back row at BUILD time',
    api.placeRowOK('you', 'back', lh) === false && api.placeRowOK('you', 'front', lh) === true,
    `back=${api.placeRowOK('you', 'back', lh)} front=${api.placeRowOK('you', 'front', lh)}`);
  check('an ungated structure is unaffected',
    api.placeRowOK('you', 'back', api.STRUCT_DEFS.foundry) === true, 'the Foundry lost its back row');
}

// ── 6. eff:'wall' — the card no longer claims something the engine does not do ────────────────
fresh();
{
  const bulwark = ev('mkBld')(api.STRUCT_DEFS.bulwark, 'foe');
  api.cellArr('foe', 'front')[2] = bulwark;
  const elig = api.eligibleInterceptors('you', api.rowIdx('youFront'), api.rowIdx('foeBack'));
  check('a Bulwark does not interpose (measured: enabling it stalls games)',
    !elig.some((r) => r.c === bulwark), 'a wall is being offered as an interceptor again');
  const says = (t) => /intercept|screens the line/i.test(t);
  check('no card text promises interception any more',
    !says(api.STRUCT_DEFS.bulwark.desc) && !says(api.STRUCT_DEFS.bastion.desc)
    && !says(ev('bldEffectText')('wall', 0, 1)),
    `bulwark: "${api.STRUCT_DEFS.bulwark.desc}"`);
  check('a structure still carries a:0 so combat maths can never go NaN',
    bulwark.a === 0 && bulwark.blocked === false, `a=${bulwark.a} blocked=${bulwark.blocked}`);
}

console.log(`
${pass} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
