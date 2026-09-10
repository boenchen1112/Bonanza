/**
 * Audio check tests. The harness uses `checkAudio` to decide whether a run's
 * captured mix is (a) actually audible and (b) landing on the game's beat
 * grid — the two things nobody has ever been able to verify, because nobody
 * has ever heard this game. These pin the checker itself against signals
 * whose right answer is known by construction.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import { detectOnsets, gridAlignment, checkAudio, encodeWav } from '../../tools/harness/audiocheck.mjs';

const SR = 44100;
const BPM = 120;
const SPB = 60 / BPM;

/** Seeded LCG so "random" fixtures are reproducible. */
function rng(seed) {
  let s = seed >>> 0;
  return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296);
}

/** Percussive hits: a short noise burst with a fast exponential decay. */
function hits(times, seconds, { gain = 0.6, bed = 0 } = {}) {
  const x = new Float32Array(Math.ceil(seconds * SR));
  const r = rng(7);
  // A quiet sustained pad under everything: onset detection must key on
  // transients, not on the mere presence of sound.
  if (bed) for (let i = 0; i < x.length; i++) x[i] = bed * Math.sin((2 * Math.PI * 220 * i) / SR);
  for (const t of times) {
    const i0 = Math.round(t * SR);
    for (let k = 0; k < SR * 0.12 && i0 + k < x.length; k++) {
      x[i0 + k] += gain * (r() * 2 - 1) * Math.exp(-k / (SR * 0.018));
    }
  }
  return x;
}

const beatTimes = (t0, n) => Array.from({ length: n }, (_, i) => t0 + i * SPB);

test('silence is reported silent and fails', () => {
  const r = checkAudio({ samples: new Float32Array(SR * 4), sampleRate: SR, startTime: 0, beatTimes: beatTimes(0, 8) });
  assert.equal(r.silent, true);
  assert.equal(r.pass, false);
});

test('onsets are found within 3ms of where hits were placed', () => {
  const times = [0.5, 0.75, 1.1, 1.62, 2.0, 2.4];
  const x = hits(times, 3, { bed: 0.05 });
  const on = detectOnsets(x, SR);
  assert.equal(on.length, times.length, `found ${on.map((o) => o.toFixed(3))}`);
  on.forEach((t, i) => assert.ok(Math.abs(t - times[i]) < 0.003, `onset ${i}: ${t} vs ${times[i]}`));
});

test('hits on the 16th grid pass with ~zero bias', () => {
  const t0 = 0.3;
  const times = [];
  for (let k = 0; k < 40; k++) if (k % 3 !== 1) times.push(t0 + (k * SPB) / 4);
  const x = hits(times, 6, { bed: 0.04 });
  const r = checkAudio({ samples: x, sampleRate: SR, startTime: 0, beatTimes: beatTimes(t0, 13) });
  assert.equal(r.silent, false);
  assert.equal(r.pass, true, JSON.stringify(r));
  assert.ok(Math.abs(r.biasMs) < 3, `bias ${r.biasMs}`);
});

test('startTime maps sample 0 onto the context clock', () => {
  const t0 = 10.3; // recording began at context time 10.0
  const times = Array.from({ length: 16 }, (_, k) => t0 + (k * SPB) / 2);
  const x = hits(times.map((t) => t - 10), 5);
  const r = checkAudio({ samples: x, sampleRate: SR, startTime: 10, beatTimes: beatTimes(t0, 10) });
  assert.equal(r.pass, true, JSON.stringify(r));
});

test('randomly timed hits fail: density alone cannot fake alignment', () => {
  const r0 = rng(99);
  const times = Array.from({ length: 48 }, () => 0.2 + r0() * 5.6).sort((a, b) => a - b)
    .filter((t, i, a) => i === 0 || t - a[i - 1] > 0.06);
  const x = hits(times, 6);
  const r = checkAudio({ samples: x, sampleRate: SR, startTime: 0, beatTimes: beatTimes(0.2, 13) });
  assert.equal(r.pass, false, JSON.stringify(r));
});

test('a grid-perfect track that sounds 60ms late fails and reports the lag', () => {
  const t0 = 0.3;
  const times = Array.from({ length: 20 }, (_, k) => t0 + (k * SPB) / 2 + 0.06);
  const x = hits(times, 6);
  const r = checkAudio({ samples: x, sampleRate: SR, startTime: 0, beatTimes: beatTimes(t0, 13) });
  assert.equal(r.pass, false, JSON.stringify(r));
  // Measured on the track's own (8th) grid, every onset is 60ms late.
  const s2 = gridAlignment(detectOnsets(x, SR), beatTimes(t0, 13), 2);
  assert.ok(Math.abs(s2.biasMs - 60) < 4, `bias ${s2.biasMs}`);
});

test('encodeWav writes a valid 16-bit PCM RIFF header', () => {
  const L = new Float32Array([0, 0.5, -0.5, 1]);
  const R = new Float32Array([0, -1, 1, 0]);
  const buf = encodeWav([L, R], 48000);
  const v = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
  const str = (o, n) => String.fromCharCode(...buf.subarray(o, o + n));
  assert.equal(str(0, 4), 'RIFF');
  assert.equal(str(8, 4), 'WAVE');
  assert.equal(v.getUint16(22, true), 2);       // channels
  assert.equal(v.getUint32(24, true), 48000);   // sample rate
  assert.equal(v.getUint16(34, true), 16);      // bits
  assert.equal(v.getUint32(40, true), 4 * 2 * 2);
  assert.equal(buf.byteLength, 44 + 16);
  assert.equal(v.getInt16(44 + 4 * 3, true), 32767); // L[3] = 1.0, interleaved
});
