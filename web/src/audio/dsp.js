/**
 * Shared DSP building blocks.  [audio agent]
 *
 * No fetches, no files: every buffer here is generated from a seeded PRNG, so
 * two runs of the game produce byte-identical noise and the offline render
 * check is reproducible.
 *
 * The one rule every function obeys: **absolute times only**. Nothing here
 * takes "now" as an implicit argument. A rhythm game that calls `.start()`
 * with no argument sounds drunk, and it is impossible to debug because the
 * error is a function of frame timing.
 */

const EPS = 1e-4;

/** Bounded, click-free exponential ramp (Web Audio explodes on a 0 target). */
export function ramp(param, value, at) {
  param.exponentialRampToValueAtTime(Math.max(value, EPS), at);
}

/**
 * AD / ADSR envelope written onto a gain param at an absolute time.
 * Returns the time the envelope has finished, so callers know when to stop().
 *
 * `hold` is derived from a musical duration by the note voices, which is why
 * a whole-note pad and a 16th stab share one code path.
 */
export function adsr(param, t, { a = 0.004, d = 0.09, s = 0, hold = 0, r = 0.08, peak = 1 } = {}) {
  const p = Math.max(peak, EPS);
  param.cancelScheduledValues(t);
  param.setValueAtTime(EPS, t);
  param.exponentialRampToValueAtTime(p, t + a);
  if (s > 0) {
    const sv = Math.max(p * s, EPS);
    param.exponentialRampToValueAtTime(sv, t + a + d);
    if (hold > 0) param.setValueAtTime(sv, t + a + d + hold);
    param.exponentialRampToValueAtTime(EPS, t + a + d + hold + r);
    return t + a + d + hold + r;
  }
  param.exponentialRampToValueAtTime(EPS, t + a + d);
  return t + a + d;
}

/**
 * White-ish noise with a touch of low-passed energy mixed back in, which
 * reads as "air" rather than "hiss". One second, looped by playbackRate
 * variation so repeated hats don't phase into a buzz.
 */
export function makeNoise(ctx, rng, seconds = 1.2) {
  const n = Math.max(1, Math.floor(ctx.sampleRate * seconds));
  const buf = ctx.createBuffer(1, n, ctx.sampleRate);
  const d = buf.getChannelData(0);
  let lp = 0;
  for (let i = 0; i < n; i++) {
    const w = rng() * 2 - 1;
    lp += (w - lp) * 0.05;
    d[i] = w * 0.72 + lp * 1.6;
  }
  return buf;
}

/**
 * Synthetic room impulse: exponentially decaying noise plus a handful of
 * discrete early reflections, decorrelated between channels.
 *
 * Why bother: dry subtractive synth in a big colourful cartoon space sounds
 * like a phone speaker. The early reflections are what actually sell "this
 * is a stadium" — the tail alone just sounds like mud.
 */
export function makeImpulse(ctx, rng, { seconds = 1.9, decay = 2.7, damp = 0.42 } = {}) {
  const n = Math.max(1, Math.floor(ctx.sampleRate * seconds));
  const buf = ctx.createBuffer(2, n, ctx.sampleRate);
  for (let c = 0; c < 2; c++) {
    const d = buf.getChannelData(c);
    let lp = 0;
    for (let i = 0; i < n; i++) {
      const x = rng() * 2 - 1;
      lp += (x - lp) * damp;
      const env = Math.pow(1 - i / n, decay);
      // build-up at the very front so the tail blooms instead of clicking in
      const onset = Math.min(1, i / (ctx.sampleRate * 0.006));
      d[i] = (lp * 0.85 + x * 0.15) * env * onset;
    }
    const taps = [0.0091, 0.0143, 0.0211, 0.0297, 0.0413, 0.0577];
    for (let k = 0; k < taps.length; k++) {
      const i = Math.floor(taps[k] * (1 + c * 0.083) * ctx.sampleRate);
      if (i < n) d[i] += (c === 0 ? 1 : -1) * 0.42 * Math.pow(0.72, k);
    }
  }
  return buf;
}

/**
 * Soft-clip curve for the drive stage. Tanh-ish, so pushing the bass into it
 * adds harmonics (audible on a laptop speaker with no low end) instead of
 * crackling.
 */
export function makeDriveCurve(amount = 2.2, n = 1024) {
  const curve = new Float32Array(n);
  for (let i = 0; i < n; i++) {
    const x = (i / (n - 1)) * 2 - 1;
    curve[i] = Math.tanh(x * amount) / Math.tanh(amount);
  }
  return curve;
}
