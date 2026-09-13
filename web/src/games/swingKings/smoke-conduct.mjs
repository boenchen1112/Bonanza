#!/usr/bin/env node
/**
 * Swing Kings conduct-mode smoke test (ADR 0004: gesture modes are exempt from
 * the standard harness, so they get this instead of `inspect.mjs`).
 *
 *   node web/src/games/swingKings/smoke-conduct.mjs [--dist dist-conduct] [--no-build]
 *
 * MOUSE: with swingInput='mouse', drive a real Playwright mouse through the
 * conducting gesture for the first scored pitches — press as the ball
 * launches, lift, then a sharp downstroke that stops dead on the beat. The
 * ictus detector must release the swing (not the button-up, which comes
 * later), so the recorded swings must be hits with real hold length.
 *
 * CAMERA: with swingInput='camera' and Chromium's fake camera, the bundled
 * MediaPipe pipeline must load with the network blocked and no console
 * errors, and the preview must appear (the fake feed has no hand, so no
 * swings are expected — the real webcam check is a person's job).
 */

import { chromium } from 'playwright';
import { spawn } from 'node:child_process';
import path from 'node:path';

const argv = process.argv.slice(2);
const ROOT = path.resolve(import.meta.dirname, '..', '..', '..', '..');
const WEB = path.join(ROOT, 'web');
const DIST = argv.includes('--dist') ? argv[argv.indexOf('--dist') + 1] : 'dist-conduct';
const PORT = 5700 + (process.pid % 200);

const run = (cmd, args, cwd) => new Promise((res, rej) => {
  const p = spawn(cmd, args, { cwd, stdio: 'pipe' });
  let out = '';
  p.stdout.on('data', (d) => { out += d; }); p.stderr.on('data', (d) => { out += d; });
  p.on('close', (c) => (c === 0 ? res(out) : rej(new Error(out.slice(-2000)))));
});

if (!argv.includes('--no-build')) {
  await run(process.execPath, [path.join(WEB, 'node_modules/vite/bin/vite.js'), 'build', '--mode', 'harness', '--outDir', DIST], WEB);
}
const server = spawn(process.execPath, [path.join(ROOT, 'tools/harness/serve.mjs'), path.join(WEB, DIST), String(PORT)], { stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 900));

const browser = await chromium.launch({
  channel: 'chromium', headless: true,
  args: ['--use-angle=d3d11', '--mute-audio', '--autoplay-policy=no-user-gesture-required',
    '--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream'],
});
const results = [];
const check = (name, ok, detail) => results.push({ name, ok, detail });

async function openGame(mode) {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 720 }, permissions: ['camera'] });
  const page = await ctx.newPage();
  const logs = [];
  // Same environment filter as inspect.mjs: the dev laptop's antivirus injects
  // a script into localhost pages; its aborted fetch is not the game's error.
  page.on('console', (m) => { if (!/kaspersky-labs.com/i.test(m.location()?.url || '')) logs.push(`[${m.type()}] ${m.text()}`); });
  page.on('pageerror', (e) => logs.push(`[pageerror] ${e.message}`));
  await ctx.route('**/*', (r) => (r.request().url().startsWith(`http://127.0.0.1:${PORT}/`) ? r.continue() : r.abort()));
  await page.addInitScript((m) => localStorage.setItem('bbb:options', JSON.stringify({ swingInput: m })), mode);
  await page.goto(`http://127.0.0.1:${PORT}/index.html?scene=title`);
  await page.mouse.click(640, 360);
  await page.waitForFunction('window.__BBB__ && window.__BBB__.ready');
  await page.evaluate(() => window.__BBB__.ready);
  await page.evaluate(() => window.__BBB__.goto('swing-kings'));
  await page.waitForFunction('window.__BBB__.swing', null, { timeout: 15000 });
  return { ctx, page, logs };
}

/**
 * Move the held pointer from y0 to y1 in `n` pointermove events `ms` apart,
 * dispatched INSIDE the page so the events carry real, evenly spaced
 * timestamps (Playwright's stepped moves arrive back to back with identical
 * ones, which no real mouse produces).
 */
const stroke = (page, y0, y1, n, ms) => page.evaluate(async ({ y0, y1, n, ms }) => {
  for (let i = 1; i <= n; i++) {
    const u = i / n;
    window.dispatchEvent(new PointerEvent('pointermove', { clientX: 900, clientY: y0 + (y1 - y0) * u * u, buttons: 1, bubbles: true }));
    await new Promise((r) => setTimeout(r, ms));
  }
}, { y0, y1, n, ms });

const waitBeat = (page, b) => page.waitForFunction((x) => window.__BBB__.clock.beat >= x, b, { polling: 'raf', timeout: 30000 });

// ------------------------------------------------------------------ mouse
{
  const { ctx, page, logs } = await openGame('mouse');
  for (const target of [0, 4, 8]) {
    await waitBeat(page, target - 2);            // ball launches
    await page.mouse.move(900, 470);
    await page.mouse.down();
    await stroke(page, 470, 350, 6, 30);            // lift
    await waitBeat(page, target - 0.2);           // ~100ms of stroke lands the stop on the beat
    // One continuous fast downstroke, ending — and stopping dead — right on
    // the beat.
    await stroke(page, 350, 610, 10, 8);            // sharp downstroke...
    await waitBeat(page, target + 0.45);         // ...stop dead, hold, then let go late
    await page.mouse.up();
  }
  await waitBeat(page, 10);
  const swings = await page.evaluate(() => window.__BBB__.swing.stats().swings);
  const hits = swings.filter((s) => s.verdict !== 'miss' && s.hold > 0.5);
  check('mouse: downstroke ictus swings the bat (3 conducted hits with a real windup)', hits.length >= 3,
    JSON.stringify(swings.map((s) => ({ beat: s.beat, v: s.verdict, err: s.errMs, hold: s.hold, tier: s.tier }))));
  check('mouse: released by the ictus, not the late button-up (|err| < 128ms)',
    hits.every((s) => Math.abs(s.errMs) < 128), hits.map((s) => s.errMs).join(', '));
  const errs = logs.filter((l) => l.startsWith('[error]') || l.startsWith('[pageerror]'));
  check('mouse: console clean', errs.length === 0, errs.join(' | ') || 'clean');
  await ctx.close();
}

// ----------------------------------------------------------------- camera
{
  const { ctx, page, logs } = await openGame('camera');
  await page.waitForTimeout(9000);               // model + wasm load, first frames
  const state = await page.evaluate(() => ({
    preview: [...document.querySelectorAll('body > div')].some((d) => d.querySelector('video')),
  }));
  const fellBack = logs.some((l) => l.includes('camera conduct unavailable'));
  check('camera: bundled MediaPipe pipeline starts offline (preview shown) — or falls back cleanly',
    state.preview || fellBack, state.preview ? 'preview shown' : `fell back: ${logs.find((l) => l.includes('unavailable'))}`);
  const errs = logs.filter((l) => l.startsWith('[error]') || l.startsWith('[pageerror]'));
  check('camera: console clean (network blocked)', errs.length === 0, errs.join(' | ') || 'clean');
  await ctx.close();
}

await browser.close();
server.kill();
for (const r of results) console.log(`${r.ok ? 'PASS' : 'FAIL'}  ${r.name}\n      ${r.detail}`);
process.exit(results.every((r) => r.ok) ? 0 : 1);
