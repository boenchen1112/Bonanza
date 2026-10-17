/**
 * Conducting-gesture detector (Swing Kings gesture mode). Fed a stream of
 * (time, y) positions — pointer or tracked hand, y growing DOWNWARD in screen
 * heights — it reports a RAISE when the hand lifts (the windup) and an ICTUS
 * at the sharp deceleration that ends a downward stroke (the downbeat), which
 * is the same event every timing measurement in this repo is anchored to.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import { createIctusDetector } from '../src/games/swingKings/gesture.js';

const FPS = 60;

/** Run a trajectory y(t) through a fresh detector; collect its events. */
function run(yAt, seconds, opts) {
  const d = createIctusDetector(opts);
  const out = [];
  for (let i = 0; i <= seconds * FPS; i++) {
    const t = i / FPS;
    const e = d.feed(t, yAt(t));
    if (e) out.push(e);
  }
  return out;
}

/** Rest low, lift over 0.4s, hold, then strike down over `strike` s and stop dead. */
function conduct({ lift = 0.25, strike = 0.12, strikeAt = 1.0, hold = 0.3 } = {}) {
  const low = 0.7, high = low - lift;
  return (t) => {
    if (t < 0.3) return low;
    if (t < 0.7) return low - lift * ((t - 0.3) / 0.4);
    if (t < strikeAt) return high;
    if (t < strikeAt + strike) {
      const u = (t - strikeAt) / strike;
      return high + lift * u * u;              // accelerating downward
    }
    return low;                                // stops dead at the bottom
  };
}

test('a lift then a sharp downstroke gives one raise and one ictus at the stop', () => {
  const ev = run(conduct(), 1.6);
  assert.deepEqual(ev.map((e) => e.type), ['raise', 'ictus']);
  const ictus = ev[1];
  // The ictus is the deceleration at the bottom of the stroke (t = 1.12s),
  // within two frames — not the start of the stroke, not the peak.
  assert.ok(Math.abs(ictus.time - 1.12) <= 2 / FPS, `ictus at ${ictus.time}`);
  assert.ok(ev[0].time > 0.3 && ev[0].time < 0.75, `raise at ${ev[0].time}`);
});

test('slow drifting movement never fires', () => {
  const ev = run((t) => 0.6 + 0.1 * Math.sin(t * 1.2), 6);
  assert.deepEqual(ev.filter((e) => e.type === 'ictus'), []);
});

test('small jitter around a held position never fires', () => {
  let s = 7;
  const rnd = () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296);
  const ev = run(() => 0.5 + (rnd() - 0.5) * 0.006, 4);
  assert.deepEqual(ev, []);
});

test('a downstroke with no lift before it is not a swing', () => {
  const ev = run((t) => (t < 1 ? 0.3 : t < 1.12 ? 0.3 + 0.3 * ((t - 1) / 0.12) ** 2 : 0.6), 1.5);
  assert.deepEqual(ev.filter((e) => e.type === 'ictus'), []);
});

test('two conducted strokes give two ictuses', () => {
  const a = conduct();
  const b = conduct({ strikeAt: 1.0 });
  const ev = run((t) => (t < 1.5 ? a(t) : b(t - 1.5)), 3.2);
  assert.equal(ev.filter((e) => e.type === 'ictus').length, 2);
});

test('reset() drops a half-finished gesture', () => {
  const d = createIctusDetector();
  const y = conduct();
  for (let i = 0; i < 0.9 * FPS; i++) d.feed(i / FPS, y(i / FPS));
  d.reset();
  const ev = [];
  for (let i = Math.round(0.9 * FPS); i < 1.6 * FPS; i++) { const e = d.feed(i / FPS, y(i / FPS)); if (e) ev.push(e); }
  assert.deepEqual(ev.filter((e) => e.type === 'ictus'), []);
});
