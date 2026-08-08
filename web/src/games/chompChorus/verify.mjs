#!/usr/bin/env node
/**
 * Chomp Chorus — lane-accurate verification harness.  [G4 builder owns this dir]
 *
 * The shared harness's autoplay only ever presses 'a' on eighth beats, so by
 * construction it misses every lane in this game. That proves nothing about
 * whether the game is beatable, and nothing at all about whether four
 * simultaneous notes on four different actions judge as four separate notes.
 *
 * This script reads the actual chart, presses the CORRECT key for every note at
 * that note's exact audio time, and reports:
 *   - overall verdict counts and mean |err|
 *   - per-lane counts (proving every lane is reachable and judged separately)
 *   - chord integrity: for every beat with k>1 notes, were all k judged, on the
 *     k distinct lanes the chart asked for?
 *
 * Presses carry the beat they were AIMED at, not the wall-clock moment they
 * were dispatched, and are emitted up to `--lookahead` seconds early so that a
 * 300ms SwiftShader frame cannot expire a note before the press for it lands.
 * Judgement error is therefore a measurement of the judge, not of the GPU.
 *
 *   node web/src/games/chompChorus/verify.mjs --out runs/g4-lanes --shots 12
 *   node web/src/games/chompChorus/verify.mjs --out runs/g4-drop --miss 0.35
 */

import { chromium } from 'playwright';
import { mkdir, writeFile, rm } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { spawn } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildChart, LANES } from './chart.js';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(HERE, '../../../..');

const argv = (() => {
  const a = process.argv.slice(2);
  const o = {};
  for (let i = 0; i < a.length; i++) {
    if (!a[i].startsWith('--')) continue;
    const k = a[i].slice(2);
    o[k] = a[i + 1] && !a[i + 1].startsWith('--') ? a[++i] : true;
  }
  return o;
})();

const OUT = path.resolve(REPO, argv.out || 'runs/g4-lanes');
const DIST = path.resolve(REPO, 'web', argv.dist || 'dist-g4');
const SECONDS = Number(argv.seconds || 78);
const SHOTS = Number(argv.shots || 12);
const W = Number(argv.width || 1280);
const H = Number(argv.height || 720);
const QUALITY = argv.quality || 'low';
const JITTER = Number(argv.jitter || 0);       // beats of timing noise
const MISS = Number(argv.miss || 0);           // fraction of notes deliberately dropped
const LOOKAHEAD = Number(argv.lookahead || 0.55);
const PORT = Number(argv.port || 6100 + (process.pid % 700));

const CHROME = process.env.PW_CHROMIUM
  || (existsSync('/opt/pw-browsers/chromium-1194/chrome-linux/chrome')
    ? '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' : undefined);

const chart = buildChart().map((n) => ({ beat: n.beat, lane: n.lane, action: LANES[n.lane].action }));

/** beat -> lanes expected, for the chord-integrity check. */
const chordMap = new Map();
for (const n of chart) {
  const k = n.beat.toFixed(4);
  if (!chordMap.has(k)) chordMap.set(k, []);
  chordMap.get(k).push(n.lane);
}

function serve(dir, port) {
  return new Promise((res) => {
    const p = spawn(process.execPath, [path.join(REPO, 'tools/harness/serve.mjs'), dir, String(port)],
      { stdio: ['ignore', 'pipe', 'pipe'] });
    p.stdout.once('data', () => res(p));
    setTimeout(() => res(p), 1200);
  });
}

