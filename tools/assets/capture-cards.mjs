#!/usr/bin/env node
/**
 * Game-card thumbnails, captured from the real games.
 *
 *   node tools/assets/capture-cards.mjs [--only swing-kings,chomp-chorus]
 *
 * Runs each minigame through the harness (real GPU, perfect autoplay, UI
 * hidden), takes one frame a few seconds into play, crops it to the card's
 * 640x300 aspect and writes web/src/assets/shell/cards/<id>.webp. The free-
 * play carousel and the title's attract reel draw these instead of the
 * procedural 2D sketches (which stay as the fallback). Re-run after a game's
 * look changes; output depends only on the build.
 */

import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import path from 'node:path';

const ROOT = path.resolve(import.meta.dirname, '..', '..');
const OUT = path.join(ROOT, 'web/src/assets/shell/cards');
const a = process.argv.slice(2);
const only = a.includes('--only') ? a[a.indexOf('--only') + 1].split(',') : null;

/** When to shoot (seconds into the run) and which slice of the frame reads best. */
const GAMES = [
  { id: 'swing-kings', skip: 7.2, crop: [0, 120, 1280, 600] },
  { id: 'drumline-dash', skip: 8, crop: [0, 100, 1280, 600] },
  { id: 'bounce-brigade', skip: 8, crop: [0, 90, 1280, 600] },
  { id: 'chomp-chorus', skip: 9.1, crop: [0, 80, 1280, 600] },
  { id: 'finale-fever', skip: 8, crop: [0, 80, 1280, 600] },
].filter((g) => !only || only.includes(g.id));

mkdirSync(OUT, { recursive: true });
const inspect = path.join(ROOT, 'tools/harness/inspect.mjs');

const req = createRequire(path.join(ROOT, 'web/package.json'));
const pw = await import(pathToFileURL(req.resolve('playwright')).href);
const chromium = pw.chromium ?? pw.default.chromium;
const browser = await chromium.launch({ headless: true });
const page = await browser.newPage();

let built = false;
for (const g of GAMES) {
  const run = path.join(ROOT, 'runs/cards', g.id);
  const args = [inspect, '--scene', g.id, '--dist', 'dist-cards', '--out', run, '--skip', String(g.skip),
    '--seconds', '0.2', '--shots', '1', '--play', 'perfect', '--chart', '--hide-ui', '--no-audio'];
  if (built) args.push('--no-build');
  const r = spawnSync(process.execPath, args, { cwd: ROOT, encoding: 'utf8' });
  if (r.status !== 0) { console.error(`${g.id}: harness failed\n${r.stderr || r.stdout}`); continue; }
  built = true;

  const png = readFileSync(path.join(run, 'shot-000.png')).toString('base64');
  const webp = await page.evaluate(async ({ png, crop }) => {
    const img = new Image();
    img.src = `data:image/png;base64,${png}`;
    await img.decode();
    const c = document.createElement('canvas');
    c.width = 640; c.height = 300;
    c.getContext('2d').drawImage(img, crop[0], crop[1], crop[2], crop[3], 0, 0, 640, 300);
    return c.toDataURL('image/webp', 0.86).split(',')[1];
  }, { png, crop: g.crop });
  const file = path.join(OUT, `${g.id}.webp`);
  writeFileSync(file, Buffer.from(webp, 'base64'));
  console.log(`${g.id.padEnd(15)} -> ${path.relative(ROOT, file)} (${(Buffer.from(webp, 'base64').length / 1024).toFixed(0)} KB)`);
}
await browser.close();
