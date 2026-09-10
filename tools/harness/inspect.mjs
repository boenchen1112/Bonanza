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
 *        [--width 1280] [--height 720] [--video] [--quality high|low]
 *        [--swiftshader]
 *
 * Rendering: by default this runs Playwright's full Chromium in new-headless
 * mode on the machine's real GPU (ANGLE/D3D11 on Windows) — no window opens,
 * frames run at display rate, and `fps`/`frameMs` mean something. Pass
 * `--swiftshader` to force the old software path (headless shell +
 * SwiftShader) for machines with no usable GPU; the summary then says so via
 * `softwareRendered: true` and those numbers go back to being meaningless.
 *
 * Outputs into --out:
 *   shot-000.png ... shot-NNN.png   evenly spaced frames (audio-time spaced)
 *   telemetry.json                  fps, frame-time percentiles, judgements
 *   console.log                     page console + errors (non-empty = bug)
 *   audio.wav                       the game's master mix, captured in-graph
 *                                   (skip with --no-audio)
 *   video/*.webm                    with --video (Playwright, 25fps, silent)
 *   summary.json                    machine-readable verdict inputs,
 *                                   including `audio` (level + beat sync)
 */

import { mkdir, writeFile, rm } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { checkAudio, encodeWav, mono } from './audiocheck.mjs';

const argv = parseArgs(process.argv.slice(2));
const SCENE = argv.scene || 'title';
const OUT = path.resolve(argv.out || `runs/${SCENE}-${Date.now()}`);
const SECONDS = Number(argv.seconds || 14);
const SHOTS = Number(argv.shots || 12);
const W = Number(argv.width || 1280);
const H = Number(argv.height || 720);
const PLAY = argv.play || 'auto';
// --chart: the bot presses the scene's own notes (scenes that implement
// `testChart()`), one press per note, instead of every grid subdivision.
const CHART = Boolean(argv.chart);
// --skip N: play N seconds before the first shot (to look at later sections).
const SKIP = Number(argv.skip || 0);
const SOFTWARE = Boolean(argv.swiftshader);
// On a real GPU the full post chain is affordable, so show the game as a
// player sees it. SwiftShader cannot afford it (frames hit ~400ms), so the
// software path keeps the old low-tier default.
const QUALITY = argv.quality || (SOFTWARE ? 'low' : 'high');
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

// Playwright is a devDependency of web/, not of the repo root, so resolve it
// from there instead of relying on a node_modules that happens to sit above
// tools/ in one particular checkout.
const webRequire = createRequire(path.join(WEB, 'package.json'));
const pw = await import(pathToFileURL(webRequire.resolve('playwright')).href);
const chromium = pw.chromium ?? pw.default.chromium;

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
    // `--mode harness` keeps the window.__BBB__ test API in the bundle; a
    // plain production build strips it.
    // Run vite's own entry with this node rather than `npx`, which on Windows
    // is a .cmd shim that spawn() cannot launch without a shell.
    const vite = path.join(path.dirname(webRequire.resolve('vite/package.json')), 'bin', 'vite.js');
    const p = spawn(process.execPath, [vite, 'build', '--mode', 'harness', '--outDir', DIST],
      { cwd: WEB, stdio: 'pipe' });
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

/**
 * Browser launch options. `PW_CHROMIUM` still overrides the binary for
 * sandboxes that ship a pinned Chromium.
 */
function launchOptions() {
  const common = ['--autoplay-policy=no-user-gesture-required', '--mute-audio'];
  const executablePath = process.env.PW_CHROMIUM || undefined;
  if (SOFTWARE) {
    return {
      executablePath,
      headless: true,
      args: [...common, '--use-gl=swiftshader', '--enable-unsafe-swiftshader', '--disable-gpu-sandbox'],
    };
  }
  // `channel: 'chromium'` selects the full Chromium build in new-headless
  // mode, which keeps the GPU process; the default headless shell falls back
  // to SwiftShader. ANGLE's D3D11 backend is what Chrome uses on Windows.
  const gpu = ['--ignore-gpu-blocklist', '--enable-gpu'];
  if (process.platform === 'win32') gpu.push('--use-angle=d3d11');
  return {
    executablePath,
    channel: executablePath ? undefined : 'chromium',
    headless: true,
    args: [...common, ...gpu],
  };
}

