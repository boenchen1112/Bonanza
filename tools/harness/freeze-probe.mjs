#!/usr/bin/env node
/**
 * Freeze probe: where do the long frames on the menus come from?
 *
 * The sweep boots each scene cold at page load, so its worst frame mixes the
 * page's own startup into the scene's. This walks the real swaps a player
 * makes (title -> freeplay -> roster -> results -> title ...) in one page and,
 * for every swap, records:
 *   - every rAF frame delta, split into ENTRY (the swap itself and the first
 *     1.5s after) and STEADY (after that),
 *   - long tasks (> 50ms) the browser reports, with their start time,
 *   - a CPU profile of the swap, reduced to the functions with the most
 *     self time (build with --minify false so the names are readable).
 *
 *   node tools/harness/freeze-probe.mjs --dist dist-prof --out runs/freeze --build
 *   node tools/harness/freeze-probe.mjs --scenes title,freeplay,roster --dwell 6
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
const OUT = path.resolve(ROOT, String(argv.out || 'runs/freeze-probe'));
const DWELL = Number(argv.dwell || 5);
const SCENES = String(argv.scenes || 'title,freeplay,roster,options,title,freeplay,results,title').split(',');
mkdirSync(OUT, { recursive: true });

if (argv.build) {
  await new Promise((res, rej) => {
    const args = [path.join(WEB, 'node_modules/vite/bin/vite.js'), 'build', '--mode', 'harness', '--outDir', DIST];
    if (!argv.minify) args.push('--minify', 'false');
    const p = spawn(process.execPath, args, { cwd: WEB, stdio: 'inherit' });
    p.on('close', (c) => (c === 0 ? res() : rej(new Error('build failed'))));
  });
}

const webRequire = createRequire(path.join(WEB, 'package.json'));
const pw = await import(pathToFileURL(webRequire.resolve('playwright')).href);
const chromium = pw.chromium ?? pw.default.chromium;
const PORT = 7400 + (process.pid % 400);
const server = spawn(process.execPath, [path.join(ROOT, 'tools/harness/serve.mjs'), DIST, String(PORT)], { stdio: ['ignore', 'pipe', 'pipe'] });
await new Promise((r) => { server.stdout.once('data', r); setTimeout(r, 1200); });

const browser = await chromium.launch({
  channel: 'chromium', headless: true,
  args: ['--autoplay-policy=no-user-gesture-required', '--mute-audio', ...(process.platform === 'win32' ? ['--use-angle=d3d11'] : [])],
});
const page = await (await browser.newContext({ viewport: { width: 1280, height: 720 } })).newPage();
const errors = [];
page.on('pageerror', (e) => errors.push(e.message));
const cdp = await page.context().newCDPSession(page);
await cdp.send('Profiler.enable');
await cdp.send('Profiler.setSamplingInterval', { interval: 200 });   // microseconds

await page.goto(`http://127.0.0.1:${PORT}/index.html?scene=title&quality=high`, { waitUntil: 'load' });
await page.keyboard.press('KeyQ');
await page.waitForFunction('window.__BBB__ && window.__BBB__.ready', null, { timeout: 30000 });
await page.evaluate(() => window.__BBB__.ready);

// In-page frame + long-task log. performance.now() is right HERE: this is
// measuring the browser's frame pacing, not judging gameplay.
await page.evaluate(() => {
  const w = window;
  w.__FP__ = { frames: [], longtasks: [] };
  let last = performance.now();
  const tick = (t) => { w.__FP__.frames.push([t, t - last]); last = t; requestAnimationFrame(tick); };
  requestAnimationFrame(tick);
  try {
    new PerformanceObserver((l) => {
      for (const e of l.getEntries()) w.__FP__.longtasks.push([e.startTime, e.duration]);
    }).observe({ type: 'longtask', buffered: false });
  } catch { /* no longtask support */ }
});
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
await wait(2500);

