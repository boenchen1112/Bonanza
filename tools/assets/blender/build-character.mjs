#!/usr/bin/env node
/**
 * Build designed cast members (docs/design/cast-sheet.html) with
 * build-character.py, one fresh Blender process per character.
 *
 * Character data and colour roles come from web/src/shell/castData.js — the
 * same definitions the menus use — so a build can't drift from the portraits.
 *
 *   node tools/assets/blender/build-character.mjs tuff [zizz ...]
 *
 * Writes tools/assets/blender/_char-<id>.glb (gitignored scratch output);
 * promote into web/src/assets/chars/cast-<id>.glb after the contract check
 * (cast-contract.mjs) and a contact-sheet review.
 */
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const HERE = import.meta.dirname;
const ROOT = path.resolve(HERE, '../../..');
const { CAST, BUILD_BY_SHAPE, colourRoles } = await import(pathToFileURL(path.join(ROOT, 'web/src/shell/castData.js')).href);

/** Idle-face defaults per character, from the design sheet. */
const FACE = {
  bopp: { mouth: 'grin', browTilt: 0 },
  zizz: { mouth: 'smirk', browTilt: 0.18 },
  kwark: { mouth: 'beak', browTilt: -0.18 },
  tuff: { mouth: 'underbite', browTilt: -0.2 },
  mimo: { mouth: 'smile', browTilt: 0 },
  nibb: { mouth: 'shout', browTilt: 0.18 },
  glub: { mouth: 'wavy', browTilt: 0 },
  fizz: { mouth: 'smile', browTilt: 0 },
};

const ids = process.argv.slice(2);
if (!ids.length) { console.error('usage: build-character.mjs <id> [<id> ...]'); process.exit(2); }

const failed = [];
for (const id of ids) {
  const def = CAST.find((c) => c.id === id);
  if (!def) { console.error(`unknown character: ${id}`); failed.push(id); continue; }
  const payload = { ...def, build: BUILD_BY_SHAPE[def.shape], roles: colourRoles(def), face: FACE[id] };
  const out = path.join(HERE, `_char-${id}.glb`);
  console.log(`\n=== building ${id} ===`);
  const res = spawnSync(process.execPath, [
    path.join(HERE, 'run-blender.mjs'), path.join(HERE, 'build-character.py'), '--', JSON.stringify(payload), out,
  ], { stdio: 'inherit' });
  if (res.status !== 0) failed.push(id);
}
if (failed.length) { console.error(`\nBUILD_CHARACTER_FAILED: ${failed.join(', ')}`); process.exit(1); }
console.log('\nBUILD_CHARACTER_OK');
