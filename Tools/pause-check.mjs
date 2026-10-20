// Usage: node Tools/pause-check.mjs <buildDir> <outPrefix> [gameId]
//   node Tools/pause-check.mjs Builds/WebGL Captures/pause swing-kings
//   MUSIC=1 also prints the music player's log.
// Asserts (exit 1 on failure): no page errors; after the resume one copy of the music plays within 15 ms of
// where the pause froze it (Tools/music-hook.mjs, from the page's own start()/stop() calls); the player's
// position getter agrees with the game's grid within 15 ms (median and max) and needs no offset; the game
// clock carries on from where it was held (it once jumped back by the pause's length); no seek follows.
// Plays a real (not autoplay) round: starts it, pauses after 6 s, resumes,
// and screenshots the menu, the count back in and play after it
// (<outPrefix>-menu.png, -count.png, -after.png). Autoplay ignores the pause
// key, so this is the only automated look at pause/resume in a build.
// Same server and Playwright setup as webgl-check.mjs.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright';
import { installMusicHook, analyzeResume } from './music-hook.mjs';

const DIR = process.argv[2];
const OUT = process.argv[3];
const GAME = process.argv[4] || 'swing-kings';
const PORT = Number(process.env.PORT || 7962);
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
const logs = [];
page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
page.on('console', (m) => {
  if (m.type() === 'error') errors.push('console: ' + m.text());
  if (m.text().includes('[MUSIC]')) { logs.push(m.text()); if (process.env.MUSIC) console.log(m.text()); }   // MUSIC=1: show the music player's log
});
await installMusicHook(page);
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
await wait(3.6);   // the count back in (3 beats, up to 1.5 s) + the player's 1.5 s report after the resume
await page.screenshot({ path: `${OUT}-after.png` });
const a = analyzeResume(await page.evaluate(() => window.__musicEv));
let fail = errors.length > 0;
const check = (ok, msg) => { console.log((ok ? 'PASS ' : 'FAIL ') + msg); if (!ok) fail = true; };
// 1. The page's own view: where each music source sounds after the resume, against where the pause froze the music.
if (a.error) check(false, a.error);
else {
  console.log(`pause lasted ${(a.dctx * 1000).toFixed(0)} ms of audio time; music froze at file position ${a.pos0.toFixed(4)} s; ${a.alive} sources playing after`);
  check(a.spread < 0.005, `one copy of the music plays after the resume (sources differ by ${(a.spread * 1000).toFixed(1)} ms)`);
  check(Math.abs(a.e) < 0.015, `resume error (JS sources) ${(a.e * 1000).toFixed(1)} ms (< 15 ms)`);
}
// 2. The player's view, half a second to 1.5 s after the resume: the position getter against the game's grid.
const rs = logs.find((l) => l.includes('[MUSIC] after resume id='));
if (!rs) check(false, 'no [MUSIC] after resume line');
else {
  const num = (k) => parseFloat(new RegExp(k + '=(-?[\\d.]+)').exec(rs)?.[1] ?? 'NaN');
  const med = parseFloat(/vsGrid\(ms\)=median=(-?[\d.]+)/.exec(rs)?.[1] ?? 'NaN');
  const max = parseFloat(/vsGrid\(ms\)=median=-?[\d.]+ absP95=[\d.]+ absMax=([\d.]+)/.exec(rs)?.[1] ?? 'NaN');
  check(Math.abs(num('resumeErr')) < 15, `resume error (getter, 0.5 s after) ${num('resumeErr').toFixed(1)} ms (< 15 ms)`);
  check(Math.abs(med) < 15 && max < 15, `position vs grid after resume: median ${med} ms, max ${max} ms (< 15 ms)`);
  check(Math.abs(num('bias')) < 15, `no getter offset taken out after the pause (bias ${num('bias')} ms)`);
  check(Math.abs(num('clockDrift')) < 50, `game clock carries on from where it was held (drift ${num('clockDrift')} ms, < 50 ms)`);
  check(num('resyncs') === 0, 'no seek after the resume');
}
check(!logs.some((l) => l.includes('[MUSIC] resync')), 'no [MUSIC] resync in the log');
console.log('errors:', errors.length ? errors.join('\n') : 'none');
if (fail) process.exitCode = 1;
await browser.close();
server.close();
