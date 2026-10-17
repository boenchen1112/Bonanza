// Shared by pause-check.mjs and audio-probe.mjs: records every start()/stop() the page makes on a long
// AudioBufferSourceNode (the music; the SFX clips are short), with the AudioContext time of the call.
// That is the ground truth for what the music did around a pause: Unity's JS layer freezes the clock
// (dspTime) and pauses every channel, and our restart (MusicPlayer.OnResume) is the last start.
//   await installMusicHook(page)            before page.goto
//   analyzeResume(await page.evaluate(() => window.__musicEv))
// -> { dctx, e, ... } where `dctx` is the AudioContext time the pause lasted (first stop -> the unpause's
//    first start) and `e` the worst continuity error among the sources playing at the end, in seconds: where
//    each sounds, minus where the music was when the pause froze it, minus the dsp time that ran since the
//    unpause. `spread` is the largest difference between sources (two copies of the music playing show up as a spread of tens of ms).
export async function installMusicHook(page) {
  await page.addInitScript(() => {
    window.__musicEv = [];
    let nextId = 1;
    const P = AudioBufferSourceNode.prototype;
    const start = P.start, stop = P.stop;
    P.start = function (when = 0, offset = 0, dur) {
      if (this.buffer && this.buffer.length > 1e6) window.__musicEv.push({ k: 'start', id: (this.__id ||= nextId++), ctx: this.context.currentTime, when, offset, len: this.buffer.length });
      return start.call(this, when, offset, dur);
    };
    P.stop = function (when = 0) {
      if (this.buffer && this.buffer.length > 1e6) window.__musicEv.push({ k: 'stop', id: (this.__id ||= nextId++), ctx: this.context.currentTime, when });
      return stop.call(this, when);
    };
  });
}

export function analyzeResume(ev) {
  const firstStart = ev.findIndex((x) => x.k === 'start');
  if (firstStart < 0) return { error: 'no music start seen' };
  // The sources playing when the pause landed: the first stop of a node that had started for real.
  const startOf = (id, before) => { let r = null; for (let i = 0; i < before; i++) if (ev[i].k === 'start' && ev[i].id === id) r = ev[i]; return r; };
  let iPause = -1;
  for (let i = firstStart; i < ev.length; i++) {
    const st = ev[i];
    if (st.k !== 'stop') continue;
    const s0 = startOf(st.id, i);
    if (s0 && st.ctx > s0.when + 1) { iPause = i; break; }   // stopped well after its start: the pause (a stop at the start is Unity's own churn)
  }
  if (iPause < 0) return { error: 'no music stop (no pause) seen' };
  const tp = ev[iPause].ctx;
  const s0 = startOf(ev[iPause].id, iPause);
  const pos0 = s0.offset + (tp - s0.when);          // where the music was when the pause froze it
  const iResume = ev.findIndex((x, i) => i > iPause && x.k === 'start');
  if (iResume < 0) return { error: 'no music start after the pause' };
  const tr = ev[iResume].ctx;                       // the unpause (dsp runs again from here)
  // Sources alive at the end: started after the pause and never stopped since.
  const alive = [];
  for (let i = iResume; i < ev.length; i++) {
    if (ev[i].k !== 'start') continue;
    if (!ev.slice(i + 1).some((y) => y.k === 'stop' && y.id === ev[i].id)) alive.push(ev[i]);
  }
  if (!alive.length) return { error: 'no music source left playing after the resume' };
  const tref = Math.max(...ev.map((x) => x.ctx));
  const errs = alive.map((a) => (a.offset + (tref - a.when)) - (pos0 + (tref - tr)));
  const distinct = errs;
  const last = alive[alive.length - 1];
  return {
    tp, tr, dctx: tr - tp, alive: alive.length, restarted: ev.slice(iResume).some((x) => x.k === 'stop'),
    delay: last.when - last.ctx, offset: last.offset, pos0, errs: distinct,
    spread: Math.max(...errs) - Math.min(...errs),   // two copies of the music would differ by tens of ms or more
    e: errs.reduce((m, x) => (Math.abs(x) > Math.abs(m) ? x : m), 0),   // the worst source
  };
}