// ---- audio capture ---------------------------------------------------------
// An AudioWorklet on the game's master bus. The worklet stamps its first
// block with `currentTime` — the context time that block was rendered for —
// and the player schedules every note at ctx time `clock.timeAt(beat)`, so
// captured audio and the beat grid share one clock with nothing to guess.
// --mute-audio silences the speakers, not the graph: capture still works.
const RECORDER_SRC = `
class BBBRec extends AudioWorkletProcessor {
  constructor() { super(); this.t0 = null; this.on = true; this.port.onmessage = () => { this.on = false; }; }
  process(inputs) {
    if (!this.on) return false;
    if (this.t0 === null) { this.t0 = currentTime; this.port.postMessage({ t0: currentTime, sr: sampleRate }); }
    const inp = inputs[0];
    const L = inp && inp[0] ? inp[0].slice() : new Float32Array(128);
    const R = inp && inp[1] ? inp[1].slice() : L;
    this.port.postMessage({ L, R }, [L.buffer].concat(R === L ? [] : [R.buffer]));
    return true;
  }
}
registerProcessor('bbb-rec', BBBRec);`;

async function startAudioCapture(page) {
  return page.evaluate(async (src) => {
    const a = window.__BBB__?.audio;
    if (!a?.ctx?.audioWorklet) return false;
    const url = URL.createObjectURL(new Blob([src], { type: 'text/javascript' }));
    await a.ctx.audioWorklet.addModule(url);
    const node = new AudioWorkletNode(a.ctx, 'bbb-rec', { numberOfInputs: 1, numberOfOutputs: 1, channelCount: 2, channelCountMode: 'explicit' });
    const rec = { node, t0: null, sr: a.ctx.sampleRate, L: [], R: [] };
    node.port.onmessage = (e) => {
      if (e.data.t0 !== undefined) { rec.t0 = e.data.t0; rec.sr = e.data.sr; return; }
      rec.L.push(e.data.L); rec.R.push(e.data.R);
    };
    a.master.connect(node);
    // A worklet nobody pulls from never runs; a muted path to the output
    // keeps it in the render graph without adding anything to the mix.
    const sink = a.ctx.createGain();
    sink.gain.value = 0;
    node.connect(sink).connect(a.ctx.destination);
    window.__BBB_REC__ = rec;
    return true;
  }, RECORDER_SRC);
}

