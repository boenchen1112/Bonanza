// Usage: node Tools/party-flow-check.mjs <buildDir> <outPrefix>
//   node Tools/party-flow-check.mjs Builds/WebGL Captures/flow
// A real (not autoplay) party, driven by keys: `?party=2` seeds the shuffle as the third party and starts
// it at the gate with the default roster, so the hub is the first screen. Then, in the first round:
//   pause -> RESTART (the round opens again, same game, party index unchanged),
//   pause -> RESUME (counts back in, then plays on),
//   pause -> QUIT TO HUB (back to the hub: the round is not recorded, the party goes on, the SAME round is
//   offered again), then SPACE opens that round again.
// Reads the host's "[SHELL] ..." lines. Exit 1 on any failed check or page error.
// Same server and Playwright setup as webgl-check.mjs.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright';

const DIR = process.argv[2];
const OUT = process.argv[3] || 'Captures/flow';
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
const page = await browser.newPage({ viewport: { width: 960, height: 600 } });
const errors = [];
const lines = [];   // every [SHELL] / [PARTY] line, in order
page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
page.on('console', (m) => {
  const t = m.text();
  if (m.type() === 'error') errors.push('console: ' + t);
  if (t.startsWith('[SHELL]') || t.startsWith('[PARTY')) { lines.push(t); if (process.env.LIVE) console.log('  ' + t); }
});
const wait = (s) => new Promise((r) => setTimeout(r, s * 1000));
let fail = false;
const check = (ok, msg) => { console.log((ok ? 'PASS ' : 'FAIL ') + msg); if (!ok) fail = true; };
/** Wait for a new line (after index `from`) that starts with `prefix`; returns it or null. */
async function waitLine(prefix, from, seconds = 15) {
  const end = Date.now() + seconds * 1000;
  while (Date.now() < end) {
    const i = lines.findIndex((l, k) => k >= from && l.startsWith(prefix));
    if (i >= 0) return { line: lines[i], at: i };
    await wait(0.25);
  }
  return null;
}
const key = async (k) => { await page.keyboard.press(k); await wait(0.15); };
const game = (l) => /game=([\w-]+)/.exec(l)?.[1];

await page.goto(`http://127.0.0.1:${PORT}/?party=2`, { waitUntil: 'load' });
await page.waitForFunction(() => getComputedStyle(document.querySelector('#unity-loading-container')).display === 'none', null, { timeout: 60000 });
await wait(1.5);
await page.mouse.click(480, 300);   // the audio gate; `party=` then starts the party

const start = await waitLine('[PARTY] start', 0, 20);
check(!!start, 'the party starts at the gate');
const hub0 = await waitLine('[SHELL] hub', 0, 10);
check(!!hub0 && /index=0/.test(hub0.line), 'the hub shows round 0: ' + hub0?.line);
const g1 = game(hub0?.line ?? '');
check(g1 === 'chomp-chorus', `party 2 opens with chomp-chorus in the registry order (got ${g1})`);
await page.screenshot({ path: `${OUT}-hub.png` });

// ---- round 1: start it
let mark = lines.length;
await wait(0.5);
await key('Space');
let op = await waitLine('[SHELL] open', mark);
check(!!op && game(op.line) === g1 && /from=party/.test(op.line), 'SPACE on the hub opens the same game from the party: ' + op?.line);
await wait(3);               // the rules card ignores presses until the audio clock runs
await key('Space');          // start the round
await wait(5);

// ---- pause -> RESTART
mark = lines.length;
await key('Escape');
let ps = await waitLine('[SHELL] pause', mark);
check(!!ps, 'Escape pauses the round');
await page.screenshot({ path: `${OUT}-pause.png` });
await key('ArrowDown');      // RESUME -> RESTART
await key('Space');
let rs = await waitLine('[SHELL] restart', mark);
check(!!rs && game(rs.line) === g1 && /party-index=0/.test(rs.line), 'RESTART in a party restarts the same round, party index unchanged: ' + rs?.line);
op = await waitLine('[SHELL] open', rs?.at ?? mark);
check(!!op && game(op.line) === g1, 'the round opens again: ' + op?.line);
await wait(3);
await key('Space');
await wait(5);

// ---- pause -> RESUME
mark = lines.length;
await key('Escape');
ps = await waitLine('[SHELL] pause', mark);
check(!!ps, 'Escape pauses the restarted round');
await key('Space');          // RESUME (first item)
const rsm = await waitLine('[SHELL] resume', mark, 10);
check(!!rsm, 'RESUME counts back in and plays on');
await wait(3);
await page.screenshot({ path: `${OUT}-after.png` });

// ---- pause -> QUIT TO HUB
mark = lines.length;
await key('Escape');
ps = await waitLine('[SHELL] pause', mark);
check(!!ps, 'Escape pauses again');
await key('ArrowDown'); await key('ArrowDown');   // QUIT TO HUB
await key('Space');
const lv = await waitLine('[SHELL] leave-round', mark);
check(!!lv && /-> party/.test(lv.line) && /party-index=0/.test(lv.line), 'QUIT TO HUB leaves the round for the hub: ' + lv?.line);
const hub1 = await waitLine('[SHELL] hub', mark);
check(!!hub1 && /index=0/.test(hub1.line) && game(hub1.line) === g1, 'the hub offers the same round again: ' + hub1?.line);
check(!lines.slice(start?.at ?? 0).some((l) => l.startsWith('[PARTY] round=')), 'the quit round was not recorded (no [PARTY] round= line)');
await wait(0.5);
await page.screenshot({ path: `${OUT}-hub2.png` });

// ---- the party goes on: SPACE plays that round again
mark = lines.length;
await key('Space');
op = await waitLine('[SHELL] open', mark);
check(!!op && game(op.line) === g1 && /from=party/.test(op.line), 'SPACE on the hub opens that round again: ' + op?.line);

console.log('errors:', errors.length ? errors.join('\n') : 'none');
if (fail || errors.length) process.exitCode = 1;
await browser.close();
server.close();
