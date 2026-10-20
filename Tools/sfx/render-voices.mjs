// Bakes the games' own instrument voices (the web's `audio.voices.*` calls inside games/<game>/index.js, which are
// not entries of the SFX table) to one OGG per clip, through the real audio graph (createAudio) in a headless
// Chromium OfflineAudioContext, like Tools/sfx/render-sfx.mjs and Tools/music/render.mjs.
//
//   node Tools/sfx/render-voices.mjs [--web <path to Bonanza-web/web>] [--out <dir>] [--q 2] [--no-ogg] [--list]
//
// Output: Assets/Game/Audio/Voices/<clip>.ogg and voices.json (the manifest VoiceTests pin to Core/VoiceCatalog.cs).
//
// What is baked, and why it is per pitch: the web voices are synthesised per call at the note asked for
// (a filter that opens relative to f, a vibrato that depends on dur, a reverb and a delay that ring on), so a
// pitch-shifted clip would change timbre and length together. Every pitch a game can ask for is known from its
// chart and its track's harmony, so each is baked exactly. Clip recipes live in RECIPES below, one per web call
// site (file:line of the game's index.js in the comment); a recipe is a list of voice calls [voice, offset s, opts].
//
// Levels (see Voices.cs, one table): the graph is the web's own except the two buses the SFX renderer also
// fixes. sfxBus = 1 (as render-sfx.mjs; the SFX slider is applied at playback). Voices that go through the web's
// "loose" bus (looseBus 0.9 -> musicBus 0.75) keep their ratio to the sfxBus (0.85): musicBus = 0.75 / 0.85.
// Voices are baked at the web's exact per-call gain; a variant that differs only in gain (a verdict's lead gain,
// the finale's x1.25) is the same clip scaled at playback.
//
// Names (Core/VoiceCatalog.cs builds the same):
//   title_kick (the title screen's beat kick)
//   dd_call_<k> dd_callhat dd_handoff dd_crash dd_fumble dd_snare dd_stumble_<i>
//   cc_sing_<lane>_<deg> cc_bell_<lane>_<deg> cc_fsing_<lane>_<deg> cc_sour_<lane>_<deg> cc_bed_<lane>_<deg> cc_gulp_<lane> cc_gulpbig_<lane>
//   ff_swing_<deg> ff_chord_<deg>_<tone> ff_call_<deg>_<tone>_<a|n> ff_rim ff_ans_<deg>_<i>_<12|24> ff_pluck_<deg>_<i>
// Swing Kings has no game voices on the web (only sfx() calls), so it has no clips here.
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
const outDir = path.resolve(arg('out', path.join(here, '../../Assets/Game/Audio/Voices')));
const oggQ = String(arg('q', '2'));
const wantOgg = !process.argv.includes('--no-ogg');
const listOnly = process.argv.includes('--list');
const ffmpeg = fs.existsSync(path.join(os.homedir(), '.local/bin/ffmpeg')) ? path.join(os.homedir(), '.local/bin/ffmpeg') : 'ffmpeg';
const SR = 44100;
const AT = 0.1;
fs.mkdirSync(outDir, { recursive: true });

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

