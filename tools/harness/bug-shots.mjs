#!/usr/bin/env node
/**
 * Shoot the three known visual bugs directly: bloom on white heads/limbs
 * (a minigame mid-play), the roster lock-in moment, and the results screen's
 * empty lower frame.
 *
 *   node tools/harness/bug-shots.mjs --dist dist-bugs --out runs/bugs
 */
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { spawn } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import path from 'node:path';

const argv = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, arr) => {
  if (a.startsWith('--')) acc.push([a.slice(2), arr[i + 1] && !arr[i + 1].startsWith('--') ? arr[i + 1] : true]);
  return acc;
}, []));
const ROOT = path.resolve(import.meta.dirname, '../..');
const WEB = path.join(ROOT, 'web');
const DIST = path.join(WEB, String(argv.dist || 'dist-bugs'));
const OUT = path.resolve(ROOT, String(argv.out || 'runs/bugs'));
mkdirSync(OUT, { recursive: true });

const webRequire = createRequire(path.join(WEB, 'package.json'));
const pw = await import(pathToFileURL(webRequire.resolve('playwright')).href);
const chromium = pw.chromium ?? pw.default.chromium;
const PORT = 8200 + (process.pid % 400);
const server = spawn(process.execPath, [path.join(ROOT, 'tools/harness/serve.mjs'), DIST, String(PORT)], { stdio: ['ignore', 'pipe', 'pipe'] });
await new Promise((r) => { server.stdout.once('data', r); setTimeout(r, 1200); });

const browser = await chromium.launch({
  channel: 'chromium', headless: true,
  args: ['--autoplay-policy=no-user-gesture-required', '--mute-audio', ...(process.platform === 'win32' ? ['--use-angle=d3d11'] : [])],
});
const page = await (await browser.newContext({ viewport: { width: 1280, height: 720 } })).newPage();
page.on('pageerror', (e) => console.log('[pageerror]', e.message));

const wait = (ms) => new Promise((r) => setTimeout(r, ms));
await page.goto(`http://127.0.0.1:${PORT}/index.html?scene=title&quality=high`, { waitUntil: 'load' });
await page.keyboard.press('KeyQ');
await page.waitForFunction('window.__BBB__ && window.__BBB__.ready', null, { timeout: 30000 });
await page.evaluate(() => window.__BBB__.ready);

// --- 1. Bloom on white heads/limbs: swing-kings mid-play, several shots ---
await page.evaluate(() => window.__BBB__.goto('swing-kings'));
await wait(2500);
await page.screenshot({ path: path.join(OUT, '01-swingkings-a.png') });
await page.evaluate(() => window.__BBB__.autoplay({ mode: 'auto', chart: true, seconds: 10 }));
await wait(3000);
await page.screenshot({ path: path.join(OUT, '01-swingkings-b.png') });
await wait(3000);
await page.screenshot({ path: path.join(OUT, '01-swingkings-c.png') });
await page.evaluate(() => window.__BBB__.stopAutoplay());

// --- 2. Roster lock-in: stage a lineup, watch P1 lock a character ---
await page.evaluate(() => window.__BBB__.goto('title'));
await wait(500);
await page.evaluate(async () => {
  const { session } = await window.__BBB__.shellState();
  session.setPlayers([{ name: 'P1', char: 'tuff', isCpu: false, cpuSkill: 0 }]);
  session.mode = 'free';
});
await page.evaluate(() => window.__BBB__.goto('roster', { mode: 'free' }));
await wait(1200);
await page.screenshot({ path: path.join(OUT, '02-roster-grid.png') });
// Move to TUFF's card and lock in. Cursor order matches CHARS array; press
// right a few times to reach a non-default character (tuff), then confirm.
for (let i = 0; i < 3; i++) { await page.keyboard.press('ArrowRight'); await wait(120); }
await page.screenshot({ path: path.join(OUT, '03-roster-precursor.png') });
await page.keyboard.press('Space');
// Shoot every ~50ms for the first half second to catch the exact lock-in frame.
for (let i = 0; i < 10; i++) {
  await wait(50);
  await page.screenshot({ path: path.join(OUT, `04-lockin-${String(i).padStart(2, '0')}.png`) });
}
await wait(1000);
await page.screenshot({ path: path.join(OUT, '05-roster-after.png') });

// --- 3. Results screen empty lower frame ---
await page.evaluate(async () => {
  const { session } = await window.__BBB__.shellState();
  session.setPlayers([{ name: 'P1', char: 'tuff', isCpu: false, cpuSkill: 0 }]);
});
await page.evaluate(() => window.__BBB__.goto('results', {
  game: 'swing-kings',
  result: { score: 42000, accuracy: 0.81, rank: 'A', stats: { perfect: 20, great: 9, good: 4, miss: 3, maxCombo: 17 } },
}));
await wait(3000);
await page.screenshot({ path: path.join(OUT, '06-results.png'), fullPage: false });

console.log('shots -> ' + OUT);
await browser.close();
server.kill();
