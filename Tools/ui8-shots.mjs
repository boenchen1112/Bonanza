import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path';
import { chromium } from 'playwright';
const DIR = 'Builds/WebGL', PORT = Number(process.env.PORT || 7968), OUT = process.env.OUT || 'Captures/ui8';
const only = process.argv.slice(2);
const TYPES = { '.html': 'text/html', '.js': 'application/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.data': 'application/octet-stream', '.png': 'image/png', '.ico': 'image/x-icon' };
const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html';
  const f = path.join(DIR, p); if (!fs.existsSync(f)) { res.writeHead(404); res.end(); return; }
  const h = {}; let ext = path.extname(f); if (ext === '.br') { h['Content-Encoding'] = 'br'; ext = path.extname(f.slice(0, -3)); }
  h['Content-Type'] = TYPES[ext] || 'application/octet-stream'; res.writeHead(200, h); fs.createReadStream(f).pipe(res);
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));
fs.mkdirSync(OUT, { recursive: true });
const wait = (s) => new Promise((r) => setTimeout(r, s * 1000));
const browser = await chromium.launch({ headless: true, args: ['--use-angle=d3d11', '--mute-audio', '--enable-unsafe-swiftshader', '--use-gl=angle', '--use-angle=swiftshader'] });
const R = 'ArrowRight', D = 'ArrowDown', U = 'ArrowUp', L = 'ArrowLeft', A = 'Space';
const S = [{"n": "title-16x9", "u": "scene=title", "w": 1280, "h": 720, "wait": 3}, {"n": "title-640", "u": "scene=title", "w": 640, "h": 360, "wait": 3}, {"n": "roster-16x9", "u": "scene=roster", "w": 1280, "h": 720, "wait": 3}, {"n": "roster-640", "u": "scene=roster", "w": 640, "h": 360, "wait": 3}, {"n": "hub-16x9", "u": "scene=party&partyDemo=r3", "w": 1280, "h": 720, "wait": 5}, {"n": "hub-640", "u": "scene=party&partyDemo=r3", "w": 640, "h": 360, "wait": 5}, {"n": "win-16x9", "u": "scene=party&partyDemo=win", "w": 1280, "h": 720, "wait": 6}, {"n": "win-640", "u": "scene=party&partyDemo=win", "w": 640, "h": 360, "wait": 6}, {"n": "youwin-16x9", "u": "scene=party&partyDemo=youwin", "w": 1280, "h": 720, "wait": 6}, {"n": "youwin-640", "u": "scene=party&partyDemo=youwin", "w": 640, "h": 360, "wait": 6}, {"n": "wintie-16x9", "u": "scene=party&partyDemo=wintie", "w": 1280, "h": 720, "wait": 6}, {"n": "wintie-640", "u": "scene=party&partyDemo=wintie", "w": 640, "h": 360, "wait": 6}, {"n": "freeplay-16x9", "u": "scene=freeplay", "w": 1280, "h": 720, "wait": 3}, {"n": "freeplay-640", "u": "scene=freeplay", "w": 640, "h": 360, "wait": 3}, {"n": "options-16x9", "u": "scene=options", "w": 1280, "h": 720, "wait": 3}, {"n": "options-640", "u": "scene=options", "w": 640, "h": 360, "wait": 3}];
for (const s of S) {
  if (only.length && !only.includes(s.n)) continue;
  const ctx = await browser.newContext({ viewport: { width: s.w, height: s.h } });
  const page = await ctx.newPage();
  const errs = [], logs = [];
  page.on('console', (m) => { const t = m.text(); logs.push(t); if (t.includes('[ERROR]') || m.type() === 'error') errs.push(t); });
  page.on('pageerror', (e) => errs.push('pageerror ' + e.message));
  await page.goto(`http://127.0.0.1:${PORT}/?${s.u}&debug`, { waitUntil: 'load' });
  await page.waitForFunction(() => getComputedStyle(document.querySelector('#unity-loading-container')).display === 'none', null, { timeout: 90000 });
  await wait(1.5);
  await page.evaluate(() => { for (const b of document.querySelectorAll('button')) if (b.textContent.trim() === 'OK') b.click(); });
  await wait(0.5);
  await page.mouse.click(s.w / 2, s.h / 2);
  await page.mouse.move(2, 2);   // park the pointer: nothing may look hovered
  await wait(s.wait);
  const ms = await page.evaluate(() => new Promise((res) => { const d = []; let l = performance.now(), n = 0; const f = (t) => { d.push(t - l); l = t; if (++n < 120) requestAnimationFrame(f); else { d.shift(); d.sort((a, b) => a - b); res([d.reduce((a, b) => a + b, 0) / d.length, d[Math.floor(d.length * 0.95)]]); } }; requestAnimationFrame(f); }));
  await page.screenshot({ path: `${OUT}/${s.n}.png` });
  console.log(s.n, 'frame avg/p95 ms', ms[0].toFixed(1), ms[1].toFixed(1), 'errors:', errs.length ? errs.slice(0, 3).join(' | ') : 'none');
  await ctx.close();
}
await browser.close(); server.close();