const rendered = await page.evaluate(async ({ base, SR, AT, listOnly }) => {
  const { createAudio, makeVirtualClock } = await import(base + '/src/audio/index.js');
  const { TRACKS } = await import(base + '/src/audio/music/index.js');
  const { SCALES, chord, mtof } = await import(base + '/src/audio/theory.js');
  const chomp = await import(base + '/src/games/chompChorus/chart.js');
  const finale = await import(base + '/src/games/finaleFever/chart.js');

  const b64 = (f32) => {
    const i16 = new Int16Array(f32.length);
    for (let i = 0; i < f32.length; i++) i16[i] = Math.max(-32768, Math.min(32767, Math.round(f32[i] * 32767)));
    const u8 = new Uint8Array(i16.buffer);
    let s = ''; for (let i = 0; i < u8.length; i += 0x8000) s += String.fromCharCode.apply(null, u8.subarray(i, i + 0x8000));
    return btoa(s);
  };
  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  const ms = (s) => Math.floor(s * 1000 + 0.5);   // JS Math.round for positives; Core uses the same floor(x + 0.5)
  const degsOf = (id) => [...new Set(TRACKS[id].prog)].sort((a, b) => a - b);
  const chordOf = (id, deg) => { const t = TRACKS[id]; return chord(t.root, SCALES[t.scale], deg, t.shape || [0, 2, 4]); };
  const SFX = 'sfx';   // dest sentinel: audio.sfxBus (hit, tone, fall default there; the games pass it for drums)

  const clips = [];   // { name, game, calls: [[voice, offset, opts]] }
  const add = (name, game, calls) => clips.push({ name, game, calls });

  // ========================================================================= Drumline Dash (games/drumlineDash/index.js)
  {
    const TOM_HZ = [252, 214, 180, 152, 128];                                   // :757
    for (let k = 0; k < 5; k++) add(`dd_call_${k}`, 'drumline-dash', [          // playCall :762, gains x1.25 on the finale
      ['tom', 0, { freq: TOM_HZ[k], gain: 0.85, decay: 0.26, pan: 0.24 + k * 0.05, rev: 0.30, dest: SFX }],
      ['rim', 0, { gain: 0.36, pan: 0.34, rev: 0.22, dest: SFX }]]);
    add('dd_callhat', 'drumline-dash', [['hat', 0, { gain: 0.18, open: 0.06, pan: 0.4, dest: SFX }]]);   // first call note :774
    add('dd_handoff', 'drumline-dash', [['hat', 0, { gain: 0.16, open: 0.22, pan: 0.1, dest: SFX }]]);   // last beat of the call bar :318
    add('dd_crash', 'drumline-dash', [['crash', 0, { gain: 0.16, decay: 0.9, rev: 0.45, pan: 0.35, dest: SFX }]]);   // onCallStart :556, finale gain 0.30
    add('dd_fumble', 'drumline-dash', [                                           // fumble :583
      ['rim', 0, { gain: 0.34, pan: -0.3, rev: 0.06, dest: SFX }],
      ['hit', 0, { gain: 0.16, d: 0.07, hp: 260, lp: 1100, dest: SFX }]]);
    add('dd_snare', 'drumline-dash', [['snare', 0, { gain: 0.42, decay: 0.12, snap: 1.25, tone: 1.12, rev: 0.10, pan: -0.18, dest: SFX }]]);   // onJudged :624, perfect gain 0.58
    for (let i = 1; i <= 3; i++) add(`dd_stumble_${i}`, 'drumline-dash', [        // CPU racer stumbles :672, i = racer index
      ['tom', 0, { freq: 96, gain: 0.24, decay: 0.3, pan: (i - 2) * 0.3, rev: 0.3, dest: SFX }]]);
  }

  // ========================================================================= Title (shell/title.js onBeat)
  // kick(ctx, t, gain) = audio.voices.kick(t, { gain }) on the loose bus, no dest. Baked at the reel's 0.5; the menu's 0.34 is a playback scale.
  add('title_kick', 'title', [['kick', 0, { gain: 0.5 }]]);

  // ========================================================================= Chomp Chorus (games/chompChorus/index.js)
  {
    const id = 'chomp-chorus';
    const spb = 60 / TRACKS[id].bpm;
    const midiFor = (lane, deg) => { const c = chordOf(id, deg); return (c[lane] ?? c[c.length - 1]) + 24; };   // _midiFor :318
    const finaleDegs = new Set();   // chords a finale note can be sung on (the press is within the claim window of the note)
    for (const n of chomp.buildChart().filter((n) => n.finale)) {
      const t = TRACKS[id];
      for (const d of [-0.5, 0, 0.5, 1]) {
        const bar = Math.floor((n.beat + d + 8) / 4);
        finaleDegs.add(t.prog[((bar % t.prog.length) + t.prog.length) % t.prog.length]);
      }
    }
    for (const deg of degsOf(id)) for (let lane = 0; lane < 4; lane++) {
      const pan = chomp.LANES[lane].pan;
      const midi = midiFor(lane, deg);
      add(`cc_sing_${lane}_${deg}`, id, [['lead', 0, { midi, dur: spb * 0.85, gain: 0.25, detune: 13, cutoff: 3.6, res: 2.4, pan, rev: 0.32, dly: 0.12, vib: 0.9 }]]);   // _sing :331, great; perfect x1.2, good x0.72
      add(`cc_bell_${lane}_${deg}`, id, [['bell', 0.012, { midi: midi + 12, dur: 0.55, gain: 0.09, ratio: 2.01, index: 4, rev: 0.4, dly: 0.2, pan }]]);   // perfect and finale: bell 12 ms later
      add(`cc_sour_${lane}_${deg}`, id, [   // _sour :353
        ['tone', 0, { freq: mtof(midi - 12), type: 'sawtooth', bend: 0.45, d: 0.30, gain: 0.13, pan, rev: 0.12 }],
        ['hit', 0, { gain: 0.05, d: 0.09, hp: 240, lp: 1400, pan }]]);
      add(`cc_bed_${lane}_${deg}`, id, [['lead', 0, { midi, dur: spb * 3.9, gain: 0.062, detune: 17, cutoff: 1.5, res: 1.1, pan, rev: 0.45, dly: 0, vib: 0.5 }]]);   // _bed :372
      if (finaleDegs.has(deg)) add(`cc_fsing_${lane}_${deg}`, id, [['lead', 0, { midi, dur: spb * 4, gain: 0.34, detune: 13, cutoff: 3.6, res: 2.4, pan, rev: 0.32, dly: 0.12, vib: 0.9 }]]);
    }
    for (let lane = 0; lane < 4; lane++) {
      const pan = chomp.LANES[lane].pan;
      for (const big of [false, true]) add(`cc_gulp${big ? 'big' : ''}_${lane}`, id, [   // _gulp :360
        ['tone', 0, { freq: 150 + lane * 26, type: 'sine', bend: big ? 4.4 : 3.0, d: spb * (big ? 3.4 : 0.62), gain: big ? 0.075 : 0.05, pan, rev: 0.14 }],
        ['hit', 0, { gain: 0.045, d: 0.05, hp: 1500, pan, rev: 0.05 }]]);
    }
  }

  // ========================================================================= Finale Fever (games/finaleFever/index.js)
  {
    const id = 'finale-fever';
    const ch = finale.buildChart();
    const callKinds = new Set(), chordTones = new Set(), noteTones = new Set();
    for (const c of ch.cues) {
      if (c.type !== 'call') continue;
      const tone = c.tone % 3;
      if (c.kind === 'swing') callKinds.add('swing');
      else if (c.kind === 'chord') chordTones.add(tone);
      else noteTones.add(`${tone}_${c.accent ? 'a' : 'n'}`);
    }
    const kinds = new Set(ch.notes.map((n) => n.kind));
    for (const deg of degsOf(id)) {
      const cd = chordOf(id, deg);
      if (callKinds.has('swing')) add(`ff_swing_${deg}`, id, [   // bossVoice 'swing' :568
        ['orchHit', 0, { midis: cd.map((m) => m - 12), gain: 0.24, rev: 0.5 }],
        ['tom', 0, { freq: 86, gain: 0.9, decay: 0.42, pan: -0.34, rev: 0.3 }]]);
      for (const tone of chordTones) {
        const midi = cd[tone % cd.length] - 12;
        add(`ff_chord_${deg}_${tone}`, id, [   // 'chord' :570
          ['stab', 0, { midis: [midi, midi + 7], dur: 0.2, gain: 0.19, wah: 1.7, rev: 0.3, pan: -0.3 }],
          ['tom', 0, { freq: 128, gain: 0.5, decay: 0.24, pan: -0.3 }]]);
      }
      for (const tn of noteTones) {
        const [tone, a] = tn.split('_'); const acc = a === 'a';
        const midi = cd[Number(tone) % cd.length] - 12;
        const calls = [['brass', 0, { midi, dur: acc ? 0.3 : 0.16, gain: acc ? 0.19 : 0.12, bite: 1.4, rev: 0.28, pan: -0.32 }]];   // :573
        if (acc) calls.push(['tom', 0, { freq: 112, gain: 0.4, decay: 0.2, pan: -0.28 }]);
        add(`ff_call_${deg}_${tone}_${a}`, id, calls);
      }
      for (let i = 0; i < cd.length; i++) {
        const octs = [12]; if (kinds.has('solo')) octs.push(24);
        for (const o of octs) add(`ff_ans_${deg}_${i}_${o}`, id, [   // playerVoice :683
          ['bell', 0, { midi: cd[i] + o, dur: 0.24, gain: 0.12, ratio: 2.01, index: 4, rev: 0.3, dly: 0.16, pan: 0.28 }]]);
        if (kinds.has('swing')) add(`ff_pluck_${deg}_${i}`, id, [
          ['pluck', 0, { midi: cd[i] + 12 + 7, dur: 0.2, gain: 0.11, bright: 5, rev: 0.25, pan: 0.3 }]]);
      }
    }
    add('ff_rim', id, [['rim', 0, { gain: 0.24, pan: -0.08, rev: 0.14 }]]);   // soloTick :587, non-accent gain 0.15
  }

  if (listOnly) return clips.map((c) => ({ name: c.name, game: c.game }));

  // ============================================================================ render
  const out = [];
  for (const c of clips) {
    let end = 0;
    for (const [, off, o] of c.calls) end = Math.max(end, off + (o.dur ?? o.d ?? o.decay ?? 0.4));
    const len = end + 2.8;
    const octx = new OfflineAudioContext(2, Math.ceil(SR * (AT + len)), SR);
    const clock = makeVirtualClock(120);
    const audio = createAudio({ ctx: octx, clock, bus: null, offline: true });
    audio.sfxBus.gain.value = 1;
    audio.voices.musicBus.gain.value = 0.75 / 0.85;
    clock.setNow(0);
    for (const [voice, off, o] of c.calls) {
      const opts = { ...o };
      if (opts.dest === SFX) opts.dest = audio.sfxBus; else delete opts.dest;
      audio.voices[voice](AT + off, opts);
    }
    const buf = await octx.startRendering();
    out.push({ name: c.name, game: c.game, L: b64(buf.getChannelData(0)), R: b64(buf.getChannelData(1)) });
  }
  // the graph's own delay, measured on the SFX tick like render-sfx.mjs
  {
    const octx = new OfflineAudioContext(2, Math.ceil(SR * 0.7), SR);
    const clock = makeVirtualClock(120);
    const audio = createAudio({ ctx: octx, clock, bus: null, offline: true });
    audio.sfxBus.gain.value = 1;
    clock.setNow(0);
    audio.sfx('tick', AT, 0.12);
    const buf = await octx.startRendering();
    out.push({ name: '__tick', game: null, L: b64(buf.getChannelData(0)), R: b64(buf.getChannelData(1)) });
  }
  return out;
}, { base, SR, AT, listOnly });
await browser.close();
server.close();

