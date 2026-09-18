#!/usr/bin/env node
/**
 * Same idea as freeze-probe.mjs, but matches sweep.mjs exactly: one cold
 * page, one scene loaded via URL, `--play auto` running for the whole
 * window. Use this when a freeze only shows up under the harness's own
 * auto-play bot, not under manual navigation.
 *
 *   node tools/harness/freeze-auto-probe.mjs --scene title --dist dist-fix1 --seconds 6 --no-build
 */
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';

const argv = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, arr) => {
  if (a.startsWith('--')) acc.push([a.slice(2), arr[i + 1] && !arr[i + 1].startsWith('--') ? arr[i + 1] : true]);
  return acc;
}, []));
const ROOT = path.resolve(import.meta.dirname, '../..');
const WEB = path.join(ROOT, 'web');
const DIST = path.join(WEB, String(argv.dist || 'dist-prof'));
const OUT = path.resolve(ROOT, String(argv.out || 'runs/freeze-auto'));
const SCENE = String(argv.scene || 'title');
const SECONDS = Number(argv.seconds || 6);
mkdirSync(OUT, { recursive: true });

if (argv.build) {
  await new Promise((res, rej) => {
    const p = spawn(process.execPath, [path.join(WEB, 'node_modules/vite/bin/vite.js'), 'build', '--mode', 'harness', '--outDir', DIST], { cwd: WEB, stdio: 'inherit' });
    p.on('close', (c) => (c === 0 ? res() : rej(new Error('build failed'))));
  });
}

const webRequire = createRequire(path.join(WEB, 'package.json'));
const pw = await import(pathToFileURL(webRequire.resolve('playwright')).href);
const chromium = pw.chromium ?? pw.default.chromium;
const PORT = 7900 + (process.pid % 400);
const server = spawn(process.execPath, [path.join(ROOT, 'tools/harness/serve.mjs'), DIST, String(PORT)], { stdio: ['ignore', 'pipe', 'pipe'] });
await new Promise((r) => { server.stdout.once('data', r); setTimeout(r, 1200); });

const browser = await chromium.launch({
  channel: 'chromium', headless: true,
  args: ['--autoplay-policy=no-user-gesture-required', '--mute-audio', ...(process.platform === 'win32' ? ['--use-angle=d3d11'] : [])],
});
const page = await (await browser.newContext({ viewport: { width: 1280, height: 720 } })).newPage();
const errors = [];
page.on('pageerror', (e) => errors.push(e.message));
page.on('console', (m) => { if (m.text().includes('TEMP-PROFILE')) console.log(`  [t=${Math.round(performance.now?.() ?? 0)}] ${m.text()}`); });
const cdp = await page.context().newCDPSession(page);
await cdp.send('Profiler.enable');
await cdp.send('Profiler.setSamplingInterval', { interval: 200 });

await page.goto(`http://127.0.0.1:${PORT}/index.html?scene=${SCENE}&quality=high`, { waitUntil: 'load' });
await page.keyboard.press('KeyQ');
await page.waitForFunction('window.__BBB__ && window.__BBB__.ready', null, { timeout: 30000 });
await page.evaluate(() => window.__BBB__.ready);

await page.evaluate(() => {
  window.__FP__ = { frames: [], scenes: [] };
  let last = performance.now();
  let lastScene = window.__BBB__.scene;
  window.__FP__.scenes.push([0, lastScene]);
  const tick = (t) => {
    window.__FP__.frames.push([t, t - last]);
    last = t;
    const sc = window.__BBB__.scene;
    if (sc !== lastScene) { lastScene = sc; window.__FP__.scenes.push([t, sc]); }
    requestAnimationFrame(tick);
  };
  requestAnimationFrame(tick);
});

const wait = (ms) => new Promise((r) => setTimeout(r, ms));
await cdp.send('Profiler.start');
await page.evaluate((s) => window.__BBB__.autoplay({ mode: 'auto', seconds: s }), SECONDS);
await wait(SECONDS * 1000 + 300);
const { profile } = await cdp.send('Profiler.stop');
const log = await page.evaluate(() => window.__FP__);

function topSelf(profile, n = 16) {
  const byId = new Map(profile.nodes.map((x) => [x.id, x]));
  const dt = profile.timeDeltas;
  const self = new Map();
  for (let i = 0; i < profile.samples.length; i++) {
    const node = byId.get(profile.samples[i]);
    if (!node) continue;
    const cf = node.callFrame;
    const file = (cf.url || '').split('/').pop() || '';
    const key = `${cf.functionName || '(anonymous)'}  ${file}:${cf.lineNumber + 1}`;
    self.set(key, (self.get(key) || 0) + (dt[i] || 0) / 1000);
  }
  return [...self.entries()].sort((a, b) => b[1] - a[1]).slice(0, n).map(([k, ms]) => ({ fn: k, ms: Math.round(ms) }));
}

// Find the worst frame and what scene was active then, plus what scenes were
// visited (auto-play may navigate through several shell screens).
const frames = log.frames.slice(1);
let worst = frames[0];
for (const f of frames) if (f[1] > worst[1]) worst = f;
const sceneAt = (t) => {
  let cur = log.scenes[0][1];
  for (const [st, sc] of log.scenes) { if (st <= t) cur = sc; else break; }
  return cur;
};
const stat = (a) => {
  const s = [...a].sort((x, y) => x - y);
  return { n: s.length, p50: Math.round(s[s.length >> 1]), p95: Math.round(s[Math.floor(s.length * 0.95)]), max: Math.round(s[s.length - 1]) };
};

console.log(`scene(url)=${SCENE}  frames=${frames.length}  ${JSON.stringify(stat(frames.map((f) => f[1])))}`);
console.log('scenes visited:', log.scenes.map(([t, s]) => `${Math.round(t)}ms:${s}`).join(' -> '));
console.log(`worst frame: ${Math.round(worst[1])}ms at t=${Math.round(worst[0])}ms, scene was "${sceneAt(worst[0])}"`);
console.log('top self time across the whole run:');
for (const t of topSelf(profile)) console.log(`  ${String(t.ms).padStart(5)}ms  ${t.fn}`);

writeFileSync(path.join(OUT, `${SCENE}.cpuprofile`), JSON.stringify(profile));
writeFileSync(path.join(OUT, `${SCENE}.json`), JSON.stringify({ scene: SCENE, scenes: log.scenes, worst, errors }, null, 2));
if (errors.length) console.log('page errors:', errors.slice(0, 5));
await browser.close();
server.kill();
