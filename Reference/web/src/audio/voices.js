/**
 * Instrument voices.  [audio agent]
 *
 * Every voice is a small function `(t, opts)` that schedules a complete sound
 * at the ABSOLUTE audio time `t` and tears itself down. No voice keeps state,
 * no voice reads `currentTime`, no voice can be "late" — if you hand it a time
 * in the future it will be exactly there.
 *
 * Node budget matters: a busy bar of Chomp Chorus fires ~40 events, so each
 * voice is built from the smallest graph that still sounds like an instrument
 * rather than a beep. Sends (reverb / delay) are only wired when the caller
 * actually asks for them.
 */

import { adsr, makeNoise } from './dsp.js';
import { mtof, cents } from './theory.js';

/**
 * @param {{ctx: BaseAudioContext, rng: () => number, buses: object}} o
 *   buses: { drums, tone, sfx, reverb, delay } — AudioNodes.
 */
export function createVoices({ ctx, rng, buses }) {
  const noiseBuf = makeNoise(ctx, rng, 1.2);
  const canPan = typeof ctx.createStereoPanner === 'function';

  // ---------------------------------------------------------------- plumbing

  /** Output stage: trim -> [pan] -> dest, plus optional reverb/delay sends. */
  function out({ dest = buses.tone, gain = 1, pan = 0, rev = 0, dly = 0 }) {
    const trim = ctx.createGain();
    trim.gain.value = gain;
    let tail = trim;
    if (pan && canPan) {
      const p = ctx.createStereoPanner();
      p.pan.value = Math.max(-1, Math.min(1, pan));
      trim.connect(p);
      tail = p;
    }
    tail.connect(dest);
    if (rev > 0 && buses.reverb) {
      const g = ctx.createGain(); g.gain.value = rev;
      tail.connect(g); g.connect(buses.reverb);
    }
    if (dly > 0 && buses.delay) {
      const g = ctx.createGain(); g.gain.value = dly;
      tail.connect(g); g.connect(buses.delay);
    }
    return trim;
  }

  function osc(type, freq, t, detune = 0) {
    const o = ctx.createOscillator();
    o.type = type;
    o.frequency.setValueAtTime(freq, t);
    if (detune) o.detune.setValueAtTime(detune, t);
    return o;
  }

  function noiseSrc(t, rate = 1) {
    const s = ctx.createBufferSource();
    s.buffer = noiseBuf;
    s.playbackRate.value = rate;
    // Random start offset so consecutive hats never phase-lock into a tone.
    s.loop = true;
    return s;
  }

  function lp(freq, q = 1, type = 'lowpass') {
    const f = ctx.createBiquadFilter();
    f.type = type;
    f.frequency.value = freq;
    f.Q.value = q;
    return f;
  }

  // ------------------------------------------------------------ percussion

  /** Deep, short, tuned. The one sound that has to be right or nothing grooves. */
  function kick(t, { gain = 0.95, dest = buses.drums, tune = 1, decay = 0.22, click = 0.5, rev = 0.03 } = {}) {
    const o = osc('sine', 165 * tune, t);
    const g = ctx.createGain();
    o.frequency.exponentialRampToValueAtTime(44 * tune, t + 0.055);
    o.frequency.exponentialRampToValueAtTime(38 * tune, t + decay);
    o.connect(g);
    g.connect(out({ dest, gain, rev }));
    const end = adsr(g.gain, t, { a: 0.0015, d: decay, peak: 1 });
    o.start(t); o.stop(end + 0.02);

    if (click > 0) {
      const n = noiseSrc(t, 1.4);
      const hp = lp(1800, 0.7, 'highpass');
      const cg = ctx.createGain();
      n.connect(hp); hp.connect(cg);
      cg.connect(out({ dest, gain: gain * click * 0.45 }));
      const ce = adsr(cg.gain, t, { a: 0.0005, d: 0.014, peak: 1 });
      n.start(t); n.stop(ce + 0.02);
    }
  }

  /** Body + snares. `snap` pushes it from marching-band crack to pop backbeat. */
  function snare(t, { gain = 0.7, dest = buses.drums, decay = 0.16, tone = 1, snap = 1, rev = 0.16, pan = 0 } = {}) {
    const dst = out({ dest, gain, rev, pan });
    const o1 = osc('triangle', 186 * tone, t);
    const o2 = osc('triangle', 331 * tone, t);
    const bg = ctx.createGain();
    o1.connect(bg); o2.connect(bg);
    bg.connect(dst);
    const be = adsr(bg.gain, t, { a: 0.001, d: decay * 0.55, peak: 0.42 });
    o1.start(t); o2.start(t); o1.stop(be + 0.02); o2.stop(be + 0.02);

    const n = noiseSrc(t, 1 + rng() * 0.12);
    const bp = lp(1750, 0.6, 'bandpass');
    const hp = lp(680, 0.7, 'highpass');
    const ng = ctx.createGain();
    n.connect(bp); bp.connect(hp); hp.connect(ng); ng.connect(dst);
    const ne = adsr(ng.gain, t, { a: 0.0008, d: decay * snap, peak: 0.85 });
    n.start(t); n.stop(ne + 0.02);
  }

  /** Layered noise bursts — reads as "a room full of people", not one hand. */
  function clap(t, { gain = 0.6, dest = buses.drums, rev = 0.24, pan = 0 } = {}) {
    const dst = out({ dest, gain, rev, pan });
    const offs = [0, 0.0105, 0.0208, 0.0305];
    for (let i = 0; i < offs.length; i++) {
      const n = noiseSrc(t + offs[i], 1 + rng() * 0.2);
      const bp = lp(1250 + i * 140, 1.6, 'bandpass');
      const g = ctx.createGain();
      n.connect(bp); bp.connect(g); g.connect(dst);
      const last = i === offs.length - 1;
      const e = adsr(g.gain, t + offs[i], {
        a: 0.0006, d: last ? 0.13 : 0.016, peak: last ? 0.9 : 0.55,
      });
      n.start(t + offs[i]); n.stop(e + 0.02);
    }
  }

  function hat(t, { gain = 0.32, dest = buses.drums, open = 0, tone = 1, pan = 0.12 } = {}) {
    const n = noiseSrc(t, 1.6 + rng() * 0.25);
    const hp = lp(7200 * tone, 0.8, 'highpass');
    const bp = lp(11000, 0.5, 'lowpass');
    const g = ctx.createGain();
    n.connect(hp); hp.connect(bp); bp.connect(g);
    g.connect(out({ dest, gain, pan, rev: open > 0 ? 0.1 : 0.02 }));
    const e = adsr(g.gain, t, { a: 0.0005, d: 0.028 + open * 0.3, peak: 1 });
    n.start(t); n.stop(e + 0.02);
  }

  function tom(t, { gain = 0.6, dest = buses.drums, freq = 180, decay = 0.28, rev = 0.14, pan = 0 } = {}) {
    const dst = out({ dest, gain, rev, pan });
    const o = osc('sine', freq * 1.35, t);
    o.frequency.exponentialRampToValueAtTime(freq * 0.82, t + decay * 0.8);
    const g = ctx.createGain();
    o.connect(g); g.connect(dst);
    const e = adsr(g.gain, t, { a: 0.001, d: decay, peak: 1 });
    o.start(t); o.stop(e + 0.02);

    const n = noiseSrc(t, 1);
    const bp = lp(freq * 3, 1.2, 'bandpass');
    const ng = ctx.createGain();
    n.connect(bp); bp.connect(ng); ng.connect(dst);
    const ne = adsr(ng.gain, t, { a: 0.0005, d: 0.03, peak: 0.35 });
    n.start(t); n.stop(ne + 0.02);
  }

  function rim(t, { gain = 0.4, dest = buses.drums, pan = -0.2, rev = 0.1 } = {}) {
    const dst = out({ dest, gain, pan, rev });
    const o = osc('square', 1720, t);
    const g = ctx.createGain();
    const bp = lp(2400, 4, 'bandpass');
    o.connect(bp); bp.connect(g); g.connect(dst);
    const e = adsr(g.gain, t, { a: 0.0004, d: 0.035, peak: 0.7 });
    o.start(t); o.stop(e + 0.02);
  }

  function shaker(t, { gain = 0.22, dest = buses.drums, pan = -0.3 } = {}) {
    const n = noiseSrc(t, 1.9);
    const hp = lp(5200, 0.9, 'highpass');
    const g = ctx.createGain();
    n.connect(hp); hp.connect(g); g.connect(out({ dest, gain, pan }));
    const e = adsr(g.gain, t, { a: 0.006, d: 0.05, peak: 1 });
    n.start(t); n.stop(e + 0.02);
  }

  /** The bar-1 marker. Long, bright, unmistakable. */
  function crash(t, { gain = 0.4, dest = buses.drums, decay = 1.35, rev = 0.4, pan = 0 } = {}) {
    const n = noiseSrc(t, 0.85);
    const hp = lp(3600, 0.6, 'highpass');
    const g = ctx.createGain();
    n.connect(hp); hp.connect(g); g.connect(out({ dest, gain, rev, pan }));
    const e = adsr(g.gain, t, { a: 0.002, d: decay, peak: 1 });
    n.start(t); n.stop(e + 0.05);
  }

  function ride(t, { gain = 0.26, dest = buses.drums, bell = 0, rev = 0.18, pan = 0.25 } = {}) {
    const dst = out({ dest, gain, rev, pan });
    const n = noiseSrc(t, 1.2);
    const hp = lp(6200, 0.7, 'highpass');
    const g = ctx.createGain();
    n.connect(hp); hp.connect(g); g.connect(dst);
    const e = adsr(g.gain, t, { a: 0.001, d: 0.22 + bell * 0.3, peak: 0.7 });
    n.start(t); n.stop(e + 0.02);
    if (bell > 0) {
      const parts = [1, 1.51, 2.31];
      for (const p of parts) {
        const o = osc('sine', 1180 * p, t);
        const og = ctx.createGain();
        o.connect(og); og.connect(dst);
        const oe = adsr(og.gain, t, { a: 0.001, d: 0.5, peak: 0.22 * bell });
        o.start(t); o.stop(oe + 0.02);
      }
    }
  }

  // --------------------------------------------------------------- tonal

  /**
   * Sub-heavy bass with a filter envelope. `drive` fattens it with a squared
   * partial so it survives laptop speakers that reproduce nothing under 150Hz.
   */
  function bass(t, {
    midi = 41, dur = 0.5, gain = 0.5, dest = buses.tone, cutoff = 3.2,
    res = 6, sub = 0.6, drive = 0.35, glide = 0, rev = 0.05, dly = 0,
  } = {}) {
    const f = mtof(midi);
    const dst = out({ dest, gain, rev, dly });
    const filt = lp(Math.min(f * cutoff * 2.4, 8000), res);
    filt.frequency.setValueAtTime(Math.min(f * cutoff * 2.4, 8000), t);
    filt.frequency.exponentialRampToValueAtTime(Math.max(f * 1.6, 90), t + Math.min(dur, 0.32));
    const g = ctx.createGain();
    filt.connect(g); g.connect(dst);

    const o = osc('sawtooth', glide ? mtof(midi + glide) : f, t);
    if (glide) o.frequency.exponentialRampToValueAtTime(f, t + 0.045);
    o.connect(filt);

    const o2 = osc('square', f, t, -6);
    const g2 = ctx.createGain(); g2.gain.value = drive;
    o2.connect(g2); g2.connect(filt);

    const os = osc('sine', f / 2, t);
    const gs = ctx.createGain(); gs.gain.value = sub;
    os.connect(gs); gs.connect(dst);

    const hold = Math.max(0, dur - 0.06);
    const end = adsr(g.gain, t, { a: 0.006, d: 0.05, s: 0.72, hold, r: 0.07, peak: 0.85 });
    adsr(gs.gain, t, { a: 0.008, d: 0.05, s: 0.8, hold, r: 0.07, peak: sub });
    o.start(t); o2.start(t); os.start(t);
    o.stop(end + 0.02); o2.stop(end + 0.02); os.stop(end + 0.02);
  }

  /** Short, woody, marimba/koto-ish. The workhorse of the bouncy track. */
  function pluck(t, {
    midi = 60, dur = 0.25, gain = 0.34, dest = buses.tone, bright = 3.5,
    rev = 0.2, dly = 0.12, pan = 0,
  } = {}) {
    const f = mtof(midi);
    const dst = out({ dest, gain, rev, dly, pan });
    const filt = lp(Math.min(f * bright * 2.2, 11000), 3.2);
    filt.frequency.setValueAtTime(Math.min(f * bright * 2.2, 11000), t);
    filt.frequency.exponentialRampToValueAtTime(Math.max(f * 1.2, 120), t + Math.min(dur * 1.4, 0.5));
    const g = ctx.createGain();
    filt.connect(g); g.connect(dst);

    const o = osc('sawtooth', f, t);
    const o2 = osc('triangle', f * 2, t);
    const g2 = ctx.createGain(); g2.gain.value = 0.35;
    o.connect(filt); o2.connect(g2); g2.connect(filt);

    const n = noiseSrc(t, 1);
    const ng = ctx.createGain();
    const nbp = lp(f * 4, 1.4, 'bandpass');
    n.connect(nbp); nbp.connect(ng); ng.connect(dst);
    adsr(ng.gain, t, { a: 0.0004, d: 0.012, peak: 0.22 });

    const end = adsr(g.gain, t, { a: 0.002, d: Math.max(dur * 1.3, 0.09), peak: 0.9 });
    o.start(t); o2.start(t); n.start(t);
    o.stop(end + 0.02); o2.stop(end + 0.02); n.stop(t + 0.06);
  }

  /** FM bell. Two operators, index envelope — cheap and unmistakably "shiny". */
  function bell(t, { midi = 84, dur = 0.6, gain = 0.24, dest = buses.tone, ratio = 3.5, index = 6, rev = 0.35, dly = 0.2, pan = 0 } = {}) {
    const f = mtof(midi);
    const dst = out({ dest, gain, rev, dly, pan });
    const car = osc('sine', f, t);
    const mod = osc('sine', f * ratio, t);
    const mg = ctx.createGain();
    mg.gain.setValueAtTime(f * index, t);
    mg.gain.exponentialRampToValueAtTime(Math.max(f * 0.2, 1), t + Math.min(dur, 0.5));
    mod.connect(mg); mg.connect(car.frequency);
    const g = ctx.createGain();
    car.connect(g); g.connect(dst);
    const end = adsr(g.gain, t, { a: 0.002, d: Math.max(dur * 1.5, 0.2), peak: 0.9 });
    car.start(t); mod.start(t); car.stop(end + 0.02); mod.stop(end + 0.02);
  }

  /** Detuned saw lead. Vibrato only on long notes — an LFO per 16th is waste. */
  function lead(t, {
    midi = 72, dur = 0.4, gain = 0.3, dest = buses.tone, detune = 9,
    cutoff = 5, res = 2.5, glide = 0, rev = 0.16, dly = 0.22, pan = 0, vib = 0.5,
  } = {}) {
    const f = mtof(midi);
    const dst = out({ dest, gain, rev, dly, pan });
    const filt = lp(Math.min(f * cutoff * 2.6, 12000), res);
    filt.frequency.setValueAtTime(Math.min(f * cutoff * 3.4, 13000), t);
    filt.frequency.exponentialRampToValueAtTime(Math.min(f * cutoff, 9000), t + 0.12);
    const g = ctx.createGain();
    filt.connect(g); g.connect(dst);

    const oscs = [];
    for (let i = 0; i < 2; i++) {
      const o = osc('sawtooth', glide ? mtof(midi + glide) : f, t, i === 0 ? -detune : detune);
      if (glide) o.frequency.exponentialRampToValueAtTime(f, t + 0.05);
      o.connect(filt);
      oscs.push(o);
    }
    const sq = osc('square', f * 2, t, 4);
    const sg = ctx.createGain(); sg.gain.value = 0.16;
    sq.connect(sg); sg.connect(filt);
    oscs.push(sq);

    let lfo = null;
    if (dur > 0.45 && vib > 0) {
      lfo = osc('sine', 5.4, t);
      const la = ctx.createGain();
      la.gain.setValueAtTime(0, t);
      la.gain.linearRampToValueAtTime(f * 0.006 * vib, t + 0.18);
      lfo.connect(la);
      for (const o of oscs) la.connect(o.frequency);
      lfo.start(t);
    }

    const hold = Math.max(0, dur - 0.09);
    const end = adsr(g.gain, t, { a: 0.008, d: 0.06, s: 0.75, hold, r: 0.1, peak: 0.9 });
    for (const o of oscs) { o.start(t); o.stop(end + 0.02); }
    if (lfo) lfo.stop(end + 0.02);
  }

  /**
   * Brass. Three saws, a filter that opens *into* the note, and a semitone of
   * portamento underneath — the three things that make a synth read as horns.
   */
  function brass(t, {
    midi = 65, dur = 0.5, gain = 0.28, dest = buses.tone, rev = 0.22, dly = 0.1,
    pan = 0, bite = 1,
  } = {}) {
    const f = mtof(midi);
    const dst = out({ dest, gain, rev, dly, pan });
    const filt = lp(f * 1.4, 3.4);
    filt.frequency.setValueAtTime(Math.max(f * 1.1, 120), t);
    filt.frequency.linearRampToValueAtTime(Math.min(f * (5 + bite * 3), 9500), t + 0.055);
    filt.frequency.exponentialRampToValueAtTime(Math.min(f * 3.2, 6500), t + Math.min(dur, 0.5));
    const g = ctx.createGain();
    filt.connect(g); g.connect(dst);
    const dets = [-7, 5, 12];
    const oscs = dets.map((d, i) => {
      const o = osc(i === 2 ? 'square' : 'sawtooth', f * cents(-14), t, d);
      o.frequency.exponentialRampToValueAtTime(f, t + 0.035);
      if (i === 2) {
        const q = ctx.createGain(); q.gain.value = 0.22;
        o.connect(q); q.connect(filt);
      } else o.connect(filt);
      return o;
    });
    const hold = Math.max(0, dur - 0.1);
    const end = adsr(g.gain, t, { a: 0.016, d: 0.07, s: 0.78, hold, r: 0.1, peak: 0.85 });
    for (const o of oscs) { o.start(t); o.stop(end + 0.02); }
  }

  /** Slow, wide, heavily reverbed chord bed. Takes an array of MIDI notes. */
  function pad(t, { midis = [60, 64, 67], dur = 2, gain = 0.16, dest = buses.tone, rev = 0.55, cutoff = 2200 } = {}) {
    const dst = out({ dest, gain, rev });
    const filt = lp(cutoff * 0.5, 0.8);
    filt.frequency.setValueAtTime(cutoff * 0.5, t);
    filt.frequency.linearRampToValueAtTime(cutoff, t + Math.min(dur * 0.4, 1.2));
    const g = ctx.createGain();
    filt.connect(g); g.connect(dst);
    const oscs = [];
    for (let i = 0; i < midis.length; i++) {
      const f = mtof(midis[i]);
      const a = osc('sawtooth', f, t, -6);
      const b = osc('triangle', f, t, 7);
      const bg = ctx.createGain(); bg.gain.value = 0.7;
      a.connect(filt); b.connect(bg); bg.connect(filt);
      oscs.push(a, b);
    }
    const hold = Math.max(0, dur - 0.5);
    const end = adsr(g.gain, t, { a: 0.22, d: 0.2, s: 0.85, hold, r: 0.4, peak: 0.8 / Math.sqrt(midis.length) });
    for (const o of oscs) { o.start(t); o.stop(end + 0.05); }
  }

  /** Drawbar organ — additive sines. Cheap, and instantly "arena". */
  function organ(t, { midis = [60, 64, 67], dur = 1, gain = 0.14, dest = buses.tone, rev = 0.3, drawbars = [1, 0.55, 0.35, 0.22, 0.12] } = {}) {
    const dst = out({ dest, gain, rev });
    const g = ctx.createGain();
    g.connect(dst);
    const oscs = [];
    const harm = [1, 2, 3, 4, 6];
    for (const m of midis) {
      const f = mtof(m);
      for (let h = 0; h < drawbars.length; h++) {
        if (drawbars[h] <= 0) continue;
        const o = osc('sine', f * harm[h], t);
        const og = ctx.createGain(); og.gain.value = drawbars[h];
        o.connect(og); og.connect(g);
        oscs.push(o);
      }
    }
    const hold = Math.max(0, dur - 0.1);
    const end = adsr(g.gain, t, { a: 0.02, d: 0.06, s: 0.9, hold, r: 0.12, peak: 0.6 / Math.sqrt(midis.length * 2) });
    for (const o of oscs) { o.start(t); o.stop(end + 0.03); }
  }

  /** Short chord hit: the "hnk" that makes a funk track move. */
  function stab(t, { midis = [60, 64, 67], dur = 0.18, gain = 0.22, dest = buses.tone, rev = 0.18, dly = 0.12, pan = 0, wah = 1 } = {}) {
    const dst = out({ dest, gain, rev, dly, pan });
    const base = mtof(midis[0]);
    const filt = lp(base * 2, 7);
    filt.frequency.setValueAtTime(base * (2 + wah * 6), t);
    filt.frequency.exponentialRampToValueAtTime(Math.max(base * 1.3, 140), t + Math.max(dur, 0.1));
    const g = ctx.createGain();
    filt.connect(g); g.connect(dst);
    const oscs = [];
    for (const m of midis) {
      const f = mtof(m);
      oscs.push(osc('sawtooth', f, t, -5), osc('sawtooth', f, t, 6));
    }
    for (const o of oscs) o.connect(filt);
    const end = adsr(g.gain, t, { a: 0.004, d: Math.max(dur, 0.08), peak: 0.8 / Math.sqrt(midis.length) });
    for (const o of oscs) { o.start(t); o.stop(end + 0.02); }
  }

  /** Orchestral hit — chord stack + noise crack + long tail. Section markers. */
  function orchHit(t, { midis = [60, 64, 67], gain = 0.3, dest = buses.tone, rev = 0.5 } = {}) {
    stab(t, { midis, dur: 0.3, gain: gain * 0.8, dest, rev, wah: 1.4 });
    const n = noiseSrc(t, 1);
    const bp = lp(2200, 0.8, 'bandpass');
    const g = ctx.createGain();
    n.connect(bp); bp.connect(g); g.connect(out({ dest, gain: gain * 0.5, rev: 0.5 }));
    const e = adsr(g.gain, t, { a: 0.002, d: 0.22, peak: 1 });
    n.start(t); n.stop(e + 0.02);
  }

  /** Noise riser — telegraphs a section change one bar early. */
  function riser(t, { dur = 2, gain = 0.16, dest = buses.tone, from = 400, to = 6000, rev = 0.3 } = {}) {
    const n = noiseSrc(t, 1);
    const bp = lp(from, 6, 'bandpass');
    bp.frequency.setValueAtTime(from, t);
    bp.frequency.exponentialRampToValueAtTime(to, t + dur);
    const g = ctx.createGain();
    n.connect(bp); bp.connect(g); g.connect(out({ dest, gain, rev }));
    g.gain.setValueAtTime(1e-4, t);
    g.gain.exponentialRampToValueAtTime(Math.max(gain, 1e-3), t + dur * 0.9);
    g.gain.exponentialRampToValueAtTime(1e-4, t + dur + 0.12);
    n.start(t); n.stop(t + dur + 0.16);
  }

  /** Downward siren — failure, but the funny kind. */
  function fall(t, { midi = 64, dur = 0.35, gain = 0.2, dest = buses.sfx, rev = 0.15 } = {}) {
    const f = mtof(midi);
    const o = osc('sawtooth', f, t);
    o.frequency.exponentialRampToValueAtTime(Math.max(f * 0.4, 40), t + dur);
    const filt = lp(f * 3, 4);
    filt.frequency.setValueAtTime(f * 3, t);
    filt.frequency.exponentialRampToValueAtTime(Math.max(f * 0.9, 120), t + dur);
    const g = ctx.createGain();
    o.connect(filt); filt.connect(g); g.connect(out({ dest, gain, rev }));
    const end = adsr(g.gain, t, { a: 0.004, d: dur, peak: 0.9 });
    o.start(t); o.stop(end + 0.02);
  }

  // ------------------------------------------------------- legacy primitives
  // Kept byte-compatible with the original facade: callers outside audio/ still
  // use these and must not break.

  function env(node, at, { a = 0.002, d = 0.12, s = 0, r = 0.05, peak = 1, sustainFor = 0 }) {
    return adsr(node.gain, at, { a, d, s, hold: sustainFor, r, peak });
  }

  function tone(at, {
    freq = 440, type = 'triangle', gain = 0.3, a = 0.003, d = 0.14,
    bend = 0, dest = buses.sfx, detune = 0, rev = 0.12, pan = 0,
  } = {}) {
    const o = osc(type, freq, at, detune);
    if (bend) o.frequency.exponentialRampToValueAtTime(Math.max(freq * bend, 1), at + d);
    const g = ctx.createGain();
    o.connect(g); g.connect(out({ dest, gain: 1, rev, pan }));
    const end = adsr(g.gain, at, { a, d, peak: gain });
    o.start(at); o.stop(end + 0.02);
    return end;
  }

  function hit(at, { gain = 0.3, d = 0.08, hp = 1200, lp: lpf = 9000, dest = buses.sfx, rev = 0.1, pan = 0 } = {}) {
    const s = noiseSrc(at, 1 + rng() * 0.2);
    const h = lp(hp, 0.7, 'highpass');
    const l = lp(lpf, 0.7, 'lowpass');
    const g = ctx.createGain();
    s.connect(h); h.connect(l); l.connect(g); g.connect(out({ dest, gain: 1, rev, pan }));
    const end = adsr(g.gain, at, { a: 0.001, d, peak: gain });
    s.start(at); s.stop(end + 0.02);
    return end;
  }

  return {
    // percussion
    kick, snare, clap, hat, tom, rim, shaker, crash, ride,
    // tonal
    bass, pluck, bell, lead, brass, pad, organ, stab, orchHit, riser, fall,
    // primitives / legacy
    tone, hit, env, noise: () => noiseBuf, out,
    ctx,
  };
}

/** Voices that take `midis` (a chord) rather than a single `midi`. */
export const CHORD_VOICES = new Set(['pad', 'organ', 'stab', 'orchHit']);
