/**
 * Ictus detector tests. `node --test web/tests/`
 *
 * This is a straight port of `src/ictus_detector.py`'s state machine (see
 * ADR-0001/0002 and CONTEXT.md's `Ictus` entry) — Baton Brawl's whole reason
 * to exist is detecting the post-peak deceleration, not the swing peak, so a
 * port that silently regresses to peak-detection would still "pass" a test
 * that only counts events. These tests pin the exact timestamps the Python
 * reference emits for the same synthetic trace, not just the event count.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import { IctusDetector, detectIctuses } from '../src/games/batonBrawl/ictusDetector.js';
import { makeRng } from '../src/core/util.js';

/** Mirrors tests/test_ictus_detector_smoke.py's synthetic_swing_trace(). */
function syntheticSwingTrace({
  numSwings, sampleRateHz, swingGapS, peakMag, riseS, fallS, baseline = 20.0,
}) {
  const dt = 1.0 / sampleRateHz;
  const samples = [];
  let t = 0.0;
  for (let i = 0; i < numSwings; i++) {
    const idleEnd = t + swingGapS;
    while (t < idleEnd) { samples.push([t, baseline]); t += dt; }
    const riseStart = t;
    const riseEnd = t + riseS;
    while (t < riseEnd) {
      const frac = (t - riseStart) / riseS;
      samples.push([t, baseline + frac * (peakMag - baseline)]);
      t += dt;
    }
    const fallStart = t;
    const fallEnd = t + fallS;
    while (t < fallEnd) {
      const frac = (t - fallStart) / fallS;
      samples.push([t, peakMag - frac * (peakMag - baseline)]);
      t += dt;
    }
  }
  const paddingEnd = t + 0.3;
  while (t < paddingEnd) { samples.push([t, baseline]); t += dt; }
  return samples;
}

/** Mirrors tests/test_ictus_intensity_invariance.py's build_single_swing(). */
function buildSingleSwing({
  sampleRateHz, peakMag, riseS, fallS, baseline = 20.0,
  preIdleS = 0.3, postIdleS = 0.3, dtJitterFrac = 0.2, seed = 1234,
}) {
  const rng = makeRng(seed);
  const dt = 1.0 / sampleRateHz;
  const nextDt = () => dt * (1.0 + rng.range(-dtJitterFrac, dtJitterFrac));

  const samples = [];
  let t = 0.0;
  while (t < preIdleS) { samples.push([t, baseline]); t += nextDt(); }
  const riseStart = t;
  while (t < riseStart + riseS) {
    const frac = (t - riseStart) / riseS;
    samples.push([t, baseline + frac * (peakMag - baseline)]);
    t += nextDt();
  }
  const fallStart = t;
  while (t < fallStart + fallS) {
    const frac = (t - fallStart) / fallS;
    samples.push([t, peakMag - frac * (peakMag - baseline)]);
    t += nextDt();
  }
  const postIdleEnd = t + postIdleS;
  while (t < postIdleEnd) { samples.push([t, baseline]); t += nextDt(); }
  return { samples, riseStart };
}

// Reference values captured by running tests/test_ictus_detector_smoke.py's
// generator through the real Python IctusDetector (same trace params below).
const PYTHON_REFERENCE_TIMESTAMPS = [
  1.1600000000000008, 2.299999999999999, 3.4333333333333584,
  4.566666666666718, 5.7000000000000774, 6.833333333333437,
  7.9666666666667965, 9.11333333333334, 10.25999999999988,
  11.406666666666421,
];

test('smoke: one ictus per synthetic swing, at the exact post-drop instant the Python reference finds', () => {
  const trace = syntheticSwingTrace({
    numSwings: 10, sampleRateHz: 150.0, swingGapS: 1.0,
    peakMag: 800.0, riseS: 0.07, fallS: 0.06,
  });
  const events = detectIctuses(trace);

  assert.equal(events.length, 10, 'no false positives from baseline noise, no swing missed');
  events.forEach((e, i) => {
    const diff = Math.abs(e.timestamp - PYTHON_REFERENCE_TIMESTAMPS[i]);
    assert.ok(diff < 1e-6, `event ${i}: timestamp ${e.timestamp} diverges from Python reference ${PYTHON_REFERENCE_TIMESTAMPS[i]} by ${diff}`);
    assert.ok(Math.abs(e.peakMagnitude - 741.1904761904727) < 0.01, `event ${i}: peak magnitude ${e.peakMagnitude}`);
    assert.ok(Math.abs(e.riseDuration - 0.06) < 0.001, `event ${i}: rise duration ${e.riseDuration}`);
  });
});

test('intensity invariance: detection timing is stable across peak magnitudes', () => {
  const sampleRate = 150.0;
  const riseS = 0.03;
  const fallS = 0.06;
  const intensities = { soft: 300.0, medium: 550.0, hard: 800.0 };

  const offsets = {};
  let idx = 0;
  for (const [label, peak] of Object.entries(intensities)) {
    const { samples, riseStart } = buildSingleSwing({
      sampleRateHz: sampleRate, peakMag: peak, riseS, fallS, seed: 1234 + idx,
    });
    idx++;

    const detector = new IctusDetector();
    const events = [];
    for (const [t, mag] of samples) {
      const ev = detector.processSample(t, mag);
      if (ev) events.push(ev);
    }
    assert.equal(events.length, 1, `${label}: expected exactly 1 event, got ${events.length}`);
    offsets[label] = events[0].timestamp - riseStart;
  }

  const values = Object.values(offsets);
  const spreadMs = (Math.max(...values) - Math.min(...values)) * 1000;
  assert.ok(spreadMs < 15.0, `detected timestamp shifts ${spreadMs.toFixed(2)}ms with intensity — exactly the coupling this test guards against`);
});

test('no DOM, WebHID, or rendering globals are touched by import or use', () => {
  const detector = new IctusDetector();
  assert.equal(detector.processSample(0, 20), null);
  assert.equal(typeof window, 'undefined');
});
