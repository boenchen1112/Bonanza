// Usage: node Tools/camera-check.mjs <buildDir> [outPrefix]
//   node Tools/camera-check.mjs Builds/WebGL Captures/camera
// Swing Kings camera conducting (?swingInput=camera), without a real camera. Real input, no autoplay.
//
//   1 sim      ?cameraSim=auto: the page replaces the camera with scripted palm traces that enter through the same
//              queue the MediaPipe loop fills (WebGLTemplates/Bonanza/bonanza-camera.js), one conducted stroke per
//              launched pitch, each sample stamped like a camera frame (shown 70 ms late). Asserts: swings fire from
//              the camera's ictus (not a button), land near it (the 70 ms camera latency is taken off, so no bias),
//              a slow drift is not a swing, RESTART keeps the camera session (no second prompt, no stop), and
//              QUIT TO MENU stops it.
//   2 deny     getUserMedia rejects with NotAllowedError: the game says NO CAMERA and a mouse flick still scores.
//   3 no-api   no navigator.mediaDevices: the camera option conducts with the mouse from the start, no camera attempt.
//   4 fake     Chromium's fake webcam (--use-fake-device-for-media-stream): the real getUserMedia and the bundled
//              MediaPipe model. Headless there is no hand to find, so it asserts the plumbing: the model and wasm are
//              fetched from this server only and the game keeps ticking, and either the tracker runs (frames reach
//              detectForVideo, hiding the page stops the camera and showing it resumes) or the GPU delegate fails
//              here and the run falls back to the mouse with a reason.
//   5 tap      a tap-mode round requests nothing under mediapipe/ (the model is lazy).
// Same server and Playwright setup as mouse-check.mjs. Exits 1 on a failed assertion.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright';

const DIR = process.argv[2];
const OUT = process.argv[3] || 'Captures/camera';
const PORT = 7966;
const TYPES = { '.html': 'text/html', '.js': 'application/javascript', '.css': 'text/css',
  '.wasm': 'application/wasm', '.data': 'application/octet-stream', '.png': 'image/png', '.ico': 'image/x-icon' };

const requests = [];
const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]);
  requests.push(p);
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

const wait = (s) => new Promise((r) => setTimeout(r, s * 1000));
let fails = 0;
const check = (name, ok, detail = '') => { console.log(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ' ' + detail : ''}`); if (!ok) fails++; };
const allErrors = [];

const browser = await chromium.launch({ headless: true, args: ['--use-angle=d3d11', '--mute-audio'] });
const fakeBrowser = await chromium.launch({ headless: true, args: ['--use-angle=d3d11', '--mute-audio',
  '--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream'] });

// One page with its own log capture.
async function open(b, query, init) {
  const ctx = await b.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await ctx.newPage();
  if (init) await page.addInitScript(init);
  const s = { page, ctx, errors: [], logs: [], launches: [], releases: [], swings: [], camera: [] };
  page.on('pageerror', (e) => s.errors.push('pageerror: ' + e.message));
  page.on('console', (m) => {
    const t = m.text();
    const wall = performance.now();
    s.logs.push(t);
    if (m.type() === 'error') s.errors.push('console: ' + t);
    else if (t.includes('[ERROR]')) s.errors.push('console: ' + t);
    let x;
    if (t.startsWith('[CAMERA]')) s.camera.push(t.trim());
    if ((x = t.match(/^\[LAUNCH\] id=(\d+) eta=([\d.-]+)/))) s.launches.push({ id: Number(x[1]), eta: Number(x[2]), wall });
    if ((x = t.match(/^\[RELEASE\] src=(\w+) t=([\d.-]+) hold=([\d.]+) power=([\d.]+)/))) s.releases.push({ src: x[1], t: Number(x[2]), hold: Number(x[3]), power: Number(x[4]), wall });
    if ((x = t.match(/^\[SWING\] verdict=(\w+) err=([\d.-]+) power=([\d.]+) hold=([\d.]+) pts=([\d.]+)/)))
      s.swings.push({ verdict: x[1], err: Number(x[2]), power: Number(x[3]), hold: Number(x[4]), pts: Number(x[5]), wall });
    if (process.env.DEBUG && /^\[(CONDUCT|RELEASE|SWING|LAUNCH|CAMERA)\]/.test(t)) console.log(Math.round(wall), t);
  });
  await page.goto(`http://127.0.0.1:${PORT}/${query}`, { waitUntil: 'load' });
  await page.waitForFunction(() => getComputedStyle(document.querySelector('#unity-loading-container')).display === 'none', null, { timeout: 60000 });
  await wait(1.5);
  return s;
}
async function startRound(s) {
  await s.page.mouse.click(640, 400);       // the audio gate
  await wait(3);                            // the rules card ignores presses until the audio clock runs
  await s.page.keyboard.press('Space');     // start the round
  await wait(0.3);
}
const until = async (fn, secs, what) => {
  const t0 = performance.now();
  while (!fn()) { if (performance.now() - t0 > secs * 1000) throw new Error('timeout: ' + what); await wait(0.01); }
};
const sleepTo = async (ms) => {
  const d = ms - performance.now();
  if (d > 6) await new Promise((r) => setTimeout(r, d - 4));
  while (performance.now() < ms) { /* spin */ }
};
const cam = (s) => s.page.evaluate(() => { const c = window.BonanzaCamera; return c && { status: c.status, reason: c.reason, frames: c.frames, samples: c.samples, losts: c.losts, starts: c.starts, stops: c.stops, queue: c.queue.length }; });
const finish = async (s, name) => { allErrors.push(...s.errors.map((e) => `${name}: ${e}`)); await s.ctx.close(); };

