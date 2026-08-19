#!/usr/bin/env node
/* =============================================================================
   export_cards.mjs — DYNAMIC export of the Spawn Row Duel card registry.

   The game's card data lives in classic <script> files that share one global
   scope (src/js/01_core_defs.js .. 06_mana_workers.js). Rather than re-parsing
   them, this tool CONCATENATES the registry-bearing files and EVALUATES them in
   a node:vm sandbox with a minimal DOM stub, then reads the resulting globals
   (ELEMENTS / POOLS / DIVINE / SPELL_NEUTRAL / STRUCT_DEFS / CCS / CARD_REG /
   forgeDef / grandForgeDef / WORKER / art-path helpers) and serialises
   everything to docs/unity/spec/cards.json.

   Because it runs the real code, derived values (dual-commander HP/workers,
   forge names + descriptions per element, the CARD_REG keys, the art URL probe
   order) are EXACTLY what the game computes — no transcription drift.

   Usage:  node tools/export_cards.mjs [--out <path>] [--no-art]
             --no-art   omit the inline placeholder-SVG data URIs (much smaller)
   ============================================================================= */
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');

const argv = process.argv.slice(2);
const OUT = (() => { const i = argv.indexOf('--out'); return i >= 0 ? argv[i + 1] : path.join(ROOT, 'docs', 'unity', 'spec', 'cards.json'); })();
const KEEP_ART = !argv.includes('--no-art');

/* ---- the registry-bearing scripts, in index.html load order ---------------- */
const FILES = [
  'src/js/01_core_defs.js',      // ELEMENTS, MAJORS, COLORS, clsOf, SLOTS, CENTER_LANES, uid
  'src/js/02_art.js',            // A_BG, phArt, ccArt, ART (placeholder SVG data URIs)
  'src/js/03_cards_creatures.js',// POOLS, DIVINE, FORGE_NAMES, WORKER, TRIBES, SUBTYPES, STRUCT_DEFS, forgeDef, grandForgeDef, SPELL_NEUTRAL
  'src/js/04_cards_leaders.js',  // CCS, CC_ART, slugify/artURLs/fieldURLs/PLACEHOLDERS, G
  'src/js/06_mana_workers.js',   // CARD_REG, CARD_BY_KEY, DECK_SIZE, MAX_COPIES, kwText, deckOf
];

/* ---- minimal DOM / browser stub ------------------------------------------- */
function makeElement(tag) {
  const el = {
    tagName: (tag || 'div').toUpperCase(),
    id: '', className: '', textContent: '', innerHTML: '', value: '', title: '',
    dataset: {}, children: [],
    style: { setProperty() {}, removeProperty() {}, getPropertyValue() { return ''; } },
    classList: { add() {}, remove() {}, toggle() {}, contains() { return false; } },
    appendChild(c) { this.children.push(c); return c; },
    insertBefore(c) { this.children.push(c); return c; },
    prepend() {}, after() {}, remove() {},
    addEventListener() {}, removeEventListener() {},
    setAttribute() {}, getAttribute() { return null; }, removeAttribute() {},
    querySelector() { return makeElement('div'); },
    querySelectorAll() { return []; },
    closest() { return null; },
    getBoundingClientRect() { return { left: 0, top: 0, right: 0, bottom: 0, width: 0, height: 0 }; },
    offsetWidth: 0, offsetHeight: 0, firstElementChild: null, nextElementSibling: null,
  };
  return el;
}
const documentStub = {
  documentElement: makeElement('html'),
  head: makeElement('head'),
  body: makeElement('body'),
  createElement: (t) => makeElement(t),
  getElementById: () => makeElement('div'),
  querySelector: () => makeElement('div'),
  querySelectorAll: () => [],
  addEventListener() {}, removeEventListener() {},
};
class ImageStub { constructor() { this.onload = null; this.onerror = null; this._src = ''; }
  set src(v) { this._src = v; /* never resolves: probeSleeves stays silent */ }
  get src() { return this._src; } }

