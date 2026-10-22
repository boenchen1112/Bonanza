// Renders the web prototype's synthesised music to WAV/OGG with the real
// audio graph (createAudio + makeVirtualClock) in a headless Chromium
// OfflineAudioContext.
//
//   node Tools/music/render.mjs [--web <path to Bonanza-web/web>] [--out <dir>]
//        [--tracks swing-kings,title] [--no-stems] [--no-ogg] [--q 4]
//
// Per track it writes (loop tracks are exactly one loop long, with the previous
// loop's reverb/delay tails wrapped onto the start, so the file loops cleanly):
//   <id>.i0..i3.{wav,ogg}   what the game sounds like at music intensity 0..3
//                           (cumulative mixes; intensity changes only on bars)
//   <id>.t0..t3.{wav,ogg}   additive stems: only the layers with min == n
//                           (the kick is kept at gain 0 in t1..t3 so the
//                           kick-sidechain duck still shapes them)
//   tracks.json             metadata + measured onset verification
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (k, d) => { const i = process.argv.indexOf('--' + k); return i < 0 ? d : (process.argv[i + 1]?.startsWith('--') || process.argv[i + 1] === undefined ? true : process.argv[i + 1]); };
const webRoot = path.resolve(arg('web', path.join(here, '../../../Bonanza-web/web')));
const outDir = path.resolve(arg('out', path.join(here, 'out')));
const wantTracks = arg('tracks', null)?.split(',');
const stems = !process.argv.includes('--no-stems');
const wantOgg = !process.argv.includes('--no-ogg');
const oggQ = String(arg('q', '4'));
const ffmpeg = fs.existsSync(path.join(os.homedir(), '.local/bin/ffmpeg')) ? path.join(os.homedir(), '.local/bin/ffmpeg') : 'ffmpeg';
const SR = 44100;
const TAIL = 3.0; // seconds of reverb tail kept for one-shot tracks
fs.mkdirSync(outDir, { recursive: true });

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

const results = await page.evaluate(async ({ base, SR, TAIL, stems, wantTracks }) => {
  const { createAudio, makeVirtualClock } = await import(base + '/src/audio/index.js');
  const { TRACKS } = await import(base + '/src/audio/music/index.js');
  const { compileTrack } = await import(base + '/src/audio/music/compile.js');
  const { renderTrackOffline } = await import(base + '/src/audio/index.js');

  async function render(track, seconds) {
    const ctx = new OfflineAudioContext(2, Math.ceil(SR * seconds), SR);
    const clock = makeVirtualClock(track.bpm);
    const audio = createAudio({ ctx, clock, bus: null, offline: true });
    // Pump only up to the render end (a one-shot's self-stop fade would otherwise cut its tail).
    audio.music.play(track, { atBeat: 0, intensity: 3, fade: 0 });
    const pumpTo = track.loop ? seconds : Math.min(seconds, (track.bars * track.beatsPerBar * 60) / track.bpm);
    for (let t = 0; t <= pumpTo; t += 0.05) { clock.setNow(t); audio.music.pump(); }
    const buf = await ctx.startRendering();
    const L = buf.getChannelData(0), R = buf.getChannelData(1);
    return { L, R };
  }
  const b64 = (f32) => {
    const i16 = new Int16Array(f32.length);
    for (let i = 0; i < f32.length; i++) i16[i] = Math.max(-32768, Math.min(32767, Math.round(f32[i] * 32767)));
    const u8 = new Uint8Array(i16.buffer);
    let s = ''; for (let i = 0; i < u8.length; i += 0x8000) s += String.fromCharCode.apply(null, u8.subarray(i, i + 0x8000));
    return btoa(s);
  };
  const stat = (L, R) => { let p = 0, s = 0; for (const d of [L, R]) for (let i = 0; i < d.length; i++) { const a = Math.abs(d[i]); if (a > p) p = a; s += d[i] * d[i]; } return { peak: p, rms: Math.sqrt(s / (L.length * 2)) }; };

  // slice the wrapped last loop (loop tracks) or the whole render (one-shots)
  function cut(track, r, loopSec) {
    if (!track.loop) return { L: r.L, R: r.R };
    const a = Math.round(loopSec * SR), n = Math.round(loopSec * SR);
    return { L: r.L.slice(a, a + n), R: r.R.slice(a, a + n) };
  }

  const out = [];
  for (const id of Object.keys(TRACKS)) {
    if (id === 'bounce-brigade') continue;   // replaced by Hymn Meter: it plays rendered hymns (Tools/hymns), no score of its own
    if (id === 'finale-fever') continue;   // replaced by the tempo game: it plays rendered hymns (Tools/hymns), no score of its own
    if (wantTracks && !wantTracks.includes(id)) continue;
    const track = TRACKS[id];
    const spb = 60 / track.bpm;
    const barSec = spb * track.beatsPerBar;
    const loopSec = barSec * track.bars;
    const seconds = track.loop ? loopSec * 2 : loopSec + TAIL;
    const entry = {
      id, title: track.title, bpm: track.bpm, beatsPerBar: track.beatsPerBar, bars: track.bars,
      loop: !!track.loop, swing: track.swing, mix: track.mix, secondsPerBeat: spb, secondsPerBar: barSec,
      loopSeconds: loopSec, key: { root: track.root, scale: track.scale, prog: track.prog },
      layers: track.layers.map((l) => ({ id: l.id, voice: l.voice, min: l.min ?? 0, max: l.max ?? 99, gain: l.gain, bars: l.bars ?? null })),
      files: {}, verify: {},
    };

    // cross-check against the canonical stats function
    const ref = await renderTrackOffline({ trackId: id, seconds: 8, intensity: 3 });
    entry.refStats = { peak: ref.peak, rms: ref.rms, clipped: ref.clipped, silent: ref.silent };

    const variants = [];
    for (let i = 0; i <= 3; i++) {
      variants.push({ name: 'i' + i, track: { ...track, layers: track.layers.filter((l) => (l.min ?? 0) <= i && i <= (l.max ?? 99)) } });
    }
    if (stems) {
      const kick = track.layers.find((l) => l.voice === 'kick');
      for (let n = 0; n <= 3; n++) {
        let ls = track.layers.filter((l) => (l.min ?? 0) === n);
        if (!ls.length) continue;
        if (n > 0 && kick) ls = [{ ...kick, gain: 0, id: '_duck' }, ...ls];
        variants.push({ name: 't' + n, track: { ...track, layers: ls } });
      }
    }
    // kick-only probe (verification only; not exported)
    const kicks = track.layers.filter((l) => l.voice === 'kick');
    if (kicks.length) variants.push({ name: 'probe', track: { ...track, layers: kicks.map((k) => ({ ...k, min: 0 })) } });

    for (const v of variants) {
      const r = await render(v.track, seconds);
      const c = cut(track, r, loopSec);
      const st = stat(c.L, c.R);
      entry.files[v.name] = { frames: c.L.length, seconds: c.L.length / SR, ...st };
      entry.files[v.name].pcm = { L: b64(c.L), R: b64(c.R) };
      if (v.name === 'probe') {
        // detect onsets in the kick probe and compare to the compiled event times
        const mono = new Float32Array(c.L.length);
        for (let i = 0; i < mono.length; i++) mono[i] = Math.abs(c.L[i]) + Math.abs(c.R[i]);
        const events = compileTrack(v.track).events.filter((e) => e.layer.voice === 'kick' && e.vel > 0.5);
        const exp = events.map((e) => e.beat * spb);
        const win = Math.round(0.03 * SR);
        let maxErr = 0, firstErr = null;
        // loop tracks: the cut is loop 2, so events are at t = beat*spb exactly. One-shots: from 0.
        const errs = exp.map((t) => {
          const a = Math.round(t * SR) - Math.round(0.02 * SR), b = Math.round(t * SR) + win;
          let pk = 0; for (let i = Math.max(0, a); i < Math.min(mono.length, b); i++) pk = Math.max(pk, mono[i]);
          let on = -1; for (let i = Math.max(0, a); i < Math.min(mono.length, b); i++) if (mono[i] > pk * 0.03) { on = i; break; }
          return on < 0 ? NaN : on / SR - t;
        });
        const good = errs.filter((e) => !Number.isNaN(e));
        entry.verify = {
          kickEvents: exp.length, detected: good.length,
          firstKickExpectedS: exp[0], firstKickMeasuredErrS: errs[0],
          meanErrS: good.reduce((a, b) => a + b, 0) / Math.max(1, good.length),
          maxAbsErrS: Math.max(...good.map(Math.abs)),
        };
        delete entry.files.probe.pcm;
      }
    }
    out.push(entry);
  }
  return out;
}, { base, SR, TAIL, stems, wantTracks });

