/**
 * Audio facade: mixer, sends, SFX, music.  [audio agent owns this directory]
 *
 * Everything is synthesised. No files, no fetches, no decode stalls — which
 * also means the first note is never late because a buffer wasn't ready.
 *
 * Scheduling rule: sounds are scheduled at an absolute audio time, always in
 * the future by at least the lookahead. Calling `.start()` with no argument is
 * how a rhythm game ends up sounding drunk.
 *
 * ─── The one idea worth stealing from this file ─────────────────────────────
 * Verdict SFX are *pitched into the music*. `music.scaleInfo()` reports the
 * chord the track is sitting on right now, and PERFECT is an ascending arpeggio
 * of that chord, GREAT is two of its tones, OKAY is its root, and WHIFF slides
 * off it. So a good run doesn't just score higher — it harmonises, and the
 * player hears themselves playing the song rather than triggering a buzzer.
 * The cost is one lookup per sound.
 *
 * Signal flow:
 *
 *   drums ────────────────┐
 *   tone ──▶ duck ────────┤ (sidechained by every kick)
 *                         ▼
 *                     trackGain ──▶ musicBus ─┐
 *   loose voices ─────────────────▶ musicBus  │
 *   sfx voices ───────────────────▶ sfxBus ───┤──▶ comp ──▶ master ──▶ clip ──▶ out
 *   reverb / delay sends ─────────────────────┘
 */

import { clamp01 } from '../core/util.js';
import { FEEL } from '../core/feel.js';
import { makeRng } from '../core/util.js';
import { makeImpulse, makeDriveCurve } from './dsp.js';
import { createVoices } from './voices.js';
import { createMusicPlayer } from './player.js';
import { TRACKS, trackForScene } from './music/index.js';
import { SCALES, arpUp, snapToChord, degree, note } from './theory.js';

/** Fallback key when nothing is playing, so SFX are never atonal. */
const SILENT_KEY = {
  root: note('C', 2),
  scale: SCALES.major,
  scaleName: 'major',
  chordDeg: 0,
  chord: [note('C', 3), note('E', 3), note('G', 3)],
  bar: 0,
  intensity: 1,
};