const sandbox = {
  console,
  Math, Date, JSON, Object, Array, String, Number, Boolean, Set, Map, Promise, RegExp, Error,
  parseInt, parseFloat, isNaN, encodeURIComponent, decodeURIComponent, setTimeout, clearTimeout,
  setInterval, clearInterval,
  document: documentStub,
  Image: ImageStub,
  localStorage: { getItem: () => null, setItem() {}, removeItem() {} },
  matchMedia: () => ({ matches: false, addListener() {}, addEventListener() {} }),
  performance: { now: () => 0 },
  navigator: { userAgent: 'node' },
};
sandbox.window = sandbox;
sandbox.globalThis = sandbox;
vm.createContext(sandbox);

/* ---- load + evaluate ------------------------------------------------------ */
/* Top-level `const` in a classic script lives in the GLOBAL LEXICAL scope, not on `window`.
   In the browser every later script still sees it; inside node:vm it is likewise visible to the
   rest of the same Script but is NOT a property of the sandbox object. So we append an epilogue
   (part of the same Script, hence in scope) that publishes the bindings we need onto `window`. */
const EXPORT_NAMES = [
  'ELEMENTS', 'MAJORS', 'COLORS', 'clsOf', 'SLOTS', 'C', 'CENTER_LANES', 'BASE_COL',
  'ART', 'POOLS', 'DIVINE', 'FORGE_NAMES', 'WORKER', 'TRIBES', 'SUBTYPES',
  'STRUCT_DEFS', 'SPELL_NEUTRAL', 'SPELL', 'forgeDef', 'grandForgeDef', 'buildList',
  'CCS', 'CC_ART', 'DUAL_LORE', 'CARD_REG', 'CARD_BY_KEY', 'SPELL_NAMES',
  'DECK_SIZE', 'MAX_COPIES', 'MAX_DECKS', 'DECKS_KEY', 'COLOR_ALIAS', 'CC_ALIAS',
  'kwText', 'typeLine', 'deckOf', 'G',
];
const epilogue = 'window.__SRD__ = {' + EXPORT_NAMES.map(n => `${n}: (typeof ${n}!=='undefined')?${n}:undefined`).join(',') + '};';

const src = FILES.map(f => {
  const p = path.join(ROOT, f);
  if (!fs.existsSync(p)) throw new Error('missing source file: ' + f);
  return `/* ===== ${f} ===== */\n` + fs.readFileSync(p, 'utf8');
}).join('\n;\n') + '\n;\n/* ===== exporter epilogue ===== */\n' + epilogue;

try {
  vm.runInContext(src, sandbox, { filename: 'srd-registry-bundle.js' });
} catch (e) {
  console.error('FAILED to evaluate the registry bundle:\n', e);
  process.exit(1);
}

/* merge the lexical bindings (published by the epilogue) over the window-assigned ones
   (slugify / artURLs / fieldURLs / spriteBase / PLACEHOLDERS / ART_DIR ... live on `window`) */
const S = Object.assign({}, sandbox, sandbox.__SRD__ || {});
const need = ['ELEMENTS', 'COLORS', 'MAJORS', 'POOLS', 'DIVINE', 'SPELL_NEUTRAL', 'STRUCT_DEFS',
  'CCS', 'CARD_REG', 'FORGE_NAMES', 'WORKER', 'TRIBES', 'SUBTYPES', 'DECK_SIZE', 'MAX_COPIES',
  'slugify', 'artURLs', 'fieldURLs', 'spriteBase', 'ART_DIR', 'ART_EXTS'];
for (const k of need) if (S[k] === undefined) { console.error('missing global after eval: ' + k); process.exit(1); }

/* ---- helpers -------------------------------------------------------------- */
const artInfo = (nm) => ({
  slug: S.slugify(nm),
  cardArtUrls: S.artURLs(nm),
  fieldArtUrls: S.fieldURLs(nm),
  spriteBase: S.spriteBase(nm),
});
function keepArt(v) { return KEEP_ART ? (v ?? null) : (v ? '<placeholder-svg omitted>' : null); }

