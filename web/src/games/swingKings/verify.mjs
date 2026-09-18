#!/usr/bin/env node
/**
 * Swing Kings — hold/release proof.
 *
 * The shared harness bot can only emit key-DOWN events, so it can never
 * perform the gesture this game is built around: it taps, and a tap is a
 * BUNT by design. That means `inspect.mjs` cannot prove the power axis works
 * at all. This drives the real gesture at exact audio times through
 * `window.__BBB__.swing` (installed by the game itself in load()) and asserts
 * the two ends of the design:
 *
 *   a long clean windup, released on the beat  ->  HOME RUN (hold power)
 *   a half windup                              ->  LINE DRIVE
 *   a bare on-time tap                         ->  HOME RUN — tap mode earns
 *                                                  power from timing instead
 *                                                  (it used to be a BUNT, so a
 *                                                  keyboard player never saw a
 *                                                  home run)
 *
 * Usage (from the repo root):
 *   node web/src/games/swingKings/verify.mjs [--dist dist-g1] [--port 5599]
 */

import { chromium } from 'playwright';
import { spawn } from 'node:child_process';
import path from 'node:path';
import { existsSync } from 'node:fs';

const argv = Object.fromEntries(
  process.argv.slice(2).reduce((acc, a, i, arr) => {
    if (a.startsWith('--')) acc.push([a.slice(2), arr[i + 1] && !arr[i + 1].startsWith('--') ? arr[i + 1] : true]);
    return acc;
  }, [])
);

const ROOT = path.resolve(path.join(import.meta.dirname, '../../../..'));
const WEB = path.join(ROOT, 'web');
const DIST = String(argv.dist || 'dist-g1');
const PORT = Number(argv.port || 5599);
const CHROME = '/opt/pw-browsers/chromium-1194/chrome-linux/chrome';

function serve(dir, port) {
  return new Promise((res) => {
    const p = spawn(process.execPath, [path.join(ROOT, 'tools/harness/serve.mjs'), dir, String(port)],
      { stdio: ['ignore', 'pipe', 'pipe'] });
    p.stdout.once('data', () => res(p));
    setTimeout(() => res(p), 1200);
  });
}

function build() {
  return new Promise((res, rej) => {
    // --mode harness keeps window.__BBB__ in the bundle (production strips it);
    // vite's JS entry via node, since npx is a .cmd shim spawn() can't run on Windows.
    const p = spawn(process.execPath, [path.join(WEB, 'node_modules/vite/bin/vite.js'), 'build', '--mode', 'harness', '--outDir', DIST],
      { cwd: WEB, stdio: 'pipe' });
    let err = '';
    p.stderr.on('data', (d) => { err += d; });
    p.stdout.on('data', (d) => { err += d; });
    p.on('close', (c) => (c === 0 ? res() : rej(new Error('build failed:\n' + err.slice(-3000)))));
  });
}

const results = [];
const fail = [];
function check(name, ok, detail) {
  results.push({ name, ok: !!ok, detail });
  if (!ok) fail.push(name);
}

