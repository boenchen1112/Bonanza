/**
 * MusicPlayer — the scheduling host.  [audio agent]
 *
 * The whole design is one idea: **the audio thread is never asked to do
 * anything "now"**. A pump runs off the frame loop (via `Clock.onBeat`, which
 * is itself driven by the clock's lookahead) plus a low-rate safety timer, and
 * every note is handed to Web Audio with a real head start, at an absolute
 * time derived from `clock.timeAt(beat)`. That is what makes the music stay
 * locked to the judgement clock instead of drifting away from it.
 *
 * Consequences that matter:
 *  - Tempo changes are free. `clock.setBpm` preserves beat phase, and because
 *    every event time is computed at *schedule* time (never cached), Finale
 *    Fever can ramp to 1.35x mid-loop and the groove just accelerates.
 *  - Intensity changes are quantised to bars, latched when the pump crosses
 *    the bar line. A layer can therefore never appear halfway through a bar,
 *    which is the difference between "the music reacted" and "something
 *    glitched".
 *  - A tab-switch cannot machine-gun. Events more than LATE_TOL in the past
 *    are skipped, not crammed in.
 */

import { compileTrack, layerActive } from './music/compile.js';
import { SCALES, degree, chord } from './theory.js';

/** How far ahead of the player's ears we hand notes to Web Audio. */
const HORIZON = 0.30;
/** Events older than this are dropped rather than crushed onto "now". */
const LATE_TOL = 0.06;
/** Beyond this we assume a stall (tab-switch) and fast-forward silently. */
const STALL = 0.6;

