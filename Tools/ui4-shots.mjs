import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path';
import { chromium } from 'playwright';
const DIR = 'Builds/WebGL', PORT = Number(process.env.PORT || 7964), OUT = 'Captures/ui4';
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
  { n: 'hub-r1', d: 'r1', w: 1280, h: 720, wait: 4 },
  { n: 'hub-mid', d: 'r3', w: 1280, h: 720, wait: 5 },
  { n: 'hub-mid-anim', d: 'r3', w: 1280, h: 720, wait: 1.4 },
  { n: 'hub-last', d: 'last', w: 1280, h: 720, wait: 5 },
  { n: 'hub-lead', d: 'lead', w: 1280, h: 720, wait: 5 },
  { n: 'hub-trail', d: 'trail', w: 1280, h: 720, wait: 5 },
  { n: 'hub-tie', d: 'tie', w: 1280, h: 720, wait: 5 },
  { n: 'win-single', d: 'win', w: 1280, h: 720, wait: 6 },
  { n: 'win-tie', d: 'wintie', w: 1280, h: 720, wait: 6 },
  { n: 'win-you', d: 'youwin', w: 1280, h: 720, wait: 6 },
  { n: 'hub-reduced', d: 'r3', w: 1280, h: 720, wait: 3, reduced: true },
  { n: 'win-reduced', d: 'win', w: 1280, h: 720, wait: 3, reduced: true },
  { n: 'hub-low', d: 'r3', w: 1280, h: 720, wait: 5, q: 'low' },
  { n: 'win-low', d: 'win', w: 1280, h: 720, wait: 6, q: 'low' },
  { n: 'hub-4x3', d: 'r3', w: 1024, h: 768, wait: 5 },
  { n: 'win-4x3', d: 'win', w: 1024, h: 768, wait: 6 },
  { n: 'hub-21x9', d: 'r3', w: 1680, h: 720, wait: 5 },
  { n: 'win-21x9', d: 'win', w: 1680, h: 720, wait: 6 },
  { n: 'hub-small', d: 'r3', w: 640, h: 360, wait: 5 },
  { n: 'win-small', d: 'win', w: 640, h: 360, wait: 6 },
  { n: 'hub-hover', d: 'r3', w: 1280, h: 720, wait: 4, hover: [640, 548] },
  { n: 'win-hover', d: 'win', w: 1280, h: 720, wait: 5, hover: [515, 666] },
];
for (const s of S) {
  if (only.length && !only.includes(s.n)) continue;
  const ctx = await browser.newContext({ viewport: { width: s.w, height: s.h }, reducedMotion: s.reduced ? 'reduce' : 'no-preference' });
  const page = await ctx.newPage();
  const errs = [], logs = [];
  page.on('console', (m) => { const t = m.text(); logs.push(t); if (t.includes('[ERROR]') || m.type() === 'error') errs.push(t); });
  page.on('pageerror', (e) => errs.push('pageerror ' + e.message));
  await page.goto(`http://127.0.0.1:${PORT}/?scene=party&debug&partyDemo=${s.d}${s.q ? '&quality=' + s.q : ''}`, { waitUntil: 'load' });
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
