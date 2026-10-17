// Renders the FULL-LENGTH, NON-LOOPING Finale Fever track with the web's
// real audio graph while the virtual clock follows the game's tempo ramp
// (clock.setBpm(bpmAt(beat)) every 1/60 s, exactly like finaleFever/index.js).
// Pitch never changes: the tempo only moves the schedule, not the voices.
//
//   node Tools/music/render-ramped.mjs [--web <Bonanza-web/web>] [--out <dir>] [--q 4] [--no-ogg]
//
// File time 0 == music beat 0 == GAME beat -8 (the first lead-in bar line;
// "file beat = game beat + 8", as in MusicPlayer.PlayGame). Output:
//   out/finale-fever-ramped.i0..i3.{wav,ogg}   cumulative mixes (as render.mjs)
//   out/finale-fever-ramped.json               beat map + verification
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
const outDir = path.resolve(arg('out', path.join(here, 'out')));
const wantOgg = !process.argv.includes('--no-ogg');
const oggQ = String(arg('q', '4'));
const ffmpeg = fs.existsSync(path.join(os.homedir(), '.local/bin/ffmpeg')) ? path.join(os.homedir(), '.local/bin/ffmpeg') : 'ffmpeg';
const SR = 44100, TAIL = 3.0, HZ = 60;
const LEAD_BEATS = 8;                       // game beat 0 == file beat 8
const MUSICAL_FILE_BEATS = 216;             // game beat 208: 8 past the finale note (200)
fs.mkdirSync(outDir, { recursive: true });

const server = http.createServer((req, res) => {
  const p = path.join(webRoot, decodeURIComponent(req.url.split('?')[0]));
  if (!p.startsWith(webRoot) || !fs.existsSync(p) || fs.statSync(p).isDirectory()) { res.writeHead(404); return res.end(); }
  res.writeHead(200, { 'content-type': p.endsWith('.js') ? 'text/javascript' : 'application/octet-stream' });
  fs.createReadStream(p).pipe(res);
});
await new Promise((r) => server.listen(0, '127.0.0.1', r));
const base = `http://127.0.0.1:${server.address().port}`;
const browser = await chromium.launch({ args: ['--autoplay-policy=no-user-gesture-required'] });
const page = await browser.newPage();
page.on('pageerror', (e) => console.error('pageerror', e.message));
await page.route('**/__blank', (r) => r.fulfill({ contentType: 'text/html', body: '<!doctype html><title>r</title>' }));
await page.goto(base + '/__blank');

