// M12 differential harness — step 1: can the living JS game boot headlessly at all?
//
// Everything else in the harness (the command adapter, the state dump, the fuzz tier) is built on
// top of this, so it is worth proving on its own before any of it exists. The old .srdtest harness
// booted the MONOLITH; this boots the modular index.html + the 29 ordered scripts, which is a
// different problem: load order matters, and several files reach for browser APIs at parse time.
//
// Usage:  node tools/diffjs/boot.mjs [--verbose]
// Exit 0 = the game booted and its rules globals are reachable.

import { readFileSync, existsSync } from 'node:fs';
import { dirname, resolve, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { JSDOM, VirtualConsole } from 'jsdom';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const VERBOSE = process.argv.includes('--verbose');

/** The browser surface the game touches that jsdom does not provide. */
function installStubs(win) {
  const noop = () => {};
  const stubCanvasCtx = () => ({
    fillRect: noop, clearRect: noop, drawImage: noop, beginPath: noop, arc: noop, fill: noop,
    stroke: noop, moveTo: noop, lineTo: noop, closePath: noop, save: noop, restore: noop,
    translate: noop, rotate: noop, scale: noop, setTransform: noop, createLinearGradient: () => ({
      addColorStop: noop,
    }),
    createRadialGradient: () => ({ addColorStop: noop }),
    measureText: () => ({ width: 0 }), fillText: noop, strokeText: noop,
    putImageData: noop, getImageData: () => ({ data: new Uint8ClampedArray(4) }),
    createImageData: () => ({ data: new Uint8ClampedArray(4) }),
    canvas: null, globalAlpha: 1, globalCompositeOperation: 'source-over',
  });

  win.HTMLCanvasElement.prototype.getContext = function () { return stubCanvasCtx(); };
  win.HTMLCanvasElement.prototype.toDataURL = () => 'data:image/png;base64,';

  // Audio: the SFX layer builds an AudioContext graph at load.
  const audioNode = () => ({
    connect: noop, disconnect: noop, start: noop, stop: noop,
    gain: { value: 1, setValueAtTime: noop, exponentialRampToValueAtTime: noop, linearRampToValueAtTime: noop },
    frequency: { value: 440, setValueAtTime: noop, exponentialRampToValueAtTime: noop, linearRampToValueAtTime: noop },
    type: 'sine', Q: { value: 1 }, buffer: null, loop: false, playbackRate: { value: 1 },
  });
  class FakeAudioContext {
    constructor() { this.currentTime = 0; this.destination = audioNode(); this.sampleRate = 48000; this.state = 'running'; }
    createOscillator() { return audioNode(); }
    createGain() { return audioNode(); }
    createBiquadFilter() { return audioNode(); }
    createBufferSource() { return audioNode(); }
    createBuffer() { return { getChannelData: () => new Float32Array(1) }; }
    createDynamicsCompressor() { return audioNode(); }
    createStereoPanner() { return audioNode(); }
    createConvolver() { return audioNode(); }
    createWaveShaper() { return audioNode(); }
    resume() { return Promise.resolve(); }
    close() { return Promise.resolve(); }
  }
  win.AudioContext = FakeAudioContext;
  win.webkitAudioContext = FakeAudioContext;

  if (!win.matchMedia) {
    win.matchMedia = (q) => ({
      matches: false, media: q, addListener: noop, removeListener: noop,
      addEventListener: noop, removeEventListener: noop, onchange: null, dispatchEvent: () => false,
    });
  }
  win.requestAnimationFrame = (cb) => win.setTimeout(() => cb(0), 0);
  win.cancelAnimationFrame = (id) => win.clearTimeout(id);
  win.scrollTo = noop;
  if (!win.navigator.serviceWorker) {
    Object.defineProperty(win.navigator, 'serviceWorker', {
      value: { register: () => Promise.resolve({}) }, configurable: true,
    });
  }
  // Element geometry: the layout code reads rects constantly and jsdom returns zeros.
  win.Element.prototype.getBoundingClientRect = function () {
    return { x: 0, y: 0, width: 100, height: 100, top: 0, left: 0, right: 100, bottom: 100, toJSON: noop };
  };
  win.HTMLElement.prototype.scrollIntoView = noop;
}

export async function bootGame({ verbose = false } = {}) {
  const htmlPath = join(ROOT, 'index.html');
  if (!existsSync(htmlPath)) throw new Error('index.html not found at ' + htmlPath);

  // Load the page WITHOUT letting jsdom fetch the scripts itself: we inject them in order so a
  // failure names the exact file, instead of surfacing as a silent no-op much later.
  const html = readFileSync(htmlPath, 'utf8');
  const scripts = [...html.matchAll(/<script src="([^"]+)"><\/script>/g)].map((m) => m[1]);
  const stripped = html.replace(/<script src="[^"]+"><\/script>/g, '');

  const problems = [];
  const virtualConsole = new VirtualConsole();
  virtualConsole.on('jsdomError', (e) => problems.push('jsdomError: ' + e.message));
  if (verbose) virtualConsole.on('error', (...a) => problems.push('console.error: ' + a.join(' ')));

  const dom = new JSDOM(stripped, {
    runScripts: 'dangerously',
    pretendToBeVisual: true,
    url: 'http://localhost/',
    virtualConsole,
  });
  const win = dom.window;
  installStubs(win);

  // Inject REAL <script> elements rather than win.eval(). The game declares almost everything
  // with top-level `const` (ELEMENTS, COLORS, ROWS, SLOTS, G...), and a const at the top level of
  // an eval is scoped to that eval and gone the moment it returns - so eval'ing the files in order
  // produces 31 scripts that each see none of the previous ones' definitions. Script elements
  // create genuine global lexical bindings, exactly as the browser does.
  const loaded = [];
  for (const src of scripts) {
    const p = join(ROOT, src);
    if (!existsSync(p)) { problems.push('missing ' + src); continue; }
    const code = readFileSync(p, 'utf8');

    const before = problems.length;
    const el = win.document.createElement('script');
    el.textContent = code;
    try {
      win.document.head.appendChild(el);
    } catch (e) {
      problems.push(src + ' threw: ' + (e && e.message ? e.message : String(e)));
    }
    // jsdom reports a throwing script through the virtual console, not the appendChild call
    if (problems.length > before) {
      problems[problems.length - 1] = src + ' → ' + problems[problems.length - 1];
    } else {
      loaded.push(src);
    }
  }

  // The page fires DOMContentLoaded/load listeners that 99_boot.js hangs the entry point on.
  try {
    win.document.dispatchEvent(new win.Event('DOMContentLoaded', { bubbles: true }));
    win.dispatchEvent(new win.Event('load'));
  } catch (e) {
    problems.push('load event threw: ' + e.message);
  }

  return { dom, win, loaded, scripts, problems };
}

/** The rules globals the harness will drive. If these are missing, nothing else can work. */
const REQUIRED = [
  'G', 'ROWS', 'SLOTS', 'rowArr', 'cellArr', 'startGame', 'startTurn', 'doHarvest',
  'place', 'castSpell', 'flip', 'cleanup', 'checkWin', 'foeTurn', 'CMB', 'mkCre', 'mkBld',
];

if (import.meta.url === `file://${process.argv[1].replace(/\\/g, '/')}`
    || process.argv[1].endsWith('boot.mjs')) {
  const { win, loaded, scripts, problems } = await bootGame({ verbose: VERBOSE });

  // `const G = ...` is a global LEXICAL binding, not a property of window, so probe by evaluating
  // the name rather than by looking it up on the window object.
  const has = (k) => {
    try { return win.eval(`typeof ${k} !== 'undefined'`); } catch { return false; }
  };
  const missing = REQUIRED.filter((k) => !has(k));
  const present = REQUIRED.filter(has);

  console.log(`scripts: ${loaded.length}/${scripts.length} evaluated`);
  console.log(`globals: ${present.length}/${REQUIRED.length} reachable`);
  if (missing.length) console.log('  MISSING: ' + missing.join(', '));
  if (problems.length) {
    console.log(`problems (${problems.length}):`);
    for (const p of problems.slice(0, 25)) console.log('  · ' + p);
  }

  const ok = missing.length === 0 && loaded.length === scripts.length;
  console.log(ok ? 'BOOT OK — the JS oracle is drivable headlessly' : 'BOOT INCOMPLETE');
  process.exit(ok ? 0 : 1);
}
