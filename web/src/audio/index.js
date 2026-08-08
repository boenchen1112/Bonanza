/**
 * Audio facade: mixer, SFX, music.  [audio agent owns this directory]
 *
 * Everything is synthesised. No files, no fetches, no decode stalls — which
 * also means the first note is never late because a buffer wasn't ready.
 *
 * Scheduling rule: sounds are scheduled at an absolute audio time, always in
 * the future by at least the lookahead. Calling `.start()` with no argument is
 * how a rhythm game ends up sounding drunk.
 */

import { clamp01 } from '../core/util.js';

export function createAudio({ ctx, clock, bus }) {
  // --- mixer ---------------------------------------------------------------
  const master = ctx.createGain();
  master.gain.value = 0.9;

  const musicBus = ctx.createGain();
  const sfxBus = ctx.createGain();
  musicBus.gain.value = 0.75;
  sfxBus.gain.value = 0.85;

  // A gentle limiter keeps a 50-combo firework from clipping into crackle.
  const comp = ctx.createDynamicsCompressor();
  comp.threshold.value = -8;
  comp.knee.value = 12;
  comp.ratio.value = 6;
  comp.attack.value = 0.003;
  comp.release.value = 0.18;

  musicBus.connect(comp);
  sfxBus.connect(comp);
  comp.connect(master);
  master.connect(ctx.destination);

  // --- noise buffer (shared) ------------------------------------------------
  let noiseBuf = null;
  function noise() {
    if (!noiseBuf) {
      const n = ctx.sampleRate * 1.0;
      noiseBuf = ctx.createBuffer(1, n, ctx.sampleRate);
      const d = noiseBuf.getChannelData(0);
      let last = 0;
      for (let i = 0; i < n; i++) {
        const w = Math.random() * 2 - 1;
        last = 0.98 * last + 0.02 * w;
        d[i] = w * 0.7 + last * 0.3;
      }
    }
    return noiseBuf;
  }

  // --- primitive voices -----------------------------------------------------

  function env(node, at, { a = 0.002, d = 0.12, s = 0, r = 0.05, peak = 1, sustainFor = 0 }) {
    const g = node.gain;
    g.cancelScheduledValues(at);
    g.setValueAtTime(0.0001, at);
    g.exponentialRampToValueAtTime(Math.max(peak, 0.0002), at + a);
    if (s > 0 && sustainFor > 0) {
      g.exponentialRampToValueAtTime(Math.max(peak * s, 0.0002), at + a + d);
      g.setValueAtTime(Math.max(peak * s, 0.0002), at + a + d + sustainFor);
      g.exponentialRampToValueAtTime(0.0001, at + a + d + sustainFor + r);
      return at + a + d + sustainFor + r;
    }
    g.exponentialRampToValueAtTime(0.0001, at + a + d);
    return at + a + d + 0.01;
  }

  /** Tonal blip. */
  function tone(at, {
    freq = 440, type = 'triangle', gain = 0.3, a = 0.003, d = 0.14,
    bend = 0, dest = sfxBus, detune = 0,
  } = {}) {
    const o = ctx.createOscillator();
    const g = ctx.createGain();
    o.type = type;
    o.frequency.setValueAtTime(freq, at);
    if (bend) o.frequency.exponentialRampToValueAtTime(Math.max(freq * bend, 1), at + d);
    o.detune.value = detune;
    o.connect(g); g.connect(dest);
    const end = env(g, at, { a, d, peak: gain });
    o.start(at); o.stop(end + 0.02);
    return end;
  }

  /** Percussive noise hit. */
  function hit(at, { gain = 0.3, d = 0.08, hp = 1200, lp = 9000, dest = sfxBus } = {}) {
    const s = ctx.createBufferSource();
    s.buffer = noise();
    s.playbackRate.value = 1 + Math.random() * 0.2;
    const hpf = ctx.createBiquadFilter(); hpf.type = 'highpass'; hpf.frequency.value = hp;
    const lpf = ctx.createBiquadFilter(); lpf.type = 'lowpass'; lpf.frequency.value = lp;
    const g = ctx.createGain();
    s.connect(hpf); hpf.connect(lpf); lpf.connect(g); g.connect(dest);
    const end = env(g, at, { a: 0.001, d, peak: gain });
    s.start(at); s.stop(end + 0.02);
    return end;
  }

  function kick(at, { gain = 0.7, dest = musicBus } = {}) {
    const o = ctx.createOscillator();
    const g = ctx.createGain();
    o.type = 'sine';
    o.frequency.setValueAtTime(150, at);
    o.frequency.exponentialRampToValueAtTime(42, at + 0.09);
    o.connect(g); g.connect(dest);
    const end = env(g, at, { a: 0.001, d: 0.19, peak: gain });
    o.start(at); o.stop(end + 0.02);
  }

  // --- SFX kit --------------------------------------------------------------

  const SFX = {
    perfect(at) {
      tone(at, { freq: 880, type: 'square', gain: 0.16, d: 0.07 });
      tone(at + 0.045, { freq: 1318.5, type: 'square', gain: 0.14, d: 0.10 });
      tone(at + 0.09, { freq: 1760, type: 'triangle', gain: 0.13, d: 0.16 });
      hit(at, { gain: 0.18, d: 0.05, hp: 3000 });
    },
    great(at) {
      tone(at, { freq: 740, type: 'square', gain: 0.14, d: 0.08 });
      tone(at + 0.05, { freq: 1109, type: 'triangle', gain: 0.11, d: 0.12 });
      hit(at, { gain: 0.12, d: 0.04, hp: 2600 });
    },
    good(at) {
      tone(at, { freq: 523, type: 'triangle', gain: 0.13, d: 0.11 });
      hit(at, { gain: 0.08, d: 0.035, hp: 2000 });
    },
    miss(at) {
      tone(at, { freq: 196, type: 'sawtooth', gain: 0.12, d: 0.2, bend: 0.6 });
      hit(at, { gain: 0.07, d: 0.09, hp: 300, lp: 1400 });
    },
    tick(at, gain = 0.12) { hit(at, { gain, d: 0.02, hp: 4000 }); },
    ui(at) { tone(at, { freq: 660, type: 'square', gain: 0.12, d: 0.06 }); },
    uiBack(at) { tone(at, { freq: 330, type: 'square', gain: 0.12, d: 0.07 }); },
    count(at, n) {
      tone(at, { freq: n === 0 ? 880 : 440 + n * 60, type: 'square', gain: 0.2, d: 0.12 });
    },
    combo(at, level = 1) {
      const scale = [523, 587, 659, 784, 880, 1046, 1174, 1318];
      const f = scale[Math.min(level, scale.length - 1)];
      tone(at, { freq: f, type: 'square', gain: 0.15, d: 0.1 });
      tone(at + 0.06, { freq: f * 1.5, type: 'triangle', gain: 0.12, d: 0.16 });
    },
    fanfare(at) {
      const notes = [523, 659, 784, 1046];
      notes.forEach((f, i) => {
        tone(at + i * 0.09, { freq: f, type: 'square', gain: 0.18, d: 0.22 });
        tone(at + i * 0.09, { freq: f * 2, type: 'triangle', gain: 0.09, d: 0.22 });
      });
    },
  };

  /** Play an SFX at an absolute audio time (defaults to "as soon as safe"). */
  function sfx(name, at) {
    const fn = SFX[name];
    if (!fn) return;
    const t = Math.max(at ?? clock.rawNow(), clock.rawNow() + 0.001);
    try { fn(t); } catch (e) { /* audio must never break gameplay */ }
  }

  // --- music ----------------------------------------------------------------
  // Tracks are pure data + a per-step render fn; see audio/music/.
  let currentTrack = null;

  const music = {
    play(track) { currentTrack = track; },
    stop() { currentTrack = null; },
    get track() { return currentTrack; },
    /** Called by the track host each scheduled step. */
    voices: { tone, hit, kick, env, noise, musicBus, sfxBus, ctx },
  };

  function setVolume(which, v) {
    const g = which === 'music' ? musicBus : which === 'sfx' ? sfxBus : master;
    g.gain.setTargetAtTime(clamp01(v), ctx.currentTime, 0.02);
  }

  async function init() {
    if (ctx.state === 'suspended') {
      try { await ctx.resume(); } catch { /* gesture required; retried on input */ }
    }
    noise();
    return true;
  }

  return {
    init, sfx, music, setVolume,
    ctx, master, musicBus, sfxBus,
    voices: music.voices,
    get unlocked() { return ctx.state === 'running'; },
  };
}
