#!/usr/bin/env node
/**
 * Party-hub probe: stages a party through `__BBB__.shellState()` instead of
 * playing four minigames, then shoots the hub mid-party, the results card,
 * and the final podium. Build first with inspect.mjs (any scene) or pass
 * --build.
 *
 *   node tools/harness/party-probe.mjs --dist dist-t19 --out runs/t19-party
 */
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { spawn } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';

const argv = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, arr) => {
  if (a.startsWith('--')) acc.push([a.slice(2), arr[i + 1] && !arr[i + 1].startsWith('--') ? arr[i + 1] : true]);
  return acc;
}, []));
const ROOT = path.resolve(import.meta.dirname, '../..');
const WEB = path.join(ROOT, 'web');
const DIST = path.join(WEB, String(argv.dist || 'dist-party'));
const OUT = path.resolve(ROOT, String(argv.out || 'runs/party-probe'));
await mkdir(OUT, { recursive: true });

if (argv.build) {
  await new Promise((res, rej) => {
    const p = spawn(process.execPath, [path.join(WEB, 'node_modules/vite/bin/vite.js'), 'build', '--mode', 'harness', '--outDir', DIST], { cwd: WEB, stdio: 'inherit' });
    p.on('close', (c) => (c === 0 ? res() : rej(new Error('build failed'))));
  });
}

const webRequire = createRequire(path.join(WEB, 'package.json'));
const pw = await import(pathToFileURL(webRequire.resolve('playwright')).href);
const chromium = pw.chromium ?? pw.default.chromium;
const PORT = 6900 + (process.pid % 500);
const server = spawn(process.execPath, [path.join(ROOT, 'tools/harness/serve.mjs'), DIST, String(PORT)], { stdio: ['ignore', 'pipe', 'pipe'] });
await new Promise((r) => { server.stdout.once('data', r); setTimeout(r, 1200); });

const browser = await chromium.launch({ channel: 'chromium', headless: true,
  args: ['--autoplay-policy=no-user-gesture-required', '--mute-audio', ...(process.platform === 'win32' ? ['--use-angle=d3d11'] : [])] });
const page = await (await browser.newContext({ viewport: { width: 1280, height: 720 } })).newPage();
const logs = [];
page.on('console', (m) => { if (m.type() === 'error' || m.type() === 'warning') logs.push(`[${m.type()}] ${m.text()}`); });
page.on('pageerror', (e) => logs.push(`[pageerror] ${e.message}`));
await page.goto(`http://127.0.0.1:${PORT}/index.html?scene=title&quality=high`, { waitUntil: 'load' });
await page.keyboard.press('KeyQ');
await page.waitForFunction('window.__BBB__ && window.__BBB__.ready', null, { timeout: 20000 });
await page.evaluate(() => window.__BBB__.ready);
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
let n = 0;
async function shot(label) {
  await page.evaluate(() => window.__BBB__.screenshotReady?.());
  const f = `${String(n++).padStart(2, '0')}-${label}.png`;
  await page.screenshot({ path: path.join(OUT, f) });
  console.log(f);
}

const GAMES = ['swing-kings', 'drumline-dash', 'chomp-chorus', 'finale-fever'];
await page.evaluate(async (games) => {
  const { session } = await window.__BBB__.shellState();
  session.setPlayers([
    { name: 'P1', char: 'bopp', isCpu: false, cpuSkill: 0 },
    { name: 'ZIZZ', char: 'zizz', isCpu: true, cpuSkill: 0.86 },
    { name: 'KWARK', char: 'kwark', isCpu: true, cpuSkill: 0.63 },
    { name: 'FIZZ', char: 'fizz', isCpu: true, cpuSkill: 0.4 },
  ]);
  session.startParty(games, games.length, 4242);
}, GAMES);

await page.evaluate(() => window.__BBB__.goto('party'));
await wait(1600); await shot('hub-round1');

// Round 1 through the real results screen (party mode records the round).
await page.evaluate(() => window.__BBB__.goto('results', { game: 'swing-kings', party: true,
  result: { score: 38000, accuracy: 0.97, rank: 'A', stats: { perfect: 30, great: 6, good: 2, miss: 1, maxCombo: 22 } } }));
await wait(1200); await shot('results-r1-reveal');
await wait(1600); await shot('results-r1');
await page.evaluate(() => window.__BBB__.goto('party'));
await wait(600); await shot('hub-round2-rising');
await wait(1400); await shot('hub-round2');

await page.evaluate(async () => {
  const { session } = await window.__BBB__.shellState();
  session.recordPartyRound('drumline-dash', { score: 21000, accuracy: 0.81, rank: 'B' });
  session.recordPartyRound('chomp-chorus', { score: 30000, accuracy: 0.9, rank: 'B' });
});
await page.evaluate(() => window.__BBB__.goto('party'));
await wait(2000); await shot('hub-final-round');

await page.evaluate(async () => {
  const { session } = await window.__BBB__.shellState();
  session.recordPartyRound('finale-fever', { score: 52000, accuracy: 0.95, rank: 'A' });
});
await page.evaluate(() => window.__BBB__.goto('party'));
await wait(900); await shot('podium-in');
await wait(1600); await shot('podium');
const party = await page.evaluate(async () => {
  const { session } = await window.__BBB__.shellState();
  return { winner: session.party?.winner, standings: session.standings().map((p) => [p.name, p.points, p.wins]) };
});
console.log(JSON.stringify(party));
await writeFile(path.join(OUT, 'console.log'), logs.join('\n') || '(clean)');
console.log(logs.length ? logs.join('\n') : 'console clean');
await browser.close();
server.kill();
