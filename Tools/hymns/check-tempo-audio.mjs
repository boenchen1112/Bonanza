// Proves that the audio of the Finale Fever tempo clips keeps the beat times of Core's TempoCurve:
//   node Tools/hymns/check-tempo-audio.mjs [--hymns <dir>] [--only hymn_058,hymn_090] [--tol-ms 1]
// In a temp dir it renders the given hymns (render-hymns.mjs --probe) and, for every hymn x level:
//   1. the renderer's own segment evaluation agrees with Core's beat lines and note times (render-hymns fails
//      otherwise, to 1 microsecond: nothing is rendered from a curve it cannot reproduce),
//   2. a click is rendered on every beat through the same placement path and the graph's start offset, and its
//      measured onset in the audio is within --tol-ms (default 1 ms) of the beat line of the curve dump,
//   3. audio.json carries the clip's beat count and curve length, equal to the dump's,
//   4. the real organ clip starts on beat 0: nothing sounds before it, and its first note is up within 20 ms (the organ's own attack; a lone note
//      there reaches 1 % of its peak ~10 ms after the beat, a soft chord ~12).
// Exit code 1 on any failure. Needs the private hymns folder, dotnet, ffmpeg and the web audio sources.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '../..');
const arg = (k, d) => { const i = process.argv.indexOf('--' + k); return i < 0 ? d : process.argv[i + 1]; };
const hymns = path.resolve(arg('hymns', path.join(root, 'notes/hymns')));
const only = arg('only', 'hymn_058,hymn_090,hymn_065');
const tolMs = +arg('tol-ms', '1');
if (!fs.existsSync(path.join(hymns, 'excerpts.txt'))) { console.log('SKIP no private hymns at ' + hymns); process.exit(0); }
const ffmpeg = fs.existsSync(path.join(os.homedir(), '.local/bin/ffmpeg')) ? path.join(os.homedir(), '.local/bin/ffmpeg') : 'ffmpeg';

let failed = false;
const check = (ok, msg) => { console.log((ok ? 'PASS ' : 'FAIL ') + msg); if (!ok) failed = true; };

const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'tempo-audio-'));
const out = path.join(tmp, 'out'), work = path.join(tmp, 'work'), probe = path.join(tmp, 'probe');
const r = spawnSync('node', [path.join(here, 'render-hymns.mjs'), '--hymns', hymns, '--out', out, '--work', work, '--only', only, '--probe', probe], { encoding: 'utf8' });
if (r.status !== 0) { console.error(r.stdout + r.stderr); process.exit(1); }

// first sample whose magnitude reaches 1 % of the peak of x[from, to)
function onset(x, from, to) {
  let pk = 0;
  for (let i = from; i < to; i++) pk = Math.max(pk, Math.abs(x[i]));
  if (pk < 200) return -1;
  for (let i = from; i < to; i++) if (Math.abs(x[i]) >= pk * 0.01) return i;
  return -1;
}
function decode(file) {
  const p = spawnSync(ffmpeg, ['-v', 'error', '-i', file, '-f', 's16le', '-ac', '1', '-ar', '44100', '-'], { maxBuffer: 1 << 28 });
  if (p.status !== 0) { console.error(String(p.stderr)); process.exit(1); }
  return new Int16Array(p.stdout.buffer, p.stdout.byteOffset, p.stdout.length / 2);
}

const organ = JSON.parse(fs.readFileSync(path.join(probe, 'organ.json'), 'utf8'));
const audioJson = JSON.parse(fs.readFileSync(path.join(out, 'audio.json'), 'utf8'));
const files = fs.readdirSync(probe).filter((f) => f.endsWith('.clicks.json')).sort();
check(files.length > 0, `${files.length} tempo clips rendered for ${only}`);
let allWorst = 0, beatsChecked = 0;
for (const f of files) {
  const info = JSON.parse(fs.readFileSync(path.join(probe, f), 'utf8'));
  const SR = info.sampleRate, delay = info.delaySamples / SR + info.lead;
  const b = fs.readFileSync(path.join(probe, info.clip + '.clicks.pcm'));
  const x = new Int16Array(b.buffer, b.byteOffset, b.length / 2);
  let worst = 0, missing = 0;
  info.curveBeatTimes.forEach((t, i) => {
    const c = Math.round((t + delay) * SR);
    const half = Math.round(Math.min(0.12, 0.45 * (i > 0 ? t - info.curveBeatTimes[i - 1] : 0.3)) * SR);
    const o = onset(x, Math.max(0, c - half), Math.min(x.length, c + half));
    if (o < 0) { missing++; return; }
    worst = Math.max(worst, Math.abs(o / SR - (t + delay)) * 1000);
  });
  beatsChecked += info.curveBeatTimes.length;
  allWorst = Math.max(allWorst, worst);
  check(missing === 0 && worst <= tolMs, `${info.clip}: ${info.curveBeatTimes.length} beat clicks, worst onset error ${worst.toFixed(3)} ms (limit ${tolMs} ms)${missing ? `, ${missing} not found` : ''}`);

  // the placed times the renderer used are the dump's beat lines
  const place = Math.max(...info.placedBeatTimes.map((t, i) => Math.abs(t - info.lead - info.curveBeatTimes[i])));
  check(place < 1e-6, `${info.clip}: the renderer's segment evaluation equals the curve dump's beat lines (worst ${(place * 1e6).toFixed(3)} us)`);

  const row = audioJson.clips.find((c) => c.name === info.clip);
  check(row && row.lead === info.lead && row.beats === info.curveBeatTimes.length - 1 && Math.abs(row.curveSeconds - info.curveBeatTimes[info.curveBeatTimes.length - 1]) < 1e-5, `${info.clip}: audio.json lists ${row?.beats} beats, ${row?.curveSeconds} s`);

  // the real clip: beat 0 is where the first note sounds
  const pcm = decode(path.join(out, info.clip + '.ogg'));
  const first = onset(pcm, 0, Math.round(0.6 * SR));
  const lone = organ.onsetAt100ms / SR;   // beat 0 is at 0.1 s (the lead): where a lone organ note placed there starts to sound
  const earliest = delay - 0.0005;       // nothing may sound before beat 0 (the click-measured start of the graph)
  check(first >= 0 && first / SR >= earliest && first / SR <= delay + 0.020, `${info.clip}: the first organ onset ${(first / SR * 1000).toFixed(2)} ms is after beat 0 (${(delay * 1000).toFixed(2)} ms) and within the 20 ms organ attack (a lone note starts at ${(lone * 1000).toFixed(2)} ms)`);
}
console.log(`${files.length} clips, ${beatsChecked} beats, worst ${allWorst.toFixed(3)} ms`);
fs.rmSync(tmp, { recursive: true, force: true });
process.exit(failed ? 1 : 0);
