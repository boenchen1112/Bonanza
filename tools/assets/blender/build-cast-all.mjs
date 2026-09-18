#!/usr/bin/env node
/**
 * Runs build-cast.py once per character, each in its own fresh Blender
 * process - matching build-tuff.py's proven single-run shape exactly.
 * Building all 8 inside one long-lived Blender session (manual scene reset
 * + orphan-data purge between characters) left stale state that made
 * bone-parented crests float off the head on characters later in the loop;
 * a fresh factory-startup process per character sidesteps that class of bug
 * entirely rather than chasing the specific stale-transform cause.
 *
 *   node tools/assets/blender/build-cast-all.mjs
 */
import { spawnSync } from 'node:child_process';
import path from 'node:path';

const CHAR_IDS = ['bopp', 'zizz', 'kwark', 'tuff', 'mimo', 'nibb', 'glub', 'fizz'];
const HERE = import.meta.dirname;

let failed = [];
for (const id of CHAR_IDS) {
  console.log(`\n=== building ${id} ===`);
  const res = spawnSync(process.execPath, [
    path.join(HERE, 'run-blender.mjs'),
    path.join(HERE, 'build-cast.py'),
    '--', id,
  ], { stdio: 'inherit' });
  if (res.status !== 0) failed.push(id);
}

if (failed.length) {
  console.error(`\nBUILD_CAST_ALL_FAILED: ${failed.join(', ')}`);
  process.exit(1);
}
console.log('\nBUILD_CAST_ALL_OK');
