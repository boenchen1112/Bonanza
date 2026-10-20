// Proves that editing a tempo map moves the grid and the audio together, end to end, on a real hymn:
//   node Tools/hymns/check-tempo-edit.mjs [--hymns <dir>] [--hymn hymn_007] [--segment-hymn hymn_058]
// Works in a temp copy (the real maps are never touched): renders the hymn as it is, then with the global scale
// edited and with one segment multiplier, and checks that (1) the beat map (Core's grid) moves exactly as the
// edit says, (2) the rendered clip's length moves with it, (3) an unchanged render is skipped.
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
const hymnA = arg('hymn', 'hymn_007'), hymnB = arg('segment-hymn', 'hymn_058');   // A: the global scale; B: a hymn with tempo changes inside verse 1
if (!fs.existsSync(path.join(hymns, hymnA + '.mid')) || !fs.existsSync(path.join(hymns, hymnB + '.mid'))) { console.log('SKIP no private hymns at ' + hymns); process.exit(0); }
const ffmpeg = fs.existsSync(path.join(os.homedir(), '.local/bin/ffmpeg')) ? path.join(os.homedir(), '.local/bin/ffmpeg') : 'ffmpeg';

let failed = false;
const check = (ok, msg) => { console.log((ok ? 'PASS ' : 'FAIL ') + msg); if (!ok) failed = true; };

function session(hymn) {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'tempo-edit-'));
  const dir = path.join(tmp, 'hymns'), out = path.join(tmp, 'out'), work = path.join(tmp, 'work');
  fs.mkdirSync(path.join(dir, 'tempo'), { recursive: true });
  for (const f of ['excerpts.txt', hymn + '.mid']) fs.copyFileSync(path.join(hymns, f), path.join(dir, f));
  const mapFile = path.join(dir, 'tempo', hymn + '.json');
  const gen = spawnSync('node', [path.join(here, 'make-tempo-maps.mjs'), '--hymns', dir, '--only', hymn], { encoding: 'utf8' });
  if (gen.status !== 0) { console.error(gen.stdout + gen.stderr); process.exit(1); }
  const render = () => {
    const r = spawnSync('node', [path.join(here, 'render-hymns.mjs'), '--hymns', dir, '--out', out, '--work', work, '--only', hymn], { encoding: 'utf8' });
    if (r.status !== 0) { console.error(r.stdout + r.stderr); process.exit(1); }
    const bm = JSON.parse(fs.readFileSync(path.join(work, 'beatmaps', hymn + '.beatmap.json'), 'utf8')).variants.base;
    const dur = /Duration: (\d+):(\d+):([\d.]+)/.exec(spawnSync(ffmpeg, ['-i', path.join(out, hymn + '.ogg')], { encoding: 'utf8' }).stderr);
    return { bm, seconds: +dur[1] * 3600 + +dur[2] * 60 + +dur[3], log: r.stdout };
  };
  return { tmp, mapFile, original: fs.readFileSync(mapFile, 'utf8'), render };
}

// 1. global scale 1.25: every beat 1.25x sooner, the clip 1.25x shorter (its 2.5 s tail aside)
{
  const s = session(hymnA);
  const a = s.render();
  check(/new\s+/.test(a.log), `${hymnA}: the first render writes the clip`);
  check(/unchanged 1 clip/.test(s.render().log), `${hymnA}: a second render with nothing edited is skipped`);
  fs.writeFileSync(s.mapFile, s.original.replace('"scale": 1.0,', '"scale": 1.25,'));
  const b = s.render();
  check(/changed\s+/.test(b.log), `${hymnA}: editing the scale re-renders ("changed" printed)`);
  let worst = 0;
  a.bm.beats.forEach((x, i) => { worst = Math.max(worst, Math.abs(x[1] / 1.25 - b.bm.beats[i][1])); });
  check(a.bm.beats.length === b.bm.beats.length && worst < 1e-4, `${hymnA}: every beat moved to time / 1.25 (microsecond rounding of the tempo allowed) worst error ${worst.toExponential(1)} s`);
  check(a.bm.fingerprint !== b.bm.fingerprint, `${hymnA}: the beat map fingerprint changed`);
  const want = (a.seconds - 2.5) / 1.25 + 2.5;
  check(Math.abs(b.seconds - want) < 0.1, `${hymnA}: the clip is ${b.seconds.toFixed(2)} s, expected ${want.toFixed(2)} s (verse ${(a.seconds - 2.5).toFixed(2)} s / 1.25 + the tail)`);
  fs.rmSync(s.tmp, { recursive: true, force: true });
}

// 2. one segment: the first tempo entry inside verse 1 after tick 0 at half speed moves only the beats after it
{
  const s = session(hymnB);
  const a = s.render();
  const lines = s.original.split('\n');
  const idx = lines.findIndex((l) => /"tick": 0, "bpm"/.test(l));
  const second = lines.findIndex((l, i) => i > idx && /"tick": \d+, "bpm"/.test(l) && /verse 1/.test(l));
  if (second < 0) { console.log('FAIL ' + hymnB + ' has no tempo change inside verse 1'); process.exit(1); }
  const tickOfSecond = +/"tick": (\d+)/.exec(lines[second])[1];
  lines[second] = lines[second].replace('"x": 1.0', '"x": 0.5');
  fs.writeFileSync(s.mapFile, lines.join('\n'));
  const c = s.render();
  const before = a.bm.beats.filter((x) => x[0] <= tickOfSecond), after = a.bm.beats.filter((x) => x[0] > tickOfSecond);
  const same = before.every((x, i) => Math.abs(x[1] - c.bm.beats[i][1]) < 1e-6);
  const slower = after.length > 0 && after.every((x, i) => c.bm.beats[before.length + i][1] > x[1] + 1e-6);
  check(same, `${hymnB}: the ${before.length} beats up to tick ${tickOfSecond} are untouched by a multiplier on the entry after them`);
  check(slower, `${hymnB}: the ${after.length} beats after it moved later`);
  check(c.seconds > a.seconds + 0.5, `${hymnB}: and the clip is longer (${a.seconds.toFixed(2)} -> ${c.seconds.toFixed(2)} s)`);
  fs.rmSync(s.tmp, { recursive: true, force: true });
}
process.exit(failed ? 1 : 0);
