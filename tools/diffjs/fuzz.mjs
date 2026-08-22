// M12 tier 3: fuzz the two engines against each other, and shrink whatever breaks.
//
// The scripted-AI traces (tier 1) prove the port plays ONE game correctly. They cannot prove much
// about the rest of the rules, because the AI never sets a card face-down, never pours into a
// charge, never sends banked mana, never moves a raider after declaring. Tier 3 replaces the AI
// with a player that picks uniformly among the LEGAL commands (FuzzPolicy.cs), which reaches all
// of that within a few hundred plies.
//
// The loop:
//   1. Unity records N fuzz traces (one batchmode run for all of them).
//   2. Each is replayed against the living JS, ply by ply, exactly as the goldens are.
//   3. A divergence is TRUNCATED at the divergent ply (free - the prefix is already a valid
//      trace), then delta-debugged down to a minimal command set.
//
// Shrinking is where the cost sits: every candidate has to be RE-RECORDED by the C# engine,
// because dropping a command changes everything after it. A Unity batchmode boot costs ~40s and a
// jsdom replay ~4s, so the whole round's candidates are re-recorded in ONE Unity run and then
// replayed one by one. That is why this is ddmin over rounds rather than a naive one-at-a-time
// bisection.
//
// Usage:
//   node tools/diffjs/fuzz.mjs                       # 6 traces x 400 plies
//   node tools/diffjs/fuzz.mjs --count 25 --plies 400
//   node tools/diffjs/fuzz.mjs --replay-only         # re-replay what is already on disk
//   node tools/diffjs/fuzz.mjs --selftest            # poison the engine; prove the shrinker works

import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { replayTrace } from './replay.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, '../..');
const OUT = join(HERE, 'fuzz');
const SHRINK = join(OUT, 'shrink');

const argv = process.argv.slice(2);
const flag = (name) => argv.includes('--' + name);
const opt = (name, fallback) => {
  const i = argv.indexOf('--' + name);
  return i >= 0 && argv[i + 1] ? argv[i + 1] : fallback;
};

const COUNT = +opt('count', flag('selftest') ? 1 : 6);
const PLIES = +opt('plies', flag('selftest') ? 120 : 400);
const SEED0 = +opt('seed0', 1);
const BUDGET = +opt('budget', 24);
const POISON = flag('selftest') ? 'manaOnThirdHarvest' : opt('poison', '');
const MAX_ROUNDS = +opt('rounds', 12);

/** One Unity batchmode run of a single test, with the harness environment set. */
function unity(filter, env) {
  const r = spawnSync('bash', [join(ROOT, 'tools/run-unity-tests.sh'), filter], {
    cwd: ROOT,
    env: { ...process.env, ...env },
    encoding: 'utf8',
  });
  const out = (r.stdout || '') + (r.stderr || '');
  if (r.status !== 0) {
    console.log(out.split('\n').slice(-12).join('\n'));
    throw new Error('unity run failed (' + filter + ') exit=' + r.status);
  }
  return out;
}

function generate() {
  mkdirSync(OUT, { recursive: true });
  console.log(`generating ${COUNT} fuzz traces x ${PLIES} plies`
    + (POISON ? ` (POISONED: ${POISON})` : '') + ' …');
  unity('SpawnRowDuel.Rules.Tests.FuzzTraceTests.GenerateFuzzTraces', {
    SRD_FUZZ_OUT: OUT,
    SRD_FUZZ_COUNT: String(COUNT),
    SRD_FUZZ_PLIES: String(PLIES),
    SRD_FUZZ_SEED0: String(SEED0),
    SRD_FUZZ_BUDGET: String(BUDGET),
    SRD_FUZZ_POISON: POISON,
  });
  return JSON.parse(readFileSync(join(OUT, 'index.json'), 'utf8')).traces;
}

/** A divergence, an adapter crash, and an unsupported command are all failures worth shrinking. */
function failed(r) {
  return r.reason === 'DIVERGED' || r.reason === 'threw' || r.reason === 'unsupported';
}

function describe(r) {
  if (r.reason === 'DIVERGED') return `DIVERGED at ply ${r.ply} (${r.stopped})`;
  if (r.reason === 'complete') return `all ${r.total} plies`;
  return `${r.reason} at ply ${r.matched + 1} (${r.stopped})`;
}

// ---- shrinking ---------------------------------------------------------------------------------

