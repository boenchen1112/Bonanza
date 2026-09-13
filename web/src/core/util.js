/** Small shared helpers. Deliberately dependency-free. */

export const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);
export const clamp01 = (v) => clamp(v, 0, 1);
export const lerp = (a, b, t) => a + (b - a) * t;
export const invLerp = (a, b, v) => (b === a ? 0 : (v - a) / (b - a));

/**
 * Fractional part of a beat, always 0..1. **Never write `x % 1` for this.**
 *
 * The transport runs negative beats through the lead-in, and `%` keeps the
 * dividend's sign — `-0.25 % 1` is `-0.25`, not `0.75`, so every pulse driven
 * off it inverts for the two bars before the first note. One game's comment
 * records that this "already crashed the codebase once"; it had been
 * re-derived in a dozen files before it lived here.
 */
export const beatPhase = (x) => x - Math.floor(x);
export const smoothstep = (t) => { t = clamp01(t); return t * t * (3 - 2 * t); };
export const smootherstep = (t) => { t = clamp01(t); return t * t * t * (t * (t * 6 - 15) + 10); };

/**
 * Frame-rate independent exponential approach.
 * `damp(current, target, lambda, dt)` — lambda is "how many e-folds per second",
 * so the result is identical at 30fps and 144fps. Never use raw `lerp(a,b,0.1)`
 * in an update loop; it silently changes feel with frame rate.
 */
export const damp = (a, b, lambda, dt) => lerp(a, b, 1 - Math.exp(-lambda * dt));

/** Overshooting ease — the backbone of every snappy pop-in. */
export const backOut = (t, s = 1.70158) => {
  t = clamp01(t) - 1;
  return t * t * ((s + 1) * t + s) + 1;
};

export const elasticOut = (t, amp = 1, period = 0.32) => {
  t = clamp01(t);
  if (t === 0 || t === 1) return t;
  const s = period / (2 * Math.PI) * Math.asin(1 / Math.max(amp, 1));
  return amp * Math.pow(2, -10 * t) * Math.sin((t - s) * (2 * Math.PI) / period) + 1;
};

export const easeOutCubic = (t) => 1 - Math.pow(1 - clamp01(t), 3);
export const easeInCubic = (t) => Math.pow(clamp01(t), 3);
export const easeOutQuint = (t) => 1 - Math.pow(1 - clamp01(t), 5);
export const easeInOutCubic = (t) => {
  t = clamp01(t);
  return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
};

/** Anticipation-then-snap, the shape of a good windup. */
export const anticipate = (t, back = 0.18) => {
  t = clamp01(t);
  if (t < 0.35) return -back * Math.sin((t / 0.35) * Math.PI);
  return easeOutQuint((t - 0.35) / 0.65);
};

/** Deterministic PRNG (mulberry32) — replays and tests need repeatable chaos. */
export function makeRng(seed = 0x9e3779b9) {
  let a = seed >>> 0;
  const next = () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
  next.range = (lo, hi) => lo + next() * (hi - lo);
  next.int = (lo, hi) => Math.floor(next.range(lo, hi + 1));
  next.pick = (arr) => arr[Math.floor(next() * arr.length)];
  next.sign = () => (next() < 0.5 ? -1 : 1);
  return next;
}

/** Tiny event bus. */
export class Bus {
  constructor() { this._m = new Map(); }
  on(evt, fn) {
    if (!this._m.has(evt)) this._m.set(evt, new Set());
    this._m.get(evt).add(fn);
    return () => this._m.get(evt)?.delete(fn);
  }
  once(evt, fn) {
    const off = this.on(evt, (...a) => { off(); fn(...a); });
    return off;
  }
  emit(evt, ...args) {
    const s = this._m.get(evt);
    if (s) for (const fn of [...s]) fn(...args);
  }
  clear() { this._m.clear(); }
}

/** localStorage that never throws (private mode, quota, disabled storage). */
export const Save = {
  get(key, fallback = null) {
    try {
      const raw = localStorage.getItem('bbb:' + key);
      return raw === null ? fallback : JSON.parse(raw);
    } catch { return fallback; }
  },
  set(key, value) {
    try { localStorage.setItem('bbb:' + key, JSON.stringify(value)); return true; }
    catch { return false; }
  },
  del(key) {
    try { localStorage.removeItem('bbb:' + key); } catch { /* ignore */ }
  },
};