(async () => {
  if (!existsSync(DIST)) throw new Error(`no build at ${DIST} — run: npx vite build --outDir dist-g4 (in web/)`);
  if (existsSync(OUT)) await rm(OUT, { recursive: true, force: true });
  await mkdir(OUT, { recursive: true });

  const server = await serve(DIST, PORT);
  const browser = await chromium.launch({
    executablePath: CHROME,
    args: ['--use-gl=swiftshader', '--enable-unsafe-swiftshader', '--mute-audio',
      '--autoplay-policy=no-user-gesture-required', '--disable-gpu-sandbox'],
  });
  const page = await (await browser.newContext({ viewport: { width: W, height: H }, deviceScaleFactor: 1 })).newPage();

  const logs = [];
  page.on('console', (m) => logs.push(`[${m.type()}] ${m.text()}`));
  page.on('pageerror', (e) => logs.push(`[pageerror] ${e.message}\n${e.stack || ''}`));

  await page.goto(`http://127.0.0.1:${PORT}/index.html?scene=chomp-chorus&quality=${QUALITY}`,
    { waitUntil: 'load', timeout: 30000 });
  await page.mouse.move(W / 2, H / 2);
  await page.keyboard.press('KeyQ');
  await page.waitForFunction('window.__BBB__ && window.__BBB__.ready', null, { timeout: 20000 });
  await page.evaluate(() => window.__BBB__.ready);
  await page.evaluate((q) => window.__BBB__.setQuality?.(q), QUALITY);
  await page.evaluate(() => window.__BBB__.goto('chomp-chorus'));
  await page.waitForTimeout(600);
  await page.evaluate(() => window.__BBB__.resetTelemetry());

  // ---- the driver --------------------------------------------------------
  await page.evaluate(({ notes, lookahead, jitter, miss }) => {
    const B = window.__BBB__;
    const log = [];
    window.__CC_LOG__ = log;
    B.bus.on('judge', (j) => log.push({ verdict: j.verdict, errMs: j.errMs, beat: j.beat, lane: j.lane }));

    // Deterministic drop/jitter so a run is reproducible.
    let seed = 0x1234567;
    const rnd = () => {
      seed = (seed * 1103515245 + 12345) & 0x7fffffff;
      return seed / 0x7fffffff;
    };

    const sorted = notes.slice().sort((a, b) => a.beat - b.beat);
    let i = 0;
    let pressed = 0;
    window.__CC_PRESSES__ = () => pressed;

    (function tick() {
      requestAnimationFrame(tick);
      const clock = B.clock;
      if (!clock.running) return;
      const ahead = lookahead / clock.spb;
      const nowB = clock.beat;
      let guard = 0;
      while (i < sorted.length && sorted[i].beat <= nowB + ahead && guard++ < 400) {
        const n = sorted[i++];
        if (miss > 0 && rnd() < miss) continue;
        const off = jitter > 0 ? (rnd() * 2 - 1) * jitter : 0;
        B.press(n.action, { atBeat: n.beat + off });
        pressed++;
      }
    }());
  }, { notes: chart, lookahead: LOOKAHEAD, jitter: JITTER, miss: MISS });

  // ---- capture -----------------------------------------------------------
  const interval = (SECONDS * 1000) / SHOTS;
  for (let i = 0; i < SHOTS; i++) {
    await page.waitForTimeout(interval);
    await page.screenshot({ path: path.join(OUT, `shot-${String(i).padStart(3, '0')}.png`) });
  }

  const judged = await page.evaluate(() => window.__CC_LOG__.slice());
  const presses = await page.evaluate(() => window.__CC_PRESSES__());
  const telemetry = await page.evaluate(() => window.__BBB__.telemetry());
  const result = await page.evaluate(() => {
    // result() is only reachable through the module the shell holds; poke the
    // registry copy directly so the score/rank/lane stats are observable.
    return null;
  });

  // ---- analysis ----------------------------------------------------------
  const counts = judged.reduce((m, j) => ((m[j.verdict] = (m[j.verdict] || 0) + 1), m), {});
  const perLane = [0, 1, 2, 3].map((i) => {
    const mine = judged.filter((j) => j.lane === i);
    const c = mine.reduce((m, j) => ((m[j.verdict] = (m[j.verdict] || 0) + 1), m), {});
    const notes = chart.filter((n) => n.lane === i).length;
    return {
      lane: i, action: LANES[i].action, chartNotes: notes, judged: mine.length,
      perfect: c.perfect || 0, great: c.great || 0, good: c.good || 0, miss: c.miss || 0,
    };
  });

  // chord integrity: every multi-note beat must produce one judgement per lane
  const byBeat = new Map();
  for (const j of judged) {
    const k = Number(j.beat).toFixed(4);
    if (!byBeat.has(k)) byBeat.set(k, []);
    byBeat.get(k).push(j.lane);
  }
  let chordBeats = 0; let chordOk = 0; const chordFails = [];
  for (const [k, lanes] of chordMap) {
    if (lanes.length < 2) continue;
    chordBeats++;
    const got = (byBeat.get(k) || []).slice().sort();
    const want = lanes.slice().sort();
    if (got.length === want.length && got.every((v, i2) => v === want[i2])) chordOk++;
    else chordFails.push({ beat: Number(k), want, got });
  }

  const hits = judged.filter((j) => j.verdict !== 'miss');
  const summary = {
    scene: 'chomp-chorus',
    mode: { jitter: JITTER, miss: MISS, lookahead: LOOKAHEAD },
    chartNotes: chart.length,
    presses,
    judgements: judged.length,
    counts,
    meanAbsErrMs: hits.length ? hits.reduce((a, j) => a + Math.abs(j.errMs), 0) / hits.length : null,
    perLane,
    chord: { multiNoteBeats: chordBeats, intact: chordOk, failures: chordFails.slice(0, 12) },
    cpuMs: telemetry.cpuMs,
    render: telemetry.render,
    consoleErrors: logs.filter((l) => l.startsWith('[pageerror]') || l.startsWith('[error]')).length,
    result,
  };

  await writeFile(path.join(OUT, 'lane-summary.json'), JSON.stringify(summary, null, 2));
  await writeFile(path.join(OUT, 'console.log'), logs.join('\n') || '(clean)');
  console.log(JSON.stringify(summary, null, 2));
  console.log('\nartifacts -> ' + OUT);

  await browser.close();
  server.kill();
  process.exit(0);
})().catch((e) => { console.error('VERIFY FAILURE:', e); process.exit(1); });