(async () => {
  if (!argv['no-build']) await build();
  const server = await serve(path.join(WEB, DIST), PORT);

  // Real GPU (full Chromium, new-headless), like the harness: the contact
  // checks are frame-bound, and at SwiftShader's 2-3fps one frame is 400ms.
  const common = ['--mute-audio', '--autoplay-policy=no-user-gesture-required'];
  const browser = await chromium.launch(existsSync(CHROME)
    ? { executablePath: CHROME, args: [...common, '--use-gl=swiftshader', '--enable-unsafe-swiftshader', '--disable-gpu-sandbox'] }
    : { channel: 'chromium', headless: true,
      args: [...common, ...(process.platform === 'win32' ? ['--use-angle=d3d11'] : [])] });
  const ctx = await browser.newContext({ viewport: { width: 960, height: 540 }, deviceScaleFactor: 1 });
  const page = await ctx.newPage();
  const logs = [];
  page.on('console', (m) => logs.push(`[${m.type()}] ${m.text()}`));
  page.on('pageerror', (e) => logs.push(`[pageerror] ${e.message}\n${e.stack || ''}`));

  await page.goto(`http://127.0.0.1:${PORT}/index.html?scene=swing-kings&quality=low`, { waitUntil: 'load' });
  await page.keyboard.press('KeyQ');
  await page.waitForFunction('window.__BBB__ && window.__BBB__.ready');
  await page.evaluate(() => window.__BBB__.ready);
  await page.evaluate(() => window.__BBB__.goto('swing-kings'));
  await page.waitForFunction('window.__BBB__.swing', null, { timeout: 15000 });

  check('debug hook installed', true);

  /**
   * Queue every gesture up-front, in audio time. Presses carry the beat they
   * were AIMED at, so software-rendered frame starvation cannot change the
   * result — the same reason the shared harness drives its bot this way.
   */
  const plan = await page.evaluate(async () => {
    const B = window.__BBB__;
    const wait = (beat) => new Promise((res) => {
      const tick = () => (B.clock.beat >= beat ? res() : requestAnimationFrame(tick));
      tick();
    });

    // Notes in the teach section are on bar downbeats: 0, 4, 8, 12, 16, ...
    // The pitch for beat `n` is launched at `n - 2`, so a full windup is the
    // ball's entire flight.
    const script = [
      { note: 0, hold: 2.0, tag: 'full-windup' },     // expect HOME RUN
      { note: 4, hold: 0.0, tag: 'bare-tap' },        // expect HOME RUN (tap: timing power)
      { note: 8, hold: 2.0, tag: 'full-windup-2' },   // expect HOME RUN
      { note: 12, hold: 1.0, tag: 'half-windup' },    // expect LINE DRIVE
      { note: 16, hold: 0.0, tag: 'bare-tap-2' },     // expect HOME RUN (tap: timing power)
      { note: 20, hold: 2.0, tag: 'full-windup-3' },  // expect HOME RUN
    ];

    for (const s of script) {
      await wait(s.note - s.hold - 0.35);
      B.swing.hold(s.note - s.hold);
      await wait(s.note - 0.05);
      B.swing.release(s.note);
    }
    await wait(22.5);
    return { script, stats: B.swing.stats(), telemetry: B.telemetry() };
  });

  const swings = plan.stats.swings;
  const byBeat = new Map(swings.map((s) => [s.beat, s]));

  const expect = [
    [0, 'homer', 'perfect'], [4, 'homer', 'perfect'], [8, 'homer', 'perfect'],
    [12, 'liner', 'perfect'], [16, 'homer', 'perfect'], [20, 'homer', 'perfect'],
  ];
  for (const [beat, tier, verdict] of expect) {
    const s = byBeat.get(beat);
    check(`beat ${beat}: ${tier} / ${verdict}`,
      s && s.tier === tier && s.verdict === verdict,
      s ? `got tier=${s.tier} verdict=${s.verdict} hold=${s.hold} power=${s.power} err=${s.errMs}ms` : 'no swing recorded');
  }

  const timing = swings.filter((s) => byBeat.get(s.beat) === s).map((s) => Math.abs(s.errMs));
  const worst = timing.length ? Math.max(...timing) : 999;
  check('release timing exact (|err| < 2ms)', worst < 2, `worst |err| = ${worst}ms`);

  const powers = expect.map(([b]) => byBeat.get(b)?.power ?? -1);
  check('hold power is monotone in hold length',
    powers[0] > powers[3] && powers[3] > 0,
    `full=${powers[0]} half=${powers[3]}`);
  check('an on-time tap earns full power from timing',
    powers[1] === 1 && powers[4] === 1,
    `tap=${powers[1]} tap2=${powers[4]}`);

  // The bat meets the ball: visuals fire on the bat's contact frame, from the
  // bat's sweet spot, and the pitch aim adapts to it — so after the first
  // contact the waiting ball is on the bat, not beside it (ball radius 0.2).
  const contacts = plan.stats.contacts || [];
  check('every hit made contact', contacts.length >= expect.length,
    `${contacts.length} contacts for ${expect.length} hits`);
  const later = contacts.slice(1).map((c) => c.gap);
  const worstGap = later.length ? Math.max(...later) : 99;
  check('ball on the bat at contact (gap < 0.3 after the first)', worstGap < 0.3,
    `gaps ${contacts.map((c) => c.gap).join(', ')}`);
  const worstDelay = contacts.length ? Math.max(...contacts.map((c) => c.delayMs)) : 999;
  check('contact visuals wait at most ~2 frames for the bat (< 80ms)', worstDelay < 80,
    `delays ${contacts.map((c) => c.delayMs).join(', ')}ms`);

  const errs = logs.filter((l) => l.startsWith('[pageerror]') || l.startsWith('[error]'));
  check('console clean', errs.length === 0, errs.join('\n') || 'no errors');

  await ctx.close();
  await browser.close();
  server.kill();

  for (const r of results) {
    console.log(`${r.ok ? 'PASS' : 'FAIL'}  ${r.name}${r.detail ? `   (${r.detail})` : ''}`);
  }
  console.log('\nswings:', JSON.stringify(swings, null, 0));
  console.log(fail.length ? `\n${fail.length} FAILED` : '\nALL PASS');
  process.exit(fail.length ? 1 : 0);
})().catch((e) => { console.error('VERIFY FAILURE:', e); process.exit(2); });