/* every field a creature template can carry, in a stable order + explicit nulls */
function creatureCard(t, element, poolIndex) {
  return {
    key: element + '|' + t.nm,
    registryKey: (t.color || element || 'neutral') + '|' + t.nm,
    type: 'creature',
    element,                              // POOLS bucket (== deck color for these)
    poolIndex,
    nm: t.nm,
    c: t.c ?? null,                       // mana cost
    a: t.a ?? null,                       // attack
    h: t.h ?? null,                       // hit points (== max HP on the template)
    up: t.up ?? 0,                        // worker upkeep (⚒ drained from its row)
    fs: !!t.fs,                           // First Strike
    kw: t.kw ?? null,                     // element keyword id
    det: t.det ?? null,                   // Detonate damage
    reap: t.reap ?? null,                 // Reap token stats (a/a)
    wardhp: t.wardhp ?? null,             // Ward token HP
    ward: t.ward ?? null,                 // (vestigial: no template sets it)
    grow: t.grow ?? null,                 // Chrysalis counters per upkeep
    hatch: t.hatch ?? null,               // Chrysalis counters needed
    into: t.into ? { ...t.into } : null,  // Chrysalis hatch form
    entrench: !!t.entrench,               // Entrench flag (mirrors kw:'entrench')
    tribe: t.tribe ?? null,
    subtype: t.subtype ?? null,
    token: !!t.token,
    art: keepArt(t.art),
    ...artInfo(t.nm),
  };
}
function spellCard(t, i) {
  return {
    key: 'neutral|' + t.nm,
    registryKey: 'neutral|' + t.nm,
    type: 'spell',
    element: null,
    poolIndex: i,
    nm: t.nm,
    c: t.c ?? null,
    trap: !!t.trap,
    effect: t.effect ?? null,             // burn | raze | chain | bounce | pitfall | thornmail
    val: t.val ?? null,                   // damage / magnitude (null when the effect has none)
    target: t.target ?? null,             // DECLARATIVE ONLY — never read by the rules code
    trigger: t.trigger ?? null,           // traps only: 'summon' | 'attack'
    ic: t.ic ?? null,
    art: keepArt(t.art),
    ...artInfo(t.nm),
  };
}
function structCard(def, key, extra) {
  return {
    key: key,
    type: 'structure',
    bid: def.bid,
    nm: def.nm,
    c: def.c ?? null,
    h: def.h ?? null,
    eff: def.eff ?? null,                 // mana|villager|damage|wall|vault|revive|none|command
    val: def.val ?? 0,
    sup: def.sup ?? 0,                    // worker support (negative = costs workers)
    ic: def.ic ?? null,
    prereq: (def.prereq || []).slice(),
    from: def.from ?? null,               // upgrade-only tier: base it is reached from
    up2: (def.up2 || []).slice(),         // in-place upgrade targets
    row: def.row ?? null,                 // row gate: 'front' | 'back' | null
    color: def.color ?? null,
    desc: def.desc ?? null,
    buildable: !def.from,
    ...(extra || {}),
    art: keepArt(def.art),
    ...artInfo(def.nm),
  };
}

/* ---- assemble ------------------------------------------------------------- */
const elements = Object.keys(S.ELEMENTS).map(id => {
  const E = S.ELEMENTS[id];
  return { id, name: E.name, glyph: E.glyph, color: E.color, accent: E.accent, deep: E.deep,
    bg: E.bg.slice(), hp: E.hp, wk: E.wk, lore: E.lore,
    deckable: S.MAJORS.includes(id), cssClass: S.clsOf[id] };
});

const creatures = [];
for (const el of S.COLORS) (S.POOLS[el] || []).forEach((t, i) => creatures.push(creatureCard(t, el, i)));
const divine = (S.DIVINE || []).map((t, i) => creatureCard(t, 'divine', i));
const spells = (S.SPELL_NEUTRAL || []).map(spellCard);

const structures = Object.keys(S.STRUCT_DEFS).map(k => structCard(S.STRUCT_DEFS[k], k));
const forges = [];
for (const el of S.COLORS) {
  forges.push(structCard(S.forgeDef(el), 'forge:' + el, { generatedBy: 'forgeDef', element: el }));
  forges.push(structCard(S.grandForgeDef(el), 'grandforge:' + el, { generatedBy: 'grandForgeDef', element: el }));
}
forges.push(structCard(S.forgeDef('divine'), 'forge:divine', { generatedBy: 'forgeDef', element: 'divine', note: 'not reachable from any commander build list (divine is not a deckable element)' }));
forges.push(structCard(S.grandForgeDef('divine'), 'grandforge:divine', { generatedBy: 'grandForgeDef', element: 'divine', note: 'not reachable from any commander build list' }));