export function createMusicPlayer({ ctx, clock, voices, buses, sends, tracks }) {
  const trackGain = ctx.createGain();
  trackGain.gain.value = 0;
  trackGain.connect(buses.music);

  let track = null;
  let compiled = null;
  let playing = false;

  // transport
  let useClock = true;
  let localOrigin = 0;
  let localSpb = 0.5;
  let startBeat = 0;

  // cursor
  let pos = 0;
  let loopIndex = 0;
  let barCursor = -1;
  /** The clock.generation the cursor was laid against (see reanchor). */
  let anchorGen = 0;

  // adaptive state
  let intensity = 1;
  let pendingIntensity = 1;
  let manual = false;

  const barListeners = new Set();
  /** Test/debug hook: called with every scheduled event. */
  let onEvent = null;

  // ------------------------------------------------------------- transport

  const spb = () => (useClock ? 60 / clock.bpm : localSpb);
  const timeOf = (absBeat) => (useClock
    ? clock.timeAt(absBeat)
    : localOrigin + absBeat * localSpb);
  const beatNow = () => (useClock
    ? clock.beatAt(clock.now())
    : (clock.now() - localOrigin) / localSpb);

  // ------------------------------------------------------- musical context

  function scaleArr() {
    return SCALES[track?.scale] || SCALES.major;
  }

  /** Bar index within the loop for a given absolute beat. */
  function barOfBeat(absBeat) {
    const b = Math.floor((absBeat - startBeat) / compiled.beatsPerBar);
    return ((b % compiled.bars) + compiled.bars) % compiled.bars;
  }

  function chordDegAt(bar) {
    const p = track.prog;
    if (!p || !p.length) return 0;
    return p[bar % p.length];
  }

  /**
   * The musical "now" handed to a track's render function, and read by the SFX
   * kit so a verdict sound can be *in key*.
   */
  function musicalCtx(bar) {
    const sc = scaleArr();
    const cdeg = chordDegAt(bar);
    return {
      track,
      bar,
      intensity,
      scale: sc,
      scaleName: track.scale,
      root: track.root,
      chordDeg: cdeg,
      spb: spb(),
      bpm: useClock ? clock.bpm : 60 / localSpb,
      /** Chord tones for the current bar. */
      chordMidis(shape = track.shape || [0, 2, 4], octaves = 0) {
        return chord(track.root, sc, cdeg, shape, octaves);
      },
      /** Resolve a layer's scale degree to MIDI, honouring rel:'chord'. */
      midiFor(layer, deg) {
        const base = layer.rel === 'chord' ? cdeg : 0;
        return degree(track.root, sc, base + deg) + (layer.octave ?? 0) * 12;
      },
      degree: (deg, oct = 0) => degree(track.root, sc, deg) + oct * 12,
    };
  }

  // ---------------------------------------------------------------- control

  /**
   * @param {string|object} which  track id or track object
   * @param {{atBeat?: number, intensity?: number, fade?: number}} [opts]
   */
  function play(which, opts = {}) {
    const t = typeof which === 'string' ? tracks[which] : which;
    if (!t) return false;
    if (track === t && playing) return true;

    track = t;
    compiled = compileTrack(t);
    if (!compiled.events.length) { track = null; return false; }

    useClock = !!clock.running;
    const bpb = compiled.beatsPerBar;

    if (useClock) {
      // Start on the next bar line so the track's bar 1 is the game's bar 1.
      const soon = clock.beatAt(clock.now() + 0.22);
      startBeat = opts.atBeat !== undefined ? opts.atBeat : Math.ceil(soon / bpb) * bpb;
    } else {
      localSpb = 60 / t.bpm;
      localOrigin = clock.now() + 0.18;
      startBeat = opts.atBeat ?? 0;
    }

    pos = 0;
    loopIndex = 0;
    barCursor = -1;
    anchorGen = clock.generation ?? 0;
    manual = opts.intensity !== undefined;
    intensity = pendingIntensity = opts.intensity ?? 1;
    playing = true;

    const g = trackGain.gain;
    const n = ctx.currentTime;
    g.cancelScheduledValues(n);
    g.setValueAtTime(Math.max(g.value, 1e-4), n);
    g.linearRampToValueAtTime(t.mix ?? 1, n + (opts.fade ?? 0.12));

    if (sends?.setDelayTime) sends.setDelayTime(spb() * 0.75, n);
    pump();
    return true;
  }

  function stop({ fade = 0.25 } = {}) {
    if (!playing) return;
    playing = false;
    const g = trackGain.gain;
    const n = ctx.currentTime;
    g.cancelScheduledValues(n);
    g.setValueAtTime(Math.max(g.value, 1e-4), n);
    g.exponentialRampToValueAtTime(1e-4, n + fade);
  }

  /** Queue an intensity. It lands on the next bar line, never mid-bar. */
  function setIntensity(n, { immediate = false } = {}) {
    pendingIntensity = Math.max(0, Math.min(3, Math.round(n)));
    manual = true;
    if (immediate || !playing) intensity = pendingIntensity;
  }

  /** Used by the automatic combo->intensity mapping; yields to manual control. */
  function suggestIntensity(n) {
    if (manual) return;
    pendingIntensity = Math.max(0, Math.min(3, Math.round(n)));
  }

  // ------------------------------------------------------------------ pump

  function onBar(absBar) {
    if (intensity !== pendingIntensity) intensity = pendingIntensity;
    if (sends?.setDelayTime) {
      sends.setDelayTime(spb() * 0.75, Math.max(ctx.currentTime, timeOf(startBeat + absBar * compiled.beatsPerBar)));
    }
    if (barListeners.size) {
      const inLoop = ((absBar % compiled.bars) + compiled.bars) % compiled.bars;
      for (const fn of barListeners) fn(inLoop, absBar, intensity);
    }
  }

  function scheduleEvent(ev, t, absBeat) {
    const bar = barOfBeat(absBeat);
    const m = musicalCtx(bar);
    const render = ev.layer.render || track.render || defaultRender;
    try {
      render(voices, ev, t, m);
    } catch (e) {
      // A broken voice must never take the frame loop with it.
      if (typeof console !== 'undefined') console.warn('music render failed', e);
    }
    // Kick-driven sidechain: the tonal bus dips under every kick. This is what
    // makes a synth arrangement breathe instead of sitting there as a slab.
    if (ev.layer.duck !== false && ev.layer.voice === 'kick' && buses.duck) {
      const p = buses.duck.gain;
      p.setValueAtTime(1, t);
      p.linearRampToValueAtTime(0.76, t + 0.014);
      p.linearRampToValueAtTime(1, t + Math.min(0.17, spb() * 0.55));
    }
    if (onEvent) onEvent({ ...ev, time: t, absBeat, bar, intensity });
  }

  /**
   * The transport was restarted under a playing track. Every shell scene
   * starts it over at beat 0 while the menu track plays on; the cursor was
   * still at the old screen's beat count, so nothing sounded until the new
   * clock caught up — seconds of dead air per screen. Continue from the next
   * bar of the loop on the new grid's next bar line. A restart that carries
   * the beat count on (pause/resume) leaves the cursor where it is.
   */
  function reanchor() {
    anchorGen = clock.generation;
    const { events, loopBeats, bars, beatsPerBar: bpb } = compiled;
    const now = clock.now();
    if (useClock && pos < events.length) {
      const t = clock.timeAt(startBeat + loopIndex * loopBeats + events[pos].beat);
      if (t >= now - STALL && t <= now + HORIZON + bpb * spb()) return;
    }
    useClock = true;
    const next = (((barCursor + 1) % bars) + bars) % bars;
    const line = Math.ceil(clock.beatAt(now + 0.05) / bpb) * bpb;
    startBeat = line - next * bpb;
    loopIndex = 0;
    pos = events.findIndex((e) => e.bar >= next);
    if (pos < 0) pos = events.length;
    barCursor = next - 1;
  }

  /** Idempotent: safe to call from several sources in the same frame. */
  function pump() {
    if (!playing || !track) return;
    if (clock.running && (clock.generation ?? 0) !== anchorGen) reanchor();
    const now = clock.now();
    const horizon = now + HORIZON;
    const { events, loopBeats } = compiled;
    let scheduled = 0;
    let skipped = 0;

    while (scheduled < 256 && skipped < 20000) {
      if (pos >= events.length) {
        if (!track.loop) {
          if (timeOf(startBeat + loopIndex * loopBeats + loopBeats) < now) stop({ fade: 0.4 });
          return;
        }
        loopIndex++;
        pos = 0;
      }
      const ev = events[pos];
      const absBeat = startBeat + loopIndex * loopBeats + ev.beat;
      const t = timeOf(absBeat);
      if (t > horizon) break;

      const evBar = loopIndex * compiled.bars + ev.bar;
      while (barCursor < evBar) { barCursor++; onBar(barCursor); }

      if (t < now - STALL) {
        // Tab-switch / long stall: silently fast-forward, never machine-gun.
        skipped++;
      } else if (t < now - LATE_TOL) {
        skipped++;
      } else if (layerActive(ev.layer, intensity)) {
        scheduleEvent(ev, Math.max(t, ctx.currentTime + 0.002), absBeat);
        scheduled++;
      }
      pos++;
    }
  }

  // ------------------------------------------------------------ inspection

  function info() {
    if (!track || !playing) return null;
    const b = beatNow();
    const bar = barOfBeat(b);
    return {
      id: track.id,
      bpm: useClock ? clock.bpm : 60 / localSpb,
      bar,
      barFrac: (((b - startBeat) / compiled.beatsPerBar) % 1 + 1) % 1,
      beatInBar: ((b - startBeat) % compiled.beatsPerBar + compiled.beatsPerBar) % compiled.beatsPerBar,
      intensity,
      bars: compiled.bars,
    };
  }

  /** What key/chord are we in right now? The SFX kit reads this. */
  function scaleInfo() {
    if (!track || !playing) return null;
    const bar = barOfBeat(beatNow());
    const m = musicalCtx(bar);
    return {
      root: track.root,
      scale: m.scale,
      scaleName: track.scale,
      chordDeg: m.chordDeg,
      chord: m.chordMidis(track.shape || [0, 2, 4]),
      bar,
      intensity,
    };
  }

  return {
    play, stop, pump, setIntensity, suggestIntensity, info, scaleInfo,
    onBar: (fn) => { barListeners.add(fn); return () => barListeners.delete(fn); },
    set onEvent(fn) { onEvent = fn; },
    get onEvent() { return onEvent; },
    get intensity() { return intensity; },
    get pendingIntensity() { return pendingIntensity; },
    get playing() { return playing; },
    get track() { return track; },
    get compiled() { return compiled; },
    get gain() { return trackGain; },
  };
}

/**
 * Default renderer: turn one compiled event into one voice call.
 * Tracks may override per-layer (`layer.render`) or wholesale (`track.render`)
 * — Finale Fever does, to bolt a riser onto its last bar.
 */
export function defaultRender(V, ev, t, m) {
  const L = ev.layer;
  const voice = V[L.voice];
  if (!voice) return;
  const o = L.opts ? { ...L.opts } : {};
  o.gain = (L.gain ?? 0.5) * ev.vel;

  if (ev.kind === 'drum') {
    if (L.voice === 'tom' && L.opts?.freq === undefined) o.freq = 180;
    voice(t, o);
    return;
  }
  if (ev.kind === 'chord') {
    o.midis = m.chordMidis(L.shape || m.track.shape || [0, 2, 4], L.octave ?? 0);
    o.dur = ev.dur * m.spb;
    voice(t, o);
    return;
  }
  // `degShift` lets a harmony layer reuse a melody's note data verbatim — a
  // brass section doubling the hook a third below costs zero extra pattern.
  o.midi = m.midiFor(L, ev.deg + (L.degShift ?? 0));
  o.dur = ev.dur * m.spb;
  voice(t, o);
}
