// Renders the hymns' audio from their MIDI notes and tempo maps, with the web prototype's own synth
// voices through the real audio graph (createAudio) in a headless Chromium OfflineAudioContext, like
// Tools/music/render.mjs. Verse 1 only, one mono OGG per hymn (and per re-timed level: <hymn>.<level>).
//
//   node Tools/hymns/render-hymns.mjs [--hymns <dir>] [--out <dir>] [--work <dir>] [--web <Bonanza-web/web>]
//                                      [--only hymn_007,hymn_058] [--force] [--q 3]
//
//   --hymns  the private hymns folder: hymn_NNN.mid, tempo/hymn_NNN.json, excerpts.txt   (default notes/hymns)
//   --out    where the runtime payload goes: <clip>.ogg and audio.json                    (default Assets/Resources/Hymns, gitignored)
//   --work   beat maps, render state, scratch                                              (default HymnBuild, gitignored)
//
// What it does, in order:
//   1. Builds each hymn's beat map with the game's own Core code (Tools/hymns/beatmap, needs `dotnet`):
//      <work>/beatmaps/<hymn>.beatmap.json = MIDI + tempo map -> the conducted grid the game judges against.
//   2. Reads the same MIDI notes, places every note with its own tick -> seconds from the beat map's
//      tempo list, and checks that arithmetic against every beat time and the note count of the beat map.
//      So the audio and the grid are the same map by construction and by check.
//   3. Renders the notes: melody (the upper track) on the web's drawbar organ, bass on a softer, darker
//      organ. A clip whose beat map fingerprint, MIDI and render settings are unchanged is skipped.
//   4. Writes the OGG files and <out>/audio.json (sample rate, the graph's start offset, the clips).
//      Prints what changed, per hymn: the old and new verse length, beats and first tempo.
//
// The first beat (tick 0) sounds `beatOffset` seconds into every file (the graph's own latency, measured
// like the other renderers); the game starts a clip that much early.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { parseMidi, makeSeconds, readExcerptRows } from './midi.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '../..');
const arg = (k, d) => { const i = process.argv.indexOf('--' + k); return i < 0 ? d : process.argv[i + 1]; };
const hymnsDir = path.resolve(arg('hymns', path.join(root, 'notes/hymns')));
const outDir = path.resolve(arg('out', path.join(root, 'Assets/Resources/Hymns')));
const workDir = path.resolve(arg('work', path.join(root, 'HymnBuild')));
const webRoot = path.resolve(arg('web', path.join(root, '../Bonanza-web/web')));
const only = arg('only', null)?.split(',');
const force = process.argv.includes('--force');
const oggQ = String(arg('q', '3'));
const RENDER_VERSION = 2;   // bump when the voices or the mix below change: it re-renders everything
const SR = 44100;
const TAIL = 2.5;           // seconds of reverb/release kept after verse 1
const AT = 0.1;             // the latency probe's time
const ffmpeg = fs.existsSync(path.join(os.homedir(), '.local/bin/ffmpeg')) ? path.join(os.homedir(), '.local/bin/ffmpeg') : 'ffmpeg';
const dotnetBin = fs.existsSync(path.join(os.homedir(), '.dotnet/dotnet')) ? path.join(os.homedir(), '.dotnet/dotnet') : 'dotnet';

fs.mkdirSync(outDir, { recursive: true });
fs.mkdirSync(path.join(workDir, 'beatmaps'), { recursive: true });
const fail = (m) => { console.error('render-hymns: ' + m); process.exit(1); };

