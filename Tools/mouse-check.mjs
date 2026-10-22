// Usage: node Tools/mouse-check.mjs <buildDir> [shot.png]
//   node Tools/mouse-check.mjs Builds/WebGL Captures/mouse.png
// Swing Kings mouse conducting through the real pointer path (?swingInput=mouse, no autoplay). Playwright's
// mouse plays scripted conducts against the live round: it waits for the game's "[LAUNCH] id=n eta=s" line
// (seconds until contact), presses, rests, lifts, holds and flicks DOWN so that the stroke stops two frames
// before the contact time (the detector reports the ictus two samples after the stop), then keeps the button
// held. The game logs "[SWING] verdict=.. err=.. power=.. hold=.. pts=..". Asserts:
//   - six conducted pitches: swings fire BEFORE the button is released (the ictus, not the button-up),
//     land near the ictus (|err| within the GREAT window, mean well inside it), at home-run power from the hold
//   - a hold released with no pointer motion swings on the button-up
//   - a slow drift never fires a swing (the pitch is not hit)
//   - the round scores and nothing logs an error
// Same server and Playwright setup as input-check.mjs. Exits 1 on a failed assertion.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright';

const DIR = process.argv[2];
const SHOT = process.argv[3];
const PORT = 7964;
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
const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
const errors = [];
const launches = [];   // { id, eta, wall }
const releases = [];   // { src, t, hold, power, wall }
const gestures = [];
const swings = [];     // { verdict, err, power, hold, pts, wall }
page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
page.on('console', (m) => {
  const t = m.text();
  const wall = performance.now();
  if (m.type() === 'error') errors.push('console: ' + t);
  else if (t.includes('[ERROR]')) errors.push('console: ' + t);
  let x;
  if ((x = t.match(/^\[LAUNCH\] id=(\d+) eta=([\d.-]+)/))) launches.push({ id: Number(x[1]), eta: Number(x[2]), wall });
  if ((x = t.match(/^\[RELEASE\] src=(\w+) t=([\d.-]+) hold=([\d.]+) power=([\d.]+)/))) releases.push({ src: x[1], t: Number(x[2]), hold: Number(x[3]), power: Number(x[4]), wall });
  if (process.env.INPUTLOG && t.startsWith('[INPUT] move')) console.log(Math.round(wall), t);
  if (process.env.DEBUG && /^\[(CONDUCT|RELEASE|SWING|LAUNCH)\]/.test(t)) console.log(Math.round(wall), t);
  if ((x = t.match(/^\[SWING\] verdict=(\w+) err=([\d.-]+) power=([\d.]+) hold=([\d.]+) pts=([\d.]+)/)))
    swings.push({ verdict: x[1], err: Number(x[2]), power: Number(x[3]), hold: Number(x[4]), pts: Number(x[5]), wall });
});
const wait = (s) => new Promise((r) => setTimeout(r, s * 1000));
const until = async (fn, secs, what) => {
  const t0 = performance.now();
  while (!fn()) { if (performance.now() - t0 > secs * 1000) throw new Error('timeout: ' + what); await wait(0.01); }
};
// Sleep to a wall-clock time (ms on performance.now): coarse timer, then a short spin.
const sleepTo = async (ms) => {
  let d = ms - performance.now();
  if (d > 6) await new Promise((r) => setTimeout(r, d - 4));
  while (performance.now() < ms) { /* spin */ }
};

await page.goto(`http://127.0.0.1:${PORT}/?game=swing-kings&swingInput=mouse${process.env.INPUTLOG ? '&inputlog' : ''}`, { waitUntil: 'load' });
await page.waitForFunction(() => getComputedStyle(document.querySelector('#unity-loading-container')).display === 'none', null, { timeout: 60000 });
await page.evaluate(() => { window.__gaps = []; let p = performance.now(); const f = (t) => { if (t - p > 60) window.__gaps.push([Math.round(t), Math.round(t - p)]); p = t; requestAnimationFrame(f); }; requestAnimationFrame(f); });
await wait(1.5);
await page.mouse.click(640, 400);       // the audio gate
await wait(3);                          // the rules card ignores presses until the audio clock runs
await page.keyboard.press('Space');     // start the round
await wait(0.3);

const box = await page.evaluate(() => { const r = document.querySelector('#unity-canvas').getBoundingClientRect(); return { top: r.top, h: r.height }; });
const cy = (f) => box.top + f * box.h;
const X = 640;

