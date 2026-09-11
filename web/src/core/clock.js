/**
 * Master rhythm clock.
 *
 * THE ONE RULE: every time value in this game lives in *audio time* (seconds,
 * AudioContext.currentTime domain). Input events, note schedules, judgement,
 * animation phase — all of it. `performance.now()` and requestAnimationFrame
 * timestamps are converted at the boundary and never used for judgement.
 *
 * Why: AudioContext.currentTime is the only clock the sound actually obeys.
 * Judging a tap against a rAF timestamp bakes in a variable 0-16ms of frame
 * jitter plus whatever drift exists between the two clocks, and that is the
 * difference between "tight" and "mushy".
 *
 * The perf->audio mapping is measured continuously and smoothed, because the
 * two clocks drift (different crystals, and Chrome quantises currentTime to
 * the render quantum). See `_syncDomains`.
 */

const LOOKAHEAD_S = 0.12; // how far ahead scheduled events are dispatched
const DOMAIN_SMOOTHING = 0.02; // EMA weight for perf->audio offset

export class Clock {
  /** @param {AudioContext} ctx */
  constructor(ctx) {
    this.ctx = ctx;

    // --- tempo map -------------------------------------------------------
    this._bpm = 120;
    this._origin = 0; // audio-time seconds at which beat 0 occurs
    this._beatAtOrigin = 0; // beat number at _origin (lets us change tempo mid-song)
    this._running = false;

    // --- clock-domain bridge --------------------------------------------
    // audioTime ≈ perfTimeSeconds + _domainOffset
    this._domainOffset = null;
    this._domainSamples = 0;

    // --- scheduling ------------------------------------------------------
    this._scheduled = []; // {beat, fn, done} sorted by beat
    this._beatListeners = new Set();
    this._lastDispatchedBeat = -Infinity;

    // Output latency compensation. Chrome reports the buffer+device latency;
    // if we don't subtract it, everything the player HEARS is late relative to
    // what we THINK we played, and every judgement is biased early.
    this._outputLatency = 0;
  }

  // ---------------------------------------------------------------- tempo

  get bpm() { return this._bpm; }

  /** Seconds per beat. */
  get spb() { return 60 / this._bpm; }

  /**
   * Change tempo without breaking the current beat phase. Used by tempo
   * ramps (the boss game speeds up) — the beat number stays continuous.
   */
  setBpm(bpm, atAudioTime = this.now()) {
    const beatHere = this.beatAt(atAudioTime);
    this._beatAtOrigin = beatHere;
    this._origin = atAudioTime;
    this._bpm = bpm;
  }

  /** Start the transport so that beat 0 lands at `audioTime`. */
  start(audioTime = this.now() + 0.1, startBeat = 0) {
    this._origin = audioTime;
    this._beatAtOrigin = startBeat;
    this._startBeat = startBeat;
    this._running = true;
    this._lastDispatchedBeat = startBeat - 1e-9;
    this._generation = (this._generation || 0) + 1;
  }

  /**
   * Bumped by every start(). Anything holding a cursor in beats (the music
   * player) compares it to notice the transport was restarted under it.
   */
  get generation() { return this._generation || 0; }

  /** The beat the last start() began counting from (0 for a fresh transport). */
  get startBeat() { return this._startBeat || 0; }

  stop() {
    this._running = false;
    this._scheduled.length = 0;
  }

  get running() { return this._running; }

  // ----------------------------------------------------------------- time

  /** Current audio time, latency-compensated: when the player HEARS "now". */
  now() {
    return this.ctx.currentTime - this._outputLatency;
  }

  /** Raw audio time — use this when scheduling into WebAudio nodes. */
  rawNow() { return this.ctx.currentTime; }

  refreshOutputLatency() {
    const l = this.ctx.outputLatency;
    if (typeof l === 'number' && isFinite(l) && l >= 0 && l < 0.5) {
      this._outputLatency = l;
    } else {
      // Safari and some Firefox builds don't expose outputLatency. baseLatency
      // is a floor, not the truth, but it beats assuming zero.
      const b = this.ctx.baseLatency;
      this._outputLatency = (typeof b === 'number' && isFinite(b)) ? b : 0;
    }
    return this._outputLatency;
  }

