#!/usr/bin/env node
/**
 * Critic harness — boots the REAL built game in headless Chromium, plays it
 * with machine-precise input, and dumps evidence.
 *
 * This exists because a builder agent's own summary of its work is worthless
 * as review input. The critic looks at what actually renders and what the
 * timing actually measures, or the critic is not doing review.
 *
 * Usage:
 *   node tools/harness/inspect.mjs --scene swing-kings --out runs/sk-01 \
 *        [--play auto|perfect|sloppy|none] [--seconds 14] [--shots 12]
 *        [--width 1280] [--height 720] [--video]
 *
 * Outputs into --out:
 *   shot-000.png ... shot-NNN.png   evenly spaced frames during play
 *   telemetry.json                  fps, frame-time percentiles, judgements
 *   console.log                     page console + errors (non-empty = bug)
 *   summary.json                    machine-readable verdict inputs
 */

import { chromium } from 'playwright';
import { mkdir, writeFile, rm } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';

const argv = parseArgs(process.argv.slice(2));
const SCENE = argv.scene || 'title';
const OUT = path.resolve(argv.out || `runs/${SCENE}-${Date.now()}`);
const SECONDS = Number(argv.seconds || 14);
const SHOTS = Number(argv.shots || 12);
const W = Number(argv.width || 1280);
const H = Number(argv.height || 720);
const PLAY = argv.play || 'auto';
// SwiftShader cannot afford the post chain: frames hit ~400ms, which starves
// the setTimeout that drives synthetic presses and turns every note into a
// false miss. Default to the low tier so timing is measurable; pass
// --quality high when the point of the run is to look at the grade.
const QUALITY = argv.quality || 'low';
// Which actions the bot presses. Lane games need more than 'a'; the judge
// swallows unclaimed presses, so covering every lane is safe for all games.
const ACTIONS = argv.actions || 'a';
// Bot subdivision: 2 = eighths (default), 4 = sixteenths. A chart denser than
// the bot's grid shows up as misses that are coverage gaps, not defects.
const DIVISION = Number(argv.division || 2);
const WEB = path.resolve(argv.web || 'web');
// Parallel agents each verify against their own build directory, so two
// harness runs can never race on the same dist/ while one is mid-write.
const DIST = argv.dist || 'dist';

function parseArgs(a) {
  const o = {};
  for (let i = 0; i < a.length; i++) {
    if (!a[i].startsWith('--')) continue;
    const k = a[i].slice(2);
    const v = a[i + 1] && !a[i + 1].startsWith('--') ? a[++i] : true;
    o[k] = v;
  }
  return o;
}

async function ensureBuild() {
  if (argv['no-build']) return;
  await new Promise((res, rej) => {
    // Mock the leaderboard client by default: a real Cloudflare Worker call
    // has latency/availability the harness can't attribute to the game,
    // which breaks the "same seed -> same numbers" guarantee every other
    // measurement here relies on. Pass --live-network to deliberately test
    // against the real Worker instead (not for critic runs).
    const env = { ...process.env };
    if (!argv['live-network']) env.VITE_MOCK_NETWORK = '1';
    const p = spawn('npx', ['vite', 'build', '--outDir', DIST], { cwd: WEB, stdio: 'pipe', env });
    let err = '';
    p.stderr.on('data', (d) => { err += d; });
    p.stdout.on('data', (d) => { err += d; });
    p.on('close', (c) => (c === 0 ? res() : rej(new Error('build failed:\n' + err.slice(-4000)))));
  });
}

