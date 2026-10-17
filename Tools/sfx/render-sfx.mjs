// Bakes the web prototype's SFX kit (audio/index.js `SFX`) to one OGG per clip, through the real
// audio graph (createAudio) in a headless Chromium OfflineAudioContext, like Tools/music/render.mjs.
//
//   node Tools/sfx/render-sfx.mjs [--web <path to Bonanza-web/web>] [--out <dir>] [--q 3] [--no-ogg]
//
// The web SFX are chord-locked: `key()` reads the chord the music sits on. Here the player's
// scaleInfo() is replaced by a fixed key, one render per (context, chord) that the game can be on,
// so every clip is exactly what the web plays on that chord. The matrix below (USE) is what each
// context needs, read off the games' and shell's `sfx(` calls; Core/SfxCatalog.cs holds the same
// matrix and SfxCatalogTests pins the two together through the manifest this writes (sfx.json).
//
// Clip names:  <ctx>_<sfx>_<chordDeg>   chord-dependent (perfect great good miss impact ui uiBack)
//              <ctx>_count_<0|1>        count-in (1 = the accent) - root/scale only
//              <ctx>_combo_<level>      combo bell - root/scale only
//              <ctx>_<sfx>              fanfare, coin - root/scale only
//              tick, swoosh             no key
// Every clip starts at its scheduled instant: the graph's own delay (the compressor look-ahead)
// is cut off the front, and the tail is trimmed where it falls under -54 dBFS.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (k, d) => { const i = process.argv.indexOf('--' + k); return i < 0 ? d : process.argv[i + 1]; };
const webRoot = path.resolve(arg('web', path.join(here, '../../../Bonanza-web/web')));
const outDir = path.resolve(arg('out', path.join(here, '../../Assets/Game/Audio/Sfx')));
const oggQ = String(arg('q', '3'));
const wantOgg = !process.argv.includes('--no-ogg');
const ffmpeg = fs.existsSync(path.join(os.homedir(), '.local/bin/ffmpeg')) ? path.join(os.homedir(), '.local/bin/ffmpeg') : 'ffmpeg';
const SR = 44100;
const AT = 0.1;          // where in the render every sound is scheduled
const FLOOR = 0.002;     // -54 dBFS: the tail is cut where it falls under this
fs.mkdirSync(outDir, { recursive: true });

const CHORD_SFX = ['perfect', 'great', 'good', 'miss', 'impact'];
const USE = {
  'swing-kings':    { chord: CHORD_SFX, fixed: ['fanfare', 'coin'], count: true },
  'bounce-brigade': { chord: ['miss'], fixed: ['fanfare'], count: true },
  'chomp-chorus':   { chord: CHORD_SFX, fixed: ['fanfare'], count: true },
  'drumline-dash':  { chord: ['perfect', 'great', 'good', 'miss'], fixed: ['fanfare'], count: true },
  'finale-fever':   { chord: CHORD_SFX, fixed: ['fanfare', 'coin'], count: true, combo: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9] },
  'title':          { chord: ['ui', 'uiBack', 'perfect', 'great', 'miss'], fixed: ['fanfare'] },
  'results':        { chord: ['ui', 'perfect', 'great', 'good', 'miss'], fixed: [] },   // its fanfare is the title's (same root and scale)
};
const LEN = { perfect: 2.4, fanfare: 3.2, impact: 1.6, combo: 2.0 };

// ---- static server for the web repo (read only) --------------------------
const MIME = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.html': 'text/html', '.json': 'application/json' };
const server = http.createServer((req, res) => {
  const p = path.join(webRoot, decodeURIComponent(req.url.split('?')[0]));
  if (!p.startsWith(webRoot) || !fs.existsSync(p) || fs.statSync(p).isDirectory()) { res.writeHead(404); return res.end(); }
  res.writeHead(200, { 'content-type': MIME[path.extname(p)] || 'application/octet-stream' });
  fs.createReadStream(p).pipe(res);
});
await new Promise((r) => server.listen(0, '127.0.0.1', r));
const base = `http://127.0.0.1:${server.address().port}`;
const browser = await chromium.launch({ args: ['--autoplay-policy=no-user-gesture-required'] });
const page = await browser.newPage();
page.on('pageerror', (e) => console.error('pageerror', e.message));
page.on('console', (m) => { if (m.type() === 'error') console.error('console', m.text()); });
await page.route('**/__blank', (r) => r.fulfill({ contentType: 'text/html', body: '<!doctype html><title>r</title>' }));
await page.goto(base + '/__blank');

