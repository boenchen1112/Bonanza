#!/usr/bin/env node
/**
 * Integrity check + screenshot for all 8 build-cast.py outputs, generalizing
 * verify-anim.mjs's single-character approach. Per character: confirm all 8
 * clip names are present, sample the bounding box across the swing clip's
 * duration (an exploded/collapsed box is the armature_apply-corruption
 * signature hit during the TUFF bake-off - catching it here means a human
 * doesn't have to), record draw calls/triangles, and save a handful of
 * scrubbed screenshots for the contact-sheet build + a sanity look before
 * anything is wired into the live game.
 *
 *   node tools/assets/blender/verify-cast.mjs [--out dir]
 */
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, copyFileSync, rmSync } from 'node:fs';
import path from 'node:path';

const argv = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, arr) => {
  if (a.startsWith('--')) acc.push([a.slice(2), arr[i + 1] && !arr[i + 1].startsWith('--') ? arr[i + 1] : true]);
  return acc;
}, []));
const ROOT = path.resolve(import.meta.dirname, '../../..');
const WEB = path.join(ROOT, 'web');
const OUT = path.resolve(ROOT, String(argv.out || 'runs/verify-cast'));
mkdirSync(OUT, { recursive: true });

const CHAR_IDS = ['bopp', 'zizz', 'kwark', 'tuff', 'mimo', 'nibb', 'glub', 'fizz'];
const EXPECT_CLIPS = ['celebrate', 'dance', 'fail', 'idle', 'pitch', 'ready', 'swing', 'taunt'];

const webRequire = createRequire(path.join(WEB, 'package.json'));
const pw = await import(pathToFileURL(webRequire.resolve('playwright')).href);
const chromium = pw.chromium ?? pw.default.chromium;
const PORT = 8700 + (process.pid % 300);
const server = spawn(process.execPath, [path.join(ROOT, 'tools/harness/serve.mjs'), WEB, String(PORT)], { stdio: ['ignore', 'pipe', 'pipe'] });
await new Promise((r) => { server.stdout.once('data', r); setTimeout(r, 1200); });

const browser = await chromium.launch({
  channel: 'chromium', headless: true,
  args: [...(process.platform === 'win32' ? ['--use-angle=d3d11'] : [])],
});

const results = [];
for (const id of CHAR_IDS) {
  const page = await (await browser.newContext({ viewport: { width: 640, height: 720 } })).newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });

  // serve.mjs's static root is web/, and its path resolution blocks
  // traversal above it - copy each build into a scratch spot inside that
  // root instead of trying to reach tools/assets/blender/ via a relative
  // "..".
  const tempGlb = path.join(WEB, 'src/assets/chars/_cast-verify-temp.glb');
  copyFileSync(path.join(ROOT, 'tools/assets/blender', `_cast-${id}.glb`), tempGlb);
  await page.goto(`http://127.0.0.1:${PORT}/_verify.html?glb=${encodeURIComponent('/src/assets/chars/_cast-verify-temp.glb')}`, { waitUntil: 'load' });
  await page.waitForFunction('window.__V && window.__V.ready', null, { timeout: 15000 }).catch(() => {});
  const v = await page.evaluate(() => window.__V);

  const clipsOk = EXPECT_CLIPS.every((c) => (v.animNames || []).includes(c));
  const N = 6;
  const shots = [];
  const boxes = [];
  for (let i = 0; i < N; i++) {
    const t = v.duration ? (v.duration * i) / (N - 1) : 0;
    await page.evaluate((tt) => window.__seekTime(tt), t);
    const shotPath = path.join(OUT, `${id}-${String(i).padStart(2, '0')}-t${t.toFixed(2)}.png`);
    await page.screenshot({ path: shotPath });
    shots.push(shotPath);
    const box = await page.evaluate(() => window.__V.box);
    boxes.push(box);
  }
  const stats = await page.evaluate(() => ({ draws: window.__V.draws, triangles: window.__V.triangles }));

  // Corruption signature: bounding-box size exploding or collapsing across
  // the timeline relative to its own first frame.
  const size0 = boxes[0]?.size || [1, 1, 1];
  const boxSane = boxes.every((b) => {
    const s = b?.size || [0, 0, 0];
    return s.every((v2, i2) => v2 > size0[i2] * 0.15 && v2 < size0[i2] * 6);
  });

  results.push({ id, clipsOk, animNames: v.animNames, boxSane, box0: boxes[0], stats, errors, error: v.error });
  console.log(`${id}: clipsOk=${clipsOk} boxSane=${boxSane} draws=${stats.draws} tris=${stats.triangles} errors=${errors.length}${v.error ? ` ERROR=${v.error}` : ''}`);
  await page.close();
}

await browser.close();
server.kill();
rmSync(path.join(WEB, 'src/assets/chars/_cast-verify-temp.glb'), { force: true });

writeFileSync(path.join(OUT, 'summary.json'), JSON.stringify(results, null, 2));
const allOk = results.every((r) => r.clipsOk && r.boxSane && !r.error && r.errors.length === 0);
console.log(allOk ? 'VERIFY_CAST_ALL_OK' : 'VERIFY_CAST_FAILURES_FOUND');
console.log('shots + summary.json -> ' + OUT);
process.exit(allOk ? 0 : 1);