function serve(dir, port) {
  return new Promise((res) => {
    const p = spawn(process.execPath, [path.join(import.meta.dirname, 'serve.mjs'), dir, String(port)], {
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    p.stdout.once('data', () => res(p));
    setTimeout(() => res(p), 1200);
  });
}

// pid-derived so concurrent harness runs never fight over a port.
const PORT = Number(argv.port || 5321 + (process.pid % 900));

(async () => {
  await ensureBuild();
  if (existsSync(OUT)) await rm(OUT, { recursive: true, force: true });
  await mkdir(OUT, { recursive: true });

  const server = await serve(path.join(WEB, DIST), PORT);

  // The sandbox ships a pinned Chromium that may not match the Playwright
  // build's expected revision, so point at it explicitly. The full `chrome`
  // binary (not headless_shell) is required: we need real WebGL.
  const CHROME = process.env.PW_CHROMIUM
    || (existsSync('/opt/pw-browsers/chromium-1194/chrome-linux/chrome')
      ? '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' : undefined);

  const browser = await chromium.launch({
    executablePath: CHROME,
    args: [
      '--autoplay-policy=no-user-gesture-required',
      '--use-gl=swiftshader',
      '--enable-unsafe-swiftshader',
      '--disable-gpu-sandbox',
      '--mute-audio',
    ],
  });
  const context = await browser.newContext({
    viewport: { width: W, height: H },
    deviceScaleFactor: 1,
    recordVideo: argv.video ? { dir: path.join(OUT, 'video'), size: { width: W, height: H } } : undefined,
  });
  const page = await context.newPage();

  const logs = [];
  page.on('console', (m) => logs.push(`[${m.type()}] ${m.text()}`));
  page.on('pageerror', (e) => logs.push(`[pageerror] ${e.message}\n${e.stack || ''}`));
  page.on('requestfailed', (r) => logs.push(`[requestfailed] ${r.url()} ${r.failure()?.errorText}`));

  await page.goto(`http://127.0.0.1:${PORT}/index.html?scene=${encodeURIComponent(SCENE)}&quality=${encodeURIComponent(QUALITY)}`, {
    waitUntil: 'load', timeout: 30000,
  });

  // Real gesture so the AudioContext is allowed to run.
  await page.mouse.move(W / 2, H / 2);
  await page.keyboard.press('KeyQ');

  await page.waitForFunction('window.__BBB__ && window.__BBB__.ready', null, { timeout: 20000 });
  await page.evaluate(() => window.__BBB__.ready);
  await page.evaluate((q) => window.__BBB__.setQuality?.(q), QUALITY);
  await page.evaluate((s) => window.__BBB__.goto(s), SCENE);
  await page.waitForTimeout(700);
  await page.evaluate(() => window.__BBB__.resetTelemetry());

  // ---- autoplay ---------------------------------------------------------
  // Drives synthetic presses at exact audio times. `perfect` proves the game
  // is beatable and shows the top-end feedback; `sloppy` shows what a real
  // human's mediocre run looks like, which is what most players will see.
  if (PLAY !== 'none') {
    await page.evaluate(({ mode, secs, actions, division }) => window.__BBB__.autoplay({ mode, seconds: secs, actions, division }),
      // Generously longer than the capture window: screenshots are slow under
      // software rendering, so wall-clock capture outruns `SECONDS` of audio
      // time and a bot that stopped on time would leave a tail of false misses.
      { mode: PLAY, secs: SECONDS * 4, actions: ACTIONS, division: DIVISION });
  }

  // ---- capture ----------------------------------------------------------
  const shots = [];
  const interval = (SECONDS * 1000) / SHOTS;
  for (let i = 0; i < SHOTS; i++) {
    await page.waitForTimeout(interval);
    const f = path.join(OUT, `shot-${String(i).padStart(3, '0')}.png`);
    await page.screenshot({ path: f });
    shots.push(f);
  }

  const botPresses = await page.evaluate(() => window.__BBB__.botPresses?.() ?? 0);
  const telemetry = await page.evaluate(() => window.__BBB__.telemetry());
  const domProbe = await page.evaluate(() => ({
    uiNodes: document.getElementById('ui')?.querySelectorAll('*').length ?? 0,
    canvasSize: (() => { const c = document.getElementById('stage'); return c ? [c.width, c.height] : null; })(),
    scene: window.__BBB__.scene,
    title: document.title,
  }));

  await writeFile(path.join(OUT, 'telemetry.json'), JSON.stringify(telemetry, null, 2));
  await writeFile(path.join(OUT, 'console.log'), logs.join('\n') || '(clean)');

  const j = telemetry.judgements || [];
  const counts = j.reduce((m, x) => ((m[x.verdict] = (m[x.verdict] || 0) + 1), m), {});
  const errs = j.filter((x) => x.verdict !== 'miss').map((x) => x.errMs);
  const summary = {
    scene: SCENE,
    play: PLAY,
    quality: QUALITY,
    actions: ACTIONS,
    division: DIVISION,
    seconds: SECONDS,
    shots: shots.map((s) => path.relative(OUT, s)),
    // NOTE FOR CRITICS: this harness renders through SwiftShader (software
    // GPU). `fps` and `frameMs` are therefore NOT a statement about the game's
    // performance on real hardware and must not be reported as one. Judge
    // performance from `cpuMs` (our JS per frame) and `render` (draw calls /
    // triangles), which are GPU-independent.
    softwareRendered: true,
    fps: Math.round(telemetry.fps * 10) / 10,
    frameMs: telemetry.frameMs,
    cpuMs: telemetry.cpuMs,
    render: telemetry.render,
    outputLatencyMs: telemetry.outputLatencyMs,
    judgementCounts: counts,
    judgementTotal: j.length,
    botPresses,
    meanAbsErrMs: errs.length ? errs.reduce((a, b) => a + Math.abs(b), 0) / errs.length : null,
    biasMs: errs.length ? errs.reduce((a, b) => a + b, 0) / errs.length : null,
    consoleErrors: logs.filter((l) => l.startsWith('[pageerror]') || l.startsWith('[error]')).length,
    consoleClean: logs.length === 0,
    dom: domProbe,
  };
  await writeFile(path.join(OUT, 'summary.json'), JSON.stringify(summary, null, 2));

  await context.close();
  await browser.close();
  server.kill();

  console.log(JSON.stringify(summary, null, 2));
  console.log('\nartifacts -> ' + OUT);
  process.exit(0);
})().catch(async (e) => {
  console.error('HARNESS FAILURE:', e.message);
  process.exit(1);
});
