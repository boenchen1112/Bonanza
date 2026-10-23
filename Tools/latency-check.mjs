// Usage: node Tools/latency-check.mjs <buildDir>
//   node Tools/latency-check.mjs Builds/WebGL
// Checks the browser bridge (Plugins/BonanzaBrowser.jslib) against a build, with a fake AudioContext.outputLatency
// (Object.defineProperty on the prototype before the page loads). Four fresh pages, one after the other:
//   A  outputLatency = 0.060 from the start. Autoplay swing-kings must be rank S, miss 0; the logged applied latency
//      is 60 ms (before the round the estimate is taken at once, like the web's refresh while the transport is idle).
//   B  outputLatency 0 -> 0.060 in the middle of the round, page opened with ?latencySlew=fast (the 2 ms-per-refresh
//      slew is unchanged, but it refreshes every 0.1 s instead of every second, so 60 ms takes ~3 s instead of
//      ~30 s). The applied latency must climb in steps of at most 2 ms, through intermediate values, to ~58-60 ms,
//      and the round is still S / miss 0 (the bot presses in the same latency-compensated time the game runs in).
//   C  outputLatency = 0: applied latency 0 and biasMs in the usual band, and within 3 ms of A's (the bot's press
//      times do not depend on the latency; biasMs is the 60 Hz frame quantisation, ~8 ms at a 960x600 canvas).
//   D  audio re-arm: the context is suspended mid-round ("OS interruption"). The game must say so ([AUDIO] lost),
//      pause, and resume the audio on the next real gesture ([AUDIO] resumed, ctx running again).
// Parses the build's [LATENCY], [AUDIO] and [RESULT] console lines. Same server and Playwright setup as
// webgl-check.mjs. Exits 1 on a failed assertion. About 6 minutes.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright';

const DIR = process.argv[2];
const PORT = Number(process.env.PORT || 7964);
const TYPES = { '.html': 'text/html', '.js': 'application/javascript', '.css': 'text/css',
  '.wasm': 'application/wasm', '.data': 'application/octet-stream', '.png': 'image/png', '.ico': 'image/x-icon' };

const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]);
  if (p === '/') p = '/index.html';
  const f = path.join(DIR, p);
  if (!fs.existsSync(f)) { res.writeHead(404); res.end(); return; }
  const headers = {};
  let ext = path.extname(f);
  if (ext === '.br') { headers['Content-Encoding'] = 'br'; ext = path.extname(f.slice(0, -3)); }
  headers['Content-Type'] = TYPES[ext] || 'application/octet-stream';
  res.writeHead(200, headers);
  fs.createReadStream(f).pipe(res);
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));
const browser = await chromium.launch({ headless: true, args: ['--use-angle=d3d11', '--mute-audio'] });
const wait = (s) => new Promise((r) => setTimeout(r, s * 1000));

let fails = 0;
const check = (name, ok, detail = '') => { console.log(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ' ' + detail : ''}`); if (!ok) fails++; };

// Opens a page with outputLatency faked to `initial` seconds (window.__fakeLatency, changeable later).
async function open(query, initial) {
  const page = await browser.newPage({ viewport: { width: 960, height: 600 } });
  const run = { page, errors: [], latency: [], audio: [], result: null, t0: Date.now() };
  page.on('pageerror', (e) => run.errors.push('pageerror: ' + e.message));
  page.on('console', (m) => {
    const t = m.text();
    if (m.type() === 'error') run.errors.push('console: ' + t);
    if (t.startsWith('[LATENCY]')) {
      const a = t.match(/applied=([\d.-]+)ms target=([\d.-]+)ms out=([\d.-]+)ms base=([\d.-]+)ms ctx=(\d)/);
      if (a) run.latency.push({ at: (Date.now() - run.t0) / 1000, applied: +a[1], target: +a[2], out: +a[3], base: +a[4], ctx: +a[5] });
    }
    if (t.startsWith('[AUDIO]')) run.audio.push({ at: (Date.now() - run.t0) / 1000, text: t });
    if (t.includes('[RESULT]')) run.result = t;
  });
  await page.addInitScript((v) => {
    window.__fakeLatency = v;
    Object.defineProperty(AudioContext.prototype, 'outputLatency', { configurable: true, get() { return window.__fakeLatency; } });
  }, initial);
  await page.goto(`http://127.0.0.1:${PORT}/${query}`, { waitUntil: 'load' });
  await page.waitForFunction(() => { const b = document.querySelector('#unity-loading-container'); return b && getComputedStyle(b).display === 'none'; }, null, { timeout: 60000 });
  await wait(1.5);
  await page.mouse.click(480, 300);   // the gesture that unlocks audio
  run.t0 = Date.now();
  return run;
}
async function untilResult(run, seconds = 200) {
  const t1 = Date.now();
  while (!run.result && Date.now() - t1 < seconds * 1000) await wait(0.5);
  return run.result;
}
const field = (r, k) => { const m = r && r.match(new RegExp(k + '=([\\w.-]+)')); return m ? m[1] : null; };
const last = (a) => a[a.length - 1];

