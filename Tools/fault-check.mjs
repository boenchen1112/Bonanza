// Usage: node Tools/fault-check.mjs <buildDir> [outPrefix]
//   node Tools/fault-check.mjs Builds/WebGL Captures/fault
// Boots the build with a set of URLs and asserts (exit 1 on any failure) what the shell does with them:
//   ?autoplay&game=fault        the injected Load fault: exactly one "[ERROR]" line, the page ends on the title
//                               ("[SHELL] screen=title" is the last screen line), no second error afterwards
//   ?autoplay&game=bogus        one "[ERROR] unknown game id 'bogus'", lands on the title (no silent swing-kings)
//   ?game=fault                 not honoured without ?autoplay / ?debug: unknown id, title
//   ?debug&game=fault           honoured with ?debug: the fault, then the title
//   ?scene=bogus                one "[ERROR] unknown scene", title
//   ?scene=<title|freeplay|menu|roster|options|party|play&game=..>   the right screen, no [ERROR]
//   ?scale / ?perf / ?swingInput ... applied (log lines) and the ` key toggles the perf HUD
// Error policy: the host logs the fault as a console WARNING (Debug.LogWarning), so the page-error check ("errors:")
// stays meaningful: any console error or page exception fails every scenario. The "[ERROR]" lines are counted from
// the warnings and must be exactly the expected number.
// Same server and Playwright setup as webgl-check.mjs.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright';

const DIR = process.argv[2];
const OUT = process.argv[3] || 'Captures/fault';
const PORT = Number(process.env.PORT || 7966);
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
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const lastScreen = (logs) => { const s = logs.filter((l) => l.includes('[SHELL] screen=')); return s.length ? s[s.length - 1].replace(/.*screen=/, '') : null; };
const errorLines = (logs) => logs.filter((l) => l.includes('[ERROR]'));

// Each scenario: query, whether a click is needed (audio gate; autoplay passes it on its own but the click is harmless),
// settle = ms to keep watching after the expected screen appeared (a repeating fault would show up here),
// check(logs, ctx) -> list of failure strings.
const scenarios = [
  { name: 'fault', q: '?autoplay&game=fault', until: (l) => lastScreen(l) === 'title' && errorLines(l).length > 0,
    check: (l) => {
      const f = [], e = errorLines(l);
      if (e.length !== 1) f.push(`expected exactly 1 [ERROR] line, saw ${e.length}`);
      else if (!/injected fault in Load/.test(e[0])) f.push('error line does not name the injected fault: ' + e[0]);
      if (lastScreen(l) !== 'title') f.push('last screen is ' + lastScreen(l) + ', not title');
      if (l.some((x) => x.includes('[RESULT]'))) f.push('a [RESULT] was logged');
      if (!l.some((x) => x.includes('[SHELL] open game=fault'))) f.push('the fault game was never opened');
      return f;
    } },
  { name: 'bogus-game', q: '?autoplay&game=bogus', until: (l) => lastScreen(l) === 'title',
    check: (l) => {
      const f = [], e = errorLines(l);
      if (e.length !== 1 || !e[0].includes("unknown game id 'bogus'")) f.push('expected one "unknown game id \'bogus\'" error, saw: ' + JSON.stringify(e));
      if (lastScreen(l) !== 'title') f.push('last screen is ' + lastScreen(l));
      if (l.some((x) => x.includes('[SHELL] open game='))) f.push('a game was opened (silent fallback)');
      return f;
    } },
  { name: 'fault-not-honoured', q: '?game=fault', until: (l) => lastScreen(l) === 'title',
    check: (l) => {
      const f = [], e = errorLines(l);
      if (e.length !== 1 || !e[0].includes("unknown game id 'fault'")) f.push('fault id must be unknown without autoplay/debug, saw: ' + JSON.stringify(e));
      if (l.some((x) => x.includes('[SHELL] open game='))) f.push('a game was opened');
      return f;
    } },
  { name: 'debug-fault', q: '?debug&game=fault', until: (l) => errorLines(l).length > 0 && lastScreen(l) === 'title',
    check: (l) => {
      const f = [], e = errorLines(l);
      if (e.length !== 1 || !e[0].includes('injected fault')) f.push('expected the injected fault once, saw: ' + JSON.stringify(e));
      if (lastScreen(l) !== 'title') f.push('last screen is ' + lastScreen(l));
      return f;
    } },
  { name: 'bogus-scene', q: '?scene=bogus', until: (l) => lastScreen(l) === 'title',
    check: (l) => {
      const e = errorLines(l);
      return e.length === 1 && e[0].includes("unknown scene 'bogus'") ? [] : ['expected one unknown scene error, saw: ' + JSON.stringify(e)];
    } },
  ...[['title', 'title'], ['freeplay', 'menu'], ['menu', 'menu'], ['roster', 'roster'], ['options', 'options']].map(([id, screen]) => ({
    name: 'scene-' + id, q: '?scene=' + id, until: (l) => lastScreen(l) === screen,
    check: (l) => (errorLines(l).length ? ['unexpected [ERROR]: ' + errorLines(l)[0]] : lastScreen(l) === screen ? [] : ['last screen ' + lastScreen(l) + ', wanted ' + screen]),
  })),
  { name: 'scene-party', q: '?scene=party', until: (l) => l.some((x) => x.includes('[SHELL] hub index=0')),
    check: (l) => errorLines(l).length ? ['unexpected [ERROR]'] : (l.some((x) => x.includes('[PARTY] start')) ? [] : ['no party started']) },
  { name: 'scene-game', q: '?scene=drumline-dash', until: (l) => l.some((x) => x.includes('[SHELL] open game=drumline-dash')),
    check: (l) => errorLines(l).length ? ['unexpected [ERROR]'] : [] },
  { name: 'scene-play', q: '?scene=play&game=chomp-chorus', until: (l) => l.some((x) => x.includes('[SHELL] open game=chomp-chorus')),
    check: (l) => errorLines(l).length ? ['unexpected [ERROR]'] : [] },
  { name: 'scene-play-nogame', q: '?scene=play', until: (l) => lastScreen(l) === 'title',
    check: (l) => (errorLines(l).length === 1 && errorLines(l)[0].includes("unknown scene 'play'") ? [] : ['expected unknown scene error for bare play']) },
  { name: 'params', q: '?scene=options&scale=0.75&swingInput=mouse&perf&quality=low', until: (l) => lastScreen(l) === 'options',
    keys: ['Backquote'], keyUntil: (l) => l.filter((x) => x.includes('[PERF] hud=')).length >= 2,
    check: (l) => {
      const f = [];
      if (!l.some((x) => x.includes('[URL] applied scale=0.75 swingInput=mouse'))) f.push('scale/swingInput not applied');
      const perf = l.filter((x) => x.includes('[PERF] hud='));
      if (perf.length < 2 || !perf[0].includes('hud=on') || !perf[1].includes('hud=off')) f.push('perf HUD should start on (?perf) and toggle off with the backquote key: ' + JSON.stringify(perf));
      if (errorLines(l).length) f.push('unexpected [ERROR]');
      return f;
    } },
  { name: 'params-clamped', q: '?scale=9&swingInput=bogus&nonsense=1&scene=title', until: (l) => lastScreen(l) === 'title',
    check: (l) => {
      const f = [];
      if (!l.some((x) => x.includes('[URL] applied scale=2 '))) f.push('scale=9 should clamp to 2');
      if (!l.some((x) => x.includes('[URL] ignored:') && x.includes('swingInput') && x.includes("unknown parameter 'nonsense'"))) f.push('ignored reasons not logged');
      if (errorLines(l).length) f.push('unexpected [ERROR]');
      return f;
    } },
  { name: 'perf-toggle', q: '?scene=title', until: (l) => lastScreen(l) === 'title', keys: ['Backquote', 'Backquote'],
    keyUntil: (l) => l.filter((x) => x.includes('[PERF] hud=')).length >= 2, shot: true,
    check: (l) => {
      const perf = l.filter((x) => x.includes('[PERF] hud='));
      return perf.length >= 2 && perf[0].includes('hud=on') && perf[1].includes('hud=off') ? [] : ['backquote should toggle the HUD on then off: ' + JSON.stringify(perf)];
    } },
];