/**
 * Cut everything after the divergent ply. This costs nothing and is always safe: a prefix of a
 * valid trace is a valid trace, its recorded hashes are still the hashes the C# produced, and the
 * commands that come after the divergence cannot be part of the reason for it.
 */
function truncate(file, upToPly, into) {
  const trace = JSON.parse(readFileSync(file, 'utf8'));
  trace.plies = trace.plies.filter((p) => p.i <= upToPly);
  trace.plies_total = trace.plies.length;
  writeFileSync(into, JSON.stringify(trace, null, 0));
  return trace.plies.length;
}

/**
 * ddmin over the command list. Each round proposes "drop this chunk" candidates, the C# engine
 * re-records all of them in one Unity run, and the first candidate that still fails becomes the
 * new baseline. Granularity doubles when a round finds nothing, exactly as delta debugging
 * prescribes, so a stubborn dependency chain still narrows.
 */
async function shrink(file, label) {
  let current = file;
  let n = JSON.parse(readFileSync(current, 'utf8')).plies.length;
  let granularity = 2;

  for (let round = 0; round < MAX_ROUNDS && n > 1; round++) {
    const chunk = Math.ceil(n / granularity);
    const jobs = [];
    for (let start = 0; start < n; start += chunk) {
      const drop = [];
      for (let i = start; i < Math.min(start + chunk, n); i++) drop.push(i);
      jobs.push({ id: `r${round}c${jobs.length}`, drop });
    }
    if (jobs.length <= 1) break;

    const jobFile = join(SHRINK, `job-${round}.json`);
    writeFileSync(jobFile, JSON.stringify({
      trace: current, out: SHRINK, poison: POISON || null, jobs,
    }));

    console.log(`  round ${round}: ${n} plies, ${jobs.length} candidates …`);
    unity('SpawnRowDuel.Rules.Tests.FuzzTraceTests.RunShrinkJobs', { SRD_FUZZ_JOB: jobFile });

    let progressed = false;
    for (const job of jobs) {
      const candidate = join(SHRINK, job.id + '.json');
      if (!existsSync(candidate)) continue;
      const r = await replayTrace(candidate);
      if (!failed(r)) continue;

      const kept = join(SHRINK, `${label}-min.json`);
      const plies = r.reason === 'DIVERGED'
        ? truncate(candidate, r.ply, kept)
        : truncate(candidate, r.matched + 1, kept);

      console.log(`    kept ${job.id}: ${plies} plies — ${describe(r)}`);
      current = kept;
      n = plies;
      progressed = true;
      break;
    }

    if (!progressed) {
      if (granularity >= n) break;
      granularity = Math.min(granularity * 2, n);
    } else {
      granularity = Math.max(granularity - 1, 2);
    }
  }

  return { file: current, plies: n };
}

// ---- the run -----------------------------------------------------------------------------------

const traces = flag('replay-only')
  ? JSON.parse(readFileSync(join(OUT, 'index.json'), 'utf8')).traces
  : generate();

let failures = 0;
let commands = 0;

for (const t of traces) {
  const file = join(OUT, t.name + '.json');
  const r = await replayTrace(file);
  commands += r.matched;

  if (!failed(r)) {
    const note = r.substituted ? ` (${r.substituted} substituted declaration(s) dropped)` : '';
    console.log(`✓ ${t.name}: ${describe(r)}${note}`);
    continue;
  }

  failures++;
  console.log(`✗ ${t.name}: ${describe(r)} — shrinking`);

  rmSync(SHRINK, { recursive: true, force: true });   // one shrink run per failure, from clean
  mkdirSync(SHRINK, { recursive: true });
  const seed = join(SHRINK, `${t.name}-seed.json`);
  const before = truncate(file, r.reason === 'DIVERGED' ? r.ply : r.matched + 1, seed);
  console.log(`  truncated to ${before} plies`);

  const min = await shrink(seed, t.name);
  console.log(`  minimal reproducer: ${min.file} (${min.plies} plies)`);
  for (const p of JSON.parse(readFileSync(min.file, 'utf8')).plies)
    console.log(`    ${p.i} ${JSON.stringify(p.cmd)}`);
}

console.log(`\n${traces.length} traces, ${commands} commands compared, ${failures} divergent`);
if (flag('selftest')) {
  if (failures === 0) {
    console.log('SELFTEST FAILED: the poisoned engine replayed clean, so the harness is blind');
    process.exit(1);
  }
  console.log('selftest: the poison was caught and shrunk');
}
process.exit(failures && !flag('selftest') ? 1 : 0);
