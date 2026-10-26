// Real-input results cards (no autoplay): node Tools/ui13-results.mjs <out-prefix> <query> <hubSpace:0|1> <w> <h> [secs]
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path';
import { chromium } from 'playwright';
const [OUT, QUERY, HUB, W, H, SECS] = [process.argv[2], process.argv[3], process.argv[4] === '1', +process.argv[5] || 1280, +process.argv[6] || 720, +process.argv[7] || 200];
const DIR = 'Builds/WebGL', PORT = Number(process.env.PORT || 7983);
const TYPES = { '.html': 'text/html', '.js': 'application/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.data': 'application/octet-stream', '.png': 'image/png', '.ico': 'image/x-icon' };
const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html';
  const f = path.join(DIR, p); if (!fs.existsSync(f)) { res.writeHead(404); res.end(); return; }
  const h = {}; let ext = path.extname(f); if (ext === '.br') { h['Content-Encoding'] = 'br'; ext = path.extname(f.slice(0, -3)); }
  h['Content-Type'] = TYPES[ext] || 'application/octet-stream'; res.writeHead(200, h); fs.createReadStream(f).pipe(res);
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));
const wait = (s) => new Promise((r) => setTimeout(r, s * 1000));
const browser = await chromium.launch({ headless: true, args: ['--use-angle=d3d11', '--mute-audio'] });
const page = await browser.newPage({ viewport: { width: W, height: H } });
let result = 0, partyDone = 0; const errs = [];
page.on('console', (m) => { const t = m.text(); if (t.includes('[ERROR]') || m.type() === 'error') errs.push(t); if (partyDone) console.log('LOG', m.type(), t.slice(0, 400)); if (t.includes('[PARTY-DONE]')) { partyDone = Date.now(); console.log(t); }
  if (t.includes('[RESULT]')) { result = Date.now(); console.log(t); } });
await page.goto(`http://127.0.0.1:${PORT}/${QUERY.startsWith("?") ? QUERY : "?" + QUERY}`, { waitUntil: 'load' });
await page.waitForFunction(() => getComputedStyle(document.querySelector('#unity-loading-container')).display === 'none', null, { timeout: 120000 });
await wait(1.5); await page.mouse.click(W / 2, H / 2); await wait(2);
if (/partyDemo/.test(QUERY)) {   // podium demos (scene=party&partyDemo=win|youwin|wintie&debug): wait for the staging, then shoot
  await wait(Math.min(SECS, 7)); await page.mouse.move(2, 2);
  await page.screenshot({ path: `${OUT}.png` });
  console.log('errors:', errs.length ? errs.slice(0, 3).join(' | ') : 'none');
  await browser.close(); server.close(); process.exit(0);
}
if (/party/.test(QUERY) && /autoplay/.test(QUERY)) {   // podium: let the autoplay party run out, then shoot the final screen
  const tp = Date.now();
  while (!partyDone && (Date.now() - tp) / 1000 < SECS) await wait(1);
  await page.mouse.move(2, 2);
  for (let i = 0; i < 14; i++) { await page.screenshot({ path: `${OUT}-t${String(i).padStart(2, '0')}.png` }); await wait(0.6); }   // the wrap-up shows for a few seconds after the last results card
  console.log('errors:', errs.length ? errs.slice(0, 3).join(' | ') : 'none', 'partyDone', !!partyDone);
  await browser.close(); server.close(); process.exit(0);
}
if (process.env.RULES) {   // the rules card, before any start press
  await page.mouse.move(2, 2); await page.screenshot({ path: `${OUT}.png` });
  console.log('errors:', errs.length ? errs.slice(0, 3).join(' | ') : 'none');
  await browser.close(); server.close(); process.exit(0);
}
if (HUB) { await page.keyboard.press('Space'); await wait(3); }
await page.keyboard.press('Space'); await wait(3); await page.keyboard.press('Space');
const t0 = Date.now();
while (!result && (Date.now() - t0) / 1000 < SECS) await wait(0.5);
await wait(4); await page.mouse.move(2, 2);
await page.screenshot({ path: `${OUT}.png` });
console.log('errors:', errs.length ? errs.slice(0, 3).join(' | ') : 'none', 'result', !!result);
await browser.close(); server.close();
