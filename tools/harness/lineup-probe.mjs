#!/usr/bin/env node
/**
 * Lineup probe: stages a session lineup through `__BBB__.shellState()` and
 * boots every minigame through the shell's `play` host (the path Free Play
 * and a party take), so the picked character and the named CPU rivals should
 * be on screen. Shoots each game ~3s in and reports the worst frame of the
 * scene swap (the entry hitch), plus a sampled draw-call range and an
 * env-root count over the following few seconds.
 *
 * The env-root/draw-call sampling matters specifically because this is the
 * *real* Free Play -> Play route, not a direct scene launch: `inspect.mjs`'s
 * critic gate only ever launches a scene directly, which never exercises the
 * async load()-vs-render() window a real nav does. Two real bugs (a
 * duplicate default env, and a lineup crest draw-call cost that only shows
 * up with a full party) went unnoticed through 28+ tickets' worth of
 * direct-launch gating before this route caught them. A single draw-call
 * *snapshot* isn't enough either -- a scene's peak can hide on an unlucky
 * frame (see ticket 28's history) -- so this samples over ~2s and reports
 * min/p50/max, not one number.
 *
 *   node tools/harness/lineup-probe.mjs --dist dist-lineup --out runs/lineup --build
 *   node tools/harness/lineup-probe.mjs --hero tuff --rivals zizz,mimo,nibb
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
const DIST = path.join(WEB, String(argv.dist || 'dist-lineup'));
const OUT = path.resolve(ROOT, String(argv.out || 'runs/lineup-probe'));
const HERO = String(argv.hero || 'tuff');
const RIVALS = String(argv.rivals || 'zizz,mimo,nibb').split(',');
const GAMES = String(argv.games || 'swing-kings,drumline-dash,bounce-brigade,finale-fever,chomp-chorus').split(',');
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
page.on('console', async (m) => {
  if (m.type() !== 'error' && m.type() !== 'warning') return;
  const stack = await Promise.all(m.args().map((a) => a.evaluate((v) => (v && v.stack) || String(v)).catch(() => '')));
  logs.push(`[${m.type()}] ${m.text()}\n${stack.join('\n')}`);
});
page.on('pageerror', (e) => logs.push(`[pageerror] ${e.message}\n${e.stack}`));
await page.goto(`http://127.0.0.1:${PORT}/index.html?scene=title&quality=high`, { waitUntil: 'load' });
await page.keyboard.press('KeyQ');
await page.waitForFunction('window.__BBB__ && window.__BBB__.ready', null, { timeout: 20000 });
await page.evaluate(() => window.__BBB__.ready);
const wait = (ms) => new Promise((r) => setTimeout(r, ms));

await page.evaluate(async ({ hero, rivals }) => {
  const { session } = await window.__BBB__.shellState();
  const skills = [0.86, 0.63, 0.4];
  session.setPlayers([
    { name: 'P1', char: hero, isCpu: false, cpuSkill: 0 },
    ...rivals.map((c, i) => ({ name: c.toUpperCase(), char: c, isCpu: true, cpuSkill: skills[i % 3] })),
  ]);
  session.mode = 'free';
}, { hero: HERO, rivals: RIVALS });

const report = [];
let n = 0;
for (const game of GAMES) {
  await page.evaluate(() => window.__BBB__.goto('freeplay'));
  await wait(1500);
  await page.evaluate(() => window.__BBB__.resetTelemetry());
  // goto() resolves once load() + start() ran, so its duration is the load;
  // the worst frame after it is the first-render cost (shader compiles).
  const loadMs = await page.evaluate(async (g) => {
    const t0 = performance.now();
    await window.__BBB__.goto('play', { game: g, from: 'freeplay' });
    return performance.now() - t0;
  }, game);
  const during = await page.evaluate(() => window.__BBB__.telemetry());
  await page.evaluate(() => window.__BBB__.resetTelemetry());
  await wait(3200);
  const s = await page.evaluate(() => window.__BBB__.telemetry());
  const f = `${String(n++).padStart(2, '0')}-${game}.png`;
  await page.screenshot({ path: path.join(OUT, f) });

  // Sampled over ~2s (not one snapshot -- a peak can hide on an unlucky
  // frame) and env-root count (>1 means a stale env never got torn down --
  // see ticket 29).
  const draws = await page.evaluate((secs) => new Promise((resolve) => {
    const st = window.__BBB__.stage;
    const v = [];
    const t0 = performance.now();
    const tick = () => {
      v.push(st.stats.drawCalls);
      if (performance.now() - t0 < secs * 1000) requestAnimationFrame(tick); else resolve(v);
    };
    requestAnimationFrame(tick);
  }), 2);
  const sortedDraws = draws.slice().sort((a, b) => a - b);
  const envRoots = await page.evaluate(() => {
    const st = window.__BBB__.stage;
    const roots = [];
    st.scene.traverse((o) => { if (o.name === 'env:root') roots.push(o.children.map((c) => c.name || c.type).join('+')); });
    return roots;
  });

  const row = {
    game, shot: f, loadMs: Math.round(loadMs), frameMaxLoad: Math.round(during.frameMs?.max || 0), framesLoad: during.frameCount, frameMaxAfter: Math.round(s.frameMs.max), cpuMax: Math.round(s.cpuMs.max), frames: s.frameCount,
    draws: { min: sortedDraws[0], p50: sortedDraws[sortedDraws.length >> 1], max: sortedDraws[sortedDraws.length - 1] },
    envRoots: envRoots.length, envs: envRoots,
  };
  report.push(row);
  console.log(JSON.stringify(row));
  if (envRoots.length > 1) console.log(`  WARNING: ${envRoots.length} env:root nodes (expected 1) -- ${game}`);
}

// --pause: pause the last game through the real menu (Escape, then confirm
// on RESUME) and report whether its music track is playing again after.
if (argv.pause) {
  const musicOn = () => page.evaluate(() => {
    const m = window.__BBB__.audio?.music;
    return m ? `${m.playing ? 'playing' : 'stopped'}:${m.track?.id ?? '-'}` : 'no music facade';
  });
  const before = await musicOn();
  await page.keyboard.press('Escape'); await wait(900);
  const during = await musicOn();
  await page.keyboard.press('Space'); await wait(2600);
  const after = await musicOn();
  console.log('pause', JSON.stringify({ before, during, after }));
  report.push({ pause: { before, during, after } });
}

// --race: run Drumline Dash to the end as round 1 of a party and report how
// the party scored it (the race's real places, not a simulation).
let race = null;
if (argv.race) {
  await page.evaluate(async () => {
    const { session } = await window.__BBB__.shellState();
    session.startParty(['drumline-dash', 'finale-fever'], 2, 77);
  });
  await page.evaluate(() => window.__BBB__.goto('play', { game: 'drumline-dash', from: 'party' }));
  await page.evaluate(() => window.__BBB__.autoplay({ mode: 'auto', chart: true, seconds: 400 }));
  const t0 = Date.now();
  while (Date.now() - t0 < 240000 && (await page.evaluate(() => window.__BBB__.scene)) !== 'results') await wait(250);
  await page.evaluate(() => window.__BBB__.stopAutoplay());   // or it presses through the card
  await wait(1200);
  await page.screenshot({ path: path.join(OUT, `${String(n++).padStart(2, '0')}-race-results.png`) });
  race = await page.evaluate(async () => {
    const { session } = await window.__BBB__.shellState();
    return { round: session.party?.scores?.[0], players: session.players.map((p) => [p.name, p.points]) };
  });
  console.log('race', JSON.stringify(race));
}

await writeFile(path.join(OUT, 'lineup.json'), JSON.stringify({ hero: HERO, rivals: RIVALS, report, race, logs }, null, 2));
if (logs.length) console.log(logs.slice(0, 20).join('\n'));
await browser.close();
server.kill();
