// Usage: node Tools/input-check.mjs <buildDir>
//   node Tools/input-check.mjs Builds/WebGL
// Drives the real input path of a WebGL build and asserts on the [INPUT] log lines
// (the build logs them when the page is opened with ?inputlog):
//   X and K are `b`, Z is `a`; a held key logs one down (auto-repeat ignored) and a later up;
//   a window blur releases what is held; a pointer drag logs the pointer height, clientY / height.
// Same server and Playwright setup as webgl-check.mjs. Exits 1 on a failed assertion.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright';

const DIR = process.argv[2];
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
const lines = [];
page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
page.on('console', (m) => {
  const t = m.text();
  if (m.type() === 'error') errors.push('console: ' + t);
  if (t.startsWith('[INPUT]')) lines.push(t);
});
const wait = (s) => new Promise((r) => setTimeout(r, s * 1000));
const parse = (l) => { const m = l.match(/^\[INPUT\] (\w+) (\S+) t=([\d.-]+)/); return m ? { what: m[1], action: m[2], t: Number(m[3]) } : null; };

await page.goto(`http://127.0.0.1:${PORT}/?game=swing-kings&inputlog`, { waitUntil: 'load' });
await page.waitForFunction(() => getComputedStyle(document.querySelector('#unity-loading-container')).display === 'none', null, { timeout: 60000 });
await wait(1.5);

let fails = 0;
const check = (name, ok, detail = '') => { console.log(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ' ' + detail : ''}`); if (!ok) fails++; };
const since = (mark) => lines.slice(mark).map(parse).filter(Boolean);
const section = async (fn) => { const mark = lines.length; await fn(); await wait(0.4); return since(mark); };

await page.mouse.click(640, 400);              // focus the canvas and unlock audio
await wait(0.5);

let ev = await section(async () => { await page.keyboard.press('KeyX'); });
check('X is b', ev.some((e) => e.what === 'down' && e.action === 'b') && ev.some((e) => e.what === 'up' && e.action === 'b'), JSON.stringify(ev));
ev = await section(async () => { await page.keyboard.press('KeyK'); });
check('K is b', ev.some((e) => e.what === 'down' && e.action === 'b'), JSON.stringify(ev));
ev = await section(async () => { await page.keyboard.press('KeyZ'); });
check('Z is a', ev.some((e) => e.what === 'down' && e.action === 'a') && !ev.some((e) => e.action === 'b'), JSON.stringify(ev));

// Held key: a repeating keydown (Playwright sends repeat=true on the second down) is one press.
const mark = lines.length;
await page.keyboard.down('KeyJ');
await wait(0.3);
await page.keyboard.down('KeyJ');
await wait(0.3);
await page.keyboard.up('KeyJ');
await wait(0.4);
ev = since(mark);
const downs = ev.filter((e) => e.what === 'down' && e.action === 'a');
const ups = ev.filter((e) => e.what === 'up' && e.action === 'a');
check('held key: one down despite auto-repeat', downs.length === 1, JSON.stringify(ev));
check('held key: release after the hold', ups.length === 1 && downs.length === 1 && ups[0].t - downs[0].t > 0.4, JSON.stringify(ev));

const box = await page.evaluate(() => { const r = document.querySelector('#unity-canvas').getBoundingClientRect(); return { top: r.top, h: r.height }; });
const cy = (f) => box.top + f * box.h;   // a clientY at fraction f of the canvas
// Pointer: press, drag to 25% and 75% of the canvas height, release.
const mark2 = lines.length;
await page.mouse.move(640, cy(0.5));
await page.mouse.down();
await wait(0.1);
await page.mouse.move(640, cy(0.25), { steps: 4 });
await wait(0.1);
await page.mouse.move(640, cy(0.75), { steps: 4 });
await wait(0.1);
await page.mouse.up();
await wait(0.4);
const rawLines = lines.slice(mark2);
const moves = rawLines.map((l) => l.match(/move y=([\d.]+)/)).filter(Boolean).map((m) => Number(m[1]));
check('pointer press logs a click down and up', rawLines.some((l) => l.includes('down click')) && rawLines.some((l) => /up click/.test(l)), rawLines.join(' | '));
check('pointer height at press is 0.5', moves.length > 0 && Math.abs(moves[0] - 0.5) < 0.03, 'first=' + moves[0]);
check('pointer height follows the drag (0.25 then 0.75)', moves.some((y) => Math.abs(y - 0.25) < 0.05) && moves.some((y) => Math.abs(y - 0.75) < 0.05), moves.join(','));
const afterUp = lines.length;
await page.mouse.move(640, cy(0.1), { steps: 3 });
await wait(0.3);
check('no pointer samples once the button is up', !lines.slice(afterUp).some((l) => l.includes('move y=')));

// Blur releases every held action.
ev = await section(async () => {
  await page.keyboard.down('KeyX');
  await wait(0.2);
  await page.evaluate(() => window.dispatchEvent(new Event('blur')));
  await wait(0.3);
});
check('blur releases the held key', ev.some((e) => e.what === 'down' && e.action === 'b') && ev.some((e) => e.what === 'blur' && e.action === 'b'), JSON.stringify(ev));
await page.keyboard.up('KeyX');

for (const l of lines) console.log(l);
check('no page errors', errors.length === 0, errors.join(' | '));
console.log('errors:', errors.length ? errors.join('\n') : 'none');
await browser.close();
server.close();
process.exit(fails ? 1 : 0);
