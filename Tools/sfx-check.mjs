// Usage: node Tools/sfx-check.mjs <buildDir> [gameId]
//   node Tools/sfx-check.mjs Builds/WebGL swing-kings
// Proves the sound effects are really played, not just scheduled. Two runs of the built player in Chromium
// (audio muted at the speakers, but tapped inside the page like Tools/audio-probe.mjs):
//
//  1. Autoplay round of <gameId> with the music volume set to 0 (through the Options screen, saved in the page's
//     storage): the SFX are then the only thing in the output. Reads the `[SFX] clip=... t=...` lines (the host
//     logs one per sound under ?autoplay/?debug) and the short AudioBufferSourceNodes Unity starts for them, and
//     asserts the output carries an RMS burst after each clip start, that the round's hit moments sound
//     (swing-kings: a verdict sound after every impact), and prints the autoplay biasMs.
//     Since ticket 30 the same run covers the games' own instrument voices (Assets/Game/Audio/Voices, the `[VOICE]` lines
//     the host logs under ?voicelog): every game's voices are counted per kind and tied to its round (a snare per drum
//     hit, an answer per Finale hit, a singer per Chomp hit), each is looked up in
//     Assets/Game/Audio/Voices/voices.json, and the RMS burst check runs for all games (the voices are baked, not
//     placeholder clips of the SFX's length).
//  2. Pause menu (?debug, no autoplay): Escape, then Down. The menu's sounds play while the listener is paused;
//     the tap must see them.
//
// Exit code 1 on any FAIL. Same server and Playwright setup as webgl-check.mjs.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright';

const DIR = process.argv[2];
const GAME = process.argv[3] || 'swing-kings';
const BIAS_BASELINE = Number(process.env.BIAS_BASELINE || 9);   // ms; the gates have run at 8.4-8.9
const PORT = 7964;
const TYPES = { '.html': 'text/html', '.js': 'application/javascript', '.css': 'text/css',
  '.wasm': 'application/wasm', '.data': 'application/octet-stream', '.png': 'image/png', '.ico': 'image/x-icon' };

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

const browser = await chromium.launch({ headless: true, args: ['--use-angle=d3d11', '--mute-audio'] });
const context = await browser.newContext({ viewport: { width: 960, height: 600 } });   // one context: the saved options carry from run 1 to run 2
const wait = (s) => new Promise((r) => setTimeout(r, s * 1000));
let failed = false;
const check = (ok, msg) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${msg}`); if (!ok) failed = true; };
const info = (msg) => console.log('INFO ' + msg);

async function open(query, collect) {
  const page = await context.newPage();
  const errors = [];
  const lines = [];
  page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
  page.on('console', (m) => {
    const t = m.text();
    if (m.type() === 'error' || t.includes('[ERROR]')) errors.push('console: ' + t);
    if (collect && (t.includes('[SFX]') || t.includes('[VOICE]') || t.includes('[RESULT]'))) lines.push({ at: Date.now(), t: t.trim() });
  });
  await page.addInitScript(() => {
    window.__env = [];       // [ctx time of the buffer, rms] from a pass-through tap in front of the destination
    window.__starts = [];    // every short AudioBufferSourceNode.start(): the SFX
    const connect = AudioNode.prototype.connect;
    AudioNode.prototype.connect = function (dest, ...rest) {
      if (dest instanceof AudioDestinationNode) {
        const ctx = this.context;
        if (!ctx.__tap) {
          const an = ctx.createAnalyser();
          const sp = ctx.createScriptProcessor(256, 2, 2);
          sp.onaudioprocess = (e) => {
            const x = e.inputBuffer.getChannelData(0);
            let sum = 0; for (let i = 0; i < x.length; i++) sum += x[i] * x[i];
            for (let c = 0; c < e.outputBuffer.numberOfChannels; c++) e.outputBuffer.getChannelData(c).set(e.inputBuffer.getChannelData(Math.min(c, e.inputBuffer.numberOfChannels - 1)));
            window.__env.push([e.playbackTime, Math.sqrt(sum / x.length)]);
          };
          connect.call(an, sp);
          connect.call(sp, dest);
          ctx.__tap = an;
        }
        return connect.call(this, ctx.__tap, ...rest);
      }
      return connect.call(this, dest, ...rest);
    };
    const start = AudioBufferSourceNode.prototype.start;
    AudioBufferSourceNode.prototype.start = function (when = 0, ...rest) {
      if (this.buffer && this.buffer.length < 600000) window.__starts.push({ when: when || this.context.currentTime, now: this.context.currentTime, len: this.buffer.length, sr: this.buffer.sampleRate });
      return start.call(this, when, ...rest);
    };
  });
  await page.goto(`http://127.0.0.1:${PORT}/${query}`, { waitUntil: 'load' });
  await page.waitForFunction(() => getComputedStyle(document.querySelector('#unity-loading-container')).display === 'none', null, { timeout: 60000 });
  await wait(1.5);
  await page.mouse.click(480, 300);   // the gesture that unlocks audio
  return { page, errors, lines };
}