const rendered = await page.evaluate(async ({ base, SR, AT, USE, LEN }) => {
  const { createAudio, makeVirtualClock } = await import(base + '/src/audio/index.js');
  const { TRACKS } = await import(base + '/src/audio/music/index.js');
  const { SCALES, chord } = await import(base + '/src/audio/theory.js');

  const b64 = (f32) => {
    const i16 = new Int16Array(f32.length);
    for (let i = 0; i < f32.length; i++) i16[i] = Math.max(-32768, Math.min(32767, Math.round(f32[i] * 32767)));
    const u8 = new Uint8Array(i16.buffer);
    let s = ''; for (let i = 0; i < u8.length; i += 0x8000) s += String.fromCharCode.apply(null, u8.subarray(i, i + 0x8000));
    return btoa(s);
  };

  const jobs = [];
  jobs.push({ name: 'tick', sfx: 'tick', ctx: null, args: [0.12], len: 0.6 });
  jobs.push({ name: 'swoosh', sfx: 'swoosh', ctx: null, args: [], len: 1.4 });
  for (const [ctxId, u] of Object.entries(USE)) {
    const t = TRACKS[ctxId];
    const degs = [...new Set(t.prog)].sort((a, b) => a - b);
    for (const sfx of u.chord) for (const d of degs) jobs.push({ name: `${ctxId}_${sfx}_${d}`, sfx, ctx: ctxId, chordDeg: d, args: [], len: LEN[sfx] ?? 1.6 });
    for (const sfx of u.fixed) jobs.push({ name: `${ctxId}_${sfx}`, sfx, ctx: ctxId, chordDeg: t.prog[0], args: [], len: LEN[sfx] ?? 1.6 });
    if (u.count) for (const n of [0, 1]) jobs.push({ name: `${ctxId}_count_${n}`, sfx: 'count', ctx: ctxId, chordDeg: t.prog[0], args: [n], len: 1.0 });
    for (const lv of u.combo || []) jobs.push({ name: `${ctxId}_combo_${lv}`, sfx: 'combo', ctx: ctxId, chordDeg: t.prog[0], args: [lv], len: LEN.combo });
  }

  const out = [];
  for (const j of jobs) {
    const octx = new OfflineAudioContext(2, Math.ceil(SR * (AT + j.len)), SR);
    const clock = makeVirtualClock(120);
    const audio = createAudio({ ctx: octx, clock, bus: null, offline: true });
    audio.sfxBus.gain.value = 1;   // the SFX slider is applied at playback
    if (j.ctx) {
      const t = TRACKS[j.ctx];
      const scale = SCALES[t.scale];
      const key = { root: t.root, scale, scaleName: t.scale, chordDeg: j.chordDeg, chord: chord(t.root, scale, j.chordDeg, t.shape || [0, 2, 4]), bar: 0, intensity: 1 };
      audio.music.player.scaleInfo = () => key;
    }
    clock.setNow(0);
    audio.sfx(j.sfx, AT, ...j.args);
    const buf = await octx.startRendering();
    out.push({ ...j, L: b64(buf.getChannelData(0)), R: b64(buf.getChannelData(1)) });
  }
  return out;
}, { base, SR, AT, USE, LEN });
await browser.close();
server.close();