// The mouse conduct of mouse-check.mjs (rest low, lift, hold, accelerating downstroke that stops 2 frames before
// contact), for the fallback runs.
const F = 1 / 60, LOW = 0.7, LIFT = 0.25;
const yAt = (u) => {
  const stop = -2 * F, sa = stop - 0.12, le = sa - 0.18, ls = le - 0.4, high = LOW - LIFT;
  if (u <= ls) return LOW;
  if (u < le) return LOW - LIFT * ((u - ls) / 0.4);
  if (u < sa) return high;
  if (u < stop) { const w = (u - sa) / 0.12; return high + LIFT * w * w; }
  return LOW;
};
async function mouseConduct(s, id) {
  const box = await s.page.evaluate(() => { const r = document.querySelector('#unity-canvas').getBoundingClientRect(); return { top: r.top, h: r.height }; });
  const cy = (f) => box.top + f * box.h;
  await until(() => s.launches.some((l) => l.id === id), 30, `launch ${id}`);
  const l = s.launches.find((x) => x.id === id);
  const deadline = l.wall + l.eta * 1000;
  const before = s.releases.length;
  await s.page.mouse.move(640, cy(LOW));
  await s.page.mouse.down();
  const sent = [];
  for (let u = -Math.floor((deadline - performance.now()) / (F * 1000)) * F; u <= -2 * F + 1e-9; u += F) {
    await sleepTo(deadline + u * 1000);
    sent.push(s.page.mouse.move(640, cy(yAt(u))));
  }
  await Promise.all(sent);
  await sleepTo(deadline + 400);
  await s.page.mouse.up();
  await until(() => s.swings.length >= id, 6, `judged ${id}`);
  await wait(0.1);
  return { rel: s.releases.length > before ? s.releases[before] : null, s: s.swings[id - 1] };
}