const voiceManifest = JSON.parse(fs.readFileSync(new URL('../Assets/Game/Audio/Voices/voices.json', import.meta.url), 'utf8'));
const sfxManifest = JSON.parse(fs.readFileSync(new URL('../Assets/Game/Audio/Sfx/sfx.json', import.meta.url), 'utf8'));
const clipInfo = new Map([...voiceManifest.clips, ...sfxManifest.clips].map((c) => [c.name, c]));
const kindOf = (n) => n.split('_').slice(0, 2).join('_');
const clipOf = (t) => /clip=(\S+)/.exec(t)?.[1];

// ---- run 1: music off (Options, music row, 15 x Left: 75% -> 0), then an autoplay round
{
  const o = await open('?scene=options', false);
  await wait(2);
  for (let i = 0; i < 16; i++) { await o.page.keyboard.press('ArrowLeft'); await wait(0.15); }
  await wait(2);   // PlayerPrefs.Save -> IndexedDB
  await o.page.close();
}
{
  const r = await open(`?autoplay&game=${GAME}&voicelog`, true);
  const t0 = Date.now();
  while (!r.lines.some((l) => l.t.includes('[RESULT]')) && Date.now() - t0 < 240000) await wait(0.5);
  await wait(1.5);
  const res = r.lines.find((l) => l.t.includes('[RESULT]'))?.t;
  check(!!res, `round finished (${res ? res.slice(0, 90) : 'no [RESULT]'})`);
  const sfx = r.lines.filter((l) => l.t.includes('[SFX]')).map((l) => ({ clip: clipOf(l.t), t: Number(/ t=(\S+)/.exec(l.t)[1]), at: l.at }));
  const by = {};
  for (const s of sfx) by[s.clip] = (by[s.clip] || 0) + 1;
  const kinds = {};
  for (const [c, n] of Object.entries(by)) { const k = c.replace(/^[a-z-]+_/, '').replace(/_\d+$/, ''); kinds[k] = (kinds[k] || 0) + n; }
  info(`${sfx.length} sounds: ` + Object.entries(kinds).map(([k, n]) => `${k}=${n}`).join(' '));
  const env = await r.page.evaluate(() => window.__env);
  const starts = await r.page.evaluate(() => window.__starts);
  info(`${starts.length} short sources started, ${env.length} output buffers`);
  check(sfx.length > 0, 'the host logged SFX plays');
  check(starts.length >= sfx.length * 0.9, `every SFX reaches Web Audio (${starts.length} sources for ${sfx.length} sounds)`);

  // ---- the games' own voices: one log line each, counted per kind against the round's result
  const vox = r.lines.filter((l) => l.t.includes('[VOICE]')).map((l) => ({ clip: clipOf(l.t), t: Number(/ t=(\S+)/.exec(l.t)[1]), vol: Number(/ vol=(\S+)/.exec(l.t)[1]) }));
  const vk = {};
  for (const v of vox) vk[kindOf(v.clip)] = (vk[kindOf(v.clip)] || 0) + 1;
  info(`${vox.length} voices: ` + Object.entries(vk).map(([k, n]) => `${k}=${n}`).join(' '));
  const num = (k) => Number(new RegExp(`${k}=(\\d+)`).exec(res || '')?.[1] || 0);
  const hits = num('perfect') + num('great') + num('good'), misses = num('miss');
  const n = (k) => vk[k] || 0;
  check(vox.every((v) => clipInfo.has(v.clip)), 'every voice played is a baked clip of voices.json');
  if (GAME === 'swing-kings') check(vox.length === 0, `Swing Kings plays no game voices (${vox.length})`);
  if (GAME === 'drumline-dash') {
    check(n('dd_snare') === hits, `a snare under every hit (${n('dd_snare')} for ${hits} hits)`);
    check(n('dd_call') >= 20 && n('dd_callhat') >= 3, `the call: toms + rim (${n('dd_call')}) and the opening hats (${n('dd_callhat')})`);
    check(n('dd_handoff') >= 3 && n('dd_crash') >= 3, `handoff hats ${n('dd_handoff')}, crash per call ${n('dd_crash')}`);
  }
  if (GAME === 'bounce-brigade') check(vox.length === 0, `Hymn Meter plays no game voices (its music is the rendered hymns) (${vox.length})`);
  if (GAME === 'chomp-chorus') {
    check(n('cc_sing') + n('cc_fsing') >= hits, `a singer per hit and per CPU note (${n('cc_sing') + n('cc_fsing')} for ${hits} player hits)`);
    check(n('cc_bed') >= 4 * 20, `the four-part bed under the bars (${n('cc_bed')})`);
    check(n('cc_gulp') + n('cc_gulpbig') >= 20 && n('cc_gulpbig') >= 1, `breaths ${n('cc_gulp')} and the big one ${n('cc_gulpbig')}`);
    check(n('cc_bell') >= 1 && n('cc_fsing') >= 1, `perfect-note bells ${n('cc_bell')}, the finale note ${n('cc_fsing')}`);
  }
  if (GAME === 'finale-fever') {
    check(n('ff_ans') >= hits - 2 && n('ff_ans') <= hits, `a bell answer per hit (${n('ff_ans')} for ${hits} hits)`);
    check(n('ff_swing') >= 1 && n('ff_chord') >= 1 && n('ff_call') >= 10, `boss: swing ${n('ff_swing')}, chord ${n('ff_chord')}, notes ${n('ff_call')}`);
    check(n('ff_rim') >= 1 && n('ff_pluck') >= 1, `solo ticks ${n('ff_rim')}, swing plucks ${n('ff_pluck')}`);
  }

  // Output energy: baseline = the quiet floor between sounds (music is off), burst = 90 ms after a clip start.
  const rms = env.map((e) => e[1]).sort((a, b) => a - b);
  const floor = rms[Math.floor(rms.length * 0.1)] || 0;
  const median = rms[Math.floor(rms.length * 0.5)] || 0;
  info(`output rms: floor(p10)=${floor.toFixed(5)} median=${median.toFixed(5)} peak=${rms[rms.length - 1].toFixed(4)}`);
  // Every logged sound (SFX and voices) is matched to the short source Unity started for it by clip length, in order;
  // a sound counts as heard when the output's loudest buffer in the 120 ms after its start is above 0.004 or a rise
  // over the 50 ms before it (the music is off, so what is there is the clips; dense phrases overlap, so no stricter test is fair). Clips quieter than 0.01 at the output are not judged.
  const logged = [...sfx.map((x) => ({ clip: x.clip, vol: 1 })), ...vox];
  const byLen = new Map();
  for (const s of starts) { const k = Math.round(s.len / s.sr * 1000); (byLen.get(k) || byLen.set(k, []).get(k)).push(s); }
  const kindLoud = {};
  let judged = 0, loud = 0, unmatched = 0;
  for (const v of logged) {
    const c = clipInfo.get(v.clip); if (!c || c.seconds < 0.15) continue;
    const cand = byLen.get(Math.round(c.seconds * 1000));
    if (!cand || !cand.length) { unmatched++; continue; }
    const st = cand.shift();
    const k = kindOf(v.clip), a = (kindLoud[k] ||= { n: 0, loud: 0, weak: 0 });
    if (v.vol !== 1 && c.peak * v.vol < 0.01) { a.weak++; continue; }
    let pre = 0, pn = 0, post = 0;
    for (const [t, e] of env) {
      if (t >= st.when - 0.05 && t < st.when - 0.002) { pre += e; pn++; }
      else if (t >= st.when && t < st.when + 0.12) post = Math.max(post, e);
    }
    const ok = post > 0.004 || post > (pn ? pre / pn : 0) * 1.4 && post > 0.002;
    if (process.env.DBG && !ok) console.log('quiet', v.clip, 'when', st.when.toFixed(3), 'pre', (pn ? pre / pn : 0).toFixed(4), 'post', post.toFixed(4), 'peak', c.peak, 'vol', v.vol);
    a.n++; judged++; if (ok) { a.loud++; loud++; }
  }
  info('bursts per kind: ' + Object.entries(kindLoud).map(([k, a]) => `${k} ${a.loud}/${a.n}${a.weak ? `(+${a.weak} weak)` : ''}`).join(' '));
  check(judged > 0 && loud >= judged * 0.85, `an RMS burst follows ${loud}/${judged} chord and voice clips (music off; ${unmatched} not matched to a started source)`);
  check(unmatched <= logged.length * 0.1, `every logged clip reaches Web Audio as its own source (${unmatched} unmatched of ${logged.length})`);

  if (GAME === 'swing-kings') {
    const n = (re) => sfx.filter((s) => re.test(s.clip)).length;
    const hits = n(/_impact_/), verdicts = n(/_(perfect|great|good)_/);
    info(`impacts=${hits} verdicts=${verdicts} coins=${n(/_coin$/)} fanfares=${n(/_fanfare$/)} counts=${n(/_count_/)} ticks=${n(/^tick$/)}`);
    check(hits >= 20, `impact on every hit (${hits})`);
    check(verdicts >= hits - 1 && verdicts <= hits, `a verdict sound with every impact but the demo's (${verdicts} for ${hits})`);
    check(n(/_coin$/) > 0 && n(/_fanfare$/) > 0, 'coin on a big hit and the fanfare at the finale');
    check(n(/_count_0$/) > 0 && n(/_count_1$/) > 0, 'the count-in plays both its notes');
    check(n(/^tick$/) > 0, 'windup / launch ticks');
    // Each impact is followed by its verdict ~10 ms later, on the same chord: same game time order.
    let paired = 0;
    for (let i = 0; i < sfx.length - 1; i++) {
      if (/_impact_(\d+)$/.test(sfx[i].clip)) {
        const d = /_impact_(\d+)$/.exec(sfx[i].clip)[1];
        const nx = sfx[i + 1];
        if (process.env.DBG && !new RegExp(`_(perfect|great|good)_${d}$`).test(nx.clip)) console.log('mismatch', sfx[i].clip, sfx[i].t, nx.clip, nx.t);
        if (new RegExp(`_(perfect|great|good)_${d}$`).test(nx.clip) && nx.t - sfx[i].t < 0.05) paired++;
      }
    }
    check(paired >= hits - 1, `each verdict is on its impact's chord (${paired}/${hits})`);
  }
  const m = /biasMs=(-?[\d.]+)/.exec(res || '');
  if (m) check(Math.abs(Number(m[1])) <= BIAS_BASELINE + 1.5, `autoplay biasMs ${m[1]} does not worsen (baseline ${BIAS_BASELINE}, +1.5 allowed)`);
  check(/miss=0/.test(res || ''), 'miss=0');
  check(r.errors.length === 0, 'errors: ' + (r.errors.length ? r.errors.join(' | ') : 'none'));
  await r.page.close();
}

