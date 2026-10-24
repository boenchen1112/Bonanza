import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path';
import { chromium } from 'playwright';
const DIR = 'Builds/WebGL', PORT = Number(process.env.PORT || 7965), OUT = 'Captures/ui2';
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
const R = 'ArrowRight', L = 'ArrowLeft';
// card centres in 1280x720 reference px: x = 640 + (i-2)*182, y = 320; PLAY (640, 551); BACK (~1110, 685)
const cx = (i) => [640 + (i - 2) * 182, 330];
const S = [
  { n: 'idle', d: 'mix', w: 1280, h: 720, wait: 4 },
  { n: 'focus-2', d: 'mix', w: 1280, h: 720, wait: 2, keys: [R, R] },
  { n: 'focus-last', d: 'mix', w: 1280, h: 720, wait: 2, keys: [R, R, R, R, R] },
  { n: 'all-ranked', d: 'all', w: 1280, h: 720, wait: 3, keys: [R] },
  { n: 'none-played', d: 'none', w: 1280, h: 720, wait: 3 },
  { n: 'real-save', d: '', w: 1280, h: 720, wait: 3 },
  { n: 'hover', d: 'mix', w: 1280, h: 720, wait: 3, hover: cx(3) },
  { n: 'press', d: 'mix', w: 1280, h: 720, wait: 3, press: cx(1) },
  { n: 'hover-play', d: 'mix', w: 1280, h: 720, wait: 3, hover: [640, 571] },
  { n: 'reduced', d: 'mix', w: 1280, h: 720, wait: 2, reduced: true, keys: [R] },
  { n: 'low', d: 'mix', w: 1280, h: 720, wait: 4, q: 'low' },
  { n: '4x3', d: 'mix', w: 1024, h: 768, wait: 4, keys: [R] },
  { n: '21x9', d: 'mix', w: 1680, h: 720, wait: 4 },
  { n: 'small', d: 'mix', w: 640, h: 360, wait: 4, keys: [R] },
  { n: 'small-all', d: 'all', w: 640, h: 360, wait: 4, keys: [R, R] },
  { n: 'small-none', d: 'none', w: 640, h: 360, wait: 4 },
  { n: 'press-focus', d: 'mix', w: 1280, h: 720, wait: 1.5, press: cx(3) },
];
for (const s of S) {
  if (only.length && !only.includes(s.n)) continue;
  const ctx = await browser.newContext({ viewport: { width: s.w, height: s.h }, reducedMotion: s.reduced ? 'reduce' : 'no-preference' });
  const page = await ctx.newPage();
  const errs = [], logs = [];
  page.on('console', (m) => { const t = m.text(); logs.push(t); if (t.includes('[ERROR]') || m.type() === 'error') errs.push(t); });
  page.on('pageerror', (e) => errs.push('pageerror ' + e.message));
  await page.goto(`http://127.0.0.1:${PORT}/?scene=freeplay&debug${s.d ? '&menuDemo=' + s.d : ''}${s.q ? '&quality=' + s.q : ''}`, { waitUntil: 'load' });
  await page.waitForFunction(() => getComputedStyle(document.querySelector('#unity-loading-container')).display === 'none', null, { timeout: 90000 });
  await wait(1.5);
  await page.mouse.click(s.w / 2, 8);
  await wait(1.0);
  for (const k of s.keys || []) { await page.keyboard.press(k); await wait(0.3); }
  if (s.hover) { await page.mouse.move(s.hover[0] * s.w / 1280, s.hover[1] * s.h / 720); await wait(0.8); }
  if (s.press) { await page.mouse.move(s.press[0] * s.w / 1280, s.press[1] * s.h / 720); await wait(0.3); await page.mouse.down(); }
  await wait(s.wait);
  await page.screenshot({ path: `${OUT}/${s.n}.png` });
  console.log(s.n, 'logs:', logs.filter((l) => l.includes('[SHELL]')).slice(-3).join(' | '), 'errors:', errs.length ? errs.slice(0, 3).join(' | ') : 'none');
  await ctx.close();
}
await browser.close(); server.close();