let failed = 0;
const only = process.env.ONLY ? process.env.ONLY.split(',') : null;
for (const sc of scenarios) {
  if (only && !only.includes(sc.name)) continue;
  const page = await browser.newPage({ viewport: { width: 960, height: 600 } });
  const logs = [], errors = [];
  page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
  page.on('console', (m) => {
    const t = m.text().trim();
    if (m.type() === 'error') errors.push('console: ' + t);
    if (t.includes('[SHELL]') || t.includes('[ERROR]') || t.includes('[URL]') || t.includes('[PERF]') || t.includes('[PARTY]') || t.includes('[RESULT]') || t.includes('[FAULT]')) logs.push(t);
  });
  await page.goto(`http://127.0.0.1:${PORT}/${sc.q}`, { waitUntil: 'load' });
  try {
    await page.waitForFunction(() => getComputedStyle(document.querySelector('#unity-loading-container')).display === 'none', null, { timeout: 90000 });
  } catch (e) { errors.push('loader never hid'); }
  await sleep(1500);
  await page.mouse.click(480, 300);   // the audio gate (autoplay passes it by itself)
  const t1 = Date.now();
  while (!sc.until(logs) && Date.now() - t1 < 30000) await sleep(250);
  const reached = sc.until(logs);
  for (const k of sc.keys || []) {
    // The first press only needs to land after Unity is running; wait for the keyUntil step by step.
    await page.keyboard.press(k);
    await sleep(400);
  }
  if (sc.keyUntil) { const t2 = Date.now(); while (!sc.keyUntil(logs) && Date.now() - t2 < 8000) await sleep(250); }
  await sleep(2500);   // a repeating fault or a second error would land here
  if (sc.shot) await page.screenshot({ path: `${OUT}-${sc.name}.png` });
  const fails = [...(reached ? [] : ['expected state never reached']), ...sc.check(logs), ...errors.map((e) => 'page error: ' + e)];
  console.log(`${fails.length ? 'FAIL' : 'ok  '} ${sc.name.padEnd(20)} ${sc.q}   errorLines=${errorLines(logs).length} lastScreen=${lastScreen(logs)}`);
  for (const f of fails) console.log('       ' + f);
  if (fails.length) { failed++; for (const l of logs) console.log('       | ' + l.split('\n')[0]); }
  await page.close();
}
console.log(failed ? `fault-check: ${failed} scenario(s) failed` : 'fault-check: all scenarios passed\nerrors: none');
await browser.close();
server.close();
process.exit(failed ? 1 : 0);