await browser.close();
server.close();

// ---- write files ----------------------------------------------------------
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

const meta = { generator: 'Tools/music/render.mjs', sampleRate: SR, channels: 2, tracks: [] };
for (const t of results) {
  const files = {};
  for (const [name, f] of Object.entries(t.files)) {
    if (!f.pcm) continue;
    const base = `${t.id}.${name}`;
    fs.writeFileSync(path.join(outDir, base + '.wav'), wav(dec(f.pcm.L), dec(f.pcm.R)));
    const rec = { wav: base + '.wav', seconds: f.seconds, frames: f.frames, peak: +f.peak.toFixed(4), rms: +f.rms.toFixed(4) };
    if (wantOgg) {
      const r = spawnSync(ffmpeg, ['-y', '-v', 'error', '-i', path.join(outDir, base + '.wav'), '-c:a', 'libvorbis', '-q:a', oggQ, path.join(outDir, base + '.ogg')]);
      if (r.status === 0) { rec.ogg = base + '.ogg'; rec.oggBytes = fs.statSync(path.join(outDir, base + '.ogg')).size; }
      else console.error('ffmpeg failed', base, String(r.stderr));
    }
    files[name] = rec;
  }
  const v = t.verify;
  const maxLayerMin = Math.max(...t.layers.map((l) => l.min));
  meta.tracks.push({
    id: t.id, title: t.title, bpm: t.bpm, secondsPerBeat: t.secondsPerBeat, secondsPerBar: t.secondsPerBar,
    beatsPerBar: t.beatsPerBar, bars: t.bars, swing: t.swing,
    firstBeatOffsetSeconds: v.meanErrS != null ? +v.meanErrS.toFixed(5) : 0, // measured kick onset vs scheduled time (graph latency, e.g. compressor look-ahead)
    loop: t.loop, loopStartSeconds: 0, loopLengthSeconds: t.loop ? t.loopSeconds : null,
    lengthSeconds: files.i3?.seconds ?? null, // one-shot: musical length + reverb tail
    musicalLengthSeconds: t.loopSeconds,
    adaptive: { intensityLevels: maxLayerMin + 1, quantise: 'bar', mixes: 'iN = cumulative mix at intensity N', stems: 'tN = additive layers with min==N' },
    layerNames: t.layers.map((l) => l.id),
    layersByMin: Object.fromEntries([0, 1, 2, 3].map((n) => [n, t.layers.filter((l) => l.min === n).map((l) => l.id)])),
    files, verify: v, refStats: t.refStats,
  });
}
fs.writeFileSync(path.join(outDir, 'tracks.json'), JSON.stringify(meta, null, 2));
console.log('wrote', meta.tracks.length, 'tracks to', outDir);