export function createAudio({ ctx, clock, bus, offline = false }) {
  const rng = makeRng(0xbea7);

  // --- master chain --------------------------------------------------------
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

  // Final safety net: a tanh curve bounded just under full scale. The limiter
  // handles musical dynamics; this handles the pathological case (every layer
  // plus a fanfare plus eight simultaneous verdicts) without a single sample
  // ever leaving [-1, 1].
  let clip = null;
  if (typeof ctx.createWaveShaper === 'function') {
    clip = ctx.createWaveShaper();
    clip.curve = makeDriveCurve(1.35);
    for (let i = 0; i < clip.curve.length; i++) clip.curve[i] *= 0.985;
    if ('oversample' in clip) clip.oversample = '2x';
  }

  musicBus.connect(comp);
  sfxBus.connect(comp);
  comp.connect(master);
  if (clip) { master.connect(clip); clip.connect(ctx.destination); } else master.connect(ctx.destination);

  // --- sends: reverb + ping-pong delay -------------------------------------
  // Dry subtractive synth in a big colourful space sounds like a phone speaker.
  // One shared room and one tempo-locked delay fix that for the whole game at
  // a fixed cost of ~10 nodes, instead of per-voice effects that would not.
  const reverbIn = ctx.createGain();
  const reverbOut = ctx.createGain();
  reverbOut.gain.value = 0.9;
  const conv = ctx.createConvolver();
  try { conv.buffer = makeImpulse(ctx, makeRng(0x1e5), { seconds: 1.9, decay: 2.7 }); } catch { /* stub ctx */ }
  const revHp = ctx.createBiquadFilter();
  revHp.type = 'highpass';
  revHp.frequency.value = 240;
  reverbIn.connect(revHp); revHp.connect(conv); conv.connect(reverbOut); reverbOut.connect(comp);

  const delayIn = ctx.createGain();
  const dL = ctx.createDelay(1.5);
  const dR = ctx.createDelay(1.5);
  dL.delayTime.value = 0.28;
  dR.delayTime.value = 0.28;
  const dFb = ctx.createGain();
  dFb.gain.value = 0.3;
  const dDamp = ctx.createBiquadFilter();
  dDamp.type = 'lowpass';
  dDamp.frequency.value = 2600;
  const dOut = ctx.createGain();
  dOut.gain.value = 0.5;
  const canPan = typeof ctx.createStereoPanner === 'function';
  const pL = canPan ? ctx.createStereoPanner() : ctx.createGain();
  const pR = canPan ? ctx.createStereoPanner() : ctx.createGain();
  if (canPan) { pL.pan.value = -0.7; pR.pan.value = 0.7; }
  delayIn.connect(dL);
  dL.connect(pL); pL.connect(dOut);
  dL.connect(dR);
  dR.connect(pR); pR.connect(dOut);
  dR.connect(dDamp); dDamp.connect(dFb); dFb.connect(dL);
  dOut.connect(comp);
  dOut.connect(reverbIn);

  function setDelayTime(seconds, at) {
    const s = Math.max(0.03, Math.min(1.4, seconds));
    const t = Math.max(at ?? ctx.currentTime, ctx.currentTime);
    dL.delayTime.setTargetAtTime(s, t, 0.04);
    dR.delayTime.setTargetAtTime(s, t, 0.04);
  }
  const sends = { setDelayTime, reverbIn, delayIn };

  // --- sub-buses -----------------------------------------------------------
  const mDrums = ctx.createGain();
  const mTone = ctx.createGain();
  const duck = ctx.createGain();   // sidechain target, driven by music kicks
  duck.gain.value = 1;
  mTone.connect(duck);

  // Voices used by direct callers (`audio.voices.kick(t)`) bypass the music
  // fader entirely, so a UI thump still sounds when no track is playing.
  const looseBus = ctx.createGain();
  looseBus.gain.value = 0.9;
  looseBus.connect(musicBus);

  const busesMusic = { music: musicBus, drums: mDrums, tone: mTone, sfx: sfxBus, reverb: reverbIn, delay: delayIn, duck };
  const busesLoose = { music: musicBus, drums: looseBus, tone: looseBus, sfx: sfxBus, reverb: reverbIn, delay: delayIn };

  const musicVoices = createVoices({ ctx, rng: makeRng(0xf00d), buses: busesMusic });
  const looseVoices = createVoices({ ctx, rng: makeRng(0xd00f), buses: busesLoose });

  // --- music ---------------------------------------------------------------
  const player = createMusicPlayer({
    ctx, clock, voices: musicVoices, buses: busesMusic, sends, tracks: TRACKS,
  });
  mDrums.connect(player.gain);
  duck.connect(player.gain);

  // ---------------------------------------------------------------- SFX kit

  /** The chord we are currently sitting on. Never null. */
  function key() {
    return player.scaleInfo() || SILENT_KEY;
  }

  const V = looseVoices;

  const SFX = {
    /**
     * PERFECT: ascending arpeggio of the CURRENT chord + a bell on the top.
     * Because it is chord-locked, stacking three PERFECTs in a bar makes a
     * chord rather than a mess.
     */
    perfect(at) {
      const k = key();
      const arp = arpUp(k.chord, note('C', 5), 3);
      V.hit(at, { gain: 0.14, d: 0.035, hp: 4200, rev: 0.1 });
      arp.forEach((m, i) => {
        V.pluck(at + i * 0.042, {
          midi: m, dur: 0.16, gain: 0.2 - i * 0.02, bright: 5, rev: 0.22, dly: 0.14, dest: sfxBus,
        });
      });
      V.bell(at + 0.09, {
        midi: arp[2] + 12, dur: 0.5, gain: 0.15, ratio: 2.01, index: 4, rev: 0.4, dly: 0.24, dest: sfxBus,
      });
    },
    /** GREAT: two chord tones. Same family, one rung down. */
    great(at) {
      const k = key();
      const arp = arpUp(k.chord, note('C', 5), 2);
      V.hit(at, { gain: 0.1, d: 0.03, hp: 3600, rev: 0.08 });
      arp.forEach((m, i) => {
        V.pluck(at + i * 0.05, {
          midi: m, dur: 0.15, gain: 0.18 - i * 0.02, bright: 4.2, rev: 0.2, dly: 0.1, dest: sfxBus,
        });
      });
    },
    /** OKAY: the chord root alone. Correct, unexciting — exactly the message. */
    good(at) {
      const k = key();
      V.pluck(at, {
        midi: snapToChord(note('G', 4), k.chord), dur: 0.14, gain: 0.17,
        bright: 3, rev: 0.16, dly: 0.06, dest: sfxBus,
      });
      V.hit(at, { gain: 0.06, d: 0.025, hp: 2600, rev: 0.05 });
    },
    /**
     * WHIFF: slides off the chord instead of landing on it. Still in tempo,
     * still in the same register — funny, not punishing.
     */
    miss(at) {
      const k = key();
      V.fall(at, { midi: snapToChord(note('C', 4), k.chord) + 1, dur: 0.34, gain: 0.16, dest: sfxBus });
      V.hit(at, { gain: 0.07, d: 0.09, hp: 220, lp: 1500, rev: 0.06 });
    },

    tick(at, gain = 0.12) { V.hit(at, { gain, d: 0.018, hp: 4600, rev: 0.04 }); },

    ui(at) {
      const k = key();
      V.pluck(at, {
        midi: snapToChord(note('E', 5), k.chord), dur: 0.09, gain: 0.16,
        bright: 5, rev: 0.14, dly: 0.08, dest: sfxBus,
      });
    },
    uiBack(at) {
      const k = key();
      V.pluck(at, {
        midi: snapToChord(note('A', 3), k.chord), dur: 0.11, gain: 0.15,
        bright: 2.6, rev: 0.14, dest: sfxBus,
      });
    },

    /**
     * Count-in. `n` is the beat within the count bar; callers that don't pass
     * it get the right note anyway, derived from the clock.
     */
    count(at, n) {
      const k = key();
      let i = n;
      if (typeof i !== 'number' || !isFinite(i)) {
        const b = Math.round(clock.beatAt(at));
        i = ((b % 4) + 4) % 4;
      }
      const accent = i === 0;
      const midi = degree(k.root, k.scale, accent ? 14 : 11) ;
      V.pluck(at, {
        midi, dur: accent ? 0.2 : 0.13, gain: accent ? 0.24 : 0.17,
        bright: accent ? 5.5 : 3.6, rev: 0.2, dest: sfxBus,
      });
      if (accent) V.hit(at, { gain: 0.1, d: 0.03, hp: 3800 });
    },

    /** Combo: climbs the scale, so a streak literally goes up. */
    combo(at, level = 1) {
      const k = key();
      const step = Math.min(Math.max(level | 0, 0), 9);
      const m = degree(k.root, k.scale, 9 + step);
      V.bell(at, { midi: m, dur: 0.42, gain: 0.16, ratio: 2.01, index: 4.5, rev: 0.4, dly: 0.22, dest: sfxBus });
      V.pluck(at + 0.05, { midi: m + 12, dur: 0.2, gain: 0.1, bright: 5, rev: 0.3, dest: sfxBus });
    },

    /** Short brass cadence in the current key — the "you did it" stinger. */
    fanfare(at) {
      const k = key();
      const seq = [[0, 7], [0.09, 9], [0.18, 11], [0.3, 14]];
      for (const [dt, deg] of seq) {
        const m = degree(k.root, k.scale, deg);
        V.brass(at + dt, { midi: m, dur: dt === 0.3 ? 0.7 : 0.16, gain: 0.2, bite: 1.3, rev: 0.35, dest: sfxBus });
        V.pluck(at + dt, { midi: m + 12, dur: 0.2, gain: 0.08, bright: 4, rev: 0.3, dest: sfxBus });
      }
      V.crash(at, { gain: 0.22, decay: 1.1, rev: 0.5, dest: sfxBus });
    },

    // --- additions the minigames can reach for -----------------------------
    swoosh(at) {
      V.riser(at, { dur: 0.28, gain: 0.1, from: 700, to: 5200, rev: 0.2, dest: sfxBus });
    },
    impact(at) {
      const k = key();
      V.kick(at, { gain: 0.7, dest: sfxBus, decay: 0.24, tune: 0.9 });
      V.stab(at, { midis: k.chord.map((m) => m + 12), dur: 0.22, gain: 0.18, wah: 1.6, rev: 0.35, dest: sfxBus });
    },
    coin(at) {
      const k = key();
      const m = degree(k.root, k.scale, 14);
      V.bell(at, { midi: m, dur: 0.1, gain: 0.13, ratio: 3.5, index: 3, rev: 0.2, dest: sfxBus });
      V.bell(at + 0.07, { midi: m + 5, dur: 0.3, gain: 0.13, ratio: 3.5, index: 3, rev: 0.3, dest: sfxBus });
    },
    ready(at) { SFX.count(at, 1); },
    go(at) { SFX.count(at, 0); },
  };

  /** Play an SFX at an absolute audio time (defaults to "as soon as safe"). */
  function sfx(name, at, ...args) {
    const fn = SFX[name];
    if (!fn) return;
    const t = Math.max(at ?? clock.rawNow(), clock.rawNow() + 0.001);
    try { fn(t, ...args); } catch (e) { /* audio must never break gameplay */ }
  }

  // ------------------------------------------------------- adaptive layers
  // Combo drives musical intensity, quantised to bar lines by the player.
  // This is the whole point of the layer system: a good run has to SOUND like
  // a good run before the player has read a single number on the HUD.
  let streak = 0;
  let missRun = 0;
  const [LVL2, LVL3] = [FEEL.comboMilestones[0] ?? 5, FEEL.comboMilestones[2] ?? 20];

  function onJudge(j) {
    const v = j && j.verdict;
    if (!v) return;
    if (v === 'miss') {
      streak = 0;
      missRun++;
      player.suggestIntensity(missRun >= 2 ? 0 : 1);
    } else {
      streak++;
      missRun = 0;
      player.suggestIntensity(streak >= LVL3 ? 3 : streak >= LVL2 ? 2 : 1);
    }
  }

  // --------------------------------------------------------------- pumping
  // Two sources, one idempotent pump: the clock's per-beat lookahead (the
  // musical one) and a low-rate timer (the safety net for scenes that never
  // start the transport, e.g. the results screen).
  let unsubBeat = null;
  let timer = null;
  if (!offline) {
    if (clock.onBeat) unsubBeat = clock.onBeat(() => player.pump());
    if (typeof setInterval === 'function') timer = setInterval(() => player.pump(), 30);
    if (bus?.on) {
      bus.on('judge', onJudge);
      bus.on('scene:active', (id) => {
        streak = 0; missRun = 0;
        const wanted = trackForScene(id);
        // A minigame that picked its own track during start() keeps it.
        if (!wanted) { player.stop(); return; }
        if (player.playing && player.track && player.track.id === wanted) return;
        if (player.playing && player.track && player.track.id !== wanted
            && player.track.__explicit) return;
        player.play(wanted);
      });
      bus.on('audio:unlocked', () => { if (ctx.state === 'suspended') ctx.resume?.(); });
    }
  }

  // ----------------------------------------------------------------- facade

  const music = {
    /** @param {string|object} which track id, scene id, or a track object */
    play(which, opts) {
      const id = typeof which === 'string' ? (TRACKS[which] ? which : trackForScene(which)) : which;
      if (!id) return false;
      const ok = player.play(id, opts);
      if (ok && player.track) player.track.__explicit = true;
      return ok;
    },
    stop(opts) { player.stop(opts || {}); },
    /** Pause menu: silent, cursor kept; resume() continues in step with the transport. */
    pause(opts) { player.pause(opts || {}); },
    resume(opts) { player.resume(opts || {}); },
    setIntensity(n, opts) { player.setIntensity(n, opts); },
    get intensity() { return player.intensity; },
    get playing() { return player.playing; },
    get track() { return player.track; },
    info: () => player.info(),
    /** Current key + chord, so callers can pitch their own sounds into it. */
    scaleInfo: () => player.scaleInfo() || { ...SILENT_KEY },
    onBar: (fn) => player.onBar(fn),
    pump: () => player.pump(),
    tracks: TRACKS,
    player,
    /** Legacy shape — older callers reached in here for raw voices. */
    voices: looseVoices,
  };

  function setVolume(which, v) {
    const g = which === 'music' ? musicBus : which === 'sfx' ? sfxBus : master;
    g.gain.setTargetAtTime(clamp01(v), ctx.currentTime, 0.02);
  }

  async function init() {
    if (ctx.state === 'suspended') {
      try { await ctx.resume(); } catch { /* gesture required; retried on input */ }
    }
    return true;
  }

  function dispose() {
    if (unsubBeat) unsubBeat();
    if (timer && typeof clearInterval === 'function') clearInterval(timer);
    player.stop({ fade: 0.1 });
  }

  const facade = {
    init, sfx, music, setVolume, dispose,
    ctx, master, musicBus, sfxBus, comp,
    /** Direct voice access. `voices.kick(t)` is always audible. */
    voices: Object.assign(looseVoices, { musicBus, sfxBus, ctx }),
    sends,
    get unlocked() { return ctx.state === 'running'; },
    /** Test hook — the offline render check and the critic harness use this. */
    renderCheck: (trackId, seconds, opts) => renderTrackOffline({ trackId, seconds, ...opts }),
  };

  // Dev/test reach-in so the harness can drive an offline render without the
  // integrator having to thread audio through `window.__BBB__`.
  // Dev/harness builds only (see TEST_API in main.js).
  const testApi = import.meta.env && (import.meta.env.DEV || import.meta.env.MODE === 'harness');
  if (testApi && !offline && typeof window !== 'undefined') window.__BBB_AUDIO__ = facade;

  return facade;
}