// ---- 1. beat maps from Core ---------------------------------------------------
const rows = readExcerptRows(path.join(hymnsDir, 'excerpts.txt')).filter((r) => r.use && (!only || only.includes(r.hymn)));
if (!rows.length) fail('no hymn to render (excerpts.txt has no `use yes` row' + (only ? ' among ' + only.join(',') : '') + ')');
{
  const r = spawnSync(dotnetBin, ['run', '-c', 'Release', '--project', path.join(here, 'beatmap'), '--', hymnsDir, path.join(workDir, 'beatmaps'), rows.map((x) => x.hymn).join(',')],
    { encoding: 'utf8', env: { ...process.env, DOTNET_ROOT: process.env.DOTNET_ROOT || path.join(os.homedir(), '.dotnet'), DOTNET_NOLOGO: '1', DOTNET_CLI_TELEMETRY_OPTOUT: '1' } });
  if (r.status !== 0) fail('the beat map tool failed:\n' + r.stdout + r.stderr);
}

// ---- 2. jobs --------------------------------------------------------------------
const statePath = path.join(workDir, 'render-state.json');
const state = fs.existsSync(statePath) ? JSON.parse(fs.readFileSync(statePath, 'utf8')) : { clips: {} };
const jobs = [];
for (const row of rows) {
  const midBytes = fs.readFileSync(path.join(hymnsDir, row.hymn + '.mid'));
  const midi = parseMidi(midBytes);
  const bm = JSON.parse(fs.readFileSync(path.join(workDir, 'beatmaps', row.hymn + '.beatmap.json'), 'utf8'));
  const noteTracks = [...new Set(midi.notes.map((n) => n.track))].sort((a, b) => a - b);
  const bassTrack = noteTracks.find((t) => /bass/i.test(midi.names[t] || '')) ?? (noteTracks.length > 1 ? noteTracks[noteTracks.length - 1] : -1);
  for (const [variant, v] of Object.entries(bm.variants)) {
    const tempos = v.tempo.map(([tick, us]) => ({ tick, us }));
    const sec = makeSeconds(bm.ticksPerQuarter, tempos);
    // the check: this file's own tick -> seconds agrees with the grid Core built
    let worst = 0;
    for (const [tick, t] of v.beats) worst = Math.max(worst, Math.abs(sec(tick) - t));
    worst = Math.max(worst, Math.abs(sec(v.verseEndTick) - v.verseEndSeconds));
    if (worst > 1e-6) fail(`${row.hymn} ${variant}: audio timing and beat map disagree by ${worst} s`);
    const notes = midi.notes.filter((n) => n.start < v.verseEndTick);
    if (notes.length !== v.notes) fail(`${row.hymn} ${variant}: ${notes.length} notes read here, ${v.notes} in the beat map`);
    const hash = crypto.createHash('sha1').update(midBytes).update(v.fingerprint).update(String(RENDER_VERSION)).update(oggQ).digest('hex').slice(0, 16);
    jobs.push({
      clip: v.clip, hymn: row.hymn, variant, hash, beats: v.beats.length, verse: v.verseEndSeconds,
      firstBpm: 60000000 / tempos[0].us, tempoCount: tempos.filter((t) => t.tick < v.verseEndTick).length,
      notes: notes.map((n) => ({ t: sec(n.start), d: Math.max(0.08, (sec(n.end) - sec(n.start)) * 0.97), p: n.pitch, v: n.velocity, bass: n.track === bassTrack })),
      length: v.verseEndSeconds + TAIL,
    });
  }
}

const stale = jobs.filter((j) => force || state.clips[j.clip]?.hash !== j.hash || !fs.existsSync(path.join(outDir, j.clip + '.ogg')));

