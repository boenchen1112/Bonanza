// Swing Kings: prove the set gives no downbeat tell.
// Every pitch leaves on a beat. The ones on the downbeat are real balls, the rest are bombs; the machine, the
// midfield pip and the plate reticle must look the same for both (they only differ AFTER the player commits).
// This opens the game (no autoplay, nothing is ever swung, so pitches are never judged early), shoots a
// matched frame at the same fraction of the flight for balls and for bombs, and pixel-diffs fixed regions
// (pip, reticle, muzzle ring) ball-vs-bomb against the noise floor of bomb-vs-bomb / ball-vs-ball.
//   node Tools/downbeat-tell-check.mjs <buildDir> <outPrefix> [query] [perClass]   (perClass 4 or more: the pip amplitude series needs 8+ beats)
// Also checks the pip's amplitude: constant in size and brightness over every beat captured at the same flight fraction.
// env: PORT, VIEWPORT (1280x720), FRACS ("0.12,0.4,0.7" fractions of the launch-to-target time),
//      REGIONS (JSON {name:[x,y,w,h]} at 1280x720), BOMB_TAMPER=1 (no-op; reserved)
// Prints per region and fraction: mean abs diff (0-255) cross-class vs same-class, and the count of pixels
// differing by more than 24; exit 1 when a cross-class diff is clearly above its own noise floor.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from './node_modules/playwright/index.mjs';

const [DIR, PREFIX, QUERY = '?game=swing-kings&inputlog&debug', PER = '6'] = process.argv.slice(2);
const PORT = Number(process.env.PORT || 7991);
const [VW, VH] = (process.env.VIEWPORT || '1280x720').split('x').map(Number);
const FRACS = (process.env.FRACS || '0.12,0.4,0.7').split(',').map(Number);
const REGIONS = JSON.parse(process.env.REGIONS || '{"pip":[580,470,100,50],"reticle":[860,465,170,65],"muzzle":[290,300,110,110]}');
const TYPES = { '.html': 'text/html', '.js': 'application/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.data': 'application/octet-stream', '.png': 'image/png' };
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
const browser = await chromium.launch({ headless: true, args: ['--mute-audio', '--enable-gpu', '--use-angle=metal', '--ignore-gpu-blocklist'] });
const page = await browser.newPage({ viewport: { width: VW, height: VH } });
const per = Number(PER);
const shots = {};   // `${kind}@${frac}` -> [Buffer]
const counts = { ball: 0, bomb: 0 };
let busy = false, retiredAt = 0;
const errors = [];
const need = () => FRACS.every((f) => (shots[`ball@${f}`] || []).length >= per && (shots[`bomb@${f}`] || []).length >= per);
page.on('pageerror', (e) => errors.push(String(e.message)));
page.on('console', (m) => {
  const t = m.text();
  if (m.type() === 'error' || t.includes('[ERROR]')) errors.push(t);
  if (!t.includes('[SKV]')) return;
  if (t.includes('ev=retired') || t.includes('retired=1')) retiredAt = Date.now();
  const kv = Object.fromEntries(t.trim().split(/\s+/).filter((x) => x.includes('=')).map((x) => x.split('=')));
  if (kv.ev !== 'launch' || kv.free !== '0') return;
  // SIDE RETIRED dims the stage for ~2 s after a let-go downbeat: that follows the player's own miss, not the pitch under test
  if (Date.now() - retiredAt < 3500) return;
  const kind = kv.ball === '1' ? 'ball' : 'bomb';
  const frac = FRACS[counts[kind] % FRACS.length];
  const key = `${kind}@${frac}`;
  if (busy || (shots[key] || []).length >= per) return;
  busy = true;
  counts[kind]++;
  setTimeout(async () => {
    try { const b = await page.screenshot(); if (Date.now() - retiredAt >= 3500) (shots[key] ||= []).push(b); else counts[kind]--; } catch { /* skip */ }
    busy = false;
  }, Math.max(0, Number(kv.eta) * 1000 * frac));
});
await page.goto(`http://127.0.0.1:${PORT}/${QUERY}`, { waitUntil: 'load' });
for (let i = 0; i < 120; i++) {
  const ok = await page.evaluate(() => { const b = document.querySelector('#unity-loading-container'); return b && getComputedStyle(b).display === 'none'; });
  if (ok) break;
  await page.waitForTimeout(500);
}
await page.waitForTimeout(1500);
await page.mouse.click(VW / 2, VH / 2);
await page.waitForTimeout(2500);
await page.keyboard.press('Space');
const until = Date.now() + Number(process.env.MAXS || 400) * 1000;
while (!need() && Date.now() < until) await page.waitForTimeout(250);

for (const [k, list] of Object.entries(shots)) list.forEach((b, i) => fs.writeFileSync(`${PREFIX}-${k.replace('@', '-')}-${i}.png`, b));