// ---------------------------------------------------------------- A: constant 60 ms
let A = await open('?autoplay&game=swing-kings', 0.060);
await untilResult(A);
console.log('A', A.result, JSON.stringify(last(A.latency) || null));
check('A: round finished', !!A.result);
check('A: rank S, miss 0', field(A.result, 'rank') === 'S' && field(A.result, 'miss') === '0');
check('A: the context was found', A.latency.length > 0 && last(A.latency).ctx > 0, `ctx=${last(A.latency)?.ctx} (1 = Unity WEBAudio.audioContext, 2 = template hook)`);
check('A: applied latency is 60 ms', A.latency.length > 0 && Math.abs(last(A.latency).applied - 60) < 2.1 && Math.abs(last(A.latency).out - 60) < 0.5, JSON.stringify(last(A.latency)));
check('A: no page errors', A.errors.length === 0, A.errors.join(' | '));
const biasA = Number(field(A.result, 'biasMs'));
await A.page.close();

// ---------------------------------------------------------------- B: 0 -> 60 ms during the round, fast refresh
let B = await open('?autoplay&game=swing-kings&latencySlew=fast', 0);
await wait(20);                               // well inside the round (count-in is a few seconds)
const nBefore = B.latency.length;
await B.page.evaluate(() => { window.__fakeLatency = 0.060; });
await untilResult(B);
console.log('B', B.result);
const after = B.latency.slice(nBefore).map((l) => l.applied);
const seq = [last(B.latency.slice(0, nBefore))?.applied ?? 0, ...after];
const steps = seq.slice(1).map((v, i) => v - seq[i]);
console.log('B applied sequence (ms):', seq.map((v) => v.toFixed(1)).join(' '));
check('B: round finished, rank S, miss 0', field(B.result, 'rank') === 'S' && field(B.result, 'miss') === '0');
check('B: started from ~0 ms', Math.abs(seq[0]) < 0.5, `seq[0]=${seq[0]}`);
check('B: slewed through intermediate values', after.filter((v) => v > 1 && v < 57).length >= 5, `${after.length} changes`);
check('B: no step above 2 ms', steps.length > 0 && Math.max(...steps) <= 2.15 && Math.min(...steps) >= -0.05, `max step ${Math.max(...steps).toFixed(2)} ms`);
check('B: converged to ~60 ms', Math.abs(last(B.latency).applied - 60) <= 2.1, `applied=${last(B.latency).applied}`);
check('B: no page errors', B.errors.length === 0, B.errors.join(' | '));
await B.page.close();

// ---------------------------------------------------------------- C: 0 ms
let C = await open('?autoplay&game=swing-kings', 0);
await untilResult(C);
console.log('C', C.result);
const biasC = Number(field(C.result, 'biasMs'));
check('C: rank S, miss 0', field(C.result, 'rank') === 'S' && field(C.result, 'miss') === '0');
check('C: applied latency stays 0', C.latency.length > 0 && last(C.latency).applied === 0, JSON.stringify(last(C.latency)));
check('C: biasMs in the usual band (5-11 ms at 960x600)', biasC >= 5 && biasC <= 11, `biasMs=${biasC}`);
check('C: biasMs within 3 ms of the 60 ms run', Math.abs(biasC - biasA) < 3, `0 ms: ${biasC}, 60 ms: ${biasA}`);
check('C: no page errors', C.errors.length === 0, C.errors.join(' | '));
await C.page.close();

// ---------------------------------------------------------------- D: the context is suspended mid-round
let D = await open('?autoplay&game=swing-kings', 0.030);
await wait(15);
await D.page.evaluate(() => { for (const c of window.__bonanzaAudioCtxs) c.suspend(); });
await wait(2.5);
check('D: lost is reported', D.audio.some((a) => /lost/.test(a.text)), JSON.stringify(D.audio));
await D.page.screenshot({ path: 'Captures/latency-audio-lost.png' });
const stateWhileLost = await D.page.evaluate(() => window.__bonanzaAudioCtxs.map((c) => c.state).join(','));
check('D: the context stays suspended until a gesture', stateWhileLost.includes('suspended'), stateWhileLost);
await D.page.mouse.click(480, 300);           // the player taps
await wait(2);
const stateAfter = await D.page.evaluate(() => window.__bonanzaAudioCtxs.map((c) => c.state).join(','));
check('D: the next gesture resumes the context', stateAfter.split(',').every((s) => s === 'running' || s === 'closed'), stateAfter);
check('D: resumed is reported', D.audio.some((a) => /resumed/.test(a.text)), JSON.stringify(D.audio));
await D.page.screenshot({ path: 'Captures/latency-audio-resumed.png' });
check('D: no page errors', D.errors.length === 0, D.errors.join(' | '));
await D.page.close();

await browser.close();
server.close();
console.log(fails ? `FAILED: ${fails}` : 'latency-check: all passed');
process.exit(fails ? 1 : 0);