// ---- 3. render in the web audio graph ------------------------------------------------
const MIME = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.html': 'text/html', '.json': 'application/json' };
let delaySamples = state.delaySamples ?? null;
const rendered = new Map();
if (stale.length || delaySamples == null) {
  if (!fs.existsSync(path.join(webRoot, 'src/audio/index.js'))) fail('the web audio sources are not at ' + webRoot + ' (pass --web)');
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

  const b64 = (f32) => {
    // one mono channel, 16-bit
    return f32;
  };
  const probe = await page.evaluate(async ({ base, SR, AT }) => {
    const { createAudio, makeVirtualClock } = await import(base + '/src/audio/index.js');
    const octx = new OfflineAudioContext(2, Math.ceil(SR * 0.7), SR);
    const clock = makeVirtualClock(120);
    const audio = createAudio({ ctx: octx, clock, bus: null, offline: true });
    audio.sfxBus.gain.value = 1;
    clock.setNow(0);
    audio.sfx('tick', AT, 0.12);
    const buf = await octx.startRendering();
    const L = buf.getChannelData(0);
    let pk = 0; for (let i = 0; i < L.length; i++) pk = Math.max(pk, Math.abs(L[i]));
    let onset = 0; while (onset < L.length && Math.abs(L[onset]) < pk * 0.01) onset++;
    return onset;
  }, { base, SR, AT });
  delaySamples = Math.max(0, probe - Math.round(AT * SR));

  for (const job of stale) {
    const data = await page.evaluate(async ({ base, SR, job, delay }) => {
      const { createAudio, makeVirtualClock } = await import(base + '/src/audio/index.js');
      const octx = new OfflineAudioContext(2, Math.ceil(SR * (job.length + delay / SR)), SR);
      const clock = makeVirtualClock(120);
      const audio = createAudio({ ctx: octx, clock, bus: null, offline: true });
      audio.voices.musicBus.gain.value = 0.75 / 0.85;
      clock.setNow(0);
      for (const n of job.notes) {
        const vel = 0.55 + 0.45 * (n.v / 127);
        if (n.bass) audio.voices.organ(n.t, { midis: [n.p], dur: n.d, gain: 0.17 * vel, rev: 0.18, drawbars: [1, 0.3, 0.08, 0, 0] });
        else audio.voices.organ(n.t, { midis: [n.p], dur: n.d, gain: 0.26 * vel, rev: 0.34, drawbars: [1, 0.6, 0.38, 0.22, 0.1] });
      }
      const buf = await octx.startRendering();
      const L = buf.getChannelData(0), R = buf.getChannelData(1);
      const i16 = new Int16Array(L.length);
      let peak = 0;
      for (let i = 0; i < L.length; i++) peak = Math.max(peak, Math.abs(L[i]), Math.abs(R[i]));
      const g = peak > 0 ? Math.min(4, 0.8 / peak) : 1;   // each clip to the same peak
      for (let i = 0; i < L.length; i++) i16[i] = Math.max(-32768, Math.min(32767, Math.round((L[i] + R[i]) * 0.5 * g * 32767)));
      const u8 = new Uint8Array(i16.buffer);
      let s = ''; for (let i = 0; i < u8.length; i += 0x8000) s += String.fromCharCode.apply(null, u8.subarray(i, i + 0x8000));
      return { pcm: btoa(s), peak, gain: g };
    }, { base, SR, job: { ...job, notes: job.notes.map((n) => ({ ...n, p: n.p })) }, delay: delaySamples });
    rendered.set(job.clip, data);
  }
  await browser.close();
  server.close();
}

