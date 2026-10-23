// Usage: node Tools/title-check.mjs <buildDir>
//   node Tools/title-check.mjs Builds/WebGL
// The title's attract reel and beat, from the build's own logs (page opened with ?scene=title&debug&voicelog):
//   - with no input the reel starts after IDLE_TO_ATTRACT (8.5 s) and steps through the games in registry order,
//     one every ATTRACT_HOLD (3.2 s), wrapping after the last; each change plays a ui sound ([SFX] title_ui_*)
//     and logs [SHELL] reel=enter / reel=step game=<id>
//   - a press inside the reel leaves it, plays uiBack and is consumed (no menu item fires, no screen change)
//   - b / Escape on the menu starts the reel at once, and it resumes at the next game
//   - the title kick ([VOICE] title_kick) lands once per beat on the title music's grid, with a tick every 4th
//     beat, and is louder in the reel (0.5) than in the menu (0.34)
//   - the menu still works afterwards (down + Space opens the roster)
// The reel's numbers come from Core/TitleReel.cs (golden-pinned to the web). Same server and Playwright setup
// as webgl-check.mjs. Exits 1 on a failed assertion.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright';

const DIR = process.argv[2];
const PORT = Number(process.env.PORT || 7964);
const IDLE = 8.5, HOLD = 3.2, BEAT = 60 / 112;
const IDS = fs.readFileSync(new URL('../Assets/Tests/EditMode/Golden/title-reel-games.txt', import.meta.url), 'utf8')
  .split('\n').filter((l) => l && !l.startsWith('#')).map((l) => l.split(' ')[1]);
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
const log = [];   // { t: wall seconds, text }
const T0 = Date.now();
page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
page.on('console', (m) => {
  const text = m.text();
  if (m.type() === 'error') errors.push('console: ' + text);
  if (text.startsWith('[SHELL]') || text.startsWith('[SFX]') || text.startsWith('[VOICE]') || text.includes('[ERROR]')) log.push({ t: (Date.now() - T0) / 1000, text });
  if (text.includes('[ERROR]')) errors.push(text);
});
const wait = (s) => new Promise((r) => setTimeout(r, s * 1000));
const now = () => (Date.now() - T0) / 1000;
const find = (re, from = 0) => log.find((l) => l.t >= from && re.test(l.text));
const all = (re, from = 0, to = 1e9) => log.filter((l) => l.t >= from && l.t <= to && re.test(l.text));
let fails = 0;
const check = (name, ok, detail = '') => { console.log(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ' ' + detail : ''}`); if (!ok) fails++; };
const waitFor = async (re, from, timeout) => {
  const end = now() + timeout;
  while (now() < end) { const l = find(re, from); if (l) return l; await wait(0.05); }
  return null;
};

await page.goto(`http://127.0.0.1:${PORT}/?scene=title&debug&voicelog`, { waitUntil: 'load' });
await page.waitForFunction(() => getComputedStyle(document.querySelector('#unity-loading-container')).display === 'none', null, { timeout: 60000 });
await wait(1.5);
await page.mouse.click(640, 400);   // unlock the audio gate; this press does not reach the title
const title = await waitFor(/\[SHELL\] screen=title/, 0, 15);
check('title reached', !!title);
if (!title) { await browser.close(); server.close(); process.exit(1); }

// ---- idle -> reel, then every game once and a wrap (7 changes after the start)
const steps = [];
const enter = await waitFor(/\[SHELL\] reel=enter/, title.t, IDLE + 4);
check('reel starts on its own', !!enter);
if (enter) {
  const idle = enter.t - title.t;
  check(`starts after ~${IDLE} s of idle`, idle > IDLE - 0.3 && idle < IDLE + 1.2, `(${idle.toFixed(2)} s)`);
  steps.push(enter);
  for (let k = 1; k <= 6; k++) {
    const s = await waitFor(/\[SHELL\] reel=step/, steps[steps.length - 1].t + 0.5, HOLD + 2);
    if (!s) break;
    steps.push(s);
  }
  check('seven reel frames seen (five games, then a wrap)', steps.length === 7, `(${steps.length})`);
  const gameOf = (l) => /game=(\S+)/.exec(l.text)[1];
  const got = steps.map(gameOf);
  const want = steps.map((_, i) => IDS[i % IDS.length]);
  check('games in registry order, wrapping', JSON.stringify(got) === JSON.stringify(want), got.join(','));
  for (let i = 1; i < steps.length; i++) {
    const d = steps[i].t - steps[i - 1].t;
    check(`step ${i} after ~${HOLD} s`, d > HOLD - 0.2 && d < HOLD + 0.3, `(${d.toFixed(2)} s)`);
  }
  const noUi = steps.filter((s) => !log.some((l) => /\[SFX\] clip=title_ui_/.test(l.text) && Math.abs(l.t - s.t) < 0.3));
  check('a ui sound on every reel change', noUi.length === 0, `(${noUi.length} without)`);
  const logos = all(/\[VOICE\] clip=title_kick/, title.t, steps[steps.length - 1].t);
  const vol = (l) => Number(/vol=([\d.]+)/.exec(l.text)[1]);
  const tms = (l) => Number(/ t=([\d.]+)/.exec(l.text)[1]);
  const inMenu = all(/\[VOICE\] clip=title_kick/, title.t, enter.t - 0.5);
  const inReel = all(/\[VOICE\] clip=title_kick/, enter.t + 0.5, steps[steps.length - 1].t);
  check('a kick every beat in the menu', inMenu.length >= (IDLE - 1) / BEAT * 0.9, `(${inMenu.length} kicks)`);
  check('a kick every beat in the reel', inReel.length >= (steps[steps.length - 1].t - enter.t - 1) / BEAT * 0.9, `(${inReel.length} kicks)`);
  const gaps = logos.slice(1).map((l, i) => tms(l) - tms(logos[i])).filter((g) => g > 0);
  gaps.sort((a, b) => a - b);
  const med = gaps[Math.floor(gaps.length / 2)];
  check(`kicks on the title grid (${(BEAT * 1000).toFixed(0)} ms)`, Math.abs(med - BEAT) < 0.004, `(median gap ${(med * 1000).toFixed(1)} ms)`);
  const bad = gaps.filter((g) => Math.abs(g - BEAT) > 0.003).length;
  check('no kick off the grid', bad === 0, `(${bad} of ${gaps.length})`);
  const ticks = all(/\[SFX\] clip=tick /, title.t, steps[steps.length - 1].t).length;
  check('a tick every 4th beat', Math.abs(ticks - logos.length / 4) <= 2, `(${ticks} ticks, ${logos.length} kicks)`);
  if (inMenu.length && inReel.length) {
    const r = vol(inMenu[Math.floor(inMenu.length / 2)]) / vol(inReel[Math.floor(inReel.length / 2)]);
    check('kick softer in the menu (0.34) than in the reel (0.5)', Math.abs(r - 0.68) < 0.05, `(ratio ${r.toFixed(2)})`);
  }
}

// ---- a press inside the reel leaves it and is consumed
const mark = now();
await page.keyboard.press('Space');            // 'a': would confirm PARTY if it reached the menu
const left = await waitFor(/\[SHELL\] reel=leave/, mark, 2);
check('a press leaves the reel', !!left);
await wait(1.2);
check('the press was consumed (no screen change)', !find(/\[SHELL\] screen=(roster|options|menu)/, mark));
check('uiBack plays on leaving', !!all(/\[SFX\] clip=title_uiBack/, mark).length);

// ---- b starts it at once and it resumes at the next game
const m2 = now();
await page.keyboard.press('KeyX');
const e2 = await waitFor(/\[SHELL\] reel=enter/, m2, 2);
check('b starts the reel immediately', !!e2 && e2.t - m2 < 1);
if (e2 && steps.length) {
  const g = /game=(\S+)/.exec(e2.text)[1];
  check('the reel resumes where it was', g === IDS[(steps.length - 1) % IDS.length], `(${g})`);
}
await wait(0.3);
const m2b = now();
await page.keyboard.press('Escape');            // pause is back too
check('Escape leaves it', !!(await waitFor(/\[SHELL\] reel=leave/, m2b, 2)));
await wait(0.3);
const m3 = now();
await page.keyboard.press('Escape');            // and starts it from the menu
check('Escape starts it', !!(await waitFor(/\[SHELL\] reel=enter/, m3, 2)));
await page.keyboard.press('ArrowDown');         // leaves (consumed), selection unchanged
await wait(0.5);

// ---- the menu works again: down + Space = FREE PLAY -> roster
await page.keyboard.press('ArrowDown');
await wait(0.3);
await page.keyboard.press('Space');
check('menu still opens a screen', !!(await waitFor(/\[SHELL\] screen=roster/, m3, 4)));

check('no errors', errors.length === 0, errors.slice(0, 3).join(' | '));
await browser.close();
server.close();
console.log(fails ? `FAILED ${fails}` : 'OK');
process.exit(fails ? 1 : 0);