/** Sum self time per function across a V8 CPU profile. */
function topSelf(profile, n = 14) {
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

const opts = {
  results: { game: 'swing-kings', result: { score: 42000, accuracy: 0.81, rank: 'A', stats: { perfect: 20, great: 9, good: 4, miss: 3, maxCombo: 17 } } },
};

const report = [];
for (let i = 0; i < SCENES.length; i++) {
  const scene = SCENES[i];
  const t0 = await page.evaluate(() => performance.now());
  await cdp.send('Profiler.start');
  const swapMs = await page.evaluate(async ({ scene, o }) => {
    const s = performance.now();
    await window.__BBB__.goto(scene, o || {});
    return performance.now() - s;
  }, { scene, o: opts[scene] });
  await wait(1500);
  const { profile } = await cdp.send('Profiler.stop');
  // Steady state: a second profile once the swap has settled, and the
  // renderer's resource counts — a scene that gets slower on every revisit
  // shows up here, not in the swap.
  await page.evaluate(() => window.__BBB__.resetTelemetry());
  const progBefore = await page.evaluate(() => window.__BBB__.stage.renderer.info.programs?.length ?? -1);
  await cdp.send('Profiler.start');
  await wait(2000);
  const { profile: steadyProfile } = await cdp.send('Profiler.stop');
  const progAfter = await page.evaluate(() => window.__BBB__.stage.renderer.info.programs?.length ?? -1);
  await wait(Math.max(0, DWELL * 1000 - 3500));
  const tele = await page.evaluate(() => window.__BBB__.telemetry());
  const t1 = await page.evaluate(() => performance.now());

  const log = await page.evaluate(() => window.__FP__);
  const frames = log.frames.filter(([t]) => t >= t0 && t <= t1);
  const entry = frames.filter(([t]) => t < t0 + swapMs + 1500).map((f) => f[1]);
  const steady = frames.filter(([t]) => t >= t0 + swapMs + 1500).map((f) => f[1]);
  const lt = log.longtasks.filter(([t]) => t >= t0 && t <= t1).map(([t, d]) => ({ at: Math.round(t - t0), ms: Math.round(d) }));
  const stat = (a) => {
    if (!a.length) return null;
    const s = [...a].sort((x, y) => x - y);
    return {
      n: s.length, p50: Math.round(s[s.length >> 1]), p95: Math.round(s[Math.floor(s.length * 0.95)]),
      max: Math.round(s[s.length - 1]), over50: s.filter((x) => x > 50).length, over100: s.filter((x) => x > 100).length,
    };
  };
  const prof = `${String(i).padStart(2, '0')}-${scene}.cpuprofile`;
  writeFileSync(path.join(OUT, prof), JSON.stringify(profile));
  const r = tele?.render || {};
  const row = {
    i, scene, swapMs: Math.round(swapMs), entry: stat(entry), steady: stat(steady), longtasks: lt,
    render: { draws: r.drawCalls, tris: r.triangles, programs: r.programs, geometries: r.geometries, textures: r.textures },
    cpu: tele?.cpuMs ? { p50: Math.round(tele.cpuMs.p50 ?? 0), p95: Math.round(tele.cpuMs.p95 ?? 0), max: Math.round(tele.cpuMs.max ?? 0) } : null,
    dom: await page.evaluate(() => document.getElementsByTagName('*').length),
    top: topSelf(profile), topSteady: topSelf(steadyProfile),
  };
  report.push(row);
  console.log(`\n[${i}] ${scene}  swap ${row.swapMs}ms`);
  console.log('  entry ', JSON.stringify(row.entry));
  console.log('  steady', JSON.stringify(row.steady), 'cpu', JSON.stringify(row.cpu));
  console.log('  programs before/after the 2s steady window:', progBefore, '->', progAfter);
  console.log('  render', JSON.stringify(row.render), 'dom nodes', row.dom);
  console.log('  long tasks', JSON.stringify(lt.slice(0, 8)));
  console.log('  top self time, swap + 1.5s:');
  for (const t of row.top.slice(0, 6)) console.log(`    ${String(t.ms).padStart(5)}ms  ${t.fn}`);
  console.log('  top self time, steady 2s:');
  for (const t of row.topSteady.slice(0, 8)) console.log(`    ${String(t.ms).padStart(5)}ms  ${t.fn}`);
}

writeFileSync(path.join(OUT, 'freeze.json'), JSON.stringify({ scenes: SCENES, dwell: DWELL, report, errors }, null, 2));
if (errors.length) console.log('\npage errors:', errors.slice(0, 5));
await browser.close();
server.kill();
