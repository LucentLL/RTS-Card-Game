// M12 tier 1, first slice: does the JS deal the SAME opening as the C# engine, given the same
// decks and the same commanders?
//
// This is deliberately the smallest end-to-end differential that exercises the whole pipeline -
// deck injection, the JS boot, the shared projection, the diff - before a large command adapter
// is written on top of an unproven foundation. If the opening diverges, nothing downstream can be
// trusted; if it matches, the scaffolding is sound and the adapter has somewhere solid to stand.
//
// Usage:  node tools/diffjs/compare-open.mjs [golden.json ...]
// Exit 0 = every trace's opening matches.

import { readFileSync, readdirSync } from 'node:fs';
import { dirname, resolve, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { bootGame } from './boot.mjs';
import { projectJs, canonical, firstDiff } from './project.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const GOLDEN = join(HERE, 'golden');

/** Rebuild a JS deck from the trace's "color|name" registry keys. */
function deckFromKeys(win, keys, problems) {
  const byKey = win.eval('CARD_BY_KEY');
  const out = [];
  for (const key of keys) {
    const e = byKey[key];
    if (!e) { problems.push('unknown deck key: ' + key); continue; }
    out.push({ type: e.type, color: e.color, ...e.tpl });
  }
  return out;
}

async function compareTrace(file) {
  const trace = JSON.parse(readFileSync(file, 'utf8'));
  const label = file.replace(/\\/g, '/').split('/').pop();

  const { win, problems } = await bootGame();
  if (problems.length) {
    console.log(`  ! boot problems: ${problems.slice(0, 3).join(' | ')}`);
  }

  const youDeck = deckFromKeys(win, trace.youDeck, problems);
  const foeDeck = deckFromKeys(win, trace.foeDeck, problems);
  if (youDeck.length !== trace.youDeck.length || foeDeck.length !== trace.foeDeck.length) {
    console.log(`✗ ${label}: could not rebuild the decks (${problems.slice(0, 4).join(' | ')})`);
    return false;
  }

  // startGame(youId, foeId, youDeck, foeDeck) - the JS already supports deck injection, which is
  // what lets the harness sidestep the unreconcilable shuffles (DECISIONS D16).
  win.startGameInjected = () => {
    win.eval('startGame')(trace.you, trace.foe, youDeck, foeDeck);
  };
  try {
    win.startGameInjected();
  } catch (e) {
    console.log(`✗ ${label}: startGame threw — ${e.message}`);
    return false;
  }

  const mine = projectJs(win);
  const theirs = trace.openProjection;

  if (canonical(mine) === canonical(theirs)) {
    console.log(`✓ ${label}: opening matches (${trace.you} vs ${trace.foe})`);
    return true;
  }

  const d = firstDiff(theirs, mine);
  console.log(`✗ ${label}: opening diverges at ${d.path}`);
  console.log(`    C#: ${JSON.stringify(d.a)}`);
  console.log(`    JS: ${JSON.stringify(d.b)}`);
  return false;
}

const args = process.argv.slice(2);
const files = args.length
  ? args.map((a) => resolve(a))
  : readdirSync(GOLDEN).filter((f) => f.endsWith('.json')).map((f) => join(GOLDEN, f));

if (!files.length) {
  console.log('no golden traces found — run the Unity test gate first to generate them');
  process.exit(1);
}

let ok = true;
for (const f of files) ok = (await compareTrace(f)) && ok;
console.log(ok ? 'OPENING PARITY OK' : 'OPENING PARITY FAILED');
process.exit(ok ? 0 : 1);
