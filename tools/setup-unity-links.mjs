#!/usr/bin/env node
// Creates the directory junction  unity/Assets/Game/Art/Cards -> <repo>/assets/cards
// so Unity imports the real card art in place (design 03 s6.3, option E). Idempotent.
// A junction needs no admin rights (unlike a symlink). Unity writes .meta files into the
// REAL assets/cards/ directory, so sprite GUIDs are committed and survive a fresh clone.
// The junction itself is git-ignored; unity/Assets/Game/Art/Cards.meta IS tracked.
import { execSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const target = path.join(root, 'assets', 'cards');
const linkParent = path.join(root, 'unity', 'Assets', 'Game', 'Art');
const link = path.join(linkParent, 'Cards');

if (!fs.existsSync(target)) {
  console.error(`target does not exist: ${target}`);
  process.exit(1);
}

fs.mkdirSync(linkParent, { recursive: true });

let status;
try {
  const st = fs.lstatSync(link);
  status = st.isSymbolicLink() || fs.statSync(link).isDirectory() ? 'exists' : 'blocked';
} catch {
  status = 'missing';
}

if (status === 'exists') {
  // verify it points where we think - a junction reads back through realpath
  const real = fs.realpathSync(link);
  if (path.resolve(real) === path.resolve(target)) {
    console.log(`ok: junction already in place (${link} -> ${target})`);
    process.exit(0);
  }
  console.error(`exists but points elsewhere: ${link} -> ${real}. Remove it and re-run.`);
  process.exit(1);
}

if (status === 'blocked') {
  console.error(`a non-directory is in the way at ${link}`);
  process.exit(1);
}

execSync(`cmd /c mklink /J "${link}" "${target}"`, { stdio: 'inherit' });
console.log(`created: ${link} -> ${target}`);