const res = await page.evaluate(async ({ base, SR, TAIL, HZ, LEAD_BEATS, MUSICAL_FILE_BEATS }) => {
  const { createAudio, makeVirtualClock } = await import(base + '/src/audio/index.js');
  const { TRACKS } = await import(base + '/src/audio/music/index.js');
  const { compileTrack } = await import(base + '/src/audio/music/compile.js');
  const chart = await import(base + '/src/games/finaleFever/chart.js');
  const { bpmAt, BPM0, RAMP, END_BEAT } = chart;
  const track = TRACKS['finale-fever'];

  // exact seconds from file beat 0 (Simpson, 1/16-beat panels): the true integral of the curve
  const gbpm = (g) => (g <= 0 ? BPM0 : bpmAt(g));
  const spbG = (g) => 60 / gbpm(g);
  const secAt = (fb) => {
    const g = fb - LEAD_BEATS;
    if (g <= 0) return fb * 60 / BPM0;
    let s = LEAD_BEATS * 60 / BPM0; const n = Math.max(1, Math.ceil(g * 16));
    const h = g / n;
    for (let k = 0; k < n; k++) { const a = k * h; s += h / 6 * (spbG(a) + 4 * spbG(a + h / 2) + spbG(a + h)); }
    return s;
  };
  const musicalSec = secAt(MUSICAL_FILE_BEATS);
  const seconds = musicalSec + TAIL;

  async function render(layers) {
    const ctx = new OfflineAudioContext(2, Math.ceil(SR * seconds), SR);
    const clock = makeVirtualClock(BPM0);
    const audio = createAudio({ ctx, clock, bus: null, offline: true });
    audio.music.play({ ...track, layers }, { atBeat: 0, intensity: 3, fade: 0 });
    const steps = Math.ceil(musicalSec * HZ);
    for (let k = 0; k <= steps; k++) {
      clock.setNow(k / HZ);
      const g = clock.beat - LEAD_BEATS;           // game beat, as in update()
      if (g > 0) clock.setBpm(bpmAt(g));
      audio.music.pump();
    }
    const b = await ctx.startRendering();
    return { L: b.getChannelData(0), R: b.getChannelData(1) };
  }
  const b64 = (f32) => {
    const i16 = new Int16Array(f32.length);
    for (let i = 0; i < f32.length; i++) i16[i] = Math.max(-32768, Math.min(32767, Math.round(f32[i] * 32767)));
    const u8 = new Uint8Array(i16.buffer); let s = '';
    for (let i = 0; i < u8.length; i += 0x8000) s += String.fromCharCode.apply(null, u8.subarray(i, i + 0x8000));
    return btoa(s);
  };

  const out = { files: {}, constants: { BPM0, RAMP, END_BEAT }, seconds, musicalSec };
  const full = track.layers;
  for (let i = 0; i <= 3; i++) {
    const r = await render(full.filter((l) => (l.min ?? 0) <= i && i <= (l.max ?? 99)));
    let pk = 0, ss = 0;
    for (const d of [r.L, r.R]) for (let j = 0; j < d.length; j++) { pk = Math.max(pk, Math.abs(d[j])); ss += d[j] * d[j]; }
    out.files['i' + i] = { frames: r.L.length, peak: pk, rms: Math.sqrt(ss / (2 * r.L.length)), L: b64(r.L), R: b64(r.R) };
  }
  // kick-only probe -> onset verification against the exact integrated map
  const kl = full.filter((l) => l.voice === 'kick').map((k) => ({ ...k, min: 0 }));
  const pr = await render(kl);
  const compiled = compileTrack({ ...track, layers: kl });
  const exp = [];
  for (let li = 0; secAt(li * compiled.loopBeats) < musicalSec; li++)
    for (const e of compiled.events) { const fb = li * compiled.loopBeats + e.beat; if (e.vel > 0.5 && fb < MUSICAL_FILE_BEATS) exp.push(fb); }
  const mono = new Float32Array(pr.L.length);
  for (let i = 0; i < mono.length; i++) mono[i] = Math.abs(pr.L[i]) + Math.abs(pr.R[i]);
  const errs = exp.map((fb) => {
    const t = secAt(fb), a = Math.max(0, Math.round((t - 0.02) * SR)), b = Math.min(mono.length, Math.round((t + 0.03) * SR));
    let pk = 0; for (let i = a; i < b; i++) pk = Math.max(pk, mono[i]);
    for (let i = a; i < b; i++) if (mono[i] > pk * 0.03) return { fb, err: i / SR - t };
    return { fb, err: NaN };
  }).filter((e) => !Number.isNaN(e.err));
  out.kick = errs;
  out.beatSeconds = Array.from({ length: MUSICAL_FILE_BEATS + 1 }, (_, k) => secAt(k));
  out.bpmSamples = Array.from({ length: MUSICAL_FILE_BEATS + 1 }, (_, k) => gbpm(k - LEAD_BEATS));
  return out;
}, { base, SR, TAIL, HZ, LEAD_BEATS, MUSICAL_FILE_BEATS });
await browser.close(); server.close();

function wav(L, R) {
  const n = L.length, buf = Buffer.alloc(44 + n * 4);
  buf.write('RIFF', 0); buf.writeUInt32LE(36 + n * 4, 4); buf.write('WAVEfmt ', 8);
  buf.writeUInt32LE(16, 16); buf.writeUInt16LE(1, 20); buf.writeUInt16LE(2, 22);
  buf.writeUInt32LE(SR, 24); buf.writeUInt32LE(SR * 4, 28); buf.writeUInt16LE(4, 32); buf.writeUInt16LE(16, 34);
  buf.write('data', 36); buf.writeUInt32LE(n * 4, 40);
  for (let i = 0; i < n; i++) { buf.writeInt16LE(L[i], 44 + i * 4); buf.writeInt16LE(R[i], 46 + i * 4); }
  return buf;
}
const dec = (s) => { const b = Buffer.from(s, 'base64'); return new Int16Array(b.buffer, b.byteOffset, b.length / 2); };