/** Stop, then pull PCM (as base64 Int16) plus the beat grid the game used. */
async function stopAudioCapture(page) {
  return page.evaluate(() => {
    const rec = window.__BBB_REC__;
    if (!rec || rec.t0 === null) return null;
    rec.node.port.postMessage('stop');
    try { window.__BBB__.audio.master.disconnect(rec.node); } catch { /* already gone */ }
    const n = rec.L.reduce((s, b) => s + b.length, 0);
    const pack = (bufs) => {
      const out = new Int16Array(n);
      let o = 0;
      for (const b of bufs) for (let i = 0; i < b.length; i++) out[o++] = Math.max(-32768, Math.min(32767, Math.round(b[i] * 32767)));
      let s = '';
      const bytes = new Uint8Array(out.buffer);
      for (let i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
      return btoa(s);
    };
    const c = window.__BBB__.clock;
    const t1 = rec.t0 + n / rec.sr;
    const beats = [];
    if (c.running !== false) {
      for (let b = Math.ceil(c.beatAt(rec.t0)); b <= Math.floor(c.beatAt(t1)); b++) beats.push(c.timeAt(b));
    }
    return { t0: rec.t0, sr: rec.sr, n, bpm: c.bpm, L: pack(rec.L), R: pack(rec.R), beatTimes: beats };
  });
}

function decodePcm(b64) {
  const bytes = Buffer.from(b64, 'base64');
  const i16 = new Int16Array(bytes.buffer, bytes.byteOffset, bytes.byteLength / 2);
  const f = new Float32Array(i16.length);
  for (let i = 0; i < i16.length; i++) f[i] = i16[i] / 32768;
  return f;
}

// pid-derived so concurrent harness runs never fight over a port.
const PORT = Number(argv.port || 5321 + (process.pid % 900));

(async () => {
  await ensureBuild();
  if (existsSync(OUT)) await rm(OUT, { recursive: true, force: true });
  await mkdir(OUT, { recursive: true });

  const server = await serve(path.join(WEB, DIST), PORT);

  const browser = await chromium.launch(launchOptions());
  const context = await browser.newContext({
    viewport: { width: W, height: H },
    deviceScaleFactor: 1,
    recordVideo: argv.video ? { dir: path.join(OUT, 'video'), size: { width: W, height: H } } : undefined,
  });
  const page = await context.newPage();

  const logs = [];
  // Scripts the HOST MACHINE injects into every page (antivirus web-scanning
  // on the dev laptop rewrites localhost HTML to pull its own script). Not
  // the game's requests: aborted silently, and the console error the abort
  // produces is dropped. Anything else off-origin is still flagged below.
  const ENV_INJECTED = /kaspersky-labs\.com/i;
  page.on('console', (m) => {
    if (ENV_INJECTED.test(m.location()?.url || '')) return;
    logs.push(`[${m.type()}] ${m.text()}`);
  });
  page.on('pageerror', (e) => logs.push(`[pageerror] ${e.message}\n${e.stack || ''}`));
  page.on('requestfailed', (r) => {
    if (!r.url().startsWith(`http://127.0.0.1:${PORT}/`)) return; // reported as [external-fetch]
    logs.push(`[requestfailed] ${r.url()} ${r.failure()?.errorText}`);
  });
  // ADR 0003: the game fetches only its own bundled files. Anything bound for
  // another origin is aborted (so the run behaves as it would offline) and
  // logged, which makes the console dirty and fails the sweep.
  await context.route('**/*', (route) => {
    const url = route.request().url();
    if (url.startsWith(`http://127.0.0.1:${PORT}/`)) return route.continue();
    if (!ENV_INJECTED.test(url)) logs.push(`[external-fetch] ${url}`);
    return route.abort('blockedbyclient');
  });

  await page.goto(`http://127.0.0.1:${PORT}/index.html?scene=${encodeURIComponent(SCENE)}&quality=${encodeURIComponent(QUALITY)}`, {
    waitUntil: 'load', timeout: 30000,
  });

  // Real gesture so the AudioContext is allowed to run.
  await page.mouse.move(W / 2, H / 2);
  await page.keyboard.press('KeyQ');

  await page.waitForFunction('window.__BBB__ && window.__BBB__.ready', null, { timeout: 20000 });
  await page.evaluate(() => window.__BBB__.ready);

  // What is actually drawing the frames. Read from the game's own canvas
  // context so it can never disagree with what the stage renders through.
  // A lost stage context answers null, so fall back to a scratch canvas
  // (same browser, same GPU process) rather than report nothing.
  const gpu = await page.evaluate(() => {
    const ask = (c) => {
      const gl = c && (c.getContext('webgl2') || c.getContext('webgl'));
      if (!gl) return null;
      const ext = gl.getExtension('WEBGL_debug_renderer_info');
      return ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER);
    };
    return ask(document.getElementById('stage')) || ask(document.createElement('canvas')) || 'unknown';
  });
  const softwareRendered = SOFTWARE || /swiftshader|llvmpipe|software/i.test(gpu);

  await page.evaluate((q) => window.__BBB__.setQuality?.(q), QUALITY);
  await page.evaluate((s) => window.__BBB__.goto(s), SCENE);
  // Let the freshly loaded scene draw a couple of settled frames before the
  // clock-based capture starts.
  await page.evaluate(() => window.__BBB__.screenshotReady());
  await page.evaluate(() => window.__BBB__.resetTelemetry());
  const capturing = !argv['no-audio'] && await startAudioCapture(page).catch((e) => {
    logs.push(`[harness] audio capture unavailable: ${e.message}`);
    return false;
  });

  // ---- autoplay ---------------------------------------------------------
  // Drives synthetic presses at exact audio times. `perfect` proves the game
  // is beatable and shows the top-end feedback; `sloppy` shows what a real
  // human's mediocre run looks like, which is what most players will see.
  // The bot runs until capture ends (stopped explicitly below): a dense
  // capture can fall behind its audio-time slots — a PNG takes ~0.3s — and a
  // bot that quit on schedule would leave the tail full of false misses.
  if (PLAY !== 'none') {
    await page.evaluate(({ mode, secs, actions, division, chart }) => window.__BBB__.autoplay({ mode, seconds: secs, actions, division, chart }),
      { mode: PLAY, secs: 600, actions: ACTIONS, division: DIVISION, chart: CHART });
  }

  // ---- capture ----------------------------------------------------------
  // Shots are spaced in AUDIO time (Clock.now()), not wall-clock sleeps: the
  // game's own time domain decides when frame i is taken, so a slow
  // screenshot can delay the next shot but never shift what it shows.
  const shots = [];
  const t0 = SKIP + await page.evaluate(() => window.__BBB__.clock.now());
  const interval = SECONDS / SHOTS;
  for (let i = 0; i < SHOTS; i++) {
    const target = t0 + interval * (i + 1);
    await page.waitForFunction((t) => window.__BBB__.clock.now() >= t, target,
      { polling: 'raf', timeout: (interval + SKIP + 15) * 1000 });
    await page.evaluate(() => window.__BBB__.screenshotReady());
    const f = path.join(OUT, `shot-${String(i).padStart(3, '0')}.png`);
    await page.screenshot({ path: f });
    shots.push(f);
  }

  await page.evaluate(() => window.__BBB__.stopAutoplay?.());

  // ---- audio: WAV + automatic check -------------------------------------
  let audio = null;
  const cap = capturing ? await stopAudioCapture(page) : null;
  if (cap) {
    const L = decodePcm(cap.L), R = decodePcm(cap.R);
    await writeFile(path.join(OUT, 'audio.wav'), encodeWav([L, R], cap.sr));
    audio = {
      file: 'audio.wav',
      seconds: Math.round((cap.n / cap.sr) * 100) / 100,
      bpm: cap.bpm,
      beats: cap.beatTimes.length,
      ...checkAudio({ samples: mono([L, R]), sampleRate: cap.sr, startTime: cap.t0, beatTimes: cap.beatTimes }),
    };
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
    chart: CHART,
    skip: SKIP,
    quality: QUALITY,
    actions: ACTIONS,
    division: DIVISION,
    seconds: SECONDS,
    shots: shots.map((s) => path.relative(OUT, s)),
    // NOTE FOR CRITICS: when `softwareRendered` is true the frames came from
    // SwiftShader and `fps`/`frameMs` say nothing about real hardware — judge
    // performance from `cpuMs` and `render` instead. On the default GPU path
    // `gpu` names the real adapter and all four numbers are meaningful.
    gpu,
    softwareRendered,
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
    // Captured mix vs the game's beat grid (tools/harness/audiocheck.mjs).
    // `pass` = audible AND onsets sit on a beat/8th/16th grid. A person
    // should still listen to audio.wav — this proves sync, not taste.
    audio,
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
