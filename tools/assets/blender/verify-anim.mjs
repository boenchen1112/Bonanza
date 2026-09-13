#!/usr/bin/env node
/**
 * Load a standalone GLB (not through the game) via a bare three.js page and
 * shoot its swing animation at several times across its duration. Sanity
 * check for a reshaped Blender build: does the baked clip still play a
 * coherent motion, or did the rest-pose bake (see build-tuff.py's warning)
 * break it?
 *
 *   node tools/assets/blender/verify-anim.mjs [--glb <url-path>] [--out dir]
 */
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { spawn } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import path from 'node:path';

const argv = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, arr) => {
  if (a.startsWith('--')) acc.push([a.slice(2), arr[i + 1] && !arr[i + 1].startsWith('--') ? arr[i + 1] : true]);
  return acc;
}, []));
const ROOT = path.resolve(import.meta.dirname, '../../..');
const WEB = path.join(ROOT, 'web');
const GLB = String(argv.glb || '/src/assets/chars/_tuff-test.glb');
const OUT = path.resolve(ROOT, String(argv.out || 'runs/verify-anim'));
mkdirSync(OUT, { recursive: true });

const webRequire = createRequire(path.join(WEB, 'package.json'));
const pw = await import(pathToFileURL(webRequire.resolve('playwright')).href);
const chromium = pw.chromium ?? pw.default.chromium;
const PORT = 8900 + (process.pid % 300);
const server = spawn(process.execPath, [path.join(ROOT, 'tools/harness/serve.mjs'), WEB, String(PORT)], { stdio: ['ignore', 'pipe', 'pipe'] });
await new Promise((r) => { server.stdout.once('data', r); setTimeout(r, 1200); });

const browser = await chromium.launch({
  channel: 'chromium', headless: true,
  args: [...(process.platform === 'win32' ? ['--use-angle=d3d11'] : [])],
});
const page = await (await browser.newContext({ viewport: { width: 640, height: 720 } })).newPage();
page.on('pageerror', (e) => console.log('[pageerror]', e.message));
page.on('console', (m) => { if (m.type() === 'error') console.log('[console]', m.text()); });

await page.goto(`http://127.0.0.1:${PORT}/_verify.html?glb=${encodeURIComponent(GLB)}`, { waitUntil: 'load' });
await page.waitForFunction('window.__V && window.__V.ready', null, { timeout: 15000 }).catch(() => {});
const v = await page.evaluate(() => window.__V);
console.log('animations found:', v.animNames, 'using duration', v.duration, v.error ? `ERROR: ${v.error}` : '');
console.log('bounding box:', JSON.stringify(v.box));

const N = 8;
for (let i = 0; i < N; i++) {
  const t = v.duration ? (v.duration * i) / (N - 1) : 0;
  await page.evaluate((tt) => window.__seekTime(tt), t);
  await page.screenshot({ path: path.join(OUT, `${String(i).padStart(2, '0')}-t${t.toFixed(2)}.png`) });
}
const stats = await page.evaluate(() => ({ draws: window.__V.draws, triangles: window.__V.triangles }));
console.log('render stats (one TUFF, one frame):', JSON.stringify(stats));
console.log('shots -> ' + OUT);
await browser.close();
server.kill();