// Diff in a scratch page: decode each PNG to pixels on a canvas.
const diffPage = await browser.newPage();
const sx = VW / 1280, sy = VH / 720;
const result = await diffPage.evaluate(async ({ shots, regions, sx, sy }) => {
  const load = (b64) => new Promise((res) => { const im = new Image(); im.onload = () => res(im); im.src = 'data:image/png;base64,' + b64; });
  const px = async (b64, r) => {
    const im = await load(b64);
    const c = document.createElement('canvas'); c.width = im.width; c.height = im.height;
    const g = c.getContext('2d'); g.drawImage(im, 0, 0);
    return g.getImageData(Math.round(r[0] * sx), Math.round(r[1] * sy), Math.round(r[2] * sx), Math.round(r[3] * sy)).data;
  };
  // The element under test is the saturated gold of the pip, reticle and muzzle ring; the crowd, turf and sway
  // behind it move on their own clock, so the masked image (everything else black) is what carries the proof.
  const gold = (a) => {
    const o = new Uint8ClampedArray(a.length);
    for (let i = 0; i < a.length; i += 4) {
      const r = a[i], g = a[i + 1], b = a[i + 2];
      if (r > 190 && g > 130 && r - b > 110 && r - g < 110) { o[i] = r; o[i + 1] = g; o[i + 2] = b; }
      o[i + 3] = 255;
    }
    return o;
  };
  const stat = (a) => { let n = 0, lum = 0; for (let i = 0; i < a.length; i += 4) if (a[i] || a[i + 1]) { n++; lum += a[i] + a[i + 1] + a[i + 2]; } return [n, n ? lum / (3 * n) : 0]; };
  const d = (a, b) => {
    let sum = 0, over = 0; const n = a.length / 4;
    for (let i = 0; i < a.length; i += 4) {
      let m = 0;
      for (let k = 0; k < 3; k++) { const e = Math.abs(a[i + k] - b[i + k]); sum += e; if (e > m) m = e; }
      if (m > 24) over++;
    }
    return { mean: sum / (3 * n), over };
  };
  const out = [];
  for (const [name, r] of Object.entries(regions)) {
    for (const key of Object.keys(shots)) {
      if (!key.startsWith('ball@')) continue;
      const frac = key.split('@')[1];
      const balls = (await Promise.all(shots[key].map((b) => px(b, r)))).map(gold);
      const bombs = (await Promise.all((shots['bomb@' + frac] || []).map((b) => px(b, r)))).map(gold);
      const cross = [], same = [];
      for (const a of balls) for (const b of bombs) cross.push(d(a, b));
      for (let i = 0; i < balls.length; i++) for (let j = i + 1; j < balls.length; j++) same.push(d(balls[i], balls[j]));
      for (let i = 0; i < bombs.length; i++) for (let j = i + 1; j < bombs.length; j++) same.push(d(bombs[i], bombs[j]));
      const avg = (l, f) => l.length ? l.reduce((s, x) => s + x[f], 0) / l.length : NaN;
      const sb = balls.map(stat), sm = bombs.map(stat);
      const m = (l, k) => l.reduce((t, x) => t + x[k], 0) / l.length;
      // amplitude per beat: every capture at this fraction of the flight, in launch order, as one series
      const all = [...balls, ...bombs].map(stat);
      const spread = (k) => (all.length ? Math.max(...all.map((x) => x[k])) - Math.min(...all.map((x) => x[k])) : 0);
      out.push({ region: name, frac, n: [balls.length, bombs.length], amp: { n: all.length, pxSpread: spread(0), pxMax: all.length ? Math.max(...all.map((x) => x[0])) : 0, lumSpread: spread(1) }, cross: [avg(cross, 'mean'), avg(cross, 'over')], same: [avg(same, 'mean'), avg(same, 'over')],
        px: [m(sb, 0), m(sm, 0)], lum: [m(sb, 1), m(sm, 1)] });
    }
  }
  return out;
}, { shots: Object.fromEntries(Object.entries(shots).map(([k, l]) => [k, l.map((b) => b.toString('base64'))])), regions: REGIONS, sx, sy });

let bad = false;
console.log('gold-masked pip/reticle/ring pixels. cross = ball vs bomb frame, same = ball-ball and bomb-bomb (noise floor); px/lum = gold pixel count and brightness (ball | bomb)');
console.log('region   frac  n(ball,bomb)  cross mean/over     same mean/over      px ball|bomb     lum ball|bomb');
for (const r of result) {
  const flag = r.cross[0] > r.same[0] * 1.5 + 0.2 || Math.abs(r.px[0] - r.px[1]) > 0.25 * Math.max(r.px[0], r.px[1], 1) + 8 || Math.abs(r.lum[0] - r.lum[1]) > 12;
  if (flag) bad = true;
  console.log(`${r.region.padEnd(8)} ${String(r.frac).padEnd(5)} ${r.n.join(',').padEnd(12)} ${r.cross[0].toFixed(3).padStart(8)} / ${r.cross[1].toFixed(1).padStart(6)}      ${r.same[0].toFixed(3).padStart(8)} / ${r.same[1].toFixed(1).padStart(6)}   ${r.px[0].toFixed(0).padStart(5)}|${r.px[1].toFixed(0).padEnd(5)}   ${r.lum[0].toFixed(0).padStart(4)}|${r.lum[1].toFixed(0).padEnd(4)}  ${flag ? 'TELL?' : 'ok'}`);
}
// The midfield diamond must be the same size and brightness on every beat: over 8+ consecutive beats at the same
// fraction of the flight its gold pixel count and brightness may not wander (they would if it pulsed with the bar).
for (const r of result.filter((x) => x.region === 'pip')) {
  const ampBad = r.amp.n < 8 || r.amp.pxSpread > 0.2 * r.amp.pxMax + 6 || r.amp.lumSpread > 10;
  if (ampBad) bad = true;
  console.log(`pip amplitude frac ${r.frac}: ${r.amp.n} beats, px spread ${r.amp.pxSpread.toFixed(0)} of ${r.amp.pxMax.toFixed(0)}, lum spread ${r.amp.lumSpread.toFixed(1)}  ${ampBad ? 'VARIES (or under 8 beats)' : 'constant'}`);
}
console.log('errors:', errors.length ? errors.join(' | ') : 'none');
await browser.close();
server.close();
process.exit(bad ? 1 : 0);