  get outputLatency() { return this._outputLatency; }

  // ------------------------------------------------------- beat <-> time

  /** Beat number at a given audio time. */
  beatAt(audioTime) {
    return this._beatAtOrigin + (audioTime - this._origin) / this.spb;
  }

  /** Audio time at which a given beat number occurs. */
  timeAt(beat) {
    return this._origin + (beat - this._beatAtOrigin) * this.spb;
  }

  /** Current beat as a float (3.25 = a sixteenth past beat 3). */
  get beat() { return this.beatAt(this.now()); }

  // --------------------------------------------------- clock-domain bridge

  /**
   * Called once per frame with the rAF timestamp (ms, performance domain).
   * Maintains the smoothed perf->audio offset used by `toAudioTime`.
   */
  _syncDomains(perfMs) {
    const perfS = perfMs / 1000;
    const observed = this.ctx.currentTime - perfS;
    if (this._domainOffset === null) {
      this._domainOffset = observed;
    } else {
      // Chrome quantises currentTime to 128-sample blocks, so `observed`
      // sawtooths by up to ~2.7ms. An EMA of the *minimum-ish* value would be
      // ideal; a plain EMA lands in the middle of the sawtooth and is stable,
      // which is what matters for consistency. Bias is removed by the
      // player's own latency calibration.
      this._domainOffset += (observed - this._domainOffset) * DOMAIN_SMOOTHING;
    }
    this._domainSamples++;
  }

  /**
   * Convert a DOM event timestamp (performance.now domain, ms) to audio time.
   * This is how a keypress becomes something we can judge.
   */
  toAudioTime(perfMs) {
    if (this._domainOffset === null) return this.now();
    return perfMs / 1000 + this._domainOffset - this._outputLatency;
  }

  get domainReady() { return this._domainSamples > 8; }

  // ------------------------------------------------------------ scheduling

  /** Run `fn(audioTime, beat)` shortly before `beat` arrives (lookahead). */
  at(beat, fn) {
    this._scheduled.push({ beat, fn, done: false });
    this._scheduled.sort((a, b) => a.beat - b.beat);
    return this;
  }

  /** Fire `fn(beatInt, audioTime)` on every integer beat. Returns unsubscribe. */
  onBeat(fn) {
    this._beatListeners.add(fn);
    return () => this._beatListeners.delete(fn);
  }

  clearSchedule() { this._scheduled.length = 0; }

  /**
   * Pump the scheduler. Call once per frame, before game update.
   * Dispatches anything whose time is within LOOKAHEAD_S so that WebAudio
   * nodes can be scheduled with a real head start instead of "now".
   */
  tick(perfMs) {
    if (perfMs !== undefined) this._syncDomains(perfMs);
    if (!this._running) return;

    const horizon = this.now() + LOOKAHEAD_S;
    const horizonBeat = this.beatAt(horizon);

    // scheduled one-shots
    for (const ev of this._scheduled) {
      if (ev.done) continue;
      if (ev.beat > horizonBeat) break; // sorted
      ev.done = true;
      ev.fn(this.timeAt(ev.beat), ev.beat);
    }
    if (this._scheduled.length > 64) {
      this._scheduled = this._scheduled.filter((e) => !e.done);
    }

    // integer beat pulses
    let next = Math.floor(this._lastDispatchedBeat) + 1;
    while (next <= horizonBeat) {
      const t = this.timeAt(next);
      for (const fn of this._beatListeners) fn(next, t);
      this._lastDispatchedBeat = next;
      next++;
    }
  }
}

/** Musical helpers — shared vocabulary so every module says "beat" the same way. */
export const Beats = {
  /** Signed distance in beats from `beat` to the nearest integer beat. */
  phaseError(beat) {
    return beat - Math.round(beat);
  },
  /** 0..1 position within the current beat. */
  frac(beat) {
    return beat - Math.floor(beat);
  },
  /** 0..1 position within a bar of `n` beats. */
  barFrac(beat, n = 4) {
    return ((beat % n) + n) % n / n;
  },
};
