// Usage: node Tools/webgl-check.mjs <buildDir> <shot.png> [query] [waitForLog] [waitSeconds]
//   node Tools/webgl-check.mjs Builds/WebGL Captures/webgl.png
//   node Tools/webgl-check.mjs Builds/WebGL Captures/autoplay.png "?autoplay" "[RESULT]" 150
// Serves the brotli build with correct headers, waits for the Unity loader,
// clicks the canvas (browsers keep audio locked until a gesture), optionally
// waits for a console line, screenshots. Prints every [RESULT] line and all
// page/console errors.
// Needs Playwright: run `npm --prefix Tools install` once.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright';

const DIR = process.argv[2];
const OUT = process.argv[3];
const QUERY = process.argv[4] || '';
const WAIT_LOG = process.argv[5] || '';
const WAIT_S = Number(process.argv[6] || 120);
const PORT = Number(process.env.PORT || 7961);
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
// The page template fills the window; 960x600 is the canvas size the gates always ran at, so their numbers
// (biasMs ~8) stay comparable. VIEWPORT=1280x800 overrides (the bigger canvas is slower, so biasMs rises to ~12).
const [VW, VH] = (process.env.VIEWPORT || '960x600').split('x').map(Number);
const page = await browser.newPage({ viewport: { width: VW, height: VH } });
const errors = [];
const results = [];
const T0 = Date.now();   // LIVE=1 prints result lines as they arrive, for long runs
let seen = false;
page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
page.on('console', (m) => {
  const t = m.text();
  if (process.env.LOGS && (t.includes('[LATENCY]') || t.includes('[AUDIO]'))) console.log(t);
  if (m.type() === 'error') errors.push('console: ' + t);
  else if (t.includes('[ERROR]')) errors.push('console: ' + t);   // the host's fault line is a console warning; any of them fails a gate
  if (t.includes('[RESULT]') || t.includes('[MISS]') || t.includes('[PARTY') || (process.env.MUSIC && t.includes('[MUSIC]'))) {   // MUSIC=1 also keeps the music player's log
    results.push(t);
    if (process.env.LIVE) console.log(`[${((Date.now() - T0) / 1000).toFixed(0)}s]`, t);
  }
  if (WAIT_LOG && t.includes(WAIT_LOG)) seen = true;
});
await page.goto(`http://127.0.0.1:${PORT}/${QUERY}`, { waitUntil: 'load' });
const t0 = Date.now();
// The loading bar hides once the Unity instance is created.
try {
  await page.waitForFunction(() => {
    const bar = document.querySelector('#unity-loading-container');
    return bar && getComputedStyle(bar).display === 'none';
  }, null, { timeout: 60000 });
} catch (e) {
  console.log('loader never hid; ids:', await page.evaluate(() => [...document.querySelectorAll('[id]')].map((n) => n.id + ':' + getComputedStyle(n).display).join(' ')));
}
console.log('loaded in', ((Date.now() - t0) / 1000).toFixed(1), 's');
await new Promise((r) => setTimeout(r, 1500));
await page.mouse.click(VW / 2, VH / 2);   // the user gesture that unlocks audio
if (WAIT_LOG) {
  const t1 = Date.now();
  while (!seen && Date.now() - t1 < WAIT_S * 1000) await new Promise((r) => setTimeout(r, 500));
  console.log(seen ? `saw "${WAIT_LOG}" after ${((Date.now() - t1) / 1000).toFixed(1)} s` : `TIMEOUT: no "${WAIT_LOG}" in ${WAIT_S} s`);
}
await new Promise((r) => setTimeout(r, 1500));
await page.screenshot({ path: OUT });
for (const r of results) console.log(r);
console.log('errors:', errors.length ? errors.join('\n') : 'none');
await browser.close();
server.close();
process.exit(WAIT_LOG && !seen ? 1 : 0);
