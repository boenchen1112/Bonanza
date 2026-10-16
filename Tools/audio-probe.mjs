// Usage: node Tools/audio-probe.mjs <buildDir> [gameId]
//   node Tools/audio-probe.mjs Builds/WebGL swing-kings
// The other checks run muted and only prove timing. This one taps the page's
// Web Audio output (an AnalyserNode in front of ctx.destination) and prints
// the output level every 0.5 s through a real round: rules card, play, pause
// menu, count back in, play. It shows whether the music is audible at all and
// whether the pause really silences it. Same server and Playwright setup as
// webgl-check.mjs.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright';

const DIR = process.argv[2];
const GAME = process.argv[3] || 'swing-kings';
const PORT = 7963;
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
page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
page.on('console', (m) => {
  if (m.type() === 'error') errors.push('console: ' + m.text());
  if (m.text().includes('[MUSIC]')) console.log(m.text().trim());
});
await page.addInitScript(() => {
  const connect = AudioNode.prototype.connect;
  window.__env = [];
  AudioNode.prototype.connect = function (dest, ...rest) {
    if (dest instanceof AudioDestinationNode) {
      const ctx = this.context;
      if (!ctx.__tap) {
        // A pass-through ScriptProcessor sees every sample; its playbackTime is on the ctx clock.
        const an = ctx.createAnalyser();
        an.fftSize = 2048;
        const sp = ctx.createScriptProcessor(256, 2, 2);
        sp.onaudioprocess = (e) => {
          const x = e.inputBuffer.getChannelData(0);
          let sum = 0; for (let i = 0; i < x.length; i++) sum += x[i] * x[i];
          for (let c = 0; c < e.outputBuffer.numberOfChannels; c++) e.outputBuffer.getChannelData(c).set(e.inputBuffer.getChannelData(Math.min(c, e.inputBuffer.numberOfChannels - 1)));
          window.__env.push([e.playbackTime, Math.sqrt(sum / x.length)]);
        };
        connect.call(an, sp);
        connect.call(sp, dest);
        ctx.__tap = an;
      }
      return connect.call(this, ctx.__tap, ...rest);
    }
    return connect.call(this, dest, ...rest);
  };
});
const wait = (s) => new Promise((r) => setTimeout(r, s * 1000));
let consumed = 0;
const marks = [];
const mark = async (label) => {
  const env = await page.evaluate(() => window.__env);
  const seg = env.slice(consumed); consumed = env.length;
  const v = seg.map((e) => e[1]);
  const mean = v.length ? v.reduce((p, q) => p + q, 0) / v.length : NaN;
  console.log(`${label.padEnd(26)} buffers=${String(seg.length).padStart(5)} meanRms=${mean.toFixed(4)} peakRms=${(v.length ? Math.max(...v) : NaN).toFixed(4)}`);
  marks.push({ label, seg });
  return seg;
};
// Where, within a beat, the music's energy falls: a histogram of the envelope by phase of the beat.
const SPB = 60 / 124, BINS = 96;
function hist(seg) {
  const h = new Float64Array(BINS), n = new Float64Array(BINS);
  for (const [t, v] of seg) { const b = Math.floor(((t % SPB) / SPB) * BINS) % BINS; h[b] += v; n[b]++; }
  return Array.from(h, (x, i) => (n[i] ? x / n[i] : 0));
}
// The circular shift of `b` against `a` (seconds) that lines the two histograms up best.
function shift(a, b) {
  let best = 0, bestScore = -Infinity;
  for (let k = 0; k < BINS; k++) {
    let sc = 0; for (let i = 0; i < BINS; i++) sc += a[i] * b[(i + k) % BINS];
    if (sc > bestScore) { bestScore = sc; best = k; }
  }
  return (best / BINS) * SPB;
}

await page.goto(`http://127.0.0.1:${PORT}/?game=${GAME}`, { waitUntil: 'load' });
await page.waitForFunction(() => getComputedStyle(document.querySelector('#unity-loading-container')).display === 'none', null, { timeout: 60000 });
await wait(1.5);
await page.mouse.click(640, 400);
await wait(3);
await mark('rules card');
await page.keyboard.press('Space');
await wait(4);
await mark('lead-in (4 s)');
await wait(3);
await mark('play (3 s)');
await page.keyboard.press('Escape');
await wait(0.4);
await mark('pause (settling)');
await wait(1.8);
await mark('pause menu (1.8 s)');
await page.keyboard.press('Space');
await wait(1.6);
await mark('count back in');
await wait(3);
await mark('play after resume (3 s)');
const [pre, post] = [marks.find((m) => m.label === 'play (3 s)').seg, marks.find((m) => m.label.startsWith('play after')).seg];
const silent = marks.filter((m) => m.label.startsWith('pause') || m.label === 'count back in').flatMap((m) => m.seg);
const quiet = silent.filter((e) => e[1] < 1e-4);
const D = quiet.length ? quiet[quiet.length - 1][0] - quiet[0][0] : NaN;   // time the output was silent
const sh = shift(hist(pre), hist(post));   // post's grid sits `sh` after pre's
const wrap = (x) => { x = ((x % SPB) + SPB) % SPB; return x > SPB / 2 ? x - SPB : x; };
console.log(`silent for ${D.toFixed(3)} s (${(D / SPB).toFixed(2)} beats)`);
console.log(`if the music froze with the game it carries on where it stopped, so after the silence its beat grid has moved by ${(wrap(D) * 1000).toFixed(0)} ms mod a beat; if it ran on in the dark it has not moved (0 ms)`);
// About 40 ms of audio is already queued in the browser when the pause lands and plays out after it, so
// the silence reads that much shorter than the pause: a difference of about +40 ms means the music is on the grid.
console.log(`measured grid move ${(wrap(sh) * 1000).toFixed(0)} ms; difference from "froze" ${(wrap(sh - D) * 1000).toFixed(0)} ms`);
console.log('errors:', errors.length ? errors.join('\n') : 'none');
await browser.close();
server.close();
