// Usage: node Tools/pause-check.mjs <buildDir> <outPrefix> [gameId]
//   node Tools/pause-check.mjs Builds/WebGL Captures/pause swing-kings
// Plays a real (not autoplay) round: starts it, pauses after 6 s, resumes,
// and screenshots the menu, the count back in and play after it
// (<outPrefix>-menu.png, -count.png, -after.png). Autoplay ignores the pause
// key, so this is the only automated look at pause/resume in a build.
// Same server and Playwright setup as webgl-check.mjs.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright';

const DIR = process.argv[2];
const OUT = process.argv[3];
const GAME = process.argv[4] || 'swing-kings';
const PORT = 7962;
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
page.on('console', (m) => { if (m.type() === 'error') errors.push('console: ' + m.text()); });
const wait = (s) => new Promise((r) => setTimeout(r, s * 1000));

await page.goto(`http://127.0.0.1:${PORT}/?game=${GAME}`, { waitUntil: 'load' });
await page.waitForFunction(() => getComputedStyle(document.querySelector('#unity-loading-container')).display === 'none', null, { timeout: 60000 });
await wait(1.5);
await page.mouse.click(640, 400);            // unlock audio (the gate)
await wait(3);                               // the rules card ignores presses until the audio clock runs
await page.keyboard.press('Space');          // rules card -> play
await wait(6);
await page.keyboard.press('Escape');         // pause
await wait(0.8);
await page.screenshot({ path: `${OUT}-menu.png` });
await page.keyboard.press('Space');          // RESUME (the first item)
await wait(0.4);
await page.screenshot({ path: `${OUT}-count.png` });
await wait(2.5);
await page.screenshot({ path: `${OUT}-after.png` });
console.log('errors:', errors.length ? errors.join('\n') : 'none');
await browser.close();
server.close();