// ---- trim, write -----------------------------------------------------------
const dec = (s) => { const b = Buffer.from(s, 'base64'); return new Int16Array(b.buffer, b.byteOffset, b.length / 2); };
function wav(L, R) {
  const n = L.length, buf = Buffer.alloc(44 + n * 4);
  buf.write('RIFF', 0); buf.writeUInt32LE(36 + n * 4, 4); buf.write('WAVEfmt ', 8);
  buf.writeUInt32LE(16, 16); buf.writeUInt16LE(1, 20); buf.writeUInt16LE(2, 22);
  buf.writeUInt32LE(SR, 24); buf.writeUInt32LE(SR * 4, 28); buf.writeUInt16LE(4, 32); buf.writeUInt16LE(16, 34);
  buf.write('data', 36); buf.writeUInt32LE(n * 4, 40);
  for (let i = 0; i < n; i++) { buf.writeInt16LE(L[i], 44 + i * 4); buf.writeInt16LE(R[i], 46 + i * 4); }
  return buf;
}
// The graph's own delay: where the first sample above 1% of the tick's peak falls, past the scheduled instant.
const tick = rendered.find((r) => r.name === 'tick');
const tl = dec(tick.L);
let pk = 0; for (const v of tl) pk = Math.max(pk, Math.abs(v));
let onset = 0; while (onset < tl.length && Math.abs(tl[onset]) < pk * 0.01) onset++;
const delay = Math.max(0, onset - Math.round(AT * SR));
console.log(`graph delay ${delay} samples = ${(delay / SR * 1000).toFixed(2)} ms (cut from every clip)`);

const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'sfx-'));
const manifest = { generator: 'Tools/sfx/render-sfx.mjs', sampleRate: SR, graphDelaySamples: delay, vorbisQuality: Number(oggQ), clips: [] };
let total = 0;
for (const r of rendered) {
  const L = dec(r.L), R = dec(r.R);
  const from = Math.round(AT * SR) + delay;
  let end = L.length;
  while (end > from && Math.abs(L[end - 1]) < FLOOR * 32767 && Math.abs(R[end - 1]) < FLOOR * 32767) end--;
  end = Math.min(L.length, end + Math.round(0.02 * SR));
  const n = end - from;
  const l = new Int16Array(n), rr = new Int16Array(n);
  const fade = Math.min(n, Math.round(0.03 * SR));
  let peak = 0;
  for (let i = 0; i < n; i++) {
    const g = i >= n - fade ? (n - i) / fade : 1;
    l[i] = Math.round(L[from + i] * g); rr[i] = Math.round(R[from + i] * g);
    peak = Math.max(peak, Math.abs(l[i]), Math.abs(rr[i]));
  }
  const rec = { name: r.name, sfx: r.sfx, ctx: r.ctx, chordDeg: r.chordDeg ?? null, arg: r.args.length ? r.args[0] : null, seconds: +(n / SR).toFixed(3), peak: +(peak / 32767).toFixed(4) };
  if (peak < 30) console.error('SILENT clip', r.name);
  if (wantOgg) {
    const w = path.join(tmp, r.name + '.wav'), o = path.join(outDir, r.name + '.ogg');
    fs.writeFileSync(w, wav(l, rr));
    const p = spawnSync(ffmpeg, ['-y', '-v', 'error', '-i', w, '-c:a', 'libvorbis', '-q:a', oggQ, o]);
    if (p.status !== 0) { console.error('ffmpeg failed', r.name, String(p.stderr)); process.exitCode = 1; continue; }
    rec.bytes = fs.statSync(o).size; total += rec.bytes;
  }
  manifest.clips.push(rec);
}
manifest.clips.sort((a, b) => (a.name < b.name ? -1 : 1));
manifest.totalBytes = total;
fs.writeFileSync(path.join(outDir, 'sfx.json'), JSON.stringify(manifest, null, 1) + '\n');
fs.rmSync(tmp, { recursive: true, force: true });
console.log(`wrote ${manifest.clips.length} clips, ${(total / 1048576).toFixed(2)} MB (${(manifest.clips.reduce((s, c) => s + c.seconds, 0)).toFixed(1)} s) to ${outDir}`);
