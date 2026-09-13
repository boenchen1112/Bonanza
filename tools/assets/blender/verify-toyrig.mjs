#!/usr/bin/env node
/**
 * Bake-off, toy-rig side, isolated: TUFF alone via charMesh(), driven
 * through anim.play('swing', {hold:true}) and scrubbed the same way
 * verify-anim.mjs scrubs the Blender build — same framing, same sample
 * count, for a fair side-by-side.
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
const OUT = path.resolve(ROOT, String(argv.out || 'runs/verify-toyrig'));
mkdirSync(OUT, { recursive: true });

const webRequire = createRequire(path.join(WEB, 'package.json'));
const pw = await import(pathToFileURL(webRequire.resolve('playwright')).href);
const chromium = pw.chromium ?? pw.default.chromium;
const PORT = 9200 + (process.pid % 300);
const server = spawn(process.execPath, [path.join(ROOT, 'tools/harness/serve.mjs'), WEB, String(PORT)], { stdio: ['ignore', 'pipe', 'pipe'] });
await new Promise((r) => { server.stdout.once('data', r); setTimeout(r, 1200); });

const browser = await chromium.launch({
  channel: 'chromium', headless: true,
  args: [...(process.platform === 'win32' ? ['--use-angle=d3d11'] : [])],
});
const page = await (await browser.newContext({ viewport: { width: 640, height: 720 } })).newPage();
page.on('pageerror', (e) => console.log('[pageerror]', e.message));
page.on('console', (m) => { if (m.type() === 'error') console.log('[console]', m.text()); });

await page.goto(`http://127.0.0.1:${PORT}/_verify-toyrig.html`, { waitUntil: 'load' });
await page.waitForFunction('window.__T && window.__T.ready', null, { timeout: 15000 }).catch(() => {});
const t = await page.evaluate(() => window.__T);
console.log('duration:', t?.duration, 'box:', JSON.stringify(t?.box));

const N = 8;
const dur = t?.duration && Number.isFinite(t.duration) ? t.duration : 1.5;
for (let i = 0; i < N; i++) {
  const time = (dur * i) / (N - 1);
  await page.evaluate((tt) => window.__advanceTo(tt), time);
  await page.screenshot({ path: path.join(OUT, `${String(i).padStart(2, '0')}-t${time.toFixed(2)}.png`) });
}
const stats = await page.evaluate(() => ({ draws: window.__T.draws, triangles: window.__T.triangles }));
console.log('render stats (one TUFF, one frame):', JSON.stringify(stats));
console.log('shots -> ' + OUT);
await browser.close();
server.kill();