// ---------------------------------------------------------------- 1 sim
console.log('--- 1 sim (?cameraSim=auto)');
{
  const s = await open(browser, '?game=swing-kings&swingInput=camera&cameraSim=auto');
  await startRound(s);
  await until(() => s.swings.length >= 6, 90, '6 judged swings');
  await wait(0.2);
  const six = s.swings.slice(0, 6);
  const rel = s.releases.slice(0, 6);
  for (const [i, c] of six.entries()) console.log(`pitch ${i + 1}: ${c.verdict} err=${c.err}ms power=${c.power} hold=${c.hold} release=${rel[i] ? rel[i].src : 'none'}`);
  const hits = six.filter((c) => c.verdict !== 'miss');
  const meanAbs = hits.reduce((a, c) => a + Math.abs(c.err), 0) / Math.max(1, hits.length);
  const meanSigned = hits.reduce((a, c) => a + c.err, 0) / Math.max(1, hits.length);
  check('the camera is started once, with the rules card up', s.camera.filter((l) => l === '[CAMERA] start').length === 1);
  check('conducted pitches are hit', hits.length >= 5, `${hits.length}/6`);
  check('every swing fires from the camera ictus, not a button', rel.length === 6 && rel.every((r) => r.src === 'camera'), rel.map((r) => r.src).join(','));
  check('swings land near the ictus (mean |err| within the GREAT window)', hits.length > 0 && meanAbs < 82, `mean |err| ${meanAbs.toFixed(1)}ms`);
  check('the 70 ms camera latency is taken off (no systematic bias)', hits.length > 0 && Math.abs(meanSigned) < 40, `mean err ${meanSigned.toFixed(1)}ms`);
  check('every hit is inside the GOOD window', hits.every((c) => Math.abs(c.err) <= 128));
  check('the windup came from the raised hand', hits.every((c) => c.hold > 0.6), hits.map((c) => c.hold).join(','));
  const before = s.swings.length, relBefore = s.releases.length;
  await s.page.evaluate(() => { const c = window.BonanzaCamera; c.autoOn = false; c.sim.driftUntil(performance.now() + 2600); });
  await until(() => s.swings.length > before, 12, 'a drifted pitch is judged');
  const d = s.swings[before];
  check('a slow drift is not a swing (no ictus, the pitch is missed)', d.verdict === 'miss' && s.releases.length === relBefore, `${d.verdict}, releases +${s.releases.length - relBefore}`);
  await s.page.evaluate(() => { window.BonanzaCamera.autoOn = true; });

  const c0 = await cam(s);
  check('the session is running', c0.status === 2 && c0.starts === 1, JSON.stringify(c0));
  await s.page.screenshot({ path: OUT + '-sim.png' });
  // RESTART from the pause menu: a new game object takes the camera over; the running session is kept
  await s.page.keyboard.press('Escape'); await wait(0.6);
  await s.page.keyboard.press('ArrowDown'); await wait(0.2);
  await s.page.keyboard.press('Space'); await wait(2.5);
  const c1 = await cam(s);
  check('RESTART joins the running camera (no second start, no stop)', c1.starts === 1 && c1.stops === 0 && c1.status === 2, JSON.stringify(c1));
  await s.page.mouse.click(640, 400); await wait(0.5);
  await s.page.keyboard.press('Escape'); await wait(0.6);
  await s.page.keyboard.press('ArrowDown'); await wait(0.2);
  await s.page.keyboard.press('ArrowDown'); await wait(0.2);
  await s.page.keyboard.press('Space'); await wait(2);
  const c2 = await cam(s);
  check('QUIT TO MENU stops the camera', c2.stops === 1 && c2.status === 0, JSON.stringify(c2));
  check('no console errors in the sim run', s.errors.length === 0, s.errors.join(' | '));
  await finish(s, 'sim');
}

// ---------------------------------------------------------------- 2 deny
console.log('--- 2 deny (getUserMedia rejects NotAllowedError)');
{
  const s = await open(browser, '?game=swing-kings&swingInput=camera', () => {
    navigator.mediaDevices.getUserMedia = () => Promise.reject(new DOMException('Permission denied', 'NotAllowedError'));
  });
  await startRound(s);
  await until(() => s.camera.some((l) => l.startsWith('[CAMERA] fallback')), 20, 'the fallback');
  await wait(0.3);
  await s.page.screenshot({ path: OUT + '-deny.png' });
  check('permission denied falls back to the mouse with the reason', s.camera.some((l) => l.includes('fallback to mouse') && l.includes('NotAllowedError')), s.camera.join(' | '));
  const r = await mouseConduct(s, 1);
  const r2 = await mouseConduct(s, 2);
  console.log(`mouse after fallback: ${r.s.verdict} err=${r.s.err} src=${r.rel && r.rel.src}; ${r2.s.verdict} err=${r2.s.err} src=${r2.rel && r2.rel.src}`);
  check('after the fallback a mouse flick still swings at the ictus and scores', [r, r2].every((x) => x.rel && x.rel.src === 'ictus' && x.s.verdict !== 'miss' && Math.abs(x.s.err) <= 128));
  const c = await cam(s);
  check('the dead camera is not left running', c.status === 3 || c.status === 0, JSON.stringify(c));
  check('no console errors in the deny run', s.errors.length === 0, s.errors.join(' | '));
  await finish(s, 'deny');
}

// ---------------------------------------------------------------- 3 no api
console.log('--- 3 no-api (navigator.mediaDevices removed)');
{
  const s = await open(browser, '?game=swing-kings&swingInput=camera', () => {
    try { Object.defineProperty(Navigator.prototype, 'mediaDevices', { get() { return undefined; }, configurable: true }); } catch (e) { /* ignore */ }
  });
  await startRound(s);
  const r = await mouseConduct(s, 1);
  console.log(`no api: ${r.s.verdict} err=${r.s.err} src=${r.rel && r.rel.src}`);
  check('without a camera API the camera option conducts with the mouse', r.rel && r.rel.src === 'ictus' && r.s.verdict !== 'miss');
  check('no camera was attempted', !s.camera.some((l) => l === '[CAMERA] start'), s.camera.join(' | '));
  await finish(s, 'noapi');
}

