import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path';
import { chromium } from 'playwright';
const DIR = 'Builds/WebGL', PORT = Number(process.env.PORT || 7975), OUT = 'Captures/ui5';
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
const browser = await chromium.launch({ headless: true, args: ['--mute-audio', '--enable-unsafe-swiftshader', '--use-gl=angle', '--use-angle=swiftshader'] });
const R = 'ArrowRight', L = 'ArrowLeft', D = 'ArrowDown', U = 'ArrowUp', SP = 'Space';
// 1280x720 reference px: left column x 200..630, right column 650..1080; track line of row at y+52
const track = (row, f) => (row === 0 ? [214 + 10 + f * 382, 190] : [214 + 10 + f * 382, 272]);
const S = [
  { n: 'idle', w: 1280, h: 720, wait: 4 },
  { n: 'music-mid', w: 1280, h: 720, wait: 1.5, drag: [0, 0.5] },
  { n: 'music-min', w: 1280, h: 720, wait: 1.5, drag: [0, 0] },
  { n: 'music-max', w: 1280, h: 720, wait: 1.5, drag: [0, 1] },
  { n: 'sfx-mid', w: 1280, h: 720, wait: 1.5, drag: [1, 0.55] },
  { n: 'keys-sfx', w: 1280, h: 720, wait: 1.5, keys: [D, L, L, L, L, L] },
  { n: 'offset', w: 1280, h: 720, wait: 1.5, keys: [D, D, L, L, L, L] },
  { n: 'reduce', w: 1280, h: 720, wait: 1.5, keys: [D, D, D, R] },
  { n: 'graphics', w: 1280, h: 720, wait: 1.5, keys: [D, D, D, D, R] },
  { n: 'swing', w: 1280, h: 720, wait: 1.5, keys: [D, D, D, D, D, R] },
  { n: 'reset', w: 1280, h: 720, wait: 1.5, keys: [D, D, D, D, D, D] },
  { n: 'confirm', w: 1280, h: 720, wait: 1.5, keys: [D, D, D, D, D, D, SP] },
  { n: 'back-focus', w: 1280, h: 720, wait: 1.5, keys: [D, D, D, D, D, D, D] },
  { n: 'hover-seg', w: 1280, h: 720, wait: 1.5, hover: [900, 258] },
  { n: 'hover-track', w: 1280, h: 720, wait: 1.5, hover: [400, 272] },
  { n: 'hover-step', w: 1280, h: 720, wait: 1.5, hover: [236, 354] },
  { n: 'press-seg', w: 1280, h: 720, wait: 1.0, press: [760, 258] },
  { n: 'press-step', w: 1280, h: 720, wait: 1.0, press: [236, 354] },
  { n: 'reduced', w: 1280, h: 720, wait: 2, reduced: true, keys: [D, D, R, R] },
  { n: 'low', w: 1280, h: 720, wait: 4, q: 'low' },
  { n: '4x3', w: 1024, h: 768, wait: 4, keys: [D, D, R] },
  { n: '21x9', w: 1680, h: 720, wait: 4 },
  { n: 'small', w: 640, h: 360, wait: 4, keys: [D, D, D, D, D, R] },
  { n: 'small-confirm', w: 640, h: 360, wait: 3, keys: [D, D, D, D, D, D, SP] },
];
for (const s of S) {
  if (only.length && !only.includes(s.n)) continue;
  const ctx = await browser.newContext({ viewport: { width: s.w, height: s.h }, reducedMotion: s.reduced ? 'reduce' : 'no-preference' });
  const page = await ctx.newPage();
  const errs = [], logs = [];
  page.on('console', (m) => { const t = m.text(); logs.push(t); if (t.includes('[ERROR]') || m.type() === 'error') errs.push(t); });
  page.on('pageerror', (e) => errs.push('pageerror ' + e.message));
  await page.goto(`http://127.0.0.1:${PORT}/?scene=options&debug${s.q ? '&quality=' + s.q : ''}`, { waitUntil: 'load' });
  await page.waitForFunction(() => getComputedStyle(document.querySelector('#unity-loading-container')).display === 'none', null, { timeout: 90000 });
  await wait(1.5);
  await page.mouse.click(s.w / 2, 8);
  await wait(1.0);
  for (const k of s.keys || []) { await page.keyboard.press(k); await wait(0.3); }
  if (s.drag) { const [rw, f] = s.drag; const [x, y] = track(rw, f); await page.mouse.move(x * s.w / 1280, y * s.h / 720); await page.mouse.down(); await wait(0.2); await page.mouse.move((x + 1) * s.w / 1280, y * s.h / 720); await wait(0.2); await page.mouse.up(); await wait(0.3); }
  if (s.hover) { await page.mouse.move(s.hover[0] * s.w / 1280, s.hover[1] * s.h / 720); await wait(0.8); }
  if (s.press) { await page.mouse.move(s.press[0] * s.w / 1280, s.press[1] * s.h / 720); await wait(0.3); await page.mouse.down(); }
  await wait(s.wait);
  await page.screenshot({ path: `${OUT}/${s.n}.png` });
  console.log(s.n, 'logs:', logs.filter((l) => l.includes('[SHELL]')).slice(-3).join(' | '), 'errors:', errs.length ? errs.slice(0, 3).join(' | ') : 'none');
  await ctx.close();
}
await browser.close(); server.close();