// ---- 4. write ------------------------------------------------------------------------
function wav(i16) {
  const n = i16.length, buf = Buffer.alloc(44 + n * 2);
  buf.write('RIFF', 0); buf.writeUInt32LE(36 + n * 2, 4); buf.write('WAVEfmt ', 8);
  buf.writeUInt32LE(16, 16); buf.writeUInt16LE(1, 20); buf.writeUInt16LE(1, 22);
  buf.writeUInt32LE(SR, 24); buf.writeUInt32LE(SR * 2, 28); buf.writeUInt16LE(2, 32); buf.writeUInt16LE(16, 34);
  buf.write('data', 36); buf.writeUInt32LE(n * 2, 40);
  for (let i = 0; i < n; i++) buf.writeInt16LE(i16[i], 44 + i * 2);
  return buf;
}
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'hymns-'));
const changes = [];
for (const job of jobs) {
  const prev = state.clips[job.clip];
  const summary = { hash: job.hash, hymn: job.hymn, variant: job.variant, verseSeconds: +job.verse.toFixed(3), beats: job.beats, firstBpm: +job.firstBpm.toFixed(2), tempoCount: job.tempoCount };
  const data = rendered.get(job.clip);
  if (data) {
    const b = Buffer.from(data.pcm, 'base64');
    const all = new Int16Array(b.buffer, b.byteOffset, b.length / 2);
    const out = path.join(outDir, job.clip + '.ogg');
    // the graph's own delay stays in: tick 0 sounds beatOffset seconds into the file. Fade the very end.
    const n = all.length, fade = Math.round(0.4 * SR);
    const x = new Int16Array(n);
    for (let i = 0; i < n; i++) x[i] = i >= n - fade ? Math.round(all[i] * (n - i) / fade) : all[i];
    const w = path.join(tmp, job.clip + '.wav');
    fs.writeFileSync(w, wav(x));
    const p = spawnSync(ffmpeg, ['-y', '-v', 'error', '-i', w, '-ac', '1', '-c:a', 'libvorbis', '-q:a', oggQ, out]);
    if (p.status !== 0) fail('ffmpeg failed for ' + job.clip + ': ' + String(p.stderr));
    summary.seconds = +(n / SR).toFixed(3);
    summary.bytes = fs.statSync(out).size;
    summary.peak = +data.peak.toFixed(3);
    const why = !prev ? 'new' : prev.hash !== job.hash ? 'changed' : 'forced';
    changes.push(`${why.padEnd(8)}${job.clip}: ${prev ? `verse ${prev.verseSeconds}s -> ${summary.verseSeconds}s, beats ${prev.beats} -> ${summary.beats}, first tempo ${prev.firstBpm} -> ${summary.firstBpm} bpm` : `verse ${summary.verseSeconds}s, ${summary.beats} beats, first tempo ${summary.firstBpm} bpm`}, ${(summary.bytes / 1024).toFixed(0)} KB`);
  } else {
    summary.seconds = prev.seconds; summary.bytes = prev.bytes; summary.peak = prev.peak;
  }
  state.clips[job.clip] = summary;
}
fs.rmSync(tmp, { recursive: true, force: true });

// clips of hymns no longer in use, or of a level variant that is gone
const live = new Set(jobs.map((j) => j.clip));
if (!only) {
  for (const f of fs.readdirSync(outDir)) {
    const m = /^(.*)\.ogg$/.exec(f);
    if (m && !live.has(m[1])) { fs.rmSync(path.join(outDir, f)); try { fs.rmSync(path.join(outDir, f + '.meta')); } catch {} changes.push('removed ' + m[1]); delete state.clips[m[1]]; }
  }
}
state.delaySamples = delaySamples;
fs.writeFileSync(statePath, JSON.stringify(state, null, 1) + '\n');
const manifest = {
  generator: 'Tools/hymns/render-hymns.mjs', sampleRate: SR, beatOffset: +(delaySamples / SR).toFixed(5),
  clips: Object.entries(state.clips).filter(([c]) => live.has(c) || only).map(([name, s]) => ({ name, seconds: s.seconds, bytes: s.bytes })).sort((a, b) => (a.name < b.name ? -1 : 1)),
};
fs.writeFileSync(path.join(outDir, 'audio.json'), JSON.stringify(manifest, null, 1) + '\n');

const unchanged = jobs.length - stale.length;
for (const c of changes) console.log(c);
if (unchanged) console.log(`unchanged ${unchanged} clip${unchanged === 1 ? '' : 's'} (same MIDI, tempo map and render settings)`);
const total = manifest.clips.reduce((s, c) => s + (c.bytes || 0), 0);
console.log(`beat offset ${(delaySamples / SR * 1000).toFixed(2)} ms; ${manifest.clips.length} clips, ${(total / 1048576).toFixed(2)} MB, in ${outDir}`);
console.log(`beat maps in ${path.join(workDir, 'beatmaps')}`);
