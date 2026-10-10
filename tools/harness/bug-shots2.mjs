#!/usr/bin/env node
/**
 * Reproduce "TUFF lies flat / small cast figures float" via a full PARTY
 * lineup: P1 human picks last (after CPUs auto-pick), and an unlock/re-pick
 * cycle, since a fresh single free-play pick didn't show it.
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
const OUT = path.resolve(ROOT, String(argv.out || 'runs/bugs2'));
mkdirSync(OUT, { recursive: true });

const webRequire = createRequire(path.join(WEB, 'package.json'));
const pw = await import(pathToFileURL(webRequire.resolve('playwright')).href);
const chromium = pw.chromium ?? pw.default.chromium;
const PORT = 8600 + (process.pid % 400);
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

await page.evaluate(async () => {
  const { session } = await window.__BBB__.shellState();
  session.startParty(['swing-kings', 'drumline-dash'], 4, 42);
});
await page.evaluate(() => window.__BBB__.goto('roster', { mode: 'party' }));
await wait(800);
await page.screenshot({ path: path.join(OUT, '00-lineup.png') });
// lineup stage: confirm defaults (P1 you, 2 CPU normal, 1 off) straight through
await page.keyboard.press('Space');
await wait(300);
await page.screenshot({ path: path.join(OUT, '01-chars-start.png') });

// P1 picks a character (move a few cards, confirm) -- rapid frames after
for (let i = 0; i < 3; i++) { await page.keyboard.press('ArrowRight'); await wait(80); }
await page.keyboard.press('Space');
for (let i = 0; i < 8; i++) { await wait(40); await page.screenshot({ path: path.join(OUT, `02-p1lock-${String(i).padStart(2, '0')}.png`) }); }

// Undo P1's pick and re-pick a DIFFERENT character (unlock -> lockIn cycle)
await page.keyboard.press('KeyX');
await wait(300);
await page.screenshot({ path: path.join(OUT, '03-after-undo-attempt.png') });

// let CPUs auto-pick (should progress automatically after P1 stage ends)
await wait(4000);
await page.screenshot({ path: path.join(OUT, '04-cpu-picks.png') });
for (let i = 0; i < 6; i++) { await wait(400); await page.screenshot({ path: path.join(OUT, `05-progress-${i}.png`) }); }

console.log('shots -> ' + OUT);
await browser.close();
server.kill();
