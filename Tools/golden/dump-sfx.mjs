// Dump what the web's SFX kit plays, so Core/SfxTheory.cs can be test-pinned against it.
//   node Tools/golden/dump-sfx.mjs [--web <path to web>]      (default: Reference/web)
// Runs the web's real audio graph in headless Chromium (needs Playwright, like Tools/music/render.mjs).
//   sfx-notes.txt  every (context, chord, sfx[, arg]) -> the voice calls the web made (voice@ms:midi[+midi])
//   sfx-keys.txt   the key() the web's music player reports at sampled beats of each track
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (k, d) => { const i = process.argv.indexOf('--' + k); return i < 0 ? d : process.argv[i + 1]; };
const webRoot = path.resolve(arg('web', path.join(here, '../../Reference/web')));
const OUT = path.join(here, '../../Assets/Tests/EditMode/Golden');
fs.mkdirSync(OUT, { recursive: true });

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
await page.route('**/__blank', (r) => r.fulfill({ contentType: 'text/html', body: '<!doctype html><title>r</title>' }));
await page.goto(base + '/__blank');

const res = await page.evaluate(async ({ base }) => {
  const { createAudio, makeVirtualClock } = await import(base + '/src/audio/index.js');
  const { TRACKS } = await import(base + '/src/audio/music/index.js');
  const { SCALES, chord } = await import(base + '/src/audio/theory.js');
  const CTX = ['swing-kings', 'chomp-chorus', 'drumline-dash', 'finale-fever', 'bounce-brigade', 'title', 'results'];
  const VOICES = ['hit', 'pluck', 'bell', 'fall', 'brass', 'stab', 'kick', 'crash', 'riser'];
  const AT = 1;

  // ---- notes: the voice calls of each SFX on each chord
  const notes = [];
  for (const id of CTX) {
    const t = TRACKS[id];
    const scale = SCALES[t.scale];
    const degs = [...new Set(t.prog)].sort((a, b) => a - b);
    for (const d of degs) {
      const octx = new OfflineAudioContext(2, 44100, 44100);
      const audio = createAudio({ ctx: octx, clock: makeVirtualClock(120), bus: null, offline: true });
      const key = { root: t.root, scale, scaleName: t.scale, chordDeg: d, chord: chord(t.root, scale, d, t.shape || [0, 2, 4]), bar: 0, intensity: 1 };
      audio.music.player.scaleInfo = () => key;
      let calls = [];
      for (const v of VOICES) {
        const orig = audio.voices[v];
        audio.voices[v] = (at, o = {}) => {
          const ms = Math.round((at - AT) * 1000);
          let s = `${v}@${ms}`;
          const m = o.midis ?? (o.midi !== undefined ? [o.midi] : []);
          if (m.length) s += ':' + m.join('+');
          calls.push(s);
          return orig(at, o);
        };
      }
      const run = (sfx, ...args) => { calls = []; audio.sfx(sfx, AT, ...args); return calls.join(','); };
      for (const sfx of ['perfect', 'great', 'good', 'miss', 'impact', 'ui', 'uiBack', 'tick']) notes.push(`${id} ${d} ${sfx} - ${run(sfx)}`);
      if (d === t.prog[0]) {
        // root/scale only: independent of the chord, listed once per context
        notes.push(`${id} ${d} fanfare - ${run('fanfare')}`);
        notes.push(`${id} ${d} coin - ${run('coin')}`);
        notes.push(`${id} ${d} swoosh - ${run('swoosh')}`);
        for (const n of [0, 1, 2, 3]) notes.push(`${id} ${d} count ${n} ${run('count', n)}`);
        for (let lv = 0; lv <= 10; lv++) notes.push(`${id} ${d} combo ${lv} ${run('combo', lv)}`);
      }
    }
  }

  // ---- keys: the player's scaleInfo() at sampled beats (games start at atBeat -8, the shell tracks at 0)
  const keys = [];
  for (const id of CTX) {
    const t = TRACKS[id];
    const game = !['title', 'results'].includes(id);
    const atBeat = game ? -8 : 0;
    const spb = 60 / t.bpm;
    const octx = new OfflineAudioContext(2, 44100, 44100);
    const clock = makeVirtualClock(t.bpm);
    const audio = createAudio({ ctx: octx, clock, bus: null, offline: true });
    audio.music.play(id, { atBeat, intensity: 1 });
    const last = atBeat + t.bars * t.beatsPerBar * 2 + 9;   // two trips round the loop
    for (let b = atBeat + 0.25; b < last; b += 1) {
      clock.setNow(b * spb);
      const k = audio.music.player.scaleInfo();
      keys.push(`${id} ${b} ${k.bar} ${k.chordDeg} ${k.chord.join(',')}`);
    }
  }
  return { notes, keys };
}, { base });
await browser.close();
server.close();

fs.writeFileSync(path.join(OUT, 'sfx-notes.txt'), ['# ctx chordDeg sfx arg events', ...res.notes].join('\n') + '\n');
fs.writeFileSync(path.join(OUT, 'sfx-keys.txt'), ['# ctx beat bar chordDeg chord', ...res.keys].join('\n') + '\n');
console.log('sfx-notes.txt', res.notes.length, 'sfx-keys.txt', res.keys.length);
