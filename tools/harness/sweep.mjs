#!/usr/bin/env node
/**
 * Run the harness over every registered scene and print one line each.
 *
 *   node tools/harness/sweep.mjs [--dist dist-sweep] [--out runs/sweep]
 *        [--seconds 6] [--scenes a,b,c] [--play auto]
 *
 * Builds once, then runs each scene with --no-build. Exits non-zero if any
 * scene's console is not clean, so it doubles as the "console-clean on every
 * scene" gate. Per-scene artifacts land in <out>/<scene>/.
 */

import { readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';

const a = process.argv.slice(2);
const opt = (k, d) => (a.includes(`--${k}`) ? a[a.indexOf(`--${k}`) + 1] : d);
const ROOT = path.resolve(import.meta.dirname, '..', '..');
const DIST = opt('dist', 'dist-sweep');
const OUT = opt('out', 'runs/sweep');
const SECONDS = opt('seconds', '6');
const PLAY = opt('play', 'auto');

const registry = readFileSync(path.join(ROOT, 'web/src/shell/registry.js'), 'utf8');
const all = [...registry.matchAll(/id:\s*'([^']+)'/g)].map((m) => m[1]);
const scenes = opt('scenes', null)?.split(',') ?? all;

const inspect = path.join(ROOT, 'tools/harness/inspect.mjs');
let dirty = 0;
scenes.forEach((scene, i) => {
  const out = path.join(OUT, scene);
  const args = [inspect, '--scene', scene, '--dist', DIST, '--out', out, '--seconds', SECONDS,
    '--shots', '2', '--play', PLAY];
  if (i > 0 || a.includes('--no-build')) args.push('--no-build');
  const r = spawnSync(process.execPath, args, { cwd: ROOT, encoding: 'utf8', maxBuffer: 64 << 20 });
  let s = null;
  try { s = JSON.parse(readFileSync(path.join(ROOT, out, 'summary.json'), 'utf8')); } catch { /* failed run */ }
  if (!s || r.status !== 0) {
    dirty++;
    console.log(`${scene.padEnd(15)} HARNESS FAILURE ${(r.stderr || r.stdout || '').trim().split('\n').pop()}`);
    return;
  }
  const log = readFileSync(path.join(ROOT, out, 'console.log'), 'utf8');
  const lines = log === '(clean)' ? [] : [...new Set(log.split('\n').filter((l) => l.startsWith('[')))];
  if (!s.consoleClean) dirty++;
  const au = s.audio ? `audio ${s.audio.pass ? 'pass' : 'FAIL'} ${s.audio.rmsDb}dB` : 'audio -';
  console.log(`${scene.padEnd(15)} ${s.consoleClean ? 'clean' : 'DIRTY'}  ${String(s.fps).padStart(5)}fps  `
    + `cpu ${s.cpuMs.mean.toFixed(1)}ms  draws ${s.render.drawCalls}  ${au}`
    + (lines.length ? '\n    ' + lines.slice(0, 6).join('\n    ') : ''));
});
process.exit(dirty ? 1 : 0);
