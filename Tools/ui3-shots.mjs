import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path';
import { chromium } from 'playwright';
const DIR = 'Builds/WebGL', PORT = Number(process.env.PORT || 7963), OUT = 'Captures/ui3';
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
const S = [
  { n: 'idle', w: 1280, h: 720, wait: 6 },
  { n: 'lineup-cpu', w: 1280, h: 720, keys: [D, R], wait: 2 },
  { n: 'lineup-empty', w: 1280, h: 720, keys: [D, R, R, D, R, R], wait: 2 },
  { n: 'chars', w: 1280, h: 720, keys: [A], wait: 3 },
  { n: 'focus-right', w: 1280, h: 720, keys: [A, R], wait: 1.2 },
  { n: 'focus-right-settled', w: 1280, h: 720, keys: [A, R], wait: 3 },
  { n: 'focus-down', w: 1280, h: 720, keys: [A, D], wait: 3 },
  { n: 'focus-tall', w: 1280, h: 720, keys: [A, R, R, R, R], wait: 3 },
  { n: 'focus-small', w: 1280, h: 720, keys: [A, D, R], wait: 3 },
  { n: 'locked', w: 1280, h: 720, keys: [A, A], wait: 1.5 },
  { n: 'seats-full', w: 1280, h: 720, keys: [A, R, A], wait: 5 },
  { n: 'taken', w: 1280, h: 720, keys: [A, A, 'Escape', 'Escape'], wait: 1 },
  { n: 'hover-card', w: 1280, h: 720, keys: [A], wait: 2, hover: [265, 300] },
  { n: 'hover-action', w: 1280, h: 720, wait: 2, hover: [880, 205] },
  { n: 'reduced', w: 1280, h: 720, keys: [A, R], wait: 4, reduced: true },
  { n: 'low', w: 1280, h: 720, keys: [A, R], wait: 5, q: 'low' },
  { n: '4x3', w: 1024, h: 768, keys: [A, R], wait: 5 },
  { n: '21x9', w: 1680, h: 720, keys: [A, R], wait: 5 },
  { n: 'small', w: 640, h: 360, keys: [A, R], wait: 5 },
];
for (const s of S) {
  if (only.length && !only.includes(s.n)) continue;
  const ctx = await browser.newContext({ viewport: { width: s.w, height: s.h }, reducedMotion: s.reduced ? 'reduce' : 'no-preference' });
  const page = await ctx.newPage();
  const errs = [], logs = [];
  page.on('console', (m) => { const t = m.text(); logs.push(t); if (t.includes('[ERROR]') || m.type() === 'error') errs.push(t); });
  page.on('pageerror', (e) => errs.push('pageerror ' + e.message));
  await page.goto(`http://127.0.0.1:${PORT}/?scene=roster&debug${s.q ? '&quality=' + s.q : ''}`, { waitUntil: 'load' });
  await page.waitForFunction(() => getComputedStyle(document.querySelector('#unity-loading-container')).display === 'none', null, { timeout: 90000 });
  await wait(1.5);
  await page.mouse.click(s.w / 2, 60);
  await wait(1.0);
  for (const k of s.keys || []) { await page.keyboard.press(k); await wait(0.3); }
  if (s.hover) { await page.mouse.move(s.hover[0] * s.w / 1280, s.hover[1] * s.h / 720); await wait(0.8); }
  if (s.press) { await page.mouse.move(s.press[0] * s.w / 1280, s.press[1] * s.h / 720); await wait(0.3); await page.mouse.down(); }
  await wait(s.wait);
  await page.screenshot({ path: `${OUT}/${s.n}.png` });
  console.log(s.n, 'logs:', logs.filter((l) => l.includes('[SHELL]')).slice(-4).join(' | '), 'errors:', errs.length ? errs.slice(0, 3).join(' | ') : 'none');
  await ctx.close();
}
await browser.close(); server.close();