const commanders = Object.keys(S.CCS).map(id => {
  const c = S.CCS[id];
  return { id, name: c.name, hp: c.hp, wk: c.wk, colors: c.colors.slice(), desc: c.desc,
    dual: c.colors.length === 2, buildList: S.buildList(id).map(d => d.bid + (d.color ? ':' + d.color : '')) };
});

const worker = { key: 'token|Worker', type: 'worker-token', nm: S.WORKER.nm, c: S.WORKER.c,
  a: S.WORKER.a, h: S.WORKER.h, art: keepArt(S.WORKER.art), ...artInfo(S.WORKER.nm) };

const tokens = [
  { key: 'token|Lumen', type: 'token', nm: 'Lumen', a: 0, hFrom: 'wardhp of the Ward creature (default 2 if absent)',
    createdBy: 'keyword ward, on creature ENTER', sick: true, note: 'placed in the first empty cell: own back row, then front row, then a free centre LANE' },
  { key: 'token|Shade', type: 'token', nm: 'Shade', aFrom: 'reap value (default 1)', hFrom: 'reap value (default 1)',
    createdBy: 'keyword reap, on creature DEATH', sick: true, note: 'same first-empty-cell placement rule' },
];

const keywords = Object.keys({ detonate: 1, undertow: 1, entrench: 1, ward: 1, reap: 1, chrysalis: 1, scour: 1, overcharge: 1 })
  .map(k => ({ id: k, inspectText: S.kwText({ kw: k, det: 0, reap: 0, wardhp: 0, cnt: 0, hatch: 0, grow: 0, into: {} }) }));

/* counts */
const byElement = {};
for (const el of S.COLORS) byElement[el] = (S.POOLS[el] || []).length;
const counts = {
  elements: elements.length,
  deckableElements: S.MAJORS.length,
  commanders: commanders.length,
  commandersSolo: commanders.filter(c => !c.dual).length,
  commandersDual: commanders.filter(c => c.dual).length,
  creatures: creatures.length,
  creaturesByElement: byElement,
  divineCreatures: divine.length,
  spellsTotal: spells.length,
  spellsNonTrap: spells.filter(s => !s.trap).length,
  traps: spells.filter(s => s.trap).length,
  structuresStaticDefs: structures.length,
  structuresGeneratedForges: forges.length,
  deckRegistryEntries: S.CARD_REG.length,
  tokens: tokens.length + 1,
};

const out = {
  $schemaNote: 'Spawn Row Duel card registry, exported by tools/export_cards.mjs from the live JS registry (node:vm sandbox). Every value here is what the game computes at runtime.',
  generatedAt: new Date().toISOString(),
  sourceFiles: FILES,
  artIncluded: KEEP_ART,
  rules: {
    DECK_SIZE: S.DECK_SIZE,
    MAX_COPIES: S.MAX_COPIES,
    MAX_DECKS: S.MAX_DECKS,
    DECKS_KEY: S.DECKS_KEY,
    SLOTS: S.SLOTS,
    CENTER_LANES: S.CENTER_LANES.slice(),
    BASE_COL: S.BASE_COL,
    ART_DIR: S.ART_DIR,
    ART_EXTS: S.ART_EXTS.slice(),
    FIELD_EXTS: S.FIELD_EXTS.slice(),
    SPRITE_DIR: S.SPRITE_DIR,
    SPRITE_EXTS: S.SPRITE_EXTS.slice(),
    TRIBES: S.TRIBES.slice(),
    SUBTYPES: S.SUBTYPES.slice(),
    FORGE_NAMES: { ...S.FORGE_NAMES },
    COLOR_ALIAS: { ...S.COLOR_ALIAS },
    CC_ALIAS: { ...S.CC_ALIAS },
  },
  counts,
  elements,
  keywords,
  commanders,
  creatures,
  divine,
  spells,
  structures,
  forges,
  worker,
  tokens,
  deckRegistry: S.CARD_REG.map(e => ({ key: e.key, type: e.type, color: e.color, nm: e.nm })),
};

fs.mkdirSync(path.dirname(OUT), { recursive: true });
fs.writeFileSync(OUT, JSON.stringify(out, null, 2), 'utf8');

const size = fs.statSync(OUT).size;
console.log('wrote ' + OUT + ' (' + (size / 1024).toFixed(1) + ' KB)');
console.log(JSON.stringify(counts, null, 2));
