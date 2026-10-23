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
const PORT = Number(process.env.PORT || 7963);
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

// ---- the roster and its character-pick stage (web roster.js), real keys, no autoplay
const CAST_IDS = ['bopp', 'zizz', 'kwark', 'tuff', 'mimo', 'nibb', 'glub', 'fizz'];
async function openTitle(query = '') {
  const m = lines.length;
  await page.goto(`http://127.0.0.1:${PORT}/${query}`, { waitUntil: 'load' });
  await page.waitForFunction(() => getComputedStyle(document.querySelector('#unity-loading-container')).display === 'none', null, { timeout: 60000 });
  await wait(1.5);
  await page.mouse.click(480, 300);   // the audio gate
  await waitLine('[SHELL] screen=title', m, 20);
  await wait(1);
}
const stageOf = (l) => /stage=(\w+)/.exec(l ?? '')?.[1];
const charOf = (l) => /char=(\w+)/.exec(l ?? '')?.[1];
let cursor = 0;   // the grid cursor, mirrored from the keys we send (it starts on the first character)
const goTo = async (idx) => { while (cursor !== idx) { await key('ArrowRight'); cursor = (cursor + 1) % 8; } };

await openTitle();
mark = lines.length;
await key('Space');                   // PARTY
let r0 = await waitLine('[SHELL] roster mode=', mark, 10);
check(!!r0 && /mode=party/.test(r0.line) && stageOf(r0.line) === 'lineup', 'PARTY opens the roster on the lineup stage: ' + r0?.line);
mark = lines.length;
await key('ArrowLeft');
const miss = await waitLine('[SHELL] roster miss', mark, 5);
check(!!miss && /P1 IS YOU/.test(miss.line), 'seat P1 cannot be changed (miss + flash): ' + miss?.line);
await key('ArrowDown'); await key('ArrowRight');                  // P2: normal -> hard
await key('ArrowDown'); await key('ArrowDown'); await key('ArrowRight');   // P4: off -> easy
await page.screenshot({ path: `${OUT}-roster-lineup.png` });
mark = lines.length;
await key('Space');                   // continue to the character pick
const r1 = await waitLine('[SHELL] roster stage=', mark, 5);
check(stageOf(r1?.line) === 'chars', 'SPACE moves on to the character grid: ' + r1?.line);
await goTo(5);
await page.screenshot({ path: `${OUT}-roster-chars.png` });
mark = lines.length;
await key('Space');                   // P1 locks in NIBB
let pk = await waitLine('[SHELL] roster pick seat=0', mark, 5);
check(!!pk && charOf(pk.line) === 'nibb', 'P1 locks in the highlighted character: ' + pk?.line);
const rg = await waitLine('[SHELL] roster stage=go', mark, 12);
check(!!rg, 'with every seat filled the roster reads LINEUP SET');
const cpu = lines.slice(mark).filter((l) => l.startsWith('[SHELL] roster pick seat=') && / cpu/.test(l));
const held = [charOf(pk?.line), ...cpu.map(charOf)];
check(cpu.length === 3 && new Set(held).size === 4, 'three CPUs each pick, and nobody shares a character: ' + held.join(','));
await page.screenshot({ path: `${OUT}-roster-go.png` });
mark = lines.length;
await key('KeyX');                    // B: undo P1's pick
const un = await waitLine('[SHELL] roster unpick seat=0', mark, 5);
check(!!un, 'X undoes the last human pick: ' + un?.line);
mark = lines.length;
await goTo(CAST_IDS.indexOf(held[1]));   // a character a CPU holds
await key('Space');
const tk = await waitLine('[SHELL] roster taken', mark, 5);
check(!!tk && charOf(tk.line) === held[1], 'a character a CPU already holds cannot be picked again: ' + tk?.line);
const free = CAST_IDS.findIndex((id) => !held.includes(id));
mark = lines.length;
await goTo(free);
await key('Space');
pk = await waitLine('[SHELL] roster pick seat=0', mark, 5);
check(!!pk && charOf(pk.line) === CAST_IDS[free], 'a free character locks in after the refusal: ' + pk?.line);
await wait(0.5);
mark = lines.length;
await key('Space');                   // START PARTY (the lineup is still full: the CPUs kept their picks)
const ps0 = await waitLine('[PARTY] start', mark, 10);
check(!!ps0 && new RegExp(`players=${CAST_IDS[free].toUpperCase()},\\w+,\\w+,\\w+`).test(ps0.line), 'the party starts with the picked characters as the players: ' + ps0?.line);

// Free Play: one human, one pick, then the list
await openTitle();
mark = lines.length;
await key('ArrowDown'); await key('Space');   // FREE PLAY
r0 = await waitLine('[SHELL] roster mode=', mark, 10);
check(!!r0 && /mode=free/.test(r0.line) && stageOf(r0.line) === 'chars', 'FREE PLAY opens the roster straight on the character grid: ' + r0?.line);
await page.screenshot({ path: `${OUT}-roster-free.png` });
mark = lines.length;
await key('Space');
pk = await waitLine('[SHELL] roster pick seat=0', mark, 5);
check(!!pk && charOf(pk.line) === 'bopp', 'Free Play locks in one character: ' + pk?.line);
const rg2 = await waitLine('[SHELL] roster stage=go', mark, 5);
check(!!rg2, 'no rivals to wait for: straight to READY');
mark = lines.length;
await key('Space');
const fc = await waitLine('[SHELL] freecast', mark, 5);
const mn = await waitLine('[SHELL] screen=menu', mark, 5);
check(!!fc && /name=BOPP/.test(fc.line) && !!mn, 'SPACE leaves for the Free Play list as BOPP: ' + fc?.line);
await openTitle();
mark = lines.length;
await key('ArrowDown'); await key('Space');
await waitLine('[SHELL] roster mode=free', mark, 10);
mark = lines.length;
await key('KeyX');
const tt = await waitLine('[SHELL] screen=title', mark, 5);
check(!!tt, 'X on an empty pick leaves the roster for the title');

console.log('errors:', errors.length ? errors.join('\n') : 'none');
if (fail || errors.length) process.exitCode = 1;
await browser.close();
server.close();