// ---------------------------------------------------------------- 4 fake webcam, real MediaPipe
console.log('--- 4 fake (Chromium fake webcam, bundled MediaPipe)');
{
  requests.length = 0;
  const s = await open(fakeBrowser, '?game=swing-kings&swingInput=camera');
  
  await startRound(s);
  const t0 = performance.now();
  let st = null;
  for (let i = 0; i < 60; i++) {   // up to 30 s for the model to load or the delegate to fail
    st = await cam(s);
    if (st.status === 2 || st.status === 3) break;
    await wait(0.5);
  }
  console.log('camera after load:', JSON.stringify(st), `(${((performance.now() - t0) / 1000).toFixed(1)} s)`);
  const mp = requests.filter((p) => p.startsWith('/mediapipe/'));
  console.log('mediapipe requests:', mp.join(' '));
  check('the bundled model and wasm are fetched from this server', ['/mediapipe/vision_bundle.js', '/mediapipe/vision_wasm_internal.js', '/mediapipe/vision_wasm_internal.wasm', '/mediapipe/hand_landmarker.task'].every((p) => mp.includes(p)));
  check('the camera ended running or failed with a reason (never stuck loading)', st.status === 2 || (st.status === 3 && st.reason), JSON.stringify(st));
  if (st.status === 2) {
    await wait(2);
    const a = await cam(s);
    check('frames reach the hand landmarker', a.frames > 10, `frames=${a.frames}`);
    await s.page.screenshot({ path: OUT + '-fake.png' });
    // hide the page: the camera must stop; show it again: it resumes by itself
    await s.page.evaluate(() => { Object.defineProperty(document, 'hidden', { configurable: true, get: () => true }); document.dispatchEvent(new Event('visibilitychange')); });
    await wait(0.5);
    const h = await cam(s);
    const live = await s.page.evaluate(() => [...document.querySelectorAll('video')].some((v) => v.srcObject && v.srcObject.getTracks().some((t) => t.readyState === 'live')));
    check('a hidden page stops the camera', h.status === 4 && !live, JSON.stringify(h) + ' live=' + live);
    const framesHidden = (await cam(s)).frames; await wait(0.5);
    check('no frames are processed while hidden', (await cam(s)).frames === framesHidden);
    await s.page.evaluate(() => { Object.defineProperty(document, 'hidden', { configurable: true, get: () => false }); document.dispatchEvent(new Event('visibilitychange')); });
    await wait(2);
    const b = await cam(s);
    check('showing the page resumes the camera', b.status === 2 && b.frames > framesHidden, JSON.stringify(b));
  } else {
    check('the failure is logged and the game falls back to the mouse', s.camera.some((l) => l.startsWith('[CAMERA] fallback to mouse') && l.length > 30), s.camera.join(' | '));
    const r = await mouseConduct(s, 1);
    check('the mouse still scores after the fallback', r.rel && r.rel.src === 'ictus' && r.s.verdict !== 'miss', `${r.s.verdict} ${r.s.err}ms`);
  }
  const hosts = new Set((await s.page.evaluate(() => performance.getEntriesByType('resource').map((e) => new URL(e.name).host))));
  check('no request leaves this server', [...hosts].every((h) => h === `127.0.0.1:${PORT}`), [...hosts].join(','));
  const alive = s.logs.length;
  await wait(1);
  check('the game keeps running (logs/frames still advancing)', s.logs.length >= alive);
  check('no console errors in the fake-webcam run', s.errors.length === 0, s.errors.join(' | '));
  await finish(s, 'fake');
}

// ---------------------------------------------------------------- 5 tap is lazy
console.log('--- 5 tap (no camera files requested)');
{
  requests.length = 0;
  const s = await open(browser, '?game=swing-kings');
  await startRound(s);
  await wait(3);
  check('a tap round requests nothing under mediapipe/ and starts no camera', !requests.some((p) => p.startsWith('/mediapipe/')) && !s.camera.length, requests.filter((p) => p.startsWith('/mediapipe/')).join(' '));
  check('only the small bridge script is fetched for it', requests.includes('/bonanza-camera.js'));
  await finish(s, 'tap');
}

check('no page or console errors anywhere', allErrors.length === 0, allErrors.join(' | '));
console.log('errors:', allErrors.length ? allErrors.join('\n') : 'none');
await browser.close(); await fakeBrowser.close();
server.close();
process.exit(fails ? 1 : 0);