let fails = 0;
const check = (name, ok, detail = '') => { console.log(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ' ' + detail : ''}`); if (!ok) fails++; };

// The conducting motion for one pitch (web ConductScript, mirrored): rest low, lift over 0.4 s, hold, an
// accelerating downstroke that reaches the bottom two frames before contact. Pointer height fractions.
const F = 1 / 60, LOW = 0.7, LIFT = 0.25;
const yAt = (u) => {
  const stop = -2 * F, sa = stop - 0.12, le = sa - 0.18, ls = le - 0.4, high = LOW - LIFT;
  if (u <= ls) return LOW;
  if (u < le) return LOW - LIFT * ((u - ls) / 0.4);
  if (u < sa) return high;
  if (u < stop) { const w = (u - sa) / 0.12; return high + LIFT * w * w; }
  return LOW;
};

const next = async (id) => {
  await until(() => launches.some((l) => l.id === id), 30, `launch ${id}`);
  const l = launches.find((x) => x.id === id);
  return { id, deadline: l.wall + l.eta * 1000, l };
};
const nRel = () => releases.length;

// One pitch. Returns the release the game logged (its source: ictus or button), whether it came before the
// button went up, and the judged swing for the pitch (Judged fires once per scored pitch, in order).
async function conduct(id, mode) {
  const { deadline } = await next(id);
  const before = nRel();
  await page.mouse.move(X, cy(LOW));
  await page.mouse.down();
  let upAt = 0;
  const sent = [];
  if (mode === 'flick') {
    // 60 Hz samples anchored to the contact time, up to the stop two frames before it.
    for (let u = -Math.floor((deadline - performance.now()) / (F * 1000)) * F; u <= -2 * F + 1e-9; u += F) {
      await sleepTo(deadline + u * 1000);
      sent.push(page.mouse.move(X, cy(yAt(u))));   // not awaited: a round trip is ~25 ms, slower than a 60 Hz stroke
    }
    await Promise.all(sent);
    await sleepTo(deadline + 400);               // keep the button down: an ictus swing must not wait for it
  } else if (mode === 'button') {
    await sleepTo(deadline);
    upAt = performance.now();
    await page.mouse.up();
  } else {
    // a slow lazy drift: never a stroke
    const t0 = performance.now();
    while (performance.now() < deadline + 300) {
      const k = (performance.now() - t0) / 1000;
      await page.mouse.move(X, cy(0.7 - 0.05 * Math.sin(k * 3)));
      await wait(0.016);
    }
  }
  const ranBeforeUp = nRel() > before && (!upAt || releases[before].wall <= upAt + 50);
  if (!upAt) { upAt = performance.now(); await page.mouse.up(); }
  await until(() => swings.length >= id, 6, `judged ${id}`);
  await wait(0.1);
  const rel = nRel() > before ? releases[before] : null;
  return { rel, ranBeforeUp, s: swings[id - 1] };
}

const conducted = [];
for (let id = 1; id <= 6; id++) conducted.push(await conduct(id, 'flick'));
const seven = await conduct(7, 'button');
const eight = await conduct(8, 'drift');

for (const [i, c] of conducted.entries()) console.log(`pitch ${i + 1}: ${c.s.verdict} err=${c.s.err}ms power=${c.s.power} hold=${c.s.hold} release=${c.rel ? c.rel.src : 'none'} beforeUp=${c.ranBeforeUp}`);
console.log(`pitch 7 (button-up): ${seven.s.verdict} err=${seven.s.err}ms power=${seven.s.power} hold=${seven.s.hold} release=${seven.rel ? seven.rel.src : 'none'}`);
console.log(`pitch 8 (drift): ${eight.s.verdict} release=${eight.rel ? eight.rel.src : 'none'}`);

const hits = conducted.filter((c) => c.s.verdict !== 'miss');
const meanErr = hits.reduce((a, c) => a + Math.abs(c.s.err), 0) / Math.max(1, hits.length);
check('conducted pitches are hit', hits.length >= 5, `${hits.length}/6`);
check('swings fire at the ictus, before the button goes up', conducted.every((c) => c.rel && c.rel.src === 'ictus' && c.ranBeforeUp));
check('swings land near the ictus (mean |err| within the GREAT window)', hits.length > 0 && meanErr < 82, `mean ${meanErr.toFixed(1)}ms`);
check('every hit is inside the GOOD window', hits.every((c) => Math.abs(c.s.err) <= 128));
check('a hold for the whole flight is a home run (power >= 0.72)', hits.every((c) => c.s.power >= 0.72), hits.map((c) => c.s.power).join(','));
check('the windup length is the flight (~1 beat since the downbeat round: one ball per beat)', hits.every((c) => c.s.hold > 0.6 && c.s.hold < 1.4), hits.map((c) => c.s.hold).join(','));
check('release with no pointer motion swings on the button-up', seven.rel && seven.rel.src === 'button' && seven.s.verdict !== 'miss', `${seven.s.verdict} ${seven.s.err}ms`);
check('a slow drift is not a swing (no ictus, pitch missed)', eight.s.verdict === 'miss' && (!eight.rel || eight.rel.src !== 'ictus'));
check('the round scores', swings[swings.length - 1].pts > 0, `pts=${swings[swings.length - 1].pts}`);
console.log('rAF gaps >60ms (page ms, gap):', JSON.stringify(await page.evaluate(() => window.__gaps)));
if (SHOT) await page.screenshot({ path: SHOT });
check('no page errors', errors.length === 0, errors.join(' | '));
console.log('errors:', errors.length ? errors.join('\n') : 'none');
await browser.close();
server.close();
process.exit(fails ? 1 : 0);