const med = (a) => { const s = [...a].sort((x, y) => x - y); return s[Math.floor(s.length / 2)]; };
const errsMs = res.kick.map((e) => e.err * 1000);
const offsetMs = errsMs.reduce((a, b) => a + b, 0) / errsMs.length;
const resid = errsMs.map((e) => e - offsetMs);
const files = {};
for (const [name, f] of Object.entries(res.files)) {
  const stem = `finale-fever-ramped.${name}`;
  fs.writeFileSync(path.join(outDir, stem + '.wav'), wav(dec(f.L), dec(f.R)));
  const rec = { wav: stem + '.wav', frames: f.frames, seconds: f.frames / SR, peak: +f.peak.toFixed(4), rms: +f.rms.toFixed(4) };
  if (wantOgg) {
    const r = spawnSync(ffmpeg, ['-y', '-v', 'error', '-i', path.join(outDir, stem + '.wav'), '-c:a', 'libvorbis', '-q:a', oggQ, path.join(outDir, stem + '.ogg')]);
    if (r.status === 0) { rec.ogg = stem + '.ogg'; rec.oggBytes = fs.statSync(path.join(outDir, stem + '.ogg')).size; }
    else console.error('ffmpeg failed', String(r.stderr));
  }
  files[name] = rec;
}
const round = (x, d = 6) => +x.toFixed(d);
const meta = {
  generator: 'Tools/music/render-ramped.mjs',
  id: 'finale-fever-ramped', sampleRate: SR, channels: 2, ramped: true, loop: false,
  beatsPerBar: 4, intensityLevels: 4,
  fileBeatZeroIsGameBeat: -LEAD_BEATS,           // fileBeat = gameBeat + 8
  firstBeatOffsetSeconds: round(offsetMs / 1000, 5), // audible onset lags scheduled beat time (graph latency)
  musicalBeats: res.beatSeconds.length - 1,       // last entry of beatSeconds
  musicalLengthSeconds: round(res.musicalSec),
  totalLengthSeconds: round(res.seconds),
  bpmFunction: {
    formula: 'bpm(g) = bpm0 * (1 + (ramp - 1) * (0.25*s + 0.75*s*s)), s = clamp01(g / endBeat); g = game beat; bpm(g<=0) = bpm0',
    bpm0: res.constants.BPM0, ramp: res.constants.RAMP, endBeat: res.constants.END_BEAT, leadBeats: LEAD_BEATS,
  },
  beatSeconds: res.beatSeconds.map((x) => round(x)),   // beatSeconds[k] = seconds from file start of FILE beat k (game beat k-8), exact integral, excluding firstBeatOffsetSeconds
  bpmAtBeat: res.bpmSamples.map((x) => round(x, 4)),   // bpm at file beat k
  files,
  verify: {
    kickOnsets: errsMs.length,
    meanOffsetMs: round(offsetMs, 3),
    medianAbsErrMs_afterOffset: round(med(resid.map(Math.abs)), 3),
    maxAbsErrMs_afterOffset: round(Math.max(...resid.map(Math.abs)), 3),
    medianErrMs_raw: round(med(errsMs), 3),
    maxRawErrMs: round(Math.max(...errsMs.map(Math.abs)), 3),
    perKickErrMsFirst: res.kick.slice(0, 3).map((e) => [e.fb, round(e.err * 1000, 3)]),
    perKickErrMsLast: res.kick.slice(-3).map((e) => [e.fb, round(e.err * 1000, 3)]),
  },
};
fs.writeFileSync(path.join(outDir, 'finale-fever-ramped.json'), JSON.stringify(meta, null, 1));
console.log(JSON.stringify({ files, verify: meta.verify, offset: meta.firstBeatOffsetSeconds, len: meta.totalLengthSeconds }, null, 1));