// ---------------------------------------------------------------------------
// Offline render check
// ---------------------------------------------------------------------------

/**
 * Render a track through the real graph in an OfflineAudioContext and report
 * peak/RMS. Used by the offline clipping check: if peak > 1.0 the mix is
 * wrong, and no amount of "it sounded fine on my laptop" changes that.
 *
 * Browser-only (Node has no OfflineAudioContext).
 */
export async function renderTrackOffline({
  trackId, seconds = 12, sampleRate = 44100, intensity = 3, bpm = null,
} = {}) {
  const OAC = typeof OfflineAudioContext !== 'undefined' ? OfflineAudioContext : null;
  if (!OAC) throw new Error('OfflineAudioContext unavailable');
  const track = TRACKS[trackId];
  if (!track) throw new Error('unknown track ' + trackId);

  const ctx = new OAC(2, Math.ceil(sampleRate * seconds), sampleRate);
  const clock = makeVirtualClock(bpm || track.bpm);
  const audio = createAudio({ ctx, clock, bus: null, offline: true });
  audio.music.play(trackId, { atBeat: 0, intensity });

  // Pre-schedule the whole render by walking the virtual clock forward. Every
  // event time is absolute, so the offline graph receives exactly what a live
  // run would have received — just all at once.
  for (let t = 0; t <= seconds; t += 0.05) {
    clock.setNow(t);
    audio.music.pump();
  }

  const buf = await ctx.startRendering();
  let peak = 0;
  let sum = 0;
  let n = 0;
  for (let c = 0; c < buf.numberOfChannels; c++) {
    const d = buf.getChannelData(c);
    for (let i = 0; i < d.length; i++) {
      const a = Math.abs(d[i]);
      if (a > peak) peak = a;
      sum += d[i] * d[i];
      n++;
    }
  }
  return {
    trackId, seconds, sampleRate, intensity,
    peak, rms: Math.sqrt(sum / Math.max(n, 1)),
    clipped: peak > 1.0,
    silent: peak < 0.02,
  };
}

/** Minimal Clock stand-in for offline rendering and tests. */
export function makeVirtualClock(bpm = 120) {
  let _now = 0;
  let _bpm = bpm;
  let origin = 0;
  let beatAtOrigin = 0;
  return {
    running: true,
    get bpm() { return _bpm; },
    get spb() { return 60 / _bpm; },
    setNow(t) { _now = t; },
    now: () => _now,
    rawNow: () => _now,
    beatAt: (t) => beatAtOrigin + (t - origin) / (60 / _bpm),
    timeAt: (b) => origin + (b - beatAtOrigin) * (60 / _bpm),
    get beat() { return beatAtOrigin + (_now - origin) / (60 / _bpm); },
    setBpm(v, at = _now) {
      beatAtOrigin += (at - origin) / (60 / _bpm);
      origin = at;
      _bpm = v;
    },
    onBeat: () => () => {},
  };
}
