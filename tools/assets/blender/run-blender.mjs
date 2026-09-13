#!/usr/bin/env node
/**
 * Run a Blender build script headlessly.
 *
 *   node tools/assets/blender/run-blender.mjs <script.py> [-- <script args>]
 *
 * bpy (Blender's Python API) has no Node binding, so every actual build
 * lives in a .py file Blender executes itself; this wrapper's only job is
 * finding blender.exe and running it the same way every time — matching
 * `convert-mixamo.mjs` / `bake-clips.mjs`'s "one deterministic script is the
 * only record of how the asset was made" convention, just for a tool that
 * happens to be a separate binary instead of an npm package.
 *
 * Pinned to Blender 4.2 LTS. A different installed version still runs (a
 * newer bpy API rarely breaks these scripts), but re-run and re-commit the
 * output if you're on one, and note the version in the asset's README.
 */
import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import path from 'node:path';

const PINNED = '4.2';

/** Where winget's per-machine MSI installs it; PATH is the fallback. */
function findBlender() {
  if (process.env.BLENDER_EXE && existsSync(process.env.BLENDER_EXE)) return process.env.BLENDER_EXE;
  if (process.platform === 'win32') {
    const candidates = [
      `C:\\Program Files\\Blender Foundation\\Blender ${PINNED}\\blender.exe`,
      'C:\\Program Files\\Blender Foundation\\Blender\\blender.exe',
    ];
    for (const c of candidates) if (existsSync(c)) return c;
  } else {
    for (const c of ['/usr/bin/blender', '/usr/local/bin/blender', '/opt/blender/blender']) {
      if (existsSync(c)) return c;
    }
  }
  return 'blender';   // rely on PATH; spawnSync will report ENOENT if that fails too
}

const argv = process.argv.slice(2);
const sep = argv.indexOf('--');
const script = sep === 0 || sep === -1 ? argv[0] : argv[0];
const scriptArgs = sep === -1 ? [] : argv.slice(sep + 1);
if (!script) throw new Error('usage: run-blender.mjs <script.py> [-- <script args>]');

const BLENDER = findBlender();
const scriptPath = path.resolve(script);
if (!existsSync(scriptPath)) throw new Error(`script not found: ${scriptPath}`);

console.log(`[run-blender] ${BLENDER}`);
const res = spawnSync(BLENDER, [
  '--background',       // no window, no GPU display context needed
  '--factory-startup',  // ignore the user's own Blender preferences/addons —
                         // deterministic output must not depend on this machine's config
  '--python', scriptPath,
  ...(scriptArgs.length ? ['--', ...scriptArgs] : []),
], { stdio: 'inherit' });

if (res.error) {
  if (res.error.code === 'ENOENT') {
    console.error(`Blender not found. Install the pinned ${PINNED} LTS build:\n`
      + `  winget install --id BlenderFoundation.Blender.LTS.4.2 --version 4.2.16\n`
      + `or set BLENDER_EXE to an existing install.`);
  }
  throw res.error;
}
process.exit(res.status ?? 1);
