#!/usr/bin/env node
/**
 * Bake-off, toy-rig side: boot chars-demo (TUFF wired into cast member 0 —
 * see the TEMP edit in chars/demo.js), force step 2 (MOCAP SWING), and shoot
 * several times across the clip for a frame-matched comparison against the
 * Blender build's own verify-anim.mjs shots.
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
const DIST = path.join(WEB, String(argv.dist || 'dist-bakeoff'));
const OUT = path.resolve(ROOT, String(argv.out || 'runs/bakeoff-toyrig'));
mkdirSync(OUT, { recursive: true });

const webRequire = createRequire(path.join(WEB, 'package.json'));
const pw = await import(pathToFileURL(webRequire.resolve('playwright')).href);
const chromium = pw.chromium ?? pw.default.chromium;
const PORT = 8500 + (process.pid % 300);
const server = spawn(process.execPath, [path.join(ROOT, 'tools/harness/serve.mjs'), DIST, String(PORT)], { stdio: ['ignore', 'pipe', 'pipe'] });
await new Promise((r) => { server.stdout.once('data', r); setTimeout(r, 1200); });

const browser = await chromium.launch({
  channel: 'chromium', headless: true,
  args: ['--autoplay-policy=no-user-gesture-required', '--mute-audio', ...(process.platform === 'win32' ? ['--use-angle=d3d11'] : [])],
});
const page = await (await browser.newContext({ viewport: { width: 640, height: 720 } })).newPage();
page.on('pageerror', (e) => console.log('[pageerror]', e.message));

const wait = (ms) => new Promise((r) => setTimeout(r, ms));
await page.goto(`http://127.0.0.1:${PORT}/index.html?scene=chars-demo&quality=high`, { waitUntil: 'load' });
await page.keyboard.press('KeyQ');
await page.waitForFunction('window.__BBB__ && window.__BBB__.ready', null, { timeout: 20000 });
await page.evaluate(() => window.__BBB__.ready);
await wait(500);

// setStep() gets overridden by the next natural beat-boundary check inside
// update() — wait for the beat-driven script to reach MOCAP SWING itself
// (step 2, one bar in at 118bpm ≈ 2.03s/bar) rather than forcing it.
const t0 = Date.now();
let stepName = '';
while (Date.now() - t0 < 8000) {
  stepName = await page.evaluate(() => window.__DEMO__?.stepName ?? '');
  if (stepName === 'MOCAP SWING') break;
  await wait(50);
}
console.log('step:', stepName, `(waited ${Date.now() - t0}ms)`);

const info = await page.evaluate(() => {
  const cast = window.__DEMO__?.cast;
  if (!cast) return null;
  return cast.members.map((m) => ({
    id: m.id, name: m.name, x: m.char.position.x,
    palette: m.palette?.body ?? m.palette,
  }));
});
console.log('cast members:', JSON.stringify(info));

// Match verify-anim.mjs's 8-sample sweep over the swing clip's own duration.
const N = 8;
const DUR = 40 / 30;   // swing_rig: frames 0-40 at 30fps, per inspect-ybot.py
for (let i = 0; i < N; i++) {
  await wait(i === 0 ? 30 : Math.round((DUR * 1000) / (N - 1)));
  await page.screenshot({ path: path.join(OUT, `${String(i).padStart(2, '0')}.png`) });
}
console.log('shots -> ' + OUT);
await browser.close();
server.kill();
