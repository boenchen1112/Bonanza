// Usage: node Tools/options-check.mjs <buildDir> <outPrefix>
// Opens the Options screen in the WebGL build, changes every row, reloads and
// checks the values persisted. Writes <outPrefix>-1/2/3.png and prints the errors.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright';

const DIR = process.argv[2], OUT = process.argv[3], PORT = 7962;
const TYPES = { '.html': 'text/html', '.js': 'application/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.data': 'application/octet-stream' };
const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html';
  const f = path.join(DIR, p);
  if (!fs.existsSync(f)) { res.writeHead(404); res.end(); return; }
  const h = {}; let ext = path.extname(f);
  if (ext === '.br') { h['Content-Encoding'] = 'br'; ext = path.extname(f.slice(0, -3)); }
  h['Content-Type'] = TYPES[ext] || 'application/octet-stream';
  res.writeHead(200, h); fs.createReadStream(f).pipe(res);
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));
const browser = await chromium.launch({ headless: true, args: ['--use-angle=d3d11', '--mute-audio'] });
const ctx = await browser.newContext({ viewport: { width: 1280, height: 720 } });
const errors = [];
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function open() {
  const page = await ctx.newPage();
  page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
  page.on('console', (m) => { if (m.type() === 'error') errors.push('console: ' + m.text()); if (m.text().includes('[GFX]')) console.log(m.text()); });
  await page.goto(`http://127.0.0.1:${PORT}/`, { waitUntil: 'load' });
  await page.waitForFunction(() => { const b = document.querySelector('#unity-loading-container'); return b && getComputedStyle(b).display === 'none'; }, null, { timeout: 90000 });
  await sleep(1500);
  await page.mouse.click(5, 5);          // audio gesture; the gate passes on any press
  await page.keyboard.press('Space');
  await sleep(2500);
  await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown');
  await sleep(200);
  await page.keyboard.press('Space');    // OPTIONS
  await sleep(800);
  return page;
}
const press = async (page, k, n = 1) => { for (let i = 0; i < n; i++) { await page.keyboard.press(k); await sleep(120); } };

let page = await open();
await page.screenshot({ path: OUT + '-1.png' });
// MUSIC +3 (90%), SFX -2 (75%), OFFSET +4 (+20ms), REDUCE on, GRAPHICS -> HIGH->..., SWING -> camera
await press(page, 'ArrowRight', 3);
await press(page, 'ArrowDown'); await press(page, 'ArrowLeft', 2);
await press(page, 'ArrowDown'); await press(page, 'ArrowRight', 4);
await press(page, 'ArrowDown'); await press(page, 'ArrowRight');
await press(page, 'ArrowDown'); await press(page, 'ArrowRight', 3);   // auto -> high -> medium -> low
await press(page, 'ArrowDown'); await press(page, 'ArrowRight', 2);   // tap -> mouse -> camera
await sleep(600);
await page.screenshot({ path: OUT + '-2.png' });
await page.close();
await sleep(1500);
page = await open();
await press(page, 'ArrowDown', 4);   // park on GRAPHICS to show the live level line
await sleep(500);
await page.screenshot({ path: OUT + '-3.png' });
console.log('errors:', errors.length ? errors.join('\n') : 'none');
await browser.close(); server.close();