// ---- run 2: the pause menu (the listener is paused; the menu's sounds must still play)
{
  const r = await open(`?debug&game=${GAME}`, true);
  await wait(2.5);
  await r.page.keyboard.press('Space');   // past the rules card
  await wait(5);
  const before = (await r.page.evaluate(() => window.__env)).length;
  await r.page.keyboard.press('Escape');
  await wait(0.8);
  await r.page.keyboard.press('ArrowDown');
  await wait(0.8);
  const env = await r.page.evaluate(() => window.__env);
  const starts = await r.page.evaluate(() => window.__starts);
  const sfx = r.lines.filter((l) => l.t.includes('[SFX]')).map((l) => clipOf(l.t));
  info('sounds around the pause: ' + sfx.filter((c) => /^title_ui/.test(c)).join(' '));
  check(sfx.includes('title_uiBack_0'), 'opening the pause menu plays uiBack');
  check(sfx.includes('title_ui_0'), 'moving in the pause menu plays ui');
  // Music is off, and paused anyway: whatever the tap hears in the last 1.6 s is the menu.
  const tail = env.slice(before).filter((e) => e[1] > 0.003);
  check(tail.length > 5, `the pause menu's sounds reach the output (${tail.length} loud buffers)`);
  check(r.errors.length === 0, 'errors: ' + (r.errors.length ? r.errors.join(' | ') : 'none'));
  await r.page.close();
}

await browser.close();
server.close();
if (failed) process.exitCode = 1;