if (listOnly) {
  const byGame = {};
  for (const r of rendered) (byGame[r.game] ||= []).push(r.name);
  for (const [g, n] of Object.entries(byGame)) console.log(g, n.length);
  console.log('total', rendered.length);
  process.exit(0);
}

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
const tick = rendered.find((r) => r.name === '__tick');
const tl = dec(tick.L);
let pk = 0; for (const v of tl) pk = Math.max(pk, Math.abs(v));
let onset = 0; while (onset < tl.length && Math.abs(tl[onset]) < pk * 0.01) onset++;
const delay = Math.max(0, onset - Math.round(AT * SR));
console.log(`graph delay ${delay} samples = ${(delay / SR * 1000).toFixed(2)} ms (cut from every clip)`);

const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'voices-'));
const manifest = { generator: 'Tools/sfx/render-voices.mjs', sampleRate: SR, graphDelaySamples: delay, vorbisQuality: Number(oggQ), clips: [] };
let total = 0;
for (const r of rendered) {
  if (r.name === '__tick') continue;
  const L = dec(r.L), R = dec(r.R);
  const from = Math.round(AT * SR) + delay;
  let peak0 = 0;
  for (let i = from; i < L.length; i++) peak0 = Math.max(peak0, Math.abs(L[i]), Math.abs(R[i]));
  // The tail (reverb and delay ring) is cut where it falls 30 dB under the clip's peak, but never above -44 dBFS
  // and never under -62 dBFS: it sits under the music, and the decoded length is memory in the WebGL player.
  const floor = Math.max(0.0008, Math.min(0.006, (peak0 / 32767) * 0.03)) * 32767;
  let end = L.length;
  while (end > from && Math.abs(L[end - 1]) < floor && Math.abs(R[end - 1]) < floor) end--;
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
  const rec = { name: r.name, game: r.game, seconds: +(n / SR).toFixed(3), peak: +(peak / 32767).toFixed(4) };
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
// stale clips from an earlier recipe set
const keep = new Set(manifest.clips.map((c) => c.name + '.ogg'));
if (wantOgg) for (const f of fs.readdirSync(outDir)) if (f.endsWith('.ogg') && !keep.has(f)) { fs.rmSync(path.join(outDir, f)); try { fs.rmSync(path.join(outDir, f + '.meta')); } catch {} }
fs.writeFileSync(path.join(outDir, 'voices.json'), JSON.stringify(manifest, null, 1) + '\n');
fs.rmSync(tmp, { recursive: true, force: true });
const byGame = {};
for (const c of manifest.clips) { const g = (byGame[c.game] ||= { n: 0, s: 0, b: 0 }); g.n++; g.s += c.seconds; g.b += c.bytes || 0; }
for (const [g, v] of Object.entries(byGame)) console.log(`${g}: ${v.n} clips, ${v.s.toFixed(1)} s, ${(v.b / 1048576).toFixed(2)} MB`);
console.log(`wrote ${manifest.clips.length} clips, ${(total / 1048576).toFixed(2)} MB (${manifest.clips.reduce((s, c) => s + c.seconds, 0).toFixed(1)} s) to ${outDir}`);
